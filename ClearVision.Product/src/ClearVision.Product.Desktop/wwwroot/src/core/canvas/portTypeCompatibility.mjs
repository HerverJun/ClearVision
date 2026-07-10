const IMAGE_TO_REGION_MISMATCH_MESSAGE =
    '当前输出是 Image/图像，不是 Region；请插入 BinaryImageToRegion。';

const CONTOUR_TO_REGION_MISMATCH_MESSAGE =
    '当前输出是 Contour/轮廓，不是 Region/像素区域。区域形态学需要 Region；请从二值图使用 BinaryImageToRegion 生成 Region，或改用轮廓测量、Blob特征处理算子。';

const BLOB_LIST_TO_REGION_MISMATCH_MESSAGE =
    '当前输出是 BlobList/Blob结果列表，不是 Region/像素区域。区域形态学需要 Region；请从二值图使用 BinaryImageToRegion 生成 Region，或改用 Blob 特征处理算子。';

const PORT_TYPE_DEFINITIONS = Object.freeze({
    Any: Object.freeze({ label: 'Any/任意', color: '#94a3b8', description: '未限定的数据类型。仅用于明确声明为通用数据的端口。' }),
    Image: Object.freeze({ label: 'Image/图像', color: '#16a34a', description: '像素图像数据。' }),
    String: Object.freeze({ label: 'String/文本', color: '#2563eb', description: '文本数据。' }),
    Integer: Object.freeze({ label: 'Integer/整数', color: '#ea580c', description: '整数数值。' }),
    Float: Object.freeze({ label: 'Float/浮点数', color: '#f59e0b', description: '浮点数值。' }),
    Boolean: Object.freeze({ label: 'Boolean/布尔值', color: '#dc2626', description: '真/假逻辑值。' }),
    Point: Object.freeze({ label: 'Point/点', color: '#db2777', description: '单个二维点。' }),
    Rectangle: Object.freeze({ label: 'Rectangle/矩形', color: '#ec4899', description: '矩形几何数据。' }),
    Contour: Object.freeze({ label: 'Contour/轮廓', color: '#7c3aed', description: '由边界点组成的轮廓，不等同于像素区域。' }),
    PointList: Object.freeze({ label: 'PointList/点集', color: '#f472b6', description: '二维点列表。' }),
    DetectionResult: Object.freeze({ label: 'DetectionResult/检测结果', color: '#0891b2', description: '单个检测结果。' }),
    DetectionList: Object.freeze({ label: 'DetectionList/检测结果列表', color: '#06b6d4', description: '检测结果列表。' }),
    CircleData: Object.freeze({ label: 'CircleData/圆数据', color: '#4f46e5', description: '圆心、半径等圆几何数据。' }),
    LineData: Object.freeze({ label: 'LineData/线数据', color: '#6366f1', description: '直线几何数据。' }),
    Region: Object.freeze({ label: 'Region/像素区域', color: '#0d9488', description: '由前景像素组成的区域，可用于区域形态学。' }),
    BlobList: Object.freeze({ label: 'BlobList/Blob结果列表', color: '#d97706', description: 'Blob 结果字典列表，不是轮廓或像素区域。' }),
    BlobFeatureList: Object.freeze({ label: 'BlobFeatureList/Blob特征列表', color: '#e11d48', description: '按 Blob 汇总的详细特征表。' })
});

const PORT_TYPE_ENUM_NAMES = Object.freeze({
    0: 'Image',
    1: 'Integer',
    2: 'Float',
    3: 'Boolean',
    4: 'String',
    5: 'Point',
    6: 'Rectangle',
    7: 'Contour',
    8: 'PointList',
    9: 'DetectionResult',
    10: 'DetectionList',
    11: 'CircleData',
    12: 'LineData',
    13: 'Region',
    14: 'BlobList',
    15: 'BlobFeatureList',
    99: 'Any'
});

export function normalizePortType(type) {
    if (typeof type === 'number') return type;
    if (type === null || type === undefined) return 'Any';

    const raw = String(type).trim();
    if (raw.length === 0) return 'Any';
    if (/^\d+$/.test(raw)) {
        return Number(raw);
    }

    const map = {
        any: 'Any',
        image: 'Image',
        string: 'String',
        integer: 'Integer',
        int: 'Integer',
        float: 'Float',
        double: 'Float',
        boolean: 'Boolean',
        bool: 'Boolean',
        point: 'Point',
        rectangle: 'Rectangle',
        contour: 'Contour',
        pointlist: 'PointList',
        point_list: 'PointList',
        detectionresult: 'DetectionResult',
        detection_result: 'DetectionResult',
        detectionlist: 'DetectionList',
        detection_list: 'DetectionList',
        circledata: 'CircleData',
        circle_data: 'CircleData',
        linedata: 'LineData',
        line_data: 'LineData',
        region: 'Region',
        bloblist: 'BlobList',
        blob_list: 'BlobList',
        blobfeaturelist: 'BlobFeatureList',
        blob_feature_list: 'BlobFeatureList'
    };

    return map[raw.toLowerCase()] || raw;
}

export function resolvePortTypeName(type) {
    const normalized = normalizePortType(type);
    if (typeof normalized === 'number') {
        return PORT_TYPE_ENUM_NAMES[normalized] || String(normalized);
    }
    return normalized;
}

