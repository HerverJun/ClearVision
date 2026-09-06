import { deriveAiBuildPresentation } from './aiPanelBuildPresentation.js';

const AI_SHELL_PHASE_TEXT = Object.freeze({
    idle: '',
    routing: '正在判断请求类型',
    clarifying: '等待补充信息',
    plan_blocked: '方案待补充',
    ready_to_build: '方案可构建',
    building: '正在构建',
    build_failed: '构建失败',
    applied: '已应用'
});

const shellRuntimeByPanel = new WeakMap();

function readProjection(panel) {
    return panel?.agentWorkspaceState?.projection || null;
}

function readState(panel) {
    return panel?.agentWorkspaceState || null;
}

function clean(value) {
    return String(value ?? '').trim();
}

function readRuntime(panel) {
    if (!panel || (typeof panel !== 'object' && typeof panel !== 'function')) return null;
    return shellRuntimeByPanel.get(panel) || null;
}

function ensureRuntime(panel) {
    if (!panel || (typeof panel !== 'object' && typeof panel !== 'function')) return null;
    let runtime = readRuntime(panel);
    if (!runtime) {
        runtime = {
            syncScheduled: false,
            restoredContent: false,
            restoredTaskTitle: '',
            desktopCollapsed: false,
            drawerOpen: false,
            unread: false
        };
        shellRuntimeByPanel.set(panel, runtime);
    }
    return runtime;
}

function hasNonIdleStatus(value) {
    const status = clean(value).toLowerCase();
    return Boolean(status && status !== 'idle');
}

function hasCanonicalActivity(state, projection = state?.projection) {
    if (clean(projection?.phase).toLowerCase() !== 'idle' && clean(projection?.phase)) return true;
    if (state?.intent || state?.plan || state?.result) return true;

    const planRun = state?.run?.plan;
    const buildRun = state?.run?.build;
    if (clean(planRun?.runId) || hasNonIdleStatus(planRun?.status)) return true;
    if (clean(buildRun?.runId) || hasNonIdleStatus(buildRun?.status)) return true;
    return hasNonIdleStatus(state?.apply?.status);
}

function hasPendingSubmittedRequest(panel) {
    if (clean(panel?.activeIntentRouterRequestId) || clean(panel?.activeGenerateRequestId)) return true;
    const lifecycleStatus = clean(panel?.planningLifecycle?.status).toLowerCase();
    if (['running', 'failed', 'timeout', 'cancelled'].includes(lifecycleStatus) &&
        Boolean(clean(panel?.lastPlanningRequestContext?.description || panel?.lastUserPrompt))) {
        return true;
    }
    return panel?.isGenerating === true && Boolean(clean(panel?.lastUserPrompt));
}

function readTaskTitle(panel, state, runtime = readRuntime(panel)) {
    const plan = state?.plan;
    const values = [
        plan?.goal,
        plan?.originalUserPrompt,
        plan?.rawPlanSnapshot?.goal,
        plan?.rawPlanSnapshot?.originalUserPrompt,
        state?.intent?.description,
        state?.intent?.userMessage,
        panel?.lastUserPrompt,
        runtime?.restoredTaskTitle
    ];
    const value = values.find(item => clean(item));
    return value ? clean(value) : '';
}

function readStageLabel(projection, panel) {
    const lifecycle = panel?.planningLifecycle;
    const lifecycleStatus = clean(lifecycle?.status).toLowerCase();
    if (lifecycleStatus === 'cancelled') return '规划已取消';
    if (lifecycleStatus === 'timeout') return '规划超时';
    if (lifecycleStatus === 'failed') return '规划失败';
    if (lifecycleStatus === 'running') {
        return {
            understand: '正在理解需求',
            context: '正在整理工程上下文',
            generate: '正在生成方案',
            validate: '正在校验方案'
        }[clean(lifecycle?.phase).toLowerCase()] || '正在规划';
    }
    return AI_SHELL_PHASE_TEXT[clean(projection?.phase).toLowerCase()] || '';
}

