import httpClient from '../../core/messaging/httpClient.js';
import projectManager, { getCurrentProject, subscribeProject } from '../project/projectManager.js';
import { showToast } from '../../shared/components/uiComponents.js';

const ISSUE_TEXT = Object.freeze({
    DECISION_BINDING_REQUIRED: '尚未配置最终判定。请选择一个可用于 OK/NG 判定的算子输出。',
    DECISION_SOURCE_OPERATOR_NOT_FOUND: '绑定的来源算子已被删除，请重新选择。',
    DECISION_SOURCE_OPERATOR_DISABLED: '绑定的来源算子已禁用，请启用算子或重新选择。',
    DECISION_SOURCE_OUTPUT_NOT_FOUND: '绑定的输出端口已不存在，请重新选择。',
    DECISION_SOURCE_OUTPUT_MISMATCH: '绑定端口标识与名称不一致，请重新选择输出。',
    DECISION_SOURCE_TYPE_MISMATCH: '绑定输出类型已变化，当前规则不再兼容。',
    DECISION_SOURCE_OUTPUT_INELIGIBLE: '该输出未被后端算子契约声明为正式判定事实源，请重新选择。',
    DECISION_RULE_TYPE_MISMATCH: '判定规则与输出类型不兼容。',
    DECISION_RULE_CONTRACT_MISMATCH: '判定规则与后端候选契约不一致，请重新选择来源。',
    DECISION_STRING_MAP_VALUES_REQUIRED: '请同时填写代表 OK 和 NG 的字符串值。',
    DECISION_STRING_MAP_VALUES_CONFLICT: 'OK 与 NG 字符串值不能相同。',
    DECISION_STRING_MAP_CONSTRAINT_MISMATCH: '字符串映射与该输出的已知有限值域不一致。',
    DECISION_NUMERIC_COMPARISON_REQUIRED: '请配置数值比较符和有效阈值。'
});

const COMPARATOR_OPTIONS = [
    ['Equal', '='],
    ['NotEqual', '≠'],
    ['GreaterThan', '>'],
    ['GreaterThanOrEqual', '≥'],
    ['LessThan', '<'],
    ['LessThanOrEqual', '≤']
];

function clone(value) {
    return value ? JSON.parse(JSON.stringify(value)) : null;
}

function read(source, camel, pascal) {
    return source?.[camel] ?? source?.[pascal] ?? null;
}

function escapeHtml(value) {
    const div = document.createElement('div');
    div.textContent = value === null || value === undefined ? '' : String(value);
    return div.innerHTML;
}

export class FinalDecisionPanel {
    constructor(flowCanvas) {
        this.flowCanvas = flowCanvas;
        this.button = document.getElementById('btn-final-decision');
        this.overlay = null;
        this.validation = null;
        this.validationTimer = null;
        this.validationRequestId = 0;
        this.disposers = [];

        this.onButtonClick = () => this.open();
        this.onExternalOpen = event => this.open(event?.detail || null);
        this.button?.addEventListener('click', this.onButtonClick);
        window.addEventListener('clearvision:open-final-decision', this.onExternalOpen);
        this.disposers.push(() => this.button?.removeEventListener('click', this.onButtonClick));
        this.disposers.push(() => window.removeEventListener('clearvision:open-final-decision', this.onExternalOpen));
        this.disposers.push(this.flowCanvas?.subscribeStructureState?.(() => this.scheduleValidation()) || (() => {}));
        this.disposers.push(subscribeProject(() => this.scheduleValidation()));
        this.scheduleValidation();
    }

    getFlowSnapshot() {
        return this.flowCanvas?.serialize?.() || getCurrentProject()?.flow || null;
    }

    getConfiguration() {
        const flow = this.getFlowSnapshot();
        return clone(read(flow, 'decisionConfiguration', 'DecisionConfiguration'));
    }

    scheduleValidation(delay = 120) {
        if (this.validationTimer !== null) {
            clearTimeout(this.validationTimer);
        }
        this.validationTimer = setTimeout(() => {
            this.validationTimer = null;
            void this.validate();
        }, delay);
    }

    async validate() {
        const flow = this.getFlowSnapshot();
        if (!flow || !Array.isArray(flow.operators) || flow.operators.length === 0) {
            this.validation = { isValid: false, issues: [], eligibleOutputs: [] };
            this.updateButtonState('unconfigured', '最终判定：等待流程');
            this.render();
            return this.validation;
        }

        const requestId = ++this.validationRequestId;
        try {
            const response = await httpClient.post('/inspection/decision-configuration/validate', flow);
            if (requestId !== this.validationRequestId) {
                return this.validation;
            }
            this.validation = {
                isValid: Boolean(read(response, 'isValid', 'IsValid')),
                issues: read(response, 'issues', 'Issues') || [],
                eligibleOutputs: read(response, 'eligibleOutputs', 'EligibleOutputs') || []
            };
            this.updateButtonState(
                this.validation.isValid ? 'valid' : 'invalid',
                this.validation.isValid ? '最终判定：有效' : '最终判定：需要修复');
            this.render();
            return this.validation;
        } catch (error) {
            if (requestId !== this.validationRequestId) {
                return this.validation;
            }
            this.validation = { isValid: false, issues: [], eligibleOutputs: [] };
            this.updateButtonState('invalid', '最终判定：验证失败');
            this.renderValidationFailure(error);
            return this.validation;
        }
    }

