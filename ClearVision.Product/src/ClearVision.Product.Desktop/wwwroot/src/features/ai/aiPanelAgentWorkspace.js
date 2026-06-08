import httpClient from '../../core/messaging/httpClient.js';
import { AiWorkbenchStates } from './aiPanelWorkbench.js';

export const AgentWorkspaceModes = Object.freeze({
    PLAN: 'plan',
    BUILD: 'build'
});

const BUILD_STAGE_ORDER = [
    'understand_requirement',
    'context_collection',
    'plan_generation',
    'assumption_confirmation',
    'requirement_parsing',
    'resolve_build_intent',
    'template_strategy',
    'operator_pipeline',
    'parameter_mapping',
    'planner',
    'workflow_draft',
    'validate_schema',
    'metadata_dry_run',
    'readiness',
    'manifest_dry_run',
    'package_readiness',
    'station_compatibility',
    'operator_contract',
    'release_review',
    'repair_loop',
    'workflow_diff',
    'apply_gate',
    'artifact',
    'run'
];

const BUILD_STAGE_LABELS = {
    understand_requirement: '理解需求',
    context_collection: '收集上下文',
    plan_generation: '生成计划',
    assumption_confirmation: '确认假设',
    requirement_parsing: '需求归一',
    resolve_build_intent: '构建意图',
    template_strategy: '模板策略',
    operator_pipeline: '算子链',
    parameter_mapping: '参数映射',
    planner: '规划与工具',
    tool_policy: '工具策略',
    workflow_draft: '流程草稿',
    validate_schema: '结构校验',
    metadata_dry_run: '元数据预演',
    readiness: '就绪检查',
    manifest_dry_run: '运行包预演',
    package_readiness: '运行包就绪',
    station_compatibility: '工站兼容',
    operator_contract: '算子契约',
    release_review: '发布复核',
    repair_loop: '自动修复',
    workflow_diff: '流程差异',
    apply_gate: '应用门禁',
    artifact: '结果产物',
    run: '运行'
};

