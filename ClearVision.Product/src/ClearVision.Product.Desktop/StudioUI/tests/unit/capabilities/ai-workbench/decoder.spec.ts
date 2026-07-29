import { describe, expect, it } from 'vitest';
import { AiContractDecodeError } from '@/capabilities/ai-workbench/contracts';
import {
  decodeAiOperationProjectionV1,
  decodeAiSessionDetailV1
} from '@/capabilities/ai-workbench/decoder';

const timestamp = '2026-07-29T08:00:00.000Z';
const operationId = '11111111-1111-4111-8111-111111111111';
const projectId = '22222222-2222-4222-8222-222222222222';

function sessionFixture() {
  return {
    sessionId: 'session_01',
    snapshot: {
      schemaVersion: 1,
      revision: 3,
      projectId,
      lifecycleState: 'idle',
      planRunId: null,
      planRunStatus: null,
      buildRunId: null,
      buildRunStatus: null,
      buildClientOperationId: null,
      projectBaseline: null,
      updatedAtUtc: timestamp
    },
    updatedAtUtc: timestamp
  };
}

describe('AI workbench public decoders', () => {
  it('accepts only the narrow public session projection', () => {
    expect(decodeAiSessionDetailV1(sessionFixture()).snapshot.revision).toBe(3);
    expect(() => decodeAiSessionDetailV1({
      ...sessionFixture(),
      reasoning: 'private chain-of-thought'
    })).toThrow(AiContractDecodeError);
    expect(() => decodeAiSessionDetailV1({
      ...sessionFixture(),
      snapshot: { ...sessionFixture().snapshot, authorization: 'Bearer secret' }
    })).toThrow(AiContractDecodeError);
  });

  it('fails closed on malformed operation and baseline projections', () => {
    const operation = {
      clientOperationId: operationId,
      kind: 'build_run',
      status: 'created',
      sessionId: 'session_01',
      runId: 'ar_01',
      payloadFingerprint: `sha256:${'a'.repeat(64)}`,
      projectBaseline: {
        targetKind: 'existing',
        projectId,
        persistenceRevision: 4,
        canonicalFlowHash: 'b'.repeat(64)
      },
      errorCode: null,
      publicMessage: null,
      createdAtUtc: timestamp,
      updatedAtUtc: timestamp,
      expiresAtUtc: timestamp
    };
    expect(decodeAiOperationProjectionV1(operation).projectBaseline?.persistenceRevision).toBe(4);
    expect(() => decodeAiOperationProjectionV1({
      ...operation,
      projectBaseline: { ...operation.projectBaseline, canonicalFlowHash: 'draft-flow' }
    })).toThrow(AiContractDecodeError);
    expect(() => decodeAiOperationProjectionV1({ ...operation, rawPayload: 'secret' }))
      .toThrow(AiContractDecodeError);
  });
});
