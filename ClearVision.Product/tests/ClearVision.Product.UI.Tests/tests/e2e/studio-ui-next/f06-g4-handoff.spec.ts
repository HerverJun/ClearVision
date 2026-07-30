import { expect, test, type Page } from '@playwright/test';
import { installF02VisualPreferences } from './f02-browser-fixture';
import {
  captureF06Evidence,
  f06CreatedProjectId,
  f06HandoffArtifactId,
  f06ProjectId,
  f06SecondHandoffArtifactId,
  installF06Fixture,
  type F06BrowserAudit,
  type F06BrowserFixtureOptions
} from './f06-ai-fixture';

async function boot(
  page: Page,
  viewport: Readonly<{ width: number; height: number }>,
  density: 'compact' | 'comfortable',
  route: string,
  options: F06BrowserFixtureOptions
): Promise<F06BrowserAudit> {
  await page.setViewportSize(viewport);
  await installF02VisualPreferences(page, 'light', density);
  const audit = await installF06Fixture(page, { ...options, enableHandoff: true });
  await page.goto(`/studio/index.html#${route}`);
  return audit;
}

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => Math.max(
    document.documentElement.scrollWidth - document.documentElement.clientWidth,
    document.body.scrollWidth - document.body.clientWidth
  ));
  expect(overflow).toBeLessThanOrEqual(1);
}

function expectNoRuntimeErrors(audit: F06BrowserAudit, allowedConsole: RegExp | null = null): void {
  expect(audit.pageErrors).toEqual([]);
  expect(allowedConsole ? audit.consoleErrors.filter(item => !allowedConsole.test(item)) : audit.consoleErrors)
    .toEqual([]);
}

function projectWrites(audit: F06BrowserAudit) {
  return audit.requests.filter(item =>
    item.method === 'PUT' && /^\/api\/projects\/[0-9a-f-]+$/i.test(item.path) ||
    item.method === 'POST' && item.path === '/api/projects'
  );
}

