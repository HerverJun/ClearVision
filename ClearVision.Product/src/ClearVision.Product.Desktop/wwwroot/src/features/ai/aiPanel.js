import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import httpClient from '../../core/messaging/httpClient.js';
import { createSignal } from '../../core/state/store.js';
import { buildWireSequenceFollowupHint } from '../flow-editor/wireSequenceAssist.js';
import { getOperatorTypeDisplayName } from '../../shared/operatorDisplayNames.js';
import {
    AiWorkbenchStates,
    STAGE_DIAGNOSTIC_LABELS,
    aiPanelWorkbenchMixin
} from './aiPanelWorkbench.js';
import { aiPanelPendingParametersMixin } from './aiPanelPendingParameters.js';
import { aiPanelChatMixin } from './aiPanelChat.js';
import { aiPanelValidationPreviewMixin } from './aiPanelValidationPreview.js';
import { aiPanelGenerateRequestMixin } from './aiPanelGenerateRequest.js';
import { aiPanelRequirementBriefMixin } from './aiPanelRequirementBrief.js';
import { aiPanelAttachmentsMixin } from './aiPanelAttachments.js';
import { aiPanelSessionHistoryMixin } from './aiPanelSessionHistory.js';
import { aiPanelApplyPreviewMixin } from './aiPanelApplyPreview.js';
import { aiPanelTopologySummaryMixin } from './aiPanelTopologySummary.js';

/**
 * AI 智能助手面板
 * 负责管理 AI 交互界面、发送生成请求、显示思考链和结果
 */
export class AiPanel {
    constructor(containerId, flowCanvas, options = {}) {
        this.containerId = containerId;
        this.flowCanvas = flowCanvas;
        this.options = options || {};
        this.container = document.getElementById(containerId);
        this.sessionStorageKey = 'cv_ai_session_id';
        
        // 状态
        this.isGenerating = false;
        this.history = []; // { sessionId, lastMessage, updatedAtUtc, turnCount }
        this.filteredHistory = [];
        this.historyKeyword = '';
        this.isHistoryPanelOpen = false;
        this.currentThinkingStep = null;
        this.sessionId = this._loadSessionId();
        this.currentResult = null;
        this.lastUserPrompt = '';
        this.nextHintDraft = '';
        this.nextTemplateSelection = null;
        this.activeGenerateRequestId = null;
        this.activeGenerateSessionId = null;
        this.isCancellingGenerate = false;
        this.attachments = [];
        this.pendingParameterDrafts = {};
        this.pendingParameterDraftSignature = '';
        this.operatorMetadataCache = new Map();
        this.operatorMetadataLoading = new Map();
        this.cameraBindingsCache = [];
        this.cameraBindingsLoadingPromise = null;
        this.currentResultVersion = 0;
        this.appliedResultVersion = 0;
        this.currentCanvasRevision = this.flowCanvas?.getFlowRevision?.() || 0;
        this.appliedCanvasRevision = 0;
        this.pendingOperatorBindings = {};
        this.unsubscribeStructureState = null;
        this.pendingParameterFilePickContext = null;
        this.pendingParameterHighlightTimer = null;
        this.pendingParameterConfirmedDraftSignature = '';
        this.pendingParameterConfirmedValueSignature = '';
        this._streamBuffer = { thinking: '', content: '' };
        this._streamFlushPending = false;
        this.activeAssistantTurn = null;
        this.pendingManualRetry = null;
        this.requirementMode = this._loadRequirementMode();

        // 工作台状态机
        this.workbenchState = AiWorkbenchStates.IDLE;
        this._lastActiveWorkbenchState = AiWorkbenchStates.IDLE;
        this._workbenchStageTimeline = [];
        this._lastAgentRuntime = null;
        this._clarificationSelectionDraft = {};
        this._lastClarificationDraftText = '';
        this.isVisionAgentDeveloperUiEnabled = this._isAgentDeveloperControlsEnabled();
        this.useVisionAgentGenerateFlow = this._loadAgentGenerateFlowEnabled();
        this.agentGenerateFlowMode = this._loadAgentGenerateFlowMode();
        this.runtimePreviewConsent = false;

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
    }

