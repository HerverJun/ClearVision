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

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function round(value) {
  return Math.round(value * 100) / 100;
}

function summarize(values) {
  const numeric = values.filter(Number.isFinite);
  if (!numeric.length) return { count: 0, medianMs: 0, p95Ms: 0, maximumMs: 0 };
  return {
    count: numeric.length,
    medianMs: round(percentile(numeric, 0.5)),
    p95Ms: round(percentile(numeric, 0.95)),
    maximumMs: round(Math.max(...numeric))
  };
}

function summarizeActionSamples(samples) {
  return {
    total: summarize(samples.map(item => item.totalMs)),
    fixture: summarize(samples.map(item => item.fixtureMs)),
    transportExcludingFixture: summarize(samples.map(item => item.transportExcludingFixtureMs)),
    decode: summarize(samples.map(item => item.decodeMs)),
    filterOrViewModel: summarize(samples.map(item => item.viewModelMs)),
    domRender: summarize(samples.map(item => item.domRenderMs))
  };
}

function sha256File(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex');
}

function parseFrozenFixtureAuthority() {
  const filePath = path.join(__dirname, 'f02-browser-fixture.ts');
  const source = fs.readFileSync(filePath, 'utf8');
  const readString = (name) => {
    const match = source.match(new RegExp(`${name}:\\s*'([^']+)'`));
    if (!match) throw new Error(`Frozen fixture field ${name} was not found.`);
    return match[1];
  };
  const readCount = (name) => {
    const match = source.match(new RegExp(`export const ${name}\\s*=\\s*(\\d+)`));
    if (!match) throw new Error(`Frozen fixture count ${name} was not found.`);
    return Number(match[1]);
  };
  const authority = {
    filePath,
    sha256: sha256File(filePath),
    schemaVersion: readString('schemaVersion'),
    sourceSha: readString('sourceSha'),
    dataSource: readString('dataSource'),
    authSource: readString('authSource'),
    operatorCount: readCount('f02OperatorPerformanceFixtureCount'),
    resultCount: readCount('f02ResultsPerformanceFixtureCount')
  };
  assert(authority.operatorCount === 200, 'Frozen operator fixture must contain 200 entries.');
  assert(authority.resultCount === 500, 'Frozen results fixture must contain 500 entries.');
  return authority;
}

const categoryLabels = Object.freeze([
  '采集', '图像预处理', '分割与区域', '特征提取', '匹配与定位', '缺陷检测', '测量',
  '标定与坐标', 'AI推理', '3D点云', '数据处理', '流程控制', '通信', '输出与辅助'
]);

const canonicalCases = Object.freeze([
  ['Ok', 'Succeeded', 'Ok'],
  ['Ng', 'Succeeded', 'Ng'],
  ['Undetermined', 'Succeeded', 'Undetermined'],
  ['NotApplicable', 'Succeeded', 'NotApplicable'],
  ['Invalid', 'Succeeded', 'Invalid'],
  ['Failed', 'Failed', 'Undetermined'],
  ['Cancelled', 'Cancelled', 'NotApplicable'],
  ['TimedOut', 'TimedOut', 'Undetermined'],
  ['Skipped', 'Skipped', 'NotApplicable']
]);

function createOperatorFixture(index) {
  const type = 1000 + index;
  const categoryId = index === 0 ? 8 : index % categoryLabels.length;
  const lifecycle = index === 0 ? 1 : index === 1 ? 3 : index === 2 ? 4 : 0;
  const hidden = lifecycle === 3 || lifecycle === 4;
  return {
    fixtureId: `operator-fixture-${String(index + 1).padStart(3, '0')}`,
    originalOperatorType: index % 158,
    type,
    displayName: index === 0 ? '颜色分析' : `性能算子 ${String(index + 1).padStart(3, '0')}`,
    description: index === 0 ? '颜色检查 fixture' : `确定性分页样本 ${index + 1}`,
    categoryId,
    category: categoryLabels[categoryId],
    lifecycle,
    lifecycleNote: lifecycle === 0 ? null : 'fixture 生命周期说明',
    defaultHidden: hidden,
    iconName: 'operator',
    keywords: index === 0 ? ['颜色', 'Color'] : [`fixture-${index + 1}`],
    tags: ['F02', 'performance'],
    version: '1.0.0',
    inputPorts: [{
      name: index === 0 ? 'Image' : 'Input',
      displayName: index === 0 ? '图像' : '输入',
      dataType: 0,
      isRequired: true,
      description: null
    }],
    outputPorts: [{
      name: 'Result',
      displayName: '结果',
      dataType: 6,
      isRequired: false,
      description: null
    }],
    parameters: [{
      name: index === 0 ? 'Threshold' : 'Value',
      displayName: index === 0 ? '阈值' : '值',
      description: null,
      dataType: 'double',
      defaultValue: 0.5,
      minValue: 0,
      maxValue: 1,
      isRequired: true,
      options: null
    }]
  };
}

function createStationResultFixture(index) {
  const canonical = canonicalCases[index % canonicalCases.length];
  const legacy = index === 0;
  return {
    schemaVersion: 2,
    stationId: `station-${String((index % 8) + 1).padStart(2, '0')}`,
    lineName: `line-${(index % 3) + 1}`,
    sequenceId: index + 1,
    messageId: `fixture-result-${String(index + 1).padStart(4, '0')}`,
    runId: `fixture-run-${String(index + 1).padStart(4, '0')}`,
    packageId: 'package-results-fixture',
    packageName: 'Results 500 Fixture',
    packageVersion: '1.0.0',
    projectRevision: 8,
    outcome: legacy ? 2 : canonical[0] === 'Ng' ? 1 : 0,
    inspectionStatus: legacy ? 'Error' : canonical[0],
    ...(legacy ? {} : {
      executionOutcome: canonical[1],
      decisionOutcome: canonical[2],
      hasJudgmentSignal: canonical[0] === 'Ok' || canonical[0] === 'Ng',
      decisionSource: 'FinalDecision',
      reasonCode: `FIXTURE_${canonical[0].toUpperCase()}`
    }),
    executionTimeMs: 10 + index,
    diagnosticCode: legacy ? 'LEGACY_ERROR' : `FIXTURE_${canonical[0].toUpperCase()}`,
    diagnosticMessage: legacy ? 'legacy 文案中即使出现 NG 也不得推断' : null,
    startedAtUtc: new Date(Date.UTC(2026, 6, 15, 0, 0, index)).toISOString(),
    completedAtUtc: new Date(Date.UTC(2026, 6, 15, 0, 0, index + 1)).toISOString()
  };
}

