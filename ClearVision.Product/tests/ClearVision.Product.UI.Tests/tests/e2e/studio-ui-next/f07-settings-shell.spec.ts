import { expect, test, type Page, type Route } from '@playwright/test';
import {
  auditF02Request,
  expectGetOnly,
  fulfillF02Json,
  installF02BrowserStartup,
  type F02MethodAuditEntry
} from './f02-browser-fixture';

const fixtureSchema = 'f07-settings-shell.v1';

function fullSettingsPayload(): Record<string, unknown> {
  return {
    revision: 7,
    general: { softwareTitle: 'ClearVision Browser', theme: 'light', autoStart: false },
    storage: { imageSavePath: 'D:/VisionData', savePolicy: 'NgOnly', retentionDays: 30, minFreeSpaceGb: 5 },
    runtime: {
      autoRun: false,
      stopOnConsecutiveNg: 3,
      missingMaterialTimeoutSeconds: 120,
      applyProtectionRules: true,
      runtimePreviewPilot: { mode: 'metadata_only' }
    },
    security: { passwordMinLength: 8, sessionTimeoutMinutes: 30, loginFailureLockoutCount: 5 },
    communication: {},
    tcpCommunication: {},
    features: {},
    cameras: [],
    activeCameraId: ''
  };
}

function safeSettingsPayload(): Record<string, unknown> {
  return {
    safeSubset: true,
    revision: 7,
    general: { softwareTitle: 'ClearVision Browser', theme: 'light' }
  };
}

async function bootSettings(
  page: Page,
  role: 'Admin' | 'Engineer' | 'Operator',
  response: 'full' | 'safe' = 'safe',
  delayMs = 0
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  await installF02BrowserStartup(page);
  await page.route('**/health', async route => {
    audit.push(auditF02Request(route.request()));
    await fulfillF02Json(route, 200, { status: 'Healthy', port: 5177 }, fixtureSchema);
  });
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    if (url.pathname === '/api/auth/setup-status') {
      await fulfillF02Json(route, 200, {
        requiresInitialAdminSetup: false,
        usernameMinLength: 3,
        passwordMinLength: 6,
        requiresUppercase: false,
        requiresLowercase: false,
        requiresDigit: false
      }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/auth/me') {
      await fulfillF02Json(route, 200, { userId: 'fixture-user', username: 'fixture-settings', role }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/settings') {
      if (delayMs > 0) await new Promise(resolve => setTimeout(resolve, delayMs));
      await fulfillF02Json(route, 200, response === 'full' ? fullSettingsPayload() : safeSettingsPayload(), fixtureSchema);
      return;
    }
    await fulfillF02Json(route, 404, { error: 'NotFound' }, fixtureSchema);
  });
  return audit;
}

test('Settings shell reads Engineer safe projection, exposes groups, and stays GET-only', async ({ page }) => {
  const audit = await bootSettings(page, 'Engineer', 'safe');
  await page.goto('/studio/index.html#/settings');

  await expect(page.locator('[data-capability="settings"]')).toBeVisible();
  await expect(page.locator('[data-settings-phase="ready"]')).toBeVisible();
  await expect(page.locator('[data-capability="settings"][data-settings-safe-subset="true"]')).toBeVisible();
  await expect(page.locator('[data-capability="settings"]')).toContainText('safe subset');
  await expect(page.locator('[data-product-nav="/settings"]')).toBeVisible();
  await expect(page.locator('[data-settings-group="camera"]')).toBeVisible();

  await page.locator('[data-settings-group="storage"]').click();
  await expect(page.getByText('当前响应未包含此分组', { exact: true })).toBeVisible();
  expect(expectGetOnly(audit)).toBe(true);
  expect(audit.filter(entry => entry.path === '/api/settings')).toHaveLength(1);
});

test('Settings shell renders loading then Admin full projection without a save request', async ({ page }) => {
  const audit = await bootSettings(page, 'Admin', 'full', 250);
  await page.goto('/studio/index.html#/settings');

  await expect(page.locator('[data-settings-phase="loading"]')).toBeVisible();
  await expect(page.locator('[data-settings-phase="ready"]')).toBeVisible();
  await expect(page.getByText('完整管理员投影', { exact: true })).toBeVisible();
  await page.locator('[data-settings-group="storage"]').click();
  await expect(page.getByText('D:/VisionData', { exact: true })).toBeVisible();
  expect(audit.filter(entry => entry.method !== 'GET')).toEqual([]);
});

test('Operator is redirected to forbidden and never mounts Settings owner read', async ({ page }) => {
  const audit = await bootSettings(page, 'Operator');
  await page.goto('/studio/index.html#/settings');

  await expect(page.locator('[data-studio-page="forbidden"]')).toBeVisible();
  await expect(page.locator('[data-capability="settings"]')).toHaveCount(0);
  expect(audit.some(entry => entry.path === '/api/settings')).toBe(false);
  expect(expectGetOnly(audit)).toBe(true);
});

test('Settings shell keeps the page width bounded at desktop and narrow viewports', async ({ page }) => {
  const audit = await bootSettings(page, 'Engineer', 'safe');
  for (const viewport of [{ width: 1920, height: 1080 }, { width: 390, height: 844 }] as const) {
    await page.setViewportSize(viewport);
    await page.goto('/studio/index.html#/settings');
    await expect(page.locator('[data-capability="settings"]')).toBeVisible();
    const overflow = await page.locator('html').evaluate(element =>
      Math.max(
        element.scrollWidth - element.clientWidth,
        document.body.scrollWidth - document.body.clientWidth
      )
    );
    expect(overflow, `horizontal overflow at ${viewport.width}x${viewport.height}`).toBeLessThanOrEqual(1);
  }
  expect(expectGetOnly(audit)).toBe(true);
});
