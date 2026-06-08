import { AiWorkbenchStates } from './aiPanelWorkbench.js';

export const aiPanelApplyPreviewMixin = {
    _handleApplyFlow() {
        if (!this.flowCanvas) return;
        const baseFlow = this._getResultFlowForCanvas?.(this.currentResult) ||
            this.currentResult?.flow ||
            this.currentResult?.Flow ||
            null;
        if (baseFlow && this.currentResult) {
            this.currentResult.flow = baseFlow;
            this.currentResult.Flow = baseFlow;
            this._hydrateCurrentResultFollowupsFromBuildResult();
        }
        if (baseFlow && !(this._isCanvasApplyReadyForResult?.(this.currentResult) ?? true)) {
            this._addMessage('system', '当前应用门禁阻止应用到画布，请先复核公开阻断项。');
            return;
        }
        if (!this.currentResult?.flow) {
            this._addMessage('system', '当前会话没有可应用到画布的流程草稿。');
            return;
        }

        const flow = this._buildFlowWithPendingDrafts(this.currentResult.flow);
        if (!flow) {
            this._addMessage('system', '当前会话没有可应用到画布的流程草稿。');
            return;
        }

        this._setWorkbenchState(AiWorkbenchStates.APPLYING);
        const applyRisk = this._buildApplyRiskSummary(this.currentResult);

        let currentFlow = null;
        try {
            currentFlow = this.flowCanvas.serialize();
        } catch {
            // 画布可能为空，直接进入应用预览或应用。
        }

        if (currentFlow) {
            const diff = this._computeFlowDiff(currentFlow, flow);
            const totalChanges = this._getApplyPreviewChangeCount(diff);
            if (totalChanges > 0 || applyRisk.hasWarnings) {
                this._showApplyPreview(diff, flow, { applyRisk });
                return;
            }
        }

        if (applyRisk.hasWarnings) {
            this._showApplyPreview(this._emptyFlowDiff(), flow, { applyRisk });
            return;
        }

        this._executeApplyFlow(flow);
    },

    _computeFlowDiff(currentFlow, newFlow) {
        const currentOps = this._extractOperators(currentFlow);
        const newOps = this._extractOperators(newFlow);
        const currentConns = this._extractConnections(currentFlow);
        const newConns = this._extractConnections(newFlow);

        const opDiffKey = (op, index) => {
            const id = op.tempId || op.TempId || op.id || op.Id || '';
            if (id) return `id:${id}`;
            return `idx:${index}::type:${op.operatorType || op.OperatorType || op.type || op.Type || ''}`;
        };

        const currentOpMap = new Map();
        currentOps.forEach((op, index) => { currentOpMap.set(opDiffKey(op, index), op); });

        const newOpMap = new Map();
        newOps.forEach((op, index) => { newOpMap.set(opDiffKey(op, index), op); });

        const added = [];
        const removed = [];
        const modified = [];

        for (const [key, newOp] of newOpMap) {
            if (!currentOpMap.has(key)) {
                added.push(newOp);
            } else {
                const currentOp = currentOpMap.get(key);
                const currentParams = currentOp.parameters || currentOp.Parameters || {};
                const newParams = newOp.parameters || newOp.Parameters || {};
                const paramChanges = [];
                const currentDisplayName = currentOp.displayName || currentOp.DisplayName || currentOp.name || currentOp.Name || '';
                const newDisplayName = newOp.displayName || newOp.DisplayName || newOp.name || newOp.Name || '';
                const currentOperatorType = currentOp.operatorType || currentOp.OperatorType || currentOp.type || currentOp.Type || '';
                const newOperatorType = newOp.operatorType || newOp.OperatorType || newOp.type || newOp.Type || '';
                if (String(currentDisplayName) !== String(newDisplayName)) {
                    paramChanges.push({ name: 'displayName', old: currentDisplayName, new: newDisplayName });
                }
                if (String(currentOperatorType) !== String(newOperatorType)) {
                    paramChanges.push({ name: 'operatorType', old: currentOperatorType, new: newOperatorType });
                }
                const parameterNames = new Set([
                    ...Object.keys(currentParams),
                    ...Object.keys(newParams)
                ]);
                for (const pName of parameterNames) {
                    if (String(currentParams[pName] ?? '') !== String(newParams[pName] ?? '')) {
                        paramChanges.push({ name: pName, old: currentParams[pName], new: newParams[pName] });
                    }
                }
                if (paramChanges.length > 0) {
                    modified.push({ op: newOp, changes: paramChanges });
                }
            }
        }

        for (const [key, currentOp] of currentOpMap) {
            if (!newOpMap.has(key)) {
                removed.push(currentOp);
            }
        }

        const readConnEndpoint = (c, role) => {
            const prefix = role === 'source' ? 'source' : 'target';
            const pascalPrefix = role === 'source' ? 'Source' : 'Target';
            const operatorId = c[`${prefix}TempId`]
                || c[`${pascalPrefix}TempId`]
                || c[`${prefix}OperatorId`]
                || c[`${pascalPrefix}OperatorId`]
                || c[`${prefix}Id`]
                || c[`${pascalPrefix}Id`]
                || '';
            const portId = c[`${prefix}PortName`]
                || c[`${pascalPrefix}PortName`]
                || c[`${prefix}PortId`]
                || c[`${pascalPrefix}PortId`]
                || c[`${prefix}Port`]
                || c[`${pascalPrefix}Port`]
                || '';
            return `${operatorId}.${portId}`;
        };
        const connKey = c => `${readConnEndpoint(c, 'source')}::${readConnEndpoint(c, 'target')}`;
        const currentConnSet = new Set(currentConns.map(connKey));
        const newConnSet = new Set(newConns.map(connKey));
        const addedConnections = newConns.filter(c => !currentConnSet.has(connKey(c)));
        const removedConnections = currentConns.filter(c => !newConnSet.has(connKey(c)));

        return { added, removed, modified, addedConnections, removedConnections };
    },

    _emptyFlowDiff() {
        return {
            added: [],
            removed: [],
            modified: [],
            addedConnections: [],
            removedConnections: []
        };
    },

    _getApplyPreviewChangeCount(diff = {}) {
        return (diff.added?.length || 0)
            + (diff.removed?.length || 0)
            + (diff.modified?.length || 0)
            + (diff.addedConnections?.length || 0)
            + (diff.removedConnections?.length || 0);
    },

    _buildApplyRiskSummary(result = this.currentResult) {
        const buildResult = this._getPayloadBuildResult?.(result) || null;
        const pendingSource = (this._toArray?.(result?.pendingParameters ?? result?.PendingParameters) || []).length
            ? (result?.pendingParameters ?? result?.PendingParameters)
            : (buildResult?.pendingParameters ?? buildResult?.PendingParameters);
        const missingSource = (this._toArray?.(result?.missingResources ?? result?.MissingResources) || []).length
            ? (result?.missingResources ?? result?.MissingResources)
            : (buildResult?.missingResources ?? buildResult?.MissingResources);
        const pending = this._resolvePendingParametersForDraft({
            ...(result || {}),
            pendingParameters: pendingSource || [],
            missingResources: missingSource || []
        });
        const missing = this._normalizeMissingResources(missingSource);
        const brief = this._normalizeRequirementBrief(result?.requirementBrief ?? result?.RequirementBrief ?? null);
        const nonBlockingFields = this._normalizeRuntimeFieldList(
            result?.nonBlockingMissingFields
            ?? result?.NonBlockingMissingFields
            ?? brief?.nonBlockingMissingFields
            ?? []
        );

        return {
            pending,
            missing,
            nonBlockingFields,
            hasWarnings: pending.length > 0 || missing.length > 0 || nonBlockingFields.length > 0,
            totalCount: pending.length + missing.length + nonBlockingFields.length
        };
    },

    _hydrateCurrentResultFollowupsFromBuildResult() {
        if (!this.currentResult) return;
        const buildResult = this._getPayloadBuildResult?.(this.currentResult) || null;
        if (!buildResult) return;

        if (!(this._toArray?.(this.currentResult.pendingParameters ?? this.currentResult.PendingParameters) || []).length) {
            const pending = this._toArray?.(buildResult.pendingParameters ?? buildResult.PendingParameters) || [];
            this.currentResult.pendingParameters = pending;
            this.currentResult.PendingParameters = pending;
        }

        if (!(this._toArray?.(this.currentResult.missingResources ?? this.currentResult.MissingResources) || []).length) {
            const missing = this._toArray?.(buildResult.missingResources ?? buildResult.MissingResources) || [];
            this.currentResult.missingResources = missing;
            this.currentResult.MissingResources = missing;
        }
    },

    _formatApplyPendingItem(item) {
        const operatorLabel = item.actualOperatorId || item.operatorId || '未定位算子';
        const names = item.parameterNames?.length > 0 ? item.parameterNames.join('、') : '待确认参数';
        return `${operatorLabel}：${names}`;
    },

    _renderApplyRiskSummary(applyRisk) {
        if (!applyRisk?.hasWarnings) return '';

        const pendingItems = (applyRisk.pending || [])
            .slice(0, 4)
            .map(item => `<li>${this._escapeHtml(this._formatApplyPendingItem(item))}</li>`)
            .join('');
        const missingItems = (applyRisk.missing || [])
            .slice(0, 4)
            .map(item => `<li>${this._escapeHtml(item.description || item.resourceKey || item.resourceType || '缺失资源')}</li>`)
            .join('');
        const nonBlockingItems = (applyRisk.nonBlockingFields || [])
            .slice(0, 6)
            .map(field => `<li>${this._escapeHtml(this._getRequirementFieldLabel(field))}</li>`)
            .join('');

        return `
            <section class="ai-apply-preview-risk">
                <div class="ai-apply-preview-risk-title">应用前检查</div>
                <div class="ai-apply-preview-risk-copy">
                    当前方案仍有 ${this._escapeHtml(String(applyRisk.totalCount))} 项部署前信息需要复核。可以先应用到画布继续编辑，但运行前应补齐。
                </div>
                ${pendingItems ? `
                    <div class="ai-apply-preview-risk-group">
                        <div class="ai-apply-preview-risk-label">待确认参数</div>
                        <ul>${pendingItems}</ul>
                    </div>
                ` : ''}
                ${missingItems ? `
                    <div class="ai-apply-preview-risk-group">
                        <div class="ai-apply-preview-risk-label">缺失资源</div>
                        <ul>${missingItems}</ul>
                    </div>
                ` : ''}
                ${nonBlockingItems ? `
                    <div class="ai-apply-preview-risk-group">
                        <div class="ai-apply-preview-risk-label">非阻断待补</div>
                        <ul>${nonBlockingItems}</ul>
                    </div>
                ` : ''}
            </section>
        `;
    },

    _formatConnectionPreview(connection) {
        if (!connection) return '未知连线';

        const source = connection.sourceTempId
            || connection.SourceTempId
            || connection.sourceOperatorId
            || connection.SourceOperatorId
            || connection.sourceId
            || connection.SourceId
            || '?';
        const sourcePort = connection.sourcePortName
            || connection.SourcePortName
            || connection.sourcePortId
            || connection.SourcePortId
            || connection.sourcePort
            || connection.SourcePort
            || 'Output';
        const target = connection.targetTempId
            || connection.TargetTempId
            || connection.targetOperatorId
            || connection.TargetOperatorId
            || connection.targetId
            || connection.TargetId
            || '?';
        const targetPort = connection.targetPortName
            || connection.TargetPortName
            || connection.targetPortId
            || connection.TargetPortId
            || connection.targetPort
            || connection.TargetPort
            || 'Input';

        return `${source}.${sourcePort} -> ${target}.${targetPort}`;
    },

    _showApplyPreview(diff, newFlow, options = {}) {
        const totalChanges = this._getApplyPreviewChangeCount(diff);
        const applyRisk = options.applyRisk || this._buildApplyRiskSummary(this.currentResult);
        if (totalChanges === 0 && !applyRisk.hasWarnings) {
            this._executeApplyFlow(newFlow);
            return;
        }

        const existing = this.container.querySelector('.ai-apply-preview-overlay');
        if (existing) existing.remove();

        const overlay = document.createElement('div');
        overlay.className = 'ai-apply-preview-overlay';
        overlay.innerHTML = `
            <div class="ai-apply-preview-dialog">
                <div class="ai-apply-preview-header">
                    <span>应用预览</span>
                    <small>${this._escapeHtml(String(totalChanges))} 项变更 · ${this._escapeHtml(String(applyRisk.totalCount || 0))} 项待复核</small>
                    <button class="ai-apply-preview-close" type="button">&times;</button>
                </div>
                <div class="ai-apply-preview-body">
                    ${this._renderApplyRiskSummary(applyRisk)}
                    ${diff.added.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-add">新增算子 (${diff.added.length})</div>
                            ${diff.added.map(op => `<div class="ai-apply-preview-item is-add">+ ${this._escapeHtml(op.displayName || op.DisplayName || op.name || '未命名')}</div>`).join('')}
                        </div>
                    ` : ''}
                    ${diff.removed.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-remove">删除算子 (${diff.removed.length})</div>
                            ${diff.removed.map(op => `<div class="ai-apply-preview-item is-remove">- ${this._escapeHtml(op.displayName || op.DisplayName || op.name || '未命名')}</div>`).join('')}
                        </div>
                    ` : ''}
                    ${diff.modified.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-modify">参数变更 (${diff.modified.length})</div>
                            ${diff.modified.map(m => `
                                <div class="ai-apply-preview-item is-modify">
                                    ${this._escapeHtml(m.op.displayName || m.op.DisplayName || m.op.name || '未命名')}
                                    ${m.changes.map(c => `<div class="ai-apply-preview-param">${this._escapeHtml(c.name)}: ${this._escapeHtml(String(c.old ?? '--'))} &rarr; ${this._escapeHtml(String(c.new ?? '--'))}</div>`).join('')}
                                </div>
                            `).join('')}
                        </div>
                    ` : ''}
                    ${diff.addedConnections.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-add">新增连线 (${diff.addedConnections.length})</div>
                            ${diff.addedConnections.slice(0, 6).map(conn => `<div class="ai-apply-preview-item is-add">+ ${this._escapeHtml(this._formatConnectionPreview(conn))}</div>`).join('')}
                        </div>
                    ` : ''}
                    ${diff.removedConnections.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-remove">删除连线 (${diff.removedConnections.length})</div>
                            ${diff.removedConnections.slice(0, 6).map(conn => `<div class="ai-apply-preview-item is-remove">- ${this._escapeHtml(this._formatConnectionPreview(conn))}</div>`).join('')}
                        </div>
                    ` : ''}
                </div>
                <div class="ai-apply-preview-actions">
                    <button class="ai-apply-preview-cancel" type="button">取消</button>
                    <button class="ai-apply-preview-confirm" type="button">确认应用到画布</button>
                </div>
            </div>
        `;

        this.container.appendChild(overlay);

        const cancelPreview = () => {
            overlay.remove();
            this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
        };
        overlay.querySelector('.ai-apply-preview-close').addEventListener('click', cancelPreview);
        overlay.querySelector('.ai-apply-preview-cancel').addEventListener('click', cancelPreview);
        overlay.querySelector('.ai-apply-preview-confirm').addEventListener('click', () => {
            overlay.remove();
            this._executeApplyFlow(newFlow);
        });
    },

    _executeApplyFlow(flow) {
        if (!this.flowCanvas) return;
        try {
            this._preApplySnapshot = this.flowCanvas.serialize();
            this._preApplySnapshotVersion += 1;
            this._preApplyCanvasRevision = this.flowCanvas?.getFlowRevision?.() || 0;

            const flowBtn = document.querySelector('.nav-btn[data-view="flow"]');
            if (flowBtn) flowBtn.click();
            this.flowCanvas.deserialize(flow);
            const appliedFlow = this.flowCanvas.serialize?.() || flow;
            const appliedOperators = this._extractOperators(appliedFlow);
            if (appliedOperators.length === 0) {
                throw new Error('应用后的草稿没有算子。');
            }
            this.currentResult.flow = appliedFlow;
            this.currentResult.Flow = appliedFlow;
            this._markCurrentResultAppliedToCanvas();
            this._syncPendingParameterDrafts(this.currentResult, appliedFlow, { force: true });
            this._renderFollowupChecklist(this.currentResult, appliedFlow);
            this._renderParameterDraftEditor(this.currentResult, appliedFlow);
            this.options.onApplied?.(appliedFlow);
            this.options.showToast?.('已应用到画布', 'success');
            this._setWorkbenchState(AiWorkbenchStates.APPLIED);

            const applyRiskAfterApply = this._buildApplyRiskSummary(this.currentResult);
            const deploymentNote = applyRiskAfterApply.hasWarnings
                ? `已应用到画布，仍有 ${applyRiskAfterApply.totalCount} 项部署前待绑定或确认。`
                : '已应用到画布，请在部署前复核就绪状态。';
            this._setResultStatusNote(
                `${this._escapeHtml(deploymentNote)} <button class="ai-undo-btn" id="ai-btn-undo">撤销应用</button>`,
                'success',
                true
            );
            const undoBtn = this.container.querySelector('#ai-btn-undo');
            if (undoBtn) {
                undoBtn.addEventListener('click', () => this._undoApply());
            }
        } catch (err) {
            console.error('应用流程失败:', err);
            const message = err?.message || '未知错误';
            this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
            this._setResultStatusNote(`应用流程失败：${message}`, 'warning');
            this._addMessage('system', `应用流程失败：${message}`);
        }
    },

    _undoApply() {
        if (!this._preApplySnapshot || !this.flowCanvas) {
            this._addMessage('system', '没有可撤销的应用记录。');
            return;
        }

        const currentRevision = this.flowCanvas?.getFlowRevision?.() || 0;
        const revisionAtApply = this._preApplyCanvasRevision || 0;
        if (currentRevision > revisionAtApply + 1) {
            const confirmed = window.confirm('画布在应用后已被手动修改，撤销会覆盖这些修改。确定继续吗？');
            if (!confirmed) return;
        }

        try {
            this.flowCanvas.deserialize(this._preApplySnapshot);
            this.appliedResultVersion = 0;
            this.appliedCanvasRevision = this.flowCanvas?.getFlowRevision?.() || 0;
            this._preApplySnapshot = null;
            this._preApplySnapshotVersion = 0;
            this._preApplyCanvasRevision = 0;
            this._updateApplyButtonState();
            this.options.onCanvasChanged?.({
                source: 'ai',
                action: 'undo-apply',
                flow: this.flowCanvas.serialize?.() || null
            });
            this._setResultStatusNote('已撤销上一次应用。', 'info');
            this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
            this._addMessage('system', '已撤销应用，画布已恢复到应用前状态。');
        } catch (err) {
            console.error('撤销应用失败:', err);
            this._addMessage('system', `撤销失败：${err.message}`);
        }
    }
};