function readTaskProgress(panel, state) {
    const phase = clean(panel?._getAgentWorkspacePhase?.()).toLowerCase();
    if (phase === 'build' || phase === 'applied') {
        const build = deriveAiBuildPresentation(panel);
        return {
            blockerCount: build.blockerCount,
            countText: build.blockerCount > 0 ? `待处理 ${build.blockerCount} 项` : '',
            detail: build.overall.next,
            phaseText: build.overall.label
        };
    }

    const plan = panel?.pendingVisionPlan || state?.plan;
    if (!plan) return { blockerCount: null, countText: '', detail: '' };
    const previewStatus = state?.readinessStatus || 'idle';
    const preview = panel?._getCurrentCanonicalPreview?.(plan);
    if (panel?.workspaceRecoveryBlocked || !preview || !['ready', 'blocked'].includes(previewStatus)) {
        return {
            blockerCount: null,
            countText: '',
            detail: panel?._getPlanBuildActionState?.(plan)?.statusText || '等待校验构建条件'
        };
    }
    const summary = panel?._buildPlanMissingSummary?.(plan);
    return {
        blockerCount: summary?.totalCount ?? null,
        countText: summary?.totalCount > 0 ? `待补齐 ${summary.totalCount} 项` : '',
        detail: summary?.summaryText || ''
    };
}

function readNextStep(state, projection) {
    const values = [
        projection?.readiness?.primaryMessage,
        state?.plan?.nextAction,
        state?.plan?.rawPlanSnapshot?.nextAction
    ];
    const value = values.find(item => clean(item));
    return value ? clean(value) : '';
}

export function deriveAiShellPresentation(panel) {
    const state = readState(panel);
    const projection = readProjection(panel);
    const runtime = readRuntime(panel);
    const canonicalActive = hasCanonicalActivity(state, projection);
    const requestPending = hasPendingSubmittedRequest(panel);
    const restoreActive = runtime?.restoredContent === true;
    const progress = readTaskProgress(panel, state);

    return {
        shellState: canonicalActive || requestPending || restoreActive ? 'active' : 'idle',
        phaseText: progress.phaseText || readStageLabel(projection, panel),
        taskTitle: readTaskTitle(panel, state, runtime),
        blockerCount: progress.blockerCount,
        countText: progress.countText,
        nextStep: progress.detail || readNextStep(state, projection),
        canonicalActive,
        requestPending,
        restoreActive
    };
}

function setText(element, value, { hideWhenEmpty = false } = {}) {
    if (!element) return;
    const text = clean(value);
    element.textContent = text;
    if (hideWhenEmpty) element.hidden = !text;
}

function moveConversationActions(panel, active) {
    const actions = panel?.container?.querySelector('.ai-pane-actions');
    const target = panel?.container?.querySelector(active
        ? '[data-ai-hook="task-utilities"]'
        : '[data-ai-hook="idle-actions"]');
    if (actions && target && actions.parentElement !== target) {
        target.appendChild(actions);
    }
}

function movePrimaryAction(panel, active) {
    const slot = panel?.container?.querySelector('[data-ai-hook="task-primary-action"]');
    if (!slot) return;

    const findOrigin = button => button?.id === 'ai-btn-apply'
        ? panel.container.querySelector('.apply-container')
        : panel.container.querySelector('.ai-plan-actions');
    const restoreButton = button => {
        if (!button) return;
        const target = findOrigin(button);
        if (target && button.parentElement !== target) target.prepend(button);
    };

    const mountedButton = slot.querySelector('button');
    if (!active) {
        restoreButton(mountedButton);
        return;
    }

    const workspacePhase = clean(panel?._getAgentWorkspacePhase?.()).toLowerCase();
    const desiredId = workspacePhase === 'build' || workspacePhase === 'applied'
        ? 'ai-btn-apply'
        : 'ai-btn-start-build';
    const candidates = Array.from(panel.container.querySelectorAll?.(`#${desiredId}`) || []);
    const rebuiltButton = candidates.find(button => button !== mountedButton && !slot.contains(button));
    const button = rebuiltButton || (mountedButton?.id === desiredId ? mountedButton : null) || candidates[0] || null;

    if (mountedButton && mountedButton !== button) {
        if (mountedButton.id === desiredId) mountedButton.remove();
        else restoreButton(mountedButton);
    }
    candidates.forEach(candidate => {
        if (candidate !== button) candidate.remove();
    });
    if (button && button.parentElement !== slot) slot.appendChild(button);
}

