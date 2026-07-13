import { createPinia } from 'pinia';
import { createApp, type App as VueApp } from 'vue';
import type { Router } from 'vue-router';
import App from '@/app/App.vue';
import { createStudioRouter } from '@/app/router';

export interface MountStudioAppOptions {
  router?: Router;
}

export interface MountedStudioApp {
  readonly app: VueApp<Element>;
  readonly router: Router;
  unmount(): void;
}

export async function mountStudioApp(
  target: string | Element,
  options: MountStudioAppOptions = {}
): Promise<MountedStudioApp> {
  const mountTarget = typeof target === 'string'
    ? document.querySelector(target)
    : target;

  if (!(mountTarget instanceof Element)) {
    throw new Error('StudioUI mount target was not found.');
  }

  const app = createApp(App);
  const router = options.router ?? createStudioRouter();
  app.use(createPinia());
  app.use(router);
  await router.isReady();
  app.mount(mountTarget);

  let mounted = true;
  return {
    app,
    router,
    unmount() {
      if (!mounted) {
        return;
      }

      mounted = false;
      app.unmount();
    }
  };
}
