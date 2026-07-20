import {
    buildPreviewSummaryItems,
    isPreviewImageLikePayload
} from './previewOutputFormatter.mjs';
import httpClient from '../../core/messaging/httpClient.js';
import { normalizeAcquisitionSourceType } from '../../shared/parameterDependencyRules.js';

const DEFAULT_DEBOUNCE_MS = 500;
// 预览请求遇到认证失效（401）时展示的登录态提示，区别于算子执行失败。
const PREVIEW_SESSION_INVALID_MESSAGE = '登录状态无效，请重新登录。';
const DEFAULT_PREVIEW_TIMEOUT_MS = 15000;
const HIGH_COST_PREVIEW_TIMEOUT_MS = 30000;
const MAX_PREVIEW_INPUT_BASE64_CHARS = 24 * 1024 * 1024;
const MAX_CACHE_IMAGE_BASE64_CHARS = 6 * 1024 * 1024;
const DEFAULT_MAX_CACHE_OUTPUT_IMAGE_BASE64_CHARS = 24 * 1024 * 1024;
const PREVIEW_OUTPUT_ARRAY_LIMIT = 24;
const PREVIEW_OUTPUT_OBJECT_FIELD_LIMIT = 48;
const PREVIEW_OUTPUT_STRING_LIMIT = 512;
const PREVIEW_OUTPUT_MAX_DEPTH = 3;
const PREVIEW_OUTPUT_IMAGE_KEY_PATTERN = /(image|bitmap|preview|thumbnail|base64|mask)/i;
const MISSING_PROJECT_PREVIEW_MESSAGE = '请先新建/保存/打开工程后再预览';
const SIDE_EFFECT_BLOCKED_PREVIEW_MESSAGE = '预览已安全拦截副作用算子：该算子可能访问外部设备、网络服务或执行文件系统写入。正式运行流程时才会执行这些动作。';
const HIGH_COST_OPERATOR_TYPES = new Set([
    'DeepLearning',
    'OnnxInference',
    'SemanticSegmentation',
    'SurfaceDefectDetection',
    'AnomalyDetection',
    'OcrRecognition',
    'TemplateMatch',
    'TemplateMatching',
    'ShapeMatching',
    'PlanarMatching',
    'AkazeFeatureMatch',
    'OrbFeatureMatch',
    'LocalDeformableMatching',
    'PPFMatch',
    'RansacPlaneSegmentation',
    'PPFEstimation'
]);
const LIGHT_AUTO_PREVIEW_OPERATOR_TYPES = new Set([
    'BlobLabeling',
    'BoxFilter',
    'BoxNms',
    'CaliperTool',
    'DetectionSequenceJudge',
    'DualModalVoting',
    'EdgePairDefect',
    'FrequencyFilter',
    'GeometricTolerance',
    'GlcmTexture',
    'HistogramAnalysis',
    'LineMeasurement',
    'ParallelLineFind',
    'PhaseClosure',
    'QuadrilateralFind'
]);
const HIGH_COST_METADATA_TOKENS = new Set([
    'ai-model',
    'onnx',
    'deep-learning',
    'ocr-model',
    'yolo-model',
    'template-matching',
    'feature-matching'
]);
const IMAGE_NODE_TYPE_FALLBACKS = new Set([
    'ImageAcquisition'
]);

/**
 * @typedef {{
 *   eligible: boolean,
 *   reason: string | null,
 *   source: string | null
 * }} PreviewEligibility
 */

/**
 * @typedef {{
 *   requestKey: string | null,
 *   projectId: string | null,
 *   nodeId: string | null,
 *   flowRevision: number,
 *   parameterSnapshot: string,
 *   inputImageHash: string,
 *   inputFrameId: string | null
 * }} PreviewRequestKey
 */

/**
 * @typedef {{
 *   title: string,
 *   statusText: string,
 *   inputImageSrc: string | null,
 *   outputImageSrc: string | null,
 *   summaryItems: Array<{ key: string, value: string, title?: string | null, kind?: string }>,
 *   overlayEnabled: boolean,
 *   canOpenImage: boolean,
 *   isLoading: boolean,
 *   hasError: boolean,
 *   errorMessage: string | null
 * }} PreviewPresenterState
 */

/**
 * @typedef {{
 *   activeNodeId: string | null,
 *   nodeType: string | null,
 *   title: string,
 *   status: 'idle' | 'loading' | 'success' | 'error' | 'canceled',
 *   executionTimeMs: number | null,
 *   errorMessage: string | null,
 *   canvasEligibility: PreviewEligibility,
 *   request: PreviewRequestKey,
 *   inputImageBase64: string | null,
 *   outputImageBase64: string | null,
 *   outputData: Record<string, unknown> | null,
 *   presenter: PreviewPresenterState
 * }} PreviewState
 */

function readFirstDefined(source, keys) {
    for (const key of keys) {
        if (source?.[key] !== undefined && source?.[key] !== null) {
            return source[key];
        }
    }

    return undefined;
}

function normalizeBase64Image(imageValue) {
    if (!imageValue || typeof imageValue !== 'string') {
        return null;
    }

    const trimmed = imageValue.trim();
    const commaIndex = trimmed.indexOf(',');
    if (trimmed.startsWith('data:image/') && commaIndex > 0) {
        return trimmed.substring(commaIndex + 1);
    }

    return trimmed;
}

function isPreviewPayloadTooLarge(imageBase64) {
    return typeof imageBase64 === 'string' && imageBase64.length > MAX_PREVIEW_INPUT_BASE64_CHARS;
}

function getCacheOutputImageBase64Chars(value) {
    return typeof value?.outputImageBase64 === 'string'
        ? value.outputImageBase64.length
        : 0;
}

function isPreviewOutputImageLikeValue(key, value) {
    if (typeof value !== 'string') {
        return false;
    }

    const text = value.trim();
    return isPreviewImageLikePayload(text) ||
        (PREVIEW_OUTPUT_IMAGE_KEY_PATTERN.test(String(key || '')) && text.length > 120);
}

function compactPreviewOutputString(key, value) {
    if (isPreviewOutputImageLikeValue(key, value)) {
        return '[image omitted]';
    }

    const text = String(value ?? '');
    return text.length > PREVIEW_OUTPUT_STRING_LIMIT
        ? `${text.slice(0, PREVIEW_OUTPUT_STRING_LIMIT)}...`
        : text;
}

function compactPreviewOutputValue(value, depth = 0, seen = new WeakSet(), sourceKey = '') {
    if (typeof value === 'string') {
        return compactPreviewOutputString(sourceKey, value);
    }

    if (value === null || value === undefined || typeof value !== 'object') {
        return value;
    }

    if (seen.has(value)) {
        return '[circular]';
    }

    if (depth >= PREVIEW_OUTPUT_MAX_DEPTH) {
        return Array.isArray(value)
            ? `${value.length} items`
            : `${Object.keys(value).length} fields`;
    }

    seen.add(value);

    if (Array.isArray(value)) {
        const compactItems = value
            .slice(0, PREVIEW_OUTPUT_ARRAY_LIMIT)
            .map(item => compactPreviewOutputValue(item, depth + 1, seen, sourceKey));
        if (value.length > compactItems.length) {
            compactItems.push(`+${value.length - compactItems.length} more`);
        }
        return compactItems;
    }

    const compact = {};
    const entries = Object.entries(value);
    let visibleCount = 0;
    let omittedImageCount = 0;
    for (const [key, entryValue] of entries) {
        if (isPreviewOutputImageLikeValue(key, entryValue)) {
            omittedImageCount += 1;
            continue;
        }

        if (visibleCount >= PREVIEW_OUTPUT_OBJECT_FIELD_LIMIT) {
            break;
        }

        compact[key] = compactPreviewOutputValue(entryValue, depth + 1, seen, key);
        visibleCount += 1;
    }

    const hiddenCount = Math.max(0, entries.length - visibleCount - omittedImageCount);
    if (hiddenCount > 0) {
        compact.__hiddenFieldCount = hiddenCount;
    }
    if (omittedImageCount > 0) {
        compact.__omittedImageFieldCount = omittedImageCount;
    }

    return compact;
}

