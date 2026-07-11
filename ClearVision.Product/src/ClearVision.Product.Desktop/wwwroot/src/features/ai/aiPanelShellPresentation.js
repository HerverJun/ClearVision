function readProjection(panel) {
    return panel?.agentWorkspaceState?.projection || null;
}

function readState(panel) {
    return panel?.agentWorkspaceState || null;
}

function isActiveProjection(projection) {
    return Boolean(projection && projection.phase && projection.phase !== 'idle');
}

function readTaskTitle(panel, state) {
    const plan = state?.plan;
    const values = [
        plan?.goal,
        plan?.originalUserPrompt,
        plan?.rawPlanSnapshot?.goal,
        plan?.rawPlanSnapshot?.originalUserPrompt,
        state?.intent?.description,
        state?.intent?.userMessage
    ];
    const value = values.find(item => String(item || '').trim());
    return value ? String(value).trim() : '';
}

function readStageLabel(panel, projection) {
    if (!projection?.phase) return '';
    return String(panel?._formatWorkspaceModeLabel?.() || '').trim();
}

function countReliableBlockers(projection) {
    const queue = Array.isArray(projection?.clarificationQueue)
        ? projection.clarificationQueue
        : [];
    return queue.filter(item => item?.blocksBuild === true && item?.answered !== true && item?.deferred !== true).length;
}

function readNextStep(state, projection) {
    const values = [
        projection?.readiness?.primaryMessage,
        state?.plan?.nextAction,
        state?.plan?.rawPlanSnapshot?.nextAction
    ];
    const value = values.find(item => String(item || '').trim());
    return value ? String(value).trim() : '';
}

function setText(element, value, { hideWhenEmpty = false } = {}) {
    if (!element) return;
    const text = String(value || '').trim();
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

function movePrimaryAction(panel, projection, active) {
    const slot = panel?.container?.querySelector('[data-ai-hook="task-primary-action"]');
    if (!slot) return;

    const restoreButton = button => {
        if (!button) return;
        const target = button.id === 'ai-btn-apply'
            ? panel.container.querySelector('.apply-container')
            : panel.container.querySelector('.ai-plan-actions');
        if (target) target.prepend(button);
    };

    const mountedButton = slot.querySelector('button');
    if (!active) {
        restoreButton(mountedButton);
        return;
    }

    const workspacePhase = String(panel?._getAgentWorkspacePhase?.() || '').trim();
    const selector = workspacePhase === 'build' || workspacePhase === 'applied'
        ? '#ai-btn-apply'
        : '#ai-btn-start-build';
    const button = selector === '#ai-btn-start-build'
        ? panel.container.querySelector(`#ai-plan-workspace ${selector}`) || mountedButton
        : panel.container.querySelector(selector);
    if (mountedButton && mountedButton !== button && mountedButton.id === button?.id) {
        mountedButton.remove();
    } else if (mountedButton && mountedButton !== button) {
        restoreButton(mountedButton);
    }
    if (button && button.parentElement !== slot) {
        slot.appendChild(button);
    }
}

function renderRecentTasks(panel) {
    const root = panel?.container?.querySelector('[data-ai-hook="idle-recent"]');
    const list = panel?.container?.querySelector('[data-ai-hook="idle-recent-list"]');
    if (!root || !list) return;

    const history = Array.isArray(panel.history) ? panel.history.slice(0, 3) : [];
    root.hidden = history.length === 0;
    list.innerHTML = history.map(item => {
        const sessionId = panel._escapeHtml?.(String(item?.sessionId || '')) || '';
        const title = panel._escapeHtml?.(
            panel._sanitizeSessionHistoryText?.(item?.lastMessage, 88) || '未命名任务'
        ) || '未命名任务';
        const time = panel._escapeHtml?.(panel._formatHistoryTime?.(item?.updatedAtUtc) || '') || '';
        return `
            <button type="button" class="ai-idle-recent-item" data-ai-hook="idle-recent-item" data-session-id="${sessionId}">
                <span>${title}</span>
                <small>${time}</small>
            </button>
        `;
    }).join('');

    list.querySelectorAll('[data-ai-hook="idle-recent-item"]').forEach(button => {
        button.addEventListener('click', () => panel._switchToSession?.(button.dataset.sessionId || ''));
    });
}

export function syncAiPanelShell(panel) {
    const root = panel?.container?.querySelector('[data-ai-hook="shell"]');
    if (!root) return;

    const state = readState(panel);
    const projection = readProjection(panel);
    const active = isActiveProjection(projection);
    root.dataset.aiShellState = active ? 'active' : 'idle';
    panel.container.dataset.aiShellState = root.dataset.aiShellState;

    const context = panel.container.querySelector('[data-ai-hook="task-context"]');
    if (context) context.hidden = !active;
    const workbenchPane = panel.container.querySelector('[data-ai-hook="workbench-pane"]');
    if (workbenchPane) workbenchPane.hidden = !active;
    const chatContainer = panel.container.querySelector('#ai-chat-container');
    if (chatContainer) chatContainer.hidden = !active;

    setText(panel.container.querySelector('[data-ai-hook="task-title"]'), readTaskTitle(panel, state), { hideWhenEmpty: true });
    setText(panel.container.querySelector('[data-ai-hook="task-phase"]'), readStageLabel(panel, projection));

    const blockerCount = countReliableBlockers(projection);
    const blockers = panel.container.querySelector('[data-ai-hook="task-blockers"]');
    if (blockers) {
        blockers.hidden = blockerCount === 0;
        blockers.textContent = blockerCount > 0 ? `${blockerCount} 项阻断` : '';
    }

    setText(panel.container.querySelector('[data-ai-hook="task-next-step"]'), readNextStep(state, projection), { hideWhenEmpty: true });
    moveConversationActions(panel, active);
    movePrimaryAction(panel, projection, active);
    renderRecentTasks(panel);
}

export function initializeAiPanelShell(panel) {
    const root = panel?.container?.querySelector('[data-ai-hook="shell"]');
    if (!root || root.dataset.aiShellBound === 'true') return;
    root.dataset.aiShellBound = 'true';

    panel.container.querySelectorAll('[data-ai-shell-pane]').forEach(button => {
        button.addEventListener('click', () => {
            const pane = button.dataset.aiShellPane === 'conversation' ? 'conversation' : 'workbench';
            root.dataset.aiActivePane = pane;
            panel.container.querySelectorAll('[data-ai-shell-pane]').forEach(candidate => {
                candidate.setAttribute('aria-selected', candidate.dataset.aiShellPane === pane ? 'true' : 'false');
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

function wrapPresentationMethod(prototype, methodName) {
    const original = prototype?.[methodName];
    if (typeof original !== 'function' || original.__aiShellWrapped) return;

    const wrapped = function (...args) {
        const result = original.apply(this, args);
        if (result && typeof result.finally === 'function') {
            return result.finally(() => syncAiPanelShell(this));
        }
        syncAiPanelShell(this);
        return result;
    };
    wrapped.__aiShellWrapped = true;
    prototype[methodName] = wrapped;
}

export function installAiPanelShellPresentation(prototype) {
    [
        '_renderAgentWorkspaceOverview',
        '_renderPlanWorkspace',
        '_renderBuildWorkspaceFromAgentRun',
        '_renderHistoryList',
        '_setWorkbenchState'
    ].forEach(methodName => wrapPresentationMethod(prototype, methodName));
}

export const aiPanelShellTestApi = {
    countReliableBlockers,
    isActiveProjection,
    readNextStep,
    readTaskTitle
};
