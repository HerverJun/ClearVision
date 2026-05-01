/**
 * Stable facade around FlowCanvas.
 *
 * The canvas engine stays untouched; feature modules depend on this smaller API.
 */
class FlowCanvasAdapter {
    constructor(flowCanvas, options = {}) {
        if (!flowCanvas) {
            throw new Error('FlowCanvas instance is required.');
        }

        this.canvas = flowCanvas;
        this.eventBus = options.eventBus || null;
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
            revision: this.getRevision(),
            flow: this.serialize()
        });
    }
}

function createFlowCanvasAdapter(flowCanvas, options = {}) {
    return new FlowCanvasAdapter(flowCanvas, options);
}

export { FlowCanvasAdapter, createFlowCanvasAdapter };
export default FlowCanvasAdapter;
