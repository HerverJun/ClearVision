'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const {
  assert,
  captureRuntimeErrors,
  connectToDesktopWebView2,
  requiredEnvironment,
  safeFileName,
  seedAuthenticatedSession,
  waitForDoubleAnimationFrame,
  writeJsonEvidence,
  writePngEvidence
} = require('./webview2-harness.cjs');

function metadataField(value, name) {
  if (!value || typeof value !== 'object') return undefined;
  const expected = name.toLowerCase();
  const key = Object.keys(value).find(candidate => candidate.toLowerCase() === expected);
  return key === undefined ? undefined : value[key];
}

function isSensitiveFieldName(value) {
  return /token|password|authorization/i.test(value);
}

function redactSensitiveText(value) {
  return String(value ?? '')
    .replace(/(Bearer\s+)[^\s'",}\]]+/gi, '$1[REDACTED]')
    .replace(
      /((?:["']?(?:access[_-]?token|refresh[_-]?token|token|password|authorization)["']?)\s*[:=]\s*["']?)[^,\s'"}\]\r\n]+/gi,
      '$1[REDACTED]'
    )
    .replace(
      /([?&](?:access[_-]?token|refresh[_-]?token|token|password|authorization)=)[^&#\s]+/gi,
      '$1[REDACTED]'
    )
    .replace(
      /\b(?:https?|wss?):\/\/[^\s'",}\]]+/gi,
      '[REDACTED_URL]'
    );
}

function redactSensitiveValue(value, seen = new WeakSet()) {
  if (typeof value === 'string') return redactSensitiveText(value);
  if (!value || typeof value !== 'object') return value;
  if (seen.has(value)) return '[Circular]';
  seen.add(value);
  if (Array.isArray(value)) return value.map(item => redactSensitiveValue(item, seen));
  return Object.fromEntries(Object.entries(value).map(([key, nested]) => [
    key,
    isSensitiveFieldName(key) ? '[REDACTED]' : redactSensitiveValue(nested, seen)
  ]));
}

function safeJson(value) {
  try {
    return JSON.stringify(redactSensitiveValue(value));
  } catch {
    return '"[Unserializable diagnostic value]"';
  }
}

function writeRedactedEvidence(evidenceDirectory, outputName, evidence) {
  return writeJsonEvidence(evidenceDirectory, outputName, redactSensitiveValue(evidence));
}

function redactedRuntimeErrors(runtimeErrors) {
  return {
    consoleErrors: runtimeErrors.consoleErrors.map(redactSensitiveText),
    pageErrors: runtimeErrors.pageErrors.map(redactSensitiveText),
    requestFailures: runtimeErrors.requestFailures.map(({ method, errorText }) => ({
      method,
      errorText: redactSensitiveText(errorText)
    })),
    requests: runtimeErrors.requests.map(({ method }) => ({ method }))
  };
}

function recordRuntimeErrors(evidence, runtimeErrors) {
  const diagnostics = redactedRuntimeErrors(runtimeErrors);
  evidence.runtimeErrors = diagnostics;
  // Keep evidence aliases independent so recursive redaction does not label them as circular.
  evidence.meaningfulConsoleErrors = [...diagnostics.consoleErrors];
  evidence.meaningfulRequestFailures = diagnostics.requestFailures.map(failure => ({ ...failure }));
  return diagnostics;
}

function redactUser(value) {
  return {
    userId: String(metadataField(value, 'userId') ?? metadataField(value, 'id') ?? ''),
    username: String(metadataField(value, 'username') ?? ''),
    role: String(metadataField(value, 'role') ?? '')
  };
}

async function requestJson(webPort, requestPath, options = {}) {
  const response = await fetch(`http://127.0.0.1:${webPort}${requestPath}`, {
    method: options.method || 'GET',
    headers: {
      ...(options.token ? { Authorization: `Bearer ${options.token}` } : {}),
      ...(options.body === undefined ? {} : { 'Content-Type': 'application/json' })
    },
    body: options.body === undefined ? undefined : JSON.stringify(options.body)
  });
  const text = await response.text();
  let body = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    body = { raw: text };
  }
  return {
    status: response.status,
    body,
    contentType: response.headers.get('content-type') || ''
  };
}

async function requireSessionIdentity(webPort, token, expectedRole, expectedUsername) {
  const response = await requestJson(webPort, '/api/auth/me', { token });
  const user = redactUser(response.body);
  assert(response.status === 200,
    `/api/auth/me returned ${response.status}: ${safeJson(response.body)}`);
  assert(user.role === expectedRole,
    `/api/auth/me returned role ${safeJson(user.role)} instead of ${expectedRole}.`);
  if (expectedUsername) {
    assert(user.username === expectedUsername,
      `/api/auth/me returned username ${safeJson(user.username)} instead of ${expectedUsername}.`);
  }
  assert(user.userId, '/api/auth/me did not expose a stable user id.');
  return user;
}

async function assertForbidden(webPort, token, name, requestPath, body, expectedCode) {
  const response = await requestJson(webPort, requestPath, {
    method: 'POST',
    token,
    body
  });
  const code = String(metadataField(response.body, 'code') ?? metadataField(response.body, 'error') ?? '');
  assert(response.status === 403,
    `${name} returned ${response.status} instead of 403: ${safeJson(response.body)}`);
  assert(code === expectedCode,
    `${name} returned ${safeJson(code)} instead of ${expectedCode}: ${safeJson(response.body)}`);
  return { name, requestPath, status: response.status, code };
}

function studioUrl(webPort, route) {
  return `http://localhost:${webPort}/studio/index.html#${route}`;
}

function isRouteApiResponse(response, webPort, expectedPath) {
  const url = new URL(response.url());
  return response.request().method() === 'GET' &&
    url.origin === `http://localhost:${webPort}` &&
    url.pathname === expectedPath;
}

async function readRoutePageState(page, selector) {
  return page.evaluate(targetSelector => {
    const root = document.querySelector(targetSelector);
    const visibleStateKinds = root
      ? [...root.querySelectorAll('[data-page-state]')]
        .filter(element => element.getClientRects().length > 0)
        .map(element => element.getAttribute('data-page-state'))
      : [];
    return {
      rootVisible: Boolean(root && root.getClientRects().length > 0),
      forbiddenVisible: Boolean(document.querySelector('[data-studio-page="forbidden"]')?.getClientRects().length),
      visibleStateKinds
    };
  }, selector);
}

async function navigateToRoute(page, webPort, route, selector, expectedApiPath = null) {
  const routeResponse = expectedApiPath
    ? page.waitForResponse(response => isRouteApiResponse(response, webPort, expectedApiPath), {
      timeout: 45_000
    })
    : null;
  await page.goto(studioUrl(webPort, route), {
    waitUntil: 'domcontentloaded',
    timeout: 45_000
  });
  await page.locator(selector).waitFor({ state: 'visible', timeout: 45_000 });
  await page.waitForFunction(expectedRoute => window.location.hash === `#${expectedRoute}`, route, {
    timeout: 45_000
  });
  const response = routeResponse ? await routeResponse : null;
  if (response) {
    assert(response.status() >= 200 && response.status() < 300,
      `${route} did not receive a successful expected read response: ${response.status()}.`);
  }
  await waitForDoubleAnimationFrame(page);
  const pageState = await readRoutePageState(page, selector);
  assert(pageState.rootVisible && !pageState.forbiddenVisible &&
    !pageState.visibleStateKinds.some(kind =>
      ['loading', 'error', 'forbidden', 'unauthorized', 'not-found'].includes(kind)),
  `${route} settled on an invalid read-only page state: ${safeJson(pageState)}`);
  return {
    route,
    selector,
    hash: await page.evaluate(() => window.location.hash),
    pageState: 'ready',
    pageVisualState: pageState,
    readResponse: response ? {
      status: response.status()
    } : null
  };
}

async function assertForbiddenRoute(page, webPort, route) {
  await page.goto(studioUrl(webPort, route), {
    waitUntil: 'domcontentloaded',
    timeout: 45_000
  });
  await page.locator('[data-studio-page="forbidden"]').waitFor({ state: 'visible', timeout: 45_000 });
  const hash = await page.evaluate(() => window.location.hash);
  assert(hash === '#/forbidden',
    `${route} did not settle on the forbidden route: ${hash}`);
  return { requestedRoute: route, settledRoute: hash };
}

async function readBrowserSessionIdentity(page) {
  return page.evaluate(async () => {
    const token = sessionStorage.getItem('cv_auth_token');
    const projectedUser = sessionStorage.getItem('cv_current_user');
    const response = await fetch('/api/auth/me', {
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
      cache: 'no-store'
    });
    const text = await response.text();
    let body = null;
    try {
      body = text ? JSON.parse(text) : null;
    } catch {
      body = { raw: text };
    }
    return { status: response.status, body, projectedUser };
  });
}

async function main() {
  const cdpPort = Number(requiredEnvironment('CV_CDP_PORT'));
  const webPort = Number(requiredEnvironment('CV_WEB_PORT'));
  const evidenceDirectory = path.resolve(requiredEnvironment('CV_EVIDENCE_DIR'));
  const runName = String(process.env.CV_STUDIO_UI_RUN_NAME || 'next-operator-pilot').trim();
  const expectation = requiredEnvironment('CV_STUDIO_UI_EXPECTATION').toLowerCase();
  const sourceSha = requiredEnvironment('CV_STUDIO_UI_SOURCE_SHA').toLowerCase();
  const startupProfile = requiredEnvironment('Studio__StartupProfile').toUpperCase();
  const authMode = String(process.env.CV_STUDIO_UI_AUTH_MODE || 'UNRECORDED').trim().toUpperCase();
  const adminToken = requiredEnvironment('CV_SMOKE_TOKEN');
  const suppliedAdmin = JSON.parse(requiredEnvironment('CV_SMOKE_USER'));
  const outputName = `studio-ui-webview2-${safeFileName(runName)}.json`;
  const evidence = {
    schemaVersion: 1,
    evidenceKind: 'F09_NEXT_OPERATOR_PILOT_REAL_AUTHORITY',
    status: 'running',
    runName,
    expectation,
    sourceSha,
    startupProfileRequested: startupProfile,
    authMode,
    capturedAtUtc: new Date().toISOString(),
    credentialSource: 'RUNNER_ISSUED_ADMIN_TOKEN_THEN_REAL_OPERATOR_LOGIN'
  };
  let browser;
  let runtimeErrors;

  try {
    assert(expectation === 'studio-product',
      `F09 Operator pilot requires studio-product, received ${expectation}.`);
    assert(startupProfile === 'NEXT_OPERATOR_PILOT',
      `F09 Operator pilot requires NEXT_OPERATOR_PILOT, received ${startupProfile}.`);
    assert(/^[0-9a-f]{40}$/.test(sourceSha), 'F09 Operator pilot requires a 40-character source SHA.');
    assert(Number.isInteger(cdpPort) && cdpPort > 0, 'CV_CDP_PORT must be a valid port.');
    assert(Number.isInteger(webPort) && webPort > 0, 'CV_WEB_PORT must be a valid port.');
    assert(String(metadataField(suppliedAdmin, 'role') ?? '') === 'Admin',
      'The runner-provided authenticated user must be a real Admin.');

    evidence.admin = await requireSessionIdentity(webPort, adminToken, 'Admin');

    const suffix = crypto.randomBytes(8).toString('hex');
    const operatorUsername = `f09op-${suffix}`;
    const operatorPassword = `F09-${crypto.randomBytes(18).toString('base64url')}!`;
    const createOperator = await requestJson(webPort, '/api/users', {
      method: 'POST',
      token: adminToken,
      body: {
        username: operatorUsername,
        password: operatorPassword,
        displayName: 'F09 Operator Pilot',
        role: 'Operator'
      }
    });
    assert(createOperator.status === 201,
      `Admin could not create the isolated Operator: ${createOperator.status} ${safeJson(createOperator.body)}`);
    assert(String(metadataField(createOperator.body, 'role') ?? '') === 'Operator',
      `Created user is not an Operator: ${safeJson(createOperator.body)}`);

    const operatorLogin = await requestJson(webPort, '/api/auth/login', {
      method: 'POST',
      body: { username: operatorUsername, password: operatorPassword }
    });
    const operatorToken = String(metadataField(operatorLogin.body, 'token') ?? '');
    const operatorLoginUser = metadataField(operatorLogin.body, 'user');
    assert(operatorLogin.status === 200 && operatorToken && operatorLoginUser,
      `Operator login did not return a server-issued session: ${operatorLogin.status} ${safeJson(operatorLogin.body)}`);
    assert(String(metadataField(operatorLoginUser, 'role') ?? '') === 'Operator',
      `Operator login returned the wrong role: ${safeJson(operatorLoginUser)}`);
    evidence.operator = await requireSessionIdentity(webPort, operatorToken, 'Operator', operatorUsername);

    const deniedOperations = await Promise.all([
      assertForbidden(webPort, operatorToken, 'project-create', '/api/projects', {
        name: 'F09 Operator Forbidden Project'
      }, 'ProjectEditPermissionRequired'),
      assertForbidden(webPort, operatorToken, 'formal-admission', '/api/inspection/admission', {},
        'HardwareOperationPermissionRequired'),
      assertForbidden(webPort, operatorToken, 'formal-execute', '/api/inspection/execute', {},
        'HardwareOperationPermissionRequired'),
      assertForbidden(webPort, operatorToken, 'plc-test-connection', '/api/plc/test-connection', {
        protocol: 'S7', ipAddress: '127.0.0.1', port: 1
      }, 'HardwareOperationPermissionRequired')
    ]);
    evidence.permissionDenials = deniedOperations;

    const connected = await connectToDesktopWebView2(cdpPort);
    browser = connected.browser;
    const { page, version } = connected;
    evidence.cdpVersion = version;
    runtimeErrors = captureRuntimeErrors(page);

    await seedAuthenticatedSession(page, webPort, adminToken, JSON.stringify(suppliedAdmin));
    evidence.profileRejectsAdmin = await assertForbiddenRoute(page, webPort, '/overview');

    await seedAuthenticatedSession(page, webPort, operatorToken, JSON.stringify(operatorLoginUser));
    evidence.readOnlyRoutes = [];
    for (const { route, selector, expectedApiPath } of [
      { route: '/overview', selector: '[data-capability="overview"]', expectedApiPath: '/api/projects/recent' },
      { route: '/projects', selector: '[data-capability="projects-read"]', expectedApiPath: '/api/projects' },
      { route: '/operators', selector: '[data-capability="operators-read"]', expectedApiPath: '/api/operators/library' },
      { route: '/results', selector: '[data-capability="results-read"]', expectedApiPath: '/api/projects' },
      { route: '/stations', selector: '[data-capability="stations-read"]', expectedApiPath: '/api/stations' },
      { route: '/about', selector: '[data-studio-page="about"]', expectedApiPath: null }
    ]) {
      const routeEvidence = await navigateToRoute(page, webPort, route, selector, expectedApiPath);
      const routeIdentity = await readBrowserSessionIdentity(page);
      const routeUser = redactUser(routeIdentity.body);
      assert(routeIdentity.status === 200 && routeUser.role === 'Operator' &&
        routeUser.username === operatorUsername,
      `${route} did not retain the real Operator browser session: ${safeJson(routeIdentity.body)}.`);
      evidence.readOnlyRoutes.push({
        ...routeEvidence,
        authenticatedRead: { status: routeIdentity.status, role: routeUser.role },
        browserSession: { status: routeIdentity.status, user: routeUser }
      });
    }

    evidence.forbiddenRoutes = [];
    for (const route of [
      '/projects/00000000-0000-0000-0000-000000000000/workspace',
      '/ai',
      '/inspection',
      '/settings',
      '/diagnostics'
    ]) {
      evidence.forbiddenRoutes.push(await assertForbiddenRoute(page, webPort, route));
    }

    evidence.operatorLanding = await navigateToRoute(
      page,
      webPort,
      '/overview',
      '[data-capability="overview"]'
    );
    const projection = await page.evaluate(() => ({
      startup: {
        profile: window.__CLEARVISION_STARTUP__?.startupProfile || null,
        allowedRoles: Array.isArray(window.__CLEARVISION_STARTUP__?.profileAllowedRoles)
          ? [...window.__CLEARVISION_STARTUP__.profileAllowedRoles] : null,
        featureFlags: { ...(window.__CLEARVISION_STARTUP__?.featureFlags || {}) }
      },
      headerUser: document.querySelector('.product-layout__user strong')?.textContent?.trim() || null,
      ownerLedger: {
        studio: window.__STUDIO_UI_DIAGNOSTICS__ ? { ...window.__STUDIO_UI_DIAGNOSTICS__ } : null,
        projectLifecycle: window.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__
          ? { ...window.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__ } : null,
        leaveGuard: window.__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__
          ? { ...window.__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__ } : null,
        workspace: window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__
          ? { ...window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__ } : null
      }
    }));
    assert(projection.startup.profile === 'NEXT_OPERATOR_PILOT',
      `Operator session received the wrong startup profile: ${safeJson(projection.startup)}.`);
    assert(JSON.stringify(projection.startup.allowedRoles) === JSON.stringify(['Operator']),
      `Operator profile did not admit exactly Operator: ${safeJson(projection.startup.allowedRoles)}.`);
    for (const flag of ['Studio2.Workspace', 'Studio2.Settings', 'Studio2.InspectionRun', 'Studio2.AiWorkbench']) {
      assert(projection.startup.featureFlags[flag] === false,
        `${flag} must be disabled in NEXT_OPERATOR_PILOT.`);
    }
    assert(projection.startup.featureFlags['Studio2.StationsRead'] === true,
      'Studio2.StationsRead must remain enabled for NEXT_OPERATOR_PILOT.');
    assert(projection.headerUser === operatorUsername,
      `Product shell did not project the real Operator identity: ${projection.headerUser}.`);
    assert(projection.ownerLedger.studio?.mountCount === 1 &&
      projection.ownerLedger.studio?.activeRoot === 'studio-ui',
    `Operator pilot mounted an invalid Studio root: ${safeJson(projection.ownerLedger)}.`);
    assert(projection.ownerLedger.projectLifecycle === null,
      `Operator pilot mounted a project lifecycle owner: ${safeJson(projection.ownerLedger)}.`);
    assert(projection.ownerLedger.leaveGuard?.ownerCount === 1,
      `Operator pilot did not retain one Leave Guard owner: ${safeJson(projection.ownerLedger)}.`);
    assert(projection.ownerLedger.workspace?.workspaceOwnerCount === 0,
      `Operator pilot mounted a Workspace owner: ${safeJson(projection.ownerLedger)}.`);
    evidence.productPage = { ownerLedger: projection.ownerLedger };
    evidence.startupProjection = projection.startup;

    const browserIdentity = await readBrowserSessionIdentity(page);
    const browserUser = redactUser(browserIdentity.body);
    assert(browserIdentity.status === 200 && browserUser.role === 'Operator' &&
      browserUser.username === operatorUsername,
    `Browser session is not backed by the real Operator session: ${safeJson(browserIdentity.body)}.`);
    evidence.browserSession = { status: browserIdentity.status, user: browserUser };

    const screenshot = await page.screenshot({ type: 'png', animations: 'disabled' });
    evidence.viewportScreenshot = writePngEvidence(
      evidenceDirectory,
      `f09-operator-pilot-${safeFileName(runName)}.png`,
      screenshot
    );
    const runtimeDiagnostics = recordRuntimeErrors(evidence, runtimeErrors);
    assert(runtimeErrors.consoleErrors.length === 0,
      `Operator pilot browser console errors: ${runtimeErrors.consoleErrors.join(' | ')}`);
    assert(runtimeErrors.pageErrors.length === 0,
      `Operator pilot browser page errors: ${runtimeErrors.pageErrors.join(' | ')}`);
    assert(runtimeErrors.requestFailures.length === 0,
      `Operator pilot browser request failures: ${safeJson(runtimeDiagnostics.requestFailures)}`);

    evidence.status = 'pass';
    evidence.completedAtUtc = new Date().toISOString();
    const output = writeRedactedEvidence(evidenceDirectory, outputName, evidence);
    fs.writeFileSync(
      requiredEnvironment('CV_NODE_COMPLETION_SIGNAL'),
      `${JSON.stringify({ status: 'PASS', completedAtUtc: evidence.completedAtUtc })}\n`,
      'utf8'
    );
    process.stdout.write(`${JSON.stringify({ ok: true, output, runName })}\n`);
  } catch (error) {
    evidence.status = 'fail';
    evidence.completedAtUtc = new Date().toISOString();
    evidence.error = redactSensitiveText(error?.stack || error?.message || String(error));
    if (runtimeErrors) {
      recordRuntimeErrors(evidence, runtimeErrors);
    }
    const output = writeRedactedEvidence(evidenceDirectory, outputName, evidence);
    process.stderr.write(`${safeJson({ ok: false, output, error: evidence.error })}\n`);
    throw error;
  } finally {
    await browser?.close();
  }
}

main().catch(error => {
  process.stderr.write(`${redactSensitiveText(error?.stack || error)}\n`);
  process.exitCode = 1;
});
