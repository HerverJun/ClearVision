import {
    CIRCLE_PARAM_KEYS,
    CIRCLE_SEARCH_V2_PARAM_KEYS,
    DEFAULT_RECT_PARAM_KEYS,
    POINT_PAIRS_PARAM_KEYS,
    POLYGON_PARAM_KEYS,
    POLAR_ANNULUS_ARC_PARAM_KEYS,
    REGION_RECT_PARAM_KEYS,
    computeClockwiseAngleSpanDegrees,
    computeRawAngleSpanDegrees,
    normalizeAnnulusGeometry,
    normalizeAngleDegrees,
    normalizeCircleSearchV2Geometry,
    normalizeCircleGeometry,
    normalizePointSequenceGeometry,
    normalizePolygonGeometry,
    parsePointPairs,
    parsePolygonPoints,
    pointPairsToParamsJson,
    polygonToParamsJson,
    rectFromParams,
    rectToParams,
    validateAnnulusGeometry,
    validateCircleSearchV2Geometry,
    validatePointSequenceGeometry,
    validatePolygonGeometry
} from './roiGeometry.mjs';
import { isFeatureEnabled } from '../../shared/featureRegistry.js';

export const CIRCLE_SEARCH_V2_TOOL_FEATURE_ID = 'operator.circleSearchV2Tool';
export const CIRCLE_SEARCH_V2_TOOL_STARTUP_FLAG = 'Studio:CircleSearchV2ToolEnabled';
export const NPOINT_CALIBRATION_WORKBENCH_FEATURE_ID = 'operator.nPointCalibrationWorkbench';
export const NPOINT_CALIBRATION_WORKBENCH_STARTUP_FLAG = 'Studio:NPointCalibrationWorkbenchEnabled';
export const TEMPLATE_MATCHING_ROI_PARAM_KEYS = {
    x: 'RoiX',
    y: 'RoiY',
    width: 'RoiWidth',
    height: 'RoiHeight'
};

export function isCircleSearchV2ToolEnabled(options = {}) {
    const startupFlags = globalThis?.window?.__CLEARVISION_STARTUP__?.featureFlags;
    if (startupFlags && Object.prototype.hasOwnProperty.call(startupFlags, CIRCLE_SEARCH_V2_TOOL_STARTUP_FLAG)) {
        return startupFlags[CIRCLE_SEARCH_V2_TOOL_STARTUP_FLAG] === true;
    }

    if (typeof options?.circleSearchV2ToolEnabled === 'boolean') {
        return options.circleSearchV2ToolEnabled;
    }

    if (typeof options?.featureEnabled === 'boolean') {
        return options.featureEnabled;
    }

    return isFeatureEnabled(CIRCLE_SEARCH_V2_TOOL_FEATURE_ID);
}

export function isNPointCalibrationWorkbenchEnabled(options = {}) {
    const startupFlags = globalThis?.window?.__CLEARVISION_STARTUP__?.featureFlags;
    if (startupFlags && Object.prototype.hasOwnProperty.call(startupFlags, NPOINT_CALIBRATION_WORKBENCH_STARTUP_FLAG)) {
        return startupFlags[NPOINT_CALIBRATION_WORKBENCH_STARTUP_FLAG] === true;
    }

    if (typeof options?.nPointCalibrationWorkbenchEnabled === 'boolean') {
        return options.nPointCalibrationWorkbenchEnabled;
    }

    if (typeof options?.featureEnabled === 'boolean') {
        return options.featureEnabled;
    }

    return isFeatureEnabled(NPOINT_CALIBRATION_WORKBENCH_FEATURE_ID);
}

function readOperatorValue(operator, name, fallback = '') {
    const parameter = (operator?.parameters || []).find(item =>
        String(item?.name || item?.Name || '').toLowerCase() === String(name).toLowerCase());
    const value = parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue;
    return value == null ? fallback : value;
}

function readOperatorValues(operator) {
    const values = {};
    (operator?.parameters || []).forEach(param => {
        const key = String(param?.name || param?.Name || '');
        if (!key) {
            return;
        }
        values[key] = param?.value ?? param?.Value ?? param?.defaultValue ?? param?.DefaultValue;
    });
    return values;
}

