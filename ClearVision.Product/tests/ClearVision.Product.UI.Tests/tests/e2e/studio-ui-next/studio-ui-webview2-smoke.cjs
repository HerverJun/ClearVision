'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const {
  assert,
  captureRuntimeErrors,
  connectToDesktopWebView2,
  readApi,
  readBrowserDpiEvidence,
  requiredEnvironment,
  runDesktopRuntimeProbe,
  safeFileName,
  seedAuthenticatedSession,
  waitForDoubleAnimationFrame,
  writeJsonEvidence,
  writePngEvidence
} = require('./webview2-harness.cjs');
const {
  createCanvasFixtureDescriptor
} = require('./canvas-benchmark-fixture.cjs');
const {
  resolveProductNavigationContract
} = require('./product-navigation-contract.cjs');

const expectations = new Set([
  'legacy',
  'studio-diagnostics',
  'studio-product',
  'studio-auth',
  'studio-design',
  'studio-canvas',
  'missing-assets'
]);

function parseBooleanEnvironment(name, fallback = false) {
  const value = String(process.env[name] || '').trim().toLowerCase();
  if (!value) return fallback;
  if (['1', 'true', 'yes', 'on'].includes(value)) return true;
  if (['0', 'false', 'no', 'off'].includes(value)) return false;
  throw new Error(`${name} must be a boolean value.`);
}

function createPreviewPpm(filePath) {
  const width = 100;
  const height = 100;
  const header = Buffer.from(`P6\n${width} ${height}\n255\n`, 'ascii');
  const pixels = Buffer.alloc(width * height * 3);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const offset = (y * width + x) * 3;
      pixels[offset] = x < 50 ? 32 : 125;
      pixels[offset + 1] = y < 50 ? 64 : 211;
      pixels[offset + 2] = (x - 50) ** 2 + (y - 50) ** 2 < 24 ** 2 ? 252 : 96;
    }
  }
  fs.writeFileSync(filePath, Buffer.concat([header, pixels]));
  return { filePath, width, height, byteLength: header.length + pixels.length };
}

async function readAuthorizedJson(webPort, token, requestPath) {
  const response = await fetch(`http://127.0.0.1:${webPort}${requestPath}`, {
    headers: { Authorization: `Bearer ${token}` }
  });
  const text = await response.text();
  assert(response.ok, `GET ${requestPath} returned ${response.status}: ${text.slice(0, 500)}`);
  return JSON.parse(text);
}

async function postAuthorizedJson(webPort, token, requestPath, body) {
  const response = await fetch(`http://127.0.0.1:${webPort}${requestPath}`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(body)
  });
  const text = await response.text();
  assert(response.ok, `POST ${requestPath} returned ${response.status}: ${text.slice(0, 500)}`);
  return JSON.parse(text);
}

async function requestJson(webPort, requestPath, options = {}) {
  const token = String(options.token || '');
  const method = String(options.method || 'GET').toUpperCase();
  const expectedStatuses = options.expectedStatuses || [200];
  const response = await fetch(`http://127.0.0.1:${webPort}${requestPath}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
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
  assert(expectedStatuses.includes(response.status),
    `${method} ${requestPath} returned ${response.status}: ${text.slice(0, 500)}`);
  return { status: response.status, body, headers: Object.fromEntries(response.headers.entries()) };
}

async function putAuthorizedJson(webPort, token, requestPath, body) {
  const response = await requestJson(webPort, requestPath, {
    token,
    method: 'PUT',
    body,
    expectedStatuses: [200]
  });
  return response.body;
}

async function waitForAuthoritativeRunStatus(webPort, token, identity, expectedStatus, timeoutMs = 10_000) {
  const startedAt = Date.now();
  const observations = [];
  while (Date.now() - startedAt < timeoutMs) {
    const reconciliation = await postAuthorizedJson(
      webPort,
      token,
      '/api/inspection/reconcile',
      identity
    );
    observations.push({
      status: reconciliation.status,
      code: reconciliation.code,
      observedAtUtc: new Date().toISOString()
    });
    if (reconciliation.status === expectedStatus) {
      return { reconciliation, observations };
    }
    await new Promise(resolve => setTimeout(resolve, 25));
  }
  throw new Error(
    `Authoritative run status did not reach ${expectedStatus}: ${JSON.stringify(observations)}`
  );
}

function metadataField(value, camelName) {
  return value?.[camelName] ?? value?.[camelName[0].toUpperCase() + camelName.slice(1)];
}

function instantiateOperator(definition, operatorType, name, x, y, values = {}) {
  const operatorId = crypto.randomUUID();
  const ports = (metadataField(definition, 'inputPorts') || []).map(port => ({
    id: crypto.randomUUID(),
    name: metadataField(port, 'name'),
    direction: 0,
    dataType: metadataField(port, 'dataType'),
    isRequired: metadataField(port, 'isRequired') === true
  }));
  const outputs = (metadataField(definition, 'outputPorts') || []).map(port => ({
    id: crypto.randomUUID(),
    name: metadataField(port, 'name'),
    direction: 1,
    dataType: metadataField(port, 'dataType'),
    isRequired: false
  }));
  return {
    id: operatorId,
    name,
    type: operatorType,
    metadata: null,
    x,
    y,
    inputPorts: ports,
    outputPorts: outputs,
    parameters: (metadataField(definition, 'parameters') || []).map(parameter => {
      const parameterName = metadataField(parameter, 'name');
      const defaultValue = metadataField(parameter, 'defaultValue');
      return ({
      id: crypto.randomUUID(),
      name: parameterName,
      displayName: metadataField(parameter, 'displayName') || parameterName,
      description: metadataField(parameter, 'description') || null,
      dataType: metadataField(parameter, 'dataType'),
      value: Object.prototype.hasOwnProperty.call(values, parameterName)
        ? values[parameterName]
        : defaultValue,
      defaultValue,
      minValue: metadataField(parameter, 'minValue') ?? null,
      maxValue: metadataField(parameter, 'maxValue') ?? null,
      isRequired: metadataField(parameter, 'isRequired') === true,
      options: metadataField(parameter, 'options') ?? null
    });
    }),
    isEnabled: true,
    executionStatus: 0,
    executionTimeMs: null,
    errorMessage: null
  };
}

async function seedWorkspaceProject(webPort, token, runName, formalRun = false, goldenJourney = false) {
  const evidenceDirectory = path.resolve(requiredEnvironment('CV_EVIDENCE_DIR'));
  const imageEvidence = createPreviewPpm(path.join(evidenceDirectory, 'g4-preview-input.ppm'));
  const catalogPayload = await readAuthorizedJson(webPort, token, '/api/operators/library?includeCompatibility=true');
  const catalog = Array.isArray(catalogPayload) ? catalogPayload : catalogPayload.items || catalogPayload.Items || [];
  const matchesType = (item, numericType, name) => Number(metadataField(item, 'type')) === numericType ||
    String(metadataField(item, 'type') || '').toLowerCase() === name.toLowerCase();
  const imageDefinition = catalog.find(item => matchesType(item, 0, 'ImageAcquisition'));
  const roiDefinition = catalog.find(item => matchesType(item, 42, 'RoiManager'));
  const judgmentDefinition = formalRun
    ? catalog.find(item => matchesType(item, -1, 'ResultJudgment'))
    : null;
  const delayDefinition = formalRun
    ? catalog.find(item => matchesType(item, 123, 'Delay'))
    : null;
  assert(imageDefinition && roiDefinition && (!formalRun || (judgmentDefinition && delayDefinition)),
    `The operator catalog did not expose the seeded operators: ${JSON.stringify(catalog.slice(0, 3))}`);
  const image = instantiateOperator(imageDefinition, 0, 'G4 File Image', 60, 80, {
    SourceType: 'File',
    FilePath: imageEvidence.filePath
  });
  const roi = instantiateOperator(roiDefinition, 42, 'G4 ROI Rectangle', 320, 80, {
    Shape: 'Rectangle',
    Operation: 'Crop',
    X: 10,
    Y: 10,
    Width: 40,
    Height: 30
  });
  const imageOutput = image.outputPorts.find(port => port.name === 'Image');
  const roiInput = roi.inputPorts.find(port => port.name === 'Image');
  assert(imageOutput && roiInput, 'The G4 seed operators did not expose the expected Image ports.');
  const camera = goldenJourney
    ? instantiateOperator(imageDefinition, 0, 'G4B Camera Binding', 60, 180, {
        SourceType: 'Camera',
        CameraBindingId: ''
      })
    : null;
  const cameraBindingParameter = camera?.parameters.find(parameter =>
    String(parameter.dataType).toLowerCase() === 'camerabinding' ||
      ['cameraid', 'camerabindingid'].includes(String(parameter.name).toLowerCase())) ?? null;
  let formalRunSeed = null;
  if (formalRun) {
    const judgment = instantiateOperator(
      judgmentDefinition,
      metadataField(judgmentDefinition, 'type'),
      'G6 Formal Judgment',
      580,
      80,
      { Condition: 'Equal', ExpectValue: '' }
    );
    const judgmentOutput = judgment.outputPorts.find(port => port.name === 'JudgmentResult');
    assert(judgmentOutput, 'The Formal Run ResultJudgment seed did not expose JudgmentResult.');
    const slowOperator = instantiateOperator(
      delayDefinition,
      metadataField(delayDefinition, 'type'),
      'G6 Deterministic Running Stop',
      780,
      80,
      { Milliseconds: 60_000 }
    );
    formalRunSeed = {
      operator: judgment,
      slowOperator,
      binding: {
        sourceOperatorId: judgment.id,
        sourceOutputPortId: judgmentOutput.id,
        sourceOutputName: judgmentOutput.name,
        dataType: 'String',
        rule: 'StringMap',
        okValue: 'OK',
        ngValue: 'NG'
      }
    };
  }
  const response = await fetch(`http://127.0.0.1:${webPort}/api/projects`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      name: `F03 G4 WebView2 ${runName}`,
      description: 'Harness-seeded authority for formal Preview, ImageCanvas and ROI evidence.',
      flow: {
        id: crypto.randomUUID(),
        name: 'F03 G4 WebView2 Flow',
        operators: [image, roi, ...(camera ? [camera] : []), ...(goldenJourney && formalRunSeed ? [formalRunSeed.operator] : [])],
        connections: [{
          id: crypto.randomUUID(),
          sourceOperatorId: image.id,
          sourcePortId: imageOutput.id,
          targetOperatorId: roi.id,
          targetPortId: roiInput.id
        }],
        decisionConfiguration: null
      }
    })
  });
  const text = await response.text();
  assert(response.ok, `Harness project seed returned ${response.status}: ${text.slice(0, 500)}`);
  const project = JSON.parse(text);
  assert(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(project.id),
    'Harness project seed did not return a UUID project id.');
  return {
    projectId: project.id,
    route: `/projects/${project.id}/workspace`,
    responseStatus: response.status,
    authority: 'HARNESS_SEEDED_EXISTING_PROJECT_APPLICATION_SERVICE',
    runtimeAuditStartedAfterSeed: true,
    imageEvidence,
    roiNodeId: roi.id,
    formalRunSeed,
    goldenJourneySeed: goldenJourney && camera && formalRunSeed ? {
      cameraNodeId: camera.id,
      cameraBindingParameterName: cameraBindingParameter?.name ?? 'CameraId',
      cameraBindingId: 'g4b-camera-fixture',
      judgmentNodeId: formalRunSeed.operator.id,
      judgmentOutputId: formalRunSeed.binding.sourceOutputPortId,
      judgmentParameterId: formalRunSeed.operator.parameters.find(parameter => parameter.name === 'ExpectValue')?.id ?? null
    } : null
  };
}

function normalizeStudioRoute(value) {
  const normalized = String(value || '/diagnostics').trim()
    .replace(/^#/, '')
    .replace(/^\/?/, '/');
  assert(!normalized.includes('..'), 'Studio route must not contain parent-directory segments.');
  return normalized;
}

function routeForExpectation(expectation) {
  switch (expectation) {
    case 'studio-product': return '/overview';
    case 'studio-auth': return '/login';
    case 'studio-design': return '/labs/design';
    case 'studio-canvas': return '/labs/canvas';
    default: return '/diagnostics';
  }
}

async function verifyStudioAuthLifecycle(page, userJson, password) {
  const user = JSON.parse(userJson);
  const username = user.username || user.Username;
  assert(username, 'The WebView2 auth scenario did not receive a username.');
  assert(password, 'CV_SMOKE_PASSWORD is required for the WebView2 auth scenario.');

  await page.waitForFunction(() => Boolean(
    document.querySelector('[data-auth-page="setup"]') ||
    document.querySelector('[data-auth-page="login"]')
  ), null, { timeout: 45_000 });
  if (await page.locator('[data-auth-page="setup"]').isVisible()) {
    await page.reload({ waitUntil: 'domcontentloaded', timeout: 45_000 });
  }
  await page.waitForSelector('[data-auth-page="login"]', { state: 'visible', timeout: 45_000 });
  const beforeLogin = await page.evaluate(() => ({
    authShellCount: document.querySelectorAll('[data-auth-shell="ready"]').length,
    productShellCount: document.querySelectorAll('[data-product-shell]').length,
    tokenPresent: Boolean(sessionStorage.getItem('cv_auth_token'))
  }));
  assert(beforeLogin.authShellCount === 1, 'WebView2 did not mount the login shell.');
  assert(beforeLogin.productShellCount === 0, 'ProductRuntime mounted before WebView2 login.');
  assert(beforeLogin.tokenPresent === false, 'WebView2 auth scenario started with a pre-seeded page token.');

  await page.getByLabel('用户名').fill(username);
  await page.getByLabel('密码').fill(password);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await page.waitForSelector('[data-product-shell="ready"]', { state: 'visible', timeout: 45_000 });
  const authenticated = await page.evaluate(() => ({
    authShellCount: document.querySelectorAll('[data-auth-shell="ready"]').length,
    productShellCount: document.querySelectorAll('[data-product-shell="ready"]').length,
    tokenPresent: Boolean(sessionStorage.getItem('cv_auth_token')),
    route: location.hash
  }));
  assert(authenticated.productShellCount === 1, 'WebView2 login did not mount exactly one ProductRuntime shell.');
  assert(authenticated.authShellCount === 0, 'WebView2 login left the Auth shell mounted.');
  assert(authenticated.tokenPresent, 'WebView2 login did not persist the token through the token port.');

  await page.getByRole('button', { name: '退出', exact: true }).click();
  await page.waitForSelector('[data-auth-page="login"]', { state: 'visible', timeout: 45_000 });
  await page.goBack();
  await page.waitForSelector('[data-auth-page="login"]', { state: 'visible', timeout: 45_000 });
  const loggedOut = await page.evaluate(() => ({
    authShellCount: document.querySelectorAll('[data-auth-shell="ready"]').length,
    productShellCount: document.querySelectorAll('[data-product-shell]').length,
    tokenPresent: Boolean(sessionStorage.getItem('cv_auth_token')),
    route: location.hash
  }));
  assert(loggedOut.authShellCount === 1, 'WebView2 logout did not restore the Auth shell.');
  assert(loggedOut.productShellCount === 0, 'WebView2 logout retained ProductRuntime DOM.');
  assert(loggedOut.tokenPresent === false, 'WebView2 logout did not clear the token port.');
  return { beforeLogin, authenticated, loggedOut };
}

function meaningfulRequestFailures(requestFailures) {
  return requestFailures.filter(item => !/ERR_ABORTED|NS_BINDING_ABORTED/i.test(item.errorText));
}

async function waitForSelectorWithoutHandle(page, selector, options = {}) {
  await page.locator(selector).waitFor(options);
}

async function waitForFunctionWithoutHandle(page, pageFunction, arg, options) {
  const handle = await page.waitForFunction(pageFunction, arg, options);
  await handle.dispose();
}

function classifyConsoleErrors(consoleErrors, productPage, finalJourney) {
  const lifecycle = productPage?.workspaceLifecycle;
  const expectedNotFoundCount = [lifecycle?.mounted?.state, lifecycle?.remounted?.state]
    .filter(state => state === 'not-found')
    .length + Number(finalJourney?.expectedConsoleErrors?.notFound || 0);
  const expectedNotFoundMessage = 'Failed to load resource: the server responded with a status of 404 (Not Found)';
  let remainingExpectedNotFound = expectedNotFoundCount;
  const expectedResponseLossCount =
    (productPage?.workspaceG6?.responseLoss?.backendResultRecovered ? 1 : 0) +
    Number(finalJourney?.expectedConsoleErrors?.responseLoss || 0);
  const expectedResponseLossMessage = 'Failed to load resource: the server responded with a status of 599 (Unknown)';
  let remainingExpectedResponseLoss = expectedResponseLossCount;
  const ignoredExpected = [];
  const meaningful = [];

  for (const message of consoleErrors) {
    if (remainingExpectedNotFound > 0 && message === expectedNotFoundMessage) {
      remainingExpectedNotFound -= 1;
      ignoredExpected.push(message);
    } else if (remainingExpectedResponseLoss > 0 && message === expectedResponseLossMessage) {
      remainingExpectedResponseLoss -= 1;
      ignoredExpected.push(message);
    } else {
      meaningful.push(message);
    }
  }

  return { ignoredExpected, meaningful };
}

async function readApiEvidence(webPort, token) {
  const [health, setupStatus, me, operators, projects] = await Promise.all([
    readApi(webPort, '/health'),
    readApi(webPort, '/api/auth/setup-status'),
    readApi(webPort, '/api/auth/me', token),
    readApi(webPort, '/api/operators/library', token),
    readApi(webPort, '/api/projects', token)
  ]);
  return { health, setupStatus, me, operators, projects };
}

async function readStartupEvidence(page) {
  return page.evaluate(() => {
    const startup = window.__CLEARVISION_STARTUP__;
    const descriptor = Object.getOwnPropertyDescriptor(window, '__CLEARVISION_STARTUP__');
    const featureFlags = startup && typeof startup === 'object'
      ? startup.featureFlags
      : undefined;
    return {
      exists: startup !== undefined,
      value: startup && typeof startup === 'object'
        ? JSON.parse(JSON.stringify(startup))
        : startup ?? null,
      keys: startup && typeof startup === 'object'
        ? Reflect.ownKeys(startup).map(String).sort()
        : [],
      frozen: startup && typeof startup === 'object' ? Object.isFrozen(startup) : false,
      featureFlagsFrozen: featureFlags && typeof featureFlags === 'object'
        ? Object.isFrozen(featureFlags)
        : false,
      featureFlagTypes: featureFlags && typeof featureFlags === 'object'
        ? Object.fromEntries(Object.entries(featureFlags).map(([key, value]) => [key, typeof value]))
        : {},
      descriptor: descriptor ? {
        configurable: descriptor.configurable,
        enumerable: descriptor.enumerable,
        writable: descriptor.writable === true,
        hasGetter: typeof descriptor.get === 'function'
      } : null,
      tokenPresent: Boolean(sessionStorage.getItem('cv_auth_token')),
      chromeWebView: typeof window.chrome?.webview?.postMessage === 'function'
    };
  });
}

function assertFrozenStartupProjection(startup) {
  assert(startup.exists, 'Desktop did not inject window.__CLEARVISION_STARTUP__.');
  assert(startup.frozen, 'Desktop startup projection is not frozen.');
  assert(startup.featureFlagsFrozen, 'Desktop startup featureFlags projection is not frozen.');
  assert(
    Object.values(startup.featureFlagTypes).every(value => value === 'boolean'),
    'Desktop startup featureFlags contains a non-boolean value.'
  );
  assert(startup.descriptor?.configurable === false, 'Desktop startup projection is configurable.');
  assert(startup.descriptor?.writable === false, 'Desktop startup projection is writable.');
  assert(startup.descriptor?.enumerable === true, 'Desktop startup projection is not enumerable.');
  assert(startup.tokenPresent, 'Authenticated session token was not visible to the mounted page.');
  assert(startup.chromeWebView, 'The formal WebView2 host bridge is unavailable.');
}

async function navigateWithAuthenticatedSession(page, webPort, expectation, route) {
  const target = expectation === 'legacy'
    ? `http://localhost:${webPort}/index.html`
    : `http://localhost:${webPort}/studio/index.html#${route}`;
  await page.goto(target, { waitUntil: 'domcontentloaded', timeout: 45_000 });
  return target;
}

async function verifyLegacy(page, webPort) {
  await page.waitForSelector('#loading-screen', { state: 'hidden', timeout: 45_000 });
  await page.waitForSelector('#main-content', { state: 'visible', timeout: 30_000 });
  await page.waitForFunction(() => Boolean(window.flowCanvas && window.flowCanvasAdapter));
  const [startup, projection] = await Promise.all([
    readStartupEvidence(page),
    page.evaluate(() => ({
      url: location.href,
      title: document.title,
      legacyNavigationCount: document.querySelectorAll('.nav-btn[data-view]').length,
      legacyMainVisible: Boolean(document.querySelector('#main-content')),
      studioPageCount: document.querySelectorAll('[data-studio-page]').length,
      studioReadyType: typeof window.__STUDIO_UI_READY__,
      studioDiagnosticsType: typeof window.__STUDIO_UI_DIAGNOSTICS__,
      flowCanvasClass: window.flowCanvas?.constructor?.name || null,
      flowCanvasAdapterClass: window.flowCanvasAdapter?.constructor?.name || null
    }))
  ]);

  assertFrozenStartupProjection(startup);
  assert(startup.value.hostKind === 'desktop-webview2', 'Legacy page is not in the Desktop host.');
  assert(startup.value.apiBaseUrl === `http://localhost:${webPort}/api`, 'Legacy API base URL is unexpected.');
  assert(!Object.prototype.hasOwnProperty.call(startup.value, 'uiKind'), 'Legacy startup unexpectedly claims StudioUI ownership.');
  assert(projection.legacyNavigationCount > 0 && projection.legacyMainVisible, 'Legacy mounted root is unavailable.');
  assert(projection.studioPageCount === 0, 'StudioUI mounted while the legacy flag was selected.');
  assert(projection.studioReadyType === 'undefined', 'Legacy page leaked StudioUI lifecycle diagnostics.');
  assert(projection.studioDiagnosticsType === 'undefined', 'Legacy page leaked StudioUI diagnostics.');
  assert(projection.flowCanvasClass === 'FlowCanvas', 'Legacy did not mount the canonical FlowCanvas engine.');
  assert(projection.flowCanvasAdapterClass === 'FlowCanvasAdapter', 'Legacy did not expose the canonical FlowCanvas adapter.');
  return { startup, projection };
}

async function waitForStudioReady(page) {
  await page.waitForFunction(() => window.__STUDIO_UI_READY__ === true, null, { timeout: 45_000 });
  await page.waitForFunction(() => window.__STUDIO_UI_DIAGNOSTICS__?.mountCount === 1);
}

async function verifyStudioFoundation(page, webPort, route) {
  await waitForStudioReady(page);
  const [startup, diagnostics, projection] = await Promise.all([
    readStartupEvidence(page),
    page.evaluate(() => ({ ...window.__STUDIO_UI_DIAGNOSTICS__ })),
    page.evaluate(() => ({
      url: location.href,
      path: location.pathname,
      hash: location.hash,
      studioPage: document.querySelector('[data-studio-page]')?.getAttribute('data-studio-page') ||
        document.querySelector('[data-capability]')?.getAttribute('data-capability') || null,
      legacyNavigationCount: document.querySelectorAll('.nav-btn[data-view]').length,
      legacyMainCount: document.querySelectorAll('#main-content').length,
      legacyFlowCanvasType: typeof window.flowCanvas,
      appChildCount: document.querySelector('#app')?.childElementCount ?? -1
    }))
  ]);

  assertFrozenStartupProjection(startup);
  assert(
    JSON.stringify(startup.keys) === JSON.stringify([
      'apiBaseUrl',
      'featureFlags',
      'hostKind',
      'schemaVersion',
      'studioUiBasePath',
      'uiKind'
    ]),
    `StudioUI startup schema fields drifted: ${JSON.stringify(startup.keys)}`
  );
  assert(startup.value.schemaVersion === 1, 'StudioUI startup schemaVersion is not 1.');
  assert(startup.value.uiKind === 'studio-ui', 'StudioUI startup uiKind is invalid.');
  assert(startup.value.hostKind === 'desktop-webview2', 'StudioUI is not running in Desktop WebView2.');
  assert(startup.value.apiBaseUrl === `http://localhost:${webPort}/api`, 'StudioUI API base URL is unexpected.');
  assert(startup.value.studioUiBasePath === '/studio/', 'StudioUI base path is unexpected.');
  assert(diagnostics.ready === true, 'StudioUI lifecycle diagnostics did not reach ready.');
  assert(diagnostics.mountCount === 1, `StudioUI mounted ${diagnostics.mountCount} times.`);
  assert(diagnostics.activeRoot === 'studio-ui', 'StudioUI is not the active mounted root.');
  assert(diagnostics.hostKind === 'desktop-webview2', 'StudioUI lifecycle hostKind is unexpected.');
  assert(diagnostics.unhandledErrorCount === 0, 'StudioUI counted an unhandled runtime error.');
  assert(diagnostics.lastBootstrapError === null, 'StudioUI recorded a bootstrap error.');
  assert(projection.path === '/studio/index.html', `Unexpected StudioUI page path: ${projection.path}`);
  assert(projection.hash === `#${route}`, `Unexpected StudioUI route: ${projection.hash}`);
  assert(projection.studioPage, 'StudioUI route did not mount a page owner.');
  assert(projection.appChildCount === 1, 'StudioUI composition root did not mount exactly one child root.');
  assert(projection.legacyNavigationCount === 0 && projection.legacyMainCount === 0,
    'Legacy root remained mounted beside StudioUI.');
  assert(projection.legacyFlowCanvasType === 'undefined', 'StudioUI leaked the legacy FlowCanvas global.');
  return { startup, diagnostics, projection };
}

async function verifyDiagnosticsPage(page) {
  await page.waitForSelector('[data-studio-page="diagnostics"]', { state: 'visible' });
  await page.waitForFunction(() => {
    const states = [...document.querySelectorAll('[data-probe-state]')]
      .map(element => element.getAttribute('data-probe-state'));
    return states.length === 2 && states.every(state => state === 'ok');
  }, null, { timeout: 30_000 });
  return page.evaluate(() => ({
    probeStates: [...document.querySelectorAll('[data-probe-state]')]
      .map(element => element.getAttribute('data-probe-state')),
    bodyText: document.querySelector('[data-studio-page="diagnostics"]')?.textContent?.trim() || ''
  }));
}

async function waitForFlowSurfaceNumber(page, attribute, expected) {
  await page.waitForFunction(({ name, value }) => {
    const surface = document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]');
    return Number(surface?.getAttribute(name)) === value;
  }, { name: attribute, value: expected }, { timeout: 30_000 });
}

