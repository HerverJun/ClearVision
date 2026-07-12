import { AiWorkbenchStates } from './aiPanelWorkbench.js';
import {
    getOperatorTypeDisplayName,
    getParameterDisplayName
} from '../../shared/operatorDisplayNames.js';

export const aiPanelApplyPreviewMixin = {
    _handleApplyFlow() {
        if (!this.flowCanvas || this._disposed) return;
        if (this._applySafetyBlockReason) {
            this._setResultStatusNote?.('当前画布处于本地安全阻断状态，请先生成新结果或完成明确的安全恢复。', 'warning');
            this._announceAccessibilityStatus?.('当前无法应用，请先完成安全恢复。', 'assertive');
            return;
        }
        if (this._applyInFlight || this._activeApplyPreview) {
            this._announceAccessibilityStatus?.('应用操作正在处理中，请勿重复提交。');
            return;
        }
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

    _sanitizeApplyPreviewText(value, maxChars = 180) {
        const text = String(value ?? '').trim();
        if (!text) return '';
        return this._sanitizeAssistantFailureText?.(text, maxChars) ||
            this._redactPublicDiagnosticText?.(text)?.slice(0, maxChars) ||
            text.slice(0, maxChars);
    },

    _formatApplyPendingItem(item) {
        const operatorLabel = item.actualOperatorId || item.operatorId || '未定位算子';
        const names = item.parameterNames?.length > 0
            ? item.parameterNames.map(name => getParameterDisplayName(name, { fallback: name })).join('、')
            : '待确认参数';
        return this._sanitizeApplyPreviewText(`${operatorLabel}：${names}`, 220);
    },

    _formatApplyOperatorLabel(op) {
        const rawType = op?.operatorType || op?.OperatorType || op?.type || op?.Type || '';
        const label = op?.displayName || op?.DisplayName || op?.name || op?.Name ||
            getOperatorTypeDisplayName(rawType, { fallback: '未命名算子' });
        return this._sanitizeApplyPreviewText(label, 160);
    },

    _formatApplyChangeLabel(name) {
        const rawName = String(name || '').trim();
        if (rawName === 'displayName') return '显示名称';
        if (rawName === 'operatorType') return '算子类型';
        return this._sanitizeApplyPreviewText(getParameterDisplayName(rawName, { fallback: rawName || '参数' }), 120);
    },

    _formatApplyChangeValue(change, value) {
        if (change?.name === 'operatorType') {
            return this._sanitizeApplyPreviewText(getOperatorTypeDisplayName(value, { fallback: String(value ?? '--') }), 180);
        }
        return this._sanitizeApplyPreviewText(value ?? '--', 180);
    },

    _renderApplyRiskSummary(applyRisk) {
        if (!applyRisk?.hasWarnings) return '';

        const pendingItems = (applyRisk.pending || [])
            .slice(0, 4)
            .map(item => `<li>${this._escapeHtml(this._formatApplyPendingItem(item))}</li>`)
            .join('');
        const missingItems = (applyRisk.missing || [])
            .slice(0, 4)
            .map(item => `<li>${this._escapeHtml(this._sanitizeApplyPreviewText(item.description || item.resourceKey || item.resourceType || '缺失资源', 220))}</li>`)
            .join('');
        const nonBlockingItems = (applyRisk.nonBlockingFields || [])
            .slice(0, 6)
            .map(field => `<li>${this._escapeHtml(this._sanitizeApplyPreviewText(this._getRequirementFieldLabel(field), 160))}</li>`)
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

        return this._sanitizeApplyPreviewText(`${source}.${sourcePort} -> ${target}.${targetPort}`, 220);
    },

    _showApplyPreview(diff, newFlow, options = {}) {
        const totalChanges = this._getApplyPreviewChangeCount(diff);
        const applyRisk = options.applyRisk || this._buildApplyRiskSummary(this.currentResult);
        if (totalChanges === 0 && !applyRisk.hasWarnings) {
            this._executeApplyFlow(newFlow);
            return;
        }

        this._closeApplyPreview?.({ restoreFocus: false, setReady: false });
        const returnFocus = document.activeElement && this.container.contains?.(document.activeElement)
            ? document.activeElement
            : this.container.querySelector?.('#ai-btn-apply');
        const previewIdentity = this._createApplyPreviewIdentity(newFlow);

        const overlay = document.createElement('div');
        overlay.className = 'ai-apply-preview-overlay';
        overlay.innerHTML = `
            <div class="ai-apply-preview-dialog" role="dialog" aria-modal="true" aria-labelledby="ai-apply-preview-title" aria-describedby="ai-apply-preview-summary" tabindex="-1">
                <div class="ai-apply-preview-header">
                    <span id="ai-apply-preview-title">应用预览</span>
                    <small id="ai-apply-preview-summary">${this._escapeHtml(String(totalChanges))} 项变更 · ${this._escapeHtml(String(applyRisk.totalCount || 0))} 项待复核</small>
                    <button class="ai-apply-preview-close" type="button" aria-label="关闭应用预览">&times;</button>
                </div>
                <div class="ai-apply-preview-body">
                    ${this._renderApplyRiskSummary(applyRisk)}
                    ${diff.added.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-add">新增算子 (${diff.added.length})</div>
                            ${diff.added.map(op => `<div class="ai-apply-preview-item is-add" title="${this._escapeHtml(this._sanitizeApplyPreviewText(op.operatorType || op.OperatorType || op.type || op.Type || '', 120))}">+ ${this._escapeHtml(this._formatApplyOperatorLabel(op))}</div>`).join('')}
                        </div>
                    ` : ''}
                    ${diff.removed.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-remove">删除算子 (${diff.removed.length})</div>
                            ${diff.removed.map(op => `<div class="ai-apply-preview-item is-remove" title="${this._escapeHtml(this._sanitizeApplyPreviewText(op.operatorType || op.OperatorType || op.type || op.Type || '', 120))}">- ${this._escapeHtml(this._formatApplyOperatorLabel(op))}</div>`).join('')}
                        </div>
                    ` : ''}
                    ${diff.modified.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-modify">参数变更 (${diff.modified.length})</div>
                            ${diff.modified.map(m => `
                                <div class="ai-apply-preview-item is-modify">
                                    ${this._escapeHtml(this._formatApplyOperatorLabel(m.op))}
                                    ${m.changes.map(c => `<div class="ai-apply-preview-param" title="${this._escapeHtml(this._sanitizeApplyPreviewText(`${this._formatApplyChangeLabel(c.name)}: ${this._formatApplyChangeValue(c, c.old)} -> ${this._formatApplyChangeValue(c, c.new)}`, 260))}">${this._escapeHtml(this._formatApplyChangeLabel(c.name))}: ${this._escapeHtml(this._formatApplyChangeValue(c, c.old))} &rarr; ${this._escapeHtml(this._formatApplyChangeValue(c, c.new))}</div>`).join('')}
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
        const dialog = overlay.querySelector('.ai-apply-preview-dialog');
        const confirmButton = overlay.querySelector('.ai-apply-preview-confirm');
        const backgroundIsolation = this._isolateApplyDialogBackground(overlay);

        const cancelPreview = () => this._closeApplyPreview({ restoreFocus: true, setReady: true });
        const keyHandler = event => {
            if (event.key === 'Escape') {
                event.preventDefault();
                cancelPreview();
                return;
            }
            if (event.key !== 'Tab') return;
            const focusable = Array.from(dialog?.querySelectorAll?.('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])') || []);
            if (!focusable.length) {
                event.preventDefault();
                dialog?.focus?.();
                return;
            }
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        };
        overlay.addEventListener('keydown', keyHandler);
        this._activeApplyPreview = { overlay, dialog, returnFocus, keyHandler, identity: previewIdentity, flow: newFlow, backgroundIsolation };
        overlay.querySelector('.ai-apply-preview-close').addEventListener('click', cancelPreview);
        overlay.querySelector('.ai-apply-preview-cancel').addEventListener('click', cancelPreview);
        confirmButton.addEventListener('click', () => {
            if (this._applyInFlight) return;
            if (!this._isApplyPreviewIdentityCurrent(previewIdentity, newFlow)) {
                this._closeApplyPreview({ restoreFocus: true, setReady: false });
                this._setWorkbenchState?.(AiWorkbenchStates.FAILED);
                this._setResultStatusNote('预览打开后画布、AI 结果或 Apply 门禁已变化，旧预览已失效。请重新打开应用预览。', 'warning');
                this._announceAccessibilityStatus?.('应用预览已失效，请重新预览。', 'assertive');
                return;
            }
            this._closeApplyPreview({ restoreFocus: false, setReady: false });
            this._executeApplyFlow(newFlow);
        });
        window.requestAnimationFrame?.(() => confirmButton?.focus?.({ preventScroll: true }));
    },

    _createApplyPreviewIdentity(flow) {
        return Object.freeze({
            resultVersion: Number(this.currentResultVersion || 0),
            canvasRevision: Number(this.flowCanvas?.getFlowRevision?.() || this.currentCanvasRevision || 0),
            flowFingerprint: this._fingerprintApplyFlow(flow),
            applyGateFingerprint: this._fingerprintApplyFlow(this._getPayloadApplyGate?.(this.currentResult) || null)
        });
    },

    _fingerprintApplyFlow(flow) {
        try {
            return JSON.stringify(flow || null);
        } catch {
            return '';
        }
    },

    _isApplyPreviewIdentityCurrent(identity, flow) {
        if (!identity || this._disposed) return false;
        const currentRevision = Number(this.flowCanvas?.getFlowRevision?.() || this.currentCanvasRevision || 0);
        const currentFlow = this._buildFlowWithPendingDrafts?.(
            this._getResultFlowForCanvas?.(this.currentResult) || this.currentResult?.flow || this.currentResult?.Flow || flow
        ) || flow;
        return Number(identity.resultVersion) === Number(this.currentResultVersion || 0) &&
            Number(identity.canvasRevision) === currentRevision &&
            identity.flowFingerprint === this._fingerprintApplyFlow(currentFlow) &&
            identity.applyGateFingerprint === this._fingerprintApplyFlow(this._getPayloadApplyGate?.(this.currentResult) || null) &&
            !this._applySafetyBlockReason &&
            (this._isCanvasApplyReadyForResult?.(this.currentResult) ?? true);
    },

    _isolateApplyDialogBackground(overlay) {
        const records = [];
        let current = overlay;
        while (current?.parentElement) {
            const parent = current.parentElement;
            Array.from(parent.children || []).forEach(sibling => {
                if (sibling === current) return;
                records.push({
                    element: sibling,
                    inert: sibling.inert === true,
                    ariaHidden: sibling.getAttribute?.('aria-hidden')
                });
                sibling.inert = true;
                sibling.setAttribute?.('aria-hidden', 'true');
            });
            current = parent;
            if (parent === document.body) break;
        }
        return records;
    },

    _restoreApplyDialogBackground(records = []) {
        records.forEach(record => {
            const element = record?.element;
            if (!element) return;
            element.inert = record.inert === true;
            if (record.ariaHidden === null || record.ariaHidden === undefined) {
                element.removeAttribute?.('aria-hidden');
            } else {
                element.setAttribute?.('aria-hidden', record.ariaHidden);
            }
        });
    },

    _closeApplyPreview({ restoreFocus = true, setReady = true } = {}) {
        const preview = this._activeApplyPreview;
        const overlay = preview?.overlay || this.container?.querySelector?.('.ai-apply-preview-overlay');
        if (!overlay) {
            this._activeApplyPreview = null;
            return;
        }
        overlay.removeEventListener?.('keydown', preview?.keyHandler);
        overlay.remove?.();
        this._restoreApplyDialogBackground?.(preview?.backgroundIsolation || []);
        this._activeApplyPreview = null;
        if (setReady && !this._applyInFlight && !this._disposed && !this._applySafetyBlockReason &&
            (this._isCanvasApplyReadyForResult?.(this.currentResult) ?? true)) {
            this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
        }
        if (restoreFocus && !this._disposed) {
            const target = preview?.returnFocus?.isConnected ? preview.returnFocus : this.container?.querySelector?.('#ai-btn-apply');
            target?.focus?.({ preventScroll: true });
        }
    },

    _executeApplyFlow(flow) {
        if (!this.flowCanvas || this._disposed || this._applyInFlight || this._applySafetyBlockReason) return false;
        this._applyInFlight = true;
        this._updateApplyButtonState?.();
        const previousResultFlow = this.currentResult?.flow;
        const previousResultFlowPascal = this.currentResult?.Flow;
        let rollbackAttempted = false;
        let rollbackSucceeded = false;
        try {
            this._preApplySnapshot = this.flowCanvas.serialize();
            this._preApplySnapshotVersion += 1;
            this._preApplyCanvasRevision = this.flowCanvas?.getFlowRevision?.() || 0;

            const flowBtn = document.querySelector('.nav-btn[data-view="flow"]');
            if (flowBtn) flowBtn.click();
            this.flowCanvas.deserialize(flow);
            const appliedFlow = this.flowCanvas.serialize?.() || flow;
            const appliedOperators = this._extractOperators(appliedFlow);
            const expectedOperators = this._extractOperators(flow);
            const appliedConnections = this._extractConnections(appliedFlow);
            const expectedConnections = this._extractConnections(flow);
            if (appliedOperators.length === 0) {
                throw new Error('应用后的草稿没有算子。');
            }
            if (appliedOperators.length !== expectedOperators.length || appliedConnections.length !== expectedConnections.length) {
                throw new Error('画布只写入了部分流程内容。');
            }
            this.currentResult.flow = appliedFlow;
            this.currentResult.Flow = appliedFlow;
            this._captureAppliedCanvasBaseline?.(appliedFlow);
            this._markCurrentResultAppliedToCanvas();
            this._syncCanvasManualEditRecords?.(appliedFlow);
            this._syncPendingParameterDrafts(this.currentResult, appliedFlow, { force: true });
            this._renderFollowupChecklist(this.currentResult, appliedFlow);
            this._renderParameterDraftEditor(this.currentResult, appliedFlow);
            this.options.onApplied?.(appliedFlow);
            this.options.showToast?.('已应用到画布', 'success');
            this._applySafetyBlockReason = '';
            this._setWorkbenchState(AiWorkbenchStates.APPLIED);
            this.agentWorkspaceMode = this.agentWorkspaceMode || 'build';
            this._renderAgentWorkspaceOverview?.();
            this._renderBuildWorkspaceFromAgentRun?.();

            const applyRiskAfterApply = this._buildApplyRiskSummary(this.currentResult);
            const deploymentNote = applyRiskAfterApply.hasWarnings
                ? `已应用到画布，仍有 ${applyRiskAfterApply.totalCount} 项部署前待补齐或确认。`
                : '已应用到画布，请在部署前复核就绪状态。';
            this._setResultStatusNote(
                `${this._escapeHtml(deploymentNote)} 资源补齐在左侧集中完成；可在流程页点击算子进行细节复核与微调。流程页修改不会绕过部署门禁。 <button class="ai-undo-btn" id="ai-btn-undo">撤销应用</button>`,
                'success',
                true
            );
            const undoBtn = this.container.querySelector('#ai-btn-undo');
            if (undoBtn) {
                undoBtn.addEventListener('click', () => this._undoApply());
            }
            return true;
        } catch (err) {
            console.error('应用流程失败:', err);
            const message = this._sanitizeApplyPreviewText(err?.message || '未知错误', 260);
            if (this._preApplySnapshot) {
                rollbackAttempted = true;
                try {
                    this.flowCanvas.deserialize(this._preApplySnapshot);
                    this.flowCanvas.serialize?.();
                    rollbackSucceeded = true;
                    if (this.currentResult) {
                        this.currentResult.flow = previousResultFlow;
                        this.currentResult.Flow = previousResultFlowPascal;
                    }
                    this.appliedResultVersion = 0;
                    this.appliedCanvasBaselineFlow = null;
                    this.canvasManualEditRecords = [];
                    this.canvasManualEditSignature = '';
                    this.options.onCanvasChanged?.({
                        source: 'ai',
                        action: 'apply-rollback',
                        flow: this.flowCanvas.serialize?.() || this._preApplySnapshot
                    });
                } catch (rollbackError) {
                    console.error('应用失败后的画布回滚失败:', rollbackError);
                }
            }
            if (rollbackAttempted && rollbackSucceeded) {
                this._applySafetyBlockReason = '';
                this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
                this._setResultStatusNote(`应用流程失败，画布已恢复到应用前状态：${message}`, 'warning');
                this._addMessage('system', `应用流程失败，已恢复应用前画布：${message}`);
            } else {
                this._applySafetyBlockReason = 'apply_rollback_failed';
                this._setWorkbenchState(AiWorkbenchStates.FAILED);
                const suffix = rollbackAttempted ? '；自动回滚也失败，请先检查或恢复画布后再继续。' : '；未取得可用的应用前快照。';
                this._setResultStatusNote(`应用流程失败${suffix} ${message}`, 'warning');
                this._addMessage('system', `应用流程失败${suffix} ${message}`);
                this._announceAccessibilityStatus?.('应用失败且未能安全恢复画布。', 'assertive');
            }
            return false;
        } finally {
            this._applyInFlight = false;
            this._updateApplyButtonState?.();
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
            this.appliedCanvasBaselineFlow = null;
            this.canvasManualEditRecords = [];
            this.canvasManualEditSignature = '';
            this._applySafetyBlockReason = '';
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
            this._addMessage('system', `撤销失败：${this._sanitizeApplyPreviewText(err?.message || '未知错误', 260)}`);
        }
    }
};
