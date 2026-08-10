import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createProjectLifecycleCommandOwner,
  type ProjectLifecycleCommandOwner
} from '@/capabilities/project-lifecycle';
import { ProjectDetailPage, ProjectsPage } from '@/capabilities/projects-read';
import ProjectsRecentPanel from '@/capabilities/projects-read/ProjectsRecentPanel.vue';
import type { ProjectSummary } from '@/capabilities/projects-read/projectContracts';
import {
  ApiNotFoundError,
  type ApiBlobResponse,
  type ApiGetOptions,
  type ApiTransport,
  type ApiWriteOptions
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

const projectId = '11111111-1111-4111-8111-111111111111';
const flowId = '22222222-2222-4222-8222-222222222222';
const operationId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
let lifecycle: ProjectLifecycleCommandOwner | undefined;

afterEach(() => {
  lifecycle?.dispose('projects-page-test-cleanup');
  lifecycle = undefined;
  document.body.innerHTML = '';
});

function summary(overrides: Record<string, unknown> = {}): ProjectSummary & Record<string, unknown> {
  return {
    id: projectId,
    name: '瓶盖检测',
    description: '稳定摘要',
    version: '1.0.0',
    persistenceRevision: 7,
    createdAt: '2026-07-15T01:00:00Z',
    modifiedAt: '2026-07-15T02:00:00Z',
    lastOpenedAt: '2026-07-15T03:00:00Z',
    ...overrides
  } as ProjectSummary & Record<string, unknown>;
}

function details(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return summary({
    flow: {
      id: flowId,
      name: '空流程',
      operators: [],
      connections: [],
      decisionConfiguration: null
    },
    globalSettings: {},
    globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] },
    ...overrides
  });
}

function importDocument(): Record<string, unknown> {
  return {
    documentType: 'clearvision-project',
    schemaVersion: 1,
    identity: { sourceProjectId: projectId, sourcePersistenceRevision: 7 },
    project: { name: '导入工程', description: '导入描述', version: '1.0.0' },
    flow: { name: '导入流程', operators: [], connections: [] },
    globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
  };
}

function lifecycleOperation(kind: 'create' | 'delete' | 'import') {
  return {
    clientOperationId: operationId,
    kind,
    status: 'completed',
    projectId,
    result: kind === 'delete'
      ? {
          project: null,
          projectDeleted: true,
          deleted: true,
          alreadyDeleted: false,
          cleanupStatus: 'cleanup-pending'
        }
      : {
          project: details(),
          projectDeleted: false,
          deleted: false,
          alreadyDeleted: false,
          cleanupStatus: 'not-required'
        },
    errorCode: null,
    createdAtUtc: '2026-07-19T00:00:00Z',
    updatedAtUtc: '2026-07-19T00:00:01Z',
    expiresAtUtc: '2026-07-26T00:00:01Z'
  };
}

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/projects', component: { template: '<div />' } },
      { path: '/projects/:projectId', component: { template: '<div />' } },
      { path: '/projects/:projectId/workspace', component: { template: '<div />' } }
    ]
  });
}

type GetImplementation = (path: string, options?: ApiGetOptions) => Promise<unknown>;

function apiWith(implementation: GetImplementation): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await implementation(path, options) as T | undefined;
    }
  };
}

function commandApi(options: {
  get: GetImplementation;
  post: (path: string, body: unknown, requestOptions?: ApiWriteOptions) => Promise<unknown>;
  put?: (path: string, body: unknown, requestOptions?: ApiWriteOptions) => Promise<unknown>;
  getBlob?: (path: string, requestOptions?: ApiGetOptions) => Promise<ApiBlobResponse>;
}): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T>(path: string, requestOptions?: ApiGetOptions): Promise<T | undefined> {
      return await options.get(path, requestOptions) as T | undefined;
    },
    async post<T>(path: string, body: unknown, requestOptions?: ApiWriteOptions): Promise<T | undefined> {
      return await options.post(path, body, requestOptions) as T | undefined;
    },
    async put<T>(path: string, body: unknown, requestOptions?: ApiWriteOptions): Promise<T | undefined> {
      return await (options.put?.(path, body, requestOptions) ?? Promise.resolve(undefined)) as T | undefined;
    },
    ...(options.getBlob ? { getBlob: options.getBlob } : {})
  };
}

