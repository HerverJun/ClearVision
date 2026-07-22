import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

const root = join(import.meta.dirname, '../../..');
const read = (relative: string) => readFileSync(join(root, relative), 'utf8');

describe('F04-R G3 authority and owner guards', () => {
  it('keeps capability writes on shared ApiTransport and the unified Project payload', () => {
    const camera = read('src/capabilities/project-workspace/camera/cameraBindingEditorOwner.ts');
    const variables = read('src/capabilities/project-workspace/global-variables/workspaceGlobalVariablesOwner.ts');
    const decision = read('src/capabilities/project-workspace/final-decision/finalDecisionOwner.ts');
    const packageOwner = read('src/capabilities/project-workspace/runtime-package/runtimePackageExportOwner.ts');
    for (const source of [camera, variables, decision, packageOwner]) {
      expect(source).not.toMatch(/\bfetch\s*\(/);
      expect(source).not.toMatch(/localStorage|EventBus|createApiTransport/);
    }
    expect(variables).not.toContain('/global-variables');
    expect(decision).not.toContain('projects/');
    expect(packageOwner).toContain('Deliberately no Flow override');
    expect(packageOwner).not.toMatch(/\bflow\s*:/i);
  });

  it('mounts each Workspace child owner exactly once and disposes it', () => {
    const workspace = read('src/capabilities/project-workspace/workspaceOwner.ts');
    for (const factory of ['createWorkspaceGlobalVariablesOwner', 'createFinalDecisionOwner', 'createRuntimePackageExportOwner']) {
      expect(workspace.match(new RegExp(`${factory}\\(`, 'g'))).toHaveLength(1);
    }
    expect(workspace).toContain('runtimePackageExportOwner?.dispose()');
    expect(workspace).toContain('finalDecisionOwner?.dispose()');
    expect(workspace).toContain('globalVariablesOwner?.dispose()');
  });
});
