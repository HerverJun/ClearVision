const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('@playwright/test');

const cdpPort = Number(process.env.CV_CDP_PORT || 9223);
const scale = Number(process.env.CV_DPI_SCALE || 1);
const phase = String(process.env.CV_SMOKE_PHASE || 'full').trim().toLowerCase();
const token = String(process.env.CV_SMOKE_TOKEN || '');
const user = String(process.env.CV_SMOKE_USER || '');
const evidenceDir = path.resolve(process.env.CV_EVIDENCE_DIR || '.tmp/ai-webview2-release-evidence');
const closeFlushMarker = 'cv_ai_webview2_close_flush_probe_v1';
const rollbackRecoveryFixtureKey = 'cv_ai_webview2_rollback_recovery_fixture_v1';

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function safeFileName(value) {
  return String(value).replace(/[^a-z0-9_.-]+/gi, '-');
}

const buildFlow = {
  operators: [
    {
      id: 'smoke-acq', type: 'ImageAcquisition', name: 'WebView2 图像采集', x: 80, y: 96,
      inputPorts: [], outputPorts: [{ id: 'smoke-acq-out', name: 'Image', dataType: 'Image' }],
      parameters: [{ name: 'CameraId', value: 'camera-smoke' }], isEnabled: true,
    },
    {
      id: 'smoke-output', type: 'ResultOutput', name: 'WebView2 结果输出', x: 420, y: 96,
      inputPorts: [{ id: 'smoke-output-in', name: 'Result', dataType: 'Image', isRequired: true }],
      outputPorts: [], parameters: [], isEnabled: true,
    },
  ],
  connections: [
    {
      id: 'smoke-connection', sourceOperatorId: 'smoke-acq', sourcePortId: 'smoke-acq-out',
      targetOperatorId: 'smoke-output', targetPortId: 'smoke-output-in',
    },
  ],
};

function readyPayload() {
  const gate = {
    canvasApplyReady: true,
    runtimeDraftReady: true,
    deploymentReady: true,
    blocked: false,
    status: 'ready',
    deploymentBlockers: [],
    metadataOnly: true,
  };
  return {
    success: true,
    status: 'completed',
    completionStatus: 'completed',
    interactionState: 'completed',
    aiExplanation: 'WebView2 发布冒烟构建结果。',
    flow: buildFlow,
    pendingParameters: [],
    missingResources: [],
    validationPreview: {
      structuralValidation: { passed: true, status: 'passed' },
      dryRun: { succeeded: true, status: 'completed', inputSummary: 'webview2-smoke' },
      deploymentPrecheck: { readyForDeployment: true, deploymentBlocked: false },
    },
    buildResult: {
      buildId: 'webview2-release-smoke',
      workflowDraft: { operatorCount: 2, connectionCount: 1 },
      operatorPipeline: buildFlow.operators.map(operator => ({
        tempId: operator.id,
        operatorType: operator.type,
        displayName: operator.name,
        status: 'completed',
        source: 'release-smoke',
      })),
      parameterMapping: [],
      workflowDiff: {
        addedNodes: buildFlow.operators.map(operator => operator.name),
        modifiedNodes: [], removedNodes: [], connectionChanges: buildFlow.connections,
        parameterChanges: [], pendingParameters: [], deploymentBlockers: [],
      },
      applyGate: gate,
      metadataOnly: true,
    },
    applyGate: gate,
    metadataOnly: true,
  };
}

async function connect() {
  const version = await fetch(`http://127.0.0.1:${cdpPort}/json/version`).then(response => {
    if (!response.ok) throw new Error(`CDP version endpoint returned ${response.status}`);
    return response.json();
  });
  return chromium.connectOverCDP(version.webSocketDebuggerUrl);
}

async function activateView(page, view) {
  const navigation = page.locator(`.nav-btn[data-view="${view}"]`);
  await navigation.focus();
  await navigation.press('Enter');
  await page.waitForFunction(expectedView =>
    document.querySelector('.nav-btn.active')?.dataset.view === expectedView,
  view);
}

async function authenticateAndOpenAi(page) {
  assert(token, 'CV_SMOKE_TOKEN is required.');
  assert(user, 'CV_SMOKE_USER is required.');
  const preNavigationState = await page.evaluate(({ authToken, authUser, markerKeyPrefix, recoveryKey }) => {
    sessionStorage.setItem('cv_auth_token', authToken);
    sessionStorage.setItem('cv_current_user', authUser);
    localStorage.setItem('cv_welcome_shown', 'true');
    try {
      let key = '';
      for (let index = 0; index < localStorage.length; index += 1) {
        const candidate = localStorage.key(index) || '';
        if (candidate.startsWith(`${markerKeyPrefix}:`)) { key = candidate; break; }
      }
      return {
        safetyMarker: key ? JSON.parse(localStorage.getItem(key) || 'null') : null,
        recoveryFixture: JSON.parse(localStorage.getItem(recoveryKey) || 'null'),
      };
    } catch { return { safetyMarker: null, recoveryFixture: null }; }
  }, {
    authToken: token,
    authUser: user,
    markerKeyPrefix: 'cv_ai_apply_safety_block_v1',
    recoveryKey: rollbackRecoveryFixtureKey,
  });
  // Keep the WebView2 virtual-host origin so the authenticated sessionStorage
  // written above survives navigation from login.html to the production root.
  await page.goto('https://app.local/index.html');
  await page.waitForSelector('#loading-screen', { state: 'hidden', timeout: 45_000 });
  await activateView(page, 'ai');
  await page.waitForFunction(() => Boolean(window.aiPanel && !window.aiPanel._disposed));
  await page.waitForSelector('#ai-view .ai-shell', { timeout: 30_000 });
  return preNavigationState;
}

