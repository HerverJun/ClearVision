import { describe, expect, it } from 'vitest';
import {
  CANONICAL_OPERATOR_FLOW_FIXTURE,
  CANVAS_FIXTURE_IDS,
  CanvasFixtureDecodeError,
  createDeterministicCanvasBenchmarkFixture,
  createFlowIdentityFingerprint,
  decodeOperatorFlowDto,
  getCanvasFixture,
  type OperatorFlowDto
} from '@/labs/canvas/operatorFlowFixtures';

const guid = (tail: number): string =>
  `00000001-0000-4000-8000-${tail.toString(16).padStart(12, '0')}`;

describe('decodeOperatorFlowDto', () => {
  it('decodes the backend PascalCase DTO shape and freezes the projection', () => {
    const decoded = decodeOperatorFlowDto({
      Id: guid(1),
      Name: 'Pascal flow',
      Operators: [
        {
          Id: guid(2),
          Name: 'Source',
          Type: 'UnitConvert',
          X: 12,
          Y: 34,
          InputPorts: [
            {
              Id: guid(3),
              Name: 'Value',
              DisplayName: 'Value',
              Direction: 0,
              DataType: 'Float',
              IsRequired: true
            }
          ],
          OutputPorts: [
            {
              Id: guid(4),
              Name: 'Result',
              DisplayName: 'Result',
              Direction: 1,
              DataType: 'Float',
              IsRequired: false
            }
          ],
          Parameters: [
            {
              Id: guid(5),
              Name: 'Scale',
              DisplayName: 'Scale',
              Description: null,
              DataType: 'double',
              Value: 1,
              DefaultValue: 1,
              MinValue: 1e-9,
              MaxValue: 1_000_000,
              IsRequired: true,
              Options: null
            }
          ],
          IsEnabled: true
        }
      ],
      Connections: [],
      DecisionConfiguration: { mode: 'Pascal' }
    });

    expect(decoded.id).toBe(guid(1));
    expect(decoded.operators[0]).toMatchObject({
      id: guid(2),
      type: 'UnitConvert',
      x: 12,
      y: 34,
      isEnabled: true
    });
    expect(decoded.operators[0]?.inputPorts[0]).toMatchObject({
      id: guid(3),
      direction: 0,
      dataType: 'Float'
    });
    expect(decoded.decisionConfiguration).toEqual({ mode: 'Pascal' });
    expect(Object.isFrozen(decoded)).toBe(true);
    expect(Object.isFrozen(decoded.operators[0])).toBe(true);
    expect(Object.isFrozen(decoded.operators[0]?.parameters[0])).toBe(true);
  });

  it('rejects invalid canonical identities with a precise DTO path', () => {
    expect(() => decodeOperatorFlowDto({
      id: 'not-a-guid',
      name: 'Invalid',
      operators: [],
      connections: []
    })).toThrowError(new CanvasFixtureDecodeError('flow.id', 'expected a canonical GUID string'));
  });
});

