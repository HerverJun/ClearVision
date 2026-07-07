import { buildOperatorNodeConfig } from '../../shared/operatorVisuals.js';

export const PROPERTY_SIDEBAR_STORAGE_KEY = 'cv_flow_property_sidebar_width';
export const PROPERTY_SIDEBAR_DEFAULT_WIDTH = 380;
export const PROPERTY_SIDEBAR_MIN_WIDTH = 320;
export const PROPERTY_SIDEBAR_MAX_WIDTH = 920;
export const PROPERTY_SIDEBAR_DESKTOP_BREAKPOINT = 768;

const PROPERTY_SIDEBAR_MAX_VIEWPORT_RATIO = 0.62;
const PROPERTY_SIDEBAR_MIN_REMAINING_FLOW_WIDTH = 760;
const DRAGGING_BODY_CLASS = 'property-sidebar-resizing';
const DRAGGING_HANDLE_CLASS = 'is-dragging';
const KEYBOARD_STEP = 16;
const KEYBOARD_STEP_LARGE = 32;

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
    return String(parameter?.name ?? parameter?.Name ?? '').trim();
}

function getParameterValue(parameter) {
    return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue ?? null;
}

function normalizeName(value) {
    return String(value ?? '').trim().toLowerCase();
}

function getPortName(port) {
    return String(port?.name ?? port?.Name ?? '').trim();
}

function getPortDataType(port) {
    return port?.type ?? port?.Type ?? port?.dataType ?? port?.DataType ?? null;
}

function isRectanglePort(port) {
    const dataType = getPortDataType(port);
    return dataType === 6 || normalizeName(dataType) === 'rectangle';
}

function createRectangleRegionMetadataFallback() {
    return {
        type: 'RectangleRegion',
        displayName: '矩形区域',
        category: '几何',
        parameters: [
            { name: 'X', displayName: 'X', dataType: 'int', value: 0, defaultValue: 0, minValue: 0 },
            { name: 'Y', displayName: 'Y', dataType: 'int', value: 0, defaultValue: 0, minValue: 0 },
            { name: 'Width', displayName: '宽度', dataType: 'int', value: 1, defaultValue: 1, minValue: 1 },
            { name: 'Height', displayName: '高度', dataType: 'int', value: 1, defaultValue: 1, minValue: 1 }
        ],
        inputPorts: [],
        outputPorts: [
            { name: 'Rectangle', displayName: '矩形', dataType: 'Rectangle', type: 'Rectangle' }
        ]
    };
}

function mergeParameters(definitionParameters = [], nodeParameters = []) {
    const nodeList = Array.isArray(nodeParameters) ? nodeParameters : [];
    const definitionList = Array.isArray(definitionParameters) ? definitionParameters : [];
    const usedNodeParameters = new Set();

    const merged = definitionList.map(definition => {
        const definitionName = getParameterName(definition);
        const nodeParameter = nodeList.find(parameter =>
            getParameterName(parameter).toLowerCase() === definitionName.toLowerCase());
        if (nodeParameter) {
            usedNodeParameters.add(nodeParameter);
        }

        return {
            ...deepClone(definition),
            value: nodeParameter ? getParameterValue(nodeParameter) : getParameterValue(definition)
        };
    });

    nodeList.forEach(parameter => {
        if (!usedNodeParameters.has(parameter)) {
            merged.push(deepClone(parameter));
        }
    });

    return merged;
}

function getGlobalWindow() {
    return typeof window !== 'undefined' ? window : null;
}

function getDocumentRoot() {
    return typeof document !== 'undefined' ? document.documentElement : null;
}

function getDocumentBody() {
    return typeof document !== 'undefined' ? document.body : null;
}

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.querySelector(target) : null;
    }

    return target;
}

function getStorage() {
    try {
        return window.localStorage;
    } catch {
        return null;
    }
}

function getViewportWidth(viewportWidth) {
    const numericWidth = Number(viewportWidth);
    if (Number.isFinite(numericWidth) && numericWidth > 0) {
        return numericWidth;
    }

    return getGlobalWindow()?.innerWidth || 1280;
}

