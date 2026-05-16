import {
    DEFAULT_OPERATOR_COLOR,
    getCategoryIconPath,
    getOperatorIconPath
} from './operatorVisuals.js';

const DEFAULT_OPERATOR_ICON_PATH =
    'M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L5.03 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z';

const DEFAULT_CATEGORY_COLOR = '#d4a853';
const DEFAULT_OPERATOR_ICON_COLOR = '#ff4d4f';

export function looksLikeSvgPath(value) {
    const normalized = String(value ?? '').trim();
    return /[Mm]/.test(normalized) &&
        /\d/.test(normalized) &&
        /^[MmZzLlHhVvCcSsQqTtAa0-9,.\-\s]+$/.test(normalized);
}

export function looksLikeIconName(value) {
    return /^[A-Za-z][A-Za-z0-9_-]{1,63}$/.test(String(value ?? '').trim());
}

export function normalizeIconName(value) {
    const normalized = String(value ?? '').trim();
    return looksLikeIconName(normalized) ? normalized : null;
}

export function normalizeOperatorIconName(operator) {
    if (!operator || typeof operator !== 'object') {
        return null;
    }

    return normalizeIconName(
        operator.iconName ||
        operator.IconName ||
        operator.icon_name ||
        operator.icon ||
        operator.Icon);
}

function toSvgDataUri(svgText) {
    return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svgText)}`;
}

function createPathSvgText(pathData, { color = DEFAULT_OPERATOR_ICON_COLOR, viewBox = '0 0 24 24' } = {}) {
    const safeColor = /^#[0-9a-f]{3,8}$/i.test(color) ? color : DEFAULT_OPERATOR_ICON_COLOR;
    return [
        `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}" width="24" height="24">`,
        `<path fill="${safeColor}" d="${pathData.replace(/"/g, '&quot;')}"/>`,
        '</svg>'
    ].join('');
}

export function createIconImageFromPath(pathData, options = {}) {
    const normalizedPath = String(pathData ?? '').trim();
    const path = looksLikeSvgPath(normalizedPath) ? normalizedPath : DEFAULT_OPERATOR_ICON_PATH;
    const image = document.createElement('img');
    image.className = options.imageClassName || 'operator-icon-img';
    image.alt = '';
    image.draggable = false;
    image.decoding = 'async';
    image.src = toSvgDataUri(createPathSvgText(path, options));
    return image;
}

export function createIconShell(className, image) {
    const shell = document.createElement('span');
    shell.className = className;
    shell.replaceChildren(image);
    return shell;
}

export function createOperatorIconElement(operator, className = 'operator-icon') {
    const type = operator?.type || operator?.Type || '';
    const category = operator?.category || operator?.Category || null;
    const iconName = normalizeOperatorIconName(operator);
    const path = operator?.iconPath ||
        operator?.IconPath ||
        getOperatorIconPath(type, category, iconName);

    return createIconShell(
        className,
        createIconImageFromPath(path, {
            color: operator?.color || operator?.Color || DEFAULT_OPERATOR_ICON_COLOR
        }));
}

export function createCategoryIconElement(category, className = 'tree-node-icon category-icon') {
    return createIconShell(
        className,
        createIconImageFromPath(getCategoryIconPath(category), {
            color: DEFAULT_CATEGORY_COLOR
        }));
}

export function createPathIconElement(path, className, color = DEFAULT_OPERATOR_COLOR) {
    return createIconShell(className, createIconImageFromPath(path, { color }));
}

export function renderOperatorIconInto(container, operator, className = '') {
    if (!container) {
        return;
    }

    const icon = createOperatorIconElement(operator, className || container.className || 'operator-icon');
    container.replaceChildren(...Array.from(icon.childNodes));
}
