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

    _dispatchGenerateRequest({
        description,
        hint = '',
        userMessage = '',
        attachmentPaths = [],
        existingFlowJson = null,
        explicitMode = '',
        templateSelection = null,
        clearInput = true
    }) {
        const input = this.container.querySelector('#ai-input');
        const normalizedDescription = String(description || '').trim();
        const normalizedHint = String(hint || '').trim();
        const requestId = this._createGenerateRequestId();

        if (!normalizedDescription) {
            this._addMessage('system', '请输入需求描述。');
            return false;
        }

        if (this.isGenerating) return false;

        this.lastUserPrompt = String(userMessage || normalizedDescription).trim();
        this._setGeneratingState(true);
        this._setWorkbenchState(AiWorkbenchStates.GENERATING);
        this.activeGenerateRequestId = requestId;
        this.activeGenerateSessionId = this.sessionId;
        this.isCancellingGenerate = false;
        this.pendingManualRetry = null;
        this._renderManualRetryBanner();
        this._streamBuffer = { thinking: '', content: '' };
        this._streamFlushPending = false;

        if (attachmentPaths.length > 0) {
            this.attachments = this.attachments.map(item =>
                item.status === 'skipped'
                    ? item
                    : { ...item, status: 'pending', reason: '' });
            this._renderAttachments();
        }

        this._addMessage('user', userMessage || normalizedDescription);
        this._startAssistantTurn();

        const hasExistingFlowOverride = existingFlowJson !== null && existingFlowJson !== undefined;
        const hasCurrentFlowContext = hasExistingFlowOverride
            ? this._hasMeaningfulFlowPayload(existingFlowJson)
            : this._hasCurrentFlowContext();
        const resolvedMode = this._resolveGenerateRequestMode(explicitMode, normalizedDescription, hasCurrentFlowContext);
        const shouldIncludeFlowPayload = this._shouldIncludeCurrentFlowPayload(
            resolvedMode,
            normalizedDescription,
            hasCurrentFlowContext
        );
        const currentFlowPayload = shouldIncludeFlowPayload
            ? (hasExistingFlowOverride ? existingFlowJson : this._getCurrentFlowJson())
            : null;
        const flowPayload = shouldIncludeFlowPayload ? currentFlowPayload : null;
        const normalizedTemplateSelection = this._normalizeTemplateSelection(templateSelection);
        const agentGenerateFlowPayload = this._buildAgentGenerateFlowRequestPayload();
        this._renderAgentRuntime(this._buildOutgoingRuntimePayload(resolvedMode));

        if (this.currentResult?.flow) {
            this._setResultStatusNote('正在生成新一轮方案，右侧暂时保留上一版可应用结果。', 'info');
        } else {
            this._setResultStatusNote('', '');
        }

        try {
            webMessageBridge.sendMessage('GenerateFlow', {
                payload: {
                    description: normalizedDescription,
                    hint: normalizedHint || null,
                    mode: resolvedMode,
                    requirementMode: this.requirementMode,
                    templateSelection: normalizedTemplateSelection,
                    debugPrompt: this._shouldRequestPromptTrace(),
                    requestId,
                    sessionId: this.sessionId,
                    existingFlowJson: flowPayload,
                    attachments: attachmentPaths,
                    ...agentGenerateFlowPayload
                }
            });
            this.nextHintDraft = '';
            this.nextTemplateSelection = null;
            this._renderQueuedHintBanner();
            if (clearInput && input) {
                input.value = '';
                input.style.height = 'auto';
            }
            return true;
        } catch (err) {
            this._handleError(err.message);
            return false;
        }
    }

    _hasCurrentFlowContext() {
        if (this._hasMeaningfulFlowPayload(this.currentResult?.flow || this.currentResult?.Flow || null)) {
            return true;
        }

        if (this.flowCanvas?.nodes instanceof Map && this.flowCanvas.nodes.size > 0) {
            return true;
        }

        if (Array.isArray(this.flowCanvas?.connections) && this.flowCanvas.connections.length > 0) {
            return true;
        }

        return false;
    }

    _hasMeaningfulFlowPayload(flow) {
        if (!flow) return false;

        let parsed = flow;
        if (typeof flow === 'string') {
            try {
                parsed = JSON.parse(flow);
            } catch {
                return false;
            }
        }

        const operators = parsed?.operators || parsed?.Operators || parsed?.nodes || parsed?.Nodes || [];
        const connections = parsed?.connections || parsed?.Connections || [];
        return (Array.isArray(operators) && operators.length > 0) ||
            (Array.isArray(connections) && connections.length > 0);
    }

    _shouldIncludeCurrentFlowPayload(resolvedMode = '', description = '', hasCurrentFlowContext = false) {
        if (!hasCurrentFlowContext) return false;

        const normalizedMode = String(resolvedMode || '').trim().toLowerCase();
        if (normalizedMode === 'new') return false;

        if (['modify', 'explain', 'review_pending_parameters'].includes(normalizedMode)) {
            return true;
        }

        return this._looksLikeExistingFlowEditRequest(description) ||
            this._looksLikeModifyRequest(description) ||
            this._looksLikeExplainRequest(description);
    }

    _resolveGenerateRequestMode(explicitMode = '', description = '', hasCurrentFlowContext = false) {
        const normalizedExplicitMode = String(explicitMode || '').trim().toLowerCase();
        if (normalizedExplicitMode) {
            return normalizedExplicitMode;
        }

        if (hasCurrentFlowContext && this._looksLikeExistingFlowEditRequest(description)) {
            return 'modify';
        }

        if (hasCurrentFlowContext && this._looksLikeExplicitNewFlowRequest(description)) {
            return 'new';
        }

        if (hasCurrentFlowContext && this._looksLikeExplainRequest(description)) {
            return 'explain';
        }

        if (hasCurrentFlowContext && this._looksLikeModifyRequest(description)) {
            return 'modify';
        }

        if (hasCurrentFlowContext && this._looksLikeStandaloneVisionRequest(description)) {
            return 'new';
        }

        return 'auto';
    }

    _looksLikeExplicitNewFlowRequest(description = '') {
        const text = String(description || '').trim().toLowerCase();
        if (!text) return false;

        if (this._looksLikeExistingFlowEditRequest(text)) {
            return false;
        }

        const hardNewSignals = [
            '新流程', '新的流程', '重新', '从头', '重做', '重新做', '另一个流程',
            'new flow', 'new workflow', 'create flow', 'create workflow', 'start over', 'from scratch', 'rebuild'
        ];
        if (hardNewSignals.some(signal => text.includes(signal))) {
            return true;
        }

        return /(新增|新建|创建|生成|构建|搭建|设计).{0,12}(流程|工程|检测|测量|识别|方案)/.test(text);
    }

    _looksLikeExistingFlowEditRequest(description = '') {
        const text = String(description || '').trim().toLowerCase();
        if (!text) return false;

        const existingFlowAnchors = [
            '当前流程', '当前工程', '当前方案', '现有流程', '现有工程', '已有流程', '已有工程',
            '这个流程', '这个工程', '原流程', '原工程', '现在的流程', '现在的工程',
            'current flow', 'existing flow', 'this flow'
        ];
        const flowEditTargets = [
            '算子', '节点', '参数', '阈值', '连线', '连接', '名称', 'displayname',
            'operator', 'node', 'parameter', 'threshold', 'connection'
        ];
        const anchoredEdit = existingFlowAnchors.some(signal => text.includes(signal)) &&
            flowEditTargets.some(signal => text.includes(signal));

        return anchoredEdit ||
            /(新增|新建|增加|添加|删除|移除|修改|调整|改).{0,12}(算子|节点|参数|阈值|连线|连接|名称|displayname)/.test(text);
    }

    _looksLikeModifyRequest(description = '') {
        const text = String(description || '').trim().toLowerCase();
        if (!text) return false;

        return [
            '改', '修改', '调整', '优化', '调优', '增加', '新增', '新建', '补充', '删除', '删掉', '移除',
            '追加', '再加', '继续加', '加一个算子', '加个算子', '基于当前', '在当前', '沿用当前',
            '替换', '改成', '变成', '中文', '中文化', '阈值', '参数', '算子名称', 'displayname',
            'change', 'update', 'adjust', 'add', 'remove', 'replace', 'refine'
        ].some(signal => text.includes(signal));
    }

    _looksLikeExplainRequest(description = '') {
        const text = String(description || '').trim().toLowerCase();
        if (!text) return false;

        return [
            '解释', '说明', '讲解', '为什么', '什么意思', '含义', '原理', '思路',
            'explain', 'why', 'reason', 'meaning'
        ].some(signal => text.includes(signal));
    }

    _looksLikeStandaloneVisionRequest(description = '') {
        const text = String(description || '').trim().toLowerCase();
        if (!text) return false;

        const currentFlowAnchors = [
            '当前流程', '当前工程', '当前方案', '现有流程', '现有工程', '已有流程', '已有工程',
            '这个流程', '这个工程', '原流程', '原工程', '现在的流程', '现在的工程',
            'current flow', 'existing flow', 'this flow'
        ];
        if (currentFlowAnchors.some(signal => text.includes(signal))) {
            return false;
        }

        return [
            '检测', '测量', '识别', '缺陷', '外观', '表面', '划伤', '划痕', '裂纹', '破损',
            '压痕', '凹坑', '脏污', '污渍', '漏装', '有无', '线序', '端子', '孔距',
            '圆心距', '尺寸', '宽度', '高度', '直径', '角度', '面积', '二维码', '条码',
            'datamatrix', 'ocr', '字符', 'scratch', 'dent', 'defect', 'barcode', 'measure'
        ].some(signal => text.includes(signal));
    }

    _shouldRequestPromptTrace() {
        try {
            const search = new URLSearchParams(window.location.search || '');
            if (search.get('debugPrompt') === '1') {
                return true;
            }

            return localStorage.getItem('cv_ai_debug_prompt') === 'true';
        } catch {
            return false;
        }
    }

    _isAgentDeveloperControlsEnabled() {
        if (this.options?.enableVisionAgentGenerateFlowDevUi === true ||
            this.options?.visionAgentGenerateFlowDeveloperUi === true) {
            return true;
        }

        try {
            if (window?.__CLEARVISION_AGENT_DEV_UI__ === true) {
                return true;
            }

            const search = new URLSearchParams(window?.location?.search || '');
            const queryValue = search.get('visionAgentDev') || search.get('agentDev') || search.get('cvAgentDev');
            if (this._parseBooleanPreference(queryValue)) {
                return true;
            }
        } catch {
            // ignore browser context failures
        }

        try {
            return this._parseBooleanPreference(localStorage.getItem('cv_ai_agent_dev_ui'));
        } catch {
            return false;
        }
    }

    _parseBooleanPreference(value) {
        const normalized = String(value ?? '').trim().toLowerCase();
        return normalized === '1' || normalized === 'true' || normalized === 'yes' || normalized === 'on';
    }

    _normalizeAgentGenerateFlowMode(mode) {
        return String(mode || '').trim().toLowerCase() === 'planner' ? 'planner' : 'scripted';
    }

    _loadAgentGenerateFlowEnabled() {
        if (!this.isVisionAgentDeveloperUiEnabled) {
            return false;
        }

        if (this.options?.useVisionAgentGenerateFlow === true) {
            return true;
        }

        try {
            return this._parseBooleanPreference(localStorage.getItem('cv_ai_use_vision_agent_generate_flow'));
        } catch {
            return false;
        }
    }

    _loadAgentGenerateFlowMode() {
        const optionMode = this.options?.agentGenerateFlowMode;
        if (optionMode) {
            return this._normalizeAgentGenerateFlowMode(optionMode);
        }

        try {
            return this._normalizeAgentGenerateFlowMode(localStorage.getItem('cv_ai_agent_generate_flow_mode'));
        } catch {
            return 'scripted';
        }
    }

    _saveAgentGenerateFlowPreference() {
        if (!this.isVisionAgentDeveloperUiEnabled) {
            return;
        }

        try {
            localStorage.setItem('cv_ai_use_vision_agent_generate_flow', this.useVisionAgentGenerateFlow ? 'true' : 'false');
            localStorage.setItem('cv_ai_agent_generate_flow_mode', this._normalizeAgentGenerateFlowMode(this.agentGenerateFlowMode));
        } catch {
            // ignore localStorage failures
        }
    }

    _renderAgentDeveloperControls() {
        if (!this.isVisionAgentDeveloperUiEnabled) {
            return '';
        }

        const enabled = Boolean(this.useVisionAgentGenerateFlow);
        const mode = this._normalizeAgentGenerateFlowMode(this.agentGenerateFlowMode);
        return `
            <div class="ai-agent-dev-controls" id="ai-agent-dev-controls">
                <label class="ai-agent-dev-toggle">
                    <input id="ai-agent-generate-toggle" type="checkbox" ${enabled ? 'checked' : ''} />
                    <span>Agent GenerateFlow</span>
                </label>
                <div class="ai-agent-dev-mode-toggle" id="ai-agent-generate-mode-toggle" role="group" aria-label="Agent GenerateFlow 模式">
                    <button class="ai-mode-chip ${mode === 'scripted' ? 'is-active' : ''}" type="button" data-agent-generate-mode="scripted" ${enabled ? '' : 'disabled'}>scripted</button>
                    <button class="ai-mode-chip ${mode === 'planner' ? 'is-active' : ''}" type="button" data-agent-generate-mode="planner" ${enabled ? '' : 'disabled'}>planner</button>
                </div>
                <label class="ai-agent-dev-toggle ai-agent-preview-consent">
                    <input id="ai-agent-runtime-preview-consent" type="checkbox" ${enabled && this.runtimePreviewConsent ? 'checked' : ''} ${enabled ? '' : 'disabled'} />
                    <span>允许本轮 RuntimePreview</span>
                </label>
            </div>
        `;
    }

    _bindAgentDeveloperControls() {
        if (!this.isVisionAgentDeveloperUiEnabled) {
            return;
        }

        const toggle = this.container?.querySelector('#ai-agent-generate-toggle');
        const previewConsentToggle = this.container?.querySelector('#ai-agent-runtime-preview-consent');
        const modeButtons = Array.from(this.container?.querySelectorAll('[data-agent-generate-mode]') || []);
        const refresh = () => {
            const mode = this._normalizeAgentGenerateFlowMode(this.agentGenerateFlowMode);
            modeButtons.forEach(button => {
                const isActive = String(button.dataset.agentGenerateMode || '').toLowerCase() === mode;
                button.classList.toggle('is-active', isActive);
                button.disabled = !this.useVisionAgentGenerateFlow;
            });
            if (previewConsentToggle) {
                previewConsentToggle.disabled = !this.useVisionAgentGenerateFlow;
                previewConsentToggle.checked = Boolean(this.useVisionAgentGenerateFlow && this.runtimePreviewConsent);
            }
        };

        if (toggle) {
            toggle.checked = Boolean(this.useVisionAgentGenerateFlow);
            toggle.addEventListener('change', () => {
                this.useVisionAgentGenerateFlow = Boolean(toggle.checked);
                if (!this.useVisionAgentGenerateFlow) {
                    this.runtimePreviewConsent = false;
                }
                this._saveAgentGenerateFlowPreference();
                refresh();
            });
        }

        if (previewConsentToggle) {
            previewConsentToggle.checked = Boolean(this.useVisionAgentGenerateFlow && this.runtimePreviewConsent);
            previewConsentToggle.addEventListener('change', () => {
                this.runtimePreviewConsent = Boolean(previewConsentToggle.checked);
                refresh();
            });
        }

        modeButtons.forEach(button => {
            button.addEventListener('click', () => {
                if (!this.useVisionAgentGenerateFlow) {
                    return;
                }

                this.agentGenerateFlowMode = this._normalizeAgentGenerateFlowMode(button.dataset.agentGenerateMode);
                this._saveAgentGenerateFlowPreference();
                refresh();
            });
        });

        refresh();
    }

    _buildAgentGenerateFlowRequestPayload() {
        if (!this.isVisionAgentDeveloperUiEnabled || !this.useVisionAgentGenerateFlow) {
            this.runtimePreviewConsent = false;
            return {};
        }

        const payload = {
            useVisionAgentGenerateFlow: true,
            agentGenerateFlowMode: this._normalizeAgentGenerateFlowMode(this.agentGenerateFlowMode)
        };
        if (this.runtimePreviewConsent) {
            payload.runtimePreviewConsent = true;
            this.runtimePreviewConsent = false;
        }

        return payload;
    }

    _normalizeRequirementMode(mode) {
        return String(mode || '').trim().toLowerCase() === 'draft' ? 'draft' : 'strict';
    }

    _loadRequirementMode() {
        try {
            return this._normalizeRequirementMode(localStorage.getItem('cv_ai_requirement_mode'));
        } catch {
            return 'strict';
        }
    }

    _saveRequirementMode(mode) {
        try {
            localStorage.setItem('cv_ai_requirement_mode', this._normalizeRequirementMode(mode));
        } catch {
            // ignore localStorage failures
        }
    }

    _setRequirementMode(mode, { silent = false } = {}) {
        const normalized = this._normalizeRequirementMode(mode);
        if (normalized === this.requirementMode) {
            this._updateRequirementModeUI();
            return;
        }

        this.requirementMode = normalized;
        this._saveRequirementMode(normalized);
        this._updateRequirementModeUI();

        if (!silent) {
            const label = normalized === 'draft' ? '草稿优先' : '严格澄清';
            this._addMessage('system', `需求模式已切换为「${label}」。`);
        }
    }

    _updateRequirementModeUI() {
        const normalized = this._normalizeRequirementMode(this.requirementMode);
        const tip = this.container?.querySelector('#ai-requirement-mode-tip');
        const buttons = this.container?.querySelectorAll('[data-requirement-mode]');

        buttons?.forEach(button => {
            const buttonMode = this._normalizeRequirementMode(button.dataset.requirementMode);
            button.classList.toggle('is-active', buttonMode === normalized);
            button.setAttribute('aria-pressed', buttonMode === normalized ? 'true' : 'false');
        });

        if (tip) {
            tip.textContent = normalized === 'draft'
                ? '优先给出可运行初稿，缺项仍会以风险提示标注。'
                : '先确认关键字段，再进入正式生成。';
        }
    }

    async _handleGenerate() {
        const input = this.container.querySelector('#ai-input');
        const description = input.value.trim();
        const attachmentPaths = this.attachments.map(item => item.path);
        const hint = this.nextHintDraft.trim();
        const templateSelection = this.nextTemplateSelection ? { ...this.nextTemplateSelection } : null;
        const userMessage = attachmentPaths.length > 0
            ? `${description}\n\n[附件] ${this.attachments.map(item => item.name).join('，')}`
            : description;
        this._dispatchGenerateRequest({
            description,
            hint,
            userMessage,
            attachmentPaths,
            templateSelection,
            explicitMode: '',
            clearInput: true
        });
    }

    async _handlePendingParameterReview() {
        if (this.isGenerating) return;

        if (!this.currentResult?.flow) {
            this._addMessage('system', '当前没有可审核的方案，请先生成工程方案。');
            return;
        }

        const pending = this._resolvePendingParametersForDraft(this.currentResult);
        if (pending.length === 0) {
            this._addMessage('system', '当前没有待确认参数，无需提交 AI 审核。');
            return;
        }

        const operators = this._getPendingOperatorSourceOperators(this.currentResult.flow);
        const groups = this._collectPendingDraftGroups(pending, operators);
        if (groups.length === 0) {
            this._addMessage('system', '当前模式下没有需要补录的互斥参数，无需提交 AI 审核。');
            return;
        }

        const confirmationState = this._getPendingParameterConfirmationState(pending, operators, groups);
        if (!confirmationState.canReview) {
            this._addMessage('system', '请先确认全部参数，再提交审核。');
            return;
        }

        const reviewRequest = this._buildPendingParameterReviewRequest();
        if (!reviewRequest) {
            this._addMessage('system', '当前没有可提交的参数审核内容。');
            return;
        }

        this._dispatchGenerateRequest({
            description: '请审核并更新当前方案中的待确认参数，保持流程结构稳定，仅调整参数和必要补充信息。',
            hint: reviewRequest.hint,
            userMessage: reviewRequest.userMessage,
            existingFlowJson: reviewRequest.existingFlowJson,
            attachmentPaths: [],
            explicitMode: 'review_pending_parameters',
            clearInput: true
        });
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

    _handleCancelGenerate() {
        if (!this.isGenerating || this.isCancellingGenerate) return;

        const requestId = this.activeGenerateRequestId;
        const sessionId = this.activeGenerateSessionId || this.sessionId;
        if (!requestId) return;

        this.isCancellingGenerate = true;

        webMessageBridge.sendMessage('CancelGenerateFlow', {
            payload: {
                requestId,
                sessionId
            }
        });

        this._updateProgress({
            message: '正在取消生成...',
            phase: 'cancelling'
        });
        this._addMessage('system', '已发送取消请求，正在等待后端停止当前生成。');
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

    
    _handleApplyFlow() {
        if (!this.flowCanvas) return;
        if (!this.currentResult?.flow) {
            this._addMessage('system', '当前会话没有可应用的流程数据。');
            return;
        }

        const flow = this._buildFlowWithPendingDrafts(this.currentResult.flow);
        if (!flow) {
            this._addMessage('system', '当前会话没有可应用的流程数据。');
            return;
        }

        this._setWorkbenchState(AiWorkbenchStates.APPLYING);
        const applyRisk = this._buildApplyRiskSummary(this.currentResult);

        // Compute diff and show preview
        let currentFlow = null;
        try {
            currentFlow = this.flowCanvas.serialize();
        } catch {
            // Canvas may be empty
        }

        if (currentFlow) {
            const diff = this._computeFlowDiff(currentFlow, flow);
            const totalChanges = this._getApplyPreviewChangeCount(diff);
            if (totalChanges > 0 || applyRisk.hasWarnings) {
                this._showApplyPreview(diff, flow, { applyRisk });
                return;
            }
        }

        if (applyRisk.hasWarnings) {
            this._showApplyPreview(this._emptyFlowDiff(), flow, { applyRisk });
            return;
        }

        // No diff or no current flow - apply directly
        this._executeApplyFlow(flow);
    }
    

    

    

    
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

    _handleAttachmentClick() {
        if (this.isGenerating) return;
        webMessageBridge.sendMessage('PickFileCommand', {
            parameterName: 'aiAttachment',
            filter: 'Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*'
        });
    }

    _handleFilePickedEvent(data) {
        const payload = data?.payload || data || {};
        if (payload.parameterName === 'aiPendingParameterFile') {
            const context = this.pendingParameterFilePickContext;
            this.pendingParameterFilePickContext = null;
            if (!context || payload.isCancelled || !payload.filePath) return;
            this._setPendingDraftConfirmedValue(
                context.operatorId,
                context.parameterName,
                String(payload.filePath || '').trim(),
                'file',
                'user_input'
            );
            if (this.currentResult?.flow) {
                this._renderFollowupChecklist(this.currentResult, this.currentResult.flow);
                this._renderParameterDraftEditor(this.currentResult, this.currentResult.flow);
            }
            return;
        }

        if (payload.parameterName !== 'aiAttachment') return;
        if (payload.isCancelled || !payload.filePath) return;

        const normalizedPath = payload.filePath.trim();
        if (!normalizedPath) return;

        const exists = this.attachments.some(item =>
            item.path.toLowerCase() === normalizedPath.toLowerCase());
        if (exists) {
            this._addMessage('system', '该附件已存在，无需重复添加。');
            return;
        }

        const attachment = {
            path: normalizedPath,
            name: this._getFileName(normalizedPath),
            status: 'ready',
            reason: ''
        };
        this.attachments.push(attachment);
        this._renderAttachments();
        this._addMessage('system', `已添加附件：${attachment.name}`);
    }

    _handleAttachmentReport(data) {
        const payload = data?.payload || data || {};
        if (!this._shouldHandleGenerateRealtimePayload(payload)) return;

        // Cache for attachment panel
        this._lastAttachmentReport = payload;

        const sent = Array.isArray(payload.sent) ? payload.sent : [];
        const skipped = Array.isArray(payload.skipped) ? payload.skipped : [];

        if (sent.length === 0 && skipped.length === 0) return;

        const sentMap = new Map(sent
            .filter(item => item?.path)
            .map(item => [String(item.path).toLowerCase(), item]));
        const skippedMap = new Map(skipped
            .filter(item => item?.path)
            .map(item => [String(item.path).toLowerCase(), item]));

        this.attachments = this.attachments.map(item => {
            const key = item.path.toLowerCase();
            if (skippedMap.has(key)) {
                const skipInfo = skippedMap.get(key);
                return {
                    ...item,
                    status: 'skipped',
                    reason: this._formatSkipReason(skipInfo?.reason)
                };
            }
            if (sentMap.has(key)) {
                return {
                    ...item,
                    status: 'sent',
                    reason: ''
                };
            }
            return item;
        });

        this._renderAttachments();

        const sentNames = sent.map(item => item?.name).filter(Boolean);
        const skippedNames = skipped.map(item => {
            const name = item?.name || this._getFileName(item?.path || '');
            const reason = this._formatSkipReason(item?.reason);
            return reason ? `${name}(${reason})` : name;
        }).filter(Boolean);

        const sections = [];
        if (sentNames.length > 0) {
            sections.push(`已发送: ${sentNames.join('，')}`);
        }
        if (skippedNames.length > 0) {
            sections.push(`已跳过: ${skippedNames.join('，')}`);
        }
        if (sections.length > 0) {
            this._addMessage('system', `附件处理结果\n${sections.join('\n')}`);
        }
    }

    _removeAttachment(path) {
        this.attachments = this.attachments.filter(item => item.path !== path);
        this._renderAttachments();
    }

    _renderAttachments() {
        const container = this.container?.querySelector('#ai-attachments');
        if (!container) return;

        if (!this.attachments.length) {
            container.innerHTML = '';
            return;
        }

        const chips = this.attachments.map(item => {
            const title = item.reason ? `${item.path}\n${item.reason}` : item.path;
            const statusLabel = this._getAttachmentStatusLabel(item.status, item.reason);
            const statusClass = `status-${item.status || 'ready'}`;
            return `
                <div class="ai-attachment-chip" title="${this._escapeHtml(title)}">
                    <span class="ai-attachment-name">${this._escapeHtml(item.name)}</span>
                    <span class="ai-attachment-status ${statusClass}">${this._escapeHtml(statusLabel)}</span>
                    <button class="ai-attachment-remove" data-path="${this._escapeHtml(item.path)}" type="button" aria-label="remove attachment">×</button>
                </div>
            `;
        }).join('');

        container.innerHTML = `<div class="ai-attachment-list">${chips}</div>`;
        container.querySelectorAll('.ai-attachment-remove').forEach(btn => {
            btn.addEventListener('click', () => this._removeAttachment(btn.dataset.path || ''));
            btn.disabled = this.isGenerating;
        });
    }

    _getAttachmentStatusLabel(status, reason) {
        switch (status) {
            case 'pending': return '发送中';
            case 'sent': return '已发送';
            case 'skipped': return reason ? `已跳过(${reason})` : '已跳过';
            default: return '待发送';
        }
    }

    _formatSkipReason(reason) {
        switch (reason) {
            case 'file_missing': return '文件不存在';
            case 'unsupported_format': return '格式不支持';
            case 'file_too_large': return '文件过大';
            case 'read_failed': return '读取失败';
            case 'limit_exceeded': return '超出数量上限';
            case 'model_not_support_image': return '当前模型不支持图片';
            default: return reason || '';
        }
    }

    _getFileName(filePath) {
        const parts = String(filePath || '').split(/[/\\]/);
        return parts[parts.length - 1] || filePath;
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

    _createGenerateRequestId() {
        const randomPart = Math.random().toString(36).slice(2, 8);
        return `gen-${Date.now()}-${randomPart}`;
    }

    _getGenerateRequestId(payload) {
        return String(payload?.requestId ?? payload?.RequestId ?? '').trim();
    }

    _shouldHandleGenerateRealtimePayload(payload) {
        const requestId = this._getGenerateRequestId(payload);
        if (!requestId) {
            return this.isGenerating;
        }

        return Boolean(this.activeGenerateRequestId) && requestId === this.activeGenerateRequestId;
    }

    _shouldHandleGenerateTerminalPayload(payload) {
        const requestId = this._getGenerateRequestId(payload);
        if (!requestId) {
            return this.isGenerating;
        }

        return Boolean(this.activeGenerateRequestId) && requestId === this.activeGenerateRequestId;
    }

    _normalizeGenerateStatus(payload) {
        return String(payload?.status ?? payload?.Status ?? '').trim().toLowerCase();
    }

    _isCancelledResult(payload) {
        const status = this._normalizeGenerateStatus(payload);
        const failureType = String(payload?.failureType ?? payload?.FailureType ?? '').trim().toLowerCase();

        return ['cancelled', 'canceled', 'user_cancelled', 'user_canceled'].includes(status)
            || ['user_cancelled', 'user_canceled'].includes(failureType);
    }

    _isClarificationResult(payload) {
        const status = this._normalizeGenerateStatus(payload);
        const failureType = String(payload?.failureType ?? payload?.FailureType ?? '').trim().toLowerCase();
        const clarificationRequired = Boolean(payload?.clarificationRequired ?? payload?.ClarificationRequired);

        return clarificationRequired
            || status === 'clarification_required'
            || failureType === 'clarification_required';
    }

    _getTurnIntent(payload) {
        return String(payload?.turnIntent ?? payload?.TurnIntent ?? '').trim().toLowerCase();
    }

    _getInteractionState(payload) {
        return String(payload?.interactionState ?? payload?.InteractionState ?? '').trim().toLowerCase();
    }

    _getRouterConfidence(payload) {
        return String(payload?.routerConfidence ?? payload?.RouterConfidence ?? '').trim().toLowerCase();
    }

    _buildOutgoingRuntimePayload(resolvedMode) {
        const normalizedMode = String(resolvedMode || 'auto').trim().toLowerCase();
        const turnIntent = normalizedMode === 'modify'
            ? 'modify_flow'
            : normalizedMode === 'explain'
                ? 'explain_flow'
                : normalizedMode === 'review_pending_parameters'
                    ? 'review_pending_parameters'
                    : normalizedMode === 'new'
                        ? 'new_flow'
                        : 'unknown';
        const interactionState = turnIntent === 'modify_flow'
            ? 'modifying'
            : turnIntent === 'review_pending_parameters'
                ? 'reviewing_parameters'
                : 'generating';

        return {
            turnIntent,
            interactionState,
            routerConfidence: '',
            blockingClarificationFields: [],
            nonBlockingMissingFields: []
        };
    }

    _isInteractionOnlyResult(payload) {
        const turnIntent = this._getTurnIntent(payload);
        const interactionState = this._getInteractionState(payload);
        const flow = payload?.flow ?? payload?.Flow ?? null;
        const hasReply = Boolean(payload?.aiExplanation ?? payload?.AiExplanation ?? payload?.message ?? payload?.errorMessage);

        return Boolean(payload?.success ?? payload?.Success)
            && !flow
            && !this._isClarificationResult(payload)
            && hasReply
            && (turnIntent === 'chat_or_help' || turnIntent === 'unknown' || interactionState === 'idle');
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
    }
    
    _addToHistory(entry) {
        const normalized = this._normalizeSessionSummary(entry);
        if (!normalized) return;

        this.history = [normalized, ...this.history.filter(item => item.sessionId !== normalized.sessionId)]
            .sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime());
        this._filterHistory(this.historyKeyword);
    }

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
    }

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
    }
    
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
    }

    _formatHistoryTime(value) {
        const timestamp = new Date(value);
        if (Number.isNaN(timestamp.getTime())) return '--';
        return timestamp.toLocaleString();
    }
    
    _loadHistory() {
        webMessageBridge.sendMessage('ListAiSessions');
    }

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
    }

    _switchToSession(sessionId) {
        if (!sessionId) return;
        if (this.isGenerating) {
            this._addMessage('system', '正在生成中，暂时无法切换历史会话。');
            return;
        }

        webMessageBridge.sendMessage('GetAiSession', {
            payload: { sessionId }
        });
    }

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
        const restoredResult = {
            flow: canvasFlow || parsedFlow || null,
            aiExplanation: parsedAiFlow?.explanation || parsedAiFlow?.Explanation ||
                parsedFlow?.explanation || parsedFlow?.Explanation ||
                latestAssistantPayload?.aiExplanation || latestAssistantPayload?.AiExplanation ||
                latestAssistantPayload?.reply || latestAssistantPayload?.Reply || '--',
            reasoning: parsedAiFlow?.reasoning || parsedAiFlow?.Reasoning ||
                latestAssistantPayload?.reasoning || latestAssistantPayload?.Reasoning || '',
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
    }

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
    }

    _extractOperators(flow) {
        if (!flow) return [];
        if (Array.isArray(flow.operators)) return flow.operators;
        if (Array.isArray(flow.Operators)) return flow.Operators;
        return [];
    }

    _extractConnections(flow) {
        if (!flow) return [];
        if (Array.isArray(flow.connections)) return flow.connections;
        if (Array.isArray(flow.Connections)) return flow.Connections;
        return [];
    }

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
    }

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
    }

    _deleteSession(sessionId) {
        if (!sessionId) return;
        webMessageBridge.sendMessage('DeleteAiSession', {
            payload: { sessionId }
        });
    }

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
    }

    _loadSessionId() {
        try {
            return localStorage.getItem(this.sessionStorageKey);
        } catch {
            return null;
        }
    }

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

    _renderAttachmentPanel() {
        const card = this.container?.querySelector('#ai-result-attachment-card');
        const container = this.container?.querySelector('#ai-result-attachments');
        if (!card || !container) return;

        const report = this._lastAttachmentReport;
        const attachments = this.attachments || [];
        const supportsVision = this._lastModelSupportsVision;

        if (!report && attachments.length === 0) {
            card.hidden = true;
            container.innerHTML = '';
            return;
        }

        card.hidden = false;
        const sections = [];

        // Model vision capability
        if (supportsVision === false) {
            sections.push(`
                <div class="ai-attachment-vision-warning">
                    当前模型不支持视觉输入，附件仅用于元信息分析，不会发送图片给模型。
                </div>
            `);
        } else if (supportsVision === true) {
            sections.push(`
                <div class="ai-attachment-vision-ok">
                    当前模型支持视觉输入，图片已发送给模型分析。
                </div>
            `);
        }

        // Sent attachments
        const sent = report?.sent || report?.Sent || [];
        if (sent.length > 0) {
            sections.push(`
                <div class="ai-attachment-section">
                    <div class="ai-attachment-section-header">已发送 (${sent.length})</div>
                    ${sent.map(item => `
                        <div class="ai-attachment-item is-sent">
                            <span class="ai-attachment-icon">&#128206;</span>
                            <span class="ai-attachment-name">${this._escapeHtml(item.name || item.Name || '未知文件')}</span>
                        </div>
                    `).join('')}
                </div>
            `);
        }

        // Skipped attachments
        const skipped = report?.skipped || report?.Skipped || [];
        if (skipped.length > 0) {
            sections.push(`
                <div class="ai-attachment-section">
                    <div class="ai-attachment-section-header">已跳过 (${skipped.length})</div>
                    ${skipped.map(item => `
                        <div class="ai-attachment-item is-skipped">
                            <span class="ai-attachment-icon">&#9888;</span>
                            <span class="ai-attachment-name">${this._escapeHtml(item.name || item.Name || '未知文件')}</span>
                            <span class="ai-attachment-reason">${this._escapeHtml(item.reason || item.Reason || '未知原因')}</span>
                        </div>
                    `).join('')}
                </div>
            `);
        }

        // Pending attachments (no report yet)
        if (!report && attachments.length > 0) {
            sections.push(`
                <div class="ai-attachment-section">
                    <div class="ai-attachment-section-header">附件 (${attachments.length})</div>
                    ${attachments.map(item => `
                        <div class="ai-attachment-item is-${this._escapeHtml(item.status || 'pending')}">
                            <span class="ai-attachment-icon">&#128206;</span>
                            <span class="ai-attachment-name">${this._escapeHtml(item.name || '未知文件')}</span>
                            ${item.reason ? `<span class="ai-attachment-reason">${this._escapeHtml(item.reason)}</span>` : ''}
                        </div>
                    `).join('')}
                </div>
            `);
        }

        container.innerHTML = sections.join('');
    }

    // ── 应用预览与撤销 ────────────────────────────────────────

    _computeFlowDiff(currentFlow, newFlow) {
        const currentOps = this._extractOperators(currentFlow);
        const newOps = this._extractOperators(newFlow);
        const currentConns = this._extractConnections(currentFlow);
        const newConns = this._extractConnections(newFlow);

        const opDiffKey = (op, index) => {
            const id = op.tempId || op.TempId || op.id || op.Id || '';
            if (id) return `id:${id}`;
            return `idx:${index}::type:${op.operatorType || op.OperatorType || op.type || op.Type || ''}`;
        };

        const currentOpMap = new Map();
        currentOps.forEach((op, index) => { currentOpMap.set(opDiffKey(op, index), op); });

        const newOpMap = new Map();
        newOps.forEach((op, index) => { newOpMap.set(opDiffKey(op, index), op); });

        const added = [];
        const removed = [];
        const modified = [];

        for (const [key, newOp] of newOpMap) {
            if (!currentOpMap.has(key)) {
                added.push(newOp);
            } else {
                const currentOp = currentOpMap.get(key);
                const currentParams = currentOp.parameters || currentOp.Parameters || {};
                const newParams = newOp.parameters || newOp.Parameters || {};
                const paramChanges = [];
                const currentDisplayName = currentOp.displayName || currentOp.DisplayName || currentOp.name || currentOp.Name || '';
                const newDisplayName = newOp.displayName || newOp.DisplayName || newOp.name || newOp.Name || '';
                const currentOperatorType = currentOp.operatorType || currentOp.OperatorType || currentOp.type || currentOp.Type || '';
                const newOperatorType = newOp.operatorType || newOp.OperatorType || newOp.type || newOp.Type || '';
                if (String(currentDisplayName) !== String(newDisplayName)) {
                    paramChanges.push({ name: 'displayName', old: currentDisplayName, new: newDisplayName });
                }
                if (String(currentOperatorType) !== String(newOperatorType)) {
                    paramChanges.push({ name: 'operatorType', old: currentOperatorType, new: newOperatorType });
                }
                const parameterNames = new Set([
                    ...Object.keys(currentParams),
                    ...Object.keys(newParams)
                ]);
                for (const pName of parameterNames) {
                    if (String(currentParams[pName] ?? '') !== String(newParams[pName] ?? '')) {
                        paramChanges.push({ name: pName, old: currentParams[pName], new: newParams[pName] });
                    }
                }
                if (paramChanges.length > 0) {
                    modified.push({ op: newOp, changes: paramChanges });
                }
            }
        }

        for (const [key, currentOp] of currentOpMap) {
            if (!newOpMap.has(key)) {
                removed.push(currentOp);
            }
        }

        const readConnEndpoint = (c, role) => {
            const prefix = role === 'source' ? 'source' : 'target';
            const pascalPrefix = role === 'source' ? 'Source' : 'Target';
            const operatorId = c[`${prefix}TempId`]
                || c[`${pascalPrefix}TempId`]
                || c[`${prefix}OperatorId`]
                || c[`${pascalPrefix}OperatorId`]
                || c[`${prefix}Id`]
                || c[`${pascalPrefix}Id`]
                || '';
            const portId = c[`${prefix}PortName`]
                || c[`${pascalPrefix}PortName`]
                || c[`${prefix}PortId`]
                || c[`${pascalPrefix}PortId`]
                || c[`${prefix}Port`]
                || c[`${pascalPrefix}Port`]
                || '';
            return `${operatorId}.${portId}`;
        };
        const connKey = c => `${readConnEndpoint(c, 'source')}::${readConnEndpoint(c, 'target')}`;
        const currentConnSet = new Set(currentConns.map(connKey));
        const newConnSet = new Set(newConns.map(connKey));
        const addedConnections = newConns.filter(c => !currentConnSet.has(connKey(c)));
        const removedConnections = currentConns.filter(c => !newConnSet.has(connKey(c)));

        return { added, removed, modified, addedConnections, removedConnections };
    }

    _emptyFlowDiff() {
        return {
            added: [],
            removed: [],
            modified: [],
            addedConnections: [],
            removedConnections: []
        };
    }

    _getApplyPreviewChangeCount(diff = {}) {
        return (diff.added?.length || 0)
            + (diff.removed?.length || 0)
            + (diff.modified?.length || 0)
            + (diff.addedConnections?.length || 0)
            + (diff.removedConnections?.length || 0);
    }

    _buildApplyRiskSummary(result = this.currentResult) {
        const pending = this._resolvePendingParametersForDraft(result);
        const missing = this._normalizeMissingResources(result?.missingResources ?? result?.MissingResources);
        const brief = this._normalizeRequirementBrief(result?.requirementBrief ?? result?.RequirementBrief ?? null);
        const nonBlockingFields = this._normalizeRuntimeFieldList(
            result?.nonBlockingMissingFields
            ?? result?.NonBlockingMissingFields
            ?? brief?.nonBlockingMissingFields
            ?? []
        );

        return {
            pending,
            missing,
            nonBlockingFields,
            hasWarnings: pending.length > 0 || missing.length > 0 || nonBlockingFields.length > 0,
            totalCount: pending.length + missing.length + nonBlockingFields.length
        };
    }

    _formatApplyPendingItem(item) {
        const operatorLabel = item.actualOperatorId || item.operatorId || '未定位算子';
        const names = item.parameterNames?.length > 0 ? item.parameterNames.join('、') : '待确认参数';
        return `${operatorLabel}：${names}`;
    }

    _renderApplyRiskSummary(applyRisk) {
        if (!applyRisk?.hasWarnings) return '';

        const pendingItems = (applyRisk.pending || [])
            .slice(0, 4)
            .map(item => `<li>${this._escapeHtml(this._formatApplyPendingItem(item))}</li>`)
            .join('');
        const missingItems = (applyRisk.missing || [])
            .slice(0, 4)
            .map(item => `<li>${this._escapeHtml(item.description || item.resourceKey || item.resourceType || '缺失资源')}</li>`)
            .join('');
        const nonBlockingItems = (applyRisk.nonBlockingFields || [])
            .slice(0, 6)
            .map(field => `<li>${this._escapeHtml(this._getRequirementFieldLabel(field))}</li>`)
            .join('');

        return `
            <section class="ai-apply-preview-risk">
                <div class="ai-apply-preview-risk-title">应用前检查</div>
                <div class="ai-apply-preview-risk-copy">
                    当前方案仍有 ${this._escapeHtml(String(applyRisk.totalCount))} 项上线前信息需要复核。可以先应用草稿，但运行前应补齐。
                </div>
                ${pendingItems ? `
                    <div class="ai-apply-preview-risk-group">
                        <div class="ai-apply-preview-risk-label">待确认参数</div>
                        <ul>${pendingItems}</ul>
                    </div>
                ` : ''}
                ${missingItems ? `
                    <div class="ai-apply-preview-risk-group">
                        <div class="ai-apply-preview-risk-label">缺失资源</div>
                        <ul>${missingItems}</ul>
                    </div>
                ` : ''}
                ${nonBlockingItems ? `
                    <div class="ai-apply-preview-risk-group">
                        <div class="ai-apply-preview-risk-label">非阻断待补</div>
                        <ul>${nonBlockingItems}</ul>
                    </div>
                ` : ''}
            </section>
        `;
    }

    _formatConnectionPreview(connection) {
        if (!connection) return '未知连线';

        const source = connection.sourceTempId
            || connection.SourceTempId
            || connection.sourceOperatorId
            || connection.SourceOperatorId
            || connection.sourceId
            || connection.SourceId
            || '?';
        const sourcePort = connection.sourcePortName
            || connection.SourcePortName
            || connection.sourcePortId
            || connection.SourcePortId
            || connection.sourcePort
            || connection.SourcePort
            || 'Output';
        const target = connection.targetTempId
            || connection.TargetTempId
            || connection.targetOperatorId
            || connection.TargetOperatorId
            || connection.targetId
            || connection.TargetId
            || '?';
        const targetPort = connection.targetPortName
            || connection.TargetPortName
            || connection.targetPortId
            || connection.TargetPortId
            || connection.targetPort
            || connection.TargetPort
            || 'Input';

        return `${source}.${sourcePort} -> ${target}.${targetPort}`;
    }

    _showApplyPreview(diff, newFlow, options = {}) {
        const totalChanges = this._getApplyPreviewChangeCount(diff);
        const applyRisk = options.applyRisk || this._buildApplyRiskSummary(this.currentResult);
        if (totalChanges === 0 && !applyRisk.hasWarnings) {
            this._executeApplyFlow(newFlow);
            return;
        }

        // Remove existing preview if any
        const existing = this.container.querySelector('.ai-apply-preview-overlay');
        if (existing) existing.remove();

        const overlay = document.createElement('div');
        overlay.className = 'ai-apply-preview-overlay';
        overlay.innerHTML = `
            <div class="ai-apply-preview-dialog">
                <div class="ai-apply-preview-header">
                    <span>应用预览</span>
                    <small>${this._escapeHtml(String(totalChanges))} 项变更 · ${this._escapeHtml(String(applyRisk.totalCount || 0))} 项待复核</small>
                    <button class="ai-apply-preview-close" type="button">&times;</button>
                </div>
                <div class="ai-apply-preview-body">
                    ${this._renderApplyRiskSummary(applyRisk)}
                    ${diff.added.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-add">新增算子 (${diff.added.length})</div>
                            ${diff.added.map(op => `<div class="ai-apply-preview-item is-add">+ ${this._escapeHtml(op.displayName || op.DisplayName || op.name || '未命名')}</div>`).join('')}
                        </div>
                    ` : ''}
                    ${diff.removed.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-remove">删除算子 (${diff.removed.length})</div>
                            ${diff.removed.map(op => `<div class="ai-apply-preview-item is-remove">- ${this._escapeHtml(op.displayName || op.DisplayName || op.name || '未命名')}</div>`).join('')}
                        </div>
                    ` : ''}
                    ${diff.modified.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-modify">参数变更 (${diff.modified.length})</div>
                            ${diff.modified.map(m => `
                                <div class="ai-apply-preview-item is-modify">
                                    ${this._escapeHtml(m.op.displayName || m.op.DisplayName || m.op.name || '未命名')}
                                    ${m.changes.map(c => `<div class="ai-apply-preview-param">${this._escapeHtml(c.name)}: ${this._escapeHtml(String(c.old ?? '--'))} &rarr; ${this._escapeHtml(String(c.new ?? '--'))}</div>`).join('')}
                                </div>
                            `).join('')}
                        </div>
                    ` : ''}
                    ${diff.addedConnections.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-add">新增连线 (${diff.addedConnections.length})</div>
                            ${diff.addedConnections.slice(0, 6).map(conn => `<div class="ai-apply-preview-item is-add">+ ${this._escapeHtml(this._formatConnectionPreview(conn))}</div>`).join('')}
                        </div>
                    ` : ''}
                    ${diff.removedConnections.length > 0 ? `
                        <div class="ai-apply-preview-section">
                            <div class="ai-apply-preview-section-title is-remove">删除连线 (${diff.removedConnections.length})</div>
                            ${diff.removedConnections.slice(0, 6).map(conn => `<div class="ai-apply-preview-item is-remove">- ${this._escapeHtml(this._formatConnectionPreview(conn))}</div>`).join('')}
                        </div>
                    ` : ''}
                </div>
                <div class="ai-apply-preview-actions">
                    <button class="ai-apply-preview-cancel" type="button">取消</button>
                    <button class="ai-apply-preview-confirm" type="button">确认应用到流程草稿</button>
                </div>
            </div>
        `;

        this.container.appendChild(overlay);

        const cancelPreview = () => {
            overlay.remove();
            this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
        };
        overlay.querySelector('.ai-apply-preview-close').addEventListener('click', cancelPreview);
        overlay.querySelector('.ai-apply-preview-cancel').addEventListener('click', cancelPreview);
        overlay.querySelector('.ai-apply-preview-confirm').addEventListener('click', () => {
            overlay.remove();
            this._executeApplyFlow(newFlow);
        });
    }

    _executeApplyFlow(flow) {
        if (!this.flowCanvas) return;
        try {
            // Snapshot before apply for undo
            this._preApplySnapshot = this.flowCanvas.serialize();
            this._preApplySnapshotVersion += 1;
            this._preApplyCanvasRevision = this.flowCanvas?.getFlowRevision?.() || 0;

            const flowBtn = document.querySelector('.nav-btn[data-view="flow"]');
            if (flowBtn) flowBtn.click();
            this.flowCanvas.deserialize(flow);
            this._markCurrentResultAppliedToCanvas();
            this._syncPendingParameterDrafts(this.currentResult, this.currentResult?.flow, { force: true });
            this._renderFollowupChecklist(this.currentResult, this.currentResult?.flow);
            this._renderParameterDraftEditor(this.currentResult, this.currentResult?.flow);
            this.options.onApplied?.(this.flowCanvas.serialize?.() || flow);
            this.options.showToast?.('方案已应用到画布', 'success');
            this._setWorkbenchState(AiWorkbenchStates.APPLIED);

            // Show undo option in status note
            this._setResultStatusNote(
                '方案已应用到画布。<button class="ai-undo-btn" id="ai-btn-undo">撤销应用</button>',
                'success',
                true
            );
            const undoBtn = this.container.querySelector('#ai-btn-undo');
            if (undoBtn) {
                undoBtn.addEventListener('click', () => this._undoApply());
            }
        } catch (err) {
            console.error('应用流程失败:', err);
            const message = err?.message || '未知错误';
            this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
            this._setResultStatusNote(`应用流程失败：${message}`, 'warning');
            this._addMessage('system', `应用流程失败：${message}`);
        }
    }

    _undoApply() {
        if (!this._preApplySnapshot || !this.flowCanvas) {
            this._addMessage('system', '没有可撤销的应用记录。');
            return;
        }

        // Check if canvas was manually modified after apply
        const currentRevision = this.flowCanvas?.getFlowRevision?.() || 0;
        const revisionAtApply = this._preApplyCanvasRevision || 0;
        if (currentRevision > revisionAtApply + 1) {
            const confirmed = window.confirm('画布在应用后已被手动修改，撤销将覆盖这些修改。确定要继续吗？');
            if (!confirmed) return;
        }

        try {
            this.flowCanvas.deserialize(this._preApplySnapshot);
            this.appliedResultVersion = 0;
            this.appliedCanvasRevision = this.flowCanvas?.getFlowRevision?.() || 0;
            this._preApplySnapshot = null;
            this._preApplySnapshotVersion = 0;
            this._preApplyCanvasRevision = 0;
            this._updateApplyButtonState();
            this.options.onCanvasChanged?.({
                source: 'ai',
                action: 'undo-apply',
                flow: this.flowCanvas.serialize?.() || null
            });
            this._setResultStatusNote('已撤销上一次应用。', 'info');
            this._setWorkbenchState(AiWorkbenchStates.READY_TO_APPLY);
            this._addMessage('system', '已撤销应用，画布已恢复到应用前状态。');
        } catch (err) {
            console.error('撤销应用失败:', err);
            this._addMessage('system', '撤销失败: ' + err.message);
        }
    }

    // ── 拓扑摘要提取 ─────────────────────────────────────────

    _extractTopologySummary(flow) {
        if (!flow) return '';
        const ops = this._extractOperators(flow);
        const connections = this._extractConnections(flow);
        if (ops.length === 0) return '';

        // Build adjacency from connections
        const adj = new Map();
        const inDegree = new Map();
        ops.forEach(op => {
            const tid = op.tempId || op.TempId || '';
            if (!adj.has(tid)) adj.set(tid, []);
            if (!inDegree.has(tid)) inDegree.set(tid, 0);
        });
        connections.forEach(conn => {
            const src = conn.sourceTempId || conn.SourceTempId || '';
            const tgt = conn.targetTempId || conn.TargetTempId || '';
            if (adj.has(src)) adj.get(src).push(tgt);
            inDegree.set(tgt, (inDegree.get(tgt) || 0) + 1);
        });

        // Topological sort with cycle detection
        const queue = [];
        for (const [tid, deg] of inDegree) {
            if (deg === 0) queue.push(tid);
        }
        const sorted = [];
        while (queue.length > 0) {
            const tid = queue.shift();
            sorted.push(tid);
            for (const next of (adj.get(tid) || [])) {
                inDegree.set(next, inDegree.get(next) - 1);
                if (inDegree.get(next) === 0) queue.push(next);
            }
        }

        // Cycle detection: append remaining nodes (in cycles) to avoid silent loss
        if (sorted.length < ops.length) {
            for (const [tid, deg] of inDegree) {
                if (deg > 0 && !sorted.includes(tid)) {
                    sorted.push(tid);
                }
            }
        }

        const opMap = new Map();
        ops.forEach(op => {
            const tid = op.tempId || op.TempId || '';
            opMap.set(tid, op);
        });

        return sorted
            .map(tid => opMap.get(tid))
            .filter(Boolean)
            .map(op => {
                const operatorType = op.operatorType || op.OperatorType || op.type || op.Type || '';
                return getOperatorTypeDisplayName(operatorType) || op.displayName || op.DisplayName || '?';
            })
            .join(' -> ');
    }

    // ── 路径脱敏 ─────────────────────────────────────────────

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
    aiPanelValidationPreviewMixin
);

