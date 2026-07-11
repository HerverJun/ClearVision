import {
    getOperatorTypeDisplayName,
    getParameterDisplayName,
    getResourceDisplayName
} from '../../shared/operatorDisplayNames.js';

const BUILD_RUNNING_STATES = new Set([
    'matching_template',
    'generating',
    'parsing',
    'validating',
    'dry_running',
    'applying'
]);

function clean(value) {
    return String(value ?? '').trim();
}

function asObject(value) {
    return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
}

function toArray(value) {
    return Array.isArray(value) ? value.filter(Boolean) : [];
}

function read(source, ...names) {
    const item = asObject(source);
    for (const name of names) {
        if (Object.prototype.hasOwnProperty.call(item, name)) return item[name];
    }
    return undefined;
}

function readBoolean(source, ...names) {
    const value = read(source, ...names);
    if (value === true || value === false) return value;
    if (typeof value === 'string') {
        const normalized = value.trim().toLowerCase();
        if (normalized === 'true') return true;
        if (normalized === 'false') return false;
    }
    return null;
}

function sanitize(panel, value, maxChars = 220) {
    const text = clean(value);
    if (!text) return '';
    return panel?._sanitizeBuildWorkspaceText?.(text, maxChars) ||
        panel?._sanitizeAssistantFailureText?.(text, maxChars) ||
        text.slice(0, maxChars);
}

function escapeHtml(panel, value) {
    return panel?._escapeHtml?.(String(value ?? '')) || String(value ?? '');
}

function classifyCheck(value) {
    if (!value || typeof value !== 'object') {
        return { status: 'pending', label: '待检查' };
    }

    const status = clean(read(value, 'status', 'Status', 'result', 'Result')).toLowerCase();
    const boolean = readBoolean(value,
        'passed', 'Passed',
        'succeeded', 'Succeeded',
        'success', 'Success',
        'valid', 'Valid',
        'ready', 'Ready');

    if (boolean === false || ['failed', 'error', 'invalid', 'blocked', 'rejected'].includes(status)) {
        return { status: 'failed', label: '未通过' };
    }
    if (boolean === true || ['passed', 'completed', 'success', 'succeeded', 'ready', 'valid'].includes(status)) {
        return { status: 'passed', label: '已通过' };
    }
    if (['running', 'pending', 'queued', 'in_progress'].includes(status)) {
        return { status: 'running', label: status === 'running' || status === 'in_progress' ? '进行中' : '等待中' };
    }
    return { status: 'pending', label: '待检查' };
}

function countPendingFields(pending) {
    return toArray(pending).reduce((total, item) => {
        const names = toArray(read(item, 'parameterNames', 'ParameterNames'));
        return total + Math.max(names.length, 1);
    }, 0);
}

function getDisplayText(panel, value, fallback = '') {
    return sanitize(panel, value, 180) || fallback;
}

function getDiagnosticMessage(panel, item) {
    if (typeof item === 'string') return sanitize(panel, item, 220);
    return sanitize(panel, read(item,
        'publicMessage', 'PublicMessage',
        'message', 'Message',
        'summary', 'Summary',
        'description', 'Description',
        'reason', 'Reason'), 220);
}

function getDiagnosticSeverity(item) {
    const value = clean(read(item, 'severity', 'Severity', 'status', 'Status', 'level', 'Level')).toLowerCase();
    if (['error', 'failed', 'fatal', 'invalid', 'blocked'].includes(value)) return 'error';
    if (['warning', 'warn'].includes(value)) return 'warning';
    return 'info';
}

function getPipeline(panel, buildResult, flow) {
    const pipeline = toArray(read(buildResult, 'operatorPipeline', 'OperatorPipeline'));
    const source = pipeline.length ? pipeline : (panel?._extractOperators?.(flow) || []);
    return source.map((item, index) => {
        const type = clean(read(item, 'operatorType', 'OperatorType', 'type', 'Type'));
        const label = sanitize(panel,
            read(item, 'displayName', 'DisplayName', 'name', 'Name'),
            120) || getOperatorTypeDisplayName(type, { fallback: type || `阶段 ${index + 1}` });
        const status = clean(read(item, 'status', 'Status')).toLowerCase();
        return {
            label,
            type,
            status: status || 'unknown'
        };
    });
}

function getWorkflowDiff(buildResult, result) {
    return asObject(read(buildResult, 'workflowDiff', 'WorkflowDiff') || read(result, 'workflowDiff', 'WorkflowDiff'));
}

function deriveDiff(diff) {
    const added = toArray(read(diff, 'addedNodes', 'AddedNodes'));
    const modified = toArray(read(diff, 'modifiedNodes', 'ModifiedNodes', 'updatedNodes', 'UpdatedNodes'));
    const removed = toArray(read(diff, 'removedNodes', 'RemovedNodes', 'deletedNodes', 'DeletedNodes'));
    const connections = toArray(read(diff, 'connectionChanges', 'ConnectionChanges', 'changedConnections', 'ChangedConnections'));
    const parameters = toArray(read(diff, 'parameterChanges', 'ParameterChanges', 'pendingParameters', 'PendingParameters'));
    const blockers = toArray(read(diff, 'deploymentBlockers', 'DeploymentBlockers'));
    return {
        available: Object.keys(diff).length > 0,
        added: added.length,
        modified: modified.length,
        removed: removed.length,
        connections: connections.length,
        parameters: parameters.length,
        blockers
    };
}

