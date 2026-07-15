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
  createOperatorCatalogQuery,
  createOperatorDetailPath,
  createOperatorDetailQuery,
  operatorCatalogPath
} from '@/capabilities/operators-read';

function operator(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    type: 45,
    displayName: '颜色分析',
    description: '',
    categoryId: 8,
    category: 'AI推理',
    lifecycle: 0,
    lifecycleNote: null,
    defaultHidden: false,
    iconName: null,
    keywords: [],
    tags: [],
    version: '1.0.0',
    inputPorts: [],
    outputPorts: [],
    parameters: [],
    ...overrides
  };
}

function details(status: number) {
  return {
    url: 'http://localhost:5000/api/operators/library',
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

describe('operator queries', () => {
  it('builds only the frozen GET paths', () => {
    expect(operatorCatalogPath).toBe('operators/library?includeCompatibility=true');
    expect(createOperatorDetailPath('ColorDetection')).toBe('operators/ColorDetection/metadata');
    expect(createOperatorDetailPath('45')).toBe('operators/45/metadata');
    expect(() => createOperatorDetailPath('../preview')).toThrow(TypeError);
  });

  it.each([
    [new ApiUnauthorizedError(details(401)), 'unauthorized'],
    [new ApiForbiddenError(details(403)), 'forbidden'],
    [new ApiServerError(details(503)), 'error']
  ] as const)('maps catalog %s to %s', async (failure, phase) => {
    const client = createReadQueryClient(apiWith(async () => { throw failure; }));
    const owner = createOperatorCatalogQuery(client);
    await expect(owner.refresh({ force: true })).resolves.toMatchObject({ phase });
    owner.dispose();
    client.dispose();
  });

  it('maps detail 404 to not-found', async () => {
    const client = createReadQueryClient(apiWith(async () => { throw new ApiNotFoundError(details(404)); }));
    const owner = createOperatorDetailQuery(client, () => '45');
    await expect(owner.refresh({ force: true })).resolves.toMatchObject({ phase: 'not-found' });
    owner.dispose();
    client.dispose();
  });

  it('keeps the previous catalog stale after a server failure', async () => {
    let attempt = 0;
    const client = createReadQueryClient(apiWith(async () => {
      attempt += 1;
      if (attempt === 1) return [operator()];
      throw new ApiServerError(details(503));
    }));
    const owner = createOperatorCatalogQuery(client);
    await owner.refresh({ force: true });
    await expect(owner.refresh({ force: true })).resolves.toMatchObject({
      phase: 'stale',
      data: [{ operatorType: '45' }]
    });
    owner.dispose();
    client.dispose();
  });

  it('aborts a superseded detail request and lets the latest response win', async () => {
    const pending: Array<{ signal?: AbortSignal; resolve(value: unknown): void; reject(error: unknown): void }> = [];
    const get = vi.fn((path: string, options: ApiGetOptions = {}) => new Promise<unknown>((resolve, reject) => {
      const entry = { ...(options.signal ? { signal: options.signal } : {}), resolve, reject };
      options.signal?.addEventListener('abort', () => reject(new ApiAbortError(path)), { once: true });
      pending.push(entry);
    }));
    const client = createReadQueryClient(apiWith(get));
    let type = '45';
    const owner = createOperatorDetailQuery(client, () => type);
    const first = owner.refresh({ force: true });
    await Promise.resolve();
    type = '46';
    const second = owner.refresh({ force: true });
    await Promise.resolve();
    expect(pending[0]?.signal?.aborted).toBe(true);
    pending[1]?.resolve(operator({ type: 46, displayName: '第二个算子' }));
    await Promise.all([first, second]);
    expect(owner.state.value).toMatchObject({ phase: 'success', data: { operatorType: '46' } });
    owner.dispose();
    client.dispose();
  });

  it('turns malformed payloads into the shared decode failure', async () => {
    const client = createReadQueryClient(apiWith(async () => ({ items: [] })));
    const owner = createOperatorCatalogQuery(client);
    await expect(owner.refresh()).resolves.toMatchObject({ phase: 'error', failure: { kind: 'decode' } });
    owner.dispose();
    client.dispose();
  });
});
