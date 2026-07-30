'use strict';

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
  await page.waitForSelector(selector, { state: 'visible', timeout });
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
  const runName = String(process.env.CV_STUDIO_UI_RUN_NAME || 'f06-g3-debug').trim();
  const evidence = {
    schemaVersion: 'f06-g3-real-webview2.v1', status: 'running', runName, sourceSha,
    MODEL_MODE: 'RULE_FALLBACK', REAL_LLM_PRODUCT_QUALITY: 'NOT_EVALUATED',
    DATA_SOURCE: 'REAL_ASPNETCORE_WEBVIEW2_WITH_RESPONSE_LOSS_FAULT_INJECTION',
    capturedAtUtc: new Date().toISOString(), authority: {}, requests: {}, screenshots: {}, runtime: {}
  };
  let browser;
  try {
    const connected = await connectToDesktopWebView2(cdpPort);
    browser = connected.browser;
    const { context, page } = connected;
    const runtimeErrors = captureRuntimeErrors(page);
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
    await page.waitForSelector('[data-ai-owner-phase="idle"]', { state: 'visible', timeout: 30_000 });
    const startup = await page.evaluate(() => ({ ...window.__CLEARVISION_STARTUP__?.featureFlags }));
    assert(startup['Studio2.AiWorkbench'] === true, `AI feature flag was not enabled: ${JSON.stringify(startup)}`);
    const task = page.getByRole('textbox', { name: '任务描述' });
    assert(await task.evaluate(element => element === document.activeElement), 'Task composer did not receive initial focus.');
    await task.fill('使用已配置顶视相机检测金属冲压件表面划伤，划伤长度超过 2 毫米判定 NG，输出缺陷位置、长度和 OK/NG。');
    await page.getByRole('button', { name: '理解并规划任务' }).click();
    let phase = await waitForOwnerPhase(page, ['clarifying', 'plan-ready', 'plan-blocked']);
    if (phase !== 'plan-ready') {
      const recommended = page.getByRole('button', { name: '采用推荐答案' });
      assert(await recommended.count() === 1, `Real Plan stopped in ${phase} without recommended answers.`);
      await recommended.click();
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

    const apiRequests = runtimeErrors.requests.map(item => ({ method: item.method, path: new URL(item.url).pathname + new URL(item.url).search }));
    const forbidden = apiRequests.filter(item =>
      /handoff|apply-to-canvas|workspace\/consume|project\/save|flow-canvas|image-canvas/i.test(item.path) ||
      item.method === 'PUT' && /^\/api\/projects(?:\/|$)/i.test(item.path));
    const operationLookups = apiRequests.filter(item => /\/api\/ai\/operations\/.+kind=build_run/.test(item.path));
    const replays = apiRequests.filter(item => /^\/api\/ai\/agent-runs\/[^/]+$/.test(item.path) && item.method === 'GET');
    const streams = apiRequests.filter(item => /\/api\/ai\/agent-runs\/[^/]+\/events/.test(item.path));
    assert(buildPostCount === 2, `Expected two real Build creates, observed ${buildPostCount}.`);
    assert(operationLookups.length >= 1, 'Response-loss simulation did not trigger durable Build operation lookup.');
    assert(replays.length >= 2 && streams.length >= 1,
      `Real replay/SSE evidence was incomplete: ${JSON.stringify({ replays, streams })}`);
    assert(forbidden.length === 0, `Forbidden G4/Canvas/Project writes were observed: ${JSON.stringify(forbidden)}`);
    assert(runtimeErrors.consoleErrors.length === 0 && runtimeErrors.pageErrors.length === 0,
      `WebView2 runtime errors were observed: ${JSON.stringify(runtimeErrors)}`);
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
    evidence.requests = {
      total: apiRequests.length, sessionCreates: sessionRequests.length, buildPosts: buildPostCount,
      operationLookups, replays, streams, forbidden
    };
    evidence.runtime = {
      consoleErrors: runtimeErrors.consoleErrors, pageErrors: runtimeErrors.pageErrors,
      requestFailures: runtimeErrors.requestFailures, horizontalOverflow: overflow,
      dpi: await readBrowserDpiEvidence(page, context), native: runDesktopRuntimeProbe(executable)
    };
    evidence.status = 'PASS';
    evidence.capturedAtUtc = new Date().toISOString();
    writeJsonEvidence(evidenceDirectory, `studio-ui-webview2-f06-${runName}.json`, evidence);
  } catch (error) {
    evidence.status = 'FAIL';
    evidence.error = error?.stack || error?.message || String(error);
    evidence.capturedAtUtc = new Date().toISOString();
    writeJsonEvidence(evidenceDirectory, `studio-ui-webview2-f06-${runName}.json`, evidence);
    throw error;
  } finally {
    if (browser) await browser.close();
  }
}

main().catch(error => {
  console.error(error?.stack || error);
  process.exitCode = 1;
});
