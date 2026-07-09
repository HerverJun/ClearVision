export const PIXEL_PROBE_DEFAULT_MESSAGE = '移动鼠标查看像素坐标和值';
export const PIXEL_PROBE_OUTSIDE_MESSAGE = '光标不在图像内';
export const PIXEL_PROBE_NO_IMAGE_MESSAGE = '暂无图像，无法显示像素值';
export const PIXEL_PROBE_LOADING_MESSAGE = '图像未加载，无法显示像素值';
export const PIXEL_PROBE_UNREADABLE_MESSAGE = '无法读取像素值';
export const PIXEL_PROBE_NO_WORLD_MESSAGE = '未配置标定/暂无世界坐标';

function isFinitePositive(value) {
    return Number.isFinite(value) && value > 0;
}

function normalizeRect(rect) {
    if (!rect) {
        return null;
    }

    const left = Number(rect.left ?? rect.x ?? 0);
    const top = Number(rect.top ?? rect.y ?? 0);
    const width = Number(rect.width ?? 0);
    const height = Number(rect.height ?? 0);
    if (!Number.isFinite(left) ||
        !Number.isFinite(top) ||
        !isFinitePositive(width) ||
        !isFinitePositive(height)) {
        return null;
    }

    return {
        left,
        top,
        width,
        height,
        right: Number(rect.right ?? left + width),
        bottom: Number(rect.bottom ?? top + height)
    };
}

function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
}

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

export function computeContainedImageRect({
    naturalWidth,
    naturalHeight,
    elementRect
} = {}) {
    const width = Number(naturalWidth);
    const height = Number(naturalHeight);
    const rect = normalizeRect(elementRect);
    if (!isFinitePositive(width) || !isFinitePositive(height) || !rect) {
        return null;
    }

    const naturalAspect = width / height;
    const elementAspect = rect.width / rect.height;
    let renderedWidth = rect.width;
    let renderedHeight = rect.height;
    let left = rect.left;
    let top = rect.top;

    if (elementAspect > naturalAspect) {
        renderedHeight = rect.height;
        renderedWidth = renderedHeight * naturalAspect;
        left = rect.left + ((rect.width - renderedWidth) / 2);
    } else if (elementAspect < naturalAspect) {
        renderedWidth = rect.width;
        renderedHeight = renderedWidth / naturalAspect;
        top = rect.top + ((rect.height - renderedHeight) / 2);
    }

    return {
        left,
        top,
        width: renderedWidth,
        height: renderedHeight,
        right: left + renderedWidth,
        bottom: top + renderedHeight,
        scaleX: renderedWidth / width,
        scaleY: renderedHeight / height
    };
}

export function mapPointToImagePixel({
    clientX,
    clientY,
    naturalWidth,
    naturalHeight,
    elementRect,
    clampToImage = false
} = {}) {
    const width = Number(naturalWidth);
    const height = Number(naturalHeight);
    const contentRect = computeContainedImageRect({
        naturalWidth: width,
        naturalHeight: height,
        elementRect
    });
    const x = Number(clientX);
    const y = Number(clientY);
    if (!contentRect || !Number.isFinite(x) || !Number.isFinite(y)) {
        return {
            inside: false,
            reason: 'invalid'
        };
    }

    const rawInside = x >= contentRect.left &&
        x <= contentRect.right &&
        y >= contentRect.top &&
        y <= contentRect.bottom;
    if (!rawInside && !clampToImage) {
        return {
            inside: false,
            reason: 'outside',
            contentRect,
            width,
            height
        };
    }

    const localX = clamp(x - contentRect.left, 0, contentRect.width);
    const localY = clamp(y - contentRect.top, 0, contentRect.height);
    return {
        inside: true,
        clamped: !rawInside,
        x: clamp(Math.floor(localX / contentRect.scaleX), 0, width - 1),
        y: clamp(Math.floor(localY / contentRect.scaleY), 0, height - 1),
        width,
        height,
        scale: contentRect.scaleX,
        scaleX: contentRect.scaleX,
        scaleY: contentRect.scaleY,
        contentRect
    };
}

