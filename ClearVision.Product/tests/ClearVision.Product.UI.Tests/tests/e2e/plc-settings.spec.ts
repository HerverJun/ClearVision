import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, Page, test } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const screenshotDir = resolve(process.cwd(), 'test-results', 'plc-settings');

type PlcSaveMode = 'success' | 'validation-error';
type PlcTestMode = 'success' | 'failure' | 'delayed-success';

function screenshotPath(name: string) {
  mkdirSync(screenshotDir, { recursive: true });
  return resolve(screenshotDir, name);
}

function buildSettingsPayload() {
  return {
    general: {
      softwareTitle: 'ClearVision',
      language: 'zh-CN',
      theme: 'light',
      autoStart: false,
    },
    communication: {
      activeProtocol: 'S7',
      heartbeatIntervalMs: 1000,
      s7: {
        ipAddress: '192.168.0.1',
        port: 102,
        cpuType: 'S7-1200',
        rack: 0,
        slot: 1,
        mappings: [],
      },
      mc: {
        ipAddress: '192.168.3.1',
        port: 5002,
        mappings: [],
      },
      fins: {
        ipAddress: '192.168.250.1',
        port: 9600,
        mappings: [],
      },
    },
    tcpCommunication: {
      profiles: [],
    },
    storage: {
      imageSavePath: 'C:/VisionData/Images',
      savePolicy: 'NgOnly',
      retentionDays: 30,
      minFreeSpaceGb: 5,
    },
    runtime: {
      autoRun: false,
      stopOnConsecutiveNg: 0,
      missingMaterialTimeoutSeconds: 120,
      applyProtectionRules: true,
    },
    security: {
      passwordMinLength: 6,
      sessionTimeoutMinutes: 30,
      loginFailureLockoutCount: 5,
    },
    cameras: [],
    activeCameraId: '',
  };
}

function createApiState() {
  return {
    settingsPayload: buildSettingsPayload(),
    plcPuts: [] as any[],
    settingsPuts: [] as any[],
    plcTestRequests: [] as any[],
    plcSaveMode: 'success' as PlcSaveMode,
    plcTestMode: 'success' as PlcTestMode,
  };
}

async function installStartupFlags(page: Page) {
  await page.addInitScript(() => {
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: Object.freeze({
        featureFlags: Object.freeze({
          'Studio2.Settings': true,
          'Studio2.AiPanel': true,
          'Studio2.PropertyPanel': true,
          'Studio2.PreviewPanel': true,
        }),
      }),
      writable: false,
      configurable: false,
    });
  });
}

async function installApiRoutes(page: Page, state: ReturnType<typeof createApiState>) {
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const pathname = url.pathname;

    const fulfillJson = (body: unknown, status = 200) => route.fulfill({
      status,
      contentType: 'application/json',
      body: JSON.stringify(body),
    });

    if (pathname === '/api/auth/me') {
      await fulfillJson({ username: 'admin', displayName: 'E2E Admin', role: 'Admin' });
      return;
    }

    if (pathname === '/api/health' || pathname.endsWith('/health')) {
      await fulfillJson({ status: 'ok' });
      return;
    }

    if (pathname === '/api/plc/settings') {
      if (request.method() === 'GET') {
        await fulfillJson({ success: true, settings: state.settingsPayload.communication });
        return;
      }

      const payload = JSON.parse(request.postData() || '{}');
      state.plcPuts.push(payload);
      if (state.plcSaveMode === 'validation-error') {
        await fulfillJson({
          success: false,
          message: 'PLC 配置校验失败。',
          settings: payload,
          errors: [
            {
              protocol: payload.activeProtocol || 'S7',
              section: 'mapping',
              field: 'address',
              index: 0,
              message: 'PLC 地址格式无效。',
            },
          ],
        });
        return;
      }

      state.settingsPayload.communication = payload;
      await fulfillJson({
        success: true,
        message: 'PLC 配置已保存。',
        settings: state.settingsPayload.communication,
        errors: [],
      });
      return;
    }

    if (pathname === '/api/plc/test-connection') {
      const payload = JSON.parse(request.postData() || '{}');
      state.plcTestRequests.push(payload);
      if (state.plcTestMode === 'delayed-success') {
        await new Promise(resolve => setTimeout(resolve, 2000));
      }

      await fulfillJson({
        success: state.plcTestMode !== 'failure',
        message: state.plcTestMode === 'failure' ? '连接失败：目标无响应。' : '连接成功。',
        protocol: payload.protocol,
      });
      return;
    }

    if (pathname === '/api/settings') {
      if (request.method() === 'GET') {
        await fulfillJson(state.settingsPayload);
        return;
      }

      const payload = JSON.parse(request.postData() || '{}');
      state.settingsPuts.push(payload);
      state.settingsPayload = {
        ...state.settingsPayload,
        ...payload,
        communication: payload.communication || state.settingsPayload.communication,
      };
      await fulfillJson(state.settingsPayload);
      return;
    }

    if (pathname === '/api/settings/disk-usage') {
      await fulfillJson({
        canWrite: true,
        totalGb: 256,
        freeGb: 180,
        usedPercent: 30,
      });
      return;
    }

    if (pathname === '/api/settings/database/status') {
      await fulfillJson({ status: 'ok', migrations: [], tables: [] });
      return;
    }

    if (pathname === '/api/users' || pathname === '/api/ai/models' || pathname === '/api/cameras/bindings') {
      await fulfillJson([]);
      return;
    }

    if (pathname === '/api/station-communication/settings') {
      await fulfillJson({ enabled: false, mode: 'Disabled', port: 5000, lanHost: '127.0.0.1' });
      return;
    }

    if (pathname === '/api/operators/library') {
      await fulfillJson([]);
      return;
    }

    if (pathname === '/api/operators/types') {
      await fulfillJson([]);
      return;
    }

    if (request.method() !== 'GET') {
      await fulfillJson({ success: true });
      return;
    }

    await fulfillJson({});
  });
}

