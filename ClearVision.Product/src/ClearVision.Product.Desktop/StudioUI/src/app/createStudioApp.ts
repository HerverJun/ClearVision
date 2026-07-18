import { createPinia } from 'pinia';
import { createApp, type App as VueApp } from 'vue';
import type { Router } from 'vue-router';
import App from '@/app/App.vue';
import { authLifecycleRootKey, createAuthLifecycleRoot, type AuthLifecycleRoot } from '@/app/auth';
import { installProductLeaveGuardBridge } from '@/app/leave';
import type { ProductRuntime } from '@/app/productRuntime';
import { createStudioRouter, installAuthRouteGuard } from '@/app/router';
import {
  createDesktopStudioPlatform,
  createRuntimeStudioPlatform,
  studioPlatformKey,
  type DesktopStudioRuntimeWindow,
  type StudioPlatform
} from '@/app/studioPlatform';

export interface MountStudioAppOptions {
  readonly platform: StudioPlatform;
  router?: Router;
}

export interface MountDesktopStudioAppOptions {
  readonly router?: Router;
  readonly runtimeWindow?: DesktopStudioRuntimeWindow;
}

export interface MountedStudioApp {
  readonly app: VueApp<Element>;
  readonly router: Router;
  readonly platform: StudioPlatform;
  readonly authRoot: AuthLifecycleRoot;
  readonly productRuntime: ProductRuntime | null;
  unmount(): void;
}

export async function mountStudioApp(
  target: string | Element,
  options: MountStudioAppOptions
): Promise<MountedStudioApp> {
  const mountTarget = typeof target === 'string'
    ? document.querySelector(target)
    : target;

  if (!(mountTarget instanceof Element)) {
    options.platform.dispose();
    throw new Error('StudioUI mount target was not found.');
  }

  const app = createApp(App);
  const router = options.router ?? createStudioRouter();
  const authRoot = createAuthLifecycleRoot(options.platform);
  let removeAuthRouteGuard: (() => void) | undefined;
  let removeLeaveGuardBridge: (() => void) | undefined;

  try {
    authRoot.bindRouter(router);
    removeAuthRouteGuard = installAuthRouteGuard(router, authRoot.auth, options.platform.startup);
    removeLeaveGuardBridge = installProductLeaveGuardBridge(router, authRoot);
    app.use(createPinia());
    app.use(router);
    app.provide(studioPlatformKey, options.platform);
    app.provide(authLifecycleRootKey, authRoot);
    await authRoot.start();
    await router.isReady();
    app.mount(mountTarget);
  } catch (error) {
    removeLeaveGuardBridge?.();
    removeAuthRouteGuard?.();
    authRoot.dispose();
    options.platform.dispose();
    throw error;
  }

  let mounted = true;
  return {
    app,
    router,
    platform: options.platform,
    authRoot,
    get productRuntime(): ProductRuntime | null {
      return authRoot.productRuntime.value;
    },
    unmount() {
      if (!mounted) {
        return;
      }

      mounted = false;
      try {
        app.unmount();
      } finally {
        try {
          removeLeaveGuardBridge?.();
          removeAuthRouteGuard?.();
        } finally {
          try {
            authRoot.dispose();
          } finally {
            options.platform.dispose();
          }
        }
      }
    }
  };
}

export async function mountDesktopStudioApp(
  target: string | Element,
  options: MountDesktopStudioAppOptions = {}
): Promise<MountedStudioApp> {
  const platform = createDesktopStudioPlatform(options.runtimeWindow);
  return mountStudioApp(target, {
    platform,
    ...(options.router ? { router: options.router } : {})
  });
}

export async function mountRuntimeStudioApp(
  target: string | Element,
  options: MountDesktopStudioAppOptions = {}
): Promise<MountedStudioApp> {
  const platform = createRuntimeStudioPlatform(options.runtimeWindow);
  return mountStudioApp(target, {
    platform,
    ...(options.router ? { router: options.router } : {})
  });
}
