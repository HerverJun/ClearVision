import { mount } from '@vue/test-utils';
import { h, nextTick } from 'vue';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CvMenu, CvMenuItem, CvTooltip } from '@/design-system/primitives';

afterEach(() => {
  document.body.innerHTML = '';
});

describe('Design System overlay primitives', () => {
  it('shows a described tooltip for hover or keyboard focus and disposes it', async () => {
    const wrapper = mount(CvTooltip, {
      attachTo: document.body,
      props: { text: '打开运行详情', placement: 'bottom' },
      slots: {
        default: ({ tooltipId }: { tooltipId: string }) => h('button', {
          type: 'button',
          'aria-describedby': tooltipId
        }, '详情')
      }
    });

    const button = wrapper.get('button');
    await button.trigger('focusin');
    await nextTick();
    const tooltip = document.querySelector<HTMLElement>('[role="tooltip"]');
    expect(tooltip?.textContent).toBe('打开运行详情');
    expect(button.attributes('aria-describedby')).toBe(tooltip?.id);

    await button.trigger('keydown', { key: 'Escape' });
    await nextTick();
    expect(document.querySelector('[role="tooltip"]')).toBeNull();
    wrapper.unmount();
  });

  it('skips disabled items, closes on Escape and returns focus to the trigger', async () => {
    const wrapper = mount(CvMenu, {
      attachTo: document.body,
      props: {
        modelValue: false,
        label: '工程操作',
        triggerLabel: '打开工程操作'
      },
      slots: {
        default: () => [
          h(CvMenuItem, { value: 'open' }, () => '打开'),
          h(CvMenuItem, { value: 'disabled', disabled: true }, () => '不可用'),
          h(CvMenuItem, { value: 'delete', tone: 'destructive' }, () => '删除')
        ]
      }
    });

    const trigger = wrapper.get('[aria-haspopup="menu"]');
    await trigger.trigger('click');
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([true]);
    await wrapper.setProps({ modelValue: true });
    await nextTick();

    const items = [...document.querySelectorAll<HTMLElement>('[role^="menuitem"]')];
    expect(document.activeElement).toBe(items[0]);
    items[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    await nextTick();
    expect(document.activeElement).toBe(items[2]);

    items[2]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await nextTick();
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([false]);
    await wrapper.setProps({ modelValue: false });
    await nextTick();
    expect(document.activeElement).toBe(trigger.element);
    wrapper.unmount();
  });

  it('emits the selected menu value before requesting close', async () => {
    const wrapper = mount(CvMenu, {
      props: { modelValue: true, label: '视图选项', triggerLabel: '打开视图选项' },
      slots: {
        default: () => h(CvMenuItem, { value: 'compact' }, () => '紧凑')
      },
      global: { stubs: { Teleport: true } }
    });
    await nextTick();
    await wrapper.get('[role="menuitem"]').trigger('click');
    expect(wrapper.emitted('select')).toEqual([['compact']]);
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([false]);
    wrapper.unmount();
  });

  it('removes every document and window listener when unmounted while open', async () => {
    const documentAdd = vi.spyOn(document, 'addEventListener');
    const documentRemove = vi.spyOn(document, 'removeEventListener');
    const windowAdd = vi.spyOn(window, 'addEventListener');
    const windowRemove = vi.spyOn(window, 'removeEventListener');
    const wrapper = mount(CvMenu, {
      attachTo: document.body,
      props: { modelValue: true, label: '生命周期菜单', triggerLabel: '打开生命周期菜单' },
      slots: { default: () => h(CvMenuItem, { value: 'inspect' }, () => '检查') }
    });
    await nextTick();

    const pointerCall = documentAdd.mock.calls.find(([type]) => type === 'pointerdown');
    const resizeCall = windowAdd.mock.calls.find(([type]) => type === 'resize');
    const scrollCall = windowAdd.mock.calls.find(([type]) => type === 'scroll');
    expect(pointerCall).toBeDefined();
    expect(resizeCall).toBeDefined();
    expect(scrollCall).toBeDefined();

    wrapper.unmount();
    expect(documentRemove).toHaveBeenCalledWith(...pointerCall!);
    expect(windowRemove).toHaveBeenCalledWith(...resizeCall!);
    expect(windowRemove).toHaveBeenCalledWith(...scrollCall!);
  });
});
