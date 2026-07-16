import {
  createHostedFlowCanvasAdapter,
  type FlowCanvasAdapter
} from '@clearvision/canonical-flow-canvas';
import { FlowEditorInteraction } from '@clearvision/canonical-flow-interaction';

export type FlowMutationGate = 'editable' | 'readonly' | 'running';
export type FlowFeedbackTone = 'info' | 'success' | 'warning' | 'error';

export interface CanonicalFlowFeedback {
  readonly sequence: number;
  readonly code: string;
  readonly message: string;
  readonly tone: FlowFeedbackTone;
  readonly details: unknown;
}

export interface CanonicalOperatorPortDefinition {
  readonly name: string;
  readonly displayName?: string;
  readonly description?: string | null;
  readonly dataType: string;
  readonly isRequired: boolean;
}

export interface CanonicalOperatorParameterDefinition {
  readonly name: string;
  readonly displayName: string;
  readonly description: string | null;
  readonly dataType: string;
  readonly defaultValue: unknown;
  readonly minValue: unknown;
  readonly maxValue: unknown;
  readonly isRequired: boolean;
  readonly options: readonly unknown[] | null;
}

export interface CanonicalOperatorDefinition {
  readonly operatorType: string;
  readonly displayName: string;
  readonly category: string;
  readonly iconName: string | null;
  readonly inputPorts: readonly CanonicalOperatorPortDefinition[];
  readonly outputPorts: readonly CanonicalOperatorPortDefinition[];
  readonly parameters: readonly CanonicalOperatorParameterDefinition[];
}

export interface CanonicalFlowDraft {
  readonly id: string | null;
  readonly name: string;
  readonly operators: readonly Readonly<Record<string, unknown>>[];
  readonly connections: readonly Readonly<Record<string, unknown>>[];
  readonly decisionConfiguration: unknown;
  readonly opaquePassthrough: Readonly<Record<string, unknown>>;
}

export interface CanonicalPortPoint {
  readonly id: string;
  readonly name: string;
  readonly dataType: string;
  readonly x: number;
  readonly y: number;
  readonly isOutput: boolean;
}

export interface CanonicalNodeGeometry {
  readonly id: string;
  readonly type: string;
  readonly title: string;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  readonly disabled: boolean;
  readonly inputs: readonly CanonicalPortPoint[];
  readonly outputs: readonly CanonicalPortPoint[];
}

export interface CanonicalCanvasResourceDiagnostics {
  readonly adapterDisposed: boolean;
  readonly canvasDestroyed: boolean;
  readonly interactionDisposed: boolean;
  readonly resizeObserverActive: boolean;
  readonly themeObserverActive: boolean;
  readonly drawFramePending: boolean;
  readonly resizeFramePending: boolean;
  readonly interactionFramePending: boolean;
  readonly contextMenuTimerActive: boolean;
  readonly structureListenerCount: number;
  readonly viewListenerCount: number;
  readonly selectionListenerCount: number;
  readonly interactionCleanupCount: number;
  readonly facadeListenerCount: number;
}

export interface CanonicalCanvasRuntimeSnapshot {
  readonly nodeCount: number;
  readonly connectionCount: number;
  readonly flowRevision: number;
  readonly selectionRevision: number;
  readonly selectedNodeId: string | null;
  readonly selectedNodeIds: readonly string[];
  readonly selectedConnectionId: string | null;
  readonly multiSelectionCount: number;
  readonly scale: number;
  readonly offsetX: number;
  readonly offsetY: number;
  readonly logicalWidth: number;
  readonly logicalHeight: number;
  readonly backingWidth: number;
  readonly backingHeight: number;
  readonly dpr: number;
  readonly isConnecting: boolean;
  readonly isDraggingNodes: boolean;
  readonly isPanning: boolean;
  readonly isSelecting: boolean;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
  readonly mutationGate: FlowMutationGate;
  readonly nodes: readonly CanonicalNodeGeometry[];
  readonly resources: CanonicalCanvasResourceDiagnostics;
}

export interface CanonicalFlowCanvasProjection {
  readonly draft: CanonicalFlowDraft;
  readonly runtime: CanonicalCanvasRuntimeSnapshot;
  readonly feedback: CanonicalFlowFeedback | null;
}

export type CanonicalFlowCanvasEventKind =
  | 'structure'
  | 'selection'
  | 'view'
  | 'command'
  | 'feedback'
  | 'hydrate';

export interface CanonicalFlowCanvasEvent {
  readonly kind: CanonicalFlowCanvasEventKind;
  readonly projection: CanonicalFlowCanvasProjection;
}

export interface CanonicalFlowCommandResult {
  readonly ok: boolean;
  readonly code: string;
  readonly message: string;
  readonly flowRevision: number;
}

