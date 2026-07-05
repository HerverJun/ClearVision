/**
 * 流程编辑器交互系统
 * 扩展FlowCanvas的拖拽、连线、选择、快捷键功能
 */

import { showToast } from '../../shared/components/uiComponents.js';
import { buildOperatorNodeConfig } from '../../shared/operatorVisuals.js';
import debugLogger from '../../core/logging/debugLogger.js';
import TemplateSelector from './templateSelector.js';

export class FlowEditorInteraction {
    constructor(flowCanvas, options = {}) {
        this.canvas = flowCanvas;
        this.projectManager = options.projectManager || null;
        this.isConnecting = false;
        this.connectionStart = null;
        this.connectionEnd = null;
        this.connectionAnchor = null;
        this.connectionDidDrag = false;
        this.isSelecting = false;
        this.selectionBox = null;
        this.selectionStart = null;
        this.isPanning = false;
        this.panStart = null;
        this.panStartOffset = null;
        this.isDraggingNodes = false;
        this.dragStartPos = null;
        this.dragInitialPositions = new Map();
        this.hasNodeDragMoved = false;
        this.multiSelectedNodes = new Set();
        this.copiedNodes = [];
        this.history = [];
        this.historyIndex = -1;
        this.maxHistorySize = 50;
        this.templateSelector = null;
        this.cleanup = [];
        this.originalCanvasHandlers = null;
        this.viewStateNotifyRaf = null;

        this.initialize();
    }

    initialize() {
        // 增强原有事件监听
        this.installCanvasDeletionBridge();
        this.installCanvasDuplicationBridge();
        this.installCanvasDisabledToggleBridge();
        this.enhanceEventListeners();
        // 绑定键盘快捷键
        this.bindKeyboardShortcuts();
        // 启用算子库拖拽
        this.enableOperatorLibraryDrag();
        // 初始化流程模板选择器入口
        this.initializeTemplateSelector();
        // 初始化历史记录
        this.saveState();
    }

    destroy() {
        if (this.viewStateNotifyRaf !== null) {
            cancelAnimationFrame(this.viewStateNotifyRaf);
            this.viewStateNotifyRaf = null;
        }

        this.cleanup.splice(0).forEach(dispose => {
            try {
                dispose();
            } catch (error) {
                debugLogger.warn('[FlowEditorInteraction] 清理事件监听失败:', error);
            }
        });

        if (this.originalCanvasHandlers) {
            this.canvas.handleMouseDown = this.originalCanvasHandlers.mouseDown;
            this.canvas.handleMouseMove = this.originalCanvasHandlers.mouseMove;
            this.canvas.handleMouseUp = this.originalCanvasHandlers.mouseUp;
            this.originalCanvasHandlers = null;
        }

        this.templateSelector?.destroy?.();
    }

    scheduleViewStateNotification() {
        if (this.viewStateNotifyRaf !== null) {
            return;
        }

        this.viewStateNotifyRaf = requestAnimationFrame(() => {
            this.viewStateNotifyRaf = null;
            this.canvas.notifyViewStateChanged?.();
        });
    }

    /**
     * 初始化模板选择器
     */
    initializeTemplateSelector() {
        this.templateSelector = new TemplateSelector(this.canvas, {
            onApplied: (payload) => this.handleTemplateApplied(payload)
        });

        const toolbar = document.querySelector('.toolbar-right');
        if (!toolbar) return;

        let templateButton = document.getElementById('btn-template-create');
        if (!templateButton) {
            templateButton = document.createElement('button');
            templateButton.id = 'btn-template-create';
            templateButton.className = 'btn btn-secondary btn-with-icon';
            templateButton.innerHTML = `
                <svg class="btn-icon-svg" viewBox="0 0 24 24" width="16" height="16" fill="currentColor">
                    <path d="M3 5h18v2H3V5zm0 6h18v2H3v-2zm0 6h18v2H3v-2z"/>
                </svg>
                <span>从模板创建</span>
            `;

            toolbar.insertBefore(templateButton, toolbar.firstChild);
        }

        if (templateButton.dataset.boundTemplateSelector === 'true') {
            return;
        }

        templateButton.dataset.boundTemplateSelector = 'true';
        const templateClickHandler = async () => {
            try {
                await this.templateSelector.open();
            } catch (error) {
                console.error('[FlowEditorInteraction] 打开模板选择器失败:', error);
                showToast(`打开模板选择器失败: ${error.message}`, 'error');
            }
        };
        templateButton.addEventListener('click', templateClickHandler);
        this.cleanup.push(() => {
            templateButton.removeEventListener('click', templateClickHandler);
            delete templateButton.dataset.boundTemplateSelector;
        });
    }

    async handleTemplateApplied(payload = null) {
        this.saveState();
        await this.ensureProjectForAppliedTemplate(payload);
    }

    async ensureProjectForAppliedTemplate(payload = null) {
        if (!this.projectManager || this.projectManager.getCurrentProject?.()?.id) {
            return null;
        }

        const flow = payload?.serializedFlow
            || (typeof this.canvas?.serialize === 'function' ? this.canvas.serialize() : null);
        if (!this.hasSerializedFlowContent(flow) || typeof this.projectManager.createProject !== 'function') {
            return null;
        }

        const template = payload?.template || {};
        const projectName = this.buildTemplateProjectName(template);
        const projectDescription = this.buildTemplateProjectDescription(template);

        try {
            const project = await this.projectManager.createProject(projectName, projectDescription);
            if (typeof this.projectManager.updateFlow === 'function') {
                this.projectManager.updateFlow(flow);
            } else {
                const currentProject = this.projectManager.getCurrentProject?.() || project;
                if (currentProject) {
                    currentProject.flow = flow;
                }
            }

            if (typeof this.projectManager.saveProject === 'function') {
                await this.projectManager.saveProject(this.projectManager.getCurrentProject?.() || project);
            }

            showToast(`已从模板创建工程：${project?.name || projectName}`, 'success');
            return project;
        } catch (error) {
            console.error('[FlowEditorInteraction] 从模板创建工程失败:', error);
            showToast(`模板已应用，但创建工程失败: ${error?.message || error}`, 'warning');
            return null;
        }
    }

