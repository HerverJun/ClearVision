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
import {
    applyFeatureToButton,
    getFeatureBadge,
    getFeatureDescription,
    isFeatureEnabled
} from '../../shared/featureRegistry.js';
import {
    getCategoryIconPath as getSharedCategoryIconPath,
    getOperatorIconPath as getSharedOperatorIconPath
} from '../../shared/operatorVisuals.js';
import {
    createCategoryIconElement,
    createOperatorIconElement,
    createPathIconElement,
    normalizeOperatorIconName,
    renderOperatorIconInto
} from '../../shared/operatorIconRenderer.js';

export class OperatorLibraryPanel {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.treeView = null;
        this.operators = [];
        this.filteredOperators = [];
        this.categories = new Map();
        this.metadataByType = new Map();

        // 事件回调
        this.onOperatorDragStart = null;
        this.onOperatorSelected = null;

        // 【新增】展开状态持久化
        this.storageKey = 'operator-library-expanded-categories';

        this.initialize();
    }

    /**
     * 初始化面板
     */
    initialize() {
        // 【修复】页面加载时清理可能残留的全局拖拽数据
        if (window.__draggingOperatorData) {
            window.__draggingOperatorData = null;
        }
        
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
                        <span class="preview-svg-icon"></span>
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

        const placeholderIcon = this.container.querySelector('.preview-svg-icon');
        placeholderIcon?.replaceChildren(createPathIconElement(
            'M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L5.03 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z',
            'preview-svg-icon',
            '#9ca3af').firstElementChild);
        
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
            onExpand: () => this.saveExpandedState(),
            onCollapse: () => this.saveExpandedState(),
            renderNode: (node, element) => {
                // 自定义渲染算子节点
                if (node.type === 'operator') {
                    console.log('[OperatorLibrary] renderNode 渲染算子:', node.label);
                    const operator = node.data || {};
                    const content = document.createElement('div');
                    content.className = 'operator-item-content';

                    const dragHandle = document.createElement('span');
                    dragHandle.className = 'operator-drag-handle';
                    dragHandle.textContent = '⋮⋮';

                    const icon = createOperatorIconElement(operator, 'operator-icon');

                    const info = document.createElement('div');
                    info.className = 'operator-info';

                    const label = document.createElement('span');
                    label.className = 'operator-name';
                    label.textContent = node.label || '';

                    const description = document.createElement('span');
                    description.className = 'operator-desc';
                    description.textContent = operator?.description || '';

                    info.append(label, description);
                    content.append(dragHandle, icon, info);
                    element.replaceChildren(content);
                    element.draggable = true;
                    element.classList.add('operator-draggable');
                    element.classList.add('operator-with-preview');
                    
                    // 添加拖拽预览效果
                    element.addEventListener('dragstart', (e) => {
                        element.classList.add('dragging-shadow');
                    });
                    
                    element.addEventListener('dragend', (e) => {
                        element.classList.remove('dragging-shadow');
                    });
                    
                    console.log('[OperatorLibrary] 算子元素设置完成, draggable:', element.draggable, 'classList:', element.className);
                } else {
                    // 【新增】分类节点 - 自定义渲染包含展开/收起按钮
                    const hasChildren = node.children && node.children.length > 0;
                    const isExpanded = this.treeView.expandedNodes.has(node.id) || node.expanded;
                    const count = node.children ? node.children.length : 0;

                    element.replaceChildren();

                    // 展开/收起按钮
                    if (hasChildren) {
                        const toggle = document.createElement('span');
                        toggle.className = `cv-treeview-toggle ${isExpanded ? 'expanded' : 'collapsed'}`;
                        toggle.dataset.nodeId = node.id || '';
                        toggle.appendChild(createPathIconElement(
                            isExpanded
                                ? 'M7 10l5 5 5-5z'
                                : 'M8.59 16.59L13.17 12 8.59 7.41 10 6l6 6-6 6-1.41-1.41z',
                            'cv-treeview-toggle-icon',
                            '#d4a853'));
                        element.appendChild(toggle);
                    } else {
                        const placeholder = document.createElement('span');
                        placeholder.className = 'cv-treeview-toggle-placeholder';
                        element.appendChild(placeholder);
                    }

                    // 分类内容包装器
                    const wrapper = document.createElement('div');
                    wrapper.className = 'category-content-wrapper';

                    const icon = createCategoryIconElement(node.label, 'tree-node-icon category-icon');

                    const label = document.createElement('span');
                    label.className = 'tree-node-label category-label';
                    label.textContent = node.label || '';

                    wrapper.append(icon, label);
                    if (!isExpanded && count > 0) {
                        const countBadge = document.createElement('span');
                        countBadge.className = 'category-count';
                        countBadge.textContent = String(count);
                        wrapper.appendChild(countBadge);
                    }

                    element.appendChild(wrapper);

                    // 绑定展开/收起事件
                    if (hasChildren) {
                        const toggle = element.querySelector('.cv-treeview-toggle');

                        const toggleHandler = (e) => {
                            e.preventDefault();
                            e.stopPropagation();
                            console.log('[OperatorLibrary] Toggle clicked, node.id:', node.id);
                            
                            // 通过 id 查找正确的节点对象
                            const actualNode = this.treeView.findNode(node.id);
                            
                            if (actualNode) {
                                this.treeView.toggleNode(actualNode);
                                console.log('[OperatorLibrary] After toggle, expandedNodes:', Array.from(this.treeView.expandedNodes));
                            }
                            return false;
                        };
                        
                        if (toggle) {
                            toggle.onclick = toggleHandler;
                            console.log('[OperatorLibrary] Toggle onclick bound for:', node.id);
                        }
                        
                        // 点击分类内容也可以展开/收起
                        if (wrapper) {
                            wrapper.style.cursor = 'pointer';
                            wrapper.onclick = toggleHandler;
                            console.log('[OperatorLibrary] Wrapper onclick bound for:', node.id);
                        }
                    }
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
            const operators = await this.loadOperatorsFromMetadata();
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

    async loadOperatorsFromMetadata() {
        try {
            const types = await httpClient.get('/operators/types');
            if (Array.isArray(types) && types.length > 0) {
                const operators = await Promise.all(types.map(async (type) => {
                    const typeIdentifier = typeof type === 'string'
                        ? type
                        : (type?.name || type?.Name || type?.type || type?.Type || String(type));
                    try {
                        const metadata = await httpClient.get(`/operators/${encodeURIComponent(typeIdentifier)}/metadata`);
                        const normalized = this.normalizeOperatorMetadata(metadata, typeIdentifier);
                        if (normalized) {
                            this.metadataByType.set(normalized.type, normalized);
                        }
                        return normalized;
                    } catch (error) {
                        console.warn('[OperatorLibraryPanel] 加载算子元数据失败:', typeIdentifier, error);
                        return null;
                    }
                }));

                const validOperators = operators.filter(Boolean);
                if (validOperators.length > 0) {
                    return validOperators;
                }
            }
        } catch (error) {
            console.warn('[OperatorLibraryPanel] 获取算子类型失败，回退到算子库接口:', error);
        }

        const operators = await httpClient.get('/operators/library');
        return operators.map(operator => this.normalizeOperatorMetadata(operator, operator.type || operator.Type)).filter(Boolean);
    }

    normalizeOperatorMetadata(metadata, fallbackType = '') {
        if (!metadata || typeof metadata !== 'object') {
            return null;
        }

        const type = String(metadata.type || metadata.Type || fallbackType || '').trim();
        if (!type) {
            return null;
        }

        const category = metadata.category || metadata.Category || '其他';
        const displayName = metadata.displayName || metadata.DisplayName || metadata.name || metadata.Name || type;
        const parameters = metadata.parameters || metadata.Parameters || [];
        const inputPorts = metadata.inputPorts || metadata.InputPorts || [];
        const outputPorts = metadata.outputPorts || metadata.OutputPorts || [];
        const iconName = normalizeOperatorIconName(metadata);

        return {
            ...metadata,
            type,
            category,
            displayName,
            iconName,
            description: metadata.description || metadata.Description || '暂无描述',
            parameters,
            inputPorts,
            outputPorts,
            inputType: metadata.inputType || metadata.InputType || (inputPorts[0]?.dataType || inputPorts[0]?.DataType || '图像'),
            outputType: metadata.outputType || metadata.OutputType || (outputPorts[0]?.dataType || outputPorts[0]?.DataType || '图像/数据')
        };
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
                    { name: 'SourceType', displayName: '采集源', type: 'enum', dataType: 'enum', defaultValue: 'File', options: [{label: '文件', value: 'File'}, {label: '相机', value: 'Camera'}] },
                    { name: 'CameraId', displayName: '相机', type: 'cameraBinding', dataType: 'cameraBinding', defaultValue: '' },
                    { name: 'FilePath', displayName: '文件路径', type: 'file', dataType: 'file', defaultValue: '', description: '支持 .bmp, .png, .jpg' }
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
            },
            { 
                type: 'ResultJudgment', 
                displayName: '结果判定', 
                category: '流程控制', 
                icon: '⚖️', 
                description: '通用判定逻辑（数量/范围/阈值），输出OK/NG结果',
                inputPorts: [
                    { name: 'Value', displayName: '输入值', dataType: 'Any', isRequired: true },
                    { name: 'Confidence', displayName: '置信度', dataType: 'Float', isRequired: false }
                ],
                outputPorts: [
                    { name: 'JudgmentResult', displayName: '判定结果', dataType: 'String' },
                    { name: 'IsOk', displayName: '是否OK', dataType: 'Boolean' },
                    { name: 'Details', displayName: '详细信息', dataType: 'String' }
                ],
                parameters: [
                    { name: 'FieldName', displayName: '判定字段', type: 'string', dataType: 'string', defaultValue: 'Value' },
                    { name: 'Condition', displayName: '判定条件', type: 'enum', dataType: 'enum', defaultValue: 'Equal', options: [
                        {label: '等于', value: 'Equal'}, {label: '大于', value: 'GreaterThan'}, {label: '小于', value: 'LessThan'}, {label: '范围内', value: 'Range'}
                    ]},
                    { name: 'ExpectValue', displayName: '期望值', type: 'string', dataType: 'string', defaultValue: '1' }
                ]
            }
        ];
    }

    getOperatorIconName(operator) {
        return normalizeOperatorIconName(operator);
    }

    /**
     * 获取算子图标的 SVG Path 字符串
     * 按 type 匹配，未匹配则尝试 category，最后使用默认图标
     */
    getOperatorIconPath(type, category = null, iconName = null) {
        return getSharedOperatorIconPath(type, category, iconName);
    }

    /**
     * 渲染算子树
     */
    renderOperatorTree() {
        // 按类别分组
        const grouped = this.groupByCategory(this.filteredOperators);
        
        // 构建树形数据
        const treeData = Object.entries(grouped).map(([category, operators]) => {
            const categoryIconPath = getSharedCategoryIconPath(category);
            return {
                id: `category_${category}`,
                label: category,
                type: 'category',
                icon: null, // 禁止 TreeView 默认渲染 (会转义 SVG)
                fallbackIconPath: categoryIconPath,
                expanded: true,
                children: operators.map((op, index) => {
                    // 预先获取图标路径并注入到 operator 数据中，
                    // 这样拖拽到画布时，flowEditorInteraction.js 就能直接使用正确的图标
                    const iconName = this.getOperatorIconName(op);
                    const iconPath = this.getOperatorIconPath(op.type, category, iconName);
                    op.iconPath = iconPath;

                    return {
                        id: `operator_${op.type}_${index}`,
                        label: op.displayName || op.name,
                        type: 'operator',
                        icon: null, // 禁止 TreeView 默认渲染
                        fallbackIconPath: iconPath,
                        data: op
                    };
                })
            };
        });
        
        this.treeView.setData(treeData);

        // 【关键修复】初始化 expandedNodes，确保所有设置了 expanded: true 的节点 ID 都在 Set 中
        // 这样第一次点击就能正确切换状态
        treeData.forEach(node => {
            if (node.expanded) {
                this.treeView.expandedNodes.add(node.id);
            }
        });
        
        // 【新增】加载保存的展开状态（覆盖默认值）
        this.loadExpandedState();
        
        // 【调试】打印展开状态
        console.log('[OperatorLibrary] After setData, expandedNodes:', Array.from(this.treeView.expandedNodes));
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
     * 【新增】保存展开状态到 localStorage
     */
    saveExpandedState() {
        try {
            const expandedIds = Array.from(this.treeView.expandedNodes);
            localStorage.setItem(this.storageKey, JSON.stringify(expandedIds));
        } catch (e) {
            console.warn('[OperatorLibrary] Failed to save expanded state:', e);
        }
    }

    /**
     * 【新增】从 localStorage 加载展开状态
     */
    loadExpandedState() {
        try {
            const saved = localStorage.getItem(this.storageKey);
            console.log('[OperatorLibrary] Loading expanded state from localStorage:', saved);
            if (saved) {
                const expandedIds = JSON.parse(saved);
                console.log('[OperatorLibrary] Parsed expandedIds:', expandedIds);
                expandedIds.forEach(id => this.treeView.expandedNodes.add(id));
                console.log('[OperatorLibrary] After loading, expandedNodes:', Array.from(this.treeView.expandedNodes));
                // 加载状态后重新渲染
                this.treeView.render();
            }
        } catch (e) {
            console.warn('[OperatorLibrary] Failed to load expanded state:', e);
        }
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
        const resolvedOperator = this.metadataByType.get(operator.type) || operator;
        const inputPorts = Array.isArray(resolvedOperator.inputPorts) ? resolvedOperator.inputPorts : [];
        const outputPorts = Array.isArray(resolvedOperator.outputPorts) ? resolvedOperator.outputPorts : [];
        const detailIconPath = resolvedOperator.iconPath ||
            this.getOperatorIconPath(resolvedOperator.type, resolvedOperator.category, this.getOperatorIconName(resolvedOperator));
        resolvedOperator.iconPath = detailIconPath;
        const displayName = this.escapeHtml(resolvedOperator.displayName || resolvedOperator.name || '未命名算子');
        const category = this.escapeHtml(resolvedOperator.category || '其他');
        const type = this.escapeHtml(resolvedOperator.type || '');
        const description = this.escapeHtml(resolvedOperator.description || '暂无描述');
        const inputType = this.escapeHtml(resolvedOperator.inputType || '图像');
        const outputType = this.escapeHtml(resolvedOperator.outputType || '图像/数据');
        const featureBadge = this.escapeHtml(getFeatureBadge('operator.autotuneStrategies'));
        const featureDescription = this.escapeHtml(getFeatureDescription('operator.autotuneStrategies'));
        const renderPortLabel = (port, fallbackName = '未命名') => {
            const name = port.displayName || port.DisplayName || port.name || port.Name || fallbackName;
            const dataType = port.dataType || port.DataType || 'Any';
            return `${this.escapeHtml(name)} (${this.escapeHtml(dataType)})`;
        };
        
        preview.innerHTML = `
            <div class="operator-detail">
                <div class="detail-header">
                    <span class="detail-icon"></span>
                    <h4>${displayName}</h4>
                </div>
                <div class="detail-meta">
                    <span class="detail-category">${category}</span>
                    <span class="detail-type">${type}</span>
                </div>
                <p class="detail-description">${description}</p>
                
                <div class="detail-params">
                    <h5>参数配置</h5>
                    ${this.renderParameterList(resolvedOperator.parameters)}
                </div>
                
                <div class="detail-ports">
                    <h5>端口定义</h5>
                    <div class="ports-list">
                        ${inputPorts.length > 0
                            ? inputPorts.map(port => `
                                <div class="port-item input">
                                    <span class="port-dot input"></span>
                                    <span>输入: ${renderPortLabel(port)}</span>
                                </div>
                            `).join('')
                            : `
                                <div class="port-item input">
                                    <span class="port-dot input"></span>
                                    <span>输入: ${inputType}</span>
                                </div>
                            `}
                        ${outputPorts.length > 0
                            ? outputPorts.map(port => `
                                <div class="port-item output">
                                    <span class="port-dot output"></span>
                                    <span>输出: ${renderPortLabel(port)}</span>
                                </div>
                            `).join('')
                            : `
                                <div class="port-item output">
                                    <span class="port-dot output"></span>
                                    <span>输出: ${outputType}</span>
                                </div>
                            `}
                    </div>
                </div>
                <div class="detail-actions" style="margin-top:16px;">
                    <button class="cv-btn cv-btn-secondary" id="btn-show-autotune-strategies">查看自动调参策略</button>
                    <div class="params-empty" style="margin-top:8px;">${featureBadge}：${featureDescription}</div>
                    <div id="autotune-strategies-panel" style="margin-top:12px;"></div>
                </div>
            </div>
        `;

        renderOperatorIconInto(preview.querySelector('.detail-icon'), resolvedOperator, 'detail-icon');

        const autotuneButton = preview.querySelector('#btn-show-autotune-strategies');
        applyFeatureToButton(autotuneButton, 'operator.autotuneStrategies', { fallbackLabel: '查看自动调参策略' });

        autotuneButton?.addEventListener('click', async () => {
            const panel = preview.querySelector('#autotune-strategies-panel');
            if (!panel) {
                return;
            }

            if (!isFeatureEnabled('operator.autotuneStrategies')) {
                panel.innerHTML = `<div class="params-empty">${this.escapeHtml(getFeatureDescription('operator.autotuneStrategies', '该能力当前不可用'))}</div>`;
                return;
            }

            panel.innerHTML = '<div class="params-empty">正在加载自动调参策略...</div>';

            try {
                const strategies = await httpClient.get('/autotune/strategies');
                if (!Array.isArray(strategies) || strategies.length === 0) {
                    panel.innerHTML = '<div class="params-empty">暂无可用自动调参策略</div>';
                    return;
                }

                panel.innerHTML = `
                    <div class="detail-params">
                        <h5>自动调参策略</h5>
                        <ul class="params-list">
                            ${strategies.map(strategy => `
                                <li class="param-item">
                                    <span class="param-name">${this.escapeHtml(strategy.name || strategy.Name || '未命名策略')}</span>
                                    <span class="param-type">${this.escapeHtml(strategy.category || strategy.Category || '策略')}</span>
                                    <span class="param-default">${this.escapeHtml(strategy.description || strategy.Description || '暂无描述')}</span>
                                </li>
                            `).join('')}
                        </ul>
                    </div>
                `;
            } catch (error) {
                console.error('[OperatorLibraryPanel] 获取自动调参策略失败:', error);
                panel.innerHTML = `<div class="params-empty">加载失败：${this.escapeHtml(error.message)}</div>`;
            }
        });
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
                        <span class="param-name">${this.escapeHtml(param.name)}</span>
                        <span class="param-type">${this.escapeHtml(param.type)}</span>
                        <span class="param-default">默认: ${this.escapeHtml(param.defaultValue)}</span>
                    </li>
                `).join('')}
            </ul>
        `;
    }

    escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    escapeAttribute(value) {
        return this.escapeHtml(value);
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

