import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import httpClient from '../../core/messaging/httpClient.js';
import { createSignal } from '../../core/state/store.js';
import { buildWireSequenceFollowupHint } from '../flow-editor/wireSequenceAssist.js';
import {
    getOperatorTypeDisplayName,
    getResourceDisplayName
} from '../../shared/operatorDisplayNames.js';
import {
    AiWorkbenchStates,
    STAGE_DIAGNOSTIC_LABELS,
    aiPanelWorkbenchMixin
} from './aiPanelWorkbench.js';
import { aiPanelPendingParametersMixin } from './aiPanelPendingParameters.js';
import { aiPanelResourceBindingMixin } from './aiPanelResourceBinding.js';
import { aiPanelChatMixin } from './aiPanelChat.js';
import { aiPanelValidationPreviewMixin } from './aiPanelValidationPreview.js';
import { aiPanelGenerateRequestMixin } from './aiPanelGenerateRequest.js';
import { aiPanelRequirementBriefMixin } from './aiPanelRequirementBrief.js';
import { aiPanelAttachmentsMixin } from './aiPanelAttachments.js';
import { aiPanelSessionHistoryMixin } from './aiPanelSessionHistory.js';
import { aiPanelApplyPreviewMixin } from './aiPanelApplyPreview.js';
import { aiPanelTopologySummaryMixin } from './aiPanelTopologySummary.js';
import { aiPanelAgentRunMixin } from './aiPanelAgentRun.js';
import { aiPanelLiveEventsMixin } from './aiPanelLiveEvents.js';
import { aiPanelLifecycleMixin } from './aiPanelLifecycle.js';
import { aiPanelAccessibilityMixin } from './aiPanelAccessibility.js';
import {
    initializeAiPanelShell,
    installAiPanelShellPresentation
} from './aiPanelShellPresentation.js';
import { installAiPanelPlanPresentation } from './aiPanelPlanPresentation.js';
import {
    installAiPanelBuildPresentation,
    renderAiBuildWorkspaceScaffold
} from './aiPanelBuildPresentation.js';
import {
    AgentWorkspaceModes,
    aiPanelAgentWorkspaceMixin
} from './aiPanelAgentWorkspace.js';
import {
    AgentWorkspaceEventTypes,
    createAgentWorkspaceSnapshot,
    dispatchAgentWorkspaceEvent,
    installAgentWorkspaceState
} from './agentWorkspaceState.js';

/**
 * AI 智能助手面板
 * 负责管理 AI 交互界面、发送生成请求、显示公开诊断和结果
 */
export class AiPanel {
    constructor(containerId, flowCanvas, options = {}) {
        this.containerId = containerId;
        this.flowCanvas = flowCanvas;
        this.options = options || {};
        this.container = document.getElementById(containerId);
        this.sessionStorageKey = 'cv_ai_session_id';
        this.workspaceViewStorageKey = 'cv_ai_workspace_view_mode';

        // 状态
        this.isGenerating = false;
        this.history = []; // { sessionId, lastMessage, updatedAtUtc, turnCount }
        this.filteredHistory = [];
        this.historyKeyword = '';
        this.isHistoryPanelOpen = false;
        this.currentThinkingStep = null;
        this.sessionId = this._loadSessionId();
        installAgentWorkspaceState(this, {
            sessionId: this.sessionId,
            requirementMode: 'strict',
            viewMode: this._loadWorkspaceViewMode?.() || AgentWorkspaceModes.PLAN
        });
        this.initialAutoRestoreSessionId = this.sessionId;
        this.sessionNavigationEpoch = 0;
        this.pendingSessionLoad = null;
        this.autoRestoreAttempted = false;
        this.autoRestoreNoticeShown = false;
        this.currentResult = null;
        this.lastUserPrompt = '';
        this.nextHintDraft = '';
        this.nextTemplateSelection = null;
        this.activeGenerateRequestId = null;
        this.activePlanRequestId = null;
        this.activePlanRunId = null;
        this.activePlanRunRequestId = null;
        this.activePlanRunEvents = [];
        this.activePlanRunEventKeys = new Set();
        this.activePlanRunCompletion = null;
        this.activeGenerateSessionId = null;
        this.isCancellingGenerate = false;
        this.attachments = [];
        this.pendingParameterDrafts = {};
        this.pendingResourceDrafts = {};
        this.pendingParameterDraftSignature = '';
        this.operatorMetadataCache = new Map();
        this.operatorMetadataLoading = new Map();
        this.cameraBindingsCache = [];
        this.cameraBindingsLoadingPromise = null;
        this.currentResultVersion = 0;
        this.appliedResultVersion = 0;
        this.currentCanvasRevision = this.flowCanvas?.getFlowRevision?.() || 0;
        this.appliedCanvasRevision = 0;
        this.appliedCanvasBaselineFlow = null;
        this.canvasManualEditRecords = [];
        this.canvasManualEditSignature = '';
        this.pendingOperatorBindings = {};
        this.unsubscribeStructureState = null;
        this.pendingParameterFilePickContext = null;
        this.pendingParameterHighlightTimer = null;
        this.pendingParameterConfirmedDraftSignature = '';
        this.pendingParameterConfirmedValueSignature = '';
        this._streamBuffer = { thinking: '', content: '' };
        this._streamFlushPending = false;
        this.activeAssistantTurn = null;
        this.activeAgentRunId = null;
        this.activeAgentRunEventSource = null;
        this.activeAgentRunEvents = [];
        this.activeAgentRunEventKeys = new Set();
        this.agentRunStepMap = new Map();
        this.agentRunToolMap = new Map();
        this.agentRunArtifactMap = new Map();
        this.publicLiveEventKeys = new Set();
        this.publicLiveEvents = [];
        this.publicLiveStatusTimer = null;
        this.publicLiveWorkbenchSequence = 0;
        this.pendingManualRetry = null;
        this.requirementMode = 'strict';

        // 工作台状态机
        this.workbenchState = AiWorkbenchStates.IDLE;
        this._lastActiveWorkbenchState = AiWorkbenchStates.IDLE;
        this._workbenchStageTimeline = [];
        this._lastAgentRuntime = null;
        this.isVisionAgentDeveloperUiEnabled = this._isAgentDeveloperControlsEnabled();
        this.useVisionAgentGenerateFlow = this._loadAgentGenerateFlowEnabled();
        this.agentGenerateFlowMode = this._loadAgentGenerateFlowMode();
        this.runtimePreviewConsent = false;
        this.directBuildDebugNextRequest = false;
        this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
        this.workspaceViewMode = this._loadWorkspaceViewMode?.() || AgentWorkspaceModes.PLAN;
        this.workspaceSnapshotRevision = 0;
        this.workspaceSnapshotDirty = false;
        this.workspaceSnapshotSaveQueue = Promise.resolve();
        this.workspaceMutationGeneration = 0;
        this.workspacePersistedGeneration = 0;
        this.workspacePendingMutationCount = 0;
        this.workspaceSaveErrorGeneration = 0;
        this.workspaceBoundaryInProgress = false;
        this.workspaceBuildRunId = '';
        this.workspaceSubmittedBuildFingerprint = '';
        this.workspacePersistenceWarning = null;
        this._workspacePersistenceStatusNoteActive = false;
        this._workspacePersistenceStatusNoteText = '';
        this.pendingVisionPlan = null;
        this.pendingClarificationPayload = null;
        this.planQuestionSelections = {};
        this.planQuestionAnswers = {};
        this.planAnswerRevision = 0;
        this.planAcceptedRecommendedDefaults = false;
        this.planRequirementModes = new Map();
        this.currentPlanIdentity = '';
        this.effectiveReadiness = null;
        this.previewState = 'idle';
        this.activePlanReadinessPreviewController = null;
        this.activePlanReadinessPreviewRequest = null;
        this.lastPlanReadinessPreviewError = '';

        // 应用预览与撤销
        this._preApplySnapshot = null;
        this._preApplySnapshotVersion = 0;
        this._preApplyCanvasRevision = 0;

        // 附件报告缓存
        this._lastAttachmentReport = null;
        this._lastModelSupportsVision = null;
        this._chatContainer = null;
        this._scrollBottomButton = null;
        this._scrollBottomBadge = null;
        this._scrollStateRaf = 0;
        this._inputResizeObserver = null;
        this._messageUnsubscribes = [];
        this._chatScrollHandler = null;
        this._composerResizeHandler = null;
        this._disposed = false;
        this._lifecycleEpoch = 1;
        this._accessibilityInitialized = false;
        this._activeApplyPreview = null;
        this._applyInFlight = false;
        this.userHasScrolledUp = false;
        this.unreadStreamCount = 0;

        // 绑定方法
        this._handleGenerate = this._handleGenerate.bind(this);
        this._handleApplyFlow = this._handleApplyFlow.bind(this);
        this._handleConfirmPendingParameters = this._handleConfirmPendingParameters.bind(this);
        this._handlePendingParameterReview = this._handlePendingParameterReview.bind(this);
        this._handleNewConversation = this._handleNewConversation.bind(this);
        this._handleAttachmentClick = this._handleAttachmentClick.bind(this);
        this._handleFilePickedEvent = this._handleFilePickedEvent.bind(this);
        this._handleAttachmentReport = this._handleAttachmentReport.bind(this);
        this._handleCancelGenerate = this._handleCancelGenerate.bind(this);
        this._toggleHistoryPanel = this._toggleHistoryPanel.bind(this);

        // 初始化
        this._init();
        if (typeof window !== 'undefined') {
            this._workspaceFlushHandler = async (reason = 'host_close') =>
                (await this._flushWorkspaceSnapshotBeforeBoundary?.(reason)) ?? true;
            window.__clearVisionFlushAiPanelWorkspace = this._workspaceFlushHandler;
        }
    }

    _dispatchAgentWorkspaceEvent(event) {
        return dispatchAgentWorkspaceEvent(this, {
            sessionId: this.sessionId,
            ...event
        });
    }

    _ensureAgentWorkspaceState(seed = {}) {
        return installAgentWorkspaceState(this, {
            sessionId: this.sessionId,
            ...seed
        });
    }

    _createAgentWorkspaceSnapshot() {
        return createAgentWorkspaceSnapshot(this.agentWorkspaceState);
    }

    _init() {
        if (this._disposed || this._initialized) return;
        if (!this.container) {
            console.error('[AiPanel] 容器未找到:', this.containerId);
            return;
        }
        this._initialized = true;

        this.render();
        this._setupMessageListeners();
        this._setupCanvasStructureSync();
        this._loadHistory();
        this._setupScrollListener();
        this._setupComposerLayoutSync();
        this._setupExamplesFolding();
        this._setupAccessibility?.();
    }

    activate() {
        this._checkConnection();
        const mainContent = this.container.closest('.main-content');
        if (mainContent) {
            mainContent.scrollTop = 0;
        }

        const textarea = this.container.querySelector('.ai-textarea');
        if (textarea) {
            try {
                textarea.focus({ preventScroll: true });
            } catch {
                textarea.focus();
                if (mainContent) {
                    mainContent.scrollTop = 0;
                }
            }
        }
    }

    async _handleNewConversation() {
        const flushed = (await this._flushWorkspaceSnapshotBeforeBoundary?.('new_conversation')) ?? true;
        if (!flushed) {
            this._setResultStatusNote?.('Plan 修改尚未成功保存，已阻止新建会话。', 'warning');
            return;
        }

        this.sessionNavigationEpoch += 1;
        if (this.pendingSessionLoad?.timeoutId) window.clearTimeout?.(this.pendingSessionLoad.timeoutId);
        this.pendingSessionLoad = null;
        this.sessionId = null;
        this._saveSessionId(null);
        this.currentResult = null;
        this.lastUserPrompt = '';
        this.unreadStreamCount = 0;
        this.userHasScrolledUp = false;
        this._updateScrollBottomBtn();
        this.nextHintDraft = '';
        this.nextTemplateSelection = null;
        this.activeGenerateRequestId = null;
        this.activePlanRequestId = null;
        this.activePlanRunId = null;
        this.activePlanRunRequestId = null;
        this.activePlanRunEvents = [];
        this.activePlanRunEventKeys = new Set();
        this.activePlanRunCompletion = null;
        this._resetPublicLiveEventState?.();
        this.activeGenerateSessionId = null;
        this.isCancellingGenerate = false;
        this._resetAgentRunState();
        this.attachments = [];
        this._resetPendingDraftState();
        this._resetCurrentResultSyncState();
        this.pendingParameterFilePickContext = null;
        this.pendingManualRetry = null;
        this.pendingClarificationPayload = null;
        this.appliedCanvasBaselineFlow = null;
        this.canvasManualEditRecords = [];
        this.canvasManualEditSignature = '';
        this.activeAssistantTurn = null;
        this.directBuildDebugNextRequest = false;
        this.workspaceSnapshotRevision = 0;
        this.workspaceSnapshotDirty = false;
        this.workspaceSnapshotSaveQueue = Promise.resolve();
        this.workspaceMutationGeneration = 0;
        this.workspacePersistedGeneration = 0;
        this.workspacePendingMutationCount = 0;
        this.workspaceSaveErrorGeneration = 0;
        this.workspaceBoundaryInProgress = false;
        this.workspaceBuildRunId = '';
        this.workspaceSubmittedBuildFingerprint = '';
        this.workspacePersistenceWarning = null;
        this._workspacePersistenceStatusNoteActive = false;
        this._workspacePersistenceStatusNoteText = '';
        this._preApplySnapshot = null;
        this._lastAttachmentReport = null;
        this._lastModelSupportsVision = null;
        this._resetAgentWorkspace();
        this._setWorkbenchState(AiWorkbenchStates.IDLE);
        this._clearResultPane();
        this._renderAttachments();
        this._renderManualRetryBanner();
        this._renderQueuedHintBanner();
        const container = this.container.querySelector('#ai-chat-container');
        if (container) container.innerHTML = '';
        this._addMessage('ai', '您好！我是您的视觉工程助手。已开始新对话。');
    }

