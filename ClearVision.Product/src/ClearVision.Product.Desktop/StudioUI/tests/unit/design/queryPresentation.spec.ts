import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import {
  CvDescriptionList,
  CvInlineAlert,
  CvPagination,
  CvSearchField
} from '@/design-system/primitives';

describe('read-only query presentation primitives', () => {
  it('provides a labelled search field with keyboard clearing and focus retention', async () => {
    const wrapper = mount(CvSearchField, {
      attachTo: document.body,
      props: { modelValue: '相机', label: '搜索工程', inputTestId: 'project-search' }
    });
    const input = wrapper.get('input');
    expect(input.attributes('type')).toBe('search');
    expect(input.attributes('name')).toBe(input.attributes('id'));
    expect(input.attributes('autocomplete')).toBe('off');
    expect(input.attributes('data-testid')).toBe('project-search');
    expect(wrapper.get('label').text()).toContain('搜索工程');

    input.element.focus();
    await input.trigger('keydown', { key: 'Escape' });
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['']);
    expect(wrapper.emitted('clear')).toHaveLength(1);
    expect(document.activeElement).toBe(input.element);

    await wrapper.setProps({ modelValue: '工位' });
    await input.trigger('keydown', { key: 'Enter' });
    expect(wrapper.emitted('search')?.at(-1)).toEqual(['工位']);
    wrapper.unmount();
  });

  it('announces the current page and bounds navigation', async () => {
    const wrapper = mount(CvPagination, {
      props: { page: 2, pageSize: 20, totalItems: 95 }
    });

    expect(wrapper.get('nav').attributes('aria-label')).toBe('分页导航');
    expect(wrapper.get('[aria-current="page"]').text()).toBe('2');
    expect(wrapper.text()).toContain('第 21–40 项，共 95 项');

    await wrapper.get('button[aria-label="上一页"]').trigger('click');
    expect(wrapper.emitted('change')?.at(-1)).toEqual([1]);

    await wrapper.setProps({ page: 1 });
    expect(wrapper.get('button[aria-label="上一页"]').attributes()).toHaveProperty('disabled');
  });

  it('renders compact description semantics and missing values honestly', () => {
    const wrapper = mount(CvDescriptionList, {
      props: {
        label: '工程摘要',
        items: [
          { key: 'name', label: '名称', value: '检测工程' },
          { key: 'description', label: '描述', value: null }
        ]
      }
    });

    expect(wrapper.get('dl').attributes('aria-label')).toBe('工程摘要');
    expect(wrapper.findAll('dt').map(node => node.text())).toEqual(['名称', '描述']);
    expect(wrapper.findAll('dd').map(node => node.text())).toEqual(['检测工程', '—']);
  });

  it('keeps partial and stale information visible as readable inline alerts', async () => {
    const warning = mount(CvInlineAlert, {
      props: { tone: 'warning', title: '数据可能已过期', dismissible: true },
      slots: { default: '当前显示上一次成功读取的数据。' }
    });
    expect(warning.get('[role="status"]').text()).toContain('当前显示上一次成功读取的数据。');
    await warning.get('button[aria-label="关闭提示"]').trigger('click');
    expect(warning.emitted('dismiss')).toHaveLength(1);

    const error = mount(CvInlineAlert, {
      props: { tone: 'error' },
      slots: { default: '部分区域加载失败。' }
    });
    expect(error.get('[role="alert"]').attributes('aria-live')).toBe('assertive');
  });
});
