import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import { describe, expect, it } from 'vitest';
import { ProjectDetailPage, ProjectsPage } from '@/capabilities/projects-read';
import { ApiNotFoundError, type ApiGetOptions, type ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

const projectId = '11111111-1111-4111-8111-111111111111';
const flowId = '22222222-2222-4222-8222-222222222222';

function summary(overrides: Record<string, unknown> = {}): Record<string, unknown> {
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
  };
}

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/projects', component: { template: '<div />' } },
      { path: '/projects/:projectId', component: { template: '<div />' } }
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

describe('Projects read pages', () => {
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
      props: { projectId, runtime: { queries } },
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

    wrapper.unmount();
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
});
