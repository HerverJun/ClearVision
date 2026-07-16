import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const studioRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const sourceRoot = join(studioRoot, 'src');
const workspaceRoot = join(sourceRoot, 'capabilities/project-workspace');
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

describe('F03 G1-G2 architecture guards', () => {
  const files = sourceFiles(sourceRoot);
  const workspaceFiles = sourceFiles(workspaceRoot);

  it('keeps one direct fetch, one Product shell, one main and one Workspace owner implementation', () => {
    expect(files
      .filter(path => /\b(?:globalThis\.)?fetch\s*\(/.test(read(path)))
      .map(studioRelative))
      .toEqual(['src/platform/api/apiTransport.ts']);
    expect(files.filter(path => path.endsWith('ProductLayout.vue'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('workspaceOwner.ts'))).toHaveLength(1);
    expect(files
      .filter(path => !studioRelative(path).startsWith('src/labs/'))
      .filter(path => /<main(?:\s|>)/.test(read(path)))
      .map(studioRelative))
      .toEqual(['src/app/layouts/ProductLayout.vue']);
  });

  it('keeps the Workspace capability on a narrow GET-only read port', () => {
    const workspaceSource = workspaceFiles.map(read).join('\n');
    const transport = read(join(sourceRoot, 'platform/api/apiTransport.ts'));
    const session = read(join(sourceRoot, 'app/session/sessionProjectionOwner.ts'));
    const query = read(join(workspaceRoot, 'workspaceQueries.ts'));

    expect(workspaceSource).not.toMatch(/\bApiTransport\b/);
    expect(workspaceSource).not.toMatch(/\b(?:globalThis\.)?fetch\s*\(/);
    expect(workspaceSource).not.toMatch(/\b(?:api|transport|client)\s*\.\s*(?:post|put|patch|delete)\s*\(/i);
    expect(workspaceSource).not.toMatch(/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i);
    expect(transport).toContain("method: 'GET'");
    expect(transport).not.toMatch(/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i);
    expect(session).toContain("path: 'auth/me'");
    expect(query).toContain('return `projects/${projectId}`');
    expect(query).not.toMatch(/preview|artifact|admission|execute|results|upload|global-variables/i);
    expect(read(join(sourceRoot, 'capabilities/operators-read/operatorQueries.ts')))
      .toContain("operators/library?includeCompatibility=true");
  });

  it('uses one production canonical facade for Lab and Workspace without raw Canvas exposure', () => {
    const aliasConsumers = files
      .filter(path => /from\s+['"]@clearvision\/canonical-flow-(?:canvas|interaction)['"]/.test(read(path)))
      .map(studioRelative);
    const productionFacade = read(join(sourceRoot, 'platform/canvas/canonicalFlowCanvas.ts'));
    const flowOwner = read(join(workspaceRoot, 'flow/flowCanvasOwner.ts'));
    const labOwner = read(join(sourceRoot, 'labs/canvas/canvasLabOwner.ts'));

    expect(aliasConsumers).toEqual(['src/platform/canvas/canonicalFlowCanvas.ts']);
    expect(productionFacade).toContain('createHostedFlowCanvasAdapter');
    expect(productionFacade).toContain('new FlowEditorInteraction');
    expect(productionFacade).toContain('CanonicalFlowCanvasOwnerConflictError');
    expect(flowOwner).toContain("from '@/platform/canvas'");
    expect(labOwner).toContain("from '@/platform/canvas'");
    expect(flowOwner).not.toMatch(/\.raw\b|window\.|operator-library-hidden-host/);
  });

  it('forbids Labs, FrontendV2, raw Canvas and global command bypasses from Workspace production', () => {
    for (const file of workspaceFiles) {
      const source = read(file);
      expect(source, studioRelative(file)).not.toMatch(/from\s+['"][^'"]*(?:\/labs\/|FrontendV2)/);
      expect(source, studioRelative(file)).not.toMatch(/\bnew\s+(?:FlowCanvas|ImageCanvas)\s*\(/);
      expect(source, studioRelative(file)).not.toMatch(/from\s+['"]@clearvision\/canonical-flow/i);
      expect(source, studioRelative(file)).not.toContain('window.flowCanvas');
      expect(source, studioRelative(file)).not.toContain('FlowCanvas.serialize()');
      expect(source, studioRelative(file)).not.toMatch(/\b(?:EventBus|ServiceRegistry|EventSource)\b/);
      expect(source, studioRelative(file)).not.toMatch(/\bchrome\s*\?*\.\s*webview\b/);
    }
  });

  it('keeps G3-G6 absent while G1 and G2 are the only implemented F03 slices', () => {
    const plan = read(join(
      repositoryRoot,
      'docs/进行中/StudioUINext/Studio_UI_Next_F03_完整开发计划.md'
    ));
    expect(plan).toContain('F03_IMPLEMENTED=NO');
    expect(existsSync(join(workspaceRoot, 'flow'))).toBe(true);
    expect(sourceFiles(join(workspaceRoot, 'flow')).map(studioRelative)).toEqual(expect.arrayContaining([
      'src/capabilities/project-workspace/flow/flowCanvasOwner.ts',
      'src/capabilities/project-workspace/flow/OperatorRail.vue',
      'src/capabilities/project-workspace/flow/FlowCanvasSurface.vue'
    ]));
    expect(existsSync(join(workspaceRoot, 'inspector'))).toBe(false);
    expect(existsSync(join(workspaceRoot, 'preview'))).toBe(false);
    expect(existsSync(join(workspaceRoot, 'persistence'))).toBe(false);
    expect(existsSync(join(workspaceRoot, 'run'))).toBe(false);
  });
});
