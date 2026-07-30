import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import type {
  OperatorCatalogItem,
  OperatorParameter,
  OperatorPort
} from '@/capabilities/operators-read/operatorContracts';
import type { CanonicalFlowCommandResult, FlowMutationGate } from '@/platform/canvas';
import type {
  FlowCanvasOwner,
  FlowNodeParameterPatch,
  FlowNodePropertiesPatch
} from '../flow';
import type {
  WorkspaceJsonValue,
  WorkspaceOperatorV1,
  WorkspaceCanvasProjectV1
} from '../workspaceContracts';
import type {
  WorkspaceInspectorDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner
} from '../workspaceLifecycleDiagnostics';
import {
  decodeInspectorOutputAvailabilityRules,
  decodeInspectorParameterConstraints,
  InspectorMetadataDecodeError,
  type InspectorParameterConstraint,
  type InspectorOutputAvailabilityRule
} from './parameterContracts';
import {
  resolveInspectorParameterEditor,
  type InspectorParameterEditorKind
} from './parameterEditorRegistry';
import {
  resolveInspectorConstraintStates,
  resolveInspectorOutputAvailability,
  validateInspectorParameterPatch,
  type InspectorParameterConstraintState,
  type InspectorParameterValidationDescriptor,
  type InspectorValidationError
} from './parameterValidation';

export type InspectorMode = 'empty' | 'node' | 'multi-node' | 'connection';
export type InspectorMetadataPhase = 'idle' | 'loading' | 'ready' | 'missing' | 'error';
export type InspectorValueSource = 'explicit' | 'metadata-default' | 'undefined';

export interface InspectorPortProjection {
  readonly id: string;
  readonly name: string;
  readonly displayName: string;
  readonly description: string | null;
  readonly dataType: string;
  readonly required: boolean;
  readonly connected: boolean;
  readonly available: boolean;
  readonly availabilityReasonCode: string;
}

export interface InspectorParameterProjection extends InspectorParameterValidationDescriptor {
  readonly id: string | null;
  readonly description: string | null;
  readonly valueSource: InspectorValueSource;
  readonly editorKind: InspectorParameterEditorKind;
  readonly extensionSlot: 'file-picker' | 'camera-binding' | 'image-backed' | null;
  readonly extensionMessage: string | null;
  readonly persisted: boolean;
  readonly visible: boolean;
  readonly disabledByConstraint: boolean;
  readonly ignored: boolean;
  readonly deprecated: boolean;
  readonly reasonCode: string | null;
  readonly definition: OperatorParameter | null;
  readonly errors: readonly InspectorValidationError[];
}

export interface InspectorNodeProjection {
  readonly id: string;
  readonly name: string;
  readonly type: string;
  readonly enabled: boolean;
  readonly description: string | null;
  readonly executionStatus: string;
  readonly executionTimeMs: number | null;
  readonly errorMessage: string | null;
  readonly inputPorts: readonly InspectorPortProjection[];
  readonly outputPorts: readonly InspectorPortProjection[];
  readonly parameters: readonly InspectorParameterProjection[];
  readonly metadataPhase: InspectorMetadataPhase;
  readonly metadataMessage: string | null;
}

export interface InspectorMultiNodeItem {
  readonly id: string;
  readonly name: string;
  readonly type: string;
  readonly enabled: boolean;
}

export interface InspectorConnectionEndpoint {
  readonly nodeId: string;
  readonly nodeName: string;
  readonly portId: string;
  readonly portName: string;
  readonly dataType: string;
}

export interface InspectorConnectionProjection {
  readonly id: string;
  readonly source: InspectorConnectionEndpoint;
  readonly target: InspectorConnectionEndpoint;
}

export interface InspectorOwnerProjection {
  readonly phase: 'active' | 'disposed';
  readonly mode: InspectorMode;
  readonly projectId: string | null;
  readonly flowRevision: number;
  readonly selectionRevision: number;
  readonly mutationGate: FlowMutationGate;
  readonly node: InspectorNodeProjection | null;
  readonly nodes: readonly InspectorMultiNodeItem[];
  readonly connection: InspectorConnectionProjection | null;
  readonly validationErrors: readonly InspectorValidationError[];
  readonly activeDraftCount: number;
}

