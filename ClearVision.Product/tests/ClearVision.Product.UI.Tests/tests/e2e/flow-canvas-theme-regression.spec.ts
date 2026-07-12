import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { expect, Page, test } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const evidenceDirectory = path.resolve(
  process.env.FLOW_CANVAS_THEME_EVIDENCE_DIR
    || path.join(process.cwd(), '../../../artifacts/quiet-precision/flow-canvas-theme/final'),
);

const viewports = [
  { width: 854, height: 695 },
  { width: 1024, height: 768 },
  { width: 1366, height: 768 },
  { width: 1920, height: 1080 },
];

function parseRgb(value: string) {
  const match = value.match(/rgba?\(([^)]+)\)/);
  if (!match) throw new Error(`Expected an RGB color, received: ${value}`);
  return match[1].split(',').slice(0, 3).map(part => Number.parseFloat(part.trim()));
}

function maxChannel(value: string) {
  return Math.max(...parseRgb(value));
}

function rgbDistance(left: number[], right: number[]) {
  return Math.sqrt(left.slice(0, 3).reduce((sum, channel, index) => (
    sum + ((channel - right[index]) ** 2)
  ), 0));
}

async function mockThemeSettings(page: Page, initialTheme: 'light' | 'dark') {
  let theme = initialTheme;
  const payload = () => ({
    general: { softwareTitle: 'ClearVision', theme, autoStart: false },
    runtime: {
      autoRun: false,
      stopOnConsecutiveNg: 2,
      missingMaterialTimeoutSeconds: 15,
      applyProtectionRules: true,
    },
  });

  await page.route('**/api/settings/theme', async route => {
    const nextTheme = String(route.request().postDataJSON()?.theme || theme).toLowerCase();
    theme = nextTheme === 'dark' ? 'dark' : 'light';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ theme }),
    });
  });

  await page.route('**/api/settings', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(payload()),
    });
  });
}

async function bootFlow(page: Page, viewport: { width: number; height: number }) {
  await page.setViewportSize(viewport);
  await mockThemeSettings(page, 'light');
  await page.addInitScript(() => localStorage.setItem('cv_theme', 'light'));
  await bootAuthenticatedApp(page);
  await expect.poll(() => page.evaluate(() => Boolean((window as any).flowCanvas))).toBe(true);
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await mkdir(evidenceDirectory, { recursive: true });
}

async function waitForThemePalette(page: Page, theme: 'light' | 'dark') {
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
  await expect.poll(() => page.evaluate(() => {
    const root = getComputedStyle(document.documentElement);
    const flowCanvas = (window as any).flowCanvas;
    return flowCanvas?.themePalette?.grid === root.getPropertyValue('--flow-canvas-grid').trim();
  })).toBe(true);
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
}

async function toggleThemeWithKeyboard(page: Page) {
  const toggle = page.locator('#btn-theme-toggle');
  await toggle.focus();
  await expect(toggle).toBeFocused();
  await toggle.press('Enter');
}

async function seedFlow(page: Page) {
  await page.evaluate(() => {
    const flow = (window as any).flowCanvas;
    flow.clear();
    const availableWidth = Math.max(320, flow._logicalWidth || 640);
    const targetX = Math.max(190, Math.min(390, availableWidth - 175));

    const source = flow.addNode('ImageAcquisition', 34, 76, {
      title: 'Image Acquisition',
      color: '#4eac94',
      inputs: [],
      outputs: [{ id: 'theme-source-image', name: 'Image', type: 'Image' }],
      parameters: [],
    });
    const target = flow.addNode('Threshold', targetX, 138, {
      title: 'Threshold',
      color: '#47738f',
      inputs: [{ id: 'theme-target-image', name: 'Image', type: 'Image' }],
      outputs: [{ id: 'theme-target-binary', name: 'Binary', type: 'Image' }],
      parameters: [],
    });
    const disabled = flow.addNode('GaussianBlur', 68, 262, {
      title: 'Disabled Filter',
      color: '#4eac94',
      inputs: [{ id: 'theme-disabled-image', name: 'Image', type: 'Image' }],
      outputs: [{ id: 'theme-disabled-output', name: 'Image', type: 'Image' }],
      parameters: [],
    });
    disabled.disabled = true;
    target.status = 'error';

    const connection = flow.addConnection(source.id, 0, target.id, 0);
    flow.selectedNode = target.id;
    flow.selectedConnection = null;
    flow.markSelectionChanged?.('theme-regression-selected-node');
    flow.render();
    (window as any).__flowThemeFixture = {
      sourceId: source.id,
      targetId: target.id,
      disabledId: disabled.id,
      connectionId: connection?.id || null,
    };
  });
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
}

