import serviceRegistry from '../../core/app/serviceRegistry.js';
import inspectionController from '../inspection/inspectionController.js';
import projectManager from '../project/projectManager.js';
import { showToast as showSharedToast } from '../../shared/components/uiComponents.js';
import {
    GLOBAL_VARIABLE_TYPES,
    coerceGlobalVariableValue,
    createEmptyGlobalVariableSchema,
    createVariableDraft,
    formatGlobalVariableValue,
    formatValueForInput,
    getTypeLabel,
    isVariableCompatibleWithDataType,
    normalizeGlobalVariableSchema,
    normalizeValueType,
    resetGlobalVariableValue,
    resetGlobalVariableValues,
    sameId,
    serializeVariableDraft,
    loadGlobalVariableValues,
    writeGlobalVariableValue
} from './globalVariableStore.js';

const LOCKED_RUNTIME_STATES = new Set(['starting', 'running', 'stopping']);
const ALL_TYPES_LABEL = '全部类型';
const ALL_SOURCES_LABEL = '全部来源';
const FIXED_SOURCE_LABEL = '固定初始值';
const OPERATOR_SOURCE_LABEL = '算子输出';
const TYPE_FILTERS = [ALL_TYPES_LABEL, ...GLOBAL_VARIABLE_TYPES.map(getTypeLabel)];
const SOURCE_FILTERS = [ALL_SOURCES_LABEL, FIXED_SOURCE_LABEL, OPERATOR_SOURCE_LABEL];
const RUNTIME_STATE_POLL_INTERVAL_MS = 1500;

