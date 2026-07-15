import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import {
  StationDetailPage,
  StationsPage
} from '@/capabilities/stations-read';
import {
  ApiForbiddenError,
  ApiServerError,
  type ApiGetOptions,
  type ApiTransport
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import {
  stationHealth,
  stationResult,
  stationStatistics,
  stationStatus,
  stationSummary
} from './stationFixtures';

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
      { path: '/stations/:stationId', component: { template: '<div />' } }
    ]
  });
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

  it('keeps an Admin 403 inside the enhancement panel while ordinary detail, results and health remain usable', async () => {
    const get = vi.fn(async (path: string) => {
      if (path === 'stations') return [stationStatus()];
      if (path === 'stations/station-a/results?take=50') return [stationResult()];
      if (path === 'stations/station-a/health?take=50') return [stationHealth()];
      if (path === 'stations/station-a') throw new ApiForbiddenError(details(403));
      throw new Error(`Unexpected path ${path}`);
    });
    const queries = createReadQueryClient(apiWith(get));
    const router = createTestRouter();
    await router.push('/stations/station-a');
    await router.isReady();

    const wrapper = mount(StationDetailPage, {
      props: { stationId: 'station-a', runtime: { queries } },
      global: { plugins: [router] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('普通详情');
    expect(wrapper.text()).toContain('一号检测站');
    expect(wrapper.text()).toContain('管理员增强信息不可用');
    expect(wrapper.text()).toContain('执行成功');
    expect(wrapper.text()).toContain('判定 NG');
    expect(wrapper.text()).toContain('进程运行时长');
    expect(wrapper.text()).not.toContain('Station 普通详情读取失败');

    wrapper.unmount();
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
    expect(empty.text()).toContain('暂无 Station');
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
    expect(malformed.text()).toContain('Station 列表读取失败');
    malformed.unmount();
    malformedQueries.dispose();
  });
});
