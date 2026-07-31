import { expect, test, type Page } from '@playwright/test';
import { installF02VisualPreferences } from './f02-browser-fixture';
import {
  captureF06Evidence,
  f06ProjectId,
  f06ProjectSessionId,
  f06SessionId,
  f06UnboundHistorySessionId,
  installF06Fixture,
  type F06BrowserAudit,
  type F06BrowserFixtureOptions
} from './f06-ai-fixture';

type Density = 'compact' | 'comfortable';
type Viewport = Readonly<{ width: number; height: number }>;

async function boot(
  page: Page,
  viewport: Viewport,
  density: Density,
  route: string,
  options: F06BrowserFixtureOptions = {}
): Promise<F06BrowserAudit> {
  await page.setViewportSize(viewport);
  await installF02VisualPreferences(page, 'light', density);
  const audit = await installF06Fixture(page, options);
  await page.goto(`/studio/index.html#${route}`);
  return audit;
}

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const projection = await page.evaluate(() => ({
    documentOverflow: Math.max(
      document.documentElement.scrollWidth - document.documentElement.clientWidth,
      document.body.scrollWidth - document.body.clientWidth
    ),
    dialogOffenders: [...document.querySelectorAll<HTMLElement>('[role="dialog"], [role="dialog"] *')]
      .filter(element => element.getClientRects().length > 0 && element.scrollWidth - element.clientWidth > 1)
      .map(element => ({
        element: `${element.tagName.toLowerCase()}${element.className ? `.${String(element.className).trim().replace(/\s+/g, '.')}` : ''}`,
        overflow: element.scrollWidth - element.clientWidth
      }))
  }));
  expect(projection.documentOverflow).toBeLessThanOrEqual(1);
  expect(projection.dialogOffenders).toEqual([]);
}

function expectNoRuntimeErrors(audit: F06BrowserAudit): void {
  expect(audit.consoleErrors).toEqual([]);
  expect(audit.pageErrors).toEqual([]);
}

async function openHistory(page: Page) {
  await page.getByRole('button', { name: '打开历史与恢复' }).click();
  const drawer = page.getByRole('dialog', { name: '历史与恢复' });
  await expect(drawer).toBeVisible();
  return drawer;
}

async function openDiagnostics(page: Page) {
  await page.getByRole('button', { name: '打开公开诊断' }).click();
  const drawer = page.getByRole('dialog', { name: '公开诊断' });
  await expect(drawer).toBeVisible();
  return drawer;
}

