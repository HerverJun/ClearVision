const DETECTION_KEYS = new Set(['detections']);
const SUPPRESSED_DETECTION_KEYS = new Set(['suppresseddetections']);
const IMAGE_OUTPUT_KEYS = new Set([
    'inputimage',
    'outputimage',
    'previewimage',
    'originalimage',
    'currentframe',
    'image',
    'mask',
    'binaryimage'
]);
const KEY_OUTPUT_KEYS = new Set([
    ...IMAGE_OUTPUT_KEYS,
    'region',
    'roi',
    'area',
    'height',
    'width',
    'length',
    'center',
    'centerx',
    'centery',
    'radius',
    'score',
    'result',
    'ok',
    'ng',
    'passed',
    'success',
    'detections',
    'detectionlist',
    'objects',
    'defects',
    'spatialcontext',
    'transform',
    'transforms',
    'calibration',
    'calibrationdata',
    'binding',
    'bindings',
    'matrix3x3',
    'count',
    'objectcount',
    'detectioncount'
]);
const FRIENDLY_FIELD_LABELS = new Map([
    ['inputimage', '输入图像'],
    ['outputimage', '输出图像'],
    ['previewimage', '预览图像'],
    ['originalimage', '原始图像'],
    ['currentframe', '当前帧'],
    ['image', '图像'],
    ['mask', '掩膜'],
    ['binaryimage', '二值图像'],
    ['region', '区域'],
    ['roi', 'ROI 区域'],
    ['area', '面积'],
    ['height', '高度'],
    ['width', '宽度'],
    ['length', '长度'],
    ['center', '圆心/中心'],
    ['centerx', '中心 X'],
    ['centery', '中心 Y'],
    ['radius', '半径'],
    ['score', '分数'],
    ['result', '结果'],
    ['ok', 'OK'],
    ['ng', 'NG'],
    ['success', '是否成功'],
    ['passed', '是否通过'],
    ['diagnostics', '诊断'],
    ['profile', '附件摘要'],
    ['artifact', '附件'],
    ['resourcedescriptor', '资源描述'],
    ['executiontimems', '耗时'],
    ['operatortype', '算子类型'],
    ['operatorid', '算子 ID'],
    ['projectid', '工程 ID'],
    ['previewsequence', '预览序号'],
    ['spatialcontext', '空间上下文'],
    ['transform', '坐标变换'],
    ['transforms', '坐标变换'],
    ['calibration', '标定数据'],
    ['calibrationdata', '标定数据'],
    ['binding', '绑定信息'],
    ['bindings', '绑定信息'],
    ['matrix3x3', '3x3 矩阵'],
    ['resultpath', '结果路径'],
    ['filepath', '文件路径'],
    ['directory', '保存目录'],
    ['filenametemplate', '命名规则'],
    ['estimatedfilename', '预计文件名'],
    ['estimatedfilepath', '预计路径'],
    ['format', '格式'],
    ['quality', '质量'],
    ['message', '提示'],
    ['willwritetodisk', '预览写盘'],
    ['previewmode', '预览模式'],
    ['previewkind', '预览类型'],
    ['previewblocked', '预览阻断'],
    ['previewsafe', '安全预览'],
    ['detections', '检测结果'],
    ['detectionlist', '检测结果'],
    ['objects', '对象列表'],
    ['defects', '缺陷列表'],
    ['count', '数量'],
    ['objectcount', '对象数量'],
    ['detectioncount', '检测数量']
]);
const INTERNAL_TYPE_LABELS = new Map([
    ['system.int32', '整数'],
    ['system.int64', '整数'],
    ['system.single', '浮点数'],
    ['system.double', '浮点数'],
    ['system.decimal', '小数'],
    ['system.string', '字符串'],
    ['system.boolean', '布尔值'],
    ['system.datetime', '时间'],
    ['system.guid', 'GUID'],
    ['system.text.json.jsonelement', 'JSON 对象']
]);
const DIAGNOSTIC_TRANSLATIONS = [
    {
        pattern: /^image artifact; content omitted\.?$/i,
        message: '图像内容已省略，可点击查看摘要/预览。'
    },
    {
        pattern: /^Observation detail omitted because depth-limit was reached\.?$/i,
        message: '详情过深，已自动折叠。'
    },
    {
        pattern: /^Observation output key does not match a declared output port; canonical ResultPath metadata omitted\.?$/i,
        message: '输出键不属于声明端口，已作为诊断信息折叠。'
    },
    {
        pattern: /<truncated>/ig,
        message: '已截断'
    }
];
const TECHNICAL_DIAGNOSTIC_PATTERN = /(depth-limit|resource-descriptor|resultpath-port-missing|System\.Text\.Json\.JsonElement|Observation detail omitted|canonical ResultPath metadata omitted)/i;

