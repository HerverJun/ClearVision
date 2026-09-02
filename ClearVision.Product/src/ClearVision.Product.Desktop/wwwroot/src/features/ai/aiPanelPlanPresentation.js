import {
    bindAiClarificationInteractions,
    deriveAiClarificationPresentation,
    renderAiClarification
} from './aiPanelClarificationPresentation.js';
import { normalizeAiPrimaryTask } from './aiTaskContract.js';

const UNDERSTANDING_FIELDS = Object.freeze([
    { field: 'inspection_object', label: '检测对象', semantic: 'inspectionObject' },
    { field: 'task_type', label: '任务类型', semantic: 'taskType', format: 'taskType' },
    { field: 'image_source', label: '图像来源', semantic: 'imageSource' },
    { field: 'defect_type', fallbackField: 'target_attribute', label: '检测目标 / 缺陷', semantic: 'defectType', fallbackSemantic: 'targetAttribute' },
    { field: 'acceptance_criteria', label: 'OK / NG 判定', format: 'acceptance' },
    { field: 'output_target', label: '输出目标', semantic: 'outputTarget' }
]);

const PLAN_OPERATOR_LABELS = Object.freeze({
    RoiManager: 'ROI 管理'
});

function clean(value) {
    return String(value ?? '').trim();
}

function asArray(value) {
    return Array.isArray(value) ? value : [];
}

function escapeHtml(panel, value) {
    return panel?._escapeHtml?.(clean(value)) ?? clean(value);
}

function localize(panel, value) {
    return clean(panel?._localizeDisplayText?.(clean(value)) || value);
}

function formatPlanCopy(panel, value) {
    const replacements = {
        industrial_camera: '工业相机',
        image_folder: '图片目录',
        ok_ng: 'OK/NG',
        structured_result: '结构化结果'
    };
    return localize(panel, value).replace(/\b(industrial_camera|image_folder|ok_ng|structured_result)\b/g, token => replacements[token] || token);
}

function formatAnswerValue(panel, plan, field, value) {
    const raw = clean(value);
    if (!raw) return '';
    const question = asArray(plan?.questions).find(candidate =>
        panel?._inferPlanQuestionFieldForQuestion?.(candidate, plan) === field
    );
    const option = asArray(question?.options).find(candidate => clean(candidate?.value) === raw);
    return clean(option?.label) || formatPlanCopy(panel, raw);
}

function readAnswer(state, field, fallbackField = '') {
    const confirmed = state?.answers?.confirmedByField?.[field] ||
        (fallbackField ? state?.answers?.confirmedByField?.[fallbackField] : null);
    const optimistic = state?.answers?.optimisticByField?.[field] ||
        (fallbackField ? state?.answers?.optimisticByField?.[fallbackField] : null);
    return { confirmed, optimistic };
}

function readSemanticValue(panel, semantic, definition) {
    if (definition.format === 'acceptance') {
        const values = [
            clean(semantic?.okCondition) ? `OK：${clean(semantic.okCondition)}` : '',
            clean(semantic?.ngCondition) ? `NG：${clean(semantic.ngCondition)}` : ''
        ].filter(Boolean);
        return values.join('；');
    }
    const raw = semantic?.[definition.semantic] ?? semantic?.[definition.fallbackSemantic];
    if (['unknown', 'unspecified', 'pending', 'not_set'].includes(clean(raw).toLowerCase())) return '';
    if (definition.format === 'taskType') {
        const canonical = normalizeAiPrimaryTask(raw);
        if (!canonical) return '';
        return clean(panel?._formatRequirementTaskTypeLabel?.(canonical) || localize(panel, canonical));
    }
    return localize(panel, raw);
}

function readUnderstandingItem(panel, plan, definition) {
    const state = panel?.agentWorkspaceState;
    const semantic = plan?.semanticExtraction || {};
    const { confirmed, optimistic } = readAnswer(state, definition.field, definition.fallbackField);
    const rawAnswer = optimistic || confirmed;
    const canonicalAnswer = definition.format === 'taskType'
        ? normalizeAiPrimaryTask(rawAnswer?.value)
        : clean(rawAnswer?.value);
    const answer = rawAnswer && canonicalAnswer
        ? { ...rawAnswer, value: canonicalAnswer }
        : null;
    const semanticValue = readSemanticValue(panel, semantic, definition);
    const value = clean(answer?.value)
        ? formatAnswerValue(panel, plan, definition.field, answer.value)
        : semanticValue || '待确认';
    const status = answer && optimistic
        ? 'confirming'
        : answer && confirmed
            ? 'confirmed'
            : semanticValue
                ? 'inferred'
                : 'pending';
    const statusText = status === 'confirmed'
        ? '已确认'
        : status === 'confirming'
            ? '确认中'
            : status === 'inferred'
                ? '模型推断'
                : '待确认';
    return { ...definition, value: localize(panel, value), status, statusText };
}

