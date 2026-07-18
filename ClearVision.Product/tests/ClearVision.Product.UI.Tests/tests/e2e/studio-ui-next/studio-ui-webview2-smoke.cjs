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

async function seedWorkspaceProject(webPort, token, runName, formalRun = false) {
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
        operators: [image, roi],
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
    formalRunSeed
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

function classifyConsoleErrors(consoleErrors, productPage) {
  const lifecycle = productPage?.workspaceLifecycle;
  const expectedNotFoundCount = [lifecycle?.mounted?.state, lifecycle?.remounted?.state]
    .filter(state => state === 'not-found')
    .length;
  const expectedNotFoundMessage = 'Failed to load resource: the server responded with a status of 404 (Not Found)';
  let remainingExpectedNotFound = expectedNotFoundCount;
  const expectedResponseLossCount = productPage?.workspaceG6?.responseLoss?.backendResultRecovered ? 1 : 0;
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
  await page.locator('[data-testid="operator-search"]').fill('');
  await page.locator('.operator-rail__categories button').first().click();
}

async function readOperatorDefinition(page, typeCandidates) {
  const candidates = Array.isArray(typeCandidates) ? typeCandidates : [typeCandidates];
  const selector = candidates.map(type => `.operator-item[data-type="${type}"]`).join(', ');
  await resetOperatorRailFilters(page);
  let item = page.locator(selector);
  if (await item.count() === 0) {
    const compatibility = page.locator('.operator-rail__compatibility input');
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
    const response = await fetch(`/api/projects/${projectId}`, {
      method: 'PUT',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: project.name,
        description: project.description,
        expectedPersistenceRevision: project.persistenceRevision,
        flow: {
          ...project.flow,
          operators: [...project.flow.operators, seed.operator],
          decisionConfiguration: {
            finalDecisionBinding: seed.binding,
            missingDecisionPolicy: 'Undetermined'
          }
        },
        globalVariables: null
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
      project: body
    };
  }, formalRunSeed);
  assert(installed.getStatus === 200, `Formal Run decision Project GET returned ${installed.getStatus}.`);
  assert(installed.putStatus === 200,
    `Formal Run decision Project PUT returned ${installed.putStatus}: ${JSON.stringify(installed.project)}`);
  assert(Number(installed.project?.persistenceRevision) > Number(installed.previousPersistenceRevision),
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
  await page.waitForSelector('[data-capability="results-read"]', { state: 'visible', timeout: 45_000 });
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

  await run.click();
  const normalHandoff = await readFormalResultsHandoff(page, projectId);

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
  phase = 'full'
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
  const workspaceReady = isWorkspaceRoute && ['ready', 'empty'].includes(projection.workspaceState);
  assert(!formalRun || (seededWorkspace && workspaceReady),
    'Formal Run evidence requires a seeded, ready Workspace route.');
  const workspaceG4 = workspaceReady && seededWorkspace ? await verifyWorkspaceG4(page) : null;
  const workspaceG3 = workspaceReady && !seededWorkspace ? await verifyWorkspaceG3(page) : null;
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
      await page.locator('[data-product-nav="/about"]').click();
      await page.waitForSelector('[data-studio-page="about"]', { state: 'visible', timeout: 30_000 });
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
    if (workspaceG3 || workspaceG4) {
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
    if (workspaceG3 || workspaceG4) {
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
    if (workspaceG4) {
      await selectSeededRoiNode(page);
      await waitForSeededPreview(page);
    }
  }
  const formalRunInstallation = formalRun ? await installFormalRunDecision(page, formalRunSeed) : null;
  const workspaceG6 = formalRun
    ? await verifyWorkspaceG6(page, runtimeErrors, formalRunSeed, webPort, token)
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
      !(item.method === 'POST' && url.pathname === '/api/flows/preview-node') &&
      !(item.method === 'PUT' && /^\/api\/projects\/[0-9a-f-]{36}$/i.test(url.pathname)) &&
      !(item.method === 'DELETE' && /^\/api\/preview-artifacts\/[A-Za-z0-9_-]{43}$/.test(url.pathname)) &&
      !(formalRun && item.method === 'POST' && expectedRunPaths.includes(url.pathname));
  });

  assert(projection.shellCount === 1, 'Product route did not mount exactly one ProductLayout.');
  assert(projection.internalLabCount === 0, 'Product route mounted the InternalLabLayout.');
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
  if (workspaceG4) {
    const expectedProjectPutCount = formalRun ? 4 : 1;
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
        !(url.pathname === '/api/operators/library' && url.search === '?includeCompatibility=true') &&
        !/^\/api\/operators\/[^/]+\/metadata$/i.test(url.pathname) &&
        !(isF04Evidence && isProjectOpenRequest(item)) &&
        url.pathname !== '/api/flows/preview-node' &&
        !/^\/api\/preview-artifacts\/[A-Za-z0-9_-]{43}$/.test(url.pathname) &&
        !/^\/api\/projects\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
          .test(url.pathname) &&
        !(formalRun && url.pathname === '/api/projects') &&
        !(formalRun && expectedRunPaths.includes(url.pathname)) &&
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
    formalRunInstallation,
    workspaceG6,
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

async function main() {
  const cdpPort = Number(requiredEnvironment('CV_CDP_PORT'));
  const webPort = Number(requiredEnvironment('CV_WEB_PORT'));
  const scale = Number(requiredEnvironment('CV_DPI_SCALE'));
  const token = requiredEnvironment('CV_SMOKE_TOKEN');
  const user = requiredEnvironment('CV_SMOKE_USER');
  const evidenceDirectory = path.resolve(requiredEnvironment('CV_EVIDENCE_DIR'));
  const executablePath = path.resolve(requiredEnvironment('CV_STUDIO_UI_DESKTOP_EXECUTABLE'));
  const expectation = requiredEnvironment('CV_STUDIO_UI_EXPECTATION').toLowerCase();
  const password = expectation === 'studio-auth'
    ? requiredEnvironment('CV_SMOKE_PASSWORD')
    : String(process.env.CV_SMOKE_PASSWORD || '');
  const runName = String(process.env.CV_STUDIO_UI_RUN_NAME || expectation).trim();
  const phase = String(process.env.CV_SMOKE_PHASE || 'full').trim();
  const runtimeKind = String(process.env.CV_STUDIO_UI_RUNTIME_KIND || 'unknown').trim();
  const configuration = String(process.env.CV_STUDIO_UI_CONFIGURATION || 'unknown').trim();
  const sanitizedDesktopPath = parseBooleanEnvironment('CV_STUDIO_UI_SANITIZED_PATH');
  const deepCanvas = parseBooleanEnvironment('CV_STUDIO_UI_DEEP_CANVAS', scale === 1);
  const formalRun = parseBooleanEnvironment('CV_STUDIO_UI_FORMAL_RUN');
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
    if (seedWorkspace) {
      assert(expectation === 'studio-product', 'Workspace seeding requires studio-product evidence.');
      assert(route === '/projects/seeded/workspace',
        'Workspace seeding requires the /projects/seeded/workspace route placeholder.');
      evidence.workspaceSeed = await seedWorkspaceProject(webPort, token, runName, formalRun);
      route = evidence.workspaceSeed.route;
      evidence.route = route;
    }
    evidence.api = await readApiEvidence(webPort, token);

    if (expectation === 'missing-assets') {
      runtimeErrors = captureRuntimeErrors(page);
      evidence.missingAssets = await verifyMissingAssets(page);
    } else {
      if (expectation === 'studio-auth') {
        runtimeErrors = captureRuntimeErrors(page);
      } else {
        await seedAuthenticatedSession(page, webPort, token, user);
        runtimeErrors = captureRuntimeErrors(page);
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
            phase
          );
        } else if (expectation === 'studio-design') {
          evidence.designPage = await verifyDesignPage(page);
        } else if (expectation === 'studio-canvas') {
          evidence.canvasPage = await verifyCanvasPage(page, deepCanvas, context);
        }
      }
    }

    evidence.browserDpi = await readBrowserDpiEvidence(page, context);
    evidence.nativeRuntime = runDesktopRuntimeProbe(executablePath);
    if (expectation === 'studio-product') {
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
    }
    const consoleErrorClassification = classifyConsoleErrors(
      runtimeErrors.consoleErrors,
      evidence.productPage
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
