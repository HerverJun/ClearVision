import { mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import { CvSplitter } from '@/design-system/primitives';

function pointerEvent(
  type: string,
  values: { pointerId: number; clientX: number; clientY?: number; button?: number; isPrimary?: boolean }
): Event {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperties(event, {
    pointerId: { value: values.pointerId },
    clientX: { value: values.clientX },
    clientY: { value: values.clientY ?? 0 },
    button: { value: values.button ?? 0 },
    isPrimary: { value: values.isPrimary ?? true }
  });
  return event;
}

describe('CvSplitter', () => {
  it('owns pointer listeners only for the active drag', () => {
    const onUpdate = vi.fn();
    const wrapper = mount(CvSplitter, {
      props: {
        modelValue: 300,
        min: 220,
        max: 420,
        'onUpdate:modelValue': onUpdate
      }
    });
    const separator = wrapper.get('[role="separator"]');
    expect(separator.attributes('aria-label')).toBe('调整面板大小');

    separator.element.dispatchEvent(pointerEvent('pointerdown', { pointerId: 7, clientX: 100 }));
    window.dispatchEvent(pointerEvent('pointermove', { pointerId: 7, clientX: 140 }));
    expect(wrapper.emitted('resizeStart')).toHaveLength(1);
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([340]);
    expect(onUpdate).toHaveBeenCalledTimes(1);

    window.dispatchEvent(pointerEvent('pointerup', { pointerId: 7, clientX: 140 }));
    expect(wrapper.emitted('resizeEnd')).toHaveLength(1);
    wrapper.unmount();
    window.dispatchEvent(pointerEvent('pointermove', { pointerId: 7, clientX: 180 }));
    expect(onUpdate).toHaveBeenCalledTimes(1);
  });

  it('supports bounded keyboard resizing', async () => {
    const wrapper = mount(CvSplitter, {
      props: { modelValue: 300, min: 220, max: 420, step: 8 }
    });
    const separator = wrapper.get('[role="separator"]');

    await separator.trigger('keydown', { key: 'ArrowRight', shiftKey: true });
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([332]);
    await separator.trigger('keydown', { key: 'Home' });
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([220]);
    await separator.trigger('keydown', { key: 'End' });
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([420]);
  });
});