async function installBrowserInstrumentation(page) {
  await page.addInitScript(() => {
    const native = {
      addEventListener: EventTarget.prototype.addEventListener,
      removeEventListener: EventTarget.prototype.removeEventListener,
      setTimeout: window.setTimeout.bind(window),
      clearTimeout: window.clearTimeout.bind(window),
      setInterval: window.setInterval.bind(window),
      clearInterval: window.clearInterval.bind(window),
      fetch: window.fetch.bind(window),
      AbortController: window.AbortController,
      jsonParse: JSON.parse.bind(JSON)
    };
    const listenerRecords = new WeakMap();
    const targetTokens = new WeakMap();
    const activeTimeouts = new Set();
    const activeIntervals = new Set();
    const activeFetchSignals = new Map();
    const network = [];
    const longTasks = [];
    let listenerCount = 0;
    let finalizedTargetCount = 0;
    let abortControllerCreated = 0;
    let abortControllerAborted = 0;
    let abortControllerLiveCount = 0;
    let abortControllerFinalized = 0;
    let abortedFetchCount = 0;
    let activeFetchSignalCount = 0;
    let pendingTextDecode = null;

    const finalizer = typeof FinalizationRegistry === 'function'
      ? new FinalizationRegistry(token => {
          listenerCount -= token.activeCount;
          token.activeCount = 0;
          finalizedTargetCount += 1;
        })
      : null;
    const abortFinalizer = typeof FinalizationRegistry === 'function'
      ? new FinalizationRegistry(token => {
          if (token.live) {
            token.live = false;
            abortControllerLiveCount -= 1;
          }
          abortControllerFinalized += 1;
        })
      : null;

    function tokenFor(target) {
      let token = targetTokens.get(target);
      if (!token) {
        token = { activeCount: 0 };
        targetTokens.set(target, token);
        finalizer?.register(target, token);
      }
      return token;
    }

    function deactivate(record) {
      if (!record.active) return;
      record.active = false;
      listenerCount -= 1;
      record.token.activeCount -= 1;
      if (record.signal && record.abortHook) {
        native.removeEventListener.call(record.signal, 'abort', record.abortHook, false);
      }
    }

    EventTarget.prototype.addEventListener = function(type, listener, options) {
      if (!listener || options?.signal?.aborted) {
        return native.addEventListener.call(this, type, listener, options);
      }
      const capture = typeof options === 'boolean' ? options : Boolean(options?.capture);
      let byType = listenerRecords.get(this);
      if (!byType) {
        byType = new Map();
        listenerRecords.set(this, byType);
      }
      let handlers = byType.get(type);
      if (!handlers) {
        handlers = new Map();
        byType.set(type, handlers);
      }
      let captures = handlers.get(listener);
      if (!captures) {
        captures = new Map();
        handlers.set(listener, captures);
      }
      const existing = captures.get(capture);
      if (existing?.active) {
        return native.addEventListener.call(this, type, existing.effective, options);
      }

      const token = tokenFor(this);
      const record = {
        active: true,
        token,
        signal: typeof options === 'object' ? options?.signal : null,
        abortHook: null,
        effective: listener
      };
      if (typeof options === 'object' && options?.once) {
        record.effective = function(event) {
          deactivate(record);
          if (typeof listener === 'function') return listener.call(this, event);
          return listener.handleEvent(event);
        };
      }
      if (record.signal) {
        record.abortHook = () => deactivate(record);
        native.addEventListener.call(record.signal, 'abort', record.abortHook, { once: true });
      }
      captures.set(capture, record);
      listenerCount += 1;
      token.activeCount += 1;
      return native.addEventListener.call(this, type, record.effective, options);
    };

    EventTarget.prototype.removeEventListener = function(type, listener, options) {
      const capture = typeof options === 'boolean' ? options : Boolean(options?.capture);
      const record = listenerRecords.get(this)?.get(type)?.get(listener)?.get(capture);
      if (record) {
        deactivate(record);
        return native.removeEventListener.call(this, type, record.effective, options);
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
        abortControllerLiveCount += 1;
        const token = { live: true };
        abortFinalizer?.register(this, token);
        native.addEventListener.call(this.signal, 'abort', () => {
          abortControllerAborted += 1;
          if (token.live) {
            token.live = false;
            abortControllerLiveCount -= 1;
          }
        }, { once: true });
      }
    }
    window.AbortController = InstrumentedAbortController;

    JSON.parse = function(text, reviver) {
      const decodeStartedAt = performance.now();
      try {
        return native.jsonParse(text, reviver);
      } finally {
        if (pendingTextDecode?.text === text) {
          pendingTextDecode.entry.decodeStartAt = decodeStartedAt;
          pendingTextDecode.entry.decodeEndAt = performance.now();
          pendingTextDecode.entry.decodeMs =
            pendingTextDecode.entry.decodeEndAt - pendingTextDecode.entry.decodeStartAt;
          pendingTextDecode = null;
        }
      }
    };

    function addActiveSignal(signal) {
      if (!signal) return null;
      const count = activeFetchSignals.get(signal) || 0;
      activeFetchSignals.set(signal, count + 1);
      activeFetchSignalCount += 1;
      const abortHook = () => { abortedFetchCount += 1; };
      native.addEventListener.call(signal, 'abort', abortHook, { once: true });
      return () => {
        native.removeEventListener.call(signal, 'abort', abortHook, false);
        const current = activeFetchSignals.get(signal) || 0;
        if (current <= 1) activeFetchSignals.delete(signal);
        else activeFetchSignals.set(signal, current - 1);
        activeFetchSignalCount -= 1;
      };
    }

    window.fetch = async function(input, init) {
      const request = input instanceof Request ? input : null;
      const url = String(request?.url || input);
      const signal = init?.signal || request?.signal || null;
      const releaseSignal = addActiveSignal(signal);
      const entry = {
        url,
        method: String(init?.method || request?.method || 'GET').toUpperCase(),
        startAt: performance.now(),
        responseAt: null,
        fetchMs: null,
        fixtureMs: 0,
        bodyReadStartAt: null,
        bodyReadEndAt: null,
        bodyReadMs: 0,
        decodeStartAt: null,
        decodeEndAt: null,
        decodeMs: 0,
        status: null,
        failed: false
      };
      network.push(entry);
      try {
        const response = await native.fetch(input, init);
        entry.responseAt = performance.now();
        entry.fetchMs = entry.responseAt - entry.startAt;
        entry.status = response.status;
        const serverTiming = response.headers.get('server-timing') || '';
        const fixtureMatch = serverTiming.match(/fixture;dur=([0-9.]+)/i);
        entry.fixtureMs = fixtureMatch ? Number(fixtureMatch[1]) : 0;
        const nativeJson = response.json.bind(response);
        const nativeText = response.text.bind(response);
        Object.defineProperty(response, 'json', {
          configurable: true,
          value: async () => {
            entry.decodeStartAt = performance.now();
            try {
              return await nativeJson();
            } finally {
              entry.decodeEndAt = performance.now();
              entry.decodeMs = entry.decodeEndAt - entry.decodeStartAt;
            }
          }
        });
        Object.defineProperty(response, 'text', {
          configurable: true,
          value: async () => {
            entry.bodyReadStartAt = performance.now();
            const body = await nativeText();
            entry.bodyReadEndAt = performance.now();
            entry.bodyReadMs = entry.bodyReadEndAt - entry.bodyReadStartAt;
            pendingTextDecode = { text: body, entry };
            return body;
          }
        });
        return response;
      } catch (error) {
        entry.failed = true;
        entry.responseAt = performance.now();
        entry.fetchMs = entry.responseAt - entry.startAt;
        throw error;
      } finally {
        releaseSignal?.();
      }
    };

    const longTaskSupported = typeof PerformanceObserver === 'function' &&
      PerformanceObserver.supportedEntryTypes?.includes('longtask');
    if (longTaskSupported) {
      const observer = new PerformanceObserver(list => {
        for (const entry of list.getEntries()) {
          longTasks.push({
            name: entry.name,
            startTime: entry.startTime,
            duration: entry.duration
          });
        }
      });
      observer.observe({ entryTypes: ['longtask'] });
    }

    Object.defineProperty(window, '__F02_PRODUCT_PERF__', {
      configurable: false,
      value: Object.freeze({
        probe: () => ({
          listenerCount,
          finalizedTargetCount,
          activeTimeoutCount: activeTimeouts.size,
          activeIntervalCount: activeIntervals.size,
          abortControllerCreated,
          abortControllerAborted,
          abortControllerOutstanding: abortControllerCreated - abortControllerAborted,
          abortControllerLiveCount,
          abortControllerFinalized,
          activeFetchSignalCount,
          abortedFetchCount,
          domElementCount: document.querySelectorAll('*').length
        }),
        network: () => network.map(item => ({ ...item })),
        resetLongTasks: () => { longTasks.length = 0; },
        longTasks: () => longTasks.map(item => ({ ...item })),
        longTaskSupported
      })
    });
  });
}