export interface CanonicalConnectCommand {
  readonly sourceNodeId: string;
  readonly sourcePortId: string;
  readonly targetNodeId: string;
  readonly targetPortId: string;
}

export interface CanonicalNodeParameterPatch {
  readonly nodeId: string;
  readonly parameterName: string;
  readonly value: unknown;
  readonly allowCreate?: boolean;
  readonly definition?: CanonicalOperatorParameterDefinition;
}

export interface CanonicalNodePropertiesPatch {
  readonly nodeId: string;
  readonly name?: string;
  readonly isEnabled?: boolean;
}

export interface CreateCanonicalFlowCanvasHostOptions {
  readonly canvasId: string;
  readonly initialFlow: unknown;
  readonly operatorLibraryElement?: HTMLElement | null;
  readonly shortcutScopeElement?: HTMLElement | null;
  readonly initialMutationGate?: FlowMutationGate;
}

export interface CanonicalFlowCanvasHost {
  serialize(): unknown;
  serializeDraft(): CanonicalFlowDraft;
  replaceFlow(flow: unknown): void;
  resize(): void;
  focus(): void;
  setMutationGate(gate: FlowMutationGate): void;
  addOperator(operator: CanonicalOperatorDefinition, position?: Readonly<{ x: number; y: number }>): CanonicalFlowCommandResult;
  deleteSelection(): CanonicalFlowCommandResult;
  copySelection(): CanonicalFlowCommandResult;
  pasteSelection(): CanonicalFlowCommandResult;
  duplicateSelection(): CanonicalFlowCommandResult;
  toggleSelectedDisabled(): CanonicalFlowCommandResult;
  undo(): CanonicalFlowCommandResult;
  redo(): CanonicalFlowCommandResult;
  selectAll(): CanonicalFlowCommandResult;
  clearSelection(): CanonicalFlowCommandResult;
  selectNode(nodeId: string): CanonicalFlowCommandResult;
  connect(command: CanonicalConnectCommand): CanonicalFlowCommandResult;
  disconnect(connectionId: string): CanonicalFlowCommandResult;
  patchNodeParameter(command: CanonicalNodeParameterPatch): CanonicalFlowCommandResult;
  patchNodeProperties(command: CanonicalNodePropertiesPatch): CanonicalFlowCommandResult;
  zoomBy(factor: number): CanonicalFlowCommandResult;
  resetView(): CanonicalFlowCommandResult;
  validateConnection(
    sourceId: string,
    sourcePort: number,
    targetId: string,
    targetPort: number
  ): string | null;
  subscribe(listener: (event: CanonicalFlowCanvasEvent) => void): () => void;
  getProjection(): CanonicalFlowCanvasProjection;
  getRuntimeSnapshot(): CanonicalCanvasRuntimeSnapshot;
  disposeInteraction(): void;
  disposeAdapter(): void;
}

export class CanonicalFlowCanvasOwnerConflictError extends Error {
  constructor() {
    super('A canonical FlowCanvas owner is already mounted.');
    this.name = 'CanonicalFlowCanvasOwnerConflictError';
  }
}

interface CanonicalPort {
  readonly id?: unknown;
  readonly Id?: unknown;
  readonly name?: unknown;
  readonly Name?: unknown;
  readonly type?: unknown;
  readonly Type?: unknown;
  readonly dataType?: unknown;
  readonly DataType?: unknown;
}

interface CanonicalNode {
  readonly id?: unknown;
  readonly type?: unknown;
  readonly title?: unknown;
  readonly disabled?: unknown;
  readonly inputs?: readonly CanonicalPort[];
  readonly outputs?: readonly CanonicalPort[];
  readonly parameters?: readonly Readonly<Record<string, unknown>>[];
}

interface CanonicalSelectionState {
  readonly selectedNodeId?: unknown;
  readonly selectedConnectionId?: unknown;
  readonly selectionRevision?: unknown;
}

interface CanonicalConnection {
  readonly id?: unknown;
}

