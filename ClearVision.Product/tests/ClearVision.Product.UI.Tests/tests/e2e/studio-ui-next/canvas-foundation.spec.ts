import { expect, Page, test } from '@playwright/test';

interface CanvasPortPoint {
  readonly id: string;
  readonly name: string;
  readonly dataType: string;
  readonly x: number;
  readonly y: number;
  readonly isOutput: boolean;
}

interface CanvasNodeGeometry {
  readonly id: string;
  readonly type: string;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  readonly inputs: readonly CanvasPortPoint[];
  readonly outputs: readonly CanvasPortPoint[];
}

interface CanvasResourceDiagnostics {
  readonly adapterDisposed: boolean;
  readonly canvasDestroyed: boolean;
  readonly interactionDisposed: boolean;
  readonly resizeObserverActive: boolean;
  readonly themeObserverActive: boolean;
  readonly drawFramePending: boolean;
  readonly resizeFramePending: boolean;
  readonly interactionFramePending: boolean;
  readonly contextMenuTimerActive: boolean;
  readonly structureListenerCount: number;
  readonly viewListenerCount: number;
  readonly selectionListenerCount: number;
  readonly interactionCleanupCount: number;
}

interface CanvasRuntimeDiagnostics {
  readonly nodeCount: number;
  readonly connectionCount: number;
  readonly multiSelectionCount: number;
  readonly scale: number;
  readonly offsetX: number;
  readonly offsetY: number;
  readonly logicalWidth: number;
  readonly logicalHeight: number;
  readonly backingWidth: number;
  readonly backingHeight: number;
  readonly dpr: number;
  readonly isConnecting: boolean;
  readonly nodes: readonly CanvasNodeGeometry[];
  readonly resources: CanvasResourceDiagnostics;
}

interface CanvasDiagnostics {
  readonly status: 'idle' | 'mounted' | 'disposed' | 'error';
  readonly ownerCount: 0 | 1;
  readonly generation: number;
  readonly totalMounts: number;
  readonly totalDisposals: number;
  readonly fixtureId: string | null;
  readonly identity: {
    readonly state: 'not-run' | 'pass' | 'fail';
    readonly beforeFingerprint: string | null;
    readonly afterFingerprint: string | null;
  };
  readonly validation: readonly {
    readonly id: string;
    readonly expected: string;
    readonly actual: string | null;
    readonly passed: boolean;
  }[];
  readonly runtime: CanvasRuntimeDiagnostics | null;
}

type CanvasWindow = Window & {
  readonly __STUDIO_UI_CANVAS_DIAGNOSTICS__?: CanvasDiagnostics;
  readonly __STUDIO_UI_DIAGNOSTICS__?: {
    readonly canvasOwnerCount: number;
  };
};

async function installStudioStartup(page: Page): Promise<string[]> {
  const runtimeErrors: string[] = [];
  page.on('pageerror', error => runtimeErrors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') {
      runtimeErrors.push(message.text());
    }
  });

  await page.route('**/health', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers: { 'x-clearvision-data-source': 'BROWSER_FIXTURE' },
    body: JSON.stringify({ status: 'Healthy', port: 5177 })
  }));
  await page.route('**/api/auth/me', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers: { 'x-clearvision-data-source': 'BROWSER_FIXTURE' },
    body: JSON.stringify({ userId: 'canvas-lab-user', username: 'canvas-lab', role: 'Engineer' })
  }));
  await page.route('**/api/auth/setup-status', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers: { 'x-clearvision-data-source': 'BROWSER_FIXTURE' },
    body: JSON.stringify({ requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false })
  }));

  await page.addInitScript(() => {
    sessionStorage.setItem('cv_auth_token', 'canvas-lab-browser-fixture-token');
    sessionStorage.setItem('cv_current_user', 'canvas-lab-user');
    const startup = Object.freeze({
      schemaVersion: 1,
      uiKind: 'studio-ui',
      hostKind: 'browser-test',
      apiBaseUrl: `${window.location.origin}/api`,
      studioUiBasePath: '/studio/',
      featureFlags: Object.freeze({})
    });
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: startup,
      writable: false,
      configurable: false
    });
  });
  return runtimeErrors;
}

