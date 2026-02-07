/**
 * 流程编辑器交互系统
 * 扩展FlowCanvas的拖拽、连线、选择、快捷键功能
 */

import { showToast } from '../shared/components/uiComponents.js';

export class FlowEditorInteraction {
    constructor(flowCanvas) {
        this.canvas = flowCanvas;
        this.isConnecting = false;
        this.connectionStart = null;
        this.connectionEnd = null;
        this.isSelecting = false;
        this.selectionBox = null;
        this.selectionStart = null;
        this.multiSelectedNodes = new Set();
        this.copiedNodes = [];
        this.history = [];
        this.historyIndex = -1;
        this.maxHistorySize = 50;

        this.initialize();
    }

    initialize() {
        // 增强原有事件监听
        this.enhanceEventListeners();
        // 绑定键盘快捷键
        this.bindKeyboardShortcuts();
        // 启用算子库拖拽
        this.enableOperatorLibraryDrag();
        // 初始化历史记录
        this.saveState();
    }

    /**
     * 增强事件监听
     */
    enhanceEventListeners() {
        const originalMouseDown = this.canvas.handleMouseDown.bind(this.canvas);
        const originalMouseMove = this.canvas.handleMouseMove.bind(this.canvas);
        const originalMouseUp = this.canvas.handleMouseUp.bind(this.canvas);

        // 重写鼠标按下事件
        this.canvas.handleMouseDown = (e) => {
            const rect = this.canvas.canvas.getBoundingClientRect();
            const x = (e.clientX - rect.left - this.canvas.offset.x) / this.canvas.scale;
            const y = (e.clientY - rect.top - this.canvas.offset.y) / this.canvas.scale;

            // 检测点击的端口
            const clickedPort = this.getPortAt(x, y);
            
            if (clickedPort) {
                // 开始连线
                this.startConnection(clickedPort, e);
                return;
            }

            // 检测点击的节点
            const clickedNode = this.getNodeAt(x, y);

            if (e.shiftKey || e.ctrlKey) {
                // 多选模式
                if (clickedNode) {
                    this.toggleNodeSelection(clickedNode.id);
                } else {
                    // 开始框选
                    this.startSelection(e);
                }
            } else {
                if (clickedNode) {
                    if (!this.multiSelectedNodes.has(clickedNode.id)) {
                        this.clearSelection();
                        this.selectNode(clickedNode.id);
                    }
                    // 调用原生的拖拽逻辑
                    originalMouseDown(e);
                } else {
                    this.clearSelection();
                    this.startSelection(e);
                }
            }
        };

        // 重写鼠标移动事件
        this.canvas.handleMouseMove = (e) => {
            if (this.isConnecting) {
                this.updateConnectionPreview(e);
            } else if (this.isSelecting) {
                this.updateSelectionBox(e);
            } else {
                originalMouseMove(e);
            }
        };

        // 重写鼠标释放事件
        this.canvas.handleMouseUp = (e) => {
            if (this.isConnecting) {
                this.endConnection(e);
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
    }

    /**
     * 绑定键盘快捷键
     */
    bindKeyboardShortcuts() {
        document.addEventListener('keydown', (e) => {
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
                e.preventDefault();
                this.deleteSelectedNodes();
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
        });
    }

    /**
     * 启用算子库拖拽
     */
    enableOperatorLibraryDrag() {
        const library = document.getElementById('operator-library');
        if (!library) return;

        library.addEventListener('dragstart', (e) => {
            if (e.target.classList.contains('operator-item')) {
                const operatorType = e.target.dataset.type;
                const operatorName = e.target.dataset.name || operatorType;
                e.dataTransfer.setData('application/json', JSON.stringify({
                    type: operatorType,
                    name: operatorName
                }));
            }
        });

        this.canvas.canvas.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
        });

        this.canvas.canvas.addEventListener('drop', (e) => {
            e.preventDefault();
            const data = e.dataTransfer.getData('application/json');
            if (data) {
                try {
                    const operator = JSON.parse(data);
                    const rect = this.canvas.canvas.getBoundingClientRect();
                    const x = (e.clientX - rect.left - this.canvas.offset.x) / this.canvas.scale;
                    const y = (e.clientY - rect.top - this.canvas.offset.y) / this.canvas.scale;

                    this.addOperatorNode(operator.type, operator.name, x, y);
                    this.saveState();
                    showToast(`已添加算子: ${operator.name}`, 'success');
                } catch (err) {
                    console.error('拖拽解析失败:', err);
                }
            }
        });
    }

    /**
     * 添加算子节点
     */
    addOperatorNode(type, title, x, y) {
        const colors = {
            'ImageAcquisition': '#52c41a',
            'Filtering': '#1890ff',
            'EdgeDetection': '#722ed1',
            'Thresholding': '#eb2f96',
            'Morphology': '#fa8c16',
            'BlobAnalysis': '#13c2c2',
            'TemplateMatching': '#f5222d',
            'Measurement': '#2f54eb',
            'DeepLearning': '#a0d911',
            'ResultOutput': '#595959'
        };

        const node = this.canvas.addNode(type, x, y, {
            title: title,
            color: colors[type] || '#1890ff',
            inputs: [{ name: 'in', type: 'any' }],
            outputs: [{ name: 'out', type: 'any' }]
        });

        return node;
    }

