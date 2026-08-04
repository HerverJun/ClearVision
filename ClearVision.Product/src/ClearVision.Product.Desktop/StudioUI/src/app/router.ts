import {
  createRouter,
  createWebHashHistory,
  type RouteLocationNormalized,
  type RouteRecordRaw,
  type Router,
  type RouterHistory
} from 'vue-router';
import type { AuthLifecycleOwner } from '@/app/auth';
import ProductRuntimeBoundary from '@/app/layouts/ProductRuntimeBoundary.vue';
import ProductLayout from '@/app/layouts/ProductLayout.vue';
import InternalLabLayout from '@/app/layouts/InternalLabLayout.vue';
import LoginPage from '@/app/pages/auth/LoginPage.vue';
import SetupPage from '@/app/pages/auth/SetupPage.vue';
import ChangePasswordPage from '@/app/pages/auth/ChangePasswordPage.vue';
import ForbiddenPage from '@/app/pages/ForbiddenPage.vue';
import NotFoundPage from '@/app/pages/NotFoundPage.vue';
import { isRouteChunkLoadError } from '@/platform/diagnostics/bootstrapDiagnostic';
import type { StudioStartupConfigV1 } from '@/platform/startup';

export { isRouteChunkLoadError } from '@/platform/diagnostics/bootstrapDiagnostic';

const AboutPage = () => import('@/capabilities/about/AboutPage.vue');
const OverviewPage = () => import('@/capabilities/overview/OverviewPage.vue');
const OperatorDetailPage = () => import('@/capabilities/operators-read/OperatorDetailPage.vue');
const OperatorsPage = () => import('@/capabilities/operators-read/OperatorsPage.vue');
const WorkspacePage = () => import('@/capabilities/project-workspace/WorkspacePage.vue');
const ProjectDetailPage = () => import('@/capabilities/projects-read/ProjectDetailPage.vue');
const ProjectsPage = () => import('@/capabilities/projects-read/ProjectsPage.vue');
const ResultsPage = () => import('@/capabilities/results-read/ResultsPage.vue');
const InspectionProjectsPage = () => import('@/capabilities/inspection-run/InspectionProjectsPage.vue');
const InspectionRunPage = () => import('@/capabilities/inspection-run/InspectionRunPage.vue');
const StationDetailPage = () => import('@/capabilities/stations-read/StationDetailPage.vue');
const StationsPage = () => import('@/capabilities/stations-read/StationsPage.vue');
const CanvasLabPlaceholder = () => import('@/labs/canvas/CanvasLabPlaceholder.vue');
const DesignLabPlaceholder = () => import('@/labs/design/DesignLabPlaceholder.vue');
const DiagnosticsPage = () => import('@/platform/diagnostics/DiagnosticsPage.vue');
const AiWorkbenchPage = () => import('@/capabilities/ai-workbench/AiWorkbenchPage.vue');
const SettingsPage = () => import('@/capabilities/settings/SettingsPage.vue');

const editorRoles = Object.freeze(['Admin', 'Engineer']);
const workspaceFlagKey = 'Studio2.Workspace';
const stationFlagKey = 'Studio2.StationsRead';
const inspectionRunFlagKey = 'Studio2.InspectionRun';
const aiWorkbenchFlagKey = 'Studio2.AiWorkbench';
const settingsFlagKey = 'Studio2.Settings';

