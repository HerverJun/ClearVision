import projectManager from '../project/projectManager.js';
import {
    createEmptyGlobalVariableSchema,
    createGlobalVariableDefinition,
    loadGlobalVariableValues,
    normalizeGlobalVariableSchema,
    resetGlobalVariableValues,
    saveGlobalVariableSchema,
    writeGlobalVariableValue
} from './globalVariableStore.js';

export default class GlobalVariablePanel {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.project = null;
        this.schema = createEmptyGlobalVariableSchema();
        this.values = [];
    }

    async setProject(project) {
        this.project = project;
        this.schema = normalizeGlobalVariableSchema(project?.globalVariables || project?.GlobalVariables);
        await this.refreshValues();
        this.render();
    }

    async refreshValues() {
        if (!this.project?.id) {
            this.values = [];
            return;
        }

        this.values = await loadGlobalVariableValues(this.project.id);
    }

    render() {
        if (!this.container) {
            return;
        }

        if (!this.project) {
            this.container.innerHTML = '<div class="empty-text">No project open</div>';
            return;
        }

        const rows = this.schema.variables.map(variable => {
            const value = this.values.find(item => sameId(item.variableId, variable.id));
            const source = this.schema.sourceBindings.find(item => sameId(item.variableId, variable.id));
            const targetCount = this.schema.targetBindings.filter(item => sameId(item.variableId, variable.id)).length;
            return `
                <tr data-variable-id="${escapeHtml(variable.id)}">
                    <td>${escapeHtml(variable.name)}</td>
                    <td>${escapeHtml(variable.valueType)}</td>
                    <td>${escapeHtml(formatValue(variable.initialValue))}</td>
                    <td>${escapeHtml(formatValue(value?.value))}</td>
                    <td>${escapeHtml(source ? `${source.operatorName || source.operatorId}.${source.outputPortName || source.outputPortId}` : '-')}</td>
                    <td>${targetCount}</td>
                    <td>${escapeHtml(value?.updatedBy || 'Initial')}</td>
                    <td>
                        <button type="button" class="btn btn-secondary btn-gv-write">Write</button>
                        <button type="button" class="btn btn-danger btn-gv-delete">Delete</button>
                    </td>
                </tr>
            `;
        }).join('');

        this.container.innerHTML = `
            <section class="global-variable-panel">
                <div class="panel-toolbar">
                    <button type="button" class="btn btn-primary" id="gv-add">Add</button>
                    <button type="button" class="btn btn-secondary" id="gv-refresh">Refresh</button>
                    <button type="button" class="btn btn-secondary" id="gv-reset">Reset values</button>
                    <button type="button" class="btn btn-primary" id="gv-save">Save</button>
                </div>
                <table class="data-table global-variable-table">
                    <thead>
                        <tr>
                            <th>Name</th>
                            <th>Type</th>
                            <th>Initial</th>
                            <th>Current</th>
                            <th>Source</th>
                            <th>Targets</th>
                            <th>Updated by</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>${rows || '<tr><td colspan="8" class="empty-text">No global variables</td></tr>'}</tbody>
                </table>
            </section>
        `;

        this.addSourceBindingButton();
        this.bindEvents();
    }

    bindEvents() {
        this.container.querySelector('#gv-add')?.addEventListener('click', () => this.addVariable());
        this.container.querySelector('#gv-save')?.addEventListener('click', () => this.save());
        this.container.querySelector('#gv-source')?.addEventListener('click', () => this.configureSourceBinding());
        this.container.querySelector('#gv-refresh')?.addEventListener('click', async () => {
            await this.refreshValues();
            this.render();
        });
        this.container.querySelector('#gv-reset')?.addEventListener('click', async () => {
            this.values = await resetGlobalVariableValues(this.project.id);
            this.render();
        });

        this.container.querySelectorAll('.btn-gv-delete').forEach(button => {
            button.addEventListener('click', event => {
                const variableId = event.currentTarget.closest('tr')?.dataset.variableId;
                this.deleteVariable(variableId);
            });
        });

        this.container.querySelectorAll('.btn-gv-write').forEach(button => {
            button.addEventListener('click', async event => {
                const variableId = event.currentTarget.closest('tr')?.dataset.variableId;
                await this.writeValue(variableId);
            });
        });
    }

    addVariable() {
        const name = window.prompt('Variable name, for example judge.expected_count');
        if (!name) {
            return;
        }

        const valueType = window.prompt('Type: String / Int64 / Double / Boolean', 'String') || 'String';
        const initialValue = window.prompt('Initial value', valueType === 'Boolean' ? 'false' : '0') ?? '';
        const definition = createGlobalVariableDefinition({
            name: name.trim(),
            displayName: name.trim(),
            valueType,
            initialValue
        });
        definition.order = this.schema.variables.length;
        this.schema.variables.push(definition);
        projectManager.updateGlobalVariables(this.schema);
        this.render();
    }

    deleteVariable(variableId) {
        const referenced = this.schema.sourceBindings.some(item => sameId(item.variableId, variableId)) ||
            this.schema.targetBindings.some(item => sameId(item.variableId, variableId));
        if (referenced) {
            window.alert('This variable is still referenced and cannot be deleted.');
            return;
        }

        this.schema.variables = this.schema.variables.filter(item => !sameId(item.id, variableId));
        projectManager.updateGlobalVariables(this.schema);
        this.render();
    }

    addSourceBindingButton() {
        const toolbar = this.container?.querySelector('.panel-toolbar');
        if (!toolbar || toolbar.querySelector('#gv-source')) {
            return;
        }

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-secondary';
        button.id = 'gv-source';
        button.textContent = 'Source';
        toolbar.appendChild(button);
    }

    configureSourceBinding() {
        if (this.schema.variables.length === 0) {
            window.alert('No global variables are defined.');
            return;
        }

        const variables = this.schema.variables
            .map((variable, index) => `${index + 1}. ${variable.name}`)
            .join('\n');
        const variableRaw = window.prompt(`Select variable number:\n${variables}`, '1');
        if (variableRaw == null) {
            return;
        }

        const variable = this.schema.variables[Number.parseInt(variableRaw, 10) - 1];
        if (!variable) {
            window.alert('Invalid variable selection.');
            return;
        }

        const outputs = getFlowOutputs(this.project)
            .filter(output => isOutputCompatible(variable.valueType, output.dataType));
        if (outputs.length === 0) {
            window.alert('No output ports are available in the current flow.');
            return;
        }

        const sourceMenu = outputs
            .map((item, index) => `${index + 1}. ${item.operatorName}.${item.outputPortName} (${item.dataType || '-'})`)
            .join('\n');
        const sourceRaw = window.prompt(`Select source output number, or leave empty to clear:\n${sourceMenu}`, '');
        if (sourceRaw == null) {
            return;
        }

        this.schema.sourceBindings = this.schema.sourceBindings.filter(item => !sameId(item.variableId, variable.id));
        if (sourceRaw.trim() !== '') {
            const selected = outputs[Number.parseInt(sourceRaw, 10) - 1];
            if (!selected) {
                window.alert('Invalid output selection.');
                return;
            }

            this.schema.sourceBindings.push({
                id: crypto.randomUUID(),
                variableId: variable.id,
                operatorId: selected.operatorId,
                outputPortId: selected.outputPortId,
                operatorName: selected.operatorName,
                outputPortName: selected.outputPortName
            });
        }

        projectManager.updateGlobalVariables(this.schema);
        this.render();
    }

    async writeValue(variableId) {
        const variable = this.schema.variables.find(item => sameId(item.id, variableId));
        if (!variable) {
            return;
        }

        if (!variable.manualWriteAllowed) {
            window.alert('This variable does not allow manual writes.');
            return;
        }

        const raw = window.prompt('Current value', '');
        if (raw == null) {
            return;
        }

        this.values = await writeGlobalVariableValue(this.project.id, variableId, coerceValue(variable.valueType, raw));
        this.render();
    }

    async save() {
        const saved = await saveGlobalVariableSchema(this.project.id, this.schema);
        this.schema = normalizeGlobalVariableSchema(saved);
        projectManager.updateGlobalVariables(this.schema);
        await this.refreshValues();
        this.render();
    }
}

