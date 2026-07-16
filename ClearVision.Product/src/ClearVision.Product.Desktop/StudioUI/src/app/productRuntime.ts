import { inject, type InjectionKey } from 'vue';
import { createUiPreferencesOwner, type UiPreferencesOwner } from '@/app/preferences';
import { createSessionProjectionOwner, type SessionProjectionOwner } from '@/app/session';
import type { StudioPlatform } from '@/app/studioPlatform';
import {
  createWorkspaceRuntime,
  type WorkspaceRuntime
} from '@/capabilities/project-workspace/workspaceRuntime';
import { createReadQueryClient, type ReadQueryClient } from '@/platform/query';
import { createSystemStatusOwner, type SystemStatusOwner } from '@/platform/status';

export interface ProductRuntime {
  readonly queries: ReadQueryClient;
  readonly session: SessionProjectionOwner;
  readonly systemStatus: SystemStatusOwner;
  readonly preferences: UiPreferencesOwner;
  readonly workspace: WorkspaceRuntime;
  dispose(): void;
}

export const productRuntimeKey: InjectionKey<ProductRuntime> = Symbol('ProductRuntime');

export function createProductRuntime(platform: StudioPlatform): ProductRuntime {
  const queries = createReadQueryClient(platform.api);
  const preferences = createUiPreferencesOwner();
  const session = createSessionProjectionOwner({
    queries,
    hasToken: platform.hasToken
  });
  const systemStatus = createSystemStatusOwner({ queries });
  const workspace = createWorkspaceRuntime({
    queries,
    session,
    featureFlags: platform.startup.featureFlags
  });
  let disposed = false;
  session.start();
  systemStatus.start();

  return Object.freeze({
    queries,
    session,
    systemStatus,
    preferences,
    workspace,
    dispose(): void {
      if (disposed) return;
      disposed = true;
      workspace.dispose();
      session.dispose();
      systemStatus.dispose();
      queries.dispose();
      preferences.dispose();
    }
  });
}

export function useProductRuntime(): ProductRuntime {
  const runtime = inject(productRuntimeKey);
  if (!runtime) throw new Error('ProductRuntime was not provided by the application composition root.');
  return runtime;
}
