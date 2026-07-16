import type {
  InspectorParameterCondition,
  InspectorParameterConditionSet,
  InspectorParameterConstraint,
  InspectorOutputAvailabilityRule
} from './parameterContracts';

export type InspectorValidationCode =
  | 'required'
  | 'type'
  | 'range'
  | 'enum'
  | 'disabled'
  | 'at-least-one'
  | 'mutually-exclusive';

export interface InspectorValidationError {
  readonly code: InspectorValidationCode;
  readonly parameterNames: readonly string[];
  readonly message: string;
  readonly reasonCode: string | null;
}

export interface InspectorParameterValidationDescriptor {
  readonly name: string;
  readonly label: string;
  readonly dataType: string;
  readonly isRequired: boolean;
  readonly nullable: boolean;
  readonly integer: boolean;
  readonly options: readonly Readonly<{ label: string; value: string }>[] | null;
  readonly minValue: unknown;
  readonly maxValue: unknown;
  readonly explicitValuePresent: boolean;
  readonly value: unknown;
  readonly defaultValue: unknown;
}

export interface InspectorParameterConstraintState {
  readonly parameterName: string;
  readonly effectiveRequired: boolean;
  readonly effectiveDisabled: boolean;
  readonly effectiveVisible: boolean;
  readonly effectiveIgnored: boolean;
  readonly constraint: InspectorParameterConstraint | null;
}

function normalizedName(value: string): string {
  return value.trim().toLowerCase();
}

export function isInspectorParameterMissing(value: unknown): boolean {
  if (value === null || value === undefined) return true;
  if (typeof value !== 'string') return false;
  const text = value.trim();
  if (text.length === 0) return true;
  if (/^<pending>$/i.test(text)) return true;
  return /^<pending-[^<>\s]+>$/i.test(text);
}

function valuesEqual(left: unknown, right: unknown): boolean {
  const leftBoolean = typeof left === 'boolean' ? left : normalizedBoolean(left);
  const rightBoolean = typeof right === 'boolean' ? right : normalizedBoolean(right);
  if (leftBoolean !== null && rightBoolean !== null) return leftBoolean === rightBoolean;
  return String(left ?? '').trim().toLowerCase() === String(right ?? '').trim().toLowerCase();
}

function normalizedBoolean(value: unknown): boolean | null {
  if (typeof value !== 'string') return null;
  if (value.trim().toLowerCase() === 'true') return true;
  if (value.trim().toLowerCase() === 'false') return false;
  return null;
}

function valueFrom(values: ReadonlyMap<string, unknown>, name: string): unknown {
  return values.get(normalizedName(name));
}

function evaluateCondition(condition: InspectorParameterCondition, values: ReadonlyMap<string, unknown>): boolean {
  const value = valueFrom(values, condition.parameter);
  if (condition.comparison === 'equals') return valuesEqual(value, condition.value);
  if (condition.comparison === 'not-equals') return !valuesEqual(value, condition.value);
  if (condition.comparison === 'empty') return isInspectorParameterMissing(value);
  return !isInspectorParameterMissing(value);
}

export function evaluateInspectorConditionSet(
  set: InspectorParameterConditionSet,
  values: ReadonlyMap<string, unknown>
): boolean {
  const all = set.all.length === 0 || set.all.every(item => evaluateCondition(item, values));
  const any = set.any.length === 0 || set.any.some(item => evaluateCondition(item, values));
  return all && any;
}

function buildEffectiveValues(
  parameters: readonly InspectorParameterValidationDescriptor[],
  constraints: readonly InspectorParameterConstraint[],
  patch?: Readonly<{ name: string; value: unknown }>
): ReadonlyMap<string, unknown> {
  const explicit = new Map<string, unknown>();
  const defaults = new Map<string, unknown>();
  for (const parameter of parameters) {
    const key = normalizedName(parameter.name);
    if (parameter.defaultValue !== null && parameter.defaultValue !== undefined) {
      defaults.set(key, parameter.defaultValue);
    }
    if (parameter.explicitValuePresent) explicit.set(key, parameter.value);
  }
  if (patch) explicit.set(normalizedName(patch.name), patch.value);

  for (const constraint of constraints.filter(item => item.aliasFor !== null)) {
    const alias = normalizedName(constraint.parameter);
    const canonical = normalizedName(constraint.aliasFor!);
    if (!explicit.has(canonical) && explicit.has(alias)) explicit.set(canonical, explicit.get(alias));
  }

  const effective = new Map(defaults);
  for (const [key, value] of explicit) effective.set(key, value);
  for (const constraint of constraints.filter(item => item.aliasFor !== null)) {
    const canonical = normalizedName(constraint.aliasFor!);
    if (effective.has(canonical)) effective.set(normalizedName(constraint.parameter), effective.get(canonical));
  }
  return effective;
}