export function mapImagePixelToStagePoint({
    x,
    y,
    naturalWidth,
    naturalHeight,
    imageElement,
    stageElement,
    elementRect = null,
    stageRect = null
} = {}) {
    const width = Number(naturalWidth ?? imageElement?.naturalWidth ?? 0);
    const height = Number(naturalHeight ?? imageElement?.naturalHeight ?? 0);
    const imageRect = normalizeRect(elementRect || imageElement?.getBoundingClientRect?.());
    const stageBounds = normalizeRect(stageRect || stageElement?.getBoundingClientRect?.());
    const contentRect = computeContainedImageRect({
        naturalWidth: width,
        naturalHeight: height,
        elementRect: imageRect
    });
    if (!contentRect || !stageBounds) {
        return null;
    }

    const pixelX = clamp(Number(x), 0, width - 1);
    const pixelY = clamp(Number(y), 0, height - 1);
    const scrollLeft = Number(stageElement?.scrollLeft ?? 0) || 0;
    const scrollTop = Number(stageElement?.scrollTop ?? 0) || 0;
    return {
        left: contentRect.left - stageBounds.left + scrollLeft + ((pixelX + 0.5) * contentRect.scaleX),
        top: contentRect.top - stageBounds.top + scrollTop + ((pixelY + 0.5) * contentRect.scaleY),
        scaleX: contentRect.scaleX,
        scaleY: contentRect.scaleY,
        contentRect
    };
}

export function mapImageRoiToStageRect({
    roi,
    naturalWidth,
    naturalHeight,
    imageElement,
    stageElement,
    elementRect = null,
    stageRect = null
} = {}) {
    const width = Number(naturalWidth ?? imageElement?.naturalWidth ?? 0);
    const height = Number(naturalHeight ?? imageElement?.naturalHeight ?? 0);
    const clampedRoi = clampImageRoi(roi, width, height);
    const imageRect = normalizeRect(elementRect || imageElement?.getBoundingClientRect?.());
    const stageBounds = normalizeRect(stageRect || stageElement?.getBoundingClientRect?.());
    const contentRect = computeContainedImageRect({
        naturalWidth: width,
        naturalHeight: height,
        elementRect: imageRect
    });
    if (!clampedRoi || !contentRect || !stageBounds) {
        return null;
    }

    const scrollLeft = Number(stageElement?.scrollLeft ?? 0) || 0;
    const scrollTop = Number(stageElement?.scrollTop ?? 0) || 0;
    return {
        left: contentRect.left - stageBounds.left + scrollLeft + (clampedRoi.x * contentRect.scaleX),
        top: contentRect.top - stageBounds.top + scrollTop + (clampedRoi.y * contentRect.scaleY),
        width: clampedRoi.width * contentRect.scaleX,
        height: clampedRoi.height * contentRect.scaleY,
        scaleX: contentRect.scaleX,
        scaleY: contentRect.scaleY,
        contentRect,
        roi: clampedRoi
    };
}

function formatZoomPercent(scale) {
    const percent = Number(scale) * 100;
    if (!Number.isFinite(percent) || percent <= 0) {
        return '-';
    }

    if (percent > 0 && percent < 1) {
        return '<1%';
    }

    return `${Math.round(percent)}%`;
}

export function rgbToGray(r, g, b) {
    return Math.round((0.299 * Number(r)) + (0.587 * Number(g)) + (0.114 * Number(b)));
}

function formatNumber(value, fractionDigits = 1) {
    const number = Number(value);
    if (!Number.isFinite(number)) {
        return '-';
    }

    return Number.isInteger(number)
        ? String(number)
        : number.toFixed(fractionDigits).replace(/\.0+$/, '').replace(/(\.\d*?)0+$/, '$1');
}

function formatPixelValue(rgba) {
    const red = Number(rgba?.[0] ?? 0);
    const green = Number(rgba?.[1] ?? 0);
    const blue = Number(rgba?.[2] ?? 0);
    return red === green && green === blue
        ? `灰度: ${red}`
        : `RGB: ${red},${green},${blue}  灰度≈${rgbToGray(red, green, blue)}`;
}

