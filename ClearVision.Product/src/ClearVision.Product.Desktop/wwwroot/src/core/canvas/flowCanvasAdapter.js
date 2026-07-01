/**
 * Stable facade around FlowCanvas.
 *
 * The canvas engine stays untouched; feature modules depend on this smaller API.
 */
import FlowCanvas from './flowCanvas.js';

const hostedAdapters = new Map();

class FlowCanvasAdapter {
    constructor(flowCanvas, options = {}) {
        if (!flowCanvas) {
            throw new Error('FlowCanvas instance is required.');
        }

        this.canvas = flowCanvas;
        this.eventBus = options.eventBus || null;
        this.ownsCanvas = options.ownsCanvas === true;
        this.disposed = false;
    }

    get raw() {
        return this.canvas;
    }

    serialize() {
        return this.canvas.serialize();
    }

    deserialize(flow) {
        const result = this.canvas.deserialize(flow);
        this.emitFlowChanged('deserialize');
        return result;
    }

    clear(silent = false) {
        const result = this.canvas.clear(silent);
        if (!silent) {
            this.emitFlowChanged('clear');
        }
        return result;
    }

    addNode(type, x, y, config = {}) {
        const node = this.canvas.addNode(type, x, y, config);
        this.emitFlowChanged('addNode');
        return node;
    }

    selectNode(nodeId) {
        this.canvas.selectedNode = nodeId || null;
        this.canvas.render();
    }

    resize() {
        return this.canvas.resize();
    }

    render() {
        return this.canvas.render();
    }

    getViewState() {
        return {
            selectedNode: this.canvas.selectedNode || null,
            selectedConnection: this.canvas.selectedConnection?.id || null,
            scale: Number.isFinite(this.canvas.scale) ? this.canvas.scale : 1,
            offset: {
                x: Number.isFinite(this.canvas.offset?.x) ? this.canvas.offset.x : 0,
                y: Number.isFinite(this.canvas.offset?.y) ? this.canvas.offset.y : 0
            },
            nodeCount: this.canvas.nodes?.size || 0,
            connectionCount: Array.isArray(this.canvas.connections) ? this.canvas.connections.length : 0
        };
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        if (this.ownsCanvas && typeof this.canvas.destroy === 'function') {
            this.canvas.destroy();
        }
    }

    markFlowStructureChanged(reason = 'adapter') {
        if (typeof this.canvas.markFlowStructureChanged === 'function') {
            this.canvas.markFlowStructureChanged(reason);
        }
        this.emitFlowChanged(reason);
    }

    subscribeStructureState(listener) {
        if (typeof this.canvas.subscribeStructureState === 'function') {
            return this.canvas.subscribeStructureState(listener);
        }

        return () => {};
    }

    subscribeViewState(listener) {
        if (typeof this.canvas.subscribeViewState === 'function') {
            return this.canvas.subscribeViewState(listener);
        }

        return () => {};
    }

    getRevision() {
        return typeof this.canvas.getFlowRevision === 'function'
            ? this.canvas.getFlowRevision()
            : 0;
    }

    getFlowRevision() {
        return this.getRevision();
    }

    get nodes() {
        return this.canvas.nodes;
    }

    get selectedNode() {
        return this.canvas.selectedNode;
    }

    set selectedNode(value) {
        this.canvas.selectedNode = value;
    }

    emitFlowChanged(reason) {
        this.eventBus?.emit?.('flow:changed', {
            reason,
            revision: this.getRevision()
        });
    }
}

function createFlowCanvasAdapter(flowCanvas, options = {}) {
    return new FlowCanvasAdapter(flowCanvas, options);
}

function createHostedFlowCanvasAdapter(canvasId, options = {}) {
    if (typeof canvasId !== 'string' || !canvasId.trim()) {
        throw new Error('Hosted FlowCanvas requires a canvas element id.');
    }

    const key = canvasId.trim();
    const existing = hostedAdapters.get(key);
    if (existing) {
        return existing;
    }

    const canvasElement = document.getElementById(key);
    if (!canvasElement) {
        throw new Error(`Hosted FlowCanvas canvas not found: ${key}`);
    }

    const flowCanvas = new FlowCanvas(key);
    const adapter = new FlowCanvasAdapter(flowCanvas, {
        ...options,
        ownsCanvas: true
    });
    const disposeAdapter = adapter.dispose.bind(adapter);
    adapter.dispose = () => {
        disposeAdapter();
        hostedAdapters.delete(key);
    };

    hostedAdapters.set(key, adapter);
    return adapter;
}

export { FlowCanvasAdapter, createFlowCanvasAdapter, createHostedFlowCanvasAdapter };
export default FlowCanvasAdapter;
