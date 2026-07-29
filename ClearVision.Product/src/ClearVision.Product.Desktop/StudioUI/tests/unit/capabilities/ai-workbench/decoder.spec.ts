import { describe, expect, it } from 'vitest';
import { AiContractDecodeError } from '@/capabilities/ai-workbench/contracts';
import {
  decodeAiAgentRunEventV1,
  decodeAiIntentResultV1,
  decodeAiOperationProjectionV1,
  decodeAiPlanV1,
  decodeAiSessionDetailV1
} from '@/capabilities/ai-workbench/decoder';
import {
  aiProjectId,
  intentFixture,
  operationFixture,
  planFixture,
  runEventFixture,
  sessionFixture
} from './aiFixtures';

describe('AI workbench public decoders', () => {
  it('accepts the G2 snapshot projection and rejects unapproved fields', () => {
    const decoded = decodeAiSessionDetailV1(sessionFixture({
      planQuestionSelections: { defect_definition: '2 mm' }
    }));
    expect(decoded.snapshot.revision).toBe(3);
    expect(decoded.snapshot.planQuestionSelections.defect_definition).toBe('2 mm');
    expect(() => decodeAiSessionDetailV1({ ...sessionFixture(), reasoning: 'private' }))
      .toThrow(AiContractDecodeError);
    expect(() => decodeAiSessionDetailV1({
      ...sessionFixture(),
      snapshot: { ...sessionFixture().snapshot, authorization: 'Bearer secret' }
    })).toThrow(AiContractDecodeError);
  });

  it('strictly decodes Intent and replay-safe Plan public projections', () => {
    expect(decodeAiIntentResultV1(intentFixture()).semanticExtraction?.inspectionObject).toBe('冲压件');
    expect(decodeAiPlanV1(planFixture()).clarificationQuestions).toHaveLength(1);
    expect(() => decodeAiIntentResultV1({ ...intentFixture(), privateTrace: 'hidden' }))
      .toThrow(AiContractDecodeError);
    expect(() => decodeAiPlanV1({ ...planFixture(), rawPayload: 'hidden' }))
      .toThrow(AiContractDecodeError);
  });

  it('requires a redacted metadata-only event and matching Plan identity', () => {
    const event = decodeAiAgentRunEventV1(runEventFixture(2, 'plan.completed'));
    expect(event.plan?.planId).toBe('plan_fixture_01');
    expect(() => decodeAiAgentRunEventV1({ ...runEventFixture(1), redactionPass: false }))
      .toThrow(AiContractDecodeError);
    const mismatched = runEventFixture(2, 'plan.completed');
    const payload = mismatched.payload as Record<string, unknown>;
    expect(() => decodeAiAgentRunEventV1({
      ...mismatched,
      payload: { ...payload, planHash: 'f'.repeat(64) }
    })).toThrow(AiContractDecodeError);
  });

  it('fails closed on malformed operation and baseline projections', () => {
    const operation = {
      ...operationFixture('plan_run'),
      projectBaseline: {
        targetKind: 'existing', projectId: aiProjectId, persistenceRevision: 4,
        canonicalFlowHash: 'b'.repeat(64)
      }
    };
    expect(decodeAiOperationProjectionV1(operation).projectBaseline?.persistenceRevision).toBe(4);
    expect(() => decodeAiOperationProjectionV1({
      ...operation,
      projectBaseline: { ...operation.projectBaseline, canonicalFlowHash: 'draft-flow' }
    })).toThrow(AiContractDecodeError);
    expect(() => decodeAiOperationProjectionV1({ ...operation, privatePayload: 'secret' }))
      .toThrow(AiContractDecodeError);
  });
});