export const studioRoutes: readonly RouteRecordRaw[] = [
  {
    path: '/setup',
    name: 'setup',
    component: SetupPage,
    meta: { title: '首次管理员初始化', breadcrumb: '初始化', public: true, setupOnly: true }
  },
  {
    path: '/login',
    name: 'login',
    component: LoginPage,
    meta: { title: '登录', breadcrumb: '登录', public: true }
  },
  {
    path: '/change-password',
    name: 'change-password',
    component: ChangePasswordPage,
    meta: { title: '修改密码', breadcrumb: '修改密码', requiresSession: true }
  },
  {
    path: '/forbidden',
    name: 'forbidden',
    component: ForbiddenPage,
    meta: { title: '无权访问', breadcrumb: '403', requiresSession: true }
  },
  {
    path: '/not-found',
    name: 'not-found',
    component: NotFoundPage,
    meta: { title: '页面未找到', breadcrumb: '404', requiresSession: true }
  },
  {
    path: '/',
    component: ProductRuntimeBoundary,
    meta: { requiresSession: true },
    children: [
      {
        path: '',
        component: ProductLayout,
        meta: { title: 'ClearVision Studio', breadcrumb: 'Studio', requiresSession: true },
        children: [
          { path: '', redirect: '/projects' },
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
            path: 'ai',
            name: 'ai-workbench',
            component: AiWorkbenchPage,
            meta: {
              title: 'AI 工程工作台', breadcrumb: 'AI 工程工作台', requiresSession: true,
              allowedRoles: editorRoles, requiredFeatureFlag: aiWorkbenchFlagKey
            }
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
              allowedRoles: editorRoles,
              requiredFeatureFlag: workspaceFlagKey,
              workspaceMode: true
            }
          },
          {
            path: 'projects/:id/ai',
            name: 'project-ai-workbench',
            component: AiWorkbenchPage,
            meta: {
              title: 'AI 工程工作台', breadcrumb: 'AI 工程工作台', requiresSession: true,
              allowedRoles: editorRoles, requiredFeatureFlag: aiWorkbenchFlagKey
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
            meta: {
              title: '工作站',
              breadcrumb: '工作站',
              requiresSession: true,
              productProfile: 'stations-read'
            }
          },
          {
            path: 'stations/:stationId',
            name: 'station-detail',
            component: StationDetailPage,
            meta: {
              title: '工作站详情',
              breadcrumb: '工作站详情',
              requiresSession: true,
              productProfile: 'stations-read'
            }
          },
          {
            path: 'inspection',
            name: 'inspection-projects',
            component: InspectionProjectsPage,
            meta: {
              title: '连续检测', breadcrumb: '连续检测', requiresSession: true,
              allowedRoles: editorRoles, requiredFeatureFlag: inspectionRunFlagKey
            }
          },
          {
            path: 'projects/:id/inspection',
            name: 'project-inspection',
            component: InspectionRunPage,
            meta: {
              title: '连续检测运行', breadcrumb: '连续检测', requiresSession: true,
              allowedRoles: editorRoles, requiredFeatureFlag: inspectionRunFlagKey
            }
          },
          {
            path: 'results',
            name: 'results',
            component: ResultsPage,
            meta: { title: '检测结果', breadcrumb: '检测结果', requiresSession: true }
          },
          {
            path: 'settings',
            name: 'settings',
            component: SettingsPage,
            meta: {
              title: '设置',
              breadcrumb: '设置',
              requiresSession: true,
              allowedRoles: editorRoles,
              requiredFeatureFlag: settingsFlagKey
            }
          },
          {
            path: 'diagnostics',
            name: 'diagnostics',
            component: DiagnosticsPage,
            meta: {
              title: '诊断',
              breadcrumb: '诊断',
              requiresSession: true,
              allowedRoles: editorRoles
            }
          },
          {
            path: 'about',
            name: 'about',
            component: AboutPage,
            meta: { title: '关于', breadcrumb: '关于', requiresSession: true }
          },
          {
            path: ':pathMatch(.*)*',
            name: 'not-found-catchall',
            component: NotFoundPage,
            meta: { title: '页面未找到', breadcrumb: '404', requiresSession: true }
          }
        ]
      }
    ]
  },
  {
    path: '/labs',
    component: ProductRuntimeBoundary,
    meta: { title: '内部实验室', breadcrumb: '实验室', internal: true, requiresSession: true },
    children: [
      {
        path: '',
        component: InternalLabLayout,
        meta: { internal: true, requiresSession: true },
        children: [
          {
            path: 'design',
            name: 'design-lab-placeholder',
            component: DesignLabPlaceholder,
            meta: { title: 'Design Lab', breadcrumb: 'Design Lab', internal: true, requiresSession: true }
          },
          {
            path: 'canvas',
            name: 'canvas-lab-placeholder',
            component: CanvasLabPlaceholder,
            meta: { title: 'Canvas Lab', breadcrumb: 'Canvas Lab', internal: true, requiresSession: true }
          }
        ]
      }
    ]
  }
];