function readStoredWidthCandidate(storage = getStorage()) {
    if (!storage || typeof storage.getItem !== 'function') {
        return PROPERTY_SIDEBAR_DEFAULT_WIDTH;
    }

    try {
        const rawValue = storage.getItem(PROPERTY_SIDEBAR_STORAGE_KEY);
        if (rawValue == null || rawValue === '') {
            return PROPERTY_SIDEBAR_DEFAULT_WIDTH;
        }

        const parsedWidth = Number(rawValue);
        if (!Number.isFinite(parsedWidth)) {
            return PROPERTY_SIDEBAR_DEFAULT_WIDTH;
        }

        const normalizedWidth = Math.round(parsedWidth);
        if (normalizedWidth < PROPERTY_SIDEBAR_MIN_WIDTH || normalizedWidth > PROPERTY_SIDEBAR_MAX_WIDTH) {
            return PROPERTY_SIDEBAR_DEFAULT_WIDTH;
        }

        return normalizedWidth;
    } catch {
        return PROPERTY_SIDEBAR_DEFAULT_WIDTH;
    }
}

export function getMaxWidth(viewportWidth = getViewportWidth()) {
    const safeViewportWidth = getViewportWidth(viewportWidth);
    const maxByRemainingFlow = Math.max(
        PROPERTY_SIDEBAR_MIN_WIDTH,
        Math.round(safeViewportWidth - PROPERTY_SIDEBAR_MIN_REMAINING_FLOW_WIDTH)
    );

    return Math.min(
        PROPERTY_SIDEBAR_MAX_WIDTH,
        Math.round(safeViewportWidth * PROPERTY_SIDEBAR_MAX_VIEWPORT_RATIO),
        maxByRemainingFlow
    );
}

export function clampWidth(width, viewportWidth = getViewportWidth()) {
    const parsedWidth = Number(width);
    const safeWidth = Number.isFinite(parsedWidth)
        ? Math.round(parsedWidth)
        : PROPERTY_SIDEBAR_DEFAULT_WIDTH;

    return Math.min(
        getMaxWidth(viewportWidth),
        Math.max(PROPERTY_SIDEBAR_MIN_WIDTH, safeWidth)
    );
}

export function readSavedWidth({
    storage = getStorage(),
    viewportWidth = getViewportWidth()
} = {}) {
    return clampWidth(readStoredWidthCandidate(storage), viewportWidth);
}

export function applyWidth({
    root = getDocumentRoot(),
    handle = null,
    width = PROPERTY_SIDEBAR_DEFAULT_WIDTH,
    viewportWidth = getViewportWidth()
} = {}) {
    const nextWidth = clampWidth(width, viewportWidth);

    if (root?.style?.setProperty) {
        root.style.setProperty('--right-sidebar-width', `${nextWidth}px`);
    }

    if (handle?.setAttribute) {
        handle.setAttribute('aria-valuemin', String(PROPERTY_SIDEBAR_MIN_WIDTH));
        handle.setAttribute('aria-valuemax', String(getMaxWidth(viewportWidth)));
        handle.setAttribute('aria-valuenow', String(nextWidth));
    }

    return nextWidth;
}

export class PropertySidebarController {
    constructor({
        handle,
        root = getDocumentRoot(),
        storage = getStorage(),
        getCurrentView = () => 'flow'
    } = {}) {
        this.handle = resolveElement(handle);
        this.root = root;
        this.storage = storage;
        this.getCurrentView = typeof getCurrentView === 'function'
            ? getCurrentView
            : () => 'flow';

        this.currentWidth = null;
        this.preferredWidth = readStoredWidthCandidate(this.storage);
        this.dragState = null;

        this.handlePointerDown = this.handlePointerDown.bind(this);
        this.handlePointerMove = this.handlePointerMove.bind(this);
        this.handlePointerUp = this.handlePointerUp.bind(this);
        this.handleKeyDown = this.handleKeyDown.bind(this);
        this.handleWindowResize = this.handleWindowResize.bind(this);

        this.handle?.addEventListener('pointerdown', this.handlePointerDown);
        this.handle?.addEventListener('keydown', this.handleKeyDown);
        getGlobalWindow()?.addEventListener('resize', this.handleWindowResize);

        this.sync();
    }

