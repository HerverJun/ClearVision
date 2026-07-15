const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { performance } = require('node:perf_hooks');

const repoRoot = path.resolve(__dirname, '../../../../../..');
const { chromium } = require(path.join(
  repoRoot,
  'ClearVision.Product/tests/ClearVision.Product.UI.Tests/node_modules/@playwright/test'
));

function required(name) {
  const value = String(process.env[name] || '').trim();
  if (!value) throw new Error(`${name} is required.`);
  return value;
}

function percentile(values, ratio) {
  const ordered = [...values].sort((a, b) => a - b);
  return ordered[Math.max(0, Math.ceil(ordered.length * ratio) - 1)];
}

async function seedSession(page, origin, token, user) {
  const url = `${origin}/__clearvision_test_auth_seed__`;
  await page.route(url, route => route.fulfill({
    status: 200,
    contentType: 'text/html; charset=utf-8',
    body: '<!doctype html><html><head><link rel="icon" href="data:,"></head><body></body></html>'
  }));
  await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
  await page.unroute(url);
  await page.evaluate(({ tokenValue, userValue }) => {
    sessionStorage.setItem('cv_auth_token', tokenValue);
    sessionStorage.setItem('cv_current_user', userValue);
    localStorage.setItem('cv_welcome_shown', 'true');
  }, { tokenValue: token, userValue: user });
}

async function installInstrumentation(page) {
  await page.addInitScript(() => {
    const native = {
      addEventListener: EventTarget.prototype.addEventListener,
      removeEventListener: EventTarget.prototype.removeEventListener,
      setTimeout: window.setTimeout.bind(window),
      clearTimeout: window.clearTimeout.bind(window),
      setInterval: window.setInterval.bind(window),
      clearInterval: window.clearInterval.bind(window),
      AbortController: window.AbortController
    };
    const listeners = new WeakMap();
    const activeTimeouts = new Set();
    const activeIntervals = new Set();
    let listenerCount = 0;
    let abortControllerCreated = 0;
    let abortControllerAborted = 0;

    EventTarget.prototype.addEventListener = function(type, listener, options) {
      if (listener) {
        let byType = listeners.get(this);
        if (!byType) {
          byType = new Map();
          listeners.set(this, byType);
        }
        let handlers = byType.get(type);
        if (!handlers) {
          handlers = new Map();
          byType.set(type, handlers);
        }
        const capture = typeof options === 'boolean' ? options : Boolean(options?.capture);
        let captures = handlers.get(listener);
        if (!captures) {
          captures = new Set();
          handlers.set(listener, captures);
        }
        if (!captures.has(capture)) {
          captures.add(capture);
          listenerCount += 1;
        }
      }
      return native.addEventListener.call(this, type, listener, options);
    };

    EventTarget.prototype.removeEventListener = function(type, listener, options) {
      if (listener) {
        const capture = typeof options === 'boolean' ? options : Boolean(options?.capture);
        const captures = listeners.get(this)?.get(type)?.get(listener);
        if (captures?.delete(capture)) listenerCount -= 1;
      }
      return native.removeEventListener.call(this, type, listener, options);
    };

    window.setTimeout = function(handler, timeout, ...args) {
      let id;
      const wrapped = (...callbackArgs) => {
        activeTimeouts.delete(id);
        if (typeof handler === 'function') return handler(...callbackArgs);
        return Function(handler)();
      };
      id = native.setTimeout(wrapped, timeout, ...args);
      activeTimeouts.add(id);
      return id;
    };
    window.clearTimeout = function(id) {
      activeTimeouts.delete(id);
      return native.clearTimeout(id);
    };
    window.setInterval = function(handler, timeout, ...args) {
      const id = native.setInterval(handler, timeout, ...args);
      activeIntervals.add(id);
      return id;
    };
    window.clearInterval = function(id) {
      activeIntervals.delete(id);
      return native.clearInterval(id);
    };

    class InstrumentedAbortController extends native.AbortController {
      constructor() {
        super();
        abortControllerCreated += 1;
        this.signal.addEventListener('abort', () => { abortControllerAborted += 1; }, { once: true });
      }
    }
    window.AbortController = InstrumentedAbortController;

    Object.defineProperty(window, '__F02_INITIAL_RESOURCE_PROBE__', {
      configurable: false,
      value: () => ({
        listenerCount,
        activeTimeoutCount: activeTimeouts.size,
        activeIntervalCount: activeIntervals.size,
        abortControllerCreated,
        abortControllerAborted,
        abortControllerOutstanding: abortControllerCreated - abortControllerAborted,
        domElementCount: document.querySelectorAll('*').length
      })
    });
  });
}

