export const OPERATOR_PARAMETER_RULES = Object.freeze({
    ImageAcquisition: Object.freeze({
        parameters: Object.freeze({
            FilePath: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'SourceType', equals: 'file' }),
                disabledWhen: Object.freeze({ parameter: 'SourceType', equals: 'camera' }),
                disabledReason: '相机模式下不需要文件路径',
                mutuallyExclusiveGroup: 'image-source'
            }),
            CameraId: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'SourceType', equals: 'camera' }),
                disabledWhen: Object.freeze({ parameter: 'SourceType', equals: 'file' }),
                disabledReason: '文件模式下不需要相机绑定',
                mutuallyExclusiveGroup: 'image-source',
                atLeastOneOf: Object.freeze(['CameraId', 'CameraBindingId'])
            }),
            CameraBindingId: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'SourceType', equals: 'camera' }),
                disabledWhen: Object.freeze({ parameter: 'SourceType', equals: 'file' }),
                disabledReason: '文件模式下不需要相机绑定',
                mutuallyExclusiveGroup: 'image-source',
                atLeastOneOf: Object.freeze(['CameraId', 'CameraBindingId'])
            })
        })
    })
});

export function normalizeParameterName(name) {
    return String(name || '').trim().toLowerCase();
}

export function normalizeOperatorType(operatorOrType) {
    if (typeof operatorOrType === 'string') {
        return operatorOrType.trim();
    }

    return String(
        operatorOrType?.type ??
        operatorOrType?.Type ??
        operatorOrType?.operatorType ??
        operatorOrType?.OperatorType ??
        ''
    ).trim();
}

export function isEmptyParameterValue(value) {
    return value === null ||
        value === undefined ||
        (typeof value === 'string' && value.trim() === '');
}

export function normalizeAcquisitionSourceType(value) {
    const raw = String(value || 'File').trim();
    const separatorIndex = raw.indexOf('|');
    const token = (separatorIndex >= 0 ? raw.substring(0, separatorIndex) : raw).trim().toLowerCase();

    if (token === 'camera' || token.includes('相机')) {
        return 'camera';
    }

    if (token === 'file' || token.includes('文件')) {
        return 'file';
    }

    return token || 'file';
}

export function getParameterRawName(param) {
    return String(param?.name ?? param?.Name ?? '').trim();
}

export function isParameterRawRequired(param) {
    return Boolean(param?.isRequired ?? param?.IsRequired);
}

export function getParameterEffectiveValue(param) {
    return param?.value ?? param?.Value ?? param?.defaultValue ?? param?.DefaultValue ?? null;
}

export function getOperatorParameterValue(operator, parameterName, values = null) {
    const normalizedName = normalizeParameterName(parameterName);
    if (!normalizedName) {
        return null;
    }

    if (values && typeof values === 'object') {
        const matchKey = Object.keys(values).find(key => normalizeParameterName(key) === normalizedName);
        if (matchKey !== undefined) {
            return values[matchKey];
        }
    }

    const parameters = operator?.parameters ?? operator?.Parameters ?? null;
    if (Array.isArray(parameters)) {
        const parameter = parameters.find(item => normalizeParameterName(getParameterRawName(item)) === normalizedName);
        return parameter ? getParameterEffectiveValue(parameter) : null;
    }

    if (parameters && typeof parameters === 'object') {
        const matchKey = Object.keys(parameters).find(key => normalizeParameterName(key) === normalizedName);
        if (matchKey !== undefined) {
            return parameters[matchKey];
        }
    }

    return null;
}

export function getOperatorParameterRule(operatorOrType, parameterName) {
    const operatorType = normalizeOperatorType(operatorOrType);
    const normalizedName = normalizeParameterName(parameterName);
    const rules = OPERATOR_PARAMETER_RULES[operatorType]?.parameters || {};
    return Object.entries(rules)
        .find(([name]) => normalizeParameterName(name) === normalizedName)?.[1] || null;
}

