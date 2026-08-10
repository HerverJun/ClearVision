import { inject, readonly, shallowRef, type DeepReadonly, type InjectionKey, type ShallowRef } from 'vue';
import type { Router } from 'vue-router';
import type { ProductLeaveGuardOwner } from '@/app/leave';
import { createUiPreferencesOwner, type UiPreferencesOwner } from '@/app/preferences';
import { createProductRuntime, type ProductRuntime } from '@/app/productRuntime';
import { sessionIdentityOf, type SessionUserProjection } from '@/app/session';
import type { StudioPlatform } from '@/app/studioPlatform';
import {
  createAuthLifecycleOwner,
  type AuthLifecycleOwner,
  type AuthRuntimeTransitions
} from './authLifecycleOwner';

export interface AuthLifecycleRoot {
  readonly auth: AuthLifecycleOwner;
  readonly preferences: UiPreferencesOwner;
  readonly productRuntime: DeepReadonly<ShallowRef<ProductRuntime | null>>;
  getProductLeaveGuard(): ProductLeaveGuardOwner | null;
  bindRouter(router: Router): void;
  start(): Promise<void>;
  dispose(): void;
}

export const authLifecycleRootKey: InjectionKey<AuthLifecycleRoot> = Symbol('AuthLifecycleRoot');

export function createAuthLifecycleRoot(
  platform: StudioPlatform,
  preferences: UiPreferencesOwner = createUiPreferencesOwner()
): AuthLifecycleRoot {
  const productRuntime = shallowRef<ProductRuntime | null>(null);
  let quarantinedRuntime: ProductRuntime | null = null;
  let router: Router | undefined;
  let disposed = false;
  const authHolder: { current?: AuthLifecycleOwner } = {};

  function disposeRuntime(reason: string): void {
    void reason;
    const active = productRuntime.value;
    productRuntime.value = null;
    active?.dispose();
    if (quarantinedRuntime && quarantinedRuntime !== active) quarantinedRuntime.dispose();
    quarantinedRuntime = null;
  }

  const transitions: AuthRuntimeTransitions = Object.freeze({
    prepareForProtectedTransition(reason: 'logout' | 'change-password'): Promise<boolean> {
      return productRuntime.value?.prepareForProtectedTransition(reason) ?? Promise.resolve(true);
    },
    async activateAuthenticatedSession(
      user: SessionUserProjection,
      generation: number
    ): Promise<boolean> {
      if (disposed) return false;
      if (quarantinedRuntime) {
        const reconciled = await quarantinedRuntime.reconcileAfterReauthentication();
        if (!reconciled || disposed) return false;
        productRuntime.value = quarantinedRuntime;
        quarantinedRuntime = null;
        return true;
      }
      if (productRuntime.value) return true;
      const currentAuth = authHolder.current;
      if (!currentAuth) return false;
      const nextRuntime = await createProductRuntime(platform, currentAuth.session, preferences);
      const currentSession = currentAuth.session.projection;
      const activationIsCurrent = currentSession.sessionGeneration === generation &&
        currentSession.user !== null && sessionIdentityOf(currentSession.user) === sessionIdentityOf(user);
      if (disposed || !activationIsCurrent || productRuntime.value !== null) {
        nextRuntime.dispose();
        return !disposed && activationIsCurrent && productRuntime.value !== null;
      }
      productRuntime.value = nextRuntime;
      return true;
    },
    endAuthenticatedSession(reason: 'logout' | 'change-password'): void {
      disposeRuntime(reason);
    },
    async expireAuthenticatedSession(): Promise<void> {
      const active = productRuntime.value;
      productRuntime.value = null;
      if (!active) return;
      const quarantine = active.quarantineForSessionExpiration();
      if (quarantine.requiresPreservation) {
        quarantinedRuntime = active;
      } else {
        active.dispose();
      }
    },
    navigateToLogin(reason: 'logout' | 'change-password' | 'expired'): Promise<void> | void {
      if (!router || disposed) return;
      return router.replace({ path: '/login', query: { reason } }).then(() => undefined);
    }
  });

  const auth = createAuthLifecycleOwner({
    api: platform.api,
    tokenPort: platform.tokenPort,
    runtime: transitions
  });
  authHolder.current = auth;

  return Object.freeze({
    auth,
    preferences,
    productRuntime: readonly(productRuntime),
    getProductLeaveGuard(): ProductLeaveGuardOwner | null {
      return productRuntime.value?.leaveGuard ?? quarantinedRuntime?.leaveGuard ?? null;
    },
    bindRouter(nextRouter: Router): void {
      if (router && router !== nextRouter) throw new Error('AuthLifecycleRoot router is already bound.');
      router = nextRouter;
    },
    start(): Promise<void> {
      if (disposed) return Promise.resolve();
      return auth.start();
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      auth.dispose();
      delete authHolder.current;
      disposeRuntime('auth-root-disposed');
      preferences.dispose();
      router = undefined;
    }
  });
}

export function useAuthLifecycleRoot(): AuthLifecycleRoot {
  const root = inject(authLifecycleRootKey);
  if (!root) throw new Error('AuthLifecycleRoot was not provided by the application composition root.');
  return root;
}
