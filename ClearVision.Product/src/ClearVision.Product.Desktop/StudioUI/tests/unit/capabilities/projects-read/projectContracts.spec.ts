import { describe, expect, it } from 'vitest';
import {
  ProjectContractDecodeError,
  decodeProjectDetails,
  decodeProjectSummaryList
} from '@/capabilities/projects-read';

const projectId = '11111111-1111-4111-8111-111111111111';
const flowId = '22222222-2222-4222-8222-222222222222';

function summary(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: projectId,
    name: '瓶盖检测',
    description: null,
    version: '1.2.0',
    persistenceRevision: 7,
    createdAt: '2026-07-15T01:00:00Z',
    modifiedAt: '2026-07-15T02:00:00Z',
    lastOpenedAt: null,
    ...overrides
  };
}

describe('project response decoders', () => {
  it('decodes only stable list summary fields and ignores wide ProjectDto fields', () => {
    const result = decodeProjectSummaryList([
      summary({
        flow: { operators: new Array(40).fill({}), connections: new Array(50).fill({}) },
        globalVariables: { variables: [{ name: 'must-not-become-list-authority' }] },
        assets: 'intentionally malformed list-only extra field',
        futureField: { enabled: true }
      })
    ]);

    expect(result).toEqual([{
      id: projectId,
      name: '瓶盖检测',
      description: null,
      version: '1.2.0',
      persistenceRevision: 7,
      createdAt: '2026-07-15T01:00:00Z',
      modifiedAt: '2026-07-15T02:00:00Z',
      lastOpenedAt: null
    }]);
    expect(result[0]).not.toHaveProperty('flow');
    expect(result[0]).not.toHaveProperty('assets');
  });

  it('decodes canonical detail counts, decision and asset summaries from the detail payload', () => {
    const result = decodeProjectDetails(summary({
      flow: {
        id: flowId,
        name: '主流程',
        operators: [{ id: 'operator-a' }, { id: 'operator-b' }],
        connections: [{ id: 'connection-a' }],
        decisionConfiguration: {
          finalDecisionBinding: { sourceOperatorId: 'operator-b' },
          missingDecisionPolicy: 'Undetermined',
          futureDecisionField: true
        }
      },
      assets: {
        schemaVersion: 1,
        calibrationAssets: [{ assetId: 'calibration-a' }],
        spatialAssets: [{ assetId: 'spatial-a' }, { assetId: 'spatial-b' }]
      },
      futureField: 'accepted'
    }));

    expect(result.flow).toEqual({
      id: flowId,
      name: '主流程',
      operatorCount: 2,
      connectionCount: 1,
      decision: {
        configured: true,
        missingDecisionPolicy: 'Undetermined'
      }
    });
    expect(result.assets).toEqual({
      schemaVersion: 1,
      calibrationAssetCount: 1,
      spatialAssetCount: 2
    });
  });

  it('preserves unknown decision policy strings without inferring protocol meaning', () => {
    const result = decodeProjectDetails(summary({
      flow: {
        id: flowId,
        name: '主流程',
        operators: [],
        connections: [],
        decisionConfiguration: {
          finalDecisionBinding: null,
          missingDecisionPolicy: 'FutureSafePolicy'
        }
      },
      assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
    }));

    expect(result.flow?.decision).toEqual({
      configured: false,
      missingDecisionPolicy: 'FutureSafePolicy'
    });
  });

  it.each([
    ['top-level object for list', {}, '$'],
    ['invalid id', [summary({ id: 'not-a-guid' })], '$[0].id'],
    ['negative revision', [summary({ persistenceRevision: -1 })], '$[0].persistenceRevision'],
    ['invalid date', [summary({ createdAt: 'not-a-date' })], '$[0].createdAt']
  ])('rejects malformed %s payloads', (_label, payload, expectedPath) => {
    expect(() => decodeProjectSummaryList(payload)).toThrow(ProjectContractDecodeError);
    try {
      decodeProjectSummaryList(payload);
    } catch (error) {
      expect(error).toMatchObject({ path: expectedPath });
    }
  });

  it.each([
    ['operators', { operators: 'not-an-array', connections: [], decisionConfiguration: null }],
    ['connections', { operators: [], connections: [1], decisionConfiguration: null }],
    ['decision', { operators: [], connections: [], decisionConfiguration: [] }]
  ])('rejects malformed detail %s fields', (_label, flow) => {
    expect(() => decodeProjectDetails(summary({
      flow: { id: flowId, name: '主流程', ...flow },
      assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
    }))).toThrow(ProjectContractDecodeError);
  });
});
