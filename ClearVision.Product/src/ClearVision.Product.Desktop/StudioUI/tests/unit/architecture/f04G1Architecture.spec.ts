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

describe('F04 G1 owner, route and transport boundary guards', () => {
  const files = sourceFiles(sourceRoot);

  it('records the current single session projection owner without inventing the future auth lifecycle owner', () => {
    expect(files.filter(path => path.endsWith('sessionProjectionOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => path.toLowerCase().endsWith('authlifecycleowner.ts'))).toHaveLength(0);

    const tokenLiteralFiles = files
      .filter(path => read(path).includes('cv_auth_token'))
      .map(studioRelative);
    expect(tokenLiteralFiles).toEqual(['src/app/studioPlatform.ts']);
    expect(files.filter(path => /(?:localStorage|sessionStorage)\s*\??\.\s*(?:setItem|removeItem)\s*\(/.test(read(path))))
      .toEqual([]);
  });

  it('keeps one HTTP transport and only the approved F03 write ports', () => {
    expect(files
      .filter(path => /\b(?:globalThis\.)?fetch\s*\(/.test(read(path)))
      .map(studioRelative))
      .toEqual(['src/platform/api/apiTransport.ts']);

    const writePortFiles = files
      .filter(path => /api\.(?:post|put|delete)(?:\.bind\(api\)|\s*\()/.test(read(path)))
      .map(studioRelative)
      .sort();
    expect(writePortFiles).toEqual([
      'src/capabilities/project-workspace/persistence/projectPersistencePort.ts',
      'src/capabilities/project-workspace/preview/previewTransport.ts',
      'src/capabilities/project-workspace/run/runContracts.ts'
    ]);

    expect(read(join(sourceRoot, 'capabilities/project-workspace/persistence/projectPersistencePort.ts')))
      .toContain('return `projects/${projectId}`');
    expect(read(join(sourceRoot, 'capabilities/project-workspace/preview/previewTransport.ts')))
      .toContain("'flows/preview-node'");
    const run = read(join(sourceRoot, 'capabilities/project-workspace/run/runContracts.ts'));
    expect(run).toContain("'inspection/admission'");
    expect(run).toContain("'inspection/execute'");
    expect(run).toContain("'inspection/stop'");
    expect(run).toContain("'inspection/reconcile'");
  });

  it('keeps one Workspace authority and no frontend Project, Result or Runtime repository', () => {
    expect(files.filter(path => path.endsWith('workspaceOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('workspacePersistenceOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('runCommandOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => /(?:project|result|runtime)Repository\.(?:ts|js)$/i.test(path)))
      .toHaveLength(0);
    expect(files.filter(path => /\bclass\s+(?:Project|Result|Runtime)Repository\b/.test(read(path))))
      .toHaveLength(0);
  });

  it('freezes current route metadata while leaving the real G2 guard explicitly unimplemented', () => {
    const routes = createTestRouter().getRoutes();
    const protectedProductPaths = [
      '/overview',
      '/projects',
      '/projects/:id',
      '/projects/:id/workspace',
      '/operators',
      '/operators/:operatorType',
      '/stations',
      '/stations/:stationId',
      '/results',
      '/diagnostics',
      '/about',
      '/:pathMatch(.*)*'
    ];
    for (const path of protectedProductPaths) {
      expect(routes.find(route => route.path === path)?.meta.requiresSession, path).toBe(true);
    }
    expect(routes.find(route => route.path === '/labs/design')?.meta.internal).toBe(true);
    expect(routes.find(route => route.path === '/labs/canvas')?.meta.internal).toBe(true);

    const router = read(join(sourceRoot, 'app/router.ts'));
    expect(router).not.toContain('beforeEach(');
    expect(router).not.toContain('safeReturn');
  });

  it('keeps internal Labs out of product navigation and formal defaults false/false', () => {
    const navigation = read(join(sourceRoot, 'app/navigation.ts'));
    expect(navigation).not.toContain('/labs');

    const settings = JSON.parse(read(join(
      repositoryRoot,
      'ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json'
    ))) as { Studio: { StudioUiEnabled: boolean; WorkspaceCapabilityEnabled: boolean } };
    expect(settings.Studio.StudioUiEnabled).toBe(false);
    expect(settings.Studio.WorkspaceCapabilityEnabled).toBe(false);
  });
});