function readBlockingRisks(panel) {
    const blockers = asArray(panel?.agentWorkspaceState?.projection?.readiness?.blockers);
    const seen = new Set();
    return blockers.filter(blocker => {
        if (blocker?.blocksBuild !== true) return false;
        const resolutionMode = clean(blocker?.resolutionMode).toLowerCase();
        const category = clean(blocker?.category).toLowerCase();
        return resolutionMode !== 'answer_question' && category !== 'hard_requirement';
    }).map(blocker => {
        const rawText = clean(blocker?.publicLabel || blocker?.title || blocker?.message);
        const field = clean(blocker?.field);
        const text = rawText && rawText !== field
            ? rawText
            : clean(panel?._formatRequirementFieldLabel?.(field) || field || '构建条件待确认');
        if (!text || seen.has(text)) return null;
        seen.add(text);
        return text;
    }).filter(Boolean);
}

export function deriveAiPlanPresentation(panel, plan) {
    const clarification = deriveAiClarificationPresentation(panel, plan);
    const routeOperators = asArray(plan?.route?.operators).map(operator => ({
        raw: clean(operator),
        label: PLAN_OPERATOR_LABELS[clean(operator)] || clean(panel?._formatOperatorType?.(operator) || localize(panel, operator))
    })).filter(operator => operator.label);
    const steps = asArray(plan?.steps).map(clean).filter(Boolean);
    return {
        understanding: UNDERSTANDING_FIELDS.map(definition => readUnderstandingItem(panel, plan, definition)),
        route: {
            title: clean(plan?.route?.title) || '推荐视觉方案',
            summary: clean(plan?.route?.summary) || clean(plan?.nextAction),
            operators: routeOperators,
            steps
        },
        assumptions: asArray(plan?.assumptions).map(item => formatPlanCopy(panel, item)).filter(Boolean),
        acceptanceCriteria: asArray(plan?.acceptanceCriteria).map(item => formatPlanCopy(panel, item)).filter(Boolean),
        blockingRisks: readBlockingRisks(panel),
        nonBlockingRisks: asArray(plan?.risks).map(item => formatPlanCopy(panel, item)).filter(Boolean),
        clarification,
        canViewDraft: panel?._canViewBuildWorkspace?.() === true,
        readOnly: panel?._isPlanSnapshotReadOnly?.() === true,
        requirementMode: clean(panel?.requirementMode || plan?.requirementMode || 'strict').toLowerCase() === 'draft' ? 'draft' : 'strict'
    };
}

function renderUnderstanding(panel, items) {
    return `
        <section class="ai-plan-v2-section ai-plan-v2-understanding" data-ai-hook="plan-understanding">
            <div class="ai-plan-v2-section-heading">
                <span>任务理解</span>
                <strong>AI 理解成了什么</strong>
            </div>
            <dl class="ai-plan-v2-definition-list">
                ${items.map(item => `
                    <div class="is-${item.status}">
                        <dt>${escapeHtml(panel, item.label)}</dt>
                        <dd>${escapeHtml(panel, item.value)}</dd>
                        <small>${escapeHtml(panel, item.statusText)}</small>
                    </div>
                `).join('')}
            </dl>
        </section>
    `;
}

function renderRoute(panel, route, presentation) {
    const sequence = route.operators.length ? route.operators.map(item => item.label) : route.steps;
    return `
        <section class="ai-plan-v2-section ai-plan-v2-recommendation" data-ai-hook="plan-recommendation">
            <div class="ai-plan-v2-section-heading">
                <span>推荐方案</span>
                <strong>${escapeHtml(panel, route.title)}</strong>
            </div>
            ${route.summary ? `<p class="ai-plan-v2-summary">${escapeHtml(panel, route.summary)}</p>` : ''}
            ${sequence.length ? `
                <ol class="ai-plan-v2-sequence" aria-label="推荐处理步骤">
                    ${sequence.map((item, index) => `<li><span>${index + 1}</span><strong>${escapeHtml(panel, item)}</strong></li>`).join('')}
                </ol>
            ` : '<p class="ai-plan-v2-muted">当前方案尚未提供可靠的处理步骤。</p>'}
            ${renderDecisionDetails(panel, presentation)}
        </section>
    `;
}