async function waitInteractive(page, route) {
  if (route === '/diagnostics') {
    await page.waitForSelector('[data-studio-page="diagnostics"]', { state: 'visible' });
    await page.waitForFunction(() => {
      const states = [...document.querySelectorAll('[data-probe-state]')]
        .map(node => node.getAttribute('data-probe-state'));
      return states.length === 2 && states.every(state => state === 'ok');
    }, null, { timeout: 30_000 });
    return;
  }
  if (route === '/labs/design') {
    await page.waitForSelector('[data-design-lab="ready"]', { state: 'visible' });
    return;
  }
  const selector = route === '/projects'
    ? '[data-capability="projects-read"]'
    : '[data-capability="overview"]';
  await page.waitForSelector(selector, { state: 'visible' });
  await page.waitForFunction(() => {
    const user = document.querySelector('.product-layout__user strong')?.textContent?.trim();
    const loading = document.querySelector('[data-page-state="loading"]');
    return Boolean(user && user !== '未认证' && !loading);
  }, null, { timeout: 30_000 });
}

async function measureNavigation(page, origin, route) {
  const started = performance.now();
  await page.goto(`${origin}/studio/index.html#${route}`, {
    waitUntil: 'domcontentloaded',
    timeout: 45_000
  });
  await waitInteractive(page, route);
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  return Math.round((performance.now() - started) * 100) / 100;
}

async function readResources(page, session) {
  await session.send('HeapProfiler.collectGarbage');
  await session.send('Performance.enable');
  const [metrics, heap, probe] = await Promise.all([
    session.send('Performance.getMetrics'),
    session.send('Runtime.getHeapUsage'),
    page.evaluate(() => window.__F02_INITIAL_RESOURCE_PROBE__?.() || null)
  ]);
  return {
    probe,
    performance: Object.fromEntries(metrics.metrics.map(item => [item.name, item.value])),
    heap
  };
}

