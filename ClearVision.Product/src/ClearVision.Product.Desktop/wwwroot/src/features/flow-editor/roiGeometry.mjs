export const ROI_HANDLE_NAMES = [
    'nw',
    'n',
    'ne',
    'e',
    'se',
    's',
    'sw',
    'w'
];

export const DEFAULT_RECT_PARAM_KEYS = {
    x: 'X',
    y: 'Y',
    width: 'Width',
    height: 'Height'
};

export const REGION_RECT_PARAM_KEYS = {
    x: 'RegionX',
    y: 'RegionY',
    width: 'RegionW',
    height: 'RegionH'
};

export const CIRCLE_PARAM_KEYS = {
    centerX: 'CenterX',
    centerY: 'CenterY',
    radius: 'Radius'
};

export const POLAR_ANNULUS_ARC_PARAM_KEYS = {
    centerX: 'CenterX',
    centerY: 'CenterY',
    innerRadius: 'InnerRadius',
    outerRadius: 'OuterRadius',
    startAngle: 'StartAngle',
    endAngle: 'EndAngle'
};

export const RECT_DRAFT_HISTORY_LIMIT = 50;
export const GEOMETRY_ANGLE_UNITS = 'degrees';
export const GEOMETRY_ANGLE_ZERO_DIRECTION = '+x';
export const GEOMETRY_ANGLE_DIRECTION = 'clockwise-image-y-down';

export function clamp(value, min, max) {
    if (!Number.isFinite(value)) {
        return min;
    }

    return Math.min(Math.max(value, min), max);
}

function readNumber(value, fallback = 0) {
    const numberValue = Number(value);
    return Number.isFinite(numberValue) ? numberValue : fallback;
}

function normalizeBounds(bounds, minSize = 1) {
    const minimum = Math.max(1, readNumber(minSize, 1));
    return {
        width: Math.max(minimum, readNumber(bounds?.width, minimum)),
        height: Math.max(minimum, readNumber(bounds?.height, minimum))
    };
}

export function isFinitePoint(point) {
    return Number.isFinite(Number(point?.x)) && Number.isFinite(Number(point?.y));
}

export function isFiniteRect(rect) {
    return Number.isFinite(Number(rect?.x)) &&
        Number.isFinite(Number(rect?.y)) &&
        Number.isFinite(Number(rect?.width)) &&
        Number.isFinite(Number(rect?.height));
}

export function validateRectangleGeometry(rect, bounds = null, minSize = 1) {
    const minimum = Math.max(1, readNumber(minSize, 1));
    const errors = [];

    if (!isFiniteRect(rect)) {
        errors.push('non-finite');
    }

    const width = Number(rect?.width);
    const height = Number(rect?.height);
    if (!Number.isFinite(width) || width < minimum) {
        errors.push('width');
    }
    if (!Number.isFinite(height) || height < minimum) {
        errors.push('height');
    }

    if (bounds) {
        const normalizedBounds = normalizeBounds(bounds, minimum);
        const x = Number(rect?.x);
        const y = Number(rect?.y);
        if (!Number.isFinite(x) || x < 0 || x + width > normalizedBounds.width) {
            errors.push('x');
        }
        if (!Number.isFinite(y) || y < 0 || y + height > normalizedBounds.height) {
            errors.push('y');
        }
    }

    return {
        valid: errors.length === 0,
        errors
    };
}

export function normalizeAngleDegrees(angle) {
    const value = Number(angle);
    if (!Number.isFinite(value)) {
        return 0;
    }

    return ((value % 360) + 360) % 360;
}

export function computeClockwiseAngleSpanDegrees(startAngle, endAngle, options = {}) {
    if (!Number.isFinite(Number(startAngle)) || !Number.isFinite(Number(endAngle))) {
        return 0;
    }

    const rawSpan = Number(endAngle) - Number(startAngle);
    const normalized = ((rawSpan % 360) + 360) % 360;
    if (normalized === 0 && options.allowFullCircle === true && Math.abs(rawSpan) >= 360) {
        return 360;
    }

    return normalized;
}