interface CanonicalFlowCanvas {
  readonly canvas: HTMLCanvasElement;
  readonly nodes: ReadonlyMap<string, CanonicalNode>;
  connections: CanonicalConnection[];
  selectedNode?: unknown;
  selectedConnection?: CanonicalConnection | null;
  scale?: number;
  offset?: { x?: number; y?: number };
  readonly _dpr?: unknown;
  readonly _logicalWidth?: unknown;
  readonly _logicalHeight?: unknown;
  readonly _isDestroyed?: unknown;
  readonly _resizeObserver?: unknown;
  readonly _themeObserver?: unknown;
  readonly _animationFrameId?: unknown;
  readonly _resizeRafId?: unknown;
  readonly _contextMenuOpenTimer?: unknown;
  readonly structureStateListeners?: ReadonlySet<unknown>;
  readonly viewStateListeners?: ReadonlySet<unknown>;
  readonly selectionStateListeners?: ReadonlySet<unknown>;
  nodeRunEnabled?: boolean;
  nodeHelpEnabled?: boolean;
  getSelectionState?(): CanonicalSelectionState;
  getNodeScreenRect?(nodeId: string): Readonly<Record<string, unknown>> | null;
  getPortPosition?(nodeId: string, portIndex: number, isOutput: boolean): Readonly<Record<string, unknown>> | null;
  getConnectionValidationError?(sourceId: string, sourcePort: number, targetId: string, targetPort: number): unknown;
  addConnection?(sourceId: string, sourcePort: number, targetId: string, targetPort: number): unknown;
  removeConnection?(connectionId: string): boolean;
  toggleNodeDisabled?(nodeId: string): boolean;
  markSelectionChanged?(reason?: string): void;
  notifyViewStateChanged?(): void;
  invalidate?(): void;
  render?(): void;
}

let activeHostToken: symbol | undefined;

