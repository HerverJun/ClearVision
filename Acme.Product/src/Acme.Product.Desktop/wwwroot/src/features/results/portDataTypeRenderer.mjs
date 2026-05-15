/**
 * @typedef {'Image'|'Integer'|'Float'|'Boolean'|'String'|'Point'|'Rectangle'|'Contour'|'PointList'|'DetectionResult'|'DetectionList'|'CircleData'|'LineData'|'Any'} PortDataType
 *
 * @typedef {Object} ResultField
 * @property {string} key
 * @property {string} [label]
 * @property {*} value
 * @property {PortDataType|string} [dataType]
 * @property {string} [unit]
 * @property {string} [displayHint]
 * @property {string} [status]
 *
 * @typedef {Object} ResultCard
 * @property {string} id
 * @property {string} category
 * @property {string} title
 * @property {string} [status]
 * @property {number} [priority]
 * @property {ResultField[]} fields
 * @property {Object<string, *>} [meta]
 */

const IMAGE_KEY_PATTERN = /(image|bitmap|preview|thumbnail|base64|mask)/i;
const DETECTION_LIST_KEYS = new Set([
    'detections',
    'objects',
    'results',
    'defects',
    'boxes',
    'candidates',
    'suppresseddetections'
]);
const COMMUNICATION_KEYS = new Set([
    'response',
    'request',
    'statuscode',
    'httpstatus',
    'isconnected',
    'connected',
    'success',
    'topic',
    'address',
    'value',
    'latencyms',
    'roundtripms',
    'error',
    'errormessage'
]);

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function toLookupKey(value) {
    return String(value || '').trim().replace(/[\s_-]+/g, '').toLowerCase();
}

function isPlainObject(value) {
    return !!value && typeof value === 'object' && !Array.isArray(value);
}

function isImageLikeKey(key) {
    return IMAGE_KEY_PATTERN.test(toLookupKey(key));
}

function isImageLikeString(key, value) {
    if (typeof value !== 'string') {
        return false;
    }

    const trimmed = value.trim();
    return trimmed.startsWith('data:image/')
        || (isImageLikeKey(key) && trimmed.length > 120);
}

function isPointLike(value) {
    return isPlainObject(value)
        && ('x' in value || 'X' in value)
        && ('y' in value || 'Y' in value);
}

function isRectangleLike(value) {
    if (!isPlainObject(value)) {
        return false;
    }

    const keys = Object.keys(value).map(toLookupKey);
    return keys.includes('x')
        && keys.includes('y')
        && (keys.includes('width') || keys.includes('w'))
        && (keys.includes('height') || keys.includes('h'));
}

function isCircleLike(value) {
    if (!isPlainObject(value)) {
        return false;
    }

    const keys = Object.keys(value).map(toLookupKey);
    return (keys.includes('x') || keys.includes('centerx'))
        && (keys.includes('y') || keys.includes('centery'))
        && (keys.includes('radius') || keys.includes('r'));
}

function isLineLike(value) {
    if (!isPlainObject(value)) {
        return false;
    }

    const keys = Object.keys(value).map(toLookupKey);
    return (keys.includes('x1') || keys.includes('startx'))
        && (keys.includes('y1') || keys.includes('starty'))
        && (keys.includes('x2') || keys.includes('endx'))
        && (keys.includes('y2') || keys.includes('endy'));
}

function getCaseInsensitive(value, ...keys) {
    if (!isPlainObject(value)) {
        return undefined;
    }

    const entries = Object.entries(value);
    for (const requestedKey of keys) {
        const match = entries.find(([key]) => key.toLowerCase() === requestedKey.toLowerCase());
        if (match) {
            return match[1];
        }
    }

    return undefined;
}

function normalizeNumber(value) {
    const parsed = typeof value === 'number' ? value : Number.parseFloat(String(value ?? '').trim());
    return Number.isFinite(parsed) ? parsed : null;
}

function formatNumber(value) {
    const numericValue = normalizeNumber(value);
    if (numericValue === null) {
        return '--';
    }

    return Number.isInteger(numericValue) ? String(numericValue) : numericValue.toFixed(3);
}

function formatPoint(value) {
    const x = getCaseInsensitive(value, 'x', 'centerX');
    const y = getCaseInsensitive(value, 'y', 'centerY');
    return `(${formatNumber(x)}, ${formatNumber(y)})`;
}

