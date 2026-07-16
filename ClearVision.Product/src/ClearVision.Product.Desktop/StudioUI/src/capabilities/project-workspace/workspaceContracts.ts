export type WorkspaceJsonPrimitive = string | number | boolean | null;
declare const workspaceJsonArrayBrand: unique symbol;
export interface WorkspaceJsonArray extends ReadonlyArray<WorkspaceJsonValue> {
  readonly [workspaceJsonArrayBrand]?: never;
}
export interface WorkspaceJsonObject {
  readonly [key: string]: WorkspaceJsonValue;
}
export type WorkspaceJsonValue =
  | WorkspaceJsonPrimitive
  | WorkspaceJsonArray
  | WorkspaceJsonObject;

export class WorkspaceContractDecodeError extends Error {
  readonly path: string;

  constructor(path: string, expectation: string) {
    super(`Workspace project field ${path} must be ${expectation}.`);
    this.name = 'WorkspaceContractDecodeError';
    this.path = path;
  }
}

export class WorkspaceSaveCompatibilityError extends Error {
  readonly blockedPaths: readonly string[];

  constructor(blockedPaths: readonly string[]) {
    super(`Workspace persistence cannot be encoded safely: ${blockedPaths.join(', ')}.`);
    this.name = 'WorkspaceSaveCompatibilityError';
    this.blockedPaths = blockedPaths;
  }
}

export interface WorkspaceEnumValue<T extends string> {
  readonly value: T;
  readonly persistenceValue: T | number;
}

export type WorkspaceSaveCompatibilityStatus =
  | 'compatible'
  | 'opaque-passthrough'
  | 'blocked';

export interface WorkspaceSaveCompatibility {
  readonly status: WorkspaceSaveCompatibilityStatus;
  readonly canEncode: boolean;
  readonly opaquePassthroughPaths: readonly string[];
  readonly blockedPaths: readonly string[];
  readonly readOnlyUnknownPaths: readonly string[];
}

export interface WorkspaceParameterOptionV1 {
  readonly label: string;
  readonly value: string;
  readonly opaquePassthrough: WorkspaceJsonObject;
}

export interface WorkspaceParameterV1 {
  readonly id: string;
  readonly name: string;
  readonly displayName: string;
  readonly description: string | null;
  readonly dataType: string;
  readonly value: WorkspaceJsonValue;
  readonly defaultValue: WorkspaceJsonValue;
  readonly minValue: WorkspaceJsonValue;
  readonly maxValue: WorkspaceJsonValue;
  readonly isRequired: boolean;
  readonly options: readonly WorkspaceParameterOptionV1[] | null;
  readonly opaquePassthrough: WorkspaceJsonObject;
}

export type WorkspacePortDirection = 'Input' | 'Output';
export type WorkspacePortDataType =
  | 'Image'
  | 'Integer'
  | 'Float'
  | 'Boolean'
  | 'String'
  | 'Point'
  | 'Rectangle'
  | 'Contour'
  | 'PointList'
  | 'DetectionResult'
  | 'DetectionList'
  | 'CircleData'
  | 'LineData'
  | 'Region'
  | 'BlobList'
  | 'BlobFeatureList'
  | 'Any';

export interface WorkspacePortV1 {
  readonly id: string;
  readonly name: string;
  readonly direction: WorkspaceEnumValue<WorkspacePortDirection>;
  readonly dataType: WorkspaceEnumValue<WorkspacePortDataType>;
  readonly isRequired: boolean;
  readonly opaquePassthrough: WorkspaceJsonObject;
}

export type WorkspaceOperatorExecutionStatus =
  | 'NotExecuted'
  | 'Executing'
  | 'Success'
  | 'Failed'
  | 'Skipped';

export interface WorkspaceOperatorV1 {
  readonly id: string;
  readonly name: string;
  readonly type: WorkspaceEnumValue<string>;
  readonly metadata: WorkspaceJsonObject | null;
  readonly x: number;
  readonly y: number;
  readonly inputPorts: readonly WorkspacePortV1[];
  readonly outputPorts: readonly WorkspacePortV1[];
  readonly parameters: readonly WorkspaceParameterV1[];
  readonly isEnabled: boolean;
  readonly executionStatus: WorkspaceEnumValue<WorkspaceOperatorExecutionStatus>;
  readonly executionTimeMs: number | null;
  readonly errorMessage: string | null;
  readonly opaquePassthrough: WorkspaceJsonObject;
}

export interface WorkspaceConnectionV1 {
  readonly id: string;
  readonly sourceOperatorId: string;
  readonly sourcePortId: string;
  readonly targetOperatorId: string;
  readonly targetPortId: string;
  readonly opaquePassthrough: WorkspaceJsonObject;
}

export type WorkspaceDecisionValueType = 'Boolean' | 'String' | 'Integer' | 'Float';
export type WorkspaceDecisionInterpretationRule =
  | 'Boolean'
  | 'StringMap'
  | 'NumericComparison';
export type WorkspaceDecisionComparator =
  | 'Equal'
  | 'NotEqual'
  | 'GreaterThan'
  | 'GreaterThanOrEqual'
  | 'LessThan'
  | 'LessThanOrEqual';
export type WorkspaceMissingDecisionPolicy = 'Undetermined' | 'NotApplicable' | 'Invalid';

export interface WorkspaceFinalDecisionBindingV1 {
  readonly sourceOperatorId: string;
  readonly sourceOutputPortId: string | null;
  readonly sourceOutputName: string | null;
  readonly dataType: WorkspaceEnumValue<WorkspaceDecisionValueType>;
  readonly rule: WorkspaceEnumValue<WorkspaceDecisionInterpretationRule>;
  readonly trueMeansOk: boolean;
  readonly okValue: string | null;
  readonly ngValue: string | null;
  readonly comparator: WorkspaceEnumValue<WorkspaceDecisionComparator> | null;
  readonly threshold: number | null;
  readonly opaquePassthrough: WorkspaceJsonObject;
}

export interface WorkspaceDecisionConfigurationV1 {
  readonly finalDecisionBinding: WorkspaceFinalDecisionBindingV1 | null;
  readonly missingDecisionPolicy: WorkspaceEnumValue<WorkspaceMissingDecisionPolicy>;
  readonly opaquePassthrough: WorkspaceJsonObject;
}

