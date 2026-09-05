import {
    getOperatorTypeDisplayName,
    getParameterDisplayName,
    getResourceDisplayName
} from '../../shared/operatorDisplayNames.js';
import { isPendingParameterSentinel } from '../../shared/parameterDependencyRules.js';
import webMessageBridge from '../../core/messaging/webMessageBridge.js';

export const aiPanelResourceBindingMixin = {
    _getMissingResourceActionModel(item = {}) {
        const normalizedItem = this._normalizeMissingResources?.([item])?.[0] || item;
        const resourceType = String(normalizedItem?.resourceType || '').trim().toLowerCase();
        const parameterName = String(normalizedItem?.parameterName || '').trim() || this._inferPendingParameterNameFromMissingResource?.(normalizedItem) || '';
        const make = (overrides = {}) => ({
            action: 'bind_resource_metadata',
            primaryLabel: '人工确认资源元数据',
            inputLabel: '资源元数据',
            inputType: 'text',
            placeholder: '输入人工确认后的资源 ID 或元数据键',
            parameterName,
            writesParameter: true,
            fieldType: 'text',
            ...overrides
        });

        if (resourceType.includes('model')) {
            return make({
                action: 'pick_model_resource',
                primaryLabel: '选择模型文件',
                inputLabel: '模型资源',
                inputType: '',
                parameterName: parameterName || 'ModelPath',
                writesParameter: true
            });
        }

        if (resourceType.includes('template')) {
            const templateParameter = parameterName && parameterName.toLowerCase() !== 'templatepath'
                ? parameterName
                : 'Template';
            return make({
                action: 'pick_template_resource',
                primaryLabel: '选择模板文件',
                inputLabel: '模板资源',
                inputType: '',
                parameterName: templateParameter,
                writesParameter: templateParameter.toLowerCase() !== 'template'
            });
        }

        if (resourceType.includes('measurement') || resourceType.includes('calibration')) {
            return make({
                action: 'fill_measurement_parameter',
                primaryLabel: '人工填写标定参数',
                inputLabel: '像素比例',
                inputType: 'number',
                placeholder: '输入 mm/px 或标定比例',
                parameterName: parameterName || 'Scale',
                fieldType: 'number'
            });
        }

        if (resourceType.includes('camera')) {
            return make({
                action: 'select_camera_binding',
                primaryLabel: '绑定所选相机',
                inputLabel: '相机绑定',
                inputType: 'camera_select',
                parameterName: parameterName || 'CameraBindingId'
            });
        }

        if (resourceType.includes('output')) {
            return make({
                action: 'set_output_channel',
                primaryLabel: '前往输出设置',
                inputLabel: '输出通道',
                inputType: '',
                parameterName: parameterName || 'OutputChannelId',
                writesParameter: false
            });
        }

        if (resourceType.includes('plc')) {
            return make({
                action: 'set_plc_address_metadata',
                primaryLabel: '前往通信设置',
                inputLabel: 'PLC 地址元数据',
                inputType: '',
                parameterName: parameterName || 'PlcAddress',
                writesParameter: false
            });
        }

        return make();
    },

    _renderMissingResourceActionControls(item, actionModel, index) {
        const draft = this._getPendingResourceDraft(item);
        const currentValue = draft?.value ?? '';
        const inputHtml = actionModel?.inputType === 'camera_select'
            ? `
                <label class="ai-followup-resource-input">
                    <span>${this._escapeHtml(actionModel.inputLabel || '相机绑定')}</span>
                    <select data-resource-input="true">
                        <option value="">请选择已登记相机</option>
                        ${(this.cameraBindingsCache || []).map(binding => `
                            <option value="${this._escapeHtml(String(binding?.id || ''))}">${this._escapeHtml(`${binding?.displayName || binding?.id || '相机'}${binding?.serialNumber ? ` (${binding.serialNumber})` : ''}`)}</option>
                        `).join('')}
                    </select>
                </label>
            `
            : actionModel?.inputType
            ? `
                <label class="ai-followup-resource-input">
                    <span>${this._escapeHtml(actionModel.inputLabel || '资源元数据')}</span>
                    <input
                        type="${this._escapeHtml(actionModel.inputType)}"
                        data-resource-input="true"
                        value="${this._escapeHtml(currentValue)}"
                        placeholder="${this._escapeHtml(actionModel.placeholder || '')}"
                    />
                </label>
            `
            : '';
        const metadataNote = String(item?.resourceType || '').toLowerCase().includes('plc')
            ? '<div class="ai-followup-item-meta">必须在通信设置中完成；不会从 AI 工作台直接写入 PLC。</div>'
            : '';
        const resolvedNote = ['bound', 'resolved'].includes(draft?.status)
            ? '<div class="ai-followup-item-meta">已写入待补草稿。</div>'
            : draft?.status === 'deferred'
                ? '<div class="ai-followup-item-meta">已标记稍后处理。</div>'
                : '';

        return `
            <div class="ai-followup-resource-controls">
                ${inputHtml}
                <div class="ai-followup-resource-actions">
                    <button class="ai-followup-resource-action" type="button" data-resource-index="${this._escapeHtml(String(index))}" data-resource-action="${this._escapeHtml(actionModel.action)}">${this._escapeHtml(actionModel.primaryLabel)}</button>
                    ${String(item?.draftPolicy || '').toLowerCase() === 'build_required'
                        ? `<button class="ai-followup-resource-action is-secondary" type="button" data-resource-index="${this._escapeHtml(String(index))}" data-resource-action="open_resource_location">前往解决位置</button>`
                        : `<button class="ai-followup-resource-action is-secondary" type="button" data-resource-index="${this._escapeHtml(String(index))}" data-resource-action="switch_to_draft">切换为可编辑草稿</button>`}
                </div>
                ${metadataNote}
                ${resolvedNote}
            </div>
        `;
    },

    _renderResourceAuditTaskCard(item, actionModel, index) {
        const normalizedItem = this._normalizeMissingResources?.([item])?.[0] || item || {};
        if (String(normalizedItem.resourceType || '').toLowerCase().includes('camera') &&
            (this.cameraBindingsCache || []).length === 0 &&
            !this.cameraBindingsLoadingPromise) {
            this._ensureCameraBindings?.('plan-resource').then(() => {
                if (this.pendingVisionPlan) this._renderPlanWorkspace?.(this.pendingVisionPlan);
            });
        }
        const resourceTypeRaw = normalizedItem.resourceType || 'resource';
        const resourceType = this._sanitizeResourceAuditDisplayText(resourceTypeRaw, 100) || 'resource';
        const resourceLabel = getResourceDisplayName(resourceType, { fallback: '资源' });
        const operatorIdRaw = normalizedItem.operatorId || normalizedItem.actualOperatorId || normalizedItem.operatorKey || normalizedItem.operatorType || this._inferPendingOperatorIdFromResourceKey?.(normalizedItem.resourceKey) || '';
        const operatorId = this._sanitizeResourceAuditDisplayText(operatorIdRaw, 120);
        const parameterNameRaw = normalizedItem.parameterName || this._inferPendingParameterNameFromMissingResource?.(normalizedItem) || actionModel?.parameterName || '';
        const parameterName = this._sanitizeResourceAuditDisplayText(parameterNameRaw, 120);
        const parameterLabel = getParameterDisplayName(parameterName, { fallback: parameterName || '影响参数' });
        const blockerReason = normalizedItem.description || normalizedItem.resourceKey || '部署前资源元数据缺失，必须人工确认后才能进入部署就绪。';
        const suggestion = this._getResourceAuditSuggestion(normalizedItem, actionModel);
        const statusLabel = normalizedItem.resourceDecision?.status === 'bound' || normalizedItem.status === 'bound' ? '已绑定' : '待绑定';
        const scopeLabel = normalizedItem.blocksBuild === true || normalizedItem.blockingScope === 'build'
            ? '阻止构建'
            : normalizedItem.blockingScope === 'build_deploy_run' ? '阻止构建、部署和运行' : '允许草稿；阻止部署和运行';
        const sourceLabel = (normalizedItem.sources || [normalizedItem.source]).filter(Boolean).join(' / ') || '权威 Readiness';
        const resolutionLabel = this._formatResourceResolutionTarget?.(normalizedItem.resolutionTarget) || normalizedItem.resolutionTarget || '当前方案';
        const flow = this.currentResult?.flow || this.currentResult?.Flow;
        const operator = (this._extractOperators?.(flow || {}) || []).find(op =>
            [op.id, op.Id, op.tempId, op.TempId].some(id => id && String(id) === String(operatorIdRaw)));
        const operatorLabel = operator ? this._formatApplyOperatorLabel?.(operator)
            : getOperatorTypeDisplayName(normalizedItem.operatorType, { fallback: '待识别算子' });
        const technical = [
            resourceType ? `resourceType=${resourceType}` : '',
            normalizedItem.resourceKey ? `resourceRef=${this._sanitizeResourceAuditDisplayText(normalizedItem.resourceKey, 160)}` : '',
            operatorId ? `operator=${operatorId}` : '',
            parameterName ? `parameter=${parameterName}` : '',
            `来源=${sourceLabel}`
        ].filter(Boolean).join('；');

        return `
            <article class="ai-followup-item ai-followup-resource-task ai-resource-audit-card" data-resource-index="${this._escapeHtml(String(index))}">
                <div class="ai-resource-audit-card-head">
                    <div>
                        <div class="ai-followup-item-title" title="${this._escapeHtml(resourceType)}">${this._escapeHtml(resourceLabel)}</div>
                        <div class="ai-followup-item-body">${this._escapeHtml(normalizedItem.resourceName || resourceLabel)}</div>
                    </div>
                    <span class="ai-resource-audit-badge">${this._escapeHtml(statusLabel)}</span>
                </div>
                <div class="ai-resource-audit-grid">
                    <span><small>资源</small><b>${this._escapeHtml(`${resourceLabel}${normalizedItem.resourceName && normalizedItem.resourceName !== resourceLabel ? ` · ${normalizedItem.resourceName}` : ''}`)}</b></span>
                    <span><small>影响算子</small><b>${this._escapeHtml(operatorLabel || '待识别算子')}</b></span>
                    <span><small>影响参数</small><b>${this._escapeHtml(parameterLabel)}</b></span>
                    <span><small>阻断范围</small><b>${this._escapeHtml(scopeLabel)}</b></span>
                    <span><small>解决位置</small><b>${this._escapeHtml(resolutionLabel)}</b></span>
                    <span><small>阻断原因</small><b>${this._escapeHtml(this._sanitizeAuditText(blockerReason))}</b></span>
                    <span><small>AI 建议</small><b>${this._escapeHtml(suggestion)}</b></span>
                </div>
                <div class="ai-resource-audit-manual-title">下一步</div>
                ${this._renderMissingResourceActionControls(normalizedItem, actionModel, index)}
                <details class="ai-resource-audit-details">
                    <summary>查看技术详情</summary>
                    <div>${this._escapeHtml(this._sanitizeAuditText(technical || '暂无技术详情'))}</div>
                </details>
            </article>
        `;
    },

    _getResourceAuditSuggestion(item = {}, actionModel = {}) {
        const type = String(item?.resourceType || '').trim().toLowerCase();
        if (type.includes('model')) return '请核对模型与检测任务的适配性。';
        if (type.includes('template')) return '请人工选择模板资源并确认版本，AI 不会替用户选择模板文件。';
        if (type.includes('measurement') || type.includes('calibration')) return '请人工填写标定或像素比例，并确认单位来源。';
        if (type.includes('camera')) return '请人工选择已登记的相机绑定，系统不会访问真实相机。';
        if (type.includes('output')) return '请人工确认输出通道元数据，默认不启用真实输出。';
        if (type.includes('plc')) return '仅记录 PLC 元数据用于审计，不写入 PLC，也不启用输出。';
        return '请人工确认资源元数据，AI 只提示缺口与风险。';
    },

    _formatResourceResolutionTarget(value = '') {
        const target = String(value || '').toLowerCase();
        if (target === 'settings:cameras') return '设置 → 相机';
        if (target === 'picker:model') return '模型文件选择器';
        if (target === 'picker:template') return '模板文件选择器';
        if (target === 'settings:calibration') return '标定设置';
        if (target === 'settings:communication') return '设置 → 通信';
        if (target === 'replan') return '重新规划';
        return '当前方案';
    },

    _getPendingResourceDraftKey(item = {}) {
        const normalizedItem = this._normalizeMissingResources?.([item])?.[0] || item;
        const canonicalId = String(normalizedItem?.canonicalId || '').trim();
        if (canonicalId) return canonicalId;
        const resourceKey = String(normalizedItem?.resourceKey || '').trim();
        if (resourceKey) return resourceKey;
        return [
            normalizedItem?.resourceType || 'resource',
            normalizedItem?.operatorId || normalizedItem?.actualOperatorId || '',
            normalizedItem?.parameterName || ''
        ].map(value => String(value || '').trim()).filter(Boolean).join(':');
    },

    _getPendingResourceDraft(item = {}) {
        const key = this._getPendingResourceDraftKey(item);
        return key && this.pendingResourceDrafts ? this.pendingResourceDrafts[key] || null : null;
    },

    _setPendingResourceDraft(item = {}, entry = {}) {
        const key = this._getPendingResourceDraftKey(item);
        if (!key) return;
        const normalizedItem = this._normalizeMissingResources?.([item])?.[0] || item;
        if (!this.pendingResourceDrafts) {
            this.pendingResourceDrafts = {};
        }
        this.pendingResourceDrafts[key] = {
            canonicalId: String(normalizedItem?.canonicalId || key).trim(),
            resourceType: String(normalizedItem?.resourceType || '').trim(),
            resourceKey: String(normalizedItem?.resourceKey || '').trim(),
            operatorId: String(normalizedItem?.operatorId || normalizedItem?.actualOperatorId || '').trim(),
            actualOperatorId: String(normalizedItem?.actualOperatorId || '').trim(),
            parameterName: String(normalizedItem?.parameterName || '').trim(),
            operatorKey: String(normalizedItem?.operatorKey || '').trim(),
            operatorType: String(normalizedItem?.operatorType || '').trim(),
            operatorIndex: Number(normalizedItem?.operatorIndex ?? -1),
            metadataOnly: true,
            ...entry
        };
        this._dispatchAgentWorkspaceEvent?.({
            type: 'workspace/resource-decision-set',
            payload: {
                resource: normalizedItem,
                decision: this.pendingResourceDrafts[key]
            },
            planId: this.agentWorkspaceState?.identity?.planId,
            planHash: this.agentWorkspaceState?.identity?.planHash
        });
    },

    _handleMissingResourceAction(item = {}, action = '', options = {}) {
        const normalizedItem = this._normalizeMissingResources?.([item])?.[0] || item;
        const normalizedAction = String(action || '').trim();
        const actionModel = this._getMissingResourceActionModel(normalizedItem);
        const result = options.data || this.currentResult || null;
        const flow = options.flow || result?.flow || result?.Flow || null;

        if (normalizedAction === 'switch_to_draft') {
            this._setRequirementMode?.('draft');
            this._setPlanCtaFeedback?.('已切换为可编辑草稿模式；后端将重新判定哪些资源可后补。', 'info');
            return true;
        }

        if (normalizedAction === 'open_resource_location' ||
            normalizedAction === 'set_output_channel' ||
            normalizedAction === 'set_plc_address_metadata') {
            return this._navigateToResourceResolution?.(normalizedItem) === true;
        }

        if ((normalizedAction === 'pick_model_resource' || normalizedAction === 'pick_template_resource') &&
            !String(options.value ?? '').trim()) {
            this.pendingParameterFilePickContext = {
                resource: normalizedItem,
                action: normalizedAction,
                result,
                flow,
                operatorId: normalizedItem.operatorId || '',
                parameterName: actionModel.parameterName || normalizedItem.parameterName || ''
            };
            webMessageBridge.sendMessage('PickFileCommand', {
                parameterName: 'aiPendingParameterFile',
                filter: normalizedAction === 'pick_model_resource'
                    ? 'Model Files|*.onnx;*.pt;*.pth;*.engine;*.bin|All Files|*.*'
                    : 'Template Files|*.png;*.jpg;*.jpeg;*.bmp;*.json|All Files|*.*'
            });
            return true;
        }

        if (normalizedAction === 'defer_resource') {
            const mode = this._normalizeRequirementMode?.(this.requirementMode) || 'strict';
            if (mode !== 'draft' || normalizedItem.draftPolicy === 'build_required') {
                this._setResultStatusNote('该资源不能在当前模式下暂缓，请完成绑定、前往解决位置或重新规划。', 'warning');
                return false;
            }
            this._setPendingResourceDraft(normalizedItem, { status: 'deferred', source: 'user_deferred', value: '' });
            this._setResultStatusNote('已标记为草稿后补；部署和运行门禁继续保留。', 'info');
            this._addMessage('system', '该资源由后端判定为可在草稿后补；部署和运行仍被阻止。');
            this._submitClarificationBatchIfComplete?.('resource_deferred');
            if (this.pendingVisionPlan) this._requestPlanReadinessPreview?.(this.pendingVisionPlan, { reason: 'resource_deferred' });
            if (result) this._renderFollowupChecklist(result, flow);
            return false;
        }

        const rawValue = String(options.value ?? '').trim();
        if (!rawValue) {
            this._setResultStatusNote(`请先填写${actionModel.inputLabel || '资源元数据'}，再执行${actionModel.primaryLabel}。`, 'warning');
            return false;
        }

        const fieldType = actionModel.fieldType || 'text';
        const parameterName = actionModel.parameterName || normalizedItem.parameterName || '';
        const operatorId = String(
            normalizedItem.operatorId ||
            normalizedItem.actualOperatorId ||
            this._inferPendingOperatorIdFromResourceKey?.(normalizedItem.resourceKey) ||
            ''
        ).trim();
        if (fieldType === 'number' && !Number.isFinite(Number(rawValue))) {
            this._setResultStatusNote('标定/像素比例必须是有效数值。', 'warning');
            return false;
        }

        if (operatorId && parameterName && actionModel.writesParameter !== false) {
            this._setPendingDraftConfirmedValue(operatorId, parameterName, rawValue, fieldType, 'resource_binding');
        }

        const gateBefore = this._snapshotApplyGateState(result);
        this._setPendingResourceDraft(normalizedItem, {
            status: 'bound',
            source: 'resource_binding',
            action: normalizedAction,
            value: rawValue,
            valueSummary: this._summarizeManualConfirmationValue(rawValue, normalizedItem),
            parameterName,
            operatorId,
            confirmedAtUtc: new Date().toISOString(),
            confirmedBy: 'local-user'
        });
        this._submitClarificationBatchIfComplete?.('resource_bound');

        if (this.pendingVisionPlan) {
            this._requestPlanReadinessPreview?.(this.pendingVisionPlan, { reason: 'resource_bound' });
            this._renderPlanWorkspace?.(this.pendingVisionPlan);
        }

        if (!result) {
            this._setPlanCtaFeedback?.(`已绑定${actionModel.inputLabel || normalizedItem.resourceName || '资源'}，正在重新校验构建条件。`, 'info');
            return true;
        }

        this._appendManualResourceConfirmationRecord(result, normalizedItem, {
            action: normalizedAction,
            actionLabel: actionModel.primaryLabel,
            parameterName,
            operatorId,
            value: rawValue,
            valueSummary: this._summarizeManualConfirmationValue(rawValue, normalizedItem),
            writesParameter: actionModel.writesParameter !== false
        });

        this._applyResourceResolutionToResult(result, normalizedItem, {
            parameterName,
            operatorId,
            value: rawValue,
            writesParameter: actionModel.writesParameter !== false
        });
        this._updateLatestManualConfirmationGateChange(result, gateBefore, this._snapshotApplyGateState(result));

        this._renderFollowupChecklist(result, flow);
        this._renderParameterDraftEditor(result, flow);
        this._renderAgentRuntime(result);
        this._renderValidationConsole(result);
        this._updateApplyButtonState();

        const risk = this._buildApplyRiskSummary?.(result) || { hasWarnings: true, totalCount: 0 };
        const note = risk.hasWarnings
            ? `已记录${actionModel.inputLabel || '资源'}元数据，仍有 ${risk.totalCount} 项部署前待补。`
            : '资源信息已补齐，已满足元数据检查的应用条件；尚未运行真实样本。';
        this._setResultStatusNote(note, risk.hasWarnings ? 'info' : 'success');
        this._addMessage('system', note);
        return true;
    },

    _navigateToResourceResolution(item = {}) {
        const target = String(item?.resolutionTarget || '').toLowerCase();
        if (target.startsWith('picker:model')) return this._handleMissingResourceAction(item, 'pick_model_resource');
        if (target.startsWith('picker:template')) return this._handleMissingResourceAction(item, 'pick_template_resource');
        if (target === 'replan') {
            this._setPlanCtaFeedback?.('当前资源无法在本阶段补齐，请重新规划为已有资源可支持的路线。', 'warning');
            return this._retryPlanningLifecycle?.() === true;
        }
        const settingsButton = document.querySelector('.nav-btn[data-view="settings"]');
        if (!settingsButton) {
            this._setPlanCtaFeedback?.('未找到设置入口，请从顶部导航进入设置页处理该资源。', 'warning');
            return false;
        }
        const tab = target === 'settings:cameras' ? 'cameras'
            : target === 'settings:communication' ? 'communication'
                : target === 'settings:calibration' ? 'runtime' : '';
        this._armResourceReturnRevalidation?.();
        settingsButton.click();
        if (tab) {
            this._setOwnedTimeout?.(() => document.querySelector(`.settings-menu-item[data-tab="${tab}"]`)?.click?.(), 120);
        }
        return true;
    },

    _armResourceReturnRevalidation() {
        if (this._resourceReturnRevalidationArmed) return;
        this._resourceReturnRevalidationArmed = true;
        const aiButton = document.querySelector('.nav-btn[data-view="ai"]');
        aiButton?.addEventListener('click', () => {
            this._resourceReturnRevalidationArmed = false;
            if (this.pendingVisionPlan) {
                this.lastPlanReadinessRequestFingerprint = '';
                this._requestPlanReadinessPreview?.(this.pendingVisionPlan, { reason: 'resource_return' });
            }
        }, { once: true });
    },

    _snapshotApplyGateState(result = this.currentResult) {
        const gate = this._getPayloadApplyGate?.(result) || result?.applyGate || result?.ApplyGate || null;
        if (!gate || typeof gate !== 'object') {
            return {
                status: '',
                deploymentReady: false,
                deploymentBlockers: []
            };
        }

        return {
            status: String(gate.status || gate.Status || '').trim(),
            deploymentReady: Boolean(gate.deploymentReady ?? gate.DeploymentReady),
            deploymentBlockers: this._toArray?.(gate.deploymentBlockers || gate.DeploymentBlockers) || []
        };
    },

    _appendManualResourceConfirmationRecord(result, resource, resolution = {}) {
        if (!result || typeof result !== 'object') return;

        const normalizedItem = this._normalizeMissingResources?.([resource])?.[0] || resource || {};
        const record = {
            confirmedAtUtc: new Date().toISOString(),
            actor: 'local-user',
            resourceType: String(normalizedItem.resourceType || '').trim(),
            affectedOperator: String(resolution.operatorId || normalizedItem.operatorId || normalizedItem.actualOperatorId || '').trim(),
            affectedParameters: [String(resolution.parameterName || normalizedItem.parameterName || '').trim()].filter(Boolean),
            originalMissingResource: {
                resourceType: String(normalizedItem.resourceType || '').trim(),
                resourceRef: String(normalizedItem.resourceKey || '').trim(),
                operatorId: String(normalizedItem.operatorId || normalizedItem.actualOperatorId || '').trim(),
                parameterName: String(normalizedItem.parameterName || resolution.parameterName || '').trim(),
                description: this._sanitizeAuditText(normalizedItem.description || '')
            },
            writebackSummary: resolution.valueSummary || this._summarizeManualConfirmationValue(resolution.value, normalizedItem),
            action: resolution.action || '',
            actionLabel: resolution.actionLabel || '',
            resourceRef: String(normalizedItem.resourceKey || [resolution.operatorId, resolution.parameterName].filter(Boolean).join('.')).trim(),
            parameterName: String(resolution.parameterName || normalizedItem.parameterName || '').trim(),
            metadataOnly: true,
            applyGateChange: null,
            deploymentBlocked: true
        };

        this._storeManualResourceConfirmationRecord(result, record);
        const buildResult = this._getPayloadBuildResult?.(result) || null;
        if (buildResult && buildResult !== result) {
            this._storeManualResourceConfirmationRecord(buildResult, record);
        }

        const flow = result.flow || result.Flow || null;
        if (flow && typeof flow === 'object') {
            this._storeManualResourceConfirmationRecord(flow, record);
        }
    },

    _storeManualResourceConfirmationRecord(target, record) {
        if (!target || typeof target !== 'object') return;
        const key = Object.prototype.hasOwnProperty.call(target, 'ManualResourceConfirmations') ||
            Object.prototype.hasOwnProperty.call(target, 'ApplyGate') ||
            Object.prototype.hasOwnProperty.call(target, 'Flow')
            ? 'ManualResourceConfirmations'
            : 'manualResourceConfirmations';
        const records = Array.isArray(target[key]) ? target[key] : [];
        const resourceRef = String(record.resourceRef || record.resourceKey || '').toLowerCase();
        const nextRecords = records.filter(item => {
            const existingRef = String(item?.resourceRef || item?.ResourceRef || item?.resourceKey || item?.ResourceKey || '').toLowerCase();
            return !resourceRef || existingRef !== resourceRef;
        });
        nextRecords.push({ ...record });
        target[key] = nextRecords;
    },

    _updateLatestManualConfirmationGateChange(result, before, after) {
        const update = target => {
            const records = this._getManualResourceConfirmationRecords(target);
            const latest = records[records.length - 1];
            if (!latest) return;
            latest.applyGateChange = {
                from: before?.status || '',
                to: after?.status || '',
                deploymentReadyBefore: Boolean(before?.deploymentReady),
                deploymentReadyAfter: Boolean(after?.deploymentReady),
                clearedBlockers: this._toArray(before?.deploymentBlockers)
                    .filter(item => !this._toArray(after?.deploymentBlockers).includes(item))
            };
            latest.deploymentBlocked = !Boolean(after?.deploymentReady);
        };

        update(result);
        update(this._getPayloadBuildResult?.(result) || null);
        update(result?.flow || result?.Flow || null);
    },

    _getManualResourceConfirmationRecords(target = this.currentResult) {
        if (!target || typeof target !== 'object') return [];
        const records = target.manualResourceConfirmations || target.ManualResourceConfirmations || [];
        return Array.isArray(records) ? records : [];
    },

    _summarizeManualConfirmationValue(value, resource = {}) {
        const text = String(value ?? '').trim();
        const type = String(resource?.resourceType || '').toLowerCase();
        if (!text) return '已记录空值检查';
        if (type.includes('plc')) return 'PLC 元数据已记录，未执行写入';
        if (/([a-z]:\\|\\\\|\/[^/\s]+\/|\.onnx\b|\.pt\b|\.bmp\b|\.png\b|\.jpg\b|\.jpeg\b|\.tif\b|\.tiff\b)/i.test(text)) {
            return '已记录资源标识，完整路径已隐藏';
        }
        if (/\b\d{1,3}(?:\.\d{1,3}){3}\b/.test(text) || /token|api[-_]?key|secret/i.test(text)) {
            return '已记录资源标识，敏感值已隐藏';
        }
        return text.length > 80 ? `${text.slice(0, 77)}...` : text;
    },

    _sanitizeResourceAuditDisplayText(value, maxChars = 220) {
        const text = this._sanitizeAuditText(value);
        return text ? text.slice(0, maxChars) : '';
    },

    _sanitizeAuditText(value) {
        const raw = String(value ?? '');
        const text = this._sanitizeAssistantFailureText?.(raw, 1200) || raw;
        return text
            .replace(/[a-z]:\\[^\s"'<>]+/gi, '<本地路径已隐藏>')
            .replace(/\\\\[^\s"'<>]+/g, '<网络路径已隐藏>')
            .replace(/\b\d{1,3}(?:\.\d{1,3}){3}\b/g, '<IP已隐藏>')
            .replace(/(token|api[-_]?key|secret)\s*[:=]\s*[^\s"'<>]+/gi, '$1=<已隐藏>')
            .replace(/data:image\/[a-z0-9.+-]+;base64,[a-z0-9+/=]+/gi, '<base64图像已隐藏>');
    },

    _applyResourceResolutionToResult(result, resource, resolution = {}) {
        if (!result || typeof result !== 'object') return;

        const applyTo = (target) => {
            if (!target || typeof target !== 'object') return;
            this._removeResolvedResourceFromContainer(target, resource, resolution);
            this._refreshApplyGateAfterResourceResolution(target);
        };

        applyTo(result);
        const buildResult = this._getPayloadBuildResult?.(result) || null;
        applyTo(buildResult);

        if (resolution.writesParameter && resolution.operatorId && resolution.parameterName) {
            const flow = result.flow || result.Flow || null;
            const operators = this._extractOperators(flow);
            const context = this._resolvePendingOperatorContext(resolution.operatorId, operators);
            if (context.operator && !this._isResourceOnlyDraftParameter(context.operatorType, resolution.parameterName)) {
                this._writeOperatorParameterValue(context.operator, resolution.parameterName, resolution.value);
            }
        }
    },

    _removeResolvedResourceFromContainer(target, resource, resolution = {}) {
        const pendingKeys = ['pendingParameters', 'PendingParameters'];
        const missingKeys = ['missingResources', 'MissingResources'];
        const diff = target.workflowDiff || target.WorkflowDiff || null;
        const gate = target.applyGate || target.ApplyGate || null;

        missingKeys.forEach(key => {
            if (Array.isArray(target[key])) {
                target[key] = target[key].filter(item => !this._doesMissingResourceMatch(item, resource, resolution));
            }
        });

        pendingKeys.forEach(key => {
            if (Array.isArray(target[key])) {
                target[key] = this._removeResolvedPendingParameters(target[key], resource, resolution);
            }
        });

        if (diff && typeof diff === 'object') {
            ['pendingParameters', 'PendingParameters', 'deploymentBlockers', 'DeploymentBlockers'].forEach(key => {
                if (Array.isArray(diff[key])) {
                    diff[key] = diff[key].filter(value => !this._doesResourceReferenceMatch(value, resource, resolution));
                }
            });
        }

        if (gate && typeof gate === 'object') {
            ['deploymentBlockers', 'DeploymentBlockers'].forEach(key => {
                if (Array.isArray(gate[key])) {
                    gate[key] = gate[key].filter(value => !this._doesResourceReferenceMatch(value, resource, resolution));
                }
            });
        }
    },

    _removeResolvedPendingParameters(items, resource, resolution = {}) {
        const candidates = this._getResolvedParameterCandidates(resource, resolution);
        const operatorId = String(resolution.operatorId || resource?.operatorId || resource?.actualOperatorId || '').trim().toLowerCase();
        return (Array.isArray(items) ? items : [])
            .map(item => {
                const itemOperator = String(item?.operatorId ?? item?.OperatorId ?? item?.actualOperatorId ?? item?.ActualOperatorId ?? '').trim().toLowerCase();
                if (operatorId && itemOperator && itemOperator !== operatorId) {
                    return item;
                }

                const rawNames = item?.parameterNames ?? item?.ParameterNames;
                const names = Array.isArray(rawNames) ? rawNames : [];
                const filtered = names.filter(name => !candidates.has(String(name || '').trim().toLowerCase()));
                if (filtered.length === names.length) {
                    return item;
                }

                return {
                    ...item,
                    parameterNames: filtered,
                    ParameterNames: item?.ParameterNames ? filtered : item?.ParameterNames
                };
            })
            .filter(item => {
                const names = item?.parameterNames ?? item?.ParameterNames;
                return Array.isArray(names) && names.length > 0;
            });
    },

    _getResolvedParameterCandidates(resource, resolution = {}) {
        const values = [
            resolution.parameterName,
            resource?.parameterName,
            this._inferPendingParameterNameFromMissingResource?.(resource)
        ].map(value => String(value || '').trim()).filter(Boolean);
        const type = String(resource?.resourceType || '').trim().toLowerCase();
        if (type.includes('model')) values.push('ModelPath', 'ModelId', 'ModelCatalogPath');
        if (type.includes('template')) values.push('Template', 'TemplateId');
        if (type.includes('measurement') || type.includes('calibration')) values.push('Scale', 'PixelScale');
        if (type.includes('camera')) values.push('CameraBindingId', 'CameraId');
        if (type.includes('output')) values.push('OutputChannelId', 'OutputChannel', 'Channel');
        if (type.includes('plc')) values.push('PlcAddress', 'PLCParameters', 'PlcParameters');
        return new Set(values.map(value => value.toLowerCase()));
    },

    _doesMissingResourceMatch(candidate, resource, resolution = {}) {
        const normalizedCandidate = this._normalizeMissingResources([candidate])[0] || {};
        const normalizedResource = this._normalizeMissingResources([resource])[0] || {};
        if (normalizedCandidate.resourceKey && normalizedResource.resourceKey && normalizedCandidate.resourceKey === normalizedResource.resourceKey) {
            return true;
        }

        const candidateOperator = String(normalizedCandidate.operatorId || normalizedCandidate.actualOperatorId || '').trim().toLowerCase();
        const resourceOperator = String(resolution.operatorId || normalizedResource.operatorId || normalizedResource.actualOperatorId || '').trim().toLowerCase();
        const candidates = this._getResolvedParameterCandidates(normalizedResource, resolution);
        const candidateParameter = String(normalizedCandidate.parameterName || '').trim().toLowerCase();

        return Boolean(
            resourceOperator &&
            candidateOperator === resourceOperator &&
            (!candidateParameter || candidates.has(candidateParameter))
        );
    },

    _doesResourceReferenceMatch(value, resource, resolution = {}) {
        const text = String(value || '').trim();
        if (!text) return false;
        const normalizedResource = this._normalizeMissingResources?.([resource])?.[0] || resource;
        const resourceKey = String(normalizedResource?.resourceKey || '').trim();
        const textLower = text.toLowerCase();
        if (resourceKey && textLower.includes(resourceKey.toLowerCase())) return true;

        const operatorId = String(
            resolution.operatorId ||
            normalizedResource?.operatorId ||
            normalizedResource?.actualOperatorId ||
            ''
        ).trim();
        const candidates = this._getResolvedParameterCandidates(normalizedResource, resolution);
        return [...candidates].some(parameterName => {
            const reference = operatorId ? `${operatorId}.${parameterName}`.toLowerCase() : parameterName;
            return operatorId ? textLower.includes(reference) : textLower === reference;
        });
    },

    _refreshApplyGateAfterResourceResolution(target) {
        if (!target || typeof target !== 'object') return;

        const missing = this._normalizeMissingResources(target.missingResources ?? target.MissingResources);
        const pending = this._normalizePendingParameters(target.pendingParameters ?? target.PendingParameters);
        const hasUnconfirmedResources = this._hasUnconfirmedDeploymentRequirements(target);
        const hasBlockers = missing.length > 0 || pending.length > 0 || hasUnconfirmedResources;
        const gate = target.applyGate || target.ApplyGate || null;

        if (gate && typeof gate === 'object') {
            if ('deploymentReady' in gate || !('DeploymentReady' in gate)) gate.deploymentReady = !hasBlockers;
            if ('DeploymentReady' in gate) gate.DeploymentReady = !hasBlockers;
            if ('blocked' in gate || !('Blocked' in gate)) gate.blocked = false;
            if ('Blocked' in gate) gate.Blocked = false;
            if ('status' in gate || !('Status' in gate)) gate.status = hasBlockers ? 'canvas_apply_ready' : 'deployment_ready';
            if ('Status' in gate) gate.Status = hasBlockers ? 'canvas_apply_ready' : 'deployment_ready';
            const nextFix = this._buildFirstFixRecommendationFromFollowups(missing, pending);
            if ('firstFixRecommendation' in gate || !('FirstFixRecommendation' in gate)) gate.firstFixRecommendation = nextFix;
            if ('FirstFixRecommendation' in gate) gate.FirstFixRecommendation = nextFix;
        }

        const nextFix = this._buildFirstFixRecommendationFromFollowups(missing, pending);
        if ('firstFixRecommendation' in target || !('FirstFixRecommendation' in target)) target.firstFixRecommendation = nextFix;
        if ('FirstFixRecommendation' in target) target.FirstFixRecommendation = nextFix;

        const preview = target.validationPreview || target.ValidationPreview || null;
        const precheck = preview?.deploymentPrecheck || preview?.DeploymentPrecheck || null;
        if (precheck && typeof precheck === 'object') {
            if ('readyForDeployment' in precheck || !('ReadyForDeployment' in precheck)) precheck.readyForDeployment = !hasBlockers;
            if ('ReadyForDeployment' in precheck) precheck.ReadyForDeployment = !hasBlockers;
            if ('deploymentBlocked' in precheck || !('DeploymentBlocked' in precheck)) precheck.deploymentBlocked = hasBlockers;
            if ('DeploymentBlocked' in precheck) precheck.DeploymentBlocked = hasBlockers;
        }
    },

    _hasUnconfirmedDeploymentRequirements(target) {
        const flow = target?.flow || target?.Flow || target?.workflowDraft || target?.WorkflowDraft || target;
        const requirements = this._collectDeploymentConfirmationRequirements(flow);
        if (!requirements.length) return false;
        const records = [
            ...this._getManualResourceConfirmationRecords(target),
            ...this._getManualResourceConfirmationRecords(flow)
        ];
        return requirements.some(requirement => !records.some(record => this._doesManualConfirmationMatch(record, requirement)));
    },

    _collectDeploymentConfirmationRequirements(flow) {
        const operators = this._extractOperators?.(flow) || [];
        const requirements = [];
        const readParam = (op, names) => {
            const params = op.parameters || op.Parameters || {};
            for (const name of names) {
                const value = Array.isArray(params)
                    ? params.find(item => String(item?.name || item?.Name || '').toLowerCase() === name.toLowerCase())?.value
                    : params[name];
                const text = String(value ?? '').trim();
                if (text && !isPendingParameterSentinel(text)) {
                    return { name, value: text };
                }
            }
            return null;
        };
        const add = (op, resourceType, names) => {
            const match = readParam(op, names);
            if (!match) return;
            const operatorId = String(op.id || op.Id || op.tempId || op.TempId || op.name || op.Name || '').trim();
            if (!operatorId) return;
            requirements.push({
                resourceType,
                operatorId,
                parameterName: match.name,
                resourceKey: `${operatorId}.${match.name}`
            });
        };

        operators.forEach(op => {
            const type = String(op.type || op.Type || op.operatorType || op.OperatorType || '').trim().toLowerCase();
            if (type === 'imageacquisition') {
                const source = readParam(op, ['SourceType'])?.value || '';
                if (!['file', 'image', 'path'].includes(source.toLowerCase())) {
                    add(op, 'camera_binding', ['CameraId', 'CameraBindingId']);
                }
            }
            if (['deeplearning', 'onnxinference', 'semanticsegmentation', 'anomalydetection'].includes(type)) {
                add(op, 'model_resource', ['ModelPath', 'ModelId', 'ModelCatalogPath']);
            }
            if (type === 'templatematching') {
                add(op, 'template_artifact', ['Template', 'TemplateId', 'TemplatePath']);
            }
            if (type === 'unitconvert') {
                add(op, 'measurement_parameter', ['Scale', 'PixelScale', 'CalibrationScale']);
            }
            if (type === 'resultoutput') {
                add(op, 'output_channel', ['OutputChannel', 'OutputChannelId', 'Channel']);
                const outputMode = readParam(op, ['OutputChannel', 'OutputChannelId', 'Channel'])?.value || '';
                if (outputMode.toLowerCase() === 'plc') {
                    add(op, 'plc_address', ['PlcAddress', 'PLCParameters']);
                }
            }
            if (type.includes('plc')) {
                add(op, 'plc_address', ['PLCParameters', 'PlcAddress']);
            }
        });

        return requirements;
    },

    _doesManualConfirmationMatch(record = {}, requirement = {}) {
        if (!record || record.metadataOnly !== true && record.MetadataOnly !== true) return false;
        const resourceType = String(record.resourceType || record.ResourceType || '').trim().toLowerCase();
        const operatorId = String(record.affectedOperator || record.AffectedOperator || record.operatorId || record.OperatorId || '').trim().toLowerCase();
        const parameterName = String(record.parameterName || record.ParameterName || record.affectedParameters?.[0] || record.AffectedParameters?.[0] || '').trim().toLowerCase();
        const resourceKey = String(record.resourceRef || record.ResourceRef || record.resourceKey || record.ResourceKey || '').trim().toLowerCase();
        const expectedKey = String(requirement.resourceKey || '').trim().toLowerCase();
        return Boolean(
            (!resourceType || resourceType === String(requirement.resourceType || '').toLowerCase()) &&
            (resourceKey === expectedKey ||
                (operatorId === String(requirement.operatorId || '').toLowerCase() &&
                    parameterName === String(requirement.parameterName || '').toLowerCase()))
        );
    },

    _buildFirstFixRecommendationFromFollowups(missing = [], pending = []) {
        if (missing.length > 0) {
            const item = missing[0];
            const label = getResourceDisplayName(item.resourceType, { fallback: '资源' });
            const key = item.resourceKey || [item.operatorId, item.parameterName].filter(Boolean).join('.');
            return key ? `先补齐 ${label}：${key}` : `先补齐 ${label}`;
        }

        if (pending.length > 0) {
            const item = pending[0];
            const names = item.parameterNames?.length
                ? item.parameterNames.map(name => getParameterDisplayName(name, { fallback: name })).join('、')
                : '待确认参数';
            return `先确认 ${item.operatorId || '算子'} 的 ${names}`;
        }

        return '';
    },

    _isResourceOnlyDraftParameter(operatorType = '', parameterName = '') {
        const type = String(operatorType || '').trim().toLowerCase();
        const name = String(parameterName || '').trim().toLowerCase();
        if (type === 'templatematching' && name === 'template') {
            return true;
        }

        if (type === 'resultoutput' && ['outputchannel', 'outputchannelid', 'channel', 'plcaddress', 'plcparameters'].includes(name)) {
            return true;
        }

        return false;
    },

    _renderManualConfirmationRecords(target = this.currentResult) {
        const records = this._getManualResourceConfirmationRecords(target);
        const buildResult = this._getPayloadBuildResult?.(target) || null;
        const buildRecords = buildResult ? this._getManualResourceConfirmationRecords(buildResult) : [];
        const merged = [...records, ...buildRecords]
            .filter((record, index, list) => {
                const key = `${record.resourceRef || record.ResourceRef || record.resourceKey || record.ResourceKey || ''}|${record.confirmedAtUtc || record.ConfirmedAtUtc || ''}`;
                return key === '|' || list.findIndex(item => `${item.resourceRef || item.ResourceRef || item.resourceKey || item.ResourceKey || ''}|${item.confirmedAtUtc || item.ConfirmedAtUtc || ''}` === key) === index;
            });
        const rows = merged.length
            ? merged.slice().reverse().map(record => {
                const resourceType = this._sanitizeResourceAuditDisplayText(record.resourceType || record.ResourceType || '', 100);
                const operatorId = this._sanitizeResourceAuditDisplayText(record.affectedOperator || record.AffectedOperator || record.operatorId || record.OperatorId || '', 120);
                const parameters = (this._toArray?.(record.affectedParameters || record.AffectedParameters || [record.parameterName || record.ParameterName || '']) || []).map(item => this._sanitizeResourceAuditDisplayText(item, 120)).filter(Boolean);
                const gate = { ...(record.applyGateChange || record.ApplyGateChange || {}) };
                const cleared = this._toArray?.(gate.clearedBlockers || gate.ClearedBlockers) || [];
                const displayResourceType = this._sanitizeResourceAuditDisplayText(resourceType, 100) || resourceType;
                const displayOperatorId = this._sanitizeResourceAuditDisplayText(operatorId, 120);
                const displayParameters = (this._toArray?.(parameters) || []).map(item => this._sanitizeResourceAuditDisplayText(item, 120)).filter(Boolean);
                const displayWriteback = this._sanitizeResourceAuditDisplayText(record.writebackSummary || record.WritebackSummary || '', 200);
                const displayActor = this._sanitizeResourceAuditDisplayText(record.actor || record.Actor || 'local-user', 100) || 'local-user';
                const displayConfirmedAt = this._sanitizeResourceAuditDisplayText(record.confirmedAtUtc || record.ConfirmedAtUtc || '', 80);
                const gateFrom = this._sanitizeResourceAuditDisplayText(gate.from || gate.From || '', 80);
                const gateTo = this._sanitizeResourceAuditDisplayText(gate.to || gate.To || '', 80);
                record = { ...record, confirmedAtUtc: displayConfirmedAt, ConfirmedAtUtc: displayConfirmedAt, actor: displayActor, Actor: displayActor, writebackSummary: displayWriteback, WritebackSummary: displayWriteback };
                gate.from = gate.From = gateFrom;
                gate.to = gate.To = gateTo;
                return `
                    <div class="ai-manual-confirmation-record">
                        <div>
                            <strong>${this._escapeHtml(getResourceDisplayName(resourceType, { fallback: '资源' }))}</strong>
                            <span>${this._escapeHtml(record.confirmedAtUtc || record.ConfirmedAtUtc || '')} / ${this._escapeHtml(record.actor || record.Actor || 'local-user')}</span>
                        </div>
                        <p>${this._escapeHtml(operatorId || '待识别算子')} · ${this._escapeHtml(parameters.filter(Boolean).join('、') || '待识别参数')} · ${this._escapeHtml(record.writebackSummary || record.WritebackSummary || '')}</p>
                        <small>metadataOnly=true；ApplyGate：${this._escapeHtml(gate.from || gate.From || '未设置')} -> ${this._escapeHtml(gate.to || gate.To || '未设置')}；${cleared.length ? `已清理 ${this._escapeHtml(String(cleared.length))} 个对应阻断` : '仍需复核部署阻断'}；部署阻断：${record.deploymentBlocked || record.DeploymentBlocked ? '仍存在' : '已清空'}</small>
                    </div>
                `;
            }).join('')
            : '<div class="ai-followup-empty">当前构建结果暂无人工确认记录。</div>';

        return `
            <details class="ai-manual-confirmation-panel" ${merged.length ? 'open' : ''}>
                <summary>人工确认记录（${this._escapeHtml(String(merged.length))}）</summary>
                <div class="ai-manual-confirmation-list">${rows}</div>
            </details>
        `;
    }
};
