export class OperatorContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expectation: string) {
    super(`Operator response field ${path} must be ${expectation}.`);
    this.name = 'OperatorContractDecodeError';
    this.path = path;
  }
}

export const operatorCategories = Object.freeze([
  'Acquisition',
  'ImagePreprocessing',
  'SegmentationAndRegion',
  'FeatureExtraction',
  'MatchingAndLocalization',
  'DefectDetection',
  'Measurement',
  'CalibrationAndCoordinates',
  'AiInference',
  'PointCloud3D',
  'DataProcessing',
  'FlowControl',
  'Communication',
  'OutputAndAuxiliary'
] as const);

export type OperatorCategoryId = typeof operatorCategories[number];

export const operatorLifecycles = Object.freeze([
  'Stable',
  'Experimental',
  'Reference',
  'Legacy',
  'Deprecated'
] as const);

export type OperatorLifecycle = typeof operatorLifecycles[number];

export const operatorPortDataTypes = Object.freeze({
  0: 'Image',
  1: 'Integer',
  2: 'Float',
  3: 'Boolean',
  4: 'String',
  5: 'Point',
  6: 'Rectangle',
  7: 'Contour',
  8: 'PointList',
  9: 'DetectionResult',
  10: 'DetectionList',
  11: 'CircleData',
  12: 'LineData',
  13: 'Region',
  14: 'BlobList',
  15: 'BlobFeatureList',
  99: 'Any'
} as const);

export type OperatorPortDataType = typeof operatorPortDataTypes[keyof typeof operatorPortDataTypes];

export interface OperatorPort {
  readonly name: string;
  readonly displayName: string;
  readonly dataType: string;
  readonly isRequired: boolean;
  readonly description: string | null;
}

export interface OperatorParameterOption {
  readonly label: string;
  readonly value: string;
}

export interface OperatorParameter {
  readonly name: string;
  readonly displayName: string;
  readonly description: string | null;
  readonly dataType: string;
  readonly defaultValue: unknown;
  readonly minValue: unknown;
  readonly maxValue: unknown;
  readonly isRequired: boolean;
  readonly options: readonly OperatorParameterOption[] | null;
}

export interface OperatorCatalogItem {
  readonly operatorType: string;
  readonly displayName: string;
  readonly description: string;
  readonly categoryId: OperatorCategoryId;
  readonly category: string;
  readonly lifecycle: OperatorLifecycle;
  readonly lifecycleNote: string | null;
  readonly defaultHidden: boolean;
  readonly iconName: string | null;
  readonly keywords: readonly string[];
  readonly tags: readonly string[];
  readonly version: string;
  readonly inputPorts: readonly OperatorPort[];
  readonly outputPorts: readonly OperatorPort[];
  readonly parameters: readonly OperatorParameter[];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function record(value: unknown, path: string): Record<string, unknown> {
  if (!isRecord(value)) throw new OperatorContractDecodeError(path, 'an object');
  return value;
}

function string(value: unknown, path: string, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && value.trim().length === 0)) {
    throw new OperatorContractDecodeError(path, allowEmpty ? 'a string' : 'a non-empty string');
  }
  return value;
}

function nullableString(value: unknown, path: string): string | null {
  if (value === null || value === undefined) return null;
  return string(value, path, true);
}

function boolean(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') throw new OperatorContractDecodeError(path, 'a boolean');
  return value;
}

function enumValue<T extends string>(
  value: unknown,
  path: string,
  values: readonly T[]
): T {
  if (typeof value === 'number' && Number.isInteger(value) && value >= 0 && value < values.length) {
    return values[value]!;
  }
  if (typeof value === 'string' && values.includes(value as T)) return value as T;
  throw new OperatorContractDecodeError(path, `one of ${values.join(', ')} or its numeric enum value`);
}

function operatorType(value: unknown, path: string): string {
  if (typeof value === 'number' && Number.isInteger(value) && value >= 0) return String(value);
  return string(value, path);
}

function dataType(value: unknown, path: string): OperatorPortDataType {
  const names = Object.values(operatorPortDataTypes) as readonly OperatorPortDataType[];
  if (typeof value === 'number' && Number.isInteger(value)) {
    const name = operatorPortDataTypes[value as keyof typeof operatorPortDataTypes];
    if (name) return name;
  }
  if (typeof value === 'string' && names.includes(value as OperatorPortDataType)) {
    return value as OperatorPortDataType;
  }
  throw new OperatorContractDecodeError(path, `a known PortDataType (${names.join(', ')})`);
}

