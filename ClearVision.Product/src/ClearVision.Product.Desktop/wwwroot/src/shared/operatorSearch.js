function normalizeSearchPart(value) {
    return String(value ?? '').trim();
}

function collectArray(...values) {
    return values.flatMap(value => Array.isArray(value) ? value : []);
}

function collectOptionText(option) {
    if (typeof option === 'string') {
        return option;
    }

    return [
        option?.label,
        option?.Label,
        option?.name,
        option?.Name,
        option?.value,
        option?.Value
    ].filter(value => value !== undefined && value !== null).join(' ');
}

function collectPortText(port) {
    return [
        port?.name,
        port?.Name,
        port?.displayName,
        port?.DisplayName,
        port?.description,
        port?.Description,
        port?.dataType,
        port?.DataType,
        port?.type,
        port?.Type
    ].filter(Boolean).join(' ');
}

function collectParameterText(parameter) {
    const options = collectArray(parameter?.options, parameter?.Options);
    return [
        parameter?.name,
        parameter?.Name,
        parameter?.displayName,
        parameter?.DisplayName,
        parameter?.description,
        parameter?.Description,
        parameter?.dataType,
        parameter?.DataType,
        parameter?.type,
        parameter?.Type,
        ...options.map(collectOptionText)
    ].filter(Boolean).join(' ');
}

export function buildOperatorSearchText(operator) {
    if (!operator || typeof operator !== 'object') {
        return '';
    }

    const inputPorts = collectArray(operator.inputPorts, operator.InputPorts);
    const outputPorts = collectArray(operator.outputPorts, operator.OutputPorts);
    const parameters = collectArray(operator.parameters, operator.Parameters);
    const tags = collectArray(operator.tags, operator.Tags, operator.keywords, operator.Keywords);

    return [
        operator.displayName,
        operator.DisplayName,
        operator.title,
        operator.Title,
        operator.name,
        operator.Name,
        operator.type,
        operator.Type,
        operator.category,
        operator.Category,
        operator.description,
        operator.Description,
        operator.inputType,
        operator.InputType,
        operator.outputType,
        operator.OutputType,
        ...tags,
        ...inputPorts.map(collectPortText),
        ...outputPorts.map(collectPortText),
        ...parameters.map(collectParameterText)
    ]
        .map(normalizeSearchPart)
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
}

export function searchOperators(operators = [], keyword = '') {
    const term = normalizeSearchPart(keyword).toLowerCase();
    const source = Array.isArray(operators) ? operators : [];
    if (!term) {
        return source.slice();
    }

    return source.filter(operator => buildOperatorSearchText(operator).includes(term));
}
