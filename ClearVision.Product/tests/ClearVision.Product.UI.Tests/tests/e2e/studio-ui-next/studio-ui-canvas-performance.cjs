'use strict';

const path = require('node:path');
const {
  assert,
  captureRuntimeErrors,
  connectToDesktopWebView2,
  readBrowserDpiEvidence,
  requiredEnvironment,
  runDesktopRuntimeProbe,
  safeFileName,
  seedAuthenticatedSession,
  waitForDoubleAnimationFrame,
  writeJsonEvidence
} = require('./webview2-harness.cjs');
const {
  createCanvasFixtureDescriptor,
  createFlowIdentityFingerprint
} = require('./canvas-benchmark-fixture.cjs');

const fixtures = Object.freeze({
  benchmark: createCanvasFixtureDescriptor(100, 150),
  stress: createCanvasFixtureDescriptor(300, 450)
});

function positiveIntegerEnvironment(name, fallback, minimum) {
  const raw = String(process.env[name] || '').trim();
  const value = raw ? Number.parseInt(raw, 10) : fallback;
  assert(Number.isInteger(value) && value >= minimum, `${name} must be at least ${minimum}.`);
  return value;
}

function meaningfulRequestFailures(requestFailures) {
  return requestFailures.filter(item => !/ERR_ABORTED|NS_BINDING_ABORTED/i.test(item.errorText));
}

async function installMeasurementHelpers(page) {
  return page.evaluate(() => {
    window.__CV_PERF_LONG_TASKS__ = [];
    window.__CV_PERF_LONG_TASK_OBSERVER__?.disconnect?.();
    const supported = typeof PerformanceObserver === 'function' &&
      PerformanceObserver.supportedEntryTypes?.includes('longtask');
    if (supported) {
      const observer = new PerformanceObserver(list => {
        for (const entry of list.getEntries()) {
          window.__CV_PERF_LONG_TASKS__.push({
            name: entry.name,
            startTime: entry.startTime,
            duration: entry.duration
          });
        }
      });
      observer.observe({ entryTypes: ['longtask'] });
      window.__CV_PERF_LONG_TASK_OBSERVER__ = observer;
    } else {
      window.__CV_PERF_LONG_TASK_OBSERVER__ = null;
    }
    window.__CV_PERF_PAINT_PROBES__ = {};
    return supported;
  });
}

async function resetLongTasks(page) {
  await page.evaluate(() => { window.__CV_PERF_LONG_TASKS__ = []; });
}

async function readLongTasks(page) {
  await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 0)));
  return page.evaluate(() => [...(window.__CV_PERF_LONG_TASKS__ || [])]);
}

async function armPaintProbe(page, selector, eventType) {
  const id = `paint-${Date.now()}-${Math.random().toString(16).slice(2)}`;
  await page.evaluate(({ id: probeId, selector: targetSelector, eventType: type }) => {
    const target = document.querySelector(targetSelector);
    if (!target) throw new Error(`Paint probe target was not found: ${targetSelector}`);
    const state = window.__CV_PERF_PAINT_PROBES__[probeId] = {
      eventType: type,
      done: false,
      inputToDoubleRafMs: null,
      handlerToDoubleRafMs: null
    };
    const handler = event => {
      window.removeEventListener(type, handler, true);
      const handlerTime = performance.now();
      const raw = Number(event.timeStamp);
      const relative = raw > 1e12 ? raw - performance.timeOrigin : raw;
      const eventTime = Number.isFinite(relative) && Math.abs(relative - handlerTime) < 60_000
        ? relative
        : handlerTime;
      requestAnimationFrame(() => requestAnimationFrame(() => {
        const completed = performance.now();
        state.inputToDoubleRafMs = completed - eventTime;
        state.handlerToDoubleRafMs = completed - handlerTime;
        state.done = true;
      }));
    };
    window.addEventListener(type, handler, true);
  }, { id, selector, eventType });
  return id;
}