export function angleDegreesFromCenter(center, point) {
    if (!isFinitePoint(center) || !isFinitePoint(point)) {
        return 0;
    }

    const radians = Math.atan2(Number(point.y) - Number(center.y), Number(point.x) - Number(center.x));
    return normalizeAngleDegrees(radians * 180 / Math.PI);
}

export function pointFromAngleDegrees(center, radius, angleDegrees) {
    const angle = normalizeAngleDegrees(angleDegrees) * Math.PI / 180;
    return {
        x: Number(center?.x ?? center?.centerX ?? 0) + Number(radius ?? 0) * Math.cos(angle),
        y: Number(center?.y ?? center?.centerY ?? 0) + Number(radius ?? 0) * Math.sin(angle)
    };
}

function maxContainedRadius(centerX, centerY, bounds, minRadius = 1) {
    const normalizedBounds = normalizeBounds(bounds, minRadius);
    const x = clamp(readNumber(centerX, 0), 0, normalizedBounds.width);
    const y = clamp(readNumber(centerY, 0), 0, normalizedBounds.height);
    return Math.max(
        minRadius,
        Math.min(x, y, normalizedBounds.width - x, normalizedBounds.height - y)
    );
}

export function normalizeCircleGeometry(circle, bounds, minRadius = 1) {
    const minimum = Math.max(1, readNumber(minRadius, 1));
    const normalizedBounds = normalizeBounds(bounds, minimum);
    const centerX = clamp(readNumber(circle?.centerX ?? circle?.x, normalizedBounds.width / 2), 0, normalizedBounds.width);
    const centerY = clamp(readNumber(circle?.centerY ?? circle?.y, normalizedBounds.height / 2), 0, normalizedBounds.height);
    const maxRadius = maxContainedRadius(centerX, centerY, normalizedBounds, minimum);
    const radius = clamp(readNumber(circle?.radius, minimum), minimum, maxRadius);

    return {
        kind: 'circle',
        centerX,
        centerY,
        radius
    };
}

export function validateCircleGeometry(circle, bounds = null, minRadius = 1) {
    const minimum = Math.max(1, readNumber(minRadius, 1));
    const errors = [];
    if (!Number.isFinite(Number(circle?.centerX)) ||
        !Number.isFinite(Number(circle?.centerY)) ||
        !Number.isFinite(Number(circle?.radius))) {
        errors.push('non-finite');
    }

    if (!Number.isFinite(Number(circle?.radius)) || Number(circle.radius) < minimum) {
        errors.push('radius');
    }

    if (bounds) {
        const normalized = normalizeCircleGeometry(circle, bounds, minimum);
        if (Math.round(normalized.centerX) !== Math.round(Number(circle?.centerX)) ||
            Math.round(normalized.centerY) !== Math.round(Number(circle?.centerY)) ||
            Math.round(normalized.radius) !== Math.round(Number(circle?.radius))) {
            errors.push('bounds');
        }
    }

    return {
        valid: errors.length === 0,
        errors
    };
}

export function normalizeAnnulusGeometry(annulus, bounds, options = {}) {
    const minimumOuter = Math.max(1, readNumber(options.minRadius, 1));
    const normalizedBounds = normalizeBounds(bounds, minimumOuter);
    const centerX = clamp(readNumber(annulus?.centerX ?? annulus?.x, normalizedBounds.width / 2), 0, normalizedBounds.width);
    const centerY = clamp(readNumber(annulus?.centerY ?? annulus?.y, normalizedBounds.height / 2), 0, normalizedBounds.height);
    const maxRadius = maxContainedRadius(centerX, centerY, normalizedBounds, minimumOuter);
    const outerRadius = clamp(readNumber(annulus?.outerRadius ?? annulus?.radius, minimumOuter), minimumOuter, maxRadius);
    const innerRadius = clamp(readNumber(annulus?.innerRadius, 0), 0, Math.max(0, outerRadius - minimumOuter));
    const startAngle = normalizeAngleDegrees(annulus?.startAngle ?? 0);
    const rawEnd = annulus?.endAngle ?? 360;
    const spanDegrees = computeClockwiseAngleSpanDegrees(annulus?.startAngle ?? 0, rawEnd, { allowFullCircle: true });

    return {
        kind: annulus?.kind === 'arc' || (spanDegrees > 0 && spanDegrees < 360) ? 'arc' : 'annulus',
        centerX,
        centerY,
        innerRadius,
        outerRadius,
        startAngle,
        endAngle: startAngle + (spanDegrees || 360),
        spanDegrees: spanDegrees || 360
    };
}

