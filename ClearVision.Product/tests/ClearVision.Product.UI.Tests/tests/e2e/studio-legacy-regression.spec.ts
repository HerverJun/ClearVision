import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, Page, test } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const screenshotDir = resolve(process.cwd(), 'test-results', 'studio-legacy-regression');
const flowOnlySelectors = [
  '#operator-rail',
  '#operator-group-flyout',
  '.inspector-pane',
  '.preview-workbench-pane',
  '[data-sidebar-resizer="property"]',
];

const operators = [
  {
    type: 'ImageAcquisition',
    displayName: '图像采集',
    category: '输入',
    description: '从相机或文件读取图像',
    parameters: [],
    inputPorts: [],
    outputPorts: [{ name: 'Image', dataType: 'Image' }],
  },
  {
    type: 'Thresholding',
    displayName: '阈值分割',
    category: '预处理',
    description: '按阈值生成二值图',
    parameters: [{ name: 'Threshold', displayName: '阈值', dataType: 'int', value: 128 }],
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Mask', dataType: 'Image' }],
  },
];

function screenshotPath(name: string) {
  mkdirSync(screenshotDir, { recursive: true });
  return resolve(screenshotDir, name);
}

async function installFailClosedStartupFlags(page: Page) {
  await page.addInitScript(() => {
    const startup = Object.freeze({
      featureFlags: Object.freeze({
        'Studio2.PropertyPanel': true,
        'Studio2.PreviewPanel': true,
        'Studio2.Settings': true,
        'Studio2.AiPanel': true,
      }),
    });

    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: startup,
      writable: false,
      configurable: false,
    });
  });
}

async function installApiRoutes(page: Page) {
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const pathname = url.pathname;

    const fulfillJson = (body: unknown, status = 200) => route.fulfill({
      status,
      contentType: 'application/json',
      body: JSON.stringify(body),
    });

    if (pathname.endsWith('/auth/me')) {
      await fulfillJson({ username: 'admin', displayName: 'E2E Admin', role: 'Admin' });
      return;
    }

    if (pathname.endsWith('/health')) {
      await fulfillJson({ status: 'ok' });
      return;
    }

    if (pathname.endsWith('/operators/library')) {
      await fulfillJson(operators);
      return;
    }

    if (pathname.endsWith('/operators/types')) {
      await fulfillJson(operators.map(operator => operator.type));
      return;
    }

    if (/\/api\/operators\/[^/]+\/metadata$/.test(pathname)) {
      const type = decodeURIComponent(pathname.split('/').at(-2) ?? '');
      await fulfillJson(operators.find(operator => operator.type === type) ?? operators[0]);
      return;
    }

    if (pathname.endsWith('/settings')) {
      await fulfillJson({});
      return;
    }

    if (pathname.endsWith('/settings/disk-usage')) {
      await fulfillJson({
        canWrite: true,
        totalBytes: 512 * 1024 * 1024 * 1024,
        availableBytes: 384 * 1024 * 1024 * 1024,
      });
      return;
    }

    if (pathname.endsWith('/settings/database/status')) {
      await fulfillJson({ status: 'ok', migrations: [], tables: [] });
      return;
    }

    if (pathname.endsWith('/users')) {
      await fulfillJson([]);
      return;
    }

    if (pathname.endsWith('/ai/models')) {
      await fulfillJson([]);
      return;
    }

    if (pathname.endsWith('/plc/settings')) {
      await fulfillJson({ settings: {} });
      return;
    }

    if (pathname.endsWith('/station-communication/settings')) {
      await fulfillJson({ enabled: false, mode: 'loopback', port: 5000 });
      return;
    }

    if (pathname.endsWith('/cameras/bindings')) {
      await fulfillJson([]);
      return;
    }

    if (request.method() !== 'GET') {
      await fulfillJson({ ok: true });
      return;
    }

    await fulfillJson({});
  });
}

async function bootStudio(page: Page) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFailClosedStartupFlags(page);
  await installApiRoutes(page);
  await bootAuthenticatedApp(page);
}

async function openView(page: Page, view: 'flow' | 'ai' | 'settings') {
  await page.locator(`.nav-btn[data-view="${view}"]`).click();
  await expect(page.locator(`.nav-btn[data-view="${view}"]`)).toHaveClass(/active/);
}

