import {
  decodeInspectionOutcome,
  type InspectionOutcome
} from '@/shared/inspectionOutcome';

export class StationContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expectation: string) {
    super(`Station response field ${path} must be ${expectation}.`);
    this.name = 'StationContractDecodeError';
    this.path = path;
  }
}

export const stationOnlineStates = Object.freeze([
  'Unknown',
  'Online',
  'Warning',
  'Degraded',
  'Critical',
  'Offline'
] as const);

export const stationRuntimeStates = Object.freeze([
  'Unknown',
  'Idle',
  'Running',
  'Paused',
  'LoadingPackage',
  'Faulted',
  'Stopping'
] as const);

export const stationOfflineReasons = Object.freeze([
  'NeverRegistered',
  'HeartbeatExpired',
  'Disabled',
  'Disconnected'
] as const);

export type StationOnlineState = (typeof stationOnlineStates)[number];
export type StationRuntimeState = (typeof stationRuntimeStates)[number];
export type StationOfflineReason = (typeof stationOfflineReasons)[number];

export interface InspectionOutcomeStatistics {
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
}

export interface StationStatus {
  readonly stationId: string;
  readonly stationName: string;
  readonly lineName: string | null;
  readonly machineName: string;
  readonly onlineState: StationOnlineState;
  readonly runtimeState: StationRuntimeState;
  readonly isOnline: boolean;
  readonly isEnabled: boolean;
  readonly offlineReason: StationOfflineReason | null;
  readonly lastSeenAtUtc: string;
  readonly startedAtUtc: string;
  readonly packageId: string | null;
  readonly packageName: string | null;
  readonly packageVersion: string | null;
  readonly packageSha256: string | null;
  readonly sourceProjectId: string | null;
  readonly sourceProjectRevision: number | null;
  readonly packageFlowHash: string | null;
  readonly executionFlowHash: string | null;
  readonly executionSnapshotId: string | null;
  readonly projectRevision: number | null;
  readonly decisionConfigurationHash: string | null;
  readonly executionRunMode: string | null;
  readonly currentRunId: string | null;
  readonly averageExecutionTimeMs: number;
  readonly spoolPendingCount: number;
  readonly spoolBytes: number;
  readonly cpuUsagePercent: number | null;
  readonly workingSetMb: number;
  readonly diskFreeMb: number;
  readonly diskTotalMb: number;
  readonly cameraStatusSummary: string | null;
  readonly plcStatusSummary: string | null;
  readonly currentPackageHealth: string | null;
  readonly lastDiagnosticCode: string | null;
  readonly lastDiagnosticMessage: string | null;
  readonly lastResultAtUtc: string | null;
  readonly lastOutcome: InspectionOutcome | null;
  readonly sessionOutcomeStatistics: InspectionOutcomeStatistics;
  readonly sessionOutcomeStatisticsIsLegacyProjection: boolean;
}

export interface StationAdminDetails {
  readonly stationId: string;
  readonly stationName: string;
  readonly lineName: string | null;
  readonly clientVersion: string;
  readonly areaName: string | null;
  readonly workcellName: string | null;
  readonly inspectionNodeName: string | null;
  readonly cameraAlias: string | null;
  readonly stationRole: string;
  readonly owner: string | null;
  readonly isEnabled: boolean;
  readonly remark: string | null;
  readonly onlineState: StationOnlineState;
  readonly isOnline: boolean;
  readonly packageId: string | null;
  readonly packageName: string | null;
  readonly packageVersion: string | null;
  readonly packageSha256: string | null;
  readonly sourceProjectId: string | null;
  readonly sourceProjectRevision: number | null;
  readonly packageFlowHash: string | null;
  readonly executionFlowHash: string | null;
  readonly projectRevision: number | null;
  readonly decisionConfigurationHash: string | null;
  readonly executionRunMode: string | null;
}

export const stationCommandTypes = Object.freeze([
  'Ping', 'StartRuntime', 'StopRuntime', 'ReloadPackage', 'DeployPackage', 'ApplySiteProfile', 'CollectLogs'
] as const);
export const stationCommandStatuses = Object.freeze([
  'Created', 'Delivered', 'Accepted', 'Rejected', 'Running', 'Succeeded', 'Failed', 'TimedOut', 'Cancelled'
] as const);
export type StationCommandType = (typeof stationCommandTypes)[number];
export type StationCommandStatus = (typeof stationCommandStatuses)[number];

