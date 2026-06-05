export const AiWorkbenchStates = Object.freeze({
    IDLE: 'idle',
    CLARIFYING: 'clarifying',
    MATCHING_TEMPLATE: 'matching_template',
    GENERATING: 'generating',
    PARSING: 'parsing',
    VALIDATING: 'validating',
    DRY_RUNNING: 'dry_running',
    REVIEWING_PARAMETERS: 'reviewing_parameters',
    READY_TO_APPLY: 'ready_to_apply',
    APPLYING: 'applying',
    APPLIED: 'applied',
    FAILED: 'failed',
    CANCELLED: 'cancelled'
});

const WORKBENCH_STAGE_ORDER = [
    { key: 'scenario_match', label: '场景识别', states: [AiWorkbenchStates.MATCHING_TEMPLATE] },
    { key: 'template_match', label: '模板匹配', states: [AiWorkbenchStates.MATCHING_TEMPLATE] },
    { key: 'generating', label: '生成', states: [AiWorkbenchStates.GENERATING, AiWorkbenchStates.PARSING] },
    { key: 'validating', label: '校验', states: [AiWorkbenchStates.VALIDATING] },
    { key: 'dryrun', label: 'DryRun', states: [AiWorkbenchStates.DRY_RUNNING] },
    { key: 'parameters', label: '待补参数', states: [AiWorkbenchStates.REVIEWING_PARAMETERS] },
    { key: 'apply', label: '可应用', states: [AiWorkbenchStates.READY_TO_APPLY, AiWorkbenchStates.APPLYING, AiWorkbenchStates.APPLIED] }
];

export const STAGE_DIAGNOSTIC_LABELS = {
    conversation: '会话准备',
    turn_router: '回合路由',
    scenario_match: '场景匹配',
    requirement_brief: '需求提炼',
    clarification: '需求澄清',
    prompt_context: 'Prompt 构建',
    llm: '模型调用',
    parse: '结果解析',
    validator: '流程校验',
    template_gate: '模板约束',
    dryrun: 'DryRun 预演',
    layout: '自动布局'
};

