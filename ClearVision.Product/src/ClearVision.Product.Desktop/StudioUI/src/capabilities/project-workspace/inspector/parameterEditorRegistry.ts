export type InspectorParameterEditorKind =
  | 'text'
  | 'number'
  | 'boolean'
  | 'enum'
  | 'slider'
  | 'extension'
  | 'unsupported';

export interface InspectorParameterEditorRegistration {
  readonly kind: InspectorParameterEditorKind;
  readonly integer: boolean;
  readonly nullable: boolean;
  readonly extensionSlot: 'file-picker' | 'camera-binding' | 'image-backed' | null;
  readonly message: string | null;
}

export interface InspectorParameterEditorInput {
  readonly dataType: string;
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
  const nullable = explicitNullable(input.raw, input.value);
  if (dataType === 'file') {
    return Object.freeze({ kind: 'extension', integer: false, nullable, extensionSlot: 'file-picker', message: '文件选择在 G3 本轮范围外。' });
  }
  if (dataType === 'camerabinding') {
    return Object.freeze({ kind: 'extension', integer: false, nullable, extensionSlot: 'camera-binding', message: 'CameraBinding 资源补齐在 G3 本轮范围外。' });
  }
  if (['rectangle', 'circle', 'polygon', 'annulus', 'circlesearch', 'npoint', 'caliper'].includes(dataType)) {
    return Object.freeze({ kind: 'extension', integer: false, nullable, extensionSlot: 'image-backed', message: '图像耦合编辑器接口已保留，将由 G4 接入唯一 ImageCanvas owner。' });
  }
  if (input.options && input.options.length > 0) {
    return Object.freeze({ kind: 'enum', integer: false, nullable, extensionSlot: null, message: null });
  }
  if (['bool', 'boolean'].includes(dataType)) {
    return Object.freeze({ kind: 'boolean', integer: false, nullable, extensionSlot: null, message: null });
  }
  const integer = ['int', 'integer', 'int32', 'int64', 'long'].includes(dataType);
  if (integer || ['double', 'float', 'decimal', 'number'].includes(dataType)) {
    if (explicitSlider(input.raw)) {
      const bounded = typeof input.minValue === 'number' && Number.isFinite(input.minValue) &&
        typeof input.maxValue === 'number' && Number.isFinite(input.maxValue);
      return bounded
        ? Object.freeze({ kind: 'slider', integer, nullable, extensionSlot: null, message: null })
        : Object.freeze({ kind: 'unsupported', integer, nullable, extensionSlot: null, message: 'slider metadata 必须同时提供有限 min/max。' });
    }
    return Object.freeze({ kind: 'number', integer, nullable, extensionSlot: null, message: null });
  }
  if (['string', 'text', 'guid'].includes(dataType)) {
    return Object.freeze({ kind: 'text', integer: false, nullable, extensionSlot: null, message: null });
  }
  return Object.freeze({
    kind: 'unsupported',
    integer: false,
    nullable,
    extensionSlot: null,
    message: `不支持的参数类型：${input.dataType || 'unknown'}`
  });
}