export default class GlobalVariablePanel {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        this.project = null;
        this.baselineSchema = createEmptyGlobalVariableSchema();
        this.schema = createEmptyGlobalVariableSchema();
        this.values = [];
        this.isOpen = false;
        this.selectedVariableId = '';
        this.draft = null;
        this.dirty = false;
        this.loading = false;
        this.errorMessage = '';
        this.fieldErrors = {};
        this.filters = { search: '', type: '全部类型', source: '全部来源' };
        this.pendingAction = '';
        this.requestSerial = 0;
        this.dialog = null;
        this.options = options;
        this.runtimeState = options.getRuntimeState?.() || inspectionController.getState?.() || {};
        this.runtimeStateSource = 'local';
        this.endpointTerminalState = null;
        this.runtimeStateLoading = false;
        this.unsubscribeInspectionState = null;
        this.runtimeStatePollTimer = null;
        this.runtimeStateRequestSerial = 0;
        this.subscribeRuntimeState();
    }

    async setProject(project) {
        const requestId = ++this.requestSerial;
        this.stopRuntimeStatePolling({ cancelRequests: true });
        this.project = project || null;
        this.runtimeState = this.options.getRuntimeState?.() || inspectionController.getState?.() || {};
        this.runtimeStateSource = 'local';
        this.endpointTerminalState = null;
        this.runtimeStateLoading = false;
        this.rebuildBaseline(project?.globalVariables || project?.GlobalVariables);
        this.values = [];
        this.errorMessage = '';
        this.selectedVariableId = this.schema.variables[0]?.id || '';
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.dirty = false;
        this.render();

        if (!this.project?.id) {
            return;
        }

        try {
            await this.refreshValues({ requestId, render: false });
        } catch (error) {
            this.errorMessage = this.toUserMessage(error, '当前值加载失败。');
        }

        if (requestId === this.requestSerial) {
            this.render();
        }
    }

    async refreshValues({ requestId = ++this.requestSerial, render = true } = {}) {
        if (!this.project?.id) {
            this.values = [];
            return [];
        }

        const values = await loadGlobalVariableValues(this.project.id);
        if (requestId !== this.requestSerial) {
            return this.values;
        }

        this.values = values;
        if (render) {
            this.render();
            this.toast('当前值已刷新。', 'success');
        }
        return values;
    }

    render() {
        if (!this.container) {
            return;
        }

        const count = this.schema.variables.length;
        const locked = this.isRuntimeLocked();
        this.container.innerHTML = `
            <section class="global-variable-entry">
                <div class="global-variable-entry-summary">
                    <strong>全局变量</strong>
                    <span>${this.project ? `${count} 个变量` : '未打开工程'}</span>
                </div>
                <button type="button" class="btn btn-secondary" id="gv-open-manager" ${this.project ? '' : 'disabled'} title="打开全局变量管理" aria-label="打开全局变量管理">管理</button>
                ${locked ? '<p class="global-variable-entry-hint">工程运行中，仅可查看和刷新。</p>' : ''}
            </section>
        `;
        this.container.querySelector('#gv-open-manager')?.addEventListener('click', () => this.openManager());

        if (this.isOpen) {
            this.renderDialog();
        } else {
            this.removeDialog();
        }
    }

    openManager() {
        if (!this.project) {
            this.toast('请先打开工程。', 'warning');
            return;
        }

        this.isOpen = true;
        this.rebuildBaseline(this.project?.globalVariables || this.project?.GlobalVariables);
        if (!this.selectedVariableId && this.schema.variables.length > 0) {
            this.selectedVariableId = this.schema.variables[0].id;
        }
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.fieldErrors = {};
        this.render();
        void this.refreshRuntimeStateOnOpen(this.project.id);
    }

    async closeManager() {
        if (this.dirty) {
            const choice = await this.requestChoice('存在未保存修改', '关闭前是否保存当前变量编辑？', [
                { value: 'save', text: '保存' },
                { value: 'discard', text: '放弃' },
                { value: 'cancel', text: '取消' }
            ]);
            if (choice === 'cancel') {
                return false;
            }
            if (choice === 'save') {
                const saved = await this.save();
                if (!saved) {
                    return false;
                }
            } else {
                this.discardDraft();
            }
        }

        this.isOpen = false;
        this.stopRuntimeStatePolling({ cancelRequests: true });
        this.runtimeStateLoading = false;
        this.removeDialog();
        this.render();
        return true;
    }

    renderDialog() {
        this.removeDialog();
        const overlay = document.createElement('div');
        overlay.className = 'gv-manager-overlay show';
        overlay.innerHTML = this.renderDialogHtml();
        document.body.appendChild(overlay);
        this.dialog = overlay;
        this.bindDialogEvents();
    }

    renderDialogHtml() {
        const locked = this.isRuntimeLocked();
        const selected = this.getSelectedVariable();
        const runtimeBanner = locked
            ? '<div class="gv-warning" role="status">工程运行中，变量结构和值不可修改；仍可查看与刷新。</div>'
            : '';
        const listHtml = this.renderVariableListHtml();
        const schemaIsEmpty = this.schema.variables.length === 0 && !this.draft;
        const hasManualWriteBlockedVariable = this.schema.variables.some(variable => !variable.manualWriteAllowed);
        const resetAllDisabled = locked ||
            this.schema.variables.length === 0 ||
            hasManualWriteBlockedVariable ||
            this.pendingAction === 'reset-all';
        const resetAllTitle = hasManualWriteBlockedVariable
            ? '存在未允许人工写入的变量，不能全部重置'
            : '全部重置';

        return `
            <div class="gv-manager" role="dialog" aria-modal="true" aria-labelledby="gv-manager-title" tabindex="-1">
                <header class="gv-manager-header">
                    <div>
                        <h2 id="gv-manager-title">全局变量</h2>
                        <p>集中管理变量结构、算子绑定和当前运行值。</p>
                    </div>
                    <button type="button" class="gv-icon-button" data-action="close" title="关闭" aria-label="关闭">×</button>
                </header>
                ${runtimeBanner}
                ${this.errorMessage ? `<div class="gv-error" role="alert">${escapeHtml(this.errorMessage)}</div>` : ''}
                <section class="gv-toolbar">
                    <input type="search" class="form-input" id="gv-search" value="${escapeHtml(this.filters.search)}" placeholder="搜索名称、显示名称或说明" aria-label="搜索变量">
                    <select class="form-input" id="gv-type-filter" aria-label="类型筛选">
                        ${TYPE_FILTERS.map(item => `<option value="${escapeHtml(item)}" ${this.filters.type === item ? 'selected' : ''}>${escapeHtml(item)}</option>`).join('')}
                    </select>
                    <select class="form-input" id="gv-source-filter" aria-label="来源筛选">
                        ${SOURCE_FILTERS.map(item => `<option value="${escapeHtml(item)}" ${this.filters.source === item ? 'selected' : ''}>${escapeHtml(item)}</option>`).join('')}
                    </select>
                    ${schemaIsEmpty ? '' : `<button type="button" class="btn btn-primary" data-action="new" ${locked ? 'disabled' : ''} title="新建变量">新建变量</button>`}
                    <button type="button" class="btn btn-secondary" data-action="refresh" ${this.pendingAction === 'refresh' ? 'disabled' : ''} title="刷新当前值">${this.pendingAction === 'refresh' ? '刷新中...' : '刷新'}</button>
                    <button type="button" class="btn btn-secondary" data-action="reset-all" ${resetAllDisabled ? 'disabled' : ''} title="${escapeHtml(resetAllTitle)}">${this.pendingAction === 'reset-all' ? '重置中...' : '全部重置'}</button>
                </section>
                <main class="gv-manager-body ${schemaIsEmpty ? 'gv-manager-body-empty' : ''}">
                    <aside class="gv-variable-list" aria-label="变量列表">
                        ${listHtml}
                    </aside>
                    <section class="gv-detail ${schemaIsEmpty ? 'gv-detail-empty' : ''}">
                        ${selected || this.draft ? this.renderEditorHtml() : this.renderEmptyDetailHtml()}
                    </section>
                </main>
            </div>
        `;
    }

    renderVariableListHtml() {
        if (this.loading) {
            return '<div class="gv-loading">正在加载变量...</div>';
        }

        const valuesById = new Map(this.values.map(item => [String(item.variableId).toLowerCase(), item]));
        const filteredVariables = this.getFilteredVariables();
        if (filteredVariables.length === 0) {
            if (this.schema.variables.length === 0) {
                return '';
            }
            return '<div class="gv-empty">没有符合条件的变量。</div>';
        }

        return filteredVariables.map(variable => {
            const current = valuesById.get(String(variable.id).toLowerCase());
            const source = this.getSourceBinding(variable.id);
            return `
                    <button type="button" class="gv-variable-row ${sameId(variable.id, this.selectedVariableId) ? 'selected' : ''}" data-action="select" data-variable-id="${escapeHtml(variable.id)}">
                        <span class="gv-variable-name">${escapeHtml(variable.displayName || variable.name)}</span>
                        <span class="gv-variable-meta">${escapeHtml(variable.name)} · ${escapeHtml(getTypeLabel(variable.valueType))}</span>
                        <span class="gv-variable-value">${escapeHtml(formatGlobalVariableValue(current?.value ?? variable.initialValue))}</span>
                        <span class="gv-variable-source">${source ? OPERATOR_SOURCE_LABEL : FIXED_SOURCE_LABEL}</span>
                    </button>
                `;
        }).join('');
    }

    renderVariableList() {
        const list = this.dialog?.querySelector?.('.gv-variable-list');
        if (list) {
            list.innerHTML = this.renderVariableListHtml();
        }
    }

    renderEmptyDetailHtml() {
        return `
            <div class="gv-empty gv-empty-detail">
                <h3>暂无变量</h3>
                <p>点击“新建变量”创建第一个全局变量。</p>
                ${this.isRuntimeLocked() ? '<p class="gv-muted">工程运行中，停止后才能新建变量。</p>' : ''}
                ${this.dirty ? `
                    <div class="gv-actions">
                        <button type="button" class="btn btn-secondary" data-action="discard">放弃</button>
                        <button type="button" class="btn btn-primary" data-action="save" ${this.isRuntimeLocked() || this.pendingAction === 'save' ? 'disabled' : ''}>${this.pendingAction === 'save' ? '保存中...' : '保存'}</button>
                    </div>
                ` : `<button type="button" class="btn btn-primary" data-action="new" ${this.isRuntimeLocked() ? 'disabled' : ''}>新建变量</button>`}
            </div>
        `;
    }

    renderEditorHtml() {
        const draft = this.draft || createVariableDraft(this.getSelectedVariable());
        const variable = this.getSelectedVariable();
        const locked = this.isRuntimeLocked();
        const current = this.getCurrentValue(draft.id);
        const targetBindings = this.getTargetBindings(draft.id);
        const sourceBinding = this.getSourceBinding(draft.id);
        const invalidBindings = variable ? this.getIncompatibleBindings(variable, draft.valueType) : [];
        const writeAllowed = Boolean(draft.manualWriteAllowed);

        return `
            <div class="gv-detail-grid">
                <section class="gv-card gv-editor-card">
                    <div class="gv-section-header">
                        <h3>${draft.isNew ? '新建变量' : '变量编辑'}</h3>
                        <div class="gv-actions">
                            <button type="button" class="btn btn-secondary" data-action="discard" ${this.dirty ? '' : 'disabled'}>放弃</button>
                            <button type="button" class="btn btn-primary" data-action="save" ${locked || this.pendingAction === 'save' ? 'disabled' : ''}>${this.pendingAction === 'save' ? '保存中...' : '保存'}</button>
                            ${draft.isNew ? '' : `<button type="button" class="btn btn-danger" data-action="delete" ${locked ? 'disabled' : ''}>删除</button>`}
                        </div>
                    </div>
                    <div class="gv-form">
                        ${this.renderTextField('name', '名称', draft.name, '例如 judge.expected_count', true)}
                        ${this.renderTextField('displayName', '显示名称', draft.displayName, '用于界面展示', false)}
                        ${this.renderTextArea('description', '说明', draft.description)}
                        <label class="gv-field">
                            <span>类型</span>
                            <select class="form-input" data-field="valueType" ${locked ? 'disabled' : ''}>
                                ${GLOBAL_VARIABLE_TYPES.map(type => `<option value="${type}" ${draft.valueType === type ? 'selected' : ''}>${getTypeLabel(type)}</option>`).join('')}
                            </select>
                            ${this.renderFieldError('valueType')}
                        </label>
                        ${this.renderInitialValueField(draft)}
                        <div class="gv-two-columns">
                            ${this.renderTextField('minText', '最小值', draft.minText, '可为空', false, draft.valueType !== 'Int64' && draft.valueType !== 'Double', 'min')}
                            ${this.renderTextField('maxText', '最大值', draft.maxText, '可为空', false, draft.valueType !== 'Int64' && draft.valueType !== 'Double', 'max')}
                        </div>
                        <div class="gv-two-columns">
                            <label class="gv-check-field"><input type="checkbox" data-field="manualWriteAllowed" ${draft.manualWriteAllowed ? 'checked' : ''} ${locked ? 'disabled' : ''}> 允许人工写入</label>
                            <label class="gv-check-field"><input type="checkbox" data-field="includeInResultMetadata" ${draft.includeInResultMetadata ? 'checked' : ''} ${locked ? 'disabled' : ''}> 写入结果元数据</label>
                        </div>
                        ${this.renderTextField('order', '排序', String(draft.order ?? 0), '数字越小越靠前', false)}
                        ${invalidBindings.length ? `<div class="gv-warning">${escapeHtml(this.describeBindingImpact(invalidBindings))}</div>` : ''}
                    </div>
                </section>
                <section class="gv-card">
                    <div class="gv-section-header">
                        <h3>来源绑定</h3>
                        <div class="gv-actions">
                            <button type="button" class="btn btn-secondary" data-action="source-dialog" ${locked || draft.isNew ? 'disabled' : ''}>选择来源</button>
                            <button type="button" class="btn btn-secondary" data-action="clear-source" ${locked || !sourceBinding ? 'disabled' : ''}>清除</button>
                        </div>
                    </div>
                    ${this.renderSourceBindingHtml(sourceBinding)}
                </section>
                <section class="gv-card">
                    <div class="gv-section-header">
                        <h3>目标绑定</h3>
                    </div>
                    ${this.renderTargetBindingsHtml(targetBindings)}
                </section>
                <section class="gv-card">
                    <div class="gv-section-header">
                        <h3>当前值</h3>
                        <button type="button" class="btn btn-secondary" data-action="refresh">刷新</button>
                    </div>
                    ${this.renderCurrentValueHtml(draft, current, locked, writeAllowed)}
                </section>
            </div>
        `;
    }

    renderTextField(field, label, value, placeholder, required = false, disabled = false, errorKey = field) {
        return `
            <label class="gv-field">
                <span>${escapeHtml(label)}${required ? ' *' : ''}</span>
                <input class="form-input" data-field="${escapeHtml(field)}" value="${escapeHtml(value)}" placeholder="${escapeHtml(placeholder || '')}" ${disabled || this.isRuntimeLocked() ? 'disabled' : ''}>
                ${this.renderFieldError(errorKey)}
            </label>
        `;
    }

    renderTextArea(field, label, value) {
        return `
            <label class="gv-field">
                <span>${escapeHtml(label)}</span>
                <textarea class="form-input" data-field="${escapeHtml(field)}" rows="3" ${this.isRuntimeLocked() ? 'disabled' : ''}>${escapeHtml(value)}</textarea>
                ${this.renderFieldError(field)}
            </label>
        `;
    }

    renderInitialValueField(draft) {
        const disabled = this.isRuntimeLocked() ? 'disabled' : '';
        if (draft.valueType === 'Boolean') {
            return `
                <label class="gv-field">
                    <span>初始值</span>
                    <select class="form-input" data-field="initialValueText" ${disabled}>
                        <option value="false" ${draft.initialValueText !== 'true' ? 'selected' : ''}>否</option>
                        <option value="true" ${draft.initialValueText === 'true' ? 'selected' : ''}>是</option>
                    </select>
                    ${this.renderFieldError('initialValue')}
                </label>
            `;
        }

        const type = draft.valueType === 'String' ? 'text' : 'number';
        const step = draft.valueType === 'Int64' ? '1' : 'any';
        return `
            <label class="gv-field">
                <span>初始值</span>
                <input type="${type}" step="${step}" class="form-input" data-field="initialValueText" value="${escapeHtml(draft.initialValueText)}" ${disabled}>
                ${this.renderFieldError('initialValue')}
            </label>
        `;
    }

    renderFieldError(field) {
        const message = this.fieldErrors[field];
        return message ? `<span class="gv-field-error">${escapeHtml(message)}</span>` : '';
    }

    getFieldErrorKey(field) {
        switch (field) {
            case 'initialValueText':
                return 'initialValue';
            case 'minText':
                return 'min';
            case 'maxText':
                return 'max';
            default:
                return field;
        }
    }

    clearFieldErrorForInput(field) {
        const errorKey = this.getFieldErrorKey(field);
        if (!Object.prototype.hasOwnProperty.call(this.fieldErrors, errorKey)) {
            return;
        }

        const nextErrors = { ...this.fieldErrors };
        delete nextErrors[errorKey];
        this.fieldErrors = nextErrors;
        const input = this.dialog?.querySelector?.(`[data-field="${cssEscape(field)}"]`);
        input?.closest?.('.gv-field')?.querySelector?.('.gv-field-error')?.remove?.();
    }

    renderSourceBindingHtml(binding) {
        if (!binding) {
            return '<div class="gv-empty compact">当前使用固定初始值，没有绑定算子输出。</div>';
        }

        const resolved = this.resolveOutputBinding(binding);
        return `
            <div class="gv-binding-row ${resolved.valid ? '' : 'invalid'}">
                <div>
                    <strong>${escapeHtml(binding.operatorName || binding.operatorId || '未知算子')}</strong>
                    <span>${escapeHtml(binding.outputPortName || binding.outputPortId || '未知输出')}</span>
                    ${resolved.valid ? '' : '<em>引用已失效，请重新选择或清除。</em>'}
                </div>
                <button type="button" class="btn btn-secondary" data-action="locate" data-operator-id="${escapeHtml(binding.operatorId)}">定位算子</button>
            </div>
        `;
    }

    renderTargetBindingsHtml(bindings) {
        if (!bindings.length) {
            return '<div class="gv-empty compact">暂无目标参数绑定。</div>';
        }

        return bindings.map(binding => {
            const resolved = this.resolveTargetBinding(binding);
            return `
                <div class="gv-binding-row ${resolved.valid ? '' : 'invalid'}">
                    <div>
                        <strong>${escapeHtml(binding.operatorName || binding.operatorId || '未知算子')}</strong>
                        <span>${escapeHtml(binding.parameterName || binding.parameterId || '未知参数')}</span>
                        ${resolved.valid ? '' : '<em>引用已失效，请在属性面板重新绑定或移除。</em>'}
                    </div>
                    <div class="gv-actions">
                        <button type="button" class="btn btn-secondary" data-action="locate" data-operator-id="${escapeHtml(binding.operatorId)}">定位</button>
                        <button type="button" class="btn btn-secondary" data-action="remove-target" data-binding-id="${escapeHtml(binding.id)}" ${this.isRuntimeLocked() ? 'disabled' : ''}>移除</button>
                    </div>
                </div>
            `;
        }).join('');
    }

    renderCurrentValueHtml(draft, current, locked, writeAllowed) {
        const disableMutation = locked || !writeAllowed;
        const parsedInitial = coerceGlobalVariableValue(draft.valueType, draft.initialValueText);
        const displayedInitial = parsedInitial.ok ? parsedInitial.value : draft.initialValue;
        const reason = locked
            ? '工程运行中，变量结构和值不可修改。'
            : (!writeAllowed ? '该变量未允许人工写入。' : '');
        const inputHtml = draft.valueType === 'Boolean'
            ? `<select class="form-input" id="gv-write-value" ${disableMutation ? 'disabled' : ''}><option value="false">否</option><option value="true">是</option></select>`
            : `<input class="form-input" id="gv-write-value" type="${draft.valueType === 'String' ? 'text' : 'number'}" step="${draft.valueType === 'Int64' ? '1' : 'any'}" ${disableMutation ? 'disabled' : ''}>`;

        return `
            <dl class="gv-value-grid">
                <div><dt>当前值</dt><dd>${escapeHtml(formatGlobalVariableValue(current?.value))}</dd></div>
                <div><dt>初始值</dt><dd>${escapeHtml(formatGlobalVariableValue(displayedInitial))}</dd></div>
                <div><dt>版本</dt><dd>${escapeHtml(current?.version ?? 0)}</dd></div>
                <div><dt>更新来源</dt><dd>${escapeHtml(formatUpdatedBy(current?.updatedBy))}</dd></div>
                <div><dt>更新时间</dt><dd>${escapeHtml(formatDateTime(current?.updatedAtUtc))}</dd></div>
                <div><dt>RunId / 算子</dt><dd>${escapeHtml([current?.runId, current?.operatorName || current?.operatorId].filter(Boolean).join(' / ') || '-')}</dd></div>
            </dl>
            <div class="gv-write-row">
                ${inputHtml}
                <button type="button" class="btn btn-primary" data-action="write" ${disableMutation || this.pendingAction === 'write' ? 'disabled' : ''}>${this.pendingAction === 'write' ? '写入中...' : '写入'}</button>
                <button type="button" class="btn btn-secondary" data-action="reset-one" ${disableMutation || this.pendingAction === 'reset-one' ? 'disabled' : ''}>${this.pendingAction === 'reset-one' ? '重置中...' : '重置'}</button>
            </div>
            ${reason ? `<p class="gv-muted">${escapeHtml(reason)}</p>` : ''}
        `;
    }

    bindDialogEvents() {
        const root = this.dialog;
        if (!root) {
            return;
        }

        root.querySelector('.gv-manager')?.focus?.();
        root.addEventListener('keydown', event => {
            if (event.key === 'Escape') {
                event.preventDefault();
                void this.closeManager();
            }
        });
        root.addEventListener('input', event => this.handleInput(event));
        root.addEventListener('change', event => this.handleInput(event));
        root.addEventListener('click', event => {
            const actionTarget = event.target.closest('[data-action]');
            if (!actionTarget) {
                return;
            }
            event.preventDefault();
            void this.handleAction(actionTarget.dataset.action, actionTarget);
        });
    }

    handleInput(event) {
        const target = event.target;
        if (target.id === 'gv-search') {
            const selectionStart = target.selectionStart;
            const selectionEnd = target.selectionEnd;
            this.filters.search = target.value || '';
            this.renderVariableList();
            const search = this.dialog?.querySelector?.('#gv-search');
            if (search) {
                search.focus?.();
                search.setSelectionRange?.(selectionStart ?? search.value.length, selectionEnd ?? search.value.length);
            }
            return;
        }
        if (target.id === 'gv-type-filter') {
            this.filters.type = target.value || ALL_TYPES_LABEL;
            this.renderVariableList();
            return;
        }
        if (target.id === 'gv-source-filter') {
            this.filters.source = target.value || ALL_SOURCES_LABEL;
            this.renderVariableList();
            return;
        }
        if (!target.dataset?.field || !this.draft) {
            return;
        }

        const field = target.dataset.field;
        let shouldRender = false;
        if (target.type === 'checkbox') {
            this.draft[field] = target.checked;
        } else if (field === 'valueType') {
            this.draft.valueType = normalizeValueType(target.value);
            this.draft.initialValueText = formatValueForInput(this.draft.valueType, this.draft.initialValue);
            if (this.draft.valueType !== 'Int64' && this.draft.valueType !== 'Double') {
                this.draft.minText = '';
                this.draft.maxText = '';
            }
            shouldRender = true;
        } else if (field === 'order') {
            this.draft.order = Number.parseInt(target.value || '0', 10) || 0;
        } else {
            this.draft[field] = target.value;
        }

        this.updateDirtyState();
        this.clearFieldErrorForInput(field);
        if (shouldRender) {
            this.renderDialog();
        }
    }

    async handleAction(action, target) {
        switch (action) {
            case 'close':
                await this.closeManager();
                break;
            case 'new':
                if (await this.selectVariable('')) {
                    this.createNewDraft();
                }
                break;
            case 'select':
                await this.selectVariable(target.dataset.variableId);
                break;
            case 'discard':
                this.discardDraft();
                break;
            case 'save':
                await this.save();
                break;
            case 'delete':
                await this.deleteSelectedVariable();
                break;
            case 'refresh':
                await this.runMutation('refresh', () => this.refreshValues({ requestId: this.requestSerial }));
                break;
            case 'reset-all':
                await this.resetAllValues();
                break;
            case 'reset-one':
                await this.resetSelectedValue();
                break;
            case 'write':
                await this.writeSelectedValue();
                break;
            case 'source-dialog':
                await this.openSourceBindingDialog();
                break;
            case 'clear-source':
                this.clearSourceBinding();
                break;
            case 'remove-target':
                this.removeTargetBinding(target.dataset.bindingId);
                break;
            case 'locate':
                this.locateOperator(target.dataset.operatorId);
                break;
            default:
                break;
        }
    }

    async selectVariable(variableId) {
        if (this.dirty) {
            const choice = await this.requestChoice('存在未保存修改', '切换变量前是否保存当前编辑？', [
                { value: 'save', text: '保存' },
                { value: 'discard', text: '放弃' },
                { value: 'cancel', text: '取消' }
            ]);
            if (choice === 'cancel') {
                return false;
            }
            if (choice === 'save') {
                const saved = await this.save();
                if (!saved) {
                    return false;
                }
            } else {
                this.discardDraft();
            }
        }

        this.selectedVariableId = variableId || '';
        this.draft = variableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.updateDirtyState();
        this.fieldErrors = {};
        this.renderDialog();
        return true;
    }

    createNewDraft() {
        this.selectedVariableId = '';
        this.draft = createVariableDraft(null, this.schema.variables.length);
        this.updateDirtyState();
        this.fieldErrors = {};
        this.renderDialog();
    }

    discardDraft() {
        const selectedVariableId = this.selectedVariableId;
        this.schema = cloneSchema(this.baselineSchema);
        this.selectedVariableId = this.schema.variables.some(item => sameId(item.id, selectedVariableId))
            ? selectedVariableId
            : (this.schema.variables[0]?.id || '');
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.dirty = false;
        this.fieldErrors = {};
        this.errorMessage = '';
        this.syncSchemaToProject();
        this.renderDialog();
    }

    async save() {
        if (this.isRuntimeLocked()) {
            this.toast('工程运行中，变量结构和值不可修改。', 'warning');
            return false;
        }
        const original = this.getSelectedVariable();
        const prepared = this.prepareSchemaForSave();
        if (!prepared.ok) {
            this.fieldErrors = prepared.errors;
            this.errorMessage = '请先修正表单中的错误。';
            this.renderDialog();
            return false;
        }

        const impact = original && prepared.variable && normalizeValueType(original.valueType) !== normalizeValueType(prepared.variable.valueType)
            ? this.getTypeChangeImpact(original, prepared.variable.valueType)
            : [];
        if (impact.length) {
            const choice = await this.requestChoice('类型变更影响绑定', this.describeBindingImpact(impact), [
                { value: 'continue', text: '继续保存' },
                { value: 'cancel', text: '取消' }
            ]);
            if (choice !== 'continue') {
                return false;
            }
        }

        const nextSchema = prepared.schema;
        this.fieldErrors = {};

        return await this.runMutation('save', async () => {
            const projectId = this.project.id;
            const saved = await projectManager.saveGlobalVariables(nextSchema);
            if (this.project?.id !== projectId) {
                return false;
            }
            this.applySchema(saved, prepared.selectedVariableId, { rebuildBaseline: true, sync: true });
            this.dirty = false;
            await this.refreshValues({ requestId: this.requestSerial, render: false });
            this.toast('全局变量已保存。', 'success');
            this.render();
            return true;
        }, { conflictRefresh: true, preserveDraftOnError: true });
    }

    async deleteSelectedVariable() {
        const variable = this.getSelectedVariable();
        if (!variable || this.isRuntimeLocked()) {
            return false;
        }

        const references = this.getVariableReferences(variable.id);
        if (references.length) {
            this.errorMessage = `变量“${variable.name}”仍被引用，不能删除。请在绑定区查看并移除引用。`;
            this.renderDialog();
            return false;
        }

        const choice = await this.requestChoice('删除变量', `确定删除“${variable.name}”吗？此操作保存前仍可放弃。`, [
            { value: 'delete', text: '删除' },
            { value: 'cancel', text: '取消' }
        ]);
        if (choice !== 'delete') {
            return false;
        }

        this.schema.variables = this.schema.variables.filter(item => !sameId(item.id, variable.id));
        this.schema.sourceBindings = this.schema.sourceBindings.filter(item => !sameId(item.variableId, variable.id));
        this.schema.targetBindings = this.schema.targetBindings.filter(item => !sameId(item.variableId, variable.id));
        this.selectedVariableId = this.schema.variables[0]?.id || '';
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.updateDirtyState();
        this.render();
        return true;
    }

    async writeSelectedValue() {
        const variable = this.getSelectedVariable();
        const input = this.dialog?.querySelector('#gv-write-value');
        if (!variable || !input || this.isRuntimeLocked()) {
            return false;
        }
        if (!variable.manualWriteAllowed) {
            this.toast('该变量未允许人工写入。', 'warning');
            return false;
        }

        const coerced = coerceGlobalVariableValue(variable.valueType, input.value);
        if (!coerced.ok) {
            this.errorMessage = coerced.error;
            this.renderDialog();
            return false;
        }

        return await this.runMutation('write', async () => {
            const projectId = this.project.id;
            const values = await writeGlobalVariableValue(projectId, variable.id, coerced.value);
            if (this.project?.id !== projectId) {
                return false;
            }
            this.values = values;
            this.toast('当前值已写入。', 'success');
            this.render();
            return true;
        }, { conflictRefresh: true });
    }

    async resetSelectedValue() {
        const variable = this.getSelectedVariable();
        if (!variable || this.isRuntimeLocked()) {
            return false;
        }
        if (!variable.manualWriteAllowed) {
            this.toast('该变量未允许人工写入，不能重置。', 'warning');
            return false;
        }

        return await this.runMutation('reset-one', async () => {
            const projectId = this.project.id;
            const values = await resetGlobalVariableValue(projectId, variable.id);
            if (this.project?.id !== projectId) {
                return false;
            }
            this.values = values;
            this.toast('变量已重置。', 'success');
            this.render();
            return true;
        }, { conflictRefresh: true });
    }

    async resetAllValues() {
        if (this.isRuntimeLocked()) {
            this.toast('工程运行中，变量结构和值不可修改。', 'warning');
            return false;
        }
        const blockedVariable = this.schema.variables.find(variable => !variable.manualWriteAllowed);
        if (blockedVariable) {
            this.toast(`变量 ${blockedVariable.displayName || blockedVariable.name} 未允许人工写入，不能全部重置。`, 'warning');
            return false;
        }

        const choice = await this.requestChoice('全部重置', '确定将所有全局变量恢复为初始值吗？', [
            { value: 'reset', text: '重置' },
            { value: 'cancel', text: '取消' }
        ]);
        if (choice !== 'reset') {
            return false;
        }

        return await this.runMutation('reset-all', async () => {
            const projectId = this.project.id;
            const values = await resetGlobalVariableValues(projectId);
            if (this.project?.id !== projectId) {
                return false;
            }
            this.values = values;
            this.toast('全部变量已重置。', 'success');
            this.render();
            return true;
        }, { conflictRefresh: true });
    }

    async openSourceBindingDialog() {
        const variable = this.getSelectedVariable();
        if (!variable || this.isRuntimeLocked()) {
            return;
        }

        const outputs = this.getFlowOutputs()
            .filter(output => isVariableCompatibleWithDataType(variable.valueType, output.dataType));
        if (!outputs.length) {
            this.errorMessage = '当前流程没有与该变量类型兼容的算子输出。';
            this.renderDialog();
            return;
        }

        const body = document.createElement('div');
        body.className = 'gv-source-picker';
        const grouped = groupBy(outputs, item => item.operatorId);
        Object.values(grouped).forEach(group => {
            const first = group[0];
            const section = document.createElement('section');
            section.className = 'gv-source-group';
            section.innerHTML = `<h4>${escapeHtml(first.operatorName || first.operatorId)}</h4>`;
            group.forEach(output => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'gv-source-option';
                button.dataset.operatorId = output.operatorId;
                button.dataset.outputPortId = output.outputPortId;
                button.innerHTML = `
                    <span>${escapeHtml(output.outputPortName || output.outputPortId)}</span>
                    <small>${escapeHtml(formatDataType(output.dataType))}</small>
                `;
                section.appendChild(button);
            });
            body.appendChild(section);
        });

        const selected = await this.requestElementChoice('选择来源绑定', body);
        if (!selected) {
            return;
        }

        const output = outputs.find(item =>
            sameId(item.operatorId, selected.operatorId) &&
            sameId(item.outputPortId, selected.outputPortId));
        if (!output) {
            return;
        }

        this.schema.sourceBindings = this.schema.sourceBindings.filter(item => !sameId(item.variableId, variable.id));
        this.schema.sourceBindings.push({
            id: createUuid(),
            variableId: variable.id,
            operatorId: output.operatorId,
            outputPortId: output.outputPortId,
            operatorName: output.operatorName,
            outputPortName: output.outputPortName,
            conversionMode: 'Exact',
            expression: ''
        });
        this.updateDirtyState();
        this.render();
    }

    clearSourceBinding() {
        const variable = this.getSelectedVariable();
        if (!variable || this.isRuntimeLocked()) {
            return;
        }
        this.schema.sourceBindings = this.schema.sourceBindings.filter(item => !sameId(item.variableId, variable.id));
        this.updateDirtyState();
        this.render();
    }

    removeTargetBinding(bindingId) {
        if (this.isRuntimeLocked()) {
            return;
        }
        this.schema.targetBindings = this.schema.targetBindings.filter(item => !sameId(item.id, bindingId));
        this.updateDirtyState();
        this.render();
    }

    locateOperator(operatorId) {
        const flowCanvas = serviceRegistry.get('flowCanvas');
        if (operatorId && flowCanvas?.nodes?.has?.(operatorId)) {
            flowCanvas.selectedNode = operatorId;
            flowCanvas.onNodeSelected?.(flowCanvas.nodes.get(operatorId));
            flowCanvas.invalidate?.();
            this.toast('已定位到算子。', 'info');
            return true;
        }

        const adapter = serviceRegistry.get('flowCanvasAdapter');
        if (adapter?.selectNode?.(operatorId)) {
            this.toast('已定位到算子。', 'info');
            return true;
        }

        this.toast('未找到对应算子。', 'warning');
        return false;
    }

    async runMutation(action, operation, options = {}) {
        if (this.pendingAction) {
            return false;
        }

        const projectId = this.project?.id;
        this.pendingAction = action;
        this.errorMessage = '';
        this.render();
        try {
            const result = await operation();
            if (projectId !== this.project?.id) {
                return false;
            }
            return result;
        } catch (error) {
            if (this.isConflict(error)) {
                this.errorMessage = '工程正在运行，变量结构和值不可修改。正在重新获取当前状态和值。';
                const [valuesResult, stateResult] = await Promise.allSettled([
                    this.refreshValues({ requestId: this.requestSerial, render: false }),
                    this.syncRuntimeStateAfterConflict(projectId)
                ]);
                if (projectId === this.project?.id) {
                    const valuesOk = valuesResult.status === 'fulfilled';
                    const stateOk = stateResult.status === 'fulfilled' && stateResult.value;
                    this.errorMessage = valuesOk && stateOk
                        ? '工程正在运行，变量结构和值不可修改。已重新获取当前状态和值。'
                        : '工程正在运行，变量结构和值不可修改。当前状态或值刷新失败，请稍后重试。';
                }
            } else {
                this.errorMessage = this.toUserMessage(error, '操作失败。');
            }
            this.render();
            return false;
        } finally {
            this.pendingAction = '';
            if (projectId === this.project?.id) {
                this.render();
            }
        }
    }

    applySchema(schema, selectedVariableId = this.selectedVariableId, options = {}) {
        this.schema = normalizeGlobalVariableSchema(schema);
        if (options.rebuildBaseline) {
            this.baselineSchema = cloneSchema(this.schema);
        }
        this.selectedVariableId = selectedVariableId || this.schema.variables[0]?.id || '';
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.updateDirtyState();
        if (options.sync !== false) {
            this.syncSchemaToProject();
        }
    }

    setSchemaFromExternal(schema) {
        this.schema = normalizeGlobalVariableSchema(schema);
        if (this.selectedVariableId && !this.getSelectedVariable()) {
            this.selectedVariableId = this.schema.variables[0]?.id || '';
        }
        if (!this.dirty) {
            this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        }
        this.updateDirtyState();
        this.render();
    }

    rebuildBaseline(schema) {
        this.baselineSchema = normalizeGlobalVariableSchema(schema);
        this.schema = cloneSchema(this.baselineSchema);
        this.dirty = false;
    }

    prepareSchemaForSave() {
        const nextSchema = normalizeGlobalVariableSchema(this.schema);
        if (!this.draft) {
            return {
                ok: true,
                errors: {},
                schema: nextSchema,
                variable: null,
                selectedVariableId: this.selectedVariableId || nextSchema.variables[0]?.id || ''
            };
        }

        const original = this.getSelectedVariable();
        const serialized = serializeVariableDraft(this.draft, nextSchema, original);
        if (!serialized.ok) {
            return {
                ok: false,
                errors: serialized.errors,
                schema: nextSchema,
                variable: null,
                selectedVariableId: this.selectedVariableId
            };
        }

        const index = nextSchema.variables.findIndex(item => sameId(item.id, serialized.variable.id));
        if (index >= 0) {
            nextSchema.variables[index] = serialized.variable;
        } else {
            nextSchema.variables.push(serialized.variable);
        }
        nextSchema.variables.sort((left, right) => Number(left.order ?? 0) - Number(right.order ?? 0));

        return {
            ok: true,
            errors: {},
            schema: nextSchema,
            variable: serialized.variable,
            selectedVariableId: serialized.variable.id
        };
    }

    isDraftChanged() {
        if (!this.draft) {
            return false;
        }
        if (this.draft.isNew) {
            return true;
        }
        const original = this.getSelectedVariable();
        if (!original) {
            return true;
        }
        const serialized = serializeVariableDraft(this.draft, this.schema, original);
        if (!serialized.ok) {
            return true;
        }
        return !schemaEquals(
            { schemaVersion: '1.0', variables: [serialized.variable], sourceBindings: [], targetBindings: [] },
            { schemaVersion: '1.0', variables: [original], sourceBindings: [], targetBindings: [] }
        );
    }

    updateDirtyState() {
        this.dirty = !schemaEquals(this.schema, this.baselineSchema) || this.isDraftChanged();
    }

    syncSchemaToProject() {
        projectManager.updateGlobalVariables(this.schema);
        serviceRegistry.get('flowCanvas')?.setGlobalVariableSchema?.(this.schema);
        serviceRegistry.get('propertyPanel')?.render?.();
    }

    getFilteredVariables() {
        const search = this.filters.search.trim().toLowerCase();
        return [...this.schema.variables]
            .sort((left, right) => Number(left.order ?? 0) - Number(right.order ?? 0))
            .filter(variable => {
                if (this.filters.type !== ALL_TYPES_LABEL && getTypeLabel(variable.valueType) !== this.filters.type) {
                    return false;
                }
                const hasSource = Boolean(this.getSourceBinding(variable.id));
                if (this.filters.source === FIXED_SOURCE_LABEL && hasSource) {
                    return false;
                }
                if (this.filters.source === OPERATOR_SOURCE_LABEL && !hasSource) {
                    return false;
                }
                if (!search) {
                    return true;
                }
                return [variable.name, variable.displayName, variable.description]
                    .some(text => String(text || '').toLowerCase().includes(search));
            });
    }

    getSelectedVariable() {
        return this.schema.variables.find(item => sameId(item.id, this.selectedVariableId)) || null;
    }

    getCurrentValue(variableId) {
        return this.values.find(item => sameId(item.variableId, variableId)) || null;
    }

    getSourceBinding(variableId) {
        return this.schema.sourceBindings.find(item => sameId(item.variableId, variableId)) || null;
    }

    getTargetBindings(variableId) {
        return this.schema.targetBindings.filter(item => sameId(item.variableId, variableId));
    }

    getVariableReferences(variableId) {
        const variable = this.schema.variables.find(item => sameId(item.id, variableId)) || null;
        const references = [
            ...this.schema.sourceBindings.filter(item => sameId(item.variableId, variableId)).map(item => `来源：${item.operatorName || item.operatorId}.${item.outputPortName || item.outputPortId}`),
            ...this.schema.targetBindings.filter(item => sameId(item.variableId, variableId)).map(item => `目标：${item.operatorName || item.operatorId}.${item.parameterName || item.parameterId}`)
        ];
        const addReference = reference => {
            if (reference && !references.includes(reference)) {
                references.push(reference);
            }
        };
        this.getFlowVariableOperatorReferences(variable).forEach(addReference);
        this.getExpressionReferences(variable).forEach(addReference);
        return references;
    }

    getFlowVariableOperatorReferences(variable) {
        if (!variable) {
            return [];
        }

        return this.getFlowOperators()
            .filter(operator => this.getProjectVariableOperatorKind(operator))
            .filter(operator => String(this.getOperatorParameterValue(operator, 'Scope') || '').toLowerCase() === 'project')
            .filter(operator => {
                const variableId = this.getOperatorParameterValue(operator, 'VariableId');
                if (variableId) {
                    return sameId(variableId, variable.id);
                }
                return sameId(this.getOperatorParameterValue(operator, 'VariableName'), variable.name);
            })
            .map(operator => {
                const kind = this.getProjectVariableOperatorKind(operator);
                const label = kind === 'VariableRead' ? '读取' : kind === 'VariableWrite' ? '写入' : '递增';
                return `算子：${this.getOperatorDisplayName(operator)}.${label}`;
            });
    }

    getExpressionReferences(variable) {
        if (!variable?.name) {
            return [];
        }

        const references = [];
        this.schema.sourceBindings
            .filter(item => expressionReferencesVariable(item.expression, variable.name))
            .forEach(item => references.push(`表达式：来源：${item.operatorName || item.operatorId}.${item.outputPortName || item.outputPortId}`));
        this.schema.targetBindings
            .filter(item => expressionReferencesVariable(item.expression, variable.name))
            .forEach(item => references.push(`表达式：目标：${item.operatorName || item.operatorId}.${item.parameterName || item.parameterId}`));
        this.getFlowOperators()
            .filter(operator => expressionReferencesVariable(this.getOperatorParameterValue(operator, 'Expression'), variable.name))
            .forEach(operator => references.push(`表达式：算子：${this.getOperatorDisplayName(operator)}`));
        return references;
    }

    getFlowOperators() {
        const flow = this.project?.flow || this.project?.Flow;
        return normalizeArray(flow?.operators ?? flow?.Operators);
    }

    getProjectVariableOperatorKind(operator) {
        const rawType = operator?.type ?? operator?.Type ?? operator?.operatorType ?? operator?.OperatorType;
        const text = String(rawType || '').toLowerCase();
        if (text.endsWith('variableread')) {
            return 'VariableRead';
        }
        if (text.endsWith('variablewrite')) {
            return 'VariableWrite';
        }
        if (text.endsWith('variableincrement')) {
            return 'VariableIncrement';
        }

        switch (Number(rawType)) {
            case 80:
                return 'VariableRead';
            case 81:
                return 'VariableWrite';
            case 82:
                return 'VariableIncrement';
            default:
                return '';
        }
    }

    getOperatorParameterValue(operator, parameterName) {
        const lowerName = String(parameterName || '').toLowerCase();
        const parameter = normalizeArray(operator?.parameters ?? operator?.Parameters)
            .find(item => String(item.name ?? item.Name ?? '').toLowerCase() === lowerName);
        return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue ?? '';
    }

    getOperatorDisplayName(operator) {
        return operator?.name || operator?.Name || operator?.title || operator?.Title || operator?.type || operator?.Type || operator?.id || operator?.Id || '未命名算子';
    }

    getIncompatibleBindings(variable, nextType) {
        const impacts = [];
        const source = this.getSourceBinding(variable.id);
        if (source) {
            const resolved = this.resolveOutputBinding(source);
            if (!resolved.valid || !isVariableCompatibleWithDataType(nextType, resolved.dataType)) {
                impacts.push(`来源 ${source.operatorName || source.operatorId}.${source.outputPortName || source.outputPortId}`);
            }
        }
        this.getTargetBindings(variable.id).forEach(binding => {
            const resolved = this.resolveTargetBinding(binding);
            if (!resolved.valid || !isVariableCompatibleWithDataType(nextType, resolved.dataType)) {
                impacts.push(`目标 ${binding.operatorName || binding.operatorId}.${binding.parameterName || binding.parameterId}`);
            }
        });
        return impacts;
    }

    getTypeChangeImpact(variable, nextType) {
        const incompatible = new Map(
            this.getIncompatibleBindings(variable, nextType)
                .map(item => [normalizeImpactKey(item), item])
        );
        const impacts = [];
        const seen = new Set();
        const addImpact = (text, key = normalizeImpactKey(text)) => {
            if (!text || seen.has(key)) {
                return;
            }
            seen.add(key);
            impacts.push(text);
        };

        this.getVariableReferences(variable.id).forEach(reference => {
            const key = normalizeImpactKey(reference);
            const suffix = incompatible.has(key) ? '（类型不兼容）' : '';
            addImpact(`${reference}${suffix}`, key);
            incompatible.delete(key);
        });
        incompatible.forEach((item, key) => addImpact(`${item}（类型不兼容）`, key));
        return impacts;
    }

    describeBindingImpact(impact) {
        return `类型变更会影响以下引用/绑定，请确认后再保存：${impact.join('；')}`;
    }

    getFlowOutputs() {
        return this.getFlowOperators().flatMap(operator => {
            const outputs = normalizeArray(operator.outputPorts ?? operator.OutputPorts);
            return outputs.map(port => ({
                operatorId: operator.id || operator.Id,
                operatorName: this.getOperatorDisplayName(operator),
                outputPortId: port.id || port.Id,
                outputPortName: port.name || port.Name || port.displayName || port.DisplayName || '',
                dataType: port.dataType || port.DataType || port.type || port.Type || ''
            }));
        }).filter(item => item.operatorId && item.outputPortId);
    }

    resolveOutputBinding(binding) {
        const output = this.getFlowOutputs().find(item =>
            sameId(item.operatorId, binding.operatorId) &&
            sameId(item.outputPortId, binding.outputPortId));
        return {
            valid: Boolean(output),
            dataType: output?.dataType || ''
        };
    }

    resolveTargetBinding(binding) {
        const operator = this.getFlowOperators().find(item => sameId(item.id || item.Id, binding.operatorId));
        const parameters = normalizeArray(operator?.parameters ?? operator?.Parameters);
        const parameter = parameters.find(item => sameId(item.id || item.Id, binding.parameterId));
        return {
            valid: Boolean(operator && parameter),
            dataType: parameter?.dataType || parameter?.DataType || parameter?.type || parameter?.Type || ''
        };
    }

    isRuntimeLocked() {
        if (this.runtimeStateLoading) {
            return true;
        }

        const states = [
            this.runtimeState,
            this.options.getRuntimeState?.(),
            inspectionController.getState?.()
        ];
        return states.some(state =>
            this.isRelevantRuntimeState(state) &&
            this.isRuntimeStateBusy(state) &&
            !this.isStaleBusyRuntimeState(state));
    }

    isRelevantRuntimeState(state, projectId = this.project?.id) {
        if (!state || !projectId) {
            return false;
        }

        const stateProjectId = this.getRuntimeProjectId(state);
        if (!stateProjectId) {
            return false;
        }

        return sameId(stateProjectId, projectId);
    }

    getRuntimeProjectId(state) {
        return state?.projectId ?? state?.ProjectId ?? null;
    }

    getRuntimeSessionId(state) {
        const sessionId = state?.sessionId ?? state?.SessionId ?? null;
        return sessionId == null ? '' : String(sessionId).trim();
    }

    isStaleBusyRuntimeState(state) {
        if (!this.endpointTerminalState ||
            !this.isRelevantRuntimeState(state, this.endpointTerminalState.projectId) ||
            !this.isRuntimeStateBusy(state)) {
            return false;
        }

        const terminalSessionId = this.endpointTerminalState.sessionId;
        const stateSessionId = this.getRuntimeSessionId(state);
        return Boolean(terminalSessionId && stateSessionId && terminalSessionId === stateSessionId);
    }

    updateEndpointTerminalState(state, source) {
        if (source === 'endpoint' && this.isRelevantRuntimeState(state) && !this.isRuntimeStateBusy(state)) {
            this.endpointTerminalState = {
                projectId: this.getRuntimeProjectId(state),
                sessionId: this.getRuntimeSessionId(state)
            };
            return;
        }

        if (this.isRelevantRuntimeState(state) &&
            this.isRuntimeStateBusy(state) &&
            !this.isStaleBusyRuntimeState(state) &&
            this.getRuntimeSessionId(state)) {
            this.endpointTerminalState = null;
        }
    }

    subscribeRuntimeState() {
        const subscribe = this.options.subscribeRuntimeState || inspectionController.subscribeState?.bind(inspectionController);
        if (typeof subscribe !== 'function') {
            return;
        }

        this.unsubscribeInspectionState = subscribe(state => {
            const wasLocked = this.isRuntimeLocked();
            if (this.isStaleBusyRuntimeState(state)) {
                return;
            }

            this.runtimeState = state || {};
            this.runtimeStateSource = 'local';
            this.updateEndpointTerminalState(this.runtimeState, 'local');
            if (this.isRelevantRuntimeState(this.runtimeState) &&
                this.isRuntimeStateBusy(this.runtimeState) &&
                !this.getRuntimeSessionId(this.runtimeState)) {
                void this.refreshRuntimeStateOnOpen(this.project?.id).catch(() => {});
            }
            if (this.isRelevantRuntimeState(this.runtimeState) && !this.isRuntimeStateBusy(this.runtimeState)) {
                this.stopRuntimeStatePolling();
            }
            const isLocked = this.isRuntimeLocked();
            if (wasLocked !== isLocked || this.isOpen) {
                this.render();
            }
        });
    }

    async syncRuntimeStateAfterConflict(projectId = this.project?.id, { schedulePoll = true } = {}) {
        if (!projectId) {
            return null;
        }

        const requestId = ++this.runtimeStateRequestSerial;
        let state;
        try {
            state = await this.fetchRuntimeState(projectId);
        } catch (error) {
            if (requestId === this.runtimeStateRequestSerial && sameId(projectId, this.project?.id)) {
                throw error;
            }
            return null;
        }

        if (requestId !== this.runtimeStateRequestSerial || !sameId(projectId, this.project?.id)) {
            return null;
        }

        this.applyRuntimeState(state, 'endpoint');
        if (this.isRelevantRuntimeState(state, projectId) && this.isRuntimeStateBusy(state)) {
            if (schedulePoll) {
                this.startRuntimeStatePolling(projectId);
            }
        } else {
            this.stopRuntimeStatePolling();
        }
        return state;
    }

    async refreshRuntimeStateOnOpen(projectId = this.project?.id) {
        if (!projectId) {
            return null;
        }

        this.runtimeStateLoading = true;
        this.errorMessage = '';
        this.render();
        try {
            const state = await this.syncRuntimeStateAfterConflict(projectId, { schedulePoll: true });
            if (sameId(projectId, this.project?.id)) {
                this.errorMessage = '';
            }
            return state;
        } catch (error) {
            if (sameId(projectId, this.project?.id)) {
                this.errorMessage = this.toUserMessage(error, '运行状态刷新失败，已保留当前草稿；请稍后重试或等待运行状态更新。');
            }
            return null;
        } finally {
            if (sameId(projectId, this.project?.id)) {
                this.runtimeStateLoading = false;
                this.render();
            }
        }
    }

    async fetchRuntimeState(projectId) {
        if (typeof this.options.fetchRuntimeState === 'function') {
            return this.options.fetchRuntimeState(projectId);
        }
        return inspectionController.fetchRuntimeState(projectId);
    }

    applyRuntimeState(state, source = 'local') {
        this.runtimeState = state || {};
        this.runtimeStateSource = source;
        this.updateEndpointTerminalState(this.runtimeState, source);
        if (this.isOpen) {
            this.render();
        } else {
            this.render();
        }
    }

    isRuntimeStateBusy(state) {
        if (!state) {
            return false;
        }
        const status = String(state.status || state.Status || '').toLowerCase();
        return state.isBusy === true ||
            state.IsBusy === true ||
            state.isRunning === true ||
            state.isRealtime === true ||
            LOCKED_RUNTIME_STATES.has(status);
    }

    startRuntimeStatePolling(projectId = this.project?.id) {
        if (!projectId || this.runtimeStatePollTimer !== null) {
            return;
        }

        this.runtimeStatePollTimer = window.setTimeout(async () => {
            this.runtimeStatePollTimer = null;
            if (!this.isOpen || projectId !== this.project?.id) {
                return;
            }

            try {
                await this.syncRuntimeStateAfterConflict(projectId, { schedulePoll: true });
            } catch {
                if (this.isOpen && projectId === this.project?.id) {
                    this.startRuntimeStatePolling(projectId);
                }
            }
        }, RUNTIME_STATE_POLL_INTERVAL_MS);
    }

    stopRuntimeStatePolling({ cancelRequests = false } = {}) {
        if (this.runtimeStatePollTimer !== null) {
            window.clearTimeout(this.runtimeStatePollTimer);
            this.runtimeStatePollTimer = null;
        }
        if (cancelRequests) {
            this.runtimeStateRequestSerial += 1;
        }
    }

    destroy() {
        this.stopRuntimeStatePolling({ cancelRequests: true });
        this.endpointTerminalState = null;
        this.runtimeStateLoading = false;
        this.unsubscribeInspectionState?.();
        this.unsubscribeInspectionState = null;
        this.removeDialog();
    }

    isConflict(error) {
        const message = String(error?.message || error || '').toLowerCase();
        return message.includes('409') ||
            message.includes('conflict') ||
            message.includes('currently running') ||
            message.includes('正在运行');
    }

    toUserMessage(error, fallback) {
        const message = String(error?.message || error || '').trim();
        if (!message) {
            return fallback;
        }
        if (this.isConflict(error)) {
            return '工程正在运行，变量结构和值不可修改。';
        }
        if (message.includes('404')) {
            return '工程或变量不存在，请刷新后重试。';
        }
        if (message.includes('500')) {
            return `服务端处理失败：${message}`;
        }
        if (/failed to fetch|network/i.test(message)) {
            return `网络连接失败：${message}`;
        }
        if (/manual write is not allowed/i.test(message)) {
            return '该变量未允许人工写入。';
        }
        return message;
    }

    requestChoice(title, message, choices) {
        if (typeof this.options.requestChoice === 'function') {
            return Promise.resolve(this.options.requestChoice(title, message, choices));
        }

        return new Promise(resolve => {
            const overlay = document.createElement('div');
            overlay.className = 'gv-choice-overlay show';
            overlay.innerHTML = `
                <div class="gv-choice" role="dialog" aria-modal="true">
                    <h3>${escapeHtml(title)}</h3>
                    <p>${escapeHtml(message)}</p>
                    <div class="gv-actions">
                        ${choices.map(choice => `<button type="button" class="btn ${choice.value === 'cancel' ? 'btn-secondary' : 'btn-primary'}" data-choice="${escapeHtml(choice.value)}">${escapeHtml(choice.text)}</button>`).join('')}
                    </div>
                </div>
            `;
            document.body.appendChild(overlay);
            overlay.addEventListener('click', event => {
                const button = event.target.closest('[data-choice]');
                if (!button) {
                    return;
                }
                const value = button.dataset.choice;
                overlay.remove();
                resolve(value);
            });
        });
    }

    requestElementChoice(title, element) {
        return new Promise(resolve => {
            const overlay = document.createElement('div');
            overlay.className = 'gv-choice-overlay show';
            const dialog = document.createElement('div');
            dialog.className = 'gv-choice gv-source-dialog';
            dialog.setAttribute('role', 'dialog');
            dialog.setAttribute('aria-modal', 'true');
            const heading = document.createElement('h3');
            heading.textContent = title;
            const actions = document.createElement('div');
            actions.className = 'gv-actions';
            const cancel = document.createElement('button');
            cancel.type = 'button';
            cancel.className = 'btn btn-secondary';
            cancel.textContent = '取消';
            actions.appendChild(cancel);
            dialog.appendChild(heading);
            dialog.appendChild(element);
            dialog.appendChild(actions);
            overlay.appendChild(dialog);
            document.body.appendChild(overlay);
            cancel.addEventListener('click', () => {
                overlay.remove();
                resolve(null);
            });
            element.addEventListener('click', event => {
                const option = event.target.closest('.gv-source-option');
                if (!option) {
                    return;
                }
                overlay.remove();
                resolve({
                    operatorId: option.dataset.operatorId,
                    outputPortId: option.dataset.outputPortId
                });
            });
        });
    }

    removeDialog() {
        this.dialog?.remove?.();
        this.dialog = null;
    }

    toast(message, type = 'info') {
        if (typeof this.options.showToast === 'function') {
            this.options.showToast(message, type);
            return;
        }
        showSharedToast(message, type);
    }
}

