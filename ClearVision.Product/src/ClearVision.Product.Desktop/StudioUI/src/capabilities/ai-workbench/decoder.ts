import {
  AiContractDecodeError,
  type AiOperationKind,
  type AiOperationProjectionV1,
  type AiOperationStatus,
  type AiProjectBaselineV1,
  type AiSessionCreateResponseV1,
  type AiSessionDetailV1,
  type AiSessionSnapshotV1
} from './contracts';

type JsonRecord = Record<string, unknown>;

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const sessionPattern = /^[a-z0-9_-]{1,80}$/i;
const hashPattern = /^(?:sha256:)?[0-9a-f]{64}$/i;
const tokenPattern = /^[a-z0-9_.:-]{1,96}$/i;
const operationKinds = new Set<AiOperationKind>(['session_create', 'session_delete', 'plan_run', 'build_run']);
const operationStatuses = new Set<AiOperationStatus>(['pending', 'created', 'failed', 'rejected']);

function record(value: unknown, path: string): JsonRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new AiContractDecodeError(path, 'an object');
  }
  return value as JsonRecord;
}

function exact(source: JsonRecord, allowed: readonly string[], path: string): void {
  const allowedKeys = new Set(allowed);
  const unexpected = Object.keys(source).find(key => !allowedKeys.has(key));
  if (unexpected) throw new AiContractDecodeError(`${path}.${unexpected}`, 'absent from the public DTO');
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

function integer(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new AiContractDecodeError(path, 'a non-negative safe integer');
  }
  return value;
}

function timestamp(value: unknown, path: string): string {
  const decoded = string(value, path);
  if (!Number.isFinite(Date.parse(decoded))) throw new AiContractDecodeError(path, 'an ISO timestamp');
  return decoded;
}

function baseline(value: unknown, path: string): AiProjectBaselineV1 | null {
  if (value === null) return null;
  const source = record(value, path);
  exact(source, ['targetKind', 'projectId', 'persistenceRevision', 'canonicalFlowHash'], path);
  const targetKind = string(source.targetKind, `${path}.targetKind`);
  if (targetKind !== 'new' && targetKind !== 'existing') {
    throw new AiContractDecodeError(`${path}.targetKind`, 'new or existing');
  }
  const projectId = nullableString(source.projectId, `${path}.projectId`, guidPattern);
  const persistenceRevision = source.persistenceRevision === null
    ? null
    : integer(source.persistenceRevision, `${path}.persistenceRevision`);
  const canonicalFlowHash = string(source.canonicalFlowHash, `${path}.canonicalFlowHash`);
  if (targetKind === 'new' && (projectId !== null || persistenceRevision !== null || canonicalFlowHash !== '')) {
    throw new AiContractDecodeError(path, 'a baseline-free new target');
  }
  if (targetKind === 'existing' &&
      (projectId === null || persistenceRevision === null || !hashPattern.test(canonicalFlowHash))) {
    throw new AiContractDecodeError(path, 'a complete existing Project baseline');
  }
  return Object.freeze({ targetKind, projectId, persistenceRevision, canonicalFlowHash });
}

export function decodeAiSessionSnapshotV1(value: unknown, path = '$'): AiSessionSnapshotV1 {
  const source = record(value, path);
  exact(source, [
    'schemaVersion', 'revision', 'projectId', 'lifecycleState', 'planRunId', 'planRunStatus',
    'buildRunId', 'buildRunStatus', 'buildClientOperationId', 'projectBaseline', 'updatedAtUtc'
  ], path);
  return Object.freeze({
    schemaVersion: integer(source.schemaVersion, `${path}.schemaVersion`),
    revision: integer(source.revision, `${path}.revision`),
    projectId: nullableString(source.projectId, `${path}.projectId`, guidPattern),
    lifecycleState: string(source.lifecycleState, `${path}.lifecycleState`, tokenPattern),
    planRunId: nullableString(source.planRunId, `${path}.planRunId`, tokenPattern),
    planRunStatus: nullableString(source.planRunStatus, `${path}.planRunStatus`, tokenPattern),
    buildRunId: nullableString(source.buildRunId, `${path}.buildRunId`, tokenPattern),
    buildRunStatus: nullableString(source.buildRunStatus, `${path}.buildRunStatus`, tokenPattern),
    buildClientOperationId: nullableString(source.buildClientOperationId, `${path}.buildClientOperationId`, guidPattern),
    projectBaseline: baseline(source.projectBaseline, `${path}.projectBaseline`),
    updatedAtUtc: timestamp(source.updatedAtUtc, `${path}.updatedAtUtc`)
  });
}

export function decodeAiSessionDetailV1(value: unknown, path = '$'): AiSessionDetailV1 {
  const source = record(value, path);
  exact(source, ['sessionId', 'snapshot', 'updatedAtUtc'], path);
  return Object.freeze({
    sessionId: string(source.sessionId, `${path}.sessionId`, sessionPattern),
    snapshot: decodeAiSessionSnapshotV1(source.snapshot, `${path}.snapshot`),
    updatedAtUtc: timestamp(source.updatedAtUtc, `${path}.updatedAtUtc`)
  });
}

export function decodeAiOperationProjectionV1(value: unknown, path = '$'): AiOperationProjectionV1 {
  const source = record(value, path);
  exact(source, [
    'clientOperationId', 'kind', 'status', 'sessionId', 'runId', 'payloadFingerprint',
    'projectBaseline', 'errorCode', 'publicMessage', 'createdAtUtc', 'updatedAtUtc', 'expiresAtUtc'
  ], path);
  const kind = string(source.kind, `${path}.kind`) as AiOperationKind;
  const status = string(source.status, `${path}.status`) as AiOperationStatus;
  if (!operationKinds.has(kind)) throw new AiContractDecodeError(`${path}.kind`, 'a supported operation kind');
  if (!operationStatuses.has(status)) throw new AiContractDecodeError(`${path}.status`, 'a supported operation status');
  return Object.freeze({
    clientOperationId: string(source.clientOperationId, `${path}.clientOperationId`, guidPattern),
    kind,
    status,
    sessionId: nullableString(source.sessionId, `${path}.sessionId`, sessionPattern),
    runId: nullableString(source.runId, `${path}.runId`, tokenPattern),
    payloadFingerprint: string(source.payloadFingerprint, `${path}.payloadFingerprint`, hashPattern),
    projectBaseline: baseline(source.projectBaseline, `${path}.projectBaseline`),
    errorCode: nullableString(source.errorCode, `${path}.errorCode`, tokenPattern),
    publicMessage: nullableString(source.publicMessage, `${path}.publicMessage`),
    createdAtUtc: timestamp(source.createdAtUtc, `${path}.createdAtUtc`),
    updatedAtUtc: timestamp(source.updatedAtUtc, `${path}.updatedAtUtc`),
    expiresAtUtc: timestamp(source.expiresAtUtc, `${path}.expiresAtUtc`)
  });
}

export function decodeAiSessionCreateResponseV1(value: unknown): AiSessionCreateResponseV1 {
  const source = record(value, '$');
  exact(source, ['operation', 'session'], '$');
  return Object.freeze({
    operation: decodeAiOperationProjectionV1(source.operation, '$.operation'),
    session: source.session === null ? null : decodeAiSessionDetailV1(source.session, '$.session')
  });
}