    hasSerializedFlowContent(flow) {
        const operators = flow?.operators || flow?.Operators || flow?.nodes || flow?.Nodes || [];
        if (Array.isArray(operators)) {
            return operators.length > 0;
        }

        return operators && typeof operators === 'object' && Object.keys(operators).length > 0;
    }

    buildTemplateProjectName(template = {}) {
        const templateName = String(template?.name || template?.Name || '').trim();
        const baseName = templateName || `模板工程_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}`;
        const projectName = templateName.endsWith('工程') ? templateName : `${baseName} 工程`;
        return projectName.length > 80 ? projectName.slice(0, 80) : projectName;
    }

    buildTemplateProjectDescription(template = {}) {
        const templateName = String(template?.name || template?.Name || '未命名模板').trim();
        const description = String(template?.description || template?.Description || '').trim();
        return description
            ? `从模板“${templateName}”创建。${description}`
            : `从模板“${templateName}”创建。`;
    }

    syncProjectFlow() {
        if (!this.projectManager?.updateFlow || typeof this.canvas.serialize !== 'function') {
            return;
        }

        this.projectManager.updateFlow(this.canvas.serialize());
    }

    installCanvasDeletionBridge() {
        if (!this.canvas || !('onSelectionDeleteRequested' in this.canvas)) {
            return;
        }

        const previousHandler = this.canvas.onSelectionDeleteRequested;
        const handler = () => this.deleteSelectedItems();
        this.canvas.onSelectionDeleteRequested = handler;
        this.cleanup.push(() => {
            if (this.canvas.onSelectionDeleteRequested === handler) {
                this.canvas.onSelectionDeleteRequested = previousHandler;
            }
        });
    }

    installCanvasDuplicationBridge() {
        if (!this.canvas || !('onNodeDuplicateRequested' in this.canvas)) {
            return;
        }

        const previousHandler = this.canvas.onNodeDuplicateRequested;
        const handler = ({ nodeId } = {}) => this.duplicateNodeFromCanvasRequest(nodeId);
        this.canvas.onNodeDuplicateRequested = handler;
        this.cleanup.push(() => {
            if (this.canvas.onNodeDuplicateRequested === handler) {
                this.canvas.onNodeDuplicateRequested = previousHandler;
            }
        });
    }

    installCanvasDisabledToggleBridge() {
        if (!this.canvas || !('onNodeDisabledToggleRequested' in this.canvas)) {
            return;
        }

        const previousHandler = this.canvas.onNodeDisabledToggleRequested;
        const handler = ({ nodeId } = {}) => this.toggleNodeDisabledFromCanvasRequest(nodeId);
        this.canvas.onNodeDisabledToggleRequested = handler;
        this.cleanup.push(() => {
            if (this.canvas.onNodeDisabledToggleRequested === handler) {
                this.canvas.onNodeDisabledToggleRequested = previousHandler;
            }
        });
    }

