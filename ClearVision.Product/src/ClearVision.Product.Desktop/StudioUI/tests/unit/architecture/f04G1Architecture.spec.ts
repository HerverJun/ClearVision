import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { createTestRouter } from '@/test-support/createTestRouter';

const studioRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const sourceRoot = join(studioRoot, 'src');
const repositoryRoot = resolve(studioRoot, '../../../..');

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

describe('F04 G2 auth, owner, route and transport boundary guards', () => {
  const files = sourceFiles(sourceRoot);

  it('has one auth owner, no runtime session projection owner and one token writer', () => {
    expect(files.filter(path => path.toLowerCase().endsWith('authlifecycleowner.ts'))).toHaveLength(1);
    expect(files.filter(path => read(path).includes('createSessionProjectionOwner('))).toEqual([]);

    const tokenLiteralFiles = files
      .filter(path => read(path).includes('cv_auth_token'))
      .map(studioRelative);
    expect(tokenLiteralFiles).toEqual(['src/platform/auth/tokenPort.ts']);

    const tokenWriterConsumers = files
      .filter(path => /\.\s*(?:setToken|removeToken)\s*\(/.test(read(path)))
      .map(studioRelative);
    expect(tokenWriterConsumers).toEqual(['src/app/auth/authLifecycleOwner.ts']);
    expect(files.filter(path => /localStorage\s*\.\s*(?:setItem|removeItem)\s*\(/.test(read(path))))
      .toEqual([]);
  });

  it('keeps one HTTP authority and uses a direct unauthorized callback without EventBus', () => {
    expect(files
      .filter(path => /\b(?:globalThis\.)?fetch\s*\(/.test(read(path)))
      .map(studioRelative))
      .toEqual(['src/platform/api/apiTransport.ts']);
    const transport = read(join(sourceRoot, 'platform/api/apiTransport.ts'));
    const auth = read(join(sourceRoot, 'app/auth/authLifecycleOwner.ts'));
    expect(transport).toContain('setUnauthorizedHandler');
    expect(transport).toContain('sessionGeneration');
    expect(auth).toContain('unauthorizedFlights');
    expect(`${transport}\n${auth}`).not.toMatch(/EventBus|ServiceRegistry/);
  });

  it('gates ProductRuntime behind authenticated composition and preserves one Workspace authority', () => {
    const root = read(join(sourceRoot, 'app/auth/authLifecycleRoot.ts'));
    const productRuntime = read(join(sourceRoot, 'app/productRuntime.ts'));
    expect(root).toContain('createProductRuntime(platform, currentAuth.session)');
    expect(root).toContain('const nextRuntime = await createProductRuntime');
    expect(root).toContain('productRuntime.value = null');
    expect(root).toContain('quarantineForSessionExpiration');
    expect(productRuntime).toContain("await import('@/app/productRuntimeFactory')");
    expect(productRuntime).not.toContain('createWorkspaceRuntime');
    expect(productRuntime).not.toContain('createSessionProjectionOwner');
    expect(files.filter(path => path.endsWith('workspaceOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('workspacePersistenceOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('runCommandOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => /(?:project|result|runtime)Repository\.(?:ts|js)$/i.test(path)))
      .toHaveLength(0);
  });

  it('installs real setup, session, role, profile, internal and safe-return guards', () => {
    const routes = createTestRouter().getRoutes();
    for (const path of ['/setup', '/login', '/change-password', '/forbidden', '/not-found']) {
      expect(routes.find(route => route.path === path), path).toBeDefined();
    }
    expect(routes.find(route => route.path === '/projects/:id/workspace')?.meta.allowedRoles)
      .toEqual(['Admin', 'Engineer']);
    expect(routes.find(route => route.path === '/diagnostics')?.meta.allowedRoles)
      .toEqual(['Admin', 'Engineer']);
    expect(routes.find(route => route.path === '/stations')?.meta.productProfile).toBe('stations-read');
    expect(routes.find(route => route.path === '/labs/design')?.meta.internal).toBe(true);

    const router = read(join(sourceRoot, 'app/router.ts'));
    expect(router).toContain('router.beforeEach');
    expect(router).toContain('resolveSafeReturnRoute');
    expect(router).toContain("path: '/forbidden'");
    expect(router).toContain("path: '/login'");
  });

  it('keeps Labs out of product navigation and preserves the named F09 default', () => {
    const navigation = read(join(sourceRoot, 'app/navigation.ts'));
    expect(navigation).not.toContain('/labs');

    const settings = JSON.parse(read(join(
      repositoryRoot,
      'ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json'
    ))) as { Studio: { StartupProfile: string; StudioUiEnabled: boolean; WorkspaceCapabilityEnabled: boolean } };
    expect(settings.Studio.StartupProfile).toBe('NEXT_DEFAULT');
    expect(settings.Studio.StudioUiEnabled).toBe(true);
    expect(settings.Studio.WorkspaceCapabilityEnabled).toBe(true);
  });
});
