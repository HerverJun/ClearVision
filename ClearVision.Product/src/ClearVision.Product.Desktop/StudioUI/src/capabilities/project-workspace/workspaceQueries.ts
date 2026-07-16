import type {
  ReadQueryClient,
  ReadQueryDefinition,
  ReadQueryRefreshOptions,
  ReadQueryState
} from '@/platform/query';
import {
  decodeWorkspaceProjectV1,
  isWorkspaceProjectId,
  type WorkspaceProjectV1
} from './workspaceContracts';
import type {
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceReadDiagnosticsLease
} from './workspaceLifecycleDiagnostics';

export interface WorkspaceProjectReadPort {
  readonly projectId: string;
  readonly state: Readonly<{ readonly value: ReadQueryState<WorkspaceProjectV1> }>;
  refresh(options?: ReadQueryRefreshOptions): Promise<ReadQueryState<WorkspaceProjectV1>>;
  dispose(reason?: string): void;
}

export function createWorkspaceProjectPath(projectId: string): string {
  if (!isWorkspaceProjectId(projectId)) {
    throw new TypeError('Workspace project id must be a non-empty UUID.');
  }
  return `projects/${projectId}`;
}

export function createWorkspaceProjectDefinition(
  client: ReadQueryClient,
  projectId: string
): ReadQueryDefinition<WorkspaceProjectV1> {
  const path = createWorkspaceProjectPath(projectId);
  return Object.freeze({
    key: () => `workspace-project:${client.sessionGeneration}:${projectId}`,
    path,
    decode: decodeWorkspaceProjectV1,
    protected: true,
    cacheTimeMs: 0
  });
}

export function createWorkspaceProjectReadPort(
  client: ReadQueryClient,
  diagnostics: WorkspaceLifecycleDiagnosticsOwner,
  projectId: string
): WorkspaceProjectReadPort {
  const definition = createWorkspaceProjectDefinition(client, projectId);
  const query = client.createQuery(definition);
  const lease: WorkspaceReadDiagnosticsLease = diagnostics.reserveRead(projectId);
  let disposed = false;

  return Object.freeze({
    projectId,
    state: query.state,
    async refresh(options: ReadQueryRefreshOptions = {}): Promise<ReadQueryState<WorkspaceProjectV1>> {
      if (disposed) throw new Error('The Workspace project read port has been disposed.');
      const requestToken = lease.startRequest();
      try {
        return await query.refresh(options);
      } finally {
        lease.settleRequest(requestToken);
      }
    },
    dispose(reason = 'workspace-read-disposed'): void {
      if (disposed) return;
      disposed = true;
      try {
        query.dispose();
      } finally {
        lease.dispose(reason);
      }
    }
  });
}
