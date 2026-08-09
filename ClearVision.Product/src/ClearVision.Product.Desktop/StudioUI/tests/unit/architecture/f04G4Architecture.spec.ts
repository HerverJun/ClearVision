import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { productNavigation, visibleProductNavigation } from '@/app/navigation';
import { createTestRouter } from '@/test-support/createTestRouter';

const studioRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const sourceRoot = join(studioRoot, 'src');

function sourceFiles(root: string): string[] {
  if (!existsSync(root)) return [];
  return readdirSync(root, { withFileTypes: true }).flatMap(entry => {
    const path = join(root, entry.name);
    return entry.isDirectory()
      ? sourceFiles(path)
      : ['.ts', '.vue', '.js'].includes(extname(path)) ? [path] : [];
  });
}

function read(path: string): string {
  return readFileSync(path, 'utf8');
}

function studioRelative(path: string): string {
  return relative(studioRoot, path).replaceAll('\\', '/');
}

describe('F04 G4 product shell and leave protection architecture guards', () => {
  const files = sourceFiles(sourceRoot);

  it('keeps one leave owner and installs its route, unload and Host bridge once at composition root', () => {
    expect(files.filter(path => path.endsWith('productLeaveGuardOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('productLeaveGuardBridge.ts'))).toHaveLength(1);

    const runtime = read(join(sourceRoot, 'app/productRuntimeFactory.ts'));
    const compositionRoot = read(join(sourceRoot, 'app/createStudioApp.ts'));
    expect(runtime.match(/createProductLeaveGuardOwner\(/g)).toHaveLength(1);
    expect(compositionRoot.match(/installProductLeaveGuardBridge\(/g)).toHaveLength(1);

    const beforeUnloadOwners = files
      .filter(path => read(path).includes("addEventListener('beforeunload'"))
      .map(studioRelative);
    const hostCloseOwners = files
      .filter(path => read(path).includes('__clearVisionFlushProjectWorkspace'))
      .map(studioRelative);
    expect(beforeUnloadOwners).toEqual(['src/app/leave/productLeaveGuardBridge.ts']);
    expect(hostCloseOwners).toEqual(['src/app/leave/productLeaveGuardBridge.ts']);
  });

  it('keeps WorkspacePage free of a second route, unload, Host-close or native-confirm guard', () => {
    const workspacePage = read(join(sourceRoot, 'capabilities/project-workspace/WorkspacePage.vue'));
    expect(workspacePage).not.toMatch(/router\.beforeEach|beforeunload|__clearVisionFlushProjectWorkspace/);
    expect(workspacePage).not.toMatch(/(?:globalThis\.)?confirm\s*\(/);
  });

  it('keeps navigation visibility aligned with route role and profile guards', () => {
    const routes = createTestRouter().getRoutes();
    for (const item of productNavigation) {
      const route = routes.find(candidate => candidate.path === item.to);
      expect(route, item.to).toBeDefined();
      expect(route?.meta.allowedRoles).toEqual(item.allowedRoles);
      if (item.requiredFeatureFlag === 'Studio2.StationsRead') {
        expect(route?.meta.productProfile).toBe('stations-read');
      } else {
        expect(route?.meta.productProfile).toBeUndefined();
      }
    }

    expect(visibleProductNavigation('Operator', {}).map(item => item.to)).toEqual([
      '/overview', '/operators', '/projects', '/results', '/about'
    ]);
    expect(visibleProductNavigation('Engineer', {}).map(item => item.to))
      .toEqual(['/overview', '/operators', '/projects', '/results', '/diagnostics', '/about']);
    expect(visibleProductNavigation('Engineer', { 'Studio2.Settings': true }).map(item => item.to))
      .toEqual(['/overview', '/operators', '/projects', '/results', '/settings', '/diagnostics', '/about']);
    expect(visibleProductNavigation('Engineer', { 'Studio2.InspectionRun': true, 'Studio2.Settings': true }).map(item => item.to))
      .toEqual(['/overview', '/inspection', '/operators', '/projects', '/results', '/settings', '/diagnostics', '/about']);
    expect(visibleProductNavigation('Engineer', { 'Studio2.StationsRead': true, 'Studio2.Settings': true }).map(item => item.to))
      .toEqual(['/overview', '/operators', '/projects', '/results', '/settings', '/stations', '/diagnostics', '/about']);
    expect(visibleProductNavigation('Admin', { 'Studio2.StationsRead': true }).map(item => item.to))
      .toEqual(['/overview', '/operators', '/projects', '/results', '/stations', '/diagnostics', '/about']);
    expect(visibleProductNavigation('Engineer', { 'Studio2.AiWorkbench': true }).map(item => item.to))
      .toEqual(['/overview', '/ai', '/operators', '/projects', '/results', '/diagnostics', '/about']);
    expect(visibleProductNavigation('Operator', { 'Studio2.AiWorkbench': true }).map(item => item.to))
      .toEqual(['/overview', '/operators', '/projects', '/results', '/about']);
    expect(productNavigation.some(item => item.to.startsWith('/labs'))).toBe(false);
  });

  it('uses the shared accessible modal contract for promptable leave states', () => {
    const layout = read(join(sourceRoot, 'app/layouts/ProductLayout.vue'));
    expect(layout).toContain('<CvModal');
    expect(layout).toContain('data-testid="leave-guard-stay"');
    expect(layout).toContain('data-testid="leave-guard-discard"');
    expect(layout).toContain('data-modal-initial-focus');
    expect(layout).toContain('@close="leaveGuard.cancelPrompt"');
    expect(layout).toContain('data-product-state="leave-blocked"');
  });

  it('keeps forbidden and not-found as distinct authenticated product states', () => {
    const routes = createTestRouter().getRoutes();
    expect(routes.find(route => route.name === 'forbidden')?.path).toBe('/forbidden');
    expect(routes.find(route => route.name === 'not-found')?.path).toBe('/not-found');
    expect(routes.find(route => route.name === 'not-found-catchall')?.path)
      .toBe('/:pathMatch(.*)*');
  });
});