function readNumberValue(values, key, fallback = 0) {
    const value = Number(values?.[key]);
    return Number.isFinite(value) ? value : fallback;
}

function readStringValue(values, key, fallback = '') {
    const value = values?.[key];
    return value === null || value === undefined || String(value).trim() === ''
        ? fallback
        : String(value);
}

function readBooleanValue(values, key, fallback = false) {
    if (!key || !Object.prototype.hasOwnProperty.call(values || {}, key)) {
        return fallback;
    }

    const value = values?.[key];
    if (typeof value === 'boolean') {
        return value;
    }

    return String(value).trim().toLowerCase() === 'true';
}

function roundGeometryNumber(value, digits = 3) {
    const numberValue = Number(value);
    if (!Number.isFinite(numberValue)) {
        return 0;
    }
    const scale = 10 ** digits;
    return Math.round(numberValue * scale) / scale;
}

export function geometryFromParams(values, config, bounds = null) {
    const adapter = config?.geometryAdapter;
    if (!adapter) {
        return null;
    }

    if (adapter.kind === 'rectangle') {
        if (adapter.enabledParamName && !readBooleanValue(values, adapter.enabledParamName, false)) {
            return null;
        }

        return {
            kind: 'rectangle',
            ...rectFromParams(values, adapter.paramKeys)
        };
    }

    if (adapter.kind === 'circle') {
        const keys = adapter.paramKeys || CIRCLE_PARAM_KEYS;
        const circle = {
            kind: 'circle',
            centerX: readNumberValue(values, keys.centerX, 0),
            centerY: readNumberValue(values, keys.centerY, 0),
            radius: readNumberValue(values, keys.radius, 1)
        };
        return normalizeCircleGeometry(circle, null);
    }

    if (adapter.kind === 'annulusArc') {
        const keys = adapter.paramKeys || POLAR_ANNULUS_ARC_PARAM_KEYS;
        const annulus = {
            kind: config.shape === 'Arc' ? 'arc' : 'annulus',
            centerX: readNumberValue(values, keys.centerX, 0),
            centerY: readNumberValue(values, keys.centerY, 0),
            innerRadius: readNumberValue(values, keys.innerRadius, 0),
            outerRadius: readNumberValue(values, keys.outerRadius, 1),
            startAngle: readNumberValue(values, keys.startAngle, 0),
            endAngle: readNumberValue(values, keys.endAngle, 360)
        };
        return normalizeAnnulusGeometry(annulus, null);
    }

    if (adapter.kind === 'circleSearchV2') {
        const keys = adapter.paramKeys || CIRCLE_SEARCH_V2_PARAM_KEYS;
        const mode = readStringValue(values, keys.searchCenterMode, 'ImageCenter');
        const centerX = mode.toLowerCase() === 'imagecenter' && bounds
            ? (Number(bounds.width) - 1) * 0.5
            : readNumberValue(values, keys.centerX, 0);
        const centerY = mode.toLowerCase() === 'imagecenter' && bounds
            ? (Number(bounds.height) - 1) * 0.5
            : readNumberValue(values, keys.centerY, 0);
        const geometry = {
            kind: 'circleSearchV2',
            searchCenterMode: mode,
            centerX,
            centerY,
            minRadius: readNumberValue(values, keys.minRadius, 1),
            nominalRadius: readNumberValue(values, keys.nominalRadius, 1),
            maxRadius: readNumberValue(values, keys.maxRadius, 1)
        };
        if (!validateCircleSearchV2Geometry(geometry, bounds).valid) {
            return null;
        }

        return normalizeCircleSearchV2Geometry(geometry, bounds);
    }

    if (adapter.kind === 'polygon') {
        const keys = adapter.paramKeys || POLYGON_PARAM_KEYS;
        const polygon = parsePolygonPoints(values?.[keys.points]);
        if (!polygon || !validatePolygonGeometry(polygon, bounds).valid) {
            return null;
        }

        return normalizePolygonGeometry(polygon);
    }

    if (adapter.kind === 'pointSequence') {
        const keys = adapter.paramKeys || POINT_PAIRS_PARAM_KEYS;
        const sequence = parsePointPairs(values?.[keys.pointPairs]) || { kind: 'pointSequence', points: [] };
        if (!validatePointSequenceGeometry(sequence, bounds).valid) {
            return null;
        }

        return normalizePointSequenceGeometry(sequence);
    }

    return null;
}