function getResolvedResourceCount(panel, missingResources) {
    return toArray(missingResources).filter(item => {
        const draft = panel?._getPendingResourceDraft?.(item);
        return draft?.status === 'resolved';
    }).length;
}

function deriveParameterState(panel, source, flow, missingResources = []) {
    const pending = panel?._resolvePendingParametersForDraft?.(source) ||
        toArray(read(source, 'pendingParameters', 'PendingParameters'));
    const operators = panel?._getPendingOperatorSourceOperators?.(flow) || [];
    const rawGroups = panel?._collectPendingDraftGroups?.(pending, operators) || [];
    const resourceRefs = toArray(missingResources).map(item => {
        const operatorId = clean(read(item, 'operatorId', 'OperatorId', 'actualOperatorId', 'ActualOperatorId')).toLowerCase();
        const parameterName = clean(read(item, 'parameterName', 'ParameterName') || panel?._inferPendingParameterNameFromMissingResource?.(item)).toLowerCase();
        const resourceKey = clean(read(item, 'resourceKey', 'ResourceKey')).toLowerCase();
        return { operatorId, parameterName, resourceKey };
    });
    const isResourceBacked = (operatorId, parameterName) => {
        const op = clean(operatorId).toLowerCase();
        const name = clean(parameterName).toLowerCase();
        return resourceRefs.some(ref =>
            (ref.parameterName === name && (!ref.operatorId || ref.operatorId === op)) ||
            (ref.resourceKey && ref.resourceKey === `${op}.${name}`));
    };
    const groups = rawGroups.map(group => ({
        ...group,
        fields: toArray(group.fields).filter(field => !isResourceBacked(group.operatorId, field.parameterName))
    })).filter(group => group.fields.length > 0);
    const confirmation = panel?._getPendingParameterConfirmationState?.(pending, operators, rawGroups) || null;
    const total = groups.reduce((sum, group) => sum + group.fields.length, 0);
    const filled = groups.reduce((sum, group) => sum + group.fields.filter(field =>
        panel?._hasPendingDraftValue?.(field.confirmedValue, field.dataType) === true
    ).length, 0);
    const confirmed = total === 0 || confirmation?.isConfirmed === true;
    return {
        items: pending,
        groups,
        total,
        filled,
        confirmed,
        unresolved: confirmed ? 0 : total,
        resourceBackedCount: Math.max((confirmation?.totals?.total ?? countPendingFields(pending)) - total, 0)
    };
}

function deriveValidation(panel, source, gate) {
    const preview = asObject(read(source, 'validationPreview', 'ValidationPreview'));
    const structural = asObject(read(preview, 'structuralValidation', 'StructuralValidation'));
    const dryRun = asObject(read(preview, 'dryRun', 'DryRun') || read(source, 'dryRunResult', 'DryRunResult'));
    const diagnostics = [
        ...toArray(read(source, 'lastAttemptDiagnostics', 'LastAttemptDiagnostics')),
        ...toArray(read(source, 'knowledgeDiagnostics', 'KnowledgeDiagnostics'))
    ];
    const errors = diagnostics.filter(item => getDiagnosticSeverity(item) === 'error');
    const warnings = diagnostics.filter(item => getDiagnosticSeverity(item) === 'warning');
    const structuralState = errors.length
        ? { status: 'failed', label: '未通过' }
        : classifyCheck(structural);
    const dryRunState = classifyCheck(dryRun);
    const gateBlocked = readBoolean(gate, 'blocked', 'Blocked') === true;
    const canvasReady = readBoolean(gate, 'canvasApplyReady', 'CanvasApplyReady');
    const gateState = gateBlocked
        ? { status: 'failed', label: '已阻断' }
        : canvasReady === true
            ? { status: 'passed', label: '可应用' }
            : { status: 'pending', label: '待确认' };
    const overall = errors.length || structuralState.status === 'failed' || dryRunState.status === 'failed'
        ? 'failed'
        : structuralState.status === 'passed' && dryRunState.status === 'passed'
            ? 'passed'
            : structuralState.status === 'running' || dryRunState.status === 'running'
                ? 'running'
                : 'pending';
    return {
        structural: structuralState,
        dryRun: dryRunState,
        gate: gateState,
        overall,
        errors,
        warnings,
        diagnostics
    };
}