export function validateAnnulusGeometry(annulus, bounds = null, options = {}) {
    const errors = [];
    const inner = Number(annulus?.innerRadius);
    const outer = Number(annulus?.outerRadius);
    const start = Number(annulus?.startAngle ?? 0);
    const end = Number(annulus?.endAngle ?? 360);

    if (!Number.isFinite(Number(annulus?.centerX)) ||
        !Number.isFinite(Number(annulus?.centerY)) ||
        !Number.isFinite(inner) ||
        !Number.isFinite(outer) ||
        !Number.isFinite(start) ||
        !Number.isFinite(end)) {
        errors.push('non-finite');
    }

    if (!Number.isFinite(inner) || inner < 0) {
        errors.push('innerRadius');
    }
    if (!Number.isFinite(outer) || outer <= inner) {
        errors.push('outerRadius');
    }

    const span = computeClockwiseAngleSpanDegrees(start, end, { allowFullCircle: true });
    if (annulus?.kind === 'arc' && (span <= 0 || span >= 360)) {
        errors.push('arcSpan');
    }

    if (bounds && errors.length === 0) {
        const normalized = normalizeAnnulusGeometry(annulus, bounds, options);
        if (Math.round(normalized.centerX) !== Math.round(Number(annulus.centerX)) ||
            Math.round(normalized.centerY) !== Math.round(Number(annulus.centerY)) ||
            Math.round(normalized.innerRadius) !== Math.round(inner) ||
            Math.round(normalized.outerRadius) !== Math.round(outer)) {
            errors.push('bounds');
        }
    }

    return {
        valid: errors.length === 0,
        errors
    };
}

export function normalizeRectFromPoints(start, end) {
    const x1 = Number(start?.x ?? 0);
    const y1 = Number(start?.y ?? 0);
    const x2 = Number(end?.x ?? 0);
    const y2 = Number(end?.y ?? 0);

    return {
        x: Math.min(x1, x2),
        y: Math.min(y1, y2),
        width: Math.abs(x2 - x1),
        height: Math.abs(y2 - y1)
    };
}

export function clampRectToBounds(rect, bounds, minSize = 1) {
    const normalizedBounds = normalizeBounds(bounds, minSize);
    const minimum = Math.max(1, readNumber(minSize, 1));
    const width = clamp(readNumber(rect?.width, minimum), minimum, normalizedBounds.width);
    const height = clamp(readNumber(rect?.height, minimum), minimum, normalizedBounds.height);
    const x = clamp(readNumber(rect?.x, 0), 0, Math.max(0, normalizedBounds.width - width));
    const y = clamp(readNumber(rect?.y, 0), 0, Math.max(0, normalizedBounds.height - height));

    return {
        x,
        y,
        width,
        height
    };
}

export function translateRect(rect, delta, bounds, minSize = 1) {
    return clampRectToBounds({
        x: Number(rect?.x ?? 0) + Number(delta?.x ?? 0),
        y: Number(rect?.y ?? 0) + Number(delta?.y ?? 0),
        width: Number(rect?.width ?? minSize),
        height: Number(rect?.height ?? minSize)
    }, bounds, minSize);
}

export function resizeRectByHandle(rect, handle, point, bounds, minSize = 1) {
    const left = Number(rect?.x ?? 0);
    const top = Number(rect?.y ?? 0);
    const right = left + Number(rect?.width ?? minSize);
    const bottom = top + Number(rect?.height ?? minSize);

    let nextLeft = left;
    let nextTop = top;
    let nextRight = right;
    let nextBottom = bottom;

    const nextX = Number(point?.x ?? left);
    const nextY = Number(point?.y ?? top);

    if (handle.includes('w')) {
        nextLeft = nextX;
    }
    if (handle.includes('e')) {
        nextRight = nextX;
    }
    if (handle.includes('n')) {
        nextTop = nextY;
    }
    if (handle.includes('s')) {
        nextBottom = nextY;
    }

    if (nextRight - nextLeft < minSize) {
        if (handle.includes('w')) {
            nextLeft = nextRight - minSize;
        } else {
            nextRight = nextLeft + minSize;
        }
    }

    if (nextBottom - nextTop < minSize) {
        if (handle.includes('n')) {
            nextTop = nextBottom - minSize;
        } else {
            nextBottom = nextTop + minSize;
        }
    }

    return clampRectToBounds({
        x: nextLeft,
        y: nextTop,
        width: nextRight - nextLeft,
        height: nextBottom - nextTop
    }, bounds, minSize);
}

