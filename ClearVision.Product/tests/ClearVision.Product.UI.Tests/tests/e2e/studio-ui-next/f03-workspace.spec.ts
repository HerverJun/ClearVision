import { expect, Page, Route, test } from '@playwright/test';
import {
  auditF03Request,
  captureF03WorkspaceEvidence,
  createF03RuntimeErrorAudit,
  fulfillF03Json,
  hasF03VisualEvidenceTarget,
  installF03BrowserStartup,
  isF03G1RequestAllowlist,
  type F03RequestAuditEntry
} from './f03-browser-fixture';

const fixtureSchema = 'f03-g1-workspace.v1';
const projectA = '11111111-1111-4111-8111-111111111111';
const projectB = '22222222-2222-4222-8222-222222222222';
const flowId = '33333333-3333-4333-8333-333333333333';

function projectPayload(projectId = projectA, overrides: Record<string, unknown> = {}) {
  return {
    id: projectId,
    name: projectId === projectA ? '瓶盖检测 A' : '瓶盖检测 B',
    description: 'F03 G1 Browser fixture',
    version: '1.0.0',
    persistenceRevision: projectId === projectA ? 7 : 8,
    flow: {
      id: flowId,
      name: '空流程',
      operators: [],
      connections: [],
      decisionConfiguration: null
    },
    globalSettings: {},
    globalVariables: {
      schemaVersion: '1.0',
      variables: [],
      sourceBindings: [],
      targetBindings: []
    },
    assets: {
      schemaVersion: 1,
      calibrationAssets: [],
      spatialAssets: []
    },
    createdAt: '2026-07-15T01:00:00Z',
    modifiedAt: '2026-07-15T02:00:00Z',
    lastOpenedAt: null,
    ...overrides
  };
}

interface BootOptions {
  readonly workspaceEnabled?: boolean;
  readonly authStatus?: number;
  readonly projectStatus?: number;
  readonly projectBody?: unknown | ((projectId: string) => unknown);
  readonly projectDelayMs?: number;
}

async function bootWorkspace(page: Page, options: BootOptions = {}) {
  const audit: F03RequestAuditEntry[] = [];
  await installF03BrowserStartup(page, options.workspaceEnabled ?? true);
  await page.route('**/health', route => fulfillF03Json(
    route,
    200,
    { status: 'Healthy', port: 5177 },
    fixtureSchema
  ));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF03Request(request));
    if (url.pathname === '/api/auth/me') {
      const status = options.authStatus ?? 200;
      await fulfillF03Json(route, status, status === 200
        ? { userId: 'f03-user', username: 'f03-engineer', role: 'Engineer' }
        : { code: 'AUTH_REQUIRED' }, fixtureSchema);
      return;
    }
    const projectMatch = url.pathname.match(
      /^\/api\/projects\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i
    );
    if (projectMatch) {
      if (options.projectDelayMs) await new Promise(resolve => setTimeout(resolve, options.projectDelayMs));
      const id = projectMatch[1]!;
      const status = options.projectStatus ?? 200;
      const body = typeof options.projectBody === 'function'
        ? options.projectBody(id)
        : options.projectBody ?? projectPayload(id);
      await fulfillF03Json(route, status, body, fixtureSchema);
      return;
    }
    await fulfillF03Json(route, 404, { code: 'UNEXPECTED_F03_ROUTE' }, fixtureSchema);
  });
  await page.goto(`/studio/index.html#/projects/${projectA}/workspace`);
  await expect(page.locator('[data-evidence-surface="f03-workspace-shell"]')).toBeVisible();
  return audit;
}

async function workspaceDiagnostics(page: Page) {
  return page.evaluate(() => {
    const diagnostics = (window as typeof window & {
      __STUDIO_UI_WORKSPACE_DIAGNOSTICS__?: Record<string, unknown>;
    }).__STUDIO_UI_WORKSPACE_DIAGNOSTICS__;
    return diagnostics ? { ...diagnostics } : null;
  });
}

test('flag off keeps Workspace owner/resources at zero and skips the Project GET', async ({ page }) => {
  const audit = await bootWorkspace(page, { workspaceEnabled: false });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(shell).toHaveAttribute('data-workspace-state', 'flag-off');
  await expect(shell).toHaveAttribute('data-workspace-owner-count', '0');
  expect(audit.filter(entry => entry.path.startsWith('/api/projects/'))).toEqual([]);
  expect(await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    activeSubscriptions: 0,
    inFlightReads: 0
  });
  expect(isF03G1RequestAllowlist(audit)).toBe(true);
});