async function readPresentation(page: Page) {
  return page.evaluate(() => {
    const read = (selector: string) => {
      const node = document.querySelector(selector);
      if (!node) throw new Error(`Missing theme regression selector: ${selector}`);
      const styles = getComputedStyle(node);
      return {
        background: styles.backgroundColor,
        border: styles.borderColor,
      };
    };
    const flow = (window as any).flowCanvas;
    const fixture = (window as any).__flowThemeFixture;
    const sampleNode = (nodeId: string, sampleY = 48, sampleXRatio = 0.5) => {
      const node = flow.nodes.get(nodeId);
      const dpr = window.devicePixelRatio || 1;
      const x = (node.x - flow.offset.x) * flow.scale + node.width * flow.scale * sampleXRatio;
      const y = (node.y - flow.offset.y) * flow.scale + Math.min(node.height - 8, sampleY) * flow.scale;
      return Array.from(flow.ctx.getImageData(Math.round(x * dpr), Math.round(y * dpr), 1, 1).data);
    };
    const samplePort = () => {
      const position = flow.getPortPosition(fixture.sourceId, 0, true);
      const dpr = window.devicePixelRatio || 1;
      return Array.from(flow.ctx.getImageData(
        Math.round(position.x * dpr),
        Math.round(position.y * dpr),
        1,
        1,
      ).data);
    };
    const html = document.documentElement;
    const body = document.body;
    const shell = document.querySelector('.flow-editor-shell');
    const canvas = read('#flow-canvas');
    const workspace = read('.flow-editor-shell .workspace');
    const minimap = read('.flow-minimap');
    const imageViewer = read('.image-viewer-container');

    return {
      theme: html.dataset.theme,
      canvas,
      workspace,
      minimap,
      imageViewer,
      palette: { ...flow.themePalette },
      nodePixel: sampleNode(fixture.sourceId),
      disabledNodePixel: sampleNode(fixture.disabledId),
      nodeHeaderPixel: sampleNode(fixture.sourceId, 20, 0.82),
      disabledNodeHeaderPixel: sampleNode(fixture.disabledId, 20, 0.82),
      portPixel: samplePort(),
      overflow: {
        document: html.scrollWidth - html.clientWidth,
        body: body.scrollWidth - body.clientWidth,
        shell: shell ? shell.scrollWidth - shell.clientWidth : null,
        offenders: Array.from(document.querySelectorAll('body *'))
          .map(node => ({
            selector: `${node.tagName.toLowerCase()}${node.id ? `#${node.id}` : ''}${node.classList.length ? `.${Array.from(node.classList).join('.')}` : ''}`,
            right: Math.round(node.getBoundingClientRect().right),
            width: Math.round(node.getBoundingClientRect().width),
          }))
          .filter(item => item.right > window.innerWidth + 1)
          .sort((left, right) => right.right - left.right)
          .slice(0, 8),
      },
    };
  });
}

async function captureFlow(page: Page, name: string) {
  await page.locator('.flow-editor-shell').screenshot({
    path: path.join(evidenceDirectory, name),
  });
}

