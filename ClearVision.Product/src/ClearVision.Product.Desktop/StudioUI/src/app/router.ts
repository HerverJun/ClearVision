import {
  createRouter,
  createWebHashHistory,
  type RouteRecordRaw,
  type Router,
  type RouterHistory
} from 'vue-router';
import ProductLayout from '@/app/layouts/ProductLayout.vue';
import InternalLabLayout from '@/app/layouts/InternalLabLayout.vue';
import NotFoundPage from '@/app/pages/NotFoundPage.vue';
import AboutPage from '@/capabilities/about/AboutPage.vue';
import OverviewPage from '@/capabilities/overview/OverviewPage.vue';
import OperatorDetailPage from '@/capabilities/operators-read/OperatorDetailPage.vue';
import OperatorsPage from '@/capabilities/operators-read/OperatorsPage.vue';
import WorkspacePage from '@/capabilities/project-workspace/WorkspacePage.vue';
import ProjectDetailPage from '@/capabilities/projects-read/ProjectDetailPage.vue';
import ProjectsPage from '@/capabilities/projects-read/ProjectsPage.vue';
import ResultsPage from '@/capabilities/results-read/ResultsPage.vue';
import StationDetailPage from '@/capabilities/stations-read/StationDetailPage.vue';
import StationsPage from '@/capabilities/stations-read/StationsPage.vue';
import CanvasLabPlaceholder from '@/labs/canvas/CanvasLabPlaceholder.vue';
import DesignLabPlaceholder from '@/labs/design/DesignLabPlaceholder.vue';
import DiagnosticsPage from '@/platform/diagnostics/DiagnosticsPage.vue';

export const studioRoutes: readonly RouteRecordRaw[] = [
  {
    path: '/',
    component: ProductLayout,
    meta: { title: 'ClearVision Studio', breadcrumb: 'Studio', requiresSession: true },
    children: [
      { path: '', redirect: '/overview' },
      {
        path: 'overview',
        name: 'overview',
        component: OverviewPage,
        meta: { title: '概览', breadcrumb: '概览', requiresSession: true }
      },
      {
        path: 'projects',
        name: 'projects',
        component: ProjectsPage,
        meta: { title: '工程', breadcrumb: '工程', requiresSession: true }
      },
      {
        path: 'projects/:id',
        name: 'project-detail',
        component: ProjectDetailPage,
        meta: { title: '工程详情', breadcrumb: '工程详情', requiresSession: true }
      },
      {
        path: 'projects/:id/workspace',
        name: 'project-workspace',
        component: WorkspacePage,
        meta: {
          title: '工程工作区',
          breadcrumb: '工作区',
          requiresSession: true,
          workspaceMode: true
        }
      },
      {
        path: 'operators',
        name: 'operators',
        component: OperatorsPage,
        meta: { title: '算子库', breadcrumb: '算子库', requiresSession: true }
      },
      {
        path: 'operators/:operatorType',
        name: 'operator-detail',
        component: OperatorDetailPage,
        meta: { title: '算子详情', breadcrumb: '算子详情', requiresSession: true }
      },
      {
        path: 'stations',
        name: 'stations',
        component: StationsPage,
        meta: { title: '工作站', breadcrumb: '工作站', requiresSession: true }
      },
      {
        path: 'stations/:stationId',
        name: 'station-detail',
        component: StationDetailPage,
        meta: { title: '工作站详情', breadcrumb: '工作站详情', requiresSession: true }
      },
      {
        path: 'results',
        name: 'results',
        component: ResultsPage,
        meta: { title: '检测结果', breadcrumb: '检测结果', requiresSession: true }
      },
      {
        path: 'diagnostics',
        name: 'diagnostics',
        component: DiagnosticsPage,
        meta: { title: '诊断', breadcrumb: '诊断', requiresSession: true }
      },
      {
        path: 'about',
        name: 'about',
        component: AboutPage,
        meta: { title: '关于', breadcrumb: '关于', requiresSession: true }
      },
      {
        path: ':pathMatch(.*)*',
        name: 'not-found',
        component: NotFoundPage,
        meta: { title: '页面未找到', breadcrumb: '404', requiresSession: true }
      }
    ]
  },
  {
    path: '/labs',
    component: InternalLabLayout,
    meta: { title: '内部实验室', breadcrumb: '实验室', internal: true },
    children: [
      {
        path: 'design',
        name: 'design-lab-placeholder',
        component: DesignLabPlaceholder,
        meta: { title: 'Design Lab', breadcrumb: 'Design Lab', internal: true }
      },
      {
        path: 'canvas',
        name: 'canvas-lab-placeholder',
        component: CanvasLabPlaceholder,
        meta: { title: 'Canvas Lab', breadcrumb: 'Canvas Lab', internal: true }
      }
    ]
  }
];

export function createStudioRouter(
  history: RouterHistory = createWebHashHistory(import.meta.env.BASE_URL)
): Router {
  const router = createRouter({
    history,
    routes: [...studioRoutes]
  });
  router.afterEach(to => {
    if (typeof document !== 'undefined') {
      document.title = `${to.meta.title ?? 'ClearVision Studio'} · ClearVision`;
    }
  });
  return router;
}
