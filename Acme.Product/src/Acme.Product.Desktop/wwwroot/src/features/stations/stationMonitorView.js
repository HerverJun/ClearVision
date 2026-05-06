import httpClient from '../../core/messaging/httpClient.js';
import { getStoredToken } from '../auth/authStorage.js';
import { buildSseHeaders, parseSseFrame } from '../inspection/inspectionSseClient.mjs';

class StationMonitorView {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.summary = null;
        this.stations = new Map();
        this.globalResults = [];
        this.globalLogs = [];
        this.packages = [];
        this.selectedStationId = null;
        this.selectedStationDetail = null;
        this.commandBusy = false;
        this.commandStatusMessage = '';
        this.commandStatusLevel = 'idle';
        this.offlineThresholdSeconds = 15;
        this.refreshTimer = null;
        this.eventSource = null;
        this.lastSseEventId = null;
        this.sseConnectionId = 0;
        this.sseReconnectAttempt = 0;
        this.sseReconnectBaseDelayMs = 1000;
        this.sseReconnectMaxDelayMs = 10000;
        this.renderShell();
        this.bindEvents();
    }

    async activate() {
        await this.loadInitialData();
        this.connectSse();
        this.startRefreshTimer();
        this.render();
    }

    dispose() {
        if (this.refreshTimer) {
            clearInterval(this.refreshTimer);
            this.refreshTimer = null;
        }

        if (this.eventSource) {
            this.eventSource.close();
            this.eventSource = null;
        }
    }

    renderShell() {
        if (!this.container) {
            return;
        }

        this.container.innerHTML = `
            <div class="station-monitor-view">
                <header class="sm-header">
                    <div class="sm-brand">
                        <span class="sm-kicker">工作站监控</span>
                        <h2 class="sm-title">中央监控台</h2>
                    </div>
                    <div class="sm-status" id="sm-status" data-state="connecting">
                        <span class="sm-pulse"></span>
                        <span id="sm-status-text">连接中...</span>
                    </div>
                </header>

                <section class="sm-kpis" id="sm-kpis"></section>

                <section class="sm-workspace">
                    <div class="sm-stage">
                        <div class="sm-surface">
                            <div class="sm-surface-header">
                                <span>工作站集群</span>
                                <span class="sm-meta" id="sm-fleet-meta">0 个在线</span>
                            </div>
                            <div class="sm-fleet" id="sm-fleet"></div>
                        </div>
                    </div>

                    <aside class="sm-rail">
                        <div class="sm-surface sm-detail">
                            <div class="sm-surface-header">
                                <span>详情检查器</span>
                                <span class="sm-meta" id="sm-detail-meta">未选择</span>
                            </div>
                            <div class="sm-detail-body" id="sm-detail"></div>
                        </div>

                        <div class="sm-surface sm-feed">
                            <div class="sm-surface-header">
                                <span>实时数据流</span>
                                <span class="sm-meta" id="sm-feed-meta">0 个事件</span>
                            </div>
                            <div class="sm-feed-body" id="sm-feed"></div>
                        </div>
                    </aside>
                </section>
            </div>
        `;

        this.summaryGrid = this.container.querySelector('#sm-kpis');
        this.matrix = this.container.querySelector('#sm-fleet');
        this.matrixMeta = this.container.querySelector('#sm-fleet-meta');
        this.focus = this.container.querySelector('#sm-detail');
        this.focusMeta = this.container.querySelector('#sm-detail-meta');
        this.stream = this.container.querySelector('#sm-feed');
        this.streamMeta = this.container.querySelector('#sm-feed-meta');
        this.syncText = this.container.querySelector('#sm-status-text');
        this.syncElement = this.container.querySelector('#sm-status');
    }

    bindEvents() {
        this.container?.addEventListener('click', async (event) => {
            const action = event.target.closest('[data-station-action]');
            if (action) {
                event.preventDefault();
                event.stopPropagation();
                await this.handleStationAction(action.dataset.stationAction);
                return;
            }

            const card = event.target.closest('[data-station-id]');
            if (!card) {
                return;
            }

            const stationId = card.dataset.stationId;
            if (stationId) {
                void this.selectStation(stationId);
            }
        });
    }

    async loadInitialData() {
        try {
            const [summary, stations, packages] = await Promise.all([
                httpClient.get('/stations/summary'),
                httpClient.get('/stations'),
                httpClient.get('/station-packages').catch(() => [])
            ]);

            this.summary = this.normalizeSummary(summary);
            this.packages = Array.isArray(packages) ? packages : [];
            this.offlineThresholdSeconds = this.summary?.offlineThresholdSeconds || this.offlineThresholdSeconds;
            this.stations.clear();

            (Array.isArray(stations) ? stations : []).forEach((station) => {
                const normalized = this.normalizeStation(station);
                this.stations.set(normalized.stationId, normalized);
            });

            if (!this.selectedStationId && this.stations.size > 0) {
                this.selectedStationId = this.getSortedStations()[0]?.stationId || null;
            }

            if (this.selectedStationId) {
                await this.loadStationDetail(this.selectedStationId);
            }

            this.updateSyncState('实时', 'live');
        } catch (error) {
            console.error('[StationMonitorView] Failed to load initial data:', error);
            this.updateSyncState('重连中', 'retrying');
        }
    }

    async selectStation(stationId) {
        this.selectedStationId = stationId;
        await this.loadStationDetail(stationId);
        this.render();
    }

    async loadStationDetail(stationId) {
        if (!stationId) {
            this.selectedStationDetail = null;
            return;
        }

        try {
            const detail = await httpClient.get(`/stations/${encodeURIComponent(stationId)}`);
            this.selectedStationDetail = this.normalizeDetail(detail);
            this.stations.set(this.selectedStationDetail.stationId, this.selectedStationDetail);
        } catch (error) {
            console.error('[StationMonitorView] Failed to load station detail:', error);
        }
    }

    connectSse() {
        if (this.eventSource) {
            return;
        }

        const eventUrl = httpClient.buildRequestUrl('/stations/events');
        const token = getStoredToken();
        const controller = new AbortController();
        const connectionId = ++this.sseConnectionId;

        this.eventSource = {
            connectionId,
            close: () => controller.abort()
        };
        this.runSseStream(eventUrl, token, controller.signal, connectionId);
    }

    async runSseStream(eventUrl, token, signal, connectionId) {
        while (!signal.aborted && this.isActiveConnection(connectionId)) {
            try {
                await this.openSseStream(eventUrl, token, signal);
                if (signal.aborted || !this.isActiveConnection(connectionId)) {
                    return;
                }
            } catch (error) {
                if (error?.name === 'AbortError' || signal.aborted || !this.isActiveConnection(connectionId)) {
                    return;
                }

                console.error('[StationMonitorView] SSE stream failed:', error);
            }

            this.updateSyncState('重连中', 'retrying');
            this.sseReconnectAttempt += 1;

            try {
                await this.waitForReconnect(signal, connectionId);
            } catch (error) {
                if (error?.name !== 'AbortError') {
                    console.error('[StationMonitorView] SSE reconnect wait failed:', error);
                }
                return;
            }
        }
    }

    async openSseStream(eventUrl, token, signal) {
        const headers = buildSseHeaders(token, this.lastSseEventId);
        const response = await fetch(eventUrl, {
            method: 'GET',
            headers,
            signal
        });

        if (!response.ok || !response.body) {
            throw new Error(`Station SSE connection failed: HTTP ${response.status}`);
        }

        this.updateSyncState('流式接收', 'streaming');
        this.sseReconnectAttempt = 0;

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const { value, done } = await reader.read();
            if (done) {
                break;
            }

            buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');
            let separatorIndex = buffer.indexOf('\n\n');
            while (separatorIndex >= 0) {
                const frame = buffer.slice(0, separatorIndex);
                buffer = buffer.slice(separatorIndex + 2);
                this.dispatchSseFrame(frame);
                separatorIndex = buffer.indexOf('\n\n');
            }
        }
    }

    dispatchSseFrame(frame) {
        const parsed = parseSseFrame(frame);
        if (!parsed) {
            return;
        }

        const { eventName, eventId, payload } = parsed;
        if (eventId) {
            this.lastSseEventId = eventId;
        }

        switch (eventName) {
            case 'initialState':
                this.applyInitialSnapshot(payload);
                break;
            case 'stationUpserted':
                this.upsertStation(payload);
                break;
            case 'stationResultAdded':
                this.applyResultEvent(payload);
                break;
            case 'stationHealthUpdated':
                this.applyHealthEvent(payload);
                break;
            case 'stationLogAdded':
                this.applyLogEvent(payload);
                break;
            case 'stationCommandUpdated':
                this.applyCommandEvent(payload);
                break;
            case 'summaryUpdated':
                this.summary = this.normalizeSummary(payload);
                this.offlineThresholdSeconds = this.summary?.offlineThresholdSeconds || this.offlineThresholdSeconds;
                break;
            case 'heartbeat':
                break;
            default:
                console.debug('[StationMonitorView] Unhandled SSE event:', eventName);
                break;
        }

        this.render();
    }

    applyInitialSnapshot(payload) {
        const stations = Array.isArray(payload?.stations) ? payload.stations : [];
        const recentResults = Array.isArray(payload?.recentResults) ? payload.recentResults : [];
        this.summary = this.normalizeSummary(payload?.summary);
        this.offlineThresholdSeconds = this.summary?.offlineThresholdSeconds || this.offlineThresholdSeconds;
        this.stations.clear();

        stations.forEach((station) => {
            const normalized = this.normalizeStation(station);
            this.stations.set(normalized.stationId, normalized);
        });

        this.globalResults = recentResults.map((item) => ({
            stationId: item?.stationId ?? item?.StationId ?? '--',
            result: this.normalizeResult(item?.result ?? item?.Result)
        }));

        if (!this.selectedStationId && this.stations.size > 0) {
            this.selectedStationId = this.getSortedStations()[0]?.stationId || null;
        }
    }

    upsertStation(station) {
        const normalized = this.normalizeStation(station);
        this.stations.set(normalized.stationId, normalized);

        if (this.selectedStationDetail?.stationId === normalized.stationId) {
            this.selectedStationDetail = {
                ...this.selectedStationDetail,
                ...normalized
            };
        }
    }

    applyResultEvent(payload) {
        const station = this.normalizeStation(payload?.station);
        const result = this.normalizeResult(payload?.result);

        this.stations.set(station.stationId, station);
        this.globalResults.unshift({
            stationId: station.stationId,
            stationLabel: station.lineName || station.stationId,
            result
        });
        this.globalResults = this.globalResults.slice(0, 40);

        if (this.selectedStationId === station.stationId) {
            if (!this.selectedStationDetail) {
                this.selectedStationDetail = {
                    ...station,
                    recentResults: []
                };
            }

            const recentResults = Array.isArray(this.selectedStationDetail.recentResults)
                ? [...this.selectedStationDetail.recentResults]
                : [];
            recentResults.unshift(result);
            this.selectedStationDetail = {
                ...this.selectedStationDetail,
                ...station,
                recentResults: recentResults.slice(0, 25)
            };
        }
    }

    applyHealthEvent(payload) {
        const station = this.normalizeStation(payload?.station);
        const health = this.normalizeHealth(payload?.health);
        if (!station.stationId || station.stationId === '--') {
            return;
        }

        this.stations.set(station.stationId, station);
        if (this.selectedStationId === station.stationId) {
            const detail = this.selectedStationDetail || { ...station, recentHealth: [] };
            const recentHealth = Array.isArray(detail.recentHealth) ? [...detail.recentHealth] : [];
            if (health.sequenceId > 0) {
                recentHealth.unshift(health);
            }

            this.selectedStationDetail = {
                ...detail,
                ...station,
                recentHealth: recentHealth.slice(0, 20)
            };
        }
    }

    applyLogEvent(payload) {
        const station = this.normalizeStation(payload?.station);
        const log = this.normalizeLog(payload?.log);
        const stationId = station.stationId && station.stationId !== '--' ? station.stationId : log.stationId;
        if (!stationId) {
            return;
        }

        if (station.stationId && station.stationId !== '--') {
            this.stations.set(station.stationId, station);
        }

        this.globalLogs.unshift({ stationId, log });
        this.globalLogs = this.globalLogs.slice(0, 40);

        if (this.selectedStationId === stationId) {
            const detail = this.selectedStationDetail || { ...station, recentLogs: [] };
            const recentLogs = Array.isArray(detail.recentLogs) ? [...detail.recentLogs] : [];
            recentLogs.unshift(log);
            this.selectedStationDetail = {
                ...detail,
                ...station,
                stationId,
                recentLogs: recentLogs.slice(0, 30)
            };
        }
    }

    applyCommandEvent(payload) {
        const command = this.normalizeCommand(payload);
        if (!command.stationId) {
            return;
        }

        if (this.selectedStationId === command.stationId) {
            const detail = this.selectedStationDetail || {
                ...(this.stations.get(command.stationId) || { stationId: command.stationId }),
                recentCommands: []
            };
            const commands = Array.isArray(detail.recentCommands) ? [...detail.recentCommands] : [];
            const existingIndex = commands.findIndex((item) => item.commandId === command.commandId);
            if (existingIndex >= 0) {
                commands[existingIndex] = command;
            } else {
                commands.unshift(command);
            }

            this.selectedStationDetail = {
                ...detail,
                recentCommands: commands.slice(0, 30)
            };
        }
    }

    isActiveConnection(connectionId) {
        return this.eventSource?.connectionId === connectionId;
    }

    waitForReconnect(signal, connectionId) {
        if (signal.aborted || !this.isActiveConnection(connectionId)) {
            return Promise.reject(this.createAbortError());
        }

        const attempt = Math.max(0, this.sseReconnectAttempt - 1);
        const delayMs = Math.min(
            this.sseReconnectMaxDelayMs,
            this.sseReconnectBaseDelayMs * (2 ** Math.min(attempt, 4))
        );

        return new Promise((resolve, reject) => {
            const timer = window.setTimeout(() => {
                signal.removeEventListener('abort', onAbort);
                if (this.isActiveConnection(connectionId)) {
                    resolve();
                } else {
                    reject(this.createAbortError());
                }
            }, delayMs);

            const onAbort = () => {
                clearTimeout(timer);
                signal.removeEventListener('abort', onAbort);
                reject(this.createAbortError());
            };

            signal.addEventListener('abort', onAbort, { once: true });
        });
    }

    createAbortError() {
        const error = new Error('Station monitor SSE aborted');
        error.name = 'AbortError';
        return error;
    }

    async handleStationAction(action) {
        if (!this.selectedStationId || this.commandBusy) {
            return;
        }

        this.commandBusy = true;
        this.commandStatusMessage = this.getActionBusyMessage(action);
        this.commandStatusLevel = 'busy';
        this.render();
        try {
            switch (action) {
                case 'ping':
                    await this.createCommand('Ping', {});
                    break;
                case 'reload':
                    await this.createCommand('ReloadPackage', {});
                    break;
                case 'stop':
                    await this.createCommand('StopRuntime', {});
                    break;
                case 'deploy':
                    await this.deployLatestPackage();
                    break;
                case 'testDeploy':
                    await this.createAndDeployTestPackage();
                    break;
                default:
                    break;
            }

            await this.loadStationDetail(this.selectedStationId);
            this.commandStatusMessage = this.getActionSuccessMessage(action);
            this.commandStatusLevel = 'success';
        } catch (error) {
            console.error('[StationMonitorView] Station action failed:', error);
            this.commandStatusMessage = error?.message || 'Station action failed.';
            this.commandStatusLevel = 'error';
        } finally {
            this.commandBusy = false;
            this.render();
        }
    }

    async createCommand(commandType, payload) {
        return httpClient.post(`/stations/${encodeURIComponent(this.selectedStationId)}/commands`, {
            commandType,
            payloadJson: JSON.stringify(payload || {}),
            issuedBy: 'Studio'
        });
    }

    async deployLatestPackage() {
        const packageId = this.packages[0]?.packageId ?? this.packages[0]?.PackageId;
        if (!packageId) {
            return null;
        }

        return httpClient.post(`/stations/${encodeURIComponent(this.selectedStationId)}/deploy-package`, {
            packageId,
            issuedBy: 'Studio'
        });
    }

    getActionBusyMessage(action) {
        switch (action) {
            case 'testDeploy':
                return '正在生成测试包并创建下发命令...';
            case 'deploy':
                return '正在创建运行包下发命令...';
            case 'reload':
                return '正在下发重载命令...';
            case 'stop':
                return '正在下发停止命令...';
            case 'ping':
                return '正在下发 Ping 命令...';
            default:
                return '正在下发命令...';
        }
    }

    getActionSuccessMessage(action) {
        switch (action) {
            case 'testDeploy':
                return '测试包已生成，部署命令已下发；等待 Station 轮询执行。';
            case 'deploy':
                return '部署命令已下发；等待 Station 轮询执行。';
            case 'reload':
                return '重载命令已下发。';
            case 'stop':
                return '停止命令已下发。';
            case 'ping':
                return 'Ping 命令已下发。';
            default:
                return '命令已下发。';
        }
    }

    async createAndDeployTestPackage() {
        const createdPackage = await httpClient.post('/station-packages/test', {});
        const packageId = createdPackage?.packageId ?? createdPackage?.PackageId;
        if (!packageId) {
            throw new Error('Test package was created without a packageId.');
        }

        this.packages = await httpClient.get('/station-packages').catch(() => this.packages);
        return httpClient.post(`/stations/${encodeURIComponent(this.selectedStationId)}/deploy-package`, {
            packageId,
            issuedBy: 'Studio'
        });
    }

    startRefreshTimer() {
        if (this.refreshTimer) {
            return;
        }

        this.refreshTimer = window.setInterval(() => this.render(), 1000);
    }

    render() {
        this.renderSummary();
        this.renderMatrix();
        this.renderFocus();
        this.renderStream();
    }

    renderSummary() {
        if (!this.summaryGrid) {
            return;
        }

        const stations = this.getSortedStations();
        const onlineStations = stations.filter((station) => this.computeIsOnline(station));
        const alerts = stations.filter((station) => !this.computeIsOnline(station) || station.state === 'Faulted');
        const recentResults = stations.flatMap((station) => Array.isArray(station.recentResults) ? station.recentResults : []);
        const avgExecutionTime = recentResults.length > 0
            ? recentResults.reduce((sum, result) => sum + Number(result.executionTimeMs || 0), 0) / recentResults.length
            : stations.reduce((sum, station) => sum + Number(station.averageExecutionTimeMs || 0), 0) / Math.max(stations.length, 1);
        const totalOk = stations.reduce((sum, station) => sum + Number(station.sessionOkCount || 0), 0);
        const totalNg = stations.reduce((sum, station) => sum + Number(station.sessionNgCount || 0), 0);
        const totalError = stations.reduce((sum, station) => sum + Number(station.sessionErrorCount || 0), 0);

        const cards = [
            { label: '在线', value: `${onlineStations.length}/${stations.length || 0}`, meta: `${stations.length - onlineStations.length} 个离线`, tone: 'success' },
            { label: '告警', value: String(alerts.length), meta: '故障或超时', tone: alerts.length > 0 ? 'warning' : 'default' },
            { label: '产量统计', value: `${totalOk} / ${totalNg} / ${totalError}`, meta: '良品 / 不良 / 异常', tone: 'accent' },
            { label: '平均节拍', value: this.formatMilliseconds(avgExecutionTime), meta: `${this.offlineThresholdSeconds}秒后判定超时`, tone: 'info' }
        ];

        this.summaryGrid.innerHTML = cards.map((card) => `
            <article class="sm-kpi sm-kpi--${card.tone}">
                <div class="sm-kpi-inner">
                    <span class="sm-kpi-label">${card.label}</span>
                    <strong class="sm-kpi-value">${card.value}</strong>
                    <span class="sm-kpi-meta">${card.meta}</span>
                </div>
            </article>
        `).join('');
    }

    renderMatrix() {
        if (!this.matrix || !this.matrixMeta) {
            return;
        }

        const stations = this.getSortedStations();
        this.matrixMeta.textContent = `${stations.length} 个在线`;

        if (stations.length === 0) {
            this.matrix.innerHTML = `
                <div class="sm-empty is-center">
                    <strong>暂无工作站数据</strong>
                    <span>请在 Studio 中启用 Station 接入，并配置 Station 同步以启动闭环。</span>
                </div>
            `;
            return;
        }

        this.matrix.innerHTML = stations.map((station) => {
            const isOnline = this.computeIsOnline(station);
            const isSelected = station.stationId === this.selectedStationId;
            const outcomeLabel = this.formatOutcome(station.lastOutcome, station.lastInspectionStatus);
            const stateLabel = station.onlineState && station.onlineState !== 'Online'
                ? station.onlineState
                : this.formatState(station.state, isOnline);

            return `
                <button
                    type="button"
                    class="sm-station ${isSelected ? 'is-selected' : ''} ${isOnline ? 'is-online' : 'is-offline'}"
                    data-station-id="${this.escapeHtml(station.stationId)}"
                >
                    <div class="sm-station-main">
                        <div class="sm-station-head">
                            <span class="sm-station-name">${this.escapeHtml(station.stationName || station.stationId)}</span>
                            <span class="sm-station-badge">${stateLabel}</span>
                        </div>
                        <span class="sm-station-line">${this.escapeHtml(station.lineName || station.machineName || station.stationId)}</span>
                    </div>
                    <div class="sm-station-stats">
                        <span class="sm-stat"><b>${Number(station.sessionOkCount || 0)}</b> OK</span>
                        <span class="sm-stat"><b>${Number(station.sessionNgCount || 0)}</b> NG</span>
                        <span class="sm-stat"><b>${Number(station.sessionErrorCount || 0)}</b> ERR</span>
                    </div>
                    <div class="sm-station-foot">
                        <span>${outcomeLabel}</span>
                        <span>${this.formatRelativeTime(station.lastSeenAtUtc)}</span>
                    </div>
                </button>
            `;
        }).join('');
    }

    renderFocus() {
        if (!this.focus || !this.focusMeta) {
            return;
        }

        const selectedStation = this.selectedStationId
            ? this.stations.get(this.selectedStationId)
            : null;

        if (!selectedStation) {
            this.focusMeta.textContent = '未选择';
            this.focus.innerHTML = `
                <div class="sm-empty is-center">
                    <strong>请选择工作站</strong>
                    <span>选择左侧某个工作站卡片，查看其包信息、计数器和近期结果。</span>
                </div>
            `;
            return;
        }

        const detail = this.selectedStationDetail && this.selectedStationDetail.stationId === selectedStation.stationId
            ? this.selectedStationDetail
            : { ...selectedStation, recentResults: [], recentHealth: [], recentLogs: [], recentCommands: [] };
        const isOnline = this.computeIsOnline(selectedStation);
        const recentResults = Array.isArray(detail.recentResults) ? detail.recentResults : [];
        const recentHealth = Array.isArray(detail.recentHealth) ? detail.recentHealth : [];
        const recentLogs = Array.isArray(detail.recentLogs) ? detail.recentLogs : [];
        const recentCommands = Array.isArray(detail.recentCommands) ? detail.recentCommands : [];
        const latestHealth = recentHealth[0] || detail;
        const actionsDisabled = this.commandBusy ? 'disabled' : '';
        const deployDisabled = this.commandBusy || this.packages.length === 0 ? 'disabled' : '';
        const testDeployDisabled = this.commandBusy ? 'disabled' : '';

        this.focusMeta.textContent = isOnline ? '实时详情' : '已超时';
        this.focus.innerHTML = `
            <div class="sm-detail-header ${isOnline ? 'is-online' : 'is-offline'}">
                <div>
                    <span class="sm-detail-id">${this.escapeHtml(detail.stationId)}</span>
                    <h3>${this.escapeHtml(detail.stationName || detail.lineName || detail.machineName || '未命名工作站')}</h3>
                    <p>${this.escapeHtml([detail.lineName, detail.areaName, detail.workcellName].filter(Boolean).join(' / ') || detail.packageName || '未加载包')}</p>
                </div>
                <span class="sm-detail-badge">${this.formatState(detail.state, isOnline)}</span>
            </div>
            <div class="sm-detail-actions">
                <button type="button" data-station-action="ping" ${actionsDisabled}>Ping</button>
                <button type="button" data-station-action="reload" ${actionsDisabled}>重载</button>
                <button type="button" data-station-action="stop" ${actionsDisabled}>停止</button>
                <button type="button" data-station-action="deploy" ${deployDisabled}>部署</button>
                <button type="button" class="sm-action-wide" data-station-action="testDeploy" ${testDeployDisabled}>生成测试包并下发</button>
            </div>
            ${this.commandStatusMessage
                ? `<div class="sm-command-status" data-level="${this.escapeHtml(this.commandStatusLevel)}">${this.escapeHtml(this.commandStatusMessage)}</div>`
                : ''}
            <div class="sm-detail-stats">
                <div><span>良品</span><b>${Number(detail.sessionOkCount || 0)}</b></div>
                <div><span>不良</span><b>${Number(detail.sessionNgCount || 0)}</b></div>
                <div><span>异常</span><b>${Number(detail.sessionErrorCount || 0)}</b></div>
                <div><span>平均</span><b>${this.formatMilliseconds(detail.averageExecutionTimeMs)}</b></div>
            </div>
            <dl class="sm-detail-meta">
                <div><dt>上次在线</dt><dd>${this.escapeHtml(this.formatRelativeTime(detail.lastSeenAtUtc))}</dd></div>
                <div><dt>最近结果</dt><dd>${this.escapeHtml(this.formatOutcome(detail.lastOutcome, detail.lastInspectionStatus))}</dd></div>
                <div><dt>诊断码</dt><dd>${this.escapeHtml(detail.lastDiagnosticCode || detail.lastDiagnosticMessage || '--')}</dd></div>
                <div><dt>包版本</dt><dd>${this.escapeHtml(detail.packageId || '--')}</dd></div>
                <div><dt>缓存队列</dt><dd>${Number(detail.spoolPendingCount || latestHealth.spoolPendingCount || 0)} / ${this.formatBytes(detail.spoolBytes || latestHealth.spoolBytes || 0)}</dd></div>
                <div><dt>磁盘剩余</dt><dd>${this.formatDisk(latestHealth.diskFreeMb, latestHealth.diskTotalMb)}</dd></div>
                <div><dt>内存占用</dt><dd>${Number(latestHealth.workingSetMb || detail.workingSetMb || 0)} MB</dd></div>
                <div><dt>健康状态</dt><dd>${this.escapeHtml(detail.onlineState || latestHealth.currentPackageHealth || '--')}</dd></div>
            </dl>
            <div class="sm-section">
                <div class="sm-section-header">
                    <span>指令队列</span>
                    <span>${recentCommands.length}</span>
                </div>
                ${recentCommands.length === 0
                    ? '<div class="sm-empty compact"><span>暂无指令记录。</span></div>'
                    : recentCommands.slice(0, 6).map((command) => `
                        <article class="sm-row">
                            <div class="sm-row-main">
                                <span class="sm-row-label">${this.escapeHtml(command.commandType || '--')}</span>
                                <span class="sm-row-sublabel">${this.escapeHtml(command.commandId || '--')}</span>
                            </div>
                            <div class="sm-row-side">
                                <span class="sm-row-value">${this.escapeHtml(command.status || '--')} ${Number(command.progressPercent || 0)}%</span>
                                <span class="sm-row-time">${this.escapeHtml(command.resultMessage || command.errorCode || this.formatRelativeTime(command.createdAtUtc))}</span>
                            </div>
                        </article>
                    `).join('')}
            </div>
            <div class="sm-section">
                <div class="sm-section-header">
                    <span>健康采样</span>
                    <span>${recentHealth.length}</span>
                </div>
                ${recentHealth.length === 0
                    ? '<div class="sm-empty compact"><span>暂无健康采样。</span></div>'
                    : recentHealth.slice(0, 4).map((health) => `
                        <article class="sm-row">
                            <div class="sm-row-main">
                                <span class="sm-row-label">${this.escapeHtml(health.runtimeState || '--')}</span>
                                <span class="sm-row-sublabel">${this.escapeHtml(health.currentPackageHealth || '--')}</span>
                            </div>
                            <div class="sm-row-side">
                                <span class="sm-row-value">${this.formatDisk(health.diskFreeMb, health.diskTotalMb)}</span>
                                <span class="sm-row-time">${this.escapeHtml(this.formatRelativeTime(health.createdAtUtc))}</span>
                            </div>
                        </article>
                    `).join('')}
            </div>
            <div class="sm-section">
                <div class="sm-section-header">
                    <span>日志</span>
                    <span>${recentLogs.length}</span>
                </div>
                ${recentLogs.length === 0
                    ? '<div class="sm-empty compact"><span>暂无 WARN 或 ERROR 级别日志。</span></div>'
                    : recentLogs.slice(0, 5).map((log) => `
                        <article class="sm-row sm-row--${this.escapeHtml(String(log.level || '').toLowerCase())}">
                            <div class="sm-row-main">
                                <span class="sm-row-label">${this.escapeHtml(log.level || '--')}</span>
                                <span class="sm-row-sublabel">${this.escapeHtml(log.source || '--')}</span>
                            </div>
                            <span class="sm-row-value">${this.escapeHtml(log.renderedMessage || log.exceptionMessage || '--')}</span>
                        </article>
                    `).join('')}
            </div>
            <div class="sm-section">
                <div class="sm-section-header">
                    <span>近期结果</span>
                    <span>${recentResults.length}</span>
                </div>
                ${recentResults.length === 0
                    ? '<div class="sm-empty compact"><span>该工作站暂无缓存结果。</span></div>'
                    : recentResults.slice(0, 8).map((result) => `
                        <article class="sm-row">
                            <div class="sm-row-main">
                                <span class="sm-row-label">${this.escapeHtml(this.formatOutcome(result.outcome, result.inspectionStatus))}</span>
                                <span class="sm-row-sublabel">${this.escapeHtml(result.imageId || result.runId || '--')}</span>
                            </div>
                            <div class="sm-row-side">
                                <span class="sm-row-value">${this.formatMilliseconds(result.executionTimeMs)}</span>
                                <span class="sm-row-time">${this.escapeHtml(this.formatRelativeTime(result.completedAtUtc))}</span>
                            </div>
                        </article>
                    `).join('')}
            </div>
        `;
    }

    renderStream() {
        if (!this.stream || !this.streamMeta) {
            return;
        }

        const flow = [
            ...this.globalResults.map((item) => ({
                type: 'result',
                stationId: item.stationId,
                atUtc: item.result?.completedAtUtc,
                data: item.result
            })),
            ...this.globalLogs.map((item) => ({
                type: 'log',
                stationId: item.stationId,
                atUtc: item.log?.timestampUtc,
                data: item.log
            }))
        ].sort((left, right) => new Date(right.atUtc || 0).getTime() - new Date(left.atUtc || 0).getTime());

        this.streamMeta.textContent = `${flow.length} 条缓存`;
        if (flow.length === 0) {
            this.stream.innerHTML = `
                <div class="sm-empty is-center">
                    <strong>暂无事件</strong>
                    <span>工作站开始上报后，结果和日志数据流将显示在这里。</span>
                </div>
            `;
            return;
        }

        this.stream.innerHTML = flow.slice(0, 12).map((item) => {
            if (item.type === 'log') {
                return `
                    <article class="sm-feed-item sm-feed-item--log">
                        <div class="sm-feed-main">
                            <span class="sm-feed-station">${this.escapeHtml(item.stationId)}</span>
                            <strong>${this.escapeHtml(item.data.level || '--')}</strong>
                            <span>${this.escapeHtml(item.data.source || '--')}</span>
                        </div>
                        <div class="sm-feed-extra">
                            <span>${this.escapeHtml(item.data.renderedMessage || item.data.exceptionMessage || '--')}</span>
                            <span>${this.escapeHtml(this.formatRelativeTime(item.data.timestampUtc))}</span>
                        </div>
                    </article>
                `;
            }

            return `
                <article class="sm-feed-item">
                    <div class="sm-feed-main">
                        <span class="sm-feed-station">${this.escapeHtml(item.stationId)}</span>
                        <strong>${this.escapeHtml(this.formatOutcome(item.data.outcome, item.data.inspectionStatus))}</strong>
                        <span>${this.escapeHtml(item.data.imageId || item.data.runId || '--')}</span>
                    </div>
                    <div class="sm-feed-extra">
                        <span>${this.escapeHtml(item.data.diagnosticCode || '--')}</span>
                        <span>${this.formatMilliseconds(item.data.executionTimeMs)}</span>
                        <span>${this.escapeHtml(this.formatRelativeTime(item.data.completedAtUtc))}</span>
                    </div>
                </article>
            `;
        }).join('');
    }

    updateSyncState(displayText, stateKey) {
        if (this.syncText) {
            this.syncText.textContent = displayText;
        }

        if (this.syncElement) {
            this.syncElement.dataset.state = stateKey || String(displayText).toLowerCase();
        }
    }

    getSortedStations() {
        return [...this.stations.values()].sort((left, right) => {
            const leftOnline = this.computeIsOnline(left) ? 1 : 0;
            const rightOnline = this.computeIsOnline(right) ? 1 : 0;
            if (leftOnline !== rightOnline) {
                return rightOnline - leftOnline;
            }

            if (left.state !== right.state) {
                return String(left.state || '').localeCompare(String(right.state || ''));
            }

            return String(left.stationId || '').localeCompare(String(right.stationId || ''));
        });
    }

    computeIsOnline(station) {
        if (station?.isEnabled === false) {
            return false;
        }

        if (!station?.lastSeenAtUtc) {
            return Boolean(station?.isOnline);
        }

        const lastSeen = new Date(station.lastSeenAtUtc).getTime();
        if (!Number.isFinite(lastSeen)) {
            return Boolean(station?.isOnline);
        }

        return Date.now() - lastSeen <= this.offlineThresholdSeconds * 1000;
    }

    normalizeSummary(summary) {
        if (!summary || typeof summary !== 'object') {
            return null;
        }

        return {
            offlineThresholdSeconds: Number(summary.offlineThresholdSeconds ?? summary.OfflineThresholdSeconds ?? 15)
        };
    }

    normalizeStation(station) {
        const recentResults = station?.recentResults ?? station?.RecentResults;
        const recentHealth = station?.recentHealth ?? station?.RecentHealth;
        const recentLogs = station?.recentLogs ?? station?.RecentLogs;
        const recentCommands = station?.recentCommands ?? station?.RecentCommands;
        return {
            stationId: station?.stationId ?? station?.StationId ?? '--',
            stationName: station?.stationName ?? station?.StationName ?? '',
            lineName: station?.lineName ?? station?.LineName ?? null,
            areaName: station?.areaName ?? station?.AreaName ?? null,
            workcellName: station?.workcellName ?? station?.WorkcellName ?? null,
            inspectionNodeName: station?.inspectionNodeName ?? station?.InspectionNodeName ?? null,
            cameraAlias: station?.cameraAlias ?? station?.CameraAlias ?? null,
            stationRole: station?.stationRole ?? station?.StationRole ?? '',
            owner: station?.owner ?? station?.Owner ?? null,
            isEnabled: Boolean(station?.isEnabled ?? station?.IsEnabled ?? true),
            remark: station?.remark ?? station?.Remark ?? null,
            machineName: station?.machineName ?? station?.MachineName ?? '',
            clientVersion: station?.clientVersion ?? station?.ClientVersion ?? '',
            onlineState: station?.onlineState ?? station?.OnlineState ?? 'Unknown',
            runtimeState: station?.runtimeState ?? station?.RuntimeState ?? null,
            state: station?.state ?? station?.State ?? 'Idle',
            isOnline: Boolean(station?.isOnline ?? station?.IsOnline),
            startedAtUtc: station?.startedAtUtc ?? station?.StartedAtUtc ?? null,
            lastSeenAtUtc: station?.lastSeenAtUtc ?? station?.LastSeenAtUtc ?? null,
            packageId: station?.packageId ?? station?.PackageId ?? null,
            packageName: station?.packageName ?? station?.PackageName ?? null,
            flowHash: station?.flowHash ?? station?.FlowHash ?? null,
            currentRunId: station?.currentRunId ?? station?.CurrentRunId ?? null,
            sessionOkCount: Number(station?.sessionOkCount ?? station?.SessionOkCount ?? 0),
            sessionNgCount: Number(station?.sessionNgCount ?? station?.SessionNgCount ?? 0),
            sessionErrorCount: Number(station?.sessionErrorCount ?? station?.SessionErrorCount ?? 0),
            lastOutcome: station?.lastOutcome ?? station?.LastOutcome ?? null,
            lastInspectionStatus: station?.lastInspectionStatus ?? station?.LastInspectionStatus ?? null,
            lastDiagnosticCode: station?.lastDiagnosticCode ?? station?.LastDiagnosticCode ?? null,
            lastDiagnosticMessage: station?.lastDiagnosticMessage ?? station?.LastDiagnosticMessage ?? null,
            lastResultAtUtc: station?.lastResultAtUtc ?? station?.LastResultAtUtc ?? null,
            lastSequenceId: Number(station?.lastSequenceId ?? station?.LastSequenceId ?? 0),
            averageExecutionTimeMs: Number(station?.averageExecutionTimeMs ?? station?.AverageExecutionTimeMs ?? 0),
            recentResultCount: Number(station?.recentResultCount ?? station?.RecentResultCount ?? 0),
            spoolPendingCount: Number(station?.spoolPendingCount ?? station?.SpoolPendingCount ?? 0),
            spoolBytes: Number(station?.spoolBytes ?? station?.SpoolBytes ?? 0),
            cpuUsagePercent: station?.cpuUsagePercent ?? station?.CpuUsagePercent ?? null,
            workingSetMb: Number(station?.workingSetMb ?? station?.WorkingSetMb ?? 0),
            diskFreeMb: Number(station?.diskFreeMb ?? station?.DiskFreeMb ?? 0),
            diskTotalMb: Number(station?.diskTotalMb ?? station?.DiskTotalMb ?? 0),
            cameraStatusSummary: station?.cameraStatusSummary ?? station?.CameraStatusSummary ?? null,
            plcStatusSummary: station?.plcStatusSummary ?? station?.PlcStatusSummary ?? null,
            currentPackageHealth: station?.currentPackageHealth ?? station?.CurrentPackageHealth ?? null,
            recentResults: Array.isArray(recentResults)
                ? recentResults.map((result) => this.normalizeResult(result))
                : [],
            recentHealth: Array.isArray(recentHealth)
                ? recentHealth.map((health) => this.normalizeHealth(health))
                : [],
            recentLogs: Array.isArray(recentLogs)
                ? recentLogs.map((log) => this.normalizeLog(log))
                : [],
            recentCommands: Array.isArray(recentCommands)
                ? recentCommands.map((command) => this.normalizeCommand(command))
                : []
        };
    }

    normalizeDetail(detail) {
        return this.normalizeStation(detail);
    }

    normalizeResult(result) {
        return {
            stationId: result?.stationId ?? result?.StationId ?? null,
            sequenceId: Number(result?.sequenceId ?? result?.SequenceId ?? 0),
            runId: result?.runId ?? result?.RunId ?? '--',
            packageId: result?.packageId ?? result?.PackageId ?? null,
            packageName: result?.packageName ?? result?.PackageName ?? null,
            flowHash: result?.flowHash ?? result?.FlowHash ?? null,
            imageId: result?.imageId ?? result?.ImageId ?? '--',
            outcome: result?.outcome ?? result?.Outcome ?? 'Error',
            inspectionStatus: result?.inspectionStatus ?? result?.InspectionStatus ?? null,
            executionTimeMs: Number(result?.executionTimeMs ?? result?.ExecutionTimeMs ?? 0),
            diagnosticCode: result?.diagnosticCode ?? result?.DiagnosticCode ?? null,
            diagnosticMessage: result?.diagnosticMessage ?? result?.DiagnosticMessage ?? null,
            startedAtUtc: result?.startedAtUtc ?? result?.StartedAtUtc ?? null,
            completedAtUtc: result?.completedAtUtc ?? result?.CompletedAtUtc ?? null
        };
    }

    normalizeHealth(health) {
        return {
            stationId: health?.stationId ?? health?.StationId ?? null,
            sequenceId: Number(health?.sequenceId ?? health?.SequenceId ?? 0),
            runtimeState: health?.runtimeState ?? health?.RuntimeState ?? 'Unknown',
            workingSetMb: Number(health?.workingSetMb ?? health?.WorkingSetMb ?? 0),
            privateMemoryMb: Number(health?.privateMemoryMb ?? health?.PrivateMemoryMb ?? 0),
            diskFreeMb: Number(health?.diskFreeMb ?? health?.DiskFreeMb ?? 0),
            diskTotalMb: Number(health?.diskTotalMb ?? health?.DiskTotalMb ?? 0),
            spoolPendingCount: Number(health?.spoolPendingCount ?? health?.SpoolPendingCount ?? 0),
            spoolBytes: Number(health?.spoolBytes ?? health?.SpoolBytes ?? 0),
            cameraStatusSummary: health?.cameraStatusSummary ?? health?.CameraStatusSummary ?? null,
            plcStatusSummary: health?.plcStatusSummary ?? health?.PlcStatusSummary ?? null,
            currentPackageId: health?.currentPackageId ?? health?.CurrentPackageId ?? null,
            currentPackageHealth: health?.currentPackageHealth ?? health?.CurrentPackageHealth ?? null,
            lastErrorCode: health?.lastErrorCode ?? health?.LastErrorCode ?? null,
            lastErrorMessage: health?.lastErrorMessage ?? health?.LastErrorMessage ?? null,
            createdAtUtc: health?.createdAtUtc ?? health?.CreatedAtUtc ?? null
        };
    }

    normalizeLog(log) {
        return {
            stationId: log?.stationId ?? log?.StationId ?? null,
            sequenceId: Number(log?.sequenceId ?? log?.SequenceId ?? 0),
            timestampUtc: log?.timestampUtc ?? log?.TimestampUtc ?? null,
            level: log?.level ?? log?.Level ?? '',
            source: log?.source ?? log?.Source ?? '',
            renderedMessage: log?.renderedMessage ?? log?.RenderedMessage ?? '',
            exceptionType: log?.exceptionType ?? log?.ExceptionType ?? null,
            exceptionMessage: log?.exceptionMessage ?? log?.ExceptionMessage ?? null,
            runId: log?.runId ?? log?.RunId ?? null,
            packageId: log?.packageId ?? log?.PackageId ?? null,
            createdAtUtc: log?.createdAtUtc ?? log?.CreatedAtUtc ?? null
        };
    }

    normalizeCommand(command) {
        return {
            commandId: command?.commandId ?? command?.CommandId ?? '',
            stationId: command?.stationId ?? command?.StationId ?? this.selectedStationId,
            commandType: command?.commandType ?? command?.CommandType ?? '',
            status: command?.status ?? command?.Status ?? '',
            progressPercent: Number(command?.progressPercent ?? command?.ProgressPercent ?? 0),
            createdAtUtc: command?.createdAtUtc ?? command?.CreatedAtUtc ?? null,
            deliveredAtUtc: command?.deliveredAtUtc ?? command?.DeliveredAtUtc ?? null,
            acceptedAtUtc: command?.acceptedAtUtc ?? command?.AcceptedAtUtc ?? null,
            startedAtUtc: command?.startedAtUtc ?? command?.StartedAtUtc ?? null,
            completedAtUtc: command?.completedAtUtc ?? command?.CompletedAtUtc ?? null,
            resultMessage: command?.resultMessage ?? command?.ResultMessage ?? command?.message ?? command?.Message ?? null,
            errorCode: command?.errorCode ?? command?.ErrorCode ?? null
        };
    }

    formatMilliseconds(value) {
        const numeric = Number(value || 0);
        if (!Number.isFinite(numeric) || numeric <= 0) {
            return '--';
        }

        return `${Math.round(numeric)} ms`;
    }

    formatBytes(value) {
        const bytes = Number(value || 0);
        if (!Number.isFinite(bytes) || bytes <= 0) {
            return '0 B';
        }

        if (bytes < 1024) {
            return `${Math.round(bytes)} B`;
        }

        if (bytes < 1024 * 1024) {
            return `${Math.round(bytes / 1024)} KB`;
        }

        return `${Math.round(bytes / 1024 / 1024)} MB`;
    }

    formatDisk(freeMb, totalMb) {
        const free = Number(freeMb || 0);
        const total = Number(totalMb || 0);
        if (!Number.isFinite(free) || free <= 0 || !Number.isFinite(total) || total <= 0) {
            return '--';
        }

        return `${Math.round(free)} / ${Math.round(total)} MB`;
    }

    formatRelativeTime(value) {
        if (!value) {
            return '--';
        }

        const timestamp = new Date(value).getTime();
        if (!Number.isFinite(timestamp)) {
            return '--';
        }

        const deltaSeconds = Math.max(0, Math.floor((Date.now() - timestamp) / 1000));
        if (deltaSeconds < 5) {
            return '刚刚';
        }

        if (deltaSeconds < 60) {
            return `${deltaSeconds}秒前`;
        }

        const minutes = Math.floor(deltaSeconds / 60);
        if (minutes < 60) {
            return `${minutes}分钟前`;
        }

        const hours = Math.floor(minutes / 60);
        if (hours < 24) {
            return `${hours}小时前`;
        }

        const days = Math.floor(hours / 24);
        return `${days}天前`;
    }

    formatState(state, isOnline) {
        if (!isOnline) {
            return '离线';
        }

        const normalized = String(state || '').trim();
        switch (normalized) {
            case 'Running':
                return '运行中';
            case 'Stopping':
                return '停止中';
            case 'Faulted':
                return '故障';
            case 'Loaded':
                return '就绪';
            default:
                return normalized || '空闲';
        }
    }

    formatOutcome(outcome, inspectionStatus) {
        const normalizedOutcome = String(outcome || '').trim().toUpperCase();
        if (normalizedOutcome === 'OK') {
            return '良品';
        }

        if (normalizedOutcome === 'NG') {
            return '不良';
        }

        if (normalizedOutcome === 'ERROR') {
            return '异常';
        }

        if (normalizedOutcome === 'CANCELED') {
            return '已取消';
        }

        const normalizedStatus = String(inspectionStatus || '').trim().toUpperCase();
        if (normalizedStatus === 'OK' || normalizedStatus === 'NG' || normalizedStatus === 'ERROR') {
            return normalizedStatus === 'ERROR' ? '异常' : (normalizedStatus === 'OK' ? '良品' : '不良');
        }

        return '待定';
    }

    escapeHtml(value) {
        const div = document.createElement('div');
        div.textContent = String(value ?? '');
        return div.innerHTML;
    }
}

export { StationMonitorView };
