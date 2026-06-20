import httpClient from '../../core/messaging/httpClient.js';

export const GLOBAL_VARIABLE_TYPES = Object.freeze(['String', 'Int64', 'Double', 'Boolean']);

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
        min: normalizeNullableNumber(variable.min ?? variable.Min),
        max: normalizeNullableNumber(variable.max ?? variable.Max),
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
        outputPortName: binding.outputPortName ?? binding.OutputPortName ?? ''
    };
}

export function normalizeTargetBinding(binding = {}) {
    return {
        id: binding.id ?? binding.Id ?? '',
        variableId: binding.variableId ?? binding.VariableId ?? '',
        operatorId: binding.operatorId ?? binding.OperatorId ?? '',
        parameterId: binding.parameterId ?? binding.ParameterId ?? '',
        operatorName: binding.operatorName ?? binding.OperatorName ?? '',
        parameterName: binding.parameterName ?? binding.ParameterName ?? ''
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
        min: normalizeNullableNumber(min),
        max: normalizeNullableNumber(max),
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
    } else if (!/^[A-Za-z_][A-Za-z0-9_.-]*$/.test(name)) {
        errors.name = '名称只能包含字母、数字、下划线、点和短横线，且必须以字母或下划线开头。';
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

    const min = normalizeNullableNumber(draft?.minText);
    const max = normalizeNullableNumber(draft?.maxText);
    if (draft?.minText !== '' && min == null) {
        errors.min = '最小值必须是数字。';
    }
    if (draft?.maxText !== '' && max == null) {
        errors.max = '最大值必须是数字。';
    }
    if (min != null && max != null && min > max) {
        errors.max = '最大值必须大于或等于最小值。';
    }
    if ((min != null || max != null) && valueType !== 'Int64' && valueType !== 'Double') {
        errors.min = '只有数值类型支持范围。';
    }

    const coerced = coerceGlobalVariableValue(valueType, draft?.initialValueText);
    if (!coerced.ok) {
        errors.initialValue = coerced.error;
    } else if ((valueType === 'Int64' || valueType === 'Double') && coerced.value != null) {
        if (min != null && coerced.value < min) {
            errors.initialValue = '初始值不能小于最小值。';
        }
        if (max != null && coerced.value > max) {
            errors.initialValue = '初始值不能大于最大值。';
        }
    }

    return errors;
}

export function coerceGlobalVariableValue(valueType, rawValue) {
    const type = normalizeValueType(valueType);
    if (rawValue === null || rawValue === undefined) {
        return { ok: true, value: null, error: '' };
    }

    switch (type) {
        case 'Int64': {
            if (rawValue === '') {
                return { ok: true, value: null, error: '' };
            }
            const value = Number(String(rawValue).trim());
            if (!Number.isFinite(value) || !Number.isInteger(value)) {
                return { ok: false, value: null, error: '请输入整数。' };
            }
            return { ok: true, value, error: '' };
        }
        case 'Double': {
            if (rawValue === '') {
                return { ok: true, value: null, error: '' };
            }
            const value = Number(String(rawValue).trim());
            if (!Number.isFinite(value)) {
                return { ok: false, value: null, error: '请输入数字。' };
            }
            return { ok: true, value, error: '' };
        }
        case 'Boolean': {
            if (typeof rawValue === 'boolean') {
                return { ok: true, value: rawValue, error: '' };
            }
            const text = String(rawValue).trim().toLowerCase();
            if (text === '' || text === 'false' || text === '0' || text === '否') {
                return { ok: true, value: false, error: '' };
            }
            if (text === 'true' || text === '1' || text === '是') {
                return { ok: true, value: true, error: '' };
            }
            return { ok: false, value: null, error: '请选择布尔值。' };
        }
        default:
            return { ok: true, value: String(rawValue), error: '' };
    }
}

export function isVariableCompatibleWithDataType(valueType, dataType) {
    const variableType = normalizeValueType(valueType).toLowerCase();
    const normalizedDataType = String(dataType || '').trim().toLowerCase();
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
        return ['int', 'integer', 'long', 'int64', 'double', 'float', 'number', 'decimal'].includes(normalizedDataType);
    }
    if (variableType === 'boolean') {
        return ['bool', 'boolean'].includes(normalizedDataType);
    }
    return false;
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

function normalizeNullableNumber(value) {
    if (value === null || value === undefined || value === '') {
        return null;
    }
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
}

function createUuid() {
    return globalThis.crypto?.randomUUID?.() || `gv-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
