import { describe, expect, it } from 'vitest';
import {
  ProjectLifecycleContractDecodeError,
  decodeProjectCreateAuthorityResult,
  decodeProjectLifecycleOperation,
  decodeProjectOpenAuthorityResult
} from '@/capabilities/project-lifecycle';

const projectId = '11111111-1111-4111-8111-111111111111';
const operationId = '22222222-2222-4222-8222-222222222222';
const flowId = '33333333-3333-4333-8333-333333333333';

function project() {
  return {
    id: projectId,
    name: '空白工程',
    description: null,
    version: '1.0.0',
    persistenceRevision: 0,
    flow: {
      id: flowId,
      name: '空流程',
      operators: [],
      connections: [],
      decisionConfiguration: null
    },
    globalSettings: {},
    globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] },
    createdAt: '2026-07-19T00:00:00Z',
    modifiedAt: null,
    lastOpenedAt: null
  };
}

function operation(overrides: Record<string, unknown> = {}) {
  return {
    clientOperationId: operationId,
    kind: 'create',
    status: 'completed',
    projectId,
    result: {
      project: project(),
      projectDeleted: false,
      deleted: false,
      alreadyDeleted: false,
      cleanupStatus: 'not-required'
    },
    errorCode: null,
    createdAtUtc: '2026-07-19T00:00:00Z',
    updatedAtUtc: '2026-07-19T00:00:01Z',
    expiresAtUtc: '2026-07-26T00:00:01Z',
    ...overrides
  };
}

describe('Project lifecycle response contracts', () => {
  it('decodes the server-bound blank create authority', () => {
    const result = decodeProjectCreateAuthorityResult({
      projectId,
      project: project(),
      operationReplayed: false,
      operation: operation()
    });

    expect(result).toMatchObject({
      projectId,
      operationReplayed: false,
      project: { id: projectId, persistenceRevision: 0 },
      operation: { clientOperationId: operationId, status: 'completed' }
    });
  });

  it('decodes pending/retryable operation states without inventing a terminal result', () => {
    expect(decodeProjectLifecycleOperation(operation({
      status: 'failed-retryable',
      result: null,
      errorCode: 'PROJECT_OPERATION_RETRYABLE'
    }))).toMatchObject({
      status: 'failed-retryable',
      result: null,
      errorCode: 'PROJECT_OPERATION_RETRYABLE'
    });
  });

  it('rejects mismatched Project identity and malformed open timestamps', () => {
    expect(() => decodeProjectCreateAuthorityResult({
      projectId: '44444444-4444-4444-8444-444444444444',
      project: project(),
      operationReplayed: false,
      operation: operation()
    })).toThrow(ProjectLifecycleContractDecodeError);
    expect(() => decodeProjectOpenAuthorityResult({ projectId, lastOpenedAtUtc: 'not-a-date' }))
      .toThrow(ProjectLifecycleContractDecodeError);
  });
});
