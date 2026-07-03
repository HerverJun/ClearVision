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

export const CIRCLE_SEARCH_V2_PARAM_KEYS = {
    searchCenterMode: 'SearchCenterMode',
    centerX: 'SearchCenterX',
    centerY: 'SearchCenterY',
    minRadius: 'MinRadius',
    nominalRadius: 'NominalRadius',
    maxRadius: 'MaxRadius'
};

export const POLYGON_PARAM_KEYS = {
    points: 'PolygonPoints'
};

export const POINT_PAIRS_PARAM_KEYS = {
    pointPairs: 'PointPairs'
};

export const RECT_DRAFT_HISTORY_LIMIT = 50;
export const GEOMETRY_ANGLE_UNITS = 'degrees';
export const GEOMETRY_ANGLE_ZERO_DIRECTION = '+x';
export const GEOMETRY_ANGLE_DIRECTION = 'clockwise-image-y-down';
export const POLYGON_MIN_POINTS = 3;
export const POLYGON_MIN_EDGE_LENGTH = 1;

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

function roundCoordinate(value, digits = 3) {
    const numberValue = Number(value);
    if (!Number.isFinite(numberValue)) {
        return 0;
    }

    const scale = 10 ** digits;
    return Math.round(numberValue * scale) / scale;
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

function readPointLike(value) {
    if (Array.isArray(value) && value.length >= 2) {
        const x = Number(value[0]);
        const y = Number(value[1]);
        return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
    }

    const x = Number(value?.x ?? value?.X ?? value?.imageX ?? value?.ImageX ?? value?.pixelX ?? value?.PixelX);
    const y = Number(value?.y ?? value?.Y ?? value?.imageY ?? value?.ImageY ?? value?.pixelY ?? value?.PixelY);
    return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
}

function parseJsonArray(raw) {
    if (Array.isArray(raw)) {
        return raw;
    }

    if (typeof raw !== 'string') {
        return null;
    }

    try {
        const parsed = JSON.parse(raw);
        return Array.isArray(parsed) ? parsed : null;
    } catch {
        return null;
    }
}

function getPointSequencePoints(geometry) {
    return Array.isArray(geometry?.points)
        ? geometry.points
            .map(point => {
                const imagePoint = readPointLike(point);
                if (!imagePoint) {
                    return null;
                }

                const rawWorldX = point?.worldX ?? point?.WorldX ?? point?.physicalX ?? point?.PhysicalX;
                const rawWorldY = point?.worldY ?? point?.WorldY ?? point?.physicalY ?? point?.PhysicalY;
                const worldX = Number(rawWorldX);
                const worldY = Number(rawWorldY);
                if (!Number.isFinite(worldX) || !Number.isFinite(worldY)) {
                    return null;
                }

                return {
                    x: imagePoint.x,
                    y: imagePoint.y,
                    worldX,
                    worldY,
                    enabled: point?.enabled ?? point?.Enabled ?? true
                };
            })
            .filter(Boolean)
        : [];
}

function isPointInBounds(point, bounds) {
    if (!bounds) {
        return true;
    }

    const normalizedBounds = normalizeBounds(bounds);
    return isFinitePoint(point) &&
        Number(point.x) >= 0 &&
        Number(point.y) >= 0 &&
        Number(point.x) <= normalizedBounds.width &&
        Number(point.y) <= normalizedBounds.height;
}

function squaredDistance(left, right) {
    const dx = Number(left?.x ?? 0) - Number(right?.x ?? 0);
    const dy = Number(left?.y ?? 0) - Number(right?.y ?? 0);
    return dx * dx + dy * dy;
}

function distancePointToSegment(point, start, end) {
    const px = Number(point?.x ?? 0);
    const py = Number(point?.y ?? 0);
    const ax = Number(start?.x ?? 0);
    const ay = Number(start?.y ?? 0);
    const bx = Number(end?.x ?? 0);
    const by = Number(end?.y ?? 0);
    const dx = bx - ax;
    const dy = by - ay;
    const lengthSquared = dx * dx + dy * dy;

    if (lengthSquared <= Number.EPSILON) {
        return Math.sqrt((px - ax) ** 2 + (py - ay) ** 2);
    }

    const t = clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
    const closestX = ax + t * dx;
    const closestY = ay + t * dy;
    return Math.sqrt((px - closestX) ** 2 + (py - closestY) ** 2);
}

function orientation(a, b, c) {
    const value = (Number(b.y) - Number(a.y)) * (Number(c.x) - Number(b.x)) -
        (Number(b.x) - Number(a.x)) * (Number(c.y) - Number(b.y));
    if (Math.abs(value) <= 1e-9) {
        return 0;
    }
    return value > 0 ? 1 : 2;
}

function onSegment(a, b, c) {
    return Number(b.x) <= Math.max(Number(a.x), Number(c.x)) + 1e-9 &&
        Number(b.x) >= Math.min(Number(a.x), Number(c.x)) - 1e-9 &&
        Number(b.y) <= Math.max(Number(a.y), Number(c.y)) + 1e-9 &&
        Number(b.y) >= Math.min(Number(a.y), Number(c.y)) - 1e-9;
}

function segmentsIntersect(a1, a2, b1, b2) {
    const o1 = orientation(a1, a2, b1);
    const o2 = orientation(a1, a2, b2);
    const o3 = orientation(b1, b2, a1);
    const o4 = orientation(b1, b2, a2);

    if (o1 !== o2 && o3 !== o4) {
        return true;
    }
    if (o1 === 0 && onSegment(a1, b1, a2)) {
        return true;
    }
    if (o2 === 0 && onSegment(a1, b2, a2)) {
        return true;
    }
    if (o3 === 0 && onSegment(b1, a1, b2)) {
        return true;
    }
    return o4 === 0 && onSegment(b1, a2, b2);
}

function polygonArea(points) {
    if (!Array.isArray(points) || points.length < 3) {
        return 0;
    }

    let area = 0;
    for (let index = 0; index < points.length; index += 1) {
        const current = points[index];
        const next = points[(index + 1) % points.length];
        area += Number(current.x) * Number(next.y) - Number(next.x) * Number(current.y);
    }
    return area / 2;
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

export function computeRawAngleSpanDegrees(startAngle, endAngle) {
    if (!Number.isFinite(Number(startAngle)) || !Number.isFinite(Number(endAngle))) {
        return 0;
    }

    return Number(endAngle) - Number(startAngle);
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
    if (!bounds) {
        return {
            kind: 'circle',
            centerX: readNumber(circle?.centerX ?? circle?.x, 0),
            centerY: readNumber(circle?.centerY ?? circle?.y, 0),
            radius: Math.max(minimum, readNumber(circle?.radius, minimum))
        };
    }

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

    if (bounds && errors.length === 0) {
        const normalizedBounds = normalizeBounds(bounds, minimum);
        const centerX = Number(circle?.centerX);
        const centerY = Number(circle?.centerY);
        const radius = Number(circle?.radius);
        const intersectsImage = centerX + radius > 0 &&
            centerY + radius > 0 &&
            centerX - radius < normalizedBounds.width &&
            centerY - radius < normalizedBounds.height;
        if (!intersectsImage) {
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
    if (!bounds) {
        const startAngle = readNumber(annulus?.startAngle, 0);
        const endAngle = readNumber(annulus?.endAngle, 360);
        const spanDegrees = computeRawAngleSpanDegrees(startAngle, endAngle);
        return {
            kind: annulus?.kind === 'arc' || Math.abs(spanDegrees) < 360 ? 'arc' : 'annulus',
            centerX: readNumber(annulus?.centerX ?? annulus?.x, 0),
            centerY: readNumber(annulus?.centerY ?? annulus?.y, 0),
            innerRadius: Math.max(0, readNumber(annulus?.innerRadius, 0)),
            outerRadius: Math.max(minimumOuter, readNumber(annulus?.outerRadius ?? annulus?.radius, minimumOuter)),
            startAngle,
            endAngle,
            spanDegrees
        };
    }

    const normalizedBounds = normalizeBounds(bounds, minimumOuter);
    const centerX = readNumber(annulus?.centerX ?? annulus?.x, normalizedBounds.width / 2);
    const centerY = readNumber(annulus?.centerY ?? annulus?.y, normalizedBounds.height / 2);
    const outerRadius = Math.max(minimumOuter, readNumber(annulus?.outerRadius ?? annulus?.radius, minimumOuter));
    const innerRadius = clamp(readNumber(annulus?.innerRadius, 0), 0, Math.max(0, outerRadius - minimumOuter));
    const startAngle = readNumber(annulus?.startAngle, 0);
    const rawEnd = readNumber(annulus?.endAngle, 360);
    const spanDegrees = computeRawAngleSpanDegrees(startAngle, rawEnd);

    return {
        kind: annulus?.kind === 'arc' || Math.abs(spanDegrees) < 360 ? 'arc' : 'annulus',
        centerX,
        centerY,
        innerRadius,
        outerRadius,
        startAngle,
        endAngle: rawEnd,
        spanDegrees
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

    const span = computeRawAngleSpanDegrees(start, end);
    if (annulus?.kind === 'arc' && span === 0) {
        errors.push('arcSpan');
    }

    if (bounds && errors.length === 0) {
        const normalizedBounds = normalizeBounds(bounds, Math.max(1, readNumber(options.minRadius, 1)));
        const intersectsImage = Number(annulus.centerX) + outer > 0 &&
            Number(annulus.centerY) + outer > 0 &&
            Number(annulus.centerX) - outer < normalizedBounds.width &&
            Number(annulus.centerY) - outer < normalizedBounds.height;
        if (!intersectsImage) {
            errors.push('bounds');
        }
    }

    return {
        valid: errors.length === 0,
        errors
    };
}

export function normalizeCircleSearchV2Geometry(geometry, bounds, options = {}) {
    const normalizedBounds = bounds ? normalizeBounds(bounds) : null;
    const minimum = Math.max(1, readNumber(options.minRadius, 1));
    const centerX = normalizedBounds
        ? clamp(readNumber(geometry?.centerX ?? geometry?.x, normalizedBounds.width / 2), 0, normalizedBounds.width)
        : readNumber(geometry?.centerX ?? geometry?.x, 0);
    const centerY = normalizedBounds
        ? clamp(readNumber(geometry?.centerY ?? geometry?.y, normalizedBounds.height / 2), 0, normalizedBounds.height)
        : readNumber(geometry?.centerY ?? geometry?.y, 0);
    const minRadius = Math.max(minimum, readNumber(geometry?.minRadius ?? geometry?.innerRadius, minimum));
    const nominalRadius = Math.max(minRadius, readNumber(geometry?.nominalRadius, minRadius));
    const maxRadius = Math.max(nominalRadius, readNumber(geometry?.maxRadius ?? geometry?.outerRadius ?? geometry?.radius, nominalRadius));

    return {
        kind: 'circleSearchV2',
        searchCenterMode: String(geometry?.searchCenterMode || 'Explicit'),
        centerX: roundCoordinate(centerX),
        centerY: roundCoordinate(centerY),
        minRadius: roundCoordinate(minRadius),
        nominalRadius: roundCoordinate(nominalRadius),
        maxRadius: roundCoordinate(maxRadius)
    };
}

export function validateCircleSearchV2Geometry(geometry, bounds = null, options = {}) {
    const minimum = Math.max(1, readNumber(options.minRadius, 1));
    const centerX = Number(geometry?.centerX ?? geometry?.x);
    const centerY = Number(geometry?.centerY ?? geometry?.y);
    const minRadius = Number(geometry?.minRadius ?? geometry?.innerRadius);
    const nominalRadius = Number(geometry?.nominalRadius);
    const maxRadius = Number(geometry?.maxRadius ?? geometry?.outerRadius ?? geometry?.radius);

    if (![centerX, centerY, minRadius, nominalRadius, maxRadius].every(Number.isFinite)) {
        return { valid: false, reason: 'Circle Search V2 geometry values must be finite.' };
    }

    if (minRadius < minimum || minRadius > nominalRadius || nominalRadius > maxRadius) {
        return { valid: false, reason: 'Circle Search V2 requires MinRadius <= NominalRadius <= MaxRadius.' };
    }

    if (bounds && !isPointInBounds({ x: centerX, y: centerY }, bounds)) {
        return { valid: false, reason: 'Circle Search V2 center must stay inside the image bounds.' };
    }

    return { valid: true };
}

export function parsePolygonPoints(value) {
    const rawPoints = parseJsonArray(value);
    if (!rawPoints) {
        return null;
    }

    const points = rawPoints
        .map(item => readPointLike(item))
        .filter(Boolean);

    return {
        kind: 'polygon',
        points
    };
}

export function polygonToParamsJson(polygon) {
    const points = Array.isArray(polygon?.points) ? polygon.points : [];
    return JSON.stringify(points.map(point => [
        Math.round(Number(point.x ?? 0)),
        Math.round(Number(point.y ?? 0))
    ]));
}

export function normalizePolygonGeometry(polygon) {
    const points = Array.isArray(polygon?.points)
        ? polygon.points
            .map(item => readPointLike(item))
            .filter(Boolean)
            .map(point => ({
                x: roundCoordinate(point.x),
                y: roundCoordinate(point.y)
            }))
        : [];

    return {
        kind: 'polygon',
        points
    };
}

export function validatePolygonGeometry(polygon, bounds = null, options = {}) {
    const minPoints = Math.max(POLYGON_MIN_POINTS, Math.floor(readNumber(options.minPoints, POLYGON_MIN_POINTS)));
    const minEdgeLength = Math.max(0, readNumber(options.minEdgeLength, POLYGON_MIN_EDGE_LENGTH));
    const minEdgeLengthSquared = minEdgeLength * minEdgeLength;
    const rawPoints = Array.isArray(polygon?.points) ? polygon.points : [];
    const points = normalizePolygonGeometry(polygon).points;
    const errors = [];

    if (!Array.isArray(polygon?.points)) {
        errors.push('points');
    }

    if (rawPoints.length !== points.length || rawPoints.some(point => readPointLike(point) === null)) {
        errors.push('non-finite');
    }

    if (points.length < minPoints) {
        errors.push('pointCount');
    }

    if (points.some(point => !isFinitePoint(point))) {
        errors.push('non-finite');
    }

    if (bounds && points.some(point => !isPointInBounds(point, bounds))) {
        errors.push('bounds');
    }

    for (let index = 0; index < points.length; index += 1) {
        for (let other = index + 1; other < points.length; other += 1) {
            if (squaredDistance(points[index], points[other]) <= minEdgeLengthSquared) {
                errors.push('duplicatePoint');
                index = points.length;
                break;
            }
        }
    }

    for (let index = 0; index < points.length; index += 1) {
        const next = points[(index + 1) % points.length];
        if (squaredDistance(points[index], next) <= minEdgeLengthSquared) {
            errors.push('nearZeroEdge');
            break;
        }
    }

    if (Math.abs(polygonArea(points)) <= Math.max(1e-6, minEdgeLengthSquared * 0.5)) {
        errors.push('area');
    }

    for (let index = 0; index < points.length; index += 1) {
        const a1 = points[index];
        const a2 = points[(index + 1) % points.length];
        for (let other = index + 1; other < points.length; other += 1) {
            if (Math.abs(index - other) <= 1 || (index === 0 && other === points.length - 1)) {
                continue;
            }

            const b1 = points[other];
            const b2 = points[(other + 1) % points.length];
            if (segmentsIntersect(a1, a2, b1, b2)) {
                errors.push('selfIntersection');
                index = points.length;
                break;
            }
        }
    }

    return {
        valid: errors.length === 0,
        errors: [...new Set(errors)]
    };
}

export function getPolygonHandlePoints(polygon) {
    return normalizePolygonGeometry(polygon).points.reduce((handles, point, index) => {
        handles[`vertex:${index}`] = point;
        return handles;
    }, {});
}

export function hitTestPolygonVertex(point, polygon, viewport = {}, handleSize = 10) {
    if (!isFinitePoint(point)) {
        return null;
    }

    const scale = Math.max(0.0001, Math.abs(Number(viewport?.scale ?? 1) || 1));
    const tolerance = Math.max(1, Number(handleSize ?? 10)) / scale;
    const points = normalizePolygonGeometry(polygon).points;

    for (let index = 0; index < points.length; index += 1) {
        if (Math.sqrt(squaredDistance(point, points[index])) <= tolerance) {
            return `vertex:${index}`;
        }
    }

    return null;
}

export function hitTestPolygonEdge(point, polygon, viewport = {}, handleSize = 10) {
    if (!isFinitePoint(point)) {
        return null;
    }

    const scale = Math.max(0.0001, Math.abs(Number(viewport?.scale ?? 1) || 1));
    const tolerance = Math.max(1, Number(handleSize ?? 10)) / scale;
    const points = normalizePolygonGeometry(polygon).points;
    if (points.length < 2) {
        return null;
    }

    for (let index = 0; index < points.length; index += 1) {
        const nextIndex = (index + 1) % points.length;
        if (distancePointToSegment(point, points[index], points[nextIndex]) <= tolerance) {
            return nextIndex;
        }
    }

    return null;
}

export function hitTestPolygon(point, polygon) {
    if (!isFinitePoint(point)) {
        return false;
    }

    const points = normalizePolygonGeometry(polygon).points;
    if (points.length < 3) {
        return false;
    }

    let inside = false;
    const x = Number(point.x);
    const y = Number(point.y);
    for (let index = 0, previous = points.length - 1; index < points.length; previous = index, index += 1) {
        const current = points[index];
        const previousPoint = points[previous];
        const intersects = ((Number(current.y) > y) !== (Number(previousPoint.y) > y)) &&
            x < (Number(previousPoint.x) - Number(current.x)) * (y - Number(current.y)) /
                (Number(previousPoint.y) - Number(current.y)) + Number(current.x);
        if (intersects) {
            inside = !inside;
        }
    }

    return inside;
}

export function translatePolygon(polygon, delta, bounds = null) {
    const points = normalizePolygonGeometry(polygon).points.map(point => ({
        x: roundCoordinate(point.x + Number(delta?.x ?? 0)),
        y: roundCoordinate(point.y + Number(delta?.y ?? 0))
    }));
    const next = { kind: 'polygon', points };
    return validatePolygonGeometry(next, bounds).valid ? next : normalizePolygonGeometry(polygon);
}

export function movePolygonVertex(polygon, vertexIndex, point, bounds = null) {
    const points = normalizePolygonGeometry(polygon).points;
    const index = Number(vertexIndex);
    if (!Number.isInteger(index) || index < 0 || index >= points.length || !isFinitePoint(point)) {
        return normalizePolygonGeometry(polygon);
    }

    const next = {
        kind: 'polygon',
        points: points.map((existing, currentIndex) =>
            currentIndex === index
                ? { x: roundCoordinate(point.x), y: roundCoordinate(point.y) }
                : existing)
    };
    return validatePolygonGeometry(next, bounds).valid ? next : normalizePolygonGeometry(polygon);
}

export function insertPolygonVertex(polygon, insertIndex, point, bounds = null) {
    const points = normalizePolygonGeometry(polygon).points;
    const index = Number(insertIndex);
    if (!Number.isInteger(index) || index < 0 || index > points.length || !isFinitePoint(point)) {
        return normalizePolygonGeometry(polygon);
    }

    const next = {
        kind: 'polygon',
        points: [
            ...points.slice(0, index),
            { x: roundCoordinate(point.x), y: roundCoordinate(point.y) },
            ...points.slice(index)
        ]
    };
    return validatePolygonGeometry(next, bounds).valid ? next : normalizePolygonGeometry(polygon);
}

export function deletePolygonVertex(polygon, vertexIndex, bounds = null) {
    const points = normalizePolygonGeometry(polygon).points;
    const index = Number(vertexIndex);
    if (!Number.isInteger(index) || index < 0 || index >= points.length || points.length <= POLYGON_MIN_POINTS) {
        return normalizePolygonGeometry(polygon);
    }

    const next = {
        kind: 'polygon',
        points: points.filter((_, currentIndex) => currentIndex !== index)
    };
    return validatePolygonGeometry(next, bounds).valid ? next : normalizePolygonGeometry(polygon);
}

export function parsePointPairs(value) {
    const rawPairs = parseJsonArray(value);
    if (!rawPairs) {
        return null;
    }

    const points = rawPairs
        .map(item => {
            const imagePoint = readPointLike(item?.ImagePoint ?? item?.imagePoint ?? item);
            const worldPoint = readPointLike(item?.WorldPoint ?? item?.worldPoint ?? {
                x: item?.WorldX ?? item?.worldX ?? item?.PhysicalX ?? item?.physicalX,
                y: item?.WorldY ?? item?.worldY ?? item?.PhysicalY ?? item?.physicalY
            });
            if (!imagePoint || !worldPoint) {
                return null;
            }

            return {
                x: imagePoint.x,
                y: imagePoint.y,
                worldX: worldPoint.x,
                worldY: worldPoint.y,
                enabled: item?.Enabled ?? item?.enabled ?? true
            };
        })
        .filter(Boolean);

    return {
        kind: 'pointSequence',
        points
    };
}

export function pointPairsToParamsJson(sequence) {
    const points = getPointSequencePoints(sequence);
    return JSON.stringify(points.map(point => ({
        ImageX: roundCoordinate(point.x),
        ImageY: roundCoordinate(point.y),
        WorldX: roundCoordinate(point.worldX),
        WorldY: roundCoordinate(point.worldY),
        Enabled: point.enabled !== false
    })));
}

export function normalizePointSequenceGeometry(sequence) {
    return {
        kind: 'pointSequence',
        points: getPointSequencePoints(sequence).map(point => ({
            x: roundCoordinate(point.x),
            y: roundCoordinate(point.y),
            worldX: roundCoordinate(point.worldX),
            worldY: roundCoordinate(point.worldY),
            enabled: point.enabled !== false
        }))
    };
}

export function validatePointSequenceGeometry(sequence, bounds = null) {
    const rawPoints = Array.isArray(sequence?.points) ? sequence.points : [];
    const points = normalizePointSequenceGeometry(sequence).points;
    const errors = [];

    if (!Array.isArray(sequence?.points)) {
        errors.push('points');
    }

    if (rawPoints.length !== points.length ||
        rawPoints.some(point => readPointLike(point) === null) ||
        points.some(point => !isFinitePoint(point) ||
        !Number.isFinite(Number(point.worldX)) ||
        !Number.isFinite(Number(point.worldY)))) {
        errors.push('non-finite');
    }

    if (bounds && points.some(point => !isPointInBounds(point, bounds))) {
        errors.push('bounds');
    }

    return {
        valid: errors.length === 0,
        errors: [...new Set(errors)]
    };
}

export function getPointSequenceHandlePoints(sequence) {
    return normalizePointSequenceGeometry(sequence).points.reduce((handles, point, index) => {
        handles[`point:${index}`] = point;
        return handles;
    }, {});
}

export function hitTestPointSequencePoint(point, sequence, viewport = {}, handleSize = 10) {
    if (!isFinitePoint(point)) {
        return null;
    }

    const scale = Math.max(0.0001, Math.abs(Number(viewport?.scale ?? 1) || 1));
    const tolerance = Math.max(1, Number(handleSize ?? 10)) / scale;
    const points = normalizePointSequenceGeometry(sequence).points;

    for (let index = 0; index < points.length; index += 1) {
        if (Math.sqrt(squaredDistance(point, points[index])) <= tolerance) {
            return `point:${index}`;
        }
    }

    return null;
}

export function translatePointSequence(sequence, delta, bounds = null) {
    const points = normalizePointSequenceGeometry(sequence).points.map(point => ({
        ...point,
        x: roundCoordinate(point.x + Number(delta?.x ?? 0)),
        y: roundCoordinate(point.y + Number(delta?.y ?? 0))
    }));
    const next = { kind: 'pointSequence', points };
    return validatePointSequenceGeometry(next, bounds).valid ? next : normalizePointSequenceGeometry(sequence);
}

export function movePointSequencePoint(sequence, pointIndex, point, bounds = null) {
    const points = normalizePointSequenceGeometry(sequence).points;
    const index = Number(pointIndex);
    if (!Number.isInteger(index) || index < 0 || index >= points.length || !isFinitePoint(point)) {
        return normalizePointSequenceGeometry(sequence);
    }

    const next = {
        kind: 'pointSequence',
        points: points.map((existing, currentIndex) =>
            currentIndex === index
                ? { ...existing, x: roundCoordinate(point.x), y: roundCoordinate(point.y) }
                : existing)
    };
    return validatePointSequenceGeometry(next, bounds).valid ? next : normalizePointSequenceGeometry(sequence);
}

export function appendPointSequencePoint(sequence, point, bounds = null) {
    return normalizePointSequenceGeometry(sequence);
}

export function deletePointSequencePoint(sequence, pointIndex) {
    const points = normalizePointSequenceGeometry(sequence).points;
    const index = Number(pointIndex);
    if (!Number.isInteger(index) || index < 0 || index >= points.length) {
        return normalizePointSequenceGeometry(sequence);
    }

    return {
        kind: 'pointSequence',
        points: points.filter((_, currentIndex) => currentIndex !== index)
    };
}

export function togglePointSequencePointEnabled(sequence, pointIndex) {
    const points = normalizePointSequenceGeometry(sequence).points;
    const index = Number(pointIndex);
    if (!Number.isInteger(index) || index < 0 || index >= points.length) {
        return normalizePointSequenceGeometry(sequence);
    }

    return {
        kind: 'pointSequence',
        points: points.map((point, currentIndex) =>
            currentIndex === index ? { ...point, enabled: point.enabled === false } : point)
    };
}

export function reorderPointSequencePoint(sequence, pointIndex, direction) {
    const points = normalizePointSequenceGeometry(sequence).points;
    const index = Number(pointIndex);
    const offset = Number(direction) < 0 ? -1 : 1;
    const nextIndex = index + offset;
    if (!Number.isInteger(index) || index < 0 || index >= points.length || nextIndex < 0 || nextIndex >= points.length) {
        return normalizePointSequenceGeometry(sequence);
    }

    const nextPoints = [...points];
    const [moved] = nextPoints.splice(index, 1);
    nextPoints.splice(nextIndex, 0, moved);
    return {
        kind: 'pointSequence',
        points: nextPoints
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

export function getCircleSearchV2HandlePoints(geometry) {
    const center = {
        x: Number(geometry?.centerX ?? geometry?.x ?? 0),
        y: Number(geometry?.centerY ?? geometry?.y ?? 0)
    };
    return {
        center,
        minRadius: pointFromAngleDegrees(center, Number(geometry?.minRadius ?? geometry?.innerRadius ?? 0), 180),
        nominalRadius: pointFromAngleDegrees(center, Number(geometry?.nominalRadius ?? 0), 0),
        maxRadius: pointFromAngleDegrees(center, Number(geometry?.maxRadius ?? geometry?.outerRadius ?? geometry?.radius ?? 0), 0)
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

export function hitTestCircleSearchV2(point, geometry) {
    if (!isFinitePoint(point)) {
        return false;
    }

    const centerX = Number(geometry?.centerX ?? geometry?.x);
    const centerY = Number(geometry?.centerY ?? geometry?.y);
    const minRadius = Number(geometry?.minRadius ?? geometry?.innerRadius);
    const maxRadius = Number(geometry?.maxRadius ?? geometry?.outerRadius ?? geometry?.radius);
    if (![centerX, centerY, minRadius, maxRadius].every(Number.isFinite) || minRadius < 0 || maxRadius <= minRadius) {
        return false;
    }

    const dx = Number(point.x) - centerX;
    const dy = Number(point.y) - centerY;
    const distance = Math.sqrt(dx * dx + dy * dy);
    return distance >= minRadius && distance <= maxRadius;
}

export function hitTestCircleSearchV2Handle(point, geometry, viewport = {}, handleSize = 10) {
    if (!isFinitePoint(point)) {
        return null;
    }

    const scale = Math.max(0.0001, Math.abs(Number(viewport?.scale ?? 1) || 1));
    const tolerance = Math.max(1, Number(handleSize ?? 10)) / scale;
    const handles = getCircleSearchV2HandlePoints(geometry);

    return Object.entries(handles).find(([, handlePoint]) =>
        Math.abs(Number(point.x) - handlePoint.x) <= tolerance &&
        Math.abs(Number(point.y) - handlePoint.y) <= tolerance)?.[0] || null;
}

export function translateCircleSearchV2(geometry, delta, bounds, options = {}) {
    return normalizeCircleSearchV2Geometry({
        ...geometry,
        centerX: Number(geometry?.centerX ?? geometry?.x ?? 0) + Number(delta?.x ?? 0),
        centerY: Number(geometry?.centerY ?? geometry?.y ?? 0) + Number(delta?.y ?? 0),
        searchCenterMode: 'Explicit'
    }, bounds, options);
}

export function resizeCircleSearchV2ByHandle(geometry, handle, point, bounds, options = {}) {
    const center = {
        x: Number(geometry?.centerX ?? geometry?.x ?? 0),
        y: Number(geometry?.centerY ?? geometry?.y ?? 0)
    };
    const dx = Number(point?.x ?? center.x) - center.x;
    const dy = Number(point?.y ?? center.y) - center.y;
    const radius = Math.sqrt(dx * dx + dy * dy);
    const next = { ...geometry };

    if (handle === 'minRadius') {
        next.minRadius = Math.min(radius, Number(geometry?.nominalRadius ?? radius));
    } else if (handle === 'nominalRadius') {
        next.nominalRadius = clamp(
            radius,
            Number(geometry?.minRadius ?? geometry?.innerRadius ?? 1),
            Number(geometry?.maxRadius ?? geometry?.outerRadius ?? geometry?.radius ?? radius));
    } else if (handle === 'maxRadius') {
        next.maxRadius = Math.max(radius, Number(geometry?.nominalRadius ?? radius));
    }

    return normalizeCircleSearchV2Geometry(next, bounds, options);
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