    _setupCanvasStructureSync() {
        this.currentCanvasRevision = this.flowCanvas?.getFlowRevision?.() || 0;
        if (!this.flowCanvas?.subscribeStructureState) {
            return;
        }

        if (this.unsubscribeStructureState) {
            this.unsubscribeStructureState();
        }

        this.unsubscribeStructureState = this.flowCanvas.subscribeStructureState((payload) => {
            const revision = Number(payload?.flowRevision);
            if (Number.isFinite(revision)) {
                this.currentCanvasRevision = revision;
            } else {
                this.currentCanvasRevision = this.flowCanvas?.getFlowRevision?.() || this.currentCanvasRevision;
            }

            if (!this._isCurrentResultAppliedToCanvas() || !this.currentResult?.flow) {
                return;
            }

            const canvasFlow = this.flowCanvas?.serialize?.() || this.currentResult.flow;
            this._syncCanvasManualEditRecords?.(canvasFlow);
            this._syncPendingParameterDrafts(this.currentResult, this.currentResult.flow, { force: true });
            this._renderFollowupChecklist(this.currentResult, canvasFlow);
            const editor = this.container?.querySelector('#ai-result-parameter-editor');
            if (editor && !editor.classList.contains('is-empty')) {
                this._renderParameterDraftEditor(this.currentResult, canvasFlow);
            }
        });
    }

    _resetPendingDraftState() {
        this.pendingParameterDrafts = {};
        this.pendingResourceDrafts = {};
        this.pendingParameterDraftSignature = '';
        this.pendingOperatorBindings = {};
        this.pendingParameterConfirmedDraftSignature = '';
        this.pendingParameterConfirmedValueSignature = '';
    }

    _resetCurrentResultSyncState() {
        this._closeApplyPreview?.({ restoreFocus: false, setReady: false });
        this.currentResult = null;
        this.currentResultVersion = 0;
        this.appliedResultVersion = 0;
        this.appliedCanvasRevision = this.currentCanvasRevision;
        this.appliedCanvasBaselineFlow = null;
        this.canvasManualEditRecords = [];
        this.canvasManualEditSignature = '';
        this._updateApplyButtonState();
    }

    _setCurrentResult(payload) {
        this._closeApplyPreview?.({ restoreFocus: false, setReady: false });
        this.currentResult = payload;
        this.currentResultVersion += 1;
        this.appliedResultVersion = 0;
        this.appliedCanvasRevision = 0;
        this.appliedCanvasBaselineFlow = null;
        this.canvasManualEditRecords = [];
        this.canvasManualEditSignature = '';
        this._updateApplyButtonState();
    }

    _markCurrentResultAppliedToCanvas() {
        if (!this.currentResultVersion) return;
        this.currentCanvasRevision = this.flowCanvas?.getFlowRevision?.() || this.currentCanvasRevision;
        this.appliedResultVersion = this.currentResultVersion;
        this.appliedCanvasRevision = this.currentCanvasRevision;
        this._updateApplyButtonState();
    }

    _isCurrentResultAppliedToCanvas() {
        return Boolean(this.currentResult && this.currentResultVersion > 0 && this.appliedResultVersion === this.currentResultVersion);
    }

    _updateApplyButtonState() {
        const button = this.container?.querySelector('#ai-btn-apply');
        if (!button) return;

        const flow = this._getResultFlowForCanvas?.(this.currentResult) ||
            this.currentResult?.flow ||
            this.currentResult?.Flow ||
            null;
        const hasFlow = Boolean(flow && this._extractOperators(flow).length > 0);
        const canvasApplyAllowed = !this.currentResult ||
            (this._isCanvasApplyReadyForResult?.(this.currentResult) ?? true);
        const applied = this._isCurrentResultAppliedToCanvas();
        button.disabled = this.isGenerating || this._applyInFlight || Boolean(this._activeApplyPreview) || !hasFlow || !canvasApplyAllowed || applied;
        button.classList.toggle('is-disabled', button.disabled);
        button.setAttribute('aria-disabled', button.disabled ? 'true' : 'false');
        const label = applied
            ? '已应用到画布'
            : hasFlow
                ? (canvasApplyAllowed ? '应用到画布' : '当前草稿暂不可应用')
                : '暂无可应用方案';
        button.innerHTML = `
            <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" style="margin-right:6px;">
                <path d="M9 16.2L4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4L9 16.2z"/>
            </svg>
            ${this._escapeHtml(label)}
        `;
    }

    render() {
        this.container.innerHTML = `
            <div class="ai-shell" data-ai-hook="shell" data-ai-shell-state="idle" data-ai-active-pane="workbench">
                <header class="ai-task-context" data-ai-hook="task-context" hidden>
                    <div class="ai-task-context-copy">
                        <div class="ai-task-context-heading">
                            <span class="ai-task-context-kicker">当前任务</span>
                            <h2 data-ai-hook="task-title" hidden></h2>
                            <span class="ai-task-context-phase" data-ai-hook="task-phase"></span>
                            <span class="ai-task-context-blockers" data-ai-hook="task-blockers" hidden></span>
                        </div>
                        <p data-ai-hook="task-next-step" hidden></p>
                    </div>
                    <div class="ai-task-context-actions">
                        <div class="ai-task-primary-action" data-ai-hook="task-primary-action"></div>
                        <button class="ai-task-more-button" data-ai-hook="task-more" type="button" aria-expanded="false">
                            更多
                        </button>
                        <div class="ai-task-more-menu" data-ai-hook="task-more-menu" hidden></div>
                    </div>
                </header>

                <nav class="ai-shell-tabs" data-ai-hook="compact-tabs" role="tablist" aria-label="AI 页面区域">
                    <button type="button" role="tab" data-ai-hook="compact-tab" data-ai-shell-pane="workbench" aria-controls="ai-result-pane" aria-selected="true" tabindex="0">工作台</button>
                    <button type="button" role="tab" data-ai-hook="compact-tab" data-ai-shell-pane="conversation" aria-controls="ai-conversation-pane" aria-selected="false" tabindex="-1">会话</button>
                </nav>

                <div class="ai-workspace" data-ai-hook="workspace">
                <aside class="ai-pane-left" id="ai-conversation-pane" role="tabpanel" tabindex="0" data-ai-chat-pane="true" data-ai-hook="conversation-pane">
                    <div class="ai-pane-header">
                        <span class="pane-icon">
                            <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor"><path d="M20 2H4c-1.1 0-2 .9-2 2v18l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2z"/></svg>
                        </span>
                        <span class="pane-title">智能体对话</span>
                        <span class="status-badge online" id="ai-conn-status"><span class="status-dot connected"></span>在线</span>
                        <div class="ai-pane-actions" role="group" aria-label="AI 对话操作">
                            <button class="icon-btn ai-action-btn" id="ai-btn-new-session" type="button" title="新建对话" aria-label="新建对话">
                                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                                    <path d="M12 5v14"></path>
                                    <path d="M5 12h14"></path>
                                </svg>
                                <span>新对话</span>
                            </button>
                            <button class="icon-btn ai-action-btn ai-btn-history" id="ai-btn-history" type="button" title="历史会话" aria-label="历史会话" aria-expanded="false">
                                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                                    <path d="M3 12a9 9 0 1 0 3-6.7"></path>
                                    <path d="M3 4v5h5"></path>
                                    <path d="M12 7v5l3 2"></path>
                                </svg>
                                <span>历史</span>
                            </button>
                        </div>
                    </div>

                    <section class="ai-idle-intro" data-ai-hook="idle-intro">
                        <div class="ai-idle-utility" data-ai-hook="idle-actions"></div>
                        <div class="ai-idle-copy">
                            <span>ClearVision AI</span>
                            <h2>描述你的视觉任务</h2>
                            <p>从检测对象、图像来源和输出目标开始，AI 会沿用现有规划、构建与恢复链路。</p>
                        </div>
                        <div class="ai-idle-recent" data-ai-hook="idle-recent" hidden>
                            <div class="ai-idle-recent-heading">最近任务</div>
                            <div class="ai-idle-recent-list" data-ai-hook="idle-recent-list"></div>
                        </div>
                    </section>

                    <div class="ai-history-panel" id="ai-history-panel">
                        <div class="ai-history-panel-inner">
                            <input
                                type="text"
                                class="ai-history-search"
                                id="ai-history-search"
                                placeholder="搜索历史会话..."
                            />
                            <div class="ai-history-list" id="ai-history-list"></div>
                        </div>
                    </div>

                    <div class="ai-chat-container" id="ai-chat-container">
                        <div class="ai-message ai">
                            <div class="ai-bubble">您好！我是您的视觉工程助手。请描述您想要检测的缺陷，我将为您构建流水线。</div>
                        </div>
                    </div>

                    <div class="ai-input-section">
                        <div class="ai-agent-mode-bar">
                            <div>
                                <strong>规划 / 构建</strong>
                                <span>先形成工程计划，再由后端事件驱动构建。</span>
                            </div>
                            <button class="ai-agent-mode-build-btn" id="ai-btn-start-build-inline" type="button">开始构建</button>
                        </div>
                        ${this._renderAgentDeveloperControls()}
                        <div class="ai-input-box">
                            <button class="icon-btn" id="ai-btn-attach" type="button" title="添加附件" aria-label="添加附件">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor" aria-hidden="true"><path d="M16.5 6v11.5c0 2.21-1.79 4-4 4s-4-1.79-4-4V5a2.5 2.5 0 015 0v10.5c0 .55-.45 1-1 1s-1-.45-1-1V6H10v9.5a2.5 2.5 0 005 0V5c0-1.38-1.12-2.5-2.5-2.5S8 3.62 8 5v11.5c0 3.04 2.46 5.5 5.5 5.5s5.5-2.46 5.5-5.5V6h-1.5z"/></svg>
                            </button>
                            <textarea class="ai-textarea" id="ai-input" aria-label="视觉任务需求" aria-describedby="ai-input-help" placeholder="描述检测目标、缺陷或流程修改..."></textarea>
                            <button class="ai-btn-cancel" id="ai-btn-cancel" type="button" title="取消生成">取消</button>
                            <button class="ai-btn-send" id="ai-btn-gen" type="button" title="发送" aria-label="发送">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="white" aria-hidden="true"><path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z"/></svg>
                            </button>
                        </div>
                        <p class="sr-only" id="ai-input-help">输入需求后按 Ctrl+Enter 发送。生成完成后可切换 Plan 与 Build 并继续验证和应用。</p>
                        <div class="ai-attachments" id="ai-attachments"></div>
                        <div class="ai-manual-retry-banner" id="ai-manual-retry-banner"></div>
                        <div class="ai-followup-hint-banner" id="ai-followup-hint-banner"></div>
                        <div class="ai-quick-examples">
                            <button class="examples-header" id="examples-toggle" type="button" aria-expanded="true" aria-controls="ai-example-tags">
                                <span>快捷示例</span>
                                <svg class="examples-chevron" viewBox="0 0 24 24" width="14" height="14" fill="currentColor" aria-hidden="true"><path d="M7 10l5 5 5-5z"/></svg>
                            </button>
                            <div class="ai-example-tags" id="ai-example-tags">
                                <button class="ai-tag" type="button" data-text="读取产品上的DataMatrix二维码。">条码读取</button>
                                <button class="ai-tag" type="button" data-text="检测金属零件表面的划痕缺陷。先进行高斯滤波去噪，然后使用Canny边缘检测，最后通过Blob分析计算划痕面积。">缺陷检测</button>
                                <button class="ai-tag" type="button" data-text="测量两个圆形孔位的圆心距离。">孔距测量</button>
                                <button class="ai-tag" type="button" data-text="识别线束端子颜色顺序，并输出线序是否正确。">线序检测</button>
                            </div>
                        </div>
                    </div>
                </aside>

                <aside class="ai-pane-right" id="ai-result-pane" role="tabpanel" tabindex="0" data-ai-workbench-pane="true" data-ai-hook="workbench-pane">
                    <div class="ai-pane-header">
                        <span class="pane-icon ai-badge">AI</span>
                        <span class="pane-title">视觉智能体工作台</span>
                    </div>
                    <div class="ai-agent-workspace-overview" id="ai-agent-workspace-overview"></div>
                    <div class="ai-plan-workspace" id="ai-plan-workspace"></div>
                    ${renderAiBuildWorkspaceScaffold()}
                </aside>
                </div>
                <div class="sr-only" id="ai-accessibility-status" role="status" aria-live="polite" aria-atomic="true"></div>
            </div>
        `;

        // 事件绑定
        const attachBtn = this.container.querySelector('#ai-btn-attach');
        const cancelBtn = this.container.querySelector('#ai-btn-cancel');
        const inlineBuildBtn = this.container.querySelector('#ai-btn-start-build-inline');
        this.container.querySelector('#ai-btn-gen').addEventListener('click', this._handleGenerate);
        this.container.querySelector('#ai-btn-apply').addEventListener('click', this._handleApplyFlow);
        this._updateApplyButtonState();
        if (attachBtn) attachBtn.addEventListener('click', this._handleAttachmentClick);
        if (cancelBtn) cancelBtn.addEventListener('click', this._handleCancelGenerate);
        if (inlineBuildBtn) {
            inlineBuildBtn.addEventListener('click', event => this._startBuildFromCurrentPlan({
                acceptedRecommended: event.currentTarget?.dataset?.acceptRecommended === 'true'
            }));
        }
        const newSessionBtn = this.container.querySelector('#ai-btn-new-session');
        if (newSessionBtn) newSessionBtn.addEventListener('click', this._handleNewConversation);
        const historyBtn = this.container.querySelector('#ai-btn-history');
        if (historyBtn) historyBtn.addEventListener('click', this._toggleHistoryPanel);
        const historySearch = this.container.querySelector('#ai-history-search');
        if (historySearch) {
            historySearch.addEventListener('input', (event) => {
                this._filterHistory(event.target.value);
            });
        }
        this.container.querySelectorAll('[data-requirement-mode]').forEach(button => {
            button.addEventListener('click', () => {
                this._setRequirementMode(button.dataset.requirementMode || 'strict');
            });
        });
        this._bindAgentDeveloperControls();

        this.container.querySelectorAll('.ai-tag').forEach(tag => {
            tag.addEventListener('click', () => {
                this._handleQuickExampleSelection(tag.dataset.text);
            });
        });

        const aiInput = this.container.querySelector('#ai-input');
        aiInput.addEventListener('keydown', (e) => {
            if (e.ctrlKey && e.key === 'Enter') {
                this._handleGenerate();
            }
        });

        // 自动扩展高度
        aiInput.addEventListener('input', () => {
            aiInput.style.height = 'auto';
            aiInput.style.height = (aiInput.scrollHeight) + 'px';
        });

        initializeAiPanelShell(this);
        this._syncAccessibilitySemantics?.();

        this._renderAttachments();
        this._updateRequirementModeUI();
        this._renderQueuedHintBanner();
        this._renderRequirementBrief(null);
        this._renderFollowupChecklist(null);
        this._resetAgentWorkspace({ preservePlan: true });
    }

