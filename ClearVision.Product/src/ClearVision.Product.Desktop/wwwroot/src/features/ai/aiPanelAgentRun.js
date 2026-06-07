import httpClient from '../../core/messaging/httpClient.js';
import { AiWorkbenchStates } from './aiPanelWorkbench.js';

const TERMINAL_EVENT_TYPES = new Set(['run.completed', 'run.failed', 'run.cancelled']);
const TOOL_EVENT_TYPES = new Set(['tool.call.started', 'tool.call.completed', 'tool.call.failed']);
const ARTIFACT_EVENT_TYPES = new Set([
    'artifact.created',
    'workflow.draft.updated',
    'readiness.checked',
    'package.readiness.checked',
    'manifest.dryrun.completed',
    'station.compatibility.completed',
    'operator.contract.completed',
    'release.review.completed'
]);

const STAGE_LABELS = {
    run: '运行',
    brief: '任务摘要',
    requirement_parsing: '需求解析',
    planner: 'Planner',
    tool_policy: '工具策略',
    workflow_draft: '工作流草稿',
    readiness: 'Readiness',
    manifest_dry_run: 'Manifest dry-run',
    package_readiness: 'Package readiness',
    station_compatibility: 'Station compatibility',
    operator_contract: 'Operator contract',
    release_review: 'Release review',
    artifact: '报告'
};