async function finishPaintProbe(page, id) {
  await page.waitForFunction(probeId =>
    window.__CV_PERF_PAINT_PROBES__?.[probeId]?.done === true,
  id, { timeout: 30_000 });
  return page.evaluate(probeId => {
    const value = window.__CV_PERF_PAINT_PROBES__[probeId];
    delete window.__CV_PERF_PAINT_PROBES__[probeId];
    return value;
  }, id);
}

async function installLegacyActions(page) {
  await page.evaluate(({ benchmark, stress }) => {
    const clone = value => typeof structuredClone === 'function'
      ? structuredClone(value)
      : JSON.parse(JSON.stringify(value));
    const fixtureMap = { benchmark: clone(benchmark), stress: clone(stress) };
    const metadata = {
      benchmark: { id: benchmark.id, name: benchmark.name },
      stress: { id: stress.id, name: stress.name }
    };
    const state = window.__CV_CANVAS_PERF_STATE__ = { current: null, identity: null };
    const envelope = kind => {
      const serialized = window.flowCanvasAdapter.serialize();
      return {
        id: metadata[kind].id,
        name: metadata[kind].name,
        operators: serialized.operators,
        connections: serialized.connections,
        decisionConfiguration: serialized.decisionConfiguration ?? null
      };
    };
    const load = kind => {
      window.flowCanvasAdapter.replaceFlow(fixtureMap[kind]);
      window.flowCanvas.scale = 1;
      window.flowCanvas.offset = { x: 0, y: 0 };
      window.flowCanvas.render();
      state.current = kind;
      state.identity = null;
    };
    const identity = () => {
      const kind = state.current;
      if (!kind) throw new Error('Legacy performance fixture is not loaded.');
      const before = envelope(kind);
      window.flowCanvasAdapter.replaceFlow(before);
      const after = envelope(kind);
      state.identity = { before, after };
    };

    document.querySelector('[data-cv-perf-controls]')?.remove();
    const controls = document.createElement('div');
    controls.dataset.cvPerfControls = 'true';
    controls.style.cssText = 'position:fixed;left:8px;bottom:8px;z-index:2147483647;display:flex;gap:4px';
    for (const [action, callback] of [
      ['load-benchmark', () => load('benchmark')],
      ['load-stress', () => load('stress')],
      ['identity', identity]
    ]) {
      const button = document.createElement('button');
      button.type = 'button';
      button.dataset.cvPerfAction = action;
      button.textContent = action;
      button.addEventListener('click', callback);
      controls.appendChild(button);
    }
    document.body.appendChild(controls);
  }, { benchmark: fixtures.benchmark.flow, stress: fixtures.stress.flow });
}

async function openRuntime(page, runtime, webPort) {
  const url = runtime === 'legacy'
    ? `http://localhost:${webPort}/index.html`
    : `http://localhost:${webPort}/studio/index.html#/labs/canvas`;
  await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
  await waitForRuntime(page, runtime);
}

async function waitForRuntime(page, runtime) {
  if (runtime === 'legacy') {
    await page.waitForSelector('#loading-screen', { state: 'hidden', timeout: 45_000 });
    await page.waitForSelector('#flow-canvas', { state: 'visible' });
    await page.waitForFunction(() => Boolean(window.flowCanvas && window.flowCanvasAdapter));
    await installLegacyActions(page);
  } else {
    await page.waitForFunction(() => window.__STUDIO_UI_READY__ === true, null, { timeout: 45_000 });
    await page.waitForSelector('[data-canvas-lab="ready"]', { state: 'visible', timeout: 45_000 });
    await page.waitForFunction(() => window.__STUDIO_UI_CANVAS_DIAGNOSTICS__?.ownerCount === 1);
  }
}

async function reloadRuntime(page, runtime) {
  await page.reload({ waitUntil: 'domcontentloaded', timeout: 45_000 });
  await waitForRuntime(page, runtime);
  return installMeasurementHelpers(page);
}