async function resetSmokeConversation(page, { clearSafety = false } = {}) {
  await page.evaluate(async ({ shouldClearSafety }) => {
    const panel = window.aiPanel;
    await panel._handleNewConversation();
    panel.sessionId = 'webview2-release-smoke-session';
    if (shouldClearSafety) panel._clearApplySafetyBlock?.({ clearPersisted: true });
  }, { shouldClearSafety: clearSafety });
}

async function captureLayout(page, label, theme) {
  const conversationPane = page.locator('[data-ai-shell-pane="conversation"]:visible').first();
  if (await conversationPane.count()) {
    await conversationPane.click();
  }
  await page.evaluate(selectedTheme => {
    document.documentElement.dataset.theme = selectedTheme;
  }, theme);
  await page.waitForTimeout(100);
  const layout = await page.evaluate(() => {
    const ai = document.querySelector('#ai-view');
    const composer = document.querySelector('#ai-input');
    const send = document.querySelector('#ai-btn-gen');
    const rect = element => {
      const value = element?.getBoundingClientRect?.();
      return value ? { width: value.width, height: value.height, left: value.left, right: value.right } : null;
    };
    return {
      devicePixelRatio: window.devicePixelRatio,
      documentOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      bodyOverflow: document.body.scrollWidth - document.body.clientWidth,
      aiOverflow: ai ? ai.scrollWidth - ai.clientWidth : null,
      composer: rect(composer),
      send: rect(send),
      viewport: { width: innerWidth, height: innerHeight },
    };
  });
  await page.screenshot({ path: path.join(evidenceDir, `${safeFileName(label)}-${theme}.png`) });
  assert(layout.documentOverflow <= 1, `document overflow at ${label}: ${JSON.stringify(layout)}`);
  assert(layout.bodyOverflow <= 1, `body overflow at ${label}: ${JSON.stringify(layout)}`);
  assert(layout.aiOverflow <= 1, `AI view overflow at ${label}: ${JSON.stringify(layout)}`);
  assert(layout.send && layout.send.width >= 32 && layout.send.height >= 32,
    `send target too small at ${label}: ${JSON.stringify(layout.send)}`);
  return { label, theme, ...layout };
}

async function captureFormalProductLayouts(page) {
  const formalViews = {
    project: '#project-view',
    flow: '#flow-editor',
    inspection: '#inspection-view',
    results: '#results-view',
    stations: '#stations-view',
    ai: '#ai-view',
    settings: '#settings-view',
  };
  const settingsTabs = [
    'general', 'communication', 'tcp', 'station', 'storage',
    'database', 'runtime', 'cameras', 'ai', 'users',
  ];
  const standardSettingsTabs = new Set(['general', 'storage', 'runtime']);
  const wideSettingsTabs = new Set(['communication', 'tcp', 'station', 'cameras', 'users']);
  const result = {};

  const collect = async (view, ownerSelector) => page.evaluate(({ currentView, selector }) => {
    const rect = element => {
      const value = element?.getBoundingClientRect?.();
      return value ? {
        x: Math.round(value.x), y: Math.round(value.y),
        width: Math.round(value.width), height: Math.round(value.height),
        clientWidth: element.clientWidth, scrollWidth: element.scrollWidth,
        clientHeight: element.clientHeight, scrollHeight: element.scrollHeight,
      } : null;
    };
    const visible = element => {
      if (!element) return false;
      const value = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return style.display !== 'none' && style.visibility !== 'hidden'
        && value.width > 0 && value.height > 0
        && value.bottom > 0 && value.top < innerHeight;
    };
    const hit = element => {
      if (!visible(element)) return false;
      const value = element.getBoundingClientRect();
      const target = document.elementFromPoint(value.left + value.width / 2, value.top + value.height / 2);
      return target === element || Boolean(target && element.contains(target));
    };
    const owner = document.querySelector(selector);
    const settingsPanel = document.querySelector('.settings-panel.active');
    const formRows = new Map();
    document.querySelectorAll('.settings-panel.active .settings-fieldset').forEach(element => {
      const value = element.getBoundingClientRect();
      if (value.width <= 0 || value.height <= 0) return;
      const key = Math.round(value.top);
      formRows.set(key, (formRows.get(key) || 0) + 1);
    });
    return {
      view: currentView,
      viewport: { width: innerWidth, height: innerHeight, devicePixelRatio },
      document: {
        clientWidth: document.documentElement.clientWidth,
        scrollWidth: document.documentElement.scrollWidth,
        overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      },
      body: {
        clientWidth: document.body.clientWidth,
        scrollWidth: document.body.scrollWidth,
        overflow: document.body.scrollWidth - document.body.clientWidth,
      },
      toolbar: rect(document.querySelector('.toolbar')),
      toolbarLeft: rect(document.querySelector('.toolbar-left')),
      toolbarRight: rect(document.querySelector('.toolbar-right')),
      main: rect(document.querySelector('#main-content')),
      owner: rect(owner),
      ownerOverflow: owner ? owner.scrollWidth - owner.clientWidth : null,
      settingsContent: rect(document.querySelector('.settings-content-area')),
      settingsPanel: rect(settingsPanel),
      settingsPanelOverflow: settingsPanel ? settingsPanel.scrollWidth - settingsPanel.clientWidth : null,
      maxFormColumns: Math.max(0, ...formRows.values()),
      actions: {
        settingsNavigation: hit(document.querySelector('.nav-btn[data-view="settings"]')),
        finalDecision: hit(document.querySelector('#btn-final-decision')),
        save: hit(document.querySelector('#btn-save')),
        run: hit(document.querySelector('#btn-run')),
        settingsSaveVisible: visible(document.querySelector('#btn-save-settings')),
      },
    };
  }, { currentView: view, selector: ownerSelector });

  for (const theme of ['light', 'dark']) {
    await page.evaluate(selectedTheme => { document.documentElement.dataset.theme = selectedTheme; }, theme);
    await page.waitForTimeout(100);
    const themeResult = {};
    for (const [view, selector] of Object.entries(formalViews)) {
      await activateView(page, view);
      await page.waitForSelector(`${selector}:not(.hidden)`, { state: 'visible', timeout: 30_000 });
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      const metrics = await collect(view, selector);
      themeResult[view] = metrics;
      await page.screenshot({ path: path.join(evidenceDir, `formal-dpi-${safeFileName(scale)}-${theme}-${view}.png`) });
      assert(metrics.document.overflow <= 1 && metrics.body.overflow <= 1,
        `${view} root overflow at DPI ${scale} ${theme}: ${JSON.stringify(metrics)}`);
      assert(metrics.actions.settingsNavigation && metrics.actions.finalDecision
        && metrics.actions.save && metrics.actions.run,
      `${view} toolbar action unreachable at DPI ${scale} ${theme}: ${JSON.stringify(metrics.actions)}`);
      assert((metrics.toolbarLeft?.x || 0) + (metrics.toolbarLeft?.width || 0)
        <= (metrics.toolbarRight?.x || 0) + 1,
      `${view} toolbar regions overlap at DPI ${scale} ${theme}.`);

      if (view === 'settings') {
        const tabResult = {};
        for (const tabName of settingsTabs) {
          const tab = page.locator(`.settings-menu-item[data-tab="${tabName}"]`);
          await tab.click();
          await page.waitForSelector(`.settings-panel[data-section="${tabName}"].active`, { state: 'visible' });
          await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
          const tabMetrics = await collect('settings', '#settings-view');
          tabResult[tabName] = tabMetrics;
          await page.screenshot({ path: path.join(evidenceDir, `formal-dpi-${safeFileName(scale)}-${theme}-settings-${tabName}.png`) });
          assert(tabMetrics.document.overflow <= 1 && tabMetrics.body.overflow <= 1,
            `settings/${tabName} root overflow at DPI ${scale} ${theme}.`);
          assert(tabMetrics.actions.settingsSaveVisible,
            `settings/${tabName} save action unreachable at DPI ${scale} ${theme}.`);
          const availableWidth = Math.max(0, (tabMetrics.settingsContent?.width || 0) - 64);
          const expectedWidth = standardSettingsTabs.has(tabName)
            ? Math.min(availableWidth, 1160)
            : wideSettingsTabs.has(tabName)
              ? Math.min(availableWidth, 1440)
              : availableWidth;
          assert((tabMetrics.settingsPanel?.width || 0) >= expectedWidth,
            `settings/${tabName} remains artificially narrow at DPI ${scale} ${theme}: ${JSON.stringify(tabMetrics)}`);
        }
        themeResult.settingsTabs = tabResult;
      }
    }
    result[theme] = themeResult;
  }
  await activateView(page, 'ai');
  return result;
}

