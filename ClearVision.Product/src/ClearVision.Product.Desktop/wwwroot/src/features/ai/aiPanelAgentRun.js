import httpClient from '../../core/messaging/httpClient.js';
import { AiWorkbenchStates } from './aiPanelWorkbench.js';

const TERMINAL_EVENT_TYPES = new Set(['run.completed', 'run.failed', 'run.cancelled']);
const TOOL_LOOP_SEGMENT_TERMINAL_EVENT_TYPES = new Set([
    'tool_loop.finalized',
    'tool_loop.fallback',
    'tool_loop.failed',
    'tool_loop.draft.accepted',
    'tool_loop.draft.rejected'
]);
const PLAN_EVENT_TYPES = new Set([
    'plan.created',
    'plan.started',
    'semantic.started',
    'semantic.completed',
    'semantic.failed',
    'semantic.fallback.used',
    'plan.context.started',
    'plan.context.completed',
    'plan.model.started',
    'plan.model.completed',
    'plan.model.timeout',
    'plan.model.failed',
    'plan.contract.started',
    'plan.contract.completed',
    'plan.safety.completed',
    'plan.fallback.used',
    'plan.completed',
    'plan.failed',
    'plan.cancelled'
]);
const TOOL_EVENT_TYPES = new Set([
    'tool.call.started',
    'tool.call.completed',
    'tool.call.failed',
    'tool_call.requested',
    'tool_call.completed',
    'tool_call.denied'
]);
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
    brief: '摘要',
    semantic_extraction: '语义抽取',
    semantic_fallback_used: '语义降级',
    understand_requirement: '理解需求',
    context_collection: '收集上下文',
    plan_generation: '生成计划',
    assumption_confirmation: '确认假设',
    requirement_parsing: '需求归一',
    planner: '规划器',
    tool_policy: '工具策略',
    tool_loop: 'Tool Loop 实验',
    workflow_draft: '流程草稿',
    readiness: '就绪检查',
    manifest_dry_run: '运行包预演',
    package_readiness: '运行包就绪',
    station_compatibility: '工站兼容',
    operator_contract: '算子契约',
    release_review: '发布复核',
    artifact: '结果产物'
};

const AGENT_RUN_EVENT_TYPES = [
    'run.started',
    'assistant.brief',
    'stage.started',
    'stage.completed',
    'tool.call.started',
    'tool.call.completed',
    'tool.call.failed',
    'tool_loop.started',
    'tool_loop.round.started',
    'tool_call.requested',
    'tool_call.completed',
    'tool_call.denied',
    'tool_result.appended',
    'tool_loop.finalized',
    'tool_loop.draft.accepted',
    'tool_loop.draft.rejected',
    'tool_loop.fallback',
    'tool_loop.failed',
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
    'run.cancelled',
    ...PLAN_EVENT_TYPES
];

const AGENT_RUN_FIRST_EVENT_TIMEOUT_MS = 10000;
const AGENT_RUN_REPLAY_INTERVAL_MS = 1500;

export class AgentRunEventTransport {
    constructor(panel, runId, options = {}) {
        this.panel = panel;
        this.runId = String(runId || '').trim();
        this.streamToken = String(options.streamToken || '').trim();
        this.lastSequence = Number(options.lastSequence || 0);
        this.closed = false;
        this.reader = null;
        this.abortController = null;
        this.eventSource = null;
        this.replayTimer = null;
        this.firstEventTimer = null;
        this.receivedStreamEvent = false;
        this.replayFailureCount = 0;
    }

    async start() {
        if (!this.runId || this.closed) return;

        this.panel._setAgentRunTransportStatus('正在连接事件流', 'streaming');
        if (await this._startFetchStream()) return;
        if (await this._replayRecentEvents('已切换备用事件流')) return;
        if (await this._startEventSource()) return;
        await this._startReplayMode();
    }

    close() {
        this.closed = true;
        this._clearFirstEventTimer();

        if (this.replayTimer) {
            window.clearTimeout(this.replayTimer);
            this.replayTimer = null;
        }

        try {
            this.reader?.cancel?.();
        } catch {
            // Reader cancellation is best-effort during UI cleanup.
        }
        this.reader = null;

        try {
            this.abortController?.abort?.();
        } catch {
            // AbortController cleanup is best-effort.
        }
        this.abortController = null;

        try {
            this.eventSource?.close?.();
        } catch {
            // EventSource close should never block UI cleanup.
        }
        this.eventSource = null;
    }

    async _startFetchStream() {
        if (!this._canUseFetchStream()) {
            return false;
        }

        this.abortController = new AbortController();
        this.receivedStreamEvent = false;

        try {
            const url = httpClient.buildRequestUrl(
                `/ai/agent-runs/${encodeURIComponent(this.runId)}/events`,
                { lastEventId: String(this.lastSequence || 0) });
            const response = await fetch(url, {
                method: 'GET',
                headers: httpClient.defaultHeaders,
                signal: this.abortController.signal
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            if (!response.body?.getReader) {
                throw new Error('ReadableStream is unavailable.');
            }

            this.panel._setAgentRunTransportStatus('事件流已连接', 'streaming');
            this._armFirstEventTimeout('fetchStream');
            await this._readFetchStream(response);
            return this.closed || this.panel._isAgentRunTerminalSeen(this.runId);
        } catch (error) {
            if (!this.closed) {
                this.panel._setAgentRunTransportStatus('已切换备用事件流', 'warning', this._formatTransportError(error));
            }
            return false;
        } finally {
            this._clearFirstEventTimer();
            this.abortController = null;
            this.reader = null;
        }
    }

    async _readFetchStream(response) {
        this.reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (!this.closed) {
            const { value, done } = await this.reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });
            buffer = this._consumeSseBuffer(buffer);
        }

        buffer += decoder.decode();
        this._consumeSseBuffer(buffer, true);
    }

