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
        this.gridColor = '#2a2a2a';
        
        this.initialize();
    }

    /**
     * 初始化画布
     */
    initialize() {
        this.resize();
        window.addEventListener('resize', () => this.resize());
        
        // 绑定事件
        this.canvas.addEventListener('mousedown', this.handleMouseDown.bind(this));
        this.canvas.addEventListener('mousemove', this.handleMouseMove.bind(this));
        this.canvas.addEventListener('mouseup', this.handleMouseUp.bind(this));
        this.canvas.addEventListener('wheel', this.handleWheel.bind(this));
        
        // 开始渲染循环
        this.render();
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
     * 添加节点
     */
    addNode(type, x, y, config = {}) {
        const node = {
            id: `node_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
            type,
            x,
            y,
            width: 140,
            height: 60,
            title: config.title || type,
            inputs: config.inputs || [],
            outputs: config.outputs || [],
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
            id: `conn_${Date.now()}`,
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
        
        // 节点阴影
        this.ctx.shadowColor = 'rgba(0, 0, 0, 0.3)';
        this.ctx.shadowBlur = 8;
        this.ctx.shadowOffsetX = 2;
        this.ctx.shadowOffsetY = 2;
        
        // 节点背景
        this.ctx.fillStyle = this.selectedNode === node.id ? '#2c2c2c' : '#1f1f1f';
        this.ctx.strokeStyle = this.selectedNode === node.id ? node.color : '#434343';
        this.ctx.lineWidth = this.selectedNode === node.id ? 2 : 1;
        
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
        
        // 绘制端口
        this.drawPorts(node, x, y, w, h);
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
        
        // 绘制节点
        this.nodes.forEach(node => this.drawNode(node));
        
        requestAnimationFrame(() => this.render());
    }

    /**
     * 处理鼠标按下
     */
    handleMouseDown(e) {
        const rect = this.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left) / this.scale + this.offset.x;
        const y = (e.clientY - rect.top) / this.scale + this.offset.y;
        
        // 查找点击的节点
        for (const [id, node] of this.nodes) {
            if (x >= node.x && x <= node.x + node.width &&
                y >= node.y && y <= node.y + node.height) {
                this.selectedNode = id;
                this.draggedNode = id;
                this.dragOffset = { x: x - node.x, y: y - node.y };
                this.render();
                return;
            }
        }
        
        this.selectedNode = null;
        this.render();
    }

    /**
     * 处理鼠标移动
     */
    handleMouseMove(e) {
        if (this.draggedNode) {
            const rect = this.canvas.getBoundingClientRect();
            const x = (e.clientX - rect.left) / this.scale + this.offset.x - this.dragOffset.x;
            const y = (e.clientY - rect.top) / this.scale + this.offset.y - this.dragOffset.y;
            
            const node = this.nodes.get(this.draggedNode);
            if (node) {
                node.x = Math.round(x / 10) * 10; // 对齐网格
                node.y = Math.round(y / 10) * 10;
            }
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
     * 序列化流程数据
     */
    serialize() {
        return {
            nodes: Array.from(this.nodes.values()),
            connections: this.connections
        };
    }

    /**
     * 反序列化流程数据
     */
    deserialize(data) {
        this.clear();
        
        if (data.nodes) {
            data.nodes.forEach(node => {
                this.nodes.set(node.id, node);
            });
        }
        
        if (data.connections) {
            this.connections = data.connections;
        }
        
        this.render();
    }
}

export default FlowCanvas;
export { FlowCanvas };
