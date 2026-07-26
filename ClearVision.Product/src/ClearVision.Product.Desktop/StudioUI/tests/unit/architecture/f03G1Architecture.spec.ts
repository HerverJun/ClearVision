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

describe('F03 G1-G5 architecture guards', () => {
  const files = sourceFiles(sourceRoot);
  const workspaceFiles = sourceFiles(workspaceRoot);

  it('keeps one direct fetch, one Product shell, one main and one Workspace owner implementation', () => {
    expect(files
      .filter(path => /\b(?:globalThis\.)?fetch\s*\(/.test(read(path)))
      .map(studioRelative))
      .toEqual(['src/platform/api/apiTransport.ts']);
    expect(files.filter(path => path.endsWith('ProductLayout.vue'))).toHaveLength(1);
    expect(files.filter(path => path.endsWith('workspaceOwner.ts'))).toHaveLength(1);
    expect(files.filter(path => /data-product-shell="ready"/.test(read(path))).map(studioRelative))
      .toEqual(['src/app/layouts/ProductLayout.vue']);
  });

  it('keeps Workspace HTTP on the shared transport and the G5 method allowlist', () => {
    const workspaceSource = workspaceFiles.map(read).join('\n');
    const transport = read(join(sourceRoot, 'platform/api/apiTransport.ts'));
    const auth = read(join(sourceRoot, 'app/auth/authLifecycleOwner.ts'));
    const query = read(join(workspaceRoot, 'workspaceQueries.ts'));

    expect(workspaceSource).not.toMatch(/\b(?:globalThis\.)?fetch\s*\(/);
    expect(workspaceSource).not.toMatch(/\.\s*(?:put|patch)\s*\(/i);
    expect(transport).toContain("method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'");
    const previewTransport = read(join(workspaceRoot, 'preview/previewTransport.ts'));
    expect(previewTransport).toContain("'flows/preview-node'");
    expect(previewTransport).toContain('preview-artifacts/');
    expect(previewTransport).not.toMatch(/api\/projects.*(?:PUT|POST)|inspection\/(?:admission|execute)/i);
    expect(auth).toContain("'auth/me'");
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
    const imageFacade = read(join(sourceRoot, 'platform/canvas/canonicalImageCanvas.ts'));
    const imageOwner = read(join(workspaceRoot, 'image/imageCanvasOwner.ts'));
    const imageAliasConsumers = files
      .filter(path => /from\s+['"]@clearvision\/canonical-image-canvas['"]/.test(read(path)))
      .map(studioRelative);

    expect(aliasConsumers).toEqual(['src/platform/canvas/canonicalFlowCanvas.ts']);
    expect(productionFacade).toContain('createHostedFlowCanvasAdapter');
    expect(productionFacade).toContain('new FlowEditorInteraction');
    expect(productionFacade).toContain('CanonicalFlowCanvasOwnerConflictError');
    expect(flowOwner).toContain("from '@/platform/canvas'");
    expect(labOwner).toContain("from '@/platform/canvas'");
    expect(flowOwner).not.toMatch(/\.raw\b|window\.|operator-library-hidden-host/);
    expect(productionFacade).toContain('patchNodeParameter');
    expect(productionFacade).toContain('patchNodeProperties');
    expect(flowOwner).toContain('openInspector');
    expect(imageFacade).toContain('new ImageCanvas');
    expect(imageFacade).toContain('CanonicalImageCanvasOwnerConflictError');
    expect(imageAliasConsumers).toEqual(['src/platform/canvas/canonicalImageCanvas.ts']);
    expect(imageOwner).toContain("from '@/platform/canvas'");
    expect(imageOwner).not.toMatch(/\bnew\s+ImageCanvas\s*\(/);
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
      expect(source, studioRelative(file)).not.toMatch(/RoiEditorPanel|PreviewPanelCapabilityOwner/);
    }
  });

  it('keeps Preview on the shared transport without the legacy static HTTP owner', () => {
    const previewTransportPath = join(workspaceRoot, 'preview/previewTransport.ts');
    const previewEndpointConsumers = workspaceFiles
      .filter(path => read(path).includes("'flows/preview-node'"))
      .map(studioRelative);
    const legacyCoordinator = read(join(
      repositoryRoot,
      'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js'
    ));

    expect(previewEndpointConsumers).toEqual([studioRelative(previewTransportPath)]);
    expect(files.map(read).join('\n')).not.toMatch(/core\/messaging\/httpClient\.js/);
    expect(legacyCoordinator).not.toMatch(/from\s+['"][^'"]*httpClient\.js['"]/);
    expect(read(join(workspaceRoot, 'preview/previewOwner.ts')))
      .toContain("from '@clearvision/canonical-preview-coordinator'");
  });

  it('keeps G6 on one persisted-snapshot Run owner without a parallel execution authority', () => {
    const plan = read(join(
      repositoryRoot,
      'docs/进行中/StudioUINext/Studio_UI_Next_F03_完整开发计划.md'
    ));
    expect(plan).toContain('F03_IMPLEMENTED=YES');
    expect(existsSync(join(workspaceRoot, 'flow'))).toBe(true);
    expect(sourceFiles(join(workspaceRoot, 'flow')).map(studioRelative)).toEqual(expect.arrayContaining([
      'src/capabilities/project-workspace/flow/flowCanvasOwner.ts',
      'src/capabilities/project-workspace/flow/OperatorRail.vue',
      'src/capabilities/project-workspace/flow/FlowCanvasSurface.vue'
    ]));
    expect(existsSync(join(workspaceRoot, 'inspector'))).toBe(true);
    expect(sourceFiles(join(workspaceRoot, 'inspector')).map(studioRelative)).toEqual(expect.arrayContaining([
      'src/capabilities/project-workspace/inspector/inspectorOwner.ts',
      'src/capabilities/project-workspace/inspector/InspectorPanel.vue',
      'src/capabilities/project-workspace/inspector/ParameterEditor.vue',
      'src/capabilities/project-workspace/inspector/parameterValidation.ts'
    ]));
    expect(existsSync(join(workspaceRoot, 'preview'))).toBe(true);
    expect(existsSync(join(workspaceRoot, 'image'))).toBe(true);
    expect(existsSync(join(workspaceRoot, 'roi'))).toBe(true);
    expect(sourceFiles(join(workspaceRoot, 'preview')).map(studioRelative)).toEqual(expect.arrayContaining([
      'src/capabilities/project-workspace/preview/previewOwner.ts',
      'src/capabilities/project-workspace/preview/previewTransport.ts',
      'src/capabilities/project-workspace/preview/PreviewPanel.vue'
    ]));
    expect(existsSync(join(workspaceRoot, 'persistence'))).toBe(true);
    expect(sourceFiles(join(workspaceRoot, 'persistence')).map(studioRelative)).toEqual(expect.arrayContaining([
      'src/capabilities/project-workspace/persistence/projectPersistencePort.ts',
      'src/capabilities/project-workspace/persistence/index.ts',
      'src/capabilities/project-workspace/persistence/workspacePersistenceOwner.ts'
    ]));
    const persistenceSource = sourceFiles(join(workspaceRoot, 'persistence')).map(read).join('\n');
    expect(persistenceSource).toContain('api.put.bind(api)');
    expect(persistenceSource).toContain('projects/${projectId}');
    expect(persistenceSource).not.toMatch(
      /['"`](?:inspection\/(?:admission|execute)|runs\/|results\/|runtime\/)/i
    );
    const runRoot = join(workspaceRoot, 'run');
    expect(existsSync(runRoot)).toBe(true);
    expect(sourceFiles(runRoot).map(studioRelative)).toEqual(expect.arrayContaining([
      'src/capabilities/project-workspace/run/runContracts.ts',
      'src/capabilities/project-workspace/run/runCommandOwner.ts'
    ]));
    const runSource = sourceFiles(runRoot).map(read).join('\n');
    expect(runSource).toContain("'inspection/admission'");
    expect(runSource).toContain("'inspection/execute'");
    expect(runSource).toContain('expectedPersistenceRevision');
    expect(runSource).toContain('expectedCanonicalFlowHash');
    expect(runSource).toContain('expectedDecisionConfigurationHash');
    expect(runSource).toContain('unknown-outcome');
    expect(runSource).not.toMatch(/FlowData|new\s+EventSource|WebMessage|fetch\s*\(/);
  });

  it('keeps Inspector on the G2 projection/commands without Host, Preview, save, or raw Canvas authority', () => {
    const inspectorRoot = join(workspaceRoot, 'inspector');
    const inspectorSource = sourceFiles(inspectorRoot).map(read).join('\n');
    expect(inspectorSource).toContain('FlowCanvasOwner');
    expect(inspectorSource).toContain('patchNodeParameter');
    expect(inspectorSource).toContain('patchNodeProperties');
    expect(inspectorSource).toContain('disconnect');
    expect(inspectorSource).not.toMatch(/FlowCanvas\.serialize|\b(?:flowOwner|canvas)\s*\.\s*raw\b|new\s+FlowCanvas/);
    expect(inspectorSource).not.toMatch(/HostBridge|FilePickedEvent|CameraBindingQuery|preview-node|api\/projects.*PUT/i);
  });
});