    _handleQuickExampleSelection(text = '') {
        const input = this.container.querySelector('#ai-input');
        if (!input || this.isGenerating) return false;

        input.value = String(text || '').trim();
        input.focus?.();
        input.style.height = 'auto';
        input.style.height = `${input.scrollHeight || 0}px`;
        return true;
    }

    _checkConnection() {
        if (this._disposed) return;
        const lifecycleEpoch = Number(this._lifecycleEpoch || 0);
        const indicator = this.container.querySelector('#ai-conn-status');
        const dot = indicator?.querySelector('.status-dot');
        if (!dot) return;

        httpClient.get('/health')
            .then(() => {
                if (this._disposed || lifecycleEpoch !== Number(this._lifecycleEpoch || 0) || dot.isConnected === false) return;
                dot.className = 'status-dot connected';
            })
            .catch(() => {
                if (this._disposed || lifecycleEpoch !== Number(this._lifecycleEpoch || 0) || dot.isConnected === false) return;
                dot.className = 'status-dot disconnected';
            });
    }

    _setupMessageListeners() {
        this._messageUnsubscribes.forEach(unsubscribe => unsubscribe?.());
        this._messageUnsubscribes = [
            webMessageBridge.on('GenerateFlowProgress', (data) => this._updateProgress(data)),
            webMessageBridge.on('GenerateFlowStreamChunk', (data) => this._handleStreamChunk(data)),
            webMessageBridge.on('AiFirewallBlocked', (data) => this._handleFirewallBlocked(data)),
            webMessageBridge.on('GenerateFlowResult', (data) => this._handleResult(data)),
            webMessageBridge.on('CancelGenerateFlowResult', (data) => this._handleCancelResult(data)),
            webMessageBridge.on('FilePickedEvent', this._handleFilePickedEvent),
            webMessageBridge.on('GenerateFlowAttachmentReport', this._handleAttachmentReport),
            webMessageBridge.on('ListAiSessionsResult', (data) => this._handleListAiSessionsResult(data)),
            webMessageBridge.on('GetAiSessionResult', (data) => this._handleGetAiSessionResult(data)),
            webMessageBridge.on('DeleteAiSessionResult', (data) => this._handleDeleteAiSessionResult(data))
        ];
    }

    _getCurrentFlowJson() {
        let baseFlow = null;
        if (this.currentResult && this.currentResult.flow && !this._isCurrentResultAppliedToCanvas()) {
            baseFlow = this.currentResult.flow;
        } else if (this.flowCanvas && typeof this.flowCanvas.serialize === 'function') {
            baseFlow = this.flowCanvas.serialize();
        } else if (this.currentResult && this.currentResult.flow) {
            baseFlow = this.currentResult.flow;
        }

        return this._buildFlowWithPendingDrafts(baseFlow);
    }

    _handleConfirmPendingParameters(data = this.currentResult, flow = null) {
        if (this.isGenerating) return;

        if (!this.currentResult?.flow) {
            this._addMessage('system', '当前没有可确认的方案，请先生成工程方案。');
            return;
        }

        const pending = this._resolveOrdinaryPendingParametersForDraft?.(data) ||
            this._resolvePendingParametersForDraft(data);
        if (pending.length === 0) {
            this._addMessage('system', '当前没有待确认参数，无需执行确认。');
            return;
        }

        const operators = this._getPendingOperatorSourceOperators(flow || data?.flow || data?.Flow || null);
        const groups = this._collectPendingDraftGroups(pending, operators);
        if (groups.length === 0) {
            this._addMessage('system', '当前模式下没有需要补录的互斥参数，无需执行确认。');
            return;
        }
        const confirmationState = this._getPendingParameterConfirmationState(pending, operators, groups);
        if (confirmationState.isConfirmed) {
            return;
        }
        if (!confirmationState.canConfirm) {
            this._addMessage('system', '请先填写全部待确认参数，再确认人工参数。');
            return;
        }

        this.pendingParameterConfirmedDraftSignature = this.pendingParameterDraftSignature;
        this.pendingParameterConfirmedValueSignature = confirmationState.valueSignature;
        this._updatePendingDraftSummary(data, flow);
        this._setResultStatusNote?.('人工参数已确认，可直接应用到画布；AI 复核为可选二次检查。', 'success');
    }

    _updateProgress(data) {
        if (typeof data !== 'string') {
            const payload = data?.payload || data || {};
            if (!this._shouldHandleGenerateRealtimePayload(payload)) {
                return;
            }
        }

        const phase = typeof data === 'string' ? '' : (data.payload?.phase || data.phase || '');

        // Map progress phases to workbench states
        if (phase === 'validating' || phase === 'validator') {
            this._setWorkbenchState(AiWorkbenchStates.VALIDATING);
        } else if (phase === 'layouting' || phase === 'dryrun') {
            this._setWorkbenchState(AiWorkbenchStates.DRY_RUNNING);
        } else if (phase === 'matching_template' || phase === 'scenario_match' || phase === 'prompt_context') {
            this._setWorkbenchState(AiWorkbenchStates.MATCHING_TEMPLATE);
        } else if (phase === 'parsing') {
            this._setWorkbenchState(AiWorkbenchStates.PARSING);
        } else if (phase === 'clarification') {
            this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
        } else if (phase === 'calling_ai' || phase === 'connecting') {
            this._setWorkbenchState(AiWorkbenchStates.GENERATING);
        }

        // Progress messages drive the workbench state, but are intentionally
        // not rendered into each chat turn to keep the conversation focused.
    }

    _showPhaseHint() {}

    _handleStreamChunk(data) {
        const payload = data.payload || data;
        if (!this._shouldHandleGenerateRealtimePayload(payload)) {
            return;
        }

        const chunkType = payload.chunkType; // 'content' or legacy hidden thinking
        const content = payload.content || '';

        if (!content) return;

        if (chunkType === 'thinking') {
            return;
        } else if (chunkType === 'content') {
            this._streamBuffer.content += content;
        } else {
            return;
        }

        if (!this._streamFlushPending) {
            this._streamFlushPending = true;
            this._requestOwnedAnimationFrame?.(() => this._flushStreamBuffer());
        }
    }

    _flushStreamBuffer() {
        this._streamFlushPending = false;
        const replyText = this._streamBuffer?.content || '';

        this._streamBuffer.thinking = '';
        this._streamBuffer.content = '';

        if (replyText) {
            this._appendAssistantStreamText('reply', replyText);
        }

        if ((this._streamBuffer.thinking || this._streamBuffer.content) && !this._streamFlushPending) {
            this._streamFlushPending = true;
            this._requestOwnedAnimationFrame?.(() => this._flushStreamBuffer());
        }
    }

    _appendStreamText() {}

    _isNearBottom(targetEl, threshold = 24) {
        if (!targetEl) return false;
        return (targetEl.scrollHeight - targetEl.scrollTop - targetEl.clientHeight) <= threshold;
    }

    _normalizeIntent(intent) {
        if (!intent) return 'UNKNOWN';
        return intent.toUpperCase();
    }

    _getIntentLabel(intent) {
        switch(intent) {
            case 'NEW': return '全新生成';
            case 'MODIFY': return '增量修改';
            case 'EXPLAIN': return '解释说明';
            default: return '智能回复';
        }
    }

