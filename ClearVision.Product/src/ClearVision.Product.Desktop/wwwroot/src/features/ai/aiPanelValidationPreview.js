
import { renderRuntimePreviewSection } from './aiPanelRuntimePreview.js';
import { normalizeAgentToolTrace, renderAgentToolTrace } from './aiPanelToolTrace.js';

export const aiPanelValidationPreviewMixin = {
    _getObjectValue(source, names) {
        if (!source || typeof source !== 'object') {
            return undefined;
        }

        for (const name of names) {
            if (Object.prototype.hasOwnProperty.call(source, name)) {
                return source[name];
            }
        }

        const keys = Object.keys(source);
        const matched = keys.find(key => names.some(name => key.toLowerCase() === String(name).toLowerCase()));
        return matched ? source[matched] : undefined;
    },

    _getArrayValue(source, names) {
        const value = this._getObjectValue(source, names);
        return Array.isArray(value) ? value : [];
    },

    _getBooleanValue(source, names) {
        const value = this._getObjectValue(source, names);
        if (typeof value === 'boolean') {
            return value;
        }
        if (typeof value === 'string') {
            const normalized = value.trim().toLowerCase();
            if (normalized === 'true') return true;
            if (normalized === 'false') return false;
        }
        return null;
    },

    _normalizeAgentValidationPreview(data) {
        const preview = data?.validationPreview ?? data?.ValidationPreview ?? null;
        if (!preview || typeof preview !== 'object') {
            return null;
        }

        return {
            structuralValidation: this._getObjectValue(preview, ['structuralValidation', 'StructuralValidation']),
            dryRun: this._getObjectValue(preview, ['dryRun', 'DryRun']),
            deploymentPrecheck: this._getObjectValue(preview, ['deploymentPrecheck', 'DeploymentPrecheck']),
            runtimePreview: this._getObjectValue(preview, ['runtimePreview', 'RuntimePreview'])
        };
    },

    _deriveAgentDeploymentUiState(data) {
        const preview = data?.validationPreview ?? data?.ValidationPreview ?? data ?? null;
        const precheck = this._getObjectValue(preview, ['deploymentPrecheck', 'DeploymentPrecheck']) || preview;
        const readyValue = this._getBooleanValue(precheck, ['readyForDeployment', 'ReadyForDeployment']);
        const draftAllowedValue = this._getBooleanValue(precheck, ['workflowDraftAllowed', 'WorkflowDraftAllowed']);
        const deploymentBlockedValue = this._getBooleanValue(precheck, ['deploymentBlocked', 'DeploymentBlocked']);
        const deploymentActionsDisabled = readyValue === false || deploymentBlockedValue === true;
        const workflowEditingAllowed = draftAllowedValue !== false;

        return {
            readyForDeployment: readyValue === true,
            workflowDraftAllowed: workflowEditingAllowed,
            deploymentBlocked: deploymentActionsDisabled,
            deploymentActionsDisabled,
            workflowEditingAllowed
        };
    },

    _normalizeAgentPendingActions(items) {
        if (!Array.isArray(items)) return [];

        return items
            .map(item => {
                if (typeof item === 'string') {
                    return { actionType: 'pending', summary: item, resourceKey: '', operatorId: '', parameterName: '' };
                }

                return {
                    actionType: String(item?.actionType ?? item?.ActionType ?? item?.type ?? item?.Type ?? 'pending').trim(),
                    summary: String(item?.summary ?? item?.Summary ?? item?.message ?? item?.Message ?? item?.description ?? item?.Description ?? '').trim(),
                    resourceKey: String(item?.resourceKey ?? item?.ResourceKey ?? '').trim(),
                    operatorId: String(item?.operatorId ?? item?.OperatorId ?? item?.tempId ?? item?.TempId ?? '').trim(),
                    parameterName: String(item?.parameterName ?? item?.ParameterName ?? '').trim()
                };
            })
            .filter(item => item.actionType || item.summary || item.resourceKey || item.operatorId || item.parameterName);
    },

    _normalizeAgentToolTrace(items) {
        return normalizeAgentToolTrace(items);
    },

    _renderAgentPreviewIssueList(items, type) {
        const normalized = (Array.isArray(items) ? items : [])
            .slice(0, 4)
            .map(item => {
                if (typeof item === 'string') {
                    return { message: item, code: '', operatorId: '' };
                }
                return {
                    message: String(item?.message ?? item?.Message ?? item?.summary ?? item?.Summary ?? item?.description ?? item?.Description ?? item?.resourceKey ?? item?.ResourceKey ?? '').trim(),
                    code: String(item?.code ?? item?.Code ?? item?.resourceType ?? item?.ResourceType ?? '').trim(),
                    operatorId: String(item?.operatorId ?? item?.OperatorId ?? item?.tempId ?? item?.TempId ?? '').trim()
                };
            })
            .filter(item => item.message || item.code || item.operatorId);

        if (normalized.length === 0) {
            return '';
        }

        const cls = type === 'blocking' ? 'is-error' : 'is-warning';
        const icon = type === 'blocking' ? '&#10007;' : '&#9888;';
        return normalized.map(item => `
            <div class="ai-validation-issue ${cls}">
                <span class="ai-validation-issue-icon">${icon}</span>
                <div class="ai-validation-issue-body">
                    <div class="ai-validation-issue-msg">${this._escapeHtml(item.message || item.code || '待处理项')}</div>
                    ${(item.code || item.operatorId) ? `<div class="ai-validation-issue-meta">${this._escapeHtml([item.code, item.operatorId].filter(Boolean).join(' · '))}</div>` : ''}
                </div>
            </div>
        `).join('');
    },

    _renderAgentValidationPreviewSection(sectionKey, title, section) {
        if (!section || typeof section !== 'object') {
            return '';
        }

        const blocking = this._getArrayValue(section, ['blockingIssues', 'BlockingIssues']);
        const warnings = this._getArrayValue(section, ['warnings', 'Warnings']);
        const missing = this._getArrayValue(section, ['missingResources', 'MissingResources']);
        const executed = this._getArrayValue(section, ['executedOperators', 'ExecutedOperators']);
        const skipped = this._getArrayValue(section, ['skippedOperators', 'SkippedOperators']);
        const artifacts = this._getArrayValue(section, ['artifacts', 'Artifacts']);
        const summary = String(this._getObjectValue(section, ['summary', 'Summary', 'precheckSummary', 'PrecheckSummary', 'dryRunSummary', 'DryRunSummary']) ?? '').trim();
        const readyForDeployment = this._getBooleanValue(section, ['readyForDeployment', 'ReadyForDeployment']);
        const workflowDraftAllowed = this._getBooleanValue(section, ['workflowDraftAllowed', 'WorkflowDraftAllowed']);
        const previewReady = this._getBooleanValue(section, ['previewReady', 'PreviewReady']);
        const deployed = this._getBooleanValue(section, ['deployed', 'Deployed']);
        const packageCreated = this._getBooleanValue(section, ['packageCreated', 'PackageCreated']);
        const stationTouched = this._getBooleanValue(section, ['stationTouched', 'StationTouched']);
        const adapterName = String(this._getObjectValue(section, ['adapterName', 'AdapterName']) ?? '').trim();
        const previewMode = String(this._getObjectValue(section, ['previewMode', 'PreviewMode']) ?? '').trim();
        const countLabel = [
            blocking.length ? `${blocking.length} blocking` : '',
            warnings.length ? `${warnings.length} warning` : '',
            missing.length ? `${missing.length} missing` : '',
            executed.length ? `${executed.length} executed` : '',
            skipped.length ? `${skipped.length} skipped` : '',
            artifacts.length ? `${artifacts.length} artifact` : ''
        ].filter(Boolean).join(' · ') || 'no issues';

        return `
            <details class="ai-agent-preview-section" data-validation-preview-section="${this._escapeHtml(sectionKey)}">
                <summary class="ai-agent-preview-header">
                    <span>${this._escapeHtml(title)}</span>
                    <small>${this._escapeHtml(countLabel)}</small>
                </summary>
                <div class="ai-agent-preview-body">
                ${summary ? `<div class="ai-agent-preview-summary">${this._escapeHtml(summary)}</div>` : ''}
                ${(readyForDeployment !== null || workflowDraftAllowed !== null || previewReady !== null || adapterName || previewMode) ? `
                    <div class="ai-agent-preview-state-row">
                        ${readyForDeployment !== null ? `<span>readyForDeployment=${readyForDeployment ? 'true' : 'false'}</span>` : ''}
                        ${workflowDraftAllowed !== null ? `<span>workflowDraftAllowed=${workflowDraftAllowed ? 'true' : 'false'}</span>` : ''}
                        ${previewReady !== null ? `<span>previewReady=${previewReady ? 'true' : 'false'}</span>` : ''}
                        ${adapterName ? `<span>adapterName=${this._escapeHtml(adapterName)}</span>` : ''}
                        ${previewMode ? `<span>previewMode=${this._escapeHtml(previewMode)}</span>` : ''}
                    </div>
                ` : ''}
                ${(deployed !== null || packageCreated !== null || stationTouched !== null) ? `
                    <div class="ai-agent-preview-state-row">
                        ${deployed !== null ? `<span>deployed=${deployed ? 'true' : 'false'}</span>` : ''}
                        ${packageCreated !== null ? `<span>packageCreated=${packageCreated ? 'true' : 'false'}</span>` : ''}
                        ${stationTouched !== null ? `<span>stationTouched=${stationTouched ? 'true' : 'false'}</span>` : ''}
                    </div>
                ` : ''}
                ${this._renderAgentPreviewIssueList(blocking, 'blocking')}
                ${this._renderAgentPreviewIssueList(warnings, 'warning')}
                ${this._renderAgentPreviewIssueList(missing, 'warning')}
                ${artifacts.length > 0 ? `
                    <div class="ai-agent-artifact-list">
                        ${artifacts.slice(0, 6).map(item => `
                            <div class="ai-agent-artifact-row">
                                <span>${this._escapeHtml(String(item?.artifactType ?? item?.ArtifactType ?? 'artifact'))}</span>
                                <small>${this._escapeHtml([
                                    item?.artifactId ?? item?.ArtifactId ?? '',
                                    `metadataOnly=${item?.metadataOnly ?? item?.MetadataOnly ?? true}`,
                                    `binaryIncluded=${item?.binaryIncluded ?? item?.BinaryIncluded ?? false}`
                                ].filter(Boolean).join(' · '))}</small>
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
                </div>
            </details>
        `;
    },

    _renderAgentValidationArtifacts(data) {
        const preview = this._normalizeAgentValidationPreview(data);
        const pendingActions = this._normalizeAgentPendingActions(data?.pendingActions ?? data?.PendingActions);
        const missingResources = this._normalizeMissingResources(data?.missingResources ?? data?.MissingResources);
        const toolTrace = this._normalizeAgentToolTrace(data?.toolTrace ?? data?.ToolTrace);
        const hasPreview = Boolean(preview?.structuralValidation || preview?.dryRun || preview?.deploymentPrecheck || preview?.runtimePreview);
        const hasContent = hasPreview || pendingActions.length > 0 || missingResources.length > 0 || toolTrace.length > 0;
        if (!hasContent) {
            return '';
        }

        const deploymentState = this._deriveAgentDeploymentUiState(data);

        return `
            <div class="ai-agent-validation-artifacts">
                <div
                    class="ai-agent-deployment-state"
                    data-agent-deployment-disabled="${deploymentState.deploymentActionsDisabled ? 'true' : 'false'}"
                    data-agent-workflow-edit-enabled="${deploymentState.workflowEditingAllowed ? 'true' : 'false'}"
                >
                    <span>deploymentActions=${deploymentState.deploymentActionsDisabled ? 'disabled' : 'enabled'}</span>
                    <span>workflowEditing=${deploymentState.workflowEditingAllowed ? 'enabled' : 'disabled'}</span>
                </div>
                ${this._renderAgentValidationPreviewSection('structuralValidation', 'structuralValidation', preview?.structuralValidation)}
                ${this._renderAgentValidationPreviewSection('dryRun', 'dryRun', preview?.dryRun)}
                ${this._renderAgentValidationPreviewSection('deploymentPrecheck', 'deploymentPrecheck', preview?.deploymentPrecheck)}
                ${renderRuntimePreviewSection(this, preview?.runtimePreview)}
                ${missingResources.length > 0 ? `
                    <div class="ai-agent-preview-section" data-agent-artifact="missingResources">
                        <div class="ai-agent-preview-header"><span>missingResources</span><small>${missingResources.length}</small></div>
                        ${missingResources.slice(0, 6).map(item => `
                            <div class="ai-agent-artifact-row">
                                <span>${this._escapeHtml(item.resourceKey || item.parameterName || item.resourceType || 'missing')}</span>
                                <small>${this._escapeHtml(item.description || item.operatorId || '')}</small>
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
                ${pendingActions.length > 0 ? `
                    <div class="ai-agent-preview-section" data-agent-artifact="pendingActions">
                        <div class="ai-agent-preview-header"><span>pendingActions</span><small>${pendingActions.length}</small></div>
                        ${pendingActions.slice(0, 6).map(item => `
                            <div class="ai-agent-artifact-row">
                                <span>${this._escapeHtml(item.actionType || 'pending')}</span>
                                <small>${this._escapeHtml(item.summary || item.resourceKey || [item.operatorId, item.parameterName].filter(Boolean).join('.'))}</small>
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
                ${renderAgentToolTrace(this, toolTrace)}
            </div>
        `;
    },

    _renderValidationConsole(data) {
        const card = this.container?.querySelector('#ai-result-validation-card');
        const container = this.container?.querySelector('#ai-result-validation');
        if (!card || !container) return;

        const diagnostics = data?.lastAttemptDiagnostics || data?.LastAttemptDiagnostics || [];
        const manualRetry = data?.manualRetry || data?.ManualRetry || null;
        const dryRun = data?.dryRunResult || data?.DryRunResult || null;
        const knowledgeDiags = data?.knowledgeDiagnostics || data?.KnowledgeDiagnostics || [];
        const agentArtifactsHtml = this._renderAgentValidationArtifacts(data);
        const hasContent = diagnostics.length > 0 || manualRetry?.required || dryRun || knowledgeDiags.length > 0 || agentArtifactsHtml;

        if (!hasContent) {
            card.hidden = true;
            container.innerHTML = '';
            return;
        }

        card.hidden = false;
        const sections = [];
        if (agentArtifactsHtml) {
            sections.push(agentArtifactsHtml);
        }

        // ManualRetry banner
        if (manualRetry?.required) {
            sections.push(`
                <div class="ai-validation-retry-banner">
                    <div class="ai-validation-retry-title">需要手动确认</div>
                    <div class="ai-validation-retry-summary">${this._escapeHtml(manualRetry.summary || manualRetry.repairTarget || '')}</div>
                    <div class="ai-validation-retry-stage">失败阶段：${this._escapeHtml(manualRetry.stage || '未知')}</div>
                </div>
            `);
        }

        // Diagnostics list
        if (diagnostics.length > 0) {
            const issueItems = diagnostics.flatMap(d => {
                const issues = d.issues || d.Issues || [];
                return issues.map(issue => ({
                    severity: issue.severity || issue.Severity || 'error',
                    category: issue.category || issue.Category || '',
                    code: issue.code || issue.Code || '',
                    message: issue.message || issue.Message || '',
                    repairHint: issue.repairHint || issue.RepairHint || '',
                    operatorId: issue.operatorId || issue.OperatorId || ''
                }));
            });

            if (issueItems.length > 0) {
                sections.push(`
                    <div class="ai-validation-issues">
                        <div class="ai-validation-issues-header">校验问题 (${issueItems.length})</div>
                        ${issueItems.map(item => {
                            const isWarning = item.severity === 'warning';
                            const icon = isWarning ? '&#9888;' : '&#10007;';
                            const cls = isWarning ? 'is-warning' : 'is-error';
                            return `
                                <div class="ai-validation-issue ${cls}">
                                    <span class="ai-validation-issue-icon">${icon}</span>
                                    <div class="ai-validation-issue-body">
                                        <div class="ai-validation-issue-msg">${this._escapeHtml(item.message)}</div>
                                        ${item.category ? `<div class="ai-validation-issue-meta">${this._escapeHtml(item.category)}${item.operatorId ? ` · ${this._escapeHtml(item.operatorId)}` : ''}</div>` : ''}
                                        ${item.repairHint ? `<div class="ai-validation-issue-hint">${this._escapeHtml(item.repairHint)}</div>` : ''}
                                    </div>
                                </div>
                            `;
                        }).join('')}
                    </div>
                `);
            }
        }

        // DryRun result
        if (dryRun) {
            const isSuccess = dryRun.isSuccess ?? dryRun.IsSuccess ?? false;
            const coverage = dryRun.coveragePercentage ?? dryRun.CoveragePercentage ?? 0;
            const covered = dryRun.coveredBranches ?? dryRun.CoveredBranches ?? 0;
            const total = dryRun.totalBranches ?? dryRun.TotalBranches ?? 0;
            const duration = dryRun.durationMs ?? dryRun.DurationMs ?? null;
            const icon = isSuccess ? '&#10003;' : '&#10007;';
            const cls = isSuccess ? 'is-ok' : 'is-failed';
            sections.push(`
                <div class="ai-validation-dryrun ${cls}">
                    <div class="ai-validation-dryrun-header">
                        <span class="ai-validation-issue-icon">${icon}</span>
                        <span>DryRun ${isSuccess ? '通过' : '失败'}</span>
                        ${duration != null ? `<span class="ai-validation-dryrun-duration">${Math.round(duration)}ms</span>` : ''}
                    </div>
                    <div class="ai-validation-dryrun-coverage">
                        <div class="ai-coverage-bar">
                            <div class="ai-coverage-bar-fill" style="width:${Math.min(100, coverage)}%"></div>
                        </div>
                        <div class="ai-coverage-text">分支覆盖 ${covered}/${total} (${coverage.toFixed(1)}%)</div>
                    </div>
                </div>
            `);
        }

        // Knowledge graph diagnostics
        if (knowledgeDiags.length > 0) {
            sections.push(`
                <div class="ai-validation-issues">
                    <div class="ai-validation-issues-header">知识图谱诊断 (${knowledgeDiags.length})</div>
                    ${knowledgeDiags.map(d => {
                        const severity = d.severity || d.Severity || 'warning';
                        const isWarning = severity === 'warning';
                        const icon = isWarning ? '&#9888;' : '&#10007;';
                        const cls = isWarning ? 'is-warning' : 'is-error';
                        const message = d.message || d.Message || '';
                        const code = d.code || d.Code || '';
                        const operatorId = d.operatorId || d.OperatorId || '';
                        const repairHint = d.repairHint || d.RepairHint || '';
                        return `
                            <div class="ai-validation-issue ${cls}">
                                <span class="ai-validation-issue-icon">${icon}</span>
                                <div class="ai-validation-issue-body">
                                    <div class="ai-validation-issue-msg">${this._escapeHtml(message)}</div>
                                    ${code ? `<div class="ai-validation-issue-meta">${this._escapeHtml(code)}${operatorId ? ` · ${this._escapeHtml(operatorId)}` : ''}</div>` : ''}
                                    ${repairHint ? `<div class="ai-validation-issue-hint">${this._escapeHtml(repairHint)}</div>` : ''}
                                </div>
                            </div>
                        `;
                    }).join('')}
                </div>
            `);
        }

        container.innerHTML = sections.join('');
    }
};