async function verifyImeComposition(page) {
  const input = page.locator('#ai-input');
  await input.fill('');
  await input.focus();
  const session = await page.context().newCDPSession(page);
  await session.send('Input.imeSetComposition', {
    text: '中文组合输入', selectionStart: 6, selectionEnd: 6,
    replacementStart: 0, replacementEnd: 0,
  });
  await page.evaluate(() => window.aiPanel?._renderAgentWorkspaceOverview?.());
  const during = await input.inputValue();
  const focusedDuring = await input.evaluate(element => document.activeElement === element);
  await session.send('Input.insertText', { text: '中文组合输入' });
  const committed = await input.inputValue();
  const focusedAfter = await input.evaluate(element => document.activeElement === element);
  await session.detach();
  assert(during.includes('中文组合输入'), 'IME composition text was lost during rerender.');
  assert(committed.includes('中文组合输入'), 'IME committed text was lost.');
  assert(focusedDuring && focusedAfter, 'IME composition lost focus.');
  return { during, committed, focusedDuring, focusedAfter, protocol: 'CDP Input.imeSetComposition' };
}

async function seedReadyBuild(page) {
  const payload = readyPayload();
  await page.evaluate(payloadValue => {
    const panel = window.aiPanel;
    const plan = panel._normalizeBackendPlanResult({
      planId: 'webview2-smoke-plan',
      planHash: 'sha256:webview2-smoke-plan',
      originalUserPrompt: 'WebView2 发布冒烟',
      goal: '验证真实 WebView2 Apply 路径',
      requirementMode: 'strict',
      recommendedRoute: {
        routeId: 'webview2-smoke-route', title: 'WebView2 冒烟流程',
        summary: '采集并输出结果', operators: ['ImageAcquisition', 'ResultOutput'],
      },
      clarificationQuestions: [], acceptanceCriteria: ['流程可安全应用'],
      canPlan: true, canBuild: true,
      buildReadiness: { canBuild: true, blockers: [], resolvedFields: ['goal'], remainingFields: [] },
      metadataOnly: true,
    }, 'WebView2 发布冒烟');
    panel.pendingVisionPlan = plan;
    panel._setCurrentResult(payloadValue);
    panel.activeAgentRunId = 'webview2-smoke-run';
    panel.activeAgentRunEvents = [{
      runId: 'webview2-smoke-run', sequence: 1, eventType: 'run.completed',
      stage: 'run', status: 'completed', summary: payloadValue.aiExplanation, payload: payloadValue,
    }];
    panel.agentWorkspaceMode = 'build';
    panel.workspaceViewMode = 'build';
    panel._displayResult(payloadValue, { appendChatMessage: false });
    panel._setWorkspaceViewMode('build', { persist: false, render: false });
    panel._setWorkbenchState('ready_to_apply');
    panel._renderAgentRuntime(payloadValue);
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(plan);
    panel._renderBuildWorkspaceFromAgentRun();
    panel._setWorkspaceViewMode('build', { persist: false, render: true });
    panel._renderWorkbenchStateBar();
    panel._updateApplyButtonState();
    panel._syncShellPresentation?.();
  }, payload);
  await page.waitForSelector('#ai-build-workspace:not([hidden])');
  assert(await page.locator('#ai-btn-apply').isEnabled(), 'Apply was not enabled for a ready result.');
}