async function readCanvasSnapshot(page, runtime) {
  if (runtime === 'studio') {
    return page.evaluate(() => ({ ...window.__STUDIO_UI_CANVAS_DIAGNOSTICS__.runtime }));
  }
  return page.evaluate(() => {
    const canvas = window.flowCanvas;
    const interaction = window.flowEditorInteraction;
    const selection = canvas.getSelectionState?.() || {};
    return {
      nodeCount: canvas.nodes.size,
      connectionCount: canvas.connections.length,
      flowRevision: Number(canvas.getFlowRevision?.()) || 0,
      selectionRevision: Number(selection.selectionRevision) || 0,
      selectedNodeId: selection.selectedNodeId || canvas.selectedNode || null,
      selectedConnectionId: selection.selectedConnectionId || canvas.selectedConnection?.id || null,
      scale: Number(canvas.scale) || 1,
      offsetX: Number(canvas.offset?.x) || 0,
      offsetY: Number(canvas.offset?.y) || 0,
      logicalWidth: Number(canvas._logicalWidth) || canvas.canvas.clientWidth,
      logicalHeight: Number(canvas._logicalHeight) || canvas.canvas.clientHeight,
      backingWidth: canvas.canvas.width,
      backingHeight: canvas.canvas.height,
      dpr: Number(canvas._dpr) || window.devicePixelRatio,
      nodes: [...canvas.nodes.values()].map(node => {
        const rect = canvas.getNodeScreenRect?.(node.id) || {};
        return {
          id: node.id,
          x: Number(rect.x) || 0,
          y: Number(rect.y) || 0,
          width: Number(rect.width) || 0,
          height: Number(rect.height) || 0
        };
      }),
      resources: {
        adapterDisposed: window.flowCanvasAdapter?.disposed === true,
        canvasDestroyed: canvas._isDestroyed === true,
        interactionDisposed: interaction?.disposed === true,
        resizeObserverActive: Boolean(canvas._resizeObserver),
        themeObserverActive: Boolean(canvas._themeObserver),
        structureListenerCount: canvas.structureStateListeners?.size || 0,
        viewListenerCount: canvas.viewStateListeners?.size || 0,
        selectionListenerCount: canvas.selectionStateListeners?.size || 0,
        interactionCleanupCount: interaction?.cleanup?.length || 0
      }
    };
  });
}

function assertFixtureSnapshot(snapshot, descriptor, label) {
  assert(snapshot.nodeCount === descriptor.nodeCount,
    `${label} node count is ${snapshot.nodeCount}, expected ${descriptor.nodeCount}.`);
  assert(snapshot.connectionCount === descriptor.connectionCount,
    `${label} connection count is ${snapshot.connectionCount}, expected ${descriptor.connectionCount}.`);
  assert(snapshot.logicalWidth > 0 && snapshot.logicalHeight > 0, `${label} logical size is empty.`);
  assert(snapshot.backingWidth > 0 && snapshot.backingHeight > 0, `${label} backing size is empty.`);
  assert(Math.abs(snapshot.backingWidth - snapshot.logicalWidth * snapshot.dpr) <= 2,
    `${label} backing width does not match DPR.`);
  assert(Math.abs(snapshot.backingHeight - snapshot.logicalHeight * snapshot.dpr) <= 2,
    `${label} backing height does not match DPR.`);
}

function fixtureSelector(runtime, kind) {
  if (runtime === 'legacy') return `[data-cv-perf-action="load-${kind}"]`;
  return kind === 'benchmark'
    ? '[data-canvas-action="load-benchmark-100"]'
    : '[data-canvas-action="load-stress-300"]';
}

