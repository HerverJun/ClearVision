import assert from 'node:assert/strict';
import fs from 'node:fs';
import { aiPanelAgentWorkspaceMixin } from '../../../ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelAgentWorkspace.js';
import { aiPanelAgentRunMixin } from '../../../ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelAgentRun.js';
import { normalizeWorkspaceSnapshotForRestore } from '../../../ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelSnapshotRecovery.js';
import { installAgentWorkspaceState, dispatchAgentWorkspaceEvent } from '../../../ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/agentWorkspaceState.js';
import httpClient from '../../../ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js';

// These probes assert observed defects, not desired product behavior.
global.window = { setTimeout, clearTimeout, location: { protocol: 'http:', hostname: '127.0.0.1', port: '5000' } };
const { aiPanelGenerateRequestMixin } = await import('../../../ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelGenerateRequest.js');
const observations = [];
const originalPost = httpClient.post;
const originalGet = httpClient.get;
const plan = { planId: 'audit-plan', planHash: 'sha256:audit-plan' };
const noop = () => {};
function panel() {
  const value = Object.assign({}, aiPanelAgentWorkspaceMixin, aiPanelAgentRunMixin, aiPanelGenerateRequestMixin, {
    sessionId: 'audit-session', sessionNavigationEpoch: 0, workspaceSnapshotRevision: 7,
    workspaceMutationGeneration: 0, workspacePersistedGeneration: 0,
    workspaceSnapshotSaveQueue: Promise.resolve(), workspacePendingMutationCount: 0,
    _setResultStatusNote: noop, _addMessage: noop, _setGeneratingState: noop,
    _renderAgentWorkspaceOverview: noop, _renderPlanWorkspace: noop, _renderBuildWorkspaceFromAgentRun: noop,
    _updateProgress: noop, _renderQueuedHintBanner: noop, _resetPublicLiveEventState: noop,
    _handleWorkspacePersistenceStatus: noop, _setWorkspaceViewMode: noop,
    _asObject: value => value,
    _adoptCanonicalSessionId(value) { this.sessionId = value; },
    _dispatchAgentWorkspaceEvent(event) { return dispatchAgentWorkspaceEvent(this, event); },
    _buildWorkspaceSnapshotDelta() { return { lifecycleState: 'plan', answerRevision: 1 }; }
  });
  installAgentWorkspaceState(value, { sessionId: value.sessionId, plan });
  value.pendingVisionPlan = plan;
  return value;
}

