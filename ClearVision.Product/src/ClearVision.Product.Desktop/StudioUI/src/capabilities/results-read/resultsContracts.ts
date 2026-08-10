import {
  decodeInspectionOutcome,
  type InspectionOutcome
} from '@/shared/inspectionOutcome';

export class ResultsContractDecodeError extends Error {
  readonly path: string;
  readonly expectation: string;

  constructor(path: string, expectation: string) {
    super('检测结果服务返回的数据格式不符合要求，请刷新后重试。');
    this.name = 'ResultsContractDecodeError';
    this.path = path;
    this.expectation = expectation;
  }
}

export interface ResultsProjectOption {
  readonly id: string;
  readonly name: string;
  readonly version: string;
  readonly persistenceRevision: number;
}

export interface LocalInspectionResultSummary {
  readonly id: string;
  readonly projectId: string;
  readonly status: string;
  readonly outcome: InspectionOutcome;
  readonly decisionSource: string | null;
  readonly reasonCode: string | null;
  readonly hasJudgmentSignal: boolean;
  readonly defectCount: number;
  readonly processingTimeMs: number;
  readonly inspectionTime: string;
  readonly startedAt: string;
  readonly completedAt: string;
  readonly confidenceScore: number | null;
  readonly flowVersionHash: string | null;
  readonly calibrationBundleId: string | null;
  readonly runId: string | null;
  readonly diagnosticCode: string | null;
  readonly diagnosticMessage: string | null;
  readonly errorMessage: string | null;
}

export interface LocalInspectionResultPage {
  readonly items: readonly LocalInspectionResultSummary[];
  readonly totalCount: number;
  readonly pageIndex: number;
  readonly pageSize: number;
}

export interface LocalInspectionDefectSummary {
  readonly id: string;
  readonly type: string;
  readonly confidenceScore: number;
  readonly description: string | null;
}

export interface InspectionTraceability {
  readonly flowVersionHash: string | null;
  readonly calibrationBundleId: string | null;
  readonly sessionId: string | null;
  readonly runId: string | null;
  readonly executionSnapshotId: string | null;
  readonly projectPersistenceRevision: number | null;
  readonly decisionConfigurationHash: string | null;
  readonly packageId: string | null;
  readonly runtimePackageId: string | null;
  readonly executionSource: string | null;
  readonly executionRunMode: string | null;
  readonly shadowRole: string | null;
  readonly stationId: string | null;
}

export interface LocalInspectionResultDetail extends LocalInspectionResultSummary {
  readonly defects: readonly LocalInspectionDefectSummary[];
  readonly traceability: InspectionTraceability;
  readonly hasEvidenceManifest: boolean;
  readonly evidenceStatus: string;
  readonly evidenceManifestReference: string | null;
  readonly evidenceTotalBytes: number | null;
  readonly retentionExpiresAtUtc: string | null;
  readonly evidenceMessage: string | null;
  readonly hasImage: boolean;
  readonly imageReference: string | null;
  readonly imageMissing: boolean;
  readonly imageMissingMessage: string | null;
  readonly hasOutputData: boolean;
  readonly hasAnalysisData: boolean;
}

export type LegacyStationRuntimeOutcome =
  | 'Ok'
  | 'Ng'
  | 'Error'
  | 'Canceled'
  | 'Undetermined';

export interface StationInspectionResultSummary {
  readonly schemaVersion: number;
  readonly stationId: string;
  readonly lineName: string | null;
  readonly sequenceId: number;
  readonly messageId: string;
  readonly runId: string;
  readonly packageId: string;
  readonly packageName: string;
  readonly packageVersion: string;
  readonly packageFlowHash: string | null;
  readonly executionFlowHash: string | null;
  readonly flowHash: string | null;
  readonly executionSnapshotId: string | null;
  readonly projectRevision: number | null;
  readonly decisionConfigurationHash: string | null;
  readonly executionRunMode: string | null;
  readonly outcome: InspectionOutcome;
  readonly legacyProjection: boolean;
  readonly decisionSource: string | null;
  readonly reasonCode: string | null;
  readonly hasJudgmentSignal: boolean;
  readonly executionTimeMs: number;
  readonly diagnosticCode: string;
  readonly diagnosticMessage: string | null;
  readonly primaryOutputsPreview: Readonly<Record<string, string | null>>;
  readonly remoteImageAvailability: 'not-uploaded';
  readonly startedAtUtc: string;
  readonly completedAtUtc: string;
}