function formatRectangle(value) {
    const x = getCaseInsensitive(value, 'x');
    const y = getCaseInsensitive(value, 'y');
    const width = getCaseInsensitive(value, 'width', 'w');
    const height = getCaseInsensitive(value, 'height', 'h');
    return `x ${formatNumber(x)}, y ${formatNumber(y)}, w ${formatNumber(width)}, h ${formatNumber(height)}`;
}

function formatCircle(value) {
    const x = getCaseInsensitive(value, 'x', 'centerX');
    const y = getCaseInsensitive(value, 'y', 'centerY');
    const radius = getCaseInsensitive(value, 'radius', 'r');
    return `center ${formatPoint({ x, y })}, r ${formatNumber(radius)}`;
}

function formatLine(value) {
    const x1 = getCaseInsensitive(value, 'x1', 'startX');
    const y1 = getCaseInsensitive(value, 'y1', 'startY');
    const x2 = getCaseInsensitive(value, 'x2', 'endX');
    const y2 = getCaseInsensitive(value, 'y2', 'endY');
    return `(${formatNumber(x1)}, ${formatNumber(y1)}) -> (${formatNumber(x2)}, ${formatNumber(y2)})`;
}

function extractDetectionItems(value) {
    if (Array.isArray(value)) {
        return value;
    }

    if (!isPlainObject(value)) {
        return [];
    }

    return getCaseInsensitive(value, 'detections', 'objects', 'defects', 'items', 'results', 'boxes') || [];
}

function detectionLabel(item, index) {
    if (!isPlainObject(item)) {
        return `Item ${index + 1}`;
    }

    return getCaseInsensitive(item, 'label', 'className', 'class', 'type', 'name', 'description')
        || `Detection ${index + 1}`;
}

function detectionScore(item) {
    const value = getCaseInsensitive(item, 'confidence', 'confidenceScore', 'score', 'probability');
    const numeric = normalizeNumber(value);
    if (numeric === null) {
        return '';
    }

    const pct = numeric <= 1 ? numeric * 100 : numeric;
    return `${pct.toFixed(1)}%`;
}

function renderDetectionList(value) {
    const items = extractDetectionItems(value);
    if (items.length === 0) {
        return '<span class="cv-result-empty">0 detections</span>';
    }

    const rows = items.slice(0, 8).map((item, index) => {
        const score = detectionScore(item);
        const nestedBox = isPlainObject(item)
            ? getCaseInsensitive(item, 'box', 'bbox', 'boundingBox', 'rect', 'rectangle')
            : null;
        const box = isRectangleLike(nestedBox)
            ? nestedBox
            : (isRectangleLike(item) ? item : null);
        const boxText = isRectangleLike(box) ? `<span class="cv-result-detection-box">${escapeHtml(formatRectangle(box))}</span>` : '';

        return `
            <div class="cv-result-detection-row">
                <span class="cv-result-detection-label">${escapeHtml(detectionLabel(item, index))}</span>
                ${score ? `<span class="cv-result-detection-score">${escapeHtml(score)}</span>` : ''}
                ${boxText}
            </div>
        `;
    }).join('');

    const hidden = items.length > 8
        ? `<div class="cv-result-hint">+${items.length - 8} more</div>`
        : '';

    return `<div class="cv-result-detection-list">${rows}${hidden}</div>`;
}

function renderStructuredObject(value) {
    if (!isPlainObject(value)) {
        return `<span class="cv-result-value">${escapeHtml(String(value ?? '--'))}</span>`;
    }

    const rows = Object.entries(value)
        .filter(([entryKey, entryValue]) => !isImageLikeString(entryKey, entryValue))
        .slice(0, 8)
        .map(([entryKey, entryValue]) => {
            const displayValue = isPlainObject(entryValue) || Array.isArray(entryValue)
                ? JSON.stringify(entryValue)
                : String(entryValue ?? '--');

            return `
                <div class="cv-result-kv-row">
                    <span class="cv-result-kv-key">${escapeHtml(entryKey)}</span>
                    <span class="cv-result-kv-value">${escapeHtml(displayValue)}</span>
                </div>
            `;
        }).join('');

    return `<div class="cv-result-kv-table">${rows || '<span class="cv-result-empty">--</span>'}</div>`;
}