export function deriveAiTaskAction(panel) {
    if (panel?.planningLifecycle?.status === 'running') return { kind: 'cancel', label: '取消规划' };
    const phase = clean(panel?._getAgentWorkspacePhase?.()).toLowerCase();
    if (phase === 'build' || phase === 'applied') {
        const build = deriveAiBuildPresentation(panel);
        if (build.applied) return { kind: 'flow', label: '查看流程' };
        if (['failed', 'validation_failed', 'gate_blocked'].includes(build.overall.key)) {
            return { kind: 'navigate', label: '查看问题', target: build.overall.target };
        }
        if (build.overall.key === 'needs_input') return { kind: 'navigate', label: '处理待办', target: build.overall.target };
        return null;
    }
    const progress = readTaskProgress(panel, readState(panel));
    const buildAction = panel?._getPlanBuildActionState?.(panel?.pendingVisionPlan || readState(panel)?.plan);
    if (progress.blockerCount > 0 && buildAction?.canStart !== true) return { kind: 'navigate', label: '处理待办', target: 'plan-todos' };
    return null;
}

function syncTaskAction(panel) {
    const action = deriveAiTaskAction(panel);
    const button = panel.container.querySelector('[data-ai-hook="task-navigation-action"]');
    const slot = panel.container.querySelector('[data-ai-hook="task-primary-action"]');
    if (!button || !slot) return;
    button.hidden = !action;
    slot.hidden = Boolean(action);
    if (action) button.textContent = action.label;
}

function navigateTaskAction(panel) {
    const action = deriveAiTaskAction(panel);
    if (!action) return;
    if (action.kind === 'cancel') return panel._handleCancelGenerate?.();
    if (action.kind === 'flow') return document.querySelector('.nav-btn[data-view="flow"]')?.click();
    if (ensureRuntime(panel).media?.matches) panel._setAiConversationOpen?.(false, { transient: true });
    const target = action.target === 'plan-todos'
        ? panel.container.querySelector('[data-ai-hook="clarification-workspace"], [data-ai-hook="clarification-contract-gap"], #ai-plan-workspace')
        : panel.container.querySelector(`#${action.target}`);
    if (!target) return;
    target.scrollIntoView?.({ block: 'start', behavior: 'auto' });
    const focusTarget = target.querySelector('input:not(:disabled), select:not(:disabled), textarea:not(:disabled), button:not(:disabled)') || target;
    if (!focusTarget.hasAttribute('tabindex')) focusTarget.tabIndex = -1;
    focusTarget.focus?.({ preventScroll: true });
}

function syncConversation(panel) {
    const runtime = ensureRuntime(panel);
    const root = panel.container.querySelector('[data-ai-hook="shell"]');
    const pane = panel.container.querySelector('[data-ai-hook="conversation-pane"]');
    if (!root || !pane) return;
    const compact = runtime.media?.matches === true;
    const active = root.dataset.aiShellState === 'active';
    const open = !active || (compact ? runtime.drawerOpen : !runtime.desktopCollapsed);
    root.dataset.aiConversation = open ? 'open' : 'closed';
    root.dataset.aiActivePane = open && compact ? 'conversation' : 'workbench';
    pane.inert = !open;
    pane.setAttribute('aria-hidden', open ? 'false' : 'true');
    const modal = active && compact && open;
    pane.setAttribute('role', modal ? 'dialog' : 'complementary');
    if (modal) pane.setAttribute('aria-modal', 'true');
    else pane.removeAttribute('aria-modal');
    const workbench = panel.container.querySelector('[data-ai-hook="workbench-pane"]');
    const context = panel.container.querySelector('[data-ai-hook="task-context"]');
    if (workbench) workbench.inert = modal;
    if (context) context.inert = modal;
    const backdrop = panel.container.querySelector('[data-ai-hook="conversation-backdrop"]');
    if (backdrop) backdrop.hidden = !modal;
    const toggle = panel.container.querySelector('[data-ai-hook="conversation-toggle"]');
    const label = open ? '收起对话' : runtime.unread ? '打开对话，有新消息' : '打开对话';
    toggle?.setAttribute('aria-expanded', open ? 'true' : 'false');
    toggle?.setAttribute('aria-label', label);
    toggle?.setAttribute('title', label);
    if (open) runtime.unread = false;
    const unread = panel.container.querySelector('[data-ai-hook="conversation-unread"]');
    if (unread) unread.hidden = !runtime.unread;
}

