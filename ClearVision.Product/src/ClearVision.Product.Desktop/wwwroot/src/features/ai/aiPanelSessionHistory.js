import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import { AgentWorkspaceModes } from './aiPanelAgentWorkspace.js';
import { normalizeWorkspaceSnapshotForRestore } from './aiPanelSnapshotRecovery.js';
import { AiWorkbenchStates } from './aiPanelWorkbench.js';

const SESSION_LOAD_TIMEOUT_MS = 15000;

function downgradeRestoredAppliedPlan(plan) {
    if (!plan || typeof plan !== 'object') return plan;
    const maturity = plan.requirementMaturity && typeof plan.requirementMaturity === 'object'
        ? { ...plan.requirementMaturity, canBuild: false, CanBuild: false }
        : plan.requirementMaturity;
    const rawPlanSnapshot = plan.rawPlanSnapshot && typeof plan.rawPlanSnapshot === 'object'
        ? {
            ...plan.rawPlanSnapshot,
            canBuild: false,
            CanBuild: false,
            buildReadiness: null,
            BuildReadiness: null,
            effectiveReadiness: null,
            EffectiveReadiness: null
        }
        : plan.rawPlanSnapshot;
    return {
        ...plan,
        canBuild: false,
        CanBuild: false,
        executable: false,
        buildReadiness: null,
        effectiveReadiness: null,
        requirementMaturity: maturity,
        rawPlanSnapshot
    };
}

