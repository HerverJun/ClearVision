import test from 'node:test';
import assert from 'node:assert/strict';
import httpClient from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js';
import { AgentRunEventTransport } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelAgentRun.js';
import { normalizeWorkspaceSnapshotForRestore } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelSnapshotRecovery.js';
import { aiPanelLifecycleMixin } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelLifecycle.js';
import { deriveAiBuildPresentation } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelBuildPresentation.js';

function installWindow() {
  const storage = new Map();
  global.window = {
    location: { protocol: 'http:', hostname: '127.0.0.1', port: '5000', origin: 'http://127.0.0.1:5000' },
    setTimeout,
    clearTimeout,
    requestAnimationFrame(callback) { callback(); return 1; },
    localStorage: {
      getItem(key) { return storage.has(key) ? storage.get(key) : null; },
      setItem(key, value) { storage.set(key, String(value)); },
      removeItem(key) { storage.delete(key); },
    },
  };
}

function transportPanel(runId = 'run-1') {
  const handled = [];
  const panel = {
    _disposed: false,
    activeAgentRunTransport: null,
    _setAgentRunTransportStatus() {},
    _isAgentRunTerminalSeen() { return false; },
    _normalizeAgentRunEvent(event) { return event; },
    _handleAgentRunEvent(event) { handled.push(event); },
    _buildAgentRunTerminalEventFromSummary() { return null; },
  };
  const transport = new AgentRunEventTransport(panel, runId);
  panel.activeAgentRunTransport = transport;
  return { panel, transport, handled };
}

test('AgentRun replay delay settles immediately when transport closes', async () => {
  installWindow();
  const { transport } = transportPanel();
  const waiting = transport._delay(60_000);
  transport.close();
  await Promise.race([
    waiting,
    new Promise((_, reject) => setTimeout(() => reject(new Error('delay remained pending')), 50)),
  ]);
  assert.equal(transport.pendingDelayResolve, null);
});

test('AgentRun EventSource wait settles when transport closes', async () => {
  installWindow();
  class FakeEventSource {
    addEventListener() {}
    close() { this.closed = true; }
  }
  window.EventSource = FakeEventSource;
  const { transport } = transportPanel();
  transport._ensureStreamToken = async () => 'token';
  const waiting = transport._startEventSource();
  await Promise.resolve();
  transport.close();
  const result = await Promise.race([
    waiting,
    new Promise((_, reject) => setTimeout(() => reject(new Error('EventSource remained pending')), 50)),
  ]);
  assert.equal(result, false);
});

test('AgentRun replay ignores duplicate sequence values in one replay payload', async () => {
  installWindow();
  const originalGet = httpClient.get;
  const { transport, handled } = transportPanel();
  try {
    httpClient.get = async () => ({
      events: [
        { runId: 'run-1', sequence: 1, eventType: 'stage.started' },
        { runId: 'run-1', sequence: 1, eventType: 'stage.started' },
        { runId: 'run-1', sequence: 2, eventType: 'stage.completed' },
      ],
    });
    await transport._replayRecentEvents();
    assert.deepEqual(handled.map(event => event.sequence), [1, 2]);
  } finally {
    httpClient.get = originalGet;
    transport.close();
  }
});

test('late AgentRun replay response cannot update a closed transport', async () => {
  installWindow();
  const originalGet = httpClient.get;
  const { transport, handled } = transportPanel();
  let resolveReplay;
  try {
    httpClient.get = () => new Promise(resolve => { resolveReplay = resolve; });
    const replaying = transport._replayRecentEvents();
    transport.close();
    resolveReplay({ events: [{ runId: 'run-1', sequence: 1, eventType: 'run.completed' }] });
    await replaying;
    assert.deepEqual(handled, []);
  } finally {
    httpClient.get = originalGet;
  }
});

test('supported workspace snapshot preserves safe Plan and run metadata', () => {
  const result = normalizeWorkspaceSnapshotForRestore({
    schemaVersion: 2,
    revision: 7,
    lifecycleState: 'plan_ready',
    workspaceViewMode: 'plan',
    pendingPlanSnapshot: { planId: 'plan-a', planHash: 'sha256:a', canBuild: true },
    readinessPreview: { canBuild: true },
    planRunId: 'plan-run-a',
    planRunStatus: 'completed',
  });
  assert.equal(result.trusted, true);
  assert.equal(result.snapshot.revision, 7);
  assert.equal(result.snapshot.planRunId, 'plan-run-a');
});

