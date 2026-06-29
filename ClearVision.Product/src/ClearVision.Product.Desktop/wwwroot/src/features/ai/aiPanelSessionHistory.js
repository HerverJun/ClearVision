import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import { AgentWorkspaceModes } from './aiPanelAgentWorkspace.js';
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

    _normalizeSessionSummary(entry) {
        const sessionId = String(entry?.sessionId ?? entry?.SessionId ?? '').trim();
        if (!sessionId) return null;

        const lastMessage = String(entry?.lastMessage ?? entry?.LastMessage ?? '').trim();
        const updatedAtUtc = String(entry?.updatedAtUtc ?? entry?.UpdatedAtUtc ?? new Date().toISOString());
        const turnCountRaw = Number(entry?.turnCount ?? entry?.TurnCount ?? 0);
        return {
            sessionId,
            lastMessage: lastMessage || '（空会话）',
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
                const text = `${item.lastMessage} ${item.sessionId}`.toLowerCase();
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
            const templateBadge = item.templateName
                ? `<span class="history-template-badge">${this._escapeHtml(item.templateName)}</span>`
                : '';
            const modeChip = item.generationMode
                ? `<span class="history-mode-chip">${this._escapeHtml(item.generationMode)}</span>`
                : '';
            const appliedIcon = item.applied ? '<span class="history-applied-icon" title="已应用">&#10003;</span>' : '';
            return `
            <div class="ai-history-item ${item.sessionId === this.sessionId ? 'active' : ''}" data-session-id="${this._escapeHtml(item.sessionId)}">
                <div class="history-desc">${this._escapeHtml(item.lastMessage)}</div>
                <div class="history-badges">${templateBadge}${modeChip}${appliedIcon}</div>
                <div class="history-meta">
                    <span>${this._escapeHtml(this._formatHistoryTime(item.updatedAtUtc))}</span>
                    <span>${this._escapeHtml(String(item.turnCount))} 轮</span>
                </div>
                <button class="ai-history-delete" type="button" data-session-id="${this._escapeHtml(item.sessionId)}" title="删除会话">删除</button>
            </div>
        `}).join('');

        list.querySelectorAll('.ai-history-item').forEach(itemEl => {
            itemEl.addEventListener('click', (event) => {
                if (event.target.closest('.ai-history-delete')) return;
                const sessionId = itemEl.dataset.sessionId || '';
                this._switchToSession(sessionId);
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
                this._addMessage('system', `历史加载失败: ${payload.errorMessage}`);
            }
            return;
        }

        const sessions = Array.isArray(payload.sessions) ? payload.sessions : [];
        this.history = sessions
            .map(item => this._normalizeSessionSummary(item))
            .filter(Boolean)
            .sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime());
        this._filterHistory(this.historyKeyword);
    },

    async _switchToSession(sessionId) {
        if (!sessionId) return;
        if (this.isGenerating) {
            this._addMessage('system', '正在生成中，暂时无法切换历史会话。');
            return;
        }

        const flushed = (await this._flushWorkspaceSnapshotBeforeBoundary?.('history_switch')) ?? true;
        if (!flushed) {
            this._setResultStatusNote?.('Plan 修改尚未成功保存，已阻止切换历史。', 'warning');
            return;
        }

        webMessageBridge.sendMessage('GetAiSession', {
            payload: { sessionId }
        });
    },

    _handleGetAiSessionResult(data) {
        const payload = data?.payload || data || {};
        if (!payload.success) {
            this._addMessage('system', `会话恢复失败: ${payload.errorMessage || '未知错误'}`);
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

        this.sessionId = sessionId;
        this._saveSessionId(this.sessionId);
        const workspaceSnapshot = this._normalizeSessionWorkspaceSnapshot(session.workspaceSnapshot ?? session.WorkspaceSnapshot);
        this.nextHintDraft = '';
        this.nextTemplateSelection = null;
        this._resetPendingDraftState();
        this._resetCurrentResultSyncState();
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

        if (canvasFlow) {
            this._setCurrentResult(restoredResult);
            this._rebuildPendingOperatorBindings({
                pending: this._resolvePendingParametersForDraft(restoredResult),
                flow: restoredResult?.flow,
                sourceFlow: followupSource,
                preferIndexFallback: true
            });
        } else {
            this._resetCurrentResultSyncState();
        }
        this._displayResult(restoredResult, { appendChatMessage: false });
        this._restoreWorkspaceSnapshotFromSession(workspaceSnapshot, sessionId);

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
        if (!raw || typeof raw !== 'object') return null;
        const snapshot = raw;
        const read = (camel, pascal = '') => snapshot?.[camel] ?? snapshot?.[pascal || `${camel[0].toUpperCase()}${camel.slice(1)}`];
        return {
            schemaVersion: Number(read('schemaVersion')) || 0,
            revision: Number(read('revision')) || 0,
            lifecycleState: String(read('lifecycleState') || 'idle').trim(),
            pendingPlanSnapshot: read('pendingPlanSnapshot'),
            planQuestionSelections: read('planQuestionSelections') || {},
            confirmedPlanAnswers: Array.isArray(read('confirmedPlanAnswers')) ? read('confirmedPlanAnswers') : [],
            requirementMode: String(read('requirementMode') || 'strict').trim().toLowerCase(),
            planAcceptedRecommendedDefaults: read('planAcceptedRecommendedDefaults') === true,
            planRunId: String(read('planRunId') || '').trim(),
            planRunStatus: String(read('planRunStatus') || '').trim().toLowerCase(),
            buildRunId: String(read('buildRunId') || '').trim(),
            buildRunStatus: String(read('buildRunStatus') || '').trim().toLowerCase(),
            submittedBuildFingerprint: String(read('submittedBuildFingerprint') || '').trim()
        };
    },

    _restoreWorkspaceSnapshotFromSession(snapshot, sessionId) {
        if (!snapshot) {
            this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
            this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
            this._addMessage('system', '该历史版本不包含完整工作台状态，已仅恢复对话和可用结果。');
            this._renderAgentWorkspaceOverview?.();
            this._renderPlanWorkspace?.(this.pendingVisionPlan);
            this._renderBuildWorkspaceFromAgentRun?.();
            return false;
        }

        const planSnapshot = snapshot.pendingPlanSnapshot;
        if (planSnapshot && typeof this._normalizeBackendPlanResult === 'function') {
            const fallback = planSnapshot.originalUserPrompt || planSnapshot.OriginalUserPrompt || this.lastUserPrompt || '';
            this.pendingVisionPlan = this._normalizeBackendPlanResult(planSnapshot, fallback);
            this.pendingVisionPlan.requirementMode = snapshot.requirementMode || this.pendingVisionPlan.requirementMode || 'strict';
        }

        this.planQuestionSelections = { ...(snapshot.planQuestionSelections || {}) };
        this.planQuestionAnswers = {};
        snapshot.confirmedPlanAnswers.forEach(answer => {
            const normalized = this._normalizePlanAnswer?.(answer) || answer;
            const key = normalized?.questionId || normalized?.field || normalized?.Field || normalized?.QuestionId || '';
            if (key) {
                this.planQuestionAnswers[key] = normalized;
            }
        });
        this.requirementMode = snapshot.requirementMode === 'draft' ? 'draft' : 'strict';
        this.planAcceptedRecommendedDefaults = snapshot.planAcceptedRecommendedDefaults === true;
        this.activePlanRunId = snapshot.planRunId || null;
        this.activePlanRunRequestId = snapshot.planRunId ? `session-restore-plan-${snapshot.planRunId}` : null;
        this.activePlanRunEvents = [];
        this.activePlanRunEventKeys = new Set();
        this.activePlanRunCompletion = null;
        this.activeAgentRunId = snapshot.buildRunId || null;
        this.activeAgentRunEvents = [];
        this.activeAgentRunEventKeys = new Set();

        const lifecycle = snapshot.lifecycleState.toLowerCase();
        this.agentWorkspaceMode = lifecycle.includes('build') || snapshot.buildRunId
            ? AgentWorkspaceModes.BUILD
            : AgentWorkspaceModes.PLAN;
        this._setWorkspaceViewMode(snapshot.buildRunId ? AgentWorkspaceModes.BUILD : AgentWorkspaceModes.PLAN, { render: false });

        this._renderAgentWorkspaceOverview?.();
        this._renderPlanWorkspace?.(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun?.();
        this._updatePlanBuildActionState?.();

        this._restoreWorkspaceRunReplays(snapshot, sessionId);
        return true;
    },

    async _restoreWorkspaceRunReplays(snapshot, sessionId) {
        try {
            if (snapshot?.planRunId) {
                await this._replayAgentRunPublicEventsById?.(snapshot.planRunId, {
                    kind: 'plan',
                    statusText: '回放历史 Plan 事件'
                });
            }
            if (snapshot?.buildRunId) {
                await this._replayAgentRunPublicEventsById?.(snapshot.buildRunId, {
                    kind: 'build',
                    statusText: '回放历史 Build 事件'
                });
            }
        } catch (error) {
            console.warn('[AiPanel] 恢复工作台 Run 回放失败。', {
                sessionId,
                error: error?.message || String(error)
            });
            this._addMessage?.('system', '历史工作台已从会话快照恢复，但部分事件回放失败。');
        }
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
        webMessageBridge.sendMessage('DeleteAiSession', {
            payload: { sessionId }
        });
    },

    _handleDeleteAiSessionResult(data) {
        const payload = data?.payload || data || {};
        if (!payload.success) {
            this._addMessage('system', `删除会话失败: ${payload.errorMessage || '未知错误'}`);
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
    }

    // ── 工作台状态机 ──────────────────────────────────────────

    // ── 生成流水线时间线 ──────────────────────────────────────

    // ── 校验与 DryRun 控制台 ──────────────────────────────────

    // ── 附件与模型能力面板 ────────────────────────────────────
};
