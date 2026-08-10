import { expect, test, type Page, type Route } from '@playwright/test';
import {
  auditF02Request,
  createF02RuntimeErrorAudit,
  expectGetOnly,
  fulfillF02Json,
  installF02BrowserStartup,
  installF02VisualPreferences,
  type F02MethodAuditEntry
} from './f02-browser-fixture';
import { captureF07Evidence, hasF07EvidenceTarget } from './f07-device-fixture';

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

function stationSettingsPayload(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    success: true,
    message: 'Station settings loaded.',
    mode: 'LocalLoopback',
    port: 5010,
    lanHost: '127.0.0.1',
    lanAddresses: ['127.0.0.1'],
    localStationSyncEnabled: true,
    token: { hasToken: true, mask: '******', last4: '9876' },
    paths: { studio: 'C:/fixture/studio-settings.json', localStation: 'C:/fixture/station-settings.json' },
    currentRunning: {
      studioEnabled: true,
      studioListenMode: 'Loopback',
      studioPort: 5000,
      studioToken: { hasToken: true, mask: '******', last4: '0000' }
    },
    requiresRestart: { studio: false, localStation: false },
    localStationBaseUrl: 'http://127.0.0.1:5010',
    remoteStationBaseUrl: '',
    localStationHubUrl: 'http://127.0.0.1:5010/hubs/station-sync',
    remoteStationHubUrl: '',
    diagnostics: ['Station communication fixture is available.'],
    ...overrides
  };
}

function aiReasoningSupportPayload(): Record<string, unknown> {
  return {
    familyId: 'openai_gpt5',
    familyName: 'OpenAI GPT-5',
    allowedModes: ['auto', 'off', 'on'],
    allowedEfforts: ['low', 'medium', 'high'],
    helpText: '测试环境仅描述此服务端点支持的推理能力。',
    supportsExplicitMode: true,
    supportsEffort: true,
    isModelLockedOn: false,
    defaultMode: 'auto'
  };
}

function aiFullModelPayload(hasApiKey = true): Record<string, unknown> {
  return {
    id: 'model-1',
    name: 'fixture-primary',
    displayName: '测试语言模型',
    provider: 'OpenAI Compatible',
    model: 'gpt-5.1-mini',
    hasApiKey,
    apiKeyMasked: hasApiKey ? '******' : '',
    baseUrl: 'https://fixture.example/v1',
    timeoutMs: 120000,
    isActive: true,
    isEnabled: true,
    protocol: 'openai_compatible',
    wireApi: 'responses',
    authMode: 'bearer',
    authHeaderName: 'Authorization',
    extraHeaders: { authorization: '<redacted>' },
    extraQuery: null,
    extraBody: null,
    roleBindings: ['generation', 'planner'],
    modelRole: 'generation',
    priority: 10,
    remark: 'Browser fixture',
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    lastTestStatus: null,
    lastTestAt: null,
    lastTestLatencyMs: null,
    capabilities: { supportsVisionInput: true, supportsToolCall: true },
    reasoning: { mode: 'auto', effort: 'medium' },
    reasoningSupport: aiReasoningSupportPayload()
  };
}

function aiSafeModelPayload(): Record<string, unknown> {
  return {
    id: 'model-1',
    displayName: '测试语言模型',
    provider: 'OpenAI Compatible',
    model: 'gpt-5.1-mini',
    modelRole: 'generation',
    isEnabled: true,
    isActive: true,
    capabilities: { supportsVisionInput: true, supportsToolCall: true }
  };
}

