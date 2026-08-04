import { describe, expect, it } from 'vitest';
import {
  WorkspaceContractDecodeError,
  WorkspaceSaveCompatibilityError,
  buildWorkspaceProjectUpdatePayloadV1,
  decodeWorkspaceProjectV1,
  encodeWorkspaceFlowDraftUpdateV1,
  encodeWorkspaceFlowUpdateV1,
  workspacePersistenceFingerprint,
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

  it('keeps an untouched server port contract clean when Canvas projects numeric enums', () => {
    const decoded = decodeWorkspaceProjectV1(projectFixture());
    const encodedBaseline = encodeWorkspaceFlowUpdateV1(decoded)!;
    const draft = structuredClone(encodedBaseline) as Record<string, unknown>;
    const operator = (draft.operators as Array<Record<string, unknown>>)[0]!;
    const input = (operator.inputPorts as Array<Record<string, unknown>>)[0]!;
    const output = (operator.outputPorts as Array<Record<string, unknown>>)[0]!;
    input.direction = 0;
    input.dataType = 3;
    input.displayName = 'Trigger';
    input.description = '';
    output.direction = 1;
    output.dataType = 0;
    output.displayName = 'Image';
    output.description = '';
    output.isRequired = false;

    const encodedDraft = encodeWorkspaceFlowDraftUpdateV1(decoded, {
      id: decoded.flow!.id,
      name: decoded.flow!.name,
      operators: draft.operators as readonly Readonly<Record<string, unknown>>[],
      connections: draft.connections as readonly Readonly<Record<string, unknown>>[],
      decisionConfiguration: draft.decisionConfiguration ?? null,
      opaquePassthrough: {}
    });

    expect(encodedDraft).toEqual(encodedBaseline);
    expect(workspacePersistenceFingerprint(encodedDraft)).toBe(
      workspacePersistenceFingerprint(encodedBaseline)
    );
  });

  it('keeps server-owned parameter metadata stable when Canvas carries an enriched definition', () => {
    const sourceFlow = flowFixture();
    const sourceParameter = (sourceFlow.operators as Array<Record<string, unknown>>)[0]!
      .parameters as Array<Record<string, unknown>>;
    sourceParameter[0]!.description = null;
    sourceParameter[0]!.options = null;
    const decoded = decodeWorkspaceProjectV1(projectFixture({ flow: sourceFlow }));
    const encodedBaseline = encodeWorkspaceFlowUpdateV1(decoded)!;
    const operators = structuredClone(encodedBaseline.operators) as Array<Record<string, unknown>>;
    const parameter = ((operators[0]!.parameters as Array<Record<string, unknown>>)[0])!;
    parameter.description = 'catalog description';
    parameter.displayName = 'Catalog label';
    parameter.dataType = 'select';
    parameter.defaultValue = 'CatalogDefault';
    parameter.minValue = 1;
    parameter.maxValue = 99;
    parameter.isRequired = false;
    parameter.options = [{ label: 'Catalog', value: 'catalog' }];
    parameter.value = 'Auto';

    const encodedDraft = encodeWorkspaceFlowDraftUpdateV1(decoded, {
      id: decoded.flow!.id,
      name: decoded.flow!.name,
      operators,
      connections: encodedBaseline.connections as readonly Readonly<Record<string, unknown>>[],
      decisionConfiguration: encodedBaseline.decisionConfiguration,
      opaquePassthrough: decoded.flow!.opaquePassthrough
    });

    expect(encodedDraft).toEqual(encodedBaseline);
    expect(workspacePersistenceFingerprint(encodedDraft)).toBe(
      workspacePersistenceFingerprint(encodedBaseline)
    );
  });

  it('merges the typed G2-G4 draft with the G1 baseline without losing null, falsy, ROI, structure or opaque fields', () => {
    const sourceFlow = flowFixture({ futureFlowPolicy: { mode: 'strict' } });
    const sourceOperator = (sourceFlow.operators as Array<Record<string, unknown>>)[0]!;
    sourceOperator.futureOperatorField = { keep: true };
    (sourceOperator.inputPorts as Array<Record<string, unknown>>)[0]!.futurePortField = 'input-opaque';
    const sourceParameter = (sourceOperator.parameters as Array<Record<string, unknown>>)[0]!;
    sourceParameter.futureParameterField = { keep: 'parameter' };
    sourceParameter.value = null;
    sourceOperator.parameters = [
      sourceParameter,
      {
        id: 'abababab-abab-4bab-8bab-abababababab',
        name: 'Zero', displayName: 'Zero', description: null, dataType: 'int',
        value: 0, defaultValue: 1, minValue: 0, maxValue: 10, isRequired: false, options: null
      },
      {
        id: 'bcbcbcbc-bcbc-4cbc-8cbc-bcbcbcbcbcbc',
        name: 'Disabled', displayName: 'Disabled', description: null, dataType: 'bool',
        value: false, defaultValue: true, minValue: null, maxValue: null, isRequired: false, options: null
      },
      {
        id: 'cdcdcdcd-cdcd-4dcd-8dcd-cdcdcdcdcdcd',
        name: 'Label', displayName: 'Label', description: null, dataType: 'string',
        value: '', defaultValue: 'default', minValue: null, maxValue: null, isRequired: false, options: null
      },
      {
        id: 'dededede-dede-4ede-8ede-dededededede',
        name: 'Roi', displayName: 'ROI', description: null, dataType: 'roi',
        value: { x: 10, y: 11, width: 40, height: 30 }, defaultValue: null,
        minValue: null, maxValue: null, isRequired: false, options: null
      }
    ];
    const decoded = decodeWorkspaceProjectV1(projectFixture({ flow: sourceFlow }));
    const encodedBaseline = encodeWorkspaceFlowUpdateV1(decoded)!;
    const operators = structuredClone(encodedBaseline.operators) as Array<Record<string, unknown>>;
    const edited = operators[0]!;
    edited.x = 240;
    edited.y = 160;
    const parameters = edited.parameters as Array<Record<string, unknown>>;
    parameters.find(parameter => parameter.name === 'Roi')!.value = { x: 12, y: 14, width: 52, height: 38 };
    const rectangleNodeId = 'efefefef-efef-4fef-8fef-efefefefefef';
    const rectangleInputId = '12121212-1212-4212-8212-121212121212';
    operators.push({
      id: rectangleNodeId,
      name: 'Caliper RectangleRegion',
      type: 'RectangleRegion',
      metadata: { lifecycle: 'Stable' },
      x: 480,
      y: 160,
      inputPorts: [{
        id: rectangleInputId,
        name: 'Image',
        direction: 'Input',
        dataType: 'Image',
        isRequired: true,
        futurePortField: 'new-port-opaque'
      }],
      outputPorts: [],
      parameters: [{
        id: '13131313-1313-4313-8313-131313131313',
        name: 'Region', displayName: 'Region', description: null, dataType: 'rectangle',
        value: { x: 20, y: 30, width: 100, height: 24, angle: 5 }, defaultValue: null,
        minValue: null, maxValue: null, isRequired: true, options: null
      }],
      isEnabled: true,
      uiCatalogField: { createdBy: 'caliper-structure-editor' }
    });
    const nextConnectionId = '14141414-1414-4414-8414-141414141414';
    const draft = {
      id: decoded.flow!.id,
      name: decoded.flow!.name,
      operators,
      connections: [{
        id: nextConnectionId,
        sourceOperatorId: operatorId,
        sourcePortId: outputPortId,
        targetOperatorId: rectangleNodeId,
        targetPortId: rectangleInputId,
        uiConnectionField: 'connected'
      }],
      decisionConfiguration: encodedBaseline.decisionConfiguration,
      opaquePassthrough: { futureFlowPolicy: { mode: 'strict' } }
    };

    const flow = encodeWorkspaceFlowDraftUpdateV1(decoded, draft);
    const payload = buildWorkspaceProjectUpdatePayloadV1(decoded, draft);

    expect(flow).toMatchObject({
      futureFlowPolicy: { mode: 'strict' },
      operators: [{
        x: 240,
        y: 160,
        futureOperatorField: { keep: true },
        inputPorts: [{ futurePortField: 'input-opaque' }],
        parameters: [
          { name: 'Exposure', value: null, futureParameterField: { keep: 'parameter' } },
          { name: 'Zero', value: 0 },
          { name: 'Disabled', value: false },
          { name: 'Label', value: '' },
          { name: 'Roi', value: { x: 12, y: 14, width: 52, height: 38 } }
        ]
      }, {
        id: rectangleNodeId,
        type: 'RectangleRegion',
        parameters: [{ value: { x: 20, y: 30, width: 100, height: 24, angle: 5 } }]
      }],
      connections: [{ id: nextConnectionId }]
    });
    expect(flow).not.toHaveProperty('operators.1.uiCatalogField');
    expect(flow).not.toHaveProperty('connections.0.uiConnectionField');
    expect(flow).not.toHaveProperty('operators.0.executionStatus');
    expect(flow).not.toHaveProperty('operators.0.executionTimeMs');
    expect(flow).not.toHaveProperty('operators.0.errorMessage');
    expect(payload).toMatchObject({
      globalVariables: expect.objectContaining({
        schemaVersion: '1.0',
        variables: expect.arrayContaining([expect.objectContaining({ name: 'Counter' })]),
        sourceBindings: expect.arrayContaining([expect.objectContaining({ outputPortName: 'Image' })]),
        targetBindings: expect.arrayContaining([expect.objectContaining({ parameterName: 'Exposure' })])
      }),
      expectedPersistenceRevision: 17,
      flow: { id: flowId }
    });
    expect(workspacePersistenceFingerprint(flow)).toBe(
      workspacePersistenceFingerprint(JSON.parse(JSON.stringify(flow)))
    );
  });

  it('normalizes a catalog-added numeric operator type before PUT and decodes the saved response', () => {
    const decoded = decodeWorkspaceProjectV1(projectFixture());
    const encodedBaseline = encodeWorkspaceFlowUpdateV1(decoded)!;
    const operators = structuredClone(encodedBaseline.operators) as Array<Record<string, unknown>>;
    const addedOperatorId = '15151515-1515-4515-8515-151515151515';
    operators.push({
      id: addedOperatorId,
      name: 'Catalog Line Measurement',
      type: '20',
      metadata: null,
      x: 320,
      y: 180,
      inputPorts: [],
      outputPorts: [],
      parameters: [{
        id: '16161616-1616-4616-8616-161616161616',
        name: 'Text',
        displayName: 'Text',
        description: null,
        dataType: 'string',
        value: 'catalog-added',
        defaultValue: '',
        minValue: null,
        maxValue: null,
        isRequired: false,
        options: null
      }],
      isEnabled: true
    });
    const draft = {
      id: decoded.flow!.id,
      name: decoded.flow!.name,
      operators,
      connections: encodedBaseline.connections as readonly Readonly<Record<string, unknown>>[],
      decisionConfiguration: encodedBaseline.decisionConfiguration,
      opaquePassthrough: decoded.flow!.opaquePassthrough
    };

    const payload = buildWorkspaceProjectUpdatePayloadV1(decoded, draft);
    const savedFlow = payload.flow!;
    expect((savedFlow.operators as Array<Record<string, unknown>>)[1]!.type).toBe(20);

    const saved = decodeWorkspaceProjectV1(projectFixture({
      persistenceRevision: 18,
      flow: savedFlow
    }));
    expect(saved.persistenceRevision).toBe(18);
    expect(saved.flow!.operators[1]!.type).toMatchObject({
      value: 'LineMeasurement',
      persistenceValue: 20
    });
    expect((encodeWorkspaceFlowUpdateV1(saved)!.operators as Array<Record<string, unknown>>)[1]!.type)
      .toBe(20);
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