    getViewportWidth() {
        return getViewportWidth();
    }

    isDesktopViewport() {
        return this.getViewportWidth() > PROPERTY_SIDEBAR_DESKTOP_BREAKPOINT;
    }

    isEnabled(view = this.getCurrentView()) {
        return view === 'flow' && this.isDesktopViewport();
    }

    sync(view = this.getCurrentView()) {
        const enabled = this.isEnabled(view);

        if (this.handle) {
            this.handle.classList.toggle('hidden', !enabled);
            this.handle.setAttribute('aria-disabled', enabled ? 'false' : 'true');
            this.handle.setAttribute('tabindex', enabled ? '0' : '-1');

            if (enabled) {
                this.handle.removeAttribute('aria-hidden');
            } else {
                this.handle.setAttribute('aria-hidden', 'true');
            }
        }

        if (!enabled) {
            this.stopDragging({ persist: false });
            return this.currentWidth;
        }

        this.currentWidth = applyWidth({
            root: this.root,
            handle: this.handle,
            width: this.preferredWidth,
            viewportWidth: this.getViewportWidth()
        });

        return this.currentWidth;
    }

    handlePointerDown(event) {
        if (!this.isEnabled()) {
            return;
        }

        if (!event.isPrimary || event.button !== 0) {
            return;
        }

        event.preventDefault();

        const baseWidth = this.currentWidth ?? readSavedWidth({
            storage: this.storage,
            viewportWidth: this.getViewportWidth()
        });

        this.dragState = {
            pointerId: event.pointerId,
            startX: event.clientX,
            startWidth: baseWidth
        };

        try {
            this.handle?.setPointerCapture?.(event.pointerId);
        } catch {
            // Synthetic pointer events in tests may not support pointer capture.
        }
        this.handle?.classList.add(DRAGGING_HANDLE_CLASS);
        getDocumentBody()?.classList.add(DRAGGING_BODY_CLASS);

        const globalWindow = getGlobalWindow();
        globalWindow?.addEventListener('pointermove', this.handlePointerMove);
        globalWindow?.addEventListener('pointerup', this.handlePointerUp);
        globalWindow?.addEventListener('pointercancel', this.handlePointerUp);
    }

    handlePointerMove(event) {
        if (!this.dragState || event.pointerId !== this.dragState.pointerId) {
            return;
        }

        event.preventDefault();

        const deltaX = this.dragState.startX - event.clientX;
        this.currentWidth = applyWidth({
            root: this.root,
            handle: this.handle,
            width: this.dragState.startWidth + deltaX,
            viewportWidth: this.getViewportWidth()
        });
    }

    handlePointerUp(event) {
        if (!this.dragState || event.pointerId !== this.dragState.pointerId) {
            return;
        }

        this.stopDragging({
            persist: this.isEnabled(),
            width: this.currentWidth ?? this.dragState.startWidth,
            pointerId: event.pointerId
        });
    }

    handleKeyDown(event) {
        if (!this.isEnabled()) {
            return;
        }

        const step = event.shiftKey ? KEYBOARD_STEP_LARGE : KEYBOARD_STEP;
        let nextWidth = null;

        switch (event.key) {
            case 'ArrowLeft':
                nextWidth = (this.currentWidth ?? this.preferredWidth) + step;
                break;
            case 'ArrowRight':
                nextWidth = (this.currentWidth ?? this.preferredWidth) - step;
                break;
            case 'Home':
                nextWidth = PROPERTY_SIDEBAR_MIN_WIDTH;
                break;
            case 'End':
                nextWidth = getMaxWidth(this.getViewportWidth());
                break;
            default:
                return;
        }

        event.preventDefault();
        this.commitWidth(nextWidth);
    }

    commitWidth(width) {
        const nextWidth = clampWidth(width, this.getViewportWidth());
        this.preferredWidth = nextWidth;
        this.currentWidth = applyWidth({
            root: this.root,
            handle: this.handle,
            width: nextWidth,
            viewportWidth: this.getViewportWidth()
        });

        try {
            this.storage?.setItem(PROPERTY_SIDEBAR_STORAGE_KEY, String(nextWidth));
        } catch {
            // Ignore storage failures and keep runtime width.
        }

        return this.currentWidth;
    }