function cloneSchema(schema) {
    return normalizeGlobalVariableSchema(JSON.parse(JSON.stringify(normalizeGlobalVariableSchema(schema))));
}

function schemaEquals(left, right) {
    return JSON.stringify(normalizeGlobalVariableSchema(left)) === JSON.stringify(normalizeGlobalVariableSchema(right));
}

function normalizeArray(value) {
    return Array.isArray(value) ? value : [];
}

function expressionReferencesVariable(expression, variableName) {
    const normalizedName = String(variableName || '').toLowerCase();
    if (!normalizedName) {
        return false;
    }

    const identifiers = String(expression || '').match(/[A-Za-z_][A-Za-z0-9_.]*/g) || [];
    return identifiers.some(identifier => identifier.toLowerCase() === normalizedName);
}

function normalizeImpactKey(value) {
    return String(value || '')
        .replace(/[：:]/g, ' ')
        .replace(/\s+/g, ' ')
        .trim()
        .toLowerCase();
}

function groupBy(items, getKey) {
    return items.reduce((groups, item) => {
        const key = getKey(item);
        groups[key] = groups[key] || [];
        groups[key].push(item);
        return groups;
    }, {});
}

function formatDateTime(value) {
    if (!value) {
        return '-';
    }
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString('zh-CN');
}