export interface StationCommand {
  readonly schemaVersion: number;
  readonly commandId: string;
  readonly stationId: string;
  readonly commandType: StationCommandType;
  readonly payloadJson: string;
  readonly createdAtUtc: string;
  readonly expiresAtUtc: string;
  readonly issuedBy: string;
  readonly correlationId: string;
  readonly clientRequestId: string | null;
  readonly status: StationCommandStatus;
  readonly progressPercent: number;
  readonly completedAtUtc: string | null;
  readonly resultMessage: string | null;
  readonly errorCode: string | null;
}

export interface StationLog {
  readonly schemaVersion: number;
  readonly stationId: string;
  readonly sequenceId: number;
  readonly messageId: string;
  readonly timestampUtc: string;
  readonly level: string;
  readonly source: string;
  readonly eventId: string | null;
  readonly renderedMessage: string;
  readonly exceptionType: string | null;
  readonly exceptionMessage: string | null;
  readonly correlationId: string | null;
  readonly runId: string | null;
  readonly packageId: string | null;
}

export interface StationAudit {
  readonly auditId: string;
  readonly userName: string | null;
  readonly action: string;
  readonly targetStationId: string | null;
  readonly commandId: string | null;
  readonly payloadSummary: string | null;
  readonly createdAtUtc: string;
  readonly result: string | null;
  readonly clientIp: string | null;
}

export type StationPackageKind = 'Production' | 'Test';
export interface StationPackage {
  readonly schemaVersion: number;
  readonly packageId: string;
  readonly packageName: string;
  readonly packageVersion: string;
  readonly packageKind: StationPackageKind;
  readonly flowHash: string;
  readonly sourceProjectId: string | null;
  readonly sourceProjectRevision: number | null;
  readonly decisionConfigurationHash: string | null;
  readonly createdBy: string;
  readonly minStationVersion: string;
  readonly requiredOperators: readonly string[];
  readonly sizeBytes: number;
  readonly sha256: string;
  readonly createdAtUtc: string;
}

export interface StationSummary {
  readonly totalStations: number;
  readonly onlineStations: number;
  readonly offlineStations: number;
  readonly runningStations: number;
  readonly faultedStations: number;
  readonly alertCount: number;
  readonly warningStations: number;
  readonly criticalStations: number;
  readonly outcomeStatistics: InspectionOutcomeStatistics;
  readonly averageExecutionTimeMs: number;
  readonly offlineThresholdSeconds: number;
  readonly updatedAtUtc: string;
}

export interface StationResult {
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
  readonly executionTimeMs: number;
  readonly diagnosticCode: string;
  readonly diagnosticMessage: string | null;
  readonly completedAtUtc: string;
  readonly outcome: InspectionOutcome;
  readonly legacyOutcomeProjection: boolean;
}

export interface StationHealthSnapshot {
  readonly schemaVersion: number;
  readonly stationId: string;
  readonly sequenceId: number;
  readonly messageId: string;
  readonly runtimeState: StationRuntimeState;
  readonly processUptimeSeconds: number;
  readonly cpuUsagePercent: number | null;
  readonly workingSetMb: number;
  readonly privateMemoryMb: number;
  readonly diskFreeMb: number;
  readonly diskTotalMb: number;
  readonly spoolPendingCount: number;
  readonly spoolBytes: number;
  readonly cameraStatusSummary: string | null;
  readonly plcStatusSummary: string | null;
  readonly currentPackageId: string | null;
  readonly currentPackageHealth: string | null;
  readonly lastErrorCode: string | null;
  readonly lastErrorMessage: string | null;
  readonly createdAtUtc: string;
}

export interface StationOutcomeBreakdown {
  readonly stationId: string;
  readonly outcomeStatistics: InspectionOutcomeStatistics;
  readonly averageExecutionTimeMs: number;
}

export interface StationDiagnosticBreakdown {
  readonly diagnosticCode: string;
  readonly count: number;
}

export interface StationOutcomeTrend {
  readonly hourUtc: string;
  readonly outcomeStatistics: InspectionOutcomeStatistics;
}

export interface StationStatistics {
  readonly fromUtc: string | null;
  readonly toUtc: string | null;
  readonly outcomeStatistics: InspectionOutcomeStatistics;
  readonly averageExecutionTimeMs: number;
  readonly byStation: readonly StationOutcomeBreakdown[];
  readonly byDiagnosticCode: readonly StationDiagnosticBreakdown[];
  readonly hourlyTrend: readonly StationOutcomeTrend[];
}