export function geometryToParams(geometry, config) {
    const adapter = config?.geometryAdapter;
    if (!adapter || !geometry) {
        return {};
    }

    if (adapter.kind === 'rectangle') {
        return rectToParams(geometry, adapter.paramKeys);
    }

    if (adapter.kind === 'circle') {
        const keys = adapter.paramKeys || CIRCLE_PARAM_KEYS;
        return {
            [keys.centerX]: Math.round(Number(geometry.centerX ?? geometry.x ?? 0)),
            [keys.centerY]: Math.round(Number(geometry.centerY ?? geometry.y ?? 0)),
            [keys.radius]: Math.max(1, Math.round(Number(geometry.radius ?? 1)))
        };
    }

    if (adapter.kind === 'annulusArc') {
        const keys = adapter.paramKeys || POLAR_ANNULUS_ARC_PARAM_KEYS;
        const startAngle = Number(geometry.startAngle ?? 0);
        const rawEndAngle = Number(geometry.endAngle ?? 360);
        const span = Number(geometry.spanDegrees ?? computeRawAngleSpanDegrees(startAngle, rawEndAngle));
        const endAngle = Number.isFinite(span) ? startAngle + span : rawEndAngle;
        return {
            [keys.centerX]: Math.round(Number(geometry.centerX ?? geometry.x ?? 0)),
            [keys.centerY]: Math.round(Number(geometry.centerY ?? geometry.y ?? 0)),
            [keys.innerRadius]: Math.max(0, Math.round(Number(geometry.innerRadius ?? 0))),
            [keys.outerRadius]: Math.max(1, Math.round(Number(geometry.outerRadius ?? geometry.radius ?? 1))),
            [keys.startAngle]: roundGeometryNumber(startAngle),
            [keys.endAngle]: roundGeometryNumber(endAngle)
        };
    }

    if (adapter.kind === 'circleSearchV2') {
        const keys = adapter.paramKeys || CIRCLE_SEARCH_V2_PARAM_KEYS;
        return {
            [keys.searchCenterMode]: 'Explicit',
            [keys.centerX]: roundGeometryNumber(Number(geometry.centerX ?? geometry.x ?? 0)),
            [keys.centerY]: roundGeometryNumber(Number(geometry.centerY ?? geometry.y ?? 0)),
            [keys.minRadius]: Math.max(1, roundGeometryNumber(Number(geometry.minRadius ?? geometry.innerRadius ?? 1))),
            [keys.nominalRadius]: Math.max(1, roundGeometryNumber(Number(geometry.nominalRadius ?? 1))),
            [keys.maxRadius]: Math.max(1, roundGeometryNumber(Number(geometry.maxRadius ?? geometry.outerRadius ?? geometry.radius ?? 1)))
        };
    }

    if (adapter.kind === 'polygon') {
        const keys = adapter.paramKeys || POLYGON_PARAM_KEYS;
        return {
            [keys.points]: polygonToParamsJson(geometry)
        };
    }

    if (adapter.kind === 'pointSequence') {
        const keys = adapter.paramKeys || POINT_PAIRS_PARAM_KEYS;
        return {
            [keys.pointPairs]: pointPairsToParamsJson(geometry)
        };
    }

    return {};
}