export function roundRect(rect) {
    return {
        x: Math.round(Number(rect?.x ?? 0)),
        y: Math.round(Number(rect?.y ?? 0)),
        width: Math.max(1, Math.round(Number(rect?.width ?? 1))),
        height: Math.max(1, Math.round(Number(rect?.height ?? 1)))
    };
}

function normalizeRectParamKeys(paramKeys = DEFAULT_RECT_PARAM_KEYS) {
    return {
        x: paramKeys?.x || DEFAULT_RECT_PARAM_KEYS.x,
        y: paramKeys?.y || DEFAULT_RECT_PARAM_KEYS.y,
        width: paramKeys?.width || DEFAULT_RECT_PARAM_KEYS.width,
        height: paramKeys?.height || DEFAULT_RECT_PARAM_KEYS.height
    };
}

export function rectFromParams(values, paramKeys = DEFAULT_RECT_PARAM_KEYS) {
    const keys = normalizeRectParamKeys(paramKeys);
    return roundRect({
        x: Number(values?.[keys.x] ?? values?.x ?? 0),
        y: Number(values?.[keys.y] ?? values?.y ?? 0),
        width: Number(values?.[keys.width] ?? values?.width ?? 1),
        height: Number(values?.[keys.height] ?? values?.height ?? 1)
    });
}

export function rectToParams(rect, paramKeys = DEFAULT_RECT_PARAM_KEYS) {
    const normalized = roundRect(rect);
    const keys = normalizeRectParamKeys(paramKeys);
    return {
        [keys.x]: normalized.x,
        [keys.y]: normalized.y,
        [keys.width]: normalized.width,
        [keys.height]: normalized.height
    };
}

export function screenToImagePoint(point, viewport) {
    const scale = Number(viewport?.scale ?? 1) || 1;
    const offsetX = Number(viewport?.offset?.x ?? 0);
    const offsetY = Number(viewport?.offset?.y ?? 0);

    return {
        x: (Number(point?.x ?? 0) - offsetX) / scale,
        y: (Number(point?.y ?? 0) - offsetY) / scale
    };
}

export function imageToScreenPoint(point, viewport) {
    const scale = Number(viewport?.scale ?? 1) || 1;
    const offsetX = Number(viewport?.offset?.x ?? 0);
    const offsetY = Number(viewport?.offset?.y ?? 0);

    return {
        x: Number(point?.x ?? 0) * scale + offsetX,
        y: Number(point?.y ?? 0) * scale + offsetY
    };
}

export function imageToScreenRect(rect, viewport) {
    const topLeft = imageToScreenPoint({ x: rect?.x, y: rect?.y }, viewport);
    const scale = Number(viewport?.scale ?? 1) || 1;
    return {
        x: topLeft.x,
        y: topLeft.y,
        width: Number(rect?.width ?? 0) * scale,
        height: Number(rect?.height ?? 0) * scale
    };
}

export function screenToImageRect(rect, viewport) {
    const topLeft = screenToImagePoint({ x: rect?.x, y: rect?.y }, viewport);
    const scale = Number(viewport?.scale ?? 1) || 1;
    return {
        x: topLeft.x,
        y: topLeft.y,
        width: Number(rect?.width ?? 0) / scale,
        height: Number(rect?.height ?? 0) / scale
    };
}