function deriveActionItems(panel, context) {
    const items = [];
    const {
        parameters,
        missingResources,
        resolvedResourceCount,
        validation,
        gateBlocked,
        gateBlockers,
        diff
    } = context;

    if (parameters.unresolved > 0) {
        const firstGroup = parameters.groups[0];
        const names = toArray(firstGroup?.fields)
            .slice(0, 2)
            .map(field => getParameterDisplayName(field.parameterName, { fallback: field.parameterName || '参数' }))
            .filter(Boolean);
        items.push({
            key: 'parameters',
            priority: 'blocking',
            title: `${parameters.unresolved} 项参数待确认`,
            summary: names.length ? `优先补齐 ${names.join('、')}。` : '必填参数尚未完成工程确认。',
            impact: '影响参数确认、验证或后续应用准备。',
            status: `已填写 ${parameters.filled}/${parameters.total}`,
            target: 'ai-build-parameters-section',
            action: '前往参数'
        });
    }

    const unresolvedResources = Math.max(missingResources.length - resolvedResourceCount, 0);
    if (unresolvedResources > 0) {
        const first = missingResources.find(item => panel?._getPendingResourceDraft?.(item)?.status !== 'resolved') || missingResources[0];
        const type = clean(read(first, 'resourceType', 'ResourceType'));
        items.push({
            key: 'resources',
            priority: 'blocking',
            title: `${unresolvedResources} 项资源待绑定`,
            summary: `${getResourceDisplayName(type, { fallback: '工程资源' })}尚未完成具体绑定。`,
            impact: '可能阻断运行、部署验证或 Apply Gate。',
            status: '等待人工绑定',
            target: 'ai-build-resources-section',
            action: '前往资源'
        });
    }

    if (validation.overall === 'failed') {
        const firstError = validation.errors.map(item => getDiagnosticMessage(panel, item)).find(Boolean);
        items.push({
            key: 'validation',
            priority: 'blocking',
            title: `${Math.max(validation.errors.length, 1)} 项验证问题`,
            summary: firstError || '静态验证或 DryRun 未通过。',
            impact: '需要修复后重新进入现有验证链路。',
            status: '验证未通过',
            target: 'ai-build-validation-section',
            action: '查看验证'
        });
    }

    if (diff.blockers.length > 0 && items.length === 0) {
        items.push({
            key: 'contract',
            priority: 'blocking',
            title: `${diff.blockers.length} 项拓扑或部署阻断`,
            summary: getDiagnosticMessage(panel, diff.blockers[0]) || '流程差异仍包含未解决的工程阻断。',
            impact: '影响部署准备或画布应用。',
            status: '等待处理',
            target: 'ai-build-apply-section',
            action: '查看预览'
        });
    }

    if (gateBlocked && items.length === 0) {
        items.push({
            key: 'gate',
            priority: 'blocking',
            title: '应用门禁尚未开放',
            summary: gateBlockers[0] || '当前草稿仍受现有 Apply Gate 约束。',
            impact: '阻断应用到画布。',
            status: 'ApplyGate blocked',
            target: 'ai-build-apply-section',
            action: '查看门禁'
        });
    }

    if (!items.length && validation.warnings.length > 0) {
        items.push({
            key: 'warnings',
            priority: 'advisory',
            title: `${validation.warnings.length} 项非阻断建议`,
            summary: getDiagnosticMessage(panel, validation.warnings[0]) || '建议在应用前复核工程提示。',
            impact: '不直接解除或改变现有 Gate。',
            status: '建议复核',
            target: 'ai-build-validation-section',
            action: '查看建议'
        });
    }

    return items.slice(0, 4);
}

