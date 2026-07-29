import {
  AiContractDecodeError,
  type AiAgentRunEventV1,
  type AiAgentRunReplayV1,
  type AiAgentRunSummaryV1,
  type AiBuildBlockerV1,
  type AiBuildReadinessV1,
  type AiClarificationOptionV1,
  type AiClarificationQuestionV1,
  type AiDefaultAssumptionV1,
  type AiIntentResultV1,
  type AiOperationKind,
  type AiOperationProjectionV1,
  type AiOperationStatus,
  type AiPlanAnswerV1,
  type AiPlanPublicEventV1,
  type AiPlanRunResponseV1,
  type AiPlanV1,
  type AiProjectBaselineV1,
  type AiProjectContextV1,
  type AiReadinessPreviewV1,
  type AiRecommendedRouteV1,
  type AiRequirementMode,
  type AiResourceRequirementV1,
  type AiRunStatus,
  type AiSemanticExtractionV1,
  type AiSessionCreateResponseV1,
  type AiSessionDetailV1,
  type AiSessionSnapshotV1
} from './contracts';

type JsonRecord = Record<string, unknown>;

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const sessionPattern = /^[a-z0-9_-]{1,80}$/i;
const hashPattern = /^(?:sha256:)?[0-9a-f]{64}$/i;
const tokenPattern = /^[a-z0-9_.:-]{1,128}$/i;
const operationKinds = new Set<AiOperationKind>(['session_create', 'session_delete', 'plan_run', 'build_run']);
const operationStatuses = new Set<AiOperationStatus>(['pending', 'created', 'failed', 'rejected']);
const runStatuses = new Set<AiRunStatus>([
  'pending', 'running', 'completed', 'failed', 'cancelled', 'blocked', 'warning'
]);

function record(value: unknown, path: string): JsonRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new AiContractDecodeError(path, 'an object');
  }
  return value as JsonRecord;
}

function optionalRecord(value: unknown, path: string): JsonRecord | null {
  return value === null || value === undefined ? null : record(value, path);
}

function exact(source: JsonRecord, allowed: readonly string[], path: string): void {
  const allowedKeys = new Set(allowed);
  const unexpected = Object.keys(source).find(key => !allowedKeys.has(key));
  if (unexpected) throw new AiContractDecodeError(`${path}.${unexpected}`, 'absent from the public DTO');
}

function required(source: JsonRecord, key: string, path: string): unknown {
  if (!(key in source)) throw new AiContractDecodeError(`${path}.${key}`, 'present');
  return source[key];
}

function string(value: unknown, path: string, pattern?: RegExp): string {
  if (typeof value !== 'string' || (pattern && !pattern.test(value))) {
    throw new AiContractDecodeError(path, pattern ? 'a valid public identifier' : 'a string');
  }
  return value;
}

function nullableString(value: unknown, path: string, pattern?: RegExp): string | null {
  return value === null ? null : string(value, path, pattern);
}

function boolean(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') throw new AiContractDecodeError(path, 'a boolean');
  return value;
}

function integer(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new AiContractDecodeError(path, 'a non-negative safe integer');
  }
  return value;
}

function signedInteger(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value)) {
    throw new AiContractDecodeError(path, 'a safe integer');
  }
  return value;
}

function finite(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new AiContractDecodeError(path, 'a finite number');
  }
  return value;
}

function timestamp(value: unknown, path: string): string {
  const decoded = string(value, path);
  if (!Number.isFinite(Date.parse(decoded))) throw new AiContractDecodeError(path, 'an ISO timestamp');
  return decoded;
}

function strings(value: unknown, path: string): readonly string[] {
  if (!Array.isArray(value)) throw new AiContractDecodeError(path, 'an array of strings');
  return Object.freeze(value.map((item, index) => string(item, `${path}[${index}]`)));
}

function stringMap(value: unknown, path: string): Readonly<Record<string, string>> {
  const source = record(value, path);
  const result: Record<string, string> = {};
  for (const [key, item] of Object.entries(source)) {
    if (!tokenPattern.test(key)) throw new AiContractDecodeError(`${path}.${key}`, 'a canonical field key');
    result[key] = string(item, `${path}.${key}`);
  }
  return Object.freeze(result);
}

function requirementMode(value: unknown, path: string): AiRequirementMode {
  const decoded = string(value, path);
  if (decoded !== 'strict' && decoded !== 'draft') {
    throw new AiContractDecodeError(path, 'strict or draft');
  }
  return decoded;
}

function baseline(value: unknown, path: string): AiProjectBaselineV1 | null {
  if (value === null) return null;
  const source = record(value, path);
  exact(source, ['targetKind', 'projectId', 'persistenceRevision', 'canonicalFlowHash'], path);
  const targetKind = string(required(source, 'targetKind', path), `${path}.targetKind`);
  if (targetKind !== 'new' && targetKind !== 'existing') {
    throw new AiContractDecodeError(`${path}.targetKind`, 'new or existing');
  }
  const projectId = nullableString(required(source, 'projectId', path), `${path}.projectId`, guidPattern);
  const revisionValue = required(source, 'persistenceRevision', path);
  const persistenceRevision = revisionValue === null ? null : integer(revisionValue, `${path}.persistenceRevision`);
  const canonicalFlowHash = string(required(source, 'canonicalFlowHash', path), `${path}.canonicalFlowHash`);
  if (targetKind === 'new' && (projectId !== null || persistenceRevision !== null || canonicalFlowHash !== '')) {
    throw new AiContractDecodeError(path, 'a baseline-free new target');
  }
  if (targetKind === 'existing' &&
      (projectId === null || persistenceRevision === null || !hashPattern.test(canonicalFlowHash))) {
    throw new AiContractDecodeError(path, 'a complete existing Project baseline');
  }
  return Object.freeze({ targetKind, projectId, persistenceRevision, canonicalFlowHash });
}

