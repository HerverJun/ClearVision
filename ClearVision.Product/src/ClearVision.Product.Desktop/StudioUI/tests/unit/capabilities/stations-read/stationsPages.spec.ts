import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import {
  getStationAdminCommandOwnerActiveCount,
  StationDetailPage,
  StationsPage
} from '@/capabilities/stations-read';
import {
  ApiServerError,
  type ApiGetOptions,
  type ApiTransport
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import {
  stationAudit,
  stationCommand,
  stationHealth,
  stationLog,
  stationPackage,
  stationResult,
  stationStatistics,
  stationStatus,
  stationSummary
} from './stationFixtures';
import type { SessionProjectionOwner } from '@/app/session';

type GetImplementation = (path: string, options?: ApiGetOptions) => Promise<unknown>;

function apiWith(implementation: GetImplementation): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await implementation(path, options) as T | undefined;
    }
  };
}

function details(status: number) {
  return {
    url: 'http://localhost:5000/api/stations',
    status,
    statusText: 'test',
    payload: undefined,
    responseBody: ''
  };
}

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/stations', component: { template: '<div />' } },
      { path: '/stations/:stationId', component: { template: '<div />' } },
      { path: '/results', component: { template: '<div />' } },
      { path: '/projects/:id/workspace', component: { template: '<div />' } }
    ]
  });
}

function session(role: 'Admin' | 'Engineer'): SessionProjectionOwner {
  return {
    projection: {
      phase: 'authenticated',
      user: { userId: `${role.toLowerCase()}-id`, username: role.toLowerCase(), role },
      sessionGeneration: 1,
      message: 'test',
      updatedAt: Date.now()
    },
    start: vi.fn(),
    refresh: vi.fn(async () => undefined),
    dispose: vi.fn()
  };
}

