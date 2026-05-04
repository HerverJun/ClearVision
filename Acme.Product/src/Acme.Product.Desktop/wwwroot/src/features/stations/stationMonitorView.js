import httpClient from '../../core/messaging/httpClient.js';
import { getStoredToken } from '../auth/authStorage.js';
import { buildSseHeaders, parseSseFrame } from '../inspection/inspectionSseClient.mjs';

class StationMonitorView {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.summary = null;
        this.stations = new Map();
        this.globalResults = [];
        this.selectedStationId = null;
        this.selectedStationDetail = null;
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
                <section class="station-monitor-hero">
                    <div class="station-monitor-heading">
                        <div>
                            <p class="station-monitor-kicker">Studio registry</p>
                            <h2 class="station-monitor-title">Station monitor</h2>
                            <p class="station-monitor-subtitle">REST snapshot plus SSE deltas for the live station fleet.</p>
                        </div>
                        <div class="station-monitor-sync" id="station-monitor-sync">
                            <span class="station-monitor-sync-dot"></span>
                            <span id="station-monitor-sync-text">Connecting...</span>
                        </div>
                    </div>
                    <div class="station-monitor-summary-grid" id="station-monitor-summary-grid"></div>
                </section>

                <section class="station-monitor-layout">
                    <div class="station-monitor-stage">
                        <div class="station-monitor-panel">
                            <div class="station-monitor-panel-header">
                                <span>Stations</span>
                                <span id="station-monitor-matrix-meta">0 tracked</span>
                            </div>
                            <div class="station-monitor-matrix" id="station-monitor-matrix"></div>
                        </div>
                    </div>

                    <aside class="station-monitor-rail">
                        <div class="station-monitor-panel station-monitor-focus-panel">
                            <div class="station-monitor-panel-header">
                                <span>Focus</span>
                                <span id="station-monitor-focus-meta">No selection</span>
                            </div>
                            <div id="station-monitor-focus"></div>
                        </div>

