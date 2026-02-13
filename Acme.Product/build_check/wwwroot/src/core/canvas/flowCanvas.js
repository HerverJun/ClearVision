/**
 * 流程编辑器画布引擎
 * 负责算子节点的渲染、拖拽、连线
 */

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

        // 网格设置
        this.gridSize = 20;
        this.gridColor = '#e5e5e5'; // 浅色主题网格
        
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

        // 动画帧ID
        this._animationFrameId = null;

        // 选中的连接
        this.selectedConnection = null;

        this.initialize();
    }

    /**
     * 初始化画布
     */
    initialize() {
        this.resize();
        window.addEventListener('resize', this._resizeHandler);

        // 绑定事件
        this.canvas.addEventListener('mousedown', this._mouseDownHandler);
        this.canvas.addEventListener('mousemove', this._mouseMoveHandler);
        this.canvas.addEventListener('mouseup', this._mouseUpHandler);
        this.canvas.addEventListener('wheel', this._wheelHandler);
        this.canvas.addEventListener('contextmenu', this._contextMenuHandler);
        window.addEventListener('keydown', this._keyDownHandler);

        // 开始渲染循环
        this.render();
    }

    /**
     * 销毁画布，清理所有事件监听和动画循环
     */
    destroy() {
        // 停止渲染循环
        if (this._animationFrameId) {
            cancelAnimationFrame(this._animationFrameId);
            this._animationFrameId = null;
        }

        // 移除窗口事件监听
        window.removeEventListener('resize', this._resizeHandler);
        window.removeEventListener('keydown', this._keyDownHandler);

        // 移除画布事件监听
        this.canvas.removeEventListener('mousedown', this._mouseDownHandler);
        this.canvas.removeEventListener('mousemove', this._mouseMoveHandler);
        this.canvas.removeEventListener('mouseup', this._mouseUpHandler);
        this.canvas.removeEventListener('wheel', this._wheelHandler);
        this.canvas.removeEventListener('contextmenu', this._contextMenuHandler);

        // 清理资源
        this.nodes.clear();
        this.connections = [];
        this.selectedNode = null;
        this.draggedNode = null;
        this.selectedConnection = null;
    }

    /**
     * 调整画布大小
     */
    resize() {
        const container = this.canvas.parentElement;
        this.canvas.width = container.clientWidth;
        this.canvas.height = container.clientHeight;
        this.render();
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
        const node = {
            id: this.generateUUID(),
            type,
            x,
            y,
            width: 140,
            height: 60,
            title: config.title || type,
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
        
        this.nodes.set(node.id, node);
        this.render();
        return node;
    }

    /**
     * 删除节点
     */
    removeNode(nodeId) {
        // 删除相关连接
        this.connections = this.connections.filter(
            conn => conn.source !== nodeId && conn.target !== nodeId
        );
        
        this.nodes.delete(nodeId);
        if (this.selectedNode === nodeId) {
            this.selectedNode = null;
        }
        this.render();
    }

    /**
     * 添加连接
     */
    addConnection(sourceId, sourcePort, targetId, targetPort) {
        const connection = {
            id: this.generateUUID(),
            source: sourceId,
            sourcePort,
            target: targetId,
            targetPort
        };
        
        this.connections.push(connection);
        this.render();
        return connection;
    }

    /**
     * 绘制网格
     */
    drawGrid() {
        const { width, height } = this.canvas;
        
        this.ctx.strokeStyle = this.gridColor;
        this.ctx.lineWidth = 1;
        
        // 计算偏移后的起始位置
        const startX = Math.floor(this.offset.x / this.gridSize) * this.gridSize;
        const startY = Math.floor(this.offset.y / this.gridSize) * this.gridSize;
        
        this.ctx.beginPath();
        
        // 垂直线
        for (let x = startX; x < width + this.offset.x; x += this.gridSize) {
            const screenX = (x - this.offset.x) * this.scale;
            this.ctx.moveTo(screenX, 0);
            this.ctx.lineTo(screenX, height);
        }
        
        // 水平线
        for (let y = startY; y < height + this.offset.y; y += this.gridSize) {
            const screenY = (y - this.offset.y) * this.scale;
            this.ctx.moveTo(0, screenY);
            this.ctx.lineTo(width, screenY);
        }
        
        this.ctx.stroke();
    }

    /**
     * 绘制节点
     */
    drawNode(node) {
        const x = (node.x - this.offset.x) * this.scale;
        const y = (node.y - this.offset.y) * this.scale;
        const w = node.width * this.scale;
        const h = node.height * this.scale;

        // 根据状态调整边框颜色
        let borderColor = this.selectedNode === node.id ? node.color : '#434343';
        let borderWidth = this.selectedNode === node.id ? 2 : 1;

        if (node.status === 'running') {
            borderColor = '#1890ff';
            borderWidth = 3;
        } else if (node.status === 'success') {
            borderColor = '#52c41a';
        } else if (node.status === 'error') {
            borderColor = '#f5222d';
        }

        // 节点阴影
        this.ctx.shadowColor = 'rgba(0, 0, 0, 0.3)';
        this.ctx.shadowBlur = 8;
        this.ctx.shadowOffsetX = 2;
        this.ctx.shadowOffsetY = 2;

        // 节点背景
        this.ctx.fillStyle = this.selectedNode === node.id ? '#2c2c2c' : '#1f1f1f';
        this.ctx.strokeStyle = borderColor;
        this.ctx.lineWidth = borderWidth;

        // 绘制圆角矩形
        this.roundRect(x, y, w, h, 6);
        this.ctx.fill();
        this.ctx.stroke();

        // 重置阴影
        this.ctx.shadowColor = 'transparent';

        // 标题栏
        this.ctx.fillStyle = node.color;
        this.ctx.fillRect(x, y, w, 20 * this.scale);

        // 标题文字
        this.ctx.fillStyle = '#ffffff';
        this.ctx.font = `${12 * this.scale}px sans-serif`;
        this.ctx.textAlign = 'center';
        this.ctx.textBaseline = 'middle';
        this.ctx.fillText(node.title, x + w / 2, y + 10 * this.scale);

        // 绘制状态指示器
        if (node.status === 'running') {
            this.drawStatusIndicator(x + w - 15 * this.scale, y + 35 * this.scale, 'running');
        } else if (node.status === 'success') {
            this.drawStatusIndicator(x + w - 15 * this.scale, y + 35 * this.scale, 'success');
        } else if (node.status === 'error') {
            this.drawStatusIndicator(x + w - 15 * this.scale, y + 35 * this.scale, 'error');
        }

        // 绘制端口
        this.drawPorts(node, x, y, w, h);
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
        const portY = y + h / 2;
        
        // 输入端口
        node.inputs.forEach((input, index) => {
            this.ctx.beginPath();
            this.ctx.arc(x, portY, portRadius, 0, Math.PI * 2);
            this.ctx.fillStyle = '#52c41a';
            this.ctx.fill();
            this.ctx.strokeStyle = '#ffffff';
            this.ctx.lineWidth = 1;
            this.ctx.stroke();
        });
        
        // 输出端口
        node.outputs.forEach((output, index) => {
            this.ctx.beginPath();
            this.ctx.arc(x + w, portY, portRadius, 0, Math.PI * 2);
            this.ctx.fillStyle = '#1890ff';
            this.ctx.fill();
            this.ctx.strokeStyle = '#ffffff';
            this.ctx.lineWidth = 1;
            this.ctx.stroke();
        });
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
        const portY = y + h / 2;

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
        const hitRadius = 15 * this.scale; // 点击检测半径

        for (const [nodeId, node] of this.nodes) {
            const nodeScreenX = (node.x - this.offset.x) * this.scale;
            const nodeScreenY = (node.y - this.offset.y) * this.scale;
            const w = node.width * this.scale;
            const h = node.height * this.scale;
            const portY = nodeScreenY + h / 2;

            // 检测输入端口
            for (let i = 0; i < node.inputs.length; i++) {
                const dx = screenX - nodeScreenX;
                const dy = screenY - portY;
                if (Math.sqrt(dx * dx + dy * dy) < hitRadius) {
                    return { nodeId, portIndex: i, isOutput: false };
                }
            }

            // 检测输出端口
            for (let i = 0; i < node.outputs.length; i++) {
                const dx = screenX - (nodeScreenX + w);
                const dy = screenY - portY;
                if (Math.sqrt(dx * dx + dy * dy) < hitRadius) {
                    return { nodeId, portIndex: i, isOutput: true };
                }
            }
        }

        return null;
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
        console.log('[FlowCanvas] 开始连线，从节点:', nodeId, '端口:', portIndex);
    }

    /**
     * 完成连接创建
     * @param {string} nodeId - 目标节点ID
     * @param {number} portIndex - 目标端口索引
     */
    finishConnection(nodeId, portIndex) {
        if (!this.isConnecting || !this.connectingFrom) return;

        // 验证连接有效性
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
        const connection = this.addConnection(
            this.connectingFrom.nodeId,
            this.connectingFrom.portIndex,
            nodeId,
            portIndex
        );

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
    }

    /**
     * 删除连接
     * @param {string} connectionId - 连接ID
     */
    removeConnection(connectionId) {
        this.connections = this.connections.filter(conn => conn.id !== connectionId);
        this.render();
    }

    /**
     * 绘制临时连线（拖拽过程中）
     */
    drawTempConnection() {
        if (!this.isConnecting || !this.connectingFrom) return;

        const startPos = this.getPortPosition(
            this.connectingFrom.nodeId,
            this.connectingFrom.portIndex,
            true
        );

        if (!startPos) return;

        const endX = (this.mousePosition.x - this.offset.x) * this.scale;
        const endY = (this.mousePosition.y - this.offset.y) * this.scale;

        // 绘制虚线
        this.ctx.beginPath();
        this.ctx.moveTo(startPos.x, startPos.y);

        const controlPoint1X = startPos.x + (endX - startPos.x) / 2;
        const controlPoint1Y = startPos.y;
        const controlPoint2X = startPos.x + (endX - startPos.x) / 2;
        const controlPoint2Y = endY;

        this.ctx.bezierCurveTo(
            controlPoint1X, controlPoint1Y,
            controlPoint2X, controlPoint2Y,
            endX, endY
        );

        this.ctx.strokeStyle = '#1890ff';
        this.ctx.lineWidth = 2 * this.scale;
        this.ctx.setLineDash([5 * this.scale, 5 * this.scale]);
        this.ctx.stroke();
        this.ctx.setLineDash([]);
    }

    /**
     * 绘制连接线
     */
    drawConnection(connection) {
        const sourceNode = this.nodes.get(connection.source);
        const targetNode = this.nodes.get(connection.target);
        
        if (!sourceNode || !targetNode) return;
        
        const startX = (sourceNode.x + sourceNode.width - this.offset.x) * this.scale;
        const startY = (sourceNode.y + sourceNode.height / 2 - this.offset.y) * this.scale;
        const endX = (targetNode.x - this.offset.x) * this.scale;
        const endY = (targetNode.y + targetNode.height / 2 - this.offset.y) * this.scale;
        
        // 绘制贝塞尔曲线
        this.ctx.beginPath();
        this.ctx.moveTo(startX, startY);
        
        const controlPoint1X = startX + (endX - startX) / 2;
        const controlPoint1Y = startY;
        const controlPoint2X = startX + (endX - startX) / 2;
        const controlPoint2Y = endY;
        
        this.ctx.bezierCurveTo(
            controlPoint1X, controlPoint1Y,
            controlPoint2X, controlPoint2Y,
            endX, endY
        );
        
        this.ctx.strokeStyle = '#1890ff';
        this.ctx.lineWidth = 2 * this.scale;
        this.ctx.stroke();
    }

    /**
     * 绘制圆角矩形
     */
    roundRect(x, y, w, h, r) {
        this.ctx.beginPath();
        this.ctx.moveTo(x + r, y);
        this.ctx.lineTo(x + w - r, y);
        this.ctx.quadraticCurveTo(x + w, y, x + w, y + r);
        this.ctx.lineTo(x + w, y + h - r);
        this.ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
        this.ctx.lineTo(x + r, y + h);
        this.ctx.quadraticCurveTo(x, y + h, x, y + h - r);
        this.ctx.lineTo(x, y + r);
        this.ctx.quadraticCurveTo(x, y, x + r, y);
        this.ctx.closePath();
    }

    /**
     * 渲染循环
     */
    render() {
        // 清空画布
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);

        // 绘制网格
        this.drawGrid();

        // 绘制连接线
        this.connections.forEach(conn => this.drawConnection(conn));

        // 绘制临时连线（拖拽过程中）
        if (this.isConnecting) {
            this.drawTempConnection();
        }

        // 绘制节点
        this.nodes.forEach(node => this.drawNode(node));

        // 绘制悬停端口高亮
        if (this.hoveredPort && !this.isConnecting) {
            this.drawPortHighlight(this.hoveredPort);
        }

        this._animationFrameId = requestAnimationFrame(() => this.render());
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
            type: node.type,
            x: node.x,
            y: node.y,
            inputPorts: (node.inputs || []).map(p => ({
                id: p.id || p.Id || this.generateUUID(), // 【修复】同时检查大小写
                name: p.name,
                dataType: p.type || 0, // PortDataType enum
                direction: 0, // Input
                isRequired: false
            })),
            outputPorts: (node.outputs || []).map(p => ({
                id: p.id || p.Id || this.generateUUID(), // 【修复】同时检查大小写
                name: p.name,
                dataType: p.type || 0,
                direction: 1, // Output
                isRequired: false
            })),
            parameters: node.parameters || [],
            isEnabled: true
        }));

        // 构建 Connections 列表 (camelCase)
        console.log('[DEBUG serialize] === START SERIALIZE ===');
        console.log('[DEBUG serialize] Raw connections count:', this.connections.length);
        console.log('[DEBUG serialize] Raw connections:', JSON.stringify(this.connections, null, 2));
        console.log('[DEBUG serialize] Nodes in canvas:', Array.from(this.nodes.keys()));
        
        const connections = this.connections
            .filter(conn => {
                // 过滤掉无效的连接（source 或 target 为空、undefined 或空GUID）
                const isValidSource = conn.source && conn.source !== '00000000-0000-0000-0000-000000000000';
                const isValidTarget = conn.target && conn.target !== '00000000-0000-0000-0000-000000000000';
                if (!isValidSource || !isValidTarget) {
                    console.warn(`[DEBUG serialize] 过滤掉无效连接: source=${conn.source}, target=${conn.target}`);
                }
                return isValidSource && isValidTarget;
            })
            .map(conn => {
                const sourceNode = this.nodes.get(conn.source);
                const targetNode = this.nodes.get(conn.target);

                console.log(`[DEBUG serialize] Processing connection: source=${conn.source}, target=${conn.target}`);
                console.log(`[DEBUG serialize]   sourceNode exists: ${!!sourceNode}, targetNode exists: ${!!targetNode}`);

                // 【修复】添加端口索引边界检查，并同时检查 id/Id 属性
                let sourcePortId = null;
                let targetPortId = null;

                if (sourceNode && conn.sourcePort >= 0 && conn.sourcePort < sourceNode.outputs.length) {
                    const port = sourceNode.outputs[conn.sourcePort];
                    sourcePortId = port?.id || port?.Id;
                    if (!sourcePortId) {
                        console.error(`[DEBUG serialize] 源端口索引 ${conn.sourcePort} 存在但没有ID，生成新ID`);
                        port.id = this.generateUUID(); // 为端口分配ID
                        sourcePortId = port.id;
                    }
                } else {
                    console.error(`[DEBUG serialize] 源端口索引越界: ${conn.sourcePort}, 可用端口数: ${sourceNode?.outputs?.length || 0}`);
                }

                if (targetNode && conn.targetPort >= 0 && conn.targetPort < targetNode.inputs.length) {
                    const port = targetNode.inputs[conn.targetPort];
                    targetPortId = port?.id || port?.Id;
                    if (!targetPortId) {
                        console.error(`[DEBUG serialize] 目标端口索引 ${conn.targetPort} 存在但没有ID，生成新ID`);
                        port.id = this.generateUUID(); // 为端口分配ID
                        targetPortId = port.id;
                    }
                } else {
                    console.error(`[DEBUG serialize] 目标端口索引越界: ${conn.targetPort}, 可用端口数: ${targetNode?.inputs?.length || 0}`);
                }

                console.log(`[DEBUG serialize]   sourcePortId: ${sourcePortId}, targetPortId: ${targetPortId}`);

                // 【修复】如果无法获取端口ID，跳过此连接而不是生成错误的UUID
                if (!sourcePortId || !targetPortId) {
                    console.error(`[DEBUG serialize] 跳过无效连接: sourcePortId=${sourcePortId}, targetPortId=${targetPortId}`);
                    return null;
                }

                const result = {
                    id: conn.id,
                    sourceOperatorId: conn.source,
                    sourcePortId: sourcePortId,
                    targetOperatorId: conn.target,
                    targetPortId: targetPortId
                };

                console.log(`[DEBUG serialize]   Serialized connection:`, JSON.stringify(result));
                return result;
            })
            .filter(conn => conn !== null); // 过滤掉无效的连接

        // UpdateFlowRequest 期望的结构 (camelCase 会被后端自动映射)
        const result = {
            operators: operators,
            connections: connections
        };
        
        console.log('[DEBUG serialize] === FINAL SERIALIZED DATA ===');
        console.log('[DEBUG serialize] Operators count:', operators.length);
        console.log('[DEBUG serialize] Operator IDs:', operators.map(o => o.id));
        console.log('[DEBUG serialize] Connections count:', connections.length);
        console.log('[DEBUG serialize] Connections:', JSON.stringify(connections, null, 2));
        console.log('[DEBUG serialize] === END SERIALIZE ===');
        
        return result;
    }

    /**
     * 反序列化流程数据
     */
    deserialize(data) {
        this.clear();

        // Handle both lowercase (frontend) and uppercase (backend) keys for lists
        const operators = data.operators || data.Operators || data.nodes || [];
        const connections = data.connections || data.Connections || [];

        if (operators) {
            operators.forEach(op => {
                // Adapt backend DTO (PascalCase) or frontend (camelCase) to frontend node
                const id = op.id || op.Id;
                const type = op.type || op.Type;
                const title = op.name || op.Name || op.title || type;
                
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
                    x: op.x || op.X || 0,
                    y: op.y || op.Y || 0,
                    width: 140,
                    height: 60,
                    title: title,
                    inputs: inputs,
                    outputs: outputs,
                    parameters: op.parameters || op.Parameters || [],
                    color: '#1890ff' // Default
                };

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

                let sourcePortIndex = conn.sourcePort || 0;
                let targetPortIndex = conn.targetPort || 0;

                // Find index by Port ID if available (Backend/DTO usually provides IDs)
                if (sourcePortId && sourceNode && sourceNode.outputs) {
                    // Note: Node outputs might be objects with 'Id' or 'id' depending on how they were deserialized above
                    // But we simply copied the array. Let's check the array content structure if it came from DTO
                    // DTO InputPorts/OutputPorts have 'Id'. Frontend 'inputs/outputs' have 'id'.
                    // We need to handle that map above?
                    // Actually, in 'node' construction above, we assigned 'inputs' directly.
                    // If it came from DTO, the objects inside have 'Id', 'Name', etc.
                    // If it came from frontend, they have 'id', 'name'.
                    // We should normalize ports too?
                    // For now, let's just find by checking both 'id' and 'Id'.
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
        }

        this.render();
    }

    /**
     * 绘制端口高亮效果
     * @param {{nodeId: string, portIndex: number, isOutput: boolean}} port
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
                // 从输出端口开始连线
                this.startConnection(port.nodeId, port.portIndex);
                return;
            } else if (this.isConnecting) {
                // 从输入端口完成连线
                this.finishConnection(port.nodeId, port.portIndex);
                return;
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
                this.draggedNode = id;
                this.dragOffset = { x: x - node.x, y: y - node.y };

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
            return;
        }

        // 处理节点拖拽
        if (this.draggedNode) {
            const dragX = x - this.dragOffset.x;
            const dragY = y - this.dragOffset.y;

            const node = this.nodes.get(this.draggedNode);
            if (node) {
                node.x = Math.round(dragX / 10) * 10; // 对齐网格
                node.y = Math.round(dragY / 10) * 10;
            }
            return;
        }

        // 检测端口悬停（改变光标）
        const port = this.getPortAt(x, y);
        if (port) {
            this.canvas.style.cursor = 'pointer';
            this.hoveredPort = port;
        } else {
            this.canvas.style.cursor = 'default';
            this.hoveredPort = null;
        }
    }

    /**
     * 处理鼠标释放
     */
    handleMouseUp() {
        this.draggedNode = null;
    }

    /**
     * 处理滚轮缩放
     */
    handleWheel(e) {
        e.preventDefault();
        
        const delta = e.deltaY > 0 ? 0.9 : 1.1;
        const newScale = Math.max(0.5, Math.min(2, this.scale * delta));
        
        if (newScale !== this.scale) {
            const rect = this.canvas.getBoundingClientRect();
            const mouseX = e.clientX - rect.left;
            const mouseY = e.clientY - rect.top;
            
            // 以鼠标为中心缩放
            this.offset.x += mouseX / this.scale - mouseX / newScale;
            this.offset.y += mouseY / this.scale - mouseY / newScale;
            this.scale = newScale;
        }
    }

    /**
     * 清空画布
     */
    clear() {
        this.nodes.clear();
        this.connections = [];
        this.selectedNode = null;
        this.render();
    }

    /**
     * 检测鼠标位置是否在连接线上
     * @param {number} x - 鼠标X坐标（世界坐标）
     * @param {number} y - 鼠标Y坐标（世界坐标）
     * @param {Object} connection - 连接线对象
     * @returns {boolean}
     */
    isPointOnConnection(x, y, connection) {
        const sourceNode = this.nodes.get(connection.source);
        const targetNode = this.nodes.get(connection.target);

        if (!sourceNode || !targetNode) return false;

        const startX = (sourceNode.x + sourceNode.width - this.offset.x) * this.scale;
        const startY = (sourceNode.y + sourceNode.height / 2 - this.offset.y) * this.scale;
        const endX = (targetNode.x - this.offset.x) * this.scale;
        const endY = (targetNode.y + targetNode.height / 2 - this.offset.y) * this.scale;

        const screenX = (x - this.offset.x) * this.scale;
        const screenY = (y - this.offset.y) * this.scale;

        // 简单的点到线段距离检测（使用贝塞尔曲线的控制点近似）
        const controlPoint1X = startX + (endX - startX) / 2;
        const controlPoint1Y = startY;
        const controlPoint2X = startX + (endX - startX) / 2;
        const controlPoint2Y = endY;

        // 采样贝塞尔曲线上的点
        const samples = 10;
        const threshold = 10 * this.scale;

        for (let t = 0; t <= 1; t += 1 / samples) {
            const mt = 1 - t;
            const px = mt * mt * mt * startX +
                       3 * mt * mt * t * controlPoint1X +
                       3 * mt * t * t * controlPoint2X +
                       t * t * t * endX;
            const py = mt * mt * mt * startY +
                       3 * mt * mt * t * controlPoint1Y +
                       3 * mt * t * t * controlPoint2Y +
                       t * t * t * endY;

            const dx = screenX - px;
            const dy = screenY - py;
            if (Math.sqrt(dx * dx + dy * dy) < threshold) {
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
    handleContextMenu(e) {
        e.preventDefault();

        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;

        // 检查是否右键点击了连接线
        const connection = this.getConnectionAt(x, y);
        if (connection) {
            if (confirm('确定要删除这条连接线吗？')) {
                this.removeConnection(connection.id);
            }
            return;
        }

        // 检查是否右键点击了节点
        for (const [id, node] of this.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                if (confirm(`确定要删除节点 "${node.title}" 吗？`)) {
                    this.removeNode(id);
                }
                return;
            }
        }
    }

    /**
     * 处理键盘事件
     */
    handleKeyDown(e) {
        // Delete 键删除选中的节点或连接线
        if (e.key === 'Delete' || e.key === 'Backspace') {
            if (this.selectedNode) {
                if (confirm('确定要删除选中的节点吗？')) {
                    this.removeNode(this.selectedNode);
                }
            } else if (this.selectedConnection) {
                this.removeConnection(this.selectedConnection.id);
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
}

export default FlowCanvas;
export { FlowCanvas };
