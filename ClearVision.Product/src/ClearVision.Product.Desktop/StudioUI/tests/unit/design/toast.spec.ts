import { mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CvToastRegion } from '@/design-system/primitives';

afterEach(() => {
  vi.clearAllTimers();
  vi.useRealTimers();
});

describe('CvToastRegion', () => {
  it('dismisses timed notifications and clears timers on unmount', async () => {
    vi.useFakeTimers();
    const onDismiss = vi.fn();
    const wrapper = mount(CvToastRegion, {
      props: {
        toasts: [{ id: 'one', title: 'Saved', tone: 'ok', durationMs: 1000 }],
        onDismiss
      }
    });

    vi.advanceTimersByTime(1000);
    expect(wrapper.emitted('dismiss')).toEqual([['one']]);
    expect(onDismiss).toHaveBeenCalledTimes(1);

    await wrapper.setProps({
      toasts: [{ id: 'two', title: 'Pending', tone: 'info', durationMs: 1000 }]
    });
    wrapper.unmount();
    vi.advanceTimersByTime(1500);
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it('keeps zero-duration notifications until explicitly dismissed', async () => {
    vi.useFakeTimers();
    const wrapper = mount(CvToastRegion, {
      props: {
        toasts: [{ id: 'persistent', title: 'Review required', durationMs: 0 }]
      }
    });

    vi.advanceTimersByTime(20_000);
    expect(wrapper.emitted('dismiss')).toBeUndefined();
    await wrapper.get('button').trigger('click');
    expect(wrapper.emitted('dismiss')).toEqual([['persistent']]);
  });
});
