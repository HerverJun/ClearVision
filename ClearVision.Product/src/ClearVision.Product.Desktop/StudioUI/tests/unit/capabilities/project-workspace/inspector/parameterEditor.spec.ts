import { mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import ParameterEditor from '@/capabilities/project-workspace/inspector/ParameterEditor.vue';
import type { InspectorParameterProjection } from '@/capabilities/project-workspace/inspector';
import type { FilePickerPort, FilePickerResult } from '@/platform/host';

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
    filePickerFilter: null,
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

  it('preserves a local draft when the owner republishes an equivalent parameter projection', async () => {
    const current = parameter({
      name: 'Count', label: '缺陷数量上限', dataType: 'int', editorKind: 'number', integer: true,
      value: 0, minValue: 0, maxValue: 10
    });
    const wrapper = mount(ParameterEditor, { props: { parameter: current, disabled: false } });
    const input = wrapper.get('input[type="number"]');

    await input.setValue('11');
    await wrapper.setProps({ parameter: parameter({ ...current }) });
    expect((wrapper.get('input[type="number"]').element as HTMLInputElement).value).toBe('11');

    await wrapper.get('input[type="number"]').trigger('blur');
    expect(wrapper.emitted('commit')).toEqual([[11]]);
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

  it('uses the shared picker for file parameters and does not commit cancellation', async () => {
    const picker: FilePickerPort = {
      pick: vi.fn()
        .mockResolvedValueOnce({ status: 'selected', parameterName: 'ModelPath', filePath: 'C:\\models\\demo.onnx' })
        .mockResolvedValueOnce({ status: 'cancelled', parameterName: 'ModelPath' }),
      getDiagnostics: () => ({
        disposed: false,
        activeRequest: false,
        queuedRequestCount: 0,
        activeSubscriptionCount: 1,
        lateResponseCount: 0,
        ignoredResponseCount: 0
      }),
      dispose: vi.fn()
    };
    const wrapper = mount(ParameterEditor, {
      props: {
        parameter: parameter({
          name: 'ModelPath',
          dataType: 'file',
          editorKind: 'file',
          extensionSlot: 'file-picker',
          filePickerFilter: 'Model Files|*.onnx|All Files|*.*'
        }),
        disabled: false,
        filePicker: picker
      }
    });

    await wrapper.get('.file-parameter-editor__choose').trigger('click');
    await vi.waitFor(() => expect(wrapper.emitted('commit')).toEqual([['C:\\models\\demo.onnx']]));
    expect(picker.pick).toHaveBeenCalledWith({
      parameterName: 'ModelPath',
      filter: 'Model Files|*.onnx|All Files|*.*'
    });

    await wrapper.get('.file-parameter-editor__choose').trigger('click');
    await vi.waitFor(() => expect(picker.pick).toHaveBeenCalledTimes(2));
    expect(wrapper.emitted('commit')).toEqual([['C:\\models\\demo.onnx']]);
  });

  it('ignores a file result after the selected node changes and blocks picker in readonly mode', async () => {
    let resolvePick!: (result: FilePickerResult) => void;
    const picker: FilePickerPort = {
      pick: vi.fn(() => new Promise<FilePickerResult>(resolve => { resolvePick = resolve; })),
      getDiagnostics: () => ({
        disposed: false,
        activeRequest: true,
        queuedRequestCount: 0,
        activeSubscriptionCount: 1,
        lateResponseCount: 0,
        ignoredResponseCount: 0
      }),
      dispose: vi.fn()
    };
    const wrapper = mount(ParameterEditor, {
      props: {
        parameter: parameter({ id: 'file-a', name: 'InputPath', dataType: 'file', editorKind: 'file', extensionSlot: 'file-picker' }),
        disabled: false,
        filePicker: picker
      }
    });

    await wrapper.get('.file-parameter-editor__choose').trigger('click');
    await wrapper.setProps({
      parameter: parameter({ id: 'file-b', name: 'OtherPath', dataType: 'file', editorKind: 'file', extensionSlot: 'file-picker' })
    });
    resolvePick({ status: 'selected', parameterName: 'InputPath', filePath: 'C:\\old\\path.png' });
    await vi.waitFor(() => expect(wrapper.emitted('commit')).toBeUndefined());

    const readonly = mount(ParameterEditor, {
      props: {
        parameter: parameter({ dataType: 'file', editorKind: 'file', extensionSlot: 'file-picker' }),
        disabled: true,
        filePicker: picker
      }
    });
    expect(readonly.get('.file-parameter-editor__choose').attributes('disabled')).toBeDefined();
    expect(picker.pick).toHaveBeenCalledTimes(1);
  });

  it('commits a color swatch value while preserving the text representation', async () => {
    const wrapper = mount(ParameterEditor, {
      props: {
        parameter: parameter({ dataType: 'color', editorKind: 'color', value: '#112233' }),
        disabled: false
      }
    });
    await wrapper.get('input[type="color"]').setValue('#445566');
    expect(wrapper.emitted('commit')).toEqual([['#445566']]);
    expect((wrapper.get('input[type="text"]').element as HTMLInputElement).value).toBe('#445566');
  });

  it('uses Chinese parameter states and associates long help and validation text with the control', () => {
    const wrapper = mount(ParameterEditor, {
      props: {
        parameter: parameter({
          name: 'LongField',
          label: '相机触发等待帧数（未配置时使用设备默认值）',
          description: '超过等待帧数后停止当前预览，并提示操作员检查相机触发与网络连接。',
          nullable: true,
          value: null,
          valueSource: 'metadata-default',
          deprecated: true,
          errors: Object.freeze([{
            code: 'range',
            parameterNames: Object.freeze(['LongField']),
            message: '不能大于 10，请输入 0 到 10。',
            reasonCode: 'OUT_OF_RANGE'
          }])
        }),
        disabled: false
      }
    });

    expect(wrapper.text()).toContain('默认值');
    expect(wrapper.text()).toContain('已弃用');
    expect(wrapper.text()).toContain('使用默认值（空值）');
    expect(wrapper.text()).not.toContain('deprecated');
    expect(wrapper.text()).not.toContain('Use default value');
    const input = wrapper.get('input[type="text"]');
    expect(input.attributes('name')).toBe('LongField');
    expect(input.attributes('autocomplete')).toBe('off');
    expect(input.attributes('aria-describedby')).toContain('-description');
    expect(input.attributes('aria-describedby')).toContain('-errors');
  });
});