export function getOperatorRoiConfig(operator, options = {}) {
    const type = String(operator?.type || operator?.operatorType || '').trim();

    if (type === 'RoiManager') {
        const shape = String(readOperatorValue(operator, 'Shape', 'Rectangle'));
        const polygon = shape === 'Polygon'
            ? parsePolygonPoints(readOperatorValue(operator, 'PolygonPoints', '[]'))
            : null;
        const polygonValidation = polygon ? validatePolygonGeometry(polygon) : { valid: false };
        const editable = shape === 'Rectangle' || shape === 'Circle' || (shape === 'Polygon' && polygonValidation.valid);
        const geometryAdapter = shape === 'Circle'
            ? { kind: 'circle', paramKeys: CIRCLE_PARAM_KEYS }
            : shape === 'Polygon'
                ? { kind: 'polygon', paramKeys: POLYGON_PARAM_KEYS }
                : { kind: 'rectangle', paramKeys: DEFAULT_RECT_PARAM_KEYS };

        return {
            supported: true,
            editable,
            shape,
            geometryAdapter,
            rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
            subtitle: shape === 'Circle'
                ? '拖拽圆形 ROI，提交后写入 CenterX / CenterY / Radius。'
                : shape === 'Polygon'
                    ? '编辑多边形 ROI 顶点，提交后写入 PolygonPoints JSON。'
                    : '拖拽矩形 ROI，提交后写入 X / Y / Width / Height。',
            readonlyMessage: shape === 'Polygon'
                ? 'PolygonPoints 必须是有效 JSON，至少包含 3 个有限、图内且不自交的顶点。'
                : '图上编辑支持矩形、圆形和有效多边形 ROI。'
        };
    }

    if (type === 'TemplateMatching') {
        const values = readOperatorValues(operator);
        const useRoi = readBooleanValue(values, 'UseRoi', false);
        return {
            supported: true,
            editable: true,
            shape: 'Rectangle',
            geometryAdapter: {
                kind: 'rectangle',
                paramKeys: TEMPLATE_MATCHING_ROI_PARAM_KEYS,
                enabledParamName: 'UseRoi'
            },
            rectParamKeys: TEMPLATE_MATCHING_ROI_PARAM_KEYS,
            commitValues: { UseRoi: true },
            clearValues: {
                UseRoi: false,
                RoiX: 0,
                RoiY: 0,
                RoiWidth: 0,
                RoiHeight: 0
            },
            subtitle: useRoi
                ? '拖拽模板搜索 ROI，提交后写入 UseRoi / RoiX / RoiY / RoiWidth / RoiHeight。'
                : '框选模板搜索 ROI，提交后启用 UseRoi 并写入 RoiX / RoiY / RoiWidth / RoiHeight。',
            readonlyMessage: 'TemplateMatching 搜索 ROI 可在搜索输入图像上编辑。',
            description: '模板搜索 ROI 使用搜索图坐标。模板原点必须基于模板图坐标，不能在这里用搜索图点选写入 OriginX / OriginY。'
        };
    }

    if (type === 'BoxFilter') {
        const filterMode = String(readOperatorValue(operator, 'FilterMode', 'Area'));
        const editable = filterMode.toLowerCase() === 'region';
        return {
            supported: true,
            editable,
            shape: 'Rectangle',
            geometryAdapter: { kind: 'rectangle', paramKeys: REGION_RECT_PARAM_KEYS },
            rectParamKeys: REGION_RECT_PARAM_KEYS,
            subtitle: '拖拽区域矩形，提交后写入 RegionX / RegionY / RegionW / RegionH。',
            readonlyMessage: 'BoxFilter 仅在 FilterMode 为 Region 时支持图上编辑。'
        };
    }

    if (type === 'PolarUnwrap') {
        const values = readOperatorValues(operator);
        const validation = validateAnnulusGeometry({
            kind: 'annulus',
            centerX: readNumberValue(values, 'CenterX', 0),
            centerY: readNumberValue(values, 'CenterY', 0),
            innerRadius: readNumberValue(values, 'InnerRadius', 0),
            outerRadius: readNumberValue(values, 'OuterRadius', 1),
            startAngle: readNumberValue(values, 'StartAngle', 0),
            endAngle: readNumberValue(values, 'EndAngle', 360)
        });
        const span = computeClockwiseAngleSpanDegrees(
            readNumberValue(values, 'StartAngle', 0),
            readNumberValue(values, 'EndAngle', 360),
            { allowFullCircle: true }
        );
        const rawSpan = computeRawAngleSpanDegrees(
            readNumberValue(values, 'StartAngle', 0),
            readNumberValue(values, 'EndAngle', 360)
        );
        const shape = Math.abs(rawSpan) > 0 && Math.abs(rawSpan) < 360 ? 'Arc' : 'Annulus';

        return {
            supported: true,
            editable: validation.valid,
            shape,
            geometryAdapter: { kind: 'annulusArc', paramKeys: POLAR_ANNULUS_ARC_PARAM_KEYS },
            rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
            subtitle: '编辑环形或弧形区域，提交后写入 CenterX / CenterY / 半径 / 角度。',
            readonlyMessage: '环形参数无效：OuterRadius 必须大于 InnerRadius，且所有值必须为有限数。'
        };
    }

    if (type === 'NPointCalibration') {
        if (!isNPointCalibrationWorkbenchEnabled(options)) {
            return {
                supported: false,
                editable: false,
                shape: 'PointSequence',
                geometryAdapter: { kind: 'pointSequence', paramKeys: POINT_PAIRS_PARAM_KEYS },
                rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
                subtitle: 'N 点标定草稿工作台已禁用。',
                readonlyMessage: 'N 点标定草稿工作台已禁用，请使用通用 PointPairs 参数。'
            };
        }

        const sequence = parsePointPairs(readOperatorValue(operator, 'PointPairs', '[]')) ||
            { kind: 'pointSequence', points: [] };
        const validation = validatePointSequenceGeometry(sequence);

        return {
            supported: true,
            editable: validation.valid,
            shape: 'PointSequence',
            geometryAdapter: { kind: 'pointSequence', paramKeys: POINT_PAIRS_PARAM_KEYS },
            rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
            subtitle: '编辑标定图像采样点，提交时保留 PointPairs JSON 中的 WorldX / WorldY。',
            readonlyMessage: 'PointPairs 必须是可解析的 JSON 数组。'
        };
    }

    if (type === 'CircleMeasurement') {
        const method = String(readOperatorValue(operator, 'Method', 'HoughCircle'));
        if (method.trim().toLowerCase() === 'caliperfitv2' && isCircleSearchV2ToolEnabled(options)) {
            const values = readOperatorValues(operator);
            const geometry = {
                kind: 'circleSearchV2',
                centerX: readNumberValue(values, 'SearchCenterX', 0),
                centerY: readNumberValue(values, 'SearchCenterY', 0),
                minRadius: readNumberValue(values, 'MinRadius', 1),
                nominalRadius: readNumberValue(values, 'NominalRadius', 1),
                maxRadius: readNumberValue(values, 'MaxRadius', 1)
            };
            const validation = validateCircleSearchV2Geometry(geometry);

            return {
                supported: true,
                editable: validation.valid,
                shape: 'CircleSearchV2',
                geometryAdapter: { kind: 'circleSearchV2', paramKeys: CIRCLE_SEARCH_V2_PARAM_KEYS },
                rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
                subtitle: '编辑搜索环和名义圆，提交后写入 SearchCenterX / SearchCenterY / MinRadius / NominalRadius / MaxRadius。',
                readonlyMessage: 'Circle Search V2 几何要求 1 <= MinRadius <= NominalRadius <= MaxRadius。'
            };
        }
    }

    if (type === 'CaliperTool') {
        return {
            supported: true,
            editable: true,
            shape: 'Rectangle',
            geometryAdapter: { kind: 'rectangle', paramKeys: DEFAULT_RECT_PARAM_KEYS },
            rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
            subtitle: '框选卡尺搜索矩形，提交后创建或更新连接到 SearchRegion 的 RectangleRegion 节点。',
            readonlyMessage: 'CaliperTool.SearchRegion 是输入端口，必须通过 RectangleRegion 连线建模。',
            description: '卡尺搜索区域通过 RectangleRegion -> CaliperTool.SearchRegion 连接表达，不会给 CaliperTool 增加伪参数。'
        };
    }

    return {
        supported: false,
        editable: false,
        shape: 'Rectangle',
        geometryAdapter: { kind: 'rectangle', paramKeys: DEFAULT_RECT_PARAM_KEYS },
        rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
        subtitle: '拖拽 ROI 以更新参数。',
        readonlyMessage: '当前节点不支持图上编辑。'
    };
}