function initializeConversation(panel, root) {
    const runtime = ensureRuntime(panel);
    try { runtime.desktopCollapsed = localStorage.getItem('cv_ai_conversation_collapsed') === 'true'; } catch { /* Optional preference. */ }
    runtime.media = globalThis.matchMedia?.('(max-width: 1179px)');
    panel._setAiConversationOpen = (open, { focus = false, transient = false } = {}) => {
        if (runtime.media?.matches) runtime.drawerOpen = open;
        else {
            runtime.desktopCollapsed = !open;
            if (!transient) {
                try { localStorage.setItem('cv_ai_conversation_collapsed', String(!open)); } catch { /* Optional preference. */ }
            }
        }
        syncConversation(panel);
        if (focus) panel.container.querySelector(open ? '#ai-input' : '[data-ai-hook="conversation-toggle"]')?.focus?.({ preventScroll: true });
    };
    const onResize = () => {
        const focusInPane = panel.container.querySelector('[data-ai-hook="conversation-pane"]')?.contains(document.activeElement);
        runtime.drawerOpen = false;
        syncConversation(panel);
        if (focusInPane && root.dataset.aiConversation === 'closed') panel.container.querySelector('[data-ai-hook="conversation-toggle"]')?.focus();
    };
    runtime.media?.addEventListener?.('change', onResize);
    panel.container.querySelector('[data-ai-hook="conversation-toggle"]')?.addEventListener('click', () => {
        panel._setAiConversationOpen(root.dataset.aiConversation !== 'open', { focus: true });
    });
    ['conversation-close', 'conversation-backdrop'].forEach(hook => {
        panel.container.querySelector(`[data-ai-hook="${hook}"]`)?.addEventListener('click', () => panel._setAiConversationOpen(false, { focus: true }));
    });
    const onDrawerKeyDown = event => {
        if (!runtime.media?.matches || !runtime.drawerOpen || panel._activeApplyPreview ||
            root.dataset.aiShellState !== 'active' || panel.container.closest('.hidden')) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            panel._setAiConversationOpen(false, { focus: true });
        }
        if (event.key !== 'Tab') return;
        const pane = panel.container.querySelector('[data-ai-hook="conversation-pane"]');
        const controls = Array.from(pane.querySelectorAll('button:not(:disabled), input:not(:disabled), textarea:not(:disabled), select:not(:disabled), a[href], summary, [tabindex="0"]'))
            .filter(element => element.getClientRects().length && !element.closest('[hidden]'));
        const first = controls[0], last = controls.at(-1);
        if (!pane.contains(document.activeElement)) { event.preventDefault(); first?.focus(); }
        else if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    };
    document.addEventListener('keydown', onDrawerKeyDown, true);
    if (typeof MutationObserver !== 'undefined') {
        runtime.observer = new MutationObserver(records => {
            if (runtime.restoring || root.dataset.aiShellState !== 'active' || root.dataset.aiConversation !== 'closed') return;
            if (records.some(record => {
                const element = record.target.nodeType === 1 ? record.target : record.target.parentElement;
                return element?.closest?.('.ai-message.ai, .ai-message.system') || Array.from(record.addedNodes).some(node => node.matches?.('.ai-message.ai, .ai-message.system'));
            })) {
                runtime.unread = true;
                syncConversation(panel);
            }
        });
        runtime.observer.observe(panel.container.querySelector('#ai-chat-container'), { childList: true, subtree: true, characterData: true });
    }
    panel._disposeAiShell = () => {
        document.removeEventListener('keydown', onDrawerKeyDown, true);
        runtime.observer?.disconnect();
        runtime.media?.removeEventListener?.('change', onResize);
        shellRuntimeByPanel.delete(panel);
    };
}

