import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { aiPanelAgentWorkspaceMixin } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelAgentWorkspace.js';
import { aiPanelAgentRunMixin } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelAgentRun.js';
import { normalizeWorkspaceSnapshotForRestore } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelSnapshotRecovery.js';
import { installAgentWorkspaceState, dispatchAgentWorkspaceEvent } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/agentWorkspaceState.js';
import { renderAiPlanWorkspace } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelPlanPresentation.js';
import httpClient from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js';

global.window = { setTimeout, clearTimeout, location: { protocol: 'http:', hostname: '127.0.0.1', port: '5000' } };
const { aiPanelSessionHistoryMixin } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelSessionHistory.js');
const { aiPanelGenerateRequestMixin } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelGenerateRequest.js');
const plan = { planId: 'plan-1', planHash: 'sha256:plan-1' };
const noop = () => {};
function panel() {
  const value = Object.assign({}, aiPanelAgentWorkspaceMixin, aiPanelAgentRunMixin, aiPanelSessionHistoryMixin, aiPanelGenerateRequestMixin, {
    sessionId: 'session-1', sessionNavigationEpoch: 0,
    workspaceSnapshotSaveQueue: Promise.resolve(),
    _setResultStatusNote: noop, _addMessage: noop,
    _setGeneratingState(value) { this.isGenerating = value; },
    _renderAgentWorkspaceOverview: noop, _renderPlanWorkspace: noop, _renderBuildWorkspaceFromAgentRun: noop,
    _updateProgress: noop, _renderQueuedHintBanner: noop, _resetPublicLiveEventState: noop,
    _handleWorkspacePersistenceStatus: noop, _setWorkspaceViewMode: noop,
    _setAssistantTurnStatus: noop, _renderPlanRunTimeline: noop, _requestPlanReadinessPreview: noop,
    _asObject: value => value, _normalizeRequirementMode: value => value === 'draft' ? 'draft' : 'strict',
    _adoptCanonicalSessionId(value) { this.sessionId = value; },
    _dispatchAgentWorkspaceEvent(event) { return dispatchAgentWorkspaceEvent(this, event); },
    _buildWorkspaceSnapshotDelta() { return { lifecycleState: 'plan', answerRevision: 1 }; }
  });
  installAgentWorkspaceState(value, { sessionId: value.sessionId });
  value.pendingVisionPlan = plan;
  value.workspaceSnapshotRevision = 7;
  return value;
}

for (const status of ['completed', 'failed', 'cancelled']) {
  test(`new Plan after ${status} Build clears only previous Plan authority`, async t => {
    const value = panel();
    value.workspaceBuildRunId = 'old-build';
    value.workspaceSubmittedBuildFingerprint = 'old-fingerprint';
    value._dispatchAgentWorkspaceEvent({ type: 'workspace/run-started', payload: { kind: 'build', runId: 'old-build' } });
    value._dispatchAgentWorkspaceEvent({ type: 'workspace/run-patched', payload: { kind: 'build', patch: { status } } });
    value.pendingVisionPlan = { ...plan };
    assert.equal(value._isPlanSnapshotReadOnly(), true);
    value.pendingVisionPlan = { planId: 'new-plan', planHash: 'sha256:new-plan' };
    assert.equal(value._isPlanSnapshotReadOnly(), false);
    assert.equal(value.activeAgentRunId, null);
    assert.equal(value.workspaceSnapshotRevision, 7);
    t.mock.method(httpClient, 'post', async () => ({ snapshot: { revision: 8 } }));
    await value._queueWorkspaceSnapshotFlush('answer');
    assert.equal(value.workspaceSnapshotDirty, false);
    assert.equal(value.workspaceSnapshotRevision, 8);
  });
}

for (const reason of ['before_build', 'history_switch', 'new_conversation', 'host_close']) {
  test(`503 then ${reason} retries without another edit`, async t => {
    const value = panel();
    let posts = 0;
    t.mock.method(httpClient, 'post', async () => {
      if (++posts === 1) throw Object.assign(new Error('temporary storage failure'), { status: 503 });
      return { snapshot: { revision: 8 } };
    });
    await assert.rejects(value._queueWorkspaceSnapshotFlush('answer'));
    assert.equal(await value._flushWorkspaceSnapshotBeforeBoundary(reason), true);
    assert.equal(posts, 2);
    assert.equal(value.workspaceSnapshotDirty, false);
  });
}

