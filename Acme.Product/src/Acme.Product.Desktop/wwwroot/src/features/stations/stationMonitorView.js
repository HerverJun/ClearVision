import httpClient from '../../core/messaging/httpClient.js';
import { getStoredToken } from '../auth/authStorage.js';
import { buildSseHeaders, parseSseFrame } from '../inspection/inspectionSseClient.mjs';
import {
    buildResultCardsFromOutputData,
    renderResultCardHtml
} from '../results/portDataTypeRenderer.mjs';

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
        this.monitorResults = [];
        this.monitorTotalCount = 0;
        this.monitorPageIndex = 0;
        this.monitorPageSize = 12;
        this.resultFilters = {
            status: 'all',
            diagnosticCode: 'all'
        };
        this.resultLoading = false;
        this.resultLoadError = '';
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

    focusResultsWorkbench() {
        this.resultsWorkbench?.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
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
                    <div class="sm-header-actions">
                        <button type="button" class="sm-scope-btn is-active" data-monitor-scope="all">全站</button>
                        <button type="button" class="sm-scope-btn" data-monitor-refresh>刷新</button>
                        <div class="sm-status" id="sm-status" data-state="connecting">
                            <span class="sm-pulse"></span>
                            <span id="sm-status-text">连接中...</span>
                        </div>
                    </div>
                </header>

                <section class="sm-kpis" id="sm-kpis"></section>

                <section class="sm-workspace">
                    <div class="sm-stage">
                        <div class="sm-surface">
                            <div class="sm-surface-header">
                                <span>工作站集群</span>
                                <span class="sm-meta" id="sm-fleet-meta">0 个工作站</span>
                            </div>
                            <div class="sm-fleet" id="sm-fleet"></div>
                        </div>
                    </div>

                    <aside class="sm-rail">
                        <div class="sm-surface sm-detail">
                            <div class="sm-surface-header">
                                <span>详情检查器</span>
                                <span class="sm-meta" id="sm-detail-meta">全站范围</span>
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

                <section class="sm-results-workbench" id="sm-results-workbench">
                    <div class="sm-surface">
                        <div class="sm-surface-header sm-results-header">
                            <div>
                                <span id="sm-results-title">全站结果明细</span>
                                <small id="sm-results-subtitle">等待真实 Station 结果</small>
                            </div>
                            <span class="sm-meta" id="sm-results-meta">0 条记录</span>
                        </div>
                        <div class="sm-results-body">
                            <div class="sm-result-overview" id="sm-result-overview"></div>
                            <div class="sm-result-charts" id="sm-result-charts"></div>
                            <div class="sm-result-toolbar" id="sm-result-toolbar"></div>
                            <div class="sm-result-list" id="sm-result-list"></div>
                            <div class="sm-result-pagination" id="sm-result-pagination"></div>
                        </div>
                    </div>
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
        this.scopeAllButton = this.container.querySelector('[data-monitor-scope="all"]');
        this.resultsWorkbench = this.container.querySelector('#sm-results-workbench');
        this.resultsTitle = this.container.querySelector('#sm-results-title');
        this.resultsSubtitle = this.container.querySelector('#sm-results-subtitle');
        this.resultsMeta = this.container.querySelector('#sm-results-meta');
        this.resultOverview = this.container.querySelector('#sm-result-overview');
        this.resultCharts = this.container.querySelector('#sm-result-charts');
        this.resultToolbar = this.container.querySelector('#sm-result-toolbar');
        this.resultList = this.container.querySelector('#sm-result-list');
        this.resultPagination = this.container.querySelector('#sm-result-pagination');
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

            const exportAction = event.target.closest('[data-result-export]');
            if (exportAction) {
                event.preventDefault();
                this.exportMonitorResults(exportAction.dataset.resultExport);
                return;
            }

            const pageAction = event.target.closest('[data-result-page]');
            if (pageAction) {
                event.preventDefault();
                await this.loadResultsPage(Number(pageAction.dataset.resultPage || 0));
                return;
            }

            const refreshAction = event.target.closest('[data-monitor-refresh]');
            if (refreshAction) {
                event.preventDefault();
                await this.refreshCurrentScope();
                return;
            }

            const allScopeAction = event.target.closest('[data-monitor-scope="all"]');
            if (allScopeAction) {
                event.preventDefault();
                await this.selectAllStations();
                return;
            }

            const card = event.target.closest('[data-station-id]');
            if (!card) {
                return;
            }

            const stationId = card.dataset.stationId;
            if (stationId) {
                await this.selectStation(stationId);
            }
        });

        this.container?.addEventListener('change', async (event) => {
            if (event.target?.id === 'sm-result-status-filter') {
                this.resultFilters.status = event.target.value || 'all';
                await this.loadResultsPage(0);
                return;
            }

            if (event.target?.id === 'sm-result-diagnostic-filter') {
                this.resultFilters.diagnosticCode = event.target.value || 'all';
                await this.loadResultsPage(0);
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

            await this.loadResultsPage(0, { renderAfter: false });
            this.updateSyncState('实时', 'live');
        } catch (error) {
            console.error('[StationMonitorView] Failed to load initial data:', error);
            this.resultLoadError = error?.message || '加载监控数据失败';
            this.updateSyncState('重连中', 'retrying');
        }
    }

    async refreshCurrentScope() {
        if (this.selectedStationId) {
            await this.loadStationDetail(this.selectedStationId);
        }

        await this.loadResultsPage(this.monitorPageIndex);
    }

    async selectAllStations() {
        this.selectedStationId = null;
        this.selectedStationDetail = null;
        this.scopeAllButton?.classList.add('is-active');
        await this.loadResultsPage(0);
        this.render();
    }

    async selectStation(stationId) {
        this.selectedStationId = stationId;
        this.scopeAllButton?.classList.remove('is-active');
        await this.loadStationDetail(stationId);
        await this.loadResultsPage(0, { renderAfter: false });
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

    async loadResultsPage(pageIndex = 0, { renderAfter = true } = {}) {
        this.resultLoading = true;
        this.resultLoadError = '';
        this.monitorPageIndex = Math.max(0, Number(pageIndex) || 0);
        if (renderAfter) {
            this.renderResultsWorkbench();
        }

        try {
            const params = {
                pageIndex: this.monitorPageIndex,
                pageSize: this.monitorPageSize
            };

            if (this.selectedStationId) {
                params.stationId = this.selectedStationId;
            }

            if (this.resultFilters.status !== 'all') {
                params.status = this.resultFilters.status;
            }

            if (this.resultFilters.diagnosticCode !== 'all') {
                params.diagnosticCode = this.resultFilters.diagnosticCode;
            }

            const response = await httpClient.get('/stations/results', params);
            const page = this.normalizeResultsPage(response);
            this.monitorResults = page.items;
            this.monitorTotalCount = page.totalCount;
            this.monitorPageIndex = page.pageIndex;
            this.monitorPageSize = page.pageSize;
        } catch (error) {
            console.error('[StationMonitorView] Failed to load station results:', error);
            this.resultLoadError = error?.message || '结果查询失败';
        } finally {
            this.resultLoading = false;
            if (renderAfter) {
                this.render();
            }
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

        this.globalResults = recentResults
            .map((item) => this.normalizeMonitorResult(item?.result ?? item?.Result, item?.station ?? item?.Station))
            .filter(Boolean);

        if (this.monitorResults.length === 0 && this.globalResults.length > 0) {
            const scoped = this.globalResults
                .filter((record) => this.resultMatchesCurrentScope(record))
                .filter((record) => this.resultMatchesFilters(record));
            this.monitorResults = scoped.slice(0, this.monitorPageSize);
            this.monitorTotalCount = scoped.length;
        }
    }

    upsertStation(station) {
        const normalized = this.normalizeStation(station);
        if (!normalized.stationId || normalized.stationId === '--') {
            return;
        }

        this.stations.set(normalized.stationId, normalized);

        if (this.selectedStationDetail?.stationId === normalized.stationId) {
            this.selectedStationDetail = {
                ...this.selectedStationDetail,
                ...normalized
            };
        }
    }

    applyResultEvent(payload) {
        const payloadStation = this.normalizeStation(payload?.station);
        const result = this.normalizeResult(payload?.result);
        const stationId = payloadStation.stationId && payloadStation.stationId !== '--'
            ? payloadStation.stationId
            : result.stationId;
        const station = this.stations.get(stationId) || payloadStation;

        if (payloadStation.stationId && payloadStation.stationId !== '--') {
            this.stations.set(payloadStation.stationId, payloadStation);
        }

        const record = this.normalizeMonitorResult(result, station);
        if (!record) {
            return;
        }

        this.globalResults.unshift(record);
        this.globalResults = this.dedupeResultRecords(this.globalResults).slice(0, 80);

        if (this.monitorPageIndex === 0 &&
            this.resultMatchesCurrentScope(record) &&
            this.resultMatchesFilters(record)) {
            this.monitorResults = this.dedupeResultRecords([record, ...this.monitorResults]).slice(0, this.monitorPageSize);
            this.monitorTotalCount += 1;
        }

        if (this.selectedStationId === record.stationId) {
            const detail = this.selectedStationDetail || {
                ...(this.stations.get(record.stationId) || { stationId: record.stationId }),
                recentResults: []
            };
            const recentResults = Array.isArray(detail.recentResults) ? [...detail.recentResults] : [];
            recentResults.unshift(result);
            this.selectedStationDetail = {
                ...detail,
                ...station,
                recentResults: this.dedupeResults(recentResults).slice(0, 25)
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
        this.globalLogs = this.globalLogs.slice(0, 80);

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
        this.scopeAllButton?.classList.toggle('is-active', !this.selectedStationId);
        this.renderSummary();
        this.renderMatrix();
        this.renderFocus();
        this.renderStream();
        this.renderResultsWorkbench();
    }

    renderSummary() {
        if (!this.summaryGrid) {
            return;
        }

        const stations = this.getSortedStations();
        const onlineStations = stations.filter((station) => this.computeIsOnline(station));
        const alerts = stations.filter((station) => !this.computeIsOnline(station) || station.state === 'Faulted');
        const totalOk = this.summary?.totalOkCount ?? stations.reduce((sum, station) => sum + Number(station.sessionOkCount || 0), 0);
        const totalNg = this.summary?.totalNgCount ?? stations.reduce((sum, station) => sum + Number(station.sessionNgCount || 0), 0);
        const totalError = this.summary?.totalErrorCount ?? stations.reduce((sum, station) => sum + Number(station.sessionErrorCount || 0), 0);
        const avgExecutionTime = this.summary?.averageExecutionTimeMs ??
            stations.reduce((sum, station) => sum + Number(station.averageExecutionTimeMs || 0), 0) / Math.max(stations.length, 1);
        const totalInspections = totalOk + totalNg + totalError;
        const yieldRate = totalInspections === 0 ? '--' : `${Math.round((totalOk / totalInspections) * 1000) / 10}%`;

        const cards = [
            { label: '在线', value: `${onlineStations.length}/${stations.length || 0}`, meta: `${stations.length - onlineStations.length} 个离线`, tone: 'success' },
            { label: '告警', value: String(alerts.length), meta: '故障或超时', tone: alerts.length > 0 ? 'warning' : 'default' },
            { label: '良率', value: yieldRate, meta: `${totalOk} OK / ${totalNg} NG / ${totalError} ERR`, tone: 'accent' },
            { label: '平均节拍', value: this.formatMilliseconds(avgExecutionTime), meta: `${this.offlineThresholdSeconds} 秒后判定超时`, tone: 'info' }
        ];

        this.summaryGrid.innerHTML = cards.map((card) => `
            <article class="sm-kpi sm-kpi--${card.tone}">
                <div class="sm-kpi-inner">
                    <span class="sm-kpi-label">${this.escapeHtml(card.label)}</span>
                    <strong class="sm-kpi-value">${this.escapeHtml(card.value)}</strong>
                    <span class="sm-kpi-meta">${this.escapeHtml(card.meta)}</span>
                </div>
            </article>
        `).join('');
    }

    renderMatrix() {
        if (!this.matrix || !this.matrixMeta) {
            return;
        }

        const stations = this.getSortedStations();
        const onlineCount = stations.filter((station) => this.computeIsOnline(station)).length;
        this.matrixMeta.textContent = `${onlineCount}/${stations.length} 在线`;

        if (stations.length === 0) {
            this.matrix.innerHTML = `
                <div class="sm-empty is-center">
                    <strong>暂无工作站数据</strong>
                    <span>等待 Station 通过 /hubs/station-ingest 注册、心跳和上报结果。</span>
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
                            <span class="sm-station-badge">${this.escapeHtml(stateLabel)}</span>
                        </div>
                        <span class="sm-station-line">${this.escapeHtml(station.lineName || station.machineName || station.stationId)}</span>
                    </div>
                    <div class="sm-station-stats">
                        <span class="sm-stat"><b>${Number(station.sessionOkCount || 0)}</b> OK</span>
                        <span class="sm-stat"><b>${Number(station.sessionNgCount || 0)}</b> NG</span>
                        <span class="sm-stat"><b>${Number(station.sessionErrorCount || 0)}</b> ERR</span>
                    </div>
                    <div class="sm-station-foot">
                        <span>${this.escapeHtml(outcomeLabel)}</span>
                        <span>${this.escapeHtml(this.formatRelativeTime(station.lastSeenAtUtc))}</span>
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
            this.focusMeta.textContent = '全站范围';
            this.focus.innerHTML = `
                <div class="sm-empty is-center">
                    <strong>当前为全站监控</strong>
                    <span>选择左侧某个工作站后，健康、日志、命令和结果明细会联动到该 Station。</span>
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
                <span class="sm-detail-badge">${this.escapeHtml(this.formatState(detail.state, isOnline))}</span>
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
            ${this.renderDetailSection('指令队列', recentCommands, (command) => `
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
            `, '暂无指令记录。', 6)}
            ${this.renderDetailSection('健康采样', recentHealth, (health) => `
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
            `, '暂无健康采样。', 4)}
            ${this.renderDetailSection('日志', recentLogs, (log) => `
                <article class="sm-row sm-row--${this.escapeHtml(String(log.level || '').toLowerCase())}">
                    <div class="sm-row-main">
                        <span class="sm-row-label">${this.escapeHtml(log.level || '--')}</span>
                        <span class="sm-row-sublabel">${this.escapeHtml(log.source || '--')}</span>
                    </div>
                    <span class="sm-row-value">${this.escapeHtml(log.renderedMessage || log.exceptionMessage || '--')}</span>
                </article>
            `, '暂无 WARN 或 ERROR 级别日志。', 5)}
            ${this.renderDetailSection('近期结果', recentResults, (result) => `
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
            `, '该工作站暂无缓存结果。', 8)}
        `;
    }

    renderDetailSection(title, items, renderItem, emptyText, take) {
        const rows = Array.isArray(items) ? items : [];
        return `
            <div class="sm-section">
                <div class="sm-section-header">
                    <span>${this.escapeHtml(title)}</span>
                    <span>${rows.length}</span>
                </div>
                ${rows.length === 0
                    ? `<div class="sm-empty compact"><span>${this.escapeHtml(emptyText)}</span></div>`
                    : rows.slice(0, take).map(renderItem).join('')}
            </div>
        `;
    }

    renderStream() {
        if (!this.stream || !this.streamMeta) {
            return;
        }

        const flow = [
            ...this.globalResults.map((record) => ({
                type: 'result',
                stationId: record.stationId,
                atUtc: record.completedAtUtc,
                data: record
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
                    <span>工作站开始上报后，结果和日志数据流会显示在这里。</span>
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
                        <strong>${this.escapeHtml(item.data.status)}</strong>
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

    renderResultsWorkbench() {
        if (!this.resultOverview || !this.resultCharts || !this.resultToolbar || !this.resultList || !this.resultPagination) {
            return;
        }

        const scopeLabel = this.getCurrentScopeLabel();
        const stats = this.calculateResultStats(this.monitorResults);
        const totalPages = Math.max(1, Math.ceil(this.monitorTotalCount / this.monitorPageSize));
        this.resultsTitle.textContent = `${scopeLabel}结果明细`;
        this.resultsSubtitle.textContent = this.selectedStationId
            ? '已按选中 Station 过滤'
            : '默认汇总所有已接入 Station';
        this.resultsMeta.textContent = this.resultLoading
            ? '加载中'
            : `${this.monitorTotalCount} 条记录`;

        this.resultOverview.innerHTML = `
            <article class="sm-result-kpi">
                <span>当前页</span>
                <strong>${this.monitorResults.length}</strong>
                <small>总计 ${this.monitorTotalCount}</small>
            </article>
            <article class="sm-result-kpi">
                <span>OK / NG / ERR</span>
                <strong>${stats.ok} / ${stats.ng} / ${stats.error}</strong>
                <small>来自真实结果</small>
            </article>
            <article class="sm-result-kpi">
                <span>良率</span>
                <strong>${stats.total === 0 ? '--' : `${Math.round((stats.ok / stats.total) * 1000) / 10}%`}</strong>
                <small>按当前加载结果计算</small>
            </article>
            <article class="sm-result-kpi">
                <span>平均耗时</span>
                <strong>${this.formatMilliseconds(stats.averageExecutionTimeMs)}</strong>
                <small>executionTimeMs</small>
            </article>
        `;

        this.resultCharts.innerHTML = `
            ${this.renderYieldChart(stats)}
            ${this.renderDiagnosticChart(this.monitorResults)}
            ${this.renderTrendChart(this.monitorResults)}
        `;

        this.resultToolbar.innerHTML = `
            <label class="sm-filter">
                <span>状态</span>
                <select id="sm-result-status-filter">
                    ${this.renderOption('all', '全部', this.resultFilters.status)}
                    ${this.renderOption('Ok', 'OK', this.resultFilters.status)}
                    ${this.renderOption('Ng', 'NG', this.resultFilters.status)}
                    ${this.renderOption('Error', '异常', this.resultFilters.status)}
                    ${this.renderOption('Canceled', '已取消', this.resultFilters.status)}
                </select>
            </label>
            <label class="sm-filter">
                <span>诊断码</span>
                <select id="sm-result-diagnostic-filter">
                    ${this.renderOption('all', '全部', this.resultFilters.diagnosticCode)}
                    ${this.getDiagnosticOptions().map((code) => this.renderOption(code, code, this.resultFilters.diagnosticCode)).join('')}
                </select>
            </label>
            <div class="sm-result-actions">
                <button type="button" data-monitor-refresh>刷新</button>
                <button type="button" data-result-export="csv">CSV</button>
                <button type="button" data-result-export="json">JSON</button>
                <button type="button" data-result-export="excel">Excel</button>
            </div>
        `;

        if (this.resultLoading) {
            this.resultList.innerHTML = '<div class="sm-empty is-center"><strong>正在加载结果...</strong></div>';
        } else if (this.resultLoadError) {
            this.resultList.innerHTML = `
                <div class="sm-empty is-center">
                    <strong>结果加载失败</strong>
                    <span>${this.escapeHtml(this.resultLoadError)}</span>
                </div>
            `;
        } else if (this.monitorResults.length === 0) {
            this.resultList.innerHTML = `
                <div class="sm-empty is-center">
                    <strong>暂无结果</strong>
                    <span>没有真实 Station 结果命中当前范围和筛选条件。</span>
                </div>
            `;
        } else {
            this.resultList.innerHTML = this.monitorResults.map((record) => this.renderMonitorResultCard(record)).join('');
        }

        this.resultPagination.innerHTML = `
            <button type="button" data-result-page="${Math.max(0, this.monitorPageIndex - 1)}" ${this.monitorPageIndex <= 0 ? 'disabled' : ''}>上一页</button>
            <span>第 ${this.monitorPageIndex + 1} / ${totalPages} 页</span>
            <button type="button" data-result-page="${Math.min(totalPages - 1, this.monitorPageIndex + 1)}" ${this.monitorPageIndex + 1 >= totalPages ? 'disabled' : ''}>下一页</button>
        `;
    }

    renderYieldChart(stats) {
        const rate = stats.total === 0 ? 0 : Math.max(0, Math.min(100, (stats.ok / stats.total) * 100));
        return `
            <article class="sm-chart sm-chart-yield">
                <div class="sm-chart-head">
                    <span>良率仪表</span>
                    <strong>${stats.total === 0 ? '--' : `${rate.toFixed(1)}%`}</strong>
                </div>
                <div class="sm-yield-track">
                    <span style="width:${rate}%"></span>
                </div>
                <div class="sm-chart-foot">
                    <span>${stats.ok} OK</span>
                    <span>${stats.ng} NG</span>
                    <span>${stats.error} ERR</span>
                </div>
            </article>
        `;
    }

    renderDiagnosticChart(records) {
        const groups = this.groupBy(records, (record) => record.diagnosticCode || 'Unknown')
            .slice(0, 6);
        const max = Math.max(1, ...groups.map((item) => item.count));
        return `
            <article class="sm-chart">
                <div class="sm-chart-head">
                    <span>诊断码分布</span>
                    <strong>${groups.length}</strong>
                </div>
                <div class="sm-bars">
                    ${groups.length === 0
                        ? '<div class="sm-empty compact"><span>暂无诊断码。</span></div>'
                        : groups.map((item) => `
                            <div class="sm-bar-row">
                                <span>${this.escapeHtml(item.key)}</span>
                                <div><i style="width:${Math.max(4, (item.count / max) * 100)}%"></i></div>
                                <b>${item.count}</b>
                            </div>
                        `).join('')}
                </div>
            </article>
        `;
    }

    renderTrendChart(records) {
        const groups = this.groupBy(records, (record) => this.formatHourBucket(record.completedAtUtc))
            .sort((left, right) => String(left.key).localeCompare(String(right.key)))
            .slice(-8);
        const max = Math.max(1, ...groups.map((item) => item.count));
        return `
            <article class="sm-chart">
                <div class="sm-chart-head">
                    <span>吞吐趋势</span>
                    <strong>${records.length}</strong>
                </div>
                <div class="sm-trend">
                    ${groups.length === 0
                        ? '<div class="sm-empty compact"><span>暂无趋势数据。</span></div>'
                        : groups.map((item) => `
                            <div class="sm-trend-col">
                                <span style="height:${Math.max(8, (item.count / max) * 100)}%"></span>
                                <small>${this.escapeHtml(item.key)}</small>
                            </div>
                        `).join('')}
                </div>
            </article>
        `;
    }

    renderMonitorResultCard(record) {
        const outputCards = buildResultCardsFromOutputData(record.primaryOutputsPreview || {}, {
            status: record.status
        });
        return `
            <article class="sm-monitor-result sm-monitor-result--${this.toCssToken(record.status)}">
                <header>
                    <div>
                        <span class="sm-result-status">${this.escapeHtml(record.status)}</span>
                        <strong>${this.escapeHtml(record.stationLabel || record.stationId || '--')}</strong>
                    </div>
                    <span>${this.escapeHtml(this.formatRelativeTime(record.completedAtUtc))}</span>
                </header>
                <dl>
                    <div><dt>序号</dt><dd>${Number(record.sequenceId || 0)}</dd></div>
                    <div><dt>诊断</dt><dd>${this.escapeHtml(record.diagnosticCode || '--')}</dd></div>
                    <div><dt>耗时</dt><dd>${this.formatMilliseconds(record.executionTimeMs)}</dd></div>
                    <div><dt>包</dt><dd>${this.escapeHtml(record.packageName || record.packageId || '--')}</dd></div>
                </dl>
                ${record.diagnosticMessage
                    ? `<p class="sm-result-message">${this.escapeHtml(record.diagnosticMessage)}</p>`
                    : ''}
                <div class="sm-result-output">
                    ${outputCards.length === 0
                        ? '<div class="sm-empty compact"><span>暂无主输出预览。</span></div>'
                        : outputCards.map((card) => renderResultCardHtml(card, { fallbackStatus: record.status })).join('')}
                </div>
            </article>
        `;
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
            totalStations: Number(summary.totalStations ?? summary.TotalStations ?? 0),
            onlineStations: Number(summary.onlineStations ?? summary.OnlineStations ?? 0),
            offlineStations: Number(summary.offlineStations ?? summary.OfflineStations ?? 0),
            runningStations: Number(summary.runningStations ?? summary.RunningStations ?? 0),
            faultedStations: Number(summary.faultedStations ?? summary.FaultedStations ?? 0),
            alertCount: Number(summary.alertCount ?? summary.AlertCount ?? 0),
            totalOkCount: Number(summary.totalOkCount ?? summary.TotalOkCount ?? 0),
            totalNgCount: Number(summary.totalNgCount ?? summary.TotalNgCount ?? 0),
            totalErrorCount: Number(summary.totalErrorCount ?? summary.TotalErrorCount ?? 0),
            averageExecutionTimeMs: Number(summary.averageExecutionTimeMs ?? summary.AverageExecutionTimeMs ?? 0),
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

    normalizeResultsPage(response) {
        const items = Array.isArray(response)
            ? response
            : (response?.items ?? response?.Items ?? []);
        return {
            items: (Array.isArray(items) ? items : [])
                .map((item) => this.normalizeMonitorResult(item))
                .filter(Boolean),
            totalCount: Number(response?.totalCount ?? response?.TotalCount ?? items.length ?? 0),
            pageIndex: Number(response?.pageIndex ?? response?.PageIndex ?? this.monitorPageIndex),
            pageSize: Number(response?.pageSize ?? response?.PageSize ?? this.monitorPageSize)
        };
    }

    normalizeMonitorResult(result, station = null) {
        const normalized = this.normalizeResult(result);
        if (!normalized.stationId) {
            return null;
        }

        const stationInfo = station && station.stationId && station.stationId !== '--'
            ? station
            : this.stations.get(normalized.stationId);
        const status = this.normalizeResultStatus(normalized.outcome, normalized.inspectionStatus);
        return {
            ...normalized,
            status,
            stationLabel: stationInfo?.stationName || stationInfo?.lineName || stationInfo?.machineName || normalized.stationId
        };
    }

    normalizeResult(result) {
        const preview = result?.primaryOutputsPreview ?? result?.PrimaryOutputsPreview ?? {};
        return {
            stationId: result?.stationId ?? result?.StationId ?? null,
            sequenceId: Number(result?.sequenceId ?? result?.SequenceId ?? 0),
            messageId: result?.messageId ?? result?.MessageId ?? '',
            runId: result?.runId ?? result?.RunId ?? '--',
            packageId: result?.packageId ?? result?.PackageId ?? null,
            packageName: result?.packageName ?? result?.PackageName ?? null,
            packageVersion: result?.packageVersion ?? result?.PackageVersion ?? null,
            flowHash: result?.flowHash ?? result?.FlowHash ?? null,
            imageId: result?.imageId ?? result?.ImageId ?? '--',
            outcome: result?.outcome ?? result?.Outcome ?? 'Error',
            inspectionStatus: result?.inspectionStatus ?? result?.InspectionStatus ?? null,
            executionTimeMs: Number(result?.executionTimeMs ?? result?.ExecutionTimeMs ?? 0),
            diagnosticCode: result?.diagnosticCode ?? result?.DiagnosticCode ?? null,
            diagnosticMessage: result?.diagnosticMessage ?? result?.DiagnosticMessage ?? null,
            primaryOutputsPreview: preview && typeof preview === 'object' && !Array.isArray(preview) ? preview : {},
            startedAtUtc: result?.startedAtUtc ?? result?.StartedAtUtc ?? null,
            completedAtUtc: result?.completedAtUtc ?? result?.CompletedAtUtc ?? null,
            createdAtUtc: result?.createdAtUtc ?? result?.CreatedAtUtc ?? null
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

    normalizeResultStatus(outcome, inspectionStatus) {
        const normalizedOutcome = String(outcome ?? '').trim().toUpperCase();
        if (normalizedOutcome === 'OK' || normalizedOutcome === '0') {
            return 'OK';
        }

        if (normalizedOutcome === 'NG' || normalizedOutcome === '1') {
            return 'NG';
        }

        if (normalizedOutcome === 'ERROR' || normalizedOutcome === '2') {
            return 'Error';
        }

        if (normalizedOutcome === 'CANCELED' || normalizedOutcome === 'CANCELLED' || normalizedOutcome === '3') {
            return 'Canceled';
        }

        const normalizedStatus = String(inspectionStatus ?? '').trim().toUpperCase();
        if (normalizedStatus === 'OK' || normalizedStatus === '2') {
            return 'OK';
        }

        if (normalizedStatus === 'NG' || normalizedStatus === '3') {
            return 'NG';
        }

        if (normalizedStatus === 'ERROR' || normalizedStatus === '4') {
            return 'Error';
        }

        return normalizedOutcome || 'Unknown';
    }

    calculateResultStats(records) {
        const total = records.length;
        const ok = records.filter((record) => record.status === 'OK').length;
        const ng = records.filter((record) => record.status === 'NG').length;
        const error = records.filter((record) => record.status === 'Error' || record.status === 'Canceled').length;
        const timed = records.filter((record) => Number(record.executionTimeMs) > 0);
        const averageExecutionTimeMs = timed.length === 0
            ? 0
            : timed.reduce((sum, record) => sum + Number(record.executionTimeMs || 0), 0) / timed.length;

        return { total, ok, ng, error, averageExecutionTimeMs };
    }

    getCurrentScopeLabel() {
        if (!this.selectedStationId) {
            return '全站';
        }

        const station = this.stations.get(this.selectedStationId);
        return station?.stationName || station?.lineName || this.selectedStationId;
    }

    resultMatchesCurrentScope(record) {
        return !this.selectedStationId ||
            String(record.stationId || '').toLowerCase() === String(this.selectedStationId).toLowerCase();
    }

    resultMatchesFilters(record) {
        const statusMatches = this.resultFilters.status === 'all' ||
            String(record.outcome || '').toLowerCase() === this.resultFilters.status.toLowerCase() ||
            String(record.status || '').toLowerCase() === this.normalizeResultStatus(this.resultFilters.status).toLowerCase();
        const diagnosticMatches = this.resultFilters.diagnosticCode === 'all' ||
            String(record.diagnosticCode || '').toLowerCase() === this.resultFilters.diagnosticCode.toLowerCase();
        return statusMatches && diagnosticMatches;
    }

    getDiagnosticOptions() {
        const values = [
            ...this.monitorResults.map((record) => record.diagnosticCode),
            ...this.globalResults.map((record) => record.diagnosticCode),
            ...this.getSortedStations().map((station) => station.lastDiagnosticCode)
        ].filter(Boolean);
        return [...new Set(values)].sort((left, right) => String(left).localeCompare(String(right)));
    }

    renderOption(value, label, selectedValue) {
        return `<option value="${this.escapeHtml(value)}" ${String(value) === String(selectedValue) ? 'selected' : ''}>${this.escapeHtml(label)}</option>`;
    }

    groupBy(records, keySelector) {
        const groups = new Map();
        records.forEach((record) => {
            const key = keySelector(record) || 'Unknown';
            groups.set(key, (groups.get(key) || 0) + 1);
        });
        return [...groups.entries()]
            .map(([key, count]) => ({ key, count }))
            .sort((left, right) => right.count - left.count || String(left.key).localeCompare(String(right.key)));
    }

    dedupeResults(results) {
        const seen = new Set();
        return results.filter((result) => {
            const key = `${result.stationId || ''}:${result.sequenceId || ''}:${result.messageId || ''}`;
            if (seen.has(key)) {
                return false;
            }
            seen.add(key);
            return true;
        });
    }

    dedupeResultRecords(records) {
        const seen = new Set();
        return records.filter((record) => {
            const key = `${record.stationId || ''}:${record.sequenceId || ''}:${record.messageId || ''}`;
            if (seen.has(key)) {
                return false;
            }
            seen.add(key);
            return true;
        });
    }

    exportMonitorResults(format) {
        const records = this.monitorResults;
        if (records.length === 0) {
            return;
        }

        const normalizedFormat = String(format || 'csv').toLowerCase();
        if (normalizedFormat === 'json') {
            this.downloadBlob(
                JSON.stringify(records, null, 2),
                'application/json',
                `station-results-${Date.now()}.json`);
            return;
        }

        const csv = this.convertResultsToCsv(records);
        this.downloadBlob(
            csv,
            normalizedFormat === 'excel' ? 'application/vnd.ms-excel' : 'text/csv;charset=utf-8',
            `station-results-${Date.now()}.${normalizedFormat === 'excel' ? 'xls' : 'csv'}`);
    }

    convertResultsToCsv(records) {
        const headers = [
            'StationId',
            'Station',
            'SequenceId',
            'Status',
            'DiagnosticCode',
            'DiagnosticMessage',
            'ExecutionTimeMs',
            'CompletedAtUtc',
            'PackageName'
        ];
        const rows = records.map((record) => [
            record.stationId,
            record.stationLabel,
            record.sequenceId,
            record.status,
            record.diagnosticCode,
            record.diagnosticMessage,
            record.executionTimeMs,
            record.completedAtUtc,
            record.packageName
        ]);
        return [headers, ...rows]
            .map((row) => row.map((value) => `"${String(value ?? '').replaceAll('"', '""')}"`).join(','))
            .join('\n');
    }

    downloadBlob(content, mimeType, fileName) {
        const blob = new Blob([content], { type: mimeType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
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
            return `${deltaSeconds} 秒前`;
        }

        const minutes = Math.floor(deltaSeconds / 60);
        if (minutes < 60) {
            return `${minutes} 分钟前`;
        }

        const hours = Math.floor(minutes / 60);
        if (hours < 24) {
            return `${hours} 小时前`;
        }

        const days = Math.floor(hours / 24);
        return `${days} 天前`;
    }

    formatHourBucket(value) {
        if (!value) {
            return '--';
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return '--';
        }

        return `${String(date.getHours()).padStart(2, '0')}:00`;
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
        const status = this.normalizeResultStatus(outcome, inspectionStatus);
        switch (status) {
            case 'OK':
                return '良品';
            case 'NG':
                return '不良';
            case 'Error':
                return '异常';
            case 'Canceled':
                return '已取消';
            default:
                return '待定';
        }
    }

    toCssToken(value) {
        return String(value ?? 'unknown').toLowerCase().replace(/[^a-z0-9_-]/g, '-') || 'unknown';
    }

    escapeHtml(value) {
        const div = document.createElement('div');
        div.textContent = String(value ?? '');
        return div.innerHTML;
    }
}

export { StationMonitorView };