function renderDecisionDetails(panel, presentation) {
    return `
        <div class="ai-plan-v2-decision-grid">
            <section>
                <h4>关键假设</h4>
                ${presentation.assumptions.length
                    ? `<ul>${presentation.assumptions.map(item => `<li>${escapeHtml(panel, item)}</li>`).join('')}</ul>`
                    : '<p class="ai-plan-v2-muted">暂无额外假设。</p>'}
            </section>
            <section>
                <h4>验收标准</h4>
                ${presentation.acceptanceCriteria.length
                    ? `<ul>${presentation.acceptanceCriteria.map(item => `<li>${escapeHtml(panel, item)}</li>`).join('')}</ul>`
                    : '<p class="ai-plan-v2-muted">待补充验收标准。</p>'}
            </section>
        </div>
    `;
}

function renderEngineeringDetails(panel, plan, presentation) {
    const diagnostics = panel?._renderPlannerFailureDiagnostics?.(plan) || '';
    const rawDiagnostics = panel?._renderPlanRawDiagnostics?.(plan) || '';
    const details = [
        ['Planner 来源', panel?._formatPlanSource?.(plan.planSource) || plan.planSource],
        ['置信度', plan.confidence],
        ['算子目录版本', plan.operatorCatalogVersion],
        ['模板目录版本', plan.templateCatalogVersion],
        ['Station 边界', plan.stationBoundarySummary],
        ['Fallback / Repair', plan.fallbackReason || asArray(plan.contractRepairNotes).join('；')]
    ].filter(([, value]) => clean(value));
    return `
        <details class="ai-plan-v2-details" data-ai-hook="plan-engineering-details">
            <summary>风险与工程详情</summary>
            <div class="ai-plan-v2-details-body">
                ${presentation.blockingRisks.length ? `
                    <section class="ai-plan-v2-detail-section" data-ai-hook="plan-risks">
                        <h4>构建阻断风险</h4>
                        <ul class="ai-plan-v2-blocking-risks">${presentation.blockingRisks.map(item => `<li>${escapeHtml(panel, item)}</li>`).join('')}</ul>
                    </section>
                ` : ''}
                ${presentation.nonBlockingRisks.length ? `
                    <section class="ai-plan-v2-detail-section" data-ai-hook="plan-non-blocking-risks">
                        <h4>非阻断风险</h4>
                        <ul>${presentation.nonBlockingRisks.map(item => `<li>${escapeHtml(panel, item)}</li>`).join('')}</ul>
                    </section>
                ` : ''}
                ${details.length ? `<dl>${details.map(([label, value]) => `<div><dt>${escapeHtml(panel, label)}</dt><dd>${escapeHtml(panel, value)}</dd></div>`).join('')}</dl>` : ''}
                ${diagnostics}
                ${rawDiagnostics}
            </div>
        </details>
    `;
}

function renderModeControl(panel, presentation) {
    return `
        <div class="ai-plan-v2-mode" data-ai-hook="plan-mode">
            <div>
                <strong>构建确认方式</strong>
                <span>${presentation.requirementMode === 'draft' ? '先生成可编辑草稿，不代表可部署。' : '关键决策确认后再进入构建。'}</span>
            </div>
            <div role="group" aria-label="构建确认方式">
                <button type="button" data-requirement-mode="strict" aria-pressed="${presentation.requirementMode === 'strict'}" ${presentation.readOnly ? 'disabled' : ''}>确认完整后构建</button>
                <button type="button" data-requirement-mode="draft" aria-pressed="${presentation.requirementMode === 'draft'}" ${presentation.readOnly ? 'disabled' : ''}>先生成可编辑草稿</button>
            </div>
        </div>
    `;
}