function readRecentTasks(panel) {
    return (Array.isArray(panel?.history) ? panel.history : []).slice(0, 3).map(item => ({
        sessionId: clean(item?.sessionId),
        title: panel._sanitizeSessionHistoryText?.(item?.lastMessage, 88) || '未命名任务',
        time: panel._formatHistoryTime?.(item?.updatedAtUtc) || ''
    }));
}

function bindRecentTaskDelegation(panel, list) {
    if (!list || list.dataset.aiShellBound === 'true') return;
    list.dataset.aiShellBound = 'true';
    list.addEventListener('click', event => {
        const button = event.target?.closest?.('[data-ai-hook="idle-recent-item"]');
        if (!button || !list.contains(button)) return;
        panel._switchToSession?.(button.dataset.sessionId || '');
    });
}

function renderRecentTasks(panel) {
    const root = panel?.container?.querySelector('[data-ai-hook="idle-recent"]');
    const list = panel?.container?.querySelector('[data-ai-hook="idle-recent-list"]');
    if (!root || !list) return;

    bindRecentTaskDelegation(panel, list);
    const history = readRecentTasks(panel);
    root.hidden = history.length === 0;
    const signature = JSON.stringify(history);
    if (list.dataset.aiShellSignature === signature) return;
    list.dataset.aiShellSignature = signature;
    list.innerHTML = history.map(item => {
        const sessionId = panel._escapeHtml?.(item.sessionId) || '';
        const title = panel._escapeHtml?.(item.title) || '未命名任务';
        const time = panel._escapeHtml?.(item.time) || '';
        return `
            <button type="button" class="ai-idle-recent-item" data-ai-hook="idle-recent-item" data-session-id="${sessionId}">
                <span>${title}</span>
                <small>${time}</small>
            </button>
        `;
    }).join('');
}

export function syncAiPanelShell(panel) {
    const root = panel?.container?.querySelector('[data-ai-hook="shell"]');
    if (!root) return;

    const presentation = deriveAiShellPresentation(panel);
    const active = presentation.shellState === 'active';
    root.dataset.aiShellState = presentation.shellState;
    panel.container.dataset.aiShellState = presentation.shellState;

    const context = panel.container.querySelector('[data-ai-hook="task-context"]');
    if (context) context.hidden = !active;
    const workbenchPane = panel.container.querySelector('[data-ai-hook="workbench-pane"]');
    if (workbenchPane) workbenchPane.hidden = !active;
    const chatContainer = panel.container.querySelector('#ai-chat-container');
    if (chatContainer) chatContainer.hidden = !active;

    setText(panel.container.querySelector('[data-ai-hook="task-title"]'), presentation.taskTitle, { hideWhenEmpty: true });
    setText(panel.container.querySelector('[data-ai-hook="task-phase"]'), presentation.phaseText, { hideWhenEmpty: true });

    const blockers = panel.container.querySelector('[data-ai-hook="task-blockers"]');
    if (blockers) {
        blockers.hidden = !presentation.countText;
        blockers.textContent = presentation.countText;
    }

    setText(panel.container.querySelector('[data-ai-hook="task-next-step"]'), presentation.nextStep, { hideWhenEmpty: true });
    const announcement = [presentation.phaseText, presentation.countText, presentation.nextStep]
        .filter(Boolean)
        .join('。');
    if (announcement) panel._announceAccessibilityStatus?.(announcement);
    moveConversationActions(panel, active);
    movePrimaryAction(panel, active);
    syncTaskAction(panel);
    syncConversation(panel);
    renderRecentTasks(panel);
}

