import { expect, test, type Page, type Route } from '@playwright/test';
import {
  captureF04VisualEvidence,
  createF04RuntimeErrorAudit,
  hasF04VisualEvidenceTarget
} from './f04-browser-evidence';
import { installF02VisualPreferences } from './f02-browser-fixture';

interface AuthFixtureState {
  requiresSetup: boolean;
  password: string;
  role: 'Admin' | 'Engineer' | 'Operator';
  validTokens: Set<string>;
  tokenSequence: number;
  loginCalls: number;
  logoutCalls: number;
  protected401Calls: number;
  protectedFailuresEnabled: boolean;
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
        startupProfile: 'NEXT_DEFAULT',
        profileAllowedRoles: Object.freeze(['Admin', 'Engineer', 'Operator']),
        featureFlags: Object.freeze({ 'Studio2.Workspace': true })
      }),
      writable: false,
      configurable: false
    });
  }, token);
}

async function collectAuthLayoutProjection(page: Page) {
  return page.evaluate(() => {
    const shell = document.querySelector<HTMLElement>('[data-auth-shell="ready"]');
    const frame = document.querySelector<HTMLElement>('.auth-shell__frame');
    const form = document.querySelector<HTMLElement>('[data-auth-page="login"]');
    const submit = document.querySelector<HTMLElement>('.auth-form__submit');
    if (!shell || !frame || !form || !submit) throw new Error('Auth layout is incomplete.');

    const inspect = (element: HTMLElement) => {
      const box = element.getBoundingClientRect();
      const centerX = box.left + box.width / 2;
      const centerY = box.top + box.height / 2;
      const hit = document.elementFromPoint(centerX, centerY);
      return {
        x: box.x,
        y: box.y,
        width: box.width,
        height: box.height,
        right: box.right,
        bottom: box.bottom,
        centerX,
        unobscured: hit !== null && (hit === element || element.contains(hit) || hit.contains(element))
      };
    };

    return {
      viewport: { width: innerWidth, height: innerHeight },
      shell: inspect(shell),
      frame: inspect(frame),
      form: inspect(form),
      submit: inspect(submit),
      productShells: document.querySelectorAll('[data-product-shell]').length,
      productPreviews: document.querySelectorAll('.auth-product-preview').length,
      overflow: Math.max(
        document.documentElement.scrollWidth - document.documentElement.clientWidth,
        document.body.scrollWidth - document.body.clientWidth
      )
    };
  });
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
    if ((path === '/api/projects' || path.startsWith('/api/inspection/history')) && state.protectedFailuresEnabled) {
      state.protected401Calls += 1;
      await json(route, 401, { error: 'Unauthorized' });
      return;
    }
    if (path === '/api/projects') {
      await json(route, 200, []);
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
    protectedFailuresEnabled: false,
    loginDelayMs: 0,
    ...overrides
  };
}

async function openSessionMenu(page: Page): Promise<void> {
  await page.locator('[data-product-user-menu] button[aria-haspopup="menu"]').click();
}

