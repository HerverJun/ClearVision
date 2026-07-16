'use strict';

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

const expectations = new Set([
  'legacy',
  'studio-diagnostics',
  'studio-product',
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
    case 'studio-design': return '/labs/design';
    case 'studio-canvas': return '/labs/canvas';
    default: return '/diagnostics';
  }
}

function meaningfulRequestFailures(requestFailures) {
  return requestFailures.filter(item => !/ERR_ABORTED|NS_BINDING_ABORTED/i.test(item.errorText));
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

async function verifyProductPage(page, route, runtimeErrors) {
  await page.waitForSelector('[data-product-shell="ready"]', { state: 'visible', timeout: 30_000 });
  const selectors = [
    ['/projects', '[data-capability="projects-read"]'],
    ['/operators', '[data-capability="operators-read"]'],
    ['/stations', '[data-capability="stations-read"]'],
    ['/results', '[data-capability="results-read"]']
  ];
  const selector = selectors.find(([prefix]) => route.startsWith(prefix))?.[1]
    || '[data-capability="overview"]';
  await page.waitForSelector(selector, { state: 'visible', timeout: 30_000 });
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
    labNavigationCount: document.querySelectorAll('[data-product-nav^="/labs"]').length,
    theme: document.documentElement.dataset.theme || null,
    density: document.documentElement.dataset.density || null,
    horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    dataSource: 'REAL_WEBVIEW2_EMPTY_AUTHORITY',
    authSource: 'HARNESS_SEEDED_SESSION'
  }));
  const origin = new URL(page.url()).origin;
  const productRequests = runtimeErrors.requests.filter(item => {
    const url = new URL(item.url);
    return url.origin === origin && (url.pathname === '/health' || url.pathname.startsWith('/api/'));
  });
  const writeRequests = productRequests.filter(item => item.method !== 'GET');

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
  const preferenceRequests = runtimeErrors.requests.slice(preferenceRequestStart).filter(item => {
    const url = new URL(item.url);
    return url.origin === origin && (url.pathname === '/health' || url.pathname.startsWith('/api/'));
  });
  const preferenceWriteRequests = preferenceRequests.filter(item => item.method !== 'GET');

  assert(projection.shellCount === 1, 'Product route did not mount exactly one ProductLayout.');
  assert(projection.internalLabCount === 0, 'Product route mounted the InternalLabLayout.');
  assert(projection.labNavigationCount === 0, 'Labs leaked into formal product navigation.');
  assert([
    '/overview',
    '/projects',
    '/operators',
    '/stations',
    '/results'
  ].every(routePath => projection.formalNavigation.includes(routePath)),
    'Formal product navigation is incomplete.');
  assert(projection.density === 'compact', 'Formal product did not default to compact density.');
  assert(projection.horizontalOverflow <= 1, 'Formal product route has global horizontal overflow.');
  assert(productRequests.length > 0, 'Product route emitted no observable GET requests.');
  assert(writeRequests.length === 0,
    `Product route emitted write requests: ${JSON.stringify(writeRequests)}`);
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
  return {
    ...projection,
    productRequests,
    writeRequests,
    preferenceCycle,
    preferenceRequests,
    preferenceWriteRequests
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
  const runName = String(process.env.CV_STUDIO_UI_RUN_NAME || expectation).trim();
  const phase = String(process.env.CV_SMOKE_PHASE || 'full').trim();
  const runtimeKind = String(process.env.CV_STUDIO_UI_RUNTIME_KIND || 'unknown').trim();
  const configuration = String(process.env.CV_STUDIO_UI_CONFIGURATION || 'unknown').trim();
  const sanitizedDesktopPath = parseBooleanEnvironment('CV_STUDIO_UI_SANITIZED_PATH');
  const deepCanvas = parseBooleanEnvironment('CV_STUDIO_UI_DEEP_CANVAS', scale === 1);
  const route = normalizeStudioRoute(
    process.env.CV_STUDIO_UI_ROUTE || routeForExpectation(expectation)
  );

  assert(expectations.has(expectation), `Unsupported CV_STUDIO_UI_EXPECTATION: ${expectation}`);
  assert(Number.isInteger(cdpPort) && cdpPort > 0, 'CV_CDP_PORT must be a valid port.');
  assert(Number.isInteger(webPort) && webPort > 0, 'CV_WEB_PORT must be a valid port.');
  assert(Number.isFinite(scale) && scale > 0, 'CV_DPI_SCALE must be a positive number.');

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
    evidence.api = await readApiEvidence(webPort, token);

    if (expectation === 'missing-assets') {
      runtimeErrors = captureRuntimeErrors(page);
      evidence.missingAssets = await verifyMissingAssets(page);
    } else {
      await seedAuthenticatedSession(page, webPort, token, user);
      runtimeErrors = captureRuntimeErrors(page);
      evidence.targetUrl = await navigateWithAuthenticatedSession(
        page,
        webPort,
        expectation,
        route
      );

      if (expectation === 'legacy') {
        evidence.legacy = await verifyLegacy(page, webPort);
      } else {
        evidence.studio = await verifyStudioFoundation(page, webPort, route);
        if (expectation === 'studio-diagnostics') {
          evidence.diagnosticsPage = await verifyDiagnosticsPage(page);
        } else if (expectation === 'studio-product') {
          evidence.productPage = await verifyProductPage(page, route, runtimeErrors);
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
        DATA_SOURCE: 'REAL_WEBVIEW2_EMPTY_AUTHORITY',
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
    evidence.runtimeErrors = runtimeErrors;
    evidence.meaningfulRequestFailures = meaningfulRequestFailures(runtimeErrors.requestFailures);

    assertNativeRuntime(evidence.nativeRuntime);
    assert(evidence.externalDriver.executableIsAbsolute,
      'External CDP driver did not use an absolute Node executable path.');
    assert(runtimeErrors.consoleErrors.length === 0,
      `WebView2 console errors: ${runtimeErrors.consoleErrors.join(' | ')}`);
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