for (const viewport of viewports) {
  test(`FlowCanvas follows light and dark themes at ${viewport.width}px`, async ({ page }) => {
    await bootFlow(page, viewport);

    if (viewport.width === 1024) {
      await captureFlow(page, '1024-light-empty.png');
    }

    await seedFlow(page);
    await captureFlow(page, `${viewport.width}-light-nodes.png`);
    const light = await readPresentation(page);

    expect(maxChannel(light.canvas.background)).toBeGreaterThan(180);
    expect(maxChannel(light.canvas.background)).toBeLessThan(250);
    expect(light.workspace.background).toBe(light.canvas.background);
    expect(maxChannel(light.imageViewer.background)).toBeLessThan(50);
    expect(rgbDistance(light.disabledNodeHeaderPixel, parseRgb(light.canvas.background)))
      .toBeLessThan(rgbDistance(light.nodeHeaderPixel, parseRgb(light.canvas.background)));
    expect(light.overflow.document, JSON.stringify(light.overflow.offenders)).toBeLessThanOrEqual(1);
    expect(light.overflow.body).toBeLessThanOrEqual(1);
    expect(light.overflow.shell, JSON.stringify(light.overflow.offenders)).toBeLessThanOrEqual(1);

    if (viewport.width === 1366) {
      const canvas = page.locator('#flow-canvas');
      const box = await canvas.boundingBox();
      if (!box) throw new Error('Flow canvas has no bounding box.');

      const outputPort = await page.evaluate(() => {
        const flow = (window as any).flowCanvas;
        const fixture = (window as any).__flowThemeFixture;
        const canvasRect = flow.canvas.getBoundingClientRect();
        const position = flow.getPortPosition(fixture.sourceId, 0, true);
        return { x: canvasRect.left + position.x, y: canvasRect.top + position.y };
      });
      await page.mouse.move(outputPort.x, outputPort.y);
      await expect.poll(() => page.evaluate(() => Boolean((window as any).flowCanvas.hoveredPort))).toBe(true);
      await captureFlow(page, '1366-light-port-hover.png');

      await page.keyboard.down('Shift');
      await page.mouse.move(box.x + 18, box.y + 24);
      await page.mouse.down();
      await page.mouse.move(box.x + Math.min(box.width - 18, 520), box.y + Math.min(box.height - 24, 360), { steps: 8 });
      await expect(page.locator('.flow-selection-box')).toBeVisible();
      const selection = await page.locator('.flow-selection-box').evaluate(node => {
        const styles = getComputedStyle(node);
        return { background: styles.backgroundColor, border: styles.borderColor };
      });
      expect(selection.background).not.toBe('rgba(0, 0, 0, 0)');
      expect(selection.border).not.toBe(light.canvas.background);
      await captureFlow(page, '1366-light-selection.png');
      await page.mouse.up();
      await page.keyboard.up('Shift');

      await page.evaluate(() => {
        const flow = (window as any).flowCanvas;
        const fixture = (window as any).__flowThemeFixture;
        flow.selectedNode = null;
        flow.selectedConnection = flow.connections.find((item: any) => item.id === fixture.connectionId) || null;
        flow.markSelectionChanged?.('theme-regression-selected-connection');
        flow.render();
      });
      await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => resolve())));
      await captureFlow(page, '1366-light-connection-selected.png');
    }

    await toggleThemeWithKeyboard(page);
    await waitForThemePalette(page, 'dark');
    await captureFlow(page, `${viewport.width}-dark-nodes.png`);
    const dark = await readPresentation(page);

    expect(maxChannel(dark.canvas.background)).toBeLessThan(60);
    expect(dark.canvas.background).not.toBe(light.canvas.background);
    expect(dark.workspace.background).toBe(dark.canvas.background);
    expect(dark.palette.grid).not.toBe(light.palette.grid);
    expect(dark.palette.nodeBackgroundStart).not.toBe(light.palette.nodeBackgroundStart);
    expect(dark.palette.connection).not.toBe(light.palette.connection);
    expect(dark.palette.connectionSelected).not.toBe(dark.palette.connection);
    expect(dark.nodePixel).not.toEqual(light.nodePixel);
    expect(dark.portPixel).toEqual(light.portPixel);
    expect(rgbDistance(dark.disabledNodeHeaderPixel, parseRgb(dark.canvas.background)))
      .toBeLessThan(rgbDistance(dark.nodeHeaderPixel, parseRgb(dark.canvas.background)));
    expect(maxChannel(dark.imageViewer.background)).toBeLessThan(50);
    expect(dark.overflow.document, JSON.stringify(dark.overflow.offenders)).toBeLessThanOrEqual(1);
    expect(dark.overflow.body).toBeLessThanOrEqual(1);
    expect(dark.overflow.shell, JSON.stringify(dark.overflow.offenders)).toBeLessThanOrEqual(1);

    if (viewport.width === 1366) {
      await page.evaluate(() => {
        const flow = (window as any).flowCanvas;
        const fixture = (window as any).__flowThemeFixture;
        flow.selectedConnection = null;
        flow.selectedNode = fixture.targetId;
        flow.markSelectionChanged?.('theme-regression-dark-selected-node');
        flow.render();
      });
      await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => resolve())));
      await captureFlow(page, '1366-dark-node-selected.png');
    }

    await toggleThemeWithKeyboard(page);
    await waitForThemePalette(page, 'light');
    const restored = await readPresentation(page);
    expect(restored.canvas.background).toBe(light.canvas.background);
    expect(restored.palette.grid).toBe(light.palette.grid);
  });
}