    /**
     * 获取端口
     */
    getPortAt(x, y) {
        for (const [id, node] of this.canvas.nodes) {
            // 输入端口
            for (let i = 0; i < node.inputs.length; i++) {
                const input = node.inputs[i];
                const px = node.x;
                const py = node.y + node.height / 2;
                const dist = Math.sqrt(Math.pow(x - px, 2) + Math.pow(y - py, 2));
                if (dist < 10) {
                    return { nodeId: id, portIndex: i, type: 'input', port: input };
                }
            }

            // 输出端口
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

    /**
     * 获取节点
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
        
        const rect = this.canvas.canvas.getBoundingClientRect();
        this.connectionEnd = {
            x: (e.clientX - rect.left - this.canvas.offset.x) / this.canvas.scale,
            y: (e.clientY - rect.top - this.canvas.offset.y) / this.canvas.scale
        };
    }

    /**
     * 更新连线预览
     */
    updateConnectionPreview(e) {
        const rect = this.canvas.canvas.getBoundingClientRect();
        this.connectionEnd = {
            x: (e.clientX - rect.left - this.canvas.offset.x) / this.canvas.scale,
            y: (e.clientY - rect.top - this.canvas.offset.y) / this.canvas.scale
        };
        this.canvas.render();

        // 绘制预览线
        const startNode = this.canvas.nodes.get(this.connectionStart.nodeId);
        if (startNode) {
            const startX = startNode.x + (this.connectionStart.type === 'output' ? startNode.width : 0);
            const startY = startNode.y + startNode.height / 2;

            this.canvas.ctx.beginPath();
            this.canvas.ctx.strokeStyle = '#1890ff';
            this.canvas.ctx.lineWidth = 2;
            this.canvas.ctx.setLineDash([5, 5]);
            this.drawBezierCurve(
                (startX - this.canvas.offset.x) * this.canvas.scale,
                (startY - this.canvas.offset.y) * this.canvas.scale,
                (this.connectionEnd.x - this.canvas.offset.x) * this.canvas.scale,
                (this.connectionEnd.y - this.canvas.offset.y) * this.canvas.scale
            );
            this.canvas.ctx.stroke();
            this.canvas.ctx.setLineDash([]);
        }
    }

    /**
     * 结束连线
     */
    endConnection(e) {
        const rect = this.canvas.canvas.getBoundingClientRect();
        const x = (e.clientX - rect.left - this.canvas.offset.x) / this.canvas.scale;
        const y = (e.clientY - rect.top - this.canvas.offset.y) / this.canvas.scale;

        const endPort = this.getPortAt(x, y);

        if (endPort && endPort.type !== this.connectionStart.type) {
            // 确保从输出连接到输入
            const source = this.connectionStart.type === 'output' ? this.connectionStart : endPort;
            const target = this.connectionStart.type === 'input' ? this.connectionStart : endPort;

            // 检查是否已存在连接
            const exists = this.canvas.connections.some(conn =>
                conn.source === source.nodeId &&
                conn.target === target.nodeId &&
                conn.sourcePort === source.portIndex &&
                conn.targetPort === target.portIndex
            );

            if (!exists && source.nodeId !== target.nodeId) {
                this.canvas.addConnection(source.nodeId, source.portIndex, target.nodeId, target.portIndex);
                this.saveState();
                showToast('连接成功', 'success');
            } else {
                showToast('连接已存在或无效', 'warning');
            }
        }

        this.cancelConnection();
    }

    /**
     * 取消连线
     */
    cancelConnection() {
        this.isConnecting = false;
        this.connectionStart = null;
        this.connectionEnd = null;
        this.canvas.render();
    }

    /**
     * 开始框选
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
            const left = (boxRect.left - canvasRect.left - this.canvas.offset.x) / this.canvas.scale;
            const top = (boxRect.top - canvasRect.top - this.canvas.offset.y) / this.canvas.scale;
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
    selectNode(nodeId) {
        this.multiSelectedNodes.add(nodeId);
        this.canvas.selectedNode = nodeId;
        this.updateSelection();
    }

    /**
     * 切换节点选择
     */
    toggleNodeSelection(nodeId) {
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
    }

    /**
     * 清除选择
     */
    clearSelection() {
        this.multiSelectedNodes.clear();
        this.canvas.selectedNode = null;
        this.updateSelection();
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
        this.canvas.render();
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
                this.copiedNodes.push({ ...node });
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
            const newNode = this.canvas.addNode(node.type, node.x + offset, node.y + offset, {
                title: node.title,
                color: node.color,
                inputs: node.inputs,
                outputs: node.outputs
            });
            this.multiSelectedNodes.add(newNode.id);
        });

        this.saveState();
        showToast(`已粘贴 ${this.copiedNodes.length} 个节点`, 'success');
    }

    /**
     * 删除选中节点
     */
    deleteSelectedNodes() {
        if (this.multiSelectedNodes.size === 0) return;

        const count = this.multiSelectedNodes.size;
        for (const nodeId of this.multiSelectedNodes) {
            this.canvas.removeNode(nodeId);
        }

        this.multiSelectedNodes.clear();
        this.canvas.selectedNode = null;
        this.saveState();
        showToast(`已删除 ${count} 个节点`, 'success');
    }

    /**
     * 保存状态（历史记录）
     */
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
        this.canvas.render();
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
