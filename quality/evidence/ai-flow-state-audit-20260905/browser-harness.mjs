const sourceRoot = '../../../ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/';
for (const name of ['variables', 'main', 'ui-components', 'ai-panel', 'ai-shell', 'ai-conversation', 'ai-plan', 'ai-clarification', 'ai-responsive', 'ai-build']) {
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = new URL(`${sourceRoot}src/shared/styles/${name}.css`, import.meta.url);
  document.head.appendChild(link);
}
const { AiPanel } = await import(`${sourceRoot}src/features/ai/aiPanel.js`);
const { normalizeWorkspaceSnapshotForRestore } = await import(`${sourceRoot}src/features/ai/aiPanelSnapshotRecovery.js`);
const records = await (await fetch('./backend-observations.json')).json();
// Keep external I/O out of this isolated rendering harness. State transitions,
// production renderers and actual controls still come from the shipped AiPanel.
AiPanel.prototype._loadHistory = () => {};
AiPanel.prototype._refreshPlanningDeadlineContract = async () => {};
AiPanel.prototype._restoreWorkspaceRunReplays = async () => {};
let currentPanel;
function showObservation() {
  document.querySelector('#audit-observation').textContent = JSON.stringify({
    plan: currentPanel.pendingVisionPlan?.planId,
    revision: currentPanel.workspaceSnapshotRevision,
    readOnly: currentPanel._isPlanSnapshotReadOnly(),
    effectiveMode: currentPanel.requirementMode,
    previousBuild: currentPanel.workspaceBuildRunId
  });
}
function load() {
  currentPanel?.dispose();
  currentPanel = new AiPanel('audit-panel', null);
  currentPanel._requestPlanReadinessPreview = () => false;
  const kind = document.querySelector('#audit-scenario').value;
  const record = records.find(item => item.id === (kind === 'successful-restore' ? 'real-success-snapshot' : 'real-new-plan-inherits-build'));
  const snapshot = structuredClone(record.snapshot);
  if (kind === 'draft-restore' || kind === 'readiness-timeout') {
    snapshot.buildRunId = '';
    snapshot.submittedBuildFingerprint = '';
    if (kind === 'draft-restore') {
      snapshot.requirementMode = 'draft';
      snapshot.pendingPlanSnapshot.requirementMode = 'draft';
    }
  }
  const normalized = normalizeWorkspaceSnapshotForRestore(snapshot).snapshot;
  currentPanel._restoreWorkspaceSnapshotFromSession(normalized, currentPanel.sessionId);
  if (kind === 'draft-restore') {
    currentPanel._activatePlanIdentity(currentPanel.pendingVisionPlan);
    currentPanel._renderPlanWorkspace(currentPanel.pendingVisionPlan);
  }
  if (kind === 'readiness-timeout') {
    currentPanel._dispatchAgentWorkspaceEvent({
      type: 'workspace/readiness-failed', payload: { message: 'Injected readiness timeout', status: 'timeout' }
    });
    currentPanel._renderPlanWorkspace(currentPanel.pendingVisionPlan);
  }
  showObservation();
}
document.querySelector('#audit-load').addEventListener('click', load);
load();
