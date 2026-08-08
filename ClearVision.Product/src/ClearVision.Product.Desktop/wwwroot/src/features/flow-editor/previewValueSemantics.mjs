const COLLECTION_PORT_TYPES = new Map([
    ['pointlist', 'collection'],
    ['detectionlist', 'detection-list'],
    ['bloblist', 'blob-list'],
    ['blobfeaturelist', 'blob-feature-list']
]);
const GEOMETRY_PORT_TYPES = new Set(['point', 'rectangle', 'circledata', 'linedata', 'contour', 'region']);
const SCALAR_PORT_TYPES = new Set(['integer', 'float', 'boolean', 'string']);
const DETECTION_KEY_FALLBACKS = new Set(['detections', 'detectionlist', 'suppresseddetections']);

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

function normalizeToken(value) {
    return String(value ?? '').trim().replace(/[\s_-]+/g, '').toLowerCase();
}

function finiteCount(value) {
    if (value === null || value === undefined || value === '') {
        return null;
    }
    const number = typeof value === 'number' ? value : Number(value);
    return Number.isSafeInteger(number) && number >= 0 ? number : null;
}

function isObject(value) {
    return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function getCaseInsensitive(source, ...keys) {
    if (!isObject(source)) {
        return undefined;
    }

    const entries = Object.entries(source);
    for (const key of keys) {
        const match = entries.find(([candidate]) => candidate.toLowerCase() === key.toLowerCase());
        if (match) {
            return match[1];
        }
    }

    return undefined;
}

function getObservationChildren(node) {
    const children = readOwn(node, 'children', 'Children');
    return Array.isArray(children) ? children : [];
}

function observationNumber(node, ...keys) {
    return finiteCount(readOwn(node, ...keys));
}

function readObservationChildValue(node, ...names) {
    const normalizedNames = new Set(names.map(normalizeToken));
    const child = getObservationChildren(node).find(candidate =>
        normalizedNames.has(normalizeToken(readOwn(candidate, 'name', 'Name'))));
    if (!child) {
        return undefined;
    }

    const value = readOwn(child, 'displayValue', 'DisplayValue');
    const number = Number(value);
    return Number.isFinite(number) ? number : value;
}

function semanticFromPortType(dataType) {
    const normalized = normalizeToken(dataType);
    if (COLLECTION_PORT_TYPES.has(normalized)) {
        return COLLECTION_PORT_TYPES.get(normalized);
    }
    if (GEOMETRY_PORT_TYPES.has(normalized)) {
        return 'geometry';
    }
    if (SCALAR_PORT_TYPES.has(normalized)) {
        return 'scalar';
    }
    if (normalized === 'detectionresult') {
        return 'detection';
    }
    if (normalized === 'image') {
        return 'image-resource';
    }
    return null;
}

function semanticFromObservation(node) {
    const explicit = String(readOwn(node, 'semanticKind', 'SemanticKind') ?? '').trim().toLowerCase();
    if (explicit) {
        return explicit;
    }

    const kind = normalizeToken(readOwn(node, 'kind', 'Kind'));
    if (kind === 'null') {
        return 'absent';
    }
    if (['unsupported', 'unsupportedenumerable', 'objectdescriptor'].includes(kind)) {
        return 'unsupported';
    }
    if (kind === 'detectionlist') {
        return 'detection-list';
    }
    if (kind === 'array') {
        return 'collection';
    }
    if (kind === 'dictionary' || kind === 'object' || kind === 'jsonelement') {
        return 'dictionary';
    }
    if (['point', 'rectangle', 'rect', 'circle', 'line', 'region', 'contour'].includes(kind)) {
        return 'geometry';
    }
    if (['image', 'matrix', 'mask', 'binary', 'stream', 'resource', 'pointset', 'profile'].includes(kind)) {
        return 'image-resource';
    }
    if (['boolean', 'number', 'string', 'enum', 'guid', 'datetime', 'duration', 'nonfinitenumber'].includes(kind)) {
        return 'scalar';
    }
    return null;
}

function descriptorSemantic(value) {
    if (!isObject(value)) {
        return null;
    }

    const kind = normalizeToken(getCaseInsensitive(value, 'kind'));
    if (['unsupported', 'unsupportedenumerable', 'object', 'objectdescriptor'].includes(kind) &&
        getCaseInsensitive(value, 'displayValue') !== undefined) {
        return 'unsupported';
    }
    return null;
}

function nestedCollection(value) {
    if (!isObject(value)) {
        return null;
    }

    const items = getCaseInsensitive(value, 'items', 'detections');
    return Array.isArray(items) ? items : null;
}

function runtimeSemantic(value) {
    if (value === null || value === undefined) {
        return 'absent';
    }
    if (descriptorSemantic(value)) {
        return 'unsupported';
    }
    if (Array.isArray(value) || nestedCollection(value)) {
        return 'collection';
    }
    if (typeof value !== 'object') {
        return 'scalar';
    }
    return 'dictionary';
}

function collectionCounts(value, observationNode) {
    const observationVisible = observationNumber(observationNode, 'visibleItemCount', 'VisibleItemCount');
    const observationTotal = observationNumber(observationNode, 'totalItemCount', 'TotalItemCount');
    const nested = nestedCollection(value);
    const runtimeVisible = Array.isArray(value) ? value.length : nested?.length;
    const runtimeTotal = isObject(value)
        ? finiteCount(getCaseInsensitive(value, 'count', 'total', 'length'))
        : null;
    const visible = observationVisible ?? finiteCount(runtimeVisible) ?? observationTotal ?? runtimeTotal ?? 0;
    const total = observationTotal ?? runtimeTotal ?? finiteCount(runtimeVisible) ?? visible;
    return {
        visible,
        total,
        truncated: Boolean(readOwn(observationNode, 'truncated', 'Truncated')) || visible < total
    };
}

function numeric(value) {
    const parsed = typeof value === 'number' ? value : Number(value);
    return Number.isFinite(parsed) ? parsed : null;
}

function formatNumber(value) {
    const number = numeric(value);
    if (number === null) {
        return '--';
    }
    return Number.isInteger(number) ? String(number) : number.toFixed(3).replace(/0+$/, '').replace(/\.$/, '');
}

function geometryValues(value, observationNode) {
    const read = (...keys) => {
        const runtime = getCaseInsensitive(value, ...keys);
        return runtime !== undefined ? runtime : readObservationChildValue(observationNode, ...keys);
    };
    return {
        x: read('X', 'CenterX'),
        y: read('Y', 'CenterY'),
        width: read('Width', 'W'),
        height: read('Height', 'H'),
        radius: read('Radius', 'R'),
        x1: read('X1', 'StartX'),
        y1: read('Y1', 'StartY'),
        x2: read('X2', 'EndX'),
        y2: read('Y2', 'EndY'),
        area: read('Area'),
        runLengthCount: read('RunLengthCount')
    };
}

function formatGeometry(dataType, value, observationNode) {
    const type = normalizeToken(dataType) || normalizeToken(readOwn(observationNode, 'kind', 'Kind'));
    const values = geometryValues(value, observationNode);
    if (type === 'point') {
        if (numeric(values.x) === null || numeric(values.y) === null) {
            return String(readOwn(observationNode, 'displayValue', 'DisplayValue') || '点几何结果');
        }
        return `(${formatNumber(values.x)}, ${formatNumber(values.y)})`;
    }
    if (type === 'rectangle' || type === 'rect') {
        if ([values.x, values.y, values.width, values.height].some(item => numeric(item) === null)) {
            return String(readOwn(observationNode, 'displayValue', 'DisplayValue') || '矩形几何结果');
        }
        return `${formatNumber(values.x)}, ${formatNumber(values.y)}, ${formatNumber(values.width)} × ${formatNumber(values.height)}`;
    }
    if (type === 'circledata' || type === 'circle') {
        if ([values.x, values.y, values.radius].some(item => numeric(item) === null)) {
            return String(readOwn(observationNode, 'displayValue', 'DisplayValue') || '圆几何结果');
        }
        return `中心 (${formatNumber(values.x)}, ${formatNumber(values.y)})，半径 ${formatNumber(values.radius)}`;
    }
    if (type === 'linedata' || type === 'line') {
        const coordinates = [values.x1, values.y1, values.x2, values.y2].map(numeric);
        if (coordinates.some(item => item === null)) {
            return String(readOwn(observationNode, 'displayValue', 'DisplayValue') || '线几何结果');
        }
        const [x1, y1, x2, y2] = coordinates;
        const dx = x2 - x1;
        const dy = y2 - y1;
        const length = Math.sqrt(dx * dx + dy * dy);
        return `(${formatNumber(values.x1)}, ${formatNumber(values.y1)}) → (${formatNumber(values.x2)}, ${formatNumber(values.y2)})${length === null ? '' : `，长度 ${formatNumber(length)}`}`;
    }
    if (type === 'region') {
        return values.area === undefined
            ? 'Region 几何结果'
            : `面积 ${formatNumber(values.area)}${values.runLengthCount === undefined ? '' : `，${formatNumber(values.runLengthCount)} 个游程`}`;
    }
    if (type === 'contour') {
        const { visible, total, truncated } = collectionCounts(value, observationNode);
        return truncated ? `${visible} / ${total} 个轮廓点，已截断` : `${total} 个轮廓点`;
    }
    return String(readOwn(observationNode, 'displayValue', 'DisplayValue') || '几何结果');
}

function formatScalar(value, observationNode, stringMaxLength) {
    const kind = normalizeToken(readOwn(observationNode, 'kind', 'Kind'));
    const source = value !== undefined ? value : readOwn(observationNode, 'displayValue', 'DisplayValue');
    if (kind === 'boolean' || typeof source === 'boolean') {
        return { text: source === true || String(source).toLowerCase() === 'true' ? '是' : '否', title: null };
    }
    if (kind === 'number' || typeof source === 'number') {
        return { text: formatNumber(source), title: null };
    }
    const text = String(source ?? '').trim() || '--';
    if (text.length <= stringMaxLength) {
        return { text, title: null };
    }
    const clipped = `${text.slice(0, Math.ceil((stringMaxLength - 3) * 0.58))}...${text.slice(-(Math.floor((stringMaxLength - 3) * 0.42)))}`;
    return { text: clipped, title: text };
}

export function classifyPreviewValue({
    key,
    value,
    declaredPortDataType = null,
    observationNode = null
} = {}) {
    const declaredType = declaredPortDataType || readOwn(observationNode, 'declaredPortDataType', 'DeclaredPortDataType');
    const observationKind = semanticFromObservation(observationNode);
    let semanticKind = value === null || (value === undefined && observationKind === 'absent')
        ? 'absent'
        : semanticFromPortType(declaredType) || observationKind || runtimeSemantic(value);

    if (!semanticKind && DETECTION_KEY_FALLBACKS.has(normalizeToken(key))) {
        semanticKind = 'detection-list';
    }
    semanticKind ||= 'unsupported';

    if (semanticKind === 'collection' && DETECTION_KEY_FALLBACKS.has(normalizeToken(key))) {
        semanticKind = 'detection-list';
    }

    if (['collection', 'detection-list', 'blob-list', 'blob-feature-list'].includes(semanticKind) &&
        descriptorSemantic(value) === 'unsupported' &&
        observationNumber(observationNode, 'totalItemCount', 'TotalItemCount') === null) {
        semanticKind = 'unsupported';
    }

    const counts = ['collection', 'detection-list', 'blob-list', 'blob-feature-list'].includes(semanticKind)
        ? collectionCounts(value, observationNode)
        : { visible: null, total: null, truncated: false };
    const observationChildren = getObservationChildren(observationNode);
    const fieldCount = observationNumber(observationNode, 'fieldCount', 'FieldCount') ??
        (semanticKind === 'dictionary' && observationChildren.length > 0 ? observationChildren.length : null) ??
        (semanticKind === 'dictionary' && isObject(value) ? Object.keys(value).length : null);

    return {
        semanticKind,
        declaredPortDataType: declaredType,
        visibleItemCount: counts.visible,
        totalItemCount: counts.total,
        fieldCount,
        truncated: counts.truncated
    };
}

export function formatPreviewSemanticValue({
    key,
    value,
    declaredPortDataType = null,
    observationNode = null,
    stringMaxLength = 48
} = {}) {
    const classification = classifyPreviewValue({ key, value, declaredPortDataType, observationNode });
    const { semanticKind } = classification;
    if (semanticKind === 'absent') {
        return { ...classification, text: '无输出', title: null, kind: 'null' };
    }
    if (['collection', 'detection-list', 'blob-list', 'blob-feature-list'].includes(semanticKind)) {
        const { visibleItemCount: visible, totalItemCount: total, truncated } = classification;
        const normalizedKey = normalizeToken(key);
        const detectionSuffix = normalizedKey === 'suppresseddetections' ? '个已抑制' : '个检测结果';
        const unit = semanticKind === 'detection-list' ? detectionSuffix : '项';
        const text = truncated
            ? `${visible} / ${total} ${unit}，已截断`
            : `${total} ${unit}`;
        const kind = semanticKind === 'detection-list'
            ? (normalizedKey === 'suppresseddetections' ? 'suppressed' : 'detections')
            : 'array';
        return { ...classification, text, title: null, kind };
    }
    if (semanticKind === 'dictionary') {
        const count = classification.fieldCount ?? 0;
        return { ...classification, text: count > 0 ? `${count} 个字段` : '对象', title: null, kind: 'object' };
    }
    if (semanticKind === 'unsupported') {
        return { ...classification, text: '暂不支持展示此结果类型', title: null, kind: 'unsupported' };
    }
    if (semanticKind === 'geometry' || semanticKind === 'detection') {
        return {
            ...classification,
            text: formatGeometry(classification.declaredPortDataType, value, observationNode),
            title: null,
            kind: 'geometry'
        };
    }
    if (semanticKind === 'image-resource') {
        const artifact = readOwn(observationNode, 'artifact', 'Artifact');
        const width = finiteCount(readOwn(artifact, 'width', 'Width')) ?? readObservationChildValue(observationNode, 'width');
        const height = finiteCount(readOwn(artifact, 'height', 'Height')) ?? readObservationChildValue(observationNode, 'height');
        const channels = finiteCount(readOwn(artifact, 'channels', 'Channels')) ?? readObservationChildValue(observationNode, 'channels');
        const text = artifact
            ? '图像内容已省略'
            : (width !== undefined && height !== undefined
                ? `${width} × ${height}${channels ? `，${channels} 通道` : ''}`
                : '图像/资源摘要');
        return { ...classification, text, title: null, kind: 'resource' };
    }

    const scalar = formatScalar(value, observationNode, stringMaxLength);
    const scalarKind = typeof value === 'number'
        ? 'number'
        : (typeof value === 'boolean' ? 'boolean' : (typeof value === 'string' ? 'string' : 'scalar'));
    return { ...classification, ...scalar, kind: scalarKind };
}

export function findObservationOutputNode(observation, outputKey) {
    const detail = readOwn(observation, 'detail', 'Detail');
    return getObservationChildren(detail).find(node => {
        const name = readOwn(node, 'outputPortName', 'OutputPortName') ?? readOwn(node, 'name', 'Name');
        return normalizeToken(name) === normalizeToken(outputKey);
    }) || null;
}
