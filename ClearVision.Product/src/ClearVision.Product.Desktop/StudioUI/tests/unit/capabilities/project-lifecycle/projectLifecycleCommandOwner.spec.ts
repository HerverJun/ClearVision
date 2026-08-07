import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createProjectLifecycleCommandOwner,
  type ProjectLifecycleCommandOwner
} from '@/capabilities/project-lifecycle';
import {
  ApiConflictError,
  ApiNetworkError,
  ApiUnauthorizedError,
  type ApiGetOptions,
  type ApiBlobResponse,
  type ApiTransport,
  type ApiWriteOptions
} from '@/platform/api';

const projectA = '11111111-1111-4111-8111-111111111111';
const projectB = '22222222-2222-4222-8222-222222222222';
const operationA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const operationB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const flowId = '33333333-3333-4333-8333-333333333333';

let owner: ProjectLifecycleCommandOwner | undefined;

afterEach(() => {
  owner?.dispose('test-cleanup');
  owner = undefined;
});

function project(projectId = projectA, overrides: Record<string, unknown> = {}) {
  return {
    id: projectId,
    name: projectId === projectA ? '工程 A' : '工程 B',
    description: null,
    version: '1.0.0',
    persistenceRevision: 3,
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
    createdAt: '2026-07-19T00:00:00Z',
    modifiedAt: null,
    lastOpenedAt: null,
    ...overrides
  };
}

function importDocument() {
  return {
    documentType: 'clearvision-project' as const,
    schemaVersion: 1 as const,
    identity: {
      sourceProjectId: projectA,
      sourcePersistenceRevision: 3
    },
    project: { name: '导入工程', description: '来自 JSON', version: '1.0.0' },
    flow: { name: '空流程', operators: [], connections: [] },
    globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
  };
}

function operation(kind: 'create' | 'delete' | 'import', operationId: string, projectId = projectA, overrides: Record<string, unknown> = {}) {
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
          project: project(projectId),
          projectDeleted: false,
          deleted: false,
          alreadyDeleted: false,
          cleanupStatus: 'not-required'
        },
    errorCode: null,
    createdAtUtc: '2026-07-19T00:00:00Z',
    updatedAtUtc: '2026-07-19T00:00:01Z',
    expiresAtUtc: '2026-07-26T00:00:01Z',
    ...overrides
  };
}

function httpDetails(status: number, code?: string) {
  return {
    url: `http://localhost:5000/api/projects/${projectA}`,
    status,
    statusText: 'test',
    payload: code ? { code } : undefined,
    responseBody: code ? JSON.stringify({ code }) : ''
  };
}

function apiWith(options: {
  get?: (path: string, requestOptions?: ApiGetOptions) => Promise<unknown>;
  getBlob?: (path: string, requestOptions?: ApiGetOptions) => Promise<ApiBlobResponse>;
  post?: (path: string, body: unknown, requestOptions?: ApiWriteOptions) => Promise<unknown>;
  put?: (path: string, body: unknown, requestOptions?: ApiWriteOptions) => Promise<unknown>;
} = {}): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T>(path: string, requestOptions?: ApiGetOptions): Promise<T | undefined> {
      return await (options.get?.(path, requestOptions) ?? Promise.resolve(undefined)) as T | undefined;
    },
    async post<T>(path: string, body: unknown, requestOptions?: ApiWriteOptions): Promise<T | undefined> {
      return await (options.post?.(path, body, requestOptions) ?? Promise.resolve(undefined)) as T | undefined;
    },
    async put<T>(path: string, body: unknown, requestOptions?: ApiWriteOptions): Promise<T | undefined> {
      return await (options.put?.(path, body, requestOptions) ?? Promise.resolve(undefined)) as T | undefined;
    },
    ...(options.getBlob ? { getBlob: options.getBlob } : {})
  };
}

function createOwner(api: ApiTransport, ids: string[] = [operationA], prepareProjectLeave?: () => Promise<boolean>) {
  owner = createProjectLifecycleCommandOwner({
    api,
    createOperationId: () => ids.shift() ?? operationB,
    publishToWindow: false,
    ...(prepareProjectLeave ? { prepareProjectLeave: async () => await prepareProjectLeave() } : {})
  });
  return owner;
}

