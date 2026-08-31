/**
 * Pure CSV cell formatter used by all browser-generated exports.
 * Formula markers remain dangerous after a leading space, tab, CR, or LF, so they are detected
 * after leading whitespace before CSV quoting is applied.
 */
export function sanitizeCsvText(value) {
    const text = value === null || value === undefined ? '' : String(value);
    return /^[\s]*[=+\-@]/u.test(text) ? `'${text}` : text;
}

export function formatCsvField(value) {
    const text = sanitizeCsvText(value);
    return /[",\r\n]/u.test(text)
        ? `"${text.replaceAll('"', '""')}"`
        : text;
}

export function formatCsvRow(fields) {
    return Array.from(fields ?? [], formatCsvField).join(',');
}
