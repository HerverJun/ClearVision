import { describe, expect, it } from 'vitest';
import {
  WorkspaceContractDecodeError,
  WorkspaceSaveCompatibilityError,
  decodeWorkspaceProjectV1,
  encodeWorkspaceFlowUpdateV1,
  workspacePersistenceFieldsV1,
  workspaceTransientStripFieldsV1
} from '@/capabilities/project-workspace/workspaceContracts';

const projectId = '11111111-1111-4111-8111-111111111111';
const flowId = '22222222-2222-4222-8222-222222222222';
const operatorId = '33333333-3333-4333-8333-333333333333';
const inputPortId = '44444444-4444-4444-8444-444444444444';
const outputPortId = '55555555-5555-4555-8555-555555555555';
const parameterId = '66666666-6666-4666-8666-666666666666';
const connectionId = '77777777-7777-4777-8777-777777777777';
const variableId = '88888888-8888-4888-8888-888888888888';
const sourceBindingId = '99999999-9999-4999-8999-999999999999';
const targetBindingId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

function flowFixture(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: flowId,
    name: '主流程',
    operators: [{
      id: operatorId,
      name: '采集',
      type: 'ImageAcquisition',
      metadata: {
        lifecycle: 'Stable',
        extension: { preserve: true }
      },
      x: 120.5,
      y: 88.25,
      inputPorts: [{
        id: inputPortId,
        name: 'Trigger',
        direction: 'Input',
        dataType: 'Boolean',
        isRequired: false
      }],
      outputPorts: [{
        id: outputPortId,
        name: 'Image',
        direction: 'Output',
        dataType: 'Image',
        isRequired: true
      }],
      parameters: [{
        id: parameterId,
        name: 'Exposure',
        displayName: '曝光',
        description: null,
        dataType: 'enum',
        value: 'Auto',
        defaultValue: 'Manual',
        minValue: 0,
        maxValue: 1000,
        isRequired: true,
        options: [
          { label: '自动', value: 'Auto' },
          { label: '手动', value: 'Manual' }
        ]
      }],
      isEnabled: true,
      executionStatus: 'Success',
      executionTimeMs: 12,
      errorMessage: null
    }],
    connections: [{
      id: connectionId,
      sourceOperatorId: operatorId,
      sourcePortId: outputPortId,
      targetOperatorId: operatorId,
      targetPortId: inputPortId
    }],
    decisionConfiguration: {
      finalDecisionBinding: {
        sourceOperatorId: operatorId,
        sourceOutputPortId: outputPortId,
        sourceOutputName: 'Image',
        dataType: 'Boolean',
        rule: 'Boolean',
        trueMeansOk: true,
        okValue: '1',
        ngValue: '0',
        comparator: null,
        threshold: null
      },
      missingDecisionPolicy: 'Undetermined'
    },
    ...overrides
  };
}

function projectFixture(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: projectId,
    name: '瓶盖检测',
    description: '完整 persistence envelope',
    version: '1.2.0',
    persistenceRevision: 17,
    flow: flowFixture(),
    globalSettings: { locale: 'zh-CN' },
    globalVariables: {
      schemaVersion: '1.0',
      variables: [{
        id: variableId,
        name: 'Counter',
        displayName: '计数',
        description: null,
        valueType: 'Int64',
        initialValue: '9007199254740993',
        min: '0',
        max: '9999999999999999',
        manualWriteAllowed: true,
        includeInResultMetadata: true,
        order: 0
      }],
      sourceBindings: [{
        id: sourceBindingId,
        variableId,
        operatorId,
        outputPortId,
        operatorName: '采集',
        outputPortName: 'Image',
        resultPathVersion: 1,
        resultPath: '$.value',
        conversionMode: 'Exact',
        expression: null
      }],
      targetBindings: [{
        id: targetBindingId,
        variableId,
        operatorId,
        parameterId,
        operatorName: '采集',
        parameterName: 'Exposure',
        conversionMode: 'Round',
        expression: 'value + 1'
      }]
    },
    assets: {
      schemaVersion: 1,
      calibrationAssets: [{
        assetId: 'calibration-main',
        kind: 'CalibrationBundleV2',
        version: '2',
        producer: 'CalibrationWorkbench',
        sourceDraftSessionId: 'draft-1',
        targetNodeId: operatorId,
        imageIdentity: 'image:1',
        contentHash: 'sha256:abc',
        projectRevision: 17,
        createdAtUtc: '2026-07-15T01:00:00Z',
        updatedAtUtc: '2026-07-15T02:00:00Z',
        status: 'authority',
        payload: { matrix: [1, 0, 0, 1] }
      }],
      spatialAssets: [{
        assetId: 'spatial-main',
        kind: 'SpatialContextV1',
        version: '1',
        producer: 'SpatialEditor',
        sourceDraftSessionId: 'draft-2',
        contentHash: 'sha256:def',
        projectRevision: 17,
        createdAtUtc: '2026-07-15T01:00:00Z',
        updatedAtUtc: '2026-07-15T02:00:00Z',
        status: 'authority',
        payload: { coordinateSystem: 'world' }
      }]
    },
    createdAt: '2026-07-15T01:00:00Z',
    modifiedAt: '2026-07-15T02:00:00Z',
    lastOpenedAt: null,
    ...overrides
  };
}