async function installFrozenBrowserFixture(page, authority, operators, results, requestAudit) {
  await page.addInitScript(metadata => {
    sessionStorage.setItem('cv_auth_token', 'f02-browser-fixture-token');
    sessionStorage.setItem('cv_current_user', 'fixture-user');
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: Object.freeze({
        schemaVersion: 1,
        uiKind: 'studio-ui',
        hostKind: 'browser-test',
        apiBaseUrl: `${window.location.origin}/api`,
        studioUiBasePath: '/studio/',
        featureFlags: Object.freeze({})
      }),
      writable: false,
      configurable: false
    });
    Object.defineProperty(window, '__F02_BROWSER_FIXTURE__', {
      value: Object.freeze(metadata),
      writable: false,
      configurable: false
    });
  }, {
    schemaVersion: authority.schemaVersion,
    sourceSha: authority.sourceSha,
    dataSource: authority.dataSource,
    authSource: authority.authSource
  });

  const fulfill = async (route, body, schemaVersion, status = 200, fixtureStarted = performance.now()) => {
    const serialized = JSON.stringify(body);
    const fixtureMs = performance.now() - fixtureStarted;
    const requestUrl = new URL(route.request().url());
    await route.fulfill({
      status,
      contentType: 'application/json; charset=utf-8',
      headers: {
        'server-timing': `fixture;dur=${fixtureMs.toFixed(3)}`,
        'x-clearvision-fixture-schema': schemaVersion,
        'x-clearvision-fixture-endpoint': `${route.request().method()} ${requestUrl.pathname}`,
        'x-clearvision-fixture-source-sha': authority.sourceSha,
        'x-clearvision-data-source': authority.dataSource,
        'x-clearvision-auth-source': authority.authSource
      },
      body: serialized
    });
  };

  await page.route('**/health', async route => {
    const fixtureStarted = performance.now();
    const url = new URL(route.request().url());
    requestAudit.push({ method: route.request().method(), path: `${url.pathname}${url.search}` });
    await fulfill(
      route,
      { status: 'Healthy', port: Number(url.port) },
      'f02-browser-health.v1',
      200,
      fixtureStarted
    );
  });

  await page.route('**/api/**', async route => {
    const fixtureStarted = performance.now();
    const request = route.request();
    const url = new URL(request.url());
    requestAudit.push({ method: request.method(), path: `${url.pathname}${url.search}` });
    if (url.pathname === '/api/auth/me') {
      await fulfill(route, {
        userId: 'fixture-user',
        username: 'fixture-engineer',
        role: 'Engineer'
      }, 'f02-auth-read.v1', 200, fixtureStarted);
      return;
    }
    if (url.pathname === '/api/operators/library') {
      await fulfill(route, operators, 'f02-operators-read.v1', 200, fixtureStarted);
      return;
    }
    const operatorDetail = /^\/api\/operators\/(\d+)\/metadata$/.exec(url.pathname);
    if (operatorDetail) {
      const match = operators.find(item => item.type === Number(operatorDetail[1]));
      await fulfill(
        route,
        match || { error: 'NotFound' },
        'f02-operators-read.v1',
        match ? 200 : 404,
        fixtureStarted
      );
      return;
    }
    if (url.pathname === '/api/stations/results') {
      const requestedStatus = url.searchParams.get('status');
      const requestedDiagnostic = url.searchParams.get('diagnosticCode');
      const filtered = results.filter(item => {
        const kind = item.messageId === 'fixture-result-0001'
          ? 'Failed'
          : canonicalCases[(item.sequenceId - 1) % canonicalCases.length][0];
        return (!requestedStatus || kind === requestedStatus) &&
          (!requestedDiagnostic || item.diagnosticCode === requestedDiagnostic);
      });
      const pageIndex = Number(url.searchParams.get('pageIndex') || 0);
      const pageSize = Number(url.searchParams.get('pageSize') || 20);
      await fulfill(route, {
        items: filtered.slice(pageIndex * pageSize, (pageIndex + 1) * pageSize),
        totalCount: filtered.length,
        pageIndex,
        pageSize
      }, 'f02-results-read.v1', 200, fixtureStarted);
      return;
    }
    await fulfill(route, { error: 'NotFound' }, 'f02-product-performance.v1', 404, fixtureStarted);
  });
}