function sameId(left, right) {
    return String(left || '').toLowerCase() === String(right || '').toLowerCase();
}

function formatValue(value) {
    if (value == null) {
        return '';
    }

    if (typeof value === 'object') {
        return JSON.stringify(value);
    }

    return String(value);
}

function coerceValue(valueType, raw) {
    switch (String(valueType || '').toLowerCase()) {
        case 'int64':
            return Number.parseInt(raw || '0', 10) || 0;
        case 'double':
            return Number(raw || 0) || 0;
        case 'boolean':
            return raw === true || String(raw).toLowerCase() === 'true';
        default:
            return raw == null ? '' : String(raw);
    }
}

function getFlowOutputs(project) {
    const flow = project?.flow || project?.Flow;
    const operators = Array.isArray(flow?.operators) ? flow.operators : (flow?.Operators || []);
    return operators.flatMap(op => {
        const outputs = Array.isArray(op.outputPorts) ? op.outputPorts : (op.OutputPorts || []);
        return outputs.map(port => ({
            operatorId: op.id || op.Id,
            operatorName: op.name || op.Name || op.type || op.Type || '',
            outputPortId: port.id || port.Id,
            outputPortName: port.name || port.Name || '',
            dataType: port.dataType || port.DataType || ''
        }));
    }).filter(item => item.operatorId && item.outputPortId);
}

function isOutputCompatible(valueType, dataType) {
    const normalizedValueType = String(valueType || '').toLowerCase();
    const normalizedDataType = String(dataType || '').toLowerCase();
    if (normalizedDataType === 'any') {
        return true;
    }

    switch (normalizedValueType) {
        case 'string':
            return normalizedDataType === 'string';
        case 'int64':
            return normalizedDataType === 'integer';
        case 'double':
            return normalizedDataType === 'integer' || normalizedDataType === 'float' || normalizedDataType === 'double' || normalizedDataType === 'number';
        case 'boolean':
            return normalizedDataType === 'boolean' || normalizedDataType === 'bool';
        default:
            return false;
    }
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}
