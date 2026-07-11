export const OPERATOR_PARAMETER_RULES = Object.freeze({
    TemplateMatching: Object.freeze({
        parameters: Object.freeze({
            TemplatePath: Object.freeze({
                required: true,
                disabledWhen: Object.freeze({ parameter: 'TemplateId', notEmpty: true }),
                disabledReason: 'Template path is disabled when TemplateId is selected.',
                mutuallyExclusiveGroup: 'template-source',
                atLeastOneOf: Object.freeze(['TemplatePath', 'TemplateId']),
                atLeastOneMessage: 'TemplateMatching requires TemplatePath or TemplateId.'
            }),
            TemplateId: Object.freeze({
                required: true,
                disabledWhen: Object.freeze({ parameter: 'TemplatePath', notEmpty: true }),
                disabledReason: 'TemplateId is disabled when TemplatePath is selected.',
                mutuallyExclusiveGroup: 'template-source',
                atLeastOneOf: Object.freeze(['TemplatePath', 'TemplateId']),
                atLeastOneMessage: 'TemplateMatching requires TemplatePath or TemplateId.'
            }),
            RoiX: Object.freeze({
                disabledWhen: Object.freeze({ parameter: 'UseRoi', equals: false }),
                disabledReason: 'ROI X is disabled when UseRoi is false.'
            }),
            RoiY: Object.freeze({
                disabledWhen: Object.freeze({ parameter: 'UseRoi', equals: false }),
                disabledReason: 'ROI Y is disabled when UseRoi is false.'
            }),
            RoiWidth: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'UseRoi', equals: true }),
                disabledWhen: Object.freeze({ parameter: 'UseRoi', equals: false }),
                disabledReason: 'ROI width is disabled when UseRoi is false.'
            }),
            RoiHeight: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'UseRoi', equals: true }),
                disabledWhen: Object.freeze({ parameter: 'UseRoi', equals: false }),
                disabledReason: 'ROI height is disabled when UseRoi is false.'
            }),
            OriginX: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'OriginMode', equals: 'Custom' }),
                disabledWhen: Object.freeze({ parameter: 'OriginMode', notEquals: 'Custom' }),
                disabledReason: 'Origin X is only editable when OriginMode is Custom.'
            }),
            OriginY: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'OriginMode', equals: 'Custom' }),
                disabledWhen: Object.freeze({ parameter: 'OriginMode', notEquals: 'Custom' }),
                disabledReason: 'Origin Y is only editable when OriginMode is Custom.'
            }),
            AngleStart: Object.freeze({
                disabledWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: false }),
                disabledReason: 'Angle search is disabled when EnablePoseSearch is false.'
            }),
            AngleExtent: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: true }),
                disabledWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: false }),
                disabledReason: 'Angle extent is disabled when EnablePoseSearch is false.'
            }),
            AngleStep: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: true }),
                disabledWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: false }),
                disabledReason: 'Angle step is disabled when EnablePoseSearch is false.'
            }),
            ScaleMin: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: true }),
                disabledWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: false }),
                disabledReason: 'Scale search is disabled when EnablePoseSearch is false.'
            }),
            ScaleMax: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: true }),
                disabledWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: false }),
                disabledReason: 'Scale search is disabled when EnablePoseSearch is false.'
            }),
            ScaleStep: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: true }),
                disabledWhen: Object.freeze({ parameter: 'EnablePoseSearch', equals: false }),
                disabledReason: 'Scale step is disabled when EnablePoseSearch is false.'
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

export function isPendingParameterSentinel(value) {
    const text = String(value ?? '').trim();
    if (text.toLowerCase() === '<pending>') {
        return true;
    }

    if (!text.toLowerCase().startsWith('<pending-') || !text.endsWith('>')) {
        return false;
    }

    const payload = text.slice('<pending-'.length, -1);
    return payload.length > 0 && !/[<>\s]/.test(payload);
}