export function resolveInspectorConstraintStates(
  parameters: readonly InspectorParameterValidationDescriptor[],
  constraints: readonly InspectorParameterConstraint[],
  patch?: Readonly<{ name: string; value: unknown }>
): ReadonlyMap<string, InspectorParameterConstraintState> {
  const values = buildEffectiveValues(parameters, constraints, patch);
  const constraintsByName = new Map(constraints.map(item => [normalizedName(item.parameter), item]));
  const states = new Map<string, InspectorParameterConstraintState>();
  for (const parameter of parameters) {
    const key = normalizedName(parameter.name);
    const constraint = constraintsByName.get(key) ?? null;
    let required = constraint?.requiredPolicy === 'required'
      ? true
      : constraint?.requiredPolicy === 'optional' ? false : parameter.isRequired;
    if (constraint?.requiredWhen) required = evaluateInspectorConditionSet(constraint.requiredWhen, values);
    const visible = (constraint?.visibleWhen === null || constraint?.visibleWhen === undefined ||
      evaluateInspectorConditionSet(constraint.visibleWhen, values)) &&
      !(constraint?.hiddenWhen && evaluateInspectorConditionSet(constraint.hiddenWhen, values));
    const ignored = Boolean(constraint?.ignoredWhen && evaluateInspectorConditionSet(constraint.ignoredWhen, values));
    const enabled = constraint?.enabledWhen === null || constraint?.enabledWhen === undefined ||
      evaluateInspectorConditionSet(constraint.enabledWhen, values);
    const disabled = !enabled || ignored || Boolean(
      constraint?.disabledWhen && evaluateInspectorConditionSet(constraint.disabledWhen, values)
    );
    states.set(key, Object.freeze({
      parameterName: parameter.name,
      effectiveRequired: required && !disabled,
      effectiveDisabled: disabled,
      effectiveVisible: visible,
      effectiveIgnored: ignored,
      constraint
    }));
  }

  for (const state of [...states.values()]) {
    const group = state.constraint?.mutuallyExclusiveGroup;
    if (!group || state.effectiveDisabled || !isInspectorParameterMissing(valueFrom(values, state.parameterName))) continue;
    const configuredPeer = [...states.values()].some(peer =>
      peer.parameterName !== state.parameterName &&
      peer.constraint?.mutuallyExclusiveGroup?.toLowerCase() === group.toLowerCase() &&
      !peer.effectiveDisabled && !peer.effectiveIgnored &&
      !isInspectorParameterMissing(valueFrom(values, peer.parameterName))
    );
    if (configuredPeer) {
      states.set(normalizedName(state.parameterName), Object.freeze({
        ...state,
        effectiveRequired: false,
        effectiveDisabled: true
      }));
    }
  }
  return states;
}

function validateBasic(
  parameter: InspectorParameterValidationDescriptor,
  value: unknown,
  required: boolean
): readonly InspectorValidationError[] {
  const type = parameter.dataType.trim().toLowerCase();
  if (isInspectorParameterMissing(value)) {
    if (required) return [Object.freeze({ code: 'required', parameterNames: [parameter.name], message: `${parameter.label}为必填项。`, reasonCode: null })];
    if (value === null && parameter.nullable) return Object.freeze([]);
    if (typeof value === 'string' && ['string', 'text', 'guid'].includes(type)) return Object.freeze([]);
    return [Object.freeze({ code: 'type', parameterNames: [parameter.name], message: `${parameter.label}不允许空值。`, reasonCode: null })];
  }
  if (['bool', 'boolean'].includes(type) && typeof value !== 'boolean') {
    return [Object.freeze({ code: 'type', parameterNames: [parameter.name], message: `${parameter.label}必须是布尔值。`, reasonCode: null })];
  }
  const numeric = parameter.integer || ['double', 'float', 'decimal', 'number'].includes(type);
  if (numeric) {
    if (typeof value !== 'number' || !Number.isFinite(value) || (parameter.integer && !Number.isSafeInteger(value))) {
      return [Object.freeze({ code: 'type', parameterNames: [parameter.name], message: `${parameter.label}必须是${parameter.integer ? '整数' : '有限数字'}。`, reasonCode: null })];
    }
    if (typeof parameter.minValue === 'number' && value < parameter.minValue) {
      return [Object.freeze({ code: 'range', parameterNames: [parameter.name], message: `${parameter.label}不能小于 ${parameter.minValue}。`, reasonCode: null })];
    }
    if (typeof parameter.maxValue === 'number' && value > parameter.maxValue) {
      return [Object.freeze({ code: 'range', parameterNames: [parameter.name], message: `${parameter.label}不能大于 ${parameter.maxValue}。`, reasonCode: null })];
    }
  }
  if (parameter.options && !parameter.options.some(option => option.value === value)) {
    return [Object.freeze({ code: 'enum', parameterNames: [parameter.name], message: `${parameter.label}必须使用 metadata 枚举值。`, reasonCode: null })];
  }
  if (['string', 'text', 'guid'].includes(type) && typeof value !== 'string') {
    return [Object.freeze({ code: 'type', parameterNames: [parameter.name], message: `${parameter.label}必须是字符串。`, reasonCode: null })];
  }
  return Object.freeze([]);
}

