import httpClient from '../../core/messaging/httpClient.js';

export const GLOBAL_VARIABLE_TYPES = Object.freeze(['String', 'Int64', 'Double', 'Boolean']);
export const GLOBAL_VARIABLE_CONVERSION_MODES = Object.freeze(['Exact', 'Round', 'Floor', 'Ceiling', 'Truncate']);
const MIN_INT64 = -9223372036854775808n;
const MAX_INT64 = 9223372036854775807n;
const INT64_RANGE_ERROR = '超出 Int64 范围。';
const INTEGER_ERROR = '\u8bf7\u8f93\u5165\u6574\u6570\u3002';
const NUMBER_ERROR = '\u8bf7\u8f93\u5165\u6570\u5b57\u3002';
const REQUIRED_NUMBER_ERROR = '请输入初始值。';

export function createEmptyGlobalVariableSchema() {
    return {
        schemaVersion: '1.0',
        variables: [],
        sourceBindings: [],
        targetBindings: []
    };
}

export function normalizeGlobalVariableSchema(schema) {
    return {
        schemaVersion: schema?.schemaVersion || schema?.SchemaVersion || '1.0',
        variables: normalizeArray(schema?.variables ?? schema?.Variables).map(normalizeGlobalVariableDefinition),
        sourceBindings: normalizeArray(schema?.sourceBindings ?? schema?.SourceBindings).map(normalizeSourceBinding),
        targetBindings: normalizeArray(schema?.targetBindings ?? schema?.TargetBindings).map(normalizeTargetBinding)
    };
}

export function normalizeGlobalVariableDefinition(variable = {}) {
    const valueType = normalizeValueType(variable.valueType ?? variable.ValueType ?? 'String');
    const initialValue = variable.initialValue ?? variable.InitialValue;
    return {
        id: variable.id ?? variable.Id ?? '',
        name: String(variable.name ?? variable.Name ?? '').trim(),
        displayName: String(variable.displayName ?? variable.DisplayName ?? variable.name ?? variable.Name ?? '').trim(),
        description: String(variable.description ?? variable.Description ?? ''),
        valueType,
        initialValue: coerceGlobalVariableValue(valueType, initialValue).value,
        min: normalizeNullableNumber(variable.min ?? variable.Min, valueType),
        max: normalizeNullableNumber(variable.max ?? variable.Max, valueType),
        manualWriteAllowed: Boolean(variable.manualWriteAllowed ?? variable.ManualWriteAllowed ?? true),
        includeInResultMetadata: Boolean(variable.includeInResultMetadata ?? variable.IncludeInResultMetadata ?? false),
        order: Number.isFinite(Number(variable.order ?? variable.Order)) ? Number(variable.order ?? variable.Order) : 0
    };
}

export function normalizeSourceBinding(binding = {}) {
    return {
        id: binding.id ?? binding.Id ?? '',
        variableId: binding.variableId ?? binding.VariableId ?? '',
        operatorId: binding.operatorId ?? binding.OperatorId ?? '',
        outputPortId: binding.outputPortId ?? binding.OutputPortId ?? '',
        operatorName: binding.operatorName ?? binding.OperatorName ?? '',
        outputPortName: binding.outputPortName ?? binding.OutputPortName ?? '',
        conversionMode: normalizeConversionMode(binding.conversionMode ?? binding.ConversionMode),
        expression: String(binding.expression ?? binding.Expression ?? '')
    };
}

export function normalizeTargetBinding(binding = {}) {
    return {
        id: binding.id ?? binding.Id ?? '',
        variableId: binding.variableId ?? binding.VariableId ?? '',
        operatorId: binding.operatorId ?? binding.OperatorId ?? '',
        parameterId: binding.parameterId ?? binding.ParameterId ?? '',
        operatorName: binding.operatorName ?? binding.OperatorName ?? '',
        parameterName: binding.parameterName ?? binding.ParameterName ?? '',
        conversionMode: normalizeConversionMode(binding.conversionMode ?? binding.ConversionMode),
        expression: String(binding.expression ?? binding.Expression ?? '')
    };
}

export async function loadGlobalVariableValues(projectId) {
    if (!projectId) {
        return [];
    }

    const values = await httpClient.get(`/projects/${projectId}/global-variable-values`);
    return normalizeArray(values).map(normalizeGlobalVariableValue);
}

