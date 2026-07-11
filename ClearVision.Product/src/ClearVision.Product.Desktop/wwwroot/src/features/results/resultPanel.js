/**
 * 结果面板组件 - 阶段二增强版
 * 现代化数据可视化仪表板
 */

import httpClient from '../../core/messaging/httpClient.js';
import { getStoredToken } from '../auth/authStorage.js';
import { renderDiagnosticsCardsHtml } from '../inspection/analysisCardsPanel.js';
import { buildSseHeaders, buildSseUrl, parseSseFrame } from '../inspection/inspectionSseClient.mjs';
import debugLogger from '../../core/logging/debugLogger.js';
import { t } from '../../core/i18n/resources.js';
import {
    calculateCanonicalStatistics,
    matchesCanonicalOutcomeFilter,
    normalizeCanonicalOutcome,
    normalizeCanonicalStatistics
} from '../inspection/canonicalOutcome.mjs';
import {
    buildResultCardsFromOutputData,
    renderResultCardHtml,
    summarizeResultField
} from './portDataTypeRenderer.mjs';

const LIVE_RESULT_HISTORY_REFRESH_DELAY_MS = 2000;
const LIVE_RESULT_ANALYTICS_REFRESH_DELAY_MS = 5000;
const RESULT_DATA_SOURCE_INSPECTION = 'inspection';
const RESULT_DATA_SOURCE_STATION = 'station';
const LOCAL_RESULT_HISTORY_LIMIT = 500;
const LOCAL_RESULT_INLINE_IMAGE_RETAIN_LIMIT = 12;
const LOCAL_RESULT_PAYLOAD_ARRAY_LIMIT = 24;
const LOCAL_RESULT_PAYLOAD_OBJECT_FIELD_LIMIT = 48;
const LOCAL_RESULT_PAYLOAD_STRING_LIMIT = 512;
const LOCAL_RESULT_PAYLOAD_MAX_DEPTH = 3;
const LOCAL_RESULT_PAYLOAD_IMAGE_KEY_PATTERN = /(image|bitmap|preview|thumbnail|base64|mask)/i;
const DEFAULT_RESULTS_SSE_MAX_FRAME_CHARS = 2 * 1024 * 1024;
const DEFAULT_RESULTS_SSE_MAX_BUFFER_CHARS = 4 * 1024 * 1024;
const RESULT_DETAIL_MAX_ANALYSIS_CARDS = 24;
const RESULT_DETAIL_MAX_STRUCTURED_CARDS = 12;
const RESULT_DETAIL_MAX_FIELDS_PER_CARD = 16;
const RESULT_DETAIL_MAX_RAW_OUTPUT_ROWS = 64;
const RESULT_DETAIL_MAX_DEFECT_ROWS = 50;
const RESULT_DETAIL_MAX_FIELD_VALUE_CHARS = 240;
const RESULT_DETAIL_REMOVE_DELAY_MS = 200;
const RESULT_COMPARISON_MAX_DIFF_ROWS = 80;
const INLINE_RESULT_IMAGE_KEYS = [
    'imageData',
    'ImageData',
    'outputImage',
    'OutputImage',
    'outputImageBase64',
    'OutputImageBase64',
    'resultImageBase64',
    'ResultImageBase64'
];

class ResultPanel {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.results = [];
        this.filteredResults = [];
        this.projectId = null;
        this.serverReport = null;
        this.serverAnalysis = null;
        this.serverAnalysisSource = 'local';
        this.dataSource = RESULT_DATA_SOURCE_INSPECTION;
        this._resultsStreamController = null;
        this._resultsStreamConnectionId = 0;
        this._resultsStreamReconnectAttempt = 0;
        this._resultsStreamReconnectTimer = null;
        this._resultsLastEventId = null;
        this._analyticsRefreshTimer = null;
        this._historyRefreshTimer = null;
        this._renderFrameHandle = null;
        this._renderFrameCancel = null;
        this._eventDisposers = [];
        this._isDisposed = false;
        this._activeDetailModals = new Set();
        this.resultDetailMaxAnalysisCards = RESULT_DETAIL_MAX_ANALYSIS_CARDS;
        this.resultDetailMaxStructuredCards = RESULT_DETAIL_MAX_STRUCTURED_CARDS;
        this.resultDetailMaxFieldsPerCard = RESULT_DETAIL_MAX_FIELDS_PER_CARD;
        this.resultDetailMaxRawOutputRows = RESULT_DETAIL_MAX_RAW_OUTPUT_ROWS;
        this.resultDetailMaxDefectRows = RESULT_DETAIL_MAX_DEFECT_ROWS;
        this.resultDetailMaxFieldValueChars = RESULT_DETAIL_MAX_FIELD_VALUE_CHARS;
        this.resultsSseMaxFrameChars = DEFAULT_RESULTS_SSE_MAX_FRAME_CHARS;
        this.resultsSseMaxBufferChars = DEFAULT_RESULTS_SSE_MAX_BUFFER_CHARS;
        this.statistics = {
            total: 0,
            executionSucceeded: 0,
            validDecisions: 0,
            ok: 0,
            ng: 0,
            executionFailures: 0,
            undetermined: 0,
            notApplicable: 0,
            invalid: 0,
            failed: 0,
            cancelled: 0,
            timedOut: 0,
            skipped: 0,
            yieldRate: 0,
            decisionCoverageRate: 0,
            avgTime: 0
        };
        
        // 分页
        this.currentPage = 1;
        this.pageSize = 12;
        this.totalPages = 1;
        this.totalResultCount = 0;
        this.serverPageIndex = 0;
        this.serverPaged = false;
        this.historyLoader = null;
        this.historyDetailLoader = null;
        this.comparisonLoader = null;
        this.previousSuccessLoader = null;
        this.evidenceExportLoader = null;
        this.comparisonBaseline = null;
        this.comparisonSelection = { left: null, right: null };
        this.latestFormalResult = null;
        
        // 筛选
        this.filters = {
            status: 'all',
            defectType: 'all',
            startTime: null,
            endTime: null
        };
        
        // 时间范围
        this.timeRange = 'today';
        
        // 趋势图数据
        this.trendData = [];
        
        // 缺陷类型统计
        this.defectTypes = {};
        
        // 绑定事件
        this.bindEvents();
        