async function loadFixture(page, runtime, kind) {
  const descriptor = fixtures[kind];
  await page.locator(fixtureSelector(runtime, kind)).click();
  if (runtime === 'studio') {
    const fixtureId = kind === 'benchmark' ? 'benchmark-100' : 'stress-300';
    await page.waitForFunction(({ fixtureId, nodes, connections }) => {
      const value = window.__STUDIO_UI_CANVAS_DIAGNOSTICS__;
      return value?.fixtureId === fixtureId &&
        value.runtime?.nodeCount === nodes && value.runtime?.connectionCount === connections;
    }, { fixtureId, nodes: descriptor.nodeCount, connections: descriptor.connectionCount });
  }
  await waitForDoubleAnimationFrame(page);
  const snapshot = await readCanvasSnapshot(page, runtime);
  assertFixtureSnapshot(snapshot, descriptor, `${runtime} ${kind}`);
  return snapshot;
}

async function readIdentity(page, runtime, kind) {
  const descriptor = fixtures[kind];
  if (runtime === 'legacy') {
    const raw = await page.evaluate(() => window.__CV_CANVAS_PERF_STATE__.identity);
    assert(raw?.before && raw?.after, 'Legacy identity round-trip did not return both snapshots.');
    const beforeFingerprint = createFlowIdentityFingerprint(raw.before);
    const afterFingerprint = createFlowIdentityFingerprint(raw.after);
    return {
      state: beforeFingerprint === afterFingerprint ? 'pass' : 'fail',
      beforeFingerprint,
      afterFingerprint,
      expectedFingerprint: descriptor.fingerprint
    };
  }
  return page.evaluate(() => ({ ...window.__STUDIO_UI_CANVAS_DIAGNOSTICS__.identity }));
}

async function runIdentity(page, runtime, kind) {
  const selector = runtime === 'legacy'
    ? '[data-cv-perf-action="identity"]'
    : '[data-canvas-action="identity-roundtrip"]';
  await page.locator(selector).click();
  await waitForDoubleAnimationFrame(page);
  const identity = await readIdentity(page, runtime, kind);
  const expected = fixtures[kind].fingerprint;
  assert(identity.state === 'pass' &&
    identity.beforeFingerprint === expected && identity.afterFingerprint === expected,
  `${runtime} ${kind} identity fingerprint drifted.`);
  return identity;
}

function findBlankPoint(snapshot) {
  const horizontal = [
    24,
    snapshot.logicalWidth - 24,
    snapshot.logicalWidth / 2,
    snapshot.logicalWidth * 0.25,
    snapshot.logicalWidth * 0.75
  ];
  const vertical = [
    snapshot.logicalHeight - 28,
    snapshot.logicalHeight * 0.8,
    snapshot.logicalHeight * 0.6,
    Math.max(96, snapshot.logicalHeight * 0.4)
  ];
  const candidates = vertical.flatMap(y => horizontal.map(x => ({ x, y })));
  const occupied = point => snapshot.nodes.some(node =>
    point.x >= node.x - 4 && point.x <= node.x + node.width + 4 &&
    point.y >= node.y - 4 && point.y <= node.y + node.height + 4);
  return candidates.find(point => point.x > 0 && point.y > 0 &&
    point.x < snapshot.logicalWidth && point.y < snapshot.logicalHeight && !occupied(point)) ||
    { x: 24, y: Math.max(24, snapshot.logicalHeight - 28) };
}

function findVisibleNode(snapshot) {
  const centerX = snapshot.logicalWidth / 2;
  const centerY = snapshot.logicalHeight / 2;
  return snapshot.nodes
    .filter(node => node.width > 0 && node.height > 0 &&
      node.x + node.width > 0 && node.y + node.height > 80 &&
      node.x < snapshot.logicalWidth && node.y < snapshot.logicalHeight)
    .sort((left, right) => {
      const leftDistance = Math.hypot(
        left.x + left.width / 2 - centerX,
        left.y + left.height / 2 - centerY
      );
      const rightDistance = Math.hypot(
        right.x + right.width / 2 - centerX,
        right.y + right.height / 2 - centerY
      );
      return leftDistance - rightDistance;
    })[0];
}