async function bootCanvasLab(page: Page): Promise<string[]> {
  const runtimeErrors = await installStudioStartup(page);
  await page.goto('/studio/index.html#/labs/canvas');
  await expect(page.locator('[data-canvas-lab="ready"]')).toBeVisible();
  await expect(page.locator('#studio-ui-canonical-flow-canvas')).toBeVisible();
  return runtimeErrors;
}

async function readDiagnostics(page: Page): Promise<CanvasDiagnostics> {
  return page.evaluate(() => {
    const diagnostics = (window as CanvasWindow).__STUDIO_UI_CANVAS_DIAGNOSTICS__;
    if (!diagnostics) {
      throw new Error('Canvas diagnostics projection is unavailable.');
    }
    return diagnostics;
  });
}

async function readLifecycleCanvasOwnerCount(page: Page): Promise<number | null> {
  return page.evaluate(() =>
    (window as CanvasWindow).__STUDIO_UI_DIAGNOSTICS__?.canvasOwnerCount ?? null);
}

async function waitForFixture(
  page: Page,
  fixtureId: string,
  nodeCount: number,
  connectionCount: number
): Promise<void> {
  await expect.poll(async () => {
    const diagnostics = await readDiagnostics(page);
    return {
      status: diagnostics.status,
      ownerCount: diagnostics.ownerCount,
      fixtureId: diagnostics.fixtureId,
      nodeCount: diagnostics.runtime?.nodeCount,
      connectionCount: diagnostics.runtime?.connectionCount
    };
  }).toEqual({
    status: 'mounted',
    ownerCount: 1,
    fixtureId,
    nodeCount,
    connectionCount
  });
}

async function loadFixture(
  page: Page,
  action: string,
  fixtureId: string,
  nodeCount: number,
  connectionCount: number
): Promise<void> {
  await page.locator(`[data-canvas-action="${action}"]`).click();
  await waitForFixture(page, fixtureId, nodeCount, connectionCount);
}

function requireNode(diagnostics: CanvasDiagnostics, type: string): CanvasNodeGeometry {
  const node = diagnostics.runtime?.nodes.find(item => item.type === type);
  if (!node) {
    throw new Error(`Canvas node was not found: ${type}`);
  }
  return node;
}

async function canvasBox(page: Page): Promise<{ x: number; y: number; width: number; height: number }> {
  const box = await page.locator('#studio-ui-canonical-flow-canvas').boundingBox();
  if (!box) {
    throw new Error('Canonical Canvas has no browser bounding box.');
  }
  return box;
}

async function dragCanvasPoints(
  page: Page,
  from: { x: number; y: number },
  to: { x: number; y: number }
): Promise<void> {
  const box = await canvasBox(page);
  await page.mouse.move(box.x + from.x, box.y + from.y);
  await page.mouse.down();
  await page.mouse.move(box.x + to.x, box.y + to.y, { steps: 8 });
  await page.mouse.up();
}

test('canonical Canvas exposes the rejection matrix and preserves identity', async ({ page }) => {
  const runtimeErrors = await bootCanvasLab(page);
  const mounted = await readDiagnostics(page);

  expect(mounted.ownerCount).toBe(1);
  expect(await readLifecycleCanvasOwnerCount(page)).toBe(1);
  expect(mounted.fixtureId).toBe('canonical');
  expect(mounted.validation.map(item => ({
    id: item.id,
    result: item.actual,
    passed: item.passed
  }))).toEqual([
    { id: 'duplicate', result: 'duplicate-connection', passed: true },
    { id: 'occupied', result: 'input-port-occupied', passed: true },
    { id: 'self', result: 'self-connection', passed: true },
    { id: 'incompatible', result: 'incompatible-port-type', passed: true },
    { id: 'cycle', result: 'cycle', passed: true }
  ]);

  await page.locator('[data-canvas-action="identity-roundtrip"]').click();
  await expect.poll(async () => (await readDiagnostics(page)).identity.state).toBe('pass');
  const identity = (await readDiagnostics(page)).identity;
  expect(identity.beforeFingerprint).toMatch(/^[0-9a-f]{8}$/);
  expect(identity.afterFingerprint).toBe(identity.beforeFingerprint);
  expect(runtimeErrors).toEqual([]);
});