    /**
     * 增强事件监听
     */
    enhanceEventListeners() {
        const originalMouseDown = this.canvas.handleMouseDown.bind(this.canvas);
        const originalMouseMove = this.canvas.handleMouseMove.bind(this.canvas);
        const originalMouseUp = this.canvas.handleMouseUp.bind(this.canvas);
        this.originalCanvasHandlers = {
            mouseDown: originalMouseDown,
            mouseMove: originalMouseMove,
            mouseUp: originalMouseUp
        };

        // 重写鼠标按下事件
        this.canvas.handleMouseDown = (e) => {
            const { x, y } = this.getCanvasWorldPoint(e);
            const isLeftClick = e.button === 0;
            const isMiddleClick = e.button === 1;
            const isMultiSelectModifier = e.shiftKey || e.ctrlKey || e.metaKey;

            // 右键交由 contextmenu 事件处理，避免触发基础层的端口/拖拽副作用
            if (e.button === 2) {
                return;
            }

            // 仅左键触发连线
            const clickedPort = isLeftClick ? this.getPortAt(x, y) : null;
            if (clickedPort) {
                if (!this.isConnecting && this.tryDisconnectPortConnections(clickedPort)) {
                    return;
                }

                if (this.isConnecting) {
                    if (clickedPort.type !== this.connectionStart?.type) {
                        this.endConnection(null, clickedPort);
                    } else {
                        this.startConnection(clickedPort, e);
                    }
                } else {
                    this.startConnection(clickedPort, e);
                }
                return;
            }

            if (this.isConnecting && isLeftClick) {
                this.cancelConnection();
                return;
            }

            const clickedNode = this.getNodeAt(x, y);

            // 空白区域中键拖拽平移（备用）
            if (isMiddleClick && !clickedNode) {
                this.startPan(e);
                return;
            }

            // 仅处理左键主交互
            if (!isLeftClick) {
                return;
            }

            if (isMultiSelectModifier) {
                if (clickedNode) {
                    this.toggleNodeSelection(clickedNode.id);
                } else {
                    this.startSelection(e);
                }
                return;
            }

            if (clickedNode) {
                if (!this.multiSelectedNodes.has(clickedNode.id)) {
                    this.clearSelection({ notify: false });
                    this.selectNode(clickedNode.id);
                } else {
                    this.canvas.selectedNode = clickedNode.id;
                    this.updateSelection();
                    this.notifyNodeSelectionChanged('mouse-select-node');
                }

                this.startNodeDrag(e, clickedNode.id);
                return;
            }

            this.clearSelection();
            this.startPan(e);
        };

        // 重写鼠标移动事件
        this.canvas.handleMouseMove = (e) => {
            if (this.isConnecting) {
                this.updateConnectionPreview(e);
            } else if (this.isDraggingNodes) {
                this.updateNodeDrag(e);
            } else if (this.isPanning) {
                this.updatePan(e);
            } else if (this.isSelecting) {
                this.updateSelectionBox(e);
            } else {
                originalMouseMove(e);
            }
        };

        // 重写鼠标释放事件
        this.canvas.handleMouseUp = (e) => {
            if (this.isConnecting) {
                if (this.connectionDidDrag) {
                    this.endConnection(e);
                }
            } else if (this.isDraggingNodes) {
                this.endNodeDrag(e);
            } else if (this.isPanning) {
                this.endPan(e);
            } else if (this.isSelecting) {
                this.endSelection();
            } else {
                // 拖拽结束，保存状态
                if (this.canvas.draggedNode) {
                    this.saveState();
                }
                originalMouseUp(e);
            }
        };

        // 重新绑定事件监听，确保包装后的处理器生效
        if (this.canvas.canvas && this.canvas._mouseDownHandler && this.canvas._mouseMoveHandler && this.canvas._mouseUpHandler) {
            this.canvas.canvas.removeEventListener('mousedown', this.canvas._mouseDownHandler);
            this.canvas.canvas.removeEventListener('mousemove', this.canvas._mouseMoveHandler);
            this.canvas.canvas.removeEventListener('mouseup', this.canvas._mouseUpHandler);

            this.canvas._mouseDownHandler = this.canvas.handleMouseDown;
            this.canvas._mouseMoveHandler = this.canvas.handleMouseMove;
            this.canvas._mouseUpHandler = this.canvas.handleMouseUp;

            this.canvas.canvas.addEventListener('mousedown', this.canvas._mouseDownHandler);
            this.canvas.canvas.addEventListener('mousemove', this.canvas._mouseMoveHandler);
            this.canvas.canvas.addEventListener('mouseup', this.canvas._mouseUpHandler);
            this.cleanup.push(() => {
                this.canvas.canvas.removeEventListener('mousedown', this.canvas._mouseDownHandler);
                this.canvas.canvas.removeEventListener('mousemove', this.canvas._mouseMoveHandler);
                this.canvas.canvas.removeEventListener('mouseup', this.canvas._mouseUpHandler);

                this.canvas.handleMouseDown = originalMouseDown;
                this.canvas.handleMouseMove = originalMouseMove;
                this.canvas.handleMouseUp = originalMouseUp;
                this.canvas._mouseDownHandler = originalMouseDown;
                this.canvas._mouseMoveHandler = originalMouseMove;
                this.canvas._mouseUpHandler = originalMouseUp;

                this.canvas.canvas.addEventListener('mousedown', this.canvas._mouseDownHandler);
                this.canvas.canvas.addEventListener('mousemove', this.canvas._mouseMoveHandler);
                this.canvas.canvas.addEventListener('mouseup', this.canvas._mouseUpHandler);
            });
        }
    }

    tryDisconnectPortConnections(port) {
        if (!port || this.isConnecting) {
            return false;
        }

        if (port.isOutput) {
            const existingConnections = typeof this.canvas.getConnectionsAtPort === 'function'
                ? this.canvas.getConnectionsAtPort(port.nodeId, port.portIndex, true)
                : [];

            if (!Array.isArray(existingConnections) || existingConnections.length === 0) {
                return false;
            }

            existingConnections.forEach(connection => {
                this.canvas.removeConnection(connection.id);
            });

            this.saveState();
            const message = existingConnections.length === 1
                ? '连接已断开'
                : `已断开 ${existingConnections.length} 个连接`;
            showToast(message, 'info');
            return true;
        }

        const existingConnection = typeof this.canvas.getConnectionAtPort === 'function'
            ? this.canvas.getConnectionAtPort(port.nodeId, port.portIndex, false)
            : null;

        if (!existingConnection) {
            return false;
        }

        this.canvas.removeConnection(existingConnection.id);
        this.saveState();
        showToast('连接已断开', 'info');
        return true;
    }

    /**
     * 获取画布世界坐标
     */
    getCanvasWorldPoint(e) {
        const rect = this.canvas.canvas.getBoundingClientRect();
        return {
            x: (e.clientX - rect.left) / this.canvas.scale + this.canvas.offset.x,
            y: (e.clientY - rect.top) / this.canvas.scale + this.canvas.offset.y
        };
    }

    /**
     * 开始画布平移
     */
    startPan(e) {
        const rect = this.canvas.canvas.getBoundingClientRect();
        this.isPanning = true;
        this.panStart = {
            x: e.clientX - rect.left,
            y: e.clientY - rect.top
        };
        this.panStartOffset = {
            x: this.canvas.offset.x,
            y: this.canvas.offset.y
        };
        this.canvas.canvas.style.cursor = 'move';
        e.preventDefault();
    }

    /**
     * 更新画布平移
     */
    updatePan(e) {
        if (!this.isPanning || !this.panStart || !this.panStartOffset) return;

        const rect = this.canvas.canvas.getBoundingClientRect();
        const currentX = e.clientX - rect.left;
        const currentY = e.clientY - rect.top;

        const deltaX = (currentX - this.panStart.x) / this.canvas.scale;
        const deltaY = (currentY - this.panStart.y) / this.canvas.scale;

        this.canvas.offset.x = this.panStartOffset.x - deltaX;
        this.canvas.offset.y = this.panStartOffset.y - deltaY;
        this.canvas.canvas.style.cursor = 'move';
        this.canvas.invalidate?.();
        this.scheduleViewStateNotification();
    }