async function verifyDialog(page) {
  const apply = page.locator('#ai-btn-apply');
  await apply.focus();
  await page.keyboard.press('Enter');
  await page.waitForSelector('.ai-apply-preview-dialog');
  const opened = await page.evaluate(() => ({
    role: document.querySelector('.ai-apply-preview-dialog')?.getAttribute('role'),
    modal: document.querySelector('.ai-apply-preview-dialog')?.getAttribute('aria-modal'),
    backgroundInert: document.querySelector('.ai-shell')?.hasAttribute('inert'),
    focusedClass: document.activeElement?.className || '',
  }));
  assert(opened.role === 'dialog' && opened.modal === 'true', 'Apply Preview is not a modal dialog.');
  assert(opened.backgroundInert, 'Apply Preview did not isolate the background.');
  assert(String(opened.focusedClass).includes('ai-apply-preview-confirm'), 'Apply Preview did not focus confirm.');
  await page.keyboard.press('Escape');
  await page.waitForSelector('.ai-apply-preview-overlay', { state: 'detached' });
  const closed = await page.evaluate(() => ({
    backgroundInert: document.querySelector('.ai-shell')?.hasAttribute('inert'),
    focusReturned: document.activeElement?.id === 'ai-btn-apply',
  }));
  assert(!closed.backgroundInert && closed.focusReturned, 'Apply Preview did not restore background and focus.');
  return { opened, closed };
}

async function verifyRealCanvasApplyAndRollback(page) {
  const result = await page.evaluate(() => {
    const panel = window.aiPanel;
    const canvas = window.flowCanvasAdapter || window.flowCanvas;
    const originalDeserialize = canvas.deserialize.bind(canvas);
    const originalOnApplied = panel.options.onApplied;
    const before = canvas.serialize();
    let writes = 0;
    panel.options.onApplied = () => {};
    canvas.deserialize = flow => {
      writes += 1;
      if (writes === 1) {
        originalDeserialize({ operators: flow.operators.slice(0, 1), connections: [] });
        throw new Error('webview2 injected partial write');
      }
      return originalDeserialize(flow);
    };
    const failed = panel._executeApplyFlow(panel.currentResult.flow);
    const afterRollback = canvas.serialize();
    canvas.deserialize = originalDeserialize;
    panel._setCurrentResult(panel.currentResult);
    panel.workbenchState = 'ready_to_apply';
    panel._updateApplyButtonState();
    const succeeded = panel._executeApplyFlow(panel.currentResult.flow);
    const afterApply = canvas.serialize();
    const applied = panel._isCurrentResultAppliedToCanvas();
    panel._undoApply();
    const afterUndo = canvas.serialize();
    panel.options.onApplied = originalOnApplied;
    const signature = value => JSON.stringify(value || null);
    return {
      failedResult: failed,
      failedState: panel.workbenchState,
      rollbackRestored: signature(afterRollback) === signature(before),
      succeeded,
      applied,
      appliedOperators: afterApply?.operators?.length || 0,
      appliedConnections: afterApply?.connections?.length || 0,
      undoSucceeded: signature(afterUndo) === signature(before),
      undoRestored: signature(afterUndo) === signature(before),
    };
  });
  assert(result.failedResult === false && result.rollbackRestored, 'Partial write rollback did not restore the canvas.');
  assert(result.succeeded && result.applied, 'Real canvas Apply did not complete.');
  assert(result.appliedOperators === 2 && result.appliedConnections === 1, 'Applied canvas shape is incomplete.');
  assert(result.undoSucceeded && result.undoRestored, 'Applied canvas undo did not restore the baseline.');
  return result;
}

