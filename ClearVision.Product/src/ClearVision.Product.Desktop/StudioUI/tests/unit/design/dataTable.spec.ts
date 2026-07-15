import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { CvDataTable } from '@/design-system/primitives';
import type { CvDataTableColumn } from '@/design-system/primitives';

interface ProjectRow {
  readonly id: string;
  readonly name: string;
  readonly updatedAt: string;
}

const columns: readonly CvDataTableColumn<ProjectRow>[] = [
  { key: 'name', label: '工程名称', sortable: true },
  { key: 'updatedAt', label: '更新时间', align: 'end' }
];

const rows: readonly ProjectRow[] = [
  { id: 'project-1', name: '视觉检测一号线', updatedAt: '2026-07-15' }
];

describe('CvDataTable', () => {
  it('renders native table semantics and emits explicit sorting', async () => {
    const wrapper = mount(CvDataTable, {
      props: {
        rows,
        columns,
        rowKey: 'id',
        caption: '工程列表',
        sortKey: 'name',
        sortDirection: 'ascending'
      }
    });

    expect(wrapper.get('table').element.tagName).toBe('TABLE');
    expect(wrapper.get('caption').text()).toBe('工程列表');
    expect(wrapper.get('th').attributes('scope')).toBe('col');
    expect(wrapper.get('th').attributes('aria-sort')).toBe('ascending');
    expect(wrapper.get('tbody').text()).toContain('视觉检测一号线');

    await wrapper.get('button[aria-label="按工程名称排序"]').trigger('click');
    expect(wrapper.emitted('sort')).toEqual([[{ key: 'name', direction: 'descending' }]]);
    expect(wrapper.emitted('update:sortKey')).toEqual([['name']]);
    expect(wrapper.emitted('update:sortDirection')).toEqual([['descending']]);
  });

  it('keeps loading and empty states readable without replacing table semantics', async () => {
    const wrapper = mount(CvDataTable, {
      props: { rows: [], columns, busy: true }
    });

    expect(wrapper.attributes('aria-busy')).toBe('true');
    expect(wrapper.get('[role="status"]').text()).toContain('正在加载数据');
    expect(wrapper.get('table').element.tagName).toBe('TABLE');

    await wrapper.setProps({ busy: false });
    expect(wrapper.text()).toContain('暂无数据');
    expect(wrapper.find('tbody').exists()).toBe(false);
  });
});