async function readFlowSurface(page) {
  return page.evaluate(() => {
    const surface = document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]');
    return {
      nodeCount: Number(surface?.getAttribute('data-node-count') || -1),
      connectionCount: Number(surface?.getAttribute('data-connection-count') || -1),
      selectedCount: Number(surface?.getAttribute('data-selected-count') || -1),
      selectedDisabledCount: Number(surface?.getAttribute('data-selected-disabled-count') || -1),
      flowRevision: Number(surface?.getAttribute('data-flow-revision') || -1),
      scale: Number(surface?.getAttribute('data-scale') || -1),
      mutationGate: surface?.getAttribute('data-mutation-gate') || null,
      feedback: document.querySelector('.flow-canvas-surface__status')?.textContent?.trim() || '',
      minimapCount: document.querySelectorAll('.flow-minimap').length
    };
  });
}

async function resetOperatorRailFilters(page) {
  await ensureOperatorFlyout(page);
  await page.locator('[data-testid="operator-search"]').fill('');
  await page.locator('.operator-rail__categories button').first().click();
}

async function ensureOperatorFlyout(page) {
  if (await page.locator('[data-capability="operator-flyout"]').count()) return;
  await page.getByRole('button', { name: '搜索与全部算子' }).click();
  await page.locator('[data-capability="operator-flyout"]').waitFor({ state: 'visible' });
}

async function readOperatorDefinition(page, typeCandidates) {
  const candidates = Array.isArray(typeCandidates) ? typeCandidates : [typeCandidates];
  const selector = candidates.map(type => `.operator-item[data-type="${type}"]`).join(', ');
  await resetOperatorRailFilters(page);
  let item = page.locator(selector);
  if (await item.count() === 0) {
    const compatibility = page.locator('.operator-flyout__compatibility input');
    if (!(await compatibility.isChecked())) await compatibility.check();
    item = page.locator(selector);
  }
  assert(await item.count() === 1,
    `Operator Rail did not expose exactly one of: ${candidates.join(', ')}.`);
  const serialized = await item.getAttribute('data-operator');
  assert(serialized, `Operator Rail type ${candidates.join('/')} did not expose its drag payload.`);
  return { item, definition: JSON.parse(serialized) };
}

function operatorNodeHeight(definition) {
  const portRows = Math.max(
    definition.inputPorts?.length || 0,
    definition.outputPorts?.length || 0,
    1
  );
  return Math.max(60, 24 + 10 + 10 + portRows * 18);
}

function operatorPortPoint(position, definition, isOutput, portIndex = 0) {
  const ports = isOutput ? definition.outputPorts || [] : definition.inputPorts || [];
  assert(ports.length > portIndex, `Operator ${definition.operatorType} is missing the requested port.`);
  const height = operatorNodeHeight(definition);
  const top = position.y + 24 + 10;
  const bottom = position.y + height - 10;
  const y = ports.length === 1
    ? (top + bottom) / 2
    : top + ((bottom - top) * portIndex) / (ports.length - 1);
  return { x: position.x + (isOutput ? 140 : 0), y };
}

async function dragOperatorToCanvas(page, typeCandidates, targetPosition) {
  const operator = await readOperatorDefinition(page, typeCandidates);
  await operator.item.dragTo(page.locator('[data-testid="flow-canvas"]'), { targetPosition });
  return operator.definition;
}

async function dragCanvasPointer(page, from, to) {
  const box = await page.locator('[data-testid="flow-canvas"]').boundingBox();
  assert(box, 'Flow Canvas did not expose a browser bounding box.');
  await page.mouse.move(box.x + from.x, box.y + from.y);
  await page.mouse.down();
  await page.mouse.move(box.x + to.x, box.y + to.y, { steps: 8 });
  await page.mouse.up();
}

async function readInspectorSurface(page) {
  return page.evaluate(() => {
    const inspector = document.querySelector('[data-evidence-surface="f03-g3-inspector"]');
    const body = inspector?.querySelector('.inspector-panel__body');
    return {
      mode: inspector?.getAttribute('data-inspector-mode') || null,
      metadataPhase: inspector?.getAttribute('data-metadata-phase') || null,
      mutationGate: inspector?.getAttribute('data-mutation-gate') || null,
      flowRevision: Number(inspector?.getAttribute('data-flow-revision') || -1),
      activeDrafts: Number(inspector?.getAttribute('data-active-drafts') || -1),
      parameterCount: inspector?.querySelectorAll('[data-parameter-name]').length || 0,
      scrollTop: body?.scrollTop || 0,
      scrollHeight: body?.scrollHeight || 0,
      clientHeight: body?.clientHeight || 0
    };
  });
}

async function verifyWorkspaceInspectorG3(page, expectedName) {
  await page.waitForFunction(() => {
    const inspector = document.querySelector('[data-evidence-surface="f03-g3-inspector"]');
    return inspector?.getAttribute('data-inspector-mode') === 'node' &&
      inspector.getAttribute('data-metadata-phase') === 'ready';
  }, null, { timeout: 30_000 });
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  const before = await readInspectorSurface(page);
  const name = inspector.locator('.inspector-panel__field input');
  const committedName = `${expectedName} · G3`;
  await name.fill(committedName);
  await name.press('Enter');
  await waitForFlowSurfaceNumber(page, 'data-flow-revision', before.flowRevision + 1);

  const inputRevision = (await readFlowSurface(page)).flowRevision;
  await name.fill(`${committedName} draft`);
  await page.keyboard.press('Control+z');
  assert((await readFlowSurface(page)).flowRevision === inputRevision,
    'Inspector text focus leaked Ctrl+Z into Canvas history.');
  await name.press('Escape');

  const editor = inspector.locator(
    '[data-parameter-name][data-editor-kind="text"], ' +
    '[data-parameter-name][data-editor-kind="number"], ' +
    '[data-parameter-name][data-editor-kind="boolean"], ' +
    '[data-parameter-name][data-editor-kind="enum"], ' +
    '[data-parameter-name][data-editor-kind="slider"]'
  ).first();
  assert(await editor.count() === 1, 'Inspector did not expose an implemented metadata parameter editor.');
  const kind = await editor.getAttribute('data-editor-kind');
  const revisionBeforeParameter = (await readFlowSurface(page)).flowRevision;
  if (kind === 'boolean') {
    const control = editor.locator('input[type="checkbox"]').last();
    if (await control.isChecked()) await control.uncheck();
    else await control.check();
  } else if (kind === 'enum') {
    const select = editor.locator('select');
    const current = await select.inputValue();
    const options = await select.locator('option').evaluateAll(items =>
      items.map(item => item.value));
    const next = options.find(value => value !== current);
    assert(next !== undefined, 'Inspector enum did not expose an alternate option.');
    await select.selectOption(next);
  } else if (kind === 'text') {
    const control = editor.locator('input[type="text"]');
    const current = await control.inputValue();
    await control.fill(`${current}0`);
    await control.press('Enter');
  } else {
    const control = editor.locator('input[type="number"]').first();
    const current = Number(await control.inputValue());
    const min = Number(await control.getAttribute('min'));
    const max = Number(await control.getAttribute('max'));
    const boundedMin = Number.isFinite(min) ? min : 0;
    const boundedMax = Number.isFinite(max) ? max : boundedMin + 10;
    const candidate = current < boundedMax ? Math.min(boundedMax, current + 1) : Math.max(boundedMin, current - 1);
    await control.fill(String(candidate));
    await control.press('Enter');
  }
  await waitForFlowSurfaceNumber(page, 'data-flow-revision', revisionBeforeParameter + 1);
  const parameterRevision = (await readFlowSurface(page)).flowRevision;
  await page.locator('[data-flow-command="undo"]').click();
  await waitForFlowSurfaceNumber(page, 'data-flow-revision', parameterRevision + 1);
  await page.locator('[data-flow-command="redo"]').click();
  await waitForFlowSurfaceNumber(page, 'data-flow-revision', parameterRevision + 2);

  const scaleBeforeScroll = (await readFlowSurface(page)).scale;
  const body = inspector.locator('.inspector-panel__body');
  await body.hover();
  await page.mouse.wheel(0, 420);
  await waitForDoubleAnimationFrame(page);
  const afterScroll = await readInspectorSurface(page);
  assert((await readFlowSurface(page)).scale === scaleBeforeScroll,
    'Inspector wheel scrolling leaked into Canvas zoom.');
  if (afterScroll.scrollHeight > afterScroll.clientHeight) {
    assert(afterScroll.scrollTop > 0, 'Scrollable Inspector did not consume the wheel gesture.');
  }
  return {
    before,
    committedName,
    editorKind: kind,
    revisionAfterRedo: (await readFlowSurface(page)).flowRevision,
    afterScroll
  };
}

async function selectSeededRoiNode(page) {
  const flowCanvas = page.locator('[data-testid="flow-canvas"]');
  const box = await flowCanvas.boundingBox();
  assert(box, 'The seeded G4 Flow Canvas did not expose a bounding box.');
  await page.mouse.click(box.x + 360, box.y + 125);
  await page.waitForFunction(() =>
    document.querySelector('[data-evidence-surface="f03-g3-inspector"]')
      ?.getAttribute('data-inspector-mode') === 'node');
  return { flowCanvas, box };
}

async function readWorkspaceCanvasDpiEvidence(page, pointerNodeId) {
  assert(/^[0-9a-f-]{36}$/i.test(pointerNodeId || ''),
    'F04 Workspace DPI evidence did not retain the seeded pointer-hit node identity.');
  const observed = await page.evaluate(() => {
    const canvas = document.querySelector('[data-testid="flow-canvas"]');
    const surface = document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]');
    const inspector = document.querySelector('[data-evidence-surface="f03-g3-inspector"]');
    if (!(canvas instanceof HTMLCanvasElement)) return null;
    const rect = canvas.getBoundingClientRect();
    return {
      runtime: {
        dpr: window.devicePixelRatio,
        logicalWidth: rect.width,
        logicalHeight: rect.height,
        backingWidth: canvas.width,
        backingHeight: canvas.height
      },
      selectedCount: Number(surface?.getAttribute('data-selected-count') || -1),
      inspectorMode: inspector?.getAttribute('data-inspector-mode') || null
    };
  });
  assert(observed, 'F04 Workspace DPI evidence did not find the canonical Flow Canvas element.');
  assert(observed.runtime.logicalWidth > 0 && observed.runtime.logicalHeight > 0,
    `F04 Workspace Flow Canvas has no logical size: ${JSON.stringify(observed)}`);
  assert(
    Math.abs(observed.runtime.backingWidth -
        (observed.runtime.logicalWidth * observed.runtime.dpr)) <= 2 &&
      Math.abs(observed.runtime.backingHeight -
        (observed.runtime.logicalHeight * observed.runtime.dpr)) <= 2,
    `F04 Workspace Flow Canvas backing store does not match DPR: ${JSON.stringify(observed)}`
  );
  assert(observed.selectedCount === 1 && observed.inspectorMode === 'node',
    `F04 Workspace pointer hit did not preserve the seeded ROI selection: ${JSON.stringify(observed)}`);
  return {
    source: 'FORMAL_PRODUCT_WORKSPACE_CANONICAL_FLOW_CANVAS',
    mounted: { canvas: { runtime: observed.runtime } },
    pointerHit: {
      id: pointerNodeId,
      logicalOffset: { x: 360, y: 125 },
      selectedCount: observed.selectedCount,
      inspectorMode: observed.inspectorMode
    }
  };
}

async function waitForSeededPreview(page) {
  await page.waitForFunction(() =>
    document.querySelector('[data-capability="preview-workbench"]')
      ?.getAttribute('data-preview-phase') === 'success', null, { timeout: 30_000 });
  await page.waitForFunction(() =>
    document.querySelector('[data-capability="image-canvas"]')
      ?.getAttribute('data-image-phase') === 'ready', null, { timeout: 30_000 });
  await page.waitForFunction(() =>
    document.querySelector('.preview-panel__roi')
      ?.getAttribute('data-roi-phase') === 'ready', null, { timeout: 30_000 });
}

async function installG4BGoldenJourneyHarness(page, evidenceDirectory, imageEvidence) {
  const frame = fs.readFileSync(imageEvidence.filePath);
  const checksum = crypto.createHash('sha256').update(frame).digest('hex');
  const binding = {
    id: 'g4b-camera-fixture',
    displayName: 'G4B 宿主验证相机',
    deviceId: 'G4B-NO-HARDWARE',
    serialNumber: 'G4B-NO-HARDWARE',
    ipAddress: '',
    manufacturer: 'Evidence Harness',
    modelName: 'Fixture Frame',
    interfaceType: 'Harness',
    isEnabled: true,
    exposureTimeUs: 5000,
    gainDb: 1,
    pixelFormat: 'Mono8',
    triggerMode: 'Software',
    connectionStatus: 'Connected'
  };
  await page.route('**/api/cameras/bindings', async route => {
    if (route.request().method() !== 'GET') return route.fallback();
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([binding]) });
  });
  await page.route('**/api/cameras/soft-trigger-capture', async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    await route.fulfill({
      status: 200,
      contentType: 'image/x-portable-pixmap',
      headers: {
        'X-Camera-Id': binding.id,
        'X-Trigger-Mode': 'Software',
        'X-Image-Width': String(imageEvidence.width),
        'X-Image-Height': String(imageEvidence.height),
        'X-G4B-Harness': 'camera-frame'
      },
      body: frame
    });
  });
  await page.route(/\/api\/inspection\/history\/[0-9a-f-]{36}\/[0-9a-f-]{36}\/evidence\/manifest$/i, async route => {
    if (route.request().method() !== 'GET') return route.fallback();
    const parts = new URL(route.request().url()).pathname.split('/');
    const projectId = parts[4];
    const resultId = parts[5];
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        status: 'available',
        message: 'G4B 宿主证据可用。',
        manifest: {
          schemaVersion: 1,
          manifestId: `g4b-${resultId}`,
          projectId,
          inspectionResultId: resultId,
          status: 'available',
          outcome: 'OK',
          createdAtUtc: new Date().toISOString(),
          flowVersionHash: 'g4b-host-authority',
          calibrationBundleId: null,
          sessionId: null,
          runId: resultId,
          retentionClass: 'evidence-harness',
          retentionExpiresAtUtc: null,
          totalBytes: frame.length,
          checksum,
          redaction: { applied: true },
          items: [{
            id: 'g4b-camera-frame',
            role: 'input-image',
            contentType: 'image/x-portable-pixmap',
            relativePath: 'g4b-camera-frame.ppm',
            sizeBytes: frame.length,
            sha256: checksum,
            available: true,
            missingReason: null
          }]
        }
      })
    });
  });
  await page.route(/\/api\/inspection\/history\/[0-9a-f-]{36}\/[0-9a-f-]{36}\/evidence\/export$/i, async route => {
    if (route.request().method() !== 'GET') return route.fallback();
    await route.fulfill({
      status: 200,
      contentType: 'application/zip',
      headers: {
        'Content-Disposition': 'attachment; filename="g4b-webview2-evidence.zip"',
        'X-G4B-Harness': 'evidence-export'
      },
      body: frame
    });
  });
  return {
    cameraBinding: binding,
    cameraFrame: { path: imageEvidence.filePath, sha256: checksum, bytes: frame.length },
    evidenceManifest: 'HARNESS_PROJECTION_OVER_REAL_WEBVIEW2_RESULT_ROUTE',
    realCamera: 'NOT_PERFORMED'
  };
}