function formatUpdatedBy(value) {
    const text = String(value || '').trim();
    switch (text.toLowerCase()) {
        case '':
        case 'initial':
            return '初始值';
        case 'studiomanual':
            return 'Studio 写入';
        case 'stationmanual':
            return '工站写入';
        case 'operatoroutput':
            return '算子输出';
        case 'variablewrite':
            return '变量写入';
        case 'variableincrement':
            return '变量递增';
        case 'reset':
            return '重置';
        default:
            return text;
    }
}

function formatDataType(value) {
    const text = String(value || '').trim();
    switch (text.toLowerCase()) {
        case '':
        case 'any':
            return '任意';
        case 'string':
        case 'text':
            return '文本';
        case 'int':
        case 'integer':
        case 'long':
        case 'int64':
            return '整数';
        case 'double':
        case 'float':
        case 'number':
        case 'decimal':
            return '小数';
        case 'bool':
        case 'boolean':
            return '布尔';
        case 'image':
            return '图像';
        default:
            return text;
    }
}

function createUuid() {
    return globalThis.crypto?.randomUUID?.() || `gv-binding-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function cssEscape(value) {
    if (globalThis.CSS?.escape) {
        return globalThis.CSS.escape(String(value ?? ''));
    }
    return String(value ?? '').replaceAll('\\', '\\\\').replaceAll('"', '\\"');
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}