function decodePlanAnswer(value: unknown, path: string): AiPlanAnswerV1 {
  const source = record(value, path);
  exact(source, ['questionId', 'field', 'value', 'origin', 'confidence', 'resolved'], path);
  return Object.freeze({
    questionId: string(required(source, 'questionId', path), `${path}.questionId`),
    field: string(required(source, 'field', path), `${path}.field`, tokenPattern),
    value: string(required(source, 'value', path), `${path}.value`),
    origin: string(required(source, 'origin', path), `${path}.origin`, tokenPattern),
    confidence: finite(required(source, 'confidence', path), `${path}.confidence`),
    resolved: boolean(required(source, 'resolved', path), `${path}.resolved`)
  });
}

function planAnswers(value: unknown, path: string): readonly AiPlanAnswerV1[] {
  if (!Array.isArray(value)) throw new AiContractDecodeError(path, 'an array of Plan answers');
  return Object.freeze(value.map((item, index) => decodePlanAnswer(item, `${path}[${index}]`)));
}

function decodeResource(value: unknown, path: string): AiResourceRequirementV1 {
  const source = record(value, path);
  exact(source, [
    'canonicalId', 'resourceType', 'resourceName', 'resourceKey', 'operatorKey', 'operatorId',
    'operatorType', 'operatorIndex', 'parameterName', 'status', 'blockingScope', 'source',
    'resolutionTarget', 'draftPolicy', 'description', 'aliases'
  ], path);
  signedInteger(required(source, 'operatorIndex', path), `${path}.operatorIndex`);
  strings(required(source, 'aliases', path), `${path}.aliases`);
  return Object.freeze({
    canonicalId: string(required(source, 'canonicalId', path), `${path}.canonicalId`),
    resourceType: string(required(source, 'resourceType', path), `${path}.resourceType`),
    resourceName: string(required(source, 'resourceName', path), `${path}.resourceName`),
    status: string(required(source, 'status', path), `${path}.status`),
    blockingScope: string(required(source, 'blockingScope', path), `${path}.blockingScope`),
    resolutionTarget: string(required(source, 'resolutionTarget', path), `${path}.resolutionTarget`),
    draftPolicy: string(required(source, 'draftPolicy', path), `${path}.draftPolicy`),
    description: string(required(source, 'description', path), `${path}.description`)
  });
}

function resources(value: unknown, path: string): readonly AiResourceRequirementV1[] {
  if (!Array.isArray(value)) throw new AiContractDecodeError(path, 'an array of resources');
  return Object.freeze(value.map((item, index) => decodeResource(item, `${path}[${index}]`)));
}

function decodeBlocker(value: unknown, path: string): AiBuildBlockerV1 {
  const source = record(value, path);
  exact(source, [
    'id', 'category', 'field', 'questionId', 'blocksBuild', 'resolutionMode', 'publicLabel', 'resource'
  ], path);
  return Object.freeze({
    id: string(required(source, 'id', path), `${path}.id`),
    category: string(required(source, 'category', path), `${path}.category`),
    field: string(required(source, 'field', path), `${path}.field`),
    questionId: string(required(source, 'questionId', path), `${path}.questionId`),
    blocksBuild: boolean(required(source, 'blocksBuild', path), `${path}.blocksBuild`),
    resolutionMode: string(required(source, 'resolutionMode', path), `${path}.resolutionMode`),
    publicLabel: string(required(source, 'publicLabel', path), `${path}.publicLabel`),
    resource: source.resource === null ? null : decodeResource(required(source, 'resource', path), `${path}.resource`)
  });
}

export function decodeAiBuildReadinessV1(value: unknown, path = '$'): AiBuildReadinessV1 {
  const source = record(value, path);
  exact(source, [
    'canBuild', 'blockers', 'resolvedFields', 'remainingFields', 'primaryMessage',
    'contractVersion', 'missingResources'
  ], path);
  const blockersValue = required(source, 'blockers', path);
  if (!Array.isArray(blockersValue)) throw new AiContractDecodeError(`${path}.blockers`, 'an array');
  return Object.freeze({
    canBuild: boolean(required(source, 'canBuild', path), `${path}.canBuild`),
    blockers: Object.freeze(blockersValue.map((item, index) => decodeBlocker(item, `${path}.blockers[${index}]`))),
    resolvedFields: strings(required(source, 'resolvedFields', path), `${path}.resolvedFields`),
    remainingFields: strings(required(source, 'remainingFields', path), `${path}.remainingFields`),
    primaryMessage: string(required(source, 'primaryMessage', path), `${path}.primaryMessage`),
    contractVersion: string(required(source, 'contractVersion', path), `${path}.contractVersion`),
    missingResources: resources(required(source, 'missingResources', path), `${path}.missingResources`)
  });
}

