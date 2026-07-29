export type AiOperationKind = 'session_create' | 'session_delete' | 'plan_run' | 'build_run';
export type AiOperationStatus = 'pending' | 'created' | 'failed' | 'rejected';

export interface AiProjectBaselineV1 {
  readonly targetKind: 'new' | 'existing';
  readonly projectId: string | null;
  readonly persistenceRevision: number | null;
  readonly canonicalFlowHash: string;
}

export interface AiSessionSnapshotV1 {
  readonly schemaVersion: number;
  readonly revision: number;
  readonly projectId: string | null;
  readonly lifecycleState: string;
  readonly planRunId: string | null;
  readonly planRunStatus: string | null;
  readonly buildRunId: string | null;
  readonly buildRunStatus: string | null;
  readonly buildClientOperationId: string | null;
  readonly projectBaseline: AiProjectBaselineV1 | null;
  readonly updatedAtUtc: string;
}

export interface AiSessionDetailV1 {
  readonly sessionId: string;
  readonly snapshot: AiSessionSnapshotV1;
  readonly updatedAtUtc: string;
}

export interface AiOperationProjectionV1 {
  readonly clientOperationId: string;
  readonly kind: AiOperationKind;
  readonly status: AiOperationStatus;
  readonly sessionId: string | null;
  readonly runId: string | null;
  readonly payloadFingerprint: string;
  readonly projectBaseline: AiProjectBaselineV1 | null;
  readonly errorCode: string | null;
  readonly publicMessage: string | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly expiresAtUtc: string;
}

export interface AiSessionCreateResponseV1 {
  readonly operation: AiOperationProjectionV1;
  readonly session: AiSessionDetailV1 | null;
}

export interface AiSessionCreateCommandV1 {
  readonly clientOperationId: string;
  readonly projectId?: string;
}

export interface AiWorkspaceSnapshotMutationV1 {
  readonly expectedRevision: number;
  readonly clientMutationId: string;
  readonly projectId?: string | null;
  readonly lifecycleState?: string;
}

export class AiContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expected: string) {
    super(`${path} must be ${expected}.`);
    this.name = 'AiContractDecodeError';
    this.path = path;
  }
}
