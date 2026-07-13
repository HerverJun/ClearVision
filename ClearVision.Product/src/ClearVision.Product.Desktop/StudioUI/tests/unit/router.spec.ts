import { describe, expect, it } from 'vitest';
import { createTestRouter } from '@/test-support/createTestRouter';

describe('StudioUI router', () => {
  it('exposes only the Prompt 1 route skeleton', () => {
    const routePaths = createTestRouter()
      .getRoutes()
      .map(route => route.path);

    expect(routePaths).toEqual(expect.arrayContaining([
      '/',
      '/diagnostics',
      '/labs/design',
      '/labs/canvas',
      '/:pathMatch(.*)*'
    ]));
  });

  it('redirects unknown routes to diagnostics', async () => {
    const router = createTestRouter();

    await router.push('/unknown');

    expect(router.currentRoute.value.path).toBe('/diagnostics');
  });
});
