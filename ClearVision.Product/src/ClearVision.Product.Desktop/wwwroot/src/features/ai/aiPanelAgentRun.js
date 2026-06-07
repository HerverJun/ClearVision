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

const AGENT_RUN_EVENT_TYPES = [
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
                this.panel._setAgentRunTransportStatus('已降级为 WebMessage', 'warning', '事件流不可用，等待后端最终结果回放。');
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
            if (statusText) {
                this.panel._setAgentRunTransportStatus(statusText, 'warning');
            }
            events
                .map(evt => this.panel._normalizeAgentRunEvent(evt))
                .filter(Boolean)
                .filter(evt => evt.sequence > this.lastSequence)
                .sort((a, b) => a.sequence - b.sequence)
                .forEach(evt => this._handleEvent(evt));
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
        return Boolean(
            this.isVisionAgentDeveloperUiEnabled &&
            this.useVisionAgentGenerateFlow &&
            typeof window !== 'undefined' &&
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

        const streamToken = createResult?.streamToken || createResult?.StreamToken || '';
        const lastSequence = this._getAgentRunLastSequence();
        this._startAgentRunEventSource(runId, { streamToken, lastSequence });
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
                this._setAgentRunTransportStatus('已降级为 WebMessage', 'warning', error?.message || '事件流不可用。');
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

    _getAgentRunLastSequence() {
        return (Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [])
            .reduce((max, evt) => Math.max(max, Number(evt?.sequence || 0)), 0);
    },

    _isAgentRunTerminalSeen(runId = this.activeAgentRunId) {
        const expectedRunId = String(runId || '').trim();
        return (Array.isArray(this.activeAgentRunEvents) ? this.activeAgentRunEvents : [])
            .some(evt =>
                (!expectedRunId || evt.runId === expectedRunId) &&
                TERMINAL_EVENT_TYPES.has(evt.eventType));
    },

    _setAgentRunTransportStatus(statusText, tone = 'streaming', detail = '') {
        const turn = this.activeAssistantTurn;
        if (turn) {
            this._setAssistantTurnStatus(turn, statusText, tone);
        }

        const summary = String(detail || '').trim();
        this._appendAgentRunProcessLine({
            runId: this.activeAgentRunId || 'agent-run',
            sequence: `transport-${Date.now()}`,
            stage: 'run',
            status: tone === 'warning' ? 'blocked' : 'running',
            title: statusText,
            summary
        });
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
