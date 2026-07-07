const PUBLIC_LIVE_DEFAULT_TTL_MS = 1800;
const PUBLIC_LIVE_RUNNING_TTL_MS = 0;

const PLAN_PUBLIC_EVENT_DEFINITIONS = {
    'plan.created': {
        channel: 'plan',
        kind: 'status',
        phase: 'plan',
        title: '规划已创建',
        summary: '已创建 Plan Run，公开进度将通过事件流更新。',
        status: 'completed',
        visibility: 'ephemeral',
        ttlMs: 1200
    },
    'plan.started': {
        channel: 'plan',
        kind: 'status',
        phase: 'plan',
        title: '正在进入规划阶段...',
        summary: '规划运行已启动。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    'semantic.started': {
        channel: 'semantic',
        kind: 'model_call',
        phase: 'semantic_extraction',
        title: '正在抽取视觉语义...',
        summary: '正在抽取任务类型、检测对象、属性、OK/NG 条件和输入源。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    'semantic.completed': {
        channel: 'semantic',
        kind: 'status',
        phase: 'semantic_extraction',
        title: '语义抽取完成',
        summary: '语义理解来自模型，已生成公开结构化摘要。',
        status: 'completed',
        visibility: 'persistent'
    },
    'semantic.failed': {
        channel: 'semantic',
        kind: 'failure',
        phase: 'semantic_extraction',
        title: '语义抽取失败',
        summary: '语义抽取模型不可用，当前为规则降级解析。',
        status: 'failed',
        visibility: 'persistent'
    },
    'semantic.fallback.used': {
        channel: 'semantic',
        kind: 'warning',
        phase: 'semantic_fallback_used',
        title: '已启用语义规则降级',
        summary: '语义抽取模型不可用，当前为规则降级解析。',
        status: 'warning',
        visibility: 'persistent'
    },
    'plan.context.started': {
        channel: 'plan',
        kind: 'status',
        phase: 'collecting_context',
        title: '正在收集工程上下文...',
        summary: '正在收集公开需求、流程、模板、附件、算子和工站边界。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    'plan.context.completed': {
        channel: 'plan',
        kind: 'status',
        phase: 'collecting_context',
        title: '工程上下文已收集',
        summary: '已收集公开需求、流程、模板、附件、算子和工站边界。',
        status: 'completed',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
    },
    'plan.model.started': {
        channel: 'model',
        kind: 'model_call',
        phase: 'planning_with_model',
        title: '正在调用 Planner 模型生成规划候选...',
        summary: '模型正在生成公开结构化规划候选。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    'plan.model.completed': {
        channel: 'model',
        kind: 'model_call',
        phase: 'planning_with_model',
        title: 'Planner 已返回结构化规划候选',
        summary: '模型已返回公开结构化候选，等待契约校验。',
        status: 'completed',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
    },
    'plan.model.failed': {
        channel: 'model',
        kind: 'failure',
        phase: 'planning_with_model',
        title: 'Planner 未能产出可用规划',
        summary: 'Planner 未能产出可用规划，已启用规则兜底。',
        status: 'failed',
        visibility: 'persistent'
    },
    'plan.model.timeout': {
        channel: 'model',
        kind: 'failure',
        phase: 'planning_with_model',
        title: 'Planner 超时，已切换规则兜底',
        summary: 'Planner 超时，已启用规则兜底方案。',
        status: 'failed',
        visibility: 'persistent'
    },
    'plan.contract.started': {
        channel: 'validation',
        kind: 'status',
        phase: 'validating_plan_contract',
        title: '正在校验规划契约...',
        summary: '正在校验规划结构、问题质量、算子目录和模板约束。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    'plan.contract.completed': {
        channel: 'validation',
        kind: 'status',
        phase: 'validating_plan_contract',
        title: '规划契约已校验',
        summary: '规划已归一到公开 PlanModeResult 契约。',
        status: 'completed',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
    },
    'plan.safety.completed': {
        channel: 'validation',
        kind: 'status',
        phase: 'applying_safety_constraints',
        title: '安全约束与脱敏已应用',
        summary: '已应用脱敏、元数据边界、资源占位和 PLC 安全策略。',
        status: 'completed',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
    },
    'plan.fallback.used': {
        channel: 'fallback',
        kind: 'warning',
        phase: 'rule_fallback_used',
        title: '已启用规则兜底草案',
        summary: '当前方案为规则兜底草案，不是大模型 Planner 生成结果。',
        status: 'warning',
        visibility: 'persistent'
    },
    'plan.completed': {
        channel: 'plan',
        kind: 'result',
        phase: 'plan_ready',
        title: '规划已就绪',
        summary: '规划已完成，可以开始构建。',
        status: 'completed',
        visibility: 'persistent',
        ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
    },
    'plan.failed': {
        channel: 'plan',
        kind: 'failure',
        phase: 'plan',
        title: '规划失败',
        summary: '规划在完成前失败，请检查公开诊断后重试。',
        status: 'failed',
        visibility: 'persistent'
    },
    'plan.cancelled': {
        channel: 'plan',
        kind: 'status',
        phase: 'plan',
        title: '规划已取消',
        summary: '用户已取消本次规划。',
        status: 'cancelled',
        visibility: 'persistent'
    }
};

const PUBLIC_LIVE_DIAGNOSTIC_LABELS = {
    semantic_model_request_failed: '语义抽取请求失败',
    semantic_model_empty: '语义抽取返回为空',
    semantic_json_parse_failed: '语义抽取 JSON 解析失败',
    semantic_timeout: '语义抽取超时',
    semantic_unauthorized: '语义抽取鉴权失败',
    semantic_unknown_error: '语义抽取未知错误',
    completion_request_failed: 'Planner 请求失败',
    completion_empty: 'Planner 返回为空',
    planner_json_parse_failed: 'Planner JSON 解析失败',
    planner_contract_repair_failed: 'Planner 契约修复失败',
    planner_unauthorized: 'Planner 鉴权失败',
    planner_timeout: 'Planner 超时',
    planner_failed: 'Planner 失败',
    timeout: '超时'
};

const PUBLIC_LIVE_FALLBACK_LABELS = {
    semantic_model_request_failed: '语义模型不可用，使用规则降级',
    semantic_model_empty: '语义模型返回为空，使用规则降级',
    semantic_json_parse_failed: '语义 JSON 解析失败，使用规则降级',
    semantic_timeout: '语义模型超时，使用规则降级',
    semantic_unauthorized: '语义模型鉴权失败，使用规则降级',
    semantic_unknown_error: '语义模型异常，使用规则降级',
    planner_failed: 'Planner 失败后启用规则兜底',
    planner_timeout: 'Planner 超时后启用规则兜底',
    planner_disabled: 'Planner 未启用，使用规则兜底',
    maturity_gate_blocked: '需求成熟度不足',
    rule_fallback: '规则兜底'
};