async function openCommunicationSettings(page: Page) {
  await page.locator('.nav-btn[data-view="settings"]').click();
  await expect(page.locator('.settings-menu-item[data-tab="communication"]')).toBeVisible();
  await page.locator('.settings-menu-item[data-tab="communication"]').click();
  await expect(page.locator('#cfg-protocol')).toBeVisible();
  await expect(page.locator('#btn-plc-test')).toBeVisible();
}

async function addMapping(
  page: Page,
  index: number,
  values: { name: string; address: string; dataType: string; canWrite?: boolean; description?: string },
) {
  await page.locator('#btn-add-plc-mapping').click();
  const row = page.locator('tr.plc-mapping-row').nth(index);
  await expect(row).toBeVisible();
  await row.locator('[data-field="name"]').fill(values.name);
  await row.locator('[data-field="address"]').fill(values.address);
  await row.locator('[data-field="dataType"]').selectOption(values.dataType);
  await row.locator('[data-field="canWrite"]').selectOption(values.canWrite ? 'true' : 'false');
  await row.locator('[data-field="description"]').fill(values.description || '');
}

async function expectLayoutHealthy(
  page: Page,
  options: { requireActionBarVisible?: boolean; requireActionBarInViewport?: boolean } = {},
) {
  await expect(page.locator('#btn-plc-test')).toBeVisible();
  await expect(page.locator('#btn-add-plc-mapping')).toBeVisible();
  if (options.requireActionBarVisible || options.requireActionBarInViewport) {
    await expect(page.locator('#btn-save-plc')).toBeVisible();
    await expect(page.locator('#btn-reset-plc')).toBeVisible();
  }

  const metrics = await page.evaluate(() => {
    const root = document.querySelector('#settings-view') as HTMLElement | null;
    const panel = document.querySelector('.settings-tab-panels') as HTMLElement | null;
    const actionBar = document.querySelector('.plc-settings-actions') as HTMLElement | null;
    const saveButton = document.querySelector('#btn-save-plc') as HTMLElement | null;
    const resetButton = document.querySelector('#btn-reset-plc') as HTMLElement | null;
    const lastRow = Array.from(document.querySelectorAll('tr.plc-mapping-row')).at(-1) as HTMLElement | undefined;
    const actionRect = actionBar?.getBoundingClientRect();
    const saveRect = saveButton?.getBoundingClientRect();
    const resetRect = resetButton?.getBoundingClientRect();
    const lastRect = lastRow?.getBoundingClientRect();
    const bothVisible = Boolean(actionRect && lastRect
      && actionRect!.bottom > 0
      && actionRect!.top < window.innerHeight
      && lastRect!.bottom > 0
      && lastRect!.top < window.innerHeight);
    const actionOverlapsLastRow = bothVisible
      ? !(actionRect!.top >= lastRect!.bottom || actionRect!.bottom <= lastRect!.top)
      : false;
    const text = document.body.innerText || '';
    return {
      documentOverflow: Math.ceil(document.documentElement.scrollWidth - window.innerWidth),
      bodyOverflow: Math.ceil(document.body.scrollWidth - window.innerWidth),
      rootOverflow: root ? Math.ceil(root.scrollWidth - root.clientWidth) : 0,
      panelOverflow: panel ? Math.ceil(panel.scrollWidth - panel.clientWidth) : 0,
      hasMojibake: /�|鍔|閰|绔|鏄犲|涓夎|淇濆|娴嬭瘯|濡\?/.test(text),
      actionOverlapsLastRow,
      saveButtonInViewport: Boolean(saveRect && saveRect.top >= 0 && saveRect.bottom <= window.innerHeight && saveRect.left >= 0 && saveRect.right <= window.innerWidth),
      resetButtonInViewport: Boolean(resetRect && resetRect.top >= 0 && resetRect.bottom <= window.innerHeight && resetRect.left >= 0 && resetRect.right <= window.innerWidth),
    };
  });

  expect(metrics.documentOverflow).toBeLessThanOrEqual(2);
  expect(metrics.bodyOverflow).toBeLessThanOrEqual(2);
  expect(metrics.rootOverflow).toBeLessThanOrEqual(2);
  expect(metrics.panelOverflow).toBeLessThanOrEqual(2);
  expect(metrics.hasMojibake).toBe(false);
  expect(metrics.actionOverlapsLastRow).toBe(false);
  if (options.requireActionBarInViewport) {
    expect(metrics.saveButtonInViewport).toBe(true);
    expect(metrics.resetButtonInViewport).toBe(true);
  }
}