    _handleResult(data) {
        if (this._streamFlushPending) {
            this._flushStreamBuffer();
        }

        const rawPayload = data.payload || data;
        const payload = this._normalizeRuntimePayload(rawPayload) || rawPayload;
        if (!this._shouldHandleGenerateTerminalPayload(payload)) {
            return;
        }

        const isCancelled = this._isCancelledResult(payload);
        this.isCancellingGenerate = false;
        this._setGeneratingState(false);
        this._adoptCanonicalSessionId?.(payload.sessionId || this.sessionId, { reason: 'generate_result' });
        const activeTurn = this.activeAssistantTurn
            || this._startAssistantTurn({ activate: false, statusText: '处理中', statusTone: 'streaming' });
        const isClarification = this._isClarificationResult(payload);
        const isInteractionOnly = this._isInteractionOnlyResult(payload);
        const appliedBuildFromPlanCanonical = this._applyBuildFromPlanCanonicalState?.(payload) === true;
        this._renderAgentRuntime(payload);

        if (isCancelled) {
            this._clearActiveRequestState();
            this._setWorkbenchState(AiWorkbenchStates.CANCELLED);
            this._setAssistantTurnStatus(activeTurn, '已取消', 'cancelled');
            this._setResultStatusNote('', '');
            this.activeAssistantTurn = null;
            return;
        }

        if (isInteractionOnly) {
            this._clearActiveRequestState();
            if (!this.currentResult?.flow) {
                this._setWorkbenchState(AiWorkbenchStates.IDLE);
            }
            this.pendingManualRetry = null;
            this._renderManualRetryBanner();
            this._setAssistantTurnStatus(activeTurn, '已回复', 'success');
            this._setAssistantSectionText(
                activeTurn,
                'reply',
                payload.aiExplanation || payload.AiExplanation || payload.errorMessage || payload.message || '我在。'
            );
            if (activeTurn.clarificationSection) {
                activeTurn.clarificationSection.hidden = true;
            }
            this._setResultStatusNote('', '');

            if (this.sessionId) {
                this._addToHistory({
                    sessionId: this.sessionId,
                    lastMessage: this.lastUserPrompt || payload.aiExplanation || '普通对话',
                    updatedAtUtc: new Date().toISOString(),
                    turnCount: 0
                });
            }

            this.activeAssistantTurn = null;
            return;
        }

        if (isClarification) {
            const isVisionAgentMode = this.useVisionAgentGenerateFlow === true;
            this.pendingClarificationPayload = isVisionAgentMode ? null : payload;
            const shouldResetPendingPlan = (payload.shouldResetPendingPlan ?? payload.ShouldResetPendingPlan) === true ||
                (payload.resetPendingPlan ?? payload.ResetPendingPlan) === true;
            if (!isVisionAgentMode && shouldResetPendingPlan) {
                this.pendingVisionPlan = null;
                this._clearPlanQuestionAnswers?.();
            }
            if (!isVisionAgentMode) {
            }
            this.agentWorkspaceMode = AgentWorkspaceModes.PLAN;
            this._setWorkspaceViewMode?.(AgentWorkspaceModes.PLAN, { render: false });
            this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
        } else {
            this.pendingClarificationPayload = null;
            this.agentWorkspaceMode = AgentWorkspaceModes.BUILD;
            this._setWorkspaceViewMode?.(AgentWorkspaceModes.BUILD, { render: false });
        }
        this._renderAgentWorkspaceOverview();
        this._renderPlanWorkspace(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun();
        if (!isClarification) {
            this._renderRequirementBrief(payload);
        }

        if (!payload.success) {
            this._clearActiveRequestState();
            if (isClarification) {
                const isVisionAgentMode = this.useVisionAgentGenerateFlow === true;
                this.pendingManualRetry = null;
                this._renderManualRetryBanner();
                this._setAssistantTurnStatus(activeTurn, isVisionAgentMode ? '已回复' : '待澄清', isVisionAgentMode ? 'success' : 'warning');
                this._setAssistantSectionText(
                    activeTurn,
                    'reply',
                    payload.aiExplanation || payload.AiExplanation || payload.errorMessage || payload.message || '当前需求需要先澄清。'
                );
                if (!isVisionAgentMode) {
                    this._renderAssistantClarification(activeTurn, payload);
                } else if (activeTurn.clarificationSection) {
                    activeTurn.clarificationSection.hidden = true;
                }
                if (!this.currentResult?.flow) {
                    const summary = this.container.querySelector('#ai-result-summary');
                    if (summary) {
                        summary.textContent = this._sanitizeAssistantFailureText?.(payload.aiExplanation || payload.AiExplanation || '当前需求需要先澄清。', 360) || '当前需求需要先澄清。';
                    }
                }
                if (this.currentResult?.flow) {
                    this._setResultStatusNote('本轮生成前需要先补充需求，右侧仍保留上一版可应用方案。', 'warning');
                } else if (!isVisionAgentMode) {
                    this._setResultStatusNote('当前需求还需要澄清，右侧已整理问题清单。', 'info');
                } else {
                    this._setResultStatusNote('', '');
                }
                const persistenceWarning = this._getPersistenceWarning?.(payload);
                if (persistenceWarning) {
                    this._setResultStatusNote(persistenceWarning.message || '结果已生成，但本次会话尚未成功保存。', 'warning');
                }

                if (this.sessionId) {
                    this._addToHistory({
                        sessionId: this.sessionId,
                        lastMessage: this.lastUserPrompt || payload.aiExplanation || '需求待澄清',
                        updatedAtUtc: new Date().toISOString(),
                        turnCount: 0
                    });
                }

                this.activeAssistantTurn = null;
                return;
            }

            const manualRetry = payload.manualRetry || payload.ManualRetry || null;
            this._setWorkbenchState(AiWorkbenchStates.FAILED);
            if (manualRetry?.required) {
                this.pendingManualRetry = {
                    ...manualRetry,
                    originalMessage: this.lastUserPrompt
                };
                this._setAssistantTurnStatus(activeTurn, '待手动确认', 'warning');
                this._renderAssistantFailure(activeTurn, payload);
                this._appendManualRetryDraftToInput(this.pendingManualRetry);
                this._renderManualRetryBanner();
            } else {
                this.pendingManualRetry = null;
                this._renderManualRetryBanner();
                this._setAssistantTurnStatus(activeTurn, '生成失败', 'failed');
                this._renderAssistantFailure(activeTurn, payload);
            }

            if (this.currentResult?.flow) {
                this._setResultStatusNote('本轮修改失败，右侧仍显示上一版可应用方案。', 'warning');
            } else {
                this._setResultStatusNote('', '');
            }
            const persistenceWarning = this._getPersistenceWarning?.(payload);
            if (persistenceWarning) {
                this._setResultStatusNote(persistenceWarning.message || '结果已生成，但本次会话尚未成功保存。', 'warning');
            }

            this.activeAssistantTurn = null;
            return;
        }

        this._clearActiveRequestState();
        this._setCurrentResult(payload);
        this._resetPendingDraftState();
        this.pendingManualRetry = null;
        this._renderManualRetryBanner();
        this._rebuildPendingOperatorBindings({
            pending: this._resolvePendingParametersForDraft(payload),
            flow: payload?.flow ?? payload?.Flow ?? null,
            preferIndexFallback: true
        });

        // Cache stage timeline and model capabilities for new cards
        this._workbenchStageTimeline = payload.stageTimeline || payload.StageTimeline || [];
        const promptTrace = payload.promptTrace || payload.PromptTrace || null;
        if (promptTrace?.capabilities) {
            this._lastModelSupportsVision = promptTrace.capabilities.supportsVisionInput ?? null;
        }

        // Set workbench state based on pending parameters
        const hasPending = (payload.pendingParameters || payload.PendingParameters || []).length > 0;
        const interactionState = this._getInteractionState(payload);
        this._setWorkbenchState(
            interactionState === 'reviewing_parameters' || hasPending
                ? AiWorkbenchStates.REVIEWING_PARAMETERS
                : AiWorkbenchStates.READY_TO_APPLY
        );

        if (this.sessionId) {
            this._addToHistory({
                sessionId: this.sessionId,
                lastMessage: this.lastUserPrompt || payload.aiExplanation || '已生成流程',
                updatedAtUtc: new Date().toISOString(),
                turnCount: 0,
                scenarioKey: payload.requirementBrief?.scenarioKey || payload.RequirementBrief?.ScenarioKey || '',
                templateName: payload.recommendedTemplate?.templateName || payload.RecommendedTemplate?.TemplateName || '',
                generationMode: payload.generationMode || payload.GenerationMode || '',
                applied: false
            });
        }
        const turnIntent = this._getTurnIntent(payload);
        this._setAssistantTurnStatus(activeTurn, turnIntent === 'modify_flow' ? '微调完成' : '生成成功', 'success');
        const persistenceWarning = this._getPersistenceWarning?.(payload);
        if (persistenceWarning) {
            this._setResultStatusNote(persistenceWarning.message || '结果已生成，但本次会话尚未成功保存。', 'warning');
        } else {
            this._setResultStatusNote(turnIntent === 'modify_flow' ? '已基于当前工程完成微调。' : '', turnIntent === 'modify_flow' ? 'info' : '');
        }
        this._displayResult(payload, {
            appendChatMessage: false,
            assistantTurn: activeTurn
        });
        this.activeAssistantTurn = null;
    }

    _handleCancelResult(data) {
        const payload = data?.payload || data || {};
        if (!this._shouldHandleGenerateRealtimePayload(payload)) {
            return;
        }

        const status = this._normalizeGenerateStatus(payload);
        if (status === 'cancelled' || status === 'canceled') {
            this._addMessage('system', this._sanitizeAssistantFailureText?.(payload.message || '已发送取消请求，正在等待后端停止当前生成。', 220) || '已发送取消请求，正在等待后端停止当前生成。');
            return;
        }

        this.isCancellingGenerate = false;
        this._addMessage('system', `取消生成未生效: ${this._sanitizeAssistantFailureText?.(payload.errorMessage || payload.message || '未知错误', 220) || '未知错误'}`);
    }

    _handleFirewallBlocked(data) {
        this._setGeneratingState(false);
        this._clearActiveRequestState();
        const payload = data?.payload || {};
        const title = this._sanitizeAssistantFailureText?.(payload.message || '网络连接被拦截', 160) || '网络连接被拦截';
        const detail = this._sanitizeAssistantFailureText?.(payload.detail || '请检查防火墙设置或网络代理。', 260) || '请检查防火墙设置或网络代理。';
        if (this.activeAssistantTurn) {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '连接受阻', 'failed');
            this._renderAssistantFailure(this.activeAssistantTurn, {
                errorMessage: title
            });
            this.activeAssistantTurn = null;
        }
        const chatContainer = this.container.querySelector('#ai-chat-container');
        if (!chatContainer) return;
        const alert = document.createElement('div');
        alert.className = 'firewall-alert';
        const part = (className, text = '') => Object.assign(document.createElement('div'), { className, textContent: text });
        const content = part('firewall-content');
        content.appendChild(part('firewall-title', title));
        content.appendChild(part('firewall-desc', detail));
        alert.appendChild(part('firewall-icon', '!'));
        alert.appendChild(content);
        chatContainer.appendChild(alert);
        this._scrollToBottom();
    }

