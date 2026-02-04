/**
 * 结果面板组件
 * 显示检测统计和缺陷列表
 */

class ResultPanel {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.results = [];
        this.statistics = {
            total: 0,
            ok: 0,
            ng: 0,
            error: 0
        };
    }

    /**
     * 更新统计
     */
    updateStatistics(stats) {
        this.statistics = { ...this.statistics, ...stats };
        this.renderStatistics();
    }

    /**
     * 添加结果
     */
    addResult(result) {
        this.results.unshift(result); // 最新的在前面
        
        // 更新统计
        this.statistics.total++;
        if (result.status === 'OK') {
            this.statistics.ok++;
        } else if (result.status === 'NG') {
            this.statistics.ng++;
        } else if (result.status === 'Error') {
            this.statistics.error++;
        }
        
        this.render();
    }

    /**
     * 清空结果
     */
    clear() {
        this.results = [];
        this.statistics = { total: 0, ok: 0, ng: 0, error: 0 };
        this.render();
    }

    /**
     * 渲染面板
     */
    render() {
        this.renderStatistics();
        this.renderResultsList();
    }

    /**
     * 渲染统计信息
     */
    renderStatistics() {
        const { total, ok, ng, error } = this.statistics;
        const okRate = total > 0 ? ((ok / total) * 100).toFixed(1) : 0;
        
        const statsHtml = `
            <div class="stats-grid">
                <div class="stat-item">
                    <span class="stat-value stat-total">${total}</span>
                    <span class="stat-label">总数</span>
                </div>
                <div class="stat-item">
                    <span class="stat-value stat-ok">${ok}</span>
                    <span class="stat-label">OK</span>
                </div>
                <div class="stat-item">
                    <span class="stat-value stat-ng">${ng}</span>
                    <span class="stat-label">NG</span>
                </div>
                <div class="stat-item">
                    <span class="stat-value stat-rate">${okRate}%</span>
                    <span class="stat-label">良率</span>
                </div>
            </div>
            ${error > 0 ? `<div class="stat-error">错误: ${error}</div>` : ''}
        `;
        
        // 更新或创建统计区域
        let statsContainer = this.container.querySelector('.results-statistics');
        if (!statsContainer) {
            statsContainer = document.createElement('div');
            statsContainer.className = 'results-statistics';
            this.container.insertBefore(statsContainer, this.container.firstChild);
        }
        statsContainer.innerHTML = statsHtml;
    }

    /**
     * 渲染结果列表
     */
    renderResultsList() {
        let listContainer = this.container.querySelector('.results-list');
        if (!listContainer) {
            listContainer = document.createElement('div');
            listContainer.className = 'results-list';
            this.container.appendChild(listContainer);
        }
        
        if (this.results.length === 0) {
            listContainer.innerHTML = '<p class="empty-text">暂无检测结果</p>';
            return;
        }
        
        const listHtml = this.results.map((result, index) => {
            const statusClass = result.status?.toLowerCase() || 'unknown';
            const time = result.timestamp ? new Date(result.timestamp).toLocaleTimeString() : '--:--:--';
            
            return `
                <div class="result-item result-${statusClass}" data-index="${index}">
                    <div class="result-header">
                        <span class="result-status">${result.status || 'Unknown'}</span>
                        <span class="result-time">${time}</span>
                    </div>
                    ${result.defects?.length > 0 ? `
                        <div class="result-defects">
                            ${result.defects.map(defect => `
                                <div class="defect-item">
                                    <span class="defect-type">${defect.type}</span>
                                    <span class="defect-confidence">${(defect.confidence * 100).toFixed(1)}%</span>
                                </div>
                            `).join('')}
                        </div>
                    ` : ''}
                    ${result.processingTime ? `
                        <div class="result-meta">
                            处理时间: ${result.processingTime}ms
                        </div>
                    ` : ''}
                </div>
            `;
        }).join('');
        
        listContainer.innerHTML = listHtml;
        
        // 绑定点击事件
        listContainer.querySelectorAll('.result-item').forEach(item => {
            item.addEventListener('click', (e) => {
                const index = parseInt(e.currentTarget.dataset.index);
                this.onResultClick?.(this.results[index]);
            });
        });
    }

    /**
     * 导出结果
     */
    exportResults(format = 'json') {
        if (this.results.length === 0) {
            alert('没有可导出的结果');
            return;
        }
        
        let content, filename, mimeType;
        
        switch (format) {
            case 'json':
                content = JSON.stringify(this.results, null, 2);
                filename = `inspection_results_${Date.now()}.json`;
                mimeType = 'application/json';
                break;
                
            case 'csv':
                content = this.convertToCSV(this.results);
                filename = `inspection_results_${Date.now()}.csv`;
                mimeType = 'text/csv';
                break;
                
            default:
                throw new Error(`不支持的导出格式: ${format}`);
        }
        
        // 下载文件
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
    convertToCSV(results) {
        const headers = ['时间', '状态', '缺陷数', '处理时间(ms)', '置信度'];
        const rows = results.map(r => [
            r.timestamp ? new Date(r.timestamp).toISOString() : '',
            r.status,
            r.defects?.length || 0,
            r.processingTime || '',
            r.confidenceScore || ''
        ]);
        
        return [headers.join(','), ...rows.map(row => row.join(','))].join('\n');
    }

    /**
     * 设置结果点击回调
     */
    onResultClick(callback) {
        this.onResultClick = callback;
    }

    /**
     * 获取最新结果
     */
    getLatestResult() {
        return this.results[0] || null;
    }

    /**
     * 获取所有结果
     */
    getAllResults() {
        return [...this.results];
    }

    /**
     * 按状态筛选
     */
    filterByStatus(status) {
        return this.results.filter(r => r.status === status);
    }

    /**
     * 按时间范围筛选
     */
    filterByTimeRange(startTime, endTime) {
        return this.results.filter(r => {
            const time = new Date(r.timestamp).getTime();
            return time >= startTime.getTime() && time <= endTime.getTime();
        });
    }
}

export default ResultPanel;
export { ResultPanel };