test('boundary retry cannot save into a different session', async t => {
  const value = panel();
  let release;
  t.mock.method(httpClient, 'post', () => new Promise(resolve => { release = resolve; }));
  const save = value._queueWorkspaceSnapshotFlush();
  await new Promise(resolve => setImmediate(resolve));
  const boundary = value._flushWorkspaceSnapshotBeforeBoundary();
  value.sessionId = 'session-2';
  value.sessionNavigationEpoch++;
  release({ snapshot: { revision: 8 } });
  await save;
  assert.equal(await boundary, false);
  assert.equal(value.workspaceSnapshotRevision, 7);
});

test('production backend lifecycle literals restore without losing run or revision', () => {
  const files = [
    '../../../../src/ClearVision.Product.Desktop/Endpoints/AgentRunEndpoints.cs',
    '../../../../src/ClearVision.Product.Infrastructure/AI/Agent/VisionAgentBuildTerminalProjector.cs'
  ];
  const states = new Set(['planning', 'plan_ready', 'plan_blocked', 'build_completed', 'build_failed', 'build_cancelled']);
  for (const file of files) {
    const source = fs.readFileSync(new URL(file, import.meta.url), 'utf8');
    for (const match of source.matchAll(/LifecycleState\s*=\s*"([a-z_]+)"/g)) states.add(match[1]);
  }
  for (const lifecycleState of states) {
    const result = normalizeWorkspaceSnapshotForRestore({ schemaVersion: 2, revision: 7, lifecycleState,
      pendingPlanSnapshot: plan, buildRunId: 'build-1', buildRunStatus: 'completed' });
    assert.equal(result.snapshot.revision, 7, lifecycleState);
    assert.equal(result.trusted, lifecycleState !== 'recovery_conflict', lifecycleState);
    if (result.trusted) assert.equal(result.snapshot.buildRunId, 'build-1');
    else assert.equal(result.snapshot.recoveryRunIds.build, 'build-1');
  }
});

for (const kind of ['plan', 'build']) {
  test(`running ${kind} history resumes after sequence and handles a later terminal`, async t => {
    const value = panel();
    let subscribed;
    value._startAgentRunEventSource = (runId, options) => { subscribed = { runId, ...options }; };
    value._normalizeBackendPlanResult = result => result;
    value._handleAgentRunTerminalEvent = () => { value.isCancellingGenerate = false; value._setGeneratingState(false); };
    value._appendAgentRunProcessLine = noop;
    value._updateAgentRunWorkbenchState = noop;
    t.mock.method(httpClient, 'get', async () => ({ summary: { status: 'running' }, events: [
      { runId: 'live-run', sequence: 1, eventType: 'run.started', status: 'running', payload: {} }
    ] }));
    await value._replayAgentRunPublicEventsById('live-run', { kind });
    assert.deepEqual(subscribed, { runId: 'live-run', lastSequence: 1 });
    assert.equal(value.isGenerating, true);
    value._handleAgentRunEvent({ runId: 'live-run', sequence: 2, eventType: 'run.completed', status: 'completed',
      payload: { planResult: { planId: 'recovered-plan', planHash: 'sha256:recovered' }, workspaceSnapshot: { revision: 9 } } });
    assert.equal(value.isGenerating, false);
    if (kind === 'plan') {
      assert.equal(value.pendingVisionPlan.planId, 'recovered-plan');
      assert.equal(value.workspaceSnapshotRevision, 9);
      assert.equal(value.activePlanRunId, null);
    }
  });
}

test('completed Plan replay cannot overwrite a newer Build snapshot and cancellation targets Build', async t => {
  const value = panel();
  value.activePlanRunId = 'old-plan-run';
  value._dispatchAgentWorkspaceEvent({ type: 'workspace/run-patched', payload: { kind: 'plan', patch: { status: 'completed' } } });
  value._resolveActivePlanRun({ runId: 'old-plan-run', eventType: 'run.completed', payload: {
    planResult: { ...plan }, workspaceSnapshot: { revision: 2 }
  } });
  assert.equal(value.workspaceSnapshotRevision, 7);
  value._dispatchAgentWorkspaceEvent({ type: 'workspace/run-started', payload: { kind: 'build', runId: 'current-build' } });
  value.isGenerating = true;
  let cancelled;
  t.mock.method(httpClient, 'post', async url => { cancelled = url; });
  value._handleCancelGenerate();
  assert.equal(cancelled, '/ai/agent-runs/current-build/cancel');
});