export function scheduleAiPanelShellSync(panel) {
    const runtime = ensureRuntime(panel);
    if (!runtime || runtime.syncScheduled) return;
    runtime.syncScheduled = true;
    const enqueue = typeof queueMicrotask === 'function'
        ? queueMicrotask
        : callback => Promise.resolve().then(callback);
    enqueue(() => {
        runtime.syncScheduled = false;
        syncAiPanelShell(panel);
    });
}

export function initializeAiPanelShell(panel) {
    const root = panel?.container?.querySelector('[data-ai-hook="shell"]');
    if (!root) return;
    renderRecentTasks(panel);
    if (root.dataset.aiShellBound === 'true') {
        scheduleAiPanelShellSync(panel);
        return;
    }
    root.dataset.aiShellBound = 'true';

    initializeConversation(panel, root);
    const recent = panel.container.querySelector('[data-ai-hook="idle-recent"]');
    if (recent) panel.container.querySelector('[data-ai-hook="conversation-pane"]')?.appendChild(recent);
    panel.container.querySelector('[data-ai-hook="task-navigation-action"]')?.addEventListener('click', () => navigateTaskAction(panel));
    panel.container.querySelector('[data-ai-hook="model-settings"]')?.addEventListener('click', () => {
        document.querySelector('.nav-btn[data-view="settings"]')?.click();
        panel._setOwnedTimeout?.(() => document.querySelector('.settings-menu-item[data-tab="ai"]')?.click(), 120);
    });

    const moreButton = panel.container.querySelector('[data-ai-hook="task-more"]');
    const moreMenu = panel.container.querySelector('[data-ai-hook="task-more-menu"]');
    moreButton?.addEventListener('click', () => {
        const expanded = moreButton.getAttribute('aria-expanded') === 'true';
        moreButton.setAttribute('aria-expanded', expanded ? 'false' : 'true');
        if (moreMenu) moreMenu.hidden = expanded;
    });
    root.addEventListener('keydown', event => {
        if (event.key === 'Escape' && moreButton?.getAttribute('aria-expanded') === 'true') {
            moreButton.setAttribute('aria-expanded', 'false');
            moreMenu.hidden = true;
            moreButton.focus();
        }
    });
    root.addEventListener('click', event => {
        if (event.target.closest('[data-ai-hook="task-more"]')) return;
        if (moreMenu) moreMenu.hidden = true;
        moreButton?.setAttribute('aria-expanded', 'false');
    });

    syncAiPanelShell(panel);
}

function wrapPresentationMethod(prototype, methodName, afterCall = null, beforeCall = null) {
    const original = prototype?.[methodName];
    if (typeof original !== 'function' || original.__aiShellWrapped) return;

    const wrapped = function (...args) {
        const callContext = beforeCall?.(this, args);
        const finish = () => {
            afterCall?.(this, args, callContext);
            scheduleAiPanelShellSync(this);
        };
        const result = original.apply(this, args);
        if (result && typeof result.finally === 'function') return result.finally(finish);
        finish();
        return result;
    };
    wrapped.__aiShellWrapped = true;
    prototype[methodName] = wrapped;
}

function readRestoredSessionContent(data) {
    const payload = data?.payload || data || {};
    const session = payload?.session || null;
    if (payload?.success !== true || !session) return { restoredContent: false, restoredTaskTitle: '' };

    const history = Array.isArray(session.history)
        ? session.history
        : (Array.isArray(session.History) ? session.History : []);
    const meaningfulTurns = history.filter(turn => clean(turn?.message ?? turn?.Message) || turn?.payload || turn?.Payload);
    const latestUserTurn = [...meaningfulTurns].reverse().find(turn => clean(turn?.role ?? turn?.Role).toLowerCase() === 'user');
    const hasHistoryResult = meaningfulTurns.some(turn => {
        const payload = turn?.payload ?? turn?.Payload;
        return Boolean(
            payload?.result || payload?.Result ||
            payload?.flow || payload?.Flow ||
            payload?.buildResult || payload?.BuildResult ||
            payload?.applyGate || payload?.ApplyGate
        );
    });
    const snapshot = session.workspaceSnapshot ?? session.WorkspaceSnapshot;
    const hasSerializedResult = [
        session.currentCanvasFlowJson,
        session.CurrentCanvasFlowJson,
        session.currentFlowJson,
        session.CurrentFlowJson
    ].some(value => clean(value));
    const hasSnapshotContent = Boolean(
        snapshot?.result || snapshot?.Result ||
        snapshot?.pendingPlanSnapshot || snapshot?.PendingPlanSnapshot ||
        snapshot?.planRunId || snapshot?.PlanRunId ||
        snapshot?.buildRunId || snapshot?.BuildRunId
    );
    const hasUsableResult = hasHistoryResult || hasSerializedResult || hasSnapshotContent;
    return {
        restoredContent: meaningfulTurns.length > 0 || hasUsableResult,
        hasUsableResult,
        restoredTaskTitle: clean(latestUserTurn?.message ?? latestUserTurn?.Message)
    };
}

