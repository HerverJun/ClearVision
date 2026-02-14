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
                        <span class="preview-svg-icon">${this.getSvgIcon('M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L5.03 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z', '0 0 24 24')}</span>
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
                    const operator = node.data;
                    element.innerHTML = `
                        <div class="operator-item-content">
                            <span class="operator-drag-handle">⋮⋮</span>
                            <span class="operator-icon">${node.customIcon || node.icon || '📦'}</span>
                            <div class="operator-info">
                                <span class="operator-name">${node.label}</span>
                                <span class="operator-desc">${operator?.description || ''}</span>
                            </div>
                        </div>
                    `;
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
                    // 修复：对于分类节点，也需要自定义渲染以支持 SVG 图标 (innerHTML)
                    // 使用 customIcon 避免 TreeView 默认渲染将 SVG 转义
                    element.innerHTML = `
                        <span class="tree-node-icon">${node.customIcon || node.icon}</span>
                        <span class="tree-node-label">${node.label}</span>
                    `;
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
     * 获取SVG图标内容
     */
    getSvgIcon(path, viewBox = "0 0 24 24") {
        return `<svg viewBox="${viewBox}" width="16" height="16" fill="currentColor"><path d="${path}"/></svg>`;
    }

    /**
     * 获取算子图标的 SVG Path 字符串
     * 按 type 匹配，未匹配则尝试 category，最后使用默认图标
     */
    getOperatorIconPath(type, category = null) {
        // 工业风格 SVG 路径定义 (Material Design / Fluent 风格)
        const icons = {
            // 输入
            'ImageAcquisition': 'M9 3L7.17 5H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2h-3.17L15 3H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z', // 相机
            'ReadImage': 'M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm0 12H4V8h16v10zM8 13.01l1.41 1.41L11 12.83l1.59 1.58L14 13l-3-3-3 3z', // 文件夹图片
            
            // 预处理
            'Filtering': 'M10 18h4v-2h-4v2zM3 6v2h18V6H3zm3 7h12v-2H6v2z', // 过滤/筛选
            'Thresholding': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z', // 对比度/圆形
            'Morphology': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 11h-4v4h-2v-4H7v-2h4V7h2v4h4v2z', // 加号/形态
            'ColorConversion': 'M12 3c-4.97 0-9 4.03-9 9s4.03 9 9 9c.83 0 1.5-.67 1.5-1.5 0-.39-.15-.74-.39-1.01-.23-.26-.38-.61-.38-.99 0-.83.67-1.5 1.5-1.5H16c2.76 0 5-2.24 5-5 0-4.42-4.03-8-9-8zm-5.5 9c-.83 0-1.5-.67-1.5-1.5S5.67 9 6.5 9 8 9.67 8 10.5 7.33 12 6.5 12zm3-4C8.67 8 8 7.33 8 6.5S8.67 5 9.5 5s1.5.67 1.5 1.5S10.33 8 9.5 8zm5 0c-.83 0-1.5-.67-1.5-1.5S13.67 5 14.5 5s1.5.67 1.5 1.5S15.33 8 14.5 8zm3 4c-.83 0-1.5-.67-1.5-1.5S16.67 9 17.5 9s1.5.67 1.5 1.5-.67 1.5-1.5 1.5z', // 调色板
            'AdaptiveThreshold': 'M3 5H1v16c0 1.1.9 2 2 2h16v-2H3V5zm18-4H7c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V3c0-1.1-.9-2-2-2zm0 16H7V3h14v14z', // 层叠
            'HistogramEqualization': 'M5 9.2h3V19H5zM10.6 5h2.8v14h-2.8zm5.6 8H19v6h-2.8z', // 直方图
            'Preprocessing': 'M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L5.03 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12-.22.37-.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z', // 齿轮
            'Undistort': 'M21 5c-1.11-.35-2.33-.5-3.5-.5-1.95 0-4.05 0.4-5.5 1.5-1.45-1.1-3.55-1.5-5.5-1.5S2.45 4.9 1 6v14.65c0 .25.25.5.5.5.1 0 .15-.05.25-.05C3.1 20.45 5.05 20 6.5 20c1.95 0 4.05.4 5.5 1.5 1.35-.85 3.8-1.5 5.5-1.5 1.65 0 3.35.3 4.75 1.05.1.05.15.05.25.05.25 0 .5-.25.5-.5V6c-.6-.45-1.25-.75-2-1zm0 13.5c-1.1-.35-2.3-.5-3.5-.5-1.7 0-4.15.65-5.5 1.5V8c1.35-.85 3.8-1.5 5.5-1.5 1.2 0 2.4.15 3.5.5v11.5z', // 书本/矫正

            // 特征提取
            'EdgeDetection': 'M3 17h18v2H3zm0-7h18v5H3zm0-7h18v5H3z', // 边缘/线条
            'SubpixelEdgeDetection': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z', // 精细目标
            'BlobAnalysis': 'M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z', // 斑点/圆形
            
            // 检测 / 匹配
            'TemplateMatching': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 13h4v-2h-4v2zm0-4h4V9h-4v2z', // 匹配/定位
            'ShapeMatching': 'M12 6c3.31 0 6 2.69 6 6s-2.69 6-6 6-6-2.69-6-6 2.69-6 6-6m0-2c-4.42 0-8 3.58-8 8s3.58 8 8 8 8-3.58 8-8-3.58-8-8-8z', // 形状/圆环
            'Measurement': 'M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm0 10H3V8h2v4h2V8h2v4h2V8h2v4h2V8h2v8z', // 尺子
            'AngleMeasurement': 'M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm0 10H3V8h2v4h2V8h2v4h2V8h2v4h2V8h2v8z', // 量角器 (复用尺子暂代，或找专用图标)
            'GeometricTolerance': 'M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V5h14v14zM7 10h2v7H7zm4-3h2v10h-2zm4 6h2v4h-2z', // 公差/直方图
            'GeometricFitting': 'M12 6c3.31 0 6 2.69 6 6s-2.69 6-6 6-6-2.69-6-6 2.69-6 6-6m0-2c-4.42 0-8 3.58-8 8s3.58 8 8 8 8-3.58 8-8-3.58-8-8-8z', // 拟合
            'ColorDetection': 'M12 3c-4.97 0-9 4.03-9 9s4.03 9 9 9c.83 0 1.5-.67 1.5-1.5 0-.39-.15-.74-.39-1.01-.23-.26-.38-.61-.38-.99 0-.83.67-1.5 1.5-1.5H16c2.76 0 5-2.24 5-5 0-4.42-4.03-8-9-8zm-5.5 9c-.83 0-1.5-.67-1.5-1.5S5.67 9 6.5 9 8 9.67 8 10.5 7.33 12 6.5 12zm3-4C8.67 8 8 7.33 8 6.5S8.67 5 9.5 5s1.5.67 1.5 1.5S10.33 8 9.5 8zm5 0c-.83 0-1.5-.67-1.5-1.5S13.67 5 14.5 5s1.5.67 1.5 1.5S15.33 8 14.5 8zm3 4c-.83 0-1.5-.67-1.5-1.5S16.67 9 17.5 9s1.5.67 1.5 1.5-.67 1.5-1.5 1.5z', // 颜色/调色板
            'CodeReading': 'M3 5h4v4H3V5zm0 10h4v4H3v-4zm6 0h4v4H9v-4zm6 0h4v4h-4v-4zm0-10h4v4h-4V5zm-6 4h4v6H9V9zm6 0h4v6h-4V9zM3 3h18v18H3V3z', // 二维码 (新增猜测)
            'OCR': 'M4 6h16v12H4z m2 2v8h12V8H6z m2 2h2v4H8V10z m4 0h2v4h-2V10z', // 文本识别 (新增猜测)
            
            // AI
            'DeepLearning': 'M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-6 10H6v-2h8v2zm4-4H6v-2h12v2z', // 神经网络
            
            // ROI / 标定
            'RoiManager': 'M3 5v4h2V5h4V3H5c-1.1 0-2 .9-2 2zm2 10H3v4c0 1.1.9 2 2 2h4v-2H5v-4zm14 4h-4v2h4c1.1 0 2-.9 2-2v-4h-2v4zm0-16h-4v2h4v4h2V5c0-1.1-.9-2-2-2z', // 聚焦框
            'CameraCalibration': 'M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z', // 代码/校准
            'CoordinateTransform': 'M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm8.9 11c-.5-3.4-3.5-6-7-6.9v-2.2c1.4.3 2.8-.7 2.8-2.2 0-1.1-.9-2-2-2s-2 .9-2 2c0 1.4 1.4 2.5 2.8 2.2V12c-3.6.9-6.6 4-7 7H2v2h20v-2h-.1z', // 坐标系

            // 通信
            'SerialCommunication': 'M20 18c1.1 0 1.99-.9 1.99-2L22 6c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2H0v2h24v-2h-4zM4 6h16v10H4V6z', // 显示器
            'ModbusCommunication': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 14H9V8h2v8zm4 0h-2V8h2v8z', // 暂停/传输
            'TcpCommunication': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z', // 地球/网络

            // 输出
            'ResultOutput': 'M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z', // 下载
            'DatabaseWrite': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm.31-8.86c-1.77-.45-2.34-.94-2.34-1.67 0-.84.79-1.43 2.1-1.43 1.38 0 1.9.66 1.94 1.64h1.71c-.05-1.34-.87-2.57-2.49-2.97V5H10.9v1.69c-1.51.32-2.72 1.3-2.72 2.81 0 1.79 1.49 2.69 3.66 3.21 1.95.46 2.34 1.15 2.34 1.87 0 .53-.39 1.39-2.1 1.39-1.6 0-2.23-.72-2.32-1.64H8.04c.1 1.7 1.36 2.66 2.86 2.97V19h2.34v-1.67c1.52-.29 2.72-1.16 2.73-2.77-.01-2.2-1.9-2.96-3.66-3.42z', // 数据库/货币符号 (暂代)
        };

        if (icons[type]) {
            return icons[type];
        }

        // 如果没有匹配到具体算子，尝试按照类别返回通用图标，避免全部显示齿轮
        if (category) {
            const categoryPaths = {
                '输入': 'M9 3L7.17 5H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2h-3.17L15 3H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z', // 相机
                '预处理': 'M20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83zM3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25z', // 笔/编辑
                '特征提取': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-8h-2V7h2v2z', // 信息
                '检测': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z', // 勾选
                '测量': 'M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm0 10H3V8h2v4h2V8h2v4h2V8h2v4h2V8h2v8z', // 尺子
                'AI检测': 'M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-6 10H6v-2h8v2zm4-4H6v-2h12v2z', // AI/文件夹
                '通信': 'M20 18c1.1 0 1.99-.9 1.99-2L22 6c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2H0v2h24v-2h-4zM4 6h16v10H4V6z', // 屏幕
                '输出': 'M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z', // 下载
                '标定': 'M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z', // 标定
            };
            
            if (categoryPaths[category]) {
                return categoryPaths[category];
            }
        }

        // 最后的回退图标 (齿轮)
        return this.getSvgIcon('M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L5.03 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z');
    }

    /**
     * 分类图标映射 (SVG)
     */
    getCategoryIcon(category) {
        const icons = {
            '输入': 'M5 13h14v-2H5v2zm-2 4h14v-2H3v2zM7 7v2h14V7H7z', // 列表
            '预处理': 'M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z', // 笔
            '特征提取': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z', // 信息
            '检测': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z', // 勾选
            'AI检测': 'M9 21c0 .55.45 1 1 1h4c.55 0 1-.45 1-1v-1H9v1zm3-19C8.14 2 5 5.14 5 9c0 2.38 1.19 4.47 3 5.74V17c0 .55.45 1 1 1h6c.55 0 1-.45 1-1v-2.26c1.81-1.27 3-3.36 3-5.74 0-3.86-3.14-7-7-7zm2.85 11.1l-.85.6V16h-4v-2.3l-.85-.6A4.997 4.997 0 0 1 7 9c0-2.76 2.24-5 5-5s5 2.24 5 5c0 1.63-.8 3.16-2.15 4.1z', // 灯泡
            '测量': 'M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm0 10H3V8h2v4h2V8h2v4h2V8h2v4h2V8h2v8z', // 尺子
            '通信': 'M4 6h16v10H4V6zM20 18c1.1 0 1.99-.9 1.99-2L22 6c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2H0v2h24v-2h-4z', // 电脑
            '输出': 'M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z', // 下载
            '标定': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17h-2v-2h2v2zm2.07-7.75l-.9.92C13.45 12.9 13 13.5 13 15h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H8c0-2.21 1.79-4 4-4s4 1.79 4 4c0 .88-.36 1.68-.93 2.25z', // 问号/校准
            '其他': 'M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z', // 文件夹
        };
        const path = icons[category] || 'M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z'; // 文件
        return this.getSvgIcon(path);
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
            icon: null, // 禁止 TreeView 默认渲染 (会转义 SVG)
            customIcon: this.getCategoryIcon(category),
            expanded: true,
            children: operators.map((op, index) => {
                // 预先获取图标路径并注入到 operator 数据中，
                // 这样拖拽到画布时，flowEditorInteraction.js 就能直接使用正确的图标
                const iconPath = this.getOperatorIconPath(op.type, category);
                op.iconPath = iconPath;
                
                return {
                    id: `operator_${op.type}_${index}`,
                    label: op.displayName || op.name,
                    type: 'operator',
                    icon: null, // 禁止 TreeView 默认渲染
                    customIcon: op.icon || this.getSvgIcon(iconPath),
                    data: op
                };
            })
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
                    <span class="detail-icon">${operator.icon || this.getOperatorIcon(operator.type)}</span>
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
