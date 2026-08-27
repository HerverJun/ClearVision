import {
    arePortTypesCompatible,
    buildPortTooltipModel,
    canonicalizeOperatorPortType,
    formatPortTypeForMessage,
    getPortTypeColor,
    getPortTypeMismatchMessage,
    normalizePortType as normalizeFlowPortType
} from './portTypeCompatibility.mjs';

/**
 * Flow canvas rendering and interaction controller.
 * Set `window.__FLOW_CANVAS_DEBUG__ = true` to enable debug logging.
 */
const DEBUG_FLOW_CANVAS = false;
function flowDebugEnabled() {
    return DEBUG_FLOW_CANVAS || (typeof window !== 'undefined' && window.__FLOW_CANVAS_DEBUG__ === true);
}

/**
 * Encoding cleanup: previous comment text was unreadable.
 */
const COMM_OPERATOR_TYPES = new Set([
    'HttpRequest', 'MqttPublish', 'ModbusCommunication',
    'OmronFinsCommunication', 'MitsubishiMcCommunication',
    'TcpCommunication', 'SerialCommunication', 'DatabaseWrite'
]);

const LEGACY_OPERATOR_TYPE_ALIASES = {
    'Preprocessing': 'Filtering',
    'GaussianBlur': 'Filtering',
    'OnnxInference': 'DeepLearning',
    'ModbusRtuCommunication': 'ModbusCommunication'
};

const PORT_HIT_RADIUS_PX = 12;       // 端口屏幕命中半径
const CONNECTION_HIT_RADIUS_PX = 10; // 连接线命中半径
const CONNECTION_HIT_SAMPLES = 16;   // 连接线命中采样点数
const NODE_DEFAULT_WIDTH = 140;
const NODE_MIN_HEIGHT = 60;
const NODE_HEADER_HEIGHT = 24;
const NODE_PORT_TOP_PADDING = 10;
const NODE_PORT_BOTTOM_PADDING = 10;
const NODE_PORT_ROW_HEIGHT = 18;

const FLOW_CANVAS_THEME_DEFAULTS = Object.freeze({
    grid: 'rgba(143, 158, 174, 0.2)',
    nodeBackgroundStart: 'rgba(26, 58, 82, 0.86)',
    nodeBackgroundEnd: 'rgba(13, 27, 42, 0.94)',
    nodeSelectedStart: 'rgba(45, 74, 94, 0.94)',
    nodeSelectedEnd: 'rgba(26, 58, 82, 0.98)',
    nodeBorder: 'rgba(255, 255, 255, 0.14)',
    nodeText: '#ffffff',
    nodeMutedText: 'rgba(255, 255, 255, 0.58)',
    nodeShadow: 'rgba(0, 0, 0, 0.34)',
    nodeBadgeBackground: 'rgba(13, 27, 42, 0.88)',
    portOutline: '#ffffff',
    connection: '#5ac8fa',
    connectionSelected: '#ffffff',
    guide: '#75a4bf',
    minimapViewport: 'rgba(224, 106, 95, 0.9)',
    minimapSelected: '#ffffff',
    minimapDisabled: '#666b72'
});

const FLOW_CANVAS_THEME_TOKENS = Object.freeze({
    grid: '--flow-canvas-grid',
    nodeBackgroundStart: '--flow-canvas-node-background-start',
    nodeBackgroundEnd: '--flow-canvas-node-background-end',
    nodeSelectedStart: '--flow-canvas-node-selected-start',
    nodeSelectedEnd: '--flow-canvas-node-selected-end',
    nodeBorder: '--flow-canvas-node-border',
    nodeText: '--flow-canvas-node-text',
    nodeMutedText: '--flow-canvas-node-muted-text',
    nodeShadow: '--flow-canvas-node-shadow',
    nodeBadgeBackground: '--flow-canvas-node-badge-background',
    portOutline: '--flow-canvas-port-outline',
    connection: '--flow-canvas-connection',
    connectionSelected: '--flow-canvas-connection-selected',
    guide: '--flow-canvas-guide',
    minimapViewport: '--flow-canvas-minimap-viewport',
    minimapSelected: '--flow-canvas-minimap-selected',
    minimapDisabled: '--flow-canvas-minimap-disabled'
});

function portKey(nodeId, portIndex) {
    return `${nodeId}:${portIndex}`;
}


class FlowCanvas {