export function decodeAiReadinessPreviewV1(value: unknown, path = '$'): AiReadinessPreviewV1 {
  const source = record(value, path);
  exact(source, [
    'planId', 'planHash', 'requirementMode', 'answerRevision', 'resourceRevision', 'acceptedAnswers',
    'answerSetFingerprint', 'buildReadiness', 'deferredQuestionIds', 'pendingConfirmationCount',
    'resourcePendingCount', 'hardBlockerCount', 'contractValid', 'failureCode', 'failureMessage', 'metadataOnly'
  ], path);
  if (required(source, 'metadataOnly', path) !== true) {
    throw new AiContractDecodeError(`${path}.metadataOnly`, 'true');
  }
  return Object.freeze({
    planId: string(required(source, 'planId', path), `${path}.planId`),
    planHash: string(required(source, 'planHash', path), `${path}.planHash`),
    requirementMode: requirementMode(required(source, 'requirementMode', path), `${path}.requirementMode`),
    answerRevision: integer(required(source, 'answerRevision', path), `${path}.answerRevision`),
    resourceRevision: integer(required(source, 'resourceRevision', path), `${path}.resourceRevision`),
    acceptedAnswers: planAnswers(required(source, 'acceptedAnswers', path), `${path}.acceptedAnswers`),
    answerSetFingerprint: string(required(source, 'answerSetFingerprint', path), `${path}.answerSetFingerprint`),
    buildReadiness: decodeAiBuildReadinessV1(required(source, 'buildReadiness', path), `${path}.buildReadiness`),
    deferredQuestionIds: strings(required(source, 'deferredQuestionIds', path), `${path}.deferredQuestionIds`),
    pendingConfirmationCount: integer(required(source, 'pendingConfirmationCount', path), `${path}.pendingConfirmationCount`),
    resourcePendingCount: integer(required(source, 'resourcePendingCount', path), `${path}.resourcePendingCount`),
    hardBlockerCount: integer(required(source, 'hardBlockerCount', path), `${path}.hardBlockerCount`),
    contractValid: boolean(required(source, 'contractValid', path), `${path}.contractValid`),
    failureCode: string(required(source, 'failureCode', path), `${path}.failureCode`),
    failureMessage: string(required(source, 'failureMessage', path), `${path}.failureMessage`),
    metadataOnly: true
  });
}

export function decodeAiSessionSnapshotV1(value: unknown, path = '$'): AiSessionSnapshotV1 {
  const source = record(value, path);
  exact(source, [
    'schemaVersion', 'revision', 'projectId', 'lifecycleState', 'planRunId', 'planRunStatus',
    'buildRunId', 'buildRunStatus', 'buildClientOperationId', 'projectBaseline', 'requirementMode',
    'planQuestionSelections', 'confirmedPlanAnswers', 'optimisticPlanAnswers', 'answerRevision',
    'readinessPreview', 'planAcceptedRecommendedDefaults', 'planTerminalSequence', 'updatedAtUtc'
  ], path);
  const terminalSequence = required(source, 'planTerminalSequence', path);
  return Object.freeze({
    schemaVersion: integer(required(source, 'schemaVersion', path), `${path}.schemaVersion`),
    revision: integer(required(source, 'revision', path), `${path}.revision`),
    projectId: nullableString(required(source, 'projectId', path), `${path}.projectId`, guidPattern),
    lifecycleState: string(required(source, 'lifecycleState', path), `${path}.lifecycleState`, tokenPattern),
    planRunId: nullableString(required(source, 'planRunId', path), `${path}.planRunId`, tokenPattern),
    planRunStatus: nullableString(required(source, 'planRunStatus', path), `${path}.planRunStatus`, tokenPattern),
    buildRunId: nullableString(required(source, 'buildRunId', path), `${path}.buildRunId`, tokenPattern),
    buildRunStatus: nullableString(required(source, 'buildRunStatus', path), `${path}.buildRunStatus`, tokenPattern),
    buildClientOperationId: nullableString(
      required(source, 'buildClientOperationId', path), `${path}.buildClientOperationId`, guidPattern
    ),
    projectBaseline: baseline(required(source, 'projectBaseline', path), `${path}.projectBaseline`),
    requirementMode: requirementMode(required(source, 'requirementMode', path), `${path}.requirementMode`),
    planQuestionSelections: stringMap(required(source, 'planQuestionSelections', path), `${path}.planQuestionSelections`),
    confirmedPlanAnswers: planAnswers(required(source, 'confirmedPlanAnswers', path), `${path}.confirmedPlanAnswers`),
    optimisticPlanAnswers: planAnswers(required(source, 'optimisticPlanAnswers', path), `${path}.optimisticPlanAnswers`),
    answerRevision: integer(required(source, 'answerRevision', path), `${path}.answerRevision`),
    readinessPreview: source.readinessPreview === null
      ? null
      : decodeAiReadinessPreviewV1(required(source, 'readinessPreview', path), `${path}.readinessPreview`),
    planAcceptedRecommendedDefaults: boolean(
      required(source, 'planAcceptedRecommendedDefaults', path), `${path}.planAcceptedRecommendedDefaults`
    ),
    planTerminalSequence: terminalSequence === null ? null : integer(terminalSequence, `${path}.planTerminalSequence`),
    updatedAtUtc: timestamp(required(source, 'updatedAtUtc', path), `${path}.updatedAtUtc`)
  });
}

export function decodeAiSessionDetailV1(value: unknown, path = '$'): AiSessionDetailV1 {
  const source = record(value, path);
  exact(source, ['sessionId', 'snapshot', 'updatedAtUtc'], path);
  return Object.freeze({
    sessionId: string(required(source, 'sessionId', path), `${path}.sessionId`, sessionPattern),
    snapshot: decodeAiSessionSnapshotV1(required(source, 'snapshot', path), `${path}.snapshot`),
    updatedAtUtc: timestamp(required(source, 'updatedAtUtc', path), `${path}.updatedAtUtc`)
  });
}

