import { inject, type InjectionKey } from 'vue';
import type { ProductLeaveGuardOwner } from '@/app/leave';
import type { UiPreferencesOwner } from '@/app/preferences';
import type { SessionProjectionOwner } from '@/app/session';
import type { StudioPlatform } from '@/app/studioPlatform';
import type { ProjectLifecycleCommandOwner } from '@/capabilities/project-lifecycle';
import type { WorkspaceRuntime } from '@/capabilities/project-workspace/workspaceRuntime';
import type { ReadQueryClient } from '@/platform/query';
import type { SystemStatusOwner } from '@/platform/status';

export interface ProductRuntime {
  readonly api: StudioPlatform['api'];
  readonly featureFlags: Readonly<Record<string, boolean>>;
  readonly queries: ReadQueryClient;
  readonly session: SessionProjectionOwner;
  readonly systemStatus: SystemStatusOwner;
  readonly preferences: UiPreferencesOwner;
  readonly projectLifecycle?: ProjectLifecycleCommandOwner;
  readonly leaveGuard: ProductLeaveGuardOwner;
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

export async function createProductRuntime(
  platform: StudioPlatform,
  session: SessionProjectionOwner
): Promise<ProductRuntime> {
  const { buildProductRuntime } = await import('@/app/productRuntimeFactory');
  return buildProductRuntime(platform, session);
}

export function useProductRuntime(): ProductRuntime {
  const runtime = inject(productRuntimeKey);
  if (!runtime) throw new Error('ProductRuntime was not provided by the application composition root.');
  return runtime;
}
