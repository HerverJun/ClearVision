/**
 * 鐠嬪啳鐦鈧崗绛圭窗姒涙顓婚崗鎶芥４閵嗗倸绱戦崥顖氭倵 serialize/deserialize 娴兼碍澧﹂崡鎷岊嚊缂佸棙妫╄箛妞尖偓?
 * 闂団偓鐟曚焦甯撻弻銉︽闁俺绻?`window.__FLOW_CANVAS_DEBUG__ = true` 閹存牔鎱ㄩ弨瑙勵劃鐢悂鍣洪崥顖滄暏閵?
 */
const DEBUG_FLOW_CANVAS = false;
function flowDebugEnabled() {
    return DEBUG_FLOW_CANVAS || (typeof window !== 'undefined' && window.__FLOW_CANVAS_DEBUG__ === true);
}

/**
 * 缁旑垰褰涚猾璇茬€锋０婊嗗閺勭姴鐨犵悰?(濡€虫健缁狙冪埗闁插骏绱濋幓鎰扮彯閸忕厧顔愰幀?
 */
const PORT_TYPE_COLORS = {
    'Image':           '#52c41a',  // 缂佽儻澹?- 閸ユ儳鍎?
    'String':          '#1890ff',  // 閽冩繆澹?- 鐎涙顑佹稉?
    'Integer':         '#fa8c16',  // Encoding cleanup: previous comment text was unreadable.
    'Float':           '#fa8c16',  // 濮楁瑨澹?- 濞搭喚鍋?
    'Boolean':         '#f5222d',  // 缁俱垼澹?- 鐢啫鐨甸崐?
    'Point':           '#eb2f96',  // 缁澹?- 閸ф劖鐖?
    'Rectangle':       '#eb2f96',  // 缁澹?- 閻晛鑸?
    'Contour':         '#722ed1',  // 缁鳖偉澹?- 鏉烆喖绮?閸栧搫鐓?
    'PointList':       '#eb2f96',  // 缁澹?- 閻愮懓鍨悰?(Sprint 1.2)
    'DetectionResult': '#13c2c2',  // 闂堟帟澹?- 濡偓濞村绮ㄩ弸?(Sprint 1.2)
    'DetectionList':   '#13c2c2',  // 闂堟帟澹?- 濡偓濞村鍨悰?(Sprint 1.2)
    'CircleData':      '#2f54eb',  // Encoding cleanup: previous comment text was unreadable.
    'LineData':        '#2f54eb',  // 闂堟稖鎽?- 閻╁鍤庨弫鐗堝祦 (Sprint 1.2)
    'Any':             '#bfbfbf',  // 閻忔媽澹?- 娴犵粯鍓?
    // 鍏煎鏋氫妇鏁板瓧鍊?
    0: '#52c41a',
    1: '#fa8c16', 2: '#fa8c16', 3: '#f5222d',
    4: '#1890ff', 5: '#eb2f96', 6: '#eb2f96', 7: '#722ed1',
    8: '#eb2f96', 9: '#13c2c2', 10: '#13c2c2', 11: '#2f54eb', 12: '#2f54eb',
    99: '#bfbfbf'
};