export const aiPanelAgentRunMixin = {
    _shouldUseAgentRunEventStream() {
        return Boolean(
            this.isVisionAgentDeveloperUiEnabled &&
            this.useVisionAgentGenerateFlow &&
            typeof window !== 'undefined' &&
            typeof window.EventSource === 'function'
        );
    },

    _resetAgentRunState({ close = true } = {}) {
        if (close) {
            this._closeAgentRunEventSource();
        }

        this.activeAgentRunId = null;
        this.activeAgentRunEventSource = null;
        this.activeAgentRunEvents = [];
        this.activeAgentRunEventKeys = new Set();
        this.agentRunStepMap = new Map();
        this.agentRunToolMap = new Map();
        this.agentRunArtifactMap = new Map();
    },

    _closeAgentRunEventSource() {
        if (!this.activeAgentRunEventSource) return;

        try {
            this.activeAgentRunEventSource.close();
        } catch {
            // EventSource close should never block UI cleanup.
        }

        this.activeAgentRunEventSource = null;
    },

    _buildAgentRunCreatePayload({
        normalizedDescription,
        normalizedHint,
        requestId,
        resolvedMode,
        flowPayload,
        attachmentPaths,
        normalizedTemplateSelection,
        agentGenerateFlowPayload
    }) {
        return {
            description: normalizedDescription,
            additionalContext: normalizedHint || null,
            mode: resolvedMode,
            requirementMode: this.requirementMode,
            templateSelection: normalizedTemplateSelection,
            debugPrompt: this._shouldRequestPromptTrace(),
            requestId,
            sessionId: this.sessionId,
            existingFlowJson: this._stringifyAgentRunFlowPayload(flowPayload),
            attachments: [],
            attachmentCount: Array.isArray(attachmentPaths) ? attachmentPaths.length : 0,
            ...agentGenerateFlowPayload,
            useVisionAgentGenerateFlow: true,
            runtimePreviewConsent: false
        };
    },

    _stringifyAgentRunFlowPayload(flowPayload) {
        if (flowPayload === null || flowPayload === undefined) {
            return null;
        }

        if (typeof flowPayload === 'string') {
            return flowPayload;
        }

        try {
            return JSON.stringify(flowPayload);
        } catch {
            return null;
        }
    },

    async _dispatchAgentRunGenerateRequest(payload, { clearInput = true, input = null } = {}) {
        const createResult = await httpClient.post('/ai/agent-runs', payload);
        const runId = String(createResult?.runId || createResult?.RunId || '').trim();
        if (!runId) {
            throw new Error('AgentRun create endpoint did not return runId.');
        }

        this.activeAgentRunId = runId;
        this.activeAgentRunEvents = [];
        this.activeAgentRunEventKeys = new Set();
        this.agentRunStepMap = new Map();
        this.agentRunToolMap = new Map();
        this.agentRunArtifactMap = new Map();

        const initialEvents = createResult?.events || createResult?.Events || [];
        initialEvents.forEach(evt => this._handleAgentRunEvent(evt));
        if (!initialEvents.length) {
            const brief = createResult?.brief || createResult?.Brief || '';
            if (brief) {
                this._appendAssistantStreamText('reply', `${brief}\n`);
            }
        }

        this._startAgentRunEventSource(runId);
        this.nextHintDraft = '';
        this.nextTemplateSelection = null;
        this._renderQueuedHintBanner();
        if (clearInput && input) {
            input.value = '';
            input.style.height = 'auto';
        }

        return true;
    },

    _startAgentRunEventSource(runId) {
        this._closeAgentRunEventSource();
        if (!runId || typeof window.EventSource !== 'function') {
            return null;
        }

        const url = httpClient.buildRequestUrl(`/ai/agent-runs/${encodeURIComponent(runId)}/events`);
        const source = new window.EventSource(url);
        this.activeAgentRunEventSource = source;

        const eventTypes = [
            'run.started',
            'assistant.brief',
            'stage.started',
            'stage.completed',
            'tool.call.started',
            'tool.call.completed',
            'tool.call.failed',
            'workflow.draft.updated',
            'readiness.checked',
            'package.readiness.checked',
            'manifest.dryrun.completed',
            'station.compatibility.completed',
            'operator.contract.completed',
            'release.review.completed',
            'artifact.created',
            'run.completed',
            'run.failed',
            'run.cancelled'
        ];

        eventTypes.forEach(type => {
            source.addEventListener(type, event => {
                this._handleAgentRunSseMessage(event);
            });
        });

        source.onmessage = event => this._handleAgentRunSseMessage(event);
        source.onerror = () => {
            if (!this.isGenerating || !this.activeAgentRunId || this.activeAgentRunId !== runId) {
                return;
            }

            const turn = this.activeAssistantTurn;
            if (turn) {
                this._setAssistantTurnStatus(turn, '事件流重连中', 'warning');
                this._appendAgentRunProcessLine({
                    runId,
                    sequence: `sse-error-${Date.now()}`,
                    stage: 'run',
                    status: 'running',
                    title: '事件流暂时中断',
                    summary: '正在等待浏览器自动重连，后端 run 会继续保存可回放事件。'
                });
            }
        };

        return source;
    },

    _handleAgentRunSseMessage(event) {
        if (!event?.data) return;

        try {
            this._handleAgentRunEvent(JSON.parse(event.data));
        } catch {
            this._appendAgentRunProcessLine({
                runId: this.activeAgentRunId || 'unknown',
                sequence: `sse-parse-${Date.now()}`,
                stage: 'run',
                status: 'failed',
                title: '事件解析失败',
                summary: '收到一条无法解析的事件，已忽略该条并继续等待后续事件。'
            });
        }
    },

    _handleAgentRunEvent(rawEvent = {}) {
        const evt = this._normalizeAgentRunEvent(rawEvent);
        if (!evt || (this.activeAgentRunId && evt.runId !== this.activeAgentRunId)) {
            return;
        }

        const key = `${evt.runId}:${evt.sequence}:${evt.eventType}`;
        this.activeAgentRunEventKeys = this.activeAgentRunEventKeys instanceof Set
            ? this.activeAgentRunEventKeys
            : new Set();
        if (this.activeAgentRunEventKeys.has(key)) {
            return;
        }

        this.activeAgentRunEventKeys.add(key);
        this.activeAgentRunEvents = Array.isArray(this.activeAgentRunEvents)
            ? this.activeAgentRunEvents
            : [];
        this.activeAgentRunEvents.push(evt);

        if (evt.eventType === 'assistant.brief') {
            this._renderAgentRunBrief(evt);
            return;
        }

        if (TOOL_EVENT_TYPES.has(evt.eventType)) {
            this._renderAgentRunToolEvent(evt);
        } else if (ARTIFACT_EVENT_TYPES.has(evt.eventType)) {
            this._renderAgentRunArtifactEvent(evt);
            this._appendAgentRunProcessLine(evt);
        } else if (evt.eventType === 'run.failed') {
            this._renderAgentRunFailure(evt);
        } else if (evt.eventType !== 'run.started') {
            this._appendAgentRunProcessLine(evt);
        }

        if (evt.eventType === 'run.started') {
            this._appendAgentRunProcessLine(evt);
        }

        if (TERMINAL_EVENT_TYPES.has(evt.eventType)) {
            this._handleAgentRunTerminalEvent(evt);
        } else {
            this._updateAgentRunWorkbenchState(evt);
        }
    },

    _normalizeAgentRunEvent(rawEvent) {
        if (!rawEvent || typeof rawEvent !== 'object') return null;

        const runId = String(rawEvent.runId ?? rawEvent.RunId ?? '').trim();
        const eventType = String(rawEvent.eventType ?? rawEvent.EventType ?? '').trim();
        if (!runId || !eventType) return null;

        const sequence = Number(rawEvent.sequence ?? rawEvent.Sequence ?? 0);
        return {
            runId,
            sequence: Number.isFinite(sequence) ? sequence : 0,
            timestamp: rawEvent.timestamp ?? rawEvent.Timestamp ?? '',
            eventType,
            stage: String(rawEvent.stage ?? rawEvent.Stage ?? '').trim(),
            title: String(rawEvent.title ?? rawEvent.Title ?? '').trim(),
            summary: String(rawEvent.summary ?? rawEvent.Summary ?? '').trim(),
            status: String(rawEvent.status ?? rawEvent.Status ?? '').trim().toLowerCase(),
            payload: rawEvent.payload ?? rawEvent.Payload ?? null,
            metadataOnly: Boolean(rawEvent.metadataOnly ?? rawEvent.MetadataOnly),
            redactionPass: Boolean(rawEvent.redactionPass ?? rawEvent.RedactionPass)
        };
    },

    _renderAgentRunBrief(evt) {
        const text = evt.summary || this._payloadString(evt.payload, 'brief') || '已创建 Vision Agent 任务，将实时展示公开执行过程。';
        const body = this.activeAssistantTurn?.replyBody;
        if (!body?.textContent?.includes(text)) {
            this._appendAssistantStreamText('reply', `${text}\n`);
        }

        this._setAssistantTurnStatus(this.activeAssistantTurn, '运行中', 'streaming');
    },

    _appendAgentRunProcessLine(evt) {
        const turn = this.activeAssistantTurn;
        if (!turn?.processSection || !turn?.processBody) return;

        const stepId = this._getAgentRunStepId(evt);
        const stageLabel = this._getAgentRunStageLabel(evt.stage);
        const statusLabel = this._getAgentRunStatusLabel(evt.status);
        const title = evt.title || stageLabel;
        const summary = evt.summary || '';
        const text = `${stageLabel} · ${statusLabel} · ${title}${summary && summary !== title ? `\n${summary}` : ''}`;
        const item = this._updateThinkingStep(evt.runId, stepId, text);
        if (!item) return;

        item.className = `ai-agent-run-step is-${this._getAgentRunTone(evt.status, evt.eventType)}`;
        item.dataset.eventType = evt.eventType;
        item.dataset.stage = evt.stage || '';
    },

    _renderAgentRunToolEvent(evt) {
        const turn = this.activeAssistantTurn;
        if (!turn?.toolsSection || !turn?.toolsBody) return;

        const payload = this._asObject(evt.payload);
        const toolName = this._payloadString(payload, 'toolName') ||
            this._payloadString(payload, 'name') ||
            this._deriveToolNameFromTitle(evt.title);
        const toolKey = `${evt.runId}:${toolName || evt.sequence}`;
        this.agentRunToolMap = this.agentRunToolMap instanceof Map
            ? this.agentRunToolMap
            : new Map();

        turn.toolsSection.hidden = false;
        let card = this.agentRunToolMap.get(toolKey);
        if (!card || !turn.toolsBody.contains?.(card)) {
            card = document.createElement('div');
            card.className = 'ai-agent-run-tool-card';
            turn.toolsBody.appendChild(card);
            this.agentRunToolMap.set(toolKey, card);
        }

        const durationMs = this._payloadNumber(payload, 'durationMs');
        const reportId = this._payloadString(payload, 'reportId');
        const blockedReasons = this._payloadArray(payload, 'blockedReasons');
        const firstFix = this._payloadString(payload, 'firstFixRecommendation');
        const resultSummary = this._payloadString(payload, 'summary') ||
            this._payloadString(payload, 'resultSummary') ||
            evt.summary ||
            '工具调用状态已更新。';
        const tone = this._getAgentRunTone(evt.status, evt.eventType);
        card.className = `ai-agent-run-tool-card is-${tone}`;
        card.innerHTML = `
            <div class="ai-agent-run-tool-header">
                <span class="ai-agent-run-tool-name">${this._escapeHtml(toolName || 'Vision Agent tool')}</span>
                <span class="ai-agent-run-badge is-${tone}">${this._escapeHtml(this._getAgentRunStatusLabel(evt.status))}</span>
            </div>
            <div class="ai-agent-run-tool-summary">${this._escapeHtml(resultSummary)}</div>
            <div class="ai-agent-run-meta-row">
                ${durationMs != null ? `<span>${this._escapeHtml(`${durationMs} ms`)}</span>` : ''}
                ${reportId ? `<span>reportId ${this._escapeHtml(reportId)}</span>` : ''}
            </div>
            ${blockedReasons.length > 0 ? `<div class="ai-agent-run-blocked">${blockedReasons.map(reason => `<span>${this._escapeHtml(reason)}</span>`).join('')}</div>` : ''}
            ${firstFix ? `<div class="ai-agent-run-first-fix"><span>第一修复建议</span>${this._escapeHtml(firstFix)}</div>` : ''}
        `;
        this._scrollToBottom();
    },

    _renderAgentRunArtifactEvent(evt) {
        const turn = this.activeAssistantTurn;
        if (!turn?.artifactsSection || !turn?.artifactsBody) return;

        const payload = this._asObject(evt.payload);
        const reportId = this._payloadString(payload, 'reportId') ||
            this._payloadString(payload, 'manifestId') ||
            this._payloadString(payload, 'reviewId');
        const blockedReasons = this._payloadArray(payload, 'blockedReasons');
        const firstFix = this._payloadString(payload, 'firstFixRecommendation');
        const artifactKey = `${evt.runId}:${evt.eventType}:${reportId || evt.stage || evt.sequence}`;
        this.agentRunArtifactMap = this.agentRunArtifactMap instanceof Map
            ? this.agentRunArtifactMap
            : new Map();

        turn.artifactsSection.hidden = false;
        let card = this.agentRunArtifactMap.get(artifactKey);
        if (!card || !turn.artifactsBody.contains?.(card)) {
            card = document.createElement('div');
            card.className = 'ai-agent-run-artifact-card';
            turn.artifactsBody.appendChild(card);
            this.agentRunArtifactMap.set(artifactKey, card);
        }

        const tone = this._getAgentRunTone(evt.status, evt.eventType);
        card.className = `ai-agent-run-artifact-card is-${tone}`;
        card.innerHTML = `
            <div class="ai-agent-run-artifact-title">
                <span>${this._escapeHtml(evt.title || this._getAgentRunStageLabel(evt.stage))}</span>
                <span class="ai-agent-run-badge is-${tone}">${this._escapeHtml(this._getAgentRunStatusLabel(evt.status))}</span>
            </div>
            <div class="ai-agent-run-artifact-summary">${this._escapeHtml(evt.summary || '已生成可回放的 metadata-only 报告事件。')}</div>
            ${reportId ? `<div class="ai-agent-run-report-id">reportId ${this._escapeHtml(reportId)}</div>` : ''}
            ${blockedReasons.length > 0 ? `<div class="ai-agent-run-blocked">${blockedReasons.map(reason => `<span>${this._escapeHtml(reason)}</span>`).join('')}</div>` : ''}
            ${firstFix ? `<div class="ai-agent-run-first-fix"><span>第一修复建议</span>${this._escapeHtml(firstFix)}</div>` : ''}
        `;
        this._scrollToBottom();
    },

    _renderAgentRunFailure(evt) {
        const firstFix = this._payloadString(evt.payload, 'firstFixRecommendation') ||
            this._payloadString(this._asObject(evt.payload)?.diagnostic, 'firstFixRecommendation') ||
            '请检查公开诊断，补齐缺失元数据后重试。';
        this._renderAssistantFailure(this.activeAssistantTurn, {
            errorMessage: evt.summary || 'Vision Agent run failed.',
            failureSummary: {
                message: evt.summary || 'Vision Agent run failed.',
                repairTarget: firstFix
            },
            manualRetry: null
        });
    },

    _handleAgentRunTerminalEvent(evt) {
        this._closeAgentRunEventSource();
        this.isCancellingGenerate = false;
        this._clearActiveRequestState();
        this._setGeneratingState(false);

        if (evt.eventType === 'run.completed') {
            this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
            this._setAssistantTurnStatus(this.activeAssistantTurn, '生成完成', 'success');
            if (evt.summary) {
                this._setResultStatusNote(evt.summary, 'info');
            }
        } else if (evt.eventType === 'run.cancelled') {
            this._setWorkbenchState(AiWorkbenchStates.CANCELLED);
            this._setAssistantTurnStatus(this.activeAssistantTurn, '已取消', 'cancelled');
            this._setResultStatusNote('', '');
        } else {
            this._setWorkbenchState(AiWorkbenchStates.FAILED);
            this._setAssistantTurnStatus(this.activeAssistantTurn, '生成失败', 'failed');
            if (evt.summary) {
                this._setResultStatusNote(evt.summary, 'warning');
            }
        }

        if (this.sessionId) {
            this._addToHistory({
                sessionId: this.sessionId,
                lastMessage: this.lastUserPrompt || evt.summary || 'Vision Agent run',
                updatedAtUtc: new Date().toISOString(),
                turnCount: 0,
                generationMode: 'agent_run_event_stream',
                applied: false
            });
        }

        this.activeAssistantTurn = null;
    },

    _updateAgentRunWorkbenchState(evt) {
        if (!evt?.stage) return;

        if (evt.stage === 'requirement_parsing') {
            this._setWorkbenchState(AiWorkbenchStates.PARSING);
        } else if (evt.stage === 'planner' || evt.stage === 'tool_policy') {
            this._setWorkbenchState(AiWorkbenchStates.GENERATING);
        } else if (evt.stage === 'readiness' || evt.stage === 'manifest_dry_run') {
            this._setWorkbenchState(AiWorkbenchStates.DRY_RUNNING);
        }
    },

    _getAgentRunStepId(evt) {
        if (evt.eventType === 'stage.started' || evt.eventType === 'stage.completed') {
            return evt.stage || evt.sequence;
        }

        return `${evt.eventType}:${evt.stage || evt.sequence}`;
    },

    _getAgentRunStageLabel(stage) {
        const key = String(stage || '').trim();
        return STAGE_LABELS[key] || key || '运行';
    },

    _getAgentRunStatusLabel(status) {
        switch (String(status || '').trim().toLowerCase()) {
            case 'running':
                return '进行中';
            case 'completed':
                return '完成';
            case 'blocked':
                return '阻断';
            case 'failed':
                return '失败';
            case 'cancelled':
            case 'canceled':
                return '已取消';
            default:
                return '已记录';
        }
    },

    _getAgentRunTone(status, eventType = '') {
        const normalizedStatus = String(status || '').trim().toLowerCase();
        if (normalizedStatus === 'failed' || eventType === 'run.failed' || eventType === 'tool.call.failed') {
            return 'failed';
        }
        if (normalizedStatus === 'blocked') {
            return 'warning';
        }
        if (normalizedStatus === 'cancelled' || normalizedStatus === 'canceled' || eventType === 'run.cancelled') {
            return 'cancelled';
        }
        if (normalizedStatus === 'completed' || eventType === 'run.completed') {
            return 'success';
        }
        return 'running';
    },

    _asObject(value) {
        return value && typeof value === 'object' ? value : {};
    },

    _payloadString(payload, name) {
        const obj = this._asObject(payload);
        const value = obj?.[name] ?? obj?.[this._toPascalCase(name)];
        return typeof value === 'string' ? value.trim() : '';
    },

    _payloadNumber(payload, name) {
        const obj = this._asObject(payload);
        const value = obj?.[name] ?? obj?.[this._toPascalCase(name)];
        const number = Number(value);
        return Number.isFinite(number) ? number : null;
    },

    _payloadArray(payload, name) {
        const obj = this._asObject(payload);
        const value = obj?.[name] ?? obj?.[this._toPascalCase(name)];
        if (!Array.isArray(value)) return [];
        return value.map(item => String(item || '').trim()).filter(Boolean).slice(0, 8);
    },

    _toPascalCase(name) {
        const text = String(name || '');
        return text ? `${text[0].toUpperCase()}${text.slice(1)}` : text;
    },

    _deriveToolNameFromTitle(title) {
        const text = String(title || '').trim();
        const marker = text.indexOf(':');
        return marker >= 0 ? text.slice(marker + 1).trim() : text;
    },

    _cancelActiveAgentRun() {
        const runId = String(this.activeAgentRunId || '').trim();
        if (!runId) {
            return Promise.resolve(false);
        }

        return httpClient
            .post(`/ai/agent-runs/${encodeURIComponent(runId)}/cancel`)
            .then(() => true)
            .catch(error => {
                this.isCancellingGenerate = false;
                this._setGeneratingState(this.isGenerating);
                this._addMessage('system', `取消生成未生效: ${error?.message || '未知错误'}`);
                return false;
            });
    }
};
