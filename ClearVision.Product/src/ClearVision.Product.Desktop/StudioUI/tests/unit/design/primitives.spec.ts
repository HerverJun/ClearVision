import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import {
  CvButton,
  CvField,
  CvIconButton,
  CvPanel,
  CvSelect,
  CvStatusBadge,
  CvSurface,
  CvTypography
} from '@/design-system/primitives';

describe('Design Foundation primitives', () => {
  it('renders semantic typography, surfaces and panels', () => {
    const typography = mount(CvTypography, {
      props: { as: 'h2', variant: 'title', weight: 'semibold' },
      slots: { default: 'Precision controls' }
    });
    expect(typography.element.tagName).toBe('H2');
    expect(typography.attributes('data-design-primitive')).toBe('typography');

    const surface = mount(CvSurface, {
      props: { level: 2, elevation: 1 },
      slots: { default: 'Surface content' }
    });
    expect(surface.attributes('data-surface-level')).toBe('2');

    const panel = mount(CvPanel, {
      props: { title: 'Inspection state', description: 'Read-only projection' },
      slots: { default: 'Panel content' }
    });
    const heading = panel.get('h2');
    expect(panel.attributes('aria-labelledby')).toBe(heading.attributes('id'));
    expect(panel.text()).toContain('Read-only projection');
  });

  it('exposes button state and requires an accessible icon label', () => {
    const button = mount(CvButton, {
      props: { loading: true, loadingLabel: 'Applying design token' },
      slots: { default: 'Apply' }
    });
    expect(button.get('button').attributes('aria-busy')).toBe('true');
    expect(button.get('button').attributes()).toHaveProperty('disabled');
    expect(button.text()).toContain('Applying design token');

    const iconButton = mount(CvIconButton, {
      props: { label: 'Open details' },
      slots: { default: '<svg />' }
    });
    expect(iconButton.get('button').attributes('aria-label')).toBe('Open details');
    expect(iconButton.get('button').attributes('title')).toBe('Open details');
  });

  it('links field errors and emits edited values', async () => {
    const wrapper = mount(CvField, {
      props: {
        label: 'Camera address',
        modelValue: '192.168.0.999',
        hint: 'IPv4 only',
        error: 'Enter a valid address.'
      }
    });

    const input = wrapper.get('input');
    const describedBy = input.attributes('aria-describedby')?.split(' ') ?? [];
    expect(input.attributes('aria-invalid')).toBe('true');
    expect(describedBy).toHaveLength(2);
    expect(describedBy.every(id => document.getElementById(id) !== null || wrapper.find(`#${id}`).exists())).toBe(true);

    await input.setValue('192.168.0.10');
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['192.168.0.10']);
  });

  it('renders disabled select options and emits selection changes', async () => {
    const wrapper = mount(CvSelect, {
      props: {
        label: 'Operator family',
        modelValue: 'camera',
        options: [
          { value: 'camera', label: 'Camera' },
          { value: 'decision', label: 'Decision', disabled: true }
        ]
      }
    });

    expect(wrapper.findAll('option')).toHaveLength(2);
    expect(wrapper.findAll('option')[1]?.attributes()).toHaveProperty('disabled');
    await wrapper.get('select').setValue('decision');
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['decision']);
  });

  it.each(['ok', 'ng', 'warning', 'info', 'idle'] as const)(
    'keeps the %s status tone explicit',
    tone => {
      const wrapper = mount(CvStatusBadge, { props: { tone, label: tone.toUpperCase() } });
      expect(wrapper.attributes('data-status-tone')).toBe(tone);
      expect(wrapper.text()).toContain(tone.toUpperCase());
    }
  );
});