describe('Canvas fixtures', () => {
  it('uses real operator metadata for compatibility and decision checks', () => {
    const byType = new Map(
      CANONICAL_OPERATOR_FLOW_FIXTURE.operators.map(operator => [operator.type, operator])
    );
    const acquisition = byType.get('ImageAcquisition');
    const threshold = byType.get('Thresholding');
    const blob = byType.get('BlobAnalysis');
    const statistics = byType.get('Statistics');
    const regionErosion = byType.get('RegionErosion');

    expect(acquisition?.outputPorts[0]).toMatchObject({ name: 'Image', dataType: 'Image' });
    expect(threshold?.inputPorts[0]).toMatchObject({ name: 'Image', dataType: 'Image' });
    expect(blob?.outputPorts.find(port => port.name === 'BlobCount')).toMatchObject({
      id: CANVAS_FIXTURE_IDS.blob.countOutput,
      dataType: 'Integer'
    });
    expect(statistics?.inputPorts[0]).toMatchObject({
      id: CANVAS_FIXTURE_IDS.statistics.valueInput,
      name: 'Value',
      dataType: 'Float'
    });
    expect(regionErosion?.inputPorts[0]).toMatchObject({
      name: 'Region',
      dataType: 'Region'
    });
    expect(regionErosion?.isEnabled).toBe(false);
    expect(CANONICAL_OPERATOR_FLOW_FIXTURE.connections).toContainEqual({
      id: CANVAS_FIXTURE_IDS.connections.blobCountToStatistics,
      sourceOperatorId: CANVAS_FIXTURE_IDS.blob.operator,
      sourcePortId: CANVAS_FIXTURE_IDS.blob.countOutput,
      targetOperatorId: CANVAS_FIXTURE_IDS.statistics.operator,
      targetPortId: CANVAS_FIXTURE_IDS.statistics.valueInput
    });
    expect(CANONICAL_OPERATOR_FLOW_FIXTURE.decisionConfiguration).toMatchObject({
      finalDecisionBinding: {
        sourceOperatorId: CANVAS_FIXTURE_IDS.blob.operator,
        sourceOutputPortId: CANVAS_FIXTURE_IDS.blob.countOutput,
        dataType: 'Integer',
        rule: 'NumericComparison'
      }
    });
  });

  it.each([
    ['benchmark-100', 100, 150],
    ['stress-300', 300, 450]
  ] as const)('builds deterministic %s DAG fixtures', (fixtureId, nodeCount, connectionCount) => {
    const first = getCanvasFixture(fixtureId);
    const second = getCanvasFixture(fixtureId);
    const nodeIndex = new Map(first.operators.map((operator, index) => [operator.id, index]));
    const occupiedInputs = new Set<string>();

    expect(first).toBe(second);
    expect(first.operators).toHaveLength(nodeCount);
    expect(first.connections).toHaveLength(connectionCount);
    expect(new Set(first.operators.map(operator => operator.id)).size).toBe(nodeCount);
    expect(new Set(first.connections.map(connection => connection.id)).size).toBe(connectionCount);

    for (const connection of first.connections) {
      const sourceIndex = nodeIndex.get(connection.sourceOperatorId);
      const targetIndex = nodeIndex.get(connection.targetOperatorId);
      expect(sourceIndex).toBeTypeOf('number');
      expect(targetIndex).toBeTypeOf('number');
      expect(sourceIndex).toBeLessThan(targetIndex ?? -1);
      expect(occupiedInputs.has(connection.targetPortId)).toBe(false);
      occupiedInputs.add(connection.targetPortId);
    }
  });

  it('bounds the generic deterministic generator to its real two-input DAG capacity', () => {
    expect(createDeterministicCanvasBenchmarkFixture(4, 5).connections).toHaveLength(5);
    expect(() => createDeterministicCanvasBenchmarkFixture(4, 6)).toThrow(RangeError);
  });
});

describe('createFlowIdentityFingerprint', () => {
  it('is stable across clones and changes for identity-bearing edits', () => {
    type MutableFlow = {
      operators: Array<{ x: number; parameters: Array<{ value: unknown }> }>;
      connections: Array<{ targetPortId: string }>;
    };
    const original = CANONICAL_OPERATOR_FLOW_FIXTURE;
    const clone = structuredClone(original) as OperatorFlowDto;
    const moved = structuredClone(original) as unknown as MutableFlow;
    const parameterChanged = structuredClone(original) as unknown as MutableFlow;
    const endpointChanged = structuredClone(original) as unknown as MutableFlow;

    moved.operators[0]!.x += 10;
    parameterChanged.operators[0]!.parameters[0]!.value = 'Camera';
    endpointChanged.connections[0]!.targetPortId = guid(999);

    const fingerprint = createFlowIdentityFingerprint(original);
    expect(createFlowIdentityFingerprint(clone)).toBe(fingerprint);
    expect(createFlowIdentityFingerprint(moved as unknown as OperatorFlowDto)).not.toBe(fingerprint);
    expect(createFlowIdentityFingerprint(parameterChanged as unknown as OperatorFlowDto)).not.toBe(fingerprint);
    expect(createFlowIdentityFingerprint(endpointChanged as unknown as OperatorFlowDto)).not.toBe(fingerprint);
  });
});