async function canvasTarget(page, runtime) {
  const selector = runtime === 'legacy' ? '#flow-canvas' : '[data-canvas-surface]';
  const box = await page.locator(selector).boundingBox();
  assert(box, `${runtime} Canvas has no browser bounding box.`);
  return { selector, box };
}

async function performPointerAction(page, runtime, action, direction) {
  const before = await readCanvasSnapshot(page, runtime);
  const { selector, box } = await canvasTarget(page, runtime);
  if (action === 'zoom') {
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    const probe = await armPaintProbe(page, selector, 'wheel');
    await page.mouse.wheel(0, -120 * direction);
    const latency = await finishPaintProbe(page, probe);
    const after = await readCanvasSnapshot(page, runtime);
    assert(after.scale !== before.scale, `${runtime} zoom did not change scale.`);
    return { latency, before, after };
  }

  const point = action === 'drag'
    ? (() => {
        const node = findVisibleNode(before);
        assert(node, `${runtime} exposed no visible node for drag.`);
        return {
          nodeId: node.id,
          x: Math.max(4, Math.min(before.logicalWidth - 4, node.x + node.width / 2)),
          y: Math.max(4, Math.min(before.logicalHeight - 4, node.y + node.height / 2))
        };
      })()
    : findBlankPoint(before);
  await page.mouse.move(box.x + point.x, box.y + point.y);
  await page.mouse.down();
  const probe = await armPaintProbe(page, selector, 'mousemove');
  const dx = (action === 'drag' ? 30 : 48) * direction;
  const dy = (action === 'drag' ? 20 : 32) * direction;
  await page.mouse.move(box.x + point.x + dx, box.y + point.y + dy, { steps: 8 });
  await page.mouse.up();
  const latency = await finishPaintProbe(page, probe);
  const after = await readCanvasSnapshot(page, runtime);
  if (action === 'pan') {
    assert(after.offsetX !== before.offsetX || after.offsetY !== before.offsetY,
      `${runtime} pan did not change offset.`);
  } else {
    const moved = after.nodes.find(node => node.id === point.nodeId);
    const prior = before.nodes.find(node => node.id === point.nodeId);
    assert(moved && prior && (moved.x !== prior.x || moved.y !== prior.y),
      `${runtime} drag did not move its node.`);
  }
  return { latency, before, after, ...(point.nodeId ? { nodeId: point.nodeId } : {}) };
}

async function performClickAction(page, runtime, action) {
  const before = await readCanvasSnapshot(page, runtime);
  const selector = action === 'stress-load'
    ? fixtureSelector(runtime, 'stress')
    : (runtime === 'legacy'
      ? '[data-cv-perf-action="identity"]'
      : '[data-canvas-action="identity-roundtrip"]');
  const probe = await armPaintProbe(page, selector, 'click');
  await page.locator(selector).click();
  const latency = await finishPaintProbe(page, probe);
  await waitForDoubleAnimationFrame(page);
  if (action === 'stress-identity') {
    const identity = await readIdentity(page, runtime, 'stress');
    assert(identity.state === 'pass' && identity.afterFingerprint === fixtures.stress.fingerprint,
      `${runtime} measured stress identity drifted.`);
  }
  const after = await readCanvasSnapshot(page, runtime);
  assertFixtureSnapshot(after, fixtures.stress, `${runtime} ${action}`);
  return { latency, before, after };
}