async function captureG4BScene(page, evidenceDirectory, runName, scene, sourceSha) {
  await waitForDoubleAnimationFrame(page);
  const projection = await page.evaluate(() => ({
    route: window.location.hash.replace(/^#/, ''),
    theme: document.documentElement.dataset.theme || null,
    density: document.documentElement.dataset.density || null,
    viewport: { width: window.innerWidth, height: window.innerHeight, dpr: window.devicePixelRatio },
    overflow: {
      horizontal: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      vertical: document.documentElement.scrollHeight - document.documentElement.clientHeight
    },
    activeElement: document.activeElement instanceof HTMLElement ? {
      tagName: document.activeElement.tagName,
      testId: document.activeElement.dataset.testid || null,
      ariaLabel: document.activeElement.getAttribute('aria-label')
    } : null,
    modalTitle: document.querySelector('[role="dialog"] h2')?.textContent?.trim() || null,
    capabilities: [...document.querySelectorAll('[data-capability]')]
      .map(element => element.getAttribute('data-capability')).filter(Boolean),
    hostBridgeAvailable: Boolean(window.chrome?.webview),
    ownerCount: Number(document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-owner-count') ?? 0)
  }));
  assert(projection.theme === 'light' && projection.density === 'compact',
    `G4B scene did not retain light/compact: ${JSON.stringify(projection)}`);
  assert(projection.overflow.horizontal <= 1,
    `G4B scene overflowed horizontally: ${JSON.stringify(projection)}`);
  assert(projection.hostBridgeAvailable, 'G4B scene did not run inside the real WebView2 HostBridge surface.');
  const buffer = await page.screenshot({ type: 'png', animations: 'disabled' });
  const png = writePngEvidence(evidenceDirectory, `g4b-${safeFileName(runName)}-${scene}.png`, buffer);
  return { scene, sourceSha, projection, screenshot: png };
}

async function verifyWorkspaceG4B(page, seed, evidenceDirectory, runName, sourceSha) {
  assert(seed?.cameraNodeId && seed?.judgmentNodeId && seed?.judgmentOutputId && seed?.judgmentParameterId,
    `G4B seed is incomplete: ${JSON.stringify(seed)}`);
  const scenes = [];
  scenes.push(await captureG4BScene(page, evidenceDirectory, runName, 'workspace', sourceSha));

  const canvas = page.locator('[data-testid="flow-canvas"]');
  const canvasBox = await canvas.boundingBox();
  assert(canvasBox, 'G4B camera selection could not resolve the Flow Canvas bounds.');
  await page.mouse.click(canvasBox.x + 100, canvasBox.y + 225);
  const camera = page.locator('[data-capability="camera-binding-editor"]');
  await camera.waitFor({ state: 'visible', timeout: 30_000 });
  await camera.getByLabel('相机绑定').selectOption(seed.cameraBindingId);
  await waitForDoubleAnimationFrame(page);
  const cameraSelection = await page.evaluate(() => ({
    value: document.querySelector('[data-capability="camera-binding-editor"] select')?.value || null,
    message: document.querySelector('[data-capability="camera-binding-editor"] [role="status"]')?.textContent?.trim() || null,
    mutationGate: document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]')
      ?.getAttribute('data-mutation-gate') || null,
    inspectorName: document.querySelector('.inspector-panel__field input')?.value || null
  }));
  assert(cameraSelection.value === seed.cameraBindingId || cameraSelection.message?.includes('已绑定相机'),
    `G4B Camera Binding selection failed: ${JSON.stringify(cameraSelection)}`);
  await page.locator('[data-flow-command="toggle-disabled"]').click();
  await waitForFlowSurfaceNumber(page, 'data-selected-disabled-count', 1);
  scenes.push(await captureG4BScene(page, evidenceDirectory, runName, 'camera-binding', sourceSha));

  await page.locator('[data-testid="global-variables"]').click();
  const variables = page.locator('[data-capability="global-variables-workbench"]');
  await variables.waitFor({ state: 'visible', timeout: 30_000 });
  await variables.getByLabel('名称', { exact: true }).fill('G4BDecision');
  await variables.getByLabel('显示名称', { exact: true }).fill('G4B 判定文本');
  await variables.locator('select[name="global-variable-value-type"]').selectOption('String');
  await variables.getByLabel('默认 / 手动初始值').fill('OK');
  await variables.getByRole('button', { name: '添加定义' }).click();
  await variables.getByRole('button', { name: '绑定', exact: true }).click();
  await variables.locator('select[name="global-variable-source-variable"]').selectOption({ label: 'G4B 判定文本' });
  await variables.locator('select[name="global-variable-source-output"]')
    .selectOption(`${seed.judgmentNodeId}:${seed.judgmentOutputId}`);
  await variables.getByRole('button', { name: '添加来源' }).click();
  await variables.locator('select[name="global-variable-target-variable"]').selectOption({ label: 'G4B 判定文本' });
  await variables.locator('select[name="global-variable-target-parameter"]')
    .selectOption(`${seed.judgmentNodeId}:${seed.judgmentParameterId}`);
  await variables.getByRole('button', { name: '添加绑定' }).click();
  scenes.push(await captureG4BScene(page, evidenceDirectory, runName, 'global-variables', sourceSha));
  await page.getByRole('button', { name: '应用到工程草稿' }).click();

  await page.locator('[data-testid="final-decision"]').click();
  const decision = page.locator('[data-capability="final-decision-workbench"]');
  await decision.locator('select[name="final-decision-candidate"]')
    .selectOption(`${seed.judgmentNodeId}:${seed.judgmentOutputId}`);
  await decision.locator('input[name="final-decision-ok-value"]').fill('OK');
  await decision.locator('input[name="final-decision-ng-value"]').fill('NG');
  scenes.push(await captureG4BScene(page, evidenceDirectory, runName, 'final-decision', sourceSha));
  await page.getByRole('button', { name: '校验并应用' }).click();
  await decision.waitFor({ state: 'detached', timeout: 30_000 });

  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const initialPersistenceRevision = Number(await shell.getAttribute('data-workspace-persistence-revision'));
  await selectSeededRoiNode(page);
  await waitForSeededPreview(page);
  await page.locator('[data-testid="roi-start"]').click();
  await page.locator('[data-testid="image-canvas"]').focus();
  await page.keyboard.press('ArrowRight');
  await page.locator('[data-testid="roi-confirm"]').click();
  await waitForSeededPreview(page);
  scenes.push(await captureG4BScene(page, evidenceDirectory, runName, 'preview-roi', sourceSha));

  await page.locator('[data-testid="workspace-save"]').click();
  await page.waitForFunction(() => {
    const surface = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    return surface?.getAttribute('data-workspace-persistence-phase') === 'saved' &&
      surface.getAttribute('data-workspace-dirty') === 'false';
  }, null, { timeout: 30_000 });
  const savedPersistenceRevision = Number(await shell.getAttribute('data-workspace-persistence-revision'));
  assert(savedPersistenceRevision > initialPersistenceRevision,
    `G4B save did not advance PersistenceRevision: ${initialPersistenceRevision} -> ${savedPersistenceRevision}`);
  const persisted = await page.evaluate(async projectId => {
    const token = sessionStorage.getItem('cv_auth_token');
    const response = await fetch(`/api/projects/${projectId}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
      cache: 'no-store'
    });
    return { status: response.status, body: await response.json() };
  }, await shell.getAttribute('data-workspace-project-id'));
  const persistedCamera = persisted.body.flow.operators.find(operator => operator.id === seed.cameraNodeId);
  const persistedCameraBinding = persistedCamera?.parameters.find(
    parameter => parameter.name === seed.cameraBindingParameterName
  )?.value;
  assert(persisted.status === 200 && persistedCameraBinding === seed.cameraBindingId,
    `G4B camera binding was not persisted: ${JSON.stringify(persisted)}`);
  assert(persisted.body.globalVariables?.variables?.some(variable => variable.name === 'G4BDecision') &&
    persisted.body.globalVariables?.sourceBindings?.length === 1 &&
    persisted.body.globalVariables?.targetBindings?.length === 1,
  `G4B GlobalVariables were not persisted: ${JSON.stringify(persisted.body.globalVariables)}`);
  assert(persisted.body.flow.decisionConfiguration?.finalDecisionBinding?.sourceOperatorId === seed.judgmentNodeId,
    `G4B FinalDecision was not persisted: ${JSON.stringify(persisted.body.flow.decisionConfiguration)}`);
  scenes.push(await captureG4BScene(page, evidenceDirectory, runName, 'saved', sourceSha));
  return { scenes, initialPersistenceRevision, savedPersistenceRevision, persistedProjectStatus: persisted.status };
}

async function verifyG4BResultAndPackage(page, workspaceG6, evidenceDirectory, runName, sourceSha) {
  const origin = new URL(page.url()).origin;
  await page.goto(`${origin}/studio/index.html#/results?source=local&projectId=${workspaceG6.projectId}&resultId=${workspaceG6.resultId}`, {
    waitUntil: 'domcontentloaded', timeout: 45_000
  });
  const evidence = page.locator('[data-capability="result-evidence"]');
  await evidence.waitFor({ state: 'visible', timeout: 45_000 });
  await page.waitForFunction(() =>
    document.querySelector('[data-capability="result-evidence"]')
      ?.getAttribute('data-evidence-phase') === 'available', null, { timeout: 30_000 });
  const downloadPromise = page.waitForEvent('download');
  await page.locator('[data-testid="result-evidence-export"]').click();
  const download = await downloadPromise;
  const resultScene = await captureG4BScene(page, evidenceDirectory, runName, 'result-evidence', sourceSha);

  await page.locator('[data-testid="results-return-workspace"]').click();
  await page.locator('[data-evidence-surface="f03-workspace-shell"]').waitFor({ state: 'visible', timeout: 45_000 });
  await page.locator('[data-testid="runtime-package-export"]').click();
  const packageDialog = page.locator('[data-capability="runtime-package-export"]');
  await packageDialog.waitFor({ state: 'visible', timeout: 30_000 });
  const responsePromise = page.waitForResponse(response =>
    response.request().method() === 'POST' &&
      /\/api\/projects\/[0-9a-f-]{36}\/runtime-package\/export$/i.test(new URL(response.url()).pathname));
  await page.getByRole('button', { name: '导出运行包', exact: true }).click();
  const response = await responsePromise;
  await page.waitForFunction(() =>
    document.querySelector('[data-capability="runtime-package-export"]')?.getAttribute('data-phase') === 'success',
  null, { timeout: 45_000 });
  const packageScene = await captureG4BScene(page, evidenceDirectory, runName, 'runtime-package', sourceSha);
  return {
    evidenceExportSuggestedName: download.suggestedFilename(),
    runtimePackageStatus: response.status(),
    scenes: [resultScene, packageScene]
  };
}

async function verifyWorkspaceG4(page) {
  await page.waitForSelector('[data-capability="flow-workspace"]', { state: 'visible', timeout: 30_000 });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const initialPersistenceRevision = Number(await shell.getAttribute('data-workspace-persistence-revision'));
  const { flowCanvas } = await selectSeededRoiNode(page);
  await waitForSeededPreview(page);
  const preview = page.locator('[data-capability="preview-workbench"]');
  const image = page.locator('[data-capability="image-canvas"]');
  const imageCanvas = page.locator('[data-testid="image-canvas"]');
  const flowSurface = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  const revisionBefore = Number(await flowSurface.getAttribute('data-flow-revision'));
  const readRoiParameters = () => page.evaluate(() => Object.fromEntries(
    ['X', 'Y', 'Width', 'Height'].map(name => [
      name,
      document.querySelector(`[data-parameter-name="${name}"] input`)?.value || null
    ])
  ));
  const roiBefore = await readRoiParameters();
  assert(roiBefore.X === '10' && roiBefore.Y === '10' && roiBefore.Width === '40' && roiBefore.Height === '30',
    `The seeded ROI parameters are unexpected: ${JSON.stringify(roiBefore)}`);

  await page.locator('[data-testid="image-actual-size"]').click();
  await page.waitForFunction(() => Number(
    document.querySelector('[data-capability="image-canvas"]')?.getAttribute('data-image-scale')) === 1);
  await page.locator('[data-testid="image-zoom-in"]').click();
  await page.waitForFunction(() => Number(
    document.querySelector('[data-capability="image-canvas"]')?.getAttribute('data-image-scale')) > 1);
  await page.locator('[data-testid="image-fit"]').click();

  const imageBox = await imageCanvas.boundingBox();
  assert(imageBox, 'The G4 ImageCanvas did not expose a bounding box.');
  await page.mouse.click(imageBox.x + imageBox.width / 2, imageBox.y + imageBox.height / 2);
  await page.waitForFunction(() =>
    document.querySelector('.image-viewport__probe')?.getAttribute('data-probe-phase') === 'locked');

  await page.locator('[data-testid="roi-start"]').click();
  await page.waitForFunction(() =>
    document.querySelector('.preview-panel__roi')?.getAttribute('data-roi-phase') === 'editing');
  await imageCanvas.focus();
  await page.keyboard.press('ArrowRight');
  assert(await page.locator('[data-testid="roi-confirm"]').isEnabled(), 'ROI confirm did not enable after editing.');
  assert(Number(await flowSurface.getAttribute('data-flow-revision')) === revisionBefore,
    'ROI draft changed the Flow revision before confirmation.');
  await page.locator('[data-testid="roi-confirm"]').click();
  await waitForFlowSurfaceNumber(page, 'data-flow-revision', revisionBefore + 1);
  await page.waitForFunction(before => {
    const current = Object.fromEntries(['X', 'Y', 'Width', 'Height'].map(name => [
      name,
      document.querySelector(`[data-parameter-name="${name}"] input`)?.value || null
    ]));
    return JSON.stringify(current) !== JSON.stringify(before);
  }, roiBefore);
  const roiAfter = await readRoiParameters();

  await flowCanvas.focus();
  await page.keyboard.press('Control+z');
  await page.waitForFunction(expected => {
    const current = Object.fromEntries(['X', 'Y', 'Width', 'Height'].map(name => [
      name,
      document.querySelector(`[data-parameter-name="${name}"] input`)?.value || null
    ]));
    return JSON.stringify(current) === JSON.stringify(expected);
  }, roiBefore);
  await page.keyboard.press('Control+y');
  await page.waitForFunction(expected => {
    const current = Object.fromEntries(['X', 'Y', 'Width', 'Height'].map(name => [
      name,
      document.querySelector(`[data-parameter-name="${name}"] input`)?.value || null
    ]));
    return JSON.stringify(current) === JSON.stringify(expected);
  }, roiAfter);
  await waitForSeededPreview(page);

  await page.locator('[data-testid="roi-start"]').click();
  await imageCanvas.focus();
  await page.keyboard.press('ArrowRight');
  await page.locator('[data-testid="roi-cancel"]').click();
  assert(JSON.stringify(await readRoiParameters()) === JSON.stringify(roiAfter),
    'ROI cancel mutated the Flow draft.');

  assert(await shell.getAttribute('data-workspace-dirty') === 'true',
    'The confirmed ROI edit did not mark the Workspace dirty.');
  const saveButton = page.locator('[data-testid="workspace-save"]');
  assert(await saveButton.isEnabled(), 'The G5 save command did not enable for the ROI draft.');
  await saveButton.click();
  await page.waitForFunction(() => {
    const surface = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    return surface?.getAttribute('data-workspace-persistence-phase') === 'saved' &&
      surface.getAttribute('data-workspace-dirty') === 'false';
  }, null, { timeout: 30_000 });
  const savedPersistenceRevision = Number(await shell.getAttribute('data-workspace-persistence-revision'));
  assert(savedPersistenceRevision > initialPersistenceRevision,
    `Project PUT did not advance PersistenceRevision: ${initialPersistenceRevision} -> ${savedPersistenceRevision}.`);
  const savedProject = await page.evaluate(async () => {
    const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    const projectId = shell?.getAttribute('data-workspace-project-id');
    const token = sessionStorage.getItem('cv_auth_token');
    const response = await fetch(`/api/projects/${projectId}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
      cache: 'no-store'
    });
    return { status: response.status, body: await response.json() };
  });
  assert(savedProject.status === 200, `Post-save Project GET returned ${savedProject.status}.`);
  assert(Number(savedProject.body.persistenceRevision) === savedPersistenceRevision,
    'Post-save Project GET did not return the saved PersistenceRevision.');
  const savedRoi = savedProject.body.flow.operators.find(operator => operator.id === roiAfter.id) ||
    savedProject.body.flow.operators.find(operator => operator.name === 'G4 ROI Rectangle');
  const savedRoiValues = Object.fromEntries((savedRoi?.parameters || []).map(parameter => [parameter.name, parameter.value]));
  assert(String(savedRoiValues.X) === String(roiAfter.X) && String(savedRoiValues.Y) === String(roiAfter.Y) &&
    String(savedRoiValues.Width) === String(roiAfter.Width) && String(savedRoiValues.Height) === String(roiAfter.Height),
  `Post-save Project GET lost ROI parameters: ${JSON.stringify({ roiAfter, savedRoiValues })}`);
  await waitForSeededPreview(page);

  const projection = await page.evaluate(() => {
    const diagnostics = window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__;
    const previewSurface = document.querySelector('[data-capability="preview-workbench"]');
    const imageSurface = document.querySelector('[data-capability="image-canvas"]');
    return {
      previewPhase: previewSurface?.getAttribute('data-preview-phase') || null,
      previewStale: previewSurface?.getAttribute('data-preview-stale') || null,
      imagePhase: imageSurface?.getAttribute('data-image-phase') || null,
      imageScale: Number(imageSurface?.getAttribute('data-image-scale') || -1),
      imageDpr: Number(imageSurface?.getAttribute('data-image-dpr') || -1),
      probePhase: document.querySelector('.image-viewport__probe')?.getAttribute('data-probe-phase') || null,
      roiPhase: document.querySelector('.preview-panel__roi')?.getAttribute('data-roi-phase') || null,
      flowRevision: Number(document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]')
        ?.getAttribute('data-flow-revision') || -1),
      roiX: document.querySelector('[data-parameter-name="X"] input')?.value || null,
      diagnostics: diagnostics ? { ...diagnostics } : null
    };
  });
  return {
    ...projection,
    save: {
      initialPersistenceRevision,
      savedPersistenceRevision,
      postSaveGetStatus: savedProject.status,
      roiValues: savedRoiValues
    }
  };
}

async function installFormalRunDecision(page, formalRunSeed) {
  assert(formalRunSeed?.operator && formalRunSeed?.binding,
    'Formal Run evidence did not retain an unpersisted Final Decision seed.');
  const installed = await page.evaluate(async seed => {
    const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    const projectId = shell?.getAttribute('data-workspace-project-id');
    const token = sessionStorage.getItem('cv_auth_token');
    const headers = {
      Accept: 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    };
    const projectResponse = await fetch(`/api/projects/${projectId}`, {
      headers,
      cache: 'no-store'
    });
    const projectText = await projectResponse.text();
    const project = projectText ? JSON.parse(projectText) : null;
    if (!projectResponse.ok || !project?.flow) {
      return { getStatus: projectResponse.status, putStatus: null, project };
    }
    const operatorExists = project.flow.operators.some(operator => operator.id === seed.operator.id);
    const binding = project.flow.decisionConfiguration?.finalDecisionBinding;
    if (operatorExists && binding?.sourceOperatorId === seed.binding.sourceOperatorId &&
      binding?.sourceOutputPortId === seed.binding.sourceOutputPortId) {
      return {
        getStatus: projectResponse.status,
        putStatus: 200,
        previousPersistenceRevision: project.persistenceRevision,
        project,
        reused: true
      };
    }
    const response = await fetch(`/api/projects/${projectId}`, {
      method: 'PUT',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: project.name,
        description: project.description,
        expectedPersistenceRevision: project.persistenceRevision,
        flow: {
          ...project.flow,
          operators: operatorExists ? project.flow.operators : [...project.flow.operators, seed.operator],
          decisionConfiguration: {
            finalDecisionBinding: seed.binding,
            missingDecisionPolicy: 'Undetermined'
          }
        },
        globalVariables: project.globalVariables ?? null
      })
    });
    const text = await response.text();
    let body = null;
    try {
      body = text ? JSON.parse(text) : null;
    } catch {
      body = { raw: text };
    }
    return {
      getStatus: projectResponse.status,
      putStatus: response.status,
      previousPersistenceRevision: project.persistenceRevision,
      project: body,
      reused: false
    };
  }, formalRunSeed);
  assert(installed.getStatus === 200, `Formal Run decision Project GET returned ${installed.getStatus}.`);
  assert(installed.putStatus === 200,
    `Formal Run decision Project PUT returned ${installed.putStatus}: ${JSON.stringify(installed.project)}`);
  assert(installed.reused === true ||
    Number(installed.project?.persistenceRevision) > Number(installed.previousPersistenceRevision),
  `Formal Run decision PUT did not advance PersistenceRevision: ${JSON.stringify(installed)}`);
  assert(installed.project?.flow?.decisionConfiguration?.finalDecisionBinding?.sourceOperatorId ===
    formalRunSeed.binding.sourceOperatorId,
  `Formal Run decision PUT did not persist the configured source identity: ${JSON.stringify(installed.project)}`);

  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-evidence-surface="f03-workspace-shell"]', {
    state: 'visible',
    timeout: 30_000
  });
  try {
    await page.waitForFunction(expectedRevision => {
      const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
      return shell?.getAttribute('data-workspace-state') === 'ready' &&
        Number(shell.getAttribute('data-workspace-persistence-revision')) === expectedRevision &&
        shell.getAttribute('data-workspace-dirty') === 'false';
    }, Number(installed.project.persistenceRevision), { timeout: 30_000 });
  } catch (error) {
    const reloaded = await page.evaluate(() => {
      const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
      return {
        shell: shell ? Object.fromEntries([...shell.attributes].map(item => [item.name, item.value])) : null,
        diagnostics: window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__
          ? { ...window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__ }
          : null,
        text: document.querySelector('[data-evidence-surface="f03-workspace-shell"]')?.textContent?.trim() || null
      };
    });
    throw new Error(`Formal Run canonical reload did not settle: ${JSON.stringify(reloaded)}; ${error.message}`);
  }
  return {
    ...installed,
    reloadedPersistenceRevision: Number(await page.locator('[data-evidence-surface="f03-workspace-shell"]')
      .getAttribute('data-workspace-persistence-revision'))
  };
}

async function setFormalRunSlowFixture(page, formalRunSeed, enabled) {
  assert(formalRunSeed?.slowOperator?.id,
    'Formal Run evidence did not retain the deterministic slow operator fixture.');
  const updated = await page.evaluate(async ({ slowOperator, shouldEnable }) => {
    const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    const projectId = shell?.getAttribute('data-workspace-project-id');
    const token = sessionStorage.getItem('cv_auth_token');
    const headers = {
      Accept: 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    };
    const projectResponse = await fetch(`/api/projects/${projectId}`, { headers, cache: 'no-store' });
    const projectText = await projectResponse.text();
    const project = projectText ? JSON.parse(projectText) : null;
    if (!projectResponse.ok || !project?.flow) {
      return { getStatus: projectResponse.status, putStatus: null, project };
    }
    const withoutSlowFixture = project.flow.operators.filter(operator => operator.id !== slowOperator.id);
    const operators = shouldEnable ? [...withoutSlowFixture, slowOperator] : withoutSlowFixture;
    const response = await fetch(`/api/projects/${projectId}`, {
      method: 'PUT',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: project.name,
        description: project.description,
        expectedPersistenceRevision: project.persistenceRevision,
        flow: { ...project.flow, operators },
        globalVariables: project.globalVariables ?? null
      })
    });
    const text = await response.text();
    let body = null;
    try {
      body = text ? JSON.parse(text) : null;
    } catch {
      body = { raw: text };
    }
    return {
      getStatus: projectResponse.status,
      putStatus: response.status,
      previousPersistenceRevision: project.persistenceRevision,
      project: body,
      enabled: shouldEnable
    };
  }, { slowOperator: formalRunSeed.slowOperator, shouldEnable: enabled });
  assert(updated.getStatus === 200, `Formal Run slow fixture GET returned ${updated.getStatus}.`);
  assert(updated.putStatus === 200,
    `Formal Run slow fixture PUT returned ${updated.putStatus}: ${JSON.stringify(updated.project)}`);
  const hasSlowFixture = updated.project?.flow?.operators?.some(
    operator => operator.id === formalRunSeed.slowOperator.id
  );
  assert(hasSlowFixture === enabled,
    `Formal Run slow fixture state did not match ${enabled}: ${JSON.stringify(updated.project)}`);

  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-evidence-surface="f03-workspace-shell"]', {
    state: 'visible',
    timeout: 30_000
  });
  await page.waitForFunction(expectedRevision => {
    const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    return shell?.getAttribute('data-workspace-state') === 'ready' &&
      Number(shell.getAttribute('data-workspace-persistence-revision')) === expectedRevision &&
      shell.getAttribute('data-workspace-dirty') === 'false';
  }, Number(updated.project.persistenceRevision), { timeout: 30_000 });
  return updated;
}

async function readFormalResultsHandoff(page, projectId) {
  await page.waitForFunction(() =>
    Boolean(document.querySelector('[data-capability="results-read"]')) ||
      Boolean(document.querySelector('[data-testid="workspace-current-result"]')),
  null, { timeout: 45_000 });
  const currentResult = page.locator('[data-testid="workspace-current-result"]');
  if (await currentResult.isVisible()) {
    await currentResult.click();
  }
  await waitForSelectorWithoutHandle(
    page,
    '[data-capability="results-read"]',
    { state: 'visible', timeout: 45_000 }
  );
  const hash = new URL(page.url()).hash.replace(/^#/, '');
  const [route, query = ''] = hash.split('?');
  const params = new URLSearchParams(query);
  const resultId = params.get('resultId');
  assert(route === '/results', `Formal Run did not navigate to Results: ${hash}`);
  assert(params.get('source') === 'local', `Formal Run Results source was not local: ${hash}`);
  assert(params.get('projectId') === projectId, `Formal Run Results Project identity changed: ${hash}`);
  assert(resultId && /^[0-9a-f-]{36}$/i.test(resultId), `Formal Run Results did not contain a result id: ${hash}`);
  return { projectId, resultId, route: hash };
}

async function waitForPersistedWorkspace(page, projectId) {
  const origin = new URL(page.url()).origin;
  await page.goto(`${origin}/studio/index.html#/projects/${projectId}/workspace`, {
    waitUntil: 'domcontentloaded',
    timeout: 45_000
  });
  await page.waitForSelector('[data-evidence-surface="f03-workspace-shell"]', {
    state: 'visible',
    timeout: 45_000
  });
  await page.waitForFunction(() => {
    const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    return shell?.getAttribute('data-workspace-state') === 'ready' &&
      shell.getAttribute('data-workspace-dirty') === 'false';
  }, null, { timeout: 45_000 });
}

async function verifyWorkspaceG6(page, runtimeErrors, formalRunSeed, webPort, token) {
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const projectId = await shell.getAttribute('data-workspace-project-id');
  assert(projectId && /^[0-9a-f-]{36}$/i.test(projectId), 'Formal Run lost the persisted Project identity.');
  const run = page.locator('[data-testid="workspace-run"]');
  assert(await run.isEnabled(), 'Formal Run did not enable after the persisted Project save settled.');

  const normalAdmissionResponsePromise = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/inspection/admission' &&
    response.request().method() === 'POST'
  );
  const normalExecuteResponsePromise = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/inspection/execute' &&
    response.request().method() === 'POST'
  );
  await run.click();
  const [normalAdmissionResponse, normalExecuteResponse] = await Promise.all([
    normalAdmissionResponsePromise,
    normalExecuteResponsePromise
  ]);
  const [normalAdmission, normalResult] = await Promise.all([
    normalAdmissionResponse.json(),
    normalExecuteResponse.json()
  ]);
  const normalHandoff = await readFormalResultsHandoff(page, projectId);
  const normalRunIdentity = {
    projectId,
    clientSnapshotId: normalAdmission.clientSnapshotId,
    expectedPersistenceRevision: normalAdmission.projectPersistenceRevision,
    expectedCanonicalFlowHash: normalAdmission.canonicalFlowHash,
    expectedDecisionConfigurationHash: normalAdmission.decisionConfigurationHash
  };
  assert(normalResult.id === normalHandoff.resultId &&
    normalResult.projectId === projectId &&
    normalResult.executionSnapshotId === normalRunIdentity.clientSnapshotId &&
    normalResult.projectPersistenceRevision === normalRunIdentity.expectedPersistenceRevision &&
    normalResult.flowVersionHash === normalRunIdentity.expectedCanonicalFlowHash &&
    normalResult.decisionConfigurationHash === normalRunIdentity.expectedDecisionConfigurationHash,
  `Normal Formal Run identity changed between admission, execute, and Results: ${JSON.stringify({
    normalAdmission,
    normalResult,
    normalHandoff
  })}`);
  normalHandoff.runIdentity = normalRunIdentity;

  await waitForPersistedWorkspace(page, projectId);
  let responseLossSeen = false;
  const withholdExecuteResponse = async route => {
    const response = await route.fetch();
    responseLossSeen = true;
    await route.fulfill({
      status: 599,
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ code: 'WEBVIEW2_RESPONSE_WITHHELD', error: 'Evidence runner withheld the execute response.' })
    });
  };
  await page.route('**/api/inspection/execute', withholdExecuteResponse);
  let reconcileHandoff;
  try {
    await page.locator('[data-testid="workspace-run"]').click();
    await page.waitForFunction(() =>
      document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
        ?.getAttribute('data-workspace-run-phase') === 'unknown-outcome',
      null,
      { timeout: 45_000 }
    );
    assert(responseLossSeen, 'Real WebView2 execute response-loss fixture did not reach the server.');
    await page.locator('[data-testid="workspace-run-reconcile"]').click();
    reconcileHandoff = await readFormalResultsHandoff(page, projectId);
  } finally {
    await page.unroute('**/api/inspection/execute', withholdExecuteResponse);
  }

  await waitForPersistedWorkspace(page, projectId);
  const slowFixtureEnabled = await setFormalRunSlowFixture(page, formalRunSeed, true);
  let executeResponseCompleted = false;
  const observeExecuteResponse = response => {
    if (new URL(response.url()).pathname === '/api/inspection/execute') executeResponseCompleted = true;
  };
  page.on('response', observeExecuteResponse);
  const admissionResponsePromise = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/inspection/admission' &&
    response.request().method() === 'POST'
  );
  await page.locator('[data-testid="workspace-run"]').click();
  const admissionResponse = await admissionResponsePromise;
  const admitted = await admissionResponse.json();
  const identity = {
    projectId,
    clientSnapshotId: admitted.clientSnapshotId,
    expectedPersistenceRevision: admitted.projectPersistenceRevision,
    expectedCanonicalFlowHash: admitted.canonicalFlowHash,
    expectedDecisionConfigurationHash: admitted.decisionConfigurationHash
  };
  const runningSignal = await waitForAuthoritativeRunStatus(
    webPort,
    token,
    identity,
    'still-running'
  );
  await page.waitForSelector('[data-testid="workspace-run-stop"]', {
    state: 'visible',
    timeout: 45_000
  });
  assert(await shell.getAttribute('data-workspace-run-phase') === 'executing',
    'Real WebView2 Formal Run was not executing after the coordinator reported Running.');
  assert(await shell.getAttribute('data-workspace-run-snapshot-id') === admitted.clientSnapshotId,
    'Real WebView2 Formal Run snapshot identity did not match admission.');
  assert(!executeResponseCompleted,
    'Real WebView2 Stop was attempted after the execute response had already completed.');

  const stopResponsePromise = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/inspection/stop' &&
    response.request().method() === 'POST'
  );
  await page.locator('[data-testid="workspace-run-stop"]').click();
  const stopResponse = await stopResponsePromise;
  const stopReconciliation = await stopResponse.json();
  const cancelledSignal = await waitForAuthoritativeRunStatus(
    webPort,
    token,
    identity,
    'cancelled'
  );
  await page.waitForFunction(() => {
    const phase = document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-run-phase');
    return phase === 'cancelled' || phase === 'cancel-requested' || phase === 'unknown-outcome';
  }, null, { timeout: 45_000 });
  let uiReconciliation = null;
  if (await shell.getAttribute('data-workspace-run-phase') !== 'cancelled') {
    const reconcileResponsePromise = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/api/inspection/reconcile' &&
      response.request().method() === 'POST'
    );
    await page.waitForSelector('[data-testid="workspace-run-reconcile"]', {
      state: 'visible',
      timeout: 45_000
    });
    await page.locator('[data-testid="workspace-run-reconcile"]').click();
    uiReconciliation = await (await reconcileResponsePromise).json();
  }
  await page.waitForFunction(() =>
    document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-run-phase') === 'cancelled',
  null, { timeout: 45_000 });
  page.off('response', observeExecuteResponse);

  const cancelledResult = cancelledSignal.reconciliation.result;
  assert(cancelledResult?.executionOutcome === 'Cancelled',
    `Cancelled reconcile did not return a Cancelled result: ${JSON.stringify(cancelledSignal)}`);
  assert(cancelledResult.executionSnapshotId === admitted.clientSnapshotId &&
    cancelledResult.projectPersistenceRevision === admitted.projectPersistenceRevision &&
    cancelledResult.flowVersionHash === admitted.canonicalFlowHash &&
    cancelledResult.decisionConfigurationHash === admitted.decisionConfigurationHash,
  `Cancelled result identity changed: ${JSON.stringify(cancelledResult)}`);
  const storedCancelledResult = await readAuthorizedJson(
    webPort,
    token,
    `/api/inspection/history/${projectId}/${cancelledResult.id}`
  );
  assert(storedCancelledResult.id === cancelledResult.id &&
    storedCancelledResult.projectId === projectId &&
    storedCancelledResult.executionOutcome === 'Cancelled' &&
    storedCancelledResult.flowVersionHash === admitted.canonicalFlowHash,
  `Stored Cancelled result did not preserve identity: ${JSON.stringify(storedCancelledResult)}`);
  assert(new URL(page.url()).hash.includes(`/projects/${projectId}/workspace`),
    `Cancelled Formal Run navigated away from Workspace: ${page.url()}`);
  assert(await page.locator('[data-capability="results-read"]').count() === 0,
    'Cancelled Formal Run mounted the success Results page.');
  assert(await shell.getAttribute('data-workspace-persistence-phase') === 'clean',
    'Cancelled Formal Run did not release the persistence mutation gate.');
  assert(await page.locator('[data-evidence-surface="f03-g2-flow-canvas"]')
    .getAttribute('data-mutation-gate') === 'editable',
  'Cancelled Formal Run did not release the Flow mutation gate.');
  await selectSeededRoiNode(page);
  await waitForSeededPreview(page);
  assert(await page.locator('[data-testid="roi-start"]').isEnabled(),
    'Cancelled Formal Run did not release the ROI mutation gate.');
  const roiX = page.locator('[data-parameter-name="X"] input');
  const previousRoiX = Number(await roiX.inputValue());
  await roiX.fill(String(previousRoiX + 1));
  await roiX.press('Enter');
  await page.waitForFunction(() =>
    document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-dirty') === 'true',
  null, { timeout: 30_000 });
  assert(await page.locator('[data-testid="workspace-save"]').isEnabled(),
    'Cancelled Formal Run did not re-enable save after an ROI mutation.');
  const revisionBeforeUnlockedSave = Number(await shell.getAttribute('data-workspace-persistence-revision'));
  const unlockedSaveResponsePromise = page.waitForResponse(response =>
    response.request().method() === 'PUT' &&
    new URL(response.url()).pathname === `/api/projects/${projectId}`
  );
  await page.locator('[data-testid="workspace-save"]').click();
  const unlockedSaveResponse = await unlockedSaveResponsePromise;
  const unlockedSave = await unlockedSaveResponse.json();
  assert(unlockedSaveResponse.status() === 200 &&
    Number(unlockedSave.persistenceRevision) > revisionBeforeUnlockedSave,
  `Cancelled Formal Run save did not reach the authoritative Project service: ${JSON.stringify(unlockedSave)}`);

  const runPaths = runtimeErrors.requests
    .map(item => new URL(item.url).pathname)
    .filter(path => /^\/api\/inspection\/(admission|execute|stop|reconcile)$/.test(path));
  return {
    projectId,
    resultId: reconcileHandoff.resultId,
    route: reconcileHandoff.route,
    normalHandoff,
    genuineRunningStop: {
      fixture: 'DETERMINISTIC_DELAY_OPERATOR_60000MS',
      slowFixtureEnabled,
      runtimeRunningSignal: runningSignal,
      executeResponseCompletedBeforeStop: false,
      stopReconciliation,
      cancelledSignal,
      uiReconciliation,
      cancelledResult,
      storedCancelledResult,
      workspaceRouteRetained: true,
      mutationSaveRoiUnlocked: true
    },
    reconcileHandoff,
    runPaths,
    responseLoss: {
      transport: 'REAL_WEBVIEW2_ROUTE_WITHHELD_AFTER_SERVER_RESPONSE',
      backendResultRecovered: true
    }
  };
}

