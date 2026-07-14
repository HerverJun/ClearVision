export type PortDirectionDto = 'Input' | 'Output' | 0 | 1;

export interface OperatorPortDto {
  readonly id: string;
  readonly name: string;
  readonly displayName: string;
  readonly description: string;
  readonly direction: PortDirectionDto;
  readonly dataType: string;
  readonly isRequired: boolean;
}

export interface ParameterOptionDto {
  readonly label: string;
  readonly value: string;
}

export interface OperatorParameterDto {
  readonly id: string;
  readonly name: string;
  readonly displayName: string;
  readonly description: string | null;
  readonly dataType: string;
  readonly value: unknown;
  readonly defaultValue: unknown;
  readonly minValue: unknown;
  readonly maxValue: unknown;
  readonly isRequired: boolean;
  readonly options: readonly ParameterOptionDto[] | null;
}

export interface OperatorDto {
  readonly id: string;
  readonly name: string;
  readonly type: string;
  readonly x: number;
  readonly y: number;
  readonly inputPorts: readonly OperatorPortDto[];
  readonly outputPorts: readonly OperatorPortDto[];
  readonly parameters: readonly OperatorParameterDto[];
  readonly isEnabled: boolean;
}

export interface OperatorConnectionDto {
  readonly id: string;
  readonly sourceOperatorId: string;
  readonly sourcePortId: string;
  readonly targetOperatorId: string;
  readonly targetPortId: string;
}

export interface OperatorFlowDto {
  readonly id: string;
  readonly name: string;
  readonly operators: readonly OperatorDto[];
  readonly connections: readonly OperatorConnectionDto[];
  readonly decisionConfiguration: unknown | null;
}

export type CanvasFixtureId = 'canonical' | 'interaction' | 'benchmark-100' | 'stress-300';

export class CanvasFixtureDecodeError extends Error {
  constructor(path: string, message: string) {
    super(`${path}: ${message}`);
    this.name = 'CanvasFixtureDecodeError';
  }
}

type UnknownRecord = Readonly<Record<string, unknown>>;

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function isRecord(value: unknown): value is UnknownRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function read(record: UnknownRecord, camelName: string, pascalName: string): unknown {
  return Object.prototype.hasOwnProperty.call(record, camelName)
    ? record[camelName]
    : record[pascalName];
}

function requiredRecord(value: unknown, path: string): UnknownRecord {
  if (!isRecord(value)) {
    throw new CanvasFixtureDecodeError(path, 'expected an object');
  }
  return value;
}

function requiredArray(value: unknown, path: string): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw new CanvasFixtureDecodeError(path, 'expected an array');
  }
  return value;
}

function requiredString(value: unknown, path: string): string {
  if (typeof value !== 'string' || value.trim() !== value || value.length === 0) {
    throw new CanvasFixtureDecodeError(path, 'expected a non-empty trimmed string');
  }
  return value;
}

function requiredGuid(value: unknown, path: string): string {
  const id = requiredString(value, path);
  if (!guidPattern.test(id)) {
    throw new CanvasFixtureDecodeError(path, 'expected a canonical GUID string');
  }
  return id.toLowerCase();
}

function finiteNumber(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new CanvasFixtureDecodeError(path, 'expected a finite number');
  }
  return value;
}

function booleanValue(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') {
    throw new CanvasFixtureDecodeError(path, 'expected a boolean');
  }
  return value;
}

function optionalValue(value: unknown): unknown {
  return value === undefined ? null : structuredCloneValue(value);
}

function structuredCloneValue<T>(value: T): T {
  if (typeof structuredClone === 'function') {
    return structuredClone(value);
  }
  return JSON.parse(JSON.stringify(value)) as T;
}

function deepFreeze<T>(value: T): T {
  if (typeof value !== 'object' || value === null || Object.isFrozen(value)) {
    return value;
  }

  for (const key of Reflect.ownKeys(value)) {
    deepFreeze(Reflect.get(value, key));
  }
  return Object.freeze(value);
}

function decodeDirection(value: unknown, path: string): PortDirectionDto {
  if (value === 0 || value === 'Input') {
    return value;
  }
  if (value === 1 || value === 'Output') {
    return value;
  }
  throw new CanvasFixtureDecodeError(path, 'expected Input, Output, 0, or 1');
}

