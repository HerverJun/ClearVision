import {
    formatPreviewDiagnosticMessage,
    formatPreviewOutputValue,
    getPreviewResultLabel,
    getPreviewTypeLabel,
    isPreviewImageOutputKey,
    isPreviewKeyOutputKey,
    isPreviewTechnicalDiagnostic,
    isPreviewImageLikePayload
} from './previewOutputFormatter.mjs';
import { buildPreviewParameterSnapshot } from './previewCoordinator.js';

export const MAX_OPERATOR_RESULT_RAW_JSON_CHARS = 4096;
export const MAX_OPERATOR_RESULT_STRING_CHARS = 512;
export const MAX_OPERATOR_RESULT_TREE_ROWS = 48;
export const MAX_OPERATOR_RESULT_ARTIFACT_TEXT_PREVIEW_BYTES = 64 * 1024;
export const MAX_OPERATOR_RESULT_ARTIFACT_TEXT_DISPLAY_CHARS = 4096;
export const STALE_PREVIEW_MESSAGE = '参数或流程已变更，需重新预览';

const PRODUCTIZED_KEY_OUTPUT_LIMIT = 10;
const RAW_DATA_SECTION_ITEM_LIMIT = 8;
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

function normalizeIdentityText(value) {
    const text = asString(value).trim().toLowerCase();
    return text || null;
}

function normalizeIdentityNumber(value) {
    if (value === undefined || value === null || value === '') {
        return null;
    }

    const numberValue = Number(value);
    return Number.isSafeInteger(numberValue) && numberValue >= 0
        ? numberValue
        : null;
}

function readRequestField(state, ...keys) {
    return readOwn(state?.request, ...keys);
}

