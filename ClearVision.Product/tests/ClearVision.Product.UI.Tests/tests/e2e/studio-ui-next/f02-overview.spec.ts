import { expect, Page, Route, test } from '@playwright/test';
import {
  auditF02Request,
  captureF02VisualEvidence,
  createF02RuntimeErrorAudit,
  expectGetOnly,
  fulfillF02Json,
  hasF02VisualEvidenceTarget,
  installF02BrowserStartup,
  installF02VisualPreferences,
  type F02MethodAuditEntry
} from './f02-browser-fixture';
import {
  captureR2FinalMatrixGroup,
  prepareR2FinalMatrixPage,
  r2Viewport,
  type R2FinalVariant
} from './r2-visual/r2-final-matrix-evidence';

const fixtureSchemaVersion = 'f02-overview.v1';
const projectId = '11111111-1111-4111-8111-111111111111';

async function fulfillJson(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, fixtureSchemaVersion);
}

async function bootOverview(
  page: Page,
  authenticated = true,
  recentProjectsStatus: 200 | 401 | 403 = authenticated ? 200 : 401,
  role: 'Admin' | 'Engineer' | 'Operator' = 'Operator'
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  await installF02BrowserStartup(page);

  await page.route('**/health', async route => {
    const request = route.request();
    audit.push(auditF02Request(request));
    await fulfillJson(route, 200, { status: 'Healthy', port: 5177 });
  });
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    if (url.pathname === '/api/auth/setup-status') {
      await fulfillJson(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
      return;
    }
    if (url.pathname === '/api/auth/me') {
      await fulfillJson(route, authenticated ? 200 : 401, authenticated
        ? { userId: 'fixture-user', username: 'fixture-operator', role }
        : { error: 'Unauthorized' });
      return;
    }
    if (url.pathname === '/api/projects/recent') {
      await fulfillJson(route, recentProjectsStatus, recentProjectsStatus === 200
        ? [{
            id: projectId,
            name: '瓶盖检测',
            description: 'Browser fixture 最近工程',
            version: '1.0.0',
            persistenceRevision: 12,
            createdAt: '2026-07-15T01:00:00Z',
            modifiedAt: '2026-07-15T02:00:00Z',
            lastOpenedAt: '2026-07-15T03:00:00Z',
            flow: { operators: new Array(99).fill({}), connections: [] }
          }]
        : { error: recentProjectsStatus === 403 ? 'Forbidden' : 'Unauthorized' });
      return;
    }
    await fulfillJson(route, 404, { error: 'NotFound' });
  });

  await page.goto('/studio/index.html#/overview');
  if (authenticated) {
    await expect(page.locator('[data-capability="overview"]')).toBeVisible();
  } else {
    await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  }
  return audit;
}

test('Overview consumes shared health/session and recent-project projections with GET-only traffic', async ({ page }) => {
  const audit = await bootOverview(page);

  await expect(page.getByText('健康', { exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: '当前会话' }).getByText('fixture-operator', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: '瓶盖检测' })).toBeVisible();
  await expect(page.getByRole('link', { name: '继续配置' })).toHaveAttribute(
    'href', `#/projects/${projectId}/workspace`
  );
  await expect.poll(() => audit.some(entry => entry.path === '/health')).toBe(true);
  await expect.poll(() => audit.some(entry => entry.path === '/api/auth/me')).toBe(true);
  await expect.poll(() => audit.some(entry => entry.path.startsWith('/api/projects/recent'))).toBe(true);
  expect(expectGetOnly(audit)).toBe(true);
});

test('Overview does not mount ProductRuntime when the seeded session is rejected', async ({ page }) => {
  const audit = await bootOverview(page, false);

  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(page.locator('[data-product-shell]')).toHaveCount(0);
  await expect(page.locator('[data-capability="overview"]')).toHaveCount(0);
  expect(expectGetOnly(audit)).toBe(true);
});