function formatNeighborhood(label, stats) {
    if (!stats?.ok) {
        return `${label}: -`;
    }

    return `${label}: 均值 ${formatNumber(stats.gray.mean)} min ${stats.gray.min} max ${stats.gray.max}`;
}

export function formatWorldCoordinateStatus(world) {
    if (world?.kind === 'world') {
        const unit = world.unit ? ` ${world.unit}` : '';
        const frame = world.frameId ? ` @${world.frameId}` : '';
        return `世界: ${formatNumber(world.x, 3)},${formatNumber(world.y, 3)}${unit}${frame}`;
    }

    const hint = world?.hint ? ` (${world.hint})` : '';
    return `世界: ${PIXEL_PROBE_NO_WORLD_MESSAGE}${hint}`;
}

export function formatPixelProbeStatus({
    x,
    y,
    width,
    height,
    rgba,
    scale
} = {}) {
    return `X: ${x}  Y: ${y}  ${formatPixelValue(rgba)}  图像: ${width}x${height}  缩放: ${formatZoomPercent(scale)}`;
}

export function formatLockedPixelProbeStatus({
    x,
    y,
    width,
    height,
    rgba,
    scale,
    neighborhoods = {},
    world = null
} = {}) {
    return [
        `已锁定 X: ${x}  Y: ${y}`,
        formatPixelValue(rgba),
        formatNeighborhood('3x3', neighborhoods[3]),
        formatNeighborhood('5x5', neighborhoods[5]),
        `图像: ${width}x${height}`,
        `缩放: ${formatZoomPercent(scale)}`,
        formatWorldCoordinateStatus(world)
    ].join('  ');
}

export function formatRoiProbeStatus({
    roi,
    stats,
    world = null
} = {}) {
    if (!roi || !stats?.ok) {
        return `ROI: -  ${formatWorldCoordinateStatus(world)}`;
    }

    const parts = [
        `ROI x:${roi.x} y:${roi.y} w:${roi.width} h:${roi.height}`,
        `像素:${stats.count}`,
        `灰度 mean:${formatNumber(stats.gray.mean)} min:${stats.gray.min} max:${stats.gray.max}`
    ];
    if (stats.rgbMean) {
        parts.push(`RGB mean:${formatNumber(stats.rgbMean.r)},${formatNumber(stats.rgbMean.g)},${formatNumber(stats.rgbMean.b)}`);
    }
    parts.push(formatWorldCoordinateStatus(world));
    return parts.join('  ');
}

function getImageSourceKey(imageElement) {
    return String(
        imageElement?.currentSrc ||
        imageElement?.src ||
        imageElement?.getAttribute?.('src') ||
        ''
    );
}

function normalizeSampleSize(size) {
    const value = Math.max(1, Math.round(Number(size) || 1));
    return value % 2 === 0 ? value + 1 : value;
}

export function summarizeRgbaData(data) {
    const values = data || [];
    const count = Math.floor(values.length / 4);
    if (count <= 0) {
        return null;
    }

    let graySum = 0;
    let grayMin = 255;
    let grayMax = 0;
    let redSum = 0;
    let greenSum = 0;
    let blueSum = 0;
    let hasColor = false;

    for (let index = 0; index < count; index += 1) {
        const offset = index * 4;
        const red = Number(values[offset] ?? 0);
        const green = Number(values[offset + 1] ?? 0);
        const blue = Number(values[offset + 2] ?? 0);
        const gray = rgbToGray(red, green, blue);
        graySum += gray;
        grayMin = Math.min(grayMin, gray);
        grayMax = Math.max(grayMax, gray);
        redSum += red;
        greenSum += green;
        blueSum += blue;
        hasColor = hasColor || red !== green || green !== blue;
    }

    return {
        ok: true,
        count,
        gray: {
            mean: graySum / count,
            min: grayMin,
            max: grayMax
        },
        rgbMean: hasColor
            ? {
                r: redSum / count,
                g: greenSum / count,
                b: blueSum / count
            }
            : null
    };
}