export function validateInspectorParameterPatch(
  parameters: readonly InspectorParameterValidationDescriptor[],
  constraints: readonly InspectorParameterConstraint[],
  parameterName: string,
  value: unknown,
  satisfiedInputPorts: ReadonlySet<string> = new Set<string>()
): readonly InspectorValidationError[] {
  const parameter = parameters.find(item => normalizedName(item.name) === normalizedName(parameterName));
  if (!parameter) return [Object.freeze({ code: 'type', parameterNames: [parameterName], message: '参数合同不存在。', reasonCode: null })];
  const patch = Object.freeze({ name: parameter.name, value });
  const states = resolveInspectorConstraintStates(parameters, constraints, patch);
  const state = states.get(normalizedName(parameter.name));
  if (state?.effectiveDisabled || state?.effectiveIgnored) {
    return [Object.freeze({
      code: 'disabled',
      parameterNames: [parameter.name],
      message: `${parameter.label}当前由 metadata constraints 禁用。`,
      reasonCode: state.constraint?.reasonCode ?? null
    })];
  }
  const satisfiedByInput = state?.constraint?.satisfiedByInputPorts.some(name =>
    [...satisfiedInputPorts].some(port => normalizedName(port) === normalizedName(name))) === true;
  const errors = [...validateBasic(
    parameter,
    value,
    (state?.effectiveRequired ?? parameter.isRequired) && !satisfiedByInput
  )];
  if (errors.length > 0) return Object.freeze(errors);

  const values = buildEffectiveValues(parameters, constraints, patch);
  const active = [...states.values()].filter(item => !item.effectiveDisabled && !item.effectiveIgnored);
  const atLeastGroups = new Map<string, InspectorParameterConstraintState[]>();
  const exclusiveGroups = new Map<string, InspectorParameterConstraintState[]>();
  for (const item of active) {
    const atLeast = item.constraint?.atLeastOneGroup;
    const exclusive = item.constraint?.mutuallyExclusiveGroup;
    if (atLeast) atLeastGroups.set(atLeast, [...(atLeastGroups.get(atLeast) ?? []), item]);
    if (exclusive) exclusiveGroups.set(exclusive, [...(exclusiveGroups.get(exclusive) ?? []), item]);
  }
  for (const [group, items] of atLeastGroups) {
    const required = items.filter(item => item.effectiveRequired);
    const configured = required.some(item =>
      !isInspectorParameterMissing(valueFrom(values, item.parameterName)) ||
      item.constraint?.satisfiedByInputPorts.some(name =>
        [...satisfiedInputPorts].some(port => normalizedName(port) === normalizedName(name))) === true
    );
    if (required.length > 0 && !configured) {
      errors.push(Object.freeze({
        code: 'at-least-one',
        parameterNames: required.map(item => item.parameterName),
        message: `参数组 ${group} 至少需要配置一项。`,
        reasonCode: required[0]?.constraint?.reasonCode ?? null
      }));
    }
  }
  for (const [group, items] of exclusiveGroups) {
    const configured = items.filter(item => !isInspectorParameterMissing(valueFrom(values, item.parameterName)));
    if (configured.length > 1) {
      errors.push(Object.freeze({
        code: 'mutually-exclusive',
        parameterNames: configured.map(item => item.parameterName),
        message: `参数组 ${group} 只能配置一项。`,
        reasonCode: configured[0]?.constraint?.reasonCode ?? null
      }));
    }
  }
  return Object.freeze(errors);
}

export function resolveInspectorOutputAvailability(
  outputName: string,
  rules: readonly InspectorOutputAvailabilityRule[],
  parameters: readonly InspectorParameterValidationDescriptor[],
  constraints: readonly InspectorParameterConstraint[]
): Readonly<{ available: boolean; reasonCode: string }> {
  const rule = rules.find(item => normalizedName(item.output) === normalizedName(outputName));
  if (!rule?.availableWhen) return Object.freeze({ available: true, reasonCode: 'OUTPUT_ALWAYS_AVAILABLE' });
  return Object.freeze({
    available: evaluateInspectorConditionSet(rule.availableWhen, buildEffectiveValues(parameters, constraints)),
    reasonCode: rule.reasonCode
  });
}