        debugLogger.debug('[ResultPanel] 结果面板初始化完成');
    }
    
    /**
     * 绑定事件
     */
    createEmptyStatistics() {
        return {
            total: 0,
            executionSucceeded: 0,
            validDecisions: 0,
            ok: 0,
            ng: 0,
            executionFailures: 0,
            undetermined: 0,
            notApplicable: 0,
            invalid: 0,
            failed: 0,
            cancelled: 0,
            timedOut: 0,
            skipped: 0,
            yieldRate: 0,
            decisionCoverageRate: 0,
            avgTime: 0
        };
    }

    bindEvents() {
        this.ensureDataSourceFilter();

        const dataSourceFilter = document.getElementById('filter-data-source');
        if (dataSourceFilter) {
            this.addManagedEventListener(dataSourceFilter, 'change', (e) => {
                this.setDataSource(e.target.value);
            });
        }

        // 时间范围选择
        document.querySelectorAll('.time-range-btn').forEach(btn => {
            this.addManagedEventListener(btn, 'click', (e) => {
                document.querySelectorAll('.time-range-btn').forEach(b => b.classList.remove('active'));
                e.target.classList.add('active');
                this.setTimeRange(e.target.dataset.range);
            });
        });
        
        // 状态筛选
        const statusFilter = document.getElementById('filter-status');
        if (statusFilter) {
            this.addManagedEventListener(statusFilter, 'change', (e) => {
                this.setFilter('status', e.target.value);
            });
        }
        
        // 缺陷类型筛选
        const defectTypeFilter = document.getElementById('filter-defect-type');
        if (defectTypeFilter) {
            this.addManagedEventListener(defectTypeFilter, 'change', (e) => {
                this.setFilter('defectType', e.target.value);
            });
        }
        
        // 导出下拉菜单
        const exportDropdown = document.getElementById('export-dropdown');
        const exportBtn = document.getElementById('btn-export-results');
        if (exportBtn && exportDropdown) {
            this.addManagedEventListener(exportBtn, 'click', () => {
                exportDropdown.classList.toggle('open');
            });
            
            // 导出选项
            exportDropdown.querySelectorAll('.export-menu-item').forEach(item => {
                this.addManagedEventListener(item, 'click', () => {
                    const format = item.dataset.format;
                    this.exportResults(format);
                    exportDropdown.classList.remove('open');
                });
            });
            
            // 点击外部关闭
            this.addManagedEventListener(document, 'click', (e) => {
                if (!exportDropdown.contains(e.target)) {
                    exportDropdown.classList.remove('open');
                }
            });
        }

        // 【后端对接占位符 1】：生成深度报告按钮
        const advancedReportBtn = document.getElementById('btn-advanced-report');
        if (advancedReportBtn) {
            this.addManagedEventListener(advancedReportBtn, 'click', () => {
                this.generatePdfReport(this.getAnalyticsQueryParams());
            });
        }
    }

    addManagedEventListener(target, type, handler, options) {
        if (!target || typeof target.addEventListener !== 'function') {
            return;
        }

        target.addEventListener(type, handler, options);
        this._eventDisposers.push(() => {
            if (typeof target.removeEventListener === 'function') {
                target.removeEventListener(type, handler, options);
            }
        });
    }

    ensureDataSourceFilter() {
        const filterBar = document.getElementById('results-filters-bar');
        if (!filterBar || document.getElementById('filter-data-source')) {
            return;
        }

        const group = document.createElement('div');
        group.className = 'filter-group';
        group.innerHTML = `
            <label>数据源:</label>
            <select class="filter-select" id="filter-data-source">
                <option value="${RESULT_DATA_SOURCE_INSPECTION}">工程追溯</option>
                <option value="${RESULT_DATA_SOURCE_STATION}">Station采集</option>
            </select>
        `;
        filterBar.insertBefore(group, filterBar.firstElementChild);
    }

    setDataSource(source) {
        const normalizedSource = source === RESULT_DATA_SOURCE_STATION
            ? RESULT_DATA_SOURCE_STATION
            : RESULT_DATA_SOURCE_INSPECTION;
        if (this.dataSource === normalizedSource) {
            return;
        }

        this.dataSource = normalizedSource;
        const dataSourceFilter = document.getElementById('filter-data-source');
        if (dataSourceFilter) {
            dataSourceFilter.value = this.dataSource;
        }

        this.serverReport = null;
        this.serverAnalysis = null;
        this.serverAnalysisSource = 'local';
        this.currentPage = 1;
        this.results = [];
        this.filteredResults = [];
        this.trendData = [];
        this.defectTypes = {};
        this.statistics = this.createEmptyStatistics();
        this.totalResultCount = 0;
        this.serverPageIndex = 0;
        this.serverPaged = true;
        this.comparisonBaseline = null;
        this.comparisonSelection = { left: null, right: null };
        this.latestFormalResult = null;

        if (this.dataSource === RESULT_DATA_SOURCE_STATION) {
            this.disconnectResultsStream();
        }

        if (this.historyLoader) {
            this.requestHistoryPage(0).catch(error => {
                debugLogger.warn('[ResultPanel] Failed to switch result data source:', error);
            });
        }

        this.render();
    }
    
    /**
     * 设置时间范围
     */
    setTimeRange(range) {
        this.timeRange = range;
        const { startTime, endTime } = this.getTimeRangeBounds(range);
        this.filters.startTime = startTime;
        this.filters.endTime = endTime;
        this.currentPage = 1;
        
        this.applyFilters();
        this.render();

        if (this.historyLoader && this.canRequestServerData()) {
            this.requestHistoryPage(0).catch(error => {
                debugLogger.warn('[ResultPanel] 刷新服务端历史失败:', error);
            });
        }

        if (this.canRequestServerData()) {
            this.loadServerAnalytics().catch(error => {
                debugLogger.warn('[ResultPanel] 刷新服务端分析失败:', error);
            });
        }
    }
    getTimeRangeBounds(range = this.timeRange) {
        const now = new Date();

        switch (range) {
            case 'today':
                return {
                    startTime: new Date(now.getFullYear(), now.getMonth(), now.getDate()),
                    endTime: now
                };
            case 'week': {
                const weekStart = new Date(now);
                weekStart.setDate(now.getDate() - now.getDay());
                weekStart.setHours(0, 0, 0, 0);
                return {
                    startTime: weekStart,
                    endTime: now
                };
            }
            case 'month':
                return {
                    startTime: new Date(now.getFullYear(), now.getMonth(), 1),
                    endTime: now
                };
            case 'custom':
                return {
                    startTime: this.filters.startTime,
                    endTime: this.filters.endTime
                };
            default:
                return {
                    startTime: null,
                    endTime: null
                };
        }
    }

    setProjectContext(projectId) {
        const normalizedProjectId = projectId || null;
        if (this.projectId !== normalizedProjectId) {
            this.disconnectResultsStream();
            this.projectId = normalizedProjectId;
            this._resultsLastEventId = null;
            this.serverReport = null;
            this.serverAnalysis = null;
            this.serverAnalysisSource = 'local';
            this.totalResultCount = 0;
            this.serverPageIndex = 0;
            this.serverPaged = false;
            this.comparisonBaseline = null;
            this.comparisonSelection = { left: null, right: null };
            this.latestFormalResult = null;
            if (this.projectId && this.dataSource !== RESULT_DATA_SOURCE_STATION) {
                this.connectResultsHub();
            }
        }
    }

    setHistoryLoader(loader) {
        this.historyLoader = typeof loader === 'function' ? loader : null;
    }

    setHistoryDetailLoader(loader) {
        this.historyDetailLoader = typeof loader === 'function' ? loader : null;
    }

    setComparisonLoader(loader) {
        this.comparisonLoader = typeof loader === 'function' ? loader : null;
    }

    setPreviousSuccessLoader(loader) {
        this.previousSuccessLoader = typeof loader === 'function' ? loader : null;
    }

    setEvidenceExportLoader(loader) {
        this.evidenceExportLoader = typeof loader === 'function' ? loader : null;
    }

    canRequestServerData() {
        return this.dataSource === RESULT_DATA_SOURCE_STATION || !!this.projectId;
    }

    hasLocalPageFilters() {
        return !this.serverPaged && (this.filters.status !== 'all' || this.filters.defectType !== 'all');
    }

    isServerPaginationActive() {
        return this.serverPaged && !this.hasLocalPageFilters();
    }

    isClientFilteringServerPage() {
        return this.serverPaged && this.hasLocalPageFilters();
    }

    getVisiblePageResults() {
        return this.serverPaged
            ? this.filteredResults
            : this.filteredResults.slice(
                (this.currentPage - 1) * this.pageSize,
                Math.min(this.currentPage * this.pageSize, this.filteredResults.length)
            );
    }

    getResultsScopeSummary(pageResults = this.getVisiblePageResults()) {
        if (this.isClientFilteringServerPage()) {
            return `当前仅筛选已加载页：本页命中 ${this.filteredResults.length} 条，未覆盖其余 ${Math.max(this.totalResultCount - pageResults.length, 0)} 条历史记录`;
        }

        if (this.serverPaged) {
            return `历史列表：当前页 ${pageResults.length} 条 / 共 ${this.totalResultCount} 条记录`;
        }

        return `共 ${this.filteredResults.length} 条记录`;
    }

    requestHistoryPage(pageIndex = 0) {
        if (this._isDisposed || !this.historyLoader || !this.canRequestServerData()) {
            return Promise.resolve(false);
        }

        return this.historyLoader({
            pageIndex,
            pageSize: this.pageSize,
            dataSource: this.dataSource,
            ...this.getAnalyticsQueryParams()
        });
    }
    getAnalyticsQueryParams() {
        const { startTime, endTime } = this.getTimeRangeBounds(this.timeRange);
        const params = {};

        if (startTime instanceof Date && !Number.isNaN(startTime.getTime())) {
            params.startTime = startTime.toISOString();
        }

        if (endTime instanceof Date && !Number.isNaN(endTime.getTime())) {
            params.endTime = endTime.toISOString();
        }

        if (this.filters.status && this.filters.status !== 'all') {
            params.status = this.filters.status;
        }

        if (this.filters.defectType && this.filters.defectType !== 'all') {
            params.defectType = this.filters.defectType;
        }

        return params;
    }

    queueServerAnalyticsRefresh(delayMs = 800) {
        if (this._isDisposed || !this.canRequestServerData()) {
            return;
        }

        this.clearQueuedAnalyticsRefresh();

        this._analyticsRefreshTimer = window.setTimeout(() => {
            this._analyticsRefreshTimer = null;
            if (this._isDisposed) {
                return;
            }
            this.loadServerAnalytics().catch(error => {
                debugLogger.warn('[ResultPanel] Server analytics refresh failed:', error);
            });
        }, delayMs);
    }

    queueServerHistoryRefresh(delayMs = 400) {
        if (this._isDisposed || !this.historyLoader || !this.canRequestServerData()) {
            return;
        }

        this.clearQueuedHistoryRefresh();

        this._historyRefreshTimer = window.setTimeout(() => {
            this._historyRefreshTimer = null;
            if (this._isDisposed) {
                return;
            }
            this.requestHistoryPage(0).catch(error => {
                debugLogger.warn('[ResultPanel] Server history refresh failed:', error);
            });
        }, delayMs);
    }

    clearQueuedAnalyticsRefresh() {
        if (this._analyticsRefreshTimer) {
            clearTimeout(this._analyticsRefreshTimer);
            this._analyticsRefreshTimer = null;
        }
    }

    clearQueuedHistoryRefresh() {
        if (this._historyRefreshTimer) {
            clearTimeout(this._historyRefreshTimer);
            this._historyRefreshTimer = null;
        }
    }

    clearQueuedRefreshes() {
        this.clearQueuedAnalyticsRefresh();
        this.clearQueuedHistoryRefresh();
    }

    scheduleRender() {
        if (this._isDisposed || this._renderFrameHandle != null) {
            return;
        }

        const scheduleFrame = window.requestAnimationFrame
            ? callback => window.requestAnimationFrame(callback)
            : callback => window.setTimeout(callback, 0);
        this._renderFrameCancel = window.cancelAnimationFrame || window.clearTimeout;
        this._renderFrameHandle = scheduleFrame(() => {
            this._renderFrameHandle = null;
            this._renderFrameCancel = null;
            if (!this._isDisposed) {
                this.render();
            }
        });
    }

    clearQueuedRender() {
        if (this._renderFrameHandle == null) {
            return;
        }

        const cancel = this._renderFrameCancel || window.cancelAnimationFrame || window.clearTimeout;
        cancel.call(window, this._renderFrameHandle);
        this._renderFrameHandle = null;
        this._renderFrameCancel = null;
    }

    normalizeStatistics(statistics) {
        if (!statistics || typeof statistics !== 'object') {
            return null;
        }
        return normalizeCanonicalStatistics(statistics);
    }

    normalizeDefectDistribution(defectDistribution) {
        const items = Array.isArray(defectDistribution)
            ? defectDistribution
            : (defectDistribution?.items || defectDistribution?.Items || []);
        return items.reduce((accumulator, item) => {
            const defectType = item.defectType || item.DefectType || item.diagnosticCode || item.DiagnosticCode || t('common.unknown', '未知');
            const count = item.count ?? item.Count ?? 0;
            accumulator[defectType] = count;
            return accumulator;
        }, {});
    }

    normalizeTrendPoints(trend) {
        const points = Array.isArray(trend)
            ? trend
            : (trend?.dataPoints || trend?.DataPoints || []);
        return points.map(point => ({
            time: new Date(point.timestamp || point.Timestamp || point.hourUtc || point.HourUtc || Date.now()),
            status: (point.executionFailureCount ?? point.ExecutionFailureCount ?? 0) > 0
                ? 'failed'
                : ((point.invalidCount ?? point.InvalidCount ?? 0) > 0
                    ? 'invalid'
                    : ((point.undeterminedCount ?? point.UndeterminedCount ?? 0) > 0
                        ? 'undetermined'
                        : ((point.ngCount ?? point.NGCount ?? 0) > 0 ? 'ng' : 'ok'))),
            defectCount: point.defectCount ?? point.DefectCount ?? 0,
            count: point.totalCount ?? point.TotalCount ?? point.total ?? point.Total ?? 1
        }));
    }

    applyServerAnalysis({ report = null, statistics = null, defectDistribution = null, trend = null } = {}) {
        const normalizedStatistics = this.normalizeStatistics(
            report?.summary || report?.Summary || statistics
        );
        const normalizedDefects = this.normalizeDefectDistribution(
            report?.defectDistribution
            || report?.DefectDistribution
            || defectDistribution
            || statistics?.defectDistribution
            || statistics?.DefectDistribution
            || statistics?.byDiagnosticCode
            || statistics?.ByDiagnosticCode
        );
        const normalizedTrend = this.normalizeTrendPoints(
            report?.hourlyTrend
            || report?.HourlyTrend
            || trend
            || statistics?.hourlyTrend
            || statistics?.HourlyTrend
            || statistics?.trend
            || statistics?.Trend
        );

        if (normalizedStatistics) {
            this.statistics = normalizedStatistics;
        }

        this.defectTypes = normalizedDefects;
        this.updateDefectTypeFilter();

        this.trendData = normalizedTrend;

        this.serverReport = report || this.serverReport;
        this.serverAnalysis = {
            statistics: normalizedStatistics,
            defectTypes: normalizedDefects,
            trendData: normalizedTrend
        };
        this.serverAnalysisSource = 'server';
    }

    async loadServerAnalytics(projectId = this.projectId) {
        if (this.dataSource === RESULT_DATA_SOURCE_STATION) {
            return this.loadStationAnalytics();
        }

        if (!projectId) {
            return;
        }

        const commonParams = this.getAnalyticsQueryParams();

        const reportPromise = httpClient.get(`/analysis/report/${projectId}`, commonParams)
            .catch(error => {
                debugLogger.warn('[ResultPanel] Failed to load analysis report:', error);
                return null;
            });

        const statisticsPromise = httpClient.get(`/analysis/statistics/${projectId}`, commonParams)
            .catch(error => {
                debugLogger.warn('[ResultPanel] Failed to load statistics:', error);
                return null;
            });
        const defectDistributionPromise = httpClient.get(`/analysis/defect-distribution/${projectId}`, commonParams)
            .catch(error => {
                debugLogger.warn('[ResultPanel] 获取缺陷分布失败:', error);
                return null;
            });

        const trendPromise = commonParams.startTime && commonParams.endTime
            ? httpClient.get(`/analysis/trend/${projectId}`, {
                ...commonParams,
                interval: this.timeRange === 'today' ? 'Hour' : 'Day',
            }).catch(error => {
                debugLogger.warn('[ResultPanel] 获取趋势分析失败:', error);
                return null;
            })
            : Promise.resolve(null);

        const [report, statistics, defectDistribution, trend] = await Promise.all([
            reportPromise,
            statisticsPromise,
            defectDistributionPromise,
            trendPromise
        ]);

        if (report || statistics || defectDistribution || trend) {
            this.applyServerAnalysis({ report, statistics, defectDistribution, trend });
            this.render();
            return;
        }

        if (this.serverPaged) {
            this.serverReport = null;
            this.serverAnalysis = null;
            this.serverAnalysisSource = 'server-unavailable';
            this.statistics = this.createEmptyStatistics();
            this.defectTypes = {};
            this.trendData = [];
            this.updateDefectTypeFilter();
            this.render();
            return;
        }

        this.serverAnalysisSource = 'local';

        if (statistics) {
            this.statistics = normalizeCanonicalStatistics(statistics);
        }

        if (defectDistribution?.items || defectDistribution?.Items) {
            const items = defectDistribution.items || defectDistribution.Items || [];
            this.defectTypes = items.reduce((accumulator, item) => {
                const defectType = item.defectType || item.DefectType || t('common.unknown', '未知');
                const count = item.count ?? item.Count ?? 0;
                accumulator[defectType] = count;
                return accumulator;
            }, {});
        }

        if (trend?.dataPoints || trend?.DataPoints) {
            const points = trend.dataPoints || trend.DataPoints || [];
            this.trendData = points.map(point => ({
                time: new Date(point.timestamp || point.Timestamp || Date.now()),
                status: (point.executionFailureCount ?? point.ExecutionFailureCount ?? 0) > 0
                    ? 'failed'
                    : ((point.undeterminedCount ?? point.UndeterminedCount ?? 0) > 0
                        ? 'undetermined'
                        : ((point.ngCount ?? point.NGCount ?? 0) > 0 ? 'ng' : 'ok')),
                defectCount: point.defectCount ?? point.DefectCount ?? 0
            }));
        }

        this.render();
    }

    async loadStationAnalytics() {
        const queryParams = this.getAnalyticsQueryParams();
        const stationParams = {
            range: queryParams.startTime && queryParams.endTime ? undefined : 'all',
            ...(queryParams.startTime ? { from: queryParams.startTime } : {}),
            ...(queryParams.endTime ? { to: queryParams.endTime } : {}),
            ...(queryParams.status ? { status: queryParams.status } : {}),
            ...(queryParams.defectType ? { diagnosticCode: queryParams.defectType } : {})
        };

        Object.keys(stationParams).forEach((key) => {
            if (stationParams[key] === undefined || stationParams[key] === null || stationParams[key] === '') {
                delete stationParams[key];
            }
        });

        const statistics = await httpClient.get('/stations/statistics', stationParams)
            .catch(error => {
                debugLogger.warn('[ResultPanel] Failed to load Station analytics:', error);
                return null;
            });

        if (!statistics) {
            this.serverReport = null;
            this.serverAnalysis = null;
            this.serverAnalysisSource = 'server-unavailable';
            this.render();
            return;
        }

        this.applyServerAnalysis({ statistics });
        this.serverAnalysisSource = 'station';
        this.render();
    }
    
    /**
     * 更新统计
     */
    updateStatistics(stats) {
        this.statistics = { ...this.statistics, ...stats };
        this.renderKPIs();
        this.renderYieldChart();
    }
    
    /**
     * 添加结果
     */
    addResult(result, options = {}) {
        if (this._isDisposed) {
            return;
        }

        const preparedResult = this.prepareResultForLocalHistory(result, 0);
        if (this.dataSource !== RESULT_DATA_SOURCE_STATION && preparedResult) {
            this.latestFormalResult = preparedResult;
        }

        if (this.serverPaged) {
            if (this.projectId) {
                const isRealtime = options?.isRealtime === true;
                this.queueServerHistoryRefresh(isRealtime ? LIVE_RESULT_HISTORY_REFRESH_DELAY_MS : 400);
                this.queueServerAnalyticsRefresh(isRealtime ? LIVE_RESULT_ANALYTICS_REFRESH_DELAY_MS : 800);
            }
            return;
        }

        this.results.unshift(preparedResult);
        this.pruneLocalResultHistory();
        this.applyFilters();
        this.calculateStatistics();
        this.updateTrendData();

        if (this.canRequestServerData()) {
            this.queueServerAnalyticsRefresh();
        }

        this.scheduleRender();
    }
    
    /**
     * 加载历史结果
     */
    loadResults(results, { totalCount = null, pageIndex = 0, pageSize = this.pageSize, serverPaged = false } = {}) {
        const isServerPaged = !!serverPaged;
        this.results = Array.isArray(results)
            ? results.map((result, index) => this.prepareResultForLocalHistory(result, isServerPaged ? 0 : index))
            : [];
        if (this.dataSource !== RESULT_DATA_SOURCE_STATION) {
            this.latestFormalResult = this.results[0] || null;
        }
        this.serverPaged = isServerPaged;
        if (!this.serverPaged) {
            this.pruneLocalResultHistory();
        }
        this.serverPageIndex = Math.max(0, pageIndex);
        this.pageSize = Number.isFinite(pageSize) && pageSize > 0 ? pageSize : this.pageSize;
        this.totalResultCount = Number.isFinite(totalCount) ? totalCount : this.results.length;
        this.currentPage = this.serverPaged ? this.serverPageIndex + 1 : 1;
        this.applyFilters();

        if (!this.serverPaged) {
            this.calculateStatistics();
            this.updateTrendData();
        } else if (this.serverAnalysisSource === 'server') {
            this.updateDefectTypeFilter();
        }

        this.render();
    }

    prepareResultForLocalHistory(result, index = 0) {
        if (!result || typeof result !== 'object') {
            return result;
        }

        const normalized = { ...result };
        const outcome = normalizeCanonicalOutcome(normalized);
        normalized.executionOutcome = outcome.executionOutcome;
        normalized.decisionOutcome = outcome.decisionOutcome;
        normalized.outcomeCategory = outcome.category;
        normalized.outcomeLabel = outcome.label;
        normalized.outcomeTone = outcome.tone;
        normalized.isLegacyOutcomeProjection = outcome.isLegacyProjection;
        this.compactInlineResultImage(normalized);
        this.compactStoredResultPayload(normalized);
        if (index >= LOCAL_RESULT_INLINE_IMAGE_RETAIN_LIMIT) {
            this.stripInlineResultImage(normalized);
        }

        return normalized;
    }

    compactStoredResultPayload(result) {
        if (!result || typeof result !== 'object') {
            return result;
        }

        ['outputData', 'OutputData', 'analysisData', 'AnalysisData'].forEach(key => {
            if (Object.prototype.hasOwnProperty.call(result, key)) {
                result[key] = this.compactStoredResultValue(result[key]);
            }
        });

        ['defects', 'Defects'].forEach(key => {
            if (Array.isArray(result[key]) && result[key].length > LOCAL_RESULT_PAYLOAD_ARRAY_LIMIT) {
                result[key] = [
                    ...result[key]
                        .slice(0, LOCAL_RESULT_PAYLOAD_ARRAY_LIMIT)
                        .map(item => this.compactStoredResultValue(item)),
                    `+${result[key].length - LOCAL_RESULT_PAYLOAD_ARRAY_LIMIT} more`
                ];
            }
        });

        return result;
    }

    compactStoredResultValue(value, depth = 0, seen = new WeakSet(), sourceKey = '') {
        if (typeof value === 'string') {
            return this.compactStoredResultString(sourceKey, value);
        }

        if (value === null || value === undefined || typeof value !== 'object') {
            return value;
        }

        if (seen.has(value)) {
            return '[circular]';
        }

        if (depth >= LOCAL_RESULT_PAYLOAD_MAX_DEPTH) {
            return Array.isArray(value)
                ? `${value.length} items`
                : `${Object.keys(value).length} fields`;
        }

        seen.add(value);

        if (Array.isArray(value)) {
            const visibleItems = value
                .slice(0, LOCAL_RESULT_PAYLOAD_ARRAY_LIMIT)
                .map(item => this.compactStoredResultValue(item, depth + 1, seen, sourceKey));
            if (value.length > visibleItems.length) {
                visibleItems.push(`+${value.length - visibleItems.length} more`);
            }
            return visibleItems;
        }

        const compact = {};
        const entries = Object.entries(value);
        let visibleCount = 0;
        let omittedImageCount = 0;
        for (const [key, entryValue] of entries) {
            if (this.isStoredResultImageLikeValue(key, entryValue)) {
                omittedImageCount += 1;
                continue;
            }

            if (visibleCount >= LOCAL_RESULT_PAYLOAD_OBJECT_FIELD_LIMIT) {
                break;
            }

            compact[key] = this.compactStoredResultValue(entryValue, depth + 1, seen, key);
            visibleCount += 1;
        }

        const hiddenCount = Math.max(0, entries.length - visibleCount - omittedImageCount);
        if (hiddenCount > 0) {
            compact.__hiddenFieldCount = hiddenCount;
        }
        if (omittedImageCount > 0) {
            compact.__omittedImageFieldCount = omittedImageCount;
        }

        return compact;
    }

    compactStoredResultString(key, value) {
        if (this.isStoredResultImageLikeValue(key, value)) {
            return '[image omitted]';
        }

        const text = String(value ?? '');
        return text.length > LOCAL_RESULT_PAYLOAD_STRING_LIMIT
            ? `${text.slice(0, LOCAL_RESULT_PAYLOAD_STRING_LIMIT)}...`
            : text;
    }

    isStoredResultImageLikeValue(key, value) {
        if (typeof value !== 'string') {
            return false;
        }

        const text = value.trim();
        if (text.startsWith('data:image/')) {
            return true;
        }

        return LOCAL_RESULT_PAYLOAD_IMAGE_KEY_PATTERN.test(String(key || '')) && text.length > 120;
    }

    compactInlineResultImage(result) {
        if (!result || typeof result !== 'object') {
            return result;
        }

        const inlineImage = this.getInlineResultImageBase64(result);
        INLINE_RESULT_IMAGE_KEYS.forEach(key => {
            if (key !== 'imageData' && Object.prototype.hasOwnProperty.call(result, key)) {
                result[key] = null;
            }
        });

        if (inlineImage) {
            result.imageData = inlineImage;
        }

        return result;
    }

    stripInlineResultImage(result) {
        if (!result || typeof result !== 'object') {
            return result;
        }

        let stripped = false;
        INLINE_RESULT_IMAGE_KEYS.forEach(key => {
            if (result[key]) {
                result[key] = null;
                stripped = true;
            }
        });

        if (stripped) {
            result.inlineImageDiscarded = true;
        }

        return result;
    }

    pruneLocalResultHistory() {
        if (this.serverPaged || !Array.isArray(this.results)) {
            return;
        }

        if (this.results.length > LOCAL_RESULT_HISTORY_LIMIT) {
            this.results.length = LOCAL_RESULT_HISTORY_LIMIT;
        }

        for (let index = LOCAL_RESULT_INLINE_IMAGE_RETAIN_LIMIT; index < this.results.length; index += 1) {
            this.stripInlineResultImage(this.results[index]);
        }
    }

    /**
     * 计算统计
     */
    calculateStatistics() {
        const canonical = calculateCanonicalStatistics(this.results);
        const validResults = this.results.filter(r => Number(r.processingTimeMs ?? r.processingTime ?? r.executionTimeMs) > 0);
        const totalTime = validResults.reduce((sum, r) => sum + Number(r.processingTimeMs ?? r.processingTime ?? r.executionTimeMs ?? 0), 0);
        const avgTime = validResults.length > 0 ? Math.round(totalTime / validResults.length) : 0;
        this.statistics = { ...canonical, avgTime };
        
        // 重新计算缺陷类型
        this.defectTypes = {};
        this.results.forEach(r => {
            if (r.defects) {
                r.defects.forEach(defect => {
                    const type = defect.type || defect.description || t('common.unknown', '未知');
                    this.defectTypes[type] = (this.defectTypes[type] || 0) + 1;
                });
            }
        });
        
        // 更新缺陷类型下拉框
        this.updateDefectTypeFilter();
    }
    
    /**
     * 更新缺陷类型筛选器
     */
    updateDefectTypeFilter() {
        const select = document.getElementById('filter-defect-type');
        if (!select) return;
        
        const currentValue = select.value;
        select.innerHTML = '<option value="all">全部</option>';
        
        Object.keys(this.defectTypes).forEach(type => {
            const option = document.createElement('option');
            option.value = type;
            option.textContent = `${type} (${this.defectTypes[type]})`;
            select.appendChild(option);
        });
        
        select.value = currentValue;
    }
    
    /**
     * 更新趋势图数据
     */
    updateTrendData() {
        this.trendData = this.results
            .slice(0, 100)
            .map(r => ({
                time: new Date(r.timestamp || Date.now()),
                status: normalizeCanonicalOutcome(r).category,
                defectCount: r.defects?.length || 0
            }))
            .reverse();
    }
    
    applyFilters() {
        if (this.serverPaged) {
            this.filteredResults = [...this.results];
            this.totalPages = Math.ceil(this.totalResultCount / this.pageSize) || 1;
            this.currentPage = this.serverPageIndex + 1;
            return;
        }

        this.filteredResults = this.results.filter(r => {
            // 状态筛选
            if (!matchesCanonicalOutcomeFilter(r, this.filters.status)) {
                return false;
            }
            
            // 缺陷类型筛选
            if (this.filters.defectType !== 'all') {
                const hasDefectType = r.defects?.some(d => 
                    (d.type || d.description || t('common.unknown', '未知')) === this.filters.defectType
                );
                if (!hasDefectType) return false;
            }
            
            // 时间范围筛选
            if (this.filters.startTime) {
                const resultTime = new Date(r.timestamp).getTime();
                if (resultTime < this.filters.startTime.getTime()) {
                    return false;
                }
            }
            
            if (this.filters.endTime) {
                const resultTime = new Date(r.timestamp).getTime();
                if (resultTime > this.filters.endTime.getTime()) {
                    return false;
                }
            }
            
            return true;
        });

        this.totalPages = Math.ceil(this.filteredResults.length / this.pageSize) || 1;
        
        if (this.currentPage > this.totalPages) {
            this.currentPage = this.totalPages;
        }
    }
    
    /**
     * 设置筛选条件
     */
    setFilter(type, value) {
        this.filters[type] = value;
        this.currentPage = 1;
        if (this.serverPaged && this.canRequestServerData()) {
            this.requestHistoryPage(0).catch(error => {
                debugLogger.warn('[ResultPanel] 刷新服务端历史失败:', error);
            });
            this.loadServerAnalytics().catch(error => {
                debugLogger.warn('[ResultPanel] 刷新服务端分析失败:', error);
            });
            return;
        }

        this.applyFilters();
        this.render();
    }
    
    /**
     * 翻页
     */
    goToPage(page) {
        if (page < 1 || page > this.totalPages) return;

        if (this.isServerPaginationActive()) {
            this.requestHistoryPage(page - 1).catch(error => {
                debugLogger.warn('[ResultPanel] 翻页加载服务端历史失败:', error);
            });
            return;
        }

        this.currentPage = page;
        this.render();
    }
    
    /**
     * 清空结果
     */
    clear() {
        this.results = [];
        this.filteredResults = [];
        this.trendData = [];
        this.defectTypes = {};
        this.statistics = this.createEmptyStatistics();
        this.serverReport = null;
        this.serverAnalysis = null;
        this.serverAnalysisSource = 'local';
        if (this._analyticsRefreshTimer) {
            clearTimeout(this._analyticsRefreshTimer);
            this._analyticsRefreshTimer = null;
        }
        if (this._historyRefreshTimer) {
            clearTimeout(this._historyRefreshTimer);
            this._historyRefreshTimer = null;
        }
        this.totalResultCount = 0;
        this.serverPageIndex = 0;
        this.serverPaged = false;
        this.currentPage = 1;
        this.applyFilters();
        this.render();
    }

    getInlineResultImageBase64(result) {
        if (!result) {
            return null;
        }

        for (const key of INLINE_RESULT_IMAGE_KEYS) {
            const value = result[key];
            if (typeof value === 'string' && value.length > 0) {
                return value;
            }
        }

        return null;
    }

    getResultImageSrc(result) {
        if (!result) {
            return '';
        }

        const inlineImage = this.getInlineResultImageBase64(result);
        if (inlineImage) {
            return `data:image/png;base64,${inlineImage}`;
        }

        if (result.imageUrl) {
            return result.imageUrl;
        }

        if (result.imageId) {
            return httpClient.buildRequestUrl(`/images/${result.imageId}`);
        }

        return '';
    }
    
    /**
     * 渲染面板
     */
    render() {
        this.renderKPIs();
        this.renderYieldChart();
        this.renderRadarChart();
        this.renderThroughputChart();
        this.renderAdvancedStats();
        this.renderResultsList();
        this.renderPagination();
    }
    
    /**
     * 渲染KPI卡片 (V3 工业看板风格)
     */
    renderKPIs() {
        const { total, ok, ng, executionFailures, undetermined, yieldRate, decisionCoverageRate, avgTime } = this.statistics;
        const hasSamples = total > 0;
        const yieldText = this.statistics.validDecisions > 0 ? (yieldRate * 100).toFixed(1) : '--';
        const coverageText = this.statistics.executionSucceeded > 0 ? (decisionCoverageRate * 100).toFixed(1) : '--';
        const timeSec = avgTime > 1000 ? (avgTime / 1000).toFixed(1) : avgTime;
        const timeUnit = avgTime > 1000 ? 's' : 'ms';

        const setKPI = (id, value) => {
            const el = document.getElementById(id);
            if (el) el.textContent = value;
        };

        setKPI('kpi-total', total.toLocaleString());
        setKPI('kpi-ok', ok.toLocaleString());
        setKPI('kpi-ng', ng.toLocaleString());
        setKPI('kpi-error', executionFailures.toLocaleString());
        setKPI('kpi-undetermined', undetermined.toLocaleString());
        setKPI('kpi-yield', yieldText === '--' ? '--' : `${yieldText}%`);
        setKPI('kpi-coverage', coverageText === '--' ? '--' : `${coverageText}%`);
        setKPI('kpi-avg-time', hasSamples && avgTime > 0 ? `${timeSec}${timeUnit}` : '--');

        ['kpi-total-change', 'kpi-ok-change', 'kpi-ng-change', 'kpi-error-change', 'kpi-undetermined-change', 'kpi-yield-change', 'kpi-coverage-change', 'kpi-time-change']
            .forEach(id => this.renderUnavailableChange(id));

        // 更新时间戳
        const updateTimeEl = document.getElementById('last-update-time');
        if (updateTimeEl) {
            updateTimeEl.textContent = hasSamples ? `更新于 ${new Date().toLocaleTimeString([], { hour12: false })}` : '暂无更新时间';
        }

        const statusText = document.querySelector('.status-pill-text');
        if (statusText) {
            statusText.textContent = hasSamples ? this.getDashboardDataSourceLabel() : '暂无数据';
        }
    }

    renderUnavailableChange(id) {
        const el = document.getElementById(id);
        if (!el) return;

        const valueEl = el.querySelector('.change-value');
        const refEl = el.querySelector('.change-ref');
        if (valueEl) valueEl.textContent = '--';
        if (refEl) refEl.textContent = '无历史窗口';
        el.classList.remove('up', 'down');
    }

    getDashboardDataSourceLabel() {
        if (this.serverAnalysisSource === 'station' || this.dataSource === RESULT_DATA_SOURCE_STATION) {
            return 'Station采集';
        }

        if (this.serverAnalysisSource === 'server') {
            return '真实数据';
        }

        if (this.serverAnalysisSource === 'server-unavailable') {
            return '接口不可用';
        }

        return this.serverPaged ? '已加载页数据' : '本机视图数据';
    }
    
    /**
     * 渲染良率仪表盘 — 半圆弧 SVG
     */
    renderYieldChart() {
        const { validDecisions, yieldRate } = this.statistics;
        const percentage = (yieldRate * 100).toFixed(1);

        // 更新数值文字
        const gaugeValue = document.getElementById('gauge-percentage');
        if (gaugeValue) gaugeValue.textContent = validDecisions > 0 ? percentage : '--';

        // 状态评级
        const gaugeStatus = document.getElementById('gauge-status');
        if (gaugeStatus) {
            if (validDecisions <= 0) {
                gaugeStatus.textContent = '状态：暂无数据';
            } else {
                let status = '严重';
                if (yieldRate >= 0.95) status = '优秀';
                else if (yieldRate >= 0.85) status = '良好';
                else if (yieldRate >= 0.7) status = '预警';
                gaugeStatus.textContent = `状态：${status}`;
            }
        }

        // 更新 SVG 半圆弧 — 总长度 282.7，按良率比例显示
        const arcFill = document.getElementById('gauge-arc-fill');
        if (arcFill) {
            const totalLength = 282.7;
            const offset = totalLength * (1 - yieldRate);
            arcFill.style.strokeDashoffset = offset;
            arcFill.style.transition = 'stroke-dashoffset 0.8s cubic-bezier(0.4, 0, 0.2, 1)';
        }
    }
    
    /**
     * 渲染缺陷雷达图 — 基于缺陷类型分布计算五维数据
     */
    renderRadarChart() {
        const radarData = document.getElementById('radar-data');
        if (!radarData) return;
        const radarPoints = document.getElementById('radar-points');

        // 五个维度：划伤、脏污、缺角、气泡、其他
        const dimensions = ['划伤', '脏污', '缺角', '气泡', '其他'];
        const types = Object.entries(this.defectTypes);

        if (types.length === 0) {
            radarData.setAttribute('points', '');
            radarData.dataset.state = 'no-data';
            if (radarPoints) {
                radarPoints.innerHTML = '';
            }
            return;
        }

        const maxCount = Math.max(...types.map(([, count]) => count)) || 1;

        // 将缺陷类型映射到五维（简单归一化）
        const getValue = (dim) => {
            const match = types.find(([t]) => t.includes(dim));
            return match ? Math.max(0.15, match[1] / maxCount) : 0.15;
        };

        const values = dimensions.map(d => getValue(d));

        // 雷达图中心 (100,100)，半径方向：上、右上、右下、左下、左上
        // 对应角度: 270°, 342°, 54°, 126°, 198°（从正上方顺时针）
        // 但 SVG 坐标系中，我们手动计算五个顶点的位置
        // R=80 (外圈), R=15 (内圈基线)
        const baseR = 15;
        const maxR = 80;
        const centers = [
            { x: 100, y: 100 - maxR },      // 上 (划伤)
            { x: 100 + maxR * 0.95, y: 100 - maxR * 0.31 }, // 右上 (脏污)
            { x: 100 + maxR * 0.59, y: 100 + maxR * 0.81 }, // 右下 (缺角)
            { x: 100 - maxR * 0.59, y: 100 + maxR * 0.81 }, // 左下 (气泡)
            { x: 100 - maxR * 0.95, y: 100 - maxR * 0.31 }, // 左上 (其他)
        ];

        const points = centers.map((c, i) => {
            const r = baseR + (maxR - baseR) * values[i];
            const ratio = r / maxR;
            return `${100 + (c.x - 100) * ratio},${100 + (c.y - 100) * ratio}`;
        });

        radarData.setAttribute('points', points.join(' '));
        radarData.dataset.state = 'ready';
        if (radarPoints) {
            radarPoints.innerHTML = points
                .map(point => {
                    const [x, y] = point.split(',');
                    return `<circle cx="${x}" cy="${y}" r="4" />`;
                })
                .join('');
        }
    }
    
    /**
     * 渲染吞吐量面积图 — SVG Path
     */
    renderThroughputChart() {
        const areaPath = document.getElementById('throughput-area');
        const linePath = document.getElementById('throughput-line');
        if (!areaPath || !linePath) return;

        if (this.trendData.length < 2) {
            areaPath.setAttribute('d', '');
            linePath.setAttribute('d', '');
            areaPath.dataset.state = 'no-data';
            linePath.dataset.state = 'no-data';
            return;
        }

        // 将趋势数据映射为检测量（每个时间点的总检测数）
        // 按时间分组统计每小时的检测量
        const bucketMap = new Map();
        this.trendData.forEach(p => {
            const hour = new Date(p.time).getHours();
            const count = Math.max(1, Number(p.count ?? 1) || 1);
            bucketMap.set(hour, (bucketMap.get(hour) || 0) + count);
        });

        const buckets = Array.from(bucketMap.entries()).sort((a, b) => a[0] - b[0]);
        if (buckets.length < 2) {
            areaPath.setAttribute('d', '');
            linePath.setAttribute('d', '');
            areaPath.dataset.state = 'no-data';
            linePath.dataset.state = 'no-data';
            return;
        }

        const maxCount = Math.max(...buckets.map(([, c]) => c));
        const width = 400;
        const height = 160;
        const padding = { top: 10, bottom: 10 };
        const chartHeight = height - padding.top - padding.bottom;

        const stepX = width / (buckets.length - 1);

        const getY = (count) => padding.top + chartHeight * (1 - count / maxCount);

        // 构建平滑曲线路径（使用三次贝塞尔）
        let lineD = `M 0 ${getY(buckets[0][1])}`;
        for (let i = 0; i < buckets.length - 1; i++) {
            const x0 = i * stepX;
            const y0 = getY(buckets[i][1]);
            const x1 = (i + 1) * stepX;
            const y1 = getY(buckets[i + 1][1]);
            const cpx1 = x0 + stepX * 0.5;
            const cpx2 = x1 - stepX * 0.5;
            lineD += ` C ${cpx1} ${y0}, ${cpx2} ${y1}, ${x1} ${y1}`;
        }

        const areaD = `${lineD} L ${width} ${height} L 0 ${height} Z`;

        linePath.setAttribute('d', lineD);
        areaPath.setAttribute('d', areaD);
        areaPath.dataset.state = 'ready';
        linePath.dataset.state = 'ready';
    }
    
    /**
     * 渲染结果列表
     */
    renderResultsList() {
        const gridContainer = document.getElementById('results-grid');
        const countInfo = document.getElementById('results-count-info');
        if (!gridContainer) return;

        const pageResults = this.getVisiblePageResults();

        if (countInfo) {
            countInfo.textContent = this.getResultsScopeSummary(pageResults);
        }

        if (pageResults.length === 0) {
            const emptyText = this.isClientFilteringServerPage()
                ? '当前页未命中筛选条件。当前筛选只作用于已加载页，可调整时间范围后重新翻页加载。'
                : (this.serverPaged ? '暂无历史记录' : '暂无检测结果');
            gridContainer.innerHTML = `<p class="empty-text">${emptyText}</p>`;
            return;
        }

        gridContainer.innerHTML = pageResults.map((result, index) => {
            const outcome = normalizeCanonicalOutcome(result);
            const statusClass = this.toCssToken(outcome.category);
            const statusText = this.escapeHtml(outcome.label);
            const time = result.timestamp ? new Date(result.timestamp).toLocaleTimeString() : '--:--:--';
            const processingTime = result.processingTime || result.executionTimeMs || '--';
            const outputDataHtml = this.renderAnalysisDataPreview(result.analysisData);
            const historyHint = this.serverPaged
                ? `<span class="result-defect-count">${result.hasOutputData || result.hasAnalysisData ? '检测详情' : '运行摘要'}</span>`
                : '';

            return `
                <div class="result-card result-${statusClass}" data-index="${index}" style="cursor:pointer;">
                    <div class="result-card-header">
                        <span class="result-status-badge ${statusClass}">${statusText}</span>
                        <span class="result-time">${time}</span>
                    </div>
                    <div class="result-card-body">
                        <span class="result-processing-time">${this.escapeHtml(processingTime)}ms</span>
                        ${result.defects?.length > 0 ? `<span class="result-defect-count">${result.defects.length} 缺陷</span>` : ''}
                        ${historyHint}
                        ${outputDataHtml}
                    </div>
                </div>
            `;
        }).join('');

        gridContainer.querySelectorAll('.result-card').forEach(card => {
            card.addEventListener('click', (e) => {
                const index = parseInt(e.currentTarget.dataset.index, 10);
                const result = pageResults[index];
                if (result) {
                    this.showResultDetail(result);
                }
            });
        });
    }
    
    /**
     * 渲染分页控件
     */
    renderPagination() {
        const paginationContainer = document.getElementById('results-pagination');
        if (!paginationContainer) return;

        if (this.isClientFilteringServerPage()) {
            paginationContainer.innerHTML = `
                <div class="empty-text" style="margin:0; text-align:center;">
                    当前筛选仅作用于已加载页，分页已暂停以避免误认为正在筛选全量历史。
                </div>
            `;
            return;
        }
        
        if (this.totalPages <= 1) {
            paginationContainer.innerHTML = '';
            return;
        }
        
        let pageButtons = '';
        const maxVisiblePages = 5;
        let startPage = Math.max(1, this.currentPage - Math.floor(maxVisiblePages / 2));
        let endPage = Math.min(this.totalPages, startPage + maxVisiblePages - 1);
        
        if (endPage - startPage < maxVisiblePages - 1) {
            startPage = Math.max(1, endPage - maxVisiblePages + 1);
        }
        
        // 上一页
        pageButtons += `<button class="page-btn ${this.currentPage === 1 ? 'disabled' : ''}" 
            ${this.currentPage === 1 ? 'disabled' : ''} data-page="${this.currentPage - 1}">«</button>`;
        
        if (startPage > 1) {
            pageButtons += `<button class="page-btn" data-page="1">1</button>`;
            if (startPage > 2) pageButtons += `<span class="page-ellipsis">...</span>`;
        }
        
        for (let i = startPage; i <= endPage; i++) {
            pageButtons += `<button class="page-btn ${i === this.currentPage ? 'active' : ''}" data-page="${i}">${i}</button>`;
        }
        
        if (endPage < this.totalPages) {
            if (endPage < this.totalPages - 1) pageButtons += `<span class="page-ellipsis">...</span>`;
            pageButtons += `<button class="page-btn" data-page="${this.totalPages}">${this.totalPages}</button>`;
        }
        
        // 下一页
        pageButtons += `<button class="page-btn ${this.currentPage === this.totalPages ? 'disabled' : ''}" 
            ${this.currentPage === this.totalPages ? 'disabled' : ''} data-page="${this.currentPage + 1}">»</button>`;
        
        paginationContainer.innerHTML = pageButtons;
        
        // 绑定分页事件
        paginationContainer.querySelectorAll('.page-btn:not(.disabled)').forEach(btn => {
            btn.addEventListener('click', () => {
                const page = parseInt(btn.dataset.page);
                if (page) this.goToPage(page);
            });
        });
    }
    
    /**
     * 导出结果
     */
    exportResults(format = 'json') {
        if (this.isClientFilteringServerPage()) {
            window.alert('当前导出仅包含已加载页中的筛选结果，并非全量历史记录。');
        }

        const exportContext = this.getExportPayload(format);
        if (exportContext) {
            window.alert('将导出服务端生成的追溯报告，包含当前时间范围、筛选条件、记录总数和数据来源。');
            const blob = new Blob([exportContext.content], { type: exportContext.mimeType });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = exportContext.filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
            return;
        }

        if (this.projectId && this.serverPaged) {
            window.alert('服务端报告尚未就绪，当前结果页不再回退导出本地页数据。请稍后重试或先确认服务端分析链路正常。');
            return;
        }

        if (this.filteredResults.length === 0) {
            alert('没有可导出的结果');
            return;
        }
        
        let content, filename, mimeType;
        const filenamePrefix = this.isClientFilteringServerPage()
            ? 'inspection_results_current_page'
            : 'inspection_results';
        const exportMetadata = this.buildClientExportMetadata(format);
        
        switch (format) {
            case 'json':
                content = JSON.stringify({
                    metadata: exportMetadata,
                    records: this.filteredResults
                }, null, 2);
                filename = `${filenamePrefix}_${Date.now()}.json`;
                mimeType = 'application/json';
                break;
            case 'csv':
            case 'excel':
                content = this.convertToCSV(this.filteredResults, exportMetadata);
                filename = `${filenamePrefix}_${Date.now()}.csv`;
                mimeType = 'text/csv';
                break;
            default:
                throw new Error(`不支持的导出格式: ${format}`);
        }
        
        const blob = new Blob([content], { type: mimeType });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }
    
    /**
     * 转换为 CSV
     */
    getExportPayload(format = 'json') {
        const timestamp = Date.now();
        const report = this.serverReport;

        if (report && this.projectId) {
            if (format === 'json') {
                return {
                    content: JSON.stringify(report, null, 2),
                    filename: `inspection_report_server_${timestamp}.json`,
                    mimeType: 'application/json'
                };
            }

            if (format === 'csv' || format === 'excel') {
                return {
                    content: this.convertReportToCSV(report),
                    filename: `inspection_report_server_${timestamp}.csv`,
                    mimeType: 'text/csv'
                };
            }
        }

        return null;
    }

    buildClientExportMetadata(format = 'json') {
        const { startTime, endTime } = this.getTimeRangeBounds();
        return {
            dataSource: this.dataSource === RESULT_DATA_SOURCE_STATION
                ? 'station-ingest'
                : (this.serverPaged ? 'server-page-loaded-in-browser' : 'browser-current-results'),
            exportScope: this.isClientFilteringServerPage() ? '当前已加载页' : '当前视图筛选结果',
            format,
            exportedAt: new Date().toISOString(),
            timeRange: this.timeRange,
            startTime: startTime instanceof Date ? startTime.toISOString() : '',
            endTime: endTime instanceof Date ? endTime.toISOString() : '',
            statusFilter: this.filters.status,
            defectTypeFilter: this.filters.defectType,
            loadedCount: this.results.length,
            filteredCount: this.filteredResults.length,
            totalServerCount: this.serverPaged ? this.totalResultCount : this.filteredResults.length
        };
    }

    convertToCSV(results, metadata = null) {
        const headers = ['时间', '状态', '缺陷数', '处理时间(ms)', '置信度'];
        const rows = results.map(r => [
            r.timestamp ? new Date(r.timestamp).toISOString() : '',
            r.status,
            r.defects?.length || 0,
            r.processingTime || r.executionTimeMs || '',
            r.defects?.[0]?.confidenceScore ? (r.defects[0].confidenceScore * 100).toFixed(1) + '%' : ''
        ]);

        const metadataRows = metadata
            ? [
                ['导出范围', metadata.exportScope],
                ['数据来源', metadata.dataSource],
                ['导出时间', metadata.exportedAt],
                ['时间范围', metadata.timeRange],
                ['开始时间', metadata.startTime],
                ['结束时间', metadata.endTime],
                ['状态筛选', metadata.statusFilter],
                ['缺陷筛选', metadata.defectTypeFilter],
                ['已加载记录数', metadata.loadedCount],
                ['本次导出记录数', metadata.filteredCount],
                ['服务端总记录数', metadata.totalServerCount],
                []
            ]
            : [];

        return [
            ...metadataRows.map(row => row.length === 0 ? '' : this.toCsvRow(row)),
            this.toCsvRow(headers),
            ...rows.map(row => this.toCsvRow(row))
        ].join('\n');
    }

    toCsvRow(fields) {
        return fields.map(value => this.escapeCsvField(value)).join(',');
    }

    escapeCsvField(value) {
        let text = value === null || value === undefined ? '' : String(value);
        if (/^[=+\-@]/.test(text)) {
            text = `'${text}`;
        }

        if (/[",\r\n]/.test(text)) {
            return `"${text.replace(/"/g, '""')}"`;
        }

        return text;
    }
    
    /**
     * 显示结果详情
     */
    convertReportToCSV(report) {
        const summary = report?.summary || report?.Summary || {};
        const period = report?.period || report?.Period || {};
        const recommendations = report?.recommendations || report?.Recommendations || [];
        const defectItems = report?.defectDistribution?.items
            || report?.defectDistribution?.Items
            || report?.DefectDistribution?.Items
            || [];
        const trendItems = report?.hourlyTrend?.dataPoints
            || report?.hourlyTrend?.DataPoints
            || report?.HourlyTrend?.DataPoints
            || [];

        const lines = [
            this.toCsvRow(['Section', 'Key', 'Value']),
            this.toCsvRow(['Summary', 'ProjectId', report?.projectId || report?.ProjectId || this.projectId || '']),
            this.toCsvRow(['Summary', 'GeneratedAt', report?.generatedAt || report?.GeneratedAt || '']),
            this.toCsvRow(['Summary', 'StartTime', period.startTime || period.StartTime || '']),
            this.toCsvRow(['Summary', 'EndTime', period.endTime || period.EndTime || '']),
            this.toCsvRow(['Summary', 'TotalCount', summary.totalCount ?? summary.TotalCount ?? 0]),
            this.toCsvRow(['Summary', 'OKCount', summary.okCount ?? summary.OKCount ?? 0]),
            this.toCsvRow(['Summary', 'NGCount', summary.ngCount ?? summary.NGCount ?? 0]),
            this.toCsvRow(['Summary', 'ErrorCount', summary.errorCount ?? summary.ErrorCount ?? 0]),
            this.toCsvRow(['Summary', 'AverageProcessingTimeMs', summary.averageProcessingTimeMs ?? summary.AverageProcessingTimeMs ?? 0])
        ];

        defectItems.forEach(item => {
            lines.push(this.toCsvRow(['DefectDistribution', item.defectType || item.DefectType || t('common.unknown', '未知'), item.count ?? item.Count ?? 0]));
        });

        trendItems.forEach(point => {
            lines.push(this.toCsvRow(['Trend', point.timestamp || point.Timestamp || '', point.totalCount ?? point.TotalCount ?? 0]));
        });

        recommendations.forEach((recommendation, index) => {
            lines.push(this.toCsvRow(['Recommendation', index + 1, recommendation]));
        });

        return lines.join('\n');
    }

    getDetailLimit(value, fallback) {
        const numericValue = Number(value);
        return Number.isFinite(numericValue) && numericValue >= 0
            ? Math.floor(numericValue)
            : fallback;
    }

    getResultDetailLimits() {
        return {
            analysisCards: this.getDetailLimit(this.resultDetailMaxAnalysisCards, RESULT_DETAIL_MAX_ANALYSIS_CARDS),
            structuredCards: this.getDetailLimit(this.resultDetailMaxStructuredCards, RESULT_DETAIL_MAX_STRUCTURED_CARDS),
            fieldsPerCard: this.getDetailLimit(this.resultDetailMaxFieldsPerCard, RESULT_DETAIL_MAX_FIELDS_PER_CARD),
            rawOutputRows: this.getDetailLimit(this.resultDetailMaxRawOutputRows, RESULT_DETAIL_MAX_RAW_OUTPUT_ROWS),
            defects: this.getDetailLimit(this.resultDetailMaxDefectRows, RESULT_DETAIL_MAX_DEFECT_ROWS),
            fieldValueChars: this.getDetailLimit(this.resultDetailMaxFieldValueChars, RESULT_DETAIL_MAX_FIELD_VALUE_CHARS)
        };
    }

    closeActiveDetailModals({ immediate = true } = {}) {
        if (!this._activeDetailModals || typeof this._activeDetailModals.forEach !== 'function') {
            return;
        }

        Array.from(this._activeDetailModals).forEach(handle => {
            try {
                handle.close({ immediate });
            } catch (error) {
                debugLogger.warn('[ResultPanel] Failed to close result detail modal:', error);
            }
        });
    }

    showResultDetail(result) {
        if (this._isDisposed) {
            return;
        }

        this.closeActiveDetailModals({ immediate: true });

        debugLogger.debug('[ResultPanel] 查看结果详情:', result);
        
        const modal = document.createElement('div');
        modal.className = 'result-detail-modal';
        
        const outcome = normalizeCanonicalOutcome(result);
        const statusClass = this.toCssToken(outcome.category);
        const statusText = this.escapeHtml(outcome.label);
        const time = result.timestamp ? new Date(result.timestamp).toLocaleString() : '--';
        const processingTime = result.processingTime || result.executionTimeMs || '--';
        const imageSrc = this.getResultImageSrc(result);
        const shouldLoadHistoryDetail = this.shouldLoadHistoryDetail(result);
        
        modal.innerHTML = `
            <div class="result-detail-overlay"></div>
            <div class="result-detail-content">
                <div class="result-detail-header">
                    <h3>${this.serverPaged ? '正式检测历史 · 检测详情' : '检测详情'}</h3>
                    <span class="result-status-badge ${statusClass}" style="font-size:12px;padding:4px 12px;">${statusText}</span>
                    <button class="result-detail-close">✕</button>
                </div>
                <div class="result-detail-body">
                    ${this.renderResultDetailBody(result, { imageSrc, statusClass, time, processingTime, isLoading: shouldLoadHistoryDetail })}
                </div>
            </div>
        `;
        
        document.body.appendChild(modal);
        const scheduleFrame = typeof window.requestAnimationFrame === 'function'
            ? callback => window.requestAnimationFrame(callback)
            : (typeof globalThis.requestAnimationFrame === 'function'
                ? callback => globalThis.requestAnimationFrame(callback)
                : callback => window.setTimeout(callback, 0));
        scheduleFrame(() => modal.classList.add('visible'));

        const closeButton = modal.querySelector('.result-detail-close');
        const overlay = modal.querySelector('.result-detail-overlay');
        const body = modal.querySelector('.result-detail-body');
        let removeTimer = null;
        let closed = false;
        let listenersAttached = true;
        let currentDetailResult = result;

        const cleanupListeners = () => {
            if (!listenersAttached) {
                return;
            }

            closeButton?.removeEventListener?.('click', closeModal);
            overlay?.removeEventListener?.('click', closeModal);
            listenersAttached = false;
        };

        const removeModal = () => {
            if (removeTimer !== null) {
                window.clearTimeout(removeTimer);
                removeTimer = null;
            }

            cleanupListeners();
            modal.remove?.();
            this._activeDetailModals?.delete(detailModalHandle);
        };

        const closeModal = ({ immediate = false } = {}) => {
            if (closed) {
                if (immediate) {
                    removeModal();
                }
                return;
            }

            closed = true;
            modal.classList.remove('visible');
            cleanupListeners();
            if (immediate) {
                removeModal();
                return;
            }

            removeTimer = window.setTimeout(removeModal, RESULT_DETAIL_REMOVE_DELAY_MS);
        };

        const detailModalHandle = { close: closeModal };
        if (!this._activeDetailModals) {
            this._activeDetailModals = new Set();
        }
        this._activeDetailModals.add(detailModalHandle);
        closeButton?.addEventListener?.('click', closeModal);
        overlay?.addEventListener?.('click', closeModal);
        this.attachHistoryComparisonControls(body, currentDetailResult);

        if (shouldLoadHistoryDetail) {
            this.historyDetailLoader(result)
                .then(detail => {
                    if (closed || this._isDisposed || !body) {
                        return;
                    }

                    const loadedResult = detail && typeof detail === 'object'
                        ? { ...result, ...detail, historyDetailLoaded: true }
                        : { ...result, historyDetailLoaded: true };
                    currentDetailResult = loadedResult;
                    body.innerHTML = this.renderResultDetailBody(loadedResult);
                    this.attachHistoryComparisonControls(body, currentDetailResult);
                })
                .catch(error => {
                    if (closed || this._isDisposed || !body) {
                        return;
                    }

                    body.innerHTML = this.renderResultDetailBody(result, {
                        errorMessage: error?.message || '检测详情加载失败'
                    });
                });
        }
    }

    shouldLoadHistoryDetail(result) {
        return this.serverPaged &&
            this.dataSource !== RESULT_DATA_SOURCE_STATION &&
            typeof this.historyDetailLoader === 'function' &&
            result?.historyDetailLoaded !== true &&
            !!(result?.id || result?.resultId) &&
            !!result?.projectId;
    }

    renderResultDetailBody(result, options = {}) {
        const outcome = normalizeCanonicalOutcome(result);
        const statusClass = options.statusClass || this.toCssToken(outcome.category);
        const time = options.time || (result?.timestamp ? new Date(result.timestamp).toLocaleString() : '--');
        const processingTime = options.processingTime || result?.processingTime || result?.executionTimeMs || '--';
        const imageSrc = options.imageSrc !== undefined ? options.imageSrc : this.getResultImageSrc(result);

        if (options.errorMessage) {
            return `
                <div class="result-detail-data">
                    <div class="detail-section">
                        <div class="detail-section-title">检测详情</div>
                        <div class="detail-item type-null"><span class="detail-label">error</span><span class="detail-value">${this.escapeHtml(options.errorMessage)}</span></div>
                    </div>
                </div>
            `;
        }

        const loadingHtml = options.isLoading
            ? `<div class="detail-section"><div class="detail-section-title">检测详情</div><div class="detail-item type-null"><span class="detail-label">loading</span><span class="detail-value">正在加载检测详情...</span></div></div>`
            : '';

        return `
            ${imageSrc ? `<div class="result-detail-image"><img src="${imageSrc}" alt="检测结果图像" /></div>` : ''}
            <div class="result-detail-data">
                <div class="detail-section">
                    <div class="detail-section-title">运行摘要</div>
                    <div class="detail-item"><span class="detail-label">结果</span><span class="detail-value status-${statusClass}">${this.escapeHtml(outcome.label)}</span></div>
                    <div class="detail-item"><span class="detail-label">执行</span><span class="detail-value">${this.escapeHtml(outcome.executionOutcome)}</span></div>
                    <div class="detail-item"><span class="detail-label">判定</span><span class="detail-value">${this.escapeHtml(outcome.decisionOutcome)}</span></div>
                    <div class="detail-item"><span class="detail-label">时间</span><span class="detail-value">${this.escapeHtml(time)}</span></div>
                    <div class="detail-item"><span class="detail-label">处理耗时</span><span class="detail-value">${this.escapeHtml(processingTime)}ms</span></div>
                </div>
                ${this.renderHistoryTraceabilitySection(result)}
                ${this.renderHistoryImageReferenceSection(result, imageSrc)}
                ${this.renderHistoryEvidenceSection(result)}
                ${this.renderHistoryComparisonSection(result)}
                ${loadingHtml}
                ${this.renderJsonPreviewNotice('输出数据', result?.outputDataPreview)}
                ${this.renderJsonPreviewNotice('分析数据', result?.analysisDataPreview)}
                ${this.renderAnalysisDataSection(result?.analysisData)}
                ${this.renderStructuredOutputSection(result?.outputData, result?.status)}
                ${this.renderDiagnosticsSection(result?.outputData, result?.status)}
                ${this.renderOutputDataTable(result?.outputData)}
                ${this.renderDefectsSection(result?.defects)}
            </div>
        `;
    }

    renderHistoryTraceabilitySection(result) {
        if (!this.serverPaged || !result) {
            return '';
        }

        const legacy = '旧数据未记录';
        const rows = [
            ['FlowVersionHash', result.flowVersionHash || legacy],
            ['CalibrationBundleId', result.calibrationBundleId || legacy],
            ['SessionId / RunId', result.sessionId || result.runId || legacy]
        ];

        return `
            <div class="detail-section">
                <div class="detail-section-title">追溯信息</div>
                ${rows.map(([label, value]) => `<div class="detail-item"><span class="detail-label">${this.escapeHtml(label)}</span><span class="detail-value">${this.escapeHtml(value)}</span></div>`).join('')}
            </div>
        `;
    }

    renderHistoryImageReferenceSection(result, imageSrc) {
        if (!this.serverPaged || !result) {
            return '';
        }

        const missing = result.imageMissing || (result.hasImage && !imageSrc && !result.imageId && !result.imageReference);
        const value = missing
            ? (result.imageMissingMessage || '图像文件不存在或已清理')
            : (result.imageReference || result.imageId || '本次结果未记录');

        return `
            <div class="detail-section">
                <div class="detail-section-title">${missing ? '图像缺失' : '图像引用'}</div>
                <div class="detail-item"><span class="detail-label">image</span><span class="detail-value">${this.escapeHtml(value)}</span></div>
            </div>
        `;
    }

    renderHistoryEvidenceSection(result) {
        if (!this.serverPaged || !result || this.dataSource === RESULT_DATA_SOURCE_STATION) {
            return '';
        }

        const status = result.evidenceStatus || (result.hasEvidenceManifest ? 'available' : 'missing');
        const statusLabel = this.describeEvidenceStatus(status);
        const message = result.evidenceMessage || this.describeEvidenceMessage(status);
        const manifestReference = result.evidenceManifestReference || '证据清单缺失或已清理';
        const totalBytes = Number.isFinite(Number(result.evidenceTotalBytes))
            ? `${Number(result.evidenceTotalBytes).toLocaleString()} bytes`
            : '--';
        const expiresAt = result.retentionExpiresAtUtc
            ? new Date(result.retentionExpiresAtUtc).toLocaleString()
            : '--';
        const canExport = typeof this.evidenceExportLoader === 'function' &&
            (status === 'available' || status === 'partial');

        return `
            <div class="detail-section history-evidence-section">
                <div class="detail-section-title">证据清单</div>
                <div class="detail-item"><span class="detail-label">状态</span><span class="detail-value">${this.escapeHtml(statusLabel)}</span></div>
                <div class="detail-item"><span class="detail-label">manifest</span><span class="detail-value">${this.escapeHtml(manifestReference)}</span></div>
                <div class="detail-item"><span class="detail-label">totalBytes</span><span class="detail-value">${this.escapeHtml(totalBytes)}</span></div>
                <div class="detail-item"><span class="detail-label">expires</span><span class="detail-value">${this.escapeHtml(expiresAt)}</span></div>
                <div class="detail-item type-null"><span class="detail-label">message</span><span class="detail-value">${this.escapeHtml(message)}</span></div>
                <div class="history-comparison-actions">
                    <button type="button" class="btn btn-sm" data-evidence-export-action="export" ${canExport ? '' : 'disabled'}>导出证据</button>
                </div>
                <div class="history-evidence-output" aria-live="polite"></div>
            </div>
        `;
    }

    describeEvidenceStatus(status) {
        switch (String(status || '').toLowerCase()) {
            case 'available':
                return 'available / 可用';
            case 'partial':
                return 'partial / 部分缺失';
            case 'expired':
                return 'expired / 已过期';
            case 'disabled':
                return 'disabled / 未启用';
            case 'missing':
            default:
                return 'missing / 缺失或已清理';
        }
    }

    describeEvidenceMessage(status) {
        switch (String(status || '').toLowerCase()) {
            case 'available':
                return '证据清单可用';
            case 'partial':
                return '部分证据文件缺失，摘要仍可查看';
            case 'expired':
                return '证据已过期或被留存策略清理，摘要仍可查看';
            case 'disabled':
                return '证据采集未启用';
            case 'missing':
            default:
                return '证据清单缺失或已清理，摘要仍可查看';
        }
    }

    renderHistoryComparisonSection(result) {
        if (this.dataSource === RESULT_DATA_SOURCE_STATION) {
            return '';
        }

        const resultId = this.getResultComparisonId(result);
        const hasResult = !!resultId;
        const hasComparisonLoader = typeof this.comparisonLoader === 'function';
        const hasPreviousSuccessLoader = typeof this.previousSuccessLoader === 'function';
        const baseline = this.comparisonBaseline;
        const current = this.getLatestFormalComparisonResult();
        const hasCurrent = !!(current && this.getResultComparisonId(current));
        const hasBaseline = !!(baseline && this.getResultComparisonId(baseline));
        const selectedLeft = this.comparisonSelection?.left;
        const selectedRight = this.comparisonSelection?.right;
        const hasSelectionPair = !!(
            selectedLeft &&
            selectedRight &&
            this.getResultComparisonId(selectedLeft) &&
            this.getResultComparisonId(selectedRight)
        );
        const isBaselineThisResult = hasBaseline &&
            resultId &&
            this.getResultComparisonId(baseline) === resultId;
        const failureLike = this.isFailureLikeResult(result);

        const baselineRows = hasBaseline
            ? [
                ['resultId', baseline.resultId || baseline.id || '--'],
                ['时间', this.formatComparisonTime(baseline.timestamp || baseline.inspectionTime)],
                ['status', baseline.status || '--'],
                ['FlowVersionHash', baseline.flowVersionHash || '旧数据未记录'],
                ['CalibrationBundleId', baseline.calibrationBundleId || '旧数据未记录']
            ].map(([label, value]) =>
                `<div class="history-comparison-meta-row"><span>${this.escapeHtml(label)}</span><strong>${this.escapeHtml(value)}</strong></div>`
            ).join('')
            : '<div class="history-comparison-empty">无基线</div>';

        const selectionText = [
            `左侧：${this.escapeHtml(this.describeComparisonAnchor(selectedLeft))}`,
            `右侧：${this.escapeHtml(this.describeComparisonAnchor(selectedRight))}`
        ].join(' / ');

        return `
            <div class="detail-section history-comparison-section">
                <div class="detail-section-title">结果对比</div>
                ${!hasResult ? '<div class="detail-item type-null"><span class="detail-label">state</span><span class="detail-value">未选择结果</span></div>' : ''}
                <div class="history-comparison-toolbar">
                    <button type="button" class="history-compare-btn" data-history-compare-action="${isBaselineThisResult ? 'clear-baseline' : 'set-baseline'}" ${hasResult ? '' : 'disabled'}>
                        ${isBaselineThisResult ? '取消基线' : '固定为基线'}
                    </button>
                    <button type="button" class="history-compare-btn" data-history-compare-action="compare-baseline" ${hasResult && hasBaseline && hasComparisonLoader ? '' : 'disabled'}>与基线对比</button>
                    <button type="button" class="history-compare-btn" data-history-compare-action="compare-current" ${hasResult && hasCurrent && hasComparisonLoader ? '' : 'disabled'}>与当前结果对比</button>
                    <button type="button" class="history-compare-btn" data-history-compare-action="previous-success" ${hasResult && failureLike && hasPreviousSuccessLoader ? '' : 'disabled'}>查找失败前成功</button>
                    <button type="button" class="history-compare-btn" data-history-compare-action="select-left" ${hasResult ? '' : 'disabled'}>作为左侧</button>
                    <button type="button" class="history-compare-btn" data-history-compare-action="select-right" ${hasResult ? '' : 'disabled'}>作为右侧</button>
                    <button type="button" class="history-compare-btn" data-history-compare-action="compare-selected" ${hasSelectionPair && hasComparisonLoader ? '' : 'disabled'}>对比选中结果</button>
                </div>
                <div class="history-comparison-meta">
                    <div class="history-comparison-meta-title">固定基线</div>
                    ${baselineRows}
                    <div class="history-comparison-selection">${selectionText}</div>
                </div>
                <div class="history-comparison-output" aria-live="polite">
                    <div class="history-comparison-empty">选择一个对比动作</div>
                </div>
            </div>
        `;
    }

    attachHistoryComparisonControls(container, result) {
        if (!container || typeof container.querySelectorAll !== 'function') {
            return;
        }

        container.querySelectorAll('[data-evidence-export-action]').forEach(button => {
            button.addEventListener('click', () => {
                this.runEvidenceExport(container, result);
            });
        });

        container.querySelectorAll('[data-history-compare-action]').forEach(button => {
            button.addEventListener('click', () => {
                const action = button.dataset.historyCompareAction;
                this.handleHistoryComparisonAction(container, result, action);
            });
        });
    }

    async runEvidenceExport(container, result) {
        const output = container?.querySelector?.('.history-evidence-output');
        const resultId = this.getResultComparisonId(result);
        if (!resultId) {
            if (output) {
                output.innerHTML = '<div class="history-comparison-error">缺少证据导出上下文</div>';
            }
            return;
        }

        if (typeof this.evidenceExportLoader !== 'function') {
            if (output) {
                output.innerHTML = '<div class="history-comparison-error">证据导出服务未接入</div>';
            }
            return;
        }

        if (output) {
            output.innerHTML = '<div class="history-comparison-loading">正在导出证据...</div>';
        }

        try {
            const exported = await this.evidenceExportLoader(result);
            if (exported?.blob) {
                this.downloadEvidenceBlob(exported.blob, exported.filename || `inspection-evidence-${resultId}.json`);
            }

            if (output) {
                const checksum = exported?.sha256 ? ` SHA-256: ${exported.sha256}` : '';
                output.innerHTML = `<div class="history-comparison-message is-ok">证据导出已生成。${this.escapeHtml(checksum)}</div>`;
            }
        } catch (error) {
            if (output) {
                output.innerHTML = `<div class="history-comparison-error">${this.escapeHtml(error?.message || '证据导出失败')}</div>`;
            }
        }
    }

    downloadEvidenceBlob(blob, filename) {
        if (typeof URL === 'undefined' || typeof document === 'undefined') {
            return;
        }

        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    handleHistoryComparisonAction(container, result, action) {
        if (!result || !action) {
            this.setHistoryComparisonOutput(container, '<div class="history-comparison-empty">未选择结果</div>');
            return;
        }

        switch (action) {
            case 'set-baseline':
                this.comparisonBaseline = this.toComparisonAnchor(result);
                this.replaceHistoryComparisonSection(container, result);
                return;
            case 'clear-baseline':
                this.comparisonBaseline = null;
                this.replaceHistoryComparisonSection(container, result);
                return;
            case 'select-left':
                this.comparisonSelection = {
                    ...this.comparisonSelection,
                    left: this.toComparisonAnchor(result)
                };
                this.replaceHistoryComparisonSection(container, result);
                return;
            case 'select-right':
                this.comparisonSelection = {
                    ...this.comparisonSelection,
                    right: this.toComparisonAnchor(result)
                };
                this.replaceHistoryComparisonSection(container, result);
                return;
            case 'compare-baseline':
                this.runHistoryComparison(container, result, this.comparisonBaseline, result, '与基线对比');
                return;
            case 'compare-current':
                this.runHistoryComparison(container, result, this.getLatestFormalComparisonResult(), result, '与当前结果对比');
                return;
            case 'compare-selected':
                this.runHistoryComparison(
                    container,
                    result,
                    this.comparisonSelection?.left,
                    this.comparisonSelection?.right,
                    '历史结果对比');
                return;
            case 'previous-success':
                this.runPreviousSuccessComparison(container, result);
                return;
            default:
                this.setHistoryComparisonOutput(container, '<div class="history-comparison-empty">未选择结果</div>');
        }
    }

    replaceHistoryComparisonSection(container, result) {
        const section = container?.querySelector?.('.history-comparison-section');
        if (!section) {
            return;
        }

        section.outerHTML = this.renderHistoryComparisonSection(result);
        this.attachHistoryComparisonControls(container, result);
    }

    async runHistoryComparison(container, contextResult, left, right, title) {
        const leftId = this.getResultComparisonId(left);
        const rightId = this.getResultComparisonId(right);
        if (!leftId || !rightId) {
            this.setHistoryComparisonOutput(container, '<div class="history-comparison-empty">未选择结果</div>');
            return;
        }

        if (typeof this.comparisonLoader !== 'function') {
            this.setHistoryComparisonOutput(container, '<div class="history-comparison-error">结果对比服务未接入</div>');
            return;
        }

        this.setHistoryComparisonOutput(container, '<div class="history-comparison-loading">正在加载结果对比...</div>');

        try {
            const comparison = await this.comparisonLoader({
                left,
                right,
                leftId,
                rightId,
                contextResult
            });
            this.setHistoryComparisonOutput(container, this.renderHistoryComparisonResult(comparison, title));
        } catch (error) {
            this.setHistoryComparisonOutput(
                container,
                `<div class="history-comparison-error">${this.escapeHtml(error?.message || '结果对比加载失败')}</div>`
            );
        }
    }

    async runPreviousSuccessComparison(container, result) {
        if (typeof this.previousSuccessLoader !== 'function') {
            this.setHistoryComparisonOutput(container, '<div class="history-comparison-error">失败前成功查询未接入</div>');
            return;
        }

        if (!this.isFailureLikeResult(result)) {
            this.setHistoryComparisonOutput(container, '<div class="history-comparison-empty">当前结果不是失败/NG</div>');
            return;
        }

        this.setHistoryComparisonOutput(container, '<div class="history-comparison-loading">正在查找失败前成功...</div>');

        try {
            const reference = await this.previousSuccessLoader(result);
            let html = this.renderPreviousSuccessReference(reference);
            const referenceSummary = reference?.referenceSummary || reference?.ReferenceSummary;
            if (reference?.found === true && referenceSummary && typeof this.comparisonLoader === 'function') {
                try {
                    const comparison = await this.comparisonLoader({
                        left: referenceSummary,
                        right: result,
                        leftId: this.getResultComparisonId(referenceSummary),
                        rightId: this.getResultComparisonId(result),
                        contextResult: result
                    });
                    html += this.renderHistoryComparisonResult(comparison, '失败结果 vs 失败前最近一次成功结果');
                } catch (compareError) {
                    html += `<div class="history-comparison-error">${this.escapeHtml(compareError?.message || '失败前成功对比加载失败')}</div>`;
                }
            }

            this.setHistoryComparisonOutput(container, html);
        } catch (error) {
            this.setHistoryComparisonOutput(
                container,
                `<div class="history-comparison-error">${this.escapeHtml(error?.message || '查找失败前成功失败')}</div>`
            );
        }
    }

    setHistoryComparisonOutput(container, html) {
        const output = container?.querySelector?.('.history-comparison-output');
        if (output) {
            output.innerHTML = html;
        }
    }

    renderPreviousSuccessReference(reference) {
        if (!reference || typeof reference !== 'object') {
            return '<div class="history-comparison-empty">未找到失败前成功参考</div>';
        }

        const found = reference.found === true || reference.Found === true;
        const message = reference.message || reference.Message || (found ? '已找到失败前成功参考' : '未找到失败前成功参考');
        const warnings = this.normalizeComparisonWarnings(reference.warnings || reference.Warnings);
        const summary = reference.referenceSummary || reference.ReferenceSummary;
        const fallback = reference.isFlowVersionFallback === true || reference.IsFlowVersionFallback === true;

        return `
            <div class="history-comparison-reference">
                <div class="history-comparison-subtitle">查找失败前成功</div>
                <div class="history-comparison-message ${found ? 'is-ok' : 'is-empty'}">${this.escapeHtml(message)}</div>
                ${fallback ? '<div class="history-comparison-warning">流程版本不一致，对比仅供参考</div>' : ''}
                ${warnings.map(warning => `<div class="history-comparison-warning">${this.escapeHtml(warning)}</div>`).join('')}
                ${summary ? this.renderComparisonSummaryPair(summary, null) : ''}
            </div>
        `;
    }

    renderHistoryComparisonResult(comparison, title = '结果对比') {
        if (!comparison || typeof comparison !== 'object') {
            return '<div class="history-comparison-empty">暂无对比结果</div>';
        }

        const warnings = this.getComparisonWarningMessages(comparison);
        const traceabilityDiff = comparison.traceabilityDiff || comparison.TraceabilityDiff || [];
        const fieldDiffs = comparison.fieldDiffs || comparison.FieldDiffs || [];
        const diffs = [...traceabilityDiff, ...fieldDiffs];
        const visibleDiffs = diffs.slice(0, RESULT_COMPARISON_MAX_DIFF_ROWS);
        const hiddenCount = Math.max(0, diffs.length - visibleDiffs.length);
        const scene = comparison.sceneReplayAvailability || comparison.SceneReplayAvailability || null;
        const image = comparison.imageReplayAvailability || comparison.ImageReplayAvailability || null;

        return `
            <div class="history-comparison-result">
                <div class="history-comparison-subtitle">${this.escapeHtml(title)}</div>
                ${this.renderComparisonSummaryPair(comparison.leftSummary || comparison.LeftSummary, comparison.rightSummary || comparison.RightSummary)}
                ${warnings.map(warning => `<div class="history-comparison-warning">${this.escapeHtml(warning)}</div>`).join('')}
                ${scene ? this.renderReplayAvailability(scene, 'Scene replay') : ''}
                ${image ? this.renderReplayAvailability(image, '图像回放') : ''}
                ${visibleDiffs.length > 0 ? `
                    <div class="history-comparison-diff-list">
                        ${visibleDiffs.map(diff => this.renderComparisonDiffRow(diff)).join('')}
                        ${hiddenCount > 0 ? `<div class="history-comparison-empty">Hidden ${hiddenCount} more diff rows</div>` : ''}
                    </div>
                ` : '<div class="history-comparison-empty">暂无字段差异</div>'}
            </div>
        `;
    }

    renderComparisonSummaryPair(left, right) {
        if (!left && !right) {
            return '';
        }

        const columns = [
            ['左侧', left],
            ['右侧', right]
        ].filter(([, summary]) => !!summary);

        return `
            <div class="history-comparison-summary-grid">
                ${columns.map(([label, summary]) => `
                    <div class="history-comparison-summary">
                        <div class="history-comparison-meta-title">${this.escapeHtml(label)}</div>
                        <div>${this.escapeHtml(this.describeComparisonAnchor(summary))}</div>
                        <div>${this.escapeHtml(summary?.executionOutcome || summary?.ExecutionOutcome || '--')} / ${this.escapeHtml(summary?.decisionOutcome || summary?.DecisionOutcome || '--')} · ${this.escapeHtml(this.formatComparisonTime(summary?.timestamp || summary?.inspectionTime || summary?.InspectionTime))}</div>
                        <div>FlowVersionHash: ${this.escapeHtml(summary?.flowVersionHash || summary?.FlowVersionHash || '旧数据未记录')}</div>
                        <div>CalibrationBundleId: ${this.escapeHtml(summary?.calibrationBundleId || summary?.CalibrationBundleId || '旧数据未记录')}</div>
                    </div>
                `).join('')}
            </div>
        `;
    }

    renderReplayAvailability(availability, label) {
        const message = availability.message || availability.Message || '';
        const mode = availability.mode || availability.Mode || 'summary-only';
        const left = availability.leftSummary || availability.LeftSummary || availability.leftReference || availability.LeftReference || '--';
        const right = availability.rightSummary || availability.RightSummary || availability.rightReference || availability.RightReference || '--';

        return `
            <div class="history-comparison-replay">
                <strong>${this.escapeHtml(label)}</strong>
                <span>${this.escapeHtml(mode)}</span>
                <div>${this.escapeHtml(message || '暂无 Scene evidence，已降级为摘要回放')}</div>
                <div>左侧：${this.escapeHtml(left)}</div>
                <div>右侧：${this.escapeHtml(right)}</div>
            </div>
        `;
    }

    renderComparisonDiffRow(diff) {
        const diffType = diff?.diffType || diff?.DiffType || 'Unknown';
        const severity = diff?.severity || diff?.Severity || 'info';
        const path = diff?.path || diff?.Path || '';
        const label = diff?.label || diff?.Label || path || '--';
        const left = diff?.leftValuePreview ?? diff?.LeftValuePreview ?? '--';
        const right = diff?.rightValuePreview ?? diff?.RightValuePreview ?? '--';
        const message = diff?.message || diff?.Message || '';

        return `
            <div class="history-comparison-diff-row history-diff-${this.toCssToken(diffType)} severity-${this.toCssToken(severity)}">
                <div class="history-comparison-diff-head">
                    <span>${this.escapeHtml(diffType)}</span>
                    <code>${this.escapeHtml(path)}</code>
                </div>
                <div class="history-comparison-diff-label">${this.escapeHtml(label)}</div>
                <div class="history-comparison-diff-values">
                    <span>${this.escapeHtml(left)}</span>
                    <span>${this.escapeHtml(right)}</span>
                </div>
                ${message ? `<div class="history-comparison-diff-message">${this.escapeHtml(message)}</div>` : ''}
            </div>
        `;
    }

    getComparisonWarningMessages(comparison) {
        const compatibility = comparison.compatibility || comparison.Compatibility || {};
        const warnings = this.normalizeComparisonWarnings(comparison.warnings || comparison.Warnings);
        if (compatibility.flowVersionCompatible === false || compatibility.FlowVersionCompatible === false) {
            warnings.push('流程版本不一致，对比仅供参考');
        }
        if (compatibility.calibrationBundleCompatible === false || compatibility.CalibrationBundleCompatible === false) {
            warnings.push('标定资产不一致，空间坐标对比可能无效');
        }
        if (compatibility.onlySafePreviewComparison === true || compatibility.OnlySafePreviewComparison === true) {
            warnings.push('仅比较安全预览字段');
        }

        return Array.from(new Set(warnings.filter(Boolean)));
    }

    normalizeComparisonWarnings(warnings) {
        return Array.isArray(warnings)
            ? warnings.map(warning => String(warning || '')).filter(Boolean)
            : [];
    }

    getLatestFormalComparisonResult() {
        return this.latestFormalResult || this.results?.[0] || null;
    }

    toComparisonAnchor(result) {
        if (!result || typeof result !== 'object') {
            return null;
        }

        return {
            id: this.getResultComparisonId(result),
            resultId: this.getResultComparisonId(result),
            projectId: result.projectId || result.ProjectId || this.projectId || null,
            status: normalizeCanonicalOutcome(result).label,
            executionOutcome: result.executionOutcome || result.ExecutionOutcome || null,
            decisionOutcome: result.decisionOutcome || result.DecisionOutcome || null,
            timestamp: result.timestamp || result.inspectionTime || result.InspectionTime || result.Timestamp || null,
            processingTimeMs: result.processingTimeMs || result.processingTime || result.ProcessingTimeMs || result.ExecutionTimeMs || null,
            flowVersionHash: result.flowVersionHash || result.FlowVersionHash || result.traceability?.flowVersionHash || null,
            calibrationBundleId: result.calibrationBundleId || result.CalibrationBundleId || result.traceability?.calibrationBundleId || null,
            sessionId: result.sessionId || result.runId || result.SessionId || result.RunId || null
        };
    }

    getResultComparisonId(result) {
        return result?.resultId || result?.id || result?.ResultId || result?.Id || null;
    }

    describeComparisonAnchor(result) {
        const resultId = this.getResultComparisonId(result);
        if (!resultId) {
            return '未选择结果';
        }

        const status = normalizeCanonicalOutcome(result).label;
        const time = this.formatComparisonTime(result?.timestamp || result?.inspectionTime || result?.InspectionTime);
        return `${resultId} · ${status} · ${time}`;
    }

    formatComparisonTime(value) {
        if (!value) {
            return '--';
        }

        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
    }

    isFailureLikeResult(result) {
        return ['ng', 'invalid', 'failed', 'timedOut'].includes(normalizeCanonicalOutcome(result).category);
    }

    renderJsonPreviewNotice(title, preview) {
        if (!preview || typeof preview !== 'object') {
            return '';
        }

        const wasTruncated = preview.wasTruncated === true || preview.WasTruncated === true;
        const wasRedacted = preview.wasRedacted === true || preview.WasRedacted === true;
        const error = preview.error || preview.Error || '';
        const message = preview.message || preview.Message || '';
        if (!wasTruncated && !wasRedacted && !error && !message) {
            return '';
        }

        const parts = [];
        if (wasTruncated) {
            parts.push('大 JSON 已截断');
        }
        if (wasRedacted) {
            parts.push('敏感字段已脱敏');
        }
        if (error) {
            parts.push(error);
        }
        if (message) {
            parts.push(message);
        }

        return `
            <div class="detail-section">
                <div class="detail-section-title">${this.escapeHtml(title)}</div>
                <div class="detail-item type-null"><span class="detail-label">preview</span><span class="detail-value">${this.escapeHtml(parts.join('；'))}</span></div>
            </div>
        `;
    }
    
    renderAnalysisDataPreview(analysisData) {
        const cards = Array.isArray(analysisData?.cards) ? analysisData.cards : [];
        if (cards.length === 0) {
            return '';
        }

        const items = cards.slice(0, 3).map(card => {
            const summary = this.getAnalysisCardSummary(card);
            return `<div class="output-data-item output-text">
                <span class="output-label">${this.escapeHtml(card.title || card.category || '分析卡片')}</span>
                <span class="output-value" title="${this.escapeHtml(summary)}">${this.escapeHtml(summary.length > 30 ? summary.substring(0, 30) + '...' : summary)}</span>
            </div>`;
        });

        return items.length > 0 ? `<div class="output-data-preview">${items.join('')}</div>` : '';
    }
    
    /**
     * 渲染输出数据表格（详情弹窗内完整展示）
     */
    renderStructuredOutputSection(outputData, fallbackStatus) {
        const cards = buildResultCardsFromOutputData(outputData, { status: fallbackStatus || 'OK' });
        if (cards.length === 0) {
            return '';
        }

        const { structuredCards, fieldsPerCard } = this.getResultDetailLimits();
        const visibleCards = cards.slice(0, structuredCards);
        const hiddenCardCount = Math.max(0, cards.length - visibleCards.length);
        const hiddenHint = hiddenCardCount > 0
            ? `<div class="detail-item type-null"><span class="detail-label">More</span><span class="detail-value">Hidden ${hiddenCardCount} more structured cards</span></div>`
            : '';

        return `
            <div class="detail-section">
                <div class="detail-section-title">结构化输出</div>
                <div class="analysis-cards-container ac-diagnostics-inline ac-diagnostics-detail">
                    ${visibleCards.map(card => renderResultCardHtml(card, { fallbackStatus, maxFields: fieldsPerCard })).join('')}
                </div>
                ${hiddenHint}
            </div>
        `;
    }

    renderOutputDataTable(outputData) {
        if (!outputData || typeof outputData !== 'object' || Object.keys(outputData).length === 0) return '';
        
        const rows = [];
        let hiddenCount = 0;
        const { rawOutputRows } = this.getResultDetailLimits();
        for (const [key, value] of Object.entries(outputData)) {
            if (this.shouldHideOutputDetailEntry(key, value, outputData)) {
                hiddenCount += 1;
                continue;
            }

            if (rows.length >= rawOutputRows) {
                hiddenCount += 1;
                continue;
            }
            
            let displayValue = '';
            let typeClass = '';
            
            if (typeof value === 'string') {
                displayValue = this.escapeHtml(value);
                typeClass = 'type-string';
            } else if (typeof value === 'number') {
                displayValue = Number.isInteger(value) ? String(value) : value.toFixed(4);
                typeClass = 'type-number';
            } else if (typeof value === 'boolean') {
                displayValue = value ? '✓ True' : '✗ False';
                typeClass = value ? 'type-bool-true' : 'type-bool-false';
            } else if (value === null || value === undefined) {
                displayValue = '--';
                typeClass = 'type-null';
            } else {
                displayValue = this.escapeHtml(JSON.stringify(value).substring(0, 100));
                typeClass = 'type-object';
            }
            
            rows.push(`<div class="detail-item ${typeClass}"><span class="detail-label">${this.escapeHtml(key)}</span><span class="detail-value">${displayValue}</span></div>`);
        }
        
        if (rows.length === 0 && hiddenCount === 0) return '';

        const hiddenNotice = hiddenCount > 0
            ? `<div class="detail-item type-null"><span class="detail-label">More</span><span class="detail-value">Hidden ${hiddenCount} output fields</span></div>`
            : '';

        return `<div class="detail-section"><div class="detail-section-title">输出数据</div>${rows.join('')}${hiddenNotice}</div>`;
    }

    renderAnalysisDataSection(analysisData) {
        const sourceCards = Array.isArray(analysisData?.cards) ? analysisData.cards : [];
        if (sourceCards.length === 0) {
            return '';
        }

        const { analysisCards, fieldsPerCard } = this.getResultDetailLimits();
        const cards = sourceCards.slice(0, analysisCards);
        const sections = cards.map(card => {
            const sourceFields = Array.isArray(card?.fields) ? card.fields : [];
            const fields = sourceFields.slice(0, fieldsPerCard);
            const rows = fields.map(field => `<div class="detail-item">
                <span class="detail-label">${this.escapeHtml(field.label || field.key || '--')}</span>
                <span class="detail-value">${this.escapeHtml(this.formatAnalysisFieldValue(field.value))}${field.unit ? ` ${this.escapeHtml(field.unit)}` : ''}</span>
            </div>`).join('');
            const hiddenFieldCount = Math.max(0, sourceFields.length - fields.length);
            const hiddenFieldsHint = hiddenFieldCount > 0
                ? `<div class="detail-item type-null"><span class="detail-label">More</span><span class="detail-value">Hidden ${hiddenFieldCount} more fields</span></div>`
                : '';

            return `
                <div class="detail-section">
                    <div class="detail-section-title">${this.escapeHtml(card.title || card.category || '分析数据')}</div>
                    ${rows || '<div class="detail-item"><span class="detail-label">内容</span><span class="detail-value">--</span></div>'}
                    ${hiddenFieldsHint}
                </div>
            `;
        }).join('');

        const hiddenCardCount = Math.max(0, sourceCards.length - cards.length);
        const hiddenCardsHint = hiddenCardCount > 0
            ? `<div class="detail-section"><div class="detail-item type-null"><span class="detail-label">More</span><span class="detail-value">Hidden ${hiddenCardCount} more analysis cards</span></div></div>`
            : '';

        return `${sections}${hiddenCardsHint}`;
    }

    renderDiagnosticsSection(outputData, fallbackStatus) {
        if (!outputData || typeof outputData !== 'object') {
            return '';
        }

        const diagnosticsHtml = renderDiagnosticsCardsHtml(outputData, fallbackStatus || 'OK', {
            containerClass: 'analysis-cards-container ac-diagnostics-inline ac-diagnostics-detail',
            maxFields: this.getResultDetailLimits().fieldsPerCard
        });

        if (!diagnosticsHtml) {
            return '';
        }

        return `
            <div class="detail-section">
                <div class="detail-section-title">诊断面板</div>
                ${diagnosticsHtml}
            </div>
        `;
    }

    renderDefectsSection(defects) {
        if (!Array.isArray(defects) || defects.length === 0) {
            return '';
        }

        const { defects: maxDefects } = this.getResultDetailLimits();
        const visibleDefects = defects.slice(0, maxDefects);
        const rows = visibleDefects.map(defect => `
            <div class="detail-item">
                <span class="detail-label">${this.escapeHtml(defect.type || defect.description || t('common.unknown', '未知'))}</span>
                <span class="detail-value">${defect.confidenceScore ? (defect.confidenceScore * 100).toFixed(1) + '%' : '--'}</span>
            </div>
        `).join('');
        const hiddenCount = Math.max(0, defects.length - visibleDefects.length);
        const hiddenNotice = hiddenCount > 0
            ? `<div class="detail-item type-null"><span class="detail-label">More</span><span class="detail-value">Hidden ${hiddenCount} more defects</span></div>`
            : '';

        return `
            <div class="detail-section">
                <div class="detail-section-title">缺陷列表 (${defects.length})</div>
                ${rows || '<div class="detail-item"><span class="detail-label">Defects</span><span class="detail-value">--</span></div>'}
                ${hiddenNotice}
            </div>
        `;
    }

    getAnalysisCardSummary(card) {
        const fields = Array.isArray(card?.fields) ? card.fields : [];
        const firstField = fields.find(field => field && field.value !== undefined && field.value !== null);
        if (!firstField) {
            return card?.status || '--';
        }

        const label = firstField.label || firstField.key || '值';
        const value = summarizeResultField({
            key: firstField.key || firstField.label || label,
            value: firstField.value,
            unit: firstField.unit,
            dataType: firstField.dataType || firstField.DataType
        });
        return `${label}: ${value}`;
    }

    truncateDetailText(value, maxChars = this.getResultDetailLimits().fieldValueChars) {
        const text = String(value ?? '');
        if (!Number.isFinite(maxChars) || maxChars <= 0 || text.length <= maxChars) {
            return text;
        }

        return `${text.slice(0, maxChars)}...`;
    }

    formatAnalysisFieldValue(value) {
        let text = '';
        if (typeof value === 'number') {
            text = Number.isInteger(value) ? String(value) : value.toFixed(3);
            return this.truncateDetailText(text);
        }

        if (typeof value === 'boolean') {
            return value ? 'True' : 'False';
        }

        if (value === null || value === undefined) {
            return '--';
        }

        if (typeof value === 'object') {
            try {
                text = JSON.stringify(value);
            } catch {
                text = '[unserializable object]';
            }
            return this.truncateDetailText(text);
        }

        return this.truncateDetailText(value);
    }

    isMeaningfulRecognitionText(value, outputData, sourceKey = '') {
        if (typeof value !== 'string') {
            return false;
        }

        const text = value.trim();
        if (!text || text.length >= 200) {
            return false;
        }

        return !this.isStructuredExportText(text, outputData, sourceKey);
    }

    isStructuredExportText(value, outputData, sourceKey = '') {
        const text = String(value || '').trim();
        if (!text) {
            return false;
        }

        if (this.isExportMetadataKey(sourceKey)) {
            return true;
        }

        const looksLikeStructuredPayload =
            (text.startsWith('{') && text.endsWith('}')) ||
            (text.startsWith('[') && text.endsWith(']'));
        if (!looksLikeStructuredPayload) {
            return false;
        }

        const exportHintKeys = ['Format', 'format', 'SaveToFile', 'saveToFile', 'Output', 'output', 'FilePath', 'filePath', 'SaveError', 'saveError'];
        const hasExportHints = Object.keys(outputData || {}).some(key => exportHintKeys.includes(key));
        if (hasExportHints) {
            return true;
        }

        return text.includes('"Format"')
            || text.includes('"SaveToFile"')
            || text.includes('"FilePath"')
            || text.includes('"SaveError"');
    }

    isExportMetadataKey(key) {
        return [
            'format',
            'savetofile',
            'output',
            'filepath',
            'saveerror',
            'success'
        ].includes(String(key || '').toLowerCase());
    }

    isTechnicalCollectionKey(key) {
        return [
            'detectionlist',
            'objects',
            'defects',
            'rawcandidatecount',
            'visualizationdetectioncount',
            'internalnmsenabled',
            'visualizationdetections'
        ].includes(String(key || '').toLowerCase());
    }

    shouldHideOutputDetailEntry(key, value, outputData) {
        const normalizedKey = String(key || '').toLowerCase();
        if (normalizedKey === 'image' || normalizedKey === 'originalimage') {
            return true;
        }

        if (this.isExportMetadataKey(normalizedKey)) {
            return true;
        }

        if (this.isTechnicalCollectionKey(normalizedKey)) {
            return true;
        }

        if (typeof value === 'string') {
            if (value.length > 500) {
                return true;
            }

            if (this.isStructuredExportText(value, outputData, key)) {
                return true;
            }
        }

        return false;
    }
    
    /**
     * HTML转义
     */
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = String(text);
        return div.innerHTML;
    }

    toCssToken(value) {
        const token = String(value ?? 'unknown').toLowerCase().replace(/[^a-z0-9_-]/g, '-');
        return token || 'unknown';
    }
    
    /**
     * 获取最新结果
     */
    getLatestResult() {
        return this.filteredResults[0] || null;
    }
    
    /**
     * 获取所有结果
     */
    getAllResults() {
        return [...this.filteredResults];
    }

    // ==========================================================================
    // 【后端对接占位符】高级功能扩展
    // ==========================================================================

    async connectResultsHub() {
        if (this._isDisposed || !this.projectId || this._resultsStreamController) {
            return;
        }

        const url = httpClient.buildRequestUrl(`/inspection/realtime/${encodeURIComponent(this.projectId)}/events`);
        const token = getStoredToken();
        const controller = new AbortController();
        const connectionId = ++this._resultsStreamConnectionId;

        this._resultsStreamController = controller;
        this._resultsStreamReconnectAttempt = 0;
        this.runResultsStreamWithReconnect(url, token, controller.signal, connectionId)
            .catch((error) => {
                if (error?.name !== 'AbortError') {
                    debugLogger.warn('[ResultPanel] Results SSE stream failed:', error);
                }
            });
    }

    async runResultsStreamWithReconnect(url, token, signal, connectionId) {
        while (!signal.aborted && this.isActiveResultsStream(connectionId)) {
            try {
                await this.openResultsStream(url, token, signal);
                if (signal.aborted || !this.isActiveResultsStream(connectionId)) {
                    return;
                }

                debugLogger.warn('[ResultPanel] Results SSE stream ended; reconnecting.');
            } catch (error) {
                if (error?.name === 'AbortError' || signal.aborted || !this.isActiveResultsStream(connectionId)) {
                    return;
                }

                debugLogger.warn('[ResultPanel] Results SSE connection failed; reconnecting.', error);
            }

            this._resultsStreamReconnectAttempt += 1;
            await this.waitForResultsStreamReconnect(signal, connectionId);
        }
    }

    async openResultsStream(url, token, signal) {
        const response = await fetch(buildSseUrl(url, this._resultsLastEventId), {
            method: 'GET',
            headers: buildSseHeaders(token, this._resultsLastEventId),
            signal
        });

        if (!response.ok || !response.body) {
            throw new Error(`Results SSE connection failed: HTTP ${response.status}`);
        }

        this._resultsStreamReconnectAttempt = 0;
        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        try {
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
                    this.dispatchBoundedResultsSseFrame(frame);
                    separatorIndex = buffer.indexOf('\n\n');
                }

                this.assertResultsSseBufferWithinLimit(buffer);
            }
        } finally {
            try {
                reader.releaseLock?.();
            } catch (error) {
                debugLogger.warn('[ResultPanel] Failed to release results SSE reader:', error);
            }
        }
    }

    dispatchBoundedResultsSseFrame(frame) {
        const maxFrameChars = Number(this.resultsSseMaxFrameChars);
        if (Number.isFinite(maxFrameChars) && maxFrameChars > 0 && frame.length > maxFrameChars) {
            debugLogger.warn('[ResultPanel] Dropping oversized results SSE frame.', {
                length: frame.length,
                maxFrameChars
            });
            return false;
        }

        this.dispatchResultsSseFrame(frame);
        return true;
    }

    assertResultsSseBufferWithinLimit(buffer) {
        const maxBufferChars = Number(this.resultsSseMaxBufferChars);
        if (Number.isFinite(maxBufferChars) && maxBufferChars > 0 && buffer.length > maxBufferChars) {
            throw new Error(`Results SSE buffer exceeded ${maxBufferChars} characters without a frame boundary`);
        }
    }

    dispatchResultsSseFrame(frame) {
        const parsed = parseSseFrame(frame);
        if (!parsed) {
            return;
        }

        const { eventName, eventId, payload } = parsed;
        if (eventId) {
            this._resultsLastEventId = eventId;
        }

        if (eventName === 'resultProduced') {
            this.addResult({
                ...payload,
                id: payload.resultId,
                processingTime: payload.processingTimeMs,
                timestamp: payload.timestamp || new Date().toISOString()
            }, {
                isRealtime: true
            });
        }
    }

    isActiveResultsStream(connectionId) {
        return this._resultsStreamController !== null && this._resultsStreamConnectionId === connectionId;
    }

    waitForResultsStreamReconnect(signal, connectionId) {
        if (signal.aborted || !this.isActiveResultsStream(connectionId)) {
            return Promise.reject(this.createResultsStreamAbortError());
        }

        const attempt = Math.max(0, this._resultsStreamReconnectAttempt - 1);
        const delayMs = Math.min(15000, 1000 * (2 ** Math.min(attempt, 4)));

        return new Promise((resolve, reject) => {
            let timer = null;
            const cleanup = () => {
                if (timer !== null) {
                    clearTimeout(timer);
                }
                if (this._resultsStreamReconnectTimer === timer) {
                    this._resultsStreamReconnectTimer = null;
                }
                signal.removeEventListener('abort', onAbort);
            };
            const onAbort = () => {
                cleanup();
                reject(this.createResultsStreamAbortError());
            };

            this.clearResultsStreamReconnectTimer();
            timer = setTimeout(() => {
                cleanup();
                if (this.isActiveResultsStream(connectionId)) {
                    resolve();
                } else {
                    reject(this.createResultsStreamAbortError());
                }
            }, delayMs);
            this._resultsStreamReconnectTimer = timer;
            signal.addEventListener('abort', onAbort, { once: true });
        });
    }

    clearResultsStreamReconnectTimer() {
        if (this._resultsStreamReconnectTimer !== null) {
            clearTimeout(this._resultsStreamReconnectTimer);
            this._resultsStreamReconnectTimer = null;
        }
    }

    createResultsStreamAbortError() {
        const error = new Error('Results SSE stream aborted');
        error.name = 'AbortError';
        return error;
    }

    disconnectResultsStream() {
        this.clearResultsStreamReconnectTimer();
        if (this._resultsStreamController) {
            this._resultsStreamController.abort();
            this._resultsStreamController = null;
            this._resultsStreamConnectionId += 1;
            this._resultsStreamReconnectAttempt = 0;
        }
    }

    dispose() {
        if (this._isDisposed) {
            return;
        }

        this._isDisposed = true;
        this.disconnectResultsStream();
        this.clearQueuedRefreshes();
        this.clearQueuedRender();

        const eventDisposers = this._eventDisposers.splice(0);
        eventDisposers.forEach(dispose => {
            try {
                dispose();
            } catch (error) {
                debugLogger.warn('[ResultPanel] Failed to dispose event listener:', error);
            }
        });

        this.closeActiveDetailModals({ immediate: true });
        this.historyLoader = null;
        this.historyDetailLoader = null;
        this.comparisonLoader = null;
        this.previousSuccessLoader = null;
        this.evidenceExportLoader = null;
        this.comparisonBaseline = null;
        this.comparisonSelection = { left: null, right: null };
        this.latestFormalResult = null;
        this.onResultClick = null;
    }

    /**
     * 【后端对接占位符 2】：高级统计 API
     * 后端需要提供: GET /api/v1/analytics/advanced?timeRange=xxx
     */
    async fetchAdvancedAnalytics() {
        debugLogger.debug('[ResultPanel] 高级分析 API 未接入，显示暂无数据。');
        // placeholder for fetching CPK, MTBF, Defect Clustering data.
        try {
            // const response = await httpClient.get('/api/v1/analytics/advanced', {
            //     projectId: this.projectId,
            //     ...this.getAnalyticsQueryParams()
            // });
            // this.serverAnalysis = { ...this.serverAnalysis, ...response };
            // this.renderAdvancedStats();

            // Advanced CPK/MTBF/cluster analytics are not yet backed by an API.
            this.serverAnalysis = {
                ...this.serverAnalysis,
                cpk: { value: t('common.noData', '暂无数据'), change: t('common.unavailable', '未接入') },
                mtbf: { value: t('common.noData', '暂无数据'), change: t('common.unavailable', '未接入') },
                defectCluster: { topRegion: t('common.unavailable', '未接入') }
            };
            this.renderAdvancedStats();
        } catch (error) {
            debugLogger.warn('[ResultPanel] 高级分析数据获取失败:', error);
        }
    }

    /**
     * 【后端对接占位符 3】：报表生成与图片打包
     * 后端需要提供: POST /api/v1/reports/generate (生成 PDF)
     * 后端需要提供: POST /api/v1/results/export-images (打包 ZIP)
     */
    async generatePdfReport(filters) {
        debugLogger.debug('[ResultPanel] 深度报告 API 未接入。', filters);
        const btn = document.getElementById('btn-advanced-report');
        if (btn) {
            btn.disabled = true;
            btn.textContent = t('common.unavailable', '未接入');
        }
    }

    /**
     * 渲染高级分析占位数据到 UI
     */
    /**
     * 渲染高级统计卡片 (V3)
     */
    renderAdvancedStats() {
        // Advanced analytics remain empty until a backend API provides real data.
        const cpk = this.serverAnalysis?.cpk ?? { value: t('common.noData', '暂无数据'), change: t('common.unavailable', '未接入') };
        const mtbf = this.serverAnalysis?.mtbf ?? { value: t('common.noData', '暂无数据'), change: t('common.unavailable', '未接入') };
        const cluster = this.serverAnalysis?.defectCluster ?? { topRegion: t('common.unavailable', '未接入') };

        const cpkEl = document.getElementById('stat-cpk');
        const cpkChange = document.getElementById('stat-cpk-change');
        const mtbfEl = document.getElementById('stat-mtbf');
        const mtbfChange = document.getElementById('stat-mtbf-change');
        const clusterEl = document.getElementById('stat-cluster');

        if (cpkEl) cpkEl.textContent = cpk.value ?? '--';
        if (cpkChange) {
            cpkChange.textContent = cpk.change ?? '';
            cpkChange.className = 'stat-card-change' + ((cpk.change || '').startsWith('-') ? ' down' : '');
        }
        if (mtbfEl) mtbfEl.textContent = mtbf.value ?? '--';
        if (mtbfChange) {
            mtbfChange.textContent = mtbf.change ?? '';
            mtbfChange.className = 'stat-card-change' + ((mtbf.change || '').startsWith('-') ? ' down' : '');
        }
        if (clusterEl) clusterEl.textContent = cluster.topRegion ?? '--';
    }
}

// 创建全局实例供HTML事件使用
let resultPanel = null;

export default ResultPanel;
export { ResultPanel };
