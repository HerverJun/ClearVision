import { describe, expect, it, vi } from 'vitest';
import type { ApiTransport } from '@/platform/api';
import { decodeWorkspaceProjectV1 } from '@/capabilities/project-workspace';
import { createWorkspaceGlobalVariablesOwner } from '@/capabilities/project-workspace/global-variables';

const projectId = '11111111-1111-4111-8111-111111111111';
function baseline() {
  return decodeWorkspaceProjectV1({
    id: projectId, name: 'P', description: null, version: '1', persistenceRevision: 1, flow: null,
    globalSettings: {}, globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] },
    createdAt: '2026-07-22T00:00:00Z', modifiedAt: '2026-07-22T00:00:00Z', lastOpenedAt: null
  }).globalVariables;
}

describe('workspaceGlobalVariablesOwner', () => {
  it('supports definition and binding drafts, apply/cancel, and read-only runtime values', async () => {
    const api = { apiBaseUrl: 'http://localhost/api', get: vi.fn(async () => [{
      variableId: '22222222-2222-4222-8222-222222222222', name: 'Count', displayName: '计数', valueType: 'Int64', value: '12', version: 3,
      updatedAtUtc: '2026-07-22T00:00:00Z', updatedBy: 'InspectionRun', runId: null, operatorId: null
    }]) } as unknown as ApiTransport;
    const owner = createWorkspaceGlobalVariablesOwner({ projectId, baseline: baseline(), api });
    const variableId = owner.upsertDefinition({ name: 'Count', displayName: '计数', valueType: 'Int64', initialValue: 0, min: 0, max: 100 })!;
    owner.upsertSourceBinding({ variableId, operatorId: crypto.randomUUID(), outputPortId: crypto.randomUUID(), operatorName: '统计', outputPortName: 'Count' });
    owner.upsertTargetBinding({ variableId, operatorId: crypto.randomUUID(), parameterId: crypto.randomUUID(), operatorName: '阈值', parameterName: 'Threshold' });
    expect(owner.projection.dirty).toBe(true);
    expect(owner.apply()).toBe(true);
    expect(owner.getApplied()).toMatchObject({ variables: [{ name: 'Count' }] });

    owner.removeDefinition(variableId);
    expect(owner.projection.draft.variables).toHaveLength(0);
    owner.cancel();
    expect(owner.projection.draft.variables).toHaveLength(1);
    await owner.refreshRuntimeValues();
    expect(owner.projection.runtimeValues[0]).toMatchObject({ value: '12', version: 3 });
    expect(owner.projection.applied.variables[0]?.initialValue).toBe(0);
    owner.dispose();
  });

  it('uses structured server diagnostics without parsing messages', () => {
    const owner = createWorkspaceGlobalVariablesOwner({ projectId, baseline: baseline(), api: { apiBaseUrl: 'http://localhost/api', get: vi.fn() } as unknown as ApiTransport });
    owner.setServerDiagnostics({ diagnostics: [{ code: 'GV011', message: 'missing', field: 'globalVariables.targetBindings[0].parameterId', variableId: null, operatorId: 'op', portId: null, parameterId: 'parameter', severity: 'Error' }] });
    expect(owner.projection.fieldErrors[0]).toMatchObject({ code: 'GV011', field: 'globalVariables.targetBindings[0].parameterId', parameterId: 'parameter' });
  });

  it('keeps runtime writes and resets separate from the definition draft', async () => {
    const variableId = '22222222-2222-4222-8222-222222222222';
    const runtime = (value: string, version: number) => [{
      variableId, name: 'Count', displayName: '计数', valueType: 'Int64', value, version,
      updatedAtUtc: '2026-07-22T00:00:00Z', updatedBy: 'StudioManual', runId: null, operatorId: null
    }];
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async () => runtime('5', 0)),
      put: vi.fn(async () => runtime('8', 1)),
      post: vi.fn(async (path: string) => path.endsWith('/reset') ? runtime('5', 2) : runtime('5', 2))
    } as unknown as ApiTransport;
    const owner = createWorkspaceGlobalVariablesOwner({ projectId, baseline: baseline(), api });
    owner.upsertDefinition({ id: variableId, name: 'Count', displayName: '计数', valueType: 'Int64', initialValue: 5, manualWriteAllowed: true });
    expect(owner.apply()).toBe(true);
    await owner.refreshRuntimeValues();
    expect(await owner.writeRuntimeValue(variableId, 8, 0)).toBe(true);
    expect(owner.projection.dirty).toBe(false);
    expect(api.put).toHaveBeenCalledWith(`projects/${projectId}/global-variable-values/${variableId}`, { value: 8, expectedVersion: 0 });
    expect(await owner.resetRuntimeValue(variableId, 1)).toBe(true);
    expect(api.post).toHaveBeenCalledWith(`projects/${projectId}/global-variable-values/${variableId}/reset`, { expectedVersion: 1 });
    expect(await owner.resetAllRuntimeValues({ [variableId]: 2 })).toBe(true);
    expect(api.post).toHaveBeenCalledWith(`projects/${projectId}/global-variable-values/reset`, { expectedVersions: { [variableId]: 2 } });
  });

  it('rejects runtime mutation for variables that do not allow manual writes', async () => {
    const variableId = '22222222-2222-4222-8222-222222222222';
    const api = {
      apiBaseUrl: 'http://localhost/api',
      put: vi.fn(),
      post: vi.fn()
    } as unknown as ApiTransport;
    const owner = createWorkspaceGlobalVariablesOwner({ projectId, baseline: baseline(), api });
    owner.upsertDefinition({ id: variableId, name: 'Count', displayName: '计数', valueType: 'Int64', initialValue: 5 });
    expect(owner.apply()).toBe(true);
    expect(await owner.writeRuntimeValue(variableId, 8)).toBe(false);
    expect(await owner.resetRuntimeValue(variableId)).toBe(false);
    expect(await owner.resetAllRuntimeValues()).toBe(false);
    expect(api.put).not.toHaveBeenCalled();
    expect(api.post).not.toHaveBeenCalled();
    expect(owner.projection.runtimeErrorCode).toBe('GV030');
  });
});