export interface StationInspectionResultPage {
  readonly items: readonly StationInspectionResultSummary[];
  readonly totalCount: number;
  readonly pageIndex: number;
  readonly pageSize: number;
}

export interface ResultsOutcomeStatistics {
  readonly totalAttemptCount: number;
  readonly executionSucceededCount: number;
  readonly validDecisionCount: number;
  readonly okCount: number;
  readonly ngCount: number;
  readonly undeterminedCount: number;
  readonly notApplicableCount: number;
  readonly invalidCount: number;
  readonly failedCount: number;
  readonly cancelledCount: number;
  readonly timedOutCount: number;
  readonly skippedCount: number;
  readonly executionFailureCount: number;
  readonly yieldRate: number;
  readonly decisionCoverageRate: number;
  readonly averageExecutionTimeMs: number;
}

export interface InspectionHistoryComparisonSummary {
  readonly resultId: string;
  readonly projectId: string;
  readonly status: string;
  readonly outcome: InspectionOutcome;
  readonly inspectionTime: string;
  readonly defectCount: number;
  readonly processingTimeMs: number;
  readonly confidenceScore: number | null;
  readonly flowVersionHash: string | null;
  readonly calibrationBundleId: string | null;
  readonly sessionId: string | null;
  readonly runId: string | null;
  readonly imageReference: string | null;
  readonly hasImage: boolean;
  readonly hasOutputData: boolean;
  readonly hasAnalysisData: boolean;
}

export interface InspectionHistoryFieldDiff {
  readonly path: string;
  readonly label: string;
  readonly leftValuePreview: string | null;
  readonly rightValuePreview: string | null;
  readonly diffType: string;
  readonly severity: string;
  readonly message: string | null;
}

export interface InspectionHistoryReplayAvailability {
  readonly kind: string;
  readonly mode: string;
  readonly isAvailable: boolean;
  readonly leftAvailable: boolean;
  readonly rightAvailable: boolean;
  readonly leftReference: string | null;
  readonly rightReference: string | null;
  readonly leftSummary: string | null;
  readonly rightSummary: string | null;
  readonly message: string;
}

export interface InspectionHistoryComparison {
  readonly leftSummary: InspectionHistoryComparisonSummary;
  readonly rightSummary: InspectionHistoryComparisonSummary;
  readonly compatibility: Readonly<{
    flowVersionCompatible: boolean;
    calibrationBundleCompatible: boolean;
    onlySafePreviewComparison: boolean;
    hasUnknownFields: boolean;
  }>;
  readonly warnings: readonly string[];
  readonly fieldDiffs: readonly InspectionHistoryFieldDiff[];
  readonly traceabilityDiff: readonly InspectionHistoryFieldDiff[];
  readonly sceneReplayAvailability: InspectionHistoryReplayAvailability;
  readonly imageReplayAvailability: InspectionHistoryReplayAvailability;
}

export interface InspectionPreviousSuccessReference {
  readonly currentSummary: InspectionHistoryComparisonSummary;
  readonly referenceSummary: InspectionHistoryComparisonSummary | null;
  readonly found: boolean;
  readonly isFlowVersionFallback: boolean;
  readonly queryLimit: number;
  readonly warnings: readonly string[];
  readonly message: string;
}

export interface ExpectedInspectionResultIdentity {
  readonly projectId: string;
  readonly resultId: string;
}

export interface ExpectedInspectionComparisonIdentity {
  readonly projectId: string;
  readonly leftResultId: string;
  readonly rightResultId: string;
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const emptyUuid = '00000000-0000-0000-0000-000000000000';
const legacyRuntimeOutcomes = Object.freeze([
  'Ok',
  'Ng',
  'Error',
  'Canceled',
  'Undetermined'
] as const);

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function sameIdentity(actual: string, expected: string): boolean {
  return actual.toLowerCase() === expected.toLowerCase();
}

function requireIdentity(actual: string, expected: string, path: string): void {
  if (!sameIdentity(actual, expected)) {
    throw new ResultsContractDecodeError(path, `the requested identity ${expected}`);
  }
}

function record(value: unknown, path: string): Record<string, unknown> {
  if (!isRecord(value)) throw new ResultsContractDecodeError(path, 'an object');
  return value;
}

function string(value: unknown, path: string, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && value.trim().length === 0)) {
    throw new ResultsContractDecodeError(path, allowEmpty ? 'a string' : 'a non-empty string');
  }
  return value;
}