async function verifyWorkspaceG3(page) {
  await page.waitForSelector('[data-capability="flow-workspace"]', { state: 'visible', timeout: 30_000 });
  await page.waitForFunction(() =>
    document.querySelector('[data-evidence-surface="f03-g2-operator-rail"]')
      ?.getAttribute('data-catalog-phase') === 'success', null, { timeout: 30_000 });
  await page.waitForFunction(() =>
    document.querySelector('[data-capability="flow-workspace"]')
      ?.getAttribute('data-flow-owner-phase') === 'mounted', null, { timeout: 30_000 });

  const canvas = page.locator('[data-testid="flow-canvas"]');
  const box = await canvas.boundingBox();
  assert(box && box.width >= 500 && box.height >= 300,
    `Formal Flow Canvas is undersized: ${JSON.stringify(box)}`);
  const threshold = await readOperatorDefinition(page, ['Thresholding', '4']);
  const thresholdName = await threshold.item.getAttribute('data-name');
  assert(thresholdName, 'Thresholding operator did not expose a display name.');
  await page.locator('[data-testid="operator-search"]').fill(thresholdName);
  const searchMatchCount = await page.locator('.operator-item').count();
  assert(searchMatchCount === 1, `Operator search returned ${searchMatchCount} matches instead of one.`);
  await threshold.item.click();
  await waitForFlowSurfaceNumber(page, 'data-node-count', 1);
  await waitForFlowSurfaceNumber(page, 'data-flow-revision', 1);
  const inspectorG3 = await verifyWorkspaceInspectorG3(page, thresholdName);

  await resetOperatorRailFilters(page);
  const category = page.locator('.operator-rail__categories [data-category]').first();
  const categoryId = await category.getAttribute('data-category');
  await category.click();
  const categoryMatchCount = await page.locator('.operator-item').count();
  assert(categoryId && categoryMatchCount > 0, 'Operator category filtering produced no visible operators.');
  await resetOperatorRailFilters(page);

  const thresholdPosition = { x: box.width / 2, y: box.height / 2 };
  const sourcePosition = { x: 48, y: 56 };
  const source = await dragOperatorToCanvas(page, ['ImageAcquisition', '0'], sourcePosition);
  await waitForFlowSurfaceNumber(page, 'data-node-count', 2);
  const regionDefinition = await readOperatorDefinition(page, ['RegionErosion', '240']);
  const regionPosition = {
    x: Math.min(box.width - 150, thresholdPosition.x),
    y: Math.min(box.height - operatorNodeHeight(regionDefinition) - 8, thresholdPosition.y + 112)
  };
  await regionDefinition.item.dragTo(canvas, { targetPosition: regionPosition });
  await waitForFlowSurfaceNumber(page, 'data-node-count', 3);

  const sourceOutput = operatorPortPoint(sourcePosition, source, true);
  const thresholdInput = operatorPortPoint(thresholdPosition, threshold.definition, false);
  const thresholdOutput = operatorPortPoint(thresholdPosition, threshold.definition, true);
  const regionInput = operatorPortPoint(regionPosition, regionDefinition.definition, false);
  await dragCanvasPointer(page, sourceOutput, thresholdInput);
  await waitForFlowSurfaceNumber(page, 'data-connection-count', 1);
  const legalConnection = await readFlowSurface(page);

  await dragCanvasPointer(page, thresholdOutput, regionInput);
  await waitForFlowSurfaceNumber(page, 'data-connection-count', 1);
  const illegalConnection = await readFlowSurface(page);
  assert(/Region|不兼容|不匹配/.test(illegalConnection.feedback),
    `Illegal connection did not expose a stable reason: ${illegalConnection.feedback}`);

  const canvasOrigin = await canvas.boundingBox();
  assert(canvasOrigin, 'Flow Canvas lost its bounding box before disconnect.');
  await page.mouse.click(
    canvasOrigin.x + (sourceOutput.x + thresholdInput.x) / 2,
    canvasOrigin.y + (sourceOutput.y + thresholdInput.y) / 2
  );
  await page.waitForFunction(() =>
    document.querySelector('[data-evidence-surface="f03-g3-inspector"]')
      ?.getAttribute('data-inspector-mode') === 'connection', null, { timeout: 30_000 });
  await page.locator('.inspector-panel__danger').click();
  await waitForFlowSurfaceNumber(page, 'data-connection-count', 0);

  const thresholdHit = { x: thresholdPosition.x + 44, y: thresholdPosition.y + 16 };
  const movedThresholdHit = { x: thresholdHit.x + 42, y: thresholdHit.y + 24 };
  await dragCanvasPointer(page, thresholdHit, movedThresholdHit);
  const moved = await readFlowSurface(page);
  assert(moved.selectedCount === 1 && moved.flowRevision >= 6,
    `Node move did not commit one selected draft mutation: ${JSON.stringify(moved)}`);

  await page.locator('[data-flow-command="toggle-disabled"]').click();
  await waitForFlowSurfaceNumber(page, 'data-selected-disabled-count', 1);
  await page.locator('[data-flow-command="toggle-disabled"]').click();
  await waitForFlowSurfaceNumber(page, 'data-selected-disabled-count', 0);

  await canvas.focus();
  await page.keyboard.press('Control+c');
  await page.keyboard.press('Control+v');
  await waitForFlowSurfaceNumber(page, 'data-node-count', 4);
  await page.keyboard.press('Control+z');
  await waitForFlowSurfaceNumber(page, 'data-node-count', 3);
  await page.keyboard.press('Control+y');
  await waitForFlowSurfaceNumber(page, 'data-node-count', 4);

  const search = page.locator('[data-testid="operator-search"]');
  await search.focus();
  await page.keyboard.press('Control+a');
  await page.keyboard.press('Backspace');
  const afterInputShortcut = await readFlowSurface(page);
  assert(afterInputShortcut.nodeCount === 4, 'Canvas shortcut escaped into the Operator search input.');

  const selectedPoint = { x: sourcePosition.x + 40, y: sourcePosition.y + 16 };
  const currentBox = await canvas.boundingBox();
  assert(currentBox, 'Flow Canvas lost its bounding box before the IME gate check.');
  await page.mouse.click(currentBox.x + selectedPoint.x, currentBox.y + selectedPoint.y);
  await waitForFlowSurfaceNumber(page, 'data-selected-count', 1);
  await canvas.dispatchEvent('keydown', {
    key: 'Delete',
    code: 'Delete',
    isComposing: true,
    bubbles: true,
    cancelable: true
  });
  assert((await readFlowSurface(page)).nodeCount === 4, 'IME composition triggered a Canvas delete shortcut.');
  await canvas.focus();
  await page.keyboard.press('Delete');
  await waitForFlowSurfaceNumber(page, 'data-node-count', 3);

  const scaleBefore = (await readFlowSurface(page)).scale;
  const zoomBox = await canvas.boundingBox();
  assert(zoomBox, 'Flow Canvas lost its bounding box before wheel zoom.');
  await page.mouse.move(zoomBox.x + zoomBox.width / 2, zoomBox.y + zoomBox.height / 2);
  await page.mouse.wheel(0, -240);
  await page.waitForFunction(previous =>
    Number(document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]')
      ?.getAttribute('data-scale')) > previous, scaleBefore, { timeout: 30_000 });
  const final = await readFlowSurface(page);
  assert(final.minimapCount === 1, 'Formal Flow Canvas did not mount exactly one minimap.');

  return {
    initial: { nodeCount: 0, connectionCount: 0, flowRevision: 0 },
    operatorRail: { searchMatchCount, categoryId, categoryMatchCount },
    inspectorG3,
    legalConnection,
    illegalConnection,
    inputShortcutGate: afterInputShortcut,
    scaleBefore,
    final
  };
}

async function verifyProductPage(
  page,
  route,
  runtimeErrors,
  formalRunSeed = null,
  webPort = null,
  token = null,
  phase = 'full',
  workspaceSeedRoiNodeId = null,
  goldenJourneySeed = null,
  evidenceDirectory = null,
  runName = 'g4b',
  sourceSha = 'unknown'
) {
  await page.waitForSelector('[data-product-shell="ready"]', { state: 'visible', timeout: 30_000 });
  const isWorkspaceRoute = /^\/projects\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\/workspace$/i
    .test(route);
  const selectors = [
    ['/projects/', '[data-evidence-surface="f03-workspace-shell"]'],
    ['/projects', '[data-capability="projects-read"]'],
    ['/operators', '[data-capability="operators-read"]'],
    ['/stations', '[data-capability="stations-read"]'],
    ['/results', '[data-capability="results-read"]']
  ];
  const selector = isWorkspaceRoute
    ? '[data-evidence-surface="f03-workspace-shell"]'
    : selectors.find(([prefix]) => route.startsWith(prefix))?.[1]
    || '[data-capability="overview"]';
  await page.waitForSelector(selector, { state: 'visible', timeout: 30_000 });
  if (isWorkspaceRoute) {
    await page.waitForFunction(() => {
      const state = document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
        ?.getAttribute('data-workspace-state');
      return Boolean(state && state !== 'loading');
    }, null, { timeout: 30_000 });
  }
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => {
    const user = document.querySelector('.product-layout__user strong')?.textContent?.trim();
    return Boolean(user && user !== '未认证');
  }, null, { timeout: 30_000 });

  const projection = await page.evaluate(() => ({
    shellCount: document.querySelectorAll('[data-product-shell]').length,
    internalLabCount: document.querySelectorAll('[data-internal-lab-layout]').length,
    capability: document.querySelector('[data-capability]')?.getAttribute('data-capability') || null,
    formalNavigation: [...document.querySelectorAll('[data-product-nav]')]
      .map(node => node.getAttribute('data-product-nav')),
    startupFeatureFlags: { ...(window.__CLEARVISION_STARTUP__?.featureFlags || {}) },
    labNavigationCount: document.querySelectorAll('[data-product-nav^="/labs"]').length,
    theme: document.documentElement.dataset.theme || null,
    density: document.documentElement.dataset.density || null,
    horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    verticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight,
    mainCount: document.querySelectorAll('main').length,
    workspaceMode: document.querySelector('[data-product-shell]')?.getAttribute('data-workspace-mode') || null,
    workspaceShellCount: document.querySelectorAll('[data-evidence-surface="f03-workspace-shell"]').length,
    workspaceState: document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-state') || null,
    ownerLedger: {
      studio: window.__STUDIO_UI_DIAGNOSTICS__ ? { ...window.__STUDIO_UI_DIAGNOSTICS__ } : null,
      projectLifecycle: window.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__
        ? { ...window.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__ }
        : null,
      leaveGuard: window.__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__
        ? { ...window.__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__ }
        : null,
      workspace: window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__
        ? { ...window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__ }
        : null
    },
    dataSource: document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ? 'REAL_WEBVIEW2_PROJECT_AUTHORITY'
      : 'REAL_WEBVIEW2_EMPTY_AUTHORITY',
    authSource: 'HARNESS_SEEDED_SESSION'
  }));
  const navigationContract = resolveProductNavigationContract(phase, projection.startupFeatureFlags);
  const isF04Evidence = navigationContract.phase === 'f04';
  const origin = new URL(page.url()).origin;
  const isProductRequest = item => {
    const url = new URL(item.url);
    return url.origin === origin && (url.pathname === '/health' || url.pathname.startsWith('/api/'));
  };

  const preferenceRequestStart = runtimeErrors.requests.length;
  await page.locator('[data-product-appearance] > summary').click();
  const setPreference = async (groupName, buttonName, attribute, expectedValue) => {
    const group = page.getByRole('group', { name: groupName });
    const button = group.getByRole('button', { name: buttonName, exact: true });
    await button.click();
    await page.waitForFunction(
      ({ attributeName, value }) => document.documentElement.getAttribute(attributeName) === value,
      { attributeName: attribute, value: expectedValue }
    );
    return page.evaluate(({ attributeName, groupLabel }) => ({
      attributeValue: document.documentElement.getAttribute(attributeName),
      pressed: document.querySelector(
        `[role="group"][aria-label="${groupLabel}"] button[aria-pressed="true"]`
      )?.textContent?.trim() || null
    }), { attributeName: attribute, groupLabel: groupName });
  };
  const preferenceCycle = {
    initial: { theme: projection.theme, density: projection.density },
    dark: await setPreference('主题', '深色', 'data-theme', 'dark'),
    comfortable: await setPreference('界面密度', '舒适', 'data-density', 'comfortable'),
    light: await setPreference('主题', '浅色', 'data-theme', 'light'),
    compact: await setPreference('界面密度', '紧凑', 'data-density', 'compact'),
    final: await page.evaluate(() => ({
      theme: document.documentElement.dataset.theme || null,
      density: document.documentElement.dataset.density || null,
      stored: JSON.parse(localStorage.getItem('clearvision.studio-ui.preferences.v1') || 'null')
    }))
  };
  await page.locator('[data-product-appearance] > summary').click();
  assert(await page.locator('[data-product-appearance]').getAttribute('open') === null,
    'Appearance disclosure did not close after the preference audit.');
  const resultsFilterLayout = route.startsWith('/results')
    ? await page.evaluate(() => {
        const selectors = [
          '.results-page__source',
          '.results-page__project',
          '.results-page__outcome',
          '.results-page__diagnostic',
          '.results-page__date',
          '.results-page__page-size'
        ];
        const controls = selectors.flatMap(selector =>
          [...document.querySelectorAll(selector)].map(element => ({
            selector,
            top: Math.round(element.getBoundingClientRect().top * 100) / 100
          }))
        );
        const tops = controls.map(item => item.top);
        return {
          controls,
          maximumTopDelta: tops.length ? Math.max(...tops) - Math.min(...tops) : null
        };
      })
    : null;
  const preferenceRequests = runtimeErrors.requests.slice(preferenceRequestStart).filter(item => {
    const url = new URL(item.url);
    return url.origin === origin && (url.pathname === '/health' || url.pathname.startsWith('/api/'));
  });
  const preferenceWriteRequests = preferenceRequests.filter(item => item.method !== 'GET');
  const seededWorkspace = parseBooleanEnvironment('CV_STUDIO_UI_SEED_WORKSPACE');
  const formalRun = parseBooleanEnvironment('CV_STUDIO_UI_FORMAL_RUN');
  const goldenJourney = parseBooleanEnvironment('CV_STUDIO_UI_G4B_GOLDEN_JOURNEY');
  const dpiOnly = parseBooleanEnvironment('CV_STUDIO_UI_DPI_ONLY');
  const rollbackPhase = String(process.env.CV_STUDIO_UI_ROLLBACK_PHASE || '').trim().toUpperCase();
  const workspaceReady = isWorkspaceRoute && ['ready', 'empty'].includes(projection.workspaceState);
  assert(!formalRun || (seededWorkspace && workspaceReady),
    'Formal Run evidence requires a seeded, ready Workspace route.');
  assert(!dpiOnly || (seededWorkspace && workspaceReady && !formalRun),
    'DPI-only evidence requires a seeded, ready Workspace without Formal Run.');
  assert(!goldenJourney || (formalRun && goldenJourneySeed && evidenceDirectory),
    'G4B Golden Journey requires Formal Run, the dedicated seed, and an evidence directory.');
  const workspaceG4B = workspaceReady && goldenJourney
    ? await verifyWorkspaceG4B(page, goldenJourneySeed, evidenceDirectory, runName, sourceSha)
    : null;
  const workspaceG4 = workspaceReady && seededWorkspace && !dpiOnly && !goldenJourney
    ? await verifyWorkspaceG4(page)
    : null;
  const workspaceG3 = workspaceReady && !seededWorkspace && rollbackPhase !== 'NEXT_REOPEN'
    ? await verifyWorkspaceG3(page)
    : null;
  let workspaceLifecycle = null;
  if (isWorkspaceRoute) {
    const readWorkspace = () => page.evaluate(() => {
      const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
      const diagnostics = window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__;
      const activeElement = document.activeElement;
      return {
        state: shell?.getAttribute('data-workspace-state') || null,
        projectId: shell?.getAttribute('data-workspace-project-id') || null,
        persistencePhase: shell?.getAttribute('data-workspace-persistence-phase') || null,
        dirty: shell?.getAttribute('data-workspace-dirty') || null,
        dirtyGeneration: Number(shell?.getAttribute('data-workspace-dirty-generation') ?? -1),
        activeElement: activeElement ? {
          tagName: activeElement.tagName,
          testId: activeElement.getAttribute('data-testid'),
          parameterName: activeElement.closest('[data-parameter-name]')?.getAttribute('data-parameter-name') || null,
          value: 'value' in activeElement ? activeElement.value : null
        } : null,
        ownerCount: Number(shell?.getAttribute('data-workspace-owner-count') || -1),
        flowCanvasOwnerCount: Number(diagnostics?.flowCanvasOwnerCount ?? -1),
        inspectorOwnerCount: Number(diagnostics?.inspectorOwnerCount ?? -1),
        previewOwnerCount: Number(diagnostics?.previewOwnerCount ?? -1),
        imageCanvasOwnerCount: Number(diagnostics?.imageCanvasOwnerCount ?? -1),
        roiOwnerCount: Number(diagnostics?.roiOwnerCount ?? -1),
        persistenceOwnerCount: Number(diagnostics?.persistenceOwnerCount ?? -1),
        activeInspectorDrafts: Number(diagnostics?.activeInspectorDrafts ?? -1),
        activeSubscriptions: Number(shell?.getAttribute('data-workspace-active-subscriptions') || -1),
        activeAnimationFrames: Number(diagnostics?.activeAnimationFrames ?? -1),
        activeObservers: Number(diagnostics?.activeObservers ?? -1),
        activeTimers: Number(diagnostics?.activeTimers ?? -1),
        activeBlobUrls: Number(diagnostics?.activeBlobUrls ?? -1),
        activePreviewArtifactIds: Number(diagnostics?.activePreviewArtifactIds ?? -1),
        inFlightPreview: Number(diagnostics?.inFlightPreview ?? -1),
        inFlightReads: Number(shell?.getAttribute('data-workspace-in-flight-reads') || -1),
        inFlightWrites: Number(shell?.getAttribute('data-workspace-in-flight-writes') || -1),
        diagnostics: diagnostics ? { ...diagnostics } : null,
        mainCount: document.querySelectorAll('main').length,
        shellCount: document.querySelectorAll('[data-evidence-surface="f03-workspace-shell"]').length,
        horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        verticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight
      };
    });
    if (dpiOnly) {
      const mounted = await readWorkspace();
      assert(mounted.dirty === 'false' && ['clean', 'saved'].includes(mounted.persistencePhase),
        `Workspace was not clean before DPI capture: ${JSON.stringify(mounted)}`);
      assert(mounted.mainCount === 1 && mounted.shellCount === 1 &&
        mounted.ownerCount === 1 && mounted.flowCanvasOwnerCount === 1 && mounted.inspectorOwnerCount === 1 &&
        mounted.previewOwnerCount === 1 && mounted.imageCanvasOwnerCount === 1 && mounted.roiOwnerCount === 1 &&
        mounted.persistenceOwnerCount === 1,
      `F04 DPI-only Workspace did not retain exactly one owner chain: ${JSON.stringify(mounted)}`);
      workspaceLifecycle = { mode: 'dpi-only', mounted, disposed: null, remounted: null, cycles: [] };
    } else {
      const mounted = await readWorkspace();
      assert(mounted.dirty === 'false' && ['clean', 'saved'].includes(mounted.persistencePhase),
        `Workspace was not clean before lifecycle navigation: ${JSON.stringify(mounted)}`);
      const lifecycleCycles = [];
      await page.evaluate(() => {
      window.__g5LifecycleOriginalConfirm = window.confirm;
      window.__g5LifecycleConfirmSnapshots = [];
      window.confirm = message => {
        const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
        const flow = document.querySelector('[data-evidence-surface="f03-g2-flow-canvas"]');
        const diagnostics = window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__;
        const activeElement = document.activeElement;
        window.__g5LifecycleConfirmSnapshots.push({
          message,
          persistencePhase: shell?.getAttribute('data-workspace-persistence-phase') || null,
          dirty: shell?.getAttribute('data-workspace-dirty') || null,
          dirtyGeneration: Number(shell?.getAttribute('data-workspace-dirty-generation') ?? -1),
          flowRevision: Number(flow?.getAttribute('data-flow-revision') ?? -1),
          activeInspectorDrafts: Number(diagnostics?.activeInspectorDrafts ?? -1),
          activeElement: activeElement ? {
            tagName: activeElement.tagName,
            testId: activeElement.getAttribute('data-testid'),
            parameterName: activeElement.closest('[data-parameter-name]')?.getAttribute('data-parameter-name') || null,
            value: 'value' in activeElement ? activeElement.value : null
          } : null
        });
        return true;
      };
    });
    let disposed = null;
    let remounted = null;
    for (let cycle = 0; cycle < 20; cycle += 1) {
      await page.locator('[data-product-nav="/projects"]').click();
      await page.waitForSelector('[data-capability="projects-read"]', { state: 'visible', timeout: 30_000 });
      const confirmSnapshots = await page.evaluate(() => [...window.__g5LifecycleConfirmSnapshots]);
      assert(confirmSnapshots.length === 0,
        `Clean Workspace navigation unexpectedly prompted: ${JSON.stringify(confirmSnapshots)}`);
      await page.waitForFunction(() => {
        const diagnostics = window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__;
        return diagnostics?.workspaceOwnerCount === 0 &&
          diagnostics.flowCanvasOwnerCount === 0 &&
          diagnostics.inspectorOwnerCount === 0 &&
          diagnostics.previewOwnerCount === 0 &&
          diagnostics.imageCanvasOwnerCount === 0 &&
          diagnostics.roiOwnerCount === 0 &&
          diagnostics.persistenceOwnerCount === 0 &&
          diagnostics.activeInspectorDrafts === 0 &&
          diagnostics.activeSubscriptions === 0 &&
          diagnostics.activeAnimationFrames === 0 &&
          diagnostics.activeObservers === 0 &&
          diagnostics.activeTimers === 0 &&
          diagnostics.activeAbortControllers === 0 &&
          diagnostics.activeBlobUrls === 0 &&
          diagnostics.activePreviewArtifactIds === 0 &&
          diagnostics.inFlightPreview === 0 &&
          diagnostics.inFlightReads === 0 &&
          diagnostics.inFlightWrites === 0;
      }, null, { timeout: 30_000 });
      disposed = await page.evaluate(() => ({
        diagnostics: { ...window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__ },
        shellCount: document.querySelectorAll('[data-evidence-surface="f03-workspace-shell"]').length,
        mainCount: document.querySelectorAll('main').length
      }));
      await page.evaluate(nextRoute => { window.location.hash = `#${nextRoute}`; }, route);
      await page.waitForSelector('[data-evidence-surface="f03-workspace-shell"]', {
        state: 'visible',
        timeout: 30_000
      });
      await page.waitForFunction(() => {
        const state = document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
          ?.getAttribute('data-workspace-state');
        const diagnostics = window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__;
        return Boolean(state && state !== 'loading') &&
          diagnostics?.workspaceOwnerCount === 1 &&
          diagnostics.flowCanvasOwnerCount === 1 &&
          diagnostics.inspectorOwnerCount === 1 &&
          diagnostics.previewOwnerCount === 1 &&
          diagnostics.imageCanvasOwnerCount === 1 &&
          diagnostics.roiOwnerCount === 1 &&
          diagnostics.persistenceOwnerCount === 1;
      }, null, { timeout: 30_000 });
      remounted = await readWorkspace();
      lifecycleCycles.push({
        cycle,
        disposedOwnerCount: disposed.diagnostics.workspaceOwnerCount,
        disposedInspectorCount: disposed.diagnostics.inspectorOwnerCount,
        disposedPreviewCount: disposed.diagnostics.previewOwnerCount,
        disposedImageCount: disposed.diagnostics.imageCanvasOwnerCount,
        disposedRoiCount: disposed.diagnostics.roiOwnerCount,
        disposedPersistenceCount: disposed.diagnostics.persistenceOwnerCount,
        remountedOwnerCount: remounted.ownerCount,
        remountedInspectorCount: remounted.inspectorOwnerCount,
        remountedPreviewCount: remounted.previewOwnerCount,
        remountedImageCount: remounted.imageCanvasOwnerCount,
        remountedRoiCount: remounted.roiOwnerCount,
        remountedPersistenceCount: remounted.persistenceOwnerCount
      });
    }
    await page.evaluate(() => {
      window.confirm = window.__g5LifecycleOriginalConfirm;
      delete window.__g5LifecycleOriginalConfirm;
      delete window.__g5LifecycleConfirmSnapshots;
    });
    workspaceLifecycle = { mounted, disposed, remounted, cycles: lifecycleCycles };

    assert(mounted.mainCount === 1 && mounted.shellCount === 1,
      `Workspace did not remain inside the single Product main: ${JSON.stringify(mounted)}`);
    assert(mounted.ownerCount >= 0 && mounted.ownerCount <= 1,
      `Workspace owner count escaped 0/1: ${JSON.stringify(mounted)}`);
    if (workspaceG3 || workspaceG4 || workspaceG4B) {
      assert(mounted.ownerCount === 1 && mounted.flowCanvasOwnerCount === 1 && mounted.inspectorOwnerCount === 1 &&
        mounted.previewOwnerCount === 1 && mounted.imageCanvasOwnerCount === 1 && mounted.roiOwnerCount === 1 &&
        mounted.persistenceOwnerCount === 1,
      `Formal G4 Workspace did not retain exactly one owner chain: ${JSON.stringify(mounted)}`);
    }
    assert(disposed.shellCount === 0 && disposed.mainCount === 1,
      `Workspace route leave did not unmount its shell: ${JSON.stringify(disposed)}`);
    assert(disposed.diagnostics.workspaceOwnerCount === 0 &&
      disposed.diagnostics.flowCanvasOwnerCount === 0 &&
      disposed.diagnostics.inspectorOwnerCount === 0 &&
      disposed.diagnostics.previewOwnerCount === 0 &&
      disposed.diagnostics.imageCanvasOwnerCount === 0 &&
      disposed.diagnostics.roiOwnerCount === 0 &&
      disposed.diagnostics.persistenceOwnerCount === 0 &&
      disposed.diagnostics.activeInspectorDrafts === 0 &&
      disposed.diagnostics.activeSubscriptions === 0 &&
      disposed.diagnostics.activeAnimationFrames === 0 &&
      disposed.diagnostics.activeObservers === 0 &&
      disposed.diagnostics.activeTimers === 0 &&
      disposed.diagnostics.activeAbortControllers === 0 &&
      disposed.diagnostics.activeBlobUrls === 0 &&
      disposed.diagnostics.activePreviewArtifactIds === 0 &&
      disposed.diagnostics.inFlightPreview === 0 &&
      disposed.diagnostics.inFlightReads === 0 &&
      disposed.diagnostics.inFlightWrites === 0,
    `Workspace resources survived route leave: ${JSON.stringify(disposed)}`);
    assert(remounted.ownerCount >= 0 && remounted.ownerCount <= 1,
      `Workspace remount owner count escaped 0/1: ${JSON.stringify(remounted)}`);
    if (workspaceG3 || workspaceG4 || workspaceG4B) {
      assert(remounted.ownerCount === 1 && remounted.flowCanvasOwnerCount === 1 && remounted.inspectorOwnerCount === 1 &&
        remounted.previewOwnerCount === 1 && remounted.imageCanvasOwnerCount === 1 && remounted.roiOwnerCount === 1 &&
        remounted.persistenceOwnerCount === 1,
      `Formal G4 Workspace remount did not restore one owner chain: ${JSON.stringify(remounted)}`);
    }
    assert(lifecycleCycles.length === 20 && lifecycleCycles.every(item =>
      item.disposedOwnerCount === 0 && item.disposedInspectorCount === 0 && item.disposedPreviewCount === 0 &&
      item.disposedImageCount === 0 && item.disposedRoiCount === 0 &&
      item.disposedPersistenceCount === 0 &&
      item.remountedOwnerCount === 1 && item.remountedInspectorCount === 1 && item.remountedPreviewCount === 1 &&
      item.remountedImageCount === 1 && item.remountedRoiCount === 1 && item.remountedPersistenceCount === 1),
    `Workspace 20-cycle lifecycle ledger failed: ${JSON.stringify(lifecycleCycles)}`);
    assert(remounted.horizontalOverflow <= 1 && remounted.verticalOverflow <= 1,
      `Workspace remount overflowed globally: ${JSON.stringify(remounted)}`);
      if (workspaceG4 || workspaceG4B) {
        await selectSeededRoiNode(page);
        await waitForSeededPreview(page);
      }
    }
  }
  if (dpiOnly && workspaceReady && seededWorkspace) {
    await selectSeededRoiNode(page);
  }
  const workspaceCanvasDpi = isF04Evidence && workspaceReady && seededWorkspace
    ? await readWorkspaceCanvasDpiEvidence(page, workspaceSeedRoiNodeId)
    : null;
  const formalRunInstallation = formalRun ? await installFormalRunDecision(page, formalRunSeed) : null;
  const workspaceG6 = formalRun
    ? await verifyWorkspaceG6(page, runtimeErrors, formalRunSeed, webPort, token)
    : null;
  const workspaceG4BCompletion = goldenJourney && workspaceG6
    ? await verifyG4BResultAndPackage(page, workspaceG6, evidenceDirectory, runName, sourceSha)
    : null;
  const productRequests = runtimeErrors.requests.filter(isProductRequest);
  const writeRequests = productRequests.filter(item => item.method !== 'GET');
  const projectPutRequests = productRequests.filter(item => {
    const url = new URL(item.url);
    return item.method === 'PUT' && /^\/api\/projects\/[0-9a-f-]{36}$/i.test(url.pathname);
  });
  const isProjectOpenRequest = item => {
    const url = new URL(item.url);
    return item.method === 'POST' && /^\/api\/projects\/[0-9a-f-]{36}\/open$/i.test(url.pathname);
  };
  const projectOpenRequests = productRequests.filter(isProjectOpenRequest);
  const forbiddenRunRequests = productRequests.filter(item => {
    const url = new URL(item.url);
    return /\/api\/(?:inspection\/(?:admission|execute|stop|reconcile)|runs)(?:\/|$)/i.test(url.pathname);
  });
  const expectedRunPaths = formalRun
    ? [
        '/api/inspection/admission', '/api/inspection/execute',
        '/api/inspection/admission', '/api/inspection/execute', '/api/inspection/reconcile',
        '/api/inspection/admission', '/api/inspection/execute', '/api/inspection/stop',
        ...(workspaceG6?.genuineRunningStop?.uiReconciliation ? ['/api/inspection/reconcile'] : [])
      ]
    : [];
  const unexpectedWriteRequests = writeRequests.filter(item => {
    if (!isWorkspaceRoute) return true;
    const url = new URL(item.url);
    return !(isF04Evidence && isProjectOpenRequest(item)) &&
      !(item.method === 'POST' && url.pathname === '/api/inspection/decision-configuration/validate') &&
      !(item.method === 'POST' && url.pathname === '/api/flows/preview-node') &&
      !(goldenJourney && item.method === 'POST' && url.pathname === '/api/cameras/soft-trigger-capture') &&
      !(goldenJourney && item.method === 'POST' &&
        /^\/api\/projects\/[0-9a-f-]{36}\/runtime-package\/export$/i.test(url.pathname)) &&
      !(item.method === 'PUT' && /^\/api\/projects\/[0-9a-f-]{36}$/i.test(url.pathname)) &&
      !(item.method === 'DELETE' && /^\/api\/preview-artifacts\/[A-Za-z0-9_-]{43}$/.test(url.pathname)) &&
      !(formalRun && item.method === 'POST' && expectedRunPaths.includes(url.pathname));
  });

  assert(projection.shellCount === 1, 'Product route did not mount exactly one ProductLayout.');
  assert(projection.internalLabCount === 0, 'Product route mounted the InternalLabLayout.');
  assert(projection.ownerLedger.studio?.mountCount === 1 &&
    projection.ownerLedger.studio?.activeRoot === 'studio-ui',
  `Product route did not retain exactly one StudioUI root: ${JSON.stringify(projection.ownerLedger)}`);
  assert(projection.ownerLedger.projectLifecycle?.ownerCount === 1,
    `Project lifecycle owner ledger is not one: ${JSON.stringify(projection.ownerLedger)}`);
  assert(projection.ownerLedger.leaveGuard?.ownerCount === 1,
    `Leave Guard owner ledger is not one: ${JSON.stringify(projection.ownerLedger)}`);
  assert(Number(projection.ownerLedger.workspace?.workspaceOwnerCount ?? 0) ===
    (isWorkspaceRoute ? 1 : 0),
  `Workspace owner ledger did not match the active route: ${JSON.stringify(projection.ownerLedger)}`);
  assert(projection.labNavigationCount === 0, 'Labs leaked into formal product navigation.');
  const missingNavigation = navigationContract.requiredRoutes
    .filter(routePath => !projection.formalNavigation.includes(routePath));
  const forbiddenNavigation = navigationContract.forbiddenRoutes
    .filter(routePath => projection.formalNavigation.includes(routePath));
  assert(missingNavigation.length === 0,
    `Formal product navigation is incomplete: ${JSON.stringify({ navigationContract, missingNavigation, actual: projection.formalNavigation })}`);
  assert(forbiddenNavigation.length === 0,
    `Formal product navigation exposed a feature-disabled route: ${JSON.stringify({ navigationContract, forbiddenNavigation, actual: projection.formalNavigation })}`);
  assert(projection.density === 'compact', 'Formal product did not default to compact density.');
  assert(projection.horizontalOverflow <= 1, 'Formal product route has global horizontal overflow.');
  if (isWorkspaceRoute) {
    assert(projection.verticalOverflow <= 1, 'Workspace route has global vertical overflow.');
  }
  assert(projection.mainCount === 1, 'Formal product route did not keep exactly one main landmark.');
  assert(productRequests.length > 0, 'Product route emitted no observable API requests.');
  assert(!isF04Evidence || !isWorkspaceRoute || projectOpenRequests.length > 0,
    'F04 Workspace navigation did not issue the approved explicit Project open command.');
  assert(unexpectedWriteRequests.length === 0,
    `Product route emitted writes outside approved Project/Preview/Run commands: ${JSON.stringify(unexpectedWriteRequests)}`);
  if (formalRun) {
    assert(forbiddenRunRequests.map(item => new URL(item.url).pathname).join(',') === expectedRunPaths.join(','),
      `Formal Run did not issue the expected Admission/Execute/Stop/Reconcile request chain: ${JSON.stringify(forbiddenRunRequests)}`);
    assert(workspaceG6?.projectId && workspaceG6.resultId,
      `Formal Run did not retain the Results handoff identity: ${JSON.stringify(workspaceG6)}`);
  } else {
    assert(forbiddenRunRequests.length === 0,
      `G5 emitted Run/Admission/Execute requests: ${JSON.stringify(forbiddenRunRequests)}`);
  }
  if (workspaceG4 || workspaceG4B) {
    const expectedProjectPutCount = workspaceG4B ? 3 : formalRun ? 4 : 1;
    assert(projectPutRequests.length === expectedProjectPutCount,
      `G5/G6 real WebView2 expected ${expectedProjectPutCount} Project PUT request(s): ${JSON.stringify(projectPutRequests)}`);
  }
  assert(preferenceCycle.dark.attributeValue === 'dark', 'Dark theme projection was not applied.');
  assert(preferenceCycle.comfortable.attributeValue === 'comfortable',
    'Comfortable density projection was not applied.');
  assert(preferenceCycle.final.theme === 'light' && preferenceCycle.final.density === 'compact',
    'Theme/density preference cycle did not restore the formal product defaults.');
  assert(preferenceCycle.final.stored?.theme === 'light' &&
    preferenceCycle.final.stored?.density === 'compact',
    'Theme/density preference cycle was not persisted as a disposable UI projection.');
  assert(preferenceWriteRequests.length === 0,
    `Theme/density controls emitted write requests: ${JSON.stringify(preferenceWriteRequests)}`);
  if (isWorkspaceRoute) {
    const unexpectedWorkspaceRequests = productRequests.filter(item => {
      const url = new URL(item.url);
      return url.pathname !== '/health' &&
        !(isF04Evidence && item.method === 'GET' && url.pathname === '/api/auth/setup-status') &&
        url.pathname !== '/api/auth/me' &&
        !(item.method === 'GET' && url.pathname === '/api/cameras/bindings') &&
        !(item.method === 'GET' && url.pathname === '/api/projects/recent' && url.search === '?count=5') &&
        !(url.pathname === '/api/operators/library' && url.search === '?includeCompatibility=true') &&
        !/^\/api\/operators\/[^/]+\/metadata$/i.test(url.pathname) &&
        !(isF04Evidence && isProjectOpenRequest(item)) &&
        !(item.method === 'POST' && url.pathname === '/api/inspection/decision-configuration/validate') &&
        !(goldenJourney && item.method === 'POST' && url.pathname === '/api/cameras/soft-trigger-capture') &&
        !(goldenJourney && item.method === 'POST' &&
          /^\/api\/projects\/[0-9a-f-]{36}\/runtime-package\/export$/i.test(url.pathname)) &&
        url.pathname !== '/api/flows/preview-node' &&
        !/^\/api\/preview-artifacts\/[A-Za-z0-9_-]{43}$/.test(url.pathname) &&
        !/^\/api\/projects\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
          .test(url.pathname) &&
        !(formalRun && url.pathname === '/api/projects') &&
        !(formalRun && expectedRunPaths.includes(url.pathname)) &&
        !(formalRun && item.method === 'GET' &&
          /^\/api\/inspection\/history\/[0-9a-f-]{36}\/[0-9a-f-]{36}\/evidence\/manifest$/i.test(url.pathname)) &&
        !(goldenJourney && item.method === 'GET' &&
          /^\/api\/inspection\/history\/[0-9a-f-]{36}\/[0-9a-f-]{36}\/evidence\/export$/i.test(url.pathname)) &&
        !(formalRun && /^\/api\/inspection\/history\/[0-9a-f-]{36}(?:\/[0-9a-f-]{36})?$/i.test(url.pathname));
    });
    assert(unexpectedWorkspaceRequests.length === 0,
      `Workspace emitted a route outside the formal persistence/Preview/Run allowlist: ${JSON.stringify(unexpectedWorkspaceRequests)}`);
    assert(projection.workspaceMode === 'true' && projection.workspaceShellCount === 1,
      `ProductLayout did not enter workspaceMode: ${JSON.stringify(projection)}`);
  }
  if (resultsFilterLayout) {
    assert(resultsFilterLayout.controls.length === 7,
      `Results filter rail did not expose seven controls: ${JSON.stringify(resultsFilterLayout)}`);
    assert(resultsFilterLayout.maximumTopDelta <= 1,
      `Results filter rail wrapped in the 1350px WebView2 client: ${JSON.stringify(resultsFilterLayout)}`);
  }
  return {
    ...projection,
    navigationContract,
    productRequests,
    writeRequests,
    projectPutRequests,
    projectOpenRequests,
    forbiddenRunRequests,
    preferenceCycle,
    preferenceRequests,
    preferenceWriteRequests,
    resultsFilterLayout,
    workspaceG3,
    workspaceG4,
    workspaceG4B,
    workspaceCanvasDpi,
    formalRunInstallation,
    workspaceG6,
    workspaceG4BCompletion,
    workspaceLifecycle
  };
}

