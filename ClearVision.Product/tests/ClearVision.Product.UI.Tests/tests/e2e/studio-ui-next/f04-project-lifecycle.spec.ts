import { expect, test, type Page, type Request, type Route } from '@playwright/test';
import {
  captureF04VisualEvidence,
  createF04RuntimeErrorAudit,
  hasF04VisualEvidenceTarget
} from './f04-browser-evidence';

const projectId = '11111111-1111-4111-8111-111111111111';
const flowId = '22222222-2222-4222-8222-222222222222';

interface FixtureProject {
  id: string;
  name: string;
  description: string | null;
  version: string;
  persistenceRevision: number;
  flow: Record<string, unknown>;
  globalSettings: Record<string, string>;
  globalVariables: Record<string, unknown>;
  assets: Record<string, unknown>;
  createdAt: string;
  modifiedAt: string | null;
  lastOpenedAt: string | null;
}

interface FixtureState {
  project: FixtureProject | null;
  deleted: boolean;
  operations: Map<string, Record<string, unknown>>;
  createPosts: number;
  deletePosts: number;
  openPosts: number;
  updatePuts: number;
  operationGets: number;
  conflictOnFirstUpdate: boolean;
  audit: Array<{ method: string; path: string }>;
}

function blankProject(name = '新建工程', description: string | null = '空白创建'): FixtureProject {
  return {
    id: projectId,
    name,
    description,
    version: '1.0.0',
    persistenceRevision: 0,
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
    createdAt: '2026-07-19T00:00:00Z',
    modifiedAt: null,
    lastOpenedAt: null
  };
}

function operation(
  kind: 'create' | 'delete',
  clientOperationId: string,
  project: FixtureProject
): Record<string, unknown> {
  return {
    clientOperationId,
    kind,
    status: 'completed',
    projectId: project.id,
    result: kind === 'create'
      ? {
          project,
          projectDeleted: false,
          deleted: false,
          alreadyDeleted: false,
          cleanupStatus: 'not-required'
        }
      : {
          project: null,
          projectDeleted: true,
          deleted: true,
          alreadyDeleted: false,
          cleanupStatus: 'cleanup-pending'
        },
    errorCode: null,
    createdAtUtc: '2026-07-19T00:00:00Z',
    updatedAtUtc: '2026-07-19T00:00:01Z',
    expiresAtUtc: kind === 'create' ? '2026-07-26T00:00:01Z' : '2026-08-18T00:00:01Z'
  };
}

async function json(route: Route, status: number, body: unknown): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    headers: { 'x-clearvision-fixture-schema': 'f04-g3c-project-lifecycle.v1' },
    body: JSON.stringify(body)
  });
}

function audit(request: Request): { method: string; path: string } {
  const url = new URL(request.url());
  return { method: request.method(), path: `${url.pathname}${url.search}` };
}

async function installStartup(page: Page): Promise<void> {
  await page.addInitScript(() => {
    sessionStorage.setItem('cv_auth_token', 'f04-project-token');
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
  });
}

