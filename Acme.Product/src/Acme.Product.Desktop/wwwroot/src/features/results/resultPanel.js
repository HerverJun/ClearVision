/**
 * 结果面板组件 - 阶段二增强版
 * 现代化数据可视化仪表板
 */

import httpClient from '../../core/messaging/httpClient.js';
import { getStoredToken } from '../auth/authStorage.js';
import { renderDiagnosticsCardsHtml } from '../inspection/analysisCardsPanel.js';
import { buildSseHeaders, parseSseFrame } from '../inspection/inspectionSseClient.mjs';
import debugLogger from '../../core/logging/debugLogger.js';
import { t } from '../../core/i18n/resources.js';
import {
    buildResultCardsFromOutputData,
    renderResultCardHtml,
    summarizeResultField
} from './portDataTypeRenderer.mjs';

class ResultPanel {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.results = [];
        this.filteredResults = [];
        this.projectId = null;
        this.serverReport = null;
        this.serverAnalysis = null;
        this.serverAnalysisSource = 'local';
        this._resultsStreamController = null;
        this._resultsStreamConnectionId = 0;
        this._resultsStreamReconnectAttempt = 0;
        this._resultsStreamReconnectTimer = null;
        this._resultsLastEventId = null;
        this._analyticsRefreshTimer = null;
        this._historyRefreshTimer = null;
        this.statistics = {
            total: 0,
            ok: 0,
            ng: 0,
            error: 0,
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
    bindEvents() {
        // 时间范围选择
        document.querySelectorAll('.time-range-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                document.querySelectorAll('.time-range-btn').forEach(b => b.classList.remove('active'));
                e.target.classList.add('active');
                this.setTimeRange(e.target.dataset.range);
            });
        });
        
        // 状态筛选
        const statusFilter = document.getElementById('filter-status');
        if (statusFilter) {
            statusFilter.addEventListener('change', (e) => {
                this.setFilter('status', e.target.value);
            });
        }
        
        // 缺陷类型筛选
        const defectTypeFilter = document.getElementById('filter-defect-type');
        if (defectTypeFilter) {
            defectTypeFilter.addEventListener('change', (e) => {
                this.setFilter('defectType', e.target.value);
            });
        }
        
        // 导出下拉菜单
        const exportDropdown = document.getElementById('export-dropdown');
        const exportBtn = document.getElementById('btn-export-results');
        if (exportBtn && exportDropdown) {
            exportBtn.addEventListener('click', () => {
                exportDropdown.classList.toggle('open');
            });
            
            // 导出选项
            exportDropdown.querySelectorAll('.export-menu-item').forEach(item => {
                item.addEventListener('click', () => {
                    const format = item.dataset.format;
                    this.exportResults(format);
                    exportDropdown.classList.remove('open');
                });
            });
            
            // 点击外部关闭
            document.addEventListener('click', (e) => {
                if (!exportDropdown.contains(e.target)) {
                    exportDropdown.classList.remove('open');
                }
            });
        }

        // 【后端对接占位符 1】：生成深度报告按钮
        const advancedReportBtn = document.getElementById('btn-advanced-report');
        if (advancedReportBtn) {
            advancedReportBtn.addEventListener('click', () => {
                this.generatePdfReport(this.getAnalyticsQueryParams());
            });
        }
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

        if (this.projectId && this.historyLoader) {
            this.requestHistoryPage(0).catch(error => {
                debugLogger.warn('[ResultPanel] 刷新服务端历史失败:', error);
            });
        }

        if (this.projectId) {
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
            if (this.projectId) {
                this.connectResultsHub();
            }
        }
    }

    setHistoryLoader(loader) {
        this.historyLoader = typeof loader === 'function' ? loader : null;
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
            return `当前页 ${pageResults.length} 条 / 共 ${this.totalResultCount} 条记录`;
        }

        return `共 ${this.filteredResults.length} 条记录`;
    }

    requestHistoryPage(pageIndex = 0) {
        if (!this.historyLoader || !this.projectId) {
            return Promise.resolve(false);
        }

        return this.historyLoader({
            pageIndex,
            pageSize: this.pageSize,
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
        if (!this.projectId) {
            return;
        }

        if (this._analyticsRefreshTimer) {
            clearTimeout(this._analyticsRefreshTimer);
        }

        this._analyticsRefreshTimer = window.setTimeout(() => {
            this._analyticsRefreshTimer = null;
            this.loadServerAnalytics().catch(error => {
                debugLogger.warn('[ResultPanel] Server analytics refresh failed:', error);
            });
        }, delayMs);
    }

    queueServerHistoryRefresh(delayMs = 400) {
        if (!this.projectId || !this.historyLoader) {
            return;
        }

        if (this._historyRefreshTimer) {
            clearTimeout(this._historyRefreshTimer);
        }

        this._historyRefreshTimer = window.setTimeout(() => {
            this._historyRefreshTimer = null;
            this.requestHistoryPage(0).catch(error => {
                debugLogger.warn('[ResultPanel] Server history refresh failed:', error);
            });
        }, delayMs);
    }

    normalizeStatistics(statistics) {
        if (!statistics || typeof statistics !== 'object') {
            return null;
        }

        return {
            total: statistics.totalCount ?? statistics.TotalCount ?? 0,
            ok: statistics.okCount ?? statistics.OKCount ?? 0,
            ng: statistics.ngCount ?? statistics.NGCount ?? 0,
            error: statistics.errorCount ?? statistics.ErrorCount ?? 0,
            avgTime: Math.round(statistics.averageProcessingTimeMs ?? statistics.AverageProcessingTimeMs ?? 0)
        };
    }

    normalizeDefectDistribution(defectDistribution) {
        const items = defectDistribution?.items || defectDistribution?.Items || [];
        return items.reduce((accumulator, item) => {
            const defectType = item.defectType || item.DefectType || t('common.unknown', '未知');
            const count = item.count ?? item.Count ?? 0;
            accumulator[defectType] = count;
            return accumulator;
        }, {});
    }

    normalizeTrendPoints(trend) {
        const points = trend?.dataPoints || trend?.DataPoints || [];
        return points.map(point => ({
            time: new Date(point.timestamp || point.Timestamp || Date.now()),
            status: (point.ngCount ?? point.NGCount ?? 0) > 0
                ? 'NG'
                : ((point.errorCount ?? point.ErrorCount ?? 0) > 0 ? 'Error' : 'OK'),
            defectCount: point.defectCount ?? point.DefectCount ?? 0
        }));
    }

    applyServerAnalysis({ report = null, statistics = null, defectDistribution = null, trend = null } = {}) {
        const normalizedStatistics = this.normalizeStatistics(
            report?.summary || report?.Summary || statistics
        );
        const normalizedDefects = this.normalizeDefectDistribution(
            report?.defectDistribution || report?.DefectDistribution || defectDistribution
        );
        const normalizedTrend = this.normalizeTrendPoints(
            report?.hourlyTrend || report?.HourlyTrend || trend
        );

        if (normalizedStatistics) {
            this.statistics = normalizedStatistics;
        }

        if (Object.keys(normalizedDefects).length > 0) {
            this.defectTypes = normalizedDefects;
            this.updateDefectTypeFilter();
        }

        if (normalizedTrend.length > 0) {
            this.trendData = normalizedTrend;
        }

        this.serverReport = report || this.serverReport;
        this.serverAnalysis = {
            statistics: normalizedStatistics,
            defectTypes: normalizedDefects,
            trendData: normalizedTrend
        };
        this.serverAnalysisSource = 'server';
    }

    async loadServerAnalytics(projectId = this.projectId) {
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
            this.statistics = {
                total: 0,
                ok: 0,
                ng: 0,
                error: 0,
                avgTime: 0
            };
            this.defectTypes = {};
            this.trendData = [];
            this.updateDefectTypeFilter();
            this.render();
            return;
        }

        this.serverAnalysisSource = 'local';

        if (statistics) {
            this.statistics = {
                total: statistics.totalCount ?? statistics.TotalCount ?? this.statistics.total,
                ok: statistics.okCount ?? statistics.OKCount ?? this.statistics.ok,
                ng: statistics.ngCount ?? statistics.NGCount ?? this.statistics.ng,
                error: statistics.errorCount ?? statistics.ErrorCount ?? this.statistics.error,
                avgTime: Math.round(statistics.averageProcessingTimeMs ?? statistics.AverageProcessingTimeMs ?? this.statistics.avgTime ?? 0)
            };
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
                status: (point.ngCount ?? point.NGCount ?? 0) > 0
                    ? 'NG'
                    : ((point.errorCount ?? point.ErrorCount ?? 0) > 0 ? 'Error' : 'OK'),
                defectCount: point.defectCount ?? point.DefectCount ?? 0
            }));
        }

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
    addResult(result) {
        if (this.serverPaged) {
            if (this.projectId) {
                this.queueServerHistoryRefresh();
                this.queueServerAnalyticsRefresh();
            }
            return;
        }

        this.results.unshift(result);
        this.applyFilters();
        
        // 更新统计
        this.statistics.total++;
        if (result.status === 'OK') {
            this.statistics.ok++;
        } else if (result.status === 'NG') {
            this.statistics.ng++;
        } else if (result.status === 'Error') {
            this.statistics.error++;
        }
        
        // 更新平均耗时
        if (result.processingTime) {
            const validResults = this.results.filter(r => r.processingTime);
            const totalTime = validResults.reduce((sum, r) => sum + r.processingTime, 0);
            this.statistics.avgTime = validResults.length > 0 ? Math.round(totalTime / validResults.length) : 0;
        }
        
        // 更新趋势图数据
        this.trendData.push({
            time: new Date(result.timestamp || Date.now()),
            status: result.status,
            defectCount: result.defects?.length || 0
        });
        if (this.trendData.length > 100) {
            this.trendData.shift();
        }
        
        // 更新缺陷类型统计
        if (result.defects) {
            result.defects.forEach(defect => {
                const type = defect.type || defect.description || t('common.unknown', '未知');
                this.defectTypes[type] = (this.defectTypes[type] || 0) + 1;
            });
        }

        if (this.projectId) {
            this.queueServerAnalyticsRefresh();
        }

        this.render();
    }
    
    /**
     * 加载历史结果
     */
    loadResults(results, { totalCount = null, pageIndex = 0, pageSize = this.pageSize, serverPaged = false } = {}) {
        this.results = Array.isArray(results) ? results : [];
        this.serverPaged = !!serverPaged;
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
    /**
     * 计算统计
     */
    calculateStatistics() {
        const total = this.results.length;
        const ok = this.results.filter(r => r.status === 'OK').length;
        const ng = this.results.filter(r => r.status === 'NG').length;
        const error = this.results.filter(r => r.status === 'Error').length;
        
        const validResults = this.results.filter(r => r.processingTime);
        const totalTime = validResults.reduce((sum, r) => sum + (r.processingTime || 0), 0);
        const avgTime = validResults.length > 0 ? Math.round(totalTime / validResults.length) : 0;
        
        this.statistics = { total, ok, ng, error, avgTime };
        
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
                status: r.status,
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
            if (this.filters.status !== 'all' && r.status?.toLowerCase() !== this.filters.status) {
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
        if (this.serverPaged && this.projectId) {
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
        this.statistics = { total: 0, ok: 0, ng: 0, error: 0, avgTime: 0 };
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

        return result.imageData
            || result.outputImage
            || result.outputImageBase64
            || result.resultImageBase64
            || null;
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
        const { total, ok, ng, error, avgTime } = this.statistics;
        const yieldRate = total > 0 ? ((ok / total) * 100).toFixed(1) : '0';
        const timeSec = avgTime > 1000 ? (avgTime / 1000).toFixed(1) : avgTime;
        const timeUnit = avgTime > 1000 ? 's' : 'ms';

        const setKPI = (id, value) => {
            const el = document.getElementById(id);
            if (el) el.textContent = value;
        };

        setKPI('kpi-total', total.toLocaleString());
        setKPI('kpi-ok', ok.toLocaleString());
        setKPI('kpi-ng', ng.toLocaleString());
        setKPI('kpi-error', error.toLocaleString());
        setKPI('kpi-yield', `${yieldRate}%`);
        setKPI('kpi-avg-time', `${timeSec}${timeUnit}`);

        // 更新时间戳
        const updateTimeEl = document.getElementById('last-update-time');
        if (updateTimeEl) {
            updateTimeEl.textContent = '更新于 刚刚';
        }
    }
    
    /**
     * 渲染良率仪表盘 — 半圆弧 SVG
     */
    renderYieldChart() {
        const { total, ok } = this.statistics;
        const yieldRate = total > 0 ? (ok / total) : 0;
        const percentage = (yieldRate * 100).toFixed(1);

        // 更新数值文字
        const gaugeValue = document.getElementById('gauge-percentage');
        if (gaugeValue) gaugeValue.textContent = percentage;

        // 状态评级
        const gaugeStatus = document.getElementById('gauge-status');
        if (gaugeStatus) {
            let status = 'Critical';
            if (yieldRate >= 0.95) status = 'Excellent';
            else if (yieldRate >= 0.85) status = 'Good';
            else if (yieldRate >= 0.7) status = 'Warning';
            gaugeStatus.textContent = `Status: ${status}`;
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

        // 五个维度：划伤、脏污、缺角、气泡、其他
        const dimensions = ['划伤', '脏污', '缺角', '气泡', '其他'];
        const types = Object.entries(this.defectTypes);

        if (types.length === 0) {
            // 无数据时保持默认形状
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
    }
    
    /**
     * 渲染吞吐量面积图 — SVG Path
     */
    renderThroughputChart() {
        const areaPath = document.getElementById('throughput-area');
        const linePath = document.getElementById('throughput-line');
        if (!areaPath || !linePath) return;

        if (this.trendData.length < 2) {
            // 保持默认 Mock 路径
            return;
        }

        // 将趋势数据映射为检测量（每个时间点的总检测数）
        // 按时间分组统计每小时的检测量
        const bucketMap = new Map();
        this.trendData.forEach(p => {
            const hour = new Date(p.time).getHours();
            bucketMap.set(hour, (bucketMap.get(hour) || 0) + 1);
        });

        const buckets = Array.from(bucketMap.entries()).sort((a, b) => a[0] - b[0]);
        if (buckets.length < 2) return;

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
                : '暂无检测结果';
            gridContainer.innerHTML = `<p class="empty-text">${emptyText}</p>`;
            return;
        }

        gridContainer.innerHTML = pageResults.map((result, index) => {
            const statusClass = this.toCssToken(result.status || 'unknown');
            const statusText = this.escapeHtml(result.status || 'Unknown');
            const time = result.timestamp ? new Date(result.timestamp).toLocaleTimeString() : '--:--:--';
            const processingTime = result.processingTime || result.executionTimeMs || '--';
            const outputDataHtml = this.renderAnalysisDataPreview(result.analysisData);

            return `
                <div class="result-card result-${statusClass}" data-index="${index}" style="cursor:pointer;">
                    <div class="result-card-header">
                        <span class="result-status-badge ${statusClass}">${statusText}</span>
                        <span class="result-time">${time}</span>
                    </div>
                    <div class="result-card-body">
                        <span class="result-processing-time">${this.escapeHtml(processingTime)}ms</span>
                        ${result.defects?.length > 0 ? `<span class="result-defect-count">${result.defects.length} 缺陷</span>` : ''}
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
        
        switch (format) {
            case 'json':
                content = JSON.stringify(this.filteredResults, null, 2);
                filename = `${filenamePrefix}_${Date.now()}.json`;
                mimeType = 'application/json';
                break;
            case 'csv':
            case 'excel':
                content = this.convertToCSV(this.filteredResults);
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
                    filename: `inspection_report_${timestamp}.json`,
                    mimeType: 'application/json'
                };
            }

            if (format === 'csv' || format === 'excel') {
                return {
                    content: this.convertReportToCSV(report),
                    filename: `inspection_report_${timestamp}.csv`,
                    mimeType: 'text/csv'
                };
            }
        }

        return null;
    }

    convertToCSV(results) {
        const headers = ['时间', '状态', '缺陷数', '处理时间(ms)', '置信度'];
        const rows = results.map(r => [
            r.timestamp ? new Date(r.timestamp).toISOString() : '',
            r.status,
            r.defects?.length || 0,
            r.processingTime || r.executionTimeMs || '',
            r.defects?.[0]?.confidenceScore ? (r.defects[0].confidenceScore * 100).toFixed(1) + '%' : ''
        ]);
        
        return [this.toCsvRow(headers), ...rows.map(row => this.toCsvRow(row))].join('\n');
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

    showResultDetail(result) {
        debugLogger.debug('[ResultPanel] 查看结果详情:', result);
        
        const modal = document.createElement('div');
        modal.className = 'result-detail-modal';
        
        const statusClass = this.toCssToken(result.status || 'unknown');
        const statusText = this.escapeHtml(result.status || 'Unknown');
        const time = result.timestamp ? new Date(result.timestamp).toLocaleString() : '--';
        const processingTime = result.processingTime || result.executionTimeMs || '--';
        const imageSrc = this.getResultImageSrc(result);
        
        modal.innerHTML = `
            <div class="result-detail-overlay"></div>
            <div class="result-detail-content">
                <div class="result-detail-header">
                    <h3>检测结果详情</h3>
                    <span class="result-status-badge ${statusClass}" style="font-size:12px;padding:4px 12px;">${statusText}</span>
                    <button class="result-detail-close">✕</button>
                </div>
                <div class="result-detail-body">
                    ${imageSrc ? `<div class="result-detail-image"><img src="${imageSrc}" alt="检测结果图像" /></div>` : ''}
                    <div class="result-detail-data">
                        <div class="detail-section">
                            <div class="detail-item"><span class="detail-label">状态</span><span class="detail-value status-${statusClass}">${this.escapeHtml(result.status || '--')}</span></div>
                            <div class="detail-item"><span class="detail-label">时间</span><span class="detail-value">${time}</span></div>
                            <div class="detail-item"><span class="detail-label">处理耗时</span><span class="detail-value">${this.escapeHtml(processingTime)}ms</span></div>
                        </div>
                        ${this.renderAnalysisDataSection(result.analysisData)}
                        ${this.renderStructuredOutputSection(result.outputData, result.status)}
                        ${this.renderDiagnosticsSection(result.outputData, result.status)}
                        ${this.renderOutputDataTable(result.outputData)}
                        ${result.defects?.length > 0 ? `
                            <div class="detail-section">
                                <div class="detail-section-title">缺陷列表 (${result.defects.length})</div>
                                ${result.defects.map(d => `
                                    <div class="detail-item">
                                        <span class="detail-label">${this.escapeHtml(d.type || d.description || t('common.unknown', '未知'))}</span>
                                        <span class="detail-value">${d.confidenceScore ? (d.confidenceScore * 100).toFixed(1) + '%' : '--'}</span>
                                    </div>
                                `).join('')}
                            </div>
                        ` : ''}
                    </div>
                </div>
            </div>
        `;
        
        document.body.appendChild(modal);
        // 入场动画
        requestAnimationFrame(() => modal.classList.add('visible'));
        
        const closeModal = () => {
            modal.classList.remove('visible');
            setTimeout(() => modal.remove(), 200);
        };
        modal.querySelector('.result-detail-close').addEventListener('click', closeModal);
        modal.querySelector('.result-detail-overlay').addEventListener('click', closeModal);
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

        return `
            <div class="detail-section">
                <div class="detail-section-title">结构化输出</div>
                <div class="analysis-cards-container ac-diagnostics-inline ac-diagnostics-detail">
                    ${cards.map(card => renderResultCardHtml(card, { fallbackStatus })).join('')}
                </div>
            </div>
        `;
    }

    renderOutputDataTable(outputData) {
        if (!outputData || typeof outputData !== 'object' || Object.keys(outputData).length === 0) return '';
        
        const rows = [];
        let hiddenCount = 0;
        for (const [key, value] of Object.entries(outputData)) {
            if (this.shouldHideOutputDetailEntry(key, value, outputData)) {
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
            ? `<div class="detail-item type-null"><span class="detail-label">说明</span><span class="detail-value">已隐藏 ${hiddenCount} 个导出/技术字段</span></div>`
            : '';

        return `<div class="detail-section"><div class="detail-section-title">原始输出数据（调试）</div>${rows.join('')}${hiddenNotice}</div>`;
    }

    renderAnalysisDataSection(analysisData) {
        const cards = Array.isArray(analysisData?.cards) ? analysisData.cards : [];
        if (cards.length === 0) {
            return '';
        }

        const sections = cards.map(card => {
            const fields = Array.isArray(card?.fields) ? card.fields : [];
            const rows = fields.map(field => `<div class="detail-item">
                <span class="detail-label">${this.escapeHtml(field.label || field.key || '--')}</span>
                <span class="detail-value">${this.escapeHtml(this.formatAnalysisFieldValue(field.value))}${field.unit ? ` ${this.escapeHtml(field.unit)}` : ''}</span>
            </div>`).join('');

            return `
                <div class="detail-section">
                    <div class="detail-section-title">${this.escapeHtml(card.title || card.category || '分析卡片')}</div>
                    ${rows || '<div class="detail-item"><span class="detail-label">内容</span><span class="detail-value">--</span></div>'}
                </div>
            `;
        }).join('');

        return sections;
    }

    renderDiagnosticsSection(outputData, fallbackStatus) {
        if (!outputData || typeof outputData !== 'object') {
            return '';
        }

        const diagnosticsHtml = renderDiagnosticsCardsHtml(outputData, fallbackStatus || 'OK', {
            containerClass: 'analysis-cards-container ac-diagnostics-inline ac-diagnostics-detail'
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

    formatAnalysisFieldValue(value) {
        if (typeof value === 'number') {
            return Number.isInteger(value) ? String(value) : value.toFixed(3);
        }

        if (typeof value === 'boolean') {
            return value ? 'True' : 'False';
        }

        if (value === null || value === undefined) {
            return '--';
        }

        if (typeof value === 'object') {
            return JSON.stringify(value);
        }

        return String(value);
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
        if (!this.projectId || this._resultsStreamController) {
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
        const response = await fetch(url, {
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
                this.dispatchResultsSseFrame(frame);
                separatorIndex = buffer.indexOf('\n\n');
            }
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