function stripRestoredApplyAuthority(result) {
    if (!result || typeof result !== 'object') return result;
    const buildResult = result.buildResult && typeof result.buildResult === 'object'
        ? {
            ...result.buildResult,
            applyGate: null,
            ApplyGate: null,
            buildReadiness: null,
            BuildReadiness: null,
            readiness: null,
            Readiness: null
        }
        : null;
    result.applyGate = null;
    result.ApplyGate = null;
    result.buildReadiness = null;
    result.BuildReadiness = null;
    result.readiness = null;
    result.Readiness = null;
    result.buildResult = buildResult;
    result.BuildResult = buildResult;
    return result;
}
export const aiPanelSessionHistoryMixin = {
    _toggleHistoryPanel() {
        const panel = this.container.querySelector('#ai-history-panel');
        const historyBtn = this.container.querySelector('#ai-btn-history');
        if (!panel || !historyBtn) return;

        this.isHistoryPanelOpen = !this.isHistoryPanelOpen;
        panel.classList.toggle('expanded', this.isHistoryPanelOpen);
        historyBtn.setAttribute('aria-expanded', this.isHistoryPanelOpen ? 'true' : 'false');

        if (this.isHistoryPanelOpen) {
            this._loadHistory();
            const searchInput = this.container.querySelector('#ai-history-search');
            if (searchInput) searchInput.focus();
        }
    },

    _addToHistory(entry) {
        const normalized = this._normalizeSessionSummary(entry);
        if (!normalized) return;

        this.history = [normalized, ...this.history.filter(item => item.sessionId !== normalized.sessionId)]
            .sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime());
        this._filterHistory(this.historyKeyword);
    },

    _sanitizeSessionHistoryText(value, maxChars = 220) {
        const text = String(value ?? '').trim();
        if (!text) return '';
        return this._sanitizeAssistantFailureText?.(text, maxChars) ||
            this._redactPublicDiagnosticText?.(text)?.slice(0, maxChars) ||
            text.slice(0, maxChars);
    },

    _normalizeSessionSummary(entry) {
        const sessionId = String(entry?.sessionId ?? entry?.SessionId ?? '').trim();
        if (!sessionId) return null;

        const lastMessage = this._sanitizeSessionHistoryText(entry?.lastMessage ?? entry?.LastMessage ?? '', 220);
        const templateName = this._sanitizeSessionHistoryText(entry?.templateName ?? entry?.TemplateName ?? '', 120);
        const generationMode = this._sanitizeSessionHistoryText(entry?.generationMode ?? entry?.GenerationMode ?? '', 80);
        const updatedAtUtc = String(entry?.updatedAtUtc ?? entry?.UpdatedAtUtc ?? new Date().toISOString());
        const turnCountRaw = Number(entry?.turnCount ?? entry?.TurnCount ?? 0);
        return {
            sessionId,
            lastMessage: lastMessage || '（空会话）',
            templateName,
            generationMode,
            applied: Boolean(entry?.applied ?? entry?.Applied),
            updatedAtUtc,
            turnCount: Number.isFinite(turnCountRaw) ? turnCountRaw : 0
        };
    },

    _filterHistory(keyword = '') {
        this.historyKeyword = String(keyword || '').trim().toLowerCase();
        if (!this.historyKeyword) {
            this.filteredHistory = [...this.history];
        } else {
            this.filteredHistory = this.history.filter(item => {
                const text = `${this._sanitizeSessionHistoryText(item.lastMessage, 220)} ${item.sessionId}`.toLowerCase();
                return text.includes(this.historyKeyword);
            });
        }

        this._renderHistoryList();
    },

    _renderHistoryList() {
        const list = this.container.querySelector('#ai-history-list');
        if (!list) return;
        const rows = this.filteredHistory.length > 0 || this.historyKeyword
            ? this.filteredHistory
            : this.history;
        if (rows.length === 0) {
            list.innerHTML = '<div class="ai-history-empty">暂无历史记录</div>';
            return;
        }

        list.innerHTML = rows.map(item => {
            const lastMessage = this._sanitizeSessionHistoryText(item.lastMessage, 220) || '（空会话）';
            const templateName = this._sanitizeSessionHistoryText(item.templateName, 120);
            const generationMode = this._sanitizeSessionHistoryText(item.generationMode, 80);
            const templateBadge = templateName
                ? `<span class="history-template-badge">${this._escapeHtml(templateName)}</span>`
                : '';
            const modeChip = generationMode
                ? `<span class="history-mode-chip">${this._escapeHtml(generationMode)}</span>`
                : '';
            const appliedIcon = item.applied ? '<span class="history-applied-icon" title="已应用">&#10003;</span>' : '';
            return `
            <div class="ai-history-item ${item.sessionId === this.sessionId ? 'active' : ''}" role="listitem" data-session-id="${this._escapeHtml(item.sessionId)}">
                <button class="ai-history-select" type="button" data-session-id="${this._escapeHtml(item.sessionId)}" ${item.sessionId === this.sessionId ? 'aria-current="true"' : ''}>
                    <span class="history-desc">${this._escapeHtml(lastMessage)}</span>
                    <span class="history-badges">${templateBadge}${modeChip}${appliedIcon}</span>
                    <span class="history-meta">
                        <span>${this._escapeHtml(this._formatHistoryTime(item.updatedAtUtc))}</span>
                        <span>${this._escapeHtml(String(item.turnCount))} 轮</span>
                    </span>
                </button>
                <button class="ai-history-delete" type="button" data-session-id="${this._escapeHtml(item.sessionId)}" aria-label="删除会话：${this._escapeHtml(lastMessage)}" title="删除会话">删除</button>
            </div>
        `}).join('');

        list.querySelectorAll('.ai-history-select').forEach(button => {
            button.addEventListener('click', () => {
                this._switchToSession(button.dataset.sessionId || '');
            });
        });

        list.querySelectorAll('.ai-history-delete').forEach(btn => {
            btn.addEventListener('click', (event) => {
                event.stopPropagation();
                this._deleteSession(btn.dataset.sessionId || '');
            });
        });
    },

    _formatHistoryTime(value) {
        const timestamp = new Date(value);
        if (Number.isNaN(timestamp.getTime())) return '--';
        return timestamp.toLocaleString();
    },

    _loadHistory() {
        webMessageBridge.sendMessage('ListAiSessions');
    },

    _handleListAiSessionsResult(data) {
        const payload = data?.payload || data || {};
        if (!payload.success) {
            if (payload.errorMessage) {
                this._addMessage('system', `历史加载失败: ${this._sanitizeSessionHistoryText(payload.errorMessage, 260) || '未知错误'}`);
            }
            return;
        }

        const sessions = Array.isArray(payload.sessions) ? payload.sessions : [];
        this.history = sessions
            .map(item => this._normalizeSessionSummary(item))
            .filter(Boolean)
            .sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime());
        this._filterHistory(this.historyKeyword);
        this._maybeAutoRestoreActiveSession(this.history);
    },

    _maybeAutoRestoreActiveSession(sessions = []) {
        if (this.autoRestoreAttempted) return;
        this.autoRestoreAttempted = true;

        const candidateSessionId = String(this.initialAutoRestoreSessionId || '').trim();
        if (!candidateSessionId) return;

        const exists = sessions.some(item =>
            String(item?.sessionId || '').trim().toLowerCase() === candidateSessionId.toLowerCase());
        if (!exists) {
            if (String(this.sessionId || '').trim().toLowerCase() === candidateSessionId.toLowerCase()) {
                this._adoptCanonicalSessionId?.(null, { reason: 'auto_restore_missing' });
            }
            this._saveSessionId?.(null);
            return;
        }

        this._requestSessionLoad(candidateSessionId, 'auto_restore');
    },

    async _switchToSession(sessionId) {
        const normalizedSessionId = String(sessionId || '').trim();
        if (!normalizedSessionId) return;
        if (this.isGenerating) {
            this._addMessage('system', '正在生成中，暂时无法切换历史会话。');
            return;
        }

        const selectionGeneration = Number(this.sessionSelectionGeneration || 0) + 1;
        this.sessionSelectionGeneration = selectionGeneration;
        this.sessionNavigationEpoch = Number(this.sessionNavigationEpoch || 0) + 1;
        this._cancelPendingSessionLoad?.();

        let flushed = false;
        try {
            flushed = (await this._flushWorkspaceSnapshotBeforeBoundary?.('history_switch')) ?? true;
        } catch {
            flushed = false;
        }
        if (selectionGeneration !== Number(this.sessionSelectionGeneration || 0) || this._disposed) {
            return;
        }
        if (!flushed) {
            this._setResultStatusNote?.('Plan 修改尚未成功保存，已阻止切换历史。', 'warning');
            return;
        }

        this._requestSessionLoad(normalizedSessionId, 'history_switch');
    },

    _cancelPendingSessionLoad(request = this.pendingSessionLoad) {
        if (!request) return false;
        if (request.timeoutId) window.clearTimeout?.(request.timeoutId);
        if (this.pendingSessionLoad === request) this.pendingSessionLoad = null;
        return true;
    },

    _finishPendingSessionLoad(request) {
        if (!request || this.pendingSessionLoad !== request) return false;
        return this._cancelPendingSessionLoad(request);
    },

    _requestSessionLoad(sessionId, source = 'manual') {
        const normalizedSessionId = String(sessionId || '').trim();
        if (!normalizedSessionId) return;
        this._cancelPendingSessionLoad?.();
        const request = {
            sessionId: normalizedSessionId,
            source,
            epoch: this.sessionNavigationEpoch || 0,
            requestId: this._createSessionLoadRequestId?.() || `${Date.now()}-${Math.random().toString(16).slice(2)}`
        };
        request.timeoutId = window.setTimeout?.(() => {
            if (this.pendingSessionLoad !== request || this._disposed) return;
            this.pendingSessionLoad = null;
            this._setResultStatusNote?.('会话恢复超时，当前安全状态已保留，可重新选择该会话重试。', 'warning');
            this._announceAccessibilityStatus?.('会话恢复超时，可重试。', 'assertive');
        }, SESSION_LOAD_TIMEOUT_MS) || null;
        this.pendingSessionLoad = request;
        try {
            this._sendGetAiSession(normalizedSessionId, request);
        } catch (error) {
            this._finishPendingSessionLoad(request);
            this._setResultStatusNote?.('会话恢复请求发送失败，当前安全状态已保留，可重试。', 'warning');
            console.warn('[AiPanel] 会话恢复请求发送失败。', error);
        }
    },

    _sendGetAiSession(sessionId, request = {}) {
        webMessageBridge.sendMessage('GetAiSession', {
            payload: {
                sessionId,
                requestId: request.requestId || '',
                navigationEpoch: Number(request.epoch || 0)
            }
        });
    },

    _handleGetAiSessionResult(data) {
        const payload = data?.payload || data || {};
        const pendingLoad = this.pendingSessionLoad;
        const responseSessionId = String(payload.sessionId ?? payload.SessionId ?? '').trim();
        const responseRequestId = String(payload.requestId ?? payload.RequestId ?? '').trim();
        const responseEpoch = Number(payload.navigationEpoch ?? payload.NavigationEpoch ?? -1);
        const isMatchingLoad = Boolean(pendingLoad) &&
            responseEpoch === Number(pendingLoad.epoch || 0) &&
            responseRequestId === String(pendingLoad.requestId || '') &&
            responseSessionId.toLowerCase() === String(pendingLoad.sessionId || '').trim().toLowerCase();
        if (!isMatchingLoad) {
            return;
        }
        this._finishPendingSessionLoad(pendingLoad);
        if (!payload.success) {
            if (pendingLoad?.source === 'auto_restore') {
                this._saveSessionId?.(null);
                if (!this.autoRestoreNoticeShown) {
                    this.autoRestoreNoticeShown = true;
                    this._addMessage('system', `自动恢复上次会话失败：${this._sanitizeSessionHistoryText(payload.errorMessage, 260) || '未知错误'}。已进入新会话。`);
                }
                return;
            }

            this._addMessage('system', `会话恢复失败: ${this._sanitizeSessionHistoryText(payload.errorMessage, 260) || '未知错误'}`);
            return;
        }

        const session = payload.session;
        if (!session) {
            this._addMessage('system', '会话恢复失败: 会话数据为空');
            return;
        }

        const sessionId = String(session.sessionId ?? session.SessionId ?? '').trim();
        if (!sessionId) {
            this._addMessage('system', '会话恢复失败: 会话 ID 无效');
            return;
        }

        if (responseSessionId.toLowerCase() !== sessionId.toLowerCase()) {
            this._addMessage('system', '会话恢复失败: 返回的会话 ID 与请求不一致，当前安全状态已保留。');
            return;
        }

        this._adoptCanonicalSessionId?.(sessionId, { reason: 'session_restore' });
        const workspaceSnapshot = this._normalizeSessionWorkspaceSnapshot(session.workspaceSnapshot ?? session.WorkspaceSnapshot);
        this.nextHintDraft = '';
        this.nextTemplateSelection = null;
        this._resetPendingDraftState();
        this._resetCurrentResultSyncState({ clearPersistedApplySafetyBlock: false });
        this.pendingParameterFilePickContext = null;
        this.pendingManualRetry = null;
        this.activeAssistantTurn = null;
        this._clearActiveRequestState();
        this._clearResultPane();
        this._renderManualRetryBanner();
        this._renderQueuedHintBanner();

        const chatContainer = this.container.querySelector('#ai-chat-container');
        if (chatContainer) chatContainer.innerHTML = '';

        const rawHistory = Array.isArray(session.history)
            ? session.history
            : (Array.isArray(session.History) ? session.History : []);
        const normalizedHistory = rawHistory
            .map(turn => ({
                role: String(turn?.role ?? turn?.Role ?? '').trim().toLowerCase(),
                message: String(turn?.message ?? turn?.Message ?? ''),
                payload: turn?.payload ?? turn?.Payload ?? null
            }))
            .filter(turn => turn.message.trim().length > 0 || turn.payload);

        if (normalizedHistory.length === 0) {
            this._addMessage('ai', '已恢复历史会话（当前没有可展示的消息）。');
        } else {
            normalizedHistory.forEach(turn => {
                if (turn.role === 'assistant' || turn.role === 'ai') {
                    const rendered = this._renderAssistantTurnFromPayload(turn);
                    if (!rendered) {
                        this._addMessage('ai', turn.message);
                    }
                    return;
                }

                const role = turn.role === 'user' ? 'user' : 'ai';
                this._addMessage(role, turn.message);
            });
        }

        const canvasFlowRaw = session.currentCanvasFlowJson ?? session.CurrentCanvasFlowJson;
        const aiFlowRaw = session.currentFlowJson ?? session.CurrentFlowJson;
        const parsedCanvasFlow = this._parseFlowJson(canvasFlowRaw);
        const parsedAiFlow = this._parseFlowJson(aiFlowRaw);
        const parsedFlow = parsedCanvasFlow || parsedAiFlow;
        const canvasFlow = this._normalizeSessionFlowForCanvas(parsedCanvasFlow, sessionId);

        if (!canvasFlow && parsedAiFlow && !parsedCanvasFlow) {
            console.warn('[AiPanel] 历史会话仅包含 AI 原始结构，未包含画布快照，无法直接应用到画布。', {
                sessionId
            });
            this._addMessage('system', '该历史会话缺少可回放的画布快照，已恢复对话内容，但无法直接还原到当前画布。');
        }

        const latestAssistantPayload = [...normalizedHistory]
            .reverse()
            .find(turn => (turn.role === 'assistant' || turn.role === 'ai') && turn.payload)?.payload ?? null;
        const followupSource = parsedAiFlow || parsedFlow;
        const restoredBuildResult = latestAssistantPayload?.buildResult ?? latestAssistantPayload?.BuildResult ?? null;
        const restoredWorkflowDiff = latestAssistantPayload?.workflowDiff ?? latestAssistantPayload?.WorkflowDiff ??
            restoredBuildResult?.workflowDiff ?? restoredBuildResult?.WorkflowDiff ?? null;
        const restoredApplyGate = latestAssistantPayload?.applyGate ?? latestAssistantPayload?.ApplyGate ??
            restoredBuildResult?.applyGate ?? restoredBuildResult?.ApplyGate ?? null;
        const restoredToolEvidence = latestAssistantPayload?.toolEvidenceTimeline ?? latestAssistantPayload?.ToolEvidenceTimeline ??
            restoredBuildResult?.toolEvidenceTimeline ?? restoredBuildResult?.ToolEvidenceTimeline ?? [];
        const restoredFirstFix = latestAssistantPayload?.firstFixRecommendation ?? latestAssistantPayload?.FirstFixRecommendation ??
            restoredBuildResult?.firstFixRecommendation ?? restoredBuildResult?.FirstFixRecommendation ?? '';
        const restoredResult = {
            flow: canvasFlow || parsedFlow || null,
            aiExplanation: parsedAiFlow?.explanation || parsedAiFlow?.Explanation ||
                parsedFlow?.explanation || parsedFlow?.Explanation ||
                latestAssistantPayload?.aiExplanation || latestAssistantPayload?.AiExplanation ||
                latestAssistantPayload?.reply || latestAssistantPayload?.Reply || '--',
            reasoning: '',
            recommendedTemplate: followupSource?.recommendedTemplate ?? followupSource?.RecommendedTemplate ?? null,
            templateCandidates: followupSource?.templateCandidates ?? followupSource?.TemplateCandidates ??
                latestAssistantPayload?.templateCandidates ?? latestAssistantPayload?.TemplateCandidates ?? [],
            generationMode: followupSource?.generationMode ?? followupSource?.GenerationMode ??
                latestAssistantPayload?.generationMode ?? latestAssistantPayload?.GenerationMode ?? '',
            templateLockLevel: followupSource?.templateLockLevel ?? followupSource?.TemplateLockLevel ??
                latestAssistantPayload?.templateLockLevel ?? latestAssistantPayload?.TemplateLockLevel ?? '',
            pendingParameters: followupSource?.pendingParameters ?? followupSource?.PendingParameters ?? [],
            missingResources: followupSource?.missingResources ?? followupSource?.MissingResources ?? [],
            requirementBrief: followupSource?.requirementBrief ?? followupSource?.RequirementBrief ?? latestAssistantPayload?.requirementBrief ?? latestAssistantPayload?.RequirementBrief ?? null,
            turnIntent: latestAssistantPayload?.turnIntent ?? latestAssistantPayload?.TurnIntent ?? '',
            interactionState: latestAssistantPayload?.interactionState ?? latestAssistantPayload?.InteractionState ?? '',
            routerConfidence: latestAssistantPayload?.routerConfidence ?? latestAssistantPayload?.RouterConfidence ?? '',
            blockingClarificationFields: latestAssistantPayload?.blockingClarificationFields ?? latestAssistantPayload?.BlockingClarificationFields ?? [],
            nonBlockingMissingFields: latestAssistantPayload?.nonBlockingMissingFields ?? latestAssistantPayload?.NonBlockingMissingFields ?? [],
            buildResult: restoredBuildResult,
            workflowDiff: restoredWorkflowDiff,
            applyGate: restoredApplyGate,
            toolEvidenceTimeline: restoredToolEvidence,
            firstFixRecommendation: restoredFirstFix,
            kind: latestAssistantPayload?.kind ?? latestAssistantPayload?.Kind ?? '',
            status: latestAssistantPayload?.status ?? latestAssistantPayload?.Status ?? '',
            success: latestAssistantPayload?.success ?? latestAssistantPayload?.Success,
            completionStatus: latestAssistantPayload?.completionStatus ?? latestAssistantPayload?.CompletionStatus ?? '',
            interactionState: latestAssistantPayload?.interactionState ?? latestAssistantPayload?.InteractionState ?? '',
            failureType: latestAssistantPayload?.failureType ?? latestAssistantPayload?.FailureType ?? '',
            failureSummary: latestAssistantPayload?.failureSummary ?? latestAssistantPayload?.FailureSummary ?? null,
            errorMessage: latestAssistantPayload?.errorMessage ?? latestAssistantPayload?.ErrorMessage ?? '',
            buildCompatibilityStatus: latestAssistantPayload?.buildCompatibilityStatus ?? latestAssistantPayload?.BuildCompatibilityStatus ?? '',
            compatibilityDiagnosticCode: latestAssistantPayload?.compatibilityDiagnosticCode ?? latestAssistantPayload?.CompatibilityDiagnosticCode ?? '',
            clarificationRequired: Boolean(
                followupSource?.clarificationRequired ??
                followupSource?.ClarificationRequired ??
                latestAssistantPayload?.clarificationRequired ??
                latestAssistantPayload?.ClarificationRequired
            ),
            sessionId
        };
        if (!workspaceSnapshot || workspaceSnapshot.trusted !== true) {
            restoredResult.flow = null;
            restoredResult.buildResult = null;
            restoredResult.workflowDiff = null;
            restoredResult.applyGate = null;
            restoredResult.toolEvidenceTimeline = [];
            restoredResult.success = false;
            restoredResult.status = 'degraded_restore';
            restoredResult.completionStatus = 'degraded_restore';
        } else if (workspaceSnapshot.appliedDowngraded) {
            stripRestoredApplyAuthority(restoredResult);
            restoredResult.status = 'completed';
            restoredResult.completionStatus = 'completed';
            restoredResult.interactionState = 'completed';
            this.appliedResultVersion = 0;
            this.appliedCanvasRevision = 0;
        }

        this._restoreWorkspaceSnapshotFromSession(workspaceSnapshot, sessionId, restoredResult);
        if (canvasFlow) {
            this._applySafetyBlockReason = this._restorePersistedApplySafetyBlock?.(restoredResult) || '';
            this._updateApplyButtonState?.();
            this._rebuildPendingOperatorBindings({
                pending: this._resolvePendingParametersForDraft(restoredResult),
                flow: restoredResult?.flow,
                sourceFlow: followupSource,
                preferIndexFallback: true
            });
        } else {
            this._resetCurrentResultSyncState({ clearPersistedApplySafetyBlock: false });
        }
        this._displayResult(restoredResult, { appendChatMessage: false });
        if (workspaceSnapshot?.appliedDowngraded) {
            this._applySafetyBlockReason = 'restored_applied_requires_revalidation';
            this._setWorkbenchState?.(AiWorkbenchStates.FAILED);
            this._setResultStatusNote?.('历史 Applied 状态已降级为待重新验证的 Build；完成新的验证或生成新结果前不可再次应用。', 'warning');
            this._updateApplyButtonState?.();
        } else if (this._applySafetyBlockReason) {
            this._setWorkbenchState?.(AiWorkbenchStates.FAILED);
            this._setResultStatusNote?.('检测到该结果上次应用后的画布未能安全恢复；请先完成明确的安全恢复或生成新结果。', 'warning');
            this._updateApplyButtonState?.();
        }

        const updatedAtUtc = session.updatedAtUtc ?? session.UpdatedAtUtc ?? new Date().toISOString();
        const latestMessage = normalizedHistory.length > 0
            ? normalizedHistory[normalizedHistory.length - 1].message
            : '（空会话）';
        this._addToHistory({
            sessionId,
            lastMessage: latestMessage,
            updatedAtUtc,
            turnCount: normalizedHistory.length
        });
    },

    _normalizeSessionWorkspaceSnapshot(raw) {
        return normalizeWorkspaceSnapshotForRestore(raw).snapshot;
    },

    _restoreWorkspaceSnapshotFromSession(snapshot, sessionId, result = null) {
        if (snapshot && snapshot.trusted === undefined) {
            snapshot = normalizeWorkspaceSnapshotForRestore(snapshot).snapshot;
        }
        if (!snapshot) {
            this._dispatchAgentWorkspaceEvent?.({
                type: 'workspace/session-restored',
                payload: {
                    sessionId,
                    result,
                    plan: null,
                    requirementMode: 'strict',
                    ui: { workspaceMode: AgentWorkspaceModes.PLAN, viewMode: AgentWorkspaceModes.PLAN }
                }
            });
            this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
            this._addMessage('system', '该历史版本不包含完整工作台状态，已仅恢复对话和可用结果。');
            this._renderAgentWorkspaceOverview?.();
            this._renderPlanWorkspace?.(this.pendingVisionPlan);
            this._renderBuildWorkspaceFromAgentRun?.();
            return false;
        }

        if (snapshot.degraded || snapshot.trusted !== true) {
            this._dispatchAgentWorkspaceEvent?.({
                type: 'workspace/session-restored',
                payload: {
                    sessionId,
                    result: null,
                    plan: snapshot.pendingPlanSnapshot || null,
                    readiness: null,
                    readinessPreview: null,
                    readinessStatus: 'idle',
                    run: {
                        plan: { runId: '', status: 'idle', events: [], eventKeys: {}, terminalSequence: null },
                        build: { runId: '', status: 'idle', events: [], eventKeys: {}, terminalSequence: null }
                    },
                    persistence: { snapshotRevision: 0, buildRunId: '', submittedBuildFingerprint: '' },
                    requirementMode: 'strict',
                    ui: { workspaceMode: AgentWorkspaceModes.PLAN, viewMode: AgentWorkspaceModes.PLAN }
                }
            });
            this.workspaceSnapshotRevision = 0;
            this.workspaceBuildRunId = '';
            this.workspaceSubmittedBuildFingerprint = '';
            this.activeAgentRunId = null;
            this.activePlanRunId = null;
            this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
            this._addMessage?.('system', '工作台快照版本缺失、过新或内容损坏，已仅保留安全的对话摘要；Build、Apply Ready 与 Applied 状态未恢复。');
            this._renderAgentWorkspaceOverview?.();
            this._renderPlanWorkspace?.(this.pendingVisionPlan);
            this._renderBuildWorkspaceFromAgentRun?.();
            return false;
        }

        this._applyWorkspaceSnapshotSummary?.(snapshot);
        const planSnapshot = snapshot.pendingPlanSnapshot;
        let normalizedPlan = null;
        if (planSnapshot && typeof this._normalizeBackendPlanResult === 'function') {
            const fallback = planSnapshot.originalUserPrompt || planSnapshot.OriginalUserPrompt || this.lastUserPrompt || '';
            normalizedPlan = this._normalizeBackendPlanResult(planSnapshot, fallback);
        }
        if (snapshot.appliedDowngraded) {
            normalizedPlan = downgradeRestoredAppliedPlan(normalizedPlan);
        }
        this.planAcceptedRecommendedDefaults = snapshot.planAcceptedRecommendedDefaults === true;
        this.activePlanRunRequestId = snapshot.planRunId ? `session-restore-plan-${snapshot.planRunId}` : null;
        this.activePlanRunCompletion = null;
        const lifecycle = snapshot.lifecycleState.toLowerCase();
        const workspaceMode = lifecycle.includes('build') || snapshot.buildRunId
            ? AgentWorkspaceModes.BUILD : AgentWorkspaceModes.PLAN;
        this._dispatchAgentWorkspaceEvent?.({
            type: 'workspace/session-restored',
            payload: {
                sessionId,
                revision: snapshot.revision,
                planId: normalizedPlan?.planId,
                planHash: normalizedPlan?.planHash,
                planRevision: snapshot.revision,
                plan: normalizedPlan,
                result,
                requirementMode: snapshot.requirementMode,
                confirmedAnswers: snapshot.confirmedPlanAnswers,
                optimisticAnswers: snapshot.optimisticPlanAnswers,
                answerRevision: snapshot.answerRevision,
                selections: snapshot.planQuestionSelections,
                readiness: snapshot.appliedDowngraded ? null : normalizedPlan?.buildReadiness,
                readinessPreview: snapshot.appliedDowngraded ? null : (snapshot.readinessPreview || normalizedPlan?.effectiveReadiness),
                readinessStatus: snapshot.appliedDowngraded ? 'idle' : (snapshot.readinessPreview || normalizedPlan ? 'ready' : 'idle'),
                resourceDecisions: snapshot.resourceDecisions,
                run: {
                    plan: { runId: snapshot.planRunId || '', status: snapshot.planRunStatus || 'idle', events: [], eventKeys: {}, terminalSequence: snapshot.planTerminalSequence },
                    build: { runId: snapshot.buildRunId || '', status: snapshot.buildRunStatus || 'idle', events: [], eventKeys: {}, terminalSequence: snapshot.buildTerminalSequence }
                },
                persistence: {
                    snapshotRevision: snapshot.revision,
                    buildRunId: snapshot.buildRunId || '',
                    submittedBuildFingerprint: snapshot.submittedBuildFingerprint || ''
                },
                ui: {
                    workspaceMode,
                    viewMode: snapshot.workspaceViewMode === AgentWorkspaceModes.BUILD
                        ? AgentWorkspaceModes.BUILD
                        : AgentWorkspaceModes.PLAN
                }
            }
        });
        this._setWorkspaceViewMode(
            snapshot.workspaceViewMode === AgentWorkspaceModes.BUILD
                ? AgentWorkspaceModes.BUILD
                : AgentWorkspaceModes.PLAN,
            { render: false });

        this._renderAgentWorkspaceOverview?.();
        this._renderPlanWorkspace?.(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun?.();
        this._updatePlanBuildActionState?.();

        const identity = this._captureSessionNavigationIdentity?.();
        this._restoreWorkspaceRunReplays(snapshot, sessionId, identity);
        return true;
    },

    async _restoreWorkspaceRunReplays(snapshot, sessionId, identity = null) {
        try {
            if (identity && !this._isSessionNavigationIdentityCurrent?.(identity)) return false;
            if (snapshot?.planRunId) {
                await this._replayAgentRunPublicEventsById?.(snapshot.planRunId, {
                    kind: 'plan',
                    statusText: '回放历史 Plan 事件',
                    identity
                });
            }
            if (identity && !this._isSessionNavigationIdentityCurrent?.(identity)) return false;
            if (snapshot?.buildRunId) {
                await this._replayAgentRunPublicEventsById?.(snapshot.buildRunId, {
                    kind: 'build',
                    statusText: '回放历史 Build 事件',
                    identity
                });
            }
            return true;
        } catch (error) {
            if (identity && !this._isSessionNavigationIdentityCurrent?.(identity)) return false;
            console.warn('[AiPanel] 恢复工作台 Run 回放失败。', {
                sessionId,
                error: error?.message || String(error)
            });
            this._addMessage?.('system', '历史工作台已从会话快照恢复，但部分事件回放失败。');
            return false;
        }
    },

    _captureSessionNavigationIdentity() {
        return Object.freeze({
            sessionId: String(this.sessionId || '').trim().toLowerCase(),
            navigationEpoch: Number(this.sessionNavigationEpoch || 0),
            lifecycleEpoch: Number(this._lifecycleEpoch || 0)
        });
    },

    _isSessionNavigationIdentityCurrent(identity) {
        return Boolean(identity) && !this._disposed &&
            identity.sessionId === String(this.sessionId || '').trim().toLowerCase() &&
            Number(identity.navigationEpoch) === Number(this.sessionNavigationEpoch || 0) &&
            Number(identity.lifecycleEpoch) === Number(this._lifecycleEpoch || 0);
    },

    _parseFlowJson(raw) {
        if (!raw) return null;
        if (typeof raw === 'object') return raw;
        if (typeof raw !== 'string') return null;
        try {
            return JSON.parse(raw);
        } catch (error) {
            console.warn('[AiPanel] 解析会话 flow JSON 失败。', {
                rawLength: raw.length,
                error: error?.message || String(error)
            });
            return null;
        }
    },

    _extractOperators(flow) {
        if (!flow) return [];
        if (Array.isArray(flow.operators)) return flow.operators;
        if (Array.isArray(flow.Operators)) return flow.Operators;
        return [];
    },

    _extractConnections(flow) {
        if (!flow) return [];
        if (Array.isArray(flow.connections)) return flow.connections;
        if (Array.isArray(flow.Connections)) return flow.Connections;
        return [];
    },

    _isCanvasFlowLike(flow) {
        if (!flow || typeof flow !== 'object') return false;
        const operators = this._extractOperators(flow);
        const connections = this._extractConnections(flow);
        if (!Array.isArray(operators) || !Array.isArray(connections)) {
            return false;
        }

        const hasValue = (value) => {
            if (value === null || value === undefined) return false;
            if (typeof value === 'string') return value.trim().length > 0;
            return true;
        };

        if (operators.length === 0) {
            return connections.length === 0;
        }

        const hasOperatorId = operators.every(op => {
            const id = op?.id ?? op?.Id;
            const type = op?.type ?? op?.Type;
            return hasValue(id) && hasValue(type);
        });
        if (!hasOperatorId) return false;

        return connections.every(conn => {
            const source = conn?.sourceOperatorId ?? conn?.SourceOperatorId ?? conn?.source;
            const target = conn?.targetOperatorId ?? conn?.TargetOperatorId ?? conn?.target;
            return hasValue(source) && hasValue(target);
        });
    },

    _normalizeSessionFlowForCanvas(flow, sessionId = '') {
        if (!flow || typeof flow !== 'object') return null;
        if (this._isCanvasFlowLike(flow)) {
            return flow;
        }
        console.warn('[AiPanel] 历史会话中的 flow 不是可直接反序列化的画布结构，已跳过应用兜底。', {
            sessionId,
            flowKeys: Object.keys(flow || {})
        });
        return null;
    },

    _deleteSession(sessionId) {
        if (!sessionId) return;
        if (String(this.pendingSessionLoad?.sessionId || '').trim().toLowerCase() === String(sessionId).trim().toLowerCase()) {
            this.sessionNavigationEpoch += 1;
            if (this.pendingSessionLoad?.timeoutId) window.clearTimeout?.(this.pendingSessionLoad.timeoutId);
            this.pendingSessionLoad = null;
        }
        webMessageBridge.sendMessage('DeleteAiSession', {
            payload: { sessionId }
        });
    },

    _handleDeleteAiSessionResult(data) {
        const payload = data?.payload || data || {};
        if (!payload.success) {
            this._addMessage('system', `删除会话失败: ${this._sanitizeSessionHistoryText(payload.errorMessage, 260) || '未知错误'}`);
            return;
        }

        const deletedSessionId = String(payload.sessionId ?? payload.SessionId ?? '').trim();
        if (!deletedSessionId) return;

        this.history = this.history.filter(item => item.sessionId !== deletedSessionId);
        this._filterHistory(this.historyKeyword);

        if (this.sessionId === deletedSessionId) {
            this._handleNewConversation();
        }
    },

    _loadSessionId() {
        try {
            return localStorage.getItem(this.sessionStorageKey);
        } catch {
            return null;
        }
    },

    _saveSessionId(sessionId) {
        try {
            if (sessionId) {
                localStorage.setItem(this.sessionStorageKey, sessionId);
            } else {
                localStorage.removeItem(this.sessionStorageKey);
            }
        } catch {
            // ignore localStorage failures
        }
    },

    _createSessionLoadRequestId() {
        if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID();
        }
        return `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    },

    _adoptCanonicalSessionId(sessionId, { reason = '' } = {}) {
        const normalized = String(sessionId || '').trim();
        const current = String(this.sessionId || '').trim();
        if (normalized.toLowerCase() === current.toLowerCase()) {
            this.sessionId = normalized || null;
            this._dispatchAgentWorkspaceEvent?.({
                type: 'workspace/session-adopted',
                payload: { sessionId: this.sessionId || '' }
            });
            this._saveSessionId?.(this.sessionId);
            return false;
        }

        this.sessionNavigationEpoch = (Number(this.sessionNavigationEpoch) || 0) + 1;
        if (this.pendingSessionLoad?.timeoutId) window.clearTimeout?.(this.pendingSessionLoad.timeoutId);
        this.pendingSessionLoad = null;
        this.sessionId = normalized || null;
        this._dispatchAgentWorkspaceEvent?.({
            type: 'workspace/session-adopted',
            payload: { sessionId: this.sessionId || '' }
        });
        this._saveSessionId?.(this.sessionId);
        return true;
    }

    // ── 工作台状态机 ──────────────────────────────────────────

    // ── 生成流水线时间线 ──────────────────────────────────────

    // ── 校验与 DryRun 控制台 ──────────────────────────────────

    // ── 附件与模型能力面板 ────────────────────────────────────
};