    async _startEventSource() {
        if (this.closed || typeof window.EventSource !== 'function') {
            return false;
        }

        const token = await this._ensureStreamToken();
        if (!token) {
            return false;
        }

        return await new Promise(resolve => {
            if (this.closed) {
                resolve(false);
                return;
            }

            this.receivedStreamEvent = false;
            const url = httpClient.buildRequestUrl(
                `/ai/agent-runs/${encodeURIComponent(this.runId)}/events`,
                {
                    streamToken: token,
                    lastEventId: String(this.lastSequence || 0)
                });
            const source = new window.EventSource(url);
            this.eventSource = source;
            let settled = false;
            const finish = value => {
                if (settled) return;
                settled = true;
                this._clearFirstEventTimer();
                try {
                    source.close();
                } catch {
                    // EventSource close is best-effort.
                }
                if (this.eventSource === source) {
                    this.eventSource = null;
                }
                resolve(value);
            };

            this.panel._setAgentRunTransportStatus('已切换备用事件流', 'warning');
            this._armFirstEventTimeout('eventSource', () => finish(false));

            AGENT_RUN_EVENT_TYPES.forEach(type => {
                source.addEventListener(type, event => {
                    this._handleSseMessage(event?.data);
                    if (this.panel._isAgentRunTerminalSeen(this.runId)) {
                        finish(true);
                    }
                });
            });

            source.onmessage = event => {
                this._handleSseMessage(event?.data);
                if (this.panel._isAgentRunTerminalSeen(this.runId)) {
                    finish(true);
                }
            };
            source.onerror = () => {
                if (this.closed || this.panel._isAgentRunTerminalSeen(this.runId)) {
                    finish(true);
                    return;
                }

                this.panel._setAgentRunTransportStatus('已切换备用事件流', 'warning', 'EventSource 连接失败，正在补齐回放事件。');
                finish(false);
            };
        });
    }

    async _startReplayMode() {
        if (this.closed) return false;

        this.panel._setAgentRunTransportStatus('已进入回放模式', 'warning');
        while (!this.closed && !this.panel._isAgentRunTerminalSeen(this.runId)) {
            const terminalSeen = await this._replayRecentEvents();
            if (terminalSeen || this.closed) return true;
            if (this.replayFailureCount >= 3) {
                this.panel._setAgentRunTransportStatus('事件回放等待中', 'warning', '事件流不可用，正在等待后端回放完成。');
            }
            await this._delay(AGENT_RUN_REPLAY_INTERVAL_MS);
        }

        return true;
    }

    async _replayRecentEvents(statusText = '') {
        if (this.closed) return true;

        try {
            const replay = await httpClient.get(`/ai/agent-runs/${encodeURIComponent(this.runId)}`);
            const events = replay?.events || replay?.Events || [];
            const summary = replay?.summary || replay?.Summary || null;
            if (statusText) {
                this.panel._setAgentRunTransportStatus(statusText, 'warning');
            }
            events
                .map(evt => this.panel._normalizeAgentRunEvent(evt))
                .filter(Boolean)
                .filter(evt => evt.sequence > this.lastSequence)
                .sort((a, b) => a.sequence - b.sequence)
                .forEach(evt => this._handleEvent(evt));
            if (!this.panel._isAgentRunTerminalSeen(this.runId)) {
                const terminal = this.panel._buildAgentRunTerminalEventFromSummary?.(this.runId, summary);
                if (terminal) {
                    this._handleEvent(terminal);
                }
            }
            this.replayFailureCount = 0;
            return this.panel._isAgentRunTerminalSeen(this.runId);
        } catch {
            this.replayFailureCount += 1;
            return false;
        }
    }

    async _ensureStreamToken() {
        if (this.streamToken) {
            const token = this.streamToken;
            this.streamToken = '';
            return token;
        }

        try {
            const result = await httpClient.post(`/ai/agent-runs/${encodeURIComponent(this.runId)}/stream-token`);
            const token = String(result?.streamToken || result?.StreamToken || '').trim();
            return token || null;
        } catch {
            return null;
        }
    }

    _consumeSseBuffer(buffer, flush = false) {
        const normalized = String(buffer || '').replaceAll('\r\n', '\n');
        const frames = normalized.split('\n\n');
        const remainder = flush ? '' : frames.pop() || '';
        frames.forEach(frame => this._handleSseFrame(frame));
        return remainder;
    }

    _handleSseFrame(frame) {
        const dataLines = String(frame || '')
            .split('\n')
            .filter(line => line.startsWith('data:'))
            .map(line => line.slice(5).trimStart());
        if (!dataLines.length) return;
        this._handleSseMessage(dataLines.join('\n'));
    }

    _handleSseMessage(data) {
        if (!data || this.closed) return;

        try {
            this._handleEvent(JSON.parse(data));
        } catch {
            this.panel._appendAgentRunProcessLine({
                runId: this.runId,
                sequence: `stream-parse-${Date.now()}`,
                stage: 'run',
                status: 'failed',
                title: '事件解析失败',
                summary: '收到一条无法解析的事件，已忽略该条并继续等待后续事件。'
            });
        }
    }

    _handleEvent(rawEvent) {
        const evt = this.panel._normalizeAgentRunEvent(rawEvent);
        if (!evt || evt.runId !== this.runId) return;

        this.receivedStreamEvent = true;
        this._clearFirstEventTimer();
        this.lastSequence = Math.max(this.lastSequence || 0, evt.sequence || 0);
        this.panel._handleAgentRunEvent(evt);
    }

    _armFirstEventTimeout(transportName, onTimeout = null) {
        this._clearFirstEventTimer();
        this.firstEventTimer = window.setTimeout(() => {
            if (this.closed || this.receivedStreamEvent || this.panel._isAgentRunTerminalSeen(this.runId)) {
                return;
            }

            this.panel._setAgentRunTransportStatus('已切换备用事件流', 'warning', `${transportName} 首个事件超时。`);
            if (onTimeout) {
                onTimeout();
                return;
            }

            try {
                this.abortController?.abort?.();
            } catch {
                // Abort is best-effort.
            }
        }, AGENT_RUN_FIRST_EVENT_TIMEOUT_MS);
    }

