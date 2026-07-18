import { inject, type InjectionKey } from 'vue';
import { createUiPreferencesOwner, type UiPreferencesOwner } from '@/app/preferences';
import { sessionIdentityOf, type SessionProjectionOwner } from '@/app/session';
import type { StudioPlatform } from '@/app/studioPlatform';
import {
  createProjectLifecycleCommandOwner,
  type ProjectLifecycleCommandOwner
} from '@/capabilities/project-lifecycle';
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
  readonly projectLifecycle: ProjectLifecycleCommandOwner;
  readonly workspace: WorkspaceRuntime;
  prepareForProtectedTransition(reason: 'logout' | 'change-password'): Promise<boolean>;
  quarantineForSessionExpiration(): ProductRuntimeQuarantine;
  reconcileAfterReauthentication(): Promise<boolean>;
  dispose(): void;
}

export interface ProductRuntimeQuarantine {
  readonly requiresPreservation: boolean;
  readonly activeWorkspaceOwnerCount: number;
  readonly runIdentities: ReturnType<WorkspaceRuntime['quarantineForSessionExpiration']>['runIdentities'];
}

export const productRuntimeKey: InjectionKey<ProductRuntime> = Symbol('ProductRuntime');

export function createProductRuntime(
  platform: StudioPlatform,
  session: SessionProjectionOwner
): ProductRuntime {
  const queries = createReadQueryClient(platform.api);
  const authenticatedUser = session.projection.user;
  if (!authenticatedUser) throw new Error('ProductRuntime requires an authenticated session projection.');
  queries.setSessionIdentity(sessionIdentityOf(authenticatedUser));
  const preferences = createUiPreferencesOwner();
  const systemStatus = createSystemStatusOwner({ queries });
  const workspaceHolder: { current?: WorkspaceRuntime } = {};
  const projectLifecycle = createProjectLifecycleCommandOwner({
    api: platform.api,
    prepareProjectLeave(projectId, reason) {
      return workspaceHolder.current?.prepareForProjectTransition(projectId, reason) ?? Promise.resolve(true);
    }
  });
  const workspace = createWorkspaceRuntime({
    queries,
    api: platform.api,
    session,
    featureFlags: platform.startup.featureFlags
  });
  workspaceHolder.current = workspace;
  let disposed = false;
  let quarantined = false;
  systemStatus.start();

  return Object.freeze({
    queries,
    session,
    systemStatus,
    preferences,
    projectLifecycle,
    workspace,
    prepareForProtectedTransition(reason: 'logout' | 'change-password'): Promise<boolean> {
      if (disposed || quarantined) return Promise.resolve(false);
      return projectLifecycle.prepareForProtectedTransition(`auth-${reason}`).then(projectReady =>
        projectReady ? workspace.prepareForProtectedTransition(reason) : false);
    },
    quarantineForSessionExpiration(): ProductRuntimeQuarantine {
      if (disposed) return Object.freeze({
        requiresPreservation: false,
        activeWorkspaceOwnerCount: 0,
        runIdentities: Object.freeze([])
      });
      quarantined = true;
      const projectLifecycleRequiresPreservation = projectLifecycle.quarantineForSessionExpiration();
      const result = workspace.quarantineForSessionExpiration();
      systemStatus.dispose();
      return Object.freeze({
        requiresPreservation: projectLifecycleRequiresPreservation || result.activeOwnerCount > 0,
        activeWorkspaceOwnerCount: result.activeOwnerCount,
        runIdentities: result.runIdentities
      });
    },
    async reconcileAfterReauthentication(): Promise<boolean> {
      if (disposed) return false;
      const projectLifecycleReconciled = await projectLifecycle.reconcileAfterReauthentication();
      if (!projectLifecycleReconciled) return false;
      const reconciled = await workspace.reconcileAfterReauthentication();
      if (reconciled) quarantined = false;
      return reconciled;
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      workspace.dispose();
      projectLifecycle.dispose('product-runtime-disposed');
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
