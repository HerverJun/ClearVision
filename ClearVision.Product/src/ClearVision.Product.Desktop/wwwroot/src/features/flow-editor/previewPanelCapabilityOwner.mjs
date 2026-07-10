import {
    buildPreviewSummaryItems,
    formatPreviewOutputValue
} from './previewOutputFormatter.mjs';
import {
    buildPreviewInputImageHash
} from './previewCoordinator.js';
import {
    formatPortTypeForMessage
} from '../../core/canvas/portTypeCompatibility.mjs';
import {
    buildRegionInputGuidance
} from './regionInputGuidance.mjs';
import {
    ImagePixelProbe,
    PIXEL_PROBE_DEFAULT_MESSAGE,
    createImageRoiFromPoints,
    mapImagePixelToStagePoint,
    mapImageRoiToStageRect
} from './imagePixelProbe.mjs';
import {
    MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS,
    MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES,
    buildOperatorResultViewModel,
    buildSafeJsonPreview,
    formatByteLength,
    formatResultArtifactMetadata,
    isTextArtifactForResultPanel,
    normalizeArtifactReference,
    redactLocalAbsolutePaths,
    STALE_PREVIEW_MESSAGE
} from './operatorResultViewModel.mjs';

export const PREVIEW_PANEL_CAPABILITY_OWNER_ID = 'preview-panel-capability-v2';
const PIXEL_PROBE_ROI_DRAG_THRESHOLD_PX = 4;
export { buildRegionInputGuidance } from './regionInputGuidance.mjs';

// Preview request and artifact-read route: PreviewPanelCapabilityOwner -> PreviewPanelCapabilityAdapter -> NodePreviewCoordinator.

function deepClone(value) {
    if (value === null || value === undefined) {
        return value;
    }

    if (typeof structuredClone === 'function') {
        return structuredClone(value);
    }

    return JSON.parse(JSON.stringify(value));
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function escapeAttribute(value) {
    return escapeHtml(value);
}

function renderButtonDisabledAttributes(disabled) {
    return disabled ? 'disabled aria-disabled="true"' : 'aria-disabled="false"';
}

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.getElementById(target) : null;
    }

    return target;
}

function getParameterName(parameter) {
    return String(parameter?.name ?? parameter?.Name ?? '').trim();
}