type MutableInspectorOwnerProjection = {
  -readonly [Key in keyof InspectorOwnerProjection]: InspectorOwnerProjection[Key]
};

export interface InspectorMutationResult extends CanonicalFlowCommandResult {
  readonly validationErrors: readonly InspectorValidationError[];
}

export interface InspectorOwner {
  readonly projectId: string | null;
  readonly projection: DeepReadonly<InspectorOwnerProjection>;
  patchNodeParameter(parameterName: string, value: unknown): InspectorMutationResult;
  patchNodeProperties(patch: Readonly<{ name?: string; isEnabled?: boolean }>): InspectorMutationResult;
  commitImageBacked(command: InspectorImageBackedCommit): InspectorMutationResult;
  disconnectConnection(): InspectorMutationResult;
  selectNode(nodeId: string): CanonicalFlowCommandResult;
  setDraftActive(key: string, active: boolean): void;
  dispose(reason?: string): void;
}

export interface InspectorImageBackedCommit {
  readonly nodeId: string;
  readonly selectionRevision: number;
  readonly flowRevision: number;
  readonly mode: 'parameters' | 'caliper-search-region';
  readonly values: Readonly<Record<string, WorkspaceJsonValue>>;
}

function record(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function array(value: unknown): readonly Readonly<Record<string, unknown>>[] {
  return Array.isArray(value) ? value.map(record) : Object.freeze([]);
}

function text(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : value === null || value === undefined ? fallback : String(value);
}

function hasOwn(source: Readonly<Record<string, unknown>>, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(source, key);
}

function field(source: Readonly<Record<string, unknown>>, camel: string): unknown {
  const pascal = `${camel.slice(0, 1).toUpperCase()}${camel.slice(1)}`;
  return hasOwn(source, camel) ? source[camel] : source[pascal];
}

function recordsById(records: readonly Readonly<Record<string, unknown>>[]): ReadonlyMap<string, Readonly<Record<string, unknown>>> {
  return new Map(records.map(item => [text(field(item, 'id')), item]));
}

function normalized(value: string): string {
  return value.trim().toLowerCase();
}

function metadataForType(
  catalog: readonly OperatorCatalogItem[],
  type: string
): OperatorCatalogItem | null {
  const identity = normalized(type);
  return catalog.find(item => normalized(item.operatorType) === identity) ?? null;
}

function persistedOperator(project: WorkspaceCanvasProjectV1, nodeId: string): WorkspaceOperatorV1 | null {
  return project.flow?.operators.find(item => item.id === nodeId) ?? null;
}

function selectedNodeIds(flowOwner: FlowCanvasOwner): readonly string[] {
  const runtime = flowOwner.projection.runtime;
  if (!runtime) return Object.freeze([]);
  const ids = [...runtime.selectedNodeIds];
  if (runtime.selectedNodeId && !ids.includes(runtime.selectedNodeId)) ids.push(runtime.selectedNodeId);
  return Object.freeze(ids);
}

function parameterValue(
  persisted: Readonly<Record<string, unknown>> | undefined,
  metadata: OperatorParameter | undefined
): Readonly<{
  explicitValuePresent: boolean;
  value: unknown;
  defaultValue: unknown;
  source: InspectorValueSource;
}> {
  const explicitValuePresent = persisted !== undefined && (hasOwn(persisted, 'value') || hasOwn(persisted, 'Value'));
  const explicit = persisted ? field(persisted, 'value') : undefined;
  const defaultValue = metadata
    ? metadata.defaultValue
    : persisted ? field(persisted, 'defaultValue') : undefined;
  if (explicitValuePresent) {
    return Object.freeze({ explicitValuePresent: true, value: explicit, defaultValue, source: 'explicit' });
  }
  if (defaultValue !== undefined) {
    return Object.freeze({ explicitValuePresent: false, value: defaultValue, defaultValue, source: 'metadata-default' });
  }
  return Object.freeze({ explicitValuePresent: false, value: undefined, defaultValue, source: 'undefined' });
}

function buildParameters(
  node: Readonly<Record<string, unknown>>,
  metadata: OperatorCatalogItem | null,
  constraints: readonly InspectorParameterConstraint[],
  errors: ReadonlyMap<string, readonly InspectorValidationError[]>
): readonly InspectorParameterProjection[] {
  const persisted = array(field(node, 'parameters'));
  const persistedByName = new Map(persisted.map(item => [normalized(text(field(item, 'name'))), item]));
  const metadataByName = new Map((metadata?.parameters ?? []).map(item => [normalized(item.name), item]));
  const names = [
    ...metadataByName.keys(),
    ...[...persistedByName.keys()].filter(name => !metadataByName.has(name))
  ];
  const base = names.map(name => {
    const parameter = metadataByName.get(name);
    const stored = persistedByName.get(name);
    const values = parameterValue(stored, parameter);
    const raw = Object.freeze({ ...(stored ?? {}) });
    const editor = resolveInspectorParameterEditor({
      dataType: parameter?.dataType ?? text(field(raw, 'dataType'), 'unknown'),
      options: parameter?.options ?? (Array.isArray(field(raw, 'options'))
        ? array(field(raw, 'options')).map(option => Object.freeze({
            label: text(field(option, 'label')),
            value: text(field(option, 'value'))
          }))
        : null),
      minValue: parameter?.minValue ?? field(raw, 'minValue'),
      maxValue: parameter?.maxValue ?? field(raw, 'maxValue'),
      value: values.value,
      raw
    });
    return Object.freeze({
      id: stored ? text(field(stored, 'id')) || null : null,
      name: parameter?.name ?? text(field(raw, 'name')),
      label: parameter?.displayName || text(field(raw, 'displayName')) || parameter?.name || text(field(raw, 'name')),
      description: parameter?.description ?? (field(raw, 'description') === null ? null : text(field(raw, 'description')) || null),
      dataType: parameter?.dataType ?? text(field(raw, 'dataType'), 'unknown'),
      isRequired: parameter?.isRequired ?? field(raw, 'isRequired') === true,
      nullable: editor.nullable,
      integer: editor.integer,
      options: parameter?.options ?? (Array.isArray(field(raw, 'options'))
        ? array(field(raw, 'options')).map(option => Object.freeze({ label: text(field(option, 'label')), value: text(field(option, 'value')) }))
        : null),
      minValue: parameter?.minValue ?? field(raw, 'minValue'),
      maxValue: parameter?.maxValue ?? field(raw, 'maxValue'),
      explicitValuePresent: values.explicitValuePresent,
      value: values.value,
      defaultValue: values.defaultValue,
      valueSource: values.source,
      editorKind: editor.kind,
      extensionSlot: editor.extensionSlot,
      extensionMessage: editor.message,
      persisted: stored !== undefined,
      visible: true,
      disabledByConstraint: false,
      ignored: false,
      deprecated: false,
      reasonCode: null,
      definition: parameter ?? null,
      errors: errors.get(normalized(parameter?.name ?? text(field(raw, 'name')))) ?? Object.freeze([])
    });
  });
  const states = resolveInspectorConstraintStates(base, constraints);
  return Object.freeze(base.map(parameter => {
    const state: InspectorParameterConstraintState | undefined = states.get(normalized(parameter.name));
    return Object.freeze({
      ...parameter,
      isRequired: state?.effectiveRequired ?? parameter.isRequired,
      visible: state?.effectiveVisible ?? true,
      disabledByConstraint: state?.effectiveDisabled ?? false,
      ignored: state?.effectiveIgnored ?? false,
      deprecated: state?.constraint?.deprecated ?? false,
      reasonCode: state?.constraint?.reasonCode ?? null
    });
  }));
}

function connectedPortIds(
  connections: readonly Readonly<Record<string, unknown>>[],
  nodeId: string,
  direction: 'input' | 'output'
): ReadonlySet<string> {
  return new Set(connections
    .filter(connection => text(field(connection, direction === 'input' ? 'targetOperatorId' : 'sourceOperatorId')) === nodeId)
    .map(connection => text(field(connection, direction === 'input' ? 'targetPortId' : 'sourcePortId'))));
}

function buildPorts(
  node: Readonly<Record<string, unknown>>,
  direction: 'input' | 'output',
  metadataPorts: readonly OperatorPort[],
  connections: readonly Readonly<Record<string, unknown>>[],
  parameters: readonly InspectorParameterProjection[],
  constraints: readonly InspectorParameterConstraint[],
  outputRules: readonly InspectorOutputAvailabilityRule[]
): readonly InspectorPortProjection[] {
  const nodeId = text(field(node, 'id'));
  const ports = array(field(node, direction === 'input' ? 'inputPorts' : 'outputPorts'));
  const metadataByName = new Map(metadataPorts.map(item => [normalized(item.name), item]));
  const connected = connectedPortIds(connections, nodeId, direction);
  return Object.freeze(ports.map(port => {
    const name = text(field(port, 'name'));
    const metadata = metadataByName.get(normalized(name));
    const availability = direction === 'output'
      ? resolveInspectorOutputAvailability(name, outputRules, parameters, constraints)
      : Object.freeze({ available: true, reasonCode: 'INPUT_PORT' });
    const id = text(field(port, 'id'));
    return Object.freeze({
      id,
      name,
      displayName: metadata?.displayName || name,
      description: metadata?.description ?? null,
      dataType: metadata?.dataType ?? text(field(port, 'dataType'), 'Any'),
      required: metadata?.isRequired ?? field(port, 'isRequired') === true,
      connected: connected.has(id),
      available: availability.available,
      availabilityReasonCode: availability.reasonCode
    });
  }));
}

function metadataState(
  flowOwner: FlowCanvasOwner,
  type: string
): Readonly<{
  phase: InspectorMetadataPhase;
  message: string | null;
  metadata: OperatorCatalogItem | null;
  constraints: readonly InspectorParameterConstraint[];
  outputRules: readonly InspectorOutputAvailabilityRule[];
}> {
  const catalog = flowOwner.projection.catalog;
  const metadata = metadataForType(catalog.operators, type);
  if (metadata) {
    try {
      return Object.freeze({
        phase: 'ready',
        message: null,
        metadata,
        constraints: decodeInspectorParameterConstraints(metadata.parameterConstraints),
        outputRules: decodeInspectorOutputAvailabilityRules(metadata.outputAvailabilityRules)
      });
    } catch (error) {
      return Object.freeze({
        phase: 'error',
        message: error instanceof InspectorMetadataDecodeError
          ? '参数定义格式无效，当前节点暂不可编辑。请刷新算子目录或联系维护人员。'
          : '参数定义读取失败，当前节点暂不可编辑。请刷新算子目录。',
        metadata,
        constraints: Object.freeze([]),
        outputRules: Object.freeze([])
      });
    }
  }
  if (catalog.phase === 'idle' || catalog.phase === 'loading') {
    return Object.freeze({ phase: 'loading', message: catalog.message, metadata: null, constraints: Object.freeze([]), outputRules: Object.freeze([]) });
  }
  if (catalog.phase === 'success' || catalog.phase === 'empty') {
    return Object.freeze({ phase: 'missing', message: `未找到类型 ${type} 的参数定义，当前节点参数不可编辑。请刷新算子目录。`, metadata: null, constraints: Object.freeze([]), outputRules: Object.freeze([]) });
  }
  return Object.freeze({ phase: 'error', message: catalog.message ?? '参数定义不可用，当前节点参数不可编辑。请刷新算子目录。', metadata: null, constraints: Object.freeze([]), outputRules: Object.freeze([]) });
}

function buildNode(
  project: WorkspaceCanvasProjectV1,
  flowOwner: FlowCanvasOwner,
  node: Readonly<Record<string, unknown>>,
  errors: ReadonlyMap<string, readonly InspectorValidationError[]>
): InspectorNodeProjection {
  const id = text(field(node, 'id'));
  const type = text(field(node, 'type'), 'Unknown');
  const metadata = metadataState(flowOwner, type);
  const parameters = buildParameters(node, metadata.metadata, metadata.constraints, errors);
  const connections = flowOwner.projection.draft.connections;
  const baseline = persistedOperator(project, id);
  const rawDescription = metadata.metadata?.description ?? field(record(field(node, 'metadata')), 'description');
  return Object.freeze({
    id,
    name: text(field(node, 'name'), type),
    type,
    enabled: field(node, 'isEnabled') !== false,
    description: typeof rawDescription === 'string' && rawDescription.length > 0 ? rawDescription : null,
    executionStatus: baseline?.executionStatus.value ?? 'NotExecuted',
    executionTimeMs: baseline?.executionTimeMs ?? null,
    errorMessage: baseline?.errorMessage ?? null,
    inputPorts: buildPorts(
      node,
      'input',
      metadata.metadata?.inputPorts ?? Object.freeze([]),
      connections,
      parameters,
      metadata.constraints,
      metadata.outputRules
    ),
    outputPorts: buildPorts(
      node,
      'output',
      metadata.metadata?.outputPorts ?? Object.freeze([]),
      connections,
      parameters,
      metadata.constraints,
      metadata.outputRules
    ),
    parameters,
    metadataPhase: metadata.phase,
    metadataMessage: metadata.message
  });
}

function endpoint(
  nodes: ReadonlyMap<string, Readonly<Record<string, unknown>>>,
  nodeId: string,
  portId: string,
  direction: 'input' | 'output'
): InspectorConnectionEndpoint {
  const node = nodes.get(nodeId) ?? Object.freeze({});
  const port = array(field(node, direction === 'input' ? 'inputPorts' : 'outputPorts'))
    .find(item => text(field(item, 'id')) === portId) ?? Object.freeze({});
  return Object.freeze({
    nodeId,
    nodeName: text(field(node, 'name'), nodeId),
    portId,
    portName: text(field(port, 'name'), portId),
    dataType: text(field(port, 'dataType'), 'Any')
  });
}

function commandResult(
  result: CanonicalFlowCommandResult,
  validationErrors: readonly InspectorValidationError[] = Object.freeze([])
): InspectorMutationResult {
  return Object.freeze({ ...result, validationErrors });
}

export function createInspectorOwner(options: {
  readonly project: WorkspaceCanvasProjectV1;
  readonly flowOwner: FlowCanvasOwner;
  readonly diagnostics: WorkspaceLifecycleDiagnosticsOwner;
  readonly diagnosticsKey?: string;
}): InspectorOwner {
  const diagnosticsKey = options.project.id ?? options.diagnosticsKey;
  if (!diagnosticsKey?.trim()) throw new Error('An unsaved Inspector owner requires a diagnostics key.');
  const lease: WorkspaceInspectorDiagnosticsLease = options.diagnostics.reserveInspector(diagnosticsKey);
  const state = reactive<MutableInspectorOwnerProjection>({
    phase: 'active',
    mode: 'empty',
    projectId: options.project.id,
    flowRevision: 0,
    selectionRevision: 0,
    mutationGate: options.flowOwner.projection.mutationGate,
    node: null,
    nodes: Object.freeze([]),
    connection: null,
    validationErrors: Object.freeze([]),
    activeDraftCount: 0
  });
  const validationByParameter = new Map<string, readonly InspectorValidationError[]>();
  const activeDrafts = new Set<string>();
  let selectionIdentity = 'empty';
  let disposed = false;

  function sync(): void {
    if (disposed) return;
    const runtime = options.flowOwner.projection.runtime;
    const draft = options.flowOwner.projection.draft;
    const nodes = recordsById(draft.operators);
    const ids = selectedNodeIds(options.flowOwner);
    const connectionId = runtime?.selectedConnectionId ?? null;
    const nextIdentity = connectionId && ids.length === 0
      ? `connection:${connectionId}`
      : ids.length > 1 ? `multi:${ids.join('|')}` : ids.length === 1 ? `node:${ids[0]}` : 'empty';
    if (selectionIdentity !== nextIdentity) {
      selectionIdentity = nextIdentity;
      validationByParameter.clear();
      activeDrafts.clear();
      lease.updateDraftCount(0);
    }
    state.flowRevision = runtime?.flowRevision ?? 0;
    state.selectionRevision = runtime?.selectionRevision ?? 0;
    state.mutationGate = options.flowOwner.projection.mutationGate;
    state.activeDraftCount = activeDrafts.size;
    state.validationErrors = Object.freeze([...validationByParameter.values()].flat());
    state.node = null;
    state.nodes = Object.freeze([]);
    state.connection = null;

    if (connectionId && ids.length === 0) {
      const connection = draft.connections.find(item => text(field(item, 'id')) === connectionId);
      if (connection) {
        const sourceNodeId = text(field(connection, 'sourceOperatorId'));
        const sourcePortId = text(field(connection, 'sourcePortId'));
        const targetNodeId = text(field(connection, 'targetOperatorId'));
        const targetPortId = text(field(connection, 'targetPortId'));
        state.mode = 'connection';
        state.connection = Object.freeze({
          id: connectionId,
          source: endpoint(nodes, sourceNodeId, sourcePortId, 'output'),
          target: endpoint(nodes, targetNodeId, targetPortId, 'input')
        });
        return;
      }
    }
    if (ids.length > 1) {
      state.mode = 'multi-node';
      state.nodes = Object.freeze(ids.map(id => {
        const node = nodes.get(id) ?? Object.freeze({});
        return Object.freeze({
          id,
          name: text(field(node, 'name'), id),
          type: text(field(node, 'type'), 'Unknown'),
          enabled: field(node, 'isEnabled') !== false
        });
      }));
      return;
    }
    if (ids.length === 1) {
      const node = nodes.get(ids[0]!);
      if (node) {
        state.mode = 'node';
        state.node = buildNode(options.project, options.flowOwner, node, validationByParameter);
        return;
      }
    }
    state.mode = 'empty';
  }

  const stop = watch(
    () => [
      options.flowOwner.projection.draft,
      options.flowOwner.projection.runtime?.selectionRevision,
      options.flowOwner.projection.runtime?.flowRevision,
      options.flowOwner.projection.mutationGate,
      options.flowOwner.projection.catalog.phase,
      options.flowOwner.projection.catalog.operators,
      options.flowOwner.projection.catalog.message
    ],
    sync,
    { immediate: true }
  );

  function reject(code: string, message: string, errors: readonly InspectorValidationError[] = Object.freeze([])): InspectorMutationResult {
    return Object.freeze({
      ok: false,
      code,
      message,
      flowRevision: options.flowOwner.projection.runtime?.flowRevision ?? 0,
      validationErrors: errors
    });
  }

  return Object.freeze({
    projectId: options.project.id,
    projection: readonly(state),
    patchNodeParameter(parameterName: string, value: unknown): InspectorMutationResult {
      if (disposed) return reject('disposed', '属性检查器已关闭，请重新进入工程工作台。');
      const node = state.node;
      if (state.mode !== 'node' || !node) return reject('selection-mismatch', '当前未选择单个节点。');
      if (state.mutationGate !== 'editable') return reject(state.mutationGate, '当前状态禁止修改参数。');
      if (node.metadataPhase !== 'ready') return reject('metadata-unavailable', node.metadataMessage ?? '参数定义不可用。');
      const parameter = node.parameters.find(item => normalized(item.name) === normalized(parameterName));
      if (!parameter) return reject('parameter-not-found', '参数合同不存在。');
      if (!parameter.persisted) return reject('parameter-not-persisted', '该参数不在当前流程数据中，不能直接创建。请刷新流程或重新添加节点。');
      if (parameter.editorKind === 'unsupported' || parameter.editorKind === 'extension') {
        return reject('editor-unavailable', parameter.extensionMessage ?? '参数编辑器不可用。');
      }
      const satisfiedInputs = new Set(node.inputPorts.filter(port => port.connected).map(port => port.name));
      const errors = validateInspectorParameterPatch(
        node.parameters,
        decodeInspectorParameterConstraints(
          metadataForType(options.flowOwner.projection.catalog.operators, node.type)?.parameterConstraints ?? Object.freeze([])
        ),
        parameter.name,
        value,
        satisfiedInputs
      );
      if (errors.length > 0) {
        validationByParameter.set(normalized(parameter.name), errors);
        sync();
        return reject('validation-failed', errors[0]?.message ?? '参数校验失败。', errors);
      }
      validationByParameter.delete(normalized(parameter.name));
      const patch: FlowNodeParameterPatch = parameter.definition
        ? Object.freeze({
            nodeId: node.id,
            parameterName: parameter.name,
            value: value as WorkspaceJsonValue,
            definition: parameter.definition
          })
        : Object.freeze({
            nodeId: node.id,
            parameterName: parameter.name,
            value: value as WorkspaceJsonValue
          });
      const result = options.flowOwner.commands.patchNodeParameter(patch);
      sync();
      return commandResult(result);
    },
    patchNodeProperties(patch: Readonly<{ name?: string; isEnabled?: boolean }>): InspectorMutationResult {
      if (disposed) return reject('disposed', '属性检查器已关闭，请重新进入工程工作台。');
      const node = state.node;
      if (state.mode !== 'node' || !node) return reject('selection-mismatch', '当前未选择单个节点。');
      if (state.mutationGate !== 'editable') return reject(state.mutationGate, '当前状态禁止修改节点属性。');
      const command: { nodeId: string; name?: string; isEnabled?: boolean } = { nodeId: node.id };
      if (Object.prototype.hasOwnProperty.call(patch, 'name')) {
        const name = patch.name?.trim() ?? '';
        if (!name) return reject('validation-failed', '节点名称不能为空。');
        command.name = name;
      }
      if (Object.prototype.hasOwnProperty.call(patch, 'isEnabled')) {
        if (typeof patch.isEnabled !== 'boolean') return reject('validation-failed', '节点启用状态必须是布尔值。');
        command.isEnabled = patch.isEnabled;
      }
      const result = options.flowOwner.commands.patchNodeProperties(command as FlowNodePropertiesPatch);
      sync();
      return commandResult(result);
    },
    commitImageBacked(command: InspectorImageBackedCommit): InspectorMutationResult {
      if (disposed) return reject('disposed', '属性检查器已关闭，请重新进入工程工作台。');
      const node = state.node;
      if (state.mode !== 'node' || !node || node.id !== command.nodeId) {
        return reject('selection-mismatch', 'ROI 编辑会话已因节点切换失效。');
      }
      if (state.selectionRevision !== command.selectionRevision || state.flowRevision !== command.flowRevision) {
        return reject('stale-editor-session', '流程或选择已变化，请重新打开图像编辑器。');
      }
      if (state.mutationGate !== 'editable') {
        return reject(state.mutationGate, '当前状态禁止提交图像参数。');
      }
      if (Object.keys(command.values).length === 0) {
        return reject('empty-image-backed-patch', '图像参数提交为空。');
      }
      if (command.mode === 'caliper-search-region') {
        const result = options.flowOwner.commands.upsertCaliperSearchRegion({
          caliperNodeId: node.id,
          values: command.values
        });
        sync();
        return commandResult(result);
      }

      const definitions: OperatorParameter[] = [];
      for (const name of Object.keys(command.values)) {
        const parameter = node.parameters.find(item => normalized(item.name) === normalized(name));
        if (!parameter || !parameter.persisted) {
          return reject('parameter-not-persisted', `图像参数 ${name} 不在当前流程数据中，无法应用本次修改。`);
        }
        if (parameter.definition) definitions.push(parameter.definition);
      }
      const result = options.flowOwner.commands.patchNodeParameters({
        nodeId: node.id,
        values: command.values,
        definitions: Object.freeze(definitions)
      });
      sync();
      return commandResult(result);
    },
    disconnectConnection(): InspectorMutationResult {
      if (disposed) return reject('disposed', '属性检查器已关闭，请重新进入工程工作台。');
      if (state.mode !== 'connection' || !state.connection) return reject('selection-mismatch', '当前未选择连接。');
      if (state.mutationGate !== 'editable') return reject(state.mutationGate, '当前状态禁止断开连接。');
      const result = options.flowOwner.commands.disconnect(state.connection.id);
      sync();
      return commandResult(result);
    },
    selectNode(nodeId: string): CanonicalFlowCommandResult {
      if (disposed) return Object.freeze({ ok: false, code: 'disposed', message: '属性检查器已关闭，请重新进入工程工作台。', flowRevision: state.flowRevision });
      return options.flowOwner.commands.selectNode(nodeId);
    },
    setDraftActive(key: string, active: boolean): void {
      if (disposed || !key) return;
      if (active) activeDrafts.add(key);
      else activeDrafts.delete(key);
      state.activeDraftCount = activeDrafts.size;
      lease.updateDraftCount(activeDrafts.size);
    },
    dispose(reason = 'inspector-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      stop();
      validationByParameter.clear();
      activeDrafts.clear();
      state.activeDraftCount = 0;
      state.validationErrors = Object.freeze([]);
      state.node = null;
      state.nodes = Object.freeze([]);
      state.connection = null;
      state.mode = 'empty';
      state.phase = 'disposed';
      lease.updateDraftCount(0);
      lease.dispose(reason);
    }
  });
}
