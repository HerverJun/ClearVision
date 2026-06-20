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
    saveGlobalVariableSchema,
    serializeVariableDraft,
    loadGlobalVariableValues,
    writeGlobalVariableValue
} from './globalVariableStore.js';

const LOCKED_RUNTIME_STATES = new Set(['starting', 'running', 'stopping']);
const TYPE_FILTERS = ['全部类型', ...GLOBAL_VARIABLE_TYPES.map(getTypeLabel)];
const SOURCE_FILTERS = ['全部来源', '固定初始值', '算子输出'];

export default class GlobalVariablePanel {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        this.project = null;
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
    }

    async setProject(project) {
        const requestId = ++this.requestSerial;
        this.project = project || null;
        this.schema = normalizeGlobalVariableSchema(project?.globalVariables || project?.GlobalVariables);
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
        if (!this.selectedVariableId && this.schema.variables.length > 0) {
            this.selectedVariableId = this.schema.variables[0].id;
        }
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.fieldErrors = {};
        this.render();
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
        const valuesById = new Map(this.values.map(item => [String(item.variableId).toLowerCase(), item]));
        const filteredVariables = this.getFilteredVariables();
        const runtimeBanner = locked
            ? '<div class="gv-warning" role="status">工程运行中，变量结构和值不可修改；仍可查看与刷新。</div>'
            : '';
        const listHtml = filteredVariables.length === 0
            ? '<div class="gv-empty">没有符合条件的变量。</div>'
            : filteredVariables.map(variable => {
                const current = valuesById.get(String(variable.id).toLowerCase());
                const source = this.getSourceBinding(variable.id);
                return `
                    <button type="button" class="gv-variable-row ${sameId(variable.id, this.selectedVariableId) ? 'selected' : ''}" data-action="select" data-variable-id="${escapeHtml(variable.id)}">
                        <span class="gv-variable-name">${escapeHtml(variable.displayName || variable.name)}</span>
                        <span class="gv-variable-meta">${escapeHtml(variable.name)} · ${escapeHtml(getTypeLabel(variable.valueType))}</span>
                        <span class="gv-variable-value">${escapeHtml(formatGlobalVariableValue(current?.value ?? variable.initialValue))}</span>
                        <span class="gv-variable-source">${source ? '算子输出' : '固定初始值'}</span>
                    </button>
                `;
            }).join('');

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
                    <button type="button" class="btn btn-primary" data-action="new" ${locked ? 'disabled' : ''} title="新建变量">新建变量</button>
                    <button type="button" class="btn btn-secondary" data-action="refresh" ${this.pendingAction === 'refresh' ? 'disabled' : ''} title="刷新当前值">${this.pendingAction === 'refresh' ? '刷新中...' : '刷新'}</button>
                    <button type="button" class="btn btn-secondary" data-action="reset-all" ${locked || this.pendingAction === 'reset-all' ? 'disabled' : ''} title="全部重置">${this.pendingAction === 'reset-all' ? '重置中...' : '全部重置'}</button>
                </section>
                <main class="gv-manager-body">
                    <aside class="gv-variable-list" aria-label="变量列表">
                        ${this.loading ? '<div class="gv-loading">正在加载变量...</div>' : listHtml}
                    </aside>
                    <section class="gv-detail">
                        ${selected || this.draft ? this.renderEditorHtml() : this.renderEmptyDetailHtml()}
                    </section>
                </main>
            </div>
        `;
    }

    renderEmptyDetailHtml() {
        return `
            <div class="gv-empty gv-empty-detail">
                <h3>暂无变量</h3>
                <p>点击“新建变量”创建第一个全局变量。</p>
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
                            ${this.renderTextField('minText', '最小值', draft.minText, '可为空', false, draft.valueType !== 'Int64' && draft.valueType !== 'Double')}
                            ${this.renderTextField('maxText', '最大值', draft.maxText, '可为空', false, draft.valueType !== 'Int64' && draft.valueType !== 'Double')}
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

    renderTextField(field, label, value, placeholder, required = false, disabled = false) {
        return `
            <label class="gv-field">
                <span>${escapeHtml(label)}${required ? ' *' : ''}</span>
                <input class="form-input" data-field="${escapeHtml(field)}" value="${escapeHtml(value)}" placeholder="${escapeHtml(placeholder || '')}" ${disabled || this.isRuntimeLocked() ? 'disabled' : ''}>
                ${this.renderFieldError(field)}
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
                <button type="button" class="btn btn-secondary" data-action="reset-one" ${locked || this.pendingAction === 'reset-one' ? 'disabled' : ''}>${this.pendingAction === 'reset-one' ? '重置中...' : '重置'}</button>
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
            this.filters.search = target.value || '';
            this.renderDialog();
            return;
        }
        if (target.id === 'gv-type-filter') {
            this.filters.type = target.value || '全部类型';
            this.renderDialog();
            return;
        }
        if (target.id === 'gv-source-filter') {
            this.filters.source = target.value || '全部来源';
            this.renderDialog();
            return;
        }
        if (!target.dataset?.field || !this.draft) {
            return;
        }

        const field = target.dataset.field;
        if (target.type === 'checkbox') {
            this.draft[field] = target.checked;
        } else if (field === 'valueType') {
            this.draft.valueType = normalizeValueType(target.value);
            this.draft.initialValueText = formatValueForInput(this.draft.valueType, this.draft.initialValue);
            if (this.draft.valueType !== 'Int64' && this.draft.valueType !== 'Double') {
                this.draft.minText = '';
                this.draft.maxText = '';
            }
            this.renderDialog();
        } else if (field === 'order') {
            this.draft.order = Number.parseInt(target.value || '0', 10) || 0;
        } else {
            this.draft[field] = target.value;
        }

        this.dirty = true;
        this.fieldErrors = {};
    }

    async handleAction(action, target) {
        switch (action) {
            case 'close':
                await this.closeManager();
                break;
            case 'new':
                await this.selectVariable('');
                this.createNewDraft();
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
        this.dirty = false;
        this.fieldErrors = {};
        this.renderDialog();
        return true;
    }

    createNewDraft() {
        this.selectedVariableId = '';
        this.draft = createVariableDraft(null, this.schema.variables.length);
        this.dirty = true;
        this.fieldErrors = {};
        this.renderDialog();
    }

    discardDraft() {
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.dirty = false;
        this.fieldErrors = {};
        this.renderDialog();
    }

    async save() {
        if (this.isRuntimeLocked()) {
            this.toast('工程运行中，变量结构和值不可修改。', 'warning');
            return false;
        }
        if (!this.draft) {
            return false;
        }

        const original = this.getSelectedVariable();
        const serialized = serializeVariableDraft(this.draft, this.schema, original);
        if (!serialized.ok) {
            this.fieldErrors = serialized.errors;
            this.errorMessage = '请先修正表单中的错误。';
            this.renderDialog();
            return false;
        }

        const impact = original && normalizeValueType(original.valueType) !== normalizeValueType(serialized.variable.valueType)
            ? this.getIncompatibleBindings(original, serialized.variable.valueType)
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

        const nextSchema = normalizeGlobalVariableSchema(this.schema);
        const index = nextSchema.variables.findIndex(item => sameId(item.id, serialized.variable.id));
        if (index >= 0) {
            nextSchema.variables[index] = serialized.variable;
        } else {
            nextSchema.variables.push(serialized.variable);
        }
        nextSchema.variables.sort((left, right) => Number(left.order ?? 0) - Number(right.order ?? 0));

        return await this.runMutation('save', async () => {
            const projectId = this.project.id;
            const saved = await saveGlobalVariableSchema(projectId, nextSchema);
            if (this.project?.id !== projectId) {
                return false;
            }
            this.applySchema(saved, serialized.variable.id);
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
        this.dirty = true;
        this.syncSchemaToProject();
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
            outputPortName: output.outputPortName
        });
        this.dirty = true;
        this.syncSchemaToProject();
        this.render();
    }

    clearSourceBinding() {
        const variable = this.getSelectedVariable();
        if (!variable || this.isRuntimeLocked()) {
            return;
        }
        this.schema.sourceBindings = this.schema.sourceBindings.filter(item => !sameId(item.variableId, variable.id));
        this.dirty = true;
        this.syncSchemaToProject();
        this.render();
    }

    removeTargetBinding(bindingId) {
        if (this.isRuntimeLocked()) {
            return;
        }
        this.schema.targetBindings = this.schema.targetBindings.filter(item => !sameId(item.id, bindingId));
        this.dirty = true;
        this.syncSchemaToProject();
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
                this.errorMessage = '工程正在运行，变量结构和值不可修改。已重新获取当前状态和值。';
                await this.refreshValues({ requestId: this.requestSerial, render: false }).catch(() => {});
            } else {
                this.errorMessage = this.toUserMessage(error, '操作失败。');
            }
            if (!options.preserveDraftOnError && this.selectedVariableId) {
                this.draft = createVariableDraft(this.getSelectedVariable());
                this.dirty = false;
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

    applySchema(schema, selectedVariableId = this.selectedVariableId) {
        this.schema = normalizeGlobalVariableSchema(schema);
        this.selectedVariableId = selectedVariableId || this.schema.variables[0]?.id || '';
        this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        this.syncSchemaToProject();
    }

    setSchemaFromExternal(schema) {
        this.schema = normalizeGlobalVariableSchema(schema);
        if (this.selectedVariableId && !this.getSelectedVariable()) {
            this.selectedVariableId = this.schema.variables[0]?.id || '';
        }
        if (!this.dirty) {
            this.draft = this.selectedVariableId ? createVariableDraft(this.getSelectedVariable()) : null;
        }
        this.render();
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
                if (this.filters.type !== '全部类型' && getTypeLabel(variable.valueType) !== this.filters.type) {
                    return false;
                }
                const hasSource = Boolean(this.getSourceBinding(variable.id));
                if (this.filters.source === '固定初始值' && hasSource) {
                    return false;
                }
                if (this.filters.source === '算子输出' && !hasSource) {
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
        return [
            ...this.schema.sourceBindings.filter(item => sameId(item.variableId, variableId)).map(item => `来源：${item.operatorName || item.operatorId}.${item.outputPortName || item.outputPortId}`),
            ...this.schema.targetBindings.filter(item => sameId(item.variableId, variableId)).map(item => `目标：${item.operatorName || item.operatorId}.${item.parameterName || item.parameterId}`)
        ];
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

    describeBindingImpact(impact) {
        return `类型变更会影响以下绑定，请确认后再保存：${impact.join('；')}`;
    }

    getFlowOutputs() {
        const flow = this.project?.flow || this.project?.Flow;
        const operators = normalizeArray(flow?.operators ?? flow?.Operators);
        return operators.flatMap(operator => {
            const outputs = normalizeArray(operator.outputPorts ?? operator.OutputPorts);
            return outputs.map(port => ({
                operatorId: operator.id || operator.Id,
                operatorName: operator.name || operator.Name || operator.title || operator.Title || operator.type || operator.Type || '',
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
        const flow = this.project?.flow || this.project?.Flow;
        const operators = normalizeArray(flow?.operators ?? flow?.Operators);
        const operator = operators.find(item => sameId(item.id || item.Id, binding.operatorId));
        const parameters = normalizeArray(operator?.parameters ?? operator?.Parameters);
        const parameter = parameters.find(item => sameId(item.id || item.Id, binding.parameterId));
        return {
            valid: Boolean(operator && parameter),
            dataType: parameter?.dataType || parameter?.DataType || parameter?.type || parameter?.Type || ''
        };
    }

    isRuntimeLocked() {
        const state = this.options.getRuntimeState?.() || inspectionController.getState?.() || {};
        const status = String(state.status || state.Status || '').toLowerCase();
        return state.isRunning === true ||
            state.isRealtime === true ||
            LOCKED_RUNTIME_STATES.has(status);
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

function normalizeArray(value) {
    return Array.isArray(value) ? value : [];
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

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}