function nullableString(value: unknown, path: string): string | null {
  if (value === null) return null;
  return string(value, path, true);
}

function optionalNullableString(value: unknown, path: string): string | null {
  if (value === undefined || value === null) return null;
  return string(value, path, true);
}

function uuid(value: unknown, path: string): string {
  const decoded = string(value, path);
  if (!uuidPattern.test(decoded) || decoded.toLowerCase() === emptyUuid) {
    throw new ResultsContractDecodeError(path, 'a non-empty UUID');
  }
  return decoded;
}

function optionalNullableUuid(value: unknown, path: string): string | null {
  if (value === undefined || value === null) return null;
  return uuid(value, path);
}

function optionalNullableNonNegativeInteger(value: unknown, path: string): number | null {
  if (value === undefined || value === null) return null;
  return nonNegativeInteger(value, path);
}

function finiteNumber(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new ResultsContractDecodeError(path, 'a finite number');
  }
  return value;
}

function nullableFiniteNumber(value: unknown, path: string): number | null {
  if (value === null) return null;
  return finiteNumber(value, path);
}

function nonNegativeInteger(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new ResultsContractDecodeError(path, 'a non-negative safe integer');
  }
  return value;
}

function positiveInteger(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) {
    throw new ResultsContractDecodeError(path, 'a positive safe integer');
  }
  return value;
}

function boolean(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') throw new ResultsContractDecodeError(path, 'a boolean');
  return value;
}

function optionalBoolean(value: unknown, path: string): boolean | undefined {
  if (value === undefined || value === null) return undefined;
  return boolean(value, path);
}

function dateTime(value: unknown, path: string): string {
  const decoded = string(value, path);
  if (Number.isNaN(Date.parse(decoded))) {
    throw new ResultsContractDecodeError(path, 'an ISO date-time string');
  }
  return decoded;
}

function array(value: unknown, path: string): readonly unknown[] {
  if (!Array.isArray(value)) throw new ResultsContractDecodeError(path, 'an array');
  return value;
}

function stringArray(value: unknown, path: string): readonly string[] {
  return Object.freeze(array(value, path).map((item, index) => string(item, `${path}[${index}]`, true)));
}

function stringRecord(value: unknown, path: string): Readonly<Record<string, string | null>> {
  if (value === undefined || value === null) return Object.freeze({});
  const source = record(value, path);
  return Object.freeze(Object.fromEntries(Object.entries(source).map(([key, item]) => [
    key,
    item === null ? null : string(item, `${path}.${key}`, true)
  ])));
}

function decodeProject(value: unknown, path: string): ResultsProjectOption {
  const item = record(value, path);
  nullableString(item.description, `${path}.description`);
  dateTime(item.createdAt, `${path}.createdAt`);
  if (item.modifiedAt !== null) dateTime(item.modifiedAt, `${path}.modifiedAt`);
  if (item.lastOpenedAt !== null) dateTime(item.lastOpenedAt, `${path}.lastOpenedAt`);
  return Object.freeze({
    id: uuid(item.id, `${path}.id`),
    name: string(item.name, `${path}.name`),
    version: string(item.version, `${path}.version`),
    persistenceRevision: nonNegativeInteger(
      item.persistenceRevision,
      `${path}.persistenceRevision`
    )
  });
}

function decodeLocalSummary(value: unknown, path: string): LocalInspectionResultSummary {
  const item = record(value, path);
  const id = uuid(item.resultId ?? item.id, `${path}.resultId`);
  return Object.freeze({
    id,
    projectId: uuid(item.projectId, `${path}.projectId`),
    status: string(item.status, `${path}.status`),
    outcome: decodeInspectionOutcome(item.executionOutcome, item.decisionOutcome, path),
    decisionSource: optionalNullableString(item.decisionSource, `${path}.decisionSource`),
    reasonCode: optionalNullableString(item.reasonCode, `${path}.reasonCode`),
    hasJudgmentSignal: boolean(item.hasJudgmentSignal, `${path}.hasJudgmentSignal`),
    defectCount: nonNegativeInteger(item.defectCount, `${path}.defectCount`),
    processingTimeMs: nonNegativeInteger(
      item.processingTimeMs ?? item.processingTime,
      `${path}.processingTimeMs`
    ),
    inspectionTime: dateTime(
      item.inspectionTime ?? item.timestamp,
      `${path}.inspectionTime`
    ),
    startedAt: dateTime(item.startedAt, `${path}.startedAt`),
    completedAt: dateTime(item.completedAt, `${path}.completedAt`),
    confidenceScore: nullableFiniteNumber(item.confidenceScore, `${path}.confidenceScore`),
    flowVersionHash: optionalNullableString(item.flowVersionHash, `${path}.flowVersionHash`),
    calibrationBundleId: optionalNullableString(
      item.calibrationBundleId,
      `${path}.calibrationBundleId`
    ),
    runId: optionalNullableUuid(item.runId, `${path}.runId`),
    diagnosticCode: optionalNullableString(item.diagnosticCode, `${path}.diagnosticCode`),
    diagnosticMessage: optionalNullableString(
      item.diagnosticMessage,
      `${path}.diagnosticMessage`
    ),
    errorMessage: optionalNullableString(item.errorMessage, `${path}.errorMessage`)
  });
}