/**
 * 闁矮淇婄猾鑽ょ暬鐎涙劗琚崹瀣肠閸?- Sprint 4 Task 4.3 鐎瑰鍙忛幓鎰仛
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

const PORT_HIT_RADIUS_PX = 12;       // 绔彛灞忓箷鍛戒腑鍗婂緞
const CONNECTION_HIT_RADIUS_PX = 10; // Encoding cleanup: previous comment text was unreadable.
const CONNECTION_HIT_SAMPLES = 16;   // 鐠愭繂顢ｇ亸鏃€娲哥痪鍧楀櫚閺嶉鍋ｉ弫?
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
        this.globalVariableSchema = { sourceBindings: [], targetBindings: [] };

        // Encoding cleanup: previous comment text was unreadable.
        this._dpr = 1;
        this._logicalWidth = 0;
        this._logicalHeight = 0;

        // 閻㈣绔风€靛綊缍堟稉搴ｅ仯闂冧絻鍎楅弲顖濐啎缂?
        this.gridSize = 20;
        this.gridColor = 'rgba(48, 71, 62, 0.16)';
        this.gridDotRadius = 1.05;

        // 浜嬩欢鍥炶皟
        this.onNodeSelected = null;
        this.onConnectionCreated = null;
        this.onSelectionDeleteRequested = null;
        this.onNodeDuplicateRequested = null;
        this.onNodeDisabledToggleRequested = null;

        // 鏉╃偟鍤庨悩鑸碘偓浣侯吀閻?
        this.isConnecting = false;
        this.connectingFrom = null;  // { nodeId, portIndex, isOutput }
        this.mousePosition = { x: 0, y: 0 };
        this.hoveredPort = null;  // { nodeId, portIndex, isOutput }

        // 娴滃娆㈡径鍕倞閸ｃ劌绱╅悽顭掔礄閻劋绨柨鈧В浣规缁夊娅庨敍?
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

        // 濞撳弶鐓嬬拫鍐ㄥ閻樿埖鈧緤绱欓懘蹇旂垼鐠?+ 閸楁洑绔?RAF閿?
        this._animationFrameId = null;
        this._dirty = true;          // 閺勵垰鎯侀棁鈧憰渚€鍣哥紒?
        this._lastFrameTime = 0;     // 娑撳﹣绔寸敮褎妞傞梻瀛樺煈閿涘瞼鏁ゆ禍?dt 閹恒劏绻橀崝銊ф暰
        this._isPaused = false;      // 妞ょ敻娼伴梾鎰閺冭埖娈忛崑?

        // ResizeObserver 娑撳氦濡ù?
        this._resizeObserver = null;
        this._resizeRafId = null;

        // 闁鑵戦惃鍕箾閹?
        this.selectedConnection = null;

        // Encoding cleanup: previous comment text was unreadable.
        this._connectionById = new Map();
        this._connectionsByOutputPort = new Map();  // key=portKey -> Set<connection>
        this._connectionByInputPort = new Map();    // key=portKey -> connection閿涘牐绶崗銉ь伂閸欙絼绮庨崗浣筋啅 1 閺夆槄绱?

        // 缁帒鐡欑划鍓т紥缂傛挸鐡ㄩ敍鍫ヤ缉閸忓秵鐦＄敮?createRadialGradient閿?
        this._particleSprite = null;
        this._particleSpriteSize = 0;

        // ForEach 鐎涙劕娴橀懞鍌滃仯閺佹媽袙閺嬫劗绱︾€?
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

        // 鍙抽敭鑿滃崟
        this.contextMenu = null;
        this._clickOutsideHandler = this.hideContextMenu.bind(this);

        this.initialize();
    }

    /**
     * 閸掓繂顫愰崠鏍暰鐢?
     */
    initialize() {
        this.resize();

        // 娴ｈ法鏁?ResizeObserver 閺囧じ鍞?window.resize閿涙稓鏁?RAF 閸氬牆鎶氶敍宀勬Щ濮濄垺瀚嬮幏鐣岀崶閸欙綀绻涚紒顓⌒曢崣?
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

        // 缂佹垵鐣炬禍瀣╂
        this.canvas.addEventListener('mousedown', this._mouseDownHandler);
        this.canvas.addEventListener('mousemove', this._mouseMoveHandler);
        this.canvas.addEventListener('mouseup', this._mouseUpHandler);
        this.canvas.addEventListener('wheel', this._wheelHandler);
        this.canvas.addEventListener('contextmenu', this._contextMenuHandler);
        this.canvas.addEventListener('dblclick', this._dblClickHandler);
        window.addEventListener('keydown', this._keyDownHandler);

        document.addEventListener('visibilitychange', this._visibilityHandler);

        // 閸氼垰濮╁〒鍙夌厠閿涘牐顕Ч鍌欑濞嗭繝鍣哥紒姗堢礉閸氬海鐢婚弽瑙勫祦閼村繑鐖ｇ拋?閸斻劎鏁鹃悩鑸碘偓浣稿枀鐎规碍妲搁崥锔炬埛缂?RAF閿?
        this.invalidate();

        // 閸掓繂顫愰崠鏍х毈閸︽澘娴?
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

    /**
     * 闁库偓濮ｄ胶鏁剧敮鍐跨礉濞撳懐鎮婇幍鈧張澶夌皑娴犲墎娲冮崥顒€鎷伴崝銊ф暰瀵邦亞骞?
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

        this.hideContextMenu();
    }

    /**
     * Encoding cleanup: previous comment text was unreadable.
     * - canvas.width/height閿涘潌acking store閿涘濞囬悽?dpr 閺€鎯с亣
     * - canvas.style.width/height 娣囨繃瀵?CSS 閸嶅繒绀?
     * - ctx.setTransform(dpr,0,0,dpr,0,0) 鐠佲晜澧嶉張澶岀帛閸掕泛娼楅弽鍥﹀▏閻劑鈧槒绶崓蹇曠
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
     * 閻㈢喐鍨歎UID
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
     * 濞ｈ濮為懞鍌滃仯
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
     * Encoding cleanup: previous comment text was unreadable.
     * 缁崵绮洪懞鍌滃仯 (_systemNode) 閸欐ぞ绻氶幎銈忕礉娑撳秴褰查崚鐘绘珟閵?
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

        this.nodes.delete(nodeId);
        if (this.selectedNode === nodeId) {
            this.selectedNode = null;
        }
        if (this.selectedConnection && (this.selectedConnection.source === nodeId || this.selectedConnection.target === nodeId)) {
            this.selectedConnection = null;
        }
        this.invalidate();
        this.markFlowStructureChanged('removeNode');
        return true;
    }

    /**
     * 濞ｈ濮炴潻鐐村复
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
            // 闁槒绶崓蹇曠閿涘湑SS 閸嶅繒绀岄敍澶涚礉娓氬じ绨☉鍫ｅ瀭閼板懏瀵?getBoundingClientRect 缁涘鐜€佃鐦?
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

        // 鏉╂柨娲栭柅鏄忕帆閸嶅繒绀岄崸鎰垼閿涘奔绗?nodePreviewOverlay 缁涘鐔€娴?CSS 鐎圭懓娅掔亸鍝勵嚟閻ㄥ嫰鈧槒绶穱婵囧瘮娑撯偓閼?
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
        // Encoding cleanup: previous comment text was unreadable.
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
            Boolean(left?.hasConnection) === Boolean(right?.hasConnection);
    }

    /**
     * 缂佹ê鍩楅悙褰掓█閼冲本娅?
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

        // 閸欘垵顫嬮崠鍝勭厵娴ｈ法鏁ら柅鏄忕帆鐏忓搫顕敍宀勪缉閸?DPR 鐎佃壈鍤ч柌宥咁槻缂佹ê鍩?
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
     * 缂佹ê鍩楅懞鍌滃仯 - 闂冭埖顔岄崶娑橆杻瀵櫣澧?
     * 濞撴劕褰夋繅顐㈠帠 + 閸ョ偓鐖?+ 閻樿埖鈧礁褰傞崗澶嬫櫏閺?
     */
    drawNode(node) {
        const x = (node.x - this.offset.x) * this.scale;
        const y = (node.y - this.offset.y) * this.scale;
        const w = node.width * this.scale;
        const h = node.height * this.scale;
        const isSelected = this.selectedNode === node.id;

        // === ForEach 鐎圭懓娅掗懞鍌滃仯閸掋倕鐣?===
        const isForEach = node.type === 'ForEach';
        const ioMode = isForEach ? (node.parameters?.find(p => p.name === 'IoMode' || p.Name === 'IoMode')?.value || 'Parallel') : null;
        const isSequential = ioMode === 'Sequential';

        // 閺嶈宓侀悩鑸碘偓浣界殶閺佺绔熷鍡涱杹閼规彃鎷伴崣鎴濆帨閺佸牊鐏?
        let borderColor = isSelected ? node.color : 'rgba(255, 255, 255, 0.1)';
        let borderWidth = isSelected ? 3 : 1;
        let glowColor = null;

        // === Sprint 4 Task 4.3: 鐎瑰鍙忛幓鎰仛鐏?===
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
            // ForEach Sequential 濡€崇础閿涙碍顭堕懝鑼剁珶濡?
            borderColor = '#fa8c16';
            borderWidth = 2;
            glowColor = 'rgba(250, 140, 22, 0.3)';
        } else if (isForEach) {
            // ForEach Parallel 濡€崇础閿涙岸娼氶懝鑼舵珓缁捐儻绔熷?
            borderColor = '#13c2c2';
            borderWidth = 2;
            glowColor = 'rgba(19, 194, 194, 0.2)';
        } else if (isCommunicationOp) {
            // 闁矮淇婄粻妤€鐡欓敍姘卞閼硅尪顒熼幋鎺曠珶濡?
            borderColor = '#f5222d';
            borderWidth = 2;
            glowColor = 'rgba(245, 34, 45, 0.3)';
        } else if (hasFileParam) {
            // Encoding cleanup: previous comment text was unreadable.
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
            this.ctx.shadowColor = 'rgba(0, 0, 0, 0.3)';
            this.ctx.shadowBlur = 8;
            this.ctx.shadowOffsetX = 2;
            this.ctx.shadowOffsetY = 2;
        }

        // 閼哄倻鍋ｉ懗灞炬珯 - 濞撴劕褰夋繅顐㈠帠
        const gradient = this.ctx.createLinearGradient(x, y, x, y + h);
        gradient.addColorStop(0, isSelected ? 'rgba(45, 74, 94, 0.9)' : 'rgba(26, 58, 82, 0.8)');
        gradient.addColorStop(1, isSelected ? 'rgba(26, 58, 82, 0.95)' : 'rgba(13, 27, 42, 0.9)');

        this.ctx.fillStyle = gradient;
        this.ctx.strokeStyle = borderColor;
        this.ctx.lineWidth = borderWidth;

        // ForEach 閼哄倻鍋ｆ担璺ㄦ暏閾忔氨鍤庢潏瑙勵攱
        if (isForEach) {
            this.ctx.setLineDash([6, 4]);
        }

        // 缂佹ê鍩楅崷鍡氼潡閻晛鑸?
        this.roundRect(x, y, w, h, 8);
        this.ctx.fill();
        this.ctx.stroke();

        // 閹垹顦茬€圭偟鍤?
        if (isForEach) {
            this.ctx.setLineDash([]);
        }

        this.ctx.restore();

        // 閺嶅洭顣介弽?- 濞撴劕褰?
        const headerGradient = this.ctx.createLinearGradient(x, y, x + w, y);
        headerGradient.addColorStop(0, node.color);
        headerGradient.addColorStop(1, this.adjustColor(node.color, -20));
        this.ctx.fillStyle = headerGradient;
        this.roundRect(x, y, w, 24 * this.scale, { tl: 8, tr: 8, bl: 0, br: 0 });
        this.ctx.fill();

        // 閸ョ偓鐖?
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

        // 閺嶅洭顣介弬鍥х摟
        this.ctx.fillStyle = '#ffffff';
        this.ctx.font = `bold ${11 * this.scale}px sans-serif`;
        this.ctx.textAlign = 'left';
        this.ctx.textBaseline = 'middle';
        const titleX = (node.icon || node.iconPath) ? x + 28 * this.scale : x + 10 * this.scale;
        this.ctx.fillText(node.title, titleX, y + 12 * this.scale);

        // === ForEach IoMode 閺嶅洨顒?===
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
            this.ctx.fillStyle = 'rgba(255, 255, 255, 0.6)';
            this.ctx.font = `${10 * this.scale}px sans-serif`;
            this.ctx.textAlign = 'center';
            this.ctx.textBaseline = 'bottom';
            this.ctx.fillText(`[${subNodeCount} nodes]`, x + w / 2, y + h - 8 * this.scale);
        }

        if (node.status) {
            const indicatorY = y + 40 * this.scale;
            this.drawStatusIndicator(x + w - 12 * this.scale, indicatorY, node.status);
        }

        // 缂佹ê鍩楃粩顖氬經
        this.drawGlobalVariableBadges(node, x, y, w);
        this.drawPorts(node, x, y, w, h);

        // === Sprint 4 Task 4.3: 缂佹ê鍩楃€瑰鍙忛弽鍥唶 ===
        if (isCommunicationOp) {
            // 闁矮淇婄粻妤€鐡欓敍姘礁娑撳﹨顫楃紒妯哄煑 閳?閸ョ偓鐖?
            this.ctx.fillStyle = '#f5222d';
            this.ctx.font = `bold ${14 * this.scale}px sans-serif`;
            this.ctx.textAlign = 'right';
            this.ctx.textBaseline = 'top';
            this.ctx.fillText('COM', x + w - 4 * this.scale, y + 2 * this.scale);
        }
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
            this.ctx.fillStyle = 'rgba(13, 27, 42, 0.86)';
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
     * 缂佹ê鍩楅悩鑸碘偓浣瑰瘹缁€鍝勬珤
     * @param {number} x - 娑擃厼绺綳閸ф劖鐖?
     * @param {number} y - 娑擃厼绺綴閸ф劖鐖?
     * @param {string} status - 閻樿埖鈧?
     */
    drawStatusIndicator(x, y, status) {
        const radius = 6 * this.scale;

        this.ctx.beginPath();
        this.ctx.arc(x, y, radius, 0, Math.PI * 2);

        switch (status) {
            case 'running':
                this.ctx.fillStyle = '#1890ff';
                this.ctx.fill();
                // 缂佹ê鍩楅弮瀣祮閻ㄥ嫯绻樻惔锔惧箚
                this.ctx.beginPath();
                this.ctx.arc(x, y, radius + 2 * this.scale, 0, Math.PI * 2);
                this.ctx.strokeStyle = 'rgba(24, 144, 255, 0.5)';
                this.ctx.lineWidth = 2 * this.scale;
                this.ctx.stroke();
                break;
            case 'success':
                this.ctx.fillStyle = '#52c41a';
                this.ctx.fill();
                // 缂佹ê鍩楃€电懓瀣€
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
                // 缂佹ê鍩梄
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
     * 缂佹ê鍩楃粩顖氬經
     */
    drawPorts(node, x, y, w, h) {
        const portRadius = 5 * this.scale;
        
        // 濞撳弶鐓嬫潏鎾冲弳缁旑垰褰?- 閸ㄥ倻娲块崸鍥у瀻
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

            // 缂佹ê鍩楃猾璇茬€烽崥?(闂堢姾绻庣粩顖氬經閻ㄥ嫬鐨弽鍥╊劮)
            if (this.scale > 0.8) {
                this.ctx.fillStyle = 'rgba(255, 255, 255, 0.5)';
                this.ctx.font = `${8 * this.scale}px sans-serif`;
                this.ctx.textAlign = 'left';
                const typeName = typeof input.type === 'string' ? input.type : 'Any';
                this.ctx.fillText(input.name || typeName, x + 8 * this.scale, portY + 3 * this.scale);
            }
        });
        
        // 濞撳弶鐓嬫潏鎾冲毉缁旑垰褰?- 閸ㄥ倻娲块崸鍥у瀻
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

            // 缂佹ê鍩楃猾璇茬€烽崥?
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
     * 濡偓濞村绱堕弽鍥︾秴缂冾喗妲搁崥锕€婀粩顖氬經娑?
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * @returns {{nodeId: string, portIndex: number, isOutput: boolean}|null}
     */
    getPortAt(x, y) {
        const screenX = (x - this.offset.x) * this.scale;
        const screenY = (y - this.offset.y) * this.scale;
        const hitRadiusSq = (PORT_HIT_RADIUS_PX * this.scale) ** 2; // 娴ｈ法鏁ょ捄婵堫瀲楠炶櫕鏌熼敍宀勪缉閸?Math.sqrt

        for (const [nodeId, node] of this.nodes) {
            const nodeScreenX = (node.x - this.offset.x) * this.scale;
            const nodeScreenY = (node.y - this.offset.y) * this.scale;
            const w = node.width * this.scale;
            const h = node.height * this.scale;

            // 濡偓濞村绶崗銉ь伂閸?(閸ㄥ倻娲块崚鍡楃)
            for (let i = 0; i < node.inputs.length; i++) {
                const portY = this.getPortYInScreen(nodeScreenY, h, i, node.inputs.length);
                const dx = screenX - nodeScreenX;
                const dy = screenY - portY;
                if (dx * dx + dy * dy < hitRadiusSq) {
                    return { nodeId, portIndex: i, isOutput: false };
                }
            }

            // 濡偓濞村绶崙铏诡伂閸?(閸ㄥ倻娲块崚鍡楃)
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
     * 閼惧嘲褰囬幐鍥х暰缁旑垰褰涙稉濠勬畱閹碘偓閺堝绻涢幒銉礄閻劋绨潏鎾冲毉缁旑垰褰涢敍?
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
     * 瀵偓婵鍨卞楦跨箾閹?
     * @param {string} nodeId - 璧峰鑺傜偣ID
     * @param {number} portIndex - 鐠у嘲顫愮粩顖氬經缁便垹绱?
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
     * 鐎瑰本鍨氭潻鐐村复閸掓稑缂?
     * @param {string} nodeId - 閻╊喗鐖ｉ懞鍌滃仯ID
     * @param {number} portIndex - 閻╊喗鐖ｇ粩顖氬經缁便垹绱?
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

        if (!this.checkTypeCompatibility(sourcePort.type, targetPort.type)) {
            const incompatibilityMessage = `端口类型不匹配：${sourcePort.type} -> ${targetPort.type}`;
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
        this.canvas.style.cursor = 'default';
        this.invalidate(); // 鍒锋柊浠ユ竻闄ら珮浜?
    }

    /**
     * 濡偓閺屻儳琚崹瀣悑鐎硅鈧?
     */
    checkTypeCompatibility(sourceType, targetType) {
        // 閺嬫矮濡囨潪顒佸床閺勭姴鐨?(閸忕厧顔愰弫鏉跨摟閸滃苯鐡х粭锔胯)
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
     * Encoding cleanup: previous comment text was unreadable.
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
     * Encoding cleanup: previous comment text was unreadable.
     * @param {string} connectionId - 鏉╃偞甯碔D
     */
    removeConnection(connectionId) {
        const connection = this._connectionById.get(connectionId);
        if (!connection) {
            return false;
        }
        this.connections = this.connections.filter(conn => conn.id !== connectionId);
        this._unindexConnection(connection);
        if (this.selectedConnection && this.selectedConnection.id === connectionId) {
            this.selectedConnection = null;
        }
        this.invalidate();
        this.markFlowStructureChanged('removeConnection');
        return true;
    }

    /**
     * 缂佹ê鍩楁稉瀛樻鏉╃偟鍤庨敍鍫熷珛閹峰€熺箖缁嬪鑵戦敍?
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

        // 缂佹ê鍩楅搹姘卞殠
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
     * 缂佹ê鍩楁潻鐐村复缁?- 闂冭埖顔岄崶娑橆杻瀵櫣澧楅敍灞界敨閺佺増宓佸ù浣稿З缁帒鐡欓崝銊ф暰
     */
    drawConnection(connection, dt) {
        const sourceNode = this.nodes.get(connection.source);
        const targetNode = this.nodes.get(connection.target);

        if (!sourceNode || !targetNode) return;

        // 閵嗘劒鎱ㄥ锝冣偓鎴滃▏閻?getPortPosition 娴犮儲鏁幐浣哥€惄鏉戝瀻鐢?
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

        // 閺嶈宓佹潻鐐村复閻樿埖鈧浇顔曠純顔界壉瀵?
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

        // 缂佹ê鍩楅弫鐗堝祦濞翠礁濮╃划鎺戠摍閸斻劎鏁?
        if (connection.status === 'active' || connection.status === 'flowing') {
            this.drawFlowParticles(start.x, start.y, controlPoint1X, start.y,
                                   controlPoint2X, end.y, end.x, end.y, connection, dt);
        }
    }
    
    /**
     * 缂佹ê鍩楅弫鐗堝祦濞翠礁濮╃划鎺戠摍 - 闂冭埖顔岄崶娑橆杻瀵?
     * @param {number} dt - 鐠烘繀绗傛稉鈧敮褏娈戝В顐ゎ潡閺佸府绱濋悽銊ょ艾閸╄桨绨弮鍫曟？閻ㄥ嫬閽╁鎴炲腹鏉?
     */
    drawFlowParticles(startX, startY, cp1x, cp1y, cp2x, cp2y, endX, endY, connection, dt) {
        // 鍒濆鍖栫矑瀛愮郴缁?
        if (!connection.particles) {
            connection.particles = [];
            for (let i = 0; i < 5; i++) {
                connection.particles.push({
                    t: i / 5,
                    speed: 0.005 + Math.random() * 0.003
                });
            }
        }

        // 缂傛挸鐡ㄧ划鎺戠摍閸欐垵鍘滅划鍓т紥閿涘牊瀵滆ぐ鎾冲 scale 閸欘亜鍨卞杞扮濞嗏槄绱?
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
        const timeScale = dt / 16; // 娴?60fps 娑撳搫鐔€閸戝棗缍婃稉鈧崠?

        connection.particles.forEach(particle => {
            // 閵嗘劒鎱ㄦ径宥冣偓鎴濈唨娴?dt 閹恒劏绻橀敍宀勪缉閸忓秴鎶氶悳鍥ㄥ皾閸斻劌顕遍懛鎾偓鐔峰娑撳秴娼?
            particle.t += particle.speed * timeScale;
            if (particle.t > 1) particle.t = 0;

            // 鐠侊紕鐣荤拹婵嗩敚鐏忔梹娲哥痪澶哥瑐閻ㄥ嫮鍋?
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

            // 缂佹ê鍩楃紓鎾崇摠閻ㄥ嫬褰傞崗澶岀煈鐎?
            this.ctx.drawImage(sprite.canvas, x - sprite.size / 2, y - sprite.size / 2, sprite.size, sprite.size);
        });
    }

    /**
     * 缂佹ê鍩楅崷鍡氼潡閻晛鑸?
     * @param {number} x
     * @param {number} y
     * @param {number} w
     * @param {number} h
     * @param {number|{tl?:number,tr?:number,bl?:number,br?:number}} r 閸楀﹤绶為敍灞藉讲缂佺喍绔存导鐘插弳閹存牗瀵滅憴鎺戝瀻閸掝偅瀵氱€?
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

        // 闂勬劕鍩楅崡濠傜窞閿涘矂妲诲?w/h 鏉堝啫鐨弮璺哄毉閻滈姘﹂崣?
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
     * 鐠囬攱鐪伴柌宥囩帛閿涙碍鐖ｇ拋鎵暰鐢啩璐熼懘蹇ョ礉楠炴儼鐨熸惔锔跨濞?RAF閵?
     * Encoding cleanup: previous comment text was unreadable.
     */
    invalidate() {
        this._dirty = true;
        this._scheduleFrame();
    }

    /**
     * 閸忕厧顔愰崗銉ュ經閿涙氨鐡戞禒铚傜艾 invalidate()閵?
     * Encoding cleanup: previous comment text was unreadable.
     * 娴ｅ棔绗夐崘宥呭灡瀵ょ儤妫ら梽鎰儕閻滎垬鈧?
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
     * 鐟欏棗褰涚憗浣稿閿涙艾鍨介弬顓″Ν閻愯妲搁崥锕€婀ぐ鎾冲閸欘垵顫嬮崠鍝勭厵閸愬拑绱欓崥顐＄閻愬湱绱﹂崘璇х礆閵?
     * @private
     */
    _isNodeInViewport(node) {
        const vw = this._logicalWidth / this.scale;
        const vh = this._logicalHeight / this.scale;
        const vx = this.offset.x;
        const vy = this.offset.y;
        // 閻?100px 缂傛挸鍟块敍宀勪缉閸忓秷绔熺紓妯款梿閸掑洨鐛婇崗鈧?
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
     * 鐎圭偤妾紒妯哄煑娑撯偓鐢佲偓鍌氬枀鐎规碍妲搁崥锔炬埛缂?RAF 瀵邦亞骞嗛妴?
     */
    _drawFrame(now) {
        this._animationFrameId = null;

        if (this._isPaused) {
            return;
        }

        const timestamp = typeof now === 'number' ? now : performance.now();
        const dt = this._lastFrameTime > 0 ? Math.max(0, timestamp - this._lastFrameTime) : 16;
        this._lastFrameTime = timestamp;

        // 濞撳懐鈹栭悽璇茬閿涘牓鈧槒绶崓蹇曠閿?
        this.ctx.clearRect(0, 0, this._logicalWidth, this._logicalHeight);

        // 缂佹ê鍩楃純鎴炵壐
        this.drawGrid();

        // 缂佹ê鍩楁潻鐐村复缁惧尅绱檇t 閻劋绨划鎺戠摍閸斻劎鏁鹃敍?
        for (const conn of this.connections) {
            if (this._isConnectionVisible(conn)) {
                this.drawConnection(conn, dt);
            }
        }

        // 缂佹ê鍩楁稉瀛樻鏉╃偟鍤?
        if (this.isConnecting) {
            this.drawTempConnection();
        }

        // 缂佹ê鍩楅懞鍌滃仯閿涘牐顫嬮崣锝堫梿閸擃亷绱?
        for (const node of this.nodes.values()) {
            if (this._isNodeInViewport(node)) {
                this.drawNode(node);
            }
        }

        // 缂佹ê鍩楅幃顒€浠犵粩顖氬經妤傛ü瀵?
        if (this.hoveredPort && !this.isConnecting) {
            this.drawPortHighlight(this.hoveredPort);
        }

        // 缂佹ê鍩楃亸蹇撴勾閸?
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
        if (!type) return 'Any';
        // 婵″倹鐏夐崥搴ｎ伂娴肩姾绻冮弶銉ф畱閺勵垱鐏囨稉鐐殶鐎涙绱濋崚娆庣箽閹镐椒绗夐崣姗堢礉閸氬海顏?JsonStringEnumConverter 閸欘垯浜掔憴锝嗙€?
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
     * Encoding cleanup: previous comment text was unreadable.
     * 閸氬海顏?Program.cs 闁板秶鐤?JsonNamingPolicy.CamelCase閿涘本澧嶆禒銉ョ箑妞よ濞囬悽銊ョ毈妞圭厧鍢?
     */
    serialize() {
        // 閵嗘劒鎱ㄦ径宥冣偓鎴濆帥绾喕绻氶幍鈧張澶庡Ν閻愬湱娈戠粩顖氬經闁姤婀佺粙鍐茬暰閻?ID
        // 鏉╂瑦鐗?operators 閸?connections 闁垝绱版担璺ㄦ暏閻╃鎮撻惃?ID
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
        const operators = Array.from(this.nodes.values()).map(node => ({
            id: node.id,
            name: node.title,
            type: this.normalizeOperatorType(node.type),
            x: node.x,
            y: node.y,
            inputPorts: (node.inputs || []).map(p => ({
                id: p.id || p.Id || this.generateUUID(), // 閵嗘劒鎱ㄦ径宥冣偓鎴濇倱閺冭埖顥呴弻銉ャ亣鐏忓繐鍟?
                name: p.name,
                dataType: this.normalizePortType(p.type), // PortDataType enum
                direction: 0, // Input
                isRequired: Boolean(p.isRequired ?? p.IsRequired ?? false)
            })),
            outputPorts: (node.outputs || []).map(p => ({
                id: p.id || p.Id || this.generateUUID(), // 閵嗘劒鎱ㄦ径宥冣偓鎴濇倱閺冭埖顥呴弻銉ャ亣鐏忓繐鍟?
                name: p.name,
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
        }));

        // Encoding cleanup: previous comment text was unreadable.
        const debug = flowDebugEnabled();
        if (debug) {
            console.log('[FlowCanvas serialize] === START ===');
            console.log('[FlowCanvas serialize] Raw connections count:', this.connections.length);
            console.log('[FlowCanvas serialize] Nodes in canvas:', Array.from(this.nodes.keys()));
        }

        const connections = this.connections
            .filter(conn => {
                // 鏉╁洦鎶ら幒澶嬫￥閺佸牏娈戞潻鐐村复閿涘澃ource 閹?target 娑撹櫣鈹栭妴涔絥defined 閹存牜鈹朑UID閿?
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

                // 閵嗘劒鎱ㄦ径宥冣偓鎴炲潑閸旂姷顏崣锝囧偍瀵洝绔熼悾灞绢梾閺屻儻绱濋獮璺烘倱閺冭埖顥呴弻?id/Id 鐏炵偞鈧?
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
            .filter(conn => conn !== null); // 鏉╁洦鎶ら幒澶嬫￥閺佸牏娈戞潻鐐村复

        // UpdateFlowRequest 閺堢喐婀滈惃鍕波閺?(camelCase 娴兼俺顫﹂崥搴ｎ伂閼奉亜濮╅弰鐘茬殸)
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
     * 閸欏秴绨崚妤€瀵插ù浣衡柤閺佺増宓?
     */
    deserialize(data) {
        if (!data) return;
        this.clear(true);

        // 閺€顖涘瘮婢舵氨顫掑畵灞筋殰缂佹挻鐎?(閸氬海顏?DTO 閸欘垵鍏橀崠鍛邦棅閸?project.flow 娑?
        const flowData = data.project?.flow || data.flow || data;

        // 婢跺嫮鎮婇崚妤勩€冪仦鐐粹偓?(妞圭厧鍢?鐢洘鏌夐崡?閺冄呭 nodes 闁?
        const operators = flowData.operators || flowData.Operators || flowData.nodes || [];
        const connections = flowData.connections || flowData.Connections || [];

        if (flowDebugEnabled()) {
            console.log('[FlowCanvas] Deserialize start: operators', operators.length, 'connections', connections.length);
        }

        if (operators) {
            operators.forEach(op => {
                // 闁倿鍘ら崥搴ｎ伂 DTO (PascalCase) 閹存牕澧犵粩?(camelCase)
                const id = op.id ?? op.Id;
                const type = this.normalizeOperatorType(op.type ?? op.Type);
                const title = op.name ?? op.Name ?? op.title ?? type;

                // 閵嗘劒鎱ㄦ径宥冣偓鎴炵垼閸戝棗瀵茬粩顖氬經閺佺増宓侀敍宀€绮烘稉鈧担璺ㄦ暏鐏忓繐鍟撶仦鐐粹偓褍鎮曢敍鍧昫/name/type閿?
                const normalizePort = (p) => ({
                    id: p.id || p.Id || this.generateUUID(),
                    name: p.name || p.Name,
                    displayName: p.displayName || p.DisplayName || p.name || p.Name,
                    description: p.description || p.Description || '',
                    type: p.type || p.Type || p.dataType || p.DataType || 0,
                    dataType: p.dataType || p.DataType || p.type || p.Type || 0,
                    isRequired: Boolean(p.isRequired ?? p.IsRequired ?? false)
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
                // 鏉╁洦鎶ら幒澶嬫￥閺佸牏娈戞潻鐐村复閿涘澃ource 閹?target 娑撹櫣鈹栭妴涔絥defined 閹存牜鈹朑UID閿?
                const isValidSource = conn.source && conn.source !== '00000000-0000-0000-0000-000000000000';
                const isValidTarget = conn.target && conn.target !== '00000000-0000-0000-0000-000000000000';
                if (!isValidSource || !isValidTarget) {
                    console.warn('[FlowCanvas] Skipping invalid connection:', conn);
                }
                return isValidSource && isValidTarget;
            });

            // 閸欏秴绨崚妤€瀵查崥搴＄箑妞ゅ鍣稿楦跨箾缁捐法鍌ㄥ鏇礉閸氾箑鍨崥搴ｇ敾 O(1) 閺屻儴顕楁径杈ㄦ櫏
            this._rebuildConnectionIndex();
        }

        this.invalidate();
        this.markFlowStructureChanged('deserialize');
    }

    /**
     * 缂佹ê鍩楃粩顖氬經妤傛ü瀵掗弫鍫熺亯
     * @param {{nodeId: string, portIndex: number, isOutput: boolean, hasConnection: boolean}} port
     */
    drawPortHighlight(port) {
        const pos = this.getPortPosition(port.nodeId, port.portIndex, port.isOutput);
        if (!pos) return;

        // 缂佹ê鍩楁妯瑰瘨閸﹀棛骞?
        this.ctx.beginPath();
        this.ctx.arc(pos.x, pos.y, 8 * this.scale, 0, Math.PI * 2);
        this.ctx.strokeStyle = port.isOutput ? '#1890ff' : '#52c41a';
        this.ctx.lineWidth = 2 * this.scale;
        this.ctx.stroke();

        // 缂佹ê鍩楅崣鎴濆帨閺佸牊鐏?
        this.ctx.beginPath();
        this.ctx.arc(pos.x, pos.y, 12 * this.scale, 0, Math.PI * 2);
        this.ctx.fillStyle = port.isOutput
            ? 'rgba(24, 144, 255, 0.2)'
            : 'rgba(82, 196, 26, 0.2)';
        this.ctx.fill();

        // 閵嗘劖鏌婃晶鐐偓鎴濐洤閺嬫粎顏崣锝呭嚒鏉╃偞甯撮敍宀€绮崚鑸垫焽瀵偓閹稿洨銇?
        if (port.hasConnection) {
            // 缂佹ê鍩楃痪銏ｅ閾忔氨鍤庨崷鍡欏箚鐞涖劎銇氶崣顖涙焽瀵偓
            this.ctx.beginPath();
            this.ctx.arc(pos.x, pos.y, 14 * this.scale, 0, Math.PI * 2);
            this.ctx.strokeStyle = 'rgba(231, 76, 60, 0.6)'; // 缁俱垼澹婇崡濠団偓蹇旀
            this.ctx.lineWidth = 2 * this.scale;
            this.ctx.setLineDash([4 * this.scale, 2 * this.scale]);
            this.ctx.stroke();
            this.ctx.setLineDash([]);
        }
    }

    /**
     * 婢跺嫮鎮婃Η鐘崇垼閹稿绗?
     */
    handleMouseDown(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        // 閺囧瓨鏌婃Η鐘崇垼娴ｅ秶鐤?
        this.mousePosition = { x, y };

        // 妫ｆ牕鍘涘Λ鈧ù瀣Ц閸氾妇鍋ｉ崙璁崇啊缁旑垰褰?
        const port = this.getPortAt(x, y);
        if (port) {
            if (port.isOutput) {
                // 閵嗘劖鏌婃晶鐐偓鎴烆梾閺屻儴绶崙铏诡伂閸欙絾妲搁崥锕€鍑￠張澶庣箾閹?
                const existingConns = this.getConnectionsAtPort(port.nodeId, port.portIndex, true);
                
                if (existingConns.length > 0) {
                    // 閺傤厼绱戠拠銉ь伂閸欙絿娈戦幍鈧張澶庣箾閹?
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
                    // 濞屸剝婀佹潻鐐村复閿涘奔绮犳潏鎾冲毉缁旑垰褰涘鈧慨瀣箾缁?
                    this.startConnection(port.nodeId, port.portIndex);
                }
                return;
            } else if (this.isConnecting) {
                // 娴犲氦绶崗銉ь伂閸欙絽鐣幋鎰箾缁?
                this.finishConnection(port.nodeId, port.portIndex);
                return;
            } else {
                // 閵嗘劖鏌婃晶鐐偓鎴犲仯閸戞槒绶崗銉ь伂閸欙絾妞傚Λ鈧弻銉︽Ц閸氾箑鍑￠張澶庣箾閹?
                const existingConn = this.getConnectionAtPort(port.nodeId, port.portIndex, false);
                
                if (existingConn) {
                    // 閺傤厼绱戠拠銉ㄧ翻閸忋儳顏崣锝囨畱鏉╃偞甯?
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

        // 婵″倹鐏夐崷銊ㄧ箾缁捐法濮搁幀浣风稻閻愮懓鍤禍鍡欌敄閻ц棄顦╅敍灞藉絿濞戝牐绻涚痪?
        if (this.isConnecting) {
            this.cancelConnection();
            return;
        }

        // 閺屻儲澹橀悙鐟板毊閻ㄥ嫯濡悙?
        for (const [id, node] of this.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                this.selectedNode = id;
                // 娴犲懎涔忛柨顔兼儙閸斻劏濡悙瑙勫珛閹锋枻绱濋柆鍨帳閸欐娊鏁懣婊冨礋閺堢喖妫跨憴锕€褰傞幇蹇擃樆閹锋牗瀚?
                if (e.button === 0) {
                    this.draggedNode = id;
                    this.dragOffset = { x: x - node.x, y: y - node.y };
                }

                // 鐟欙箑褰傞懞鍌滃仯闁鑵戦崶鐐剁殶
                if (this.onNodeSelected) {
                    this.onNodeSelected(node);
                }

                this.render();
                return;
            }
        }

        this.selectedNode = null;

        // 鐟欙箑褰傞崣鏍ㄧХ闁鑵戦崶鐐剁殶
        if (this.onNodeSelected) {
            this.onNodeSelected(null);
        }

        this.render();
    }

    /**
     * 婢跺嫮鎮婇崣灞藉毊娴滃娆㈤敍鍫滃瘜鐟曚胶鏁ゆ禍搴＄摍閸ユ儳鐫嶅鈧粵澶愮彯缁狙傛唉娴滄帪绱?
     */
    handleDoubleClick(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        // 閺屻儲澹橀崣灞藉毊閻ㄥ嫯濡悙?
        for (const [id, node] of this.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                
                // 瑙﹀彂鍙屽嚮浜嬩欢鍥炶皟
                if (this.onNodeDoubleClicked) {
                    this.onNodeDoubleClicked(node);
                }
                return;
            }
        }
    }

    /**
     * 婢跺嫮鎮婃Η鐘崇垼缁夎濮?
     */
    handleMouseMove(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        this.mousePosition = { x, y };

        if (this.isConnecting) {
            const port = this.getPortAt(x, y);
            const nextHoveredPort = port && !port.isOutput && port.nodeId !== this.connectingFrom?.nodeId
                ? port
                : null;
            const nextCursor = nextHoveredPort ? 'pointer' : 'crosshair';
            if (!this._isSamePortState(this.hoveredPort, nextHoveredPort) || this.canvas.style.cursor !== nextCursor) {
                this.hoveredPort = nextHoveredPort;
                this.canvas.style.cursor = nextCursor;
            }
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
            this.hoveredPort = null;
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

        if (!this._isSamePortState(this.hoveredPort, nextHoveredPort) || this.canvas.style.cursor !== nextCursor) {
            this.hoveredPort = nextHoveredPort;
            this.canvas.style.cursor = nextCursor;
            this.invalidate();
        }
    }

    handleMouseUp() {
        this.draggedNode = null;
        if (!this.isConnecting) {
            this.canvas.style.cursor = 'default';
        }
    }

    /**
     * 婢跺嫮鎮婂姘崇枂缂傗晜鏂?
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
            
            // 浠ラ紶鏍囦负涓績缂╂斁
            this.offset.x += mouseX / this.scale - mouseX / newScale;
            this.offset.y += mouseY / this.scale - mouseY / newScale;
            this.scale = newScale;
            this.invalidate();
            this.notifyViewStateChanged();
        }
    }

    /**
     * 濞撳懐鈹栭悽璇茬
     */
    /**
     * 濡偓濞村绱堕弽鍥︾秴缂冾喗妲搁崥锕€婀潻鐐村复缁惧じ绗?
     * Encoding cleanup: previous comment text was unreadable.
     * Encoding cleanup: previous comment text was unreadable.
     * @param {Object} connection - 鏉╃偞甯寸痪鍨嚠鐠?
     * @returns {boolean}
     */
    isPointOnConnection(x, y, connection) {
        // 娴ｈ法鏁?getPortPosition 閼惧嘲绶遍崙鍡欌€樼粩顖氬經娴ｅ秶鐤嗛敍宀冣偓灞肩瑝閺勵垳鏁ら懞鍌滃仯娑擃厼绺炬潻鎴滄妧
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
        // 閸婃帒绨柆宥呭坊閿涘奔绱崗鍫モ偓澶嬪閺堚偓娑撳﹪娼伴惃鍕箾閹恒儳鍤?
        for (let i = this.connections.length - 1; i >= 0; i--) {
            if (this.isPointOnConnection(x, y, this.connections[i])) {
                return this.connections[i];
            }
        }
        return null;
    }

    /**
     * 婢跺嫮鎮婇崣鎶芥暛閼挎粌宕?
     */
    /**
     * 婢跺嫮鎮婇柨顔炬磸娴滃娆?
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
    // 闂冭埖顔岄崶娑橆杻瀵尨绱伴崣鎶芥暛閼挎粌宕熼崝鐔诲厴
    // ==========================================================================

    /**
     * 澶勭悊鍙抽敭鑿滃崟浜嬩欢
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
        this.showNodeContextMenu(e.clientX, e.clientY, clickedNode.id);
    }

    /**
     * 鏄剧ず鑺傜偣鍙抽敭鑿滃崟
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
     * 濞撳懐鈹栭悽璇茬
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
        this.nodes.clear();
        this.connections = [];
        this._connectionById.clear();
        this._connectionsByOutputPort.clear();
        this._connectionByInputPort.clear();
        this.selectedNode = null;
        this.draggedNode = null;
        this.selectedConnection = null;
        this._markNodesBoundsDirty();
        this.invalidate();
        if (!silent) {
            this.markFlowStructureChanged('clear');
        }
    }

    /**
     * 鏉╂劘顢戦崡鏇氶嚋閼哄倻鍋?
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
     * 澶嶅埗鑺傜偣
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
        return newNode;
    }

    /**
     * 閸掔娀娅庨懞鍌滃仯閿涘牆褰搁柨顔垮綅閸楁洖寮?API 閸忕厧顔愰崗銉ュ經閿涘鈧倸顫欓幍妯肩舶 removeNode 娴犮儰绻氶悾?_systemNode 娣囨繃濮㈡稉搴ｅ偍瀵洖鎮撳銉ｂ偓?
     */
    deleteNode(nodeId) {
        return this.removeNode(nodeId);
    }

    /**
     * 鍒囨崲鑺傜偣绂佺敤鐘舵€?
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
     * 閺勫墽銇氶懞鍌滃仯鐢喖濮?
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
     * 閸掓繂顫愰崠鏍х毈閸︽澘娴?
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
        this.minimap.style.background = 'rgba(15, 36, 53, 0.9)';
        this.minimap.style.border = '1px solid rgba(255, 255, 255, 0.1)';
        this.minimap.style.borderRadius = '8px';
        this.minimap.style.overflow = 'hidden';
        this.minimap.style.zIndex = '100';
        this.minimap.style.boxShadow = '0 4px 16px rgba(0, 0, 0, 0.3)';

        this.minimapToggle = document.createElement('button');
        this.minimapToggle.type = 'button';
        this.minimapToggle.title = '折叠/展开小地图';
        this.minimapToggle.textContent = '-';
        this.minimapToggle.style.position = 'absolute';
        this.minimapToggle.style.right = '4px';
        this.minimapToggle.style.top = '4px';
        this.minimapToggle.style.zIndex = '2';
        this.minimapToggle.style.width = '22px';
        this.minimapToggle.style.height = '22px';
        this.minimapToggle.style.border = '1px solid rgba(255,255,255,0.16)';
        this.minimapToggle.style.borderRadius = '4px';
        this.minimapToggle.style.background = 'rgba(15, 36, 53, 0.82)';
        this.minimapToggle.style.color = '#fff';
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

            cacheCtx.fillStyle = node.disabled ? '#666' : (node.color || '#1890ff');
            cacheCtx.fillRect(x, y, w, h);

            if (node.id === this.selectedNode) {
                cacheCtx.strokeStyle = '#fff';
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

        ctx.strokeStyle = 'rgba(231, 76, 60, 0.8)';
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
