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

function databaseStatusPayload(): Record<string, unknown> {
  return {
    databasePath: 'C:/private/vision.db',
    exists: true,
    state: 'Healthy',
    schemaVersion: 6,
    currentSchemaVersion: 6,
    appliedMigrations: ['001'],
    pendingMigrations: [],
    missingSchemaItems: [],
    integrityCheck: 'ok',
    foreignKeyViolationCount: 0,
    rowCounts: { projects: 2 },
    issues: [],
    databaseSizeBytes: 1024,
    walSizeBytes: 0,
    backupRootDirectory: 'C:/private/backups',
    packageRootDirectory: 'C:/private/packages',
    packageFileCount: 2
  };
}

function databaseBackupPayload(): Record<string, unknown> {
  return {
    backupPath: 'C:/private/backups/manual.cvdbbak',
    createdAtUtc: '2026-08-01T00:00:00Z',
    sizeBytes: 2048,
    databaseSizeBytes: 1024,
    packageFileCount: 2,
    packageBytes: 256
  };
}

async function bootSettings(
  page: Page,
  role: 'Admin' | 'Engineer' | 'Operator',
  response: 'full' | 'safe' = 'safe',
  delayMs = 0
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  let currentSettings = fullSettingsPayload();
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
      if (request.method() === 'PUT') {
        const body = JSON.parse(request.postData() ?? '{}') as Record<string, unknown>;
        const scope = typeof body.saveScope === 'string' ? body.saveScope : '';
        const incoming = body[scope];
        const existing = currentSettings[scope];
        if (!scope || typeof incoming !== 'object' || incoming === null || Array.isArray(incoming) ||
            typeof existing !== 'object' || existing === null || Array.isArray(existing)) {
          await fulfillF02Json(route, 400, { error: 'Invalid scoped settings fixture payload' }, fixtureSchema);
          return;
        }
        currentSettings = {
          ...currentSettings,
          [scope]: { ...(existing as Record<string, unknown>), ...(incoming as Record<string, unknown>) }
        };
        await fulfillF02Json(route, 200, { message: '设置已保存', config: currentSettings }, fixtureSchema);
        return;
      }
      if (delayMs > 0) await new Promise(resolve => setTimeout(resolve, delayMs));
      await fulfillF02Json(route, 200, response === 'full' ? currentSettings : safeSettingsPayload(), fixtureSchema);
      return;
    }
    if (url.pathname === '/api/auth/change-password' && request.method() === 'POST') {
      await fulfillF02Json(route, 200, { message: '密码修改成功' }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/settings/database/status' && request.method() === 'GET') {
      await fulfillF02Json(route, 200, databaseStatusPayload(), fixtureSchema);
      return;
    }
    if (url.pathname === '/api/settings/database/backup' && request.method() === 'POST') {
      await fulfillF02Json(route, 200, databaseBackupPayload(), fixtureSchema);
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
  await expect(page.locator('input[name="imageSavePath"]')).toHaveValue('D:/VisionData');
  expect(audit.filter(entry => entry.method !== 'GET')).toEqual([]);
});

test('Admin can save a scoped General section and discard the next draft', async ({ page }) => {
  const audit = await bootSettings(page, 'Admin', 'full');
  await page.goto('/studio/index.html#/settings');

  await page.locator('[data-settings-group="general"]').click();
  await page.locator('input[name="softwareTitle"]').fill('Updated Browser Title');
  await page.getByRole('button', { name: '保存常规设置', exact: true }).click();
  await expect(page.locator('[data-settings-feedback="saved"]')).toBeVisible();
  await expect(page.locator('input[name="softwareTitle"]')).toHaveValue('Updated Browser Title');

  await page.locator('input[name="softwareTitle"]').fill('Discarded Browser Draft');
  await page.getByRole('button', { name: '放弃修改', exact: true }).click();
  await expect(page.locator('input[name="softwareTitle"]')).toHaveValue('Updated Browser Title');

  expect(audit.filter(entry => entry.method === 'PUT' && entry.path === '/api/settings')).toHaveLength(1);
});

test('Admin password change clears secrets and Database exposes only status and backup metadata', async ({ page }) => {
  const audit = await bootSettings(page, 'Admin', 'full');
  await page.goto('/studio/index.html#/settings');

  await page.locator('[data-settings-group="security"]').click();
  const passwordPanel = page.locator('[data-settings-change-password]');
  const oldPassword = passwordPanel.locator('input[autocomplete="current-password"]');
  const newPassword = passwordPanel.locator('input[autocomplete="new-password"]');
  await expect(oldPassword).toHaveCount(1);
  await expect(newPassword).toHaveCount(1);
  await oldPassword.fill('fixture-old-password');
  await newPassword.fill('fixture-new-password');
  await page.getByRole('button', { name: '修改密码', exact: true }).click();
  await expect(page.locator('[data-settings-feedback="saved"]')).toBeVisible();
  await expect(oldPassword).toHaveValue('');
  await expect(newPassword).toHaveValue('');

  await page.locator('[data-settings-group="database"]').click();
  await expect(page.locator('[data-settings-section="database"]')).toContainText('Healthy');
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: '创建备份', exact: true }).click();
  await expect(page.locator('[data-settings-backup-result]')).toBeVisible();
  await expect(page.locator('[data-settings-section="database"]')).not.toContainText('manual.cvdbbak');
  expect(audit.some(entry => entry.method === 'POST' && entry.path === '/api/auth/change-password')).toBe(true);
  expect(audit.some(entry => entry.method === 'GET' && entry.path === '/api/settings/database/status')).toBe(true);
  expect(audit.some(entry => entry.method === 'POST' && entry.path === '/api/settings/database/backup')).toBe(true);
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