export interface StationResultsPage {
  readonly items: readonly StationResult[];
  readonly totalCount: number;
  readonly pageIndex: number;
  readonly pageSize: number;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function decodeRecord(value: unknown, path: string): Record<string, unknown> {
  if (!isRecord(value)) throw new StationContractDecodeError(path, 'an object');
  return value;
}

function decodeArray(value: unknown, path: string): readonly unknown[] {
  if (!Array.isArray(value)) throw new StationContractDecodeError(path, 'an array');
  return value;
}

function decodeString(value: unknown, path: string, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && value.trim().length === 0)) {
    throw new StationContractDecodeError(path, allowEmpty ? 'a string' : 'a non-empty string');
  }
  return value;
}

function decodeNullableString(value: unknown, path: string): string | null {
  if (value === null) return null;
  return decodeString(value, path, true);
}

function decodeBoolean(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') throw new StationContractDecodeError(path, 'a boolean');
  return value;
}

function decodeNumber(value: unknown, path: string, integer = false): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 || (integer && !Number.isInteger(value))) {
    throw new StationContractDecodeError(path, integer ? 'a non-negative integer' : 'a non-negative finite number');
  }
  return value;
}

function decodeNullableNumber(value: unknown, path: string, integer = false): number | null {
  if (value === null) return null;
  return decodeNumber(value, path, integer);
}

function decodeDateTime(value: unknown, path: string): string {
  const decoded = decodeString(value, path);
  if (Number.isNaN(Date.parse(decoded))) {
    throw new StationContractDecodeError(path, 'an ISO date-time string');
  }
  return decoded;
}

function decodeNullableDateTime(value: unknown, path: string): string | null {
  if (value === null) return null;
  return decodeDateTime(value, path);
}

function decodeEnum<T extends string>(
  value: unknown,
  path: string,
  allowed: readonly T[]
): T {
  if (typeof value !== 'string' || !allowed.includes(value as T)) {
    throw new StationContractDecodeError(path, `one of ${allowed.join(', ')}`);
  }
  return value as T;
}

function decodeNumericOrStringEnum<T extends string>(
  value: unknown,
  path: string,
  allowed: readonly T[]
): T {
  if (typeof value === 'number' && Number.isInteger(value)) {
    const mapped = allowed[value];
    if (mapped !== undefined) return mapped;
  }
  return decodeEnum(value, path, allowed);
}

function decodeOutcomeStatistics(value: unknown, path: string): InspectionOutcomeStatistics {
  const record = decodeRecord(value, path);
  return Object.freeze({
    totalAttemptCount: decodeNumber(record.totalAttemptCount, `${path}.totalAttemptCount`, true),
    executionSucceededCount: decodeNumber(record.executionSucceededCount, `${path}.executionSucceededCount`, true),
    validDecisionCount: decodeNumber(record.validDecisionCount, `${path}.validDecisionCount`, true),
    okCount: decodeNumber(record.okCount, `${path}.okCount`, true),
    ngCount: decodeNumber(record.ngCount, `${path}.ngCount`, true),
    undeterminedCount: decodeNumber(record.undeterminedCount, `${path}.undeterminedCount`, true),
    notApplicableCount: decodeNumber(record.notApplicableCount, `${path}.notApplicableCount`, true),
    invalidCount: decodeNumber(record.invalidCount, `${path}.invalidCount`, true),
    failedCount: decodeNumber(record.failedCount, `${path}.failedCount`, true),
    cancelledCount: decodeNumber(record.cancelledCount, `${path}.cancelledCount`, true),
    timedOutCount: decodeNumber(record.timedOutCount, `${path}.timedOutCount`, true),
    skippedCount: decodeNumber(record.skippedCount, `${path}.skippedCount`, true)
  });
}

function decodeOptionalOutcomePair(
  execution: unknown,
  decision: unknown,
  path: string
): InspectionOutcome | null {
  if (execution === null && decision === null) return null;
  if (execution === null || decision === null) {
    throw new StationContractDecodeError(path, 'both canonical outcome axes or neither');
  }
  return decodeInspectionOutcome(execution, decision, path);
}