describe('projectLifecycleCommandOwner', () => {
  it('enforces one mounted owner and disposes its ledger', () => {
    const api = apiWith();
    const first = createOwner(api);

    expect(first.diagnostics.ownerCount).toBe(1);
    expect(() => createProjectLifecycleCommandOwner({ api, publishToWindow: false }))
      .toThrow('already has an active mounted owner');

    first.dispose();
    expect(first.diagnostics).toMatchObject({ ownerCount: 0, disposed: true });
    owner = undefined;
  });

  it('deduplicates duplicate create clicks and keeps one stable operation id', async () => {
    let resolvePost!: (value: unknown) => void;
    const post = vi.fn((...args: [string, unknown]) => {
      void args;
      return new Promise<unknown>(resolve => { resolvePost = resolve; });
    });
    const commandOwner = createOwner(apiWith({ post }));

    const first = commandOwner.createBlank({ name: '工程 A' });
    const second = commandOwner.createBlank({ name: '另一个按钮值不会创建第二 operation' });
    expect(first).toBe(second);
    expect(post).toHaveBeenCalledTimes(1);
    expect(post.mock.calls[0]?.[1]).toMatchObject({ clientOperationId: operationA });

    resolvePost({
      projectId: projectA,
      project: project(),
      operationReplayed: false,
      operation: operation('create', operationA)
    });
    await expect(first).resolves.toMatchObject({ projectId: projectA });
    expect(commandOwner.projection).toMatchObject({ phase: 'succeeded', projectId: projectA });
  });

  it('reconciles create response loss by operation query without a second POST', async () => {
    const post = vi.fn(async () => { throw new ApiNetworkError('projects', new Error('socket reset')); });
    const get = vi.fn(async () => operation('create', operationA));
    const commandOwner = createOwner(apiWith({ post, get }));

    await expect(commandOwner.createBlank({ name: '工程 A' })).resolves.toMatchObject({ projectId: projectA });

    expect(post).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenCalledWith(
      `project-operations/${operationA}?kind=create`,
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(commandOwner.projection).toMatchObject({ phase: 'succeeded', command: 'create' });
    expect(commandOwner.diagnostics.totalReconcileCount).toBe(1);
  });

  it('keeps unknown outcome when reconcile is still pending and later settles explicitly', async () => {
    let queryCount = 0;
    const commandOwner = createOwner(apiWith({
      post: async () => { throw new ApiNetworkError('projects', new Error('response lost')); },
      get: async () => {
        queryCount += 1;
        return queryCount === 1
          ? operation('create', operationA, projectA, { status: 'pending', result: null })
          : operation('create', operationA);
      }
    }));

    await expect(commandOwner.createBlank({ name: '工程 A' })).resolves.toBeNull();
    expect(commandOwner.projection).toMatchObject({ phase: 'unknown-outcome', canReconcile: true });
    await expect(commandOwner.reconcile()).resolves.toMatchObject({ projectId: projectA });
    expect(commandOwner.projection.phase).toBe('succeeded');
  });

  it('maps structured revision conflict without parsing an exception message', async () => {
    const commandOwner = createOwner(apiWith({
      put: async () => { throw new ApiConflictError(httpDetails(409, 'PROJECT_REVISION_CONFLICT')); }
    }));

    await expect(commandOwner.updateProject({
      projectId: projectA,
      name: '工程 A',
      expectedPersistenceRevision: 3
    })).resolves.toBeNull();

    expect(commandOwner.projection).toMatchObject({
      phase: 'conflict',
      errorCode: 'PROJECT_REVISION_CONFLICT',
      projectId: projectA
    });
  });

  it('requires explicit open authority and ignores Project A late response after switching to B', async () => {
    const pending = new Map<string, (value: unknown) => void>();
    const post = vi.fn((path: string) => new Promise<unknown>(resolve => pending.set(path, resolve)));
    const commandOwner = createOwner(apiWith({ post }));

    commandOwner.setProjectScope(projectA);
    const first = commandOwner.openProject(projectA);
    commandOwner.setProjectScope(projectB);
    const second = commandOwner.openProject(projectB);
    pending.get(`projects/${projectB}/open`)?.({
      projectId: projectB,
      lastOpenedAtUtc: '2026-07-19T00:00:02Z'
    });
    await expect(second).resolves.toMatchObject({ projectId: projectB });
    pending.get(`projects/${projectA}/open`)?.({
      projectId: projectA,
      lastOpenedAtUtc: '2026-07-19T00:00:03Z'
    });
    await expect(first).resolves.toBeNull();

    expect(commandOwner.projection).toMatchObject({
      phase: 'succeeded',
      projectId: projectB,
      openedAtUtc: '2026-07-19T00:00:02Z'
    });
  });

  it('blocks delete before POST when Workspace leave protection does not settle', async () => {
    const post = vi.fn();
    const leave = vi.fn(async () => false);
    const commandOwner = createOwner(apiWith({ post }), [operationA], leave);

    await expect(commandOwner.deleteProject({
      projectId: projectA,
      expectedPersistenceRevision: 3
    })).resolves.toBeNull();

    expect(leave).toHaveBeenCalledOnce();
    expect(post).not.toHaveBeenCalled();
    expect(commandOwner.projection).toMatchObject({
      phase: 'failed',
      errorCode: 'PROJECT_LEAVE_BLOCKED'
    });
  });

  it('reconciles delete response loss and preserves cleanup-pending authority', async () => {
    const post = vi.fn(async () => { throw new ApiNetworkError('delete', new Error('response lost')); });
    const get = vi.fn(async () => operation('delete', operationA));
    const commandOwner = createOwner(apiWith({ post, get }), [operationA], async () => true);

    await expect(commandOwner.deleteProject({
      projectId: projectA,
      expectedPersistenceRevision: 3
    })).resolves.toMatchObject({
      projectId: projectA,
      operation: { result: { cleanupStatus: 'cleanup-pending' } }
    });
    expect(post).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('imports a validated document through one stable operation identity', async () => {
    const post = vi.fn(async (path: string, body: unknown) => {
      expect(path).toBe('projects/import');
      expect(body).toMatchObject({
        mode: 'CREATE_NEW',
        clientOperationId: operationA,
        document: importDocument()
      });
      return {
        projectId: projectA,
        project: project(projectA, { name: '导入工程' }),
        operationReplayed: false,
        operation: operation('import', operationA)
      };
    });
    const commandOwner = createOwner(apiWith({ post }));

    const result = await commandOwner.importProject({
      mode: 'CREATE_NEW',
      document: importDocument()
    });

    expect(result).toMatchObject({ projectId: projectA, project: { name: '导入工程' } });
    expect(post).toHaveBeenCalledTimes(1);
    expect(commandOwner.projection).toMatchObject({
      phase: 'succeeded',
      command: 'import',
      projectId: projectA
    });
  });

  it('reconciles an overwrite import response loss without posting a second import', async () => {
    const post = vi.fn(async () => { throw new ApiNetworkError('projects/import', new Error('response lost')); });
    const get = vi.fn(async (path: string) => {
      expect(path).toBe(`project-operations/${operationA}?kind=import`);
      return operation('import', operationA, projectB);
    });
    const commandOwner = createOwner(apiWith({ post, get }));

    const result = await commandOwner.importProject({
      mode: 'OVERWRITE_EXISTING',
      document: importDocument(),
      targetProjectId: projectB,
      expectedPersistenceRevision: 3
    });

    expect(result).toMatchObject({ projectId: projectB, operation: { kind: 'import' } });
    expect(post).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenCalledTimes(1);
    expect(commandOwner.diagnostics.totalReconcileCount).toBe(1);
  });

  it('validates the canonical export blob and returns a safe download name', async () => {
    const getBlob = vi.fn(async (path: string) => {
      expect(path).toBe(`projects/${projectA}/export`);
      const blob = new Blob([JSON.stringify(importDocument())], { type: 'application/json' });
      return {
        blob,
        contentType: 'application/json',
        contentLength: blob.size,
        etag: null,
        sha256: null,
        headers: new Headers({ 'Content-Disposition': 'attachment; filename="project-export.json"' })
      };
    });
    const commandOwner = createOwner(apiWith({ getBlob }));

    const result = await commandOwner.exportProject(projectA);

    expect(result).toMatchObject({
      projectId: projectA,
      fileName: 'project-export.json',
      document: { documentType: 'clearvision-project', schemaVersion: 1 }
    });
    expect(getBlob).toHaveBeenCalledOnce();
    expect(commandOwner.projection).toMatchObject({ phase: 'succeeded', command: 'export' });
  });

  it('hands unauthorized responses to Auth authority and preserves no private token state', async () => {
    const commandOwner = createOwner(apiWith({
      post: async () => { throw new ApiUnauthorizedError(httpDetails(401)); }
    }));

    await expect(commandOwner.openProject(projectA)).resolves.toBeNull();
    expect(commandOwner.projection).toMatchObject({
      phase: 'failed',
      errorCode: 'SESSION_UNAUTHORIZED'
    });
    expect(commandOwner.projection).not.toHaveProperty('token');
  });

  it('preserves an unknown operation across session quarantine and reconciles after reauthentication', async () => {
    let authenticated = false;
    const commandOwner = createOwner(apiWith({
      post: async () => { throw new ApiNetworkError('projects', new Error('response lost')); },
      get: async () => {
        if (!authenticated) throw new ApiUnauthorizedError(httpDetails(401));
        return operation('create', operationA);
      }
    }));

    await expect(commandOwner.createBlank({ name: '工程 A' })).resolves.toBeNull();
    expect(commandOwner.projection).toMatchObject({
      phase: 'unknown-outcome',
      errorCode: 'SESSION_UNAUTHORIZED',
      canReconcile: true
    });
    expect(commandOwner.quarantineForSessionExpiration()).toBe(true);

    authenticated = true;
    await expect(commandOwner.reconcileAfterReauthentication()).resolves.toBe(true);
    expect(commandOwner.projection).toMatchObject({ phase: 'succeeded', projectId: projectA });
  });

  it('ignores late responses after disposal', async () => {
    let resolvePost!: (value: unknown) => void;
    const commandOwner = createOwner(apiWith({
      post: async () => await new Promise<unknown>(resolve => { resolvePost = resolve; })
    }));
    const pending = commandOwner.openProject(projectA);

    commandOwner.dispose('route-disposed');
    owner = undefined;
    resolvePost({ projectId: projectA, lastOpenedAtUtc: '2026-07-19T00:00:04Z' });
    await expect(pending).resolves.toBeNull();

    expect(commandOwner.projection.phase).toBe('disposed');
    expect(commandOwner.diagnostics).toMatchObject({
      ownerCount: 0,
      activeAbortControllerCount: 0,
      disposed: true
    });
  });
});
