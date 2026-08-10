export type InspectorConditionComparison = 'equals' | 'not-equals' | 'empty' | 'not-empty';
export type InspectorRequiredPolicy = 'metadata' | 'required' | 'optional';

export interface InspectorParameterCondition {
  readonly parameter: string;
  readonly comparison: InspectorConditionComparison;
  readonly value: unknown;
}

export interface InspectorParameterConditionSet {
  readonly all: readonly InspectorParameterCondition[];
  readonly any: readonly InspectorParameterCondition[];
}

export interface InspectorParameterConstraint {
  readonly parameter: string;
  readonly requiredPolicy: InspectorRequiredPolicy;
  readonly requiredWhen: InspectorParameterConditionSet | null;
  readonly enabledWhen: InspectorParameterConditionSet | null;
  readonly disabledWhen: InspectorParameterConditionSet | null;
  readonly visibleWhen: InspectorParameterConditionSet | null;
  readonly hiddenWhen: InspectorParameterConditionSet | null;
  readonly ignoredWhen: InspectorParameterConditionSet | null;
  readonly atLeastOneGroup: string | null;
  readonly mutuallyExclusiveGroup: string | null;
  readonly aliasFor: string | null;
  readonly deprecated: boolean;
  readonly resourceKind: string | null;
  readonly reasonCode: string;
  readonly satisfiedByInputPorts: readonly string[];
}

export interface InspectorOutputAvailabilityRule {
  readonly output: string;
  readonly availableWhen: InspectorParameterConditionSet | null;
  readonly reasonCode: string;
}

export class InspectorMetadataDecodeError extends Error {
  readonly path: string;
  readonly expectation: string;

  constructor(path: string, expectation: string) {
    super('属性检查器参数定义格式不符合要求，请刷新后重试。');
    this.name = 'InspectorMetadataDecodeError';
    this.path = path;
    this.expectation = expectation;
  }
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function field(source: Readonly<Record<string, unknown>>, camel: string): unknown {
  const pascal = `${camel.slice(0, 1).toUpperCase()}${camel.slice(1)}`;
  return Object.prototype.hasOwnProperty.call(source, camel) ? source[camel] : source[pascal];
}

function requiredString(value: unknown, path: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new InspectorMetadataDecodeError(path, 'a non-empty string');
  }
  return value;
}

function optionalString(value: unknown, path: string): string | null {
  if (value === null || value === undefined || value === '') return null;
  if (typeof value !== 'string') throw new InspectorMetadataDecodeError(path, 'a string or null');
  return value;
}

function condition(value: unknown, path: string): InspectorParameterCondition {
  if (!isRecord(value)) throw new InspectorMetadataDecodeError(path, 'an object');
  const comparison = requiredString(field(value, 'comparison'), `${path}.comparison`).toLowerCase();
  if (!['equals', 'not-equals', 'empty', 'not-empty'].includes(comparison)) {
    throw new InspectorMetadataDecodeError(`${path}.comparison`, 'a supported comparison');
  }
  return Object.freeze({
    parameter: requiredString(field(value, 'parameter'), `${path}.parameter`),
    comparison: comparison as InspectorConditionComparison,
    value: field(value, 'value')
  });
}

function conditionList(value: unknown, path: string): readonly InspectorParameterCondition[] {
  if (value === null || value === undefined) return Object.freeze([]);
  if (!Array.isArray(value)) throw new InspectorMetadataDecodeError(path, 'an array');
  return Object.freeze(value.map((entry, index) => condition(entry, `${path}[${index}]`)));
}

function conditionSet(value: unknown, path: string): InspectorParameterConditionSet | null {
  if (value === null || value === undefined) return null;
  if (!isRecord(value)) throw new InspectorMetadataDecodeError(path, 'an object or null');
  return Object.freeze({
    all: conditionList(field(value, 'all'), `${path}.all`),
    any: conditionList(field(value, 'any'), `${path}.any`)
  });
}

function stringList(value: unknown, path: string): readonly string[] {
  if (value === null || value === undefined) return Object.freeze([]);
  if (!Array.isArray(value)) throw new InspectorMetadataDecodeError(path, 'an array of strings');
  return Object.freeze(value.map((entry, index) => requiredString(entry, `${path}[${index}]`)));
}

export function decodeInspectorParameterConstraints(
  records: readonly Readonly<Record<string, unknown>>[]
): readonly InspectorParameterConstraint[] {
  return Object.freeze(records.map((source, index) => {
    const path = `parameterConstraints[${index}]`;
    const policy = String(field(source, 'requiredPolicy') ?? 'metadata').toLowerCase();
    if (!['metadata', 'required', 'optional'].includes(policy)) {
      throw new InspectorMetadataDecodeError(`${path}.requiredPolicy`, 'metadata, required, or optional');
    }
    const deprecated = field(source, 'deprecated');
    if (deprecated !== undefined && typeof deprecated !== 'boolean') {
      throw new InspectorMetadataDecodeError(`${path}.deprecated`, 'a boolean');
    }
    return Object.freeze({
      parameter: requiredString(field(source, 'parameter'), `${path}.parameter`),
      requiredPolicy: policy as InspectorRequiredPolicy,
      requiredWhen: conditionSet(field(source, 'requiredWhen'), `${path}.requiredWhen`),
      enabledWhen: conditionSet(field(source, 'enabledWhen'), `${path}.enabledWhen`),
      disabledWhen: conditionSet(field(source, 'disabledWhen'), `${path}.disabledWhen`),
      visibleWhen: conditionSet(field(source, 'visibleWhen'), `${path}.visibleWhen`),
      hiddenWhen: conditionSet(field(source, 'hiddenWhen'), `${path}.hiddenWhen`),
      ignoredWhen: conditionSet(field(source, 'ignoredWhen'), `${path}.ignoredWhen`),
      atLeastOneGroup: optionalString(field(source, 'atLeastOneGroup'), `${path}.atLeastOneGroup`),
      mutuallyExclusiveGroup: optionalString(
        field(source, 'mutuallyExclusiveGroup'),
        `${path}.mutuallyExclusiveGroup`
      ),
      aliasFor: optionalString(field(source, 'aliasFor'), `${path}.aliasFor`),
      deprecated: deprecated === true,
      resourceKind: optionalString(field(source, 'resourceKind'), `${path}.resourceKind`),
      reasonCode: optionalString(field(source, 'reasonCode'), `${path}.reasonCode`) ?? 'PARAMETER_CONSTRAINT',
      satisfiedByInputPorts: stringList(
        field(source, 'satisfiedByInputPorts'),
        `${path}.satisfiedByInputPorts`
      )
    });
  }));
}

export function decodeInspectorOutputAvailabilityRules(
  records: readonly Readonly<Record<string, unknown>>[]
): readonly InspectorOutputAvailabilityRule[] {
  return Object.freeze(records.map((source, index) => {
    const path = `outputAvailabilityRules[${index}]`;
    return Object.freeze({
      output: requiredString(field(source, 'output'), `${path}.output`),
      availableWhen: conditionSet(field(source, 'availableWhen'), `${path}.availableWhen`),
      reasonCode: optionalString(field(source, 'reasonCode'), `${path}.reasonCode`) ?? 'OUTPUT_CONDITION_NOT_MET'
    });
  }));
}
