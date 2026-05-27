import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import httpClient from '../../core/messaging/httpClient.js';
import { createSignal } from '../../core/state/store.js';
import { buildWireSequenceFollowupHint } from '../flow-editor/wireSequenceAssist.js';

const AiWorkbenchStates = Object.freeze({
    IDLE: 'idle',
    CLARIFYING: 'clarifying',
    MATCHING_TEMPLATE: 'matching_template',
    GENERATING: 'generating',
    PARSING: 'parsing',
    VALIDATING: 'validating',
    DRY_RUNNING: 'dry_running',
    REVIEWING_PARAMETERS: 'reviewing_parameters',
    READY_TO_APPLY: 'ready_to_apply',
    APPLYING: 'applying',
    APPLIED: 'applied',
    FAILED: 'failed',
    CANCELLED: 'cancelled'
});

const WORKBENCH_STAGE_ORDER = [
    { key: 'scenario_match', label: '场景识别', states: [AiWorkbenchStates.MATCHING_TEMPLATE] },
    { key: 'template_match', label: '模板匹配', states: [AiWorkbenchStates.MATCHING_TEMPLATE] },
    { key: 'generating', label: '生成', states: [AiWorkbenchStates.GENERATING, AiWorkbenchStates.PARSING] },
    { key: 'validating', label: '校验', states: [AiWorkbenchStates.VALIDATING] },
    { key: 'dryrun', label: 'DryRun', states: [AiWorkbenchStates.DRY_RUNNING] },
    { key: 'parameters', label: '待补参数', states: [AiWorkbenchStates.REVIEWING_PARAMETERS] },
    { key: 'apply', label: '可应用', states: [AiWorkbenchStates.READY_TO_APPLY, AiWorkbenchStates.APPLYING, AiWorkbenchStates.APPLIED] }
];