test('Product shell persists theme/density preferences and keeps Labs out of navigation', async ({ page }) => {
  await bootOverview(page);

  const appearanceTrigger = page.locator('[data-product-appearance] button[aria-haspopup="menu"]');
  await appearanceTrigger.click();
  await page.getByRole('menuitemcheckbox', { name: '深色', exact: true }).click();
  await appearanceTrigger.click();
  await page.getByRole('menuitemcheckbox', { name: '舒适', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable');
  await page.reload();

  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable');
  await expect(page.locator('[data-product-nav^="/labs"]')).toHaveCount(0);
  await expect(page.getByRole('navigation', { name: '产品主导航' })).not.toContainText('实验室');
});

test('Product shell exposes one main landmark and keeps disclosure menus mutually exclusive', async ({ page }) => {
  await bootOverview(page);

  await expect(page.getByRole('main')).toHaveCount(1);
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
  const skipLink = page.getByRole('link', { name: '跳到主要内容' });
  await skipLink.focus();
  await expect(skipLink).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('main')).toBeFocused();

  const appearance = page.locator('[data-product-appearance]');
  const trigger = appearance.locator('button[aria-haspopup="menu"]');
  await trigger.focus();
  await page.keyboard.press('Enter');
  await expect(trigger).toHaveAttribute('aria-expanded', 'true');
  await page.keyboard.press('Escape');
  await expect(trigger).toHaveAttribute('aria-expanded', 'false');
  await expect(trigger).toBeFocused();

  const more = page.locator('[data-product-more]');
  const moreTrigger = more.locator('button[aria-haspopup="menu"]');
  await trigger.click();
  await expect(trigger).toHaveAttribute('aria-expanded', 'true');
  await moreTrigger.click();
  await expect(trigger).toHaveAttribute('aria-expanded', 'false');
  await expect(moreTrigger).toHaveAttribute('aria-expanded', 'true');
  await page.keyboard.press('Escape');
  await expect(moreTrigger).toHaveAttribute('aria-expanded', 'false');
  await expect(moreTrigger).toBeFocused();

  await trigger.click();
  await expect(trigger).toHaveAttribute('aria-expanded', 'true');
  await page.getByRole('main').click({ position: { x: 12, y: 12 } });
  await expect(trigger).toHaveAttribute('aria-expanded', 'false');

  await page.locator('[data-product-nav="/projects"]').click();
  await expect(page.locator('[data-capability="projects-read"]')).toBeVisible();
  await expect(page.getByRole('main')).toBeFocused();
});

test('Product shell keeps reduced motion and short-viewport keyboard targets visible', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 1366, height: 600 });
  await bootOverview(page);

  const duration = await page.locator('html').evaluate(element =>
    getComputedStyle(element).getPropertyValue('--cv-motion-duration-normal').trim()
  );
  expect(['0ms', '0s']).toContain(duration);

  const projectsLink = page.locator('[data-product-nav="/projects"]');
  await projectsLink.focus();
  const projectsBox = await projectsLink.boundingBox();
  expect(projectsBox).not.toBeNull();
  expect(projectsBox!.y).toBeGreaterThanOrEqual(0);
  expect(projectsBox!.y + projectsBox!.height).toBeLessThanOrEqual(600);

  const appearance = page.locator('[data-product-appearance] button[aria-haspopup="menu"]');
  await appearance.focus();
  const appearanceBox = await appearance.boundingBox();
  expect(appearanceBox).not.toBeNull();
  expect(appearanceBox!.y).toBeGreaterThanOrEqual(0);
  expect(appearanceBox!.y + appearanceBox!.height).toBeLessThanOrEqual(600);
  await appearance.click();
  const appearancePopover = await page.locator('[role="menu"][aria-label="外观设置"]').boundingBox();
  expect(appearancePopover).not.toBeNull();
  expect(appearancePopover!.x).toBeGreaterThanOrEqual(0);
  expect(appearancePopover!.y).toBeGreaterThanOrEqual(0);
  expect(appearancePopover!.x + appearancePopover!.width).toBeLessThanOrEqual(1366);
  expect(appearancePopover!.y + appearancePopover!.height).toBeLessThanOrEqual(600);

  const more = page.locator('[data-product-more] button[aria-haspopup="menu"]');
  await more.click();
  const morePopover = await page.locator('[role="menu"][aria-label="更多产品入口"]').boundingBox();
  expect(morePopover).not.toBeNull();
  expect(morePopover!.x).toBeGreaterThanOrEqual(0);
  expect(morePopover!.y).toBeGreaterThanOrEqual(0);
  expect(morePopover!.x + morePopover!.width).toBeLessThanOrEqual(1366);
  expect(morePopover!.y + morePopover!.height).toBeLessThanOrEqual(600);
  const overflow = await page.locator('html').evaluate(element => element.scrollWidth - element.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
});

test('Diagnostics, About and 404 remain inside the single product shell', async ({ page }) => {
  await bootOverview(page, true, 200, 'Engineer');

  await page.goto('/studio/index.html#/diagnostics');
  await expect(page.locator('[data-studio-page="diagnostics"]')).toBeVisible();
  await expect(page.locator('[data-product-shell]')).toHaveCount(1);

  await page.goto('/studio/index.html#/about');
  await expect(page.locator('[data-studio-page="about"]')).toBeVisible();
  await expect(page.getByText('产品版本', { exact: true })).toBeVisible();
  await expect(page.getByText('桌面宿主版本', { exact: true })).toBeVisible();
  await expect(page.getByText('本地服务版本', { exact: true })).toBeVisible();
  await expect(page.getByText('启动模式', { exact: true })).toBeVisible();
  await expect(page.getByText('请联系系统管理员或实施交付方', { exact: true })).toBeVisible();

  await page.goto('/studio/index.html#/not-a-product-route');
  await expect(page.locator('[data-studio-page="not-found"]')).toBeVisible();
  await expect(page.getByRole('heading', { name: '未找到此页面' })).toBeVisible();
});

