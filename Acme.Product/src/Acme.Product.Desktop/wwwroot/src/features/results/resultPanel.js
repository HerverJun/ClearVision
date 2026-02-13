/**
 * 结果面板组件 - 阶段C增强版
 * 显示检测统计、缺陷列表、趋势图，支持翻页和筛选
 */

class ResultPanel {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.results = [];
        this.filteredResults = [];
        this.statistics = {
            total: 0,
            ok: 0,
            ng: 0,
            error: 0
        };
        
        // 分页
        this.currentPage = 1;
        this.pageSize = 10;
        this.totalPages = 1;
        
        // 筛选
        this.filters = {
            status: 'all', // all, ok, ng, error
            startTime: null,
            endTime: null
        };
        
        // 趋势图数据
        this.trendData = [];
    }

    /**
     * 更新统计
     */
    updateStatistics(stats) {
        this.statistics = { ...this.statistics, ...stats };
        this.renderStatistics();
        this.renderTrendChart();
    }

    /**
     * 添加结果
     */
    addResult(result) {
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
        
        // 更新趋势图数据（只保留最近100个点）
        this.trendData.push({
            time: new Date(result.timestamp || Date.now()),
            status: result.status,
            defectCount: result.defects?.length || 0
        });
        if (this.trendData.length > 100) {
            this.trendData.shift();
        }
        
        this.render();
    }

    /**
     * 加载历史结果（支持翻页）
     */
    loadResults(results, total = null) {
        this.results = results;
        this.applyFilters();
        
        // 重新计算统计
        this.calculateStatistics();
        
        // 更新趋势图数据
        this.updateTrendData();
        
        this.render();
    }

    /**
     * 计算统计
     */
    calculateStatistics() {
        this.statistics = {
            total: this.results.length,
            ok: this.results.filter(r => r.status === 'OK').length,
            ng: this.results.filter(r => r.status === 'NG').length,
            error: this.results.filter(r => r.status === 'Error').length
        };
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

    /**
     * 应用筛选
     */
    applyFilters() {
        this.filteredResults = this.results.filter(r => {
            // 状态筛选
            if (this.filters.status !== 'all' && r.status?.toLowerCase() !== this.filters.status) {
                return false;
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
        
        // 重新计算总页数
        this.totalPages = Math.ceil(this.filteredResults.length / this.pageSize) || 1;
        
        // 确保当前页有效
        if (this.currentPage > this.totalPages) {
            this.currentPage = this.totalPages;
        }
    }

    /**
     * 设置筛选条件
     */
    setFilter(type, value) {
        this.filters[type] = value;
        this.currentPage = 1; // 重置到第一页
        this.applyFilters();
        this.render();
    }

    /**
     * 翻页
     */
    goToPage(page) {
        if (page < 1 || page > this.totalPages) return;
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
        this.statistics = { total: 0, ok: 0, ng: 0, error: 0 };
        this.currentPage = 1;
        this.applyFilters();
        this.render();
    }

    /**
     * 渲染面板
     */
    render() {
        this.renderStatistics();
        this.renderFilters();
        this.renderTrendChart();
        this.renderResultsList();
        this.renderPagination();
    }

    /**
     * 渲染筛选控件
     */
    renderFilters() {
        let filtersContainer = this.container.querySelector('.results-filters');
        if (!filtersContainer) {
            filtersContainer = document.createElement('div');
            filtersContainer.className = 'results-filters';
            this.container.insertBefore(filtersContainer, this.container.firstChild?.nextSibling);
        }
        
        filtersContainer.innerHTML = `
            <div class="filter-group">
                <label>状态筛选:</label>
                <select class="filter-status" onchange="resultPanel.setFilter('status', this.value)">
                    <option value="all" ${this.filters.status === 'all' ? 'selected' : ''}>全部</option>
                    <option value="ok" ${this.filters.status === 'ok' ? 'selected' : ''}>OK</option>
                    <option value="ng" ${this.filters.status === 'ng' ? 'selected' : ''}>NG</option>
                    <option value="error" ${this.filters.status === 'error' ? 'selected' : ''}>Error</option>
                </select>
            </div>
            <div class="filter-group">
                <label>时间范围:</label>
                <input type="datetime-local" class="filter-start" 
                    value="${this.filters.startTime ? this.toDateTimeLocal(this.filters.startTime) : ''}"
                    onchange="resultPanel.setFilter('startTime', this.value ? new Date(this.value) : null)">
                <span>至</span>
                <input type="datetime-local" class="filter-end"
                    value="${this.filters.endTime ? this.toDateTimeLocal(this.filters.endTime) : ''}"
                    onchange="resultPanel.setFilter('endTime', this.value ? new Date(this.value) : null)">
            </div>
            <div class="filter-group">
                <button onclick="resultPanel.exportResults('csv')" class="btn btn-secondary">导出CSV</button>
                <button onclick="resultPanel.exportResults('json')" class="btn btn-secondary">导出JSON</button>
            </div>
        `;
    }

    /**
     * 辅助函数：Date 转 datetime-local 格式
     */
    toDateTimeLocal(date) {
        const d = new Date(date);
        d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
        return d.toISOString().slice(0, 16);
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
        
        let statsContainer = this.container.querySelector('.results-statistics');
        if (!statsContainer) {
            statsContainer = document.createElement('div');
            statsContainer.className = 'results-statistics';
            this.container.insertBefore(statsContainer, this.container.firstChild);
        }
        statsContainer.innerHTML = statsHtml;
    }

    /**
     * 渲染趋势图
     */
    renderTrendChart() {
        let chartContainer = this.container.querySelector('.results-trend-chart');
        if (!chartContainer) {
            chartContainer = document.createElement('div');
            chartContainer.className = 'results-trend-chart';
            this.container.insertBefore(chartContainer, this.container.querySelector('.results-list') || null);
        }
        
        if (this.trendData.length < 2) {
            chartContainer.innerHTML = '<p class="empty-text">数据不足，无法显示趋势图</p>';
            return;
        }
        
        // 创建 canvas
        const canvasId = 'trend-canvas';
        chartContainer.innerHTML = `
            <h4 class="chart-title">检测趋势（最近${this.trendData.length}次）</h4>
            <canvas id="${canvasId}" width="600" height="200"></canvas>
            <div class="chart-legend">
                <span class="legend-item"><span class="legend-color ok"></span> OK</span>
                <span class="legend-item"><span class="legend-color ng"></span> NG</span>
                <span class="legend-item"><span class="legend-color error"></span> Error</span>
            </div>
        `;
        
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        
        const ctx = canvas.getContext('2d');
        const width = canvas.width;
        const height = canvas.height;
        const padding = 30;
        
        // 清空画布
        ctx.clearRect(0, 0, width, height);
        
        // 绘制背景网格
        ctx.strokeStyle = '#e5e5e5';
        ctx.lineWidth = 1;
        for (let i = 0; i <= 5; i++) {
            const y = padding + (height - 2 * padding) * i / 5;
            ctx.beginPath();
            ctx.moveTo(padding, y);
            ctx.lineTo(width - padding, y);
            ctx.stroke();
        }
        
        // 绘制数据线
        const chartWidth = width - 2 * padding;
        const chartHeight = height - 2 * padding;
        const stepX = chartWidth / (this.trendData.length - 1);
        
        // 状态映射到Y坐标
        const statusY = {
            'OK': padding + chartHeight * 0.2,
            'NG': padding + chartHeight * 0.5,
            'Error': padding + chartHeight * 0.8
        };
        
        // 绘制连线
        ctx.strokeStyle = '#1890ff';
        ctx.lineWidth = 2;
        ctx.beginPath();
        
        this.trendData.forEach((point, index) => {
            const x = padding + index * stepX;
            const y = statusY[point.status] || padding + chartHeight * 0.5;
            
            if (index === 0) {
                ctx.moveTo(x, y);
            } else {
                ctx.lineTo(x, y);
            }
        });
        ctx.stroke();
        
        // 绘制数据点
        this.trendData.forEach((point, index) => {
            const x = padding + index * stepX;
            const y = statusY[point.status] || padding + chartHeight * 0.5;
            
            // 根据状态设置颜色
            if (point.status === 'OK') {
                ctx.fillStyle = '#52c41a';
            } else if (point.status === 'NG') {
                ctx.fillStyle = '#f5222d';
            } else {
                ctx.fillStyle = '#faad14';
            }
            
            ctx.beginPath();
            ctx.arc(x, y, 5, 0, Math.PI * 2);
            ctx.fill();
        });
        
        // 绘制Y轴标签
        ctx.fillStyle = '#666';
        ctx.font = '12px sans-serif';
        ctx.textAlign = 'right';
        ctx.fillText('OK', padding - 5, padding + chartHeight * 0.2 + 4);
        ctx.fillText('NG', padding - 5, padding + chartHeight * 0.5 + 4);
        ctx.fillText('Error', padding - 5, padding + chartHeight * 0.8 + 4);
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
        
        if (this.filteredResults.length === 0) {
            listContainer.innerHTML = '<p class="empty-text">暂无检测结果</p>';
            return;
        }
        
        // 计算当前页的数据
        const startIndex = (this.currentPage - 1) * this.pageSize;
        const endIndex = Math.min(startIndex + this.pageSize, this.filteredResults.length);
        const pageResults = this.filteredResults.slice(startIndex, endIndex);
        
        const listHtml = pageResults.map((result, index) => {
            const statusClass = result.status?.toLowerCase() || 'unknown';
            const time = result.timestamp ? new Date(result.timestamp).toLocaleTimeString() : '--:--:--';
            const globalIndex = startIndex + index;
            
            return `
                <div class="result-item result-${statusClass}" data-index="${globalIndex}">
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
                    ${result.executionTimeMs ? `
                        <div class="result-meta">
                            处理时间: ${result.executionTimeMs}ms
                        </div>
                    ` : ''}
                </div>
            `;
        }).join('');
        
        listContainer.innerHTML = `
            <div class="results-info">
                显示 ${startIndex + 1}-${endIndex} 条，共 ${this.filteredResults.length} 条
                ${this.results.length !== this.filteredResults.length ? ` (已筛选，总计 ${this.results.length} 条)` : ''}
            </div>
            ${listHtml}
        `;
        
        // 绑定点击事件
        listContainer.querySelectorAll('.result-item').forEach(item => {
            item.addEventListener('click', (e) => {
                const index = parseInt(e.currentTarget.dataset.index);
                this.onResultClick?.(this.filteredResults[index]);
            });
        });
    }

    /**
     * 渲染分页控件
     */
    renderPagination() {
        if (this.filteredResults.length === 0) {
            const existingPagination = this.container.querySelector('.results-pagination');
            if (existingPagination) existingPagination.remove();
            return;
        }
        
        let paginationContainer = this.container.querySelector('.results-pagination');
        if (!paginationContainer) {
            paginationContainer = document.createElement('div');
            paginationContainer.className = 'results-pagination';
            this.container.appendChild(paginationContainer);
        }
        
        let pageButtons = '';
        const maxVisiblePages = 5;
        let startPage = Math.max(1, this.currentPage - Math.floor(maxVisiblePages / 2));
        let endPage = Math.min(this.totalPages, startPage + maxVisiblePages - 1);
        
        if (endPage - startPage < maxVisiblePages - 1) {
            startPage = Math.max(1, endPage - maxVisiblePages + 1);
        }
        
        // 上一页按钮
        pageButtons += `
            <button class="page-btn ${this.currentPage === 1 ? 'disabled' : ''}" 
                onclick="resultPanel.goToPage(${this.currentPage - 1})"
                ${this.currentPage === 1 ? 'disabled' : ''}>
                上一页
            </button>
        `;
        
        // 第一页
        if (startPage > 1) {
            pageButtons += `<button class="page-btn" onclick="resultPanel.goToPage(1)">1</button>`;
            if (startPage > 2) {
                pageButtons += `<span class="page-ellipsis">...</span>`;
            }
        }
        
        // 页码按钮
        for (let i = startPage; i <= endPage; i++) {
            pageButtons += `
                <button class="page-btn ${i === this.currentPage ? 'active' : ''}" 
                    onclick="resultPanel.goToPage(${i})">
                    ${i}
                </button>
            `;
        }
        
        // 最后一页
        if (endPage < this.totalPages) {
            if (endPage < this.totalPages - 1) {
                pageButtons += `<span class="page-ellipsis">...</span>`;
            }
            pageButtons += `<button class="page-btn" onclick="resultPanel.goToPage(${this.totalPages})">${this.totalPages}</button>`;
        }
        
        // 下一页按钮
        pageButtons += `
            <button class="page-btn ${this.currentPage === this.totalPages ? 'disabled' : ''}" 
                onclick="resultPanel.goToPage(${this.currentPage + 1})"
                ${this.currentPage === this.totalPages ? 'disabled' : ''}>
                下一页
            </button>
        `;
        
        // 页码跳转
        pageButtons += `
            <span class="page-jump">
                跳至 <input type="number" min="1" max="${this.totalPages}" value="${this.currentPage}" 
                    onchange="resultPanel.goToPage(Math.min(Math.max(1, parseInt(this.value) || 1), ${this.totalPages}))"> 页
            </span>
        `;
        
        paginationContainer.innerHTML = pageButtons;
    }

    /**
     * 导出结果
     */
    exportResults(format = 'json') {
        if (this.filteredResults.length === 0) {
            alert('没有可导出的结果');
            return;
        }
        
        let content, filename, mimeType;
        
        switch (format) {
            case 'json':
                content = JSON.stringify(this.filteredResults, null, 2);
                filename = `inspection_results_${Date.now()}.json`;
                mimeType = 'application/json';
                break;
                
            case 'csv':
                content = this.convertToCSV(this.filteredResults);
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
            r.executionTimeMs || '',
            r.defects?.[0]?.confidence ? (r.defects[0].confidence * 100).toFixed(1) + '%' : ''
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
        return this.filteredResults[0] || null;
    }

    /**
     * 获取所有结果
     */
    getAllResults() {
        return [...this.filteredResults];
    }
}

// 创建全局实例供HTML事件使用
let resultPanel = null;

export default ResultPanel;
export { ResultPanel };