function hasKnownImageSignature(base64Text) {
    const sanitized = String(base64Text || '').replace(/\s+/g, '');
    if (sanitized.length < 32 || /[^A-Za-z0-9+/=]/.test(sanitized)) {
        return false;
    }

    if (typeof atob !== 'function') {
        return false;
    }

    const prefixLength = Math.min(64, sanitized.length);
    let sample = sanitized.slice(0, prefixLength);
    const paddingLength = sample.length % 4;
    if (paddingLength !== 0) {
        sample = sample.padEnd(sample.length + (4 - paddingLength), '=');
    }

    try {
        const decoded = atob(sample);
        if (!decoded || decoded.length < 4) {
            return false;
        }

        const bytes = Array.from(decoded.slice(0, 12)).map(char => char.charCodeAt(0));
        const ascii = decoded.slice(0, 12);

        return (
            (bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47) ||
            (bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) ||
            (bytes[0] === 0x42 && bytes[1] === 0x4d) ||
            ascii.startsWith('GIF8') ||
            ascii.startsWith('RIFF')
        );
    } catch {
        return false;
    }
}

function normalizeOutputKey(key) {
    return String(key || '')
        .trim()
        .replace(/[\s_-]+/g, '')
        .toLowerCase();
}

function truncateText(text, maxLength) {
    const value = String(text ?? '');
    if (value.length <= maxLength) {
        return {
            text: value,
            title: null,
            truncated: false
        };
    }

    if (maxLength <= 6) {
        return {
            text: `${value.slice(0, Math.max(1, maxLength - 3))}...`,
            title: value,
            truncated: true
        };
    }

    return {
        text: `${value.slice(0, Math.ceil((maxLength - 3) * 0.58))}...${value.slice(-(Math.floor((maxLength - 3) * 0.42)))}`,
        title: value,
        truncated: true
    };
}

function countNestedArrayItems(value) {
    if (Array.isArray(value)) {
        return value.length;
    }

    if (!value || typeof value !== 'object') {
        return 0;
    }

    const nestedArrays = [
        value.Detections,
        value.detections,
        value.Items,
        value.items
    ];

    for (const candidate of nestedArrays) {
        if (Array.isArray(candidate)) {
            return candidate.length;
        }
    }

    const countCandidates = [
        value.Count,
        value.count,
        value.Total,
        value.total,
        value.Length,
        value.length
    ];

    for (const candidate of countCandidates) {
        if (typeof candidate === 'number' && Number.isFinite(candidate)) {
            return candidate;
        }
    }

    return 0;
}

