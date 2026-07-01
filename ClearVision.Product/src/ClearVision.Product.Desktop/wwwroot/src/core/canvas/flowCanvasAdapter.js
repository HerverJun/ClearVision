/**
 * Stable facade around FlowCanvas.
 *
 * The canvas engine stays untouched; feature modules depend on this smaller API.
 */
import FlowCanvas from './flowCanvas.js';

const hostedAdapters = new Map();

function deepClone(value) {
    if (value === null || value === undefined) {
        return value;
    }

    if (typeof structuredClone === 'function') {
        return structuredClone(value);
    }

    return JSON.parse(JSON.stringify(value));
}

function getParameterName(parameter) {
    return parameter?.name ?? parameter?.Name ?? '';
}

function hasOwn(object, key) {
    return Object.prototype.hasOwnProperty.call(object, key);
}

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

    getSnapshot() {
        const flow = deepClone(this.serialize());
        const selectedNodeId = this.canvas.selectedNode || null;
        const selectedNode = selectedNodeId ? this.canvas.nodes?.get?.(selectedNodeId) : null;
        const selectionState = typeof this.canvas.getSelectionState === 'function'
            ? this.canvas.getSelectionState()
            : {
                selectedNodeId,
                selectedConnectionId: this.canvas.selectedConnection?.id || null,
                selectionRevision: 0,
                flowRevision: this.getRevision()
            };

        return {
            flowRevision: this.getRevision(),
            selectionRevision: selectionState.selectionRevision ?? 0,
            selectedNodeId,
            flow,
            selectedNode: deepClone(selectedNode)
        };
    }

    deserialize(flow) {
        const result = this.canvas.deserialize(flow);
        this.emitFlowChanged('deserialize');
        return result;
    }

    replaceFlow(flow) {
        const result = this.canvas.deserialize(deepClone(flow));
        this.emitFlowChanged('replaceFlow');
        if (typeof this.canvas.markSelectionChanged === 'function') {
            this.canvas.markSelectionChanged('replaceFlow');
        }
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
        const nextNodeId = nodeId || null;
        if (nextNodeId && !this.canvas.nodes?.has?.(nextNodeId)) {
            return false;
        }

        this.canvas.selectedNode = nextNodeId;
        this.canvas.selectedConnection = null;
        if (typeof this.canvas.markSelectionChanged === 'function') {
            this.canvas.markSelectionChanged('adapter-selectNode');
        }
        if (typeof this.canvas.onNodeSelected === 'function') {
            this.canvas.onNodeSelected(nextNodeId ? this.canvas.nodes.get(nextNodeId) : null);
        }
        this.canvas.render();
        return true;
    }

    patchNodeParameters(nodeId, parameterPatch = {}) {
        const node = this.canvas.nodes?.get?.(nodeId);
        if (!node) {
            return {
                updated: false,
                reason: 'node_not_found',
                missingParameters: []
            };
        }

        const parameters = Array.isArray(node.parameters) ? node.parameters : [];
        const entries = Object.entries(parameterPatch);
        const resolvedEntries = [];
        const missingParameters = [];
        let changed = false;

        for (const [name, value] of entries) {
            const parameter = parameters.find(item =>
                String(getParameterName(item)).toLowerCase() === String(name).toLowerCase());
            if (!parameter) {
                missingParameters.push(name);
                continue;
            }

            resolvedEntries.push([parameter, value]);
        }

        if (missingParameters.length > 0) {
            return {
                updated: false,
                reason: 'parameter_not_found',
                missingParameters
            };
        }

        for (const [parameter, value] of resolvedEntries) {
            const oldValue = parameter.value ?? parameter.Value;
            if (Object.is(oldValue, value)) {
                continue;
            }

            if (hasOwn(parameter, 'value') || !hasOwn(parameter, 'Value')) {
                parameter.value = deepClone(value);
            }
            if (hasOwn(parameter, 'Value')) {
                parameter.Value = deepClone(value);
            }
            changed = true;
        }

        if (changed) {
            this.canvas.render();
            this.markFlowStructureChanged('patchNodeParameters');
            if (typeof this.canvas.markSelectionChanged === 'function') {
                this.canvas.markSelectionChanged('patchNodeParameters');
            }
        }

        return {
            updated: changed,
            reason: changed ? 'updated' : 'no_change',
            missingParameters: []
        };
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

    subscribeStructure(listener) {
        return this.subscribeStructureState(listener);
    }

    subscribeViewState(listener) {
        if (typeof this.canvas.subscribeViewState === 'function') {
            return this.canvas.subscribeViewState(listener);
        }

        return () => {};
    }

    subscribeSelection(listener) {
        if (typeof this.canvas.subscribeSelectionState === 'function') {
            return this.canvas.subscribeSelectionState(listener);
        }

        if (typeof listener === 'function') {
            listener({
                selectedNodeId: this.canvas.selectedNode || null,
                selectedConnectionId: this.canvas.selectedConnection?.id || null,
                selectionRevision: 0,
                flowRevision: this.getRevision(),
                reason: 'initial'
            });
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
    let hostedDisposed = false;
    adapter.dispose = () => {
        if (hostedDisposed) {
            return;
        }

        hostedDisposed = true;
        disposeAdapter();
        if (hostedAdapters.get(key) === adapter) {
            hostedAdapters.delete(key);
        }
    };

    hostedAdapters.set(key, adapter);
    return adapter;
}

export { FlowCanvasAdapter, createFlowCanvasAdapter, createHostedFlowCanvasAdapter };
export default FlowCanvasAdapter;
