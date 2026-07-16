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

const fixtureSchemaVersion = 'f02-overview.v1';
const projectId = '11111111-1111-4111-8111-111111111111';

async function fulfillJson(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, fixtureSchemaVersion);
}

async function bootOverview(
  page: Page,
  authenticated = true,
  recentProjectsStatus: 200 | 401 | 403 = authenticated ? 200 : 401
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
    if (url.pathname === '/api/auth/me') {
      await fulfillJson(route, authenticated ? 200 : 401, authenticated
        ? { userId: 'fixture-user', username: 'fixture-operator', role: 'Operator' }
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
    await expect(page.locator('[data-product-state="unauthorized"]')).toBeVisible();
  }
  return audit;
}

test('Overview consumes shared health/session and recent-project projections with GET-only traffic', async ({ page }) => {
  const audit = await bootOverview(page);

  await expect(page.getByText('健康', { exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: '当前会话' }).getByText('fixture-operator', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: '瓶盖检测' })).toBeVisible();
  await expect.poll(() => audit.some(entry => entry.path === '/health')).toBe(true);
  await expect.poll(() => audit.some(entry => entry.path === '/api/auth/me')).toBe(true);
  await expect.poll(() => audit.some(entry => entry.path.startsWith('/api/projects/recent'))).toBe(true);
  expect(expectGetOnly(audit)).toBe(true);
});

test('Overview keeps public system status visible when the seeded session is rejected', async ({ page }) => {
  const audit = await bootOverview(page, false);

  await expect(page.getByText('健康', { exact: true })).toBeVisible();
  await expect(page.locator('[data-product-state="unauthorized"]')).toBeVisible();
  expect(expectGetOnly(audit)).toBe(true);
});

test('Product shell persists theme/density preferences and keeps Labs out of navigation', async ({ page }) => {
  await bootOverview(page);

  await page.locator('[data-product-appearance] > summary').click();
  await page.getByRole('button', { name: '深色', exact: true }).click();
  await page.getByRole('button', { name: '舒适', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable');
  await page.reload();

  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable');
  await expect(page.locator('[data-product-nav^="/labs"]')).toHaveCount(0);
  await expect(page.getByRole('navigation', { name: '产品主导航' })).not.toContainText('实验室');
});

test('Product shell exposes one main landmark, skip navigation and disclosure focus recovery', async ({ page }) => {
  await bootOverview(page);

  await expect(page.getByRole('main')).toHaveCount(1);
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
  await page.keyboard.press('Tab');
  const skipLink = page.getByRole('link', { name: '跳到主要内容' });
  await expect(skipLink).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('main')).toBeFocused();

  const appearance = page.locator('[data-product-appearance]');
  const trigger = appearance.locator('summary');
  await trigger.focus();
  await page.keyboard.press('Enter');
  await expect(appearance).toHaveAttribute('open', '');
  await page.keyboard.press('Escape');
  await expect(appearance).not.toHaveAttribute('open', '');
  await expect(trigger).toBeFocused();

  await page.getByRole('link', { name: /关于/ }).click();
  await expect(page.locator('[data-studio-page="about"]')).toBeVisible();
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

  const aboutLink = page.locator('[data-product-nav="/about"]');
  await aboutLink.focus();
  const aboutBox = await aboutLink.boundingBox();
  expect(aboutBox).not.toBeNull();
  expect(aboutBox!.y).toBeGreaterThanOrEqual(0);
  expect(aboutBox!.y + aboutBox!.height).toBeLessThanOrEqual(600);

  const appearance = page.locator('[data-product-appearance] > summary');
  await appearance.focus();
  const appearanceBox = await appearance.boundingBox();
  expect(appearanceBox).not.toBeNull();
  expect(appearanceBox!.y).toBeGreaterThanOrEqual(0);
  expect(appearanceBox!.y + appearanceBox!.height).toBeLessThanOrEqual(600);
  const overflow = await page.locator('html').evaluate(element => element.scrollWidth - element.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
});

test('Diagnostics, About and 404 remain inside the single product shell', async ({ page }) => {
  await bootOverview(page);

  await page.goto('/studio/index.html#/diagnostics');
  await expect(page.locator('[data-studio-page="diagnostics"]')).toBeVisible();
  await expect(page.locator('[data-product-shell]')).toHaveCount(1);

  await page.goto('/studio/index.html#/about');
  await expect(page.locator('[data-studio-page="about"]')).toBeVisible();
  await expect(page.getByText('预置会话 authenticated preview', { exact: true })).toBeVisible();

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
  { id: 'diagnostics', route: '/diagnostics', selector: '[data-studio-page="diagnostics"]' },
  { id: 'about', route: '/about', selector: '[data-studio-page="about"]' },
  { id: 'not-found', route: '/not-a-product-route', selector: '[data-studio-page="not-found"]' }
] as const) {
  test(`captures ${state.id} product-state evidence`, async ({ page }) => {
    test.skip(!hasF02VisualEvidenceTarget(), 'F02 visual evidence output was not requested.');
    await page.setViewportSize({ width: 1366, height: 768 });
    await installF02VisualPreferences(page, 'light', 'compact');
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const audit = await bootOverview(page);
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

for (const state of [
  {
    id: 'unauthorized',
    authenticated: false,
    recentProjectsStatus: 401,
    selector: '[data-product-state="unauthorized"]'
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
      expectedHttpStatuses: [state.recentProjectsStatus]
    });
    expect(expectGetOnly(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}
