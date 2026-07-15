import { describe, expect, it, vi } from 'vitest';
import {
  ApiAbortError,
  ApiForbiddenError,
  ApiNotFoundError,
  ApiServerError,
  ApiUnauthorizedError,
  type ApiGetOptions,
  type ApiTransport
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import {
  createProjectDetailsPath,
  createProjectsListQuery,
  createProjectsPath,
  createRecentProjectsPath
} from '@/capabilities/projects-read';

const projectId = '11111111-1111-4111-8111-111111111111';

function project(name = '瓶盖检测'): Record<string, unknown> {
  return {
    id: projectId,
    name,
    description: null,
    version: '1.0.0',
    persistenceRevision: 3,
    createdAt: '2026-07-15T01:00:00Z',
    modifiedAt: null,
    lastOpenedAt: null
  };
}

function httpDetails(status: number) {
  return {
    url: 'http://localhost:5000/api/projects',
    status,
    statusText: 'test',
    payload: undefined,
    responseBody: ''
  };
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

describe('project query definitions', () => {
  it('builds only frozen transport-relative GET paths', () => {
    expect(createProjectsPath('')).toBe('projects');
    expect(createProjectsPath('  瓶盖 A/B  ')).toBe('projects/search?keyword=%E7%93%B6%E7%9B%96+A%2FB');
    expect(createRecentProjectsPath()).toBe('projects/recent?count=5');
    expect(createProjectDetailsPath(projectId)).toBe(`projects/${projectId}`);
    expect(() => createRecentProjectsPath(0)).toThrow(RangeError);
    expect(() => createProjectDetailsPath('not-a-guid')).toThrow(TypeError);
  });

  it.each([
    [new ApiUnauthorizedError(httpDetails(401)), 'unauthorized'],
    [new ApiForbiddenError(httpDetails(403)), 'forbidden'],
    [new ApiNotFoundError(httpDetails(404)), 'not-found'],
    [new ApiServerError(httpDetails(503)), 'error']
  ] as const)('projects shared query maps %s to %s', async (failure, phase) => {
    const client = createReadQueryClient(apiWith(async () => { throw failure; }));
    const owner = createProjectsListQuery(client, () => '');

    const result = await owner.refresh({ force: true });

    expect(result.phase).toBe(phase);
    owner.dispose();
    client.dispose();
  });

  it('maps an empty list to the shared empty phase', async () => {
    const client = createReadQueryClient(apiWith(async () => []));
    const owner = createProjectsListQuery(client, () => '');

    await expect(owner.refresh()).resolves.toMatchObject({ phase: 'empty', data: [] });
    owner.dispose();
    client.dispose();
  });

  it('keeps previous data as stale after a server failure', async () => {
    let attempt = 0;
    const client = createReadQueryClient(apiWith(async () => {
      attempt += 1;
      if (attempt === 1) return [project()];
      throw new ApiServerError(httpDetails(503));
    }));
    const owner = createProjectsListQuery(client, () => '');

    await owner.refresh({ force: true });
    const stale = await owner.refresh({ force: true });

    expect(stale).toMatchObject({
      phase: 'stale',
      data: [{ id: projectId, name: '瓶盖检测' }]
    });
    owner.dispose();
    client.dispose();
  });

  it('aborts superseded search requests and lets the latest response win', async () => {
    const pending: Array<{
      readonly path: string;
      readonly signal?: AbortSignal;
      resolve(value: unknown): void;
      reject(error: unknown): void;
    }> = [];
    const get = vi.fn((_path: string, options: ApiGetOptions = {}) => new Promise<unknown>((resolve, reject) => {
      const entry = {
        path: _path,
        ...(options.signal ? { signal: options.signal } : {}),
        resolve,
        reject
      };
      options.signal?.addEventListener('abort', () => reject(new ApiAbortError(_path)), { once: true });
      pending.push(entry);
    }));
    const client = createReadQueryClient(apiWith(get));
    let term = 'first';
    const owner = createProjectsListQuery(client, () => term);

    const first = owner.refresh({ force: true });
    await Promise.resolve();
    term = 'second';
    const second = owner.refresh({ force: true });
    await Promise.resolve();

    expect(pending[0]?.signal?.aborted).toBe(true);
    expect(pending[1]?.path).toBe('projects/search?keyword=second');
    pending[1]?.resolve([project('第二次查询')]);
    await Promise.all([first, second]);

    expect(owner.state.value).toMatchObject({
      phase: 'success',
      data: [{ name: '第二次查询' }]
    });
    owner.dispose();
    client.dispose();
  });

  it('treats a valid JSON value with the wrong DTO shape as a contract error', async () => {
    const client = createReadQueryClient(apiWith(async () => ({ items: [] })));
    const owner = createProjectsListQuery(client, () => '');

    const result = await owner.refresh({ force: true });

    expect(result).toMatchObject({ phase: 'error', failure: { kind: 'decode' } });
    owner.dispose();
    client.dispose();
  });
});