function renderEmptyPlan(panel) {
    const progress = panel?._getPlanRunProgressState?.();
    const phases = [
        ['understand', '理解需求'],
        ['context', '整理工程上下文'],
        ['generate', '生成方案'],
        ['validate', '校验方案']
    ].map(([key, label]) => ({
        key,
        label,
        status: clean(progress?.phases?.[key]?.status || 'waiting').toLowerCase(),
        summary: clean(progress?.phases?.[key]?.summary)
    }));
    const statusLabel = status => ({
        running: '进行中',
        completed: '已完成',
        failed: '失败',
        timeout: '超时',
        cancelled: '已取消',
        canceled: '已取消',
        warning: '需注意',
        waiting: '等待中',
        pending: '等待中'
    }[status] || '等待中');
    const lifecycleStatus = clean(progress?.status || 'idle').toLowerCase();
    const statusPresentation = ({
        running: { label: '处理中', tone: 'running' },
        completed: { label: '规划完成', tone: 'completed' },
        failed: { label: '规划失败', tone: 'failed' },
        timeout: { label: '等待超时', tone: 'timeout' },
        cancelled: { label: '已取消', tone: 'cancelled' },
        canceled: { label: '已取消', tone: 'cancelled' },
        idle: { label: '等待需求', tone: 'waiting' }
    }[lifecycleStatus] || { label: '等待中', tone: 'waiting' });
    const current = clean(progress?.currentLabel) || '发送需求后，这里会立即显示可信的规划阶段。';
    const taskSummary = clean(
        panel?.lastPlanningRequestContext?.userMessage ||
        panel?.lastPlanningRequestContext?.description ||
        panel?.lastUserPrompt
    ) || '等待新的视觉任务';
    const activePhase = phases.find(phase => phase.status === 'running')
        || phases.find(phase => ['failed', 'timeout', 'cancelled', 'canceled'].includes(phase.status))
        || phases.find(phase => phase.status === 'waiting' || phase.status === 'pending')
        || phases[phases.length - 1];
    const eventCount = Number(progress?.eventCount || 0);
    const sourceText = eventCount > 0
        ? `已接收 ${eventCount} 条 Plan Run 公开事件，阶段状态由真实事件更新。`
        : lifecycleStatus === 'running'
            ? '尚未收到 Plan Run 流式事件；当前只显示真实的处理中状态。'
            : '未收到流式事件，不会补写或推断已完成进度。';
    const detailText = progress?.slow
        ? '响应时间较长，请求仍在运行。你可以继续等待，也可以取消后重试。'
        : lifecycleStatus === 'running'
            ? `当前聚焦“${activePhase.label}”。未收到真实完成信号的阶段保持等待。`
            : current;
    return `
        <div class="ai-plan-v2-empty ai-planning-wait is-${escapeHtml(panel, statusPresentation.tone)}" data-ai-hook="planning-wait" data-planning-status="${escapeHtml(panel, lifecycleStatus)}" data-planning-event-count="${eventCount}" role="status" aria-live="polite">
            <div class="ai-planning-wait-heading">
                <div class="ai-planning-task-summary">
                    <span>规划进行中工作台</span>
                    <strong>${escapeHtml(panel, taskSummary)}</strong>
                    <p>${escapeHtml(panel, current)}</p>
                </div>
                <div class="ai-planning-status-block">
                    <span class="ai-planning-status-indicator" aria-hidden="true"></span>
                    <div>
                        <strong>${escapeHtml(panel, statusPresentation.label)}</strong>
                        <small>${progress?.slow ? '响应较慢，但仍在工作' : (eventCount ? `实时事件 ${eventCount}` : '等待真实事件')}</small>
                    </div>
                </div>
            </div>
            <div class="ai-planning-progress" aria-label="规划阶段进度">
                <div class="ai-planning-progress-line" aria-hidden="true"></div>
                <ol class="ai-planning-stages">
                    ${phases.map((phase, index) => `
                        <li class="is-${escapeHtml(panel, phase.status)}" data-planning-phase="${phase.key}">
                            <span class="ai-planning-stage-index">${index + 1}</span>
                            <div>
                                <strong>${escapeHtml(panel, phase.label)}</strong>
                                <small>${escapeHtml(panel, phase.summary || statusLabel(phase.status))}</small>
                            </div>
                            <b>${escapeHtml(panel, statusLabel(phase.status))}</b>
                        </li>
                    `).join('')}
                </ol>
            </div>
            <div class="ai-planning-work-grid">
                <section class="ai-planning-current-work" aria-label="当前工作详情">
                    <span>当前工作</span>
                    <strong>${escapeHtml(panel, activePhase.label)}</strong>
                    <p>${escapeHtml(panel, detailText)}</p>
                    <div class="ai-planning-processing-dots" aria-hidden="true"><i></i><i></i><i></i></div>
                </section>
                <aside class="ai-planning-truth-panel" aria-label="进度依据">
                    <span>进度依据</span>
                    <strong>${eventCount ? 'Plan Run 实时事件' : 'Router 请求状态'}</strong>
                    <p>${escapeHtml(panel, sourceText)}</p>
                    ${(progress?.canCancel || progress?.canRetry) ? `
                        <div class="ai-planning-actions">
                            ${progress?.canCancel ? '<button type="button" data-ai-action="planning-cancel">取消规划</button>' : ''}
                            ${progress?.canRetry ? '<button type="button" class="is-primary" data-ai-action="planning-retry">重试</button>' : ''}
                        </div>
                    ` : ''}
                </aside>
            </div>
            <p class="ai-planning-integrity-note">
                真实 Plan Run 事件到达后会接管同一条进度；等待、失败、超时和取消不会被显示为完成。
            </p>
        </div>
    `;
}