async function armRollbackFailurePersistence(page) {
  const result = await page.evaluate(recoveryKey => {
    const panel = window.aiPanel;
    const canvas = window.flowCanvasAdapter || window.flowCanvas;
    const originalDeserialize = canvas.deserialize.bind(canvas);
    const originalOnApplied = panel.options.onApplied;
    const before = canvas.serialize();
    let writes = 0;
    panel.options.onApplied = () => {};
    canvas.deserialize = flow => {
      writes += 1;
      if (writes === 1) {
        originalDeserialize({ operators: flow.operators.slice(0, 1), connections: [] });
        throw new Error('webview2 injected apply failure');
      }
      throw new Error('webview2 injected rollback failure');
    };
    const applied = panel._executeApplyFlow(panel.currentResult.flow);
    const failedCanvas = canvas.serialize();
    const blocked = panel._applySafetyBlockReason === 'apply_rollback_failed';
    const applyDisabled = panel.container.querySelector('#ai-btn-apply')?.disabled === true;
    const marker = panel._readPersistedApplySafetyBlock?.();
    canvas.deserialize = originalDeserialize;
    panel.options.onApplied = originalOnApplied;
    const recoveryFixture = {
      version: 1,
      sessionId: panel.sessionId,
      result: panel.currentResult,
      beforeCanvas: before,
      failedCanvas,
      session: {
        sessionId: panel.sessionId,
        currentCanvasFlowJson: JSON.stringify(panel.currentResult.flow),
        history: [{ role: 'assistant', message: 'WebView2 rollback recovery fixture', payload: panel.currentResult }],
        workspaceSnapshot: {
          schemaVersion: 2,
          revision: Math.max(1, Number(panel.workspaceSnapshotRevision || 0)),
          lifecycleState: 'build',
          pendingPlanSnapshot: panel.pendingVisionPlan,
          workspaceViewMode: 'build',
        },
      },
    };
    localStorage.setItem(recoveryKey, JSON.stringify(recoveryFixture));
    panel._saveSessionId?.(panel.sessionId);
    return {
      applied,
      blocked,
      applyDisabled,
      markerReason: marker?.reason || '',
      markerSessionId: marker?.sessionId || '',
      currentSessionId: String(panel.sessionId || '').toLowerCase(),
      workbenchState: panel.workbenchState,
      baselineCanvas: before,
      failedCanvas,
      partialWriteRetained: JSON.stringify(failedCanvas) !== JSON.stringify(before),
      workspaceLifecycle: panel._buildWorkspaceSnapshotDelta().lifecycleState,
      statusNote: panel.container.querySelector('#ai-result-status-note')?.textContent?.trim() || '',
    };
  }, rollbackRecoveryFixtureKey);
  assert(result.applied === false && result.blocked && result.applyDisabled, 'Rollback failure did not establish the safety block.');
  assert(result.markerReason === 'apply_rollback_failed' && result.markerSessionId === result.currentSessionId,
    'Rollback failure safety marker was not persisted for the active session.');
  assert(result.workbenchState === 'failed', 'Rollback failure was not exposed as failed.');
  assert(result.partialWriteRetained, 'Rollback failure stage did not retain the partial canvas before Desktop close.');
  assert(result.workspaceLifecycle !== 'applied', 'Rollback failure was projected as Applied before close.');
  return result;
}

async function verifyReplayAndTransportReplacement(page) {
  let replayCalls = 0;
  await page.route('**/api/ai/agent-runs/webview2-replay/events**', route => route.fulfill({ status: 503, body: '' }));
  await page.route('**/api/ai/agent-runs/webview2-replay/stream-token', route => route.fulfill({ status: 503, body: '{}' }));
  await page.route('**/api/ai/agent-runs/webview2-replay', route => {
    replayCalls += 1;
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        events: [
          { runId: 'webview2-replay', sequence: 1, eventType: 'stage.started', stage: 'planner', status: 'running' },
          { runId: 'webview2-replay', sequence: 1, eventType: 'stage.started', stage: 'planner', status: 'running' },
          { runId: 'webview2-replay', sequence: 2, eventType: 'run.cancelled', stage: 'run', status: 'cancelled' },
        ],
      }),
    });
  });
  const replay = await page.evaluate(async () => {
    const panel = window.aiPanel;
    panel._resetAgentRunState();
    panel.activeAgentRunId = 'webview2-replay';
    panel._dispatchAgentWorkspaceEvent?.({
      type: 'workspace/run-started',
      payload: { kind: 'build', runId: 'webview2-replay' },
      sessionId: panel.sessionId,
      runId: 'webview2-replay',
    });
    const transport = panel._startAgentRunEventSource('webview2-replay');
    const started = Date.now();
    while (!transport.closed && Date.now() - started < 8_000) await new Promise(resolve => setTimeout(resolve, 25));
    const sequences = panel.activeAgentRunEvents.map(event => event.sequence);
    return {
      closed: transport.closed,
      sequences,
      uniqueSequences: [...new Set(sequences)],
      terminalCount: panel.activeAgentRunEvents.filter(event => event.eventType === 'run.cancelled').length,
    };
  });
  assert(replay.closed, 'Replay transport did not close on terminal.');
  assert(replay.terminalCount === 1, 'Replay terminal appeared more than once.');
  assert(replay.sequences.length === replay.uniqueSequences.length, 'Replay duplicated event sequences.');

  await page.route('**/api/ai/agent-runs/webview2-hang-a**', route => route.fulfill({ status: 503, body: '{}' }));
  await page.route('**/api/ai/agent-runs/webview2-hang-b**', route => route.fulfill({ status: 503, body: '{}' }));
  const replacement = await page.evaluate(async () => {
    const panel = window.aiPanel;
    panel.activeAgentRunId = 'webview2-hang-a';
    const first = panel._startAgentRunEventSource('webview2-hang-a');
    await new Promise(resolve => setTimeout(resolve, 50));
    panel.activeAgentRunId = 'webview2-hang-b';
    const second = panel._startAgentRunEventSource('webview2-hang-b');
    await new Promise(resolve => setTimeout(resolve, 50));
    panel._closeAgentRunEventSource();
    await new Promise(resolve => setTimeout(resolve, 25));
    return {
      firstClosed: first.closed,
      secondClosed: second.closed,
      activeTransport: panel.activeAgentRunTransport,
      firstPendingDelay: first.pendingDelayResolve !== null,
      secondPendingDelay: second.pendingDelayResolve !== null,
    };
  });
  assert(replacement.firstClosed && replacement.secondClosed, 'Transport replacement left an open transport.');
  assert(replacement.activeTransport === null, 'Transport replacement left an active owner.');
  assert(!replacement.firstPendingDelay && !replacement.secondPendingDelay, 'Transport close left a replay delay pending.');
  return { replayCalls, replay, replacement };
}