for (const [name, previewOverride] of [
  ['plan id', { planId: 'plan-other' }],
  ['plan hash', { planHash: 'sha256:other' }],
  ['requirement mode', { requirementMode: 'strict' }],
  ['answer revision', { answerRevision: 8 }],
  ['resource revision', { resourceRevision: 6 }],
]) {
  test(`workspace restore marks readiness stale when ${name} differs`, () => {
    const result = normalizeWorkspaceSnapshotForRestore({
      schemaVersion: 2,
      revision: 7,
      lifecycleState: 'plan_ready',
      requirementMode: 'draft',
      answerRevision: 7,
      resourceRevision: 5,
      pendingPlanSnapshot: { planId: 'plan-a', planHash: 'sha256:a' },
      readinessPreview: {
        planId: 'plan-a',
        planHash: 'sha256:a',
        requirementMode: 'draft',
        answerRevision: 7,
        resourceRevision: 5,
        buildReadiness: { canBuild: true },
        ...previewOverride,
      },
    });
    assert.equal(result.trusted, true);
    assert.equal(result.snapshot.readinessPreview, null);
    assert.equal(result.snapshot.readinessStale, true);
  });
}

test('workspace restore marks missing readiness stale while a current plan is pending', () => {
  const result = normalizeWorkspaceSnapshotForRestore({
    schemaVersion: 2,
    revision: 8,
    lifecycleState: 'plan_blocked',
    requirementMode: 'draft',
    answerRevision: 7,
    resourceRevision: 5,
    pendingPlanSnapshot: {
      planId: 'plan-mode-transition',
      planHash: 'sha256:mode-transition',
      buildReadiness: {
        canBuild: false,
        requirementMode: 'strict',
      },
    },
    readinessPreview: null,
  });

  assert.equal(result.trusted, true);
  assert.equal(result.snapshot.readinessPreview, null);
  assert.equal(result.snapshot.readinessStale, true);
});

test('workspace restore discards a matching but double-encoded derived readiness resource', () => {
  const polluted = {
    canonicalId: 'resource:v1|resource|resourcev1resourceglobalresource|resource',
    resourceType: 'resource',
    operatorKey: 'resourcev1resourceglobalresource',
    parameterName: 'resource',
  };
  const result = normalizeWorkspaceSnapshotForRestore({
    schemaVersion: 2,
    revision: 9,
    lifecycleState: 'plan_blocked',
    requirementMode: 'draft',
    answerRevision: 7,
    resourceRevision: 5,
    pendingPlanSnapshot: { planId: 'plan-polluted', planHash: 'sha256:polluted' },
    readinessPreview: {
      planId: 'plan-polluted',
      planHash: 'sha256:polluted',
      requirementMode: 'draft',
      answerRevision: 7,
      resourceRevision: 5,
      buildReadiness: { canBuild: false, missingResources: [polluted] },
    },
    missingResources: [polluted],
  });

  assert.equal(result.trusted, true);
  assert.equal(result.snapshot.readinessPreview, null);
  assert.equal(result.snapshot.readinessStale, true);
  assert.deepEqual(result.snapshot.missingResources, []);
});

for (const [name, snapshot] of [
  ['missing version', { lifecycleState: 'applied', readinessPreview: { canBuild: true }, buildRunId: 'build-a' }],
  ['future version', { schemaVersion: 99, lifecycleState: 'building', buildRunId: 'build-a' }],
  ['invalid lifecycle', { schemaVersion: 2, lifecycleState: 'teleported', buildRunId: 'build-a' }],
  ['damaged plan', { schemaVersion: 2, lifecycleState: 'plan_ready', pendingPlanSnapshot: { canBuild: true } }],
]) {
  test(`unsafe workspace snapshot (${name}) cannot restore build or readiness authority`, () => {
    const result = normalizeWorkspaceSnapshotForRestore(snapshot);
    assert.equal(result.trusted, false);
    assert.equal(result.snapshot.lifecycleState, 'idle');
    assert.equal(result.snapshot.readinessPreview, null);
    assert.equal(result.snapshot.buildRunId, '');
    assert.equal(result.snapshot.submittedBuildFingerprint, '');
    assert.equal(result.snapshot.workspaceViewMode, 'plan');
  });
}