async function collectMemory(page, cdpSession) {
  const [metrics, heap, js] = await Promise.all([
    cdpSession.send('Performance.getMetrics'),
    cdpSession.send('Runtime.getHeapUsage'),
    page.evaluate(() => ({
      domNodeCount: document.getElementsByTagName('*').length,
      performanceMemory: performance.memory ? {
        jsHeapSizeLimit: performance.memory.jsHeapSizeLimit,
        totalJSHeapSize: performance.memory.totalJSHeapSize,
        usedJSHeapSize: performance.memory.usedJSHeapSize
      } : null
    }))
  ]);
  return {
    cdpHeap: heap,
    cdpPerformance: Object.fromEntries(metrics.metrics.map(item => [item.name, item.value])),
    js
  };
}

async function collectSample(page, runtime, action, iteration, cdpSession) {
  const direction = iteration % 2 === 0 ? 1 : -1;
  const memoryBefore = await collectMemory(page, cdpSession);
  await resetLongTasks(page);
  const operation = ['pan', 'zoom', 'drag'].includes(action)
    ? await performPointerAction(page, runtime, action, direction)
    : await performClickAction(page, runtime, action);
  const longTasks = await readLongTasks(page);
  const memoryAfter = await collectMemory(page, cdpSession);
  return {
    iteration,
    direction,
    inputToDoubleRafMs: operation.latency.inputToDoubleRafMs,
    handlerToDoubleRafMs: operation.latency.handlerToDoubleRafMs,
    latency: operation.latency,
    longTasks,
    longTaskCount: longTasks.length,
    longTaskTotalMs: longTasks.reduce((sum, item) => sum + item.duration, 0),
    longTaskMaxMs: longTasks.reduce((maximum, item) => Math.max(maximum, item.duration), 0),
    memoryBefore,
    memoryAfter,
    runtimeBefore: operation.before,
    runtimeAfter: operation.after,
    ...(operation.nodeId ? { nodeId: operation.nodeId } : {})
  };
}

async function runScenario(page, runtime, scenario, warmups, formal, cdpSession) {
  await reloadRuntime(page, runtime);
  const descriptor = fixtures[scenario.fixture];
  await loadFixture(page, runtime, scenario.fixture);
  const identity = await runIdentity(page, runtime, scenario.fixture);
  const warmupSamples = [];
  for (let index = 0; index < warmups; index += 1) {
    warmupSamples.push(await collectSample(page, runtime, scenario.action, index, cdpSession));
  }
  const formalSamples = [];
  for (let index = 0; index < formal; index += 1) {
    formalSamples.push(await collectSample(
      page,
      runtime,
      scenario.action,
      warmups + index,
      cdpSession
    ));
  }
  return {
    id: scenario.id,
    fixture: scenario.fixture,
    action: scenario.action,
    expectedFingerprint: descriptor.fingerprint,
    identity,
    warmupCount: warmupSamples.length,
    formalCount: formalSamples.length,
    warmupSamples,
    formalSamples
  };
}

function assertResourceStability(initial, final, runtime) {
  for (const field of [
    'structureListenerCount',
    'viewListenerCount',
    'selectionListenerCount',
    'interactionCleanupCount'
  ]) {
    assert(final.resources[field] === initial.resources[field],
      `${runtime} ${field} grew from ${initial.resources[field]} to ${final.resources[field]}.`);
  }
  assert(final.resources.resizeObserverActive === initial.resources.resizeObserverActive,
    `${runtime} ResizeObserver ownership changed.`);
  assert(final.resources.themeObserverActive === initial.resources.themeObserverActive,
    `${runtime} theme observer ownership changed.`);
}

