import type { LocationQuery, Router } from 'vue-router';
import type { AuthLifecycleRoot } from '@/app/auth';
import type { ProductLeaveReason } from './productLeaveGuardOwner';

export interface ProductLeaveGuardWindow extends Window {
  __clearVisionFlushProjectWorkspace?: (reason?: string) => Promise<boolean>;
}

function projectIdFromPath(path: string): string | null {
  return path.match(/^\/projects\/([0-9a-f-]{36})(?:\/|$)/i)?.[1] ?? null;
}

function routeLeaveReason(toPath: string, fromPath: string): ProductLeaveReason {
  if (toPath === '/change-password') return 'change-password';
  const fromProjectId = projectIdFromPath(fromPath);
  const toProjectId = projectIdFromPath(toPath);
  return fromProjectId && toProjectId && fromProjectId !== toProjectId
    ? 'project-switch'
    : 'route-leave';
}

function sameQueryExceptHandoff(to: LocationQuery, from: LocationQuery): boolean {
  const keys = new Set([...Object.keys(to), ...Object.keys(from)]);
  keys.delete('handoff');
  return [...keys].every(key => JSON.stringify(to[key]) === JSON.stringify(from[key]));
}

export function installProductLeaveGuardBridge(
  router: Router,
  authRoot: AuthLifecycleRoot,
  runtimeWindow: ProductLeaveGuardWindow = window as ProductLeaveGuardWindow
): () => void {
  if (runtimeWindow.__clearVisionFlushProjectWorkspace) {
    throw new Error('Product leave guard bridge is already installed.');
  }
  const removeRouteGuard = router.beforeEach(async (to, from) => {
    if (to.fullPath === from.fullPath) return true;
    if (to.name === 'project-workspace' && from.name === 'project-workspace' &&
        to.path === from.path && sameQueryExceptHandoff(to.query, from.query)) {
      return true;
    }
    if (to.path === '/login' && authRoot.auth.projection.phase === 'expired') return true;
    const guard = authRoot.getProductLeaveGuard();
    if (!guard) return true;
    return await guard.request(routeLeaveReason(to.path, from.path));
  });
  const handleBeforeUnload = (event: BeforeUnloadEvent): void => {
    if (!authRoot.getProductLeaveGuard()?.hasProtection()) return;
    event.preventDefault();
    event.returnValue = '';
  };
  runtimeWindow.addEventListener('beforeunload', handleBeforeUnload);
  const flushProjectWorkspace = async () =>
    await authRoot.getProductLeaveGuard()?.request('host-close') ?? true;
  runtimeWindow.__clearVisionFlushProjectWorkspace = flushProjectWorkspace;

  let disposed = false;
  return () => {
    if (disposed) return;
    disposed = true;
    removeRouteGuard();
    runtimeWindow.removeEventListener('beforeunload', handleBeforeUnload);
    if (runtimeWindow.__clearVisionFlushProjectWorkspace === flushProjectWorkspace) {
      delete runtimeWindow.__clearVisionFlushProjectWorkspace;
    }
  };
}