function capturePendingSessionLoad(panel) {
    ensureRuntime(panel).restoring = true;
    const pending = panel?.pendingSessionLoad;
    return pending ? {
        sessionId: clean(pending.sessionId),
        requestId: clean(pending.requestId),
        epoch: Number(pending.epoch || 0)
    } : null;
}

function isMatchingSessionRestore(data, pending) {
    if (!pending) return false;
    const payload = data?.payload || data || {};
    return clean(payload.sessionId ?? payload.SessionId).toLowerCase() === pending.sessionId.toLowerCase() &&
        clean(payload.requestId ?? payload.RequestId) === pending.requestId &&
        Number(payload.navigationEpoch ?? payload.NavigationEpoch ?? -1) === pending.epoch;
}

function updateRestorePresentation(panel, args, pending) {
    const restoringRuntime = ensureRuntime(panel);
    restoringRuntime.observer?.takeRecords();
    restoringRuntime.restoring = false;
    if (!isMatchingSessionRestore(args[0], pending)) return;
    const runtime = ensureRuntime(panel);
    if (!runtime) return;
    const restored = readRestoredSessionContent(args[0]);
    runtime.restoredContent = restored.restoredContent;
    runtime.restoredTaskTitle = restored.restoredTaskTitle;
    runtime.unread = false;
    if (restored.restoredContent && !restored.hasUsableResult) {
        panel._clearResultPane?.();
        panel._setAiConversationOpen?.(true, { transient: true });
    } else if (restored.hasUsableResult && runtime.media?.matches) {
        panel._setAiConversationOpen?.(false, { transient: true });
    }
}

function updateEventPresentation(panel, args) {
    const type = clean(args[0]?.type).toLowerCase();
    if (type !== 'workspace/reset') return;
    const runtime = ensureRuntime(panel);
    if (!runtime) return;
    runtime.restoredContent = false;
    runtime.restoredTaskTitle = '';
    runtime.unread = false;
    runtime.drawerOpen = false;
}

export function installAiPanelShellPresentation(prototype) {
    wrapPresentationMethod(prototype, '_toggleHistoryPanel', panel => {
        if (panel.isHistoryPanelOpen) panel._setAiConversationOpen?.(true, { transient: true });
    });
    wrapPresentationMethod(prototype, '_dispatchAgentWorkspaceEvent', updateEventPresentation);
    wrapPresentationMethod(
        prototype,
        '_handleGetAiSessionResult',
        updateRestorePresentation,
        capturePendingSessionLoad
    );
    [
        '_renderAgentWorkspaceOverview',
        '_renderPlanWorkspace',
        '_renderBuildWorkspaceFromAgentRun',
        '_renderHistoryList',
        '_displayResult',
        '_clearResultPane',
        '_updatePlanBuildActionState',
        '_setWorkbenchState'
    ].forEach(methodName => wrapPresentationMethod(prototype, methodName));
}

export const aiPanelShellTestApi = {
    AI_SHELL_PHASE_TEXT,
    readTaskProgress,
    hasCanonicalActivity,
    hasPendingSubmittedRequest,
    isMatchingSessionRestore,
    readNextStep,
    readRestoredSessionContent,
    readStageLabel,
    readTaskTitle
};
