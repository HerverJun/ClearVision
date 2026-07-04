export const RESULTS_REVIEW_CAPABILITY_OWNER_ID = 'results-review-capability-v2';

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.getElementById(target) : null;
    }

    return target;
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function resolveResultId(result) {
    return result?.id ?? result?.resultId ?? result?.Id ?? result?.ResultId ?? '';
}

function resolveStatus(result) {
    return result?.status ?? result?.Status ?? result?.isOk ?? result?.IsOk ?? '--';
}

export class ResultsReviewCapabilityAdapter {
    constructor({
        loadHistory = null,
        loadDetail = null,
        loadComparison = null,
        loadPreviousSuccess = null,
        exportEvidence = null
    } = {}) {
        this.loadHistory = loadHistory;
        this.loadDetail = loadDetail;
        this.loadComparison = loadComparison;
        this.loadPreviousSuccess = loadPreviousSuccess;
        this.exportEvidence = exportEvidence;
    }

    async fetchHistory(query) {
        return await this.loadHistory?.(query);
    }

    async fetchDetail(result) {
        return await this.loadDetail?.(result);
    }

    async compare(args) {
        return await this.loadComparison?.(args);
    }

    async previousSuccess(result, options) {
        return await this.loadPreviousSuccess?.(result, options);
    }

    async exportResultEvidence(result) {
        return await this.exportEvidence?.(result);
    }
}

export function createResultsReviewCapabilityAdapter(options = {}) {
    return new ResultsReviewCapabilityAdapter(options);
}

export class ResultsReviewCapabilityOwner {
    constructor(container, {
        adapter,
        showToast = () => {}
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('ResultsReviewCapabilityOwner requires a container.');
        }
        if (!adapter) {
            throw new Error('ResultsReviewCapabilityOwner requires an adapter.');
        }

        this.adapter = adapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.results = [];
        this.projectId = null;
        this.pageIndex = 0;
        this.pageSize = 12;
        this.totalCount = 0;
        this.serverPaged = true;
        this.dataSource = 'inspection';
        this.loading = false;
        this.errorMessage = '';
        this.selectedResult = null;
        this.disposed = false;
        this.refreshRequestId = 0;

        this.handleClick = this.handleClick.bind(this);
        this.container.dataset.resultsReviewOwner = RESULTS_REVIEW_CAPABILITY_OWNER_ID;
        this.container.addEventListener('click', this.handleClick);
        this.render();
    }

    setProjectContext(projectId) {
        this.projectId = projectId || null;
    }

    setHistoryLoader(loadHistory) {
        this.adapter.loadHistory = loadHistory;
    }

    setHistoryDetailLoader(loadDetail) {
        this.adapter.loadDetail = loadDetail;
    }

    setComparisonLoader(loadComparison) {
        this.adapter.loadComparison = loadComparison;
    }

    setPreviousSuccessLoader(loadPreviousSuccess) {
        this.adapter.loadPreviousSuccess = loadPreviousSuccess;
    }

    setEvidenceExportLoader(exportEvidence) {
        this.adapter.exportEvidence = exportEvidence;
    }

    getAnalyticsQueryParams() {
        return {
            startTime: '',
            endTime: '',
            status: 'all',
            defectType: 'all'
        };
    }

    loadResults(results = [], {
        totalCount = Array.isArray(results) ? results.length : 0,
        pageIndex = this.pageIndex,
        pageSize = this.pageSize,
        serverPaged = true
    } = {}) {
        this.results = Array.isArray(results) ? results : [];
        this.totalCount = totalCount;
        this.pageIndex = pageIndex;
        this.pageSize = pageSize;
        this.serverPaged = serverPaged;
        this.render();
    }

    addResult(result, options = {}) {
        if (!result) {
            return;
        }

        if (options.projectId && this.projectId && options.projectId !== this.projectId) {
            return;
        }

        this.results = [result, ...this.results].slice(0, this.pageSize);
        this.totalCount += 1;
        this.render();
    }

    clear() {
        this.results = [];
        this.selectedResult = null;
        this.totalCount = 0;
        this.render();
    }

    async refresh() {
        if (this.disposed) {
            return;
        }

        const requestId = ++this.refreshRequestId;
        this.loading = true;
        this.errorMessage = '';
        this.render();
        try {
            await this.adapter.fetchHistory({
                pageIndex: this.pageIndex,
                pageSize: this.pageSize,
                dataSource: this.dataSource
            });
        } catch (error) {
            if (requestId === this.refreshRequestId) {
                this.errorMessage = error?.message || '追溯数据加载失败';
            }
        } finally {
            if (requestId === this.refreshRequestId) {
                this.loading = false;
                this.render();
            }
        }
    }

