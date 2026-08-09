import { describe, expect, it, vi } from 'vitest';
import { ApiAbortError, type ApiTransport } from '@/platform/api';
import { decodeWorkspaceProjectV1 } from '@/capabilities/project-workspace';
import {
  createWorkspaceGlobalVariablesOwner,
  normalizeGlobalVariableDataType
} from '@/capabilities/project-workspace/global-variables';
import { createWorkspaceLifecycleDiagnosticsOwner } from '@/capabilities/project-workspace/workspaceLifecycleDiagnostics';

const projectId = '11111111-1111-4111-8111-111111111111';
const nextProjectId = '33333333-3333-4333-8333-333333333333';
function baseline(id = projectId) {
  return decodeWorkspaceProjectV1({
    id, name: 'P', description: null, version: '1', persistenceRevision: 1, flow: null,
    globalSettings: {}, globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] },
    createdAt: '2026-07-22T00:00:00Z', modifiedAt: '2026-07-22T00:00:00Z', lastOpenedAt: null
  }).globalVariables;
}

describe('workspaceGlobalVariablesOwner', () => {
  it('normalizes persisted numeric scalar port types without admitting image or unknown types', () => {
    expect([1, 2, 3, 4].map(normalizeGlobalVariableDataType))
      .toEqual(['Int64', 'Double', 'Boolean', 'String']);
    expect(normalizeGlobalVariableDataType('2')).toBe('Double');
    expect(normalizeGlobalVariableDataType(0)).toBe('Unknown');
    expect(normalizeGlobalVariableDataType(99)).toBe('Unknown');
  });

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

  it('validates binding identity and scalar compatibility against the current flow draft', () => {
    const variableId = '22222222-2222-4222-8222-222222222222';
    const api = { apiBaseUrl: 'http://localhost/api' } as unknown as ApiTransport;
    const flow = {
      id: 'flow-1',
      name: 'Flow',
      operators: [{
        id: 'op-1',
        outputPorts: [{ id: 'out-1', dataType: 2 }],
        parameters: [{ id: 'param-1', dataType: 'bool' }]
      }],
      connections: [],
      decisionConfiguration: null,
      opaquePassthrough: {}
    };
    const owner = createWorkspaceGlobalVariablesOwner({
      projectId,
      baseline: baseline(),
      api,
      getFlowDraft: () => flow
    });
    owner.upsertDefinition({ id: variableId, name: 'Score', displayName: '得分', valueType: 'Double', initialValue: 0 });
    owner.upsertSourceBinding({
      variableId, operatorId: 'op-1', outputPortId: 'out-1', operatorName: '算子', outputPortName: '输出'
    });
    owner.upsertTargetBinding({
      variableId, operatorId: 'op-1', parameterId: 'param-1', operatorName: '算子', parameterName: '布尔参数'
    });

    expect(owner.apply()).toBe(false);
    expect(owner.projection.fieldErrors).toEqual(expect.arrayContaining([
      expect.objectContaining({ code: 'GV015', parameterId: 'param-1' })
    ]));
    expect(owner.projection.fieldErrors).not.toEqual(expect.arrayContaining([
      expect.objectContaining({ code: 'GV014', portId: 'out-1' })
    ]));
    owner.dispose();
  });

  it('uses structured server diagnostics without parsing messages', () => {
    const owner = createWorkspaceGlobalVariablesOwner({ projectId, baseline: baseline(), api: { apiBaseUrl: 'http://localhost/api', get: vi.fn() } as unknown as ApiTransport });
    owner.setServerDiagnostics({ diagnostics: [{ code: 'GV011', message: 'missing', field: 'globalVariables.targetBindings[0].parameterId', variableId: null, operatorId: 'op', portId: null, parameterId: 'parameter', severity: 'Error' }] });
    expect(owner.projection.fieldErrors[0]).toMatchObject({ code: 'GV011', field: 'globalVariables.targetBindings[0].parameterId', parameterId: 'parameter' });
  });

  it('blocks local and runtime writes while the session is quarantined, then restores the owner after reauthentication', async () => {
    const put = vi.fn();
    const owner = createWorkspaceGlobalVariablesOwner({
      projectId,
      baseline: baseline(),
      api: { apiBaseUrl: 'http://localhost/api', put } as unknown as ApiTransport
    });

    owner.setReadonly('会话已失效');
    expect(owner.upsertDefinition({ name: 'Blocked', displayName: 'Blocked', valueType: 'Int64', initialValue: 0 })).toBeNull();
    expect(owner.apply()).toBe(false);
    expect(await owner.writeRuntimeValue('missing', 1)).toBe(false);
    expect(put).not.toHaveBeenCalled();

    owner.clearReadonly();
    expect(owner.upsertDefinition({ name: 'Restored', displayName: 'Restored', valueType: 'Int64', initialValue: 0 })).not.toBeNull();
    owner.dispose();
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
    expect(api.put).toHaveBeenCalledWith(
      `projects/${projectId}/global-variable-values/${variableId}`,
      { value: 8, expectedVersion: 0 },
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(await owner.resetRuntimeValue(variableId, 1)).toBe(true);
    expect(api.post).toHaveBeenCalledWith(
      `projects/${projectId}/global-variable-values/${variableId}/reset`,
      { expectedVersion: 1 },
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(await owner.resetAllRuntimeValues({ [variableId]: 2 })).toBe(true);
    expect(api.post).toHaveBeenCalledWith(
      `projects/${projectId}/global-variable-values/reset`,
      { expectedVersions: { [variableId]: 2 } },
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
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

  it('does not let a disposed project runtime read overwrite the next project projection', async () => {
    let resolveOldRead: ((value: unknown) => void) | undefined;
    const oldApi = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async () => await new Promise<unknown>(resolve => {
        resolveOldRead = resolve;
      }))
    } as unknown as ApiTransport;
    const oldOwner = createWorkspaceGlobalVariablesOwner({ projectId, baseline: baseline(), api: oldApi });
    const oldRefresh = oldOwner.refreshRuntimeValues();
    await Promise.resolve();
    oldOwner.dispose();

    const nextApi = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async () => [{
        variableId: '22222222-2222-4222-8222-222222222222', name: 'Count', displayName: '计数',
        valueType: 'Int64', value: 'new-project', version: 4, updatedAtUtc: '2026-07-22T00:00:00Z',
        updatedBy: 'InspectionRun', runId: null, operatorId: null
      }])
    } as unknown as ApiTransport;
    const nextOwner = createWorkspaceGlobalVariablesOwner({
      projectId: nextProjectId,
      baseline: baseline(nextProjectId),
      api: nextApi
    });
    await nextOwner.refreshRuntimeValues();
    expect(nextOwner.projection.runtimeValues[0]).toMatchObject({ value: 'new-project', version: 4 });

    resolveOldRead?.([{
      variableId: '22222222-2222-4222-8222-222222222222', name: 'Count', displayName: '计数',
      valueType: 'Int64', value: 'old-project', version: 3, updatedAtUtc: '2026-07-22T00:00:00Z',
      updatedBy: 'InspectionRun', runId: null, operatorId: null
    }]);
    await oldRefresh;

    expect(oldOwner.projection.phase).toBe('disposed');
    expect(oldOwner.projection.runtimeValues).toHaveLength(0);
    expect(nextOwner.projection.runtimeValues[0]).toMatchObject({ value: 'new-project', version: 4 });
    nextOwner.dispose();
  });

  it('aborts runtime reads during leave and releases request resources before disposal', async () => {
    let signal: AbortSignal | undefined;
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async (_path: string, options?: { signal?: AbortSignal }) => await new Promise<never>((_resolve, reject) => {
        signal = options?.signal;
        signal?.addEventListener('abort', () => reject(new ApiAbortError('global-variable-values')), { once: true });
      }))
    } as unknown as ApiTransport;
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const owner = createWorkspaceGlobalVariablesOwner({ projectId, baseline: baseline(), api, diagnostics });
    const refresh = owner.refreshRuntimeValues();

    expect(signal).toBeDefined();
    expect(diagnostics.diagnostics).toMatchObject({
      capabilityOwnerCounts: { 'global-variables': 1 },
      activeAbortControllers: 1,
      inFlightReads: 1
    });

    const leaving = owner.prepareForLeave();
    expect(signal?.aborted).toBe(true);
    await expect(leaving).resolves.toBe(true);
    await refresh;
    expect(diagnostics.diagnostics).toMatchObject({
      capabilityOwnerCounts: { 'global-variables': 1 },
      activeAbortControllers: 0,
      inFlightReads: 0,
      inFlightWrites: 0
    });

    owner.dispose();
    expect(diagnostics.diagnostics.capabilityOwnerCounts['global-variables']).toBe(0);
    diagnostics.dispose();
  });
});