function readDefined(source, ...keys) {
    if (!source || typeof source !== 'object') {
        return undefined;
    }

    for (const key of keys) {
        if (Object.prototype.hasOwnProperty.call(source, key) &&
            source[key] !== undefined &&
            source[key] !== null) {
            return source[key];
        }
    }

    return undefined;
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
            return '[图像内容已省略]';
        }

        return clipText(redactLocalAbsolutePaths(value), maxStringChars);
    }

    if (value === null || value === undefined || typeof value !== 'object') {
        return value;
    }

    if (seen.has(value)) {
        return '[循环引用]';
    }

    if (depth >= maxDepth) {
        return Array.isArray(value)
            ? `[${value.length} 项]`
            : `{${Object.keys(value).length} 个字段}`;
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
            result.push(`[还有 ${value.length - result.length} 项]`);
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

function clipDiagnosticText(value, maxChars = 240) {
    return clipText(redactLocalAbsolutePaths(value), maxChars);
}

function normalizeDiagnostic(item, source = 'diagnostic') {
    if (typeof item === 'string') {
        return {
            source,
            code: source,
            message: formatPreviewDiagnosticMessage(clipDiagnosticText(item)),
            pathHint: null
        };
    }

    if (!item || typeof item !== 'object') {
        return null;
    }

    return {
        source,
        code: asString(readDefined(item, 'code', 'Code'), source),
        message: formatPreviewDiagnosticMessage(
            clipDiagnosticText(
                readDefined(item, 'message', 'Message', 'description', 'Description', 'reason', 'Reason') ??
                readDefined(item, 'displayValue', 'DisplayValue') ??
                JSON.stringify(sanitizeResultValue(item, {
                    maxDepth: 2,
                    maxArrayItems: 6,
                    maxObjectFields: 12,
                    maxStringChars: 120
                }))
            )
        ),
        pathHint: readDefined(item, 'pathHint', 'PathHint') === undefined
            ? null
            : clipDiagnosticText(readDefined(item, 'pathHint', 'PathHint'), 120)
    };
}

function normalizeDiagnosticList(value, source = 'diagnostic') {
    if (!Array.isArray(value)) {
        return [];
    }

    return value
        .map(item => normalizeDiagnostic(item, source))
        .filter(Boolean);
}

function classifyPreviewIssue(message, status = '') {
    const text = asString(message).trim();
    const normalized = text.toLowerCase();
    const statusText = asString(status).toLowerCase();

    if (statusText === 'canceled' || /cancel|abort|取消/.test(normalized)) {
        return {
            code: 'canceled',
            message: text ? `预览已取消：${clipDiagnosticText(text)}` : '预览已取消'
        };
    }

    if (/timeout|timed out|unavailable|不可用|超时|服务|连接/.test(normalized)) {
        return {
            code: 'timeout',
            message: `预览超时或服务不可用：${clipDiagnosticText(text || '请稍后重试')}`
        };
    }

    if (/missingresource|resource|artifact|not found|404|缺少资源|资源/.test(normalized)) {
        return {
            code: 'missing-resource',
            message: `缺少资源：${clipDiagnosticText(text || '预览依赖的资源不可用')}`
        };
    }

    if (/input|image|file path|camera|采集源|文件路径|输入图|相机/.test(normalized)) {
        return {
            code: 'missing-input',
            message: `缺输入图或采集源：${clipDiagnosticText(text || '请先准备输入图像')}`
        };
    }

    if (/validat|schema|invalid|parameter|参数|校验|无效/.test(normalized)) {
        return {
            code: 'validation',
            message: `参数校验失败：${clipDiagnosticText(text || '请检查算子参数')}`
        };
    }

    return {
        code: 'backend',
        message: `后端异常：${clipDiagnosticText(text || '预览执行失败')}`
    };
}

function normalizeMissingResource(item) {
    if (typeof item === 'string') {
        return clipDiagnosticText(item, 160);
    }

    if (!item || typeof item !== 'object') {
        return null;
    }

    const label = readDefined(item, 'name', 'Name', 'resourceId', 'ResourceId', 'id', 'Id', 'kind', 'Kind') ?? 'resource';
    const hint = readDefined(item, 'pathHint', 'PathHint', 'reason', 'Reason', 'message', 'Message');
    return clipDiagnosticText(hint ? `${label}: ${hint}` : label, 180);
}

function collectMissingResourceDiagnostics(value, source = 'missing-resource') {
    if (!Array.isArray(value) || value.length === 0) {
        return [];
    }

    const summary = value
        .map(normalizeMissingResource)
        .filter(Boolean)
        .slice(0, 6)
        .join('；');

    return summary
        ? [{
            source,
            code: 'missing-resource',
            message: `缺少资源：${summary}`,
            pathHint: null
        }]
        : [];
}

function collectFailedOperatorDiagnostics(...sources) {
    for (const source of sources) {
        const failedOperatorName = readDefined(source, 'failedOperatorName', 'FailedOperatorName');
        const failedOperatorType = readDefined(source, 'failedOperatorType', 'FailedOperatorType');
        const failedOperatorId = readDefined(source, 'failedOperatorId', 'FailedOperatorId');
        if (!failedOperatorName && !failedOperatorType && !failedOperatorId) {
            continue;
        }

        const detail = [
            failedOperatorName ? `名称 ${failedOperatorName}` : null,
            failedOperatorType ? `类型 ${failedOperatorType}` : null,
            failedOperatorId ? `ID ${failedOperatorId}` : null
        ].filter(Boolean).join('，');

        return [{
            source: 'failed-operator',
            code: 'failed-operator',
            message: `失败算子：${clipDiagnosticText(detail || '未知算子', 180)}`,
            pathHint: null
        }];
    }

    return [];
}

function pushUniqueDiagnostic(target, item) {
    if (!item?.message) {
        return;
    }

    const key = `${item.source || ''}|${item.code || ''}|${item.message || ''}|${item.pathHint || ''}`;
    if (target.some(existing =>
        `${existing.source || ''}|${existing.code || ''}|${existing.message || ''}|${existing.pathHint || ''}` === key)) {
        return;
    }

    target.push(item);
}

function collectDiagnostics(state, observation, scene) {
    const result = [];
    const status = asString(readDefined(state, 'status', 'Status'));
    const outcome = getObservationOutcome(observation) || {};
    const stateErrorMessage = readDefined(state, 'errorMessage', 'ErrorMessage');
    const outcomeErrorMessage = readDefined(outcome, 'errorMessage', 'ErrorMessage');
    const primaryError = stateErrorMessage || outcomeErrorMessage;

    if (status === 'auth-error') {
        // 认证失效是登录态问题，不是算子/后端故障：仅给出登录态提示，
        // 不生成 backend/failed-operator 诊断，避免污染节点诊断。
        pushUniqueDiagnostic(result, {
            source: 'auth',
            code: 'auth-error',
            message: primaryError || '登录状态无效，请重新登录。',
            pathHint: null
        });
        return result;
    }

    if (primaryError) {
        const issue = classifyPreviewIssue(primaryError, status);
        pushUniqueDiagnostic(result, {
            source: 'preview',
            code: issue.code,
            message: issue.message,
            pathHint: null
        });
    } else if (status === 'canceled') {
        pushUniqueDiagnostic(result, {
            source: 'preview',
            code: 'canceled',
            message: '预览已取消',
            pathHint: null
        });
    } else if (status === 'error') {
        pushUniqueDiagnostic(result, {
            source: 'preview',
            code: 'backend',
            message: '后端异常：预览执行失败',
            pathHint: null
        });
    }

    collectFailedOperatorDiagnostics(state, outcome)
        .forEach(item => pushUniqueDiagnostic(result, item));

    collectMissingResourceDiagnostics(readDefined(state, 'missingResources', 'MissingResources'), 'preview')
        .forEach(item => pushUniqueDiagnostic(result, item));

    collectMissingResourceDiagnostics(readDefined(outcome, 'missingResources', 'MissingResources'), 'observation')
        .forEach(item => pushUniqueDiagnostic(result, item));

    normalizeDiagnosticList(readDefined(state, 'diagnostics', 'Diagnostics'), 'preview')
        .forEach(item => pushUniqueDiagnostic(result, item));

    const observationDiagnostics = readDefined(observation, 'diagnostics', 'Diagnostics');
    normalizeDiagnosticList(observationDiagnostics, 'observation')
        .forEach(item => pushUniqueDiagnostic(result, item));

    getSceneDiagnostics(scene)
        .map(item => normalizeDiagnostic(item, 'scene'))
        .filter(Boolean)
        .forEach(item => pushUniqueDiagnostic(result, item));

    const artifactDiagnostics = state?.outputData?._previewArtifactDiagnostics;
    normalizeDiagnosticList(artifactDiagnostics, 'artifact')
        .forEach(item => pushUniqueDiagnostic(result, item));

    normalizeDiagnosticList(readDefined(state?.outputData, 'diagnostics', 'Diagnostics'), 'output')
        .forEach(item => pushUniqueDiagnostic(result, item));

    return result.slice(0, 16);
}

function getObservationDisplayValue(normalized, formatted) {
    if (normalized.childCount > 0 &&
        (isPreviewTechnicalDiagnostic(normalized.displayValue) ||
            ['array', 'dictionary', 'object', 'jsonelement', 'detectionlist'].includes(normalized.kindKey))) {
        return normalized.kindKey === 'array' || normalized.kindKey === 'detectionlist'
            ? `${normalized.childCount} 项`
            : `${normalized.childCount} 个字段`;
    }

    return formatted.text || clipText(normalized.displayValue, 96);
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
        const outputName = normalized.outputPortName || normalized.name || normalized.pathHint;
        const item = {
            key: normalized.name || normalized.pathHint,
            label: getPreviewResultLabel(outputName),
            pathHint: normalized.pathHint,
            kind: normalized.kind,
            depth: row.depth,
            value: getObservationDisplayValue(normalized, formatted),
            title: formatted.title || null,
            meta: getPreviewTypeLabel(normalized.originalType),
            rawMeta: normalized.originalType || null,
            outputPortId: normalized.outputPortId,
            outputPortName: normalized.outputPortName,
            resultPathVersion: normalized.resultPathVersion,
            resultPath: normalized.resultPath,
            artifact: normalized.artifact,
            truncated: normalized.truncated,
            technical: isPreviewTechnicalDiagnostic(`${normalized.name || ''} ${normalized.kind || ''} ${normalized.displayValue || ''} ${normalized.originalType || ''}`) &&
                !isPreviewKeyOutputKey(outputName)
        };

        sections[row.category]?.push(item);
    }

    if (Array.isArray(artifacts) && artifacts.length > 0) {
        artifacts.forEach(artifact => {
            if (!sections.artifact.some(item => item.artifact?.artifactId === artifact.artifactId)) {
                sections.artifact.push({
                    key: artifact.role || artifact.kind || artifact.artifactId,
                    label: getPreviewResultLabel(artifact.role || artifact.kind || artifact.artifactId, '附件'),
                    pathHint: artifact.pathHint,
                    kind: artifact.kind || 'artifact',
                    depth: null,
                    value: `${artifact.contentType || 'artifact'} ${formatByteLength(artifact.length)}`,
                    title: null,
                    meta: artifact.artifactId,
                    rawMeta: artifact.artifactId,
                    outputPortId: null,
                    outputPortName: null,
                    resultPathVersion: null,
                    resultPath: null,
                    artifact,
                    truncated: false,
                    technical: false
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
                    label: getPreviewResultLabel(key),
                    pathHint: `$["${key}"]`,
                    kind: formatted.kind,
                    depth: 1,
                    value: formatted.text,
                    title: formatted.title || null,
                    meta: formatted.title || null,
                    rawMeta: formatted.title || null,
                    outputPortId: null,
                    outputPortName: null,
                    resultPathVersion: null,
                    resultPath: null,
                    artifact: null,
                    truncated: false,
                    technical: isPreviewTechnicalDiagnostic(`${key} ${formatted.text}`)
                });
            });
    }

    if (collected.truncated) {
        sections.json.push({
            key: 'truncated',
            label: '截断',
            pathHint: '$',
            kind: 'bounded',
            depth: 0,
            value: '结果较大，已自动折叠部分详情',
            title: null,
            meta: null,
            rawMeta: null,
            outputPortId: null,
            outputPortName: null,
            resultPathVersion: null,
            resultPath: null,
            artifact: null,
            truncated: true,
            technical: true
        });
    }

    return Object.entries(sections)
        .map(([kind, items]) => ({
            kind,
            items: items.slice(0, 24)
        }))
        .filter(section => section.items.length > 0);
}

function flattenOutputSectionItems(outputSections) {
    return (Array.isArray(outputSections) ? outputSections : [])
        .flatMap(section => (Array.isArray(section.items) ? section.items : [])
            .map(item => ({
                ...item,
                sectionKind: section.kind
            })));
}

function normalizeDedupKey(value) {
    return asString(value)
        .trim()
        .replace(/[\s_-]+/g, '')
        .toLowerCase();
}

function isDeclaredOutputItem(item) {
    return Boolean(item?.outputPortId || item?.outputPortName);
}

function isImageOutputItem(item) {
    const key = item?.outputPortName || item?.key || item?.label || '';
    const kind = normalizeKind(item?.kind);
    return Boolean(item?.artifact) ||
        isPreviewImageOutputKey(key) ||
        ['image', 'mask', 'binary', 'stream', 'resource'].includes(kind);
}

function isProductizedKeyOutputItem(item) {
    if (!item || item.technical) {
        return false;
    }

    if (isImageOutputItem(item)) {
        return false;
    }

    const depth = Number.isFinite(item.depth) ? item.depth : 1;
    return isDeclaredOutputItem(item) ||
        (depth <= 1 && (
            isPreviewKeyOutputKey(item.outputPortName || item.key || item.label) ||
            ['scalar', 'geometry'].includes(item.sectionKind)
        ));
}

function outputItemPriority(item) {
    if (isDeclaredOutputItem(item)) {
        return 0;
    }

    if (isPreviewKeyOutputKey(item.outputPortName || item.key || item.label)) {
        return 1;
    }

    if (item.sectionKind === 'geometry') {
        return 2;
    }

    if (item.sectionKind === 'scalar') {
        return 3;
    }

    return 9;
}

function buildKeyOutputs(outputSections, statusInfo) {
    if (statusInfo.kind === 'loading') {
        return [];
    }

    const seen = new Set();
    return flattenOutputSectionItems(outputSections)
        .filter(isProductizedKeyOutputItem)
        .sort((left, right) => outputItemPriority(left) - outputItemPriority(right))
        .filter(item => {
            const key = normalizeDedupKey(item.outputPortName || item.label || item.key || item.pathHint);
            if (!key || seen.has(key)) {
                return false;
            }

            seen.add(key);
            return true;
        })
        .slice(0, PRODUCTIZED_KEY_OUTPUT_LIMIT)
        .map(item => ({
            key: item.key,
            label: item.label || getPreviewResultLabel(item.outputPortName || item.key),
            value: formatPreviewDiagnosticMessage(item.value || '-'),
            title: item.title,
            kind: item.sectionKind || item.kind || 'value',
            pathHint: item.pathHint,
            resultPath: item.resultPath,
            meta: item.meta ? `类型：${item.meta}` : null,
            declared: isDeclaredOutputItem(item)
        }));
}

function buildImageSummaries(outputSections, artifacts) {
    const items = [];
    const seen = new Set();

    const pushItem = item => {
        const key = item.artifact?.artifactId ||
            normalizeDedupKey(item.outputPortName || item.label || item.key || item.pathHint);
        if (!key || seen.has(key)) {
            return;
        }

        seen.add(key);
        const artifact = item.artifact || null;
        const dimensions = artifact?.width && artifact?.height
            ? `${artifact.width} x ${artifact.height}${artifact.channels ? ` x ${artifact.channels}` : ''}`
            : null;
        const size = artifact ? formatByteLength(artifact.length) : null;
        const formattedValue = item.value ? formatPreviewDiagnosticMessage(item.value) : null;
        const summary = [
            dimensions,
            size,
            formattedValue && !isPreviewTechnicalDiagnostic(formattedValue) ? formattedValue : null
        ].filter(Boolean).join('，') || '图像内容已省略';

        items.push({
            label: item.label || getPreviewResultLabel(item.outputPortName || item.key || artifact?.role || artifact?.kind, '图像/附件'),
            summary,
            kind: artifact?.kind || item.kind || 'artifact',
            contentType: artifact?.contentType || null,
            artifact,
            pathHint: item.pathHint,
            resultPath: item.resultPath
        });
    };

    flattenOutputSectionItems(outputSections)
        .filter(isImageOutputItem)
        .forEach(pushItem);

    (Array.isArray(artifacts) ? artifacts : []).forEach(artifact => {
        pushItem({
            label: getPreviewResultLabel(artifact.role || artifact.kind || 'artifact', '图像/附件'),
            key: artifact.role || artifact.kind || artifact.artifactId,
            kind: artifact.kind || 'artifact',
            value: '附件内容可按需查看',
            artifact,
            pathHint: artifact.pathHint
        });
    });

    return items.slice(0, 8);
}

function buildRawDataSections(outputSections) {
    const labels = {
        scalar: '标量输出',
        table: '表格/列表',
        geometry: '几何结果',
        artifact: '图像/附件',
        json: '复杂对象'
    };

    return (Array.isArray(outputSections) ? outputSections : [])
        .map(section => ({
            kind: section.kind,
            label: labels[section.kind] || section.kind,
            items: (section.items || []).slice(0, RAW_DATA_SECTION_ITEM_LIMIT).map(item => ({
                label: item.label || getPreviewResultLabel(item.key || item.pathHint),
                value: formatPreviewDiagnosticMessage(item.value || '-'),
                meta: item.meta ? `类型：${item.meta}` : (item.resultPath || item.pathHint || ''),
                pathHint: item.pathHint,
                resultPath: item.resultPath,
                technical: Boolean(item.technical)
            })),
            omittedCount: Math.max(0, (section.items || []).length - RAW_DATA_SECTION_ITEM_LIMIT)
        }))
        .filter(section => section.items.length > 0);
}

function pushAdvancedDiagnostic(target, item) {
    if (!item?.message) {
        return;
    }

    const message = formatPreviewDiagnosticMessage(item.message);
    const code = asString(item.code || item.source || 'diagnostic');
    const key = `${code}|${message}|${item.pathHint || ''}`;
    const existing = target.find(candidate => candidate.key === key);
    if (existing) {
        existing.count += 1;
        existing.message = existing.count > 1 && /详情过深|已自动折叠/.test(message)
            ? `详情过深，已自动折叠 ${existing.count} 项`
            : message;
        return;
    }

    target.push({
        key,
        source: item.source || 'diagnostic',
        code,
        label: getPreviewResultLabel(code, code),
        message,
        pathHint: item.pathHint || null,
        technical: Boolean(item.technical || isPreviewTechnicalDiagnostic(`${code} ${message}`)),
        count: 1
    });
}

function buildAdvancedDiagnostics(diagnostics, outputSections, rawJsonPreview) {
    const result = [];
    (Array.isArray(diagnostics) ? diagnostics : [])
        .forEach(item => pushAdvancedDiagnostic(result, item));

    flattenOutputSectionItems(outputSections)
        .filter(item => item.technical || item.truncated || isPreviewTechnicalDiagnostic(`${item.key} ${item.value} ${item.rawMeta || ''}`))
        .forEach(item => pushAdvancedDiagnostic(result, {
            source: item.sectionKind || 'output',
            code: item.key || item.kind || 'diagnostic',
            message: item.value || item.rawMeta || item.pathHint,
            pathHint: item.pathHint,
            technical: true
        }));

    if (rawJsonPreview?.truncated) {
        pushAdvancedDiagnostic(result, {
            source: 'raw-json',
            code: 'truncated',
            message: '结果较大，已自动折叠部分详情',
            technical: true
        });
    }

    return result.slice(0, 16).map(({ key, ...item }) => item);
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
            label: '需重新预览',
            message: STALE_PREVIEW_MESSAGE
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

function pushStaleReason(reasons, reason) {
    if (!reasons.includes(reason)) {
        reasons.push(reason);
    }
}

function detectStale({ operator, liveNode, state, flowRevision, projectId, inputImageHash }) {
    if (!state?.request) {
        return {
            stale: false,
            reasons: []
        };
    }

    const reasons = [];
    const selectedNodeId = normalizeIdentityText(liveNode?.id || operator?.id);
    const requestNodeId = normalizeIdentityText(readRequestField(state, 'nodeId', 'NodeId'));
    const observation = getObservation(state);
    const identity = getObservationIdentity(observation) || {};
    const observedNodeId = normalizeIdentityText(readOwn(identity, 'targetNodeId', 'TargetNodeId'));

    if (selectedNodeId && requestNodeId && selectedNodeId !== requestNodeId) {
        pushStaleReason(reasons, 'targetNodeId');
    }
    if (selectedNodeId && observedNodeId && selectedNodeId !== observedNodeId) {
        pushStaleReason(reasons, 'targetNodeId');
    }

    const currentProjectId = normalizeIdentityText(projectId);
    const requestProjectId = normalizeIdentityText(readRequestField(state, 'projectId', 'ProjectId'));
    const observedProjectId = normalizeIdentityText(readOwn(identity, 'projectId', 'ProjectId'));
    if (currentProjectId && requestProjectId && currentProjectId !== requestProjectId) {
        pushStaleReason(reasons, 'projectId');
    }
    if (currentProjectId && observedProjectId && currentProjectId !== observedProjectId) {
        pushStaleReason(reasons, 'projectId');
    }

    const requestedFlowRevision = normalizeIdentityNumber(readRequestField(state, 'flowRevision', 'FlowRevision'));
    const observedFlowRevision = normalizeIdentityNumber(readOwn(identity, 'flowRevision', 'FlowRevision'));
    const currentFlowRevision = normalizeIdentityNumber(flowRevision);
    if (currentFlowRevision !== null && requestedFlowRevision !== null && currentFlowRevision !== requestedFlowRevision) {
        pushStaleReason(reasons, 'flowRevision');
    }
    if (currentFlowRevision !== null && observedFlowRevision !== null && currentFlowRevision !== observedFlowRevision) {
        pushStaleReason(reasons, 'flowRevision');
    }

    const currentParameters = liveNode?.parameters || operator?.parameters || [];
    const currentSnapshot = buildPreviewParameterSnapshot(currentParameters);
    if (readRequestField(state, 'parameterSnapshot', 'ParameterSnapshot') &&
        currentSnapshot &&
        readRequestField(state, 'parameterSnapshot', 'ParameterSnapshot') !== currentSnapshot) {
        pushStaleReason(reasons, 'parameters');
    }

    const currentInputImageHash = asString(inputImageHash).trim();
    const requestInputImageHash = asString(readRequestField(state, 'inputImageHash', 'InputImageHash')).trim();
    if (currentInputImageHash && requestInputImageHash && currentInputImageHash !== requestInputImageHash) {
        pushStaleReason(reasons, 'inputImageHash');
    }

    return {
        stale: reasons.length > 0,
        reasons
    };
}

function formatExecutionTime(value) {
    if (value === null || value === undefined || value === '') {
        return '-';
    }

    const numberValue = Number(value);
    return Number.isFinite(numberValue) ? `${numberValue} ms` : asString(value, '-');
}

function buildExecutionSummaryItems({
    operator,
    liveNode,
    state,
    observation,
    statusInfo
}) {
    const outcome = getObservationOutcome(observation) || {};
    const observedAtUtc = readOwn(observation, 'observedAtUtc', 'ObservedAtUtc');
    return [
        {
            label: '状态',
            value: statusInfo.label,
            kind: statusInfo.kind
        },
        {
            label: '耗时',
            value: formatExecutionTime(state?.executionTimeMs ?? readOwn(outcome, 'executionTimeMs', 'ExecutionTimeMs')),
            kind: 'duration'
        },
        {
            label: '节点名称',
            value: getOperatorTitle(operator, liveNode),
            kind: 'text'
        },
        {
            label: '算子类型',
            value: operator?.type || liveNode?.type || '-',
            kind: 'text'
        },
        {
            label: '最近运行时间',
            value: observedAtUtc || '-',
            kind: 'time'
        }
    ];
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
            flowRevision: options.flowRevision,
            projectId: options.projectId,
            inputImageHash: options.inputImageHash
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
    const executionSummaryItems = buildExecutionSummaryItems({
        operator,
        liveNode,
        state: previewState,
        observation,
        statusInfo
    });
    const keyOutputs = buildKeyOutputs(outputSections, statusInfo);
    const imageSummaries = buildImageSummaries(outputSections, artifacts);
    const rawDataSections = buildRawDataSections(outputSections);
    const advancedDiagnostics = buildAdvancedDiagnostics(diagnostics, outputSections, rawJsonPreview);
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
        executionSummaryItems,
        keyOutputs,
        imageSummaries,
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
        advancedDiagnostics,
        rawDataSections,
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
