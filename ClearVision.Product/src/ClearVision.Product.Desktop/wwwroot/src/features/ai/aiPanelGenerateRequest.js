import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import { AiWorkbenchStates } from './aiPanelWorkbench.js';
export const aiPanelGenerateRequestMixin = {
    _dispatchGenerateRequest({
        description,
        hint = '',
        userMessage = '',
        attachmentPaths = [],
        existingFlowJson = null,
        explicitMode = '',
        templateSelection = null,
        clearInput = true,
        skipPlan = false,
        skipPlanSource = '',
        buildFromPlan = null,
        suppressUserMessage = false
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

        const hasExistingFlowOverride = existingFlowJson !== null && existingFlowJson !== undefined;
        const hasCurrentFlowContext = hasExistingFlowOverride
            ? this._hasMeaningfulFlowPayload(existingFlowJson)
            : this._hasCurrentFlowContext();

        if (this._shouldRouteIntentBeforeGenerate?.({
            explicitMode,
            skipPlan,
            skipPlanSource,
            buildFromPlan,
            description: normalizedDescription,
            hasCurrentFlowContext
        })) {
            return this._enterIntentRouterFromPrompt({
                description: normalizedDescription,
                hint: normalizedHint,
                userMessage,
                attachmentPaths,
                templateSelection,
                clearInput,
                input,
                explicitMode,
                hasCurrentFlowContext
            });
        }

        if (this._shouldOpenPlanModeBeforeBuild?.({
            explicitMode,
            skipPlan,
            skipPlanSource,
            buildFromPlan,
            description: normalizedDescription,
            hasCurrentFlowContext
        })) {
            return this._enterPlanModeFromPrompt({
                description: normalizedDescription,
                hint: normalizedHint,
                userMessage,
                attachmentPaths,
                templateSelection,
                clearInput,
                input
            });
        }

        this.lastUserPrompt = String(userMessage || normalizedDescription).trim();
        this._setGeneratingState(true);
        this._setWorkbenchState(AiWorkbenchStates.GENERATING);
        this.agentWorkspaceMode = 'build';
        this._renderAgentWorkspaceOverview?.();
        this._renderPlanWorkspace?.(this.pendingVisionPlan);
        this._renderBuildWorkspaceFromAgentRun?.();
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

        if (!suppressUserMessage) {
            this._addMessage('user', userMessage || normalizedDescription);
        }
        this._startAssistantTurn();

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

        if (this._shouldUseAgentRunEventStream?.()) {
            const agentRunPayload = this._buildAgentRunCreatePayload({
                normalizedDescription,
                normalizedHint,
                requestId,
                resolvedMode,
                flowPayload,
                attachmentPaths: [],
                normalizedTemplateSelection,
                agentGenerateFlowPayload,
                buildFromPlan
            });

            this._dispatchAgentRunGenerateRequest(agentRunPayload, { clearInput, input })
                .catch(err => {
                    this._handleError(err?.message || String(err || 'AgentRun 创建失败'));
                });
            return true;
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
                    buildFromPlan,
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
    },

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
    },

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
    },

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
    },

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
    },

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
    },

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
    },

    _looksLikeModifyRequest(description = '') {
        const text = String(description || '').trim().toLowerCase();
        if (!text) return false;

        return [
            '改', '修改', '调整', '优化', '调优', '增加', '新增', '新建', '补充', '删除', '删掉', '移除',
            '追加', '再加', '继续加', '加一个算子', '加个算子', '基于当前', '在当前', '沿用当前',
            '替换', '改成', '变成', '中文', '中文化', '阈值', '参数', '算子名称', 'displayname',
            'change', 'update', 'adjust', 'add', 'remove', 'replace', 'refine'
        ].some(signal => text.includes(signal));
    },

    _looksLikeExplainRequest(description = '') {
        const text = String(description || '').trim().toLowerCase();
        if (!text) return false;

        return [
            '解释', '说明', '讲解', '为什么', '什么意思', '含义', '原理', '思路',
            'explain', 'why', 'reason', 'meaning'
        ].some(signal => text.includes(signal));
    },

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
    },

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
    },

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
    },

    _parseBooleanPreference(value) {
        const normalized = String(value ?? '').trim().toLowerCase();
        return normalized === '1' || normalized === 'true' || normalized === 'yes' || normalized === 'on';
    },

    _normalizeAgentGenerateFlowMode(mode) {
        const normalized = String(mode || '').trim().toLowerCase();
        if (normalized === 'planner') return 'planner';
        if (normalized === 'tool_loop') return 'tool_loop';
        return 'scripted';
    },

    _loadAgentGenerateFlowEnabled() {
        if (this.options?.useVisionAgentGenerateFlow === true) {
            return true;
        }
        if (this.options?.useVisionAgentGenerateFlow === false) {
            return false;
        }

        try {
            const stored = localStorage.getItem('cv_ai_use_vision_agent_generate_flow');
            return stored === null ? true : this._parseBooleanPreference(stored);
        } catch {
            return true;
        }
    },

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
    },

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
    },

    _renderAgentDeveloperControls() {
        if (!this.isVisionAgentDeveloperUiEnabled) {
            return '';
        }

        const enabled = Boolean(this.useVisionAgentGenerateFlow);
        const mode = this._normalizeAgentGenerateFlowMode(this.agentGenerateFlowMode);
        const directBuildDebugActive = Boolean(this.directBuildDebugNextRequest);
        return `
            <div class="ai-agent-dev-controls" id="ai-agent-dev-controls">
                <label class="ai-agent-dev-toggle">
                    <input id="ai-agent-generate-toggle" type="checkbox" ${enabled ? 'checked' : ''} />
                    <span>Agent GenerateFlow</span>
                </label>
                <div class="ai-agent-dev-mode-toggle" id="ai-agent-generate-mode-toggle" role="group" aria-label="Agent GenerateFlow 模式">
                    <button class="ai-mode-chip ${mode === 'scripted' ? 'is-active' : ''}" type="button" data-agent-generate-mode="scripted" ${enabled ? '' : 'disabled'}>固定构建链路：稳定</button>
                    <button class="ai-mode-chip ${mode === 'planner' ? 'is-active' : ''}" type="button" data-agent-generate-mode="planner" ${enabled ? '' : 'disabled'}>planner</button>
                    <button class="ai-mode-chip ${mode === 'tool_loop' ? 'is-active' : ''}" type="button" data-agent-generate-mode="tool_loop" ${enabled ? '' : 'disabled'}>Tool Loop 实验</button>
                </div>
                <div class="ai-agent-dev-note" ${mode === 'tool_loop' ? '' : 'hidden'}>实验模式：LLM 会在权限门禁内自主选择工具；失败会回退稳定构建链路。</div>
                <label class="ai-agent-dev-toggle ai-agent-preview-consent">
                    <input id="ai-agent-runtime-preview-consent" type="checkbox" ${enabled && this.runtimePreviewConsent ? 'checked' : ''} ${enabled ? '' : 'disabled'} />
                    <span>允许本轮 RuntimePreview</span>
                </label>
                <button
                    class="ai-mode-chip ai-direct-build-debug ${directBuildDebugActive ? 'is-active' : ''}"
                    id="ai-agent-direct-build-debug"
                    type="button"
                    title="跳过 Plan，仅用于调试">
                    ${directBuildDebugActive ? '直接 Build 调试（下一次）' : '直接 Build 调试'}
                </button>
                <div class="ai-agent-dev-note ai-agent-direct-build-note">跳过 Plan，仅用于调试；下一次发送后自动关闭。</div>
            </div>
        `;
    },

    _bindAgentDeveloperControls() {
        if (!this.isVisionAgentDeveloperUiEnabled) {
            return;
        }

        const toggle = this.container?.querySelector('#ai-agent-generate-toggle');
        const previewConsentToggle = this.container?.querySelector('#ai-agent-runtime-preview-consent');
        const modeButtons = Array.from(this.container?.querySelectorAll('[data-agent-generate-mode]') || []);
        const directBuildDebugButton = this.container?.querySelector('#ai-agent-direct-build-debug');
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
            const note = this.container?.querySelector('.ai-agent-dev-note');
            if (note) {
                note.hidden = mode !== 'tool_loop';
            }
            if (directBuildDebugButton) {
                directBuildDebugButton.disabled = false;
                directBuildDebugButton.classList.toggle('is-active', Boolean(this.directBuildDebugNextRequest));
                directBuildDebugButton.textContent = this.directBuildDebugNextRequest
                    ? '直接 Build 调试（下一次）'
                    : '直接 Build 调试';
                directBuildDebugButton.title = '跳过 Plan，仅用于调试';
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

        if (directBuildDebugButton) {
            directBuildDebugButton.addEventListener('click', () => {
                this.directBuildDebugNextRequest = !this.directBuildDebugNextRequest;
                this._setResultStatusNote?.(
                    this.directBuildDebugNextRequest
                        ? '下一次发送将跳过 Plan，仅用于调试。'
                        : '',
                    this.directBuildDebugNextRequest ? 'warning' : ''
                );
                refresh();
            });
        }

        refresh();
    },

    _buildAgentGenerateFlowRequestPayload() {
        if (!this.useVisionAgentGenerateFlow) {
            this.runtimePreviewConsent = false;
            return {};
        }

        const payload = {
            useVisionAgentGenerateFlow: true,
            agentGenerateFlowMode: this._normalizeAgentGenerateFlowMode(this.agentGenerateFlowMode)
        };
        if (this.isVisionAgentDeveloperUiEnabled && this.runtimePreviewConsent) {
            payload.runtimePreviewConsent = true;
            this.runtimePreviewConsent = false;
        }

        return payload;
    },

    _consumeDirectBuildDebugRequest() {
        if (!this.isVisionAgentDeveloperUiEnabled || !this.directBuildDebugNextRequest) {
            return false;
        }

        this.directBuildDebugNextRequest = false;
        return true;
    },

    async _handleGenerate() {
        const input = this.container.querySelector('#ai-input');
        const description = input.value.trim();
        const attachmentPaths = this.attachments.map(item => item.path);
        const hint = this.nextHintDraft.trim();
        const templateSelection = this.nextTemplateSelection ? { ...this.nextTemplateSelection } : null;
        const userMessage = attachmentPaths.length > 0
            ? `${description}\n\n[附件] ${this.attachments.map(item => item.name).join('，')}`
            : description;
        const directBuildDebug = this._consumeDirectBuildDebugRequest?.() === true;
        return this._dispatchGenerateRequest({
            description,
            hint,
            userMessage,
            attachmentPaths,
            templateSelection,
            explicitMode: directBuildDebug ? 'new' : '',
            clearInput: true,
            skipPlan: directBuildDebug,
            skipPlanSource: directBuildDebug ? 'developer_direct_build_debug' : ''
        });
    },

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
            this._addMessage('system', '请先确认人工参数，再提交 AI 复核（可选）。');
            return;
        }

        const reviewRequest = this._buildPendingParameterReviewRequest();
        if (!reviewRequest) {
            this._addMessage('system', '当前没有可提交的 AI 复核内容。');
            return;
        }

        this._dispatchGenerateRequest({
            description: '请对当前方案做可选 AI 复核，重点检查已确认人工参数、画布人工修改记录、仍缺资源和仍缺参数；不要自动覆盖画布。',
            hint: reviewRequest.hint,
            userMessage: reviewRequest.userMessage,
            existingFlowJson: reviewRequest.existingFlowJson,
            attachmentPaths: [],
            explicitMode: 'review_pending_parameters',
            clearInput: true,
            skipPlan: true,
            skipPlanSource: 'pending_parameter_review'
        });
    },

    _handleCancelGenerate() {
        if (!this.isGenerating || this.isCancellingGenerate) return;

        const requestId = this.activeGenerateRequestId;
        const sessionId = this.activeGenerateSessionId || this.sessionId;
        if (!requestId && !this.activeAgentRunId && !this.activePlanRunId) return;

        this.isCancellingGenerate = true;

        if (this.activePlanRunId && this._cancelActivePlanRun) {
            this._cancelActivePlanRun();
            this._updateProgress({
                message: '正在取消规划...',
                phase: 'cancelling'
            });
            this._addMessage('system', '已发送 Plan Run 取消请求，正在等待事件流确认。');
            this._setGeneratingState(this.isGenerating);
            return;
        }

        if (this.activeAgentRunId && this._cancelActiveAgentRun) {
            this._cancelActiveAgentRun();
            this._updateProgress({
                message: '正在取消生成...',
                phase: 'cancelling'
            });
            this._addMessage('system', '已发送 AgentRun 取消请求，正在等待事件流确认。');
            this._setGeneratingState(this.isGenerating);
            return;
        }

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
    },

    _createGenerateRequestId() {
        const randomPart = Math.random().toString(36).slice(2, 8);
        return `gen-${Date.now()}-${randomPart}`;
    },

    _getGenerateRequestId(payload) {
        return String(payload?.requestId ?? payload?.RequestId ?? '').trim();
    },

    _shouldHandleGenerateRealtimePayload(payload) {
        const requestId = this._getGenerateRequestId(payload);
        if (!requestId) {
            return this.isGenerating;
        }

        return Boolean(this.activeGenerateRequestId) && requestId === this.activeGenerateRequestId;
    },

    _shouldHandleGenerateTerminalPayload(payload) {
        const requestId = this._getGenerateRequestId(payload);
        if (!requestId) {
            return this.isGenerating;
        }

        return Boolean(this.activeGenerateRequestId) && requestId === this.activeGenerateRequestId;
    },

    _normalizeGenerateStatus(payload) {
        return String(payload?.status ?? payload?.Status ?? '').trim().toLowerCase();
    },

    _isCancelledResult(payload) {
        const status = this._normalizeGenerateStatus(payload);
        const failureType = String(payload?.failureType ?? payload?.FailureType ?? '').trim().toLowerCase();

        return ['cancelled', 'canceled', 'user_cancelled', 'user_canceled'].includes(status)
            || ['user_cancelled', 'user_canceled'].includes(failureType);
    },

    _isClarificationResult(payload) {
        const status = this._normalizeGenerateStatus(payload);
        const failureType = String(payload?.failureType ?? payload?.FailureType ?? '').trim().toLowerCase();
        const clarificationRequired = Boolean(payload?.clarificationRequired ?? payload?.ClarificationRequired);

        return clarificationRequired
            || status === 'clarification_required'
            || failureType === 'clarification_required';
    },

    _getTurnIntent(payload) {
        return String(payload?.turnIntent ?? payload?.TurnIntent ?? '').trim().toLowerCase();
    },

    _getInteractionState(payload) {
        return String(payload?.interactionState ?? payload?.InteractionState ?? '').trim().toLowerCase();
    },

    _getRouterConfidence(payload) {
        return String(payload?.routerConfidence ?? payload?.RouterConfidence ?? '').trim().toLowerCase();
    },

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
    },

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
};
