import httpClient from '../../core/messaging/httpClient.js';
import { getStoredToken } from '../auth/authStorage.js';
import { buildSseHeaders, buildSseUrl, parseSseFrame } from '../inspection/inspectionSseClient.mjs';
import {
    buildResultCardsFromOutputData,
    renderResultCardHtml
} from '../results/portDataTypeRenderer.mjs';
import {
    calculateCanonicalStatistics,
    normalizeCanonicalOutcome,
    normalizeCanonicalStatistics
} from '../inspection/canonicalOutcome.mjs';

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
        this.monitorStatistics = null;
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
        this.isActive = false;
        this.lastSseEventId = null;
        this.sseConnectionId = 0;
        this.sseReconnectAttempt = 0;
        this.sseReconnectBaseDelayMs = 1000;
        this.sseReconnectMaxDelayMs = 10000;
        this._renderDirty = true;
        this._resultsDirty = true;
        this._renderQueued = false;
        this._renderContextActive = false;
        this._renderNowMs = 0;
        this._stationEntriesCache = null;
        this._stationRenderSnapshot = null;
        this._onlineCache = new Map();
        this._relativeTimeCache = new Map();
        this._summaryRenderSignature = '';
        this.renderShell();
        this.bindEvents();
        this._visibilityHandler = this.handleVisibilityChange.bind(this);
        document.addEventListener('visibilitychange', this._visibilityHandler);
    }

    async activate() {
        this.isActive = true;
        await this.loadInitialData();
        if (!this.isActive) {
            return;
        }

        this.connectSse();
        this.startRefreshTimer();
        this.render();
    }

    deactivate() {
        this.isActive = false;
        this.stopRefreshTimer();

        if (this.eventSource) {
            this.eventSource.close();
            this.eventSource = null;
        }
    }

    dispose() {
        this.deactivate();
        document.removeEventListener('visibilitychange', this._visibilityHandler);
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
                                <small id="sm-results-subtitle">等待真实工站结果</small>
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

    handleVisibilityChange() {
        if (!this.isActive) {
            return;
        }

        if (document.hidden) {
            this.stopRefreshTimer();
            return;
        }

        this.startRefreshTimer();
        this.markDirty();
        this.requestRender();
    }

    stopRefreshTimer() {
        if (!this.refreshTimer) {
            return;
        }

        clearInterval(this.refreshTimer);
        this.refreshTimer = null;
    }

    markDirty() {
        this._renderDirty = true;
    }

    markResultsDirty() {
        this._renderDirty = true;
        this._resultsDirty = true;
    }

    requestRender() {
        if (!this.isActive || document.hidden || this._renderQueued || !this._renderDirty) {
            return;
        }

        this._renderQueued = true;
        window.requestAnimationFrame(() => {
            this._renderQueued = false;
            if (this.isActive && !document.hidden && this._renderDirty) {
                this.render();
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
            this.markDirty();
            this.updateSyncState('实时', 'live');
        } catch (error) {
            console.error('[StationMonitorView] Failed to load initial data:', error);
            this.markDirty();
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
        this.markDirty();
        await this.loadResultsPage(0);
        this.render();
    }

    async selectStation(stationId) {
        this.selectedStationId = stationId;
        this.scopeAllButton?.classList.remove('is-active');
        this.markDirty();
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
            this.markDirty();
        } catch (error) {
            console.error('[StationMonitorView] Failed to load station detail:', error);
        }
    }

    async loadResultsPage(pageIndex = 0, { renderAfter = true } = {}) {
        this.resultLoading = true;
        this.resultLoadError = '';
        this.monitorPageIndex = Math.max(0, Number(pageIndex) || 0);
        this.markResultsDirty();
        if (renderAfter) {
            this.renderResultsWorkbench();
        }

        try {
            const resultParams = {
                pageIndex: this.monitorPageIndex,
                pageSize: this.monitorPageSize,
                ...this.buildResultQueryParams()
            };

            const statisticsParams = {
                range: 'all',
                ...this.buildResultQueryParams()
            };

            const [response, statistics] = await Promise.all([
                httpClient.get('/stations/results', resultParams),
                httpClient.get('/stations/statistics', statisticsParams).catch((error) => {
                    console.warn('[StationMonitorView] Failed to load station statistics:', error);
                    return null;
                })
            ]);
            const page = this.normalizeResultsPage(response);
            this.monitorResults = page.items;
            this.monitorStatistics = this.normalizeResultStatistics(statistics);
            this.monitorTotalCount = page.totalCount;
            this.monitorPageIndex = page.pageIndex;
            this.monitorPageSize = page.pageSize;
            this.markResultsDirty();
        } catch (error) {
            console.error('[StationMonitorView] Failed to load station results:', error);
            this.resultLoadError = error?.message || '结果查询失败';
            this.markResultsDirty();
        } finally {
            this.resultLoading = false;
            if (renderAfter) {
                this.render();
            }
        }
    }

    buildResultQueryParams() {
        const params = {};

        if (this.selectedStationId) {
            params.stationId = this.selectedStationId;
        }

        if (this.resultFilters.status !== 'all') {
            params.status = this.resultFilters.status;
        }

        if (this.resultFilters.diagnosticCode !== 'all') {
            params.diagnosticCode = this.resultFilters.diagnosticCode;
        }

        return params;
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
        const response = await fetch(buildSseUrl(eventUrl, this.lastSseEventId), {
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
                this.markDirty();
                break;
            case 'heartbeat':
                break;
            default:
                console.debug('[StationMonitorView] Unhandled SSE event:', eventName);
                break;
        }

        this.requestRender();
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

        this.markDirty();
        this.markResultsDirty();
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

        this.markDirty();
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

        const alreadyLoaded = this.hasResultRecord(this.globalResults, record) ||
            this.hasResultRecord(this.monitorResults, record);

        this.globalResults.unshift(record);
        this.globalResults = this.dedupeResultRecords(this.globalResults).slice(0, 80);

        const matchesCurrentQuery = this.resultMatchesCurrentScope(record) &&
            this.resultMatchesFilters(record);
        if (matchesCurrentQuery && !alreadyLoaded) {
            this.monitorTotalCount += 1;
            this.applyRealtimeResultToStatistics(record);
        }

        if (this.monitorPageIndex === 0 && matchesCurrentQuery) {
            this.monitorResults = this.dedupeResultRecords([record, ...this.monitorResults]).slice(0, this.monitorPageSize);
            if (!alreadyLoaded) {
                this.monitorTotalCount = Math.max(this.monitorTotalCount, this.monitorResults.length);
            }
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

        this.markResultsDirty();
        this.markDirty();
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

        this.markDirty();
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

        this.markDirty();
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

        this.markDirty();
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

        if (this.stationActionRequiresConfirmation(action) && !this.confirmStationAction(action)) {
            return;
        }

        this.commandBusy = true;
        this.commandStatusMessage = this.getActionBusyMessage(action);
        this.commandStatusLevel = 'busy';
        this.markDirty();
        this.render();
        let actionResult = null;
        try {
            switch (action) {
                case 'ping':
                    actionResult = await this.createCommand('Ping', {});
                    break;
                case 'reload':
                    actionResult = await this.createCommand('ReloadPackage', {});
                    break;
                case 'stop':
                    actionResult = await this.createCommand('StopRuntime', {});
                    break;
                case 'deploy':
                    actionResult = await this.deployLatestPackage();
                    break;
                case 'testDeploy':
                    actionResult = await this.createAndDeployTestPackage();
                    break;
                default:
                    break;
            }

            await this.loadStationDetail(this.selectedStationId);
            this.commandStatusMessage = this.getActionSuccessMessage(action, actionResult);
            this.commandStatusLevel = 'success';
            this.markDirty();
        } catch (error) {
            console.error('[StationMonitorView] Station action failed:', error);
            this.commandStatusMessage = error?.message || '工站命令下发失败。';
            this.commandStatusLevel = 'error';
            this.markDirty();
        } finally {
            this.commandBusy = false;
            this.markDirty();
            this.render();
        }
    }

    async createCommand(commandType, payload) {
        return httpClient.post(`/stations/${encodeURIComponent(this.selectedStationId)}/commands`, {
            commandType,
            payloadJson: JSON.stringify(payload || {}),
            issuedBy: this.getCommandIssuer()
        });
    }

    async deployLatestPackage() {
        const targetPackage = this.getLatestProductionPackage();
        const packageId = targetPackage?.packageId ?? targetPackage?.PackageId;
        if (!packageId) {
            throw new Error('当前没有正式运行包。请先从工程导出正式运行包，或使用“下发测试包”。');
        }

        return httpClient.post(`/stations/${encodeURIComponent(this.selectedStationId)}/deploy-package`, {
            packageId,
            issuedBy: this.getCommandIssuer()
        });
    }

    getPackageKind(pkg) {
        const value = pkg?.packageKind ?? pkg?.PackageKind ?? pkg?.kind ?? pkg?.Kind;
        if (typeof value === 'string') {
            return value.toLowerCase();
        }

        if (typeof value === 'number') {
            return value === 1 ? 'test' : 'production';
        }

        return 'production';
    }

    isProductionPackage(pkg) {
        return this.getPackageKind(pkg) === 'production';
    }

    getProductionPackages() {
        return Array.isArray(this.packages)
            ? this.packages.filter((pkg) => this.isProductionPackage(pkg))
            : [];
    }

    getLatestProductionPackage() {
        return this.getProductionPackages()[0] || null;
    }

    formatPackageLabel(pkg) {
        if (!pkg) {
            return '--';
        }

        const packageName = pkg.packageName ?? pkg.PackageName ?? pkg.packageId ?? pkg.PackageId ?? '--';
        const packageId = pkg.packageId ?? pkg.PackageId;
        const kind = this.isProductionPackage(pkg) ? '正式包' : '测试包';
        return packageId && packageId !== packageName
            ? `${packageName}（${kind} / ${packageId}）`
            : `${packageName}（${kind}）`;
    }

    stationActionRequiresConfirmation(action) {
        return ['stop', 'deploy', 'testDeploy'].includes(action);
    }

    getStationActionMeta(action) {
        switch (action) {
            case 'stop':
                return {
                    title: '停止运行',
                    impact: '可能中断当前工站节拍，并让正在执行的检测进入停止流程。',
                    confirmHint: '确认现场允许停机后再继续。'
                };
            case 'deploy':
                return {
                    title: '部署正式运行包',
                    impact: '会向工站下发最新正式运行包，工站轮询执行后可能替换当前检测配置。',
                    confirmHint: '测试包不会从这里下发；请确认当前产线允许变更正式配置。'
                };
            case 'testDeploy':
                return {
                    title: '生成测试包并下发',
                    impact: '会生成测试运行包并下发到工站，仅应用于调试或验收场景。',
                    confirmHint: '请勿在生产节拍中误下发测试包。'
                };
            default:
                return {
                    title: '执行命令',
                    impact: '将向当前工站下发命令。',
                    confirmHint: '请确认后继续。'
                };
        }
    }

    confirmStationAction(action) {
        const station = this.selectedStationDetail || this.stations.get(this.selectedStationId) || {};
        const meta = this.getStationActionMeta(action);
        const stationName = station.stationName || station.lineName || station.machineName || '未命名工作站';
        const state = this.formatState(station.state, this.computeIsOnline(station));
        const targetPackage = action === 'deploy' ? this.getLatestProductionPackage() : null;
        const packageLabel = action === 'deploy'
            ? this.formatPackageLabel(targetPackage)
            : action === 'testDeploy'
                ? '将生成新的测试包'
                : (station.packageId || '--');
        const lines = [
            `即将执行：${meta.title}`,
            '',
            `工站：${stationName}`,
            `标识：${this.selectedStationId}`,
            `当前状态：${state}`,
            `目标包：${packageLabel}`,
            '',
            `影响范围：${meta.impact}`,
            meta.confirmHint,
            '',
            '该操作会记录操作人、时间和命令 ID。确认继续？'
        ];

        return window.confirm(lines.join('\n'));
    }

    getCommandIssuer() {
        const user = window.currentUser || {};
        return user.displayName || user.username || 'Studio';
    }

    formatCommandAck(result) {
        const commandId = result?.commandId ?? result?.CommandId ?? result?.id ?? result?.Id ?? '--';
        const issuer = this.getCommandIssuer();
        const issuedAt = new Date().toLocaleString();
        return `审计：操作人 ${issuer}，时间 ${issuedAt}，命令 ID ${commandId}`;
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

    getActionSuccessMessage(action, result = null) {
        const audit = this.formatCommandAck(result);
        switch (action) {
            case 'testDeploy':
                return `测试包已生成，部署命令已下发；等待工站轮询执行；${audit}`;
            case 'deploy':
                return `部署命令已下发；等待工站轮询执行；${audit}`;
            case 'reload':
                return `重载命令已下发；${audit}`;
            case 'stop':
                return `停止命令已下发；${audit}`;
            case 'ping':
                return `Ping 命令已下发；${audit}`;
            default:
                return `命令已下发；${audit}`;
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
            issuedBy: this.getCommandIssuer()
        });
    }

    startRefreshTimer() {
        if (!this.isActive || this.refreshTimer || document.hidden) {
            return;
        }

        this.refreshTimer = window.setInterval(() => {
            if (this.isActive && !document.hidden) {
                this.markDirty();
                this.requestRender();
            }
        }, 5000);
    }

    render() {
        this._renderContextActive = true;
        this._renderNowMs = Date.now();
        this._stationEntriesCache = null;
        this._stationRenderSnapshot = null;
        this._onlineCache.clear();
        this._relativeTimeCache.clear();
        this.scopeAllButton?.classList.toggle('is-active', !this.selectedStationId);
        this._renderDirty = false;
        try {
            this.renderSummary();
            this.renderMatrix();
            this.renderFocus();
            this.renderStream();
            if (this._resultsDirty) {
                this.renderResultsWorkbench();
                this._resultsDirty = false;
            }
        } finally {
            this._renderContextActive = false;
        }
    }

    renderSummary() {
        if (!this.summaryGrid) {
            return;
        }

        const snapshot = this.getStationRenderSnapshot();
        const outcomeStatistics = this.summary?.outcomeStatistics ?? snapshot.outcomeStatistics ??
            normalizeCanonicalStatistics({
                okCount: snapshot.totalOk,
                ngCount: snapshot.totalNg,
                errorCount: snapshot.totalError
            });
        const totalOk = outcomeStatistics.ok;
        const totalNg = outcomeStatistics.ng;
        const executionFailures = outcomeStatistics.executionFailures;
        const invalid = outcomeStatistics.invalid;
        const undetermined = outcomeStatistics.undetermined;
        const avgExecutionTime = this.summary?.averageExecutionTimeMs ??
            snapshot.averageExecutionTimeMs;
        const validDecisions = outcomeStatistics.validDecisions;
        const yieldRate = validDecisions === 0 ? '--' : `${Math.round(outcomeStatistics.yieldRate * 1000) / 10}%`;

        const cards = [
            { label: '在线', value: `${snapshot.onlineCount}/${snapshot.stations.length || 0}`, meta: `${snapshot.stations.length - snapshot.onlineCount} 个离线`, tone: 'success' },
            { label: '告警', value: String(snapshot.alertCount), meta: '故障或超时', tone: snapshot.alertCount > 0 ? 'warning' : 'default' },
            { label: '良率', value: yieldRate, meta: `${totalOk} OK / ${totalNg} NG · ${executionFailures} 执行异常 · ${invalid} 判定无效 · ${undetermined} 未判定`, tone: 'accent' },
            { label: '平均节拍', value: this.formatMilliseconds(avgExecutionTime), meta: `${this.offlineThresholdSeconds} 秒后判定超时`, tone: 'info' }
        ];

        const signature = cards
            .map((card) => `${card.label}:${card.value}:${card.meta}:${card.tone}`)
            .join('|');
        if (signature === this._summaryRenderSignature) {
            return;
        }

        this._summaryRenderSignature = signature;
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

        const snapshot = this.getStationRenderSnapshot();
        const stations = snapshot.stations;
        this.matrixMeta.textContent = `${snapshot.onlineCount}/${stations.length} 在线`;

        if (stations.length === 0) {
            this.matrix.innerHTML = `
                <div class="sm-empty is-center">
                    <strong>暂无工作站数据</strong>
                    <span>等待工站通过 /hubs/station-ingest 注册、心跳和上报结果。</span>
                </div>
            `;
            return;
        }

        this.matrix.innerHTML = stations.map((station) => {
            const isOnline = this.computeIsOnline(station);
            const isSelected = station.stationId === this.selectedStationId;
            const outcomeLabel = this.formatOutcome(
                station.lastOutcome,
                station.lastInspectionStatus,
                station.lastExecutionOutcome,
                station.lastDecisionOutcome);
            const stationStats = station.sessionOutcomeStatistics;
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
                        <span class="sm-stat"><b>${stationStats.ok}</b> OK</span>
                        <span class="sm-stat"><b>${stationStats.ng}</b> NG</span>
                        <span class="sm-stat"><b>${stationStats.executionFailures}</b> 执行异常</span>
                        <span class="sm-stat"><b>${stationStats.invalid}</b> 无效 / <b>${stationStats.undetermined}</b> 未判定</span>
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
                    <span>选择左侧某个工作站后，健康、日志、命令和结果明细会联动到该工站。</span>
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
        const healthDiagnosticCode = latestHealth.lastErrorCode || null;
        const healthDiagnosticMessage = latestHealth.lastErrorMessage || null;
        const activeDiagnosticCode = healthDiagnosticCode || detail.lastDiagnosticCode || null;
        const activeDiagnosticMessage = healthDiagnosticMessage || detail.lastDiagnosticMessage || null;
        const actionsDisabled = this.commandBusy ? 'disabled' : '';
        const productionActionDisabled = this.commandBusy || !isOnline
            ? 'disabled title="工站离线或状态未知，不能下发会影响生产的命令"'
            : '';
        const productionPackages = this.getProductionPackages();
        const deployDisabled = this.commandBusy || !isOnline || productionPackages.length === 0
            ? 'disabled title="工站离线、状态未知或暂无正式运行包，不能部署正式包"'
            : '';
        const testDeployDisabled = this.commandBusy || !isOnline
            ? 'disabled title="工站离线或状态未知，不能下发测试包"'
            : '';

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
                <button type="button" data-station-action="stop" data-risk="production-impact" ${productionActionDisabled}>停止运行</button>
                <button type="button" data-station-action="deploy" data-risk="configuration-change" ${deployDisabled}>部署正式包</button>
                <button type="button" class="sm-action-wide" data-station-action="testDeploy" data-risk="configuration-change" ${testDeployDisabled}>下发测试包</button>
            </div>
            ${this.commandStatusMessage
                ? `<div class="sm-command-status" data-level="${this.escapeHtml(this.commandStatusLevel)}">${this.escapeHtml(this.commandStatusMessage)}</div>`
                : ''}
            ${this.renderStationDiagnosticAdvice(activeDiagnosticCode, activeDiagnosticMessage)}
            <div class="sm-detail-stats">
                <div><span>良品</span><b>${detail.sessionOutcomeStatistics.ok}</b></div>
                <div><span>不良</span><b>${detail.sessionOutcomeStatistics.ng}</b></div>
                <div><span>执行异常</span><b>${detail.sessionOutcomeStatistics.executionFailures}</b></div>
                <div><span>无效 / 未判定</span><b>${detail.sessionOutcomeStatistics.invalid} / ${detail.sessionOutcomeStatistics.undetermined}</b></div>
                <div><span>平均</span><b>${this.formatMilliseconds(detail.averageExecutionTimeMs)}</b></div>
            </div>
            <dl class="sm-detail-meta">
                <div><dt>上次在线</dt><dd>${this.escapeHtml(this.formatRelativeTime(detail.lastSeenAtUtc))}</dd></div>
                <div><dt>最近结果</dt><dd>${this.escapeHtml(this.formatOutcome(detail.lastOutcome, detail.lastInspectionStatus, detail.lastExecutionOutcome, detail.lastDecisionOutcome))}</dd></div>
                <div><dt>诊断码</dt><dd>${this.escapeHtml(activeDiagnosticCode || activeDiagnosticMessage || '--')}</dd></div>
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
                        <span class="sm-row-label">${this.escapeHtml(this.formatOutcome(result.outcome, result.inspectionStatus, result.executionOutcome, result.decisionOutcome))}</span>
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

    renderStationDiagnosticAdvice(code, message) {
        const normalizedCode = String(code || '').trim();
        const text = String(message || '').trim();
        const defaultAdvice = this.getStationDiagnosticAdvice(normalizedCode);
        if (!defaultAdvice) {
            return '';
        }

        const label = normalizedCode ? `${normalizedCode}: ` : '';
        const body = text.includes('请检查') ? text : `${defaultAdvice}${text ? ` ${text}` : ''}`;
        return `<div class="sm-command-status" data-level="error">排查建议：${this.escapeHtml(label + body)}</div>`;
    }

    getStationDiagnosticAdvice(code) {
        const normalizedCode = String(code || '').trim().toLowerCase();
        if (normalizedCode === 'stationresultbackpressure') {
            return '请检查：Studio 连接、工站到 Studio 的网络、防火墙规则、spool 磁盘空间/权限、StationSync 队列容量。';
        }

        if (normalizedCode === 'stationresultspoolpersistfailed') {
            return '请检查：spool 磁盘空间/权限、StationSync spool 路径、Studio 连接、工站到 Studio 的网络和防火墙规则。';
        }

        return '';
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
        this.stream.classList.toggle('is-empty', flow.length === 0);
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
        const stats = this.monitorStatistics || this.calculateResultStats(this.monitorResults);
        const totalPages = Math.max(1, Math.ceil(this.monitorTotalCount / this.monitorPageSize));
        this.resultsTitle.textContent = `${scopeLabel}结果明细`;
        this.resultsSubtitle.textContent = this.selectedStationId
            ? '已按选中工站过滤'
            : '默认汇总所有已接入工站';
        this.resultsMeta.textContent = this.resultLoading
            ? '加载中'
            : `${this.monitorTotalCount} 条记录`;

        this.resultOverview.innerHTML = `
            <article class="sm-result-kpi">
                <span>总计</span>
                <strong>${this.monitorTotalCount}</strong>
                <small>当前页 ${this.monitorResults.length}</small>
            </article>
            <article class="sm-result-kpi">
                <span>有效判定 / 执行失败</span>
                <strong>${stats.validDecisions} / ${stats.executionFailures}</strong>
                <small>${stats.undetermined} 未判定</small>
            </article>
            <article class="sm-result-kpi">
                <span>良率</span>
                <strong>${stats.validDecisions === 0 ? '--' : `${Math.round(stats.yieldRate * 1000) / 10}%`}</strong>
                <small>${this.monitorStatistics ? '按筛选范围计算' : '按当前加载结果计算'}</small>
            </article>
            <article class="sm-result-kpi">
                <span>平均耗时</span>
                <strong>${this.formatMilliseconds(stats.averageExecutionTimeMs)}</strong>
                <small>executionTimeMs</small>
            </article>
        `;

        this.resultCharts.innerHTML = `
            ${this.renderYieldChart(stats)}
            ${this.renderDiagnosticChart(this.monitorResults, this.monitorStatistics)}
            ${this.renderTrendChart(this.monitorResults, this.monitorStatistics)}
        `;

        this.resultToolbar.innerHTML = `
            <label class="sm-filter">
                <span>状态</span>
                <select id="sm-result-status-filter">
                    ${this.renderOption('all', '全部', this.resultFilters.status)}
                    ${this.renderOption('ok', 'OK', this.resultFilters.status)}
                    ${this.renderOption('ng', 'NG', this.resultFilters.status)}
                    ${this.renderOption('undetermined', '未判定', this.resultFilters.status)}
                    ${this.renderOption('notApplicable', '不适用', this.resultFilters.status)}
                    ${this.renderOption('invalid', '判定无效', this.resultFilters.status)}
                    ${this.renderOption('failed', '执行失败', this.resultFilters.status)}
                    ${this.renderOption('timedOut', '执行超时', this.resultFilters.status)}
                    ${this.renderOption('cancelled', '已取消', this.resultFilters.status)}
                    ${this.renderOption('skipped', '未检测', this.resultFilters.status)}
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
                    <span>没有真实工站结果命中当前范围和筛选条件。</span>
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
        const rate = stats.validDecisions === 0 ? 0 : Math.max(0, Math.min(100, stats.yieldRate * 100));
        return `
            <article class="sm-chart sm-chart-yield">
                <div class="sm-chart-head">
                    <span>良率仪表</span>
                    <strong>${stats.validDecisions === 0 ? '--' : `${rate.toFixed(1)}%`}</strong>
                </div>
                <div class="sm-yield-track">
                    <span style="width:${rate}%"></span>
                </div>
                <div class="sm-chart-foot">
                    <span>${stats.ok} OK</span>
                    <span>${stats.ng} NG</span>
                    <span>${stats.executionFailures} 执行失败</span>
                    <span>${stats.undetermined} 未判定</span>
                </div>
            </article>
        `;
    }

    renderDiagnosticChart(records, statistics = null) {
        const groups = (Array.isArray(statistics?.byDiagnosticCode) && statistics.byDiagnosticCode.length > 0
            ? statistics.byDiagnosticCode
            : this.groupBy(records, (record) => record.diagnosticCode || 'Unknown'))
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

    renderTrendChart(records, statistics = null) {
        const groups = (Array.isArray(statistics?.hourlyTrend) && statistics.hourlyTrend.length > 0
            ? statistics.hourlyTrend.map((item) => ({
                key: this.formatHourBucket(item.time),
                count: item.count
            }))
            : this.groupBy(records, (record) => this.formatHourBucket(record.completedAtUtc)))
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
            <article class="sm-monitor-result sm-monitor-result--${this.toCssToken(record.outcomeCategory)}">
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
                    <div><dt>判定来源</dt><dd>${this.escapeHtml(record.decisionSource || '--')}</dd></div>
                    <div><dt>判定原因</dt><dd>${this.escapeHtml(record.reasonCode || '--')}</dd></div>
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
        return this.getStationRenderEntries().map((entry) => entry.station);
    }

    getStationRenderSnapshot() {
        if (this._renderContextActive && this._stationRenderSnapshot) {
            return this._stationRenderSnapshot;
        }

        const entries = this.getStationRenderEntries();
        let onlineCount = 0;
        let alertCount = 0;
        let totalExecutionTime = 0;
        const sessionStatistics = [];

        for (const entry of entries) {
            const station = entry.station;
            if (entry.isOnline) {
                onlineCount += 1;
            }
            if (!entry.isOnline || station.state === 'Faulted') {
                alertCount += 1;
            }
            sessionStatistics.push(station.sessionOutcomeStatistics);
            totalExecutionTime += Number(station.averageExecutionTimeMs || 0);
        }

        const snapshot = {
            stations: entries.map((entry) => entry.station),
            onlineCount,
            alertCount,
            outcomeStatistics: this.combineCanonicalStatistics(sessionStatistics),
            averageExecutionTimeMs: entries.length === 0 ? 0 : totalExecutionTime / entries.length
        };

        if (this._renderContextActive) {
            this._stationRenderSnapshot = snapshot;
        }

        return snapshot;
    }

    getStationRenderEntries() {
        if (this._renderContextActive && this._stationEntriesCache) {
            return this._stationEntriesCache;
        }

        const entries = [...this.stations.values()].map((station) => ({
            station,
            isOnline: this.computeIsOnline(station)
        }));

        entries.sort((left, right) => {
            const leftOnline = left.isOnline ? 1 : 0;
            const rightOnline = right.isOnline ? 1 : 0;
            if (leftOnline !== rightOnline) {
                return rightOnline - leftOnline;
            }

            if (left.station.state !== right.station.state) {
                return String(left.station.state || '').localeCompare(String(right.station.state || ''));
            }

            return String(left.station.stationId || '').localeCompare(String(right.station.stationId || ''));
        });

        if (this._renderContextActive) {
            this._stationEntriesCache = entries;
        }

        return entries;
    }

    computeIsOnline(station) {
        if (this._renderContextActive && station) {
            const cacheKey = [
                station.stationId || '',
                station.lastSeenAtUtc || '',
                station.isEnabled === false ? '0' : '1',
                station.isOnline ? '1' : '0'
            ].join('|');
            if (this._onlineCache.has(cacheKey)) {
                return this._onlineCache.get(cacheKey);
            }
        }

        if (station?.isEnabled === false) {
            if (this._renderContextActive && station) {
                this._onlineCache.set([
                    station.stationId || '',
                    station.lastSeenAtUtc || '',
                    '0',
                    station.isOnline ? '1' : '0'
                ].join('|'), false);
            }
            return false;
        }

        if (!station?.lastSeenAtUtc) {
            const result = Boolean(station?.isOnline);
            if (this._renderContextActive && station) {
                this._onlineCache.set([
                    station.stationId || '',
                    '',
                    station.isEnabled === false ? '0' : '1',
                    station.isOnline ? '1' : '0'
                ].join('|'), result);
            }
            return result;
        }

        const lastSeen = new Date(station.lastSeenAtUtc).getTime();
        if (!Number.isFinite(lastSeen)) {
            const result = Boolean(station?.isOnline);
            if (this._renderContextActive && station) {
                this._onlineCache.set([
                    station.stationId || '',
                    station.lastSeenAtUtc || '',
                    station.isEnabled === false ? '0' : '1',
                    station.isOnline ? '1' : '0'
                ].join('|'), result);
            }
            return result;
        }

        const result = (this._renderNowMs || Date.now()) - lastSeen <= this.offlineThresholdSeconds * 1000;
        if (this._renderContextActive && station) {
            this._onlineCache.set([
                station.stationId || '',
                station.lastSeenAtUtc || '',
                station.isEnabled === false ? '0' : '1',
                station.isOnline ? '1' : '0'
            ].join('|'), result);
        }
        return result;
    }

    normalizeSummary(summary) {
        if (!summary || typeof summary !== 'object') {
            return null;
        }

        const nestedOutcomeStatistics = summary.outcomeStatistics ?? summary.OutcomeStatistics;
        const outcomeSource = nestedOutcomeStatistics ?? {
            totalCount: summary.totalAttemptCount ?? summary.TotalAttemptCount,
            okCount: summary.totalOkCount ?? summary.TotalOkCount,
            ngCount: summary.totalNgCount ?? summary.TotalNgCount,
            errorCount: summary.totalErrorCount ?? summary.TotalErrorCount
        };
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
            outcomeStatistics: normalizeCanonicalStatistics(outcomeSource),
            averageExecutionTimeMs: Number(summary.averageExecutionTimeMs ?? summary.AverageExecutionTimeMs ?? 0),
            offlineThresholdSeconds: Number(summary.offlineThresholdSeconds ?? summary.OfflineThresholdSeconds ?? 15)
        };
    }

    normalizeStation(station) {
        const recentResults = station?.recentResults ?? station?.RecentResults;
        const recentHealth = station?.recentHealth ?? station?.RecentHealth;
        const recentLogs = station?.recentLogs ?? station?.RecentLogs;
        const recentCommands = station?.recentCommands ?? station?.RecentCommands;
        const canonicalSessionStatistics = station?.sessionOutcomeStatistics ?? station?.SessionOutcomeStatistics;
        const sessionOutcomeStatistics = normalizeCanonicalStatistics(canonicalSessionStatistics ?? {
            okCount: station?.sessionOkCount ?? station?.SessionOkCount ?? 0,
            ngCount: station?.sessionNgCount ?? station?.SessionNgCount ?? 0,
            errorCount: station?.sessionErrorCount ?? station?.SessionErrorCount ?? 0
        });
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
            sessionOutcomeStatistics,
            sessionOutcomeStatisticsIsLegacyProjection: Boolean(
                station?.sessionOutcomeStatisticsIsLegacyProjection
                ?? station?.SessionOutcomeStatisticsIsLegacyProjection
                ?? !canonicalSessionStatistics),
            lastOutcome: station?.lastOutcome ?? station?.LastOutcome ?? null,
            lastInspectionStatus: station?.lastInspectionStatus ?? station?.LastInspectionStatus ?? null,
            lastExecutionOutcome: station?.lastExecutionOutcome ?? station?.LastExecutionOutcome ?? null,
            lastDecisionOutcome: station?.lastDecisionOutcome ?? station?.LastDecisionOutcome ?? null,
            lastHasJudgmentSignal: station?.lastHasJudgmentSignal ?? station?.LastHasJudgmentSignal ?? null,
            lastDecisionSource: station?.lastDecisionSource ?? station?.LastDecisionSource ?? null,
            lastReasonCode: station?.lastReasonCode ?? station?.LastReasonCode ?? null,
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

    normalizeResultStatistics(statistics) {
        if (!statistics || typeof statistics !== 'object') {
            return null;
        }

        const diagnosticItems = statistics.byDiagnosticCode
            ?? statistics.ByDiagnosticCode
            ?? statistics.defectDistribution?.items
            ?? statistics.DefectDistribution?.Items
            ?? [];
        const trendItems = statistics.hourlyTrend
            ?? statistics.HourlyTrend
            ?? statistics.trend?.dataPoints
            ?? statistics.Trend?.DataPoints
            ?? [];

        return {
            ...normalizeCanonicalStatistics(statistics),
            averageExecutionTimeMs: Number(
                statistics.averageExecutionTimeMs
                ?? statistics.AverageExecutionTimeMs
                ?? statistics.averageProcessingTimeMs
                ?? statistics.AverageProcessingTimeMs
                ?? 0),
            byDiagnosticCode: (Array.isArray(diagnosticItems) ? diagnosticItems : [])
                .map((item) => ({
                    key: item.diagnosticCode ?? item.DiagnosticCode ?? item.defectType ?? item.DefectType ?? 'Unknown',
                    count: Number(item.count ?? item.Count ?? 0)
                }))
                .filter((item) => item.count > 0),
            hourlyTrend: (Array.isArray(trendItems) ? trendItems : [])
                .map((item) => ({
                    time: item.hourUtc ?? item.HourUtc ?? item.timestamp ?? item.Timestamp ?? null,
                    count: Number(item.totalCount ?? item.TotalCount ?? item.total ?? item.Total ?? 0)
                }))
                .filter((item) => item.time && item.count > 0)
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
        const outcome = normalizeCanonicalOutcome(normalized);
        return {
            ...normalized,
            status: outcome.label,
            outcomeCategory: outcome.category,
            executionOutcome: outcome.executionOutcome,
            decisionOutcome: outcome.decisionOutcome,
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
            executionOutcome: result?.executionOutcome ?? result?.ExecutionOutcome ?? null,
            decisionOutcome: result?.decisionOutcome ?? result?.DecisionOutcome ?? null,
            hasJudgmentSignal: result?.hasJudgmentSignal ?? result?.HasJudgmentSignal ?? null,
            decisionSource: result?.decisionSource ?? result?.DecisionSource ?? null,
            reasonCode: result?.reasonCode ?? result?.ReasonCode ?? null,
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
        const canonical = calculateCanonicalStatistics(records);
        const timed = records.filter((record) => Number(record.executionTimeMs) > 0);
        const averageExecutionTimeMs = timed.length === 0
            ? 0
            : timed.reduce((sum, record) => sum + Number(record.executionTimeMs || 0), 0) / timed.length;

        return { ...canonical, averageExecutionTimeMs };
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
            String(record.outcomeCategory || '').toLowerCase() === this.resultFilters.status.toLowerCase();
        const diagnosticMatches = this.resultFilters.diagnosticCode === 'all' ||
            String(record.diagnosticCode || '').toLowerCase() === this.resultFilters.diagnosticCode.toLowerCase();
        return statusMatches && diagnosticMatches;
    }

    getDiagnosticOptions() {
        const values = [
            ...this.monitorResults.map((record) => record.diagnosticCode),
            ...this.globalResults.map((record) => record.diagnosticCode),
            ...(this.monitorStatistics?.byDiagnosticCode || []).map((item) => item.key),
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

    applyRealtimeResultToStatistics(record) {
        if (!this.monitorStatistics) {
            return;
        }

        this.monitorStatistics.total += 1;
        const outcome = normalizeCanonicalOutcome(record);
        if (Object.prototype.hasOwnProperty.call(this.monitorStatistics, outcome.category)) {
            this.monitorStatistics[outcome.category] += 1;
        }
        if (String(outcome.executionOutcome).toLowerCase() === 'succeeded') {
            this.monitorStatistics.executionSucceeded += 1;
        }
        this.monitorStatistics.validDecisions = this.monitorStatistics.ok + this.monitorStatistics.ng;
        this.monitorStatistics.executionFailures = this.monitorStatistics.failed + this.monitorStatistics.timedOut;
        this.monitorStatistics.yieldRate = this.monitorStatistics.validDecisions > 0
            ? this.monitorStatistics.ok / this.monitorStatistics.validDecisions
            : 0;
        this.monitorStatistics.decisionCoverageRate = this.monitorStatistics.executionSucceeded > 0
            ? this.monitorStatistics.validDecisions / this.monitorStatistics.executionSucceeded
            : 0;

        const executionTime = Number(record.executionTimeMs || 0);
        if (executionTime > 0) {
            const totalTime = Number(this.monitorStatistics.averageExecutionTimeMs || 0) * Math.max(0, this.monitorStatistics.total - 1);
            this.monitorStatistics.averageExecutionTimeMs = (totalTime + executionTime) / Math.max(1, this.monitorStatistics.total);
        }

        const diagnosticCode = record.diagnosticCode || 'Unknown';
        const existingDiagnostic = this.monitorStatistics.byDiagnosticCode
            .find((item) => String(item.key).toLowerCase() === String(diagnosticCode).toLowerCase());
        if (existingDiagnostic) {
            existingDiagnostic.count += 1;
        } else {
            this.monitorStatistics.byDiagnosticCode.push({ key: diagnosticCode, count: 1 });
        }
        this.monitorStatistics.byDiagnosticCode.sort((left, right) =>
            right.count - left.count || String(left.key).localeCompare(String(right.key)));

        const bucketKey = this.formatHourBucket(record.completedAtUtc);
        const existingBucket = this.monitorStatistics.hourlyTrend
            .find((item) => this.formatHourBucket(item.time) === bucketKey);
        if (existingBucket) {
            existingBucket.count += 1;
        } else if (record.completedAtUtc) {
            this.monitorStatistics.hourlyTrend.push({ time: record.completedAtUtc, count: 1 });
        }
    }

    dedupeResults(results) {
        const seen = new Set();
        return results.filter((result) => {
            const key = this.getResultRecordKey(result);
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
            const key = this.getResultRecordKey(record);
            if (seen.has(key)) {
                return false;
            }
            seen.add(key);
            return true;
        });
    }

    hasResultRecord(records, record) {
        const key = this.getResultRecordKey(record);
        return (Array.isArray(records) ? records : []).some((item) => this.getResultRecordKey(item) === key);
    }

    getResultRecordKey(record) {
        return `${record?.stationId || ''}:${record?.sequenceId || ''}:${record?.messageId || ''}`;
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
            '工站ID',
            '工站名称',
            '序列号',
            '状态',
            '诊断码',
            '诊断信息',
            '耗时毫秒',
            '完成时间UTC',
            '运行包名称'
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

        if (this._renderContextActive && this._relativeTimeCache.has(value)) {
            return this._relativeTimeCache.get(value);
        }

        const timestamp = new Date(value).getTime();
        if (!Number.isFinite(timestamp)) {
            return '--';
        }

        const deltaSeconds = Math.max(0, Math.floor(((this._renderNowMs || Date.now()) - timestamp) / 1000));
        let result;
        if (deltaSeconds < 5) {
            result = '刚刚';
        } else if (deltaSeconds < 60) {
            result = `${deltaSeconds} 秒前`;
        } else {
            const minutes = Math.floor(deltaSeconds / 60);
            if (minutes < 60) {
                result = `${minutes} 分钟前`;
            } else {
                const hours = Math.floor(minutes / 60);
                if (hours < 24) {
                    result = `${hours} 小时前`;
                } else {
                    const days = Math.floor(hours / 24);
                    result = `${days} 天前`;
                }
            }
        }

        if (this._renderContextActive) {
            this._relativeTimeCache.set(value, result);
        }

        return result;
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

    combineCanonicalStatistics(statisticsList) {
        const fields = [
            'total', 'executionSucceeded', 'validDecisions', 'ok', 'ng', 'undetermined',
            'notApplicable', 'invalid', 'failed', 'cancelled', 'timedOut', 'skipped'
        ];
        const combined = Object.fromEntries(fields.map((field) => [field, 0]));
        (Array.isArray(statisticsList) ? statisticsList : []).forEach((statistics) => {
            fields.forEach((field) => {
                combined[field] += Number(statistics?.[field] ?? 0);
            });
        });
        combined.executionFailures = combined.failed + combined.timedOut;
        combined.yieldRate = combined.validDecisions > 0 ? combined.ok / combined.validDecisions : 0;
        combined.decisionCoverageRate = combined.executionSucceeded > 0
            ? combined.validDecisions / combined.executionSucceeded
            : 0;
        return combined;
    }

    formatOutcome(outcome, inspectionStatus, executionOutcome = null, decisionOutcome = null) {
        return normalizeCanonicalOutcome({ outcome, inspectionStatus, executionOutcome, decisionOutcome }).label;
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