async function main() {
  const cdpPort = Number(required('CV_CDP_PORT'));
  const webPort = Number(required('CV_WEB_PORT'));
  const token = required('CV_SMOKE_TOKEN');
  const user = required('CV_SMOKE_USER');
  const evidenceDir = path.resolve(required('CV_EVIDENCE_DIR'));
  const executable = path.resolve(required('CV_STUDIO_UI_DESKTOP_EXECUTABLE'));
  const origin = `http://localhost:${webPort}`;
  const outputPath = path.join(evidenceDir, 'studio-ui-product-performance.json');
  fs.mkdirSync(evidenceDir, { recursive: true });

  const versionResponse = await fetch(`http://127.0.0.1:${cdpPort}/json/version`);
  if (!versionResponse.ok) throw new Error(`CDP version returned ${versionResponse.status}`);
  const version = await versionResponse.json();
  const browser = await chromium.connectOverCDP(version.webSocketDebuggerUrl);
  try {
    const context = browser.contexts()[0];
    const page = context.pages()[0];
    const runtimeErrors = [];
    page.on('console', message => {
      if (message.type() === 'error') runtimeErrors.push(`console:${message.text()}`);
    });
    page.on('pageerror', error => runtimeErrors.push(`page:${error.stack || error.message}`));
    await seedSession(page, origin, token, user);
    await installInstrumentation(page);

    const profile = String(process.env.CV_F02_PERF_PROFILE || 'product').trim().toLowerCase();
    const routes = profile === 'initial'
      ? [
          { id: 'diagnostics', route: '/diagnostics' },
          { id: 'design-lab', route: '/labs/design' }
        ]
      : [
          { id: 'overview', route: '/overview' },
          { id: 'projects', route: '/projects' }
        ];
    const [primary, secondary] = routes;

    const warmup = {
      [primary.id]: await measureNavigation(page, origin, primary.route),
      [secondary.id]: await measureNavigation(page, origin, secondary.route)
    };
    const primaryMs = [];
    const secondaryMs = [];
    for (let index = 0; index < 5; index += 1) {
      primaryMs.push(await measureNavigation(page, origin, primary.route));
      secondaryMs.push(await measureNavigation(page, origin, secondary.route));
    }

    await measureNavigation(page, origin, primary.route);
    const session = await context.newCDPSession(page);
    const before = await readResources(page, session);
    const routeSwitchMs = [];
    for (let index = 0; index < 20; index += 1) {
      const route = index % 2 === 0 ? secondary.route : primary.route;
      const started = performance.now();
      await page.evaluate(hash => { window.location.hash = hash; }, `#${route}`);
      await waitInteractive(page, route);
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      routeSwitchMs.push(Math.round((performance.now() - started) * 100) / 100);
    }
    const after = await readResources(page, session);
    await session.detach();

    const payload = {
      schemaVersion: 1,
      phase: 'f02-product',
      sourceSha: required('CV_F02_SOURCE_SHA'),
      dataSource: 'REAL_WEBVIEW2_EMPTY_AUTHORITY',
      authSource: 'HARNESS_SEEDED_SESSION',
      capturedAtUtc: new Date().toISOString(),
      configuration: required('CV_STUDIO_UI_CONFIGURATION'),
      runtimeKind: required('CV_STUDIO_UI_RUNTIME_KIND'),
      executable: {
        path: executable,
        sha256: crypto.createHash('sha256').update(fs.readFileSync(executable)).digest('hex')
      },
      machine: {
        platform: os.platform(),
        release: os.release(),
        arch: os.arch(),
        cpuModel: os.cpus()[0]?.model || null,
        logicalCpuCount: os.cpus().length,
        totalMemoryBytes: os.totalmem()
      },
      webView2: {
        browser: version.Browser,
        userAgent: version['User-Agent'],
        protocolVersion: version['Protocol-Version']
      },
      viewport: await page.evaluate(() => ({
        innerWidth: window.innerWidth,
        innerHeight: window.innerHeight,
        devicePixelRatio: window.devicePixelRatio,
        screenWidth: window.screen.width,
        screenHeight: window.screen.height
      })),
      fixture: { schemaVersion: 1, projects: 'isolated-empty-sqlite' },
      profile,
      measuredRoutes: routes,
      sampling: {
        warmupRule: 'one full navigation per measured route before five recorded navigations',
        navigationSamplesPerRoute: 5,
        routeSwitchSamples: 20,
        measurementInterval: 'navigation start until target ready selector/probes plus two animation frames'
      },
      warmup,
      primaryFirstInteractiveMs: primaryMs,
      secondaryFirstInteractiveMs: secondaryMs,
      routeSwitchMs,
      summaries: {
        primaryMedianMs: percentile(primaryMs, 0.5),
        secondaryMedianMs: percentile(secondaryMs, 0.5),
        routeSwitchP95Ms: percentile(routeSwitchMs, 0.95)
      },
      resources: { before, after },
      deltas: {
        listenerCount: (after.probe?.listenerCount || 0) - (before.probe?.listenerCount || 0),
        activeTimeoutCount: (after.probe?.activeTimeoutCount || 0) - (before.probe?.activeTimeoutCount || 0),
        activeIntervalCount: (after.probe?.activeIntervalCount || 0) - (before.probe?.activeIntervalCount || 0),
        abortControllerOutstanding: (after.probe?.abortControllerOutstanding || 0) - (before.probe?.abortControllerOutstanding || 0),
        domElementCount: (after.probe?.domElementCount || 0) - (before.probe?.domElementCount || 0),
        jsHeapUsedSize: (after.performance.JSHeapUsedSize || 0) - (before.performance.JSHeapUsedSize || 0),
        nodes: (after.performance.Nodes || 0) - (before.performance.Nodes || 0),
        jsEventListeners: (after.performance.JSEventListeners || 0) - (before.performance.JSEventListeners || 0)
      },
      runtimeErrors
    };
    fs.writeFileSync(outputPath, `${JSON.stringify(payload, null, 2)}\n`, 'utf8');
    if (runtimeErrors.length) throw new Error(runtimeErrors.join('\n'));
    process.stdout.write(`${JSON.stringify({ ok: true, outputPath, summaries: payload.summaries, deltas: payload.deltas })}\n`);
  } finally {
    await browser.close();
  }
}

main().catch(error => {
  process.stderr.write(`${error.stack || error}\n`);
  process.exitCode = 1;
});