async function waitForBrowserRoute(page, route) {
  if (route === 'operators') {
    await page.waitForFunction(() => {
      const capability = document.querySelector('[data-capability="operators-read"]');
      const rows = document.querySelectorAll('[data-capability="operators-read"] tbody tr');
      const loading = document.querySelector('[data-page-state="loading"]');
      return Boolean(capability && !loading && rows.length === 25);
    }, null, { timeout: 30_000 });
    return;
  }
  await page.waitForFunction(() => {
    const capability = document.querySelector('[data-capability="results-read"][data-results-source="station"]');
    const rows = document.querySelectorAll('[data-capability="results-read"] tbody tr');
    const summary = [...document.querySelectorAll('.cv-pagination__summary')]
      .some(node => node.textContent?.includes('共 500 项'));
    return Boolean(capability && summary && rows.length > 0);
  }, null, { timeout: 30_000 });
}

async function readBrowserResources(page, session) {
  await session.send('HeapProfiler.collectGarbage');
  await page.waitForTimeout(50);
  await session.send('HeapProfiler.collectGarbage');
  const [metrics, heap, probe] = await Promise.all([
    session.send('Performance.getMetrics'),
    session.send('Runtime.getHeapUsage'),
    page.evaluate(() => window.__F02_PRODUCT_PERF__.probe())
  ]);
  return {
    probe,
    performance: Object.fromEntries(metrics.metrics.map(item => [item.name, item.value])),
    heap
  };
}

async function resetLongTasks(page) {
  await page.evaluate(() => window.__F02_PRODUCT_PERF__.resetLongTasks());
}

async function readLongTasksForProduct(page) {
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve)));
  return page.evaluate(() => window.__F02_PRODUCT_PERF__.longTasks());
}

async function measureRouteAction(page, route) {
  const target = route === 'operators'
    ? { hash: '#/operators', capability: 'operators-read', rows: 25, summary: null }
    : {
        hash: '#/results?source=station&pageSize=200',
        capability: 'results-read',
        rows: 200,
        summary: '第 1–200 项，共 500 项'
      };
  return page.evaluate(async expected => {
    const start = performance.now();
    const networkBefore = window.__F02_PRODUCT_PERF__.network().length;
    window.location.hash = expected.hash;
    let readyAt = null;
    while (performance.now() - start < 30_000) {
      const capability = document.querySelector(`[data-capability="${expected.capability}"]`);
      const rows = capability?.querySelectorAll('tbody tr').length || 0;
      const summaryReady = !expected.summary || [...document.querySelectorAll('.cv-pagination__summary')]
        .some(node => node.textContent?.includes(expected.summary));
      if (capability && rows === expected.rows && summaryReady) {
        readyAt = performance.now();
        break;
      }
      await new Promise(resolve => requestAnimationFrame(resolve));
    }
    if (readyAt === null) throw new Error(`Route ${expected.hash} did not become ready.`);
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const completedAt = performance.now();
    const network = window.__F02_PRODUCT_PERF__.network()
      .slice(networkBefore)
      .filter(item => new URL(item.url).pathname.startsWith('/api/'));
    const fixtureMs = network.reduce((sum, item) => sum + (item.fixtureMs || 0), 0);
    const transportExcludingFixtureMs = network.reduce((sum, item) =>
      sum + Math.max(0, (item.fetchMs || 0) + (item.bodyReadMs || 0) -
        (item.fixtureMs || 0)), 0);
    const decodeMs = network.reduce((sum, item) => sum + (item.decodeMs || 0), 0);
    const decodeCompletedAt = network.reduce((latest, item) =>
      Math.max(latest, item.decodeEndAt || item.responseAt || start), start);
    return {
      route: expected.hash,
      totalMs: completedAt - start,
      fixtureMs,
      transportExcludingFixtureMs,
      decodeMs,
      viewModelMs: Math.max(0, readyAt - decodeCompletedAt),
      domRenderMs: Math.max(0, completedAt - readyAt),
      network
    };
  }, target);
}