export async function saveGlobalVariableSchema(projectId, schema) {
    if (!projectId) {
        throw new Error('工程 ID 不能为空。');
    }

    return normalizeGlobalVariableSchema(
        await httpClient.put(`/projects/${projectId}/global-variables`, normalizeGlobalVariableSchema(schema))
    );
}

export async function writeGlobalVariableValue(projectId, variableId, value) {
    const values = await httpClient.put(`/projects/${projectId}/global-variable-values/${variableId}`, { value });
    return normalizeArray(values).map(normalizeGlobalVariableValue);
}

export async function resetGlobalVariableValues(projectId) {
    const values = await httpClient.post(`/projects/${projectId}/global-variable-values/reset`, {});
    return normalizeArray(values).map(normalizeGlobalVariableValue);
}

export async function resetGlobalVariableValue(projectId, variableId) {
    const values = await httpClient.post(`/projects/${projectId}/global-variable-values/${variableId}/reset`, {});
    return normalizeArray(values).map(normalizeGlobalVariableValue);
}

export function createGlobalVariableDefinition({
    id = '',
    name = '',
    displayName = '',
    description = '',
    valueType = 'String',
    initialValue = '',
    min = null,
    max = null,
    manualWriteAllowed = true,
    includeInResultMetadata = false,
    order = 0
} = {}) {
    const normalizedType = normalizeValueType(valueType);
    return {
        id: id || createUuid(),
        name: String(name || '').trim(),
        displayName: String(displayName || name || '').trim(),
        description: String(description || ''),
        valueType: normalizedType,
        initialValue: coerceGlobalVariableValue(normalizedType, initialValue).value,
        min: normalizeNullableNumber(min, normalizedType),
        max: normalizeNullableNumber(max, normalizedType),
        manualWriteAllowed: Boolean(manualWriteAllowed),
        includeInResultMetadata: Boolean(includeInResultMetadata),
        order: Number.isFinite(Number(order)) ? Number(order) : 0
    };
}

export function createVariableDraft(variable = null, nextOrder = 0) {
    const normalized = variable
        ? normalizeGlobalVariableDefinition(variable)
        : createGlobalVariableDefinition({ order: nextOrder, valueType: 'String', initialValue: '' });
    return {
        ...normalized,
        initialValueText: formatValueForInput(normalized.valueType, normalized.initialValue),
        minText: normalized.min == null ? '' : String(normalized.min),
        maxText: normalized.max == null ? '' : String(normalized.max),
        isNew: !variable
    };
}

export function serializeVariableDraft(draft, schema, originalVariable = null) {
    const errors = validateVariableDraft(draft, schema, originalVariable);
    if (Object.keys(errors).length > 0) {
        return { ok: false, errors, variable: null };
    }

    const valueType = normalizeValueType(draft.valueType);
    const initialValue = coerceGlobalVariableValue(valueType, draft.initialValueText);
    return {
        ok: true,
        errors: {},
        variable: createGlobalVariableDefinition({
            id: draft.id || originalVariable?.id,
            name: draft.name,
            displayName: draft.displayName || draft.name,
            description: draft.description,
            valueType,
            initialValue: initialValue.value,
            min: draft.minText,
            max: draft.maxText,
            manualWriteAllowed: draft.manualWriteAllowed,
            includeInResultMetadata: draft.includeInResultMetadata,
            order: draft.order
        })
    };
}

export function validateVariableDraft(draft, schema, originalVariable = null) {
    const errors = {};
    const name = String(draft?.name || '').trim();
    const valueType = normalizeValueType(draft?.valueType);
    const normalizedSchema = normalizeGlobalVariableSchema(schema);

    if (!name) {
        errors.name = '名称不能为空。';
    } else if (!/^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)*$/.test(name)) {
        errors.name = '名称只能使用字母、数字、下划线和点分段，且每段必须以字母开头。';
    } else {
        const duplicate = normalizedSchema.variables.find(item =>
            sameId(item.id, originalVariable?.id) === false &&
            item.name.toLowerCase() === name.toLowerCase());
        if (duplicate) {
            errors.name = '名称已存在。';
        }
    }

    if (!GLOBAL_VARIABLE_TYPES.includes(valueType)) {
        errors.valueType = '类型不受支持。';
    }

    const minResult = coerceNullableNumber(valueType, draft?.minText);
    const maxResult = coerceNullableNumber(valueType, draft?.maxText);
    const min = minResult.value;
    const max = maxResult.value;
    if (!minResult.ok) {
        errors.min = minResult.error;
    }
    if (!maxResult.ok) {
        errors.max = maxResult.error;
    }
    if (min != null && max != null && compareRangeValues(valueType, min, max) > 0) {
        errors.max = '最大值必须大于或等于最小值。';
    }
    if ((min != null || max != null) && valueType !== 'Int64' && valueType !== 'Double') {
        errors.min = '只有数值类型支持范围。';
    }

    const coerced = coerceGlobalVariableValue(valueType, draft?.initialValueText);
    if (!coerced.ok) {
        errors.initialValue = coerced.error;
    } else if ((valueType === 'Int64' || valueType === 'Double') && coerced.value != null) {
        if (min != null && compareRangeValues(valueType, coerced.value, min) < 0) {
            errors.initialValue = '初始值不能小于最小值。';
        }
        if (max != null && compareRangeValues(valueType, coerced.value, max) > 0) {
            errors.initialValue = '初始值不能大于最大值。';
        }
    }

    return errors;
}