function deriveOverallState(context) {
    const {
        workbenchState,
        hasFlow,
        parameters,
        unresolvedResources,
        validation,
        canvasReady,
        applied,
        applying,
        gateBlocked,
        nodeCount,
        connectionCount
    } = context;

    if (applied) {
        return {
            key: 'applied',
            tone: 'success',
            label: '已应用',
            result: `流程草稿已应用到画布，保留 ${nodeCount} 个节点和 ${connectionCount} 条连线的构建证据。`,
            next: '查看画布结果，或返回方案继续调整。',
            target: 'ai-build-apply-section'
        };
    }
    if (applying) {
        return {
            key: 'applying',
            tone: 'info',
            label: '正在应用',
            result: '正在按现有 Apply Preview 和画布应用语义更新流程。',
            next: '等待画布应用完成。',
            target: 'ai-build-apply-section'
        };
    }
    if (workbenchState === 'failed' && validation.overall !== 'failed') {
        return {
            key: 'failed',
            tone: 'danger',
            label: '构建失败',
            result: '本轮构建没有形成可继续应用的完整结果。',
            next: '在验证与工程详情中查看失败原因后重试。',
            target: 'ai-build-validation-section'
        };
    }
    if (validation.overall === 'failed') {
        return {
            key: 'validation_failed',
            tone: 'danger',
            label: '验证失败',
            result: hasFlow ? `流程草稿已生成，但 ${Math.max(validation.errors.length, 1)} 项检查未通过。` : '构建结果未通过验证。',
            next: '先修复最重要的验证问题，再重新执行现有验证链路。',
            target: 'ai-build-validation-section'
        };
    }
    if (parameters.unresolved > 0 || unresolvedResources > 0) {
        return {
            key: 'needs_input',
            tone: 'warning',
            label: '构建完成，等待补齐',
            result: hasFlow
                ? `已生成 ${nodeCount} 个节点、${connectionCount} 条连线的流程草稿。`
                : '构建结果已返回，但工程补齐尚未完成。',
            next: parameters.unresolved > 0
                ? `先补齐并确认 ${parameters.unresolved} 项参数。`
                : `先绑定 ${unresolvedResources} 项具体资源。`,
            target: parameters.unresolved > 0 ? 'ai-build-parameters-section' : 'ai-build-resources-section'
        };
    }
    if (canvasReady && !gateBlocked) {
        return {
            key: 'ready_to_apply',
            tone: 'success',
            label: '可以应用',
            result: `构建、工程补齐与当前门禁检查已完成，草稿包含 ${nodeCount} 个节点。`,
            next: '复核 Workflow Diff 与应用预览后，应用到画布。',
            target: 'ai-build-apply-section'
        };
    }
    if (hasFlow && validation.overall === 'passed') {
        return {
            key: 'validation_passed',
            tone: 'success',
            label: '验证通过',
            result: `流程草稿已通过当前验证，包含 ${nodeCount} 个节点和 ${connectionCount} 条连线。`,
            next: '继续检查应用门禁与 Workflow Diff。',
            target: 'ai-build-apply-section'
        };
    }
    if (hasFlow) {
        return {
            key: 'ready_to_validate',
            tone: 'info',
            label: '可以开始验证',
            result: `已生成 ${nodeCount} 个节点、${connectionCount} 条连线的流程草稿。`,
            next: '复核参数与资源后，执行静态验证和 DryRun。',
            target: 'ai-build-validation-section'
        };
    }
    if (BUILD_RUNNING_STATES.has(workbenchState)) {
        const validating = workbenchState === 'validating' || workbenchState === 'dry_running';
        return {
            key: validating ? 'validating' : 'building',
            tone: 'info',
            label: validating ? '正在验证' : '正在构建',
            result: validating ? '正在执行现有静态检查或 DryRun 链路。' : '正在生成并整理可编辑流程草稿。',
            next: '等待当前阶段完成，期间不会开放未满足条件的应用操作。',
            target: 'ai-build-validation-section'
        };
    }
    return {
        key: 'waiting',
        tone: 'neutral',
        label: '等待构建结果',
        result: 'Build 已进入工程工作区，尚未收到完整流程草稿。',
        next: '等待后端 AgentRun 发布构建结果。',
        target: 'ai-build-status-section'
    };
}

export function deriveAiBuildPresentation(panel) {
    const events = Array.isArray(panel?.activeAgentRunEvents) ? panel.activeAgentRunEvents : [];
    const eventPayload = asObject(panel?._getAgentRunResultPayload?.(events));
    const currentResult = asObject(panel?.currentResult);
    const result = Object.keys(currentResult).length ? currentResult : eventPayload;
    const buildResult = asObject(
        panel?._getPayloadBuildResult?.(currentResult) ||
        panel?._getPayloadBuildResult?.(eventPayload) ||
        read(result, 'buildResult', 'BuildResult')
    );
    const flow = panel?._getResultFlowForCanvas?.(currentResult) ||
        panel?._getResultFlowForCanvas?.(eventPayload) ||
        read(result, 'flow', 'Flow') ||
        read(currentResult, 'flow', 'Flow') ||
        null;
    const operators = flow ? (panel?._extractOperators?.(flow) || []) : [];
    const connections = flow ? (panel?._extractConnections?.(flow) || []) : [];
    const sourceForPending = Object.keys(result).length ? result : currentResult;
    const missingResources = panel?._normalizeMissingResources?.(
        read(sourceForPending, 'missingResources', 'MissingResources') ||
        read(buildResult, 'missingResources', 'MissingResources') ||
        []
    ) || [];
    const parameters = deriveParameterState(panel, sourceForPending, flow, missingResources);
    const resolvedResourceCount = getResolvedResourceCount(panel, missingResources);
    const unresolvedResources = Math.max(missingResources.length - resolvedResourceCount, 0);
    const gate = asObject(
        panel?._getPayloadApplyGate?.(sourceForPending) ||
        read(buildResult, 'applyGate', 'ApplyGate') ||
        read(currentResult, 'applyGate', 'ApplyGate')
    );
    const gateBlocked = readBoolean(gate, 'blocked', 'Blocked') === true;
    const canvasReady = readBoolean(gate, 'canvasApplyReady', 'CanvasApplyReady') === true;
    const validation = deriveValidation(panel, sourceForPending, gate);
    const diff = deriveDiff(getWorkflowDiff(buildResult, sourceForPending));
    const gateBlockers = [
        ...toArray(read(gate, 'blockers', 'Blockers', 'deploymentBlockers', 'DeploymentBlockers')),
        ...toArray(read(buildResult, 'unresolvedStrategyBlockers', 'UnresolvedStrategyBlockers')),
        ...diff.blockers
    ].map(item => getDiagnosticMessage(panel, item)).filter(Boolean);
    const pipeline = getPipeline(panel, buildResult, flow);
    const workbenchState = clean(panel?.workbenchState || read(sourceForPending, 'interactionState', 'InteractionState', 'status', 'Status')).toLowerCase();
    const applied = workbenchState === 'applied' || panel?._isCurrentResultAppliedToCanvas?.() === true;
    const applying = workbenchState === 'applying';
    const plan = asObject(panel?.pendingVisionPlan);
    const semantic = asObject(read(plan, 'semanticExtraction', 'SemanticExtraction') || read(sourceForPending, 'semanticExtraction', 'SemanticExtraction'));
    const inputSource = getDisplayText(panel,
        read(semantic, 'imageSource', 'ImageSource') || read(sourceForPending, 'inputSource', 'InputSource'),
        '由流程输入决定');
    const outputTarget = getDisplayText(panel,
        read(semantic, 'outputTarget', 'OutputTarget') || read(sourceForPending, 'outputTarget', 'OutputTarget'),
        '由末端算子输出');
    const actionItems = deriveActionItems(panel, {
        parameters,
        missingResources,
        resolvedResourceCount,
        validation,
        gateBlocked,
        gateBlockers,
        diff
    });
    const overall = deriveOverallState({
        workbenchState,
        hasFlow: operators.length > 0,
        parameters,
        unresolvedResources,
        validation,
        canvasReady,
        applied,
        applying,
        gateBlocked,
        nodeCount: operators.length,
        connectionCount: connections.length
    });

    return {
        overall,
        workbenchState,
        nodeCount: operators.length,
        connectionCount: connections.length,
        pipeline,
        parameters,
        resources: {
            total: missingResources.length,
            resolved: resolvedResourceCount,
            unresolved: unresolvedResources,
            items: missingResources
        },
        validation,
        gate: {
            blocked: gateBlocked,
            canvasReady,
            runtimeReady: readBoolean(gate, 'runtimeDraftReady', 'RuntimeDraftReady'),
            deploymentReady: readBoolean(gate, 'deploymentReady', 'DeploymentReady'),
            status: sanitize(panel, read(gate, 'status', 'Status'), 80) || 'unknown',
            blockers: gateBlockers
        },
        diff,
        actionItems,
        inputSource,
        outputTarget,
        structureStatus: operators.length > 0
            ? (validation.structural.status === 'failed' ? '结构存在问题' : '草稿结构已生成')
            : '等待流程草稿',
        applied
    };
}