function inferPortDataType(key, value, explicitType = null) {
    if (explicitType) {
        const normalizedExplicit = String(explicitType);
        if (normalizedExplicit && normalizedExplicit !== 'Any') {
            return normalizedExplicit;
        }
    }

    const lookupKey = toLookupKey(key);

    if (isImageLikeString(key, value)) {
        return 'Image';
    }

    if (typeof value === 'boolean' || lookupKey.startsWith('is') || lookupKey.startsWith('has')) {
        return 'Boolean';
    }

    if (typeof value === 'number') {
        return Number.isInteger(value) ? 'Integer' : 'Float';
    }

    if (Array.isArray(value)) {
        if (DETECTION_LIST_KEYS.has(lookupKey) || value.some(isRectangleLike) || value.some(item => isPlainObject(item) && (getCaseInsensitive(item, 'confidence', 'score', 'label', 'className') !== undefined))) {
            return 'DetectionList';
        }

        if (value.every(isPointLike)) {
            return 'PointList';
        }

        return 'Any';
    }

    if (isRectangleLike(value)) {
        return 'Rectangle';
    }

    if (isCircleLike(value)) {
        return 'CircleData';
    }

    if (isLineLike(value)) {
        return 'LineData';
    }

    if (isPointLike(value)) {
        return 'Point';
    }

    if (isPlainObject(value) && DETECTION_LIST_KEYS.has(lookupKey)) {
        return 'DetectionList';
    }

    if (typeof value === 'string') {
        return 'String';
    }

    return 'Any';
}

function inferResultCategory(field) {
    const category = String(field.category || '').trim();
    if (category) {
        return category;
    }

    const key = toLookupKey(field.key);
    const dataType = inferPortDataType(field.key, field.value, field.dataType);

    if (COMMUNICATION_KEYS.has(key) || /^(http|tcp|modbus|mqtt|serial|plc)/i.test(field.key)) {
        return 'communication';
    }

    if (dataType === 'DetectionResult' || dataType === 'DetectionList' || dataType === 'Rectangle') {
        return 'detection';
    }

    if (['Integer', 'Float', 'Point', 'PointList', 'CircleData', 'LineData', 'Contour'].includes(dataType)) {
        return 'measurement';
    }

    if (dataType === 'Boolean') {
        return 'boolean';
    }

    if (dataType === 'String' && /text|code|barcode|ocr|result/i.test(field.key)) {
        return 'recognition';
    }

    return 'structured';
}

function renderPortDataTypeValue(field) {
    const key = field?.key || '';
    const value = field?.value;
    const dataType = inferPortDataType(key, value, field?.dataType);
    const unit = field?.unit ? ` <span class="cv-result-unit">${escapeHtml(field.unit)}</span>` : '';

    if (value === null || value === undefined) {
        return '<span class="cv-result-empty">--</span>';
    }

    switch (dataType) {
        case 'Image':
            return '<span class="cv-result-muted">Image output available</span>';
        case 'Integer':
        case 'Float':
            return `<span class="cv-result-number">${escapeHtml(formatNumber(value))}</span>${unit}`;
        case 'Boolean':
            return `<span class="cv-result-bool ${value ? 'true' : 'false'}">${value ? 'True' : 'False'}</span>`;
        case 'Point':
            return `<span class="cv-result-geometry">${escapeHtml(formatPoint(value))}</span>`;
        case 'Rectangle':
            return `<span class="cv-result-geometry">${escapeHtml(formatRectangle(value))}</span>`;
        case 'CircleData':
            return `<span class="cv-result-geometry">${escapeHtml(formatCircle(value))}</span>`;
        case 'LineData':
            return `<span class="cv-result-geometry">${escapeHtml(formatLine(value))}</span>`;
        case 'PointList':
            return `<span class="cv-result-list-summary">${Array.isArray(value) ? value.length : 0} points</span>`;
        case 'DetectionResult':
        case 'DetectionList':
            return renderDetectionList(value);
        case 'String':
            return `<span class="cv-result-text">${escapeHtml(value)}</span>`;
        default:
            return Array.isArray(value)
                ? `<span class="cv-result-list-summary">${value.length} items</span>`
                : renderStructuredObject(value);
    }
}

function normalizeAnalysisCard(card, fallbackStatus = 'OK') {
    const fields = Array.isArray(card?.fields)
        ? card.fields
        : (Array.isArray(card?.Fields) ? card.Fields : []);

    return {
        id: card?.id || card?.Id || `card-${Math.random().toString(36).slice(2)}`,
        category: card?.category || card?.Category || 'structured',
        title: card?.title || card?.Title || card?.category || card?.Category || 'Result',
        status: card?.status || card?.Status || fallbackStatus,
        priority: Number(card?.priority ?? card?.Priority ?? 0),
        fields: fields.map((field, index) => ({
            key: field?.key || field?.Key || `field-${index}`,
            label: field?.label || field?.Label || field?.key || field?.Key || `Field ${index + 1}`,
            value: field?.value ?? field?.Value,
            dataType: field?.dataType || field?.DataType,
            unit: field?.unit || field?.Unit,
            displayHint: field?.displayHint || field?.DisplayHint,
            status: field?.status || field?.Status
        })),
        meta: card?.meta || card?.Meta || null
    };
}

