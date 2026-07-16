import {
  geometryFromParams as canonicalGeometryFromParams,
  geometryToParams as canonicalGeometryToParams,
  getOperatorRoiConfig as canonicalGetOperatorRoiConfig
} from '@clearvision/canonical-roi-support';

export const circleSearchV2StartupFlag = 'Studio:CircleSearchV2ToolEnabled';
export const nPointCalibrationWorkbenchStartupFlag = 'Studio:NPointCalibrationWorkbenchEnabled';

export interface RoiImageBounds {
  readonly width: number;
  readonly height: number;
}

export interface RoiDraftParameter {
  readonly name?: unknown;
  readonly Name?: unknown;
  readonly value?: unknown;
  readonly Value?: unknown;
  readonly defaultValue?: unknown;
  readonly DefaultValue?: unknown;
}

export interface RoiSelectedNodeDraft {
  readonly id?: unknown;
  readonly Id?: unknown;
  readonly type?: unknown;
  readonly Type?: unknown;
  readonly operatorType?: unknown;
  readonly OperatorType?: unknown;
  readonly parameters?: readonly RoiDraftParameter[];
  readonly Parameters?: readonly RoiDraftParameter[];
}

export type RoiStartupFlags = Readonly<Record<string, boolean | undefined>>;

export type RoiGeometryKind =
  | 'rectangle'
  | 'circle'
  | 'polygon'
  | 'annulus'
  | 'arc'
  | 'circle-search-v2'
  | 'point-sequence';

export type RoiEditorDescriptorKind =
  | 'roi-manager-rectangle'
  | 'roi-manager-circle'
  | 'roi-manager-polygon'
  | 'template-matching-roi'
  | 'box-filter-region'
  | 'polar-annulus'
  | 'polar-arc'
  | 'circle-search-v2'
  | 'npoint-sequence'
  | 'caliper-search-region'
  | 'unsupported';

interface RoiEditorDescriptorBase {
  readonly descriptorId: string;
  readonly nodeId: string;
  readonly nodeType: string;
  readonly kind: RoiEditorDescriptorKind;
  readonly geometryKind: RoiGeometryKind | null;
  readonly supported: boolean;
  readonly editable: boolean;
  readonly parameterNames: readonly string[];
  readonly message: string;
  readonly canonicalConfig: Readonly<Record<string, unknown>>;
}

export interface RoiParameterEditorDescriptor extends RoiEditorDescriptorBase {
  readonly commandKind: 'parameter-patch';
  readonly kind: Exclude<RoiEditorDescriptorKind, 'caliper-search-region' | 'unsupported'>;
  readonly geometryKind: RoiGeometryKind;
  readonly supported: true;
}

export interface RoiCaliperEditorDescriptor extends RoiEditorDescriptorBase {
  readonly commandKind: 'caliper-structural';
  readonly kind: 'caliper-search-region';
  readonly geometryKind: 'rectangle';
  readonly supported: true;
  readonly sourceOperatorType: 'RectangleRegion';
  readonly sourceOutputPortName: 'Rectangle';
  readonly targetInputPortName: 'SearchRegion';
}

export interface RoiUnsupportedEditorDescriptor extends RoiEditorDescriptorBase {
  readonly commandKind: 'unsupported';
  readonly kind: 'unsupported';
  readonly geometryKind: null;
  readonly supported: false;
  readonly editable: false;
}

export type RoiEditorDescriptor =
  | RoiParameterEditorDescriptor
  | RoiCaliperEditorDescriptor
  | RoiUnsupportedEditorDescriptor;