    updateButtonState(state, title) {
        if (!this.button) return;
        this.button.dataset.decisionState = state;
        this.button.title = title;
        this.button.setAttribute('aria-label', title);
    }

    async open(context = null) {
        if (!getCurrentProject()) {
            showToast('请先创建或打开工程', 'warning');
            return;
        }
        if (!this.overlay) {
            this.overlay = document.createElement('div');
            this.overlay.className = 'final-decision-overlay';
            this.overlay.innerHTML = '<section class="final-decision-dialog" role="dialog" aria-modal="true" aria-label="最终判定配置"></section>';
            document.body.appendChild(this.overlay);
            this.overlay.addEventListener('click', event => {
                if (event.target === this.overlay || event.target.closest('[data-decision-close]')) {
                    this.close();
                }
            });
        }
        this.overlay.classList.add('show');
        this.overlay.dataset.admissionCode = context?.code || context?.Code || '';
        await this.validate();
    }

    close() {
        this.overlay?.classList.remove('show');
    }

    renderValidationFailure(error) {
        if (!this.overlay?.classList.contains('show')) return;
        const dialog = this.overlay.querySelector('.final-decision-dialog');
        if (dialog) {
            dialog.innerHTML = `
                <header><div><strong>最终判定</strong><span>无法读取后端验证结果</span></div><button data-decision-close aria-label="关闭">×</button></header>
                <div class="final-decision-empty">${escapeHtml(error?.message || '验证失败')}</div>`;
        }
    }

    render() {
        if (!this.overlay?.classList.contains('show')) return;
        const dialog = this.overlay.querySelector('.final-decision-dialog');
        if (!dialog) return;

        const validation = this.validation || { isValid: false, issues: [], eligibleOutputs: [] };
        const configuration = this.getConfiguration() || {};
        const binding = read(configuration, 'finalDecisionBinding', 'FinalDecisionBinding') || {};
        const sourceOperatorId = String(read(binding, 'sourceOperatorId', 'SourceOperatorId') || '').toLowerCase();
        const sourceOutputPortId = String(read(binding, 'sourceOutputPortId', 'SourceOutputPortId') || '').toLowerCase();
        const sourceOutputName = String(read(binding, 'sourceOutputName', 'SourceOutputName') || '').toLowerCase();
        const candidates = validation.eligibleOutputs;
        const selectedCandidate = candidates.find(candidate =>
            this.candidateKey(candidate) === `${sourceOperatorId}:${sourceOutputPortId}` ||
            (String(read(candidate, 'operatorId', 'OperatorId') || '').toLowerCase() === sourceOperatorId &&
             String(read(candidate, 'outputName', 'OutputName') || '').toLowerCase() === sourceOutputName)) || null;
        const selectedKey = selectedCandidate ? this.candidateKey(selectedCandidate) : '';
        const dataType = String(read(binding, 'dataType', 'DataType') || read(selectedCandidate, 'dataType', 'DataType') || 'Boolean');
        const rule = String(read(selectedCandidate, 'rule', 'Rule') || read(binding, 'rule', 'Rule') || '');
        const missingPolicy = String(read(configuration, 'missingDecisionPolicy', 'MissingDecisionPolicy') || 'Undetermined');

        dialog.innerHTML = `
            <header>
                <div><strong>最终判定</strong><span>正式运行的 OK/NG 事实源</span></div>
                <button data-decision-close aria-label="关闭">×</button>
            </header>
            <div class="final-decision-body">
                ${this.renderStatus(validation)}
                <label class="final-decision-field">
                    <span>判定来源</span>
                    <select data-decision-source>
                        <option value="">请选择兼容输出</option>
                        ${candidates.map(candidate => {
                            const key = this.candidateKey(candidate);
                            const operatorName = read(candidate, 'operatorName', 'OperatorName');
                            const outputName = read(candidate, 'outputName', 'OutputName');
                            const candidateType = read(candidate, 'dataType', 'DataType');
                            return `<option value="${escapeHtml(key)}" ${key === selectedKey ? 'selected' : ''}>${escapeHtml(operatorName)} · ${escapeHtml(outputName)} (${escapeHtml(candidateType)})</option>`;
                        }).join('')}
                    </select>
                    <small>仅显示后端算子契约明确认可的判定信号或可比较测量值。</small>
                </label>
                ${selectedCandidate ? this.renderRuleFields(rule, binding) : '<div class="final-decision-empty">选择来源后配置 OK/NG 规则。</div>'}
                <label class="final-decision-field">
                    <span>缺失信号策略</span>
                    <select data-decision-missing-policy>
                        ${this.option('Undetermined', '未判定', missingPolicy)}
                        ${this.option('NotApplicable', '不适用', missingPolicy)}
                        ${this.option('Invalid', '判定无效', missingPolicy)}
                    </select>
                    <small>算子未输出绑定值时采用的 canonical outcome。</small>
                </label>
                <div class="final-decision-rule-summary">${this.renderRuleSummary(rule, binding, missingPolicy)}</div>
            </div>
            <footer>
                <button type="button" class="btn btn-secondary" data-decision-clear>清除绑定</button>
                <span></span>
                <button type="button" class="btn btn-secondary" data-decision-close>关闭</button>
                <button type="button" class="btn btn-primary" data-decision-save>应用并保存工程</button>
            </footer>`;

        dialog.querySelector('[data-decision-source]')?.addEventListener('change', event => {
            this.applySource(event.target.value);
        });
        dialog.querySelectorAll('[data-decision-input]').forEach(element => {
            element.addEventListener('change', () => this.applyForm(dialog));
            element.addEventListener('input', () => this.applyForm(dialog));
        });
        dialog.querySelector('[data-decision-missing-policy]')?.addEventListener('change', () => this.applyForm(dialog));
        dialog.querySelector('[data-decision-clear]')?.addEventListener('click', () => this.clearBinding());
        dialog.querySelector('[data-decision-save]')?.addEventListener('click', () => this.save());
    }

