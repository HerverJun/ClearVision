'use strict';

// This is an engineering-evidence scenario: the product is hosted by a real
// WinForms/WebView2 instance, while the Station and inspection authorities are
// deliberately local fixtures. It must never be presented as field acceptance.

const crypto = require('node:crypto');
const {
  assert,
  captureRuntimeErrors,
  connectToDesktopWebView2,
  readBrowserDpiEvidence,
  requiredEnvironment,
  seedAuthenticatedSession,
  writeJsonEvidence,
  writePngEvidence
} = require('./webview2-harness.cjs');

const projectId = '11111111-1111-4111-8111-111111111111';
const snapshotId = '22222222-2222-4222-8222-222222222222';
const sessionId = '33333333-3333-4333-8333-333333333333';
const resultId = '44444444-4444-4444-8444-444444444444';
const stationId = 'station-a';

function metadata(value, name) {
  return value?.[name] ?? value?.[name[0].toUpperCase() + name.slice(1)];
}

function project() {
  return {
    id: projectId,
    name: 'F05 隔离连续检测工程',
    description: '真实 WebView2 中的隔离工程证据。',
    version: '1.0.0',
    persistenceRevision: 12,
    createdAt: '2026-07-26T00:00:00Z',
    modifiedAt: '2026-07-26T01:00:00Z',
    lastOpenedAt: '2026-07-26T01:00:00Z',
    flow: null,
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
  };
}

function station() {
  return {
    stationId,
    stationName: 'F05 隔离工作站',
    lineName: 'F05 验证线',
    machineName: 'CV-F05-STATION-A',
    clientVersion: '2.1.0',
    areaName: '验证区',
    workcellName: '单元 1',
    inspectionNodeName: '连续检测',
    cameraAlias: '本机替身相机',
    stationRole: 'Inspection',
    owner: '工程验证',
    isEnabled: true,
    remark: 'Fixture，本机替身，不是现场工作站。',
    onlineState: 1,
    state: 'Running',
    runtimeState: 2,
    isOnline: true,
    startedAtUtc: '2026-07-26T01:00:00Z',
    lastSeenAtUtc: '2026-07-26T02:00:00Z',
    packageId: 'pkg-f05',
    packageName: 'F05 隔离运行包',
    packageFlowHash: 'sha256:package',
    executionFlowHash: 'sha256:execution',
    flowHash: 'sha256:execution',
    executionSnapshotId: snapshotId,
    projectRevision: 12,
    decisionConfigurationHash: 'sha256:decision',
    executionRunMode: 'Production',
    currentRunId: 'run-f05',
    sessionOutcomeStatistics: outcomeStatistics(),
    sessionOutcomeStatisticsIsLegacyProjection: false,
    lastExecutionOutcome: 'Succeeded',
    lastDecisionOutcome: 'Ng',
    lastDiagnosticCode: 'FIXTURE_NG',
    lastDiagnosticMessage: 'F05 fixture NG',
    lastResultAtUtc: '2026-07-26T01:59:30Z',
    averageExecutionTimeMs: 24.5,
    spoolPendingCount: 0,
    spoolBytes: 0,
    cpuUsagePercent: 32.5,
    workingSetMb: 256,
    diskFreeMb: 20480,
    diskTotalMb: 51200,
    cameraStatusSummary: 'Ready',
    plcStatusSummary: 'Fixture',
    currentPackageHealth: 'Healthy'
  };
}

function outcomeStatistics() {
  return {
    totalAttemptCount: 9, executionSucceededCount: 5, validDecisionCount: 2,
    okCount: 1, ngCount: 1, undeterminedCount: 1, notApplicableCount: 1,
    invalidCount: 1, failedCount: 1, cancelledCount: 1, timedOutCount: 1, skippedCount: 1
  };
}

function stationResult() {
  return {
    schemaVersion: 2, stationId, lineName: 'F05 验证线', sequenceId: 9,
    messageId: 'f05-result-9', runId: 'run-f05', packageId: 'pkg-f05',
    packageName: 'F05 隔离运行包', packageVersion: '1.0.0', outcome: 1,
    inspectionStatus: 'NG', executionOutcome: 'Succeeded', decisionOutcome: 'Ng',
    executionTimeMs: 25, diagnosticCode: 'FIXTURE_NG', diagnosticMessage: 'F05 fixture NG',
    completedAtUtc: '2026-07-26T02:00:00Z'
  };
}