test('G5 drawers default closed and expose keyboard-safe empty history plus redacted public diagnostics', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 } as const;
  const audit = await boot(page, viewport, 'comfortable', '/ai', { historyMode: 'empty' });
  const historyTrigger = page.getByRole('button', { name: '打开历史与恢复' });
  const diagnosticsTrigger = page.getByRole('button', { name: '打开公开诊断' });

  await expect(historyTrigger).toHaveAttribute('aria-expanded', 'false');
  await expect(diagnosticsTrigger).toHaveAttribute('aria-expanded', 'false');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await captureF06Evidence(page, audit, 'g5-drawers-default-closed', viewport, 'comfortable');

  const history = await openHistory(page);
  await expect(history.getByRole('button', { name: '关闭抽屉' })).toBeFocused();
  await expect(history.getByRole('heading', { name: '暂无历史会话' })).toBeVisible();
  await history.getByRole('tab', { name: '运行' }).click();
  await expect(history.getByRole('heading', { name: '暂无运行记录' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(history).toHaveCount(0);
  await expect(historyTrigger).toBeFocused();

  const diagnostics = await openDiagnostics(page);
  await expect(diagnostics.getByRole('button', { name: '关闭抽屉' })).toBeFocused();
  await expect(diagnostics.getByRole('heading', { name: '阶段时间线' })).toBeVisible();
  await expect(diagnostics.getByText('当前无待恢复运行')).toBeVisible();
  await expect(diagnostics.getByText('当前没有公开阻断或警告。')).toBeVisible();
  for (const sensitive of [
    'SYSTEM_PROMPT_SENTINEL', 'RAW_TOOL_PAYLOAD_SENTINEL', 'sk-private-f06',
    'C:\\factory\\secret', '10.23.45.67', '192.168.88.9'
  ]) {
    await expect(page.getByText(sensitive, { exact: false })).toHaveCount(0);
  }
  await captureF06Evidence(page, audit, 'g5-diagnostics-open-redacted', viewport, 'comfortable');
  await expectNoHorizontalOverflow(page);
  await page.keyboard.press('Tab');
  await expect(diagnostics.getByRole('button', { name: '关闭抽屉' })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(diagnosticsTrigger).toBeFocused();
  await expectNoHorizontalOverflow(page);
  expectNoRuntimeErrors(audit);
});

test('G5 long owner history paginates Sessions and Runs without exposing internal identities', async ({ page }) => {
  const viewport = { width: 1366, height: 768 } as const;
  const audit = await boot(page, viewport, 'compact', `/ai?sessionId=${f06SessionId}`, {
    historyMode: 'long'
  });
  const history = await openHistory(page);

  await expect(history.getByText('第 1–10 项，共 25 项')).toBeVisible();
  await expect(history.locator('ul.ai-history__list > li')).toHaveCount(10);
  await expect(history.getByText(f06ProjectSessionId, { exact: false })).toHaveCount(0);
  await history.getByRole('button', { name: '第 2 页' }).click();
  await expect(history.getByText('第 11–20 项，共 25 项')).toBeVisible();
  await expect.poll(() => audit.requests.some(item =>
    item.path === '/api/ai/sessions?offset=10&limit=10')).toBe(true);

  await history.getByRole('tab', { name: '运行' }).click();
  await expect(history.getByText('第 1–10 项，共 23 项')).toBeVisible();
  await expect(history.locator('ol.ai-history__list > li')).toHaveCount(10);
  await expect(history.getByText('PUBLIC_VALIDATION_RECOMMENDATION_WITHOUT_INTERNAL_IDENTIFIERS', { exact: false })).toBeVisible();
  await history.getByRole('button', { name: '第 2 页' }).click();
  await expect(history.getByText('第 11–20 项，共 23 项')).toBeVisible();
  await expectNoHorizontalOverflow(page);
  await captureF06Evidence(page, audit, 'g5-history-long-paged', viewport, 'compact');
  await history.getByRole('button', { name: '当前会话' }).click();
  await expect.poll(() => audit.requests.some(item =>
    item.path.includes(`sessionId=${f06SessionId}`))).toBe(true);

  await expectNoHorizontalOverflow(page);
  expectNoRuntimeErrors(audit);
});

test('G5 restores project-bound and unbound canonical Sessions through one disposed owner at a time', async ({ page }) => {
  const viewport = { width: 1366, height: 768 } as const;
  const audit = await boot(page, viewport, 'compact', `/ai?sessionId=${f06SessionId}`, {
    historyMode: 'long'
  });
  let history = await openHistory(page);
  const projectSession = history.locator('li.ai-history__item').filter({
    hasText: '需从该会话绑定的工程入口恢复。'
  }).first();
  await projectSession.getByRole('button', { name: '前往绑定工程' }).click();

  await expect(page).toHaveURL(new RegExp(
    `#\/projects\/${f06ProjectId}\/ai\\?sessionId=${f06ProjectSessionId}$`
  ));
  await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(1);
  await expect(page.getByText('会话 revision 29')).toBeVisible();
  await expect(page.getByRole('dialog', { name: '历史与恢复' })).toHaveCount(0);
  await expect(page.locator('[data-ai-owner-phase]')).toHaveAttribute('data-ai-owner-stream-count', '0');
  await captureF06Evidence(page, audit, 'g5-session-restored-project', viewport, 'compact');

  history = await openHistory(page);
  const unboundSession = history.locator('li.ai-history__item').filter({ hasText: '会话版本 28' });
  await unboundSession.getByRole('button', { name: '前往独立工作台' }).click();
  await expect(page).toHaveURL(new RegExp(`#\/ai\\?sessionId=${f06UnboundHistorySessionId}$`));
  await expect(page.getByText('会话 revision 28')).toBeVisible();
  await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(1);
  await expect(page.locator('[data-ai-owner-phase]')).toHaveAttribute('data-ai-owner-subscription-count', '0');
  await captureF06Evidence(page, audit, 'g5-session-switched-unbound', viewport, 'compact');
  expectNoRuntimeErrors(audit);
});

test('G5 rejects late history responses after a Session route switch', async ({ page }) => {
  const audit = await boot(page, { width: 1366, height: 768 }, 'compact', `/ai?sessionId=${f06SessionId}`, {
    historyMode: 'long', holdHistory: true
  });
  await page.getByRole('button', { name: '打开历史与恢复' }).click();
  await expect.poll(() => audit.requests.filter(item =>
    item.path.startsWith('/api/ai/sessions?') || item.path.startsWith('/api/ai/agent-runs?')).length
  ).toBe(2);

  await page.evaluate(sessionId => {
    window.location.hash = `#/ai?sessionId=${sessionId}`;
  }, f06UnboundHistorySessionId);
  await expect(page).toHaveURL(new RegExp(`#\/ai\\?sessionId=${f06UnboundHistorySessionId}$`));
  await expect(page.getByText('会话 revision 28')).toBeVisible();
  audit.releaseHistory();
  await expect(page.getByRole('dialog', { name: '历史与恢复' })).toHaveCount(0);
  await expect(page.getByText('会话 revision 28')).toBeVisible();
  await expect(page.locator('[data-ai-owner-phase]')).toHaveAttribute('data-ai-owner-request-count', '0');
  expectNoRuntimeErrors(audit);
});

test('G5 Session delete fails closed for an active Build and never cascades to Project', async ({ page }) => {
  const viewport = { width: 1366, height: 768 } as const;
  const audit = await boot(page, viewport, 'compact', `/ai?sessionId=${f06SessionId}`, {
    historyMode: 'long', historyDelete: 'blocked'
  });
  const history = await openHistory(page);
  await history.getByRole('button', { name: '删除工程绑定会话' }).first().click();
  const confirmation = page.getByRole('dialog', { name: '删除会话' });
  await expect(confirmation.getByRole('button', { name: '删除会话' })).toBeFocused();
  await confirmation.getByRole('button', { name: '删除会话' }).click();
  await expect(confirmation.getByRole('button', { name: '取消' })).toBeEnabled();
  await confirmation.getByRole('button', { name: '取消' }).click();
  await expect(history.getByText('删除已阻断')).toBeVisible();
  await expect(history.getByText('会话仍有关联的活动构建；请等待终态并完成恢复后再删除。')).toBeVisible();
  expect(audit.requests.filter(item => item.method === 'DELETE' &&
    item.path.startsWith('/api/ai/sessions/'))).toHaveLength(1);
  expect(audit.requests.filter(item => item.method === 'DELETE' &&
    item.path.startsWith('/api/projects/'))).toEqual([]);
  await captureF06Evidence(page, audit, 'g5-session-delete-blocked', viewport, 'compact');
  await expectNoHorizontalOverflow(page);
  expectNoRuntimeErrors(audit);
});

test('G5 reconciles a lost Session delete response by its original mutation identity', async ({ page }) => {
  const audit = await boot(page, { width: 1920, height: 1080 }, 'comfortable', `/ai?sessionId=${f06SessionId}`, {
    historyMode: 'long', historyDelete: 'unknown-reconcile-deleted'
  });
  const history = await openHistory(page);
  await history.getByRole('button', { name: '删除工程绑定会话' }).first().click();
  const confirmation = page.getByRole('dialog', { name: '删除会话' });
  await confirmation.getByRole('button', { name: '删除会话' }).click();
  await expect(confirmation.getByRole('button', { name: '取消' })).toBeEnabled();
  await confirmation.getByRole('button', { name: '取消' }).click();
  await expect(history.getByText('删除结果待核对')).toBeVisible();
  await history.getByRole('button', { name: '核对删除结果' }).click();
  await expect(history.getByText('删除完成')).toBeVisible();
  await expect(history.locator('ul.ai-history__list > li')).toHaveCount(9);

  expect(audit.requests.filter(item => item.method === 'DELETE' &&
    item.path.startsWith('/api/ai/sessions/'))).toHaveLength(1);
  const operationPaths = audit.requests.filter(item =>
    item.path.includes('kind=session_delete')).map(item => item.path);
  expect(operationPaths).toHaveLength(2);
  expect(new Set(operationPaths).size).toBe(1);
  expectNoRuntimeErrors(audit);
});

test('G5 compact layout preserves stage blocker and primary action with reduced motion', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const viewport = { width: 1366, height: 768 } as const;
  const audit = await boot(page, viewport, 'compact', '/ai', { historyMode: 'empty' });
  await expect(page.locator('[data-ai-workbench-stage]')).toBeInViewport();
  await expect(page.getByRole('button', { name: '理解并规划任务' })).toBeInViewport();
  const diagnostics = await openDiagnostics(page);
  await expect(diagnostics).toBeInViewport();
  expect(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches)).toBe(true);
  const maximumTransitionMs = await diagnostics.evaluate(element => {
    const values = getComputedStyle(element).transitionDuration.split(',').map(value =>
      value.trim().endsWith('ms') ? Number.parseFloat(value) : Number.parseFloat(value) * 1000
    );
    return Math.max(0, ...values);
  });
  expect(maximumTransitionMs).toBeLessThanOrEqual(1);
  await captureF06Evidence(page, audit, 'g5-compact-reduced-motion', viewport, 'compact');
  await expectNoHorizontalOverflow(page);
  expectNoRuntimeErrors(audit);
});

