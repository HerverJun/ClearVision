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
  options: F06BrowserFixtureOptions = {},
  theme: 'light' | 'dark' = 'light'
): Promise<F06BrowserAudit> {
  await page.setViewportSize(viewport);
  await installF02VisualPreferences(page, theme, density);
  const audit = await installF06Fixture(page, options);
  await page.goto(`/studio/index.html#${route}`);
  return audit;
}

function expectNoRuntimeErrors(audit: F06BrowserAudit): void {
  expect(audit.consoleErrors).toEqual([]);
  expect(audit.pageErrors).toEqual([]);
  expect(audit.requests.filter(item =>
    /handoff|apply-to-canvas|workspace\/consume|project\/save|flow-canvas|image-canvas/i.test(item.path) ||
    item.method === 'PUT' && /^\/api\/projects(?:\/|$)/i.test(item.path))).toEqual([]);
}

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => Math.max(
    document.documentElement.scrollWidth - document.documentElement.clientWidth,
    document.body.scrollWidth - document.body.clientWidth
  ));
  expect(overflow).toBeLessThanOrEqual(1);
}

test('unbound AI journey closes Build parameters resources Validation and read-only ApplyGate', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 };
  const audit = await boot(page, viewport, 'comfortable', '/ai');
  const task = page.getByRole('textbox', { name: '任务描述' });
  await expect(task).toBeFocused();
  await page.keyboard.press('Tab');
  await page.keyboard.press('Shift+Tab');
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
  await captureF06Evidence(page, audit, 'ai-plan-ready-unbound', viewport, 'comfortable');

  await page.getByRole('button', { name: '开始构建' }).click();
  await expect(page.locator('[data-ai-owner-phase="parameters-pending"]')).toBeVisible();
  await expect(page.locator('[data-ai-build-workspace]')).toBeVisible();
  await expect(page.locator('[data-ai-pending-parameters]')).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-build-parameters-pending', viewport, 'comfortable');

  await page.getByLabel('分割阈值').fill('128');
  await page.getByRole('button', { name: '确认全部参数' }).click();
  await expect(page.locator('[data-ai-owner-phase="build-blocked"]')).toBeVisible();
  await page.getByRole('button', { name: '重新校验' }).click();
  await expect(page.locator('[data-ai-owner-phase="resources-pending"]')).toBeVisible();
  await expect(page.locator('[data-ai-resource-decisions]')).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-build-resources-pending', viewport, 'comfortable');

  await page.getByLabel('选择已配置相机').selectOption('55555555-5555-4555-8555-555555555555');
  await page.getByRole('button', { name: '保存资源决策' }).click();
  await expect(page.locator('[data-ai-owner-phase="build-blocked"]')).toBeVisible();
  await page.getByRole('button', { name: '重新校验' }).click();
  await expect(page.locator('[data-ai-owner-phase="build-ready"]')).toBeVisible();
  await expect(page.locator('[data-ai-apply-preview]')).toBeVisible();
  await expect(page.getByText('未保存的新工程', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '交接到工作区审核' })).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-build-ready-readonly-gate', viewport, 'comfortable');

  expect(audit.requests.some(item => item.path === '/api/ai/agent-intent-router-runs')).toBe(true);
  expect(audit.requests.some(item => item.path === '/api/ai/agent-plan-runs')).toBe(true);
  expect(audit.requests.some(item => item.path.startsWith('/api/ai/agent-runs/run_plan_f06_01/events'))).toBe(true);
  expect(audit.requests.filter(item => item.method === 'POST' && item.path === '/api/ai/agent-runs')).toHaveLength(1);
  expect(audit.requests.filter(item => item.path.endsWith('/revalidate'))).toHaveLength(2);
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('recovered Build exposes building and validating stages while SSE remains owner-controlled', async ({ context }) => {
  const scenarios = [
    { state: 'building' as const, phase: 'building', name: 'ai-build-building', viewport: { width: 1920, height: 1080 }, density: 'comfortable' as const },
    { state: 'validating' as const, phase: 'validating', name: 'ai-build-validating', viewport: { width: 1366, height: 768 }, density: 'compact' as const }
  ];
  for (const scenario of scenarios) {
    const page = await context.newPage();
    const audit = await boot(page, scenario.viewport, scenario.density, '/ai?sessionId=session_f06_01', {
      initialBuildState: scenario.state
    });
    await expect(page.locator(`[data-ai-owner-phase="${scenario.phase}"]`)).toBeVisible();
    await expect(page.locator('[data-ai-workbench-stage]')).toContainText(scenario.state === 'building' ? '生成流程候选' : '验证与预演');
    await captureF06Evidence(page, audit, scenario.name, scenario.viewport, scenario.density);
    audit.releaseBuildStream();
    await expect(page.locator('[data-ai-owner-phase="parameters-pending"]')).toBeVisible();
    expectNoRuntimeErrors(audit);
    await expectNoHorizontalOverflow(page);
    await page.close();
  }
});

