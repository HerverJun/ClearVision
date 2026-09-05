const sourceRoot = '../../../ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/';
for (const name of ['variables', 'main', 'ui-components', 'ai-panel', 'ai-shell', 'ai-conversation', 'ai-plan', 'ai-clarification', 'ai-responsive', 'ai-build']) {
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = new URL(`${sourceRoot}src/shared/styles/${name}.css`, import.meta.url);
  document.head.appendChild(link);
}
const { AiPanel } = await import(`${sourceRoot}src/features/ai/aiPanel.js`);
const { normalizeWorkspaceSnapshotForRestore } = await import(`${sourceRoot}src/features/ai/aiPanelSnapshotRecovery.js`);
const { default: httpClient } = await import(`${sourceRoot}src/core/messaging/httpClient.js`);
const records = await (await fetch('../ai-flow-state-audit-20260905/backend-observations.json')).json();
// Only external I/O is substituted. The production state, gate and renderer execute normally.
AiPanel.prototype._loadHistory = () => {};
AiPanel.prototype._refreshPlanningDeadlineContract = async () => {};
AiPanel.prototype._restoreWorkspaceRunReplays = async () => {};
AiPanel.prototype._tryRestoreLastSession = () => {};
let panel;
let requests = [];
function observe() {
  document.querySelector('#observation').textContent = JSON.stringify({
    revision: panel.workspaceSnapshotRevision, mode: panel.requirementMode,
    readonly: panel._isPlanSnapshotReadOnly(), readiness: panel.agentWorkspaceState.readinessStatus,
    canBuild: panel._getPlanBuildActionState(panel.pendingVisionPlan).canStart,
    requests
  });
}
httpClient.post = async (url, payload) => {
  if (url.endsWith('/workspace-snapshot')) return { snapshot: { revision: panel.workspaceSnapshotRevision + 1 } };
  throw new Error(`Unexpected test request: ${url}`);
};
function load() {
  panel?.dispose();
  panel = new AiPanel('panel', null);
  requests = [];
  const scenario = document.querySelector('#scenario').value;
  const record = records.find(item => item.id === (scenario === 'success' ? 'real-success-snapshot' : 'real-new-plan-inherits-build'));
  const snapshot = structuredClone(record.snapshot);
  if (scenario !== 'success') {
    snapshot.buildRunId = '';
    snapshot.buildRunStatus = 'idle';
    snapshot.buildTerminalSequence = null;
    snapshot.submittedBuildFingerprint = '';
  }
  if (scenario === 'draft') snapshot.requirementMode = snapshot.pendingPlanSnapshot.requirementMode = 'draft';
  panel._requestBackendPlanReadinessPreview = async request => {
    requests.push({ mode: request.requirementMode, planId: request.planId, answerRevision: request.answerRevision });
    return { ...request, metadataOnly: true, contractValid: true, acceptedAnswers: [],
      buildReadiness: { canBuild: true, blockers: [], missingResources: [], resolvedFields: [], remainingFields: [], contractVersion: 'v2', primaryMessage: 'Ready' } };
  };
  const originalPreview = panel._requestPlanReadinessPreview;
  panel._requestPlanReadinessPreview = () => false;
  panel._restoreWorkspaceSnapshotFromSession(normalizeWorkspaceSnapshotForRestore(snapshot).snapshot, panel.sessionId);
  panel._requestPlanReadinessPreview = originalPreview;
  if (scenario === 'timeout') {
    panel._dispatchAgentWorkspaceEvent({ type: 'workspace/readiness-failed', payload: { status: 'timeout', message: 'Injected timeout' } });
    panel._renderPlanWorkspace(panel.pendingVisionPlan);
  } else if (scenario === 'draft') {
    panel._requestPlanReadinessPreview(panel.pendingVisionPlan, { reason: 'session_restore_stale' });
  }
  observe();
}
document.querySelector('#load').addEventListener('click', load);
document.addEventListener('click', () => setTimeout(observe, 50));
load();
