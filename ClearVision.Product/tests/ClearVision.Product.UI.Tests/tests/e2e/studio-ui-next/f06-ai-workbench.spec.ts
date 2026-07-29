import { expect, test, type Page } from '@playwright/test';
import { installF02VisualPreferences } from './f02-browser-fixture';
import {
  captureF06Evidence,
  f06ProjectId,
  installF06Fixture,
  type F06BrowserAudit,
  type F06BrowserFixtureOptions
} from './f06-ai-fixture';

async function boot(
  page: Page,
  viewport: Readonly<{ width: number; height: number }>,
  density: 'compact' | 'comfortable',
  route: string,
  options: F06BrowserFixtureOptions = {}
): Promise<F06BrowserAudit> {
  await page.setViewportSize(viewport);
  await installF02VisualPreferences(page, 'light', density);
  const audit = await installF06Fixture(page, options);
  await page.goto(`/studio/index.html#${route}`);
  return audit;
}

function expectNoRuntimeErrors(audit: F06BrowserAudit): void {
  expect(audit.consoleErrors).toEqual([]);
  expect(audit.pageErrors).toEqual([]);
}

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => Math.max(
    document.documentElement.scrollWidth - document.documentElement.clientWidth,
    document.body.scrollWidth - document.body.clientWidth
  ));
  expect(overflow).toBeLessThanOrEqual(1);
}

test('unbound AI journey reaches Plan Ready through Intent Plan SSE and confirmed answers', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 };
  const audit = await boot(page, viewport, 'comfortable', '/ai');
  const task = page.getByRole('textbox', { name: '任务描述' });
  await expect(task).toBeFocused();
  await expect(page.getByText('尚未绑定工程', { exact: true })).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-idle-unbound', viewport, 'comfortable');

  await task.fill('检测冲压件表面划伤与压痕，任一划伤长度超过 2 mm 判定 NG，并输出缺陷位置与类型。');
  await page.getByRole('button', { name: '理解并规划任务' }).click();
  await expect(page.locator('[data-ai-plan-workspace]')).toBeVisible();
  await expect(page.locator('[data-ai-clarification-panel]')).toBeVisible();
  await expect(page.locator('[data-ai-clarification-panel] fieldset')).toHaveCount(3);
  await expect(page.getByText('工件定位 + 光照均衡 + 多尺度缺陷分割', { exact: true })).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-clarifying-unbound', viewport, 'comfortable');

  await page.getByRole('button', { name: '采用推荐答案' }).click();
  await expect(page.locator('[data-ai-owner-phase="plan-ready"]')).toBeVisible();
  await expect(page.getByText('方案已具备构建条件。', { exact: true }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: '进入下一阶段' })).toBeDisabled();
  await captureF06Evidence(page, audit, 'ai-plan-ready-unbound', viewport, 'comfortable');

  expect(audit.requests.some(item => item.path === '/api/ai/agent-intent-router-runs')).toBe(true);
  expect(audit.requests.some(item => item.path === '/api/ai/agent-plan-runs')).toBe(true);
  expect(audit.requests.some(item => item.path.startsWith('/api/ai/agent-runs/run_plan_f06_01/events'))).toBe(true);
  expect(audit.requests.filter(item => /build|handoff|apply|workspace\/save/i.test(item.path))).toEqual([]);
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('project-bound compact deep link shows canonical project revision and handles long Chinese', async ({ page }) => {
  const viewport = { width: 1366, height: 768 };
  const audit = await boot(page, viewport, 'compact', `/projects/${f06ProjectId}/ai`, {
    projectBound: true,
    longContent: true
  });
  await expect(page.getByText('新能源托盘超长中文名称外观检测工程', { exact: true })).toBeVisible();
  await expect(page.getByText('保存 revision 18')).toBeVisible();
  await page.getByRole('textbox', { name: '任务描述' }).fill('检测新能源电池托盘冲压件高反光表面的划伤、压痕与脏污，并输出缺陷类型、位置、尺寸和最终 OK/NG 判定。');
  await page.getByRole('button', { name: '理解并规划任务' }).click();
  await expect(page.locator('[data-ai-plan-workspace]')).toContainText('复杂环境光变化');
  await expect(page.locator('[data-ai-clarification-panel]')).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-project-clarifying-long-cn', viewport, 'compact');
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('AI route and owner fail closed when flag or role is denied', async ({ page }) => {
  const flagAudit = await boot(page, { width: 1366, height: 768 }, 'compact', '/ai', { flag: false });
  await expect(page).toHaveURL(/#\/forbidden$/);
  expect(flagAudit.requests.some(item => item.path.startsWith('/api/ai/'))).toBe(false);
  await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(0);

  await page.close();
});

test('Operator role fails closed before mounting the AI owner', async ({ page }) => {
  const audit = await boot(page, { width: 1366, height: 768 }, 'compact', `/projects/${f06ProjectId}/ai`, {
    role: 'Operator',
    projectBound: true
  });
  await expect(page).toHaveURL(/#\/forbidden$/);
  expect(audit.requests.some(item => item.path.startsWith('/api/ai/'))).toBe(false);
  await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(0);
});

test('service unavailable state explains impact and next action without layout overflow', async ({ page }) => {
  const viewport = { width: 1366, height: 768 };
  const audit = await boot(page, viewport, 'comfortable', '/ai', { failSession: true });
  await expect(page.getByRole('heading', { name: 'AI 服务暂时不可用' })).toBeVisible();
  await expect(page.getByText('本地服务暂时不可用，请检查服务状态后重试。')).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-service-unavailable', viewport, 'comfortable');
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});
