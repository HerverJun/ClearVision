export type InspectionRuntimeStatus = 'Idle' | 'Starting' | 'Running' | 'Stopping' | 'Stopped' | 'Faulted';
export type InspectionRuntimeSessionType = 'WorkspaceFormalRun' | 'ContinuousInspection' | 'LegacyRealtime';

export interface InspectionRunIdentity {
  readonly projectId: string;
  readonly clientSnapshotId: string;
  readonly expectedPersistenceRevision: number;
  readonly expectedCanonicalFlowHash: string;
  readonly expectedDecisionConfigurationHash: string;
}

export interface InspectionRunState {
  readonly projectId: string;
  readonly status: InspectionRuntimeStatus;
  readonly isBusy: boolean;
  readonly sessionId: string | null;
  readonly startedAt: string | null;
  readonly stoppedAt: string | null;
  readonly clientSnapshotId: string | null;
  readonly persistenceRevision: number | null;
  readonly canonicalFlowHash: string | null;
  readonly decisionConfigurationHash: string | null;
  readonly executionSource: string | null;
  readonly sessionType: InspectionRuntimeSessionType | null;
}

export interface InspectionRunStartResult extends InspectionRunIdentity {
  readonly persistenceRevision: number;
  readonly canonicalFlowHash: string;
  readonly decisionConfigurationHash: string;
  readonly runMode: 'canonical-project';
  readonly cameraId: string | null;
  readonly sessionId: string;
  readonly sessionType: 'ContinuousInspection';
}

export interface InspectionRunResult {
  readonly projectId: string;
  readonly sessionId: string;
  readonly resultId: string;
  readonly status: string;
  readonly executionOutcome: string | null;
  readonly decisionOutcome: string | null;
  readonly defectCount: number;
  readonly processingTimeMs: number;
  readonly errorMessage: string | null;
  readonly timestamp: string;
}

export type InspectionSseEvent =
  | Readonly<{ type: 'stateChanged'; id: string | null; state: InspectionRunStateEvent }>
  | Readonly<{ type: 'resultProduced'; id: string | null; result: InspectionRunResult }>
  | Readonly<{ type: 'faulted'; id: string | null; projectId: string; sessionId: string; errorMessage: string | null }>
  | Readonly<{ type: 'heartbeat'; id: string | null }>;

export interface InspectionRunStateEvent {
  readonly projectId: string;
  readonly sessionId: string;
  readonly oldState: string | null;
  readonly newState: InspectionRuntimeStatus;
  readonly errorMessage: string | null;
  readonly timestamp: string;
  readonly isSnapshot: boolean;
  readonly startedAt: string | null;
  readonly stoppedAt: string | null;
  readonly sessionType: InspectionRuntimeSessionType | null;
}

export class InspectionRunDecodeError extends Error {}

function record(value: unknown, label: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new InspectionRunDecodeError(`${label} must be an object.`);
  }
  return value as Record<string, unknown>;
}

function string(value: unknown, label: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new InspectionRunDecodeError(`${label} must be a string.`);
  return value;
}

function nullableString(value: unknown, label: string): string | null {
  if (value == null) return null;
  return string(value, label);
}

function number(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new InspectionRunDecodeError(`${label} must be a number.`);
  return value;
}

function nonNegativeInteger(value: unknown, label: string): number {
  const decoded = number(value, label);
  if (!Number.isSafeInteger(decoded) || decoded < 0) {
    throw new InspectionRunDecodeError(`${label} must be a non-negative safe integer.`);
  }
  return decoded;
}

function boolean(value: unknown, label: string): boolean {
  if (typeof value !== 'boolean') throw new InspectionRunDecodeError(`${label} must be a boolean.`);
  return value;
}

const runtimeStatuses = new Set<InspectionRuntimeStatus>(['Idle', 'Starting', 'Running', 'Stopping', 'Stopped', 'Faulted']);
const runtimeSessionTypes = new Set<InspectionRuntimeSessionType>(['WorkspaceFormalRun', 'ContinuousInspection', 'LegacyRealtime']);
function status(value: unknown): InspectionRuntimeStatus {
  const decoded = string(value, 'status') as InspectionRuntimeStatus;
  if (!runtimeStatuses.has(decoded)) throw new InspectionRunDecodeError(`Unsupported runtime status: ${decoded}.`);
  return decoded;
}

function nullableSessionType(value: unknown): InspectionRuntimeSessionType | null {
  if (value == null) return null;
  const decoded = string(value, 'sessionType') as InspectionRuntimeSessionType;
  if (!runtimeSessionTypes.has(decoded)) throw new InspectionRunDecodeError(`Unsupported runtime session type: ${decoded}.`);
  return decoded;
}

