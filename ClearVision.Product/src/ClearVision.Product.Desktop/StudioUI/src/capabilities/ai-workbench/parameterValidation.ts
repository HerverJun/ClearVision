import type {
  AiBuildParameterV1,
  AiParameterConditionV1,
  AiParameterConditionSetV1,
  AiScalarValue
} from './contracts';

export interface AiParameterValidationResult {
  readonly valid: boolean;
  readonly errors: Readonly<Record<string, string>>;
  readonly activeKeys: readonly string[];
}

function isMissing(value: AiScalarValue | undefined): boolean {
  return value === undefined || value === null || (typeof value === 'string' && value.trim() === '');
}

function valuesEqual(left: AiScalarValue | undefined, right: AiScalarValue): boolean {
  if (typeof left === 'boolean' || typeof right === 'boolean') {
    const normalizedLeft = typeof left === 'boolean' ? left : String(left).toLowerCase() === 'true';
    const normalizedRight = typeof right === 'boolean' ? right : String(right).toLowerCase() === 'true';
    return normalizedLeft === normalizedRight;
  }
  return String(left ?? '').trim().toLowerCase() === String(right ?? '').trim().toLowerCase();
}

function conditionMatches(
  condition: AiParameterConditionV1,
  parameter: AiBuildParameterV1,
  values: Readonly<Record<string, AiScalarValue>>
): boolean {
  const dependency = `${parameter.tempId}.${condition.parameter}`;
  const value = Object.prototype.hasOwnProperty.call(values, dependency)
    ? values[dependency]
    : values[condition.parameter];
  if (condition.comparison === 'equals') return valuesEqual(value, condition.value);
  if (condition.comparison === 'not-equals') return !valuesEqual(value, condition.value);
  if (condition.comparison === 'empty') return isMissing(value);
  if (condition.comparison === 'not-empty') return !isMissing(value);
  return false;
}

function conditionsMatch(
  conditions: AiParameterConditionSetV1,
  parameter: AiBuildParameterV1,
  values: Readonly<Record<string, AiScalarValue>>
): boolean {
  const allMatches = conditions.allConditions.length === 0 ||
    conditions.allConditions.every(condition => conditionMatches(condition, parameter, values));
  const anyMatches = conditions.anyConditions.length === 0 ||
    conditions.anyConditions.some(condition => conditionMatches(condition, parameter, values));
  return allMatches && anyMatches;
}

function validateParameterContracts(
  parameters: readonly AiBuildParameterV1[]
): Record<string, string> {
  const errors: Record<string, string> = {};
  const namesByOperator = new Map<string, Set<string>>();
  const operatorTypes = new Map<string, string>();
  const canonicalKeys = new Set<string>();
  const supportedDataTypes = new Set([
    'bool', 'camerabinding', 'double', 'enum', 'file', 'guid', 'int', 'number', 'string', 'text'
  ]);
  const supportedComparisons = new Set(['equals', 'not-equals', 'empty', 'not-empty']);

  for (const parameter of parameters) {
    const operatorKey = parameter.tempId.trim().toLowerCase();
    const canonicalKey = parameter.canonicalKey.trim().toLowerCase();
    const expectedKey = `${parameter.tempId}.${parameter.parameterName}`.toLowerCase();
    const knownType = operatorTypes.get(operatorKey);
    if (!operatorKey || !parameter.operatorType.trim() || canonicalKey !== expectedKey ||
        canonicalKeys.has(canonicalKey) || (knownType && knownType !== parameter.operatorType.toLowerCase())) {
      errors[parameter.canonicalKey] = '参数身份与算子合同不一致。';
    }
    canonicalKeys.add(canonicalKey);
    operatorTypes.set(operatorKey, parameter.operatorType.toLowerCase());
    const names = namesByOperator.get(operatorKey) ?? new Set<string>();
    names.add(parameter.parameterName.toLowerCase());
    namesByOperator.set(operatorKey, names);
    if (!supportedDataTypes.has(parameter.dataType.toLowerCase())) {
      errors[parameter.canonicalKey] = '参数类型不受当前严格合同支持。';
    }
  }

  for (const parameter of parameters) {
    const knownNames = namesByOperator.get(parameter.tempId.toLowerCase()) ?? new Set<string>();
    const sets = [parameter.requiredWhen, parameter.enabledWhen, parameter.disabledWhen]
      .filter((set): set is AiParameterConditionSetV1 => set !== null);
    for (const set of sets) {
      for (const condition of [...set.allConditions, ...set.anyConditions]) {
        if (!knownNames.has(condition.parameter.toLowerCase()) ||
            !supportedComparisons.has(condition.comparison)) {
          errors[parameter.canonicalKey] = '参数条件引用了未知参数或比较规则。';
        }
      }
    }
  }
  return errors;
}

