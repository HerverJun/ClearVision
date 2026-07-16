import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import ParameterEditor from '@/capabilities/project-workspace/inspector/ParameterEditor.vue';
import type { InspectorParameterProjection } from '@/capabilities/project-workspace/inspector';

function parameter(overrides: Partial<InspectorParameterProjection> = {}): InspectorParameterProjection {
  return Object.freeze({
    id: 'parameter-1',
    name: 'Value',
    label: '值',
    description: null,
    dataType: 'string',
    isRequired: false,
    nullable: false,
    integer: false,
    options: null,
    minValue: null,
    maxValue: null,
    explicitValuePresent: true,
    value: '',
    defaultValue: '',
    valueSource: 'explicit',
    editorKind: 'text',
    extensionSlot: null,
    extensionMessage: null,
    persisted: true,
    visible: true,
    disabledByConstraint: false,
    ignored: false,
    deprecated: false,
    reasonCode: null,
    definition: null,
    errors: Object.freeze([]),
    ...overrides
  });
}

describe('G3 ParameterEditor', () => {
  it('commits string "0" and empty string without truthy/falsy coercion', async () => {
    const wrapper = mount(ParameterEditor, { props: { parameter: parameter(), disabled: false } });
    const input = wrapper.get('input[type="text"]');
    await input.setValue('0');
    await input.trigger('blur');
    await input.setValue('');
    await input.trigger('blur');
    expect(wrapper.emitted('commit')).toEqual([['0'], ['']]);
  });

  it('commits numeric 0, boolean false, enum values and explicit null', async () => {
    const number = mount(ParameterEditor, {
      props: { parameter: parameter({ dataType: 'int', editorKind: 'number', integer: true, value: 1 }), disabled: false }
    });
    await number.get('input[type="number"]').setValue('0');
    await number.get('input[type="number"]').trigger('blur');
    expect(number.emitted('commit')).toEqual([[0]]);

    const boolean = mount(ParameterEditor, {
      props: { parameter: parameter({ dataType: 'bool', editorKind: 'boolean', value: true }), disabled: false }
    });
    await boolean.get('input[type="checkbox"]').setValue(false);
    expect(boolean.emitted('commit')).toEqual([[false]]);

    const enumeration = mount(ParameterEditor, {
      props: {
        parameter: parameter({
          dataType: 'enum', editorKind: 'enum', value: 'Auto',
          options: [{ label: '自动', value: 'Auto' }, { label: '手动', value: 'Manual' }]
        }),
        disabled: false
      }
    });
    await enumeration.get('select').setValue('Manual');
    expect(enumeration.emitted('commit')).toEqual([['Manual']]);

    const nullable = mount(ParameterEditor, {
      props: { parameter: parameter({ dataType: 'int', editorKind: 'number', integer: true, nullable: true, value: 2 }), disabled: false }
    });
    await nullable.get('.parameter-editor__nullable input').setValue(true);
    expect(nullable.emitted('commit')).toEqual([[null]]);
  });

  it('commits slider on change and resets short-lived drafts when authority changes', async () => {
    const wrapper = mount(ParameterEditor, {
      props: {
        parameter: parameter({
          dataType: 'double', editorKind: 'slider', value: 1, minValue: 0, maxValue: 5
        }),
        disabled: false
      }
    });
    const slider = wrapper.get('input[type="range"]');
    await slider.setValue('4');
    expect(wrapper.emitted('commit')).toEqual([[4]]);
    expect(wrapper.attributes('data-dirty')).toBe('true');

    await wrapper.setProps({
      parameter: parameter({
        id: 'parameter-2', name: 'Other', dataType: 'double', editorKind: 'slider',
        value: 3, minValue: 0, maxValue: 5
      })
    });
    expect((wrapper.get('input[type="range"]').element as HTMLInputElement).value).toBe('3');
    expect(wrapper.attributes('data-dirty')).toBe('false');
  });

  it('renders deferred extensions read-only', () => {
    const wrapper = mount(ParameterEditor, {
      props: {
        parameter: parameter({ editorKind: 'extension', extensionMessage: 'G4 extension slot' }),
        disabled: false
      }
    });
    expect(wrapper.get('.parameter-editor__extension').text()).toContain('G4 extension slot');
    expect(wrapper.find('input[type="text"]').exists()).toBe(false);
  });
});