export interface WorkspaceFlowV1 {
  readonly id: string;
  readonly name: string;
  readonly operators: readonly WorkspaceOperatorV1[];
  readonly connections: readonly WorkspaceConnectionV1[];
  readonly decisionConfiguration: WorkspaceDecisionConfigurationV1 | null;
  readonly opaquePassthrough: WorkspaceJsonObject;
}

export type WorkspaceGlobalVariableValueType = 'String' | 'Int64' | 'Double' | 'Boolean';
export type WorkspaceVariableConversionMode = 'Exact' | 'Round' | 'Floor' | 'Ceiling' | 'Truncate';

export interface WorkspaceGlobalVariableDefinitionV1 {
  readonly id: string;
  readonly name: string;
  readonly displayName: string;
  readonly description: string | null;
  readonly valueType: WorkspaceEnumValue<WorkspaceGlobalVariableValueType>;
  readonly initialValue: WorkspaceJsonValue;
  readonly min: string | number | null;
  readonly max: string | number | null;
  readonly manualWriteAllowed: boolean;
  readonly includeInResultMetadata: boolean;
  readonly order: number;
  readonly opaqueReadOnly: WorkspaceJsonObject;
}

export interface WorkspaceGlobalVariableSourceBindingV1 {
  readonly id: string;
  readonly variableId: string;
  readonly operatorId: string;
  readonly outputPortId: string;
  readonly operatorName: string;
  readonly outputPortName: string;
  readonly resultPathVersion: number | null;
  readonly resultPath: string | null;
  readonly conversionMode: WorkspaceEnumValue<WorkspaceVariableConversionMode>;
  readonly expression: string | null;
  readonly opaqueReadOnly: WorkspaceJsonObject;
}

export interface WorkspaceGlobalVariableTargetBindingV1 {
  readonly id: string;
  readonly variableId: string;
  readonly operatorId: string;
  readonly parameterId: string;
  readonly operatorName: string;
  readonly parameterName: string;
  readonly conversionMode: WorkspaceEnumValue<WorkspaceVariableConversionMode>;
  readonly expression: string | null;
  readonly opaqueReadOnly: WorkspaceJsonObject;
}

export interface WorkspaceGlobalVariablesV1 {
  readonly schemaVersion: string;
  readonly variables: readonly WorkspaceGlobalVariableDefinitionV1[];
  readonly sourceBindings: readonly WorkspaceGlobalVariableSourceBindingV1[];
  readonly targetBindings: readonly WorkspaceGlobalVariableTargetBindingV1[];
  readonly opaqueReadOnly: WorkspaceJsonObject;
}

export interface WorkspaceCalibrationAssetV1 {
  readonly assetId: string;
  readonly kind: string;
  readonly version: string;
  readonly producer: string;
  readonly sourceDraftSessionId: string;
  readonly targetNodeId: string | null;
  readonly imageIdentity: string;
  readonly contentHash: string;
  readonly projectRevision: number;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly status: string;
  readonly payload: WorkspaceJsonValue;
  readonly opaqueReadOnly: WorkspaceJsonObject;
}

export interface WorkspaceSpatialAssetV1 {
  readonly assetId: string;
  readonly kind: string;
  readonly version: string;
  readonly producer: string;
  readonly sourceDraftSessionId: string;
  readonly contentHash: string;
  readonly projectRevision: number;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly status: string;
  readonly payload: WorkspaceJsonValue;
  readonly opaqueReadOnly: WorkspaceJsonObject;
}

export interface WorkspaceProjectAssetsV1 {
  readonly schemaVersion: number;
  readonly calibrationAssets: readonly WorkspaceCalibrationAssetV1[];
  readonly spatialAssets: readonly WorkspaceSpatialAssetV1[];
  readonly opaqueReadOnly: WorkspaceJsonObject;
}

export interface WorkspaceProjectV1 {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly version: string;
  readonly persistenceRevision: number;
  readonly flow: WorkspaceFlowV1 | null;
  readonly globalSettings: Readonly<Record<string, string>>;
  readonly globalVariables: WorkspaceGlobalVariablesV1;
  readonly assets: WorkspaceProjectAssetsV1;
  readonly createdAt: string;
  readonly modifiedAt: string | null;
  readonly lastOpenedAt: string | null;
  readonly opaqueProjectFields: WorkspaceJsonObject;
  readonly saveCompatibility: WorkspaceSaveCompatibility;
}

export const workspacePersistenceFieldsV1 = Object.freeze({
  flow: Object.freeze(['id', 'name', 'decisionConfiguration', 'operators', 'connections']),
  operator: Object.freeze([
    'id', 'name', 'type', 'metadata', 'x', 'y', 'inputPorts', 'outputPorts',
    'parameters', 'isEnabled'
  ]),
  port: Object.freeze(['id', 'name', 'direction', 'dataType', 'isRequired']),
  parameter: Object.freeze([
    'id', 'name', 'displayName', 'description', 'dataType', 'value', 'defaultValue',
    'minValue', 'maxValue', 'isRequired', 'options'
  ]),
  parameterOption: Object.freeze(['label', 'value']),
  connection: Object.freeze([
    'id', 'sourceOperatorId', 'sourcePortId', 'targetOperatorId', 'targetPortId'
  ]),
  decisionConfiguration: Object.freeze(['finalDecisionBinding', 'missingDecisionPolicy']),
  finalDecisionBinding: Object.freeze([
    'sourceOperatorId', 'sourceOutputPortId', 'sourceOutputName', 'dataType', 'rule',
    'trueMeansOk', 'okValue', 'ngValue', 'comparator', 'threshold'
  ])
} as const);

export const workspaceTransientStripFieldsV1 = Object.freeze({
  operator: Object.freeze(['executionStatus', 'executionTimeMs', 'errorMessage'])
} as const);

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const emptyUuid = '00000000-0000-0000-0000-000000000000';

