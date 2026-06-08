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
    'planner',
    'workflow_draft',
    'readiness',
    'manifest_dry_run',
    'package_readiness',
    'station_compatibility',
    'operator_contract',
    'release_review',
    'artifact',
    'run'
];

const BUILD_STAGE_LABELS = {
    understand_requirement: 'Understand',
    context_collection: 'Context',
    plan_generation: 'Plan',
    assumption_confirmation: 'Assumptions',
    requirement_parsing: 'Normalize',
    planner: 'Template and tools',
    tool_policy: 'Tool policy',
    workflow_draft: 'Flow draft',
    readiness: 'Readiness',
    manifest_dry_run: 'Dry-run',
    package_readiness: 'Package',
    station_compatibility: 'Station',
    operator_contract: 'Contracts',
    release_review: 'Release review',
    artifact: 'Artifact',
    run: 'Run'
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
            this._addMessage('system', 'Enter an inspection goal before planning.');
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
            statusText: 'Planning',
            statusTone: 'warning',
            openReply: true
        });
        this._setAssistantSectionText(
            turn,
            'reply',
            'Plan Mode is collecting public engineering context and asking the backend Agent Orchestrator for a structured plan.'
        );

        this._setResultStatusNote('Plan Mode is waiting for backend Agent Orchestrator output.', 'info');
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
                this._setAssistantTurnStatus(turn, 'Plan ready', 'success');
                this._setAssistantSectionText(
                    turn,
                    'reply',
                    'Plan Mode returned a structured engineering plan. Accept recommended defaults or adjust selected options before Build.'
                );
                this._setResultStatusNote('Plan Mode is waiting for confirmation before Build starts.', 'info');
                this._renderAgentWorkspaceOverview();
                this._renderPlanWorkspace(this.pendingVisionPlan);
            })
            .catch(error => {
                if (!this._isActivePlanRequest(planRequestId)) return;
                this._clearActivePlanRequest(planRequestId);
                this.pendingVisionPlan = null;
                this._setAssistantTurnStatus(turn, 'Plan failed', 'failed');
                this._setAssistantSectionText(
                    turn,
                    'reply',
                    `Plan Mode failed: ${error?.message || String(error || 'unknown error')}`
                );
                this._setResultStatusNote('Plan Mode failed. Retry after checking backend connectivity.', 'warning');
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
            goal: plan.goal || plan.Goal || fallbackDescription || 'Vision workflow draft',
            intent: plan.intent || plan.Intent || '',
            confidence: plan.confidence || plan.Confidence || 'medium',
            blockerCount: this._toArray(plan.blockingReasons || plan.BlockingReasons).length,
            nextAction: plan.nextAction || plan.NextAction || 'Review plan, then start Build.',
            executable: Boolean(plan.canBuild ?? plan.CanBuild ?? true),
            blockingReasons: this._toArray(plan.blockingReasons || plan.BlockingReasons),
            understanding: this._toArray(plan.requirementUnderstanding || plan.RequirementUnderstanding).length
                ? this._toArray(plan.requirementUnderstanding || plan.RequirementUnderstanding)
                : [`User goal: ${fallbackDescription || 'Vision workflow draft'}`],
            route: {
                routeId: route.routeId || route.RouteId || '',
                title: route.title || route.Title || 'Vision route',
                summary: route.summary || route.Summary || '',
                operators: this._toArray(route.operators || route.Operators),
                templateDecision: route.templateDecision || route.TemplateDecision || ''
            },
            questions: normalizedQuestions,
            assumptions: normalizedDefaults.length
                ? normalizedDefaults.map(item => `${item.label}: ${item.value}${item.impact ? ` (${item.impact})` : ''}`)
                : ['Public metadata boundaries are preserved; missing resources stay pending until confirmed.'],
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
        const mode = this.agentWorkspaceMode === AgentWorkspaceModes.BUILD ? 'Build Mode' : 'Plan Mode';
        const activeEvents = Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [];
        const terminal = activeEvents.find(evt => ['run.completed', 'run.failed', 'run.cancelled'].includes(evt.eventType));
        const lastEvent = activeEvents[activeEvents.length - 1];
        const blockerCount = this._countBuildBlockers(activeEvents);
        const goal = plan?.goal || this.lastUserPrompt || 'Describe a visual inspection goal to start planning.';
        const confidence = plan?.confidence || (activeEvents.length ? 'event-backed' : 'not set');
        const nextAction = terminal
            ? (terminal.eventType === 'run.completed' ? 'Review the draft and Apply it to the canvas.' : 'Review first fix and retry Build.')
            : this.agentWorkspaceMode === AgentWorkspaceModes.BUILD
                ? (lastEvent?.summary || 'Waiting for the next public AgentRun event.')
                : (plan?.nextAction || 'Plan Mode will ask only high-value engineering questions.');
        const executable = this.agentWorkspaceMode === AgentWorkspaceModes.BUILD
            ? activeEvents.length > 0
            : Boolean(plan?.executable);

        el.innerHTML = `
            <section class="ai-agent-overview-card is-${this._escapeHtml(this.agentWorkspaceMode || AgentWorkspaceModes.PLAN)}">
                <div class="ai-agent-overview-main">
                    <span class="ai-agent-overview-kicker">${this._escapeHtml(mode)}</span>
                    <strong>${this._escapeHtml(goal)}</strong>
                    <span>${this._escapeHtml(nextAction)}</span>
                </div>
                <div class="ai-agent-overview-metrics">
                    <span><small>Confidence</small><b>${this._escapeHtml(confidence)}</b></span>
                    <span><small>Blockers</small><b>${this._escapeHtml(String(blockerCount))}</b></span>
                    <span><small>Events</small><b>${this._escapeHtml(String(activeEvents.length))}</b></span>
                    <span><small>Executable</small><b>${executable ? 'yes' : 'no'}</b></span>
                </div>
            </section>
        `;
    },

    _renderPlanWorkspace(plan = this.pendingVisionPlan) {
        const el = this.container?.querySelector('#ai-plan-workspace');
        if (!el) return;

        el.hidden = this.agentWorkspaceMode === AgentWorkspaceModes.BUILD;
        if (!plan) {
            el.innerHTML = `
                <div class="ai-plan-empty">
                    <div class="ai-plan-empty-title">Plan Mode</div>
                    <div class="ai-plan-empty-copy">Describe the inspection target. The Agent will turn it into a visual engineering plan before Build starts.</div>
                </div>
            `;
            return;
        }

        const selections = this.planQuestionSelections || {};
        el.innerHTML = `
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">Understanding</div>
                <div class="ai-workspace-list">${plan.understanding.map(item => `<div>${this._escapeHtml(item)}</div>`).join('')}</div>
            </section>
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">Recommended Route</div>
                <div class="ai-plan-route">
                    <strong>${this._escapeHtml(plan.route.title)}</strong>
                    <span>${this._escapeHtml(plan.route.summary)}</span>
                    <div class="ai-plan-chain">${plan.route.operators.map(op => `<span>${this._escapeHtml(op)}</span>`).join('')}</div>
                </div>
            </section>
            <section class="ai-workspace-section">
                <div class="ai-workspace-section-title">Clarifying Questions</div>
                <div class="ai-plan-question-list">
                    ${plan.questions.map(question => this._renderPlanQuestion(question, selections[question.id])).join('')}
                </div>
            </section>
            <section class="ai-workspace-section ai-workspace-grid-2">
                <div>
                    <div class="ai-workspace-section-title">Default Assumptions</div>
                    <ul>${plan.assumptions.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>
                </div>
                <div>
                    <div class="ai-workspace-section-title">Risks</div>
                    <ul>${plan.risks.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ul>
                </div>
            </section>
            <section class="ai-workspace-section ai-workspace-grid-2">
                <div>
                    <div class="ai-workspace-section-title">Executable Plan</div>
                    <ol>${plan.steps.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ol>
                </div>
                <div>
                    <div class="ai-workspace-section-title">Acceptance</div>
                    <ol>${plan.acceptanceCriteria.map(item => `<li>${this._escapeHtml(item)}</li>`).join('')}</ol>
                </div>
            </section>
            <div class="ai-plan-actions">
                <button class="ai-plan-action is-primary" type="button" id="ai-btn-accept-plan">Accept Recommended and Build</button>
                <button class="ai-plan-action" type="button" id="ai-btn-start-build">Start Build</button>
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
                    <b>Default</b>
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
                                <span>${this._escapeHtml(option.label)}${option.recommended ? ' (Recommended)' : ''}</span>
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
        this._setResultStatusNote('Build Mode started. Progress comes from AgentRun public events.', 'info');

        return this._dispatchGenerateRequest({
            description: plan.buildPrompt || plan.originalDescription,
            hint: '',
            userMessage: `Start Build from plan: ${plan.goal}`,
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
                timeline.innerHTML = '<div class="ai-followup-empty">Waiting for AgentRun public events.</div>';
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
        }).join('');
    },

    _renderBuildTemplateSummary(events) {
        const tools = events.filter(evt => {
            const payload = this._asObject?.(evt.payload) || {};
            const name = String(payload.toolName || payload.ToolName || evt.title || '').toLowerCase();
            return name.includes('template') || evt.stage === 'planner';
        }).slice(-4);

        if (!tools.length) {
            return '<div class="ai-followup-empty">Template matching has not reported yet.</div>';
        }

        return tools.map(evt => `
            <div class="ai-build-compact-row">
                <b>${this._escapeHtml(evt.title || 'Template event')}</b>
                <span>${this._escapeHtml(evt.summary || '')}</span>
            </div>
        `).join('');
    },

    _renderBuildOperatorChain(events) {
        const draft = [...events].reverse().find(evt => evt.eventType === 'workflow.draft.updated');
        const payload = this._asObject?.(draft?.payload) || {};
        const operatorTypes = Array.isArray(payload.operatorTypes || payload.OperatorTypes)
            ? (payload.operatorTypes || payload.OperatorTypes)
            : [];
        if (!operatorTypes.length) {
            const planOps = this.pendingVisionPlan?.route?.operators || [];
            if (!planOps.length) {
                return '<div class="ai-followup-empty">Operator chain will appear after workflow draft generation.</div>';
            }
            return `<div class="ai-plan-chain">${planOps.map(op => `<span>${this._escapeHtml(op)}</span>`).join('')}</div>`;
        }

        return `<div class="ai-plan-chain">${operatorTypes.map(op => `<span>${this._escapeHtml(op)}</span>`).join('')}</div>`;
    },

    _renderBuildParameterSummary(events) {
        const resultPayload = this._getAgentRunResultPayload(events);
        const pending = resultPayload?.pendingParameters || resultPayload?.PendingParameters || [];
        const missing = resultPayload?.missingResources || resultPayload?.MissingResources || [];
        const latest = [...events].reverse().find(evt => evt.payload);
        const latestPayload = this._asObject?.(latest?.payload) || {};
        const missingCount = Number(latestPayload.missingResourceCount ?? latestPayload.MissingResourceCount ?? missing.length);
        const pendingCount = Number(latestPayload.pendingParameterCount ?? latestPayload.PendingParameterCount ?? pending.length);

        return `
            <div class="ai-build-metric-row">
                <span><small>Pending parameters</small><b>${this._escapeHtml(String(Number.isFinite(pendingCount) ? pendingCount : 0))}</b></span>
                <span><small>Missing resources</small><b>${this._escapeHtml(String(Number.isFinite(missingCount) ? missingCount : 0))}</b></span>
            </div>
            ${(pending.length || missing.length) ? '<div class="ai-build-note">Details are rendered in the pending parameter and validation sections below.</div>' : '<div class="ai-build-note">No pending parameter details have been published yet.</div>'}
        `;
    },

    _renderBuildChecks(events) {
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
            return '<div class="ai-followup-empty">Readiness and dry-run checks have not completed yet.</div>';
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
        const flow = resultPayload?.flow || resultPayload?.Flow || this.currentResult?.flow || this.currentResult?.Flow || null;
        const ops = flow ? this._extractOperators(flow) : [];
        const connections = flow ? this._extractConnections(flow) : [];
        const terminal = events.find(evt => ['run.completed', 'run.failed', 'run.cancelled'].includes(evt.eventType));
        if (!terminal) {
            return '<div class="ai-followup-empty">Final editable draft will appear when Build completes.</div>';
        }

        if (!flow) {
            return `<div class="ai-build-note">${this._escapeHtml(terminal.summary || 'Build completed without a flow payload.')}</div>`;
        }

        return `
            <div class="ai-build-final-ready">
                <strong>Editable draft ready</strong>
                <span>${this._escapeHtml(String(ops.length))} operators / ${this._escapeHtml(String(connections.length))} connections</span>
            </div>
        `;
    },

    _getAgentRunResultPayload(events = this.activeAgentRunEvents) {
        const terminal = [...(events || [])].reverse()
            .find(evt => ['run.completed', 'run.failed'].includes(evt.eventType) && evt.payload);
        return this._asObject?.(terminal?.payload) || {};
    },

    _applyAgentRunResultPayload(evt) {
        const payload = this._asObject?.(evt?.payload) || {};
        const flow = payload.flow || payload.Flow;
        if (!flow || evt.eventType !== 'run.completed') {
            return false;
        }

        const result = {
            success: true,
            completionStatus: 'completed',
            sessionId: payload.sessionId || payload.SessionId || this.sessionId,
            ...payload,
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

    _countBuildBlockers(events = this.activeAgentRunEvents) {
        return (events || []).filter(evt => {
            const status = String(evt?.status || '').toLowerCase();
            return status === 'blocked' || status === 'failed';
        }).length;
    }
};
