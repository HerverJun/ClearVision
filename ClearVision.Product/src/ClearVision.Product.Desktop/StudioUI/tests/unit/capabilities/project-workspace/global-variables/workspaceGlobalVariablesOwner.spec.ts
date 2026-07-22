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
});