async function verifyDesignPage(page) {
  const lab = page.locator('[data-design-lab="ready"]');
  await lab.waitFor({ state: 'visible', timeout: 30_000 });
  return lab.evaluate(element => ({
    state: element.getAttribute('data-design-lab'),
    theme: document.documentElement.dataset.theme || null,
    density: document.documentElement.dataset.density || null,
    reducedMotion: document.documentElement.dataset.reducedMotion || null,
    width: element.getBoundingClientRect().width,
    height: element.getBoundingClientRect().height
  }));
}

function assertBackingRatio(runtime) {
  assert(runtime.logicalWidth > 0 && runtime.logicalHeight > 0, 'Canvas logical dimensions are empty.');
  assert(runtime.backingWidth > 0 && runtime.backingHeight > 0, 'Canvas backing dimensions are empty.');
  assert(Math.abs(runtime.backingWidth - runtime.logicalWidth * runtime.dpr) <= 2,
    'Canvas backing width does not match logical width multiplied by DPR.');
  assert(Math.abs(runtime.backingHeight - runtime.logicalHeight * runtime.dpr) <= 2,
    'Canvas backing height does not match logical height multiplied by DPR.');
}

async function clickCanvasNode(page) {
  const target = await page.evaluate(() => {
    const runtime = window.__STUDIO_UI_CANVAS_DIAGNOSTICS__?.runtime;
    if (!runtime) return null;
    const node = runtime.nodes.find(candidate =>
      candidate.width > 0 && candidate.height > 0 &&
      candidate.x + candidate.width > 0 && candidate.y + candidate.height > 0 &&
      candidate.x < runtime.logicalWidth && candidate.y < runtime.logicalHeight);
    if (!node) return null;
    return {
      id: node.id,
      x: Math.max(2, Math.min(runtime.logicalWidth - 2, node.x + node.width / 2)),
      y: Math.max(2, Math.min(runtime.logicalHeight - 2, node.y + node.height / 2))
    };
  });
  assert(target, 'Canvas diagnostics exposed no visible node for pointer hit testing.');

  const canvas = page.locator('[data-canvas-surface]');
  const box = await canvas.boundingBox();
  assert(box, 'Canvas surface has no browser bounding box.');
  const hit = await page.evaluate(({ x, y }) => {
    const surface = document.querySelector('[data-canvas-surface]');
    const rect = surface?.getBoundingClientRect();
    if (!surface || !rect) return false;
    return document.elementFromPoint(rect.left + x, rect.top + y) === surface;
  }, target);
  assert(hit, 'Canvas node test point is not hit-testable at the DOM layer.');

  await page.mouse.click(box.x + target.x, box.y + target.y);
  await page.waitForFunction(expectedId =>
    window.__STUDIO_UI_CANVAS_DIAGNOSTICS__?.runtime?.selectedNodeId === expectedId,
  target.id);
  return target;
}

async function loadAndVerifyFixture(page, action, fixtureId, descriptor) {
  await page.locator(`[data-canvas-action="${action}"]`).click();
  await page.waitForFunction(({ expectedFixtureId, nodes, connections }) => {
    const diagnostics = window.__STUDIO_UI_CANVAS_DIAGNOSTICS__;
    return diagnostics?.fixtureId === expectedFixtureId &&
      diagnostics.runtime?.nodeCount === nodes &&
      diagnostics.runtime?.connectionCount === connections;
  }, {
    expectedFixtureId: fixtureId,
    nodes: descriptor.nodeCount,
    connections: descriptor.connectionCount
  });
  await waitForDoubleAnimationFrame(page);

  await page.locator('[data-canvas-action="identity-roundtrip"]').click();
  await page.waitForFunction(expectedFingerprint => {
    const identity = window.__STUDIO_UI_CANVAS_DIAGNOSTICS__?.identity;
    return identity?.state === 'pass' &&
      identity.beforeFingerprint === expectedFingerprint &&
      identity.afterFingerprint === expectedFingerprint;
  }, descriptor.fingerprint);
  return page.evaluate(() => ({ ...window.__STUDIO_UI_CANVAS_DIAGNOSTICS__ }));
}

async function verifyCanvasPage(page, deepCanvas, context) {
  await page.waitForSelector('[data-canvas-lab="ready"]', { state: 'visible', timeout: 45_000 });
  await page.waitForSelector('#studio-ui-canonical-flow-canvas', { state: 'visible' });
  await page.waitForFunction(() => {
    const value = window.__STUDIO_UI_CANVAS_DIAGNOSTICS__;
    return value?.status === 'mounted' && value.ownerCount === 1 && value.runtime?.nodeCount === 5;
  });

  const canonical = await page.evaluate(() => ({ ...window.__STUDIO_UI_CANVAS_DIAGNOSTICS__ }));
  assert(canonical.ownerCount === 1, 'Canvas capability does not have exactly one owner.');
  assert(canonical.fixtureId === 'canonical', 'Canvas Lab did not begin with the canonical fixture.');
  assert(canonical.runtime.nodeCount === 5 && canonical.runtime.connectionCount === 3,
    'Canonical Canvas fixture counts are unexpected.');
  assert(canonical.validation.length === 5 && canonical.validation.every(item => item.passed),
    'Canvas connection rejection matrix did not pass.');
  assertBackingRatio(canonical.runtime);
  assert(canonical.runtime.resources.adapterDisposed === false, 'Mounted Canvas adapter is already disposed.');
  assert(canonical.runtime.resources.canvasDestroyed === false, 'Mounted Canvas kernel is already destroyed.');
  assert(canonical.runtime.resources.interactionDisposed === false, 'Mounted Canvas interaction is already disposed.');
  assert(canonical.runtime.resources.resizeObserverActive, 'Mounted Canvas has no ResizeObserver owner.');
  assert(canonical.runtime.resources.themeObserverActive, 'Mounted Canvas has no theme observer owner.');
  assert(canonical.runtime.resources.structureListenerCount === 1, 'Canvas structure subscription count drifted.');
  assert(canonical.runtime.resources.viewListenerCount === 1, 'Canvas view subscription count drifted.');
  assert(canonical.runtime.resources.selectionListenerCount === 1, 'Canvas selection subscription count drifted.');

  const pointerHit = await clickCanvasNode(page);
  await page.locator('[data-canvas-action="identity-roundtrip"]').click();
  await page.waitForFunction(() => window.__STUDIO_UI_CANVAS_DIAGNOSTICS__?.identity?.state === 'pass');
  const identity = await page.evaluate(() => ({ ...window.__STUDIO_UI_CANVAS_DIAGNOSTICS__.identity }));

  const deterministic = {};
  if (deepCanvas) {
    const benchmark = createCanvasFixtureDescriptor(100, 150);
    const stress = createCanvasFixtureDescriptor(300, 450);
    deterministic.benchmark100 = await loadAndVerifyFixture(
      page,
      'load-benchmark-100',
      'benchmark-100',
      benchmark
    );
    deterministic.stress300 = await loadAndVerifyFixture(
      page,
      'load-stress-300',
      'stress-300',
      stress
    );
    deterministic.expected = {
      benchmark100: {
        flowId: benchmark.flowId,
        fingerprint: benchmark.fingerprint
      },
      stress300: {
        flowId: stress.flowId,
        fingerprint: stress.fingerprint
      }
    };
  }

  const mounted = await page.evaluate(() => ({
    canvas: { ...window.__STUDIO_UI_CANVAS_DIAGNOSTICS__ },
    studio: { ...window.__STUDIO_UI_DIAGNOSTICS__ },
    jsDpr: window.devicePixelRatio
  }));
  assert(mounted.canvas.ownerCount === 1, 'Canvas owner disappeared during verification.');
  assert(mounted.studio.canvasOwnerCount === 1,
    'Studio lifecycle diagnostics did not project the active Canvas owner.');
  assert(Math.abs(mounted.canvas.runtime.dpr - mounted.jsDpr) <= 0.01,
    'Canvas DPR does not match JavaScript devicePixelRatio.');
  const mountedBrowserDpi = await readBrowserDpiEvidence(page, context);

  await page.goto(`${new URL(page.url()).origin}/studio/index.html#/diagnostics`, {
    waitUntil: 'domcontentloaded'
  });
  await waitForStudioReady(page);
  await page.waitForFunction(() => window.__STUDIO_UI_CANVAS_DIAGNOSTICS__?.ownerCount === 0);
  const disposed = await page.evaluate(() => ({
    canvas: { ...window.__STUDIO_UI_CANVAS_DIAGNOSTICS__ },
    studio: { ...window.__STUDIO_UI_DIAGNOSTICS__ }
  }));
  assert(disposed.canvas.status === 'disposed', 'Canvas owner did not report a clean disposal.');
  assert(disposed.canvas.ownerCount === 0, 'Canvas owner remained mounted after route disposal.');
  assert(disposed.canvas.totalDisposals >= 1, 'Canvas disposal count did not advance.');
  assert(disposed.canvas.runtime.resources.adapterDisposed, 'Canvas adapter survived route disposal.');
  assert(disposed.canvas.runtime.resources.canvasDestroyed, 'Canvas kernel survived route disposal.');
  assert(disposed.canvas.runtime.resources.interactionDisposed, 'Canvas interaction survived route disposal.');
  assert(!disposed.canvas.runtime.resources.resizeObserverActive, 'Canvas ResizeObserver survived disposal.');
  assert(!disposed.canvas.runtime.resources.themeObserverActive, 'Canvas theme observer survived disposal.');
  assert(disposed.canvas.runtime.resources.structureListenerCount === 0, 'Canvas structure listener survived disposal.');
  assert(disposed.canvas.runtime.resources.viewListenerCount === 0, 'Canvas view listener survived disposal.');
  assert(disposed.canvas.runtime.resources.selectionListenerCount === 0, 'Canvas selection listener survived disposal.');
  assert(disposed.studio.canvasOwnerCount === 0, 'Studio diagnostics retained a disposed Canvas owner.');
  return {
    canonical,
    pointerHit,
    identity,
    deterministic,
    mounted,
    mountedBrowserDpi,
    disposed
  };
}

async function verifyMissingAssets(page) {
  await page.waitForSelector('body main h1', { state: 'visible', timeout: 45_000 });
  const projection = await page.evaluate(() => ({
    url: location.href,
    title: document.title,
    heading: document.querySelector('body main h1')?.textContent?.trim() || '',
    bodyText: document.body.textContent?.trim() || '',
    startupType: typeof window.__CLEARVISION_STARTUP__,
    studioReadyType: typeof window.__STUDIO_UI_READY__,
    studioPageCount: document.querySelectorAll('[data-studio-page]').length,
    legacyNavigationCount: document.querySelectorAll('.nav-btn[data-view]').length,
    legacyMainCount: document.querySelectorAll('#main-content').length
  }));
  assert(projection.heading === 'ClearVision Product', 'Missing-asset diagnostic heading is unexpected.');
  assert(/StudioUI/.test(projection.bodyText), 'Missing-asset diagnostic does not identify StudioUI.');
  assert(/index\.html|assets|manifest\.json/i.test(projection.bodyText),
    'Missing-asset diagnostic does not identify the missing asset class.');
  assert(projection.startupType === 'undefined', 'Missing-asset diagnostic injected a navigable startup projection.');
  assert(projection.studioReadyType === 'undefined', 'Missing-asset diagnostic mounted StudioUI.');
  assert(projection.studioPageCount === 0, 'Missing-asset diagnostic mounted a StudioUI page owner.');
  assert(projection.legacyNavigationCount === 0 && projection.legacyMainCount === 0,
    'Missing-asset diagnostic silently fell back to Legacy.');
  return projection;
}

function assertNativeRuntime(nativeRuntime) {
  assert(nativeRuntime.awareness?.isPerMonitorV2 === true,
    `Desktop window DPI awareness is ${nativeRuntime.awareness?.label || 'unknown'}, not PerMonitorV2.`);
  assert(nativeRuntime.nativeWindow?.dpi > 0, 'Desktop native window DPI was not reported.');
  assert(nativeRuntime.nativeWindow?.clientSize?.width > 0, 'Desktop native client width is empty.');
  assert(nativeRuntime.nativeWindow?.clientSize?.height > 0, 'Desktop native client height is empty.');
  assert(nativeRuntime.nodeDescendantCount === 0,
    `Desktop process tree contains ${nativeRuntime.nodeDescendantCount} Node descendant(s).`);
}

async function captureFinalJourneyScene(page, evidenceDirectory, phase, scene, sourceSha) {
  const buffer = await page.screenshot({ type: 'png', animations: 'disabled' });
  const artifact = writePngEvidence(
    evidenceDirectory,
    `f04-g6-${safeFileName(phase)}-${safeFileName(scene)}.png`,
    buffer
  );
  return {
    ...artifact,
    phase,
    scene,
    sourceSha,
    route: new URL(page.url()).hash.replace(/^#/, '') || new URL(page.url()).pathname,
    dataSource: 'REAL_WEBVIEW2_PROJECT_AUTHORITY',
    authSource: 'UI_SETUP_OR_LOGIN'
  };
}

async function readUiSessionAuthority(page, webPort) {
  const session = await page.evaluate(() => ({
    token: sessionStorage.getItem('cv_auth_token'),
    projectedUser: sessionStorage.getItem('cv_current_user')
  }));
  assert(session.token, 'UI authentication did not persist the authoritative session token.');
  const me = await requestJson(webPort, '/api/auth/me', {
    token: session.token,
    expectedStatuses: [200]
  });
  const user = me.body?.user ?? me.body;
  const userId = metadataField(user, 'userId') || metadataField(user, 'id');
  assert(userId && metadataField(user, 'username'),
    `Authenticated UI session did not expose a stable user identity: ${JSON.stringify(me.body)}`);
  return {
    token: session.token,
    user: {
      userId: String(userId),
      username: String(metadataField(user, 'username')),
      role: String(metadataField(user, 'role') || '')
    },
    projectedUser: session.projectedUser ? JSON.parse(session.projectedUser) : null
  };
}

async function assertProductLanding(page) {
  await waitForSelectorWithoutHandle(
    page,
    '[data-product-shell="ready"]',
    { state: 'visible', timeout: 45_000 }
  );
  await waitForSelectorWithoutHandle(
    page,
    '[data-capability="projects-read"]',
    { state: 'visible', timeout: 45_000 }
  );
  const projection = await page.evaluate(() => ({
    route: location.hash,
    productShellCount: document.querySelectorAll('[data-product-shell="ready"]').length,
    authShellCount: document.querySelectorAll('[data-auth-shell]').length,
    projectLifecycleOwnerCount: Number(window.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__?.ownerCount ?? -1),
    leaveGuardOwnerCount: Number(window.__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__?.ownerCount ?? -1)
  }));
  assert(projection.route === '#/projects' && projection.productShellCount === 1 &&
    projection.authShellCount === 0 && projection.projectLifecycleOwnerCount === 1 &&
    projection.leaveGuardOwnerCount === 1,
  `UI authentication did not settle on one ProductRuntime owner chain: ${JSON.stringify(projection)}`);
  return projection;
}

async function setupAdminThroughUi(page, webPort, username, password, captureScene) {
  const setupStatus = await requestJson(webPort, '/api/auth/setup-status', { expectedStatuses: [200] });
  assert(setupStatus.body?.requiresInitialAdminSetup === true,
    `Fresh final-journey database did not require setup: ${JSON.stringify(setupStatus.body)}`);
  await waitForSelectorWithoutHandle(
    page,
    '[data-auth-page="setup"]',
    { state: 'visible', timeout: 45_000 }
  );
  if (captureScene) await captureScene('setup');
  await page.getByLabel('管理员用户名').fill(username);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByLabel('确认密码').fill(password);
  await page.getByRole('button', { name: '创建并进入 Studio' }).click();
  const product = await assertProductLanding(page);
  const session = await readUiSessionAuthority(page, webPort);
  assert(session.user.username === username,
    `Setup auto-login authenticated ${session.user.username}, expected ${username}.`);
  return { setupStatus: setupStatus.body, product, session };
}

async function loginThroughUi(page, webPort, username, password, captureScene) {
  const setupStatus = await requestJson(webPort, '/api/auth/setup-status', { expectedStatuses: [200] });
  assert(setupStatus.body?.requiresInitialAdminSetup === false,
    `Restarted final-journey database unexpectedly required setup: ${JSON.stringify(setupStatus.body)}`);
  await waitForSelectorWithoutHandle(
    page,
    '[data-auth-page="login"]',
    { state: 'visible', timeout: 45_000 }
  );
  if (captureScene) await captureScene('login');
  await page.getByLabel('用户名').fill(username);
  await page.getByLabel('密码').fill(password);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  const product = await assertProductLanding(page);
  const session = await readUiSessionAuthority(page, webPort);
  assert(session.user.username === username,
    `Restart login authenticated ${session.user.username}, expected ${username}.`);
  return { setupStatus: setupStatus.body, product, session };
}

async function logoutThroughUi(page) {
  if (await page.locator('[data-product-shell]').getAttribute('data-workspace-mode') === 'true') {
    await page.locator('[data-product-nav="/projects"]').click();
    await waitForSelectorWithoutHandle(page, '[data-capability="projects-read"]', {
      state: 'visible', timeout: 45_000
    });
  }
  await page.getByRole('button', { name: '退出', exact: true }).click();
  await waitForSelectorWithoutHandle(
    page,
    '[data-auth-page="login"]',
    { state: 'visible', timeout: 45_000 }
  );
  const projection = await page.evaluate(() => ({
    route: location.hash,
    authShellCount: document.querySelectorAll('[data-auth-shell="ready"]').length,
    productShellCount: document.querySelectorAll('[data-product-shell]').length,
    tokenPresent: Boolean(sessionStorage.getItem('cv_auth_token')),
    currentUserPresent: Boolean(sessionStorage.getItem('cv_current_user')),
    projectLifecycleDiagnosticsType: typeof window.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__,
    leaveGuardDiagnosticsType: typeof window.__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__,
    workspaceDiagnosticsType: typeof window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__
  }));
  assert(projection.route.startsWith('#/login') && projection.authShellCount === 1 &&
    projection.productShellCount === 0 && !projection.tokenPresent && !projection.currentUserPresent &&
    projection.projectLifecycleDiagnosticsType === 'undefined' &&
    projection.leaveGuardDiagnosticsType === 'undefined' &&
    projection.workspaceDiagnosticsType === 'undefined',
  `Logout retained ProductRuntime authority or token residue: ${JSON.stringify(projection)}`);
  return projection;
}

async function withholdLifecycleResponse(page, requestPath, expectedMethod = 'POST') {
  let captured = null;
  const pattern = `**${requestPath}`;
  const handler = async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (captured || request.method() !== expectedMethod || url.pathname !== requestPath) {
      await route.continue();
      return;
    }
    const requestBody = request.postDataJSON();
    const response = await route.fetch();
    const responseText = await response.text();
    let responseBody = null;
    try { responseBody = responseText ? JSON.parse(responseText) : null; } catch { responseBody = { raw: responseText }; }
    captured = {
      method: request.method(),
      path: url.pathname,
      requestBody,
      serverStatus: response.status(),
      serverBody: responseBody,
      clientStatus: 599
    };
    await route.fulfill({
      status: 599,
      contentType: 'application/json',
      body: JSON.stringify({
        code: 'WEBVIEW2_RESPONSE_WITHHELD',
        error: 'Evidence runner withheld the lifecycle response after the server committed it.'
      })
    });
  };
  await page.route(pattern, handler);
  return {
    get captured() { return captured; },
    async dispose() { await page.unroute(pattern, handler); }
  };
}

