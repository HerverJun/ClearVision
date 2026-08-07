import { resolveFilePickerFilter } from '@/platform/host';

export type InspectorParameterEditorKind =
  | 'text'
  | 'number'
  | 'boolean'
  | 'enum'
  | 'slider'
  | 'file'
  | 'color'
  | 'extension'
  | 'unsupported';

export interface InspectorParameterEditorRegistration {
  readonly kind: InspectorParameterEditorKind;
  readonly integer: boolean;
  readonly nullable: boolean;
  readonly extensionSlot: 'file-picker' | 'camera-binding' | 'image-backed' | null;
  readonly filePickerFilter: string | null;
  readonly message: string | null;
}

export interface InspectorParameterEditorInput {
  readonly dataType: string;
  readonly parameterName?: string;
  readonly options: readonly Readonly<{ label: string; value: string }>[] | null;
  readonly minValue: unknown;
  readonly maxValue: unknown;
  readonly value: unknown;
  readonly raw: Readonly<Record<string, unknown>>;
}

function normalized(value: unknown): string {
  return String(value ?? '').trim().toLowerCase();
}

function explicitSlider(raw: Readonly<Record<string, unknown>>): boolean {
  const metadata = new Map(Object.entries(raw).map(([key, value]) => [key.toLowerCase(), value]));
  if (metadata.get('showslider') === true) return true;
  return ['uicontrol', 'control', 'editor'].some(key => normalized(metadata.get(key)) === 'slider');
}

function explicitNullable(raw: Readonly<Record<string, unknown>>, value: unknown): boolean {
  const metadata = new Map(Object.entries(raw).map(([key, entry]) => [key.toLowerCase(), entry]));
  return value === null || metadata.get('nullable') === true || metadata.get('isnullable') === true;
}

export function resolveInspectorParameterEditor(
  input: InspectorParameterEditorInput
): InspectorParameterEditorRegistration {
  const dataType = normalized(input.dataType);
  const parameterName = input.parameterName?.trim() || String(input.raw.name ?? input.raw.Name ?? '').trim();
  const normalizedParameterName = normalized(parameterName).replace(/[^a-z0-9]/g, '');
  const nullable = explicitNullable(input.raw, input.value);
  const pathLikeString = dataType === 'string' && normalizedParameterName !== 'ipaddress' && (
    normalizedParameterName === 'filepath' ||
    normalizedParameterName === 'outputpath' ||
    normalizedParameterName === 'savepath' ||
    normalizedParameterName.endsWith('filepath') ||
    normalizedParameterName.endsWith('templatepath') ||
    normalizedParameterName.endsWith('modelpath') ||
    normalizedParameterName.endsWith('catalogpath') ||
    normalizedParameterName.endsWith('labelspath') ||
    normalizedParameterName.endsWith('bankpath') ||
    normalizedParameterName.endsWith('path')
  );
  if (dataType === 'file' || pathLikeString) {
    return Object.freeze({
      kind: 'file',
      integer: false,
      nullable,
      extensionSlot: 'file-picker',
      filePickerFilter: resolveFilePickerFilter(parameterName),
      message: null
    });
  }
  if (dataType === 'camerabinding') {
    return Object.freeze({ kind: 'extension', integer: false, nullable, extensionSlot: 'camera-binding', filePickerFilter: null, message: '请在相机绑定编辑器中选择工程使用的相机。' });
  }
  if (['rectangle', 'circle', 'polygon', 'annulus', 'arc', 'circlesearch', 'npoint', 'caliper'].includes(dataType)) {
    return Object.freeze({ kind: 'extension', integer: false, nullable, extensionSlot: 'image-backed', filePickerFilter: null, message: '请在下方预览区使用“编辑 ROI”进行图上编辑。' });
  }
  if (dataType === 'color') {
    return Object.freeze({ kind: 'color', integer: false, nullable, extensionSlot: null, filePickerFilter: null, message: null });
  }
  if (input.options && input.options.length > 0) {
    return Object.freeze({ kind: 'enum', integer: false, nullable, extensionSlot: null, filePickerFilter: null, message: null });
  }
  if (['bool', 'boolean'].includes(dataType)) {
    return Object.freeze({ kind: 'boolean', integer: false, nullable, extensionSlot: null, filePickerFilter: null, message: null });
  }
  const integer = ['int', 'integer', 'int32', 'int64', 'long'].includes(dataType);
  if (integer || ['double', 'float', 'decimal', 'number'].includes(dataType)) {
    if (explicitSlider(input.raw)) {
      const bounded = typeof input.minValue === 'number' && Number.isFinite(input.minValue) &&
        typeof input.maxValue === 'number' && Number.isFinite(input.maxValue);
      return bounded
        ? Object.freeze({ kind: 'slider', integer, nullable, extensionSlot: null, filePickerFilter: null, message: null })
        : Object.freeze({ kind: 'unsupported', integer, nullable, extensionSlot: null, filePickerFilter: null, message: '滑块参数需要同时提供有效的最小值和最大值。' });
    }
    return Object.freeze({ kind: 'number', integer, nullable, extensionSlot: null, filePickerFilter: null, message: null });
  }
  if (['string', 'text', 'guid'].includes(dataType)) {
    return Object.freeze({ kind: 'text', integer: false, nullable, extensionSlot: null, filePickerFilter: null, message: null });
  }
  return Object.freeze({
    kind: 'unsupported',
    integer: false,
    nullable,
    extensionSlot: null,
    filePickerFilter: null,
    message: `当前工作区暂不支持参数类型：${input.dataType || '未知类型'}`
  });
}
