import {
    buildPreviewSummaryItems,
    isPreviewImageLikePayload
} from './previewOutputFormatter.mjs';
import { normalizeAcquisitionSourceType } from '../../shared/parameterDependencyRules.js';

const DEFAULT_DEBOUNCE_MS = 500;
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
const HIGH_COST_OPERATOR_TYPE_HINTS = [
    'DeepLearning',
    'OnnxInference',
    'OcrRecognition',
    'TemplateMatch',
    'TemplateMatching',
    'ShapeMatching',
    'PlanarMatching',
    'AkazeFeatureMatch',
    'FeatureMatch',
    'SemanticSegmentation',
    'SurfaceDefectDetection',
    'AnomalyDetection'
];
const HIGH_COST_TEXT_HINTS = [
    'ai',
    'deep learning',
    'onnx',
    'ocr',
    'yolo',
    'template',
    'matching',
    'feature',
    'segmentation',
    'defect'
];
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
 *   inputImageHash: string
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
 *   status: 'idle' | 'loading' | 'success' | 'error',
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

function containsAnyHint(value, hints) {
    const text = normalizeText(value);
    return Boolean(text) && hints.some(hint => text.includes(normalizeText(hint)));
}

function getMetadataText(metadata) {
    const tags = metadata?.tags || metadata?.Tags || [];
    const keywords = metadata?.keywords || metadata?.Keywords || [];
    return [
        metadata?.type,
        metadata?.Type,
        metadata?.displayName,
        metadata?.DisplayName,
        metadata?.description,
        metadata?.Description,
        metadata?.category,
        metadata?.Category,
        ...(Array.isArray(tags) ? tags : []),
        ...(Array.isArray(keywords) ? keywords : [])
    ].join(' ');
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

    if (isLiveCameraAcquisitionNode(node)) {
        return {
            level: 'high',
            autoPreviewAllowed: false,
            reason: '相机采集会触发真实取帧，请点击“刷新预览”手动执行。',
            timeoutMs: HIGH_COST_PREVIEW_TIMEOUT_MS
        };
    }

    const type = String(node.type || metadata?.type || metadata?.Type || '');
    const normalizedType = normalizeText(type);
    const metadataText = getMetadataText(metadata);
    const isHighCost = HIGH_COST_OPERATOR_TYPE_HINTS.some(hint => normalizedType.includes(normalizeText(hint))) ||
        containsAnyHint(metadataText || type, HIGH_COST_TEXT_HINTS);

    if (isHighCost) {
        return {
            level: 'high',
            autoPreviewAllowed: false,
            reason: '该算子可能执行 AI、OCR、模板或特征匹配等高成本计算，请点击“刷新预览”手动执行。',
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

function hashString(input) {
    const text = String(input || '');
    let hash = 5381;
    for (let index = 0; index < text.length; index += 1) {
        hash = ((hash << 5) + hash) + text.charCodeAt(index);
        hash >>>= 0;
    }

    return hash.toString(16);
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
            skipImageLikeValues: true
        }),
        overlayEnabled: state.canvasEligibility.eligible,
        canOpenImage: Boolean(state.outputImageBase64),
        isLoading: state.status === 'loading',
        hasError: state.status === 'error',
        errorMessage: state.errorMessage
    };
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

function isLiveCameraAcquisitionNode(node) {
    if (node?.type !== 'ImageAcquisition') {
        return false;
    }

    const sourceTypeRaw = getParameterValue(node.parameters, 'SourceType', 'sourceType');
    return normalizeAcquisitionSourceType(sourceTypeRaw) === 'camera';
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
        const cameraId = String(getParameterValue(node.parameters, 'CameraId', 'cameraId') || '').trim();

        if (sourceType === 'file' && filePath) {
            return null;
        }

        if (sourceType === 'file' && !filePath) {
            return '请先配置文件路径';
        }

        if (sourceType === 'camera' && !cameraId) {
            return '请先选择相机';
        }

        if (!filePath && !cameraId) {
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

function buildPreviewRequestKey({ projectId, nodeId, flowRevision, parameterSnapshot, inputImageBase64 }) {
    const inputImageHash = inputImageBase64 ? hashString(inputImageBase64) : 'none';
    return {
        requestKey: `${projectId || 'no-project'}:${nodeId || 'no-node'}:${flowRevision}:${hashString(parameterSnapshot)}:${inputImageHash}`,
        projectId: projectId || null,
        nodeId: nodeId || null,
        flowRevision: Number(flowRevision || 0),
        parameterSnapshot,
        inputImageHash
    };
}

function parsePreviewResponse(response) {
    const isSuccess = Boolean(readFirstDefined(response, ['success', 'Success']));
    return {
        isSuccess,
        inputImageBase64: normalizeBase64Image(readFirstDefined(response, ['inputImageBase64', 'InputImageBase64'])),
        outputImageBase64: normalizeBase64Image(readFirstDefined(response, ['outputImageBase64', 'OutputImageBase64'])),
        outputData: readFirstDefined(response, ['outputData', 'OutputData']) || null,
        executionTimeMs: readFirstDefined(response, ['executionTimeMs', 'ExecutionTimeMs']) ?? null,
        errorMessage: readFirstDefined(response, ['errorMessage', 'ErrorMessage']) || null,
        failedOperatorId: readFirstDefined(response, ['failedOperatorId', 'FailedOperatorId']) || null,
        failedOperatorName: readFirstDefined(response, ['failedOperatorName', 'FailedOperatorName']) || null,
        failedOperatorType: readFirstDefined(response, ['failedOperatorType', 'FailedOperatorType']) || null
    };
}

function isAbortError(error) {
    return error?.name === 'AbortError';
}

export class NodePreviewCoordinator {
    constructor(options = {}) {
        this.getProjectId = options.getProjectId ?? (() => null);
        this.getFlowRevision = options.getFlowRevision ?? (() => 0);
        this.getNodeById = options.getNodeById ?? (() => null);
        this.getOperatorMetadata = options.getOperatorMetadata ?? (() => null);
        this.getInputImageBase64 = options.getInputImageBase64 ?? (() => null);
        this.previewExecutor = options.previewExecutor ?? (async () => null);
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
        this.activeAbortController = null;
        this.requestVersion = 0;
        this.debugSessionId = null;
        this.debugSessionScopeKey = null;
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

    destroy() {
        if (this.pendingTimer) {
            clearTimeout(this.pendingTimer);
            this.pendingTimer = null;
        }

        this.requestVersion += 1;
        this.cancelActivePreviewRequest();
        this.unsubscribeStructure?.();
        this.listeners.clear();
        this.cache.clear();
        this.debugSessionId = null;
        this.debugSessionScopeKey = null;
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

    setActiveNode(node) {
        if (this.pendingTimer) {
            clearTimeout(this.pendingTimer);
            this.pendingTimer = null;
        }

        this.requestVersion += 1;
        this.cancelActivePreviewRequest();
        const previousNodeId = this.state.activeNodeId || null;

        if (!node?.id) {
            this.debugSessionId = null;
            this.debugSessionScopeKey = null;
            this.updateState(createEmptyState());
            return;
        }

        if (previousNodeId !== node.id) {
            this.debugSessionId = null;
            this.debugSessionScopeKey = null;
        }

        const metadata = this.getOperatorMetadata(node.type);
        const previewCost = getOperatorPreviewCostPolicy(node, metadata);
        this.updateState({
            ...createEmptyState(),
            activeNodeId: node.id,
            nodeType: node.type,
            title: node.title || metadata?.displayName || node.type,
            canvasEligibility: getCanvasPreviewEligibility(node, metadata),
            previewCost
        });

        this.requestActivePreview({ trigger: 'auto' });
    }

    invalidateActivePreview(options = {}) {
        this.requestActivePreview({
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
            return;
        }

        const scheduledVersion = ++this.requestVersion;
        this.cancelActivePreviewRequest();

        if (this.pendingTimer) {
            clearTimeout(this.pendingTimer);
            this.pendingTimer = null;
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
                this.updateState({
                    status: 'idle',
                    executionTimeMs: null,
                    errorMessage: previewCost.reason,
                    inputImageBase64: null,
                    outputImageBase64: null,
                    outputData: null,
                    previewCost
                });
                return;
            }

            const projectId = this.getProjectId();
            if (!projectId) {
                this.debugSessionId = null;
                this.debugSessionScopeKey = null;
                this.updateState({
                    status: 'idle',
                    executionTimeMs: null,
                    errorMessage: null,
                    inputImageBase64: null,
                    outputImageBase64: null,
                    outputData: null,
                    previewCost,
                    request: buildPreviewRequestKey({
                        projectId: null,
                        nodeId: activeNode.id,
                        flowRevision: this.getFlowRevision(),
                        parameterSnapshot: buildParameterSnapshot(activeNode.parameters),
                        inputImageBase64: null
                    })
                });
                return;
            }

            const inputImageBase64 = shouldUseExternalInputImage(activeNode)
                ? await Promise.resolve(this.getInputImageBase64())
                : null;
            if (scheduledVersion !== this.requestVersion || this.state.activeNodeId !== activeNode.id) {
                return;
            }

            if (isPreviewPayloadTooLarge(inputImageBase64)) {
                this.updateState({
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
                        flowRevision: this.getFlowRevision(),
                        parameterSnapshot: buildParameterSnapshot(activeNode.parameters),
                        inputImageBase64: null
                    })
                });
                return;
            }

            const prerequisiteError = validatePreviewPrerequisites(activeNode, inputImageBase64);
            if (prerequisiteError) {
                this.updateState({
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
                        flowRevision: this.getFlowRevision(),
                        parameterSnapshot: buildParameterSnapshot(activeNode.parameters),
                        inputImageBase64
                    })
                });
                return;
            }

            const request = buildPreviewRequestKey({
                projectId,
                nodeId: activeNode.id,
                flowRevision: this.getFlowRevision(),
                parameterSnapshot: buildParameterSnapshot(activeNode.parameters),
                inputImageBase64
            });

            const bypassCache = isLiveCameraAcquisitionNode(activeNode);
            const cached = this.cache.get(request.requestKey);
            if (!force && !bypassCache && cached) {
                this.cache.delete(request.requestKey);
                this.cache.set(request.requestKey, cached);
                this.updateState({
                    ...cached,
                    request,
                    inputImageBase64: inputImageBase64 || null
                });
                return;
            }

            this.updateState({
                status: 'loading',
                errorMessage: null,
                executionTimeMs: null,
                request,
                inputImageBase64: inputImageBase64 || null,
                previewCost
            });

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
                const response = await this.previewExecutor(activeNode.id, {
                    debugSessionId: this.getDebugSessionId(projectId, activeNode.id),
                    inputImageBase64,
                    parameters: null,
                    signal: abortController?.signal,
                    timeoutMs: effectiveTimeoutMs
                });

                if (scheduledVersion !== this.requestVersion || this.state.activeNodeId !== activeNode.id) {
                    return;
                }

                const parsed = parsePreviewResponse(response);
                const outputData = compactPreviewOutputValue(parsed.outputData);
                if (parsed.outputImageBase64 && isPreviewPayloadTooLarge(parsed.outputImageBase64)) {
                    parsed.outputImageBase64 = null;
                    if (outputData && typeof outputData === 'object') {
                        outputData._previewWarning = '输出图像过大，已省略图像，仅保留结构化摘要。';
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
                    inputImageBase64: parsed.inputImageBase64 || inputImageBase64 || null,
                    outputImageBase64: parsed.outputImageBase64,
                    outputData,
                    previewCost
                };

                const cacheableImage = !nextState.outputImageBase64 ||
                    nextState.outputImageBase64.length <= MAX_CACHE_IMAGE_BASE64_CHARS;
                if (!bypassCache && cacheableImage) {
                    this.setCacheEntry(request.requestKey, nextState);
                }

                this.updateState(nextState);
            } catch (error) {
                if (isAbortError(error) && !timedOut) {
                    return;
                }

                if (scheduledVersion !== this.requestVersion || this.state.activeNodeId !== activeNode.id) {
                    return;
                }

                this.updateState({
                    status: 'error',
                    executionTimeMs: null,
                    errorMessage: timedOut
                        ? `预览超时（${Math.round(effectiveTimeoutMs / 1000)} 秒），已取消本次请求。`
                        : (error?.message || '预览请求失败'),
                    request,
                    inputImageBase64: inputImageBase64 || null,
                    outputImageBase64: null,
                    outputData: null,
                    previewCost
                });
            } finally {
                if (timeoutId !== null) {
                    clearTimeout(timeoutId);
                }
                if (this.activeAbortController === abortController) {
                    this.activeAbortController = null;
                }
            }
        };

        if (immediate) {
            void execute();
            return;
        }

        this.pendingTimer = setTimeout(() => {
            this.pendingTimer = null;
            void execute();
        }, debounceMs);
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
            this.cache.delete(requestKey);
        }

        this.cache.set(requestKey, {
            ...value,
            inputImageBase64: null
        });
        this.pruneCache();
    }

    getCachedOutputImageBase64Chars() {
        let total = 0;
        this.cache.forEach(value => {
            total += getCacheOutputImageBase64Chars(value);
        });

        return total;
    }

    pruneCache() {
        while (this.cache.size > this.maxCacheEntries) {
            const oldestKey = this.cache.keys().next().value;
            if (oldestKey === undefined) {
                break;
            }
            this.cache.delete(oldestKey);
        }

        while (
            this.cache.size > 0
            && this.getCachedOutputImageBase64Chars() > this.maxCacheOutputImageBase64Chars
        ) {
            const oldestKey = this.cache.keys().next().value;
            if (oldestKey === undefined) {
                break;
            }
            this.cache.delete(oldestKey);
        }
    }
}

function generatePreviewDebugSessionId() {
    const cryptoRef = globalThis.crypto;
    if (cryptoRef && typeof cryptoRef.randomUUID === 'function') {
        return cryptoRef.randomUUID();
    }

    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, char => {
        const value = Math.random() * 16 | 0;
        const nibble = char === 'x' ? value : (value & 0x3 | 0x8);
        return nibble.toString(16);
    });
}