async function expectFlowShellUsable(page: Page) {
  await expect(page.locator('#main-content')).toHaveClass(/flow-editor-shell/);
  await expect(page.locator('#flow-editor')).toBeVisible();
  await expect(page.locator('#flow-canvas')).toBeVisible();
  await expect(page.locator('#operator-rail')).toBeVisible();
  await expect(page.locator('#operator-rail .operator-rail-item').first()).toBeVisible();
  await expect(page.locator('.inspector-pane')).toBeVisible();
  await expect(page.locator('.preview-workbench-pane')).toBeVisible();
}

async function expectFlowOnlySurfacesCollapsed(page: Page) {
  await expect(page.locator('#main-content')).not.toHaveClass(/flow-editor-shell/);

  const states = await page.evaluate(selectors => selectors.map(selector => {
    const element = document.querySelector(selector);
    if (!element) {
      return { selector, exists: false, display: 'missing', width: 0, height: 0, hiddenClass: false };
    }

    const style = window.getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return {
      selector,
      exists: true,
      display: style.display,
      width: rect.width,
      height: rect.height,
      hiddenClass: element.classList.contains('hidden'),
    };
  }), flowOnlySelectors);

  for (const state of states) {
    expect(state.exists, `${state.selector} should exist for flow pages`).toBe(true);
    expect(state.hiddenClass || state.display === 'none', `${state.selector} should be hidden off flow`).toBe(true);
    expect(state.width, `${state.selector} should not occupy width off flow`).toBe(0);
    expect(state.height, `${state.selector} should not occupy height off flow`).toBe(0);
  }
}

test.describe('Studio legacy AI/Settings fail-closed regression', () => {
  test.beforeEach(async ({ page }) => {
    await bootStudio(page);
  });

  test('AI page uses legacy AiPanel by default even when backend flag is true', async ({ page }) => {
    await openView(page, 'ai');

    await expect(page.locator('#ai-view')).toBeVisible();
    await expect(page.locator('.ai-workspace')).toBeVisible();
    await expect(page.locator('#ai-input')).toBeVisible();
    await expect(page.locator('#ai-view')).toContainText('智能体对话');
    await expect(page.locator('#ai-view')).toContainText('视觉智能体工作台');
    await expect(page.locator('#ai-view')).toContainText('快捷示例');
    await expect(page.locator('.ai-panel-capability-owner')).toHaveCount(0);
    await expect(page.locator('#ai-view')).not.toContainText('后端 AgentRun 状态投影');
    await expectFlowOnlySurfacesCollapsed(page);
  });

  test('Settings page uses legacy SettingsView and keeps full camera AI PLC menus', async ({ page }) => {
    await openView(page, 'settings');

    const settings = page.locator('#settings-view');
    await expect(settings.locator('.settings-layout')).toBeVisible();
    await expect(settings.locator('.settings-content-area')).toBeVisible();
    await expect(settings.locator('.settings-main-title')).toBeVisible();
    await expect(settings.locator('.settings-panel')).not.toHaveCount(0);
    await expect(settings).toContainText('相机管理');
    await expect(settings).toContainText('AI 大模型');
    await expect(settings).toContainText('PLC 通讯');
    await expect(settings).toContainText('工站通讯');

    for (const tab of ['cameras', 'ai', 'communication', 'station']) {
      await settings.locator(`.settings-menu-item[data-tab="${tab}"]`).click();
      await expect(settings.locator(`.settings-panel[data-section="${tab}"]`)).toHaveClass(/active/);
    }

    await expect(settings.locator('.settings-capability-owner')).toHaveCount(0);
    await expect(settings.locator('.settings-json-editor')).toHaveCount(0);
    await expect(settings.locator('textarea.settings-json-editor')).toHaveCount(0);
    await expectFlowOnlySurfacesCollapsed(page);
  });

  test('flow to AI to settings to flow keeps layout isolated and restores the flow shell', async ({ page }) => {
    await expectFlowShellUsable(page);
    await page.screenshot({ path: screenshotPath('flow-layout-restored.png'), fullPage: true });

    await openView(page, 'ai');
    await expect(page.locator('.ai-workspace')).toBeVisible();
    await expectFlowOnlySurfacesCollapsed(page);
    await page.screenshot({ path: screenshotPath('ai-legacy.png'), fullPage: true });

    await openView(page, 'settings');
    await expect(page.locator('#settings-view .settings-layout')).toBeVisible();
    await expectFlowOnlySurfacesCollapsed(page);
    await page.screenshot({ path: screenshotPath('settings-legacy.png'), fullPage: true });

    await openView(page, 'flow');
    await expectFlowShellUsable(page);
  });
});