function decodeDefect(value: unknown, path: string): LocalInspectionDefectSummary {
  const item = record(value, path);
  return Object.freeze({
    id: uuid(item.id, `${path}.id`),
    type: string(item.type, `${path}.type`),
    confidenceScore: finiteNumber(item.confidenceScore, `${path}.confidenceScore`),
    description: nullableString(item.description, `${path}.description`)
  });
}

function decodeTraceability(value: unknown, path: string): InspectionTraceability {
  const item = record(value, path);
  return Object.freeze({
    flowVersionHash: optionalNullableString(item.flowVersionHash, `${path}.flowVersionHash`),
    calibrationBundleId: optionalNullableString(
      item.calibrationBundleId,
      `${path}.calibrationBundleId`
    ),
    sessionId: optionalNullableUuid(item.sessionId, `${path}.sessionId`),
    runId: optionalNullableUuid(item.runId, `${path}.runId`),
    executionSnapshotId: optionalNullableUuid(item.executionSnapshotId, `${path}.executionSnapshotId`),
    projectPersistenceRevision: optionalNullableNonNegativeInteger(
      item.projectPersistenceRevision,
      `${path}.projectPersistenceRevision`
    ),
    decisionConfigurationHash: optionalNullableString(
      item.decisionConfigurationHash,
      `${path}.decisionConfigurationHash`
    ),
    packageId: optionalNullableString(item.packageId, `${path}.packageId`),
    runtimePackageId: optionalNullableString(item.runtimePackageId, `${path}.runtimePackageId`),
    executionSource: optionalNullableString(item.executionSource, `${path}.executionSource`),
    executionRunMode: optionalNullableString(item.executionRunMode, `${path}.executionRunMode`),
    shadowRole: optionalNullableString(item.shadowRole, `${path}.shadowRole`),
    stationId: optionalNullableString(item.stationId, `${path}.stationId`)
  });
}

function decodeLegacyRuntimeOutcome(value: unknown, path: string): LegacyStationRuntimeOutcome {
  if (typeof value === 'number' && Number.isInteger(value)) {
    const decoded = legacyRuntimeOutcomes[value];
    if (decoded !== undefined) return decoded;
  }
  if (typeof value !== 'string' || !legacyRuntimeOutcomes.includes(value as LegacyStationRuntimeOutcome)) {
    throw new ResultsContractDecodeError(
      path,
      `an integer from 0 to 4 or one of ${legacyRuntimeOutcomes.join(', ')}`
    );
  }
  return value as LegacyStationRuntimeOutcome;
}

export function projectLegacyStationOutcome(outcome: LegacyStationRuntimeOutcome): InspectionOutcome {
  switch (outcome) {
    case 'Ok':
      return Object.freeze({ execution: 'Succeeded', decision: 'Ok' });
    case 'Ng':
      return Object.freeze({ execution: 'Succeeded', decision: 'Ng' });
    case 'Error':
      return Object.freeze({ execution: 'Failed', decision: 'Undetermined' });
    case 'Canceled':
      return Object.freeze({ execution: 'Cancelled', decision: 'NotApplicable' });
    case 'Undetermined':
      return Object.freeze({ execution: 'Succeeded', decision: 'Undetermined' });
  }
}