    stopDragging({
        persist = false,
        width = this.currentWidth,
        pointerId = this.dragState?.pointerId
    } = {}) {
        if (pointerId != null) {
            try {
                this.handle?.releasePointerCapture?.(pointerId);
            } catch {
                // Ignore missing pointer capture on synthetic events.
            }
        }

        const globalWindow = getGlobalWindow();
        globalWindow?.removeEventListener('pointermove', this.handlePointerMove);
        globalWindow?.removeEventListener('pointerup', this.handlePointerUp);
        globalWindow?.removeEventListener('pointercancel', this.handlePointerUp);

        this.dragState = null;
        this.handle?.classList.remove(DRAGGING_HANDLE_CLASS);
        getDocumentBody()?.classList.remove(DRAGGING_BODY_CLASS);

        if (persist) {
            this.commitWidth(width);
        }

        return this.currentWidth;
    }

    handleWindowResize() {
        this.sync();
    }

    destroy() {
        this.stopDragging({ persist: false });
        getGlobalWindow()?.removeEventListener('resize', this.handleWindowResize);
        this.handle?.removeEventListener('pointerdown', this.handlePointerDown);
        this.handle?.removeEventListener('keydown', this.handleKeyDown);

        if (this.handle) {
            this.handle.classList.add('hidden');
            this.handle.classList.remove(DRAGGING_HANDLE_CLASS);
            this.handle.setAttribute('aria-disabled', 'true');
            this.handle.setAttribute('aria-hidden', 'true');
            this.handle.setAttribute('tabindex', '-1');
        }
    }
}

export class PropertyPanelCapabilityAdapter {
    constructor({
        flowCanvasAdapter,
        getOperatorMetadata = () => null
    } = {}) {
        if (!flowCanvasAdapter) {
            throw new Error('PropertyPanelCapabilityAdapter requires a FlowCanvasAdapter.');
        }

        this.flowCanvasAdapter = flowCanvasAdapter;
        this.getOperatorMetadata = typeof getOperatorMetadata === 'function'
            ? getOperatorMetadata
            : () => null;
    }

    getSelectedNodeId() {
        return this.flowCanvasAdapter?.selectedNode || null;
    }

    getNode(nodeId) {
        if (!nodeId) {
            return null;
        }

        return this.flowCanvasAdapter?.nodes?.get?.(nodeId) || null;
    }

    getSelectedOperatorSnapshot(nodeId = this.getSelectedNodeId()) {
        const node = this.getNode(nodeId);
        if (!node) {
            return null;
        }

        const metadata = this.getOperatorMetadata(node.type) || {};
        const displayName = metadata.displayName || metadata.DisplayName || node.title || node.type || '算子';

        return {
            id: node.id,
            type: node.type,
            title: node.title || displayName,
            displayName,
            iconPath: node.iconPath || metadata.iconPath || metadata.IconPath || null,
            color: node.color || null,
            disabled: node.disabled === true,
            inputPorts: node.inputs || metadata.inputPorts || metadata.InputPorts || [],
            outputPorts: node.outputs || metadata.outputPorts || metadata.OutputPorts || [],
            parameters: mergeParameters(metadata.parameters || metadata.Parameters || [], node.parameters || [])
        };
    }

    getSelectedConnectionSnapshot(connectionId = null) {
        const canvas = this.flowCanvasAdapter?.raw || this.flowCanvasAdapter?.canvas || null;
        const selectedConnection = canvas?.selectedConnection || null;
        const connections = Array.isArray(canvas?.connections) ? canvas.connections : [];
        const connection = selectedConnection && (!connectionId || selectedConnection.id === connectionId)
            ? selectedConnection
            : connections.find(item => item?.id === connectionId);

        if (!connection) {
            return null;
        }

        const sourceNode = this.getNode(connection.source);
        const targetNode = this.getNode(connection.target);
        const sourcePort = sourceNode?.outputs?.[connection.sourcePort] || null;
        const targetPort = targetNode?.inputs?.[connection.targetPort] || null;

        return {
            id: connection.id,
            sourceNodeId: connection.source,
            targetNodeId: connection.target,
            sourceTitle: sourceNode?.title || sourceNode?.type || connection.source || '-',
            targetTitle: targetNode?.title || targetNode?.type || connection.target || '-',
            sourcePortName: sourcePort?.name || sourcePort?.Name || `输出 ${Number(connection.sourcePort) + 1}`,
            targetPortName: targetPort?.name || targetPort?.Name || `输入 ${Number(connection.targetPort) + 1}`,
            sourcePortType: sourcePort?.type || sourcePort?.Type || sourcePort?.dataType || sourcePort?.DataType || '-',
            targetPortType: targetPort?.type || targetPort?.Type || targetPort?.dataType || targetPort?.DataType || '-'
        };
    }

