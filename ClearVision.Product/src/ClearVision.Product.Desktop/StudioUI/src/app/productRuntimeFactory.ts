import {
  createProductLeaveGuardOwner,
  type ProductLeaveGuardOwner
} from '@/app/leave';
import type { UiPreferencesOwner } from '@/app/preferences';
import { sessionIdentityOf, type SessionProjectionOwner } from '@/app/session';
import type { StudioPlatform } from '@/app/studioPlatform';
import type { ProductRuntime, ProductRuntimeQuarantine } from '@/app/productRuntime';
import { createProjectLifecycleCommandOwner } from '@/capabilities/project-lifecycle';
import { createWorkspaceRuntime } from '@/capabilities/project-workspace/workspaceRuntime';
import { createReadQueryClient } from '@/platform/query';
import { createSystemStatusOwner } from '@/platform/status';

export function buildProductRuntime(
  platform: StudioPlatform,
  session: SessionProjectionOwner,
  preferences: UiPreferencesOwner
): ProductRuntime {
  const queries = createReadQueryClient(platform.api);
  const authenticatedUser = session.projection.user;
  if (!authenticatedUser) throw new Error('ProductRuntime requires an authenticated session projection.');
  queries.setSessionIdentity(sessionIdentityOf(authenticatedUser));
  const systemStatus = createSystemStatusOwner({ queries });
  const leaveGuardHolder: { current?: ProductLeaveGuardOwner } = {};
  const projectLifecycle = authenticatedUser.role === 'Admin' || authenticatedUser.role === 'Engineer'
    ? createProjectLifecycleCommandOwner({
        api: platform.api,
        prepareProjectLeave(projectId) {
          return leaveGuardHolder.current?.request('project-delete', projectId) ?? Promise.resolve(true);
        }
      })
    : undefined;
  const workspace = createWorkspaceRuntime({
    queries,
    api: platform.api,
    session,
    featureFlags: platform.startup.featureFlags
  });
  const leaveGuard = createProductLeaveGuardOwner({
    workspace,
    ...(projectLifecycle ? { projectLifecycle } : {})
  });
  leaveGuardHolder.current = leaveGuard;
  let disposed = false;
  let quarantined = false;
  systemStatus.start();

  return Object.freeze({
    api: platform.api,
    featureFlags: platform.startup.featureFlags,
    queries,
    session,
    systemStatus,
    preferences,
    ...(projectLifecycle ? { projectLifecycle } : {}),
    leaveGuard,
    workspace,
    prepareForProtectedTransition(reason: 'logout' | 'change-password'): Promise<boolean> {
      if (disposed || quarantined) return Promise.resolve(false);
      return leaveGuard.request(reason);
    },
    quarantineForSessionExpiration(): ProductRuntimeQuarantine {
      if (disposed) return Object.freeze({
        requiresPreservation: false,
        activeWorkspaceOwnerCount: 0,
        runIdentities: Object.freeze([])
      });
      quarantined = true;
      leaveGuard.suspendForSessionExpiration();
      const projectLifecycleRequiresPreservation = projectLifecycle?.quarantineForSessionExpiration() ?? false;
      const result = workspace.quarantineForSessionExpiration();
      systemStatus.dispose();
      return Object.freeze({
        requiresPreservation: projectLifecycleRequiresPreservation || result.activeOwnerCount > 0 ||
          result.activeNewDraftOwnerCount > 0 || result.activeHandoffReceiverCount > 0,
        activeWorkspaceOwnerCount: result.activeOwnerCount,
        runIdentities: result.runIdentities
      });
    },
    async reconcileAfterReauthentication(): Promise<boolean> {
      if (disposed) return false;
      const projectLifecycleReconciled = projectLifecycle
        ? await projectLifecycle.reconcileAfterReauthentication()
        : true;
      if (!projectLifecycleReconciled) return false;
      const reconciled = await workspace.reconcileAfterReauthentication();
      if (reconciled) quarantined = false;
      return reconciled;
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      leaveGuard.dispose('product-runtime-disposed');
      workspace.dispose();
      if (projectLifecycle) {
        projectLifecycle.dispose('product-runtime-disposed');
      }
      systemStatus.dispose();
      queries.dispose();
    }
  });
}
