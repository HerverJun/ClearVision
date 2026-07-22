import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import type { ApiTransport } from '@/platform/api';
import type { FlowCanvasOwner } from '@/capabilities/project-workspace/flow';
import { createFinalDecisionOwner, finalDecisionCandidateKey } from '@/capabilities/project-workspace/final-decision';

const operatorId = '11111111-1111-4111-8111-111111111111';
const portId = '22222222-2222-4222-8222-222222222222';

describe('finalDecisionOwner', () => {
  it('consumes backend candidates, validates fields, and applies through the canonical Flow command', async () => {
    const projection = reactive({
      draft: { id: crypto.randomUUID(), name: 'F', operators: [], connections: [], decisionConfiguration: null, opaquePassthrough: {} },
      runtime: { flowRevision: 1 }, mutationGate: 'editable'
    });
    const patchDecisionConfiguration = vi.fn(() => ({ ok: true, code: 'ok', message: 'ok', flowRevision: 2 }));
    const flowOwner = { projection, commands: { patchDecisionConfiguration } } as unknown as FlowCanvasOwner;
    const candidate = { operatorId, operatorName: '测量', outputPortId: portId, outputName: 'Width', dataType: 'Float', rule: 'NumericComparison', defaultTrueMeansOk: null, defaultOkValue: null, defaultNgValue: null, requiredOkValue: null, requiredNgValue: null };
    const post = vi.fn(async (_path: string, body: unknown) => {
      const decision = (body as { decisionConfiguration?: { finalDecisionBinding?: unknown } }).decisionConfiguration?.finalDecisionBinding;
      return { isValid: Boolean(decision), issues: decision ? [] : [{ code: 'DECISION_BINDING_REQUIRED', message: 'required', field: 'decisionConfiguration.finalDecisionBinding', operatorId: null, outputName: null }], eligibleOutputs: [candidate] };
    });
    const owner = createFinalDecisionOwner({ flowOwner, api: { apiBaseUrl: 'http://localhost/api', get: vi.fn(), post } as unknown as ApiTransport, initial: null });
    await vi.waitFor(() => expect(owner.projection.candidates).toHaveLength(1));
    owner.selectCandidate(finalDecisionCandidateKey(owner.projection.candidates[0]!));
    owner.patchBinding({ comparator: 'LessThanOrEqual', threshold: 12.5 });
    await expect(owner.apply()).resolves.toBe(true);
    expect(patchDecisionConfiguration).toHaveBeenCalledWith(expect.objectContaining({ finalDecisionBinding: expect.objectContaining({ threshold: 12.5 }) }));
    expect(owner.projection.dirty).toBe(false);
    owner.dispose();
  });
});