export function clampImageRoi(roi, naturalWidth, naturalHeight) {
    const imageWidth = Math.round(Number(naturalWidth) || 0);
    const imageHeight = Math.round(Number(naturalHeight) || 0);
    if (!isFinitePositive(imageWidth) || !isFinitePositive(imageHeight) || !roi) {
        return null;
    }

    const rawX = Number(roi.x ?? roi.left ?? 0);
    const rawY = Number(roi.y ?? roi.top ?? 0);
    const rawWidth = Number(roi.width ?? roi.w ?? 0);
    const rawHeight = Number(roi.height ?? roi.h ?? 0);
    if (!Number.isFinite(rawX) ||
        !Number.isFinite(rawY) ||
        !isFinitePositive(rawWidth) ||
        !isFinitePositive(rawHeight)) {
        return null;
    }

    const left = Math.floor(rawX);
    const top = Math.floor(rawY);
    const right = Math.ceil(rawX + rawWidth);
    const bottom = Math.ceil(rawY + rawHeight);
    const clampedLeft = clamp(left, 0, imageWidth);
    const clampedTop = clamp(top, 0, imageHeight);
    const clampedRight = clamp(right, 0, imageWidth);
    const clampedBottom = clamp(bottom, 0, imageHeight);
    const width = clampedRight - clampedLeft;
    const height = clampedBottom - clampedTop;
    if (!isFinitePositive(width) || !isFinitePositive(height)) {
        return null;
    }

    return {
        x: clampedLeft,
        y: clampedTop,
        width,
        height
    };
}

export function createImageRoiFromPoints(start, end, naturalWidth, naturalHeight) {
    const imageWidth = Number(naturalWidth ?? start?.width ?? end?.width ?? 0);
    const imageHeight = Number(naturalHeight ?? start?.height ?? end?.height ?? 0);
    if (!isFinitePositive(imageWidth) || !isFinitePositive(imageHeight)) {
        return null;
    }

    const startX = clamp(Number(start?.x), 0, imageWidth - 1);
    const startY = clamp(Number(start?.y), 0, imageHeight - 1);
    const endX = clamp(Number(end?.x), 0, imageWidth - 1);
    const endY = clamp(Number(end?.y), 0, imageHeight - 1);
    if (![startX, startY, endX, endY].every(Number.isFinite)) {
        return null;
    }

    const left = Math.min(startX, endX);
    const top = Math.min(startY, endY);
    const right = Math.max(startX, endX);
    const bottom = Math.max(startY, endY);
    return clampImageRoi({
        x: left,
        y: top,
        width: (right - left) + 1,
        height: (bottom - top) + 1
    }, imageWidth, imageHeight);
}

function flattenMatrix(raw) {
    if (!Array.isArray(raw)) {
        return null;
    }

    if (raw.length === 6 || raw.length === 9) {
        const flat = raw.map(Number);
        return flat.every(Number.isFinite) ? flat : null;
    }

    if (raw.length === 2 && raw.every(row => Array.isArray(row) && row.length >= 3)) {
        const flat = [
            Number(raw[0][0]),
            Number(raw[0][1]),
            Number(raw[0][2]),
            Number(raw[1][0]),
            Number(raw[1][1]),
            Number(raw[1][2])
        ];
        return flat.every(Number.isFinite) ? flat : null;
    }

    if (raw.length === 3 && raw.every(row => Array.isArray(row) && row.length >= 3)) {
        const flat = raw.flatMap(row => row.slice(0, 3).map(Number));
        return flat.every(Number.isFinite) ? flat : null;
    }

    return null;
}

function readWorldMatrix(candidate) {
    if (!candidate) {
        return null;
    }

    if (Array.isArray(candidate)) {
        return flattenMatrix(candidate);
    }

    const rawMatrix = readOwn(
        candidate,
        'matrix',
        'Matrix',
        'transformMatrix',
        'TransformMatrix',
        'pixelToWorldMatrix',
        'PixelToWorldMatrix',
        'pixelToWorldTransform',
        'PixelToWorldTransform'
    );
    if (rawMatrix) {
        return flattenMatrix(rawMatrix);
    }

    const affine = ['a', 'b', 'c', 'd', 'e', 'f'].map(key => Number(readOwn(candidate, key, key.toUpperCase())));
    return affine.every(Number.isFinite) ? affine : null;
}