test('F04 auth lifecycle: setup auto-login, recovery, password invalidation, new login and logout', async ({ page }) => {
  const viewport = { width: 1366, height: 768 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const state = freshState();
  await installStartup(page);
  await installAuthFixture(page, state);
  await page.goto('/studio/index.html#/projects');

  await expect(page.locator('[data-auth-page="setup"]')).toBeVisible();
  await expect(page.locator('[data-auth-shell]')).not.toContainText('/api/');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'setup', viewport, runtimeErrors });
  }
  await page.getByLabel('管理员用户名').fill('admin');
  await page.getByLabel('密码', { exact: true }).fill('old-password');
  await page.getByLabel('确认密码', { exact: true }).fill('old-password');
  await page.getByRole('button', { name: '创建管理员并进入工程库' }).click();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  await expect(page.locator('[data-capability="projects-read"]')).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'projects', viewport, runtimeErrors });
  }

  await page.reload();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  await openSessionMenu(page);
  await page.getByRole('menuitem', { name: '修改密码' }).click();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'change-password', viewport, runtimeErrors });
  }
  await page.getByLabel('当前密码', { exact: true }).fill('old-password');
  await page.getByLabel('新密码', { exact: true }).fill('new-password');
  await page.getByLabel('确认新密码', { exact: true }).fill('different-password');
  await page.getByRole('button', { name: '保存新密码并重新登录' }).click();
  await expect(page.locator('[data-auth-message]')).toContainText('两次输入的新密码不一致');
  await page.getByLabel('确认新密码', { exact: true }).fill('new-password');
  await page.getByRole('button', { name: '保存新密码并重新登录' }).click();
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(page.locator('[data-auth-message]')).toContainText('新密码重新登录');
  await expect(page.locator('[data-auth-shell]')).not.toContainText('/api/');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'login', viewport, runtimeErrors });
  }

  await page.getByLabel('用户名').fill('admin');
  await page.getByLabel('密码', { exact: true }).fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.locator('[data-auth-message]')).toContainText('用户名或密码错误');
  await page.getByLabel('密码', { exact: true }).fill('new-password');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();

  await openSessionMenu(page);
  await page.getByRole('menuitem', { name: '退出', exact: true }).click();
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  expect(state.logoutCalls).toBe(1);
  await page.goto('/studio/index.html#/projects');
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

  await page.goto('/studio/index.html#/projects');
  const navigation = page.getByRole('navigation', { name: '产品主导航' });
  for (const path of ['/overview', '/projects', '/results', '/operators']) {
    await expect(navigation.locator(`[data-product-nav="${path}"]`)).toBeVisible();
  }
  const more = page.locator('[data-product-more]');
  await expect(more).toHaveCount(1);
  await more.locator('button[aria-haspopup="menu"]').click();
  await expect(page.locator('[role="menu"] [data-product-nav="/about"]')).toBeVisible();
  await expect(page.locator('[data-product-nav="/diagnostics"]')).toHaveCount(0);
  await expect(page.locator('[data-product-nav="/stations"]')).toHaveCount(0);
  await openSessionMenu(page);
  await page.getByRole('menuitem', { name: '退出', exact: true }).click();
  await page.evaluate(() => { window.location.hash = '#/login?returnTo=https://evil.example/steal'; });
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await page.getByLabel('用户名').fill('operator');
  await page.getByLabel('密码', { exact: true }).fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page).toHaveURL(/#\/projects$/);
});

test('F04 login remembers the username only after an accepted authentication', async ({ page }) => {
  const state = freshState({ requiresSetup: false, role: 'Engineer' });
  await installStartup(page);
  await installAuthFixture(page, state);
  await page.goto('/studio/index.html#/login');

  await page.getByLabel('用户名').fill('not-persisted');
  await page.getByLabel('密码', { exact: true }).fill('wrong-password');
  await page.getByRole('checkbox', { name: '记住账号' }).check();
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.locator('[data-auth-message]')).toContainText('用户名或密码错误');
  await page.reload();
  await expect(page.getByLabel('用户名')).toHaveValue('');
  await expect(page.getByRole('checkbox', { name: '记住账号' })).not.toBeChecked();

  await page.getByLabel('用户名').fill('engineer');
  await page.getByLabel('密码', { exact: true }).fill('old-password');
  await page.getByRole('checkbox', { name: '记住账号' }).check();
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  await openSessionMenu(page);
  await page.getByRole('menuitem', { name: '退出', exact: true }).click();
  await expect(page.getByLabel('用户名')).toHaveValue('engineer');
  await expect(page.getByRole('checkbox', { name: '记住账号' })).toBeChecked();

  await page.getByLabel('密码', { exact: true }).fill('old-password');
  await page.getByRole('checkbox', { name: '记住账号' }).uncheck();
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  await openSessionMenu(page);
  await page.getByRole('menuitem', { name: '退出', exact: true }).click();
  await expect(page.getByLabel('用户名')).toHaveValue('');
  await expect(page.getByRole('checkbox', { name: '记住账号' })).not.toBeChecked();
});

test('F04 login keeps a visible checkbox focus indicator in Windows high contrast mode', async ({ page }) => {
  const state = freshState({ requiresSetup: false, role: 'Engineer' });
  await page.emulateMedia({ forcedColors: 'active' });
  await installStartup(page);
  await installAuthFixture(page, state);
  await page.goto('/studio/index.html#/login');

  const checkbox = page.getByRole('checkbox', { name: '记住账号' });
  await checkbox.focus();
  await expect(checkbox).toBeFocused();
  const focusStyle = await checkbox.evaluate(element => {
    const style = getComputedStyle(element);
    return { outlineStyle: style.outlineStyle, outlineWidth: style.outlineWidth };
  });
  expect(focusStyle.outlineStyle).not.toBe('none');
  expect(focusStyle.outlineWidth).not.toBe('0px');
});

