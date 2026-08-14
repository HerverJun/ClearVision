import { mount } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import DesignLab from '@/labs/design/DesignLabPlaceholder.vue';

afterEach(() => {
  document.body.innerHTML = '';
  document.documentElement.removeAttribute('data-theme');
  document.documentElement.removeAttribute('data-density');
  document.documentElement.removeAttribute('data-reduced-motion');
});

describe('Design Foundation Lab', () => {
  it('projects theme, density and motion only for its mounted lifecycle', async () => {
    document.documentElement.dataset.theme = 'prior-theme';
    document.documentElement.dataset.density = 'prior-density';
    document.documentElement.dataset.reducedMotion = 'prior-motion';

    const wrapper = mount(DesignLab, {
      attachTo: document.body,
      global: {
        stubs: {
          RouterLink: { template: '<a><slot /></a>' }
        }
      }
    });

    expect(wrapper.attributes('data-design-lab')).toBe('ready');
    expect(document.documentElement.dataset.theme).toBe('light');
    expect(document.documentElement.dataset.density).toBe('comfortable');
    expect(document.documentElement.dataset.reducedMotion).toBe('false');

    await wrapper.get('[data-design-theme="dark"]').trigger('click');
    await wrapper.get('[data-design-density="compact"]').trigger('click');
    await wrapper.get('[data-design-reduced-motion]').setValue(true);
    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(document.documentElement.dataset.density).toBe('compact');
    expect(document.documentElement.dataset.reducedMotion).toBe('true');

    await wrapper.get('[data-modal-trigger]').trigger('click');
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    await wrapper.findAll('button').find(button => button.text() === '显示通知')?.trigger('click');
    expect(document.querySelector('[data-toast-id]')).not.toBeNull();

    wrapper.unmount();
    expect(document.documentElement.dataset.theme).toBe('prior-theme');
    expect(document.documentElement.dataset.density).toBe('prior-density');
    expect(document.documentElement.dataset.reducedMotion).toBe('prior-motion');
  });

  it('keeps a single root projection owner across overlapping mounts', async () => {
    document.documentElement.dataset.theme = 'prior-theme';
    const mountOptions = {
      global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } }
    } as const;
    const first = mount(DesignLab, mountOptions);
    expect(document.documentElement.dataset.theme).toBe('light');

    const second = mount(DesignLab, mountOptions);
    await second.get('[data-design-theme="dark"]').trigger('click');
    expect(document.documentElement.dataset.theme).toBe('light');
    second.unmount();
    expect(document.documentElement.dataset.theme).toBe('light');

    first.unmount();
    expect(document.documentElement.dataset.theme).toBe('prior-theme');
  });

  it('hands the root projection to the next mounted lab when the owner unmounts first', async () => {
    document.documentElement.dataset.theme = 'prior-theme';
    document.documentElement.dataset.density = 'prior-density';
    const mountOptions = {
      global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } }
    } as const;
    const first = mount(DesignLab, mountOptions);
    const second = mount(DesignLab, mountOptions);

    await second.get('[data-design-theme="dark"]').trigger('click');
    await second.get('[data-design-density="compact"]').trigger('click');
    expect(document.documentElement.dataset.theme).toBe('light');

    first.unmount();
    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(document.documentElement.dataset.density).toBe('compact');

    second.unmount();
    expect(document.documentElement.dataset.theme).toBe('prior-theme');
    expect(document.documentElement.dataset.density).toBe('prior-density');
  });

  it('renders all representative primitive families and six task compositions', async () => {
    const wrapper = mount(DesignLab, {
      global: {
        stubs: {
          RouterLink: { template: '<a><slot /></a>' },
          Teleport: true
        }
      }
    });
    await wrapper.get('[data-modal-trigger]').trigger('click');
    await wrapper.get('[aria-label="打开样本操作菜单"]').trigger('click');
    const names = new Set(
      wrapper.findAll('[data-design-primitive]').map(node => node.attributes('data-design-primitive'))
    );
    expect(names).toEqual(new Set([
      'typography',
      'surface',
      'button',
      'data-table',
      'icon-button',
      'field',
      'inline-alert',
      'menu',
      'menu-item',
      'pagination',
      'search-field',
      'select',
      'panel',
      'status-badge',
      'toggle',
      'modal',
      'toast',
      'splitter',
      'tooltip'
    ]));
    expect(wrapper.text()).toContain('品牌丹红只表达产品意图');
    expect(wrapper.text()).not.toContain('Brand blue');
    expect(wrapper.findAll('[data-design-composition]')).toHaveLength(6);
    expect(new Set(wrapper.findAll('[data-design-composition]').map(node => node.attributes('data-design-composition')))).toEqual(new Set([
      'auth', 'dense-list', 'workspace', 'investigation', 'long-form', 'ai-stage'
    ]));
    expect(wrapper.findAll('.design-lab__composition-kicker').map(node => node.text())).toEqual([
      '身份入口', '工程扫描', '流程画布', '结果调查', '当前设置对象', '当前任务'
    ]);
    expect(wrapper.text()).not.toMatch(/(?:AUTH|DENSE LIST|WORKSPACE|INVESTIGATION|LONG FORM|AI STAGE)\s*\//);
    wrapper.unmount();
  });
});