function stringArray(value: unknown, path: string): readonly string[] {
  if (value === null || value === undefined) return Object.freeze([]);
  if (!Array.isArray(value)) throw new OperatorContractDecodeError(path, 'an array of strings or null');
  return Object.freeze(value.map((item, index) => string(item, `${path}[${index}]`, true)));
}

function port(value: unknown, path: string): OperatorPort {
  const item = record(value, path);
  return Object.freeze({
    name: string(item.name, `${path}.name`),
    displayName: string(item.displayName, `${path}.displayName`, true),
    dataType: dataType(item.dataType, `${path}.dataType`),
    isRequired: boolean(item.isRequired, `${path}.isRequired`),
    description: nullableString(item.description, `${path}.description`)
  });
}

function option(value: unknown, path: string): OperatorParameterOption {
  const item = record(value, path);
  return Object.freeze({
    label: string(item.label, `${path}.label`, true),
    value: string(item.value, `${path}.value`, true)
  });
}

function options(value: unknown, path: string): readonly OperatorParameterOption[] | null {
  if (value === null || value === undefined) return null;
  if (!Array.isArray(value)) throw new OperatorContractDecodeError(path, 'an array or null');
  return Object.freeze(value.map((item, index) => option(item, `${path}[${index}]`)));
}

function parameter(value: unknown, path: string): OperatorParameter {
  const item = record(value, path);
  return Object.freeze({
    name: string(item.name, `${path}.name`),
    displayName: string(item.displayName, `${path}.displayName`, true),
    description: nullableString(item.description, `${path}.description`),
    dataType: string(item.dataType, `${path}.dataType`),
    defaultValue: item.defaultValue,
    minValue: item.minValue,
    maxValue: item.maxValue,
    isRequired: boolean(item.isRequired, `${path}.isRequired`),
    options: options(item.options, `${path}.options`)
  });
}

function array<T>(
  value: unknown,
  path: string,
  decode: (entry: unknown, entryPath: string) => T
): readonly T[] {
  if (!Array.isArray(value)) throw new OperatorContractDecodeError(path, 'an array');
  return Object.freeze(value.map((entry, index) => decode(entry, `${path}[${index}]`)));
}

export function decodeOperatorCatalogItem(payload: unknown, path = '$'): OperatorCatalogItem {
  const item = record(payload, path);
  return Object.freeze({
    operatorType: operatorType(item.type, `${path}.type`),
    displayName: string(item.displayName, `${path}.displayName`),
    description: string(item.description, `${path}.description`, true),
    categoryId: enumValue(item.categoryId, `${path}.categoryId`, operatorCategories),
    category: string(item.category, `${path}.category`),
    lifecycle: enumValue(item.lifecycle, `${path}.lifecycle`, operatorLifecycles),
    lifecycleNote: nullableString(item.lifecycleNote, `${path}.lifecycleNote`),
    defaultHidden: boolean(item.defaultHidden, `${path}.defaultHidden`),
    iconName: nullableString(item.iconName, `${path}.iconName`),
    keywords: stringArray(item.keywords, `${path}.keywords`),
    tags: stringArray(item.tags, `${path}.tags`),
    version: string(item.version, `${path}.version`),
    inputPorts: array(item.inputPorts, `${path}.inputPorts`, port),
    outputPorts: array(item.outputPorts, `${path}.outputPorts`, port),
    parameters: array(item.parameters, `${path}.parameters`, parameter)
  });
}

export function decodeOperatorCatalog(payload: unknown): readonly OperatorCatalogItem[] {
  if (!Array.isArray(payload)) throw new OperatorContractDecodeError('$', 'an array');
  const items = payload.map((item, index) => decodeOperatorCatalogItem(item, `$[${index}]`));
  const types = new Set<string>();
  for (const item of items) {
    if (types.has(item.operatorType)) {
      throw new OperatorContractDecodeError('$.type', 'unique operator identities');
    }
    types.add(item.operatorType);
  }
  return Object.freeze(items);
}

export function isOperatorType(value: string): boolean {
  return /^(?:\d+|[A-Za-z][A-Za-z0-9_]*)$/.test(value);
}
