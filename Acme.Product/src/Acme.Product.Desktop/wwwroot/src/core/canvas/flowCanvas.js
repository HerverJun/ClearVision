/**
 * 调试开关：默认关闭。开启后 serialize/deserialize 会打印详细日志。
 * 需要排查时通过 `window.__FLOW_CANVAS_DEBUG__ = true` 或修改此常量启用。
 */
const DEBUG_FLOW_CANVAS = false;
function flowDebugEnabled() {
    return DEBUG_FLOW_CANVAS || (typeof window !== 'undefined' && window.__FLOW_CANVAS_DEBUG__ === true);
}

/**
 * 端口类型颜色映射表 (模块级常量，提高兼容性)
 */
const PORT_TYPE_COLORS = {
    'Image':           '#52c41a',  // 绿色 - 图像
    'String':          '#1890ff',  // 蓝色 - 字符串
    'Integer':         '#fa8c16',  // 橙色 - 整数
    'Float':           '#fa8c16',  // 橙色 - 浮点
    'Boolean':         '#f5222d',  // 红色 - 布尔值
    'Point':           '#eb2f96',  // 粉色 - 坐标
    'Rectangle':       '#eb2f96',  // 粉色 - 矩形
    'Contour':         '#722ed1',  // 紫色 - 轮廓/区域
    'PointList':       '#eb2f96',  // 粉色 - 点列表 (Sprint 1.2)
    'DetectionResult': '#13c2c2',  // 青色 - 检测结果 (Sprint 1.2)
    'DetectionList':   '#13c2c2',  // 青色 - 检测列表 (Sprint 1.2)
    'CircleData':      '#2f54eb',  // 靛蓝 - 圆数据 (Sprint 1.2)
    'LineData':        '#2f54eb',  // 靛蓝 - 直线数据 (Sprint 1.2)
    'Any':             '#bfbfbf',  // 灰色 - 任意
    // 兼容枚举数字值
    0: '#52c41a',
    1: '#fa8c16', 2: '#fa8c16', 3: '#f5222d',
    4: '#1890ff', 5: '#eb2f96', 6: '#eb2f96', 7: '#722ed1',
    8: '#eb2f96', 9: '#13c2c2', 10: '#13c2c2', 11: '#2f54eb', 12: '#2f54eb',
    99: '#bfbfbf'
};