export function decodeStationStatus(value: unknown, path = '$'): StationStatus {
  const record = decodeRecord(value, path);
  return Object.freeze({
    stationId: decodeString(record.stationId, `${path}.stationId`),
    stationName: decodeString(record.stationName, `${path}.stationName`, true),
    lineName: decodeNullableString(record.lineName, `${path}.lineName`),
    machineName: decodeString(record.machineName, `${path}.machineName`, true),
    onlineState: decodeNumericOrStringEnum(record.onlineState, `${path}.onlineState`, stationOnlineStates),
    runtimeState: decodeNumericOrStringEnum(record.runtimeState, `${path}.runtimeState`, stationRuntimeStates),
    isOnline: decodeBoolean(record.isOnline, `${path}.isOnline`),
    isEnabled: decodeBoolean(record.isEnabled, `${path}.isEnabled`),
    offlineReason: record.offlineReason === undefined || record.offlineReason === null
      ? null
      : decodeNumericOrStringEnum(record.offlineReason, `${path}.offlineReason`, stationOfflineReasons),
    lastSeenAtUtc: decodeDateTime(record.lastSeenAtUtc, `${path}.lastSeenAtUtc`),
    startedAtUtc: decodeDateTime(record.startedAtUtc, `${path}.startedAtUtc`),
    packageId: decodeNullableString(record.packageId, `${path}.packageId`),
    packageName: decodeNullableString(record.packageName, `${path}.packageName`),
    packageVersion: decodeNullableString(record.packageVersion, `${path}.packageVersion`),
    packageSha256: decodeNullableString(record.packageSha256, `${path}.packageSha256`),
    sourceProjectId: decodeNullableString(record.sourceProjectId, `${path}.sourceProjectId`),
    sourceProjectRevision: decodeNullableNumber(record.sourceProjectRevision, `${path}.sourceProjectRevision`, true),
    packageFlowHash: decodeNullableString(record.packageFlowHash, `${path}.packageFlowHash`),
    executionFlowHash: decodeNullableString(record.executionFlowHash, `${path}.executionFlowHash`),
    executionSnapshotId: decodeNullableString(record.executionSnapshotId, `${path}.executionSnapshotId`),
    projectRevision: decodeNullableNumber(record.projectRevision, `${path}.projectRevision`, true),
    decisionConfigurationHash: decodeNullableString(
      record.decisionConfigurationHash,
      `${path}.decisionConfigurationHash`
    ),
    executionRunMode: decodeNullableString(record.executionRunMode, `${path}.executionRunMode`),
    currentRunId: decodeNullableString(record.currentRunId, `${path}.currentRunId`),
    averageExecutionTimeMs: decodeNumber(record.averageExecutionTimeMs, `${path}.averageExecutionTimeMs`),
    spoolPendingCount: decodeNumber(record.spoolPendingCount, `${path}.spoolPendingCount`, true),
    spoolBytes: decodeNumber(record.spoolBytes, `${path}.spoolBytes`, true),
    cpuUsagePercent: decodeNullableNumber(record.cpuUsagePercent, `${path}.cpuUsagePercent`),
    workingSetMb: decodeNumber(record.workingSetMb, `${path}.workingSetMb`, true),
    diskFreeMb: decodeNumber(record.diskFreeMb, `${path}.diskFreeMb`, true),
    diskTotalMb: decodeNumber(record.diskTotalMb, `${path}.diskTotalMb`, true),
    cameraStatusSummary: decodeNullableString(record.cameraStatusSummary, `${path}.cameraStatusSummary`),
    plcStatusSummary: decodeNullableString(record.plcStatusSummary, `${path}.plcStatusSummary`),
    currentPackageHealth: decodeNullableString(record.currentPackageHealth, `${path}.currentPackageHealth`),
    lastDiagnosticCode: decodeNullableString(record.lastDiagnosticCode, `${path}.lastDiagnosticCode`),
    lastDiagnosticMessage: decodeNullableString(record.lastDiagnosticMessage, `${path}.lastDiagnosticMessage`),
    lastResultAtUtc: decodeNullableDateTime(record.lastResultAtUtc, `${path}.lastResultAtUtc`),
    lastOutcome: decodeOptionalOutcomePair(
      record.lastExecutionOutcome,
      record.lastDecisionOutcome,
      `${path}.lastOutcome`
    ),
    sessionOutcomeStatistics: decodeOutcomeStatistics(
      record.sessionOutcomeStatistics,
      `${path}.sessionOutcomeStatistics`
    ),
    sessionOutcomeStatisticsIsLegacyProjection: decodeBoolean(
      record.sessionOutcomeStatisticsIsLegacyProjection,
      `${path}.sessionOutcomeStatisticsIsLegacyProjection`
    )
  });
}