const PUBLIC_LIVE_FAILURE_LABELS = {
    tool_permission_denied: '工具权限被拒绝',
    duplicate_tool_call: '重复工具调用',
    manifest_risk_blocked: '运行包风险阻断',
    manifest_dryrun_failed: '运行包预演失败',
    package_not_ready: '运行包未就绪',
    package_readiness_blocked: '运行包就绪检查阻断',
    readiness_blocked: '就绪检查阻断',
    deployment_resource_pending: '部署资源待补',
    missing_resources: '缺少资源',
    station_incompatible: '工站兼容性阻断',
    station_compatibility_failed: '工站兼容性检查失败',
    operator_contract_failed: '算子契约失败',
    release_review_blocked: '发布复核阻断',
    workflow_draft_missing: '流程草稿缺失'
};

const WORKBENCH_PUBLIC_EVENT_DEFINITIONS = {
    idle: {
        channel: 'transport',
        kind: 'status',
        phase: 'idle',
        title: '工作台已复位',
        summary: '当前没有活跃的视觉智能体任务。',
        status: 'completed',
        visibility: 'ephemeral',
        ttlMs: 900
    },
    clarifying: {
        channel: 'plan',
        kind: 'status',
        phase: 'clarification',
        title: '正在澄清需求...',
        summary: '正在判断需求是否足够进入规划或构建。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    matching_template: {
        channel: 'plan',
        kind: 'status',
        phase: 'template_match',
        title: '正在匹配视觉模板...',
        summary: '正在根据公开需求和工程上下文匹配候选模板。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    generating: {
        channel: 'build',
        kind: 'status',
        phase: 'generation',
        title: '正在生成视觉流程草稿...',
        summary: '正在生成公开、可审计的视觉工作流草稿。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    parsing: {
        channel: 'build',
        kind: 'status',
        phase: 'parsing',
        title: '正在解析智能体输出...',
        summary: '正在将公开输出归一为可审计的流程契约。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    validating: {
        channel: 'validation',
        kind: 'status',
        phase: 'validation',
        title: '正在校验流程结构...',
        summary: '正在校验算子、端口、参数和模板约束。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    dry_running: {
        channel: 'validation',
        kind: 'status',
        phase: 'dryrun',
        title: '正在执行元数据预演...',
        summary: '正在检查运行包、就绪门禁和可部署风险。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    reviewing_parameters: {
        channel: 'validation',
        kind: 'warning',
        phase: 'parameters',
        title: '需要补齐人工参数',
        summary: '流程草稿已保留待补项，请确认参数或资源后继续。',
        status: 'warning',
        visibility: 'persistent'
    },
    ready_to_apply: {
        channel: 'build',
        kind: 'result',
        phase: 'apply_gate',
        title: '构建已完成，可应用到画布',
        summary: '流程草稿已就绪，可应用到画布继续编辑和复核。',
        status: 'completed',
        visibility: 'persistent',
        ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
    },
    applying: {
        channel: 'build',
        kind: 'status',
        phase: 'apply_gate',
        title: '正在应用流程草稿...',
        summary: '正在将已确认的流程草稿应用到画布。',
        status: 'running',
        visibility: 'ephemeral',
        ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
    },
    applied: {
        channel: 'build',
        kind: 'result',
        phase: 'applied',
        title: '流程草稿已应用',
        summary: '流程已应用到画布，请在流程页继续复核节点和参数。',
        status: 'completed',
        visibility: 'persistent',
        ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
    },
    failed: {
        channel: 'build',
        kind: 'failure',
        phase: 'failure',
        title: '工作台进入失败状态',
        summary: '当前任务失败，请查看公开诊断和首要修复建议。',
        status: 'failed',
        visibility: 'persistent'
    },
    cancelled: {
        channel: 'transport',
        kind: 'status',
        phase: 'cancelled',
        title: '任务已取消',
        summary: '当前任务已取消，旧事件不会继续污染当前对话。',
        status: 'cancelled',
        visibility: 'persistent'
    }
};

const PUBLIC_LIVE_TERMINAL_EVENT_TYPES = new Set([
    'run.completed',
    'run.failed',
    'run.cancelled',
    'plan.completed',
    'plan.failed',
    'plan.cancelled'
]);

const PUBLIC_LIVE_RESULT_EVENT_TYPES = new Set([
    'workflow.draft.updated',
    'readiness.checked',
    'package.readiness.checked',
    'manifest.dryrun.completed',
    'station.compatibility.completed',
    'operator.contract.completed',
    'release.review.completed',
    'artifact.created'
]);

