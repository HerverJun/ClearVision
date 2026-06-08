import {
    getParameterDisplayName,
    getResourceDisplayName
} from '../../shared/operatorDisplayNames.js';

export const aiPanelResourceBindingMixin = {
    _getMissingResourceActionModel(item = {}) {
        const normalizedItem = this._normalizeMissingResources?.([item])?.[0] || item;
        const resourceType = String(normalizedItem?.resourceType || '').trim().toLowerCase();
        const parameterName = String(normalizedItem?.parameterName || '').trim() || this._inferPendingParameterNameFromMissingResource?.(normalizedItem) || '';
        const make = (overrides = {}) => ({
            action: 'bind_resource_metadata',
            primaryLabel: '补齐资源元数据',
            inputLabel: '资源元数据',
            inputType: 'text',
            placeholder: '输入资源 ID 或元数据键',
            parameterName,
            writesParameter: true,
            fieldType: 'text',
            ...overrides
        });

        if (resourceType.includes('model')) {
            return make({
                action: 'bind_model_resource',
                primaryLabel: '绑定模型资源',
                inputLabel: '模型资源',
                placeholder: '输入模型资源 ID / 元数据键',
                parameterName: parameterName || 'ModelPath'
            });
        }

        if (resourceType.includes('template')) {
            const templateParameter = parameterName && parameterName.toLowerCase() !== 'templatepath'
                ? parameterName
                : 'Template';
            return make({
                action: 'select_template_artifact',
                primaryLabel: '选择模板文件',
                inputLabel: '模板资源',
                placeholder: '输入模板 ID / 模板元数据键',
                parameterName: templateParameter,
                writesParameter: templateParameter.toLowerCase() !== 'template'
            });
        }

        if (resourceType.includes('measurement') || resourceType.includes('calibration')) {
            return make({
                action: 'fill_measurement_parameter',
                primaryLabel: '填写标定/像素比例',
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
                primaryLabel: '选择相机绑定',
                inputLabel: '相机绑定',
                placeholder: '输入相机绑定 ID',
                parameterName: parameterName || 'CameraBindingId'
            });
        }

        if (resourceType.includes('output')) {
            return make({
                action: 'set_output_channel',
                primaryLabel: '设置输出通道',
                inputLabel: '输出通道',
                placeholder: '输入输出通道 ID',
                parameterName: parameterName || 'OutputChannelId'
            });
        }

        if (resourceType.includes('plc')) {
            return make({
                action: 'set_plc_address_metadata',
                primaryLabel: '记录 PLC 元数据',
                inputLabel: 'PLC 地址元数据',
                placeholder: '仅记录地址元数据，不写入 PLC',
                parameterName: parameterName || 'PlcAddress'
            });
        }

        return make();
    },

    _renderMissingResourceActionControls(item, actionModel, index) {
        const draft = this._getPendingResourceDraft(item);
        const currentValue = draft?.value ?? '';
        const inputHtml = actionModel?.inputType
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
            ? '<div class="ai-followup-item-meta">仅记录 metadata，不触发真实 PLC 写入。</div>'
            : '';
        const resolvedNote = draft?.status === 'resolved'
            ? '<div class="ai-followup-item-meta">已写入待补草稿。</div>'
            : draft?.status === 'deferred'
                ? '<div class="ai-followup-item-meta">已标记稍后处理。</div>'
                : '';

        return `
            <div class="ai-followup-resource-controls">
                ${inputHtml}
                <div class="ai-followup-resource-actions">
                    <button class="ai-followup-resource-action" type="button" data-resource-index="${this._escapeHtml(String(index))}" data-resource-action="${this._escapeHtml(actionModel.action)}">${this._escapeHtml(actionModel.primaryLabel)}</button>
                    <button class="ai-followup-resource-action is-secondary" type="button" data-resource-index="${this._escapeHtml(String(index))}" data-resource-action="defer_resource">稍后处理</button>
                </div>
                ${metadataNote}
                ${resolvedNote}
            </div>
        `;
    },

    _getPendingResourceDraftKey(item = {}) {
        const normalizedItem = this._normalizeMissingResources?.([item])?.[0] || item;
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
            resourceType: String(normalizedItem?.resourceType || '').trim(),
            resourceKey: String(normalizedItem?.resourceKey || '').trim(),
            operatorId: String(normalizedItem?.operatorId || normalizedItem?.actualOperatorId || '').trim(),
            actualOperatorId: String(normalizedItem?.actualOperatorId || '').trim(),
            parameterName: String(normalizedItem?.parameterName || '').trim(),
            metadataOnly: true,
            ...entry
        };
    },

    _handleMissingResourceAction(item = {}, action = '', options = {}) {
        const normalizedItem = this._normalizeMissingResources?.([item])?.[0] || item;
        const normalizedAction = String(action || '').trim();
        const actionModel = this._getMissingResourceActionModel(normalizedItem);
        const result = options.data || this.currentResult || null;
        const flow = options.flow || result?.flow || result?.Flow || null;

        if (normalizedAction === 'defer_resource') {
            this._setPendingResourceDraft(normalizedItem, { status: 'deferred', source: 'user_deferred', value: '' });
            this._setResultStatusNote('已标记为稍后处理；画布仍可应用，部署前会继续提示补齐。', 'info');
            this._addMessage('system', '已标记该资源为稍后处理，部署前仍会保留为待补项。');
            this._renderFollowupChecklist(result, flow);
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

        this._setPendingResourceDraft(normalizedItem, {
            status: 'resolved',
            source: 'resource_binding',
            action: normalizedAction,
            value: rawValue,
            parameterName,
            operatorId
        });

        this._applyResourceResolutionToResult(result, normalizedItem, {
            parameterName,
            operatorId,
            value: rawValue,
            writesParameter: actionModel.writesParameter !== false
        });

        this._renderFollowupChecklist(result, flow);
        this._renderParameterDraftEditor(result, flow);
        this._renderAgentRuntime(result);
        this._renderValidationConsole(result);
        this._updateApplyButtonState();

        const risk = this._buildApplyRiskSummary?.(result) || { hasWarnings: true, totalCount: 0 };
        const note = risk.hasWarnings
            ? `已记录${actionModel.inputLabel || '资源'}元数据，仍有 ${risk.totalCount} 项部署前待补。`
            : '资源元数据已补齐，应用门禁已更新为 metadata-only 就绪。';
        this._setResultStatusNote(note, risk.hasWarnings ? 'info' : 'success');
        this._addMessage('system', note);
        return true;
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
        if (resourceKey && text === resourceKey) return true;

        const operatorId = String(
            resolution.operatorId ||
            normalizedResource?.operatorId ||
            normalizedResource?.actualOperatorId ||
            ''
        ).trim();
        const candidates = this._getResolvedParameterCandidates(normalizedResource, resolution);
        return [...candidates].some(parameterName => {
            const reference = operatorId ? `${operatorId}.${parameterName}`.toLowerCase() : parameterName;
            return text.toLowerCase() === reference;
        });
    },

    _refreshApplyGateAfterResourceResolution(target) {
        if (!target || typeof target !== 'object') return;

        const missing = this._normalizeMissingResources(target.missingResources ?? target.MissingResources);
        const pending = this._normalizePendingParameters(target.pendingParameters ?? target.PendingParameters);
        const hasBlockers = missing.length > 0 || pending.length > 0;
        const gate = target.applyGate || target.ApplyGate || null;

        if (gate && typeof gate === 'object') {
            if ('deploymentReady' in gate || !('DeploymentReady' in gate)) gate.deploymentReady = !hasBlockers;
            if ('DeploymentReady' in gate) gate.DeploymentReady = !hasBlockers;
            if ('blocked' in gate || !('Blocked' in gate)) gate.blocked = false;
            if ('Blocked' in gate) gate.Blocked = false;
            if ('status' in gate || !('Status' in gate)) gate.status = hasBlockers ? 'canvas_apply_ready' : 'deployment_metadata_ready';
            if ('Status' in gate) gate.Status = hasBlockers ? 'canvas_apply_ready' : 'deployment_metadata_ready';
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
    }
};