export function coerceGlobalVariableValue(valueType, rawValue) {
    const type = normalizeValueType(valueType);
    if (rawValue === null || rawValue === undefined) {
        if (type === 'Int64' || type === 'Double') {
            return { ok: false, value: null, error: REQUIRED_NUMBER_ERROR };
        }
        return { ok: true, value: null, error: '' };
    }

    switch (type) {
        case 'Int64': {
            if (rawValue === '') {
                return { ok: false, value: null, error: REQUIRED_NUMBER_ERROR };
            }
            return coerceInt64Text(rawValue);
        }
        case 'Double': {
            if (rawValue === '') {
                return { ok: false, value: null, error: REQUIRED_NUMBER_ERROR };
            }
            const value = Number(String(rawValue).trim());
            if (!Number.isFinite(value)) {
                return { ok: false, value: null, error: NUMBER_ERROR };
            }
            return { ok: true, value, error: '' };
        }
        case 'Boolean': {
            if (typeof rawValue === 'boolean') {
                return { ok: true, value: rawValue, error: '' };
            }
            const text = String(rawValue).trim().toLowerCase();
            if (text === '' || text === 'false' || text === '0' || text === '否' || text === 'no') {
                return { ok: true, value: false, error: '' };
            }
            if (text === 'true' || text === '1' || text === '是' || text === 'yes') {
                return { ok: true, value: true, error: '' };
            }
            return { ok: false, value: null, error: '请选择布尔值。' };
        }
        default:
            return { ok: true, value: String(rawValue), error: '' };
    }
}

export function isVariableCompatibleWithDataType(valueType, dataType, conversionMode = 'Exact') {
    const variableType = normalizeValueType(valueType).toLowerCase();
    const normalizedDataType = String(dataType || '').trim().toLowerCase();
    const normalizedConversionMode = normalizeConversionMode(conversionMode);
    const hasExplicitIntegerConversion = ['Round', 'Floor', 'Ceiling', 'Truncate'].includes(normalizedConversionMode);
    if (!normalizedDataType || normalizedDataType === 'any' || normalizedDataType === 'object') {
        return true;
    }

    if (variableType === 'string') {
        return ['string', 'enum', 'select', 'text'].includes(normalizedDataType);
    }
    if (variableType === 'int64') {
        return ['int', 'integer', 'long', 'int64', 'double', 'float', 'number', 'decimal'].includes(normalizedDataType);
    }
    if (variableType === 'double') {
        return ['double', 'float', 'number', 'decimal'].includes(normalizedDataType) ||
            (hasExplicitIntegerConversion && ['int', 'integer', 'long', 'int64'].includes(normalizedDataType));
    }
    if (variableType === 'boolean') {
        return ['bool', 'boolean'].includes(normalizedDataType);
    }
    return false;
}

export function normalizeConversionMode(value) {
    const text = String(value || 'Exact').trim().toLowerCase();
    if (text === 'round') return 'Round';
    if (text === 'floor') return 'Floor';
    if (text === 'ceiling' || text === 'ceil') return 'Ceiling';
    if (text === 'truncate' || text === 'trunc') return 'Truncate';
    return 'Exact';
}

export function normalizeValueType(valueType) {
    const text = String(valueType || 'String').trim().toLowerCase();
    if (text === 'int64' || text === 'int' || text === 'integer' || text === 'long') {
        return 'Int64';
    }
    if (text === 'double' || text === 'float' || text === 'number' || text === 'decimal') {
        return 'Double';
    }
    if (text === 'boolean' || text === 'bool') {
        return 'Boolean';
    }
    return 'String';
}