function extractReadableKey(key) {
    const text = String(key ?? '').trim();
    const pathMatch = Array.from(text.matchAll(/\["([^"]+)"\]/g)).at(-1);
    return pathMatch?.[1] || text;
}

export function getPreviewResultLabel(key, fallback = '结果') {
    const readableKey = extractReadableKey(key);
    const normalizedKey = normalizeOutputKey(readableKey);
    if (!normalizedKey) {
        return fallback;
    }

    return FRIENDLY_FIELD_LABELS.get(normalizedKey) || readableKey || fallback;
}

export function getPreviewTypeLabel(typeName) {
    const text = String(typeName ?? '').trim();
    if (!text) {
        return null;
    }

    const normalized = text.toLowerCase();
    if (INTERNAL_TYPE_LABELS.has(normalized)) {
        return INTERNAL_TYPE_LABELS.get(normalized);
    }

    if (normalized.endsWith('[]')) {
        return '数组';
    }

    if (normalized.includes('json')) {
        return 'JSON 对象';
    }

    return text.replace(/^ClearVision\.Product\.[\w.]+/i, '内部对象');
}

export function formatPreviewDiagnosticMessage(message) {
    let text = String(message ?? '').trim();
    if (!text) {
        return '';
    }

    for (const translation of DIAGNOSTIC_TRANSLATIONS) {
        if (translation.pattern.global) {
            text = text.replace(translation.pattern, translation.message);
        } else if (translation.pattern.test(text)) {
            text = translation.message;
        }
    }

    for (const [typeName, label] of INTERNAL_TYPE_LABELS.entries()) {
        text = text.replace(new RegExp(typeName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'ig'), label);
    }

    return text;
}

export function isPreviewTechnicalDiagnostic(value) {
    return TECHNICAL_DIAGNOSTIC_PATTERN.test(String(value ?? ''));
}

export function isPreviewImageOutputKey(key) {
    return IMAGE_OUTPUT_KEYS.has(normalizeOutputKey(key));
}

export function isPreviewKeyOutputKey(key) {
    return KEY_OUTPUT_KEYS.has(normalizeOutputKey(key));
}

export function isPreviewImageLikePayload(value) {
    if (typeof value !== 'string') {
        return false;
    }

    const trimmed = value.trim();
    if (!trimmed) {
        return false;
    }

    if (trimmed.startsWith('data:image/')) {
        return true;
    }

    if (trimmed.startsWith('{') || trimmed.startsWith('[') || trimmed.includes('"Format"')) {
        return false;
    }

    return hasKnownImageSignature(trimmed);
}

export function formatPreviewOutputValue(key, value, options = {}) {
    const {
        stringMaxLength = 48
    } = options;

    const normalizedKey = normalizeOutputKey(key);

    if (DETECTION_KEYS.has(normalizedKey)) {
        return {
            text: `${countNestedArrayItems(value)} 个检测结果`,
            title: null,
            kind: 'detections'
        };
    }

    if (SUPPRESSED_DETECTION_KEYS.has(normalizedKey)) {
        return {
            text: `${countNestedArrayItems(value)} 个已抑制`,
            title: null,
            kind: 'suppressed'
        };
    }

    if (typeof value === 'number') {
        return {
            text: Number.isInteger(value) ? String(value) : value.toFixed(3),
            title: null,
            kind: 'number'
        };
    }

    if (typeof value === 'boolean') {
        return {
            text: value ? '是' : '否',
            title: null,
            kind: 'boolean'
        };
    }

    if (typeof value === 'string') {
        const stringValue = value.trim() || '--';
        const truncated = truncateText(stringValue, stringMaxLength);
        return {
            text: truncated.text,
            title: truncated.title,
            kind: 'string'
        };
    }

    if (Array.isArray(value)) {
        return {
            text: `${value.length} 项`,
            title: null,
            kind: 'array'
        };
    }

    if (value && typeof value === 'object') {
        const fieldCount = Object.keys(value).length;
        return {
            text: fieldCount > 0 ? `${fieldCount} 个字段` : '对象',
            title: null,
            kind: 'object'
        };
    }

    return {
        text: '无',
        title: null,
        kind: 'null'
    };
}

export function buildPreviewSummaryItems(outputs, options = {}) {
    const {
        maxItems = 3,
        stringMaxLength = 42,
        skipImageLikeValues = true
    } = options;

    if (!outputs || typeof outputs !== 'object') {
        return [];
    }

    const items = [];
    for (const [key, value] of Object.entries(outputs)) {
        if (items.length >= maxItems) {
            break;
        }

        if (isPreviewTechnicalDiagnostic(key) ||
            isPreviewTechnicalDiagnostic(value) ||
            normalizeOutputKey(key) === 'diagnostics') {
            continue;
        }

        if (skipImageLikeValues &&
            (isPreviewImageOutputKey(key) ||
                (typeof value === 'string' && isPreviewImageLikePayload(value)))) {
            continue;
        }

        const formattedValue = formatPreviewOutputValue(key, value, { stringMaxLength });
        items.push({
            key: getPreviewResultLabel(key),
            rawKey: key,
            value: formattedValue.text,
            title: formattedValue.title,
            kind: formattedValue.kind
        });
    }

    return items;
}