test('G5 Browser DPR matrix keeps AI history and diagnostics non-overflowing', async ({ browser }) => {
  for (const deviceScaleFactor of [1, 1.25, 1.5, 2] as const) {
    const viewport = { width: 1366, height: 768 } as const;
    const context = await browser.newContext({ viewport, deviceScaleFactor });
    const page = await context.newPage();
    await installF02VisualPreferences(page, 'light', 'compact');
    const audit = await installF06Fixture(page, { historyMode: 'long' });
    await page.goto('/studio/index.html#/ai');
    await expect(page.locator('[data-ai-owner-phase="idle"]')).toBeVisible();
    const history = await openHistory(page);
    await expect(history.getByText('第 1–10 项，共 25 项')).toBeVisible();
    expect(await page.evaluate(() => window.devicePixelRatio)).toBe(deviceScaleFactor);
    await expectNoHorizontalOverflow(page);
    await captureF06Evidence(page, audit, `g5-dpr-${deviceScaleFactor}`, viewport, 'compact');
    expectNoRuntimeErrors(audit);
    await context.close();
  }
});

test('G5 survives 20 AI route and drawer mount cycles with one owner and zero resources after leave', async ({ page }) => {
  const audit = await boot(page, { width: 1366, height: 768 }, 'compact', '/about', {
    historyMode: 'long'
  });

  for (let cycle = 0; cycle < 20; cycle += 1) {
    await page.getByRole('link', { name: 'AI', exact: true }).click();
    await expect(page).toHaveURL(/#\/ai$/);
    const owner = page.locator('[data-ai-owner-phase]');
    await expect(owner).toHaveCount(1);
    await expect(owner).toHaveAttribute('data-ai-owner-request-count', '0');

    const history = await openHistory(page);
    await expect(history.getByText('第 1–10 项，共 25 项')).toBeVisible();
    await page.keyboard.press('Escape');
    const diagnostics = await openDiagnostics(page);
    await expect(diagnostics.getByRole('heading', { name: '阶段时间线' })).toBeVisible();
    await page.keyboard.press('Escape');

    await page.evaluate(() => { window.location.hash = '#/about'; });
    await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(0);
    await expect(page).toHaveURL(/#\/about$/);
  }

  expectNoRuntimeErrors(audit);
});

test('G5 logout and protected 401 dispose the AI owner before returning to login', async ({ browser }) => {
  const logoutContext = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  const logoutPage = await logoutContext.newPage();
  const logoutAudit = await boot(logoutPage, { width: 1366, height: 768 }, 'compact', '/ai', {
    historyMode: 'long'
  });
  await openDiagnostics(logoutPage);
  await logoutPage.keyboard.press('Escape');
  await logoutPage.getByRole('button', { name: '退出' }).click();
  await expect(logoutPage.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(logoutPage.locator('[data-ai-owner-phase]')).toHaveCount(0);
  expect(logoutAudit.requests.some(item => item.path === '/api/auth/logout')).toBe(true);
  expectNoRuntimeErrors(logoutAudit);
  await logoutContext.close();

  const expiredContext = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  const expiredPage = await expiredContext.newPage();
  const expiredAudit = await boot(expiredPage, { width: 1366, height: 768 }, 'compact', '/ai', {
    historyMode: 'long', historyUnauthorized: true
  });
  await expiredPage.getByRole('button', { name: '打开历史与恢复' }).click();
  await expect(expiredPage.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(expiredPage.locator('[data-ai-owner-phase]')).toHaveCount(0);
  await captureF06Evidence(expiredPage, expiredAudit, 'g5-ai-session-expired', {
    width: 1366, height: 768
  }, 'compact');
  expect(expiredAudit.pageErrors).toEqual([]);
  await expiredContext.close();
});

test('G5 route chunk failure never mounts an AI owner and lands on the public recovery state', async ({ page }) => {
  const audit = await boot(page, { width: 1366, height: 768 }, 'compact', '/about');
  let abortedChunk = '';
  let abortNextScript = true;
  await page.route('**/assets/*.js', route => {
    if (abortNextScript) {
      abortNextScript = false;
      abortedChunk = route.request().url();
      return route.abort('failed');
    }
    return route.continue();
  });
  await page.evaluate(() => { window.location.hash = '#/ai'; });
  await expect(page.getByRole('heading', { name: '页面资源加载失败' })).toBeVisible();
  await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(0);
  expect(abortedChunk).toMatch(/\/assets\/.*\.js/i);
  expect(audit.pageErrors).toEqual([]);
});