function decodeStationSummary(value: unknown, path: string): StationInspectionResultSummary {
  const item = record(value, path);
  const hasExecution = item.executionOutcome !== undefined && item.executionOutcome !== null;
  const hasDecision = item.decisionOutcome !== undefined && item.decisionOutcome !== null;
  if (hasExecution !== hasDecision) {
    throw new ResultsContractDecodeError(
      path,
      'both canonical outcome axes or neither canonical outcome axis'
    );
  }

  const legacyProjection = !hasExecution;
  const outcome = legacyProjection
    ? projectLegacyStationOutcome(decodeLegacyRuntimeOutcome(item.outcome, `${path}.outcome`))
    : decodeInspectionOutcome(item.executionOutcome, item.decisionOutcome, path);
  const payloadSignal = optionalBoolean(item.hasJudgmentSignal, `${path}.hasJudgmentSignal`);

  return Object.freeze({
    schemaVersion: positiveInteger(item.schemaVersion, `${path}.schemaVersion`),
    stationId: string(item.stationId, `${path}.stationId`),
    lineName: optionalNullableString(item.lineName, `${path}.lineName`),
    sequenceId: nonNegativeInteger(item.sequenceId, `${path}.sequenceId`),
    messageId: string(item.messageId, `${path}.messageId`),
    runId: string(item.runId, `${path}.runId`),
    packageId: string(item.packageId, `${path}.packageId`, true),
    packageName: string(item.packageName, `${path}.packageName`, true),
    packageVersion: string(item.packageVersion, `${path}.packageVersion`, true),
    packageFlowHash: optionalNullableString(item.packageFlowHash, `${path}.packageFlowHash`),
    executionFlowHash: optionalNullableString(item.executionFlowHash, `${path}.executionFlowHash`),
    flowHash: optionalNullableString(item.flowHash, `${path}.flowHash`),
    executionSnapshotId: optionalNullableUuid(item.executionSnapshotId, `${path}.executionSnapshotId`),
    projectRevision: optionalNullableNonNegativeInteger(item.projectRevision, `${path}.projectRevision`),
    decisionConfigurationHash: optionalNullableString(
      item.decisionConfigurationHash,
      `${path}.decisionConfigurationHash`
    ),
    executionRunMode: optionalNullableString(item.executionRunMode, `${path}.executionRunMode`),
    outcome,
    legacyProjection,
    decisionSource: legacyProjection
      ? 'LegacyStationResult'
      : optionalNullableString(item.decisionSource, `${path}.decisionSource`),
    reasonCode: legacyProjection
      ? 'LegacyStationOutcomeProjection'
      : optionalNullableString(item.reasonCode, `${path}.reasonCode`),
    hasJudgmentSignal: payloadSignal ?? (
      outcome.execution === 'Succeeded' &&
      (outcome.decision === 'Ok' || outcome.decision === 'Ng')
    ),
    executionTimeMs: nonNegativeInteger(item.executionTimeMs, `${path}.executionTimeMs`),
    diagnosticCode: string(item.diagnosticCode, `${path}.diagnosticCode`, true),
    diagnosticMessage: optionalNullableString(
      item.diagnosticMessage,
      `${path}.diagnosticMessage`
    ),
    primaryOutputsPreview: stringRecord(item.primaryOutputsPreview, `${path}.primaryOutputsPreview`),
    remoteImageAvailability: 'not-uploaded',
    startedAtUtc: dateTime(item.startedAtUtc, `${path}.startedAtUtc`),
    completedAtUtc: dateTime(item.completedAtUtc, `${path}.completedAtUtc`)
  });
}

function rate(value: unknown, path: string): number {
  const decoded = finiteNumber(value, path);
  if (decoded < 0 || decoded > 1) throw new ResultsContractDecodeError(path, 'a rate from 0 to 1');
  return decoded;
}