describe('Station read pages', () => {
  it('renders URL-backed list filters and all nine canonical outcome counters', async () => {
    const paths: string[] = [];
    const queries = createReadQueryClient(apiWith(async path => {
      paths.push(path);
      if (path === 'stations') return [stationStatus()];
      if (path === 'stations/summary') return stationSummary();
      if (path.startsWith('stations/statistics')) return stationStatistics();
      throw new Error(`Unexpected path ${path}`);
    }));
    const router = createTestRouter();
    await router.push('/stations?q=一号&online=Online&range=week&outcome=Ng&diagnosticCode=WIRE_SWAP');
    await router.isReady();

    const wrapper = mount(StationsPage, {
      props: { runtime: { queries } },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('一号检测站');
    expect(wrapper.text()).toContain('未判定');
    expect(wrapper.text()).toContain('不适用');
    expect(wrapper.text()).toContain('判定无效');
    expect(wrapper.text()).toContain('执行失败');
    expect(wrapper.text()).toContain('已取消');
    expect(wrapper.text()).toContain('执行超时');
    expect(wrapper.text()).toContain('已跳过');
    expect(paths).toContain('stations/statistics?range=week&status=Ng&diagnosticCode=WIRE_SWAP');

    wrapper.unmount();
    queries.dispose();
  });

  it('renders stale Station data after a manual refresh fails', async () => {
    let listAttempt = 0;
    const queries = createReadQueryClient(apiWith(async path => {
      if (path === 'stations') {
        listAttempt += 1;
        if (listAttempt === 1) return [stationStatus()];
        throw new ApiServerError(details(503));
      }
      if (path === 'stations/summary') return stationSummary();
      if (path.startsWith('stations/statistics')) return stationStatistics();
      throw new Error(`Unexpected path ${path}`);
    }));
    const router = createTestRouter();
    await router.push('/stations');
    await router.isReady();
    const wrapper = mount(StationsPage, {
      props: { runtime: { queries } },
      global: { plugins: [router] }
    });
    await flushPromises();

    const refresh = wrapper.findAll('button').find(button => button.text() === '刷新');
    expect(refresh).toBeDefined();
    await refresh?.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('列表刷新未完成');
    expect(wrapper.text()).toContain('一号检测站');
    wrapper.unmount();
    queries.dispose();
  });

  it('locates a Station from package, project and revision URL identity without caching payloads', async () => {
    const queries = createReadQueryClient(apiWith(async path => {
      if (path === 'stations') return [stationStatus(), stationStatus({
        stationId: 'station-b', packageId: 'pkg-b', sourceProjectId: null, sourceProjectRevision: null
      })];
      if (path === 'stations/summary') return stationSummary();
      return stationStatistics();
    }));
    const router = createTestRouter();
    await router.push('/stations?packageId=pkg-a&projectId=project-a&revision=12');
    await router.isReady();
    const wrapper = mount(StationsPage, {
      props: { runtime: { queries } },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.get('[data-testid="stations-production-filter"]').text()).toContain('pkg-a');
    expect(wrapper.text()).toContain('一号检测站');
    expect(wrapper.text()).not.toContain('station-b');
    expect(wrapper.get('.stations-page__station-name a').attributes('href')).toContain(
      'returnTo=%2Fstations%3FpackageId%3Dpkg-a%26projectId%3Dproject-a%26revision%3D12'
    );

    wrapper.unmount();
    queries.dispose();
  });

  it('does not create Admin queries, control components or command owner for Engineer', async () => {
    const get = vi.fn(async (path: string) => {
      if (path === 'stations') return [stationStatus()];
      if (path === 'stations/station-a/results?take=50') return [stationResult()];
      if (path === 'stations/station-a/health?take=50') return [stationHealth()];
      throw new Error(`Unexpected path ${path}`);
    });
    const api = apiWith(get);
    const queries = createReadQueryClient(api);
    const router = createTestRouter();
    await router.push('/stations/station-a');
    await router.isReady();

    const wrapper = mount(StationDetailPage, {
      props: { stationId: 'station-a', runtime: { queries, api, session: session('Engineer') } },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('状态概览');
    expect(wrapper.text()).toContain('一号检测站');
    expect(wrapper.text()).toContain('执行成功');
    expect(wrapper.text()).toContain('判定 NG');
    expect(wrapper.text()).toContain('进程运行时长');
    expect(wrapper.text()).toContain('生产追溯链');
    expect(wrapper.text()).toContain('当前角色仅能查看监控摘要');
    expect(wrapper.text()).not.toContain('工作站详情读取失败');
    expect(wrapper.find('[data-capability="station-admin-control"]').exists()).toBe(false);
    expect(get.mock.calls.some(([path]) => path === 'stations/station-a' || path.includes('/logs') || path.includes('/commands') || path.startsWith('stations/audit') || path === 'station-packages')).toBe(false);
    expect(getStationAdminCommandOwnerActiveCount()).toBe(0);

    await wrapper.get('[data-testid="station-result-link"]').trigger('click');
    await flushPromises();
    expect(router.currentRoute.value.path).toBe('/results');
    expect(router.currentRoute.value.query).toMatchObject({
      source: 'station', stationId: 'station-a', resultId: 'message-9', returnTo: '/stations/station-a'
    });

    wrapper.unmount();
    queries.dispose();
  });

  it('mounts the real Admin control domain and reads logs, commands, audit and packages', async () => {
    const get = vi.fn(async (path: string) => {
      if (path === 'stations') return [stationStatus()];
      if (path === 'stations/station-a/results?take=50') return [stationResult()];
      if (path === 'stations/station-a/health?take=50') return [stationHealth()];
      if (path === 'stations/station-a') return stationStatus();
      if (path === 'stations/station-a/logs?take=50') return [stationLog()];
      if (path === 'stations/station-a/commands?take=50') return [stationCommand({
        commandType: 'DeployPackage',
        payloadJson: JSON.stringify({ packageId: 'pkg-a' }),
        status: 'Succeeded',
        completedAtUtc: '2026-07-15T02:00:10Z'
      })];
      if (path === 'stations/audit?stationId=station-a&take=50') return [stationAudit()];
      if (path === 'station-packages') return [stationPackage()];
      throw new Error(`Unexpected path ${path}`);
    });
    const api = {
      ...apiWith(get),
      post: vi.fn(async () => stationCommand()),
      patch: vi.fn(async () => stationStatus())
    } as ApiTransport;
    const queries = createReadQueryClient(api);
    const router = createTestRouter();
    await router.push('/stations/station-a');
    await router.isReady();

    const wrapper = mount(StationDetailPage, {
      props: { stationId: 'station-a', runtime: { queries, api, session: session('Admin') } },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.find('[data-capability="station-admin-control"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('运行控制');
    expect(wrapper.text()).toContain('身份修订');
    expect(wrapper.text()).toContain('运行包健康状态降级');
    expect(wrapper.text()).toContain('StationCommandCreated');
    expect(wrapper.get('[data-testid="station-production-trace"]').text()).toContain('身份闭合');
    expect(wrapper.get('[data-testid="station-production-trace"]').text()).toContain('command-a · Succeeded');
    expect(wrapper.get('[data-testid="station-production-trace"]').text()).toContain('run-9 · message-9');
    expect(getStationAdminCommandOwnerActiveCount()).toBe(1);

    wrapper.unmount();
    expect(getStationAdminCommandOwnerActiveCount()).toBe(0);
    queries.dispose();
  });

  it('renders localized empty and malformed response states', async () => {
    const router = createTestRouter();
    await router.push('/stations');
    await router.isReady();

    const emptyQueries = createReadQueryClient(apiWith(async path => {
      if (path === 'stations') return [];
      if (path === 'stations/summary') return stationSummary({ totalStations: 0 });
      return stationStatistics();
    }));
    const empty = mount(StationsPage, {
      props: { runtime: { queries: emptyQueries } },
      global: { plugins: [router] }
    });
    await flushPromises();
    expect(empty.text()).toContain('暂无工作站');
    empty.unmount();
    emptyQueries.dispose();

    const malformedQueries = createReadQueryClient(apiWith(async path => {
      if (path === 'stations') return { items: [] };
      if (path === 'stations/summary') return stationSummary();
      return stationStatistics();
    }));
    const malformed = mount(StationsPage, {
      props: { runtime: { queries: malformedQueries } },
      global: { plugins: [router] }
    });
    await flushPromises();
    expect(malformed.text()).toContain('工作站列表读取失败');
    malformed.unmount();
    malformedQueries.dispose();
  });
});
