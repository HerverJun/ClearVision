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
    await wrapper.findAll('button').find(button => button.text() === 'Show toast')?.trigger('click');
    expect(document.querySelector('[data-toast-id]')).not.toBeNull();

    wrapper.unmount();
    expect(document.documentElement.dataset.theme).toBe('prior-theme');
    expect(document.documentElement.dataset.density).toBe('prior-density');
    expect(document.documentElement.dataset.reducedMotion).toBe('prior-motion');
  });

  it('renders all eighteen representative primitive families', async () => {
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
      'modal',
      'toast',
      'splitter',
      'tooltip'
    ]));
    expect(wrapper.text()).toContain('Cinnabar brand intent');
    expect(wrapper.text()).not.toContain('Brand blue');
    wrapper.unmount();
  });
});
