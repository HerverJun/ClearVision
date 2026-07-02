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

export const RECT_DRAFT_HISTORY_LIMIT = 50;

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

