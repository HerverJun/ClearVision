import type { CanonicalFlowDraft } from '@/platform/canvas';
import type { WorkspaceJsonValue } from '../workspaceContracts';

export interface LineSequenceSuggestionV1 {
  readonly parameterName: string;
  readonly currentValue: unknown;
  readonly suggestedValue: unknown;
  readonly reason: string;
  readonly expectedImprovement: string;
}

export interface LineSequenceMissingResourceV1 {
  readonly resourceType: string;
  readonly resourceKey: string;
  readonly description: string;
  readonly diagnosticCode: string;
}

export interface LineSequenceAnalysisV1 {
  readonly success: boolean;
  readonly targetNodeId: string;
  readonly overallScore: number | null;
  readonly diagnosticCodes: readonly string[];
  readonly suggestions: readonly LineSequenceSuggestionV1[];
  readonly missingResources: readonly LineSequenceMissingResourceV1[];
  readonly errorMessage: string | null;
}

export interface LineSequenceRecommendationV1 {
  readonly success: boolean;
  readonly scenarioKey: string;
  readonly finalParameters: Readonly<Record<string, WorkspaceJsonValue>>;
  readonly totalIterations: number;
  readonly totalExecutionTimeMs: number;
  readonly isGoalAchieved: boolean;
  readonly diagnosticCodes: readonly string[];
  readonly missingResources: readonly LineSequenceMissingResourceV1[];
  readonly errorMessage: string | null;
}

export interface LineSequenceParameterPatchV1 {
  readonly nodeId: string;
  readonly operatorType: 'BoxNms' | 'DeepLearning';
  readonly values: Readonly<Record<string, WorkspaceJsonValue>>;
}

export class LineSequenceContractDecodeError extends Error {
  constructor(readonly path: string, message: string) {
    super(`${path}: ${message}`);
    this.name = 'LineSequenceContractDecodeError';
  }
}

function record(value: unknown, path: string): Readonly<Record<string, unknown>> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new LineSequenceContractDecodeError(path, 'expected an object');
  }
  return value as Readonly<Record<string, unknown>>;
}

function text(value: unknown, path: string, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && !value.trim())) {
    throw new LineSequenceContractDecodeError(path, 'expected a string');
  }
  return value;
}

function optionalText(value: unknown, path: string): string | null {
  if (value === null || value === undefined || value === '') return null;
  return text(value, path, true);
}

function booleanValue(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') throw new LineSequenceContractDecodeError(path, 'expected a boolean');
  return value;
}

function numberValue(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new LineSequenceContractDecodeError(path, 'expected a finite number');
  }
  return value;
}

function stringArray(value: unknown, path: string): readonly string[] {
  if (!Array.isArray(value)) throw new LineSequenceContractDecodeError(path, 'expected an array');
  return Object.freeze(value.map((item, index) => text(item, `${path}[${index}]`, true)));
}

function decodeSuggestion(value: unknown, path: string): LineSequenceSuggestionV1 {
  const source = record(value, path);
  return Object.freeze({
    parameterName: text(source.parameterName, `${path}.parameterName`),
    currentValue: source.currentValue,
    suggestedValue: source.suggestedValue,
    reason: text(source.reason ?? '', `${path}.reason`, true),
    expectedImprovement: text(source.expectedImprovement ?? '', `${path}.expectedImprovement`, true)
  });
}

function decodeMissingResource(value: unknown, path: string): LineSequenceMissingResourceV1 {
  const source = record(value, path);
  return Object.freeze({
    resourceType: text(source.resourceType ?? '', `${path}.resourceType`, true),
    resourceKey: text(source.resourceKey ?? '', `${path}.resourceKey`, true),
    description: text(source.description ?? '', `${path}.description`, true),
    diagnosticCode: text(source.diagnosticCode ?? '', `${path}.diagnosticCode`, true)
  });
}

function decodeMissingResources(value: unknown, path: string): readonly LineSequenceMissingResourceV1[] {
  if (!Array.isArray(value)) throw new LineSequenceContractDecodeError(path, 'expected an array');
  return Object.freeze(value.map((item, index) => decodeMissingResource(item, `${path}[${index}]`)));
}

export function decodeLineSequenceAnalysisV1(value: unknown): LineSequenceAnalysisV1 {
  const source = record(value, '$');
  const suggestions = source.suggestions;
  if (!Array.isArray(suggestions)) throw new LineSequenceContractDecodeError('$.suggestions', 'expected an array');
  const metrics = source.metrics === null || source.metrics === undefined ? null : record(source.metrics, '$.metrics');
  return Object.freeze({
    success: booleanValue(source.success, '$.success'),
    targetNodeId: text(source.targetNodeId, '$.targetNodeId'),
    overallScore: metrics?.overallScore === null || metrics?.overallScore === undefined
      ? null
      : numberValue(metrics.overallScore, '$.metrics.overallScore'),
    diagnosticCodes: stringArray(source.diagnosticCodes, '$.diagnosticCodes'),
    suggestions: Object.freeze(suggestions.map((item, index) => decodeSuggestion(item, `$.suggestions[${index}]`))),
    missingResources: decodeMissingResources(source.missingResources, '$.missingResources'),
    errorMessage: optionalText(source.errorMessage, '$.errorMessage')
  });
}

