import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

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

describe('F04 G3C Project lifecycle architecture guards', () => {
  const files = sourceFiles(sourceRoot);

  it('has exactly one Project command owner and mounts it once in ProductRuntime', () => {
    expect(files.filter(path => path.endsWith('projectLifecycleCommandOwner.ts'))).toHaveLength(1);
    const runtime = read(join(sourceRoot, 'app/productRuntime.ts'));
    expect(runtime.match(/createProjectLifecycleCommandOwner\(/g)).toHaveLength(1);
    expect(runtime).toContain('projectLifecycle.dispose');
    expect(runtime).toContain('leaveGuard.request(reason)');
    expect(runtime).toContain('projectLifecycle.reconcileAfterReauthentication');
  });

  it('uses shared ApiTransport with generation, AbortController and no private Project cache or EventBus', () => {
    const owner = read(join(
      sourceRoot,
      'capabilities/project-lifecycle/projectLifecycleCommandOwner.ts'
    ));
    expect(owner).toContain('readonly api: ApiTransport');
    expect(owner).toContain('new AbortController()');
    expect(owner).toContain('generation');
    expect(owner).toContain('isCurrent');
    expect(owner).toContain('dispose(reason');
    expect(owner).not.toMatch(/\bfetch\s*\(|EventBus|ServiceRegistry|localStorage|sessionStorage/);
    expect(files.filter(path => /project(?:Read)?Cache\.(?:ts|js)$/i.test(path))).toEqual([]);
  });

  it('routes create, update, open and delete only through the command owner', () => {
    const projects = read(join(sourceRoot, 'capabilities/projects-read/ProjectsPage.vue'));
    const detail = read(join(sourceRoot, 'capabilities/projects-read/ProjectDetailPage.vue'));
    const workspace = read(join(sourceRoot, 'capabilities/project-workspace/WorkspacePage.vue'));
    const combined = `${projects}\n${detail}\n${workspace}`;
    for (const command of ['createBlank(', 'updateProject(', 'openProject(', 'deleteProject(']) {
      expect(combined).toContain(command);
    }
    expect(combined).not.toMatch(/\.api\.(?:post|put|delete)\s*\(/);
    expect(workspace.indexOf('projectLifecycle.openProject(projectId)'))
      .toBeLessThan(workspace.indexOf('runtime.openProject(projectId)'));
  });

  it('keeps the existing Project read-query owner and named F09 candidate projection', () => {
    expect(files
      .filter(path => read(path).includes('createProjectsListQuery('))
      .map(studioRelative))
      .toContain('src/capabilities/projects-read/ProjectsPage.vue');
    const settings = JSON.parse(read(resolve(
      studioRoot,
      '../../../../ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json'
    ))) as { Studio: { StartupProfile: string; StudioUiEnabled: boolean; WorkspaceCapabilityEnabled: boolean } };
    expect(settings.Studio).toMatchObject({
      StartupProfile: 'NEXT_DEFAULT_CANDIDATE',
      StudioUiEnabled: true,
      WorkspaceCapabilityEnabled: true
    });
  });
});
