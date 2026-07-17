import type { ApiTransport, ApiWriteOptions } from '@/platform/api';
import { decodeInspectionOutcome, type InspectionOutcome } from '@/shared/inspectionOutcome';

export class WorkspaceRunContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expectation: string) {
    super(`Workspace Run response ${path} must be ${expectation}.`);
    this.name = 'WorkspaceRunContractDecodeError';
    this.path = path;
  }
}

export interface WorkspaceRunAdmissionRequestV1 {
  readonly projectId: string;
  readonly clientSnapshotId: string;
  readonly expectedPersistenceRevision: number;
}

export interface WorkspaceRunAdmissionV1 {
  readonly allowed: boolean;
  readonly code: string | null;
  readonly message: string;
  readonly projectId: string;
  readonly clientSnapshotId: string;
  readonly persistenceRevision: number | null;
  readonly canonicalFlowHash: string | null;
  readonly decisionConfigurationHash: string | null;
  readonly violations: readonly unknown[];
}

export interface WorkspaceRunExecuteRequestV1 {
  readonly projectId: string;
  readonly clientSnapshotId: string;
  readonly expectedPersistenceRevision: number;
  readonly expectedCanonicalFlowHash: string;
  readonly expectedDecisionConfigurationHash: string;
}

export interface WorkspaceRunResultV1 {
  readonly id: string;
  readonly projectId: string;
  readonly status: string;
  readonly outcome: InspectionOutcome;
  readonly executionSnapshotId: string;
  readonly persistenceRevision: number;
  readonly flowHash: string | null;
  readonly decisionConfigurationHash: string | null;
  readonly errorMessage: string | null;
}

export interface WorkspaceRunPort {
  readonly projectId: string;
  admit(payload: WorkspaceRunAdmissionRequestV1, options?: ApiWriteOptions): Promise<WorkspaceRunAdmissionV1>;
  execute(payload: WorkspaceRunExecuteRequestV1, options?: ApiWriteOptions): Promise<WorkspaceRunResultV1>;
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const emptyUuid = '00000000-0000-0000-0000-000000000000';

function record(value: unknown, path: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new WorkspaceRunContractDecodeError(path, 'an object');
  }
  return value as Record<string, unknown>;
}

function uuid(value: unknown, path: string): string {
  if (typeof value !== 'string' || !uuidPattern.test(value) || value.toLowerCase() === emptyUuid) {
    throw new WorkspaceRunContractDecodeError(path, 'a non-empty UUID');
  }
  return value;
}

function string(value: unknown, path: string): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new WorkspaceRunContractDecodeError(path, 'a non-empty string');
  }
  return value;
}

function nullableString(value: unknown, path: string): string | null {
  if (value === null || value === undefined) return null;
  return string(value, path);
}

function nonNegativeInteger(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new WorkspaceRunContractDecodeError(path, 'a non-negative safe integer');
  }
  return value;
}

export function decodeWorkspaceRunAdmissionV1(payload: unknown): WorkspaceRunAdmissionV1 {
  const source = record(payload, '$');
  if (typeof source.allowed !== 'boolean') {
    throw new WorkspaceRunContractDecodeError('$.allowed', 'a boolean');
  }
  const allowed = source.allowed;
  const violations = source.violations;
  if (!Array.isArray(violations)) throw new WorkspaceRunContractDecodeError('$.violations', 'an array');
  const persistenceRevision = source.projectPersistenceRevision;
  return Object.freeze({
    allowed,
    code: nullableString(source.code, '$.code'),
    message: string(source.message, '$.message'),
    projectId: uuid(source.projectId, '$.projectId'),
    clientSnapshotId: uuid(source.clientSnapshotId, '$.clientSnapshotId'),
    persistenceRevision: persistenceRevision === null || persistenceRevision === undefined
      ? null
      : nonNegativeInteger(persistenceRevision, '$.projectPersistenceRevision'),
    canonicalFlowHash: nullableString(source.canonicalFlowHash, '$.canonicalFlowHash'),
    decisionConfigurationHash: nullableString(source.decisionConfigurationHash, '$.decisionConfigurationHash'),
    violations: Object.freeze([...violations])
  });
}

export function decodeWorkspaceRunResultV1(payload: unknown): WorkspaceRunResultV1 {
  const source = record(payload, '$');
  return Object.freeze({
    id: uuid(source.id, '$.id'),
    projectId: uuid(source.projectId, '$.projectId'),
    status: string(source.status, '$.status'),
    outcome: decodeInspectionOutcome(source.executionOutcome, source.decisionOutcome, '$'),
    executionSnapshotId: uuid(source.executionSnapshotId, '$.executionSnapshotId'),
    persistenceRevision: nonNegativeInteger(source.projectPersistenceRevision, '$.projectPersistenceRevision'),
    flowHash: nullableString(source.flowVersionHash, '$.flowVersionHash'),
    decisionConfigurationHash: nullableString(
      source.decisionConfigurationHash,
      '$.decisionConfigurationHash'
    ),
    errorMessage: nullableString(source.errorMessage, '$.errorMessage')
  });
}

function assertProjectId(projectId: string): void {
  uuid(projectId, 'projectId');
}

export function createWorkspaceRunPort(api: ApiTransport, projectId: string): WorkspaceRunPort {
  assertProjectId(projectId);
  if (typeof api.post !== 'function') {
    throw new TypeError('Workspace Run requires POST on the shared ApiTransport.');
  }
  const post = api.post.bind(api);
  return Object.freeze({
    projectId,
    async admit(payload: WorkspaceRunAdmissionRequestV1, options: ApiWriteOptions = {}): Promise<WorkspaceRunAdmissionV1> {
      if (payload.projectId !== projectId) throw new TypeError('Workspace Run admission project identity changed.');
      const response = await post<unknown>('inspection/admission', payload, options);
      return decodeWorkspaceRunAdmissionV1(response);
    },
    async execute(payload: WorkspaceRunExecuteRequestV1, options: ApiWriteOptions = {}): Promise<WorkspaceRunResultV1> {
      if (payload.projectId !== projectId) throw new TypeError('Workspace Run execute project identity changed.');
      const response = await post<unknown>('inspection/execute', payload, options);
      return decodeWorkspaceRunResultV1(response);
    }
  });
}
