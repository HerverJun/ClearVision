import { expect, test } from '@playwright/test';
import {
  collectR2DomReport,
  installR2BrowserScenario,
  installR2ReadOnlyDispatcher
} from './r2-in-app-browser-fixture';
import {
  captureR2FinalMatrixGroup,
  r2Viewport,
  type R2FinalVariant
} from './r2-final-matrix-evidence';

const viewports = Object.freeze([
  Object.freeze({ id: 'B0', width: 1920, height: 1080 }),
  Object.freeze({ id: 'B2', width: 1366, height: 768 })
]);

for (const viewport of viewports) {
  test(`R2 ${viewport.id} login remains isolated, centered, and operational`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const audit = await installR2ReadOnlyDispatcher(page);
    await page.goto('/studio/index.html#/login');
    await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
    await expect(page.getByRole('heading', { name: '登录' })).toBeVisible();
    await expect(page.getByRole('button', { name: '登录', exact: true })).toBeVisible();
    await expect(page.locator('main')).toHaveCount(1);
    await expect(page.locator('[data-product-shell]')).toHaveCount(0);
    await expect(page.locator('.auth-product-preview')).toHaveCount(0);
    const report = await collectR2DomReport(page, ['.auth-form__submit']);
    expect(report.horizontalOverflow).toBe(0);
    expect(report.mainBox).toEqual({ x: 0, y: 0, width: viewport.width, height: viewport.height });
    expect(report.criticalActions).toEqual([
      expect.objectContaining({
        selector: '.auth-form__submit',
        inViewport: true,
        reachable: true,
        enabled: true,
        unobscured: true
      })
    ]);
    expect(audit.consoleErrors).toEqual([]);
    expect(audit.pageErrors).toEqual([]);
    expect(audit.failedRequests).toEqual([]);
    expect(audit.httpErrors).toEqual([]);
    expect(audit.unexpectedWrites).toEqual([]);
  });

  test(`R2 ${viewport.id} Design Lab projects stable surface, theme, density, and states`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await installR2BrowserScenario(page, {
      routeStateId: 's01-overview-main',
      theme: 'light',
      density: 'compact',
      reducedMotion: false
    });
    const audit = await installR2ReadOnlyDispatcher(page);
    await page.goto('/studio/index.html#/labs/design');
    await expect(page.locator('[data-design-lab="ready"]')).toBeVisible();
    await expect(page.locator('[data-design-state-matrix]')).toBeVisible();
    await expect(page.locator('[data-design-status-palette]')).toBeVisible();
    await page.locator('[data-design-theme="dark"]').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await page.locator('[data-design-density="comfortable"]').click();
    await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable');
    const report = await collectR2DomReport(page);
    expect(report.horizontalOverflow).toBe(0);
    expect(audit.consoleErrors).toEqual([]);
    expect(audit.pageErrors).toEqual([]);
    expect(audit.failedRequests).toEqual([]);
    expect(audit.unexpectedWrites).toEqual([]);
  });
}

test('R2 login validation and auth error remain immediate and recoverable', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  const expectedAuthError = 'POST /api/auth/login: 401';
  const audit = await installR2ReadOnlyDispatcher(page, 'Engineer', [
    { method: 'POST', path: '/api/auth/login', status: 401 }
  ]);
  await page.goto('/studio/index.html#/login');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByLabel('用户名')).toBeFocused();
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await page.getByLabel('用户名').fill('现场工程师');
  await page.locator('#login-password').fill('wrong-password');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByText('用户名或密码错误')).toBeVisible();
  expect(audit.consoleErrors).toEqual([]);
  expect(audit.pageErrors).toEqual([]);
  expect(audit.failedRequests).toEqual([]);
  expect(audit.httpErrors).toEqual([]);
  expect(audit.observedExpectedHttpErrors).toEqual([expectedAuthError]);
  expect(audit.unexpectedWrites).toEqual([]);
});

for (const variant of ['B0', 'B2', 'EXCEPTION'] as const satisfies readonly R2FinalVariant[]) {
  test(`@r2-final S00 ${variant} presents or rejects through the F04 auth projection`, async ({ page }) => {
    const viewport = r2Viewport(variant);
    await page.setViewportSize(viewport);
    const audit = await installR2ReadOnlyDispatcher(
      page,
      'Engineer',
      variant === 'EXCEPTION'
        ? [{ method: 'POST', path: '/api/auth/login', status: 401 }]
        : []
    );
    await page.goto('/studio/index.html#/login');
    await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
    await expect(page.getByRole('heading', { name: '登录' })).toBeVisible();
    await expect(page.getByRole('button', { name: '登录', exact: true })).toBeVisible();

    if (variant === 'EXCEPTION') {
      await page.getByLabel('用户名').fill('现场工程师');
      await page.locator('#login-password').fill('wrong-password');
      await page.getByRole('button', { name: '登录', exact: true }).click();
      await expect(page.getByText('用户名或密码错误')).toBeVisible();
    }

    await captureR2FinalMatrixGroup(page, {
      scene: 'S00',
      variant,
      route: '#/login',
      state: variant === 'EXCEPTION' ? 'auth-error' : 'login',
      role: 'Public',
      owner: 'F04-auth',
      allowedWrites: variant === 'EXCEPTION' ? ['POST /api/auth/login'] : [],
      runtime: audit,
      requiredCriticalActions: ['.auth-form__submit']
    });
  });
}