export function isEmptyParameterValue(value) {
    return value === null ||
        value === undefined ||
        (typeof value === 'string' && (value.trim() === '' || isPendingParameterSentinel(value)));
}

function normalizeConditionComparable(parameterName, value) {
    if (parameterName && normalizeParameterName(parameterName) === 'sourcetype') {
        return normalizeAcquisitionSourceType(value);
    }

    const raw = String(value ?? '').trim();
    const separatorIndex = raw.indexOf('|');
    return (separatorIndex >= 0 ? raw.substring(0, separatorIndex) : raw)
        .trim()
        .toLowerCase();
}

export function normalizeAcquisitionSourceType(value) {
    const raw = String(value || 'File').trim();
    const separatorIndex = raw.indexOf('|');
    const token = (separatorIndex >= 0 ? raw.substring(0, separatorIndex) : raw).trim().toLowerCase();

    if (token === 'camera' || token.includes('cam') || token.includes('相机') || token.includes('摄像')) {
        return 'camera';
    }

    if (token === 'file' ||
        token.includes('image') ||
        token.includes('path') ||
        token.includes('文件') ||
        token.includes('图像') ||
        token.includes('图片') ||
        token.includes('路径')) {
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

function getParameterValueInfo(param) {
    const hasValue = Boolean(param) && (
        Object.prototype.hasOwnProperty.call(param, 'value') ||
        Object.prototype.hasOwnProperty.call(param, 'Value')
    );
    const hasDefault = Boolean(param) && (
        Object.prototype.hasOwnProperty.call(param, 'defaultValue') ||
        Object.prototype.hasOwnProperty.call(param, 'DefaultValue')
    );
    const value = param?.value ?? param?.Value ?? null;
    const defaultValue = param?.defaultValue ?? param?.DefaultValue ?? null;
    return {
        found: Boolean(param),
        explicit: hasValue,
        value: hasValue ? value : defaultValue,
        defaultValue
    };
}

export function getParameterEffectiveValue(param) {
    return getParameterValueInfo(param).value;
}

function getOperatorConstraints(operator) {
    const constraints = operator?.parameterConstraints ?? operator?.ParameterConstraints ?? [];
    return Array.isArray(constraints) ? constraints : [];
}

function getConstraintValue(constraint, camelName, pascalName) {
    return constraint?.[camelName] ?? constraint?.[pascalName] ?? null;
}

function normalizeServerCondition(condition) {
    if (!condition) {
        return null;
    }

    const parameter = getConstraintValue(condition, 'parameter', 'Parameter');
    const comparison = String(getConstraintValue(condition, 'comparison', 'Comparison') || '').toLowerCase();
    const value = getConstraintValue(condition, 'value', 'Value');
    const normalized = { parameter };
    if (comparison === 'equals') normalized.equals = value;
    if (comparison === 'not-equals') normalized.notEquals = value;
    if (comparison === 'empty') normalized.empty = true;
    if (comparison === 'not-empty') normalized.notEmpty = true;
    return normalized;
}

function normalizeServerConditionSet(conditionSet) {
    if (!conditionSet) {
        return null;
    }

    const all = getConstraintValue(conditionSet, 'all', 'All');
    const any = getConstraintValue(conditionSet, 'any', 'Any');
    const branches = [];
    if (Array.isArray(all) && all.length > 0) {
        branches.push({ all: all.map(normalizeServerCondition).filter(Boolean) });
    }
    if (Array.isArray(any) && any.length > 0) {
        branches.push({ any: any.map(normalizeServerCondition).filter(Boolean) });
    }
    if (branches.length === 0) return null;
    return branches.length === 1 ? branches[0] : { all: branches };
}

function findOperatorConstraint(operator, parameterName) {
    const normalizedName = normalizeParameterName(parameterName);
    const constraints = getOperatorConstraints(operator);
    return constraints.find(constraint =>
        String(getConstraintValue(constraint, 'parameter', 'Parameter') || '') === String(parameterName || '')) ||
        constraints.find(constraint =>
            normalizeParameterName(getConstraintValue(constraint, 'parameter', 'Parameter')) === normalizedName) ||
        null;
}

function getConstraintGroupNames(operator, propertyName, groupName) {
    if (!groupName) return [];
    const pascalName = propertyName[0].toUpperCase() + propertyName.slice(1);
    return getOperatorConstraints(operator)
        .filter(constraint => String(getConstraintValue(constraint, propertyName, pascalName) || '') === String(groupName))
        .map(constraint => getConstraintValue(constraint, 'parameter', 'Parameter'))
        .filter(Boolean);
}

function getParameterRuleConditionalDisabled(operator, parameterName, rule, values = null) {
    return Boolean(
        (rule?.enabledWhen && !evaluateCondition(rule.enabledWhen, operator, values)) ||
        (rule?.disabledWhen && evaluateCondition(rule.disabledWhen, operator, values)) ||
        evaluateAnyCondition(rule?.disabledWhenAny, operator, values) ||
        evaluateAllConditions(rule?.disabledWhenAll, operator, values)
    );
}

function getActiveConstraintGroupNames(operator, propertyName, groupName, values = null) {
    return getConstraintGroupNames(operator, propertyName, groupName)
        .filter(name => {
            const rule = getOperatorParameterRule(operator, name);
            return !getParameterRuleConditionalDisabled(operator, name, rule, values);
        });
}

function hasConfiguredMutuallyExclusivePeer(operator, parameterName, groupName, values = null) {
    if (!groupName) return false;
    const normalizedName = normalizeParameterName(parameterName);
    return getActiveConstraintGroupNames(operator, 'mutuallyExclusiveGroup', groupName, values)
        .some(peerName =>
            normalizeParameterName(peerName) !== normalizedName &&
            !isEmptyParameterValue(getOperatorParameterValueDirect(operator, peerName, values))
        );
}

function findObjectKey(values, parameterName) {
    if (!values || typeof values !== 'object') return undefined;
    if (Object.prototype.hasOwnProperty.call(values, parameterName)) return parameterName;
    const normalizedName = normalizeParameterName(parameterName);
    return Object.keys(values).find(key => normalizeParameterName(key) === normalizedName);
}

function getOperatorParameterValueInfoDirect(operator, parameterName, values = null) {
    const normalizedName = normalizeParameterName(parameterName);
    if (!normalizedName) {
        return { found: false, explicit: false, value: null, defaultValue: null };
    }

    if (values && typeof values === 'object') {
        const matchKey = findObjectKey(values, parameterName);
        if (matchKey !== undefined) {
            return { found: true, explicit: true, value: values[matchKey], defaultValue: null };
        }
    }

    const parameters = operator?.parameters ?? operator?.Parameters ?? null;
    if (Array.isArray(parameters)) {
        const parameter = parameters.find(item => getParameterRawName(item) === parameterName) ||
            parameters.find(item => normalizeParameterName(getParameterRawName(item)) === normalizedName);
        return getParameterValueInfo(parameter);
    }

    if (parameters && typeof parameters === 'object') {
        const matchKey = findObjectKey(parameters, parameterName);
        if (matchKey !== undefined) {
            return { found: true, explicit: true, value: parameters[matchKey], defaultValue: null };
        }
    }

    return { found: false, explicit: false, value: null, defaultValue: null };
}

function getOperatorParameterValueDirect(operator, parameterName, values = null) {
    return getOperatorParameterValueInfoDirect(operator, parameterName, values).value;
}

export function getOperatorParameterValue(operator, parameterName, values = null) {
    const constraints = getOperatorConstraints(operator);
    const constraint = findOperatorConstraint(operator, parameterName);
    const aliasFor = getConstraintValue(constraint, 'aliasFor', 'AliasFor');
    const canonicalName = aliasFor || parameterName;
    const canonicalInfo = getOperatorParameterValueInfoDirect(operator, canonicalName, values);
    const aliasNames = constraints.map(item => {
        const itemAliasFor = getConstraintValue(item, 'aliasFor', 'AliasFor');
        return normalizeParameterName(itemAliasFor) === normalizeParameterName(canonicalName)
            ? getConstraintValue(item, 'parameter', 'Parameter')
            : null;
    }).filter(Boolean);

    if (canonicalInfo.explicit) {
        return canonicalInfo.value;
    }

    for (const peerName of aliasNames) {
        const aliasInfo = getOperatorParameterValueInfoDirect(operator, peerName, values);
        if (aliasInfo.explicit) {
            return aliasInfo.value;
        }
    }

    if (canonicalInfo.found) {
        return canonicalInfo.value;
    }

    return getOperatorParameterValueInfoDirect(operator, parameterName, values).value;
}

export function getOperatorParameterRule(operatorOrType, parameterName) {
    const operatorType = normalizeOperatorType(operatorOrType);
    const normalizedName = normalizeParameterName(parameterName);
    const constraint = typeof operatorOrType === 'object'
        ? findOperatorConstraint(operatorOrType, parameterName)
        : null;
    if (constraint) {
        const requiredPolicy = String(getConstraintValue(constraint, 'requiredPolicy', 'RequiredPolicy') || 'metadata').toLowerCase();
        const requiredWhen = normalizeServerConditionSet(getConstraintValue(constraint, 'requiredWhen', 'RequiredWhen'));
        const enabledWhen = normalizeServerConditionSet(getConstraintValue(constraint, 'enabledWhen', 'EnabledWhen'));
        const disabledWhen = normalizeServerConditionSet(getConstraintValue(constraint, 'disabledWhen', 'DisabledWhen'));
        const atLeastOneGroup = getConstraintValue(constraint, 'atLeastOneGroup', 'AtLeastOneGroup');
        const mutuallyExclusiveGroup = getConstraintValue(constraint, 'mutuallyExclusiveGroup', 'MutuallyExclusiveGroup');
        return {
            required: requiredPolicy === 'required' ? true : requiredPolicy === 'optional' ? false : undefined,
            requiredWhen,
            enabledWhen,
            disabledWhen,
            atLeastOneGroup,
            atLeastOneOf: getConstraintGroupNames(operatorOrType, 'atLeastOneGroup', atLeastOneGroup),
            mutuallyExclusiveGroup,
            aliasFor: getConstraintValue(constraint, 'aliasFor', 'AliasFor'),
            deprecated: Boolean(getConstraintValue(constraint, 'deprecated', 'Deprecated')),
            resourceKind: getConstraintValue(constraint, 'resourceKind', 'ResourceKind'),
            reasonCode: getConstraintValue(constraint, 'reasonCode', 'ReasonCode')
        };
    }

    const rules = OPERATOR_PARAMETER_RULES[operatorType]?.parameters || {};
    return Object.entries(rules)
        .find(([name]) => normalizeParameterName(name) === normalizedName)?.[1] || null;
}

function evaluateCondition(condition, operator, values) {
    if (!condition) {
        return false;
    }

    if (Array.isArray(condition.any)) {
        return condition.any.some(item => evaluateCondition(item, operator, values));
    }

    if (Array.isArray(condition.all)) {
        return condition.all.every(item => evaluateCondition(item, operator, values));
    }

    const rawValue = getOperatorParameterValue(operator, condition.parameter, values);
    const normalizedValue = normalizeConditionComparable(condition.parameter, rawValue);
    const isEmpty = isEmptyParameterValue(rawValue);

    if (Object.prototype.hasOwnProperty.call(condition, 'notEmpty')) {
        return !isEmpty === Boolean(condition.notEmpty);
    }

    if (Object.prototype.hasOwnProperty.call(condition, 'empty')) {
        return isEmpty === Boolean(condition.empty);
    }

    if (Object.prototype.hasOwnProperty.call(condition, 'equals')) {
        return normalizedValue === normalizeConditionComparable(condition.parameter, condition.equals);
    }

    if (Object.prototype.hasOwnProperty.call(condition, 'notEquals')) {
        return normalizedValue !== normalizeConditionComparable(condition.parameter, condition.notEquals);
    }

    if (Array.isArray(condition.in)) {
        return condition.in
            .map(item => normalizeConditionComparable(condition.parameter, item))
            .includes(normalizedValue);
    }

    if (Array.isArray(condition.notIn)) {
        return !condition.notIn
            .map(item => normalizeConditionComparable(condition.parameter, item))
            .includes(normalizedValue);
    }

    if (Object.prototype.hasOwnProperty.call(condition, 'truthy')) {
        return Boolean(rawValue) === Boolean(condition.truthy);
    }

    if (Object.prototype.hasOwnProperty.call(condition, 'falsy')) {
        return !Boolean(rawValue) === Boolean(condition.falsy);
    }

    return false;
}

function evaluateAnyCondition(conditions, operator, values) {
    return Array.isArray(conditions) && conditions.some(condition => evaluateCondition(condition, operator, values));
}

function evaluateAllConditions(conditions, operator, values) {
    return Array.isArray(conditions) && conditions.length > 0 &&
        conditions.every(condition => evaluateCondition(condition, operator, values));
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
    const effectiveDisabled = Boolean(
        getParameterRuleConditionalDisabled(operator, parameterName, rule, values) ||
        hasConfiguredMutuallyExclusivePeer(
            operator,
            parameterName,
            rule?.mutuallyExclusiveGroup,
            values
        )
    );
    let effectiveRequired = rawRequired;

    if (typeof rule?.required === 'boolean') {
        effectiveRequired = rule.required;
    }

    if (rule?.requiredWhen) {
        effectiveRequired = evaluateCondition(rule.requiredWhen, operator, values);
    }

    if (Array.isArray(rule?.requiredWhenAny)) {
        effectiveRequired = evaluateAnyCondition(rule.requiredWhenAny, operator, values);
    }

    if (Array.isArray(rule?.requiredWhenAll)) {
        effectiveRequired = evaluateAllConditions(rule.requiredWhenAll, operator, values);
    }

    if (effectiveRequired && Array.isArray(rule?.atLeastOneOf) && rule.atLeastOneOf.length > 1) {
        const activeNames = rule?.atLeastOneGroup
            ? getActiveConstraintGroupNames(
                operator,
                'atLeastOneGroup',
                rule.atLeastOneGroup,
                values
            )
            : rule.atLeastOneOf;
        effectiveRequired = !hasAnyPeerValue(operator, activeNames, values) ||
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
    const state = getParameterEffectiveState(operator, parameterName, options);
    return !state.effectiveDisabled && !state.rule?.aliasFor;
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
            const activeNames = rule?.atLeastOneGroup
                ? getActiveConstraintGroupNames(
                    operator,
                    'atLeastOneGroup',
                    rule.atLeastOneGroup,
                    options.values || null
                )
                : rule.atLeastOneOf;
            const groupKey = `${normalizeOperatorType(operator)}:${activeNames.map(normalizeParameterName).sort().join('|')}`;
            if (handledAtLeastOneGroups.has(groupKey)) {
                return;
            }
            handledAtLeastOneGroups.add(groupKey);
            if (!hasAnyPeerValue(operator, activeNames, options.values || null)) {
                errors.push({
                    name,
                    parameterNames: activeNames,
                    kind: 'atLeastOneOf',
                    message: rule.atLeastOneMessage || `At least one of ${activeNames.join(', ')} is required.`
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
                message: `${options.getLabel?.(param, name) || name} is required.`
            });
        }
    });

    return errors;
}
