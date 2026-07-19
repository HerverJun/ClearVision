import { expect, test, type Page, type Route } from '@playwright/test';
import {
  captureF04VisualEvidence,
  createF04RuntimeErrorAudit,
  hasF04VisualEvidenceTarget
} from './f04-browser-evidence';

interface AuthFixtureState {
  requiresSetup: boolean;
  password: string;
  role: 'Admin' | 'Engineer' | 'Operator';
  validTokens: Set<string>;
  tokenSequence: number;
  loginCalls: number;
  logoutCalls: number;
  protected401Calls: number;
  loginDelayMs: number;
}

function setupPolicy(state: AuthFixtureState) {
  return {
    requiresInitialAdminSetup: state.requiresSetup,
    usernameMinLength: 3,
    passwordMinLength: 6,
    requiresUppercase: false,
    requiresLowercase: false,
    requiresDigit: false
  };
}

function tokenFrom(route: Route): string | null {
  const authorization = route.request().headers().authorization ?? '';
  return authorization.startsWith('Bearer ') ? authorization.slice('Bearer '.length) : null;
}

async function json(route: Route, status: number, body: unknown): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    headers: {
      'x-clearvision-data-source': 'F04_BROWSER_FIXTURE',
      'x-clearvision-auth-source': 'STATEFUL_AUTH_FIXTURE'
    },
    body: JSON.stringify(body)
  });
}

async function installStartup(page: Page, token?: string): Promise<void> {
  await page.addInitScript(initialToken => {
    if (initialToken) sessionStorage.setItem('cv_auth_token', initialToken);
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: Object.freeze({
        schemaVersion: 1,
        uiKind: 'studio-ui',
        hostKind: 'browser-test',
        apiBaseUrl: `${window.location.origin}/api`,
        studioUiBasePath: '/studio/',
        featureFlags: Object.freeze({ 'Studio2.Workspace': true })
      }),
      writable: false,
      configurable: false
    });
  }, token);
}

async function installAuthFixture(page: Page, state: AuthFixtureState): Promise<void> {
  await page.route('**/health', route => json(route, 200, { status: 'Healthy', port: 5177 }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path === '/api/auth/setup-status') {
      await json(route, 200, setupPolicy(state));
      return;
    }
    if (path === '/api/auth/setup-admin' && request.method() === 'POST') {
      if (!state.requiresSetup) {
        await json(route, 409, { error: '系统已完成初始化，请直接登录' });
        return;
      }
      const body = request.postDataJSON() as { username: string; password: string; confirmPassword: string };
      if (!body.username || body.password.length < 6 || body.password !== body.confirmPassword) {
        await json(route, 400, { error: '初始化输入无效' });
        return;
      }
      state.requiresSetup = false;
      state.password = body.password;
      state.role = 'Admin';
      const token = `fixture-token-${++state.tokenSequence}`;
      state.validTokens.add(token);
      await json(route, 200, { token, user: { username: body.username, role: 'Admin' } });
      return;
    }
    if (path === '/api/auth/login' && request.method() === 'POST') {
      state.loginCalls += 1;
      if (state.loginDelayMs > 0) await new Promise(resolve => setTimeout(resolve, state.loginDelayMs));
      const body = request.postDataJSON() as { username: string; password: string };
      if (state.requiresSetup || body.password !== state.password) {
        await json(route, 401, { error: '用户名或密码错误' });
        return;
      }
      const token = `fixture-token-${++state.tokenSequence}`;
      state.validTokens.add(token);
      await json(route, 200, { token, user: { username: body.username, role: state.role } });
      return;
    }
    if (path === '/api/auth/me') {
      const token = tokenFrom(route);
      if (!token || !state.validTokens.has(token)) {
        await json(route, 401, { error: 'Unauthorized' });
        return;
      }
      await json(route, 200, { userId: 'fixture-user', username: 'fixture-user', role: state.role });
      return;
    }
    if (path === '/api/auth/change-password' && request.method() === 'POST') {
      const token = tokenFrom(route);
      const body = request.postDataJSON() as { oldPassword: string; newPassword: string };
      if (!token || !state.validTokens.has(token)) {
        await json(route, 401, { error: 'Unauthorized' });
        return;
      }
      if (body.oldPassword !== state.password) {
        await json(route, 400, { errorCode: 'INVALID_OLD_PASSWORD', error: '当前密码错误' });
        return;
      }
      state.password = body.newPassword;
      state.validTokens.clear();
      await json(route, 200, { message: '密码修改成功' });
      return;
    }
    if (path === '/api/auth/logout' && request.method() === 'POST') {
      state.logoutCalls += 1;
      const token = tokenFrom(route);
      if (token) state.validTokens.delete(token);
      await json(route, 200, { message: '已登出', audit: 'server-session-cleared' });
      return;
    }
    if (path === '/api/projects/recent') {
      await json(route, 200, []);
      return;
    }
    if (path === '/api/projects' || path.startsWith('/api/inspection/history')) {
      state.protected401Calls += 1;
      await json(route, 401, { error: 'Unauthorized' });
      return;
    }
    await json(route, 404, { error: 'NotFound' });
  });
}