const STAGE_DIAGNOSTIC_LABELS = {
    conversation: '会话准备',
    turn_router: '回合路由',
    scenario_match: '场景匹配',
    requirement_brief: '需求提炼',
    clarification: '需求澄清',
    prompt_context: 'Prompt 构建',
    llm: '模型调用',
    parse: '结果解析',
    validator: '流程校验',
    template_gate: '模板约束',
    dryrun: 'DryRun 预演',
    layout: '自动布局'
};

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

        // 应用预览与撤销
        this._preApplySnapshot = null;
        this._preApplySnapshotVersion = 0;
        this._preApplyCanvasRevision = 0;

        // 附件报告缓存
        this._lastAttachmentReport = null;
        this._lastModelSupportsVision = null;

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
    }
    
    activate() {
        this._checkConnection();
        const textarea = this.container.querySelector('.ai-textarea');
        if (textarea) textarea.focus();
    }
    
    _handleNewConversation() {
        this.sessionId = null;
        this._saveSessionId(null);
        this.currentResult = null;
        this.lastUserPrompt = '';
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
                <aside class="ai-pane-left">
                    <div class="ai-pane-header">
                        <span class="pane-icon">
                            <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor"><path d="M20 2H4c-1.1 0-2 .9-2 2v18l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2z"/></svg>
                        </span>
                        <span class="pane-title">CO-PILOT 对话</span>
                        <span class="status-badge online" id="ai-conn-status"><span class="status-dot connected"></span>在线</span>
                        <button class="icon-btn" id="ai-btn-new-session" title="新建对话">新对话</button>
                        <button class="icon-btn ai-btn-history" id="ai-btn-history" title="历史会话" aria-expanded="false">历史</button>
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
                            <div class="ai-requirement-mode-toggle" id="ai-requirement-mode-toggle">
                                <button class="ai-mode-chip" type="button" data-requirement-mode="strict">严格澄清</button>
                                <button class="ai-mode-chip" type="button" data-requirement-mode="draft">草稿优先</button>
                            </div>
                            <div class="ai-requirement-mode-tip" id="ai-requirement-mode-tip"></div>
                        </div>
                        <div class="ai-input-box">
                            <button class="icon-btn" id="ai-btn-attach" title="附件">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor"><path d="M16.5 6v11.5c0 2.21-1.79 4-4 4s-4-1.79-4-4V5a2.5 2.5 0 015 0v10.5c0 .55-.45 1-1 1s-1-.45-1-1V6H10v9.5a2.5 2.5 0 005 0V5c0-1.38-1.12-2.5-2.5-2.5S8 3.62 8 5v11.5c0 3.04 2.46 5.5 5.5 5.5s5.5-2.46 5.5-5.5V6h-1.5z"/></svg>
                            </button>
                            <textarea class="ai-textarea" id="ai-input" placeholder="输入指令..."></textarea>
                            <button class="ai-btn-cancel" id="ai-btn-cancel" type="button" title="取消生成">取消</button>
                            <button class="ai-btn-send" id="ai-btn-gen">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="white"><path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z"/></svg>
                            </button>
                        </div>
                        <div class="ai-attachments" id="ai-attachments"></div>
                        <div class="ai-manual-retry-banner" id="ai-manual-retry-banner"></div>
                        <div class="ai-followup-hint-banner" id="ai-followup-hint-banner"></div>
                        <div class="ai-quick-examples">
                            <div class="examples-header" id="examples-toggle">
                                快捷示例 
                                <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor" style="vertical-align:middle;"><path d="M7 10l5 5 5-5z"/></svg>
                            </div>
                            <div class="ai-example-tags">
                                <span class="ai-tag" data-text="读取产品上的DataMatrix二维码。">条码读取</span>
                                <span class="ai-tag" data-text="检测金属零件表面的划痕缺陷。先进行高斯滤波去噪，然后使用Canny边缘检测，最后通过Blob分析计算划痕面积。">缺陷检测</span>
                                <span class="ai-tag" data-text="测量两个圆形孔位的圆心距离。">孔距测量</span>
                            </div>
                        </div>
                    </div>
                </aside>

                <aside class="ai-pane-right" id="ai-result-pane">
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

        const currentFlowPayload = existingFlowJson ?? this._getCurrentFlowJson();
        const resolvedMode = this._resolveGenerateRequestMode(explicitMode, normalizedDescription, currentFlowPayload);
        const flowPayload = resolvedMode === 'new' ? null : currentFlowPayload;
        const normalizedTemplateSelection = this._normalizeTemplateSelection(templateSelection);
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
                    attachments: attachmentPaths
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

    _resolveGenerateRequestMode(explicitMode = '', description = '', currentFlowPayload = null) {
        const normalizedExplicitMode = String(explicitMode || '').trim().toLowerCase();
        if (normalizedExplicitMode) {
            return normalizedExplicitMode;
        }

        if (currentFlowPayload && this._looksLikeExistingFlowEditRequest(description)) {
            return 'modify';
        }

        if (currentFlowPayload && this._looksLikeExplicitNewFlowRequest(description)) {
            return 'new';
        }

        if (currentFlowPayload && this._looksLikeModifyRequest(description)) {
            return 'modify';
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
            '替换', '改成', '变成', '中文', '中文化', '阈值', '参数', '算子名称', 'displayname',
            'change', 'update', 'adjust', 'add', 'remove', 'replace', 'refine'
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

        const pending = this._normalizePendingParameters(
            this.currentResult?.pendingParameters ?? this.currentResult?.PendingParameters
        );
        if (pending.length === 0) {
            this._addMessage('system', '当前没有待确认参数，无需提交 AI 审核。');
            return;
        }

        const operators = this._getPendingOperatorSourceOperators(this.currentResult.flow);
        const confirmationState = this._getPendingParameterConfirmationState(pending, operators);
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

        const pending = this._normalizePendingParameters(data?.pendingParameters ?? data?.PendingParameters);
        if (pending.length === 0) {
            this._addMessage('system', '当前没有待确认参数，无需执行确认。');
            return;
        }

        const operators = this._getPendingOperatorSourceOperators(flow || data?.flow || data?.Flow || null);
        const groups = this._collectPendingDraftGroups(pending, operators);
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
            pending: payload?.pendingParameters ?? payload?.PendingParameters,
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
            (data?.pendingParameters || data?.PendingParameters || [])
                .map(p => p.operatorId || p.OperatorId || p.actualOperatorId || p.ActualOperatorId || '')
                .filter(Boolean)
        );
        const missingResourceOps = new Set(
            (data?.missingResources || data?.MissingResources || [])
                .map(r => (r.description || r.Description || '').toLowerCase())
        );
        if (clarificationRequired && !flow) {
            opsContainer.innerHTML = '<div class="ai-followup-empty">当前尚未进入生成阶段，请先完成需求澄清。</div>';
        } else {
            ops.forEach((op, i) => {
                const opName = op?.displayName || op?.DisplayName || op?.name || op?.Name || '未命名算子';
                const opType = op?.operatorType || op?.OperatorType || '';
                const opId = op?.tempId || op?.TempId || op?.id || op?.Id || '';
                const hasPending = pendingSet.has(opId);
                const hasMissing = (op.parameters || op.Parameters || {})['ModelPath'] === '' || missingResourceOps.has(opName.toLowerCase());
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
                        ${opType ? `<div class="op-type-badge">${this._escapeHtml(opType)}</div>` : ''}
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

        const pending = this._normalizePendingParameters(data?.pendingParameters ?? data?.PendingParameters);
        const missing = this._normalizeMissingResources(data?.missingResources ?? data?.MissingResources);
        const recommended = this._normalizeRecommendedTemplate(data?.recommendedTemplate ?? data?.RecommendedTemplate);
        const candidates = this._normalizeTemplateCandidates(data?.templateCandidates ?? data?.TemplateCandidates);
        const requirementBrief = this._normalizeRequirementBrief(data?.requirementBrief ?? data?.RequirementBrief ?? null);
        const nonBlockingFields = requirementBrief?.nonBlockingMissingFields || [];
        const generationMode = this._getGenerationMode(data);
        const templateLockLevel = this._getTemplateLockLevel(data);
        const operators = this._getPendingOperatorSourceOperators(flow || data?.flow || data?.Flow || null);
        const hasTemplateStrategy = Boolean(recommended || candidates.length > 0 || generationMode || templateLockLevel);

        if (!hasTemplateStrategy && pending.length === 0 && missing.length === 0 && nonBlockingFields.length === 0) {
            container.classList.add('is-empty');
            container.innerHTML = '<div class="ai-followup-empty">当前没有待确认参数或缺失资源。</div>';
            return;
        }

        const followupText = this._buildFollowupHintText({ recommended, pending, missing, operators, nonBlockingFields });
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

        const pendingHtml = pending.length > 0
            ? `
                <div class="ai-followup-section">
                    <div class="ai-followup-section-header">
                        <div class="ai-followup-section-label">待确认参数</div>
                        <div class="ai-followup-section-tip">点击可跳到下方填写区</div>
                    </div>
                    <div class="ai-followup-list">
                        ${pending.map(item => {
                            const context = this._resolvePendingOperatorContext(item.operatorId, operators);
                            const groupKey = this._getPendingDraftGroupKey(item.operatorId);
                            return `
                            <button class="ai-followup-item ai-followup-nav" type="button" data-followup-nav="${this._escapeHtml(groupKey)}">
                                <div class="ai-followup-item-title">${this._escapeHtml(context.label)}</div>
                                <div class="ai-followup-item-body">需要补充：${this._escapeHtml(item.parameterNames.join('、'))}</div>
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

    _renderParameterDraftEditor(data, flow = null) {
        const container = this.container?.querySelector('#ai-result-parameter-editor');
        if (!container) return;

        const pending = this._normalizePendingParameters(data?.pendingParameters ?? data?.PendingParameters);
        const operators = this._getPendingOperatorSourceOperators(flow || data?.flow || data?.Flow || null);
        this._syncPendingParameterDrafts(data, flow);

        if (pending.length === 0) {
            container.classList.add('is-empty');
            container.innerHTML = '<div class="ai-followup-empty">当前没有待确认参数，暂无需补录。</div>';
            return;
        }

        const groups = this._collectPendingDraftGroups(pending, operators);
        const confirmationState = this._getPendingParameterConfirmationState(pending, operators, groups);
        const { totals } = confirmationState;
        const signature = this.pendingParameterDraftSignature;

        this._ensurePendingDraftMetadata(groups, signature);
        if (groups.some(group => group.fields.some(field => field.dataType === 'camerabinding'))) {
            this._ensureCameraBindings(signature);
        }

        container.classList.remove('is-empty');
        container.innerHTML = `
            <div class="ai-parameter-editor-summary">
                已填写 <span class="result-count">${totals.filled}</span> / <span class="result-count">${totals.total}</span> 项。
                <span class="ai-parameter-editor-summary-note">${confirmationState.isConfirmed ? '参数已确认，可直接提交审核。' : '请先填写并确认全部参数，再提交审核。'}</span>
            </div>
            <div class="ai-parameter-group-list">
                ${groups.map(group => `
                    <section class="ai-parameter-group" data-draft-group="${this._escapeHtml(group.groupKey)}">
                        <div class="ai-parameter-group-header">
                            <div>
                                <div class="ai-parameter-group-title">${this._escapeHtml(group.label)}</div>
                                <div class="ai-parameter-group-meta">
                                    ${group.operatorType ? this._escapeHtml(group.operatorType) : '未识别算子类型'}
                                    ${group.operator ? '' : ' · 当前画布快照中未找到精确算子，提交时将按名称提示 AI 继续审核'}
                                </div>
                            </div>
                            <button class="ai-parameter-group-jump" type="button" data-followup-nav="${this._escapeHtml(group.groupKey)}">定位</button>
                        </div>
                        <div class="ai-parameter-field-list">
                            ${group.fields.map(field => this._renderPendingDraftField(group, field, confirmationState)).join('')}
                        </div>
                    </section>
                `).join('')}
            </div>
            <div class="ai-parameter-editor-actions">
                <div class="ai-parameter-editor-actions-hint">${confirmationState.isConfirmed ? '参数已确认，提交审核会带上当前方案、已填写参数、仍未填写项和输入框中的补充说明。' : '请先确认全部参数，再提交审核。审核会带上当前方案、已填写参数、仍未填写项和输入框中的补充说明。'}</div>
                <div class="ai-parameter-editor-action-row">
                    <button class="ai-parameter-confirm-btn" type="button" id="ai-btn-confirm-parameters">确认全部参数</button>
                    <button class="ai-parameter-review-btn" type="button" id="ai-btn-review-parameters">提交审核</button>
                </div>
            </div>
        `;

        container.querySelectorAll('[data-followup-nav]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', () => {
                this._scrollToPendingDraftGroup(button.dataset.followupNav || '');
            });
        });

        container.querySelectorAll('[data-draft-input="true"]').forEach(inputEl => {
            const updateDraft = () => {
                const operatorId = inputEl.dataset.draftOperatorId || '';
                const parameterName = inputEl.dataset.draftParameterName || '';
                const fieldType = inputEl.dataset.fieldType || '';
                const value = this._readPendingDraftInputValue(inputEl);
                this._setPendingDraftConfirmedValue(operatorId, parameterName, value, fieldType, 'user_input');
                this._updatePendingDraftSummary(data, flow);
                this._renderFollowupChecklist(data, flow);
            };

            inputEl.addEventListener('change', updateDraft);
            if (inputEl.tagName === 'INPUT') {
                inputEl.addEventListener('input', updateDraft);
            }
        });

        container.querySelectorAll('[data-draft-adopt="true"]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', () => {
                const operatorId = button.dataset.draftOperatorId || '';
                const parameterName = button.dataset.draftParameterName || '';
                const groupsForAdopt = this._collectPendingDraftGroups(pending, operators);
                const targetGroup = groupsForAdopt.find(group => group.operatorId === operatorId);
                const targetField = targetGroup?.fields.find(field =>
                    String(field.parameterName || '').trim().toLowerCase() === String(parameterName || '').trim().toLowerCase()
                );
                if (!targetField) return;
                this._setPendingDraftConfirmedValue(
                    operatorId,
                    parameterName,
                    targetField.suggestedValue,
                    targetField.dataType,
                    'user_input'
                );
                this._renderFollowupChecklist(data, flow);
                this._renderParameterDraftEditor(data, flow);
            });
        });

        container.querySelectorAll('[data-draft-file-pick]').forEach(button => {
            button.disabled = this.isGenerating;
            button.addEventListener('click', () => {
                this._pickPendingDraftFile(
                    button.dataset.draftOperatorId || '',
                    button.dataset.draftParameterName || ''
                );
            });
        });

        const confirmButton = container.querySelector('#ai-btn-confirm-parameters');
        if (confirmButton) {
            confirmButton.addEventListener('click', () => this._handleConfirmPendingParameters(data, flow));
        }

        const reviewButton = container.querySelector('#ai-btn-review-parameters');
        if (reviewButton) {
            reviewButton.addEventListener('click', this._handlePendingParameterReview);
        }
        this._updatePendingDraftSummary(data, flow);
    }

    _collectPendingDraftGroups(pending, operators) {
        return pending.map(item => {
            const context = this._resolvePendingOperatorContext(item.operatorId, operators);
            const metadata = this._getCachedOperatorMetadata(context.operatorType);
            const fields = item.parameterNames.map(parameterName => {
                const parameterMetadata = this._findMetadataParameter(metadata, parameterName);
                const entry = this._getPendingDraftEntry(item.operatorId, parameterName);
                return this._normalizePendingDraftField({
                    operatorId: item.operatorId,
                    parameterName,
                    entry,
                    metadata: parameterMetadata
                });
            });

            return {
                operatorId: item.operatorId,
                operatorType: context.operatorType,
                operator: context.operator,
                label: context.label,
                groupKey: this._getPendingDraftGroupKey(item.operatorId),
                fields
            };
        });
    }

    _renderPendingDraftField(group, field, confirmationState = null) {
        const inputId = this._buildPendingDraftInputId(group.operatorId, field.parameterName);
        const label = this._escapeHtml(field.displayName || field.parameterName);
        const description = field.description
            ? `<div class="ai-parameter-field-desc">${this._escapeHtml(field.description)}</div>`
            : '';
        const currentValue = field.confirmedValue;
        const currentValueText = currentValue === null || currentValue === undefined ? '' : String(currentValue);
        const hasSuggestedValue = this._hasPendingDraftValue(field.suggestedValue, field.dataType);
        const hasConfirmedValue = this._hasPendingDraftValue(field.confirmedValue, field.dataType);
        const isBatchConfirmed = Boolean(confirmationState?.isConfirmed && hasConfirmedValue);
        const showAdoptSuggestion = hasSuggestedValue && !this._arePendingDraftValuesEquivalent(field.confirmedValue, field.suggestedValue, field.dataType);
        const suggestionHtml = hasSuggestedValue
            ? `
                <div class="ai-parameter-field-suggestion">
                    <span class="ai-parameter-field-suggestion-label">建议值：${this._escapeHtml(this._formatPendingDraftValueForDisplay(field.suggestedValue, field))}</span>
                    ${showAdoptSuggestion ? `
                        <button
                            class="ai-parameter-suggestion-btn"
                            type="button"
                            data-draft-adopt="true"
                            data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                            data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                        >
                            采用建议值
                        </button>
                    ` : ''}
                </div>
            `
            : '';
        const sourceHint = field.source === 'canvas_override'
            ? '<div class="ai-parameter-field-desc">当前值已从画布同步。</div>'
            : '';

        let controlHtml = '';
        if (field.dataType === 'boolean' || field.dataType === 'bool') {
            const normalizedBoolean = this.normalizeBooleanLike(currentValue);
            controlHtml = `
                <select
                    id="${this._escapeHtml(inputId)}"
                    class="ai-draft-input ai-draft-select"
                    data-draft-input="true"
                    data-field-type="boolean"
                    data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                    data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                >
                    <option value="" ${normalizedBoolean === null ? 'selected' : ''}>待确认</option>
                    <option value="true" ${normalizedBoolean === true ? 'selected' : ''}>是</option>
                    <option value="false" ${normalizedBoolean === false ? 'selected' : ''}>否</option>
                </select>
            `;
        } else if (field.dataType === 'enum' || field.dataType === 'select' || field.dataType === 'camerabinding') {
            const options = field.dataType === 'camerabinding'
                ? this._buildCameraBindingOptions(currentValue)
                : this._buildEnumOptions(field.options || [], currentValue);
            const extraHint = field.dataType === 'camerabinding' && this.cameraBindingsCache.length === 0
                ? '<div class="ai-parameter-field-desc">正在加载相机绑定列表...</div>'
                : '';
            controlHtml = `
                <select
                    id="${this._escapeHtml(inputId)}"
                    class="ai-draft-input ai-draft-select"
                    data-draft-input="true"
                    data-field-type="${this._escapeHtml(field.dataType)}"
                    data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                    data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                >
                    ${options}
                </select>
                ${extraHint}
            `;
        } else if (field.dataType === 'file') {
            controlHtml = `
                <div class="ai-draft-file-row">
                    <input
                        type="text"
                        id="${this._escapeHtml(inputId)}"
                        class="ai-draft-input ai-draft-text"
                        data-draft-input="true"
                        data-field-type="file"
                        data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                        data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                        value="${this._escapeHtml(currentValueText)}"
                        placeholder="请选择或输入文件路径"
                    />
                    <button
                        class="ai-draft-file-btn"
                        type="button"
                        data-draft-file-pick="true"
                        data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                        data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                    >
                        选择文件
                    </button>
                </div>
            `;
        } else if (['int', 'integer', 'double', 'float', 'number'].includes(field.dataType)) {
            const step = field.step ?? (['int', 'integer'].includes(field.dataType) ? 1 : 'any');
            const minAttr = field.min !== undefined && field.min !== null ? `min="${this._escapeHtml(String(field.min))}"` : '';
            const maxAttr = field.max !== undefined && field.max !== null ? `max="${this._escapeHtml(String(field.max))}"` : '';
            controlHtml = `
                <input
                    type="number"
                    id="${this._escapeHtml(inputId)}"
                    class="ai-draft-input ai-draft-number"
                    data-draft-input="true"
                    data-field-type="${this._escapeHtml(field.dataType)}"
                    data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                    data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                    value="${this._escapeHtml(currentValueText)}"
                    step="${this._escapeHtml(String(step))}"
                    ${minAttr}
                    ${maxAttr}
                    placeholder="请输入数值"
                />
            `;
        } else {
            controlHtml = `
                <input
                    type="text"
                    id="${this._escapeHtml(inputId)}"
                    class="ai-draft-input ai-draft-text"
                    data-draft-input="true"
                    data-field-type="${this._escapeHtml(field.dataType || 'text')}"
                    data-draft-operator-id="${this._escapeHtml(group.operatorId)}"
                    data-draft-parameter-name="${this._escapeHtml(field.parameterName)}"
                    value="${this._escapeHtml(currentValueText)}"
                    placeholder="请输入参数值"
                />
            `;
        }

        return `
            <div class="ai-parameter-field">
                <label class="ai-parameter-field-label" for="${this._escapeHtml(inputId)}">
                    ${label}
                    <span class="ai-parameter-field-key">${this._escapeHtml(field.parameterName)}</span>
                </label>
                ${controlHtml}
                ${suggestionHtml}
                ${isBatchConfirmed ? `<div class="ai-parameter-field-status">当前状态：已确认</div>` : '<div class="ai-parameter-field-status is-unconfirmed">当前状态：待确认</div>'}
                ${sourceHint}
                ${description}
            </div>
        `;
    }

    _countPendingDraftProgress(groups) {
        let total = 0;
        let filled = 0;
        groups.forEach(group => {
            group.fields.forEach(field => {
                total += 1;
                if (this._hasPendingDraftValue(field.confirmedValue, field.dataType)) {
                    filled += 1;
                }
            });
        });
        return { total, filled };
    }

    _hasPendingParameterConfirmation() {
        return Boolean(this.pendingParameterConfirmedDraftSignature && this.pendingParameterConfirmedValueSignature);
    }

    _clearPendingParameterConfirmation() {
        this.pendingParameterConfirmedDraftSignature = '';
        this.pendingParameterConfirmedValueSignature = '';
    }

    _computePendingDraftValueSignature(pending, operators) {
        const safePending = this._normalizePendingParameters(pending);
        const safeOperators = Array.isArray(operators) ? operators : [];
        if (safePending.length === 0) return '';

        const parts = [];
        safePending.forEach(item => {
            const context = this._resolvePendingOperatorContext(item.operatorId, safeOperators);
            const metadata = this._getCachedOperatorMetadata(context.operatorType);
            item.parameterNames.forEach(parameterName => {
                const fieldType = this._normalizePendingFieldType(this._findMetadataParameter(metadata, parameterName));
                const value = this._getPendingDraftConfirmedValue(item.operatorId, parameterName);
                const valueText = this._hasPendingDraftValue(value, fieldType)
                    ? this._stringifyPendingDraftValue(value, fieldType)
                    : '';
                parts.push(`${String(item.operatorId || '').trim().toLowerCase()}::${String(parameterName || '').trim().toLowerCase()}::${String(fieldType || '').trim().toLowerCase()}::${valueText}`);
            });
        });

        return parts.sort().join('|');
    }

    _getPendingParameterConfirmationState(pending, operators, groups = null) {
        const safePending = this._normalizePendingParameters(pending);
        const safeOperators = Array.isArray(operators) ? operators : [];
        const resolvedGroups = Array.isArray(groups) ? groups : this._collectPendingDraftGroups(safePending, safeOperators);
        const totals = this._countPendingDraftProgress(resolvedGroups);
        const valueSignature = this._computePendingDraftValueSignature(safePending, safeOperators);
        const isConfirmed = Boolean(
            totals.total > 0 &&
            totals.filled === totals.total &&
            this.pendingParameterConfirmedDraftSignature &&
            this.pendingParameterConfirmedValueSignature &&
            this.pendingParameterConfirmedDraftSignature === this.pendingParameterDraftSignature &&
            this.pendingParameterConfirmedValueSignature === valueSignature
        );
        const hasCurrentFlow = Boolean(this.currentResult?.flow);

        return {
            groups: resolvedGroups,
            totals,
            valueSignature,
            isConfirmed,
            canConfirm: hasCurrentFlow && totals.total > 0 && totals.filled === totals.total && !isConfirmed,
            canReview: hasCurrentFlow && isConfirmed
        };
    }

    _updatePendingDraftSummary(data = this.currentResult, flow = null) {
        const container = this.container?.querySelector('#ai-result-parameter-editor');
        if (!container || container.classList.contains('is-empty')) return;

        const pending = this._normalizePendingParameters(
            data?.pendingParameters ?? data?.PendingParameters
        );
        if (pending.length === 0) return;

        const operators = this._getPendingOperatorSourceOperators(flow || data?.flow || data?.Flow || null);
        const confirmationState = this._getPendingParameterConfirmationState(pending, operators);
        const { totals } = confirmationState;
        const summary = container.querySelector('.ai-parameter-editor-summary');
        if (summary) {
            summary.innerHTML = `
                已填写 <span class="result-count">${totals.filled}</span> / <span class="result-count">${totals.total}</span> 项。
                <span class="ai-parameter-editor-summary-note">${confirmationState.isConfirmed ? '参数已确认，可直接提交审核。' : '请先填写并确认全部参数，再提交审核。'}</span>
            `;
        }

        const hint = container.querySelector('.ai-parameter-editor-actions-hint');
        if (hint) {
            hint.textContent = confirmationState.isConfirmed
                ? '参数已确认，提交审核会带上当前方案、已填写参数、仍未填写项和输入框中的补充说明。'
                : '请先确认全部参数，再提交审核。审核会带上当前方案、已填写参数、仍未填写项和输入框中的补充说明。';
        }

        container.querySelectorAll('.ai-parameter-field-status').forEach(statusEl => {
            statusEl.textContent = confirmationState.isConfirmed ? '当前状态：已确认' : '当前状态：待确认';
            statusEl.classList.toggle('is-unconfirmed', !confirmationState.isConfirmed);
        });

        const confirmButton = container.querySelector('#ai-btn-confirm-parameters');
        if (confirmButton) {
            confirmButton.disabled = this.isGenerating || !confirmationState.canConfirm;
        }

        const reviewButton = container.querySelector('#ai-btn-review-parameters');
        if (reviewButton) {
            reviewButton.disabled = this.isGenerating || !confirmationState.canReview;
        }
    }

    _syncPendingParameterDrafts(data, flow = null, options = {}) {
        const force = Boolean(options?.force);
        const pending = this._normalizePendingParameters(data?.pendingParameters ?? data?.PendingParameters);
        const operators = this._extractOperators(flow || data?.flow || data?.Flow || null);
        const signature = `${this.currentResultVersion || 0}::${this._computePendingDraftSignature(pending, operators)}`;
        const canvasOperators = this._isCurrentResultAppliedToCanvas()
            ? this._extractOperators(this.flowCanvas?.serialize?.() || null)
            : [];

        if (!force && signature === this.pendingParameterDraftSignature) {
            return;
        }

        if (pending.length === 0) {
            this._resetPendingDraftState();
            return;
        }

        const nextDrafts = force ? this.pendingParameterDrafts : {};
        pending.forEach(item => {
            const context = this._resolvePendingOperatorContext(item.operatorId, operators);
            const metadata = this._getCachedOperatorMetadata(context.operatorType);
            item.parameterNames.forEach(parameterName => {
                const parameterMetadata = this._findMetadataParameter(metadata, parameterName);
                const fieldType = this._normalizePendingFieldType(parameterMetadata);

                if (!nextDrafts[item.operatorId]) {
                    nextDrafts[item.operatorId] = {};
                }

                const entry = force
                    ? this._getPendingDraftEntry(item.operatorId, parameterName)
                    : this._createPendingDraftEntry();
                const suggestedValue = this._normalizePendingValueByType(
                    this._readOperatorParameterValue(context.operator, parameterName),
                    fieldType
                );
                const canvasValue = this._isCurrentResultAppliedToCanvas()
                    ? this._normalizePendingValueByType(
                        this._readOperatorParameterValue(
                            this._resolvePendingOperatorContext(item.operatorId, canvasOperators).operator,
                            parameterName
                        ),
                        fieldType
                    )
                    : null;

                let nextEntry = this._createPendingDraftEntry({
                    ...entry,
                    suggestedValue: this._hasPendingDraftValue(suggestedValue, fieldType) ? suggestedValue : null
                });

                if (!force) {
                    nextEntry.confirmedValue = null;
                    nextEntry.status = 'unconfirmed';
                    nextEntry.source = 'ai_suggestion';
                }

                if (this._isCurrentResultAppliedToCanvas() && this._hasPendingDraftValue(canvasValue, fieldType) && !this._arePendingDraftValuesEquivalent(canvasValue, nextEntry.suggestedValue, fieldType)) {
                    nextEntry = this._createPendingDraftEntry({
                        ...nextEntry,
                        confirmedValue: canvasValue,
                        status: 'confirmed',
                        source: 'canvas_override'
                    });
                } else if (force && nextEntry.source === 'canvas_override' && !this._hasPendingDraftValue(canvasValue, fieldType)) {
                    nextEntry = this._createPendingDraftEntry({
                        ...nextEntry,
                        confirmedValue: null,
                        status: 'unconfirmed',
                        source: this._hasPendingDraftValue(nextEntry.suggestedValue, fieldType) ? 'ai_suggestion' : 'user_input'
                    });
                }

                nextDrafts[item.operatorId][parameterName] = nextEntry;
            });
        });

        this.pendingParameterDrafts = nextDrafts;
        this.pendingParameterDraftSignature = signature;
        if (this._hasPendingParameterConfirmation()) {
            const confirmationState = this._getPendingParameterConfirmationState(pending, operators);
            if (!confirmationState.isConfirmed) {
                this._clearPendingParameterConfirmation();
            }
        }
    }

    _computePendingDraftSignature(pending, operators) {
        if (!Array.isArray(pending) || pending.length === 0) return '';
        const operatorPart = (Array.isArray(operators) ? operators : [])
            .map((operator, index) => {
                const operatorId = operator?.id ?? operator?.Id ?? operator?.tempId ?? operator?.TempId ?? `index-${index}`;
                const operatorType = operator?.type ?? operator?.Type ?? operator?.operatorType ?? operator?.OperatorType ?? '';
                return `${String(operatorId).trim()}:${String(operatorType).trim()}`;
            })
            .join('|');
        const pendingPart = pending
            .map(item => `${item.operatorId}:${item.parameterNames.join(',')}`)
            .join('|');
        return `${this.sessionId || 'no-session'}::${operatorPart}::${pendingPart}`;
    }

    _getPendingDraftGroupKey(operatorId) {
        const normalizedId = String(operatorId || '').trim();
        const binding = this.pendingOperatorBindings[normalizedId] || null;
        return `pending-${binding?.actualOperatorId || normalizedId || 'unknown'}`;
    }

    _buildPendingOperatorBinding({ pendingOperatorId, actualOperatorId = '', label = '', operatorType = '' }) {
        const normalizedPendingId = String(pendingOperatorId || '').trim();
        return {
            pendingOperatorId: normalizedPendingId,
            actualOperatorId: String(actualOperatorId || '').trim(),
            label: String(label || '').trim(),
            operatorType: String(operatorType || '').trim()
        };
    }

    _findOperatorByAnyId(operators, operatorId) {
        const normalizedId = String(operatorId || '').trim();
        if (!normalizedId) return null;

        return (Array.isArray(operators) ? operators : []).find(op => {
            const candidates = [
                op?.tempId,
                op?.TempId,
                op?.id,
                op?.Id
            ].map(value => String(value || '').trim()).filter(Boolean);
            return candidates.includes(normalizedId);
        }) || null;
    }

    _findOperatorByTempSequence(operators, operatorId) {
        const normalizedId = String(operatorId || '').trim();
        const match = normalizedId.match(/^op[_-](\d+)$/i);
        if (!match) return null;

        const index = Number.parseInt(match[1], 10) - 1;
        if (!Number.isInteger(index) || index < 0) {
            return null;
        }

        const safeOperators = Array.isArray(operators) ? operators : [];
        return safeOperators[index] || null;
    }

    _buildPendingOperatorDisplayLabel(operator, fallbackId = '') {
        const normalizedFallbackId = String(fallbackId || '').trim();
        const directName = String(
            operator?.displayName ??
            operator?.DisplayName ??
            operator?.name ??
            operator?.Name ??
            ''
        ).trim();
        if (directName) {
            return normalizedFallbackId ? `${directName}（${normalizedFallbackId}）` : directName;
        }
        return normalizedFallbackId ? `算子 ${normalizedFallbackId}` : '未命名算子';
    }

    _rebuildPendingOperatorBindings({ pending, flow = null, sourceFlow = null, preferIndexFallback = false }) {
        const normalizedPending = this._normalizePendingParameters(pending);
        const actualOperators = this._extractOperators(flow || null);
        const sourceOperators = this._extractOperators(sourceFlow || flow || null);
        const nextBindings = {};

        normalizedPending.forEach((item) => {
            const normalizedPendingId = String(item.operatorId || '').trim();
            const normalizedActualId = String(item.actualOperatorId || '').trim();
            if (!normalizedPendingId) return;

            let sourceMatch = this._findOperatorByAnyId(sourceOperators, normalizedPendingId);
            let actualMatch = normalizedActualId
                ? this._findOperatorByAnyId(actualOperators, normalizedActualId)
                : this._findOperatorByAnyId(actualOperators, normalizedPendingId);

            if (!sourceMatch && preferIndexFallback) {
                sourceMatch = this._findOperatorByTempSequence(sourceOperators, normalizedPendingId);
            }

            if (!actualMatch && sourceMatch) {
                const sourceActualId = String(sourceMatch?.id ?? sourceMatch?.Id ?? '').trim();
                if (sourceActualId) {
                    actualMatch = this._findOperatorByAnyId(actualOperators, sourceActualId);
                }
            }

            if (!actualMatch && preferIndexFallback) {
                actualMatch = this._findOperatorByTempSequence(actualOperators, normalizedPendingId);
            }

            const operatorForLabel = sourceMatch || actualMatch || null;
            nextBindings[normalizedPendingId] = this._buildPendingOperatorBinding({
                pendingOperatorId: normalizedPendingId,
                actualOperatorId: actualMatch?.id ?? actualMatch?.Id ?? normalizedActualId,
                label: this._buildPendingOperatorDisplayLabel(operatorForLabel, normalizedPendingId),
                operatorType: operatorForLabel?.type ?? operatorForLabel?.Type ?? operatorForLabel?.operatorType ?? operatorForLabel?.OperatorType ?? ''
            });
        });

        this.pendingOperatorBindings = nextBindings;
        return nextBindings;
    }

    _getPendingOperatorSourceFlow(fallbackFlow = null) {
        if (this._isCurrentResultAppliedToCanvas() && this.flowCanvas?.serialize) {
            return this.flowCanvas.serialize();
        }
        return fallbackFlow;
    }

    _getPendingOperatorSourceOperators(fallbackFlow = null) {
        return this._extractOperators(this._getPendingOperatorSourceFlow(fallbackFlow));
    }

    _buildPendingDraftInputId(operatorId, parameterName) {
        const normalize = (value) => String(value || '').replace(/[^a-zA-Z0-9_-]/g, '_');
        return `ai-draft-${normalize(operatorId)}-${normalize(parameterName)}`;
    }

    _resolvePendingOperatorContext(operatorId, operators) {
        const normalizedId = String(operatorId || '').trim();
        const safeOperators = Array.isArray(operators) ? operators : [];
        const binding = this.pendingOperatorBindings[normalizedId] || this._buildPendingOperatorBinding({
            pendingOperatorId: normalizedId,
            label: normalizedId ? `算子 ${normalizedId}` : '未命名算子'
        });

        const operator = binding.actualOperatorId
            ? this._findOperatorByAnyId(safeOperators, binding.actualOperatorId)
            : this._findOperatorByAnyId(safeOperators, normalizedId);
        const operatorType = String(
            operator?.type ??
            operator?.Type ??
            operator?.operatorType ??
            operator?.OperatorType ??
            binding.operatorType ??
            ''
        ).trim();
        const label = operator
            ? this._buildPendingOperatorDisplayLabel(operator, normalizedId)
            : binding.label;

        return {
            operator,
            operatorType,
            label
        };
    }

    _getCachedOperatorMetadata(type) {
        const normalizedType = String(type || '').trim().toLowerCase();
        if (!normalizedType) return null;

        if (this.operatorMetadataCache.has(normalizedType)) {
            return this.operatorMetadataCache.get(normalizedType);
        }

        const libraryOperators = this.options.getOperators?.() || [];
        const matched = libraryOperators.find(operator =>
            String(operator?.type || '').trim().toLowerCase() === normalizedType
        ) || null;
        if (matched) {
            this.operatorMetadataCache.set(normalizedType, matched);
            return matched;
        }

        return null;
    }

    async _ensureOperatorMetadata(type) {
        const normalizedType = String(type || '').trim();
        const cacheKey = normalizedType.toLowerCase();
        if (!normalizedType) return null;

        const cached = this._getCachedOperatorMetadata(normalizedType);
        if (cached) return cached;

        if (this.operatorMetadataLoading.has(cacheKey)) {
            return this.operatorMetadataLoading.get(cacheKey);
        }

        const loadingPromise = httpClient
            .get(`/operators/${encodeURIComponent(normalizedType)}/metadata`)
            .then(metadata => {
                if (metadata && typeof metadata === 'object') {
                    this.operatorMetadataCache.set(cacheKey, metadata);
                    return metadata;
                }
                this.operatorMetadataCache.set(cacheKey, null);
                return null;
            })
            .catch(error => {
                console.warn('[AiPanel] 获取算子元数据失败:', normalizedType, error);
                this.operatorMetadataCache.set(cacheKey, null);
                return null;
            })
            .finally(() => {
                this.operatorMetadataLoading.delete(cacheKey);
            });

        this.operatorMetadataLoading.set(cacheKey, loadingPromise);
        return loadingPromise;
    }

    _ensurePendingDraftMetadata(groups, signature) {
        const missingTypes = [...new Set(groups
            .map(group => group.operatorType)
            .filter(type => {
                const normalizedType = String(type || '').trim().toLowerCase();
                if (!normalizedType) return false;
                const cached = this._getCachedOperatorMetadata(type);
                return cached === null && !this.operatorMetadataCache.has(normalizedType);
            }))];

        missingTypes.forEach(type => {
            this._ensureOperatorMetadata(type).then(() => {
                if (this.pendingParameterDraftSignature !== signature || !this.currentResult?.flow) {
                    return;
                }
                this._renderParameterDraftEditor(this.currentResult, this.currentResult.flow);
            });
        });
    }

    async _ensureCameraBindings(signature) {
        if (this.cameraBindingsCache.length > 0) {
            return this.cameraBindingsCache;
        }

        if (this.cameraBindingsLoadingPromise) {
            return this.cameraBindingsLoadingPromise;
        }

        this.cameraBindingsLoadingPromise = httpClient
            .get('/cameras/bindings')
            .then(result => {
                this.cameraBindingsCache = Array.isArray(result) ? result : [];
                if (this.pendingParameterDraftSignature === signature && this.currentResult?.flow) {
                    this._renderParameterDraftEditor(this.currentResult, this.currentResult.flow);
                }
                return this.cameraBindingsCache;
            })
            .catch(error => {
                console.warn('[AiPanel] 获取相机绑定失败。', error);
                return [];
            })
            .finally(() => {
                this.cameraBindingsLoadingPromise = null;
            });

        return this.cameraBindingsLoadingPromise;
    }

    normalizeBooleanLike(value) {
        if (value === null || value === undefined) return null;
        if (typeof value === 'boolean') return value;
        if (typeof value === 'number') {
            if (value === 1) return true;
            if (value === 0) return false;
            return null;
        }

        const normalized = String(value).trim().toLowerCase();
        if (!normalized) return null;
        if (['true', '1', 'yes', 'y', 'on'].includes(normalized)) return true;
        if (['false', '0', 'no', 'n', 'off'].includes(normalized)) return false;
        return null;
    }

    _isPendingBooleanField(fieldType = '') {
        return ['boolean', 'bool'].includes(String(fieldType || '').trim().toLowerCase());
    }

    _isPendingNumericField(fieldType = '') {
        return ['int', 'integer', 'double', 'float', 'number'].includes(String(fieldType || '').trim().toLowerCase());
    }

    _parseNumericLike(value) {
        if (typeof value === 'number') {
            return Number.isFinite(value) ? value : null;
        }
        const normalized = String(value ?? '').trim();
        if (!normalized) return null;
        const parsed = Number(normalized);
        return Number.isFinite(parsed) ? parsed : null;
    }

    _normalizePendingFieldType(metadata) {
        const rawType = String(
            metadata?.dataType ??
            metadata?.DataType ??
            metadata?.type ??
            metadata?.Type ??
            'text'
        ).trim().toLowerCase();

        return ['string', 'text'].includes(rawType) ? 'text' : rawType;
    }

    _normalizePendingValueByType(value, fieldType = '') {
        if (value === undefined) return null;
        const normalizedFieldType = String(fieldType || '').trim().toLowerCase();

        if (this._isPendingBooleanField(normalizedFieldType)) {
            return this.normalizeBooleanLike(value);
        }

        if (this._isPendingNumericField(normalizedFieldType)) {
            return this._parseNumericLike(value);
        }

        if (value === null) return null;
        const normalized = String(value).trim();
        return normalized.length > 0 ? normalized : null;
    }

    _arePendingDraftValuesEquivalent(left, right, fieldType = '') {
        const normalizedFieldType = String(fieldType || '').trim().toLowerCase();
        if (this._isPendingBooleanField(normalizedFieldType)) {
            return this.normalizeBooleanLike(left) === this.normalizeBooleanLike(right);
        }

        if (this._isPendingNumericField(normalizedFieldType)) {
            return this._parseNumericLike(left) === this._parseNumericLike(right);
        }

        const leftValue = left === null || left === undefined ? '' : String(left).trim();
        const rightValue = right === null || right === undefined ? '' : String(right).trim();
        return leftValue === rightValue;
    }

    _hasPendingDraftValue(value, fieldType = '') {
        if (value === null || value === undefined) return false;

        if (this._isPendingBooleanField(fieldType)) {
            return this.normalizeBooleanLike(value) !== null;
        }

        if (this._isPendingNumericField(fieldType)) {
            return this._parseNumericLike(value) !== null;
        }

        if (typeof value === 'boolean') return true;
        if (typeof value === 'number') return Number.isFinite(value);
        return String(value).trim().length > 0;
    }

    _createPendingDraftEntry(overrides = {}) {
        return {
            confirmedValue: null,
            suggestedValue: null,
            status: 'unconfirmed',
            source: 'ai_suggestion',
            ...overrides
        };
    }

    _getPendingDraftEntry(operatorId, parameterName) {
        const operatorDrafts = this.pendingParameterDrafts[String(operatorId || '').trim()] || {};
        const normalizedName = String(parameterName || '').trim().toLowerCase();
        const matchedKey = Object.keys(operatorDrafts).find(key => key.toLowerCase() === normalizedName);
        const rawEntry = matchedKey ? operatorDrafts[matchedKey] : null;

        if (!rawEntry || typeof rawEntry !== 'object' || Array.isArray(rawEntry)) {
            return this._createPendingDraftEntry();
        }

        return this._createPendingDraftEntry(rawEntry);
    }

    _getPendingDraftConfirmedValue(operatorId, parameterName) {
        return this._getPendingDraftEntry(operatorId, parameterName).confirmedValue;
    }

    _getPendingDraftSuggestedValue(operatorId, parameterName) {
        return this._getPendingDraftEntry(operatorId, parameterName).suggestedValue;
    }

    _setPendingDraftConfirmedValue(operatorId, parameterName, value, fieldType = '', source = 'user_input') {
        const operatorKey = String(operatorId || '').trim();
        const parameterKey = this._resolvePendingDraftParameterKey(operatorKey, parameterName);
        if (!operatorKey || !parameterKey) return;

        const nextValue = this._normalizePendingValueByType(value, fieldType);
        const entry = this._getPendingDraftEntry(operatorKey, parameterKey);
        const previousValue = entry.confirmedValue;
        const hasValue = this._hasPendingDraftValue(nextValue, fieldType);

        if (!this.pendingParameterDrafts[operatorKey]) {
            this.pendingParameterDrafts[operatorKey] = {};
        }

        this.pendingParameterDrafts[operatorKey][parameterKey] = this._createPendingDraftEntry({
            ...entry,
            confirmedValue: hasValue ? nextValue : null,
            status: hasValue ? 'confirmed' : 'unconfirmed',
            source: hasValue ? source : (this._hasPendingDraftValue(entry.suggestedValue, fieldType) ? 'ai_suggestion' : source)
        });

        if (this._hasPendingParameterConfirmation() && !this._arePendingDraftValuesEquivalent(previousValue, hasValue ? nextValue : null, fieldType)) {
            this._clearPendingParameterConfirmation();
        }
    }

    _setPendingDraftSuggestedValue(operatorId, parameterName, value, fieldType = '') {
        const operatorKey = String(operatorId || '').trim();
        const parameterKey = this._resolvePendingDraftParameterKey(operatorKey, parameterName);
        if (!operatorKey || !parameterKey) return;

        if (!this.pendingParameterDrafts[operatorKey]) {
            this.pendingParameterDrafts[operatorKey] = {};
        }

        const entry = this._getPendingDraftEntry(operatorKey, parameterKey);
        const nextSuggestedValue = this._normalizePendingValueByType(value, fieldType);
        this.pendingParameterDrafts[operatorKey][parameterKey] = this._createPendingDraftEntry({
            ...entry,
            suggestedValue: this._hasPendingDraftValue(nextSuggestedValue, fieldType) ? nextSuggestedValue : null,
            source: entry.status === 'confirmed' ? entry.source : 'ai_suggestion'
        });
    }

    _formatPendingDraftValueForDisplay(value, field) {
        if (!this._hasPendingDraftValue(value, field?.dataType)) {
            return '待确认';
        }

        if (this._isPendingBooleanField(field?.dataType)) {
            return this.normalizeBooleanLike(value) ? '是' : '否';
        }

        if ((field?.dataType === 'enum' || field?.dataType === 'select' || field?.dataType === 'camerabinding') && Array.isArray(field?.options)) {
            const matched = field.options.find(option =>
                String(option?.value ?? option?.Value ?? option ?? '').trim() === String(value ?? '').trim()
            );
            if (matched) {
                return String(matched?.label ?? matched?.Label ?? matched?.value ?? matched?.Value ?? value);
            }
        }

        return String(value ?? '');
    }

    _findMetadataParameter(metadata, parameterName) {
        const parameters = metadata?.parameters || metadata?.Parameters || [];
        return parameters.find(item =>
            String(item?.name ?? item?.Name ?? '').trim().toLowerCase() === String(parameterName || '').trim().toLowerCase()
        ) || null;
    }

    _normalizePendingDraftField({ operatorId, parameterName, entry, metadata }) {
        const options = Array.isArray(metadata?.options ?? metadata?.Options)
            ? (metadata?.options ?? metadata?.Options)
            : [];
        const dataType = this._normalizePendingFieldType(metadata);

        return {
            operatorId,
            parameterName,
            displayName: String(metadata?.displayName ?? metadata?.DisplayName ?? parameterName).trim() || parameterName,
            description: String(metadata?.description ?? metadata?.Description ?? '').trim(),
            dataType,
            min: metadata?.min ?? metadata?.Min ?? metadata?.minValue ?? metadata?.MinValue,
            max: metadata?.max ?? metadata?.Max ?? metadata?.maxValue ?? metadata?.MaxValue,
            step: metadata?.step ?? metadata?.Step,
            options,
            defaultValue: metadata?.defaultValue ?? metadata?.DefaultValue ?? null,
            confirmedValue: entry?.confirmedValue ?? null,
            suggestedValue: entry?.suggestedValue ?? null,
            status: entry?.status ?? 'unconfirmed',
            source: entry?.source ?? 'ai_suggestion'
        };
    }

    _buildEnumOptions(options, currentValue) {
        const normalizedCurrent = currentValue == null ? '' : String(currentValue);
        const normalizedOptions = Array.isArray(options) ? options : [];
        const optionRows = normalizedOptions.map(option => {
            const value = option?.value ?? option?.Value ?? option;
            const label = option?.label ?? option?.Label ?? value;
            const selected = String(value ?? '') === normalizedCurrent ? 'selected' : '';
            return `<option value="${this._escapeHtml(String(value ?? ''))}" ${selected}>${this._escapeHtml(String(label ?? ''))}</option>`;
        });

        return [`<option value="">请选择</option>`, ...optionRows].join('');
    }

    _buildCameraBindingOptions(currentValue) {
        const normalizedCurrent = currentValue == null ? '' : String(currentValue);
        const optionRows = this.cameraBindingsCache.map(binding => {
            const value = String(binding?.id ?? '').trim();
            const label = `${binding?.displayName || value}${binding?.serialNumber ? ` (${binding.serialNumber})` : ''}`;
            const selected = value === normalizedCurrent ? 'selected' : '';
            return `<option value="${this._escapeHtml(value)}" ${selected}>${this._escapeHtml(label)}</option>`;
        });

        if (!optionRows.some(option => option.includes('selected')) && normalizedCurrent) {
            optionRows.unshift(`<option value="${this._escapeHtml(normalizedCurrent)}" selected>${this._escapeHtml(normalizedCurrent)}</option>`);
        }

        return [`<option value="">请选择相机绑定</option>`, ...optionRows].join('');
    }

    _readPendingDraftInputValue(inputEl) {
        if (!inputEl) return null;

        const fieldType = String(inputEl.dataset.fieldType || '').trim().toLowerCase();
        const rawValue = inputEl.value;
        return this._normalizePendingValueByType(rawValue, fieldType);
    }

    _resolvePendingDraftParameterKey(operatorId, parameterName) {
        const operatorDrafts = this.pendingParameterDrafts[String(operatorId || '').trim()] || {};
        const normalizedName = String(parameterName || '').trim().toLowerCase();
        const existingKey = Object.keys(operatorDrafts).find(key => key.toLowerCase() === normalizedName);
        return existingKey || String(parameterName || '').trim();
    }

    _getPendingDraftValue(operatorId, parameterName) {
        return this._getPendingDraftConfirmedValue(operatorId, parameterName);
    }

    _readOperatorParameterValue(operator, parameterName) {
        if (!operator || !parameterName) return '';

        const normalizedName = String(parameterName).trim().toLowerCase();
        const parameters = operator?.parameters ?? operator?.Parameters ?? null;

        if (Array.isArray(parameters)) {
            const matched = parameters.find(item =>
                String(item?.name ?? item?.Name ?? '').trim().toLowerCase() === normalizedName
            );
            if (!matched) return '';
            return matched?.value ?? matched?.Value ?? '';
        }

        if (parameters && typeof parameters === 'object') {
            const matchedKey = Object.keys(parameters).find(key => key.toLowerCase() === normalizedName);
            return matchedKey ? parameters[matchedKey] : '';
        }

        return '';
    }

    _buildFlowWithPendingDrafts(flow) {
        if (!flow || typeof flow !== 'object') return flow;

        const clonedFlow = typeof structuredClone === 'function'
            ? structuredClone(flow)
            : JSON.parse(JSON.stringify(flow));
        const operators = this._extractOperators(clonedFlow);
        const pending = this._normalizePendingParameters(
            this.currentResult?.pendingParameters ?? this.currentResult?.PendingParameters
        );

        pending.forEach(item => {
            const context = this._resolvePendingOperatorContext(item.operatorId, operators);
            if (!context.operator) return;

            item.parameterNames.forEach(parameterName => {
                const confirmedValue = this._getPendingDraftConfirmedValue(item.operatorId, parameterName);
                const fieldType = this._normalizePendingFieldType(
                    this._findMetadataParameter(
                        this._getCachedOperatorMetadata(context.operatorType),
                        parameterName
                    )
                );
                if (!this._hasPendingDraftValue(confirmedValue, fieldType)) return;
                this._writeOperatorParameterValue(
                    context.operator,
                    parameterName,
                    confirmedValue
                );
            });
        });

        return clonedFlow;
    }

    _writeOperatorParameterValue(operator, parameterName, value) {
        if (!operator || !parameterName) return;

        if (Array.isArray(operator.parameters)) {
            const matched = operator.parameters.find(item =>
                String(item?.name ?? item?.Name ?? '').trim().toLowerCase() === String(parameterName).trim().toLowerCase()
            );
            if (matched) {
                if ('value' in matched || !('Value' in matched)) {
                    matched.value = value;
                } else {
                    matched.Value = value;
                }
                return;
            }

            operator.parameters.push({
                name: parameterName,
                value
            });
            return;
        }

        if (operator.parameters && typeof operator.parameters === 'object') {
            const matchedKey = Object.keys(operator.parameters).find(key =>
                key.toLowerCase() === String(parameterName).trim().toLowerCase()
            );
            operator.parameters[matchedKey || parameterName] = value;
            return;
        }

        operator.parameters = [{ name: parameterName, value }];
    }

    _scrollToPendingDraftGroup(groupKey) {
        const scrollContainer = this.container?.querySelector('#ai-results-scroll');
        if (!scrollContainer || !groupKey) return;

        const group = Array.from(scrollContainer.querySelectorAll('[data-draft-group]'))
            .find(element => element.dataset.draftGroup === groupKey);
        if (!group) return;

        scrollContainer.scrollTo({
            top: Math.max(0, group.offsetTop - 12),
            behavior: 'smooth'
        });

        const firstInput = group.querySelector('[data-draft-input="true"]');
        if (firstInput && typeof firstInput.focus === 'function') {
            setTimeout(() => firstInput.focus(), 180);
        }

        this._highlightPendingDraftGroup(group);
    }

    _highlightPendingDraftGroup(group) {
        if (!group) return;
        group.classList.add('is-highlighted');
        if (this.pendingParameterHighlightTimer) {
            clearTimeout(this.pendingParameterHighlightTimer);
        }
        this.pendingParameterHighlightTimer = setTimeout(() => {
            group.classList.remove('is-highlighted');
            this.pendingParameterHighlightTimer = null;
        }, 1800);
    }

    _pickPendingDraftFile(operatorId, parameterName) {
        if (this.isGenerating) return;
        this.pendingParameterFilePickContext = {
            operatorId: String(operatorId || '').trim(),
            parameterName: String(parameterName || '').trim()
        };
        webMessageBridge.sendMessage('PickFileCommand', {
            parameterName: 'aiPendingParameterFile',
            filter: 'All Files|*.*'
        });
    }

    _buildPendingParameterReviewRequest() {
        const flow = this._getCurrentFlowJson();
        const pending = this._normalizePendingParameters(
            this.currentResult?.pendingParameters ?? this.currentResult?.PendingParameters
        );
        const operators = this._getPendingOperatorSourceOperators(flow || null);
        const input = this.container?.querySelector('#ai-input');
        const extraNote = String(input?.value || '').trim();
        const queuedHint = String(this.nextHintDraft || '').trim();

        if (!flow || pending.length === 0) {
            return null;
        }

        const lines = [
            '请严格基于当前 existingFlowJson 审核这套方案。',
            '要求：保持流程结构稳定，仅调整参数和必要补充信息；不要无关重建。'
        ];

        const filledLines = [];
        const unfilledLines = [];
        let filledCount = 0;
        let totalCount = 0;

        pending.forEach(item => {
            const context = this._resolvePendingOperatorContext(item.operatorId, operators);
            const filledPairs = [];
            const missingNames = [];
            const metadata = this._getCachedOperatorMetadata(context.operatorType);

            item.parameterNames.forEach(parameterName => {
                totalCount += 1;
                const fieldType = this._normalizePendingFieldType(this._findMetadataParameter(metadata, parameterName));
                const value = this._getPendingDraftConfirmedValue(item.operatorId, parameterName);
                if (this._hasPendingDraftValue(value, fieldType)) {
                    filledCount += 1;
                    filledPairs.push(`${parameterName}=${this._stringifyPendingDraftValue(value, fieldType)}`);
                } else {
                    missingNames.push(parameterName);
                }
            });

            if (filledPairs.length > 0) {
                filledLines.push(`- ${context.label}：${filledPairs.join('；')}`);
            }
            if (missingNames.length > 0) {
                unfilledLines.push(`- ${context.label}：仍缺少 ${missingNames.join('、')}`);
            }
        });

        if (filledLines.length > 0) {
            lines.push('我已补录以下参数：');
            lines.push(...filledLines);
        } else {
            lines.push('我暂时还没有填入任何参数值，请继续指出最关键的缺项。');
        }

        if (unfilledLines.length > 0) {
            lines.push('以下参数仍未填写，请继续保留为待确认项并说明还缺什么：');
            lines.push(...unfilledLines);
        }

        const missingResources = this._normalizeMissingResources(
            this.currentResult?.missingResources ?? this.currentResult?.MissingResources
        );
        if (missingResources.length > 0) {
            lines.push('当前仍存在缺失资源：');
            missingResources.forEach(item => {
                const detail = item.description || item.resourceKey || item.resourceType || '缺少资源';
                lines.push(`- ${detail}`);
            });
        }

        if (queuedHint) {
            lines.push(`附加提示：${queuedHint}`);
        }

        if (extraNote) {
            lines.push(`用户补充说明：${extraNote}`);
        }

        const userMessage = `提交参数审核：已填写 ${filledCount}/${totalCount} 项${extraNote ? '，已附加补充说明' : ''}。`;
        return {
            hint: lines.join('\n'),
            userMessage,
            existingFlowJson: flow
        };
    }

    _stringifyPendingDraftValue(value, fieldType = '') {
        if (this._isPendingBooleanField(fieldType)) {
            const normalized = this.normalizeBooleanLike(value);
            return normalized === null ? '' : (normalized ? 'true' : 'false');
        }
        return String(value ?? '');
    }

    _normalizePendingParameters(items) {
        if (!Array.isArray(items)) return [];

        return items
            .map(item => {
                const rawNames = item?.parameterNames ?? item?.ParameterNames;
                const parameterNames = Array.isArray(rawNames)
                    ? [...new Set(rawNames.map(name => String(name || '').trim()).filter(Boolean))]
                    : [];

                return {
                    operatorId: String(item?.operatorId ?? item?.OperatorId ?? '').trim(),
                    actualOperatorId: String(item?.actualOperatorId ?? item?.ActualOperatorId ?? '').trim(),
                    parameterNames
                };
            })
            .filter(item => item.operatorId || item.actualOperatorId || item.parameterNames.length > 0);
    }

    _normalizeMissingResources(items) {
        if (!Array.isArray(items)) return [];

        return items
            .map(item => ({
                resourceType: String(item?.resourceType ?? item?.ResourceType ?? '').trim(),
                resourceKey: String(item?.resourceKey ?? item?.ResourceKey ?? '').trim(),
                description: String(item?.description ?? item?.Description ?? '').trim()
            }))
            .filter(item => item.resourceType || item.resourceKey || item.description);
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
    _typewriterEffect(el, text, chunkSize = 3) {
        if (!el) return;
        el.textContent = '';
        let idx = 0;
        const write = () => {
            if (idx < text.length) {
                el.textContent += text.slice(idx, idx + chunkSize);
                idx += chunkSize;
                requestAnimationFrame(write);
            }
        };
        write();
    }
    
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
    
    _addMessage(role, text, options = {}) {
        const container = this.container.querySelector('#ai-chat-container');
        const msg = document.createElement('div');
        msg.className = `ai-message ${role}`;

        const safeText = this._escapeHtml(text);
        
        if (role === 'ai') {
            msg.innerHTML = `<div class="ai-bubble">${safeText}</div>`;
        } else if (role === 'user') {
            msg.innerHTML = `<div class="user-bubble">${safeText}</div>`;
        } else {
            msg.innerHTML = `<div class="system-bubble">${safeText}</div>`;
        }
        
        container.appendChild(msg);
        this._scrollToBottom();
        return msg;
    }
    
    _startAssistantTurn({ activate = true, statusText = '生成中', statusTone = 'streaming', openReasoning = false, openReply = true } = {}) {
        const container = this.container.querySelector('#ai-chat-container');
        if (!container) return null;

        const msg = document.createElement('div');
        msg.className = 'ai-message ai ai-message-rich';
        msg.innerHTML = `
            <div class="ai-assistant-card" data-turn-tone="${this._escapeHtml(statusTone)}">
                <div class="ai-assistant-card-header">
                    <div class="ai-assistant-card-title">AI 工作流助手</div>
                    <div class="ai-assistant-status is-${this._escapeHtml(statusTone)}">${this._escapeHtml(statusText)}</div>
                </div>
                <details class="ai-assistant-section ai-assistant-reasoning-section" ${openReasoning ? 'open' : ''} hidden>
                    <summary>生成诊断</summary>
                    <div class="ai-assistant-section-body ai-assistant-reasoning-body"></div>
                </details>
                <details class="ai-assistant-section ai-assistant-reply-section" ${openReply ? 'open' : ''} hidden>
                    <summary>回复</summary>
                    <div class="ai-assistant-section-body ai-assistant-reply-body"></div>
                </details>
                <details class="ai-assistant-section ai-assistant-clarification-section" hidden>
                    <summary>需求澄清</summary>
                    <div class="ai-assistant-section-body ai-assistant-clarification-body"></div>
                </details>
                <section class="ai-assistant-section ai-assistant-failure-section" hidden>
                    <div class="ai-assistant-panel-label">失败诊断</div>
                    <div class="ai-assistant-section-body ai-assistant-failure-body"></div>
                </section>
            </div>
        `;

        container.appendChild(msg);
        const turn = {
            root: msg,
            card: msg.querySelector('.ai-assistant-card'),
            statusEl: msg.querySelector('.ai-assistant-status'),
            reasoningSection: msg.querySelector('.ai-assistant-reasoning-section'),
            reasoningBody: msg.querySelector('.ai-assistant-reasoning-body'),
            replySection: msg.querySelector('.ai-assistant-reply-section'),
            replyBody: msg.querySelector('.ai-assistant-reply-body'),
            clarificationSection: msg.querySelector('.ai-assistant-clarification-section'),
            clarificationBody: msg.querySelector('.ai-assistant-clarification-body'),
            failureSection: msg.querySelector('.ai-assistant-failure-section'),
            failureBody: msg.querySelector('.ai-assistant-failure-body')
        };

        if (activate) {
            this.activeAssistantTurn = turn;
        }

        this._scrollToBottom();
        return turn;
    }
    
    _updateThinkingStep(chainId, stepId, text) {}

    _setAssistantTurnStatus(turn, statusText, tone = 'streaming') {
        if (!turn?.statusEl || !turn?.card) return;
        turn.statusEl.textContent = statusText;
        turn.statusEl.className = `ai-assistant-status is-${tone}`;
        turn.card.dataset.turnTone = tone;
    }

    _appendAssistantStreamText(field, text) {
        const turn = this.activeAssistantTurn;
        if (!turn || !text) return;

        const body = field === 'reasoning' ? turn.reasoningBody : turn.replyBody;
        const section = field === 'reasoning' ? turn.reasoningSection : turn.replySection;
        if (!body || !section) return;

        section.hidden = false;
        const shouldFollowBottom = this._isNearBottom(body);
        body.textContent += text;
        if (shouldFollowBottom) {
            body.scrollTop = body.scrollHeight;
        }
        this._scrollToBottom();
    }

    _setAssistantSectionText(turn, field, text, { keepExisting = false } = {}) {
        if (!turn) return;
        const body = field === 'reasoning' ? turn.reasoningBody : turn.replyBody;
        const section = field === 'reasoning' ? turn.reasoningSection : turn.replySection;
        if (!body || !section) return;

        const value = String(text || '').trim();
        if (!value) {
            section.hidden = true;
            body.textContent = '';
            return;
        }

        section.hidden = false;
        body.textContent = keepExisting && body.textContent ? `${body.textContent}${value}` : value;
    }

    _renderAssistantFailure(turn, payload = {}) {
        if (!turn?.failureSection || !turn?.failureBody) return;

        const failurePayload = payload.failure || payload.Failure || null;
        const failureSummary = failurePayload?.failureSummary
            || failurePayload?.FailureSummary
            || payload.failureSummary
            || payload.FailureSummary
            || null;
        const diagnostics = Array.isArray(failurePayload?.diagnostics)
            ? failurePayload.diagnostics
            : (Array.isArray(failurePayload?.Diagnostics)
                ? failurePayload.Diagnostics
                : (Array.isArray(payload.lastAttemptDiagnostics)
                    ? payload.lastAttemptDiagnostics
                    : (Array.isArray(payload.LastAttemptDiagnostics) ? payload.LastAttemptDiagnostics : [])));
        const manualRetry = payload.manualRetry || payload.ManualRetry || null;
        const summaryText = failurePayload?.summary
            || failurePayload?.Summary
            || failureSummary?.message
            || payload.failureSummary
            || payload.errorMessage
            || payload.message
            || '生成失败';
        const repairTarget = failureSummary?.repairTarget || manualRetry?.repairTarget || '';
        const lastOutputSummary = failureSummary?.lastOutputSummary || manualRetry?.lastOutputSummary || '';
        const issueLines = diagnostics
            .flatMap(item => Array.isArray(item?.issues) ? item.issues : (Array.isArray(item?.Issues) ? item.Issues : []))
            .slice(0, 6);

        turn.failureSection.hidden = false;
        turn.failureBody.innerHTML = `
            <div class="ai-assistant-failure-summary">${this._escapeHtml(String(summaryText))}</div>
            ${repairTarget ? `<div class="ai-assistant-failure-meta"><span>关键修复</span>${this._escapeHtml(String(repairTarget))}</div>` : ''}
            ${lastOutputSummary ? `<div class="ai-assistant-failure-meta"><span>上一轮输出摘要</span>${this._escapeHtml(String(lastOutputSummary))}</div>` : ''}
            ${issueLines.length > 0 ? `
                <div class="ai-assistant-failure-list">
                    ${issueLines.map(issue => `
                        <div class="ai-assistant-failure-item">
                            <div class="ai-assistant-failure-item-title">${this._escapeHtml(`[${issue?.category || issue?.Category || '--'}/${issue?.code || issue?.Code || '--'}] ${issue?.message || issue?.Message || ''}`)}</div>
                            ${(issue?.repairHint || issue?.RepairHint) ? `<div class="ai-assistant-failure-item-hint">${this._escapeHtml(String(issue?.repairHint || issue?.RepairHint || ''))}</div>` : ''}
                        </div>
                    `).join('')}
                </div>
            ` : ''}
        `;
        this._scrollToBottom();
    }

    _normalizeRequirementBrief(item) {
        if (!item || typeof item !== 'object') return null;

        const clarificationQuestions = Array.isArray(item.clarificationQuestions)
            ? item.clarificationQuestions
            : (Array.isArray(item.ClarificationQuestions) ? item.ClarificationQuestions : []);

        const normalizeStringList = (value) => Array.isArray(value)
            ? [...new Set(value.map(item => String(item || '').trim()).filter(Boolean))]
            : [];

        return {
            scenarioKey: String(item.scenarioKey ?? item.ScenarioKey ?? '').trim(),
            scenarioName: String(item.scenarioName ?? item.ScenarioName ?? '').trim(),
            intentType: String(item.intentType ?? item.IntentType ?? '').trim(),
            requirementMode: this._normalizeRequirementMode(item.requirementMode ?? item.RequirementMode ?? 'strict'),
            confidence: Number(item.confidence ?? item.Confidence ?? 0),
            hasOpenQuestions: Boolean(item.hasOpenQuestions ?? item.HasOpenQuestions),
            clarificationRequired: Boolean(item.clarificationRequired ?? item.ClarificationRequired),
            canGenerateDraftNow: Boolean(item.canGenerateDraftNow ?? item.CanGenerateDraftNow),
            draftRiskLevel: String(item.draftRiskLevel ?? item.DraftRiskLevel ?? 'medium').trim() || 'medium',
            requiredFields: normalizeStringList(item.requiredFields ?? item.RequiredFields),
            blockingClarificationFields: normalizeStringList(item.blockingClarificationFields ?? item.BlockingClarificationFields),
            nonBlockingMissingFields: normalizeStringList(item.nonBlockingMissingFields ?? item.NonBlockingMissingFields),
            knownFacts: normalizeStringList(item.knownFacts ?? item.KnownFacts),
            missingFacts: normalizeStringList(item.missingFacts ?? item.MissingFacts),
            attachmentFacts: normalizeStringList(item.attachmentFacts ?? item.AttachmentFacts),
            objectName: String(item.objectName ?? item.ObjectName ?? '').trim(),
            imageSource: String(item.imageSource ?? item.ImageSource ?? '').trim(),
            outputTarget: String(item.outputTarget ?? item.OutputTarget ?? '').trim(),
            decisionRule: String(item.decisionRule ?? item.DecisionRule ?? '').trim(),
            roiRequirement: String(item.roiRequirement ?? item.RoiRequirement ?? '').trim(),
            calibrationRequirement: String(item.calibrationRequirement ?? item.CalibrationRequirement ?? '').trim(),
            objectTypes: normalizeStringList(item.objectTypes ?? item.ObjectTypes),
            defectTypes: normalizeStringList(item.defectTypes ?? item.DefectTypes),
            measurementTargets: normalizeStringList(item.measurementTargets ?? item.MeasurementTargets),
            requiredResources: normalizeStringList(item.requiredResources ?? item.RequiredResources),
            clarificationQuestions: clarificationQuestions
                .map(question => ({
                    field: String(question?.field ?? question?.Field ?? '').trim(),
                    question: String(question?.question ?? question?.Question ?? '').trim(),
                    required: Boolean(question?.required ?? question?.Required),
                    reason: String(question?.reason ?? question?.Reason ?? '').trim(),
                    priority: String(question?.priority ?? question?.Priority ?? '').trim(),
                    options: normalizeStringList(question?.options ?? question?.Options)
                }))
                .filter(question => question.question || question.field || question.reason)
        };
    }

    _buildClarificationFollowupText(brief) {
        if (!brief) return '';

        const lines = ['请先补充以下阻断澄清项，再继续生成：'];
        if (brief.scenarioName) {
            lines.push(`场景：${brief.scenarioName}`);
        }
        if (brief.objectName) {
            lines.push(`对象：${brief.objectName}`);
        }
        if (brief.outputTarget) {
            lines.push(`输出目标：${brief.outputTarget}`);
        }

        if (brief.knownFacts.length > 0) {
            lines.push('已知事实：');
            brief.knownFacts.forEach(item => lines.push(`- ${item}`));
        }

        if (brief.missingFacts.length > 0) {
            lines.push('阻断待确认项：');
            brief.missingFacts.forEach(item => lines.push(`- ${item}`));
        }

        if (brief.blockingClarificationFields.length > 0) {
            lines.push(`阻断字段：${brief.blockingClarificationFields.map(field => this._getRequirementFieldLabel(field)).join('、')}`);
        }

        if (brief.clarificationQuestions.length > 0) {
            lines.push('澄清问题：');
            brief.clarificationQuestions.forEach((question, index) => {
                const suffix = question.reason ? `（${question.reason}）` : '';
                const options = question.options.length > 0 ? ` 可选：${question.options.join(' / ')}` : '';
                lines.push(`${index + 1}. ${question.question}${suffix}${options}`);
            });
        }

        if (brief.nonBlockingMissingFields.length > 0) {
            lines.push(`非阻断待补：${brief.nonBlockingMissingFields.map(field => this._getRequirementFieldLabel(field)).join('、')}`);
        }

        lines.push(brief.canGenerateDraftNow
            ? '如果想先看草稿，可以切换到“草稿优先”模式。'
            : '补齐关键字段后再继续，会更稳。');
        return lines.join('\n');
    }

    _buildClarificationSafeHint(brief) {
        if (!brief) return '';

        const lines = ['需求澄清上下文：'];
        if (brief.scenarioName) lines.push(`场景：${brief.scenarioName}`);
        if (brief.intentType) lines.push(`意图：${brief.intentType}`);
        if (brief.objectName) lines.push(`对象：${brief.objectName}`);
        if (brief.outputTarget) lines.push(`输出目标：${brief.outputTarget}`);
        if (brief.knownFacts.length > 0) {
            lines.push(`已知事实：${brief.knownFacts.join('；')}`);
        }
        if (brief.missingFacts.length > 0) {
            lines.push(`仍缺字段：${brief.missingFacts.join('；')}`);
        }
        if (brief.blockingClarificationFields.length > 0) {
            lines.push(`阻断字段：${brief.blockingClarificationFields.map(field => this._getRequirementFieldLabel(field)).join('；')}`);
        }
        if (brief.nonBlockingMissingFields.length > 0) {
            lines.push(`非阻断待补：${brief.nonBlockingMissingFields.map(field => this._getRequirementFieldLabel(field)).join('；')}`);
        }
        lines.push('请只根据用户下一轮明确补充的信息更新需求，不要把上面的澄清问题或示例选项当作用户答案。');
        return lines.join('\n');
    }

    _getRequirementFieldLabel(field) {
        const key = String(field || '').trim();
        const fieldLabelMap = {
            scene: '场景类型',
            object_type: '检测对象',
            defect_type: '缺陷类别',
            measurement_target: '测量目标',
            measurement_unit: '测量单位',
            sequence_rule: '线序规则',
            output_target: '输出目标',
            model_path: '模型资源',
            roi: 'ROI范围',
            plc_address: 'PLC地址',
            database_table: '数据库表',
            threshold: '阈值',
            calibration: '标定方式',
            calibration_file: '标定文件',
            ambiguous_negative_signal: '歧义信息'
        };
        return fieldLabelMap[key] || key || '未命名字段';
    }

    _renderRequirementBrief(data = null) {
        const card = this.container?.querySelector('#ai-result-requirement-brief-card');
        const container = this.container?.querySelector('#ai-result-requirement-brief');
        if (!card || !container) return null;

        const brief = this._normalizeRequirementBrief(data?.requirementBrief ?? data?.RequirementBrief ?? null);
        if (!brief) {
            this._resetClarificationSelectionDraft();
            card.hidden = true;
            container.classList.add('is-empty');
            container.innerHTML = '<div class="ai-followup-empty">当前尚未提炼出需求摘要。</div>';
            return null;
        }
        this._resetClarificationSelectionDraft();

        const confidence = Number.isFinite(brief.confidence) ? brief.confidence : 0;
        const confidenceText = `${Math.max(0, Math.min(100, Math.round(confidence * 100)))}%`;
        const requirementModeLabel = brief.requirementMode === 'draft' ? '草稿优先' : '严格澄清';
        const riskLabel = String(brief.draftRiskLevel || 'medium').trim() || 'medium';
        const summary = this._buildClarificationFollowupText(brief);
        const safeHint = this._buildClarificationSafeHint(brief);
        const requiredQuestionCount = brief.clarificationQuestions.filter(question => question.required).length;
        const confidenceEl = this.container?.querySelector('#ai-requirement-confidence');
        if (confidenceEl) {
            confidenceEl.textContent = brief.clarificationRequired
                ? `${requiredQuestionCount || brief.missingFacts.length} 项待确认`
                : `置信度 ${confidenceText}`;
            confidenceEl.classList.toggle('is-warning', Boolean(brief.clarificationRequired));
        }
        const metaChips = [
            brief.scenarioName ? `场景：${brief.scenarioName}` : '',
            brief.intentType ? `意图：${brief.intentType}` : '',
            `模式：${requirementModeLabel}`,
            `置信度：${confidenceText}`,
            `风险：${riskLabel}`,
            brief.objectName ? `对象：${brief.objectName}` : '',
            brief.outputTarget ? `输出：${brief.outputTarget}` : '',
            brief.imageSource && brief.imageSource !== 'unknown' ? `图像源：${brief.imageSource}` : '',
            brief.decisionRule ? `判定：${brief.decisionRule}` : '',
            brief.roiRequirement && brief.roiRequirement !== 'none' ? `ROI：${brief.roiRequirement}` : '',
            brief.calibrationRequirement && brief.calibrationRequirement !== 'none' ? `标定：${brief.calibrationRequirement}` : ''
        ].filter(Boolean);

        const renderTagList = (items, emptyText, tone = '') => {
            if (!Array.isArray(items) || items.length === 0) {
                return `<div class="ai-requirement-brief-empty">${this._escapeHtml(emptyText)}</div>`;
            }

            const toneClass = tone ? ` is-${tone}` : '';
            return `<div class="ai-requirement-brief-tags">${items
                .map(item => `<span class="ai-requirement-brief-tag${toneClass}">${this._escapeHtml(String(item))}</span>`)
                .join('')}</div>`;
        };

        const renderFieldChips = (items, emptyText, tone = '') => {
            const normalized = this._normalizeRuntimeFieldList(items);
            if (normalized.length === 0) {
                return `<div class="ai-requirement-brief-empty">${this._escapeHtml(emptyText)}</div>`;
            }

            const toneClass = tone ? ` is-${tone}` : '';
            return `<div class="ai-requirement-brief-tags">${normalized
                .map(field => `
                    <span class="ai-requirement-brief-tag${toneClass}" title="${this._escapeHtml(field)}">
                        ${this._escapeHtml(this._getRequirementFieldLabel(field))}
                    </span>`)
                .join('')}</div>`;
        };

        const renderQuestionList = (questions) => {
            if (!Array.isArray(questions) || questions.length === 0) {
                return '<div class="ai-requirement-brief-empty">当前没有进一步澄清问题。</div>';
            }

            return `<div class="ai-requirement-question-list">${questions.map((question, index) => {
                const requiredLabel = question.required ? '必填' : '建议';
                const priority = question.priority ? ` · ${question.priority}` : '';
                const fieldLabel = this._getRequirementFieldLabel(question.field);
                const options = question.options.length > 0
                    ? `
                        <div class="ai-requirement-question-options-title">参考选项，点击后生成澄清回答草稿</div>
                        <div class="ai-requirement-question-options">${question.options
                            .map(option => `
                                <button class="ai-requirement-question-option" type="button"
                                    aria-pressed="false"
                                    data-clarification-field="${this._escapeHtml(question.field)}"
                                    data-clarification-value="${this._escapeHtml(option)}">
                                    ${this._escapeHtml(option)}
                                </button>`)
                            .join('')}</div>`
                    : '';
                return `
                    <article class="ai-requirement-question ${question.required ? 'is-required' : 'is-recommended'}">
                        <div class="ai-requirement-question-header">
                            <span class="ai-requirement-question-level">${requiredLabel}${this._escapeHtml(priority)}</span>
                            ${fieldLabel ? `<span class="ai-requirement-question-field">${this._escapeHtml(fieldLabel)}</span>` : ''}
                        </div>
                        <div class="ai-requirement-question-title">${index + 1}. ${this._escapeHtml(question.question)}</div>
                        ${question.reason ? `<div class="ai-requirement-question-reason">${this._escapeHtml(question.reason)}</div>` : ''}
                        ${options}
                    </article>
                `;
            }).join('')}</div>`;
        };

        card.hidden = false;
        container.classList.remove('is-empty');
        container.innerHTML = `
            <div class="ai-requirement-brief-summary">
                <div class="ai-requirement-brief-title">当前需求摘要</div>
                <div class="ai-requirement-brief-chip-row">
                    ${metaChips.map(item => `<span class="ai-requirement-brief-chip">${this._escapeHtml(item)}</span>`).join('')}
                </div>
            </div>
            <div class="ai-requirement-brief-grid">
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">已知事实</div>
                    ${renderTagList(brief.knownFacts, '当前没有提炼出已知事实。', 'known')}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">阻断待确认</div>
                    ${this._renderMissingFactsWithActions(brief.missingFacts)}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">阻断字段</div>
                    ${renderFieldChips(brief.blockingClarificationFields, '当前没有阻断字段。', 'blocking')}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">澄清问题</div>
                    ${renderQuestionList(brief.clarificationQuestions)}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">非阻断待补</div>
                    ${renderFieldChips(brief.nonBlockingMissingFields, '当前没有非阻断待补字段。', 'nonblocking')}
                </section>
                <section class="ai-requirement-brief-section">
                    <div class="ai-requirement-brief-section-label">附件信号</div>
                    ${renderTagList(brief.attachmentFacts, '当前没有附件信号。')}
                </section>
            </div>
            <div class="ai-requirement-brief-actions">
                <button class="ai-requirement-brief-action" type="button" data-brief-action="copy">复制澄清清单</button>
                <button class="ai-requirement-brief-action" type="button" data-brief-action="insert">插入输入框</button>
                <button class="ai-requirement-brief-action" type="button" data-brief-action="queue">挂到下一轮</button>
                <button class="ai-requirement-brief-action" type="button" data-brief-action="draft">切到草稿模式</button>
                <button class="ai-requirement-brief-action is-primary" type="button" id="ai-btn-send-clarification" data-brief-action="send-clarification" disabled>发送澄清回答</button>
            </div>
        `;

        container.querySelectorAll('[data-brief-action]').forEach(button => {
            const action = button.dataset.briefAction;
            button.disabled = this.isGenerating || action === 'send-clarification';
            button.addEventListener('click', async () => {
                if (action === 'copy') {
                    const copied = await this._copyTextToClipboard(summary);
                    this._addMessage('system', copied ? '澄清清单已复制。' : '复制失败，请手动复制。');
                    return;
                }

                if (action === 'insert') {
                    this._appendFollowupTextToInput(summary);
                    this._addMessage('system', '澄清清单已插入输入框。');
                    return;
                }

                if (action === 'queue') {
                    this.nextHintDraft = safeHint || summary;
                    this._renderQueuedHintBanner();
                    this._addMessage('system', '已挂载安全澄清上下文，下一轮不会把示例选项误当作用户答案。');
                    return;
                }

                if (action === 'draft') {
                    this._setRequirementMode('draft');
                    return;
                }

                if (action === 'send-clarification') {
                    const draftText = this._buildClarificationAnswerDraft();
                    if (!draftText) {
                        this._addMessage('system', '请先选择澄清选项，或直接在输入框里补充答案。');
                        return;
                    }
                    this._mergeClarificationDraftIntoInput(draftText);
                    this._handleGenerate();
                }
            });
        });

        this._bindClarificationOptionButtons(container);

        return brief;
    }

    _renderAssistantClarification(turn, payload = {}) {
        if (!turn?.clarificationSection || !turn?.clarificationBody) return;

        const brief = this._normalizeRequirementBrief(payload.requirementBrief ?? payload.RequirementBrief ?? null);
        const summary = String(payload.aiExplanation ?? payload.AiExplanation ?? payload.errorMessage ?? payload.message ?? '').trim()
            || (brief ? this._buildClarificationFollowupText(brief).split('\n')[0] : '当前需求需要先补充信息。');
        const questionItems = brief?.clarificationQuestions || [];
        const missingFacts = brief?.missingFacts || [];
        const knownFacts = brief?.knownFacts || [];

        turn.clarificationSection.hidden = false;
        turn.clarificationBody.innerHTML = `
            <div class="ai-assistant-clarification-summary">${this._escapeHtml(summary)}</div>
            ${knownFacts.length > 0 ? `
                <div class="ai-assistant-clarification-block">
                    <div class="ai-assistant-clarification-label">已知事实</div>
                    <div class="ai-assistant-clarification-tags">
                        ${knownFacts.slice(0, 6).map(item => `<span class="ai-assistant-clarification-tag">${this._escapeHtml(item)}</span>`).join('')}
                    </div>
                </div>
            ` : ''}
            ${missingFacts.length > 0 ? `
                <div class="ai-assistant-clarification-block">
                    <div class="ai-assistant-clarification-label">待确认项</div>
                    <div class="ai-assistant-clarification-tags">
                        ${missingFacts.slice(0, 6).map(item => `<span class="ai-assistant-clarification-tag">${this._escapeHtml(item)}</span>`).join('')}
                    </div>
                </div>
            ` : ''}
            ${questionItems.length > 0 ? `
                <div class="ai-assistant-clarification-list">
                    ${questionItems.slice(0, 3).map((question, index) => `
                        <div class="ai-assistant-clarification-item">
                            <div class="ai-assistant-clarification-item-title">${this._escapeHtml(`${index + 1}. ${question.question}`)}</div>
                            ${question.reason ? `<div class="ai-assistant-clarification-item-hint">${this._escapeHtml(question.reason)}</div>` : ''}
                            ${question.options.length > 0 ? `<div class="ai-assistant-clarification-options">${question.options.map(option => `
                                <button class="ai-assistant-clarification-option" type="button"
                                    aria-pressed="false"
                                    data-clarification-field="${this._escapeHtml(question.field)}"
                                    data-clarification-value="${this._escapeHtml(option)}">
                                    ${this._escapeHtml(option)}
                                </button>`).join('')}</div>` : ''}
                        </div>
                    `).join('')}
                </div>
            ` : '<div class="ai-assistant-clarification-empty">当前没有更多澄清问题。</div>'}
        `;
        this._bindClarificationOptionButtons(turn.clarificationBody);
        this._scrollToBottom();
    }

    _resolveAssistantStatusPresentation(payload = {}) {
        const status = String(payload?.status ?? payload?.Status ?? '').trim().toLowerCase();
        const manualRetry = payload?.manualRetry ?? payload?.ManualRetry ?? payload?.manual_retry ?? null;
        const clarificationRequired = Boolean(payload?.clarificationRequired ?? payload?.ClarificationRequired);

        if (clarificationRequired || status === 'clarification_required') {
            return { text: '待澄清', tone: 'warning' };
        }
        if (manualRetry?.required || status === 'manual_retry_required') {
            return { text: '待手动确认', tone: 'warning' };
        }

        switch (status) {
            case 'completed':
            case 'success':
                return { text: '生成成功', tone: 'success' };
            case 'cancelled':
            case 'canceled':
            case 'user_cancelled':
            case 'user_canceled':
                return { text: '已取消', tone: 'cancelled' };
            case 'timed_out':
            case 'timeout':
                return { text: '请求超时', tone: 'failed' };
            case 'system_error':
                return { text: '系统错误', tone: 'failed' };
            case 'failed':
                return { text: '生成失败', tone: 'failed' };
            default:
                return { text: '已完成', tone: 'neutral' };
        }
    }

    _renderAssistantTurnFromPayload(turnData = {}) {
        const payload = turnData?.payload ?? turnData?.Payload ?? null;
        if (!payload || typeof payload !== 'object') {
            return null;
        }

        const presentation = this._resolveAssistantStatusPresentation(payload);
        const turn = this._startAssistantTurn({
            activate: false,
            statusText: presentation.text,
            statusTone: presentation.tone,
            openReasoning: false,
            openReply: false
        });
        if (!turn) return null;

        const reply = String(payload.reply ?? payload.Reply ?? turnData?.message ?? turnData?.Message ?? '').trim();
        const reasoning = String(payload.reasoning ?? payload.Reasoning ?? '').trim();
        this._setAssistantSectionText(turn, 'reply', reply);
        this._setAssistantSectionText(turn, 'reasoning', reasoning);

        if (this._isClarificationResult(payload)) {
            this._renderAssistantClarification(turn, payload);
        }

        if (payload.failure || payload.Failure || payload.manualRetry || payload.ManualRetry) {
            this._renderAssistantFailure(turn, payload);
        }

        return turn;
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

    _normalizeRuntimeFieldList(value) {
        if (!Array.isArray(value)) return [];
        return [...new Set(value.map(item => String(item || '').trim()).filter(Boolean))];
    }

    _buildAgentNextAction(meta = {}) {
        const turnIntent = String(meta.turnIntent || '').trim().toLowerCase();
        const interactionState = String(meta.interactionState || '').trim().toLowerCase();
        const blockingCount = Number(meta.blockingCount || 0);
        const nonBlockingCount = Number(meta.nonBlockingCount || 0);
        const pendingCount = Number(meta.pendingCount || 0);
        const missingResourceCount = Number(meta.missingResourceCount || 0);
        const hasFlow = Boolean(meta.hasFlow);
        const manualRetryRequired = Boolean(meta.manualRetryRequired);

        if (interactionState === 'clarifying' || blockingCount > 0) {
            const countText = blockingCount > 0 ? ` ${blockingCount} 个` : '';
            return `下一步：先回答${countText}阻断问题，系统会在补齐后继续生成。`;
        }

        if (interactionState === 'manual_retry' || manualRetryRequired || turnIntent === 'manual_retry_repair') {
            return '下一步：检查已回填的修复草稿并发送，只进入修复链路，不重新澄清需求。';
        }

        if (interactionState === 'modifying' || turnIntent === 'modify_flow') {
            return '下一步：等待微调完成，系统只改用户指定部分并保留其它算子和连线。';
        }

        if (interactionState === 'reviewing_parameters' || turnIntent === 'review_pending_parameters' || pendingCount > 0) {
            return pendingCount > 0
                ? `下一步：补齐 ${pendingCount} 组待确认参数，再执行统一确认。`
                : '下一步：审核待确认参数，流程结构保持不变。';
        }

        if (interactionState === 'generating') {
            return '下一步：等待模板匹配、生成、校验和 DryRun 完成。';
        }

        if (turnIntent === 'chat_or_help') {
            return hasFlow
                ? '下一步：可以继续提出微调、解释或参数审核需求。'
                : '下一步：描述检测、测量或识别目标即可开始生成流程。';
        }

        if (turnIntent === 'unknown' || interactionState === 'idle') {
            return hasFlow
                ? '下一步：说明要修改、解释或审核的具体内容。'
                : '下一步：补充检测场景、对象和输出目标，系统会进入业务链路。';
        }

        if (hasFlow) {
            return nonBlockingCount > 0 || missingResourceCount > 0
                ? `下一步：可先应用方案，也可以补齐 ${nonBlockingCount || missingResourceCount} 项非阻断信息后再确认。`
                : '下一步：确认方案后应用到流程草稿，或继续输入微调需求。';
        }

        return '下一步：继续补充需求，系统会保持业务生成链路稳定。';
    }

    _renderAgentRuntime(payload = null, { reset = false } = {}) {
        const el = this.container?.querySelector('#ai-agent-runtime');
        if (!el) return;

        if (reset) {
            this._lastAgentRuntime = null;
            el.hidden = true;
            el.innerHTML = '';
            el.className = 'ai-agent-runtime';
            return;
        }

        const source = payload || this._lastAgentRuntime;
        if (!source) {
            el.hidden = true;
            el.innerHTML = '';
            return;
        }

        this._lastAgentRuntime = source;
        const brief = this._normalizeRequirementBrief(source.requirementBrief ?? source.RequirementBrief ?? null);
        const turnIntent = this._getTurnIntent(source) || 'unknown';
        const interactionState = this._getInteractionState(source) || 'generating';
        const routerConfidence = this._getRouterConfidence(source);
        const blockingFields = this._normalizeRuntimeFieldList(source.blockingClarificationFields ?? source.BlockingClarificationFields);
        const nonBlockingFields = this._normalizeRuntimeFieldList(source.nonBlockingMissingFields ?? source.NonBlockingMissingFields);
        const effectiveBlockingFields = blockingFields.length > 0 ? blockingFields : (brief?.blockingClarificationFields || []);
        const effectiveNonBlockingFields = nonBlockingFields.length > 0 ? nonBlockingFields : (brief?.nonBlockingMissingFields || []);
        const questionCount = brief?.clarificationQuestions?.length || 0;
        const pendingParameters = this._normalizePendingParameters(source.pendingParameters ?? source.PendingParameters);
        const missingResources = this._normalizeMissingResources(source.missingResources ?? source.MissingResources);
        const flow = source.flow ?? source.Flow ?? this.currentResult?.flow ?? this.currentResult?.Flow ?? null;
        const manualRetry = source.manualRetry ?? source.ManualRetry ?? null;

        const intentLabels = {
            manual_retry_repair: '修复草稿',
            clarification_answer: '补充澄清',
            review_pending_parameters: '审核参数',
            explain_flow: '解释工程',
            modify_flow: '增量微调',
            new_flow: '新建流程',
            chat_or_help: '普通对话',
            unknown: '待判定'
        };
        const stateLabels = {
            idle: '待机',
            clarifying: '待澄清',
            generating: '生成中',
            modifying: '微调中',
            reviewing_parameters: '审核中',
            manual_retry: '修复中',
            completed: '已完成',
            failed: '失败'
        };
        const confidenceLabels = {
            high: '高',
            medium: '中',
            low: '低'
        };
        const summary = interactionState === 'clarifying'
            ? `${effectiveBlockingFields.length || questionCount} 项阻断澄清`
            : interactionState === 'modifying'
                ? '基于当前工程增量修改'
                : interactionState === 'reviewing_parameters'
                    ? '只审核待确认参数'
                    : turnIntent === 'chat_or_help'
                        ? '普通回复，不进入澄清'
                        : turnIntent === 'unknown'
                            ? '正在判定本轮意图'
                        : effectiveNonBlockingFields.length > 0
                            ? `${effectiveNonBlockingFields.length} 项非阻断缺项`
                            : '业务链路就绪';
        const blockingCount = effectiveBlockingFields.length || questionCount;
        const nextAction = this._buildAgentNextAction({
            turnIntent,
            interactionState,
            blockingCount,
            nonBlockingCount: effectiveNonBlockingFields.length,
            pendingCount: pendingParameters.length,
            missingResourceCount: missingResources.length,
            hasFlow: Boolean(flow),
            manualRetryRequired: Boolean(manualRetry?.required ?? manualRetry?.Required)
        });

        const stateClass = String(interactionState || 'idle').replace(/[^a-z0-9_-]/gi, '') || 'idle';
        el.hidden = false;
        el.className = `ai-agent-runtime is-${stateClass}`;
        el.innerHTML = `
            <div class="ai-agent-runtime-main">
                <span class="ai-agent-runtime-kicker">Agent 状态机</span>
                <strong>${this._escapeHtml(stateLabels[interactionState] || interactionState || '待机')}</strong>
                <span>${this._escapeHtml(summary)}</span>
            </div>
            <div class="ai-agent-runtime-metrics">
                <span>意图 ${this._escapeHtml(intentLabels[turnIntent] || turnIntent)}</span>
                <span>置信度 ${this._escapeHtml(confidenceLabels[routerConfidence] || routerConfidence || '--')}</span>
                <span>阻断 ${blockingCount}</span>
                <span>待补 ${effectiveNonBlockingFields.length}</span>
            </div>
            <div class="ai-agent-runtime-next">${this._escapeHtml(nextAction)}</div>
        `;
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
                pending: restoredResult?.pendingParameters,
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

    _setWorkbenchState(state) {
        if (this.workbenchState === state) return;
        // Track last non-terminal state for failure recovery
        if (state !== AiWorkbenchStates.FAILED && state !== AiWorkbenchStates.CANCELLED && state !== AiWorkbenchStates.IDLE) {
            this._lastActiveWorkbenchState = state;
        }
        this.workbenchState = state;
        this._renderWorkbenchStateBar();
    }

    _renderWorkbenchStateBar() {
        const bar = this.container?.querySelector('#ai-workbench-state-bar');
        if (!bar) return;

        const state = this.workbenchState;
        if (state === AiWorkbenchStates.IDLE) {
            bar.innerHTML = '';
            bar.classList.remove('is-active');
            return;
        }

        bar.classList.add('is-active');
        const stateToStageIndex = {
            [AiWorkbenchStates.MATCHING_TEMPLATE]: 1,
            [AiWorkbenchStates.GENERATING]: 2,
            [AiWorkbenchStates.PARSING]: 2,
            [AiWorkbenchStates.VALIDATING]: 3,
            [AiWorkbenchStates.DRY_RUNNING]: 4,
            [AiWorkbenchStates.REVIEWING_PARAMETERS]: 5,
            [AiWorkbenchStates.READY_TO_APPLY]: 6,
            [AiWorkbenchStates.APPLYING]: 6,
            [AiWorkbenchStates.APPLIED]: 6,
            [AiWorkbenchStates.CLARIFYING]: 0,
            [AiWorkbenchStates.FAILED]: -1,
            [AiWorkbenchStates.CANCELLED]: -1
        };
        const activeIndex = stateToStageIndex[state] ?? -1;

        // For FAILED/CANCELLED: determine which stage actually failed
        let failedStageIndex = activeIndex;
        if ((state === AiWorkbenchStates.FAILED || state === AiWorkbenchStates.CANCELLED) && activeIndex === -1) {
            // Infer from stage timeline: find the last failed stage
            const timeline = this._workbenchStageTimeline || [];
            const failedStageKey = timeline.filter(s => s.status === 'failed').pop()?.stage;
            if (failedStageKey) {
                const stageKeyToOrderIndex = {
                    conversation: 0, scenario_match: 0, requirement_brief: 0, clarification: 0,
                    prompt_context: 1, template_gate: 1,
                    llm: 2, parse: 2,
                    validator: 3,
                    dryrun: 4,
                    parameters: 5,
                    apply: 6
                };
                failedStageIndex = stageKeyToOrderIndex[failedStageKey] ?? 0;
            } else {
                // Fallback: use last active state to determine stage index
                failedStageIndex = stateToStageIndex[this._lastActiveWorkbenchState] ?? 0;
            }
        }

        bar.innerHTML = WORKBENCH_STAGE_ORDER.map((stage, i) => {
            let cls = 'wb-stage';
            if (state === AiWorkbenchStates.FAILED || state === AiWorkbenchStates.CANCELLED) {
                if (i < failedStageIndex) {
                    cls += ' completed';
                } else if (i === failedStageIndex) {
                    cls += ' failed';
                }
            } else if (state === AiWorkbenchStates.APPLIED) {
                cls += ' completed';
            } else if (i < activeIndex) {
                cls += ' completed';
            } else if (i === activeIndex) {
                cls += ' active';
            }
            return `<span class="${cls}">${stage.label}</span>`;
        }).join('');
    }

    // ── 生成流水线时间线 ──────────────────────────────────────

    _renderStageTimeline(timeline) {
        const card = this.container?.querySelector('#ai-result-stage-timeline-card');
        const container = this.container?.querySelector('#ai-result-stage-timeline');
        const summaryBadge = this.container?.querySelector('#ai-stage-timeline-summary');
        if (!card || !container) return;

        if (!Array.isArray(timeline) || timeline.length === 0) {
            card.hidden = true;
            container.innerHTML = '';
            return;
        }

        card.hidden = false;
        const totalMs = timeline.reduce((sum, s) => sum + (s.durationMs || 0), 0);
        const totalSec = (totalMs / 1000).toFixed(1);
        if (summaryBadge) {
            summaryBadge.textContent = `${timeline.length} 阶段 · ${totalSec}s`;
        }

        container.innerHTML = `
            <details class="ai-stage-timeline-details">
                <summary>${timeline.length} 个阶段，总耗时 ${totalSec}s</summary>
                <div class="ai-stage-timeline-list">
                    ${timeline.map(stage => {
                        const label = STAGE_DIAGNOSTIC_LABELS[stage.stage] || stage.stage;
                        const status = stage.status || 'completed';
                        const duration = stage.durationMs != null ? `${stage.durationMs}ms` : '--';
                        const statusIcon = status === 'completed' ? '&#10003;'
                            : status === 'failed' ? '&#10007;'
                            : status === 'warning' ? '&#9888;'
                            : '&#9675;';
                        const statusClass = status === 'failed' ? 'is-failed'
                            : status === 'warning' ? 'is-warning'
                            : 'is-ok';
                        return `
                            <div class="ai-stage-timeline-item ${statusClass}">
                                <span class="ai-stage-icon">${statusIcon}</span>
                                <span class="ai-stage-label">${this._escapeHtml(label)}</span>
                                <span class="ai-stage-summary">${this._escapeHtml(stage.summary || '')}</span>
                                <span class="ai-stage-duration">${duration}</span>
                            </div>
                        `;
                    }).join('')}
                </div>
            </details>
        `;
    }

    // ── 校验与 DryRun 控制台 ──────────────────────────────────

    _renderValidationConsole(data) {
        const card = this.container?.querySelector('#ai-result-validation-card');
        const container = this.container?.querySelector('#ai-result-validation');
        if (!card || !container) return;

        const diagnostics = data?.lastAttemptDiagnostics || data?.LastAttemptDiagnostics || [];
        const manualRetry = data?.manualRetry || data?.ManualRetry || null;
        const dryRun = data?.dryRunResult || data?.DryRunResult || null;
        const knowledgeDiags = data?.knowledgeDiagnostics || data?.KnowledgeDiagnostics || [];
        const hasContent = diagnostics.length > 0 || manualRetry?.required || dryRun || knowledgeDiags.length > 0;

        if (!hasContent) {
            card.hidden = true;
            container.innerHTML = '';
            return;
        }

        card.hidden = false;
        const sections = [];

        // ManualRetry banner
        if (manualRetry?.required) {
            sections.push(`
                <div class="ai-validation-retry-banner">
                    <div class="ai-validation-retry-title">需要手动确认</div>
                    <div class="ai-validation-retry-summary">${this._escapeHtml(manualRetry.summary || manualRetry.repairTarget || '')}</div>
                    <div class="ai-validation-retry-stage">失败阶段：${this._escapeHtml(manualRetry.stage || '未知')}</div>
                </div>
            `);
        }

        // Diagnostics list
        if (diagnostics.length > 0) {
            const issueItems = diagnostics.flatMap(d => {
                const issues = d.issues || d.Issues || [];
                return issues.map(issue => ({
                    severity: issue.severity || issue.Severity || 'error',
                    category: issue.category || issue.Category || '',
                    code: issue.code || issue.Code || '',
                    message: issue.message || issue.Message || '',
                    repairHint: issue.repairHint || issue.RepairHint || '',
                    operatorId: issue.operatorId || issue.OperatorId || ''
                }));
            });

            if (issueItems.length > 0) {
                sections.push(`
                    <div class="ai-validation-issues">
                        <div class="ai-validation-issues-header">校验问题 (${issueItems.length})</div>
                        ${issueItems.map(item => {
                            const isWarning = item.severity === 'warning';
                            const icon = isWarning ? '&#9888;' : '&#10007;';
                            const cls = isWarning ? 'is-warning' : 'is-error';
                            return `
                                <div class="ai-validation-issue ${cls}">
                                    <span class="ai-validation-issue-icon">${icon}</span>
                                    <div class="ai-validation-issue-body">
                                        <div class="ai-validation-issue-msg">${this._escapeHtml(item.message)}</div>
                                        ${item.category ? `<div class="ai-validation-issue-meta">${this._escapeHtml(item.category)}${item.operatorId ? ` · ${this._escapeHtml(item.operatorId)}` : ''}</div>` : ''}
                                        ${item.repairHint ? `<div class="ai-validation-issue-hint">${this._escapeHtml(item.repairHint)}</div>` : ''}
                                    </div>
                                </div>
                            `;
                        }).join('')}
                    </div>
                `);
            }
        }

        // DryRun result
        if (dryRun) {
            const isSuccess = dryRun.isSuccess ?? dryRun.IsSuccess ?? false;
            const coverage = dryRun.coveragePercentage ?? dryRun.CoveragePercentage ?? 0;
            const covered = dryRun.coveredBranches ?? dryRun.CoveredBranches ?? 0;
            const total = dryRun.totalBranches ?? dryRun.TotalBranches ?? 0;
            const duration = dryRun.durationMs ?? dryRun.DurationMs ?? null;
            const icon = isSuccess ? '&#10003;' : '&#10007;';
            const cls = isSuccess ? 'is-ok' : 'is-failed';
            sections.push(`
                <div class="ai-validation-dryrun ${cls}">
                    <div class="ai-validation-dryrun-header">
                        <span class="ai-validation-issue-icon">${icon}</span>
                        <span>DryRun ${isSuccess ? '通过' : '失败'}</span>
                        ${duration != null ? `<span class="ai-validation-dryrun-duration">${Math.round(duration)}ms</span>` : ''}
                    </div>
                    <div class="ai-validation-dryrun-coverage">
                        <div class="ai-coverage-bar">
                            <div class="ai-coverage-bar-fill" style="width:${Math.min(100, coverage)}%"></div>
                        </div>
                        <div class="ai-coverage-text">分支覆盖 ${covered}/${total} (${coverage.toFixed(1)}%)</div>
                    </div>
                </div>
            `);
        }

        // Knowledge graph diagnostics
        if (knowledgeDiags.length > 0) {
            sections.push(`
                <div class="ai-validation-issues">
                    <div class="ai-validation-issues-header">知识图谱诊断 (${knowledgeDiags.length})</div>
                    ${knowledgeDiags.map(d => {
                        const severity = d.severity || d.Severity || 'warning';
                        const isWarning = severity === 'warning';
                        const icon = isWarning ? '&#9888;' : '&#10007;';
                        const cls = isWarning ? 'is-warning' : 'is-error';
                        const message = d.message || d.Message || '';
                        const code = d.code || d.Code || '';
                        const operatorId = d.operatorId || d.OperatorId || '';
                        const repairHint = d.repairHint || d.RepairHint || '';
                        return `
                            <div class="ai-validation-issue ${cls}">
                                <span class="ai-validation-issue-icon">${icon}</span>
                                <div class="ai-validation-issue-body">
                                    <div class="ai-validation-issue-msg">${this._escapeHtml(message)}</div>
                                    ${code ? `<div class="ai-validation-issue-meta">${this._escapeHtml(code)}${operatorId ? ` · ${this._escapeHtml(operatorId)}` : ''}</div>` : ''}
                                    ${repairHint ? `<div class="ai-validation-issue-hint">${this._escapeHtml(repairHint)}</div>` : ''}
                                </div>
                            </div>
                        `;
                    }).join('')}
                </div>
            `);
        }

        container.innerHTML = sections.join('');
    }

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
        const pending = this._normalizePendingParameters(result?.pendingParameters ?? result?.PendingParameters);
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
            .map(op => op.operatorType || op.OperatorType || op.displayName || op.DisplayName || '?')
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
}