function finiteNumber(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function textValue(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : value === null || value === undefined ? fallback : String(value);
}

function recordValue(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function readArray(value: unknown): readonly Readonly<Record<string, unknown>>[] {
  return Array.isArray(value)
    ? Object.freeze(value.map(item => Object.freeze({ ...recordValue(item) })))
    : Object.freeze([]);
}

function objectIdentity(value: Readonly<Record<string, unknown>>): string {
  return textValue(value.id ?? value.Id) || textValue(value.name ?? value.Name).toLowerCase();
}

function mergeByIdentity(
  currentValue: unknown,
  baselineValue: unknown,
  merge: (
    current: Readonly<Record<string, unknown>>,
    baseline: Readonly<Record<string, unknown>>
  ) => Readonly<Record<string, unknown>> = (current, baseline) => Object.freeze({ ...baseline, ...current })
): readonly Readonly<Record<string, unknown>>[] {
  const current = readArray(currentValue);
  const baseline = new Map(readArray(baselineValue).map(item => [objectIdentity(item), item]));
  return Object.freeze(current.map(item => {
    const previous = baseline.get(objectIdentity(item));
    return previous ? merge(item, previous) : item;
  }));
}

function mergeOperatorPersistence(
  current: Readonly<Record<string, unknown>>,
  baseline: Readonly<Record<string, unknown>>
): Readonly<Record<string, unknown>> {
  return Object.freeze({
    ...baseline,
    ...current,
    inputPorts: mergeByIdentity(
      current.inputPorts ?? current.InputPorts,
      baseline.inputPorts ?? baseline.InputPorts
    ),
    outputPorts: mergeByIdentity(
      current.outputPorts ?? current.OutputPorts,
      baseline.outputPorts ?? baseline.OutputPorts
    ),
    parameters: mergeByIdentity(
      current.parameters ?? current.Parameters,
      baseline.parameters ?? baseline.Parameters
    )
  });
}

const flowKnownKeys = new Set([
  'id', 'Id', 'name', 'Name', 'operators', 'Operators', 'nodes',
  'connections', 'Connections', 'decisionConfiguration', 'DecisionConfiguration'
]);

function mergeFlowPersistence(
  currentValue: unknown,
  baselineValue: unknown
): Readonly<Record<string, unknown>> {
  const current = recordValue(currentValue);
  const baselineSource = recordValue(baselineValue);
  const nested = recordValue(baselineSource.flow ?? baselineSource.Flow);
  const baseline = Object.keys(nested).length > 0 ? nested : baselineSource;
  return Object.freeze({
    ...baseline,
    ...current,
    operators: mergeByIdentity(
      current.operators ?? current.Operators,
      baseline.operators ?? baseline.Operators ?? baseline.nodes,
      mergeOperatorPersistence
    ),
    connections: mergeByIdentity(
      current.connections ?? current.Connections,
      baseline.connections ?? baseline.Connections
    )
  });
}

function readFlowOpaque(flow: Readonly<Record<string, unknown>>): Readonly<Record<string, unknown>> {
  return Object.freeze(Object.fromEntries(
    Object.entries(flow).filter(([key]) => !flowKnownKeys.has(key))
  ));
}

function restoreParameterValuePresence(
  operators: readonly Readonly<Record<string, unknown>>[],
  canvas: CanonicalFlowCanvas | undefined
): readonly Readonly<Record<string, unknown>>[] {
  if (!canvas) return operators;
  return Object.freeze(operators.map(operator => {
    const node = canvas.nodes.get(textValue(operator.id ?? operator.Id));
    const rawParameters = Array.isArray(node?.parameters) ? node.parameters : [];
    const rawByIdentity = new Map(rawParameters.map(parameter => [objectIdentity(parameter), parameter]));
    const parameters = readArray(operator.parameters ?? operator.Parameters).map(parameter => {
      const raw = rawByIdentity.get(objectIdentity(parameter));
      if (!raw || Object.prototype.hasOwnProperty.call(raw, 'value') || Object.prototype.hasOwnProperty.call(raw, 'Value')) {
        return parameter;
      }
      const withoutSyntheticValue = { ...parameter };
      delete withoutSyntheticValue.value;
      delete withoutSyntheticValue.Value;
      return Object.freeze(withoutSyntheticValue);
    });
    return Object.freeze({ ...operator, parameters: Object.freeze(parameters) });
  }));
}

function readFlowIdentity(flow: unknown): Readonly<{ id: string | null; name: string }> {
  const source = recordValue(flow);
  const nested = recordValue(source.flow ?? source.Flow);
  const target = Object.keys(nested).length > 0 ? nested : source;
  const id = textValue(target.id ?? target.Id) || null;
  return Object.freeze({ id, name: textValue(target.name ?? target.Name, '未命名流程') });
}

function readPortPoint(
  canvas: CanonicalFlowCanvas,
  nodeId: string,
  port: CanonicalPort,
  portIndex: number,
  isOutput: boolean
): CanonicalPortPoint {
  const position = canvas.getPortPosition?.(nodeId, portIndex, isOutput);
  const dataType = port.type ?? port.Type ?? port.dataType ?? port.DataType;
  return Object.freeze({
    id: textValue(port.id ?? port.Id),
    name: textValue(port.name ?? port.Name),
    dataType: textValue(dataType, 'Any'),
    x: finiteNumber(position?.x),
    y: finiteNumber(position?.y),
    isOutput
  });
}

function readNodeGeometry(canvas: CanonicalFlowCanvas, node: CanonicalNode): CanonicalNodeGeometry {
  const id = textValue(node.id);
  const rect = canvas.getNodeScreenRect?.(id);
  const inputs = Array.isArray(node.inputs) ? node.inputs : [];
  const outputs = Array.isArray(node.outputs) ? node.outputs : [];
  return Object.freeze({
    id,
    type: textValue(node.type, 'Unknown'),
    title: textValue(node.title, textValue(node.type, 'Unknown')),
    x: finiteNumber(rect?.x),
    y: finiteNumber(rect?.y),
    width: finiteNumber(rect?.width),
    height: finiteNumber(rect?.height),
    disabled: node.disabled === true,
    inputs: Object.freeze(inputs.map((port, index) => readPortPoint(canvas, id, port, index, false))),
    outputs: Object.freeze(outputs.map((port, index) => readPortPoint(canvas, id, port, index, true)))
  });
}

function connectionMessage(reason: string): string {
  return {
    'missing-node': '连接端点不存在。',
    'missing-port': '连接端口不存在。',
    'self-connection': '不能连接到同一节点。',
    'incompatible-port-type': '端口类型不兼容。',
    'duplicate-connection': '该连接已存在。',
    'input-port-occupied': '目标输入端口已被占用。',
    cycle: '该连接会形成环路。'
  }[reason] ?? '连接无效。';
}

export function createCanonicalFlowCanvasHost(
  canvasIdOrOptions: string | CreateCanonicalFlowCanvasHostOptions,
  initialFlow?: unknown
): CanonicalFlowCanvasHost {
  if (activeHostToken) throw new CanonicalFlowCanvasOwnerConflictError();

  const options: CreateCanonicalFlowCanvasHostOptions = typeof canvasIdOrOptions === 'string'
    ? { canvasId: canvasIdOrOptions, initialFlow }
    : canvasIdOrOptions;
  const token = Symbol(`canonical-flow-canvas:${options.canvasId}`);
  activeHostToken = token;
  let adapter: FlowCanvasAdapter | undefined;
  let interaction: FlowEditorInteraction | undefined;
  let adapterDisposed = false;
  let interactionDisposed = false;
  let suppressEvents = true;
  let mutationGate = options.initialMutationGate ?? 'editable';
  let identity = readFlowIdentity(options.initialFlow);
  let persistenceBaseline = options.initialFlow;
  let localFlowRevision = 0;
  let feedbackSequence = 0;
  let feedback: CanonicalFlowFeedback | null = null;
  let cachedDraft: CanonicalFlowDraft | undefined;
  const listeners = new Set<(event: CanonicalFlowCanvasEvent) => void>();
  const rawUnsubscribes: Array<() => void> = [];

  const assertActive = (): void => {
    if (adapterDisposed || interactionDisposed) throw new Error('Canonical FlowCanvas owner has been disposed.');
  };

  const commandResult = (ok: boolean, code: string, message: string): CanonicalFlowCommandResult =>
    Object.freeze({ ok, code, message, flowRevision: localFlowRevision });

  const rejectMutation = (command: string): CanonicalFlowCommandResult | null => {
    if (mutationGate === 'editable') return null;
    const code = mutationGate;
    const message = mutationGate === 'running'
      ? '流程正在运行，当前命令不可用。'
      : '当前流程为只读状态，不能修改。';
    publishFeedback({ message, tone: 'warning', code, details: { command } });
    return commandResult(false, code, message);
  };

  const serializeDraft = (): CanonicalFlowDraft => {
    if (cachedDraft) return cachedDraft;
    const serialized = mergeFlowPersistence(adapter?.serialize(), persistenceBaseline);
    cachedDraft = Object.freeze({
      id: identity.id,
      name: identity.name,
      operators: restoreParameterValuePresence(
        readArray(serialized.operators ?? serialized.Operators),
        adapter?.raw as CanonicalFlowCanvas | undefined
      ),
      connections: readArray(serialized.connections ?? serialized.Connections),
      decisionConfiguration: serialized.decisionConfiguration ?? serialized.DecisionConfiguration ?? null,
      opaquePassthrough: readFlowOpaque(serialized)
    });
    return cachedDraft;
  };

  const readResources = (canvas: CanonicalFlowCanvas): CanonicalCanvasResourceDiagnostics => Object.freeze({
    adapterDisposed: adapterDisposed || adapter?.disposed === true,
    canvasDestroyed: canvas._isDestroyed === true,
    interactionDisposed: interactionDisposed || interaction?.disposed === true,
    resizeObserverActive: Boolean(canvas._resizeObserver),
    themeObserverActive: Boolean(canvas._themeObserver),
    drawFramePending: canvas._animationFrameId !== null && canvas._animationFrameId !== undefined,
    resizeFramePending: canvas._resizeRafId !== null && canvas._resizeRafId !== undefined,
    interactionFramePending: interaction?.viewStateNotifyRaf !== null && interaction?.viewStateNotifyRaf !== undefined,
    contextMenuTimerActive: canvas._contextMenuOpenTimer !== null && canvas._contextMenuOpenTimer !== undefined,
    structureListenerCount: canvas.structureStateListeners?.size ?? 0,
    viewListenerCount: canvas.viewStateListeners?.size ?? 0,
    selectionListenerCount: canvas.selectionStateListeners?.size ?? 0,
    interactionCleanupCount: interaction?.cleanup?.length ?? 0,
    facadeListenerCount: listeners.size
  });

  const runtimeSnapshot = (): CanonicalCanvasRuntimeSnapshot => {
    const canvas = adapter?.raw as CanonicalFlowCanvas;
    const selection = canvas.getSelectionState?.() ?? {};
    const selectedNodeIds = Object.freeze([...(interaction?.multiSelectedNodes ?? [])]);
    const selectedNodeId = textValue(selection.selectedNodeId ?? canvas.selectedNode) || selectedNodeIds.at(-1) || null;
    const selectedConnectionId = textValue(selection.selectedConnectionId ?? canvas.selectedConnection?.id) || null;
    const history = interaction?.getHistoryState() ?? { canUndo: false, canRedo: false };
    return Object.freeze({
      nodeCount: canvas.nodes.size,
      connectionCount: canvas.connections.length,
      flowRevision: localFlowRevision,
      selectionRevision: finiteNumber(selection.selectionRevision),
      selectedNodeId,
      selectedNodeIds,
      selectedConnectionId,
      multiSelectionCount: selectedNodeIds.length,
      scale: finiteNumber(canvas.scale, 1),
      offsetX: finiteNumber(canvas.offset?.x),
      offsetY: finiteNumber(canvas.offset?.y),
      logicalWidth: finiteNumber(canvas._logicalWidth),
      logicalHeight: finiteNumber(canvas._logicalHeight),
      backingWidth: finiteNumber(canvas.canvas.width),
      backingHeight: finiteNumber(canvas.canvas.height),
      dpr: finiteNumber(canvas._dpr, 1),
      isConnecting: interaction?.isConnecting === true,
      isDraggingNodes: interaction?.isDraggingNodes === true,
      isPanning: interaction?.isPanning === true,
      isSelecting: interaction?.isSelecting === true,
      canUndo: history.canUndo,
      canRedo: history.canRedo,
      mutationGate,
      nodes: Object.freeze([...canvas.nodes.values()].map(node => readNodeGeometry(canvas, node))),
      resources: readResources(canvas)
    });
  };

  const projection = (): CanonicalFlowCanvasProjection => Object.freeze({
    draft: serializeDraft(),
    runtime: runtimeSnapshot(),
    feedback
  });

  const emit = (kind: CanonicalFlowCanvasEventKind): void => {
    if (suppressEvents || adapterDisposed) return;
    const event = Object.freeze({ kind, projection: projection() });
    for (const listener of listeners) listener(event);
  };

  function publishFeedback(value: Omit<CanonicalFlowFeedback, 'sequence'>): CanonicalFlowFeedback {
    feedback = Object.freeze({ sequence: ++feedbackSequence, ...value });
    emit('feedback');
    return feedback;
  }

  try {
    adapter = createHostedFlowCanvasAdapter(options.canvasId);
    adapter.replaceFlow(options.initialFlow);
    const canvas = adapter.raw as CanonicalFlowCanvas;
    canvas.nodeRunEnabled = false;
    canvas.nodeHelpEnabled = false;
    interaction = new FlowEditorInteraction(canvas, {
      operatorLibraryElement: options.operatorLibraryElement ?? null,
      shortcutScopeElement: options.shortcutScopeElement ?? null,
      isReadonly: () => mutationGate !== 'editable',
      getMutationGate: () => mutationGate,
      onFeedback: (value: Readonly<Record<string, unknown>>) => {
        publishFeedback({
          message: textValue(value.message, '流程命令已处理。'),
          tone: textValue(value.tone, 'info') as FlowFeedbackTone,
          code: textValue(value.code, 'flow-command'),
          details: value.details ?? null
        });
      },
      onDraftCommitted: () => {
        localFlowRevision += 1;
        cachedDraft = undefined;
        emit('command');
      }
    });
    rawUnsubscribes.push(
      adapter.subscribeStructureState(() => {
        cachedDraft = undefined;
        emit('structure');
      }),
      adapter.subscribeViewState(() => emit('view')),
      adapter.subscribeSelection(() => emit('selection'))
    );
    suppressEvents = false;
  } catch (error) {
    try {
      interaction?.destroy();
    } finally {
      adapter?.dispose();
      if (activeHostToken === token) activeHostToken = undefined;
    }
    throw error;
  }

  const ownedAdapter = adapter;
  const ownedInteraction = interaction;
  const canvas = ownedAdapter.raw as CanonicalFlowCanvas;

  const selectedIds = (): readonly string[] => {
    const ids = [...ownedInteraction.multiSelectedNodes];
    const selected = textValue(canvas.selectedNode);
    if (selected && !ids.includes(selected)) ids.push(selected);
    return ids;
  };

  const findPortIndex = (nodeId: string, portId: string, isOutput: boolean): number => {
    const node = canvas.nodes.get(nodeId);
    const ports = isOutput ? node?.outputs : node?.inputs;
    return Array.isArray(ports)
      ? ports.findIndex(port => textValue(port.id ?? port.Id) === portId)
      : -1;
  };

  return Object.freeze({
    serialize(): unknown {
      return ownedAdapter.serialize();
    },
    serializeDraft,
    replaceFlow(flow: unknown): void {
      assertActive();
      suppressEvents = true;
      try {
        identity = readFlowIdentity(flow);
        persistenceBaseline = flow;
        ownedAdapter.replaceFlow(flow);
        ownedInteraction.resetTransientInteractionAfterRestore();
        ownedInteraction.resetHistory({ notify: false });
        localFlowRevision = 0;
        cachedDraft = undefined;
        feedback = null;
      } finally {
        suppressEvents = false;
      }
      emit('hydrate');
    },
    resize(): void {
      assertActive();
      ownedAdapter.resize();
    },
    focus(): void {
      assertActive();
      canvas.canvas.focus({ preventScroll: true });
    },
    setMutationGate(gate: FlowMutationGate): void {
      assertActive();
      mutationGate = gate;
      ownedInteraction.resetTransientInteractionAfterRestore();
      emit('command');
    },
    addOperator(
      operator: CanonicalOperatorDefinition,
      position?: Readonly<{ x: number; y: number }>
    ): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('add-operator');
      if (rejected) return rejected;
      const x = position?.x ?? finiteNumber(canvas.offset?.x) + finiteNumber(canvas._logicalWidth, 640) / (2 * finiteNumber(canvas.scale, 1));
      const y = position?.y ?? finiteNumber(canvas.offset?.y) + finiteNumber(canvas._logicalHeight, 480) / (2 * finiteNumber(canvas.scale, 1));
      const node = ownedInteraction.addOperatorNode(operator.operatorType, x, y, {
        ...operator,
        type: operator.operatorType,
        name: operator.displayName
      }) as Readonly<{ id?: unknown }> | null;
      if (!node) return commandResult(false, 'operator-add-failed', '无法添加算子。');
      ownedInteraction.clearSelection({ notify: false });
      ownedInteraction.selectNode(textValue(node.id));
      ownedInteraction.saveState({ reason: 'add-operator-click' });
      return commandResult(true, 'operator-added', `已添加算子：${operator.displayName}`);
    },
    deleteSelection(): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('delete-selection');
      if (rejected) return rejected;
      const ok = ownedInteraction.deleteSelectedItems();
      return commandResult(ok, ok ? 'selection-deleted' : 'selection-empty', ok ? '已删除选中项。' : '没有可删除的选中项。');
    },
    copySelection(): CanonicalFlowCommandResult {
      assertActive();
      const count = selectedIds().length;
      ownedInteraction.copySelectedNodes();
      return commandResult(count > 0, count > 0 ? 'selection-copied' : 'selection-empty', count > 0 ? `已复制 ${count} 个节点。` : '没有选中节点。');
    },
    pasteSelection(): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('paste');
      if (rejected) return rejected;
      const ok = ownedInteraction.pasteNodes();
      return commandResult(ok, ok ? 'selection-pasted' : 'clipboard-empty', ok ? '已粘贴节点。' : '剪贴板为空。');
    },
    duplicateSelection(): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('duplicate-selection');
      if (rejected) return rejected;
      const ids = selectedIds();
      if (ids.length === 1) {
        const ok = ownedInteraction.duplicateNodeFromCanvasRequest(ids[0]!);
        return commandResult(ok, ok ? 'selection-duplicated' : 'duplicate-failed', ok ? '已复制节点。' : '无法复制节点。');
      }
      if (ids.length > 1) {
        ownedInteraction.copySelectedNodes();
        const ok = ownedInteraction.pasteNodes();
        return commandResult(ok, ok ? 'selection-duplicated' : 'duplicate-failed', ok ? `已复制 ${ids.length} 个节点。` : '无法复制节点。');
      }
      return commandResult(false, 'selection-empty', '没有选中节点。');
    },
    toggleSelectedDisabled(): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('toggle-node-disabled');
      if (rejected) return rejected;
      const ids = selectedIds();
      let changed = 0;
      for (const id of ids) if (canvas.toggleNodeDisabled?.(id)) changed += 1;
      if (changed > 0) ownedInteraction.saveState({ reason: 'toggle-node-disabled' });
      return commandResult(changed > 0, changed > 0 ? 'nodes-toggled' : 'selection-empty', changed > 0 ? `已切换 ${changed} 个节点的启用状态。` : '没有选中节点。');
    },
    undo(): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('undo');
      if (rejected) return rejected;
      const ok = ownedInteraction.undo();
      return commandResult(ok, ok ? 'undo' : 'undo-empty', ok ? '已撤销。' : '没有可撤销的操作。');
    },
    redo(): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('redo');
      if (rejected) return rejected;
      const ok = ownedInteraction.redo();
      return commandResult(ok, ok ? 'redo' : 'redo-empty', ok ? '已重做。' : '没有可重做的操作。');
    },
    selectAll(): CanonicalFlowCommandResult {
      assertActive();
      ownedInteraction.selectAll();
      return commandResult(true, 'selection-all', `已选择 ${canvas.nodes.size} 个节点。`);
    },
    clearSelection(): CanonicalFlowCommandResult {
      assertActive();
      ownedInteraction.clearSelection();
      canvas.selectedConnection = null;
      canvas.markSelectionChanged?.('clear-selection-command');
      return commandResult(true, 'selection-cleared', '已清除选择。');
    },
    selectNode(nodeId: string): CanonicalFlowCommandResult {
      assertActive();
      const ok = ownedAdapter.selectNode(nodeId);
      return commandResult(ok, ok ? 'node-selected' : 'node-not-found', ok ? '已选择节点。' : '节点不存在。');
    },
    connect(command: CanonicalConnectCommand): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('connect');
      if (rejected) return rejected;
      const sourcePort = findPortIndex(command.sourceNodeId, command.sourcePortId, true);
      const targetPort = findPortIndex(command.targetNodeId, command.targetPortId, false);
      const reason = sourcePort < 0 || targetPort < 0
        ? 'missing-port'
        : textValue(canvas.getConnectionValidationError?.(
          command.sourceNodeId,
          sourcePort,
          command.targetNodeId,
          targetPort
        ));
      if (reason) {
        const message = connectionMessage(reason);
        publishFeedback({ code: reason, message, tone: 'warning', details: command });
        return commandResult(false, reason, message);
      }
      const connection = canvas.addConnection?.(command.sourceNodeId, sourcePort, command.targetNodeId, targetPort);
      if (!connection) return commandResult(false, 'connection-rejected', '连接未创建。');
      ownedInteraction.saveState({ reason: 'connect-command' });
      return commandResult(true, 'connection-created', '连接已建立。');
    },
    disconnect(connectionId: string): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('disconnect');
      if (rejected) return rejected;
      const ok = canvas.removeConnection?.(connectionId) === true;
      if (ok) ownedInteraction.saveState({ reason: 'disconnect-command' });
      return commandResult(ok, ok ? 'connection-disconnected' : 'connection-not-found', ok ? '连接已断开。' : '连接不存在。');
    },
    patchNodeParameter(command: CanonicalNodeParameterPatch): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('patch-node-parameter');
      if (rejected) return rejected;
      const options = command.definition
        ? {
            allowCreateParameters: command.allowCreate === true,
            parameterDefinitions: [command.definition]
          }
        : { allowCreateParameters: command.allowCreate === true };
      const result = ownedAdapter.patchNodeParameters(
        command.nodeId,
        { [command.parameterName]: command.value },
        options
      );
      if (result.updated) {
        ownedInteraction.saveState({ reason: 'patch-node-parameter' });
        return commandResult(true, 'node-parameter-patched', '参数已更新。');
      }
      const code = result.reason === 'no_change' ? 'no-change' : result.reason;
      const message = result.reason === 'no_change'
        ? '参数值未变化。'
        : result.reason === 'node_not_found'
          ? '节点不存在。'
          : '参数不存在。';
      return commandResult(false, code, message);
    },
    patchNodeProperties(command: CanonicalNodePropertiesPatch): CanonicalFlowCommandResult {
      assertActive();
      const rejected = rejectMutation('patch-node-properties');
      if (rejected) return rejected;
      const patch: { name?: string; isEnabled?: boolean } = {};
      if (Object.prototype.hasOwnProperty.call(command, 'name')) patch.name = command.name!;
      if (Object.prototype.hasOwnProperty.call(command, 'isEnabled')) patch.isEnabled = command.isEnabled!;
      const result = ownedAdapter.patchNodeProperties(command.nodeId, patch);
      if (result.updated) {
        ownedInteraction.saveState({ reason: 'patch-node-properties' });
        return commandResult(true, 'node-properties-patched', '节点属性已更新。');
      }
      const code = result.reason === 'no_change' ? 'no-change' : result.reason;
      return commandResult(false, code, result.reason === 'node_not_found' ? '节点不存在。' : '节点属性未变化。');
    },
    zoomBy(factor: number): CanonicalFlowCommandResult {
      assertActive();
      const current = finiteNumber(canvas.scale, 1);
      canvas.scale = Math.max(0.2, Math.min(2, current * factor));
      canvas.invalidate?.();
      canvas.notifyViewStateChanged?.();
      return commandResult(true, 'view-zoomed', `缩放 ${Math.round(canvas.scale * 100)}%。`);
    },
    resetView(): CanonicalFlowCommandResult {
      assertActive();
      canvas.scale = 1;
      if (canvas.offset) {
        canvas.offset.x = 0;
        canvas.offset.y = 0;
      }
      canvas.invalidate?.();
      canvas.notifyViewStateChanged?.();
      return commandResult(true, 'view-reset', '画布视图已重置。');
    },
    validateConnection(
      sourceId: string,
      sourcePort: number,
      targetId: string,
      targetPort: number
    ): string | null {
      const result = canvas.getConnectionValidationError?.(sourceId, sourcePort, targetId, targetPort);
      return typeof result === 'string' ? result : null;
    },
    subscribe(listener: (event: CanonicalFlowCanvasEvent) => void): () => void {
      assertActive();
      listeners.add(listener);
      listener(Object.freeze({ kind: 'hydrate', projection: projection() }));
      let subscribed = true;
      return () => {
        if (!subscribed) return;
        subscribed = false;
        listeners.delete(listener);
      };
    },
    getProjection: projection,
    getRuntimeSnapshot: runtimeSnapshot,
    disposeInteraction(): void {
      if (interactionDisposed) return;
      interactionDisposed = true;
      while (rawUnsubscribes.length > 0) rawUnsubscribes.pop()?.();
      ownedInteraction.destroy();
    },
    disposeAdapter(): void {
      if (adapterDisposed) return;
      if (!interactionDisposed) {
        interactionDisposed = true;
        while (rawUnsubscribes.length > 0) rawUnsubscribes.pop()?.();
        ownedInteraction.destroy();
      }
      adapterDisposed = true;
      ownedAdapter.dispose();
      listeners.clear();
      if (activeHostToken === token) activeHostToken = undefined;
    }
  });
}
