const REGION_IMAGE_MISMATCH_MESSAGE =
    '区域闭运算需要 Region 输入，请先使用二值图转区域/区域生成算子；若要直接处理二值图，请使用图像形态学闭运算。';

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
        float: 'Float',
        boolean: 'Boolean',
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
        region: 'Region'
    };

    return map[raw.toLowerCase()] || raw;
}

export function getPortTypeFamily(type) {
    const normalized = normalizePortType(type);
    if (normalized === 'Any' || normalized === 99) return 'Any';
    if (normalized === 'Image' || normalized === 0) return 'Image';
    if (normalized === 'Integer' || normalized === 1 || normalized === 'Float' || normalized === 2) return 'Number';
    if (normalized === 'Boolean' || normalized === 3) return 'Boolean';
    if (normalized === 'String' || normalized === 4) return 'String';
    if (normalized === 'Point' || normalized === 5 || normalized === 'Rectangle' || normalized === 6 || normalized === 'PointList' || normalized === 8) return 'Geometry';
    if (normalized === 'Contour' || normalized === 7) return 'Contour';
    if (normalized === 'DetectionResult' || normalized === 9 || normalized === 'DetectionList' || normalized === 10) return 'Detection';
    if (normalized === 'CircleData' || normalized === 11) return 'CircleData';
    if (normalized === 'LineData' || normalized === 12) return 'LineData';
    if (normalized === 'Region' || normalized === 13) return 'Region';
    return normalized;
}

export function arePortTypesCompatible(sourceType, targetType) {
    const sourceFamily = getPortTypeFamily(sourceType);
    const targetFamily = getPortTypeFamily(targetType);
    return sourceFamily === 'Any' || targetFamily === 'Any' || sourceFamily === targetFamily;
}

export function formatPortTypeForMessage(type) {
    const normalized = normalizePortType(type);
    return typeof normalized === 'number' ? String(normalized) : normalized;
}

export function getPortTypeMismatchMessage(sourceType, targetType) {
    const source = normalizePortType(sourceType);
    const target = normalizePortType(targetType);
    if ((source === 'Image' || source === 0) && (target === 'Region' || target === 13)) {
        return REGION_IMAGE_MISMATCH_MESSAGE;
    }

    return `端口类型不匹配：${formatPortTypeForMessage(sourceType)} -> ${formatPortTypeForMessage(targetType)}`;
}
