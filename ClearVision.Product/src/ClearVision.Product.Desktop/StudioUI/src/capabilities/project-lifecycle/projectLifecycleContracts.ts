import {
  decodeProjectDetails,
  isProjectId,
  type ProjectDetails
} from '@/capabilities/projects-read/projectContracts';

export type ProjectLifecycleOperationKind = 'create' | 'delete';
export type ProjectLifecycleOperationStatus =
  | 'pending'
  | 'completed'
  | 'failed-retryable'
  | 'failed-terminal';
export type ProjectLifecycleCleanupStatus =
  | 'not-required'
  | 'cleanup-pending'
  | 'cleanup-completed'
  | 'cleanup-failed-retryable';

export class ProjectLifecycleContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expectation: string) {
    super(`Project lifecycle response field ${path} must be ${expectation}.`);
    this.name = 'ProjectLifecycleContractDecodeError';
    this.path = path;
  }
}

export interface ProjectLifecycleOperationResult {
  readonly project: ProjectDetails | null;
  readonly projectDeleted: boolean;
  readonly deleted: boolean;
  readonly alreadyDeleted: boolean;
  readonly cleanupStatus: ProjectLifecycleCleanupStatus | null;
}

export interface ProjectLifecycleOperation {
  readonly clientOperationId: string;
  readonly kind: ProjectLifecycleOperationKind;
  readonly status: ProjectLifecycleOperationStatus;
  readonly projectId: string | null;
  readonly result: ProjectLifecycleOperationResult | null;
  readonly errorCode: string | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly expiresAtUtc: string | null;
}

export interface ProjectCreateAuthorityResult {
  readonly projectId: string;
  readonly project: ProjectDetails;
  readonly operationReplayed: boolean;
  readonly operation: ProjectLifecycleOperation;
}

export interface ProjectDeleteAuthorityResult {
  readonly projectId: string;
  readonly operationReplayed: boolean;
  readonly operation: ProjectLifecycleOperation;
}

export interface ProjectOpenAuthorityResult {
  readonly projectId: string;
  readonly lastOpenedAtUtc: string;
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const emptyUuid = '00000000-0000-0000-0000-000000000000';

function record(value: unknown, path: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new ProjectLifecycleContractDecodeError(path, 'an object');
  }
  return value as Record<string, unknown>;
}

function text(value: unknown, path: string): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new ProjectLifecycleContractDecodeError(path, 'a non-empty string');
  }
  return value;
}

function nullableText(value: unknown, path: string): string | null {
  if (value === null) return null;
  return text(value, path);
}

function uuid(value: unknown, path: string): string {
  const decoded = text(value, path);
  if (!uuidPattern.test(decoded) || decoded.toLowerCase() === emptyUuid) {
    throw new ProjectLifecycleContractDecodeError(path, 'a non-empty UUID');
  }
  return decoded;
}

function nullableUuid(value: unknown, path: string): string | null {
  if (value === null) return null;
  return uuid(value, path);
}

function boolean(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') {
    throw new ProjectLifecycleContractDecodeError(path, 'a boolean');
  }
  return value;
}

function dateTime(value: unknown, path: string): string {
  const decoded = text(value, path);
  if (Number.isNaN(Date.parse(decoded))) {
    throw new ProjectLifecycleContractDecodeError(path, 'an ISO date-time string');
  }
  return decoded;
}

function nullableDateTime(value: unknown, path: string): string | null {
  if (value === null) return null;
  return dateTime(value, path);
}

function oneOf<T extends string>(value: unknown, path: string, values: readonly T[]): T {
  const decoded = text(value, path) as T;
  if (!values.includes(decoded)) {
    throw new ProjectLifecycleContractDecodeError(path, values.join(' | '));
  }
  return decoded;
}

function decodeOperationResult(value: unknown, path: string): ProjectLifecycleOperationResult | null {
  if (value === null) return null;
  const source = record(value, path);
  const cleanupStatus = source.cleanupStatus === null
    ? null
    : oneOf(source.cleanupStatus, `${path}.cleanupStatus`, [
        'not-required',
        'cleanup-pending',
        'cleanup-completed',
        'cleanup-failed-retryable'
      ] as const);
  return Object.freeze({
    project: source.project === null ? null : decodeProjectDetails(source.project),
    projectDeleted: boolean(source.projectDeleted, `${path}.projectDeleted`),
    deleted: boolean(source.deleted, `${path}.deleted`),
    alreadyDeleted: boolean(source.alreadyDeleted, `${path}.alreadyDeleted`),
    cleanupStatus
  });
}

export function isLifecycleOperationId(value: string): boolean {
  return uuidPattern.test(value) && value.toLowerCase() !== emptyUuid;
}

export function decodeProjectLifecycleOperation(payload: unknown): ProjectLifecycleOperation {
  const source = record(payload, '$');
  return Object.freeze({
    clientOperationId: uuid(source.clientOperationId, '$.clientOperationId'),
    kind: oneOf(source.kind, '$.kind', ['create', 'delete'] as const),
    status: oneOf(source.status, '$.status', [
      'pending',
      'completed',
      'failed-retryable',
      'failed-terminal'
    ] as const),
    projectId: nullableUuid(source.projectId, '$.projectId'),
    result: decodeOperationResult(source.result, '$.result'),
    errorCode: nullableText(source.errorCode, '$.errorCode'),
    createdAtUtc: dateTime(source.createdAtUtc, '$.createdAtUtc'),
    updatedAtUtc: dateTime(source.updatedAtUtc, '$.updatedAtUtc'),
    expiresAtUtc: nullableDateTime(source.expiresAtUtc, '$.expiresAtUtc')
  });
}

export function decodeProjectCreateAuthorityResult(payload: unknown): ProjectCreateAuthorityResult {
  const source = record(payload, '$');
  const projectId = uuid(source.projectId, '$.projectId');
  const project = decodeProjectDetails(source.project);
  if (project.id !== projectId) {
    throw new ProjectLifecycleContractDecodeError('$.project.id', 'the server-issued projectId');
  }
  const operation = decodeProjectLifecycleOperation(source.operation);
  if (operation.kind !== 'create' || operation.projectId !== projectId) {
    throw new ProjectLifecycleContractDecodeError('$.operation', 'the matching create authority');
  }
  return Object.freeze({
    projectId,
    project,
    operationReplayed: boolean(source.operationReplayed, '$.operationReplayed'),
    operation
  });
}

export function decodeProjectDeleteAuthorityResult(payload: unknown): ProjectDeleteAuthorityResult {
  const source = record(payload, '$');
  const projectId = uuid(source.projectId, '$.projectId');
  const operation = decodeProjectLifecycleOperation(source.operation);
  if (operation.kind !== 'delete' || operation.projectId !== projectId) {
    throw new ProjectLifecycleContractDecodeError('$.operation', 'the matching delete authority');
  }
  return Object.freeze({
    projectId,
    operationReplayed: boolean(source.operationReplayed, '$.operationReplayed'),
    operation
  });
}

export function decodeProjectOpenAuthorityResult(payload: unknown): ProjectOpenAuthorityResult {
  const source = record(payload, '$');
  const projectId = uuid(source.projectId, '$.projectId');
  if (!isProjectId(projectId)) {
    throw new ProjectLifecycleContractDecodeError('$.projectId', 'a Project UUID');
  }
  return Object.freeze({
    projectId,
    lastOpenedAtUtc: dateTime(source.lastOpenedAtUtc, '$.lastOpenedAtUtc')
  });
}