function containsUnsafeReturnSyntax(value: string): boolean {
  if (!value || value !== value.trim() || value.includes('\\')) return true;
  if (/^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(value)) return true;
  if (/%(?:2f|5c)/i.test(value)) return true;
  let decoded: string;
  try {
    decoded = decodeURIComponent(value);
  } catch {
    return true;
  }
  return decoded.split(/[?#]/, 1)[0]?.split('/').includes('..') === true;
}

export function resolveSafeReturnRoute(value: unknown): string | null {
  if (typeof value !== 'string' || containsUnsafeReturnSyntax(value)) return null;
  const path = value.split(/[?#]/, 1)[0] ?? '';
  if (path === '/overview' || path === '/projects' || path.startsWith('/projects/') ||
      path === '/ai' ||
      path === '/operators' || path.startsWith('/operators/') || path === '/inspection' || path === '/results' ||
      path === '/stations' || path.startsWith('/stations/') || path === '/settings' || path === '/diagnostics' ||
      path === '/about') {
    return value;
  }
  return null;
}

function requestedReturnRoute(to: RouteLocationNormalized): string | null {
  return resolveSafeReturnRoute(to.query.returnTo);
}

export function installRouteChunkErrorHandler(router: Router): () => void {
  let recoveryNavigationPending = false;
  return router.onError((error, to) => {
    if (!isRouteChunkLoadError(error) || recoveryNavigationPending || to.name === 'not-found') return;

    recoveryNavigationPending = true;
    const returnTo = resolveSafeReturnRoute(to.fullPath);
    void router.replace({
      name: 'not-found',
      query: {
        reason: 'route-load',
        ...(returnTo ? { returnTo } : {})
      }
    }).finally(() => {
      recoveryNavigationPending = false;
    });
  });
}

export function installAuthRouteGuard(
  router: Router,
  auth: AuthLifecycleOwner,
  startup: StudioStartupConfigV1
): () => void {
  return router.beforeEach(async to => {
    if (auth.projection.phase === 'checking-setup') await auth.start();
    const projection = auth.projection;

    if (projection.phase === 'setup-required') {
      return to.name === 'setup' ? true : { path: '/setup', replace: true };
    }
    if (to.name === 'setup') {
      return projection.phase === 'authenticated' ? { path: '/projects', replace: true } :
        { path: '/login', replace: true };
    }
    if (to.name === 'login') {
      if (projection.phase === 'authenticated') {
        return { path: requestedReturnRoute(to) ?? '/projects', replace: true };
      }
      return true;
    }

    const requiresSession = to.matched.some(record => record.meta.requiresSession === true);
    if (requiresSession && projection.phase !== 'authenticated') {
      const returnTo = resolveSafeReturnRoute(to.fullPath);
      return {
        path: '/login',
        query: returnTo ? { returnTo } : {},
        replace: true
      };
    }

    if (!requiresSession) return true;
    const role = projection.user?.role;
    if (!role || !startup.profileAllowedRoles.includes(role as typeof startup.profileAllowedRoles[number])) {
      return to.name === 'forbidden' ? true : { path: '/forbidden', replace: true };
    }
    const allowedRoles = to.matched.flatMap(record => record.meta.allowedRoles ?? []);
    if (allowedRoles.length > 0 && (!role || !allowedRoles.includes(role))) {
      return to.name === 'forbidden' ? true : { path: '/forbidden', replace: true };
    }
    if (to.matched.some(record => record.meta.productProfile === 'stations-read') &&
        startup.featureFlags[stationFlagKey] !== true) {
      return { path: '/forbidden', replace: true };
    }
    const requiredFlags = to.matched.flatMap(record => record.meta.requiredFeatureFlag ?? []);
    if (requiredFlags.some(flag => startup.featureFlags[flag] !== true)) {
      return { path: '/forbidden', replace: true };
    }
    if (to.matched.some(record => record.meta.internal === true) && startup.hostKind !== 'browser-test') {
      return { path: '/forbidden', replace: true };
    }
    return true;
  });
}

export function createStudioRouter(
  history: RouterHistory = createWebHashHistory(import.meta.env.BASE_URL)
): Router {
  const router = createRouter({ history, routes: [...studioRoutes] });
  installRouteChunkErrorHandler(router);
  router.afterEach(to => {
    if (typeof document !== 'undefined') {
      document.title = `${to.meta.title ?? 'ClearVision Studio'} · ClearVision`;
    }
  });
  return router;
}