test('stale recovered Build exposes revalidating before applying the canonical response', async ({ page }) => {
  const viewport = { width: 1366, height: 768 };
  const audit = await boot(page, viewport, 'compact', '/ai?sessionId=session_f06_01', {
    initialBuildState: 'stale', holdRevalidation: true
  });
  await expect(page.locator('[data-ai-owner-phase="build-blocked"]')).toBeVisible();
  await page.getByRole('button', { name: '重新校验' }).click();
  await expect(page.locator('[data-ai-owner-phase="revalidating"]')).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-build-revalidating', viewport, 'compact');
  audit.releaseRevalidation();
  await expect(page.locator('[data-ai-owner-phase="build-ready"]')).toBeVisible();
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('terminal replay projects failed and cancelled Build outcomes without reviving a writer', async ({ context }) => {
  for (const state of ['failed', 'cancelled'] as const) {
    const page = await context.newPage();
    const viewport = { width: 1366, height: 768 };
    const audit = await boot(page, viewport, 'compact', '/ai?sessionId=session_f06_01', {
      initialBuildState: state
    });
    const phase = state === 'failed' ? 'build-failed' : 'build-cancelled';
    await expect(page.locator(`[data-ai-owner-phase="${phase}"]`)).toBeVisible();
    await expect(page.locator('[data-ai-terminal-state]')).toBeVisible();
    await captureF06Evidence(page, audit, `ai-build-${state}`, viewport, 'compact');
    expectNoRuntimeErrors(audit);
    await expectNoHorizontalOverflow(page);
    await page.close();
  }
});

test('project recovery names a baseline conflict and keeps the old candidate read-only', async ({ page }) => {
  const viewport = { width: 1366, height: 768 };
  const audit = await boot(page, viewport, 'compact', `/projects/${f06ProjectId}/ai?sessionId=session_f06_01`, {
    projectBound: true, initialBuildState: 'baseline-conflict'
  });
  await expect(page.locator('[data-ai-owner-phase="baseline-conflict"]')).toBeVisible();
  await expect(page.locator('[data-ai-build-workspace]')).toContainText('当前结果已过期');
  await captureF06Evidence(page, audit, 'ai-build-baseline-conflict', viewport, 'compact');
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('lost Build create response stays unknown and reconciles by operation identity without a duplicate create', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 };
  const audit = await boot(page, viewport, 'comfortable', '/ai', { buildUnknownOutcome: true });
  await page.getByRole('textbox', { name: '任务描述' }).fill('检测冲压件表面划伤与压痕，输出缺陷位置和最终判定。');
  await page.getByRole('button', { name: '理解并规划任务' }).click();
  await page.getByRole('button', { name: '采用推荐答案' }).click();
  await expect(page.locator('[data-ai-owner-phase="plan-ready"]')).toBeVisible();
  await page.getByRole('button', { name: '开始构建' }).click();
  await expect(page.locator('[data-ai-owner-phase="unknown-outcome"]')).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-build-unknown-outcome', viewport, 'comfortable');
  expect(audit.requests.filter(item => item.method === 'POST' && item.path === '/api/ai/agent-runs')).toHaveLength(1);
  expect(audit.requests.some(item => item.path.includes('kind=build_run'))).toBe(true);
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('terminal Build recovery renders the candidate without restoring a Plan owner', async ({ page }) => {
  const viewport = { width: 1366, height: 768 };
  const audit = await boot(page, viewport, 'compact', '/ai?sessionId=session_f06_01', {
    recoveredBuild: true
  });
  await expect(page.locator('[data-ai-owner-phase="build-ready"]')).toBeVisible();
  await expect(page.locator('[data-ai-apply-preview]')).toBeVisible();
  await expect(page.locator('[data-ai-plan-workspace]')).toHaveCount(0);
  await expect(page.getByRole('button', { name: '交接到工作区审核' })).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-build-terminal-recovery', viewport, 'compact');
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
  await expect(page.getByText('保存修订 18')).toBeVisible();
  await page.getByRole('textbox', { name: '任务描述' }).fill('检测新能源电池托盘冲压件高反光表面的划伤、压痕与脏污，并输出缺陷类型、位置、尺寸和最终 OK/NG 判定。');
  await page.getByRole('button', { name: '理解并规划任务' }).click();
  await expect(page.locator('[data-ai-plan-workspace]')).toContainText('复杂环境光变化');
  await expect(page.locator('[data-ai-clarification-panel]')).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-project-clarifying-long-cn', viewport, 'compact');
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('AI route and owner fail closed when flag or role is denied', async ({ page }) => {
  const viewport = { width: 1366, height: 768 } as const;
  const flagAudit = await boot(page, viewport, 'compact', '/ai', { flag: false });
  await expect(page).toHaveURL(/#\/forbidden$/);
  expect(flagAudit.requests.some(item => item.path.startsWith('/api/ai/'))).toBe(false);
  await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(0);
  await captureF06Evidence(page, flagAudit, 'g5-ai-flag-off-forbidden', viewport, 'compact');

  await page.close();
});

test('Operator role fails closed before mounting the AI owner', async ({ page }) => {
  const viewport = { width: 1366, height: 768 } as const;
  const audit = await boot(page, viewport, 'compact', `/projects/${f06ProjectId}/ai`, {
    role: 'Operator',
    projectBound: true
  });
  await expect(page).toHaveURL(/#\/forbidden$/);
  expect(audit.requests.some(item => item.path.startsWith('/api/ai/'))).toBe(false);
  await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(0);
  await captureF06Evidence(page, audit, 'g5-ai-operator-forbidden', viewport, 'compact');
});

test('Admin role mounts the same single AI owner through the unbound route', async ({ page }) => {
  const audit = await boot(page, { width: 1366, height: 768 }, 'compact', '/ai', { role: 'Admin' });
  await expect(page.locator('[data-ai-owner-phase="idle"]')).toBeVisible();
  await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(1);
  expect(audit.requests.filter(item => item.method === 'POST' && item.path === '/api/ai/sessions')).toHaveLength(1);
  expectNoRuntimeErrors(audit);
});

test('dark comfortable workbench keeps the idle task and recovery entry legible', async ({ page }) => {
  const viewport = { width: 1536, height: 864 } as const;
  const audit = await boot(page, viewport, 'comfortable', '/ai', { role: 'Engineer' }, 'dark');
  await expect(page.locator('[data-ai-owner-phase="idle"]')).toBeVisible();
  await expect(page.locator('[data-ai-task-composer]')).toBeVisible();
  await expect(page.getByRole('button', { name: '打开历史与恢复' })).toBeVisible();
  await captureF06Evidence(page, audit, 'ai-idle-dark', viewport, 'comfortable');
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('V4 stage matrix keeps the AI entry stable across B0-B4', async ({ browser }) => {
  for (const visual of [
    { id: 'b0', width: 1920, height: 1080, theme: 'light', density: 'compact' },
    { id: 'b1', width: 1536, height: 864, theme: 'light', density: 'compact' },
    { id: 'b2', width: 1366, height: 768, theme: 'light', density: 'compact' },
    { id: 'b3', width: 1920, height: 1080, theme: 'dark', density: 'compact' },
    { id: 'b4-light', width: 1920, height: 1080, theme: 'light', density: 'comfortable' },
    { id: 'b4-dark', width: 1920, height: 1080, theme: 'dark', density: 'comfortable' }
  ] as const) {
    const viewport = { width: visual.width, height: visual.height } as const;
    const context = await browser.newContext({ viewport });
    const page = await context.newPage();
    try {
      await installF02VisualPreferences(page, visual.theme, visual.density);
      const audit = await installF06Fixture(page, { role: 'Engineer', historyMode: 'empty' });
      await page.goto('/studio/index.html#/ai');
      await expect(page.locator('[data-ai-owner-phase="idle"]')).toBeVisible();
      await expect(page.locator('[data-ai-task-composer]')).toBeVisible();
      const projection = await page.evaluate(() => ({
        theme: document.documentElement.dataset.theme,
        density: document.documentElement.dataset.density,
        overflow: Math.max(
          document.documentElement.scrollWidth - document.documentElement.clientWidth,
          document.body.scrollWidth - document.body.clientWidth
        )
      }));
      expect(projection).toEqual({ theme: visual.theme, density: visual.density, overflow: 0 });
      await captureF06Evidence(page, audit, `ai-stage-${visual.id}`, viewport, visual.density);
      expectNoRuntimeErrors(audit);
    } finally {
      await context.close();
    }
  }
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