    subscribeSelectedNode(listener) {
        if (typeof listener !== 'function') {
            return () => {};
        }

        return this.flowCanvasAdapter?.subscribeSelection?.((state = {}) => {
            listener(this.getSelectedOperatorSnapshot(state.selectedNodeId), state);
        }) || (() => {});
    }

    subscribeFlowChanges(listener) {
        if (typeof listener !== 'function') {
            return () => {};
        }

        return this.flowCanvasAdapter?.subscribeStructureState?.(listener) || (() => {});
    }

    writeParameters(nodeId, values = {}) {
        const snapshot = this.getSelectedOperatorSnapshot(nodeId);
        return this.flowCanvasAdapter.patchNodeParameters(nodeId, values, {
            allowCreateParameters: true,
            parameterDefinitions: snapshot?.parameters || []
        });
    }

    getCanvas() {
        return this.flowCanvasAdapter?.raw || this.flowCanvasAdapter?.canvas || null;
    }

    findPortIndex(ports = [], portName) {
        const normalizedPortName = normalizeName(portName);
        return (Array.isArray(ports) ? ports : [])
            .findIndex(port => normalizeName(getPortName(port)) === normalizedPortName);
    }

    findInputConnection(nodeId, inputPortName) {
        const canvas = this.getCanvas();
        const node = this.getNode(nodeId);
        if (!canvas || !node) {
            return null;
        }

        const targetPortIndex = this.findPortIndex(node.inputs, inputPortName);
        if (targetPortIndex < 0) {
            return null;
        }

        return (Array.isArray(canvas.connections) ? canvas.connections : [])
            .find(connection => connection?.target === nodeId && Number(connection.targetPort) === targetPortIndex) || null;
    }

    getCaliperSearchRegionOperatorSnapshot(caliperNodeId) {
        const binding = this.getCaliperSearchRegionBinding(caliperNodeId);
        if (!binding?.sourceNode || normalizeName(binding.sourceNode.type) !== 'rectangleregion') {
            return null;
        }

        return this.getSelectedOperatorSnapshot(binding.sourceNode.id);
    }

    getCaliperSearchRegionBinding(caliperNodeId) {
        const canvas = this.getCanvas();
        const caliperNode = this.getNode(caliperNodeId);
        if (!canvas || !caliperNode || normalizeName(caliperNode.type) !== 'calipertool') {
            return null;
        }

        const targetPortIndex = this.findPortIndex(caliperNode.inputs, 'SearchRegion');
        if (targetPortIndex < 0) {
            return null;
        }

        const connection = (Array.isArray(canvas.connections) ? canvas.connections : [])
            .find(item => item?.target === caliperNodeId && Number(item.targetPort) === targetPortIndex) || null;
        if (!connection) {
            return {
                caliperNode,
                targetPortIndex,
                connection: null,
                sourceNode: null,
                sourcePortIndex: -1,
                sourcePort: null
            };
        }

        const sourceNode = this.getNode(connection.source);
        const sourcePortIndex = Number(connection.sourcePort);
        const sourcePort = sourceNode?.outputs?.[sourcePortIndex] || null;

        return {
            caliperNode,
            targetPortIndex,
            connection,
            sourceNode,
            sourcePortIndex,
            sourcePort
        };
    }