const operatorTypeValues = Object.freeze<Record<number, string>>({
  0: 'ImageAcquisition', 1: 'Preprocessing', 2: 'Filtering', 3: 'EdgeDetection',
  4: 'Thresholding', 5: 'Morphology', 6: 'BlobAnalysis', 7: 'TemplateMatching',
  8: 'Measurement', 9: 'CodeRecognition', 10: 'DeepLearning', 11: 'ResultOutput',
  12: 'ContourDetection', 13: 'MedianBlur', 14: 'BilateralFilter', 15: 'ImageResize',
  16: 'ImageCrop', 17: 'ImageRotate', 18: 'PerspectiveTransform', 19: 'CircleMeasurement',
  20: 'LineMeasurement', 21: 'ContourMeasurement', 22: 'AngleMeasurement',
  23: 'GeometricTolerance', 24: 'CameraCalibration', 25: 'Undistort',
  26: 'CoordinateTransform', 27: 'ModbusCommunication', 28: 'TcpCommunication',
  29: 'DatabaseWrite', 30: 'ConditionalBranch', 38: 'ColorConversion',
  39: 'AdaptiveThreshold', 40: 'HistogramEqualization', 41: 'GeometricFitting',
  42: 'RoiManager', 43: 'ShapeMatching', 44: 'SubpixelEdgeDetection',
  45: 'ColorDetection', 46: 'SerialCommunication', 50: 'SiemensS7Communication',
  51: 'MitsubishiMcCommunication', 52: 'OmronFinsCommunication', 60: 'ResultJudgment',
  61: 'DetectionSequenceJudge', 70: 'ModbusRtuCommunication', 71: 'ClaheEnhancement',
  72: 'MorphologicalOperation', 73: 'GaussianBlur', 74: 'LaplacianSharpen',
  75: 'OnnxInference', 76: 'ImageAdd', 77: 'ImageSubtract', 78: 'ImageBlend',
  80: 'VariableRead', 81: 'VariableWrite', 82: 'VariableIncrement', 83: 'TryCatch',
  84: 'CycleCounter', 90: 'AkazeFeatureMatch', 91: 'OrbFeatureMatch',
  92: 'GradientShapeMatch', 93: 'PyramidShapeMatch', 94: 'DualModalVoting',
  100: 'ForEach', 101: 'ArrayIndexer', 102: 'JsonExtractor', 110: 'MathOperation',
  111: 'LogicGate', 112: 'TypeConvert', 113: 'HttpRequest', 114: 'MqttPublish',
  115: 'StringFormat', 116: 'ImageSave', 117: 'OcrRecognition', 118: 'ImageDiff',
  119: 'Statistics', 120: 'Aggregator', 121: 'Comment', 122: 'Comparator',
  123: 'Delay', 130: 'CaliperTool', 131: 'WidthMeasurement', 132: 'PointLineDistance',
  133: 'LineLineDistance', 140: 'BoxNms', 141: 'BoxFilter',
  142: 'SharpnessEvaluation', 143: 'PositionCorrection', 150: 'NPointCalibration',
  151: 'CalibrationLoader', 152: 'UnitConvert', 153: 'TimerStatistics',
  160: 'ScriptOperator', 161: 'TriggerModule', 162: 'PointAlignment',
  163: 'PointCorrection', 164: 'GapMeasurement', 170: 'PolarUnwrap',
  171: 'ShadingCorrection', 172: 'FrameAveraging', 173: 'AffineTransform',
  174: 'ColorMeasurement', 180: 'SurfaceDefectDetection', 181: 'EdgePairDefect',
  182: 'RectangleDetection', 183: 'TranslationRotationCalibration', 190: 'CornerDetection',
  191: 'EdgeIntersection', 192: 'ParallelLineFind', 193: 'QuadrilateralFind',
  194: 'GeoMeasurement', 200: 'ImageStitching', 201: 'ImageTiling',
  202: 'ImageNormalize', 203: 'ImageCompose', 204: 'CopyMakeBorder', 210: 'TextSave',
  211: 'PointSetTool', 212: 'BlobLabeling', 213: 'HistogramAnalysis',
  214: 'PixelStatistics', 215: 'MeanFilter', 216: 'RoiTransform',
  217: 'VoxelDownsample', 218: 'StatisticalOutlierRemoval',
  219: 'RansacPlaneSegmentation', 220: 'EuclideanClusterExtraction',
  221: 'PPFEstimation', 222: 'PPFMatch', 223: 'LawsTextureFilter',
  224: 'GlcmTexture', 225: 'SemanticSegmentation', 226: 'AnomalyDetection',
  227: 'HandEyeCalibration', 228: 'HandEyeCalibrationValidator',
  229: 'FisheyeCalibration', 230: 'FisheyeUndistort', 231: 'StereoCalibration',
  232: 'PixelToWorldTransform', 233: 'PlanarMatching',
  234: 'LocalDeformableMatching', 235: 'DistanceTransform',
  236: 'MinEnclosingGeometry', 237: 'RectangleRegion', 238: 'BinaryImageToRegion',
  240: 'RegionErosion', 241: 'RegionDilation', 242: 'RegionOpening',
  243: 'RegionClosing', 244: 'RegionSkeleton', 245: 'RegionUnion',
  246: 'RegionIntersection', 247: 'RegionDifference', 248: 'RegionComplement',
  249: 'ArcCaliper', 250: 'ContourExtrema', 251: 'FFT1D',
  252: 'FrequencyFilter', 253: 'InverseFFT1D', 254: 'PhaseClosure',
  1620: 'FrameChangeTrigger'
});

const portDirectionValues = Object.freeze<Record<number, WorkspacePortDirection>>({
  0: 'Input', 1: 'Output'
});
const portDataTypeValues = Object.freeze<Record<number, WorkspacePortDataType>>({
  0: 'Image', 1: 'Integer', 2: 'Float', 3: 'Boolean', 4: 'String', 5: 'Point',
  6: 'Rectangle', 7: 'Contour', 8: 'PointList', 9: 'DetectionResult',
  10: 'DetectionList', 11: 'CircleData', 12: 'LineData', 13: 'Region',
  14: 'BlobList', 15: 'BlobFeatureList', 99: 'Any'
});
const executionStatusValues = Object.freeze<Record<number, WorkspaceOperatorExecutionStatus>>({
  0: 'NotExecuted', 1: 'Executing', 2: 'Success', 3: 'Failed', 4: 'Skipped'
});
const decisionValueTypeValues = Object.freeze<Record<number, WorkspaceDecisionValueType>>({
  0: 'Boolean', 1: 'String', 2: 'Integer', 3: 'Float'
});
const decisionRuleValues = Object.freeze<Record<number, WorkspaceDecisionInterpretationRule>>({
  0: 'Boolean', 1: 'StringMap', 2: 'NumericComparison'
});
const decisionComparatorValues = Object.freeze<Record<number, WorkspaceDecisionComparator>>({
  0: 'Equal', 1: 'NotEqual', 2: 'GreaterThan', 3: 'GreaterThanOrEqual',
  4: 'LessThan', 5: 'LessThanOrEqual'
});
const missingDecisionPolicyValues = Object.freeze<Record<number, WorkspaceMissingDecisionPolicy>>({
  0: 'Undetermined', 1: 'NotApplicable', 2: 'Invalid'
});
const globalVariableValueTypeValues = Object.freeze<Record<number, WorkspaceGlobalVariableValueType>>({
  0: 'String', 1: 'Int64', 2: 'Double', 3: 'Boolean'
});
const conversionModeValues = Object.freeze<Record<number, WorkspaceVariableConversionMode>>({
  0: 'Exact', 1: 'Round', 2: 'Floor', 3: 'Ceiling', 4: 'Truncate'
});

