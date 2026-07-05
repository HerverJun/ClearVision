import inspectionController from '../inspection/inspectionController.js';
import projectManager from '../project/projectManager.js';
import {
    GLOBAL_VARIABLE_TYPES,
    createEmptyGlobalVariableSchema,
    createGlobalVariableDefinition,
    createVariableDraft,
    formatGlobalVariableValue,
    getTypeLabel,
    normalizeGlobalVariableSchema,
    serializeVariableDraft,
    validateVariableDraft
} from './globalVariableStore.js';

export const GLOBAL_VARIABLES_CAPABILITY_OWNER_ID = 'global-variables-capability-v2';

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.getElementById(target) : null;
    }

    return target;
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function createUuid() {
    if (globalThis.crypto?.randomUUID) {
        return globalThis.crypto.randomUUID();
    }

    return `gv-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

function cloneSchema(schema) {
    return normalizeGlobalVariableSchema(
        typeof structuredClone === 'function'
            ? structuredClone(schema || createEmptyGlobalVariableSchema())
            : JSON.parse(JSON.stringify(schema || createEmptyGlobalVariableSchema())));
}

function normalizePreviewDescriptor(descriptor) {
    if (!descriptor || typeof descriptor !== 'object') {
        return null;
    }

    const identity = descriptor.identity || descriptor.Identity || {};
    const operatorId = identity.targetNodeId || identity.TargetNodeId || descriptor.operatorId || descriptor.nodeId || '';
    const outputPortId = descriptor.outputPortId || descriptor.OutputPortId || descriptor.portId || '';
    const outputPortName = descriptor.outputPortName || descriptor.OutputPortName || outputPortId || '输出';
    const resultPath = descriptor.resultPath || descriptor.ResultPath || '';

    if (!operatorId || !outputPortId) {
        return null;
    }

    return {
        operatorId,
        outputPortId,
        outputPortName,
        resultPath,
        operatorName: descriptor.operatorName || descriptor.OperatorName || descriptor.nodeTitle || operatorId
    };
}

export class GlobalVariablesCapabilityAdapter {
    constructor({
        projectManagerRef = projectManager,
        inspectionControllerRef = inspectionController
    } = {}) {
        this.projectManager = projectManagerRef;
        this.inspectionController = inspectionControllerRef;
    }

    getCurrentProject() {
        return this.projectManager.getCurrentProject?.() || null;
    }

    getRuntimeState() {
        return this.inspectionController.getState?.() || {};
    }

    subscribeRuntimeState(listener) {
        return this.inspectionController.subscribeState?.(listener) || (() => {});
    }

    updateSchema(schema) {
        const normalized = normalizeGlobalVariableSchema(schema);
        this.projectManager.updateGlobalVariables?.(normalized);
        return normalized;
    }

    async saveSchema(schema, expectedProjectId = this.getCurrentProject()?.id || null) {
        const normalized = this.updateSchema(schema);
        if (typeof this.projectManager.saveGlobalVariables !== 'function') {
            return normalized;
        }

        return await this.projectManager.saveGlobalVariables(normalized, expectedProjectId);
    }
}

export function createGlobalVariablesCapabilityAdapter(options = {}) {
    return new GlobalVariablesCapabilityAdapter(options);
}

export class GlobalVariablesCapabilityOwner {
    constructor(container, {
        adapter,
        showToast = () => {}
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('GlobalVariablesCapabilityOwner requires a container.');
        }
        if (!adapter) {
            throw new Error('GlobalVariablesCapabilityOwner requires an adapter.');
        }

        this.adapter = adapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.project = null;
        this.schema = createEmptyGlobalVariableSchema();
        this.selectedVariableId = '';
        this.search = '';
        this.typeFilter = '';
        this.draft = null;
        this.fieldErrors = {};
        this.statusMessage = '';
        this.pendingSave = false;
        this.disposed = false;
        this.isOpen = false;
        this.dialog = null;
        this.unsubscribes = [];
        this.handleClick = this.handleClick.bind(this);
        this.handleInput = this.handleInput.bind(this);
        this.handleChange = this.handleChange.bind(this);

        this.container.dataset.globalVariablesOwner = GLOBAL_VARIABLES_CAPABILITY_OWNER_ID;
        this.container.addEventListener('click', this.handleClick);
        this.container.addEventListener('input', this.handleInput);
        this.container.addEventListener('change', this.handleChange);
        this.unsubscribes.push(this.adapter.subscribeRuntimeState(() => this.render()));
        this.setProject(this.adapter.getCurrentProject());
    }

    async setProject(project) {
        if (this.disposed) {
            return;
        }

        this.project = project || null;
        this.schema = normalizeGlobalVariableSchema(project?.globalVariables || project?.GlobalVariables);
        this.selectedVariableId = this.schema.variables[0]?.id || '';
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.fieldErrors = {};
        this.statusMessage = '';
        this.render();
    }

    query(selector) {
        return this.dialog?.querySelector?.(selector) ||
            this.container?.querySelector?.(selector) ||
            null;
    }

    setSchemaFromExternal(schema) {
        if (this.disposed) {
            return;
        }

        this.schema = normalizeGlobalVariableSchema(schema);
        if (!this.schema.variables.some(variable => variable.id === this.selectedVariableId)) {
            this.selectedVariableId = this.schema.variables[0]?.id || '';
        }
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.render();
    }

    getSelectedVariable() {
        return this.schema.variables.find(variable => variable.id === this.selectedVariableId) || null;
    }

    getVisibleVariables() {
        const query = this.search.trim().toLowerCase();
        return this.schema.variables.filter(variable => {
            const matchesQuery = !query ||
                String(variable.name || '').toLowerCase().includes(query) ||
                String(variable.displayName || '').toLowerCase().includes(query);
            const matchesType = !this.typeFilter || variable.valueType === this.typeFilter;
            return matchesQuery && matchesType;
        });
    }

    isRuntimeLocked() {
        const state = this.adapter.getRuntimeState?.() || {};
        const status = String(state.status || state.Status || '').toLowerCase();
        return state.isRunning === true ||
            state.isRealtime === true ||
            state.isBusy === true ||
            ['starting', 'running', 'stopping'].includes(status);
    }

    handleInput(event) {
        if (this.disposed) {
            return;
        }

        const target = event.target;
        if (target?.matches?.('[data-gv-search]')) {
            this.search = target.value || '';
            this.renderVariableList();
            return;
        }

        if (!target?.matches?.('[data-gv-field]') || !this.draft) {
            return;
        }

        this.draft[target.dataset.gvField] = target.type === 'checkbox' ? target.checked : target.value;
        this.fieldErrors = {};
        this.statusMessage = '变量草稿已修改';
        this.updateStatus();
    }

    handleChange(event) {
        if (this.disposed) {
            return;
        }

        const target = event.target;
        if (target?.matches?.('[data-gv-type-filter]')) {
            this.typeFilter = target.value || '';
            this.renderVariableList();
            return;
        }

        if (target?.matches?.('[data-gv-field]') && this.draft) {
            this.draft[target.dataset.gvField] = target.type === 'checkbox' ? target.checked : target.value;
            this.fieldErrors = {};
            this.statusMessage = '变量草稿已修改';
            this.updateStatus();
        }
    }

    handleClick(event) {
        const action = event.target?.closest?.('[data-gv-action]')?.dataset?.gvAction;
        if (!action || this.disposed) {
            return;
        }

        event.preventDefault();
        if (action === 'open-manager') {
            this.openManager();
        } else if (action === 'close-manager') {
            this.closeManager();
        } else if (action === 'new') {
            this.createDraft();
        } else if (action === 'select') {
            this.selectVariable(event.target.closest('[data-variable-id]')?.dataset?.variableId || '');
        } else if (action === 'save') {
            void this.saveDraft();
        } else if (action === 'delete') {
            void this.deleteSelectedVariable();
        } else if (action === 'discard') {
            this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
            this.fieldErrors = {};
            this.statusMessage = '已放弃未保存修改';
            this.render();
        }
    }

    openManager() {
        if (!this.project) {
            this.showToast('请先打开工程。', 'warning');
            return;
        }

        this.isOpen = true;
        if (!this.selectedVariableId && this.schema.variables.length > 0) {
            this.selectedVariableId = this.schema.variables[0].id;
            this.draft = createVariableDraft(this.getSelectedVariable());
        }
        this.fieldErrors = {};
        this.render();
    }

    closeManager() {
        this.isOpen = false;
        this.removeDialog();
        this.render();
    }

    createDraft() {
        const nextOrder = this.schema.variables.length;
        this.selectedVariableId = '';
        this.draft = createVariableDraft(null, nextOrder);
        this.fieldErrors = {};
        this.statusMessage = '正在创建变量';
        this.render();
    }

    selectVariable(variableId) {
        if (!variableId) {
            return;
        }

        this.selectedVariableId = variableId;
        this.draft = createVariableDraft(this.getSelectedVariable());
        this.fieldErrors = {};
        this.statusMessage = '';
        this.render();
    }

    async saveDraft() {
        if (this.pendingSave || this.isRuntimeLocked() || !this.project?.id || !this.draft) {
            return false;
        }

        const originalVariable = this.getSelectedVariable();
        const errors = validateVariableDraft(this.draft, this.schema, originalVariable);
        if (Object.keys(errors).length > 0) {
            this.fieldErrors = errors;
            this.statusMessage = '变量校验失败';
            this.render();
            return false;
        }

        const serialized = serializeVariableDraft(this.draft, this.schema, originalVariable);
        const nextVariable = serialized.variable;
        if (!serialized.ok || !nextVariable) {
            this.fieldErrors = serialized.errors || {};
            this.statusMessage = '变量校验失败';
            this.render();
            return false;
        }
        const nextSchema = cloneSchema(this.schema);
        const existingIndex = nextSchema.variables.findIndex(variable => variable.id === nextVariable.id);
        if (existingIndex >= 0) {
            nextSchema.variables[existingIndex] = nextVariable;
        } else {
            nextSchema.variables.push(nextVariable);
        }

        this.pendingSave = true;
        this.statusMessage = '正在保存变量';
        this.updateStatus();
        try {
            const saved = await this.adapter.saveSchema(nextSchema, this.project.id);
            this.schema = normalizeGlobalVariableSchema(saved || nextSchema);
            this.selectedVariableId = nextVariable.id;
            this.draft = createVariableDraft(this.getSelectedVariable());
            this.fieldErrors = {};
            this.statusMessage = '变量已保存';
            this.showToast('全局变量已保存', 'success');
            this.render();
            return true;
        } catch (error) {
            this.statusMessage = error?.message || '变量保存失败';
            this.showToast(this.statusMessage, 'error');
            this.render();
            return false;
        } finally {
            this.pendingSave = false;
        }
    }

    async deleteSelectedVariable() {
        if (this.pendingSave || this.isRuntimeLocked() || !this.project?.id || !this.selectedVariableId) {
            return false;
        }

        const nextSchema = cloneSchema(this.schema);
        const variableId = this.selectedVariableId;
        nextSchema.variables = nextSchema.variables.filter(variable => variable.id !== variableId);
        nextSchema.sourceBindings = nextSchema.sourceBindings.filter(binding => binding.variableId !== variableId);
        nextSchema.targetBindings = nextSchema.targetBindings.filter(binding => binding.variableId !== variableId);

        this.pendingSave = true;
        try {
            const saved = await this.adapter.saveSchema(nextSchema, this.project.id);
            this.schema = normalizeGlobalVariableSchema(saved || nextSchema);
            this.selectedVariableId = this.schema.variables[0]?.id || '';
            this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
            this.statusMessage = '变量已删除';
            this.showToast('全局变量已删除', 'success');
            this.render();
            return true;
        } catch (error) {
            this.statusMessage = error?.message || '变量删除失败';
            this.showToast(this.statusMessage, 'error');
            this.render();
            return false;
        } finally {
            this.pendingSave = false;
        }
    }

    async bindPreviewField(descriptor) {
        const normalized = normalizePreviewDescriptor(descriptor);
        if (!normalized || !this.project?.id || this.isRuntimeLocked()) {
            this.showToast('预览字段无法绑定到全局变量', 'warning');
            return false;
        }

        const nextSchema = cloneSchema(this.schema);
        let variable = nextSchema.variables.find(item => item.id === this.selectedVariableId) || nextSchema.variables[0] || null;
        if (!variable) {
            variable = createGlobalVariableDefinition({
                name: `preview_${nextSchema.variables.length + 1}`,
                displayName: normalized.outputPortName || '预览字段',
                valueType: 'String',
                initialValue: '',
                order: nextSchema.variables.length
            });
            nextSchema.variables.push(variable);
        }

        nextSchema.sourceBindings = nextSchema.sourceBindings.filter(binding => binding.variableId !== variable.id);
        nextSchema.sourceBindings.push({
            id: createUuid(),
            variableId: variable.id,
            operatorId: normalized.operatorId,
            outputPortId: normalized.outputPortId,
            operatorName: normalized.operatorName,
            outputPortName: normalized.outputPortName,
            resultPathVersion: 1,
            resultPath: normalized.resultPath,
            conversionMode: 'Exact',
            expression: ''
        });

        const saved = await this.adapter.saveSchema(nextSchema, this.project.id);
        this.schema = normalizeGlobalVariableSchema(saved || nextSchema);
        this.selectedVariableId = variable.id;
        this.draft = createVariableDraft(this.getSelectedVariable());
        this.statusMessage = '预览字段已绑定';
        this.showToast('预览字段已绑定到全局变量', 'success');
        this.render();
        return true;
    }

    render() {
        if (this.disposed || !this.container) {
            return;
        }

        const locked = this.isRuntimeLocked();
        const count = this.schema.variables.length;
        this.container.innerHTML = `
            <section class="global-variable-entry" data-owner="${GLOBAL_VARIABLES_CAPABILITY_OWNER_ID}">
                <div class="global-variable-entry-summary">
                    <strong>全局变量</strong>
                    <span>${this.project ? `${count} 个变量` : '未打开工程'}</span>
                </div>
                <button type="button"
                        class="btn btn-secondary"
                        id="gv-open-manager"
                        data-gv-action="open-manager"
                        ${this.project ? '' : 'disabled'}
                        title="${this.project ? '打开全局变量管理' : '未打开工程'}"
                        aria-label="打开全局变量管理">管理</button>
                ${locked ? '<p class="global-variable-entry-hint">工程运行中，仅可查看变量。</p>' : ''}
            </section>
        `;
        if (this.isOpen && this.project) {
            this.renderDialog();
        } else {
            this.removeDialog();
        }
    }

    renderDialog() {
        this.removeDialog();
        const overlay = document.createElement('div');
        overlay.className = 'gv-manager-overlay show';
        overlay.innerHTML = this.renderDialogHtml();
        overlay.addEventListener('click', this.handleClick);
        overlay.addEventListener('input', this.handleInput);
        overlay.addEventListener('change', this.handleChange);
        document.body.appendChild(overlay);
        this.dialog = overlay;
        this.renderVariableList();
        overlay.querySelector('.gv-manager')?.focus?.();
    }

    removeDialog() {
        if (!this.dialog) {
            return;
        }

        this.dialog.removeEventListener('click', this.handleClick);
        this.dialog.removeEventListener('input', this.handleInput);
        this.dialog.removeEventListener('change', this.handleChange);
        this.dialog.remove();
        this.dialog = null;
    }

    renderDialogHtml() {
        const locked = this.isRuntimeLocked();
        const selected = this.getSelectedVariable();
        return `
            <div class="gv-manager global-variable-capability-manager"
                 role="dialog"
                 aria-modal="true"
                 aria-labelledby="gv-manager-title"
                 tabindex="-1">
                <header class="gv-manager-header">
                    <div>
                        <h2 id="gv-manager-title">全局变量</h2>
                        <p>管理变量结构、初始值和算子绑定。</p>
                    </div>
                    <button type="button" class="gv-icon-button" data-gv-action="close-manager" title="关闭" aria-label="关闭">×</button>
                </header>
                ${locked ? '<div class="gv-warning" role="status">工程运行中，仅可查看变量。</div>' : ''}
                <section class="gv-toolbar global-variable-capability-toolbar">
                    <input class="form-input" data-gv-search value="${escapeHtml(this.search)}" placeholder="搜索变量" ${this.project ? '' : 'disabled'} aria-label="搜索变量">
                    <select class="form-input" data-gv-type-filter ${this.project ? '' : 'disabled'} aria-label="类型筛选">
                        <option value="">全部类型</option>
                        ${GLOBAL_VARIABLE_TYPES.map(type => `<option value="${type}" ${this.typeFilter === type ? 'selected' : ''}>${escapeHtml(getTypeLabel(type))}</option>`).join('')}
                    </select>
                    <button type="button" class="btn btn-primary" data-gv-action="new" ${this.project && !locked ? '' : 'disabled'}>新建</button>
                </section>
                <main class="gv-manager-body global-variable-capability-layout" data-low-height-scroll="true">
                    <aside class="gv-variable-list global-variable-capability-list" data-gv-list></aside>
                    <section class="gv-detail">
                        <form class="global-variable-capability-editor" data-gv-editor>
                            ${this.renderEditor(selected, locked)}
                        </form>
                    </section>
                </main>
                <div class="global-variable-capability-status" data-gv-status aria-live="polite">${escapeHtml(this.statusMessage)}</div>
            </div>
        `;
    }

    renderVariableList() {
        const list = this.query('[data-gv-list]');
        if (!list) {
            return;
        }

        const variables = this.getVisibleVariables();
        if (variables.length === 0) {
            list.innerHTML = '<p class="empty-text">没有匹配的全局变量</p>';
            return;
        }

        list.innerHTML = variables.map(variable => `
            <button type="button"
                    class="gv-variable-row global-variable-capability-item ${variable.id === this.selectedVariableId ? 'selected active' : ''}"
                    data-gv-action="select"
                    data-variable-id="${escapeHtml(variable.id)}">
                <strong class="gv-variable-name">${escapeHtml(variable.displayName || variable.name)}</strong>
                <span class="gv-variable-meta">${escapeHtml(variable.name)} · ${escapeHtml(getTypeLabel(variable.valueType))}</span>
                <small class="gv-variable-value">${escapeHtml(formatGlobalVariableValue(variable.initialValue))}</small>
            </button>
        `).join('');
    }

    renderEditor(selected, locked) {
        if (!this.project) {
            return '<p class="empty-text">未打开工程</p>';
        }

        if (!this.draft) {
            return '<p class="empty-text">请选择或新建变量</p>';
        }

        const disabled = locked || this.pendingSave ? 'disabled' : '';
        const error = key => this.fieldErrors[key] ? `<p class="form-description validation-error">${escapeHtml(this.fieldErrors[key])}</p>` : '';
        const bindingCounts = {
            source: selected ? this.schema.sourceBindings.filter(binding => binding.variableId === selected.id).length : 0,
            target: selected ? this.schema.targetBindings.filter(binding => binding.variableId === selected.id).length : 0
        };

        return `
            <div class="form-group">
                <label class="form-label">变量名</label>
                <input class="form-input" data-gv-field="name" value="${escapeHtml(this.draft.name)}" ${disabled}>
                ${error('name')}
            </div>
            <div class="form-group">
                <label class="form-label">显示名</label>
                <input class="form-input" data-gv-field="displayName" value="${escapeHtml(this.draft.displayName)}" ${disabled}>
                ${error('displayName')}
            </div>
            <div class="form-group">
                <label class="form-label">类型</label>
                <select class="form-select" data-gv-field="valueType" ${disabled}>
                    ${GLOBAL_VARIABLE_TYPES.map(type => `<option value="${type}" ${this.draft.valueType === type ? 'selected' : ''}>${escapeHtml(getTypeLabel(type))}</option>`).join('')}
                </select>
            </div>
            <div class="form-group">
                <label class="form-label">初始值</label>
                <input class="form-input" data-gv-field="initialValueText" value="${escapeHtml(this.draft.initialValueText ?? '')}" ${disabled}>
                ${error('initialValue')}
            </div>
            <div class="form-group">
                <label class="form-label">描述</label>
                <textarea class="form-input" data-gv-field="description" rows="3" ${disabled}>${escapeHtml(this.draft.description || '')}</textarea>
            </div>
            <div class="global-variable-capability-bindings">
                <span>来源绑定 ${bindingCounts.source}</span>
                <span>目标绑定 ${bindingCounts.target}</span>
            </div>
            <div class="global-variable-capability-actions">
                <button type="button" class="btn btn-primary" data-gv-action="save" ${disabled}>保存</button>
                <button type="button" class="btn btn-secondary" data-gv-action="discard" ${disabled}>放弃</button>
                <button type="button" class="btn btn-danger" data-gv-action="delete" ${selected && !disabled ? '' : 'disabled'}>删除</button>
            </div>
        `;
    }

    updateStatus() {
        const status = this.query('[data-gv-status]');
        if (status) {
            status.textContent = this.statusMessage || '';
        }
    }

    destroy() {
        this.dispose();
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        this.unsubscribes.forEach(unsubscribe => {
            try {
                unsubscribe?.();
            } catch {
                // Best-effort cleanup.
            }
        });
        this.unsubscribes = [];
        this.container.removeEventListener('click', this.handleClick);
        this.container.removeEventListener('input', this.handleInput);
        this.container.removeEventListener('change', this.handleChange);
        this.removeDialog();
        delete this.container.dataset.globalVariablesOwner;
        this.container.innerHTML = '';
    }
}

export default GlobalVariablesCapabilityOwner;
