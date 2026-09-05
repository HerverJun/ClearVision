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
            restoredTaskTitle: ''
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
        ? '[data-ai-hook="task-more-menu"]'
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

    panel.container.querySelectorAll('[data-ai-shell-pane]').forEach(button => {
        button.addEventListener('click', () => {
            const pane = button.dataset.aiShellPane === 'conversation' ? 'conversation' : 'workbench';
            root.dataset.aiActivePane = pane;
            panel.container.querySelectorAll('[data-ai-shell-pane]').forEach(candidate => {
                const selected = candidate.dataset.aiShellPane === pane;
                candidate.setAttribute('aria-selected', selected ? 'true' : 'false');
                candidate.tabIndex = selected ? 0 : -1;
            });
            if (pane === 'conversation') {
                panel.container.querySelector('#ai-input')?.focus?.({ preventScroll: true });
            }
        });
    });

    const moreButton = panel.container.querySelector('[data-ai-hook="task-more"]');
    const moreMenu = panel.container.querySelector('[data-ai-hook="task-more-menu"]');
    moreButton?.addEventListener('click', () => {
        const expanded = moreButton.getAttribute('aria-expanded') === 'true';
        moreButton.setAttribute('aria-expanded', expanded ? 'false' : 'true');
        if (moreMenu) moreMenu.hidden = expanded;
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
    if (!isMatchingSessionRestore(args[0], pending)) return;
    const runtime = ensureRuntime(panel);
    if (!runtime) return;
    const restored = readRestoredSessionContent(args[0]);
    runtime.restoredContent = restored.restoredContent;
    runtime.restoredTaskTitle = restored.restoredTaskTitle;
    if (restored.restoredContent && !restored.hasUsableResult) {
        panel._clearResultPane?.();
    }
}

function updateEventPresentation(panel, args) {
    const type = clean(args[0]?.type).toLowerCase();
    if (type !== 'workspace/reset') return;
    const runtime = ensureRuntime(panel);
    if (!runtime) return;
    runtime.restoredContent = false;
    runtime.restoredTaskTitle = '';
}

export function installAiPanelShellPresentation(prototype) {
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