interface CompatibilityAccumulator {
  readonly opaquePassthroughPaths: Set<string>;
  readonly blockedPaths: Set<string>;
  readonly readOnlyUnknownPaths: Set<string>;
}

type UnknownFieldMode = 'passthrough' | 'blocked' | 'read-only';

function isRecord(value: unknown): value is Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function record(value: unknown, path: string): Record<string, unknown> {
  if (!isRecord(value)) throw new WorkspaceContractDecodeError(path, 'an object');
  return value;
}

function required(source: Record<string, unknown>, key: string, path: string): unknown {
  if (!Object.prototype.hasOwnProperty.call(source, key)) {
    throw new WorkspaceContractDecodeError(`${path}.${key}`, 'present');
  }
  return source[key];
}

function string(value: unknown, path: string, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && value.trim().length === 0)) {
    throw new WorkspaceContractDecodeError(path, allowEmpty ? 'a string' : 'a non-empty string');
  }
  return value;
}

function nullableString(value: unknown, path: string): string | null {
  if (value === null) return null;
  return string(value, path, true);
}

function boolean(value: unknown, path: string): boolean {
  if (typeof value !== 'boolean') throw new WorkspaceContractDecodeError(path, 'a boolean');
  return value;
}

function finiteNumber(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new WorkspaceContractDecodeError(path, 'a finite number');
  }
  return value;
}

function nonNegativeInteger(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new WorkspaceContractDecodeError(path, 'a non-negative safe integer');
  }
  return value;
}

function nullableNonNegativeInteger(value: unknown, path: string): number | null {
  if (value === null || value === undefined) return null;
  return nonNegativeInteger(value, path);
}

function positiveInteger(value: unknown, path: string): number {
  const decoded = nonNegativeInteger(value, path);
  if (decoded === 0) throw new WorkspaceContractDecodeError(path, 'a positive safe integer');
  return decoded;
}

function uuid(value: unknown, path: string): string {
  const decoded = string(value, path);
  if (!uuidPattern.test(decoded) || decoded.toLowerCase() === emptyUuid) {
    throw new WorkspaceContractDecodeError(path, 'a non-empty UUID');
  }
  return decoded;
}

function nullableUuid(value: unknown, path: string): string | null {
  if (value === null) return null;
  return uuid(value, path);
}

function dateTime(value: unknown, path: string): string {
  const decoded = string(value, path);
  if (Number.isNaN(Date.parse(decoded))) {
    throw new WorkspaceContractDecodeError(path, 'an ISO date-time string');
  }
  return decoded;
}

function nullableDateTime(value: unknown, path: string): string | null {
  if (value === null) return null;
  return dateTime(value, path);
}

function jsonValue(value: unknown, path: string): WorkspaceJsonValue {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
  if (typeof value === 'number') return finiteNumber(value, path);
  if (Array.isArray(value)) {
    return Object.freeze(value.map((item, index) => jsonValue(item, `${path}[${index}]`)));
  }
  const source = record(value, path);
  const target: Record<string, WorkspaceJsonValue> = {};
  for (const key of Object.keys(source)) {
    Object.defineProperty(target, key, {
      value: jsonValue(source[key], `${path}.${key}`),
      enumerable: true,
      configurable: false,
      writable: false
    });
  }
  return Object.freeze(target);
}

function jsonObject(value: unknown, path: string): WorkspaceJsonObject {
  const decoded = jsonValue(value, path);
  if (!isRecord(decoded)) throw new WorkspaceContractDecodeError(path, 'a JSON object');
  return decoded;
}

function array<T>(
  value: unknown,
  path: string,
  decode: (item: unknown, itemPath: string) => T
): readonly T[] {
  if (!Array.isArray(value)) throw new WorkspaceContractDecodeError(path, 'an array');
  return Object.freeze(value.map((item, index) => decode(item, `${path}[${index}]`)));
}

function enumValue<T extends string>(
  value: unknown,
  path: string,
  values: Readonly<Record<number, T>>
): WorkspaceEnumValue<T> {
  if (typeof value === 'number' && Number.isInteger(value) && values[value] !== undefined) {
    return Object.freeze({ value: values[value]!, persistenceValue: value });
  }
  if (typeof value === 'string' && Object.values(values).includes(value as T)) {
    return Object.freeze({ value: value as T, persistenceValue: value as T });
  }
  throw new WorkspaceContractDecodeError(path, 'a supported string or numeric enum value');
}

function collectUnknown(
  source: Record<string, unknown>,
  knownFields: readonly string[],
  path: string,
  accumulator: CompatibilityAccumulator,
  mode: UnknownFieldMode
): WorkspaceJsonObject {
  const known = new Set(knownFields);
  const knownCaseInsensitive = new Set(knownFields.map(field => field.toLocaleLowerCase('en-US')));
  const entries: Array<readonly [string, WorkspaceJsonValue]> = [];
  for (const key of Object.keys(source)) {
    if (known.has(key)) continue;
    const fieldPath = `${path}.${key}`;
    entries.push([key, jsonValue(source[key], fieldPath)]);
    if (knownCaseInsensitive.has(key.toLocaleLowerCase('en-US'))) {
      accumulator.blockedPaths.add(fieldPath);
    } else if (mode === 'passthrough') {
      accumulator.opaquePassthroughPaths.add(fieldPath);
    } else if (mode === 'blocked') {
      accumulator.blockedPaths.add(fieldPath);
    } else {
      accumulator.readOnlyUnknownPaths.add(fieldPath);
    }
  }
  return Object.freeze(Object.fromEntries(entries));
}