describe('Projects read pages', () => {
  it('keeps recent Projects visible while refresh is in progress or stale', async () => {
    const router = createTestRouter();
    await router.push('/projects');
    await router.isReady();
    const wrapper = mount(ProjectsRecentPanel, {
      props: {
        phase: 'success',
        projects: [summary()],
        isRefreshing: true,
        canOpen: false,
        busy: false
      },
      global: { plugins: [router] }
    });

    expect(wrapper.text()).toContain('正在刷新，暂时显示上次读取的最近工程');
    expect(wrapper.text()).toContain('瓶盖检测');

    await wrapper.setProps({ phase: 'partial-failure', isRefreshing: false });
    expect(wrapper.text()).toContain('最近工程刷新未完成');
    expect(wrapper.text()).toContain('瓶盖检测');

    wrapper.unmount();
  });

  it('renders only stable list fields even when the list payload contains misleading Flow counts', async () => {
    const api = apiWith(async path => path.startsWith('projects/recent')
      ? []
      : [summary({
          flow: {
            operators: new Array(40).fill({}),
            connections: new Array(50).fill({})
          }
        })]);
    const queries = createReadQueryClient(api);
    const router = createTestRouter();
    await router.push('/projects');
    await router.isReady();

    const wrapper = mount(ProjectsPage, {
      props: { runtime: { queries } },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('瓶盖检测');
    expect(wrapper.text()).toContain('稳定摘要');
    expect(wrapper.text()).not.toContain('算子数量');
    expect(wrapper.text()).not.toContain('连接数量');
    expect(wrapper.text()).not.toContain('40');
    expect(wrapper.text()).not.toContain('50');
    expect(wrapper.get(`a.projects-page__detail[href="/projects/${projectId}"]`).text()).toBe('查看详情');

    wrapper.unmount();
    queries.dispose();
  });

  it('renders detail counts, decision and assets only from the detail response', async () => {
    const detail = summary({
      flow: {
        id: flowId,
        name: '主流程',
        operators: [{ id: 'a' }, { id: 'b' }],
        connections: [{ id: 'c' }],
        decisionConfiguration: {
          finalDecisionBinding: { sourceOperatorId: 'b' },
          missingDecisionPolicy: 'Undetermined'
        }
      },
      assets: {
        schemaVersion: 1,
        calibrationAssets: [{ assetId: 'calibration' }],
        spatialAssets: [{ assetId: 'spatial-a' }, { assetId: 'spatial-b' }]
      }
    });
    const queries = createReadQueryClient(apiWith(async () => detail));
    const router = createTestRouter();
    await router.push(`/projects/${projectId}`);
    await router.isReady();

    const wrapper = mount(ProjectDetailPage, {
      props: { projectId, runtime: { queries }, workspaceEnabled: true },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('工程版本');
    expect(wrapper.text()).not.toContain('流程版本');
    expect(wrapper.text()).toContain('算子数量');
    expect(wrapper.text()).toContain('连接数量');
    expect(wrapper.text()).toContain('已配置（缺失策略：Undetermined）');
    expect(wrapper.text()).toContain('标定资源');
    expect(wrapper.text()).toContain('空间资源');
    expect(wrapper.text()).toContain('2');
    expect(wrapper.find('[data-testid="project-detail-open"]').exists()).toBe(false);

    wrapper.unmount();
    queries.dispose();
  });

  it('calls explicit open authority before routing from Project Detail to Workspace', async () => {
    const post = vi.fn(async (path: string) => {
      expect(path).toBe(`projects/${projectId}/open`);
      return {
        projectId,
        lastOpenedAtUtc: '2026-07-19T00:00:02Z'
      };
    });
    const api = commandApi({ get: async () => details(), post });
    const queries = createReadQueryClient(api);
    lifecycle = createProjectLifecycleCommandOwner({ api, publishToWindow: false });
    const router = createTestRouter();
    await router.push(`/projects/${projectId}`);
    await router.isReady();
    const wrapper = mount(ProjectDetailPage, {
      props: {
        projectId,
        runtime: { queries, projectLifecycle: lifecycle },
        workspaceEnabled: true
      },
      global: { plugins: [router] }
    });
    await flushPromises();

    await wrapper.get('[data-testid="project-detail-open"]').trigger('click');
    await flushPromises();

    expect(post).toHaveBeenCalledWith(
      `projects/${projectId}/open`,
      {},
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(router.currentRoute.value.path).toBe(`/projects/${projectId}/workspace`);
    wrapper.unmount();
    lifecycle.dispose();
    lifecycle = undefined;
    queries.dispose();
  });

  it('creates a blank Project once and routes using only the server-issued ProjectId', async () => {
    let projects: Record<string, unknown>[] = [];
    const post = vi.fn(async (path: string, body: unknown) => {
      expect(path).toBe('projects');
      expect(body).toEqual({
        clientOperationId: operationId,
        name: '新建工程',
        description: '空白创建'
      });
      projects = [summary({ name: '新建工程', description: '空白创建' })];
      return {
        projectId,
        project: details({ name: '新建工程', description: '空白创建' }),
        operationReplayed: false,
        operation: lifecycleOperation('create')
      };
    });
    const api = commandApi({
      get: async path => path.startsWith('projects/recent') ? [] : projects,
      post
    });
    const queries = createReadQueryClient(api);
    lifecycle = createProjectLifecycleCommandOwner({
      api,
      createOperationId: () => operationId,
      publishToWindow: false
    });
    const router = createTestRouter();
    await router.push('/projects');
    await router.isReady();
    const wrapper = mount(ProjectsPage, {
      props: { runtime: { queries, projectLifecycle: lifecycle } },
      global: { plugins: [router] }
    });
    await flushPromises();

    await wrapper.get('[data-testid="project-create-open"]').trigger('click');
    const inputs = [...document.body.querySelectorAll<HTMLInputElement>('.cv-modal input')];
    expect(inputs).toHaveLength(2);
    await inputs[0]?.dispatchEvent(new Event('input', { bubbles: true }));
    if (inputs[0]) inputs[0].value = '新建工程';
    inputs[0]?.dispatchEvent(new Event('input', { bubbles: true }));
    if (inputs[1]) inputs[1].value = '空白创建';
    inputs[1]?.dispatchEvent(new Event('input', { bubbles: true }));
    (document.body.querySelector('[data-testid="project-create-submit"]') as HTMLButtonElement).click();
    await flushPromises();

    expect(post).toHaveBeenCalledTimes(1);
    expect(router.currentRoute.value.path).toBe(`/projects/${projectId}`);
    wrapper.unmount();
    lifecycle.dispose();
    lifecycle = undefined;
    queries.dispose();
  });

  it('reads a Project JSON file, submits CREATE_NEW through the lifecycle owner, and routes to the result', async () => {
    const post = vi.fn(async (path: string, body: unknown) => {
      expect(path).toBe('projects/import');
      expect(body).toMatchObject({ mode: 'CREATE_NEW', clientOperationId: operationId });
      return {
        projectId,
        project: details({ name: '导入工程', description: '导入描述' }),
        operationReplayed: false,
        operation: lifecycleOperation('import')
      };
    });
    const api = commandApi({
      get: async path => path.startsWith('projects/recent') ? [] : [summary()],
      post
    });
    const queries = createReadQueryClient(api);
    lifecycle = createProjectLifecycleCommandOwner({
      api,
      createOperationId: () => operationId,
      publishToWindow: false
    });
    const router = createTestRouter();
    await router.push('/projects');
    await router.isReady();
    const wrapper = mount(ProjectsPage, {
      props: { runtime: { queries, projectLifecycle: lifecycle } },
      global: { plugins: [router] }
    });
    await flushPromises();

    const file = new File([JSON.stringify(importDocument())], 'project.json', { type: 'application/json' });
    const fileInput = wrapper.get('input[type="file"]');
    Object.defineProperty(fileInput.element, 'files', { value: [file], configurable: true });
    await fileInput.trigger('change');
    await flushPromises();

    expect(document.body.textContent).toContain('project.json');
    (document.body.querySelector('[data-testid="project-import-submit"]') as HTMLButtonElement).click();
    await flushPromises();

    expect(post).toHaveBeenCalledTimes(1);
    expect(router.currentRoute.value.path).toBe(`/projects/${projectId}`);
    wrapper.unmount();
    lifecycle.dispose();
    lifecycle = undefined;
    queries.dispose();
  });

  it('downloads the canonical server export and releases its temporary object URL', async () => {
    const getBlob = vi.fn(async (path: string) => {
      expect(path).toBe(`projects/${projectId}/export`);
      const blob = new Blob([JSON.stringify(importDocument())], { type: 'application/json' });
      return {
        blob,
        contentType: 'application/json',
        contentLength: blob.size,
        etag: null,
        sha256: null,
        headers: new Headers({ 'Content-Disposition': 'attachment; filename="project.json"' })
      };
    });
    const api = commandApi({
      get: async path => path.startsWith('projects/recent') ? [] : [summary()],
      post: async () => undefined,
      getBlob
    });
    const queries = createReadQueryClient(api);
    lifecycle = createProjectLifecycleCommandOwner({ api, publishToWindow: false });
    const createObjectUrl = vi.fn(() => 'blob:project-export');
    const revokeObjectUrl = vi.fn();
    vi.stubGlobal('URL', { createObjectURL: createObjectUrl, revokeObjectURL: revokeObjectUrl });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const router = createTestRouter();
    await router.push('/projects');
    await router.isReady();
    const wrapper = mount(ProjectsPage, {
      props: { runtime: { queries, projectLifecycle: lifecycle } },
      global: { plugins: [router] }
    });
    await flushPromises();

    await wrapper.get(`[data-testid="project-export-${projectId}"]`).trigger('click');
    await flushPromises();

    expect(getBlob).toHaveBeenCalledOnce();
    expect(click).toHaveBeenCalledOnce();
    expect(createObjectUrl).toHaveBeenCalledOnce();
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:project-export');
    wrapper.unmount();
    lifecycle.dispose();
    lifecycle = undefined;
    queries.dispose();
    click.mockRestore();
    vi.unstubAllGlobals();
  });

  it('keeps a Project visible until delete tombstone authority completes', async () => {
    let projects = [summary()];
    let resolveDelete!: (value: unknown) => void;
    const post = vi.fn(async (path: string) => {
      if (!path.endsWith('/delete')) throw new Error(`Unexpected POST ${path}`);
      return await new Promise<unknown>(resolve => { resolveDelete = resolve; });
    });
    const api = commandApi({
      get: async path => path.startsWith('projects/recent') ? projects : projects,
      post
    });
    const queries = createReadQueryClient(api);
    lifecycle = createProjectLifecycleCommandOwner({
      api,
      createOperationId: () => operationId,
      publishToWindow: false
    });
    const router = createTestRouter();
    await router.push('/projects');
    await router.isReady();
    const wrapper = mount(ProjectsPage, {
      props: { runtime: { queries, projectLifecycle: lifecycle } },
      global: { plugins: [router] }
    });
    await flushPromises();

    await wrapper.get(`[data-testid="project-delete-${projectId}"]`).trigger('click');
    (document.body.querySelector('[data-testid="project-delete-confirm"]') as HTMLButtonElement).click();
    await Promise.resolve();
    expect(wrapper.text()).toContain('瓶盖检测');

    projects = [];
    resolveDelete({
      projectId,
      operationReplayed: false,
      operation: lifecycleOperation('delete')
    });
    await flushPromises();

    expect(wrapper.text()).not.toContain('瓶盖检测');
    expect(lifecycle.projection.phase).toBe('succeeded');
    wrapper.unmount();
    lifecycle.dispose();
    lifecycle = undefined;
    queries.dispose();
  });

  it('shows the product not-found state for a missing project', async () => {
    const queries = createReadQueryClient(apiWith(async () => {
      throw new ApiNotFoundError({
        url: `http://localhost:5000/api/projects/${projectId}`,
        status: 404,
        statusText: 'Not Found',
        payload: undefined,
        responseBody: ''
      });
    }));
    const router = createTestRouter();
    await router.push(`/projects/${projectId}`);
    await router.isReady();

    const wrapper = mount(ProjectDetailPage, {
      props: { projectId, runtime: { queries } },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('工程不存在');
    expect(wrapper.text()).toContain('404');

    wrapper.unmount();
    queries.dispose();
  });

  it('renders the project list as read-only when no command owner is mounted', async () => {
    const queries = createReadQueryClient(apiWith(async path =>
      path.startsWith('projects/recent') ? [] : [summary()]));
    const router = createTestRouter();
    await router.push('/projects');
    await router.isReady();

    const wrapper = mount(ProjectsPage, {
      props: { runtime: { queries } },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.find('[data-capability="projects-read"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="project-create-open"]').exists()).toBe(false);
    expect(wrapper.find(`[data-testid="project-open-${projectId}"]`).exists()).toBe(false);
    expect(wrapper.find(`[data-testid="project-delete-${projectId}"]`).exists()).toBe(false);
    expect(wrapper.text()).not.toContain('新建空白工程');

    wrapper.unmount();
    queries.dispose();
  });

  it('renders project details without edit, delete, or workspace controls when no command owner is mounted', async () => {
    const queries = createReadQueryClient(apiWith(async () => details()));
    const router = createTestRouter();
    await router.push(`/projects/${projectId}`);
    await router.isReady();

    const wrapper = mount(ProjectDetailPage, {
      props: { projectId, runtime: { queries }, workspaceEnabled: true },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.find('[data-testid="project-detail-open"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="project-detail-delete"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="project-detail-update"]').exists()).toBe(false);
    expect(wrapper.text()).not.toContain('编辑工程信息');

    wrapper.unmount();
    queries.dispose();
  });
});
