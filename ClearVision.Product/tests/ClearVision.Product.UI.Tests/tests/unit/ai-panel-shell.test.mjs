import test from 'node:test';
import assert from 'node:assert/strict';
import {
  aiPanelShellTestApi,
  deriveAiShellPresentation,
  initializeAiPanelShell,
  installAiPanelShellPresentation,
  scheduleAiPanelShellSync,
  syncAiPanelShell,
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelShellPresentation.js';

function createIdleState(overrides = {}) {
  return {
    intent: null,
    plan: null,
    result: null,
    run: {
      plan: { runId: '', status: 'idle' },
      build: { runId: '', status: 'idle' },
    },
    apply: { status: 'idle' },
    projection: {
      phase: 'idle',
      clarificationQueue: [],
      readiness: { canBuild: false, primaryMessage: '' },
      buildAction: { canBuild: false, canStart: false, status: 'no_plan' },
    },
    ...overrides,
  };
}

function createElement() {
  const listeners = new Map();
  return {
    dataset: {},
    hidden: false,
    textContent: '',
    innerHTML: '',
    parentElement: null,
    children: [],
    attributes: new Map(),
    listeners,
    addEventListener(type, handler) {
      const handlers = listeners.get(type) || [];
      handlers.push(handler);
      listeners.set(type, handlers);
    },
    setAttribute(name, value) { this.attributes.set(name, String(value)); },
    getAttribute(name) { return this.attributes.get(name) ?? null; },
    appendChild(child) {
      child.parentElement?.removeChild?.(child);
      this.children.push(child);
      child.parentElement = this;
    },
    prepend(child) {
      child.parentElement?.removeChild?.(child);
      this.children.unshift(child);
      child.parentElement = this;
    },
    removeChild(child) {
      this.children = this.children.filter(candidate => candidate !== child);
      if (child.parentElement === this) child.parentElement = null;
    },
    remove() { this.parentElement?.removeChild?.(this); },
    contains(child) { return child === this || this.children.includes(child); },
    querySelector(selector) {
      if (selector === 'button') return this.children.find(child => child.tagName === 'BUTTON') || null;
      return null;
    },
    querySelectorAll() { return []; },
  };
}

function createShellHarness() {
  const root = createElement();
  const context = createElement();
  const workbench = createElement();
  const chat = createElement();
  const title = createElement();
  const phase = createElement();
  const blockers = createElement();
  const nextStep = createElement();
  const slot = createElement();
  const planActions = createElement();
  const idleRecent = createElement();
  const recentList = createElement();
  const idleActions = createElement();
  const moreMenu = createElement();
  const moreButton = createElement();
  moreButton.setAttribute('aria-expanded', 'false');
  const actions = createElement();
  const tabs = [];
  let buttons = [];
  const selectors = new Map([
    ['[data-ai-hook="shell"]', root],
    ['[data-ai-hook="task-context"]', context],
    ['[data-ai-hook="workbench-pane"]', workbench],
    ['#ai-chat-container', chat],
    ['[data-ai-hook="task-title"]', title],
    ['[data-ai-hook="task-phase"]', phase],
    ['[data-ai-hook="task-blockers"]', blockers],
    ['[data-ai-hook="task-next-step"]', nextStep],
    ['[data-ai-hook="task-primary-action"]', slot],
    ['.ai-plan-actions', planActions],
    ['[data-ai-hook="idle-recent"]', idleRecent],
    ['[data-ai-hook="idle-recent-list"]', recentList],
    ['[data-ai-hook="idle-actions"]', idleActions],
    ['[data-ai-hook="task-more-menu"]', moreMenu],
    ['[data-ai-hook="task-more"]', moreButton],
    ['.ai-pane-actions', actions],
  ]);
  const container = createElement();
  container.querySelector = selector => selectors.get(selector) || null;
  container.querySelectorAll = selector => {
    if (selector === '[data-ai-shell-pane]') return tabs;
    if (selector.startsWith('#')) return buttons.filter(button => button.id === selector.slice(1) && button.parentElement);
    return [];
  };
  return {
    root,
    context,
    workbench,
    chat,
    title,
    phase,
    blockers,
    nextStep,
    slot,
    planActions,
    recentList,
    idleRecent,
    moreButton,
    moreMenu,
    container,
    setButtons(value) { buttons = value; },
  };
}

function createButton(id, clickHandler = null) {
  const button = createElement();
  button.id = id;
  button.tagName = 'BUTTON';
  button.clickHandler = clickHandler;
  return button;
}

test('AI shell is idle only for a brand-new canonical and presentation state', () => {
  const panel = { agentWorkspaceState: createIdleState() };
  assert.equal(deriveAiShellPresentation(panel).shellState, 'idle');
});

test('AI shell canonical activity matrix covers intent, plan, result, runs, apply, and phase', () => {
  const cases = [
    ['phase', state => { state.projection.phase = 'routing'; }],
    ['intent', state => { state.intent = { description: 'inspect' }; }],
    ['plan', state => { state.plan = { goal: 'inspect' }; }],
    ['result', state => { state.result = { flow: { operators: [] } }; }],
    ['plan run id', state => { state.run.plan.runId = 'plan-run'; }],
    ['plan run status', state => { state.run.plan.status = 'running'; }],
    ['build run id', state => { state.run.build.runId = 'build-run'; }],
    ['build run status', state => { state.run.build.status = 'failed'; }],
    ['apply', state => { state.apply.status = 'applied'; }],
  ];

  for (const [name, mutate] of cases) {
    const state = createIdleState();
    mutate(state);
    assert.equal(deriveAiShellPresentation({ agentWorkspaceState: state }).shellState, 'active', name);
  }
});

test('Router and generate pending signals activate only the display shell', () => {
  for (const panel of [
    { activeIntentRouterRequestId: 'router-1' },
    { activeGenerateRequestId: 'generate-1' },
    { isGenerating: true, lastUserPrompt: 'inspect the terminal' },
  ]) {
    panel.agentWorkspaceState = createIdleState();
    const beforeProjection = structuredClone(panel.agentWorkspaceState.projection);
    const presentation = deriveAiShellPresentation(panel);
    assert.equal(presentation.shellState, 'active');
    assert.equal(presentation.requestPending, true);
    assert.deepEqual(panel.agentWorkspaceState.projection, beforeProjection);
    assert.equal(panel.agentWorkspaceState.projection.buildAction.canBuild, false);
  }
});

test('AI shell reads task title and next step from canonical data before UI fallbacks', () => {
  assert.equal(aiPanelShellTestApi.readTaskTitle({ lastUserPrompt: 'fallback' }, {
    plan: { goal: '检测端子线序' },
  }), '检测端子线序');
  assert.equal(aiPanelShellTestApi.readTaskTitle({ lastUserPrompt: 'fallback' }, {
    plan: null,
    intent: { description: '检查包装箱外观' },
  }), '检查包装箱外观');
  assert.equal(aiPanelShellTestApi.readNextStep({
    plan: { nextAction: '计划建议' },
  }, {
    readiness: { primaryMessage: '请先确认图像来源' },
  }), '请先确认图像来源');
});

test('AI shell uses the shared missing summary including resources and deferred items', () => {
  const state = createIdleState({ plan: {}, readinessStatus: 'blocked' });
  state.projection.clarificationQueue = [{ blocksBuild: true }, { blocksBuild: true }];
  for (const [total, detail] of [[3, '构建前必需 3 项'], [3, '构建前必需 2 项 · 可后补 1 项'], [0, '构建前必需 0 项']]) {
    const panel = {
      agentWorkspaceState: state,
      _getCurrentCanonicalPreview: () => ({}),
      _buildPlanMissingSummary: () => ({ totalCount: total, summaryText: detail })
    };
    const presentation = deriveAiShellPresentation(panel);
    assert.equal(presentation.blockerCount, total);
    assert.equal(presentation.countText, total ? `待补齐 ${total} 项` : '');
    assert.equal(presentation.nextStep, detail);
    assert.equal(state.projection.buildAction.canBuild, false);
  }
});

test('AI shell hides definite counts while validation is missing, stale, failed, or recovering', () => {
  for (const status of ['idle', 'validating', 'timeout', 'failed', 'ready', 'blocked']) {
    const panel = {
      agentWorkspaceState: createIdleState({ plan: {}, readinessStatus: status }),
      _getCurrentCanonicalPreview: () => null,
      _getPlanBuildActionState: () => ({ statusText: `校验状态 ${status}` }),
      _buildPlanMissingSummary: () => { throw new Error('stale count must not be read'); }
    };
    assert.equal(deriveAiShellPresentation(panel).blockerCount, null);
    assert.equal(deriveAiShellPresentation(panel).nextStep, `校验状态 ${status}`);
    panel.workspaceRecoveryBlocked = true;
    panel._getCurrentCanonicalPreview = () => ({});
    assert.equal(deriveAiShellPresentation(panel).countText, '');
  }
});

test('AI shell stops reading Plan counts after entering Build', () => {
  const panel = {
    agentWorkspaceState: createIdleState({ plan: {} }),
    _getAgentWorkspacePhase: () => 'build',
    _buildPlanMissingSummary: () => { throw new Error('Plan count is no longer relevant'); }
  };
  const presentation = deriveAiShellPresentation(panel);
  assert.equal(presentation.blockerCount, 0);
  assert.equal(presentation.countText, '');
});

test('canonical phase text matrix is independent from workspace mode', () => {
  const expected = {
    routing: '正在判断请求类型',
    clarifying: '等待补充信息',
    plan_blocked: '方案待补充',
    ready_to_build: '方案可构建',
    building: '正在构建',
    build_failed: '构建失败',
    applied: '已应用',
  };
  for (const [phase, label] of Object.entries(expected)) {
    const panel = {
      agentWorkspaceMode: phase === 'building' ? 'plan' : 'build',
      workbenchState: 'unrelated-mode',
      _formatWorkspaceModeLabel: () => '不应显示的 Workspace Mode',
      agentWorkspaceState: createIdleState({ projection: { phase, clarificationQueue: [], readiness: {} } }),
    };
    assert.equal(deriveAiShellPresentation(panel).phaseText, label, phase);
  }
});

test('restored message-only and serialized result sessions are presentation-active signals', async () => {
  const Prototype = function () {};
  Prototype.prototype._handleGetAiSessionResult = function () {};
  installAiPanelShellPresentation(Prototype.prototype);
  const panel = new Prototype();
  panel.agentWorkspaceState = createIdleState();
  panel.pendingSessionLoad = { sessionId: 'message-only', requestId: 'message-request', epoch: 1 };

  panel._handleGetAiSessionResult({
    payload: {
      success: true,
      sessionId: 'message-only',
      requestId: 'message-request',
      navigationEpoch: 1,
      session: { history: [{ role: 'user', message: '恢复的消息' }] },
    },
  });
  assert.equal(deriveAiShellPresentation(panel).shellState, 'active');
  assert.equal(deriveAiShellPresentation(panel).taskTitle, '恢复的消息');

  const resultOnlyPanel = new Prototype();
  resultOnlyPanel.agentWorkspaceState = createIdleState();
  resultOnlyPanel.pendingSessionLoad = { sessionId: 'result-only', requestId: 'result-request', epoch: 2 };
  resultOnlyPanel._handleGetAiSessionResult({
    payload: {
      success: true,
      sessionId: 'result-only',
      requestId: 'result-request',
      navigationEpoch: 2,
      session: { history: [], currentFlowJson: '{"operators":[]}' },
    },
  });
  assert.equal(deriveAiShellPresentation(resultOnlyPanel).shellState, 'active');
});

test('stale history responses cannot activate the current shell presentation', () => {
  const Prototype = function () {};
  Prototype.prototype._handleGetAiSessionResult = function () {};
  installAiPanelShellPresentation(Prototype.prototype);
  const panel = new Prototype();
  panel.agentWorkspaceState = createIdleState();
  panel.pendingSessionLoad = { sessionId: 'current-load', requestId: 'current-request', epoch: 4 };
  panel._handleGetAiSessionResult({
    payload: {
      success: true,
      sessionId: 'stale-load',
      requestId: 'stale-request',
      navigationEpoch: 3,
      session: { history: [{ role: 'user', message: '不应恢复的消息' }] },
    },
  });
  assert.equal(deriveAiShellPresentation(panel).shellState, 'idle');
});

test('canonical Result-only presentation does not rewrite projection or business gates', () => {
  const state = createIdleState({ result: { flow: { operators: [] } } });
  const before = structuredClone(state);
  const presentation = deriveAiShellPresentation({ agentWorkspaceState: state });
  assert.equal(presentation.shellState, 'active');
  assert.deepEqual(state, before);
  assert.equal(state.projection.buildAction.canBuild, false);
});

test('canonical RESET clears restored presentation content and returns shell to idle', async () => {
  const Prototype = function () {};
  Prototype.prototype._handleGetAiSessionResult = function () {};
  Prototype.prototype._dispatchAgentWorkspaceEvent = function (event) {
    if (event.type === 'workspace/reset') this.agentWorkspaceState = createIdleState();
    return this.agentWorkspaceState;
  };
  installAiPanelShellPresentation(Prototype.prototype);
  const panel = new Prototype();
  panel.agentWorkspaceState = createIdleState();
  panel.pendingSessionLoad = { sessionId: 'reset-session', requestId: 'reset-request', epoch: 3 };
  panel._handleGetAiSessionResult({
    payload: {
      success: true,
      sessionId: 'reset-session',
      requestId: 'reset-request',
      navigationEpoch: 3,
      session: { history: [{ role: 'assistant', message: '历史内容' }] },
    },
  });
  assert.equal(deriveAiShellPresentation(panel).shellState, 'active');
  panel._dispatchAgentWorkspaceEvent({ type: 'workspace/reset' });
  await Promise.resolve();
  assert.equal(deriveAiShellPresentation(panel).shellState, 'idle');
});

test('canonical event dispatch schedules shell sync without an old renderer call', async () => {
  const Prototype = function () {};
  Prototype.prototype._dispatchAgentWorkspaceEvent = function () {
    this.agentWorkspaceState = createIdleState({ result: { flow: { operators: [] } } });
    return this.agentWorkspaceState;
  };
  installAiPanelShellPresentation(Prototype.prototype);
  const harness = createShellHarness();
  const panel = new Prototype();
  panel.container = harness.container;
  panel.agentWorkspaceState = createIdleState();
  panel._getAgentWorkspacePhase = () => 'plan';

  panel._dispatchAgentWorkspaceEvent({ type: 'workspace/result-received' });
  assert.notEqual(harness.root.dataset.aiShellState, 'active');
  await Promise.resolve();
  assert.equal(harness.root.dataset.aiShellState, 'active');
});

test('batched shell sync keeps a rebuilt primary button unique and preserves its handler', async () => {
  const harness = createShellHarness();
  const oldHandler = () => 'old';
  const newHandler = () => 'new';
  const oldButton = createButton('ai-btn-start-build', oldHandler);
  const newButton = createButton('ai-btn-start-build', newHandler);
  harness.slot.appendChild(oldButton);
  harness.planActions.appendChild(newButton);
  harness.setButtons([oldButton, newButton]);
  const panel = {
    container: harness.container,
    agentWorkspaceState: createIdleState({ plan: { goal: '构建任务' } }),
    _getAgentWorkspacePhase: () => 'plan',
    history: [],
  };

  scheduleAiPanelShellSync(panel);
  scheduleAiPanelShellSync(panel);
  await Promise.resolve();
  assert.equal(harness.slot.children.length, 1);
  assert.equal(harness.slot.children[0], newButton);
  assert.equal(harness.slot.children[0].clickHandler, newHandler);
  assert.equal(oldButton.parentElement, null);

  syncAiPanelShell(panel);
  assert.equal(harness.slot.children.length, 1);
  assert.equal(harness.slot.children[0], newButton);
});

test('repeated initialization binds shell and recent-task clicks once', () => {
  const harness = createShellHarness();
  let switches = 0;
  const panel = {
    container: harness.container,
    agentWorkspaceState: createIdleState(),
    history: [{ sessionId: 'history-1', lastMessage: '历史任务', updatedAtUtc: '2026-07-11' }],
    _escapeHtml: value => value,
    _sanitizeSessionHistoryText: value => value,
    _formatHistoryTime: value => value,
    _switchToSession: sessionId => { if (sessionId === 'history-1') switches += 1; },
    _getAgentWorkspacePhase: () => 'plan',
  };

  initializeAiPanelShell(panel);
  initializeAiPanelShell(panel);
  syncAiPanelShell(panel);
  assert.equal(harness.recentList.listeners.get('click').length, 1);
  assert.equal(harness.moreButton.listeners.get('click').length, 1);

  const button = { dataset: { sessionId: 'history-1' } };
  harness.recentList.contains = candidate => candidate === button;
  const target = { closest: () => button };
  harness.recentList.listeners.get('click')[0]({ target });
  assert.equal(switches, 1);
});