test('F04 product shell keeps the approved navigation stable across viewport and Browser DPR matrix', async ({ browser }) => {
  test.setTimeout(60_000);

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
      try {
        await page.goto('/studio/index.html#/projects');
        await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
        await expect(page.locator('[data-product-nav="/projects"]')).toBeVisible();
        await expect(page.locator('[data-product-nav="/results"]')).toBeVisible();
        const more = page.locator('[data-product-more]');
        await expect(more).toHaveCount(1);
        await more.locator('button[aria-haspopup="menu"]').click();
        await expect(page.locator('[role="menu"] [data-product-nav="/diagnostics"]')).toBeVisible();

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
      } finally {
        await context.close();
      }
    }
  }
});

test('F04 auth deduplicates submit, ignores late response and collapses concurrent protected 401s', async ({ page }) => {
  const state = freshState({ requiresSetup: false, role: 'Engineer', loginDelayMs: 200 });
  await installStartup(page);
  await installAuthFixture(page, state);
  await page.goto('/studio/index.html#/login');
  await page.getByLabel('用户名').fill('engineer');
  await page.getByLabel('密码', { exact: true }).fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).evaluate((button: HTMLButtonElement) => {
    button.click();
    button.click();
  });
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  expect(state.loginCalls).toBe(1);

  await openSessionMenu(page);
  await page.getByRole('menuitem', { name: '退出', exact: true }).click();
  state.loginDelayMs = 300;
  await page.getByLabel('用户名').fill('engineer');
  await page.getByLabel('密码', { exact: true }).fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).click({ noWaitAfter: true });
  await page.reload();
  await page.waitForTimeout(350);
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(page.locator('[data-product-shell]')).toHaveCount(0);

  state.loginDelayMs = 0;
  await page.getByLabel('用户名').fill('engineer');
  await page.getByLabel('密码', { exact: true }).fill('old-password');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.locator('[data-product-shell="ready"]')).toBeVisible();
  state.protectedFailuresEnabled = true;
  await page.goto('/studio/index.html#/results?source=local&projectId=11111111-1111-4111-8111-111111111111');
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  expect(state.protected401Calls).toBeGreaterThanOrEqual(2);
  await expect(page.locator('[data-product-shell]')).toHaveCount(0);
});