export function decodeAiOperationProjectionV1(value: unknown, path = '$'): AiOperationProjectionV1 {
  const source = record(value, path);
  exact(source, [
    'clientOperationId', 'kind', 'status', 'sessionId', 'runId', 'payloadFingerprint',
    'projectBaseline', 'errorCode', 'publicMessage', 'createdAtUtc', 'updatedAtUtc', 'expiresAtUtc'
  ], path);
  const kind = string(required(source, 'kind', path), `${path}.kind`) as AiOperationKind;
  const status = string(required(source, 'status', path), `${path}.status`) as AiOperationStatus;
  if (!operationKinds.has(kind)) throw new AiContractDecodeError(`${path}.kind`, 'a supported operation kind');
  if (!operationStatuses.has(status)) throw new AiContractDecodeError(`${path}.status`, 'a supported operation status');
  return Object.freeze({
    clientOperationId: string(required(source, 'clientOperationId', path), `${path}.clientOperationId`, guidPattern),
    kind,
    status,
    sessionId: nullableString(required(source, 'sessionId', path), `${path}.sessionId`, sessionPattern),
    runId: nullableString(required(source, 'runId', path), `${path}.runId`, tokenPattern),
    payloadFingerprint: string(required(source, 'payloadFingerprint', path), `${path}.payloadFingerprint`, hashPattern),
    projectBaseline: baseline(required(source, 'projectBaseline', path), `${path}.projectBaseline`),
    errorCode: nullableString(required(source, 'errorCode', path), `${path}.errorCode`, tokenPattern),
    publicMessage: nullableString(required(source, 'publicMessage', path), `${path}.publicMessage`),
    createdAtUtc: timestamp(required(source, 'createdAtUtc', path), `${path}.createdAtUtc`),
    updatedAtUtc: timestamp(required(source, 'updatedAtUtc', path), `${path}.updatedAtUtc`),
    expiresAtUtc: timestamp(required(source, 'expiresAtUtc', path), `${path}.expiresAtUtc`)
  });
}

export function decodeAiSessionCreateResponseV1(value: unknown): AiSessionCreateResponseV1 {
  const source = record(value, '$');
  exact(source, ['operation', 'session'], '$');
  return Object.freeze({
    operation: decodeAiOperationProjectionV1(required(source, 'operation', '$'), '$.operation'),
    session: source.session === null ? null : decodeAiSessionDetailV1(required(source, 'session', '$'), '$.session')
  });
}

function decodeSemantic(value: unknown, path: string): AiSemanticExtractionV1 | null {
  if (value === null) return null;
  const source = record(value, path);
  exact(source, [
    'isVisionRequest', 'intent', 'taskType', 'confidence', 'taskTypeConfidence', 'inspectionObject',
    'targetAttribute', 'defectType', 'measurementTarget', 'imageSource', 'okCondition', 'ngCondition',
    'outputTarget', 'suggestedRoute', 'canPlanCandidate', 'canBuildCandidate', 'objectSignals',
    'taskSignals', 'missingFields', 'clarificationQuestions', 'source', 'failureCode',
    'sanitizedErrorMessage', 'metadataOnly'
  ], path);
  strings(required(source, 'objectSignals', path), `${path}.objectSignals`);
  strings(required(source, 'taskSignals', path), `${path}.taskSignals`);
  strings(required(source, 'clarificationQuestions', path), `${path}.clarificationQuestions`);
  if (required(source, 'metadataOnly', path) !== true) throw new AiContractDecodeError(`${path}.metadataOnly`, 'true');
  return Object.freeze({
    isVisionRequest: boolean(required(source, 'isVisionRequest', path), `${path}.isVisionRequest`),
    intent: string(required(source, 'intent', path), `${path}.intent`),
    taskType: string(required(source, 'taskType', path), `${path}.taskType`),
    confidence: finite(required(source, 'confidence', path), `${path}.confidence`),
    taskTypeConfidence: finite(required(source, 'taskTypeConfidence', path), `${path}.taskTypeConfidence`),
    inspectionObject: string(required(source, 'inspectionObject', path), `${path}.inspectionObject`),
    targetAttribute: string(required(source, 'targetAttribute', path), `${path}.targetAttribute`),
    defectType: string(required(source, 'defectType', path), `${path}.defectType`),
    measurementTarget: string(required(source, 'measurementTarget', path), `${path}.measurementTarget`),
    imageSource: string(required(source, 'imageSource', path), `${path}.imageSource`),
    okCondition: string(required(source, 'okCondition', path), `${path}.okCondition`),
    ngCondition: string(required(source, 'ngCondition', path), `${path}.ngCondition`),
    outputTarget: string(required(source, 'outputTarget', path), `${path}.outputTarget`),
    suggestedRoute: string(required(source, 'suggestedRoute', path), `${path}.suggestedRoute`),
    canPlanCandidate: boolean(required(source, 'canPlanCandidate', path), `${path}.canPlanCandidate`),
    canBuildCandidate: boolean(required(source, 'canBuildCandidate', path), `${path}.canBuildCandidate`),
    missingFields: strings(required(source, 'missingFields', path), `${path}.missingFields`),
    source: string(required(source, 'source', path), `${path}.source`),
    failureCode: string(required(source, 'failureCode', path), `${path}.failureCode`),
    sanitizedErrorMessage: string(required(source, 'sanitizedErrorMessage', path), `${path}.sanitizedErrorMessage`)
  });
}

function validateOptionalPublicRecord(value: unknown, path: string): void {
  const source = optionalRecord(value, path);
  if (source && source.metadataOnly !== true) throw new AiContractDecodeError(`${path}.metadataOnly`, 'true');
}