function applyWorldMatrix(matrix, x, y) {
    if (!matrix) {
        return null;
    }

    if (matrix.length === 6) {
        return {
            x: (matrix[0] * x) + (matrix[1] * y) + matrix[2],
            y: (matrix[3] * x) + (matrix[4] * y) + matrix[5]
        };
    }

    if (matrix.length === 9) {
        const denominator = (matrix[6] * x) + (matrix[7] * y) + matrix[8];
        if (!Number.isFinite(denominator) || Math.abs(denominator) < Number.EPSILON) {
            return null;
        }

        return {
            x: ((matrix[0] * x) + (matrix[1] * y) + matrix[2]) / denominator,
            y: ((matrix[3] * x) + (matrix[4] * y) + matrix[5]) / denominator
        };
    }

    return null;
}

function collectWorldCandidates(previewState) {
    const observation = readOwn(previewState, 'observation', 'Observation');
    const visualScene = readOwn(observation, 'visualScene', 'VisualScene');
    const outputData = readOwn(previewState, 'outputData', 'OutputData');
    const stateSpatial = readOwn(previewState, 'spatialContext', 'SpatialContext');
    const outputSpatial = readOwn(outputData, 'spatialContext', 'SpatialContext');
    const observationSpatial = readOwn(observation, 'spatialContext', 'SpatialContext');
    const sources = [
        previewState,
        stateSpatial,
        outputSpatial,
        observationSpatial,
        visualScene
    ].filter(Boolean);
    const candidates = [];

    sources.forEach(source => {
        [
            'pixelToWorld',
            'PixelToWorld',
            'pixelToWorldMapping',
            'PixelToWorldMapping',
            'pixelToWorldTransform',
            'PixelToWorldTransform'
        ].forEach(key => {
            const value = readOwn(source, key);
            if (value) {
                candidates.push({ value, source });
            }
        });
    });

    return {
        candidates,
        visualScene,
        spatialContexts: [stateSpatial, outputSpatial, observationSpatial].filter(Boolean)
    };
}

function collectWorldHints(visualScene, spatialContexts) {
    const hints = [];
    const coordinateSpace = readOwn(visualScene, 'coordinateSpace', 'CoordinateSpace');
    const frameId = readOwn(visualScene, 'frameId', 'FrameId');
    const unit = readOwn(visualScene, 'unit', 'Unit');
    if (coordinateSpace) {
        hints.push(`coordinateSpace=${coordinateSpace}`);
    }
    if (frameId) {
        hints.push(`frameId=${frameId}`);
    }
    if (unit) {
        hints.push(`unit=${unit}`);
    }
    spatialContexts.forEach(context => {
        const contextFrame = readOwn(context, 'frameId', 'FrameId');
        const contextSpace = readOwn(context, 'coordinateSpace', 'CoordinateSpace');
        if (contextFrame || contextSpace) {
            hints.push(`spatialContext=${contextFrame || contextSpace}`);
        }
    });

    return hints.slice(0, 3).join(' · ');
}

export function resolvePixelWorldCoordinate(point, previewState) {
    const x = Number(point?.x);
    const y = Number(point?.y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) {
        return {
            kind: 'unavailable',
            message: PIXEL_PROBE_NO_WORLD_MESSAGE
        };
    }

    const { candidates, visualScene, spatialContexts } = collectWorldCandidates(previewState);
    for (const candidate of candidates) {
        const matrix = readWorldMatrix(candidate.value);
        const world = applyWorldMatrix(matrix, x, y);
        if (world && Number.isFinite(world.x) && Number.isFinite(world.y)) {
            return {
                kind: 'world',
                x: world.x,
                y: world.y,
                unit: readOwn(candidate.value, 'unit', 'Unit', 'worldUnit', 'WorldUnit') ||
                    readOwn(candidate.source, 'unit', 'Unit', 'worldUnit', 'WorldUnit') ||
                    readOwn(visualScene, 'unit', 'Unit') ||
                    '',
                frameId: readOwn(candidate.value, 'frameId', 'FrameId', 'worldFrameId', 'WorldFrameId') ||
                    readOwn(candidate.source, 'frameId', 'FrameId', 'worldFrameId', 'WorldFrameId') ||
                    readOwn(visualScene, 'frameId', 'FrameId') ||
                    '',
                source: 'pixelToWorld'
            };
        }
    }

    return {
        kind: 'unavailable',
        message: PIXEL_PROBE_NO_WORLD_MESSAGE,
        hint: collectWorldHints(visualScene, spatialContexts)
    };
}

