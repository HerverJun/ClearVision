import httpClient from '../../core/messaging/httpClient.js';

export const AI_PANEL_CAPABILITY_OWNER_ID = 'ai-panel-capability-v2';

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.getElementById(target) : null;
    }

    return target;
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function sanitizePublicText(value, maxChars = 360) {
    const text = String(value ?? '').trim();
    if (!text) {
        return '';
    }

    return text
        .replace(/\b(?:rawPrompt|systemPrompt|userPrompt|chainOfThought|chain_of_thought|reasoningContent|reasoning_content)\b\s*[:=]\s*["']?[^"'\n,;}]+/gi, '[redacted]')
        .replace(/\bBearer\s+[A-Za-z0-9._~+/=-]{8,}/gi, 'Bearer [redacted]')
        .replace(/\b(?:authorization|x-api-key|api[-_ ]?key|token|secret|baseUrl|base_url|headers?)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '[redacted]')
        .replace(/\bhttps?:\/\/[^\s"'<>|]+/gi, '[redacted:url]')
        .replace(/\bsk-[A-Za-z0-9_-]{8,}/gi, '[redacted]')
        .replace(/\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?::\d+)?\b/g, '[redacted:ip]')
        .replace(/\bDB\d+\.DB[XBWD]\d+(?:\.\d+)?\b/gi, '[redacted:plc]')
        .replace(/(?:[a-z]:\\|\\\\)[^\s"'<>|]+/gi, '[redacted:path]')
        .replace(/(?:\/users\/|\/home\/|\/var\/|\/tmp\/|\/mnt\/|\/data\/|\/models\/|\/artifacts\/)[^\s"'<>|]+/gi, '[redacted:path]')
        .replace(/data:image\/[a-z0-9.+-]+;base64,[a-z0-9+/=\r\n]+/gi, '[redacted:image]')
        .replace(/;base64,[a-z0-9+/=\r\n]+/gi, '[redacted:image]')
        .replace(/\b[\w.-]*\.(?:onnx|pt|pth|engine|caffemodel|weights|bin)\b/gi, '[redacted:model]')
        .replace(/(?<![a-z0-9+/=])(?:[a-z0-9+/]{96,}={0,2})(?![a-z0-9+/=])/gi, '[redacted]')
        .slice(0, maxChars);
}

function resolveRunId(replay) {
    return replay?.runId || replay?.RunId || replay?.summary?.runId || replay?.Summary?.RunId || '';
}

function resolveStatus(replay) {
    return replay?.summary?.status || replay?.Summary?.Status || replay?.status || replay?.Status || 'none';
}

function normalizeEvents(replay) {
    const events = replay?.events || replay?.Events || [];
    return Array.isArray(events) ? events : [];
}

export class AiPanelCapabilityAdapter {
    constructor({ httpClientRef = httpClient } = {}) {
        this.httpClient = httpClientRef;
    }

    async loadLatestRun() {
        try {
            return await this.httpClient.get('/ai/agent-runs/latest');
        } catch (error) {
            const message = String(error?.message || error || '');
            if (message.includes('404')) {
                return null;
            }
            throw error;
        }
    }

    async loadRun(runId) {
        return await this.httpClient.get(`/ai/agent-runs/${encodeURIComponent(runId)}`);
    }

    async loadRunEvents(runId, { afterSequence = null } = {}) {
        return await this.httpClient.get(`/ai/agent-runs/${encodeURIComponent(runId)}/events`, {
            ...(afterSequence !== null ? { afterSequence } : {})
        });
    }

    async cancelRun(runId) {
        return await this.httpClient.post(`/ai/agent-runs/${encodeURIComponent(runId)}/cancel`, {});
    }

    buildEventStreamUrl(runId, { afterSequence = null } = {}) {
        return this.httpClient.buildRequestUrl(`/ai/agent-runs/${encodeURIComponent(runId)}/events`, {
            ...(afterSequence !== null ? { afterSequence } : {})
        });
    }
}

export function createAiPanelCapabilityAdapter(options = {}) {
    return new AiPanelCapabilityAdapter(options);
}

export class AiPanelCapabilityOwner {
    constructor(container, {
        adapter,
        showToast = () => {}
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('AiPanelCapabilityOwner requires a container.');
        }
        if (!adapter) {
            throw new Error('AiPanelCapabilityOwner requires an adapter.');
        }

        this.adapter = adapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.replay = null;
        this.events = [];
        this.errorMessage = '';
        this.loading = false;
        this.activeRunId = '';
        this.eventSource = null;
        this.replayTimer = null;
        this.disposed = false;
        this.requestId = 0;

        this.handleClick = this.handleClick.bind(this);
        this.container.dataset.aiPanelOwner = AI_PANEL_CAPABILITY_OWNER_ID;
        this.container.addEventListener('click', this.handleClick);
        this.render();
    }

    async activate() {
        if (this.disposed) {
            return;
        }

        await this.refreshLatest();
    }

    async refreshLatest() {
        const requestId = ++this.requestId;
        this.loading = true;
        this.errorMessage = '';
        this.render();

        try {
            const replay = await this.adapter.loadLatestRun();
            if (this.disposed || requestId !== this.requestId) {
                return;
            }

            this.applyReplay(replay);
        } catch (error) {
            if (requestId === this.requestId) {
                this.errorMessage = sanitizePublicText(error?.message || 'AI 运行状态加载失败');
            }
        } finally {
            if (requestId === this.requestId) {
                this.loading = false;
                this.render();
            }
        }
    }

    applyReplay(replay) {
        this.replay = replay || null;
        this.events = normalizeEvents(replay);
        const nextRunId = resolveRunId(replay);
        if (nextRunId && nextRunId !== this.activeRunId) {
            this.activeRunId = nextRunId;
            this.openEventStream(nextRunId);
        } else if (!nextRunId) {
            this.closeEventStream();
            this.activeRunId = '';
        }
    }

    openEventStream(runId) {
        this.closeEventStream();
        if (!runId || typeof window === 'undefined') {
            return;
        }

        const lastSequence = this.events.reduce((max, event) => {
            const sequence = Number(event?.sequence ?? event?.Sequence ?? 0);
            return Number.isFinite(sequence) ? Math.max(max, sequence) : max;
        }, 0);

        if (typeof window.EventSource === 'function') {
            const source = new window.EventSource(this.adapter.buildEventStreamUrl(runId, {
                afterSequence: lastSequence > 0 ? lastSequence : null
            }));
            source.onmessage = event => this.appendEventFromSource(event);
            source.onerror = () => {
                this.closeEventStream();
                this.scheduleReplayPoll(runId);
            };
            this.eventSource = source;
            return;
        }

        this.scheduleReplayPoll(runId);
    }

    appendEventFromSource(event) {
        if (this.disposed) {
            return;
        }

        try {
            const payload = JSON.parse(event.data || '{}');
            const records = Array.isArray(payload) ? payload : [payload];
            this.appendEvents(records);
        } catch {
            // Ignore malformed streaming chunks; replay polling can repair gaps.
        }
    }

    appendEvents(events) {
        const seen = new Set(this.events.map(event => String(event?.sequence ?? event?.Sequence ?? '')));
        events.forEach(event => {
            const sequenceKey = String(event?.sequence ?? event?.Sequence ?? '');
            if (!sequenceKey || seen.has(sequenceKey)) {
                return;
            }
            seen.add(sequenceKey);
            this.events.push(event);
        });
        this.render();
    }

    scheduleReplayPoll(runId) {
        if (this.replayTimer || this.disposed || !runId) {
            return;
        }

        this.replayTimer = window.setTimeout(async () => {
            this.replayTimer = null;
            if (this.disposed || runId !== this.activeRunId) {
                return;
            }

            try {
                const replay = await this.adapter.loadRun(runId);
                if (!this.disposed && runId === this.activeRunId) {
                    this.applyReplay(replay);
                    this.render();
                }
            } catch {
                this.scheduleReplayPoll(runId);
            }
        }, 2000);
    }

    async handleClick(event) {
        const action = event.target?.closest?.('[data-ai-action]')?.dataset?.aiAction;
        if (!action || this.disposed) {
            return;
        }

        event.preventDefault();
        if (action === 'refresh') {
            await this.refreshLatest();
        } else if (action === 'cancel' && this.activeRunId) {
            try {
                const replay = await this.adapter.cancelRun(this.activeRunId);
                this.applyReplay(replay || this.replay);
                this.showToast('AI 运行已请求取消', 'success');
                this.render();
            } catch (error) {
                this.errorMessage = sanitizePublicText(error?.message || 'AI 运行取消失败');
                this.showToast(this.errorMessage, 'error');
                this.render();
            }
        }
    }

    closeEventStream() {
        if (this.eventSource) {
            try {
                this.eventSource.close?.();
            } catch {
                // Event stream cleanup is best-effort.
            }
            this.eventSource = null;
        }

        if (this.replayTimer) {
            window.clearTimeout?.(this.replayTimer);
            this.replayTimer = null;
        }
    }

    render() {
        if (this.disposed || !this.container) {
            return;
        }

        const runId = sanitizePublicText(this.activeRunId || resolveRunId(this.replay) || '', 180);
        const status = sanitizePublicText(resolveStatus(this.replay), 120);
        this.container.innerHTML = `
            <section class="ai-panel ai-panel-capability-owner" data-owner="${AI_PANEL_CAPABILITY_OWNER_ID}">
                <header class="ai-panel-header">
                    <div>
                        <h2>AI</h2>
                        <p>后端 AgentRun 状态投影</p>
                    </div>
                    <div class="ai-panel-actions">
                        <button type="button" class="btn btn-secondary" data-ai-action="refresh" ${this.loading ? 'disabled' : ''}>刷新</button>
                        <button type="button" class="btn btn-danger" data-ai-action="cancel" ${runId ? '' : 'disabled'}>取消运行</button>
                    </div>
                </header>
                ${this.errorMessage ? `<div class="ai-error" role="alert">${escapeHtml(sanitizePublicText(this.errorMessage))}</div>` : ''}
                <div class="ai-agent-run-summary">
                    <dl>
                        <div><dt>Run</dt><dd>${runId ? escapeHtml(runId) : '暂无运行'}</dd></div>
                        <div><dt>状态</dt><dd>${escapeHtml(status)}</dd></div>
                    </dl>
                </div>
                ${this.events.length === 0
                    ? '<p class="empty-text">暂无 AgentRun 事件</p>'
                    : `<ol class="ai-agent-event-list">${this.events.map(event => this.renderEvent(event)).join('')}</ol>`}
            </section>
        `;
    }

    renderEvent(event) {
        const sequence = event?.sequence ?? event?.Sequence ?? '';
        const type = sanitizePublicText(event?.eventType ?? event?.EventType ?? event?.type ?? event?.Type ?? 'event', 120);
        const title = sanitizePublicText(event?.title ?? event?.Title ?? type, 180);
        const summary = sanitizePublicText(event?.summary ?? event?.Summary ?? event?.message ?? event?.Message ?? '', 360);
        return `
            <li class="ai-agent-event">
                <span>${escapeHtml(sequence)}</span>
                <strong>${escapeHtml(title)}</strong>
                <small>${escapeHtml(type)}</small>
                ${summary ? `<p>${escapeHtml(summary)}</p>` : ''}
            </li>
        `;
    }

    destroy() {
        this.dispose();
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        this.requestId += 1;
        this.closeEventStream();
        this.container.removeEventListener('click', this.handleClick);
        delete this.container.dataset.aiPanelOwner;
        this.container.innerHTML = '';
    }
}

export default AiPanelCapabilityOwner;