function freshState(overrides: Partial<AuthFixtureState> = {}): AuthFixtureState {
  return {
    requiresSetup: true,
    password: 'old-password',
    role: 'Admin',
    validTokens: new Set<string>(),
    tokenSequence: 0,
    loginCalls: 0,
    logoutCalls: 0,
    protected401Calls: 0,
    loginDelayMs: 0,
    ...overrides
  };
}

test('F04 auth lifecycle: setup auto-login, recovery, password invalidation, new login and logout', async ({ page }) => {
  const viewport = { width: 1366, height: 768 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const state = freshState();
  await installStartup(page);
  await installAuthFixture(page, state);
  await page.goto('/studio/index.html#/overview');

  await expect(page.locator('[data-auth-page="setup"]')).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'setup', viewport, runtimeErrors });
  }
  await page.getByLabel('管理员用户名').fill('admin');
  await page.getByLabel('密码', { exact: true }).fill('old-password');
  await page.getByLabel('确认密码').fill('old-password');
  await page.getByRole('button', { name: '创建并进入 Studio' }).click();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  await expect(page.locator('[data-capability="overview"]')).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'overview', viewport, runtimeErrors });
  }

  await page.reload();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  await page.getByRole('link', { name: '修改密码' }).click();
  await page.getByLabel('当前密码').fill('old-password');
  await page.getByLabel('新密码').fill('new-password');
  await page.getByRole('button', { name: '修改密码并退出' }).click();
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(page.locator('[data-auth-message]')).toContainText('新密码重新登录');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'login', viewport, runtimeErrors });
  }

  await page.getByLabel('用户名').fill('admin');
  await page.getByLabel('密码').fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.locator('[data-auth-message]')).toContainText('用户名或密码错误');
  await page.getByLabel('密码').fill('new-password');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();

  await page.getByRole('button', { name: '退出', exact: true }).click();
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  expect(state.logoutCalls).toBe(1);
  await page.goto('/studio/index.html#/overview');
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await page.goBack();
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(page.locator('[data-product-shell]')).toHaveCount(0);
  await expect(page.locator('html')).toHaveJSProperty('scrollWidth', 1366);
});