    renderStatus(validation) {
        if (validation.isValid) {
            return '<div class="final-decision-status valid"><strong>配置有效</strong><span>正式运行准入已满足。</span></div>';
        }
        const issues = validation.issues.length > 0
            ? validation.issues.map(issue => {
                const code = read(issue, 'code', 'Code');
                const message = ISSUE_TEXT[code] || read(issue, 'message', 'Message') || code;
                return `<li><code>${escapeHtml(code)}</code><span>${escapeHtml(message)}</span></li>`;
            }).join('')
            : '<li><span>请完成最终判定绑定。</span></li>';
        return `<div class="final-decision-status invalid"><strong>配置需要修复</strong><ul>${issues}</ul></div>`;
    }

    renderRuleFields(rule, binding) {
        if (rule === 'Boolean') {
            const trueMeansOk = read(binding, 'trueMeansOk', 'TrueMeansOk') !== false;
            return `
                <label class="final-decision-field"><span>布尔规则</span>
                    <select data-decision-input="trueMeansOk">
                        ${this.option('true', 'true = OK，false = NG', String(trueMeansOk))}
                        ${this.option('false', 'true = NG，false = OK', String(trueMeansOk))}
                    </select>
                </label>`;
        }
        if (rule === 'StringMap') {
            return `
                <div class="final-decision-grid">
                    <label class="final-decision-field"><span>OK 值</span><input data-decision-input="okValue" value="${escapeHtml(read(binding, 'okValue', 'OkValue') || '')}" /></label>
                    <label class="final-decision-field"><span>NG 值</span><input data-decision-input="ngValue" value="${escapeHtml(read(binding, 'ngValue', 'NgValue') || '')}" /></label>
                </div>`;
        }
        const comparator = String(read(binding, 'comparator', 'Comparator') || '');
        const threshold = read(binding, 'threshold', 'Threshold') ?? '';
        return `
            <div class="final-decision-grid">
                <label class="final-decision-field"><span>OK 比较</span><select data-decision-input="comparator"><option value="">请选择</option>${COMPARATOR_OPTIONS.map(([value, label]) => this.option(value, label, comparator)).join('')}</select></label>
                <label class="final-decision-field"><span>阈值</span><input type="number" step="any" data-decision-input="threshold" value="${escapeHtml(threshold)}" /></label>
            </div>`;
    }

    renderRuleSummary(rule, binding, missingPolicy) {
        let ruleText = '尚未选择判定来源。';
        if (rule === 'Boolean') {
            ruleText = read(binding, 'trueMeansOk', 'TrueMeansOk') !== false
                ? 'OK：true；NG：false'
                : 'OK：false；NG：true';
        } else if (rule === 'StringMap') {
            ruleText = `OK：“${read(binding, 'okValue', 'OkValue') || '未设置'}”；NG：“${read(binding, 'ngValue', 'NgValue') || '未设置'}”`;
        } else if (rule === 'NumericComparison') {
            const comparator = COMPARATOR_OPTIONS.find(([value]) => value === read(binding, 'comparator', 'Comparator'))?.[1];
            const threshold = read(binding, 'threshold', 'Threshold');
            ruleText = comparator && threshold !== null
                ? `OK：值 ${comparator} ${threshold}；NG：不满足该条件`
                : '请设置比较符和阈值。';
        }
        const missingText = { Undetermined: '未判定', NotApplicable: '不适用', Invalid: '判定无效' }[missingPolicy] || missingPolicy;
        return `<strong>规则摘要</strong><span>${escapeHtml(ruleText)}</span><span>缺失信号：${escapeHtml(missingText)}</span>`;
    }