export const aiPanelWorkbenchMixin = {
    _normalizeRuntimeFieldList(value) {
        if (!Array.isArray(value)) return [];
        return [...new Set(value.map(item => String(item || '').trim()).filter(Boolean))];
    },

    _normalizePerformanceBudget(value) {
        if (!value || typeof value !== 'object') return null;

        const readNumber = (...keys) => {
            for (const key of keys) {
                const raw = value?.[key];
                const numeric = Number(raw);
                if (Number.isFinite(numeric)) return numeric;
            }
            return 0;
        };

        const warnings = Array.isArray(value.warnings || value.Warnings)
            ? [...new Set((value.warnings || value.Warnings)
                .map(item => String(item || '').trim())
                .filter(Boolean))]
            : [];

        return {
            totalDurationMs: readNumber('totalDurationMs', 'TotalDurationMs'),
            stageCount: readNumber('stageCount', 'StageCount'),
            retryCount: readNumber('retryCount', 'RetryCount'),
            estimatedInputTokens: readNumber('estimatedInputTokens', 'EstimatedInputTokens'),
            estimatedOutputTokens: readNumber('estimatedOutputTokens', 'EstimatedOutputTokens'),
            budgetStatus: String(value.budgetStatus || value.BudgetStatus || '').trim().toLowerCase(),
            slowestStage: String(value.slowestStage || value.SlowestStage || '').trim(),
            slowestStageDurationMs: readNumber('slowestStageDurationMs', 'SlowestStageDurationMs'),
            warnings
        };
    },

    _formatDuration(ms) {
        const value = Number(ms);
        if (!Number.isFinite(value) || value <= 0) return '--';
        if (value >= 1000) return `${(value / 1000).toFixed(value >= 10_000 ? 0 : 1)}s`;
        return `${Math.round(value)}ms`;
    },

    _formatTokenEstimate(inputTokens = 0, outputTokens = 0) {
        const total = Number(inputTokens || 0) + Number(outputTokens || 0);
        if (!Number.isFinite(total) || total <= 0) return '--';
        return total >= 1000 ? `~${(total / 1000).toFixed(total >= 10_000 ? 0 : 1)}k` : `~${Math.round(total)}`;
    },

    _buildAgentNextAction(meta = {}) {
        const turnIntent = String(meta.turnIntent || '').trim().toLowerCase();
        const interactionState = String(meta.interactionState || '').trim().toLowerCase();
        const blockingCount = Number(meta.blockingCount || 0);
        const nonBlockingCount = Number(meta.nonBlockingCount || 0);
        const pendingCount = Number(meta.pendingCount || 0);
        const missingResourceCount = Number(meta.missingResourceCount || 0);
        const hasFlow = Boolean(meta.hasFlow);
        const manualRetryRequired = Boolean(meta.manualRetryRequired);

        if (interactionState === 'clarifying' || blockingCount > 0) {
            const countText = blockingCount > 0 ? ` ${blockingCount} 个` : '';
            return `下一步：先回答${countText}阻断问题，系统会在补齐后继续生成。`;
        }

        if (interactionState === 'manual_retry' || manualRetryRequired || turnIntent === 'manual_retry_repair') {
            return '下一步：检查已回填的修复草稿并发送，只进入修复链路，不重新澄清需求。';
        }

        if (interactionState === 'modifying' || turnIntent === 'modify_flow') {
            return '下一步：等待微调完成，系统只改用户指定部分并保留其它算子和连线。';
        }

        if (interactionState === 'reviewing_parameters' || turnIntent === 'review_pending_parameters' || pendingCount > 0) {
            return pendingCount > 0
                ? `下一步：补齐 ${pendingCount} 组待确认参数，再执行统一确认。`
                : '下一步：审核待确认参数，流程结构保持不变。';
        }

        if (interactionState === 'generating') {
            return '下一步：等待模板匹配、生成、校验和 DryRun 完成。';
        }

        if (turnIntent === 'chat_or_help') {
            return hasFlow
                ? '下一步：可以继续提出微调、解释或参数审核需求。'
                : '下一步：描述检测、测量或识别目标即可开始生成流程。';
        }

        if (turnIntent === 'unknown' || interactionState === 'idle') {
            return hasFlow
                ? '下一步：说明要修改、解释或审核的具体内容。'
                : '下一步：补充检测场景、对象和输出目标，系统会进入业务链路。';
        }

        if (hasFlow) {
            return nonBlockingCount > 0 || missingResourceCount > 0
                ? `下一步：可先应用方案，也可以补齐 ${nonBlockingCount || missingResourceCount} 项非阻断信息后再确认。`
                : '下一步：确认方案后应用到流程草稿，或继续输入微调需求。';
        }

        return '下一步：继续补充需求，系统会保持业务生成链路稳定。';
    },

    _renderAgentRuntime(payload = null, { reset = false } = {}) {
        const el = this.container?.querySelector('#ai-agent-runtime');
        if (!el) return;

        if (reset) {
            this._lastAgentRuntime = null;
            el.hidden = true;
            el.innerHTML = '';
            el.className = 'ai-agent-runtime';
            return;
        }

        const source = payload || this._lastAgentRuntime;
        if (!source) {
            el.hidden = true;
            el.innerHTML = '';
            return;
        }

        this._lastAgentRuntime = source;
        const brief = this._normalizeRequirementBrief(source.requirementBrief ?? source.RequirementBrief ?? null);
        const turnIntent = this._getTurnIntent(source) || 'unknown';
        const interactionState = this._getInteractionState(source) || 'generating';
        const routerConfidence = this._getRouterConfidence(source);
        const blockingFields = this._normalizeRuntimeFieldList(source.blockingClarificationFields ?? source.BlockingClarificationFields);
        const nonBlockingFields = this._normalizeRuntimeFieldList(source.nonBlockingMissingFields ?? source.NonBlockingMissingFields);
        const effectiveBlockingFields = blockingFields.length > 0 ? blockingFields : (brief?.blockingClarificationFields || []);
        const effectiveNonBlockingFields = nonBlockingFields.length > 0 ? nonBlockingFields : (brief?.nonBlockingMissingFields || []);
        const questionCount = brief?.clarificationQuestions?.length || 0;
        const pendingParameters = this._resolvePendingParametersForDraft(source);
        const missingResources = this._normalizeMissingResources(source.missingResources ?? source.MissingResources);
        const flow = source.flow ?? source.Flow ?? this.currentResult?.flow ?? this.currentResult?.Flow ?? null;
        const manualRetry = source.manualRetry ?? source.ManualRetry ?? null;
        const performanceBudget = this._normalizePerformanceBudget(source.performanceBudget ?? source.PerformanceBudget);

        const intentLabels = {
            manual_retry_repair: '修复草稿',
            clarification_answer: '补充澄清',
            review_pending_parameters: '审核参数',
            explain_flow: '解释工程',
            modify_flow: '增量微调',
            new_flow: '新建流程',
            chat_or_help: '普通对话',
            unknown: '待判定'
        };
        const stateLabels = {
            idle: '待机',
            clarifying: '待澄清',
            generating: '生成中',
            modifying: '微调中',
            reviewing_parameters: '审核中',
            manual_retry: '修复中',
            completed: '已完成',
            failed: '失败'
        };
        const confidenceLabels = {
            high: '高',
            medium: '中',
            low: '低'
        };
        const summary = interactionState === 'clarifying'
            ? `${effectiveBlockingFields.length || questionCount} 项阻断澄清`
            : interactionState === 'modifying'
                ? '基于当前工程增量修改'
                : interactionState === 'reviewing_parameters'
                    ? '只审核待确认参数'
                    : turnIntent === 'chat_or_help'
                        ? '普通回复，不进入澄清'
                        : turnIntent === 'unknown'
                            ? '正在判定本轮意图'
                        : effectiveNonBlockingFields.length > 0
                            ? `${effectiveNonBlockingFields.length} 项非阻断缺项`
                            : '业务链路就绪';
        const blockingCount = effectiveBlockingFields.length || questionCount;
        const nextAction = this._buildAgentNextAction({
            turnIntent,
            interactionState,
            blockingCount,
            nonBlockingCount: effectiveNonBlockingFields.length,
            pendingCount: pendingParameters.length,
            missingResourceCount: missingResources.length,
            hasFlow: Boolean(flow),
            manualRetryRequired: Boolean(manualRetry?.required ?? manualRetry?.Required)
        });
        const perfStatusText = performanceBudget?.budgetStatus === 'warning' ? '预警' : '正常';
        const perfWarningText = performanceBudget?.warnings?.length > 0
            ? `性能提示：${performanceBudget.warnings.slice(0, 2).join('、')}`
            : '';
        const runtimeMetrics = [
            { label: '意图', value: intentLabels[turnIntent] || turnIntent },
            { label: '置信度', value: confidenceLabels[routerConfidence] || routerConfidence || '--' },
            { label: '阻断', value: blockingCount },
            { label: '待补', value: effectiveNonBlockingFields.length }
        ];
        if (performanceBudget) {
            runtimeMetrics.push(
                { label: '耗时', value: this._formatDuration(performanceBudget.totalDurationMs) },
                { label: 'Token', value: this._formatTokenEstimate(performanceBudget.estimatedInputTokens, performanceBudget.estimatedOutputTokens) },
                { label: '预算', value: perfStatusText }
            );
        }

        const stateClass = String(interactionState || 'idle').replace(/[^a-z0-9_-]/gi, '') || 'idle';
        el.hidden = false;
        el.className = `ai-agent-runtime is-${stateClass}`;
        if (performanceBudget?.budgetStatus === 'warning') {
            el.classList.add('has-budget-warning');
        }
        el.innerHTML = `
            <div class="ai-agent-runtime-main">
                <span class="ai-agent-runtime-kicker">Agent 状态机</span>
                <strong>${this._escapeHtml(stateLabels[interactionState] || interactionState || '待机')}</strong>
                <span>${this._escapeHtml(summary)}</span>
            </div>
            <div class="ai-agent-runtime-metrics">
                ${runtimeMetrics.map(metric => `
                    <span>
                        <small>${this._escapeHtml(String(metric.label))}</small>
                        <b>${this._escapeHtml(String(metric.value))}</b>
                    </span>
                `).join('')}
            </div>
            <div class="ai-agent-runtime-next">
                ${this._escapeHtml(nextAction)}
                ${perfWarningText ? `<span class="ai-agent-runtime-budget-note">${this._escapeHtml(perfWarningText)}</span>` : ''}
            </div>
        `;
    },

    _setWorkbenchState(state) {
        if (this.workbenchState === state) return;
        // Track last non-terminal state for failure recovery
        if (state !== AiWorkbenchStates.FAILED && state !== AiWorkbenchStates.CANCELLED && state !== AiWorkbenchStates.IDLE) {
            this._lastActiveWorkbenchState = state;
        }
        this.workbenchState = state;
        this._renderWorkbenchStateBar();
    },

    _renderWorkbenchStateBar() {
        const bar = this.container?.querySelector('#ai-workbench-state-bar');
        if (!bar) return;

        const state = this.workbenchState;
        if (state === AiWorkbenchStates.IDLE) {
            bar.innerHTML = '';
            bar.classList.remove('is-active');
            return;
        }

        bar.classList.add('is-active');
        const stateToStageIndex = {
            [AiWorkbenchStates.MATCHING_TEMPLATE]: 1,
            [AiWorkbenchStates.GENERATING]: 2,
            [AiWorkbenchStates.PARSING]: 2,
            [AiWorkbenchStates.VALIDATING]: 3,
            [AiWorkbenchStates.DRY_RUNNING]: 4,
            [AiWorkbenchStates.REVIEWING_PARAMETERS]: 5,
            [AiWorkbenchStates.READY_TO_APPLY]: 6,
            [AiWorkbenchStates.APPLYING]: 6,
            [AiWorkbenchStates.APPLIED]: 6,
            [AiWorkbenchStates.CLARIFYING]: 0,
            [AiWorkbenchStates.FAILED]: -1,
            [AiWorkbenchStates.CANCELLED]: -1
        };
        const activeIndex = stateToStageIndex[state] ?? -1;

        // For FAILED/CANCELLED: determine which stage actually failed
        let failedStageIndex = activeIndex;
        if ((state === AiWorkbenchStates.FAILED || state === AiWorkbenchStates.CANCELLED) && activeIndex === -1) {
            // Infer from stage timeline: find the last failed stage
            const timeline = this._workbenchStageTimeline || [];
            const failedStageKey = timeline.filter(s => s.status === 'failed').pop()?.stage;
            if (failedStageKey) {
                const stageKeyToOrderIndex = {
                    conversation: 0, scenario_match: 0, requirement_brief: 0, clarification: 0,
                    prompt_context: 1, template_gate: 1,
                    llm: 2, parse: 2,
                    validator: 3,
                    dryrun: 4,
                    parameters: 5,
                    apply: 6
                };
                failedStageIndex = stageKeyToOrderIndex[failedStageKey] ?? 0;
            } else {
                // Fallback: use last active state to determine stage index
                failedStageIndex = stateToStageIndex[this._lastActiveWorkbenchState] ?? 0;
            }
        }

        bar.innerHTML = WORKBENCH_STAGE_ORDER.map((stage, i) => {
            let cls = 'wb-stage';
            if (state === AiWorkbenchStates.FAILED || state === AiWorkbenchStates.CANCELLED) {
                if (i < failedStageIndex) {
                    cls += ' completed';
                } else if (i === failedStageIndex) {
                    cls += ' failed';
                }
            } else if (state === AiWorkbenchStates.APPLIED) {
                cls += ' completed';
            } else if (i < activeIndex) {
                cls += ' completed';
            } else if (i === activeIndex) {
                cls += ' active';
            }
            return `<span class="${cls}">${stage.label}</span>`;
        }).join('');
    },

    _renderStageTimeline(timeline) {
        const card = this.container?.querySelector('#ai-result-stage-timeline-card');
        const container = this.container?.querySelector('#ai-result-stage-timeline');
        const summaryBadge = this.container?.querySelector('#ai-stage-timeline-summary');
        if (!card || !container) return;

        if (!Array.isArray(timeline) || timeline.length === 0) {
            card.hidden = true;
            container.innerHTML = '';
            return;
        }

        card.hidden = false;
        const totalMs = timeline.reduce((sum, s) => sum + (s.durationMs || 0), 0);
        const totalSec = (totalMs / 1000).toFixed(1);
        if (summaryBadge) {
            summaryBadge.textContent = `${timeline.length} 阶段 · ${totalSec}s`;
        }

        container.innerHTML = `
            <details class="ai-stage-timeline-details">
                <summary>${timeline.length} 个阶段，总耗时 ${totalSec}s</summary>
                <div class="ai-stage-timeline-list">
                    ${timeline.map(stage => {
                        const label = STAGE_DIAGNOSTIC_LABELS[stage.stage] || stage.stage;
                        const status = stage.status || 'completed';
                        const duration = stage.durationMs != null ? `${stage.durationMs}ms` : '--';
                        const statusIcon = status === 'completed' ? '&#10003;'
                            : status === 'failed' ? '&#10007;'
                            : status === 'warning' ? '&#9888;'
                            : '&#9675;';
                        const statusClass = status === 'failed' ? 'is-failed'
                            : status === 'warning' ? 'is-warning'
                            : 'is-ok';
                        return `
                            <div class="ai-stage-timeline-item ${statusClass}">
                                <span class="ai-stage-icon">${statusIcon}</span>
                                <span class="ai-stage-label">${this._escapeHtml(label)}</span>
                                <span class="ai-stage-summary">${this._escapeHtml(stage.summary || '')}</span>
                                <span class="ai-stage-duration">${duration}</span>
                            </div>
                        `;
                    }).join('')}
                </div>
            </details>
        `;
    }
};