export class ImagePixelProbe {
    constructor({
        createCanvas = null
    } = {}) {
        this.createCanvas = typeof createCanvas === 'function' ? createCanvas : null;
        this.cache = null;
    }

    reset() {
        this.cache = null;
    }

    mapPoint(point, imageElement, options = {}) {
        const naturalWidth = Number(imageElement?.naturalWidth ?? 0);
        const naturalHeight = Number(imageElement?.naturalHeight ?? 0);
        const elementRect = imageElement?.getBoundingClientRect?.();
        return mapPointToImagePixel({
            clientX: point?.clientX,
            clientY: point?.clientY,
            naturalWidth,
            naturalHeight,
            elementRect,
            clampToImage: options.clampToImage === true
        });
    }

    probePoint(point, imageElement) {
        if (!imageElement) {
            return {
                kind: 'unavailable',
                message: PIXEL_PROBE_NO_IMAGE_MESSAGE
            };
        }

        const naturalWidth = Number(imageElement.naturalWidth ?? 0);
        const naturalHeight = Number(imageElement.naturalHeight ?? 0);
        if (!isFinitePositive(naturalWidth) || !isFinitePositive(naturalHeight) || imageElement.complete === false) {
            return {
                kind: 'loading',
                message: PIXEL_PROBE_LOADING_MESSAGE
            };
        }

        const mapped = this.mapPoint(point, imageElement);
        if (!mapped.inside) {
            return {
                kind: 'outside',
                message: PIXEL_PROBE_OUTSIDE_MESSAGE,
                mapped
            };
        }

        const pixel = this.readPixel(imageElement, mapped.x, mapped.y);
        if (!pixel.ok) {
            return {
                kind: 'unavailable',
                message: PIXEL_PROBE_UNREADABLE_MESSAGE,
                mapped,
                error: pixel.error
            };
        }

        return {
            kind: 'pixel',
            message: formatPixelProbeStatus({
                ...mapped,
                rgba: pixel.rgba
            }),
            mapped,
            rgba: pixel.rgba
        };
    }

    createLockedPoint(mapped, imageElement, previewState = null) {
        const pixel = this.readPixel(imageElement, mapped?.x, mapped?.y);
        if (!pixel.ok) {
            return {
                kind: 'unavailable',
                message: PIXEL_PROBE_UNREADABLE_MESSAGE,
                mapped,
                error: pixel.error
            };
        }

        const neighborhoods = {
            3: this.readNeighborhoodStats(imageElement, mapped.x, mapped.y, 3),
            5: this.readNeighborhoodStats(imageElement, mapped.x, mapped.y, 5)
        };
        const world = resolvePixelWorldCoordinate(mapped, previewState);
        return {
            kind: 'locked',
            message: formatLockedPixelProbeStatus({
                ...mapped,
                rgba: pixel.rgba,
                neighborhoods,
                world
            }),
            mapped,
            rgba: pixel.rgba,
            neighborhoods,
            world
        };
    }

    createRoiSelection(roi, imageElement, previewState = null) {
        const statsResult = this.readRoiStats(imageElement, roi);
        if (!statsResult.ok) {
            return {
                kind: 'unavailable',
                message: PIXEL_PROBE_UNREADABLE_MESSAGE,
                roi,
                error: statsResult.error
            };
        }

        const world = resolvePixelWorldCoordinate({
            x: statsResult.roi.x,
            y: statsResult.roi.y
        }, previewState);
        return {
            kind: 'roi',
            message: formatRoiProbeStatus({
                roi: statsResult.roi,
                stats: statsResult.stats,
                world
            }),
            roi: statsResult.roi,
            stats: statsResult.stats,
            world
        };
    }