export function getRectHandlePoints(rect) {
    const x = Number(rect?.x ?? 0);
    const y = Number(rect?.y ?? 0);
    const width = Number(rect?.width ?? 0);
    const height = Number(rect?.height ?? 0);
    const centerX = x + width / 2;
    const centerY = y + height / 2;
    const right = x + width;
    const bottom = y + height;

    return {
        nw: { x, y },
        n: { x: centerX, y },
        ne: { x: right, y },
        e: { x: right, y: centerY },
        se: { x: right, y: bottom },
        s: { x: centerX, y: bottom },
        sw: { x, y: bottom },
        w: { x, y: centerY }
    };
}

export function hitTestRectangle(point, rect) {
    if (!isFinitePoint(point) || !isFiniteRect(rect)) {
        return false;
    }

    const x = Number(point.x);
    const y = Number(point.y);
    const left = Number(rect.x);
    const top = Number(rect.y);
    const right = left + Number(rect.width);
    const bottom = top + Number(rect.height);

    return x >= left && x <= right && y >= top && y <= bottom;
}

export function hitTestRectHandle(point, rect, viewport = {}, handleSize = 10) {
    if (!isFinitePoint(point) || !isFiniteRect(rect)) {
        return null;
    }

    const scale = Math.max(0.0001, Math.abs(Number(viewport?.scale ?? 1) || 1));
    const tolerance = Math.max(1, Number(handleSize ?? 10)) / scale;
    const handles = getRectHandlePoints(rect);

    return ROI_HANDLE_NAMES.find(handle => {
        const handlePoint = handles[handle];
        return Math.abs(Number(point.x) - handlePoint.x) <= tolerance &&
            Math.abs(Number(point.y) - handlePoint.y) <= tolerance;
    }) || null;
}

export function hitTestRectangleInteraction(point, rect, viewport = {}, handleSize = 10) {
    const handle = hitTestRectHandle(point, rect, viewport, handleSize);
    if (handle) {
        return {
            target: 'handle',
            handle
        };
    }

    return {
        target: hitTestRectangle(point, rect) ? 'body' : 'none',
        handle: null
    };
}

export function nudgeRect(rect, delta, bounds, minSize = 1) {
    return translateRect(rect, {
        x: Number(delta?.x ?? 0),
        y: Number(delta?.y ?? 0)
    }, bounds, minSize);
}

export function getCircleHandlePoints(circle) {
    const center = {
        x: Number(circle?.centerX ?? circle?.x ?? 0),
        y: Number(circle?.centerY ?? circle?.y ?? 0)
    };
    const radius = Number(circle?.radius ?? 0);
    return {
        center,
        radius: pointFromAngleDegrees(center, radius, 0)
    };
}

export function hitTestCircle(point, circle) {
    if (!isFinitePoint(point)) {
        return false;
    }

    const centerX = Number(circle?.centerX ?? circle?.x);
    const centerY = Number(circle?.centerY ?? circle?.y);
    const radius = Number(circle?.radius);
    if (!Number.isFinite(centerX) || !Number.isFinite(centerY) || !Number.isFinite(radius) || radius <= 0) {
        return false;
    }

    const dx = Number(point.x) - centerX;
    const dy = Number(point.y) - centerY;
    return Math.sqrt(dx * dx + dy * dy) <= radius;
}

export function hitTestCircleHandle(point, circle, viewport = {}, handleSize = 10) {
    if (!isFinitePoint(point)) {
        return null;
    }

    const scale = Math.max(0.0001, Math.abs(Number(viewport?.scale ?? 1) || 1));
    const tolerance = Math.max(1, Number(handleSize ?? 10)) / scale;
    const handles = getCircleHandlePoints(circle);

    return Object.entries(handles).find(([, handlePoint]) =>
        Math.abs(Number(point.x) - handlePoint.x) <= tolerance &&
        Math.abs(Number(point.y) - handlePoint.y) <= tolerance)?.[0] || null;
}

export function translateCircle(circle, delta, bounds, minRadius = 1) {
    return normalizeCircleGeometry({
        ...circle,
        centerX: Number(circle?.centerX ?? circle?.x ?? 0) + Number(delta?.x ?? 0),
        centerY: Number(circle?.centerY ?? circle?.y ?? 0) + Number(delta?.y ?? 0)
    }, bounds, minRadius);
}

