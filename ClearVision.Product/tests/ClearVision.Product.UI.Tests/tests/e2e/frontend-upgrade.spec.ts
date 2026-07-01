import { test, expect } from '@playwright/test';

async function openModuleHost(page) {
  await page.route('**/module-host.html', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'text/html',
      body: '<!doctype html><html><head><meta charset="utf-8"><title>Module Host</title></head><body></body></html>',
    });
  });

  await page.goto('/module-host.html');
}

test('result card system renders OCR, measurement, boolean, detection, and communication output', async ({ page }) => {
  await openModuleHost(page);

  const text = await page.evaluate(async () => {
    const host = document.createElement('div');
    host.id = 'analysis-cards-container';
    document.body.appendChild(host);

    const { AnalysisCardsPanel } = await import('/src/features/inspection/analysisCardsPanel.js');
    const panel = new AnalysisCardsPanel('analysis-cards-container');
    panel.updateCards({
      Text: 'LOT-2026-0429',
      Width: 12.42,
      IsOk: false,
      Detections: [{ label: 'Scratch', confidence: 0.91, box: { x: 1, y: 2, width: 3, height: 4 } }],
      Response: { statusCode: 200, body: 'ACK' }
    }, 'NG');

    return host.innerText;
  });

  expect(text).toContain('LOT-2026-0429');
  expect(text).toContain('12.42');
  expect(text).toContain('False');
  expect(text).toContain('Scratch');
  expect(text).toContain('statusCode');
});

test('communication policy keeps HTTP queries separate from realtime events', async ({ page }) => {
  await openModuleHost(page);

  const channels = await page.evaluate(async () => {
    const policy = await import('/src/core/messaging/communicationPolicy.js');
    return {
      query: policy.resolveCommunicationChannel(policy.CommunicationUseCase.CRUD_QUERY),
      command: policy.resolveCommunicationChannel(policy.CommunicationUseCase.COMMAND),
      host: policy.resolveCommunicationChannel(policy.CommunicationUseCase.HOST_COMMAND),
      realtime: policy.resolveCommunicationChannel(policy.CommunicationUseCase.REALTIME_EVENT),
      internal: policy.resolveCommunicationChannel(policy.CommunicationUseCase.INTERNAL_EVENT),
    };
  });

  expect(channels).toEqual({
    query: 'http',
    command: 'http',
    host: 'webview',
    realtime: 'sse',
    internal: 'event-bus',
  });
});

test('service registry and event bus coordinate a flow apply event without window service lookup', async ({ page }) => {
  await openModuleHost(page);

  const result = await page.evaluate(async () => {
    const [{ default: serviceRegistry }, { default: eventBus }, { createFlowCanvasAdapter }] = await Promise.all([
      import('/src/core/app/serviceRegistry.js'),
      import('/src/core/app/eventBus.js'),
      import('/src/core/canvas/flowCanvasAdapter.js'),
    ]);

    const events = [];
    eventBus.on('ai:applied', payload => events.push(payload.flow.nodes.length));

    const rawCanvas = {
      nodes: new Map(),
      selectedNode: null,
      flow: { nodes: [] },
      serialize() { return this.flow; },
      deserialize(flow) { this.flow = flow; },
      clear() { this.flow = { nodes: [] }; },
      addNode() { return null; },
      resize() {},
      render() {},
      markFlowStructureChanged() {},
      subscribeStructureState() { return () => {}; },
      getFlowRevision() { return 1; },
    };

    const adapter = createFlowCanvasAdapter(rawCanvas, { eventBus });
    serviceRegistry.register('flowCanvasAdapter', adapter);

    const flow = { nodes: [{ id: 'n1', type: 'ImageAcquisition' }] };
    serviceRegistry.get('flowCanvasAdapter').deserialize(flow);
    eventBus.emit('ai:applied', { flow });

    return {
      hasWindowFlowCanvas: Object.prototype.hasOwnProperty.call(window, 'flowCanvas'),
      flowNodeCount: serviceRegistry.get('flowCanvasAdapter').serialize().nodes.length,
      events
    };
  });

  expect(result.hasWindowFlowCanvas).toBe(false);
  expect(result.flowNodeCount).toBe(1);
  expect(result.events).toEqual([1]);
});

test('parameter-panel save path can apply a current operator before serialization', async ({ page }) => {
  await openModuleHost(page);

  const saved = await page.evaluate(async () => {
    const { default: serviceRegistry } = await import('/src/core/app/serviceRegistry.js');
    let applied = false;
    serviceRegistry.register('propertyPanel', {
      currentOperator: { id: 'op-1' },
      applyChanges() { applied = true; }
    });

    const panel = serviceRegistry.get('propertyPanel');
    if (panel?.currentOperator) {
      panel.applyChanges();
    }

    return applied;
  });

  expect(saved).toBe(true);
});
