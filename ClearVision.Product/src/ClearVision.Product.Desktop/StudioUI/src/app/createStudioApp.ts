import { createPinia } from 'pinia';
import { createApp, type App as VueApp } from 'vue';
import type { Router } from 'vue-router';
import App from '@/app/App.vue';
import {
  createProductRuntime,
  productRuntimeKey,
  type ProductRuntime
} from '@/app/productRuntime';
import { createStudioRouter } from '@/app/router';
import {
  createDesktopStudioPlatform,
  createRuntimeStudioPlatform,
  studioPlatformKey,
  type DesktopStudioRuntimeWindow,
  type StudioPlatform
} from '@/app/studioPlatform';

export interface MountStudioAppOptions {
  readonly platform: StudioPlatform;
  readonly productRuntime?: ProductRuntime;
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
  readonly productRuntime: ProductRuntime;
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
  let productRuntime: ProductRuntime | undefined;

  try {
    productRuntime = options.productRuntime ?? createProductRuntime(options.platform);
    app.use(createPinia());
    app.use(router);
    app.provide(studioPlatformKey, options.platform);
    app.provide(productRuntimeKey, productRuntime);
    await router.isReady();
    app.mount(mountTarget);
  } catch (error) {
    productRuntime?.dispose();
    options.platform.dispose();
    throw error;
  }

  if (!productRuntime) {
    options.platform.dispose();
    throw new Error('ProductRuntime was not created.');
  }

  let mounted = true;
  return {
    app,
    router,
    platform: options.platform,
    productRuntime,
    unmount() {
      if (!mounted) {
        return;
      }

      mounted = false;
      try {
        app.unmount();
      } finally {
        try {
          productRuntime.dispose();
        } finally {
          options.platform.dispose();
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
