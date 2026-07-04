import { getOperatorTypeDisplayName } from '../../shared/operatorDisplayNames.js';
import {
    collectEffectiveRequiredParameterErrors,
    getParameterEffectiveState
} from '../../shared/parameterDependencyRules.js';

export const PROPERTY_PANEL_CAPABILITY_OWNER_ID = 'property-panel-capability-v2';

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
    const div = document.createElement('div');
    div.textContent = value == null ? '' : String(value);
    return div.innerHTML;
}

function escapeAttribute(value) {
    return escapeHtml(value).replace(/"/g, '&quot;');
}

function getParameterName(parameter) {
    return String(parameter?.name ?? parameter?.Name ?? '').trim();
}

function getParameterLabel(parameter, fallbackName = '') {
    return String(
        parameter?.displayName ??
        parameter?.DisplayName ??
        parameter?.label ??
        parameter?.Label ??
        parameter?.name ??
        parameter?.Name ??
        fallbackName ??
        '参数'
    );
}

function getParameterDataType(parameter) {
    return String(
        parameter?.dataType ??
        parameter?.DataType ??
        parameter?.type ??
        parameter?.Type ??
        'string'
    ).trim().toLowerCase();
}

function getParameterDescription(parameter) {
    return String(parameter?.description ?? parameter?.Description ?? '').trim();
}

function getParameterValue(parameter) {
    return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue ?? null;
}

function getParameterRangeValue(parameter, ...keys) {
    for (const key of keys) {
        if (parameter?.[key] !== undefined && parameter?.[key] !== null && parameter?.[key] !== '') {
            const parsed = Number(parameter[key]);
            if (Number.isFinite(parsed)) {
                return parsed;
            }
        }
    }

    return null;
}

function isEmptyValue(value) {
    return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
}

function parseNumericValue(rawValue) {
    if (isEmptyValue(rawValue)) {
        return { empty: true, valid: false, value: null };
    }

    const value = Number(String(rawValue).trim());
    return {
        empty: false,
        valid: Number.isFinite(value),
        value: Number.isFinite(value) ? value : null
    };
}

function toBoolean(value) {
    if (typeof value === 'boolean') {
        return value;
    }

    return String(value).toLowerCase() === 'true';
}

function getParameterOptions(parameter) {
    const options = parameter?.options ?? parameter?.Options ?? [];
    return Array.isArray(options) ? options : [];
}

function getFormValue(input) {
    const dataType = String(input.dataset.type || '').toLowerCase();
    if (dataType === 'boolean' || dataType === 'bool') {
        return Boolean(input.checked);
    }

    if (dataType === 'int' || dataType === 'integer') {
        const parsed = parseNumericValue(input.value);
        return parsed.empty ? null : (parsed.valid && Number.isInteger(parsed.value) ? parsed.value : input.value);
    }

    if (dataType === 'double' || dataType === 'float' || dataType === 'number') {
        const parsed = parseNumericValue(input.value);
        return parsed.empty ? null : (parsed.valid ? parsed.value : input.value);
    }

    return input.value;
}

export class PropertyPanelCapabilityOwner {
    constructor(container, {
        propertyAdapter,
        showToast = () => {}
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('PropertyPanelCapabilityOwner requires a container.');
        }
        if (!propertyAdapter) {
            throw new Error('PropertyPanelCapabilityOwner requires a property adapter.');
        }

        this.propertyAdapter = propertyAdapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.currentOperator = null;
        this.currentNodeId = null;
        this.validationErrors = [];
        this.statusMessage = '';
        this.lastChangedParameterName = null;
        this.dirty = false;
        this.disposed = false;
        this.unsubscribes = [];

        this.handleContainerChange = this.handleContainerChange.bind(this);
        this.handleContainerSubmit = this.handleContainerSubmit.bind(this);

        this.container.dataset.propertyPanelOwner = PROPERTY_PANEL_CAPABILITY_OWNER_ID;
        this.container.addEventListener('change', this.handleContainerChange);
        this.container.addEventListener('submit', this.handleContainerSubmit);

        this.unsubscribes.push(
            this.propertyAdapter.subscribeSelectedNode((operator, state) => {
                this.handleSelectedNodeChanged(operator, state);
            }),
            this.propertyAdapter.subscribeFlowChanges((state) => {
                this.handleFlowChanged(state);
            })
        );

        this.render();
    }

    handleSelectedNodeChanged(operator) {
        if (this.disposed) {
            return;
        }

        this.currentOperator = operator || null;
        this.currentNodeId = operator?.id || null;
        this.validationErrors = [];
        this.statusMessage = '';
        this.lastChangedParameterName = null;
        this.dirty = false;
        this.render();
    }

    handleFlowChanged() {
        if (this.disposed || !this.currentNodeId) {
            return;
        }

        const operator = this.propertyAdapter.getSelectedOperatorSnapshot(this.currentNodeId);
        if (!operator) {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.validationErrors = [];
            this.statusMessage = '';
            this.lastChangedParameterName = null;
            this.dirty = false;
            this.render();
            return;
        }

        this.currentOperator = operator;
        this.render();
    }

    handleContainerSubmit(event) {
        event.preventDefault();
        this.applyChanges();
    }

    handleContainerChange(event) {
        const input = event.target?.closest?.('[data-property-parameter="true"]');
        if (!input || !this.currentNodeId || this.disposed) {
            return;
        }

        const values = this.collectFormValues();
        const errors = this.validateValues(values);
        if (errors.length > 0) {
            this.validationErrors = errors;
            this.statusMessage = '参数校验失败';
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }

        const parameterName = input.name;
        const result = this.propertyAdapter.writeParameters(this.currentNodeId, {
            [parameterName]: values[parameterName]
        });

        if (result?.reason === 'node_not_found') {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.render();
            return;
        }

        this.validationErrors = [];
        this.statusMessage = '参数已更新';
        this.lastChangedParameterName = parameterName;
        this.dirty = result?.updated === true;
        this.renderValidationErrors();
        this.updateStatus();
    }

    setOperator(operator) {
        this.handleSelectedNodeChanged(operator);
    }

    clear() {
        this.currentOperator = null;
        this.currentNodeId = null;
        this.validationErrors = [];
        this.statusMessage = '';
        this.lastChangedParameterName = null;
        this.dirty = false;
        this.render();
    }

    collectFormValues() {
        const form = this.container.querySelector('[data-property-capability-form="true"]');
        if (!form) {
            return {};
        }

        const values = {};
        form.querySelectorAll('[data-property-parameter="true"]').forEach(input => {
            if (!input.name) {
                return;
            }

            values[input.name] = getFormValue(input);
        });

        return values;
    }

    validateValues(values) {
        if (!this.currentOperator) {
            return [];
        }

        return this.validateOperatorModel(this.currentOperator, { values });
    }

    validateOperatorModel(operator, options = {}) {
        const values = options.values || null;
        const parameters = Array.isArray(operator?.parameters) ? operator.parameters : [];
        const errors = [];

        collectEffectiveRequiredParameterErrors(operator, parameters, {
            values,
            getLabel: (parameter, fallbackName) => getParameterLabel(parameter, fallbackName)
        }).forEach(error => {
            errors.push({ name: error.name, message: error.message });
        });

        parameters.forEach(parameter => {
            const name = getParameterName(parameter);
            if (!name) {
                return;
            }

            const state = getParameterEffectiveState(operator, parameter, { values });
            if (state.effectiveDisabled) {
                return;
            }

            const dataType = getParameterDataType(parameter);
            const value = values && Object.prototype.hasOwnProperty.call(values, name)
                ? values[name]
                : getParameterValue(parameter);

            if (!['int', 'integer', 'double', 'float', 'number'].includes(dataType) || isEmptyValue(value)) {
                return;
            }

            const parsed = parseNumericValue(value);
            const label = getParameterLabel(parameter, name);
            if (!parsed.valid) {
                errors.push({ name, message: `${label} 必须是有效数字` });
                return;
            }

            if ((dataType === 'int' || dataType === 'integer') && !Number.isInteger(parsed.value)) {
                errors.push({ name, message: `${label} 必须是整数` });
                return;
            }

            const min = getParameterRangeValue(parameter, 'min', 'Min', 'minValue', 'MinValue');
            const max = getParameterRangeValue(parameter, 'max', 'Max', 'maxValue', 'MaxValue');
            if (min !== null && parsed.value < min) {
                errors.push({ name, message: `${label} 不能小于 ${min}` });
                return;
            }
            if (max !== null && parsed.value > max) {
                errors.push({ name, message: `${label} 不能大于 ${max}` });
            }
        });

        return errors;
    }

    validateCurrentOperator(options = {}) {
        const errors = this.validateValues(this.collectFormValues());
        this.validationErrors = errors;

        if (errors.length > 0) {
            this.statusMessage = '参数校验失败';
            if (options.showToast) {
                this.showToast(`参数校验失败：${errors[0].message}`, 'error');
            }
            this.renderValidationErrors();
            this.updateStatus();
            return false;
        }

        this.renderValidationErrors();
        this.updateStatus();
        return true;
    }

    validateFlowForAction(flowCanvas, options = {}) {
        const action = options.action || '执行';
        if (this.currentOperator && this.validateCurrentOperator({
            showToast: options.showToast === true
        }) === false) {
            return false;
        }

        const nodes = flowCanvas?.nodes instanceof Map
            ? Array.from(flowCanvas.nodes.values())
            : Array.from(this.propertyAdapter.flowCanvasAdapter?.nodes?.values?.() || []);
        for (const node of nodes) {
            if (!node?.id || node.id === this.currentNodeId) {
                continue;
            }

            const operator = this.propertyAdapter.getSelectedOperatorSnapshot(node.id);
            const errors = this.validateOperatorModel(operator);
            if (errors.length === 0) {
                continue;
            }

            this.propertyAdapter.selectNode(node.id);
            if (options.showToast) {
                this.showToast(`${action}前请先修正参数：${errors[0].message}`, 'error');
            }
            return false;
        }

        return true;
    }

    applyChanges(options = {}) {
        const showToast = options.showToast !== false;
        if (!this.currentNodeId || !this.currentOperator) {
            return true;
        }

        const values = this.collectFormValues();
        const errors = this.validateValues(values);
        if (errors.length > 0) {
            this.validationErrors = errors;
            this.statusMessage = '参数校验失败';
            if (showToast) {
                this.showToast(`参数校验失败：${errors[0].message}`, 'error');
            }
            this.renderValidationErrors();
            this.updateStatus();
            return false;
        }

        const result = this.propertyAdapter.writeParameters(this.currentNodeId, values);
        this.validationErrors = [];
        this.statusMessage = '参数已更新';
        this.lastChangedParameterName = null;
        this.dirty = result?.updated === true;
        if (showToast) {
            this.showToast('参数已更新', 'success');
        }
        this.renderValidationErrors();
        this.updateStatus();
        return result?.reason !== 'node_not_found';
    }

    syncDraftChanges(options = {}) {
        return this.applyChanges({
            showToast: options.showToast === true
        });
    }

    render() {
        if (this.disposed) {
            return;
        }

        if (!this.currentOperator) {
            this.container.innerHTML = `
                <section class="property-capability-owner property-capability-empty" data-owner="${PROPERTY_PANEL_CAPABILITY_OWNER_ID}">
                    <div class="property-capability-title">属性面板</div>
                    <p class="property-capability-empty-title">未选择算子</p>
                    <p class="empty-text">请选择一个算子</p>
                </section>
            `;
            return;
        }

        const operator = this.currentOperator;
        const title = operator.title || operator.displayName || operator.type || '算子';
        const type = operator.type || '';
        const typeDisplay = getOperatorTypeDisplayName(type, { includeType: true }) || type;
        const parameters = Array.isArray(operator.parameters) ? operator.parameters : [];

        this.container.innerHTML = `
            <section class="property-capability-owner" data-owner="${PROPERTY_PANEL_CAPABILITY_OWNER_ID}">
                <header class="property-header property-capability-header">
                    <div class="header-text">
                        <div class="property-capability-title">属性面板</div>
                        <h4>${escapeHtml(title)}</h4>
                        <span class="property-type">${escapeHtml(typeDisplay)}</span>
                    </div>
                </header>
                <div class="property-capability-scroll" data-low-height-scroll="true">
                    <section class="property-summary-section">
                        <h5>基础信息</h5>
                        <dl class="property-capability-meta">
                            <div>
                                <dt>当前算子</dt>
                                <dd>${escapeHtml(title)}</dd>
                            </div>
                            <div>
                                <dt>类型</dt>
                                <dd>${escapeHtml(typeDisplay)}</dd>
                            </div>
                        </dl>
                    </section>
                    <section class="property-summary-section">
                        <h5>参数</h5>
                        ${parameters.length === 0
                            ? '<p class="property-summary-empty">当前算子没有可编辑参数</p>'
                            : `<form class="property-form property-capability-form" data-property-capability-form="true">
                                ${parameters.map(parameter => this.renderParameterField(parameter)).join('')}
                                <button type="submit" class="hidden" aria-hidden="true" tabindex="-1">应用</button>
                            </form>`}
                    </section>
                </div>
                <div class="property-capability-status" data-property-capability-status aria-live="polite">
                    ${escapeHtml(this.statusMessage)}
                </div>
            </section>
        `;
        this.renderValidationErrors();
        this.updateStatus();
    }

    renderParameterField(parameter) {
        const name = getParameterName(parameter);
        if (!name) {
            return '';
        }

        const label = getParameterLabel(parameter, name);
        const dataType = getParameterDataType(parameter);
        const description = getParameterDescription(parameter);
        const value = getParameterValue(parameter);
        const values = this.currentOperator ? null : {};
        const effectiveState = getParameterEffectiveState(this.currentOperator, parameter, { values });
        const requiredMark = effectiveState.effectiveRequired ? '<span class="required">*</span>' : '';
        const disabled = effectiveState.effectiveDisabled ? 'disabled aria-disabled="true"' : '';
        const disabledHint = effectiveState.effectiveDisabled && effectiveState.disabledReason
            ? `<p class="form-description parameter-rule-hint">${escapeHtml(effectiveState.disabledReason)}</p>`
            : '';

        return `
            <div class="form-group ${effectiveState.effectiveDisabled ? 'is-rule-disabled' : ''}" data-parameter-name="${escapeAttribute(name)}">
                <label for="v2-param-${escapeAttribute(name)}" class="form-label">${escapeHtml(label)} ${requiredMark}</label>
                ${this.renderInput(parameter, { name, dataType, value, disabled })}
                ${description ? `<p class="form-description">${escapeHtml(description)}</p>` : ''}
                ${disabledHint}
            </div>
        `;
    }

    renderInput(parameter, { name, dataType, value, disabled }) {
        const inputId = `v2-param-${escapeAttribute(name)}`;
        const common = `id="${inputId}" name="${escapeAttribute(name)}" data-property-parameter="true"`;
        const currentValue = value ?? '';

        if (dataType === 'boolean' || dataType === 'bool') {
            return `
                <label class="property-toggle">
                    <input type="checkbox" ${common} data-type="boolean" ${toBoolean(currentValue) ? 'checked' : ''} ${disabled}>
                    <span class="toggle-slider"></span>
                </label>
            `;
        }

        if (dataType === 'enum' || dataType === 'select') {
            const options = getParameterOptions(parameter);
            if (options.length > 0) {
                return `
                    <select ${common} class="form-select" data-type="enum" ${disabled}>
                        ${options.map(option => {
                            const optionLabel = typeof option === 'string' ? option : (option.label ?? option.Label ?? option.value ?? option.Value ?? '');
                            const optionValue = typeof option === 'string' ? option : (option.value ?? option.Value ?? optionLabel);
                            return `<option value="${escapeAttribute(optionValue)}" ${String(optionValue) === String(currentValue) ? 'selected' : ''}>${escapeHtml(optionLabel)}</option>`;
                        }).join('')}
                    </select>
                `;
            }
        }

        if (['int', 'integer', 'double', 'float', 'number'].includes(dataType)) {
            const min = getParameterRangeValue(parameter, 'min', 'Min', 'minValue', 'MinValue');
            const max = getParameterRangeValue(parameter, 'max', 'Max', 'maxValue', 'MaxValue');
            const step = parameter?.step ?? parameter?.Step ?? (dataType === 'int' || dataType === 'integer' ? 1 : 0.1);
            return `
                <input type="number"
                       ${common}
                       value="${escapeAttribute(currentValue)}"
                       ${min !== null ? `min="${min}"` : ''}
                       ${max !== null ? `max="${max}"` : ''}
                       step="${escapeAttribute(step)}"
                       class="form-input number-input"
                       data-type="${dataType}"
                       ${disabled}>
            `;
        }

        return `
            <input type="text"
                   ${common}
                   value="${escapeAttribute(currentValue)}"
                   class="form-input"
                   data-type="${escapeAttribute(dataType || 'string')}"
                   ${disabled}>
        `;
    }

    findInputForError(name) {
        return Array.from(this.container.querySelectorAll('[data-property-parameter="true"]'))
            .find(input => String(input.name || '').toLowerCase() === String(name || '').toLowerCase()) || null;
    }

    renderValidationErrors() {
        this.container.querySelectorAll('.form-group.invalid').forEach(group => {
            group.classList.remove('invalid');
        });
        this.container.querySelectorAll('[data-validation-error="true"]').forEach(error => {
            error.remove();
        });

        this.validationErrors.forEach(error => {
            const input = this.findInputForError(error.name);
            const group = input?.closest?.('.form-group');
            if (!group) {
                return;
            }

            group.classList.add('invalid');
            const message = document.createElement('p');
            message.className = 'form-description validation-error';
            message.dataset.validationError = 'true';
            message.textContent = error.message;
            group.appendChild(message);
        });
    }

    updateStatus() {
        const status = this.container.querySelector('[data-property-capability-status]');
        if (!status) {
            return;
        }

        status.textContent = this.statusMessage || '';
        status.classList.toggle('is-error', this.statusMessage === '参数校验失败');
        status.classList.toggle('is-success', this.statusMessage === '参数已更新');
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
                // Ignore best-effort subscription cleanup failures.
            }
        });
        this.unsubscribes = [];
        this.container.removeEventListener('change', this.handleContainerChange);
        this.container.removeEventListener('submit', this.handleContainerSubmit);
        delete this.container.dataset.propertyPanelOwner;
        this.container.innerHTML = '';
        this.currentOperator = null;
        this.currentNodeId = null;
    }
}

export default PropertyPanelCapabilityOwner;