                        <div class="station-monitor-panel station-monitor-stream-panel">
                            <div class="station-monitor-panel-header">
                                <span>Recent flow</span>
                                <span id="station-monitor-stream-meta">0 events</span>
                            </div>
                            <div class="station-monitor-stream" id="station-monitor-stream"></div>
                        </div>
                    </aside>
                </section>
            </div>
        `;

        this.summaryGrid = this.container.querySelector('#station-monitor-summary-grid');
        this.matrix = this.container.querySelector('#station-monitor-matrix');
        this.matrixMeta = this.container.querySelector('#station-monitor-matrix-meta');
        this.focus = this.container.querySelector('#station-monitor-focus');
        this.focusMeta = this.container.querySelector('#station-monitor-focus-meta');
        this.stream = this.container.querySelector('#station-monitor-stream');
        this.streamMeta = this.container.querySelector('#station-monitor-stream-meta');
        this.syncText = this.container.querySelector('#station-monitor-sync-text');
        this.syncElement = this.container.querySelector('#station-monitor-sync');
    }

    bindEvents() {
        this.container?.addEventListener('click', (event) => {
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
            const [summary, stations] = await Promise.all([
                httpClient.get('/stations/summary'),
                httpClient.get('/stations')
            ]);

            this.summary = this.normalizeSummary(summary);
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

            this.updateSyncState('Live');
        } catch (error) {
            console.error('[StationMonitorView] Failed to load initial data:', error);
            this.updateSyncState('Retrying');
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

            this.updateSyncState('Retrying');
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

        this.updateSyncState('Streaming');
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
            { label: 'Online', value: `${onlineStations.length}/${stations.length || 0}`, meta: `${stations.length - onlineStations.length} offline`, tone: 'success' },
            { label: 'Alerts', value: String(alerts.length), meta: 'faulted or stale', tone: alerts.length > 0 ? 'warning' : 'default' },
            { label: 'Yield flow', value: `${totalOk} / ${totalNg} / ${totalError}`, meta: 'OK / NG / ERR', tone: 'accent' },
            { label: 'Avg cycle', value: this.formatMilliseconds(avgExecutionTime), meta: `stale after ${this.offlineThresholdSeconds}s`, tone: 'info' }
        ];

        this.summaryGrid.innerHTML = cards.map((card) => `
            <article class="station-summary-card station-summary-card--${card.tone}">
                <span class="station-summary-label">${card.label}</span>
                <strong class="station-summary-value">${card.value}</strong>
                <span class="station-summary-meta">${card.meta}</span>
            </article>
        `).join('');
    }

    renderMatrix() {
        if (!this.matrix || !this.matrixMeta) {
            return;
        }

        const stations = this.getSortedStations();
        this.matrixMeta.textContent = `${stations.length} tracked`;

        if (stations.length === 0) {
            this.matrix.innerHTML = `
                <div class="station-monitor-empty">
                    <strong>No station data yet</strong>
                    <span>Enable Station ingress on Studio and configure Station sync to start the closed loop.</span>
                </div>
            `;
            return;
        }

        this.matrix.innerHTML = stations.map((station) => {
            const isOnline = this.computeIsOnline(station);
            const isSelected = station.stationId === this.selectedStationId;
            const outcomeLabel = this.formatOutcome(station.lastOutcome, station.lastInspectionStatus);
            const stateLabel = this.formatState(station.state, isOnline);

            return `
                <button
                    type="button"
                    class="station-card ${isSelected ? 'is-selected' : ''} ${isOnline ? 'is-online' : 'is-offline'}"
                    data-station-id="${this.escapeHtml(station.stationId)}"
                >
                    <div class="station-card-top">
                        <div>
                            <span class="station-card-id">${this.escapeHtml(station.stationId)}</span>
                            <span class="station-card-line">${this.escapeHtml(station.lineName || station.machineName || 'Unassigned')}</span>
                        </div>
                        <span class="station-card-state">${stateLabel}</span>
                    </div>
                    <div class="station-card-metrics">
                        <span><strong>${Number(station.sessionOkCount || 0)}</strong> OK</span>
                        <span><strong>${Number(station.sessionNgCount || 0)}</strong> NG</span>
                        <span><strong>${Number(station.sessionErrorCount || 0)}</strong> ERR</span>
                    </div>
                    <div class="station-card-bottom">
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
            this.focusMeta.textContent = 'No selection';
            this.focus.innerHTML = `
                <div class="station-monitor-empty">
                    <strong>Select a station</strong>
                    <span>Choose a station card to inspect package, counters and recent results.</span>
                </div>
            `;
            return;
        }

        const detail = this.selectedStationDetail && this.selectedStationDetail.stationId === selectedStation.stationId
            ? this.selectedStationDetail
            : { ...selectedStation, recentResults: [] };
        const isOnline = this.computeIsOnline(selectedStation);
        const recentResults = Array.isArray(detail.recentResults) ? detail.recentResults : [];

        this.focusMeta.textContent = isOnline ? 'Live focus' : 'Stale focus';
        this.focus.innerHTML = `
            <div class="station-focus-hero ${isOnline ? 'is-online' : 'is-offline'}">
                <div>
                    <span class="station-focus-id">${this.escapeHtml(detail.stationId)}</span>
                    <h3>${this.escapeHtml(detail.lineName || detail.machineName || 'Unnamed station')}</h3>
                    <p>${this.escapeHtml(detail.packageName || 'No package loaded')}</p>
                </div>
                <div class="station-focus-pill">${this.formatState(detail.state, isOnline)}</div>
            </div>
            <div class="station-focus-kpis">
                <div><span>OK</span><strong>${Number(detail.sessionOkCount || 0)}</strong></div>
                <div><span>NG</span><strong>${Number(detail.sessionNgCount || 0)}</strong></div>
                <div><span>ERR</span><strong>${Number(detail.sessionErrorCount || 0)}</strong></div>
                <div><span>AVG</span><strong>${this.formatMilliseconds(detail.averageExecutionTimeMs)}</strong></div>
            </div>
            <dl class="station-focus-meta">
                <div><dt>Last seen</dt><dd>${this.escapeHtml(this.formatRelativeTime(detail.lastSeenAtUtc))}</dd></div>
                <div><dt>Last result</dt><dd>${this.escapeHtml(this.formatOutcome(detail.lastOutcome, detail.lastInspectionStatus))}</dd></div>
                <div><dt>Diagnostic</dt><dd>${this.escapeHtml(detail.lastDiagnosticCode || detail.lastDiagnosticMessage || '--')}</dd></div>
                <div><dt>Package</dt><dd>${this.escapeHtml(detail.packageId || '--')}</dd></div>
            </dl>
            <div class="station-focus-results">
                <div class="station-focus-results-header">
                    <span>Recent results</span>
                    <span>${recentResults.length}</span>
                </div>
                ${recentResults.length === 0
                    ? '<div class="station-monitor-empty compact"><span>No results buffered for this station yet.</span></div>'
                    : recentResults.slice(0, 8).map((result) => `
                        <article class="station-result-chip">
                            <div>
                                <strong>${this.escapeHtml(this.formatOutcome(result.outcome, result.inspectionStatus))}</strong>
                                <span>${this.escapeHtml(result.imageId || result.runId || '--')}</span>
                            </div>
                            <div>
                                <span>${this.formatMilliseconds(result.executionTimeMs)}</span>
                                <span>${this.escapeHtml(this.formatRelativeTime(result.completedAtUtc))}</span>
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

        this.streamMeta.textContent = `${this.globalResults.length} buffered`;
        if (this.globalResults.length === 0) {
            this.stream.innerHTML = `
                <div class="station-monitor-empty compact">
                    <span>Recent result summaries will appear here once Station sync starts streaming.</span>
                </div>
            `;
            return;
        }

        this.stream.innerHTML = this.globalResults.slice(0, 12).map((item) => `
            <article class="station-stream-row">
                <div class="station-stream-primary">
                    <span class="station-stream-station">${this.escapeHtml(item.stationId)}</span>
                    <strong>${this.escapeHtml(this.formatOutcome(item.result.outcome, item.result.inspectionStatus))}</strong>
                    <span>${this.escapeHtml(item.result.imageId || item.result.runId || '--')}</span>
                </div>
                <div class="station-stream-secondary">
                    <span>${this.escapeHtml(item.result.diagnosticCode || '--')}</span>
                    <span>${this.formatMilliseconds(item.result.executionTimeMs)}</span>
                    <span>${this.escapeHtml(this.formatRelativeTime(item.result.completedAtUtc))}</span>
                </div>
            </article>
        `).join('');
    }

    updateSyncState(text) {
        if (this.syncText) {
            this.syncText.textContent = text;
        }

        if (this.syncElement) {
            this.syncElement.dataset.state = text.toLowerCase();
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
        return {
            stationId: station?.stationId ?? station?.StationId ?? '--',
            lineName: station?.lineName ?? station?.LineName ?? null,
            machineName: station?.machineName ?? station?.MachineName ?? '',
            clientVersion: station?.clientVersion ?? station?.ClientVersion ?? '',
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
            recentResults: Array.isArray(station?.recentResults)
                ? station.recentResults.map((result) => this.normalizeResult(result))
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

    formatMilliseconds(value) {
        const numeric = Number(value || 0);
        if (!Number.isFinite(numeric) || numeric <= 0) {
            return '--';
        }

        return `${Math.round(numeric)} ms`;
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
            return 'just now';
        }

        if (deltaSeconds < 60) {
            return `${deltaSeconds}s ago`;
        }

        const minutes = Math.floor(deltaSeconds / 60);
        if (minutes < 60) {
            return `${minutes}m ago`;
        }

        const hours = Math.floor(minutes / 60);
        if (hours < 24) {
            return `${hours}h ago`;
        }

        const days = Math.floor(hours / 24);
        return `${days}d ago`;
    }

    formatState(state, isOnline) {
        if (!isOnline) {
            return 'Offline';
        }

        const normalized = String(state || '').trim();
        switch (normalized) {
            case 'Running':
                return 'Running';
            case 'Stopping':
                return 'Stopping';
            case 'Faulted':
                return 'Faulted';
            case 'Loaded':
                return 'Ready';
            default:
                return normalized || 'Idle';
        }
    }

    formatOutcome(outcome, inspectionStatus) {
        const normalizedOutcome = String(outcome || '').trim().toUpperCase();
        if (normalizedOutcome === 'OK') {
            return 'OK';
        }

        if (normalizedOutcome === 'NG') {
            return 'NG';
        }

        if (normalizedOutcome === 'ERROR') {
            return 'Error';
        }

        if (normalizedOutcome === 'CANCELED') {
            return 'Canceled';
        }

        const normalizedStatus = String(inspectionStatus || '').trim().toUpperCase();
        if (normalizedStatus === 'OK' || normalizedStatus === 'NG' || normalizedStatus === 'ERROR') {
            return normalizedStatus === 'ERROR' ? 'Error' : normalizedStatus;
        }

        return 'Pending';
    }

    escapeHtml(value) {
        const div = document.createElement('div');
        div.textContent = String(value ?? '');
        return div.innerHTML;
    }
}

export { StationMonitorView };