export function validateBuildParameterValues(
  parameters: readonly AiBuildParameterV1[],
  submittedValues: Readonly<Record<string, AiScalarValue>>,
  confirmedValues: Readonly<Record<string, AiScalarValue>> = {},
  validationKeys: readonly string[] | null = null
): AiParameterValidationResult {
  const values = Object.freeze({ ...confirmedValues, ...submittedValues });
  const errors = validateParameterContracts(parameters);
  const validationScope = validationKeys ? new Set(validationKeys) : null;
  const active = parameters.filter(parameter => !parameter.resourceDependent && !errors[parameter.canonicalKey] &&
    (parameter.enabledWhen === null || conditionsMatch(parameter.enabledWhen, parameter, values)) &&
    !(parameter.disabledWhen !== null && conditionsMatch(parameter.disabledWhen, parameter, values)));
  const validated = validationScope
    ? active.filter(parameter => validationScope.has(parameter.canonicalKey))
    : active;

  for (const parameter of validated) {
    const key = parameter.canonicalKey;
    const hasValue = Object.prototype.hasOwnProperty.call(values, key);
    const value = values[key];
    const required = parameter.requiredWhen !== null
      ? conditionsMatch(parameter.requiredWhen, parameter, values)
      : parameter.isRequired || parameter.requiredPolicy === 'required';
    if (!hasValue || value === null) {
      if (required && !parameter.atLeastOneGroup) errors[key] = '必填参数不能缺失或使用 null。';
      continue;
    }
    if (parameter.dataType === 'int' && (typeof value !== 'number' || !Number.isSafeInteger(value))) {
      errors[key] = '请输入有效整数。';
      continue;
    }
    if (['double', 'number'].includes(parameter.dataType) &&
        (typeof value !== 'number' || !Number.isFinite(value))) {
      errors[key] = '请输入有效数值。';
      continue;
    }
    if (parameter.dataType === 'bool' && typeof value !== 'boolean') {
      errors[key] = '请选择有效布尔值。';
      continue;
    }
    if (parameter.dataType === 'string' && typeof value !== 'string') {
      errors[key] = '请输入有效文本。';
      continue;
    }
    if (required && typeof value === 'string' && value.trim() === '') {
      errors[key] = '必填参数不能为空字符串。';
      continue;
    }
    if (typeof value === 'number') {
      if (typeof parameter.minValue === 'number' && value < parameter.minValue) {
        errors[key] = `不能小于 ${parameter.minValue}。`;
      } else if (typeof parameter.maxValue === 'number' && value > parameter.maxValue) {
        errors[key] = `不能大于 ${parameter.maxValue}。`;
      }
    }
    if (!errors[key] && parameter.options.length > 0 &&
        !parameter.options.some(option => valuesEqual(value, option.value))) {
      errors[key] = '请选择合同声明的枚举值。';
    }
  }

  const atLeastOneGroups = new Map<string, AiBuildParameterV1[]>();
  const mutuallyExclusiveGroups = new Map<string, AiBuildParameterV1[]>();
  for (const parameter of active) {
    if (parameter.atLeastOneGroup) {
      const members = atLeastOneGroups.get(parameter.atLeastOneGroup) ?? [];
      members.push(parameter);
      atLeastOneGroups.set(parameter.atLeastOneGroup, members);
    }
    if (parameter.mutuallyExclusiveGroup) {
      const members = mutuallyExclusiveGroups.get(parameter.mutuallyExclusiveGroup) ?? [];
      members.push(parameter);
      mutuallyExclusiveGroups.set(parameter.mutuallyExclusiveGroup, members);
    }
  }
  for (const members of atLeastOneGroups.values()) {
    if (members.some(parameter => !isMissing(values[parameter.canonicalKey]))) continue;
    for (const parameter of members) {
      if (!validationScope || validationScope.has(parameter.canonicalKey)) {
        errors[parameter.canonicalKey] = '此组参数至少需要确认一项。';
      }
    }
  }
  for (const members of mutuallyExclusiveGroups.values()) {
    const configured = members.filter(parameter => !isMissing(values[parameter.canonicalKey]));
    if (configured.length > 1) {
      for (const parameter of configured) {
        if (!validationScope || validationScope.has(parameter.canonicalKey)) {
          errors[parameter.canonicalKey] = '互斥参数只能确认其中一项。';
        }
      }
    }
  }

  return Object.freeze({
    valid: Object.keys(errors).length === 0,
    errors: Object.freeze(errors),
    activeKeys: Object.freeze(validated.map(parameter => parameter.canonicalKey))
  });
}
