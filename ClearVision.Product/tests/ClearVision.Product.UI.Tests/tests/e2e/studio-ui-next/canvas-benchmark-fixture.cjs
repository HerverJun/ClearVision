'use strict';

function assertInteger(value, message) {
  if (!Number.isInteger(value)) throw new RangeError(message);
}

function stableGuid(kind, index) {
  assertInteger(kind, 'Stable GUID kind must be an integer.');
  assertInteger(index, 'Stable GUID index must be an integer.');
  if (kind < 0 || kind > 0xffffffff) {
    throw new RangeError('Stable GUID kind must fit in an unsigned 32-bit integer.');
  }
  if (index < 0 || index > 0xffffffffffff) {
    throw new RangeError('Stable GUID index must fit in an unsigned 48-bit integer.');
  }

  const group = kind.toString(16).padStart(8, '0');
  const tail = index.toString(16).padStart(12, '0');
  return `${group}-0000-4000-8000-${tail}`;
}

function port(id, name, displayName, dataType, direction, isRequired, description = '') {
  return {
    id,
    name,
    displayName,
    description,
    direction,
    dataType,
    isRequired
  };
}

function parameter(
  id,
  name,
  displayName,
  dataType,
  value,
  defaultValue,
  minValue = null,
  maxValue = null,
  options = null
) {
  return {
    id,
    name,
    displayName,
    description: null,
    dataType,
    value,
    defaultValue,
    minValue,
    maxValue,
    isRequired: true,
    options
  };
}

function option(value, label = value) {
  return { label, value };
}

function createUnitConvertOperator(index, columns) {
  const operatorId = stableGuid(80, index + 1);
  const basePortIndex = index * 10;
  const row = Math.floor(index / columns);
  const column = index % columns;
  return {
    id: operatorId,
    name: `Unit Convert ${String(index + 1).padStart(3, '0')}`,
    type: 'UnitConvert',
    x: 36 + column * 190,
    y: 36 + row * 120,
    inputPorts: [
      port(stableGuid(81, basePortIndex + 1), 'Value', 'Value', 'Float', 'Input', true),
      port(stableGuid(81, basePortIndex + 2), 'PixelSize', 'Pixel Size', 'Float', 'Input', false)
    ],
    outputPorts: [
      port(stableGuid(82, basePortIndex + 1), 'Result', 'Result', 'Float', 'Output', false),
      port(stableGuid(82, basePortIndex + 2), 'Unit', 'Unit', 'String', 'Output', false)
    ],
    parameters: [
      parameter(
        stableGuid(83, basePortIndex + 1),
        'FromUnit',
        'From Unit',
        'enum',
        'Pixel',
        'Pixel',
        null,
        null,
        [option('Pixel'), option('mm'), option('um'), option('inch')]
      ),
      parameter(
        stableGuid(83, basePortIndex + 2),
        'ToUnit',
        'To Unit',
        'enum',
        'mm',
        'mm',
        null,
        null,
        [option('Pixel'), option('mm'), option('um'), option('inch')]
      ),
      parameter(
        stableGuid(83, basePortIndex + 3),
        'Scale',
        'Scale',
        'double',
        1,
        1,
        1e-9,
        1000000
      ),
      parameter(
        stableGuid(83, basePortIndex + 4),
        'UseCalibration',
        'Use Calibration',
        'bool',
        false,
        false
      )
    ],
    isEnabled: true
  };
}