    readPixel(imageElement, x, y) {
        try {
            const cache = this.ensureCanvasCache(imageElement);
            if (!cache.ok) {
                return cache;
            }

            const pixel = cache.context.getImageData(x, y, 1, 1);
            return {
                ok: true,
                rgba: pixel?.data || [0, 0, 0, 255]
            };
        } catch (error) {
            return {
                ok: false,
                error
            };
        }
    }

    readNeighborhoodStats(imageElement, x, y, size = 3) {
        const naturalWidth = Number(imageElement?.naturalWidth ?? 0);
        const naturalHeight = Number(imageElement?.naturalHeight ?? 0);
        const sampleSize = normalizeSampleSize(size);
        const radius = Math.floor(sampleSize / 2);
        const roi = clampImageRoi({
            x: Number(x) - radius,
            y: Number(y) - radius,
            width: sampleSize,
            height: sampleSize
        }, naturalWidth, naturalHeight);
        if (!roi) {
            return {
                ok: false
            };
        }

        const block = this.readImageDataBlock(imageElement, roi.x, roi.y, roi.width, roi.height);
        if (!block.ok) {
            return block;
        }

        return {
            ...summarizeRgbaData(block.data),
            roi
        };
    }

    readRoiStats(imageElement, roi) {
        const naturalWidth = Number(imageElement?.naturalWidth ?? 0);
        const naturalHeight = Number(imageElement?.naturalHeight ?? 0);
        const clampedRoi = clampImageRoi(roi, naturalWidth, naturalHeight);
        if (!clampedRoi) {
            return {
                ok: false
            };
        }

        const block = this.readImageDataBlock(
            imageElement,
            clampedRoi.x,
            clampedRoi.y,
            clampedRoi.width,
            clampedRoi.height
        );
        if (!block.ok) {
            return block;
        }

        return {
            ok: true,
            roi: clampedRoi,
            stats: summarizeRgbaData(block.data)
        };
    }

    readImageDataBlock(imageElement, x, y, width, height) {
        try {
            const cache = this.ensureCanvasCache(imageElement);
            if (!cache.ok) {
                return cache;
            }

            const block = cache.context.getImageData(x, y, width, height);
            return {
                ok: true,
                data: block?.data || []
            };
        } catch (error) {
            return {
                ok: false,
                error
            };
        }
    }

    ensureCanvasCache(imageElement) {
        const naturalWidth = Number(imageElement?.naturalWidth ?? 0);
        const naturalHeight = Number(imageElement?.naturalHeight ?? 0);
        if (!isFinitePositive(naturalWidth) || !isFinitePositive(naturalHeight)) {
            return {
                ok: false
            };
        }

        const sourceKey = `${getImageSourceKey(imageElement)}|${naturalWidth}x${naturalHeight}`;
        if (this.cache?.sourceKey === sourceKey && this.cache?.context) {
            return {
                ok: true,
                context: this.cache.context
            };
        }

        const canvas = this.createCanvas
            ? this.createCanvas(naturalWidth, naturalHeight)
            : this.createDocumentCanvas();
        const context = canvas?.getContext?.('2d', { willReadFrequently: true }) ||
            canvas?.getContext?.('2d');
        if (!canvas || !context) {
            return {
                ok: false
            };
        }

        canvas.width = naturalWidth;
        canvas.height = naturalHeight;
        context.drawImage(imageElement, 0, 0, naturalWidth, naturalHeight);
        this.cache = {
            sourceKey,
            canvas,
            context
        };

        return {
            ok: true,
            context
        };
    }

    createDocumentCanvas() {
        if (typeof document === 'undefined' || typeof document.createElement !== 'function') {
            return null;
        }

        return document.createElement('canvas');
    }
}