    _init() {
        if (!this.container) {
            console.error('[AiPanel] 容器未找到:', this.containerId);
            return;
        }
        
        this.render();
        this._setupMessageListeners();
        this._setupCanvasStructureSync();
        this._loadHistory();
        this._setupScrollListener();
        this._setupComposerLayoutSync();
        this._setupExamplesFolding();
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
    
    _handleNewConversation() {
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
        this.activeGenerateSessionId = null;
        this.isCancellingGenerate = false;
        this.attachments = [];
        this._resetPendingDraftState();
        this._resetCurrentResultSyncState();
        this.pendingParameterFilePickContext = null;
        this.pendingManualRetry = null;
        this.activeAssistantTurn = null;
        this._preApplySnapshot = null;
        this._lastAttachmentReport = null;
        this._lastModelSupportsVision = null;
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

            this._syncPendingParameterDrafts(this.currentResult, this.currentResult.flow, { force: true });
            this._renderFollowupChecklist(this.currentResult, this.currentResult.flow);
            const editor = this.container?.querySelector('#ai-result-parameter-editor');
            if (editor && !editor.classList.contains('is-empty')) {
                this._renderParameterDraftEditor(this.currentResult, this.currentResult.flow);
            }
        });
    }

    _resetPendingDraftState() {
        this.pendingParameterDrafts = {};
        this.pendingParameterDraftSignature = '';
        this.pendingOperatorBindings = {};
        this.pendingParameterConfirmedDraftSignature = '';
        this.pendingParameterConfirmedValueSignature = '';
    }

    _resetCurrentResultSyncState() {
        this.currentResult = null;
        this.currentResultVersion = 0;
        this.appliedResultVersion = 0;
        this.appliedCanvasRevision = this.currentCanvasRevision;
        this._updateApplyButtonState();
    }

    _setCurrentResult(payload) {
        this.currentResult = payload;
        this.currentResultVersion += 1;
        this.appliedResultVersion = 0;
        this.appliedCanvasRevision = 0;
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

        const hasFlow = Boolean(this.currentResult?.flow || this.currentResult?.Flow);
        const applied = this._isCurrentResultAppliedToCanvas();
        button.disabled = this.isGenerating || !hasFlow || applied;
        button.classList.toggle('is-disabled', button.disabled);
        button.setAttribute('aria-disabled', button.disabled ? 'true' : 'false');
        const label = applied
            ? '已应用到流程草稿'
            : hasFlow
                ? '应用到当前流程草稿'
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
            <div class="ai-workspace">
                <aside class="ai-pane-left" data-ai-chat-pane="true">
                    <div class="ai-pane-header">
                        <span class="pane-icon">
                            <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor"><path d="M20 2H4c-1.1 0-2 .9-2 2v18l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2z"/></svg>
                        </span>
                        <span class="pane-title">CO-PILOT 对话</span>
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
                        <div class="ai-requirement-mode-bar">
                            <div class="ai-requirement-mode-label">需求模式</div>
                            <div class="ai-requirement-mode-toggle" id="ai-requirement-mode-toggle" role="group" aria-label="需求模式">
                                <button class="ai-mode-chip" type="button" data-requirement-mode="strict">严格澄清</button>
                                <button class="ai-mode-chip" type="button" data-requirement-mode="draft">草稿优先</button>
                            </div>
                            <div class="ai-requirement-mode-tip" id="ai-requirement-mode-tip"></div>
                        </div>
                        ${this._renderAgentDeveloperControls()}
                        <div class="ai-input-box">
                            <button class="icon-btn" id="ai-btn-attach" type="button" title="添加附件" aria-label="添加附件">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor" aria-hidden="true"><path d="M16.5 6v11.5c0 2.21-1.79 4-4 4s-4-1.79-4-4V5a2.5 2.5 0 015 0v10.5c0 .55-.45 1-1 1s-1-.45-1-1V6H10v9.5a2.5 2.5 0 005 0V5c0-1.38-1.12-2.5-2.5-2.5S8 3.62 8 5v11.5c0 3.04 2.46 5.5 5.5 5.5s5.5-2.46 5.5-5.5V6h-1.5z"/></svg>
                            </button>
                            <textarea class="ai-textarea" id="ai-input" placeholder="描述检测目标、缺陷或流程修改..."></textarea>
                            <button class="ai-btn-cancel" id="ai-btn-cancel" type="button" title="取消生成">取消</button>
                            <button class="ai-btn-send" id="ai-btn-gen" type="button" title="发送" aria-label="发送">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="white" aria-hidden="true"><path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z"/></svg>
                            </button>
                        </div>
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

                <aside class="ai-pane-right" id="ai-result-pane" data-ai-workbench-pane="true">
                    <div class="ai-agent-runtime" id="ai-agent-runtime" hidden></div>
                    <div class="ai-workbench-state-bar" id="ai-workbench-state-bar"></div>
                    <div class="ai-result-status-note" id="ai-result-status-note"></div>
                    <div class="ai-results-scroll" id="ai-results-scroll">
                        <div class="result-card requirement-brief-card" id="ai-result-requirement-brief-card" hidden>
                            <div class="card-title requirement-brief-titlebar">
                                <span>需求澄清闭环</span>
                                <span class="card-badge ai-requirement-confidence" id="ai-requirement-confidence"></span>
                            </div>
                            <div class="ai-requirement-brief is-empty" id="ai-result-requirement-brief">
                                <div class="ai-followup-empty">当前尚未提炼出需求摘要。</div>
                            </div>
                        </div>

                        <div class="result-card overview">
                            <div class="card-title">方案概览</div>
                            <div class="ai-explanation" id="ai-result-summary">--</div>
                        </div>

                        <div class="result-card stage-timeline-card" id="ai-result-stage-timeline-card" hidden>
                            <div class="card-title stage-timeline-titlebar">
                                <span>生成流水线</span>
                                <span class="card-badge" id="ai-stage-timeline-summary"></span>
                            </div>
                            <div class="ai-stage-timeline" id="ai-result-stage-timeline"></div>
                        </div>

                        <div class="result-card ops-list">
                            <div class="card-title">生成的算子清单</div>
                            <div class="generated-ops-list" id="ai-result-ops"></div>
                        </div>

                        <div class="result-card validation-card" id="ai-result-validation-card" hidden>
                            <div class="card-title">
                                <span>校验与预演</span>
                            </div>
                            <div class="ai-validation-panel" id="ai-result-validation"></div>
                        </div>

                        <div class="result-card followup-card">
                            <div class="card-title">待补信息</div>
                            <div class="ai-followup-panel is-empty" id="ai-result-followups">
                                <div class="ai-followup-empty">当前没有待确认参数或缺失资源。</div>
                            </div>
                        </div>

                        <div class="result-card parameter-editor-card">
                            <div class="card-title">参数补录与审核</div>
                            <div class="ai-parameter-editor is-empty" id="ai-result-parameter-editor">
                                <div class="ai-followup-empty">当前没有待确认参数，暂无需补录。</div>
                            </div>
                        </div>

                        <div class="result-card attachment-card" id="ai-result-attachment-card" hidden>
                            <div class="card-title">附件与模型能力</div>
                            <div class="ai-attachment-panel" id="ai-result-attachments"></div>
                        </div>

                        <div class="result-card prompt-trace-card" id="ai-result-prompt-trace-card" hidden>
                            <div class="card-title prompt-trace-titlebar">
                                <span>调试信息</span>
                                <button class="ai-trace-toggle-btn" id="ai-trace-toggle" type="button">切换视图</button>
                            </div>
                            <div class="ai-prompt-trace" id="ai-result-prompt-trace"></div>
                        </div>
                    </div>
                     
                    <div class="apply-container">
                        <button class="btn-apply-flow" id="ai-btn-apply" disabled>
                            <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" style="margin-right:6px;">
                                <path d="M9 16.2L4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4L9 16.2z"/>
                            </svg>
                            应用到当前流程草稿
                        </button>
                    </div>
                </aside>
            </div>
        `;
        
        // 事件绑定
        const attachBtn = this.container.querySelector('#ai-btn-attach');
        const cancelBtn = this.container.querySelector('#ai-btn-cancel');
        this.container.querySelector('#ai-btn-gen').addEventListener('click', this._handleGenerate);
        this.container.querySelector('#ai-btn-apply').addEventListener('click', this._handleApplyFlow);
        this._updateApplyButtonState();
        if (attachBtn) attachBtn.addEventListener('click', this._handleAttachmentClick);
        if (cancelBtn) cancelBtn.addEventListener('click', this._handleCancelGenerate);
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
                const text = tag.dataset.text;
                const input = this.container.querySelector('#ai-input');
                input.value = text;
                input.focus();
                // 触发自动扩展
                input.style.height = 'auto';
                input.style.height = (input.scrollHeight) + 'px';
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

        this._renderAttachments();
        this._updateRequirementModeUI();
        this._renderQueuedHintBanner();
        this._renderRequirementBrief(null);
        this._renderFollowupChecklist(null);
    }
    
    _checkConnection() {
        const indicator = this.container.querySelector('#ai-conn-status');
        const dot = indicator?.querySelector('.status-dot');
        if (!dot) return;
        
        httpClient.get('/health')
            .then(() => {
                dot.className = 'status-dot connected';
            })
            .catch(() => {
                dot.className = 'status-dot disconnected';
            });
    }
    
    _setupMessageListeners() {
        webMessageBridge.on('GenerateFlowProgress', (data) => this._updateProgress(data));
        webMessageBridge.on('GenerateFlowStreamChunk', (data) => this._handleStreamChunk(data));
        webMessageBridge.on('AiFirewallBlocked', (data) => this._handleFirewallBlocked(data));
        webMessageBridge.on('GenerateFlowResult', (data) => this._handleResult(data));
        webMessageBridge.on('CancelGenerateFlowResult', (data) => this._handleCancelResult(data));
        webMessageBridge.on('FilePickedEvent', this._handleFilePickedEvent);
        webMessageBridge.on('GenerateFlowAttachmentReport', this._handleAttachmentReport);
        webMessageBridge.on('ListAiSessionsResult', (data) => this._handleListAiSessionsResult(data));
        webMessageBridge.on('GetAiSessionResult', (data) => this._handleGetAiSessionResult(data));
        webMessageBridge.on('DeleteAiSessionResult', (data) => this._handleDeleteAiSessionResult(data));
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
            this._addMessage('system', '当前没有可提交审核的方案，请先生成工程方案。');
            return;
        }

        const pending = this._resolvePendingParametersForDraft(data);
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
            this._addMessage('system', '请先填写全部待确认参数，再执行统一确认。');
            return;
        }

        this.pendingParameterConfirmedDraftSignature = this.pendingParameterDraftSignature;
        this.pendingParameterConfirmedValueSignature = confirmationState.valueSignature;
        this._updatePendingDraftSummary(data, flow);
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

        const chunkType = payload.chunkType; // 'thinking' or 'content'
        const content = payload.content || '';
        
        if (!content) return;

        if (chunkType === 'thinking') {
            this._streamBuffer.thinking += content;
        } else if (chunkType === 'content') {
            this._streamBuffer.content += content;
        } else {
            return;
        }

        if (!this._streamFlushPending) {
            this._streamFlushPending = true;
            requestAnimationFrame(() => this._flushStreamBuffer());
        }
    }

    _flushStreamBuffer() {
        this._streamFlushPending = false;
        const thinkingText = this._streamBuffer?.thinking || '';
        const replyText = this._streamBuffer?.content || '';

        this._streamBuffer.thinking = '';
        this._streamBuffer.content = '';

        if (thinkingText) {
            this._appendAssistantStreamText('reasoning', thinkingText);
        }
        if (replyText) {
            this._appendAssistantStreamText('reply', replyText);
        }

        if ((this._streamBuffer.thinking || this._streamBuffer.content) && !this._streamFlushPending) {
            this._streamFlushPending = true;
            requestAnimationFrame(() => this._flushStreamBuffer());
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

        const payload = data.payload || data;
        if (!this._shouldHandleGenerateTerminalPayload(payload)) {
            return;
        }

        const isCancelled = this._isCancelledResult(payload);
        this.isCancellingGenerate = false;
        this._setGeneratingState(false);
        this.sessionId = payload.sessionId || this.sessionId;
        this._saveSessionId(this.sessionId);
        const activeTurn = this.activeAssistantTurn
            || this._startAssistantTurn({ activate: false, statusText: '处理中', statusTone: 'streaming' });
        const isClarification = this._isClarificationResult(payload);
        const isInteractionOnly = this._isInteractionOnlyResult(payload);
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

        this._renderRequirementBrief(payload);

        if (!payload.success) {
            this._clearActiveRequestState();
            if (isClarification) {
                this.pendingManualRetry = null;
                this._setWorkbenchState(AiWorkbenchStates.CLARIFYING);
                this._renderManualRetryBanner();
                this._setAssistantTurnStatus(activeTurn, '待澄清', 'warning');
                this._setAssistantSectionText(
                    activeTurn,
                    'reply',
                    payload.aiExplanation || payload.AiExplanation || payload.errorMessage || payload.message || '当前需求需要先澄清。'
                );
                this._renderAssistantClarification(activeTurn, payload);
                if (!this.currentResult?.flow) {
                    const summary = this.container.querySelector('#ai-result-summary');
                    if (summary) {
                        summary.textContent = payload.aiExplanation || payload.AiExplanation || '当前需求需要先澄清。';
                    }
                }
                if (this.currentResult?.flow) {
                    this._setResultStatusNote('本轮生成前需要先补充需求，右侧仍保留上一版可应用方案。', 'warning');
                } else {
                    this._setResultStatusNote('当前需求还需要澄清，右侧已整理问题清单。', 'info');
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
        this._setResultStatusNote(turnIntent === 'modify_flow' ? '已基于当前工程完成微调。' : '', turnIntent === 'modify_flow' ? 'info' : '');
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
            this._addMessage('system', payload.message || '已发送取消请求，正在等待后端停止当前生成。');
            return;
        }

        this.isCancellingGenerate = false;
        this._addMessage('system', `取消生成未生效: ${payload.errorMessage || payload.message || '未知错误'}`);
    }
    
    _handleFirewallBlocked(data) {
        this._setGeneratingState(false);
        this._clearActiveRequestState();
        if (this.activeAssistantTurn) {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '连接受阻', 'failed');
            this._renderAssistantFailure(this.activeAssistantTurn, {
                errorMessage: data?.payload?.message || '网络连接被拦截'
            });
            this.activeAssistantTurn = null;
        }
        const chatContainer = this.container.querySelector('#ai-chat-container');
        const alert = document.createElement('div');
        alert.className = 'firewall-alert';
        alert.innerHTML = `
            <div class="firewall-icon">🚫</div>
            <div class="firewall-content">
                <div class="firewall-title">${data.payload?.message || '网络连接被拦截'}</div>
                <div class="firewall-desc">${data.payload?.detail || '请检查防火墙设置或网络代理。'}</div>
            </div>
        `;
        chatContainer.appendChild(alert);
        this._scrollToBottom();
    }
    
    _handleError(msg) {
        this._setGeneratingState(false);
        this._clearActiveRequestState();
        if (this.activeAssistantTurn) {
            this._setAssistantTurnStatus(this.activeAssistantTurn, '系统错误', 'failed');
            this._renderAssistantFailure(this.activeAssistantTurn, {
                errorMessage: `系统错误: ${msg}`
            });
            if (this.currentResult?.flow) {
                this._setResultStatusNote('本轮修改失败，右侧仍显示上一版可应用方案。', 'warning');
            }
            this.activeAssistantTurn = null;
            return;
        }
        this._addMessage('system', `❌ 系统错误: ${msg}`);
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

            if (!assistantTurn.reasoningBody?.textContent?.trim()) {
                this._setAssistantSectionText(
                    assistantTurn,
                    'reasoning',
                    data.reasoning || data.Reasoning || ''
                );
            }
        }

        const flow = data?.flow || data?.Flow || null;
        const ops = this._extractOperators(flow);
        const connections = this._extractConnections(flow);
        this._syncPendingParameterDrafts(data, flow);
        this._renderAgentRuntime(data);
        this._renderRequirementBrief(data);

        const clarificationRequired = this._isClarificationResult(data);
        const requirementBrief = this._normalizeRequirementBrief(data?.requirementBrief ?? data?.RequirementBrief ?? null);
        const summaryLines = clarificationRequired && !flow
            ? [
                `当前需求还需要澄清，已整理 <span class="result-count">${(requirementBrief?.clarificationQuestions?.length || requirementBrief?.missingFacts?.length || 0)}</span> 个问题。`
            ]
            : [
                `该方案包含 <span class="result-count">${ops.length}</span> 个算子和 <span class="result-count">${connections.length}</span> 条连线。`
            ];
        const templateSummary = this._buildTemplateFirstSummary(data);
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
            opsContainer.innerHTML = '<div class="ai-followup-empty">当前尚未进入生成阶段，请先完成需求澄清。</div>';
        } else {
            ops.forEach((op, i) => {
                const opName = op?.displayName || op?.DisplayName || op?.name || op?.Name || '未命名算子';
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
                setTimeout(() => {
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
        const baseUrl = String(trace.baseUrl || '').trim();

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

        // Debug view: full prompt trace
        const capabilities = this._formatPromptTraceJson(trace.capabilities || null);
        const attachmentReport = this._formatPromptTraceJson(trace.attachmentReport || null);
        const referenceFlow = String(trace.usedReferenceFlowSummary || '').trim();
        const systemPrompt = String(trace.systemPrompt || '').trim();
        const userPrompt = String(trace.userPrompt || '').trim();

        container.innerHTML = `
            <details class="ai-prompt-trace-details" open>
                <summary>本次实际发送给模型的上下文</summary>
                <div class="ai-prompt-trace-grid">
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">Meta</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml([
                            `mode=${mode || '--'}`,
                            `provider=${provider || '--'}`,
                            `model=${model || '--'}`,
                            `baseUrl=${baseUrl || '--'}`
                        ].join('\n'))}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">Capabilities</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(capabilities)}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">Attachment Report</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(attachmentReport)}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">Reference Flow Summary</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(referenceFlow || '--')}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">System Prompt</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(systemPrompt || '--')}</pre>
                    </div>
                    <div class="ai-prompt-trace-block">
                        <div class="ai-prompt-trace-label">User Prompt</div>
                        <pre class="ai-prompt-trace-pre">${this._escapeHtml(userPrompt || '--')}</pre>
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
            return JSON.stringify(value, null, 2);
        } catch {
            return String(value);
        }
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
            const templateName = this._escapeHtml(String(recommended.templateName));
            const reason = this._escapeHtml(String(recommended.matchReason || '命中高频场景'));
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
                .map(item => this._escapeHtml(String(item?.resourceKey || item?.description || '未知资源')))
                .join('、');
            const suffix = missing.length > 2 ? '...' : '';
            parts.push(`缺失资源：<span class="result-count">${missing.length}</span> 项（${missingPreview}${suffix}）`);
        }

        return parts.join('；');
    }

    _renderFollowupChecklist(data, flow = null) {
        const container = this.container?.querySelector('#ai-result-followups');
        if (!container) return;

        const pending = this._resolvePendingParametersForDraft(data);
        const missing = this._normalizeMissingResources(data?.missingResources ?? data?.MissingResources);
        const recommended = this._normalizeRecommendedTemplate(data?.recommendedTemplate ?? data?.RecommendedTemplate);
        const candidates = this._normalizeTemplateCandidates(data?.templateCandidates ?? data?.TemplateCandidates);
        const requirementBrief = this._normalizeRequirementBrief(data?.requirementBrief ?? data?.RequirementBrief ?? null);
        const nonBlockingFields = requirementBrief?.nonBlockingMissingFields || [];
        const generationMode = this._getGenerationMode(data);
        const templateLockLevel = this._getTemplateLockLevel(data);
        const operators = this._getPendingOperatorSourceOperators(flow || data?.flow || data?.Flow || null);
        const pendingGroups = this._collectPendingDraftGroups(pending, operators);
        const effectivePending = pendingGroups.map(group => ({
            operatorId: group.operatorId,
            parameterNames: group.fields.map(field => field.parameterName)
        }));
        const hasTemplateStrategy = Boolean(recommended || candidates.length > 0 || generationMode || templateLockLevel);

        if (!hasTemplateStrategy && pendingGroups.length === 0 && missing.length === 0 && nonBlockingFields.length === 0) {
            container.classList.add('is-empty');
            container.innerHTML = '<div class="ai-followup-empty">当前没有待确认参数或缺失资源。</div>';
            return;
        }

        const followupText = this._buildFollowupHintText({ recommended, pending: effectivePending, missing, operators, nonBlockingFields });
        const strategyText = this._formatTemplateStrategy(generationMode, templateLockLevel);
        const primaryTemplate = recommended || candidates[0] || null;
        const candidatesHtml = candidates.length > 0
            ? `
                <div class="ai-followup-template-candidates">
                    ${candidates.map((candidate, index) => {
                        const confidence = Number.isFinite(candidate.confidence) && candidate.confidence > 0
                            ? ` · ${(candidate.confidence * 100).toFixed(0)}%`
                            : '';
                        const meta = [candidate.scenarioKey, candidate.industry, candidate.templateVersion ? `v${candidate.templateVersion}` : '']
                            .filter(Boolean)
                            .join(' · ');
                        return `
                            <div class="ai-followup-template-candidate">
                                <div class="ai-followup-template-candidate-main">
                                    <div class="ai-followup-template-name">${this._escapeHtml(candidate.templateName)}</div>
                                    <div class="ai-followup-template-reason">${this._escapeHtml(candidate.matchReason || `候选 ${index + 1}`)}${this._escapeHtml(confidence)}</div>
                                    ${meta ? `<div class="ai-followup-item-meta">${this._escapeHtml(meta)}</div>` : ''}
                                </div>
                                <div class="ai-followup-template-actions">
                                    <button class="ai-followup-template-action" type="button" data-template-action="fill" data-template-id="${this._escapeHtml(candidate.templateId)}" data-scenario-key="${this._escapeHtml(candidate.scenarioKey)}" data-template-name="${this._escapeHtml(candidate.templateName)}">严格沿用</button>
                                    <button class="ai-followup-template-action" type="button" data-template-action="adapt" data-template-id="${this._escapeHtml(candidate.templateId)}" data-scenario-key="${this._escapeHtml(candidate.scenarioKey)}" data-template-name="${this._escapeHtml(candidate.templateName)}">参考改造</button>
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
                        <div class="ai-followup-template-name">${this._escapeHtml(primaryTemplate.templateName)}</div>
                        <div class="ai-followup-template-reason">${this._escapeHtml(primaryTemplate.matchReason || '建议延续当前模板骨架继续补齐缺失项。')}</div>
                        ${primaryTemplate.matchMode ? `<div class="ai-followup-item-meta">匹配模式：${this._escapeHtml(primaryTemplate.matchMode)}</div>` : ''}
                        ${primaryTemplate.matchedFields && primaryTemplate.matchedFields.length > 0 ? `<div class="ai-followup-item-meta">匹配字段：${this._escapeHtml(primaryTemplate.matchedFields.join('、'))}</div>` : ''}
                        ${primaryTemplate.missingSignals && primaryTemplate.missingSignals.length > 0 ? `<div class="ai-followup-item-meta">缺失信号：${this._escapeHtml(primaryTemplate.missingSignals.join('、'))}</div>` : ''}
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

        const pendingHtml = pendingGroups.length > 0
            ? `
                <div class="ai-followup-section">
                    <div class="ai-followup-section-header">
                        <div class="ai-followup-section-label">待确认参数</div>
                        <div class="ai-followup-section-tip">点击可跳到下方填写区</div>
                    </div>
                    <div class="ai-followup-list">
                        ${pendingGroups.map(group => {
                            return `
                            <button class="ai-followup-item ai-followup-nav" type="button" data-followup-nav="${this._escapeHtml(group.groupKey)}">
                                <div class="ai-followup-item-title">${this._escapeHtml(group.label)}</div>
                                <div class="ai-followup-item-body">需要补充：${this._escapeHtml(group.fields.map(field => field.parameterName).join('、'))}</div>
                            </button>
                        `;
                        }).join('')}
                    </div>
                </div>
            `
            : '';

        const missingHtml = missing.length > 0
            ? `
                <div class="ai-followup-section">
                    <div class="ai-followup-section-label">缺失资源</div>
                    <div class="ai-followup-list">
                        ${missing.map(item => `
                            <div class="ai-followup-item">
                                <div class="ai-followup-item-title">${this._escapeHtml(item.resourceType || '资源')}</div>
                                <div class="ai-followup-item-body">${this._escapeHtml(item.description || item.resourceKey || '缺少必要资源')}</div>
                                ${item.resourceKey ? `<div class="ai-followup-item-meta">${this._escapeHtml(item.resourceKey)}</div>` : ''}
                            </div>
                        `).join('')}
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
            ${recommendedHtml}
            ${pendingHtml}
            ${missingHtml}
            ${nonBlockingHtml}
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
        const lines = ['请基于上一轮流程继续完善，优先补齐待确认参数和缺失资源，不要重建无关结构。'];

        if (recommended?.templateName) {
            lines.push(`优先沿用模板：${recommended.templateName}${recommended.matchReason ? `（${recommended.matchReason}）` : ''}。`);
        }

        if (pending.length > 0) {
            lines.push('待确认参数：');
            pending.forEach(item => {
                const label = this._resolvePendingOperatorLabel(item.operatorId, operators);
                const filledPairs = [];
                const missingNames = [];
                const context = this._resolvePendingOperatorContext(item.operatorId, operators);
                const metadata = this._getCachedOperatorMetadata(context.operatorType);

                item.parameterNames.forEach(parameterName => {
                    const fieldType = this._normalizePendingFieldType(this._findMetadataParameter(metadata, parameterName));
                    const value = this._getPendingDraftConfirmedValue(item.operatorId, parameterName);
                    if (this._hasPendingDraftValue(value, fieldType)) {
                        filledPairs.push(`${parameterName}=${this._stringifyPendingDraftValue(value, fieldType)}`);
                    } else {
                        missingNames.push(parameterName);
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
                const name = item.resourceType || '资源';
                const detail = item.description || item.resourceKey || '缺少必要资源';
                lines.push(`- ${name}${item.resourceKey ? `（${item.resourceKey}）` : ''}：${detail}`);
            });
        }

        if (Array.isArray(nonBlockingFields) && nonBlockingFields.length > 0) {
            lines.push(`非阻断待补字段：${nonBlockingFields.map(field => this._getRequirementFieldLabel(field)).join('、')}`);
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
        this.container.querySelectorAll('.ai-followup-action').forEach(btnEl => {
            btnEl.disabled = busy;
        });
        this.container.querySelectorAll('[data-draft-input="true"], [data-draft-file-pick], [data-followup-nav], [data-draft-adopt]').forEach(el => {
            el.disabled = busy;
        });
        const clearHintBtn = this.container.querySelector('#ai-btn-clear-followup-hint');
        if (clearHintBtn) clearHintBtn.disabled = busy;
        const input = this.container.querySelector('#ai-input');
        if(input) input.disabled = busy;
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

        const normalizedText = String(text || '').trim();
        note.className = 'ai-result-status-note';
        if (!normalizedText) {
            note.textContent = '';
            note.hidden = true;
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

    _resetClarificationSelectionDraft() {
        this._clarificationSelectionDraft = {};
        this._lastClarificationDraftText = '';
    }

    _buildClarificationAnswerDraft(selection = this._clarificationSelectionDraft) {
        const entries = Object.entries(selection || {})
            .map(([field, value]) => [String(field || '').trim(), String(value || '').trim()])
            .filter(([field, value]) => field && value);

        if (entries.length === 0) return '';

        return [
            '澄清回答：',
            ...entries.map(([field, value]) => `${this._getRequirementFieldLabel(field)}：${value}`)
        ].join('\n');
    }

    _mergeClarificationDraftIntoInput(draftText) {
        const input = this.container?.querySelector('#ai-input');
        if (!input) return;

        const nextDraft = String(draftText || '').trim();
        if (!nextDraft) return;

        const previousDraft = String(this._lastClarificationDraftText || '').trim();
        const current = String(input.value || '').trim();
        const nextValue = previousDraft && current.includes(previousDraft)
            ? current.replace(previousDraft, nextDraft).trim()
            : current
                ? `${current}\n\n${nextDraft}`
                : nextDraft;

        this._lastClarificationDraftText = nextDraft;
        input.value = nextValue;
        input.focus();
        input.style.height = 'auto';
        input.style.height = `${input.scrollHeight}px`;
    }

    _updateClarificationSendButtonState() {
        const button = this.container?.querySelector('#ai-btn-send-clarification');
        if (!button) return;

        const hasDraft = Boolean(this._buildClarificationAnswerDraft());
        button.disabled = this.isGenerating || !hasDraft;
        button.setAttribute('aria-disabled', button.disabled ? 'true' : 'false');
    }

    _syncClarificationOptionPressedStates(field, selectedValue) {
        if (!this.container?.querySelectorAll) return;

        this.container.querySelectorAll('[data-clarification-field][data-clarification-value]').forEach(button => {
            const sameField = button.getAttribute('data-clarification-field') === field;
            const sameValue = button.getAttribute('data-clarification-value') === selectedValue;
            const selected = sameField && sameValue;
            button.classList.toggle('is-selected', selected);
            button.setAttribute('aria-pressed', selected ? 'true' : 'false');
        });
    }

    _handleClarificationOptionSelection(button) {
        if (!button) return;

        const field = button.getAttribute('data-clarification-field') || '';
        const value = button.getAttribute('data-clarification-value') || '';
        if (!field || !value) return;

        this._clarificationSelectionDraft = {
            ...(this._clarificationSelectionDraft || {}),
            [field]: value
        };
        this._syncClarificationOptionPressedStates(field, value);
        const draftText = this._buildClarificationAnswerDraft();
        this._mergeClarificationDraftIntoInput(draftText);
        this._updateClarificationSendButtonState();
        this._addMessage('system', `已选择「${value}」，并生成澄清回答草稿。`);
    }

    _bindClarificationOptionButtons(root) {
        if (!root?.querySelectorAll) return;

        root.querySelectorAll('[data-clarification-field][data-clarification-value]').forEach(button => {
            button.addEventListener('click', () => this._handleClarificationOptionSelection(button));
        });
        this._updateClarificationSendButtonState();
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
        this._resetClarificationSelectionDraft();
        this._renderAgentRuntime(null, { reset: true });
        this._setResultStatusNote('', '');
        this._streamBuffer = { thinking: '', content: '' };
        this._streamFlushPending = false;
    }
    
    _scrollToBottom() {
        const container = this.container.querySelector('#ai-chat-container');
        if(container) container.scrollTop = container.scrollHeight;
    }

    _sanitizePath(path) {
        if (!path || typeof path !== 'string') return path;
        // Replace Windows absolute paths (including spaces, Unicode, parentheses)
        return path.replace(/[A-Z]:\\[^\s"'\]]+/gi, '<local-path>')
            .replace(/\/home\/[^\s"'\]]+/gi, '<local-path>')
            .replace(/\/Users\/[^\s"'\]]+/gi, '<local-path>');
    }

    _sanitizePromptTraceForNormalMode(trace) {
        if (!trace || typeof trace !== 'object') return trace;
        return {
            ...trace,
            systemPrompt: this._sanitizePath(trace.systemPrompt || ''),
            userPrompt: this._sanitizePath(trace.userPrompt || ''),
            baseUrl: this._sanitizePath(trace.baseUrl || '')
        };
    }

    // ── 缺失字段动作映射 ─────────────────────────────────────

    _getMissingFactAction(fact) {
        const lower = (fact || '').toLowerCase();
        if (lower.includes('modelpath') || lower.includes('模型路径') || lower.includes('模型文件')) {
            return { label: '选择模型文件', action: 'pick_model' };
        }
        if (lower.includes('labelspath') || lower.includes('标签')) {
            return { label: '选择标签文件', action: 'pick_labels' };
        }
        if (lower.includes('roi') || lower.includes('区域')) {
            return { label: '绘制 ROI', action: 'draw_roi', disabled: true, tip: 'ROI 编辑器即将推出' };
        }
        if (lower.includes('plc') || lower.includes('modbus')) {
            return { label: '配置 PLC', action: 'configure_plc', disabled: true, tip: 'PLC 配置即将推出' };
        }
        if (lower.includes('阈值') || lower.includes('threshold')) {
            return { label: '填写阈值', action: 'fill_threshold' };
        }
        return null;
    }

    // ── 缺失字段带动作按钮 ───────────────────────────────────

    _renderMissingFactsWithActions(missingFacts) {
        if (!Array.isArray(missingFacts) || missingFacts.length === 0) {
            return '<div class="ai-requirement-brief-empty">当前没有待确认项。</div>';
        }

        return `<div class="ai-requirement-brief-tags">${missingFacts.map(fact => {
            const action = this._getMissingFactAction(fact);
            if (action && !action.disabled) {
                return `<span class="ai-requirement-brief-tag is-missing ai-requirement-tag-with-action">
                    ${this._escapeHtml(String(fact))}
                    <button class="ai-requirement-tag-action" type="button" data-gap-action="${this._escapeHtml(action.action)}" data-gap-fact="${this._escapeHtml(String(fact))}">${this._escapeHtml(action.label)}</button>
                </span>`;
            }
            if (action && action.disabled) {
                return `<span class="ai-requirement-brief-tag is-missing ai-requirement-tag-with-action" title="${this._escapeHtml(action.tip || '')}">
                    ${this._escapeHtml(String(fact))}
                    <span class="ai-requirement-tag-action is-disabled">${this._escapeHtml(action.label)}</span>
                </span>`;
            }
            return `<span class="ai-requirement-brief-tag is-missing">${this._escapeHtml(String(fact))}</span>`;
        }).join('')}</div>`;
    }

    _setupScrollListener() {
        const container = this.container?.querySelector('#ai-chat-container');
        if (!container) return;
        this._chatContainer = container;

        container.addEventListener('scroll', () => {
            if (this._scrollStateRaf) return;
            this._scrollStateRaf = window.requestAnimationFrame(() => {
                this._scrollStateRaf = 0;
                this._syncScrollFollowState();
            });
        }, { passive: true });

        this._createScrollBottomBtn();
    }

    _setupComposerLayoutSync() {
        const pane = this.container?.querySelector('.ai-pane-left');
        const input = this.container?.querySelector('.ai-input-section');
        if (!pane || !input) return;

        this._syncComposerOffset();

        if (typeof ResizeObserver === 'undefined') {
            window.addEventListener('resize', () => this._syncComposerOffset(), { passive: true });
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

            window.requestAnimationFrame(() => this._syncComposerOffset());
            window.setTimeout(() => this._syncComposerOffset(), 260);
        };

        toggle.addEventListener('click', toggleExamples);
    }
}

Object.assign(
    AiPanel.prototype,
    aiPanelWorkbenchMixin,
    aiPanelPendingParametersMixin,
    aiPanelChatMixin,
    aiPanelValidationPreviewMixin,
    aiPanelGenerateRequestMixin,
    aiPanelRequirementBriefMixin,
    aiPanelAttachmentsMixin,
    aiPanelSessionHistoryMixin,
    aiPanelApplyPreviewMixin,
    aiPanelTopologySummaryMixin
);