function localResultSummary() {
  return {
    id: resultId, resultId, projectId, status: 'Ok', executionOutcome: 'Succeeded',
    decisionOutcome: 'Ok', decisionSource: 'FinalDecision', reasonCode: 'F05_OK',
    hasJudgmentSignal: true, defectCount: 0, processingTimeMs: 18,
    inspectionTime: '2026-07-26T02:00:02Z', startedAt: '2026-07-26T02:00:01Z',
    completedAt: '2026-07-26T02:00:02Z', confidenceScore: 0.99,
    flowVersionHash: 'f05-flow', calibrationBundleId: null, runId: 'run-f05',
    diagnosticCode: null, diagnosticMessage: null, errorMessage: null
  };
}

function localResultDetail() {
  return {
    ...localResultSummary(), defects: [], traceability: {
      flowVersionHash: 'f05-flow', calibrationBundleId: null, sessionId,
      runId: 'run-f05', projectPersistenceRevision: 12,
      decisionConfigurationHash: 'sha256:decision', packageId: null, stationId: null
    }, hasEvidenceManifest: false, evidenceStatus: 'disabled',
    evidenceManifestReference: null, evidenceTotalBytes: 0, retentionExpiresAtUtc: null,
    evidenceMessage: '本机替身未生成现场证据。'
  };
}

function runtimeState(mode) {
  const busy = mode === 'formal' || mode === 'continuous';
  const sessionType = mode === 'formal' ? 'WorkspaceFormalRun' : mode === 'continuous' ? 'ContinuousInspection' : null;
  return {
    projectId, status: busy ? 'Running' : 'Idle', isBusy: busy,
    sessionId: busy ? sessionId : null, startedAt: busy ? '2026-07-26T02:00:00Z' : null,
    stoppedAt: null, clientSnapshotId: busy ? snapshotId : null,
    persistenceRevision: busy ? 12 : null, canonicalFlowHash: busy ? 'sha256:flow' : null,
    decisionConfigurationHash: busy ? 'sha256:decision' : null,
    executionSource: busy ? 'PersistedProject' : null, sessionType
  };
}

function fulfill(route, status, body, contentType = 'application/json; charset=utf-8') {
  return route.fulfill({ status, contentType, body: typeof body === 'string' ? body : JSON.stringify(body) });
}