    _handleError(msg) {
        this._setGeneratingState(false);
        this._clearActiveRequestState();
        if (this.activeAssistantTurn) {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '系统错误', 'failed');
            this._renderAssistantFailure(this.activeAssistantTurn, {
                errorMessage: `系统错误: ${this._sanitizeAssistantFailureText?.(msg, 260) || '未知错误'}`
            });
            if (this.currentResult?.flow) {
                this._setResultStatusNote('本轮修改失败，右侧仍显示上一版可应用方案。', 'warning');
            }
            this.activeAssistantTurn = null;
            return;
        }
        this._addMessage('system', `❌ 系统错误: ${this._sanitizeAssistantFailureText?.(msg, 260) || '未知错误'}`);
    }

    _displayResult(data, options = {}) {
        const {
            appendChatMessage = false,
            assistantTurn = this.activeAssistantTurn
        } = options;

        if (assistantTurn) {
            if (!assistantTurn.replyBody?.textContent?.trim()) {
                this._setAssistantSectionText(
                    assistantTurn,
                    'reply',
                    data.aiExplanation || data.AiExplanation || '已生成工程方案。'
                );
            }

            this._renderPublicDiagnosticsSection(assistantTurn, data);
        }

        const flow = data?.flow || data?.Flow || null;
        const ops = this._extractOperators(flow);
        const connections = this._extractConnections(flow);
        const buildFlowCompatibility = this._getBuildArtifactFlowCompatibilityState?.(data);
        const legacyMissingCanonicalFlow = buildFlowCompatibility?.status === 'legacy_build_artifact_missing_canonical_flow';
        const nonCompletedBuildTerminalWithoutFlow = !flow && [
            'terminal_failed_without_flow',
            'terminal_cancelled_without_flow',
            'terminal_clarification_without_flow'
        ].includes(buildFlowCompatibility?.status);
        this._syncPendingParameterDrafts(data, flow);
        this._renderAgentRuntime(data);
        this._renderRequirementBrief(data);

        const clarificationRequired = this._isClarificationResult(data);
        const requirementBrief = this._normalizeRequirementBrief(data?.requirementBrief ?? data?.RequirementBrief ?? null);
        const failureSummary = data?.failureSummary || data?.FailureSummary || null;
        const terminalSummary = this._sanitizeAssistantFailureText?.(String(
            failureSummary?.message ||
            failureSummary?.Message ||
            data?.errorMessage ||
            data?.ErrorMessage ||
            data?.aiExplanation ||
            data?.AiExplanation ||
            data?.message ||
            data?.Message ||
            '构建未返回可应用画布流程，已保留原始终态。'
        ).trim(), 360) || '构建未返回可应用画布流程，已保留原始终态。';
        const compatibilityMessage = this._sanitizeAssistantFailureText?.(buildFlowCompatibility?.publicMessage || '', 360) ||
            buildFlowCompatibility?.publicMessage || '';
        const summaryLines = legacyMissingCanonicalFlow
            ? [
                this._escapeHtml(compatibilityMessage).replace(/\n/g, '<br/>')
            ]
            : nonCompletedBuildTerminalWithoutFlow
            ? [
                this._escapeHtml(terminalSummary)
            ]
            : clarificationRequired && !flow
            ? [
                `当前需求还需要澄清，已整理 <span class="result-count">${(requirementBrief?.clarificationQuestions?.length || requirementBrief?.missingFacts?.length || 0)}</span> 个问题。`
            ]
            : [
                `该方案包含 <span class="result-count">${ops.length}</span> 个算子和 <span class="result-count">${connections.length}</span> 条连线。`
            ];
        const templateSummary = legacyMissingCanonicalFlow ? '' : this._buildTemplateFirstSummary(data);
        if (templateSummary) {
            summaryLines.push(templateSummary);
        }
        this.container.querySelector('#ai-result-summary').innerHTML = summaryLines.join('<br/>');

        // 算子列表逐个淡入（增强：显示工程角色、资源状态、待确认参数）
        const opsContainer = this.container.querySelector('#ai-result-ops');
        opsContainer.innerHTML = '';
        const pendingSet = new Set(
            this._resolvePendingParametersForDraft(data)
                .flatMap(p => [p.operatorId, p.actualOperatorId])
                .filter(Boolean)
        );
        const normalizedMissingResources = this._normalizeMissingResources(data?.missingResources || data?.MissingResources || []);
        const missingResourceOps = new Set(
            normalizedMissingResources
                .flatMap(r => [r.operatorId, r.actualOperatorId, r.description])
                .map(value => String(value || '').toLowerCase())
                .filter(Boolean)
        );
        if (clarificationRequired && !flow) {
            opsContainer.innerHTML = '<div class="ai-followup-empty">构建尚未开始。请先确认计划或开始构建，生成算子链后会显示在这里。</div>';
        } else if (legacyMissingCanonicalFlow) {
            opsContainer.innerHTML = `<div class="ai-followup-empty">${this._escapeHtml(compatibilityMessage).replace(/\n/g, '<br/>')}</div>`;
        } else if (nonCompletedBuildTerminalWithoutFlow) {
            opsContainer.innerHTML = `<div class="ai-followup-empty">${this._escapeHtml(terminalSummary)}</div>`;
        } else {
            ops.forEach((op, i) => {
                const rawOpName = op?.displayName || op?.DisplayName || op?.name || op?.Name || '未命名算子';
                const opName = this._sanitizeAssistantFailureText?.(rawOpName, 120) || '未命名算子';
                const opType = op?.operatorType || op?.OperatorType || op?.type || op?.Type || '';
                const opTypeDisplay = getOperatorTypeDisplayName(opType);
                const opId = op?.tempId || op?.TempId || op?.id || op?.Id || '';
                const hasPending = pendingSet.has(opId);
                const hasMissing = (op.parameters || op.Parameters || {})['ModelPath'] === ''
                    || missingResourceOps.has(opId.toLowerCase())
                    || missingResourceOps.has(opName.toLowerCase());
                const statusBadges = [];
                if (hasPending) statusBadges.push('<span class="op-badge op-badge-pending">待确认</span>');
                if (hasMissing) statusBadges.push('<span class="op-badge op-badge-missing">缺资源</span>');

                const item = document.createElement('div');
                item.className = 'generated-op-item';
                item.style.opacity = '0';
                item.style.transform = 'translateX(12px)';
                item.innerHTML = `
                    <div class="op-dot"></div>
                    <div class="op-main">
                        <div class="op-name">${this._escapeHtml(String(opName))}</div>
                        ${opTypeDisplay ? `<div class="op-type-badge">${this._escapeHtml(opTypeDisplay)}</div>` : ''}
                    </div>
                    ${statusBadges.length > 0 ? `<div class="op-badges">${statusBadges.join('')}</div>` : ''}
                `;
                opsContainer.appendChild(item);
                this._setOwnedTimeout?.(() => {
                    item.style.transition = 'all 0.3s var(--ease-ink-smooth)';
                    item.style.opacity = '1';
                    item.style.transform = 'translateX(0)';
                }, 80 * i);
            });
        }

        const matchedTemplateName = data?.recommendedTemplate?.templateName || '';
        const templateNotice = matchedTemplateName ? ` 已按模板优先命中「${matchedTemplateName}」。` : '';
        this._renderFollowupChecklist(data, flow);
        this._renderParameterDraftEditor(data, flow);
        this._renderStageTimeline(data?.stageTimeline || data?.StageTimeline || this._workbenchStageTimeline || []);
        this._renderValidationConsole(data);
        this._renderAttachmentPanel();
        this._renderPromptTrace(data?.promptTrace ?? data?.PromptTrace ?? null);
        this._renderBuildPresentation?.();
        if (appendChatMessage) {
            this._addMessage('ai', `工程方案已生成！包含 ${ops.length} 个算子、${connections.length} 条连线。${templateNotice}可继续输入修改指令。`);
        }
    }

    _renderPromptTrace(trace) {
        const card = this.container?.querySelector('#ai-result-prompt-trace-card');
        const container = this.container?.querySelector('#ai-result-prompt-trace');
        const toggleBtn = this.container?.querySelector('#ai-trace-toggle');
        if (!card || !container) return;

        if (!trace || typeof trace !== 'object') {
            card.hidden = true;
            container.innerHTML = '';
            return;
        }

        const isDebugMode = new URLSearchParams(window.location.search).has('debugPrompt')
            || localStorage.getItem('cv_ai_debug_prompt') === '1';

        // Store trace for toggle
        this._currentPromptTrace = trace;
        this._promptTraceViewMode = this._promptTraceViewMode || (isDebugMode ? 'debug' : 'engineering');

        if (toggleBtn) {
            toggleBtn.textContent = this._promptTraceViewMode === 'debug' ? '工程视图' : '调试视图';
            toggleBtn.onclick = () => {
                this._promptTraceViewMode = this._promptTraceViewMode === 'debug' ? 'engineering' : 'debug';
                this._renderPromptTrace(this._currentPromptTrace);
            };
        }

        const mode = String(trace.mode || '').trim();
        const provider = String(trace.provider || '').trim();
        const model = String(trace.model || '').trim();
        const baseUrl = this._sanitizePromptTracePublicText(trace.baseUrl || '');

        card.hidden = false;

        if (this._promptTraceViewMode === 'engineering') {
            // Engineering view: show summary only
            const stageTimeline = this._workbenchStageTimeline || [];
            const totalMs = stageTimeline.reduce((sum, s) => sum + (s.durationMs || 0), 0);
            container.innerHTML = `
                <div class="ai-trace-engineering">
                    <div class="ai-trace-eng-row"><span class="ai-trace-eng-label">模型</span><span>${this._escapeHtml(model || '--')}</span></div>
                    <div class="ai-trace-eng-row"><span class="ai-trace-eng-label">提供商</span><span>${this._escapeHtml(provider || '--')}</span></div>
                    <div class="ai-trace-eng-row"><span class="ai-trace-eng-label">模式</span><span>${this._escapeHtml(mode || '--')}</span></div>
                    ${totalMs > 0 ? `<div class="ai-trace-eng-row"><span class="ai-trace-eng-label">总耗时</span><span>${(totalMs / 1000).toFixed(1)}s</span></div>` : ''}
                    ${stageTimeline.length > 0 ? `
                        <div class="ai-trace-eng-stages">
                            ${stageTimeline.map(s => {
                                const label = STAGE_DIAGNOSTIC_LABELS[s.stage] || s.stage;
                                const status = s.status === 'failed' ? '&#10007;' : '&#10003;';
                                const cls = s.status === 'failed' ? 'is-failed' : 'is-ok';
                                return `<span class="ai-trace-eng-stage ${cls}">${status} ${this._escapeHtml(label)} ${s.durationMs != null ? s.durationMs + 'ms' : '--'}</span>`;
                            }).join('')}
                        </div>
                    ` : ''}
                </div>
            `;
            return;
        }

        // Debug view: public diagnostics only. Raw/system prompts stay hidden even when debugPrompt is enabled.
        const capabilities = this._formatPromptTraceJson(trace.capabilities || null);
        const attachmentReport = this._formatPromptTraceJson(trace.attachmentReport || null);
        const referenceFlow = this._sanitizePromptTracePublicText(trace.usedReferenceFlowSummary || '');
        const systemPrompt = String(trace.systemPrompt || '').trim();
        const userPrompt = String(trace.userPrompt || '').trim();

        container.innerHTML = `
            <details class="ai-prompt-trace-details" open>
                <summary>本次模型调用的公开诊断上下文</summary>
                <div class="ai-prompt-trace-grid">
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">元信息</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml([
                            `mode=${mode || '--'}`,
                            `provider=${provider || '--'}`,
                            `model=${model || '--'}`,
                            `baseUrl=${baseUrl || '--'}`
                        ].join('\n'))}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">模型能力</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(capabilities)}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">附件报告</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(attachmentReport)}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">参考流程摘要</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(referenceFlow || '--')}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">系统提示状态</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(systemPrompt ? `已隐藏（${systemPrompt.length} 字符）` : '--')}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">用户提示状态</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(userPrompt ? `已隐藏（${userPrompt.length} 字符）` : '--')}</pre>
                    </div>
                </div>
            </details>
        `;
    }

    _formatPromptTraceJson(value) {
        if (value === null || value === undefined || value === '') {
            return '--';
        }

        try {
            return this._sanitizePromptTracePublicText(JSON.stringify(value, null, 2));
        } catch {
            return this._sanitizePromptTracePublicText(String(value));
        }
    }

    _sanitizePromptTracePublicText(value) {
        return String(value || '')
            .replace(/["']?(?:authorization|x-api-key|api[-_ ]?key|token|secret|baseUrl|base_url|headers?)["']?\s*:\s*["'][^"']+["']/gi, '"[已隐藏字段]": "[已隐藏]"')
            .replace(/\bBearer\s+[A-Za-z0-9._~+/=-]{8,}/gi, 'Bearer [已隐藏]')
            .replace(/\b(?:authorization|x-api-key|api[-_ ]?key)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '[已隐藏字段]: [已隐藏]')
            .replace(/\b(?:token|secret|baseUrl|base_url|headers?)\b\s*[:=]\s*["']?[^"'\s,;}]+/gi, '[已隐藏字段]: [已隐藏]')
            .replace(/https?:\/\/[^\s"'<>|]+/gi, '[已隐藏URL]')
            .replace(/\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b/g, '[已隐藏IP]')
            .replace(/\bDB\d+\.DB[XBWD]\d+(?:\.\d+)?\b/gi, '[已隐藏PLC]')
            .replace(/\bM\d+(?:\.\d+)?\b/gi, '[已隐藏PLC]')
            .replace(/\bD\d+\b/gi, '[已隐藏PLC]')
            .replace(/plc:\/\/[^\s"'<>|]+/gi, '[已隐藏PLC]')
            .replace(/(?:[a-z]:\\|\\\\)[^\s"'<>|]+/gi, '[已隐藏路径]')
            .replace(/(?:\/users\/|\/home\/|\/var\/|\/tmp\/|\/mnt\/|\/data\/|\/models\/|\/artifacts\/)[^\s"'<>|]+/gi, '[已隐藏路径]')
            .replace(/data:image\/[a-z0-9.+-]+;base64,[a-z0-9+/=\r\n]+/gi, '[已隐藏图像]')
            .replace(/(?<![a-z0-9+/=])(?:[a-z0-9+/]{96,}={0,2})(?![a-z0-9+/=])/gi, '[已隐藏编码]')
            .trim();
    }

    _buildTemplateFirstSummary(data) {
        const recommended = data?.recommendedTemplate || null;
        const pending = Array.isArray(data?.pendingParameters) ? data.pendingParameters : [];
        const missing = Array.isArray(data?.missingResources) ? data.missingResources : [];

        if (!recommended && pending.length === 0 && missing.length === 0) {
            return '';
        }

        const parts = [];
        if (recommended && recommended.templateName) {
            const templateName = this._escapeHtml(this._sanitizeAssistantFailureText?.(recommended.templateName, 160) || String(recommended.templateName));
            const reason = this._escapeHtml(this._sanitizeAssistantFailureText?.(recommended.matchReason || '命中高频场景', 220) || String(recommended.matchReason || '命中高频场景'));
            const confidence = Number(recommended.confidence);
            const confidenceText = Number.isFinite(confidence) && confidence > 0
                ? `，置信度 ${(confidence * 100).toFixed(0)}%`
                : '';
            parts.push(`模板优先：<span class="result-count">${templateName}</span>（${reason}${confidenceText}）`);
        }

        if (pending.length > 0) {
            parts.push(`待确认参数：<span class="result-count">${pending.length}</span> 组`);
        }

        if (missing.length > 0) {
            const missingPreview = missing
                .slice(0, 2)
                .map(item => this._escapeHtml(this._sanitizeAssistantFailureText?.(item?.resourceKey || item?.description || '未知资源', 160) || String(item?.resourceKey || item?.description || '未知资源')))
                .join('、');
            const suffix = missing.length > 2 ? '...' : '';
            parts.push(`缺失资源：<span class="result-count">${missing.length}</span> 项（${missingPreview}${suffix}）`);
        }

        return parts.join('；');
    }

    _renderFollowupChecklist(data, flow = null) {
        const container = this.container?.querySelector('#ai-result-followups');
        if (!container) return;

        const partition = this._getPendingParameterPartition(data);
        const pending = partition.ordinaryPendingParameters;
        const missing = partition.resources;
        const recommended = this._normalizeRecommendedTemplate(data?.recommendedTemplate ?? data?.RecommendedTemplate);
        const candidates = this._normalizeTemplateCandidates(data?.templateCandidates ?? data?.TemplateCandidates);
        const requirementBrief = this._normalizeRequirementBrief(data?.requirementBrief ?? data?.RequirementBrief ?? null);
        const nonBlockingFields = requirementBrief?.nonBlockingMissingFields || [];
        const generationMode = this._getGenerationMode(data);
        const templateLockLevel = this._getTemplateLockLevel(data);
        const operators = this._getPendingOperatorSourceOperators(flow || data?.flow || data?.Flow || null);
        const pendingGroups = this._collectPendingDraftGroups(pending, operators).map(group => ({ ...group, groupKey: this._sanitizeResourceAuditDisplayText?.(group.groupKey, 160) || group.groupKey, label: this._sanitizeResourceAuditDisplayText?.(group.label, 180) || group.label, operatorId: this._sanitizeResourceAuditDisplayText?.(group.operatorId, 120) || group.operatorId, fields: (group.fields || []).map(field => ({ ...field, parameterName: this._sanitizeResourceAuditDisplayText?.(field.parameterName, 120) || field.parameterName })) }));
        const displayPendingGroups = pendingGroups;
        const effectivePending = displayPendingGroups.map(group => ({
            operatorId: group.operatorId,
            parameterNames: group.fields.map(field => field.parameterName)
        }));
        const hasTemplateStrategy = Boolean(recommended || candidates.length > 0 || generationMode || templateLockLevel);
        const manualRecords = this._getManualResourceConfirmationRecords?.(data) || [];
        const canvasManualEditRecords = this._getCanvasManualEditRecords?.(data) || [];
        if (!hasTemplateStrategy && pendingGroups.length === 0 && missing.length === 0 && nonBlockingFields.length === 0 && manualRecords.length === 0 && canvasManualEditRecords.length === 0) {
            container.classList.add('is-empty');
            container.innerHTML = '<div class="ai-followup-empty">当前没有待确认参数或缺失资源。</div>';
            return;
        }

        const followupText = this._buildFollowupHintText({ recommended, pending: effectivePending, missing, operators, nonBlockingFields });
        const safeTemplateText = (value, maxChars = 180) => this._sanitizeAssistantFailureText?.(value, maxChars) || String(value ?? '').trim().slice(0, maxChars);
        const strategyText = this._formatTemplateStrategy(generationMode, templateLockLevel);
        const primaryTemplate = recommended || candidates[0] || null;
        const candidatesHtml = candidates.length > 0
            ? `
                <div class="ai-followup-template-candidates">
                    ${candidates.map((candidate, index) => {
                        const confidence = Number.isFinite(candidate.confidence) && candidate.confidence > 0
                            ? ` · ${(candidate.confidence * 100).toFixed(0)}%`
                            : '';
                        const meta = [safeTemplateText(candidate.scenarioKey, 120), safeTemplateText(candidate.industry, 80), candidate.templateVersion ? `v${safeTemplateText(candidate.templateVersion, 60)}` : '']
                            .filter(Boolean)
                            .join(' · ');
                        return `
                            <div class="ai-followup-template-candidate">
                                <div class="ai-followup-template-candidate-main">
                                    <div class="ai-followup-template-name">${this._escapeHtml(safeTemplateText(candidate.templateName, 160))}</div>
                                    <div class="ai-followup-template-reason">${this._escapeHtml(safeTemplateText(candidate.matchReason || `候选 ${index + 1}`, 220))}${this._escapeHtml(confidence)}</div>
                                    ${meta ? `<div class="ai-followup-item-meta">${this._escapeHtml(meta)}</div>` : ''}
                                </div>
                                <div class="ai-followup-template-actions">
                                    <button class="ai-followup-template-action" type="button" data-template-action="fill" data-template-id="${this._escapeHtml(candidate.templateId)}" data-scenario-key="${this._escapeHtml(candidate.scenarioKey)}" data-template-name="${this._escapeHtml(safeTemplateText(candidate.templateName, 160))}">严格沿用</button>
                                    <button class="ai-followup-template-action" type="button" data-template-action="adapt" data-template-id="${this._escapeHtml(candidate.templateId)}" data-scenario-key="${this._escapeHtml(candidate.scenarioKey)}" data-template-name="${this._escapeHtml(safeTemplateText(candidate.templateName, 160))}">参考改造</button>
                                </div>
                            </div>
                        `;
                    }).join('')}
                </div>
            `
            : '';
        const topologySummary = this._extractTopologySummary(flow || data?.flow || data?.Flow || null);
        const recommendedHtml = hasTemplateStrategy
            ? `
                <div class="ai-followup-template">
                    <div class="ai-followup-section-header">
                        <div class="ai-followup-section-label">模板策略</div>
                        <div class="ai-followup-section-tip">${this._escapeHtml(strategyText)}</div>
                    </div>
                    ${primaryTemplate ? `
                        <div class="ai-followup-template-name">${this._escapeHtml(safeTemplateText(primaryTemplate.templateName, 160))}</div>
                        <div class="ai-followup-template-reason">${this._escapeHtml(safeTemplateText(primaryTemplate.matchReason || '建议延续当前模板骨架继续补齐缺失项。', 220))}</div>
                        ${primaryTemplate.matchMode ? `<div class="ai-followup-item-meta">匹配模式：${this._escapeHtml(safeTemplateText(primaryTemplate.matchMode, 120))}</div>` : ''}
                        ${primaryTemplate.matchedFields && primaryTemplate.matchedFields.length > 0 ? `<div class="ai-followup-item-meta">匹配字段：${this._escapeHtml(primaryTemplate.matchedFields.map(field => safeTemplateText(field, 80)).join('、'))}</div>` : ''}
                        ${primaryTemplate.missingSignals && primaryTemplate.missingSignals.length > 0 ? `<div class="ai-followup-item-meta">缺失信号：${this._escapeHtml(primaryTemplate.missingSignals.map(signal => safeTemplateText(signal, 80)).join('、'))}</div>` : ''}
                    ` : `
                        <div class="ai-followup-template-reason">本轮按自由生成处理，可在候选出现后手动指定模板。</div>
                    `}
                    ${topologySummary ? `<div class="ai-followup-template-topology">${this._escapeHtml(topologySummary)}</div>` : ''}
                    ${candidatesHtml}
                    <div class="ai-followup-template-free-row">
                        <button class="ai-followup-template-action" type="button" data-template-action="free">下一轮不用模板</button>
                    </div>
                </div>
            `
            : '';

        const missingHtml = missing.length > 0
            ? `
                <div class="ai-followup-section">
                    <div class="ai-followup-section-header">
                        <div class="ai-followup-section-label">待绑定资源</div>
                        <div class="ai-followup-section-tip">复用现有资源绑定与门禁刷新链路</div>
                    </div>
                    <div class="ai-followup-list">
                        ${missing.map((item, index) => this._renderResourceAuditTaskCard(
                            item,
                            this._getMissingResourceActionModel(item),
                            index
                        )).join('')}
                    </div>
                </div>
            `
            : '';

        const nonBlockingHtml = nonBlockingFields.length > 0
            ? `
                <div class="ai-followup-section ai-followup-section-nonblocking">
                    <div class="ai-followup-section-header">
                        <div class="ai-followup-section-label">非阻断待补</div>
                        <div class="ai-followup-section-tip">不阻塞初稿，可在应用前后补齐</div>
                    </div>
                    <div class="ai-requirement-brief-tags">
                        ${nonBlockingFields.map(field => `
                            <span class="ai-requirement-brief-tag is-nonblocking" title="${this._escapeHtml(field)}">
                                ${this._escapeHtml(this._getRequirementFieldLabel(field))}
                            </span>
                        `).join('')}
                    </div>
                </div>
            `
            : '';

        container.classList.remove('is-empty');
        container.innerHTML = `
            <div class="ai-resource-audit-intro">
                <strong>具体资源在 Build 中完成绑定。</strong>
                <span>绑定结果继续进入现有参数草稿、Readiness、验证与 Apply Gate 刷新机制，不在前端建立平行状态。</span>
            </div>
            ${recommendedHtml}
            ${missingHtml}
            ${nonBlockingHtml}
            ${this._renderCanvasManualEditRecords?.(data) || ''}
            ${this._renderManualConfirmationRecords?.(data) || ''}
            <div class="ai-followup-actions">
                <div class="ai-followup-actions-hint">可复制成下一轮补充文本，也可直接挂到下一次生成的 hint。</div>
                <div class="ai-followup-action-row">
                    <button class="ai-followup-action" type="button" data-followup-action="copy">复制待补文本</button>
                    <button class="ai-followup-action" type="button" data-followup-action="insert">插入输入框</button>
                    <button class="ai-followup-action" type="button" data-followup-action="queue">用于下一轮提示</button>
                </div>
            </div>
        `;

        container.querySelectorAll('[data-followup-nav]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', () => {
                this._scrollToPendingDraftGroup(button.dataset.followupNav || '');
            });
        });

        container.querySelectorAll('[data-template-action]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', () => {
                const action = button.dataset.templateAction || '';
                if (action === 'free') {
                    this._queueTemplateSelection({ mode: 'free_generate' }, '下一轮将不使用模板，改为自由生成。');
                    return;
                }

                const templateName = String(button.dataset.templateName || '').trim();
                const selection = {
                    mode: action === 'fill' ? 'template_fill' : 'template_adapt',
                    templateId: String(button.dataset.templateId || '').trim() || null,
                    scenarioKey: String(button.dataset.scenarioKey || '').trim() || null
                };
                const label = action === 'fill'
                    ? `下一轮将严格沿用模板「${templateName || selection.scenarioKey || '已选模板'}」。`
                    : `下一轮将参考模板「${templateName || selection.scenarioKey || '已选模板'}」并允许改造。`;
                this._queueTemplateSelection(selection, label);
            });
        });

        container.querySelectorAll('[data-resource-action]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', () => {
                const resourceIndex = Number.parseInt(button.dataset.resourceIndex || '-1', 10);
                const item = Number.isInteger(resourceIndex) && resourceIndex >= 0 ? missing[resourceIndex] : null;
                if (!item) return;

                const task = button.closest?.('.ai-followup-resource-task') || null;
                const inputEl = task?.querySelector?.('[data-resource-input="true"]') || null;
                this._handleMissingResourceAction(item, button.dataset.resourceAction || '', {
                    value: inputEl?.value ?? '',
                    data,
                    flow
                });
            });
        });

        container.querySelectorAll('[data-followup-action]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', async () => {
                const action = button.dataset.followupAction;
                if (action === 'copy') {
                    const copied = await this._copyTextToClipboard(followupText);
                    this._addMessage('system', copied ? '待补信息已复制，可直接粘贴到下一轮说明。' : '复制失败，请手动复制待补信息。');
                    return;
                }

                if (action === 'insert') {
                    this._appendFollowupTextToInput(followupText);
                    this._addMessage('system', '待补信息已插入输入框，可继续补充修改需求。');
                    return;
                }

                if (action === 'queue') {
                    this.nextHintDraft = followupText;
                    this._renderQueuedHintBanner();
                    this._addMessage('system', '待补信息已挂到下一轮 hint，下一次生成会自动附带。');
                }
            });
        });
    }

    _queueTemplateSelection(selection, message) {
        const normalized = this._normalizeTemplateSelection(selection);
        if (!normalized) return;

        this.nextTemplateSelection = normalized;
        this._renderQueuedHintBanner();
        this._addMessage('system', message || '已设置下一轮模板策略。');
    }

    _normalizeTemplateSelection(selection) {
        if (!selection || typeof selection !== 'object') return null;

        const mode = String(selection?.mode ?? selection?.Mode ?? '').trim().toLowerCase();
        const templateId = String(selection?.templateId ?? selection?.TemplateId ?? '').trim();
        const scenarioKey = String(selection?.scenarioKey ?? selection?.ScenarioKey ?? '').trim();
        const normalizedMode = mode === 'strict'
            ? 'template_fill'
            : mode === 'relaxed'
                ? 'template_adapt'
                : mode;

        if (!normalizedMode && !templateId && !scenarioKey) return null;

        return {
            mode: normalizedMode,
            templateId: templateId || null,
            scenarioKey: scenarioKey || null
        };
    }

    _normalizeRecommendedTemplate(item) {
        if (!item || typeof item !== 'object') return null;

        const templateName = String(item?.templateName ?? item?.TemplateName ?? '').trim();
        if (!templateName) return null;

        return {
            templateId: String(item?.templateId ?? item?.TemplateId ?? '').trim(),
            templateName,
            templateVersion: String(item?.templateVersion ?? item?.TemplateVersion ?? '').trim(),
            scenarioKey: String(item?.scenarioKey ?? item?.ScenarioKey ?? '').trim(),
            industry: String(item?.industry ?? item?.Industry ?? '').trim(),
            matchReason: String(item?.matchReason ?? item?.MatchReason ?? '').trim(),
            matchMode: String(item?.matchMode ?? item?.MatchMode ?? '').trim(),
            confidence: Number(item?.confidence ?? item?.Confidence ?? 0),
            matchedFields: Array.isArray(item?.matchedFields ?? item?.MatchedFields)
                ? (item?.matchedFields ?? item?.MatchedFields).map(value => String(value || '').trim()).filter(Boolean)
                : [],
            missingSignals: Array.isArray(item?.missingSignals ?? item?.MissingSignals)
                ? (item?.missingSignals ?? item?.MissingSignals).map(value => String(value || '').trim()).filter(Boolean)
                : []
        };
    }

    _normalizeTemplateCandidates(items) {
        if (!Array.isArray(items)) return [];

        const seen = new Set();
        return items
            .map(item => this._normalizeRecommendedTemplate(item))
            .filter(Boolean)
            .sort((left, right) => (right.confidence || 0) - (left.confidence || 0))
            .filter(item => {
                const key = `${item.templateId}|${item.scenarioKey}|${item.templateName}`.toLowerCase();
                if (seen.has(key)) return false;
                seen.add(key);
                return true;
            })
            .slice(0, 3);
    }

    _getGenerationMode(data) {
        return String(data?.generationMode ?? data?.GenerationMode ?? '').trim();
    }

    _getTemplateLockLevel(data) {
        return String(data?.templateLockLevel ?? data?.TemplateLockLevel ?? '').trim();
    }

    _formatTemplateStrategy(mode, lockLevel) {
        const modeText = {
            template_fill: '严格填充',
            template_adapt: '参考改造',
            free_generate: '自由生成'
        }[String(mode || '').trim()] || '自动判定';
        const lockText = {
            strict: '强约束',
            relaxed: '弱约束',
            none: '无模板约束'
        }[String(lockLevel || '').trim()] || '约束未声明';
        return `${modeText} / ${lockText}`;
    }

    _resolvePendingOperatorLabel(operatorId, operators) {
        return this._resolvePendingOperatorContext(operatorId, operators).label;
    }

    _buildFollowupHintText({ recommended, pending, missing, operators, nonBlockingFields = [] }) {
        const lines = ['请基于上一轮流程继续完善，优先补齐待确认参数和缺失资源，不要重建无关结构。'], safeHintText = (value, maxChars = 220) => this._sanitizeAssistantFailureText?.(value, maxChars) || String(value ?? '').trim().slice(0, maxChars);

        if (recommended?.templateName) {
            lines.push(`优先沿用模板：${safeHintText(recommended.templateName, 160)}${recommended.matchReason ? `（${safeHintText(recommended.matchReason, 220)}）` : ''}。`);
        }

        if (pending.length > 0) {
            lines.push('待确认参数：');
            pending.forEach(item => {
                const label = safeHintText(this._resolvePendingOperatorLabel(item.operatorId, operators), 160);
                const filledPairs = [];
                const missingNames = [];
                const context = this._resolvePendingOperatorContext(item.operatorId, operators);
                const metadata = this._getCachedOperatorMetadata(context.operatorType);

                item.parameterNames.forEach(parameterName => {
                    const fieldType = this._normalizePendingFieldType(this._findMetadataParameter(metadata, parameterName));
                    const value = this._getPendingDraftConfirmedValue(item.operatorId, parameterName);
                    if (this._hasPendingDraftValue(value, fieldType)) {
                        filledPairs.push(`${safeHintText(parameterName, 80)}=${safeHintText(this._stringifyPendingDraftValue(value, fieldType), 180)}`);
                    } else {
                        missingNames.push(safeHintText(parameterName, 80));
                    }
                });

                if (filledPairs.length > 0 && missingNames.length > 0) {
                    lines.push(`- ${label}：已填写 ${filledPairs.join('；')}；仍需补充 ${missingNames.join('、')}`);
                } else if (filledPairs.length > 0) {
                    lines.push(`- ${label}：已填写 ${filledPairs.join('；')}`);
                } else {
                    lines.push(`- ${label}：请补充 ${missingNames.join('、')}`);
                }
            });
        }

        if (missing.length > 0) {
            lines.push('缺失资源：');
            missing.forEach(item => {
                const name = safeHintText(item.resourceType || '资源', 80);
                const detail = safeHintText(item.description || item.resourceKey || '缺少必要资源', 220);
                lines.push(`- ${name}${item.resourceKey ? `（${safeHintText(item.resourceKey, 160)}）` : ''}：${detail}`);
            });
        }

        if (Array.isArray(nonBlockingFields) && nonBlockingFields.length > 0) {
            lines.push(`非阻断待补字段：${nonBlockingFields.map(field => safeHintText(this._getRequirementFieldLabel(field), 80)).join('、')}`);
        }

        lines.push('如果仍缺文件、模型、地址或标定数据，请明确告诉我还需要补什么。');
        return lines.join('\n');
    }

    queueParameterOnlyFollowupHint(payload = {}) {
        const hint = buildWireSequenceFollowupHint(payload);
        if (!hint) {
            return '';
        }

        this.nextHintDraft = hint;
        this._renderQueuedHintBanner();
        return hint;
    }

    /**
     * 打字机效果：每次追加 chunkSize 个字符
     */


    _setGeneratingState(busy) {
        this.isGenerating = busy;
        if (!busy) {
            this.isCancellingGenerate = false;
        }
        const btn = this.container.querySelector('#ai-btn-gen');
        const cancelBtn = this.container.querySelector('#ai-btn-cancel');
        if(btn) {
            btn.disabled = busy;
            if(busy) {
                btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="white"><path d="M12 4V2A10 10 0 0 0 2 12h2a8 8 0 0 1 8-8z"><animateTransform attributeName="transform" type="rotate" from="0 12 12" to="360 12 12" dur="1s" repeatCount="indefinite"/></path></svg>`;
            } else {
                btn.innerHTML = `<svg viewBox="0 0 24 24" width="18" height="18" fill="white"><path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z"/></svg>`;
            }
        }
        if (cancelBtn) {
            cancelBtn.disabled = !busy || this.isCancellingGenerate;
            cancelBtn.classList.toggle('is-visible', busy);
        }
        const attachBtn = this.container.querySelector('#ai-btn-attach');
        if (attachBtn) attachBtn.disabled = busy;
        this.container.querySelectorAll('[data-requirement-mode]').forEach(btnEl => {
            btnEl.disabled = busy;
        });
        this.container.querySelectorAll('.ai-attachment-remove').forEach(btnEl => {
            btnEl.disabled = busy;
        });
        this.container.querySelectorAll('[data-brief-action]').forEach(btnEl => {
            btnEl.disabled = busy;
        });
        this.container.querySelectorAll('.ai-plan-action, .ai-plan-option, .ai-clarification-option, #ai-btn-start-build-inline').forEach(btnEl => {
            btnEl.disabled = busy;
        });
        this.container.querySelectorAll('.ai-followup-action').forEach(btnEl => {
            btnEl.disabled = busy;
        });
        this.container.querySelectorAll('[data-draft-input="true"], [data-draft-file-pick], [data-followup-nav], [data-draft-adopt], [data-resource-action], [data-resource-input]').forEach(el => {
            el.disabled = busy;
        });
        const clearHintBtn = this.container.querySelector('#ai-btn-clear-followup-hint');
        if (clearHintBtn) clearHintBtn.disabled = busy;
        const input = this.container.querySelector('#ai-input');
        if(input) input.disabled = busy;
        this._updatePlanBuildActionState?.();
        this._updateApplyButtonState();
        this._updatePendingDraftSummary();
    }

    _escapeHtml(value) {
        const div = document.createElement('div');
        div.textContent = value ?? '';
        return div.innerHTML;
    }

    _setResultStatusNote(text = '', tone = '', allowHtml = false) {
        const note = this.container?.querySelector('#ai-result-status-note');
        if (!note) return;

        const normalizedText = String(allowHtml ? text : (this._sanitizeAssistantFailureText?.(text, 900) || text) || '').trim();
        note.className = 'ai-result-status-note';
        if (!normalizedText) {
            note.textContent = '';
            note.hidden = true;
            this._renderBuildPresentation?.();
            return;
        }

        note.hidden = false;
        if (tone) {
            note.classList.add(`is-${tone}`);
        }
        if (allowHtml) {
            note.innerHTML = normalizedText;
        } else {
            note.textContent = normalizedText;
        }
        this._renderBuildPresentation?.();
    }

    _renderManualRetryBanner() {
        const container = this.container?.querySelector('#ai-manual-retry-banner');
        if (!container) return;

        const manualRetry = this.pendingManualRetry;
        const draft = String(manualRetry?.draft ?? manualRetry?.Draft ?? '').trim();
        if (!draft) {
            container.innerHTML = '';
            return;
        }

        const summary = String(manualRetry?.summary ?? manualRetry?.Summary ?? '').trim();
        const preview = draft.length > 180 ? `${draft.slice(0, 180)}...` : draft;
        container.innerHTML = `
            <div class="ai-manual-retry-card">
                <div class="ai-manual-retry-copy">
                    <div class="ai-manual-retry-title">已生成纠错草稿，需手动确认后发送</div>
                    <div class="ai-manual-retry-desc">系统不会自动重试。请先检查输入框中的纠错内容，再手动点击发送。</div>
                    ${summary ? `<div class="ai-manual-retry-summary">${this._escapeHtml(summary)}</div>` : ''}
                    <div class="ai-manual-retry-preview">${this._escapeHtml(preview)}</div>
                </div>
                <button class="ai-manual-retry-action" type="button" id="ai-btn-reapply-manual-retry">重新填入</button>
            </div>
        `;

        const actionButton = container.querySelector('#ai-btn-reapply-manual-retry');
        if (actionButton) {
            actionButton.disabled = this.isGenerating;
            actionButton.addEventListener('click', () => this._appendManualRetryDraftToInput(this.pendingManualRetry));
        }
    }

    _renderQueuedHintBanner() {
        const container = this.container?.querySelector('#ai-followup-hint-banner');
        if (!container) return;

        const draft = String(this.nextHintDraft || '').trim();
        const templateSelection = this._normalizeTemplateSelection(this.nextTemplateSelection);
        if (!draft && !templateSelection) {
            container.innerHTML = '';
            return;
        }

        const previewParts = [];
        if (templateSelection) {
            previewParts.push(this._formatQueuedTemplateSelection(templateSelection));
        }
        if (draft) {
            previewParts.push(draft);
        }
        const previewText = previewParts.join('\n');
        const preview = previewText.length > 120 ? `${previewText.slice(0, 120)}...` : previewText;
        container.innerHTML = `
            <div class="ai-followup-hint-card">
                <div class="ai-followup-hint-copy">
                    <div class="ai-followup-hint-title">下一轮已附加策略</div>
                    <div class="ai-followup-hint-preview">${this._escapeHtml(preview)}</div>
                </div>
                <button class="ai-followup-hint-clear" type="button" id="ai-btn-clear-followup-hint">清除</button>
            </div>
        `;

        const clearButton = container.querySelector('#ai-btn-clear-followup-hint');
        if (clearButton) {
            clearButton.disabled = this.isGenerating;
            clearButton.addEventListener('click', () => {
                this.nextHintDraft = '';
                this.nextTemplateSelection = null;
                this._renderQueuedHintBanner();
                this._addMessage('system', '已清除下一轮附加策略。');
            });
        }
    }

    _formatQueuedTemplateSelection(selection) {
        const normalized = this._normalizeTemplateSelection(selection);
        if (!normalized) return '';

        if (normalized.mode === 'free_generate') {
            return '模板策略：自由生成，不使用模板约束。';
        }

        const modeText = normalized.mode === 'template_fill' ? '严格沿用模板' : '参考模板改造';
        const target = normalized.scenarioKey || normalized.templateId || '已选模板';
        return `模板策略：${modeText}（${target}）。`;
    }

    async _copyTextToClipboard(text) {
        const value = String(text || '').trim();
        if (!value) return false;

        try {
            if (navigator?.clipboard?.writeText) {
                await navigator.clipboard.writeText(value);
                return true;
            }
        } catch (error) {
            console.warn('[AiPanel] navigator.clipboard 写入失败，准备回退。', error);
        }

        try {
            const textArea = document.createElement('textarea');
            textArea.value = value;
            textArea.setAttribute('readonly', 'readonly');
            textArea.style.position = 'fixed';
            textArea.style.left = '-9999px';
            document.body.appendChild(textArea);
            textArea.select();
            const copied = document.execCommand('copy');
            document.body.removeChild(textArea);
            return copied;
        } catch (error) {
            console.warn('[AiPanel] execCommand 复制失败。', error);
            return false;
        }
    }

    _appendFollowupTextToInput(text) {
        const input = this.container?.querySelector('#ai-input');
        if (!input) return;

        const value = String(text || '').trim();
        if (!value) return;

        const current = String(input.value || '').trim();
        input.value = current ? `${current}\n\n${value}` : value;
        input.focus();
        input.style.height = 'auto';
        input.style.height = `${input.scrollHeight}px`;
    }

    _appendManualRetryDraftToInput(manualRetry) {
        const input = this.container?.querySelector('#ai-input');
        if (!input || !manualRetry) return;

        const nextValue = this._buildManualRetryInputText(manualRetry);
        if (!nextValue) return;

        input.value = nextValue;
        input.focus();
        input.style.height = 'auto';
        input.style.height = `${input.scrollHeight}px`;
    }

    _buildManualRetryInputText(manualRetry) {
        const draft = String(manualRetry?.draft ?? manualRetry?.Draft ?? '').trim();
        const originalMessage = String(manualRetry?.originalMessage || this.lastUserPrompt || '').trim();
        if (!draft && !originalMessage) return '';

        const cleanedDraft = this._stripEmbeddedOriginalFromManualRetryDraft(draft, originalMessage);
        const parts = [];
        if (originalMessage) {
            parts.push(originalMessage);
        }
        if (cleanedDraft) {
            parts.push(cleanedDraft);
        }
        return parts.join('\n\n').trim();
    }

    _stripEmbeddedOriginalFromManualRetryDraft(draft, originalMessage) {
        const normalizedDraft = String(draft || '').trim();
        if (!normalizedDraft) return '';
        if (!originalMessage) return normalizedDraft;

        const anchor = '本轮需求原话：';
        const startIndex = normalizedDraft.indexOf(anchor);
        if (startIndex < 0 || !normalizedDraft.includes(originalMessage)) {
            return normalizedDraft;
        }

        const afterAnchor = normalizedDraft.slice(startIndex + anchor.length);
        const markerMatches = ['优先修复：', '诊断信息：', '上一轮输出摘要：', '请尽量保留已经正确的算子、连线和参数']
            .map(marker => afterAnchor.indexOf(marker))
            .filter(index => index >= 0);
        if (markerMatches.length === 0) {
            return normalizedDraft.replace(anchor, '').replace(originalMessage, '').trim();
        }

        const nextMarkerIndex = Math.min(...markerMatches);
        const before = normalizedDraft.slice(0, startIndex).trim();
        const after = afterAnchor.slice(nextMarkerIndex).trim();
        return [before, after].filter(Boolean).join('\n\n').trim();
    }

    _clearActiveRequestState() {
        this.activeGenerateRequestId = null;
        this.activeGenerateSessionId = null;
    }

    _getPersistenceWarning(payload = null) {
        const warning = payload?.persistenceWarning || payload?.PersistenceWarning || null;
        if (!warning || typeof warning !== 'object') return null;
        return {
            code: String(warning.code || warning.Code || 'session_persistence_failed'),
            message: String(warning.message || warning.Message || '结果已生成，但本次会话尚未成功保存。')
        };
    }

    _clearResultPane() {
        const briefCard = this.container.querySelector('#ai-result-requirement-brief-card');
        const brief = this.container.querySelector('#ai-result-requirement-brief');
        if (briefCard) briefCard.hidden = true;
        if (brief) {
            brief.classList.add('is-empty');
            brief.innerHTML = '<div class="ai-followup-empty">当前尚未提炼出需求摘要。</div>';
        }
        const summary = this.container.querySelector('#ai-result-summary');
        if (summary) summary.textContent = '--';
        const ops = this.container.querySelector('#ai-result-ops');
        if (ops) ops.innerHTML = '';
        const followups = this.container.querySelector('#ai-result-followups');
        if (followups) {
            followups.classList.add('is-empty');
            followups.innerHTML = '<div class="ai-followup-empty">当前没有待确认参数或缺失资源。</div>';
        }
        const editor = this.container.querySelector('#ai-result-parameter-editor');
        if (editor) {
            editor.classList.add('is-empty');
            editor.innerHTML = '<div class="ai-followup-empty">当前没有待确认参数，暂无需补录。</div>';
        }
        const promptTraceCard = this.container.querySelector('#ai-result-prompt-trace-card');
        const promptTrace = this.container.querySelector('#ai-result-prompt-trace');
        if (promptTraceCard) promptTraceCard.hidden = true;
        if (promptTrace) promptTrace.innerHTML = '';
        this.pendingClarificationPayload = null;
        this._renderAgentRuntime(null, { reset: true });
        this._setResultStatusNote('', '');
        this._streamBuffer = { thinking: '', content: '' };
        this._streamFlushPending = false;
    }

    _scrollToBottom() {
        const container = this.container.querySelector('#ai-chat-container');
        if(container) container.scrollTop = container.scrollHeight;
    }

    _setupScrollListener() {
        const container = this.container?.querySelector('#ai-chat-container');
        if (!container) return;
        this._chatContainer = container;

        this._chatScrollHandler = () => {
            if (this._scrollStateRaf) return;
            this._scrollStateRaf = window.requestAnimationFrame(() => {
                this._scrollStateRaf = 0;
                this._syncScrollFollowState();
            });
        };
        container.addEventListener('scroll', this._chatScrollHandler, { passive: true });

        this._createScrollBottomBtn();
    }

    _setupComposerLayoutSync() {
        const pane = this.container?.querySelector('.ai-pane-left');
        const input = this.container?.querySelector('.ai-input-section');
        if (!pane || !input) return;

        this._syncComposerOffset();

        if (typeof ResizeObserver === 'undefined') {
            this._composerResizeHandler = () => this._syncComposerOffset();
            window.addEventListener('resize', this._composerResizeHandler, { passive: true });
            return;
        }

        if (this._inputResizeObserver) {
            this._inputResizeObserver.disconnect();
        }
        this._inputResizeObserver = new ResizeObserver(() => this._syncComposerOffset());
        this._inputResizeObserver.observe(input);
    }

    _syncComposerOffset() {
        const pane = this.container?.querySelector('.ai-pane-left');
        const input = this.container?.querySelector('.ai-input-section');
        if (!pane || !input) return;

        const height = Math.ceil(input.getBoundingClientRect().height || 0);
        if (height > 0) {
            pane.style.setProperty('--ai-composer-offset', `${height}px`);
        }
    }

    _syncScrollFollowState() {
        const container = this._chatContainer || this.container?.querySelector('#ai-chat-container');
        if (!container) return;

        const isNearBottom = container.scrollHeight - container.scrollTop - container.clientHeight <= 120;
        this.userHasScrolledUp = !isNearBottom;
        if (isNearBottom) {
            this.unreadStreamCount = 0;
        }
        this._updateScrollBottomBtn();
    }

    _createScrollBottomBtn() {
        const container = this._chatContainer || this.container?.querySelector('#ai-chat-container');
        if (!container) return;

        let btn = this.container.querySelector('#ai-scroll-bottom-btn');
        if (btn) {
            this._scrollBottomButton = btn;
            this._scrollBottomBadge = btn.querySelector('.ai-scroll-bottom-badge');
            return;
        }

        btn = document.createElement('button');
        btn.type = 'button';
        btn.id = 'ai-scroll-bottom-btn';
        btn.className = 'ai-scroll-bottom-btn';
        btn.title = '回到底部';
        btn.setAttribute('aria-label', '回到底部');
        btn.setAttribute('aria-hidden', 'true');
        btn.tabIndex = -1;
        btn.innerHTML = `
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                <line x1="12" y1="5" x2="12" y2="19"></line>
                <polyline points="19 12 12 19 5 12"></polyline>
            </svg>
            <span class="ai-scroll-bottom-badge" id="ai-scroll-bottom-badge" hidden>0</span>
        `;

        const parent = container.parentElement;
        if (parent) {
            parent.appendChild(btn);
            btn.addEventListener('click', () => {
                const prefersReducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
                container.scroll({
                    top: container.scrollHeight,
                    behavior: prefersReducedMotion ? 'auto' : 'smooth'
                });
                this.userHasScrolledUp = false;
                this.unreadStreamCount = 0;
                this._updateScrollBottomBtn();
            });
            this._scrollBottomButton = btn;
            this._scrollBottomBadge = btn.querySelector('.ai-scroll-bottom-badge');
        }
    }

    _updateScrollBottomBtn() {
        const btn = this._scrollBottomButton || this.container?.querySelector('#ai-scroll-bottom-btn');
        const badge = this._scrollBottomBadge || this.container?.querySelector('#ai-scroll-bottom-badge');
        if (!btn) return;

        const shouldShow = this.userHasScrolledUp && this.unreadStreamCount > 0 && this.isGenerating;
        btn.classList.toggle('show', shouldShow);
        btn.setAttribute('aria-hidden', shouldShow ? 'false' : 'true');
        btn.tabIndex = shouldShow ? 0 : -1;

        if (badge) {
            badge.hidden = !shouldShow;
            badge.textContent = this.unreadStreamCount > 99 ? '99+' : String(this.unreadStreamCount);
        }
    }

    _setupExamplesFolding() {
        const toggle = this.container?.querySelector('#examples-toggle');
        const tagsContainer = this.container?.querySelector('.ai-example-tags');
        const parentSection = this.container?.querySelector('.ai-quick-examples');
        if (!toggle || !tagsContainer || !parentSection) return;

        let isCollapsed = false;
        try {
            const savedPreference = localStorage.getItem('cv_ai_examples_collapsed');
            if (savedPreference === 'true' || savedPreference === 'false') {
                isCollapsed = savedPreference === 'true';
            } else {
                isCollapsed = window.matchMedia?.('(max-width: 720px)').matches || false;
            }
        } catch {
            isCollapsed = window.matchMedia?.('(max-width: 720px)').matches || false;
        }

        if (isCollapsed) {
            tagsContainer.classList.add('is-collapsed');
            parentSection.classList.add('is-collapsed');
        }
        toggle.setAttribute('aria-expanded', isCollapsed ? 'false' : 'true');

        const toggleExamples = () => {
            const currentlyCollapsed = tagsContainer.classList.contains('is-collapsed');
            const nextCollapsed = !currentlyCollapsed;
            tagsContainer.classList.toggle('is-collapsed', nextCollapsed);
            parentSection.classList.toggle('is-collapsed', nextCollapsed);
            toggle.setAttribute('aria-expanded', nextCollapsed ? 'false' : 'true');

            try {
                localStorage.setItem('cv_ai_examples_collapsed', nextCollapsed ? 'true' : 'false');
            } catch {
                // ignore localStorage failures
            }

            this._requestOwnedAnimationFrame?.(() => this._syncComposerOffset());
            this._setOwnedTimeout?.(() => this._syncComposerOffset(), 260);
        };

        toggle.addEventListener('click', toggleExamples);
    }

}

Object.assign(
    AiPanel.prototype,
    aiPanelWorkbenchMixin,
    aiPanelPendingParametersMixin,
    aiPanelResourceBindingMixin,
    aiPanelChatMixin,
    aiPanelValidationPreviewMixin,
    aiPanelGenerateRequestMixin,
    aiPanelAgentWorkspaceMixin,
    aiPanelLiveEventsMixin,
    aiPanelLifecycleMixin,
    aiPanelAccessibilityMixin,
    aiPanelAgentRunMixin,
    aiPanelRequirementBriefMixin,
    aiPanelAttachmentsMixin,
    aiPanelSessionHistoryMixin,
    aiPanelApplyPreviewMixin,
    aiPanelTopologySummaryMixin
);

installAiPanelPlanPresentation(AiPanel.prototype);
installAiPanelBuildPresentation(AiPanel.prototype);
installAiPanelShellPresentation(AiPanel.prototype);
