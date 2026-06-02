const DEFAULT_RESULT_DEFECT_PREVIEW_LIMIT = 300;
const RESULT_DEFECT_TEXT_LIMIT = 160;
const RESULT_DEFECT_ID_TEXT_LIMIT = 96;

function normalizeLimit(limit) {
    const value = Number(limit ?? DEFAULT_RESULT_DEFECT_PREVIEW_LIMIT);
    return Number.isFinite(value) && value > 0
        ? Math.floor(value)
        : DEFAULT_RESULT_DEFECT_PREVIEW_LIMIT;
}

function readFirstDefined(...values) {
    return values.find(value => value !== undefined && value !== null);
}

function compactText(value, limit = RESULT_DEFECT_TEXT_LIMIT) {
    if (value === null || value === undefined || typeof value !== 'string') {
        return value;
    }

    const maxLength = Number.isFinite(limit) && limit > 0 ? limit : RESULT_DEFECT_TEXT_LIMIT;
    return value.length > maxLength
        ? `${value.slice(0, maxLength)}...`
        : value;
}

function getActualDefects(result) {
    const actualDefects = result?.defects || result?.Defects;
    return Array.isArray(actualDefects) ? actualDefects : [];
}

function readNumericCount(...values) {
    for (const value of values) {
        const number = Number(value);
        if (Number.isFinite(number) && number > 0) {
            return Math.floor(number);
        }
    }

    return 0;
}

function createSyntheticDefect(index) {
    return {
        type: `Target ${index + 1}`,
        description: 'Result did not include defect details.'
    };
}

function createResultDefectPreview(defect, index) {
    if (!defect || typeof defect !== 'object') {
        return {
            type: compactText(defect ?? `Target ${index + 1}`),
            description: ''
        };
    }

    const className = readFirstDefined(defect.className, defect.ClassName);
    const type = readFirstDefined(
        defect.type,
        defect.Type,
        defect.label,
        defect.Label,
        className,
        `Target ${index + 1}`
    );

    const preview = {
        id: compactText(readFirstDefined(defect.id, defect.Id, index), RESULT_DEFECT_ID_TEXT_LIMIT),
        type: compactText(type),
        description: compactText(readFirstDefined(
            defect.description,
            defect.Description,
            defect.message,
            defect.Message,
            className,
            type
        )),
        x: readFirstDefined(defect.x, defect.X),
        y: readFirstDefined(defect.y, defect.Y),
        width: readFirstDefined(defect.width, defect.Width),
        height: readFirstDefined(defect.height, defect.Height),
        confidenceScore: readFirstDefined(
            defect.confidenceScore,
            defect.ConfidenceScore,
            defect.confidence,
            defect.Confidence
        )
    };

    return Object.fromEntries(
        Object.entries(preview).filter(([, value]) => value !== undefined)
    );
}

export function getResultDefectCount(result) {
    const actualDefects = getActualDefects(result);
    if (actualDefects.length > 0) {
        return actualDefects.length;
    }

    return readNumericCount(result?.defectCount, result?.DefectCount);
}

export function buildResultDefects(result, options = {}) {
    const maxItems = normalizeLimit(options.maxItems);
    const actualDefects = getActualDefects(result);
    if (actualDefects.length > 0) {
        return actualDefects
            .slice(0, maxItems)
            .map((defect, index) => createResultDefectPreview(defect, index));
    }

    const defectCount = getResultDefectCount(result);
    if (defectCount <= 0) {
        return [];
    }

    return Array.from(
        { length: Math.min(defectCount, maxItems) },
        (_, index) => createSyntheticDefect(index)
    );
}

export {
    DEFAULT_RESULT_DEFECT_PREVIEW_LIMIT
};