function decodeParameterOption(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceParameterOptionV1 {
  const source = record(value, path);
  const known = workspacePersistenceFieldsV1.parameterOption;
  return Object.freeze({
    label: string(required(source, 'label', path), `${path}.label`, true),
    value: string(required(source, 'value', path), `${path}.value`, true),
    opaquePassthrough: collectUnknown(source, known, path, accumulator, 'passthrough')
  });
}

function decodeParameter(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceParameterV1 {
  const source = record(value, path);
  const options = required(source, 'options', path);
  return Object.freeze({
    id: uuid(required(source, 'id', path), `${path}.id`),
    name: string(required(source, 'name', path), `${path}.name`),
    displayName: string(required(source, 'displayName', path), `${path}.displayName`, true),
    description: nullableString(required(source, 'description', path), `${path}.description`),
    dataType: string(required(source, 'dataType', path), `${path}.dataType`),
    value: jsonValue(required(source, 'value', path), `${path}.value`),
    defaultValue: jsonValue(required(source, 'defaultValue', path), `${path}.defaultValue`),
    minValue: jsonValue(required(source, 'minValue', path), `${path}.minValue`),
    maxValue: jsonValue(required(source, 'maxValue', path), `${path}.maxValue`),
    isRequired: boolean(required(source, 'isRequired', path), `${path}.isRequired`),
    options: options === null
      ? null
      : array(options, `${path}.options`, (item, itemPath) =>
          decodeParameterOption(item, itemPath, accumulator)),
    opaquePassthrough: collectUnknown(
      source,
      workspacePersistenceFieldsV1.parameter,
      path,
      accumulator,
      'passthrough'
    )
  });
}

function decodePort(
  value: unknown,
  path: string,
  expectedDirection: WorkspacePortDirection,
  accumulator: CompatibilityAccumulator
): WorkspacePortV1 {
  const source = record(value, path);
  const direction = enumValue(
    required(source, 'direction', path),
    `${path}.direction`,
    portDirectionValues
  );
  if (direction.value !== expectedDirection) {
    throw new WorkspaceContractDecodeError(`${path}.direction`, expectedDirection);
  }
  return Object.freeze({
    id: uuid(required(source, 'id', path), `${path}.id`),
    name: string(required(source, 'name', path), `${path}.name`),
    direction,
    dataType: enumValue(required(source, 'dataType', path), `${path}.dataType`, portDataTypeValues),
    isRequired: boolean(required(source, 'isRequired', path), `${path}.isRequired`),
    opaquePassthrough: collectUnknown(
      source,
      workspacePersistenceFieldsV1.port,
      path,
      accumulator,
      'passthrough'
    )
  });
}

function decodeOperator(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceOperatorV1 {
  const source = record(value, path);
  const metadata = Object.prototype.hasOwnProperty.call(source, 'metadata')
    ? source.metadata
    : null;
  const known = [
    ...workspacePersistenceFieldsV1.operator,
    ...workspaceTransientStripFieldsV1.operator
  ];
  return Object.freeze({
    id: uuid(required(source, 'id', path), `${path}.id`),
    name: string(required(source, 'name', path), `${path}.name`),
    type: enumValue(required(source, 'type', path), `${path}.type`, operatorTypeValues),
    metadata: metadata === null ? null : jsonObject(metadata, `${path}.metadata`),
    x: finiteNumber(required(source, 'x', path), `${path}.x`),
    y: finiteNumber(required(source, 'y', path), `${path}.y`),
    inputPorts: array(required(source, 'inputPorts', path), `${path}.inputPorts`,
      (item, itemPath) => decodePort(item, itemPath, 'Input', accumulator)),
    outputPorts: array(required(source, 'outputPorts', path), `${path}.outputPorts`,
      (item, itemPath) => decodePort(item, itemPath, 'Output', accumulator)),
    parameters: array(required(source, 'parameters', path), `${path}.parameters`,
      (item, itemPath) => decodeParameter(item, itemPath, accumulator)),
    isEnabled: boolean(required(source, 'isEnabled', path), `${path}.isEnabled`),
    executionStatus: enumValue(
      required(source, 'executionStatus', path),
      `${path}.executionStatus`,
      executionStatusValues
    ),
    executionTimeMs: nullableNonNegativeInteger(
      required(source, 'executionTimeMs', path),
      `${path}.executionTimeMs`
    ),
    errorMessage: nullableString(required(source, 'errorMessage', path), `${path}.errorMessage`),
    opaquePassthrough: collectUnknown(source, known, path, accumulator, 'passthrough')
  });
}

function decodeConnection(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceConnectionV1 {
  const source = record(value, path);
  return Object.freeze({
    id: uuid(required(source, 'id', path), `${path}.id`),
    sourceOperatorId: uuid(required(source, 'sourceOperatorId', path), `${path}.sourceOperatorId`),
    sourcePortId: uuid(required(source, 'sourcePortId', path), `${path}.sourcePortId`),
    targetOperatorId: uuid(required(source, 'targetOperatorId', path), `${path}.targetOperatorId`),
    targetPortId: uuid(required(source, 'targetPortId', path), `${path}.targetPortId`),
    opaquePassthrough: collectUnknown(
      source,
      workspacePersistenceFieldsV1.connection,
      path,
      accumulator,
      'passthrough'
    )
  });
}

function decodeDecisionBinding(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceFinalDecisionBindingV1 {
  const source = record(value, path);
  const comparator = required(source, 'comparator', path);
  const threshold = required(source, 'threshold', path);
  return Object.freeze({
    sourceOperatorId: uuid(required(source, 'sourceOperatorId', path), `${path}.sourceOperatorId`),
    sourceOutputPortId: nullableUuid(
      required(source, 'sourceOutputPortId', path),
      `${path}.sourceOutputPortId`
    ),
    sourceOutputName: nullableString(
      required(source, 'sourceOutputName', path),
      `${path}.sourceOutputName`
    ),
    dataType: enumValue(required(source, 'dataType', path), `${path}.dataType`, decisionValueTypeValues),
    rule: enumValue(required(source, 'rule', path), `${path}.rule`, decisionRuleValues),
    trueMeansOk: boolean(required(source, 'trueMeansOk', path), `${path}.trueMeansOk`),
    okValue: nullableString(required(source, 'okValue', path), `${path}.okValue`),
    ngValue: nullableString(required(source, 'ngValue', path), `${path}.ngValue`),
    comparator: comparator === null
      ? null
      : enumValue(comparator, `${path}.comparator`, decisionComparatorValues),
    threshold: threshold === null ? null : finiteNumber(threshold, `${path}.threshold`),
    opaquePassthrough: collectUnknown(
      source,
      workspacePersistenceFieldsV1.finalDecisionBinding,
      path,
      accumulator,
      'passthrough'
    )
  });
}

function decodeDecisionConfiguration(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceDecisionConfigurationV1 | null {
  if (value === null) return null;
  const source = record(value, path);
  const binding = required(source, 'finalDecisionBinding', path);
  return Object.freeze({
    finalDecisionBinding: binding === null
      ? null
      : decodeDecisionBinding(binding, `${path}.finalDecisionBinding`, accumulator),
    missingDecisionPolicy: enumValue(
      required(source, 'missingDecisionPolicy', path),
      `${path}.missingDecisionPolicy`,
      missingDecisionPolicyValues
    ),
    opaquePassthrough: collectUnknown(
      source,
      workspacePersistenceFieldsV1.decisionConfiguration,
      path,
      accumulator,
      'passthrough'
    )
  });
}

function decodeFlow(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceFlowV1 | null {
  if (value === null) return null;
  const source = record(value, path);
  return Object.freeze({
    id: uuid(required(source, 'id', path), `${path}.id`),
    name: string(required(source, 'name', path), `${path}.name`),
    operators: array(required(source, 'operators', path), `${path}.operators`,
      (item, itemPath) => decodeOperator(item, itemPath, accumulator)),
    connections: array(required(source, 'connections', path), `${path}.connections`,
      (item, itemPath) => decodeConnection(item, itemPath, accumulator)),
    decisionConfiguration: decodeDecisionConfiguration(
      required(source, 'decisionConfiguration', path),
      `${path}.decisionConfiguration`,
      accumulator
    ),
    opaquePassthrough: collectUnknown(
      source,
      workspacePersistenceFieldsV1.flow,
      path,
      accumulator,
      'passthrough'
    )
  });
}

function decodeNumericBound(value: unknown, path: string): string | number | null {
  if (value === null) return null;
  if (typeof value === 'string') return value;
  return finiteNumber(value, path);
}

function decodeGlobalVariableDefinition(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceGlobalVariableDefinitionV1 {
  const source = record(value, path);
  const known = [
    'id', 'name', 'displayName', 'description', 'valueType', 'initialValue', 'min', 'max',
    'manualWriteAllowed', 'includeInResultMetadata', 'order'
  ];
  return Object.freeze({
    id: uuid(required(source, 'id', path), `${path}.id`),
    name: string(required(source, 'name', path), `${path}.name`),
    displayName: string(required(source, 'displayName', path), `${path}.displayName`, true),
    description: nullableString(required(source, 'description', path), `${path}.description`),
    valueType: enumValue(required(source, 'valueType', path), `${path}.valueType`, globalVariableValueTypeValues),
    initialValue: jsonValue(required(source, 'initialValue', path), `${path}.initialValue`),
    min: decodeNumericBound(required(source, 'min', path), `${path}.min`),
    max: decodeNumericBound(required(source, 'max', path), `${path}.max`),
    manualWriteAllowed: boolean(
      required(source, 'manualWriteAllowed', path),
      `${path}.manualWriteAllowed`
    ),
    includeInResultMetadata: boolean(
      required(source, 'includeInResultMetadata', path),
      `${path}.includeInResultMetadata`
    ),
    order: nonNegativeInteger(required(source, 'order', path), `${path}.order`),
    opaqueReadOnly: collectUnknown(source, known, path, accumulator, 'read-only')
  });
}

function decodeGlobalVariableSourceBinding(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceGlobalVariableSourceBindingV1 {
  const source = record(value, path);
  const known = [
    'id', 'variableId', 'operatorId', 'outputPortId', 'operatorName', 'outputPortName',
    'resultPathVersion', 'resultPath', 'conversionMode', 'expression'
  ];
  return Object.freeze({
    id: uuid(required(source, 'id', path), `${path}.id`),
    variableId: uuid(required(source, 'variableId', path), `${path}.variableId`),
    operatorId: uuid(required(source, 'operatorId', path), `${path}.operatorId`),
    outputPortId: uuid(required(source, 'outputPortId', path), `${path}.outputPortId`),
    operatorName: string(required(source, 'operatorName', path), `${path}.operatorName`, true),
    outputPortName: string(required(source, 'outputPortName', path), `${path}.outputPortName`, true),
    resultPathVersion: nullableNonNegativeInteger(source.resultPathVersion, `${path}.resultPathVersion`),
    resultPath: source.resultPath === undefined
      ? null
      : nullableString(source.resultPath, `${path}.resultPath`),
    conversionMode: enumValue(
      required(source, 'conversionMode', path),
      `${path}.conversionMode`,
      conversionModeValues
    ),
    expression: nullableString(required(source, 'expression', path), `${path}.expression`),
    opaqueReadOnly: collectUnknown(source, known, path, accumulator, 'read-only')
  });
}

function decodeGlobalVariableTargetBinding(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceGlobalVariableTargetBindingV1 {
  const source = record(value, path);
  const known = [
    'id', 'variableId', 'operatorId', 'parameterId', 'operatorName', 'parameterName',
    'conversionMode', 'expression'
  ];
  return Object.freeze({
    id: uuid(required(source, 'id', path), `${path}.id`),
    variableId: uuid(required(source, 'variableId', path), `${path}.variableId`),
    operatorId: uuid(required(source, 'operatorId', path), `${path}.operatorId`),
    parameterId: uuid(required(source, 'parameterId', path), `${path}.parameterId`),
    operatorName: string(required(source, 'operatorName', path), `${path}.operatorName`, true),
    parameterName: string(required(source, 'parameterName', path), `${path}.parameterName`, true),
    conversionMode: enumValue(
      required(source, 'conversionMode', path),
      `${path}.conversionMode`,
      conversionModeValues
    ),
    expression: nullableString(required(source, 'expression', path), `${path}.expression`),
    opaqueReadOnly: collectUnknown(source, known, path, accumulator, 'read-only')
  });
}

function decodeGlobalVariables(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceGlobalVariablesV1 {
  const source = record(value, path);
  const known = ['schemaVersion', 'variables', 'sourceBindings', 'targetBindings'];
  return Object.freeze({
    schemaVersion: string(required(source, 'schemaVersion', path), `${path}.schemaVersion`),
    variables: array(required(source, 'variables', path), `${path}.variables`,
      (item, itemPath) => decodeGlobalVariableDefinition(item, itemPath, accumulator)),
    sourceBindings: array(required(source, 'sourceBindings', path), `${path}.sourceBindings`,
      (item, itemPath) => decodeGlobalVariableSourceBinding(item, itemPath, accumulator)),
    targetBindings: array(required(source, 'targetBindings', path), `${path}.targetBindings`,
      (item, itemPath) => decodeGlobalVariableTargetBinding(item, itemPath, accumulator)),
    opaqueReadOnly: collectUnknown(source, known, path, accumulator, 'read-only')
  });
}

function decodeCalibrationAsset(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceCalibrationAssetV1 {
  const source = record(value, path);
  const known = [
    'assetId', 'kind', 'version', 'producer', 'sourceDraftSessionId', 'targetNodeId',
    'imageIdentity', 'contentHash', 'projectRevision', 'createdAtUtc', 'updatedAtUtc',
    'status', 'payload'
  ];
  return Object.freeze({
    assetId: string(required(source, 'assetId', path), `${path}.assetId`),
    kind: string(required(source, 'kind', path), `${path}.kind`),
    version: string(required(source, 'version', path), `${path}.version`, true),
    producer: string(required(source, 'producer', path), `${path}.producer`, true),
    sourceDraftSessionId: string(
      required(source, 'sourceDraftSessionId', path),
      `${path}.sourceDraftSessionId`,
      true
    ),
    targetNodeId: nullableUuid(required(source, 'targetNodeId', path), `${path}.targetNodeId`),
    imageIdentity: string(required(source, 'imageIdentity', path), `${path}.imageIdentity`, true),
    contentHash: string(required(source, 'contentHash', path), `${path}.contentHash`, true),
    projectRevision: nonNegativeInteger(
      required(source, 'projectRevision', path),
      `${path}.projectRevision`
    ),
    createdAtUtc: dateTime(required(source, 'createdAtUtc', path), `${path}.createdAtUtc`),
    updatedAtUtc: dateTime(required(source, 'updatedAtUtc', path), `${path}.updatedAtUtc`),
    status: string(required(source, 'status', path), `${path}.status`),
    payload: jsonValue(required(source, 'payload', path), `${path}.payload`),
    opaqueReadOnly: collectUnknown(source, known, path, accumulator, 'read-only')
  });
}

function decodeSpatialAsset(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceSpatialAssetV1 {
  const source = record(value, path);
  const known = [
    'assetId', 'kind', 'version', 'producer', 'sourceDraftSessionId', 'contentHash',
    'projectRevision', 'createdAtUtc', 'updatedAtUtc', 'status', 'payload'
  ];
  return Object.freeze({
    assetId: string(required(source, 'assetId', path), `${path}.assetId`),
    kind: string(required(source, 'kind', path), `${path}.kind`),
    version: string(required(source, 'version', path), `${path}.version`, true),
    producer: string(required(source, 'producer', path), `${path}.producer`, true),
    sourceDraftSessionId: string(
      required(source, 'sourceDraftSessionId', path),
      `${path}.sourceDraftSessionId`,
      true
    ),
    contentHash: string(required(source, 'contentHash', path), `${path}.contentHash`, true),
    projectRevision: nonNegativeInteger(
      required(source, 'projectRevision', path),
      `${path}.projectRevision`
    ),
    createdAtUtc: dateTime(required(source, 'createdAtUtc', path), `${path}.createdAtUtc`),
    updatedAtUtc: dateTime(required(source, 'updatedAtUtc', path), `${path}.updatedAtUtc`),
    status: string(required(source, 'status', path), `${path}.status`),
    payload: jsonValue(required(source, 'payload', path), `${path}.payload`),
    opaqueReadOnly: collectUnknown(source, known, path, accumulator, 'read-only')
  });
}

function decodeAssets(
  value: unknown,
  path: string,
  accumulator: CompatibilityAccumulator
): WorkspaceProjectAssetsV1 {
  const source = record(value, path);
  const known = ['schemaVersion', 'calibrationAssets', 'spatialAssets'];
  return Object.freeze({
    schemaVersion: positiveInteger(required(source, 'schemaVersion', path), `${path}.schemaVersion`),
    calibrationAssets: array(
      required(source, 'calibrationAssets', path),
      `${path}.calibrationAssets`,
      (item, itemPath) => decodeCalibrationAsset(item, itemPath, accumulator)
    ),
    spatialAssets: array(
      required(source, 'spatialAssets', path),
      `${path}.spatialAssets`,
      (item, itemPath) => decodeSpatialAsset(item, itemPath, accumulator)
    ),
    opaqueReadOnly: collectUnknown(source, known, path, accumulator, 'read-only')
  });
}

function decodeGlobalSettings(value: unknown, path: string): Readonly<Record<string, string>> {
  const source = record(value, path);
  const decoded: Record<string, string> = {};
  for (const key of Object.keys(source)) {
    decoded[key] = string(source[key], `${path}.${key}`, true);
  }
  return Object.freeze(decoded);
}

function createSaveCompatibility(accumulator: CompatibilityAccumulator): WorkspaceSaveCompatibility {
  const opaquePassthroughPaths = Object.freeze([...accumulator.opaquePassthroughPaths].sort());
  const blockedPaths = Object.freeze([...accumulator.blockedPaths].sort());
  const readOnlyUnknownPaths = Object.freeze([...accumulator.readOnlyUnknownPaths].sort());
  const status: WorkspaceSaveCompatibilityStatus = blockedPaths.length > 0
    ? 'blocked'
    : opaquePassthroughPaths.length > 0 ? 'opaque-passthrough' : 'compatible';
  return Object.freeze({
    status,
    canEncode: status !== 'blocked',
    opaquePassthroughPaths,
    blockedPaths,
    readOnlyUnknownPaths
  });
}

export function isWorkspaceProjectId(value: string): boolean {
  return uuidPattern.test(value) && value.toLowerCase() !== emptyUuid;
}

export function decodeWorkspaceProjectV1(payload: unknown): WorkspaceProjectV1 {
  const source = record(payload, '$');
  const accumulator: CompatibilityAccumulator = {
    opaquePassthroughPaths: new Set<string>(),
    blockedPaths: new Set<string>(),
    readOnlyUnknownPaths: new Set<string>()
  };
  const known = [
    'id', 'name', 'description', 'version', 'persistenceRevision', 'flow',
    'globalSettings', 'globalVariables', 'assets', 'createdAt', 'modifiedAt', 'lastOpenedAt'
  ];
  const project = {
    id: uuid(required(source, 'id', '$'), '$.id'),
    name: string(required(source, 'name', '$'), '$.name'),
    description: nullableString(required(source, 'description', '$'), '$.description'),
    version: string(required(source, 'version', '$'), '$.version'),
    persistenceRevision: nonNegativeInteger(
      required(source, 'persistenceRevision', '$'),
      '$.persistenceRevision'
    ),
    flow: decodeFlow(required(source, 'flow', '$'), '$.flow', accumulator),
    globalSettings: decodeGlobalSettings(
      required(source, 'globalSettings', '$'),
      '$.globalSettings'
    ),
    globalVariables: decodeGlobalVariables(
      required(source, 'globalVariables', '$'),
      '$.globalVariables',
      accumulator
    ),
    assets: decodeAssets(required(source, 'assets', '$'), '$.assets', accumulator),
    createdAt: dateTime(required(source, 'createdAt', '$'), '$.createdAt'),
    modifiedAt: nullableDateTime(required(source, 'modifiedAt', '$'), '$.modifiedAt'),
    lastOpenedAt: nullableDateTime(required(source, 'lastOpenedAt', '$'), '$.lastOpenedAt'),
    opaqueProjectFields: collectUnknown(source, known, '$', accumulator, 'blocked')
  };
  return Object.freeze({
    ...project,
    saveCompatibility: createSaveCompatibility(accumulator)
  });
}

function encodeEnum<T extends string>(value: WorkspaceEnumValue<T>): T | number {
  return value.persistenceValue;
}

function encodeParameterOption(option: WorkspaceParameterOptionV1): WorkspaceJsonObject {
  return Object.freeze({ ...option.opaquePassthrough, label: option.label, value: option.value });
}

function encodeParameter(parameter: WorkspaceParameterV1): WorkspaceJsonObject {
  return Object.freeze({
    ...parameter.opaquePassthrough,
    id: parameter.id,
    name: parameter.name,
    displayName: parameter.displayName,
    description: parameter.description,
    dataType: parameter.dataType,
    value: parameter.value,
    defaultValue: parameter.defaultValue,
    minValue: parameter.minValue,
    maxValue: parameter.maxValue,
    isRequired: parameter.isRequired,
    options: parameter.options === null
      ? null
      : Object.freeze(parameter.options.map(encodeParameterOption))
  });
}

function encodePort(port: WorkspacePortV1): WorkspaceJsonObject {
  return Object.freeze({
    ...port.opaquePassthrough,
    id: port.id,
    name: port.name,
    direction: encodeEnum(port.direction),
    dataType: encodeEnum(port.dataType),
    isRequired: port.isRequired
  });
}

function encodeOperator(operator: WorkspaceOperatorV1): WorkspaceJsonObject {
  const encoded: Record<string, WorkspaceJsonValue> = {
    ...operator.opaquePassthrough,
    id: operator.id,
    name: operator.name,
    type: encodeEnum(operator.type),
    x: operator.x,
    y: operator.y,
    inputPorts: Object.freeze(operator.inputPorts.map(encodePort)),
    outputPorts: Object.freeze(operator.outputPorts.map(encodePort)),
    parameters: Object.freeze(operator.parameters.map(encodeParameter)),
    isEnabled: operator.isEnabled
  };
  if (operator.metadata !== null) encoded.metadata = operator.metadata;
  return Object.freeze(encoded);
}

function encodeConnection(connection: WorkspaceConnectionV1): WorkspaceJsonObject {
  return Object.freeze({
    ...connection.opaquePassthrough,
    id: connection.id,
    sourceOperatorId: connection.sourceOperatorId,
    sourcePortId: connection.sourcePortId,
    targetOperatorId: connection.targetOperatorId,
    targetPortId: connection.targetPortId
  });
}

function encodeDecisionBinding(binding: WorkspaceFinalDecisionBindingV1): WorkspaceJsonObject {
  return Object.freeze({
    ...binding.opaquePassthrough,
    sourceOperatorId: binding.sourceOperatorId,
    sourceOutputPortId: binding.sourceOutputPortId,
    sourceOutputName: binding.sourceOutputName,
    dataType: encodeEnum(binding.dataType),
    rule: encodeEnum(binding.rule),
    trueMeansOk: binding.trueMeansOk,
    okValue: binding.okValue,
    ngValue: binding.ngValue,
    comparator: binding.comparator === null ? null : encodeEnum(binding.comparator),
    threshold: binding.threshold
  });
}

function encodeDecisionConfiguration(
  decision: WorkspaceDecisionConfigurationV1 | null
): WorkspaceJsonObject | null {
  if (decision === null) return null;
  return Object.freeze({
    ...decision.opaquePassthrough,
    finalDecisionBinding: decision.finalDecisionBinding === null
      ? null
      : encodeDecisionBinding(decision.finalDecisionBinding),
    missingDecisionPolicy: encodeEnum(decision.missingDecisionPolicy)
  });
}

export function encodeWorkspaceFlowUpdateV1(project: WorkspaceProjectV1): WorkspaceJsonObject | null {
  if (!project.saveCompatibility.canEncode) {
    throw new WorkspaceSaveCompatibilityError(project.saveCompatibility.blockedPaths);
  }
  const flow = project.flow;
  if (flow === null) return null;
  return Object.freeze({
    ...flow.opaquePassthrough,
    id: flow.id,
    name: flow.name,
    operators: Object.freeze(flow.operators.map(encodeOperator)),
    connections: Object.freeze(flow.connections.map(encodeConnection)),
    decisionConfiguration: encodeDecisionConfiguration(flow.decisionConfiguration)
  });
}