export function decodeAiIntentResultV1(value: unknown, path = '$'): AiIntentResultV1 {
  const source = record(value, path);
  exact(source, [
    'intent', 'confidence', 'shouldOpenPlan', 'shouldBuildDirectly', 'canBuild', 'needsClarification',
    'publicReason', 'assistantReply', 'fallbackAllowed', 'routerSource', 'fallbackReason',
    'semanticExtraction', 'requirementMaturity', 'decisionTrace', 'shouldMergeIntoPendingPlan',
    'shouldResetPendingPlan', 'planAnswerUpdates', 'resolvedPlanFields', 'remainingPlanFields', 'metadataOnly'
  ], path);
  validateOptionalPublicRecord(source.requirementMaturity, `${path}.requirementMaturity`);
  validateOptionalPublicRecord(source.decisionTrace, `${path}.decisionTrace`);
  if (required(source, 'metadataOnly', path) !== true) throw new AiContractDecodeError(`${path}.metadataOnly`, 'true');
  return Object.freeze({
    intent: string(required(source, 'intent', path), `${path}.intent`),
    confidence: string(required(source, 'confidence', path), `${path}.confidence`),
    shouldOpenPlan: boolean(required(source, 'shouldOpenPlan', path), `${path}.shouldOpenPlan`),
    shouldBuildDirectly: boolean(required(source, 'shouldBuildDirectly', path), `${path}.shouldBuildDirectly`),
    canBuild: boolean(required(source, 'canBuild', path), `${path}.canBuild`),
    needsClarification: boolean(required(source, 'needsClarification', path), `${path}.needsClarification`),
    publicReason: string(required(source, 'publicReason', path), `${path}.publicReason`),
    assistantReply: string(required(source, 'assistantReply', path), `${path}.assistantReply`),
    fallbackAllowed: boolean(required(source, 'fallbackAllowed', path), `${path}.fallbackAllowed`),
    routerSource: string(required(source, 'routerSource', path), `${path}.routerSource`),
    fallbackReason: string(required(source, 'fallbackReason', path), `${path}.fallbackReason`),
    semanticExtraction: decodeSemantic(source.semanticExtraction, `${path}.semanticExtraction`),
    shouldMergeIntoPendingPlan: boolean(
      required(source, 'shouldMergeIntoPendingPlan', path), `${path}.shouldMergeIntoPendingPlan`
    ),
    shouldResetPendingPlan: boolean(
      required(source, 'shouldResetPendingPlan', path), `${path}.shouldResetPendingPlan`
    ),
    planAnswerUpdates: planAnswers(required(source, 'planAnswerUpdates', path), `${path}.planAnswerUpdates`),
    resolvedPlanFields: strings(required(source, 'resolvedPlanFields', path), `${path}.resolvedPlanFields`),
    remainingPlanFields: strings(required(source, 'remainingPlanFields', path), `${path}.remainingPlanFields`)
  });
}

function decodeRoute(value: unknown, path: string): AiRecommendedRouteV1 {
  const source = record(value, path);
  exact(source, ['routeId', 'title', 'summary', 'operators', 'templateDecision'], path);
  return Object.freeze({
    routeId: string(required(source, 'routeId', path), `${path}.routeId`),
    title: string(required(source, 'title', path), `${path}.title`),
    summary: string(required(source, 'summary', path), `${path}.summary`),
    operators: strings(required(source, 'operators', path), `${path}.operators`),
    templateDecision: string(required(source, 'templateDecision', path), `${path}.templateDecision`)
  });
}

function decodeOption(value: unknown, path: string): AiClarificationOptionV1 {
  const source = record(value, path);
  exact(source, [
    'value', 'label', 'recommended', 'answerEffect', 'recommendationReason', 'description', 'impact'
  ], path);
  return Object.freeze({
    value: string(required(source, 'value', path), `${path}.value`),
    label: string(required(source, 'label', path), `${path}.label`),
    recommended: boolean(required(source, 'recommended', path), `${path}.recommended`),
    answerEffect: string(required(source, 'answerEffect', path), `${path}.answerEffect`),
    recommendationReason: string(required(source, 'recommendationReason', path), `${path}.recommendationReason`),
    description: string(required(source, 'description', path), `${path}.description`),
    impact: string(required(source, 'impact', path), `${path}.impact`)
  });
}

function decodeQuestion(value: unknown, path: string): AiClarificationQuestionV1 {
  const source = record(value, path);
  exact(source, ['id', 'field', 'title', 'why', 'defaultValue', 'defaultAssumption', 'impact', 'options'], path);
  const options = required(source, 'options', path);
  if (!Array.isArray(options)) throw new AiContractDecodeError(`${path}.options`, 'an array');
  return Object.freeze({
    id: string(required(source, 'id', path), `${path}.id`),
    field: string(required(source, 'field', path), `${path}.field`, tokenPattern),
    title: string(required(source, 'title', path), `${path}.title`),
    why: string(required(source, 'why', path), `${path}.why`),
    defaultValue: string(required(source, 'defaultValue', path), `${path}.defaultValue`),
    defaultAssumption: string(required(source, 'defaultAssumption', path), `${path}.defaultAssumption`),
    impact: string(required(source, 'impact', path), `${path}.impact`),
    options: Object.freeze(options.map((item, index) => decodeOption(item, `${path}.options[${index}]`)))
  });
}

function decodeDefault(value: unknown, path: string): AiDefaultAssumptionV1 {
  const source = record(value, path);
  exact(source, ['id', 'label', 'value', 'impact'], path);
  return Object.freeze({
    id: string(required(source, 'id', path), `${path}.id`),
    label: string(required(source, 'label', path), `${path}.label`),
    value: string(required(source, 'value', path), `${path}.value`),
    impact: string(required(source, 'impact', path), `${path}.impact`)
  });
}

