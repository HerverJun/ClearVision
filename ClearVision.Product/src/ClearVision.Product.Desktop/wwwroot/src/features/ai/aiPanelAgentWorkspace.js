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
        this.pendingVisionPlan = this._buildVisionEngineeringPlan(normalizedDescription, {
            hint,
            attachmentCount: Array.isArray(attachmentPaths) ? attachmentPaths.length : 0,
            templateSelection
        });
        this.planQuestionSelections = Object.fromEntries(
            this.pendingVisionPlan.questions.map(question => [question.id, question.defaultValue])
        );

        this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
        this._addMessage('user', userMessage || normalizedDescription);
        const turn = this._startAssistantTurn({
            activate: false,
            statusText: 'Plan ready',
            statusTone: 'warning',
            openReply: true
        });
        this._setAssistantSectionText(
            turn,
            'reply',
            'Plan Mode prepared an engineering plan with recommended defaults. Accept the recommendation or adjust the options before Build.'
        );

        this._setResultStatusNote('Plan Mode is waiting for confirmation before Build starts.', 'info');
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
        if (clearInput && input) {
            input.value = '';
            input.style.height = 'auto';
        }

        return true;
    },

    _buildVisionEngineeringPlan(description, meta = {}) {
        const text = String(description || '').trim();
        const lower = text.toLowerCase();
        const hasCurrentFlow = this._hasCurrentFlowContext?.() === true;
        const isScratch = /scratch|划痕|刮伤|金属|metal|surface|表面/.test(lower);
        const isWire = /wire|terminal|线序|端子|线束/.test(lower);
        const isBarcode = /barcode|datamatrix|qr|二维码|条码/.test(lower);
        const isMeasure = /measure|distance|diameter|孔距|测量|尺寸|圆心/.test(lower);

        const route = isWire
            ? {
                title: 'Template-first wire sequence inspection',
                summary: 'Use an existing wire/terminal scenario template, then bind model and output metadata.',
                operators: ['ImageAcquisition', 'DeepLearning', 'DetectionSequenceJudge', 'ResultOutput']
            }
            : isBarcode
                ? {
                    title: 'Code recognition route',
                    summary: 'Acquire the frame, normalize ROI, decode the code, and emit a judgment result.',
                    operators: ['ImageAcquisition', 'RoiManager', 'CodeRecognition', 'ResultJudgment', 'ResultOutput']
                }
                : isMeasure
                    ? {
                        title: 'Calibration-backed measurement route',
                        summary: 'Calibrate pixel scale, find geometry, measure dimensions, and report tolerance.',
                        operators: ['ImageAcquisition', 'CalibrationLoader', 'CircleMeasurement', 'GeoMeasurement', 'ResultOutput']
                    }
                    : {
                        title: 'Surface defect inspection route',
                        summary: 'Stabilize illumination, enhance scratches, segment candidate defects, then judge area and contrast.',
                        operators: ['ImageAcquisition', 'ShadingCorrection', 'Filtering', 'SurfaceDefectDetection', 'BlobAnalysis', 'ResultJudgment', 'ResultOutput']
                    };

        const questions = [
            {
                id: 'input_source',
                title: 'What image source should Build assume first?',
                why: 'Source choice controls acquisition parameters, Station compatibility, and dry-run readiness.',
                defaultValue: 'camera',
                defaultAssumption: 'Use a Station camera binding and keep file paths as pending metadata.',
                impact: 'Choosing file input makes offline validation easier; choosing camera keeps the draft closer to production.',
                options: [
                    { value: 'camera', label: 'Station camera', recommended: true, description: 'Use camera binding placeholders.', impact: 'Best for production readiness checks.' },
                    { value: 'file', label: 'Sample images', recommended: false, description: 'Use sample image metadata.', impact: 'Best for lab dry-run before camera setup.' },
                    { value: 'unknown', label: 'Decide later', recommended: false, description: 'Keep acquisition source pending.', impact: 'Build will surface acquisition as a blocker.' }
                ]
            },
            {
                id: 'inspection_scope',
                title: 'How should ROI be handled?',
                why: 'ROI strategy changes operator chain shape and parameter completeness.',
                defaultValue: isScratch ? 'surface_roi' : 'full_frame',
                defaultAssumption: isScratch ? 'Inspect the main visible metal surface ROI.' : 'Start with full-frame inspection.',
                impact: 'A fixed ROI reduces false positives; full-frame is safer when object position is unknown.',
                options: [
                    { value: isScratch ? 'surface_roi' : 'full_frame', label: isScratch ? 'Surface ROI' : 'Full frame', recommended: true, description: 'Use the most likely ROI strategy.', impact: 'Keeps the first draft focused and easy to validate.' },
                    { value: 'multi_roi', label: 'Multiple ROIs', recommended: false, description: 'Reserve several named regions.', impact: 'More complete but more parameters need review.' },
                    { value: 'auto_locate', label: 'Auto locate part', recommended: false, description: 'Add matching or detection before inspection.', impact: 'More robust to pose drift but expands Build scope.' }
                ]
            },
            {
                id: 'output_policy',
                title: 'What output should the draft target?',
                why: 'Output target affects PLC/Station policy and release review.',
                defaultValue: 'local_result',
                defaultAssumption: 'Output metadata result locally first; PLC write remains disabled until reviewed.',
                impact: 'Local result output is safest; PLC output requires address and Station policy confirmation.',
                options: [
                    { value: 'local_result', label: 'Local result', recommended: true, description: 'Use ResultOutput with a local metadata channel.', impact: 'Fastest path to editable workflow draft.' },
                    { value: 'plc_pending', label: 'PLC pending', recommended: false, description: 'Prepare PLC output as pending metadata only.', impact: 'Build will add compatibility blockers.' },
                    { value: 'dashboard', label: 'Dashboard', recommended: false, description: 'Emit result metadata for a station dashboard.', impact: 'Requires output channel confirmation.' }
                ]
            }
        ];

        const assumptions = [
            'Only public diagnostics, event metadata, and redacted engineering summaries are shown.',
            'Build may create an editable workflow draft even when deployment readiness is blocked.',
            'Missing camera/model/template/output metadata will be surfaced as pending parameters instead of guessed.'
        ];
        if (meta.attachmentCount > 0) {
            assumptions.push(`${meta.attachmentCount} attachment(s) are counted, but raw local paths are not sent through AgentRun events.`);
        }
        if (hasCurrentFlow) {
            assumptions.push('The current canvas can be used as context when the Build request is an edit or review.');
        }

        return {
            id: `plan-${Date.now()}`,
            mode: AgentWorkspaceModes.PLAN,
            originalDescription: text,
            buildPrompt: text,
            goal: this._summarizePlanGoal(text),
            confidence: isScratch || isWire || isBarcode || isMeasure ? 'medium-high' : 'medium',
            blockerCount: 0,
            nextAction: 'Review the assumptions, then start Build.',
            executable: true,
            understanding: [
                `User goal: ${text}`,
                hasCurrentFlow ? 'Current canvas context is available.' : 'No existing canvas context is required for the first draft.',
                `Likely route: ${route.title}.`
            ],
            route,
            questions,
            assumptions,
            steps: [
                'Confirm recommended assumptions.',
                'Collect operator catalog, template, current flow, and Station metadata boundaries.',
                'Match template or create operator chain.',
                'Map parameters and mark unresolved metadata as pending.',
                'Run schema readiness, dry-run, package readiness, Station compatibility, operator contract, and release review events.',
                'Return an editable ClearVision workflow draft for Apply.'
            ],
            risks: [
                'Real defect thresholds need sample images or field data before production.',
                'Camera, model, template, and PLC resources must stay metadata-only until confirmed.',
                'Station compatibility can block deployment while still allowing canvas editing.'
            ],
            acceptanceCriteria: [
                'Workflow draft contains acquisition, inspection, judgment, and output stages.',
                'All unresolved resources are listed as pending parameters or missing resources.',
                'Readiness, dry-run, package, Station, contract, and release review events are replayable.',
                'Apply button is enabled only when a draft flow is available.'
            ]
        };
    },

    _summarizePlanGoal(description) {
        const text = String(description || '').trim();
        if (!text) return 'Vision workflow draft';
        return text.length > 72 ? `${text.slice(0, 72)}...` : text;
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
        const hint = this._buildPlanBuildHint(plan, { acceptedRecommended });
        this.agentWorkspaceMode = AgentWorkspaceModes.BUILD;
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(plan);
        this._renderBuildWorkspaceFromAgentRun();
        this._setResultStatusNote('Build Mode started. Progress comes from AgentRun public events.', 'info');

        return this._dispatchGenerateRequest({
            description: plan.buildPrompt || plan.originalDescription,
            hint,
            userMessage: `Start Build from plan: ${plan.goal}`,
            attachmentPaths: [],
            existingFlowJson: null,
            explicitMode: 'new',
            templateSelection: null,
            clearInput: true,
            skipPlan: true
        });
    },

    _buildPlanBuildHint(plan, { acceptedRecommended = false } = {}) {
        const selections = this.planQuestionSelections || {};
        const selectedLines = plan.questions.map(question => {
            const value = selections[question.id] || question.defaultValue;
            const option = question.options.find(item => item.value === value);
            return `${question.id}: ${option?.label || value}`;
        });

        return [
            'Plan Mode confirmed build context:',
            `Goal: ${plan.goal}`,
            `Route: ${plan.route.title}`,
            `Accepted recommended defaults: ${acceptedRecommended ? 'yes' : 'no'}`,
            'Selections:',
            ...selectedLines,
            'Acceptance criteria:',
            ...plan.acceptanceCriteria
        ].join('\n');
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
