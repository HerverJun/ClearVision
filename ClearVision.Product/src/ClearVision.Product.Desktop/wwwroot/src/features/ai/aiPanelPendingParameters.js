import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import httpClient from '../../core/messaging/httpClient.js';
import { getOperatorTypeDisplayName, getParameterDisplayName } from '../../shared/operatorDisplayNames.js';
import { shouldIncludePendingParameter } from '../../shared/parameterDependencyRules.js';

export const aiPanelPendingParametersMixin = {
    _renderParameterDraftEditor(data, flow = null) {
        const container = this.container?.querySelector('#ai-result-parameter-editor');
        if (!container) return;

        const pending = this._resolvePendingParametersForDraft(data);
        const operators = this._getPendingOperatorSourceOperators(flow || data?.flow || data?.Flow || null);
        this._syncPendingParameterDrafts(data, flow);

        if (pending.length === 0) {
            container.classList.add('is-empty');
            container.innerHTML = '<div class="ai-followup-empty">当前没有待确认参数，暂无需补录。</div>';
            return;
        }

        const groups = this._collectPendingDraftGroups(pending, operators);
        if (groups.length === 0) {
            container.classList.add('is-empty');
            container.innerHTML = '<div class="ai-followup-empty">当前采集模式下没有需要补录的互斥参数。</div>';
            return;
        }

        const confirmationState = this._getPendingParameterConfirmationState(pending, operators, groups);
        const { totals } = confirmationState;
        const signature = this.pendingParameterDraftSignature;

        this._ensurePendingDraftMetadata(groups, signature);
        if (groups.some(group => group.fields.some(field => field.dataType === 'camerabinding'))) {
            this._ensureCameraBindings(signature);
        }

        container.classList.remove('is-empty');
        container.innerHTML = `
            <div class="ai-parameter-editor-summary">
                已填写 <span class="result-count">${totals.filled}</span> / <span class="result-count">${totals.total}</span> 项。
                <span class="ai-parameter-editor-summary-note">${confirmationState.isConfirmed ? '人工参数已确认，可直接应用到画布。' : '请先填写并确认人工参数；AI 复核不是应用到画布的必要步骤。'}</span>
            </div>
            <div class="ai-parameter-editor-policy">人工确认后的参数可直接应用到画布；AI 复核仅用于二次检查或继续优化，不是应用到画布的必要步骤。</div>
            <div class="ai-parameter-group-list">
                ${groups.map(group => `
                    <section class="ai-parameter-group" data-draft-group="${this._escapeHtml(group.groupKey)}">
                        <div class="ai-parameter-group-header">
                            <div>
                                <div class="ai-parameter-group-title">${this._escapeHtml(group.label)}</div>
                                <div class="ai-parameter-group-meta">
                                    ${group.operatorType ? `<span title="${this._escapeHtml(group.operatorType)}">${this._escapeHtml(getOperatorTypeDisplayName(group.operatorType))}</span>` : '未识别算子类型'}
                                    ${group.operator ? '' : ' · 当前画布快照中未找到精确算子，提交时将按名称提示 AI 继续审核'}
                                </div>
                            </div>
                            <button class="ai-parameter-group-jump" type="button" data-followup-nav="${this._escapeHtml(group.groupKey)}">定位</button>
                        </div>
                        <div class="ai-parameter-field-list">
                            ${group.fields.map(field => this._renderPendingDraftField(group, field, confirmationState)).join('')}
                        </div>
                    </section>
                `).join('')}
            </div>
            <div class="ai-parameter-editor-actions">
                <div class="ai-parameter-editor-actions-hint">${confirmationState.isConfirmed ? '参数已确认，可应用到画布继续编辑；提交 AI 复核会带上当前方案、人工参数、画布人工修改记录、仍缺资源/参数和输入框补充说明。' : '请先确认人工参数。应用到画布不要求 AI 复核通过；可选 AI 复核只用于二次检查。'}</div>
                <div class="ai-parameter-editor-action-row">
                    <button class="ai-parameter-confirm-btn" type="button" id="ai-btn-confirm-parameters">确认人工参数</button>
                    <button class="ai-parameter-review-btn" type="button" id="ai-btn-review-parameters">提交 AI 复核（可选）</button>
                </div>
            </div>
        `;

        container.querySelectorAll('[data-followup-nav]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', () => {
                this._scrollToPendingDraftGroup(button.dataset.followupNav || '');
            });
        });

        container.querySelectorAll('[data-draft-input="true"]').forEach(inputEl => {
            const updateDraft = () => {
                const operatorId = inputEl.dataset.draftOperatorId || '';
                const parameterName = inputEl.dataset.draftParameterName || '';
                const fieldType = inputEl.dataset.fieldType || '';
                const value = this._readPendingDraftInputValue(inputEl);
                this._setPendingDraftConfirmedValue(operatorId, parameterName, value, fieldType, 'user_input');
                this._updatePendingDraftSummary(data, flow);
                this._renderFollowupChecklist(data, flow);
            };

            inputEl.addEventListener('change', updateDraft);
            if (inputEl.tagName === 'INPUT') {
                inputEl.addEventListener('input', updateDraft);
            }
        });

        container.querySelectorAll('[data-draft-adopt="true"]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', () => {
                const operatorId = button.dataset.draftOperatorId || '';
                const parameterName = button.dataset.draftParameterName || '';
                const groupsForAdopt = this._collectPendingDraftGroups(pending, operators);
                const targetGroup = groupsForAdopt.find(group => group.operatorId === operatorId);
                const targetField = targetGroup?.fields.find(field =>
                    String(field.parameterName || '').trim().toLowerCase() === String(parameterName || '').trim().toLowerCase()
                );
                if (!targetField) return;
                this._setPendingDraftConfirmedValue(
                    operatorId,
                    parameterName,
                    targetField.suggestedValue,
                    targetField.dataType,
                    'user_input'
                );
                this._renderFollowupChecklist(data, flow);
                this._renderParameterDraftEditor(data, flow);
            });
        });

        container.querySelectorAll('[data-draft-file-pick]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', () => {
                this._pickPendingDraftFile(
                    button.dataset.draftOperatorId || '',
                    button.dataset.draftParameterName || ''
                );
            });
        });

        const confirmButton = container.querySelector('#ai-btn-confirm-parameters');
        if (confirmButton) {
            confirmButton.addEventListener('click', () => this._handleConfirmPendingParameters(data, flow));
        }

        const reviewButton = container.querySelector('#ai-btn-review-parameters');
        if (reviewButton) {
            reviewButton.addEventListener('click', this._handlePendingParameterReview);
        }
        this._updatePendingDraftSummary(data, flow);
    },

    _collectPendingDraftGroups(pending, operators) {
        return pending.map(item => {
            const context = this._resolvePendingOperatorContext(item.operatorId, operators);
            const metadata = this._getCachedOperatorMetadata(context.operatorType);
            const ruleOperator = context.operator || { operatorType: context.operatorType, parameters: {} };
            const fields = item.parameterNames
                .filter(parameterName => shouldIncludePendingParameter(ruleOperator, parameterName))
                .map(parameterName => {
                    const parameterMetadata = this._findMetadataParameter(metadata, parameterName);
                    const entry = this._getPendingDraftEntry(item.operatorId, parameterName);
                    return this._normalizePendingDraftField({
                        operatorId: item.operatorId,
                        parameterName,
                        entry,
                        metadata: parameterMetadata
                    });
                });

            return {
                operatorId: item.operatorId,
                operatorType: context.operatorType,
                operator: context.operator,
                label: context.label,
                groupKey: this._getPendingDraftGroupKey(item.operatorId),
                fields
            };
        }).filter(group => group.fields.length > 0);
    },

    _renderPendingDraftField(group, field, confirmationState = null) {
        const inputId = this._buildPendingDraftInputId(group.operatorId, field.parameterName);
        const labelText = getParameterDisplayName(field.parameterName, {
            fallback: field.displayName || field.parameterName
        });
        const label = this._escapeHtml(labelText);
        const description = field.description
            ? `<div class="ai-parameter-field-desc">${this._escapeHtml(field.description)}</div>`
            : '';
        const currentValue = field.confirmedValue;
        const currentValueText = currentValue === null || currentValue === undefined ? '' : String(currentValue);
        const hasSuggestedValue = this._hasPendingDraftValue(field.suggestedValue, field.dataType);
        const hasConfirmedValue = this._hasPendingDraftValue(field.confirmedValue, field.dataType);
        const isBatchConfirmed = Boolean(confirmationState?.isConfirmed && hasConfirmedValue);
        const showAdoptSuggestion = hasSuggestedValue && !this._arePendingDraftValuesEquivalent(field.confirmedValue, field.suggestedValue, field.dataType);
        const suggestionHtml = hasSuggestedValue
            ? `
                <div class="ai-parameter-field-suggestion">
                    <span class="ai-parameter-field-suggestion-label">建议值：${this._escapeHtml(this._formatPendingDraftValueForDisplay(field.suggestedValue, field))}</span>
                    ${showAdoptSuggestion ? `
                        <button
                            class="ai-parameter-suggestion-btn"
                            type="button"
                            data-draft-adopt="true"
                            data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                            data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                        >
                            采用建议值
                        </button>
                    ` : ''}
                </div>
            `
            : '';
        const sourceHint = field.source === 'canvas_override'
            ? '<div class="ai-parameter-field-desc">当前值已从画布同步。</div>'
            : '';
        const placeholderHint = field.isPendingPlaceholder
            ? '<div class="ai-parameter-field-placeholder">当前为待补占位符，需由工程师补齐后才能部署。</div>'
            : '';

        let controlHtml = '';
        if (field.dataType === 'boolean' || field.dataType === 'bool') {
            const normalizedBoolean = this.normalizeBooleanLike(currentValue);
            controlHtml = `
                <select
                    id="${this._escapeHtml(inputId)}"
                    class="ai-draft-input ai-draft-select"
                    data-draft-input="true"
                    data-field-type="boolean"
                    data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                    data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                >
                    <option value="" ${normalizedBoolean === null ? 'selected' : ''}>待确认</option>
                    <option value="true" ${normalizedBoolean === true ? 'selected' : ''}>是</option>
                    <option value="false" ${normalizedBoolean === false ? 'selected' : ''}>否</option>
                </select>
            `;
        } else if (field.dataType === 'enum' || field.dataType === 'select' || field.dataType === 'camerabinding') {
            const options = field.dataType === 'camerabinding'
                ? this._buildCameraBindingOptions(currentValue)
                : this._buildEnumOptions(field.options || [], currentValue);
            const extraHint = field.dataType === 'camerabinding' && this.cameraBindingsCache.length === 0
                ? '<div class="ai-parameter-field-desc">正在加载相机绑定列表...</div>'
                : '';
            controlHtml = `
                <select
                    id="${this._escapeHtml(inputId)}"
                    class="ai-draft-input ai-draft-select"
                    data-draft-input="true"
                    data-field-type="${this._escapeHtml(field.dataType)}"
                    data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                    data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                >
                    ${options}
                </select>
                ${extraHint}
            `;
        } else if (field.dataType === 'file') {
            controlHtml = `
                <div class="ai-draft-file-row">
                    <input
                        type="text"
                        id="${this._escapeHtml(inputId)}"
                        class="ai-draft-input ai-draft-text"
                        data-draft-input="true"
                        data-field-type="file"
                        data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                        data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                        value="${this._escapeHtml(currentValueText)}"
                        placeholder="请选择或输入文件路径"
                    />
                    <button
                        class="ai-draft-file-btn"
                        type="button"
                        data-draft-file-pick="true"
                        data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                        data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                    >
                        选择文件
                    </button>
                </div>
            `;
        } else if (['int', 'integer', 'double', 'float', 'number'].includes(field.dataType)) {
            const step = field.step ?? (['int', 'integer'].includes(field.dataType) ? 1 : 'any');
            const minAttr = field.min !== undefined && field.min !== null ? `min="${this._escapeHtml(String(field.min))}"` : '';
            const maxAttr = field.max !== undefined && field.max !== null ? `max="${this._escapeHtml(String(field.max))}"` : '';
            controlHtml = `
                <input
                    type="number"
                    id="${this._escapeHtml(inputId)}"
                    class="ai-draft-input ai-draft-number"
                    data-draft-input="true"
                    data-field-type="${this._escapeHtml(field.dataType)}"
                    data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                    data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                    value="${this._escapeHtml(currentValueText)}"
                    step="${this._escapeHtml(String(step))}"
                    ${minAttr}
                    ${maxAttr}
                    placeholder="请输入数值"
                />
            `;
        } else {
            controlHtml = `
                <input
                    type="text"
                    id="${this._escapeHtml(inputId)}"
                    class="ai-draft-input ai-draft-text"
                    data-draft-input="true"
                    data-field-type="${this._escapeHtml(field.dataType || 'text')}"
                    data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                    data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                    value="${this._escapeHtml(currentValueText)}"
                    placeholder="请输入参数值"
                />
            `;
        }

        return `
            <div class="ai-parameter-field ${field.isPendingPlaceholder ? 'is-placeholder' : ''}" data-pending-placeholder="${field.isPendingPlaceholder ? 'true' : 'false'}">
                <label class="ai-parameter-field-label" for="${this._escapeHtml(inputId)}">
                    ${label}
                    <span class="ai-parameter-field-key" title="${this._escapeHtml(field.parameterName)}">技术键</span>
                </label>
                ${controlHtml}
                ${suggestionHtml}
                ${isBatchConfirmed ? `<div class="ai-parameter-field-status">当前状态：已确认</div>` : '<div class="ai-parameter-field-status is-unconfirmed">当前状态：待确认</div>'}
                ${sourceHint}
                ${placeholderHint}
                ${description}
            </div>
        `;
    },

    _countPendingDraftProgress(groups) {
        let total = 0;
        let filled = 0;
        groups.forEach(group => {
            group.fields.forEach(field => {
                total += 1;
                if (this._hasPendingDraftValue(field.confirmedValue, field.dataType)) {
                    filled += 1;
                }
            });
        });
        return { total, filled };
    },

    _hasPendingParameterConfirmation() {
        return Boolean(this.pendingParameterConfirmedDraftSignature && this.pendingParameterConfirmedValueSignature);
    },

    _clearPendingParameterConfirmation() {
        this.pendingParameterConfirmedDraftSignature = '';
        this.pendingParameterConfirmedValueSignature = '';
    },

    _computePendingDraftValueSignature(pending, operators) {
        const safePending = this._normalizePendingParameters(pending);
        const safeOperators = Array.isArray(operators) ? operators : [];
        if (safePending.length === 0) return '';

        const parts = [];
        safePending.forEach(item => {
            const context = this._resolvePendingOperatorContext(item.operatorId, safeOperators);
            const metadata = this._getCachedOperatorMetadata(context.operatorType);
            const ruleOperator = context.operator || { operatorType: context.operatorType, parameters: {} };
            item.parameterNames.forEach(parameterName => {
                if (!shouldIncludePendingParameter(ruleOperator, parameterName)) {
                    return;
                }
                const fieldType = this._normalizePendingFieldType(this._findMetadataParameter(metadata, parameterName));
                const value = this._getPendingDraftConfirmedValue(item.operatorId, parameterName);
                const valueText = this._hasPendingDraftValue(value, fieldType)
                    ? this._stringifyPendingDraftValue(value, fieldType)
                    : '';
                parts.push(`${String(item.operatorId || '').trim().toLowerCase()}::${String(parameterName || '').trim().toLowerCase()}::${String(fieldType || '').trim().toLowerCase()}::${valueText}`);
            });
        });

        return parts.sort().join('|');
    },

    _getPendingParameterConfirmationState(pending, operators, groups = null) {
        const safePending = this._normalizePendingParameters(pending);
        const safeOperators = Array.isArray(operators) ? operators : [];
        const resolvedGroups = Array.isArray(groups) ? groups : this._collectPendingDraftGroups(safePending, safeOperators);
        const totals = this._countPendingDraftProgress(resolvedGroups);
        const valueSignature = this._computePendingDraftValueSignature(safePending, safeOperators);
        const isConfirmed = Boolean(
            totals.total > 0 &&
            totals.filled === totals.total &&
            this.pendingParameterConfirmedDraftSignature &&
            this.pendingParameterConfirmedValueSignature &&
            this.pendingParameterConfirmedDraftSignature === this.pendingParameterDraftSignature &&
            this.pendingParameterConfirmedValueSignature === valueSignature
        );
        const hasCurrentFlow = Boolean(this.currentResult?.flow);

        return {
            groups: resolvedGroups,
            totals,
            valueSignature,
            isConfirmed,
            canConfirm: hasCurrentFlow && totals.total > 0 && totals.filled === totals.total && !isConfirmed,
            canReview: hasCurrentFlow && isConfirmed
        };
    },

    _updatePendingDraftSummary(data = this.currentResult, flow = null) {
        const container = this.container?.querySelector('#ai-result-parameter-editor');
        if (!container || container.classList.contains('is-empty')) return;

        const pending = this._resolvePendingParametersForDraft(data);
        if (pending.length === 0) return;

        const operators = this._getPendingOperatorSourceOperators(flow || data?.flow || data?.Flow || null);
        const confirmationState = this._getPendingParameterConfirmationState(pending, operators);
        const { totals } = confirmationState;
        const summary = container.querySelector('.ai-parameter-editor-summary');
        if (summary) {
            summary.innerHTML = `
                已填写 <span class="result-count">${totals.filled}</span> / <span class="result-count">${totals.total}</span> 项。
                <span class="ai-parameter-editor-summary-note">${confirmationState.isConfirmed ? '人工参数已确认，可直接应用到画布。' : '请先填写并确认人工参数；AI 复核不是应用到画布的必要步骤。'}</span>
            `;
        }

        const hint = container.querySelector('.ai-parameter-editor-actions-hint');
        if (hint) {
            hint.textContent = confirmationState.isConfirmed
                ? '参数已确认，可应用到画布继续编辑；提交 AI 复核会带上当前方案、人工参数、画布人工修改记录、仍缺资源/参数和输入框补充说明。'
                : '请先确认人工参数。应用到画布不要求 AI 复核通过；可选 AI 复核只用于二次检查。';
        }

        container.querySelectorAll('.ai-parameter-field-status').forEach(statusEl => {
            statusEl.textContent = confirmationState.isConfirmed ? '当前状态：已确认' : '当前状态：待确认';
            statusEl.classList.toggle('is-unconfirmed', !confirmationState.isConfirmed);
        });

        const confirmButton = container.querySelector('#ai-btn-confirm-parameters');
        if (confirmButton) {
            confirmButton.disabled = this.isGenerating || !confirmationState.canConfirm;
        }

        const reviewButton = container.querySelector('#ai-btn-review-parameters');
        if (reviewButton) {
            reviewButton.disabled = this.isGenerating || !confirmationState.canReview;
        }
    },

    _syncPendingParameterDrafts(data, flow = null, options = {}) {
        const force = Boolean(options?.force);
        const pending = this._resolvePendingParametersForDraft(data);
        const operators = this._extractOperators(flow || data?.flow || data?.Flow || null);
        const signature = `${this.currentResultVersion || 0}::${this._computePendingDraftSignature(pending, operators)}`;
        const canvasOperators = this._isCurrentResultAppliedToCanvas()
            ? this._extractOperators(this.flowCanvas?.serialize?.() || null)
            : [];

        if (!force && signature === this.pendingParameterDraftSignature) {
            return;
        }

        if (pending.length === 0) {
            this.pendingParameterDrafts = {};
            this.pendingParameterDraftSignature = '';
            this.pendingOperatorBindings = {};
            this._clearPendingParameterConfirmation();
            return;
        }

        const nextDrafts = force ? this.pendingParameterDrafts : {};
        pending.forEach(item => {
            const context = this._resolvePendingOperatorContext(item.operatorId, operators);
            const metadata = this._getCachedOperatorMetadata(context.operatorType);
            const ruleOperator = context.operator || { operatorType: context.operatorType, parameters: {} };
            item.parameterNames.forEach(parameterName => {
                if (!shouldIncludePendingParameter(ruleOperator, parameterName)) {
                    return;
                }
                const parameterMetadata = this._findMetadataParameter(metadata, parameterName);
                const fieldType = this._normalizePendingFieldType(parameterMetadata);

                if (!nextDrafts[item.operatorId]) {
                    nextDrafts[item.operatorId] = {};
                }

                const entry = force
                    ? this._getPendingDraftEntry(item.operatorId, parameterName)
                    : this._createPendingDraftEntry();
                const suggestedValue = this._normalizePendingValueByType(
                    this._readOperatorParameterValue(context.operator, parameterName),
                    fieldType
                );
                const canvasValue = this._isCurrentResultAppliedToCanvas()
                    ? this._normalizePendingValueByType(
                        this._readOperatorParameterValue(
                            this._resolvePendingOperatorContext(item.operatorId, canvasOperators).operator,
                            parameterName
                        ),
                        fieldType
                    )
                    : null;

                let nextEntry = this._createPendingDraftEntry({
                    ...entry,
                    suggestedValue: this._hasPendingDraftValue(suggestedValue, fieldType) ? suggestedValue : null
                });

                if (!force) {
                    nextEntry.confirmedValue = null;
                    nextEntry.status = 'unconfirmed';
                    nextEntry.source = 'ai_suggestion';
                }

                if (this._isCurrentResultAppliedToCanvas() && this._hasPendingDraftValue(canvasValue, fieldType) && !this._arePendingDraftValuesEquivalent(canvasValue, nextEntry.suggestedValue, fieldType)) {
                    nextEntry = this._createPendingDraftEntry({
                        ...nextEntry,
                        confirmedValue: canvasValue,
                        status: 'confirmed',
                        source: 'canvas_override'
                    });
                } else if (force && nextEntry.source === 'canvas_override' && !this._hasPendingDraftValue(canvasValue, fieldType)) {
                    nextEntry = this._createPendingDraftEntry({
                        ...nextEntry,
                        confirmedValue: null,
                        status: 'unconfirmed',
                        source: this._hasPendingDraftValue(nextEntry.suggestedValue, fieldType) ? 'ai_suggestion' : 'user_input'
                    });
                }

                nextDrafts[item.operatorId][parameterName] = nextEntry;
            });
        });

        this.pendingParameterDrafts = nextDrafts;
        this.pendingParameterDraftSignature = signature;
        if (this._hasPendingParameterConfirmation()) {
            const confirmationState = this._getPendingParameterConfirmationState(pending, operators);
            if (!confirmationState.isConfirmed) {
                this._clearPendingParameterConfirmation();
            }
        }
    },

    _computePendingDraftSignature(pending, operators) {
        if (!Array.isArray(pending) || pending.length === 0) return '';
        const operatorPart = (Array.isArray(operators) ? operators : [])
            .map((operator, index) => {
                const operatorId = operator?.id ?? operator?.Id ?? operator?.tempId ?? operator?.TempId ?? `index-${index}`;
                const operatorType = operator?.type ?? operator?.Type ?? operator?.operatorType ?? operator?.OperatorType ?? '';
                return `${String(operatorId).trim()}:${String(operatorType).trim()}`;
            })
            .join('|');
        const pendingPart = pending
            .map(item => {
                const context = this._resolvePendingOperatorContext(item.operatorId, operators);
                const ruleOperator = context.operator || { operatorType: context.operatorType, parameters: {} };
                const names = item.parameterNames.filter(parameterName =>
                    shouldIncludePendingParameter(ruleOperator, parameterName)
                );
                return `${item.operatorId}:${names.join(',')}`;
            })
            .join('|');
        return `${this.sessionId || 'no-session'}::${operatorPart}::${pendingPart}`;
    },

    _getPendingDraftGroupKey(operatorId) {
        const normalizedId = String(operatorId || '').trim();
        const binding = this.pendingOperatorBindings[normalizedId] || null;
        return `pending-${binding?.actualOperatorId || normalizedId || 'unknown'}`;
    },

    _buildPendingOperatorBinding({ pendingOperatorId, actualOperatorId = '', label = '', operatorType = '' }) {
        const normalizedPendingId = String(pendingOperatorId || '').trim();
        return {
            pendingOperatorId: normalizedPendingId,
            actualOperatorId: String(actualOperatorId || '').trim(),
            label: String(label || '').trim(),
            operatorType: String(operatorType || '').trim()
        };
    },

    _findOperatorByAnyId(operators, operatorId) {
        const normalizedId = String(operatorId || '').trim();
        if (!normalizedId) return null;

        return (Array.isArray(operators) ? operators : []).find(op => {
            const candidates = [
                op?.tempId,
                op?.TempId,
                op?.id,
                op?.Id
            ].map(value => String(value || '').trim()).filter(Boolean);
            return candidates.includes(normalizedId);
        }) || null;
    },

    _findOperatorByTempSequence(operators, operatorId) {
        const normalizedId = String(operatorId || '').trim();
        const match = normalizedId.match(/^op[_-](\d+)$/i);
        if (!match) return null;

        const index = Number.parseInt(match[1], 10) - 1;
        if (!Number.isInteger(index) || index < 0) {
            return null;
        }

        const safeOperators = Array.isArray(operators) ? operators : [];
        return safeOperators[index] || null;
    },

    _buildPendingOperatorDisplayLabel(operator, fallbackId = '') {
        const normalizedFallbackId = String(fallbackId || '').trim();
        const directName = String(
            operator?.displayName ??
            operator?.DisplayName ??
            operator?.name ??
            operator?.Name ??
            ''
        ).trim();
        if (directName) {
            return normalizedFallbackId ? `${directName}（${normalizedFallbackId}）` : directName;
        }
        return normalizedFallbackId ? `算子 ${normalizedFallbackId}` : '未命名算子';
    },

    _rebuildPendingOperatorBindings({ pending, flow = null, sourceFlow = null, preferIndexFallback = false }) {
        const normalizedPending = this._normalizePendingParameters(pending);
        const actualOperators = this._extractOperators(flow || null);
        const sourceOperators = this._extractOperators(sourceFlow || flow || null);
        const nextBindings = {};

        normalizedPending.forEach((item) => {
            const normalizedPendingId = String(item.operatorId || '').trim();
            const normalizedActualId = String(item.actualOperatorId || '').trim();
            if (!normalizedPendingId) return;

            let sourceMatch = this._findOperatorByAnyId(sourceOperators, normalizedPendingId);
            let actualMatch = normalizedActualId
                ? this._findOperatorByAnyId(actualOperators, normalizedActualId)
                : this._findOperatorByAnyId(actualOperators, normalizedPendingId);

            if (!sourceMatch && preferIndexFallback) {
                sourceMatch = this._findOperatorByTempSequence(sourceOperators, normalizedPendingId);
            }

            if (!actualMatch && sourceMatch) {
                const sourceActualId = String(sourceMatch?.id ?? sourceMatch?.Id ?? '').trim();
                if (sourceActualId) {
                    actualMatch = this._findOperatorByAnyId(actualOperators, sourceActualId);
                }
            }

            if (!actualMatch && preferIndexFallback) {
                actualMatch = this._findOperatorByTempSequence(actualOperators, normalizedPendingId);
            }

            const operatorForLabel = sourceMatch || actualMatch || null;
            nextBindings[normalizedPendingId] = this._buildPendingOperatorBinding({
                pendingOperatorId: normalizedPendingId,
                actualOperatorId: actualMatch?.id ?? actualMatch?.Id ?? normalizedActualId,
                label: this._buildPendingOperatorDisplayLabel(operatorForLabel, normalizedPendingId),
                operatorType: operatorForLabel?.type ?? operatorForLabel?.Type ?? operatorForLabel?.operatorType ?? operatorForLabel?.OperatorType ?? ''
            });
        });

        this.pendingOperatorBindings = nextBindings;
        return nextBindings;
    },

    _getPendingOperatorSourceFlow(fallbackFlow = null) {
        if (this._isCurrentResultAppliedToCanvas() && this.flowCanvas?.serialize) {
            return this.flowCanvas.serialize();
        }
        return fallbackFlow;
    },

    _getPendingOperatorSourceOperators(fallbackFlow = null) {
        return this._extractOperators(this._getPendingOperatorSourceFlow(fallbackFlow));
    },

    _buildPendingDraftInputId(operatorId, parameterName) {
        const normalize = (value) => String(value || '').replace(/[^a-zA-Z0-9_-]/g, '_');
        return `ai-draft-${normalize(operatorId)}-${normalize(parameterName)}`;
    },

    _resolvePendingOperatorContext(operatorId, operators) {
        const normalizedId = String(operatorId || '').trim();
        const safeOperators = Array.isArray(operators) ? operators : [];
        const binding = this.pendingOperatorBindings[normalizedId] || this._buildPendingOperatorBinding({
            pendingOperatorId: normalizedId,
            label: normalizedId ? `算子 ${normalizedId}` : '未命名算子'
        });

        const operator = binding.actualOperatorId
            ? this._findOperatorByAnyId(safeOperators, binding.actualOperatorId)
            : this._findOperatorByAnyId(safeOperators, normalizedId);
        const operatorType = String(
            operator?.type ??
            operator?.Type ??
            operator?.operatorType ??
            operator?.OperatorType ??
            binding.operatorType ??
            ''
        ).trim();
        const label = operator
            ? this._buildPendingOperatorDisplayLabel(operator, normalizedId)
            : binding.label;

        return {
            operator,
            operatorType,
            label
        };
    },

    _getCachedOperatorMetadata(type) {
        const normalizedType = String(type || '').trim().toLowerCase();
        if (!normalizedType) return null;

        if (this.operatorMetadataCache.has(normalizedType)) {
            return this.operatorMetadataCache.get(normalizedType);
        }

        const libraryOperators = this.options.getOperators?.() || [];
        const matched = libraryOperators.find(operator =>
            String(operator?.type || '').trim().toLowerCase() === normalizedType
        ) || null;
        if (matched) {
            this.operatorMetadataCache.set(normalizedType, matched);
            return matched;
        }

        return null;
    },

    async _ensureOperatorMetadata(type) {
        const normalizedType = String(type || '').trim();
        const cacheKey = normalizedType.toLowerCase();
        if (!normalizedType) return null;

        const cached = this._getCachedOperatorMetadata(normalizedType);
        if (cached) return cached;

        if (this.operatorMetadataLoading.has(cacheKey)) {
            return this.operatorMetadataLoading.get(cacheKey);
        }

        const loadingPromise = httpClient
            .get(`/operators/${encodeURIComponent(normalizedType)}/metadata`)
            .then(metadata => {
                if (metadata && typeof metadata === 'object') {
                    this.operatorMetadataCache.set(cacheKey, metadata);
                    return metadata;
                }
                this.operatorMetadataCache.set(cacheKey, null);
                return null;
            })
            .catch(error => {
                console.warn('[AiPanel] 获取算子元数据失败:', normalizedType, error);
                this.operatorMetadataCache.set(cacheKey, null);
                return null;
            })
            .finally(() => {
                this.operatorMetadataLoading.delete(cacheKey);
            });

        this.operatorMetadataLoading.set(cacheKey, loadingPromise);
        return loadingPromise;
    },

    _ensurePendingDraftMetadata(groups, signature) {
        const missingTypes = [...new Set(groups
            .map(group => group.operatorType)
            .filter(type => {
                const normalizedType = String(type || '').trim().toLowerCase();
                if (!normalizedType) return false;
                const cached = this._getCachedOperatorMetadata(type);
                return cached === null && !this.operatorMetadataCache.has(normalizedType);
            }))];

        missingTypes.forEach(type => {
            this._ensureOperatorMetadata(type).then(() => {
                if (this.pendingParameterDraftSignature !== signature || !this.currentResult?.flow) {
                    return;
                }
                this._renderParameterDraftEditor(this.currentResult, this.currentResult.flow);
            });
        });
    },

    async _ensureCameraBindings(signature) {
        if (this.cameraBindingsCache.length > 0) {
            return this.cameraBindingsCache;
        }

        if (this.cameraBindingsLoadingPromise) {
            return this.cameraBindingsLoadingPromise;
        }

        this.cameraBindingsLoadingPromise = httpClient
            .get('/cameras/bindings')
            .then(result => {
                this.cameraBindingsCache = Array.isArray(result) ? result : [];
                if (this.pendingParameterDraftSignature === signature && this.currentResult?.flow) {
                    this._renderParameterDraftEditor(this.currentResult, this.currentResult.flow);
                }
                return this.cameraBindingsCache;
            })
            .catch(error => {
                console.warn('[AiPanel] 获取相机绑定失败。', error);
                return [];
            })
            .finally(() => {
                this.cameraBindingsLoadingPromise = null;
            });

        return this.cameraBindingsLoadingPromise;
    },

    normalizeBooleanLike(value) {
        if (value === null || value === undefined) return null;
        if (typeof value === 'boolean') return value;
        if (typeof value === 'number') {
            if (value === 1) return true;
            if (value === 0) return false;
            return null;
        }

        const normalized = String(value).trim().toLowerCase();
        if (!normalized) return null;
        if (['true', '1', 'yes', 'y', 'on'].includes(normalized)) return true;
        if (['false', '0', 'no', 'n', 'off'].includes(normalized)) return false;
        return null;
    },

    _isPendingBooleanField(fieldType = '') {
        return ['boolean', 'bool'].includes(String(fieldType || '').trim().toLowerCase());
    },

    _isPendingNumericField(fieldType = '') {
        return ['int', 'integer', 'double', 'float', 'number'].includes(String(fieldType || '').trim().toLowerCase());
    },

    _parseNumericLike(value) {
        if (typeof value === 'number') {
            return Number.isFinite(value) ? value : null;
        }
        const normalized = String(value ?? '').trim();
        if (!normalized) return null;
        const parsed = Number(normalized);
        return Number.isFinite(parsed) ? parsed : null;
    },

    _normalizePendingFieldType(metadata) {
        const rawType = String(
            metadata?.dataType ??
            metadata?.DataType ??
            metadata?.type ??
            metadata?.Type ??
            'text'
        ).trim().toLowerCase();

        return ['string', 'text'].includes(rawType) ? 'text' : rawType;
    },

    _normalizePendingValueByType(value, fieldType = '') {
        if (value === undefined) return null;
        const normalizedFieldType = String(fieldType || '').trim().toLowerCase();

        if (this._isPendingBooleanField(normalizedFieldType)) {
            return this.normalizeBooleanLike(value);
        }

        if (this._isPendingNumericField(normalizedFieldType)) {
            return this._parseNumericLike(value);
        }

        if (value === null) return null;
        const normalized = String(value).trim();
        return normalized.length > 0 ? normalized : null;
    },

    _arePendingDraftValuesEquivalent(left, right, fieldType = '') {
        const normalizedFieldType = String(fieldType || '').trim().toLowerCase();
        if (this._isPendingBooleanField(normalizedFieldType)) {
            return this.normalizeBooleanLike(left) === this.normalizeBooleanLike(right);
        }

        if (this._isPendingNumericField(normalizedFieldType)) {
            return this._parseNumericLike(left) === this._parseNumericLike(right);
        }

        const leftValue = left === null || left === undefined ? '' : String(left).trim();
        const rightValue = right === null || right === undefined ? '' : String(right).trim();
        return leftValue === rightValue;
    },

    _hasPendingDraftValue(value, fieldType = '') {
        if (value === null || value === undefined) return false;

        if (this._isPendingBooleanField(fieldType)) {
            return this.normalizeBooleanLike(value) !== null;
        }

        if (this._isPendingNumericField(fieldType)) {
            return this._parseNumericLike(value) !== null;
        }

        if (typeof value === 'boolean') return true;
        if (typeof value === 'number') return Number.isFinite(value);
        return String(value).trim().length > 0;
    },

    _createPendingDraftEntry(overrides = {}) {
        return {
            confirmedValue: null,
            suggestedValue: null,
            status: 'unconfirmed',
            source: 'ai_suggestion',
            ...overrides
        };
    },

    _getPendingDraftEntry(operatorId, parameterName) {
        const operatorDrafts = this.pendingParameterDrafts[String(operatorId || '').trim()] || {};
        const normalizedName = String(parameterName || '').trim().toLowerCase();
        const matchedKey = Object.keys(operatorDrafts).find(key => key.toLowerCase() === normalizedName);
        const rawEntry = matchedKey ? operatorDrafts[matchedKey] : null;

        if (!rawEntry || typeof rawEntry !== 'object' || Array.isArray(rawEntry)) {
            return this._createPendingDraftEntry();
        }

        return this._createPendingDraftEntry(rawEntry);
    },

    _getPendingDraftConfirmedValue(operatorId, parameterName) {
        return this._getPendingDraftEntry(operatorId, parameterName).confirmedValue;
    },

    _getPendingDraftSuggestedValue(operatorId, parameterName) {
        return this._getPendingDraftEntry(operatorId, parameterName).suggestedValue;
    },

    _setPendingDraftConfirmedValue(operatorId, parameterName, value, fieldType = '', source = 'user_input') {
        const operatorKey = String(operatorId || '').trim();
        const parameterKey = this._resolvePendingDraftParameterKey(operatorKey, parameterName);
        if (!operatorKey || !parameterKey) return;

        const nextValue = this._normalizePendingValueByType(value, fieldType);
        const entry = this._getPendingDraftEntry(operatorKey, parameterKey);
        const previousValue = entry.confirmedValue;
        const hasValue = this._hasPendingDraftValue(nextValue, fieldType);

        if (!this.pendingParameterDrafts[operatorKey]) {
            this.pendingParameterDrafts[operatorKey] = {};
        }

        this.pendingParameterDrafts[operatorKey][parameterKey] = this._createPendingDraftEntry({
            ...entry,
            confirmedValue: hasValue ? nextValue : null,
            status: hasValue ? 'confirmed' : 'unconfirmed',
            source: hasValue ? source : (this._hasPendingDraftValue(entry.suggestedValue, fieldType) ? 'ai_suggestion' : source)
        });

        if (this._hasPendingParameterConfirmation() && !this._arePendingDraftValuesEquivalent(previousValue, hasValue ? nextValue : null, fieldType)) {
            this._clearPendingParameterConfirmation();
        }
    },

    _setPendingDraftSuggestedValue(operatorId, parameterName, value, fieldType = '') {
        const operatorKey = String(operatorId || '').trim();
        const parameterKey = this._resolvePendingDraftParameterKey(operatorKey, parameterName);
        if (!operatorKey || !parameterKey) return;

        if (!this.pendingParameterDrafts[operatorKey]) {
            this.pendingParameterDrafts[operatorKey] = {};
        }

        const entry = this._getPendingDraftEntry(operatorKey, parameterKey);
        const nextSuggestedValue = this._normalizePendingValueByType(value, fieldType);
        this.pendingParameterDrafts[operatorKey][parameterKey] = this._createPendingDraftEntry({
            ...entry,
            suggestedValue: this._hasPendingDraftValue(nextSuggestedValue, fieldType) ? nextSuggestedValue : null,
            source: entry.status === 'confirmed' ? entry.source : 'ai_suggestion'
        });
    },

    _formatPendingDraftValueForDisplay(value, field) {
        if (!this._hasPendingDraftValue(value, field?.dataType)) {
            return '待确认';
        }

        if (this._isPendingBooleanField(field?.dataType)) {
            return this.normalizeBooleanLike(value) ? '是' : '否';
        }

        if ((field?.dataType === 'enum' || field?.dataType === 'select' || field?.dataType === 'camerabinding') && Array.isArray(field?.options)) {
            const matched = field.options.find(option =>
                String(option?.value ?? option?.Value ?? option ?? '').trim() === String(value ?? '').trim()
            );
            if (matched) {
                return String(matched?.label ?? matched?.Label ?? matched?.value ?? matched?.Value ?? value);
            }
        }

        return String(value ?? '');
    },

    _findMetadataParameter(metadata, parameterName) {
        const parameters = metadata?.parameters || metadata?.Parameters || [];
        return parameters.find(item =>
            String(item?.name ?? item?.Name ?? '').trim().toLowerCase() === String(parameterName || '').trim().toLowerCase()
        ) || null;
    },

    _normalizePendingDraftField({ operatorId, parameterName, entry, metadata }) {
        const options = Array.isArray(metadata?.options ?? metadata?.Options)
            ? (metadata?.options ?? metadata?.Options)
            : [];
        const dataType = this._normalizePendingFieldType(metadata);
        const confirmedValue = entry?.confirmedValue ?? null;
        const suggestedValue = entry?.suggestedValue ?? null;

        return {
            operatorId,
            parameterName,
            displayName: String(metadata?.displayName ?? metadata?.DisplayName ?? parameterName).trim() || parameterName,
            description: String(metadata?.description ?? metadata?.Description ?? '').trim(),
            dataType,
            min: metadata?.min ?? metadata?.Min ?? metadata?.minValue ?? metadata?.MinValue,
            max: metadata?.max ?? metadata?.Max ?? metadata?.maxValue ?? metadata?.MaxValue,
            step: metadata?.step ?? metadata?.Step,
            options,
            defaultValue: metadata?.defaultValue ?? metadata?.DefaultValue ?? null,
            confirmedValue,
            suggestedValue,
            status: entry?.status ?? 'unconfirmed',
            source: entry?.source ?? 'ai_suggestion',
            isPendingPlaceholder: this._isPendingPlaceholderValue(confirmedValue) || this._isPendingPlaceholderValue(suggestedValue)
        };
    },

    _isPendingPlaceholderValue(value) {
        const normalized = String(value ?? '').trim().toLowerCase();
        return normalized === '<pending-camera-binding>' ||
            normalized === '<pending-model-path>' ||
            normalized === '<pending-model-resource>' ||
            normalized === '<pending-template-path>' ||
            normalized === '<pending-template-artifact>' ||
            normalized === '<pending-output-channel>' ||
            normalized === '<pending-pixel-to-world-scale>' ||
            normalized === '<pending-wire-sequence-labels>';
    },

    _buildEnumOptions(options, currentValue) {
        const normalizedCurrent = currentValue == null ? '' : String(currentValue);
        const normalizedOptions = Array.isArray(options) ? options : [];
        const optionRows = normalizedOptions.map(option => {
            const value = option?.value ?? option?.Value ?? option;
            const label = option?.label ?? option?.Label ?? value;
            const selected = String(value ?? '') === normalizedCurrent ? 'selected' : '';
            return `<option value="${this._escapeHtml(String(value ?? ''))}" ${selected}>${this._escapeHtml(String(label ?? ''))}</option>`;
        });

        return [`<option value="">请选择</option>`, ...optionRows].join('');
    },

    _buildCameraBindingOptions(currentValue) {
        const normalizedCurrent = currentValue == null ? '' : String(currentValue);
        const optionRows = this.cameraBindingsCache.map(binding => {
            const value = String(binding?.id ?? '').trim();
            const label = `${binding?.displayName || value}${binding?.serialNumber ? ` (${binding.serialNumber})` : ''}`;
            const selected = value === normalizedCurrent ? 'selected' : '';
            return `<option value="${this._escapeHtml(value)}" ${selected}>${this._escapeHtml(label)}</option>`;
        });

        if (!optionRows.some(option => option.includes('selected')) && normalizedCurrent) {
            optionRows.unshift(`<option value="${this._escapeHtml(normalizedCurrent)}" selected>${this._escapeHtml(normalizedCurrent)}</option>`);
        }

        return [`<option value="">请选择相机绑定</option>`, ...optionRows].join('');
    },

    _readPendingDraftInputValue(inputEl) {
        if (!inputEl) return null;

        const fieldType = String(inputEl.dataset.fieldType || '').trim().toLowerCase();
        const rawValue = inputEl.value;
        return this._normalizePendingValueByType(rawValue, fieldType);
    },

    _resolvePendingDraftParameterKey(operatorId, parameterName) {
        const operatorDrafts = this.pendingParameterDrafts[String(operatorId || '').trim()] || {};
        const normalizedName = String(parameterName || '').trim().toLowerCase();
        const existingKey = Object.keys(operatorDrafts).find(key => key.toLowerCase() === normalizedName);
        return existingKey || String(parameterName || '').trim();
    },

    _getPendingDraftValue(operatorId, parameterName) {
        return this._getPendingDraftConfirmedValue(operatorId, parameterName);
    },

    _readOperatorParameterValue(operator, parameterName) {
        if (!operator || !parameterName) return '';

        const normalizedName = String(parameterName).trim().toLowerCase();
        const parameters = operator?.parameters ?? operator?.Parameters ?? null;

        if (Array.isArray(parameters)) {
            const matched = parameters.find(item =>
                String(item?.name ?? item?.Name ?? '').trim().toLowerCase() === normalizedName
            );
            if (!matched) return '';
            return matched?.value ?? matched?.Value ?? '';
        }

        if (parameters && typeof parameters === 'object') {
            const matchedKey = Object.keys(parameters).find(key => key.toLowerCase() === normalizedName);
            return matchedKey ? parameters[matchedKey] : '';
        }

        return '';
    },

    _captureAppliedCanvasBaseline(flow) {
        if (!flow || typeof flow !== 'object') {
            this.appliedCanvasBaselineFlow = null;
            this.canvasManualEditRecords = [];
            this.canvasManualEditSignature = '';
            return;
        }

        this.appliedCanvasBaselineFlow = this._cloneJsonSafe(flow);
        this.canvasManualEditRecords = [];
        this.canvasManualEditSignature = '';
        this._attachCanvasManualEditRecordsToCurrentResult();
    },

    _cloneJsonSafe(value) {
        if (value === null || value === undefined) return value;
        try {
            return typeof structuredClone === 'function'
                ? structuredClone(value)
                : JSON.parse(JSON.stringify(value));
        } catch {
            return JSON.parse(JSON.stringify(value));
        }
    },

    _syncCanvasManualEditRecords(currentFlow = null) {
        if (!this._isCurrentResultAppliedToCanvas?.() || !this.appliedCanvasBaselineFlow || !currentFlow) {
            return [];
        }

        const previousByKey = new Map((this.canvasManualEditRecords || []).map(record => [
            String(record.changeKey || '').trim(),
            record
        ]));
        const nextRecords = this._collectCanvasManualParameterChanges(this.appliedCanvasBaselineFlow, currentFlow)
            .map(change => {
                const changeKey = [
                    change.operatorId,
                    change.parameterName,
                    change.oldValueSummary,
                    change.newValueSummary
                ].join('::');
                const existing = previousByKey.get(changeKey);
                return {
                    ...change,
                    changeKey,
                    changedAtUtc: existing?.changedAtUtc || new Date().toISOString(),
                    actor: 'local-user',
                    source: 'canvas_manual_edit',
                    sourceLabel: '流程页算子属性面板',
                    metadataOnly: true
                };
            });

        const signature = nextRecords
            .map(record => record.changeKey)
            .sort()
            .join('|');
        if (signature === this.canvasManualEditSignature) {
            return this.canvasManualEditRecords || [];
        }

        this.canvasManualEditSignature = signature;
        this.canvasManualEditRecords = nextRecords;
        this._attachCanvasManualEditRecordsToCurrentResult();
        return nextRecords;
    },

    _attachCanvasManualEditRecordsToCurrentResult() {
        if (!this.currentResult || typeof this.currentResult !== 'object') return;
        const records = this.canvasManualEditRecords || [];
        this.currentResult.canvasManualEditRecords = records;
        this.currentResult.CanvasManualEditRecords = records;
    },

    _collectCanvasManualParameterChanges(baselineFlow, currentFlow) {
        const baselineOperators = this._extractOperators(baselineFlow);
        const currentOperators = this._extractOperators(currentFlow);
        const baselineMap = this._buildCanvasOperatorCompareMap(baselineOperators);
        const changes = [];

        currentOperators.forEach((operator, index) => {
            const match = this._findCanvasCompareOperator(baselineMap, operator, index);
            if (!match) return;

            const operatorId = this._getCanvasOperatorId(operator, index);
            const operatorType = this._getCanvasOperatorType(operator);
            const displayName = this._getCanvasOperatorDisplayName(operator, operatorId);
            const baselineParams = this._flattenCanvasOperatorParameters(match.operator);
            const currentParams = this._flattenCanvasOperatorParameters(operator);
            const names = new Set([...baselineParams.keys(), ...currentParams.keys()]);

            names.forEach(parameterName => {
                const oldValue = baselineParams.get(parameterName);
                const newValue = currentParams.get(parameterName);
                if (this._areCanvasAuditValuesEquivalent(oldValue, newValue)) {
                    return;
                }

                const isPendingParameter = this._isCanvasManualEditPendingParameter(operatorId, parameterName);
                const affectsDeploymentReady = this._isCanvasManualEditDeploymentBlocking(operatorId, parameterName);
                changes.push({
                    operatorId: this._sanitizeCanvasAuditText(operatorId),
                    displayName: this._sanitizeCanvasAuditText(displayName),
                    operatorType: this._sanitizeCanvasAuditText(operatorType),
                    parameterName: this._sanitizeCanvasAuditText(parameterName),
                    oldValueSummary: this._summarizeCanvasManualEditValue(oldValue),
                    newValueSummary: this._summarizeCanvasManualEditValue(newValue),
                    isPendingParameter,
                    affectsApplyGate: isPendingParameter || affectsDeploymentReady,
                    affectsDeploymentReady
                });
            });
        });

        return changes;
    },

    _buildCanvasOperatorCompareMap(operators) {
        const byId = new Map();
        const byIndex = new Map();
        (Array.isArray(operators) ? operators : []).forEach((operator, index) => {
            const id = this._getCanvasOperatorId(operator, index);
            if (id) byId.set(id, { operator, index });
            byIndex.set(String(index), { operator, index });
        });
        return { byId, byIndex };
    },

    _findCanvasCompareOperator(map, operator, index) {
        const id = this._getCanvasOperatorId(operator, index);
        return (id && map.byId.get(id)) || map.byIndex.get(String(index)) || null;
    },

    _getCanvasOperatorId(operator, index = 0) {
        return String(operator?.id ?? operator?.Id ?? operator?.tempId ?? operator?.TempId ?? `index-${index}`).trim();
    },

    _getCanvasOperatorType(operator) {
        return String(operator?.type ?? operator?.Type ?? operator?.operatorType ?? operator?.OperatorType ?? '').trim();
    },

    _getCanvasOperatorDisplayName(operator, fallback = '') {
        return String(
            operator?.displayName ??
            operator?.DisplayName ??
            operator?.name ??
            operator?.Name ??
            fallback
        ).trim();
    },

    _flattenCanvasOperatorParameters(operator) {
        const result = new Map();
        const parameters = operator?.parameters ?? operator?.Parameters ?? null;
        if (Array.isArray(parameters)) {
            parameters.forEach(item => {
                const name = String(item?.name ?? item?.Name ?? '').trim();
                if (!name) return;
                result.set(name, item?.value ?? item?.Value ?? '');
            });
            return result;
        }

        if (parameters && typeof parameters === 'object') {
            Object.keys(parameters).forEach(name => {
                result.set(name, parameters[name]);
            });
        }

        return result;
    },

    _areCanvasAuditValuesEquivalent(left, right) {
        return JSON.stringify(left ?? '') === JSON.stringify(right ?? '');
    },

    _isCanvasManualEditPendingParameter(operatorId, parameterName) {
        const pending = this._resolvePendingParametersForDraft(this.currentResult);
        const normalizedOperator = String(operatorId || '').trim().toLowerCase();
        const normalizedParameter = String(parameterName || '').trim().toLowerCase();
        return pending.some(item => {
            const operatorCandidates = [
                item.operatorId,
                item.actualOperatorId,
                this.pendingOperatorBindings?.[item.operatorId]?.actualOperatorId
            ].map(value => String(value || '').trim().toLowerCase()).filter(Boolean);
            const parameterNames = (item.parameterNames || []).map(name => String(name || '').trim().toLowerCase());
            return operatorCandidates.includes(normalizedOperator) && parameterNames.includes(normalizedParameter);
        });
    },

    _isCanvasManualEditDeploymentBlocking(operatorId, parameterName) {
        const normalizedRef = `${String(operatorId || '').trim().toLowerCase()}.${String(parameterName || '').trim().toLowerCase()}`;
        const gate = this._getPayloadApplyGate?.(this.currentResult) || this.currentResult?.applyGate || this.currentResult?.ApplyGate || null;
        const blockers = this._toArray?.(gate?.deploymentBlockers || gate?.DeploymentBlockers) || [];
        if (blockers.some(blocker => String(blocker || '').trim().toLowerCase() === normalizedRef)) {
            return true;
        }

        const missing = this._normalizeMissingResources(this.currentResult?.missingResources ?? this.currentResult?.MissingResources);
        return missing.some(item => {
            const resourceRef = String(item.resourceKey || '').trim().toLowerCase();
            const itemRef = `${String(item.operatorId || item.actualOperatorId || '').trim().toLowerCase()}.${String(item.parameterName || '').trim().toLowerCase()}`;
            return resourceRef === normalizedRef || itemRef === normalizedRef;
        });
    },

    _sanitizeCanvasAuditText(value) {
        const raw = String(value ?? '').trim();
        const text = this._sanitizeAssistantFailureText?.(raw, 1200) || raw;
        return this._redactPublicDiagnosticText?.(text) || text;
    },

    _sanitizeCanvasAuditDisplayText(value, maxChars = 220) {
        const text = this._sanitizeCanvasAuditText(value);
        return text ? text.slice(0, maxChars) : '';
    },

    _normalizeCanvasManualEditRecordForDisplay(record = {}) {
        const safe = (value, maxChars = 220) => this._sanitizeCanvasAuditDisplayText(value, maxChars);
        const truthy = (...values) => values.some(value => value === true || String(value ?? '').trim().toLowerCase() === 'true');
        const falsy = (...values) => values.some(value => value === false || String(value ?? '').trim().toLowerCase() === 'false');
        return {
            changeKey: safe(record.changeKey || record.ChangeKey || '', 220),
            operatorId: safe(record.operatorId || record.OperatorId || '', 120),
            displayName: safe(record.displayName || record.DisplayName || record.operatorId || record.OperatorId || '', 160),
            operatorType: safe(record.operatorType || record.OperatorType || '', 120),
            parameterName: safe(record.parameterName || record.ParameterName || '', 120),
            oldValueSummary: safe(record.oldValueSummary || record.OldValueSummary || '', 160),
            newValueSummary: safe(record.newValueSummary || record.NewValueSummary || '', 160),
            changedAtUtc: safe(record.changedAtUtc || record.ChangedAtUtc || '', 80),
            actor: safe(record.actor || record.Actor || 'local-user', 100) || 'local-user',
            source: safe(record.source || record.Source || 'canvas_manual_edit', 80) || 'canvas_manual_edit',
            sourceLabel: safe(record.sourceLabel || record.SourceLabel || '', 160),
            isPendingParameter: truthy(record.isPendingParameter, record.IsPendingParameter),
            affectsApplyGate: truthy(record.affectsApplyGate, record.AffectsApplyGate),
            affectsDeploymentReady: truthy(record.affectsDeploymentReady, record.AffectsDeploymentReady),
            metadataOnly: !falsy(record.metadataOnly, record.MetadataOnly)
        };
    },

    _summarizeCanvasManualEditValue(value) {
        const raw = typeof value === 'object' && value !== null
            ? JSON.stringify(value)
            : String(value ?? '');
        const safe = this._sanitizeCanvasAuditText(raw);
        return safe.length > 96 ? `${safe.slice(0, 96)}...` : safe;
    },

    _getCanvasManualEditRecords(target = this.currentResult) {
        const explicit = this._toArray?.(target?.canvasManualEditRecords ?? target?.CanvasManualEditRecords) || [];
        const records = explicit.length ? explicit : (this.canvasManualEditRecords || []);
        return records.map(record => this._normalizeCanvasManualEditRecordForDisplay(record));
    },

    _renderCanvasManualEditRecords(target = this.currentResult) {
        const records = this._getCanvasManualEditRecords(target);
        if (!records.length) return '';

        const rows = records.slice().reverse().map(record => `
            <div class="ai-manual-confirmation-record ai-canvas-manual-edit-record">
                <div>
                    <strong>${this._escapeHtml(record.displayName || record.operatorId || '未命名算子')}</strong>
                    <span>${this._escapeHtml(record.changedAtUtc || '')} / ${this._escapeHtml(record.actor || 'local-user')}</span>
                </div>
                <p>${this._escapeHtml(record.operatorType || '未知算子')} · ${this._escapeHtml(getParameterDisplayName(record.parameterName, { fallback: record.parameterName || '参数' }))} · ${this._escapeHtml(record.oldValueSummary || '--')} -> ${this._escapeHtml(record.newValueSummary || '--')}</p>
                <small>来源：${this._escapeHtml(record.sourceLabel || '流程页算子属性面板')}；pendingParameter=${record.isPendingParameter ? 'true' : 'false'}；影响 ApplyGate=${record.affectsApplyGate ? 'true' : 'false'}；影响 DeploymentReady=${record.affectsDeploymentReady ? 'true' : 'false'}；metadataOnly=true</small>
            </div>
        `).join('');

        return `
            <details class="ai-manual-confirmation-panel ai-canvas-manual-edit-panel">
                <summary>画布人工修改记录（${this._escapeHtml(String(records.length))}）</summary>
                <div class="ai-manual-confirmation-list">${rows}</div>
            </details>
        `;
    },

    _buildFlowWithPendingDrafts(flow) {
        if (!flow || typeof flow !== 'object') return flow;

        const clonedFlow = typeof structuredClone === 'function'
            ? structuredClone(flow)
            : JSON.parse(JSON.stringify(flow));
        const operators = this._extractOperators(clonedFlow);
        const pending = this._resolvePendingParametersForDraft(this.currentResult);
        const confirmationState = this._getPendingParameterConfirmationState(pending, operators);

        pending.forEach(item => {
            const context = this._resolvePendingOperatorContext(item.operatorId, operators);
            if (!context.operator) return;

            item.parameterNames.forEach(parameterName => {
                if (!shouldIncludePendingParameter(context.operator, parameterName)) {
                    return;
                }
                if (this._isResourceOnlyDraftParameter?.(context.operatorType, parameterName)) {
                    return;
                }
                const confirmedValue = this._getPendingDraftConfirmedValue(item.operatorId, parameterName);
                const entry = this._getPendingDraftEntry(item.operatorId, parameterName);
                const canWriteValue = confirmationState.isConfirmed || entry.source === 'resource_binding';
                if (!canWriteValue) return;
                const fieldType = this._normalizePendingFieldType(
                    this._findMetadataParameter(
                        this._getCachedOperatorMetadata(context.operatorType),
                        parameterName
                    )
                );
                if (!this._hasPendingDraftValue(confirmedValue, fieldType)) return;
                this._writeOperatorParameterValue(
                    context.operator,
                    parameterName,
                    confirmedValue
                );
            });
        });

        return clonedFlow;
    },

    _writeOperatorParameterValue(operator, parameterName, value) {
        if (!operator || !parameterName) return;

        if (Array.isArray(operator.parameters)) {
            const matched = operator.parameters.find(item =>
                String(item?.name ?? item?.Name ?? '').trim().toLowerCase() === String(parameterName).trim().toLowerCase()
            );
            if (matched) {
                if ('value' in matched || !('Value' in matched)) {
                    matched.value = value;
                } else {
                    matched.Value = value;
                }
                return;
            }

            operator.parameters.push({
                name: parameterName,
                value
            });
            return;
        }

        if (Array.isArray(operator.Parameters)) {
            const matched = operator.Parameters.find(item =>
                String(item?.name ?? item?.Name ?? '').trim().toLowerCase() === String(parameterName).trim().toLowerCase()
            );
            if (matched) {
                if ('Value' in matched || !('value' in matched)) {
                    matched.Value = value;
                } else {
                    matched.value = value;
                }
                return;
            }

            operator.Parameters.push({
                Name: parameterName,
                Value: value
            });
            return;
        }

        if (operator.parameters && typeof operator.parameters === 'object') {
            const matchedKey = Object.keys(operator.parameters).find(key =>
                key.toLowerCase() === String(parameterName).trim().toLowerCase()
            );
            operator.parameters[matchedKey || parameterName] = value;
            return;
        }

        if (operator.Parameters && typeof operator.Parameters === 'object') {
            const matchedKey = Object.keys(operator.Parameters).find(key =>
                key.toLowerCase() === String(parameterName).trim().toLowerCase()
            );
            operator.Parameters[matchedKey || parameterName] = value;
            return;
        }

        operator.parameters = [{ name: parameterName, value }];
    },

    _scrollToPendingDraftGroup(groupKey) {
        const scrollContainer = this.container?.querySelector('#ai-results-scroll');
        if (!scrollContainer || !groupKey) return;

        const group = Array.from(scrollContainer.querySelectorAll('[data-draft-group]'))
            .find(element => element.dataset.draftGroup === groupKey);
        if (!group) return;

        scrollContainer.scrollTo({
            top: Math.max(0, group.offsetTop - 12),
            behavior: 'smooth'
        });

        const firstInput = group.querySelector('[data-draft-input="true"]');
        if (firstInput && typeof firstInput.focus === 'function') {
            setTimeout(() => firstInput.focus(), 180);
        }

        this._highlightPendingDraftGroup(group);
    },

    _highlightPendingDraftGroup(group) {
        if (!group) return;
        group.classList.add('is-highlighted');
        if (this.pendingParameterHighlightTimer) {
            clearTimeout(this.pendingParameterHighlightTimer);
        }
        this.pendingParameterHighlightTimer = setTimeout(() => {
            group.classList.remove('is-highlighted');
            this.pendingParameterHighlightTimer = null;
        }, 1800);
    },

    _pickPendingDraftFile(operatorId, parameterName) {
        if (this.isGenerating) return;
        this.pendingParameterFilePickContext = {
            operatorId: String(operatorId || '').trim(),
            parameterName: String(parameterName || '').trim()
        };
        webMessageBridge.sendMessage('PickFileCommand', {
            parameterName: 'aiPendingParameterFile',
            filter: 'All Files|*.*'
        });
    },

    _buildPendingParameterReviewRequest() {
        const flow = this._getCurrentFlowJson();
        const pending = this._resolvePendingParametersForDraft(this.currentResult);
        const operators = this._getPendingOperatorSourceOperators(flow || null);
        const groups = this._collectPendingDraftGroups(pending, operators);
        const input = this.container?.querySelector('#ai-input');
        const extraNote = String(input?.value || '').trim();
        const queuedHint = String(this.nextHintDraft || '').trim();

        if (!flow || groups.length === 0) {
            return null;
        }

        const lines = [
            '请严格基于当前 existingFlowJson 审核这套方案。',
            '要求：保持流程结构稳定，仅调整参数和必要补充信息；不要无关重建。'
        ];

        const filledLines = [];
        const unfilledLines = [];
        let filledCount = 0;
        let totalCount = 0;

        groups.forEach(group => {
            const context = this._resolvePendingOperatorContext(group.operatorId, operators);
            const filledPairs = [];
            const missingNames = [];
            const metadata = this._getCachedOperatorMetadata(context.operatorType);

            group.fields.forEach(field => {
                const parameterName = field.parameterName;
                totalCount += 1;
                const fieldType = this._normalizePendingFieldType(this._findMetadataParameter(metadata, parameterName));
                const value = this._getPendingDraftConfirmedValue(group.operatorId, parameterName);
                if (this._hasPendingDraftValue(value, fieldType)) {
                    filledCount += 1;
                    filledPairs.push(`${parameterName}=${this._stringifyPendingDraftValue(value, fieldType)}`);
                } else {
                    missingNames.push(parameterName);
                }
            });

            if (filledPairs.length > 0) {
                filledLines.push(`- ${context.label}：${filledPairs.join('；')}`);
            }
            if (missingNames.length > 0) {
                unfilledLines.push(`- ${context.label}：仍缺少 ${missingNames.join('、')}`);
            }
        });

        if (filledLines.length > 0) {
            lines.push('我已补录以下参数：');
            lines.push(...filledLines);
        } else {
            lines.push('我暂时还没有填入任何参数值，请继续指出最关键的缺项。');
        }

        if (unfilledLines.length > 0) {
            lines.push('以下参数仍未填写，请继续保留为待确认项并说明还缺什么：');
            lines.push(...unfilledLines);
        }

        const missingResources = this._normalizeMissingResources(
            this.currentResult?.missingResources ?? this.currentResult?.MissingResources
        );
        if (missingResources.length > 0) {
            lines.push('当前仍存在缺失资源：');
            missingResources.forEach(item => {
                const detail = item.description || item.resourceKey || item.resourceType || '缺少资源';
                lines.push(`- ${detail}`);
            });
        }

        const canvasManualEdits = this._getCanvasManualEditRecords(this.currentResult);
        if (canvasManualEdits.length > 0) {
            lines.push('画布人工修改记录（metadata-only，来源：流程页算子属性面板）：');
            canvasManualEdits.slice(-12).forEach(record => {
                lines.push(`- ${record.displayName || record.operatorId || '未命名算子'} / ${record.parameterName}: ${record.oldValueSummary || '--'} -> ${record.newValueSummary || '--'}；pendingParameter=${record.isPendingParameter ? 'true' : 'false'}；affectsDeploymentReady=${record.affectsDeploymentReady ? 'true' : 'false'}`);
            });
        }

        if (queuedHint) {
            lines.push(`附加提示：${queuedHint}`);
        }

        if (extraNote) {
            lines.push(`用户补充说明：${extraNote}`);
        }

        const userMessage = `提交 AI 复核（可选）：已确认人工参数 ${filledCount}/${totalCount} 项，画布人工修改 ${canvasManualEdits.length} 条${extraNote ? '，已附加补充说明' : ''}。`;
        return {
            hint: lines.join('\n'),
            userMessage,
            existingFlowJson: flow
        };
    },

    _stringifyPendingDraftValue(value, fieldType = '') {
        if (this._isPendingBooleanField(fieldType)) {
            const normalized = this.normalizeBooleanLike(value);
            return normalized === null ? '' : (normalized ? 'true' : 'false');
        }
        return String(value ?? '');
    },

    _resolvePendingParametersForDraft(data) {
        if (Array.isArray(data)) {
            return this._normalizePendingParameters(data);
        }

        const explicitPending = this._normalizePendingParameters(
            data?.pendingParameters ?? data?.PendingParameters
        );
        const missingResourcePending = this._buildPendingParametersFromMissingResources(
            data?.missingResources ?? data?.MissingResources
        );
        return this._mergePendingParameterGroups([...explicitPending, ...missingResourcePending]);
    },

    _buildPendingParametersFromMissingResources(items) {
        const resources = this._normalizeMissingResources(items);
        return resources
            .map(resource => {
                const parameterName = resource.parameterName || this._inferPendingParameterNameFromMissingResource(resource);
                const operatorId = resource.operatorId || this._inferPendingOperatorIdFromResourceKey(resource.resourceKey);
                if (!operatorId || !parameterName) {
                    return null;
                }

                return {
                    operatorId: operatorId || resource.actualOperatorId,
                    actualOperatorId: resource.actualOperatorId,
                    parameterNames: [parameterName]
                };
            })
            .filter(Boolean);
    },

    _mergePendingParameterGroups(items) {
        const merged = new Map();
        this._normalizePendingParameters(items).forEach(item => {
            const key = (item.operatorId || item.actualOperatorId || item.parameterNames.join('|')).toLowerCase();
            if (!key) return;

            const existing = merged.get(key) || {
                operatorId: item.operatorId,
                actualOperatorId: item.actualOperatorId,
                parameterNames: []
            };
            if (!existing.operatorId && item.operatorId) {
                existing.operatorId = item.operatorId;
            }
            if (!existing.actualOperatorId && item.actualOperatorId) {
                existing.actualOperatorId = item.actualOperatorId;
            }

            item.parameterNames.forEach(name => {
                if (!existing.parameterNames.some(current => current.toLowerCase() === name.toLowerCase())) {
                    existing.parameterNames.push(name);
                }
            });
            merged.set(key, existing);
        });

        return Array.from(merged.values());
    },

    _normalizePendingParameters(items) {
        if (!Array.isArray(items)) return [];

        return items
            .map(item => {
                const rawNames = item?.parameterNames ?? item?.ParameterNames;
                const parameterNames = Array.isArray(rawNames)
                    ? [...new Set(rawNames.map(name => String(name || '').trim()).filter(Boolean))]
                    : [];

                const actualOperatorId = String(item?.actualOperatorId ?? item?.ActualOperatorId ?? '').trim();
                const operatorId = String(item?.operatorId ?? item?.OperatorId ?? '').trim() || actualOperatorId;

                return {
                    operatorId,
                    actualOperatorId,
                    parameterNames
                };
            })
            .filter(item => item.operatorId || item.actualOperatorId || item.parameterNames.length > 0);
    },

    _normalizeMissingResources(items) {
        if (!Array.isArray(items)) return [];

        return items
            .map(item => {
                const resourceKey = String(item?.resourceKey ?? item?.ResourceKey ?? '').trim();
                const actualOperatorId = String(item?.actualOperatorId ?? item?.ActualOperatorId ?? '').trim();
                const operatorId = String(
                    item?.operatorId ??
                    item?.OperatorId ??
                    item?.tempId ??
                    item?.TempId ??
                    ''
                ).trim() || this._inferPendingOperatorIdFromResourceKey(resourceKey) || actualOperatorId;
                const parameterName = String(
                    item?.parameterName ??
                    item?.ParameterName ??
                    this._inferPendingParameterNameFromResourceKey(resourceKey) ??
                    ''
                ).trim();
                return {
                    resourceType: String(item?.resourceType ?? item?.ResourceType ?? '').trim(),
                    resourceKey,
                    description: String(item?.description ?? item?.Description ?? '').trim(),
                    operatorId,
                    actualOperatorId,
                    parameterName
                };
            })
            .filter(item => item.resourceType || item.resourceKey || item.description);
    },

    _inferPendingOperatorIdFromResourceKey(resourceKey) {
        const normalized = String(resourceKey || '').trim();
        if (!this._inferPendingParameterNameFromResourceKey(normalized)) {
            return '';
        }

        const dotIndex = normalized.indexOf('.');
        return dotIndex > 0 ? normalized.slice(0, dotIndex).trim() : '';
    },

    _inferPendingParameterNameFromResourceKey(resourceKey) {
        const normalized = String(resourceKey || '').trim();
        const dotIndex = normalized.lastIndexOf('.');
        if (dotIndex < 0 || dotIndex >= normalized.length - 1) {
            return '';
        }

        const candidate = normalized.slice(dotIndex + 1).trim();
        return this._isKnownPendingResourceParameter(candidate) ? candidate : '';
    },

    _isKnownPendingResourceParameter(parameterName) {
        const normalized = String(parameterName || '').trim().toLowerCase();
        return normalized === 'camerabindingid' ||
            normalized === 'cameraid' ||
            normalized === 'modelpath' ||
            normalized === 'modelid' ||
            normalized === 'modelcatalogpath' ||
            normalized === 'template' ||
            normalized === 'templatepath' ||
            normalized === 'templateid' ||
            normalized === 'scale' ||
            normalized === 'pixelscale' ||
            normalized === 'calibrationscale' ||
            normalized === 'outputchannel' ||
            normalized === 'outputchannelid' ||
            normalized === 'channel' ||
            normalized === 'plcparameters' ||
            normalized === 'plcaddress' ||
            normalized === 'plcaddressparameters';
    },

    _inferPendingParameterNameFromMissingResource(resource) {
        const directName = String(resource?.parameterName || '').trim();
        if (directName) {
            return directName;
        }

        const fromKey = this._inferPendingParameterNameFromResourceKey(resource?.resourceKey);
        if (fromKey) {
            return fromKey;
        }

        const type = String(resource?.resourceType || '').trim().toLowerCase();
        if (type.includes('camera')) {
            return 'CameraBindingId';
        }
        if (type.includes('model')) {
            return 'ModelPath';
        }
        if (type.includes('template')) {
            return 'Template';
        }
        if (type.includes('measurement') || type.includes('calibration')) {
            return 'Scale';
        }
        if (type.includes('output')) {
            return 'OutputChannel';
        }
        if (type.includes('plc')) {
            return 'PlcParameters';
        }

        return '';
    }
};