async function disposeStudioCanvas(page) {
  await page.goto(`${new URL(page.url()).origin}/studio/index.html#/diagnostics`, {
    waitUntil: 'domcontentloaded'
  });
  await page.waitForFunction(() => window.__STUDIO_UI_READY__ === true);
  await page.waitForFunction(() => window.__STUDIO_UI_CANVAS_DIAGNOSTICS__?.ownerCount === 0);
  const disposed = await page.evaluate(() => ({
    canvas: { ...window.__STUDIO_UI_CANVAS_DIAGNOSTICS__ },
    studio: { ...window.__STUDIO_UI_DIAGNOSTICS__ }
  }));
  const resources = disposed.canvas.runtime?.resources;
  assert(disposed.canvas.status === 'disposed' && disposed.canvas.ownerCount === 0,
    'Studio Canvas did not report clean disposal.');
  assert(disposed.studio.canvasOwnerCount === 0, 'Studio retained a disposed Canvas owner.');
  assert(resources?.adapterDisposed && resources?.canvasDestroyed && resources?.interactionDisposed,
    'Studio Canvas imperative owners survived disposal.');
  assert(!resources?.resizeObserverActive && !resources?.themeObserverActive,
    'Studio Canvas observers survived disposal.');
  assert(resources?.structureListenerCount === 0 &&
    resources?.viewListenerCount === 0 && resources?.selectionListenerCount === 0,
  'Studio Canvas subscriptions survived disposal.');
  return disposed;
}