export function resizeCircleByHandle(circle, handle, point, bounds, minRadius = 1) {
    if (handle !== 'radius') {
        return normalizeCircleGeometry(circle, bounds, minRadius);
    }

    const centerX = Number(circle?.centerX ?? circle?.x ?? 0);
    const centerY = Number(circle?.centerY ?? circle?.y ?? 0);
    const dx = Number(point?.x ?? centerX) - centerX;
    const dy = Number(point?.y ?? centerY) - centerY;
    return normalizeCircleGeometry({
        ...circle,
        radius: Math.sqrt(dx * dx + dy * dy)
    }, bounds, minRadius);
}

export function getAnnulusHandlePoints(annulus) {
    const center = {
        x: Number(annulus?.centerX ?? annulus?.x ?? 0),
        y: Number(annulus?.centerY ?? annulus?.y ?? 0)
    };
    const innerRadius = Number(annulus?.innerRadius ?? 0);
    const outerRadius = Number(annulus?.outerRadius ?? annulus?.radius ?? 0);
    const startAngle = Number(annulus?.startAngle ?? 0);
    const endAngle = Number(annulus?.endAngle ?? 360);

    return {
        center,
        innerRadius: pointFromAngleDegrees(center, innerRadius, 0),
        outerRadius: pointFromAngleDegrees(center, outerRadius, 0),
        startAngle: pointFromAngleDegrees(center, outerRadius, startAngle),
        endAngle: pointFromAngleDegrees(center, outerRadius, endAngle)
    };
}

export function angleIsWithinClockwiseSpan(angle, startAngle, spanDegrees) {
    const normalizedAngle = normalizeAngleDegrees(angle);
    const normalizedStart = normalizeAngleDegrees(startAngle);
    const delta = computeClockwiseAngleSpanDegrees(normalizedStart, normalizedAngle, { allowFullCircle: false });
    return spanDegrees >= 360 || delta <= spanDegrees;
}

export function hitTestAnnulus(point, annulus) {
    if (!isFinitePoint(point)) {
        return false;
    }

    const centerX = Number(annulus?.centerX ?? annulus?.x);
    const centerY = Number(annulus?.centerY ?? annulus?.y);
    const innerRadius = Number(annulus?.innerRadius ?? 0);
    const outerRadius = Number(annulus?.outerRadius ?? annulus?.radius);
    if (!Number.isFinite(centerX) ||
        !Number.isFinite(centerY) ||
        !Number.isFinite(innerRadius) ||
        !Number.isFinite(outerRadius) ||
        innerRadius < 0 ||
        outerRadius <= innerRadius) {
        return false;
    }

    const dx = Number(point.x) - centerX;
    const dy = Number(point.y) - centerY;
    const distance = Math.sqrt(dx * dx + dy * dy);
    const span = Number(annulus?.spanDegrees ?? computeClockwiseAngleSpanDegrees(annulus?.startAngle ?? 0, annulus?.endAngle ?? 360, { allowFullCircle: true }));
    const angle = angleDegreesFromCenter({ x: centerX, y: centerY }, point);

    return distance >= innerRadius &&
        distance <= outerRadius &&
        angleIsWithinClockwiseSpan(angle, annulus?.startAngle ?? 0, span || 360);
}

export function hitTestAnnulusHandle(point, annulus, viewport = {}, handleSize = 10) {
    if (!isFinitePoint(point)) {
        return null;
    }

    const scale = Math.max(0.0001, Math.abs(Number(viewport?.scale ?? 1) || 1));
    const tolerance = Math.max(1, Number(handleSize ?? 10)) / scale;
    const handles = getAnnulusHandlePoints(annulus);

    return Object.entries(handles).find(([, handlePoint]) =>
        Math.abs(Number(point.x) - handlePoint.x) <= tolerance &&
        Math.abs(Number(point.y) - handlePoint.y) <= tolerance)?.[0] || null;
}

export function translateAnnulus(annulus, delta, bounds, options = {}) {
    return normalizeAnnulusGeometry({
        ...annulus,
        centerX: Number(annulus?.centerX ?? annulus?.x ?? 0) + Number(delta?.x ?? 0),
        centerY: Number(annulus?.centerY ?? annulus?.y ?? 0) + Number(delta?.y ?? 0)
    }, bounds, options);
}