export function formatValueForInput(valueType, value) {
    if (value === null || value === undefined) {
        return '';
    }
    if (normalizeValueType(valueType) === 'Boolean') {
        return value === true || String(value).toLowerCase() === 'true' ? 'true' : 'false';
    }
    return typeof value === 'object' ? JSON.stringify(value) : String(value);
}

export function formatGlobalVariableValue(value) {
    if (value === null || value === undefined || value === '') {
        return '空';
    }
    if (typeof value === 'boolean') {
        return value ? '是' : '否';
    }
    if (typeof value === 'object') {
        return JSON.stringify(value);
    }
    return String(value);
}

export function getTypeLabel(valueType) {
    switch (normalizeValueType(valueType)) {
        case 'Int64':
            return '整数';
        case 'Double':
            return '小数';
        case 'Boolean':
            return '布尔';
        default:
            return '文本';
    }
}

export function sameId(left, right) {
    return String(left || '').toLowerCase() === String(right || '').toLowerCase();
}

function normalizeGlobalVariableValue(value = {}) {
    return {
        variableId: value.variableId ?? value.VariableId ?? '',
        value: value.value ?? value.Value ?? null,
        version: value.version ?? value.Version ?? 0,
        updatedBy: value.updatedBy ?? value.UpdatedBy ?? '',
        updatedAtUtc: value.updatedAtUtc ?? value.UpdatedAtUtc ?? value.updatedAt ?? value.UpdatedAt ?? '',
        runId: value.runId ?? value.RunId ?? '',
        operatorId: value.operatorId ?? value.OperatorId ?? '',
        operatorName: value.operatorName ?? value.OperatorName ?? ''
    };
}

function normalizeArray(value) {
    return Array.isArray(value) ? value : [];
}

function coerceNullableNumber(valueType, value) {
    if (value === null || value === undefined || value === '') {
        return { ok: true, value: null, error: '' };
    }
    if (normalizeValueType(valueType) === 'Int64') {
        return coerceInt64Text(value);
    }
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) {
        return { ok: false, value: null, error: NUMBER_ERROR };
    }
    return { ok: true, value: parsed, error: '' };
}

function coerceInt64Text(rawValue) {
    if (typeof rawValue === 'bigint') {
        return rawValue >= MIN_INT64 && rawValue <= MAX_INT64
            ? { ok: true, value: rawValue.toString(), error: '' }
            : { ok: false, value: null, error: INT64_RANGE_ERROR };
    }

    if (typeof rawValue === 'number') {
        if (!Number.isFinite(rawValue) || !Number.isInteger(rawValue) || !Number.isSafeInteger(rawValue)) {
            return { ok: false, value: null, error: INTEGER_ERROR };
        }
    }

    const text = String(rawValue).trim();
    if (text === '') {
        return { ok: false, value: null, error: REQUIRED_NUMBER_ERROR };
    }
    if (!/^[+-]?\d+$/.test(text)) {
        return { ok: false, value: null, error: INTEGER_ERROR };
    }

    const normalized = text.replace(/^\+/, '').replace(/^(-?)0+(?=\d)/, '$1');
    const parsed = BigInt(normalized);
    if (parsed < MIN_INT64 || parsed > MAX_INT64) {
        return { ok: false, value: null, error: INT64_RANGE_ERROR };
    }

    return { ok: true, value: parsed.toString(), error: '' };
}

function normalizeNullableNumber(value, valueType = 'Double') {
    if (value === null || value === undefined || value === '') {
        return null;
    }
    if (normalizeValueType(valueType) === 'Int64') {
        const coerced = coerceInt64Text(value);
        return coerced.ok ? coerced.value : null;
    }
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
}

function compareRangeValues(valueType, left, right) {
    if (normalizeValueType(valueType) === 'Int64') {
        const leftValue = BigInt(String(left));
        const rightValue = BigInt(String(right));
        return leftValue === rightValue ? 0 : (leftValue > rightValue ? 1 : -1);
    }

    const leftValue = Number(left);
    const rightValue = Number(right);
    return leftValue === rightValue ? 0 : (leftValue > rightValue ? 1 : -1);
}

function createUuid() {
    return globalThis.crypto?.randomUUID?.() || `gv-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