    constructor(canvasId) {
        this.canvas = document.getElementById(canvasId);
        this.ctx = this.canvas.getContext('2d');
        this.nodes = new Map();
        this.connections = [];
        this.decisionConfiguration = null;
        this.selectedNode = null;
        this.selectionRevision = 0;
        this.draggedNode = null;
        this.dragOffset = { x: 0, y: 0 };
        this.dragStartNodeState = null;
        this.scale = 1;
        this.offset = { x: 0, y: 0 };
        this.flowRevision = 0;
        this.viewStateListeners = new Set();
        this.structureStateListeners = new Set();
        this.selectionStateListeners = new Set();
        this.globalVariableSchema = { sourceBindings: [], targetBindings: [] };

        // 画布像素尺寸。
        this._dpr = 1;
        this._logicalWidth = 0;
        this._logicalHeight = 0;

        this.themePalette = { ...FLOW_CANVAS_THEME_DEFAULTS };
        this._themePaletteSignature = '';
        this._themeObserver = null;

        // 背景网格样式。
        this.gridSize = 20;
        this.gridColor = this.themePalette.grid;
        this.gridDotRadius = 1.05;

        // 事件回调
        this.onNodeSelected = null;
        this.onConnectionCreated = null;
        this.onSelectionDeleteRequested = null;
        this.onNodeDuplicateRequested = null;
        this.onNodeDisabledToggleRequested = null;

        // 连线交互状态。
        this.isConnecting = false;
        this.connectingFrom = null;  // { nodeId, portIndex, isOutput }
        this.mousePosition = { x: 0, y: 0 };
        this.hoveredPort = null;  // { nodeId, portIndex, isOutput }

        // DOM 事件处理器。
        this._resizeHandler = this.resize.bind(this);
        this._mouseDownHandler = this.handleMouseDown.bind(this);
        this._mouseMoveHandler = this.handleMouseMove.bind(this);
        this._mouseUpHandler = this.handleMouseUp.bind(this);
        this._mouseLeaveHandler = () => this.setHoveredPort(null);
        this._wheelHandler = this.handleWheel.bind(this);
        this._contextMenuHandler = this.handleContextMenu.bind(this);
        this._keyDownHandler = this.handleKeyDown.bind(this);
        this._dblClickHandler = this.handleDoubleClick.bind(this);
        this._visibilityHandler = this.handleVisibilityChange.bind(this);
        this._drawFrameBound = this._drawFrame.bind(this);

        // 绘制调度状态。
        this._animationFrameId = null;
        this._dirty = true; // 初始需要绘制
        this._lastFrameTime = 0; // 上一帧时间
        this._isPaused = false;      // 页面隐藏时暂停绘制

        // ResizeObserver 防抖状态。
        this._resizeObserver = null;
        this._resizeRafId = null;

        // 当前选中的连线。
        this.selectedConnection = null;

        // 连线索引。
        this._connectionById = new Map();
        this._connectionsByOutputPort = new Map();  // key=portKey -> Set<connection>
        this._connectionByInputPort = new Map(); // key=portKey -> connection

        // 流动粒子精灵缓存。
        this._particleSprite = null;
        this._particleSpriteSize = 0;

        // Encoding cleanup: previous comment text was unreadable.
        this._subGraphNodeCountCache = new WeakMap();
        this._nodesBoundsCache = null;
        this._nodesBoundsDirty = true;
        this._minimapStructureDirty = true;
        this._minimapViewportDirty = true;
        this._minimapLastDrawTime = 0;
        this._minimapStaticCache = null;
        this._minimapClickHandler = null;
        this._minimapToggleHandler = null;
        this._minimapCollapsed = false;

        // 右键菜单
        this.contextMenu = null;
        this._clickOutsideHandler = this.hideContextMenu.bind(this);
        this._portTooltip = null;
        this._portTooltipId = `${this.canvas.id || 'flow-canvas'}-port-tooltip`;
        this._baseCanvasAriaLabel = '流程编辑画布';

        this.initialize();
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    initialize() {
        this.refreshThemePalette({ invalidate: false });
        this.observeThemeChanges();
        this.resize();
        this.canvas.setAttribute?.('aria-label', this._baseCanvasAriaLabel);
        this.canvas.setAttribute?.('aria-describedby', this._portTooltipId);
        this.ensurePortTooltip();

        // Encoding cleanup: previous comment text was unreadable.
        const ResizeObserverCtor = window.ResizeObserver;
        if (ResizeObserverCtor) {
            this._resizeObserver = new ResizeObserverCtor(entries => {
                for (const entry of entries) {
                    if (entry.contentRect.width > 0 && entry.contentRect.height > 0) {
                        if (this._resizeRafId === null) {
                            this._resizeRafId = requestAnimationFrame(() => {
                                this._resizeRafId = null;
                                this.resize();
                            });
                        }
                        break;
                    }
                }
            });
            this._resizeObserver.observe(this.canvas.parentElement);
        } else {
            window.addEventListener('resize', this._resizeHandler);
        }

        // 绑定事件
        this.canvas.addEventListener('mousedown', this._mouseDownHandler);
        this.canvas.addEventListener('mousemove', this._mouseMoveHandler);
        this.canvas.addEventListener('mouseup', this._mouseUpHandler);
        this.canvas.addEventListener('mouseleave', this._mouseLeaveHandler);
        this.canvas.addEventListener('wheel', this._wheelHandler);
        this.canvas.addEventListener('contextmenu', this._contextMenuHandler);
        this.canvas.addEventListener('dblclick', this._dblClickHandler);
        window.addEventListener('keydown', this._keyDownHandler);

        document.addEventListener('visibilitychange', this._visibilityHandler);

        // Encoding cleanup: previous comment text was unreadable.
        this.invalidate();

        // Encoding cleanup: previous comment text was unreadable.
        this.initMinimap();
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    handleVisibilityChange() {
        if (document.hidden) {
            this._isPaused = true;
            if (this._animationFrameId !== null) {
                cancelAnimationFrame(this._animationFrameId);
                this._animationFrameId = null;
            }
        } else {
            this._isPaused = false;
            this.invalidate();
        }
    }

    readThemePalette() {
        const root = typeof document !== 'undefined' ? document.documentElement : null;
        const readComputedStyle = typeof getComputedStyle === 'function' ? getComputedStyle : null;
        if (!root || !readComputedStyle) {
            return { ...FLOW_CANVAS_THEME_DEFAULTS };
        }

        const styles = readComputedStyle(root);
        return Object.fromEntries(Object.entries(FLOW_CANVAS_THEME_TOKENS).map(([key, token]) => {
            const value = styles.getPropertyValue(token).trim();
            return [key, value || FLOW_CANVAS_THEME_DEFAULTS[key]];
        }));
    }

    refreshThemePalette({ invalidate = true } = {}) {
        const nextPalette = this.readThemePalette();
        const signature = Object.values(nextPalette).join('|');
        if (signature === this._themePaletteSignature) {
            return false;
        }

        this.themePalette = nextPalette;
        this.gridColor = nextPalette.grid;
        this._themePaletteSignature = signature;
        this._minimapStaticCache = null;
        this._minimapStructureDirty = true;
        this._minimapViewportDirty = true;

        if (invalidate) {
            this.invalidate();
        }
        return true;
    }

    observeThemeChanges() {
        const root = typeof document !== 'undefined' ? document.documentElement : null;
        const Observer = typeof MutationObserver === 'function' ? MutationObserver : null;
        if (!root || !Observer || this._themeObserver) {
            return;
        }

        this._themeObserver = new Observer(records => {
            if (records.some(record => record.attributeName === 'data-theme')) {
                this.refreshThemePalette();
            }
        });
        this._themeObserver.observe(root, { attributes: true, attributeFilter: ['data-theme'] });
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    destroy() {
        if (this._animationFrameId !== null) {
            cancelAnimationFrame(this._animationFrameId);
            this._animationFrameId = null;
        }
        if (this._resizeRafId !== null) {
            cancelAnimationFrame(this._resizeRafId);
            this._resizeRafId = null;
        }

        if (!this._resizeObserver) {
            window.removeEventListener('resize', this._resizeHandler);
        } else {
            this._resizeObserver.disconnect();
            this._resizeObserver = null;
        }

        window.removeEventListener('keydown', this._keyDownHandler);
        document.removeEventListener('visibilitychange', this._visibilityHandler);
        this._themeObserver?.disconnect?.();
        this._themeObserver = null;

        this.canvas.removeEventListener('mousedown', this._mouseDownHandler);
        this.canvas.removeEventListener('mousemove', this._mouseMoveHandler);
        this.canvas.removeEventListener('mouseup', this._mouseUpHandler);
        this.canvas.removeEventListener('mouseleave', this._mouseLeaveHandler);
        this.canvas.removeEventListener('wheel', this._wheelHandler);
        this.canvas.removeEventListener('contextmenu', this._contextMenuHandler);
        this.canvas.removeEventListener('dblclick', this._dblClickHandler);

        this.nodes.clear();
        this.connections = [];
        this._connectionById.clear();
        this._connectionsByOutputPort.clear();
        this._connectionByInputPort.clear();
        this.selectedNode = null;
        this.draggedNode = null;
        this.dragStartNodeState = null;
        this.selectedConnection = null;
        this.viewStateListeners.clear();
        this.structureStateListeners.clear();
        this.selectionStateListeners.clear();
        this._particleSprite = null;

        if (this.minimapCanvas && this._minimapClickHandler) {
            this.minimapCanvas.removeEventListener('click', this._minimapClickHandler);
            this._minimapClickHandler = null;
        }
        if (this.minimapToggle && this._minimapToggleHandler) {
            this.minimapToggle.removeEventListener('click', this._minimapToggleHandler);
            this._minimapToggleHandler = null;
        }

        if (this.minimap) {
            this.minimap.remove();
            this.minimap = null;
            this.minimapCanvas = null;
            this.minimapToggle = null;
            this._minimapStaticCache = null;
        }

        this._portTooltip?.remove?.();
        this._portTooltip = null;

        this.hideContextMenu();
    }

    /**
     * 根据 devicePixelRatio 调整画布 backing store。
     * - canvas.width/height 使用 dpr 放大 backing store。
     * - canvas.style.width/height 保持 CSS 像素尺寸。
     * - ctx.setTransform(dpr,0,0,dpr,0,0) 让所有绘制坐标使用逻辑像素
     */
    resize() {
        const container = this.canvas.parentElement;
        if (!container) {
            return;
        }
        const cssWidth = container.clientWidth;
        const cssHeight = container.clientHeight;
        const dpr = (typeof window !== 'undefined' && window.devicePixelRatio) ? window.devicePixelRatio : 1;

        const backingWidth = Math.max(0, Math.round(cssWidth * dpr));
        const backingHeight = Math.max(0, Math.round(cssHeight * dpr));

        if (this.canvas.width !== backingWidth) this.canvas.width = backingWidth;
        if (this.canvas.height !== backingHeight) this.canvas.height = backingHeight;
        if (this.canvas.style.width !== `${cssWidth}px`) this.canvas.style.width = `${cssWidth}px`;
        if (this.canvas.style.height !== `${cssHeight}px`) this.canvas.style.height = `${cssHeight}px`;

        this._dpr = dpr;
        this._logicalWidth = cssWidth;
        this._logicalHeight = cssHeight;

        this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        this.invalidate();
        this.notifyViewStateChanged();
    }

    /**
     * 生成UUID
     */
    generateUUID() {
        if (typeof crypto !== 'undefined' && crypto.randomUUID) {
            return crypto.randomUUID();
        }
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    /**
     * 添加节点
     */
    addNode(type, x, y, config = {}) {
        const canonicalType = this.normalizeOperatorType(type);
        const {
            inputs: configInputs = [],
            outputs: configOutputs = [],
            ...nodeConfig
        } = config || {};
        const node = {
            id: this.generateUUID(),
            type: canonicalType,
            x,
            y,
            width: NODE_DEFAULT_WIDTH,
            height: NODE_MIN_HEIGHT,
            title: nodeConfig.title || canonicalType,
            color: nodeConfig.color || '#1890ff',
            ...nodeConfig,
            inputs: configInputs.map(p => ({
                ...p,
                id: p.id || this.generateUUID(),
                name: p.name,
                type: p.type
            })),
            outputs: configOutputs.map(p => ({
                ...p,
                id: p.id || this.generateUUID(),
                name: p.name,
                type: p.type
            }))
        };
        node.height = Math.max(
            this.getRequiredNodeHeight(node.inputs, node.outputs),
            Number(node.height) || NODE_MIN_HEIGHT
        );

        this.nodes.set(node.id, node);
        this.invalidate();
        this.markFlowStructureChanged('addNode');
        return node;
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     */
    removeNode(nodeId) {
        const node = this.nodes.get(nodeId);
        if (!node) {
            return false;
        }
        if (node._systemNode) {
            console.warn('[FlowCanvas] System node cannot be removed:', node.title || node.type);
            return false;
        }

        // Encoding cleanup: previous comment text was unreadable.
        const remaining = [];
        for (const conn of this.connections) {
            if (conn.source === nodeId || conn.target === nodeId) {
                this._unindexConnection(conn);
            } else {
                remaining.push(conn);
            }
        }
        this.connections = remaining;

        const selectionChanged = this.selectedNode === nodeId ||
            (this.selectedConnection && (this.selectedConnection.source === nodeId || this.selectedConnection.target === nodeId));
        this.nodes.delete(nodeId);
        if (this.selectedNode === nodeId) {
            this.selectedNode = null;
        }
        if (this.selectedConnection && (this.selectedConnection.source === nodeId || this.selectedConnection.target === nodeId)) {
            this.selectedConnection = null;
        }
        this.invalidate();
        this.markFlowStructureChanged('removeNode');
        if (selectionChanged) {
            this.markSelectionChanged('removeNode');
        }
        return true;
    }

    /**
     * 添加连接
     */
    addConnection(sourceId, sourcePort, targetId, targetPort) {
        const validationError = this.getConnectionValidationError(sourceId, sourcePort, targetId, targetPort);
        if (validationError) {
            console.warn('[FlowCanvas] Connection rejected:', validationError, { sourceId, sourcePort, targetId, targetPort });
            return null;
        }

        const connection = {
            id: this.generateUUID(),
            source: sourceId,
            sourcePort,
            target: targetId,
            targetPort
        };

        this.connections.push(connection);
        this._indexConnection(connection);
        this.invalidate();
        this.markFlowStructureChanged('addConnection');
        return connection;
    }

    getConnectionValidationError(sourceId, sourcePort, targetId, targetPort) {
        const sourceNode = this.nodes.get(sourceId);
        const targetNode = this.nodes.get(targetId);
        if (!sourceNode || !targetNode) {
            return 'missing-node';
        }

        if (sourceId === targetId) {
            return 'self-connection';
        }

        if (!sourceNode.outputs?.[sourcePort] || !targetNode.inputs?.[targetPort]) {
            return 'missing-port';
        }

        const sourcePortDefinition = sourceNode.outputs[sourcePort];
        const targetPortDefinition = targetNode.inputs[targetPort];
        if (!this.checkTypeCompatibility(this.readPortType(sourcePortDefinition), this.readPortType(targetPortDefinition))) {
            return 'incompatible-port-type';
        }

        const existingConn = this.connections.find(conn =>
            conn.source === sourceId &&
            conn.sourcePort === sourcePort &&
            conn.target === targetId &&
            conn.targetPort === targetPort
        );
        if (existingConn) {
            return 'duplicate-connection';
        }

        if (this.getConnectionAtPort(targetId, targetPort, false)) {
            return 'input-port-occupied';
        }

        if (this.wouldCreateCycle(sourceId, targetId)) {
            return 'cycle';
        }

        return null;
    }

    wouldCreateCycle(sourceId, targetId) {
        if (sourceId === targetId) {
            return true;
        }

        const visited = new Set();
        const stack = [targetId];
        while (stack.length > 0) {
            const current = stack.pop();
            if (current === sourceId) {
                return true;
            }

            if (!current || visited.has(current)) {
                continue;
            }

            visited.add(current);
            const outgoing = this._connectionsByOutputPort.get(portKey(current, 0));
            if (outgoing) {
                for (const conn of outgoing) {
                    if (!visited.has(conn.target)) {
                        stack.push(conn.target);
                    }
                }
            }

            const node = this.nodes.get(current);
            const outputCount = Math.max(0, node?.outputs?.length || 0);
            for (let portIndex = 1; portIndex < outputCount; portIndex += 1) {
                const portConnections = this._connectionsByOutputPort.get(portKey(current, portIndex));
                if (!portConnections) {
                    continue;
                }
                for (const conn of portConnections) {
                    if (!visited.has(conn.target)) {
                        stack.push(conn.target);
                    }
                }
            }
        }

        return false;
    }

    _indexConnection(connection) {
        this._connectionById.set(connection.id, connection);
        const outKey = portKey(connection.source, connection.sourcePort);
        let outSet = this._connectionsByOutputPort.get(outKey);
        if (!outSet) {
            outSet = new Set();
            this._connectionsByOutputPort.set(outKey, outSet);
        }
        outSet.add(connection);
        this._connectionByInputPort.set(portKey(connection.target, connection.targetPort), connection);
    }

    _unindexConnection(connection) {
        if (!connection) return;
        this._connectionById.delete(connection.id);
        const outKey = portKey(connection.source, connection.sourcePort);
        const outSet = this._connectionsByOutputPort.get(outKey);
        if (outSet) {
            outSet.delete(connection);
            if (outSet.size === 0) {
                this._connectionsByOutputPort.delete(outKey);
            }
        }
        const inKey = portKey(connection.target, connection.targetPort);
        if (this._connectionByInputPort.get(inKey) === connection) {
            this._connectionByInputPort.delete(inKey);
        }
    }

    _rebuildConnectionIndex() {
        this._connectionById.clear();
        this._connectionsByOutputPort.clear();
        this._connectionByInputPort.clear();
        for (const conn of this.connections) {
            this._indexConnection(conn);
        }
    }

    getFlowRevision() {
        return this.flowRevision;
    }

    getViewportState() {
        return {
            scale: this.scale,
            offset: {
                x: this.offset.x,
                y: this.offset.y
            },
            // Encoding cleanup: previous comment text was unreadable.
            canvasWidth: this._logicalWidth,
            canvasHeight: this._logicalHeight,
            flowRevision: this.flowRevision
        };
    }

    getNodeScreenRect(nodeId) {
        const node = this.nodes.get(nodeId);
        if (!node) {
            return null;
        }

        // Encoding cleanup: previous comment text was unreadable.
        return {
            x: (node.x - this.offset.x) * this.scale,
            y: (node.y - this.offset.y) * this.scale,
            width: node.width * this.scale,
            height: node.height * this.scale
        };
    }

    setGlobalVariableSchema(schema) {
        this.globalVariableSchema = {
            sourceBindings: Array.isArray(schema?.sourceBindings) ? schema.sourceBindings : (schema?.SourceBindings || []),
            targetBindings: Array.isArray(schema?.targetBindings) ? schema.targetBindings : (schema?.TargetBindings || [])
        };
        this.invalidate?.();
    }

    subscribeViewState(listener) {
        if (typeof listener !== 'function') {
            return () => {};
        }

        this.viewStateListeners.add(listener);
        listener(this.getViewportState());
        return () => this.viewStateListeners.delete(listener);
    }

    subscribeStructureState(listener) {
        if (typeof listener !== 'function') {
            return () => {};
        }

        this.structureStateListeners.add(listener);
        listener({
            flowRevision: this.flowRevision,
            reason: 'initial'
        });
        return () => this.structureStateListeners.delete(listener);
    }

    getSelectionState() {
        return {
            selectedNodeId: this.selectedNode || null,
            selectedConnectionId: this.selectedConnection?.id || null,
            selectionRevision: this.selectionRevision,
            flowRevision: this.flowRevision
        };
    }

    subscribeSelectionState(listener) {
        if (typeof listener !== 'function') {
            return () => {};
        }

        this.selectionStateListeners.add(listener);
        listener({
            ...this.getSelectionState(),
            reason: 'initial'
        });
        return () => this.selectionStateListeners.delete(listener);
    }

    notifySelectionStateChanged(reason = 'unknown') {
        const payload = {
            ...this.getSelectionState(),
            reason
        };

        this.selectionStateListeners.forEach(listener => {
            try {
                listener(payload);
            } catch (error) {
                console.error('[FlowCanvas] Selection state listener failed:', error);
            }
        });
    }

    markSelectionChanged(reason = 'unknown') {
        this.selectionRevision += 1;
        this.notifySelectionStateChanged(reason);
    }

    notifyViewStateChanged() {
        this._minimapViewportDirty = true;
        const state = this.getViewportState();
        this.viewStateListeners.forEach(listener => {
            try {
                listener(state);
            } catch (error) {
                console.error('[FlowCanvas] View state listener failed:', error);
            }
        });
    }

    markFlowStructureChanged(reason = 'unknown') {
        this.flowRevision += 1;
        this._markNodesBoundsDirty();
        const payload = {
            flowRevision: this.flowRevision,
            reason
        };

        this.structureStateListeners.forEach(listener => {
            try {
                listener(payload);
            } catch (error) {
                console.error('[FlowCanvas] Structure state listener failed:', error);
            }
        });

        this.notifyViewStateChanged();
        // 子图节点数量缓存。
        this._subGraphNodeCountCache = new WeakMap();
    }

    _markNodesBoundsDirty() {
        this._nodesBoundsDirty = true;
        this._nodesBoundsCache = null;
        this._minimapStructureDirty = true;
    }

    _isSamePortState(left, right) {
        return left?.nodeId === right?.nodeId &&
            left?.portIndex === right?.portIndex &&
            Boolean(left?.isOutput) === Boolean(right?.isOutput) &&
            Boolean(left?.hasConnection) === Boolean(right?.hasConnection) &&
            String(left?.compatibility || '') === String(right?.compatibility || '') &&
            String(left?.incompatibilityMessage || '') === String(right?.incompatibilityMessage || '');
    }

    ensurePortTooltip() {
        if (this._portTooltip || typeof document === 'undefined') {
            return this._portTooltip;
        }

        const parent = this.canvas.parentElement;
        if (!parent) {
            return null;
        }

        const tooltip = document.createElement('div');
        tooltip.id = this._portTooltipId;
        tooltip.className = 'flow-port-tooltip';
        tooltip.setAttribute('role', 'tooltip');
        tooltip.setAttribute('aria-live', 'polite');
        tooltip.hidden = true;
        parent.appendChild(tooltip);
        this._portTooltip = tooltip;
        return tooltip;
    }

    getPortDefinition(portState) {
        if (!portState) return null;
        const node = this.nodes.get(portState.nodeId);
        if (!node) return null;
        const ports = portState.isOutput ? (node.outputs || []) : (node.inputs || []);
        const port = ports[portState.portIndex];
        return port ? { node, port } : null;
    }

    setHoveredPort(portState, pointer = null) {
        const changed = !this._isSamePortState(this.hoveredPort, portState);
        this.hoveredPort = portState || null;
        this.updatePortTooltip(this.hoveredPort, pointer);
        if (changed) {
            this.invalidate();
        }
    }

    updatePortTooltip(portState, pointer = null) {
        const tooltip = this.ensurePortTooltip();
        const definition = this.getPortDefinition(portState);
        if (!tooltip || !definition) {
            if (tooltip) {
                tooltip.hidden = true;
                tooltip.textContent = '';
                tooltip.removeAttribute?.('data-compatibility');
            }
            this.canvas.title = '';
            this.canvas.setAttribute?.('aria-label', this._baseCanvasAriaLabel);
            return;
        }

        const model = buildPortTooltipModel(definition.port, {
            direction: portState.isOutput ? 'output' : 'input',
            incompatibilityMessage: portState.incompatibilityMessage
        });
        tooltip.textContent = model.text;
        tooltip.hidden = false;
        tooltip.setAttribute?.('data-compatibility', portState.compatibility || 'neutral');
        this.canvas.title = model.text;
        this.canvas.setAttribute?.('aria-label', model.text);

        const parent = this.canvas.parentElement;
        const parentRect = parent?.getBoundingClientRect?.();
        const canvasRect = this.canvas.getBoundingClientRect?.();
        const portPosition = this.getPortPosition(portState.nodeId, portState.portIndex, portState.isOutput);
        if (!parentRect || !canvasRect || !portPosition) {
            return;
        }

        const pointerLeft = Number.isFinite(pointer?.clientX)
            ? pointer.clientX - parentRect.left
            : canvasRect.left - parentRect.left + portPosition.x;
        const pointerTop = Number.isFinite(pointer?.clientY)
            ? pointer.clientY - parentRect.top
            : canvasRect.top - parentRect.top + portPosition.y;
        const preferredLeft = pointerLeft + (portState.isOutput ? 14 : 18);
        const preferredTop = pointerTop + 14;
        const maxLeft = Math.max(8, (parent?.clientWidth || parentRect.width || 0) - (Number(tooltip.offsetWidth) || 0) - 8);
        const maxTop = Math.max(8, (parent?.clientHeight || parentRect.height || 0) - (Number(tooltip.offsetHeight) || 0) - 8);
        tooltip.style.left = `${Math.max(8, Math.min(preferredLeft, maxLeft))}px`;
        tooltip.style.top = `${Math.max(8, Math.min(preferredTop, maxTop))}px`;
    }

    /**
     * 绘制背景网格。
     */
    drawGrid() {
        const width = this._logicalWidth;
        const height = this._logicalHeight;

        this.ctx.fillStyle = this.gridColor;

        const gridScreenSize = this.gridSize * this.scale;
        const minScreenSpacing = 12;
        const stepMultiplier = Math.max(1, Math.ceil(minScreenSpacing / Math.max(gridScreenSize, 0.01)));
        const dotStep = this.gridSize * stepMultiplier;

        const startX = Math.floor(this.offset.x / dotStep) * dotStep;
        const startY = Math.floor(this.offset.y / dotStep) * dotStep;

        // 计算当前视口可见区域。
        const visibleWidth = width / this.scale;
        const visibleHeight = height / this.scale;

        const radius = Math.max(0.65, Math.min(1.35, this.gridDotRadius * Math.sqrt(Math.max(this.scale, 0.2))));
        this.ctx.beginPath();

        for (let x = startX; x < this.offset.x + visibleWidth; x += dotStep) {
            const screenX = (x - this.offset.x) * this.scale;
            if (screenX < -radius || screenX > width + radius) {
                continue;
            }

            for (let y = startY; y < this.offset.y + visibleHeight; y += dotStep) {
                const screenY = (y - this.offset.y) * this.scale;
                if (screenY < -radius || screenY > height + radius) {
                    continue;
                }

                this.ctx.moveTo(screenX + radius, screenY);
                this.ctx.arc(screenX, screenY, radius, 0, Math.PI * 2);
            }
        }

        this.ctx.fill();
    }

    /**
     * 绘制节点主体、端口和状态边框。
     * 包含 ForEach、通信和文件参数的特殊高亮。
     */
    drawNode(node) {
        const x = (node.x - this.offset.x) * this.scale;
        const y = (node.y - this.offset.y) * this.scale;
        const w = node.width * this.scale;
        const h = node.height * this.scale;
        const isSelected = this.selectedNode === node.id;
        const palette = this.themePalette || FLOW_CANVAS_THEME_DEFAULTS;

        this.ctx.save();
        if (node.disabled) {
            this.ctx.globalAlpha = 0.52;
        }

        // 识别 ForEach 运行模式。
        const isForEach = node.type === 'ForEach';
        const ioMode = isForEach ? (node.parameters?.find(p => p.name === 'IoMode' || p.Name === 'IoMode')?.value || 'Parallel') : null;
        const isSequential = ioMode === 'Sequential';

        // 默认边框样式。
        let borderColor = isSelected ? node.color : palette.nodeBorder;
        let borderWidth = isSelected ? 3 : 1;
        let glowColor = null;

        // 特殊类型高亮。
        const isCommunicationOp = COMM_OPERATOR_TYPES.has(node.type);
        const hasFileParam = node.parameters && node.parameters.some(
            p => p.dataType === 'file' && p.value
        );

        if (node.status === 'running') {
            borderColor = '#5ac8fa';
            borderWidth = 3;
            glowColor = 'rgba(52, 152, 219, 0.6)';
        } else if (node.status === 'success') {
            borderColor = '#34c759';
            glowColor = 'rgba(46, 204, 113, 0.5)';
        } else if (node.status === 'error') {
            borderColor = '#e74c3c';
            glowColor = 'rgba(231, 76, 60, 0.5)';
        } else if (isForEach && isSequential) {
            // ForEach Sequential 模式：橙色边框。
            borderColor = '#fa8c16';
            borderWidth = 2;
            glowColor = 'rgba(250, 140, 22, 0.3)';
        } else if (isForEach) {
            // ForEach Parallel 模式：青色边框。
            borderColor = '#13c2c2';
            borderWidth = 2;
            glowColor = 'rgba(19, 194, 194, 0.2)';
        } else if (isCommunicationOp) {
            // 通信算子：红色边框。
            borderColor = '#f5222d';
            borderWidth = 2;
            glowColor = 'rgba(245, 34, 45, 0.3)';
        } else if (hasFileParam) {
            // 已配置文件参数：橙色边框。
            borderColor = '#fa8c16';
            borderWidth = 2;
        } else if (isSelected) {
            glowColor = `${node.color}80`; // 50% opacity
        }

        // Encoding cleanup: previous comment text was unreadable.
        this.ctx.save();
        if (glowColor) {
            this.ctx.shadowColor = glowColor;
            this.ctx.shadowBlur = 15;
            this.ctx.shadowOffsetX = 0;
            this.ctx.shadowOffsetY = 0;
        } else {
            this.ctx.shadowColor = palette.nodeShadow;
            this.ctx.shadowBlur = 8;
            this.ctx.shadowOffsetX = 2;
            this.ctx.shadowOffsetY = 2;
        }

        // 节点背景 - 渐变填充
        const gradient = this.ctx.createLinearGradient(x, y, x, y + h);
        gradient.addColorStop(0, isSelected ? palette.nodeSelectedStart : palette.nodeBackgroundStart);
        gradient.addColorStop(1, isSelected ? palette.nodeSelectedEnd : palette.nodeBackgroundEnd);

        this.ctx.fillStyle = gradient;
        this.ctx.strokeStyle = borderColor;
        this.ctx.lineWidth = borderWidth;

        // ForEach 节点使用虚线边框
        if (isForEach) {
            this.ctx.setLineDash([6, 4]);
        }

        // Encoding cleanup: previous comment text was unreadable.
        this.roundRect(x, y, w, h, 8);
        this.ctx.fill();
        this.ctx.stroke();

        // Encoding cleanup: previous comment text was unreadable.
        if (isForEach) {
            this.ctx.setLineDash([]);
        }

        this.ctx.restore();

        // Encoding cleanup: previous comment text was unreadable.
        const headerGradient = this.ctx.createLinearGradient(x, y, x + w, y);
        headerGradient.addColorStop(0, node.color);
        headerGradient.addColorStop(1, this.adjustColor(node.color, -20));
        this.ctx.fillStyle = headerGradient;
        this.roundRect(x, y, w, 24 * this.scale, { tl: 8, tr: 8, bl: 0, br: 0 });
        this.ctx.fill();

        // Encoding cleanup: previous comment text was unreadable.
        if (node.iconPath) {
            const targetSize = 16 * this.scale;
            const scaleFactor = targetSize / 24; // ViewBox 24x24
            
            this.ctx.save();
            this.ctx.translate(x + 8 * this.scale, y + 4 * this.scale);
            this.ctx.scale(scaleFactor, scaleFactor);
            this.ctx.fillStyle = '#ffffff'; // Encoding cleanup: previous comment text was unreadable.
            const path = new Path2D(node.iconPath);
            this.ctx.fill(path);
            this.ctx.restore();
        } else if (node.icon) {
            this.ctx.fillStyle = '#ffffff';
            this.ctx.font = `${14 * this.scale}px sans-serif`;
            this.ctx.textAlign = 'left';
            this.ctx.textBaseline = 'middle';
            this.ctx.fillText(node.icon, x + 8 * this.scale, y + 12 * this.scale);
        }

        // 标题文字
        this.ctx.fillStyle = '#ffffff';
        this.ctx.font = `bold ${11 * this.scale}px sans-serif`;
        this.ctx.textAlign = 'left';
        this.ctx.textBaseline = 'middle';
        const titleX = (node.icon || node.iconPath) ? x + 28 * this.scale : x + 10 * this.scale;
        this.ctx.fillText(node.title, titleX, y + 12 * this.scale);

        // Encoding cleanup: previous comment text was unreadable.
        if (isForEach && ioMode) {
            const ioLabel = isSequential ? 'SEQ' : 'PAR';
            const labelColor = isSequential ? '#fa8c16' : '#13c2c2';
            this.ctx.fillStyle = labelColor;
            this.ctx.font = `bold ${9 * this.scale}px sans-serif`;
            this.ctx.textAlign = 'right';
            this.ctx.textBaseline = 'middle';
            this.ctx.fillText(ioLabel, x + w - 6 * this.scale, y + 12 * this.scale);

            let subNodeCount = 0;
            if (this._subGraphNodeCountCache.has(node)) {
                subNodeCount = this._subGraphNodeCountCache.get(node);
            } else {
                const subGraphParam = node.parameters?.find(p => p.name === 'SubGraph' || p.Name === 'SubGraph');
                if (subGraphParam && subGraphParam.value) {
                    try {
                        const subGraphData = typeof subGraphParam.value === 'string' ? JSON.parse(subGraphParam.value) : subGraphParam.value;
                        if (subGraphData && Array.isArray(subGraphData.nodes)) {
                            subNodeCount = subGraphData.nodes.length;
                        }
                    } catch (e) {
                        // Ignore malformed subgraph payloads.
                    }
                }
                this._subGraphNodeCountCache.set(node, subNodeCount);
            }
            this.ctx.fillStyle = palette.nodeMutedText;
            this.ctx.font = `${10 * this.scale}px sans-serif`;
            this.ctx.textAlign = 'center';
            this.ctx.textBaseline = 'bottom';
            this.ctx.fillText(`[${subNodeCount} nodes]`, x + w / 2, y + h - 8 * this.scale);
        }

        if (node.status) {
            const indicatorY = y + 40 * this.scale;
            this.drawStatusIndicator(x + w - 12 * this.scale, indicatorY, node.status);
        }

        // 绘制端口
        this.drawGlobalVariableBadges(node, x, y, w);
        this.drawPorts(node, x, y, w, h);

        // === Sprint 4 Task 4.3: 绘制安全标记 ===
        if (isCommunicationOp) {
            // Encoding cleanup: previous comment text was unreadable.
            this.ctx.fillStyle = '#f5222d';
            this.ctx.font = `bold ${14 * this.scale}px sans-serif`;
            this.ctx.textAlign = 'right';
            this.ctx.textBaseline = 'top';
            this.ctx.fillText('COM', x + w - 4 * this.scale, y + 2 * this.scale);
        }

        this.ctx.restore();
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    drawGlobalVariableBadges(node, x, y, w) {
        const operatorId = String(node.id || '').toLowerCase();
        if (!operatorId || !this.globalVariableSchema) {
            return;
        }

        const sourceCount = (this.globalVariableSchema.sourceBindings || [])
            .filter(binding => String(binding.operatorId || binding.OperatorId || '').toLowerCase() === operatorId)
            .length;
        const targetCount = (this.globalVariableSchema.targetBindings || [])
            .filter(binding => String(binding.operatorId || binding.OperatorId || '').toLowerCase() === operatorId)
            .length;
        const badges = [];
        if (sourceCount > 0) {
            badges.push({ text: `G^${sourceCount}`, color: '#13c2c2' });
        }
        if (targetCount > 0) {
            badges.push({ text: `Gv${targetCount}`, color: '#fa8c16' });
        }
        if (badges.length === 0) {
            return;
        }

        this.ctx.save();
        this.ctx.font = `bold ${9 * this.scale}px sans-serif`;
        this.ctx.textAlign = 'right';
        this.ctx.textBaseline = 'middle';

        let right = x + w - 6 * this.scale;
        const top = y + 30 * this.scale;
        for (const badge of badges) {
            const badgeWidth = Math.max(26 * this.scale, this.ctx.measureText(badge.text).width + 10 * this.scale);
            const badgeHeight = 16 * this.scale;
            const left = right - badgeWidth;
            this.ctx.fillStyle = this.themePalette?.nodeBadgeBackground || FLOW_CANVAS_THEME_DEFAULTS.nodeBadgeBackground;
            this.roundRect(left, top, badgeWidth, badgeHeight, 6 * this.scale);
            this.ctx.fill();
            this.ctx.strokeStyle = badge.color;
            this.ctx.lineWidth = Math.max(1, this.scale);
            this.ctx.stroke();
            this.ctx.fillStyle = badge.color;
            this.ctx.fillText(badge.text, right - 5 * this.scale, top + badgeHeight / 2);
            right = left - 4 * this.scale;
        }

        this.ctx.restore();
    }

    adjustColor(color, amount) {
        const hex = color.replace('#', '');
        const r = Math.max(0, Math.min(255, parseInt(hex.substr(0, 2), 16) + amount));
        const g = Math.max(0, Math.min(255, parseInt(hex.substr(2, 2), 16) + amount));
        const b = Math.max(0, Math.min(255, parseInt(hex.substr(4, 2), 16) + amount));
        return `rgb(${r}, ${g}, ${b})`;
    }

    /**
     * 绘制状态指示器
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     */
    drawStatusIndicator(x, y, status) {
        const radius = 6 * this.scale;

        this.ctx.beginPath();
        this.ctx.arc(x, y, radius, 0, Math.PI * 2);

        switch (status) {
            case 'running':
                this.ctx.fillStyle = '#1890ff';
                this.ctx.fill();
                // 绘制旋转的进度环
                this.ctx.beginPath();
                this.ctx.arc(x, y, radius + 2 * this.scale, 0, Math.PI * 2);
                this.ctx.strokeStyle = 'rgba(24, 144, 255, 0.5)';
                this.ctx.lineWidth = 2 * this.scale;
                this.ctx.stroke();
                break;
            case 'success':
                this.ctx.fillStyle = '#52c41a';
                this.ctx.fill();
                // 绘制对勾
                this.ctx.strokeStyle = '#ffffff';
                this.ctx.lineWidth = 2 * this.scale;
                this.ctx.beginPath();
                this.ctx.moveTo(x - 3 * this.scale, y);
                this.ctx.lineTo(x - 1 * this.scale, y + 2 * this.scale);
                this.ctx.lineTo(x + 3 * this.scale, y - 2 * this.scale);
                this.ctx.stroke();
                break;
            case 'error':
                this.ctx.fillStyle = '#f5222d';
                this.ctx.fill();
                // 绘制X
                this.ctx.strokeStyle = '#ffffff';
                this.ctx.lineWidth = 2 * this.scale;
                this.ctx.beginPath();
                this.ctx.moveTo(x - 2 * this.scale, y - 2 * this.scale);
                this.ctx.lineTo(x + 2 * this.scale, y + 2 * this.scale);
                this.ctx.moveTo(x + 2 * this.scale, y - 2 * this.scale);
                this.ctx.lineTo(x - 2 * this.scale, y + 2 * this.scale);
                this.ctx.stroke();
                break;
        }
    }

    /**
     * 绘制端口
     */
    drawPorts(node, x, y, w, h) {
        const portRadius = 5 * this.scale;
        
        // Encoding cleanup: previous comment text was unreadable.
        node.inputs.forEach((input, index) => {
            const portY = this.getPortYInScreen(y, h, index, node.inputs.length);
            const color = getPortTypeColor(this.readPortType(input));
            
            this.ctx.beginPath();
            this.ctx.arc(x, portY, portRadius, 0, Math.PI * 2);
            this.ctx.fillStyle = color;
            this.ctx.fill();
            this.ctx.strokeStyle = this.themePalette?.portOutline || FLOW_CANVAS_THEME_DEFAULTS.portOutline;
            this.ctx.lineWidth = 1;
            this.ctx.stroke();

            // 绘制输入端口标签。
            if (this.scale > 0.8) {
                this.ctx.fillStyle = this.themePalette?.nodeMutedText || FLOW_CANVAS_THEME_DEFAULTS.nodeMutedText;
                this.ctx.font = `${8 * this.scale}px sans-serif`;
                this.ctx.textAlign = 'left';
                const typeName = typeof input.type === 'string' ? input.type : 'Any';
                this.ctx.fillText(input.name || typeName, x + 8 * this.scale, portY + 3 * this.scale);
            }
        });
        
        // 绘制输出端口。
        node.outputs.forEach((output, index) => {
            const portY = this.getPortYInScreen(y, h, index, node.outputs.length);
            const color = getPortTypeColor(this.readPortType(output));

            this.ctx.beginPath();
            this.ctx.arc(x + w, portY, portRadius, 0, Math.PI * 2);
            this.ctx.fillStyle = color;
            this.ctx.fill();
            this.ctx.strokeStyle = this.themePalette?.portOutline || FLOW_CANVAS_THEME_DEFAULTS.portOutline;
            this.ctx.lineWidth = 1;
            this.ctx.stroke();

            // 绘制输出端口标签。
            if (this.scale > 0.8) {
                this.ctx.fillStyle = this.themePalette?.nodeMutedText || FLOW_CANVAS_THEME_DEFAULTS.nodeMutedText;
                this.ctx.font = `${8 * this.scale}px sans-serif`;
                this.ctx.textAlign = 'right';
                const typeName = typeof output.type === 'string' ? output.type : 'Any';
                this.ctx.fillText(output.name || typeName, x + w - 8 * this.scale, portY + 3 * this.scale);
            }
        });
    }

    getRequiredNodeHeight(inputs = [], outputs = []) {
        const portCount = Math.max(inputs?.length || 0, outputs?.length || 0, 1);
        return Math.max(
            NODE_MIN_HEIGHT,
            NODE_HEADER_HEIGHT + NODE_PORT_TOP_PADDING + NODE_PORT_BOTTOM_PADDING + portCount * NODE_PORT_ROW_HEIGHT
        );
    }

    getPortYInScreen(nodeScreenY, nodeScreenHeight, portIndex, portCount) {
        if (portCount <= 0) {
            return nodeScreenY + nodeScreenHeight / 2;
        }
        const top = nodeScreenY + (NODE_HEADER_HEIGHT + NODE_PORT_TOP_PADDING) * this.scale;
        const bottom = nodeScreenY + nodeScreenHeight - NODE_PORT_BOTTOM_PADDING * this.scale;
        if (portCount === 1) {
            return (top + bottom) / 2;
        }
        return top + ((bottom - top) * portIndex) / (portCount - 1);
    }

    /**
     * Get the absolute position of a node port on the canvas.
     * @param {string} nodeId - Node id
     * @param {number} portIndex - Port index
     * @param {boolean} isOutput - Whether the port is an output port
     * @returns {{x: number, y: number}} Port position
     */
    getPortPosition(nodeId, portIndex, isOutput) {
        const node = this.nodes.get(nodeId);
        if (!node) return null;

        const x = (node.x - this.offset.x) * this.scale;
        const y = (node.y - this.offset.y) * this.scale;
        const w = node.width * this.scale;
        const h = node.height * this.scale;

        const portsCount = isOutput ? node.outputs.length : node.inputs.length;
        const portY = this.getPortYInScreen(y, h, portIndex, portsCount);

        if (isOutput) {
            return { x: x + w, y: portY };
        } else {
            return { x: x, y: portY };
        }
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * @returns {{nodeId: string, portIndex: number, isOutput: boolean}|null}
     */
    getPortAt(x, y) {
        const screenX = (x - this.offset.x) * this.scale;
        const screenY = (y - this.offset.y) * this.scale;
        const hitRadiusSq = (PORT_HIT_RADIUS_PX * this.scale) ** 2; // Encoding cleanup: previous comment text was unreadable.

        for (const [nodeId, node] of this.nodes) {
            const nodeScreenX = (node.x - this.offset.x) * this.scale;
            const nodeScreenY = (node.y - this.offset.y) * this.scale;
            const w = node.width * this.scale;
            const h = node.height * this.scale;

            // Encoding cleanup: previous comment text was unreadable.
            for (let i = 0; i < node.inputs.length; i++) {
                const portY = this.getPortYInScreen(nodeScreenY, h, i, node.inputs.length);
                const dx = screenX - nodeScreenX;
                const dy = screenY - portY;
                if (dx * dx + dy * dy < hitRadiusSq) {
                    return { nodeId, portIndex: i, isOutput: false };
                }
            }

            // Encoding cleanup: previous comment text was unreadable.
            for (let i = 0; i < node.outputs.length; i++) {
                const portY = this.getPortYInScreen(nodeScreenY, h, i, node.outputs.length);
                const dx = screenX - (nodeScreenX + w);
                const dy = screenY - portY;
                if (dx * dx + dy * dy < hitRadiusSq) {
                    return { nodeId, portIndex: i, isOutput: true };
                }
            }
        }

        return null;
    }

    /**
     * Find port metadata for a node port.
     * @param {string} nodeId - Node id
     * @param {number} portIndex - Port index
     * @param {boolean} isOutput - Whether the port is an output port
     * @returns {Object|null} Port metadata
     */
    getConnectionAtPort(nodeId, portIndex, isOutput) {
        if (isOutput) {
            const set = this._connectionsByOutputPort.get(portKey(nodeId, portIndex));
            if (!set || set.size === 0) return null;
            // Encoding cleanup: previous comment text was unreadable.
            return set.values().next().value;
        }
        return this._connectionByInputPort.get(portKey(nodeId, portIndex)) || null;
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    getConnectionsAtPort(nodeId, portIndex, isOutput) {
        if (isOutput) {
            const set = this._connectionsByOutputPort.get(portKey(nodeId, portIndex));
            return set ? Array.from(set) : [];
        }
        const conn = this._connectionByInputPort.get(portKey(nodeId, portIndex));
        return conn ? [conn] : [];
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * @param {string} nodeId - 起始节点ID
     * Encoding cleanup: previous comment text was unreadable.
     */
    startConnection(nodeId, portIndex) {
        this.isConnecting = true;
        this.connectingFrom = { nodeId, portIndex, isOutput: true };
        this.canvas.style.cursor = 'crosshair';
        this.invalidate();
        if (flowDebugEnabled()) {
            console.log('[FlowCanvas] Start connection:', nodeId, 'port', portIndex);
        }
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * @param {string} nodeId - 目标节点ID
     * Encoding cleanup: previous comment text was unreadable.
     */
    finishConnection(nodeId, portIndex) {
        if (!this.isConnecting || !this.connectingFrom) return;

        const sourceNode = this.nodes.get(this.connectingFrom.nodeId);
        const targetNode = this.nodes.get(nodeId);

        if (!sourceNode || !targetNode || !sourceNode.outputs[this.connectingFrom.portIndex]) {
            this.cancelConnection();
            return;
        }

        const sourcePort = sourceNode.outputs[this.connectingFrom.portIndex];
        const targetPort = targetNode.inputs[portIndex];

        const sourceType = this.readPortType(sourcePort);
        const targetType = this.readPortType(targetPort);
        if (!this.checkTypeCompatibility(sourceType, targetType)) {
            const incompatibilityMessage = this.getPortTypeMismatchMessage(sourceType, targetType);
            console.warn(incompatibilityMessage);
            if (window.showToast) window.showToast(incompatibilityMessage, 'warning');
            this.cancelConnection();
            return;
        }

        if (this.connectingFrom.nodeId === nodeId) {
            console.warn('[FlowCanvas] 不能连接到同一节点。');
            if (window.showToast) window.showToast('不能连接到同一节点', 'warning');
            this.cancelConnection();
            return;
        }

        const existingConn = this.connections.find(conn =>
            conn.source === this.connectingFrom.nodeId &&
            conn.sourcePort === this.connectingFrom.portIndex &&
            conn.target === nodeId &&
            conn.targetPort === portIndex
        );

        if (existingConn) {
            console.warn('[FlowCanvas] 连接已存在。');
            if (window.showToast) window.showToast('连接已存在', 'warning');
            this.cancelConnection();
            return;
        }

        const targetPortOccupied = this.connections.find(conn =>
            conn.target === nodeId &&
            conn.targetPort === portIndex
        );

        if (targetPortOccupied) {
            console.warn('[FlowCanvas] 目标输入端口已被占用。');
            if (window.showToast) window.showToast('目标输入端口已被占用', 'warning');
            this.cancelConnection();
            return;
        }

        if (this.wouldCreateCycle(this.connectingFrom.nodeId, nodeId)) {
            console.warn('[FlowCanvas] 该连接会形成环路。');
            if (window.showToast) window.showToast('该连接会形成环路', 'warning');
            this.cancelConnection();
            return;
        }

        const connection = this.addConnection(
            this.connectingFrom.nodeId,
            this.connectingFrom.portIndex,
            nodeId,
            portIndex
        );
        if (!connection) {
            this.cancelConnection();
            return;
        }

        if (flowDebugEnabled()) {
            console.log('[FlowCanvas] Connection created:', connection);
        }

        if (this.onConnectionCreated) {
            this.onConnectionCreated(connection);
        }

        this.cancelConnection();
    }

    cancelConnection() {
        this.isConnecting = false;
        this.connectingFrom = null;
        this.setHoveredPort(null);
        this.canvas.style.cursor = 'default';
        this.invalidate(); // 刷新以清除连线预览
    }

    /**
     * 检查端口类型兼容性。
     */
    checkTypeCompatibility(sourceType, targetType) {
        return arePortTypesCompatible(sourceType, targetType);
    }

    readPortType(port) {
        return port?.type ?? port?.dataType ?? port?.DataType ?? port?.Type ?? 'Any';
    }

    getPortTypeMismatchMessage(sourceType, targetType) {
        return getPortTypeMismatchMessage(sourceType, targetType);
    }

    formatPortTypeForMessage(type) {
        return formatPortTypeForMessage(type);
    }

    /**
     * 高亮当前连线可连接的端口。
     */
    highlightCompatiblePorts() {
        if (!this.isConnecting || !this.connectingFrom) return;
        
        const sourceNode = this.nodes.get(this.connectingFrom.nodeId);
        if (!sourceNode) return;
        
        const sourcePort = this.connectingFrom.isOutput ? 
            sourceNode.outputs[this.connectingFrom.portIndex] : 
            sourceNode.inputs[this.connectingFrom.portIndex];
        
        if (!sourcePort) return;

        for (const [nodeId, node] of this.nodes) {
            if (nodeId === this.connectingFrom.nodeId) continue;

            const targetPorts = this.connectingFrom.isOutput ? node.inputs : node.outputs;
            targetPorts.forEach((port, index) => {
                if (this.checkTypeCompatibility(this.readPortType(sourcePort), this.readPortType(port))) {
                    const pos = this.getPortPosition(nodeId, index, !this.connectingFrom.isOutput);
                    if (pos) {
                        this.ctx.beginPath();
                        this.ctx.arc(pos.x, pos.y, 10 * this.scale, 0, Math.PI * 2);
                        this.ctx.fillStyle = 'rgba(82, 196, 26, 0.2)';
                        this.ctx.fill();
                        this.ctx.strokeStyle = '#52c41a';
                        this.ctx.setLineDash([2, 2]);
                        this.ctx.stroke();
                        this.ctx.setLineDash([]);
                    }
                }
            });
        }
    }

    /**
     * 删除指定连线。
     * @param {string} connectionId - 连接ID
     */
    removeConnection(connectionId) {
        const connection = this._connectionById.get(connectionId);
        if (!connection) {
            return false;
        }
        this.connections = this.connections.filter(conn => conn.id !== connectionId);
        this._unindexConnection(connection);
        const selectionChanged = this.selectedConnection && this.selectedConnection.id === connectionId;
        if (this.selectedConnection && this.selectedConnection.id === connectionId) {
            this.selectedConnection = null;
        }
        this.invalidate();
        this.markFlowStructureChanged('removeConnection');
        if (selectionChanged) {
            this.markSelectionChanged('removeConnection');
        }
        return true;
    }

    /**
     * 绘制拖拽过程中的临时连线。
     */
    drawTempConnection() {
        if (!this.isConnecting || !this.connectingFrom) return;

        const startPos = this.getPortPosition(
            this.connectingFrom.nodeId,
            this.connectingFrom.portIndex,
            this.connectingFrom.isOutput
        );

        if (!startPos) return;

        // Encoding cleanup: previous comment text was unreadable.
        this.highlightCompatiblePorts();

        const endX = (this.mousePosition.x - this.offset.x) * this.scale;
        const endY = (this.mousePosition.y - this.offset.y) * this.scale;

        // 绘制虚线
        this.ctx.beginPath();
        this.ctx.moveTo(startPos.x, startPos.y);

        const controlPoint1X = startPos.x + (endX - startPos.x) / 2;
        const controlPoint2X = startPos.x + (endX - startPos.x) / 2;

        this.ctx.bezierCurveTo(
            controlPoint1X, startPos.y,
            controlPoint2X, endY,
            endX, endY
        );

        this.ctx.strokeStyle = this.hoveredPort?.compatibility === 'incompatible'
            ? '#ef4444'
            : (this.themePalette?.guide || FLOW_CANVAS_THEME_DEFAULTS.guide);
        this.ctx.lineWidth = 2 * this.scale;
        this.ctx.setLineDash([5 * this.scale, 5 * this.scale]);
        this.ctx.stroke();
        this.ctx.setLineDash([]);
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    drawConnection(connection, dt) {
        const sourceNode = this.nodes.get(connection.source);
        const targetNode = this.nodes.get(connection.target);

        if (!sourceNode || !targetNode) return;

        // Encoding cleanup: previous comment text was unreadable.
        const start = this.getPortPosition(connection.source, connection.sourcePort, true);
        const end = this.getPortPosition(connection.target, connection.targetPort, false);

        if (!start || !end) return;

        const controlPoint1X = start.x + (end.x - start.x) / 2;
        const controlPoint2X = start.x + (end.x - start.x) / 2;

        const isSelected = this.selectedConnection &&
            this.selectedConnection.source === connection.source &&
            this.selectedConnection.target === connection.target;

        this.ctx.save();
        this.ctx.beginPath();
        this.ctx.moveTo(start.x, start.y);
        this.ctx.bezierCurveTo(
            controlPoint1X, start.y,
            controlPoint2X, end.y,
            end.x, end.y
        );

        // Encoding cleanup: previous comment text was unreadable.
        if (connection.status === 'active') {
            this.ctx.strokeStyle = '#34c759';
            this.ctx.shadowColor = 'rgba(46, 204, 113, 0.5)';
            this.ctx.shadowBlur = 10;
        } else if (connection.status === 'error') {
            this.ctx.strokeStyle = '#e74c3c';
            this.ctx.shadowColor = 'rgba(231, 76, 60, 0.5)';
            this.ctx.shadowBlur = 10;
        } else {
            this.ctx.strokeStyle = isSelected
                ? (this.themePalette?.connectionSelected || FLOW_CANVAS_THEME_DEFAULTS.connectionSelected)
                : (this.themePalette?.connection || FLOW_CANVAS_THEME_DEFAULTS.connection);
            this.ctx.shadowColor = isSelected ? 'rgba(255, 255, 255, 0.4)' : 'transparent';
            this.ctx.shadowBlur = isSelected ? 8 : 0;
        }

        this.ctx.lineWidth = isSelected ? 3 * this.scale : 2 * this.scale;
        this.ctx.stroke();
        this.ctx.restore();

        // 连线流动时绘制粒子动画。
        if (connection.status === 'active' || connection.status === 'flowing') {
            this.drawFlowParticles(start.x, start.y, controlPoint1X, start.y,
                                   controlPoint2X, end.y, end.x, end.y, connection, dt);
        }
    }
    
    /**
     * 绘制连线粒子动画。
     * 使用离屏精灵减少每帧渐变绘制开销。
     */
    drawFlowParticles(startX, startY, cp1x, cp1y, cp2x, cp2y, endX, endY, connection, dt) {
        // 初始化粒子。
        if (!connection.particles) {
            connection.particles = [];
            for (let i = 0; i < 5; i++) {
                connection.particles.push({
                    t: i / 5,
                    speed: 0.005 + Math.random() * 0.003
                });
            }
        }

        // 缓存粒子精灵。
        const spriteRadius = 6 * this.scale;
        const spriteKey = `particle_${spriteRadius.toFixed(1)}`;
        if (!this._particleSprite || this._particleSprite.key !== spriteKey) {
            const size = Math.ceil(spriteRadius * 2);
            const off = document.createElement('canvas');
            off.width = size;
            off.height = size;
            const octx = off.getContext('2d');
            const grad = octx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, spriteRadius);
            grad.addColorStop(0, 'rgba(255, 255, 255, 1)');
            grad.addColorStop(0.5, 'rgba(52, 152, 219, 0.8)');
            grad.addColorStop(1, 'rgba(52, 152, 219, 0)');
            octx.fillStyle = grad;
            octx.fillRect(0, 0, size, size);
            this._particleSprite = { key: spriteKey, canvas: off, size };
        }

        const sprite = this._particleSprite;
        const timeScale = dt / 16; // 按 60fps 基准缩放速度

        connection.particles.forEach(particle => {
            // 更新粒子位置。
            particle.t += particle.speed * timeScale;
            if (particle.t > 1) particle.t = 0;

            // 计算三次贝塞尔曲线上的点。
            const t = particle.t;
            const mt = 1 - t;
            const x = mt * mt * mt * startX +
                     3 * mt * mt * t * cp1x +
                     3 * mt * t * t * cp2x +
                     t * t * t * endX;
            const y = mt * mt * mt * startY +
                     3 * mt * mt * t * cp1y +
                     3 * mt * t * t * cp2y +
                     t * t * t * endY;

            // 绘制粒子精灵。
            this.ctx.drawImage(sprite.canvas, x - sprite.size / 2, y - sprite.size / 2, sprite.size, sprite.size);
        });
    }

    /**
     * 绘制圆角矩形路径。
     * @param {number} x
     * @param {number} y
     * @param {number} w
     * @param {number} h
     * r 可为统一半径或四角半径对象。
     */
    roundRect(x, y, w, h, r) {
        let tl;
        let tr;
        let br;
        let bl;
        if (typeof r === 'number') {
            tl = tr = br = bl = r;
        } else if (r && typeof r === 'object') {
            tl = r.tl || 0;
            tr = r.tr || 0;
            br = r.br || 0;
            bl = r.bl || 0;
        } else {
            tl = tr = br = bl = 0;
        }

        // 限制半径，防止 w/h 较小时出现交叉。
        const maxR = Math.min(w, h) / 2;
        tl = Math.min(tl, maxR);
        tr = Math.min(tr, maxR);
        br = Math.min(br, maxR);
        bl = Math.min(bl, maxR);

        this.ctx.beginPath();
        this.ctx.moveTo(x + tl, y);
        this.ctx.lineTo(x + w - tr, y);
        this.ctx.quadraticCurveTo(x + w, y, x + w, y + tr);
        this.ctx.lineTo(x + w, y + h - br);
        this.ctx.quadraticCurveTo(x + w, y + h, x + w - br, y + h);
        this.ctx.lineTo(x + bl, y + h);
        this.ctx.quadraticCurveTo(x, y + h, x, y + h - bl);
        this.ctx.lineTo(x, y + tl);
        this.ctx.quadraticCurveTo(x, y, x + tl, y);
        this.ctx.closePath();
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     */
    invalidate() {
        this._dirty = true;
        this._scheduleFrame();
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     */
    render() {
        this.invalidate();
    }

    _scheduleFrame() {
        if (this._isPaused) {
            return;
        }
        if (this._animationFrameId !== null) {
            return;
        }
        this._animationFrameId = requestAnimationFrame(this._drawFrameBound);
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    _hasAnimation() {
        if (this.isConnecting) return true;
        for (const conn of this.connections) {
            if (conn.status === 'active' || conn.status === 'flowing') {
                return true;
            }
        }
        for (const node of this.nodes.values()) {
            if (node.status === 'running') {
                return true;
            }
        }
        return false;
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * @private
     */
    _isNodeInViewport(node) {
        const vw = this._logicalWidth / this.scale;
        const vh = this._logicalHeight / this.scale;
        const vx = this.offset.x;
        const vy = this.offset.y;
        // Encoding cleanup: previous comment text was unreadable.
        return node.x + node.width >= vx - 100 && node.x <= vx + vw + 100 &&
               node.y + node.height >= vy - 100 && node.y <= vy + vh + 100;
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * @private
     */
    _isConnectionVisible(conn) {
        const src = this.nodes.get(conn.source);
        const tgt = this.nodes.get(conn.target);
        return (src && this._isNodeInViewport(src)) || (tgt && this._isNodeInViewport(tgt));
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    _drawFrame(now) {
        this._animationFrameId = null;

        if (this._isPaused) {
            return;
        }

        const timestamp = typeof now === 'number' ? now : performance.now();
        const dt = this._lastFrameTime > 0 ? Math.max(0, timestamp - this._lastFrameTime) : 16;
        this._lastFrameTime = timestamp;

        // Encoding cleanup: previous comment text was unreadable.
        this.ctx.clearRect(0, 0, this._logicalWidth, this._logicalHeight);

        // 绘制网格
        this.drawGrid();

        // 绘制连接线（dt 用于粒子动画）
        for (const conn of this.connections) {
            if (this._isConnectionVisible(conn)) {
                this.drawConnection(conn, dt);
            }
        }

        // 绘制临时连接线
        if (this.isConnecting) {
            this.drawTempConnection();
        }

        // 绘制可见节点
        for (const node of this.nodes.values()) {
            if (this._isNodeInViewport(node)) {
                this.drawNode(node);
            }
        }

        // Encoding cleanup: previous comment text was unreadable.
        if (this.hoveredPort) {
            this.drawPortHighlight(this.hoveredPort);
        }

        // Encoding cleanup: previous comment text was unreadable.
        this.drawMinimap();

        this._dirty = false;

        // Encoding cleanup: previous comment text was unreadable.
        if (this._hasAnimation()) {
            this._scheduleFrame();
        } else {
            this._lastFrameTime = 0;
        }
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    normalizePortType(type) {
        return normalizeFlowPortType(type);
    }

    normalizeOperatorType(type) {
        if (!type) return type;
        return LEGACY_OPERATOR_TYPE_ALIASES[type] || type;
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     */
    serialize() {
        // Encoding cleanup: previous comment text was unreadable.
        // Encoding cleanup: previous comment text was unreadable.
        for (const node of this.nodes.values()) {
            if (node.inputs) {
                for (const port of node.inputs) {
                    if (!port.id) {
                        port.id = this.generateUUID();
                    }
                }
            }
            if (node.outputs) {
                for (const port of node.outputs) {
                    if (!port.id) {
                        port.id = this.generateUUID();
                    }
                }
            }
        }

        // Encoding cleanup: previous comment text was unreadable.
        const operators = Array.from(this.nodes.values()).map(node => {
            const metadata = node.metadata && typeof node.metadata === 'object'
                ? { ...node.metadata }
                : null;
            return {
                id: node.id,
                name: node.title,
                type: this.normalizeOperatorType(node.type),
                x: node.x,
                y: node.y,
                ...(metadata && Object.keys(metadata).length > 0 ? { metadata } : {}),
                inputPorts: (node.inputs || []).map(p => ({
                    id: p.id || p.Id || this.generateUUID(), // Encoding cleanup: previous comment text was unreadable.
                    name: p.name,
                    displayName: p.displayName || p.DisplayName || p.name,
                    description: p.description || p.Description || '',
                    dataType: this.normalizePortType(p.type), // PortDataType enum
                    direction: 0, // Input
                    isRequired: Boolean(p.isRequired ?? p.IsRequired ?? false)
                })),
                outputPorts: (node.outputs || []).map(p => ({
                    id: p.id || p.Id || this.generateUUID(), // Encoding cleanup: previous comment text was unreadable.
                    name: p.name,
                    displayName: p.displayName || p.DisplayName || p.name,
                    description: p.description || p.Description || '',
                    dataType: this.normalizePortType(p.type),
                    direction: 1, // Output
                    isRequired: false
                })),
                parameters: (node.parameters || []).map(p => ({
                    id: p.id || p.Id || this.generateUUID(),
                    name: p.name,
                    displayName: p.displayName || p.DisplayName || p.name,
                    description: p.description || p.Description || null,
                    value: p.value !== undefined ? p.value : p.defaultValue,
                    defaultValue: p.defaultValue ?? p.DefaultValue ?? null,
                    minValue: p.minValue ?? p.MinValue ?? p.min ?? p.Min ?? null,
                    maxValue: p.maxValue ?? p.MaxValue ?? p.max ?? p.Max ?? null,
                    dataType: p.dataType || p.DataType || p.type || p.Type,
                    isRequired: Boolean(p.isRequired ?? p.IsRequired ?? false),
                    options: p.options || p.Options || null
                })),
                isEnabled: node.disabled !== true
            };
        });

        // Encoding cleanup: previous comment text was unreadable.
        const debug = flowDebugEnabled();
        if (debug) {
            console.log('[FlowCanvas serialize] === START ===');
            console.log('[FlowCanvas serialize] Raw connections count:', this.connections.length);
            console.log('[FlowCanvas serialize] Nodes in canvas:', Array.from(this.nodes.keys()));
        }

        const connections = this.connections
            .filter(conn => {
                // Encoding cleanup: previous comment text was unreadable.
                const isValidSource = conn.source && conn.source !== '00000000-0000-0000-0000-000000000000';
                const isValidTarget = conn.target && conn.target !== '00000000-0000-0000-0000-000000000000';
                if (!isValidSource || !isValidTarget) {
                    console.warn(`[FlowCanvas serialize] Skipping invalid connection: source=${conn.source}, target=${conn.target}`);
                }
                return isValidSource && isValidTarget;
            })
            .map(conn => {
                const sourceNode = this.nodes.get(conn.source);
                const targetNode = this.nodes.get(conn.target);

                // Encoding cleanup: previous comment text was unreadable.
                let sourcePortId = null;
                let targetPortId = null;

                if (sourceNode && conn.sourcePort >= 0 && conn.sourcePort < sourceNode.outputs.length) {
                    const port = sourceNode.outputs[conn.sourcePort];
                    sourcePortId = port?.id || port?.Id;
                    if (!sourcePortId) {
                        port.id = this.generateUUID();
                        sourcePortId = port.id;
                    }
                } else if (debug) {
                    console.warn(`[FlowCanvas serialize] Invalid source port index ${conn.sourcePort}, output count ${sourceNode?.outputs?.length || 0}`);
                }

                if (targetNode && conn.targetPort >= 0 && conn.targetPort < targetNode.inputs.length) {
                    const port = targetNode.inputs[conn.targetPort];
                    targetPortId = port?.id || port?.Id;
                    if (!targetPortId) {
                        port.id = this.generateUUID();
                        targetPortId = port.id;
                    }
                } else if (debug) {
                    console.warn(`[FlowCanvas serialize] Invalid target port index ${conn.targetPort}, input count ${targetNode?.inputs?.length || 0}`);
                }

                // Encoding cleanup: previous comment text was unreadable.
                if (!sourcePortId || !targetPortId) {
                    if (debug) {
                        console.warn(`[FlowCanvas serialize] Skipping connection with missing port ids: sourcePortId=${sourcePortId}, targetPortId=${targetPortId}`);
                    }
                    return null;
                }

                return {
                    id: conn.id,
                    sourceOperatorId: conn.source,
                    sourcePortId: sourcePortId,
                    targetOperatorId: conn.target,
                    targetPortId: targetPortId
                };
            })
            .filter(conn => conn !== null); // 过滤掉无效的连接

        // Encoding cleanup: previous comment text was unreadable.
        const result = {
            operators: operators,
            connections: connections,
            decisionConfiguration: this.decisionConfiguration
                ? JSON.parse(JSON.stringify(this.decisionConfiguration))
                : null
        };

        if (debug) {
            console.log('[FlowCanvas serialize] Operators count:', operators.length);
            console.log('[FlowCanvas serialize] Connections count:', connections.length);
            console.log('[FlowCanvas serialize] === END ===');
        }

        return result;
    }

    /**
     * 反序列化流程数据到画布。
     */
    deserialize(data) {
        if (!data) return;
        this.clear(true);

        // 兼容项目包装结构和直接流程结构。
        const flowData = data.project?.flow || data.flow || data;
        this.decisionConfiguration = flowData.decisionConfiguration
            ?? flowData.DecisionConfiguration
            ?? null;

        // 兼容后端 DTO 和前端节点命名。
        const operators = flowData.operators || flowData.Operators || flowData.nodes || [];
        const connections = flowData.connections || flowData.Connections || [];

        if (flowDebugEnabled()) {
            console.log('[FlowCanvas] Deserialize start: operators', operators.length, 'connections', connections.length);
        }

        if (operators) {
            operators.forEach(op => {
                // 兼容后端 DTO (PascalCase) 和前端数据 (camelCase)。
                const id = op.id ?? op.Id;
                const type = this.normalizeOperatorType(op.type ?? op.Type);
                const title = op.name ?? op.Name ?? op.title ?? type;

                // 归一化端口数据。
                const normalizePort = (port, direction) => {
                    const name = port.name || port.Name;
                    const declaredType = port.type || port.Type || port.dataType || port.DataType || 0;
                    const canonicalPortType = canonicalizeOperatorPortType(type, name, direction, declaredType);
                    return {
                        id: port.id || port.Id || this.generateUUID(),
                        name,
                        displayName: port.displayName || port.DisplayName || name,
                        description: port.description || port.Description || '',
                        type: canonicalPortType,
                        dataType: canonicalPortType,
                        isRequired: Boolean(port.isRequired ?? port.IsRequired ?? false)
                    };
                };

                const inputs = (op.inputPorts || op.InputPorts || op.inputs || []).map(port => normalizePort(port, 'input'));
                const outputs = (op.outputPorts || op.OutputPorts || op.outputs || []).map(port => normalizePort(port, 'output'));

                const node = {
                    id: id,
                    type: type,
                    x: op.x ?? op.X ?? 0,
                    y: op.y ?? op.Y ?? 0,
                    width: op.width ?? op.Width ?? NODE_DEFAULT_WIDTH,
                    height: op.height ?? op.Height ?? NODE_MIN_HEIGHT,
                    title: title,
                    inputs: inputs,
                    outputs: outputs,
                    parameters: op.parameters || op.Parameters || [],
                    metadata: op.metadata || op.Metadata || undefined,
                    disabled: (op.isEnabled ?? op.IsEnabled) === false,
                    color: '#1890ff' // Default
                };
                node.height = Math.max(
                    this.getRequiredNodeHeight(node.inputs, node.outputs),
                    Number(node.height) || NODE_MIN_HEIGHT
                );

                // Restore color logic based on type
                if (node.type === 'ImageAcquisition') node.color = '#52c41a';
                if (node.type === 'ResultOutput') node.color = '#595959';

                this.nodes.set(node.id, node);
            });
        }

        if (connections) {
            this.connections = connections.map(conn => {
                // 兼容后端 DTO (PascalCase) 和前端数据 (camelCase)。
                const id = conn.id || conn.Id;
                const sourceId = conn.sourceOperatorId || conn.SourceOperatorId || conn.source;
                const targetId = conn.targetOperatorId || conn.TargetOperatorId || conn.target;

                const sourcePortId = conn.sourcePortId || conn.SourcePortId;
                const targetPortId = conn.targetPortId || conn.TargetPortId;

                const sourceNode = this.nodes.get(sourceId);
                const targetNode = this.nodes.get(targetId);

                let sourcePortIndex = conn.sourcePort ?? 0;
                let targetPortIndex = conn.targetPort ?? 0;

                // 显式 Port ID 不允许静默退回索引，否则多端口连接会恢复到错误语义。
                if (sourcePortId) {
                    if (!sourceNode || !Array.isArray(sourceNode.outputs)) {
                        throw new Error(`[FlowCanvas] Cannot restore connection ${id}: source operator ${sourceId} is missing.`);
                    }
                    const idx = sourceNode.outputs.findIndex(port => (port.id === sourcePortId) || (port.Id === sourcePortId));
                    if (idx === -1) {
                        throw new Error(`[FlowCanvas] Cannot restore connection ${id}: source port ${sourcePortId} was not found on operator ${sourceId}.`);
                    }
                    sourcePortIndex = idx;
                }

                if (targetPortId) {
                    if (!targetNode || !Array.isArray(targetNode.inputs)) {
                        throw new Error(`[FlowCanvas] Cannot restore connection ${id}: target operator ${targetId} is missing.`);
                    }
                    const idx = targetNode.inputs.findIndex(port => (port.id === targetPortId) || (port.Id === targetPortId));
                    if (idx === -1) {
                        throw new Error(`[FlowCanvas] Cannot restore connection ${id}: target port ${targetPortId} was not found on operator ${targetId}.`);
                    }
                    targetPortIndex = idx;
                }

                return {
                    id: id,
                    source: sourceId,
                    sourcePort: sourcePortIndex,
                    target: targetId,
                    targetPort: targetPortIndex
                };
            }).filter(conn => {
                // 跳过无效连接。
                const isValidSource = conn.source && conn.source !== '00000000-0000-0000-0000-000000000000';
                const isValidTarget = conn.target && conn.target !== '00000000-0000-0000-0000-000000000000';
                if (!isValidSource || !isValidTarget) {
                    console.warn('[FlowCanvas] Skipping invalid connection:', conn);
                }
                return isValidSource && isValidTarget;
            });

            // 反序列化后必须重建连线索引，否则后续 O(1) 查询失效。
            this._rebuildConnectionIndex();
        }

        this.invalidate();
        this.markFlowStructureChanged('deserialize');
    }

    /**
     * 绘制端口高亮效果。
     * @param {{nodeId: string, portIndex: number, isOutput: boolean, hasConnection: boolean}} port
     */
    drawPortHighlight(port) {
        const pos = this.getPortPosition(port.nodeId, port.portIndex, port.isOutput);
        if (!pos) return;
        const definition = this.getPortDefinition(port);
        const isIncompatible = port.compatibility === 'incompatible';
        const isCompatible = port.compatibility === 'compatible';
        const highlightColor = isIncompatible
            ? '#ef4444'
            : (isCompatible ? '#22c55e' : getPortTypeColor(this.readPortType(definition?.port)));

        // 外圈描边。
        this.ctx.beginPath();
        this.ctx.arc(pos.x, pos.y, 8 * this.scale, 0, Math.PI * 2);
        this.ctx.strokeStyle = highlightColor;
        this.ctx.lineWidth = 2 * this.scale;
        this.ctx.stroke();

        // 半透明填充。
        this.ctx.beginPath();
        this.ctx.arc(pos.x, pos.y, 12 * this.scale, 0, Math.PI * 2);
        this.ctx.fillStyle = isIncompatible
            ? 'rgba(239, 68, 68, 0.24)'
            : (isCompatible ? 'rgba(34, 197, 94, 0.2)' : 'rgba(148, 163, 184, 0.18)');
        this.ctx.fill();

        // 已连接端口使用红色虚线环提示可断开。
        if (port.hasConnection) {
            this.ctx.beginPath();
            this.ctx.arc(pos.x, pos.y, 14 * this.scale, 0, Math.PI * 2);
            this.ctx.strokeStyle = 'rgba(231, 76, 60, 0.6)';
            this.ctx.lineWidth = 2 * this.scale;
            this.ctx.setLineDash([4 * this.scale, 2 * this.scale]);
            this.ctx.stroke();
            this.ctx.setLineDash([]);
        }
    }

    /**
     * 处理鼠标按下事件。
     */
    handleMouseDown(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        // 记录当前鼠标位置。
        this.mousePosition = { x, y };

        // 优先处理端口点击。
        const port = this.getPortAt(x, y);
        if (port) {
            if (port.isOutput) {
                // 点击输出端口时，先断开已有输出连线。
                const existingConns = this.getConnectionsAtPort(port.nodeId, port.portIndex, true);

                if (existingConns.length > 0) {
                    // 移除该输出端口已有连线。
                    existingConns.forEach(conn => {
                        this.removeConnection(conn.id);
                    });
                    if (window.showToast) {
                        const msg = existingConns.length === 1
                            ? '已断开该端口的连接'
                            : `已断开 ${existingConns.length} 条连接`;
                        window.showToast(msg, 'info');
                    }
                    if (flowDebugEnabled()) {
                        console.log('[FlowCanvas] Removed existing connections:', existingConns.map(c => c.id));
                    }
                } else {
                    // 没有现有连线时开始创建新连线。
                    this.startConnection(port.nodeId, port.portIndex);
                }
                return;
            } else if (this.isConnecting) {
                // 正在连线时点击输入端口，完成连接。
                this.finishConnection(port.nodeId, port.portIndex);
                return;
            } else {
                // 点击已连接输入端口时断开连接。
                const existingConn = this.getConnectionAtPort(port.nodeId, port.portIndex, false);

                if (existingConn) {
                    // 移除输入端口现有连线。
                    this.removeConnection(existingConn.id);
                    if (window.showToast) {
                        window.showToast('连接已断开', 'info');
                    }
                    if (flowDebugEnabled()) {
                        console.log('[FlowCanvas] Removed existing connection:', existingConn.id);
                    }
                    return;
                }
            }
        }

        // 连线状态下点击空白区域时取消连线。
        if (this.isConnecting) {
            this.cancelConnection();
            return;
        }

        // 查找点击的节点。
        for (const [id, node] of this.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                this.selectedNode = id;
                // 左键点击节点时准备拖拽。
                if (e.button === 0) {
                    this.draggedNode = id;
                    this.dragOffset = { x: x - node.x, y: y - node.y };
                    this.dragStartNodeState = { id, x: node.x, y: node.y };
                }

                // 触发节点选中回调。
                if (this.onNodeSelected) {
                    this.onNodeSelected(node);
                }
                this.markSelectionChanged('mouse-select-node');

                this.render();
                return;
            }
        }

        this.selectedNode = null;

        // 触发取消选中回调。
        if (this.onNodeSelected) {
            this.onNodeSelected(null);
        }
        this.markSelectionChanged('mouse-clear-selection');

        this.render();
    }

    /**
     * 处理节点双击事件。
     */
    handleDoubleClick(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        // 查找双击的节点。
        for (const [id, node] of this.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                // 触发双击事件回调。
                if (this.onNodeDoubleClicked) {
                    this.onNodeDoubleClicked(node);
                }
                return;
            }
        }
    }
    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    handleMouseMove(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        this.mousePosition = { x, y };

        if (this.isConnecting) {
            const port = this.getPortAt(x, y);
            let nextHoveredPort = null;
            if (port && port.isOutput !== this.connectingFrom?.isOutput && port.nodeId !== this.connectingFrom?.nodeId) {
                const sourceDefinition = this.getPortDefinition(this.connectingFrom);
                const targetDefinition = this.getPortDefinition(port);
                const outputPort = this.connectingFrom.isOutput ? sourceDefinition?.port : targetDefinition?.port;
                const inputPort = this.connectingFrom.isOutput ? targetDefinition?.port : sourceDefinition?.port;
                const sourceType = this.readPortType(outputPort);
                const targetType = this.readPortType(inputPort);
                const compatible = this.checkTypeCompatibility(sourceType, targetType);
                nextHoveredPort = {
                    ...port,
                    compatibility: compatible ? 'compatible' : 'incompatible',
                    incompatibilityMessage: compatible ? '' : this.getPortTypeMismatchMessage(sourceType, targetType)
                };
            }
            const nextCursor = nextHoveredPort?.compatibility === 'incompatible'
                ? 'not-allowed'
                : (nextHoveredPort ? 'pointer' : 'crosshair');
            this.canvas.style.cursor = nextCursor;
            this.setHoveredPort(nextHoveredPort, e);
            this.invalidate();
            return;
        }

        if (this.draggedNode) {
            const dragX = x - this.dragOffset.x;
            const dragY = y - this.dragOffset.y;

            const node = this.nodes.get(this.draggedNode);
            if (node) {
                const snappedX = Math.round(dragX / this.gridSize) * this.gridSize;
                const snappedY = Math.round(dragY / this.gridSize) * this.gridSize;
                if (node.x !== snappedX || node.y !== snappedY) {
                    node.x = snappedX;
                    node.y = snappedY;
                    this._markNodesBoundsDirty();
                    this.invalidate();
                }
            }
            this.canvas.style.cursor = 'grabbing';
            this.setHoveredPort(null);
            return;
        }

        const port = this.getPortAt(x, y);
        let nextHoveredPort = null;
        let nextCursor = 'default';
        if (port) {
            const hasConnection = this.getConnectionAtPort(port.nodeId, port.portIndex, port.isOutput) !== null;

            if (hasConnection && !this.isConnecting) {
                nextCursor = 'pointer';
                nextHoveredPort = { ...port, hasConnection: true };
            } else if (this.isConnecting) {
                nextCursor = 'crosshair';
                nextHoveredPort = port;
            } else {
                nextCursor = 'pointer';
                nextHoveredPort = port;
            }
        }

        const hoverChanged = !this._isSamePortState(this.hoveredPort, nextHoveredPort);
        const cursorChanged = this.canvas.style.cursor !== nextCursor;
        if (hoverChanged || cursorChanged) {
            this.canvas.style.cursor = nextCursor;
            this.setHoveredPort(nextHoveredPort, e);
            if (!hoverChanged && cursorChanged) {
                this.invalidate();
            }
        } else if (nextHoveredPort) {
            this.updatePortTooltip(nextHoveredPort, e);
        }
    }

    handleMouseUp() {
        const draggedNodeId = this.draggedNode;
        const dragStart = this.dragStartNodeState;
        this.draggedNode = null;
        this.dragStartNodeState = null;
        if (draggedNodeId && dragStart?.id === draggedNodeId) {
            const node = this.nodes.get(draggedNodeId);
            if (node && (node.x !== dragStart.x || node.y !== dragStart.y)) {
                this.markFlowStructureChanged('moveNode');
            }
        }
        if (!this.isConnecting) {
            this.canvas.style.cursor = 'default';
        }
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    handleWheel(e) {
        e.preventDefault();
        
        const delta = e.deltaY > 0 ? 0.9 : 1.1;
        // Encoding cleanup: previous comment text was unreadable.
        const newScale = Math.max(0.2, Math.min(2.0, this.scale * delta));
        
        if (newScale !== this.scale) {
            const rect = this.canvas.getBoundingClientRect();
            const mouseX = e.clientX - rect.left;
            const mouseY = e.clientY - rect.top;
            
            // 以鼠标为中心缩放
            this.offset.x += mouseX / this.scale - mouseX / newScale;
            this.offset.y += mouseY / this.scale - mouseY / newScale;
            this.scale = newScale;
            this.invalidate();
            this.notifyViewStateChanged();
        }
    }

    /**
     * 清空画布
     */
    /**
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * @returns {boolean}
     */
    isPointOnConnection(x, y, connection) {
        // Encoding cleanup: previous comment text was unreadable.
        const start = this.getPortPosition(connection.source, connection.sourcePort, true);
        const end = this.getPortPosition(connection.target, connection.targetPort, false);

        if (!start || !end) return false;

        const screenX = (x - this.offset.x) * this.scale;
        const screenY = (y - this.offset.y) * this.scale;

        const controlPoint1X = start.x + (end.x - start.x) / 2;
        const controlPoint1Y = start.y;
        const controlPoint2X = start.x + (end.x - start.x) / 2;
        const controlPoint2Y = end.y;

        const thresholdSq = CONNECTION_HIT_RADIUS_PX * CONNECTION_HIT_RADIUS_PX;
        const step = 1 / CONNECTION_HIT_SAMPLES;

        for (let t = 0; t <= 1; t += step) {
            const mt = 1 - t;
            const px = mt * mt * mt * start.x +
                       3 * mt * mt * t * controlPoint1X +
                       3 * mt * t * t * controlPoint2X +
                       t * t * t * end.x;
            const py = mt * mt * mt * start.y +
                       3 * mt * mt * t * controlPoint1Y +
                       3 * mt * t * t * controlPoint2Y +
                       t * t * t * end.y;

            const dx = screenX - px;
            const dy = screenY - py;
            if (dx * dx + dy * dy < thresholdSq) {
                return true;
            }
        }

        return false;
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * @returns {Object|null}
     */
    getConnectionAt(x, y) {
        // Encoding cleanup: previous comment text was unreadable.
        for (let i = this.connections.length - 1; i >= 0; i--) {
            if (this.isPointOnConnection(x, y, this.connections[i])) {
                return this.connections[i];
            }
        }
        return null;
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    handleKeyDown(e) {
        if (e.target.tagName === 'INPUT' ||
            e.target.tagName === 'TEXTAREA' ||
            e.target.tagName === 'SELECT' ||
            e.target.isContentEditable) {
            return;
        }

        if (e.key === 'Delete' || e.key === 'Backspace') {
            if (this.selectedNode || this.selectedConnection) {
                this.requestSelectionDelete('keyboard');
                return;
            }
        }

        if (e.key === 'Escape' && this.isConnecting) {
            this.cancelConnection();
        }
    }

    setNodeStatus(nodeId, status) {
        const node = this.nodes.get(nodeId);
        if (node && node.status !== status) {
            node.status = status;
            this.invalidate();
        }
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    resetAllStatus() {
        let changed = false;
        this.nodes.forEach(node => {
            if (node.status !== 'idle') {
                changed = true;
            }
            node.status = 'idle';
        });
        if (changed) {
            this.invalidate();
        }
    }

    // ==========================================================================
    // 阶段四增强：右键菜单功能
    // ==========================================================================

    /**
     * 处理右键菜单事件
     */
    handleContextMenu(e) {
        e.preventDefault();

        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        const connection = this.getConnectionAt(x, y);
        if (connection) {
            this.selectedNode = null;
            this.selectedConnection = connection;
            this.markSelectionChanged('context-menu-connection');
            if (typeof this.onSelectionDeleteRequested === 'function' && !confirm('确定删除选中的连接吗？')) {
                return;
            }
            this.requestSelectionDelete('context-menu-connection');
            return;
        }

        let clickedNode = null;
        for (const [id, node] of this.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                clickedNode = { id, node };
                break;
            }
        }

        if (!clickedNode) {
            this.hideContextMenu();
            return;
        }

        this.selectedNode = clickedNode.id;
        this.selectedConnection = null;
        this.markSelectionChanged('context-menu-node');
        this.showNodeContextMenu(e.clientX, e.clientY, clickedNode.id);
    }

    /**
     * 显示节点右键菜单
     */
    showNodeContextMenu(x, y, nodeId) {
        this.hideContextMenu();

        const node = this.nodes.get(nodeId);
        if (!node) return;

        const menu = document.createElement('div');
        menu.className = 'flow-context-menu';
        menu.style.position = 'fixed';
        menu.style.left = x + 'px';
        menu.style.top = y + 'px';
        menu.style.background = 'rgba(15, 36, 53, 0.95)';
        menu.style.backdropFilter = 'blur(10px)';
        menu.style.border = '1px solid rgba(255, 255, 255, 0.1)';
        menu.style.borderRadius = '8px';
        menu.style.padding = '8px 0';
        menu.style.minWidth = '160px';
        menu.style.zIndex = '1000';
        menu.style.boxShadow = '0 8px 32px rgba(0, 0, 0, 0.3)';
        menu.style.animation = 'contextMenuFadeIn 0.15s ease-out';

        const menuItems = [
            { icon: '>', label: '运行到此节点/调试预览', action: () => this.runNode(nodeId) },
            { icon: '+', label: '复制节点', action: () => this.requestNodeDuplicate(nodeId, 'context-menu-node') },
            { icon: 'x', label: '删除节点', action: () => this.requestSelectionDelete('context-menu-node'), danger: true },
            { icon: '!', label: node.disabled ? '启用节点' : '禁用节点', action: () => this.requestNodeDisabledToggle(nodeId, 'context-menu-node') },
            { icon: '?', label: '查看帮助', action: () => this.showNodeHelp(node) }
        ];

        menuItems.forEach(item => {
            const menuItem = document.createElement('div');
            menuItem.className = 'context-menu-item';
            menuItem.style.padding = '8px 16px';
            menuItem.style.cursor = 'pointer';
            menuItem.style.display = 'flex';
            menuItem.style.alignItems = 'center';
            menuItem.style.gap = '8px';
            menuItem.style.fontSize = '13px';
            menuItem.style.color = item.danger ? '#e74c3c' : '#eceef2';
            menuItem.style.transition = 'all 0.2s';
            menuItem.innerHTML = '<span>' + item.icon + '</span><span>' + item.label + '</span>';
            menuItem.addEventListener('mouseenter', () => {
                menuItem.style.background = item.danger ? 'rgba(231, 76, 60, 0.2)' : 'rgba(255, 255, 255, 0.1)';
            });
            menuItem.addEventListener('mouseleave', () => {
                menuItem.style.background = 'transparent';
            });
            menuItem.addEventListener('click', () => {
                item.action();
                this.hideContextMenu();
            });
            menu.appendChild(menuItem);
        });

        document.body.appendChild(menu);
        this.contextMenu = menu;

        if (!document.getElementById('contextMenuStyles')) {
            const style = document.createElement('style');
            style.id = 'contextMenuStyles';
            style.textContent = '@keyframes contextMenuFadeIn { from { opacity: 0; transform: translateY(-8px); } to { opacity: 1; transform: translateY(0); } }' +
                '\n.flow-context-menu .context-menu-item:hover { background: rgba(255, 255, 255, 0.08); }';
            document.head.appendChild(style);
        }

        // Click outside to close the menu
        setTimeout(() => {
            document.addEventListener('click', this._clickOutsideHandler);
        }, 0);
    }

    hideContextMenu() {
        if (this.contextMenu) {
            this.contextMenu.remove();
            this.contextMenu = null;
        }
        document.removeEventListener('click', this._clickOutsideHandler);
    }

    /**
     * 清空画布
     */
    requestSelectionDelete(reason = 'unknown') {
        if (typeof this.onSelectionDeleteRequested === 'function') {
            return this.onSelectionDeleteRequested({
                reason,
                selectedNode: this.selectedNode,
                selectedConnection: this.selectedConnection
            }) === true;
        }

        return this.deleteSelectionWithConfirmation();
    }

    requestNodeDuplicate(nodeId, reason = 'unknown') {
        if (typeof this.onNodeDuplicateRequested === 'function') {
            return this.onNodeDuplicateRequested({
                reason,
                nodeId
            }) === true;
        }

        return Boolean(this.duplicateNode(nodeId));
    }

    requestNodeDisabledToggle(nodeId, reason = 'unknown') {
        if (typeof this.onNodeDisabledToggleRequested === 'function') {
            return this.onNodeDisabledToggleRequested({
                reason,
                nodeId
            }) === true;
        }

        return this.toggleNodeDisabled(nodeId);
    }

    deleteSelectionWithConfirmation() {
        if (this.selectedNode) {
            if (confirm('确定删除选中的节点吗？')) {
                return this.removeNode(this.selectedNode);
            }
            return false;
        }

        if (this.selectedConnection) {
            if (confirm('确定删除选中的连接吗？')) {
                return this.removeConnection(this.selectedConnection.id);
            }
        }

        return false;
    }

    clear(silent = false) {
        const hadSelection = Boolean(this.selectedNode || this.selectedConnection);
        this.nodes.clear();
        this.connections = [];
        this.decisionConfiguration = null;
        this._connectionById.clear();
        this._connectionsByOutputPort.clear();
        this._connectionByInputPort.clear();
        this.selectedNode = null;
        this.draggedNode = null;
        this.dragStartNodeState = null;
        this.selectedConnection = null;
        this._markNodesBoundsDirty();
        this.invalidate();
        if (!silent) {
            this.markFlowStructureChanged('clear');
        }
        if (hadSelection) {
            this.markSelectionChanged('clear');
        }
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    runNode(nodeId) {
        if (flowDebugEnabled()) {
            console.log('[FlowCanvas] Run node:', nodeId);
        }
        const node = this.nodes.get(nodeId);
        const coordinator = window.nodePreviewCoordinator;
        if (!node || !coordinator?.setActiveNode || !coordinator?.requestActivePreview) {
            if (window.showToast) {
                window.showToast('当前环境未启用节点调试预览', 'warning');
            }
            return;
        }

        coordinator.setActiveNode(node);
        coordinator.requestActivePreview({
            immediate: true,
            force: true,
            trigger: 'manual'
        });
        if (window.showToast) {
            window.showToast('已开始运行到此节点的调试预览', 'info');
        }
    }

    /**
     * 复制节点
     */
    duplicateNode(nodeId) {
        const node = this.nodes.get(nodeId);
        if (!node) return null;

        const newNode = {
            ...node,
            id: this.generateUUID(),
            x: node.x + 30,
            y: node.y + 30,
            title: `${node.title} (副本)`,
            inputs: (node.inputs || []).map(port => ({ ...port, id: this.generateUUID() })),
            outputs: (node.outputs || []).map(port => ({ ...port, id: this.generateUUID() })),
            parameters: (node.parameters || []).map(param => ({ ...param })),
            metadata: node.metadata ? { ...node.metadata } : node.metadata
        };

        this.nodes.set(newNode.id, newNode);
        this.selectedNode = newNode.id;
        this.invalidate();
        this.markFlowStructureChanged('duplicateNode');
        this.markSelectionChanged('duplicateNode');
        return newNode;
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    deleteNode(nodeId) {
        return this.removeNode(nodeId);
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    toggleNodeDisabled(nodeId) {
        const node = this.nodes.get(nodeId);
        if (node) {
            node.disabled = !node.disabled;
            this.invalidate();
            this.markFlowStructureChanged('toggleNodeDisabled');
            return true;
        }

        return false;
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    showNodeHelp(node) {
        const metadata = window.operatorLibraryPanel?.metadataByType?.get?.(node.type)
            || window.operatorLibraryPanel?.getOperators?.()?.find?.(operator => operator?.type === node.type)
            || {};
        const displayName = metadata.displayName || metadata.DisplayName || node.title || node.type;
        const description = metadata.description || metadata.Description || '暂无说明';
        const category = metadata.category || metadata.Category || '未分类';
        const inputs = metadata.inputPorts || metadata.InputPorts || node.inputs || [];
        const outputs = metadata.outputPorts || metadata.OutputPorts || node.outputs || [];
        const parameters = metadata.parameters || metadata.Parameters || node.parameters || [];
        const summarizePorts = ports => ports.length > 0
            ? ports.map(port => `${port.displayName || port.DisplayName || port.name || port.Name || '端口'}(${port.dataType || port.DataType || port.type || port.Type || 'Any'})`).join('，')
            : '无';
        const requiredParams = parameters
            .filter(param => Boolean(param.isRequired ?? param.IsRequired))
            .map(param => param.displayName || param.DisplayName || param.name || param.Name)
            .filter(Boolean);
        const content = [
            `算子：${displayName}`,
            `类型：${node.type}`,
            `分类：${category}`,
            `用途：${description}`,
            `输入：${summarizePorts(inputs)}`,
            `输出：${summarizePorts(outputs)}`,
            `必填参数：${requiredParams.length > 0 ? requiredParams.join('，') : '无'}`
        ].join('\n');

        alert(content);
    }

    // ==========================================================================
    // Encoding cleanup: previous comment text was unreadable.
    // ==========================================================================

    /**
     * Encoding cleanup: previous comment text was unreadable.
     */
    initMinimap() {
        if (this.minimap) return;

        this.minimap = document.createElement('div');
        this.minimap.className = 'flow-minimap';
        this.minimap.style.position = 'absolute';
        this.minimap.style.right = '20px';
        this.minimap.style.bottom = '20px';
        this.minimap.style.width = '200px';
        this.minimap.style.height = '150px';
        this.minimap.style.borderRadius = '8px';
        this.minimap.style.overflow = 'hidden';
        this.minimap.style.zIndex = '100';

        this.minimapToggle = document.createElement('button');
        this.minimapToggle.className = 'flow-minimap-toggle';
        this.minimapToggle.type = 'button';
        this.minimapToggle.title = '折叠/展开小地图';
        this.minimapToggle.textContent = '-';
        this.minimapToggle.style.position = 'absolute';
        this.minimapToggle.style.right = '4px';
        this.minimapToggle.style.top = '4px';
        this.minimapToggle.style.zIndex = '2';
        this.minimapToggle.style.width = '22px';
        this.minimapToggle.style.height = '22px';
        this.minimapToggle.style.borderRadius = '4px';
        this.minimapToggle.style.cursor = 'pointer';
        this.minimap.appendChild(this.minimapToggle);

        this.minimapCanvas = document.createElement('canvas');
        this.minimapCanvas.style.width = '200px';
        this.minimapCanvas.style.height = '150px';
        this.minimap.appendChild(this.minimapCanvas);
        this.resizeMinimapCanvas();

        this.canvas.parentElement.appendChild(this.minimap);

        this._minimapToggleHandler = (event) => {
            event.preventDefault();
            event.stopPropagation();
            this._minimapCollapsed = !this._minimapCollapsed;
            this.minimap.style.width = this._minimapCollapsed ? '32px' : '200px';
            this.minimap.style.height = this._minimapCollapsed ? '32px' : '150px';
            this.minimapCanvas.style.display = this._minimapCollapsed ? 'none' : 'block';
            this.minimapToggle.textContent = this._minimapCollapsed ? '+' : '-';
            this._minimapViewportDirty = true;
            this.invalidate();
        };
        this.minimapToggle.addEventListener('click', this._minimapToggleHandler);

        // Click minimap to navigate
        this._minimapClickHandler = (e) => {
            if (this._minimapCollapsed) {
                return;
            }
            const rect = this.minimapCanvas.getBoundingClientRect();
            const x = (e.clientX - rect.left) / rect.width;
            const y = (e.clientY - rect.top) / rect.height;

            const bounds = this.getNodesBounds();
            if (bounds) {
                const targetX = bounds.minX + x * bounds.width;
                const targetY = bounds.minY + y * bounds.height;

                this.offset.x = targetX - (this._logicalWidth / 2) / this.scale;
                this.offset.y = targetY - (this._logicalHeight / 2) / this.scale;
                this.invalidate();
                this.notifyViewStateChanged();
            }
        };
        this.minimapCanvas.addEventListener('click', this._minimapClickHandler);
    }

    getNodesBounds() {
        if (this.nodes.size === 0) {
            this._nodesBoundsDirty = false;
            this._nodesBoundsCache = null;
            return null;
        }

        if (!this._nodesBoundsDirty && this._nodesBoundsCache) {
            return this._nodesBoundsCache;
        }
        
        let minX = Infinity, minY = Infinity;
        let maxX = -Infinity, maxY = -Infinity;
        
        this.nodes.forEach(node => {
            minX = Math.min(minX, node.x);
            minY = Math.min(minY, node.y);
            maxX = Math.max(maxX, node.x + node.width);
            maxY = Math.max(maxY, node.y + node.height);
        });
        
        this._nodesBoundsCache = { minX, minY, maxX, maxY, width: maxX - minX, height: maxY - minY };
        this._nodesBoundsDirty = false;
        return this._nodesBoundsCache;
    }

    /**
     * Resize the minimap canvas to match current bounds.
     */
    resizeMinimapCanvas() {
        if (!this.minimapCanvas) {
            return { width: 0, height: 0, dpr: 1 };
        }

        const width = 200;
        const height = 150;
        const dpr = (typeof window !== 'undefined' && window.devicePixelRatio) ? window.devicePixelRatio : 1;
        const backingWidth = Math.max(1, Math.round(width * dpr));
        const backingHeight = Math.max(1, Math.round(height * dpr));
        if (this.minimapCanvas.width !== backingWidth || this.minimapCanvas.height !== backingHeight) {
            this.minimapCanvas.width = backingWidth;
            this.minimapCanvas.height = backingHeight;
            this._minimapStructureDirty = true;
        }

        return { width, height, dpr };
    }

    buildMinimapLayout(bounds, width, height) {
        const padding = 20;
        const safeWidth = Math.max(bounds.width, 1);
        const safeHeight = Math.max(bounds.height, 1);
        const scaleX = width / (safeWidth + padding * 2);
        const scaleY = height / (safeHeight + padding * 2);
        const scale = Math.min(scaleX, scaleY);

        return {
            scale,
            offsetX: (width - (safeWidth + padding * 2) * scale) / 2 + padding * scale,
            offsetY: (height - (safeHeight + padding * 2) * scale) / 2 + padding * scale
        };
    }

    rebuildMinimapStaticCache(bounds, width, height, dpr) {
        const cacheCanvas = this._minimapStaticCache || document.createElement('canvas');
        const backingWidth = Math.max(1, Math.round(width * dpr));
        const backingHeight = Math.max(1, Math.round(height * dpr));
        if (cacheCanvas.width !== backingWidth || cacheCanvas.height !== backingHeight) {
            cacheCanvas.width = backingWidth;
            cacheCanvas.height = backingHeight;
        }

        const cacheCtx = cacheCanvas.getContext('2d');
        cacheCtx.setTransform(dpr, 0, 0, dpr, 0, 0);
        cacheCtx.clearRect(0, 0, width, height);

        const layout = this.buildMinimapLayout(bounds, width, height);
        this.nodes.forEach(node => {
            const x = layout.offsetX + (node.x - bounds.minX) * layout.scale;
            const y = layout.offsetY + (node.y - bounds.minY) * layout.scale;
            const w = Math.max(4, node.width * layout.scale);
            const h = Math.max(3, node.height * layout.scale);

            cacheCtx.fillStyle = node.disabled
                ? (this.themePalette?.minimapDisabled || FLOW_CANVAS_THEME_DEFAULTS.minimapDisabled)
                : (node.color || '#1890ff');
            cacheCtx.fillRect(x, y, w, h);

            if (node.id === this.selectedNode) {
                cacheCtx.strokeStyle = this.themePalette?.minimapSelected || FLOW_CANVAS_THEME_DEFAULTS.minimapSelected;
                cacheCtx.lineWidth = 2;
                cacheCtx.strokeRect(x - 1, y - 1, w + 2, h + 2);
            }
        });

        this._minimapStaticCache = cacheCanvas;
        this._minimapStaticLayout = { bounds, ...layout };
        this._minimapStructureDirty = false;
    }

    drawMinimap() {
        if (!this.minimapCanvas || this._minimapCollapsed) return;

        const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
        if (this._minimapSelectedNodeId !== this.selectedNode) {
            this._minimapSelectedNodeId = this.selectedNode;
            this._minimapStructureDirty = true;
        }
        if (!this._minimapStructureDirty && !this._minimapViewportDirty) {
            return;
        }
        if (!this._minimapStructureDirty && now - this._minimapLastDrawTime < 80) {
            return;
        }

        const { width, height, dpr } = this.resizeMinimapCanvas();
        const ctx = this.minimapCanvas.getContext('2d');
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, width, height);

        const bounds = this.getNodesBounds();
        if (!bounds) {
            this._minimapStructureDirty = false;
            this._minimapViewportDirty = false;
            return;
        }

        if (this._minimapStructureDirty || !this._minimapStaticCache) {
            this.rebuildMinimapStaticCache(bounds, width, height, dpr);
        }

        ctx.drawImage(this._minimapStaticCache, 0, 0, width, height);
        const layout = this._minimapStaticLayout || this.buildMinimapLayout(bounds, width, height);
        const viewportX = layout.offsetX + (this.offset.x - bounds.minX) * layout.scale;
        const viewportY = layout.offsetY + (this.offset.y - bounds.minY) * layout.scale;
        const viewportW = (this._logicalWidth / this.scale) * layout.scale;
        const viewportH = (this._logicalHeight / this.scale) * layout.scale;

        ctx.strokeStyle = this.themePalette?.minimapViewport || FLOW_CANVAS_THEME_DEFAULTS.minimapViewport;
        ctx.lineWidth = 2;
        ctx.strokeRect(viewportX, viewportY, viewportW, viewportH);

        this._minimapViewportDirty = false;
        this._minimapLastDrawTime = now;
    }

    renderWithMinimap() {
        this.render();
        this.drawMinimap();
    }
}

export default FlowCanvas;
export { FlowCanvas };