async function capture(
  page: Page,
  name: string,
  options: { requireActionBarVisible?: boolean; requireActionBarInViewport?: boolean } = {},
) {
  await expectLayoutHealthy(page, options);
  await page.screenshot({ path: screenshotPath(name) });
}

async function dismissToasts(page: Page) {
  const closeButtons = page.locator('.cv-toast-close');
  const count = await closeButtons.count();
  for (let index = count - 1; index >= 0; index -= 1) {
    await closeButtons.nth(index).click();
  }
  await expect(page.locator('.cv-toast')).toHaveCount(0);
}

test('PLC communication settings payloads, errors, connection test states, and responsive screenshots', async ({ page }) => {
  const state = createApiState();
  await page.setViewportSize({ width: 1366, height: 768 });
  await installStartupFlags(page);
  await installApiRoutes(page, state);
  await bootAuthenticatedApp(page);
  await openCommunicationSettings(page);

  await expect(page.locator('#cfg-protocol')).toHaveValue('S7');
  await capture(page, 'a-s7-default.png', { requireActionBarVisible: true });
  await capture(page, 'd-mapping-empty.png', { requireActionBarVisible: true });

  await page.locator('#cfg-plcIpAddress').fill('10.10.10.11');
  await page.locator('#cfg-plcPort').fill('1102');
  await page.locator('#cfg-s7-cpuType').selectOption('S7-1500');
  await page.locator('#cfg-s7-rack').fill('2');
  await page.locator('#cfg-s7-slot').fill('3');

  await page.setViewportSize({ width: 1280, height: 720 });
  await page.locator('#cfg-protocol').selectOption('MC');
  await expect(page.locator('#cfg-plcPort')).toHaveValue('5002');
  await page.locator('#cfg-plcIpAddress').fill('10.10.10.22');
  await page.locator('#cfg-plcPort').fill('5003');
  await capture(page, 'b-mc-page.png', { requireActionBarVisible: true });

  await page.locator('#cfg-protocol').selectOption('FINS');
  await expect(page.locator('#cfg-plcPort')).toHaveValue('9600');
  await page.locator('#cfg-plcIpAddress').fill('10.10.10.33');
  await page.locator('#cfg-plcPort').fill('9603');
  await capture(page, 'c-fins-page.png', { requireActionBarVisible: true });

  await page.locator('#cfg-protocol').selectOption('S7');
  await expect(page.locator('#cfg-plcIpAddress')).toHaveValue('10.10.10.11');
  await expect(page.locator('#cfg-plcPort')).toHaveValue('1102');
  await expect(page.locator('#cfg-s7-cpuType')).toHaveValue('S7-1500');
  await expect(page.locator('#cfg-s7-rack')).toHaveValue('2');
  await expect(page.locator('#cfg-s7-slot')).toHaveValue('3');
  await page.locator('#cfg-protocol').selectOption('MC');
  await expect(page.locator('#cfg-plcIpAddress')).toHaveValue('10.10.10.22');
  await expect(page.locator('#cfg-plcPort')).toHaveValue('5003');
  await page.locator('#cfg-protocol').selectOption('FINS');

  await addMapping(page, 0, {
    name: 'FinsReady',
    address: 'DM100',
    dataType: 'Word',
    canWrite: false,
    description: '就绪信号',
  });
  await addMapping(page, 1, {
    name: 'FinsStart',
    address: 'CIO10.3',
    dataType: 'Bool',
    canWrite: true,
    description: '启动握手',
  });
  await capture(page, 'e-multi-row-mapping.png', { requireActionBarVisible: true });

  state.plcSaveMode = 'validation-error';
  await page.locator('tr.plc-mapping-row').nth(0).locator('[data-field="address"]').fill('BAD');
  await page.locator('#btn-save-plc').click();
  await expect(page.locator('.plc-field-error')).toContainText('PLC 地址格式无效');
  await capture(page, 'f-field-validation-error.png', { requireActionBarVisible: true });

  const validationPayload = state.plcPuts.at(-1);
  expect(validationPayload.activeProtocol).toBe('FINS');
  expect(validationPayload.fins.ipAddress).toBe('10.10.10.33');
  expect(validationPayload.fins.mappings[0].address).toBe('BAD');
  expect(validationPayload.s7.ipAddress).toBe('192.168.0.1');
  expect(validationPayload.mc.ipAddress).toBe('192.168.3.1');

  state.plcSaveMode = 'success';
  state.plcTestMode = 'delayed-success';
  const delayedConnection = page.waitForResponse(response => response.url().endsWith('/api/plc/test-connection'));
  await page.locator('#btn-plc-test').click();
  await expect(page.locator('#btn-plc-test')).toBeDisabled();
  await expect(page.locator('#btn-plc-test .plc-test-label')).toHaveText('测试中...');
  await expect(page.locator('#plc-connection-badge')).toContainText('测试中');
  await capture(page, 'g-connection-loading.png', { requireActionBarVisible: true });
  await delayedConnection;
  await expect(page.locator('#btn-plc-test')).toBeEnabled();
  await expect(page.locator('#plc-connection-badge')).toContainText('连接正常');
  expect(state.plcTestRequests.at(-1)).toMatchObject({
    protocol: 'FINS',
    ipAddress: '10.10.10.33',
    port: 9603,
  });

  state.plcTestMode = 'failure';
  await page.locator('#btn-plc-test').click();
  await expect(page.locator('#btn-plc-test')).toBeEnabled();
  await expect(page.locator('#plc-connection-badge')).toContainText('连接失败');
  await capture(page, 'h-connection-failed.png', { requireActionBarVisible: true });

  await page.locator('tr.plc-mapping-row').nth(0).locator('[data-field="address"]').fill('DM100');
  await page.locator('#btn-save-plc').click();
  await expect.poll(() => state.plcPuts.length).toBeGreaterThanOrEqual(2);
  await expect.poll(() => state.settingsPuts.length).toBeGreaterThanOrEqual(1);
  await expect(page.locator('.cv-toast', { hasText: 'PLC 配置已保存' }).last()).toBeVisible();
  await capture(page, 'i-save-success.png', { requireActionBarVisible: true });

  await page.locator('#cfg-plcIpAddress').fill('10.99.99.99');
  await page.locator('#btn-reset-plc').click();
  await expect(page.locator('#cfg-plcIpAddress')).toHaveValue('10.10.10.33');
  await expect(page.locator('.plc-field-error')).toHaveCount(0);

  await dismissToasts(page);
  await page.setViewportSize({ width: 1366, height: 420 });
  await page.locator('#btn-save-plc').scrollIntoViewIfNeeded();
  await capture(page, 'j-low-height-scroll.png', { requireActionBarInViewport: true });

  await page.setViewportSize({ width: 1024, height: 768 });
  await page.locator('.settings-tab-panels').evaluate(element => { element.scrollTop = 0; });
  await expect(page.locator('#cfg-protocol')).toBeVisible();
  await capture(page, 'k-narrow-width-layout.png');
});