    _clearFirstEventTimer() {
        if (!this.firstEventTimer) return;
        window.clearTimeout(this.firstEventTimer);
        this.firstEventTimer = null;
    }

    _canUseFetchStream() {
        return typeof fetch === 'function' &&
            typeof TextDecoder === 'function' &&
            typeof ReadableStream === 'function';
    }

    _formatTransportError(error) {
        const message = String(error?.message || error?.name || '').trim();
        return message ? `事件流连接失败: ${message}` : '事件流连接失败，正在切换备用通道。';
    }

    _delay(ms) {
        return new Promise(resolve => {
            this.replayTimer = window.setTimeout(() => {
                this.replayTimer = null;
                resolve();
            }, ms);
        });
    }
}

export const aiPanelAgentRunMixin = {
    _shouldUseAgentRunEventStream() {
        const hasWindow = typeof window !== 'undefined';

        return Boolean(
            this.useVisionAgentGenerateFlow &&
            hasWindow &&
            typeof fetch === 'function'
        );
    },

    _resetAgentRunState({ close = true } = {}) {
        if (close) {
            this._closeAgentRunEventSource();
        }

        this.activeAgentRunId = null;
        this.activeAgentRunEventSource = null;
        this.activeAgentRunTransport = null;
        this.activeAgentRunEvents = [];
        this.activeAgentRunEventKeys = new Set();
        this.agentRunStepMap = new Map();
        this.agentRunToolMap = new Map();
        this.agentRunArtifactMap = new Map();
        this._resetPublicLiveEventState?.();
    },

    _closeAgentRunEventSource() {
        const source = this.activeAgentRunTransport || this.activeAgentRunEventSource;
        if (!source) return;

        try {
            source.close?.();
        } catch {
            // Stream cleanup should never block UI state reset.
        }

        this.activeAgentRunTransport = null;
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
        agentGenerateFlowPayload,
        buildFromPlan = null
    }) {
        const safeAdditionalContext = this._sanitizeGenerateRequestHint?.(normalizedHint) ||
            this._sanitizeAssistantFailureText?.(normalizedHint, 1600) ||
            String(normalizedHint || '').trim().slice(0, 1600);
        return {
            description: normalizedDescription,
            additionalContext: safeAdditionalContext || null,
            mode: resolvedMode,
            requirementMode: this.requirementMode,
            templateSelection: normalizedTemplateSelection,
            debugPrompt: this._shouldRequestPromptTrace(),
            requestId,
            sessionId: this.sessionId,
            existingFlowJson: this._stringifyAgentRunFlowPayload(flowPayload),
            buildFromPlan,
            attachments: [],
            attachmentCount: Array.isArray(attachmentPaths) ? attachmentPaths.length : 0,
            ...agentGenerateFlowPayload,
            useVisionAgentGenerateFlow: true,
            runtimePreviewConsent: agentGenerateFlowPayload?.runtimePreviewConsent === true
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
            throw new Error('AgentRun 创建接口没有返回 runId。');
        }

        const canonicalSessionId = String(createResult?.sessionId || createResult?.SessionId || '').trim();
        if (canonicalSessionId) {
            this._adoptCanonicalSessionId?.(canonicalSessionId, { reason: 'agent_run_create' });
        }
        this._applyWorkspaceSnapshotSummary?.(createResult?.workspaceSnapshot || createResult?.WorkspaceSnapshot || null);
        this._handleWorkspacePersistenceStatus?.(createResult?.persistenceStatus || createResult?.PersistenceStatus || null);
        if (payload?.buildFromPlan || payload?.BuildFromPlan) {
            this.agentWorkspaceMode = 'build';
            this._setWorkspaceViewMode?.('build', { render: false });
            this._renderAgentWorkspaceOverview?.();
            this._renderPlanWorkspace?.(this.pendingVisionPlan);
            this._renderBuildWorkspaceFromAgentRun?.();
            this._setResultStatusNote?.('构建模式已启动，进度来自后端 AgentRun 公开事件。', 'info');
        }

        this.activeAgentRunId = runId;
        this.activeAgentRunEvents = [];
        this.activeAgentRunEventKeys = new Set();
        this.agentRunStepMap = new Map();
        this.agentRunToolMap = new Map();
        this.agentRunArtifactMap = new Map();
        this._resetPublicLiveEventState?.();

        const initialEvents = createResult?.events || createResult?.Events || [];
        initialEvents.forEach(evt => this._handleAgentRunEvent(evt));
        if (!initialEvents.length) {
            const brief = createResult?.brief || createResult?.Brief || '';
            if (brief) {
                this._appendAssistantStreamText('reply', `${brief}\n`);
            }
        }

        const lastSequence = this._getAgentRunLastSequence();
        this._startAgentRunEventSource(runId, { lastSequence });
        this.nextHintDraft = '';
        this.nextTemplateSelection = null;
        this._renderQueuedHintBanner();
        if (clearInput && input) {
            input.value = '';
            input.style.height = 'auto';
        }

        return true;
    },

    _startAgentRunEventSource(runId, options = {}) {
        this._closeAgentRunEventSource();
        if (!runId) {
            return null;
        }

        const transport = new AgentRunEventTransport(this, runId, options);
        this.activeAgentRunTransport = transport;
        this.activeAgentRunEventSource = transport;
        transport.start().catch(error => {
            if (this.activeAgentRunTransport === transport && !this._isAgentRunTerminalSeen(runId)) {
                this._setAgentRunTransportStatus('事件回放等待中', 'warning', error?.message || '事件流不可用，正在等待后端回放完成。');
            }
        });
        return transport;
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
        if (!evt) {
            this._recordPublicLiveEventDrop?.('dropped');
            return;
        }

        if (this._isActivePlanRunEvent?.(evt)) {
            this._handlePlanRunEvent?.(evt);
            return;
        }

        if (this.activeAgentRunId && evt.runId !== this.activeAgentRunId) {
            this._recordPublicLiveEventDrop?.('stale');
            return;
        }

        const key = `${evt.runId}:${evt.sequence}:${evt.eventType}`;
        this.activeAgentRunEventKeys = this.activeAgentRunEventKeys instanceof Set
            ? this.activeAgentRunEventKeys
            : new Set();
        if (this.activeAgentRunEventKeys.has(key)) {
            this._recordPublicLiveEventDrop?.('duplicate');
            return;
        }

        this.activeAgentRunEventKeys.add(key);
        this.activeAgentRunEvents = Array.isArray(this.activeAgentRunEvents)
            ? this.activeAgentRunEvents
            : [];
        this.activeAgentRunEvents.push(evt);
        this._handleAgentRunWorkspaceEvent?.(evt);
        this._routePublicLiveEvent?.(this._normalizePublicLiveEvent?.(evt, { source: 'agent-run' }));

        if (evt.eventType === 'assistant.brief') {
            this._renderAgentRunBrief(evt);
            return;
        }

        if (TOOL_EVENT_TYPES.has(evt.eventType)) {
            this._renderAgentRunToolEvent(evt);
            this._appendAgentRunProcessLine(evt);
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
        } else if (TOOL_LOOP_SEGMENT_TERMINAL_EVENT_TYPES.has(evt.eventType)) {
            this._handleAgentRunSegmentTerminalEvent(evt);
        } else {
            this._updateAgentRunWorkbenchState(evt);
        }
    },

    _getAgentRunLastSequence() {
        return (Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [])
            .reduce((max, evt) => Math.max(max, Number(evt?.sequence || 0)), 0);
    },

    _isAgentRunTerminalSeen(runId = this.activeAgentRunId) {
        const expectedRunId = String(runId || '').trim();
        const buildEvents = Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [];
        const planEvents = Array.isArray(this.activePlanRunEvents) ? this.activePlanRunEvents : [];
        return [...buildEvents, ...planEvents]
            .some(evt =>
                (!expectedRunId || evt.runId === expectedRunId) &&
                TERMINAL_EVENT_TYPES.has(evt.eventType));
    },

    _setAgentRunTransportStatus(statusText, tone = 'streaming', detail = '') {
        const safeStatusText = this._sanitizeAgentRunPublicText(statusText, 120) || 'AgentRun 事件状态';
        const turn = this.activeAssistantTurn;
        if (turn) {
            this._setAssistantTurnStatus(turn, safeStatusText, tone);
        }

        const summary = this._sanitizeAgentRunPublicText(detail, 260);
        this._appendAgentRunProcessLine({
            runId: this.activeAgentRunId || this.activePlanRunId || 'agent-run',
            sequence: `transport-${Date.now()}`,
            stage: 'run',
            status: tone === 'warning' ? 'blocked' : 'running',
            title: safeStatusText,
            summary
        });
    },

    _normalizeAgentRunEvent(rawEvent) {
        if (!rawEvent || typeof rawEvent !== 'object') return null;

        const runId = String(rawEvent.runId ?? rawEvent.RunId ?? '').trim();
        const eventType = String(rawEvent.eventType ?? rawEvent.EventType ?? '').trim();
        if (!runId || !eventType) return null;

        const sequence = Number(rawEvent.sequence ?? rawEvent.Sequence ?? 0);
        const title = this._sanitizeAgentRunPublicText(rawEvent.title ?? rawEvent.Title ?? '', 120);
        const summary = this._sanitizeAgentRunPublicText(rawEvent.summary ?? rawEvent.Summary ?? '', 260);
        return {
            runId,
            sequence: Number.isFinite(sequence) ? sequence : 0,
            timestamp: rawEvent.timestamp ?? rawEvent.Timestamp ?? '',
            eventType,
            stage: String(rawEvent.stage ?? rawEvent.Stage ?? '').trim(),
            title,
            summary,
            status: String(rawEvent.status ?? rawEvent.Status ?? '').trim().toLowerCase(),
            payload: rawEvent.payload ?? rawEvent.Payload ?? null,
            metadataOnly: Boolean(rawEvent.metadataOnly ?? rawEvent.MetadataOnly),
            redactionPass: Boolean(rawEvent.redactionPass ?? rawEvent.RedactionPass)
        };
    },

    _sanitizeAgentRunPublicText(value, maxChars = 220) {
        const text = String(value ?? '').trim();
        if (!text) return '';

        if (this._sanitizePublicLiveEventText) {
            return this._sanitizePublicLiveEventText(text, maxChars);
        }

        const redacted = this._redactPublicDiagnosticText?.(text) || text;
        return redacted
            .replace(/\bsk-[A-Za-z0-9_-]{8,}/gi, '[redacted]')
            .slice(0, maxChars);
    },

    _sanitizeAgentRunDiagnosticCode(value, maxChars = 96) {
        if (this._sanitizePublicLiveDiagnosticCode) {
            return this._sanitizePublicLiveDiagnosticCode(value).slice(0, maxChars);
        }

        return this._sanitizeAgentRunPublicText(value, maxChars)
            .replace(/\b(?:authorization|x-api-key|api[-_ ]?key|token|secret|baseUrl|base_url|headers?)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '')
            .replace(/[^A-Za-z0-9_.:-]/g, '')
            .slice(0, maxChars);
    },

    _buildAgentRunTerminalEventFromSummary(runId, summary = null) {
        const data = summary && typeof summary === 'object' ? summary : null;
        if (!data) return null;

        const status = String(data.status ?? data.Status ?? '').trim().toLowerCase();
        const eventType = status === 'completed'
            ? 'run.completed'
            : status === 'failed'
                ? 'run.failed'
                : (status === 'cancelled' || status === 'canceled')
                    ? 'run.cancelled'
                    : '';
        if (!eventType) return null;

        const lastSequence = Number(data.lastSequence ?? data.LastSequence ?? this._getAgentRunLastSequence?.() ?? 0);
        return {
            runId,
            sequence: Number.isFinite(lastSequence) ? lastSequence + 1 : Date.now(),
            eventType,
            stage: 'run',
            title: data.title ?? data.Title ?? '',
            summary: data.summary ?? data.Summary ?? '',
            status,
            payload: data.payload ?? data.Payload ?? null,
            metadataOnly: Boolean(data.metadataOnly ?? data.MetadataOnly ?? true),
            redactionPass: Boolean(data.redactionPass ?? data.RedactionPass ?? true)
        };
    },

    _renderAgentRunBrief(evt) {
        const localizedText = this._localizeAgentRunBriefText(
            evt.summary || this._payloadString(evt.payload, 'brief') || ''
        );
        const text = this._sanitizeAgentRunPublicText(localizedText, 360) ||
            this._localizeAgentRunBriefText('');
        const body = this.activeAssistantTurn?.replyBody;
        if (!body?.textContent?.includes(text)) {
            this._appendAssistantStreamText('reply', `${text}\n`);
        }

        this._setAssistantTurnStatus(this.activeAssistantTurn, '执行中', 'streaming');
    },

    _localizeAgentRunBriefText(value) {
        const text = String(value || '').trim();
        if (!text) {
            return '视觉智能体运行已创建，公开进度事件会在此处显示。';
        }

        const localized = this._localizeDisplayText?.(text);
        if (localized && localized !== text) {
            return localized;
        }

        const defaultPrefix = 'I will turn this request into a safe Vision Agent workflow draft and stream each public progress step:';
        if (text.startsWith(defaultPrefix)) {
            const requirement = text.slice(defaultPrefix.length).trim();
            return requirement
                ? `已创建安全的视觉智能体流程草稿任务，将在此流式显示公开进度：${requirement}`
                : '已创建安全的视觉智能体流程草稿任务，将在此流式显示公开进度。';
        }

        if (text === 'I will create a metadata-only Vision Agent run and report progress as public events.') {
            return '已创建仅元数据的视觉智能体运行，公开进度事件会在此处显示。';
        }

        return text;
    },

    _appendAgentRunProcessLine(evt) {
        const turn = this.activeAssistantTurn;
        if (!turn?.processSection || !turn?.processBody) return;

        const stepId = this._getAgentRunStepId(evt);
        const stageLabel = this._getAgentRunStageLabel(evt.stage);
        const statusLabel = this._getAgentRunStatusLabel(evt.status);
        const title = this._sanitizeAgentRunPublicText(
            this._localizeDisplayText?.(evt.title || stageLabel) || evt.title || stageLabel,
            120
        );
        const summary = this._sanitizeAgentRunPublicText(
            this._localizeDisplayText?.(evt.summary || '') || evt.summary || '',
            260
        );
        const text = this._formatAgentRunProcessText?.(evt, { stageLabel, statusLabel, title, summary }) ||
            `${stageLabel} / ${statusLabel} / ${title}${summary && summary !== title ? `\n${summary}` : ''}`;
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
        const rawToolName = this._payloadString(payload, 'toolName') ||
            this._payloadString(payload, 'name') ||
            this._deriveToolNameFromTitle(evt.title);
        const toolName = this._sanitizeAgentRunDiagnosticCode(rawToolName, 96) ||
            this._sanitizeAgentRunPublicText(rawToolName, 80);
        const toolLabel = this._sanitizeAgentRunPublicText(
            this._formatToolName?.(rawToolName) ||
            this._localizeDisplayText?.(rawToolName) ||
            rawToolName ||
            '视觉智能体工具',
            120
        );
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
        const reportId = this._sanitizeAgentRunDiagnosticCode(this._payloadString(payload, 'reportId'), 96);
        const blockedReasons = this._payloadArray(payload, 'blockedReasons')
            .map(reason => this._sanitizeAgentRunPublicText(reason, 100))
            .filter(Boolean);
        const firstFix = this._sanitizeAgentRunPublicText(this._payloadString(payload, 'firstFixRecommendation'), 180);
        const resultSummary = this._sanitizeAgentRunPublicText(
            this._payloadString(payload, 'summary') ||
            this._payloadString(payload, 'resultSummary') ||
            evt.summary ||
            '工具状态已更新。',
            220
        );
        const tone = this._getAgentRunTone(evt.status, evt.eventType);
        card.className = `ai-agent-run-tool-card is-${tone}`;
        card.innerHTML = `
            <div class="ai-agent-run-tool-header">
                <span class="ai-agent-run-tool-name" title="${this._escapeHtml(toolName || '')}">${this._escapeHtml(toolLabel)}</span>
                <span class="ai-agent-run-badge is-${tone}">${this._escapeHtml(this._getAgentRunStatusLabel(evt.status))}</span>
            </div>
            <div class="ai-agent-run-tool-summary">${this._escapeHtml(this._localizeDisplayText?.(resultSummary) || resultSummary)}</div>
            <div class="ai-agent-run-meta-row">
                ${durationMs != null ? `<span>${this._escapeHtml(`${durationMs} ms`)}</span>` : ''}
                ${reportId ? `<span>报告 ${this._escapeHtml(reportId)}</span>` : ''}
            </div>
            ${blockedReasons.length > 0 ? `<div class="ai-agent-run-blocked">${blockedReasons.map(reason => `<span>${this._escapeHtml(this._localizeDisplayText?.(reason) || reason)}</span>`).join('')}</div>` : ''}
            ${firstFix ? `<div class="ai-agent-run-first-fix"><span>首要修复</span>${this._escapeHtml(this._localizeDisplayText?.(firstFix) || firstFix)}</div>` : ''}
        `;
        this._scrollToBottom();
    },

    _formatAgentRunProcessText(evt, fallback = {}) {
        const payload = this._asObject(evt.payload);
        const rawToolName = this._payloadString(payload, 'toolName') ||
            this._deriveToolNameFromTitle(evt.title);
        const toolLabel = rawToolName
            ? this._sanitizeAgentRunPublicText(
                this._formatToolName?.(rawToolName) || this._localizeDisplayText?.(rawToolName) || rawToolName,
                120)
            : '';
        const round = this._payloadNumber(payload, 'round');
        const reason = this._payloadString(payload, evt.eventType === 'tool_loop.draft.rejected' ? 'rejectionReason' : 'fallbackReason');
        const reasonLabel = reason
            ? this._sanitizeAgentRunPublicText(this._localizeDisplayText?.(reason) || reason, 120)
            : '';
        const summary = fallback.summary || '';

        switch (evt.eventType) {
            case 'tool_loop.started':
                return `Tool Loop 实验已启动${summary ? `\n${summary}` : ''}`;
            case 'tool_loop.round.started':
                return `第 ${round || '?'} 轮工具决策${summary ? `\n${summary}` : ''}`;
            case 'tool_call.requested':
                return `请求工具：${toolLabel || '未命名工具'}${summary ? `\n${summary}` : ''}`;
            case 'tool_call.completed':
                return `工具完成：${toolLabel || '未命名工具'}${summary ? `\n${summary}` : ''}`;
            case 'tool_call.denied':
                return `工具被拒绝：${toolLabel || '未命名工具'}${summary ? `\n${summary}` : ''}`;
            case 'tool_result.appended':
                return `工具结果已回填${summary ? `\n${summary}` : ''}`;
            case 'tool_loop.finalized':
                return `LLM 已给出 final${summary ? `\n${summary}` : ''}`;
            case 'tool_loop.draft.accepted':
                return `实验草稿已通过校验${summary ? `\n${summary}` : ''}`;
            case 'tool_loop.draft.rejected':
                return `实验草稿未通过校验${reasonLabel ? `：${reasonLabel}` : ''}${summary ? `\n${summary}` : ''}`;
            case 'tool_loop.fallback':
                return `已回退稳定构建链路${reasonLabel ? `：${reasonLabel}` : ''}${summary ? `\n${summary}` : ''}`;
            case 'tool_loop.failed':
                return `实验失败${summary ? `\n${summary}` : ''}`;
            default:
                return '';
        }
    },

    _renderAgentRunArtifactEvent(evt) {
        const turn = this.activeAssistantTurn;
        if (!turn?.artifactsSection || !turn?.artifactsBody) return;

        const payload = this._asObject(evt.payload);
        const reportId = this._sanitizeAgentRunDiagnosticCode(this._payloadString(payload, 'reportId') ||
            this._payloadString(payload, 'manifestId') ||
            this._payloadString(payload, 'reviewId'), 96);
        const blockedReasons = this._payloadArray(payload, 'blockedReasons')
            .map(reason => this._sanitizeAgentRunPublicText(reason, 100))
            .filter(Boolean);
        const firstFix = this._sanitizeAgentRunPublicText(this._payloadString(payload, 'firstFixRecommendation'), 180);
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
        const artifactTitle = this._sanitizeAgentRunPublicText(
            this._localizeDisplayText?.(evt.title || this._getAgentRunStageLabel(evt.stage)) ||
            evt.title ||
            this._getAgentRunStageLabel(evt.stage),
            120
        );
        const artifactSummary = this._sanitizeAgentRunPublicText(
            this._localizeDisplayText?.(evt.summary || '') ||
            evt.summary ||
            '已发布可回放的元数据报告事件。',
            220
        );
        card.className = `ai-agent-run-artifact-card is-${tone}`;
        card.innerHTML = `
            <div class="ai-agent-run-artifact-title">
                <span>${this._escapeHtml(artifactTitle)}</span>
                <span class="ai-agent-run-badge is-${tone}">${this._escapeHtml(this._getAgentRunStatusLabel(evt.status))}</span>
            </div>
            <div class="ai-agent-run-artifact-summary">${this._escapeHtml(artifactSummary)}</div>
            ${reportId ? `<div class="ai-agent-run-report-id">报告 ${this._escapeHtml(reportId)}</div>` : ''}
            ${blockedReasons.length > 0 ? `<div class="ai-agent-run-blocked">${blockedReasons.map(reason => `<span>${this._escapeHtml(this._localizeDisplayText?.(reason) || reason)}</span>`).join('')}</div>` : ''}
            ${firstFix ? `<div class="ai-agent-run-first-fix"><span>首要修复</span>${this._escapeHtml(this._localizeDisplayText?.(firstFix) || firstFix)}</div>` : ''}
        `;
        this._scrollToBottom();
    },

    _renderAgentRunFailure(evt) {
        const firstFix = this._sanitizeAgentRunPublicText(this._payloadString(evt.payload, 'firstFixRecommendation') ||
            this._payloadString(this._asObject(evt.payload)?.diagnostic, 'firstFixRecommendation') ||
            '请复核公开诊断，补齐缺失元数据后重试。', 180);
        const summary = this._sanitizeAgentRunPublicText(evt.summary || '视觉智能体运行失败。', 260);
        this._renderAssistantFailure(this.activeAssistantTurn, {
            errorMessage: summary,
            failureSummary: {
                message: summary,
                repairTarget: firstFix
            },
            manualRetry: null
        });
    },

    _handleAgentRunTerminalEvent(evt) {
        this._finalizeAgentRunUiState('run_terminal', evt, {
            closeTransports: true,
            clearActiveRunId: false
        });

        if (evt.eventType === 'run.completed') {
            const applied = this._applyAgentRunResultPayload?.(evt) === true;
            if (applied) {
                this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
                this._setAssistantTurnStatus(this.activeAssistantTurn, '构建完成', 'success');
                if (evt.summary) {
                    this._setResultStatusNote(
                        this._sanitizeAgentRunPublicText(this._localizeDisplayText?.(evt.summary) || evt.summary, 260),
                        'info'
                    );
                }
                this._showDraftBuildCompletionNotice?.(
                    this.currentResult,
                    this._asObject?.(evt.payload) || evt.payload || {}
                );
            } else {
                this._setWorkbenchState(AiWorkbenchStates.FAILED);
                const compatibilityState = this._getBuildArtifactFlowCompatibilityState?.(this.currentResult, this.activeAgentRunEvents);
                if (compatibilityState?.status === 'legacy_build_artifact_missing_canonical_flow') {
                    this._setAssistantTurnStatus(this.activeAssistantTurn, '需要重新构建', 'warning');
                    this._setResultStatusNote(compatibilityState.publicMessage, 'warning');
                } else {
                    this._setAssistantTurnStatus(this.activeAssistantTurn, '构建完成但草稿缺失', 'warning');
                    this._setResultStatusNote('构建已结束，但没有收到可回放流程草稿；请重新构建或查看事件回放。', 'warning');
                }
            }
        } else if (evt.eventType === 'run.cancelled') {
            this._setWorkbenchState(AiWorkbenchStates.CANCELLED);
            this._setAssistantTurnStatus(this.activeAssistantTurn, '已取消', 'cancelled');
            this._setResultStatusNote('', '');
        } else {
            const appliedCanonical = this._applyBuildFromPlanCanonicalState?.(evt.payload) === true;
            const payload = this._asObject?.(evt.payload) || evt.payload || {};
            const failureType = String(payload.failureType || payload.FailureType || '').trim().toLowerCase();
            const isClarificationTerminal = appliedCanonical &&
                (this._isClarificationResult?.(payload) === true || failureType === 'clarification_required');
            if (isClarificationTerminal) {
                this.pendingClarificationPayload = null;
                this.agentWorkspaceMode = 'plan';
                this._setWorkspaceViewMode?.('plan', { render: false });
                this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
                this._renderAgentWorkspaceOverview?.();
                this._renderPlanWorkspace?.(this.pendingVisionPlan);
                this._updatePlanBuildActionState?.();
            } else {
                this._setWorkbenchState(AiWorkbenchStates.FAILED);
            }
            if (isClarificationTerminal) {
                this._setAssistantTurnStatus(this.activeAssistantTurn, '待澄清', 'warning');
            } else {
                this._setAssistantTurnStatus(this.activeAssistantTurn, '构建失败', 'failed');
            }
            if (evt.summary) {
                this._setResultStatusNote(this._sanitizeAgentRunPublicText(evt.summary, 260), 'warning');
            }
        }

        if (this.sessionId) {
            this._addToHistory({
                sessionId: this.sessionId,
                lastMessage: this.lastUserPrompt || this._sanitizeAgentRunPublicText(evt.summary, 180) || '视觉智能体运行',
                updatedAtUtc: new Date().toISOString(),
                turnCount: 0,
                generationMode: 'agent_run_event_stream',
                applied: false
            });
        }

        this.activeAssistantTurn = null;
        if (evt.runId === this.activeAgentRunId) {
            this.activeAgentRunId = null;
        }
    },

    _handleAgentRunSegmentTerminalEvent(evt) {
        this._finalizeAgentRunUiState('tool_loop_segment_terminal', evt, {
            closeTransports: false,
            clearActiveRunId: false
        });

        if (evt.eventType === 'tool_loop.fallback') {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '已回退稳定链路', 'warning');
            this._setResultStatusNote('Tool Loop 实验已回退稳定构建链路，输入框已释放；稳定构建结果返回后会刷新可应用草稿。', 'warning');
        } else if (evt.eventType === 'tool_loop.draft.rejected') {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '草稿验收未通过', 'warning');
            this._setResultStatusNote('Tool Loop 草稿未通过验收，已回退稳定构建链路。', 'warning');
        } else if (evt.eventType === 'tool_loop.failed') {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '实验失败', 'failed');
            this._setResultStatusNote(
                this._sanitizeAgentRunPublicText(evt.summary || 'Tool Loop 实验失败，已等待稳定链路或终止事件收尾。', 260),
                'warning'
            );
        } else if (evt.eventType === 'tool_loop.draft.accepted') {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '草稿验收通过', 'success');
            this._setResultStatusNote('Tool Loop 草稿已通过验收，稳定构建链路正在补全 BuildResult。', 'info');
        } else if (evt.eventType === 'tool_loop.finalized') {
            this._setAssistantTurnStatus(this.activeAssistantTurn, 'LLM final 已返回', 'success');
        }
    },

    _finalizeAgentRunUiState(reason = '', payload = null, options = {}) {
        const closeTransports = options.closeTransports !== false;
        if (closeTransports) {
            this._closeAllAgentTransports();
        }

        this.isCancellingGenerate = false;
        this._clearActiveRequestState();
        this._setGeneratingState(false);

        if (options.clearActiveRunId && payload?.runId === this.activeAgentRunId) {
            this.activeAgentRunId = null;
        }

        this._renderBuildWorkspaceFromAgentRun?.();
        this._updateApplyButtonState?.();
        return reason;
    },

    _closeAllAgentTransports() {
        this._closeAgentRunEventSource();
    },

    _updateAgentRunWorkbenchState(evt) {
        if (!evt?.stage) return;

        if (evt.stage === 'understand_requirement' ||
            evt.stage === 'context_collection' ||
            evt.stage === 'plan_generation' ||
            evt.stage === 'assumption_confirmation' ||
            evt.stage === 'requirement_parsing') {
            this._setWorkbenchState(AiWorkbenchStates.PARSING);
        } else if (evt.stage === 'planner' || evt.stage === 'tool_policy' || evt.stage === 'tool_loop') {
            this._setWorkbenchState(AiWorkbenchStates.GENERATING);
        } else if (evt.stage === 'readiness' || evt.stage === 'manifest_dry_run') {
            this._setWorkbenchState(AiWorkbenchStates.DRY_RUNNING);
        }
    },

    _getAgentRunStepId(evt) {
        if (evt.eventType === 'stage.started' || evt.eventType === 'stage.completed') {
            return evt.stage || evt.sequence;
        }

        const payload = this._asObject(evt.payload);
        if (String(evt.eventType || '').startsWith('tool_call.')) {
            const toolName = this._payloadString(payload, 'toolName') ||
                this._payloadString(payload, 'name') ||
                this._deriveToolNameFromTitle(evt.title);
            const round = this._payloadNumber(payload, 'round');
            return `${evt.eventType}:${round || evt.stage || 'round'}:${toolName || evt.sequence}`;
        }

        if (evt.eventType === 'tool_result.appended' || evt.eventType === 'tool_loop.round.started') {
            const round = this._payloadNumber(payload, 'round');
            return `${evt.eventType}:${round || evt.sequence}`;
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
                return '执行中';
            case 'completed':
                return '已完成';
            case 'blocked':
                return '已阻断';
            case 'warning':
                return '警告';
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
        if (normalizedStatus === 'failed' || eventType === 'run.failed' || eventType === 'tool.call.failed' || eventType === 'tool_loop.failed') {
            return 'failed';
        }
        if (normalizedStatus === 'blocked' || normalizedStatus === 'warning' || eventType === 'tool_call.denied' || eventType === 'tool_loop.fallback' || eventType === 'tool_loop.draft.rejected') {
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

    async _replayLatestAgentRunPublicEvents({ statusText = '回放最近一次 AgentRun' } = {}) {
        const replay = await httpClient.get('/ai/agent-runs/latest');
        const snapshotEvents = Array.isArray(replay?.snapshot?.events)
            ? replay.snapshot.events
            : [];
        const replayEvents = Array.isArray(replay?.events)
            ? replay.events
            : [];
        const rawEvents = snapshotEvents.length > 0 ? snapshotEvents : replayEvents;
        const events = rawEvents
            .map(evt => this._normalizeAgentRunEvent(evt))
            .filter(Boolean)
            .sort((a, b) => a.sequence - b.sequence);
        const runId = String(
            replay?.summary?.runId ||
            replay?.Summary?.RunId ||
            events[0]?.runId ||
            ''
        ).trim();

        if (!runId || events.length === 0) {
            this._setResultStatusNote?.('没有可回放的 AgentRun 事件。', 'warning');
            return false;
        }

        const hasPlanEvents = events.some(evt => String(evt.eventType || '').startsWith('plan.'));
        const replayRequestId = `agent-run-replay-${runId}`;
        const turn = this._startAssistantTurn?.({
            statusText,
            statusTone: 'streaming',
            openReply: false
        }) || this.activeAssistantTurn;
        if (turn) {
            this.activeAssistantTurn = turn;
        }

        this._resetPublicLiveEventState?.();
        this.agentRunStepMap = new Map();
        this.agentRunToolMap = new Map();
        this.agentRunArtifactMap = new Map();
        this.activeAgentRunEvents = [];
        this.activeAgentRunEventKeys = new Set();
        this.activePlanRunEvents = [];
        this.activePlanRunEventKeys = new Set();

        if (hasPlanEvents) {
            this.activeAgentRunId = null;
            this.activePlanRunId = runId;
            this.activePlanRequestId = replayRequestId;
            this.activePlanRunRequestId = replayRequestId;
        } else {
            this.activeAgentRunId = runId;
            this.activePlanRunId = null;
            this.activePlanRequestId = null;
            this.activePlanRunRequestId = null;
        }

        events.forEach(evt => this._handleAgentRunEvent(evt));

        const status = String(replay?.summary?.status || replay?.Summary?.Status || '').toLowerCase();
        const tone = status === 'failed'
            ? 'failed'
            : (status === 'cancelled' || status === 'canceled' ? 'cancelled' : 'success');
        this._setAssistantTurnStatus?.(turn || this.activeAssistantTurn, '回放完成', tone);
        this._setResultStatusNote?.(
            `已回放最近一次 AgentRun：${runId}`,
            'info'
        );
        return true;
    },

    async _replayAgentRunPublicEventsById(runId, { kind = '', statusText = '回放 AgentRun' } = {}) {
        const normalizedRunId = String(runId || '').trim();
        if (!normalizedRunId) return false;

        const replay = await httpClient.get(`/ai/agent-runs/${encodeURIComponent(normalizedRunId)}`);
        const events = (Array.isArray(replay?.events) ? replay.events : replay?.Events) || [];
        const normalizedEvents = events
            .map(evt => this._normalizeAgentRunEvent(evt))
            .filter(Boolean)
            .sort((a, b) => a.sequence - b.sequence);
        if (!normalizedEvents.length) return false;

        const hasPlanEvents = kind === 'plan' ||
            normalizedEvents.some(evt => String(evt.eventType || '').startsWith('plan.'));
        const replayRequestId = `agent-run-replay-${normalizedRunId}`;
        if (hasPlanEvents) {
            this.activePlanRunId = normalizedRunId;
            this.activePlanRequestId = replayRequestId;
            this.activePlanRunRequestId = replayRequestId;
            this.activePlanRunEvents = [];
            this.activePlanRunEventKeys = new Set();
        } else {
            this.activeAgentRunId = normalizedRunId;
            this.activeAgentRunEvents = [];
            this.activeAgentRunEventKeys = new Set();
        }

        this._setResultStatusNote?.(`${statusText}：${normalizedRunId}`, 'info');
        normalizedEvents.forEach(evt => this._handleAgentRunEvent(evt));
        this._renderAgentWorkspaceOverview?.();
        this._renderPlanWorkspace?.(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun?.();
        return true;
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
                this._addMessage('system', `取消生成未生效: ${this._sanitizeAgentRunPublicText(error?.message || '未知错误', 260) || '未知错误'}`);
                return false;
            });
    }
};
