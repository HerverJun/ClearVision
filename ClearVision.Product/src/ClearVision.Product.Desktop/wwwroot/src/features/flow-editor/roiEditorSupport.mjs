import {
    CIRCLE_PARAM_KEYS,
    DEFAULT_RECT_PARAM_KEYS,
    POLAR_ANNULUS_ARC_PARAM_KEYS,
    REGION_RECT_PARAM_KEYS,
    computeClockwiseAngleSpanDegrees,
    normalizeAnnulusGeometry,
    normalizeAngleDegrees,
    normalizeCircleGeometry,
    rectFromParams,
    rectToParams,
    validateAnnulusGeometry
} from './roiGeometry.mjs';

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
        return bounds ? normalizeCircleGeometry(circle, bounds) : circle;
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
        return bounds ? normalizeAnnulusGeometry(annulus, bounds) : annulus;
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
        const startAngle = normalizeAngleDegrees(geometry.startAngle ?? 0);
        const span = Number(geometry.spanDegrees ?? computeClockwiseAngleSpanDegrees(startAngle, geometry.endAngle ?? 360, { allowFullCircle: true })) || 360;
        const endAngle = startAngle + span;
        return {
            [keys.centerX]: Math.round(Number(geometry.centerX ?? geometry.x ?? 0)),
            [keys.centerY]: Math.round(Number(geometry.centerY ?? geometry.y ?? 0)),
            [keys.innerRadius]: Math.max(0, Math.round(Number(geometry.innerRadius ?? 0))),
            [keys.outerRadius]: Math.max(1, Math.round(Number(geometry.outerRadius ?? geometry.radius ?? 1))),
            [keys.startAngle]: roundGeometryNumber(startAngle),
            [keys.endAngle]: roundGeometryNumber(endAngle)
        };
    }

    return {};
}

export function getOperatorRoiConfig(operator) {
    const type = String(operator?.type || operator?.operatorType || '').trim();

    if (type === 'RoiManager') {
        const shape = String(readOperatorValue(operator, 'Shape', 'Rectangle'));
        const editable = shape === 'Rectangle' || shape === 'Circle';
        const geometryAdapter = shape === 'Circle'
            ? { kind: 'circle', paramKeys: CIRCLE_PARAM_KEYS }
            : { kind: 'rectangle', paramKeys: DEFAULT_RECT_PARAM_KEYS };
        return {
            supported: true,
            editable,
            shape,
            geometryAdapter,
            rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
            subtitle: shape === 'Circle'
                ? '拖拽圆形 ROI，自动同步到 CenterX / CenterY / Radius'
                : '拖拽框选矩形区域，自动同步到 X / Y / Width / Height',
            readonlyMessage: '图上编辑当前支持矩形和圆形 ROI，多边形仍使用参数输入'
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
            subtitle: '拖拽框选矩形区域，自动同步到 RegionX / RegionY / RegionW / RegionH',
            readonlyMessage: '图上编辑当前仅支持 BoxFilter 的 Region 模式，请先把 FilterMode 切到 Region'
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
        const shape = span > 0 && span < 360 ? 'Arc' : 'Annulus';

        return {
            supported: true,
            editable: validation.valid,
            shape,
            geometryAdapter: { kind: 'annulusArc', paramKeys: POLAR_ANNULUS_ARC_PARAM_KEYS },
            rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
            subtitle: '拖拽圆环/圆弧区域，自动同步到 CenterX / CenterY / InnerRadius / OuterRadius / StartAngle / EndAngle',
            readonlyMessage: '圆环参数无效：OuterRadius 必须大于 InnerRadius，且半径和角度必须为有限值'
        };
    }

    return {
        supported: false,
        editable: false,
        shape: 'Rectangle',
        geometryAdapter: { kind: 'rectangle', paramKeys: DEFAULT_RECT_PARAM_KEYS },
        rectParamKeys: DEFAULT_RECT_PARAM_KEYS,
        subtitle: '拖拽框选矩形区域，自动同步到 X / Y / Width / Height',
        readonlyMessage: '当前节点不支持 ROI 图上编辑'
    };
}