    /**
     * 结束画布平移
     */
    endPan(e) {
        if (!this.isPanning) return;
        this.isPanning = false;
        this.panStart = null;
        this.panStartOffset = null;
        this.syncCursorToPointer(e);
        this.canvas.invalidate?.();
        this.canvas.notifyViewStateChanged?.();
    }

    /**
     * 开始节点拖拽（支持多选批量拖拽）
     */
    startNodeDrag(e, clickedNodeId) {
        const clickedNode = this.canvas.nodes.get(clickedNodeId);
        if (!clickedNode) return;

        if (!this.multiSelectedNodes.has(clickedNodeId)) {
            this.multiSelectedNodes.add(clickedNodeId);
        }

        this.dragInitialPositions.clear();
        for (const nodeId of this.multiSelectedNodes) {
            const node = this.canvas.nodes.get(nodeId);
            if (node) {
                this.dragInitialPositions.set(nodeId, { x: node.x, y: node.y });
            }
        }

        if (this.dragInitialPositions.size === 0) {
            this.dragInitialPositions.set(clickedNodeId, { x: clickedNode.x, y: clickedNode.y });
        }

        const { x, y } = this.getCanvasWorldPoint(e);
        this.isDraggingNodes = true;
        this.dragStartPos = { x, y };
        this.hasNodeDragMoved = false;
        this.canvas.draggedNode = clickedNodeId;
        this.canvas.canvas.style.cursor = 'grabbing';
        e.preventDefault();
    }

    /**
     * 更新节点拖拽（批量）
     */
    updateNodeDrag(e) {
        if (!this.isDraggingNodes || !this.dragStartPos) return;

        const { x, y } = this.getCanvasWorldPoint(e);
        const deltaX = x - this.dragStartPos.x;
        const deltaY = y - this.dragStartPos.y;

        this.hasNodeDragMoved = this.hasNodeDragMoved || Math.abs(deltaX) > 0 || Math.abs(deltaY) > 0;

        for (const [nodeId, initialPos] of this.dragInitialPositions) {
            const node = this.canvas.nodes.get(nodeId);
            if (!node) continue;
            node.x = Math.round((initialPos.x + deltaX) / 10) * 10;
            node.y = Math.round((initialPos.y + deltaY) / 10) * 10;
        }

        this.canvas.canvas.style.cursor = 'grabbing';
        this.canvas._markNodesBoundsDirty?.();
        this.canvas.invalidate?.();
        this.scheduleViewStateNotification();
    }

    /**
     * 结束节点拖拽
     */
    endNodeDrag(e) {
        if (!this.isDraggingNodes) return;

        const shouldSave = this.hasNodeDragMoved;

        this.isDraggingNodes = false;
        this.dragStartPos = null;
        this.dragInitialPositions.clear();
        this.hasNodeDragMoved = false;
        this.canvas.draggedNode = null;
        this.syncCursorToPointer(e);
        this.canvas._markNodesBoundsDirty?.();
        this.canvas.invalidate?.();
        this.canvas.notifyViewStateChanged?.();

        if (shouldSave) {
            this.saveState();
        }
    }

    /**
     * 按当前位置同步空闲光标
     */
    syncCursorToPointer(e) {
        if (!e) {
            this.canvas.canvas.style.cursor = 'default';
            return;
        }

        const { x, y } = this.getCanvasWorldPoint(e);
        const port = this.getPortAt(x, y);
        if (port) {
            this.canvas.canvas.style.cursor = 'pointer';
            return;
        }

        const node = this.getNodeAt(x, y);
        this.canvas.canvas.style.cursor = node ? 'grab' : 'default';
    }

    /**
     * 绑定键盘快捷键
     */
    bindKeyboardShortcuts() {
        const keydownHandler = (e) => {
            // 复制
            if ((e.ctrlKey || e.metaKey) && e.key === 'c') {
                e.preventDefault();
                this.copySelectedNodes();
            }

            // 粘贴
            if ((e.ctrlKey || e.metaKey) && e.key === 'v') {
                e.preventDefault();
                this.pasteNodes();
            }

            // 删除
            if (e.key === 'Delete' || e.key === 'Backspace') {
                // 如果焦点在输入框、文本区域或可编辑元素中，不拦截
                if (e.target.tagName === 'INPUT' || 
                    e.target.tagName === 'TEXTAREA' || 
                    e.target.tagName === 'SELECT' || 
                    e.target.isContentEditable) {
                    return;
                }
                e.preventDefault();
                e.stopPropagation?.();
                this.deleteSelectedItems();
            }

            // 撤销
            if ((e.ctrlKey || e.metaKey) && e.key === 'z') {
                e.preventDefault();
                this.undo();
            }

            // 重做
            if ((e.ctrlKey || e.metaKey) && e.key === 'y') {
                e.preventDefault();
                this.redo();
            }

            // 全选
            if ((e.ctrlKey || e.metaKey) && e.key === 'a') {
                e.preventDefault();
                this.selectAll();
            }

            // 取消选择
            if (e.key === 'Escape') {
                this.clearSelection();
                this.cancelConnection();
            }
        };
        document.addEventListener('keydown', keydownHandler);
        this.cleanup.push(() => document.removeEventListener('keydown', keydownHandler));
    }