function getParameterValue(parameter) {
    return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue ?? null;
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

function readOwn(source, ...keys) {
    if (!source || typeof source !== 'object') {
        return undefined;
    }

    for (const key of keys) {
        if (Object.prototype.hasOwnProperty.call(source, key)) {
            return source[key];
        }
    }

    return undefined;
}

function normalizeBool(value) {
    return value === true || value === 'true' || value === 1;
}

function getObservationIdentity(state) {
    const observation = readOwn(state, 'observation', 'Observation');
    const identity = readOwn(observation, 'identity', 'Identity');
    return identity && typeof identity === 'object' ? identity : null;
}

function getStateIdentitySignature(state) {
    const identity = getObservationIdentity(state) || {};
    return [
        state?.activeNodeId || '',
        state?.status || '',
        state?.request?.requestKey || '',
        identity.projectId || identity.ProjectId || '',
        identity.targetNodeId || identity.TargetNodeId || '',
        identity.debugSessionId || identity.DebugSessionId || '',
        identity.clientRequestSequence || identity.ClientRequestSequence || '',
        identity.flowRevision || identity.FlowRevision || ''
    ].join('|');
}

function classifyIdlePreviewError(errorMessage) {
    const detail = redactLocalAbsolutePaths(String(errorMessage || '').trim());
    if (!detail) {
        return null;
    }

    const normalized = detail.toLowerCase();

    if (/请先新建|保存\/打开工程|打开工程|未选择工程|project.*required|active projectid|required for execution/.test(normalized)) {
        return {
            reason: 'missing-project',
            label: detail,
            message: detail,
            emptyMessage: detail
        };
    }

    if (/过大|too large|oversize|exceed|payload/.test(normalized)) {
        return {
            reason: 'input-too-large',
            label: '输入图像过大',
            message: '输入图像过大',
            emptyMessage: '输入图像过大，无法生成输出图像'
        };
    }

    if (/高成本|手动|manual|auto.*skip|自动.*跳过|刷新预览|真实取帧|\bai\b|\bocr\b|模板|特征匹配|matching/.test(normalized)) {
        return {
            reason: 'manual-required',
            label: '需手动预览',
            message: '需手动预览',
            emptyMessage: '需手动预览后生成输出图像'
        };
    }

    if (/文件路径|采集源|输入图|相机|file path|camera|input image|missing input|missing source/.test(normalized)) {
        return {
            reason: 'missing-input',
            label: '缺输入图或采集源',
            message: detail,
            emptyMessage: '缺输入图或采集源，无法生成输出图像'
        };
    }

    return {
        reason: 'not-run',
        label: '预览未运行',
        message: '预览未运行',
        emptyMessage: '预览未运行，暂无输出图像'
    };
}

function getStatusLabel(state, belongsToSelectedNode, nodeDeleted, stale = false) {
    if (nodeDeleted) {
        return {
            kind: 'deleted',
            label: '节点已删除',
            message: '节点已删除'
        };
    }

    if (!belongsToSelectedNode) {
        return {
            kind: 'idle',
            label: '等待预览',
            message: '请选择一个算子'
        };
    }

    if (stale) {
        return {
            kind: 'stale',
            label: '需重新预览',
            message: STALE_PREVIEW_MESSAGE
        };
    }

    if (state?.status === 'loading') {
        return {
            kind: 'loading',
            label: '预览中',
            message: '预览中'
        };
    }

    if (state?.status === 'success') {
        return {
            kind: 'success',
            label: '预览完成',
            message: '预览完成'
        };
    }

    if (state?.status === 'blocked') {
        return {
            kind: 'blocked',
            label: '安全拦截',
            message: state?.errorMessage || '预览已安全拦截，正式运行流程时才会执行外部动作。'
        };
    }

    if (state?.status === 'auth-error') {
        return {
            kind: 'auth-error',
            label: '登录状态无效',
            message: state?.errorMessage || '登录状态无效，请重新登录。'
        };
    }

    if (state?.status === 'error') {
        return {
            kind: 'error',
            label: '预览失败',
            message: state?.errorMessage || '预览失败'
        };
    }

    if (state?.status === 'canceled') {
        return {
            kind: 'canceled',
            label: '预览已取消',
            message: '预览已取消'
        };
    }

    if (state?.status === 'idle' && state?.errorMessage) {
        const idleError = classifyIdlePreviewError(state.errorMessage);
        if (idleError) {
            return {
                kind: 'idle-error',
                ...idleError
            };
        }
    }

    return {
        kind: 'idle',
        label: '等待预览',
        message: '等待预览'
    };
}

function normalizeOperatorTitle(operator, liveNode = null) {
    return String(
        operator?.title ||
        operator?.displayName ||
        liveNode?.title ||
        liveNode?.displayName ||
        operator?.type ||
        liveNode?.type ||
        '未命名算子');
}

function normalizeParameterDisplayName(parameter) {
    return String(
        parameter?.displayName ||
        parameter?.DisplayName ||
        parameter?.label ||
        parameter?.Label ||
        parameter?.name ||
        parameter?.Name ||
        '参数').trim();
}

function normalizePortType(value) {
    if (value === 0 || value === '0') {
        return 'image';
    }

    return String(value ?? '').trim().toLowerCase();
}

function hasImageOutputPort(operator, liveNode = null) {
    const outputPorts = Array.isArray(operator?.outputPorts) && operator.outputPorts.length > 0
        ? operator.outputPorts
        : (Array.isArray(liveNode?.outputs) ? liveNode.outputs : []);

    return outputPorts.some(port => {
        const type = port?.dataType ?? port?.DataType ?? port?.type ?? port?.Type;
        return normalizePortType(type) === 'image';
    });
}

function nodeUsesExternalInputImage(node) {
    return node?.type !== 'ImageAcquisition';
}

function hasDiagnosticCode(model, pattern) {
    const diagnostics = Array.isArray(model?.diagnostics) ? model.diagnostics : [];
    return diagnostics.some(item => pattern.test(String(item?.code || item?.source || item?.message || '')));
}

function getPreviewImageEmptyMessage({
    nodeDeleted,
    currentConnection,
    currentNodeId,
    belongsToSelectedNode,
    statusInfo,
    model,
    hasImageOutput
}) {
    if (nodeDeleted) {
        return '节点已删除';
    }

    if (currentConnection) {
        return '连线不产生图像输出，请选择算子节点';
    }

    if (!currentNodeId) {
        return '请选择一个算子';
    }

    if (!belongsToSelectedNode || !model) {
        return '尚未运行预览';
    }

    if (statusInfo.kind === 'stale') {
        return `结果过期：${STALE_PREVIEW_MESSAGE}`;
    }

    if (statusInfo.kind === 'loading') {
        return '预览中...';
    }

    if (statusInfo.kind === 'auth-error') {
        return statusInfo.message || '登录状态无效，请重新登录。';
    }

    if (statusInfo.kind === 'blocked') {
        return statusInfo.message || '预览已安全拦截，正式运行流程时才会执行外部动作。';
    }

    if (statusInfo.kind === 'error') {
        if (hasDiagnosticCode(model, /missing-input|missing-resource|input|resource/i)) {
            return '缺输入图或缺资源，无法生成输出图像';
        }

        return '预览失败，暂无输出图像';
    }

    if (statusInfo.kind === 'canceled') {
        return '预览已取消';
    }

    if (statusInfo.kind === 'idle-error') {
        return statusInfo.emptyMessage || '预览未运行，暂无输出图像';
    }

    if (!hasImageOutput) {
        return '该算子没有图像输出';
    }

    if (statusInfo.kind === 'idle') {
        return '等待预览，暂无输出图像';
    }

    return '预览完成，但没有返回图像输出';
}

export class PreviewPanelCapabilityAdapter {
    constructor({
        flowCanvasAdapter,
        previewCoordinator,
        getOperatorMetadata = () => null,
        getProjectId = () => null,
        getInputImageBase64 = () => null,
        onOpenPreviewImage = () => {}
    } = {}) {
        if (!flowCanvasAdapter) {
            throw new Error('PreviewPanelCapabilityAdapter requires a FlowCanvasAdapter.');
        }
        if (!previewCoordinator) {
            throw new Error('PreviewPanelCapabilityAdapter requires a NodePreviewCoordinator.');
        }

        this.flowCanvasAdapter = flowCanvasAdapter;
        this.previewCoordinator = previewCoordinator;
        this.getOperatorMetadata = typeof getOperatorMetadata === 'function'
            ? getOperatorMetadata
            : () => null;
        this.getProjectIdValue = typeof getProjectId === 'function'
            ? getProjectId
            : () => null;
        this.getInputImageBase64 = typeof getInputImageBase64 === 'function'
            ? getInputImageBase64
            : () => null;
        this.onOpenPreviewImage = typeof onOpenPreviewImage === 'function'
            ? onOpenPreviewImage
            : () => {};
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
            disabled: normalizeBool(node.disabled ?? node.Disabled),
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
            sourceTitle: sourceNode?.title || sourceNode?.type || connection.source || '-',
            targetTitle: targetNode?.title || targetNode?.type || connection.target || '-',
            sourcePortName: sourcePort?.name || sourcePort?.Name || `输出 ${Number(connection.sourcePort) + 1}`,
            targetPortName: targetPort?.name || targetPort?.Name || `输入 ${Number(connection.targetPort) + 1}`
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

    subscribePreviewState(listener) {
        return this.previewCoordinator?.subscribe?.(listener) || (() => {});
    }

    getPreviewState() {
        return this.previewCoordinator?.getState?.() || null;
    }

    setActiveNode(nodeId, options = {}) {
        const node = this.getNode(nodeId);
        if (!node) {
            this.previewCoordinator?.setActiveNode?.(null, options);
            return false;
        }

        this.previewCoordinator?.setActiveNode?.(node, options);
        return true;
    }

    clearActiveNode() {
        this.previewCoordinator?.setActiveNode?.(null, { autoPreview: false });
    }

    requestPreview(options = {}) {
        return this.previewCoordinator?.requestActivePreview?.(options);
    }

    cancelPreview() {
        if (typeof this.previewCoordinator?.cancelPreview === 'function') {
            this.previewCoordinator.cancelPreview();
            return;
        }

        this.previewCoordinator?.cancelActivePreviewRequest?.();
    }

    readArtifactForCurrentState(artifactId, expectedIdentity, options = {}) {
        return this.previewCoordinator?.readArtifactForCurrentState?.(artifactId, expectedIdentity, options);
    }

    selectNode(nodeId) {
        return this.flowCanvasAdapter?.selectNode?.(nodeId) === true;
    }

    getFlowRevision() {
        return this.flowCanvasAdapter?.getFlowRevision?.() ?? this.flowCanvasAdapter?.getRevision?.() ?? 0;
    }

    getProjectId() {
        return this.getProjectIdValue?.() ?? null;
    }

    getInputImageHash(node = this.getNode(this.getSelectedNodeId())) {
        if (!node || !nodeUsesExternalInputImage(node)) {
            return buildPreviewInputImageHash(null);
        }

        const value = this.getInputImageBase64?.();
        if (value && typeof value.then === 'function') {
            return null;
        }

        return buildPreviewInputImageHash(value || null);
    }

    openPreviewImage(imageSource) {
        this.onOpenPreviewImage?.(imageSource);
    }

    getNodes() {
        const nodes = this.flowCanvasAdapter?.nodes;
        if (nodes instanceof Map) {
            return Array.from(nodes.values());
        }
        if (Array.isArray(nodes)) {
            return nodes;
        }
        if (nodes?.values && typeof nodes.values === 'function') {
            return Array.from(nodes.values());
        }

        return [];
    }
}

export class PreviewPanelCapabilityOwner {
    constructor(container, {
        previewAdapter,
        showToast = () => {}
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('PreviewPanelCapabilityOwner requires a container.');
        }
        if (!previewAdapter) {
            throw new Error('PreviewPanelCapabilityOwner requires a preview adapter.');
        }

        this.previewAdapter = previewAdapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.currentOperator = null;
        this.currentNodeId = null;
        this.currentConnection = null;
        this.nodeDeleted = false;
        this.previewState = this.previewAdapter.getPreviewState();
        this.autoPreviewEnabled = true;
        this.manualPreviewPending = false;
        this.manualPreviewRequestToken = 0;
        this.previewImageMode = 'fit';
        this.previewImageSource = this.previewState?.presenter?.outputImageSrc || null;
        this.pixelProbe = new ImagePixelProbe();
        this.pixelProbeStatusText = PIXEL_PROBE_DEFAULT_MESSAGE;
        this.pixelProbeStatusKind = 'default';
        this.pixelProbeImageKey = this.buildPixelProbeImageKey(this.previewState);
        this.pixelProbeLockedPoint = null;
        this.pixelProbeRoiSelection = null;
        this.pixelProbeRoiDraft = null;
        this.pixelProbePointerDown = null;
        this.disposed = false;
        this.unsubscribes = [];
        this.artifactReadAbort = null;
        this.artifactReadToken = 0;
        this.artifactReadState = new Map();
        this.resultIdentitySignature = getStateIdentitySignature(this.previewState);

        this.handleClick = this.handleClick.bind(this);
        this.handleChange = this.handleChange.bind(this);
        this.handlePixelProbePointerMove = this.handlePixelProbePointerMove.bind(this);
        this.handlePixelProbePointerDown = this.handlePixelProbePointerDown.bind(this);
        this.handlePixelProbePointerUp = this.handlePixelProbePointerUp.bind(this);
        this.handlePixelProbePointerCancel = this.handlePixelProbePointerCancel.bind(this);
        this.handlePixelProbePointerLeave = this.handlePixelProbePointerLeave.bind(this);
        this.handlePixelProbeKeyDown = this.handlePixelProbeKeyDown.bind(this);
        this.handlePreviewImageLoad = this.handlePreviewImageLoad.bind(this);

        this.container.dataset.previewPanelOwner = PREVIEW_PANEL_CAPABILITY_OWNER_ID;
        this.container.addEventListener('click', this.handleClick);
        this.container.addEventListener('change', this.handleChange);
        this.container.addEventListener('pointermove', this.handlePixelProbePointerMove);
        this.container.addEventListener('pointerdown', this.handlePixelProbePointerDown);
        this.container.addEventListener('pointerup', this.handlePixelProbePointerUp);
        this.container.addEventListener('pointercancel', this.handlePixelProbePointerCancel);
        this.container.addEventListener('pointerleave', this.handlePixelProbePointerLeave);
        this.container.addEventListener('keydown', this.handlePixelProbeKeyDown);
        this.container.addEventListener('load', this.handlePreviewImageLoad, true);
        if (typeof document !== 'undefined' && document?.addEventListener) {
            document.addEventListener('keydown', this.handlePixelProbeKeyDown);
        }

        this.unsubscribes.push(
            this.previewAdapter.subscribePreviewState(state => this.handlePreviewStateChanged(state)),
            this.previewAdapter.subscribeSelectedNode((operator, state) => this.handleSelectedNodeChanged(operator, state)),
            this.previewAdapter.subscribeFlowChanges(state => this.handleFlowChanged(state))
        );

        this.render();
    }

    handleSelectedNodeChanged(operator, state = {}) {
        if (this.disposed) {
            return;
        }

        this.currentOperator = operator || null;
        this.currentNodeId = operator?.id || null;
        this.currentConnection = this.currentNodeId
            ? null
            : this.previewAdapter.getSelectedConnectionSnapshot?.(state?.selectedConnectionId) || null;
        this.nodeDeleted = false;
        this.manualPreviewPending = false;
        this.manualPreviewRequestToken += 1;
        this.cancelArtifactRead();
        this.artifactReadState.clear();
        this.resetPixelProbeStatus();

        if (!this.currentNodeId) {
            this.previewAdapter.clearActiveNode();
            this.render();
            return;
        }

        this.previewAdapter.setActiveNode(this.currentNodeId, {
            autoPreview: this.autoPreviewEnabled
        });
        this.render();
    }

    handleFlowChanged() {
        if (this.disposed || !this.currentNodeId) {
            return;
        }

        const liveOperator = this.previewAdapter.getSelectedOperatorSnapshot(this.currentNodeId);
        if (!liveOperator) {
            this.nodeDeleted = true;
            this.currentOperator = null;
            this.currentConnection = null;
            this.manualPreviewPending = false;
            this.manualPreviewRequestToken += 1;
            this.resetPixelProbeStatus();
            this.previewAdapter.clearActiveNode();
            this.render();
            return;
        }

        this.currentOperator = liveOperator;
        this.currentConnection = null;
        this.render();
    }

    handlePreviewStateChanged(state) {
        if (this.disposed) {
            return;
        }

        this.previewState = state || null;
        const activeNodeId = this.previewState?.activeNodeId || null;
        const updatesCurrentSelection = !this.currentNodeId || !activeNodeId || activeNodeId === this.currentNodeId;
        if (updatesCurrentSelection) {
            this.manualPreviewPending = false;
            if (this.previewState?.status !== 'loading') {
                this.manualPreviewRequestToken += 1;
            }
        }
        const nextImageSource = this.previewState?.presenter?.outputImageSrc || null;
        if (nextImageSource !== this.previewImageSource) {
            this.previewImageSource = nextImageSource;
            this.previewImageMode = 'fit';
        }
        const nextPixelProbeImageKey = this.buildPixelProbeImageKey(this.previewState);
        if (nextPixelProbeImageKey !== this.pixelProbeImageKey) {
            this.pixelProbeImageKey = nextPixelProbeImageKey;
            this.resetPixelProbeStatus();
        }
        this.resetArtifactReadsIfIdentityChanged();
        this.render();
    }

    buildPixelProbeImageKey(state) {
        return [
            state?.activeNodeId || '',
            getStateIdentitySignature(state),
            state?.presenter?.outputImageSrc || ''
        ].join('|');
    }

    resetPixelProbeStatus(options = {}) {
        const clearSelections = options.clearSelections !== false;
        this.pixelProbe?.reset?.();
        if (clearSelections) {
            this.pixelProbeLockedPoint = null;
            this.pixelProbeRoiSelection = null;
            this.pixelProbeRoiDraft = null;
            this.pixelProbePointerDown = null;
        } else {
            this.pixelProbeRoiDraft = null;
            this.pixelProbePointerDown = null;
        }
        this.pixelProbeStatusText = PIXEL_PROBE_DEFAULT_MESSAGE;
        this.pixelProbeStatusKind = 'default';
        this.syncPixelProbeStatusElement();
        this.syncPixelProbeOverlayElements();
    }

    syncPixelProbeStatusElement() {
        const element = this.container.querySelector?.('[data-role="pixel-probe-status"]');
        if (!element) {
            return;
        }

        element.textContent = this.pixelProbeStatusText || PIXEL_PROBE_DEFAULT_MESSAGE;
        element.dataset.probeState = this.pixelProbeStatusKind || 'default';
        element.setAttribute?.('data-probe-state', this.pixelProbeStatusKind || 'default');
    }

    updatePixelProbeStatus(result) {
        this.pixelProbeStatusText = result?.message || PIXEL_PROBE_DEFAULT_MESSAGE;
        this.pixelProbeStatusKind = result?.kind || 'default';
        this.syncPixelProbeStatusElement();
    }

    getPixelProbeStageFromEvent(event) {
        return event?.target?.closest?.('.preview-capability-image-stage') || null;
    }

    getPixelProbeImage(stage = null) {
        const host = stage || this.container.querySelector?.('.preview-capability-image-stage') || null;
        return host?.querySelector?.('img') || null;
    }

    mapPixelProbeEvent(event, stage = null, options = {}) {
        const image = this.getPixelProbeImage(stage);
        if (!image) {
            return {
                image: null,
                mapped: {
                    inside: false,
                    reason: 'missing-image'
                }
            };
        }

        return {
            image,
            mapped: this.pixelProbe.mapPoint({
                clientX: event?.clientX,
                clientY: event?.clientY
            }, image, options)
        };
    }

    hasInputConnection(nodeId, portIndex) {
        const canvas = this.flowCanvasAdapter?.raw || this.flowCanvasAdapter?.canvas || null;
        const connections = Array.isArray(canvas?.connections) ? canvas.connections : [];
        return connections.some(connection =>
            connection?.target === nodeId && Number(connection?.targetPort) === Number(portIndex));
    }

    syncPixelProbeOverlayElements() {
        const stage = this.container.querySelector?.('.preview-capability-image-stage') || null;
        const image = this.getPixelProbeImage(stage);
        const crosshair = this.container.querySelector?.('[data-role="pixel-probe-crosshair"]') || null;
        const roiBox = this.container.querySelector?.('[data-role="pixel-probe-roi"]') || null;
        if (!crosshair && !roiBox) {
            return;
        }

        if (crosshair && (!stage || !image || !this.pixelProbeLockedPoint?.mapped)) {
            crosshair.hidden = true;
            crosshair.setAttribute?.('hidden', '');
        } else if (crosshair) {
            const mapped = this.pixelProbeLockedPoint.mapped;
            const point = mapImagePixelToStagePoint({
                x: mapped.x,
                y: mapped.y,
                naturalWidth: mapped.width,
                naturalHeight: mapped.height,
                imageElement: image,
                stageElement: stage
            });
            if (!point) {
                crosshair.hidden = true;
                crosshair.setAttribute?.('hidden', '');
            } else {
                crosshair.hidden = false;
                crosshair.removeAttribute?.('hidden');
                crosshair.style.left = `${point.left}px`;
                crosshair.style.top = `${point.top}px`;
            }
        }

        const roi = this.pixelProbeRoiDraft || this.pixelProbeRoiSelection?.roi || null;
        if (roiBox && (!stage || !image || !roi)) {
            roiBox.hidden = true;
            roiBox.setAttribute?.('hidden', '');
        } else if (roiBox) {
            const rect = mapImageRoiToStageRect({
                roi,
                imageElement: image,
                stageElement: stage
            });
            if (!rect) {
                roiBox.hidden = true;
                roiBox.setAttribute?.('hidden', '');
            } else {
                roiBox.hidden = false;
                roiBox.removeAttribute?.('hidden');
                roiBox.style.left = `${rect.left}px`;
                roiBox.style.top = `${rect.top}px`;
                roiBox.style.width = `${rect.width}px`;
                roiBox.style.height = `${rect.height}px`;
            }
        }
    }

    schedulePixelProbeOverlaySync() {
        const sync = () => this.syncPixelProbeOverlayElements();
        if (typeof requestAnimationFrame === 'function') {
            requestAnimationFrame(sync);
            return;
        }

        sync();
    }

    handlePreviewImageLoad(event) {
        if (event?.target?.tagName?.toLowerCase?.() === 'img') {
            this.syncPixelProbeOverlayElements();
        }
    }

    setLockedPixelProbePoint(mapped, image) {
        const result = this.pixelProbe.createLockedPoint(mapped, image, this.previewState);
        if (result?.kind !== 'locked') {
            this.updatePixelProbeStatus(result);
            return;
        }

        this.pixelProbeLockedPoint = result;
        this.updatePixelProbeStatus(result);
        this.syncPixelProbeOverlayElements();
    }

    clearLockedPixelProbePoint() {
        this.pixelProbeLockedPoint = null;
        this.pixelProbePointerDown = null;
        this.updatePixelProbeStatus({
            kind: this.pixelProbeRoiSelection?.kind || 'default',
            message: this.pixelProbeRoiSelection?.message || PIXEL_PROBE_DEFAULT_MESSAGE
        });
        this.syncPixelProbeOverlayElements();
    }

    setPixelProbeRoiSelection(roi, image) {
        const result = this.pixelProbe.createRoiSelection(roi, image, this.previewState);
        if (result?.kind !== 'roi') {
            this.updatePixelProbeStatus(result);
            return;
        }

        this.pixelProbeRoiSelection = result;
        this.pixelProbeRoiDraft = null;
        this.updatePixelProbeStatus(result);
        this.syncPixelProbeOverlayElements();
    }

    clearPixelProbeRoiSelection() {
        this.pixelProbeRoiSelection = null;
        this.pixelProbeRoiDraft = null;
        this.pixelProbePointerDown = null;
        this.updatePixelProbeStatus({
            kind: this.pixelProbeLockedPoint?.kind || 'default',
            message: this.pixelProbeLockedPoint?.message || PIXEL_PROBE_DEFAULT_MESSAGE
        });
        this.syncPixelProbeOverlayElements();
    }

    clearPixelProbeSelections() {
        this.pixelProbeLockedPoint = null;
        this.pixelProbeRoiSelection = null;
        this.pixelProbeRoiDraft = null;
        this.pixelProbePointerDown = null;
        this.updatePixelProbeStatus({
            kind: 'default',
            message: PIXEL_PROBE_DEFAULT_MESSAGE
        });
        this.syncPixelProbeOverlayElements();
    }

    handlePixelProbePointerMove(event) {
        if (this.disposed) {
            return;
        }

        const stage = event.target?.closest?.('.preview-capability-image-stage') || null;
        if (!stage) {
            if (this.pixelProbeLockedPoint || this.pixelProbeRoiSelection) {
                this.syncPixelProbeOverlayElements();
                return;
            }
            if (this.pixelProbeStatusKind !== 'default') {
                this.updatePixelProbeStatus({
                    kind: 'default',
                    message: PIXEL_PROBE_DEFAULT_MESSAGE
                });
            }
            return;
        }

        if (this.pixelProbePointerDown) {
            const pointerDown = this.pixelProbePointerDown;
            if (pointerDown.pointerId === undefined ||
                event.pointerId === undefined ||
                pointerDown.pointerId === event.pointerId) {
                const { mapped } = this.mapPixelProbeEvent(event, pointerDown.stage, { clampToImage: true });
                const deltaX = Number(event.clientX) - pointerDown.startClientX;
                const deltaY = Number(event.clientY) - pointerDown.startClientY;
                const distance = Math.hypot(deltaX, deltaY);
                if (mapped?.inside && (pointerDown.dragging || distance >= PIXEL_PROBE_ROI_DRAG_THRESHOLD_PX)) {
                    pointerDown.dragging = true;
                    this.pixelProbeRoiDraft = createImageRoiFromPoints(
                        pointerDown.mapped,
                        mapped,
                        pointerDown.mapped.width,
                        pointerDown.mapped.height
                    );
                    this.syncPixelProbeOverlayElements();
                }
            }
            return;
        }

        const image = stage.querySelector?.('img') || null;
        if (this.pixelProbeLockedPoint || this.pixelProbeRoiSelection) {
            this.syncPixelProbeOverlayElements();
            return;
        }

        const result = this.pixelProbe.probePoint({
            clientX: event.clientX,
            clientY: event.clientY
        }, image);
        this.updatePixelProbeStatus(result);
    }

    handlePixelProbePointerDown(event) {
        if (this.disposed || event?.button > 0) {
            return;
        }

        const stage = this.getPixelProbeStageFromEvent(event);
        if (!stage) {
            return;
        }

        const { image, mapped } = this.mapPixelProbeEvent(event, stage);
        if (!image || !mapped?.inside) {
            return;
        }

        event.preventDefault?.();
        stage.focus?.({ preventScroll: true });
        stage.setPointerCapture?.(event.pointerId);
        this.pixelProbePointerDown = {
            pointerId: event.pointerId,
            startClientX: Number(event.clientX),
            startClientY: Number(event.clientY),
            mapped,
            image,
            stage,
            dragging: false
        };
    }

    handlePixelProbePointerUp(event) {
        if (this.disposed || !this.pixelProbePointerDown) {
            return;
        }

        const pointerDown = this.pixelProbePointerDown;
        if (pointerDown.pointerId !== undefined &&
            event.pointerId !== undefined &&
            pointerDown.pointerId !== event.pointerId) {
            return;
        }

        pointerDown.stage?.releasePointerCapture?.(event.pointerId);
        this.pixelProbePointerDown = null;
        const deltaX = Number(event.clientX) - pointerDown.startClientX;
        const deltaY = Number(event.clientY) - pointerDown.startClientY;
        const distance = Math.hypot(deltaX, deltaY);
        const shouldCreateRoi = pointerDown.dragging || distance >= PIXEL_PROBE_ROI_DRAG_THRESHOLD_PX;
        const { image, mapped } = this.mapPixelProbeEvent(event, pointerDown.stage, {
            clampToImage: shouldCreateRoi
        });
        if (!image || !mapped?.inside) {
            this.pixelProbeRoiDraft = null;
            this.syncPixelProbeOverlayElements();
            return;
        }

        if (shouldCreateRoi) {
            const roi = createImageRoiFromPoints(
                pointerDown.mapped,
                mapped,
                pointerDown.mapped.width,
                pointerDown.mapped.height
            );
            this.pixelProbeRoiDraft = null;
            if (roi) {
                this.setPixelProbeRoiSelection(roi, image);
            } else {
                this.syncPixelProbeOverlayElements();
            }
            return;
        }

        this.setLockedPixelProbePoint(mapped, image);
    }

    handlePixelProbePointerCancel(event) {
        const pointerDown = this.pixelProbePointerDown;
        if (pointerDown?.stage && event?.pointerId !== undefined) {
            pointerDown.stage.releasePointerCapture?.(event.pointerId);
        }
        this.pixelProbePointerDown = null;
        this.pixelProbeRoiDraft = null;
        this.syncPixelProbeOverlayElements();
    }

    handlePixelProbePointerLeave() {
        if (!this.disposed && !this.pixelProbeLockedPoint && !this.pixelProbeRoiSelection) {
            this.updatePixelProbeStatus({
                kind: 'default',
                message: PIXEL_PROBE_DEFAULT_MESSAGE
            });
        }
    }

    handlePixelProbeKeyDown(event) {
        if (this.disposed || event?.key !== 'Escape') {
            return;
        }

        if (this.pixelProbeLockedPoint ||
            this.pixelProbeRoiSelection ||
            this.pixelProbeRoiDraft ||
            this.pixelProbePointerDown) {
            event.preventDefault?.();
            this.clearPixelProbeSelections();
        }
    }

    handleClick(event) {
        const target = event.target?.closest?.('[data-preview-action]') || event.target;
        const action = target?.dataset?.previewAction;
        if (!action || this.disposed) {
            return;
        }

        event.preventDefault?.();

        if (target.disabled || target.getAttribute?.('aria-disabled') === 'true') {
            return;
        }

        if (action === 'manual-preview') {
            this.requestManualPreview();
            return;
        }

        if (action === 'cancel-preview') {
            this.cancelCurrentPreview();
            return;
        }

        if (action === 'image-fit') {
            this.previewImageMode = 'fit';
            this.resetPixelProbeStatus({ clearSelections: false });
            this.render();
            this.schedulePixelProbeOverlaySync();
            return;
        }

        if (action === 'image-original') {
            this.previewImageMode = 'original';
            this.resetPixelProbeStatus({ clearSelections: false });
            this.render();
            this.schedulePixelProbeOverlaySync();
            return;
        }

        if (action === 'clear-pixel-lock') {
            this.clearLockedPixelProbePoint();
            this.render();
            return;
        }

        if (action === 'clear-pixel-roi') {
            this.clearPixelProbeRoiSelection();
            this.render();
            return;
        }

        if (action === 'open-image') {
            const imageSource = target.dataset.imageSource || this.previewState?.presenter?.outputImageSrc || null;
            if (imageSource) {
                this.previewAdapter.openPreviewImage(imageSource);
            }
            return;
        }

        if (action === 'select-node') {
            const nodeId = target.dataset.nodeId;
            if (nodeId) {
                this.previewAdapter.selectNode(nodeId);
            }
            return;
        }

        if (action === 'read-artifact') {
            const artifactId = target.dataset.artifactId;
            if (artifactId) {
                void this.readArtifactPreview(artifactId);
            }
        }
    }

    handleChange(event) {
        const target = event.target;
        if (target?.dataset?.previewAuto !== 'true' || this.disposed) {
            return;
        }

        this.autoPreviewEnabled = Boolean(target.checked);
        if (!this.autoPreviewEnabled) {
            this.cancelCurrentPreview({ showToast: false });
            return;
        }

        if (this.autoPreviewEnabled && this.currentNodeId) {
            this.previewAdapter.setActiveNode(this.currentNodeId, {
                autoPreview: true
            });
        }
        this.render();
    }

    requestManualPreview() {
        if (!this.currentNodeId) {
            this.showToast('请选择一个算子', 'warning');
            return;
        }

        const liveNode = this.previewAdapter.getNode(this.currentNodeId);
        const resultModel = this.buildResultViewModel(liveNode);
        if (this.manualPreviewPending || this.isPreviewLoadingForCurrentNode(resultModel)) {
            return;
        }

        if (!liveNode) {
            this.nodeDeleted = true;
            this.previewAdapter.clearActiveNode();
            this.render();
            return;
        }

        this.previewAdapter.setActiveNode(this.currentNodeId, {
            autoPreview: false
        });
        this.manualPreviewPending = true;
        const requestToken = ++this.manualPreviewRequestToken;
        this.render();
        try {
            const requestResult = this.previewAdapter.requestPreview({
                immediate: true,
                force: true,
                trigger: 'manual'
            });
            if (requestResult && typeof requestResult.catch === 'function') {
                requestResult.catch(error => this.handleManualPreviewRequestFailure(error, requestToken));
            }
        } catch (error) {
            this.handleManualPreviewRequestFailure(error, requestToken);
        }
    }

    cancelCurrentPreview(options = {}) {
        this.manualPreviewRequestToken += 1;
        this.manualPreviewPending = false;
        try {
            this.previewAdapter.cancelPreview();
        } catch (error) {
            if (options.showToast !== false) {
                this.showToast(`取消预览失败：${error?.message || '未知错误'}`, 'error');
            }
        }
        this.render();
    }

    handleManualPreviewRequestFailure(error, requestToken) {
        if (this.disposed || requestToken !== this.manualPreviewRequestToken) {
            return;
        }

        this.manualPreviewPending = false;
        this.showToast(`预览请求失败：${error?.message || '未知错误'}`, 'error');
        this.render();
    }

    resetArtifactReadsIfIdentityChanged() {
        const nextSignature = getStateIdentitySignature(this.previewState);
        if (nextSignature === this.resultIdentitySignature) {
            return;
        }

        this.resultIdentitySignature = nextSignature;
        this.artifactReadState.clear();
        this.cancelArtifactRead();
    }

    getRegionInputGuidance() {
        const candidate = buildRegionInputGuidance(this.currentOperator);
        if (!candidate || !this.currentNodeId) {
            return candidate;
        }

        return this.previewAdapter.hasInputConnection?.(this.currentNodeId, candidate.portIndex)
            ? null
            : candidate;
    }

    buildResultViewModel(liveNode = null) {
        return buildOperatorResultViewModel(this.currentOperator, this.previewState, {
            liveNode,
            flowRevision: this.previewAdapter.getFlowRevision(),
            projectId: this.previewAdapter.getProjectId?.(),
            inputImageHash: this.previewAdapter.getInputImageHash?.(liveNode),
            getNodes: () => this.previewAdapter.getNodes()
        });
    }

    isPreviewLoadingForCurrentNode(resultModel = null) {
        return Boolean(
            this.currentNodeId &&
            this.previewState?.activeNodeId === this.currentNodeId &&
            this.previewState?.status === 'loading' &&
            resultModel?.stale !== true);
    }

    render() {
        if (this.disposed || !this.container) {
            return;
        }

        const selectedNodeId = this.currentNodeId;
        const belongsToSelectedNode = Boolean(
            selectedNodeId &&
            this.previewState?.activeNodeId === selectedNodeId);
        const liveNode = selectedNodeId ? this.previewAdapter.getNode(selectedNodeId) : null;
        const resultModel = this.buildResultViewModel(liveNode);
        const hasCurrentIdleMessage = belongsToSelectedNode &&
            this.previewState?.status === 'idle' &&
            Boolean(this.previewState?.errorMessage);
        const isStale = belongsToSelectedNode && resultModel?.stale === true && !hasCurrentIdleMessage;
        const regionInputGuidance = this.getRegionInputGuidance();
        const statusInfo = regionInputGuidance
            ? {
                kind: 'missing-region',
                label: '缺少 Region',
                message: regionInputGuidance.summary,
                emptyMessage: regionInputGuidance.summary
            }
            : getStatusLabel(this.previewState, belongsToSelectedNode, this.nodeDeleted, isStale);
        const isLoadingForCurrentNode = this.isPreviewLoadingForCurrentNode(resultModel);
        const manualPreviewDisabled = !selectedNodeId || isLoadingForCurrentNode || this.manualPreviewPending || Boolean(regionInputGuidance);
        const manualPreviewLabel = isLoadingForCurrentNode || this.manualPreviewPending ? '预览中...' : '手动预览';
        const cancelPreviewDisabled = !(isLoadingForCurrentNode || this.manualPreviewPending);
        const title = this.currentConnection
            ? '当前选中连线'
            : this.currentOperator
            ? normalizeOperatorTitle(this.currentOperator, liveNode)
            : '请选择一个算子';
        const type = this.currentConnection
            ? `${this.currentConnection.sourceTitle || '-'} → ${this.currentConnection.targetTitle || '-'}`
            : this.currentOperator?.type || liveNode?.type || '-';
        const currentHeading = this.currentConnection ? '当前连线' : '当前算子';
        const currentMessage = this.currentConnection
            ? '连线用于传递端口数据，不产生独立预览。请选择算子节点查看输出图像与模块结果。'
            : (regionInputGuidance?.summary || statusInfo.message);

        this.container.innerHTML = `
            <section class="preview-capability-owner" data-owner="${PREVIEW_PANEL_CAPABILITY_OWNER_ID}" data-status="${escapeAttribute(statusInfo.kind)}" aria-label="预览面板">
                <header class="preview-capability-header">
                    <div class="preview-capability-title-group">
                        <div class="preview-capability-title">预览工作台</div>
                        <div class="preview-capability-status" data-status="${escapeAttribute(statusInfo.kind)}">${escapeHtml(statusInfo.label)}</div>
                    </div>
                    <div class="preview-capability-actions">
                        <label class="preview-capability-auto">
                            <input type="checkbox" data-preview-auto="true" ${this.autoPreviewEnabled ? 'checked' : ''}>
                            <span>自动预览</span>
                        </label>
                        <button type="button" class="btn btn-secondary btn-sm" data-preview-action="manual-preview" ${renderButtonDisabledAttributes(manualPreviewDisabled)}>${escapeHtml(manualPreviewLabel)}</button>
                        <button type="button" class="btn btn-secondary btn-sm" data-preview-action="cancel-preview" ${renderButtonDisabledAttributes(cancelPreviewDisabled)}>取消预览</button>
                    </div>
                </header>
                <div class="preview-capability-scroll" data-low-height-scroll="true">
                    ${this.renderPreviewMedia(belongsToSelectedNode, statusInfo, resultModel, liveNode, regionInputGuidance)}
                    <section class="preview-capability-current">
                        <div class="preview-capability-current-heading">
                            <span>${escapeHtml(currentHeading)}</span>
                            <strong title="${escapeAttribute(title)}">${escapeHtml(title)}</strong>
                            <em title="${escapeAttribute(type)}">${escapeHtml(type)}</em>
                        </div>
                        <p class="preview-capability-message">${escapeHtml(currentMessage)}</p>
                        ${this.renderCurrentParameterSummary()}
                    </section>
                    ${this.renderRegionInputGuidance(regionInputGuidance)}
                    ${this.renderPreviewSummary(belongsToSelectedNode, statusInfo)}
                    ${this.renderModuleResult(belongsToSelectedNode, resultModel)}
                    ${this.renderPortsAndTiming(belongsToSelectedNode)}
                </div>
            </section>
        `;
        this.schedulePixelProbeOverlaySync();
    }

    renderCurrentParameterSummary() {
        if (this.currentConnection || !Array.isArray(this.currentOperator?.parameters)) {
            return '';
        }

        const items = this.currentOperator.parameters
            .filter(parameter => getParameterName(parameter))
            .slice(0, 4)
            .map(parameter => {
                const name = normalizeParameterDisplayName(parameter);
                const value = getParameterValue(parameter);
                const formatted = formatPreviewOutputValue(name, value, {
                    stringMaxLength: 32
                });
                return `
                    <span class="preview-capability-param-chip">
                        <em>${escapeHtml(name)}</em>
                        <strong title="${escapeAttribute(formatted.title || formatted.text)}">${escapeHtml(formatted.text)}</strong>
                    </span>
                `;
            })
            .join('');

        return items ? `<div class="preview-capability-parameter-summary">${items}</div>` : '';
    }

    renderRegionInputGuidance(guidance) {
        if (!guidance) {
            return '';
        }

        return `
            <section class="preview-capability-region-guidance" data-guidance="${escapeAttribute(guidance.code)}" role="alert">
                <h5>${escapeHtml(guidance.title)}</h5>
                <ul>
                    ${guidance.lines.map(line => `<li>${escapeHtml(line)}</li>`).join('')}
                </ul>
            </section>
        `;
    }

    renderPreviewMedia(belongsToSelectedNode, statusInfo, model, liveNode, regionInputGuidance = null) {
        const presenter = belongsToSelectedNode ? (this.previewState?.presenter || {}) : {};
        const outputImageSrc = presenter.outputImageSrc || null;
        const hasImage = Boolean(outputImageSrc);
        const hasImageOutput = hasImageOutputPort(this.currentOperator, liveNode);
        const emptyMessage = regionInputGuidance?.summary || getPreviewImageEmptyMessage({
            nodeDeleted: this.nodeDeleted,
            currentConnection: this.currentConnection,
            currentNodeId: this.currentNodeId,
            belongsToSelectedNode,
            statusInfo,
            model,
            hasImageOutput
        });
        const staleBadge = statusInfo.kind === 'stale'
            ? `<span class="preview-capability-image-badge">${escapeHtml(STALE_PREVIEW_MESSAGE)}</span>`
            : '';
        const imageFitPressed = this.previewImageMode === 'fit';
        const imageOriginalPressed = this.previewImageMode === 'original';
        const clearLockDisabled = !hasImage || !this.pixelProbeLockedPoint;
        const clearRoiDisabled = !hasImage || !this.pixelProbeRoiSelection;

        return `
            <section class="preview-capability-media preview-capability-media-single">
                <div class="preview-capability-image preview-capability-main-image" data-image-mode="${escapeAttribute(this.previewImageMode)}" data-stale="${statusInfo.kind === 'stale' ? 'true' : 'false'}">
                    <div class="preview-capability-image-toolbar">
                        <span class="preview-capability-image-title">输出图像</span>
                        <div class="preview-capability-image-actions">
                            <button type="button" class="btn btn-secondary btn-sm" data-preview-action="image-fit" aria-pressed="${imageFitPressed ? 'true' : 'false'}" ${renderButtonDisabledAttributes(!hasImage)}>适应窗口</button>
                            <button type="button" class="btn btn-secondary btn-sm" data-preview-action="image-original" aria-pressed="${imageOriginalPressed ? 'true' : 'false'}" ${renderButtonDisabledAttributes(!hasImage)}>原始大小</button>
                            <button type="button" class="btn btn-secondary btn-sm" data-preview-action="clear-pixel-lock" ${renderButtonDisabledAttributes(clearLockDisabled)}>清除锁定</button>
                            <button type="button" class="btn btn-secondary btn-sm" data-preview-action="clear-pixel-roi" ${renderButtonDisabledAttributes(clearRoiDisabled)}>清除 ROI</button>
                            <button type="button" class="btn btn-secondary btn-sm" data-preview-action="open-image" data-image-source="${escapeAttribute(outputImageSrc || '')}" ${renderButtonDisabledAttributes(!hasImage)}>打开大图</button>
                        </div>
                    </div>
                    <div class="preview-capability-image-stage" tabindex="${hasImage ? '0' : '-1'}">
                        ${hasImage
                            ? `<img src="${escapeAttribute(outputImageSrc)}" alt="当前算子输出图像预览">`
                            : `<div class="preview-capability-placeholder">${escapeHtml(emptyMessage)}</div>`}
                        ${hasImage ? '<div class="preview-capability-probe-crosshair" data-role="pixel-probe-crosshair" hidden aria-hidden="true"></div>' : ''}
                        ${hasImage ? '<div class="preview-capability-roi-box" data-role="pixel-probe-roi" hidden aria-hidden="true"></div>' : ''}
                        ${hasImage ? staleBadge : ''}
                    </div>
                    <div class="preview-capability-pixel-probe-status" data-role="pixel-probe-status" data-probe-state="${escapeAttribute(this.pixelProbeStatusKind)}">${escapeHtml(this.pixelProbeStatusText)}</div>
                </div>
            </section>
        `;
    }

    renderPortsAndTiming(belongsToSelectedNode) {
        if (this.currentConnection) {
            return `
                <section class="preview-capability-section" data-preview-section="connection">
                    <h5>预览对象</h5>
                    <div class="preview-capability-empty">
                        当前选择为连线：${escapeHtml(this.currentConnection.sourcePortName || '-')} → ${escapeHtml(this.currentConnection.targetPortName || '-')}。预览工作台仅对算子节点运行。
                    </div>
                </section>
            `;
        }

        const inputPorts = Array.isArray(this.currentOperator?.inputPorts) ? this.currentOperator.inputPorts : [];
        const outputPorts = Array.isArray(this.currentOperator?.outputPorts) ? this.currentOperator.outputPorts : [];
        const executionTime = belongsToSelectedNode
            ? (this.previewState?.executionTimeMs ?? this.previewState?.observation?.outcome?.executionTimeMs ?? null)
            : null;
        const renderPort = (port, index, fallback) => {
            const name = port?.displayName || port?.DisplayName || port?.name || port?.Name || `${fallback} ${index + 1}`;
            const type = port?.dataType || port?.DataType || port?.type || port?.Type || 'Any';
            return `${name} (${formatPortTypeForMessage(type)})`;
        };
        const inputText = inputPorts.length > 0
            ? inputPorts.map((port, index) => renderPort(port, index, '输入')).join('，')
            : '无输入端口';
        const outputText = outputPorts.length > 0
            ? outputPorts.map((port, index) => renderPort(port, index, '输出')).join('，')
            : '无输出端口';
        const timeText = executionTime === null || executionTime === undefined || executionTime === ''
            ? '暂无耗时'
            : `${executionTime} ms`;

        return `
            <details class="preview-capability-section preview-capability-secondary" data-preview-section="ports">
                <summary>
                    <span>端口与耗时</span>
                    <em>${escapeHtml(timeText)}</em>
                </summary>
                <div class="preview-capability-kv-grid">
                    <div class="preview-capability-kv">
                        <span>输入端口</span>
                        <strong>${escapeHtml(inputText)}</strong>
                    </div>
                    <div class="preview-capability-kv">
                        <span>输出端口</span>
                        <strong>${escapeHtml(outputText)}</strong>
                    </div>
                    <div class="preview-capability-kv">
                        <span>运行耗时</span>
                        <strong>${escapeHtml(timeText)}</strong>
                    </div>
                </div>
            </details>
        `;
    }

    renderPreviewSummary(belongsToSelectedNode, statusInfo) {
        if (this.nodeDeleted) {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">节点已删除</div>
                </section>
            `;
        }

        if (!this.currentNodeId) {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">请选择一个算子</div>
                </section>
            `;
        }

        if (!belongsToSelectedNode || !this.previewState) {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">等待预览</div>
                </section>
            `;
        }

        if (statusInfo.kind === 'loading') {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">预览中</div>
                </section>
            `;
        }

        if (statusInfo.kind === 'stale') {
            const summaryItems = buildPreviewSummaryItems(this.previewState.outputData, {
                maxItems: 8,
                stringMaxLength: 64,
                skipImageLikeValues: true
            });
            return `
                <section class="preview-capability-section" data-preview-section="summary" data-stale="true">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty stale">${escapeHtml(STALE_PREVIEW_MESSAGE)}</div>
                    ${summaryItems.length > 0
                        ? `<div class="preview-capability-output-heading">旧输出摘要</div>
                           <div class="preview-capability-summary">
                               ${summaryItems.map(item => `
                                   <div class="preview-capability-summary-row" data-output-kind="${escapeAttribute(item.kind || 'value')}">
                                       <span>${escapeHtml(item.key)}</span>
                                       <strong title="${escapeAttribute(item.title || item.value || '')}">${escapeHtml(item.value)}</strong>
                                   </div>
                               `).join('')}
                           </div>`
                        : ''}
                </section>
            `;
        }

        if (statusInfo.kind === 'idle-error') {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">${escapeHtml(statusInfo.message || statusInfo.label)}</div>
                </section>
            `;
        }

        if (statusInfo.kind === 'auth-error') {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty auth-error">${escapeHtml(statusInfo.message || '登录状态无效，请重新登录。')}</div>
                </section>
            `;
        }

        if (statusInfo.kind === 'blocked') {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty warning">${escapeHtml(statusInfo.message || '预览已安全拦截，正式运行流程时才会执行外部动作。')}</div>
                </section>
            `;
        }

        if (statusInfo.kind === 'error') {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty error">预览失败：${escapeHtml(statusInfo.message)}</div>
                </section>
            `;
        }

        if (statusInfo.kind === 'canceled') {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">预览已取消</div>
                </section>
            `;
        }

        const summaryItems = buildPreviewSummaryItems(this.previewState.outputData, {
            maxItems: 8,
            stringMaxLength: 64,
            skipImageLikeValues: true
        });

        if (summaryItems.length === 0) {
            return `
                <section class="preview-capability-section">
                    <h5>预览结果</h5>
                    <div class="preview-capability-empty">暂无输出摘要</div>
                </section>
            `;
        }

        return `
            <section class="preview-capability-section">
                <h5>预览结果</h5>
                <div class="preview-capability-summary">
                    ${summaryItems.map(item => `
                        <div class="preview-capability-summary-row" data-output-kind="${escapeAttribute(item.kind || 'value')}">
                            <span>${escapeHtml(item.key)}</span>
                            <strong title="${escapeAttribute(item.title || item.value || '')}">${escapeHtml(item.value)}</strong>
                        </div>
                    `).join('')}
                </div>
            </section>
        `;
    }

    renderModuleResult(belongsToSelectedNode, model) {
        const stateMessage = this.nodeDeleted ? '节点已删除' : model.stateMessage;
        const outputSections = belongsToSelectedNode ? this.renderOutputSections(model) : '';

        return `
            <section class="preview-capability-section preview-capability-module-result" data-result-status="${escapeAttribute(this.nodeDeleted ? 'deleted' : model.status)}">
                <div class="preview-capability-section-header">
                    <h5>模块结果</h5>
                    <span>${escapeHtml(this.nodeDeleted ? '节点已删除' : model.statusText)}</span>
                </div>
                <div class="preview-capability-empty">${escapeHtml(stateMessage)}</div>
                ${this.renderOverview(model)}
                ${outputSections}
                ${this.renderArtifacts(model)}
                ${this.renderDiagnostics(model)}
            </section>
        `;
    }

    renderNodeResultList(model) {
        if (!Array.isArray(model.nodeResults) || model.nodeResults.length === 0) {
            return '';
        }

        return `
            <div class="preview-capability-node-list">
                ${model.nodeResults.slice(0, 32).map(item => `
                    <button type="button"
                            class="preview-capability-node-item"
                            data-preview-action="select-node"
                            data-node-id="${escapeAttribute(item.nodeId)}"
                            data-selected="${item.selected ? 'true' : 'false'}"
                            data-status="${escapeAttribute(item.statusKind)}">
                        <span>${String(item.index + 1).padStart(2, '0')}</span>
                        <strong>${escapeHtml(item.title)}</strong>
                        <em>${escapeHtml(item.statusText)}</em>
                    </button>
                `).join('')}
            </div>
        `;
    }

    renderOverview(model) {
        const rows = (model.executionSummaryItems || [])
            .filter(item => item.value !== null && item.value !== undefined && item.value !== '')
            .slice(0, 8)
            .map(item => `
                <div class="preview-capability-kv">
                    <span>${escapeHtml(item.label)}</span>
                    <strong>${escapeHtml(item.value)}</strong>
                </div>
            `)
            .join('');

        return rows
            ? `<div class="preview-capability-output-heading">结果摘要</div><div class="preview-capability-kv-grid">${rows}</div>`
            : '';
    }

    renderOutputSections(model) {
        if (!Array.isArray(model.keyOutputs) || model.keyOutputs.length === 0) {
            return `
                <div class="preview-capability-output-heading">关键输出</div>
                <div class="preview-capability-empty">${model.status === 'success'
                    ? '执行成功，但没有可展示的关键输出；可在高级诊断中查看原始结果。'
                    : '暂无可展示的关键输出'}</div>
            `;
        }

        return `
            <div class="preview-capability-output-heading">关键输出</div>
            <div class="preview-capability-output-groups">
                <div class="preview-capability-output-group" data-output-group="key-output">
                    ${model.keyOutputs.map(item => `
                        <div class="preview-capability-output-row" data-output-kind="${escapeAttribute(item.kind || 'value')}">
                            <span>${escapeHtml(item.label || item.key || '-')}</span>
                            <strong title="${escapeAttribute(item.title || item.value || '')}">${escapeHtml(item.value || '-')}</strong>
                            <em>${escapeHtml(item.meta || (item.declared ? '声明输出' : item.resultPath || ''))}</em>
                        </div>
                    `).join('')}
                </div>
            </div>
        `;
    }

    renderArtifacts(model) {
        const imageItems = Array.isArray(model.imageSummaries) ? model.imageSummaries : [];
        const scene = model.sceneSummary || {};
        if (imageItems.length === 0 && !scene.available) {
            return `
                <div class="preview-capability-output-heading">图像与附件</div>
                <div class="preview-capability-empty">${escapeHtml(scene.message || '暂无图像/区域附件')}</div>
            `;
        }

        return `
            <div class="preview-capability-output-heading">图像与附件</div>
            <div class="preview-capability-artifacts">
                ${imageItems.map(item => {
                    const artifact = item.artifact || {};
                    const readState = artifact.artifactId ? this.artifactReadState.get(artifact.artifactId) : null;
                    const readDisabled = readState?.status === 'loading';
                    return `
                        <div class="preview-capability-artifact" data-artifact-id="${escapeAttribute(artifact.artifactId || '')}">
                            <div>
                                <strong>${escapeHtml(item.label || '图像/附件')}</strong>
                                <span>${escapeHtml(item.summary || '图像内容已省略')}</span>
                            </div>
                            ${artifact.artifactId ? `
                                <button type="button"
                                        class="btn btn-secondary btn-sm"
                                        data-preview-action="read-artifact"
                                        data-artifact-id="${escapeAttribute(artifact.artifactId)}"
                                        ${renderButtonDisabledAttributes(readDisabled)}>
                                    ${readState?.status === 'loading' ? '读取中' : '查看摘要'}
                                </button>
                            ` : ''}
                            ${readState ? `<pre class="preview-capability-artifact-preview ${escapeAttribute(readState.status)}">${escapeHtml(readState.text)}</pre>` : ''}
                        </div>
                    `;
                }).join('')}
                ${scene.available ? `
                    <div class="preview-capability-artifact" data-artifact-id="">
                        <div>
                            <strong>区域/叠加</strong>
                            <span>${escapeHtml(scene.primitiveCount ?? scene.primitives?.length ?? 0)} 项${scene.imageSize ? ` · ${escapeHtml(scene.imageSize)}` : ''}</span>
                        </div>
                    </div>
                ` : ''}
            </div>
        `;
    }

    renderDiagnostics(model) {
        const diagnostics = Array.isArray(model.advancedDiagnostics) ? model.advancedDiagnostics : [];
        const diagnosticRows = diagnostics.slice(0, 12).map(item => `
                    <div class="preview-capability-diagnostic">
                        <span>${escapeHtml(item.label || item.code || item.source || '诊断')}</span>
                        <strong>${escapeHtml(item.message || '')}</strong>
                    </div>
                `).join('');
        const rawRows = (model.rawDataSections || []).map(section => `
            <div class="preview-capability-output-group" data-output-group="${escapeAttribute(section.kind)}">
                <div class="preview-capability-output-heading">${escapeHtml(section.label)}${section.omittedCount > 0 ? ` · 已折叠 ${escapeHtml(section.omittedCount)} 项` : ''}</div>
                ${section.items.map(item => `
                    <div class="preview-capability-output-row">
                        <span>${escapeHtml(item.label || '-')}</span>
                        <strong>${escapeHtml(item.value || '-')}</strong>
                        <em>${escapeHtml(item.meta || '')}</em>
                    </div>
                `).join('')}
            </div>
        `).join('');
        const rawJson = model.rawJsonPreview?.text
            ? `<pre class="preview-capability-artifact-preview">${escapeHtml(model.rawJsonPreview.text)}</pre>`
            : '<div class="preview-capability-empty">暂无原始 JSON</div>';

        return `
            <details class="preview-capability-secondary preview-capability-advanced" data-preview-section="advanced">
                <summary>
                    <span>高级诊断</span>
                    <em>${escapeHtml(diagnostics.length)} 条诊断 · ${escapeHtml((model.rawDataSections || []).length)} 组原始数据</em>
                </summary>
                ${diagnosticRows ? `<div class="preview-capability-diagnostics">${diagnosticRows}</div>` : '<div class="preview-capability-empty">暂无诊断信息</div>'}
                ${rawRows}
                <div class="preview-capability-output-heading">原始数据摘要</div>
                ${rawJson}
            </details>
        `;
    }

    findArtifact(artifactId) {
        const safeArtifactId = String(artifactId || '');
        const artifacts = Array.isArray(this.previewState?.artifacts) ? this.previewState.artifacts : [];
        return artifacts.find(artifact => artifact?.artifactId === safeArtifactId) || null;
    }

    cancelArtifactRead() {
        this.artifactReadAbort?.abort?.();
        this.artifactReadAbort = null;
        this.artifactReadToken += 1;
        Array.from(this.artifactReadState.entries()).forEach(([artifactId, state]) => {
            if (state?.status === 'loading') {
                this.artifactReadState.delete(artifactId);
            }
        });
    }

    isArtifactReadCurrent(token, identity, abortController = null) {
        return token === this.artifactReadToken &&
            abortController?.signal?.aborted !== true &&
            JSON.stringify(getObservationIdentity(this.previewState) || {}) === JSON.stringify(identity || {});
    }

    isArtifactUnavailableError(error) {
        return error?.status === 404 ||
            error?.statusCode === 404 ||
            error?.name === 'PreviewArtifactUnavailableError' ||
            /过期|不可用|stale|missing|not found|404/i.test(String(error?.message || ''));
    }

    async readArtifactPreview(artifactId) {
        const artifact = normalizeArtifactReference(this.findArtifact(artifactId));
        const identity = getObservationIdentity(this.previewState);
        if (!artifact?.artifactId || !identity) {
            return;
        }

        if (this.artifactReadState.get(artifact.artifactId)?.status === 'loading') {
            return;
        }

        this.cancelArtifactRead();
        const token = this.artifactReadToken;

        if (!isTextArtifactForResultPanel(artifact)) {
            this.artifactReadState.set(artifact.artifactId, {
                status: 'success',
                text: formatResultArtifactMetadata(artifact, '非文本 Artifact，仅展示元数据')
            });
            this.render();
            return;
        }

        if (artifact.length > MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES) {
            this.artifactReadState.set(artifact.artifactId, {
                status: 'success',
                text: formatResultArtifactMetadata(artifact, '内容过大，仅展示元数据')
            });
            this.render();
            return;
        }

        const abortController = typeof AbortController !== 'undefined'
            ? new AbortController()
            : null;
        this.artifactReadAbort = abortController;
        this.artifactReadState.set(artifact.artifactId, {
            status: 'loading',
            text: '正在按需读取 Artifact...'
        });
        this.render();

        try {
            const result = await this.previewAdapter.readArtifactForCurrentState(
                artifact.artifactId,
                identity,
                { signal: abortController?.signal });
            if (!this.isArtifactReadCurrent(token, identity, abortController)) {
                return;
            }

            const artifactMetadata = normalizeArtifactReference(result?.artifact) || artifact;
            const blob = result?.blob;
            if (!blob || typeof blob.slice !== 'function') {
                throw new Error('Artifact Blob 不支持有界文本预览');
            }

            const actualSize = Number(blob.size ?? artifactMetadata.length ?? 0);
            const actualTextTooLarge = Number.isFinite(actualSize) &&
                actualSize > MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES;
            const previewBlob = blob.slice(0, MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES);
            const rawText = await previewBlob.text();
            if (!this.isArtifactReadCurrent(token, identity, abortController)) {
                return;
            }

            let previewText = rawText;
            if (String(artifactMetadata.contentType || '').toLowerCase().includes('json')) {
                try {
                    previewText = buildSafeJsonPreview(JSON.parse(rawText), {
                        maxChars: MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS
                    }).text;
                } catch {
                    previewText = redactLocalAbsolutePaths(rawText);
                }
            } else {
                previewText = redactLocalAbsolutePaths(rawText);
            }

            const displayTruncated = actualTextTooLarge ||
                previewText.length > MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS;
            const boundedText = displayTruncated
                ? `${previewText.slice(0, MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS)}\n已截断。`
                : previewText;

            this.artifactReadState.set(artifact.artifactId, {
                status: 'success',
                text: boundedText || formatResultArtifactMetadata(artifactMetadata, `已读取 ${formatByteLength(actualSize)}。`)
            });
            this.render();
        } catch (error) {
            if (!this.isArtifactReadCurrent(token, identity, abortController) || error?.name === 'AbortError') {
                return;
            }

            this.artifactReadState.set(artifact.artifactId, {
                status: 'error',
                text: this.isArtifactUnavailableError(error)
                    ? '资源已过期或不可用'
                    : (error?.message || 'Artifact 读取失败')
            });
            this.render();
        } finally {
            if (this.artifactReadAbort === abortController) {
                this.artifactReadAbort = null;
            }
        }
    }

    destroy() {
        this.dispose();
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        this.cancelArtifactRead();
        this.unsubscribes.forEach(unsubscribe => {
            try {
                unsubscribe?.();
            } catch {
                // Best-effort cleanup for external subscriptions.
            }
        });
        this.unsubscribes = [];
        this.artifactReadState.clear();
        this.pixelProbe?.reset?.();
        this.container.removeEventListener('click', this.handleClick);
        this.container.removeEventListener('change', this.handleChange);
        this.container.removeEventListener('pointermove', this.handlePixelProbePointerMove);
        this.container.removeEventListener('pointerdown', this.handlePixelProbePointerDown);
        this.container.removeEventListener('pointerup', this.handlePixelProbePointerUp);
        this.container.removeEventListener('pointercancel', this.handlePixelProbePointerCancel);
        this.container.removeEventListener('pointerleave', this.handlePixelProbePointerLeave);
        this.container.removeEventListener('keydown', this.handlePixelProbeKeyDown);
        this.container.removeEventListener('load', this.handlePreviewImageLoad, true);
        if (typeof document !== 'undefined' && document?.removeEventListener) {
            document.removeEventListener('keydown', this.handlePixelProbeKeyDown);
        }
        delete this.container.dataset.previewPanelOwner;
        this.container.innerHTML = '';
        this.currentOperator = null;
        this.currentNodeId = null;
        this.currentConnection = null;
        this.previewState = null;
        this.previewImageSource = null;
        this.manualPreviewPending = false;
        this.manualPreviewRequestToken += 1;
    }
}

export function createPreviewPanelCapabilityAdapter(options = {}) {
    return new PreviewPanelCapabilityAdapter(options);
}

export default PreviewPanelCapabilityOwner;