/**
 * 通信类算子类型集合 - Sprint 4 Task 4.3 安全提示
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
const CONNECTION_HIT_RADIUS_PX = 10; // 连线屏幕命中半径
const CONNECTION_HIT_SAMPLES = 16;   // 贝塞尔曲线采样点数
const NODE_DEFAULT_WIDTH = 140;
const NODE_MIN_HEIGHT = 60;
const NODE_HEADER_HEIGHT = 24;
const NODE_PORT_TOP_PADDING = 10;
const NODE_PORT_BOTTOM_PADDING = 10;
const NODE_PORT_ROW_HEIGHT = 18;

function portKey(nodeId, portIndex) {
    return `${nodeId}:${portIndex}`;
}


class FlowCanvas {

    constructor(canvasId) {
        this.canvas = document.getElementById(canvasId);
        this.ctx = this.canvas.getContext('2d');
        this.nodes = new Map();
        this.connections = [];
        this.selectedNode = null;
        this.draggedNode = null;
        this.dragOffset = { x: 0, y: 0 };
        this.scale = 1;
        this.offset = { x: 0, y: 0 };
        this.flowRevision = 0;
        this.viewStateListeners = new Set();
        this.structureStateListeners = new Set();

        // 逻辑尺寸（CSS 像素）与 DPR：所有绘制坐标在逻辑像素空间，通过 setTransform 缩放到 backing store
        this._dpr = 1;
        this._logicalWidth = 0;
        this._logicalHeight = 0;

        // 画布对齐与点阵背景设置
        this.gridSize = 20;
        this.gridColor = 'rgba(48, 71, 62, 0.16)';
        this.gridDotRadius = 1.05;

        // 事件回调
        this.onNodeSelected = null;
        this.onConnectionCreated = null;

        // 连线状态管理
        this.isConnecting = false;
        this.connectingFrom = null;  // { nodeId, portIndex, isOutput }
        this.mousePosition = { x: 0, y: 0 };
        this.hoveredPort = null;  // { nodeId, portIndex, isOutput }

        // 事件处理器引用（用于销毁时移除）
        this._resizeHandler = this.resize.bind(this);
        this._mouseDownHandler = this.handleMouseDown.bind(this);
        this._mouseMoveHandler = this.handleMouseMove.bind(this);
        this._mouseUpHandler = this.handleMouseUp.bind(this);
        this._wheelHandler = this.handleWheel.bind(this);
        this._contextMenuHandler = this.handleContextMenu.bind(this);
        this._keyDownHandler = this.handleKeyDown.bind(this);
        this._dblClickHandler = this.handleDoubleClick.bind(this);
        this._visibilityHandler = this.handleVisibilityChange.bind(this);
        this._drawFrameBound = this._drawFrame.bind(this);

        // 渲染调度状态（脏标记 + 单一 RAF）
        this._animationFrameId = null;
        this._dirty = true;          // 是否需要重绘
        this._lastFrameTime = 0;     // 上一帧时间戳，用于 dt 推进动画
        this._isPaused = false;      // 页面隐藏时暂停

        // ResizeObserver 与节流
        this._resizeObserver = null;
        this._resizeRafId = null;

        // 选中的连接
        this.selectedConnection = null;

        // 连接索引：用于 O(1) 查找，避免每帧 mousemove 上的 .find/.filter 全表扫描
        this._connectionById = new Map();
        this._connectionsByOutputPort = new Map();  // key=portKey -> Set<connection>
        this._connectionByInputPort = new Map();    // key=portKey -> connection（输入端口仅允许 1 条）

        // 粒子精灵缓存（避免每帧 createRadialGradient）
        this._particleSprite = null;
        this._particleSpriteSize = 0;

        // ForEach 子图节点数解析缓存
        this._subGraphNodeCountCache = new WeakMap();

        // 右键菜单
        this.contextMenu = null;
        this._clickOutsideHandler = this.hideContextMenu.bind(this);

        this.initialize();
    }

    /**
     * 初始化画布
     */
    initialize() {
        this.resize();

        // 使用 ResizeObserver 替代 window.resize；用 RAF 合帧，防止拖拽窗口连续触发
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
        this.canvas.addEventListener('wheel', this._wheelHandler);
        this.canvas.addEventListener('contextmenu', this._contextMenuHandler);
        this.canvas.addEventListener('dblclick', this._dblClickHandler);
        window.addEventListener('keydown', this._keyDownHandler);

        document.addEventListener('visibilitychange', this._visibilityHandler);

        // 启动渲染（请求一次重绘，后续根据脏标记/动画状态决定是否继续 RAF）
        this.invalidate();

        // 初始化小地图
        this.initMinimap();
    }

    /**
     * 处理页面可见性变化：后台暂停 RAF，回前台立即调度一次重绘
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

    /**
     * 销毁画布，清理所有事件监听和动画循环
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

        this.canvas.removeEventListener('mousedown', this._mouseDownHandler);
        this.canvas.removeEventListener('mousemove', this._mouseMoveHandler);
        this.canvas.removeEventListener('mouseup', this._mouseUpHandler);
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
        this.selectedConnection = null;
        this.viewStateListeners.clear();
        this.structureStateListeners.clear();
        this._particleSprite = null;

        if (this.minimap) {
            this.minimap.remove();
            this.minimap = null;
            this.minimapCanvas = null;
        }

        this.hideContextMenu();
    }

    /**
     * 调整画布大小，支持 devicePixelRatio。
     * - canvas.width/height（backing store）使用 dpr 放大
     * - canvas.style.width/height 保持 CSS 像素
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
        const node = {
            id: this.generateUUID(),
            type: canonicalType,
            x,
            y,
            width: NODE_DEFAULT_WIDTH,
            height: NODE_MIN_HEIGHT,
            title: config.title || canonicalType,
            inputs: (config.inputs || []).map(p => ({
                id: p.id || this.generateUUID(),
                name: p.name,
                type: p.type
            })),
            outputs: (config.outputs || []).map(p => ({
                id: p.id || this.generateUUID(),
                name: p.name,
                type: p.type
            })),
            color: config.color || '#1890ff',
            ...config
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
     * 删除节点（同时清理相关连接与索引）。
     * 系统节点 (_systemNode) 受保护，不可删除。
     */
    removeNode(nodeId) {
        const node = this.nodes.get(nodeId);
        if (!node) {
            return;
        }
        if (node._systemNode) {
            console.warn('[FlowCanvas] 系统节点不可删除:', node.title || node.type);
            return;
        }

        // 清理与本节点相关的连接（同时同步索引）
        const remaining = [];
        for (const conn of this.connections) {
            if (conn.source === nodeId || conn.target === nodeId) {
                this._unindexConnection(conn);
            } else {
                remaining.push(conn);
            }
        }
        this.connections = remaining;

        this.nodes.delete(nodeId);
        if (this.selectedNode === nodeId) {
            this.selectedNode = null;
        }
        if (this.selectedConnection && (this.selectedConnection.source === nodeId || this.selectedConnection.target === nodeId)) {
            this.selectedConnection = null;
        }
        this.invalidate();
        this.markFlowStructureChanged('removeNode');
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
            for (const conn of this.connections) {
                if (conn.source === current && !visited.has(conn.target)) {
                    stack.push(conn.target);
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
            // 逻辑像素（CSS 像素），便于消费者按 getBoundingClientRect 等价对比
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

        // 返回逻辑像素坐标，与 nodePreviewOverlay 等基于 CSS 容器尺寸的逻辑保持一致
        return {
            x: (node.x - this.offset.x) * this.scale,
            y: (node.y - this.offset.y) * this.scale,
            width: node.width * this.scale,
            height: node.height * this.scale
        };
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

    notifyViewStateChanged() {
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
        // 结构变化时清除 SubGraph 解析缓存（WeakMap 无法批量清，但重赋引用可释放旧键）
        this._subGraphNodeCountCache = new WeakMap();
    }

    /**
     * 绘制点阵背景
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

        // 可视区域使用逻辑尺寸，避免 DPR 导致重复绘制
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
     * 绘制节点 - 阶段四增强版
     * 渐变填充 + 图标 + 状态发光效果
     */
    drawNode(node) {
        const x = (node.x - this.offset.x) * this.scale;
        const y = (node.y - this.offset.y) * this.scale;
        const w = node.width * this.scale;
        const h = node.height * this.scale;
        const isSelected = this.selectedNode === node.id;

        // === ForEach 容器节点判定 ===
        const isForEach = node.type === 'ForEach';
        const ioMode = isForEach ? (node.parameters?.find(p => p.name === 'IoMode' || p.Name === 'IoMode')?.value || 'Parallel') : null;
        const isSequential = ioMode === 'Sequential';

        // 根据状态调整边框颜色和发光效果
        let borderColor = isSelected ? node.color : 'rgba(255, 255, 255, 0.1)';
        let borderWidth = isSelected ? 3 : 1;
        let glowColor = null;

        // === Sprint 4 Task 4.3: 安全提示层 ===
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
            // ForEach Sequential 模式：橙色边框
            borderColor = '#fa8c16';
            borderWidth = 2;
            glowColor = 'rgba(250, 140, 22, 0.3)';
        } else if (isForEach) {
            // ForEach Parallel 模式：青色虚线边框
            borderColor = '#13c2c2';
            borderWidth = 2;
            glowColor = 'rgba(19, 194, 194, 0.2)';
        } else if (isCommunicationOp) {
            // 通信算子：红色警戒边框
            borderColor = '#f5222d';
            borderWidth = 2;
            glowColor = 'rgba(245, 34, 45, 0.3)';
        } else if (hasFileParam) {
            // 含 file 参数的算子：橙色提示边框
            borderColor = '#fa8c16';
            borderWidth = 2;
        } else if (isSelected) {
            glowColor = `${node.color}80`; // 50% opacity
        }

        // 状态发光效果（save/restore 包裹避免阴影泄漏到后续绘制）
        this.ctx.save();
        if (glowColor) {
            this.ctx.shadowColor = glowColor;
            this.ctx.shadowBlur = 15;
            this.ctx.shadowOffsetX = 0;
            this.ctx.shadowOffsetY = 0;
        } else {
            this.ctx.shadowColor = 'rgba(0, 0, 0, 0.3)';
            this.ctx.shadowBlur = 8;
            this.ctx.shadowOffsetX = 2;
            this.ctx.shadowOffsetY = 2;
        }

        // 节点背景 - 渐变填充
        const gradient = this.ctx.createLinearGradient(x, y, x, y + h);
        gradient.addColorStop(0, isSelected ? 'rgba(45, 74, 94, 0.9)' : 'rgba(26, 58, 82, 0.8)');
        gradient.addColorStop(1, isSelected ? 'rgba(26, 58, 82, 0.95)' : 'rgba(13, 27, 42, 0.9)');

        this.ctx.fillStyle = gradient;
        this.ctx.strokeStyle = borderColor;
        this.ctx.lineWidth = borderWidth;

        // ForEach 节点使用虚线边框
        if (isForEach) {
            this.ctx.setLineDash([6, 4]);
        }

        // 绘制圆角矩形
        this.roundRect(x, y, w, h, 8);
        this.ctx.fill();
        this.ctx.stroke();

        // 恢复实线
        if (isForEach) {
            this.ctx.setLineDash([]);
        }

        this.ctx.restore();

        // 标题栏 - 渐变
        const headerGradient = this.ctx.createLinearGradient(x, y, x + w, y);
        headerGradient.addColorStop(0, node.color);
        headerGradient.addColorStop(1, this.adjustColor(node.color, -20));
        this.ctx.fillStyle = headerGradient;
        this.roundRect(x, y, w, 24 * this.scale, { tl: 8, tr: 8, bl: 0, br: 0 });
        this.ctx.fill();

        // 图标
        if (node.iconPath) {
            const targetSize = 16 * this.scale;
            const scaleFactor = targetSize / 24; // ViewBox 24x24
            
            this.ctx.save();
            this.ctx.translate(x + 8 * this.scale, y + 4 * this.scale);
            this.ctx.scale(scaleFactor, scaleFactor);
            this.ctx.fillStyle = '#ffffff'; // 图标永远白色
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

        // === ForEach IoMode 标签 ===
        if (isForEach && ioMode) {
            const ioLabel = isSequential ? '🔗 串行' : '⚡ 并行';
            const labelColor = isSequential ? '#fa8c16' : '#13c2c2';
            this.ctx.fillStyle = labelColor;
            this.ctx.font = `bold ${9 * this.scale}px sans-serif`;
            this.ctx.textAlign = 'right';
            this.ctx.textBaseline = 'middle';
            this.ctx.fillText(ioLabel, x + w - 6 * this.scale, y + 12 * this.scale);
            
            // 显示子图内算子数量提示（带缓存，避免每帧重复 JSON.parse）
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
                        // 静默忽略解析失败，不污染控制台
                    }
                }
                this._subGraphNodeCountCache.set(node, subNodeCount);
            }
            this.ctx.fillStyle = 'rgba(255, 255, 255, 0.6)';
            this.ctx.font = `${10 * this.scale}px sans-serif`;
            this.ctx.textAlign = 'center';
            this.ctx.textBaseline = 'bottom';
            this.ctx.fillText(`[内含 ${subNodeCount} 个算子]`, x + w / 2, y + h - 8 * this.scale);
        }

        // 绘制状态指示器
        if (node.status) {
            const indicatorY = y + 40 * this.scale;
            this.drawStatusIndicator(x + w - 12 * this.scale, indicatorY, node.status);
        }

        // 绘制端口
        this.drawPorts(node, x, y, w, h);

        // === Sprint 4 Task 4.3: 绘制安全标记 ===
        if (isCommunicationOp) {
            // 通信算子：右上角绘制 ⚠ 图标
            this.ctx.fillStyle = '#f5222d';
            this.ctx.font = `bold ${14 * this.scale}px sans-serif`;
            this.ctx.textAlign = 'right';
            this.ctx.textBaseline = 'top';
            this.ctx.fillText('⚠', x + w - 4 * this.scale, y + 2 * this.scale);
        }
    }

    /**
     * 调整颜色亮度
     */
    adjustColor(color, amount) {
        const hex = color.replace('#', '');
        const r = Math.max(0, Math.min(255, parseInt(hex.substr(0, 2), 16) + amount));
        const g = Math.max(0, Math.min(255, parseInt(hex.substr(2, 2), 16) + amount));
        const b = Math.max(0, Math.min(255, parseInt(hex.substr(4, 2), 16) + amount));
        return `rgb(${r}, ${g}, ${b})`;
    }

    /**
     * 绘制状态指示器
     * @param {number} x - 中心X坐标
     * @param {number} y - 中心Y坐标
     * @param {string} status - 状态
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
        
        // 渲染输入端口 - 垂直均分
        node.inputs.forEach((input, index) => {
            const portY = this.getPortYInScreen(y, h, index, node.inputs.length);
            const color = PORT_TYPE_COLORS[input.type] || PORT_TYPE_COLORS['Any'];
            
            this.ctx.beginPath();
            this.ctx.arc(x, portY, portRadius, 0, Math.PI * 2);
            this.ctx.fillStyle = color;
            this.ctx.fill();
            this.ctx.strokeStyle = '#ffffff';
            this.ctx.lineWidth = 1;
            this.ctx.stroke();

            // 绘制类型名 (靠近端口的小标签)
            if (this.scale > 0.8) {
                this.ctx.fillStyle = 'rgba(255, 255, 255, 0.5)';
                this.ctx.font = `${8 * this.scale}px sans-serif`;
                this.ctx.textAlign = 'left';
                const typeName = typeof input.type === 'string' ? input.type : 'Any';
                this.ctx.fillText(input.name || typeName, x + 8 * this.scale, portY + 3 * this.scale);
            }
        });
        
        // 渲染输出端口 - 垂直均分
        node.outputs.forEach((output, index) => {
            const portY = this.getPortYInScreen(y, h, index, node.outputs.length);
            const color = PORT_TYPE_COLORS[output.type] || PORT_TYPE_COLORS['Any'];

            this.ctx.beginPath();
            this.ctx.arc(x + w, portY, portRadius, 0, Math.PI * 2);
            this.ctx.fillStyle = color;
            this.ctx.fill();
            this.ctx.strokeStyle = '#ffffff';
            this.ctx.lineWidth = 1;
            this.ctx.stroke();

            // 绘制类型名
            if (this.scale > 0.8) {
                this.ctx.fillStyle = 'rgba(255, 255, 255, 0.5)';
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
     * 获取端口在屏幕上的坐标
     * @param {string} nodeId - 节点ID
     * @param {number} portIndex - 端口索引
     * @param {boolean} isOutput - 是否是输出端口
     * @returns {{x: number, y: number}} 端口坐标
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
     * 检测鼠标位置是否在端口上
     * @param {number} x - 鼠标X坐标（世界坐标）
     * @param {number} y - 鼠标Y坐标（世界坐标）
     * @returns {{nodeId: string, portIndex: number, isOutput: boolean}|null}
     */
    getPortAt(x, y) {
        const screenX = (x - this.offset.x) * this.scale;
        const screenY = (y - this.offset.y) * this.scale;
        const hitRadiusSq = (PORT_HIT_RADIUS_PX * this.scale) ** 2; // 使用距离平方，避免 Math.sqrt

        for (const [nodeId, node] of this.nodes) {
            const nodeScreenX = (node.x - this.offset.x) * this.scale;
            const nodeScreenY = (node.y - this.offset.y) * this.scale;
            const w = node.width * this.scale;
            const h = node.height * this.scale;

            // 检测输入端口 (垂直分布)
            for (let i = 0; i < node.inputs.length; i++) {
                const portY = this.getPortYInScreen(nodeScreenY, h, i, node.inputs.length);
                const dx = screenX - nodeScreenX;
                const dy = screenY - portY;
                if (dx * dx + dy * dy < hitRadiusSq) {
                    return { nodeId, portIndex: i, isOutput: false };
                }
            }

            // 检测输出端口 (垂直分布)
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
     * 获取指定端口上的连接（O(1) 查找）
     * @param {string} nodeId - 节点ID
     * @param {number} portIndex - 端口索引
     * @param {boolean} isOutput - 是否是输出端口
     * @returns {Object|null} 连接对象或null
     */
    getConnectionAtPort(nodeId, portIndex, isOutput) {
        if (isOutput) {
            const set = this._connectionsByOutputPort.get(portKey(nodeId, portIndex));
            if (!set || set.size === 0) return null;
            // 输出端口可能有多条连接，返回第一条
            return set.values().next().value;
        }
        return this._connectionByInputPort.get(portKey(nodeId, portIndex)) || null;
    }

    /**
     * 获取指定端口上的所有连接（用于输出端口）
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
     * 开始创建连接
     * @param {string} nodeId - 起始节点ID
     * @param {number} portIndex - 起始端口索引
     */
    startConnection(nodeId, portIndex) {
        this.isConnecting = true;
        this.connectingFrom = { nodeId, portIndex, isOutput: true };
        this.canvas.style.cursor = 'crosshair';
        this.invalidate();
        console.log('[FlowCanvas] 开始连线，从节点:', nodeId, '端口:', portIndex);
    }

    /**
     * 完成连接创建
     * @param {string} nodeId - 目标节点ID
     * @param {number} portIndex - 目标端口索引
     */
    finishConnection(nodeId, portIndex) {
        if (!this.isConnecting || !this.connectingFrom) return;

        // 检查类型兼容性
        const sourceNode = this.nodes.get(this.connectingFrom.nodeId);
        const targetNode = this.nodes.get(nodeId);
        
        if (!sourceNode || !targetNode || !sourceNode.outputs[this.connectingFrom.portIndex]) {
            this.cancelConnection();
            return;
        }

        const sourcePort = sourceNode.outputs[this.connectingFrom.portIndex];
        const targetPort = targetNode.inputs[portIndex];

        if (!this.checkTypeCompatibility(sourcePort.type, targetPort.type)) {
            console.warn(`[FlowCanvas] 类型不匹配: ${sourcePort.type} -> ${targetPort.type}`);
            if (window.showToast) window.showToast(`类型不匹配: ${sourcePort.type} -> ${targetPort.type}`, 'warning');
            this.cancelConnection();
            return;
        }

        // 检查连接有效性
        if (this.connectingFrom.nodeId === nodeId) {
            console.warn('[FlowCanvas] 不能连接到自己');
            this.cancelConnection();
            return;
        }

        // 检查是否已存在相同连接
        const existingConn = this.connections.find(conn =>
            conn.source === this.connectingFrom.nodeId &&
            conn.sourcePort === this.connectingFrom.portIndex &&
            conn.target === nodeId &&
            conn.targetPort === portIndex
        );

        if (existingConn) {
            console.warn('[FlowCanvas] 连接已存在');
            this.cancelConnection();
            return;
        }

        // 检查输入端口是否已被占用（一个输入端口只能有一个连接）
        const targetPortOccupied = this.connections.find(conn =>
            conn.target === nodeId &&
            conn.targetPort === portIndex
        );

        if (targetPortOccupied) {
            console.warn('[FlowCanvas] 目标输入端口已被占用');
            this.cancelConnection();
            return;
        }

        // 创建连接
        if (this.wouldCreateCycle(this.connectingFrom.nodeId, nodeId)) {
            console.warn('[FlowCanvas] Connection would create a cycle');
            if (window.showToast) window.showToast('Connection would create a cycle', 'warning');
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

        console.log('[FlowCanvas] 连接已创建:', connection);

        // 触发回调
        if (this.onConnectionCreated) {
            this.onConnectionCreated(connection);
        }

        this.cancelConnection();
    }

    /**
     * 取消当前连接操作
     */
    cancelConnection() {
        this.isConnecting = false;
        this.connectingFrom = null;
        this.canvas.style.cursor = 'default';
        this.render(); // 刷新以清除高亮
    }

    /**
     * 检查类型兼容性
     */
    checkTypeCompatibility(sourceType, targetType) {
        // 枚举转换映射 (兼容数字和字符串)
        const normalize = (t) => {
            if (t === 'Any' || t === 99) return 'Any';
            if (t === 'Image' || t === 0) return 'Image';
            if (t === 'Integer' || t === 1 || t === 'Float' || t === 2) return 'Number';
            if (t === 'Boolean' || t === 3) return 'Boolean';
            if (t === 'String' || t === 4) return 'String';
            if (t === 'Point' || t === 5 || t === 'Rectangle' || t === 6 || t === 'PointList' || t === 8) return 'Geometry';
            if (t === 'Contour' || t === 7) return 'Contour';
            if (t === 'DetectionResult' || t === 9 || t === 'DetectionList' || t === 10) return 'Detection';
            if (t === 'CircleData' || t === 11) return 'CircleData';
            if (t === 'LineData' || t === 12) return 'LineData';
            return t;
        };

        const s = normalize(sourceType);
        const t = normalize(targetType);

        if (s === 'Any' || t === 'Any') return true;
        return s === t;
    }

    /**
     * 连线时高亮兼容的端口
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
                if (this.checkTypeCompatibility(sourcePort.type, port.type)) {
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
     * 删除连接
     * @param {string} connectionId - 连接ID
     */
    removeConnection(connectionId) {
        const connection = this._connectionById.get(connectionId);
        if (!connection) {
            return;
        }
        this.connections = this.connections.filter(conn => conn.id !== connectionId);
        this._unindexConnection(connection);
        if (this.selectedConnection && this.selectedConnection.id === connectionId) {
            this.selectedConnection = null;
        }
        this.invalidate();
        this.markFlowStructureChanged('removeConnection');
    }

    /**
     * 绘制临时连线（拖拽过程中）
     */
    drawTempConnection() {
        if (!this.isConnecting || !this.connectingFrom) return;

        const startPos = this.getPortPosition(
            this.connectingFrom.nodeId,
            this.connectingFrom.portIndex,
            this.connectingFrom.isOutput
        );

        if (!startPos) return;

        // 【新增】连线时高亮兼容端口
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

        this.ctx.strokeStyle = '#1890ff';
        this.ctx.lineWidth = 2 * this.scale;
        this.ctx.setLineDash([5 * this.scale, 5 * this.scale]);
        this.ctx.stroke();
        this.ctx.setLineDash([]);
    }

    /**
     * 绘制连接线 - 阶段四增强版，带数据流动粒子动画
     */
    drawConnection(connection, dt) {
        const sourceNode = this.nodes.get(connection.source);
        const targetNode = this.nodes.get(connection.target);

        if (!sourceNode || !targetNode) return;

        // 【修正】使用 getPortPosition 以支持垂直分布
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

        // 根据连接状态设置样式
        if (connection.status === 'active') {
            this.ctx.strokeStyle = '#34c759';
            this.ctx.shadowColor = 'rgba(46, 204, 113, 0.5)';
            this.ctx.shadowBlur = 10;
        } else if (connection.status === 'error') {
            this.ctx.strokeStyle = '#e74c3c';
            this.ctx.shadowColor = 'rgba(231, 76, 60, 0.5)';
            this.ctx.shadowBlur = 10;
        } else {
            this.ctx.strokeStyle = isSelected ? '#ffffff' : '#5ac8fa';
            this.ctx.shadowColor = isSelected ? 'rgba(255, 255, 255, 0.4)' : 'transparent';
            this.ctx.shadowBlur = isSelected ? 8 : 0;
        }

        this.ctx.lineWidth = isSelected ? 3 * this.scale : 2 * this.scale;
        this.ctx.stroke();
        this.ctx.restore();

        // 绘制数据流动粒子动画
        if (connection.status === 'active' || connection.status === 'flowing') {
            this.drawFlowParticles(start.x, start.y, controlPoint1X, start.y,
                                   controlPoint2X, end.y, end.x, end.y, connection, dt);
        }
    }
    
    /**
     * 绘制数据流动粒子 - 阶段四增强
     * @param {number} dt - 距上一帧的毫秒数，用于基于时间的平滑推进
     */
    drawFlowParticles(startX, startY, cp1x, cp1y, cp2x, cp2y, endX, endY, connection, dt) {
        // 初始化粒子系统
        if (!connection.particles) {
            connection.particles = [];
            for (let i = 0; i < 5; i++) {
                connection.particles.push({
                    t: i / 5,
                    speed: 0.005 + Math.random() * 0.003
                });
            }
        }

        // 缓存粒子发光精灵（按当前 scale 只创建一次）
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
        const timeScale = dt / 16; // 以 60fps 为基准归一化

        connection.particles.forEach(particle => {
            // 【修复】基于 dt 推进，避免帧率波动导致速度不均
            particle.t += particle.speed * timeScale;
            if (particle.t > 1) particle.t = 0;

            // 计算贝塞尔曲线上的点
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

            // 绘制缓存的发光粒子
            this.ctx.drawImage(sprite.canvas, x - sprite.size / 2, y - sprite.size / 2, sprite.size, sprite.size);
        });
    }

    /**
     * 绘制圆角矩形
     * @param {number} x
     * @param {number} y
     * @param {number} w
     * @param {number} h
     * @param {number|{tl?:number,tr?:number,bl?:number,br?:number}} r 半径，可统一传入或按角分别指定
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

        // 限制半径，防止 w/h 较小时出现交叉
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
     * 请求重绘：标记画布为脏，并调度一次 RAF。
     * 这是新代码推荐使用的入口；render() 保留为兼容别名。
     */
    invalidate() {
        this._dirty = true;
        this._scheduleFrame();
    }

    /**
     * 兼容入口：等价于 invalidate()。
     * 历史调用大量 `this.render()`，保留语义不变（请求一次重绘），
     * 但不再创建无限循环。
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
     * 当前帧是否需要持续动画（连线拖拽、活跃数据流粒子、运行中节点等）。
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
     * 视口裁剪：判断节点是否在当前可视区域内（含一点缓冲）。
     * @private
     */
    _isNodeInViewport(node) {
        const vw = this._logicalWidth / this.scale;
        const vh = this._logicalHeight / this.scale;
        const vx = this.offset.x;
        const vy = this.offset.y;
        // 留 100px 缓冲，避免边缘裁切突兀
        return node.x + node.width >= vx - 100 && node.x <= vx + vw + 100 &&
               node.y + node.height >= vy - 100 && node.y <= vy + vh + 100;
    }

    /**
     * 视口裁剪：判断连接是否至少有一个端点在可视区域内。
     * @private
     */
    _isConnectionVisible(conn) {
        const src = this.nodes.get(conn.source);
        const tgt = this.nodes.get(conn.target);
        return (src && this._isNodeInViewport(src)) || (tgt && this._isNodeInViewport(tgt));
    }

    /**
     * 实际绘制一帧。决定是否继续 RAF 循环。
     */
    _drawFrame(now) {
        this._animationFrameId = null;

        if (this._isPaused) {
            return;
        }

        const timestamp = typeof now === 'number' ? now : performance.now();
        const dt = this._lastFrameTime > 0 ? Math.max(0, timestamp - this._lastFrameTime) : 16;
        this._lastFrameTime = timestamp;

        // 清空画布（逻辑像素）
        this.ctx.clearRect(0, 0, this._logicalWidth, this._logicalHeight);

        // 绘制网格
        this.drawGrid();

        // 绘制连接线（dt 用于粒子动画）
        for (const conn of this.connections) {
            if (this._isConnectionVisible(conn)) {
                this.drawConnection(conn, dt);
            }
        }

        // 绘制临时连线
        if (this.isConnecting) {
            this.drawTempConnection();
        }

        // 绘制节点（视口裁剪）
        for (const node of this.nodes.values()) {
            if (this._isNodeInViewport(node)) {
                this.drawNode(node);
            }
        }

        // 绘制悬停端口高亮
        if (this.hoveredPort && !this.isConnecting) {
            this.drawPortHighlight(this.hoveredPort);
        }

        // 绘制小地图
        this.drawMinimap();

        this._dirty = false;

        // 如果有活动动画，继续 RAF；否则等待下一次 invalidate()
        if (this._hasAnimation()) {
            this._scheduleFrame();
        } else {
            this._lastFrameTime = 0;
        }
    }

    /**
     * 规范化端口类型，确保其符合后端枚举名称 (PascalCase)
     */
    normalizePortType(type) {
        if (!type) return 'Any';
        // 如果后端传过来的是枚举数字，则保持不变，后端 JsonStringEnumConverter 可以解析
        if (typeof type === 'number') return type;
        
        const map = {
            'any': 'Any',
            'image': 'Image',
            'string': 'String',
            'integer': 'Integer',
            'float': 'Float',
            'boolean': 'Boolean',
            'point': 'Point',
            'rectangle': 'Rectangle',
            'contour': 'Contour'
        };
        
        return map[type.toLowerCase()] || type;
    }

    normalizeOperatorType(type) {
        if (!type) return type;
        return LEGACY_OPERATOR_TYPE_ALIASES[type] || type;
    }

    /**
     * 序列化流程数据 - 适配后端 DTO (camelCase)
     * 后端 Program.cs 配置 JsonNamingPolicy.CamelCase，所以必须使用小驼峰
     */
    serialize() {
        // 【修复】先确保所有节点的端口都有稳定的 ID
        // 这样 operators 和 connections 都会使用相同的 ID
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

        // 构建 Operators 列表 (camelCase)
        const operators = Array.from(this.nodes.values()).map(node => ({
            id: node.id,
            name: node.title,
            type: this.normalizeOperatorType(node.type),
            x: node.x,
            y: node.y,
            inputPorts: (node.inputs || []).map(p => ({
                id: p.id || p.Id || this.generateUUID(), // 【修复】同时检查大小写
                name: p.name,
                dataType: this.normalizePortType(p.type), // PortDataType enum
                direction: 0, // Input
                isRequired: false
            })),
            outputPorts: (node.outputs || []).map(p => ({
                id: p.id || p.Id || this.generateUUID(), // 【修复】同时检查大小写
                name: p.name,
                dataType: this.normalizePortType(p.type),
                direction: 1, // Output
                isRequired: false
            })),
            parameters: (node.parameters || []).map(p => ({
                name: p.name,
                value: p.value !== undefined ? p.value : p.defaultValue,
                dataType: p.dataType || p.type
            })),
            isEnabled: node.disabled !== true
        }));

        // 构建 Connections 列表 (camelCase)
        const debug = flowDebugEnabled();
        if (debug) {
            console.log('[FlowCanvas serialize] === START ===');
            console.log('[FlowCanvas serialize] Raw connections count:', this.connections.length);
            console.log('[FlowCanvas serialize] Nodes in canvas:', Array.from(this.nodes.keys()));
        }

        const connections = this.connections
            .filter(conn => {
                // 过滤掉无效的连接（source 或 target 为空、undefined 或空GUID）
                const isValidSource = conn.source && conn.source !== '00000000-0000-0000-0000-000000000000';
                const isValidTarget = conn.target && conn.target !== '00000000-0000-0000-0000-000000000000';
                if (!isValidSource || !isValidTarget) {
                    console.warn(`[FlowCanvas serialize] 过滤掉无效连接: source=${conn.source}, target=${conn.target}`);
                }
                return isValidSource && isValidTarget;
            })
            .map(conn => {
                const sourceNode = this.nodes.get(conn.source);
                const targetNode = this.nodes.get(conn.target);

                // 【修复】添加端口索引边界检查，并同时检查 id/Id 属性
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
                    console.warn(`[FlowCanvas serialize] 源端口索引越界: ${conn.sourcePort}, 可用端口数: ${sourceNode?.outputs?.length || 0}`);
                }

                if (targetNode && conn.targetPort >= 0 && conn.targetPort < targetNode.inputs.length) {
                    const port = targetNode.inputs[conn.targetPort];
                    targetPortId = port?.id || port?.Id;
                    if (!targetPortId) {
                        port.id = this.generateUUID();
                        targetPortId = port.id;
                    }
                } else if (debug) {
                    console.warn(`[FlowCanvas serialize] 目标端口索引越界: ${conn.targetPort}, 可用端口数: ${targetNode?.inputs?.length || 0}`);
                }

                // 【修复】如果无法获取端口ID，跳过此连接而不是生成错误的UUID
                if (!sourcePortId || !targetPortId) {
                    if (debug) {
                        console.warn(`[FlowCanvas serialize] 跳过无效连接: sourcePortId=${sourcePortId}, targetPortId=${targetPortId}`);
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

        // UpdateFlowRequest 期望的结构 (camelCase 会被后端自动映射)
        const result = {
            operators: operators,
            connections: connections
        };

        if (debug) {
            console.log('[FlowCanvas serialize] Operators count:', operators.length);
            console.log('[FlowCanvas serialize] Connections count:', connections.length);
            console.log('[FlowCanvas serialize] === END ===');
        }

        return result;
    }

    /**
     * 反序列化流程数据
     */
    deserialize(data) {
        if (!data) return;
        this.clear(true);

        // 支持多种嵌套结构 (后端 DTO 可能包装在 project.flow 中)
        const flowData = data.project?.flow || data.flow || data;

        // 处理列表属性 (驼峰/帕斯卡/旧版 nodes 键)
        const operators = flowData.operators || flowData.Operators || flowData.nodes || [];
        const connections = flowData.connections || flowData.Connections || [];

        if (flowDebugEnabled()) {
            console.log('[FlowCanvas] 开始反序列化. 算子数:', operators.length, '连接数:', connections.length);
        }

        if (operators) {
            operators.forEach(op => {
                // 适配后端 DTO (PascalCase) 或前端 (camelCase)
                const id = op.id ?? op.Id;
                const type = this.normalizeOperatorType(op.type ?? op.Type);
                const title = op.name ?? op.Name ?? op.title ?? type;

                // 【修复】标准化端口数据，统一使用小写属性名（id/name/type）
                const normalizePort = (p) => ({
                    id: p.id || p.Id || this.generateUUID(),
                    name: p.name || p.Name,
                    type: p.type || p.Type || p.dataType || p.DataType || 0
                });

                const inputs = (op.inputPorts || op.InputPorts || op.inputs || []).map(normalizePort);
                const outputs = (op.outputPorts || op.OutputPorts || op.outputs || []).map(normalizePort);

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
                // Adapt backend DTO (PascalCase) or frontend (camelCase)
                const id = conn.id || conn.Id;
                const sourceId = conn.sourceOperatorId || conn.SourceOperatorId || conn.source;
                const targetId = conn.targetOperatorId || conn.TargetOperatorId || conn.target;

                const sourcePortId = conn.sourcePortId || conn.SourcePortId;
                const targetPortId = conn.targetPortId || conn.TargetPortId;

                const sourceNode = this.nodes.get(sourceId);
                const targetNode = this.nodes.get(targetId);

                let sourcePortIndex = conn.sourcePort ?? 0;
                let targetPortIndex = conn.targetPort ?? 0;

                // Find index by Port ID if available (Backend/DTO usually provides IDs)
                if (sourcePortId && sourceNode && sourceNode.outputs) {
                    const idx = sourceNode.outputs.findIndex(p => (p.id === sourcePortId) || (p.Id === sourcePortId));
                    if (idx !== -1) sourcePortIndex = idx;
                }

                if (targetPortId && targetNode && targetNode.inputs) {
                    const idx = targetNode.inputs.findIndex(p => (p.id === targetPortId) || (p.Id === targetPortId));
                    if (idx !== -1) targetPortIndex = idx;
                }

                return {
                    id: id,
                    source: sourceId,
                    sourcePort: sourcePortIndex,
                    target: targetId,
                    targetPort: targetPortIndex
                };
            }).filter(conn => {
                // 过滤掉无效的连接（source 或 target 为空、undefined 或空GUID）
                const isValidSource = conn.source && conn.source !== '00000000-0000-0000-0000-000000000000';
                const isValidTarget = conn.target && conn.target !== '00000000-0000-0000-0000-000000000000';
                if (!isValidSource || !isValidTarget) {
                    console.warn('[FlowCanvas] 过滤掉无效连接:', conn);
                }
                return isValidSource && isValidTarget;
            });

            // 反序列化后必须重建连线索引，否则后续 O(1) 查询失效
            this._rebuildConnectionIndex();
        }

        this.invalidate();
        this.markFlowStructureChanged('deserialize');
    }

    /**
     * 绘制端口高亮效果
     * @param {{nodeId: string, portIndex: number, isOutput: boolean, hasConnection: boolean}} port
     */
    drawPortHighlight(port) {
        const pos = this.getPortPosition(port.nodeId, port.portIndex, port.isOutput);
        if (!pos) return;

        // 绘制高亮圆环
        this.ctx.beginPath();
        this.ctx.arc(pos.x, pos.y, 8 * this.scale, 0, Math.PI * 2);
        this.ctx.strokeStyle = port.isOutput ? '#1890ff' : '#52c41a';
        this.ctx.lineWidth = 2 * this.scale;
        this.ctx.stroke();

        // 绘制发光效果
        this.ctx.beginPath();
        this.ctx.arc(pos.x, pos.y, 12 * this.scale, 0, Math.PI * 2);
        this.ctx.fillStyle = port.isOutput
            ? 'rgba(24, 144, 255, 0.2)'
            : 'rgba(82, 196, 26, 0.2)';
        this.ctx.fill();

        // 【新增】如果端口已连接，绘制断开指示
        if (port.hasConnection) {
            // 绘制红色虚线圆环表示可断开
            this.ctx.beginPath();
            this.ctx.arc(pos.x, pos.y, 14 * this.scale, 0, Math.PI * 2);
            this.ctx.strokeStyle = 'rgba(231, 76, 60, 0.6)'; // 红色半透明
            this.ctx.lineWidth = 2 * this.scale;
            this.ctx.setLineDash([4 * this.scale, 2 * this.scale]);
            this.ctx.stroke();
            this.ctx.setLineDash([]);
        }
    }

    /**
     * 处理鼠标按下
     */
    handleMouseDown(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        // 更新鼠标位置
        this.mousePosition = { x, y };

        // 首先检测是否点击了端口
        const port = this.getPortAt(x, y);
        if (port) {
            if (port.isOutput) {
                // 【新增】检查输出端口是否已有连接
                const existingConns = this.getConnectionsAtPort(port.nodeId, port.portIndex, true);
                
                if (existingConns.length > 0) {
                    // 断开该端口的所有连接
                    existingConns.forEach(conn => {
                        this.removeConnection(conn.id);
                    });
                    if (window.showToast) {
                        const msg = existingConns.length === 1 
                            ? '连接已断开' 
                            : `已断开 ${existingConns.length} 个连接`;
                        window.showToast(msg, 'info');
                    }
                    console.log('[FlowCanvas] 已断开连接:', existingConns.map(c => c.id));
                } else {
                    // 没有连接，从输出端口开始连线
                    this.startConnection(port.nodeId, port.portIndex);
                }
                return;
            } else if (this.isConnecting) {
                // 从输入端口完成连线
                this.finishConnection(port.nodeId, port.portIndex);
                return;
            } else {
                // 【新增】点击输入端口时检查是否已有连接
                const existingConn = this.getConnectionAtPort(port.nodeId, port.portIndex, false);
                
                if (existingConn) {
                    // 断开该输入端口的连接
                    this.removeConnection(existingConn.id);
                    if (window.showToast) {
                        window.showToast('连接已断开', 'info');
                    }
                    console.log('[FlowCanvas] 已断开连接:', existingConn.id);
                    return;
                }
            }
        }

        // 如果在连线状态但点击了空白处，取消连线
        if (this.isConnecting) {
            this.cancelConnection();
            return;
        }

        // 查找点击的节点
        for (const [id, node] of this.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                this.selectedNode = id;
                // 仅左键启动节点拖拽，避免右键菜单期间触发意外拖拽
                if (e.button === 0) {
                    this.draggedNode = id;
                    this.dragOffset = { x: x - node.x, y: y - node.y };
                }

                // 触发节点选中回调
                if (this.onNodeSelected) {
                    this.onNodeSelected(node);
                }

                this.render();
                return;
            }
        }

        this.selectedNode = null;

        // 触发取消选中回调
        if (this.onNodeSelected) {
            this.onNodeSelected(null);
        }

        this.render();
    }

    /**
     * 处理双击事件（主要用于子图展开等高级交互）
     */
    handleDoubleClick(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        // 查找双击的节点
        for (const [id, node] of this.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                
                // 触发双击事件回调
                if (this.onNodeDoubleClicked) {
                    this.onNodeDoubleClicked(node);
                }
                return;
            }
        }
    }

    /**
     * 处理鼠标移动
     */
    handleMouseMove(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        // 更新鼠标位置
        this.mousePosition = { x, y };

        // 处理连线状态
        if (this.isConnecting) {
            // 检测悬停的端口
            const port = this.getPortAt(x, y);
            if (port && !port.isOutput && port.nodeId !== this.connectingFrom?.nodeId) {
                // 悬停在有效的输入端口上
                this.hoveredPort = port;
                this.canvas.style.cursor = 'pointer';
            } else {
                this.hoveredPort = null;
                this.canvas.style.cursor = 'crosshair';
            }
            this.invalidate();
            return;
        }

        // 处理节点拖拽
        if (this.draggedNode) {
            const dragX = x - this.dragOffset.x;
            const dragY = y - this.dragOffset.y;

            const node = this.nodes.get(this.draggedNode);
            if (node) {
                // 【修复】使用 this.gridSize 对齐网格，而非硬编码 10
                node.x = Math.round(dragX / this.gridSize) * this.gridSize;
                node.y = Math.round(dragY / this.gridSize) * this.gridSize;
            }
            this.canvas.style.cursor = 'grabbing';
            this.hoveredPort = null;
            this.invalidate();
            return;
        }

        // 检测端口悬停（改变光标）。getPortAt 内部已遍历所有节点/端口，
        // 此处不再重复遍历节点列表做 body hover，避免每帧双次 O(n)。
        const port = this.getPortAt(x, y);
        if (port) {
            const hasConnection = this.getConnectionAtPort(port.nodeId, port.portIndex, port.isOutput) !== null;

            if (hasConnection && !this.isConnecting) {
                this.canvas.style.cursor = 'pointer';
                this.hoveredPort = { ...port, hasConnection: true };
            } else if (this.isConnecting) {
                this.canvas.style.cursor = 'crosshair';
                this.hoveredPort = port;
            } else {
                this.canvas.style.cursor = 'pointer';
                this.hoveredPort = port;
            }
        } else {
            this.hoveredPort = null;
            this.canvas.style.cursor = 'default';
        }
        this.invalidate();
    }

    /**
     * 处理鼠标释放
     */
    handleMouseUp() {
        this.draggedNode = null;
        if (!this.isConnecting) {
            this.canvas.style.cursor = 'default';
        }
    }

    /**
     * 处理滚轮缩放
     */
    handleWheel(e) {
        e.preventDefault();
        
        const delta = e.deltaY > 0 ? 0.9 : 1.1;
        // 调整缩放范围：0.2 (20%) - 2.0 (200%) - 用户反馈缩放过小不方便定位
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
     * 检测鼠标位置是否在连接线上
     * @param {number} x - 鼠标X坐标（世界坐标）
     * @param {number} y - 鼠标Y坐标（世界坐标）
     * @param {Object} connection - 连接线对象
     * @returns {boolean}
     */
    isPointOnConnection(x, y, connection) {
        // 使用 getPortPosition 获得准确端口位置，而不是用节点中心近似
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
     * 获取鼠标位置下的连接线
     * @param {number} x - 鼠标X坐标（世界坐标）
     * @param {number} y - 鼠标Y坐标（世界坐标）
     * @returns {Object|null}
     */
    getConnectionAt(x, y) {
        // 倒序遍历，优先选择最上面的连接线
        for (let i = this.connections.length - 1; i >= 0; i--) {
            if (this.isPointOnConnection(x, y, this.connections[i])) {
                return this.connections[i];
            }
        }
        return null;
    }

    /**
     * 处理右键菜单
     */
    /**
     * 处理键盘事件
     */
    handleKeyDown(e) {
        // 如果焦点在输入框、文本区域或可编辑元素中，不拦截快捷键
        if (e.target.tagName === 'INPUT' || 
            e.target.tagName === 'TEXTAREA' || 
            e.target.tagName === 'SELECT' || 
            e.target.isContentEditable) {
            return;
        }

        // Delete 键或 Backspace 键删除选中的节点或连接线
        if (e.key === 'Delete' || e.key === 'Backspace') {
            if (this.selectedNode) {
                if (confirm('确定要删除选中的节点吗？')) {
                    this.removeNode(this.selectedNode);
                }
            } else if (this.selectedConnection) {
                if (confirm('确定要删除选中的连接线吗？')) {
                    this.removeConnection(this.selectedConnection.id);
                }
            }
        }

        // Escape 键取消连线
        if (e.key === 'Escape' && this.isConnecting) {
            this.cancelConnection();
        }
    }

    /**
     * 设置节点状态
     * @param {string} nodeId - 节点ID
     * @param {string} status - 状态: 'idle' | 'running' | 'success' | 'error'
     */
    setNodeStatus(nodeId, status) {
        const node = this.nodes.get(nodeId);
        if (node) {
            node.status = status;
            this.render();
        }
    }

    /**
     * 重置所有节点状态
     */
    resetAllStatus() {
        this.nodes.forEach(node => {
            node.status = 'idle';
        });
        this.render();
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

            if (confirm('确定要删除这条连接线吗？')) {
                this.removeConnection(connection.id);
            }
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
        menu.style.cssText = `
            position: fixed;
            left: ${x}px;
            top: ${y}px;
            background: rgba(15, 36, 53, 0.95);
            backdrop-filter: blur(10px);
            border: 1px solid rgba(255, 255, 255, 0.1);
            border-radius: 8px;
            padding: 8px 0;
            min-width: 160px;
            z-index: 1000;
            box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
            animation: contextMenuFadeIn 0.15s ease-out;
        `;
        
        const menuItems = [
            { icon: '▶️', label: '运行', action: () => this.runNode(nodeId) },
            { icon: '📋', label: '复制', action: () => this.duplicateNode(nodeId) },
            { icon: '❌', label: '删除', action: () => this.removeNode(nodeId), danger: true },
            { icon: '🚫', label: node.disabled ? '启用' : '禁用', action: () => this.toggleNodeDisabled(nodeId) },
            { icon: '❓', label: '查看帮助', action: () => this.showNodeHelp(node) }
        ];
        
        menuItems.forEach(item => {
            const menuItem = document.createElement('div');
            menuItem.className = 'context-menu-item';
            menuItem.style.cssText = `
                padding: 8px 16px;
                cursor: pointer;
                display: flex;
                align-items: center;
                gap: 8px;
                font-size: 13px;
                color: ${item.danger ? '#e74c3c' : '#eceef2'};
                transition: all 0.2s;
            `;
            menuItem.innerHTML = `<span>${item.icon}</span><span>${item.label}</span>`;
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
        
        // 添加动画样式
        if (!document.getElementById('contextMenuStyles')) {
            const style = document.createElement('style');
            style.id = 'contextMenuStyles';
            style.textContent = `
                @keyframes contextMenuFadeIn {
                    from { opacity: 0; transform: scale(0.95); }
                    to { opacity: 1; transform: scale(1); }
                }
            `;
            document.head.appendChild(style);
        }
        
        // 点击外部关闭菜单
        setTimeout(() => {
            document.addEventListener('click', this._clickOutsideHandler);
        }, 0);
    }

    /**
     * 隐藏右键菜单
     */
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
    clear(silent = false) {
        this.nodes.clear();
        this.connections = [];
        this._connectionById.clear();
        this._connectionsByOutputPort.clear();
        this._connectionByInputPort.clear();
        this.selectedNode = null;
        this.draggedNode = null;
        this.selectedConnection = null;
        this.invalidate();
        if (!silent) {
            this.markFlowStructureChanged('clear');
        }
    }

    /**
     * 运行单个节点
     */
    runNode(nodeId) {
        console.log('[FlowCanvas] 运行节点:', nodeId);
        this.setNodeStatus(nodeId, 'running');
        // 这里可以触发实际的节点执行逻辑
        setTimeout(() => {
            this.setNodeStatus(nodeId, 'success');
        }, 1000);
    }

    /**
     * 复制节点
     */
    duplicateNode(nodeId) {
        const node = this.nodes.get(nodeId);
        if (!node) return;

        const newNode = {
            ...node,
            id: this.generateUUID(),
            x: node.x + 30,
            y: node.y + 30,
            title: node.title + ' (副本)'
        };

        this.nodes.set(newNode.id, newNode);
        this.selectedNode = newNode.id;
        this.invalidate();
        this.markFlowStructureChanged('duplicateNode');
    }

    /**
     * 删除节点（右键菜单及 API 兼容入口）。委托给 removeNode 以保留 _systemNode 保护与索引同步。
     */
    deleteNode(nodeId) {
        this.removeNode(nodeId);
    }

    /**
     * 切换节点禁用状态
     */
    toggleNodeDisabled(nodeId) {
        const node = this.nodes.get(nodeId);
        if (node) {
            node.disabled = !node.disabled;
            this.invalidate();
            this.markFlowStructureChanged('toggleNodeDisabled');
        }
    }

    /**
     * 显示节点帮助
     */
    showNodeHelp(node) {
        alert(`节点类型: ${node.type}\n名称: ${node.title}\n\n这是一个 ${node.type} 算子节点。`);
    }

    // ==========================================================================
    // 阶段四增强：小地图功能
    // ==========================================================================

    /**
     * 初始化小地图
     */
    initMinimap() {
        if (this.minimap) return;
        
        this.minimap = document.createElement('div');
        this.minimap.className = 'flow-minimap';
        this.minimap.style.cssText = `
            position: absolute;
            right: 20px;
            bottom: 20px;
            width: 200px;
            height: 150px;
            background: rgba(15, 36, 53, 0.9);
            border: 1px solid rgba(255, 255, 255, 0.1);
            border-radius: 8px;
            overflow: hidden;
            z-index: 100;
            box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
        `;
        
        this.minimapCanvas = document.createElement('canvas');
        this.minimapCanvas.width = 200;
        this.minimapCanvas.height = 150;
        this.minimap.appendChild(this.minimapCanvas);
        
        this.canvas.parentElement.appendChild(this.minimap);
        
        // 点击小地图导航
        this.minimapCanvas.addEventListener('click', (e) => {
            const rect = this.minimapCanvas.getBoundingClientRect();
            const x = (e.clientX - rect.left) / rect.width;
            const y = (e.clientY - rect.top) / rect.height;

            // 计算视口中心位置
            const bounds = this.getNodesBounds();
            if (bounds) {
                // 【修复】bounds.width 已是 maxX-minX，不应再加一次
                const targetX = bounds.minX + x * bounds.width;
                const targetY = bounds.minY + y * bounds.height;

                this.offset.x = targetX - (this._logicalWidth / 2) / this.scale;
                this.offset.y = targetY - (this._logicalHeight / 2) / this.scale;
                this.invalidate();
                this.notifyViewStateChanged();
            }
        });
    }

    /**
     * 获取所有节点的边界
     */
    getNodesBounds() {
        if (this.nodes.size === 0) return null;
        
        let minX = Infinity, minY = Infinity;
        let maxX = -Infinity, maxY = -Infinity;
        
        this.nodes.forEach(node => {
            minX = Math.min(minX, node.x);
            minY = Math.min(minY, node.y);
            maxX = Math.max(maxX, node.x + node.width);
            maxY = Math.max(maxY, node.y + node.height);
        });
        
        return { minX, minY, maxX, maxY, width: maxX - minX, height: maxY - minY };
    }

    /**
     * 绘制小地图
     */
    drawMinimap() {
        if (!this.minimapCanvas) return;
        
        const ctx = this.minimapCanvas.getContext('2d');
        const width = this.minimapCanvas.width;
        const height = this.minimapCanvas.height;
        
        // 清空
        ctx.clearRect(0, 0, width, height);
        
        const bounds = this.getNodesBounds();
        if (!bounds) return;
        
        // 添加内边距
        const padding = 20;
        const scaleX = width / (bounds.width + padding * 2);
        const scaleY = height / (bounds.height + padding * 2);
        const scale = Math.min(scaleX, scaleY);
        
        const offsetX = (width - (bounds.width + padding * 2) * scale) / 2 + padding * scale;
        const offsetY = (height - (bounds.height + padding * 2) * scale) / 2 + padding * scale;
        
        // 绘制节点
        this.nodes.forEach(node => {
            const x = offsetX + (node.x - bounds.minX) * scale;
            const y = offsetY + (node.y - bounds.minY) * scale;
            const w = Math.max(4, node.width * scale);
            const h = Math.max(3, node.height * scale);
            
            ctx.fillStyle = node.disabled ? '#666' : (node.color || '#1890ff');
            ctx.fillRect(x, y, w, h);
            
            // 选中高亮
            if (node.id === this.selectedNode) {
                ctx.strokeStyle = '#fff';
                ctx.lineWidth = 2;
                ctx.strokeRect(x - 1, y - 1, w + 2, h + 2);
            }
        });
        
        // 绘制视口框（使用逻辑尺寸，避免 DPR 导致框体翻倍）
        const viewportX = offsetX + (this.offset.x - bounds.minX) * scale;
        const viewportY = offsetY + (this.offset.y - bounds.minY) * scale;
        const viewportW = (this._logicalWidth / this.scale) * scale;
        const viewportH = (this._logicalHeight / this.scale) * scale;
        
        ctx.strokeStyle = 'rgba(231, 76, 60, 0.8)';
        ctx.lineWidth = 2;
        ctx.strokeRect(viewportX, viewportY, viewportW, viewportH);
    }

    /**
     * 更新渲染循环以包含小地图
     */
    renderWithMinimap() {
        this.render();
        this.drawMinimap();
    }
}

export default FlowCanvas;
export { FlowCanvas };