test('supported Applied snapshot is restored as Build pending revalidation', () => {
  const result = normalizeWorkspaceSnapshotForRestore({
    schemaVersion: 2,
    lifecycleState: 'applied',
    pendingPlanSnapshot: { planId: 'plan-a', planHash: 'sha256:a' },
    buildRunId: 'build-a',
    buildRunStatus: 'completed',
  });
  assert.equal(result.trusted, true);
  assert.equal(result.snapshot.appliedDowngraded, true);
  assert.equal(result.snapshot.lifecycleState, 'build');
  assert.equal(result.snapshot.readinessPreview, null);
  assert.equal(result.snapshot.buildRunId, '');
  assert.equal(result.snapshot.buildRunStatus, 'idle');
  assert.equal(result.snapshot.submittedBuildFingerprint, '');
});

test('session navigation identity rejects disposal, session changes, and epoch changes', async () => {
  installWindow();
  const { AiPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js');
  const panel = Object.create(AiPanel.prototype);
  panel.sessionId = 'session-a';
  panel.sessionNavigationEpoch = 3;
  panel._lifecycleEpoch = 4;
  panel._disposed = false;
  const identity = panel._captureSessionNavigationIdentity();
  assert.equal(panel._isSessionNavigationIdentityCurrent(identity), true);
  panel.sessionNavigationEpoch = 4;
  assert.equal(panel._isSessionNavigationIdentityCurrent(identity), false);
  panel.sessionNavigationEpoch = 3;
  panel.sessionId = 'session-b';
  assert.equal(panel._isSessionNavigationIdentityCurrent(identity), false);
  panel.sessionId = 'session-a';
  panel._disposed = true;
  assert.equal(panel._isSessionNavigationIdentityCurrent(identity), false);
});

test('AiPanel dispose is idempotent and cancels owned late callbacks', async () => {
  installWindow();
  let lateEffects = 0;
  let transportCloses = 0;
  const panel = {
    ...aiPanelLifecycleMixin,
    _disposed: false,
    _lifecycleEpoch: 1,
    sessionNavigationEpoch: 2,
    pendingSessionLoad: null,
    _messageUnsubscribes: [],
    _closeAllAgentTransports() { transportCloses += 1; },
    container: { innerHTML: '', querySelector() { return null; }, removeEventListener() {} },
    operatorMetadataLoading: new Map(),
  };
  panel._setOwnedTimeout(() => { lateEffects += 1; }, 25);
  panel.dispose();
  panel.dispose();
  await new Promise(resolve => setTimeout(resolve, 40));
  assert.equal(lateEffects, 0);
  assert.equal(transportCloses, 1);
  assert.equal(panel._lifecycleEpoch, 2);
  assert.equal(panel.sessionNavigationEpoch, 3);
});

test('Apply execution is single-flight even when invoked repeatedly', async () => {
  installWindow();
  global.document = { querySelector() { return null; } };
  const { AiPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js');
  const panel = Object.create(AiPanel.prototype);
  panel._disposed = false;
  panel._applyInFlight = true;
  let deserializeCount = 0;
  panel.flowCanvas = { deserialize() { deserializeCount += 1; } };
  assert.equal(panel._executeApplyFlow({ operators: [{ id: 'a' }] }), false);
  assert.equal(deserializeCount, 0);
});

test('Apply preview identity blocks canvas and Result drift', async () => {
  installWindow();
  const { AiPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js');
  const flow = { operators: [{ id: 'a' }], connections: [] };
  const panel = Object.create(AiPanel.prototype);
  panel._disposed = false;
  panel.currentResultVersion = 2;
  panel.currentCanvasRevision = 5;
  panel._applySafetyBlockReason = '';
  panel.currentResult = { flow, applyGate: { canvasApplyReady: true, blocked: false } };
  panel.flowCanvas = { getFlowRevision() { return 5; } };
  panel._buildFlowWithPendingDrafts = value => value;
  panel._getResultFlowForCanvas = value => value.flow;
  const identity = panel._createApplyPreviewIdentity(flow);
  assert.equal(panel._isApplyPreviewIdentityCurrent(identity, flow), true);
  panel.currentResultVersion = 3;
  assert.equal(panel._isApplyPreviewIdentityCurrent(identity, flow), false);
  panel.currentResultVersion = 2;
  panel.flowCanvas.getFlowRevision = () => 6;
  assert.equal(panel._isApplyPreviewIdentityCurrent(identity, flow), false);
  panel.flowCanvas.getFlowRevision = () => 5;
  panel.currentResult.applyGate.blocked = true;
  assert.equal(panel._isApplyPreviewIdentityCurrent(identity, flow), false);
});

test('Apply failure restores the pre-apply snapshot and never enters Applied', async () => {
  installWindow();
  global.document = { querySelector() { return null; } };
  const { AiPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js');
  const before = { operators: [{ id: 'before' }], connections: [] };
  const incoming = { operators: [{ id: 'after' }], connections: [] };
  let current = before;
  const states = [];
  const panel = Object.create(AiPanel.prototype);
  Object.assign(panel, {
    _disposed: false,
    _applyInFlight: false,
    currentResult: { flow: incoming, Flow: incoming },
    currentResultVersion: 1,
    container: { querySelector() { return null; } },
    flowCanvas: {
      serialize() { return current; },
      deserialize(flow) { current = flow; },
      getFlowRevision() { return 1; },
    },
    options: { onApplied() { throw new Error('fault injection'); } },
    _markCurrentResultAppliedToCanvas() {},
    _captureAppliedCanvasBaseline() {},
    _syncCanvasManualEditRecords() {},
    _syncPendingParameterDrafts() {},
    _renderFollowupChecklist() {},
    _renderParameterDraftEditor() {},
    _renderAgentWorkspaceOverview() {},
    _renderBuildWorkspaceFromAgentRun() {},
    _setResultStatusNote() {},
    _addMessage() {},
    _updateApplyButtonState() {},
    _setWorkbenchState(state) { states.push(state); },
  });
  const result = panel._executeApplyFlow(incoming);
  assert.equal(result, false);
  assert.equal(current, before);
  assert.equal(states.includes('applied'), false);
  assert.equal(states.at(-1), 'ready_to_apply');
});

test('Apply rollback failure is exposed as failed instead of Ready', async () => {
  installWindow();
  global.document = { querySelector() { return null; } };
  const { AiPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js');
  const before = { operators: [{ id: 'before' }], connections: [] };
  let deserializeCalls = 0;
  const states = [];
  const panel = Object.create(AiPanel.prototype);
  Object.assign(panel, {
    _disposed: false,
    _applyInFlight: false,
    currentResult: { flow: { operators: [{ id: 'after' }] } },
    flowCanvas: {
      serialize() { return before; },
      deserialize() {
        deserializeCalls += 1;
        if (deserializeCalls > 1) throw new Error('rollback failed');
      },
      getFlowRevision() { return 1; },
    },
    options: { onApplied() { throw new Error('apply failed'); } },
    container: { querySelector() { return null; } },
    _extractOperators() { return [{ id: 'after' }]; },
    _markCurrentResultAppliedToCanvas() {},
    _syncPendingParameterDrafts() {},
    _renderFollowupChecklist() {},
    _renderParameterDraftEditor() {},
    _setResultStatusNote() {},
    _addMessage() {},
    _updateApplyButtonState() {},
    _setWorkbenchState(state) { states.push(state); },
  });
  panel._executeApplyFlow({ operators: [{ id: 'after' }] });
  assert.equal(states.at(-1), 'failed');
  assert.notEqual(states.at(-1), 'ready_to_apply');
  assert.equal(panel._applySafetyBlockReason, 'apply_rollback_failed');
});

test('Apply detects partial canvas writes and restores the original snapshot', async () => {
  installWindow();
  global.document = { querySelector() { return null; } };
  const { AiPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js');
  const before = { operators: [{ id: 'before' }], connections: [] };
  const incoming = { operators: [{ id: 'a' }, { id: 'b' }], connections: [{ id: 'a-b' }] };
  let current = before;
  let firstWrite = true;
  const states = [];
  const panel = Object.create(AiPanel.prototype);
  Object.assign(panel, {
    _disposed: false,
    _applyInFlight: false,
    currentResult: { flow: incoming },
    flowCanvas: {
      serialize() { return current; },
      deserialize(flow) {
        if (firstWrite) {
          firstWrite = false;
          current = { operators: flow.operators.slice(0, 1), connections: [] };
        } else {
          current = flow;
        }
      },
      getFlowRevision() { return 1; },
    },
    options: {},
    container: { querySelector() { return null; } },
    _setResultStatusNote() {},
    _addMessage() {},
    _updateApplyButtonState() {},
    _setWorkbenchState(state) { states.push(state); },
  });
  assert.equal(panel._executeApplyFlow(incoming), false);
  assert.equal(current, before);
  assert.equal(states.at(-1), 'ready_to_apply');
});

test('local Apply safety block keeps a legacy Ready gate disabled until a new result arrives', async () => {
  installWindow();
  const { AiPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js');
  const button = {
    disabled: false,
    innerHTML: '',
    classList: { toggle() {} },
    setAttribute() {},
  };
  const panel = Object.create(AiPanel.prototype);
  Object.assign(panel, {
    container: { querySelector(selector) { return selector === '#ai-btn-apply' ? button : null; } },
    currentResult: {
      flow: { operators: [{ id: 'a' }], connections: [] },
      applyGate: { canvasApplyReady: true, blocked: false }
    },
    currentResultVersion: 2,
    appliedResultVersion: 0,
    isGenerating: false,
    _applyInFlight: false,
    _activeApplyPreview: null,
    _applySafetyBlockReason: 'apply_rollback_failed',
    _escapeHtml: value => String(value),
  });
  panel._updateApplyButtonState();
  assert.equal(button.disabled, true);
  assert.match(button.innerHTML, /需安全恢复后才能应用/);

  panel._setCurrentResult({
    flow: { operators: [{ id: 'b' }], connections: [] },
    applyGate: { canvasApplyReady: true, blocked: false }
  });
  assert.equal(panel._applySafetyBlockReason, '');
  assert.equal(button.disabled, false);
});

test('Apply rollback safety block survives panel recreation only for the same session and result', async () => {
  installWindow();
  const { AiPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js');
  const result = {
    flow: { operators: [{ id: 'a' }], connections: [] },
    applyGate: { canvasApplyReady: true, blocked: false },
    buildResult: { buildId: 'build-a' },
  };
  const failedPanel = Object.create(AiPanel.prototype);
  Object.assign(failedPanel, { sessionId: 'session-a', currentResult: result, _applySafetyBlockReason: '' });
  assert.equal(failedPanel._persistApplySafetyBlock('apply_rollback_failed', result), true);

  const reopenedPanel = Object.create(AiPanel.prototype);
  Object.assign(reopenedPanel, { sessionId: 'session-a', _applySafetyBlockReason: '' });
  assert.equal(reopenedPanel._restorePersistedApplySafetyBlock(result), 'apply_rollback_failed');

  const replacement = {
    ...result,
    flow: { operators: [{ id: 'b' }], connections: [] },
    buildResult: { buildId: 'build-b' },
  };
  assert.equal(reopenedPanel._restorePersistedApplySafetyBlock(replacement), '');
  assert.equal(reopenedPanel._readPersistedApplySafetyBlock(), null);

  failedPanel._persistApplySafetyBlock('apply_rollback_failed', result);
  const otherSession = Object.create(AiPanel.prototype);
  Object.assign(otherSession, { sessionId: 'session-b', _applySafetyBlockReason: '' });
  assert.equal(otherSession._restorePersistedApplySafetyBlock(replacement), '');
  assert.equal(failedPanel._restorePersistedApplySafetyBlock(result), 'apply_rollback_failed');
});

test('Build release state matrix keeps canonical presentation, blockers, and Apply eligibility aligned', () => {
  const flow = { operators: [{ id: 'a', type: 'ImageAcquisition' }], connections: [] };
  const passedValidation = {
    structuralValidation: { passed: true, status: 'passed' },
    dryRun: { succeeded: true, status: 'completed' },
  };
  const readyGate = { canvasApplyReady: true, blocked: false, status: 'ready' };
  const baseResult = { flow, pendingParameters: [], missingResources: [], validationPreview: passedValidation, applyGate: readyGate };
  const cases = [
    { name: 'build running', workbenchState: 'generating', activeAgentRunId: 'run-a', currentResult: baseResult, expected: 'building' },
    { name: 'validation running', workbenchState: 'validating', activeAgentRunId: 'run-a', currentResult: baseResult, expected: 'validating' },
    { name: 'parameter blocked', workbenchState: 'reviewing_parameters', currentResult: { ...baseResult, pendingParameters: [{ operatorId: 'a', parameterNames: ['Threshold'] }] }, expected: 'needs_input', parameterGroups: true },
    { name: 'resource blocked', workbenchState: 'reviewing_resources', currentResult: { ...baseResult, missingResources: [{ operatorId: 'a', parameterName: 'ModelPath', resourceKey: 'a.ModelPath' }] }, expected: 'needs_input' },
    { name: 'static validation failed', workbenchState: 'failed', currentResult: { ...baseResult, validationPreview: { ...passedValidation, structuralValidation: { passed: false, status: 'failed' } }, applyGate: { canvasApplyReady: false, blocked: true } }, expected: 'validation_failed' },
    { name: 'dry run failed', workbenchState: 'failed', currentResult: { ...baseResult, validationPreview: { ...passedValidation, dryRun: { succeeded: false, status: 'failed' } }, applyGate: { canvasApplyReady: false, blocked: true } }, expected: 'validation_failed' },
    { name: 'gate blocked', workbenchState: 'ready_to_apply', currentResult: { ...baseResult, applyGate: { canvasApplyReady: false, blocked: true, status: 'blocked' } }, expected: 'gate_blocked' },
    { name: 'apply ready', workbenchState: 'ready_to_apply', currentResult: baseResult, expected: 'ready_to_apply', canApply: true },
    { name: 'applying', workbenchState: 'applying', currentResult: baseResult, expected: 'applying' },
    { name: 'failed without result', workbenchState: 'failed', currentResult: {}, expected: 'failed' },
    { name: 'applied', workbenchState: 'applied', currentResult: baseResult, expected: 'applied', applied: true },
  ];

  for (const item of cases) {
    const panel = {
      workbenchState: item.workbenchState,
      activeAgentRunId: item.activeAgentRunId || '',
      activeAgentRunEvents: [],
      currentResult: item.currentResult,
      pendingVisionPlan: {},
      _getAgentRunResultPayload() { return null; },
      _getPayloadBuildResult(result) { return result?.buildResult || null; },
      _getPayloadApplyGate(result) { return result?.applyGate || null; },
      _getResultFlowForCanvas(result) { return result?.flow || null; },
      _extractOperators(value) { return value?.operators || []; },
      _extractConnections(value) { return value?.connections || []; },
      _normalizeMissingResources(value) { return Array.isArray(value) ? value : []; },
      _resolvePendingParametersForDraft(result) { return result?.pendingParameters || []; },
      _getPendingOperatorSourceOperators() { return flow.operators; },
      _collectPendingDraftGroups(pending) {
        if (!item.parameterGroups || pending.length === 0) return [];
        return [{ operatorId: 'a', fields: [{ parameterName: 'Threshold', confirmedValue: '', dataType: 'number' }] }];
      },
      _getPendingParameterConfirmationState() { return { isConfirmed: false }; },
      _hasPendingDraftValue(value) { return String(value || '').trim().length > 0; },
      _getPendingResourceDraft() { return null; },
      _isCurrentResultAppliedToCanvas() { return item.applied === true; },
      _sanitizeAssistantFailureText(value) { return String(value || ''); },
      _formatGateStatus(value) { return String(value || ''); },
    };
    const presentation = deriveAiBuildPresentation(panel);
    assert.equal(presentation.overall.key, item.expected, item.name);
    const hasBlockingAction = presentation.actionItems.some(action => action.priority === 'blocking');
    const canApply = presentation.overall.key === 'ready_to_apply' && presentation.gate.canvasReady && !presentation.gate.blocked && !hasBlockingAction;
    assert.equal(canApply, item.canApply === true, `${item.name} apply eligibility`);
  }
});