function decodePort(value: unknown, path: string, fallbackDirection: PortDirectionDto): OperatorPortDto {
  const port = requiredRecord(value, path);
  const directionValue = read(port, 'direction', 'Direction');
  return Object.freeze({
    id: requiredGuid(read(port, 'id', 'Id'), `${path}.id`),
    name: requiredString(read(port, 'name', 'Name'), `${path}.name`),
    displayName: requiredString(
      read(port, 'displayName', 'DisplayName') ?? read(port, 'name', 'Name'),
      `${path}.displayName`
    ),
    description: typeof read(port, 'description', 'Description') === 'string'
      ? String(read(port, 'description', 'Description'))
      : '',
    direction: directionValue === undefined
      ? fallbackDirection
      : decodeDirection(directionValue, `${path}.direction`),
    dataType: requiredString(read(port, 'dataType', 'DataType'), `${path}.dataType`),
    isRequired: booleanValue(
      read(port, 'isRequired', 'IsRequired') ?? false,
      `${path}.isRequired`
    )
  });
}

function decodeParameter(value: unknown, path: string): OperatorParameterDto {
  const parameter = requiredRecord(value, path);
  const description = read(parameter, 'description', 'Description');
  const options = read(parameter, 'options', 'Options');
  return Object.freeze({
    id: requiredGuid(read(parameter, 'id', 'Id'), `${path}.id`),
    name: requiredString(read(parameter, 'name', 'Name'), `${path}.name`),
    displayName: requiredString(
      read(parameter, 'displayName', 'DisplayName') ?? read(parameter, 'name', 'Name'),
      `${path}.displayName`
    ),
    description: description === null || description === undefined
      ? null
      : requiredString(description, `${path}.description`),
    dataType: requiredString(read(parameter, 'dataType', 'DataType'), `${path}.dataType`),
    value: optionalValue(read(parameter, 'value', 'Value')),
    defaultValue: optionalValue(read(parameter, 'defaultValue', 'DefaultValue')),
    minValue: optionalValue(read(parameter, 'minValue', 'MinValue')),
    maxValue: optionalValue(read(parameter, 'maxValue', 'MaxValue')),
    isRequired: booleanValue(
      read(parameter, 'isRequired', 'IsRequired') ?? false,
      `${path}.isRequired`
    ),
    options: options === null || options === undefined
      ? null
      : Object.freeze(requiredArray(options, `${path}.options`).map((option, index) => {
          const optionRecord = requiredRecord(option, `${path}.options[${index}]`);
          return Object.freeze({
            label: requiredString(
              read(optionRecord, 'label', 'Label'),
              `${path}.options[${index}].label`
            ),
            value: requiredString(
              read(optionRecord, 'value', 'Value'),
              `${path}.options[${index}].value`
            )
          });
        }))
  });
}

function decodeOperator(value: unknown, path: string): OperatorDto {
  const operator = requiredRecord(value, path);
  const inputPorts = requiredArray(
    read(operator, 'inputPorts', 'InputPorts') ?? [],
    `${path}.inputPorts`
  );
  const outputPorts = requiredArray(
    read(operator, 'outputPorts', 'OutputPorts') ?? [],
    `${path}.outputPorts`
  );
  const parameters = requiredArray(
    read(operator, 'parameters', 'Parameters') ?? [],
    `${path}.parameters`
  );
  return Object.freeze({
    id: requiredGuid(read(operator, 'id', 'Id'), `${path}.id`),
    name: requiredString(read(operator, 'name', 'Name'), `${path}.name`),
    type: requiredString(read(operator, 'type', 'Type'), `${path}.type`),
    x: finiteNumber(read(operator, 'x', 'X') ?? 0, `${path}.x`),
    y: finiteNumber(read(operator, 'y', 'Y') ?? 0, `${path}.y`),
    inputPorts: Object.freeze(inputPorts.map((port, index) =>
      decodePort(port, `${path}.inputPorts[${index}]`, 'Input'))),
    outputPorts: Object.freeze(outputPorts.map((port, index) =>
      decodePort(port, `${path}.outputPorts[${index}]`, 'Output'))),
    parameters: Object.freeze(parameters.map((parameter, index) =>
      decodeParameter(parameter, `${path}.parameters[${index}]`))),
    isEnabled: booleanValue(
      read(operator, 'isEnabled', 'IsEnabled') ?? true,
      `${path}.isEnabled`
    )
  });
}

