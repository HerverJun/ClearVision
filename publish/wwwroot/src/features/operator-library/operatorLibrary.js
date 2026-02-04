/**
 * OperatorLibraryPanel - 算子库面板组件
 * Sprint 4: S4-002 实现
 * 
 * 功能：
 * - 算子分类树形列表
 * - 拖拽算子到画布
 * - 算子搜索过滤
 * - 算子详情预览
 */

import TreeView from '../../shared/components/treeView.js';
import httpClient from '../../core/messaging/httpClient.js';
import { showToast, createInput } from '../../shared/components/uiComponents.js';

export class OperatorLibraryPanel {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.treeView = null;
        this.operators = [];
        this.filteredOperators = [];
        this.categories = new Map();
        
        // 事件回调
        this.onOperatorDragStart = null;
        this.onOperatorSelected = null;
        
        this.initialize();
    }

    /**
     * 初始化面板
     */
    initialize() {
        this.renderUI();
        this.initializeTreeView();
        this.loadOperators();
    }

    /**
     * 渲染UI结构
     */
    renderUI() {
        this.container.innerHTML = `
            <div class="operator-library-wrapper">
                <!-- 搜索栏 -->
                <div class="library-search">
                    <input type="text" 
                           id="operator-search" 
                           class="cv-input" 
                           placeholder="搜索算子..."
                           autocomplete="off">
                    <button id="btn-clear-search" class="cv-btn cv-btn-icon" title="清除搜索">✕</button>
                </div>
                
                <!-- 算子树形列表 -->
                <div class="library-tree" id="library-tree"></div>
                
                <!-- 算子详情预览 -->
                <div class="operator-preview" id="operator-preview">
                    <div class="preview-placeholder">
                        <span>📦</span>
                        <p>选择一个算子查看详情</p>
                    </div>
                </div>
                
                <!-- 快捷操作 -->
                <div class="library-actions">
                    <button id="btn-expand-all" class="cv-btn cv-btn-secondary" title="展开全部">📂</button>
                    <button id="btn-collapse-all" class="cv-btn cv-btn-secondary" title="折叠全部">📁</button>
                    <button id="btn-refresh" class="cv-btn cv-btn-secondary" title="刷新列表">🔄</button>
                </div>
            </div>
        `;
        
        this.bindSearchEvents();
        this.bindActionEvents();
    }

    /**
     * 初始化树形控件
     */
    initializeTreeView() {
        const treeContainer = this.container.querySelector('#library-tree');
        
        this.treeView = new TreeView(treeContainer, {
            selectable: true,
            multiSelect: false,
            draggable: true,
            onSelect: (node) => {
                if (node.type === 'operator') {
                    this.showOperatorPreview(node.data);
                    if (this.onOperatorSelected) {
                        this.onOperatorSelected(node.data);
                    }
                }
            },
            renderNode: (node, element) => {
                // 自定义渲染算子节点
                if (node.type === 'operator') {
                    element.innerHTML = `
                        <span class="operator-drag-handle">⋮⋮</span>
                        <span class="operator-icon">${node.icon || '📦'}</span>
                        <span class="operator-name">${node.label}</span>
                    `;
                    element.draggable = true;
                    element.classList.add('operator-draggable');
                    
                    // 绑定拖拽事件
                    element.addEventListener('dragstart', (e) => {
                        e.dataTransfer.setData('application/json', JSON.stringify(node.data));
                        e.dataTransfer.effectAllowed = 'copy';
                        element.classList.add('dragging');
                        
                        if (this.onOperatorDragStart) {
                            this.onOperatorDragStart(node.data);
                        }
                    });
                    
                    element.addEventListener('dragend', () => {
                        element.classList.remove('dragging');
                    });
                }
                return element;
            }
        });
    }

    /**
     * 加载算子列表
     */
    async loadOperators() {
        try {
            // 从后端获取算子库
            const operators = await httpClient.get('/api/operators/library');
            this.operators = operators;
            this.filteredOperators = operators;
            this.renderOperatorTree();
            showToast(`已加载 ${operators.length} 个算子`, 'success');
        } catch (error) {
            console.error('[OperatorLibraryPanel] 加载算子失败:', error);
            // 使用默认算子数据
            this.operators = this.getDefaultOperators();
            this.filteredOperators = this.operators;
            this.renderOperatorTree();
            showToast('使用默认算子数据', 'warning');
        }
    }

    /**
     * 获取默认算子数据
     */
    getDefaultOperators() {
        return [
            { type: 'ImageAcquisition', displayName: '图像采集', category: '输入', icon: '📷', description: '从相机或文件获取图像' },
            { type: 'Filtering', displayName: '滤波', category: '预处理', icon: '🔍', description: '图像滤波降噪处理' },
            { type: 'EdgeDetection', displayName: '边缘检测', category: '特征提取', icon: '〰️', description: '检测图像边缘特征' },
            { type: 'Thresholding', displayName: '二值化', category: '预处理', icon: '⚫', description: '图像阈值分割' },
            { type: 'Morphology', displayName: '形态学', category: '预处理', icon: '🔄', description: '腐蚀、膨胀、开闭运算' },
            { type: 'BlobAnalysis', displayName: 'Blob分析', category: '特征提取', icon: '🔵', description: '连通区域分析' },
            { type: 'TemplateMatching', displayName: '模板匹配', category: '检测', icon: '🎯', description: '图像模板匹配定位' },
            { type: 'Measurement', displayName: '测量', category: '检测', icon: '📏', description: '几何尺寸测量' },
            { type: 'DeepLearning', displayName: '深度学习', category: 'AI检测', icon: '🧠', description: 'AI缺陷检测' },
            { type: 'ResultOutput', displayName: '结果输出', category: '输出', icon: '📤', description: '输出检测结果' }
        ];
    }

    /**
     * 渲染算子树
     */
    renderOperatorTree() {
        // 按类别分组
        const grouped = this.groupByCategory(this.filteredOperators);
        
        // 构建树形数据
        const treeData = Object.entries(grouped).map(([category, operators]) => ({
            id: `category_${category}`,
            label: category,
            type: 'category',
            icon: '📁',
            expanded: true,
            children: operators.map((op, index) => ({
                id: `operator_${op.type}_${index}`,
                label: op.displayName || op.name,
                type: 'operator',
                icon: op.icon || '📦',
                data: op
            }))
        }));
        
        this.treeView.setData(treeData);
    }

    /**
     * 按类别分组
     */
    groupByCategory(operators) {
        return operators.reduce((acc, op) => {
            const category = op.category || '其他';
            if (!acc[category]) {
                acc[category] = [];
            }
            acc[category].push(op);
            return acc;
        }, {});
    }

    /**
     * 绑定搜索事件
     */
    bindSearchEvents() {
        const searchInput = this.container.querySelector('#operator-search');
        const clearBtn = this.container.querySelector('#btn-clear-search');
        
        // 搜索输入
        let searchTimeout;
        searchInput.addEventListener('input', (e) => {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(() => {
                this.searchOperators(e.target.value);
            }, 300);
        });
        
        // 清除搜索
        clearBtn.addEventListener('click', () => {
            searchInput.value = '';
            this.searchOperators('');
        });
    }

    /**
     * 搜索算子
     */
    searchOperators(keyword) {
        if (!keyword.trim()) {
            this.filteredOperators = this.operators;
        } else {
            const lowerKeyword = keyword.toLowerCase();
            this.filteredOperators = this.operators.filter(op => 
                (op.displayName || op.name).toLowerCase().includes(lowerKeyword) ||
                (op.description && op.description.toLowerCase().includes(lowerKeyword)) ||
                (op.category && op.category.toLowerCase().includes(lowerKeyword))
            );
        }
        
        this.renderOperatorTree();
        
        // 显示搜索结果
        if (keyword.trim()) {
            showToast(`找到 ${this.filteredOperators.length} 个算子`, 'info');
        }
    }

    /**
     * 绑定操作按钮事件
     */
    bindActionEvents() {
        // 展开全部
        this.container.querySelector('#btn-expand-all').addEventListener('click', () => {
            this.treeView.expandAll();
        });
        
        // 折叠全部
        this.container.querySelector('#btn-collapse-all').addEventListener('click', () => {
            this.treeView.collapseAll();
        });
        
        // 刷新列表
        this.container.querySelector('#btn-refresh').addEventListener('click', () => {
            this.loadOperators();
        });
    }

    /**
     * 显示算子预览
     */
    showOperatorPreview(operator) {
        const preview = this.container.querySelector('#operator-preview');
        
        preview.innerHTML = `
            <div class="operator-detail">
                <div class="detail-header">
                    <span class="detail-icon">${operator.icon || '📦'}</span>
                    <h4>${operator.displayName || operator.name}</h4>
                </div>
                <div class="detail-meta">
                    <span class="detail-category">${operator.category || '其他'}</span>
                    <span class="detail-type">${operator.type}</span>
                </div>
                <p class="detail-description">${operator.description || '暂无描述'}</p>
                
                <div class="detail-params">
                    <h5>参数配置</h5>
                    ${this.renderParameterList(operator.parameters)}
                </div>
                
                <div class="detail-ports">
                    <h5>端口定义</h5>
                    <div class="ports-list">
                        <div class="port-item input">
                            <span class="port-dot input"></span>
                            <span>输入: ${operator.inputType || '图像'}</span>
                        </div>
                        <div class="port-item output">
                            <span class="port-dot output"></span>
                            <span>输出: ${operator.outputType || '图像/数据'}</span>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * 渲染参数列表
     */
    renderParameterList(parameters) {
        if (!parameters || parameters.length === 0) {
            return '<p class="params-empty">此算子无需配置参数</p>';
        }
        
        return `
            <ul class="params-list">
                ${parameters.map(param => `
                    <li class="param-item">
                        <span class="param-name">${param.name}</span>
                        <span class="param-type">${param.type}</span>
                        <span class="param-default">默认: ${param.defaultValue}</span>
                    </li>
                `).join('')}
            </ul>
        `;
    }

    /**
     * 获取算子列表
     */
    getOperators() {
        return this.operators;
    }

    /**
     * 获取分类列表
     */
    getCategories() {
        return [...new Set(this.operators.map(op => op.category || '其他'))];
    }

    /**
     * 刷新算子列表
     */
    refresh() {
        return this.loadOperators();
    }
}

export default OperatorLibraryPanel;