function renderMetric(panel, label, value, hint = '') {
    return `
        <div class="ai-build-v2-metric">
            <dt>${escapeHtml(panel, label)}</dt>
            <dd>${escapeHtml(panel, value)}</dd>
            ${hint ? `<small>${escapeHtml(panel, hint)}</small>` : ''}
        </div>
    `;
}

function renderStatusSummary(panel, presentation) {
    const { overall, parameters, resources, validation, gate } = presentation;
    const blockerCount = presentation.actionItems.filter(item => item.priority === 'blocking').length;
    const validationLabel = validation.overall === 'failed'
        ? '验证未通过'
        : validation.overall === 'passed'
            ? '验证已通过'
            : validation.overall === 'running'
                ? '验证进行中'
                : gate.canvasReady && !gate.blocked
                    ? '门禁已就绪'
                    : '验证待执行';
    return `
        <div class="ai-build-v2-summary-copy">
            <span class="ai-build-v2-kicker">构建状态</span>
            <div class="ai-build-v2-title-row">
                <h2>${escapeHtml(panel, overall.label)}</h2>
                <span class="ai-build-v2-status is-${escapeHtml(panel, overall.tone)}">${escapeHtml(panel, validationLabel)}</span>
            </div>
            <p>${escapeHtml(panel, overall.result)}</p>
        </div>
        <dl class="ai-build-v2-metrics">
            ${renderMetric(panel, '阻断', blockerCount, blockerCount ? '需要处理' : '当前无阻断')}
            ${renderMetric(panel, '待补参数', parameters.unresolved, parameters.confirmed ? '已确认' : `${parameters.filled}/${parameters.total} 已填写`)}
            ${renderMetric(panel, '待补资源', resources.unresolved, resources.unresolved ? '等待绑定' : '当前完整')}
            ${renderMetric(panel, '应用状态', presentation.applied ? '已应用' : gate.canvasReady && !gate.blocked ? '可应用' : '未就绪', gate.status)}
        </dl>
        <div class="ai-build-v2-next">
            <span>下一步</span>
            <strong>${escapeHtml(panel, overall.next)}</strong>
            <button type="button" data-ai-build-target="${escapeHtml(panel, overall.target)}">前往处理区域</button>
        </div>
    `;
}