export function extractPreviewImageBase64(result) {
    if (!result || typeof result !== 'object') {
        return null;
    }

    const candidateKeys = [
        'outputImage',
        'OutputImage',
        'outputImageBase64',
        'OutputImageBase64',
        'imageBase64',
        'ImageBase64',
        'resultImageBase64',
        'ResultImageBase64',
        'inputImage',
        'InputImage'
    ];

    for (const key of candidateKeys) {
        const value = result[key];
        if (typeof value === 'string' && isPreviewImageLikePayload(value)) {
            return normalizeBase64Image(value);
        }
    }

    const outputData = result.outputData || result.OutputData;
    if (outputData && typeof outputData === 'object') {
        for (const value of Object.values(outputData)) {
            if (typeof value === 'string' && isPreviewImageLikePayload(value)) {
                return normalizeBase64Image(value);
            }
        }
    }

    return null;
}

export function resolvePreviewInputImageBase64(result) {
    return extractPreviewImageBase64(result);
}

function toImageSource(imageBase64OrDataUrl) {
    if (!imageBase64OrDataUrl || typeof imageBase64OrDataUrl !== 'string') {
        return null;
    }

    const trimmed = imageBase64OrDataUrl.trim();
    if (!trimmed) {
        return null;
    }

    if (trimmed.startsWith('data:image/') || trimmed.startsWith('blob:')) {
        return trimmed;
    }

    return `data:image/png;base64,${trimmed}`;
}

function normalizePortType(value) {
    if (value === 0 || value === '0') {
        return 'image';
    }

    return String(value ?? '').trim().toLowerCase();
}

export function getCanvasPreviewEligibility(node, metadata = null) {
    const outputPorts = Array.isArray(node?.outputs) && node.outputs.length > 0
        ? node.outputs
        : (metadata?.outputPorts || metadata?.OutputPorts || []);

    const hasImageOutput = outputPorts.some(port => {
        const portType = port?.type ?? port?.Type ?? port?.dataType ?? port?.DataType;
        return normalizePortType(portType) === 'image';
    });

    if (hasImageOutput) {
        return {
            eligible: true,
            reason: null,
            source: Array.isArray(node?.outputs) && node.outputs.length > 0 ? 'node-ports' : 'metadata'
        };
    }

    if (IMAGE_NODE_TYPE_FALLBACKS.has(node?.type)) {
        return {
            eligible: true,
            reason: null,
            source: 'type-fallback'
        };
    }

    return {
        eligible: false,
        reason: 'no-image-output',
        source: null
    };
}

function normalizeText(value) {
    return String(value ?? '').trim().toLowerCase();
}

function normalizeMetadataToken(value) {
    return normalizeText(value)
        .replace(/[_\s]+/g, '-')
        .replace(/^-+|-+$/g, '');
}

function collectMetadataTokenValues(value, tokens) {
    if (value === null || value === undefined) {
        return;
    }

    if (Array.isArray(value)) {
        value.forEach(item => collectMetadataTokenValues(item, tokens));
        return;
    }

    const text = normalizeText(value);
    if (!text) {
        return;
    }

    tokens.add(normalizeMetadataToken(text));
    text.split(/[;,|/]+/u)
        .map(normalizeMetadataToken)
        .filter(Boolean)
        .forEach(token => tokens.add(token));
}

function getMetadataTokens(metadata) {
    const tokens = new Set();
    const tags = metadata?.tags || metadata?.Tags || [];
    const keywords = metadata?.keywords || metadata?.Keywords || [];
    [
        metadata?.type,
        metadata?.Type,
        metadata?.category,
        metadata?.Category,
        tags,
        keywords
    ].forEach(value => collectMetadataTokenValues(value, tokens));

    return tokens;
}

export function getOperatorPreviewCostPolicy(node, metadata = null) {
    if (!node) {
        return {
            level: 'light',
            autoPreviewAllowed: true,
            reason: null,
            timeoutMs: DEFAULT_PREVIEW_TIMEOUT_MS
        };
    }

    if (isCameraAcquisitionSourceNode(node)) {
        return {
            level: 'high',
            autoPreviewAllowed: false,
            reason: '相机采集会访问真实设备，自动预览已暂停；请使用手动预览或运行流程。',
            timeoutMs: HIGH_COST_PREVIEW_TIMEOUT_MS
        };
    }

    const type = String(node.type || metadata?.type || metadata?.Type || '');
    if (LIGHT_AUTO_PREVIEW_OPERATOR_TYPES.has(type)) {
        return {
            level: 'light',
            autoPreviewAllowed: true,
            reason: null,
            timeoutMs: DEFAULT_PREVIEW_TIMEOUT_MS
        };
    }

    const metadataTokens = getMetadataTokens(metadata);
    const isHighCost = HIGH_COST_OPERATOR_TYPES.has(type) ||
        Array.from(metadataTokens).some(token => HIGH_COST_METADATA_TOKENS.has(token));

    if (isHighCost) {
        return {
            level: 'high',
            autoPreviewAllowed: false,
            reason: '该算子可能执行 AI、OCR、模板或特征匹配等高成本计算，请点击“手动预览”执行。',
            timeoutMs: HIGH_COST_PREVIEW_TIMEOUT_MS
        };
    }

    return {
        level: 'light',
        autoPreviewAllowed: true,
        reason: null,
        timeoutMs: DEFAULT_PREVIEW_TIMEOUT_MS
    };
}

function stableSerialize(value) {
    if (value === null || value === undefined) {
        return 'null';
    }

    if (Array.isArray(value)) {
        return `[${value.map(item => stableSerialize(item)).join(',')}]`;
    }

    if (typeof value === 'object') {
        const keys = Object.keys(value).sort((a, b) => a.localeCompare(b));
        return `{${keys.map(key => `${JSON.stringify(key)}:${stableSerialize(value[key])}`).join(',')}}`;
    }

    return JSON.stringify(value);
}

function buildParameterSnapshot(parameters) {
    const normalized = (parameters || [])
        .map(parameter => ({
            name: String(parameter?.name || parameter?.Name || ''),
            value: parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue ?? null
        }))
        .sort((a, b) => a.name.localeCompare(b.name));

    return stableSerialize(normalized);
}

export function buildPreviewParameterSnapshot(parameters) {
    return buildParameterSnapshot(parameters);
}

export function buildCameraPreviewSourceSignature(node) {
    return hashString(stableSerialize({
        nodeType: node?.type || null,
        sourceType: normalizeAcquisitionSourceType(getParameterValue(node?.parameters, 'SourceType', 'sourceType')),
        cameraBindingId: getCameraAcquisitionBindingValue(node),
        triggerMode: String(getParameterValue(node?.parameters, 'TriggerMode', 'triggerMode') || '').trim(),
        exposureTime: getParameterValue(node?.parameters, 'ExposureTime', 'exposureTime'),
        gain: getParameterValue(node?.parameters, 'Gain', 'gain')
    }));
}