async function verifyLifecycleRepetition(page) {
  const before = await page.evaluate(() => {
    window.__cvSmokePanelIdentity = window.aiPanel;
    return {
      messageSubscriptions: window.aiPanel._messageUnsubscribes.length,
      ownedTimeouts: window.aiPanel._ownedTimeouts?.size || 0,
      ownedRafs: window.aiPanel._ownedAnimationFrames?.size || 0,
    };
  });
  for (let index = 0; index < 12; index += 1) {
    await page.locator('.nav-btn[data-view="flow"]').click();
    await page.locator('.nav-btn[data-view="ai"]').click();
  }
  for (let index = 0; index < 10; index += 1) {
    await page.locator('#ai-btn-apply').click();
    await page.locator('.ai-apply-preview-cancel').click();
    await page.waitForSelector('.ai-apply-preview-overlay', { state: 'detached' });
  }
  const after = await page.evaluate(() => ({
    samePanel: window.aiPanel === window.__cvSmokePanelIdentity,
    messageSubscriptions: window.aiPanel._messageUnsubscribes.length,
    ownedTimeouts: window.aiPanel._ownedTimeouts?.size || 0,
    ownedRafs: window.aiPanel._ownedAnimationFrames?.size || 0,
    activeTransport: Boolean(window.aiPanel.activeAgentRunTransport),
    activePreview: Boolean(window.aiPanel._activeApplyPreview),
    overlays: document.querySelectorAll('.ai-apply-preview-overlay').length,
  }));
  assert(after.samePanel, 'View switching replaced the AiPanel instance.');
  assert(after.messageSubscriptions === before.messageSubscriptions, 'WebMessage subscriptions accumulated.');
  assert(after.ownedTimeouts <= before.ownedTimeouts + 1, 'Owned timeouts accumulated linearly.');
  assert(after.ownedRafs <= before.ownedRafs + 1, 'Owned RAF callbacks accumulated linearly.');
  assert(!after.activeTransport && !after.activePreview && after.overlays === 0, 'Repeated operations left active resources.');
  return { before, after };
}

async function verifyHostWebMessage(page) {
  const requested = await page.evaluate(() => {
    const panel = window.aiPanel;
    const probeSessionId = `webview2-identity-probe-${Date.now()}`;
    const original = panel._handleGetAiSessionResult;
    window.__cvWebMessageIdentityProbe = { response: null, original };
    panel._handleGetAiSessionResult = function probeHandler(data) {
      const payload = data?.payload || data || {};
      window.__cvWebMessageIdentityProbe.response = {
        sessionId: String(payload.sessionId ?? payload.SessionId ?? ''),
        requestId: String(payload.requestId ?? payload.RequestId ?? ''),
        navigationEpoch: Number(payload.navigationEpoch ?? payload.NavigationEpoch ?? -1),
        success: payload.success === true,
      };
      return original.call(this, data);
    };
    panel.sessionNavigationEpoch = Number(panel.sessionNavigationEpoch || 0) + 1;
    panel._requestSessionLoad(probeSessionId, 'webview2_identity_probe');
    return {
      sessionId: probeSessionId,
      requestId: panel.pendingSessionLoad?.requestId || '',
      navigationEpoch: Number(panel.pendingSessionLoad?.epoch ?? -1),
      bridgeAvailable: typeof window.chrome?.webview?.postMessage === 'function',
    };
  });
  await page.waitForFunction(() => Boolean(window.__cvWebMessageIdentityProbe?.response));
  const completed = await page.evaluate(() => {
    const panel = window.aiPanel;
    const probe = window.__cvWebMessageIdentityProbe;
    const response = probe.response;
    panel._handleGetAiSessionResult = probe.original;
    delete window.__cvWebMessageIdentityProbe;
    return { response, pendingSessionLoad: Boolean(panel.pendingSessionLoad) };
  });
  assert(requested.bridgeAvailable, 'WebView2 postMessage bridge is unavailable.');
  assert(requested.requestId, 'Host identity probe did not allocate a requestId.');
  assert(completed.response.sessionId === requested.sessionId, 'Host response sessionId did not match the request.');
  assert(completed.response.requestId === requested.requestId, 'Host response requestId did not match the request.');
  assert(completed.response.navigationEpoch === requested.navigationEpoch, 'Host response navigation identity did not match the request.');
  assert(!completed.pendingSessionLoad, 'Host response did not finish the pending session load.');
  return { requested, ...completed };
}

async function installCloseFlushProbe(page) {
  await page.evaluate(marker => {
    const original = window.__clearVisionFlushAiPanelWorkspace;
    window.__clearVisionFlushAiPanelWorkspace = async reason => {
      const started = { called: true, completed: false, reason: String(reason || ''), startedAt: Date.now() };
      localStorage.setItem(marker, JSON.stringify(started));
      try {
        const value = typeof original === 'function' ? await original(reason) : true;
        localStorage.setItem(marker, JSON.stringify({ ...started, completed: true, succeeded: value === true, completedAt: Date.now() }));
        return value === true;
      } catch (error) {
        localStorage.setItem(marker, JSON.stringify({ ...started, completed: true, succeeded: false, error: String(error?.message || error), completedAt: Date.now() }));
        throw error;
      }
    };
  }, closeFlushMarker);
}

