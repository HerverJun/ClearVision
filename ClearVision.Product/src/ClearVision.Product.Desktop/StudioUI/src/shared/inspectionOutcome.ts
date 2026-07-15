import type { CvStatusTone } from '@/design-system';

export const executionOutcomes = Object.freeze([
  'Succeeded',
  'Failed',
  'Cancelled',
  'TimedOut',
  'Skipped'
] as const);

export const decisionOutcomes = Object.freeze([
  'Ok',
  'Ng',
  'Undetermined',
  'NotApplicable',
  'Invalid'
] as const);

export const canonicalInspectionOutcomeKinds = Object.freeze([
  'Ok',
  'Ng',
  'Undetermined',
  'NotApplicable',
  'Invalid',
  'Failed',
  'Cancelled',
  'TimedOut',
  'Skipped'
] as const);

export type ExecutionOutcome = (typeof executionOutcomes)[number];
export type DecisionOutcome = (typeof decisionOutcomes)[number];
export type CanonicalInspectionOutcomeKind = (typeof canonicalInspectionOutcomeKinds)[number];

export interface InspectionOutcome {
  readonly execution: ExecutionOutcome;
  readonly decision: DecisionOutcome;
}

export interface InspectionOutcomePresentation extends InspectionOutcome {
  readonly kind: CanonicalInspectionOutcomeKind;
  readonly label: string;
  readonly executionLabel: string;
  readonly decisionLabel: string;
  readonly tone: CvStatusTone;
}

export class InspectionOutcomeDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expectation: string) {
    super(`Inspection outcome field ${path} must be ${expectation}.`);
    this.name = 'InspectionOutcomeDecodeError';
    this.path = path;
  }
}

const executionLabels: Readonly<Record<ExecutionOutcome, string>> = Object.freeze({
  Succeeded: '执行成功',
  Failed: '执行失败',
  Cancelled: '已取消',
  TimedOut: '执行超时',
  Skipped: '已跳过'
});

const decisionLabels: Readonly<Record<DecisionOutcome, string>> = Object.freeze({
  Ok: '判定 OK',
  Ng: '判定 NG',
  Undetermined: '未判定',
  NotApplicable: '不适用',
  Invalid: '判定无效'
});

const kindPresentation: Readonly<Record<CanonicalInspectionOutcomeKind, {
  readonly label: string;
  readonly tone: CvStatusTone;
}>> = Object.freeze({
  Ok: Object.freeze({ label: 'OK', tone: 'ok' }),
  Ng: Object.freeze({ label: 'NG', tone: 'ng' }),
  Undetermined: Object.freeze({ label: '未判定', tone: 'warning' }),
  NotApplicable: Object.freeze({ label: '不适用', tone: 'info' }),
  Invalid: Object.freeze({ label: '判定无效', tone: 'warning' }),
  Failed: Object.freeze({ label: '执行失败', tone: 'ng' }),
  Cancelled: Object.freeze({ label: '已取消', tone: 'idle' }),
  TimedOut: Object.freeze({ label: '执行超时', tone: 'ng' }),
  Skipped: Object.freeze({ label: '已跳过', tone: 'idle' })
});

function decodeEnumValue<T extends string>(
  value: unknown,
  path: string,
  allowed: readonly T[]
): T {
  if (typeof value !== 'string' || !allowed.includes(value as T)) {
    throw new InspectionOutcomeDecodeError(path, `one of ${allowed.join(', ')}`);
  }
  return value as T;
}

export function decodeExecutionOutcome(value: unknown, path = '$.executionOutcome'): ExecutionOutcome {
  return decodeEnumValue(value, path, executionOutcomes);
}

export function decodeDecisionOutcome(value: unknown, path = '$.decisionOutcome'): DecisionOutcome {
  return decodeEnumValue(value, path, decisionOutcomes);
}

export function decodeInspectionOutcome(
  execution: unknown,
  decision: unknown,
  path = '$'
): InspectionOutcome {
  return Object.freeze({
    execution: decodeExecutionOutcome(execution, `${path}.executionOutcome`),
    decision: decodeDecisionOutcome(decision, `${path}.decisionOutcome`)
  });
}

export function classifyInspectionOutcome(
  outcome: InspectionOutcome
): CanonicalInspectionOutcomeKind {
  switch (outcome.execution) {
    case 'Failed':
      return 'Failed';
    case 'Cancelled':
      return 'Cancelled';
    case 'TimedOut':
      return 'TimedOut';
    case 'Skipped':
      return 'Skipped';
    case 'Succeeded':
      return outcome.decision;
  }
}

export function formatInspectionOutcome(
  outcome: InspectionOutcome
): InspectionOutcomePresentation {
  const kind = classifyInspectionOutcome(outcome);
  const presentation = kindPresentation[kind];
  return Object.freeze({
    ...outcome,
    kind,
    label: presentation.label,
    executionLabel: executionLabels[outcome.execution],
    decisionLabel: decisionLabels[outcome.decision],
    tone: presentation.tone
  });
}