export interface RoiRectangleGeometry {
  readonly kind: 'rectangle';
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

export interface RoiCircleGeometry {
  readonly kind: 'circle';
  readonly centerX: number;
  readonly centerY: number;
  readonly radius: number;
}

export interface RoiAnnulusGeometry {
  readonly kind: 'annulus' | 'arc';
  readonly centerX: number;
  readonly centerY: number;
  readonly innerRadius: number;
  readonly outerRadius: number;
  readonly startAngle: number;
  readonly endAngle: number;
  readonly spanDegrees?: number;
}

export interface RoiCircleSearchV2Geometry {
  readonly kind: 'circleSearchV2';
  readonly searchCenterMode: string;
  readonly centerX: number;
  readonly centerY: number;
  readonly minRadius: number;
  readonly nominalRadius: number;
  readonly maxRadius: number;
}

export interface RoiPolygonGeometry {
  readonly kind: 'polygon';
  readonly points: readonly Readonly<{ x: number; y: number }>[];
}

export interface RoiPointSequenceGeometry {
  readonly kind: 'pointSequence';
  readonly points: readonly Readonly<{
    x: number;
    y: number;
    worldX: number;
    worldY: number;
    enabled: boolean;
  }>[];
}

export type RoiGeometry =
  | RoiRectangleGeometry
  | RoiCircleGeometry
  | RoiAnnulusGeometry
  | RoiCircleSearchV2Geometry
  | RoiPolygonGeometry
  | RoiPointSequenceGeometry;

export type RoiParameterValue = string | number | boolean | null;

export interface RoiParameterPatchPayload {
  readonly kind: 'parameter-patch';
  readonly nodeId: string;
  readonly descriptorId: string;
  readonly values: Readonly<Record<string, RoiParameterValue>>;
}

export interface RoiCaliperStructuralPayload {
  readonly kind: 'caliper-structural';
  readonly caliperNodeId: string;
  readonly descriptorId: string;
  readonly sourceOperatorType: 'RectangleRegion';
  readonly sourceOutputPortName: 'Rectangle';
  readonly targetInputPortName: 'SearchRegion';
  readonly regionParameters: Readonly<Record<'X' | 'Y' | 'Width' | 'Height', number>>;
}

export interface RoiUnsupportedCommitPayload {
  readonly kind: 'unsupported';
  readonly nodeId: string;
  readonly descriptorId: string;
  readonly reason: string;
}

export type RoiCommitPayload =
  | RoiParameterPatchPayload
  | RoiCaliperStructuralPayload
  | RoiUnsupportedCommitPayload;

export interface RoiSessionIdentityInput {
  readonly projectId: string;
  readonly nodeId: string;
  readonly selectionRevision: number;
  readonly flowRevision: number;
  readonly previewRequestKey: string | null;
  readonly imageGeneration: number;
}

export interface RoiSessionIdentity extends RoiSessionIdentityInput {
  readonly key: string;
}

interface CanonicalRoiConfig extends Readonly<Record<string, unknown>> {
  readonly supported?: boolean;
  readonly editable?: boolean;
  readonly shape?: unknown;
  readonly subtitle?: unknown;
  readonly readonlyMessage?: unknown;
  readonly geometryAdapter?: Readonly<Record<string, unknown>>;
  readonly commitValues?: Readonly<Record<string, unknown>>;
}

type CanonicalGetOperatorRoiConfig = (
  operator: Readonly<Record<string, unknown>>,
  options: Readonly<Record<string, boolean>>
) => CanonicalRoiConfig;

type CanonicalGeometryFromParams = (
  values: Readonly<Record<string, unknown>>,
  config: CanonicalRoiConfig,
  bounds?: RoiImageBounds | null
) => unknown;

type CanonicalGeometryToParams = (
  geometry: RoiGeometry,
  config: CanonicalRoiConfig
) => Readonly<Record<string, unknown>>;

const getOperatorRoiConfig = canonicalGetOperatorRoiConfig as CanonicalGetOperatorRoiConfig;
const geometryFromParams = canonicalGeometryFromParams as CanonicalGeometryFromParams;
const geometryToParams = canonicalGeometryToParams as CanonicalGeometryToParams;

const descriptorParameters: Readonly<Record<Exclude<RoiEditorDescriptorKind, 'unsupported'>, readonly string[]>> =
  Object.freeze({
    'roi-manager-rectangle': Object.freeze(['X', 'Y', 'Width', 'Height']),
    'roi-manager-circle': Object.freeze(['CenterX', 'CenterY', 'Radius']),
    'roi-manager-polygon': Object.freeze(['PolygonPoints']),
    'template-matching-roi': Object.freeze(['UseRoi', 'RoiX', 'RoiY', 'RoiWidth', 'RoiHeight']),
    'box-filter-region': Object.freeze(['RegionX', 'RegionY', 'RegionW', 'RegionH']),
    'polar-annulus': Object.freeze(['CenterX', 'CenterY', 'InnerRadius', 'OuterRadius', 'StartAngle', 'EndAngle']),
    'polar-arc': Object.freeze(['CenterX', 'CenterY', 'InnerRadius', 'OuterRadius', 'StartAngle', 'EndAngle']),
    'circle-search-v2': Object.freeze([
      'SearchCenterMode', 'SearchCenterX', 'SearchCenterY', 'MinRadius', 'NominalRadius', 'MaxRadius'
    ]),
    'npoint-sequence': Object.freeze(['PointPairs']),
    'caliper-search-region': Object.freeze(['X', 'Y', 'Width', 'Height'])
  });

function record(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : String(value ?? '').trim();
}

function field(source: Readonly<Record<string, unknown>>, camelName: string): unknown {
  if (Object.prototype.hasOwnProperty.call(source, camelName)) return source[camelName];
  const pascalName = `${camelName.slice(0, 1).toUpperCase()}${camelName.slice(1)}`;
  return source[pascalName];
}

function nodeIdentity(node: RoiSelectedNodeDraft): Readonly<{ id: string; type: string }> {
  const source = record(node);
  return Object.freeze({
    id: text(field(source, 'id')),
    type: text(field(source, 'type') ?? field(source, 'operatorType'))
  });
}

function normalizedNode(node: RoiSelectedNodeDraft): Readonly<Record<string, unknown>> {
  const source = record(node);
  const parameters = field(source, 'parameters');
  return Object.freeze({
    ...source,
    id: text(field(source, 'id')),
    type: text(field(source, 'type') ?? field(source, 'operatorType')),
    parameters: Object.freeze((Array.isArray(parameters) ? parameters : []).map(entry => {
      const parameter = record(entry);
      return Object.freeze({
        ...parameter,
        name: text(field(parameter, 'name')),
        value: field(parameter, 'value'),
        defaultValue: field(parameter, 'defaultValue')
      });
    }))
  });
}

function parameterValues(node: RoiSelectedNodeDraft): Readonly<Record<string, unknown>> {
  const normalized = normalizedNode(node);
  const parameters = normalized.parameters as readonly Readonly<Record<string, unknown>>[];
  return Object.freeze(parameters.reduce<Record<string, unknown>>((values, parameter) => {
    const name = text(parameter.name);
    if (name) values[name] = parameter.value ?? parameter.defaultValue;
    return values;
  }, {}));
}

function startupFlag(flags: RoiStartupFlags, key: string, logicalName: string): boolean {
  return flags[key] === true || flags[logicalName] === true;
}

function canonicalOptions(flags: RoiStartupFlags): Readonly<Record<string, boolean>> {
  return Object.freeze({
    circleSearchV2ToolEnabled: startupFlag(flags, circleSearchV2StartupFlag, 'circleSearchV2ToolEnabled'),
    nPointCalibrationWorkbenchEnabled: startupFlag(
      flags,
      nPointCalibrationWorkbenchStartupFlag,
      'nPointCalibrationWorkbenchEnabled'
    )
  });
}

function descriptorKind(nodeType: string, config: CanonicalRoiConfig): RoiEditorDescriptorKind {
  const shape = text(config.shape).toLowerCase();
  if (nodeType === 'RoiManager') {
    if (shape === 'rectangle') return 'roi-manager-rectangle';
    if (shape === 'circle') return 'roi-manager-circle';
    if (shape === 'polygon') return 'roi-manager-polygon';
    return 'unsupported';
  }
  if (nodeType === 'TemplateMatching') return 'template-matching-roi';
  if (nodeType === 'BoxFilter') return 'box-filter-region';
  if (nodeType === 'PolarUnwrap') return shape === 'arc' ? 'polar-arc' : 'polar-annulus';
  if (nodeType === 'CircleMeasurement' && shape === 'circlesearchv2') return 'circle-search-v2';
  if (nodeType === 'NPointCalibration' && config.supported === true) return 'npoint-sequence';
  if (nodeType === 'CaliperTool') return 'caliper-search-region';
  return 'unsupported';
}

function geometryKind(kind: Exclude<RoiEditorDescriptorKind, 'unsupported'>): RoiGeometryKind {
  switch (kind) {
    case 'roi-manager-circle': return 'circle';
    case 'roi-manager-polygon': return 'polygon';
    case 'polar-annulus': return 'annulus';
    case 'polar-arc': return 'arc';
    case 'circle-search-v2': return 'circle-search-v2';
    case 'npoint-sequence': return 'point-sequence';
    default: return 'rectangle';
  }
}

function descriptorMessage(config: CanonicalRoiConfig, fallback: string): string {
  return text(config.editable === false ? config.readonlyMessage : config.subtitle) ||
    text(config.readonlyMessage) || fallback;
}

function unsupportedDescriptor(
  nodeId: string,
  nodeType: string,
  config: CanonicalRoiConfig,
  reason?: string
): RoiUnsupportedEditorDescriptor {
  return Object.freeze({
    descriptorId: `${nodeId}:unsupported`,
    nodeId,
    nodeType,
    kind: 'unsupported',
    commandKind: 'unsupported',
    geometryKind: null,
    supported: false,
    editable: false,
    parameterNames: Object.freeze([]),
    message: reason || descriptorMessage(config, `节点 ${nodeType || 'Unknown'} 不支持图像几何编辑。`),
    canonicalConfig: Object.freeze({ ...config })
  });
}

export function resolveRoiEditorDescriptor(
  node: RoiSelectedNodeDraft,
  startupFlags: RoiStartupFlags = Object.freeze({})
): RoiEditorDescriptor {
  const identity = nodeIdentity(node);
  if (!identity.id || !identity.type) {
    return unsupportedDescriptor(identity.id, identity.type, Object.freeze({}), '选中节点缺少稳定 id/type。');
  }

  const config = getOperatorRoiConfig(normalizedNode(node), canonicalOptions(startupFlags));
  const kind = descriptorKind(identity.type, config);
  if (config.supported !== true || kind === 'unsupported') {
    return unsupportedDescriptor(identity.id, identity.type, config);
  }

  const base = {
    descriptorId: `${identity.id}:${kind}`,
    nodeId: identity.id,
    nodeType: identity.type,
    kind,
    geometryKind: geometryKind(kind),
    supported: true as const,
    editable: config.editable === true,
    parameterNames: descriptorParameters[kind],
    message: descriptorMessage(config, '图像几何编辑器已就绪。'),
    canonicalConfig: Object.freeze({ ...config })
  };

  if (kind === 'caliper-search-region') {
    return Object.freeze({
      ...base,
      kind,
      commandKind: 'caliper-structural',
      geometryKind: 'rectangle',
      sourceOperatorType: 'RectangleRegion',
      sourceOutputPortName: 'Rectangle',
      targetInputPortName: 'SearchRegion'
    });
  }

  return Object.freeze({
    ...base,
    kind,
    commandKind: 'parameter-patch'
  }) as RoiParameterEditorDescriptor;
}

export function decodeRoiGeometry(
  node: RoiSelectedNodeDraft,
  descriptor: RoiEditorDescriptor,
  bounds: RoiImageBounds | null = null
): RoiGeometry | null {
  const identity = nodeIdentity(node);
  if (!descriptor.supported || identity.id !== descriptor.nodeId || identity.type !== descriptor.nodeType) {
    return null;
  }
  return geometryFromParams(
    parameterValues(node),
    descriptor.canonicalConfig as CanonicalRoiConfig,
    bounds
  ) as RoiGeometry | null;
}

function parameterValue(value: unknown): RoiParameterValue | undefined {
  return value === null || typeof value === 'string' || typeof value === 'boolean' ||
    (typeof value === 'number' && Number.isFinite(value))
    ? value
    : undefined;
}

function encodedValues(
  descriptor: RoiParameterEditorDescriptor | RoiCaliperEditorDescriptor,
  geometry: RoiGeometry
): Readonly<Record<string, RoiParameterValue>> {
  const config = descriptor.canonicalConfig as CanonicalRoiConfig;
  const encoded = {
    ...(config.commitValues ?? {}),
    ...geometryToParams(geometry, config)
  };
  const values: Record<string, RoiParameterValue> = {};
  for (const name of descriptor.parameterNames) {
    const value = parameterValue(encoded[name]);
    if (value !== undefined) values[name] = value;
  }
  return Object.freeze(values);
}

export function createRoiCommitPayload(
  descriptor: RoiEditorDescriptor,
  geometry: RoiGeometry
): RoiCommitPayload {
  if (!descriptor.supported) {
    return Object.freeze({
      kind: 'unsupported',
      nodeId: descriptor.nodeId,
      descriptorId: descriptor.descriptorId,
      reason: descriptor.message
    });
  }

  const values = encodedValues(descriptor, geometry);
  const missingNames = descriptor.parameterNames.filter(name =>
    !Object.prototype.hasOwnProperty.call(values, name));
  if (missingNames.length > 0) {
    return Object.freeze({
      kind: 'unsupported',
      nodeId: descriptor.nodeId,
      descriptorId: descriptor.descriptorId,
      reason: `ROI geometry codec did not produce: ${missingNames.join(', ')}.`
    });
  }
  if (descriptor.commandKind === 'caliper-structural') {
    const number = (name: 'X' | 'Y' | 'Width' | 'Height'): number => values[name] as number;
    return Object.freeze({
      kind: 'caliper-structural',
      caliperNodeId: descriptor.nodeId,
      descriptorId: descriptor.descriptorId,
      sourceOperatorType: descriptor.sourceOperatorType,
      sourceOutputPortName: descriptor.sourceOutputPortName,
      targetInputPortName: descriptor.targetInputPortName,
      regionParameters: Object.freeze({
        X: number('X'),
        Y: number('Y'),
        Width: number('Width'),
        Height: number('Height')
      })
    });
  }

  return Object.freeze({
    kind: 'parameter-patch',
    nodeId: descriptor.nodeId,
    descriptorId: descriptor.descriptorId,
    values
  });
}

function requiredIdentityText(value: string, name: string): string {
  const normalized = value.trim();
  if (!normalized) throw new Error(`ROI session ${name} must be non-empty.`);
  return normalized;
}

function revision(value: number, name: string): number {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new Error(`ROI session ${name} must be a non-negative safe integer.`);
  }
  return value;
}

export function createRoiSessionIdentity(input: RoiSessionIdentityInput): RoiSessionIdentity {
  const identity = {
    projectId: requiredIdentityText(input.projectId, 'projectId'),
    nodeId: requiredIdentityText(input.nodeId, 'nodeId'),
    selectionRevision: revision(input.selectionRevision, 'selectionRevision'),
    flowRevision: revision(input.flowRevision, 'flowRevision'),
    previewRequestKey: input.previewRequestKey === null ? null : requiredIdentityText(
      input.previewRequestKey,
      'previewRequestKey'
    ),
    imageGeneration: revision(input.imageGeneration, 'imageGeneration')
  };
  return Object.freeze({
    ...identity,
    key: JSON.stringify([
      identity.projectId,
      identity.nodeId,
      identity.selectionRevision,
      identity.flowRevision,
      identity.previewRequestKey,
      identity.imageGeneration
    ])
  });
}

export function isSameRoiSessionIdentity(
  left: RoiSessionIdentity | null | undefined,
  right: RoiSessionIdentity | null | undefined
): boolean {
  return Boolean(left && right && left.key === right.key);
}