function decodePublicEvent(value: unknown, path: string): AiPlanPublicEventV1 {
  const source = record(value, path);
  exact(source, ['stage', 'status', 'title', 'summary', 'metadata', 'metadataOnly'], path);
  if (required(source, 'metadataOnly', path) !== true) throw new AiContractDecodeError(`${path}.metadataOnly`, 'true');
  return Object.freeze({
    stage: string(required(source, 'stage', path), `${path}.stage`),
    status: string(required(source, 'status', path), `${path}.status`),
    title: string(required(source, 'title', path), `${path}.title`),
    summary: string(required(source, 'summary', path), `${path}.summary`),
    metadata: stringMap(required(source, 'metadata', path), `${path}.metadata`)
  });
}

function validatePlanContext(value: unknown, path: string): void {
  const source = record(value, path);
  exact(source, [
    'hasCurrentFlow', 'hasCurrentResult', 'attachmentCount', 'templateSelectionMode', 'templateId',
    'contextKinds', 'operatorCatalogTools'
  ], path);
  boolean(required(source, 'hasCurrentFlow', path), `${path}.hasCurrentFlow`);
  boolean(required(source, 'hasCurrentResult', path), `${path}.hasCurrentResult`);
  integer(required(source, 'attachmentCount', path), `${path}.attachmentCount`);
  string(required(source, 'templateSelectionMode', path), `${path}.templateSelectionMode`);
  string(required(source, 'templateId', path), `${path}.templateId`);
  strings(required(source, 'contextKinds', path), `${path}.contextKinds`);
  strings(required(source, 'operatorCatalogTools', path), `${path}.operatorCatalogTools`);
}

export function decodeAiPlanV1(value: unknown, path = '$'): AiPlanV1 {
  const source = record(value, path);
  exact(source, [
    'planContractVersion', 'planId', 'planHash', 'planSource', 'currentPhase', 'fallbackReason',
    'plannerFailureStage', 'plannerFailureCode', 'sanitizedErrorKind', 'sanitizedErrorMessage',
    'originalUserPrompt', 'goal', 'intent', 'confidence', 'requirementUnderstanding',
    'confirmedPlanAnswers', 'resolvedPlanFields', 'remainingPlanFields', 'recommendedRoute',
    'clarificationQuestions', 'recommendedDefaults', 'risks', 'acceptanceCriteria', 'executablePlan',
    'canBuild', 'blockingReasons', 'buildReadiness', 'semanticExtraction', 'requirementMaturity',
    'decisionTrace', 'nextAction', 'contextSummary', 'operatorCatalogVersion', 'templateCatalogVersion',
    'templateSelection', 'stationBoundarySummary', 'plcOutputPolicy', 'planWarnings',
    'contractRepairNotes', 'publicEvents', 'metadataOnly'
  ], path);
  const questions = required(source, 'clarificationQuestions', path);
  const defaults = required(source, 'recommendedDefaults', path);
  const publicEvents = required(source, 'publicEvents', path);
  if (!Array.isArray(questions) || !Array.isArray(defaults) || !Array.isArray(publicEvents)) {
    throw new AiContractDecodeError(path, 'a Plan with array fields');
  }
  validateOptionalPublicRecord(source.requirementMaturity, `${path}.requirementMaturity`);
  if (source.decisionTrace !== null) throw new AiContractDecodeError(`${path}.decisionTrace`, 'null in replay-safe Plan');
  if (source.templateSelection !== null) record(source.templateSelection, `${path}.templateSelection`);
  validatePlanContext(required(source, 'contextSummary', path), `${path}.contextSummary`);
  strings(required(source, 'contractRepairNotes', path), `${path}.contractRepairNotes`);
  if (required(source, 'metadataOnly', path) !== true) throw new AiContractDecodeError(`${path}.metadataOnly`, 'true');
  return Object.freeze({
    planContractVersion: string(required(source, 'planContractVersion', path), `${path}.planContractVersion`),
    planId: string(required(source, 'planId', path), `${path}.planId`),
    planHash: string(required(source, 'planHash', path), `${path}.planHash`),
    planSource: string(required(source, 'planSource', path), `${path}.planSource`),
    currentPhase: string(required(source, 'currentPhase', path), `${path}.currentPhase`),
    fallbackReason: string(required(source, 'fallbackReason', path), `${path}.fallbackReason`),
    plannerFailureStage: string(required(source, 'plannerFailureStage', path), `${path}.plannerFailureStage`),
    plannerFailureCode: string(required(source, 'plannerFailureCode', path), `${path}.plannerFailureCode`),
    sanitizedErrorKind: string(required(source, 'sanitizedErrorKind', path), `${path}.sanitizedErrorKind`),
    sanitizedErrorMessage: string(required(source, 'sanitizedErrorMessage', path), `${path}.sanitizedErrorMessage`),
    originalUserPrompt: string(required(source, 'originalUserPrompt', path), `${path}.originalUserPrompt`),
    goal: string(required(source, 'goal', path), `${path}.goal`),
    intent: string(required(source, 'intent', path), `${path}.intent`),
    confidence: string(required(source, 'confidence', path), `${path}.confidence`),
    requirementUnderstanding: strings(
      required(source, 'requirementUnderstanding', path), `${path}.requirementUnderstanding`
    ),
    confirmedPlanAnswers: planAnswers(
      required(source, 'confirmedPlanAnswers', path), `${path}.confirmedPlanAnswers`
    ),
    resolvedPlanFields: strings(required(source, 'resolvedPlanFields', path), `${path}.resolvedPlanFields`),
    remainingPlanFields: strings(required(source, 'remainingPlanFields', path), `${path}.remainingPlanFields`),
    recommendedRoute: decodeRoute(required(source, 'recommendedRoute', path), `${path}.recommendedRoute`),
    clarificationQuestions: Object.freeze(
      questions.map((item, index) => decodeQuestion(item, `${path}.clarificationQuestions[${index}]`))
    ),
    recommendedDefaults: Object.freeze(
      defaults.map((item, index) => decodeDefault(item, `${path}.recommendedDefaults[${index}]`))
    ),
    risks: strings(required(source, 'risks', path), `${path}.risks`),
    acceptanceCriteria: strings(required(source, 'acceptanceCriteria', path), `${path}.acceptanceCriteria`),
    executablePlan: strings(required(source, 'executablePlan', path), `${path}.executablePlan`),
    canBuild: boolean(required(source, 'canBuild', path), `${path}.canBuild`),
    blockingReasons: strings(required(source, 'blockingReasons', path), `${path}.blockingReasons`),
    buildReadiness: decodeAiBuildReadinessV1(required(source, 'buildReadiness', path), `${path}.buildReadiness`),
    semanticExtraction: decodeSemantic(source.semanticExtraction, `${path}.semanticExtraction`),
    nextAction: string(required(source, 'nextAction', path), `${path}.nextAction`),
    operatorCatalogVersion: string(required(source, 'operatorCatalogVersion', path), `${path}.operatorCatalogVersion`),
    templateCatalogVersion: string(required(source, 'templateCatalogVersion', path), `${path}.templateCatalogVersion`),
    stationBoundarySummary: string(required(source, 'stationBoundarySummary', path), `${path}.stationBoundarySummary`),
    plcOutputPolicy: string(required(source, 'plcOutputPolicy', path), `${path}.plcOutputPolicy`),
    planWarnings: strings(required(source, 'planWarnings', path), `${path}.planWarnings`),
    publicEvents: Object.freeze(
      publicEvents.map((item, index) => decodePublicEvent(item, `${path}.publicEvents[${index}]`))
    )
  });
}