export const aiPanelLiveEventsMixin = {
    _resetPublicLiveEventState({ clearLiveStatus = true } = {}) {
        this.publicLiveEventKeys = new Set();
        this.publicLiveEvents = [];
        this.publicLiveEventStats = this._createPublicLiveEventStats();
        this.publicLiveWorkbenchSequence = 0;
        if (this.publicLiveStatusTimer) {
            window.clearTimeout?.(this.publicLiveStatusTimer);
            this.publicLiveStatusTimer = null;
        }
        if (clearLiveStatus) {
            this._clearAssistantLiveStatus?.();
        }
    },

    _normalizePublicLiveEvent(rawEvent, { source = 'agent-run' } = {}) {
        const evt = this._normalizePublicLiveRawEvent(rawEvent);
        if (!evt) return null;

        const planDefinition = PLAN_PUBLIC_EVENT_DEFINITIONS[evt.eventType];
        const agentDefinition = planDefinition ? null : this._getAgentPublicLiveEventDefinition(evt);
        const definition = planDefinition || agentDefinition;
        if (!definition) return null;

        const payload = this._asObject?.(evt.payload) || {};
        const status = this._normalizePublicLiveStatus(definition.status || evt.status);
        const fallbackSummary = planDefinition && (evt.eventType === 'plan.model.failed' || evt.eventType === 'plan.model.timeout')
            ? definition.summary
            : '';
        const title = this._sanitizePublicLiveEventText(definition.title || evt.title || evt.stage || evt.eventType, 96);
        const eventSummary = definition.useEventSummary === false
            ? ''
            : (evt.summary || this._payloadString?.(payload, 'summary') || '');
        const summary = this._sanitizePublicLiveEventText(eventSummary || fallbackSummary || definition.summary || '', 220);
        const safeDiagnostics = this._collectPublicLiveDiagnostics(evt);
        const visibility = this._resolvePublicLiveVisibility(definition.visibility, {
            status,
            kind: definition.kind,
            eventType: evt.eventType,
            safeDiagnostics
        });
        const eventKey = `${evt.runId}:${evt.sequence}:${evt.eventType}`;

        return {
            eventId: eventKey,
            runId: evt.runId,
            requestId: this.activePlanRunRequestId || this.activeGenerateRequestId || '',
            sequence: evt.sequence,
            channel: definition.channel || 'build',
            kind: definition.kind || 'status',
            phase: definition.phase || evt.stage || 'run',
            title,
            summary,
            status,
            visibility,
            ttlMs: definition.ttlMs,
            dedupeKey: eventKey,
            eventType: evt.eventType,
            source,
            metadataOnly: true,
            redactionPass: true,
            safeDiagnostics
        };
    },

    _routePublicLiveEvent(publicEvent) {
        if (!publicEvent?.runId || !publicEvent?.dedupeKey) {
            this._recordPublicLiveEventDrop('dropped');
            return false;
        }
        if (!this._isPublicLiveEventForActiveRun(publicEvent)) {
            this._recordPublicLiveEventDrop('stale');
            return false;
        }

        this.publicLiveEventKeys = this.publicLiveEventKeys instanceof Set
            ? this.publicLiveEventKeys
            : new Set();
        if (this.publicLiveEventKeys.has(publicEvent.dedupeKey)) {
            this._recordPublicLiveEventDrop('duplicate');
            return false;
        }

        this.publicLiveEventKeys.add(publicEvent.dedupeKey);
        this.publicLiveEvents = Array.isArray(this.publicLiveEvents)
            ? this.publicLiveEvents
            : [];
        this.publicLiveEvents.push(publicEvent);
        this._recordPublicLiveEventDrop('accepted');

        if (this._shouldRenderEphemeralEvent(publicEvent)) {
            this._renderAssistantLiveStatus(publicEvent);
            this._recordPublicLiveEventDrop('ephemeral');
        }

        if (this._shouldPersistEvent(publicEvent)) {
            this._appendPublicLiveEventProcessLine(publicEvent);
            this._recordPublicLiveEventDrop('persistent');
        }

        if (this._shouldShowDiagnosticEvent(publicEvent)) {
            this._renderPublicLiveDiagnosticEvent(publicEvent);
            this._recordPublicLiveEventDrop('diagnostic');
        }

        if (PUBLIC_LIVE_TERMINAL_EVENT_TYPES.has(publicEvent.eventType) && publicEvent.status !== 'running') {
            this._scheduleAssistantLiveStatusClear(publicEvent.ttlMs ?? PUBLIC_LIVE_DEFAULT_TTL_MS);
        }

        return true;
    },

    _createPublicLiveEventStats() {
        return {
            accepted: 0,
            dropped: 0,
            duplicate: 0,
            stale: 0,
            ephemeral: 0,
            persistent: 0,
            diagnostic: 0
        };
    },

    _recordPublicLiveEventDrop(kind) {
        this.publicLiveEventStats = this.publicLiveEventStats || this._createPublicLiveEventStats();
        const key = String(kind || 'dropped').trim();
        if (!Object.prototype.hasOwnProperty.call(this.publicLiveEventStats, key)) {
            this.publicLiveEventStats.dropped += 1;
            return;
        }

        this.publicLiveEventStats[key] += 1;
    },

    _getPublicLiveEventStats() {
        return {
            ...this._createPublicLiveEventStats(),
            ...(this.publicLiveEventStats || {})
        };
    },

    _publishWorkbenchStatePublicEvent(state, previousState = '') {
        const publicEvent = this._normalizeWorkbenchStatePublicEvent(state, previousState);
        return this._routePublicLiveEvent(publicEvent);
    },

    _normalizeWorkbenchStatePublicEvent(state, previousState = '') {
        const normalizedState = String(state || '').trim().toLowerCase();
        const definition = WORKBENCH_PUBLIC_EVENT_DEFINITIONS[normalizedState];
        if (!definition) return null;

        this.publicLiveWorkbenchSequence = Number(this.publicLiveWorkbenchSequence || 0) + 1;
        const runId = String(
            this.activeAgentRunId ||
            this.activePlanRunId ||
            this.activeGenerateRequestId ||
            this.activePlanRequestId ||
            'workbench'
        ).trim();
        const sequence = this.publicLiveWorkbenchSequence;
        const eventType = `workbench.state.${normalizedState}`;
        const eventKey = `${runId}:${sequence}:${eventType}`;

        return {
            eventId: eventKey,
            runId,
            requestId: this.activePlanRunRequestId || this.activeGenerateRequestId || '',
            sequence,
            channel: definition.channel,
            kind: definition.kind,
            phase: definition.phase,
            title: this._sanitizePublicLiveEventText(definition.title, 96),
            summary: this._sanitizePublicLiveEventText(definition.summary, 220),
            status: definition.status,
            visibility: definition.visibility,
            ttlMs: definition.ttlMs,
            dedupeKey: eventKey,
            eventType,
            source: 'workbench-state',
            metadataOnly: true,
            redactionPass: true,
            safeDiagnostics: {
                firstFixRecommendation: normalizedState === 'failed'
                    ? '请查看最近一条失败事件，按首要修复建议补齐配置、资源或契约后重试。'
                    : '',
                previousWorkbenchState: this._sanitizePublicLiveDiagnosticCode(previousState),
                workbenchState: this._sanitizePublicLiveDiagnosticCode(normalizedState)
            }
        };
    },

    _buildPublicLiveEventSnapshot({ runId = '' } = {}) {
        const expectedRunId = String(runId || '').trim();
        return (Array.isArray(this.publicLiveEvents) ? this.publicLiveEvents : [])
            .filter(evt => !expectedRunId || evt.runId === expectedRunId)
            .slice()
            .sort((a, b) => Number(a.sequence || 0) - Number(b.sequence || 0))
            .map(evt => this._sanitizePublicLiveSnapshotEvent(evt))
            .filter(Boolean);
    },

    _replayPublicLiveEventSnapshot(snapshot = [], { clear = true, runId = '' } = {}) {
        if (!Array.isArray(snapshot)) return 0;
        if (clear) {
            this._resetPublicLiveEventState?.();
        }

        const expectedRunId = String(runId || '').trim();
        let count = 0;
        snapshot
            .map(evt => this._sanitizePublicLiveSnapshotEvent(evt))
            .filter(evt => evt && (!expectedRunId || evt.runId === expectedRunId))
            .forEach(evt => {
                if (this._routePublicLiveEvent(evt)) {
                    count += 1;
                }
            });
        return count;
    },

    _sanitizePublicLiveSnapshotEvent(evt) {
        if (!evt || typeof evt !== 'object') return null;
        const runId = this._sanitizePublicLiveDiagnosticCode(evt.runId);
        const eventType = this._sanitizePublicLiveDiagnosticCode(evt.eventType);
        if (!runId || !eventType) return null;
        const sequence = Number(evt.sequence || 0);
        const channel = this._sanitizePublicLiveDiagnosticCode(evt.channel || 'build');
        const kind = this._sanitizePublicLiveDiagnosticCode(evt.kind || 'status');
        const phase = this._sanitizePublicLiveDiagnosticCode(evt.phase || 'run');
        const status = this._normalizePublicLiveStatus(evt.status);
        const visibility = ['ephemeral', 'persistent', 'diagnostic'].includes(String(evt.visibility || '').trim())
            ? String(evt.visibility).trim()
            : this._resolvePublicLiveVisibility('', { status, kind, eventType, safeDiagnostics: evt.safeDiagnostics });
        const safeDiagnostics = this._sanitizePublicLiveDiagnostics(evt.safeDiagnostics || {});
        const dedupeKey = `${runId}:${Number.isFinite(sequence) ? sequence : 0}:${eventType}`;

        return {
            eventId: this._sanitizePublicLiveDiagnosticCode(evt.eventId) || dedupeKey,
            runId,
            requestId: this._sanitizePublicLiveDiagnosticCode(evt.requestId),
            sequence: Number.isFinite(sequence) ? sequence : 0,
            channel,
            kind,
            phase,
            title: this._sanitizePublicLiveEventText(evt.title, 96),
            summary: this._sanitizePublicLiveEventText(evt.summary, 220),
            status,
            visibility,
            ttlMs: Number.isFinite(Number(evt.ttlMs)) ? Number(evt.ttlMs) : undefined,
            dedupeKey,
            eventType,
            source: this._sanitizePublicLiveDiagnosticCode(evt.source || 'snapshot'),
            metadataOnly: true,
            redactionPass: true,
            safeDiagnostics
        };
    },

    _shouldRenderEphemeralEvent(publicEvent) {
        return publicEvent?.visibility === 'ephemeral' ||
            (publicEvent?.status === 'running' && publicEvent?.visibility !== 'persistent');
    },

    _shouldPersistEvent(publicEvent) {
        if (!publicEvent) return false;
        return publicEvent.visibility === 'persistent' ||
            publicEvent.visibility === 'diagnostic' ||
            publicEvent.status === 'failed' ||
            publicEvent.status === 'warning' ||
            publicEvent.status === 'cancelled' ||
            publicEvent.kind === 'failure' ||
            publicEvent.kind === 'result';
    },

    _shouldShowDiagnosticEvent(publicEvent) {
        if (!publicEvent) return false;
        const diagnostics = publicEvent.safeDiagnostics || {};
        const hasDiagnostics = Object.values(diagnostics).some(value => String(value || '').trim());
        return hasDiagnostics && (
            publicEvent.kind === 'failure' ||
            publicEvent.status === 'failed' ||
            publicEvent.status === 'warning' ||
            publicEvent.channel === 'fallback' ||
            publicEvent.visibility === 'diagnostic'
        );
    },

    _normalizePublicLiveRawEvent(rawEvent) {
        if (!rawEvent || typeof rawEvent !== 'object') return null;
        const runId = String(rawEvent.runId ?? rawEvent.RunId ?? '').trim();
        const eventType = String(rawEvent.eventType ?? rawEvent.EventType ?? '').trim();
        if (!runId || !eventType) return null;

        const sequence = Number(rawEvent.sequence ?? rawEvent.Sequence ?? 0);
        return {
            runId,
            sequence: Number.isFinite(sequence) ? sequence : 0,
            eventType,
            stage: String(rawEvent.stage ?? rawEvent.Stage ?? '').trim(),
            title: String(rawEvent.title ?? rawEvent.Title ?? '').trim(),
            summary: String(rawEvent.summary ?? rawEvent.Summary ?? '').trim(),
            status: String(rawEvent.status ?? rawEvent.Status ?? '').trim().toLowerCase(),
            payload: rawEvent.payload ?? rawEvent.Payload ?? null,
            metadataOnly: Boolean(rawEvent.metadataOnly ?? rawEvent.MetadataOnly ?? true),
            redactionPass: Boolean(rawEvent.redactionPass ?? rawEvent.RedactionPass ?? true)
        };
    },

    _getAgentPublicLiveEventDefinition(evt) {
        const eventType = String(evt?.eventType || '').trim();
        const status = this._normalizePublicLiveStatus(evt?.status);
        const payload = this._asObject?.(evt?.payload) || {};
        const stageLabel = this._getAgentRunStageLabel?.(evt?.stage) || evt?.stage || '运行';

        if (eventType === 'assistant.brief') {
            return null;
        }

        if (eventType === 'run.started') {
            return {
                channel: 'build',
                kind: 'status',
                phase: 'run',
                title: '视觉智能体运行已启动',
                summary: '公开进度会在此处持续更新。',
                status: 'running',
                visibility: 'ephemeral',
                ttlMs: PUBLIC_LIVE_RUNNING_TTL_MS
            };
        }

        if (eventType === 'run.completed') {
            return {
                channel: 'build',
                kind: 'result',
                phase: 'run',
                title: '构建已完成',
                summary: evt.summary || '构建完成，等待复核可应用草稿。',
                status: 'completed',
                visibility: 'persistent',
                ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
            };
        }

        if (eventType === 'run.failed') {
            return {
                channel: 'build',
                kind: 'failure',
                phase: 'run',
                title: '构建失败',
                summary: evt.summary || '构建失败，请查看公开诊断。',
                status: 'failed',
                visibility: 'persistent'
            };
        }

        if (eventType === 'run.cancelled') {
            return {
                channel: 'build',
                kind: 'status',
                phase: 'run',
                title: '运行已取消',
                summary: evt.summary || '用户已取消本次运行。',
                status: 'cancelled',
                visibility: 'persistent'
            };
        }

        if (eventType === 'stage.started' || eventType === 'stage.completed') {
            const completed = eventType === 'stage.completed' || status === 'completed';
            return {
                channel: 'build',
                kind: 'status',
                phase: evt.stage || 'run',
                title: completed ? `${stageLabel}已完成` : `正在${stageLabel}...`,
                summary: evt.summary || '',
                status: completed ? 'completed' : 'running',
                visibility: 'ephemeral',
                ttlMs: completed ? PUBLIC_LIVE_DEFAULT_TTL_MS : PUBLIC_LIVE_RUNNING_TTL_MS
            };
        }

        if (eventType.startsWith('tool.call.') || eventType.startsWith('tool_call.')) {
            const toolName = this._payloadString?.(payload, 'toolName') ||
                this._payloadString?.(payload, 'name') ||
                this._deriveToolNameFromTitle?.(evt.title) ||
                '';
            const toolLabel = this._formatToolName?.(toolName) || this._localizeDisplayText?.(toolName) || toolName || '工具';
            const failed = status === 'failed' || status === 'warning' || eventType === 'tool.call.failed' || eventType === 'tool_call.denied';
            const completed = status === 'completed' || eventType.endsWith('.completed');
            return {
                channel: 'tool',
                kind: 'tool_call',
                phase: evt.stage || 'tool',
                title: failed
                    ? `工具调用异常: ${toolLabel}`
                    : (completed ? `工具已完成: ${toolLabel}` : `正在执行工具: ${toolLabel}`),
                summary: evt.summary || '',
                status: failed ? 'warning' : (completed ? 'completed' : 'running'),
                visibility: failed ? 'persistent' : 'ephemeral',
                ttlMs: completed ? PUBLIC_LIVE_DEFAULT_TTL_MS : PUBLIC_LIVE_RUNNING_TTL_MS
            };
        }

        if (eventType === 'tool_result.appended') {
            return {
                channel: 'tool',
                kind: 'status',
                phase: evt.stage || 'tool',
                title: '工具结果已回填',
                summary: evt.summary || '',
                status: 'completed',
                visibility: 'ephemeral',
                ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
            };
        }

        if (eventType.startsWith('tool_loop.')) {
            const failed = status === 'failed' || status === 'warning' || eventType === 'tool_loop.fallback' || eventType === 'tool_loop.draft.rejected';
            const completed = status === 'completed';
            return {
                channel: 'build',
                kind: failed ? 'warning' : 'status',
                phase: evt.stage || 'tool_loop',
                title: failed
                    ? (eventType === 'tool_loop.fallback' ? 'Tool Loop 已回退稳定链路' : 'Tool Loop 需要处理')
                    : (completed ? 'Tool Loop 步骤已完成' : 'Tool Loop 正在执行...'),
                summary: evt.summary || '',
                status: failed ? 'warning' : (completed ? 'completed' : 'running'),
                visibility: failed ? 'persistent' : 'ephemeral',
                ttlMs: completed ? PUBLIC_LIVE_DEFAULT_TTL_MS : PUBLIC_LIVE_RUNNING_TTL_MS
            };
        }

        if (PUBLIC_LIVE_RESULT_EVENT_TYPES.has(eventType)) {
            const failed = status === 'failed';
            const warning = status === 'warning';
            return {
                channel: eventType.includes('readiness') || eventType.includes('review') ? 'validation' : 'build',
                kind: failed ? 'failure' : (warning ? 'warning' : 'result'),
                phase: evt.stage || 'artifact',
                title: evt.title || `${stageLabel}已完成`,
                summary: evt.summary || '',
                status: status || 'completed',
                visibility: 'persistent',
                ttlMs: PUBLIC_LIVE_DEFAULT_TTL_MS
            };
        }

        if (evt.stage || evt.title || evt.summary) {
            return {
                channel: 'build',
                kind: 'status',
                phase: evt.stage || 'run',
                title: evt.title || stageLabel,
                summary: evt.summary || '',
                status: status || 'completed',
                visibility: status === 'running' ? 'ephemeral' : 'persistent',
                ttlMs: status === 'running' ? PUBLIC_LIVE_RUNNING_TTL_MS : PUBLIC_LIVE_DEFAULT_TTL_MS
            };
        }

        return null;
    },

    _normalizePublicLiveStatus(status) {
        const value = String(status || '').trim().toLowerCase();
        if (value === 'started' || value === 'in_progress') return 'running';
        if (value === 'blocked') return 'warning';
        if (value === 'canceled') return 'cancelled';
        if (value === 'success') return 'completed';
        return value || 'completed';
    },

    _resolvePublicLiveVisibility(visibility, { status, kind, eventType, safeDiagnostics } = {}) {
        const explicit = String(visibility || '').trim();
        if (explicit) return explicit;
        if (status === 'running') return 'ephemeral';
        if (status === 'failed' || status === 'warning' || status === 'cancelled') return 'persistent';
        if (kind === 'failure' || kind === 'warning' || kind === 'result') return 'persistent';
        if (PUBLIC_LIVE_TERMINAL_EVENT_TYPES.has(eventType)) return 'persistent';
        if (safeDiagnostics && Object.values(safeDiagnostics).some(value => String(value || '').trim())) return 'diagnostic';
        return 'ephemeral';
    },

    _isPublicLiveEventForActiveRun(publicEvent) {
        const runId = String(publicEvent?.runId || '').trim();
        if (!runId) return false;
        const activeRunIds = [
            this.activeAgentRunId,
            this.activePlanRunId
        ].map(value => String(value || '').trim()).filter(Boolean);
        return activeRunIds.length === 0 || activeRunIds.includes(runId);
    },

    _renderAssistantLiveStatus(publicEvent) {
        const turn = this.activeAssistantTurn;
        const liveEl = this._ensureAssistantLiveStatusElement(turn);
        if (!liveEl || !publicEvent) return;

        if (this.publicLiveStatusTimer) {
            window.clearTimeout?.(this.publicLiveStatusTimer);
            this.publicLiveStatusTimer = null;
        }

        const tone = this._getPublicLiveTone(publicEvent);
        const summary = publicEvent.summary && publicEvent.summary !== publicEvent.title
            ? publicEvent.summary
            : '';
        liveEl.hidden = false;
        liveEl.className = `ai-assistant-live-status is-${tone}`;
        liveEl.dataset.eventId = publicEvent.eventId;
        liveEl.dataset.status = publicEvent.status;
        liveEl.innerHTML = `
            <span class="ai-assistant-live-dot"></span>
            <span class="ai-assistant-live-copy">
                <strong>${this._escapeHtml(publicEvent.title)}</strong>
                ${summary ? `<small>${this._escapeHtml(summary)}</small>` : ''}
            </span>
        `;

        this._notifyLiveEventRendered();

        if (publicEvent.status === 'completed' || publicEvent.status === 'cancelled') {
            this._scheduleAssistantLiveStatusClear(publicEvent.ttlMs ?? PUBLIC_LIVE_DEFAULT_TTL_MS);
        }
    },

    _ensureAssistantLiveStatusElement(turn = this.activeAssistantTurn) {
        if (!turn) return null;
        if (turn.liveStatusEl) return turn.liveStatusEl;

        const existing = turn.card?.querySelector?.('.ai-assistant-live-status') || null;
        if (existing) {
            turn.liveStatusEl = existing;
            return existing;
        }

        if (!turn.card?.appendChild || typeof document === 'undefined') return null;
        const liveEl = document.createElement('div');
        liveEl.className = 'ai-assistant-live-status';
        liveEl.hidden = true;
        turn.card.appendChild(liveEl);
        turn.liveStatusEl = liveEl;
        return liveEl;
    },

    _scheduleAssistantLiveStatusClear(ttlMs = PUBLIC_LIVE_DEFAULT_TTL_MS) {
        const delay = Number(ttlMs);
        if (!Number.isFinite(delay) || delay <= 0) return;
        if (this.publicLiveStatusTimer) {
            window.clearTimeout?.(this.publicLiveStatusTimer);
        }
        this.publicLiveStatusTimer = window.setTimeout?.(() => {
            this._clearAssistantLiveStatus();
            this.publicLiveStatusTimer = null;
        }, delay);
    },

    _clearAssistantLiveStatus(turn = this.activeAssistantTurn) {
        const liveEl = turn?.liveStatusEl || turn?.card?.querySelector?.('.ai-assistant-live-status');
        if (!liveEl) return;
        liveEl.hidden = true;
        liveEl.classList?.remove?.('is-running', 'is-success', 'is-warning', 'is-failed', 'is-cancelled');
        liveEl.textContent = '';
    },

    _appendPublicLiveEventProcessLine(publicEvent) {
        if (!publicEvent) return;
        const title = publicEvent.title || '公开事件';
        const summary = publicEvent.summary && publicEvent.summary !== title ? publicEvent.summary : '';
        const diagnostics = this._buildPublicLiveDiagnosticSummary(publicEvent);
        const text = [title, summary, diagnostics].filter(Boolean).join('\n');
        const item = this._updateThinkingStep?.(
            publicEvent.runId,
            `public:${publicEvent.dedupeKey}`,
            text
        );
        if (!item) return;

        const tone = this._getPublicLiveTone(publicEvent);
        item.className = `ai-agent-run-step is-${tone}`;
        item.dataset.eventType = publicEvent.eventType || '';
        item.dataset.publicChannel = publicEvent.channel || '';
        item.dataset.publicKind = publicEvent.kind || '';
        item.dataset.publicVisibility = publicEvent.visibility || '';
    },

    _renderPublicLiveDiagnosticEvent(publicEvent) {
        const turn = this.activeAssistantTurn;
        if (!turn?.failureSection || !turn?.failureBody || !publicEvent) return;
        if (publicEvent.source === 'workbench-state' && String(turn.failureBody.innerHTML || '').trim()) {
            return;
        }

        const diagnostics = publicEvent.safeDiagnostics || {};
        const code = diagnostics.plannerFailureCode || diagnostics.sanitizedErrorKind || '';
        const codeLabel = this._formatPublicLiveDiagnosticCode(code);
        const fallbackReason = diagnostics.fallbackReason || '';
        const fallbackLabel = this._formatPublicLiveFallbackReason(fallbackReason);
        const stageLabel = this._formatPublicLiveStage(diagnostics.plannerFailureStage);
        const message = diagnostics.sanitizedErrorMessage || '';
        const firstFix = diagnostics.firstFixRecommendation || this._getPublicLiveFirstFixRecommendation(code);
        const rows = [
            diagnostics.semanticSource ? ['语义来源', this._formatSemanticSourceLabel(diagnostics.semanticSource)] : null,
            diagnostics.semanticTaskType ? ['任务类型', this._formatRequirementTaskTypeLabel?.(diagnostics.semanticTaskType) || diagnostics.semanticTaskType] : null,
            diagnostics.semanticObject ? ['检测对象', diagnostics.semanticObject] : null,
            diagnostics.semanticAttribute ? ['目标属性', diagnostics.semanticAttribute] : null,
            diagnostics.semanticOkCondition ? ['OK 条件', diagnostics.semanticOkCondition] : null,
            diagnostics.semanticNgCondition ? ['NG 条件', diagnostics.semanticNgCondition] : null,
            diagnostics.semanticImageSource ? ['输入源', diagnostics.semanticImageSource] : null,
            diagnostics.toolName ? ['工具', this._formatToolName?.(diagnostics.toolName) || diagnostics.toolName] : null,
            stageLabel ? ['失败阶段', stageLabel] : null,
            codeLabel ? ['失败原因', codeLabel] : null,
            diagnostics.warningCode ? ['风险代码', this._formatPublicLiveDiagnosticCode(diagnostics.warningCode)] : null,
            diagnostics.blockedReasons ? ['阻断项', diagnostics.blockedReasons] : null,
            fallbackLabel ? ['兜底原因', fallbackLabel] : null,
            diagnostics.reportId ? ['报告', diagnostics.reportId] : null,
            message ? ['安全摘要', message] : null,
            firstFix ? ['下一步', firstFix] : null
        ].filter(Boolean);

        const fallbackCopy = fallbackReason
            ? '<div>当前方案为规则兜底草案，不是大模型 Planner 生成结果。</div>'
            : '';

        turn.failureSection.hidden = false;
        turn.failureBody.innerHTML = `
            <div class="ai-assistant-failure-summary">${this._escapeHtml(publicEvent.title)}</div>
            ${publicEvent.summary ? `<div class="ai-assistant-failure-meta"><span>公开摘要</span>${this._escapeHtml(publicEvent.summary)}</div>` : ''}
            ${fallbackCopy ? `<div class="ai-assistant-failure-meta"><span>方案来源</span>${fallbackCopy}</div>` : ''}
            <details class="ai-public-live-diagnostics">
                <summary>诊断详情</summary>
                <div class="ai-public-live-diagnostics-body">
                    ${rows.map(([label, value]) => `
                        <div class="ai-assistant-failure-meta">
                            <span>${this._escapeHtml(label)}</span>${this._escapeHtml(value)}
                        </div>
                    `).join('')}
                </div>
            </details>
        `;
        this._notifyLiveEventRendered();
    },

    _collectPublicLiveDiagnostics(evt) {
        const payload = this._asObject?.(evt?.payload) || {};
        const planResult = this._asObject?.(
            payload.planResult ||
            payload.PlanResult ||
            payload.planModeResult ||
            payload.PlanModeResult ||
            payload.result ||
            payload.Result
        ) || {};
        const sources = [
            payload,
            this._asObject?.(payload.metadata || payload.Metadata) || {},
            this._asObject?.(payload.diagnostic || payload.Diagnostic) || {},
            this._asObject?.(payload.safeDiagnostics || payload.SafeDiagnostics) || {},
            planResult
        ];
        const read = (...names) => {
            for (const source of sources) {
                for (const name of names) {
                    const value = source?.[name] ?? source?.[this._capitalizeFirst?.(name) || this._toPascalCase?.(name)];
                    if (value !== undefined && value !== null && String(value).trim()) {
                        return value;
                    }
                }
            }
            return '';
        };
        const readArray = (...names) => {
            for (const source of sources) {
                for (const name of names) {
                    const value = source?.[name] ?? source?.[this._capitalizeFirst?.(name) || this._toPascalCase?.(name)];
                    if (Array.isArray(value) && value.length > 0) {
                        return value;
                    }
                }
            }
            return [];
        };
        const eventType = String(evt?.eventType || '').trim();
        const stage = String(evt?.stage || '').trim();
        const semanticSource = this._sanitizePublicLiveDiagnosticCode(read('semanticSource'));
        const semanticTaskType = this._sanitizePublicLiveDiagnosticCode(read('taskType'));
        const semanticObject = this._sanitizePublicLiveEventText(read('inspectionObject'), 100);
        const semanticAttribute = this._sanitizePublicLiveEventText(read('targetAttribute'), 100);
        const semanticOkCondition = this._sanitizePublicLiveEventText(read('okCondition'), 140);
        const semanticNgCondition = this._sanitizePublicLiveEventText(read('ngCondition'), 140);
        const semanticImageSource = this._sanitizePublicLiveEventText(read('imageSource'), 80);
        const toolName = this._sanitizePublicLiveEventText(
            read('toolName', 'name') || this._deriveToolNameFromTitle?.(evt?.title) || '',
            80
        );
        const semanticFailureCode = this._sanitizePublicLiveDiagnosticCode(read('failureCode'));
        const fallbackReason = this._sanitizePublicLiveDiagnosticCode(read('fallbackReason') ||
            (eventType === 'plan.fallback.used' ? 'rule_fallback' : '') ||
            (eventType === 'semantic.fallback.used' ? (semanticFailureCode || 'semantic_unknown_error') : ''));
        const failureReason = this._sanitizePublicLiveDiagnosticCode(read('failureReason', 'rejectionReason', 'reason', 'failureCode'));
        const warningCode = this._sanitizePublicLiveDiagnosticCode(read('warningCode'));
        const errorCode = this._sanitizePublicLiveDiagnosticCode(read('plannerFailureCode', 'errorCode', 'failureCode') ||
            (eventType === 'plan.model.timeout' ? 'planner_timeout' : ''));
        const plannerFailureCode = this._sanitizePublicLiveDiagnosticCode(
            errorCode
        );
        const sanitizedErrorKind = this._sanitizePublicLiveDiagnosticCode(read('sanitizedErrorKind')) || plannerFailureCode;
        const plannerFailureStage = this._sanitizePublicLiveDiagnosticCode(read('plannerFailureStage', 'stage') || evt?.stage || '');
        const sanitizedErrorMessage = this._sanitizePublicLiveEventText(read('sanitizedErrorMessage', 'message'), 220);
        const blockedReasons = readArray('blockedReasons', 'blockingReasons', 'deploymentBlockers')
            .map(item => this._sanitizePublicLiveEventText(item, 80))
            .filter(Boolean)
            .slice(0, 4)
            .join('、');
        const reportId = this._sanitizePublicLiveDiagnosticCode(read('reportId', 'manifestId', 'reviewId'));
        const effectiveCode = plannerFailureCode || warningCode || failureReason || this._inferPublicLiveFailureCode(evt);
        const firstFixRecommendation = this._sanitizePublicLiveEventText(
            read('firstFixRecommendation') || this._getPublicLiveFirstFixRecommendation(effectiveCode || sanitizedErrorKind, {
                eventType,
                stage,
                toolName,
                blockedReasons
            }),
            180
        );

        return {
            fallbackReason,
            failureReason,
            semanticSource,
            semanticTaskType,
            semanticObject,
            semanticAttribute,
            semanticOkCondition,
            semanticNgCondition,
            semanticImageSource,
            plannerFailureStage,
            plannerFailureCode: plannerFailureCode || warningCode || failureReason,
            warningCode,
            sanitizedErrorKind: sanitizedErrorKind || warningCode || failureReason,
            sanitizedErrorMessage,
            firstFixRecommendation,
            blockedReasons,
            toolName,
            reportId
        };
    },

    _buildPublicLiveDiagnosticSummary(publicEvent) {
        const diagnostics = publicEvent?.safeDiagnostics || {};
        const parts = [];
        const codeLabel = this._formatPublicLiveDiagnosticCode(diagnostics.plannerFailureCode || diagnostics.sanitizedErrorKind);
        const warningLabel = this._formatPublicLiveDiagnosticCode(diagnostics.warningCode);
        const failureLabel = this._formatPublicLiveDiagnosticCode(diagnostics.failureReason);
        const fallbackLabel = this._formatPublicLiveFallbackReason(diagnostics.fallbackReason);
        const firstFix = diagnostics.firstFixRecommendation;

        if (diagnostics.semanticSource) parts.push(`语义来源：${this._formatSemanticSourceLabel(diagnostics.semanticSource)}`);
        if (diagnostics.semanticTaskType) parts.push(`任务类型：${this._formatRequirementTaskTypeLabel?.(diagnostics.semanticTaskType) || diagnostics.semanticTaskType}`);
        if (diagnostics.semanticObject) parts.push(`对象：${diagnostics.semanticObject}`);
        if (diagnostics.semanticAttribute) parts.push(`属性：${diagnostics.semanticAttribute}`);
        if (diagnostics.semanticOkCondition) parts.push(`OK：${diagnostics.semanticOkCondition}`);
        if (diagnostics.semanticNgCondition) parts.push(`NG：${diagnostics.semanticNgCondition}`);
        if (codeLabel) parts.push(`诊断：${codeLabel}`);
        if (!codeLabel && warningLabel) parts.push(`诊断：${warningLabel}`);
        if (!codeLabel && !warningLabel && failureLabel) parts.push(`诊断：${failureLabel}`);
        if (diagnostics.blockedReasons) parts.push(`阻断项：${diagnostics.blockedReasons}`);
        if (fallbackLabel) parts.push(`兜底原因：${fallbackLabel}`);
        if (firstFix) parts.push(`下一步：${firstFix}`);
        if (diagnostics.fallbackReason) {
            parts.push('当前方案为规则兜底草案，不是大模型 Planner 生成结果。');
        }
        return parts.join('\n');
    },

    _inferPublicLiveFailureCode(evt) {
        const eventType = String(evt?.eventType || '').trim();
        const stage = String(evt?.stage || '').trim();
        if (eventType === 'tool_call.denied') return 'tool_permission_denied';
        if (eventType === 'tool.call.failed') return `${stage || 'tool'}_failed`;
        if (eventType === 'readiness.checked') return 'readiness_blocked';
        if (eventType === 'package.readiness.checked') return 'package_readiness_blocked';
        if (eventType === 'manifest.dryrun.completed') return 'manifest_risk_blocked';
        if (eventType === 'station.compatibility.completed') return 'station_incompatible';
        if (eventType === 'operator.contract.completed') return 'operator_contract_failed';
        if (eventType === 'release.review.completed') return 'release_review_blocked';
        if (eventType === 'run.failed') return 'workflow_draft_missing';
        return '';
    },

    _getPublicLiveFirstFixRecommendation(code, context = {}) {
        const normalized = String(code || '').trim().toLowerCase();
        if (!normalized) return '';
        if (this._formatPlannerFailureHint) {
            const existing = this._formatPlannerFailureHint(normalized);
            if (existing) return existing;
        }

        switch (normalized) {
            case 'completion_request_failed':
                return '请检查网络、Planner 接口地址配置、模型服务和中转站状态。';
            case 'completion_empty':
                return '请检查 Planner 模型返回内容和中转站响应体。';
            case 'planner_json_parse_failed':
                return '请检查 Planner JSON 结构化输出契约。';
            case 'planner_contract_repair_failed':
                return '请检查 Planner 输出字段完整性和 RepairCandidate 容错。';
            case 'planner_unauthorized':
                return '请检查 Planner API Key、模型名和接口配置。';
            case 'planner_timeout':
                return '请稍后重试深度规划，或先复核规则兜底草案。';
            case 'tool_permission_denied':
                return '请移除被拒绝的工具意图，或改用只读/元数据工具链重试。';
            case 'readiness_blocked':
            case 'package_readiness_blocked':
            case 'deployment_resource_pending':
            case 'missing_resources':
                return '请先补齐缺失资源、人工参数或运行包元数据，再重新检查就绪状态。';
            case 'manifest_risk_blocked':
            case 'manifest_dryrun_failed':
                return '请检查运行包 manifest、资源引用和部署风险项，修复后重新预演。';
            case 'station_incompatible':
            case 'station_compatibility_failed':
                return '请检查目标工站能力、相机/模型/模板资源 allowlist 和运行配置。';
            case 'operator_contract_failed':
                return '请检查算子契约、端口、参数字段和版本兼容性。';
            case 'release_review_blocked':
                return '请先处理发布复核阻断项，再重新发起发布检查。';
            case 'workflow_draft_missing':
                return '请查看事件回放，确认 BuildResult 是否包含可回放流程草稿。';
            default:
                if (String(context.stage || '').includes('readiness')) {
                    return '请按就绪检查阻断项补齐资源、参数或运行包元数据。';
                }
                if (String(context.stage || '').includes('station')) {
                    return '请检查工站兼容性配置和资源 allowlist。';
                }
                if (String(context.stage || '').includes('operator')) {
                    return '请检查算子契约和参数字段完整性。';
                }
                return '';
        }
    },

    _formatPublicLiveDiagnosticCode(code) {
        const normalized = String(code || '').trim().toLowerCase();
        return PUBLIC_LIVE_DIAGNOSTIC_LABELS[normalized] ||
            PUBLIC_LIVE_FAILURE_LABELS[normalized] ||
            this._localizeDisplayText?.(normalized) ||
            normalized;
    },

    _formatPublicLiveFallbackReason(reason) {
        const normalized = String(reason || '').trim().toLowerCase();
        return PUBLIC_LIVE_FALLBACK_LABELS[normalized] || this._localizeDisplayText?.(normalized) || normalized;
    },

    _formatSemanticSourceLabel(source) {
        const normalized = String(source || '').trim().toLowerCase();
        if (normalized === 'model') return '模型';
        if (normalized === 'rule_fallback') return '规则降级';
        return this._localizeDisplayText?.(normalized) || normalized;
    },

    _formatPublicLiveStage(stage) {
        const normalized = String(stage || '').trim();
        if (!normalized) return '';
        return this._getAgentRunStageLabel?.(normalized) ||
            this._localizeDisplayText?.(normalized) ||
            normalized;
    },

    _getPublicLiveTone(publicEvent) {
        const status = String(publicEvent?.status || '').trim().toLowerCase();
        if (status === 'failed') return 'failed';
        if (status === 'warning') return 'warning';
        if (status === 'cancelled' || status === 'canceled') return 'cancelled';
        if (status === 'completed') return 'success';
        return 'running';
    },

    _sanitizePublicLiveDiagnosticCode(value) {
        let text = String(value ?? '').trim();
        if (!text) return '';
        text = this._redactPublicDiagnosticText?.(text) || text;
        return text
            .replace(/\b(?:rawPrompt|systemPrompt|userPrompt|chainOfThought|chain_of_thought|reasoningContent|reasoning_content)\b\s*[:=]\s*["']?[^"'\n,;}]+/gi, '')
            .replace(/chain[-_\s]?of[-_\s]?thought/gi, '')
            .replace(/\b(?:authorization|x-api-key|api[-_ ]?key|token|secret|baseUrl|base_url|headers?)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '')
            .replace(/[^A-Za-z0-9_.:-]/g, '')
            .slice(0, 96);
    },

    _sanitizePublicLiveDiagnostics(value) {
        const source = value && typeof value === 'object' ? value : {};
        const diagnostics = {};
        const codeLikeKeys = new Set([
            'fallbackReason',
            'failureReason',
            'plannerFailureStage',
            'plannerFailureCode',
            'warningCode',
            'sanitizedErrorKind',
            'toolName',
            'reportId',
            'semanticSource',
            'taskType',
            'failureCode',
            'previousWorkbenchState',
            'workbenchState'
        ]);
        Object.entries(source).forEach(([key, rawValue]) => {
            const safeKey = this._sanitizePublicLiveDiagnosticCode(key);
            if (!safeKey) return;
            diagnostics[safeKey] = codeLikeKeys.has(safeKey)
                ? this._sanitizePublicLiveDiagnosticCode(rawValue)
                : this._sanitizePublicLiveEventText(rawValue, 180);
        });
        return diagnostics;
    },

    _sanitizePublicLiveEventText(value, maxChars = 220) {
        let text = String(value ?? '').trim();
        if (!text) return '';

        const localized = this._localizeDisplayText?.(text);
        if (localized) {
            text = localized;
        }

        text = this._redactPublicDiagnosticText?.(text) || text;
        text = text
            .replace(/\b(?:rawPrompt|systemPrompt|userPrompt|chainOfThought|chain_of_thought|reasoningContent|reasoning_content)\b\s*[:=]\s*["']?[^"'\n,;}]+/gi, '[redacted]')
            .replace(/chain[-_\s]?of[-_\s]?thought/gi, '[redacted]')
            .replace(/\bBearer\s+[A-Za-z0-9._~+/=-]{8,}/gi, 'Bearer [redacted]')
            .replace(/\b(?:authorization|x-api-key|api[-_ ]?key|token|secret|baseUrl|base_url|headers?)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '[redacted]')
            .replace(/\bhttps?:\/\/[^\s"'<>|]+/gi, '[redacted:url]')
            .replace(/\bsk-[A-Za-z0-9_-]{8,}/gi, '[redacted]')
            .replace(/\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?::\d+)?\b/g, '[redacted:ip]')
            .replace(/\bDB\d+\.DB[XBWD]\d+(?:\.\d+)?\b/gi, '[redacted:plc]')
            .replace(/\bM\d+(?:\.\d+)?\b/gi, '[redacted:plc]')
            .replace(/\bD\d+\b/gi, '[redacted:plc]')
            .replace(/plc:\/\/[^\s"'<>|]+/gi, '[redacted:plc]')
            .replace(/(?:[a-z]:\\|\\\\)[^\s"'<>|]+/gi, '[redacted:path]')
            .replace(/(?:\/users\/|\/home\/|\/var\/|\/tmp\/|\/mnt\/|\/data\/|\/models\/|\/artifacts\/)[^\s"'<>|]+/gi, '[redacted:path]')
            .replace(/data:image\/[a-z0-9.+-]+;base64,[a-z0-9+/=\r\n]+/gi, '[redacted:image]')
            .replace(/(?<![a-z0-9+/=])(?:[a-z0-9+/]{96,}={0,2})(?![a-z0-9+/=])/gi, '[redacted]');

        return text.slice(0, maxChars);
    },

    _notifyLiveEventRendered() {
        if (this.userHasScrolledUp) {
            this.unreadStreamCount = (Number(this.unreadStreamCount) || 0) + 1;
            this._updateScrollBottomBtn?.();
            return;
        }

        this._scrollToBottom?.();
    }
};