function decodeStatisticsSource(value: unknown, path: string): ResultsOutcomeStatistics {
  const item = record(value, path);
  const totalAttemptCount = nonNegativeInteger(
    item.totalAttemptCount ?? item.totalCount,
    `${path}.totalAttemptCount`
  );
  const executionSucceededCount = nonNegativeInteger(
    item.executionSucceededCount,
    `${path}.executionSucceededCount`
  );
  const validDecisionCount = nonNegativeInteger(item.validDecisionCount, `${path}.validDecisionCount`);
  if (validDecisionCount > executionSucceededCount || executionSucceededCount > totalAttemptCount) {
    throw new ResultsContractDecodeError(path, 'canonical counters with valid decisions <= succeeded executions <= attempts');
  }
  const okCount = nonNegativeInteger(item.okCount ?? item.oKCount, `${path}.okCount`);
  const ngCount = nonNegativeInteger(item.ngCount ?? item.nGCount, `${path}.ngCount`);
  if (okCount + ngCount !== validDecisionCount) {
    throw new ResultsContractDecodeError(path, 'validDecisionCount to equal okCount + ngCount');
  }
  return Object.freeze({
    totalAttemptCount,
    executionSucceededCount,
    validDecisionCount,
    okCount,
    ngCount,
    undeterminedCount: nonNegativeInteger(item.undeterminedCount, `${path}.undeterminedCount`),
    notApplicableCount: nonNegativeInteger(item.notApplicableCount, `${path}.notApplicableCount`),
    invalidCount: nonNegativeInteger(item.invalidCount, `${path}.invalidCount`),
    failedCount: nonNegativeInteger(item.failedCount, `${path}.failedCount`),
    cancelledCount: nonNegativeInteger(item.cancelledCount, `${path}.cancelledCount`),
    timedOutCount: nonNegativeInteger(item.timedOutCount, `${path}.timedOutCount`),
    skippedCount: nonNegativeInteger(item.skippedCount, `${path}.skippedCount`),
    executionFailureCount: nonNegativeInteger(item.executionFailureCount, `${path}.executionFailureCount`),
    yieldRate: rate(item.yieldRate, `${path}.yieldRate`),
    decisionCoverageRate: rate(item.decisionCoverageRate, `${path}.decisionCoverageRate`),
    averageExecutionTimeMs: finiteNumber(
      item.averageExecutionTimeMs ?? item.averageProcessingTimeMs,
      `${path}.averageExecutionTimeMs`
    )
  });
}

function decodeComparisonSummary(value: unknown, path: string): InspectionHistoryComparisonSummary {
  const item = record(value, path);
  return Object.freeze({
    resultId: uuid(item.resultId ?? item.id, `${path}.resultId`),
    projectId: uuid(item.projectId, `${path}.projectId`),
    status: string(item.status, `${path}.status`),
    outcome: decodeInspectionOutcome(item.executionOutcome, item.decisionOutcome, path),
    inspectionTime: dateTime(item.inspectionTime ?? item.timestamp, `${path}.inspectionTime`),
    defectCount: nonNegativeInteger(item.defectCount, `${path}.defectCount`),
    processingTimeMs: nonNegativeInteger(item.processingTimeMs ?? item.processingTime, `${path}.processingTimeMs`),
    confidenceScore: nullableFiniteNumber(item.confidenceScore, `${path}.confidenceScore`),
    flowVersionHash: optionalNullableString(item.flowVersionHash, `${path}.flowVersionHash`),
    calibrationBundleId: optionalNullableString(item.calibrationBundleId, `${path}.calibrationBundleId`),
    sessionId: optionalNullableUuid(item.sessionId, `${path}.sessionId`),
    runId: optionalNullableUuid(item.runId, `${path}.runId`),
    imageReference: optionalNullableString(item.imageReference, `${path}.imageReference`),
    hasImage: boolean(item.hasImage, `${path}.hasImage`),
    hasOutputData: boolean(item.hasOutputData, `${path}.hasOutputData`),
    hasAnalysisData: boolean(item.hasAnalysisData, `${path}.hasAnalysisData`)
  });
}

function decodeFieldDiff(value: unknown, path: string): InspectionHistoryFieldDiff {
  const item = record(value, path);
  return Object.freeze({
    path: string(item.path, `${path}.path`),
    label: string(item.label, `${path}.label`),
    leftValuePreview: optionalNullableString(item.leftValuePreview, `${path}.leftValuePreview`),
    rightValuePreview: optionalNullableString(item.rightValuePreview, `${path}.rightValuePreview`),
    diffType: string(item.diffType, `${path}.diffType`),
    severity: string(item.severity, `${path}.severity`),
    message: optionalNullableString(item.message, `${path}.message`)
  });
}

function decodeReplayAvailability(value: unknown, path: string): InspectionHistoryReplayAvailability {
  const item = record(value, path);
  return Object.freeze({
    kind: string(item.kind, `${path}.kind`),
    mode: string(item.mode, `${path}.mode`),
    isAvailable: boolean(item.isAvailable, `${path}.isAvailable`),
    leftAvailable: boolean(item.leftAvailable, `${path}.leftAvailable`),
    rightAvailable: boolean(item.rightAvailable, `${path}.rightAvailable`),
    leftReference: optionalNullableString(item.leftReference, `${path}.leftReference`),
    rightReference: optionalNullableString(item.rightReference, `${path}.rightReference`),
    leftSummary: optionalNullableString(item.leftSummary, `${path}.leftSummary`),
    rightSummary: optionalNullableString(item.rightSummary, `${path}.rightSummary`),
    message: string(item.message, `${path}.message`, true)
  });
}

