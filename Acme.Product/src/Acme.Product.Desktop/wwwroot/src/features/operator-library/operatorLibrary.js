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
            draggable: false,
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
                    console.log('[OperatorLibrary] renderNode 渲染算子:', node.label);
                    element.innerHTML = `
                        <span class="operator-drag-handle">⋮⋮</span>
                        <span class="operator-icon">${node.icon || '📦'}</span>
                        <span class="operator-name">${node.label}</span>
                    `;
                    element.draggable = true;
                    element.classList.add('operator-draggable');
                    console.log('[OperatorLibrary] 算子元素设置完成, draggable:', element.draggable, 'classList:', element.className);
                    // 注：不再在此绑定 dragstart 事件，改用事件委托
                    // 防止 TreeView 重绘后事件丢失
                }
                // 不返回 element，因为已经在原对象上修改
                // treeView.js 会检查返回值，如果不返回则使用原 element
            }
        });

        // 使用事件委托处理拖拽 - 修复拖拽失效问题
        // 事件绑定在容器上，TreeView 重绘不会导致事件丢失
        treeContainer.addEventListener('dragstart', (e) => {
            console.log('[OperatorLibrary] dragstart 事件触发', e.target);
            
            const operatorEl = e.target.closest('.operator-draggable');
            if (!operatorEl) {
                console.log('[OperatorLibrary] 未找到 .operator-draggable 元素');
                return;
            }
            console.log('[OperatorLibrary] 找到算子元素:', operatorEl);
            
            // 从父级 li 元素获取节点 ID
            const li = operatorEl.closest('[data-id]');
            if (!li) {
                console.log('[OperatorLibrary] 未找到父级 li 元素');
                return;
            }
            console.log('[OperatorLibrary] 找到 li 元素, data-id:', li.dataset.id);
            
            const nodeId = li.dataset.id;
            // 从 treeView 中查找对应的节点数据
            const node = this.treeView.findNode(nodeId);
            console.log('[OperatorLibrary] 查找节点结果:', node);
            
            if (node && node.data) {
                // 设置数据传递
                e.dataTransfer.setData('application/json', JSON.stringify(node.data));
                e.dataTransfer.effectAllowed = 'copy';
                
                // 【修复】备份数据到全局变量，防止 WebView2 环境下 dataTransfer 数据丢失
                window.__draggingOperatorData = node.data;
                console.log('[OperatorLibrary] 开始拖拽算子:', node.data.type);

                operatorEl.classList.add('dragging');
                
                if (this.onOperatorDragStart) {
                    this.onOperatorDragStart(node.data);
                }
                
                // 监听拖拽结束事件，移除样式
                const onDragEnd = () => {
                    operatorEl.classList.remove('dragging');
                    // 延迟清理全局变量，确保 drop 事件能读取到
                    setTimeout(() => {
                        if (window.__draggingOperatorData === node.data) {
                            window.__draggingOperatorData = null;
                        }
                    }, 500);
                    operatorEl.removeEventListener('dragend', onDragEnd);
                };
                operatorEl.addEventListener('dragend', onDragEnd);
            }
        });
    }

    /**
     * 加载算子列表
     */
    async loadOperators() {
        try {
            // 从后端获取算子库
            const operators = await httpClient.get('/operators/library');
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
            { 
                type: 'ImageAcquisition', 
                displayName: '图像采集', 
                category: '输入', 
                icon: '📷', 
                description: '从相机或文件获取图像',
                parameters: [
                    { name: 'sourceType', displayName: '采集源', type: 'enum', dataType: 'enum', defaultValue: 'camera', options: [{label: '相机', value: 'camera'}, {label: '文件', value: 'file'}] },
                    { name: 'filePath', displayName: '文件路径', type: 'file', dataType: 'file', defaultValue: '', description: '支持 .bmp, .png, .jpg' },
                    { name: 'exposureTime', displayName: '曝光时间', type: 'int', dataType: 'int', defaultValue: 5000, min: 100, max: 1000000, description: '单位: us' },
                    { name: 'gain', displayName: '增益', type: 'float', dataType: 'float', defaultValue: 1.0, min: 0.0, max: 24.0 }
                ]
            },
            { 
                type: 'Filtering', 
                displayName: '滤波', 
                category: '预处理', 
                icon: '🔍', 
                description: '图像滤波降噪处理',
                parameters: [
                    { name: 'method', displayName: '滤波方法', type: 'enum', dataType: 'enum', defaultValue: 'gaussian', options: [{label: '高斯滤波', value: 'gaussian'}, {label: '中值滤波', value: 'median'}, {label: '均值滤波', value: 'mean'}] },
                    { name: 'kernelSize', displayName: '核大小', type: 'int', dataType: 'int', defaultValue: 3, min: 3, max: 15, description: '必须为奇数' }
                ]
            },
            { 
                type: 'EdgeDetection', 
                displayName: '边缘检测', 
                category: '特征提取', 
                icon: '〰️', 
                description: '检测图像边缘特征',
                parameters: [
                    { name: 'algorithm', displayName: '算子类型', type: 'enum', dataType: 'enum', defaultValue: 'canny', options: [{label: 'Canny', value: 'canny'}, {label: 'Sobel', value: 'sobel'}, {label: 'Laplacian', value: 'laplacian'}] },
                    { name: 'threshold1', displayName: '阈值 1', type: 'int', dataType: 'int', defaultValue: 50, min: 0, max: 255 },
                    { name: 'threshold2', displayName: '阈值 2', type: 'int', dataType: 'int', defaultValue: 150, min: 0, max: 255 }
                ]
            },
            { 
                type: 'Thresholding', 
                displayName: '二值化', 
                category: '预处理', 
                icon: '⚫', 
                description: '图像阈值分割',
                parameters: [
                    { name: 'method', displayName: '阈值方法', type: 'enum', dataType: 'enum', defaultValue: 'fixed', options: [{label: '固定阈值', value: 'fixed'}, {label: 'Otsu', value: 'otsu'}, {label: 'Adaptive', value: 'adaptive'}] },
                    { name: 'threshold', displayName: '阈值', type: 'int', dataType: 'int', defaultValue: 128, min: 0, max: 255 },
                    { name: 'invert', displayName: '反转结果', type: 'bool', dataType: 'bool', defaultValue: false }
                ]
            },
            { 
                type: 'Morphology', 
                displayName: '形态学', 
                category: '预处理', 
                icon: '🔄', 
                description: '腐蚀、膨胀、开闭运算',
                parameters: [
                    { name: 'operation', displayName: '操作类型', type: 'enum', dataType: 'enum', defaultValue: 'erode', options: [{label: '腐蚀', value: 'erode'}, {label: '膨胀', value: 'dilate'}, {label: '开运算', value: 'open'}, {label: '闭运算', value: 'close'}] },
                    { name: 'kernelSize', displayName: '核大小', type: 'int', dataType: 'int', defaultValue: 3, min: 3, max: 21 },
                    { name: 'iterations', displayName: '迭代次数', type: 'int', dataType: 'int', defaultValue: 1, min: 1, max: 10 }
                ]
            },
            { 
                type: 'BlobAnalysis', 
                displayName: 'Blob分析', 
                category: '特征提取', 
                icon: '🔵', 
                description: '连通区域分析',
                parameters: [
                    { name: 'minArea', displayName: '最小面积', type: 'int', dataType: 'int', defaultValue: 100, min: 0 },
                    { name: 'maxArea', displayName: '最大面积', type: 'int', dataType: 'int', defaultValue: 100000, min: 0 },
                    { name: 'color', displayName: '目标颜色', type: 'enum', dataType: 'enum', defaultValue: 'white', options: [{label: '白色', value: 'white'}, {label: '黑色', value: 'black'}] }
                ]
            },
            { 
                type: 'TemplateMatching', 
                displayName: '模板匹配', 
                category: '检测', 
                icon: '🎯', 
                description: '图像模板匹配定位',
                parameters: [
                    { name: 'method', displayName: '匹配方法', type: 'enum', dataType: 'enum', defaultValue: 'ncc', options: [{label: '归一化相关 (NCC)', value: 'ncc'}, {label: '平方差 (SQDIFF)', value: 'sqdiff'}] },
                    { name: 'threshold', displayName: '匹配分数阈值', type: 'float', dataType: 'float', defaultValue: 0.8, min: 0.1, max: 1.0 },
                    { name: 'maxMatches', displayName: '最大匹配数', type: 'int', dataType: 'int', defaultValue: 1, min: 1, max: 100 }
                ]
            },
            { 
                type: 'Measurement', 
                displayName: '测量', 
                category: '检测', 
                icon: '📏', 
                description: '几何尺寸测量',
                parameters: [
                    { name: 'type', displayName: '测量类型', type: 'enum', dataType: 'enum', defaultValue: 'distance', options: [{label: '距离', value: 'distance'}, {label: '角度', value: 'angle'}, {label: '圆径', value: 'radius'}] }    
                ]
            },
            { 
                type: 'DeepLearning', 
                displayName: '深度学习', 
                category: 'AI检测', 
                icon: '🧠', 
                description: 'AI缺陷检测',
                parameters: [
                    { name: 'modelPath', displayName: '模型路径', type: 'file', dataType: 'file', defaultValue: '' },
                    { name: 'confidence', displayName: '置信度阈值', type: 'float', dataType: 'float', defaultValue: 0.5, min: 0.0, max: 1.0 }
                ]
            },
            { 
                type: 'ResultOutput', 
                displayName: '结果输出', 
                category: '输出', 
                icon: '📤', 
                description: '输出检测结果',
                parameters: [
                    { name: 'format', displayName: '输出格式', type: 'enum', dataType: 'enum', defaultValue: 'json', options: [{label: 'JSON', value: 'json'}, {label: 'CSV', value: 'csv'}, {label: 'Text', value: 'text'}] },
                    { name: 'saveToFile', displayName: '保存到文件', type: 'bool', dataType: 'bool', defaultValue: true }
                ]
            }
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