test('pointer gestures create legal connections and reject incompatible ports', async ({ page }) => {
  const runtimeErrors = await bootCanvasLab(page);
  await loadFixture(page, 'load-interaction', 'interaction', 5, 0);

  let diagnostics = await readDiagnostics(page);
  const acquisition = requireNode(diagnostics, 'ImageAcquisition');
  const threshold = requireNode(diagnostics, 'Thresholding');
  const acquisitionOutput = acquisition.outputs[0];
  const thresholdInput = threshold.inputs[0];
  expect(acquisitionOutput).toBeDefined();
  expect(thresholdInput).toBeDefined();

  await dragCanvasPoints(page, acquisitionOutput!, thresholdInput!);
  await expect.poll(async () => (await readDiagnostics(page)).runtime?.connectionCount).toBe(1);

  diagnostics = await readDiagnostics(page);
  const thresholdOutput = requireNode(diagnostics, 'Thresholding').outputs[0];
  const statisticsInput = requireNode(diagnostics, 'Statistics').inputs[0];
  expect(thresholdOutput).toBeDefined();
  expect(statisticsInput).toBeDefined();
  await dragCanvasPoints(page, thresholdOutput!, statisticsInput!);

  await expect.poll(async () => {
    const current = await readDiagnostics(page);
    return {
      connectionCount: current.runtime?.connectionCount,
      isConnecting: current.runtime?.isConnecting
    };
  }).toEqual({ connectionCount: 1, isConnecting: false });

  await page.locator('[data-canvas-action="identity-roundtrip"]').click();
  await expect.poll(async () => (await readDiagnostics(page)).identity.state).toBe('pass');
  expect((await readDiagnostics(page)).runtime?.connectionCount).toBe(1);
  expect(runtimeErrors).toEqual([]);
});

test('drag, selection, pan, zoom and browser resize stay in canonical logical coordinates', async ({ page }) => {
  const runtimeErrors = await bootCanvasLab(page);
  await loadFixture(page, 'load-interaction', 'interaction', 5, 0);

  const beforeDrag = await readDiagnostics(page);
  const acquisitionBefore = requireNode(beforeDrag, 'ImageAcquisition');
  await dragCanvasPoints(
    page,
    { x: acquisitionBefore.x + acquisitionBefore.width / 2, y: acquisitionBefore.y + 24 },
    { x: acquisitionBefore.x + acquisitionBefore.width / 2 + 50, y: acquisitionBefore.y + 54 }
  );
  await expect.poll(async () => requireNode(await readDiagnostics(page), 'ImageAcquisition').x)
    .not.toBe(acquisitionBefore.x);

  const box = await canvasBox(page);
  await page.keyboard.down('Shift');
  await page.mouse.move(box.x + 20, box.y + 40);
  await page.mouse.down();
  await page.mouse.move(box.x + 780, box.y + 330, { steps: 8 });
  await page.mouse.up();
  await page.keyboard.up('Shift');
  await expect.poll(async () => (await readDiagnostics(page)).runtime?.multiSelectionCount ?? 0)
    .toBeGreaterThanOrEqual(3);

  const beforePan = await readDiagnostics(page);
  await dragCanvasPoints(
    page,
    { x: 24, y: Math.max(360, box.height - 28) },
    { x: 94, y: Math.max(400, box.height + 12) }
  );
  await expect.poll(async () => (await readDiagnostics(page)).runtime?.offsetX)
    .not.toBe(beforePan.runtime?.offsetX);

  const beforeZoom = await readDiagnostics(page);
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.wheel(0, -120);
  await expect.poll(async () => (await readDiagnostics(page)).runtime?.scale)
    .not.toBe(beforeZoom.runtime?.scale);

  const beforeResize = await readDiagnostics(page);
  await page.setViewportSize({ width: 1500, height: 900 });
  await expect.poll(async () => {
    const current = await readDiagnostics(page);
    return `${current.runtime?.logicalWidth}x${current.runtime?.logicalHeight}`;
  }).not.toBe(`${beforeResize.runtime?.logicalWidth}x${beforeResize.runtime?.logicalHeight}`);

  const after = await readDiagnostics(page);
  expect(after.ownerCount).toBe(1);
  expect(after.runtime?.logicalWidth).toBeGreaterThan(0);
  expect(after.runtime?.logicalHeight).toBeGreaterThan(0);
  expect(runtimeErrors).toEqual([]);
});