export function isResultsProjectId(value: string): boolean {
  return uuidPattern.test(value) && value.toLowerCase() !== emptyUuid;
}

export function decodeResultsProjects(payload: unknown): readonly ResultsProjectOption[] {
  return Object.freeze(array(payload, '$').map((item, index) => decodeProject(item, `$[${index}]`)));
}

export function decodeLocalInspectionResultPage(
  payload: unknown,
  expectedProjectId?: string
): LocalInspectionResultPage {
  const page = record(payload, '$');
  const items = Object.freeze(
    array(page.items, '$.items').map((item, index) => decodeLocalSummary(item, `$.items[${index}]`))
  );
  if (expectedProjectId) {
    items.forEach((item, index) => requireIdentity(
      item.projectId,
      expectedProjectId,
      `$.items[${index}].projectId`
    ));
  }
  return Object.freeze({
    items,
    totalCount: nonNegativeInteger(page.totalCount, '$.totalCount'),
    pageIndex: nonNegativeInteger(page.pageIndex, '$.pageIndex'),
    pageSize: positiveInteger(page.pageSize, '$.pageSize')
  });
}

export function decodeLocalInspectionResultDetail(
  payload: unknown,
  expectedIdentity?: ExpectedInspectionResultIdentity
): LocalInspectionResultDetail {
  const item = record(payload, '$');
  const summary = decodeLocalSummary(item, '$');
  if (expectedIdentity) {
    requireIdentity(summary.projectId, expectedIdentity.projectId, '$.projectId');
    requireIdentity(summary.id, expectedIdentity.resultId, '$.resultId');
  }
  return Object.freeze({
    ...summary,
    defects: Object.freeze(
      array(item.defects, '$.defects').map((defect, index) => decodeDefect(defect, `$.defects[${index}]`))
    ),
    traceability: decodeTraceability(item.traceability, '$.traceability'),
    hasEvidenceManifest: boolean(item.hasEvidenceManifest, '$.hasEvidenceManifest'),
    evidenceStatus: string(item.evidenceStatus, '$.evidenceStatus'),
    evidenceManifestReference: optionalNullableString(item.evidenceManifestReference, '$.evidenceManifestReference'),
    evidenceTotalBytes: item.evidenceTotalBytes === null || item.evidenceTotalBytes === undefined
      ? null
      : nonNegativeInteger(item.evidenceTotalBytes, '$.evidenceTotalBytes'),
    retentionExpiresAtUtc: item.retentionExpiresAtUtc === null || item.retentionExpiresAtUtc === undefined
      ? null
      : dateTime(item.retentionExpiresAtUtc, '$.retentionExpiresAtUtc'),
    evidenceMessage: optionalNullableString(item.evidenceMessage, '$.evidenceMessage'),
    hasImage: boolean(item.hasImage, '$.hasImage'),
    imageReference: optionalNullableString(item.imageReference, '$.imageReference'),
    imageMissing: item.imageMissing === undefined ? false : boolean(item.imageMissing, '$.imageMissing'),
    imageMissingMessage: optionalNullableString(item.imageMissingMessage, '$.imageMissingMessage'),
    hasOutputData: boolean(item.hasOutputData, '$.hasOutputData'),
    hasAnalysisData: boolean(item.hasAnalysisData, '$.hasAnalysisData')
  });
}

export function decodeStationInspectionResultPage(
  payload: unknown,
  expectedStationId = ''
): StationInspectionResultPage {
  const page = record(payload, '$');
  const items = Object.freeze(
    array(page.items, '$.items').map((item, index) => decodeStationSummary(item, `$.items[${index}]`))
  );
  const normalizedStationId = expectedStationId.trim();
  if (normalizedStationId && items.some(item => !sameIdentity(item.stationId, normalizedStationId))) {
    throw new ResultsContractDecodeError('$.items[].stationId', 'the requested Station identity');
  }
  return Object.freeze({
    items,
    totalCount: nonNegativeInteger(page.totalCount, '$.totalCount'),
    pageIndex: nonNegativeInteger(page.pageIndex, '$.pageIndex'),
    pageSize: positiveInteger(page.pageSize, '$.pageSize')
  });
}