async function measureSearchAction(page, value, expectedCount, expectedText) {
  return page.evaluate(async expected => {
    const input = document.querySelector('input[type="search"]');
    if (!input) throw new Error('Operator search input was not found.');
    const start = performance.now();
    const networkBefore = window.__F02_PRODUCT_PERF__.network().length;
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    setter.call(input, expected.value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
    let readyAt = null;
    while (performance.now() - start < 30_000) {
      const rows = [...document.querySelectorAll('[data-capability="operators-read"] tbody tr')];
      const rowText = rows.map(row => row.textContent || '').join('\n');
      const query = new URLSearchParams(window.location.hash.split('?')[1] || '');
      const queryReady = expected.value ? query.get('q') === expected.value : !query.has('q');
      if (queryReady && rows.length === expected.count &&
          (!expected.text || rowText.includes(expected.text))) {
        readyAt = performance.now();
        break;
      }
      await new Promise(resolve => requestAnimationFrame(resolve));
    }
    if (readyAt === null) throw new Error(`Operator search did not render ${expected.value}.`);
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const completedAt = performance.now();
    const network = window.__F02_PRODUCT_PERF__.network().slice(networkBefore);
    return {
      value: expected.value,
      totalMs: completedAt - start,
      fixtureMs: 0,
      transportExcludingFixtureMs: 0,
      decodeMs: 0,
      viewModelMs: readyAt - start,
      domRenderMs: completedAt - readyAt,
      unexpectedNetworkCount: network.length
    };
  }, { value, count: expectedCount, text: expectedText });
}

async function measurePaginationAction(page, targetPage) {
  return page.evaluate(async expectedPage => {
    const button = document.querySelector(`button[aria-label="第 ${expectedPage} 页"]`);
    if (!button) throw new Error(`Results page button ${expectedPage} was not found.`);
    const expectedSummary = expectedPage === 3
      ? '第 401–500 项，共 500 项'
      : '第 1–200 项，共 500 项';
    const expectedRows = expectedPage === 3 ? 100 : 200;
    const start = performance.now();
    const networkBefore = window.__F02_PRODUCT_PERF__.network().length;
    button.click();
    let readyAt = null;
    while (performance.now() - start < 30_000) {
      const current = document.querySelector(`button[aria-label="第 ${expectedPage} 页"][aria-current="page"]`);
      const rows = document.querySelectorAll('[data-capability="results-read"] tbody tr').length;
      const summary = [...document.querySelectorAll('.cv-pagination__summary')]
        .some(node => node.textContent?.includes(expectedSummary));
      if (current && rows === expectedRows && summary) {
        readyAt = performance.now();
        break;
      }
      await new Promise(resolve => requestAnimationFrame(resolve));
    }
    if (readyAt === null) throw new Error(`Results page ${expectedPage} did not render.`);
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const completedAt = performance.now();
    const network = window.__F02_PRODUCT_PERF__.network()
      .slice(networkBefore)
      .filter(item => new URL(item.url).pathname === '/api/stations/results');
    const fixtureMs = network.reduce((sum, item) => sum + (item.fixtureMs || 0), 0);
    const transportExcludingFixtureMs = network.reduce((sum, item) =>
      sum + Math.max(0, (item.fetchMs || 0) + (item.bodyReadMs || 0) -
        (item.fixtureMs || 0)), 0);
    const decodeMs = network.reduce((sum, item) => sum + (item.decodeMs || 0), 0);
    const decodeCompletedAt = network.reduce((latest, item) =>
      Math.max(latest, item.decodeEndAt || item.responseAt || start), start);
    return {
      page: expectedPage,
      totalMs: completedAt - start,
      fixtureMs,
      transportExcludingFixtureMs,
      decodeMs,
      viewModelMs: Math.max(0, readyAt - decodeCompletedAt),
      domRenderMs: completedAt - readyAt,
      network
    };
  }, targetPage);
}

async function measurePreferenceAction(page, groupLabel, buttonText, attribute, expectedValue) {
  return page.evaluate(async expected => {
    const group = [...document.querySelectorAll('[role="group"]')]
      .find(node => node.getAttribute('aria-label') === expected.groupLabel);
    const button = [...(group?.querySelectorAll('button') || [])]
      .find(node => node.textContent?.trim() === expected.buttonText);
    if (!button) throw new Error(`${expected.groupLabel}/${expected.buttonText} was not found.`);
    const start = performance.now();
    button.click();
    let readyAt = null;
    while (performance.now() - start < 30_000) {
      if (document.documentElement.getAttribute(expected.attribute) === expected.value) {
        readyAt = performance.now();
        break;
      }
      await new Promise(resolve => requestAnimationFrame(resolve));
    }
    if (readyAt === null) throw new Error(`${expected.attribute} did not become ${expected.value}.`);
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const completedAt = performance.now();
    return {
      value: expected.value,
      totalMs: completedAt - start,
      fixtureMs: 0,
      transportExcludingFixtureMs: 0,
      decodeMs: 0,
      viewModelMs: readyAt - start,
      domRenderMs: completedAt - readyAt
    };
  }, { groupLabel, buttonText, attribute, value: expectedValue });
}

async function readOverflow(page, route) {
  return page.evaluate(routeId => {
    const html = document.documentElement;
    const body = document.body;
    const viewportWidth = window.innerWidth;
    return {
      route: routeId,
      viewportWidth,
      viewportHeight: window.innerHeight,
      htmlScrollWidth: html.scrollWidth,
      bodyScrollWidth: body.scrollWidth,
      horizontalOverflowPx: Math.max(0, html.scrollWidth - viewportWidth, body.scrollWidth - viewportWidth),
      passed: html.scrollWidth <= viewportWidth && body.scrollWidth <= viewportWidth
    };
  }, route);
}

function resourceDeltas(before, after) {
  return {
    listenerCount: after.probe.listenerCount - before.probe.listenerCount,
    activeTimeoutCount: after.probe.activeTimeoutCount - before.probe.activeTimeoutCount,
    activeIntervalCount: after.probe.activeIntervalCount - before.probe.activeIntervalCount,
    activeFetchSignalCount: after.probe.activeFetchSignalCount - before.probe.activeFetchSignalCount,
    abortControllerLiveCount:
      after.probe.abortControllerLiveCount - before.probe.abortControllerLiveCount,
    abortControllerOutstanding:
      after.probe.abortControllerOutstanding - before.probe.abortControllerOutstanding,
    domElementCount: after.probe.domElementCount - before.probe.domElementCount,
    jsHeapUsedSize:
      (after.performance.JSHeapUsedSize || 0) - (before.performance.JSHeapUsedSize || 0),
    nodes: (after.performance.Nodes || 0) - (before.performance.Nodes || 0),
    jsEventListeners:
      (after.performance.JSEventListeners || 0) - (before.performance.JSEventListeners || 0)
  };
}

function routeProbeStability(routeProbes) {
  const fields = [
    'listenerCount',
    'activeTimeoutCount',
    'activeIntervalCount',
    'activeFetchSignalCount',
    'abortControllerLiveCount',
    'domElementCount'
  ];
  const byRoute = {};
  for (const route of ['operators', 'results']) {
    const probes = routeProbes.filter(item => item.route === route).map(item => item.probe);
    byRoute[route] = Object.fromEntries(fields.map(field => {
      const values = probes.map(item => item[field]);
      return [field, {
        first: values[0],
        last: values.at(-1),
        minimum: Math.min(...values),
        maximum: Math.max(...values),
        range: Math.max(...values) - Math.min(...values),
        monotonicallyIncreasing: values.length > 1 && values.every((value, index) =>
          index === 0 || value > values[index - 1])
      }];
    }));
  }
  const stable = Object.values(byRoute).every(route =>
    route.activeTimeoutCount.range === 0 &&
    route.activeIntervalCount.range === 0 &&
    route.activeFetchSignalCount.maximum === 0 &&
    route.abortControllerLiveCount.range === 0 &&
    route.domElementCount.range === 0 &&
    !route.listenerCount.monotonicallyIncreasing);
  return { stable, byRoute };
}

function pngDimensions(filePath) {
  const buffer = fs.readFileSync(filePath);
  assert(buffer.length >= 24 && buffer.toString('ascii', 1, 4) === 'PNG',
    `Screenshot is not a PNG: ${filePath}`);
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

async function captureServedStaticAssets(origin) {
  const indexUrl = `${origin}/studio/index.html`;
  const indexResponse = await fetch(indexUrl);
  assert(indexResponse.ok, `Static index returned ${indexResponse.status}.`);
  const indexBuffer = Buffer.from(await indexResponse.arrayBuffer());
  const indexHtml = indexBuffer.toString('utf8');
  const linkedPaths = [...indexHtml.matchAll(/(?:src|href)="([^"]+)"/g)]
    .map(match => match[1])
    .filter(value => !value.startsWith('data:'));
  const linked = [];
  for (const linkedPath of linkedPaths) {
    const assetUrl = new URL(linkedPath, indexUrl).href;
    const response = await fetch(assetUrl);
    assert(response.ok, `Static asset returned ${response.status}: ${assetUrl}`);
    const buffer = Buffer.from(await response.arrayBuffer());
    linked.push({
      url: assetUrl,
      contentType: response.headers.get('content-type'),
      bytes: buffer.length,
      sha256: crypto.createHash('sha256').update(buffer).digest('hex')
    });
  }
  return {
    index: {
      url: indexUrl,
      bytes: indexBuffer.length,
      sha256: crypto.createHash('sha256').update(indexBuffer).digest('hex')
    },
    linked
  };
}

async function runDprScenario(browser, options) {
  const {
    authority,
    operators,
    results,
    origin,
    evidenceDir,
    requestedDpr,
    actionWarmups,
    actionSamples
  } = options;
  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
    deviceScaleFactor: requestedDpr,
    colorScheme: 'light',
    reducedMotion: 'no-preference'
  });
  const page = await context.newPage();
  const requestAudit = [];
  const runtimeErrors = {
    consoleErrors: [],
    pageErrors: [],
    requestFailures: []
  };
  page.on('console', message => {
    if (message.type() === 'error') runtimeErrors.consoleErrors.push(message.text());
  });
  page.on('pageerror', error => runtimeErrors.pageErrors.push(error.stack || error.message));
  page.on('requestfailed', request => {
    const failure = request.failure();
    if (!/ERR_ABORTED|NS_BINDING_ABORTED/i.test(failure?.errorText || '')) {
      runtimeErrors.requestFailures.push({ url: request.url(), errorText: failure?.errorText || '' });
    }
  });

  await installBrowserInstrumentation(page);
  await installFrozenBrowserFixture(page, authority, operators, results, requestAudit);
  const session = await context.newCDPSession(page);
  await session.send('Performance.enable');

  try {
    await page.goto(`${origin}/studio/index.html#/operators`, {
      waitUntil: 'domcontentloaded',
      timeout: 45_000
    });
    await waitForBrowserRoute(page, 'operators');
    await page.evaluate(() => new Promise(resolve =>
      requestAnimationFrame(() => requestAnimationFrame(resolve))));

    const observed = await page.evaluate(() => ({
      devicePixelRatio: window.devicePixelRatio,
      innerWidth: window.innerWidth,
      innerHeight: window.innerHeight,
      screenWidth: window.screen.width,
      screenHeight: window.screen.height,
      longTaskSupported: window.__F02_PRODUCT_PERF__.longTaskSupported
    }));
    assert(Math.abs(observed.devicePixelRatio - requestedDpr) <= 0.01,
      `Requested browser DPR ${requestedDpr} but observed ${observed.devicePixelRatio}.`);

    await measureRouteAction(page, 'results');
    await measureRouteAction(page, 'operators');
    const resourcesBefore = await readBrowserResources(page, session);
    await resetLongTasks(page);
    const routeSwitchSamples = [];
    const routeProbes = [];
    for (let index = 0; index < 20; index += 1) {
      const route = index % 2 === 0 ? 'results' : 'operators';
      routeSwitchSamples.push(await measureRouteAction(page, route));
      await session.send('HeapProfiler.collectGarbage');
      await page.waitForTimeout(10);
      routeProbes.push({
        iteration: index + 1,
        route,
        probe: await page.evaluate(() => window.__F02_PRODUCT_PERF__.probe())
      });
    }
    const routeLongTasks = await readLongTasksForProduct(page);
    const resourcesAfter = await readBrowserResources(page, session);
    const deltas = resourceDeltas(resourcesBefore, resourcesAfter);
    const lifecycle = routeProbeStability(routeProbes);
    lifecycle.before = resourcesBefore;
    lifecycle.after = resourcesAfter;
    lifecycle.deltas = deltas;
    lifecycle.abortInterpretation =
      'activeFetchSignalCount plus GC-stabilized live AbortController count are the disposal gates; constructed-minus-aborted is observational because completed requests need not abort their controller.';
    lifecycle.passed = lifecycle.stable &&
      deltas.activeTimeoutCount === 0 &&
      deltas.activeIntervalCount === 0 &&
      deltas.activeFetchSignalCount === 0 &&
      deltas.abortControllerLiveCount === 0 &&
      deltas.domElementCount === 0;

    for (let index = 0; index < actionWarmups; index += 1) {
      await measureSearchAction(page, '性能算子 150', 1, '性能算子 150');
      await measureSearchAction(page, '', 25, '颜色分析');
    }
    await resetLongTasks(page);
    const searchSamples = [];
    for (let index = 0; index < actionSamples; index += 1) {
      const populated = index % 2 === 0;
      searchSamples.push(await measureSearchAction(
        page,
        populated ? '性能算子 150' : '',
        populated ? 1 : 25,
        populated ? '性能算子 150' : '颜色分析'
      ));
    }
    const searchLongTasks = await readLongTasksForProduct(page);

    await measureRouteAction(page, 'results');
    for (let index = 0; index < actionWarmups; index += 1) {
      await measurePaginationAction(page, 3);
      await measurePaginationAction(page, 1);
    }
    await resetLongTasks(page);
    const paginationSamples = [];
    for (let index = 0; index < actionSamples; index += 1) {
      paginationSamples.push(await measurePaginationAction(page, index % 2 === 0 ? 3 : 1));
    }
    const paginationLongTasks = await readLongTasksForProduct(page);

    for (let index = 0; index < actionWarmups; index += 1) {
      await measurePreferenceAction(page, '主题', '深色', 'data-theme', 'dark');
      await measurePreferenceAction(page, '主题', '浅色', 'data-theme', 'light');
      await measurePreferenceAction(page, '界面密度', '舒适', 'data-density', 'comfortable');
      await measurePreferenceAction(page, '界面密度', '紧凑', 'data-density', 'compact');
    }
    await resetLongTasks(page);
    const themeSamples = [];
    const densitySamples = [];
    for (let index = 0; index < actionSamples; index += 1) {
      const dark = index % 2 === 0;
      themeSamples.push(await measurePreferenceAction(
        page,
        '主题',
        dark ? '深色' : '浅色',
        'data-theme',
        dark ? 'dark' : 'light'
      ));
      const comfortable = index % 2 === 0;
      densitySamples.push(await measurePreferenceAction(
        page,
        '界面密度',
        comfortable ? '舒适' : '紧凑',
        'data-density',
        comfortable ? 'comfortable' : 'compact'
      ));
    }
    const preferenceLongTasks = await readLongTasksForProduct(page);

    await measureRouteAction(page, 'operators');
    const operatorOverflow = await readOverflow(page, 'operators');
    await measureRouteAction(page, 'results');
    const resultsOverflow = await readOverflow(page, 'results');
    const screenshotDirectory = path.join(evidenceDir, `dpr-${String(requestedDpr).replace('.', '-')}`);
    fs.mkdirSync(screenshotDirectory, { recursive: true });
    const screenshotPath = path.join(screenshotDirectory, 'results-1366x768.png');
    await page.screenshot({ path: screenshotPath });
    const screenshot = {
      path: screenshotPath,
      sha256: sha256File(screenshotPath),
      ...pngDimensions(screenshotPath)
    };

    const allLongTasks = [
      ...routeLongTasks,
      ...searchLongTasks,
      ...paginationLongTasks,
      ...preferenceLongTasks
    ];
    const maximumLongTaskMs = allLongTasks.reduce((maximum, item) =>
      Math.max(maximum, item.duration), 0);
    const routeSummary = summarizeActionSamples(routeSwitchSamples);
    const searchSummary = summarizeActionSamples(searchSamples);
    const paginationSummary = summarizeActionSamples(paginationSamples);
    const themeSummary = summarizeActionSamples(themeSamples);
    const densitySummary = summarizeActionSamples(densitySamples);
    const gates = {
      loadedRouteP95: {
        budgetMs: 250,
        actualMs: routeSummary.total.p95Ms,
        passed: routeSummary.total.p95Ms <= 250
      },
      operatorSearchP95: {
        budgetMs: 100,
        actualMs: searchSummary.total.p95Ms,
        passed: searchSummary.total.p95Ms <= 100
      },
      resultsPaginationP95: {
        budgetMs: 300,
        actualMs: paginationSummary.total.p95Ms,
        passed: paginationSummary.total.p95Ms <= 300
      },
      themeP95: {
        budgetMs: 150,
        actualMs: themeSummary.total.p95Ms,
        passed: themeSummary.total.p95Ms <= 150
      },
      densityP95: {
        budgetMs: 150,
        actualMs: densitySummary.total.p95Ms,
        passed: densitySummary.total.p95Ms <= 150
      },
      longTaskMaximum: {
        budgetMs: 200,
        actualMs: round(maximumLongTaskMs),
        passed: maximumLongTaskMs <= 200
      },
      longTaskObserver: {
        supported: observed.longTaskSupported,
        passed: observed.longTaskSupported
      },
      lifecycle: { passed: lifecycle.passed },
      horizontalOverflow: {
        passed: operatorOverflow.passed && resultsOverflow.passed
      },
      browserDpr: {
        requested: requestedDpr,
        observed: observed.devicePixelRatio,
        passed: Math.abs(observed.devicePixelRatio - requestedDpr) <= 0.01
      },
      screenshotScale: {
        expectedWidth: Math.round(1366 * requestedDpr),
        expectedHeight: Math.round(768 * requestedDpr),
        actualWidth: screenshot.width,
        actualHeight: screenshot.height,
        passed: screenshot.width === Math.round(1366 * requestedDpr) &&
          screenshot.height === Math.round(768 * requestedDpr)
      },
      getOnly: {
        passed: requestAudit.every(item => item.method === 'GET')
      },
      runtimeErrors: {
        passed: runtimeErrors.consoleErrors.length === 0 &&
          runtimeErrors.pageErrors.length === 0 && runtimeErrors.requestFailures.length === 0
      }
    };
    const passed = Object.values(gates).every(gate => gate.passed);
    return {
      requestedDpr,
      observed,
      summaries: {
        loadedRoute: routeSummary,
        operatorSearch: searchSummary,
        resultsPagination: paginationSummary,
        theme: themeSummary,
        density: densitySummary
      },
      samples: {
        routeSwitch: routeSwitchSamples,
        operatorSearch: searchSamples,
        resultsPagination: paginationSamples,
        theme: themeSamples,
        density: densitySamples
      },
      longTasks: {
        supported: observed.longTaskSupported,
        maximumMs: round(maximumLongTaskMs),
        count: allLongTasks.length,
        over200ms: allLongTasks.filter(item => item.duration > 200),
        route: routeLongTasks,
        operatorSearch: searchLongTasks,
        resultsPagination: paginationLongTasks,
        preferences: preferenceLongTasks
      },
      lifecycle,
      overflow: [operatorOverflow, resultsOverflow],
      screenshot,
      requestAudit,
      runtimeErrors,
      gates,
      passed
    };
  } finally {
    await session.detach();
    await context.close();
  }
}