function decodeConnection(value: unknown, path: string): OperatorConnectionDto {
  const connection = requiredRecord(value, path);
  return Object.freeze({
    id: requiredGuid(read(connection, 'id', 'Id'), `${path}.id`),
    sourceOperatorId: requiredGuid(
      read(connection, 'sourceOperatorId', 'SourceOperatorId'),
      `${path}.sourceOperatorId`
    ),
    sourcePortId: requiredGuid(
      read(connection, 'sourcePortId', 'SourcePortId'),
      `${path}.sourcePortId`
    ),
    targetOperatorId: requiredGuid(
      read(connection, 'targetOperatorId', 'TargetOperatorId'),
      `${path}.targetOperatorId`
    ),
    targetPortId: requiredGuid(
      read(connection, 'targetPortId', 'TargetPortId'),
      `${path}.targetPortId`
    )
  });
}

export function decodeOperatorFlowDto(value: unknown): OperatorFlowDto {
  const flow = requiredRecord(value, 'flow');
  const operators = requiredArray(read(flow, 'operators', 'Operators'), 'flow.operators');
  const connections = requiredArray(read(flow, 'connections', 'Connections'), 'flow.connections');
  return deepFreeze({
    id: requiredGuid(read(flow, 'id', 'Id'), 'flow.id'),
    name: requiredString(read(flow, 'name', 'Name'), 'flow.name'),
    operators: Object.freeze(operators.map((operator, index) =>
      decodeOperator(operator, `flow.operators[${index}]`))),
    connections: Object.freeze(connections.map((connection, index) =>
      decodeConnection(connection, `flow.connections[${index}]`))),
    decisionConfiguration: optionalValue(
      read(flow, 'decisionConfiguration', 'DecisionConfiguration')
    )
  });
}

function stableGuid(kind: number, index: number): string {
  const group = kind.toString(16).padStart(8, '0');
  const tail = index.toString(16).padStart(12, '0');
  return `${group}-0000-4000-8000-${tail}`;
}