test('Plan completing between snapshot and replay still restores its final result', async t => {
  const value = panel();
  value.pendingVisionPlan = null;
  value._normalizeBackendPlanResult = result => result;
  value._dispatchAgentWorkspaceEvent({ type: 'workspace/run-started', payload: { kind: 'plan', runId: 'race-plan' } });
  let subscriptions = 0;
  value._startAgentRunEventSource = () => subscriptions++;
  t.mock.method(httpClient, 'get', async () => ({ summary: { status: 'completed' }, events: [
    { runId: 'race-plan', sequence: 2, eventType: 'run.completed', status: 'completed', payload: {
      planResult: { ...plan }, workspaceSnapshot: { revision: 9 }
    } }
  ] }));
  await value._replayAgentRunPublicEventsById('race-plan', { kind: 'plan' });
  assert.equal(value.pendingVisionPlan.planId, plan.planId);
  assert.equal(value.workspaceSnapshotRevision, 9);
  assert.equal(subscriptions, 0);
});

test('revision conflict from another Plan never rebases old answers onto it', async t => {
  const value = panel();
  let posts = 0;
  t.mock.method(httpClient, 'post', async () => {
    posts++;
    throw Object.assign(new Error('conflict'), { status: 409, payload: {
      errorCode: 'workspace_revision_conflict', snapshot: {
        revision: 9, pendingPlanSnapshot: { planId: 'other-plan', planHash: 'sha256:other' }
      }
    } });
  });
  await assert.rejects(value._queueWorkspaceSnapshotFlush());
  assert.equal(posts, 1);
  assert.equal(value.workspaceRecoveryBlocked, true);
  assert.equal(value.workspaceSnapshotRevision, 9);
});

for (const kind of ['plan', 'build']) {
  for (const change of ['navigation', 'dispose', 'superseded']) {
    test(`late ${kind} create after ${change} cannot change session, revision or subscription`, async t => {
      const value = panel();
      let release;
      let subscriptions = 0;
      value._startAgentRunEventSource = () => subscriptions++;
      value.activePlanRequestId = 'request-1';
      t.mock.method(httpClient, 'post', () => new Promise(resolve => { release = resolve; }));
      const pending = kind === 'plan'
        ? value._requestBackendVisionPlanRun({}, { planRequestId: 'request-1' }).catch(() => false)
        : value._dispatchAgentRunGenerateRequest({});
      if (change === 'navigation') { value.sessionId = 'new-session'; value.sessionNavigationEpoch++; }
      if (change === 'dispose') value._disposed = true;
      if (change === 'superseded') { value.activePlanRequestId = null; value.pendingBuildCreateIdentity = {}; }
      const sessionId = value.sessionId;
      value.workspaceSnapshotRevision = 99;
      release({ runId: 'late-run', sessionId: 'session-1', workspaceSnapshot: { revision: 8 }, events: [] });
      await pending;
      assert.equal(value.sessionId, sessionId);
      assert.equal(value.workspaceSnapshotRevision, 99);
      assert.equal(subscriptions, 0);
    });
  }
}

test('restored draft mode survives activation with an empty mode cache', () => {
  const value = panel();
  value.requirementMode = 'draft';
  value.pendingVisionPlan = { ...plan, requirementMode: 'draft' };
  value._activatePlanIdentity(value.pendingVisionPlan);
  assert.equal(value.requirementMode, 'draft');
});