function eventStatus(value: unknown, path: string): AiRunStatus {
  const decoded = string(value, path) as AiRunStatus;
  if (!runStatuses.has(decoded)) throw new AiContractDecodeError(path, 'a public AgentRun status');
  return decoded;
}

function decodeEventPayload(
  value: unknown,
  eventType: string,
  path: string
): Pick<AiAgentRunEventV1, 'sessionId' | 'planId' | 'planHash' | 'publicMessage' | 'plan' | 'workspaceSnapshot'> {
  if (value === null) return Object.freeze({
    sessionId: null, planId: null, planHash: null, publicMessage: null, plan: null, workspaceSnapshot: null
  });
  const source = record(value, path);
  const sessionId = typeof source.sessionId === 'string'
    ? string(source.sessionId, `${path}.sessionId`, sessionPattern)
    : null;
  const planId = typeof source.planId === 'string' ? source.planId : null;
  const planHash = typeof source.planHash === 'string' ? source.planHash : null;
  const publicMessage = typeof source.publicMessage === 'string' ? source.publicMessage : null;
  let plan: AiPlanV1 | null = null;
  if (eventType === 'plan.completed') {
    exact(source, [
      'status', 'generationMode', 'sessionId', 'planRunId', 'planSource', 'fallbackReason',
      'plannerFailureStage', 'plannerFailureCode', 'sanitizedErrorKind', 'sanitizedErrorMessage',
      'planResult', 'planModeResult', 'planId', 'planHash', 'canBuild', 'questionCount',
      'publicEventCount', 'workspaceSnapshot', 'persistenceStatus', 'persistenceWarning', 'metadataOnly'
    ], path);
    plan = decodeAiPlanV1(required(source, 'planResult', path), `${path}.planResult`);
    const duplicatePlan = decodeAiPlanV1(required(source, 'planModeResult', path), `${path}.planModeResult`);
    if (duplicatePlan.planId !== plan.planId || duplicatePlan.planHash !== plan.planHash) {
      throw new AiContractDecodeError(path, 'matching replay-safe Plan projections');
    }
    if (planId !== plan.planId || planHash !== plan.planHash) {
      throw new AiContractDecodeError(path, 'matching outer and inner Plan identity');
    }
  }
  return Object.freeze({
    sessionId,
    planId,
    planHash,
    publicMessage,
    plan,
    workspaceSnapshot: source.workspaceSnapshot === null || source.workspaceSnapshot === undefined
      ? null
      : decodeAiSessionSnapshotV1(source.workspaceSnapshot, `${path}.workspaceSnapshot`)
  });
}

export function decodeAiAgentRunEventV1(value: unknown, path = '$'): AiAgentRunEventV1 {
  const source = record(value, path);
  exact(source, [
    'runId', 'sequence', 'timestamp', 'eventType', 'stage', 'title', 'summary', 'status',
    'payload', 'metadataOnly', 'redactionPass'
  ], path);
  if (required(source, 'metadataOnly', path) !== true || required(source, 'redactionPass', path) !== true) {
    throw new AiContractDecodeError(path, 'a redacted metadata-only public event');
  }
  const eventType = string(required(source, 'eventType', path), `${path}.eventType`, tokenPattern);
  const payload = decodeEventPayload(required(source, 'payload', path), eventType, `${path}.payload`);
  return Object.freeze({
    runId: string(required(source, 'runId', path), `${path}.runId`, tokenPattern),
    sequence: integer(required(source, 'sequence', path), `${path}.sequence`),
    timestamp: timestamp(required(source, 'timestamp', path), `${path}.timestamp`),
    eventType,
    stage: string(required(source, 'stage', path), `${path}.stage`),
    title: string(required(source, 'title', path), `${path}.title`),
    summary: string(required(source, 'summary', path), `${path}.summary`),
    status: eventStatus(required(source, 'status', path), `${path}.status`),
    ...payload,
    metadataOnly: true,
    redactionPass: true
  });
}