async function boot(page: Page, options: { initialProject?: FixtureProject; conflictOnFirstUpdate?: boolean } = {}) {
  const state: FixtureState = {
    project: options.initialProject ?? null,
    deleted: false,
    operations: new Map(),
    createPosts: 0,
    deletePosts: 0,
    openPosts: 0,
    updatePuts: 0,
    operationGets: 0,
    conflictOnFirstUpdate: options.conflictOnFirstUpdate ?? false,
    audit: []
  };
  await installStartup(page);
  await page.route('**/health', route => json(route, 200, { status: 'Healthy', port: 5177 }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    state.audit.push(audit(request));
    if (path === '/api/auth/setup-status') {
      await json(route, 200, {
        requiresInitialAdminSetup: false,
        usernameMinLength: 3,
        passwordMinLength: 6,
        requiresUppercase: false,
        requiresLowercase: false,
        requiresDigit: false
      });
      return;
    }
    if (path === '/api/auth/me') {
      await json(route, 200, { userId: 'f04-user', username: 'f04-engineer', role: 'Engineer' });
      return;
    }
    if (path === '/api/projects/recent') {
      await json(route, 200, state.project && !state.deleted && state.project.lastOpenedAt ? [state.project] : []);
      return;
    }
    if (path === '/api/projects/search') {
      await json(route, 200, state.project && !state.deleted ? [state.project] : []);
      return;
    }
    if (path === '/api/projects' && request.method() === 'GET') {
      await json(route, 200, state.project && !state.deleted ? [state.project] : []);
      return;
    }
    if (path === '/api/projects' && request.method() === 'POST') {
      state.createPosts += 1;
      const body = request.postDataJSON() as {
        clientOperationId: string;
        name: string;
        description: string | null;
      };
      const created = blankProject(body.name, body.description);
      state.project = created;
      state.deleted = false;
      state.operations.set(body.clientOperationId, operation('create', body.clientOperationId, created));
      await route.abort('failed');
      return;
    }
    const operationMatch = path.match(/^\/api\/project-operations\/([0-9a-f-]{36})$/i);
    if (operationMatch) {
      state.operationGets += 1;
      const found = state.operations.get(operationMatch[1]!);
      await json(route, found ? 200 : 404, found ?? { code: 'PROJECT_OPERATION_NOT_FOUND' });
      return;
    }
    if (path === '/api/operators/library' && url.search === '?includeCompatibility=true') {
      await json(route, 200, []);
      return;
    }
    const openMatch = path.match(/^\/api\/projects\/([0-9a-f-]{36})\/open$/i);
    if (openMatch && request.method() === 'POST') {
      state.openPosts += 1;
      if (!state.project || state.deleted) {
        await json(route, 404, { code: 'PROJECT_NOT_FOUND' });
        return;
      }
      state.project.lastOpenedAt = `2026-07-19T00:00:${String(state.openPosts).padStart(2, '0')}Z`;
      await json(route, 200, {
        projectId: state.project.id,
        lastOpenedAtUtc: state.project.lastOpenedAt
      });
      return;
    }
    const deleteMatch = path.match(/^\/api\/projects\/([0-9a-f-]{36})\/delete$/i);
    if (deleteMatch && request.method() === 'POST') {
      state.deletePosts += 1;
      if (!state.project || state.deleted) {
        await json(route, 404, { code: 'PROJECT_NOT_FOUND' });
        return;
      }
      const body = request.postDataJSON() as {
        clientOperationId: string;
        expectedPersistenceRevision: number;
      };
      if (body.expectedPersistenceRevision !== state.project.persistenceRevision) {
        await json(route, 409, { code: 'PROJECT_REVISION_CONFLICT' });
        return;
      }
      state.operations.set(body.clientOperationId, operation('delete', body.clientOperationId, state.project));
      state.deleted = true;
      await route.abort('failed');
      return;
    }
    const detailMatch = path.match(/^\/api\/projects\/([0-9a-f-]{36})$/i);
    if (detailMatch) {
      if (!state.project || state.deleted) {
        await json(route, 404, { code: 'PROJECT_NOT_FOUND' });
        return;
      }
      if (request.method() === 'PUT') {
        state.updatePuts += 1;
        const body = request.postDataJSON() as {
          name: string;
          description: string | null;
          expectedPersistenceRevision: number;
        };
        if (state.conflictOnFirstUpdate && state.updatePuts === 1) {
          state.project = {
            ...state.project,
            name: '服务端并发版本',
            persistenceRevision: state.project.persistenceRevision + 1,
            modifiedAt: '2026-07-19T00:10:00Z'
          };
          await json(route, 409, {
            code: 'PROJECT_REVISION_CONFLICT',
            expectedRevision: body.expectedPersistenceRevision,
            actualRevision: state.project.persistenceRevision
          });
          return;
        }
        if (body.expectedPersistenceRevision !== state.project.persistenceRevision) {
          await json(route, 409, { code: 'PROJECT_REVISION_CONFLICT' });
          return;
        }
        state.project = {
          ...state.project,
          name: body.name,
          description: body.description,
          persistenceRevision: state.project.persistenceRevision + 1,
          modifiedAt: '2026-07-19T00:05:00Z'
        };
        await json(route, 200, state.project);
        return;
      }
      await json(route, 200, state.project);
      return;
    }
    await json(route, 404, { code: 'UNEXPECTED_F04_G3C_ROUTE' });
  });
  return state;
}