test('V4 auth entry remains isolated and centered across supported viewports', async ({ browser }) => {
  for (const visual of [
    { id: 'b0', width: 1920, height: 1080, theme: 'light', density: 'compact' },
    { id: 'b1', width: 1536, height: 864, theme: 'light', density: 'compact' },
    { id: 'b2', width: 1366, height: 768, theme: 'light', density: 'compact' },
    { id: 'b2-dark', width: 1366, height: 768, theme: 'dark', density: 'compact' },
    { id: 'b3', width: 1920, height: 1080, theme: 'dark', density: 'compact' },
    { id: 'b4-light', width: 1920, height: 1080, theme: 'light', density: 'comfortable' },
    { id: 'b4-dark', width: 1920, height: 1080, theme: 'dark', density: 'comfortable' },
    { id: 'edge-920', width: 920, height: 768, theme: 'light', density: 'compact' },
    { id: 'narrow-480', width: 480, height: 720, theme: 'dark', density: 'comfortable' },
    { id: 'short-viewport', width: 1366, height: 600, theme: 'light', density: 'compact' }
  ] as const) {
    const viewport = { width: visual.width, height: visual.height } as const;
    const context = await browser.newContext({ viewport });
    const page = await context.newPage();
    const runtimeErrors = createF04RuntimeErrorAudit(page);
    try {
      await installF02VisualPreferences(page, visual.theme, visual.density);
      await installStartup(page);
      await installAuthFixture(page, freshState({ requiresSetup: false, role: 'Engineer' }));
      await page.goto('/studio/index.html#/login');
      await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
      await expect(page.getByRole('heading', { name: '登录', exact: true })).toBeVisible();
      await expect(page.locator('[data-product-shell]')).toHaveCount(0);
      await expect(page.locator('.auth-product-preview')).toHaveCount(0);
      const preferences = await page.evaluate(() => ({
        theme: document.documentElement.dataset.theme,
        density: document.documentElement.dataset.density
      }));
      expect(preferences).toEqual({ theme: visual.theme, density: visual.density });

      const layout = await collectAuthLayoutProjection(page);
      expect(layout.productShells).toBe(0);
      expect(layout.productPreviews).toBe(0);
      expect(layout.overflow).toBe(0);
      expect(Math.abs(layout.shell.x)).toBeLessThanOrEqual(1);
      expect(Math.abs(layout.shell.y)).toBeLessThanOrEqual(1);
      expect(layout.shell.width).toBeGreaterThanOrEqual(layout.viewport.width - 1);
      expect(layout.shell.height).toBeGreaterThanOrEqual(layout.viewport.height - 1);
      expect(Math.abs(layout.frame.centerX - layout.viewport.width / 2)).toBeLessThanOrEqual(12);
      expect(layout.form.x).toBeGreaterThanOrEqual(0);
      expect(layout.form.right).toBeLessThanOrEqual(layout.viewport.width);
      expect(layout.form.y).toBeGreaterThanOrEqual(0);
      expect(layout.form.bottom).toBeLessThanOrEqual(layout.viewport.height);
      expect(layout.form.unobscured).toBe(true);
      expect(layout.submit.x).toBeGreaterThanOrEqual(0);
      expect(layout.submit.right).toBeLessThanOrEqual(layout.viewport.width);
      expect(layout.submit.y).toBeGreaterThanOrEqual(0);
      expect(layout.submit.bottom).toBeLessThanOrEqual(layout.viewport.height);
      expect(layout.submit.unobscured).toBe(true);
      if (hasF04VisualEvidenceTarget()) {
        await captureF04VisualEvidence(page, {
          scenario: `auth-login-${visual.id}`,
          viewport,
          runtimeErrors
        });
      }
      expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
    } finally {
      await context.close();
    }
  }
});

test('F04 auth controls remain scroll-reachable in an extremely short viewport', async ({ page }) => {
  await page.setViewportSize({ width: 480, height: 360 });
  await installStartup(page);
  await installAuthFixture(page, freshState({ requiresSetup: false, role: 'Engineer' }));
  await page.goto('/studio/index.html#/login');

  const stage = page.locator('.auth-shell__stage');
  const username = page.getByLabel('用户名');
  const submit = page.getByRole('button', { name: '登录', exact: true });
  await expect(page.locator('[data-auth-shell="ready"]')).toBeVisible();
  await expect(page.locator('[data-product-shell]')).toHaveCount(0);
  await expect(page.locator('.auth-product-preview')).toHaveCount(0);
  await expect(stage).toHaveJSProperty('scrollTop', 0);
  const overflow = await stage.evaluate(element => ({
    horizontal: element.scrollWidth - element.clientWidth,
    vertical: element.scrollHeight - element.clientHeight,
    scrollOwner: getComputedStyle(element).overflowY
  }));
  expect(overflow.horizontal).toBe(0);
  expect(overflow.vertical).toBe(0);
  expect(overflow.scrollOwner).toBe('auto');

  await submit.scrollIntoViewIfNeeded();
  await expect(submit).toBeInViewport();
  await expect(submit).toBeEnabled();
  expect(await submit.evaluate(element => {
    const box = element.getBoundingClientRect();
    const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
    return hit === element || element.contains(hit);
  })).toBe(true);

  await username.scrollIntoViewIfNeeded();
  await expect(username).toBeInViewport();
});

test('F04 startup contract failure stays outside Vue and provides recovery actions', async ({ page }) => {
  const viewport = { width: 1366, height: 768 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  await page.goto('/studio/index.html');

  const diagnostic = page.locator('[data-studio-page="bootstrap-diagnostic"]');
  await expect(diagnostic).toBeVisible();
  await expect(diagnostic.getByRole('heading', { name: 'Studio 启动失败' })).toBeVisible();
  await expect(diagnostic.getByRole('button', { name: '重新加载 Studio' })).toBeVisible();
  await expect(diagnostic.getByRole('button', { name: '复制技术信息' })).toBeVisible();
  await expect(diagnostic.locator('details')).not.toHaveAttribute('open', '');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'startup-failure', viewport, runtimeErrors });
  }
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
});