test('flag on mounts one owner only after full decode and disposes on route leave/project switch', async ({ page }) => {
  const audit = await bootWorkspace(page);
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(shell).toHaveAttribute('data-workspace-state', 'empty');
  await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  expect(await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 1,
    activeProjectId: projectA,
    activeSubscriptions: 1,
    inFlightReads: 0
  });
  expect(audit.filter(entry => entry.path === `/api/projects/${projectA}`)).toHaveLength(1);

  await page.goto(`/studio/index.html#/projects/${projectB}/workspace`);
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectB);
  await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  expect(await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 1,
    activeProjectId: projectB,
    lastDisposedProjectId: projectA,
    lastDisposedResources: {
      activeSubscriptions: 0,
      activeTimers: 0,
      activeAnimationFrames: 0,
      activeObservers: 0,
      activeAbortControllers: 0,
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads: 0,
      inFlightWrites: 0,
      inFlightPreview: 0,
      inFlightExecute: 0
    }
  });

  await page.goto('/studio/index.html#/about');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    activeSubscriptions: 0,
    activeAbortControllers: 0,
    inFlightReads: 0
  });
  expect(isF03G1RequestAllowlist(audit)).toBe(true);
});

test('renders loading before the Project read settles', async ({ page }) => {
  const boot = bootWorkspace(page, { projectDelayMs: 300 });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(shell).toHaveAttribute('data-workspace-state', 'loading');
  await boot;
  await expect(shell).toHaveAttribute('data-workspace-state', 'empty');
});

for (const scenario of [
  { label: '401', options: { authStatus: 401 }, state: 'unauthorized', readonly: 'false' },
  { label: '403/readonly', options: { projectStatus: 403 }, state: 'forbidden', readonly: 'true' },
  { label: '404', options: { projectStatus: 404 }, state: 'not-found', readonly: 'false' },
  {
    label: 'decode-error',
    options: { projectBody: { id: projectA, operatorCount: 40, connectionCount: 50 } },
    state: 'decode-error',
    readonly: 'false'
  }
] as const) {
  test(`renders ${scenario.label} with owner=0`, async ({ page }) => {
    const audit = await bootWorkspace(page, scenario.options);
    const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
    await expect(shell).toHaveAttribute('data-workspace-state', scenario.state);
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '0');
    await expect(shell).toHaveAttribute('data-workspace-readonly', scenario.readonly);
    expect(await workspaceDiagnostics(page)).toMatchObject({ workspaceOwnerCount: 0 });
    expect(isF03G1RequestAllowlist(audit)).toBe(true);
  });
}

test('passes 20 real Browser route mount/unmount cycles with a zero ledger', async ({ page }) => {
  const audit = await bootWorkspace(page);
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');

  for (let cycle = 0; cycle < 20; cycle += 1) {
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
    await page.goto('/studio/index.html#/about');
    await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
      workspaceOwnerCount: 0,
      activeSubscriptions: 0,
      activeAbortControllers: 0,
      inFlightReads: 0
    });
    await page.goto(`/studio/index.html#/projects/${projectA}/workspace`);
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  }

  await page.goto('/studio/index.html#/about');
  const final = await workspaceDiagnostics(page);
  expect(final).toMatchObject({
    workspaceOwnerCount: 0,
    activeSubscriptions: 0,
    activeAbortControllers: 0,
    inFlightReads: 0,
    totalWorkspaceMounts: 21,
    totalWorkspaceDisposals: 21,
    ownerConflictCount: 0
  });
  expect(isF03G1RequestAllowlist(audit)).toBe(true);
});

for (const viewport of [
  { width: 1366, height: 768 },
  { width: 1366, height: 600 }
] as const) {
  test(`Workspace Shell fits ${viewport.width}x${viewport.height} without global overflow`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const runtimeErrors = createF03RuntimeErrorAudit(page);
    const audit = await bootWorkspace(page);
    const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
    await expect(shell).toHaveAttribute('data-workspace-state', 'empty');

    const layout = await page.evaluate(() => {
      const toolbar = document.querySelector('.workspace-shell__toolbar')?.getBoundingClientRect();
      const status = document.querySelector('.workspace-shell__statusbar')?.getBoundingClientRect();
      const canvas = document.querySelector('.workspace-shell__canvas-surface')?.getBoundingClientRect();
      return {
        horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        verticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight,
        toolbar: toolbar ? { top: toolbar.top, bottom: toolbar.bottom } : null,
        status: status ? { top: status.top, bottom: status.bottom } : null,
        canvas: canvas ? { width: canvas.width, height: canvas.height } : null,
        viewport: { width: window.innerWidth, height: window.innerHeight }
      };
    });

    expect(layout).toMatchObject({
      horizontalOverflow: 0,
      verticalOverflow: 0,
      viewport
    });
    expect(layout.toolbar?.top).toBeGreaterThanOrEqual(0);
    expect(layout.status?.bottom).toBeLessThanOrEqual(viewport.height + 1);
    expect(layout.canvas?.height).toBeGreaterThanOrEqual(300);
    expect(isF03G1RequestAllowlist(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });

    if (hasF03VisualEvidenceTarget()) {
      await captureF03WorkspaceEvidence(page, {
        scenario: `workspace-shell-${viewport.height}`,
        viewport,
        requests: audit,
        runtimeErrors
      });
    }
  });
}