async function runBrowserFixtureProfile() {
  const authority = parseFrozenFixtureAuthority();
  const origin = required('CV_UI_BASE_URL').replace(/\/$/, '');
  const evidenceDir = path.resolve(required('CV_EVIDENCE_DIR'));
  const sourceSha = required('CV_F02_SOURCE_SHA');
  assert(/^[0-9a-f]{40}$/i.test(sourceSha),
    'CV_F02_SOURCE_SHA must contain the 40-character frozen candidate SHA.');
  const actionWarmups = Number.parseInt(process.env.CV_F02_PERF_WARMUPS || '2', 10);
  const actionSamples = Number.parseInt(process.env.CV_F02_PERF_SAMPLES || '20', 10);
  assert(actionWarmups >= 1, 'CV_F02_PERF_WARMUPS must be at least 1.');
  assert(actionSamples >= 20, 'CV_F02_PERF_SAMPLES must be at least 20 for p95.');
  const url = new URL(origin);
  assert(['127.0.0.1', 'localhost'].includes(url.hostname),
    'Browser fixture performance origin must be local.');
  const evidenceRoot = path.resolve(repoRoot, '.tmp/studio-ui-next/f02/performance-owner');
  const evidencePrefix = `${evidenceRoot}${path.sep}`;
  assert(evidenceDir.startsWith(evidencePrefix),
    'CV_EVIDENCE_DIR must remain under .tmp/studio-ui-next/f02/performance-owner.');
  fs.mkdirSync(evidenceDir, { recursive: true });
  const outputPath = path.join(evidenceDir, 'studio-ui-product-performance.json');
  assert(!fs.existsSync(outputPath), `Evidence already exists: ${outputPath}`);
  const scriptPath = __filename;
  const operators = Object.freeze(
    Array.from({ length: authority.operatorCount }, (_, index) => createOperatorFixture(index))
  );
  const results = Object.freeze(
    Array.from({ length: authority.resultCount }, (_, index) => createStationResultFixture(index))
  );
  const browser = await chromium.launch({ headless: true });
  const startedAtUtc = new Date().toISOString();
  try {
    const browserVersion = browser.version();
    const servedStaticAssets = await captureServedStaticAssets(origin);
    const scenarios = [];
    for (const requestedDpr of [1, 1.25, 1.5, 2]) {
      scenarios.push(await runDprScenario(browser, {
        authority,
        operators,
        results,
        origin,
        evidenceDir,
        requestedDpr,
        actionWarmups,
        actionSamples
      }));
    }
    const payload = {
      schemaVersion: 2,
      phase: 'f02-product-final-browser-fixture',
      status: scenarios.every(item => item.passed) ? 'pass' : 'fail',
      sourceSha,
      capturedAtUtc: new Date().toISOString(),
      startedAtUtc,
      origin,
      configuration: 'StudioUI static build + Playwright Chromium',
      dataSource: authority.dataSource,
      authSource: authority.authSource,
      fixtureAuthority: {
        ...authority,
        generatedOperatorCount: operators.length,
        generatedResultCount: results.length
      },
      artifacts: {
        script: { path: scriptPath, sha256: sha256File(scriptPath) },
        servedStaticAssets
      },
      machine: {
        platform: os.platform(),
        release: os.release(),
        arch: os.arch(),
        cpuModel: os.cpus()[0]?.model || null,
        logicalCpuCount: os.cpus().length,
        totalMemoryBytes: os.totalmem()
      },
      browser: {
        engine: 'chromium',
        version: browserVersion,
        viewportCssPixels: { width: 1366, height: 768 },
        dprKind: 'PLAYWRIGHT_BROWSER_DEVICE_SCALE_FACTOR',
        requestedDprMatrix: [1, 1.25, 1.5, 2],
        realWindowsDpiMatrix: 'NOT PERFORMED',
        perMonitorMove: 'NOT PERFORMED'
      },
      sampling: {
        loadedRouteWarmup: 'one loaded visit per measured route before formal switching',
        routeSwitchSamples: 20,
        actionWarmups,
        actionSamples,
        percentile: 'nearest-rank p95',
        interval: 'input/hash mutation through expected view-model state and two animation frames',
        decomposition: [
          'fixture filtering, pagination and JSON serialization from Server-Timing',
          'fetch plus response-body transport excluding fixture handler time',
          'Response.json decode',
          'filter/view-model from action or decode completion to expected DOM state',
          'DOM render from expected state to two animation frames'
        ]
      },
      budgetsMs: {
        loadedRouteP95: 250,
        operatorSearchP95: 100,
        resultsPaginationP95: 300,
        themeP95: 150,
        densityP95: 150,
        longTaskMaximum: 200
      },
      scenarios
    };
    fs.writeFileSync(outputPath, `${JSON.stringify(payload, null, 2)}\n`, 'utf8');
    const evidenceSha256 = sha256File(outputPath);
    process.stdout.write(`${JSON.stringify({
      ok: payload.status === 'pass',
      outputPath,
      evidenceSha256,
      status: payload.status,
      gates: scenarios.map(item => ({ dpr: item.requestedDpr, gates: item.gates }))
    })}\n`);
    if (payload.status !== 'pass') {
      throw new Error(`F02 browser fixture performance gates failed. Evidence: ${outputPath}`);
    }
  } finally {
    await browser.close();
  }
}

async function runWebView2Profile() {
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

async function main() {
  const profile = String(process.env.CV_F02_PERF_PROFILE || 'product').trim().toLowerCase();
  if (profile === 'browser-fixture') {
    await runBrowserFixtureProfile();
    return;
  }
  await runWebView2Profile();
}

main().catch(error => {
  process.stderr.write(`${error.stack || error}\n`);
  process.exitCode = 1;
});