    async handleClick(event) {
        const actionEl = event.target?.closest?.('[data-results-action]');
        if (!actionEl || this.disposed) {
            return;
        }

        event.preventDefault();
        const action = actionEl.dataset.resultsAction;
        const resultId = actionEl.closest('[data-result-id]')?.dataset?.resultId || '';
        const result = this.results.find(item => String(resolveResultId(item)) === resultId) || null;
        try {
            if (action === 'detail' && result) {
                this.selectedResult = await this.adapter.fetchDetail(result) || result;
                this.render();
            } else if (action === 'compare' && result) {
                const baseline = this.results.find(item => String(resolveResultId(item)) !== resultId) || null;
                if (baseline) {
                    await this.adapter.compare({ left: baseline, right: result });
                    this.showToast('结果对比已完成', 'success');
                }
            } else if (action === 'previous-success' && result) {
                await this.adapter.previousSuccess(result, { limit: 50 });
                this.showToast('已查询失败前成功记录', 'success');
            } else if (action === 'evidence' && result) {
                await this.adapter.exportResultEvidence(result);
                this.showToast('证据导出已请求', 'success');
            } else if (action === 'refresh') {
                await this.refresh();
            }
        } catch (error) {
            this.errorMessage = error?.message || '追溯操作失败';
            this.showToast(this.errorMessage, 'error');
            this.render();
        }
    }

    showResultDetail(result) {
        this.selectedResult = result || null;
        this.render();
    }

    async loadServerAnalytics() {
        return null;
    }

    disconnectResultsStream() {
        return null;
    }

    render() {
        if (this.disposed || !this.container) {
            return;
        }

        this.container.innerHTML = `
            <section class="results-review-capability-owner" data-owner="${RESULTS_REVIEW_CAPABILITY_OWNER_ID}">
                <div class="results-info-bar">
                    <span id="results-count-info">共 ${this.totalCount} 条记录</span>
                    <button type="button" class="btn btn-secondary" data-results-action="refresh" ${this.loading ? 'disabled' : ''}>刷新</button>
                </div>
                ${this.errorMessage ? `<div class="results-error" role="alert">${escapeHtml(this.errorMessage)}</div>` : ''}
                ${this.loading ? '<p class="empty-text">正在加载追溯数据...</p>' : this.renderResults()}
                ${this.renderDetail()}
            </section>
        `;
    }

    renderResults() {
        if (this.results.length === 0) {
            return '<div class="results-grid" id="results-grid"><p class="empty-text">暂无正式历史记录</p></div>';
        }

        return `
            <div class="results-grid" id="results-grid">
                ${this.results.map(result => {
                    const resultId = resolveResultId(result);
                    return `
                        <article class="result-card" data-result-id="${escapeHtml(resultId)}">
                            <div class="result-card-header">
                                <strong>${escapeHtml(resolveStatus(result))}</strong>
                                <span>${escapeHtml(resultId)}</span>
                            </div>
                            <p>${escapeHtml(result.timestamp || result.Timestamp || result.createdAt || result.CreatedAt || '')}</p>
                            <div class="result-card-actions">
                                <button type="button" class="btn btn-secondary" data-results-action="detail">详情</button>
                                <button type="button" class="btn btn-secondary" data-results-action="compare">对比</button>
                                <button type="button" class="btn btn-secondary" data-results-action="previous-success">基线</button>
                                <button type="button" class="btn btn-secondary" data-results-action="evidence">证据</button>
                            </div>
                        </article>
                    `;
                }).join('')}
            </div>
        `;
    }

    renderDetail() {
        if (!this.selectedResult) {
            return '';
        }

        return `
            <aside class="result-detail-inline">
                <h3>历史详情</h3>
                <pre>${escapeHtml(JSON.stringify(this.selectedResult, null, 2))}</pre>
            </aside>
        `;
    }

    destroy() {
        this.dispose();
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        this.refreshRequestId += 1;
        this.container.removeEventListener('click', this.handleClick);
        delete this.container.dataset.resultsReviewOwner;
        this.container.innerHTML = '';
    }
}

export default ResultsReviewCapabilityOwner;