test('twenty route mount/unmount cycles retain one owner and release every resource', async ({ page }) => {
  const runtimeErrors = await installStudioStartup(page);
  await page.goto('/studio/index.html#/labs/design');
  await expect(page.locator('[data-design-lab="ready"]')).toBeVisible();
  const baseline = await readDiagnostics(page);

  for (let cycle = 0; cycle < 20; cycle += 1) {
    await page.evaluate(() => {
      window.location.hash = '#/labs/canvas';
    });
    await expect(page.locator('[data-canvas-lab="ready"]')).toBeVisible();
    const mounted = await readDiagnostics(page);
    expect(mounted.ownerCount, `cycle ${cycle + 1} mounted owner`).toBe(1);
    expect(await readLifecycleCanvasOwnerCount(page), `cycle ${cycle + 1} lifecycle owner`)
      .toBe(1);

    await page.evaluate(() => {
      window.location.hash = '#/labs/design';
    });
    await expect(page.locator('[data-design-lab="ready"]')).toBeVisible();
    const disposed = await readDiagnostics(page);
    expect(disposed.ownerCount, `cycle ${cycle + 1} disposed owner`).toBe(0);
    expect(await readLifecycleCanvasOwnerCount(page), `cycle ${cycle + 1} lifecycle disposed`)
      .toBe(0);
    expect(disposed.status).toBe('disposed');
    expect(disposed.runtime?.resources).toMatchObject({
      adapterDisposed: true,
      canvasDestroyed: true,
      interactionDisposed: true,
      resizeObserverActive: false,
      themeObserverActive: false,
      drawFramePending: false,
      resizeFramePending: false,
      interactionFramePending: false,
      contextMenuTimerActive: false,
      structureListenerCount: 0,
      viewListenerCount: 0,
      selectionListenerCount: 0,
      interactionCleanupCount: 0
    });
  }

  const finalDiagnostics = await readDiagnostics(page);
  expect(finalDiagnostics.totalMounts - baseline.totalMounts).toBe(20);
  expect(finalDiagnostics.totalDisposals - baseline.totalDisposals).toBe(20);
  expect(runtimeErrors).toEqual([]);
});

test('DPR and viewport matrix keeps backing stores and hit-test geometry aligned', async ({ browser }) => {
  const viewports = [
    { width: 1366, height: 768 },
    { width: 1600, height: 1000 },
    { width: 1920, height: 1080 }
  ] as const;
  const deviceScaleFactors = [1, 1.25, 1.5, 2] as const;

  for (const viewport of viewports) {
    for (const deviceScaleFactor of deviceScaleFactors) {
      const context = await browser.newContext({ viewport, deviceScaleFactor });
      const page = await context.newPage();
      const runtimeErrors = await bootCanvasLab(page);
      const diagnostics = await readDiagnostics(page);
      const runtime = diagnostics.runtime;
      expect(runtime).not.toBeNull();
      if (!runtime) {
        await context.close();
        continue;
      }

      expect(runtime.dpr).toBe(deviceScaleFactor);
      expect(runtime.logicalWidth).toBeGreaterThan(0);
      expect(runtime.logicalHeight).toBeGreaterThan(0);
      expect(runtime.backingWidth).toBe(Math.round(runtime.logicalWidth * deviceScaleFactor));
      expect(runtime.backingHeight).toBe(Math.round(runtime.logicalHeight * deviceScaleFactor));
      expect(runtime.resources.resizeObserverActive).toBe(true);
      expect(runtime.resources.themeObserverActive).toBe(true);
      expect(runtime.resources.structureListenerCount).toBe(1);
      expect(runtime.resources.viewListenerCount).toBe(1);
      expect(runtime.resources.selectionListenerCount).toBe(1);

      const acquisition = requireNode(diagnostics, 'ImageAcquisition');
      const output = acquisition.outputs[0];
      expect(output).toBeDefined();
      expect(output?.x).toBeCloseTo(acquisition.x + acquisition.width, 5);
      expect(output?.y).toBeGreaterThan(acquisition.y);
      expect(output?.y).toBeLessThan(acquisition.y + acquisition.height);
      expect(runtimeErrors, `${viewport.width}x${viewport.height}@${deviceScaleFactor}`).toEqual([]);
      await context.close();
    }
  }
});