function renderFlowSummary(panel, presentation) {
    const visible = presentation.pipeline.slice(0, 6);
    const hiddenCount = Math.max(presentation.pipeline.length - visible.length, 0);
    return `
        <div class="ai-build-v2-flow-meta">
            <span><small>节点</small><strong>${escapeHtml(panel, presentation.nodeCount)}</strong></span>
            <span><small>连线</small><strong>${escapeHtml(panel, presentation.connectionCount)}</strong></span>
            <span><small>输入</small><strong>${escapeHtml(panel, presentation.inputSource)}</strong></span>
            <span><small>输出</small><strong>${escapeHtml(panel, presentation.outputTarget)}</strong></span>
            <span><small>结构</small><strong>${escapeHtml(panel, presentation.structureStatus)}</strong></span>
        </div>
        ${visible.length ? `
            <ol class="ai-build-v2-flow" aria-label="真实算子链摘要">
                ${visible.map((item, index) => `
                    <li data-operator-status="${escapeHtml(panel, item.status)}">
                        <span>阶段 ${index + 1}</span>
                        <strong>${escapeHtml(panel, item.label)}</strong>
                    </li>
                `).join('')}
            </ol>
            ${hiddenCount ? `<p class="ai-build-v2-inline-note">另有 ${escapeHtml(panel, hiddenCount)} 个节点，可在工程详情中查看完整列表。</p>` : ''}
        ` : '<div class="ai-build-v2-empty">流程草稿生成后将在这里显示真实算子阶段。</div>'}
    `;
}

function renderActionQueue(panel, presentation) {
    if (!presentation.actionItems.length) {
        return `
            <div class="ai-build-v2-ready">
                <strong>当前没有需要处理的阻断</strong>
                <span>${presentation.gate.canvasReady ? '可以复核应用预览并应用到画布。' : '继续执行验证并等待现有 Gate 给出最终状态。'}</span>
            </div>
        `;
    }
    return `
        <div class="ai-build-v2-action-list">
            ${presentation.actionItems.map(item => `
                <article class="ai-build-v2-action is-${escapeHtml(panel, item.priority)}">
                    <div class="ai-build-v2-action-main">
                        <span>${item.priority === 'blocking' ? '阻断事项' : '工程建议'}</span>
                        <strong>${escapeHtml(panel, item.title)}</strong>
                        <p>${escapeHtml(panel, item.summary)}</p>
                    </div>
                    <div class="ai-build-v2-action-meta">
                        <small>${escapeHtml(panel, item.impact)}</small>
                        <b>${escapeHtml(panel, item.status)}</b>
                        <button type="button" data-ai-build-target="${escapeHtml(panel, item.target)}">${escapeHtml(panel, item.action)}</button>
                    </div>
                </article>
            `).join('')}
        </div>
    `;
}

function renderValidationSummary(panel, presentation) {
    const checks = [
        { title: '静态与拓扑检查', state: presentation.validation.structural },
        { title: 'DryRun', state: presentation.validation.dryRun },
        { title: '应用门禁', state: presentation.validation.gate }
    ];
    const summary = presentation.validation.overall === 'failed'
        ? `最重要的 ${Math.max(presentation.validation.errors.length, 1)} 项问题需要先处理。`
        : presentation.validation.overall === 'passed'
            ? '当前验证证据已通过，继续复核应用预览。'
            : '验证结果将直接消费现有静态检查、DryRun 与 Gate 状态。';
    return `
        <div class="ai-build-v2-validation-copy">
            <strong>${escapeHtml(panel, summary)}</strong>
            <span>验证状态仅来自既有 BuildResult、Validation Preview 与 Apply Gate。</span>
        </div>
        <div class="ai-build-v2-checks">
            ${checks.map(item => `
                <div class="ai-build-v2-check is-${escapeHtml(panel, item.state.status)}">
                    <span aria-hidden="true"></span>
                    <div>
                        <strong>${escapeHtml(panel, item.title)}</strong>
                        <small>${escapeHtml(panel, item.state.label)}</small>
                    </div>
                </div>
            `).join('')}
        </div>
    `;
}

function renderApplySummary(panel, presentation) {
    const diff = presentation.diff;
    const gateLabel = presentation.applied
        ? '已应用到画布'
        : presentation.gate.canvasReady && !presentation.gate.blocked
            ? 'Apply 已准备完成'
            : 'Apply 尚未准备完成';
    return `
        <div class="ai-build-v2-apply-state">
            <div>
                <span>应用状态</span>
                <strong>${escapeHtml(panel, gateLabel)}</strong>
                <small>${presentation.gate.blockers[0] ? escapeHtml(panel, presentation.gate.blockers[0]) : '按钮继续服从现有 Apply Gate。'}</small>
            </div>
            ${diff.available ? `
                <dl class="ai-build-v2-diff">
                    ${renderMetric(panel, '新增节点', diff.added)}
                    ${renderMetric(panel, '修改节点', diff.modified)}
                    ${renderMetric(panel, '删除节点', diff.removed)}
                    ${renderMetric(panel, '连线变化', diff.connections)}
                    ${renderMetric(panel, '参数变化', diff.parameters)}
                    ${renderMetric(panel, '未解风险', diff.blockers.length)}
                </dl>
            ` : '<div class="ai-build-v2-empty">当前结果尚未提供 Workflow Diff 摘要。</div>'}
        </div>
    `;
}

