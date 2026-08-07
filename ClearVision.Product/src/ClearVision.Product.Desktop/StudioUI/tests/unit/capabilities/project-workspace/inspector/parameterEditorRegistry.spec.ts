import { describe, expect, it } from 'vitest';
import { resolveInspectorParameterEditor } from '@/capabilities/project-workspace/inspector';

function editor(overrides: Partial<Parameters<typeof resolveInspectorParameterEditor>[0]> = {}) {
  return resolveInspectorParameterEditor({
    dataType: 'string',
    options: null,
    minValue: null,
    maxValue: null,
    value: '',
    raw: {},
    ...overrides
  });
}

describe('G3 parameter editor registry', () => {
  it.each([
    ['string', 'text'],
    ['int', 'number'],
    ['double', 'number'],
    ['bool', 'boolean']
  ])('maps %s to %s', (dataType, kind) => {
    expect(editor({ dataType }).kind).toBe(kind);
  });

  it('uses enum options and explicit slider metadata without inferring slider from range alone', () => {
    expect(editor({ options: [{ label: '自动', value: 'Auto' }] }).kind).toBe('enum');
    expect(editor({ dataType: 'double', minValue: 0, maxValue: 10 }).kind).toBe('number');
    expect(editor({
      dataType: 'double', minValue: 0, maxValue: 10, raw: { showSlider: true }
    }).kind).toBe('slider');
  });

  it('maps file and Legacy path-like parameters to the shared picker contract', () => {
    expect(editor({ dataType: 'file' })).toMatchObject({
      kind: 'file',
      extensionSlot: 'file-picker',
      filePickerFilter: 'All Files|*.*',
      message: null
    });
    expect(editor({ dataType: 'string', parameterName: 'TemplatePath' })).toMatchObject({
      kind: 'file',
      filePickerFilter: 'Template Files|*.png;*.jpg;*.jpeg;*.bmp;*.json|All Files|*.*'
    });
    expect(editor({ dataType: 'string', parameterName: 'IPAddress' }).kind).toBe('text');
    expect(editor({ dataType: 'string', parameterName: 'Description' }).kind).toBe('text');
  });

  it('maps color metadata to a swatch/text editor', () => {
    expect(editor({ dataType: 'color' })).toMatchObject({
      kind: 'color',
      extensionSlot: null,
      filePickerFilter: null
    });
    expect(editor({ dataType: 'cameraBinding' })).toMatchObject({
      kind: 'extension',
      extensionSlot: 'camera-binding',
      message: '请在相机绑定编辑器中选择工程使用的相机。'
    });
    expect(editor({ dataType: 'Rectangle' })).toMatchObject({ kind: 'extension', extensionSlot: 'image-backed' });
  });

  it('preserves explicit nullable metadata', () => {
    expect(editor({ dataType: 'int', value: 0, raw: { nullable: true } }).nullable).toBe(true);
  });
});