async function verifyReopen(page, preNavigationState) {
  const marker = await page.evaluate(key => {
    try { return JSON.parse(localStorage.getItem(key) || 'null'); } catch { return null; }
  }, closeFlushMarker);
  const safe = await page.evaluate(() => ({
    disposed: window.aiPanel?._disposed,
    applyInFlight: Boolean(window.aiPanel?._applyInFlight),
    activePreview: Boolean(window.aiPanel?._activeApplyPreview),
    activeTransport: Boolean(window.aiPanel?.activeAgentRunTransport),
    overlays: document.querySelectorAll('.ai-apply-preview-overlay').length,
  }));
  assert(marker?.called && marker?.completed && marker?.reason === 'host_close', 'Host close did not complete the workspace flush handshake.');
  const preNavigationSafetyMarker = preNavigationState?.safetyMarker;
  const recoveryFixture = preNavigationState?.recoveryFixture;
  assert(preNavigationSafetyMarker?.reason === 'apply_rollback_failed',
    'Rollback safety marker did not survive the real process restart.');
  assert(recoveryFixture?.version === 1 && recoveryFixture?.sessionId,
    'Rollback recovery fixture did not survive the real process restart.');
  assert(!safe.disposed && !safe.applyInFlight && !safe.activePreview && !safe.activeTransport && safe.overlays === 0,
    'Reopened AI page restored a temporary in-flight resource.');
  const restored = await page.evaluate(({ fixture, recoveryKey }) => {
    const panel = window.aiPanel;
    const canvas = window.flowCanvasAdapter || window.flowCanvas;
    const canvasBeforeSessionRestore = canvas.serialize();
    panel.sessionNavigationEpoch = Number(panel.sessionNavigationEpoch || 0) + 1;
    const identity = {
      sessionId: fixture.sessionId,
      requestId: `webview2-reopen-${Date.now()}`,
      epoch: panel.sessionNavigationEpoch,
    };
    panel.pendingSessionLoad = { ...identity, source: 'webview2_reopen_probe', timeoutId: null };
    panel._handleGetAiSessionResult({
      success: true,
      sessionId: identity.sessionId,
      requestId: identity.requestId,
      navigationEpoch: identity.epoch,
      session: fixture.session,
    });
    const value = {
      identity,
      pendingSessionLoad: Boolean(panel.pendingSessionLoad),
      reason: panel._applySafetyBlockReason,
      applyDisabled: panel.container.querySelector('#ai-btn-apply')?.disabled === true,
      applyButtonText: panel.container.querySelector('#ai-btn-apply')?.textContent?.trim() || '',
      statusNote: panel.container.querySelector('#ai-result-status-note')?.textContent?.trim() || '',
      marker: panel._readPersistedApplySafetyBlock?.() || null,
      canvasBeforeSessionRestore,
      canvasAfterSessionRestore: canvas.serialize(),
      workspaceLifecycle: panel._buildWorkspaceSnapshotDelta().lifecycleState,
      workbenchState: panel.workbenchState,
    };
    canvas.deserialize(fixture.beforeCanvas);
    panel._clearApplySafetyBlock?.({ clearPersisted: true });
    localStorage.removeItem(recoveryKey);
    panel._saveSessionId?.(null);
    return value;
  }, { fixture: recoveryFixture, recoveryKey: rollbackRecoveryFixtureKey });
  const signature = value => JSON.stringify(value || null);
  assert(!restored.pendingSessionLoad, 'Production session restore did not finish its pending identity.');
  assert(restored.reason === 'apply_rollback_failed' && restored.applyDisabled,
    'Same-session same-result restore did not re-establish the safety block.');
  assert(restored.marker?.reason === 'apply_rollback_failed', 'Reopened session lost its persisted safety marker.');
  assert(restored.workspaceLifecycle !== 'applied' && restored.workbenchState === 'failed',
    'Rollback failure was restored as a successful Applied state.');
  assert(/安全恢复/.test(`${restored.applyButtonText} ${restored.statusNote}`),
    'Reopened UI did not explain that explicit safety recovery is required.');
  assert(signature(restored.canvasBeforeSessionRestore) === signature(recoveryFixture.beforeCanvas),
    'Desktop restart preserved the temporary partial-write canvas instead of the safe baseline.');
  return {
    marker,
    safe,
    preNavigationSafetyMarker: {
      reason: preNavigationSafetyMarker.reason,
      sessionId: preNavigationSafetyMarker.sessionId,
      fingerprintLength: String(preNavigationSafetyMarker.fingerprint || '').length,
      recordedAtUtc: preNavigationSafetyMarker.recordedAtUtc,
    },
    restored,
    canvasRecovery: {
      safeBaselineRestored: signature(restored.canvasBeforeSessionRestore) === signature(recoveryFixture.beforeCanvas),
      partialCanvasWasDifferent: signature(recoveryFixture.failedCanvas) !== signature(recoveryFixture.beforeCanvas),
    },
  };
}

function colorMaxChannel(value) {
  const match = String(value || '').match(/rgba?\(([^)]+)\)/);
  assert(match, `Expected RGB color, received '${value}'.`);
  return Math.max(...match[1].split(',').slice(0, 3).map(part => Number.parseFloat(part.trim())));
}