    candidateKey(candidate) {
        return `${String(read(candidate, 'operatorId', 'OperatorId') || '').toLowerCase()}:${String(read(candidate, 'outputPortId', 'OutputPortId') || '').toLowerCase()}`;
    }

    option(value, label, selected) {
        return `<option value="${escapeHtml(value)}" ${String(value) === String(selected) ? 'selected' : ''}>${escapeHtml(label)}</option>`;
    }

    applySource(key) {
        const candidate = this.validation?.eligibleOutputs?.find(item => this.candidateKey(item) === key);
        const current = this.getConfiguration() || {};
        if (!candidate) {
            current.finalDecisionBinding = null;
            this.setConfiguration(current);
            return;
        }
        const dataType = String(read(candidate, 'dataType', 'DataType'));
        const rule = String(read(candidate, 'rule', 'Rule'));
        const binding = {
            sourceOperatorId: read(candidate, 'operatorId', 'OperatorId'),
            sourceOutputPortId: read(candidate, 'outputPortId', 'OutputPortId'),
            sourceOutputName: read(candidate, 'outputName', 'OutputName'),
            dataType,
            rule
        };
        if (rule === 'Boolean') {
            binding.trueMeansOk = read(candidate, 'defaultTrueMeansOk', 'DefaultTrueMeansOk');
        } else if (rule === 'StringMap') {
            binding.okValue = read(candidate, 'defaultOkValue', 'DefaultOkValue');
            binding.ngValue = read(candidate, 'defaultNgValue', 'DefaultNgValue');
        } else if (rule === 'NumericComparison') {
            binding.comparator = null;
            binding.threshold = null;
        }
        current.finalDecisionBinding = binding;
        current.missingDecisionPolicy = current.missingDecisionPolicy || 'Undetermined';
        this.setConfiguration(current);
    }

    applyForm(dialog) {
        const configuration = this.getConfiguration() || {};
        const binding = configuration.finalDecisionBinding || configuration.FinalDecisionBinding;
        if (!binding) return;
        const value = name => dialog.querySelector(`[data-decision-input="${name}"]`)?.value;
        const trueMeansOk = value('trueMeansOk');
        if (trueMeansOk !== undefined) binding.trueMeansOk = trueMeansOk === 'true';
        if (value('okValue') !== undefined) binding.okValue = value('okValue');
        if (value('ngValue') !== undefined) binding.ngValue = value('ngValue');
        if (value('comparator') !== undefined) binding.comparator = value('comparator');
        if (value('threshold') !== undefined) {
            const rawThreshold = value('threshold').trim();
            const parsed = rawThreshold === '' ? Number.NaN : Number(rawThreshold);
            binding.threshold = Number.isFinite(parsed) ? parsed : null;
        }
        configuration.finalDecisionBinding = binding;
        configuration.missingDecisionPolicy = dialog.querySelector('[data-decision-missing-policy]')?.value || 'Undetermined';
        this.setConfiguration(configuration, false);
    }

    clearBinding() {
        this.setConfiguration({ finalDecisionBinding: null, missingDecisionPolicy: 'Undetermined' });
    }

    setConfiguration(configuration, rerender = true) {
        this.flowCanvas.decisionConfiguration = clone(configuration);
        this.flowCanvas.markFlowStructureChanged?.('decisionConfiguration');
        projectManager.updateFlow?.(this.flowCanvas.serialize());
        this.scheduleValidation(80);
        if (rerender) this.render();
    }

    async save() {
        try {
            const validation = await this.validate();
            if (!validation?.isValid) {
                showToast('最终判定配置仍有问题，请按提示修复', 'warning');
                return;
            }
            projectManager.updateFlow?.(this.flowCanvas.serialize());
            await projectManager.saveProject(projectManager.getCurrentProject?.() || getCurrentProject());
            showToast('最终判定配置已保存', 'success');
            this.close();
        } catch (error) {
            showToast(`最终判定保存失败：${error?.message || error}`, 'error');
        }
    }

    dispose() {
        if (this.validationTimer !== null) clearTimeout(this.validationTimer);
        this.disposers.splice(0).forEach(dispose => dispose());
        this.overlay?.remove();
        this.overlay = null;
    }
}

export default FinalDecisionPanel;