function bindBuildNavigation(panel, root) {
    root?.querySelectorAll?.('[data-ai-build-target]').forEach(button => {
        button.onclick = () => {
            const targetId = button.dataset.aiBuildTarget || '';
            const target = targetId ? panel?.container?.querySelector?.(`#${targetId}`) : null;
            if (!target) return;
            const reduceMotion = globalThis.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches === true;
            target.scrollIntoView?.({ block: 'start', behavior: reduceMotion ? 'auto' : 'smooth' });
            target.focus?.({ preventScroll: true });
        };
    });
}

export function renderAiBuildPresentation(panel) {
    const root = panel?.container?.querySelector?.('#ai-build-workspace');
    if (!root) return null;
    const presentation = deriveAiBuildPresentation(panel);
    root.dataset.aiBuildState = presentation.overall.key;

    const status = root.querySelector('#ai-build-status-summary');
    const flow = root.querySelector('#ai-build-flow-summary');
    const queue = root.querySelector('#ai-build-action-queue');
    const validation = root.querySelector('#ai-build-validation-summary');
    const apply = root.querySelector('#ai-build-apply-summary');
    if (status) status.innerHTML = renderStatusSummary(panel, presentation);
    if (flow) flow.innerHTML = renderFlowSummary(panel, presentation);
    if (queue) queue.innerHTML = renderActionQueue(panel, presentation);
    if (validation) validation.innerHTML = renderValidationSummary(panel, presentation);
    if (apply) apply.innerHTML = renderApplySummary(panel, presentation);
    const parameterSection = root.querySelector('#ai-build-parameters-section');
    const resourceSection = root.querySelector('#ai-build-resources-section');
    if (parameterSection) parameterSection.hidden = presentation.parameters.total === 0;
    if (resourceSection) resourceSection.hidden = presentation.resources.total === 0;
    const completionNav = root.querySelector('[data-ai-build-nav="completion"]');
    if (completionNav) {
        completionNav.hidden = presentation.parameters.total === 0 && presentation.resources.total === 0;
        completionNav.dataset.aiBuildTarget = presentation.parameters.total > 0
            ? 'ai-build-parameters-section'
            : 'ai-build-resources-section';
    }
    bindBuildNavigation(panel, root);
    return presentation;
}

