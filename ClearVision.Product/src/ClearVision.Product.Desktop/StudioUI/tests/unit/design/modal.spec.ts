import { mount } from '@vue/test-utils';
import { h, nextTick } from 'vue';
import { afterEach, describe, expect, it } from 'vitest';
import { CvModal } from '@/design-system/primitives';

afterEach(() => {
  document.body.innerHTML = '';
});

describe('CvModal', () => {
  it('traps focus, closes on Escape and restores the trigger', async () => {
    const trigger = document.createElement('button');
    trigger.textContent = 'Open modal';
    document.body.append(trigger);
    trigger.focus();

    const wrapper = mount(CvModal, {
      attachTo: document.body,
      props: { open: true, title: 'Review design' },
      slots: {
        default: () => h('button', { id: 'first', 'data-modal-initial-focus': '' }, 'First'),
        footer: () => h('button', { id: 'last' }, 'Last')
      }
    });

    await nextTick();
    const first = document.querySelector<HTMLElement>('#first');
    const last = document.querySelector<HTMLElement>('#last');
    const close = document.querySelector<HTMLElement>('[aria-label="Close dialog"]');
    expect(document.activeElement).toBe(first);

    last?.focus();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    expect(document.activeElement).toBe(close);

    close?.focus();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));
    expect(document.activeElement).toBe(last);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(wrapper.emitted('close')).toHaveLength(1);

    await wrapper.setProps({ open: false });
    await nextTick();
    expect(document.activeElement).toBe(trigger);
    wrapper.unmount();
  });

  it('removes its document listener when unmounted while open', async () => {
    const wrapper = mount(CvModal, {
      attachTo: document.body,
      props: { open: true, title: 'Temporary modal' }
    });
    await nextTick();
    wrapper.unmount();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(wrapper.emitted('close')).toBeUndefined();
  });
});