function decodeFinalParameters(value: unknown): Readonly<Record<string, WorkspaceJsonValue>> {
  const source = record(value, '$.finalParameters');
  const result: Record<string, WorkspaceJsonValue> = {};
  for (const [key, raw] of Object.entries(source)) {
    if (typeof raw === 'number' && Number.isFinite(raw)) result[key] = raw;
  }
  return Object.freeze(result);
}

export function decodeLineSequenceRecommendationV1(value: unknown): LineSequenceRecommendationV1 {
  const source = record(value, '$');
  return Object.freeze({
    success: booleanValue(source.success, '$.success'),
    scenarioKey: text(source.scenarioKey, '$.scenarioKey'),
    finalParameters: decodeFinalParameters(source.finalParameters),
    totalIterations: numberValue(source.totalIterations, '$.totalIterations'),
    totalExecutionTimeMs: numberValue(source.totalExecutionTimeMs, '$.totalExecutionTimeMs'),
    isGoalAchieved: booleanValue(source.isGoalAchieved, '$.isGoalAchieved'),
    diagnosticCodes: stringArray(source.diagnosticCodes, '$.diagnosticCodes'),
    missingResources: decodeMissingResources(source.missingResources, '$.missingResources'),
    errorMessage: optionalText(source.errorMessage, '$.errorMessage')
  });
}

function property(source: Readonly<Record<string, unknown>>, camel: string, pascal: string): unknown {
  return source[camel] ?? source[pascal];
}

function operatorId(operator: Readonly<Record<string, unknown>>): string {
  const value = property(operator, 'id', 'Id');
  return typeof value === 'string' ? value : '';
}

function operatorType(operator: Readonly<Record<string, unknown>>): string {
  const value = property(operator, 'type', 'Type');
  return typeof value === 'string' || typeof value === 'number'
    ? String(value).toLocaleLowerCase()
    : '';
}

function upstreamDistances(flow: CanonicalFlowDraft, targetNodeId: string): ReadonlyMap<string, number> {
  const sourcesByTarget = new Map<string, string[]>();
  for (const connection of flow.connections) {
    const target = property(connection, 'targetOperatorId', 'TargetOperatorId')
      ?? property(connection, 'target', 'Target');
    const source = property(connection, 'sourceOperatorId', 'SourceOperatorId')
      ?? property(connection, 'source', 'Source');
    if (typeof target !== 'string' || typeof source !== 'string') continue;
    const existing = sourcesByTarget.get(target) ?? [];
    existing.push(source);
    sourcesByTarget.set(target, existing);
  }

  const distances = new Map<string, number>([[targetNodeId, 0]]);
  const queue = [targetNodeId];
  while (queue.length > 0) {
    const current = queue.shift()!;
    const nextDistance = (distances.get(current) ?? 0) + 1;
    for (const source of sourcesByTarget.get(current) ?? []) {
      if (distances.has(source)) continue;
      distances.set(source, nextDistance);
      queue.push(source);
    }
  }
  return distances;
}

function numericParameter(
  parameters: Readonly<Record<string, WorkspaceJsonValue>>,
  key: string
): number | null {
  const value = parameters[key];
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1
    ? value
    : null;
}

export function resolveLineSequenceParameterPatch(
  flow: CanonicalFlowDraft,
  targetNodeId: string,
  parameters: Readonly<Record<string, WorkspaceJsonValue>>
): LineSequenceParameterPatchV1 | null {
  const distances = upstreamDistances(flow, targetNodeId);
  const nearest = (type: string, numericType: number) => flow.operators
    .map((operator, index) => ({ operator, index, id: operatorId(operator) }))
    .filter(item => item.id &&
      [type.toLocaleLowerCase(), String(numericType)].includes(operatorType(item.operator)) &&
      distances.has(item.id))
    .sort((left, right) => (distances.get(left.id)! - distances.get(right.id)!) || (left.index - right.index))[0];

  const boxNms = nearest('BoxNms', 140);
  if (boxNms) {
    const values: Record<string, WorkspaceJsonValue> = {};
    const score = numericParameter(parameters, 'BoxNms.ScoreThreshold');
    const iou = numericParameter(parameters, 'BoxNms.IouThreshold');
    if (score !== null) values.ScoreThreshold = score;
    if (iou !== null) values.IouThreshold = iou;
    if (Object.keys(values).length > 0) {
      return Object.freeze({ nodeId: boxNms.id, operatorType: 'BoxNms', values: Object.freeze(values) });
    }
  }

  const deepLearning = nearest('DeepLearning', 10);
  const confidence = numericParameter(parameters, 'DeepLearning.Confidence');
  return deepLearning && confidence !== null
    ? Object.freeze({
        nodeId: deepLearning.id,
        operatorType: 'DeepLearning',
        values: Object.freeze({ Confidence: confidence })
      })
    : null;
}
