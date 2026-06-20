import httpClient from '../../core/messaging/httpClient.js';

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
        variables: Array.isArray(schema?.variables) ? schema.variables : (schema?.Variables || []),
        sourceBindings: Array.isArray(schema?.sourceBindings) ? schema.sourceBindings : (schema?.SourceBindings || []),
        targetBindings: Array.isArray(schema?.targetBindings) ? schema.targetBindings : (schema?.TargetBindings || [])
    };
}

export async function loadGlobalVariableValues(projectId) {
    if (!projectId) {
        return [];
    }

    return await httpClient.get(`/projects/${projectId}/global-variable-values`);
}

export async function saveGlobalVariableSchema(projectId, schema) {
    if (!projectId) {
        throw new Error('Project id is required.');
    }

    return await httpClient.put(`/projects/${projectId}/global-variables`, normalizeGlobalVariableSchema(schema));
}

export async function writeGlobalVariableValue(projectId, variableId, value) {
    return await httpClient.put(`/projects/${projectId}/global-variable-values/${variableId}`, { value });
}

export async function resetGlobalVariableValues(projectId) {
    return await httpClient.post(`/projects/${projectId}/global-variable-values/reset`, {});
}

export function createGlobalVariableDefinition({ name, displayName, valueType, initialValue }) {
    return {
        id: crypto.randomUUID(),
        name,
        displayName: displayName || name,
        description: '',
        valueType,
        initialValue: coerceInitialValue(valueType, initialValue),
        min: null,
        max: null,
        manualWriteAllowed: true,
        includeInResultMetadata: false,
        order: 0
    };
}

function coerceInitialValue(valueType, rawValue) {
    switch (String(valueType || '').toLowerCase()) {
        case 'int64':
            return Number.parseInt(rawValue || '0', 10) || 0;
        case 'double':
            return Number(rawValue || 0) || 0;
        case 'boolean':
            return rawValue === true || String(rawValue).toLowerCase() === 'true';
        default:
            return rawValue == null ? '' : String(rawValue);
    }
}