    /**
     * 启用算子库拖拽
     */
    enableOperatorLibraryDrag() {
        const library = document.getElementById('operator-library');
        if (!library) return;

        const libraryDragStartHandler = (e) => {
            if (e.target.classList.contains('operator-item')) {
                const operatorType = e.target.dataset.type;
                const operatorName = e.target.dataset.name || operatorType;
                e.dataTransfer.setData('application/json', JSON.stringify({
                    type: operatorType,
                    name: operatorName
                }));
            }
        };
        library.addEventListener('dragstart', libraryDragStartHandler);

        const canvasDragOverHandler = (e) => {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
        };
        this.canvas.canvas.addEventListener('dragover', canvasDragOverHandler);

        const canvasDropHandler = (e) => {
            e.preventDefault();
            
            let operator = null;
            
            // 优先从全局变量获取 (针对 WebView2 环境)
            if (window.__draggingOperatorData) {
                operator = window.__draggingOperatorData;
                window.__draggingOperatorData = null; // 使用后清除
            } else {
                const data = e.dataTransfer.getData('application/json');
                if (data) {
                    try {
                        operator = JSON.parse(data);
                    } catch (err) {
                        console.error('拖拽解析失败:', err);
                    }
                }
            }

            if (operator) {
                try {
                    const rect = this.canvas.canvas.getBoundingClientRect();
                    const x = (e.clientX - rect.left) / this.canvas.scale + this.canvas.offset.x;
                    const y = (e.clientY - rect.top) / this.canvas.scale + this.canvas.offset.y;

                    // 修复：优先使用 displayName，兼容 name 或 type
                    const operatorTitle = operator.displayName || operator.name || operator.type;

                    // 使用更新后的 addOperatorNode，传递整个 operator 对象以获取参数和端口配置
                    this.addOperatorNode(operator.type, x, y, operator);
                    this.saveState();
                    showToast(`已添加算子: ${operatorTitle}`, 'success');
                } catch (err) {
                    console.error('添加算子失败:', err);
                }
            }
        };
        this.canvas.canvas.addEventListener('drop', canvasDropHandler);
        this.cleanup.push(() => {
            library.removeEventListener('dragstart', libraryDragStartHandler);
            this.canvas.canvas.removeEventListener('dragover', canvasDragOverHandler);
            this.canvas.canvas.removeEventListener('drop', canvasDropHandler);
        });
    }

    /**
     * 添加算子节点
     */
    addOperatorNode(type, x, y, data = null) {
        const node = this.canvas.addNode(type, x, y, buildOperatorNodeConfig(type, data));
        return node;
    }

    /**
     * 获取端口
     */
    getPortAt(x, y) {
        if (typeof this.canvas.getPortAt === 'function') {
            const hitPort = this.canvas.getPortAt(x, y);
            if (hitPort) {
                const node = this.canvas.nodes.get(hitPort.nodeId);
                if (!node) {
                    return null;
                }

                const isOutput = Boolean(hitPort.isOutput);
                const ports = isOutput ? (node.outputs || []) : (node.inputs || []);
                return {
                    nodeId: hitPort.nodeId,
                    portIndex: hitPort.portIndex,
                    isOutput,
                    type: isOutput ? 'output' : 'input',
                    port: ports[hitPort.portIndex] || { type: 'Any' }
                };
            }
        }

        // fallback for legacy data shape
        for (const [id, node] of this.canvas.nodes) {
            for (let i = 0; i < node.inputs.length; i++) {
                const input = node.inputs[i];
                const px = node.x;
                const py = node.y + node.height / 2;
                const dist = Math.sqrt(Math.pow(x - px, 2) + Math.pow(y - py, 2));
                if (dist < 10) {
                    return { nodeId: id, portIndex: i, type: 'input', port: input };
                }
            }

            for (let i = 0; i < node.outputs.length; i++) {
                const output = node.outputs[i];
                const px = node.x + node.width;
                const py = node.y + node.height / 2;
                const dist = Math.sqrt(Math.pow(x - px, 2) + Math.pow(y - py, 2));
                if (dist < 10) {
                    return { nodeId: id, portIndex: i, type: 'output', port: output };
                }
            }
        }

        return null;
    }