export function getPortTypeFamily(type) {
    const normalized = resolvePortTypeName(type);
    if (normalized === 'Any') return 'Any';
    if (normalized === 'Image') return 'Image';
    if (normalized === 'Integer' || normalized === 'Float') return 'Number';
    if (normalized === 'Boolean') return 'Boolean';
    if (normalized === 'String') return 'String';
    if (normalized === 'Point' || normalized === 'Rectangle' || normalized === 'PointList') return 'Geometry';
    if (normalized === 'Contour') return 'Contour';
    if (normalized === 'DetectionResult' || normalized === 'DetectionList') return 'Detection';
    if (normalized === 'CircleData') return 'CircleData';
    if (normalized === 'LineData') return 'LineData';
    if (normalized === 'Region') return 'Region';
    if (normalized === 'BlobList') return 'BlobList';
    if (normalized === 'BlobFeatureList') return 'BlobFeatureList';
    return normalized;
}

export function arePortTypesCompatible(sourceType, targetType) {
    const sourceFamily = getPortTypeFamily(sourceType);
    const targetFamily = getPortTypeFamily(targetType);
    return sourceFamily === 'Any' || targetFamily === 'Any' || sourceFamily === targetFamily;
}

export function getPortTypeDefinition(type) {
    const name = resolvePortTypeName(type);
    return PORT_TYPE_DEFINITIONS[name] || Object.freeze({
        label: name,
        color: PORT_TYPE_DEFINITIONS.Any.color,
        description: `${name} 数据。`
    });
}

export function getPortTypeColor(type) {
    return getPortTypeDefinition(type).color;
}

export function formatPortTypeForMessage(type) {
    return getPortTypeDefinition(type).label;
}

export function getPortTypeMismatchMessage(sourceType, targetType) {
    const source = resolvePortTypeName(sourceType);
    const target = resolvePortTypeName(targetType);
    if (source === 'Contour' && target === 'Region') {
        return CONTOUR_TO_REGION_MISMATCH_MESSAGE;
    }
    if (source === 'Image' && target === 'Region') {
        return IMAGE_TO_REGION_MISMATCH_MESSAGE;
    }
    if (source === 'BlobList' && target === 'Region') {
        return BLOB_LIST_TO_REGION_MISMATCH_MESSAGE;
    }

    return `端口类型不匹配：${formatPortTypeForMessage(sourceType)} -> ${formatPortTypeForMessage(targetType)}。请连接同类型端口或使用明确支持该转换的算子。`;
}

function readPortValue(port, ...keys) {
    for (const key of keys) {
        if (port && Object.prototype.hasOwnProperty.call(port, key) && port[key] !== undefined && port[key] !== null) {
            return port[key];
        }
    }
    return undefined;
}

export function buildPortTooltipModel(port, options = {}) {
    const direction = options.direction === 'output' ? 'output' : 'input';
    const technicalName = String(readPortValue(port, 'name', 'Name') || (direction === 'output' ? '输出端口' : '输入端口'));
    const displayName = String(readPortValue(port, 'displayName', 'DisplayName') || technicalName);
    const type = readPortValue(port, 'type', 'Type', 'dataType', 'DataType') ?? 'Any';
    const typeDefinition = getPortTypeDefinition(type);
    const isRequired = direction === 'input' && Boolean(readPortValue(port, 'isRequired', 'IsRequired'));
    const explicitDescription = String(readPortValue(port, 'description', 'Description') || '').trim();
    const description = explicitDescription || `${direction === 'output' ? '输出' : '输入'}${typeDefinition.description}`;
    const name = displayName === technicalName ? technicalName : `${displayName}（${technicalName}）`;
    const directionLabel = direction === 'output' ? '输出' : '输入';
    const incompatibilityMessage = String(options.incompatibilityMessage || '').trim();
    const lines = [
        `名称：${name}`,
        `方向：${directionLabel}`,
        `数据类型：${typeDefinition.label}`,
        `必填：${isRequired ? '是' : '否'}`,
        `说明：${description}`
    ];
    if (incompatibilityMessage) {
        lines.push(`不兼容：${incompatibilityMessage}`);
    }

    return {
        name,
        direction,
        directionLabel,
        type: resolvePortTypeName(type),
        typeLabel: typeDefinition.label,
        color: typeDefinition.color,
        isRequired,
        description,
        incompatibilityMessage,
        text: lines.join('\n')
    };
}

export function buildPortTooltipText(port, options = {}) {
    return buildPortTooltipModel(port, options).text;
}

export function canonicalizeOperatorPortType(operatorType, portName, direction, declaredType) {
    const type = String(operatorType || '').trim().toLowerCase();
    const name = String(portName || '').trim().toLowerCase();
    const normalizedDirection = String(direction || '').trim().toLowerCase();

    if (type === 'blobanalysis' && normalizedDirection === 'output') {
        if (name === 'blobs') return 'BlobList';
        if (name === 'blobfeatures') return 'BlobFeatureList';
    }
    if (type === 'bloblabeling' && normalizedDirection === 'input' && name === 'blobs') {
        return 'BlobList';
    }

    return normalizePortType(declaredType);
}

export { PORT_TYPE_DEFINITIONS };
