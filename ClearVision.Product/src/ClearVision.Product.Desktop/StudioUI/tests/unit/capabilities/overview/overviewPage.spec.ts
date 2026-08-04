import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import { OverviewPage, type OverviewRuntime } from '@/capabilities/overview';
import type { ApiGetOptions, ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

const projectId = '11111111-1111-4111-8111-111111111111';

function recentProject(): Record<string, unknown> {
  return {
    id: projectId,
    name: '最近工程',
    description: '来自 projects-read public query',
    version: '1.0.0',
    persistenceRevision: 2,
    createdAt: '2026-07-15T01:00:00Z',
    modifiedAt: '2026-07-15T02:00:00Z',
    lastOpenedAt: '2026-07-15T03:00:00Z'
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

function createRuntime(api: ApiTransport): OverviewRuntime {
  return {
    queries: createReadQueryClient(api),
    session: {
      projection: {
        phase: 'authenticated',
        user: { userId: 'user-1', username: 'operator-a', role: 'Operator' },
        sessionGeneration: 1,
        message: '会话有效',
        updatedAt: Date.now()
      },
      refresh: vi.fn(async () => {})
    },
    systemStatus: {
      projection: {
        phase: 'online',
        health: { status: 'Healthy', port: 5000, healthy: true },
        message: '本地服务在线',
        updatedAt: Date.now()
      },
      refresh: vi.fn(async () => {})
    }
  };
}

describe('Overview page', () => {
  it('consumes shared session/system projections and only requests recent projects itself', async () => {
    const requestedPaths: string[] = [];
    const runtime = createRuntime(apiWith(async path => {
      requestedPaths.push(path);
      return [recentProject()];
    }));
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/overview', component: { template: '<div />' } },
        { path: '/projects', component: { template: '<div />' } },
        { path: '/projects/:projectId', component: { template: '<div />' } },
        { path: '/diagnostics', component: { template: '<div />' } },
        { path: '/about', component: { template: '<div />' } }
      ]
    });
    await router.push('/overview');
    await router.isReady();

    const wrapper = mount(OverviewPage, {
      props: { runtime },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('健康');
    expect(wrapper.text()).toContain('operator-a');
    expect(wrapper.text()).toContain('操作员');
    expect(wrapper.text()).toContain('最近工程');
    expect(wrapper.findAll('a').some(link => link.text().includes('诊断'))).toBe(false);
    expect(requestedPaths).toEqual(['projects/recent?count=5']);
    expect(requestedPaths).not.toContain('/health');
    expect(requestedPaths).not.toContain('auth/me');

    wrapper.unmount();
    runtime.queries.dispose();
  });

  it('shows a truthful empty state when the authority has no recent projects', async () => {
    const runtime = createRuntime(apiWith(async () => []));
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/overview', component: { template: '<div />' } },
        { path: '/projects', component: { template: '<div />' } },
        { path: '/diagnostics', component: { template: '<div />' } },
        { path: '/about', component: { template: '<div />' } }
      ]
    });
    await router.push('/overview');
    await router.isReady();

    const wrapper = mount(OverviewPage, {
      props: { runtime },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('暂无最近工程');
    expect(wrapper.text()).toContain('最近打开记录');

    wrapper.unmount();
    runtime.queries.dispose();
  });
});