test('F04 auth guards reject role/profile and external return routes', async ({ page }) => {
  const viewport = { width: 1366, height: 768 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const state = freshState({ requiresSetup: false, role: 'Operator' });
  const token = 'operator-token';
  state.validTokens.add(token);
  await installStartup(page, token);
  await installAuthFixture(page, state);
  await page.goto('/studio/index.html#/projects/11111111-1111-4111-8111-111111111111/workspace');
  await expect(page.locator('[data-studio-page="forbidden"]')).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'forbidden', viewport, runtimeErrors });
  }
  await page.goto('/studio/index.html#/stations');
  await expect(page.locator('[data-studio-page="forbidden"]')).toBeVisible();

  await page.goto('/studio/index.html#/overview');
  const navigation = page.getByRole('navigation', { name: '产品主导航' });
  for (const path of ['/projects', '/results']) {
    await expect(navigation.locator(`[data-product-nav="${path}"]`)).toBeVisible();
  }
  const more = page.locator('[data-product-more]');
  await more.locator('summary').click();
  for (const path of ['/overview', '/operators', '/about']) {
    await expect(more.locator(`[data-product-nav="${path}"]`)).toBeVisible();
  }
  await expect(page.locator('[data-product-nav="/diagnostics"]')).toHaveCount(0);
  await expect(page.locator('[data-product-nav="/stations"]')).toHaveCount(0);
  await page.getByRole('button', { name: '退出', exact: true }).click();
  await page.evaluate(() => { window.location.hash = '#/login?returnTo=https://evil.example/steal'; });
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await page.getByLabel('用户名').fill('operator');
  await page.getByLabel('密码').fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page).toHaveURL(/#\/overview$/);
});

test('F04 product shell keeps the approved navigation stable across viewport and Browser DPR matrix', async ({ browser }) => {
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
      const runtimeErrors = createF04RuntimeErrorAudit(page);
      const state = freshState({ requiresSetup: false, role: 'Engineer' });
      const token = `matrix-${viewport.width}-${deviceScaleFactor}`;
      state.validTokens.add(token);
      await installStartup(page, token);
      await installAuthFixture(page, state);
      await page.goto('/studio/index.html#/overview');
      await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
      await expect(page.locator('[data-product-nav="/diagnostics"]')).toBeVisible();

      const projection = await page.evaluate(() => ({
        dpr: window.devicePixelRatio,
        horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        shellCount: document.querySelectorAll('[data-product-shell="ready"]').length,
        leaveOwnerCount: document.querySelector('[data-product-shell]')
          ?.getAttribute('data-leave-guard-owner-count')
      }));
      expect(projection, `${viewport.width}x${viewport.height}@${deviceScaleFactor}`).toEqual({
        dpr: deviceScaleFactor,
        horizontalOverflow: 0,
        shellCount: 1,
        leaveOwnerCount: '1'
      });
      expect(runtimeErrors, `${viewport.width}x${viewport.height}@${deviceScaleFactor}`)
        .toEqual({ consoleErrors: [], pageErrors: [] });
      if (hasF04VisualEvidenceTarget()) {
        await captureF04VisualEvidence(page, {
          scenario: 'shell-matrix',
          viewport,
          runtimeErrors,
          notes: ['Browser-emulated DPR evidence; not native WebView2 DPI evidence.']
        });
      }
      await context.close();
    }
  }
});

test('F04 auth deduplicates submit, ignores late response and collapses concurrent protected 401s', async ({ page }) => {
  const state = freshState({ requiresSetup: false, role: 'Engineer', loginDelayMs: 200 });
  await installStartup(page);
  await installAuthFixture(page, state);
  await page.goto('/studio/index.html#/login');
  await page.getByLabel('用户名').fill('engineer');
  await page.getByLabel('密码').fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).evaluate((button: HTMLButtonElement) => {
    button.click();
    button.click();
  });
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  expect(state.loginCalls).toBe(1);

  await page.getByRole('button', { name: '退出', exact: true }).click();
  state.loginDelayMs = 300;
  await page.getByLabel('用户名').fill('engineer');
  await page.getByLabel('密码').fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).click({ noWaitAfter: true });
  await page.reload();
  await page.waitForTimeout(350);
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(page.locator('[data-product-shell]')).toHaveCount(0);

  state.loginDelayMs = 0;
  await page.getByLabel('用户名').fill('engineer');
  await page.getByLabel('密码').fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  await page.goto('/studio/index.html#/results?source=local&projectId=11111111-1111-4111-8111-111111111111');
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  expect(state.protected401Calls).toBeGreaterThanOrEqual(2);
  await expect(page.locator('[data-product-shell]')).toHaveCount(0);
});