async function main() {
  const expectation = requiredEnvironment('CV_STUDIO_UI_EXPECTATION').toLowerCase();
  assert(expectation === 'legacy' || expectation === 'studio-canvas',
    'Canvas performance requires expectation legacy or studio-canvas.');
  const runtime = expectation === 'legacy' ? 'legacy' : 'studio';
  const cdpPort = Number(requiredEnvironment('CV_CDP_PORT'));
  const webPort = Number(requiredEnvironment('CV_WEB_PORT'));
  const scale = Number(requiredEnvironment('CV_DPI_SCALE'));
  const token = requiredEnvironment('CV_SMOKE_TOKEN');
  const user = requiredEnvironment('CV_SMOKE_USER');
  const executablePath = path.resolve(requiredEnvironment('CV_STUDIO_UI_DESKTOP_EXECUTABLE'));
  const evidenceDirectory = path.resolve(requiredEnvironment('CV_EVIDENCE_DIR'));
  const runName = String(process.env.CV_STUDIO_UI_RUN_NAME || runtime).trim();
  const groupId = String(process.env.CV_STUDIO_UI_PERF_GROUP || 'group-1').trim();
  const warmups = positiveIntegerEnvironment('CV_STUDIO_UI_PERF_WARMUPS', 2, 2);
  const formal = positiveIntegerEnvironment('CV_STUDIO_UI_PERF_SAMPLES', 5, 5);
  const scenarios = [
    { id: 'benchmark-100-pan', fixture: 'benchmark', action: 'pan' },
    { id: 'benchmark-100-zoom', fixture: 'benchmark', action: 'zoom' },
    { id: 'benchmark-100-drag', fixture: 'benchmark', action: 'drag' },
    { id: 'stress-300-load', fixture: 'stress', action: 'stress-load' },
    { id: 'stress-300-serialize', fixture: 'stress', action: 'stress-identity' }
  ];
  const evidence = {
    schemaVersion: 1,
    status: 'running',
    runtime,
    expectation,
    runName,
    comparisonGroup: groupId,
    scale,
    warmups,
    formalSamples: formal,
    fixtureAuthority: Object.fromEntries(Object.entries(fixtures).map(([key, value]) => [key, {
      flowId: value.flowId,
      flowName: value.flowName,
      nodeCount: value.nodeCount,
      connectionCount: value.connectionCount,
      fingerprint: value.fingerprint
    }])),
    capturedAtUtc: new Date().toISOString(),
    externalDriver: {
      processId: process.pid,
      parentProcessId: process.ppid,
      executablePath: process.execPath,
      role: 'external-cdp-driver',
      insideDesktopProcessTree: false
    },
    scenarios: []
  };
  const outputName = `studio-ui-canvas-performance-${safeFileName(groupId)}-${runtime}.json`;
  let browser;
  let cdpSession;
  let runtimeErrors;

  try {
    const connected = await connectToDesktopWebView2(cdpPort);
    browser = connected.browser;
    const { context, page, version } = connected;
    evidence.cdpVersion = version;
    await seedAuthenticatedSession(page, webPort, token, user);
    runtimeErrors = captureRuntimeErrors(page);
    await openRuntime(page, runtime, webPort);
    evidence.longTaskObserverSupported = await installMeasurementHelpers(page);
    cdpSession = await context.newCDPSession(page);
    await cdpSession.send('Performance.enable');

    await loadFixture(page, runtime, 'benchmark');
    evidence.fixtureAgreement = { benchmark: await runIdentity(page, runtime, 'benchmark') };
    await loadFixture(page, runtime, 'stress');
    evidence.fixtureAgreement.stress = await runIdentity(page, runtime, 'stress');

    await reloadRuntime(page, runtime);
    evidence.initialRuntime = await loadFixture(page, runtime, 'benchmark');
    for (const scenario of scenarios) {
      evidence.scenarios.push(await runScenario(
        page,
        runtime,
        scenario,
        warmups,
        formal,
        cdpSession
      ));
    }

    evidence.activeRuntime = await readCanvasSnapshot(page, runtime);
    assertResourceStability(evidence.initialRuntime, evidence.activeRuntime, runtime);
    evidence.browserDpi = await readBrowserDpiEvidence(page, context);
    evidence.nativeRuntime = runDesktopRuntimeProbe(executablePath);
    assert(evidence.nativeRuntime.awareness?.isPerMonitorV2,
      'Performance run did not use a PerMonitorV2 Desktop window.');
    assert(evidence.nativeRuntime.nodeDescendantCount === 0,
      'Performance run found Node inside the Desktop process tree.');

    const screenshot = path.join(
      evidenceDirectory,
      `studio-ui-canvas-performance-${safeFileName(groupId)}-${runtime}.png`
    );
    await page.screenshot({ path: screenshot });
    evidence.screenshot = screenshot;
    evidence.disposal = runtime === 'studio'
      ? await disposeStudioCanvas(page)
      : {
          status: 'document-owned',
          note: 'Legacy active resource counts were checked for growth before shared page/process shutdown.'
        };

    evidence.runtimeErrors = runtimeErrors;
    evidence.meaningfulRequestFailures = meaningfulRequestFailures(runtimeErrors.requestFailures);
    assert(runtimeErrors.consoleErrors.length === 0,
      `Canvas performance console errors: ${runtimeErrors.consoleErrors.join(' | ')}`);
    assert(runtimeErrors.pageErrors.length === 0,
      `Canvas performance page errors: ${runtimeErrors.pageErrors.join(' | ')}`);
    assert(evidence.meaningfulRequestFailures.length === 0,
      `Canvas performance request failures: ${JSON.stringify(evidence.meaningfulRequestFailures)}`);
    evidence.correctness = {
      passed: true,
      identityAgreement: true,
      resourceGrowth: false,
      runtimeErrors: false
    };
    evidence.status = 'pass';
    evidence.completedAtUtc = new Date().toISOString();
    const output = writeJsonEvidence(evidenceDirectory, outputName, evidence);
    process.stdout.write(`${JSON.stringify({ ok: true, output, runtime, groupId })}\n`);
  } catch (error) {
    evidence.status = 'fail';
    evidence.completedAtUtc = new Date().toISOString();
    evidence.error = error?.stack || error?.message || String(error);
    evidence.runtimeErrors = runtimeErrors || null;
    evidence.correctness = { passed: false, error: evidence.error };
    const output = writeJsonEvidence(evidenceDirectory, outputName, evidence);
    process.stderr.write(`${JSON.stringify({ ok: false, output, runtime, groupId })}\n`);
    throw error;
  } finally {
    try {
      await cdpSession?.detach();
    } finally {
      await browser?.close();
    }
  }
}

main().catch(error => {
  process.stderr.write(`${error?.stack || error}\n`);
  process.exitCode = 1;
});
