export const OPERATOR_PARAMETER_RULES = Object.freeze({
    ImageAcquisition: Object.freeze({
        parameters: Object.freeze({
            SourceType: Object.freeze({
                required: true
            }),
            FilePath: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'SourceType', equals: 'file' }),
                disabledWhen: Object.freeze({ parameter: 'SourceType', equals: 'camera' }),
                disabledReason: 'File path is disabled while SourceType is camera.',
                mutuallyExclusiveGroup: 'image-source'
            }),
            CameraId: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'SourceType', equals: 'camera' }),
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'SourceType', equals: 'file' }),
                    Object.freeze({ parameter: 'CameraBindingId', notEmpty: true })
                ]),
                disabledReason: 'Camera id is disabled in file mode or when CameraBindingId is set.',
                mutuallyExclusiveGroup: 'image-source',
                atLeastOneOf: Object.freeze(['CameraId', 'CameraBindingId']),
                atLeastOneMessage: 'Camera mode requires CameraId or CameraBindingId.'
            }),
            CameraBindingId: Object.freeze({
                requiredWhen: Object.freeze({ parameter: 'SourceType', equals: 'camera' }),
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'SourceType', equals: 'file' }),
                    Object.freeze({ parameter: 'CameraId', notEmpty: true })
                ]),
                disabledReason: 'Camera binding is disabled in file mode or when CameraId is set.',
                mutuallyExclusiveGroup: 'image-source',
                atLeastOneOf: Object.freeze(['CameraId', 'CameraBindingId']),
                atLeastOneMessage: 'Camera mode requires CameraId or CameraBindingId.'
            }),
            ExposureTime: Object.freeze({
                disabledWhen: Object.freeze({ parameter: 'SourceType', equals: 'file' }),
                disabledReason: 'Camera exposure is disabled for file acquisition.'
            }),
            Gain: Object.freeze({
                disabledWhen: Object.freeze({ parameter: 'SourceType', equals: 'file' }),
                disabledReason: 'Camera gain is disabled for file acquisition.'
            }),
            TriggerMode: Object.freeze({
                disabledWhen: Object.freeze({ parameter: 'SourceType', equals: 'file' }),
                disabledReason: 'Trigger mode is disabled for file acquisition.'
            })
        })
    }),
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
    }),
    DeepLearning: Object.freeze({
        parameters: Object.freeze({
            ModelPath: Object.freeze({
                required: true,
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'ModelId', notEmpty: true }),
                    Object.freeze({ parameter: 'ModelCatalogPath', notEmpty: true })
                ]),
                disabledReason: 'ModelPath is disabled when ModelId or ModelCatalogPath is selected.',
                mutuallyExclusiveGroup: 'model-source',
                atLeastOneOf: Object.freeze(['ModelPath', 'ModelId', 'ModelCatalogPath']),
                atLeastOneMessage: 'DeepLearning requires ModelPath, ModelId, or ModelCatalogPath.'
            }),
            ModelId: Object.freeze({
                required: true,
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'ModelPath', notEmpty: true }),
                    Object.freeze({ parameter: 'ModelCatalogPath', notEmpty: true })
                ]),
                disabledReason: 'ModelId is disabled when ModelPath or ModelCatalogPath is selected.',
                mutuallyExclusiveGroup: 'model-source',
                atLeastOneOf: Object.freeze(['ModelPath', 'ModelId', 'ModelCatalogPath']),
                atLeastOneMessage: 'DeepLearning requires ModelPath, ModelId, or ModelCatalogPath.'
            }),
            ModelCatalogPath: Object.freeze({
                required: true,
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'ModelPath', notEmpty: true }),
                    Object.freeze({ parameter: 'ModelId', notEmpty: true })
                ]),
                disabledReason: 'ModelCatalogPath is disabled when ModelPath or ModelId is selected.',
                mutuallyExclusiveGroup: 'model-source',
                atLeastOneOf: Object.freeze(['ModelPath', 'ModelId', 'ModelCatalogPath']),
                atLeastOneMessage: 'DeepLearning requires ModelPath, ModelId, or ModelCatalogPath.'
            }),
            GpuDeviceId: Object.freeze({
                disabledWhen: Object.freeze({ parameter: 'UseGpu', equals: false }),
                disabledReason: 'GPU device id is disabled when UseGpu is false.'
            }),
            EnableInternalNms: Object.freeze({
                disabledWhen: Object.freeze({ parameter: 'OutputFormat', equals: 'EndToEndNms' }),
                disabledReason: 'Internal NMS is owned by the exported model when OutputFormat is EndToEndNms.'
            }),
            NmsIouThreshold: Object.freeze({
                requiredWhenAll: Object.freeze([
                    Object.freeze({ parameter: 'OutputFormat', equals: 'RawYolo' }),
                    Object.freeze({ parameter: 'EnableInternalNms', equals: true })
                ]),
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'OutputFormat', equals: 'EndToEndNms' }),
                    Object.freeze({ parameter: 'EnableInternalNms', equals: false })
                ]),
                disabledReason: 'NMS IoU is disabled when model-side NMS is trusted or internal NMS is off.'
            })
        })
    }),
    ResultOutput: Object.freeze({
        parameters: Object.freeze({
            Channel: Object.freeze({
                required: true,
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'OutputChannel', notEmpty: true }),
                    Object.freeze({ parameter: 'OutputChannelId', notEmpty: true })
                ]),
                disabledReason: 'Channel is disabled when a concrete output channel id is selected.',
                mutuallyExclusiveGroup: 'output-channel',
                atLeastOneOf: Object.freeze(['Channel', 'OutputChannel', 'OutputChannelId']),
                atLeastOneMessage: 'ResultOutput requires Channel, OutputChannel, or OutputChannelId.'
            }),
            OutputChannel: Object.freeze({
                required: true,
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'Channel', notEmpty: true }),
                    Object.freeze({ parameter: 'OutputChannelId', notEmpty: true })
                ]),
                disabledReason: 'OutputChannel is disabled when Channel or OutputChannelId is selected.',
                mutuallyExclusiveGroup: 'output-channel',
                atLeastOneOf: Object.freeze(['Channel', 'OutputChannel', 'OutputChannelId']),
                atLeastOneMessage: 'ResultOutput requires Channel, OutputChannel, or OutputChannelId.'
            }),
            OutputChannelId: Object.freeze({
                required: true,
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'Channel', notEmpty: true }),
                    Object.freeze({ parameter: 'OutputChannel', notEmpty: true })
                ]),
                disabledReason: 'OutputChannelId is disabled when Channel or OutputChannel is selected.',
                mutuallyExclusiveGroup: 'output-channel',
                atLeastOneOf: Object.freeze(['Channel', 'OutputChannel', 'OutputChannelId']),
                atLeastOneMessage: 'ResultOutput requires Channel, OutputChannel, or OutputChannelId.'
            }),
            FilePath: Object.freeze({
                requiredWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'Channel', equals: 'file' }),
                    Object.freeze({ parameter: 'OutputChannel', equals: 'file' })
                ]),
                disabledWhenAll: Object.freeze([
                    Object.freeze({ parameter: 'Channel', notEquals: 'file' }),
                    Object.freeze({ parameter: 'OutputChannel', notEquals: 'file' })
                ]),
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'OutputPath', notEmpty: true })
                ]),
                disabledReason: 'File path is enabled only for file output.',
                mutuallyExclusiveGroup: 'file-output',
                atLeastOneOf: Object.freeze(['FilePath', 'OutputPath']),
                atLeastOneMessage: 'File output requires FilePath or OutputPath.'
            }),
            OutputPath: Object.freeze({
                requiredWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'Channel', equals: 'file' }),
                    Object.freeze({ parameter: 'OutputChannel', equals: 'file' })
                ]),
                disabledWhenAll: Object.freeze([
                    Object.freeze({ parameter: 'Channel', notEquals: 'file' }),
                    Object.freeze({ parameter: 'OutputChannel', notEquals: 'file' })
                ]),
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'FilePath', notEmpty: true })
                ]),
                disabledReason: 'Output path is enabled only for file output.',
                mutuallyExclusiveGroup: 'file-output',
                atLeastOneOf: Object.freeze(['FilePath', 'OutputPath']),
                atLeastOneMessage: 'File output requires FilePath or OutputPath.'
            }),
            PlcAddress: Object.freeze({
                requiredWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'Channel', equals: 'plc' }),
                    Object.freeze({ parameter: 'OutputChannel', equals: 'plc' })
                ]),
                disabledWhenAll: Object.freeze([
                    Object.freeze({ parameter: 'Channel', notEquals: 'plc' }),
                    Object.freeze({ parameter: 'OutputChannel', notEquals: 'plc' })
                ]),
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'PLCParameters', notEmpty: true })
                ]),
                disabledReason: 'PLC metadata is enabled only for PLC output review.',
                mutuallyExclusiveGroup: 'plc-output',
                atLeastOneOf: Object.freeze(['PlcAddress', 'PLCParameters']),
                atLeastOneMessage: 'PLC output requires PlcAddress or PLCParameters.'
            }),
            PLCParameters: Object.freeze({
                requiredWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'Channel', equals: 'plc' }),
                    Object.freeze({ parameter: 'OutputChannel', equals: 'plc' })
                ]),
                disabledWhenAll: Object.freeze([
                    Object.freeze({ parameter: 'Channel', notEquals: 'plc' }),
                    Object.freeze({ parameter: 'OutputChannel', notEquals: 'plc' })
                ]),
                disabledWhenAny: Object.freeze([
                    Object.freeze({ parameter: 'PlcAddress', notEmpty: true })
                ]),
                disabledReason: 'PLC metadata is enabled only for PLC output review.',
                mutuallyExclusiveGroup: 'plc-output',
                atLeastOneOf: Object.freeze(['PlcAddress', 'PLCParameters']),
                atLeastOneMessage: 'PLC output requires PlcAddress or PLCParameters.'
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

    if (token === 'camera' || token.includes('cam')) {
        return 'camera';
    }

    if (token === 'file' || token.includes('image') || token.includes('path')) {
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
        (rule?.disabledWhen && evaluateCondition(rule.disabledWhen, operator, values)) ||
        evaluateAnyCondition(rule?.disabledWhenAny, operator, values) ||
        evaluateAllConditions(rule?.disabledWhenAll, operator, values)
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
                    message: rule.atLeastOneMessage || `At least one of ${rule.atLeastOneOf.join(', ')} is required.`
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
