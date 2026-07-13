import { nextTick } from 'vue';
import { afterEach, describe, expect, it } from 'vitest';
import { mountStudioApp, type MountedStudioApp } from '@/app/createStudioApp';
import { createTestRouter } from '@/test-support/createTestRouter';

let mountedApp: MountedStudioApp | undefined;

afterEach(() => {
  mountedApp?.unmount();
  mountedApp = undefined;
  document.body.innerHTML = '';
});

describe('mountStudioApp', () => {
  it('mounts the diagnostics route once and disposes idempotently', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const router = createTestRouter();

    mountedApp = await mountStudioApp('#app', { router });

    expect(router.currentRoute.value.path).toBe('/diagnostics');
    expect(document.querySelector('[data-studio-page="diagnostics"]')).not.toBeNull();

    mountedApp.unmount();
    mountedApp.unmount();
    expect(document.querySelector('#app')?.innerHTML).toBe('');
  });

  it('renders the reserved design and canvas routes without mounting business capabilities', async () => {
    document.body.innerHTML = '<div id="app"></div>';
    const router = createTestRouter();
    mountedApp = await mountStudioApp('#app', { router });

    await router.push('/labs/design');
    await nextTick();
    expect(document.querySelector('[data-studio-page="design-placeholder"]')).not.toBeNull();

    await router.push('/labs/canvas');
    await nextTick();
    expect(document.querySelector('[data-studio-page="canvas-placeholder"]')).not.toBeNull();
  });
});
