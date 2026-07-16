import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import OperatorRail from '@/capabilities/project-workspace/flow/OperatorRail.vue';
import { decodeOperatorCatalog, type OperatorCatalogItem } from '@/capabilities/operators-read/operatorContracts';

function metadata(overrides: Record<string, unknown>): Record<string, unknown> {
  return {
    type: 1,
    displayName: '高斯滤波',
    description: '平滑图像',
    categoryId: 'ImagePreprocessing',
    category: '图像预处理',
    lifecycle: 'Stable',
    lifecycleNote: null,
    defaultHidden: false,
    iconName: 'filter',
    keywords: ['滤波'],
    tags: ['image'],
    version: '1.0.0',
    inputPorts: [{ name: 'Image', displayName: '图像', dataType: 'Image', isRequired: true, description: null }],
    outputPorts: [{ name: 'Image', displayName: '图像', dataType: 'Image', isRequired: false, description: null }],
    parameters: [],
    ...overrides
  };
}

const operators = decodeOperatorCatalog([
  metadata({ type: 20, displayName: '全局阈值处理', keywords: ['二值化', 'threshold'] }),
  metadata({ type: 37, displayName: 'Blob分类标注', categoryId: 'FeatureExtraction', category: '特征提取' }),
  metadata({ type: 17, displayName: '形态学（兼容）', lifecycle: 'Legacy', defaultHidden: true })
]);

function mountRail(readonly = false) {
  return mount(OperatorRail, {
    props: {
      readonly,
      catalog: {
        phase: 'success',
        operators,
        isRefreshing: false,
        message: null
      }
    }
  });
}

describe('F03 G2 Operator Rail', () => {
  it('filters by stable keywords and category, then click-adds the selected metadata', async () => {
    const wrapper = mountRail();
    await wrapper.get('[data-testid="operator-search"]').setValue('二值化');
    expect(wrapper.findAll('.operator-item')).toHaveLength(1);
    expect(wrapper.text()).toContain('全局阈值处理');

    await wrapper.get('.operator-item').trigger('click');
    expect(wrapper.emitted('add')?.[0]?.[0]).toMatchObject({ displayName: '全局阈值处理' });

    await wrapper.get('[data-category="FeatureExtraction"]').trigger('click');
    expect(wrapper.findAll('.operator-item')).toHaveLength(0);
    await wrapper.get('[data-testid="operator-search"]').setValue('');
    expect(wrapper.findAll('.operator-item')).toHaveLength(1);
    expect(wrapper.text()).toContain('Blob分类标注');
  });

  it('hides compatibility operators by default and exposes them explicitly', async () => {
    const wrapper = mountRail();
    expect(wrapper.text()).not.toContain('形态学（兼容）');
    await wrapper.get('.operator-rail__compatibility input').setValue(true);
    expect(wrapper.text()).toContain('形态学（兼容）');
  });

  it('writes a complete drag payload without a global OperatorLibrary owner', async () => {
    const wrapper = mountRail();
    const payloads = new Map<string, string>();
    const dataTransfer = {
      effectAllowed: 'none',
      setData(type: string, value: string) { payloads.set(type, value); }
    };
    const item = wrapper.findAll('.operator-item')[0]!;

    await item.trigger('dragstart', { dataTransfer });

    const payload = JSON.parse(payloads.get('application/json') ?? '{}') as OperatorCatalogItem;
    expect(payload).toMatchObject({ displayName: '全局阈值处理' });
    expect(payload.inputPorts).toHaveLength(1);
    expect(payload.outputPorts).toHaveLength(1);
  });

  it('disables click and drag mutations in readonly mode', () => {
    const wrapper = mountRail(true);
    expect(wrapper.get('.operator-item').attributes('disabled')).toBeDefined();
    expect(wrapper.get('.operator-item').attributes('draggable')).toBe('false');
  });
});