function port(
  id: string,
  name: string,
  displayName: string,
  dataType: string,
  direction: 'Input' | 'Output',
  isRequired: boolean,
  description = ''
): OperatorPortDto {
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
  id: string,
  name: string,
  displayName: string,
  dataType: string,
  value: unknown,
  defaultValue: unknown,
  minValue: unknown = null,
  maxValue: unknown = null,
  options: readonly ParameterOptionDto[] | null = null
): OperatorParameterDto {
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

function option(value: string, label = value): ParameterOptionDto {
  return { label, value };
}

export const CANVAS_FIXTURE_IDS = deepFreeze({
  flow: stableGuid(1, 1),
  acquisition: {
    operator: stableGuid(16, 1),
    imageInput: stableGuid(32, 1),
    filePathInput: stableGuid(32, 2),
    imageOutput: stableGuid(33, 1)
  },
  threshold: {
    operator: stableGuid(16, 2),
    imageInput: stableGuid(32, 11),
    imageOutput: stableGuid(33, 11)
  },
  blob: {
    operator: stableGuid(16, 3),
    imageInput: stableGuid(32, 21),
    sourceImageInput: stableGuid(32, 22),
    imageOutput: stableGuid(33, 21),
    blobsOutput: stableGuid(33, 22),
    featuresOutput: stableGuid(33, 23),
    countOutput: stableGuid(33, 24)
  },
  statistics: {
    operator: stableGuid(16, 4),
    valueInput: stableGuid(32, 31),
    meanOutput: stableGuid(33, 31),
    countOutput: stableGuid(33, 32)
  },
  regionErosion: {
    operator: stableGuid(16, 5),
    regionInput: stableGuid(32, 41),
    imageInput: stableGuid(32, 42),
    regionOutput: stableGuid(33, 41),
    imageOutput: stableGuid(33, 42),
    areaOutput: stableGuid(33, 43)
  },
  connections: {
    acquisitionToThreshold: stableGuid(64, 1),
    thresholdToBlob: stableGuid(64, 2),
    blobCountToStatistics: stableGuid(64, 3)
  }
});

const canonicalOperators: readonly OperatorDto[] = [
  {
    id: CANVAS_FIXTURE_IDS.acquisition.operator,
    name: '图像采集',
    type: 'ImageAcquisition',
    x: 60,
    y: 90,
    inputPorts: [
      port(CANVAS_FIXTURE_IDS.acquisition.imageInput, 'Image', 'Runtime supplied image', 'Image', 'Input', false),
      port(CANVAS_FIXTURE_IDS.acquisition.filePathInput, 'FilePath', '文件路径输入', 'String', 'Input', false)
    ],
    outputPorts: [
      port(CANVAS_FIXTURE_IDS.acquisition.imageOutput, 'Image', '图像', 'Image', 'Output', false)
    ],
    parameters: [
      parameter(stableGuid(48, 1), 'SourceType', '采集源', 'enum', 'File', 'File', null, null, [option('File', '文件'), option('Camera', '相机')]),
      parameter(stableGuid(48, 2), 'FilePath', '文件路径', 'file', '', '')
    ],
    isEnabled: true
  },
  {
    id: CANVAS_FIXTURE_IDS.threshold.operator,
    name: '二值化',
    type: 'Thresholding',
    x: 300,
    y: 90,
    inputPorts: [
      port(CANVAS_FIXTURE_IDS.threshold.imageInput, 'Image', 'Image', 'Image', 'Input', true)
    ],
    outputPorts: [
      port(CANVAS_FIXTURE_IDS.threshold.imageOutput, 'Image', 'Image', 'Image', 'Output', false)
    ],
    parameters: [
      parameter(stableGuid(48, 11), 'Threshold', 'Threshold', 'double', 127, 127, 0, 255),
      parameter(stableGuid(48, 12), 'MaxValue', 'Max Value', 'double', 255, 255, 0, 255),
      parameter(stableGuid(48, 13), 'Type', 'Type', 'enum', '0', '0', null, null, [option('0', 'Binary'), option('8', 'Otsu')]),
      parameter(stableGuid(48, 14), 'UseOtsu', 'Use Otsu', 'bool', false, false)
    ],
    isEnabled: true
  },
  {
    id: CANVAS_FIXTURE_IDS.blob.operator,
    name: 'Blob分析',
    type: 'BlobAnalysis',
    x: 540,
    y: 90,
    inputPorts: [
      port(CANVAS_FIXTURE_IDS.blob.imageInput, 'Image', '二值图像', 'Image', 'Input', true),
      port(CANVAS_FIXTURE_IDS.blob.sourceImageInput, 'SourceImage', '参考图像', 'Image', 'Input', false)
    ],
    outputPorts: [
      port(CANVAS_FIXTURE_IDS.blob.imageOutput, 'Image', '标记图像', 'Image', 'Output', false),
      port(CANVAS_FIXTURE_IDS.blob.blobsOutput, 'Blobs', 'Blob结果列表', 'BlobList', 'Output', false),
      port(CANVAS_FIXTURE_IDS.blob.featuresOutput, 'BlobFeatures', 'Blob详细特征', 'BlobFeatureList', 'Output', false),
      port(CANVAS_FIXTURE_IDS.blob.countOutput, 'BlobCount', 'Blob数量', 'Integer', 'Output', false)
    ],
    parameters: [
      parameter(stableGuid(48, 21), 'MinArea', '最小面积', 'int', 100, 100, 0, null),
      parameter(stableGuid(48, 22), 'MaxArea', '最大面积', 'int', 100000, 100000, 0, null),
      parameter(stableGuid(48, 23), 'Color', '目标颜色', 'enum', 'White', 'White', null, null, [option('White', '白色'), option('Black', '黑色')])
    ],
    isEnabled: true
  },
  {
    id: CANVAS_FIXTURE_IDS.statistics.operator,
    name: 'Statistics',
    type: 'Statistics',
    x: 790,
    y: 50,
    inputPorts: [
      port(CANVAS_FIXTURE_IDS.statistics.valueInput, 'Value', 'Input Value', 'Float', 'Input', true)
    ],
    outputPorts: [
      port(CANVAS_FIXTURE_IDS.statistics.meanOutput, 'Mean', 'Mean', 'Float', 'Output', false),
      port(CANVAS_FIXTURE_IDS.statistics.countOutput, 'Count', 'Count', 'Integer', 'Output', false)
    ],
    parameters: [
      parameter(stableGuid(48, 31), 'WindowSize', 'Window Size', 'int', 1000, 1000, 2, 50000),
      parameter(stableGuid(48, 32), 'Reset', 'Reset History', 'bool', false, false)
    ],
    isEnabled: true
  },
  {
    id: CANVAS_FIXTURE_IDS.regionErosion.operator,
    name: 'Region Erosion',
    type: 'RegionErosion',
    x: 790,
    y: 300,
    inputPorts: [
      port(CANVAS_FIXTURE_IDS.regionErosion.regionInput, 'Region', '输入区域', 'Region', 'Input', true),
      port(CANVAS_FIXTURE_IDS.regionErosion.imageInput, 'Image', '参考图像（可选）', 'Image', 'Input', false)
    ],
    outputPorts: [
      port(CANVAS_FIXTURE_IDS.regionErosion.regionOutput, 'Region', '腐蚀后区域', 'Region', 'Output', false),
      port(CANVAS_FIXTURE_IDS.regionErosion.imageOutput, 'Image', '可视化图像', 'Image', 'Output', false),
      port(CANVAS_FIXTURE_IDS.regionErosion.areaOutput, 'Area', 'Eroded Area', 'Integer', 'Output', false)
    ],
    parameters: [
      parameter(stableGuid(48, 41), 'KernelShape', 'Structuring Element Shape', 'enum', 'Rectangle', 'Rectangle', null, null, [option('Rectangle'), option('Ellipse'), option('Cross')]),
      parameter(stableGuid(48, 42), 'KernelWidth', 'Kernel Width', 'int', 3, 3, 1, 99),
      parameter(stableGuid(48, 43), 'KernelHeight', 'Kernel Height', 'int', 3, 3, 1, 99),
      parameter(stableGuid(48, 44), 'Iterations', 'Iterations', 'int', 1, 1, 1, 100)
    ],
    isEnabled: false
  }
];

const canonicalConnections: readonly OperatorConnectionDto[] = [
  {
    id: CANVAS_FIXTURE_IDS.connections.acquisitionToThreshold,
    sourceOperatorId: CANVAS_FIXTURE_IDS.acquisition.operator,
    sourcePortId: CANVAS_FIXTURE_IDS.acquisition.imageOutput,
    targetOperatorId: CANVAS_FIXTURE_IDS.threshold.operator,
    targetPortId: CANVAS_FIXTURE_IDS.threshold.imageInput
  },
  {
    id: CANVAS_FIXTURE_IDS.connections.thresholdToBlob,
    sourceOperatorId: CANVAS_FIXTURE_IDS.threshold.operator,
    sourcePortId: CANVAS_FIXTURE_IDS.threshold.imageOutput,
    targetOperatorId: CANVAS_FIXTURE_IDS.blob.operator,
    targetPortId: CANVAS_FIXTURE_IDS.blob.imageInput
  },
  {
    id: CANVAS_FIXTURE_IDS.connections.blobCountToStatistics,
    sourceOperatorId: CANVAS_FIXTURE_IDS.blob.operator,
    sourcePortId: CANVAS_FIXTURE_IDS.blob.countOutput,
    targetOperatorId: CANVAS_FIXTURE_IDS.statistics.operator,
    targetPortId: CANVAS_FIXTURE_IDS.statistics.valueInput
  }
];

export const CANONICAL_OPERATOR_FLOW_FIXTURE = decodeOperatorFlowDto({
  id: CANVAS_FIXTURE_IDS.flow,
  name: 'F01 Canonical Canvas Contract',
  operators: canonicalOperators,
  connections: canonicalConnections,
  decisionConfiguration: {
    finalDecisionBinding: {
      sourceOperatorId: CANVAS_FIXTURE_IDS.blob.operator,
      sourceOutputPortId: CANVAS_FIXTURE_IDS.blob.countOutput,
      sourceOutputName: 'BlobCount',
      dataType: 'Integer',
      rule: 'NumericComparison',
      trueMeansOk: true,
      comparator: 'Equal',
      threshold: 0
    },
    missingDecisionPolicy: 'Invalid'
  }
});

export const CANVAS_INTERACTION_FLOW_FIXTURE = decodeOperatorFlowDto({
  ...CANONICAL_OPERATOR_FLOW_FIXTURE,
  id: stableGuid(1, 2),
  name: 'F01 Canvas Interaction Matrix',
  connections: [],
  decisionConfiguration: null
});

function createUnitConvertOperator(index: number, columns: number): OperatorDto {
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
      parameter(stableGuid(83, basePortIndex + 1), 'FromUnit', 'From Unit', 'enum', 'Pixel', 'Pixel', null, null, [option('Pixel'), option('mm'), option('um'), option('inch')]),
      parameter(stableGuid(83, basePortIndex + 2), 'ToUnit', 'To Unit', 'enum', 'mm', 'mm', null, null, [option('Pixel'), option('mm'), option('um'), option('inch')]),
      parameter(stableGuid(83, basePortIndex + 3), 'Scale', 'Scale', 'double', 1, 1, 1e-9, 1000000),
      parameter(stableGuid(83, basePortIndex + 4), 'UseCalibration', 'Use Calibration', 'bool', false, false)
    ],
    isEnabled: true
  };
}