test('completed Build restores the same result, run and revision', () => {
  const value = panel();
  value._normalizeBackendPlanResult = result => ({ ...result });
  value._restoreWorkspaceRunReplays = noop;
  value._updatePlanBuildActionState = noop;
  const result = { flow: { operators: [{ id: 'operator-1' }], connections: [] },
    applyGate: { canvasApplyReady: true } };
  const snapshot = normalizeWorkspaceSnapshotForRestore({ schemaVersion: 2, revision: 12,
    lifecycleState: 'build_completed', pendingPlanSnapshot: plan,
    planRunId: 'plan-history', planRunStatus: 'completed',
    buildRunId: 'build-history', buildRunStatus: 'completed', submittedBuildFingerprint: 'fingerprint' }).snapshot;
  assert.equal(value._restoreWorkspaceSnapshotFromSession(snapshot, value.sessionId, result), true);
  assert.deepEqual(value.currentResult, result);
  assert.equal(value.workspaceBuildRunId, 'build-history');
  assert.equal(value.workspaceSnapshotRevision, 12);
  assert.equal(value.isGenerating, false);
});

test('running history restores busy state before replay network I/O completes', () => {
  const value = panel();
  value._normalizeBackendPlanResult = result => ({ ...result });
  value._restoreWorkspaceRunReplays = noop;
  value._updatePlanBuildActionState = noop;
  const snapshot = normalizeWorkspaceSnapshotForRestore({ schemaVersion: 2, revision: 12,
    lifecycleState: 'building', pendingPlanSnapshot: plan,
    buildRunId: 'live-build', buildRunStatus: 'running' }).snapshot;
  value._restoreWorkspaceSnapshotFromSession(snapshot, value.sessionId);
  assert.equal(value.isGenerating, true);
  assert.equal(value.activeAgentRunId, 'live-build');
});

for (const kind of ['plan', 'build']) {
  test(`explicit cancellation of pending ${kind} is sent to the late created run after navigation`, async t => {
    const value = panel();
    value.isGenerating = true;
    value.activePlanRequestId = kind === 'plan' ? 'request-1' : null;
    value.activeGenerateRequestId = kind === 'build' ? 'build-request' : null;
    value.planningLifecycle = { status: 'running' };
    value._markPlanningLifecycleTerminal = noop;
    let release;
    const cancellations = [];
    t.mock.method(httpClient, 'post', (url) => {
      if (url.endsWith('/cancel')) { cancellations.push(url); return Promise.resolve({}); }
      return new Promise(resolve => { release = resolve; });
    });
    const pending = kind === 'plan'
      ? value._requestBackendVisionPlanRun({}, { planRequestId: 'request-1' }).catch(() => false)
      : value._dispatchAgentRunGenerateRequest({});
    value._handleCancelGenerate();
    value.sessionId = 'new-session';
    value.sessionNavigationEpoch++;
    release({ runId: 'cancelled-late-run', sessionId: 'session-1', workspaceSnapshot: { revision: 8 }, events: [] });
    await pending;
    assert.deepEqual(cancellations, ['/ai/agent-runs/cancelled-late-run/cancel']);
    assert.equal(value.sessionId, 'new-session');
    assert.equal(value.workspaceSnapshotRevision, 7);
  });
}

test('production renderer exposes working readiness and save retries', async () => {
  const value = panel();
  const buttons = new Map();
  const root = {
    dataset: {}, querySelectorAll: () => [],
    querySelector(selector) {
      if (!this.innerHTML?.includes(`id="${selector.slice(1)}"`)) return null;
      if (!buttons.has(selector)) buttons.set(selector, { addEventListener(_, callback) { this.click = callback; } });
      return buttons.get(selector);
    }
  };
  value.container = { querySelector: () => root };
  value._getWorkspaceViewMode = () => 'plan';
  value._getPlanBuildActionState = () => ({ canStart: false });
  value._dispatchAgentWorkspaceEvent({ type: 'workspace/readiness-failed', payload: { status: 'timeout' } });
  let retried;
  value._requestPlanReadinessPreview = (plan, options) => { retried = { plan, options }; };
  value.workspaceMutationGeneration = 1;
  value._syncWorkspaceSnapshotDirty();
  renderAiPlanWorkspace(value, plan);
  buttons.get('#ai-btn-retry-readiness-preview').click();
  assert.deepEqual(retried, { plan, options: { reason: 'retry' } });
  let saved = false;
  value._flushWorkspaceSnapshotBeforeBoundary = async () => { saved = true; };
  await buttons.get('#ai-btn-retry-workspace-save').click();
  assert.equal(saved, true);
});
