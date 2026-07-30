'use strict';

const fs = require('node:fs');

const {
  assert,
  captureRuntimeErrors,
  connectToDesktopWebView2,
  readBrowserDpiEvidence,
  requiredEnvironment,
  runDesktopRuntimeProbe,
  seedAuthenticatedSession,
  writeJsonEvidence,
  writePngEvidence
} = require('./webview2-harness.cjs');

function metadata(value, name) {
  return value?.[name] ?? value?.[name[0].toUpperCase() + name.slice(1)];
}

function waitForBrowserDisconnect(browser, timeout = 30_000) {
  if (!browser.isConnected()) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error('Desktop Host did not dispose WebView2 within the shutdown timeout.'));
    }, timeout);
    browser.once('disconnected', () => {
      clearTimeout(timer);
      resolve();
    });
  });
}

async function waitForFile(filePath, timeout = 80_000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (fs.existsSync(filePath)) return;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error(`Desktop Host did not acknowledge clean process exit: ${filePath}`);
}

async function coordinateDesktopHostShutdown(browser, processId) {
  const signalPath = requiredEnvironment('CV_DESKTOP_HOST_CLOSE_SIGNAL');
  const acknowledgementPath = `${signalPath}.closed`;
  const disconnected = waitForBrowserDisconnect(browser);
  fs.writeFileSync(signalPath, `${JSON.stringify({ processId, requestedAtUtc: new Date().toISOString() })}\n`, 'utf8');
  await disconnected;
  await waitForFile(acknowledgementPath);
  return {
    mode: 'winforms-wm-close',
    processId,
    webView2Disconnected: true,
    desktopProcessExited: true
  };
}

const replayIdentityKeys = new Set([
  'sessionId', 'projectId', 'planId', 'planHash', 'planRunId', 'planTerminalSequence',
  'runId', 'buildId', 'buildRunId', 'buildRunStatus', 'buildTerminalSequence',
  'clientOperationId', 'buildClientOperationId', 'operationId', 'submittedBuildFingerprint',
  'answerRevision', 'resourceRevision', 'revision', 'persistenceRevision',
  'canonicalFlowHash', 'targetKind', 'resourceKey', 'canonicalId'
]);

function compactIdentity(value, depth = 0, target = {}) {
  if (!value || typeof value !== 'object' || depth > 8) return target;
  if (Array.isArray(value)) {
    for (const item of value) compactIdentity(item, depth + 1, target);
    return target;
  }
  for (const [key, nested] of Object.entries(value)) {
    if (replayIdentityKeys.has(key) && ['string', 'number', 'boolean'].includes(typeof nested)) {
      const values = target[key] ??= [];
      if (!values.includes(nested)) values.push(nested);
    }
    compactIdentity(nested, depth + 1, target);
  }
  return target;
}

function compactReplayAudit(body) {
  if (!body || !Array.isArray(body.events)) return null;
  return {
    summary: body.summary ? {
      runId: metadata(body.summary, 'runId'),
      status: metadata(body.summary, 'status'),
      lastSequence: metadata(body.summary, 'lastSequence'),
      eventCount: metadata(body.summary, 'eventCount')
    } : null,
    events: body.events.map(event => ({
      sequence: metadata(event, 'sequence'),
      eventType: metadata(event, 'eventType'),
      stage: metadata(event, 'stage'),
      status: metadata(event, 'status'),
      identity: compactIdentity(metadata(event, 'payload'))
    }))
  };
}

