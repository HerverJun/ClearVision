import {
    formatPreviewOutputValue,
    isPreviewImageLikePayload
} from './previewOutputFormatter.mjs';
import { buildPreviewParameterSnapshot } from './previewCoordinator.js';

export const MAX_OPERATOR_RESULT_RAW_JSON_CHARS = 4096;
export const MAX_OPERATOR_RESULT_STRING_CHARS = 512;
export const MAX_OPERATOR_RESULT_TREE_ROWS = 48;
export const MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES = 64 * 1024;
export const MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS = 4096;

const SECRET_KEY_PATTERN = /(password|passwd|pwd|secret|token|api[-_]?key|authorization|credential|private[-_]?key|connectionstring)/i;
const WINDOWS_ABSOLUTE_PATH_PATTERN = /\b[A-Za-z]:[\\/][^\s"'<>|]+/g;
const UNC_PATH_PATTERN = /\\\\[^\\/\s"'<>|]+[\\/][^\s"'<>|]+/g;
const POSIX_LOCAL_PATH_PATTERN = /(^|[\s"'(:=])\/(?:Users|home|var\/folders|tmp)\/[^\s"'<>]+/g;

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

function asString(value, fallback = '') {
    if (value === undefined || value === null) {
        return fallback;
    }

    return String(value);
}

function normalizeBool(value) {
    return value === true || value === 'true' || value === 1;
}

function clipText(value, maxChars = MAX_OPERATOR_RESULT_STRING_CHARS) {
    const text = asString(value);
    return text.length <= maxChars ? text : `${text.slice(0, maxChars)}...`;
}

function formatByteLength(value) {
    const bytes = Number(value || 0);
    if (!Number.isFinite(bytes) || bytes <= 0) {
        return '-';
    }

    if (bytes < 1024) {
        return `${bytes} B`;
    }

    if (bytes < 1024 * 1024) {
        return `${(bytes / 1024).toFixed(1)} KB`;
    }

    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function normalizeKind(value) {
    return asString(value, 'unknown').trim().toLowerCase();
}

function getObservation(state) {
    const observation = readOwn(state, 'observation', 'Observation');
    return observation && typeof observation === 'object' ? observation : null;
}

function getObservationIdentity(observation) {
    const identity = readOwn(observation, 'identity', 'Identity');
    return identity && typeof identity === 'object' ? identity : null;
}

function getObservationDetail(observation) {
    const detail = readOwn(observation, 'detail', 'Detail');
    return detail && typeof detail === 'object' ? detail : null;
}

function getObservationOutcome(observation) {
    const outcome = readOwn(observation, 'outcome', 'Outcome');
    return outcome && typeof outcome === 'object' ? outcome : null;
}

function getObservationVisualScene(observation) {
    const scene = readOwn(observation, 'visualScene', 'VisualScene');
    return scene && typeof scene === 'object' ? scene : null;
}

function normalizeObservationNode(node) {
    const children = readOwn(node, 'children', 'Children');
    const artifact = normalizeArtifactReference(readOwn(node, 'artifact', 'Artifact'));
    const kind = asString(readOwn(node, 'kind', 'Kind'), 'unknown');
    return {
        kind,
        kindKey: normalizeKind(kind),
        name: readOwn(node, 'name', 'Name') === undefined ? null : asString(readOwn(node, 'name', 'Name')),
        displayValue: readOwn(node, 'displayValue', 'DisplayValue') === undefined
            ? ''
            : asString(readOwn(node, 'displayValue', 'DisplayValue')),
        originalType: readOwn(node, 'originalType', 'OriginalType') === undefined
            ? null
            : asString(readOwn(node, 'originalType', 'OriginalType')),
        pathHint: asString(readOwn(node, 'pathHint', 'PathHint'), '$'),
        addressable: normalizeBool(readOwn(node, 'addressable', 'Addressable')),
        locatable: normalizeBool(readOwn(node, 'locatable', 'Locatable')),
        truncated: normalizeBool(readOwn(node, 'truncated', 'Truncated')),
        outputPortId: readOwn(node, 'outputPortId', 'OutputPortId') ?? null,
        outputPortName: readOwn(node, 'outputPortName', 'OutputPortName') ?? null,
        resultPathVersion: readOwn(node, 'resultPathVersion', 'ResultPathVersion') ?? null,
        resultPath: readOwn(node, 'resultPath', 'ResultPath') ?? null,
        artifact,
        childCount: Array.isArray(children) ? children.length : 0
    };
}

function getNodeChildren(node) {
    const children = readOwn(node, 'children', 'Children');
    return Array.isArray(children) ? children : [];
}

function isScalarKind(kindKey) {
    return [
        'null',
        'boolean',
        'number',
        'string',
        'enum',
        'guid',
        'datetime',
        'duration',
        'nonfinitenumber'
    ].includes(kindKey);
}

function isGeometryKind(kindKey, originalType = '') {
    const type = asString(originalType).toLowerCase();
    return [
        'point',
        'circle',
        'line',
        'rectangle',
        'rect',
        'detection',
        'calibrationquality'
    ].includes(kindKey) ||
        ['point', 'circle', 'line', 'rectangle', 'rect', 'calibrationquality'].some(item =>
            type === item || type.endsWith(`.${item}`) || type.endsWith(`+${item}`));
}

function isResourceKind(kindKey) {
    return ['matrix', 'image', 'mask', 'binary', 'stream', 'resource', 'profile', 'pointset'].includes(kindKey);
}

function classifyObservationRow(normalized) {
    if (normalized.artifact || isResourceKind(normalized.kindKey)) {
        return 'artifact';
    }

    if (isScalarKind(normalized.kindKey)) {
        return 'scalar';
    }

    if (isGeometryKind(normalized.kindKey, normalized.originalType)) {
        return 'geometry';
    }

    if (normalized.kindKey === 'array' || normalized.kindKey === 'detectionlist' || normalized.childCount > 0) {
        return 'table';
    }

    return 'json';
}

function collectObservationRows(root, limit = MAX_OPERATOR_RESULT_TREE_ROWS) {
    if (!root || typeof root !== 'object') {
        return {
            rows: [],
            truncated: false
        };
    }

    const rows = [];
    const stack = [{ node: root, depth: 0 }];
    let visited = 0;

    while (stack.length > 0) {
        const current = stack.pop();
        visited += 1;
        if (visited > limit) {
            return {
                rows,
                truncated: true
            };
        }

        const normalized = normalizeObservationNode(current.node);
        rows.push({
            normalized,
            depth: current.depth,
            category: classifyObservationRow(normalized)
        });

        const children = getNodeChildren(current.node);
        for (let index = children.length - 1; index >= 0; index -= 1) {
            stack.push({
                node: children[index],
                depth: current.depth + 1
            });
        }
    }

    return {
        rows,
        truncated: false
    };
}

export function normalizeArtifactReference(artifact) {
    if (!artifact || typeof artifact !== 'object') {
        return null;
    }

    const artifactId = readOwn(artifact, 'artifactId', 'ArtifactId');
    if (!artifactId || typeof artifactId !== 'string') {
        return null;
    }

    return {
        artifactId,
        kind: asString(readOwn(artifact, 'kind', 'Kind')),
        role: asString(readOwn(artifact, 'role', 'Role')),
        pathHint: asString(readOwn(artifact, 'pathHint', 'PathHint'), '$'),
        contentType: asString(readOwn(artifact, 'contentType', 'ContentType'), 'application/octet-stream'),
        length: Number(readOwn(artifact, 'length', 'Length') || 0),
        sha256: asString(readOwn(artifact, 'sha256', 'Sha256')),
        width: readOwn(artifact, 'width', 'Width') ?? null,
        height: readOwn(artifact, 'height', 'Height') ?? null,
        channels: readOwn(artifact, 'channels', 'Channels') ?? null,
        createdAtUtc: readOwn(artifact, 'createdAtUtc', 'CreatedAtUtc') ?? null,
        expiresAtUtc: readOwn(artifact, 'expiresAtUtc', 'ExpiresAtUtc') ?? null
    };
}

function normalizeArtifacts(value) {
    return Array.isArray(value)
        ? value.map(normalizeArtifactReference).filter(Boolean)
        : [];
}

export function isTextArtifactForResultPanel(artifact) {
    const contentType = asString(artifact?.contentType).toLowerCase();
    const kind = asString(artifact?.kind).toLowerCase();
    return contentType.includes('json') ||
        contentType.startsWith('text/') ||
        kind === 'profile' ||
        kind === 'json';
}

export function redactLocalAbsolutePaths(text) {
    return asString(text)
        .replace(WINDOWS_ABSOLUTE_PATH_PATTERN, '[redacted-path]')
        .replace(UNC_PATH_PATTERN, '[redacted-path]')
        .replace(POSIX_LOCAL_PATH_PATTERN, (_match, prefix) => `${prefix}[redacted-path]`);
}

export function sanitizeResultValue(value, options = {}, path = '$', seen = new WeakSet()) {
    const {
        depth = 0,
        maxDepth = 5,
        maxArrayItems = 24,
        maxObjectFields = 48,
        maxStringChars = MAX_OPERATOR_RESULT_STRING_CHARS
    } = options;

    const key = path.split('.').pop() || path;
    if (SECRET_KEY_PATTERN.test(key)) {
        return '[redacted-secret]';
    }

    if (typeof value === 'string') {
        if (isPreviewImageLikePayload(value)) {
            return '[image omitted]';
        }

        return clipText(redactLocalAbsolutePaths(value), maxStringChars);
    }

    if (value === null || value === undefined || typeof value !== 'object') {
        return value;
    }

    if (seen.has(value)) {
        return '[circular]';
    }

    if (depth >= maxDepth) {
        return Array.isArray(value)
            ? `[${value.length} items]`
            : `{${Object.keys(value).length} fields}`;
    }

    seen.add(value);

    if (Array.isArray(value)) {
        const result = value
            .slice(0, maxArrayItems)
            .map((item, index) => sanitizeResultValue(item, {
                ...options,
                depth: depth + 1
            }, `${path}[${index}]`, seen));
        if (value.length > result.length) {
            result.push(`[+${value.length - result.length} more]`);
        }
        return result;
    }

    const result = {};
    const entries = Object.entries(value);
    let visibleCount = 0;
    for (const [entryKey, entryValue] of entries) {
        if (visibleCount >= maxObjectFields) {
            break;
        }

        result[entryKey] = SECRET_KEY_PATTERN.test(entryKey)
            ? '[redacted-secret]'
            : sanitizeResultValue(entryValue, {
                ...options,
                depth: depth + 1
            }, `${path}.${entryKey}`, seen);
        visibleCount += 1;
    }

    if (entries.length > visibleCount) {
        result.__truncatedFieldCount = entries.length - visibleCount;
    }

    return result;
}

export function buildSafeJsonPreview(value, options = {}) {
    const maxChars = Number.isFinite(options.maxChars)
        ? Math.max(128, Math.floor(options.maxChars))
        : MAX_OPERATOR_RESULT_RAW_JSON_CHARS;
    const sanitized = sanitizeResultValue(value, options);
    let text = '';

    try {
        text = JSON.stringify(sanitized, null, 2);
    } catch {
        text = '"[unserializable]"';
    }

    const redacted = redactLocalAbsolutePaths(text);
    return redacted.length <= maxChars
        ? {
            text: redacted,
            truncated: false
        }
        : {
            text: `${redacted.slice(0, maxChars)}\n...已截断`,
            truncated: true
        };
}

function getScenePrimitives(scene) {
    const primitives = readOwn(scene, 'primitives', 'Primitives');
    return Array.isArray(primitives) ? primitives : [];
}

function getSceneDiagnostics(scene) {
    const diagnostics = readOwn(scene, 'diagnostics', 'Diagnostics');
    return Array.isArray(diagnostics) ? diagnostics : [];
}

function summarizeScene(scene) {
    if (!scene) {
        return {
            available: false,
            message: '该算子暂无可视化叠加',
            primitives: [],
            diagnostics: []
        };
    }

    const primitives = getScenePrimitives(scene);
    const diagnostics = getSceneDiagnostics(scene);
    if (primitives.length === 0) {
        return {
            available: false,
            message: '该算子暂无可视化叠加',
            primitives: [],
            diagnostics
        };
    }

    const imageWidth = readOwn(scene, 'imageWidth', 'ImageWidth');
    const imageHeight = readOwn(scene, 'imageHeight', 'ImageHeight');
    return {
        available: true,
        message: `${primitives.length} 个可视化叠加 primitive`,
        coordinateSpace: readOwn(scene, 'coordinateSpace', 'CoordinateSpace') || '-',
        frameId: readOwn(scene, 'frameId', 'FrameId') || null,
        unit: readOwn(scene, 'unit', 'Unit') || null,
        imageSize: Number(imageWidth) > 0 && Number(imageHeight) > 0
            ? `${imageWidth} x ${imageHeight}`
            : null,
        truncated: normalizeBool(readOwn(scene, 'truncated', 'Truncated')),
        primitives: primitives.slice(0, 16).map(primitive => ({
            primitiveId: asString(readOwn(primitive, 'primitiveId', 'PrimitiveId')),
            kind: asString(readOwn(primitive, 'kind', 'Kind'), 'primitive'),
            layer: asString(readOwn(primitive, 'layer', 'Layer')),
            label: asString(readOwn(primitive, 'label', 'Label')),
            outputPortId: readOwn(primitive, 'outputPortId', 'OutputPortId') ?? null,
            resultPathVersion: readOwn(primitive, 'resultPathVersion', 'ResultPathVersion') ?? null,
            resultPath: readOwn(primitive, 'resultPath', 'ResultPath') ?? null
        })),
        primitiveCount: primitives.length,
        diagnostics
    };
}

function normalizeDiagnostic(item, source = 'diagnostic') {
    if (typeof item === 'string') {
        return {
            source,
            code: source,
            message: clipText(item, 240),
            pathHint: null
        };
    }

    if (!item || typeof item !== 'object') {
        return null;
    }

    return {
        source,
        code: asString(readOwn(item, 'code', 'Code'), source),
        message: clipText(readOwn(item, 'message', 'Message') ?? item, 240),
        pathHint: readOwn(item, 'pathHint', 'PathHint') ?? null
    };
}

function collectDiagnostics(state, observation, scene) {
    const result = [];
    if (state?.errorMessage) {
        result.push({
            source: 'preview',
            code: 'preview',
            message: clipText(state.errorMessage, 240),
            pathHint: null
        });
    }

    const observationDiagnostics = readOwn(observation, 'diagnostics', 'Diagnostics');
    if (Array.isArray(observationDiagnostics)) {
        observationDiagnostics
            .map(item => normalizeDiagnostic(item, 'observation'))
            .filter(Boolean)
            .forEach(item => result.push(item));
    }

    getSceneDiagnostics(scene)
        .map(item => normalizeDiagnostic(item, 'scene'))
        .filter(Boolean)
        .forEach(item => result.push(item));

    const artifactDiagnostics = state?.outputData?._previewArtifactDiagnostics;
    if (Array.isArray(artifactDiagnostics)) {
        artifactDiagnostics
            .map(item => normalizeDiagnostic(item, 'artifact'))
            .filter(Boolean)
            .forEach(item => result.push(item));
    }

    return result.slice(0, 16);
}

function buildOutputSections(observation, outputData, artifacts) {
    const detail = getObservationDetail(observation);
    const collected = collectObservationRows(detail);
    const sections = {
        scalar: [],
        table: [],
        geometry: [],
        artifact: [],
        json: []
    };

    for (const row of collected.rows) {
        if (row.depth === 0 && row.category === 'table') {
            continue;
        }

        const normalized = row.normalized;
        const formatted = formatPreviewOutputValue(normalized.name || normalized.pathHint, normalized.displayValue, {
            stringMaxLength: 96
        });
        const item = {
            key: normalized.name || normalized.pathHint,
            pathHint: normalized.pathHint,
            kind: normalized.kind,
            value: formatted.text || clipText(normalized.displayValue, 96),
            meta: normalized.originalType || null,
            resultPathVersion: normalized.resultPathVersion,
            resultPath: normalized.resultPath,
            artifact: normalized.artifact
        };

        sections[row.category]?.push(item);
    }

    if (Array.isArray(artifacts) && artifacts.length > 0) {
        artifacts.forEach(artifact => {
            if (!sections.artifact.some(item => item.artifact?.artifactId === artifact.artifactId)) {
                sections.artifact.push({
                    key: artifact.role || artifact.kind || artifact.artifactId,
                    pathHint: artifact.pathHint,
                    kind: artifact.kind || 'artifact',
                    value: `${artifact.contentType || 'artifact'} ${formatByteLength(artifact.length)}`,
                    meta: artifact.artifactId,
                    resultPathVersion: null,
                    resultPath: null,
                    artifact
                });
            }
        });
    }

    if ((!detail || collected.rows.length === 0) && outputData && typeof outputData === 'object') {
        Object.entries(outputData)
            .filter(([, value]) => !(typeof value === 'string' && isPreviewImageLikePayload(value)))
            .slice(0, 16)
            .forEach(([key, value]) => {
                const formatted = formatPreviewOutputValue(key, value, {
                    stringMaxLength: 96
                });
                const category = Array.isArray(value)
                    ? 'table'
                    : (value && typeof value === 'object' ? 'json' : 'scalar');
                sections[category].push({
                    key,
                    pathHint: `$["${key}"]`,
                    kind: formatted.kind,
                    value: formatted.text,
                    meta: formatted.title || null,
                    resultPathVersion: null,
                    resultPath: null,
                    artifact: null
                });
            });
    }

    if (collected.truncated) {
        sections.json.push({
            key: 'truncated',
            pathHint: '$',
            kind: 'bounded',
            value: '输出树过大，已截断',
            meta: null,
            resultPathVersion: null,
            resultPath: null,
            artifact: null
        });
    }

    return Object.entries(sections)
        .map(([kind, items]) => ({
            kind,
            items: items.slice(0, 24)
        }))
        .filter(section => section.items.length > 0);
}

function getOperatorTitle(operator, liveNode) {
    return asString(
        operator?.title ||
        operator?.displayName ||
        liveNode?.title ||
        liveNode?.displayName ||
        operator?.type ||
        liveNode?.type ||
        '未命名算子');
}

function isNodeDisabled(operator, liveNode) {
    return normalizeBool(liveNode?.disabled ?? liveNode?.Disabled ?? operator?.disabled ?? operator?.Disabled);
}

function getStatusInfo({
    selectedNodeId,
    disabled,
    state,
    belongsToSelectedNode,
    stale
}) {
    if (!selectedNodeId) {
        return {
            kind: 'no-selection',
            label: '未选择',
            message: '请选择一个算子节点查看模块结果'
        };
    }

    if (disabled) {
        return {
            kind: 'disabled',
            label: 'disabled',
            message: '该节点已禁用，模块结果仅保留为只读参考'
        };
    }

    if (!belongsToSelectedNode) {
        return {
            kind: 'empty',
            label: '未运行',
            message: '该算子暂无预览结果'
        };
    }

    const status = asString(state?.status, 'idle');
    if (status === 'loading') {
        return {
            kind: 'loading',
            label: '运行中',
            message: '预览运行中...'
        };
    }

    if (status === 'canceled') {
        return {
            kind: 'canceled',
            label: '已取消',
            message: '预览已取消'
        };
    }

    if (stale) {
        return {
            kind: 'stale',
            label: 'stale',
            message: '结果已过期，请重新预览'
        };
    }

    if (status === 'error') {
        return {
            kind: 'error',
            label: '失败',
            message: state?.errorMessage || '预览失败'
        };
    }

    if (status === 'success') {
        return {
            kind: 'success',
            label: '成功',
            message: '预览完成'
        };
    }

    return {
        kind: 'empty',
        label: '未运行',
        message: '该算子暂无预览结果'
    };
}

function detectStale({ operator, liveNode, state, flowRevision }) {
    if (!state?.request) {
        return {
            stale: false,
            reasons: []
        };
    }

    const reasons = [];
    const requestedFlowRevision = Number(state.request.flowRevision ?? 0);
    const currentFlowRevision = Number(flowRevision ?? requestedFlowRevision);
    if (Number.isFinite(currentFlowRevision) &&
        Number.isFinite(requestedFlowRevision) &&
        currentFlowRevision !== requestedFlowRevision) {
        reasons.push('flowRevision');
    }

    const currentParameters = liveNode?.parameters || operator?.parameters || [];
    const currentSnapshot = buildPreviewParameterSnapshot(currentParameters);
    if (state.request.parameterSnapshot &&
        currentSnapshot &&
        state.request.parameterSnapshot !== currentSnapshot) {
        reasons.push('parameters');
    }

    return {
        stale: reasons.length > 0,
        reasons
    };
}

function buildOverviewItems({
    operator,
    liveNode,
    state,
    observation,
    sceneSummary,
    artifacts,
    statusInfo,
    staleReasons
}) {
    const identity = getObservationIdentity(observation) || {};
    const outcome = getObservationOutcome(observation) || {};
    const observedAtUtc = readOwn(observation, 'observedAtUtc', 'ObservedAtUtc');
    const resultPathItem = findFirstResultPath(getObservationDetail(observation)) ||
        sceneSummary.primitives?.find(item => item.resultPath)?.resultPath ||
        null;

    return [
        ['节点名称', getOperatorTitle(operator, liveNode)],
        ['OperatorType', operator?.type || liveNode?.type || '-'],
        ['OperatorId', operator?.id || liveNode?.id || '-'],
        ['状态', statusInfo.label],
        ['最近运行时间', observedAtUtc || '-'],
        ['耗时', state?.executionTimeMs ?? readOwn(outcome, 'executionTimeMs', 'ExecutionTimeMs') ?? '-'],
        ['ResultPath', resultPathItem || '-'],
        ['Preview Sequence', readOwn(identity, 'clientRequestSequence', 'ClientRequestSequence') ?? '-'],
        ['Observation Node', readOwn(identity, 'targetNodeId', 'TargetNodeId') || '-'],
        ['RunId', readOwn(identity, 'runId', 'RunId') || '-'],
        ['DebugSessionId', readOwn(identity, 'debugSessionId', 'DebugSessionId') || '-'],
        ['FlowRevision', state?.request?.flowRevision ?? readOwn(identity, 'flowRevision', 'FlowRevision') ?? '-'],
        ['Artifact', artifacts.length],
        ['Scene', sceneSummary.available ? sceneSummary.primitiveCount : 0],
        ['StaleReason', staleReasons.length > 0 ? staleReasons.join(', ') : '-']
    ];
}

function findFirstResultPath(node) {
    if (!node || typeof node !== 'object') {
        return null;
    }

    const normalized = normalizeObservationNode(node);
    if (normalized.resultPath) {
        return normalized.resultPath;
    }

    for (const child of getNodeChildren(node)) {
        const matched = findFirstResultPath(child);
        if (matched) {
            return matched;
        }
    }

    return null;
}

function buildNodeResultList(nodes, selectedNodeId, state, currentStatusInfo) {
    const list = Array.isArray(nodes) ? nodes : [];
    const activeNodeId = state?.activeNodeId || null;
    return list.slice(0, 128).map((node, index) => {
        const nodeId = node?.id || node?.Id || '';
        const disabled = normalizeBool(node?.disabled ?? node?.Disabled);
        let statusKind = 'empty';
        let statusText = '未运行';
        if (disabled) {
            statusKind = 'disabled';
            statusText = 'disabled';
        } else if (nodeId && nodeId === selectedNodeId) {
            statusKind = currentStatusInfo.kind;
            statusText = currentStatusInfo.label;
        } else if (nodeId && nodeId === activeNodeId) {
            statusKind = asString(state?.status, 'idle');
            statusText = statusKind === 'success' ? '成功' : (statusKind === 'error' ? '失败' : statusKind);
        }

        return {
            index,
            nodeId,
            title: node?.title || node?.displayName || node?.type || nodeId || `Node ${index + 1}`,
            type: node?.type || '',
            selected: nodeId === selectedNodeId,
            statusKind,
            statusText
        };
    });
}

export function buildOperatorResultViewModel(operator, previewState, options = {}) {
    const liveNode = options.liveNode || null;
    const selectedNodeId = operator?.id || liveNode?.id || null;
    const observation = getObservation(previewState);
    const scene = getObservationVisualScene(observation);
    const artifacts = normalizeArtifacts(previewState?.artifacts);
    const disabled = isNodeDisabled(operator, liveNode);
    const belongsToSelectedNode = Boolean(selectedNodeId && previewState?.activeNodeId === selectedNodeId);
    const { stale, reasons: staleReasons } = belongsToSelectedNode
        ? detectStale({
            operator,
            liveNode,
            state: previewState,
            flowRevision: options.flowRevision
        })
        : {
            stale: false,
            reasons: []
        };
    const statusInfo = getStatusInfo({
        selectedNodeId,
        disabled,
        state: previewState,
        belongsToSelectedNode,
        stale
    });
    const sceneSummary = summarizeScene(scene);
    const diagnostics = collectDiagnostics(previewState, observation, scene);
    const outputSections = statusInfo.kind === 'loading'
        ? []
        : buildOutputSections(observation, previewState?.outputData, artifacts);
    const rawSource = {
        request: previewState?.request || null,
        observation,
        outputData: previewState?.outputData || null,
        artifacts
    };
    const rawJsonPreview = statusInfo.kind === 'loading'
        ? {
            text: '',
            truncated: false
        }
        : buildSafeJsonPreview(rawSource, {
            maxChars: options.rawJsonMaxChars || MAX_OPERATOR_RESULT_RAW_JSON_CHARS
        });
    const nodes = typeof options.getNodes === 'function'
        ? options.getNodes()
        : (Array.isArray(options.nodes) ? options.nodes : []);

    return {
        nodeId: selectedNodeId,
        operatorName: getOperatorTitle(operator, liveNode),
        operatorType: operator?.type || liveNode?.type || '',
        disabled,
        status: statusInfo.kind,
        statusText: statusInfo.label,
        stateMessage: statusInfo.message,
        stale,
        staleReasons,
        overviewItems: buildOverviewItems({
            operator,
            liveNode,
            state: previewState,
            observation,
            sceneSummary,
            artifacts,
            statusInfo,
            staleReasons
        }),
        outputSections,
        diagnostics,
        artifacts,
        sceneSummary,
        rawJsonPreview,
        nodeResults: buildNodeResultList(nodes, selectedNodeId, previewState, statusInfo)
    };
}

export function formatResultArtifactMetadata(artifact, lead = '') {
    const normalized = normalizeArtifactReference(artifact);
    if (!normalized) {
        return lead || 'Artifact 元数据不可用';
    }

    return [
        lead,
        `artifactId: ${normalized.artifactId}`,
        `kind: ${normalized.kind || '-'}`,
        `role: ${normalized.role || '-'}`,
        `contentType: ${normalized.contentType || '-'}`,
        `length: ${formatByteLength(normalized.length)}`,
        `sha256: ${normalized.sha256 || '-'}`,
        `createdAtUtc: ${normalized.createdAtUtc || '-'}`,
        `expiresAtUtc: ${normalized.expiresAtUtc || '-'}`
    ].filter(Boolean).join('\n');
}

export { formatByteLength };