export function resizeAnnulusByHandle(annulus, handle, point, bounds, options = {}) {
    const center = {
        x: Number(annulus?.centerX ?? annulus?.x ?? 0),
        y: Number(annulus?.centerY ?? annulus?.y ?? 0)
    };
    const dx = Number(point?.x ?? center.x) - center.x;
    const dy = Number(point?.y ?? center.y) - center.y;
    const radius = Math.sqrt(dx * dx + dy * dy);
    const next = { ...annulus };

    if (handle === 'innerRadius') {
        next.innerRadius = radius;
    } else if (handle === 'outerRadius') {
        next.outerRadius = radius;
    } else if (handle === 'startAngle') {
        next.startAngle = angleDegreesFromCenter(center, point);
    } else if (handle === 'endAngle') {
        next.endAngle = angleDegreesFromCenter(center, point);
    }

    return normalizeAnnulusGeometry(next, bounds, options);
}

function sameRect(left, right) {
    return Math.round(Number(left?.x ?? 0)) === Math.round(Number(right?.x ?? 0)) &&
        Math.round(Number(left?.y ?? 0)) === Math.round(Number(right?.y ?? 0)) &&
        Math.round(Number(left?.width ?? 0)) === Math.round(Number(right?.width ?? 0)) &&
        Math.round(Number(left?.height ?? 0)) === Math.round(Number(right?.height ?? 0));
}

function normalizeDraftRect(rect, bounds, minSize) {
    return roundRect(clampRectToBounds(rect, bounds, minSize));
}

function copyHistory(history, limit) {
    return history.slice(Math.max(0, history.length - limit)).map(item => ({ ...item }));
}

export function createRectangleDraftSession(rect, bounds, options = {}) {
    const minSize = Math.max(1, readNumber(options.minSize, 1));
    const historyLimit = Math.max(1, Math.floor(readNumber(options.historyLimit, RECT_DRAFT_HISTORY_LIMIT)));
    const normalizedBounds = normalizeBounds(bounds, minSize);
    const initial = normalizeDraftRect(rect, normalizedBounds, minSize);

    return {
        shape: 'rectangle',
        bounds: normalizedBounds,
        minSize,
        historyLimit,
        initial: { ...initial },
        current: { ...initial },
        past: [],
        future: []
    };
}

export function setRectangleDraftCurrent(session, rect) {
    if (!session) {
        return session;
    }

    const current = normalizeDraftRect(rect, session.bounds, session.minSize);
    return {
        ...session,
        current
    };
}

export function commitRectangleDraft(session, rect = session?.current, options = {}) {
    if (!session) {
        return session;
    }

    const current = normalizeDraftRect(rect, session.bounds, session.minSize);
    const previous = options.previousRect
        ? normalizeDraftRect(options.previousRect, session.bounds, session.minSize)
        : session.current;
    const lastPast = session.past.length > 0 ? session.past[session.past.length - 1] : null;

    if (sameRect(previous, current) && sameRect(lastPast, current)) {
        return {
            ...session,
            current
        };
    }

    const past = sameRect(previous, current)
        ? session.past
        : [...session.past, { ...previous }];

    return {
        ...session,
        current,
        past: copyHistory(past, session.historyLimit),
        future: []
    };
}

export function cancelRectangleDraft(session) {
    if (!session) {
        return session;
    }

    return {
        ...session,
        current: { ...session.initial },
        past: [],
        future: []
    };
}

export function undoRectangleDraft(session) {
    if (!session || session.past.length === 0) {
        return session;
    }

    const previous = session.past[session.past.length - 1];
    return {
        ...session,
        current: { ...previous },
        past: session.past.slice(0, -1),
        future: [{ ...session.current }, ...session.future].slice(0, session.historyLimit)
    };
}

export function redoRectangleDraft(session) {
    if (!session || session.future.length === 0) {
        return session;
    }

    const next = session.future[0];
    return {
        ...session,
        current: { ...next },
        past: copyHistory([...session.past, { ...session.current }], session.historyLimit),
        future: session.future.slice(1)
    };
}