    /**     * 获取节点
     */
    getNodeAt(x, y) {
        for (const [id, node] of this.canvas.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                return node;
            }
        }
        return null;
    }

    /**
     * 开始连线
     */
    startConnection(port, e) {
        this.isConnecting = true;
        this.connectionStart = port;
        this.canvas.canvas.style.cursor = 'crosshair';
        
        const rect = this.canvas.canvas.getBoundingClientRect();
        this.connectionEnd = {
            x: (e.clientX - rect.left) / this.canvas.scale + this.canvas.offset.x,
            y: (e.clientY - rect.top) / this.canvas.scale + this.canvas.offset.y
        };
        this.connectionAnchor = { ...this.connectionEnd };
        this.connectionDidDrag = false;

        this.canvas.isConnecting = true;
        this.canvas.connectingFrom = {
            nodeId: port.nodeId,
            portIndex: port.portIndex,
            isOutput: port.type === 'output'
        };
        this.canvas.mousePosition = { ...this.connectionEnd };
        this.canvas.hoveredPort = null;
        this.canvas.invalidate?.();
    }

    /**     * 更新连线预览
     */
    updateConnectionPreview(e) {
        const rect = this.canvas.canvas.getBoundingClientRect();
        this.connectionEnd = {
            x: (e.clientX - rect.left) / this.canvas.scale + this.canvas.offset.x,
            y: (e.clientY - rect.top) / this.canvas.scale + this.canvas.offset.y
        };

        if (this.connectionAnchor) {
            const dx = this.connectionEnd.x - this.connectionAnchor.x;
            const dy = this.connectionEnd.y - this.connectionAnchor.y;
            const threshold = 2 / Math.max(this.canvas.scale, 0.01);
            if (Math.abs(dx) > threshold || Math.abs(dy) > threshold) {
                this.connectionDidDrag = true;
            }
        }

        this.canvas.canvas.style.cursor = 'crosshair';
        this.canvas.mousePosition = { ...this.connectionEnd };
        this.canvas.invalidate?.();
    }

    /**     * 结束连线
     */
    endConnection(e, overrideEndPort = null) {
        debugLogger.debug('[DEBUG endConnection] === START CONNECTION ===');
        debugLogger.debug('[DEBUG endConnection] connectionStart:', JSON.stringify(this.connectionStart));

        let x = null;
        let y = null;
        let endPort = overrideEndPort;

        if (!endPort) {
            if (!e) {
                this.cancelConnection();
                return;
            }

            const rect = this.canvas.canvas.getBoundingClientRect();
            x = (e.clientX - rect.left) / this.canvas.scale + this.canvas.offset.x;
            y = (e.clientY - rect.top) / this.canvas.scale + this.canvas.offset.y;
            endPort = this.getPortAt(x, y);
        }

        debugLogger.debug(`[DEBUG endConnection] Mouse position: x=${x}, y=${y}`);
        debugLogger.debug('[DEBUG endConnection] getPortAt result:', JSON.stringify(endPort));
        debugLogger.debug('[DEBUG endConnection] connectionEnd will be:', JSON.stringify({ x, y, port: endPort }));

        if (endPort && endPort.type !== this.connectionStart.type) {
            const source = this.connectionStart.type === 'output' ? this.connectionStart : endPort;
            const target = this.connectionStart.type === 'input' ? this.connectionStart : endPort;

            const sourceType = source?.port?.type ?? 'Any';
            const targetType = target?.port?.type ?? 'Any';
            const isTypeCompatible = typeof this.canvas.checkTypeCompatibility === 'function'
                ? this.canvas.checkTypeCompatibility(sourceType, targetType)
                : (
                    String(sourceType).toLowerCase() === 'any' ||
                    String(targetType).toLowerCase() === 'any' ||
                    sourceType === targetType
                );

            if (!isTypeCompatible) {
                showToast(`端口类型不匹配：${sourceType} -> ${targetType}`, 'warning');
                this.cancelConnection();
                return;
            }

            const exists = this.canvas.connections.some(conn =>
                conn.source === source.nodeId &&
                conn.target === target.nodeId &&
                conn.sourcePort === source.portIndex &&
                conn.targetPort === target.portIndex
            );

            debugLogger.debug('[DEBUG endConnection] Source:', JSON.stringify(source));
            debugLogger.debug('[DEBUG endConnection] Target:', JSON.stringify(target));
            debugLogger.debug('[DEBUG endConnection] Connection exists:', exists);
            debugLogger.debug('[DEBUG endConnection] Same node check:', source.nodeId === target.nodeId);

            const validationError = typeof this.canvas.getConnectionValidationError === 'function'
                ? this.canvas.getConnectionValidationError(source.nodeId, source.portIndex, target.nodeId, target.portIndex)
                : (exists || source.nodeId === target.nodeId ? 'invalid' : null);

            if (!validationError) {
                debugLogger.debug(`[DEBUG endConnection] Adding connection: ${source.nodeId}:${source.portIndex} -> ${target.nodeId}:${target.portIndex}`);
                const connection = this.canvas.addConnection(source.nodeId, source.portIndex, target.nodeId, target.portIndex);
                if (connection) {
                    this.saveState();
                    showToast('连接已建立', 'success');
                } else {
                    showToast('连接已存在或无效', 'warning');
                }
            } else {
                debugLogger.debug('[DEBUG endConnection] Connection skipped - exists or invalid');
                const message = validationError === 'input-port-occupied'
                    ? '目标输入端口已被占用'
                    : (validationError === 'cycle'
                        ? '该连接会形成环路'
                        : (validationError === 'self-connection'
                            ? '不能连接到同一节点'
                            : '连接已存在或无效'));
                showToast(message, 'warning');
            }
        } else {
            debugLogger.debug('[DEBUG endConnection] No valid end port found or same type port');
        }

        debugLogger.debug('[DEBUG endConnection] === END CONNECTION ===');
        this.cancelConnection();
    }

    /**     * 取消连线
     */
    cancelConnection() {
        this.isConnecting = false;
        this.connectionStart = null;
        this.connectionEnd = null;
        this.connectionAnchor = null;
        this.connectionDidDrag = false;
        this.canvas.isConnecting = false;
        this.canvas.connectingFrom = null;
        this.canvas.hoveredPort = null;
        this.canvas.canvas.style.cursor = 'default';
        this.canvas.invalidate?.();
    }

    /**     * 开始框选
     */
    startSelection(e) {
        this.isSelecting = true;
        const rect = this.canvas.canvas.getBoundingClientRect();
        this.selectionStart = {
            x: e.clientX - rect.left,
            y: e.clientY - rect.top
        };

        // 创建选择框
        this.selectionBox = document.createElement('div');
        this.selectionBox.className = 'flow-selection-box';
        this.selectionBox.style.position = 'absolute';
        this.selectionBox.style.border = '2px dashed #1890ff';
        this.selectionBox.style.background = 'rgba(24, 144, 255, 0.1)';
        this.selectionBox.style.pointerEvents = 'none';
        this.canvas.canvas.parentElement.appendChild(this.selectionBox);
    }

    /**
     * 更新选择框
     */
    updateSelectionBox(e) {
        if (!this.selectionBox) return;

        const rect = this.canvas.canvas.getBoundingClientRect();
        const currentX = e.clientX - rect.left;
        const currentY = e.clientY - rect.top;

        const left = Math.min(this.selectionStart.x, currentX);
        const top = Math.min(this.selectionStart.y, currentY);
        const width = Math.abs(currentX - this.selectionStart.x);
        const height = Math.abs(currentY - this.selectionStart.y);

        this.selectionBox.style.left = `${left}px`;
        this.selectionBox.style.top = `${top}px`;
        this.selectionBox.style.width = `${width}px`;
        this.selectionBox.style.height = `${height}px`;
    }

    /**
     * 结束框选
     */
    endSelection() {
        if (this.selectionBox) {
            const boxRect = this.selectionBox.getBoundingClientRect();
            const canvasRect = this.canvas.canvas.getBoundingClientRect();

            // 计算选择框在画布坐标系中的范围
            const left = (boxRect.left - canvasRect.left) / this.canvas.scale + this.canvas.offset.x;
            const top = (boxRect.top - canvasRect.top) / this.canvas.scale + this.canvas.offset.y;
            const right = left + boxRect.width / this.canvas.scale;
            const bottom = top + boxRect.height / this.canvas.scale;

            // 选择框内的节点
            for (const [id, node] of this.canvas.nodes) {
                if (node.x >= left && node.x + node.width <= right &&
                    node.y >= top && node.y + node.height <= bottom) {
                    this.multiSelectedNodes.add(id);
                }
            }

            this.selectionBox.remove();
            this.selectionBox = null;
        }

        this.isSelecting = false;
        this.selectionStart = null;
        this.updateSelection();
    }

    /**
     * 选择节点
     */
    selectNode(nodeId, { notify = true } = {}) {
        this.multiSelectedNodes.add(nodeId);
        this.canvas.selectedNode = nodeId;
        this.updateSelection();
        if (notify) {
            this.notifyNodeSelectionChanged('select-node');
        }
    }

    /**
     * 切换节点选择
     */
    toggleNodeSelection(nodeId, { notify = true } = {}) {
        if (this.multiSelectedNodes.has(nodeId)) {
            this.multiSelectedNodes.delete(nodeId);
            if (this.canvas.selectedNode === nodeId) {
                this.canvas.selectedNode = null;
            }
        } else {
            this.multiSelectedNodes.add(nodeId);
            this.canvas.selectedNode = nodeId;
        }
        this.updateSelection();
        if (notify) {
            this.notifyNodeSelectionChanged('toggle-node-selection');
        }
    }

    /**
     * 清除选择
     */
    clearSelection({ notify = true } = {}) {
        this.multiSelectedNodes.clear();
        this.canvas.selectedNode = null;
        this.updateSelection();
        if (notify) {
            this.notifyNodeSelectionChanged('clear-selection');
        }
    }

    /**
     * 全选
     */
    selectAll() {
        this.multiSelectedNodes.clear();
        for (const id of this.canvas.nodes.keys()) {
            this.multiSelectedNodes.add(id);
        }
        this.updateSelection();
        showToast(`已选择 ${this.multiSelectedNodes.size} 个节点`, 'info');
    }

    /**
     * 更新选择显示
     */
    updateSelection() {
        this.canvas.invalidate?.();
    }

    cloneNodeForClipboard(node) {
        const clone = typeof structuredClone === 'function'
            ? structuredClone(node)
            : JSON.parse(JSON.stringify(node));
        clone.inputs = (node.inputs || []).map(port => ({ ...port }));
        clone.outputs = (node.outputs || []).map(port => ({ ...port }));
        clone.parameters = (node.parameters || []).map(param => ({ ...param }));
        clone.metadata = node.metadata ? { ...node.metadata } : node.metadata;
        return clone;
    }

    /**
     * 复制选中节点
     */
    copySelectedNodes() {
        if (this.multiSelectedNodes.size === 0) return;

        this.copiedNodes = [];
        for (const nodeId of this.multiSelectedNodes) {
            const node = this.canvas.nodes.get(nodeId);
            if (node) {
                this.copiedNodes.push(this.cloneNodeForClipboard(node));
            }
        }

        showToast(`已复制 ${this.copiedNodes.length} 个节点`, 'success');
    }

    /**
     * 粘贴节点
     */
    pasteNodes() {
        if (this.copiedNodes.length === 0) {
            showToast('剪贴板为空', 'warning');
            return;
        }

        const offset = 20;
        this.clearSelection();

        this.copiedNodes.forEach(node => {
            const clonedNode = this.cloneNodeForClipboard(node);
            const { id: _copiedId, x: _copiedX, y: _copiedY, ...nodeConfig } = clonedNode;
            const newNode = this.canvas.addNode(node.type, node.x + offset, node.y + offset, {
                ...nodeConfig,
                title: clonedNode.title,
                color: clonedNode.color,
                icon: clonedNode.icon,
                iconPath: clonedNode.iconPath,
                metadata: clonedNode.metadata,
                disabled: clonedNode.disabled,
                parameters: clonedNode.parameters,
                inputs: (clonedNode.inputs || []).map(port => ({ ...port, id: this.canvas.generateUUID?.() || undefined })),
                outputs: (clonedNode.outputs || []).map(port => ({ ...port, id: this.canvas.generateUUID?.() || undefined }))
            });
            this.multiSelectedNodes.add(newNode.id);
        });

        this.saveState();
        showToast(`已粘贴 ${this.copiedNodes.length} 个节点`, 'success');
    }

    /**
     * 删除选中节点
     */
    duplicateNodeFromCanvasRequest(nodeId) {
        if (!nodeId || typeof this.canvas.duplicateNode !== 'function') {
            return false;
        }

        const duplicatedNode = this.canvas.duplicateNode(nodeId);
        if (!duplicatedNode) {
            return false;
        }

        this.clearSelection({ notify: false });
        this.multiSelectedNodes.add(duplicatedNode.id);
        this.canvas.selectedNode = duplicatedNode.id;
        this.notifyNodeSelectionChanged('duplicate-node-request');
        this.saveState();
        return true;
    }

    toggleNodeDisabledFromCanvasRequest(nodeId) {
        if (!nodeId || typeof this.canvas.toggleNodeDisabled !== 'function') {
            return false;
        }

        const toggled = this.canvas.toggleNodeDisabled(nodeId);
        if (!toggled) {
            return false;
        }

        if (this.canvas.selectedNode === nodeId) {
            this.notifyNodeSelectionChanged();
        }
        this.saveState();
        return true;
    }

    deleteSelectedNodes() {
        if (this.multiSelectedNodes.size === 0) return false;

        let count = 0;
        for (const nodeId of [...this.multiSelectedNodes]) {
            const node = this.canvas.nodes.get(nodeId);
            if (!node) {
                this.multiSelectedNodes.delete(nodeId);
                continue;
            }

            if (node._systemNode) {
                continue;
            }

            if (this.canvas.removeNode(nodeId)) {
                count++;
            }
        }

        if (count === 0) {
            return false;
        }

        this.multiSelectedNodes.clear();
        this.canvas.selectedNode = null;
        this.notifyNodeSelectionChanged();
        this.saveState();
        showToast(`已删除 ${count} 个节点`, 'success');
        return true;
    }

    /**
     * 保存状态（历史记录）
     */
    deleteSelectedItems() {
        if (this.multiSelectedNodes.size > 0) {
            if (this.deleteSelectedNodes()) {
                return true;
            }
            if (this.multiSelectedNodes.size > 0) {
                return false;
            }
        }

        if (this.canvas.selectedNode) {
            const nodeId = this.canvas.selectedNode;
            const removed = this.canvas.removeNode(nodeId);
            if (!removed) {
                if (!this.canvas.nodes.has(nodeId)) {
                    this.canvas.selectedNode = null;
                    this.notifyNodeSelectionChanged();
                }
                return false;
            }
            this.canvas.selectedNode = null;
            this.notifyNodeSelectionChanged();
            this.saveState();
            showToast('已删除选中节点', 'success');
            return true;
        }

        if (this.canvas.selectedConnection) {
            const connectionId = this.canvas.selectedConnection.id;
            const removed = this.canvas.removeConnection(connectionId);
            if (!removed) {
                if (!this.canvas.connections.some(connection => connection.id === connectionId)) {
                    this.canvas.selectedConnection = null;
                }
                return false;
            }
            this.saveState();
            showToast('已删除选中连接', 'success');
            return true;
        }

        return false;
    }

    saveState() {
        const state = {
            nodes: Array.from(this.canvas.nodes.entries()),
            connections: [...this.canvas.connections]
        };

        // 移除当前指针之后的历史
        this.history = this.history.slice(0, this.historyIndex + 1);

        // 添加新状态
        this.history.push(JSON.stringify(state));

        // 限制历史大小
        if (this.history.length > this.maxHistorySize) {
            this.history.shift();
        } else {
            this.historyIndex++;
        }

        this.syncProjectFlow();
    }

    /**
     * 撤销
     */
    undo() {
        if (this.historyIndex > 0) {
            this.historyIndex--;
            this.restoreState();
            showToast('已撤销', 'info');
        } else {
            showToast('没有可撤销的操作', 'warning');
        }
    }

    /**
     * 重做
     */
    redo() {
        if (this.historyIndex < this.history.length - 1) {
            this.historyIndex++;
            this.restoreState();
            showToast('已重做', 'info');
        } else {
            showToast('没有可重做的操作', 'warning');
        }
    }

    /**
     * 恢复状态
     */
    restoreState() {
        const state = JSON.parse(this.history[this.historyIndex]);
        this.canvas.nodes = new Map(state.nodes);
        this.canvas.connections = state.connections;
        this.canvas._rebuildConnectionIndex?.();
        this.resetTransientInteractionAfterRestore();
        this.reconcileSelectionAfterRestore();
        this.canvas.markFlowStructureChanged?.('history-restore');
        this.canvas.render();
        this.syncProjectFlow();
    }

    resetTransientInteractionAfterRestore() {
        this.isConnecting = false;
        this.connectionStart = null;
        this.connectionEnd = null;
        this.connectionAnchor = null;
        this.connectionDidDrag = false;

        this.isDraggingNodes = false;
        this.dragStartPos = null;
        this.dragInitialPositions?.clear?.();
        this.hasNodeDragMoved = false;

        this.isPanning = false;
        this.panStart = null;
        this.panStartOffset = null;

        this.isSelecting = false;
        this.selectionStart = null;
        this.selectionBox?.remove?.();
        this.selectionBox = null;

        this.canvas.draggedNode = null;
        this.canvas.isConnecting = false;
        this.canvas.connectingFrom = null;
        this.canvas.hoveredPort = null;
        if (this.canvas.canvas?.style) {
            this.canvas.canvas.style.cursor = 'default';
        }
    }

    reconcileSelectionAfterRestore() {
        if (this.canvas.selectedNode && !this.canvas.nodes.has(this.canvas.selectedNode)) {
            this.canvas.selectedNode = null;
        }

        if (this.multiSelectedNodes) {
            for (const nodeId of [...this.multiSelectedNodes]) {
                if (!this.canvas.nodes.has(nodeId)) {
                    this.multiSelectedNodes.delete(nodeId);
                }
            }
        }

        if (this.canvas.selectedConnection) {
            const selectedConnectionId = this.canvas.selectedConnection.id;
            this.canvas.selectedConnection =
                this.canvas.connections.find(connection => connection.id === selectedConnectionId) || null;
        }

        this.notifyNodeSelectionChanged();
    }

    notifyNodeSelectionChanged(reason = 'flow-editor-interaction') {
        const selectedNode = this.canvas.selectedNode
            ? this.canvas.nodes.get(this.canvas.selectedNode) || null
            : null;
        this.canvas.onNodeSelected?.(selectedNode);
        this.canvas.markSelectionChanged?.(reason);
    }

    /**
     * 绘制贝塞尔曲线
     */
    drawBezierCurve(x1, y1, x2, y2) {
        const cp1x = x1 + (x2 - x1) / 2;
        const cp1y = y1;
        const cp2x = x1 + (x2 - x1) / 2;
        const cp2y = y2;

        this.canvas.ctx.moveTo(x1, y1);
        this.canvas.ctx.bezierCurveTo(cp1x, cp1y, cp2x, cp2y, x2, y2);
    }
}

export default FlowEditorInteraction;