async function installFixture(page, state) {
  await page.route('**/health', route => fulfill(route, 200, { status: 'Healthy', source: 'f05-webview2-fixture' }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    state.requests.push({ method: request.method(), path: `${url.pathname}${url.search}` });
    if (url.pathname === '/api/auth/setup-status') {
      return fulfill(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6,
        requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
    }
    if (url.pathname === '/api/auth/me') {
      return fulfill(route, 200, { userId: `fixture-${state.role.toLowerCase()}`, username: `fixture-${state.role.toLowerCase()}`, role: state.role });
    }
    if (url.pathname === '/api/projects' && request.method() === 'GET') return fulfill(route, 200, [project()]);
    if (url.pathname === `/api/projects/${projectId}`) return fulfill(route, 200, project());
    if (url.pathname === '/api/cameras/bindings') return fulfill(route, 200, [{ id: 'camera-f05', displayName: '本机替身相机', isEnabled: true, connectionStatus: 'Connected' }]);
    if (url.pathname === `/api/inspection/realtime/${projectId}/state`) return fulfill(route, 200, runtimeState(state.runMode));
    if (url.pathname === '/api/inspection/admission') {
      return fulfill(route, 200, { allowed: true, code: null, message: 'admitted', projectId, clientSnapshotId: snapshotId,
        projectPersistenceRevision: 12, canonicalFlowHash: 'sha256:flow', decisionConfigurationHash: 'sha256:decision', violations: [] });
    }
    if (url.pathname === '/api/inspection/realtime/start') {
      state.runMode = 'continuous';
      return fulfill(route, 200, { projectId, clientSnapshotId: snapshotId, persistenceRevision: 12,
        canonicalFlowHash: 'sha256:flow', decisionConfigurationHash: 'sha256:decision', runMode: 'canonical-project',
        cameraId: 'camera-f05', sessionId, sessionType: 'ContinuousInspection' });
    }
    if (url.pathname === `/api/inspection/realtime/${projectId}/events`) {
      state.sseConnections += 1;
      return fulfill(route, 200, [
        `id: 1\nevent: stateChanged\ndata: ${JSON.stringify({ projectId, sessionId, oldState: 'Starting', newState: 'Running', errorMessage: null, timestamp: '2026-07-26T02:00:01Z', isSnapshot: false, startedAt: '2026-07-26T02:00:00Z', stoppedAt: null, sessionType: 'ContinuousInspection' })}\n\n`,
        `id: 2\nevent: resultProduced\ndata: ${JSON.stringify({ projectId, sessionId, resultId, status: 'OK', executionOutcome: 'Succeeded', decisionOutcome: 'OK', defectCount: 0, processingTimeMs: 18, errorMessage: null, timestamp: '2026-07-26T02:00:02Z' })}\n\n`
      ].join(''), 'text/event-stream');
    }
    if (url.pathname === '/api/inspection/realtime/stop') {
      state.stopRequests += 1; state.runMode = 'idle'; return fulfill(route, 200, { projectId, message: 'stopped' });
    }
    if (url.pathname === `/api/inspection/history/${projectId}/${resultId}`) return fulfill(route, 200, localResultDetail());
    if (url.pathname === `/api/inspection/history/${projectId}`) return fulfill(route, 200, { items: [localResultSummary()], totalCount: 1, pageIndex: 0, pageSize: 20 });
    if (url.pathname === '/api/stations') return fulfill(route, 200, [station()]);
    if (url.pathname === '/api/stations/summary') return fulfill(route, 200, { totalStations: 1, onlineStations: 1, offlineStations: 0,
      runningStations: 1, faultedStations: 0, alertCount: 0, warningStations: 0, criticalStations: 0,
      outcomeStatistics: outcomeStatistics(), averageExecutionTimeMs: 24.5, offlineThresholdSeconds: 15,
      updatedAtUtc: '2026-07-26T02:00:00Z' });
    if (url.pathname === '/api/stations/statistics') return fulfill(route, 200, { fromUtc: null, toUtc: null,
      outcomeStatistics: outcomeStatistics(), averageExecutionTimeMs: 24.5, byStation: [], byDiagnosticCode: [], hourlyTrend: [] });
    if (url.pathname === `/api/stations/${stationId}/results`) return fulfill(route, 200, [stationResult()]);
    if (url.pathname === `/api/stations/${stationId}/health`) return fulfill(route, 200, [{ schemaVersion: 2, stationId, sequenceId: 10,
      messageId: 'health-f05', runtimeState: 2, processUptimeSeconds: 3600, cpuUsagePercent: 32.5,
      workingSetMb: 256, privateMemoryMb: 220, diskFreeMb: 20480, spoolPendingCount: 0, spoolBytes: 0,
      cameraStatusSummary: 'Ready', plcStatusSummary: 'Fixture', currentPackageId: 'pkg-f05', currentPackageHealth: 'Healthy',
      lastErrorCode: null, lastErrorMessage: null, createdAtUtc: '2026-07-26T02:00:00Z' }]);
    if (url.pathname === `/api/stations/${stationId}`) return fulfill(route, state.role === 'Admin' ? 200 : 403,
      state.role === 'Admin' ? station() : { error: 'StationAdminRequired' });
    if (url.pathname === `/api/stations/${stationId}/logs`) return fulfill(route, state.role === 'Admin' ? 200 : 403,
      state.role === 'Admin' ? [] : { error: 'StationAdminRequired' });
    if (url.pathname === `/api/stations/${stationId}/commands`) {
      if (request.method() === 'POST') {
        state.stationWrites.push({ method: 'POST', path: url.pathname });
        return fulfill(route, state.role === 'Admin' ? 200 : 403, state.role === 'Admin'
          ? { schemaVersion: 2, commandId: crypto.randomUUID(), stationId, commandType: 'Ping', payloadJson: '{}',
              createdAtUtc: '2026-07-26T02:00:00Z', expiresAtUtc: '2026-07-26T03:00:00Z', issuedBy: 'fixture-admin',
              correlationId: 'f05', status: 'Created', progressPercent: 0, deliveredAtUtc: null, acceptedAtUtc: null,
              startedAtUtc: null, completedAtUtc: null, resultMessage: null, errorCode: null }
          : { error: 'StationAdminRequired' });
      }
      return fulfill(route, state.role === 'Admin' ? 200 : 403, state.role === 'Admin' ? [] : { error: 'StationAdminRequired' });
    }
    if (url.pathname === '/api/stations/audit') return fulfill(route, state.role === 'Admin' ? 200 : 403, state.role === 'Admin' ? [] : { error: 'StationAdminRequired' });
    if (url.pathname === '/api/station-packages') return fulfill(route, state.role === 'Admin' ? 200 : 403, state.role === 'Admin' ? [{
      schemaVersion: 2, packageId: 'pkg-f05', packageName: 'F05 隔离运行包', packageVersion: '1.0.0', packageKind: 'Production',
      flowHash: 'sha256:package', createdBy: 'fixture-admin', minStationVersion: '2.0.0', requiredOperators: [],
      sizeBytes: 4096, sha256: 'a'.repeat(64), createdAtUtc: '2026-07-26T01:00:00Z'
    }] : { error: 'StationAdminRequired' });
    if (url.pathname === `/api/stations/${stationId}/identity`) {
      state.stationWrites.push({ method: 'PATCH', path: url.pathname });
      return fulfill(route, state.role === 'Admin' ? 200 : 403, state.role === 'Admin' ? station() : { error: 'StationAdminRequired' });
    }
    if (url.pathname === `/api/stations/${stationId}/deploy-package`) {
      state.stationWrites.push({ method: 'POST', path: url.pathname });
      return fulfill(route, state.role === 'Admin' ? 200 : 403, state.role === 'Admin' ? { commandId: crypto.randomUUID() } : { error: 'StationAdminRequired' });
    }
    if (url.pathname === '/api/stations/results') return fulfill(route, 200, { items: [stationResult()], totalCount: 1, pageIndex: 0, pageSize: 20 });
    return fulfill(route, 404, { error: `Unhandled F05 fixture endpoint: ${request.method()} ${url.pathname}` });
  });
}

async function captureScene(page, evidenceDirectory, id, sourceSha, scenes) {
  const buffer = await page.screenshot({ type: 'png' });
  const artifact = writePngEvidence(evidenceDirectory, `real-webview2-f05-${id}.png`, buffer);
  const pageState = await page.evaluate(() => ({
    hash: window.location.hash, capability: document.querySelector('[data-capability]')?.getAttribute('data-capability') || null,
    shellCount: document.querySelectorAll('[data-product-shell="ready"]').length,
    productMounts: window.__STUDIO_UI_DIAGNOSTICS__?.mountCount ?? null,
    productRoot: window.__STUDIO_UI_DIAGNOSTICS__?.activeRoot ?? null,
    horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    verticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight
  }));
  assert(pageState.shellCount === 1 && pageState.productMounts === 1 && pageState.productRoot === 'studio-ui',
    `F05 route did not retain a single Product shell: ${JSON.stringify(pageState)}`);
  assert(pageState.horizontalOverflow <= 1, `F05 route has horizontal overflow: ${JSON.stringify(pageState)}`);
  return { ...artifact, sourceSha, scenes, pageState };
}

async function setHash(page, hash, selector) {
  await page.evaluate(route => { window.location.hash = `#${route}`; }, hash);
  await page.waitForSelector(selector, { state: 'visible', timeout: 30_000 });
}

async function main() {
  const cdpPort = Number(requiredEnvironment('CV_CDP_PORT'));
  const webPort = Number(requiredEnvironment('CV_WEB_PORT'));
  const token = requiredEnvironment('CV_SMOKE_TOKEN');
  const user = requiredEnvironment('CV_SMOKE_USER');
  const evidenceDirectory = requiredEnvironment('CV_EVIDENCE_DIR');
  const sourceSha = requiredEnvironment('CV_STUDIO_UI_SOURCE_SHA');
  const runName = String(process.env.CV_STUDIO_UI_RUN_NAME || 'f05-e2e').trim();
  const outputName = `studio-ui-webview2-f05-${runName.replace(/[^a-z0-9_.-]+/gi, '-')}.json`;
  const state = { role: 'Engineer', runMode: 'formal', requests: [], stationWrites: [], stopRequests: 0, sseConnections: 0 };
  const evidence = {
    schemaVersion: 1, status: 'running', runName, sourceSha, dataSource: 'REAL_WEBVIEW2_WITH_LOCAL_F05_FIXTURE',
    FIELD_ACCEPTANCE: 'NOT_A_FIELD_ACCEPTANCE', capturedAtUtc: new Date().toISOString(), scenarios: {}, fixture: {
      REAL_CAMERA: 'NOT_PERFORMED', REAL_PLC: 'NOT_PERFORMED', REAL_STATION: 'NOT_PERFORMED'
    }
  };
  let browser;
  let runtimeErrors;
  try {
    const connected = await connectToDesktopWebView2(cdpPort);
    browser = connected.browser;
    const { context, page } = connected;
    runtimeErrors = captureRuntimeErrors(page);
    const responses = [];
    page.on('response', response => responses.push({ url: response.url(), status: response.status(), method: response.request().method() }));
    await seedAuthenticatedSession(page, webPort, token, user);
    await installFixture(page, state);
    const origin = `http://localhost:${webPort}`;
    await page.goto(`${origin}/studio/index.html#/inspection`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    await page.waitForSelector('[data-testid="inspection-projects-page"]', { state: 'visible', timeout: 30_000 });
    const startup = await page.evaluate(() => ({ ...window.__CLEARVISION_STARTUP__?.featureFlags }));
    assert(startup['Studio2.InspectionRun'] === true && startup['Studio2.StationsRead'] === true,
      `Isolated F05 feature flags were not applied: ${JSON.stringify(startup)}`);
    evidence.scenarios.inspectionProjects = await captureScene(page, evidenceDirectory, 'inspection-projects', sourceSha,
      ['shell', 'inspection-project-selection', 'lazy-deep-hash']);

    await page.getByTestId('inspection-project-open').click();
    await page.waitForSelector('[data-testid="inspection-run-page"]', { state: 'visible', timeout: 30_000 });
    const formalBlocked = await page.getByTestId('inspection-start').isDisabled();
    assert(formalBlocked, 'Continuous inspection start remained enabled while Formal Run owns the project.');
    evidence.scenarios.formalContinuousMutualExclusion = {
      startDisabled: formalBlocked,
      startRequestCount: state.requests.filter(item => item.path === '/api/inspection/realtime/start').length,
      screenshot: await captureScene(page, evidenceDirectory, 'inspection-formal-occupied', sourceSha, ['formal-run-mutual-exclusion'])
    };

    state.runMode = 'idle';
    await setHash(page, '/inspection', '[data-testid="inspection-projects-page"]');
    await page.getByTestId('inspection-project-open').click();
    await page.waitForSelector('[data-testid="inspection-run-page"]', { state: 'visible', timeout: 30_000 });
    await page.getByTestId('inspection-start').click();
    await page.waitForSelector('[data-testid="inspection-latest-result"]', { state: 'visible', timeout: 30_000 });
    assert(state.sseConnections >= 1, 'Continuous inspection did not establish the expected SSE connection.');
    evidence.scenarios.continuousInspection = {
      admissionRequests: state.requests.filter(item => item.path === '/api/inspection/admission').length,
      startRequests: state.requests.filter(item => item.path === '/api/inspection/realtime/start').length,
      sseConnections: state.sseConnections,
      screenshot: await captureScene(page, evidenceDirectory, 'inspection-running', sourceSha, ['continuous-start', 'sse-result'])
    };
    await page.getByRole('link', { name: '查看检测结果' }).click();
    await page.waitForSelector('[data-capability="results-read"]', { state: 'visible', timeout: 30_000 });
    assert(state.stopRequests === 1, `Leave Guard did not stop the ContinuousInspection session: ${state.stopRequests}`);
    evidence.scenarios.resultsHandoff = {
      stopRequests: state.stopRequests,
      route: await page.evaluate(() => window.location.hash),
      screenshot: await captureScene(page, evidenceDirectory, 'results-handoff', sourceSha, ['results-redirect', 'leave-guard-stop'])
    };

    state.role = 'Engineer';
    const writeCountBeforeEngineer = state.stationWrites.length;
    await setHash(page, `/stations/${stationId}`, '[data-capability="stations-read-detail"]');
    assert(await page.locator('[data-capability="station-admin-control"]').count() === 0,
      'Engineer route mounted the Station Admin control domain.');
    assert(state.stationWrites.length === writeCountBeforeEngineer, 'Engineer station route emitted a write.');
    evidence.scenarios.stationEngineerReadOnly = {
      adminControls: await page.locator('[data-capability="station-admin-control"]').count(),
      writes: state.stationWrites.length - writeCountBeforeEngineer,
      screenshot: await captureScene(page, evidenceDirectory, 'stations-engineer', sourceSha, ['station-engineer-read-only', 'deep-hash'])
    };

    state.role = 'Admin';
    await page.reload({ waitUntil: 'domcontentloaded', timeout: 45_000 });
    await page.waitForSelector('[data-capability="station-admin-control"]', { state: 'visible', timeout: 30_000 });
    const admin = page.locator('[data-capability="station-admin-control"]');
    await admin.getByTestId('station-issue-command').click();
    await admin.getByLabel('工作站名称').fill('F05 隔离工作站（Admin）');
    await admin.getByTestId('station-save-identity').click();
    await admin.getByLabel('生产运行包').selectOption('pkg-f05');
    await admin.getByTestId('station-deploy-package').click();
    assert(state.stationWrites.some(item => item.method === 'POST' && item.path.endsWith('/commands')) &&
      state.stationWrites.some(item => item.method === 'PATCH' && item.path.endsWith('/identity')) &&
      state.stationWrites.some(item => item.method === 'POST' && item.path.endsWith('/deploy-package')),
    `Admin Station journey did not issue the three authority writes: ${JSON.stringify(state.stationWrites)}`);
    evidence.scenarios.stationAdmin = {
      writes: state.stationWrites,
      screenshot: await captureScene(page, evidenceDirectory, 'stations-admin', sourceSha, ['station-admin-command', 'identity', 'package-deploy'])
    };

    const afterLeaveRequestIndex = state.requests.length;
    await setHash(page, '/results?source=local&projectId=' + projectId, '[data-capability="results-read"]');
    await new Promise(resolve => setTimeout(resolve, 1200));
    const afterLeave = state.requests.slice(afterLeaveRequestIndex);
    assert(!afterLeave.some(item => item.path.startsWith('/api/stations/')),
      `Station requests continued after route disposal: ${JSON.stringify(afterLeave)}`);
    evidence.scenarios.stationLeaveDisposal = { postLeaveStationRequests: afterLeave.filter(item => item.path.startsWith('/api/stations/')) };

    evidence.browserDpi = await readBrowserDpiEvidence(page, context);
    evidence.resources = {
      requested: runtimeErrors.requests.map(item => ({ method: item.method, url: item.url })),
      studioAssets: runtimeErrors.requests.filter(item => new URL(item.url).pathname.startsWith('/studio/assets/')),
      responses: responses.filter(item => new URL(item.url).origin === origin),
      duplicateStudioAssets: [...new Set(runtimeErrors.requests
        .filter(item => new URL(item.url).pathname.startsWith('/studio/assets/'))
        .map(item => item.url).filter((url, index, all) => all.indexOf(url) !== index))]
    };
    assert(evidence.resources.duplicateStudioAssets.length === 0,
      `Lazy chunks loaded more than once: ${JSON.stringify(evidence.resources.duplicateStudioAssets)}`);
    assert(runtimeErrors.consoleErrors.length === 0, `WebView2 console errors: ${runtimeErrors.consoleErrors.join(' | ')}`);
    assert(runtimeErrors.pageErrors.length === 0, `WebView2 page errors: ${runtimeErrors.pageErrors.join(' | ')}`);
    assert(runtimeErrors.requestFailures.length === 0, `WebView2 request failures: ${JSON.stringify(runtimeErrors.requestFailures)}`);
    assert(!evidence.resources.responses.some(item => item.status === 404),
      `WebView2 response 404: ${JSON.stringify(evidence.resources.responses.filter(item => item.status === 404))}`);
    evidence.status = 'pass';
    evidence.completedAtUtc = new Date().toISOString();
    const output = writeJsonEvidence(evidenceDirectory, outputName, evidence);
    process.stdout.write(`${JSON.stringify({ ok: true, output, runName })}\n`);
  } catch (error) {
    evidence.status = 'fail'; evidence.completedAtUtc = new Date().toISOString();
    evidence.error = error?.stack || error?.message || String(error);
    if (runtimeErrors) evidence.runtimeErrors = runtimeErrors;
    writeJsonEvidence(evidenceDirectory, outputName, evidence);
    throw error;
  } finally {
    await browser?.close();
  }
}

main().catch(error => { process.stderr.write(`${error?.stack || error}\n`); process.exitCode = 1; });