function persistenceFlowWithoutTransient(): Record<string, unknown> {
  const source = structuredClone(flowFixture());
  const operator = (source.operators as Array<Record<string, unknown>>)[0]!;
  delete operator.executionStatus;
  delete operator.executionTimeMs;
  delete operator.errorMessage;
  return source;
}

describe('F03 G1 Workspace persistence contracts', () => {
  it('decodes the complete Project/Flow persistence envelope without summary reconstruction', () => {
    const project = decodeWorkspaceProjectV1(projectFixture());

    expect(project).toMatchObject({
      id: projectId,
      persistenceRevision: 17,
      flow: {
        id: flowId,
        name: '主流程',
        operators: [{
          id: operatorId,
          type: { value: 'ImageAcquisition', persistenceValue: 'ImageAcquisition' },
          metadata: { lifecycle: 'Stable', extension: { preserve: true } },
          inputPorts: [{ id: inputPortId }],
          outputPorts: [{ id: outputPortId }],
          parameters: [{ id: parameterId, value: 'Auto' }],
          isEnabled: true
        }],
        connections: [{ id: connectionId }]
      },
      globalVariables: {
        variables: [{ id: variableId, initialValue: '9007199254740993' }],
        sourceBindings: [{ id: sourceBindingId }],
        targetBindings: [{ id: targetBindingId }]
      },
      assets: {
        calibrationAssets: [{ assetId: 'calibration-main' }],
        spatialAssets: [{ assetId: 'spatial-main' }]
      },
      saveCompatibility: { status: 'compatible', canEncode: true }
    });
  });

  it('performs the no-edit decoder/encoder golden round-trip and strips only approved transient fields', () => {
    const decoded = decodeWorkspaceProjectV1(projectFixture());

    expect(encodeWorkspaceFlowUpdateV1(decoded)).toEqual(persistenceFlowWithoutTransient());
    expect(encodeWorkspaceFlowUpdateV1(decoded)).not.toHaveProperty('operators.0.executionStatus');
    expect(workspaceTransientStripFieldsV1.operator).toEqual([
      'executionStatus', 'executionTimeMs', 'errorMessage'
    ]);
    expect(workspacePersistenceFieldsV1.flow).toEqual([
      'id', 'name', 'decisionConfiguration', 'operators', 'connections'
    ]);
  });

  it('preserves safe unknown persistence keys through opaque passthrough at every write-capable layer', () => {
    const sourceFlow = flowFixture({ futureFlowPolicy: { mode: 'strict' } });
    const operator = (sourceFlow.operators as Array<Record<string, unknown>>)[0]!;
    operator.futureOperatorField = { version: 2 };
    (operator.inputPorts as Array<Record<string, unknown>>)[0]!.futurePortField = 'preserve';
    (operator.parameters as Array<Record<string, unknown>>)[0]!.futureParameterField = [1, 2, 3];
    ((operator.parameters as Array<Record<string, unknown>>)[0]!.options as Array<Record<string, unknown>>)[0]!
      .futureOptionField = true;
    (sourceFlow.connections as Array<Record<string, unknown>>)[0]!.futureConnectionField = 'opaque';
    const decision = sourceFlow.decisionConfiguration as Record<string, unknown>;
    decision.futureDecisionField = 1;
    (decision.finalDecisionBinding as Record<string, unknown>).futureBindingField = { keep: true };

    const decoded = decodeWorkspaceProjectV1(projectFixture({ flow: sourceFlow }));
    const encoded = encodeWorkspaceFlowUpdateV1(decoded);

    expect(decoded.saveCompatibility.status).toBe('opaque-passthrough');
    expect(decoded.saveCompatibility.blockedPaths).toEqual([]);
    expect(decoded.saveCompatibility.opaquePassthroughPaths).toEqual(expect.arrayContaining([
      '$.flow.futureFlowPolicy',
      '$.flow.operators[0].futureOperatorField',
      '$.flow.operators[0].inputPorts[0].futurePortField',
      '$.flow.operators[0].parameters[0].futureParameterField',
      '$.flow.connections[0].futureConnectionField',
      '$.flow.decisionConfiguration.futureDecisionField'
    ]));
    expect(encoded).toMatchObject({
      futureFlowPolicy: { mode: 'strict' },
      operators: [{
          futureOperatorField: { version: 2 },
          inputPorts: [{ futurePortField: 'preserve' }],
          parameters: [{
            futureParameterField: [1, 2, 3],
            options: [
              { futureOptionField: true },
              { label: '手动', value: 'Manual' }
            ]
          }]
      }],
      connections: [{ futureConnectionField: 'opaque' }],
      decisionConfiguration: {
        futureDecisionField: 1,
        finalDecisionBinding: { futureBindingField: { keep: true } }
      }
    });
  });

  it('blocks the formal encoder when an unknown project persistence key cannot be safely emitted', () => {
    const decoded = decodeWorkspaceProjectV1(projectFixture({
      futureProjectPersistence: { requiredOnWrite: true }
    }));

    expect(decoded.saveCompatibility).toMatchObject({
      status: 'blocked',
      canEncode: false,
      blockedPaths: ['$.futureProjectPersistence']
    });
    expect(() => encodeWorkspaceFlowUpdateV1(decoded)).toThrow(WorkspaceSaveCompatibilityError);
  });

  it('blocks case-insensitive field collisions instead of silently overriding them', () => {
    const sourceFlow = flowFixture({ Name: 'ambiguous-name' });
    const decoded = decodeWorkspaceProjectV1(projectFixture({ flow: sourceFlow }));

    expect(decoded.saveCompatibility.status).toBe('blocked');
    expect(decoded.saveCompatibility.blockedPaths).toContain('$.flow.Name');
    expect(() => encodeWorkspaceFlowUpdateV1(decoded)).toThrow(WorkspaceSaveCompatibilityError);
  });

  it('records unknown read-only GlobalVariables/assets fields without making them a second authority', () => {
    const source = projectFixture();
    (source.globalVariables as Record<string, unknown>).futureSchemaHint = 'read-only';
    (source.assets as Record<string, unknown>).futureAssetsHash = 'sha256:future';
    const decoded = decodeWorkspaceProjectV1(source);

    expect(decoded.saveCompatibility.status).toBe('compatible');
    expect(decoded.saveCompatibility.readOnlyUnknownPaths).toEqual([
      '$.assets.futureAssetsHash',
      '$.globalVariables.futureSchemaHint'
    ]);
  });

  it('accepts current numeric enum wire values and preserves their exact persistence representation', () => {
    const sourceFlow = flowFixture();
    const operator = (sourceFlow.operators as Array<Record<string, unknown>>)[0]!;
    operator.type = 0;
    operator.executionStatus = 2;
    (operator.inputPorts as Array<Record<string, unknown>>)[0]!.direction = 0;
    (operator.outputPorts as Array<Record<string, unknown>>)[0]!.dataType = 0;
    const decoded = decodeWorkspaceProjectV1(projectFixture({ flow: sourceFlow }));
    const encoded = encodeWorkspaceFlowUpdateV1(decoded)!;

    expect(decoded.flow?.operators[0]?.type).toEqual({
      value: 'ImageAcquisition', persistenceValue: 0
    });
    expect(encoded).toMatchObject({
      operators: [{
        type: 0,
        inputPorts: [{ direction: 0 }],
        outputPorts: [{ dataType: 0 }]
      }]
    });
  });

  it('does not invent a Flow from summary counts when the formal flow is null', () => {
    const decoded = decodeWorkspaceProjectV1(projectFixture({
      flow: null,
      operatorCount: 40,
      connectionCount: 50
    }));

    expect(decoded.flow).toBeNull();
    expect(decoded.saveCompatibility.status).toBe('blocked');
    expect(decoded.saveCompatibility.blockedPaths).toEqual([
      '$.connectionCount', '$.operatorCount'
    ]);
  });

  it.each([
    ['missing flow', (() => { const value = projectFixture(); delete value.flow; return value; })(), '$.flow'],
    ['invalid revision', projectFixture({ persistenceRevision: 1.5 }), '$.persistenceRevision'],
    ['invalid operator id', projectFixture({ flow: flowFixture({
      operators: [{
        ...(flowFixture().operators as Array<Record<string, unknown>>)[0],
        id: 'not-a-guid'
      }]
    }) }), '$.flow.operators[0].id'],
    ['invalid finite coordinate', projectFixture({ flow: flowFixture({
      operators: [{
        ...(flowFixture().operators as Array<Record<string, unknown>>)[0],
        x: Number.POSITIVE_INFINITY
      }]
    }) }), '$.flow.operators[0].x'],
    ['unknown enum', projectFixture({ flow: flowFixture({
      operators: [{
        ...(flowFixture().operators as Array<Record<string, unknown>>)[0],
        type: 'FutureUnregisteredOperator'
      }]
    }) }), '$.flow.operators[0].type']
  ])('rejects malformed required persistence data: %s', (_label, payload, expectedPath) => {
    expect(() => decodeWorkspaceProjectV1(payload)).toThrow(WorkspaceContractDecodeError);
    try {
      decodeWorkspaceProjectV1(payload);
    } catch (error) {
      expect(error).toMatchObject({ path: expectedPath });
    }
  });
});