async function bootSettings(
  page: Page,
  role: 'Admin' | 'Engineer' | 'Operator',
  response: 'full' | 'safe' = 'safe',
  delayMs = 0,
  mutationDelayMs = 0
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  let currentSettings = fullSettingsPayload();
  let currentStation = stationSettingsPayload();
  let currentAiKey = 'fixture-ai-key';
  await installF02BrowserStartup(page, { 'Studio2.Settings': true });
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
    if (url.pathname === '/api/station-communication/settings') {
      if (role !== 'Admin') {
        await fulfillF02Json(route, 403, { error: 'AdminRequired' }, fixtureSchema);
        return;
      }
      if (request.method() === 'PUT') {
        if (mutationDelayMs > 0) await new Promise(resolve => setTimeout(resolve, mutationDelayMs));
        const body = JSON.parse(request.postData() ?? '{}') as Record<string, unknown>;
        currentStation = stationSettingsPayload({
          mode: typeof body.mode === 'string' ? body.mode : currentStation.mode,
          port: typeof body.port === 'number' ? body.port : currentStation.port,
          lanHost: typeof body.lanHost === 'string' ? body.lanHost : currentStation.lanHost,
          localStationSyncEnabled: typeof body.localStationSyncEnabled === 'boolean'
            ? body.localStationSyncEnabled
            : currentStation.localStationSyncEnabled,
          requiresRestart: { studio: true, localStation: true }
        });
        await fulfillF02Json(route, 200, currentStation, fixtureSchema);
        return;
      }
      await fulfillF02Json(route, 200, currentStation, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/station-communication/token' && request.method() === 'POST') {
      if (role !== 'Admin') {
        await fulfillF02Json(route, 403, { error: 'AdminRequired' }, fixtureSchema);
        return;
      }
      await fulfillF02Json(route, 200, {
        success: true,
        operation: 'regenerate',
        token: 'fixture-token-value',
        tokenInfo: { hasToken: true, mask: '******', last4: '9876' },
        settings: currentStation,
        message: 'Station token regenerated.',
        errors: []
      }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/ai/models' || url.pathname === '/api/ai/models/model-1') {
      if (request.method() === 'GET') {
        await fulfillF02Json(route, 200, role === 'Admin'
          ? [aiFullModelPayload(currentAiKey.length > 0)]
          : [aiSafeModelPayload()], fixtureSchema);
        return;
      }
      if (role !== 'Admin') {
        await fulfillF02Json(route, 403, { error: 'AdminRequired' }, fixtureSchema);
        return;
      }
      if (request.method() === 'PUT' || request.method() === 'POST') {
        if (mutationDelayMs > 0) await new Promise(resolve => setTimeout(resolve, mutationDelayMs));
        const body = JSON.parse(request.postData() ?? '{}') as Record<string, unknown>;
        const operation = typeof body.apiKeyOperation === 'string' ? body.apiKeyOperation : 'keep';
        if (operation === 'replace' && typeof body.apiKey === 'string') currentAiKey = body.apiKey;
        if (operation === 'clear') currentAiKey = '';
        await fulfillF02Json(route, 200, { message: 'AI model updated.' }, fixtureSchema);
        return;
      }
    }
    if (url.pathname === '/api/ai/reasoning-support' && request.method() === 'POST') {
      await fulfillF02Json(route, 200, aiReasoningSupportPayload(), fixtureSchema);
      return;
    }
    if (url.pathname === '/api/ai/models/model-1/test' && request.method() === 'POST') {
      await fulfillF02Json(route, 200, {
        connectionOk: true,
        success: true,
        statusCode: 200,
        errorCode: '',
        latencyMs: 17,
        sanitizedMessage: 'Connection contract verified.',
        message: 'Connection contract verified.',
        provider: 'OpenAI Compatible',
        modelName: 'gpt-5.1-mini',
        protocol: 'openai_compatible',
        wireApi: 'responses'
      }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/settings') {
      if (request.method() === 'PUT') {
        if (mutationDelayMs > 0) await new Promise(resolve => setTimeout(resolve, mutationDelayMs));
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
    if (url.pathname === '/api/users' && request.method() === 'GET') {
      await fulfillF02Json(route, 200, [{
        id: 'fixture-operator',
        username: 'operator-a',
        displayName: '产线操作员',
        role: 2,
        isActive: true,
        lastLoginAt: '2026-08-01T00:00:00Z'
      }], fixtureSchema);
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
  await expect(page.locator('[data-capability="settings"]')).toContainText('工程师安全范围');
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
  await expect(page.getByText('管理员完整范围', { exact: true })).toBeVisible();
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

test('Settings keeps dirty drafts across groups and asks before route leave', async ({ page }) => {
  await bootSettings(page, 'Admin', 'full');
  await page.goto('/studio/index.html#/settings');

  await page.locator('[data-settings-group="general"]').click();
  await page.locator('input[name="softwareTitle"]').fill('Draft retained in browser');
  await page.locator('[data-settings-group="storage"]').click();
  await page.locator('[data-settings-group="general"]').click();
  await expect(page.locator('input[name="softwareTitle"]')).toHaveValue('Draft retained in browser');

  await page.locator('[data-product-nav="/projects"]').first().click();
  await expect(page.getByTestId('leave-guard-stay')).toBeVisible();
  await expect(page).toHaveURL(/#\/settings$/);
  await page.getByTestId('leave-guard-stay').click();
  await expect(page.locator('input[name="softwareTitle"]')).toHaveValue('Draft retained in browser');
});

test('Admin Station communication preserves masked token and shows restart-required after save', async ({ page }) => {
  const stationBodies: string[] = [];
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.pathname === '/api/station-communication/settings' && request.method() === 'PUT') {
      stationBodies.push(request.postData() ?? '');
    }
  });
  await bootSettings(page, 'Admin', 'full');
  await page.goto('/studio/index.html#/settings');

  await page.locator('[data-settings-group="station"]').click();
  const station = page.locator('[data-settings-station]');
  await expect(station).toBeVisible();
  await station.locator('input[name="stationPort"]').fill('5033');
  await station.locator('[data-settings-station-save]').click();
  await expect(station.locator('[data-settings-station-restart-required]')).toBeVisible();
  await expect(station.locator('[data-settings-station-feedback]')).toBeVisible();

  expect(stationBodies).toHaveLength(1);
  expect(JSON.parse(stationBodies[0]!)).not.toHaveProperty('sharedToken');
  expect(await station.textContent()).not.toContain('fixture-token-value');

  await station.locator('select[name="stationTokenOperation"]').selectOption('replace');
  await station.locator('input[name="stationToken"]').fill('station-browser-secret');
  await station.locator('[data-settings-station-save]').click();
  await expect(station.locator('input[name="stationToken"]')).toHaveCount(0);
  expect(JSON.parse(stationBodies[1]!)).toMatchObject({ sharedToken: 'station-browser-secret' });
  expect(await station.textContent()).not.toContain('station-browser-secret');
  expect(await page.evaluate(() => Object.values(sessionStorage).join('|'))).not.toContain('station-browser-secret');
});

test('Admin AI model administration keeps API key out of projection and supports keep replace clear', async ({ page }) => {
  const aiBodies: string[] = [];
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.pathname === '/api/ai/models/model-1' && request.method() === 'PUT') {
      aiBodies.push(request.postData() ?? '');
    }
  });
  await bootSettings(page, 'Admin', 'full');
  await page.goto('/studio/index.html#/settings');

  await page.locator('[data-settings-group="ai-model"]').click();
  const ai = page.locator('[data-settings-ai-model]');
  await expect(ai.locator('[data-settings-ai-model-editor]')).toBeVisible();

  await ai.locator('input[name="aiModel"]').fill('gpt-5.1-mini-updated');
  await ai.locator('[data-settings-ai-model-save]').click();
  await expect(ai.locator('[data-settings-ai-model-feedback]')).toBeVisible();
  expect(JSON.parse(aiBodies[0]!)).toMatchObject({ apiKeyOperation: 'keep' });
  expect(JSON.parse(aiBodies[0]!)).not.toHaveProperty('apiKey');

  await ai.locator('[data-settings-ai-key-operation] select').selectOption('replace');
  await ai.locator('input[name="aiApiKey"]').fill('ai-browser-secret');
  await ai.locator('[data-settings-ai-model-save]').click();
  await expect(ai.locator('input[name="aiApiKey"]')).toHaveCount(0);
  expect(JSON.parse(aiBodies[1]!)).toMatchObject({ apiKeyOperation: 'replace', apiKey: 'ai-browser-secret' });
  expect(await ai.textContent()).not.toContain('ai-browser-secret');

  await ai.locator('[data-settings-ai-key-operation] select').selectOption('clear');
  await ai.locator('[data-settings-ai-model-save]').click();
  expect(JSON.parse(aiBodies[2]!)).toMatchObject({ apiKeyOperation: 'clear' });
  expect(JSON.parse(aiBodies[2]!)).not.toHaveProperty('apiKey');
  expect(await page.evaluate(() => Object.values(sessionStorage).join('|'))).not.toContain('ai-browser-secret');

  await ai.locator('[data-settings-ai-model-test]').click();
  await expect(ai.locator('[data-settings-ai-model-test-result]')).toBeVisible();
  await ai.locator('[data-settings-ai-reasoning-support]').click();
  await expect(ai).toContainText('OpenAI GPT-5');
});

test('Engineer receives AI safe projection and reasoning support without management or secrets', async ({ page }) => {
  const audit = await bootSettings(page, 'Engineer', 'safe');
  await page.goto('/studio/index.html#/settings');

  await page.locator('[data-settings-group="ai-model"]').click();
  const ai = page.locator('[data-settings-ai-model]');
  await expect(ai.locator('[data-settings-ai-model-safe]')).toBeVisible();
  await expect(ai.locator('[data-settings-ai-model-editor]')).toHaveCount(0);
  await expect(ai.locator('input[type="password"]')).toHaveCount(0);
  await ai.locator('[data-settings-ai-reasoning-support]').click();
  await expect(ai).toContainText('OpenAI GPT-5');
  expect(audit.some(entry => entry.method === 'GET' && entry.path === '/api/ai/models')).toBe(true);
  expect(audit.some(entry => entry.method === 'POST' && entry.path === '/api/ai/reasoning-support')).toBe(true);
  expect(await ai.textContent()).not.toContain('fixture-ai-key');
});

test('Settings blocks route leave while an AI model mutation is pending', async ({ page }) => {
  const audit = await bootSettings(page, 'Admin', 'full', 0, 600);
  await page.goto('/studio/index.html#/settings');

  await page.locator('[data-settings-group="ai-model"]').click();
  await page.locator('[data-settings-ai-model] input[name="aiModel"]').fill('pending-ai-model');
  await page.locator('[data-settings-ai-model-save]').click();
  await expect.poll(() => audit.filter(entry => entry.method === 'PUT' && entry.path === '/api/ai/models/model-1').length)
    .toBe(1);

  await page.locator('[data-product-nav="/projects"]').first().click();
  await expect(page.locator('[data-product-state="leave-blocked"]')).toBeVisible();
  await expect(page).toHaveURL(/#\/settings$/);
  await expect(page.locator('[data-settings-ai-model-feedback]')).toBeVisible();
});

test('Settings blocks route leave while a mutation is pending and keeps the mutation observable', async ({ page }) => {
  const audit = await bootSettings(page, 'Admin', 'full', 0, 600);
  await page.goto('/studio/index.html#/settings');

  await page.locator('[data-settings-group="general"]').click();
  await page.locator('input[name="softwareTitle"]').fill('Pending browser mutation');
  await page.getByRole('button', { name: '保存常规设置', exact: true }).click();
  await expect.poll(() => audit.filter(entry => entry.method === 'PUT' && entry.path === '/api/settings').length)
    .toBe(1);

  await page.locator('[data-product-nav="/projects"]').first().click();
  await expect(page.locator('[data-product-state="leave-blocked"]')).toBeVisible();
  await expect(page).toHaveURL(/#\/settings$/);
  await expect(page.locator('[data-settings-feedback="saved"]')).toBeVisible();
});

test('Admin Database exposes only status and backup metadata', async ({ page }) => {
  const audit = await bootSettings(page, 'Admin', 'full');
  await page.goto('/studio/index.html#/settings');

  const databaseGroup = page.locator('[data-settings-group="database"]');
  await expect(databaseGroup).toHaveCount(1);
  await databaseGroup.click();
  const database = page.locator('[data-settings-section="database"]');
  await expect(database).toContainText('正常');
  const backupButton = database.locator('.settings-database__backup button');
  await expect(backupButton).toHaveCount(1);
  page.once('dialog', dialog => dialog.accept());
  await backupButton.click();
  await expect(page.locator('[data-settings-backup-result]')).toBeVisible();
  await expect(database).not.toContainText('manual.cvdbbak');
  expect(audit.some(entry => entry.method === 'GET' && entry.path === '/api/settings/database/status')).toBe(true);
  expect(audit.some(entry => entry.method === 'POST' && entry.path === '/api/settings/database/backup')).toBe(true);
});

test('Admin password change invalidates the session and clears secrets', async ({ page }) => {
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
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(page).toHaveURL(/#\/login\?reason=change-password$/);
  expect(await page.evaluate(() => sessionStorage.getItem('cv_auth_token'))).toBeNull();
  expect(audit.some(entry => entry.method === 'POST' && entry.path === '/api/auth/change-password')).toBe(true);
  if (await page.locator('[data-settings-group="database"]').count() === 0) return;

  await page.locator('[data-settings-group="database"]').click();
  await expect(page.locator('[data-settings-section="database"]')).toContainText('正常');
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

for (const visual of [
  { id: 'settings-overview', group: 'overview', selector: '[data-settings-overview]', width: 1920, height: 1080, theme: 'light', density: 'compact' },
  { id: 'settings-overview-b1', group: 'overview', selector: '[data-settings-overview]', width: 1536, height: 864, theme: 'light', density: 'compact' },
  { id: 'settings-overview-b2', group: 'overview', selector: '[data-settings-overview]', width: 1366, height: 768, theme: 'light', density: 'compact' },
  { id: 'settings-overview-b3', group: 'overview', selector: '[data-settings-overview]', width: 1920, height: 1080, theme: 'dark', density: 'compact' },
  { id: 'settings-overview-b4-light', group: 'overview', selector: '[data-settings-overview]', width: 1920, height: 1080, theme: 'light', density: 'comfortable' },
  { id: 'settings-overview-b4-dark', group: 'overview', selector: '[data-settings-overview]', width: 1920, height: 1080, theme: 'dark', density: 'comfortable' },
  { id: 'settings-general', group: 'general', selector: '[data-settings-section="general"]', width: 1536, height: 864, theme: 'dark', density: 'comfortable' },
  { id: 'settings-storage', group: 'storage', selector: '[data-settings-section="storage"]', width: 1366, height: 768, theme: 'light', density: 'compact' },
  { id: 'settings-runtime', group: 'runtime', selector: '[data-settings-section="runtime"]', width: 1366, height: 768, theme: 'dark', density: 'compact' },
  { id: 'settings-security', group: 'security', selector: '[data-settings-section="security"]', width: 1536, height: 864, theme: 'light', density: 'comfortable' },
  { id: 'settings-station', group: 'station', selector: '[data-settings-station]', width: 1920, height: 1080, theme: 'dark', density: 'compact' },
  { id: 'settings-ai-model', group: 'ai-model', selector: '[data-settings-ai-model]', width: 1920, height: 1080, theme: 'light', density: 'compact' },
  { id: 'settings-database', group: 'database', selector: '[data-settings-section="database"]', width: 1366, height: 768, theme: 'dark', density: 'comfortable' }
] as const) {
  test(`captures ${visual.id} F07 baseline evidence`, async ({ page }) => {
    test.skip(!hasF07EvidenceTarget(), 'F07 visual evidence output was not requested.');
    const viewport = { width: visual.width, height: visual.height } as const;
    await page.setViewportSize(viewport);
    await installF02VisualPreferences(page, visual.theme, visual.density);
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const audit = await bootSettings(page, 'Admin', 'full');
    await page.goto('/studio/index.html#/settings');
    await expect(page.locator('[data-settings-phase="ready"]')).toBeVisible();
    if (visual.group !== 'overview') await page.locator(`[data-settings-group="${visual.group}"]`).click();
    await expect(page.locator(visual.selector)).toBeVisible();
    await captureF07Evidence(page, {
      scenario: visual.id,
      viewport,
      theme: visual.theme,
      density: visual.density,
      requests: audit,
      runtimeErrors
    });
  });
}

test('captures Settings Engineer safe-subset evidence', async ({ page }) => {
  test.skip(!hasF07EvidenceTarget(), 'F07 visual evidence output was not requested.');
  const viewport = { width: 1366, height: 768 } as const;
  await page.setViewportSize(viewport);
  await installF02VisualPreferences(page, 'light', 'compact');
  const runtimeErrors = createF02RuntimeErrorAudit(page, [403]);
  const audit = await bootSettings(page, 'Engineer', 'safe');
  await page.goto('/studio/index.html#/settings');
  await page.locator('[data-settings-group="ai-model"]').click();
  await expect(page.locator('[data-settings-ai-model-safe]')).toBeVisible();
  await captureF07Evidence(page, {
    scenario: 'settings-engineer-ai-safe', viewport, theme: 'light', density: 'compact', requests: audit, runtimeErrors
  });
});