export function createDeterministicCanvasBenchmarkFixture(
  nodeCount: number,
  connectionCount: number
): OperatorFlowDto {
  if (!Number.isInteger(nodeCount) || nodeCount < 2) {
    throw new RangeError('Canvas benchmark nodeCount must be an integer greater than one.');
  }
  const maximumConnections = (nodeCount - 1) + Math.max(0, nodeCount - 2);
  if (!Number.isInteger(connectionCount) || connectionCount < nodeCount - 1 || connectionCount > maximumConnections) {
    throw new RangeError(`Canvas benchmark connectionCount must be between ${nodeCount - 1} and ${maximumConnections}.`);
  }

  const columns = Math.max(8, Math.ceil(Math.sqrt(nodeCount * 1.6)));
  const operators = Array.from({ length: nodeCount }, (_, index) =>
    createUnitConvertOperator(index, columns));
  const connections: OperatorConnectionDto[] = [];

  for (let targetIndex = 1; targetIndex < nodeCount; targetIndex += 1) {
    const source = operators[targetIndex - 1];
    const target = operators[targetIndex];
    if (!source || !target) {
      throw new Error('Deterministic benchmark operator generation failed.');
    }
    connections.push({
      id: stableGuid(84, connections.length + 1),
      sourceOperatorId: source.id,
      sourcePortId: source.outputPorts[0]?.id ?? '',
      targetOperatorId: target.id,
      targetPortId: target.inputPorts[0]?.id ?? ''
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
      sourcePortId: source.outputPorts[0]?.id ?? '',
      targetOperatorId: target.id,
      targetPortId: target.inputPorts[1]?.id ?? ''
    });
  }

  return decodeOperatorFlowDto({
    id: stableGuid(85, nodeCount * 1000 + connectionCount),
    name: `F01 Canvas Benchmark ${nodeCount}/${connectionCount}`,
    operators,
    connections,
    decisionConfiguration: null
  });
}

