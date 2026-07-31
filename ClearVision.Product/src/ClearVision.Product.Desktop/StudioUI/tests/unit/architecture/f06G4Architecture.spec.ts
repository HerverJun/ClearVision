import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const studioRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const sourceRoot = join(studioRoot, 'src');
const aiRoot = join(sourceRoot, 'capabilities/ai-workbench');
const workspaceRoot = join(sourceRoot, 'capabilities/project-workspace');
const productRoot = resolve(studioRoot, '../../..');

function sourceFiles(root: string): string[] {
  if (!existsSync(root)) return [];
  return readdirSync(root, { withFileTypes: true }).flatMap(entry => {
    const path = join(root, entry.name);
    return entry.isDirectory()
      ? sourceFiles(path)
      : ['.ts', '.vue', '.js', '.cs'].includes(extname(path)) ? [path] : [];
  });
}

function read(path: string): string {
  return readFileSync(path, 'utf8');
}

function studioRelative(path: string): string {
  return relative(studioRoot, path).replaceAll('\\', '/');
}

describe('F06 G4 handoff and save-authority architecture guards', () => {
  const aiFiles = sourceFiles(aiRoot);
  const workspaceFiles = sourceFiles(workspaceRoot);

  it('keeps AI free of Canvas, Workspace owner and Project persistence imports', () => {
    const combined = aiFiles.map(read).join('\n');
    expect(combined).not.toMatch(/from\s+['"][^'"]*(?:project-workspace|platform\/canvas|FlowCanvas|ImageCanvas)/i);
    expect(combined).not.toMatch(/replaceFlow\s*\(|createWorkspacePersistenceOwner|ProjectSaveCoordinator/);
    expect(combined).not.toMatch(/(?:api\.)?(?:put|post)\s*\([^\n]*projects/i);
  });

  it('has one Workspace receive port and no candidate Flow fallback storage', () => {
    expect(workspaceFiles.filter(path => path.endsWith('handoffReceivePort.ts'))).toHaveLength(1);
    const consumers = sourceFiles(sourceRoot)
      .filter(path => !path.endsWith('handoffReceivePort.ts'))
      .filter(path => read(path).includes('createWorkspaceHandoffReceivePort('))
      .map(studioRelative);
    expect(consumers).toEqual(['src/capabilities/project-workspace/workspaceRuntime.ts']);
    const handoffFiles = workspaceFiles.filter(path =>
      path.includes(`${join('project-workspace', 'handoff')}`) ||
      path.endsWith('workspaceNewDraftOwner.ts') ||
      path.endsWith('WorkspacePage.vue')
    );
    const combined = [...aiFiles, ...handoffFiles].map(read).join('\n');
    expect(combined).not.toMatch(/localStorage|sessionStorage/);
    expect(read(join(workspaceRoot, 'handoff/handoffContracts.ts'))).not.toMatch(/ownerHash|authorization|secret/i);
  });

  it('passes only artifact identity through the route after disposing the AI owner', () => {
    const page = read(join(aiRoot, 'AiWorkbenchPage.vue'));
    const handoffStart = page.indexOf('async function handoffAndOpenWorkspace');
    const handoffEnd = page.indexOf('\nasync function ', handoffStart + 1);
    const handoff = page.slice(handoffStart, handoffEnd);
    const dispose = handoff.indexOf('releaseOwner(current);');
    const navigate = handoff.indexOf('await router.push({', dispose);
    expect(handoffStart).toBeGreaterThan(0);
    expect(dispose).toBeGreaterThan(0);
    expect(navigate).toBeGreaterThan(dispose);
    expect(handoff.slice(navigate, handoff.indexOf('});', navigate) + 3)).toContain(
      'query: { handoff: artifact.artifactId }'
    );
    expect(handoff.slice(navigate, handoff.indexOf('});', navigate) + 3)).not.toMatch(/candidateFlow|fingerprint/);
  });

  it('keeps new and existing formal saves on the existing Workspace persistence chain', () => {
    const page = read(join(workspaceRoot, 'WorkspacePage.vue'));
    const layout = read(join(sourceRoot, 'app/layouts/ProductLayout.vue'));
    const owner = read(join(workspaceRoot, 'workspaceOwner.ts'));
    const port = read(join(workspaceRoot, 'persistence/projectPersistencePort.ts'));
    const persistence = read(join(workspaceRoot, 'persistence/workspacePersistenceOwner.ts'));

    expect(page).toContain('projectLifecycle.createBlank({');
    expect(page).toContain('workspaceOwner.adoptNewHandoffDraft({');
    expect(page).toContain('await workspaceOwner.save()');
    expect(page).not.toMatch(/\.api\.(?:post|put)\s*\(/);
    expect(layout).toContain("route.name === 'project-workspace'");
    expect(layout).toContain(":key=\"routeViewKey\"");
    expect(owner.match(/createWorkspacePersistenceOwner\(/g)).toHaveLength(1);
    expect(port).toContain('const path = projectPath(projectId)');
    expect(port).toContain('const put = api.put.bind(api)');
    expect(port).toContain('await put<unknown>(path, payload, options)');
    expect(persistence).toContain('options.port.putProject(payload)');
  });

  it('does not turn the durable artifact store into a Project store or save coordinator', () => {
    const store = read(join(
      productRoot,
      'src/ClearVision.Product.Infrastructure/AI/Handoff/AiWorkspaceHandoffArtifactStore.cs'
    ));
    expect(store).not.toMatch(/ProjectSaveCoordinator|RuntimePackage|Station|PutProject|SaveProject/);
    expect(store).toContain('TimeSpan.FromMinutes(30)');
    expect(store).toContain('MaxActiveArtifactsPerOwner = 16');
    expect(store).toContain('MaxActiveArtifactsGlobal = 256');
  });
});
