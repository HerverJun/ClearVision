import {
  decodeInspectionOutcome,
  type InspectionOutcome
} from '@/shared/inspectionOutcome';

export class ResultsContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expectation: string) {
    super(`Results response field ${path} must be ${expectation}.`);
    this.name = 'ResultsContractDecodeError';
    this.path = path;
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
  readonly projectPersistenceRevision: number | null;
  readonly decisionConfigurationHash: string | null;
  readonly packageId: string | null;
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
  readonly projectRevision: number;
  readonly outcome: InspectionOutcome;
  readonly legacyProjection: boolean;
  readonly decisionSource: string | null;
  readonly reasonCode: string | null;
  readonly hasJudgmentSignal: boolean;
  readonly executionTimeMs: number;
  readonly diagnosticCode: string;
  readonly diagnosticMessage: string | null;
  readonly startedAtUtc: string;
  readonly completedAtUtc: string;
}

export interface StationInspectionResultPage {
  readonly items: readonly StationInspectionResultSummary[];
  readonly totalCount: number;
  readonly pageIndex: number;
  readonly pageSize: number;
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
    projectPersistenceRevision: item.projectPersistenceRevision === undefined || item.projectPersistenceRevision === null
      ? null
      : nonNegativeInteger(item.projectPersistenceRevision, `${path}.projectPersistenceRevision`),
    decisionConfigurationHash: optionalNullableString(
      item.decisionConfigurationHash,
      `${path}.decisionConfigurationHash`
    ),
    packageId: optionalNullableString(item.packageId, `${path}.packageId`),
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
    projectRevision: nonNegativeInteger(item.projectRevision, `${path}.projectRevision`),
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
    startedAtUtc: dateTime(item.startedAtUtc, `${path}.startedAtUtc`),
    completedAtUtc: dateTime(item.completedAtUtc, `${path}.completedAtUtc`)
  });
}

export function isResultsProjectId(value: string): boolean {
  return uuidPattern.test(value) && value.toLowerCase() !== emptyUuid;
}

export function decodeResultsProjects(payload: unknown): readonly ResultsProjectOption[] {
  return Object.freeze(array(payload, '$').map((item, index) => decodeProject(item, `$[${index}]`)));
}

export function decodeLocalInspectionResultPage(payload: unknown): LocalInspectionResultPage {
  const page = record(payload, '$');
  return Object.freeze({
    items: Object.freeze(
      array(page.items, '$.items').map((item, index) => decodeLocalSummary(item, `$.items[${index}]`))
    ),
    totalCount: nonNegativeInteger(page.totalCount, '$.totalCount'),
    pageIndex: nonNegativeInteger(page.pageIndex, '$.pageIndex'),
    pageSize: positiveInteger(page.pageSize, '$.pageSize')
  });
}

export function decodeLocalInspectionResultDetail(payload: unknown): LocalInspectionResultDetail {
  const item = record(payload, '$');
  const summary = decodeLocalSummary(item, '$');
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
    evidenceMessage: optionalNullableString(item.evidenceMessage, '$.evidenceMessage')
  });
}

export function decodeStationInspectionResultPage(payload: unknown): StationInspectionResultPage {
  const page = record(payload, '$');
  return Object.freeze({
    items: Object.freeze(
      array(page.items, '$.items').map((item, index) => decodeStationSummary(item, `$.items[${index}]`))
    ),
    totalCount: nonNegativeInteger(page.totalCount, '$.totalCount'),
    pageIndex: nonNegativeInteger(page.pageIndex, '$.pageIndex'),
    pageSize: positiveInteger(page.pageSize, '$.pageSize')
  });
}