test('existing Project travels from Apply Preview to staged draft and one explicit PUT', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 };
  const audit = await boot(page, viewport, 'comfortable', `/projects/${f06ProjectId}/ai?sessionId=session_f06_01`, {
    projectBound: true,
    initialBuildState: 'ready'
  });
  await expect(page.locator('[data-ai-apply-preview]')).toBeVisible();
  await expect(page.getByRole('button', { name: '交接到工作区审核' })).toBeVisible();
  expect(projectWrites(audit)).toEqual([]);

  await page.getByRole('button', { name: '交接到工作区审核' }).click();
  await expect(page).toHaveURL(new RegExp(
    `#\/projects\/${f06ProjectId}\/workspace\\?handoff=${f06HandoffArtifactId}$`
  ));
  await expect(page.locator('[data-ai-owner-phase]')).toHaveCount(0);
  await expect(page.locator('[data-workspace-handoff-phase="workspace-staged-unsaved"]')).toBeVisible();
  await expect(page.getByText('AI 候选，尚未保存。', { exact: false })).toBeVisible();
  expect(projectWrites(audit)).toEqual([]);
  await captureF06Evidence(page, audit, 'g4-existing-staged-unsaved', viewport, 'comfortable');

  await page.getByTestId('workspace-save').click();
  await expect(page.locator('[data-workspace-handoff-phase="workspace-saved"]')).toBeVisible();

  expect(audit.requests.filter(item => item.method === 'POST' && item.path === '/api/ai/handoffs')).toHaveLength(1);
  expect(audit.requests.filter(item => item.path.endsWith('/consume'))).toHaveLength(1);
  expect(audit.requests.filter(item => item.path.endsWith('/acknowledge'))).toHaveLength(1);
  expect(projectWrites(audit)).toEqual([
    expect.objectContaining({ method: 'PUT', path: `/api/projects/${f06ProjectId}` })
  ]);
  expect(page.url()).not.toMatch(/candidateFlow|fingerprint/i);
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('new target remains id-less until explicit save then uses create authority and one Project PUT', async ({ page }) => {
  const viewport = { width: 1366, height: 768 };
  const audit = await boot(page, viewport, 'compact', '/ai?sessionId=session_f06_01', {
    initialBuildState: 'ready'
  });
  await page.getByRole('button', { name: '交接到工作区审核' }).click();
  await expect(page).toHaveURL(new RegExp(
    `#\/projects\/new\/workspace\\?handoff=${f06HandoffArtifactId}$`
  ));
  await expect(page.locator('[data-workspace-project-id="new"]')).toBeVisible();
  await expect(page.getByTestId('workspace-new-project-metadata')).toBeVisible();
  await expect(page.getByTestId('new-draft-preview-unavailable')).toBeVisible();
  await expect(page.locator('[data-workspace-handoff-phase="workspace-staged-unsaved"]')).toBeVisible();
  expect(projectWrites(audit)).toEqual([]);
  await captureF06Evidence(page, audit, 'g4-new-staged-unsaved', viewport, 'compact');

  await page.getByLabel('工程名称').fill('AI 审核后的新工程');
  await page.getByLabel('工程描述').fill('显式创建并保存候选流程');
  await page.getByTestId('workspace-save').click();
  await expect(page).toHaveURL(new RegExp(`#\/projects\/${f06CreatedProjectId}\/workspace$`));
  await expect(page.locator(`[data-workspace-project-id="${f06CreatedProjectId}"]`)).toBeVisible();

  expect(projectWrites(audit)).toEqual([
    expect.objectContaining({ method: 'POST', path: '/api/projects' }),
    expect.objectContaining({ method: 'PUT', path: `/api/projects/${f06CreatedProjectId}` })
  ]);
  expect(audit.requests.filter(item => item.path === `/api/projects/${f06CreatedProjectId}/open`)).toHaveLength(1);
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('lost handoff create response reconciles by Build lookup without duplicate POST', async ({ page }) => {
  const audit = await boot(page, { width: 1366, height: 768 }, 'compact', '/ai?sessionId=session_f06_01', {
    initialBuildState: 'ready',
    handoffCreateUnknownOutcome: true
  });

  await page.getByRole('button', { name: '交接到工作区审核' }).click();
  await expect(page.locator('[data-ai-owner-phase="handoff-unknown-outcome"]')).toBeVisible();
  await page.getByRole('button', { name: '查询交接结果' }).click();
  await expect(page).toHaveURL(new RegExp(
    `#\/projects\/new\/workspace\\?handoff=${f06HandoffArtifactId}$`
  ));

  expect(audit.requests.filter(item => item.method === 'POST' && item.path === '/api/ai/handoffs')).toHaveLength(1);
  expect(audit.requests.filter(item => item.path === `/api/ai/handoffs/by-build/run_build_f06_01`)).toHaveLength(1);
  expectNoRuntimeErrors(audit, /ERR_FAILED|Failed to load resource/i);
  await expectNoHorizontalOverflow(page);
});

test('unknown Project PUT reconciles the committed revision instead of repeating save', async ({ page }) => {
  const audit = await boot(page, { width: 1920, height: 1080 }, 'comfortable',
    `/projects/${f06ProjectId}/ai?sessionId=session_f06_01`, {
      projectBound: true,
      initialBuildState: 'ready',
      saveUnknownOutcome: true
    });
  await page.getByRole('button', { name: '交接到工作区审核' }).click();
  await expect(page.locator('[data-workspace-handoff-phase="workspace-staged-unsaved"]')).toBeVisible();
  await page.getByTestId('workspace-save').click();
  await expect(page.locator('[data-workspace-persistence-phase="unknown-outcome"]')).toBeVisible();
  await page.getByTestId('workspace-save-reconcile').click();
  await expect(page.locator('[data-workspace-handoff-phase="workspace-saved"]')).toBeVisible();

  expect(projectWrites(audit)).toHaveLength(1);
  expect(audit.requests.filter(item =>
    item.method === 'GET' && item.path === `/api/projects/${f06ProjectId}`
  ).length).toBeGreaterThanOrEqual(2);
  expectNoRuntimeErrors(audit, /ERR_FAILED|Failed to load resource/i);
  await expectNoHorizontalOverflow(page);
});

test('dirty Workspace keeps its mounted draft when a second artifact query arrives', async ({ page }) => {
  const audit = await boot(page, { width: 1366, height: 768 }, 'compact',
    `/projects/${f06ProjectId}/workspace?handoff=${f06HandoffArtifactId}`, {
      projectBound: true
    });
  await expect(page.locator('[data-workspace-handoff-phase="workspace-staged-unsaved"]')).toBeVisible();
  const ownerMounts = await page.locator('[data-workspace-owner-count]').getAttribute('data-workspace-owner-count');
  await page.evaluate(route => {
    window.location.hash = route;
  }, `/projects/${f06ProjectId}/workspace?handoff=${f06SecondHandoffArtifactId}`);
  await expect(page.locator('[data-handoff-receive-phase="workspace-dirty-conflict"]')).toBeVisible();
  await expect(page.locator('[data-workspace-handoff-phase="workspace-staged-unsaved"]')).toBeVisible();
  await expect(page.locator('[data-workspace-owner-count]')).toHaveAttribute('data-workspace-owner-count', ownerMounts ?? '1');

  expect(audit.requests.filter(item =>
    item.path === `/api/ai/handoffs/${f06SecondHandoffArtifactId}/consume`
  )).toEqual([]);
  expectNoRuntimeErrors(audit);
  await expectNoHorizontalOverflow(page);
});

test('expired consumed and baseline-conflicting artifacts fail closed before staging', async ({ context }) => {
  const scenarios = [
    { status: 'expired' as const, phase: 'artifact-expired', revision: 18 },
    { status: 'consumed' as const, phase: 'artifact-consumed', revision: 18 },
    { status: 'available' as const, phase: 'artifact-baseline-conflict', revision: 17 }
  ];
  for (const scenario of scenarios) {
    const page = await context.newPage();
    const audit = await boot(page, { width: 1366, height: 768 }, 'compact',
      `/projects/${f06ProjectId}/workspace?handoff=${f06HandoffArtifactId}`, {
        projectBound: true,
        artifactStatus: scenario.status,
        artifactBaselineRevision: scenario.revision
      });
    await expect(page.locator(`[data-handoff-receive-phase="${scenario.phase}"]`)).toBeVisible();
    expect(audit.requests.filter(item => item.path.endsWith('/consume'))).toEqual([]);
    expect(audit.requests.filter(item => item.path.endsWith('/acknowledge'))).toEqual([]);
    expect(projectWrites(audit)).toEqual([]);
    expectNoRuntimeErrors(audit);
    await expectNoHorizontalOverflow(page);
    await page.close();
  }
});