function evaluateCondition(condition, operator, values) {
    if (!condition) {
        return false;
    }

    const rawValue = getOperatorParameterValue(operator, condition.parameter, values);
    const value = condition.parameter === 'SourceType'
        ? normalizeAcquisitionSourceType(rawValue)
        : String(rawValue ?? '').trim().toLowerCase();
    const expected = condition.parameter === 'SourceType'
        ? normalizeAcquisitionSourceType(condition.equals)
        : String(condition.equals ?? '').trim().toLowerCase();

    return value === expected;
}

function hasAnyPeerValue(operator, names, values) {
    return names.some(name => !isEmptyParameterValue(getOperatorParameterValue(operator, name, values)));
}

export function getParameterEffectiveState(operator, paramOrName, options = {}) {
    const parameterName = typeof paramOrName === 'string' ? paramOrName : getParameterRawName(paramOrName);
    const rule = getOperatorParameterRule(operator, parameterName);
    const values = options.values || null;
    const rawRequired = typeof paramOrName === 'string'
        ? false
        : isParameterRawRequired(paramOrName);
    const effectiveDisabled = Boolean(rule?.disabledWhen && evaluateCondition(rule.disabledWhen, operator, values));
    let effectiveRequired = rawRequired;

    if (rule?.requiredWhen) {
        effectiveRequired = evaluateCondition(rule.requiredWhen, operator, values);
    }

    if (effectiveRequired && Array.isArray(rule?.atLeastOneOf) && rule.atLeastOneOf.length > 1) {
        effectiveRequired = !hasAnyPeerValue(operator, rule.atLeastOneOf, values) ||
            !isEmptyParameterValue(getOperatorParameterValue(operator, parameterName, values));
    }

    if (effectiveDisabled) {
        effectiveRequired = false;
    }

    return {
        parameterName,
        rawRequired,
        effectiveRequired,
        effectiveDisabled,
        disabledReason: effectiveDisabled ? (rule?.disabledReason || '') : '',
        rule: rule || null
    };
}

export function getOperatorParameterStates(operator, params = null, options = {}) {
    const parameters = Array.isArray(params)
        ? params
        : Array.isArray(operator?.parameters)
            ? operator.parameters
            : Array.isArray(operator?.Parameters)
                ? operator.Parameters
                : [];

    return new Map(parameters.map(param => {
        const name = getParameterRawName(param);
        return [normalizeParameterName(name), getParameterEffectiveState(operator, param, options)];
    }));
}

export function shouldIncludePendingParameter(operator, parameterName, options = {}) {
    return !getParameterEffectiveState(operator, parameterName, options).effectiveDisabled;
}

export function collectEffectiveRequiredParameterErrors(operator, params = null, options = {}) {
    const parameters = Array.isArray(params)
        ? params
        : Array.isArray(operator?.parameters)
            ? operator.parameters
            : Array.isArray(operator?.Parameters)
                ? operator.Parameters
                : [];
    const errors = [];
    const handledAtLeastOneGroups = new Set();

    parameters.forEach(param => {
        const name = getParameterRawName(param);
        if (!name) {
            return;
        }

        const state = getParameterEffectiveState(operator, param, options);
        if (state.effectiveDisabled || !state.effectiveRequired) {
            return;
        }

        const rule = state.rule;
        if (Array.isArray(rule?.atLeastOneOf) && rule.atLeastOneOf.length > 1) {
            const groupKey = `${normalizeOperatorType(operator)}:${rule.atLeastOneOf.map(normalizeParameterName).sort().join('|')}`;
            if (handledAtLeastOneGroups.has(groupKey)) {
                return;
            }
            handledAtLeastOneGroups.add(groupKey);
            if (!hasAnyPeerValue(operator, rule.atLeastOneOf, options.values || null)) {
                errors.push({
                    name,
                    parameterNames: rule.atLeastOneOf,
                    kind: 'atLeastOneOf',
                    message: '相机采集模式必须选择相机绑定'
                });
            }
            return;
        }

        const value = getOperatorParameterValue(operator, name, options.values || null);
        if (isEmptyParameterValue(value)) {
            errors.push({
                name,
                parameterNames: [name],
                kind: 'required',
                message: `${options.getLabel?.(param, name) || name} 为必填项`
            });
        }
    });

    return errors;
}
