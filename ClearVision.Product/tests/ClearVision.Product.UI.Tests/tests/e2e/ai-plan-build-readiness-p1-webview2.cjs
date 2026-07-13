const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('@playwright/test');

const cdpPort = Number(process.env.CV_P1_CDP_PORT || 9332);
const token = String(process.env.CV_P1_TOKEN || '');
const user = JSON.parse(process.env.CV_P1_USER || '{}');
const evidenceDir = path.resolve(process.env.CV_P1_EVIDENCE_DIR || '.tmp/ai-plan-build-readiness-p1-after');
let connectedBrowser = null;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function api(url, options = {}) {
  const response = await fetch(`http://127.0.0.1:5000${url}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      ...(options.headers || {}),
    },
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${options.method || 'GET'} ${url} returned ${response.status}: ${text}`);
  return text ? JSON.parse(text) : null;
}

async function screenshot(page, name, theme) {
  await page.evaluate(value => { document.documentElement.dataset.theme = value; }, theme);
  await page.waitForTimeout(120);
  await page.screenshot({ path: path.join(evidenceDir, `${name}-${theme}-1920x1080.png`) });
}

async function focus(page, selector) {
  await page.evaluate(value => document.querySelector(value)?.scrollIntoView?.({ block: 'center', inline: 'nearest' }), selector);
  await page.waitForTimeout(120);
}

async function waitForReadiness(page, expectedStatus, expectedMode, timeout = 20_000) {
  try {
    await page.waitForFunction(({ status, mode }) =>
      window.aiPanel?.agentWorkspaceState?.readinessStatus === status &&
      window.aiPanel?.agentWorkspaceState?.readinessPreview?.requirementMode === mode,
    { status: expectedStatus, mode: expectedMode }, { timeout });
  } catch (error) {
    const actual = await page.evaluate(() => ({
      mode: window.aiPanel?.requirementMode,
      status: window.aiPanel?.agentWorkspaceState?.readinessStatus,
      error: window.aiPanel?.agentWorkspaceState?.readinessError,
      preview: window.aiPanel?.agentWorkspaceState?.readinessPreview,
      activeRequest: window.aiPanel?.activePlanReadinessPreviewRequest,
      answerRevision: window.aiPanel?.planAnswerRevision,
      resourceRevision: window.aiPanel?.agentWorkspaceState?.resources?.revision,
    }));
    throw new Error(`Expected readiness ${expectedMode}/${expectedStatus}, got ${JSON.stringify(actual)}; ${error.message}`);
  }
  await page.waitForTimeout(150);
  return page.evaluate(() => ({
    status: window.aiPanel.agentWorkspaceState.readinessStatus,
    preview: window.aiPanel.agentWorkspaceState.readinessPreview,
    buildAction: window.aiPanel.agentWorkspaceState.projection.buildAction,
    requirementMode: window.aiPanel.requirementMode,
    answerRevision: window.aiPanel.planAnswerRevision,
    resourceRevision: window.aiPanel.agentWorkspaceState.resources.revision,
    planId: window.aiPanel.pendingVisionPlan?.planId,
    planHash: window.aiPanel.pendingVisionPlan?.planHash,
    canonicalPreviewAvailable: Boolean(window.aiPanel._getCurrentCanonicalPreview(window.aiPanel.pendingVisionPlan)),
    computedAction: window.aiPanel._getPlanBuildActionState(window.aiPanel.pendingVisionPlan),
    buildLabel: document.querySelector('#ai-btn-start-build')?.textContent?.trim() || '',
  }));
}

