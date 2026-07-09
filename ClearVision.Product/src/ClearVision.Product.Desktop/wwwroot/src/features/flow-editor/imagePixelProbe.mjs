export const PIXEL_PROBE_DEFAULT_MESSAGE = '移动鼠标查看像素坐标和值';
export const PIXEL_PROBE_OUTSIDE_MESSAGE = '光标不在图像内';
export const PIXEL_PROBE_NO_IMAGE_MESSAGE = '暂无图像，无法显示像素值';
export const PIXEL_PROBE_LOADING_MESSAGE = '图像未加载，无法显示像素值';
export const PIXEL_PROBE_UNREADABLE_MESSAGE = '无法读取像素值';

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
    elementRect
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

    if (x < contentRect.left ||
        x > contentRect.right ||
        y < contentRect.top ||
        y > contentRect.bottom) {
        return {
            inside: false,
            reason: 'outside',
            contentRect,
            width,
            height
        };
    }

    const localX = x - contentRect.left;
    const localY = y - contentRect.top;
    return {
        inside: true,
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

function approximateGray(r, g, b) {
    return Math.round((0.299 * r) + (0.587 * g) + (0.114 * b));
}

export function formatPixelProbeStatus({
    x,
    y,
    width,
    height,
    rgba,
    scale
} = {}) {
    const red = Number(rgba?.[0] ?? 0);
    const green = Number(rgba?.[1] ?? 0);
    const blue = Number(rgba?.[2] ?? 0);
    const pixelText = red === green && green === blue
        ? `灰度: ${red}`
        : `RGB: ${red},${green},${blue}  灰度≈${approximateGray(red, green, blue)}`;

    return `X: ${x}  Y: ${y}  ${pixelText}  图像: ${width}x${height}  缩放: ${formatZoomPercent(scale)}`;
}

function getImageSourceKey(imageElement) {
    return String(
        imageElement?.currentSrc ||
        imageElement?.src ||
        imageElement?.getAttribute?.('src') ||
        ''
    );
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

        const elementRect = imageElement.getBoundingClientRect?.();
        const mapped = mapPointToImagePixel({
            clientX: point?.clientX,
            clientY: point?.clientY,
            naturalWidth,
            naturalHeight,
            elementRect
        });

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