test('F04 G3C completes create reconcile, open, rename, delete reconcile and tombstone journey', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  const initialRuntimeErrors = createF04RuntimeErrorAudit(page);
  const state = await boot(page);
  await page.goto('/studio/index.html#/projects');
  await expect(page.locator('[data-capability="projects-read"]')).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'projects-empty', viewport, runtimeErrors: initialRuntimeErrors, requestAudit: state.audit
    });
  }

  await page.getByRole('button', { name: '新建空白工程' }).click();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'create-project', viewport, runtimeErrors: initialRuntimeErrors, requestAudit: state.audit
    });
  }
  await page.getByLabel('工程名称').fill('生命周期工程');
  await page.getByLabel('工程描述').fill('response-loss reconcile');
  await page.getByRole('button', { name: '创建', exact: true }).click();
  await expect(page.locator('[data-capability="projects-read-detail"]')).toBeVisible();
  expect(state.createPosts).toBe(1);
  expect(state.operationGets).toBe(1);
  const productRuntimeErrors = createF04RuntimeErrorAudit(page);
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'project-detail', viewport, runtimeErrors: productRuntimeErrors, requestAudit: state.audit
    });
  }

  await page.goto('/studio/index.html#/projects');
  await expect(page.getByText('生命周期工程')).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'projects-populated', viewport, runtimeErrors: productRuntimeErrors, requestAudit: state.audit
    });
  }
  await page.goto(`/studio/index.html#/projects/${projectId}`);
  await expect(page.locator('[data-capability="projects-read-detail"]')).toBeVisible();

  await page.getByLabel('工程名称').fill('生命周期工程（已重命名）');
  await page.getByRole('button', { name: '保存工程信息' }).click();
  await expect(page.getByRole('heading', { name: '生命周期工程（已重命名）' })).toBeVisible();
  expect(state.updatePuts).toBe(1);

  await page.getByRole('button', { name: '打开工作区' }).click();
  await expect(page.locator('[data-evidence-surface="f03-workspace-shell"]')).toHaveAttribute(
    'data-workspace-project-id',
    projectId
  );
  await expect(page.locator('[data-evidence-surface="f03-workspace-shell"]')).toHaveAttribute(
    'data-workspace-state',
    'empty'
  );
  expect(state.openPosts).toBeGreaterThanOrEqual(1);
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-empty', viewport, runtimeErrors: productRuntimeErrors, requestAudit: state.audit
    });
  }

  await page.getByRole('link', { name: '工程详情' }).click();
  await page.getByRole('button', { name: '删除', exact: true }).click();
  await expect(page.getByRole('button', { name: '取消' })).toBeFocused();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'destructive-delete', viewport, runtimeErrors: productRuntimeErrors, requestAudit: state.audit
    });
  }
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: '确认删除' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('[data-capability="projects-read"]')).toBeVisible();
  await expect(page.getByText('生命周期工程（已重命名）')).toHaveCount(0);
  expect(state.deletePosts).toBe(1);
  expect(state.operationGets).toBe(2);

  await page.goto(`/studio/index.html#/projects/${projectId}`);
  await expect(page.getByText('工程不存在（404）')).toBeVisible();
  await page.goto(`/studio/index.html#/projects/${projectId}/workspace`);
  await expect(page.locator('[data-evidence-surface="f03-workspace-shell"]')).toHaveAttribute(
    'data-workspace-state',
    'not-found'
  );
  expect(state.audit.filter(entry => entry.method === 'POST' && entry.path.endsWith('/delete'))).toHaveLength(1);
});

test('F04 G3C exposes revision conflict and reloads server authority without auto-overwrite', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  const state = await boot(page, { initialProject: blankProject('本地基线'), conflictOnFirstUpdate: true });
  await page.goto(`/studio/index.html#/projects/${projectId}`);
  await expect(page.locator('[data-capability="projects-read-detail"]')).toBeVisible();

  await page.getByLabel('工程名称').fill('本地覆盖请求');
  await page.getByRole('button', { name: '保存工程信息' }).click();
  await expect(page.getByText('工程 revision 或 mutation 冲突')).toBeVisible();
  await expect(page.getByRole('heading', { name: '本地基线' })).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'project-conflict',
      viewport,
      runtimeErrors: createF04RuntimeErrorAudit(page),
      requestAudit: state.audit,
      notes: ['The expected 409 response occurred before the runtime-error audit used for this screenshot.']
    });
  }

  await page.getByRole('button', { name: '重新读取服务端版本' }).click();
  await expect(page.getByRole('heading', { name: '服务端并发版本' })).toBeVisible();
  await expect(page.getByLabel('工程名称')).toHaveValue('服务端并发版本');
  expect(state.updatePuts).toBe(1);
});