async function captureFlowCanvasThemes(page) {
  const preserved = await page.evaluate(() => {
    const flow = window.flowCanvas;
    return {
      theme: document.documentElement.dataset.theme || 'dark',
      flow: flow.serialize(),
      selectedNode: flow.selectedNode,
      selectedConnectionId: flow.selectedConnection?.id || null,
    };
  });

  await activateView(page, 'flow');
  await page.waitForSelector('#flow-canvas', { state: 'visible' });
  await page.evaluate(flowFixture => {
    const flow = window.flowCanvas;
    flow.deserialize(flowFixture);
    flow.selectedNode = 'smoke-output';
    flow.selectedConnection = null;
    flow.markSelectionChanged?.('webview2-flow-theme');
    flow.render();
  }, buildFlow);

  const capture = async theme => {
    await page.evaluate(selectedTheme => {
      document.documentElement.dataset.theme = selectedTheme;
    }, theme);
    await page.waitForFunction(() => {
      const token = getComputedStyle(document.documentElement).getPropertyValue('--flow-canvas-grid').trim();
      return window.flowCanvas?.themePalette?.grid === token;
    });
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));

    const layout = await page.evaluate(() => {
      const styles = selector => {
        const element = document.querySelector(selector);
        const computed = getComputedStyle(element);
        return { background: computed.backgroundColor, border: computed.borderColor };
      };
      const html = document.documentElement;
      const shell = document.querySelector('.flow-editor-shell');
      return {
        theme: html.dataset.theme,
        canvas: styles('#flow-canvas'),
        workspace: styles('.flow-editor-shell .workspace'),
        minimap: styles('.flow-minimap'),
        imageViewer: styles('.image-viewer-container'),
        palette: { ...window.flowCanvas.themePalette },
        overflow: {
          document: html.scrollWidth - html.clientWidth,
          body: document.body.scrollWidth - document.body.clientWidth,
          shell: shell.scrollWidth - shell.clientWidth,
        },
      };
    });
    await page.locator('.flow-editor-shell').screenshot({
      path: path.join(evidenceDir, `flow-canvas-dpi-${safeFileName(scale)}-${theme}.png`),
    });
    assert(layout.overflow.document <= 1 && layout.overflow.body <= 1,
      `Flow canvas overflow at DPI ${scale} ${theme}: ${JSON.stringify(layout.overflow)}`);
    assert(colorMaxChannel(layout.imageViewer.background) < 50,
      `Image viewer lost its dark inspection surface at DPI ${scale} ${theme}.`);
    return layout;
  };

  const light = await capture('light');
  const dark = await capture('dark');
  assert(colorMaxChannel(light.canvas.background) > 180 && colorMaxChannel(light.canvas.background) < 250,
    `Light FlowCanvas is not a restrained light engineering surface: ${light.canvas.background}`);
  assert(colorMaxChannel(dark.canvas.background) < 60,
    `Dark FlowCanvas is no longer dark: ${dark.canvas.background}`);
  assert(light.canvas.background !== dark.canvas.background,
    'Light and dark FlowCanvas backgrounds unexpectedly match.');
  assert(light.palette.grid !== dark.palette.grid && light.palette.nodeBackgroundStart !== dark.palette.nodeBackgroundStart,
    'FlowCanvas drawing palette did not refresh across the theme switch.');

  await page.evaluate(saved => {
    const flow = window.flowCanvas;
    document.documentElement.dataset.theme = saved.theme;
    flow.deserialize(saved.flow);
    flow.selectedNode = saved.selectedNode;
    flow.selectedConnection = saved.selectedConnectionId
      ? flow.connections.find(item => item.id === saved.selectedConnectionId) || null
      : null;
    flow.markSelectionChanged?.('webview2-flow-theme-restore');
    flow.render();
  }, preserved);
  await activateView(page, 'ai');
  return { light, dark };
}

async function main() {
  fs.mkdirSync(evidenceDir, { recursive: true });
  const browser = await connect();
  try {
    const context = browser.contexts()[0];
    const page = context.pages()[0];
    const preNavigationState = await authenticateAndOpenAi(page);
    if (phase !== 'reopen') {
      await resetSmokeConversation(page, { clearSafety: phase === 'full' });
    }
    const base = await page.evaluate(() => ({
      url: location.href,
      title: document.title,
      hostKind: window.__CLEARVISION_STARTUP__?.hostKind,
      aiPanelClass: window.aiPanel?.constructor?.name,
      aiPanelScript: [...performance.getEntriesByType('resource')]
        .map(item => item.name)
        .find(name => name.includes('/features/ai/aiPanel.js')) || '',
      chromeWebview: Boolean(window.chrome?.webview),
      activeView: document.querySelector('.nav-btn.active')?.dataset.view,
    }));
    assert(base.hostKind === 'desktop-webview2', 'Page is not running in the Desktop WebView2 host.');
    assert(base.aiPanelClass === 'AiPanel', 'Desktop did not load the formal AiPanel path.');
    assert(base.aiPanelScript.includes('/features/ai/aiPanel.js'), 'Formal aiPanel.js was not loaded.');
    assert(base.chromeWebview && base.activeView === 'ai', 'WebView bridge or AI view is unavailable.');

    const result = { phase, scale, base, timestampUtc: new Date().toISOString() };
    result.layouts = [
      await captureLayout(page, `dpi-${scale}-light`, 'light'),
      await captureLayout(page, `dpi-${scale}-dark`, 'dark'),
    ];
    result.flowCanvasThemes = await captureFlowCanvasThemes(page);
    if (phase !== 'reopen') {
      result.formalProductLayouts = await captureFormalProductLayouts(page);
    }
    if (phase === 'full') {
      result.ime = await verifyImeComposition(page);
      result.hostWebMessage = await verifyHostWebMessage(page);
      await seedReadyBuild(page);
      result.dialog = await verifyDialog(page);
      result.lifecycle = await verifyLifecycleRepetition(page);
      result.canvas = await verifyRealCanvasApplyAndRollback(page);
      result.stateConsistency = await page.evaluate(() => ({
        workbenchState: window.aiPanel.workbenchState,
        buildState: document.querySelector('#ai-build-workspace')?.dataset.aiBuildState || '',
        status: document.querySelector('#ai-build-status-summary')?.textContent?.trim() || '',
        actionQueue: document.querySelector('#ai-build-action-queue')?.textContent?.trim() || '',
        applyDisabled: document.querySelector('#ai-btn-apply')?.disabled,
        snapshotLifecycle: window.aiPanel._buildWorkspaceSnapshotDelta().lifecycleState,
      }));
      result.transport = await verifyReplayAndTransportReplacement(page);
      result.rollbackFailurePersistence = await armRollbackFailurePersistence(page);
      await installCloseFlushProbe(page);
    } else if (phase === 'reopen') {
      result.reopen = await verifyReopen(page, preNavigationState);
    }
    const output = path.join(evidenceDir, `webview2-${safeFileName(phase)}-dpi-${scale}.json`);
    fs.writeFileSync(output, `${JSON.stringify(result, null, 2)}\n`, 'utf8');
    console.log(JSON.stringify({ ok: true, output, result }, null, 2));
  } finally {
    await browser.close();
  }
}

main().catch(error => {
  console.error(error?.stack || error);
  process.exitCode = 1;
});