export function renderAiBuildWorkspaceScaffold() {
    return `
        <div class="ai-build-workspace" id="ai-build-workspace" data-ai-build-presentation="v3" hidden>
            <div class="ai-build-v2" data-ai-hook="build-workspace-v3">
                <section class="ai-build-v2-summary" id="ai-build-status-section" tabindex="-1">
                    <div id="ai-build-status-summary" role="status" aria-live="polite"></div>
                    <div class="ai-result-status-note" id="ai-result-status-note" role="status" aria-live="polite"></div>
                </section>

                <nav class="ai-build-v2-nav" aria-label="构建工作区导航">
                    <button type="button" data-ai-build-target="ai-build-flow-section">构建结果</button>
                    <button type="button" data-ai-build-nav="completion" data-ai-build-target="ai-build-parameters-section">参数与资源</button>
                    <button type="button" data-ai-build-target="ai-build-validation-section">验证</button>
                    <button type="button" data-ai-build-target="ai-build-apply-section">应用预览</button>
                </nav>

                <div class="ai-results-scroll ai-build-v2-body" id="ai-results-scroll">
                    <section class="ai-build-v2-section" id="ai-build-flow-section" tabindex="-1">
                        <header class="ai-build-v2-section-header">
                            <div>
                                <span>构建结果</span>
                                <h3>流程草稿摘要</h3>
                            </div>
                            <p>展示真实算子与当前工程结构，不展开内部节点 ID。</p>
                        </header>
                        <div id="ai-build-flow-summary"></div>
                    </section>

                    <section class="ai-build-v2-section ai-build-v2-action-section" id="ai-build-actions-section" tabindex="-1">
                        <header class="ai-build-v2-section-header">
                            <div>
                                <span>工程主线</span>
                                <h3>待处理事项</h3>
                            </div>
                            <p>汇总既有参数、资源、验证和 Apply 阻断，不创建第二套队列。</p>
                        </header>
                        <div id="ai-build-action-queue"></div>
                    </section>

                    <section class="ai-build-v2-section" id="ai-build-parameters-section" tabindex="-1">
                        <header class="ai-build-v2-section-header">
                            <div>
                                <span>工程补齐</span>
                                <h3>参数工作区</h3>
                            </div>
                            <p>保留现有编辑、人工确认和可选 AI 复核链路。</p>
                        </header>
                        <div class="ai-parameter-editor is-empty" id="ai-result-parameter-editor">
                            <div class="ai-followup-empty">当前没有待确认参数，暂无需补录。</div>
                        </div>
                    </section>

                    <section class="ai-build-v2-section" id="ai-build-resources-section" tabindex="-1">
                        <header class="ai-build-v2-section-header">
                            <div>
                                <span>工程补齐</span>
                                <h3>资源工作区</h3>
                            </div>
                            <p>在 Build 中绑定具体相机、模型、模板或外部资源。</p>
                        </header>
                        <div class="ai-followup-panel is-empty" id="ai-result-followups">
                            <div class="ai-followup-empty">当前没有缺失资源或后续工程动作。</div>
                        </div>
                    </section>

                    <section class="ai-build-v2-section" id="ai-build-validation-section" tabindex="-1">
                        <header class="ai-build-v2-section-header">
                            <div>
                                <span>工程验证</span>
                                <h3>验证与 DryRun</h3>
                            </div>
                            <p>集中查看静态检查、拓扑验证、预演结果与修复建议。</p>
                        </header>
                        <div id="ai-build-validation-summary"></div>
                        <div class="validation-card" id="ai-result-validation-card" hidden>
                            <div class="ai-validation-panel" id="ai-result-validation"></div>
                        </div>
                    </section>

                    <section class="ai-build-v2-section" id="ai-build-apply-section" tabindex="-1">
                        <header class="ai-build-v2-section-header">
                            <div>
                                <span>画布变更</span>
                                <h3>应用预览</h3>
                            </div>
                            <p>继续复用 Workflow Diff、Apply Preview 与原 Apply Gate。</p>
                        </header>
                        <div id="ai-build-apply-summary"></div>
                        <details class="ai-build-v2-inline-details">
                            <summary>查看完整 Workflow Diff 与门禁详情</summary>
                            <div class="ai-build-checks" id="ai-build-checks"></div>
                            <div class="ai-build-compact" id="ai-build-final-draft"></div>
                        </details>
                        <div class="apply-container">
                            <button class="btn-apply-flow" id="ai-btn-apply" disabled>
                                <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true">
                                    <path d="M9 16.2L4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4L9 16.2z"/>
                                </svg>
                                应用到画布
                            </button>
                            <div class="ai-apply-gate-hint">按钮继续服从现有 CanvasApplyReady、ApplyGate 与画布应用语义。</div>
                        </div>
                    </section>

                    <details class="ai-build-v2-engineering" data-ai-hook="build-engineering-details">
                        <summary>工程详情与内部诊断</summary>
                        <p>Agent 状态、完整时间线、模板决策、内部参数映射和公开诊断默认收起。</p>
                        <div class="ai-build-v2-engineering-body">
                            <div class="ai-agent-runtime" id="ai-agent-runtime" hidden></div>
                            <div class="ai-workbench-state-bar" id="ai-workbench-state-bar"></div>
                            <section>
                                <h4>完整 Build 事件</h4>
                                <div class="ai-build-event-timeline" id="ai-build-event-timeline"></div>
                            </section>
                            <section>
                                <h4>模板与模型决策</h4>
                                <div class="ai-build-compact" id="ai-build-template-match"></div>
                            </section>
                            <section>
                                <h4>完整算子与参数映射</h4>
                                <div class="ai-build-compact" id="ai-build-operator-chain"></div>
                                <div class="ai-build-compact" id="ai-build-parameters"></div>
                            </section>
                            <div class="requirement-brief-card" id="ai-result-requirement-brief-card" hidden>
                                <div class="card-title requirement-brief-titlebar">
                                    <span>规划证据快照</span>
                                    <span class="card-badge ai-requirement-confidence" id="ai-requirement-confidence"></span>
                                </div>
                                <div class="ai-requirement-brief is-empty" id="ai-result-requirement-brief"></div>
                            </div>
                            <div class="overview">
                                <div class="card-title">构建结果原始摘要</div>
                                <div class="ai-explanation" id="ai-result-summary">--</div>
                            </div>
                            <div class="stage-timeline-card" id="ai-result-stage-timeline-card" hidden>
                                <div class="card-title stage-timeline-titlebar">
                                    <span>构建阶段证据</span>
                                    <span class="card-badge" id="ai-stage-timeline-summary"></span>
                                </div>
                                <div class="ai-stage-timeline" id="ai-result-stage-timeline"></div>
                            </div>
                            <div class="ops-list">
                                <div class="card-title">完整草稿算子</div>
                                <div class="generated-ops-list" id="ai-result-ops"></div>
                            </div>
                            <div class="attachment-card" id="ai-result-attachment-card" hidden>
                                <div class="card-title">附件与能力元数据</div>
                                <div class="ai-attachment-panel" id="ai-result-attachments"></div>
                            </div>
                            <div class="prompt-trace-card" id="ai-result-prompt-trace-card" hidden>
                                <div class="card-title prompt-trace-titlebar">
                                    <span>公开调试摘要</span>
                                    <button class="ai-trace-toggle-btn" id="ai-trace-toggle" type="button">切换视图</button>
                                </div>
                                <div class="ai-prompt-trace" id="ai-result-prompt-trace"></div>
                            </div>
                        </div>
                    </details>
                </div>
            </div>
        </div>
    `;
}

export function installAiPanelBuildPresentation(prototype) {
    if (!prototype || prototype._renderBuildPresentation?.__aiBuildPresentationV3) return;
    const render = function () {
        return renderAiBuildPresentation(this);
    };
    render.__aiBuildPresentationV3 = true;
    prototype._renderBuildPresentation = render;
}

export const aiPanelBuildPresentationTestApi = {
    classifyCheck,
    deriveDiff,
    countPendingFields
};