function projectLegacyOutcome(record: Record<string, unknown>, path: string): InspectionOutcome {
  const outcome = decodeNumericOrStringEnum(
    record.outcome,
    `${path}.outcome`,
    ['Ok', 'Ng', 'Error', 'Canceled', 'Undetermined'] as const
  );
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

function decodeResultOutcome(
  record: Record<string, unknown>,
  path: string
): { readonly outcome: InspectionOutcome; readonly legacyOutcomeProjection: boolean } {
  const execution = record.executionOutcome;
  const decision = record.decisionOutcome;
  if (execution !== null && execution !== undefined && decision !== null && decision !== undefined) {
    return Object.freeze({
      outcome: decodeInspectionOutcome(execution, decision, path),
      legacyOutcomeProjection: false
    });
  }
  if ((execution !== null && execution !== undefined) || (decision !== null && decision !== undefined)) {
    throw new StationContractDecodeError(path, 'both canonical outcome axes or neither');
  }
  return Object.freeze({
    outcome: projectLegacyOutcome(record, path),
    legacyOutcomeProjection: true
  });
}

export function decodeStationResult(value: unknown, path = '$'): StationResult {
  const record = decodeRecord(value, path);
  const outcome = decodeResultOutcome(record, path);
  return Object.freeze({
    schemaVersion: decodeNumber(record.schemaVersion, `${path}.schemaVersion`, true),
    stationId: decodeString(record.stationId, `${path}.stationId`),
    lineName: decodeNullableString(record.lineName, `${path}.lineName`),
    sequenceId: decodeNumber(record.sequenceId, `${path}.sequenceId`, true),
    messageId: decodeString(record.messageId, `${path}.messageId`),
    runId: decodeString(record.runId, `${path}.runId`),
    packageId: decodeString(record.packageId, `${path}.packageId`, true),
    packageName: decodeString(record.packageName, `${path}.packageName`, true),
    packageVersion: decodeString(record.packageVersion, `${path}.packageVersion`, true),
    packageFlowHash: record.packageFlowHash === undefined || record.packageFlowHash === null
      ? null
      : decodeString(record.packageFlowHash, `${path}.packageFlowHash`, true),
    executionFlowHash: record.executionFlowHash === undefined || record.executionFlowHash === null
      ? null
      : decodeString(record.executionFlowHash, `${path}.executionFlowHash`, true),
    flowHash: record.flowHash === undefined || record.flowHash === null
      ? null
      : decodeString(record.flowHash, `${path}.flowHash`, true),
    executionSnapshotId: record.executionSnapshotId === undefined || record.executionSnapshotId === null
      ? null
      : decodeString(record.executionSnapshotId, `${path}.executionSnapshotId`, true),
    projectRevision: record.projectRevision === undefined || record.projectRevision === null
      ? null
      : decodeNumber(record.projectRevision, `${path}.projectRevision`, true),
    decisionConfigurationHash: record.decisionConfigurationHash === undefined || record.decisionConfigurationHash === null
      ? null
      : decodeString(record.decisionConfigurationHash, `${path}.decisionConfigurationHash`, true),
    executionRunMode: record.executionRunMode === undefined || record.executionRunMode === null
      ? null
      : decodeString(record.executionRunMode, `${path}.executionRunMode`, true),
    executionTimeMs: decodeNumber(record.executionTimeMs, `${path}.executionTimeMs`, true),
    diagnosticCode: decodeString(record.diagnosticCode, `${path}.diagnosticCode`, true),
    diagnosticMessage: decodeNullableString(record.diagnosticMessage, `${path}.diagnosticMessage`),
    completedAtUtc: decodeDateTime(record.completedAtUtc, `${path}.completedAtUtc`),
    ...outcome
  });
}

export function decodeHealthSnapshot(value: unknown, path = '$'): StationHealthSnapshot {
  const record = decodeRecord(value, path);
  return Object.freeze({
    schemaVersion: decodeNumber(record.schemaVersion, `${path}.schemaVersion`, true),
    stationId: decodeString(record.stationId, `${path}.stationId`),
    sequenceId: decodeNumber(record.sequenceId, `${path}.sequenceId`, true),
    messageId: decodeString(record.messageId, `${path}.messageId`),
    runtimeState: decodeNumericOrStringEnum(record.runtimeState, `${path}.runtimeState`, stationRuntimeStates),
    processUptimeSeconds: decodeNumber(record.processUptimeSeconds, `${path}.processUptimeSeconds`, true),
    cpuUsagePercent: decodeNullableNumber(record.cpuUsagePercent, `${path}.cpuUsagePercent`),
    workingSetMb: decodeNumber(record.workingSetMb, `${path}.workingSetMb`, true),
    privateMemoryMb: decodeNumber(record.privateMemoryMb, `${path}.privateMemoryMb`, true),
    diskFreeMb: decodeNumber(record.diskFreeMb, `${path}.diskFreeMb`, true),
    diskTotalMb: decodeNumber(record.diskTotalMb, `${path}.diskTotalMb`, true),
    spoolPendingCount: decodeNumber(record.spoolPendingCount, `${path}.spoolPendingCount`, true),
    spoolBytes: decodeNumber(record.spoolBytes, `${path}.spoolBytes`, true),
    cameraStatusSummary: decodeNullableString(record.cameraStatusSummary, `${path}.cameraStatusSummary`),
    plcStatusSummary: decodeNullableString(record.plcStatusSummary, `${path}.plcStatusSummary`),
    currentPackageId: decodeNullableString(record.currentPackageId, `${path}.currentPackageId`),
    currentPackageHealth: decodeNullableString(record.currentPackageHealth, `${path}.currentPackageHealth`),
    lastErrorCode: decodeNullableString(record.lastErrorCode, `${path}.lastErrorCode`),
    lastErrorMessage: decodeNullableString(record.lastErrorMessage, `${path}.lastErrorMessage`),
    createdAtUtc: decodeDateTime(record.createdAtUtc, `${path}.createdAtUtc`)
  });
}

export function decodeStationList(payload: unknown): readonly StationStatus[] {
  return Object.freeze(decodeArray(payload, '$').map((item, index) => decodeStationStatus(item, `$[${index}]`)));
}

export function decodeStationSummary(payload: unknown): StationSummary {
  const record = decodeRecord(payload, '$');
  return Object.freeze({
    totalStations: decodeNumber(record.totalStations, '$.totalStations', true),
    onlineStations: decodeNumber(record.onlineStations, '$.onlineStations', true),
    offlineStations: decodeNumber(record.offlineStations, '$.offlineStations', true),
    runningStations: decodeNumber(record.runningStations, '$.runningStations', true),
    faultedStations: decodeNumber(record.faultedStations, '$.faultedStations', true),
    alertCount: decodeNumber(record.alertCount, '$.alertCount', true),
    warningStations: decodeNumber(record.warningStations, '$.warningStations', true),
    criticalStations: decodeNumber(record.criticalStations, '$.criticalStations', true),
    outcomeStatistics: decodeOutcomeStatistics(record.outcomeStatistics, '$.outcomeStatistics'),
    averageExecutionTimeMs: decodeNumber(record.averageExecutionTimeMs, '$.averageExecutionTimeMs'),
    offlineThresholdSeconds: decodeNumber(record.offlineThresholdSeconds, '$.offlineThresholdSeconds', true),
    updatedAtUtc: decodeDateTime(record.updatedAtUtc, '$.updatedAtUtc')
  });
}

export function decodeStationAdminDetails(payload: unknown): StationAdminDetails {
  const record = decodeRecord(payload, '$');
  return Object.freeze({
    stationId: decodeString(record.stationId, '$.stationId'),
    stationName: decodeString(record.stationName, '$.stationName', true),
    lineName: decodeNullableString(record.lineName, '$.lineName'),
    clientVersion: decodeString(record.clientVersion, '$.clientVersion', true),
    areaName: decodeNullableString(record.areaName, '$.areaName'),
    workcellName: decodeNullableString(record.workcellName, '$.workcellName'),
    inspectionNodeName: decodeNullableString(record.inspectionNodeName, '$.inspectionNodeName'),
    cameraAlias: decodeNullableString(record.cameraAlias, '$.cameraAlias'),
    stationRole: decodeString(record.stationRole, '$.stationRole', true),
    owner: decodeNullableString(record.owner, '$.owner'),
    isEnabled: decodeBoolean(record.isEnabled, '$.isEnabled'),
    remark: decodeNullableString(record.remark, '$.remark'),
    onlineState: decodeNumericOrStringEnum(record.onlineState, '$.onlineState', stationOnlineStates),
    isOnline: decodeBoolean(record.isOnline, '$.isOnline'),
    packageId: decodeNullableString(record.packageId, '$.packageId'),
    packageName: decodeNullableString(record.packageName, '$.packageName'),
    packageVersion: decodeNullableString(record.packageVersion, '$.packageVersion'),
    packageSha256: decodeNullableString(record.packageSha256, '$.packageSha256'),
    sourceProjectId: decodeNullableString(record.sourceProjectId, '$.sourceProjectId'),
    sourceProjectRevision: decodeNullableNumber(record.sourceProjectRevision, '$.sourceProjectRevision', true),
    packageFlowHash: decodeNullableString(record.packageFlowHash, '$.packageFlowHash'),
    executionFlowHash: decodeNullableString(record.executionFlowHash, '$.executionFlowHash'),
    projectRevision: decodeNullableNumber(record.projectRevision, '$.projectRevision', true),
    decisionConfigurationHash: decodeNullableString(
      record.decisionConfigurationHash,
      '$.decisionConfigurationHash'
    ),
    executionRunMode: decodeNullableString(record.executionRunMode, '$.executionRunMode')
  });
}

export function decodeStationCommands(payload: unknown): readonly StationCommand[] {
  return Object.freeze(decodeArray(payload, '$').map((item, index) => {
    const path = `$[${index}]`;
    const record = decodeRecord(item, path);
    return Object.freeze({
      schemaVersion: decodeNumber(record.schemaVersion, `${path}.schemaVersion`, true),
      commandId: decodeString(record.commandId, `${path}.commandId`),
      stationId: decodeString(record.stationId, `${path}.stationId`),
      commandType: decodeNumericOrStringEnum(record.commandType, `${path}.commandType`, stationCommandTypes),
      payloadJson: decodeString(record.payloadJson, `${path}.payloadJson`, true),
      createdAtUtc: decodeDateTime(record.createdAtUtc, `${path}.createdAtUtc`),
      expiresAtUtc: decodeDateTime(record.expiresAtUtc, `${path}.expiresAtUtc`),
      issuedBy: decodeString(record.issuedBy, `${path}.issuedBy`, true),
      correlationId: decodeString(record.correlationId, `${path}.correlationId`, true),
      clientRequestId: decodeNullableString(record.clientRequestId, `${path}.clientRequestId`),
      status: decodeNumericOrStringEnum(record.status, `${path}.status`, stationCommandStatuses),
      progressPercent: decodeNumber(record.progressPercent, `${path}.progressPercent`, true),
      completedAtUtc: decodeNullableDateTime(record.completedAtUtc, `${path}.completedAtUtc`),
      resultMessage: decodeNullableString(record.resultMessage, `${path}.resultMessage`),
      errorCode: decodeNullableString(record.errorCode, `${path}.errorCode`)
    });
  }));
}

export function decodeStationCommand(payload: unknown): StationCommand {
  const command = decodeStationCommands([payload])[0];
  if (!command) throw new StationContractDecodeError('$', 'a Station command');
  return command;
}

export function decodeStationLogs(payload: unknown): readonly StationLog[] {
  return Object.freeze(decodeArray(payload, '$').map((item, index) => {
    const path = `$[${index}]`;
    const record = decodeRecord(item, path);
    return Object.freeze({
      schemaVersion: decodeNumber(record.schemaVersion, `${path}.schemaVersion`, true),
      stationId: decodeString(record.stationId, `${path}.stationId`),
      sequenceId: decodeNumber(record.sequenceId, `${path}.sequenceId`, true),
      messageId: decodeString(record.messageId, `${path}.messageId`),
      timestampUtc: decodeDateTime(record.timestampUtc, `${path}.timestampUtc`),
      level: decodeString(record.level, `${path}.level`, true),
      source: decodeString(record.source, `${path}.source`, true),
      eventId: decodeNullableString(record.eventId, `${path}.eventId`),
      renderedMessage: decodeString(record.renderedMessage, `${path}.renderedMessage`, true),
      exceptionType: decodeNullableString(record.exceptionType, `${path}.exceptionType`),
      exceptionMessage: decodeNullableString(record.exceptionMessage, `${path}.exceptionMessage`),
      correlationId: decodeNullableString(record.correlationId, `${path}.correlationId`),
      runId: decodeNullableString(record.runId, `${path}.runId`),
      packageId: decodeNullableString(record.packageId, `${path}.packageId`)
    });
  }));
}

export function decodeStationAudits(payload: unknown): readonly StationAudit[] {
  return Object.freeze(decodeArray(payload, '$').map((item, index) => {
    const path = `$[${index}]`;
    const record = decodeRecord(item, path);
    return Object.freeze({
      auditId: decodeString(record.auditId, `${path}.auditId`),
      userName: decodeNullableString(record.userName, `${path}.userName`),
      action: decodeString(record.action, `${path}.action`, true),
      targetStationId: decodeNullableString(record.targetStationId, `${path}.targetStationId`),
      commandId: decodeNullableString(record.commandId, `${path}.commandId`),
      payloadSummary: decodeNullableString(record.payloadSummary, `${path}.payloadSummary`),
      createdAtUtc: decodeDateTime(record.createdAtUtc, `${path}.createdAtUtc`),
      result: decodeNullableString(record.result, `${path}.result`),
      clientIp: decodeNullableString(record.clientIp, `${path}.clientIp`)
    });
  }));
}

export function decodeStationPackages(payload: unknown): readonly StationPackage[] {
  const kinds = ['Production', 'Test'] as const;
  return Object.freeze(decodeArray(payload, '$').map((item, index) => {
    const path = `$[${index}]`;
    const record = decodeRecord(item, path);
    return Object.freeze({
      schemaVersion: decodeNumber(record.schemaVersion, `${path}.schemaVersion`, true),
      packageId: decodeString(record.packageId, `${path}.packageId`),
      packageName: decodeString(record.packageName, `${path}.packageName`, true),
      packageVersion: decodeString(record.packageVersion, `${path}.packageVersion`, true),
      packageKind: decodeNumericOrStringEnum(record.packageKind, `${path}.packageKind`, kinds),
      flowHash: decodeString(record.flowHash, `${path}.flowHash`, true),
      sourceProjectId: decodeNullableString(record.sourceProjectId, `${path}.sourceProjectId`),
      sourceProjectRevision: decodeNullableNumber(record.sourceProjectRevision, `${path}.sourceProjectRevision`, true),
      decisionConfigurationHash: decodeNullableString(
        record.decisionConfigurationHash,
        `${path}.decisionConfigurationHash`
      ),
      createdBy: decodeString(record.createdBy, `${path}.createdBy`, true),
      minStationVersion: decodeString(record.minStationVersion, `${path}.minStationVersion`, true),
      requiredOperators: Object.freeze(decodeArray(record.requiredOperators, `${path}.requiredOperators`).map(
        (value, operatorIndex) => decodeString(value, `${path}.requiredOperators[${operatorIndex}]`, true)
      )),
      sizeBytes: decodeNumber(record.sizeBytes, `${path}.sizeBytes`, true),
      sha256: decodeString(record.sha256, `${path}.sha256`, true),
      createdAtUtc: decodeDateTime(record.createdAtUtc, `${path}.createdAtUtc`)
    });
  }));
}

export function decodeStationResults(payload: unknown): readonly StationResult[] {
  return Object.freeze(decodeArray(payload, '$').map((item, index) => decodeStationResult(item, `$[${index}]`)));
}

export function decodeStationResultsPage(payload: unknown): StationResultsPage {
  const record = decodeRecord(payload, '$');
  return Object.freeze({
    items: Object.freeze(
      decodeArray(record.items, '$.items').map((item, index) => decodeStationResult(item, `$.items[${index}]`))
    ),
    totalCount: decodeNumber(record.totalCount, '$.totalCount', true),
    pageIndex: decodeNumber(record.pageIndex, '$.pageIndex', true),
    pageSize: decodeNumber(record.pageSize, '$.pageSize', true)
  });
}

export function decodeStationHealth(payload: unknown): readonly StationHealthSnapshot[] {
  return Object.freeze(decodeArray(payload, '$').map((item, index) => decodeHealthSnapshot(item, `$[${index}]`)));
}

export function decodeStationStatistics(payload: unknown): StationStatistics {
  const record = decodeRecord(payload, '$');
  const byStation = decodeArray(record.byStation, '$.byStation').map((item, index) => {
    const path = `$.byStation[${index}]`;
    const breakdown = decodeRecord(item, path);
    return Object.freeze({
      stationId: decodeString(breakdown.stationId, `${path}.stationId`),
      outcomeStatistics: decodeOutcomeStatistics(breakdown.outcomeStatistics, `${path}.outcomeStatistics`),
      averageExecutionTimeMs: decodeNumber(
        breakdown.averageExecutionTimeMs,
        `${path}.averageExecutionTimeMs`
      )
    });
  });
  const byDiagnosticCode = decodeArray(record.byDiagnosticCode, '$.byDiagnosticCode').map((item, index) => {
    const path = `$.byDiagnosticCode[${index}]`;
    const breakdown = decodeRecord(item, path);
    return Object.freeze({
      diagnosticCode: decodeString(breakdown.diagnosticCode, `${path}.diagnosticCode`),
      count: decodeNumber(breakdown.count, `${path}.count`, true)
    });
  });
  const hourlyTrend = decodeArray(record.hourlyTrend, '$.hourlyTrend').map((item, index) => {
    const path = `$.hourlyTrend[${index}]`;
    const trend = decodeRecord(item, path);
    return Object.freeze({
      hourUtc: decodeDateTime(trend.hourUtc, `${path}.hourUtc`),
      outcomeStatistics: decodeOutcomeStatistics(trend.outcomeStatistics, `${path}.outcomeStatistics`)
    });
  });
  return Object.freeze({
    fromUtc: decodeNullableDateTime(record.fromUtc, '$.fromUtc'),
    toUtc: decodeNullableDateTime(record.toUtc, '$.toUtc'),
    outcomeStatistics: decodeOutcomeStatistics(record.outcomeStatistics, '$.outcomeStatistics'),
    averageExecutionTimeMs: decodeNumber(record.averageExecutionTimeMs, '$.averageExecutionTimeMs'),
    byStation: Object.freeze(byStation),
    byDiagnosticCode: Object.freeze(byDiagnosticCode),
    hourlyTrend: Object.freeze(hourlyTrend)
  });
}
