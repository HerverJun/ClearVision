import { describe, expect, it } from 'vitest';
import { createMemoryHistory, createRouter } from 'vue-router';
import {
  installRouteChunkErrorHandler,
  isRouteChunkLoadError,
  studioRoutes
} from '@/app/router';
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
      '/projects/:id/workspace',
      '/operators',
      '/operators/:operatorType',
      '/stations',
      '/stations/:stationId',
      '/results',
      '/settings',
      '/diagnostics',
      '/about',
      '/labs/design',
      '/labs/canvas',
      '/:pathMatch(.*)*'
    ]));
  });

  it('redirects the root to projects and renders unknown routes as 404', async () => {
    const router = createTestRouter();

    await router.push('/');
    expect(router.currentRoute.value.path).toBe('/projects');

    await router.push('/unknown');

    expect(router.currentRoute.value.path).toBe('/unknown');
    expect(router.currentRoute.value.name).toBe('not-found-catchall');
    expect(router.currentRoute.value.meta.title).toBe('页面未找到');
  });

  it('keeps Labs out of formal product route metadata', () => {
    const routes = createTestRouter().getRoutes();
    expect(routes.find(route => route.path === '/labs/design')?.meta.internal).toBe(true);
    expect(routes.find(route => route.path === '/overview')?.meta.internal).not.toBe(true);
  });

  it('marks only the formal Workspace route as ProductLayout workspaceMode', () => {
    const routes = createTestRouter().getRoutes();
    expect(routes.find(route => route.path === '/projects/:id/workspace')?.meta)
      .toMatchObject({ workspaceMode: true, requiresSession: true });
    expect(routes.find(route => route.path === '/projects/:id')?.meta.workspaceMode)
      .not.toBe(true);
  });

  it('lazy loads product capabilities and Labs while keeping shell and errors eager', () => {
    const router = createTestRouter();
    const lazyRouteNames = [
      'overview', 'projects', 'project-detail', 'project-workspace', 'operators', 'operator-detail',
      'stations', 'station-detail', 'inspection-projects', 'project-inspection', 'results', 'settings', 'diagnostics',
      'about', 'design-lab-placeholder', 'canvas-lab-placeholder'
    ];

    for (const name of lazyRouteNames) {
      expect(router.getRoutes().find(route => route.name === name)?.components?.default)
        .toEqual(expect.any(Function));
    }

    expect(router.getRoutes().find(route => route.name === 'not-found')?.components?.default)
      .not.toEqual(expect.any(Function));
    expect(studioRoutes).toBeDefined();
  });

  it('recognizes route chunk failures and redirects them to the eager recovery route', async () => {
    expect(isRouteChunkLoadError(new TypeError('Failed to fetch dynamically imported module: /studio/assets/x.js')))
      .toBe(true);
    expect(isRouteChunkLoadError(new TypeError('Unable to preload CSS for /studio/assets/x.css'))).toBe(true);
    expect(isRouteChunkLoadError(new Error('ordinary route error'))).toBe(false);

    const router = createRouter({
      history: createMemoryHistory('/studio/'),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/broken',
          name: 'broken',
          component: () => Promise.reject(new TypeError('Failed to fetch dynamically imported module: broken.js'))
        },
        { path: '/not-found', name: 'not-found', component: { template: '<div />' } }
      ]
    });
    const removeHandler = installRouteChunkErrorHandler(router);

    await router.push('/');
    await expect(router.push('/broken')).rejects.toThrow('Failed to fetch dynamically imported module');
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(router.currentRoute.value).toMatchObject({
      name: 'not-found',
      query: { reason: 'route-load' }
    });
    removeHandler();
  });
});
