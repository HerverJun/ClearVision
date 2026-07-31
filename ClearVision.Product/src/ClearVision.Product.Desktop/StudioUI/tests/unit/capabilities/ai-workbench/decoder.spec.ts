import { describe, expect, it } from 'vitest';
import { AiContractDecodeError } from '@/capabilities/ai-workbench/contracts';
import {
  decodeAiAgentRunEventV1,
  decodeAiAgentRunReplayV1,
  decodeAiBuildResultV1,
  decodeAiBuildRevalidationResponseV1,
  decodeAiIntentResultV1,
  decodeAiOperationProjectionV1,
  decodeAiPlanV1,
  decodeAiRunHistoryPageV1,
  decodeAiSessionPageV1,
  decodeAiSessionDetailV1
} from '@/capabilities/ai-workbench/decoder';
import {
  aiProjectId,
  aiTimestamp,
  buildParameterFixture,
  buildResultFixture,
  intentFixture,
  operationFixture,
  planFixture,
  replayFixture,
  resourceRequirementFixture,
  runEventFixture,
  sessionFixture,
  snapshotFixture
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

  it('strictly decodes owner-bound Session and Run history pages', () => {
    const sessions = {
      items: [{ sessionId: 'session_01', lifecycleState: 'plan_ready', projectId: null,
        revision: 7, updatedAtUtc: aiTimestamp }],
      offset: 0, limit: 10, total: 1
    };
    const runs = {
      items: [{
        runId: 'run_plan_01', sessionId: 'session_01', kind: 'plan', status: 'completed',
        title: '方案规划', summary: '规划已完成。', firstFixRecommendation: '', recoveryState: 'terminal',
        createdAtUtc: aiTimestamp, updatedAtUtc: aiTimestamp, lastSequence: 3, eventCount: 3
      }],
      offset: 0, limit: 10, total: 1
    };
    expect(decodeAiSessionPageV1(sessions).items[0]?.revision).toBe(7);
    expect(decodeAiRunHistoryPageV1(runs).items[0]?.kind).toBe('plan');
    expect(() => decodeAiSessionPageV1({ ...sessions, ownerHash: 'private' })).toThrow(AiContractDecodeError);
    expect(() => decodeAiRunHistoryPageV1({
      ...runs,
      items: [{ ...runs.items[0], rawReasoning: 'private' }]
    })).toThrow(AiContractDecodeError);
    expect(() => decodeAiRunHistoryPageV1({ ...runs, limit: 0 })).toThrow(AiContractDecodeError);
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

  it('preserves replay snapshot and diagnostics while rejecting malformed or private projections', () => {
    const replay = replayFixture();
    const decoded = decodeAiAgentRunReplayV1(replay);
    expect(decoded.snapshot.storageVersion).toBe('agent-run-events.jsonl.v1');
    expect(decoded.snapshot.events).toHaveLength(3);
    expect(decoded.diagnostics).toMatchObject({ eventCount: 3, redactionPass: true });
    expect(() => decodeAiAgentRunReplayV1({
      ...replay,
      diagnostics: { ...replay.diagnostics, rawReasoning: 'private' }
    })).toThrow(AiContractDecodeError);
    expect(() => decodeAiAgentRunReplayV1({
      ...replay,
      snapshot: { ...replay.snapshot, events: replay.snapshot.events.slice(0, 2) }
    })).toThrow(AiContractDecodeError);
    expect(() => decodeAiAgentRunReplayV1({
      ...replay,
      diagnostics: { ...replay.diagnostics, redactionPass: false }
    })).toThrow(AiContractDecodeError);
    expect(() => decodeAiAgentRunReplayV1({
      ...replay,
      snapshot: { ...replay.snapshot, runId: 'run_other' }
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

  it('decodes the redacted Build DTO while preserving null and empty scalar values', () => {
    const emptyString = buildParameterFixture({
      canonicalKey: 'threshold_1.label', parameterName: 'label', parameterDisplayName: 'Label',
      dataType: 'string', isRequired: false, value: '', hasExplicitValue: true,
      valueSummary: '', pending: false, defaultValue: '', minValue: null, maxValue: null
    });
    const build = buildResultFixture({
      parameterMapping: [buildParameterFixture(), emptyString]
    });
    const decoded = decodeAiBuildResultV1(build);
    expect(decoded.parameterMapping[0]?.value).toBeNull();
    expect(decoded.parameterMapping[1]?.value).toBe('');
    expect(decoded.validation.applyGate.blocked).toBe(true);
    expect(() => decodeAiBuildResultV1({ ...build, flow: { nodes: [] } }))
      .toThrow(AiContractDecodeError);
    expect(() => decodeAiBuildResultV1({ ...build, rawTrace: 'private' }))
      .toThrow(AiContractDecodeError);
  });

  it('preserves all/any condition sets and rejects unknown condition identities', () => {
    const mode = buildParameterFixture({
      canonicalKey: 'threshold_1.mode', parameterName: 'mode', dataType: 'string',
      requiredWhen: { allConditions: [], anyConditions: [] }
    });
    const value = buildParameterFixture({
      canonicalKey: 'threshold_1.value', parameterName: 'value', dataType: 'string',
      requiredWhen: {
        allConditions: [{ parameter: 'mode', comparison: 'not-empty', value: null }],
        anyConditions: [
          { parameter: 'mode', comparison: 'equals', value: 'a' },
          { parameter: 'mode', comparison: 'equals', value: 'b' }
        ]
      },
      enabledWhen: { allConditions: [], anyConditions: [] },
      disabledWhen: null
    });
    const decoded = decodeAiBuildResultV1(buildResultFixture({ parameterMapping: [mode, value] }));
    expect(decoded.parameterMapping[1]?.requiredWhen?.allConditions).toHaveLength(1);
    expect(decoded.parameterMapping[1]?.requiredWhen?.anyConditions).toHaveLength(2);

    expect(() => decodeAiBuildResultV1(buildResultFixture({
      parameterMapping: [{ ...value, requiredWhen: {
        allConditions: [{ parameter: 'missing', comparison: 'equals', value: true }],
        anyConditions: []
      } }]
    }))).toThrow(AiContractDecodeError);
    expect(() => decodeAiBuildResultV1(buildResultFixture({
      parameterMapping: [{ ...value, requiredWhen: {
        allConditions: [{ parameter: 'value', comparison: 'contains', value: 'x' }],
        anyConditions: []
      } }]
    }))).toThrow(AiContractDecodeError);
    expect(() => decodeAiBuildResultV1(buildResultFixture({
      parameterMapping: [{ ...value, operatorType: 'UnknownOperator' }]
    }))).toThrow(AiContractDecodeError);
  });

  it('requires canonical resource identities and rejects free-text paths', () => {
    const resource = resourceRequirementFixture();
    const decoded = decodeAiBuildResultV1(buildResultFixture({ missingResources: [resource] }));
    expect(decoded.missingResources[0]?.canonicalId)
      .toBe('resource:v1|camera_binding|acquireimage#1|camera_binding_id');
    expect(() => decodeAiBuildResultV1(buildResultFixture({
      missingResources: [{ ...resource, canonicalId: 'camera:C:\\line\\camera.json' }]
    }))).toThrow(AiContractDecodeError);
    expect(() => decodeAiBuildResultV1(buildResultFixture({
      missingResources: [{ ...resource, resourceKey: '../camera.json' }]
    }))).toThrow(AiContractDecodeError);
  });

  it('fails closed on malformed revalidation identities and private fields', () => {
    const build = buildResultFixture();
    const response = {
      build,
      snapshot: snapshotFixture({
        buildRunId: build.runId, buildRunStatus: 'completed', buildClientOperationId: build.clientOperationId,
        submittedBuildFingerprint: build.submittedBuildFingerprint, buildResult: build
      }),
      metadataOnly: true
    };
    expect(decodeAiBuildRevalidationResponseV1(response).build.buildId).toBe(build.buildId);
    expect(() => decodeAiBuildRevalidationResponseV1({ ...response, privateCandidate: {} }))
      .toThrow(AiContractDecodeError);
    expect(() => decodeAiBuildRevalidationResponseV1({
      ...response,
      snapshot: { ...response.snapshot, buildRunId: 'different_run' }
    })).toThrow(AiContractDecodeError);
  });
});
