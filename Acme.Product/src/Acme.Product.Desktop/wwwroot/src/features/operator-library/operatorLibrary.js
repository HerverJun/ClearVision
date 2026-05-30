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
import debugLogger from '../../core/logging/debugLogger.js';
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
        this.operatorLoadState = 'idle';
        this.operatorLoadError = '';

        // 事件回调
        this.onOperatorDragStart = null;
        this.onOperatorSelected = null;

        // 展开状态持久化。v2 从默认折叠开始，避免旧版全展开缓存覆盖启动默认状态。
        this.storageKey = 'operator-library-expanded-categories-v2';

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
                    debugLogger.debug('[OperatorLibrary] renderNode 渲染算子:', node.label);
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
                    
                    debugLogger.debug('[OperatorLibrary] 算子元素设置完成, draggable:', element.draggable, 'classList:', element.className);
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
                            debugLogger.debug('[OperatorLibrary] Toggle clicked, node.id:', node.id);
                            
                            // 通过 id 查找正确的节点对象
                            const actualNode = this.treeView.findNode(node.id);
                            
                            if (actualNode) {
                                this.treeView.toggleNode(actualNode);
                                debugLogger.debug('[OperatorLibrary] After toggle, expandedNodes:', Array.from(this.treeView.expandedNodes));
                            }
                            return false;
                        };
                        
                        if (toggle) {
                            toggle.onclick = toggleHandler;
                            debugLogger.debug('[OperatorLibrary] Toggle onclick bound for:', node.id);
                        }
                        
                        // 点击分类内容也可以展开/收起
                        if (wrapper) {
                            wrapper.style.cursor = 'pointer';
                            wrapper.onclick = toggleHandler;
                            debugLogger.debug('[OperatorLibrary] Wrapper onclick bound for:', node.id);
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
            debugLogger.debug('[OperatorLibrary] dragstart 事件触发', e.target);
            
            const operatorEl = e.target.closest('.operator-draggable');
            if (!operatorEl) {
                debugLogger.debug('[OperatorLibrary] 未找到 .operator-draggable 元素');
                return;
            }
            debugLogger.debug('[OperatorLibrary] 找到算子元素:', operatorEl);
            
            // 从父级 li 元素获取节点 ID
            const li = operatorEl.closest('[data-id]');
            if (!li) {
                debugLogger.debug('[OperatorLibrary] 未找到父级 li 元素');
                return;
            }
            debugLogger.debug('[OperatorLibrary] 找到 li 元素, data-id:', li.dataset.id);
            
            const nodeId = li.dataset.id;
            // 从 treeView 中查找对应的节点数据
            const node = this.treeView.findNode(nodeId);
            debugLogger.debug('[OperatorLibrary] 查找节点结果:', node);
            
            if (node && node.data) {
                // 设置数据传递
                e.dataTransfer.setData('application/json', JSON.stringify(node.data));
                e.dataTransfer.effectAllowed = 'copy';
                
                // 【修复】备份数据到全局变量，防止 WebView2 环境下 dataTransfer 数据丢失
                window.__draggingOperatorData = node.data;
                debugLogger.debug('[OperatorLibrary] 开始拖拽算子:', node.data.type);

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
            this.operatorLoadState = 'ready';
            this.operatorLoadError = '';
            this.renderOperatorTree();
            showToast(`已加载 ${operators.length} 个算子`, 'success');
        } catch (error) {
            debugLogger.warn('[OperatorLibraryPanel] 加载算子失败:', error);
            this.operators = [];
            this.filteredOperators = [];
            this.operatorLoadState = 'unavailable';
            this.operatorLoadError = error?.message || '算子库服务不可用';
            this.renderOperatorTree();
            showToast('算子库服务不可用，已停止显示默认演示算子', 'warning');
        }
    }

    async loadOperatorsFromMetadata() {
        try {
            const operators = await httpClient.get('/operators/library');
            if (Array.isArray(operators) && operators.length > 0) {
                const normalizedOperators = operators
                    .map(operator => this.normalizeOperatorMetadata(operator, operator.type || operator.Type))
                    .filter(Boolean);
                normalizedOperators.forEach(operator => this.metadataByType.set(operator.type, operator));
                if (normalizedOperators.length > 0) {
                    return normalizedOperators;
                }
            }
        } catch (error) {
            debugLogger.warn('[OperatorLibraryPanel] 获取算子库接口失败，回退到类型元数据接口:', error);
        }

        const types = await httpClient.get('/operators/types');
        if (!Array.isArray(types) || types.length === 0) {
            return [];
        }

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
                debugLogger.warn('[OperatorLibraryPanel] 加载算子元数据失败:', typeIdentifier, error);
                return null;
            }
        }));

        return operators.filter(Boolean);
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
        const tags = metadata.tags || metadata.Tags || [];
        const keywords = metadata.keywords || metadata.Keywords || [];
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
            tags: Array.isArray(tags) ? tags : [],
            keywords: Array.isArray(keywords) ? keywords : [],
            inputType: metadata.inputType || metadata.InputType || (inputPorts[0]?.dataType || inputPorts[0]?.DataType || '图像'),
            outputType: metadata.outputType || metadata.OutputType || (outputPorts[0]?.dataType || outputPorts[0]?.DataType || '图像/数据')
        };
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
        if (this.filteredOperators.length === 0) {
            this.treeView.setData([]);
            const treeContainer = this.container.querySelector('#library-tree');
            if (treeContainer) {
                const message = this.operatorLoadState === 'unavailable'
                    ? `算子库不可用。请检查服务连接后点击刷新；当前未展示可拖拽的默认演示算子。${this.operatorLoadError ? `\n原因：${this.operatorLoadError}` : ''}`
                    : '暂无可用算子';
                treeContainer.innerHTML = `
                    <div class="params-empty" style="white-space:pre-line; padding:12px; line-height:1.6;">
                        ${this.escapeHtml(message)}
                    </div>
                `;
            }
            return;
        }

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
                expanded: false,
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
        
        // 加载保存的展开状态；没有保存值时保持启动默认折叠。
        this.loadExpandedState(treeData);
        
        // 【调试】打印展开状态
        debugLogger.debug('[OperatorLibrary] After setData, expandedNodes:', Array.from(this.treeView.expandedNodes));
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
     * 保存展开状态到 localStorage
     */
    saveExpandedState() {
        try {
            const categoryIds = new Set((this.treeView.root?.children || []).map(node => node.id));
            const expandedIds = Array.from(this.treeView.expandedNodes)
                .filter(id => categoryIds.has(id));
            localStorage.setItem(this.storageKey, JSON.stringify(expandedIds));
        } catch (e) {
            debugLogger.warn('[OperatorLibrary] Failed to save expanded state:', e);
        }
    }

    /**
     * 从 localStorage 加载展开状态
     */
    loadExpandedState(treeData = []) {
        try {
            const saved = localStorage.getItem(this.storageKey);
            debugLogger.debug('[OperatorLibrary] Loading expanded state from localStorage:', saved);
            this.treeView.expandedNodes.clear();

            if (!saved) {
                this.treeView.render();
                return;
            }

            const expandedIds = JSON.parse(saved);
            debugLogger.debug('[OperatorLibrary] Parsed expandedIds:', expandedIds);
            if (!Array.isArray(expandedIds)) {
                localStorage.removeItem(this.storageKey);
                this.treeView.render();
                return;
            }

            const categoryIds = new Set(treeData.map(node => node.id));
            expandedIds
                .filter(id => categoryIds.has(id))
                .forEach(id => this.treeView.expandedNodes.add(id));

            debugLogger.debug('[OperatorLibrary] After loading, expandedNodes:', Array.from(this.treeView.expandedNodes));
            // 加载状态后重新渲染
            this.treeView.render();
        } catch (e) {
            debugLogger.warn('[OperatorLibrary] Failed to load expanded state:', e);
            this.treeView.expandedNodes.clear();
            this.treeView.render();
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
                this.buildOperatorSearchText(op).includes(lowerKeyword)
            );
        }
        
        this.renderOperatorTree();
        
        // 显示搜索结果
        if (keyword.trim()) {
            showToast(`找到 ${this.filteredOperators.length} 个算子`, 'info');
        }
    }

    buildOperatorSearchText(operator) {
        const collectPortText = port => [
            port?.name,
            port?.Name,
            port?.displayName,
            port?.DisplayName,
            port?.dataType,
            port?.DataType,
            port?.type,
            port?.Type,
            port?.description,
            port?.Description
        ].filter(Boolean).join(' ');
        const collectParameterText = param => [
            param?.name,
            param?.Name,
            param?.displayName,
            param?.DisplayName,
            param?.description,
            param?.Description,
            param?.dataType,
            param?.DataType,
            param?.type,
            param?.Type,
            ...((Array.isArray(param?.options || param?.Options) ? (param.options || param.Options) : [])
                .map(option => typeof option === 'string'
                    ? option
                    : `${option?.label || option?.Label || ''} ${option?.value ?? option?.Value ?? ''}`))
        ].filter(Boolean).join(' ');

        return [
            operator?.displayName,
            operator?.name,
            operator?.type,
            operator?.description,
            operator?.category,
            ...(operator?.tags || []),
            ...(operator?.keywords || []),
            ...(operator?.inputPorts || []).map(collectPortText),
            ...(operator?.outputPorts || []).map(collectPortText),
            ...(operator?.parameters || []).map(collectParameterText)
        ].filter(Boolean).join(' ').toLowerCase();
    }

    /**
     * 绑定操作按钮事件
     */
    bindActionEvents() {
        // 展开全部
        this.container.querySelector('#btn-expand-all').addEventListener('click', () => {
            this.treeView.expandAll();
            this.saveExpandedState();
        });
        
        // 折叠全部
        this.container.querySelector('#btn-collapse-all').addEventListener('click', () => {
            this.treeView.collapseAll();
            this.saveExpandedState();
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
        if (!preview) {
            return;
        }
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
        const usage = this.escapeHtml(resolvedOperator.usage || resolvedOperator.Usage || resolvedOperator.purpose || resolvedOperator.Purpose || resolvedOperator.description || '用于流程中的图像处理、检测或数据转换步骤。');
        const scenario = this.escapeHtml(resolvedOperator.scenario || resolvedOperator.Scenario || resolvedOperator.typicalScenario || resolvedOperator.TypicalScenario || '根据输入/输出端口接入合适的上游图像或结构化数据。');
        const notes = this.escapeHtml(resolvedOperator.notes || resolvedOperator.Notes || resolvedOperator.attention || resolvedOperator.Attention || '请确认必填参数、端口类型和上游图像来源后再运行。');
        const featureBadge = this.escapeHtml(getFeatureBadge('operator.autotuneStrategies'));
        const featureDescription = this.escapeHtml(getFeatureDescription('operator.autotuneStrategies'));
        const renderPortLabel = (port, fallbackName = '未命名', direction = 'output') => {
            const name = port.displayName || port.DisplayName || port.name || port.Name || fallbackName;
            const dataType = port.dataType || port.DataType || 'Any';
            const required = direction === 'input' && Boolean(port.isRequired ?? port.IsRequired);
            return `${this.escapeHtml(name)}${required ? ' *' : ''} (${this.escapeHtml(dataType)})`;
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

                <div class="detail-section">
                    <h5>用途</h5>
                    <p>${usage}</p>
                </div>
                <div class="detail-section">
                    <h5>典型场景</h5>
                    <p>${scenario}</p>
                </div>
                <div class="detail-section">
                    <h5>注意事项</h5>
                    <p>${notes}</p>
                </div>
                <div class="detail-section">
                    <h5>必填项</h5>
                    <div class="preview-params">${this.renderRequiredList(resolvedOperator.parameters)}</div>
                </div>
                
                <div class="detail-params">
                    <h5>关键参数</h5>
                    ${this.renderParameterList(resolvedOperator.parameters)}
                </div>
                
                <div class="detail-ports">
                    <h5>端口定义</h5>
                    <div class="ports-list">
                        ${inputPorts.length > 0
                            ? inputPorts.map(port => `
                                <div class="port-item input">
                                    <span class="port-dot input"></span>
                                    <span>输入: ${renderPortLabel(port, '未命名', 'input')}</span>
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
                                    <span>输出: ${renderPortLabel(port, '未命名', 'output')}</span>
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
                debugLogger.warn('[OperatorLibraryPanel] 获取自动调参策略失败:', error);
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
                ${parameters.map(param => {
                    const name = param.displayName || param.DisplayName || param.name || param.Name || '参数';
                    const internalName = param.name || param.Name || '';
                    const type = param.dataType || param.DataType || param.type || param.Type || 'Any';
                    const defaultValue = param.defaultValue ?? param.DefaultValue ?? param.value ?? param.Value ?? '';
                    const min = param.min ?? param.Min ?? param.minValue ?? param.MinValue;
                    const max = param.max ?? param.Max ?? param.maxValue ?? param.MaxValue;
                    const options = param.options || param.Options || [];
                    const required = Boolean(param.isRequired ?? param.IsRequired);
                    const rangeText = min !== undefined || max !== undefined
                        ? `范围: ${min ?? '-∞'} ~ ${max ?? '+∞'}`
                        : '';
                    const optionsText = Array.isArray(options) && options.length > 0
                        ? `可选: ${options.map(option => typeof option === 'string'
                            ? option
                            : (option.label || option.Label || option.value || option.Value || '')).filter(Boolean).join(' / ')}`
                        : '';
                    return `
                        <li class="param-item">
                            <span class="param-name">${this.escapeHtml(name)}${required ? ' *' : ''}</span>
                            <span class="param-type">${this.escapeHtml(type)}</span>
                            <span class="param-default">${defaultValue === '' ? '无默认值' : `默认: ${this.escapeHtml(defaultValue)}`}</span>
                            ${internalName && internalName !== name ? `<span class="param-note">${this.escapeHtml(internalName)}</span>` : ''}
                            ${rangeText ? `<span class="param-note">${this.escapeHtml(rangeText)}</span>` : ''}
                            ${optionsText ? `<span class="param-note">${this.escapeHtml(optionsText)}</span>` : ''}
                        </li>
                    `;
                }).join('')}
            </ul>
        `;
    }

    renderRequiredList(parameters) {
        const required = (parameters || [])
            .filter(param => Boolean(param.isRequired ?? param.IsRequired))
            .map(param => param.displayName || param.DisplayName || param.name || param.Name)
            .filter(Boolean);

        return required.length > 0
            ? required.map(item => `<span class="param-tag required-tag">${this.escapeHtml(item)}</span>`).join('')
            : '<span class="params-empty">无必填参数</span>';
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

