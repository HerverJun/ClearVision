import {
  createRouter,
  createWebHashHistory,
  type RouteRecordRaw,
  type Router,
  type RouterHistory
} from 'vue-router';
import CanvasLabPlaceholder from '@/labs/canvas/CanvasLabPlaceholder.vue';
import DesignLabPlaceholder from '@/labs/design/DesignLabPlaceholder.vue';
import DiagnosticsPage from '@/platform/diagnostics/DiagnosticsPage.vue';

export const studioRoutes: readonly RouteRecordRaw[] = [
  {
    path: '/',
    redirect: '/diagnostics'
  },
  {
    path: '/diagnostics',
    name: 'diagnostics',
    component: DiagnosticsPage
  },
  {
    path: '/labs/design',
    name: 'design-lab-placeholder',
    component: DesignLabPlaceholder
  },
  {
    path: '/labs/canvas',
    name: 'canvas-lab-placeholder',
    component: CanvasLabPlaceholder
  },
  {
    path: '/:pathMatch(.*)*',
    redirect: '/diagnostics'
  }
];

export function createStudioRouter(
  history: RouterHistory = createWebHashHistory(import.meta.env.BASE_URL)
): Router {
  return createRouter({
    history,
    routes: [...studioRoutes]
  });
}