async function main() {
  fs.mkdirSync(evidenceDir, { recursive: true });
  assert(token, 'CV_P1_TOKEN is required.');

  await api('/api/cameras/bindings', {
    method: 'PUT',
    body: JSON.stringify({
      activeCameraId: 'p1-camera-top',
      bindings: [{
        id: 'p1-camera-top',
        displayName: 'P1 顶部工位相机',
        serialNumber: 'P1-CAMERA-TOP',
        manufacturer: 'Evidence',
        modelName: 'MetadataOnly',
        interfaceType: 'GigE',
        isEnabled: true,
      }],
    }),
  });

  const version = await fetch(`http://127.0.0.1:${cdpPort}/json/version`).then(response => response.json());
  const browser = await chromium.connectOverCDP(version.webSocketDebuggerUrl);
  connectedBrowser = browser;
  const context = browser.contexts()[0];
  const page = context.pages()[0];
  await page.evaluate(({ authToken, authUser }) => {
    sessionStorage.setItem('cv_auth_token', authToken);
    sessionStorage.setItem('cv_current_user', JSON.stringify(authUser));
    localStorage.setItem('cv_welcome_shown', 'true');
  }, { authToken: token, authUser: user });
  await page.goto('http://localhost:5000/index.html');
  await page.waitForSelector('#loading-screen', { state: 'hidden', timeout: 45_000 });
  await page.locator('.nav-btn[data-view="ai"]').click();
  await page.waitForFunction(() => Boolean(window.aiPanel && !window.aiPanel._disposed));
  await page.setViewportSize({ width: 1920, height: 1080 });

  const planEnvelope = await api('/api/ai/agent-plan', {
    method: 'POST',
    body: JSON.stringify({
      description: 'Inspect packaging surface damage with a station camera; visible damage is NG; keep output local.',
      sessionId: 'p1-after-webview2',
      requirementMode: 'strict',
      metadataOnly: true,
    }),
  });

  const readinessRequest = await page.evaluate(envelope => {
    const panel = window.aiPanel;
    const plan = panel._normalizeBackendPlanResult(envelope.planResult, envelope.planResult.originalUserPrompt);
    panel.pendingVisionPlan = plan;
    panel.sessionId = envelope.sessionId;
    panel._dispatchAgentWorkspaceEvent({ type: 'workspace/plan-received', payload: { plan } });
    const answers = [
      { field: 'inspection_object', value: 'packaging_box_surface', origin: 'explicit_user_text', confidence: 1, resolved: true },
      { field: 'task_type', value: 'surface_defect', origin: 'explicit_user_selection', confidence: 1, resolved: true },
      { field: 'image_source', value: 'station_camera', origin: 'explicit_user_selection', confidence: 1, resolved: true },
      { field: 'acceptance_criteria', value: 'visible_damage_is_ng', origin: 'explicit_user_text', confidence: 1, resolved: true },
      { field: 'output_target', value: 'local_structured_result', origin: 'explicit_user_selection', confidence: 1, resolved: true },
      { field: 'algorithm_strategy', value: 'deep_learning', origin: 'explicit_user_selection', confidence: 1, resolved: true },
    ];
    panel.planQuestionAnswers = Object.fromEntries(answers.map(answer => [answer.field, answer]));
    panel.planAnswerRevision = 6;
    panel._dispatchAgentWorkspaceEvent({ type: 'workspace/answers-replaced', payload: { answers } });
    panel._dispatchAgentWorkspaceEvent({ type: 'workspace/answer-revision-set', payload: { revision: 6 } });
    return panel._buildPlanReadinessPreviewRequest(plan);
  }, planEnvelope);

  const strictReadiness = await api('/api/ai/agent-plan/readiness-preview', {
    method: 'POST', body: JSON.stringify(readinessRequest),
  });

  const strictState = await page.evaluate(({ readiness, request }) => {
    const panel = window.aiPanel;
    panel._applyPlanReadinessPreviewResult(panel.pendingVisionPlan, readiness);
    const candidates = [
      ...(readiness.missingResources || []),
      ...(readiness.buildReadiness?.blockers || []).map(item => item.resource).filter(Boolean),
    ];
    const resource = candidates.find(item => String(item.resourceType || '').toLowerCase().includes('camera'));
    if (!resource) throw new Error('Real Readiness did not return a canonical missing resource.');
    const plan = { ...panel.pendingVisionPlan, missingResources: [{ ...resource, source: 'plan' }] };
    panel.pendingVisionPlan = plan;
    panel._dispatchAgentWorkspaceEvent({ type: 'workspace/plan-received', payload: { plan } });
    panel._dispatchAgentWorkspaceEvent({ type: 'workspace/result-received', payload: { result: {
      missingResources: [{ ...resource, resourceKey: resource.resourceKey || 'op_acq.CameraBindingId', source: 'build_result' }],
    } } });
    panel._dispatchAgentWorkspaceEvent({ type: 'workspace/resource-decision-set', payload: {
      resource: { ...resource, source: 'workspace' },
      decision: { status: 'pending', source: 'workspace_restore' },
    } });
    panel._renderPlanWorkspace(panel.pendingVisionPlan);
    panel._renderAgentWorkspaceOverview();
    panel._updatePlanBuildActionState();
    return {
      resource,
      request,
      interfaceResponse: readiness,
      projection: panel.agentWorkspaceState.projection,
      snapshot: panel._buildWorkspaceSnapshotDelta(),
      readinessStatus: panel.agentWorkspaceState.readinessStatus,
      cardCount: document.querySelectorAll('[data-ai-hook="clarification-resources"] .ai-resource-audit-card').length,
      uniqueCanonicalCount: new Set(panel.agentWorkspaceState.projection.missingResources.map(item => item.canonicalId)).size,
      matchingResourceCount: panel.agentWorkspaceState.projection.missingResources.filter(item => item.canonicalId === resource.canonicalId).length,
      cardText: document.querySelector('[data-ai-hook="clarification-resources"]')?.textContent?.replace(/\s+/g, ' ').trim() || '',
      buildLabel: document.querySelector('#ai-btn-start-build')?.textContent?.trim() || '',
    };
  }, { readiness: strictReadiness, request: readinessRequest });

  assert(strictState.matchingResourceCount === 1, `Expected the same canonical resource from three sources once, got ${strictState.matchingResourceCount}.`);
  assert(strictState.cardCount === strictState.uniqueCanonicalCount,
    `Rendered cards (${strictState.cardCount}) do not match unique canonical resources (${strictState.uniqueCanonicalCount}).`);
  assert(strictState.readinessStatus === 'blocked', `Strict readiness should be blocked, got ${strictState.readinessStatus}.`);
  assert(!/正在校验构建条件/.test(strictState.buildLabel), `Strict blocked state falsely appears validating: ${strictState.buildLabel}`);
  assert(/影响算子/.test(strictState.cardText) && /影响参数/.test(strictState.cardText) && /解决位置/.test(strictState.cardText),
    `Resource card is not identifiable/actionable: ${strictState.cardText}`);
  await focus(page, '[data-ai-hook="clarification-resources"]');
  await screenshot(page, 'strict-resource-blocked', 'light');
  await screenshot(page, 'strict-resource-blocked', 'dark');

  await page.evaluate(() => window.aiPanel._setRequirementMode('draft'));
  const draftState = await waitForReadiness(page, 'ready', 'draft');
  assert(draftState.preview?.requirementMode === 'draft', 'Draft readiness response did not match Draft mode.');
  assert(draftState.preview?.buildReadiness?.canBuild === true, 'Backend did not authorize editable Draft generation.');
  assert(/草稿/.test(draftState.buildLabel), `Draft CTA does not describe draft semantics: ${JSON.stringify(draftState)}`);
  await focus(page, '[data-ai-hook="clarification-resources"]');
  await screenshot(page, 'draft-authorized-deploy-blocked', 'light');
  await screenshot(page, 'draft-authorized-deploy-blocked', 'dark');

  await page.evaluate(() => window.aiPanel._setRequirementMode('strict'));
  await waitForReadiness(page, 'blocked', 'strict');
  const temporaryModelPath = path.join(process.env.TEMP || process.cwd(), 'clearvision-p1-metadata-only-model.onnx');
  fs.writeFileSync(temporaryModelPath, 'metadata-only WebView2 binding evidence');
  const templatePath = path.resolve('../../../quality/evidence/ai-plan-build-readiness-p1/before/plan-resource-duplicates-light-1920x1080.png');
  const bindingResults = await page.evaluate(({ resources, modelPath, imagePath }) => {
    const panel = window.aiPanel;
    return resources.map(resource => {
      const model = panel._getMissingResourceActionModel(resource);
      const type = String(resource.resourceType || '').toLowerCase();
      const value = type.includes('camera') ? 'p1-camera-top'
        : type.includes('model') ? modelPath
          : type.includes('template') ? imagePath
            : (type.includes('calibration') || type.includes('measurement')) ? '0.024'
              : '';
      return {
        canonicalId: resource.canonicalId,
        resourceType: resource.resourceType,
        action: model.action,
        accepted: value ? panel._handleMissingResourceAction(resource, model.action, { value }) : false,
      };
    });
  }, { resources: strictState.projection.missingResources || [], modelPath: temporaryModelPath, imagePath: templatePath });
  const unresolved = bindingResults.filter(item => item.accepted !== true);
  assert(unresolved.length === 0, `Strict resources lacked an executable in-workbench binding path: ${JSON.stringify(unresolved)}`);
  assert(bindingResults.some(item => item.action === 'select_camera_binding'), 'Existing camera binding action was not reused.');
  const boundState = await waitForReadiness(page, 'ready', 'strict');
  assert(boundState.preview?.requirementMode === 'strict', 'Bound readiness response did not return to Strict mode.');
  assert(boundState.preview?.buildReadiness?.canBuild === true, 'Strict backend readiness did not release after canonical binding.');
  await focus(page, '#ai-btn-start-build');
  await screenshot(page, 'strict-bound-ready', 'light');
  await screenshot(page, 'strict-bound-ready', 'dark');

  const finalEvidence = await page.evaluate(() => ({
    workspaceSnapshot: window.aiPanel._buildWorkspaceSnapshotDelta(),
    projection: window.aiPanel.agentWorkspaceState.projection,
    readinessStatus: window.aiPanel.agentWorkspaceState.readinessStatus,
    readinessPreview: window.aiPanel.agentWorkspaceState.readinessPreview,
    buildLabel: document.querySelector('#ai-btn-start-build')?.textContent?.trim() || '',
  }));
  fs.writeFileSync(path.join(evidenceDir, 'real-interface-workspace-projection.json'), JSON.stringify({
    strict: strictState,
    draft: draftState,
    bound: boundState,
    final: finalEvidence,
  }, null, 2));
  console.log(JSON.stringify({
    strictStatus: strictState.readinessStatus,
    strictCardCount: strictState.cardCount,
    strictCanonicalId: strictState.resource.canonicalId,
    draftStatus: draftState.status,
    draftCanBuild: draftState.preview?.buildReadiness?.canBuild,
    boundStatus: boundState.status,
    boundCanBuild: boundState.preview?.buildReadiness?.canBuild,
    finalBuildLabel: finalEvidence.buildLabel,
  }, null, 2));
  await browser.close();
  connectedBrowser = null;
}

main().catch(async error => {
  console.error(error);
  await connectedBrowser?.close().catch(() => {});
  process.exitCode = 1;
});