function buildResultCardsFromOutputData(outputData, options = {}) {
    if (!isPlainObject(outputData)) {
        return [];
    }

    const fields = Object.entries(outputData)
        .filter(([key, value]) => !isImageLikeString(key, value))
        .map(([key, value]) => ({
            key,
            label: options.labelMap?.[key] || key,
            value,
            dataType: options.portTypes?.[key] || null,
            category: inferResultCategory({ key, value, dataType: options.portTypes?.[key] })
        }));

    if (fields.length === 0) {
        return [];
    }

    const grouped = fields.reduce((accumulator, field) => {
        const category = field.category || 'structured';
        if (!accumulator.has(category)) {
            accumulator.set(category, []);
        }
        accumulator.get(category).push(field);
        return accumulator;
    }, new Map());

    const categoryTitle = {
        recognition: 'Recognition',
        measurement: 'Measurements',
        detection: 'Detections',
        boolean: 'Judgment',
        communication: 'Communication',
        structured: 'Structured Output'
    };

    return [...grouped.entries()].map(([category, groupedFields], index) => ({
        id: `output-${category}-${index}`,
        category,
        title: categoryTitle[category] || category,
        status: options.status || 'OK',
        priority: 10 - index,
        fields: groupedFields
    }));
}

function getResultStatusClass(status) {
    const normalized = String(status || '').trim().toLowerCase();
    if (normalized === 'ng' || normalized === 'error') {
        return 'ng';
    }

    if (normalized === 'ok' || normalized === 'pass' || normalized === 'passed') {
        return 'ok';
    }

    return 'info';
}

function getResultStatusLabel(status) {
    const normalized = String(status || '').trim();
    return normalized.length > 0 ? normalized : 'INFO';
}

function renderResultCardHtml(card, options = {}) {
    const normalizedCard = normalizeAnalysisCard(card, options.fallbackStatus || 'OK');
    const statusClass = getResultStatusClass(normalizedCard.status || options.fallbackStatus || 'Info');
    const statusTitle = statusClass === 'info'
        ? '算子执行数据，仅供分析；不代表最终 OK/NG 判定'
        : '结果状态';
    const rows = normalizedCard.fields.map(field => `
        <div class="cv-result-field cv-result-field-${escapeHtml(inferResultCategory(field))}">
            <span class="cv-result-label">${escapeHtml(field.label || field.key)}</span>
            <span class="cv-result-rendered-value">${renderPortDataTypeValue(field)}</span>
        </div>
    `).join('');

    return `
        <div class="ac-card cv-result-card ac-status-${statusClass}" data-card-type="${escapeHtml(normalizedCard.category)}">
            <div class="ac-card-header">
                <span class="ac-card-title">${escapeHtml(normalizedCard.title)}</span>
                <span class="cv-result-card-status ${statusClass}" title="${escapeHtml(statusTitle)}">${escapeHtml(getResultStatusLabel(normalizedCard.status))}</span>
            </div>
            <div class="ac-card-body cv-result-card-body">
                ${rows || '<span class="cv-result-empty">No fields</span>'}
            </div>
        </div>
    `;
}

function summarizeResultField(field) {
    const dataType = inferPortDataType(field?.key, field?.value, field?.dataType);
    if (dataType === 'DetectionList' || dataType === 'DetectionResult') {
        return `${extractDetectionItems(field.value).length} detections`;
    }

    if (dataType === 'Integer' || dataType === 'Float') {
        return `${formatNumber(field.value)}${field.unit ? ` ${field.unit}` : ''}`;
    }

    if (dataType === 'Boolean') {
        return field.value ? 'True' : 'False';
    }

    if (['Point', 'Rectangle', 'CircleData', 'LineData'].includes(dataType)) {
        return renderPortDataTypeValue(field).replace(/<[^>]+>/g, '');
    }

    if (typeof field?.value === 'string') {
        return field.value;
    }

    if (Array.isArray(field?.value)) {
        return `${field.value.length} items`;
    }

    if (isPlainObject(field?.value)) {
        return `${Object.keys(field.value).length} fields`;
    }

    return String(field?.value ?? '--');
}

export {
    buildResultCardsFromOutputData,
    escapeHtml,
    inferPortDataType,
    inferResultCategory,
    normalizeAnalysisCard,
    renderPortDataTypeValue,
    renderResultCardHtml,
    summarizeResultField
};
