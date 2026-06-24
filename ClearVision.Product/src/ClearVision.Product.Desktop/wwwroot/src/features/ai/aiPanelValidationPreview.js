
import { renderRuntimePreviewSection } from './aiPanelRuntimePreview.js';
import { normalizeAgentToolTrace, renderAgentToolTrace } from './aiPanelToolTrace.js';
import {
    getParameterDisplayName,
    getResourceDisplayName,
    getStatusDisplayName
} from '../../shared/operatorDisplayNames.js';

const PREVIEW_SECTION_LABELS = Object.freeze({
    structuralValidation: '结构校验',
    dryRun: '元数据预演',
    deploymentPrecheck: '部署预检',
    runtimePreview: '运行预演'
});

function formatBoolean(value) {
    return value ? '是' : '否';
}

function formatCountLabel({ blocking, warnings, missing, executed, skipped, artifacts }) {
    return [
        blocking ? `${blocking} 项阻断` : '',
        warnings ? `${warnings} 条警告` : '',
        missing ? `${missing} 项缺失` : '',
        executed ? `${executed} 个已执行` : '',
        skipped ? `${skipped} 个已跳过` : '',
        artifacts ? `${artifacts} 份产物` : ''
    ].filter(Boolean).join(' · ') || '暂无问题';
}

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

    _getNumberValue(source, names) {
        const value = this._getObjectValue(source, names);
        if (value === undefined || value === null || value === '') {
            return null;
        }

        const number = Number(value);
        return Number.isFinite(number) ? number : null;
    },

    _hasObjectValue(source, names) {
        if (!source || typeof source !== 'object') {
            return false;
        }

        return names.some(name => this._getObjectValue(source, [name]) !== undefined);
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
                    <div class="ai-validation-issue-msg">${this._escapeHtml(this._localizeDisplayText?.(item.message || item.code || '待处理项') || item.message || item.code || '待处理项')}</div>
                    ${(item.code || item.operatorId) ? `<div class="ai-validation-issue-meta" title="${this._escapeHtml([item.code, item.operatorId].filter(Boolean).join(' · '))}">${this._escapeHtml([
                        this._localizeDisplayText?.(item.code) || item.code,
                        item.operatorId ? '关联算子' : ''
                    ].filter(Boolean).join(' · '))}</div>` : ''}
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
        const countLabel = formatCountLabel({
            blocking: blocking.length,
            warnings: warnings.length,
            missing: missing.length,
            executed: executed.length,
            skipped: skipped.length,
            artifacts: artifacts.length
        });

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
                        ${readyForDeployment !== null ? `<span title="readyForDeployment=${readyForDeployment ? 'true' : 'false'}">部署就绪：${formatBoolean(readyForDeployment)}</span>` : ''}
                        ${workflowDraftAllowed !== null ? `<span title="workflowDraftAllowed=${workflowDraftAllowed ? 'true' : 'false'}">草稿可编辑：${formatBoolean(workflowDraftAllowed)}</span>` : ''}
                        ${previewReady !== null ? `<span title="previewReady=${previewReady ? 'true' : 'false'}">预演就绪：${formatBoolean(previewReady)}</span>` : ''}
                        ${adapterName ? `<span title="adapterName=${this._escapeHtml(adapterName)}">适配器：${this._escapeHtml(getStatusDisplayName(adapterName, { fallback: '离线元数据适配器' }))}</span>` : ''}
                        ${previewMode ? `<span title="previewMode=${this._escapeHtml(previewMode)}">预演模式：${this._escapeHtml(getStatusDisplayName(previewMode, { fallback: '仅元数据' }))}</span>` : ''}
                    </div>
                ` : ''}
                ${(deployed !== null || packageCreated !== null || stationTouched !== null) ? `
                    <div class="ai-agent-preview-state-row">
                        ${deployed !== null ? `<span title="deployed=${deployed ? 'true' : 'false'}">已部署：${formatBoolean(deployed)}</span>` : ''}
                        ${packageCreated !== null ? `<span title="packageCreated=${packageCreated ? 'true' : 'false'}">运行包：${packageCreated ? '已生成' : '未生成'}</span>` : ''}
                        ${stationTouched !== null ? `<span title="stationTouched=${stationTouched ? 'true' : 'false'}">工站触达：${formatBoolean(stationTouched)}</span>` : ''}
                    </div>
                ` : ''}
                ${this._renderAgentPreviewIssueList(blocking, 'blocking')}
                ${this._renderAgentPreviewIssueList(warnings, 'warning')}
                ${this._renderAgentPreviewIssueList(missing, 'warning')}
                ${artifacts.length > 0 ? `
                    <div class="ai-agent-artifact-list">
                        ${artifacts.slice(0, 6).map(item => `
                            <div class="ai-agent-artifact-row">
                                <span title="${this._escapeHtml(String(item?.artifactType ?? item?.ArtifactType ?? 'artifact'))}">${this._escapeHtml(getResourceDisplayName(item?.artifactType ?? item?.ArtifactType ?? 'artifact'))}</span>
                                <small title="${this._escapeHtml([
                                    item?.artifactId ?? item?.ArtifactId ?? '',
                                    `metadataOnly=${item?.metadataOnly ?? item?.MetadataOnly ?? true}`,
                                    `binaryIncluded=${item?.binaryIncluded ?? item?.BinaryIncluded ?? false}`
                                ].filter(Boolean).join(' · '))}">${this._escapeHtml((item?.metadataOnly ?? item?.MetadataOnly ?? true) ? '仅元数据' : '含运行数据')}</small>
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
                </div>
            </details>
        `;
    },

    _classifyDryRunResult(dryRun) {
        if (!dryRun || typeof dryRun !== 'object') {
            return 'none';
        }

        if (this._hasObjectValue(dryRun, [
            'dryRunSucceeded',
            'DryRunSucceeded',
            'executedOperators',
            'ExecutedOperators',
            'skippedOperators',
            'SkippedOperators',
            'blockingIssues',
            'BlockingIssues',
            'missingResources',
            'MissingResources',
            'dryRunSummary',
            'DryRunSummary'
        ])) {
            return 'structureSimulation';
        }

        if (this._hasObjectValue(dryRun, [
            'isSuccess',
            'IsSuccess',
            'coveredBranches',
            'CoveredBranches',
            'totalBranches',
            'TotalBranches',
            'coveragePercentage',
            'CoveragePercentage',
            'durationMs',
            'DurationMs'
        ])) {
            return 'executionStub';
        }

        return 'unknown';
    },

    _renderStructureSimulationDryRun(dryRun) {
        const succeeded = this._getBooleanValue(dryRun, ['dryRunSucceeded', 'DryRunSucceeded']);
        const blocking = this._getArrayValue(dryRun, ['blockingIssues', 'BlockingIssues']);
        const warnings = this._getArrayValue(dryRun, ['warnings', 'Warnings']);
        const missing = this._getArrayValue(dryRun, ['missingResources', 'MissingResources']);
        const executed = this._getArrayValue(dryRun, ['executedOperators', 'ExecutedOperators']);
        const skipped = this._getArrayValue(dryRun, ['skippedOperators', 'SkippedOperators']);
        const summary = String(this._getObjectValue(dryRun, ['dryRunSummary', 'DryRunSummary', 'summary', 'Summary']) ?? '').trim();
        const cls = succeeded === true ? 'is-ok' : succeeded === false ? 'is-failed' : 'is-unavailable';
        const icon = succeeded === true ? '&#10003;' : succeeded === false ? '&#10007;' : '&#9432;';
        const title = succeeded === true
            ? '结构预演通过'
            : succeeded === false
                ? '结构预演未通过'
                : '元数据预演状态不可用';
        const countLabel = formatCountLabel({
            blocking: blocking.length,
            warnings: warnings.length,
            missing: missing.length,
            executed: executed.length,
            skipped: skipped.length,
            artifacts: 0
        });

        return `
            <div class="ai-validation-dryrun ${cls}" data-dryrun-contract="structure-simulation">
                <div class="ai-validation-dryrun-header">
                    <span class="ai-validation-issue-icon">${icon}</span>
                    <span>${this._escapeHtml(title)}</span>
                </div>
                <div class="ai-agent-preview-body">
                    <div class="ai-agent-preview-summary">${this._escapeHtml(summary || countLabel)}</div>
                    ${this._renderAgentPreviewIssueList(blocking, 'blocking')}
                    ${this._renderAgentPreviewIssueList(warnings, 'warning')}
                    ${this._renderAgentPreviewIssueList(missing, 'warning')}
                </div>
            </div>
        `;
    },

    _renderExecutionStubDryRun(dryRun) {
        const isSuccess = this._getBooleanValue(dryRun, ['isSuccess', 'IsSuccess']);
        if (isSuccess === null) {
            return this._renderUnknownDryRunResult();
        }

        const coverageValue = this._getNumberValue(dryRun, ['coveragePercentage', 'CoveragePercentage']);
        const coveredValue = this._getNumberValue(dryRun, ['coveredBranches', 'CoveredBranches']);
        const totalValue = this._getNumberValue(dryRun, ['totalBranches', 'TotalBranches']);
        const coverage = coverageValue ?? 0;
        const covered = coveredValue ?? 0;
        const total = totalValue ?? 0;
        const duration = this._getNumberValue(dryRun, ['durationMs', 'DurationMs']);
        const icon = isSuccess ? '&#10003;' : '&#10007;';
        const cls = isSuccess ? 'is-ok' : 'is-failed';
        return `
            <div class="ai-validation-dryrun ${cls}" data-dryrun-contract="execution-stub">
                <div class="ai-validation-dryrun-header">
                    <span class="ai-validation-issue-icon">${icon}</span>
                    <span>DryRun ${isSuccess ? '通过' : '失败'}</span>
                    ${duration != null ? `<span class="ai-validation-dryrun-duration">${Math.round(duration)}ms</span>` : ''}
                </div>
                <div class="ai-validation-dryrun-coverage">
                    <div class="ai-coverage-bar">
                        <div class="ai-coverage-bar-fill" style="width:${Math.min(100, Math.max(0, coverage))}%"></div>
                    </div>
                    <div class="ai-coverage-text">分支覆盖 ${covered}/${total} (${coverage.toFixed(1)}%)</div>
                </div>
            </div>
        `;
    },

    _renderUnknownDryRunResult() {
        return `
            <div class="ai-validation-dryrun is-unavailable" data-dryrun-contract="unknown">
                <div class="ai-validation-dryrun-header">
                    <span class="ai-validation-issue-icon">&#9432;</span>
                    <span>DryRun 状态不可用</span>
                </div>
                <div class="ai-agent-preview-summary">当前结果缺少可判别 DryRun 合同字段。</div>
            </div>
        `;
    },

    _renderDryRunResult(dryRun) {
        switch (this._classifyDryRunResult(dryRun)) {
            case 'structureSimulation':
                return this._renderStructureSimulationDryRun(dryRun);
            case 'executionStub':
                return this._renderExecutionStubDryRun(dryRun);
            case 'unknown':
                return this._renderUnknownDryRunResult();
            default:
                return '';
        }
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
                    <span title="deploymentActions=${deploymentState.deploymentActionsDisabled ? 'disabled' : 'enabled'}">部署操作：${deploymentState.deploymentActionsDisabled ? '禁用' : '启用'}</span>
                    <span title="workflowEditing=${deploymentState.workflowEditingAllowed ? 'enabled' : 'disabled'}">画布编辑：${deploymentState.workflowEditingAllowed ? '启用' : '禁用'}</span>
                </div>
                ${this._renderAgentValidationPreviewSection('structuralValidation', PREVIEW_SECTION_LABELS.structuralValidation, preview?.structuralValidation)}
                ${this._renderAgentValidationPreviewSection('dryRun', PREVIEW_SECTION_LABELS.dryRun, preview?.dryRun)}
                ${this._renderAgentValidationPreviewSection('deploymentPrecheck', PREVIEW_SECTION_LABELS.deploymentPrecheck, preview?.deploymentPrecheck)}
                ${renderRuntimePreviewSection(this, preview?.runtimePreview)}
                ${missingResources.length > 0 ? `
                    <div class="ai-agent-preview-section" data-agent-artifact="missingResources">
                        <div class="ai-agent-preview-header"><span title="missingResources">缺失资源</span><small>${missingResources.length}</small></div>
                        ${missingResources.slice(0, 6).map(item => `
                            <div class="ai-agent-artifact-row">
                                <span title="${this._escapeHtml(item.resourceKey || item.parameterName || item.resourceType || 'missing')}">${this._escapeHtml(getParameterDisplayName(item.parameterName, { fallback: getResourceDisplayName(item.resourceType, { fallback: '缺失资源' }) }))}</span>
                                <small>${this._escapeHtml(item.description || item.operatorId || '')}</small>
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
                ${pendingActions.length > 0 ? `
                    <div class="ai-agent-preview-section" data-agent-artifact="pendingActions">
                        <div class="ai-agent-preview-header"><span title="pendingActions">待处理动作</span><small>${pendingActions.length}</small></div>
                        ${pendingActions.slice(0, 6).map(item => `
                            <div class="ai-agent-artifact-row">
                                <span title="${this._escapeHtml(item.actionType || 'pending')}">${this._escapeHtml(getStatusDisplayName(item.actionType, { fallback: '待处理动作' }))}</span>
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
        const hasStructuredPreviewDryRun = Boolean(this._normalizeAgentValidationPreview(data)?.dryRun);
        const knowledgeDiags = data?.knowledgeDiagnostics || data?.KnowledgeDiagnostics || [];
        const agentArtifactsHtml = this._renderAgentValidationArtifacts(data);
        const hasDryRunCard = Boolean(dryRun) && !hasStructuredPreviewDryRun;
        const hasContent = diagnostics.length > 0 || manualRetry?.required || hasDryRunCard || knowledgeDiags.length > 0 || agentArtifactsHtml;

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

        if (hasDryRunCard) {
            sections.push(this._renderDryRunResult(dryRun));
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