export const aiPanelAgentWorkspaceMixin = {
    _resetAgentWorkspace({ preservePlan = false } = {}) {
        this.activePlanRequestId = null;
        if (!preservePlan) {
            this.pendingVisionPlan = null;
            this.planQuestionSelections = {};
        }

        this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
    },

    _shouldOpenPlanModeBeforeBuild({ explicitMode = '', skipPlan = false } = {}) {
        if (skipPlan) return false;
        if (this.isGenerating) return false;

        const mode = String(explicitMode || '').trim().toLowerCase();
        return mode === '' || mode === 'auto' || mode === 'new';
    },

    _enterPlanModeFromPrompt({
        description,
        hint = '',
        userMessage = '',
        attachmentPaths = [],
        templateSelection = null,
        clearInput = true,
        input = null
    }) {
        const normalizedDescription = String(description || '').trim();
        if (!normalizedDescription) {
            this._addMessage('system', '请先输入检测目标，再进入规划模式。');
            return false;
        }

        this.lastUserPrompt = String(userMessage || normalizedDescription).trim();
        this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
        this.pendingVisionPlan = null;
        this.planQuestionSelections = {};
        const planRequestId = this._createPlanRequestId();
        this.activePlanRequestId = planRequestId;

        this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
        this._addMessage('user', userMessage || normalizedDescription);
        const turn = this._startAssistantTurn({
            activate: false,
            statusText: '规划中',
            statusTone: 'warning',
            openReply: true
        });
        this._setAssistantSectionText(
            turn,
            'reply',
            '规划模式正在收集公开工程上下文，并请求后端智能体生成结构化视觉工程计划。'
        );

        this._setResultStatusNote('规划模式正在等待后端智能体输出。', 'info');
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
        if (clearInput && input) {
            input.value = '';
            input.style.height = 'auto';
        }

        const planRequest = this._buildPlanModeRequest({
            description: normalizedDescription,
            hint,
            userMessage,
            attachmentPaths,
            templateSelection
        });
        this._requestBackendVisionPlan(planRequest)
            .then(result => {
                if (!this._isActivePlanRequest(planRequestId)) return;
                this.pendingVisionPlan = this._normalizeBackendPlanResult(result, normalizedDescription);
                this.planQuestionSelections = Object.fromEntries(
                    this.pendingVisionPlan.questions.map(question => [question.id, question.defaultValue])
                );
                this._clearActivePlanRequest(planRequestId);
                const sourceLabel = this.pendingVisionPlan.planSource === 'rule_fallback'
                    ? `规划已生成（规则兜底：${this.pendingVisionPlan.fallbackReason || 'rule_fallback'}）`
                    : '规划已生成（Planner）';
                this._setAssistantTurnStatus(turn, '规划完成', 'success');
                this._setAssistantSectionText(
                    turn,
                    'reply',
                    `${sourceLabel}。可以接受推荐默认值，也可以调整选项后再开始构建。`
                );
                this._setResultStatusNote('规划模式等待确认，确认后进入构建模式。', 'info');
                this._renderAgentWorkspaceOverview();
                this._renderPlanWorkspace(this.pendingVisionPlan);
            })
            .catch(error => {
                if (!this._isActivePlanRequest(planRequestId)) return;
                this._clearActivePlanRequest(planRequestId);
                this.pendingVisionPlan = null;
                this._setAssistantTurnStatus(turn, '规划失败', 'failed');
                this._setAssistantSectionText(
                    turn,
                    'reply',
                    `规划模式失败：${error?.message || String(error || '未知错误')}`
                );
                this._setResultStatusNote('规划模式失败，请检查后端连接后重试。', 'warning');
                this._renderAgentWorkspaceOverview();
                this._renderPlanWorkspace(null);
            });

        return true;
    },

    _createPlanRequestId() {
        const randomPart = Math.random().toString(36).slice(2, 8);
        return `plan-${Date.now()}-${randomPart}`;
    },

    _isActivePlanRequest(requestId) {
        return Boolean(this.activePlanRequestId) && requestId === this.activePlanRequestId;
    },

    _clearActivePlanRequest(requestId = null) {
        if (!requestId || this.activePlanRequestId === requestId) {
            this.activePlanRequestId = null;
        }
    },

    _buildPlanModeRequest({
        description,
        hint = '',
        userMessage = '',
        attachmentPaths = [],
        templateSelection = null
    }) {
        const normalizedTemplateSelection = this._normalizeTemplateSelection?.(templateSelection) || null;
        const currentFlowSnapshot = this._hasCurrentFlowContext?.()
            ? this._stringifyPlanSnapshot(this._getCurrentFlowJson?.())
            : null;
        return {
            description: String(description || '').trim(),
            originalUserPrompt: String(userMessage || description || '').trim(),
            additionalContext: String(hint || '').trim() || null,
            sessionId: this.sessionId || null,
            mode: 'plan',
            currentFlowSnapshot,
            currentResultSnapshot: this._buildCurrentResultPlanSnapshot(),
            templateSelection: normalizedTemplateSelection,
            attachmentSummary: this._buildPlanAttachmentSummary(attachmentPaths),
            historySummary: this._buildPlanHistorySummary()
        };
    },

    _buildPlanAttachmentSummary(attachmentPaths = []) {
        const explicitCount = Array.isArray(attachmentPaths) ? attachmentPaths.length : 0;
        const attachmentCount = explicitCount > 0
            ? explicitCount
            : (Array.isArray(this.attachments) ? this.attachments.length : 0);
        const resourceKinds = attachmentCount > 0
            ? ['user_attachment_metadata']
            : [];

        return {
            count: attachmentCount,
            resourceKinds,
            pathsRedacted: true
        };
    },

    _stringifyPlanSnapshot(value) {
        if (value === null || value === undefined) return null;
        if (typeof value === 'string') return value.trim() || null;
        try {
            return JSON.stringify(value);
        } catch {
            return null;
        }
    },

    _buildCurrentResultPlanSnapshot() {
        if (!this.currentResult) return null;
        const flow = this.currentResult.flow || this.currentResult.Flow || null;
        const pending = this.currentResult.pendingParameters || this.currentResult.PendingParameters || [];
        const missing = this.currentResult.missingResources || this.currentResult.MissingResources || [];
        const snapshot = {
            hasFlow: Boolean(flow),
            operatorCount: flow ? this._extractOperators(flow).length : 0,
            connectionCount: flow ? this._extractConnections(flow).length : 0,
            pendingParameterCount: Array.isArray(pending) ? pending.length : 0,
            missingResourceCount: Array.isArray(missing) ? missing.length : 0,
            generationMode: this.currentResult.generationMode || this.currentResult.GenerationMode || ''
        };
        try {
            return JSON.stringify(snapshot);
        } catch {
            return null;
        }
    },

    _buildPlanHistorySummary() {
        const items = Array.isArray(this.history) ? this.history.slice(0, 3) : [];
        if (!items.length) return null;
        return items
            .map(item => String(item.lastMessage || '').trim())
            .filter(Boolean)
            .slice(0, 3)
            .join(' / ') || null;
    },

    async _requestBackendVisionPlan(request) {
        return await httpClient.post('/ai/agent-plan', request);
    },

    _normalizeBackendPlanResult(result, fallbackDescription = '') {
        const plan = this._asObject?.(result) || result || {};
        const route = plan.recommendedRoute || plan.RecommendedRoute || {};
        const questions = plan.clarificationQuestions || plan.ClarificationQuestions || [];
        const defaults = plan.recommendedDefaults || plan.RecommendedDefaults || [];
        const contextSummary = plan.contextSummary || plan.ContextSummary || {};
        const templateSelection = this._normalizeTemplateSelection?.(plan.templateSelection || plan.TemplateSelection) ||
            this._normalizeTemplateSelection?.({
                mode: contextSummary.templateSelectionMode || contextSummary.TemplateSelectionMode || '',
                templateId: contextSummary.templateId || contextSummary.TemplateId || ''
            }) ||
            null;
        const normalizedQuestions = Array.isArray(questions)
            ? questions.map(question => this._normalizePlanQuestion(question)).filter(Boolean)
            : [];
        const normalizedDefaults = Array.isArray(defaults)
            ? defaults.map(item => this._normalizePlanDefault(item)).filter(Boolean)
            : [];

        return {
            id: plan.planId || plan.PlanId || `plan-${Date.now()}`,
            planId: plan.planId || plan.PlanId || '',
            planHash: String(plan.planHash || plan.PlanHash || '').trim(),
            mode: AgentWorkspaceModes.PLAN,
            originalDescription: plan.originalUserPrompt || plan.OriginalUserPrompt || fallbackDescription,
            buildPrompt: plan.originalUserPrompt || plan.OriginalUserPrompt || fallbackDescription,
            goal: plan.goal || plan.Goal || fallbackDescription || '视觉流程草稿',
            intent: plan.intent || plan.Intent || '',
            confidence: plan.confidence || plan.Confidence || 'medium',
            planSource: plan.planSource || plan.PlanSource || '',
            fallbackReason: plan.fallbackReason || plan.FallbackReason || '',
            planWarnings: this._toArray(plan.planWarnings || plan.PlanWarnings),
            contractRepairNotes: this._toArray(plan.contractRepairNotes || plan.ContractRepairNotes),
            publicEvents: this._toArray(plan.publicEvents || plan.PublicEvents)
                .map(evt => this._normalizePlanPublicEvent(evt)),
            blockerCount: this._toArray(plan.blockingReasons || plan.BlockingReasons).length,
            nextAction: plan.nextAction || plan.NextAction || '复核计划后开始构建。',
            executable: Boolean(plan.canBuild ?? plan.CanBuild ?? true),
            blockingReasons: this._toArray(plan.blockingReasons || plan.BlockingReasons),
            understanding: this._toArray(plan.requirementUnderstanding || plan.RequirementUnderstanding).length
                ? this._toArray(plan.requirementUnderstanding || plan.RequirementUnderstanding)
                : [`用户目标：${fallbackDescription || '视觉流程草稿'}`],
            route: {
                routeId: route.routeId || route.RouteId || '',
                title: route.title || route.Title || '视觉方案路线',
                summary: route.summary || route.Summary || '',
                operators: this._toArray(route.operators || route.Operators),
                templateDecision: route.templateDecision || route.TemplateDecision || ''
            },
            questions: normalizedQuestions,
            assumptions: normalizedDefaults.length
                ? normalizedDefaults.map(item => `${item.label}: ${item.value}${item.impact ? ` (${item.impact})` : ''}`)
                : ['保留公开元数据边界，缺失资源在确认前保持为待补项。'],
            recommendedDefaults: normalizedDefaults,
            steps: this._toArray(plan.executablePlan || plan.ExecutablePlan),
            risks: this._toArray(plan.risks || plan.Risks),
            acceptanceCriteria: this._toArray(plan.acceptanceCriteria || plan.AcceptanceCriteria),
            contextSummary,
            operatorCatalogVersion: plan.operatorCatalogVersion || plan.OperatorCatalogVersion || '',
            templateCatalogVersion: plan.templateCatalogVersion || plan.TemplateCatalogVersion || '',
            templateSelection,
            stationBoundarySummary: plan.stationBoundarySummary || plan.StationBoundarySummary || '',
            plcOutputPolicy: plan.plcOutputPolicy || plan.PlcOutputPolicy || '',
            rawPlanSnapshot: plan
        };
    },

    _normalizePlanPublicEvent(evt) {
        const item = this._asObject?.(evt) || evt || {};
        return {
            stage: item.stage || item.Stage || '',
            status: item.status || item.Status || '',
            title: item.title || item.Title || '',
            summary: item.summary || item.Summary || ''
        };
    },

    _normalizePlanQuestion(question) {
        if (!question) return null;
        const options = this._toArray(question.options || question.Options)
            .map(option => this._normalizePlanOption(option))
            .filter(Boolean);
        return {
            id: question.id || question.Id || '',
            title: question.title || question.Title || '',
            why: question.why || question.Why || '',
            defaultValue: question.defaultValue || question.DefaultValue || options.find(item => item.recommended)?.value || options[0]?.value || '',
            defaultAssumption: question.defaultAssumption || question.DefaultAssumption || '',
            impact: question.impact || question.Impact || '',
            options
        };
    },

    _normalizePlanOption(option) {
        if (!option) return null;
        return {
            value: option.value || option.Value || '',
            label: option.label || option.Label || option.value || option.Value || '',
            recommended: Boolean(option.recommended ?? option.Recommended),
            description: option.description || option.Description || '',
            impact: option.impact || option.Impact || ''
        };
    },

    _normalizePlanDefault(item) {
        if (!item) return null;
        return {
            id: item.id || item.Id || '',
            label: item.label || item.Label || '',
            value: item.value || item.Value || '',
            impact: item.impact || item.Impact || ''
        };
    },

    _toArray(value) {
        return Array.isArray(value) ? value : [];
    },

    _renderAgentWorkspaceOverview() {
        const el = this.container?.querySelector('#ai-agent-workspace-overview');
        if (!el) return;

        const plan = this.pendingVisionPlan;
        const mode = this._formatWorkspaceModeLabel();
        const activeEvents = Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [];
        const planEvents = Array.isArray(plan?.publicEvents) ? plan.publicEvents : [];
        const terminal = activeEvents.find(evt => ['run.completed', 'run.failed', 'run.cancelled'].includes(evt.eventType));
        const lastEvent = activeEvents[activeEvents.length - 1];
        const blockerCount = this._countBuildBlockers(activeEvents);
        const goal = plan?.goal || this.lastUserPrompt || '描述视觉检测目标后开始规划。';
        const confidence = this._formatWorkspaceValue(plan?.confidence || (activeEvents.length ? '事件驱动' : '未设置'));
        const nextAction = terminal
            ? (terminal.eventType === 'run.completed' ? '复核流程草稿，可应用到画布继续编辑。' : '查看首要修复建议后重试构建。')
            : this.agentWorkspaceMode === AgentWorkspaceModes.BUILD
                ? (lastEvent?.summary || '等待下一条后端公开事件。')
                : (plan?.nextAction || '规划模式只提出高价值工程问题。');
        const executable = this.agentWorkspaceMode === AgentWorkspaceModes.BUILD
            ? activeEvents.length > 0
            : Boolean(plan?.executable);
        const source = this._formatWorkspaceValue(plan?.planSource || (activeEvents.length ? '构建事件' : '未设置'));

        el.innerHTML = `
            <section class="ai-agent-overview-card is-${this._escapeHtml(this.agentWorkspaceMode || AgentWorkspaceModes.PLAN)}">
                <div class="ai-agent-overview-main">
                    <span class="ai-agent-overview-kicker">${this._escapeHtml(mode)}</span>
                    <strong>${this._escapeHtml(goal)}</strong>
                    <span>${this._escapeHtml(nextAction)}</span>
                    <em>可构建：${executable ? '是' : '否'}</em>
                </div>
                <div class="ai-agent-overview-metrics">
                    <span><small>置信度</small><b>${this._escapeHtml(confidence)}</b></span>
                    <span><small>来源</small><b>${this._escapeHtml(source)}</b></span>
                    <span><small>阻断项</small><b>${this._escapeHtml(String(blockerCount))}</b></span>
                    <span><small>事件数</small><b>${this._escapeHtml(String(activeEvents.length || planEvents.length))}</b></span>
                </div>
            </section>
        `;
    },

    _formatWorkspaceModeLabel() {
        if (this.workbenchState === AiWorkbenchStates.APPLIED) return '已应用';
        if (this.workbenchState === AiWorkbenchStates.READY_TO_APPLY) return '可应用';
        return this.agentWorkspaceMode === AgentWorkspaceModes.BUILD ? '构建模式' : '规划模式';
    },

    _formatWorkspaceValue(value) {
        const text = String(value || '').trim();
        if (!text || text === 'not set' || text === 'unknown') return '未设置';
        if (text === 'event-backed') return '事件驱动';
        if (text === 'build') return '构建事件';
        return text;
    },

    _formatBuildStatus(status) {
        switch (String(status || '').trim().toLowerCase()) {
            case 'running':
                return '执行中';
            case 'completed':
                return '已完成';
            case 'blocked':
                return '已阻断';
            case 'failed':
                return '失败';
            case 'cancelled':
            case 'canceled':
                return '已取消';
            case 'pending':
                return '等待中';
            default:
                return status || '已记录';
        }
    },

    _formatGateStatus(status) {
        switch (String(status || '').trim().toLowerCase()) {
            case 'canvas_apply_ready':
                return '画布可应用';
            case 'runtime_draft_ready':
                return '运行草稿就绪';
            case 'deployment_ready':
                return '部署就绪';
            case 'blocked':
                return '已阻断';
            default:
                return status || '未设置';
        }
    },

    _renderPlanWorkspace(plan = this.pendingVisionPlan) {
        const el = this.container?.querySelector('#ai-plan-workspace');
        if (!el) return;

        el.hidden = this.agentWorkspaceMode === AgentWorkspaceModes.BUILD;
        if (!plan) {
            el.innerHTML = `
                <div class="ai-plan-empty">
                    <div class="ai-plan-empty-title">规划模式</div>
                    <div class="ai-plan-empty-copy">正在收集工程上下文。请输入检测目标，智能体会先形成视觉工程计划，再进入构建。</div>
                </div>
            `;
            return;
        }

        const selections = this.planQuestionSelections || {};
        el.innerHTML = `
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">需求理解</div>
                <div class="ai-workspace-list">${plan.understanding.map(item => `<div>${this._escapeHtml(item)}</div>`).join('')}</div>
            </section>
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">推荐方案</div>
                <div class="ai-plan-route">
                    <strong>${this._escapeHtml(plan.route.title)}</strong>
                    <span>${this._escapeHtml(plan.route.summary)}</span>
                    <div class="ai-plan-chain">${plan.route.operators.map(op => `<span>${this._escapeHtml(op)}</span>`).join('')}</div>
                </div>
            </section>
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">规划诊断</div>
                <div class="ai-build-compact">
                    <div class="ai-build-compact-row">
                        <b>${this._escapeHtml(this._formatWorkspaceValue(plan.planSource))}</b>
                        <span>${this._escapeHtml(plan.planHash || '计划哈希待生成')}</span>
                    </div>
                    ${plan.fallbackReason ? `<div class="ai-build-note">${this._escapeHtml(plan.fallbackReason)}</div>` : ''}
                    ${plan.planWarnings.length ? `<ul>${plan.planWarnings.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>` : ''}
                    ${plan.contractRepairNotes.length ? `<div class="ai-plan-chain">${plan.contractRepairNotes.map(item => `<span>${this._escapeHtml(item)}</span>`).join('')}</div>` : ''}
                    ${plan.publicEvents.length ? `<div class="ai-workspace-list">${plan.publicEvents.map(evt => `<div><b>${this._escapeHtml(evt.stage)}</b> ${this._escapeHtml(evt.status)} - ${this._escapeHtml(evt.summary || evt.title)}</div>`).join('')}</div>` : ''}
                </div>
            </section>
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">关键问题</div>
                <div class="ai-plan-question-list">
                    ${plan.questions.map(question => this._renderPlanQuestion(question, selections[question.id])).join('')}
                </div>
            </section>
            <section class="ai-workspace-section ai-workspace-grid-2">
                <div>
                    <div class="ai-workspace-section-title">推荐默认值</div>
                    <ul>${plan.assumptions.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>
                </div>
                <div>
                    <div class="ai-workspace-section-title">风险</div>
                    <ul>${plan.risks.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>
                </div>
            </section>
            <section class="ai-workspace-section ai-workspace-grid-2">
                <div>
                    <div class="ai-workspace-section-title">可执行计划</div>
                    <ol>${plan.steps.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ol>
                </div>
                <div>
                    <div class="ai-workspace-section-title">验收标准</div>
                    <ol>${plan.acceptanceCriteria.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ol>
                </div>
            </section>
            <div class="ai-plan-actions">
                <button class="ai-plan-action is-primary" type="button" id="ai-btn-accept-plan">按推荐方案开始构建</button>
                <button class="ai-plan-action" type="button" id="ai-btn-start-build">开始构建</button>
            </div>
        `;

        el.querySelectorAll('[data-plan-question-option]').forEach(button => {
            button.addEventListener('click', () => {
                this._selectPlanQuestionOption(
                    button.getAttribute('data-plan-question') || '',
                    button.getAttribute('data-plan-question-option') || ''
                );
            });
        });
        el.querySelector('#ai-btn-accept-plan')?.addEventListener('click', () => this._acceptRecommendedPlanAndBuild());
        el.querySelector('#ai-btn-start-build')?.addEventListener('click', () => this._startBuildFromCurrentPlan());
    },

    _renderPlanQuestion(question, selectedValue) {
        return `
            <article class="ai-plan-question">
                <div class="ai-plan-question-head">
                    <strong>${this._escapeHtml(question.title)}</strong>
                    <span>${this._escapeHtml(question.why)}</span>
                </div>
                <div class="ai-plan-question-default">
                    <b>默认假设</b>
                    <span>${this._escapeHtml(question.defaultAssumption)}</span>
                </div>
                <div class="ai-plan-question-options">
                    ${question.options.map(option => {
                        const selected = String(selectedValue || question.defaultValue) === option.value;
                        return `
                            <button
                                class="ai-plan-option ${selected ? 'is-selected' : ''} ${option.recommended ? 'is-recommended' : ''}"
                                type="button"
                                data-plan-question="${this._escapeHtml(question.id)}"
                                data-plan-question-option="${this._escapeHtml(option.value)}"
                                aria-pressed="${selected ? 'true' : 'false'}">
                                <span>${this._escapeHtml(option.label)}${option.recommended ? '（推荐）' : ''}</span>
                                <small>${this._escapeHtml(option.description)}</small>
                                <em>${this._escapeHtml(option.impact)}</em>
                            </button>
                        `;
                    }).join('')}
                </div>
                <div class="ai-plan-question-impact">${this._escapeHtml(question.impact)}</div>
            </article>
        `;
    },

    _selectPlanQuestionOption(questionId, value) {
        if (!questionId || !value || !this.pendingVisionPlan) return;
        this.planQuestionSelections = {
            ...(this.planQuestionSelections || {}),
            [questionId]: value
        };
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderAgentWorkspaceOverview();
    },

    _acceptRecommendedPlanAndBuild() {
        if (!this.pendingVisionPlan) return;
        this.planQuestionSelections = Object.fromEntries(
            this.pendingVisionPlan.questions.map(question => [
                question.id,
                question.options.find(option => option.recommended)?.value || question.defaultValue
            ])
        );
        this._startBuildFromCurrentPlan({ acceptedRecommended: true });
    },

    _startBuildFromCurrentPlan({ acceptedRecommended = false } = {}) {
        if (this.isGenerating || !this.pendingVisionPlan) return false;

        const plan = this.pendingVisionPlan;
        this.activePlanRequestId = null;
        const buildFromPlan = this._buildStructuredBuildFromPlanRequest(plan, { acceptedRecommended });
        this.agentWorkspaceMode = AgentWorkspaceModes.BUILD;
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(plan);
        this._renderBuildWorkspaceFromAgentRun();
        this._setResultStatusNote('构建模式已启动，进度来自后端 AgentRun 公开事件。', 'info');

        return this._dispatchGenerateRequest({
            description: plan.buildPrompt || plan.originalDescription,
            hint: '',
            userMessage: `从计划开始构建：${plan.goal}`,
            attachmentPaths: [],
            existingFlowJson: buildFromPlan.currentFlowSnapshot || null,
            explicitMode: buildFromPlan.buildIntent || 'new',
            templateSelection: buildFromPlan.templateSelection || null,
            clearInput: true,
            skipPlan: true,
            buildFromPlan
        });
    },

    _buildStructuredBuildFromPlanRequest(plan, { acceptedRecommended = false } = {}) {
        const currentFlowSnapshot = this._hasCurrentFlowContext?.()
            ? this._stringifyPlanSnapshot(this._getCurrentFlowJson?.())
            : null;
        const buildIntent = this._resolvePlanBuildIntent(plan, currentFlowSnapshot);
        const templateSelection = this._resolveBuildTemplateSelection(plan);
        const planHash = String(
            plan?.planHash ||
            plan?.rawPlanSnapshot?.planHash ||
            plan?.rawPlanSnapshot?.PlanHash ||
            ''
        ).trim();

        return {
            planId: plan.planId || plan.id || '',
            planHash,
            planSnapshot: this._buildPlanSnapshotForBuild(plan),
            userSelections: this._buildPlanSelectionMap(plan),
            acceptedDefaults: this._collectAcceptedDefaultIds(plan, acceptedRecommended),
            currentFlowSnapshot,
            templateSelection,
            attachmentSummary: this._buildPlanAttachmentSummary([]),
            operatorCatalogVersion: plan.operatorCatalogVersion || '',
            stationBoundarySummary: plan.stationBoundarySummary || '',
            plcOutputPolicy: plan.plcOutputPolicy || '',
            buildIntent,
            originalUserPrompt: plan.originalDescription || plan.buildPrompt || '',
            acceptedRecommendedDefaults: Boolean(acceptedRecommended),
            metadataOnly: true
        };
    },

    _resolveBuildTemplateSelection(plan) {
        const contextSummary = plan?.contextSummary || plan?.rawPlanSnapshot?.contextSummary || plan?.rawPlanSnapshot?.ContextSummary || {};
        const contextSelection = {
            mode: contextSummary.templateSelectionMode || contextSummary.TemplateSelectionMode || '',
            templateId: contextSummary.templateId || contextSummary.TemplateId || ''
        };
        const candidates = [
            plan?.templateSelection,
            plan?.rawPlanSnapshot?.templateSelection,
            plan?.rawPlanSnapshot?.TemplateSelection,
            this.nextTemplateSelection,
            contextSelection
        ];

        for (const candidate of candidates) {
            const normalized = this._normalizeTemplateSelection?.(candidate);
            if (normalized) return normalized;
        }

        return null;
    },

    _buildPlanSnapshotForBuild(plan) {
        if (plan?.rawPlanSnapshot) {
            const snapshot = { ...plan.rawPlanSnapshot };
            if (!snapshot.planHash && !snapshot.PlanHash && plan?.planHash) {
                snapshot.planHash = plan.planHash;
            }
            if (!snapshot.templateSelection && !snapshot.TemplateSelection && plan?.templateSelection) {
                snapshot.templateSelection = plan.templateSelection;
            }
            return snapshot;
        }
        return {
            planId: plan?.planId || plan?.id || '',
            planHash: plan?.planHash || '',
            planSource: plan?.planSource || '',
            fallbackReason: plan?.fallbackReason || '',
            originalUserPrompt: plan?.originalDescription || plan?.buildPrompt || '',
            goal: plan?.goal || '',
            intent: plan?.intent || '',
            confidence: plan?.confidence || 'medium',
            requirementUnderstanding: this._toArray(plan?.understanding),
            recommendedRoute: plan?.route || {},
            clarificationQuestions: this._toArray(plan?.questions),
            recommendedDefaults: this._toArray(plan?.recommendedDefaults),
            risks: this._toArray(plan?.risks),
            acceptanceCriteria: this._toArray(plan?.acceptanceCriteria),
            executablePlan: this._toArray(plan?.steps),
            canBuild: plan?.executable !== false,
            blockingReasons: this._toArray(plan?.blockingReasons),
            nextAction: plan?.nextAction || '',
            contextSummary: plan?.contextSummary || {},
            operatorCatalogVersion: plan?.operatorCatalogVersion || '',
            templateCatalogVersion: plan?.templateCatalogVersion || '',
            templateSelection: plan?.templateSelection || null,
            stationBoundarySummary: plan?.stationBoundarySummary || '',
            plcOutputPolicy: plan?.plcOutputPolicy || '',
            planWarnings: this._toArray(plan?.planWarnings),
            contractRepairNotes: this._toArray(plan?.contractRepairNotes),
            publicEvents: this._toArray(plan?.publicEvents),
            metadataOnly: true
        };
    },

    _buildPlanSelectionMap(plan) {
        const selections = this.planQuestionSelections || {};
        return Object.fromEntries(this._toArray(plan?.questions)
            .map(question => {
                const value = String(selections[question.id] || question.defaultValue || '').trim();
                return value ? [question.id, value] : null;
            })
            .filter(Boolean));
    },

    _collectAcceptedDefaultIds(plan, acceptedRecommended = false) {
        const selected = this._buildPlanSelectionMap(plan);
        return this._toArray(plan?.questions)
            .filter(question => {
                const recommended = question.options?.find(option => option.recommended)?.value || question.defaultValue;
                return acceptedRecommended || String(selected[question.id] || '') === String(recommended || '');
            })
            .map(question => question.id)
            .filter(Boolean);
    },

    _resolvePlanBuildIntent(plan, currentFlowSnapshot = null) {
        const prompt = plan?.originalDescription || plan?.buildPrompt || plan?.goal || '';
        const hasCurrentFlow = Boolean(currentFlowSnapshot);
        const resolved = this._resolveGenerateRequestMode?.('', prompt, hasCurrentFlow) || 'auto';
        return resolved === 'auto' ? 'new' : resolved;
    },

    _handleAgentRunWorkspaceEvent(evt) {
        if (!evt) return;
        this.agentWorkspaceMode = AgentWorkspaceModes.BUILD;
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
    },

    _renderBuildWorkspaceFromAgentRun() {
        const el = this.container?.querySelector('#ai-build-workspace');
        const timeline = this.container?.querySelector('#ai-build-event-timeline');
        const template = this.container?.querySelector('#ai-build-template-match');
        const chain = this.container?.querySelector('#ai-build-operator-chain');
        const parameters = this.container?.querySelector('#ai-build-parameters');
        const checks = this.container?.querySelector('#ai-build-checks');
        const finalDraft = this.container?.querySelector('#ai-build-final-draft');
        if (!el) return;

        el.hidden = this.agentWorkspaceMode !== AgentWorkspaceModes.BUILD;
        const events = Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [];
        if (!events.length) {
            if (timeline) {
                timeline.innerHTML = '<div class="ai-followup-empty">等待后端 AgentRun 公开事件。</div>';
            }
            return;
        }

        if (timeline) timeline.innerHTML = this._renderBuildTimeline(events);
        if (template) template.innerHTML = this._renderBuildTemplateSummary(events);
        if (chain) chain.innerHTML = this._renderBuildOperatorChain(events);
        if (parameters) parameters.innerHTML = this._renderBuildParameterSummary(events);
        if (checks) checks.innerHTML = this._renderBuildChecks(events);
        if (finalDraft) finalDraft.innerHTML = this._renderBuildFinalDraft(events);
    },

    _renderBuildTimeline(events) {
        const stageMap = new Map();
        events.forEach(evt => {
            const stage = evt.stage || 'run';
            const current = stageMap.get(stage) || {
                stage,
                status: 'pending',
                title: BUILD_STAGE_LABELS[stage] || stage,
                summary: '',
                sequence: 0
            };
            if (evt.sequence >= current.sequence) {
                current.status = evt.status || current.status;
                current.title = evt.title || current.title;
                current.summary = evt.summary || current.summary;
                current.sequence = evt.sequence;
                current.eventType = evt.eventType;
            }
            stageMap.set(stage, current);
        });

        const ordered = [...stageMap.values()].sort((a, b) => {
            const ai = BUILD_STAGE_ORDER.indexOf(a.stage);
            const bi = BUILD_STAGE_ORDER.indexOf(b.stage);
            return (ai < 0 ? 99 : ai) - (bi < 0 ? 99 : bi) || a.sequence - b.sequence;
        });

        return ordered.map(item => {
            const tone = this._getAgentRunTone?.(item.status, item.eventType) || 'running';
            return `
                <div class="ai-build-timeline-item is-${this._escapeHtml(tone)}">
                    <span class="ai-build-timeline-dot"></span>
                    <div>
                        <strong>${this._escapeHtml(BUILD_STAGE_LABELS[item.stage] || item.stage)}</strong>
                        <span>${this._escapeHtml(item.summary || item.title || '')}</span>
                    </div>
                </div>
            `;
        }).join('') + this._renderToolEvidenceTimeline(events);
    },

    _renderToolEvidenceTimeline(events) {
        const buildResult = this._getBuildResult(events);
        const evidence = this._toArray(
            buildResult?.toolEvidenceTimeline ||
            buildResult?.ToolEvidenceTimeline ||
            this._getAgentRunResultPayload(events)?.toolEvidenceTimeline ||
            []
        );
        if (!evidence.length) {
            return '';
        }

        return `
            <div class="ai-workspace-section-title">工具证据</div>
            ${evidence.slice(-16).map(item => {
                const stage = item.stage || item.Stage || '';
                const toolName = item.toolName || item.ToolName || '';
                const status = item.status || item.Status || '';
                const duration = item.durationMs ?? item.DurationMs ?? '';
                const warning = item.warningCode || item.WarningCode || '';
                const summary = item.outputSummary || item.OutputSummary || '';
                return `
                    <div class="ai-build-compact-row">
                        <b>${this._escapeHtml(stage)}${toolName ? ` / ${this._escapeHtml(toolName)}` : ''}</b>
                        <span>${this._escapeHtml(this._formatBuildStatus(status))}${duration !== '' ? ` / ${this._escapeHtml(String(duration))} ms` : ''}${warning ? ` / ${this._escapeHtml(warning)}` : ''}</span>
                        <small>${this._escapeHtml(summary)}</small>
                    </div>
                `;
            }).join('')}
        `;
    },

    _renderBuildTemplateSummary(events) {
        const buildResult = this._getBuildResult(events);
        const evidence = this._toArray(buildResult?.toolEvidenceTimeline || buildResult?.ToolEvidenceTimeline);
        const templateEvidence = evidence.filter(item => String(item.stage || item.Stage || '').includes('template')).slice(-3);
        if (templateEvidence.length) {
            return templateEvidence.map(item => `
                <div class="ai-build-compact-row">
                    <b>${this._escapeHtml(item.toolName || item.ToolName || 'template_strategy')}</b>
                    <span>${this._escapeHtml(item.outputSummary || item.OutputSummary || '')}</span>
                </div>
            `).join('');
        }

        const tools = events.filter(evt => {
            const payload = this._asObject?.(evt.payload) || {};
            const name = String(payload.toolName || payload.ToolName || evt.title || '').toLowerCase();
            return name.includes('template') || evt.stage === 'planner';
        }).slice(-4);

        if (!tools.length) {
            return '<div class="ai-followup-empty">模板策略尚未发布。</div>';
        }

        return tools.map(evt => `
            <div class="ai-build-compact-row">
                <b>${this._escapeHtml(evt.title || '模板事件')}</b>
                <span>${this._escapeHtml(evt.summary || '')}</span>
            </div>
        `).join('');
    },

    _renderBuildOperatorChain(events) {
        const buildResult = this._getBuildResult(events);
        const pipeline = this._toArray(buildResult?.operatorPipeline || buildResult?.OperatorPipeline);
        if (pipeline.length) {
            return `
                <div class="ai-plan-chain">
                    ${pipeline.map(item => `<span title="${this._escapeHtml([
                        item.source || item.Source || '',
                        item.repairNote || item.RepairNote || '',
                        item.status || item.Status || ''
                    ].filter(Boolean).join(' / '))}">${this._escapeHtml(item.operatorType || item.OperatorType || '')}</span>`).join('')}
                </div>
                ${pipeline.slice(0, 8).map(item => {
                    const source = item.source || item.Source || 'plan';
                    const repair = item.repairNote || item.RepairNote || '';
                    const status = item.status || item.Status || '';
                    return `
                        <div class="ai-build-compact-row">
                            <b>${this._escapeHtml(item.tempId || item.TempId || item.operatorType || item.OperatorType || '')}</b>
                            <span>${this._escapeHtml(source)}${status ? ` / ${this._escapeHtml(status)}` : ''}</span>
                            ${repair ? `<small>${this._escapeHtml(repair)}</small>` : ''}
                        </div>
                    `;
                }).join('')}
            `;
        }

        const draft = [...events].reverse().find(evt => evt.eventType === 'workflow.draft.updated');
        const payload = this._asObject?.(draft?.payload) || {};
        const operatorTypes = Array.isArray(payload.operatorTypes || payload.OperatorTypes)
            ? (payload.operatorTypes || payload.OperatorTypes)
            : [];
        if (!operatorTypes.length) {
            const planOps = this.pendingVisionPlan?.route?.operators || [];
            if (!planOps.length) {
                return '<div class="ai-followup-empty">流程草稿生成后会显示算子链。</div>';
            }
            return `<div class="ai-plan-chain">${planOps.map(op => `<span>${this._escapeHtml(op)}</span>`).join('')}</div>`;
        }

        return `<div class="ai-plan-chain">${operatorTypes.map(op => `<span>${this._escapeHtml(op)}</span>`).join('')}</div>`;
    },

    _renderBuildParameterSummary(events) {
        const resultPayload = this._getAgentRunResultPayload(events);
        const buildResult = this._getBuildResult(events);
        const mappings = this._toArray(buildResult?.parameterMapping || buildResult?.ParameterMapping);
        const pending = this._toArray(resultPayload?.pendingParameters || resultPayload?.PendingParameters);
        const buildPending = this._toArray(buildResult?.pendingParameters || buildResult?.PendingParameters);
        const missing = this._toArray(resultPayload?.missingResources || resultPayload?.MissingResources);
        const buildMissing = this._toArray(buildResult?.missingResources || buildResult?.MissingResources);
        const effectivePending = pending.length ? pending : buildPending;
        const effectiveMissing = missing.length ? missing : buildMissing;
        const latest = [...events].reverse().find(evt => evt.payload);
        const latestPayload = this._asObject?.(latest?.payload) || {};
        const missingCount = Number(latestPayload.missingResourceCount ?? latestPayload.MissingResourceCount ?? effectiveMissing.length);
        const pendingCount = Number(latestPayload.pendingParameterCount ?? latestPayload.PendingParameterCount ?? effectivePending.length);
        const mappingRows = mappings.slice(0, 8).map(item => `
            <div class="ai-build-compact-row">
                <b>${this._escapeHtml(item.tempId || item.TempId || '')}.${this._escapeHtml(item.parameterName || item.ParameterName || '')}</b>
                <span>${this._escapeHtml(item.valueSummary || item.ValueSummary || '')}</span>
                <small>${this._escapeHtml(item.source || item.Source || 'mapped')}${item.pending || item.Pending ? ' / pending' : ''}</small>
            </div>
        `).join('');

        return `
            <div class="ai-build-metric-row">
                <span><small>待确认参数</small><b>${this._escapeHtml(String(Number.isFinite(pendingCount) ? pendingCount : 0))}</b></span>
                <span><small>缺失资源</small><b>${this._escapeHtml(String(Number.isFinite(missingCount) ? missingCount : 0))}</b></span>
            </div>
            ${mappingRows ? `<div class="ai-workspace-section-title">参数映射</div>${mappingRows}` : ''}
            ${(effectivePending.length || effectiveMissing.length) ? '<div class="ai-build-note">待补详情会在下方参数与校验区继续显示。</div>' : '<div class="ai-build-note">暂无待确认参数详情。</div>'}
        `;
    },

    _renderBuildChecks(events) {
        const buildResult = this._getBuildResult(events);
        const applyGate = buildResult?.applyGate || buildResult?.ApplyGate || this._getAgentRunResultPayload(events)?.applyGate;
        const readiness = buildResult?.readinessReport || buildResult?.ReadinessReport || null;
        const firstFix = buildResult?.firstFixRecommendation || buildResult?.FirstFixRecommendation || this._getAgentRunResultPayload(events)?.firstFixRecommendation || '';
        if (applyGate) {
            const gate = this._asObject?.(applyGate) || {};
            const canvasReady = this._readBooleanField(gate, 'canvasApplyReady', 'CanvasApplyReady');
            const runtimeReady = this._readBooleanField(gate, 'runtimeDraftReady', 'RuntimeDraftReady');
            const deploymentReady = this._readBooleanField(gate, 'deploymentReady', 'DeploymentReady');
            const blocked = this._readBooleanField(gate, 'blocked', 'Blocked');
            return `
                <div class="ai-build-check is-${blocked ? 'blocked' : 'completed'}">
                    <strong>应用门禁：${this._escapeHtml(this._formatGateStatus(gate.status || gate.Status || 'unknown'))}</strong>
                    <span>画布：${canvasReady ? '可应用' : '阻断'} / 运行草稿：${runtimeReady ? '就绪' : '阻断'} / 部署：${deploymentReady ? '就绪' : '阻断'}</span>
                    ${firstFix ? `<em>${this._escapeHtml(firstFix)}</em>` : ''}
                </div>
                ${readiness ? `<div class="ai-build-note">就绪门禁已写入可回放 BuildResult。</div>` : ''}
            `;
        }

        const checkTypes = new Set([
            'readiness.checked',
            'manifest.dryrun.completed',
            'package.readiness.checked',
            'station.compatibility.completed',
            'operator.contract.completed',
            'release.review.completed'
        ]);
        const checks = events.filter(evt => checkTypes.has(evt.eventType));
        if (!checks.length) {
            return '<div class="ai-followup-empty">就绪检查和元数据预演尚未完成。</div>';
        }

        return checks.map(evt => {
            const tone = this._getAgentRunTone?.(evt.status, evt.eventType) || 'running';
            const payload = this._asObject?.(evt.payload) || {};
            const firstFix = payload.firstFixRecommendation || payload.FirstFixRecommendation || '';
            return `
                <div class="ai-build-check is-${this._escapeHtml(tone)}">
                    <strong>${this._escapeHtml(evt.title || BUILD_STAGE_LABELS[evt.stage] || evt.stage)}</strong>
                    <span>${this._escapeHtml(evt.summary || '')}</span>
                    ${firstFix ? `<em>${this._escapeHtml(firstFix)}</em>` : ''}
                </div>
            `;
        }).join('');
    },

    _renderBuildFinalDraft(events) {
        const resultPayload = this._getAgentRunResultPayload(events);
        const buildResult = this._getBuildResult(events);
        const flow = this._getResultFlowForCanvas(resultPayload, { allowMergeFallback: false }) ||
            this._getResultFlowForCanvas(this.currentResult, { allowMergeFallback: false }) ||
            null;
        const ops = flow ? this._extractOperators(flow) : [];
        const connections = flow ? this._extractConnections(flow) : [];
        const terminal = events.find(evt => ['run.completed', 'run.failed', 'run.cancelled'].includes(evt.eventType));
        const diff = buildResult?.workflowDiff || buildResult?.WorkflowDiff || resultPayload?.workflowDiff || null;
        if (!terminal) {
            return '<div class="ai-followup-empty">构建完成后会显示最终可编辑流程草稿。</div>';
        }

        if (!flow) {
            return `<div class="ai-build-note">${this._escapeHtml(terminal.summary || '构建完成，但未收到流程草稿。')}</div>`;
        }

        return `
            <div class="ai-build-final-ready">
                <strong>可编辑草稿已就绪</strong>
                <span>${this._escapeHtml(String(ops.length))} 个算子 / ${this._escapeHtml(String(connections.length))} 条连线</span>
            </div>
            ${diff ? this._renderWorkflowDiff(diff) : ''}
        `;
    },

    _renderWorkflowDiff(diff) {
        const item = this._asObject?.(diff) || {};
        const added = this._toArray(item.addedNodes || item.AddedNodes);
        const preserved = this._toArray(item.preservedNodes || item.PreservedNodes);
        const pending = this._toArray(item.pendingParameters || item.PendingParameters);
        const blockers = this._toArray(item.deploymentBlockers || item.DeploymentBlockers);
        const preview = values => values.slice(0, 4).map(value => `<span>${this._escapeHtml(value)}</span>`).join('');
        return `
            <div class="ai-workspace-section-title">流程差异</div>
            <div class="ai-build-metric-row">
                <span><small>新增节点</small><b>${this._escapeHtml(String(added.length))}</b></span>
                <span><small>保留节点</small><b>${this._escapeHtml(String(preserved.length))}</b></span>
                <span><small>待确认</small><b>${this._escapeHtml(String(pending.length))}</b></span>
                <span><small>部署阻断</small><b>${this._escapeHtml(String(blockers.length))}</b></span>
            </div>
            ${(added.length || preserved.length || pending.length || blockers.length) ? `
                <div class="ai-build-diff-tags">
                    ${preview(added)}
                    ${preview(preserved)}
                    ${preview(pending)}
                    ${preview(blockers)}
                </div>
            ` : ''}
        `;
    },

    _getAgentRunResultPayload(events = this.activeAgentRunEvents) {
        const terminal = [...(events || [])].reverse()
            .find(evt => ['run.completed', 'run.failed'].includes(evt.eventType) && evt.payload);
        return this._asObject?.(terminal?.payload) || {};
    },

    _getBuildResult(events = this.activeAgentRunEvents) {
        const payload = this._getAgentRunResultPayload(events);
        return this._getPayloadBuildResult(payload) || this._getPayloadBuildResult(this.currentResult);
    },

    _applyAgentRunResultPayload(evt) {
        const payload = this._asObject?.(evt?.payload) || {};
        if (evt.eventType !== 'run.completed') {
            return false;
        }

        const buildResult = this._getPayloadBuildResult(payload);
        const flow = this._getResultFlowForCanvas(payload);
        if (!flow) {
            return false;
        }

        const result = {
            success: true,
            completionStatus: 'completed',
            sessionId: payload.sessionId || payload.SessionId || this.sessionId,
            ...payload,
            buildResult: payload.buildResult || payload.BuildResult || buildResult || null,
            pendingParameters: this._toArray(payload.pendingParameters || payload.PendingParameters).length
                ? this._toArray(payload.pendingParameters || payload.PendingParameters)
                : this._toArray(buildResult?.pendingParameters || buildResult?.PendingParameters),
            missingResources: this._toArray(payload.missingResources || payload.MissingResources).length
                ? this._toArray(payload.missingResources || payload.MissingResources)
                : this._toArray(buildResult?.missingResources || buildResult?.MissingResources),
            flow,
            Flow: flow
        };
        this._setCurrentResult(result);
        this._resetPendingDraftState();
        this._rebuildPendingOperatorBindings({
            pending: this._resolvePendingParametersForDraft(result),
            flow,
            preferIndexFallback: true
        });
        this._workbenchStageTimeline = result.stageTimeline || result.StageTimeline || this._workbenchStageTimeline || [];
        this._displayResult(result, {
            appendChatMessage: false,
            assistantTurn: this.activeAssistantTurn
        });
        this._renderBuildWorkspaceFromAgentRun();
        return true;
    },

    _getPayloadBuildResult(payload = this.currentResult) {
        const obj = this._asObject?.(payload) || {};
        const buildResult = obj.buildResult || obj.BuildResult || null;
        return buildResult ? (this._asObject?.(buildResult) || null) : null;
    },

    _getPayloadApplyGate(payload = this.currentResult) {
        const obj = this._asObject?.(payload) || {};
        const buildResult = this._getPayloadBuildResult(obj);
        const applyGate =
            obj.applyGate ||
            obj.ApplyGate ||
            buildResult?.applyGate ||
            buildResult?.ApplyGate ||
            null;
        return applyGate ? (this._asObject?.(applyGate) || null) : null;
    },

    _isCanvasApplyReadyForResult(result = this.currentResult) {
        const gate = this._getPayloadApplyGate(result);
        if (!gate) return true;
        const canvasReady = this._readBooleanField(gate, 'canvasApplyReady', 'CanvasApplyReady');
        const blocked = this._readBooleanField(gate, 'blocked', 'Blocked');
        return canvasReady && !blocked;
    },

    _readBooleanField(obj, camelName, pascalName) {
        if (!obj || typeof obj !== 'object') return false;
        const value = obj[camelName] ?? obj[pascalName];
        return value === true || String(value).toLowerCase() === 'true';
    },

    _getResultFlowForCanvas(result = this.currentResult, options = {}) {
        const allowMergeFallback = options.allowMergeFallback !== false;
        const obj = this._asObject?.(result) || {};
        const directFlow = this._normalizeWorkflowDraftForCanvas(obj.flow || obj.Flow);
        if (directFlow && this._extractOperators(directFlow).length > 0) {
            return directFlow;
        }

        const buildResult = this._getPayloadBuildResult(obj);
        if (!buildResult) return null;

        const workflowDraft = buildResult.workflowDraft || buildResult.WorkflowDraft || null;
        let fallback = this._normalizeWorkflowDraftForCanvas(workflowDraft, buildResult);
        if (!fallback || this._extractOperators(fallback).length === 0) {
            fallback = this._buildCanvasFlowFromOperatorPipeline(buildResult);
        }

        if (!fallback || this._extractOperators(fallback).length === 0) {
            return null;
        }

        return allowMergeFallback
            ? this._mergeBuildFallbackWithCurrentCanvas(fallback, buildResult)
            : fallback;
    },

    _normalizeWorkflowDraftForCanvas(flow, buildResult = null) {
        const source = this._cloneJsonSafe(flow);
        if (!source || typeof source !== 'object') return null;

        const flowData = source.project?.flow || source.flow || source;
        const rawOperators = this._toArray(flowData.operators || flowData.Operators || flowData.nodes || flowData.Nodes);
        if (!rawOperators.length) return null;

        const operators = rawOperators.map((op, index) => this._normalizeDraftOperatorForCanvas(op, index));
        const rawConnections = this._toArray(flowData.connections || flowData.Connections);
        const connections = this._normalizeDraftConnectionsForCanvas(rawConnections, operators);
        return {
            ...flowData,
            operators,
            connections: connections.length ? connections : this._buildLinearCanvasConnections(operators),
            metadataOnly: Boolean(flowData.metadataOnly ?? flowData.MetadataOnly ?? buildResult?.metadataOnly ?? buildResult?.MetadataOnly ?? true)
        };
    },

    _normalizeDraftOperatorForCanvas(operator, index) {
        const op = this._asObject?.(operator) || {};
        const id = String(op.id || op.Id || op.tempId || op.TempId || `op_${index + 1}`).trim();
        const type = String(op.type || op.Type || op.operatorType || op.OperatorType || 'DeepLearning').trim();
        const name = String(op.name || op.Name || op.displayName || op.DisplayName || op.title || op.Title || id || type).trim();
        const parameters = this._normalizeDraftParametersForCanvas(op.parameters || op.Parameters);
        const inputPorts = this._normalizeDraftPortsForCanvas(op.inputPorts || op.InputPorts || op.inputs || op.Inputs, id, type, false);
        const outputPorts = this._normalizeDraftPortsForCanvas(op.outputPorts || op.OutputPorts || op.outputs || op.Outputs, id, type, true);
        return {
            ...op,
            id,
            name,
            type,
            x: Number(op.x ?? op.X ?? 160 + index * 180),
            y: Number(op.y ?? op.Y ?? 180),
            inputPorts,
            outputPorts,
            parameters,
            isEnabled: op.isEnabled ?? op.IsEnabled ?? true
        };
    },

    _normalizeDraftParametersForCanvas(parameters) {
        if (Array.isArray(parameters)) {
            return parameters.map(item => {
                const param = this._asObject?.(item) || {};
                const name = param.name || param.Name || '';
                const value = param.value ?? param.Value ?? param.defaultValue ?? param.DefaultValue ?? '';
                return {
                    ...param,
                    name,
                    displayName: param.displayName || param.DisplayName || name,
                    value,
                    defaultValue: param.defaultValue ?? param.DefaultValue ?? value,
                    dataType: param.dataType || param.DataType || param.type || param.Type || 'string',
                    isRequired: Boolean(param.isRequired ?? param.IsRequired ?? this._isPendingValueSummary(value))
                };
            });
        }

        const obj = this._asObject?.(parameters) || {};
        return Object.keys(obj).map(name => {
            const value = obj[name];
            return {
                name,
                displayName: name,
                value,
                defaultValue: value,
                dataType: 'string',
                isRequired: this._isPendingValueSummary(value)
            };
        });
    },

    _normalizeDraftPortsForCanvas(ports, operatorId, operatorType, isOutput) {
        const raw = this._toArray(ports);
        if (raw.length) {
            return raw.map((port, index) => {
                const item = this._asObject?.(port) || {};
                const name = item.name || item.Name || item.portName || item.PortName || (isOutput ? 'Output' : 'Input');
                return {
                    ...item,
                    id: item.id || item.Id || `${operatorId}_${isOutput ? 'out' : 'in'}_${index}`,
                    name,
                    displayName: item.displayName || item.DisplayName || name,
                    dataType: item.dataType || item.DataType || item.type || item.Type || (String(name).toLowerCase().includes('image') ? 'Image' : 'Any'),
                    direction: item.direction ?? item.Direction ?? (isOutput ? 1 : 0),
                    isRequired: Boolean(item.isRequired ?? item.IsRequired ?? !isOutput)
                };
            });
        }

        const names = this._defaultPortNamesForOperator(operatorType, isOutput);
        return names.map((name, index) => ({
            id: `${operatorId}_${isOutput ? 'out' : 'in'}_${index}`,
            name,
            displayName: name,
            dataType: String(name).toLowerCase().includes('image') ? 'Image' : 'Any',
            direction: isOutput ? 1 : 0,
            isRequired: !isOutput
        }));
    },

    _defaultPortNamesForOperator(operatorType, isOutput) {
        const type = String(operatorType || '').toLowerCase();
        if (isOutput) {
            if (type === 'resultoutput') return [];
            if (type === 'imageacquisition') return ['Image'];
            return ['Result'];
        }

        if (type === 'imageacquisition') return [];
        if (type === 'resultoutput') return ['Input'];
        return ['Image'];
    },

    _normalizeDraftConnectionsForCanvas(connections, operators) {
        const operatorById = new Map(operators.map(op => [String(op.id).toLowerCase(), op]));
        const findOperator = value => operatorById.get(String(value || '').toLowerCase()) || null;
        return this._toArray(connections).map((connection, index) => {
            const conn = this._asObject?.(connection) || {};
            const sourceId = conn.sourceOperatorId || conn.SourceOperatorId || conn.sourceTempId || conn.SourceTempId || conn.source || conn.Source;
            const targetId = conn.targetOperatorId || conn.TargetOperatorId || conn.targetTempId || conn.TargetTempId || conn.target || conn.Target;
            const source = findOperator(sourceId);
            const target = findOperator(targetId);
            if (!source || !target) return null;
            const sourcePort = this._findCanvasPort(source.outputPorts || source.OutputPorts, conn.sourcePortName || conn.SourcePortName || conn.sourcePortId || conn.SourcePortId);
            const targetPort = this._findCanvasPort(target.inputPorts || target.InputPorts, conn.targetPortName || conn.TargetPortName || conn.targetPortId || conn.TargetPortId);
            return {
                id: conn.id || conn.Id || `conn_${index + 1}`,
                sourceOperatorId: source.id,
                sourcePortId: sourcePort?.id || sourcePort?.Id || '',
                targetOperatorId: target.id,
                targetPortId: targetPort?.id || targetPort?.Id || ''
            };
        }).filter(conn => conn?.sourceOperatorId && conn?.targetOperatorId);
    },

    _findCanvasPort(ports, preferred) {
        const items = this._toArray(ports);
        const key = String(preferred || '').trim().toLowerCase();
        return items.find(port =>
            key &&
            [port.id, port.Id, port.name, port.Name]
                .map(value => String(value || '').trim().toLowerCase())
                .includes(key)) || items[0] || null;
    },

    _buildLinearCanvasConnections(operators) {
        const connections = [];
        for (let index = 0; index < operators.length - 1; index += 1) {
            const source = operators[index];
            const target = operators[index + 1];
            const sourcePort = this._toArray(source.outputPorts || source.OutputPorts)[0];
            const targetPort = this._toArray(target.inputPorts || target.InputPorts)[0];
            if (!sourcePort || !targetPort) continue;
            connections.push({
                id: `conn_${index + 1}`,
                sourceOperatorId: source.id,
                sourcePortId: sourcePort.id || sourcePort.Id || '',
                targetOperatorId: target.id,
                targetPortId: targetPort.id || targetPort.Id || ''
            });
        }
        return connections;
    },

    _buildCanvasFlowFromOperatorPipeline(buildResult) {
        const pipeline = this._toArray(buildResult?.operatorPipeline || buildResult?.OperatorPipeline);
        if (!pipeline.length) return null;
        const mappings = this._toArray(buildResult?.parameterMapping || buildResult?.ParameterMapping);
        const operators = pipeline.map(step => {
            const tempId = step.tempId || step.TempId || step.operatorType || step.OperatorType || '';
            const operatorType = step.operatorType || step.OperatorType || 'DeepLearning';
            const parameters = Object.fromEntries(mappings
                .filter(item => String(item.tempId || item.TempId || '').toLowerCase() === String(tempId).toLowerCase())
                .map(item => [item.parameterName || item.ParameterName || '', item.valueSummary || item.ValueSummary || ''])
                .filter(([name]) => name));
            return {
                tempId,
                operatorType,
                displayName: operatorType,
                parameters
            };
        });
        return this._normalizeWorkflowDraftForCanvas({
            operators,
            connections: [],
            metadataOnly: true
        }, buildResult);
    },

    _mergeBuildFallbackWithCurrentCanvas(fallbackFlow, buildResult = null) {
        const intent = String(buildResult?.buildIntent || buildResult?.BuildIntent || '').toLowerCase();
        const diff = this._asObject?.(buildResult?.workflowDiff || buildResult?.WorkflowDiff) || {};
        const preserved = this._toArray(diff.preservedNodes || diff.PreservedNodes);
        if (!['modify', 'refactor', 'review_pending_parameters'].includes(intent) && preserved.length === 0) {
            return fallbackFlow;
        }

        let current = null;
        try {
            current = this.flowCanvas?.serialize?.() || null;
        } catch {
            current = null;
        }

        const currentFlow = this._normalizeWorkflowDraftForCanvas(current);
        if (!currentFlow || this._extractOperators(currentFlow).length === 0) {
            return fallbackFlow;
        }

        const currentOps = this._extractOperators(currentFlow);
        const fallbackOps = this._extractOperators(fallbackFlow);
        const seen = new Set(currentOps.map(op => String(op.id || op.Id || op.name || op.Name || '').toLowerCase()).filter(Boolean));
        const addedOps = fallbackOps.filter(op => {
            const key = String(op.id || op.Id || op.name || op.Name || '').toLowerCase();
            if (!key || seen.has(key)) return false;
            seen.add(key);
            return true;
        });
        return {
            ...currentFlow,
            operators: [...currentOps, ...addedOps],
            connections: [
                ...this._extractConnections(currentFlow),
                ...this._extractConnections(fallbackFlow).filter(conn =>
                    addedOps.some(op => op.id === conn.sourceOperatorId || op.id === conn.targetOperatorId))
            ],
            metadataOnly: true
        };
    },

    _cloneJsonSafe(value) {
        if (value === null || value === undefined) return value;
        try {
            return typeof structuredClone === 'function'
                ? structuredClone(value)
                : JSON.parse(JSON.stringify(value));
        } catch {
            return value;
        }
    },

    _isPendingValueSummary(value) {
        const text = String(value ?? '').toLowerCase();
        return text.includes('<pending') || text.includes('pending-') || text.includes('missing');
    },

    _countBuildBlockers(events = this.activeAgentRunEvents) {
        return (events || []).filter(evt => {
            const status = String(evt?.status || '').toLowerCase();
            return status === 'blocked' || status === 'failed';
        }).length;
    }
};
