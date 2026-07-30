import type { WorkspaceFlowV1 } from '../workspaceContracts';

export type WorkspaceHandoffArtifactStatus =
  | 'available'
  | 'consuming'
  | 'consumed'
  | 'expired'
  | 'rejected';

export interface WorkspaceHandoffBaselineV1 {
  readonly targetKind: 'new' | 'existing';
  readonly projectId: string | null;
  readonly persistenceRevision: number | null;
  readonly canonicalFlowHash: string;
}

export interface WorkspaceHandoffDiffV1 {
  readonly addedNodes: readonly string[];
  readonly modifiedNodes: readonly string[];
  readonly removedNodes: readonly string[];
  readonly addedOrChangedParameters: readonly string[];
  readonly missingResources: readonly string[];
}

export interface WorkspaceHandoffParameterSummaryV1 {
  readonly canonicalKey: string;
  readonly operatorLabel: string;
  readonly parameterLabel: string;
  readonly valueSummary: string;
  readonly resourceDependent: boolean;
}

export interface WorkspaceHandoffBuildSummaryV1 {
  readonly buildId: string;
  readonly buildIdentity: string;
  readonly candidateFlowFingerprint: string;
  readonly operatorCount: number;
  readonly connectionCount: number;
  readonly handoffEligible: boolean;
  readonly applyGateReady: boolean;
  readonly blockerCount: number;
  readonly warningCount: number;
  readonly diff: WorkspaceHandoffDiffV1;
  readonly parameters: readonly WorkspaceHandoffParameterSummaryV1[];
}

export interface WorkspaceHandoffArtifactV1 {
  readonly schemaVersion: 1;
  readonly artifactId: string;
  readonly clientOperationId: string;
  readonly sessionId: string;
  readonly sessionRevision: number;
  readonly planRunId: string;
  readonly planId: string;
  readonly planHash: string;
  readonly buildRunId: string;
  readonly buildClientOperationId: string;
  readonly buildIdentity: string;
  readonly targetKind: 'new' | 'existing';
  readonly projectBaseline: WorkspaceHandoffBaselineV1;
  readonly candidateFlow: WorkspaceFlowV1;
  readonly candidateFlowFingerprint: string;
  readonly build: WorkspaceHandoffBuildSummaryV1;
  readonly createdAtUtc: string;
  readonly expiresAtUtc: string;
  readonly status: WorkspaceHandoffArtifactStatus;
  readonly consumeClientOperationId: string | null;
}

export interface WorkspaceHandoffSourceV1 {
  readonly artifactId: string;
  readonly sessionId: string;
  readonly planId: string;
  readonly buildId: string;
  readonly candidateFlowFingerprint: string;
  readonly targetKind: 'new' | 'existing';
  readonly receivedAtUtc: string;
}

export type WorkspaceHandoffReceivePhase =
  | 'idle'
  | 'workspace-loading-artifact'
  | 'workspace-dirty-conflict'
  | 'artifact-expired'
  | 'artifact-consumed'
  | 'artifact-baseline-conflict'
  | 'workspace-staging'
  | 'workspace-staged-unsaved'
  | 'error'
  | 'disposed';

export interface WorkspaceHandoffReceiveProjection {
  readonly phase: WorkspaceHandoffReceivePhase;
  readonly message: string;
  readonly blocker: string | null;
  readonly nextStep: string;
  readonly inFlightCount: number;
}

export class WorkspaceHandoffContractError extends Error {
  readonly path: string;

  constructor(path: string, expected: string) {
    super(`${path} must be ${expected}.`);
    this.name = 'WorkspaceHandoffContractError';
    this.path = path;
  }
}