    getRectangleRegionNodeConfig(values = {}) {
        const metadata = this.getOperatorMetadata('RectangleRegion') || createRectangleRegionMetadataFallback();
        const config = buildOperatorNodeConfig('RectangleRegion', metadata);
        const valueByName = new Map(Object.entries(values).map(([name, value]) => [normalizeName(name), value]));
        config.parameters = (config.parameters || createRectangleRegionMetadataFallback().parameters).map(parameter => {
            const name = getParameterName(parameter);
            if (!valueByName.has(normalizeName(name))) {
                return parameter;
            }

            const value = valueByName.get(normalizeName(name));
            const nextParameter = {
                ...parameter,
                value
            };
            if (Object.prototype.hasOwnProperty.call(parameter, 'Value')) {
                nextParameter.Value = value;
            }

            return nextParameter;
        });

        return config;
    }

    removeNode(nodeId, reason = 'property-panel-adapter-remove-node') {
        if (!nodeId) {
            return false;
        }

        const canvas = this.getCanvas();
        const removeNode = typeof this.flowCanvasAdapter?.removeNode === 'function'
            ? this.flowCanvasAdapter.removeNode.bind(this.flowCanvasAdapter)
            : canvas?.removeNode?.bind(canvas);
        if (typeof removeNode !== 'function') {
            return false;
        }

        const removed = removeNode(nodeId) === true;
        if (removed && typeof this.flowCanvasAdapter?.removeNode !== 'function') {
            this.flowCanvasAdapter?.markFlowStructureChanged?.(reason);
        }

        return removed;
    }

    upsertCaliperSearchRegion(caliperNodeId, values = {}) {
        const binding = this.getCaliperSearchRegionBinding(caliperNodeId);
        if (!binding?.caliperNode || binding.targetPortIndex < 0) {
            return { updated: false, reason: 'search_region_port_not_found' };
        }

        if (binding.connection) {
            if (normalizeName(binding.sourceNode?.type) !== 'rectangleregion' || !isRectanglePort(binding.sourcePort)) {
                return {
                    updated: false,
                    reason: 'search_region_connected_to_non_rectangle_region'
                };
            }

            const result = this.writeParameters(binding.sourceNode.id, values);
            return {
                ...result,
                operator: this.getSelectedOperatorSnapshot(binding.sourceNode.id),
                connection: binding.connection
            };
        }

        const canvas = this.getCanvas();
        const addNode = typeof this.flowCanvasAdapter?.addNode === 'function'
            ? this.flowCanvasAdapter.addNode.bind(this.flowCanvasAdapter)
            : canvas?.addNode?.bind(canvas);
        if (!canvas || !addNode || typeof canvas.addConnection !== 'function') {
            return { updated: false, reason: 'flow_canvas_mutation_unavailable' };
        }

        const regionNode = addNode(
            'RectangleRegion',
            Number(binding.caliperNode.x ?? 0) - 260,
            Number(binding.caliperNode.y ?? 0),
            this.getRectangleRegionNodeConfig(values));
        if (!regionNode) {
            return { updated: false, reason: 'rectangle_region_create_failed' };
        }

        const sourcePortIndex = (regionNode.outputs || []).findIndex(isRectanglePort);
        if (sourcePortIndex < 0) {
            const rolledBack = this.removeNode(regionNode.id, 'caliper-search-region-rollback');
            return {
                updated: false,
                reason: 'rectangle_region_output_not_found',
                operator: regionNode,
                rolledBack
            };
        }

        const connection = canvas.addConnection(regionNode.id, sourcePortIndex, caliperNodeId, binding.targetPortIndex);
        if (!connection) {
            const rolledBack = this.removeNode(regionNode.id, 'caliper-search-region-rollback');
            return {
                updated: false,
                reason: 'search_region_connection_failed',
                operator: regionNode,
                rolledBack
            };
        }

        this.flowCanvasAdapter?.markFlowStructureChanged?.('caliper-search-region-upsert');
        return {
            updated: true,
            reason: 'created',
            operator: this.getSelectedOperatorSnapshot(regionNode.id),
            connection
        };
    }

    selectNode(nodeId) {
        return this.flowCanvasAdapter?.selectNode?.(nodeId) === true;
    }
}

export function createPropertyPanelCapabilityAdapter(options = {}) {
    return new PropertyPanelCapabilityAdapter(options);
}

export default PropertySidebarController;