function createCameraPreviewFrameId() {
    return globalThis.crypto?.randomUUID?.()
        || `camera-frame-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function normalizeOptionalFiniteNumber(value) {
    if (value === null || value === undefined || value === '') {
        return null;
    }

    const numericValue = Number(value);
    return Number.isFinite(numericValue) ? numericValue : null;
}

export function createCameraPreviewInputContext(node, frame = {}) {
    const imageBase64 = normalizeBase64Image(frame.imageBase64 || frame.imageData || frame.imageSource);
    if (!node?.id || !imageBase64) {
        throw new Error('创建相机单帧预览上下文需要有效的采集节点和图像。');
    }

    return {
        imageBase64,
        sourceNodeId: node.id,
        projectId: frame.projectId || null,
        frameId: frame.frameId || createCameraPreviewFrameId(),
        sourceSignature: buildCameraPreviewSourceSignature(node),
        cameraBindingId: frame.cameraBindingId || null,
        triggerMode: frame.triggerMode || null,
        width: normalizeOptionalFiniteNumber(frame.width),
        height: normalizeOptionalFiniteNumber(frame.height),
        source: frame.source || 'camera-single-frame',
        capturedAtUtc: frame.capturedAtUtc || new Date().toISOString()
    };
}

export function isPreviewSourceReachable(sourceNodeId, targetNodeId, connections = []) {
    if (!sourceNodeId || !targetNodeId) {
        return false;
    }
    if (sourceNodeId === targetNodeId) {
        return true;
    }

    const visited = new Set([sourceNodeId]);
    const pending = [sourceNodeId];
    while (pending.length > 0) {
        const current = pending.shift();
        for (const connection of connections || []) {
            const source = connection?.source || connection?.sourceNodeId || connection?.sourceOperatorId;
            const target = connection?.target || connection?.targetNodeId || connection?.targetOperatorId;
            if (source !== current || !target || visited.has(target)) {
                continue;
            }
            if (target === targetNodeId) {
                return true;
            }
            visited.add(target);
            pending.push(target);
        }
    }

    return false;
}

export function resolveCameraPreviewInputFrame({
    frame,
    currentProjectId = null,
    sourceNode = null,
    targetNodeId = null,
    connections = []
} = {}) {
    if (!frame?.imageBase64) {
        return { frame: null, shouldInvalidate: false, message: '' };
    }

    if ((frame.projectId || null) !== (currentProjectId || null)) {
        return { frame: null, shouldInvalidate: true, message: '' };
    }

    const sourceNodeType = sourceNode?.type || sourceNode?.operatorType || null;
    if (!sourceNode || sourceNodeType !== 'ImageAcquisition' || sourceNode.disabled === true || sourceNode.isEnabled === false) {
        return {
            frame: null,
            shouldInvalidate: true,
            message: '原图像采集节点已不可用，请重新获取单帧图像。'
        };
    }

    if (frame.sourceSignature !== buildCameraPreviewSourceSignature(sourceNode)) {
        return {
            frame: null,
            shouldInvalidate: true,
            message: '相机采集配置已变更，请重新获取单帧图像。'
        };
    }

    if (targetNodeId && !isPreviewSourceReachable(frame.sourceNodeId, targetNodeId, connections)) {
        return { frame: null, shouldInvalidate: false, message: '' };
    }

    return { frame, shouldInvalidate: false, message: '' };
}

function hashString(input) {
    const text = String(input || '');
    let hash = 5381;
    for (let index = 0; index < text.length; index += 1) {
        hash = ((hash << 5) + hash) + text.charCodeAt(index);
        hash >>>= 0;
    }

    return hash.toString(16);
}

export function buildPreviewInputImageHash(inputImageBase64) {
    return inputImageBase64 ? hashString(inputImageBase64) : 'none';
}

function normalizePreviewInputContext(value) {
    if (typeof value === 'string') {
        return {
            imageBase64: normalizeBase64Image(value),
            sourceNodeId: null,
            frameId: null
        };
    }

    return {
        imageBase64: normalizeBase64Image(value?.imageBase64 || value?.imageData || value?.imageSource),
        sourceNodeId: value?.sourceNodeId || null,
        frameId: value?.frameId || null
    };
}

function createPresenterState(state) {
    let statusText = '等待预览';
    if (state.status === 'idle' && state.errorMessage) {
        statusText = state.errorMessage;
    } else if (state.status === 'loading') {
        statusText = '预览中...';
    } else if (state.status === 'success') {
        statusText = typeof state.executionTimeMs === 'number'
            ? `预览完成 (${state.executionTimeMs} ms)`
            : '预览完成';
    } else if (state.status === 'blocked') {
        statusText = state.errorMessage || '预览已安全拦截';
    } else if (state.status === 'canceled') {
        statusText = '预览已取消';
    } else if (state.status === 'auth-error') {
        statusText = state.errorMessage || '登录状态无效，请重新登录。';
    } else if (state.status === 'error') {
        statusText = `预览失败: ${state.errorMessage || '未知错误'}`;
    }

    return {
        title: state.title,
        statusText,
        inputImageSrc: toImageSource(state.inputImageBase64),
        outputImageSrc: toImageSource(state.outputImageBase64),
        summaryItems: buildPreviewSummaryItems(state.outputData, {
            maxItems: 3,
            stringMaxLength: 42,
            skipImageLikeValues: true,
            technicalLabels: true
        }),
        overlayEnabled: state.canvasEligibility.eligible,
        canOpenImage: Boolean(state.outputImageBase64),
        isLoading: state.status === 'loading',
        hasError: state.status === 'error',
        isBlocked: state.status === 'blocked',
        errorMessage: state.errorMessage
    };
}

function isSideEffectAdmissionBlockedError(error) {
    const payload = error?.payload || null;
    const code = String(payload?.code || payload?.Code || '').trim();
    if (/SIDE_EFFECT_BLOCKED/i.test(code)) {
        return true;
    }

    const message = String(error?.message || payload?.error || payload?.Error || '').trim();
    return /side-effect|副作用|外部 I\/O|持久化/.test(message) && /blocked|拦截|阻断/.test(message);
}

function normalizeSideEffectBlockedMessage(error) {
    const payload = error?.payload || null;
    const candidate = String(
        payload?.error ||
        payload?.Error ||
        payload?.message ||
        payload?.Message ||
        error?.message ||
        ''
    ).trim();

    if (candidate && !/blocked side-effect|external I\/O|persistent side effects/i.test(candidate)) {
        return candidate;
    }

    return SIDE_EFFECT_BLOCKED_PREVIEW_MESSAGE;
}

function getParameterValue(parameters, ...names) {
    const list = Array.isArray(parameters) ? parameters : [];
    for (const name of names) {
        const matched = list.find(parameter => String(parameter?.name || parameter?.Name || '').toLowerCase() === String(name).toLowerCase());
        if (!matched) {
            continue;
        }

        return matched?.value ?? matched?.Value ?? matched?.defaultValue ?? matched?.DefaultValue ?? null;
    }

    return null;
}

function shouldUseExternalInputImage(node) {
    return node?.type !== 'ImageAcquisition';
}

function getCameraAcquisitionBindingValue(node) {
    const cameraId = String(getParameterValue(node?.parameters, 'CameraId', 'cameraId') || '').trim();
    const cameraBindingId = String(getParameterValue(node?.parameters, 'CameraBindingId', 'cameraBindingId') || '').trim();
    return cameraId || cameraBindingId;
}

function isCameraAcquisitionSourceNode(node) {
    if (node?.type !== 'ImageAcquisition') {
        return false;
    }

    const sourceTypeRaw = getParameterValue(node.parameters, 'SourceType', 'sourceType');
    return normalizeAcquisitionSourceType(sourceTypeRaw) === 'camera';
}

function isLiveCameraAcquisitionNode(node) {
    return isCameraAcquisitionSourceNode(node) &&
        Boolean(getCameraAcquisitionBindingValue(node));
}

function validatePreviewPrerequisites(node, inputImageBase64) {
    if (!node) {
        return '未选中算子';
    }

    if (inputImageBase64 && shouldUseExternalInputImage(node)) {
        return null;
    }

    if (node.type === 'ImageAcquisition') {
        const sourceTypeRaw = getParameterValue(node.parameters, 'SourceType', 'sourceType');
        const sourceType = normalizeAcquisitionSourceType(sourceTypeRaw);
        const filePath = String(getParameterValue(node.parameters, 'FilePath', 'filePath') || '').trim();
        const cameraBinding = getCameraAcquisitionBindingValue(node);

        if (sourceType === 'file' && filePath) {
            return null;
        }

        if (sourceType === 'file' && !filePath) {
            return '请先配置文件路径';
        }

        if (sourceType === 'camera' && !cameraBinding) {
            return '请先选择相机';
        }

        if (!filePath && !cameraBinding) {
            return '请先配置采集源';
        }

        return null;
    }

    return null;
}

function createEmptyState() {
    const state = {
        activeNodeId: null,
        nodeType: null,
        title: '',
        status: 'idle',
        executionTimeMs: null,
        errorMessage: null,
        canvasEligibility: {
            eligible: false,
            reason: null,
            source: null
        },
        request: {
            requestKey: null,
            projectId: null,
            nodeId: null,
            flowRevision: 0,
            parameterSnapshot: '',
            inputImageHash: ''
        },
        inputImageBase64: null,
        outputImageBase64: null,
        outputData: null,
        observation: null,
        diagnostics: [],
        missingResources: [],
        failedOperatorId: null,
        failedOperatorName: null,
        failedOperatorType: null,
        artifacts: [],
        previewArtifactIds: [],
        previewArtifactObjectUrls: [],
        previewArtifactReleased: false,
        previewCost: {
            level: 'light',
            autoPreviewAllowed: true,
            reason: null,
            timeoutMs: DEFAULT_PREVIEW_TIMEOUT_MS
        },
        presenter: null
    };

    state.presenter = createPresenterState(state);
    return state;
}

function withClearedPreviewResources(patch = {}) {
    return {
        observation: null,
        diagnostics: [],
        missingResources: [],
        failedOperatorId: null,
        failedOperatorName: null,
        failedOperatorType: null,
        artifacts: [],
        previewArtifactIds: [],
        previewArtifactObjectUrls: [],
        previewArtifactReleased: false,
        ...patch
    };
}

function createPreviewArtifactAbortError(message) {
    if (typeof DOMException !== 'undefined') {
        const error = new DOMException(message, 'AbortError');
        error.previewArtifactResolutionAborted = true;
        return error;
    }

    const error = new Error(message);
    error.name = 'AbortError';
    error.previewArtifactResolutionAborted = true;
    return error;
}

function buildPreviewRequestKey({ projectId, nodeId, flowRevision, parameterSnapshot, inputImageBase64, inputFrameId = null }) {
    const inputImageHash = buildPreviewInputImageHash(inputImageBase64);
    return {
        requestKey: `${projectId || 'no-project'}:${nodeId || 'no-node'}:${flowRevision}:${hashString(parameterSnapshot)}:${inputImageHash}:${inputFrameId || 'no-frame'}`,
        projectId: projectId || null,
        nodeId: nodeId || null,
        flowRevision: Number(flowRevision || 0),
        parameterSnapshot,
        inputImageHash,
        inputFrameId: inputFrameId || null
    };
}

function buildPreviewScopeKey({ projectId, node }) {
    return [
        projectId || 'no-project',
        node?.id || 'no-node',
        node?.type || 'no-type',
        hashString(buildParameterSnapshot(node?.parameters))
    ].join(':');
}

function parsePreviewResponse(response) {
    const isSuccess = Boolean(readFirstDefined(response, ['success', 'Success']));
    return {
        isSuccess,
        inputImageBase64: normalizeBase64Image(readFirstDefined(response, ['inputImageBase64', 'InputImageBase64'])),
        outputImageBase64: normalizeBase64Image(readFirstDefined(response, ['outputImageBase64', 'OutputImageBase64'])),
        outputData: readFirstDefined(response, ['outputData', 'OutputData']) || null,
        observation: readFirstDefined(response, ['observation', 'Observation']) || null,
        artifacts: normalizeArtifactReferences(readFirstDefined(response, ['artifacts', 'Artifacts'])),
        executionTimeMs: readFirstDefined(response, ['executionTimeMs', 'ExecutionTimeMs']) ?? null,
        errorMessage: readFirstDefined(response, ['errorMessage', 'ErrorMessage']) || null,
        diagnostics: readFirstDefined(response, ['diagnostics', 'Diagnostics']) || [],
        missingResources: readFirstDefined(response, ['missingResources', 'MissingResources']) || [],
        failedOperatorId: readFirstDefined(response, ['failedOperatorId', 'FailedOperatorId']) || null,
        failedOperatorName: readFirstDefined(response, ['failedOperatorName', 'FailedOperatorName']) || null,
        failedOperatorType: readFirstDefined(response, ['failedOperatorType', 'FailedOperatorType']) || null
    };
}

function normalizeArtifactReferences(value) {
    if (!Array.isArray(value)) {
        return [];
    }

    return value
        .map(item => {
            const artifactId = readFirstDefined(item, ['artifactId', 'ArtifactId']);
            if (!artifactId || typeof artifactId !== 'string') {
                return null;
            }

            return {
                artifactId,
                kind: readFirstDefined(item, ['kind', 'Kind']) || '',
                role: readFirstDefined(item, ['role', 'Role']) || '',
                pathHint: readFirstDefined(item, ['pathHint', 'PathHint']) || '$',
                contentType: readFirstDefined(item, ['contentType', 'ContentType']) || 'application/octet-stream',
                length: Number(readFirstDefined(item, ['length', 'Length']) || 0),
                sha256: readFirstDefined(item, ['sha256', 'Sha256']) || '',
                width: readFirstDefined(item, ['width', 'Width']) ?? null,
                height: readFirstDefined(item, ['height', 'Height']) ?? null,
                channels: readFirstDefined(item, ['channels', 'Channels']) ?? null,
                createdAtUtc: readFirstDefined(item, ['createdAtUtc', 'CreatedAtUtc']) ?? null,
                expiresAtUtc: readFirstDefined(item, ['expiresAtUtc', 'ExpiresAtUtc']) ?? null
            };
        })
        .filter(Boolean);
}

function findArtifactByRole(artifacts, ...roles) {
    const roleSet = new Set(roles.map(role => String(role).toLowerCase()));
    return artifacts.find(artifact => roleSet.has(String(artifact.role || '').toLowerCase())) || null;
}

function appendArtifactDiagnostics(outputData, diagnostics) {
    if (!diagnostics.length) {
        return outputData;
    }

    const target = outputData && typeof outputData === 'object' && !Array.isArray(outputData)
        ? { ...outputData }
        : {};
    const existing = Array.isArray(target._previewArtifactDiagnostics)
        ? target._previewArtifactDiagnostics
        : [];
    target._previewArtifactDiagnostics = [...existing, ...diagnostics];
    return target;
}

function normalizeIdentityString(value) {
    return value === undefined || value === null
        ? null
        : String(value).trim().toLowerCase();
}

function normalizeIdentityNumber(value) {
    if (value === undefined || value === null) {
        return null;
    }

    const numberValue = Number(value);
    return Number.isSafeInteger(numberValue) && numberValue >= 0
        ? numberValue
        : null;
}

function normalizeFlowRevision(value) {
    const numberValue = Number(value ?? 0);
    return Number.isSafeInteger(numberValue) && numberValue >= 0
        ? numberValue
        : 0;
}

export function readPreviewObservationIdentity(response) {
    const observation = readFirstDefined(response, ['observation', 'Observation']);
    if (!observation || typeof observation !== 'object') {
        return null;
    }

    const identity = readFirstDefined(observation, ['identity', 'Identity']);
    if (!identity || typeof identity !== 'object') {
        return undefined;
    }

    return {
        projectId: normalizeIdentityString(readFirstDefined(identity, ['projectId', 'ProjectId'])),
        targetNodeId: normalizeIdentityString(readFirstDefined(identity, ['targetNodeId', 'TargetNodeId'])),
        debugSessionId: normalizeIdentityString(readFirstDefined(identity, ['debugSessionId', 'DebugSessionId'])),
        clientRequestSequence: normalizeIdentityNumber(readFirstDefined(identity, ['clientRequestSequence', 'ClientRequestSequence'])),
        flowRevision: normalizeIdentityNumber(readFirstDefined(identity, ['flowRevision', 'FlowRevision']))
    };
}

export function previewObservationMatchesRequest(response, expectedIdentity) {
    const identity = readPreviewObservationIdentity(response);
    if (identity === null) {
        return true;
    }
    if (!identity) {
        return false;
    }

    return identity.projectId === normalizeIdentityString(expectedIdentity.projectId) &&
        identity.targetNodeId === normalizeIdentityString(expectedIdentity.targetNodeId) &&
        identity.debugSessionId === normalizeIdentityString(expectedIdentity.debugSessionId) &&
        identity.clientRequestSequence === normalizeIdentityNumber(expectedIdentity.clientRequestSequence) &&
        identity.flowRevision === normalizeIdentityNumber(expectedIdentity.flowRevision);
}

function isAbortError(error) {
    return error?.name === 'AbortError';
}

function isImageArtifact(artifact) {
    return String(artifact?.contentType || '').toLowerCase().startsWith('image/') ||
        String(artifact?.kind || '').toLowerCase() === 'image';
}

function createArtifactUnavailableError(message = '资源已过期或不可用') {
    const error = new Error(message);
    error.name = 'PreviewArtifactUnavailableError';
    error.status = 404;
    return error;
}

export class NodePreviewCoordinator {
    constructor(options = {}) {
        this.getProjectId = options.getProjectId ?? (() => null);
        this.getFlowRevision = options.getFlowRevision ?? (() => 0);
        this.getNodeById = options.getNodeById ?? (() => null);
        this.getOperatorMetadata = options.getOperatorMetadata ?? (() => null);
        this.getInputImageBase64 = options.getInputImageBase64 ?? (() => null);
        this.getInputImageContext = options.getInputImageContext ?? (() => {
            const imageValue = this.getInputImageBase64();
            return imageValue && typeof imageValue.then === 'function'
                ? imageValue.then(imageBase64 => ({ imageBase64, sourceNodeId: null, frameId: null }))
                : { imageBase64: imageValue, sourceNodeId: null, frameId: null };
        });
        this.previewExecutor = options.previewExecutor ?? (async () => null);
        this.artifactClient = options.artifactClient ?? httpClient;
        this.debounceMs = options.debounceMs ?? DEFAULT_DEBOUNCE_MS;
        const maxCacheEntries = Number(options.maxCacheEntries ?? 30);
        this.maxCacheEntries = Number.isFinite(maxCacheEntries)
            ? Math.max(0, Math.floor(maxCacheEntries))
            : 30;
        const maxCacheOutputImageBase64Chars = Number(
            options.maxCacheOutputImageBase64Chars ?? DEFAULT_MAX_CACHE_OUTPUT_IMAGE_BASE64_CHARS);
        this.maxCacheOutputImageBase64Chars = Number.isFinite(maxCacheOutputImageBase64Chars)
            ? Math.max(0, Math.floor(maxCacheOutputImageBase64Chars))
            : DEFAULT_MAX_CACHE_OUTPUT_IMAGE_BASE64_CHARS;

        this.listeners = new Set();
        this.cache = new Map();
        this.state = createEmptyState();
        this.pendingTimer = null;
        this.pendingPreviewRequest = null;
        this.activeAbortController = null;
        this.requestVersion = 0;
        this.debugSessionId = null;
        this.debugSessionScopeKey = null;
        this.activeScopeKey = null;
        this.unsubscribeStructure = typeof options.subscribeStructureState === 'function'
            ? options.subscribeStructureState(() => this.handleStructureChanged())
            : null;
    }

    cancelActivePreviewRequest() {
        if (!this.activeAbortController) {
            return;
        }

        this.activeAbortController.abort();
        this.activeAbortController = null;
    }

    observePreviewRequestPromise(promise) {
        promise.catch(() => {});
        return promise;
    }

    cancelPendingPreviewRequest(result = { status: 'canceled' }) {
        if (this.pendingTimer) {
            clearTimeout(this.pendingTimer);
            this.pendingTimer = null;
        }

        const pending = this.pendingPreviewRequest;
        if (!pending) {
            return;
        }

        this.pendingPreviewRequest = null;
        pending.resolve(result);
    }

    failCurrentPreviewRequest(error, context = {}) {
        const {
            scheduledVersion = this.requestVersion,
            activeNode = null,
            request = this.state?.request || null,
            inputImageBase64 = null,
            previewCost = this.state?.previewCost || null,
            timedOut = false,
            effectiveTimeoutMs = null
        } = context;

        if (scheduledVersion !== this.requestVersion) {
            return false;
        }

        if (activeNode?.id && this.state.activeNodeId !== activeNode.id) {
            return false;
        }

        const timeoutSeconds = Number.isFinite(effectiveTimeoutMs)
            ? Math.round(effectiveTimeoutMs / 1000)
            : Math.round(DEFAULT_PREVIEW_TIMEOUT_MS / 1000);
        this.replacePreviewState(withClearedPreviewResources({
            status: 'error',
            executionTimeMs: null,
            errorMessage: timedOut
                ? `预览超时（${timeoutSeconds} 秒），已取消本次请求。`
                : (error?.message || '预览请求失败'),
            request,
            inputImageBase64: inputImageBase64 || null,
            outputImageBase64: null,
            outputData: null,
            previewCost: previewCost || this.state.previewCost
        }));
        return true;
    }

    cancelPreview(reason = '预览已取消') {
        this.cancelPendingPreviewRequest({ status: 'canceled', reason });

        this.requestVersion += 1;
        this.cancelActivePreviewRequest();
        if (!this.state.activeNodeId) {
            return;
        }

        this.replacePreviewState(withClearedPreviewResources({
            status: 'canceled',
            executionTimeMs: null,
            errorMessage: reason,
            outputImageBase64: null,
            outputData: null
        }));
    }

    releasePreviewResources(value) {
        if (!value || value.previewArtifactReleased) {
            return;
        }

        const objectUrls = Array.isArray(value.previewArtifactObjectUrls)
            ? Array.from(new Set(value.previewArtifactObjectUrls))
            : [];
        for (const objectUrl of objectUrls) {
            if (!objectUrl) {
                continue;
            }

            try {
                globalThis.URL?.revokeObjectURL?.(objectUrl);
            } catch (error) {
                console.warn('[NodePreviewCoordinator] Failed to revoke preview artifact object URL:', error);
            }
        }

        const artifactIds = Array.isArray(value.previewArtifactIds)
            ? Array.from(new Set(value.previewArtifactIds))
            : [];
        for (const artifactId of artifactIds) {
            if (!artifactId) {
                continue;
            }

            void this.artifactClient.deletePreviewArtifact?.(artifactId).catch(error => {
                if (!isAbortError(error)) {
                    console.warn('[NodePreviewCoordinator] Failed to delete preview artifact:', error);
                }
            });
        }

        value.previewArtifactReleased = true;
        value.previewArtifactObjectUrls = [];
        value.previewArtifactIds = [];
    }

    releaseCurrentPreviewResourcesIfUncached() {
        const requestKey = this.state?.request?.requestKey;
        if (requestKey && this.cache.has(requestKey)) {
            return;
        }

        this.releasePreviewResources(this.state);
    }

    releaseAllPreviewResources() {
        const requestKey = this.state?.request?.requestKey;
        if (!(requestKey && this.cache.has(requestKey))) {
            this.releasePreviewResources(this.state);
        }

        for (const value of this.cache.values()) {
            this.releasePreviewResources(value);
        }
        this.cache.clear();
    }

    releaseArtifactResourcesForNodeSwitch() {
        this.releaseCurrentPreviewResourcesIfUncached();
        for (const [key, value] of this.cache.entries()) {
            const hasArtifacts = (Array.isArray(value.previewArtifactIds) && value.previewArtifactIds.length > 0) ||
                (Array.isArray(value.previewArtifactObjectUrls) && value.previewArtifactObjectUrls.length > 0);
            if (!hasArtifacts) {
                continue;
            }

            this.releasePreviewResources(value);
            this.cache.delete(key);
        }
    }

    buildReleasedArtifactStatePatch() {
        const outputImageBase64 = String(this.state?.outputImageBase64 || '').startsWith('blob:')
            ? null
            : this.state?.outputImageBase64 ?? null;
        const inputImageBase64 = String(this.state?.inputImageBase64 || '').startsWith('blob:')
            ? null
            : this.state?.inputImageBase64 ?? null;

        return {
            inputImageBase64,
            outputImageBase64,
            artifacts: [],
            previewArtifactIds: [],
            previewArtifactObjectUrls: [],
            previewArtifactReleased: true
        };
    }

    releaseResponseArtifacts(response) {
        const parsed = parsePreviewResponse(response);
        this.releasePreviewResources({
            previewArtifactIds: parsed.artifacts.map(artifact => artifact.artifactId).filter(Boolean),
            previewArtifactObjectUrls: [],
            previewArtifactReleased: false
        });
    }

    async resolveArtifactImages(artifacts, signal, isCurrent = () => true) {
        const result = {
            inputImageSrc: null,
            outputImageSrc: null,
            previewArtifactIds: artifacts.map(artifact => artifact.artifactId).filter(Boolean),
            previewArtifactObjectUrls: [],
            diagnostics: []
        };

        const ensureCurrent = () => {
            if (signal?.aborted) {
                throw createPreviewArtifactAbortError('Preview artifact read aborted.');
            }
            if (!isCurrent()) {
                throw createPreviewArtifactAbortError('Preview artifact response is stale.');
            }
        };

        const inputArtifact = findArtifactByRole(artifacts, 'inputImage');
        const outputArtifact = findArtifactByRole(artifacts, 'outputImage', 'image');
        let readIndex = 0;
        for (const [slot, artifact] of [['inputImageSrc', inputArtifact], ['outputImageSrc', outputArtifact]]) {
            if (!artifact?.artifactId) {
                continue;
            }

            try {
                ensureCurrent();
                const response = await this.artifactClient.getPreviewArtifactBlob(artifact.artifactId, { signal });
                ensureCurrent();
                if (!globalThis.URL?.createObjectURL) {
                    result.diagnostics.push(`PreviewArtifactObjectUrlUnavailable:${artifact.role}`);
                    readIndex += 1;
                    continue;
                }

                const objectUrl = globalThis.URL.createObjectURL(response.blob);
                result.previewArtifactObjectUrls.push(objectUrl);
                result[slot] = objectUrl;
                readIndex += 1;
            } catch (error) {
                if (isAbortError(error)) {
                    this.releasePreviewResources(result);
                    throw error;
                }

                if (readIndex > 0 || result.previewArtifactObjectUrls.length > 0) {
                    this.releasePreviewResources(result);
                    throw createPreviewArtifactAbortError('Preview artifact resolution failed after partial reads.');
                }

                result.diagnostics.push(`PreviewArtifactReadFailed:${artifact.role || artifact.kind || 'artifact'}`);
                readIndex += 1;
            }
        }

        return result;
    }

    currentObservationMatches(expectedIdentity) {
        const observation = this.state?.observation;
        if (!observation || typeof observation !== 'object') {
            return false;
        }

        return previewObservationMatchesRequest({ observation }, expectedIdentity);
    }

    findCurrentArtifact(artifactId) {
        const safeArtifactId = String(artifactId || '');
        if (!safeArtifactId) {
            return null;
        }

        const artifacts = Array.isArray(this.state?.artifacts) ? this.state.artifacts : [];
        return artifacts.find(artifact => artifact?.artifactId === safeArtifactId) || null;
    }

    trackCurrentArtifactObjectUrl(artifactId, objectUrl) {
        if (!this.state || !artifactId || !objectUrl) {
            return;
        }

        if (!Array.isArray(this.state.previewArtifactIds)) {
            this.state.previewArtifactIds = [];
        }
        if (!Array.isArray(this.state.previewArtifactObjectUrls)) {
            this.state.previewArtifactObjectUrls = [];
        }
        if (!this.state.previewArtifactIds.includes(artifactId)) {
            this.state.previewArtifactIds.push(artifactId);
        }
        if (!this.state.previewArtifactObjectUrls.includes(objectUrl)) {
            this.state.previewArtifactObjectUrls.push(objectUrl);
        }
        this.state.previewArtifactReleased = false;
    }

    async readArtifactForCurrentState(artifactId, expectedIdentity, options = {}) {
        const artifact = this.findCurrentArtifact(artifactId);
        if (!artifact || !this.currentObservationMatches(expectedIdentity)) {
            throw createPreviewArtifactAbortError('Preview artifact request is stale.');
        }

        if (options?.signal?.aborted) {
            throw createPreviewArtifactAbortError('Preview artifact read aborted.');
        }

        try {
            const response = await this.artifactClient.getPreviewArtifactBlob(artifact.artifactId, {
                signal: options?.signal
            });

            if (options?.signal?.aborted) {
                throw createPreviewArtifactAbortError('Preview artifact read aborted.');
            }
            if (!this.currentObservationMatches(expectedIdentity) || !this.findCurrentArtifact(artifact.artifactId)) {
                throw createPreviewArtifactAbortError('Preview artifact response is stale.');
            }

            let objectUrl = null;
            if (options?.objectUrl === true && isImageArtifact(artifact)) {
                if (!globalThis.URL?.createObjectURL) {
                    throw createArtifactUnavailableError('资源已过期或不可用');
                }

                objectUrl = globalThis.URL.createObjectURL(response.blob);
                this.trackCurrentArtifactObjectUrl(artifact.artifactId, objectUrl);
            }

            return {
                artifact,
                blob: response.blob,
                headers: response.headers,
                objectUrl
            };
        } catch (error) {
            if (isAbortError(error) || error?.previewArtifactResolutionAborted) {
                throw error;
            }

            if (error?.status === 404 || error?.statusCode === 404) {
                throw createArtifactUnavailableError();
            }

            throw error;
        }
    }

    destroy() {
        this.cancelPendingPreviewRequest({ status: 'destroyed' });

        this.requestVersion += 1;
        this.cancelActivePreviewRequest();
        this.unsubscribeStructure?.();
        this.listeners.clear();
        this.releaseAllPreviewResources();
        this.debugSessionId = null;
        this.debugSessionScopeKey = null;
        this.activeScopeKey = null;
    }

    getState() {
        return this.state;
    }

    subscribe(listener) {
        if (typeof listener !== 'function') {
            return () => {};
        }

        this.listeners.add(listener);
        listener(this.state);
        return () => this.listeners.delete(listener);
    }

    updateState(patch) {
        this.state = {
            ...this.state,
            ...patch
        };
        this.state.presenter = createPresenterState(this.state);

        this.listeners.forEach(listener => {
            try {
                listener(this.state);
            } catch (error) {
                console.error('[NodePreviewCoordinator] Listener failed:', error);
            }
        });
    }

    replacePreviewState(patch) {
        this.releaseCurrentPreviewResourcesIfUncached();
        this.updateState(patch);
    }

    publishExternalFrame(node, frame = {}) {
        if (!node?.id) {
            throw new Error('发布单帧预览需要有效的算子节点。');
        }

        const imageBase64 = normalizeBase64Image(frame.imageBase64 || frame.imageData || frame.imageSource);
        if (!imageBase64) {
            throw new Error('单帧预览图像为空。');
        }
        if (isPreviewPayloadTooLarge(imageBase64)) {
            throw new Error('单帧图像过大，无法送入预览工作台。');
        }

        this.setActiveNode(node, { autoPreview: false });
        const metadata = this.getOperatorMetadata(node.type);
        const flowRevision = normalizeFlowRevision(this.getFlowRevision());
        const request = buildPreviewRequestKey({
            projectId: this.getProjectId(),
            nodeId: node.id,
            flowRevision,
            parameterSnapshot: buildParameterSnapshot(node.parameters),
            inputImageBase64: imageBase64,
            inputFrameId: frame.frameId || null
        });
        const outputData = compactPreviewOutputValue({
            Source: frame.source || 'camera-single-frame',
            CameraBindingId: frame.cameraBindingId || null,
            TriggerMode: frame.triggerMode || null,
            Width: Number.isFinite(Number(frame.width)) ? Number(frame.width) : null,
            Height: Number.isFinite(Number(frame.height)) ? Number(frame.height) : null,
            CapturedAtUtc: frame.capturedAtUtc || new Date().toISOString()
        });

        this.replacePreviewState(withClearedPreviewResources({
            activeNodeId: node.id,
            nodeType: node.type,
            title: node.title || metadata?.displayName || node.type,
            status: 'success',
            executionTimeMs: null,
            errorMessage: null,
            canvasEligibility: getCanvasPreviewEligibility(node, metadata),
            request,
            inputImageBase64: imageBase64,
            outputImageBase64: imageBase64,
            outputData,
            previewCost: getOperatorPreviewCostPolicy(node, metadata)
        }));

        return this.state;
    }

    setActiveNode(node, options = {}) {
        this.cancelPendingPreviewRequest({ status: 'superseded' });

        this.requestVersion += 1;
        this.cancelActivePreviewRequest();
        const previousNodeId = this.state.activeNodeId || null;

        if (!node?.id) {
            this.releaseArtifactResourcesForNodeSwitch();
            this.debugSessionId = null;
            this.debugSessionScopeKey = null;
            this.activeScopeKey = null;
            this.updateState(createEmptyState());
            return;
        }

        const nextScopeKey = buildPreviewScopeKey({
            projectId: this.getProjectId(),
            node
        });
        const nodeChanged = previousNodeId !== node.id;
        const scopeChanged = this.activeScopeKey !== nextScopeKey;
        if (nodeChanged || scopeChanged) {
            this.releaseArtifactResourcesForNodeSwitch();
        }

        if (nodeChanged) {
            this.debugSessionId = null;
            this.debugSessionScopeKey = null;
        }
        this.activeScopeKey = nextScopeKey;

        const metadata = this.getOperatorMetadata(node.type);
        const previewCost = getOperatorPreviewCostPolicy(node, metadata);
        if (nodeChanged) {
            this.updateState({
                ...createEmptyState(),
                activeNodeId: node.id,
                nodeType: node.type,
                title: node.title || metadata?.displayName || node.type,
                canvasEligibility: getCanvasPreviewEligibility(node, metadata),
                previewCost
            });
        } else {
            this.updateState({
                activeNodeId: node.id,
                nodeType: node.type,
                title: node.title || metadata?.displayName || node.type,
                canvasEligibility: getCanvasPreviewEligibility(node, metadata),
                previewCost,
                ...(scopeChanged ? withClearedPreviewResources({
                    ...this.buildReleasedArtifactStatePatch(),
                    status: 'idle',
                    executionTimeMs: null,
                    errorMessage: null,
                    inputImageBase64: null,
                    outputImageBase64: null,
                    outputData: null
                }) : {}),
                staleScopeKey: scopeChanged ? nextScopeKey : undefined
            });
        }

        if (options.autoPreview !== false) {
            this.requestActivePreview({ trigger: 'auto' });
        }
    }

    invalidateActivePreview(options = {}) {
        return this.requestActivePreview({
            ...options,
            force: true
        });
    }

    requestActivePreview(options = {}) {
        const {
            immediate = false,
            force = false,
            debounceMs = this.debounceMs,
            trigger = 'auto',
            timeoutMs = null
        } = options;
        if (!this.state.activeNodeId) {
            return Promise.resolve({ status: 'idle' });
        }

        const scheduledVersion = ++this.requestVersion;
        this.cancelActivePreviewRequest();
        this.cancelPendingPreviewRequest({ status: 'superseded' });

        let scheduledNode = null;
        try {
            scheduledNode = this.getNodeById(this.state.activeNodeId);
        } catch (error) {
            this.failCurrentPreviewRequest(error, { scheduledVersion });
            return this.observePreviewRequestPromise(Promise.reject(error));
        }
        if (!scheduledNode) {
            this.setActiveNode(null);
            return Promise.resolve({ status: 'cleared' });
        }

        // A new snapshot, node revision, or manual refresh must never leave an older
        // image or summary visible while the next preview is queued or executing.
        this.replacePreviewState(withClearedPreviewResources({
            status: 'loading',
            executionTimeMs: null,
            errorMessage: null,
            inputImageBase64: null,
            outputImageBase64: null,
            outputData: null
        }));

        try {
            const scheduledProjectId = this.getProjectId();
            const scheduledScopeKey = buildPreviewScopeKey({
                projectId: scheduledProjectId,
                node: scheduledNode
            });
            if (this.activeScopeKey !== scheduledScopeKey) {
                this.releaseArtifactResourcesForNodeSwitch();
                this.activeScopeKey = scheduledScopeKey;
                this.updateState({
                    ...this.buildReleasedArtifactStatePatch(),
                    staleScopeKey: scheduledScopeKey
                });
            }
        } catch (error) {
            this.failCurrentPreviewRequest(error, {
                scheduledVersion,
                activeNode: scheduledNode
            });
            return this.observePreviewRequestPromise(Promise.reject(error));
        }

        const execute = async () => {
            const activeNode = this.getNodeById(this.state.activeNodeId);
            if (scheduledVersion !== this.requestVersion) {
                return;
            }
            if (!activeNode) {
                this.setActiveNode(null);
                return;
            }

            const metadata = this.getOperatorMetadata(activeNode.type);
            const previewCost = getOperatorPreviewCostPolicy(activeNode, metadata);
            if (trigger === 'auto' && !previewCost.autoPreviewAllowed) {
                const prerequisiteError = validatePreviewPrerequisites(activeNode, null);
                this.replacePreviewState(withClearedPreviewResources({
                    status: 'idle',
                    executionTimeMs: null,
                    errorMessage: prerequisiteError || previewCost.reason,
                    inputImageBase64: null,
                    outputImageBase64: null,
                    outputData: null,
                    previewCost
                }));
                return;
            }

            const projectId = this.getProjectId();
            if (!projectId) {
                const flowRevision = normalizeFlowRevision(this.getFlowRevision());
                this.debugSessionId = null;
                this.debugSessionScopeKey = null;
                this.replacePreviewState(withClearedPreviewResources({
                    status: 'idle',
                    executionTimeMs: null,
                    errorMessage: MISSING_PROJECT_PREVIEW_MESSAGE,
                    inputImageBase64: null,
                    outputImageBase64: null,
                    outputData: null,
                    previewCost,
                    request: buildPreviewRequestKey({
                        projectId: null,
                        nodeId: activeNode.id,
                        flowRevision,
                        parameterSnapshot: buildParameterSnapshot(activeNode.parameters),
                        inputImageBase64: null
                    })
                }));
                return;
            }

            const previewInput = normalizePreviewInputContext(
                await Promise.resolve(this.getInputImageContext(activeNode)));
            const isExplicitCapturedSource = Boolean(previewInput.sourceNodeId) &&
                (activeNode.type !== 'ImageAcquisition' || previewInput.sourceNodeId === activeNode.id);
            const inputImageBase64 = shouldUseExternalInputImage(activeNode) || isExplicitCapturedSource
                ? previewInput.imageBase64
                : null;
            const inputImageSourceNodeId = inputImageBase64 && isExplicitCapturedSource
                ? previewInput.sourceNodeId
                : null;
            const inputFrameId = inputImageBase64 ? previewInput.frameId : null;
            if (scheduledVersion !== this.requestVersion || this.state.activeNodeId !== activeNode.id) {
                return;
            }

            if (isPreviewPayloadTooLarge(inputImageBase64)) {
                const flowRevision = normalizeFlowRevision(this.getFlowRevision());
                this.replacePreviewState(withClearedPreviewResources({
                    status: 'idle',
                    executionTimeMs: null,
                    errorMessage: '输入图像过大，已跳过预览。请先缩小图像或执行完整检测。',
                    inputImageBase64: null,
                    outputImageBase64: null,
                    outputData: null,
                    previewCost,
                    request: buildPreviewRequestKey({
                        projectId,
                        nodeId: activeNode.id,
                        flowRevision,
                        parameterSnapshot: buildParameterSnapshot(activeNode.parameters),
                        inputImageBase64: null
                    })
                }));
                return;
            }

            const prerequisiteError = validatePreviewPrerequisites(activeNode, inputImageBase64);
            if (prerequisiteError) {
                const flowRevision = normalizeFlowRevision(this.getFlowRevision());
                this.replacePreviewState(withClearedPreviewResources({
                    status: 'idle',
                    executionTimeMs: null,
                    errorMessage: prerequisiteError,
                    inputImageBase64: inputImageBase64 || null,
                    outputImageBase64: null,
                    outputData: null,
                    previewCost,
                    request: buildPreviewRequestKey({
                        projectId,
                        nodeId: activeNode.id,
                        flowRevision,
                        parameterSnapshot: buildParameterSnapshot(activeNode.parameters),
                        inputImageBase64
                    })
                }));
                return;
            }

            const flowRevision = normalizeFlowRevision(this.getFlowRevision());
            const clientRequestSequence = scheduledVersion;
            const request = buildPreviewRequestKey({
                projectId,
                nodeId: activeNode.id,
                flowRevision,
                parameterSnapshot: buildParameterSnapshot(activeNode.parameters),
                inputImageBase64,
                inputFrameId
            });

            const bypassCache = isLiveCameraAcquisitionNode(activeNode);
            const cached = this.cache.get(request.requestKey);
            if (!force && !bypassCache && cached) {
                this.cache.delete(request.requestKey);
                this.cache.set(request.requestKey, cached);
                this.releaseCurrentPreviewResourcesIfUncached();
                this.updateState({
                    ...cached,
                    request,
                    inputImageBase64: inputImageBase64 || null
                });
                return;
            }

            this.replacePreviewState(withClearedPreviewResources({
                status: 'loading',
                errorMessage: null,
                executionTimeMs: null,
                request,
                inputImageBase64: inputImageBase64 || null,
                outputImageBase64: null,
                outputData: null,
                previewCost
            }));

            const abortController = typeof AbortController !== 'undefined'
                ? new AbortController()
                : null;
            this.activeAbortController = abortController;
            const requestedTimeoutMs = Number(timeoutMs || previewCost.timeoutMs || DEFAULT_PREVIEW_TIMEOUT_MS);
            const effectiveTimeoutMs = Number.isFinite(requestedTimeoutMs)
                ? Math.max(1000, requestedTimeoutMs)
                : DEFAULT_PREVIEW_TIMEOUT_MS;
            let timeoutId = null;
            let timedOut = false;
            if (abortController && Number.isFinite(effectiveTimeoutMs)) {
                timeoutId = setTimeout(() => {
                    timedOut = true;
                    abortController.abort();
                }, effectiveTimeoutMs);
            }

            try {
                const debugSessionId = this.getDebugSessionId(projectId, activeNode.id);
                const expectedObservationIdentity = {
                    projectId,
                    targetNodeId: activeNode.id,
                    debugSessionId,
                    clientRequestSequence,
                    flowRevision
                };
                const response = await this.previewExecutor(activeNode.id, {
                    debugSessionId,
                    clientRequestSequence,
                    flowRevision,
                    inputImageBase64,
                    inputImageSourceNodeId,
                    parameters: null,
                    artifactMode: 'references',
                    signal: abortController?.signal,
                    timeoutMs: effectiveTimeoutMs
                });

                if (scheduledVersion !== this.requestVersion || this.state.activeNodeId !== activeNode.id) {
                    this.releaseResponseArtifacts(response);
                    return;
                }
                if (!previewObservationMatchesRequest(response, expectedObservationIdentity)) {
                    this.releaseResponseArtifacts(response);
                    return;
                }

                const parsed = parsePreviewResponse(response);
                const resolvedArtifacts = await this.resolveArtifactImages(
                    parsed.artifacts,
                    abortController?.signal,
                    () => scheduledVersion === this.requestVersion && this.state.activeNodeId === activeNode.id);
                if (scheduledVersion !== this.requestVersion || this.state.activeNodeId !== activeNode.id) {
                    this.releasePreviewResources({
                        previewArtifactIds: resolvedArtifacts.previewArtifactIds,
                        previewArtifactObjectUrls: resolvedArtifacts.previewArtifactObjectUrls
                    });
                    return;
                }

                const outputData = compactPreviewOutputValue(parsed.outputData);
                const outputDataWithDiagnostics = appendArtifactDiagnostics(outputData, resolvedArtifacts.diagnostics);
                if (parsed.outputImageBase64 && isPreviewPayloadTooLarge(parsed.outputImageBase64)) {
                    parsed.outputImageBase64 = null;
                    if (outputDataWithDiagnostics && typeof outputDataWithDiagnostics === 'object') {
                        outputDataWithDiagnostics._previewWarning = '输出图像过大，已省略图像，仅保留结构化摘要。';
                    }
                }

                const nextState = {
                    activeNodeId: activeNode.id,
                    nodeType: activeNode.type,
                    title: this.state.title,
                    status: parsed.isSuccess ? 'success' : 'error',
                    executionTimeMs: parsed.executionTimeMs,
                    errorMessage: parsed.isSuccess
                        ? null
                        : (parsed.failedOperatorName
                            ? `${parsed.failedOperatorName}: ${parsed.errorMessage || '预览执行失败'}`
                            : (parsed.errorMessage || '预览执行失败')),
                    canvasEligibility: this.state.canvasEligibility,
                    request,
                    inputImageBase64: resolvedArtifacts.inputImageSrc || parsed.inputImageBase64 || inputImageBase64 || null,
                    outputImageBase64: resolvedArtifacts.outputImageSrc || parsed.outputImageBase64,
                    outputData: outputDataWithDiagnostics,
                    observation: parsed.observation,
                    diagnostics: Array.isArray(parsed.diagnostics) ? parsed.diagnostics : [],
                    missingResources: Array.isArray(parsed.missingResources) ? parsed.missingResources : [],
                    failedOperatorId: parsed.failedOperatorId,
                    failedOperatorName: parsed.failedOperatorName,
                    failedOperatorType: parsed.failedOperatorType,
                    previewCost,
                    artifacts: parsed.artifacts,
                    previewArtifactIds: resolvedArtifacts.previewArtifactIds,
                    previewArtifactObjectUrls: resolvedArtifacts.previewArtifactObjectUrls,
                    previewArtifactReleased: false
                };

                const cacheableImage = !nextState.outputImageBase64 ||
                    nextState.outputImageBase64.length <= MAX_CACHE_IMAGE_BASE64_CHARS;
                if (!bypassCache && cacheableImage) {
                    this.setCacheEntry(request.requestKey, nextState);
                }

                this.releaseCurrentPreviewResourcesIfUncached();
                this.updateState(nextState);
            } catch (error) {
                if (isAbortError(error) && !timedOut) {
                    return;
                }

                if (scheduledVersion !== this.requestVersion || this.state.activeNodeId !== activeNode.id) {
                    return;
                }

                // 认证失效（401）不是算子/后端故障：单独用 auth-error 状态呈现登录态提示，
                // 且不写入 failedOperator 诊断，避免污染节点诊断、误导用户以为算子失效。
                // 全局 401 处理器会同时清理会话并引导重新登录。
                const isAuthError = error?.isAuthError === true || error?.status === 401 || error?.statusCode === 401;
                if (isAuthError && !timedOut) {
                    this.replacePreviewState(withClearedPreviewResources({
                        status: 'auth-error',
                        executionTimeMs: null,
                        errorMessage: PREVIEW_SESSION_INVALID_MESSAGE,
                        request,
                        inputImageBase64: inputImageBase64 || null,
                        outputImageBase64: null,
                        outputData: null,
                        previewCost
                    }));
                    return;
                }

                const sideEffectBlocked = !timedOut && isSideEffectAdmissionBlockedError(error);
                this.replacePreviewState(withClearedPreviewResources({
                    status: sideEffectBlocked ? 'blocked' : 'error',
                    executionTimeMs: null,
                    errorMessage: timedOut
                        ? `预览超时（${Math.round(effectiveTimeoutMs / 1000)} 秒），已取消本次请求。`
                        : (sideEffectBlocked
                            ? normalizeSideEffectBlockedMessage(error)
                            : (error?.message || '预览请求失败')),
                    request,
                    inputImageBase64: inputImageBase64 || null,
                    outputImageBase64: null,
                    outputData: null,
                    previewCost
                }));
            } finally {
                if (timeoutId !== null) {
                    clearTimeout(timeoutId);
                }
                if (this.activeAbortController === abortController) {
                    this.activeAbortController = null;
                }
            }
        };

        const executeRequest = () => execute().catch(error => {
            this.failCurrentPreviewRequest(error, {
                scheduledVersion,
                activeNode: scheduledNode
            });
            throw error;
        });

        if (immediate) {
            return this.observePreviewRequestPromise(executeRequest());
        }

        const deferred = new Promise((resolve, reject) => {
            const pending = { resolve, reject };
            this.pendingPreviewRequest = pending;
            this.pendingTimer = setTimeout(() => {
                this.pendingTimer = null;
                if (this.pendingPreviewRequest === pending) {
                    this.pendingPreviewRequest = null;
                }
                executeRequest().then(resolve, reject);
            }, debounceMs);
        });
        return this.observePreviewRequestPromise(deferred);
    }

    handleStructureChanged() {
        if (!this.state.activeNodeId) {
            return;
        }

        const activeNode = this.getNodeById(this.state.activeNodeId);
        if (!activeNode) {
            this.setActiveNode(null);
            return;
        }

        this.invalidateActivePreview({
            immediate: false
        });
    }

    getDebugSessionId(projectId, nodeId) {
        const scopeKey = `${projectId || ''}:${nodeId || ''}`;
        if (this.debugSessionScopeKey !== scopeKey || !this.debugSessionId) {
            this.debugSessionScopeKey = scopeKey;
            this.debugSessionId = generatePreviewDebugSessionId();
        }

        return this.debugSessionId;
    }

    setCacheEntry(requestKey, value) {
        if (this.maxCacheEntries <= 0) {
            return;
        }

        if (this.cache.has(requestKey)) {
            this.releasePreviewResources(this.cache.get(requestKey));
            this.cache.delete(requestKey);
        }

        this.cache.set(requestKey, {
            ...value,
            inputImageBase64: null
        });
        const protectKey = Array.isArray(value.previewArtifactIds) && value.previewArtifactIds.length > 0
            ? requestKey
            : null;
        this.pruneCache(protectKey);
    }

    getCachedOutputImageBase64Chars() {
        let total = 0;
        this.cache.forEach(value => {
            total += getCacheOutputImageBase64Chars(value);
        });

        return total;
    }

    pruneCache(protectedKey = null) {
        while (this.cache.size > this.maxCacheEntries) {
            const oldestKey = this.findOldestCacheKey(protectedKey);
            if (oldestKey === undefined) {
                break;
            }
            this.releasePreviewResources(this.cache.get(oldestKey));
            this.cache.delete(oldestKey);
        }

        while (
            this.cache.size > 0
            && this.getCachedOutputImageBase64Chars() > this.maxCacheOutputImageBase64Chars
        ) {
            const oldestKey = this.findOldestCacheKey(protectedKey);
            if (oldestKey === undefined) {
                break;
            }
            this.releasePreviewResources(this.cache.get(oldestKey));
            this.cache.delete(oldestKey);
        }
    }

    findOldestCacheKey(protectedKey = null) {
        for (const key of this.cache.keys()) {
            if (key !== protectedKey) {
                return key;
            }
        }

        return protectedKey
            ? undefined
            : this.cache.keys().next().value;
    }
}

function generatePreviewDebugSessionId() {
    const cryptoRef = globalThis.crypto;
    if (cryptoRef && typeof cryptoRef.randomUUID === 'function') {
        return cryptoRef.randomUUID();
    }

    if (cryptoRef && typeof cryptoRef.getRandomValues === 'function') {
        const bytes = new Uint8Array(16);
        cryptoRef.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;
        const hex = Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }

    throw new Error('Secure random generator is not available.');
}