for (const visual of [
  { id: 'overview-light-compact', width: 1366, height: 768, theme: 'light', density: 'compact' },
  { id: 'overview-light-comfortable', width: 1920, height: 1080, theme: 'light', density: 'comfortable' },
  { id: 'overview-short-light-compact', width: 1366, height: 600, theme: 'light', density: 'compact' }
] as const) {
  test(`captures ${visual.id} Browser fixture evidence`, async ({ page }) => {
    test.skip(!hasF02VisualEvidenceTarget(), 'F02 visual evidence output was not requested.');
    await page.setViewportSize({ width: visual.width, height: visual.height });
    await installF02VisualPreferences(page, visual.theme, visual.density);
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const audit = await bootOverview(page);
    await expect(page.locator('html')).toHaveAttribute('data-theme', visual.theme);
    await expect(page.locator('html')).toHaveAttribute('data-density', visual.density);
    await captureF02VisualEvidence(page, {
      scenario: visual.id,
      viewport: { width: visual.width, height: visual.height },
      theme: visual.theme,
      density: visual.density,
      requests: audit,
      runtimeErrors
    });
    expect(expectGetOnly(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}

for (const state of [
  { id: 'diagnostics', route: '/diagnostics', selector: '[data-studio-page="diagnostics"]', role: 'Engineer' as const },
  { id: 'about', route: '/about', selector: '[data-studio-page="about"]', role: 'Operator' as const },
  { id: 'not-found', route: '/not-a-product-route', selector: '[data-studio-page="not-found"]', role: 'Operator' as const }
] as const) {
  test(`captures ${state.id} product-state evidence`, async ({ page }) => {
    test.skip(!hasF02VisualEvidenceTarget(), 'F02 visual evidence output was not requested.');
    await page.setViewportSize({ width: 1366, height: 768 });
    await installF02VisualPreferences(page, 'light', 'compact');
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const audit = await bootOverview(page, true, 200, state.role);
    await page.goto(`/studio/index.html#${state.route}`);
    await expect(page.locator(state.selector)).toBeVisible();
    await captureF02VisualEvidence(page, {
      scenario: state.id,
      viewport: { width: 1366, height: 768 },
      theme: 'light',
      density: 'compact',
      requests: audit,
      runtimeErrors
    });
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}

for (const variant of ['B0', 'B2', 'EXCEPTION'] as const satisfies readonly R2FinalVariant[]) {
  test(`@r2-final S01 ${variant} projects the F02 shell and Overview state`, async ({ page }) => {
    await page.setViewportSize(r2Viewport(variant));
    await installF02VisualPreferences(page, 'light', 'compact');
    const runtime = await prepareR2FinalMatrixPage(page, {
      expectedHttpErrors: variant === 'EXCEPTION'
        ? [{ method: 'GET', path: '/api/projects/recent', status: 403 }]
        : []
    });
    const audit = await bootOverview(page, true, variant === 'EXCEPTION' ? 403 : 200, 'Engineer');
    await expect(page.locator('[data-product-shell]')).toHaveCount(1);
    await expect(page.locator('[data-capability="overview"]')).toBeVisible();
    if (variant === 'EXCEPTION') {
      await expect(page.locator('[data-page-state="forbidden"]')).toBeVisible();
    } else {
      await expect(page.getByRole('link', { name: '瓶盖检测' })).toBeVisible();
      await expect(page.getByRole('region', { name: '当前会话' })).toContainText('fixture-operator');
    }
    expect(expectGetOnly(audit)).toBe(true);
    await captureR2FinalMatrixGroup(page, {
      scene: 'S01', variant, route: '#/overview',
      state: variant === 'EXCEPTION' ? 'recent-projects-forbidden' : 'healthy',
      role: 'Engineer', owner: 'F02-overview', runtime,
      requiredCriticalActions: [variant === 'EXCEPTION'
        ? '.overview-page__quick-links a[href="#/projects"]'
        : '.overview-page__continue']
    });
  });
}

for (const state of [
  {
    id: 'unauthorized',
    authenticated: false,
    recentProjectsStatus: 401,
    selector: '[data-auth-page="login"]'
  },
  {
    id: 'forbidden',
    authenticated: true,
    recentProjectsStatus: 403,
    selector: '[data-page-state="forbidden"]'
  }
] as const) {
  test(`captures ${state.id} authorization-state evidence`, async ({ page }) => {
    test.skip(!hasF02VisualEvidenceTarget(), 'F02 visual evidence output was not requested.');
    await page.setViewportSize({ width: 1366, height: 768 });
    await installF02VisualPreferences(page, 'light', 'compact');
    const runtimeErrors = createF02RuntimeErrorAudit(page, [state.recentProjectsStatus]);
    const audit = await bootOverview(page, state.authenticated, state.recentProjectsStatus);
    await expect(page.locator(state.selector)).toBeVisible();
    await captureF02VisualEvidence(page, {
      scenario: state.id,
      viewport: { width: 1366, height: 768 },
      theme: 'light',
      density: 'compact',
      requests: audit,
      runtimeErrors,
      requireVisualPreferenceProjection: state.authenticated,
      expectedHttpStatuses: [state.recentProjectsStatus]
    });
    expect(expectGetOnly(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}