export function renderAiPlanWorkspace(panel, plan = panel?.pendingVisionPlan) {
    const root = panel?.container?.querySelector('#ai-plan-workspace');
    if (!root) return;
    root.hidden = panel?._getWorkspaceViewMode?.() !== 'plan';
    root.dataset.aiPlanPresentation = 'v2';
    panel?._renderPlanConfirmationGuidance?.(null, null);

    if (!plan) {
        root.innerHTML = renderEmptyPlan(panel);
        root.querySelector('[data-ai-action="planning-cancel"]')?.addEventListener('click', () =>
            panel?._handleCancelGenerate?.());
        root.querySelector('[data-ai-action="planning-retry"]')?.addEventListener('click', () =>
            panel?._retryPlanningLifecycle?.());
        panel?._updatePlanBuildActionState?.();
        return;
    }

    const presentation = deriveAiPlanPresentation(panel, plan);
    root.innerHTML = `
        <div class="ai-plan-v2" data-ai-hook="plan-workspace-v2">
            ${renderUnderstanding(panel, presentation.understanding)}
            ${renderRoute(panel, presentation.route, presentation)}
            ${renderAiClarification(panel, plan)}
            ${renderModeControl(panel, presentation)}
            ${renderEngineeringDetails(panel, plan, presentation)}
            <div class="ai-plan-v2-feedback" id="ai-plan-cta-feedback" role="status" aria-live="polite" hidden></div>
            <div class="ai-plan-actions">
                <span class="ai-plan-action-status" id="ai-plan-build-status"></span>
                <button class="ai-plan-action is-primary" type="button" id="ai-btn-start-build">开始构建</button>
                ${presentation.canViewDraft ? '<button class="ai-plan-v2-view-draft" type="button" data-ai-action="plan-view-draft">查看构建草稿</button>' : ''}
            </div>
        </div>
    `;

    bindAiClarificationInteractions(panel, root, plan);
    root.querySelectorAll('[data-requirement-mode]').forEach(button => {
        button.addEventListener('click', () => panel?._setRequirementMode?.(button.dataset.requirementMode || 'strict'));
    });
    root.querySelector('#ai-btn-start-build')?.addEventListener('click', event => panel?._startBuildFromCurrentPlan?.({
        acceptedRecommended: event.currentTarget?.dataset?.acceptRecommended === 'true'
    }));
    root.querySelector('[data-ai-action="plan-view-draft"]')?.addEventListener('click', () => panel?._handlePlanViewDraftClick?.());
    panel?._updatePlanBuildActionState?.();
}

export function installAiPanelPlanPresentation(prototype) {
    if (!prototype || prototype._renderPlanWorkspace?.__aiPlanPresentationV2) return;
    const render = function (plan = this.pendingVisionPlan) {
        return renderAiPlanWorkspace(this, plan);
    };
    render.__aiPlanPresentationV2 = true;
    prototype._renderPlanWorkspace = render;
}

export const aiPanelPlanPresentationTestApi = {
    UNDERSTANDING_FIELDS,
    readAnswer,
    readUnderstandingItem,
    renderEmptyPlan
};
