import { expect, test, type Page, type Route } from '@playwright/test';
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

const fixtureSchema = 'f02-support-surfaces.v1';

async function fulfill(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, fixtureSchema);
}

async function bootSupportSurfaces(
  page: Page,
  healthStatus = 'Healthy'
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: Object.freeze({
        async writeText(value: string): Promise<void> {
          Object.defineProperty(window, '__SUPPORT_CLIPBOARD__', {
            configurable: true,
            value
          });
        }
      })
    });
  });
  await installF02BrowserStartup(page);
  await page.route('**/health', async route => {
    audit.push(auditF02Request(route.request()));
    await fulfill(route, 200, {
      status: healthStatus,
      port: 5177,
      service: 'ClearVision 本地服务',
      version: '2.8.0'
    });
  });
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    if (url.pathname === '/api/auth/setup-status') {
      await fulfill(route, 200, {
        requiresInitialAdminSetup: false,
        usernameMinLength: 3,
        passwordMinLength: 6,
        requiresUppercase: false,
        requiresLowercase: false,
        requiresDigit: false
      });
      return;
    }
    if (url.pathname === '/api/auth/me') {
      await fulfill(route, 200, {
        userId: 'support-fixture-user',
        username: '现场工程师',
        role: 'Engineer'
      });
      return;
    }
    await fulfill(route, 404, { error: 'NotFound' });
  });
  return audit;
}

test('Diagnostics and About expose current host, service, version, license and support facts', async ({ page }) => {
  const audit = await bootSupportSurfaces(page);
  await page.goto('/studio/index.html#/diagnostics');
  const diagnostics = page.locator('[data-studio-page="diagnostics"]');
  await expect(diagnostics).toBeVisible();
  await expect(diagnostics.getByText('本地服务在线', { exact: true })).toBeVisible();
  await expect(page.getByText('2.8.0', { exact: true })).toBeVisible();
  await expect(diagnostics.locator('details')).not.toHaveAttribute('open', '');
  await diagnostics.getByRole('button', { name: '复制诊断信息' }).click();
  await expect(diagnostics.getByText('诊断信息已复制，可粘贴给技术支持。')).toBeVisible();
  const copied = await page.evaluate(() => (window as typeof window & {
    __SUPPORT_CLIPBOARD__?: string;
  }).__SUPPORT_CLIPBOARD__ ?? '');
  expect(copied).toContain('"serviceVersion": "2.8.0"');
  expect(copied).not.toContain('f02-browser-fixture-token');

  await page.goto('/studio/index.html#/about');
  await expect(page.locator('[data-studio-page="about"]')).toBeVisible();
  await expect(page.getByText('关于 ClearVision Studio', { exact: true })).toBeVisible();
  await expect(page.getByText('许可与支持', { exact: true })).toBeVisible();
  expect(expectGetOnly(audit)).toBe(true);
});

test('Diagnostics makes a degraded local service actionable without opening protocol details', async ({ page }) => {
  const audit = await bootSupportSurfaces(page, 'Degraded');
  await page.goto('/studio/index.html#/diagnostics');
  const diagnostics = page.locator('[data-studio-page="diagnostics"]');
  await expect(diagnostics.getByText('本地服务需要处理', { exact: true })).toBeVisible();
  await expect(diagnostics.getByText('本地服务状态：Degraded', { exact: true })).toBeVisible();
  await expect(diagnostics.locator('details')).not.toHaveAttribute('open', '');
  expect(expectGetOnly(audit)).toBe(true);
});

for (const visual of [
  { id: 'diagnostics-b0', surface: 'diagnostics', width: 1920, height: 1080, theme: 'light', density: 'compact' },
  { id: 'diagnostics-b1', surface: 'diagnostics', width: 1536, height: 864, theme: 'light', density: 'compact' },
  { id: 'diagnostics-b2', surface: 'diagnostics', width: 1366, height: 768, theme: 'light', density: 'compact' },
  { id: 'diagnostics-b3', surface: 'diagnostics', width: 1920, height: 1080, theme: 'dark', density: 'compact' },
  { id: 'diagnostics-b4-light', surface: 'diagnostics', width: 1920, height: 1080, theme: 'light', density: 'comfortable' },
  { id: 'diagnostics-b4-dark', surface: 'diagnostics', width: 1920, height: 1080, theme: 'dark', density: 'comfortable' },
  { id: 'about-b0', surface: 'about', width: 1920, height: 1080, theme: 'light', density: 'compact' },
  { id: 'about-b1', surface: 'about', width: 1536, height: 864, theme: 'light', density: 'compact' },
  { id: 'about-b2', surface: 'about', width: 1366, height: 768, theme: 'light', density: 'compact' },
  { id: 'about-b3', surface: 'about', width: 1920, height: 1080, theme: 'dark', density: 'compact' },
  { id: 'about-b4-light', surface: 'about', width: 1920, height: 1080, theme: 'light', density: 'comfortable' },
  { id: 'about-b4-dark', surface: 'about', width: 1920, height: 1080, theme: 'dark', density: 'comfortable' }
] as const) {
  test(`captures ${visual.id} Browser fixture evidence`, async ({ page }) => {
    test.skip(!hasF02VisualEvidenceTarget(), 'F02 visual evidence output was not requested.');
    await page.setViewportSize({ width: visual.width, height: visual.height });
    await installF02VisualPreferences(page, visual.theme, visual.density);
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const audit = await bootSupportSurfaces(page);
    await page.goto(`/studio/index.html#/${visual.surface}`);
    await expect(page.locator(`[data-studio-page="${visual.surface}"]`)).toBeVisible();
    await expect(page.getByText('2.8.0', { exact: true })).toBeVisible();
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
