import type { ApiTransport, ApiWriteOptions } from '@/platform/api';
import {
  decodeWorkspaceProjectV1,
  isWorkspaceProjectId,
  type WorkspaceProjectUpdatePayloadV1,
  type WorkspaceProjectV1
} from '../workspaceContracts';

export interface WorkspaceProjectPersistencePort {
  readonly projectId: string;
  getProject(options?: Readonly<{ signal?: AbortSignal }>): Promise<WorkspaceProjectV1>;
  putProject(
    payload: WorkspaceProjectUpdatePayloadV1,
    options?: ApiWriteOptions
  ): Promise<WorkspaceProjectV1>;
}

function projectPath(projectId: string): string {
  if (!isWorkspaceProjectId(projectId)) {
    throw new TypeError('工程保存标识必须是有效的非空 UUID。');
  }
  return `projects/${projectId}`;
}

export function createWorkspaceProjectPersistencePort(
  api: ApiTransport,
  projectId: string
): WorkspaceProjectPersistencePort {
  if (typeof api.put !== 'function') {
    throw new TypeError('工程保存通道不可用。');
  }
  const path = projectPath(projectId);
  const put = api.put.bind(api);

  return Object.freeze({
    projectId,
    async getProject(options: Readonly<{ signal?: AbortSignal }> = {}): Promise<WorkspaceProjectV1> {
      const payload = await api.get<unknown>(path, options);
      return decodeWorkspaceProjectV1(payload);
    },
    async putProject(
      payload: WorkspaceProjectUpdatePayloadV1,
      options: ApiWriteOptions = {}
    ): Promise<WorkspaceProjectV1> {
      const response = await put<unknown>(path, payload, options);
      return decodeWorkspaceProjectV1(response);
    }
  });
}