async function requestJson(webPort, token, requestPath, options = {}) {
  const method = String(options.method || 'GET').toUpperCase();
  const response = await fetch(`http://127.0.0.1:${webPort}${requestPath}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(options.body === undefined ? {} : { 'Content-Type': 'application/json' })
    },
    body: options.body === undefined ? undefined : JSON.stringify(options.body)
  });
  const text = await response.text();
  let body = null;
  try { body = text ? JSON.parse(text) : null; } catch { body = { raw: text }; }
  const expected = options.expectedStatuses || [200];
  assert(expected.includes(response.status),
    `${method} ${requestPath} returned ${response.status}: ${text.slice(0, 500)}`);
  return { status: response.status, body };
}

async function waitForOwnerPhase(page, phases, timeout = 60_000) {
  const selector = phases.map(phase => `[data-ai-owner-phase="${phase}"]`).join(',');
  try {
    await page.waitForSelector(selector, { state: 'visible', timeout });
  } catch (error) {
    const owner = page.locator('[data-ai-owner-phase]');
    const actualPhase = await owner.getAttribute('data-ai-owner-phase').catch(() => null);
    const diagnostics = await owner.evaluate(element => Object.fromEntries(
      [...element.attributes]
        .filter(attribute => attribute.name.startsWith('data-ai-owner-'))
        .map(attribute => [attribute.name, attribute.value]))).catch(() => ({}));
    const publicText = await owner.innerText().catch(() => 'AI owner projection unavailable.');
    throw new Error(
      `Timed out waiting for AI phase ${phases.join(', ')}; actual phase is ${actualPhase || 'missing'}. ` +
      `Owner diagnostics: ${JSON.stringify(diagnostics)}. Public projection: ${publicText.slice(0, 1_500)}`,
      { cause: error });
  }
  return page.locator('[data-ai-owner-phase]').getAttribute('data-ai-owner-phase');
}

async function fillPendingParameters(page) {
  const panel = page.locator('[data-ai-pending-parameters]');
  const selects = panel.locator('select');
  for (let index = 0; index < await selects.count(); index += 1) {
    const select = selects.nth(index);
    const values = await select.locator('option:not([disabled])').evaluateAll(options =>
      options.map(option => option.value).filter(Boolean));
    assert(values.length > 0, 'A pending select parameter exposed no canonical option.');
    await select.selectOption(values[0]);
  }
  const inputs = panel.locator('input[type="number"], input[type="text"]');
  for (let index = 0; index < await inputs.count(); index += 1) {
    const input = inputs.nth(index);
    const type = await input.getAttribute('type');
    const placeholder = await input.getAttribute('placeholder') || '';
    const suggested = placeholder.match(/-?\d+(?:\.\d+)?/)?.[0];
    await input.fill(type === 'number' ? suggested || '1' : placeholder.replace(/^建议[:：]\s*/, '') || '已确认');
  }
  await panel.getByRole('button', { name: '确认全部参数' }).click();
}

async function resolveCandidateInputs(page) {
  for (let attempt = 0; attempt < 10; attempt += 1) {
    const phase = await page.locator('[data-ai-owner-phase]').getAttribute('data-ai-owner-phase');
    if (phase === 'build-ready') return;
    if (phase === 'parameters-pending') {
      await fillPendingParameters(page);
      await waitForOwnerPhase(page, ['build-blocked', 'resources-pending', 'build-ready']);
      continue;
    }
    if (phase === 'resources-pending') {
      const panel = page.locator('[data-ai-resource-decisions]');
      const selects = panel.locator('select');
      assert(await selects.count() > 0, 'The real Build exposed only unsupported resources; camera binding could not close.');
      for (let index = 0; index < await selects.count(); index += 1) {
        const select = selects.nth(index);
        const values = await select.locator('option:not([disabled])').evaluateAll(options =>
          options.map(option => option.value).filter(Boolean));
        assert(values.length > 0, 'The camera resource selector exposed no enabled authority candidate.');
        await select.selectOption(values[0]);
      }
      await panel.getByRole('button', { name: '保存资源决策' }).click();
      await waitForOwnerPhase(page, ['build-blocked', 'build-ready']);
      continue;
    }
    if (phase === 'build-blocked') {
      await page.getByRole('button', { name: '重新校验' }).click();
      await waitForOwnerPhase(page, ['parameters-pending', 'resources-pending', 'build-blocked', 'build-ready']);
      continue;
    }
    if (['revalidating', 'building', 'validating', 'build-starting', 'recovering'].includes(phase || '')) {
      await waitForOwnerPhase(page, ['parameters-pending', 'resources-pending', 'build-blocked', 'build-ready', 'build-failed']);
      continue;
    }
    throw new Error(`Real Build stopped in unsupported phase ${phase}.`);
  }
  throw new Error('Real Build did not close parameter and camera resource inputs within 10 transitions.');
}

async function main() {
  const cdpPort = Number.parseInt(requiredEnvironment('CV_CDP_PORT'), 10);
  const webPort = Number.parseInt(requiredEnvironment('CV_WEB_PORT'), 10);
  const token = requiredEnvironment('CV_SMOKE_TOKEN');
  const user = requiredEnvironment('CV_SMOKE_USER');
  const evidenceDirectory = requiredEnvironment('CV_EVIDENCE_DIR');
  const sourceSha = requiredEnvironment('CV_STUDIO_UI_SOURCE_SHA');
  const executable = requiredEnvironment('CV_STUDIO_UI_DESKTOP_EXECUTABLE');
  const runName = String(process.env.CV_STUDIO_UI_RUN_NAME || 'f06-g4-debug').trim();
  const evidence = {
    schemaVersion: 'f06-g4-real-webview2.v1', status: 'running', runName, sourceSha,
    MODEL_MODE: 'RULE_FALLBACK', REAL_LLM_PRODUCT_QUALITY: 'NOT_EVALUATED',
    DATA_SOURCE: 'REAL_ASPNETCORE_WEBVIEW2_WITH_HANDOFF_AND_RESPONSE_LOSS_FAULT_INJECTION',
    capturedAtUtc: new Date().toISOString(), authority: {}, requests: {}, screenshots: {}, runtime: {}
  };
  let browser;
  let page;
  let runtimeErrors;
  const apiResponses = [];
  const httpFailures = [];
  try {
    const connected = await connectToDesktopWebView2(cdpPort);
    browser = connected.browser;
    const { context } = connected;
    page = connected.page;
    runtimeErrors = captureRuntimeErrors(page);
    page.on('response', async response => {
      const url = new URL(response.url());
      if (response.status() >= 400) {
        httpFailures.push({
          method: response.request().method(),
          path: url.pathname + url.search,
          status: response.status()
        });
      }
      if (!/^\/api\/(?:ai\/(?:sessions(?:\/[^/]+)?|agent-plan-runs|agent-runs(?:\/[^/]+)?|operations\/|handoffs(?:\/.*)?)|projects(?:\/.*)?)/.test(url.pathname) ||
          /\/events$/.test(url.pathname)) return;
      const text = await response.text().catch(error => `RESPONSE_READ_FAILED: ${error.message}`);
      let body = null;
      try { body = JSON.parse(text); } catch { /* Raw response evidence remains available below. */ }
      apiResponses.push({
        method: response.request().method(), path: url.pathname + url.search,
        status: response.status(), body: text.slice(0, 200_000),
        replayAudit: compactReplayAudit(body)
      });
    });
    await seedAuthenticatedSession(page, webPort, token, user);

    const cameraId = 'f06-evidence-camera-01';
    const cameraWrite = await requestJson(webPort, token, '/api/cameras/bindings', {
      method: 'PUT',
      body: {
        bindings: [{ id: cameraId, displayName: 'F06 真实链路顶视相机', isEnabled: true }],
        activeCameraId: cameraId
      }
    });
    const cameraCandidates = await requestJson(webPort, token, '/api/ai/resource-candidates/camera-bindings');
    const candidateItems = Array.isArray(cameraCandidates.body) ? cameraCandidates.body : [];
    assert(candidateItems.some(item => metadata(item, 'id') === cameraId && metadata(item, 'isEnabled') === true),
      `Camera authority did not return the isolated binding: ${JSON.stringify(candidateItems)}`);
    evidence.authority.camera = {
      writeStatus: cameraWrite.status,
      candidateFields: candidateItems[0] ? Object.keys(candidateItems[0]).sort() : [],
      candidateCount: candidateItems.length,
      selectedId: cameraId
    };

    let buildPostCount = 0;
    const realBuildResponses = [];
    await page.route('**/api/ai/agent-runs', async route => {
      buildPostCount += 1;
      const response = await route.fetch();
      const body = await response.json();
      realBuildResponses.push({
        status: response.status(), runId: metadata(body, 'runId'),
        sessionId: metadata(body, 'sessionId'), operation: metadata(body, 'operation')
      });
      if (buildPostCount === 1) {
        const runId = metadata(body, 'runId');
        assert(runId, 'The first real Build response did not contain a run identity.');
        const cancel = await requestJson(webPort, token, `/api/ai/agent-runs/${runId}/cancel`, {
          method: 'POST', body: {}, expectedStatuses: [200, 409]
        });
        evidence.authority.cancel = { runId, status: cancel.status, response: cancel.body };
        await route.fulfill({ response, json: body });
        return;
      }
      await route.fulfill({ response, json: { ...body, sessionId: 'response-loss-simulated-session' } });
    });

    const origin = `http://localhost:${webPort}`;
    await page.goto(`${origin}/studio/index.html#/ai`, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    const startup = await page.evaluate(() => ({ ...window.__CLEARVISION_STARTUP__?.featureFlags }));
    assert(startup['Studio2.AiWorkbench'] === true, `AI feature flag was not enabled: ${JSON.stringify(startup)}`);
    assert(startup['Studio2.Workspace'] === true, `Workspace feature flag was not enabled: ${JSON.stringify(startup)}`);
    await page.waitForSelector('[data-ai-owner-phase="idle"]', { state: 'visible', timeout: 30_000 });
    const task = page.getByRole('textbox', { name: '任务描述' });
    assert(await task.evaluate(element => element === document.activeElement), 'Task composer did not receive initial focus.');
    await task.fill('使用已配置顶视相机检测金属冲压件表面划伤，划伤长度超过 2 毫米判定 NG，输出缺陷位置、长度和 OK/NG。');
    await page.getByRole('button', { name: '理解并规划任务' }).click();
    let phase = await waitForOwnerPhase(page, ['clarifying', 'plan-ready', 'plan-blocked']);
    if (phase !== 'plan-ready') {
      const resolvableStrategy = page.locator(
        '[data-ai-clarification-panel] input[type="radio"][value="traditional_rule"]');
      assert(await resolvableStrategy.count() === 1,
        `Real Plan stopped in ${phase} without the resolvable strategy answer.`);
      await resolvableStrategy.check();
      await page.getByRole('button', { name: '确认回答并重新检查' }).click();
      phase = await waitForOwnerPhase(page, ['plan-ready', 'plan-blocked']);
    }
    assert(phase === 'plan-ready', `Real Plan did not reach ready: ${phase}.`);

    await page.getByRole('button', { name: '开始构建' }).click();
    phase = await waitForOwnerPhase(page, ['build-cancelled', 'build-failed', 'parameters-pending', 'resources-pending', 'build-ready']);
    assert(phase === 'build-cancelled', `The real cancel race did not reserve cancelled terminal state: ${phase}.`);
    evidence.screenshots.cancelled = writePngEvidence(
      evidenceDirectory, `f06-${runName}-cancelled.png`, await page.screenshot({ type: 'png', animations: 'disabled' }));

    await page.getByRole('button', { name: '重新构建' }).click();
    await waitForOwnerPhase(page, ['parameters-pending', 'resources-pending', 'build-blocked', 'build-ready', 'build-failed']);
    await resolveCandidateInputs(page);
    assert(await page.locator('[data-ai-owner-phase="build-ready"]').count() === 1,
      'The second real Build did not reach the read-only ready gate.');
    evidence.screenshots.ready = writePngEvidence(
      evidenceDirectory, `f06-${runName}-ready.png`, await page.screenshot({ type: 'png', animations: 'disabled' }));

    const requestPath = item => {
      const url = new URL(item.url);
      return url.pathname + url.search;
    };
    const isProjectWrite = item =>
      item.method === 'POST' && requestPath(item) === '/api/projects' ||
      item.method === 'PUT' && /^\/api\/projects\/[0-9a-f-]{36}$/i.test(requestPath(item));
    assert(runtimeErrors.requests.filter(isProjectWrite).length === 0,
      'The ready Build wrote a Project before the user requested handoff and save.');

    await page.getByRole('button', { name: '交接到工作区审核' }).click();
    await page.waitForURL(url =>
      /#\/projects\/new\/workspace\?handoff=[0-9a-f]{32}$/i.test(url.href),
    { timeout: 60_000 });
    assert(!/candidateFlow|fingerprint/i.test(page.url()),
      `The Workspace route leaked candidate content: ${page.url()}`);
    await page.waitForSelector('[data-workspace-handoff-phase="workspace-staged-unsaved"]', {
      state: 'visible', timeout: 60_000
    });
    assert(await page.locator('[data-ai-owner-phase]').count() === 0,
      'The AI owner remained mounted after navigating to Workspace.');
    assert(await page.locator('[data-workspace-project-id="new"]').count() === 1,
      'The new handoff target acquired a Project id before explicit save.');
    assert(runtimeErrors.requests.filter(isProjectWrite).length === 0,
      'Workspace handoff staged a candidate by writing a Project before explicit save.');
    evidence.screenshots.staged = writePngEvidence(
      evidenceDirectory, `f06-${runName}-workspace-staged.png`,
      await page.screenshot({ type: 'png', animations: 'disabled' }));

    await page.getByLabel('工程名称').fill('F06 G4 真实 WebView2 交接工程');
    await page.getByLabel('工程描述').fill('真实 artifact 接收后由用户显式创建并保存。');
    await page.getByTestId('workspace-save').click();
    await page.waitForURL(url =>
      /#\/projects\/[0-9a-f-]{36}\/workspace$/i.test(url.href),
    { timeout: 60_000 });
    const createdProjectId = new URL(page.url()).hash.match(
      /^#\/projects\/([0-9a-f-]{36})\/workspace$/i)?.[1];
    assert(createdProjectId, `Explicit save did not navigate to a server-issued Project: ${page.url()}`);
    await page.waitForSelector(`[data-workspace-project-id="${createdProjectId}"]`, {
      state: 'visible', timeout: 60_000
    });
    await page.waitForSelector(
      `[data-workspace-project-id="${createdProjectId}"]` +
      '[data-workspace-persistence-phase="saved"]' +
      '[data-workspace-dirty="false"]' +
      '[data-workspace-handoff-phase="workspace-saved"]',
      { state: 'visible', timeout: 60_000 }
    );
    evidence.screenshots.saved = writePngEvidence(
      evidenceDirectory, `f06-${runName}-workspace-saved.png`,
      await page.screenshot({ type: 'png', animations: 'disabled' }));

    const sessionRequests = runtimeErrors.requests.filter(item => /\/api\/ai\/sessions$/.test(new URL(item.url).pathname));
    const sessionResponse = await page.request.get(`${origin}/api/ai/sessions`, {
      headers: { Authorization: `Bearer ${token}` }
    });
    assert(sessionResponse.ok(), `Session list returned ${sessionResponse.status()}.`);
    const sessions = await sessionResponse.json();
    const activeSession = Array.isArray(sessions) ? sessions[0] : metadata(sessions, 'items')?.[0];
    const activeSessionId = metadata(activeSession, 'sessionId');
    assert(activeSessionId, `Real Session list exposed no session identity: ${JSON.stringify(sessions)}`);
    const detailResponse = await page.request.get(`${origin}/api/ai/sessions/${activeSessionId}`, {
      headers: { Authorization: `Bearer ${token}` }
    });
    const detail = await detailResponse.json();
    const snapshot = metadata(detail, 'snapshot');
    assert(Number(metadata(snapshot, 'resourceRevision')) >= 1,
      `Server-managed resourceRevision did not advance: ${JSON.stringify(snapshot)}`);

    const apiRequests = runtimeErrors.requests.map(item => ({ method: item.method, path: requestPath(item) }));
    const authorityViolations = apiRequests.filter(item =>
      /apply-to-canvas|workspace\/consume|project\/save|save-project|flow-canvas|image-canvas/i.test(item.path));
    const operationLookups = apiRequests.filter(item => /\/api\/ai\/operations\/.+kind=build_run/.test(item.path));
    const replays = apiRequests.filter(item => /^\/api\/ai\/agent-runs\/[^/]+$/.test(item.path) && item.method === 'GET');
    const streams = apiRequests.filter(item => /\/api\/ai\/agent-runs\/[^/]+\/events/.test(item.path));
    const handoffCreates = apiRequests.filter(item => item.method === 'POST' && item.path === '/api/ai/handoffs');
    const handoffReads = apiRequests.filter(item => item.method === 'GET' && /^\/api\/ai\/handoffs\/[0-9a-f]{32}$/i.test(item.path));
    const handoffConsumes = apiRequests.filter(item => item.method === 'POST' && /\/consume$/.test(item.path));
    const handoffAcknowledgements = apiRequests.filter(item => item.method === 'POST' && /\/acknowledge$/.test(item.path));
    const projectCreates = apiRequests.filter(item => item.method === 'POST' && item.path === '/api/projects');
    const projectPuts = apiRequests.filter(item => item.method === 'PUT' &&
      item.path === `/api/projects/${createdProjectId}`);
    assert(buildPostCount === 2, `Expected two real Build creates, observed ${buildPostCount}.`);
    assert(operationLookups.length >= 1, 'Response-loss simulation did not trigger durable Build operation lookup.');
    assert(replays.length >= 2 && streams.length >= 1,
      `Real replay/SSE evidence was incomplete: ${JSON.stringify({ replays, streams })}`);
    assert(handoffCreates.length === 1 && handoffReads.length >= 1 &&
      handoffConsumes.length === 1 && handoffAcknowledgements.length === 1,
    `Real artifact create/receive trace was incomplete: ${JSON.stringify({
      handoffCreates, handoffReads, handoffConsumes, handoffAcknowledgements
    })}`);
    assert(projectCreates.length === 1 && projectPuts.length === 1,
      `Explicit new-Project save must use one create and one existing Project PUT: ${JSON.stringify({
        projectCreates, projectPuts
      })}`);
    assert(authorityViolations.length === 0,
      `Forbidden Canvas/Project authority paths were observed: ${JSON.stringify(authorityViolations)}`);
    assert(runtimeErrors.consoleErrors.length === 0 && runtimeErrors.pageErrors.length === 0,
      `WebView2 runtime errors were observed: ${JSON.stringify(runtimeErrors)}`);
    assert(runtimeErrors.requestFailures.length === 0,
      `WebView2 request failures were observed: ${JSON.stringify(runtimeErrors.requestFailures)}`);
    assert(httpFailures.length === 0,
      `WebView2 HTTP failures were observed: ${JSON.stringify(httpFailures)}`);
    const overflow = await page.evaluate(() => Math.max(
      document.documentElement.scrollWidth - document.documentElement.clientWidth,
      document.body.scrollWidth - document.body.clientWidth));
    assert(overflow <= 1, `Real WebView2 AI route overflowed horizontally by ${overflow}px.`);

    evidence.authority.session = {
      sessionId: activeSessionId,
      revision: metadata(snapshot, 'revision'),
      answerRevision: metadata(snapshot, 'answerRevision'),
      resourceRevision: metadata(snapshot, 'resourceRevision'),
      lifecycleState: metadata(snapshot, 'lifecycleState')
    };
    evidence.authority.builds = realBuildResponses;
    evidence.authority.handoff = {
      route: page.url(),
      createdProjectId,
      createCount: handoffCreates.length,
      readCount: handoffReads.length,
      consumeCount: handoffConsumes.length,
      acknowledgeCount: handoffAcknowledgements.length,
      projectCreateCount: projectCreates.length,
      projectPutCount: projectPuts.length
    };
    evidence.requests = {
      total: apiRequests.length, sessionCreates: sessionRequests.length, buildPosts: buildPostCount,
      operationLookups, replays, streams, handoffCreates, handoffReads,
      handoffConsumes, handoffAcknowledgements, projectCreates, projectPuts, authorityViolations
    };
    const native = runDesktopRuntimeProbe(executable);
    evidence.runtime = {
      consoleErrors: runtimeErrors.consoleErrors, pageErrors: runtimeErrors.pageErrors,
      requestFailures: runtimeErrors.requestFailures, httpFailures, horizontalOverflow: overflow,
      dpi: await readBrowserDpiEvidence(page, context), native
    };
    evidence.runtime.hostShutdown = await coordinateDesktopHostShutdown(
      browser,
      native.desktop.processId);
    browser = null;
    evidence.status = 'PASS';
    evidence.capturedAtUtc = new Date().toISOString();
    writeJsonEvidence(evidenceDirectory, `studio-ui-webview2-f06-g4-${runName}.json`, evidence);
    fs.writeFileSync(
      requiredEnvironment('CV_NODE_COMPLETION_SIGNAL'),
      `${JSON.stringify({ status: 'PASS', capturedAtUtc: evidence.capturedAtUtc })}\n`,
      'utf8');
  } catch (error) {
    evidence.status = 'FAIL';
    evidence.error = error?.stack || error?.message || String(error);
    evidence.runtime = {
      ownerPhase: page ? await page.locator('[data-ai-owner-phase]').getAttribute('data-ai-owner-phase').catch(() => null) : null,
      pageUrl: page?.url() ?? null,
      startupFlags: page ? await page.evaluate(() => window.__CLEARVISION_STARTUP__?.featureFlags ?? null).catch(() => null) : null,
      workspaceAttributes: page ? await page.locator('[data-capability="project-workspace"]').evaluate(element =>
        Object.fromEntries([...element.attributes].map(attribute => [attribute.name, attribute.value]))
      ).catch(() => null) : null,
      consoleErrors: runtimeErrors?.consoleErrors ?? [],
      pageErrors: runtimeErrors?.pageErrors ?? [],
      requestFailures: runtimeErrors?.requestFailures ?? [],
      requests: runtimeErrors?.requests?.map(item => ({
        method: item.method,
        path: new URL(item.url).pathname + new URL(item.url).search
      })) ?? [],
      apiResponses,
      httpFailures
    };
    evidence.capturedAtUtc = new Date().toISOString();
    writeJsonEvidence(evidenceDirectory, `studio-ui-webview2-f06-g4-${runName}.json`, evidence);
    throw error;
  } finally {
    if (browser?.isConnected()) await browser.close();
  }
}

main().catch(error => {
  console.error(error?.stack || error);
  process.exitCode = 1;
});