export function decodeResultsOutcomeStatistics(payload: unknown): ResultsOutcomeStatistics {
  const source = record(payload, '$');
  return decodeStatisticsSource(source.outcomeStatistics ?? source, '$');
}

export function decodeInspectionHistoryComparison(
  payload: unknown,
  expectedIdentity?: ExpectedInspectionComparisonIdentity
): InspectionHistoryComparison {
  const item = record(payload, '$');
  const compatibility = record(item.compatibility, '$.compatibility');
  const leftSummary = decodeComparisonSummary(item.leftSummary, '$.leftSummary');
  const rightSummary = decodeComparisonSummary(item.rightSummary, '$.rightSummary');
  if (!sameIdentity(leftSummary.projectId, rightSummary.projectId)) {
    throw new ResultsContractDecodeError('$', 'both comparison results to belong to the same project');
  }
  if (expectedIdentity) {
    requireIdentity(leftSummary.projectId, expectedIdentity.projectId, '$.leftSummary.projectId');
    requireIdentity(leftSummary.resultId, expectedIdentity.leftResultId, '$.leftSummary.resultId');
    requireIdentity(rightSummary.projectId, expectedIdentity.projectId, '$.rightSummary.projectId');
    requireIdentity(rightSummary.resultId, expectedIdentity.rightResultId, '$.rightSummary.resultId');
  }
  return Object.freeze({
    leftSummary,
    rightSummary,
    compatibility: Object.freeze({
      flowVersionCompatible: boolean(compatibility.flowVersionCompatible, '$.compatibility.flowVersionCompatible'),
      calibrationBundleCompatible: boolean(compatibility.calibrationBundleCompatible, '$.compatibility.calibrationBundleCompatible'),
      onlySafePreviewComparison: boolean(compatibility.onlySafePreviewComparison, '$.compatibility.onlySafePreviewComparison'),
      hasUnknownFields: boolean(compatibility.hasUnknownFields, '$.compatibility.hasUnknownFields')
    }),
    warnings: stringArray(item.warnings, '$.warnings'),
    fieldDiffs: Object.freeze(array(item.fieldDiffs, '$.fieldDiffs').map((diff, index) =>
      decodeFieldDiff(diff, `$.fieldDiffs[${index}]`))),
    traceabilityDiff: Object.freeze(array(item.traceabilityDiff, '$.traceabilityDiff').map((diff, index) =>
      decodeFieldDiff(diff, `$.traceabilityDiff[${index}]`))),
    sceneReplayAvailability: decodeReplayAvailability(item.sceneReplayAvailability, '$.sceneReplayAvailability'),
    imageReplayAvailability: decodeReplayAvailability(item.imageReplayAvailability, '$.imageReplayAvailability')
  });
}

export function decodeInspectionPreviousSuccess(
  payload: unknown,
  expectedIdentity?: ExpectedInspectionResultIdentity
): InspectionPreviousSuccessReference {
  const item = record(payload, '$');
  const currentSummary = decodeComparisonSummary(item.currentSummary, '$.currentSummary');
  const reference = item.referenceSummary === null
    ? null
    : decodeComparisonSummary(item.referenceSummary, '$.referenceSummary');
  const found = boolean(item.found, '$.found');
  if (found !== (reference !== null)) {
    throw new ResultsContractDecodeError('$', 'found to agree with referenceSummary availability');
  }
  if (reference && !sameIdentity(reference.projectId, currentSummary.projectId)) {
    throw new ResultsContractDecodeError('$', 'the previous-success reference to belong to the current project');
  }
  if (expectedIdentity) {
    requireIdentity(currentSummary.projectId, expectedIdentity.projectId, '$.currentSummary.projectId');
    requireIdentity(currentSummary.resultId, expectedIdentity.resultId, '$.currentSummary.resultId');
  }
  return Object.freeze({
    currentSummary,
    referenceSummary: reference,
    found,
    isFlowVersionFallback: boolean(item.isFlowVersionFallback, '$.isFlowVersionFallback'),
    queryLimit: positiveInteger(item.queryLimit, '$.queryLimit'),
    warnings: stringArray(item.warnings, '$.warnings'),
    message: string(item.message, '$.message', true)
  });
}
