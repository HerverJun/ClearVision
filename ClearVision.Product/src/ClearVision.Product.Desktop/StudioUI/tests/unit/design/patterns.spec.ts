import { mount } from '@vue/test-utils';
import { h } from 'vue';
import { describe, expect, it } from 'vitest';
import {
  CvBreadcrumbs,
  CvPageHeader,
  CvPageState,
  CvToolbar
} from '@/design-system/patterns';
import type { CvPageStateKind } from '@/design-system/patterns';

describe('Design System page patterns', () => {
  it('renders a compact semantic page heading and action area', () => {
    const wrapper = mount(CvPageHeader, {
      props: { title: '工程', description: '只读浏览工程摘要。' },
      slots: { actions: () => h('button', { type: 'button' }, '刷新') }
    });
    expect(wrapper.get('h1').text()).toBe('工程');
    expect(wrapper.text()).toContain('只读浏览工程摘要。');
    expect(wrapper.get('button').text()).toBe('刷新');
  });

  it('marks only the current breadcrumb as the current page', () => {
    const wrapper = mount(CvBreadcrumbs, {
      props: {
        items: [
          { label: '概览', href: '#/overview' },
          { label: '工程', href: '#/projects' },
          { label: '视觉检测一号线' }
        ]
      }
    });
    expect(wrapper.get('nav').attributes('aria-label')).toBe('面包屑导航');
    expect(wrapper.findAll('[aria-current="page"]')).toHaveLength(1);
    expect(wrapper.get('[aria-current="page"]').text()).toBe('视觉检测一号线');
  });

  it('supports arrow, Home and End keyboard navigation without global listeners', async () => {
    const wrapper = mount(CvToolbar, {
      attachTo: document.body,
      slots: {
        default: () => [
          h('button', { id: 'refresh', type: 'button' }, '刷新'),
          h('button', { id: 'sort', type: 'button' }, '排序'),
          h('button', { id: 'filter', type: 'button' }, '筛选')
        ]
      }
    });
    const buttons = wrapper.findAll('button');
    buttons[0]?.element.focus();
    await buttons[0]?.trigger('keydown', { key: 'ArrowRight' });
    expect(document.activeElement).toBe(buttons[1]?.element);
    await buttons[1]?.trigger('keydown', { key: 'End' });
    expect(document.activeElement).toBe(buttons[2]?.element);
    await buttons[2]?.trigger('keydown', { key: 'Home' });
    expect(document.activeElement).toBe(buttons[0]?.element);
    wrapper.unmount();
  });

  it.each([
    ['loading', '正在加载'],
    ['empty', '暂无数据'],
    ['error', '加载失败'],
    ['unauthorized', '需要登录'],
    ['forbidden', '无权访问'],
    ['not-found', '页面不存在']
  ] as const)('renders readable %s state copy', (kind, title) => {
    const wrapper = mount(CvPageState, { props: { kind: kind as CvPageStateKind } });
    expect(wrapper.get('h2').text()).toBe(title);
    expect(wrapper.text().trim().length).toBeGreaterThan(title.length);
    if (kind === 'loading') expect(wrapper.attributes('aria-busy')).toBe('true');
    if (kind === 'error') expect(wrapper.attributes('role')).toBe('alert');
  });
});