export function decodeInspectionRunState(value: unknown): InspectionRunState {
  const data = record(value, 'inspection run state');
  const decodedStatus = status(data.status);
  return Object.freeze({
    projectId: string(data.projectId, 'projectId'),
    status: decodedStatus,
    isBusy: boolean(data.isBusy, 'isBusy'),
    sessionId: nullableString(data.sessionId, 'sessionId'),
    startedAt: nullableString(data.startedAt, 'startedAt'),
    stoppedAt: nullableString(data.stoppedAt, 'stoppedAt'),
    clientSnapshotId: nullableString(data.clientSnapshotId, 'clientSnapshotId'),
    persistenceRevision: data.persistenceRevision == null ? null : nonNegativeInteger(data.persistenceRevision, 'persistenceRevision'),
    canonicalFlowHash: nullableString(data.canonicalFlowHash, 'canonicalFlowHash'),
    decisionConfigurationHash: nullableString(data.decisionConfigurationHash, 'decisionConfigurationHash'),
    executionSource: nullableString(data.executionSource, 'executionSource'),
    sessionType: nullableSessionType(data.sessionType)
  });
}

export function decodeInspectionRunStart(value: unknown): InspectionRunStartResult {
  const data = record(value, 'inspection run start');
  const persistenceRevision = nonNegativeInteger(data.persistenceRevision, 'persistenceRevision');
  const canonicalFlowHash = string(data.canonicalFlowHash, 'canonicalFlowHash');
  const decisionConfigurationHash = string(data.decisionConfigurationHash, 'decisionConfigurationHash');
  if (data.runMode !== 'canonical-project') throw new InspectionRunDecodeError('runMode must be canonical-project.');
  return Object.freeze({
    projectId: string(data.projectId, 'projectId'),
    clientSnapshotId: string(data.clientSnapshotId, 'clientSnapshotId'),
    expectedPersistenceRevision: persistenceRevision,
    expectedCanonicalFlowHash: canonicalFlowHash,
    expectedDecisionConfigurationHash: decisionConfigurationHash,
    persistenceRevision,
    canonicalFlowHash,
    decisionConfigurationHash,
    runMode: 'canonical-project',
    cameraId: nullableString(data.cameraId, 'cameraId'),
    sessionId: string(data.sessionId, 'sessionId'),
    sessionType: data.sessionType === 'ContinuousInspection'
      ? 'ContinuousInspection'
      : (() => { throw new InspectionRunDecodeError('sessionType must be ContinuousInspection.'); })()
  });
}

export function decodeInspectionSseEvent(type: string, id: string | null, value: unknown): InspectionSseEvent | null {
  if (type === 'heartbeat') return Object.freeze({ type, id });
  const data = record(value, type);
  if (type === 'stateChanged') {
    return Object.freeze({ type, id, state: Object.freeze({
      projectId: string(data.projectId, 'projectId'), sessionId: string(data.sessionId, 'sessionId'),
      oldState: nullableString(data.oldState, 'oldState'), newState: status(data.newState),
      errorMessage: nullableString(data.errorMessage, 'errorMessage'), timestamp: string(data.timestamp, 'timestamp'),
      isSnapshot: boolean(data.isSnapshot, 'isSnapshot'), startedAt: nullableString(data.startedAt, 'startedAt'),
      stoppedAt: nullableString(data.stoppedAt, 'stoppedAt'), sessionType: nullableSessionType(data.sessionType)
    }) });
  }
  if (type === 'faulted') return Object.freeze({ type, id, projectId: string(data.projectId, 'projectId'),
    sessionId: string(data.sessionId, 'sessionId'), errorMessage: nullableString(data.errorMessage, 'errorMessage') });
  if (type === 'resultProduced') return Object.freeze({ type, id, result: Object.freeze({
    projectId: string(data.projectId, 'projectId'), sessionId: string(data.sessionId, 'sessionId'),
    resultId: string(data.resultId, 'resultId'), status: string(data.status, 'status'),
    executionOutcome: nullableString(data.executionOutcome, 'executionOutcome'),
    decisionOutcome: nullableString(data.decisionOutcome, 'decisionOutcome'), defectCount: nonNegativeInteger(data.defectCount, 'defectCount'),
    processingTimeMs: nonNegativeInteger(data.processingTimeMs, 'processingTimeMs'),
    errorMessage: nullableString(data.errorMessage, 'errorMessage'), timestamp: string(data.timestamp, 'timestamp')
  }) });
  return null;
}