function createDeterministicCanvasBenchmarkFixture(nodeCount, connectionCount) {
  if (!Number.isInteger(nodeCount) || nodeCount < 2) {
    throw new RangeError('Canvas benchmark nodeCount must be an integer greater than one.');
  }

  const maximumConnections = (nodeCount - 1) + Math.max(0, nodeCount - 2);
  if (
    !Number.isInteger(connectionCount) ||
    connectionCount < nodeCount - 1 ||
    connectionCount > maximumConnections
  ) {
    throw new RangeError(
      `Canvas benchmark connectionCount must be between ${nodeCount - 1} and ${maximumConnections}.`
    );
  }

  const columns = Math.max(8, Math.ceil(Math.sqrt(nodeCount * 1.6)));
  const operators = Array.from({ length: nodeCount }, (_, index) =>
    createUnitConvertOperator(index, columns));
  const connections = [];

  for (let targetIndex = 1; targetIndex < nodeCount; targetIndex += 1) {
    const source = operators[targetIndex - 1];
    const target = operators[targetIndex];
    if (!source || !target) {
      throw new Error('Deterministic benchmark operator generation failed.');
    }
    connections.push({
      id: stableGuid(84, connections.length + 1),
      sourceOperatorId: source.id,
      sourcePortId: source.outputPorts[0]?.id || '',
      targetOperatorId: target.id,
      targetPortId: target.inputPorts[0]?.id || ''
    });
  }

  const secondaryConnections = connectionCount - connections.length;
  for (let offset = 0; offset < secondaryConnections; offset += 1) {
    const targetIndex = offset + 2;
    const sourceIndex = Math.max(0, targetIndex - 2);
    const source = operators[sourceIndex];
    const target = operators[targetIndex];
    if (!source || !target) {
      throw new Error('Deterministic secondary connection generation failed.');
    }
    connections.push({
      id: stableGuid(84, connections.length + 1),
      sourceOperatorId: source.id,
      sourcePortId: source.outputPorts[0]?.id || '',
      targetOperatorId: target.id,
      targetPortId: target.inputPorts[1]?.id || ''
    });
  }

  return {
    id: stableGuid(85, nodeCount * 1000 + connectionCount),
    name: `F01 Canvas Benchmark ${nodeCount}/${connectionCount}`,
    operators,
    connections,
    decisionConfiguration: null
  };
}

function isRecord(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function stableSortValue(value) {
  if (Array.isArray(value)) return value.map(stableSortValue);
  if (!isRecord(value)) return value;
  return Object.fromEntries(
    Object.keys(value)
      .sort((left, right) => left.localeCompare(right))
      .map(key => [key, stableSortValue(value[key])])
  );
}

function identityPayload(flow) {
  return {
    id: flow.id,
    name: flow.name,
    operators: [...flow.operators]
      .sort((left, right) => left.id.localeCompare(right.id))
      .map(operator => ({
        id: operator.id,
        type: operator.type,
        x: operator.x,
        y: operator.y,
        isEnabled: operator.isEnabled,
        inputPorts: operator.inputPorts.map(item => item.id),
        outputPorts: operator.outputPorts.map(item => item.id),
        parameters: operator.parameters
          .map(item => ({ id: item.id, name: item.name, value: stableSortValue(item.value) }))
          .sort((left, right) => left.id.localeCompare(right.id))
      })),
    connections: [...flow.connections]
      .sort((left, right) => left.id.localeCompare(right.id))
      .map(connection => ({
        id: connection.id,
        sourceOperatorId: connection.sourceOperatorId,
        sourcePortId: connection.sourcePortId,
        targetOperatorId: connection.targetOperatorId,
        targetPortId: connection.targetPortId
      })),
    decisionConfiguration: stableSortValue(flow.decisionConfiguration)
  };
}

function fnv1a32(text) {
  let hash = 0x811c9dc5;
  for (let index = 0; index < text.length; index += 1) {
    hash ^= text.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193);
  }
  return (hash >>> 0).toString(16).padStart(8, '0');
}

function createFlowIdentityFingerprint(flow) {
  return fnv1a32(JSON.stringify(identityPayload(flow)));
}

function createCanvasFixtureDescriptor(nodeCount, connectionCount) {
  const flow = createDeterministicCanvasBenchmarkFixture(nodeCount, connectionCount);
  return Object.freeze({
    nodeCount,
    connectionCount,
    flowId: flow.id,
    flowName: flow.name,
    fingerprint: createFlowIdentityFingerprint(flow),
    flow
  });
}

module.exports = {
  createCanvasFixtureDescriptor,
  createDeterministicCanvasBenchmarkFixture,
  createFlowIdentityFingerprint,
  fnv1a32,
  identityPayload,
  stableGuid,
  stableSortValue
};