try {
  const states = ['planning', 'plan_ready', 'build_completed', 'build_failed', 'recovery_conflict'];
  const restored = states.map(lifecycleState => {
    const result = normalizeWorkspaceSnapshotForRestore({ schemaVersion: 2, revision: 7, lifecycleState,
      pendingPlanSnapshot: plan, planRunId: 'plan-run', planRunStatus: 'completed',
      buildRunId: 'build-run', buildRunStatus: 'completed' });
    return { lifecycleState, trusted: result.trusted, reason: result.reason,
      revision: result.snapshot.revision, buildRunId: result.snapshot.buildRunId };
  });
  assert.equal(restored.find(item => item.lifecycleState === 'build_completed').trusted, false);
  observations.push({ id: 'lifecycle-restore', observed: restored });

  const saving = panel();
  let posts = 0;
  let healthy = false;
  httpClient.post = async () => {
    posts++;
    if (!healthy) throw new Error('injected transient storage failure');
    return { snapshot: { revision: 8 } };
  };
  await saving._queueWorkspaceSnapshotFlush('answer').catch(noop);
  healthy = true;
  const boundaries = [];
  for (const boundary of ['before_build', 'history_switch', 'new_conversation']) {
    boundaries.push({ boundary, allowed: await saving._flushWorkspaceSnapshotBeforeBoundary(boundary) });
  }
  assert.equal(posts, 1);
  assert.equal(boundaries.every(item => !item.allowed), true);
  observations.push({ id: 'save-retry', observed: { posts, dirty: saving.workspaceSnapshotDirty, boundaries } });

  const replaying = panel();
  let subscriptions = 0;
  const replayed = [];
  replaying._startAgentRunEventSource = () => { subscriptions++; };
  replaying._handleAgentRunEvent = event => replayed.push(event.eventType);
  httpClient.get = async () => ({ summary: { status: 'running' }, events: [
    { runId: 'live-build', sequence: 1, eventType: 'run.started', status: 'running', payload: {} }
  ] });
  await replaying._replayAgentRunPublicEventsById('live-build', { kind: 'build' });
  assert.equal(subscriptions, 0);
  observations.push({ id: 'running-history-reconnect', observed: { subscriptions, replayed, activeRunId: replaying.activeAgentRunId } });

  const cancelling = panel();
  cancelling.activePlanRunId = 'completed-plan-run';
  cancelling._dispatchAgentWorkspaceEvent({ type: 'workspace/run-patched', payload: { kind: 'plan', patch: { status: 'completed' } } });
  cancelling.activePlanRunCompletion = null;
  cancelling._resolveActivePlanRun({ runId: 'completed-plan-run', eventType: 'run.completed', payload: { planResult: plan } });
  cancelling._dispatchAgentWorkspaceEvent({ type: 'workspace/run-started', payload: { kind: 'build', runId: 'current-build-run' } });
  cancelling.isGenerating = true;
  const cancellations = [];
  httpClient.post = async url => { cancellations.push(url); return { success: true }; };
  cancelling._handleCancelGenerate();
  await Promise.resolve();
  assert.equal(cancellations[0], '/ai/agent-runs/completed-plan-run/cancel');
  observations.push({ id: 'cancel-after-plan-history', observed: { cancellations,
    currentBuild: cancelling.activeAgentRunId, cancellationLocked: cancelling.isCancellingGenerate } });

  const stalePlan = panel();
  stalePlan.activePlanRequestId = 'old-request';
  let resolvePlan;
  httpClient.post = () => new Promise(resolve => { resolvePlan = resolve; });
  const latePlan = stalePlan._requestBackendVisionPlanRun({}, { planRequestId: 'old-request' }).catch(error => error.message);
  stalePlan.sessionId = 'new-session';
  stalePlan.sessionNavigationEpoch++;
  stalePlan.workspaceSnapshotRevision = 99;
  stalePlan.activePlanRequestId = null;
  resolvePlan({ runId: 'late-plan-run', sessionId: 'audit-session', workspaceSnapshot: { revision: 8 } });
  const stalePlanError = await latePlan;
  assert.equal(stalePlan.sessionId, 'audit-session');
  assert.equal(stalePlan.workspaceSnapshotRevision, 8);
  observations.push({ id: 'late-plan-create', observed: { sessionId: stalePlan.sessionId,
    revision: stalePlan.workspaceSnapshotRevision, error: stalePlanError } });

  const staleBuild = panel();
  let resolveBuild;
  const lateSubscriptions = [];
  staleBuild._startAgentRunEventSource = runId => lateSubscriptions.push(runId);
  httpClient.post = () => new Promise(resolve => { resolveBuild = resolve; });
  const lateBuild = staleBuild._dispatchAgentRunGenerateRequest({});
  staleBuild.sessionId = 'new-session';
  staleBuild.sessionNavigationEpoch++;
  staleBuild._disposed = true;
  resolveBuild({ runId: 'late-build-run', sessionId: 'audit-session', workspaceSnapshot: { revision: 8 }, events: [] });
  await lateBuild;
  assert.equal(staleBuild.sessionId, 'audit-session');
  assert.deepEqual(lateSubscriptions, ['late-build-run']);
  observations.push({ id: 'late-build-create', observed: { disposed: staleBuild._disposed,
    sessionId: staleBuild.sessionId, revision: staleBuild.workspaceSnapshotRevision, subscriptions: lateSubscriptions } });

  const replanning = panel();
  replanning.workspaceBuildRunId = 'previous-build';
  replanning.workspaceSubmittedBuildFingerprint = 'previous-fingerprint';
  replanning.activeAgentRunId = 'previous-build';
  replanning.pendingVisionPlan = null;
  replanning.pendingVisionPlan = { planId: 'new-plan', planHash: 'sha256:new-plan' };
  assert.equal(replanning._isPlanSnapshotReadOnly(), true);
  observations.push({ id: 'new-plan-old-build-lock', observed: {
    newPlanId: replanning.pendingVisionPlan.planId, previousBuildId: replanning.workspaceBuildRunId,
    readOnly: replanning._isPlanSnapshotReadOnly(), save: await replanning._queueWorkspaceSnapshotFlush('answer') } });

  const draftRestore = panel();
  draftRestore._normalizeRequirementMode = value => value === 'draft' ? 'draft' : 'strict';
  draftRestore.requirementMode = 'draft';
  draftRestore.pendingVisionPlan = { ...plan, requirementMode: 'draft' };
  draftRestore._activatePlanIdentity(draftRestore.pendingVisionPlan);
  assert.equal(draftRestore.requirementMode, 'strict');
  observations.push({ id: 'draft-mode-restore', observed: {
    planMode: draftRestore.pendingVisionPlan.requirementMode, effectiveMode: draftRestore.requirementMode } });
} finally {
  httpClient.post = originalPost;
  httpClient.get = originalGet;
}

fs.writeFileSync(new URL('./frontend-observations.json', import.meta.url), JSON.stringify(observations, null, 2) + '\n');
console.log(JSON.stringify(observations, null, 2));