let benchmark100Fixture: OperatorFlowDto | undefined;
let stress300Fixture: OperatorFlowDto | undefined;

export function getCanvasFixture(id: CanvasFixtureId): OperatorFlowDto {
  switch (id) {
    case 'canonical':
      return CANONICAL_OPERATOR_FLOW_FIXTURE;
    case 'interaction':
      return CANVAS_INTERACTION_FLOW_FIXTURE;
    case 'benchmark-100':
      benchmark100Fixture ??= createDeterministicCanvasBenchmarkFixture(100, 150);
      return benchmark100Fixture;
    case 'stress-300':
      stress300Fixture ??= createDeterministicCanvasBenchmarkFixture(300, 450);
      return stress300Fixture;
  }
}

function stableSortValue(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(stableSortValue);
  }
  if (!isRecord(value)) {
    return value;
  }
  return Object.fromEntries(
    Object.keys(value)
      .sort((left, right) => left.localeCompare(right))
      .map(key => [key, stableSortValue(value[key])])
  );
}

function identityPayload(flow: OperatorFlowDto): unknown {
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

function fingerprint(text: string): string {
  let hash = 0x811c9dc5;
  for (let index = 0; index < text.length; index += 1) {
    hash ^= text.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193);
  }
  return (hash >>> 0).toString(16).padStart(8, '0');
}

export function createFlowIdentityFingerprint(flow: OperatorFlowDto): string {
  return fingerprint(JSON.stringify(identityPayload(flow)));
}
