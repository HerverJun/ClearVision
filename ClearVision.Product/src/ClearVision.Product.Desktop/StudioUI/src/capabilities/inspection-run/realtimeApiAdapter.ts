import type { ApiGetOptions, ApiTransport } from '@/platform/api';
import { decodeInspectionRunStart, decodeInspectionRunState, type InspectionRunIdentity, type InspectionRunStartResult, type InspectionRunState } from './contracts';

export interface InspectionRunApiPort {
  hydrate(projectId: string, options?: ApiGetOptions): Promise<InspectionRunState>;
  start(identity: InspectionRunIdentity, cameraId: string | null, options?: ApiGetOptions): Promise<InspectionRunStartResult>;
  stop(identity: InspectionRunIdentity, options?: ApiGetOptions): Promise<void>;
}

export function createInspectionRunApiAdapter(api: ApiTransport): InspectionRunApiPort {
  if (!api.post) throw new Error('Inspection run requires the shared API write transport.');
  const post = api.post.bind(api);
  return Object.freeze({
    async hydrate(projectId: string, options: ApiGetOptions = {}) {
      return decodeInspectionRunState(await api.get(`inspection/realtime/${encodeURIComponent(projectId)}/state`, options));
    },
    async start(identity: InspectionRunIdentity, cameraId: string | null, options: ApiGetOptions = {}) {
      const response = await post('inspection/realtime/start', {
        projectId: identity.projectId,
        clientSnapshotId: identity.clientSnapshotId,
        expectedPersistenceRevision: identity.expectedPersistenceRevision,
        expectedCanonicalFlowHash: identity.expectedCanonicalFlowHash,
        expectedDecisionConfigurationHash: identity.expectedDecisionConfigurationHash,
        cameraId,
        runMode: 'canonical-project'
      }, options);
      return decodeInspectionRunStart(response);
    },
    async stop(identity: InspectionRunIdentity, options: ApiGetOptions = {}) {
      await post('inspection/realtime/stop', identity, options);
    }
  });
}