function projectIdFromHash(pageUrl) {
  const match = new URL(pageUrl).hash.match(/^#\/projects\/([0-9a-f-]{36})(?:\/|$)/i);
  assert(match, `Project route did not contain a UUID: ${pageUrl}`);
  return match[1];
}

async function createBlankProjectThroughUi(page, runName, options = {}) {
  const responseLoss = options.responseLoss === true;
  await waitForSelectorWithoutHandle(
    page,
    '[data-capability="projects-read"]',
    { state: 'visible', timeout: 45_000 }
  );
  if (options.captureScene) await options.captureScene('projects-empty');
  const withholder = responseLoss ? await withholdLifecycleResponse(page, '/api/projects') : null;
  const name = `F04 G6 ${runName}`;
  const description = responseLoss
    ? 'UI blank create with authoritative response-loss reconciliation.'
    : 'UI blank create for the isolated 20-cycle soak.';
  try {
    await page.getByRole('button', { name: '新建空白工程' }).click();
    if (options.captureScene) await options.captureScene('create-project');
    await page.getByLabel('工程名称').fill(name);
    await page.getByLabel('工程描述').fill(description);
    await page.getByRole('button', { name: '创建', exact: true }).click();
    await waitForSelectorWithoutHandle(page, '[data-capability="projects-read-detail"]', {
      state: 'visible',
      timeout: 45_000
    });
  } finally {
    await withholder?.dispose();
  }
  const projectId = projectIdFromHash(page.url());
  const diagnostics = await page.evaluate(() => ({
    ...window.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__
  }));
  if (responseLoss) {
    assert(withholder?.captured?.serverStatus === 201,
      `Create response-loss fixture did not commit a new Project: ${JSON.stringify(withholder?.captured)}`);
    assert(withholder.captured.serverBody?.projectId === projectId,
      'Create response-loss reconcile returned a different Project identity.');
    assert(diagnostics.totalReconcileCount >= 1,
      `Create response loss did not query operation authority: ${JSON.stringify(diagnostics)}`);
  }
  if (options.captureScene) await options.captureScene('project-detail');
  return {
    projectId,
    name,
    description,
    responseLoss: withholder?.captured ?? null,
    diagnostics
  };
}

async function waitForWorkspaceReady(page, projectId) {
  await waitForSelectorWithoutHandle(page, '[data-evidence-surface="f03-workspace-shell"]', {
    state: 'visible',
    timeout: 45_000
  });
  await waitForFunctionWithoutHandle(page, expectedProjectId => {
    const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    const state = shell?.getAttribute('data-workspace-state');
    return shell?.getAttribute('data-workspace-project-id') === expectedProjectId &&
      Boolean(state && state !== 'loading');
  }, projectId, { timeout: 45_000 });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const state = await shell.getAttribute('data-workspace-state');
  assert(['ready', 'empty'].includes(state), `Workspace did not reach a usable state: ${state}`);
  return {
    state,
    persistenceRevision: Number(await shell.getAttribute('data-workspace-persistence-revision')),
    ownerCount: Number(await shell.getAttribute('data-workspace-owner-count'))
  };
}

async function openWorkspaceFromDetail(page, projectId) {
  await page.getByRole('button', { name: '打开工作区' }).click();
  const workspace = await waitForWorkspaceReady(page, projectId);
  const command = await page.evaluate(() => ({
    phase: document.querySelector('[data-product-shell]')?.getAttribute('data-project-command-phase'),
    diagnostics: { ...window.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__ }
  }));
  assert(command.phase === 'succeeded',
    `Explicit Project open did not reach succeeded: ${JSON.stringify(command)}`);
  return { workspace, command };
}

async function selectWorkspaceNode(page, position) {
  const canvas = page.locator('[data-testid="flow-canvas"]');
  const box = await canvas.boundingBox();
  assert(box, 'Workspace Flow Canvas did not expose a bounding box.');
  await page.mouse.click(box.x + Number(position.x) + 40, box.y + Number(position.y) + 16);
  await waitForFunctionWithoutHandle(page, () =>
    document.querySelector('[data-evidence-surface="f03-g3-inspector"]')
      ?.getAttribute('data-inspector-mode') === 'node', null, { timeout: 30_000 });
}

async function saveWorkspaceThroughUi(page) {
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await waitForFunctionWithoutHandle(page, () =>
    document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-dirty') === 'true', null, { timeout: 30_000 });
  await page.locator('[data-testid="workspace-save"]').click();
  await waitForFunctionWithoutHandle(page, () => {
    const surface = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    return surface?.getAttribute('data-workspace-persistence-phase') === 'saved' &&
      surface.getAttribute('data-workspace-dirty') === 'false';
  }, null, { timeout: 45_000 });
  return Number(await shell.getAttribute('data-workspace-persistence-revision'));
}

async function revealInspectorControl(control) {
  const advanced = control.locator('xpath=ancestor::details[1]');
  if (await advanced.count()) {
    const isOpen = await advanced.evaluate(element => element.open);
    if (!isOpen) await advanced.locator('summary').click();
  }
  await control.scrollIntoViewIfNeeded();
  await control.waitFor({ state: 'visible', timeout: 30_000 });
}

async function addConfigureAndSaveAcquisition(page) {
  await waitForFunctionWithoutHandle(page, () =>
    document.querySelector('[data-evidence-surface="f03-g2-operator-rail"]')
      ?.getAttribute('data-catalog-phase') === 'success', null, { timeout: 30_000 });
  const canvas = page.locator('[data-testid="flow-canvas"]');
  const box = await canvas.boundingBox();
  assert(box && box.width >= 500 && box.height >= 300,
    `Final journey Flow Canvas is undersized: ${JSON.stringify(box)}`);
  const position = { x: 64, y: 64 };
  await dragOperatorToCanvas(page, ['ImageAcquisition', '0'], position);
  await waitForFlowSurfaceNumber(page, 'data-node-count', 1);
  await page.locator('[data-capability="operator-flyout"] [aria-label="关闭算子面板"]').click();
  await selectWorkspaceNode(page, position);
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  const sourceType = inspector.locator('[data-parameter-name="SourceType"] select');
  await revealInspectorControl(sourceType);
  await sourceType.selectOption('Camera');
  const exposure = inspector.locator('[data-parameter-name="ExposureTime"] input[type="number"]');
  await revealInspectorControl(exposure);
  await waitForFunctionWithoutHandle(page, () => {
    const input = document.querySelector('[data-parameter-name="ExposureTime"] input[type="number"]');
    return input && !input.disabled;
  }, null, { timeout: 30_000 });
  await exposure.fill('6200');
  await exposure.press('Enter');
  await sourceType.selectOption('File');
  const revision = await saveWorkspaceThroughUi(page);
  return { position, exposureTime: 6200, sourceType: 'File', persistenceRevision: revision };
}

function setOperatorParameter(operator, name, value) {
  let found = false;
  const parameters = (operator.parameters || []).map(parameter => {
    if (String(parameter.name).toLowerCase() !== name.toLowerCase()) return parameter;
    found = true;
    return { ...parameter, value };
  });
  assert(found, `Operator ${operator.name} did not expose parameter ${name}.`);
  return { ...operator, parameters };
}

async function installFinalJourneyAuthority(webPort, token, projectId, evidenceDirectory) {
  const imageEvidence = createPreviewPpm(path.join(evidenceDirectory, `f04-g6-${projectId}-input.ppm`));
  const [project, catalogPayload] = await Promise.all([
    readAuthorizedJson(webPort, token, `/api/projects/${projectId}`),
    readAuthorizedJson(webPort, token, '/api/operators/library?includeCompatibility=true')
  ]);
  const catalog = Array.isArray(catalogPayload) ? catalogPayload : catalogPayload.items || catalogPayload.Items || [];
  const judgmentDefinition = catalog.find(item =>
    Number(metadataField(item, 'type')) === -1 ||
    String(metadataField(item, 'type') || '').toLowerCase() === 'resultjudgment');
  assert(judgmentDefinition, 'Final journey operator catalog did not expose ResultJudgment.');
  const acquisition = project.flow?.operators?.find(operator =>
    Number(operator.type) === 0 || String(operator.type).toLowerCase() === 'imageacquisition');
  assert(acquisition, 'UI-added ImageAcquisition was not persisted through ProjectSaveCoordinator.');
  const fileAcquisition = setOperatorParameter(
    setOperatorParameter(acquisition, 'SourceType', 'File'),
    'FilePath',
    imageEvidence.filePath
  );
  const judgment = instantiateOperator(
    judgmentDefinition,
    metadataField(judgmentDefinition, 'type'),
    'F04 G6 Final Judgment',
    360,
    64,
    { Condition: 'Equal', ExpectValue: '' }
  );
  const judgmentOutput = judgment.outputPorts.find(port => port.name === 'JudgmentResult');
  assert(judgmentOutput, 'Final journey ResultJudgment did not expose JudgmentResult.');
  const binding = {
    sourceOperatorId: judgment.id,
    sourceOutputPortId: judgmentOutput.id,
    sourceOutputName: judgmentOutput.name,
    dataType: 'String',
    rule: 'StringMap',
    okValue: 'OK',
    ngValue: 'NG'
  };
  const updated = await putAuthorizedJson(webPort, token, `/api/projects/${projectId}`, {
    name: project.name,
    description: project.description,
    expectedPersistenceRevision: project.persistenceRevision,
    flow: {
      ...project.flow,
      operators: project.flow.operators.map(operator =>
        operator.id === acquisition.id ? fileAcquisition : operator).concat(judgment),
      decisionConfiguration: {
        finalDecisionBinding: binding,
        missingDecisionPolicy: 'Undetermined'
      }
    },
    globalVariables: project.globalVariables ?? null
  });
  assert(updated.persistenceRevision > project.persistenceRevision &&
    updated.flow?.decisionConfiguration?.finalDecisionBinding?.sourceOperatorId === judgment.id,
  `Authority preparation did not persist the formal-ready flow: ${JSON.stringify(updated)}`);
  return {
    authority: 'EXISTING_PROJECT_APPLICATION_SERVICE_PUT',
    directFrontendMutation: false,
    projectId,
    acquisitionId: acquisition.id,
    acquisitionPosition: { x: fileAcquisition.x, y: fileAcquisition.y },
    judgmentId: judgment.id,
    binding,
    imageEvidence,
    persistenceRevision: updated.persistenceRevision,
    flowId: updated.flow.id
  };
}

async function reloadWorkspaceAuthority(page, projectId, expectedRevision) {
  assert(new URL(page.url()).hash === `#/projects/${projectId}/workspace`,
    `Authority reload started from a different Workspace route: ${page.url()}`);
  await page.reload({ waitUntil: 'domcontentloaded', timeout: 45_000 });
  await waitForWorkspaceReady(page, projectId);
  await waitForFunctionWithoutHandle(page, revision => {
    const shell = document.querySelector('[data-evidence-surface="f03-workspace-shell"]');
    return Number(shell?.getAttribute('data-workspace-persistence-revision')) === revision &&
      shell?.getAttribute('data-workspace-dirty') === 'false';
  }, expectedRevision, { timeout: 45_000 });
}

async function previewAndSaveFinalWorkspace(page, projectId, authority, nameSuffix) {
  await selectWorkspaceNode(page, authority.acquisitionPosition);
  const nameInput = page.locator('[data-evidence-surface="f03-g3-inspector"] .inspector-panel__field input');
  const currentName = await nameInput.inputValue();
  const nextName = `${currentName} ${nameSuffix}`.trim();
  await nameInput.fill(nextName);
  await nameInput.press('Enter');
  const previewRun = page.locator('[data-testid="preview-run"]');
  await previewRun.waitFor({ state: 'visible', timeout: 30_000 });
  await previewRun.click();
  await waitForFunctionWithoutHandle(page, () =>
    document.querySelector('[data-capability="preview-workbench"]')
      ?.getAttribute('data-preview-phase') === 'success', null, { timeout: 45_000 });
  await waitForFunctionWithoutHandle(page, () =>
    document.querySelector('[data-capability="image-canvas"]')
      ?.getAttribute('data-image-phase') === 'ready', null, { timeout: 45_000 });
  const revision = await saveWorkspaceThroughUi(page);
  const project = await page.evaluate(async id => {
    const token = sessionStorage.getItem('cv_auth_token');
    const response = await fetch(`/api/projects/${id}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
      cache: 'no-store'
    });
    return { status: response.status, body: await response.json() };
  }, projectId);
  assert(project.status === 200 && Number(project.body.persistenceRevision) === revision,
    `UI save did not reconcile to the Project authority: ${JSON.stringify(project)}`);
  const acquisition = project.body.flow.operators.find(operator => operator.id === authority.acquisitionId);
  const values = Object.fromEntries((acquisition?.parameters || []).map(parameter => [parameter.name, parameter.value]));
  assert(acquisition?.name === nextName && values.SourceType === 'File' &&
    values.FilePath === authority.imageEvidence.filePath &&
    project.body.flow.decisionConfiguration?.finalDecisionBinding?.sourceOperatorId === authority.judgmentId,
  `UI save lost formal-ready authority fields: ${JSON.stringify({ acquisition, values, project: project.body })}`);
  return { revision, nextName, project: project.body };
}

async function executeFormalRunOnce(page, projectId) {
  const run = page.locator('[data-testid="workspace-run"]');
  await run.waitFor({ state: 'visible', timeout: 30_000 });
  assert(await run.isEnabled(), 'Formal Run was not enabled for the saved final-journey Project.');
  const admissionPromise = page.waitForResponse(response =>
    response.request().method() === 'POST' &&
    new URL(response.url()).pathname === '/api/inspection/admission');
  const executePromise = page.waitForResponse(response =>
    response.request().method() === 'POST' &&
    new URL(response.url()).pathname === '/api/inspection/execute');
  await run.click();
  const [admissionResponse, executeResponse] = await Promise.all([admissionPromise, executePromise]);
  const [admission, result] = await Promise.all([admissionResponse.json(), executeResponse.json()]);
  const handoff = await readFormalResultsHandoff(page, projectId);
  const identity = {
    projectId,
    clientSnapshotId: admission.clientSnapshotId,
    expectedPersistenceRevision: admission.projectPersistenceRevision,
    expectedCanonicalFlowHash: admission.canonicalFlowHash,
    expectedDecisionConfigurationHash: admission.decisionConfigurationHash
  };
  assert(result.id === handoff.resultId && result.projectId === projectId &&
    result.executionSnapshotId === identity.clientSnapshotId &&
    result.projectPersistenceRevision === identity.expectedPersistenceRevision &&
    result.flowVersionHash === identity.expectedCanonicalFlowHash &&
    result.decisionConfigurationHash === identity.expectedDecisionConfigurationHash,
  `Formal Run identity drifted across admission/execute/results: ${JSON.stringify({ admission, result, handoff })}`);
  return { admission, result, handoff, identity };
}

function writeFinalJourneyState(statePath, value) {
  fs.mkdirSync(path.dirname(statePath), { recursive: true });
  fs.writeFileSync(statePath, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
}

function assertFinalResultIdentity(expected, actual, phase) {
  for (const field of [
    'projectId', 'flowId', 'resultId', 'resultProjectRevision', 'executionSnapshotId',
    'flowHash', 'decisionHash', 'hasImage', 'imageId', 'evidenceStatus', 'reconciliationStatus'
  ]) {
    assert(Object.is(actual[field], expected[field]),
      `${phase} changed final-journey authority field ${field}: ${JSON.stringify({ expected, actual })}`);
  }
  assert(JSON.stringify(actual.imageReference) === JSON.stringify(expected.imageReference) &&
    actual.historyContainsResult,
  `${phase} changed final-journey image/history identity.`);
}

async function verifyFinalJourneyCreateRunLogout(
  page, webPort, username, password, runName, evidenceDirectory, sourceSha, statePath
) {
  const screenshots = [];
  const captureScene = async scene => screenshots.push(await captureFinalJourneyScene(
    page, evidenceDirectory, 'create-run-logout', scene, sourceSha));
  const auth = await setupAdminThroughUi(page, webPort, username, password, captureScene);
  const studio = await verifyStudioFoundation(page, webPort, '/projects');
  await captureScene('projects');
  const created = await createBlankProjectThroughUi(page, runName, {
    responseLoss: true,
    captureScene
  });
  const projectAfterCreate = await readAuthorizedJson(webPort, auth.session.token, `/api/projects/${created.projectId}`);
  assert(projectAfterCreate.persistenceRevision === 0 &&
    projectAfterCreate.flow?.operators?.length === 0 &&
    projectAfterCreate.flow?.connections?.length === 0,
  `UI blank create did not return the canonical empty Project: ${JSON.stringify(projectAfterCreate)}`);
  await page.locator('.project-details__back').click();
  await page.waitForSelector('[data-capability="projects-read"]', { state: 'visible', timeout: 45_000 });
  await page.getByText(created.name, { exact: true }).first().waitFor({ state: 'visible', timeout: 30_000 });
  await captureScene('projects-populated');
  await page.getByRole('link', { name: '查看详情', exact: true }).first().click();
  await page.waitForSelector('[data-capability="projects-read-detail"]', { state: 'visible', timeout: 30_000 });
  const opened = await openWorkspaceFromDetail(page, created.projectId);
  const configured = await addConfigureAndSaveAcquisition(page);
  const prepared = await installFinalJourneyAuthority(
    webPort, auth.session.token, created.projectId, evidenceDirectory);
  await reloadWorkspaceAuthority(page, created.projectId, prepared.persistenceRevision);
  const saved = await previewAndSaveFinalWorkspace(page, created.projectId, prepared, 'UI configured');
  await captureScene('workspace-preview-saved');
  const formal = await executeFormalRunOnce(page, created.projectId);
  await captureScene('formal-results');
  const resultDetail = await readAuthorizedJson(
    webPort, auth.session.token, `/api/inspection/history/${created.projectId}/${formal.result.id}`);
  assert(rollbackResultId(resultDetail) === formal.result.id,
    'Formal Results detail did not expose the executed result identity.');
  await page.locator('[data-testid="results-return-workspace"]').click();
  await waitForWorkspaceReady(page, created.projectId);
  await selectWorkspaceNode(page, prepared.acquisitionPosition);
  const nameInput = page.locator('[data-evidence-surface="f03-g3-inspector"] .inspector-panel__field input');
  const finalOperatorName = `${await nameInput.inputValue()} post-run`;
  await nameInput.fill(finalOperatorName);
  await nameInput.press('Enter');
  const finalRevision = await saveWorkspaceThroughUi(page);
  await captureScene('workspace-post-run-saved');
  const authority = await readRollbackAuthority(
    webPort,
    auth.session.token,
    created.projectId,
    formal.result.id,
    formal.identity
  );
  assert(authority.persistenceRevision === finalRevision && authority.flowId === prepared.flowId,
    `Final post-run save identity drifted: ${JSON.stringify(authority)}`);
  const state = {
    schemaVersion: 'f04-g6-final-journey.v1',
    sourceSha,
    createdAtUtc: new Date().toISOString(),
    user: auth.session.user,
    project: {
      projectId: created.projectId,
      projectName: authority.projectName,
      persistenceRevision: authority.persistenceRevision,
      flowId: authority.flowId,
      acquisitionId: prepared.acquisitionId,
      acquisitionPosition: prepared.acquisitionPosition,
      judgmentId: prepared.judgmentId,
      finalOperatorName
    },
    authority,
    runIdentity: formal.identity,
    createResponseLoss: created.responseLoss
  };
  writeFinalJourneyState(statePath, state);
  const logout = await logoutThroughUi(page);
  await captureScene('login-after-logout');
  return {
    phase: 'CREATE_RUN_LOGOUT',
    studio,
    auth: { setupStatus: auth.setupStatus, user: auth.session.user },
    created,
    canonicalBlank: {
      persistenceRevision: projectAfterCreate.persistenceRevision,
      flowId: projectAfterCreate.flow.id,
      operatorCount: projectAfterCreate.flow.operators.length,
      connectionCount: projectAfterCreate.flow.connections.length
    },
    explicitOpen: opened,
    configured,
    prepared,
    saved: { revision: saved.revision, operatorName: saved.nextName },
    formal: {
      resultId: formal.result.id,
      resultProjectRevision: formal.result.projectPersistenceRevision,
      executionSnapshotId: formal.result.executionSnapshotId,
      flowHash: formal.result.flowVersionHash,
      decisionHash: formal.result.decisionConfigurationHash,
      handoff: formal.handoff
    },
    finalAuthority: authority,
    logout,
    statePath,
    screenshots,
    expectedConsoleErrors: { responseLoss: 1, notFound: 0 }
  };
}

async function verifyFinalJourneyReopenDelete(
  page, webPort, username, password, evidenceDirectory, sourceSha, statePath
) {
  assert(fs.existsSync(statePath), `Final journey state was not found after restart: ${statePath}`);
  const state = JSON.parse(fs.readFileSync(statePath, 'utf8'));
  assert(state.schemaVersion === 'f04-g6-final-journey.v1' && state.sourceSha === sourceSha,
    'Final journey restart state schema/source SHA changed.');
  const screenshots = [];
  const captureScene = async scene => screenshots.push(await captureFinalJourneyScene(
    page, evidenceDirectory, 'reopen-delete', scene, sourceSha));
  const auth = await loginThroughUi(page, webPort, username, password, captureScene);
  assert(auth.session.user.userId === state.user.userId && auth.session.user.username === state.user.username,
    'Final journey restart authenticated a different user identity.');
  const studio = await verifyStudioFoundation(page, webPort, '/projects');
  const recentLink = page.getByRole('link', { name: state.project.projectName, exact: true }).first();
  await recentLink.waitFor({ state: 'visible', timeout: 45_000 });
  await captureScene('projects-recent-project');
  await recentLink.click();
  await page.waitForSelector('[data-capability="projects-read-detail"]', { state: 'visible', timeout: 30_000 });
  const beforeRename = await readRollbackAuthority(
    webPort, auth.session.token, state.project.projectId, state.authority.resultId, state.runIdentity);
  assertRollbackAuthorityMatches(state.authority, beforeRename, 'G6_RESTART_REOPEN');
  const renamedName = `${state.project.projectName}（重启后重命名）`;
  await page.getByLabel('工程名称').fill(renamedName);
  await page.getByRole('button', { name: '保存工程信息' }).click();
  await page.getByRole('heading', { name: renamedName, exact: true }).waitFor({ state: 'visible', timeout: 30_000 });
  const renamed = await readAuthorizedJson(webPort, auth.session.token, `/api/projects/${state.project.projectId}`);
  assert(renamed.name === renamedName &&
    renamed.persistenceRevision > state.project.persistenceRevision &&
    renamed.flow.id === state.project.flowId,
  `Restart rename did not preserve Project/Flow identity: ${JSON.stringify(renamed)}`);
  await captureScene('renamed-project-detail');
  const opened = await openWorkspaceFromDetail(page, state.project.projectId);
  assert(opened.workspace.persistenceRevision === renamed.persistenceRevision,
    'Reopened Workspace did not load the renamed Project revision.');
  const afterRenameAuthority = await readRollbackAuthority(
    webPort, auth.session.token, state.project.projectId, state.authority.resultId, state.runIdentity);
  assertFinalResultIdentity(state.authority, afterRenameAuthority, 'G6_RENAMED_REOPEN');
  await page.getByRole('link', { name: '工程详情' }).click();
  await page.waitForSelector('[data-capability="projects-read-detail"]', { state: 'visible', timeout: 30_000 });
  const withholder = await withholdLifecycleResponse(
    page, `/api/projects/${state.project.projectId}/delete`);
  try {
    await page.getByRole('button', { name: '删除', exact: true }).click();
    await captureScene('destructive-delete');
    await page.locator('[data-testid="project-detail-delete-confirm"]').click();
    await page.waitForSelector('[data-capability="projects-read"]', { state: 'visible', timeout: 45_000 });
  } finally {
    await withholder.dispose();
  }
  assert(withholder.captured?.serverStatus === 200 &&
    withholder.captured.serverBody?.projectId === state.project.projectId &&
    Number(withholder.captured.requestBody?.expectedPersistenceRevision) === renamed.persistenceRevision,
  `Delete response-loss fixture did not use the latest revision authority: ${JSON.stringify(withholder.captured)}`);
  const deleteDiagnostics = await page.evaluate(() => ({
    ...window.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__
  }));
  assert(deleteDiagnostics.totalReconcileCount >= 1,
    `Delete response loss did not reconcile operation authority: ${JSON.stringify(deleteDiagnostics)}`);
  const listAfterDelete = await readAuthorizedJson(webPort, auth.session.token, '/api/projects');
  assert(!listAfterDelete.some(project => project.id === state.project.projectId),
    'Tombstoned Project remained visible in the authoritative list.');
  const origin = new URL(page.url()).origin;
  await page.goto(`${origin}/studio/index.html#/projects/${state.project.projectId}`, {
    waitUntil: 'domcontentloaded', timeout: 45_000
  });
  await page.getByText('工程不存在（404）', { exact: true }).waitFor({ state: 'visible', timeout: 30_000 });
  await captureScene('detail-not-found');
  await page.goto(`${origin}/studio/index.html#/projects/${state.project.projectId}/workspace`, {
    waitUntil: 'domcontentloaded', timeout: 45_000
  });
  await page.waitForSelector('[data-evidence-surface="f03-workspace-shell"]', {
    state: 'visible', timeout: 30_000
  });
  await page.waitForFunction(() =>
    document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-state') === 'not-found', null, { timeout: 30_000 });
  const detail404 = await requestJson(webPort, `/api/projects/${state.project.projectId}`, {
    token: auth.session.token, expectedStatuses: [404]
  });
  const open404 = await requestJson(webPort, `/api/projects/${state.project.projectId}/open`, {
    token: auth.session.token, method: 'POST', body: {}, expectedStatuses: [404]
  });
  await captureScene('workspace-open-not-found');
  const logout = await logoutThroughUi(page);
  state.deletedAtUtc = new Date().toISOString();
  state.renamedProject = {
    name: renamed.name,
    persistenceRevision: renamed.persistenceRevision,
    flowId: renamed.flow.id
  };
  state.deleteResponseLoss = withholder.captured;
  state.notFound = { listVisible: false, detailStatus: detail404.status, openStatus: open404.status };
  writeFinalJourneyState(statePath, state);
  return {
    phase: 'REOPEN_DELETE',
    studio,
    auth: { setupStatus: auth.setupStatus, user: auth.session.user },
    recentProject: { projectId: state.project.projectId, name: state.project.projectName },
    authorityBeforeRename: beforeRename,
    renamed: {
      name: renamed.name,
      persistenceRevision: renamed.persistenceRevision,
      flowId: renamed.flow.id
    },
    explicitOpen: opened,
    authorityAfterRename: afterRenameAuthority,
    deleteResponseLoss: withholder.captured,
    deleteDiagnostics,
    notFound: { listVisible: false, detailStatus: detail404.status, openStatus: open404.status },
    logout,
    statePath,
    screenshots,
    expectedConsoleErrors: { responseLoss: 1, notFound: 2 }
  };
}

async function readWorkspaceResourceLedger(page) {
  return page.evaluate(() => {
    const diagnostics = window.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__;
    if (!diagnostics) return null;
    return {
      workspaceOwnerCount: Number(diagnostics.workspaceOwnerCount ?? -1),
      flowCanvasOwnerCount: Number(diagnostics.flowCanvasOwnerCount ?? -1),
      inspectorOwnerCount: Number(diagnostics.inspectorOwnerCount ?? -1),
      previewOwnerCount: Number(diagnostics.previewOwnerCount ?? -1),
      imageCanvasOwnerCount: Number(diagnostics.imageCanvasOwnerCount ?? -1),
      roiOwnerCount: Number(diagnostics.roiOwnerCount ?? -1),
      persistenceOwnerCount: Number(diagnostics.persistenceOwnerCount ?? -1),
      runOwnerCount: Number(diagnostics.runOwnerCount ?? -1),
      activeInspectorDrafts: Number(diagnostics.activeInspectorDrafts ?? -1),
      activeSubscriptions: Number(diagnostics.activeSubscriptions ?? -1),
      activeAnimationFrames: Number(diagnostics.activeAnimationFrames ?? -1),
      activeObservers: Number(diagnostics.activeObservers ?? -1),
      activeTimers: Number(diagnostics.activeTimers ?? -1),
      activeAbortControllers: Number(diagnostics.activeAbortControllers ?? -1),
      activeBlobUrls: Number(diagnostics.activeBlobUrls ?? -1),
      activePreviewArtifactIds: Number(diagnostics.activePreviewArtifactIds ?? -1),
      inFlightPreview: Number(diagnostics.inFlightPreview ?? -1),
      inFlightReads: Number(diagnostics.inFlightReads ?? -1),
      inFlightWrites: Number(diagnostics.inFlightWrites ?? -1)
    };
  });
}

function workspaceLedgerIsZero(ledger) {
  return ledger === null || Object.values(ledger).every(value => value === 0);
}

async function markSoakWeakReference(page, cycle, label, selector) {
  const marker = await page.evaluate(({ currentCycle, currentLabel, currentSelector }) => {
    if (typeof WeakRef !== 'function') {
      return { supported: false, targetFound: false };
    }
    const target = document.querySelector(currentSelector);
    if (!target) return { supported: true, targetFound: false };
    const sentinel = document.createElement('span');
    sentinel.dataset.soakGcSentinel = `${currentCycle}-${currentLabel}`;
    document.body.append(sentinel);
    sentinel.remove();
    const runtimeWindow = window;
    const store = runtimeWindow.__CV_STUDIO_UI_SOAK_WEAK_REFS__ ??= Object.create(null);
    const cycleStore = store[currentCycle] ??= Object.create(null);
    cycleStore[currentLabel] = new WeakRef(target);
    cycleStore[`${currentLabel}Sentinel`] = new WeakRef(sentinel);
    return { supported: true, targetFound: true };
  }, { currentCycle: cycle, currentLabel: label, currentSelector: selector });
  assert(marker.supported && marker.targetFound,
    `Soak cycle ${cycle} could not mark ${label} for GC verification: ${JSON.stringify(marker)}`);
  return marker;
}

async function markSoakWeakGlobalReference(page, cycle, label, propertyName) {
  const marker = await page.evaluate(({ currentCycle, currentLabel, currentPropertyName }) => {
    if (typeof WeakRef !== 'function') {
      return { supported: false, targetFound: false };
    }
    const runtimeWindow = window;
    const target = runtimeWindow[currentPropertyName];
    if ((typeof target !== 'object' || target === null) && typeof target !== 'function') {
      return { supported: true, targetFound: false };
    }
    const store = runtimeWindow.__CV_STUDIO_UI_SOAK_WEAK_REFS__ ??= Object.create(null);
    const cycleStore = store[currentCycle] ??= Object.create(null);
    cycleStore[currentLabel] = new WeakRef(target);
    return { supported: true, targetFound: true };
  }, { currentCycle: cycle, currentLabel: label, currentPropertyName: propertyName });
  assert(marker.supported && marker.targetFound,
    `Soak cycle ${cycle} could not mark ${label} for GC verification: ${JSON.stringify(marker)}`);
  return marker;
}

async function readAndClearSoakWeakReferences(page, cycle) {
  return page.evaluate(currentCycle => {
    if (typeof WeakRef !== 'function') {
      return { supported: false, found: false, observations: [], priorCyclesCollected: false };
    }
    const runtimeWindow = window;
    const store = runtimeWindow.__CV_STUDIO_UI_SOAK_WEAK_REFS__;
    if (!store) {
      return { supported: true, found: false, observations: [], priorCyclesCollected: true };
    }
    const observations = Object.keys(store)
      .map(Number)
      .sort((left, right) => left - right)
      .map(observedCycle => {
        const alive = Object.fromEntries(Object.entries(store[observedCycle])
          .map(([label, reference]) => [label, reference.deref() !== undefined]));
        return {
          cycle: observedCycle,
          alive,
          targetsCollected: Object.entries(alive)
            .filter(([label]) => !label.endsWith('Sentinel'))
            .every(([, isAlive]) => isAlive === false),
          sentinelsCollected: Object.entries(alive)
            .filter(([label]) => label.endsWith('Sentinel'))
            .every(([, isAlive]) => isAlive === false)
        };
      });
    for (const observation of observations) {
      if (observation.cycle < currentCycle && observation.targetsCollected && observation.sentinelsCollected) {
        delete store[observation.cycle];
      }
    }
    if (Object.keys(store).length === 0) delete runtimeWindow.__CV_STUDIO_UI_SOAK_WEAK_REFS__;
    const current = observations.find(observation => observation.cycle === currentCycle) ?? null;
    const prior = observations.filter(observation => observation.cycle < currentCycle);
    return {
      supported: true,
      found: observations.length > 0,
      current,
      observations,
      priorCyclesCollected: prior.every(observation =>
        observation.targetsCollected && observation.sentinelsCollected)
    };
  }, cycle);
}

async function captureSoakMetricSample(
  page,
  cdpSession,
  executablePath,
  cycle,
  stage,
  options = {}
) {
  await waitForDoubleAnimationFrame(page);
  const garbageCollection = { attempted: options.collectGarbage !== false, succeeded: false, error: null };
  if (garbageCollection.attempted) {
    try {
      await cdpSession.send('HeapProfiler.collectGarbage');
      garbageCollection.succeeded = true;
    } catch (error) {
      garbageCollection.error = error?.message || String(error);
    }
  }
  const weakReferences = options.readWeakReferences
    ? await readAndClearSoakWeakReferences(page, cycle)
    : null;
  const [heap, performance, memoryDom, nativeRuntime, dom] = await Promise.all([
    cdpSession.send('Runtime.getHeapUsage'),
    cdpSession.send('Performance.getMetrics'),
    cdpSession.send('Memory.getDOMCounters'),
    options.includeNative
      ? Promise.resolve(runDesktopRuntimeProbe(executablePath))
      : Promise.resolve(null),
    page.evaluate(() => ({
      route: location.hash,
      elementCount: document.querySelectorAll('*').length,
      productShellCount: document.querySelectorAll('[data-product-shell]').length,
      authShellCount: document.querySelectorAll('[data-auth-shell="ready"]').length,
      tokenPresent: Boolean(sessionStorage.getItem('cv_auth_token'))
    }))
  ]);
  const performanceMap = Object.fromEntries(performance.metrics.map(metric => [metric.name, metric.value]));
  return {
    cycle,
    stage,
    capturedAtUtc: new Date().toISOString(),
    garbageCollection,
    weakReferences,
    jsHeap: heap,
    memoryDom,
    performance: {
      JSHeapUsedSize: performanceMap.JSHeapUsedSize ?? null,
      JSHeapTotalSize: performanceMap.JSHeapTotalSize ?? null,
      Nodes: performanceMap.Nodes ?? null,
      Documents: performanceMap.Documents ?? null,
      Frames: performanceMap.Frames ?? null,
      JSEventListeners: performanceMap.JSEventListeners ?? null
    },
    desktop: nativeRuntime ? {
      workingSetBytes: nativeRuntime.desktop.workingSetBytes,
      privateMemoryBytes: nativeRuntime.desktop.privateMemoryBytes,
      virtualMemoryBytes: nativeRuntime.desktop.virtualMemoryBytes,
      handleCount: nativeRuntime.desktop.handleCount,
      threadCount: nativeRuntime.desktop.threadCount,
      nodeDescendantCount: nativeRuntime.nodeDescendantCount
    } : null,
    dom
  };
}

function analyzeSoakMetric(samples, selector, policy) {
  const values = samples
    .slice(Math.min(2, samples.length - 1))
    .map(selector)
    .map(Number)
    .filter(Number.isFinite);
  assert(values.length > 1, `Soak metric ${policy.name} did not provide enough finite samples.`);
  const first = values[0];
  const last = values[values.length - 1];
  const delta = last - first;
  const monotonicIncrease = values.length > 1 && values.every((value, index) =>
    index === 0 || value >= values[index - 1]);
  const unexplainedMonotonicGrowth = monotonicIncrease && delta > policy.monotonicGrowthLimit;
  return {
    name: policy.name,
    sampleCount: values.length,
    first,
    last,
    minimum: Math.min(...values),
    maximum: Math.max(...values),
    delta,
    growthLimit: policy.growthLimit,
    monotonicGrowthLimit: policy.monotonicGrowthLimit,
    monotonicIncrease,
    unexplainedMonotonicGrowth,
    passed: delta <= policy.growthLimit && !unexplainedMonotonicGrowth
  };
}

async function verifyFinalJourneySoak(
  page, context, webPort, username, password, runName, evidenceDirectory, sourceSha,
  executablePath, cycleCount
) {
  const screenshots = [];
  const captureScene = async scene => screenshots.push(await captureFinalJourneyScene(
    page, evidenceDirectory, 'soak', scene, sourceSha));
  const setup = await setupAdminThroughUi(page, webPort, username, password, captureScene);
  const studio = await verifyStudioFoundation(page, webPort, '/projects');
  const created = await createBlankProjectThroughUi(page, `${runName} Soak`, {
    responseLoss: false,
    captureScene: null
  });
  await openWorkspaceFromDetail(page, created.projectId);
  const configured = await addConfigureAndSaveAcquisition(page);
  const prepared = await installFinalJourneyAuthority(
    webPort, setup.session.token, created.projectId, evidenceDirectory);
  await reloadWorkspaceAuthority(page, created.projectId, prepared.persistenceRevision);
  const saved = await previewAndSaveFinalWorkspace(page, created.projectId, prepared, 'soak-ready');
  const preparationLogout = await logoutThroughUi(page);

  const cdpSession = await context.newCDPSession(page);
  await cdpSession.send('Performance.enable');
  await cdpSession.send('HeapProfiler.enable');
  const cycles = [];
  const resultIds = new Set();
  let postSoakDisposalSettle = null;
  let postReloadSample = null;
  try {
    for (let cycle = 0; cycle < cycleCount; cycle += 1) {
      const auth = await loginThroughUi(page, webPort, username, password, null);
      await markSoakWeakReference(page, cycle, 'productShell', '[data-product-shell="ready"]');
      await markSoakWeakGlobalReference(
        page,
        cycle,
        'projectLifecycleDiagnostics',
        '__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__'
      );
      await markSoakWeakGlobalReference(
        page,
        cycle,
        'leaveGuardDiagnostics',
        '__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__'
      );
      const stageSamples = {
        login: await captureSoakMetricSample(
          page, cdpSession, executablePath, cycle, 'login', { includeNative: false })
      };
      const projects = await readAuthorizedJson(webPort, auth.session.token, '/api/projects');
      assert(projects.length === 1 && projects[0].id === created.projectId,
        `Soak cycle ${cycle} observed duplicate or missing Projects: ${JSON.stringify(projects)}`);
      await page.locator('[data-product-nav="/projects"]').click();
      await waitForSelectorWithoutHandle(
        page,
        '[data-capability="projects-read"]',
        { state: 'visible', timeout: 45_000 }
      );
      await page.locator(`[data-testid="project-open-${created.projectId}"]`).click();
      const workspace = await waitForWorkspaceReady(page, created.projectId);
      await selectWorkspaceNode(page, prepared.acquisitionPosition);
      await page.locator('[data-testid="preview-run"]').click();
      await waitForFunctionWithoutHandle(page, () =>
        document.querySelector('[data-capability="preview-workbench"]')
          ?.getAttribute('data-preview-phase') === 'success', null, { timeout: 45_000 });
      await markSoakWeakReference(
        page,
        cycle,
        'workspaceShell',
        '[data-evidence-surface="f03-workspace-shell"]'
      );
      await markSoakWeakGlobalReference(
        page,
        cycle,
        'workspaceDiagnostics',
        '__STUDIO_UI_WORKSPACE_DIAGNOSTICS__'
      );
      stageSamples.workspace = await captureSoakMetricSample(
        page, cdpSession, executablePath, cycle, 'workspace', { includeNative: false });
      const formal = await executeFormalRunOnce(page, created.projectId);
      await markSoakWeakReference(page, cycle, 'resultsPage', '[data-capability="results-read"]');
      stageSamples.results = await captureSoakMetricSample(
        page, cdpSession, executablePath, cycle, 'results', { includeNative: false });
      assert(!resultIds.has(formal.result.id),
        `Soak cycle ${cycle} reused Result identity ${formal.result.id}.`);
      resultIds.add(formal.result.id);
      const history = await readAuthorizedJson(
        webPort, auth.session.token, `/api/inspection/history/${created.projectId}?pageIndex=0&pageSize=100`);
      const historyIds = rollbackItems(history).map(rollbackResultId);
      assert([...resultIds].every(id => historyIds.includes(id)),
        `Soak cycle ${cycle} lost an authoritative Result history row.`);
      const workspaceLedger = await readWorkspaceResourceLedger(page);
      assert(workspaceLedgerIsZero(workspaceLedger),
        `Soak cycle ${cycle} retained Workspace resources on Results: ${JSON.stringify(workspaceLedger)}`);
      const logout = await logoutThroughUi(page);
      const metrics = await captureSoakMetricSample(
        page,
        cdpSession,
        executablePath,
        cycle,
        'logout',
        { includeNative: true, readWeakReferences: true }
      );
      stageSamples.logout = metrics;
      const weakReferenceProgressGate = metrics.weakReferences?.supported === true &&
        metrics.weakReferences.current?.sentinelsCollected === true &&
        metrics.weakReferences.priorCyclesCollected === true;
      assert(metrics.dom.productShellCount === 0 && metrics.dom.authShellCount === 1 &&
        !metrics.dom.tokenPresent && metrics.desktop.nodeDescendantCount === 0 &&
        metrics.garbageCollection.succeeded && weakReferenceProgressGate,
      `Soak cycle ${cycle} retained UI/token/Node residue: ${JSON.stringify(metrics)}`);
      cycles.push({
        cycle,
        projectCount: projects.length,
        workspace,
        preview: 'success',
        resultId: formal.result.id,
        resultProjectRevision: formal.result.projectPersistenceRevision,
        executionSnapshotId: formal.result.executionSnapshotId,
        historyCount: rollbackItems(history).length,
        workspaceLedger,
        logout,
        metrics,
        stageSamples,
        weakReferenceProgressGate
      });
    }
    const settleAuth = await loginThroughUi(page, webPort, username, password, null);
    const settleLogout = await logoutThroughUi(page);
    const settleMetrics = await captureSoakMetricSample(
      page,
      cdpSession,
      executablePath,
      cycleCount,
      'post-soak-disposal-settle',
      { includeNative: false, readWeakReferences: true }
    );
    const allTrackedReferencesCollected = settleMetrics.weakReferences?.supported === true &&
      settleMetrics.weakReferences.found === true &&
      settleMetrics.weakReferences.observations.every(observation =>
        observation.targetsCollected && observation.sentinelsCollected);
    assert(settleMetrics.garbageCollection.succeeded && allTrackedReferencesCollected,
      `Post-soak disposal settle retained a prior Product tree: ${JSON.stringify(settleMetrics)}`);
    postSoakDisposalSettle = {
      auth: { user: settleAuth.session.user },
      logout: settleLogout,
      metrics: settleMetrics,
      allTrackedReferencesCollected
    };
    await page.reload({ waitUntil: 'domcontentloaded', timeout: 45_000 });
    await waitForSelectorWithoutHandle(
      page,
      '[data-auth-page="login"]',
      { state: 'visible', timeout: 45_000 }
    );
    postReloadSample = await captureSoakMetricSample(
      page,
      cdpSession,
      executablePath,
      cycleCount,
      'login-after-reload',
      { includeNative: false }
    );
  } finally {
    await cdpSession.detach();
  }

  const requestAudit = await Promise.resolve(null);
  const nativeSamples = cycles.map(item => item.metrics);
  const stableUiSamples = cycles.map(item => item.stageSamples.login);
  const trends = {
    jsHeapUsedBytes: analyzeSoakMetric(stableUiSamples, item => item.jsHeap.usedSize, {
      name: 'jsHeapUsedBytes', growthLimit: 8 * 1024 * 1024, monotonicGrowthLimit: 2 * 1024 * 1024
    }),
    domNodeCount: analyzeSoakMetric(stableUiSamples, item => item.memoryDom.nodes, {
      name: 'domNodeCount', growthLimit: 512, monotonicGrowthLimit: 128
    }),
    jsEventListenerCount: analyzeSoakMetric(stableUiSamples, item => item.memoryDom.jsEventListeners, {
      name: 'jsEventListenerCount', growthLimit: 128, monotonicGrowthLimit: 32
    }),
    documentCount: analyzeSoakMetric(stableUiSamples, item => item.memoryDom.documents, {
      name: 'documentCount', growthLimit: 1, monotonicGrowthLimit: 0
    }),
    workingSetBytes: analyzeSoakMetric(nativeSamples, item => item.desktop.workingSetBytes, {
      name: 'workingSetBytes', growthLimit: 128 * 1024 * 1024, monotonicGrowthLimit: 32 * 1024 * 1024
    }),
    privateMemoryBytes: analyzeSoakMetric(nativeSamples, item => item.desktop.privateMemoryBytes, {
      name: 'privateMemoryBytes', growthLimit: 64 * 1024 * 1024, monotonicGrowthLimit: 16 * 1024 * 1024
    }),
    handleCount: analyzeSoakMetric(nativeSamples, item => item.desktop.handleCount, {
      name: 'handleCount', growthLimit: 32, monotonicGrowthLimit: 8
    })
  };
  const gcGate = cycles.every(item => Object.values(item.stageSamples)
    .every(sample => sample.garbageCollection.succeeded)) &&
    postSoakDisposalSettle?.metrics.garbageCollection.succeeded === true &&
    postReloadSample?.garbageCollection.succeeded === true;
  const weakReferenceGate = cycles.every(item => item.weakReferenceProgressGate) &&
    postSoakDisposalSettle?.allTrackedReferencesCollected === true;
  const firstLogout = nativeSamples[0];
  const lastLogout = nativeSamples.at(-1);
  const firstHistoryCount = cycles[0].historyCount;
  const lastHistoryCount = cycles.at(-1).historyCount;
  const historyDelta = lastHistoryCount - firstHistoryCount;
  const logoutCurrentTreeObservation = {
    sampleStage: 'LOGOUT_WITH_CURRENT_RESULTS_TREE_RETAINED_FOR_ONE_ROUTER_GENERATION',
    firstHistoryCount,
    lastHistoryCount,
    historyDelta,
    firstDomNodeCount: firstLogout.memoryDom.nodes,
    lastDomNodeCount: lastLogout.memoryDom.nodes,
    domNodeDelta: lastLogout.memoryDom.nodes - firstLogout.memoryDom.nodes,
    domNodesPerAddedHistoryRow: historyDelta > 0
      ? (lastLogout.memoryDom.nodes - firstLogout.memoryDom.nodes) / historyDelta
      : null,
    firstJsEventListenerCount: firstLogout.memoryDom.jsEventListeners,
    lastJsEventListenerCount: lastLogout.memoryDom.jsEventListeners,
    jsEventListenerDelta: lastLogout.memoryDom.jsEventListeners - firstLogout.memoryDom.jsEventListeners,
    jsEventListenersPerAddedHistoryRow: historyDelta > 0
      ? (lastLogout.memoryDom.jsEventListeners - firstLogout.memoryDom.jsEventListeners) / historyDelta
      : null,
    priorGenerationWeakReferencesCollected: weakReferenceGate
  };
  const postReloadComparison = postReloadSample ? {
    before: {
      jsHeapUsedBytes: nativeSamples.at(-1).jsHeap.usedSize,
      domNodeCount: nativeSamples.at(-1).memoryDom.nodes,
      jsEventListenerCount: nativeSamples.at(-1).memoryDom.jsEventListeners,
      documentCount: nativeSamples.at(-1).memoryDom.documents
    },
    after: {
      jsHeapUsedBytes: postReloadSample.jsHeap.usedSize,
      domNodeCount: postReloadSample.memoryDom.nodes,
      jsEventListenerCount: postReloadSample.memoryDom.jsEventListeners,
      documentCount: postReloadSample.memoryDom.documents
    }
  } : null;
  const soakGatePassed = cycles.length === cycleCount && resultIds.size === cycleCount &&
    cycles.every(item => workspaceLedgerIsZero(item.workspaceLedger)) &&
    gcGate && weakReferenceGate && Object.values(trends).every(item => item.passed);
  const diagnosticArtifact = writeJsonEvidence(
    evidenceDirectory,
    `f04-g6-soak-diagnostics-${safeFileName(runName)}.json`,
    {
      status: soakGatePassed ? 'pass' : 'fail',
      sourceSha,
      cycleCount,
      uniqueResultCount: resultIds.size,
      gcGate,
      weakReferenceGate,
      trends,
      logoutCurrentTreeObservation,
      postSoakDisposalSettle,
      postReloadSample,
      postReloadComparison,
      cycles
    }
  );
  assert(soakGatePassed,
  `F04 G6 soak did not satisfy lifecycle/result/memory gates: ${JSON.stringify({
    cycles: cycles.length, resultIds: resultIds.size, gcGate, weakReferenceGate, trends, postReloadComparison
  })}`);
  await captureScene('login-after-20-cycle');
  return {
    phase: 'SOAK',
    studio,
    setup: { setupStatus: setup.setupStatus, user: setup.session.user },
    project: {
      projectId: created.projectId,
      name: created.name,
      flowId: prepared.flowId,
      persistenceRevision: saved.revision,
      acquisitionId: prepared.acquisitionId,
      judgmentId: prepared.judgmentId
    },
    preparation: { configured, prepared, saved: { revision: saved.revision, operatorName: saved.nextName } },
    preparationLogout,
    cycleCount,
    uniqueResultCount: resultIds.size,
    resultIds: [...resultIds],
    trends,
    gcGate,
    weakReferenceGate,
    postSoakDisposalSettle,
    postReloadSample,
    postReloadComparison,
    logoutCurrentTreeObservation,
    diagnosticArtifact,
    primaryLeakGate: 'OWNER_RESOURCE_WEAKREF_AND_STABLE_LOGIN_DOM_COUNTERS',
    trendPolicy: {
      rendererTrendSampleStage: 'LOGIN_AFTER_PRIOR_LOGOUT_GC',
      nativeTrendSampleStage: 'LOGOUT',
      warmupCyclesExcluded: Math.min(2, stableUiSamples.length - 1),
      jsHeapGrowthLimitBytes: 8 * 1024 * 1024,
      jsHeapMonotonicGrowthLimitBytes: 2 * 1024 * 1024,
      domNodeGrowthLimit: 512,
      domNodeMonotonicGrowthLimit: 128,
      jsEventListenerGrowthLimit: 128,
      jsEventListenerMonotonicGrowthLimit: 32,
      nativeWorkingSetGrowthLimitBytes: 128 * 1024 * 1024,
      nativePrivateMemoryGrowthLimitBytes: 64 * 1024 * 1024,
      handleGrowthLimit: 32
    },
    requestAudit,
    cycles,
    screenshots,
    expectedConsoleErrors: { responseLoss: 0, notFound: 0 }
  };
}

async function verifyFinalJourney(options) {
  const {
    page, context, webPort, username, password, runName, evidenceDirectory,
    sourceSha, executablePath, phase, statePath, soakCycles
  } = options;
  assert(username && password, 'Final journey requires UI username and password.');
  if (phase === 'CREATE_RUN_LOGOUT') {
    return verifyFinalJourneyCreateRunLogout(
      page, webPort, username, password, runName, evidenceDirectory, sourceSha, statePath);
  }
  if (phase === 'REOPEN_DELETE') {
    return verifyFinalJourneyReopenDelete(
      page, webPort, username, password, evidenceDirectory, sourceSha, statePath);
  }
  assert(phase === 'SOAK' && soakCycles >= 20, 'Final journey SOAK requires at least 20 cycles.');
  return verifyFinalJourneySoak(
    page, context, webPort, username, password, runName, evidenceDirectory, sourceSha,
    executablePath, soakCycles);
}

function rollbackItems(payload) {
  if (Array.isArray(payload)) return payload;
  return payload?.items || payload?.Items || payload?.results || payload?.Results || [];
}

function rollbackResultId(result) {
  return String(metadataField(result, 'resultId') || metadataField(result, 'id') || '');
}

function rollbackSnapshot(project, result, history, reconciliation) {
  const flow = metadataField(project, 'flow') || {};
  const imageReference = metadataField(result, 'imageReference') ?? null;
  const reconciledResult = metadataField(reconciliation, 'result') || {};
  return {
    projectId: String(metadataField(project, 'id') || ''),
    projectName: String(metadataField(project, 'name') || ''),
    persistenceRevision: Number(metadataField(project, 'persistenceRevision')),
    flowId: String(metadataField(flow, 'id') || ''),
    resultId: rollbackResultId(result),
    resultProjectRevision: Number(metadataField(reconciledResult, 'projectPersistenceRevision')),
    executionSnapshotId: String(metadataField(reconciledResult, 'executionSnapshotId') || ''),
    flowHash: String(metadataField(result, 'flowVersionHash') || ''),
    decisionHash: String(metadataField(reconciledResult, 'decisionConfigurationHash') || ''),
    hasImage: metadataField(result, 'hasImage') === true,
    imageId: metadataField(result, 'imageId') ?? null,
    imageReference,
    evidenceStatus: metadataField(result, 'evidenceStatus') ?? null,
    reconciliationStatus: String(metadataField(reconciliation, 'status') || ''),
    historyContainsResult: rollbackItems(history)
      .some(item => rollbackResultId(item) === rollbackResultId(result))
  };
}

async function readRollbackAuthority(webPort, token, projectId, resultId, runIdentity) {
  assert(runIdentity?.projectId === projectId && runIdentity.clientSnapshotId,
    'Rollback authority requires the admitted Formal Run identity.');
  const [project, result, history, reconciliation] = await Promise.all([
    readAuthorizedJson(webPort, token, `/api/projects/${projectId}`),
    readAuthorizedJson(webPort, token, `/api/inspection/history/${projectId}/${resultId}`),
    readAuthorizedJson(webPort, token, `/api/inspection/history/${projectId}?pageIndex=0&pageSize=100`),
    postAuthorizedJson(webPort, token, '/api/inspection/reconcile', runIdentity)
  ]);
  const snapshot = rollbackSnapshot(project, result, history, reconciliation);
  assert(snapshot.projectId === projectId, 'Rollback Project authority changed identity.');
  assert(snapshot.resultId === resultId, 'Rollback Result authority changed identity.');
  assert(snapshot.historyContainsResult, 'Rollback Result disappeared from authoritative history.');
  assert(snapshot.reconciliationStatus === 'succeeded' &&
    rollbackResultId(metadataField(reconciliation, 'result')) === resultId,
  `Rollback reconciliation did not recover the authoritative Result: ${JSON.stringify(reconciliation)}`);
  assert(snapshot.executionSnapshotId === runIdentity.clientSnapshotId &&
    snapshot.resultProjectRevision === runIdentity.expectedPersistenceRevision &&
    snapshot.flowHash === runIdentity.expectedCanonicalFlowHash &&
    snapshot.decisionHash === runIdentity.expectedDecisionConfigurationHash,
  `Rollback reconciliation identity changed: ${JSON.stringify({ runIdentity, snapshot })}`);
  assert(snapshot.flowId && snapshot.executionSnapshotId && snapshot.flowHash && snapshot.decisionHash,
    `Rollback authority is missing frozen identity fields: ${JSON.stringify(snapshot)}`);
  return snapshot;
}

function assertRollbackAuthorityMatches(expected, actual, phase) {
  const fields = [
    'projectId',
    'projectName',
    'persistenceRevision',
    'flowId',
    'resultId',
    'resultProjectRevision',
    'executionSnapshotId',
    'flowHash',
    'decisionHash',
    'hasImage',
    'imageId',
    'evidenceStatus',
    'reconciliationStatus'
  ];
  for (const field of fields) {
    assert(Object.is(actual[field], expected[field]),
      `${phase} changed rollback authority field ${field}: ${JSON.stringify({ expected, actual })}`);
  }
  assert(JSON.stringify(actual.imageReference) === JSON.stringify(expected.imageReference),
    `${phase} changed the rollback image reference.`);
  assert(actual.historyContainsResult, `${phase} lost the authoritative Result history row.`);
}

async function applyRollbackEvidence(evidence, webPort, token, user, rollbackPhase, statePath) {
  if (!rollbackPhase) return null;
  assert(statePath, 'Rollback evidence requires CV_STUDIO_UI_ROLLBACK_STATE.');
  const normalizedUser = JSON.parse(user);
  if (rollbackPhase === 'NEXT_CREATE') {
    const handoff = evidence.productPage?.workspaceG6?.normalHandoff;
    const userId = metadataField(normalizedUser, 'userId') || metadataField(normalizedUser, 'id');
    assert(evidence.expectation === 'studio-product' && evidence.seedWorkspace && evidence.formalRun,
      'NEXT_CREATE requires the seeded formal Product Workspace journey.');
    assert(handoff?.projectId && handoff?.resultId,
      `NEXT_CREATE did not produce a formal Result handoff: ${JSON.stringify(handoff)}`);
    assert(userId, 'NEXT_CREATE did not capture the authenticated user identity.');
    const authority = await readRollbackAuthority(
      webPort,
      token,
      handoff.projectId,
      handoff.resultId,
      handoff.runIdentity
    );
    const state = {
      schemaVersion: 'f04-next-legacy-next-rollback.v1',
      sourceSha: evidence.sourceSha,
      createdAtUtc: new Date().toISOString(),
      user: {
        userId,
        username: metadataField(normalizedUser, 'username'),
        role: metadataField(normalizedUser, 'role')
      },
      authority,
      runIdentity: handoff.runIdentity
    };
    fs.mkdirSync(path.dirname(statePath), { recursive: true });
    fs.writeFileSync(statePath, `${JSON.stringify(state, null, 2)}\n`, 'utf8');
    return { phase: rollbackPhase, statePath, authority, matched: true };
  }

  assert(fs.existsSync(statePath), `Rollback state was not found: ${statePath}`);
  const state = JSON.parse(fs.readFileSync(statePath, 'utf8'));
  assert(state.schemaVersion === 'f04-next-legacy-next-rollback.v1',
    'Rollback state schema is unsupported.');
  assert(state.sourceSha === evidence.sourceSha, 'Rollback state source SHA changed between restarts.');
  assert(state.runIdentity?.projectId === state.authority.projectId &&
    state.runIdentity?.clientSnapshotId === state.authority.executionSnapshotId,
  'Rollback state lost the admitted Formal Run identity.');
  const currentUserId = metadataField(normalizedUser, 'userId') || metadataField(normalizedUser, 'id');
  assert(currentUserId && String(currentUserId) === String(state.user.userId),
    'Rollback restart authenticated a different user identity.');
  assert(metadataField(normalizedUser, 'username') === state.user.username,
    'Rollback restart authenticated a different user authority.');
  if (rollbackPhase === 'LEGACY_VERIFY') {
    assert(evidence.expectation === 'legacy', 'LEGACY_VERIFY requires the Legacy root.');
  } else {
    assert(rollbackPhase === 'NEXT_REOPEN' && evidence.expectation === 'studio-product',
      'NEXT_REOPEN requires the Product StudioUI root.');
    assert(evidence.route === `/projects/${state.authority.projectId}/workspace`,
      'NEXT_REOPEN did not reopen the original Project route.');
  }
  const authority = await readRollbackAuthority(
    webPort,
    token,
    state.authority.projectId,
    state.authority.resultId,
    state.runIdentity
  );
  assertRollbackAuthorityMatches(state.authority, authority, rollbackPhase);
  return {
    phase: rollbackPhase,
    statePath,
    authority,
    matched: true,
    root: evidence.expectation === 'legacy' ? 'LEGACY_DEFAULT' : 'NEXT_PILOT'
  };
}

async function main() {
  const cdpPort = Number(requiredEnvironment('CV_CDP_PORT'));
  const webPort = Number(requiredEnvironment('CV_WEB_PORT'));
  const scale = Number(requiredEnvironment('CV_DPI_SCALE'));
  const finalJourneyPhase = String(process.env.CV_STUDIO_UI_FINAL_JOURNEY_PHASE || '').trim().toUpperCase();
  const authenticationDeferredToScenario = Boolean(finalJourneyPhase);
  const token = authenticationDeferredToScenario ? '' : requiredEnvironment('CV_SMOKE_TOKEN');
  const user = authenticationDeferredToScenario ? '' : requiredEnvironment('CV_SMOKE_USER');
  const username = authenticationDeferredToScenario
    ? requiredEnvironment('CV_SMOKE_USERNAME')
    : String(process.env.CV_SMOKE_USERNAME || '');
  const evidenceDirectory = path.resolve(requiredEnvironment('CV_EVIDENCE_DIR'));
  const executablePath = path.resolve(requiredEnvironment('CV_STUDIO_UI_DESKTOP_EXECUTABLE'));
  const expectation = requiredEnvironment('CV_STUDIO_UI_EXPECTATION').toLowerCase();
  const password = expectation === 'studio-auth' || authenticationDeferredToScenario
    ? requiredEnvironment('CV_SMOKE_PASSWORD')
    : String(process.env.CV_SMOKE_PASSWORD || '');
  const runName = String(process.env.CV_STUDIO_UI_RUN_NAME || expectation).trim();
  const phase = String(process.env.CV_SMOKE_PHASE || 'full').trim();
  const runtimeKind = String(process.env.CV_STUDIO_UI_RUNTIME_KIND || 'unknown').trim();
  const configuration = String(process.env.CV_STUDIO_UI_CONFIGURATION || 'unknown').trim();
  const sanitizedDesktopPath = parseBooleanEnvironment('CV_STUDIO_UI_SANITIZED_PATH');
  const deepCanvas = parseBooleanEnvironment('CV_STUDIO_UI_DEEP_CANVAS', scale === 1);
  const formalRun = parseBooleanEnvironment('CV_STUDIO_UI_FORMAL_RUN');
  const goldenJourney = parseBooleanEnvironment('CV_STUDIO_UI_G4B_GOLDEN_JOURNEY');
  const dpiOnly = parseBooleanEnvironment('CV_STUDIO_UI_DPI_ONLY');
  const startupProfileRequested = String(process.env.CV_STUDIO_UI_PROFILE || '').trim().toUpperCase();
  const authMode = String(process.env.CV_STUDIO_UI_AUTH_MODE || 'UNRECORDED').trim().toUpperCase();
  const rollbackPhase = String(process.env.CV_STUDIO_UI_ROLLBACK_PHASE || '').trim().toUpperCase();
  const rollbackStatePath = String(process.env.CV_STUDIO_UI_ROLLBACK_STATE || '').trim();
  const finalJourneyStatePath = String(process.env.CV_STUDIO_UI_FINAL_JOURNEY_STATE || '').trim();
  const soakCycles = Number(process.env.CV_STUDIO_UI_SOAK_CYCLES || 0);
  let route = normalizeStudioRoute(
    process.env.CV_STUDIO_UI_ROUTE || routeForExpectation(expectation)
  );
  const seedWorkspace = parseBooleanEnvironment('CV_STUDIO_UI_SEED_WORKSPACE');

  assert(expectations.has(expectation), `Unsupported CV_STUDIO_UI_EXPECTATION: ${expectation}`);
  assert(Number.isInteger(cdpPort) && cdpPort > 0, 'CV_CDP_PORT must be a valid port.');
  assert(Number.isInteger(webPort) && webPort > 0, 'CV_WEB_PORT must be a valid port.');
  assert(Number.isFinite(scale) && scale > 0, 'CV_DPI_SCALE must be a positive number.');
  assert(!formalRun || (expectation === 'studio-product' && seedWorkspace),
    'CV_STUDIO_UI_FORMAL_RUN requires studio-product plus a seeded Workspace.');
  assert(!goldenJourney || (expectation === 'studio-product' && seedWorkspace && formalRun && !dpiOnly),
    'CV_STUDIO_UI_G4B_GOLDEN_JOURNEY requires studio-product, seeded Workspace, and Formal Run.');
  assert(!dpiOnly || (expectation === 'studio-product' && seedWorkspace && !formalRun),
    'CV_STUDIO_UI_DPI_ONLY requires studio-product plus a seeded Workspace without Formal Run.');
  assert(['', 'NEXT_CREATE', 'LEGACY_VERIFY', 'NEXT_REOPEN'].includes(rollbackPhase),
    `Unsupported CV_STUDIO_UI_ROLLBACK_PHASE: ${rollbackPhase}`);
  assert(['', 'CREATE_RUN_LOGOUT', 'REOPEN_DELETE', 'SOAK'].includes(finalJourneyPhase),
    `Unsupported CV_STUDIO_UI_FINAL_JOURNEY_PHASE: ${finalJourneyPhase}`);
  assert(!finalJourneyPhase || (expectation === 'studio-product' && !seedWorkspace &&
    !formalRun && !dpiOnly && !rollbackPhase),
  'Final journey must use an unseeded studio-product route without the standard Formal Run routine.');
  assert(finalJourneyPhase !== 'SOAK' || (Number.isInteger(soakCycles) && soakCycles >= 20),
    'Final journey SOAK requires at least 20 cycles.');

  const evidence = {
    schemaVersion: 1,
    status: 'running',
    runName,
    expectation,
    phase,
    runtimeKind,
    configuration,
    scale,
    sanitizedDesktopPath,
    deepCanvas,
    seedWorkspace,
    formalRun,
    goldenJourney,
    dpiOnly,
    startupProfileRequested: startupProfileRequested || null,
    authMode,
    rollbackPhase: rollbackPhase || null,
    finalJourneyPhase: finalJourneyPhase || null,
    authenticationDeferredToScenario,
    soakCycles,
    route,
    sourceSha: String(process.env.CV_STUDIO_UI_SOURCE_SHA || 'unknown').trim(),
    capturedAtUtc: new Date().toISOString(),
    externalDriver: {
      processId: process.pid,
      parentProcessId: process.ppid,
      executablePath: process.execPath,
      executableIsAbsolute: path.isAbsolute(process.execPath),
      role: 'external-cdp-driver',
      insideDesktopProcessTree: false
    }
  };
  if (expectation === 'studio-product') {
    assert(/^[0-9a-f]{40}$/i.test(evidence.sourceSha),
      'CV_STUDIO_UI_SOURCE_SHA must contain the frozen 40-character candidate SHA.');
  }
  const outputName = `studio-ui-webview2-${safeFileName(runName)}.json`;
  let browser;
  let runtimeErrors;

  try {
    const connected = await connectToDesktopWebView2(cdpPort);
    browser = connected.browser;
    const { context, page, version } = connected;
    evidence.cdpVersion = version;
    if (finalJourneyPhase) {
      runtimeErrors = captureRuntimeErrors(page);
      const responseAudit = [];
      page.on('response', response => {
        const url = new URL(response.url());
        if (url.origin === `http://localhost:${webPort}` && url.pathname.startsWith('/api/') &&
          response.status() >= 400) {
          responseAudit.push({ method: response.request().method(), path: url.pathname, status: response.status() });
        }
      });
      evidence.targetUrl = await navigateWithAuthenticatedSession(
        page,
        webPort,
        expectation,
        route
      );
      evidence.finalJourney = await verifyFinalJourney({
        page,
        context,
        webPort,
        username,
        password,
        runName,
        evidenceDirectory,
        sourceSha: evidence.sourceSha,
        executablePath,
        phase: finalJourneyPhase,
        statePath: finalJourneyStatePath,
        soakCycles
      });
      evidence.finalJourney.httpFailureResponses = responseAudit;
      evidence.finalJourney.expectedConsoleErrors = {
        responseLoss: responseAudit.filter(item => item.status === 599).length,
        notFound: responseAudit.filter(item => item.status === 404).length
      };
      const finalRequests = runtimeErrors.requests
        .map(item => {
          const url = new URL(item.url);
          return { method: item.method, path: `${url.pathname}${url.search}` };
        })
        .filter(item => item.path.startsWith('/api/'));
      const countRequest = (method, matcher) => finalRequests.filter(item =>
        item.method === method && (typeof matcher === 'string' ? item.path === matcher : matcher.test(item.path))).length;
      const requestAudit = {
        setupAdminPosts: countRequest('POST', '/api/auth/setup-admin'),
        loginPosts: countRequest('POST', '/api/auth/login'),
        logoutPosts: countRequest('POST', '/api/auth/logout'),
        createPosts: countRequest('POST', '/api/projects'),
        operationGets: countRequest('GET', /^\/api\/project-operations\/[0-9a-f-]{36}\?kind=(?:create|delete)$/i),
        openPosts: countRequest('POST', /^\/api\/projects\/[0-9a-f-]{36}\/open$/i),
        deletePosts: countRequest('POST', /^\/api\/projects\/[0-9a-f-]{36}\/delete$/i),
        admissionPosts: countRequest('POST', '/api/inspection/admission'),
        executePosts: countRequest('POST', '/api/inspection/execute')
      };
      evidence.finalJourney.requestAudit = requestAudit;
      if (finalJourneyPhase === 'CREATE_RUN_LOGOUT') {
        assert(requestAudit.setupAdminPosts === 1 && requestAudit.createPosts === 1 &&
          requestAudit.operationGets === 1 && requestAudit.admissionPosts === 1 &&
          requestAudit.executePosts === 1 && requestAudit.logoutPosts === 1,
        `Final create/run request audit drifted: ${JSON.stringify(requestAudit)}`);
      } else if (finalJourneyPhase === 'REOPEN_DELETE') {
        assert(requestAudit.loginPosts === 1 && requestAudit.deletePosts === 1 &&
          requestAudit.operationGets === 1 && requestAudit.logoutPosts === 1,
        `Final reopen/delete request audit drifted: ${JSON.stringify(requestAudit)}`);
      } else {
        assert(requestAudit.setupAdminPosts === 1 && requestAudit.createPosts === 1 &&
          requestAudit.loginPosts === soakCycles + 1 && requestAudit.logoutPosts === soakCycles + 2 &&
          requestAudit.admissionPosts === soakCycles && requestAudit.executePosts === soakCycles &&
          requestAudit.deletePosts === 0,
        `20-cycle request audit drifted: ${JSON.stringify({ requestAudit, soakCycles })}`);
      }
    } else if (seedWorkspace) {
      assert(expectation === 'studio-product', 'Workspace seeding requires studio-product evidence.');
      assert(route === '/projects/seeded/workspace',
        'Workspace seeding requires the /projects/seeded/workspace route placeholder.');
      evidence.workspaceSeed = await seedWorkspaceProject(webPort, token, runName, formalRun, goldenJourney);
      route = evidence.workspaceSeed.route;
      evidence.route = route;
    }
    if (!finalJourneyPhase) evidence.api = await readApiEvidence(webPort, token);

    if (finalJourneyPhase) {
      evidence.studio = evidence.finalJourney.studio;
    } else if (expectation === 'missing-assets') {
      runtimeErrors = captureRuntimeErrors(page);
      evidence.missingAssets = await verifyMissingAssets(page);
    } else {
      if (expectation === 'studio-auth') {
        runtimeErrors = captureRuntimeErrors(page);
      } else {
        await seedAuthenticatedSession(page, webPort, token, user);
        runtimeErrors = captureRuntimeErrors(page);
      }
      if (goldenJourney) {
        evidence.g4bHarness = await installG4BGoldenJourneyHarness(
          page,
          evidenceDirectory,
          evidence.workspaceSeed.imageEvidence
        );
      }
      evidence.targetUrl = await navigateWithAuthenticatedSession(
        page,
        webPort,
        expectation,
        route
      );

      if (expectation === 'studio-auth') {
        evidence.authLifecycle = await verifyStudioAuthLifecycle(page, user, password);
      } else if (expectation === 'legacy') {
        evidence.legacy = await verifyLegacy(page, webPort);
      } else {
        evidence.studio = await verifyStudioFoundation(page, webPort, route);
        if (expectation === 'studio-diagnostics') {
          evidence.diagnosticsPage = await verifyDiagnosticsPage(page);
        } else if (expectation === 'studio-product') {
          evidence.productPage = await verifyProductPage(
            page,
            route,
            runtimeErrors,
            evidence.workspaceSeed?.formalRunSeed ?? null,
            webPort,
            token,
            phase,
            evidence.workspaceSeed?.roiNodeId ?? null,
            evidence.workspaceSeed?.goldenJourneySeed ?? null,
            evidenceDirectory,
            runName,
            evidence.sourceSha
          );
        } else if (expectation === 'studio-design') {
          evidence.designPage = await verifyDesignPage(page);
        } else if (expectation === 'studio-canvas') {
          evidence.canvasPage = await verifyCanvasPage(page, deepCanvas, context);
        }
      }
    }

    evidence.rollback = finalJourneyPhase ? null : await applyRollbackEvidence(
      evidence,
      webPort,
      token,
      user,
      rollbackPhase,
      rollbackStatePath
    );

    evidence.browserDpi = await readBrowserDpiEvidence(page, context);
    evidence.nativeRuntime = runDesktopRuntimeProbe(executablePath);
    if (expectation === 'studio-product' && evidence.productPage) {
      const routeName = safeFileName(route.replace(/^\//, '') || 'overview');
      const screenshotBuffer = await page.screenshot({ type: 'png' });
      const artifact = writePngEvidence(
        evidenceDirectory,
        `real-webview2-${routeName}-${safeFileName(runName)}.png`,
        screenshotBuffer
      );
      evidence.viewportScreenshot = {
        ...artifact,
        sourceSha: evidence.sourceSha,
        scenes: route === '/overview' ? ['app-shell', 'overview'] : [routeName],
        route,
        DATA_SOURCE: evidence.productPage.dataSource,
        AUTH_SOURCE: 'HARNESS_SEEDED_SESSION',
        theme: evidence.productPage.preferenceCycle.final.theme,
        density: evidence.productPage.preferenceCycle.final.density,
        nativeWindow: evidence.nativeRuntime.nativeWindow,
        browserViewport: evidence.browserDpi.js,
        DPR_TYPE: 'WEBVIEW2_FORCE_DEVICE_SCALE_FACTOR',
        requestedDprScale: scale,
        observedDevicePixelRatio: evidence.browserDpi.js.devicePixelRatio,
        DPI_TYPE: 'NATIVE_WINDOW_DPI_OBSERVED',
        observedNativeWindowDpi: evidence.nativeRuntime.nativeWindow.dpi
      };
    } else if (expectation === 'studio-product' && evidence.finalJourney) {
      const routeName = safeFileName(new URL(page.url()).hash.replace(/^#\/?/, '') || 'final-journey');
      const screenshotBuffer = await page.screenshot({ type: 'png' });
      const artifact = writePngEvidence(
        evidenceDirectory,
        `real-webview2-${routeName}-${safeFileName(runName)}.png`,
        screenshotBuffer
      );
      const appearance = await page.evaluate(() => ({
        theme: document.documentElement.dataset.theme || null,
        density: document.documentElement.dataset.density || null
      }));
      evidence.viewportScreenshot = {
        ...artifact,
        sourceSha: evidence.sourceSha,
        scenes: ['f04-g6-final-journey', finalJourneyPhase.toLowerCase()],
        route: new URL(page.url()).hash.replace(/^#/, ''),
        DATA_SOURCE: 'REAL_WEBVIEW2_PROJECT_AUTHORITY',
        AUTH_SOURCE: 'UI_SETUP_OR_LOGIN',
        theme: appearance.theme,
        density: appearance.density,
        nativeWindow: evidence.nativeRuntime.nativeWindow,
        browserViewport: evidence.browserDpi.js,
        DPR_TYPE: 'WEBVIEW2_FORCE_DEVICE_SCALE_FACTOR',
        requestedDprScale: scale,
        observedDevicePixelRatio: evidence.browserDpi.js.devicePixelRatio,
        DPI_TYPE: 'NATIVE_WINDOW_DPI_OBSERVED',
        observedNativeWindowDpi: evidence.nativeRuntime.nativeWindow.dpi
      };
    }
    const consoleErrorClassification = classifyConsoleErrors(
      runtimeErrors.consoleErrors,
      evidence.productPage,
      evidence.finalJourney
    );
    evidence.runtimeErrors = runtimeErrors;
    evidence.ignoredExpectedConsoleErrors = consoleErrorClassification.ignoredExpected;
    evidence.meaningfulConsoleErrors = consoleErrorClassification.meaningful;
    evidence.meaningfulRequestFailures = meaningfulRequestFailures(runtimeErrors.requestFailures);

    assertNativeRuntime(evidence.nativeRuntime);
    assert(evidence.externalDriver.executableIsAbsolute,
      'External CDP driver did not use an absolute Node executable path.');
    assert(evidence.meaningfulConsoleErrors.length === 0,
      `WebView2 console errors: ${evidence.meaningfulConsoleErrors.join(' | ')}`);
    assert(runtimeErrors.pageErrors.length === 0,
      `WebView2 page errors: ${runtimeErrors.pageErrors.join(' | ')}`);
    assert(evidence.meaningfulRequestFailures.length === 0,
      `WebView2 request failures: ${JSON.stringify(evidence.meaningfulRequestFailures)}`);

    evidence.status = 'pass';
    evidence.completedAtUtc = new Date().toISOString();
    const output = writeJsonEvidence(evidenceDirectory, outputName, evidence);
    process.stdout.write(`${JSON.stringify({ ok: true, output, expectation, runName })}\n`);
  } catch (error) {
    evidence.status = 'fail';
    evidence.completedAtUtc = new Date().toISOString();
    evidence.error = error?.stack || error?.message || String(error);
    if (runtimeErrors) {
      evidence.runtimeErrors = runtimeErrors;
      evidence.meaningfulRequestFailures = meaningfulRequestFailures(runtimeErrors.requestFailures);
    }
    const output = writeJsonEvidence(evidenceDirectory, outputName, evidence);
    process.stderr.write(`${JSON.stringify({ ok: false, output, error: evidence.error })}\n`);
    throw error;
  } finally {
    await browser?.close();
  }
}

main().catch(error => {
  process.stderr.write(`${error?.stack || error}\n`);
  process.exitCode = 1;
});