function decodeSummary(value: unknown, path: string): AiAgentRunSummaryV1 {
  const source = record(value, path);
  exact(source, [
    'runId', 'createdAt', 'updatedAt', 'status', 'title', 'summary', 'firstFixRecommendation',
    'lastSequence', 'eventCount', 'duplicateEventCount', 'droppedEventCount', 'staleEventCount',
    'ownerHash', 'terminalIntent', 'metadataOnly', 'redactionPass', 'payload'
  ], path);
  timestamp(required(source, 'createdAt', path), `${path}.createdAt`);
  timestamp(required(source, 'updatedAt', path), `${path}.updatedAt`);
  string(required(source, 'ownerHash', path), `${path}.ownerHash`);
  optionalRecord(source.terminalIntent, `${path}.terminalIntent`);
  optionalRecord(source.payload, `${path}.payload`);
  if (required(source, 'metadataOnly', path) !== true || required(source, 'redactionPass', path) !== true) {
    throw new AiContractDecodeError(path, 'a redacted metadata-only summary');
  }
  return Object.freeze({
    runId: string(required(source, 'runId', path), `${path}.runId`, tokenPattern),
    status: eventStatus(required(source, 'status', path), `${path}.status`),
    title: string(required(source, 'title', path), `${path}.title`),
    summary: string(required(source, 'summary', path), `${path}.summary`),
    firstFixRecommendation: string(
      required(source, 'firstFixRecommendation', path), `${path}.firstFixRecommendation`
    ),
    lastSequence: integer(required(source, 'lastSequence', path), `${path}.lastSequence`),
    eventCount: integer(required(source, 'eventCount', path), `${path}.eventCount`),
    duplicateEventCount: integer(required(source, 'duplicateEventCount', path), `${path}.duplicateEventCount`),
    droppedEventCount: integer(required(source, 'droppedEventCount', path), `${path}.droppedEventCount`),
    staleEventCount: integer(required(source, 'staleEventCount', path), `${path}.staleEventCount`)
  });
}

export function decodeAiAgentRunReplayV1(value: unknown, path = '$'): AiAgentRunReplayV1 {
  const source = record(value, path);
  exact(source, ['summary', 'events', 'snapshot', 'diagnostics'], path);
  const events = required(source, 'events', path);
  if (!Array.isArray(events)) throw new AiContractDecodeError(`${path}.events`, 'an array');
  record(required(source, 'snapshot', path), `${path}.snapshot`);
  record(required(source, 'diagnostics', path), `${path}.diagnostics`);
  return Object.freeze({
    summary: decodeSummary(required(source, 'summary', path), `${path}.summary`),
    events: Object.freeze(events.map((item, index) => decodeAiAgentRunEventV1(item, `${path}.events[${index}]`)))
  });
}

export function decodeAiPlanRunResponseV1(value: unknown, path = '$'): AiPlanRunResponseV1 {
  const source = record(value, path);
  exact(source, [
    'runId', 'sessionId', 'brief', 'events', 'workspaceSnapshot', 'operation',
    'persistenceStatus', 'metadataOnly'
  ], path);
  const events = required(source, 'events', path);
  if (!Array.isArray(events)) throw new AiContractDecodeError(`${path}.events`, 'an array');
  if (source.persistenceStatus !== undefined) record(source.persistenceStatus, `${path}.persistenceStatus`);
  if (source.metadataOnly !== undefined && source.metadataOnly !== true) {
    throw new AiContractDecodeError(`${path}.metadataOnly`, 'true');
  }
  return Object.freeze({
    runId: nullableString(required(source, 'runId', path), `${path}.runId`, tokenPattern),
    sessionId: nullableString(required(source, 'sessionId', path), `${path}.sessionId`, sessionPattern),
    brief: source.brief === null || source.brief === undefined ? null : string(source.brief, `${path}.brief`),
    events: Object.freeze(events.map((item, index) => decodeAiAgentRunEventV1(item, `${path}.events[${index}]`))),
    workspaceSnapshot: source.workspaceSnapshot === null
      ? null
      : decodeAiSessionSnapshotV1(required(source, 'workspaceSnapshot', path), `${path}.workspaceSnapshot`),
    operation: decodeAiOperationProjectionV1(required(source, 'operation', path), `${path}.operation`)
  });
}

export function decodeAiProjectContextV1(value: unknown, path = '$'): AiProjectContextV1 {
  const source = record(value, path);
  exact(source, [
    'id', 'name', 'description', 'version', 'persistenceRevision', 'createdAt', 'modifiedAt',
    'lastOpenedAt', 'flow', 'assets'
  ], path);
  nullableString(required(source, 'description', path), `${path}.description`);
  timestamp(required(source, 'createdAt', path), `${path}.createdAt`);
  if (source.lastOpenedAt !== null) timestamp(required(source, 'lastOpenedAt', path), `${path}.lastOpenedAt`);
  if (source.flow !== null) record(required(source, 'flow', path), `${path}.flow`);
  record(required(source, 'assets', path), `${path}.assets`);
  return Object.freeze({
    id: string(required(source, 'id', path), `${path}.id`, guidPattern),
    name: string(required(source, 'name', path), `${path}.name`),
    version: string(required(source, 'version', path), `${path}.version`),
    persistenceRevision: integer(required(source, 'persistenceRevision', path), `${path}.persistenceRevision`),
    modifiedAt: source.modifiedAt === null ? null : timestamp(source.modifiedAt, `${path}.modifiedAt`)
  });
}
