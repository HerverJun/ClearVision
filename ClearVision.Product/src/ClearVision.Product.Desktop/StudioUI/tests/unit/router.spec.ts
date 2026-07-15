import { describe, expect, it } from 'vitest';
import { createTestRouter } from '@/test-support/createTestRouter';

describe('StudioUI router', () => {
  it('exposes the frozen F02 product routes and isolated Labs', () => {
    const routePaths = createTestRouter()
      .getRoutes()
      .map(route => route.path);

    expect(routePaths).toEqual(expect.arrayContaining([
      '/',
      '/overview',
      '/projects',
      '/projects/:id',
      '/diagnostics',
      '/about',
      '/labs/design',
      '/labs/canvas',
      '/:pathMatch(.*)*'
    ]));
  });

  it('redirects the root to overview and renders unknown routes as 404', async () => {
    const router = createTestRouter();

    await router.push('/');
    expect(router.currentRoute.value.path).toBe('/overview');

    await router.push('/unknown');

    expect(router.currentRoute.value.path).toBe('/unknown');
    expect(router.currentRoute.value.name).toBe('not-found');
    expect(router.currentRoute.value.meta.title).toBe('页面未找到');
  });

  it('keeps Labs out of formal product route metadata', () => {
    const routes = createTestRouter().getRoutes();
    expect(routes.find(route => route.path === '/labs/design')?.meta.internal).toBe(true);
    expect(routes.find(route => route.path === '/overview')?.meta.internal).not.toBe(true);
  });
});
