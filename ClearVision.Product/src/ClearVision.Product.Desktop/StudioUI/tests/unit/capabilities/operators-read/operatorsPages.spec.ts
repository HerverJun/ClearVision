import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import { describe, expect, it } from 'vitest';
import { OperatorDetailPage, OperatorsPage } from '@/capabilities/operators-read';
import { ApiNotFoundError, type ApiGetOptions, type ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

function operator(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    type: 45,
    displayName: '颜色分析',
    description: '颜色检测说明',
    categoryId: 8,
    category: 'AI推理',
    lifecycle: 1,
    lifecycleNote: '现场数据验证中',
    defaultHidden: false,
    iconName: 'color',
    keywords: ['颜色', 'Color'],
    tags: ['inspection'],
    version: '1.0.0',
    inputPorts: [{ name: 'Image', displayName: '图像', dataType: 0, isRequired: true, description: null }],
    outputPorts: [{ name: 'Result', displayName: '结果', dataType: 6, isRequired: false, description: null }],
    parameters: [{
      name: 'Threshold',
      displayName: '阈值',
      description: null,
      dataType: 'double',
      defaultValue: 0.5,
      minValue: 0,
      maxValue: 1,
      isRequired: true,
      options: null
    }],
    ...overrides
  };
}

function router(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/operators', component: { template: '<div />' } },
      { path: '/operators/:operatorType', component: { template: '<div />' } }
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

describe('operator read pages', () => {
  it('applies URL-backed search, category, port, parameter and visibility filters', async () => {
    const queries = createReadQueryClient(apiWith(async () => [
      operator(),
      operator({ type: 1, displayName: '图像采集', categoryId: 0, category: '采集', inputPorts: [], parameters: [] })
    ]));
    const appRouter = router();
    await appRouter.push('/operators?q=颜色&category=AiInference&port=Image&parameter=Threshold&visibility=all&page=1');
    await appRouter.isReady();
    const wrapper = mount(OperatorsPage, {
      props: { runtime: { queries } },
      global: { plugins: [appRouter] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('颜色分析');
    expect(wrapper.text()).not.toContain('图像采集');
    expect(wrapper.text()).toContain('输入 1 · 输出 1');
    expect(wrapper.text()).toContain('1 个匹配项');
    expect(appRouter.currentRoute.value.query).toMatchObject({
      q: '颜色', category: 'AiInference', port: 'Image', parameter: 'Threshold', visibility: 'all'
    });

    wrapper.unmount();
    queries.dispose();
  });

  it('renders identity, current ports and parameters without readiness or side-effect inference', async () => {
    const queries = createReadQueryClient(apiWith(async () => operator()));
    const appRouter = router();
    await appRouter.push('/operators/45');
    await appRouter.isReady();
    const wrapper = mount(OperatorDetailPage, {
      props: { operatorType: '45', runtime: { queries } },
      global: { plugins: [appRouter] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('颜色分析');
    expect(wrapper.text()).toContain('实验性');
    expect(wrapper.text()).toContain('输入端口');
    expect(wrapper.text()).toContain('输出端口');
    expect(wrapper.text()).toContain('Threshold');
    expect(wrapper.text()).toContain('浮点数');
    expect(wrapper.find('details').attributes('open')).toBeUndefined();
    expect(wrapper.text()).not.toContain('readiness');
    expect(wrapper.text()).not.toContain('side-effect');

    wrapper.unmount();
    queries.dispose();
  });

  it('shows the frozen 404 detail state', async () => {
    const queries = createReadQueryClient(apiWith(async () => {
      throw new ApiNotFoundError({
        url: 'http://localhost:5000/api/operators/999/metadata',
        status: 404,
        statusText: 'Not Found',
        payload: undefined,
        responseBody: ''
      });
    }));
    const appRouter = router();
    await appRouter.push('/operators/999');
    await appRouter.isReady();
    const wrapper = mount(OperatorDetailPage, {
      props: { operatorType: '999', runtime: { queries } },
      global: { plugins: [appRouter] }
    });
    await flushPromises();

    expect(wrapper.text()).toContain('未找到算子');
    expect(wrapper.text()).not.toContain('OperatorType');

    wrapper.unmount();
    queries.dispose();
  });
});
