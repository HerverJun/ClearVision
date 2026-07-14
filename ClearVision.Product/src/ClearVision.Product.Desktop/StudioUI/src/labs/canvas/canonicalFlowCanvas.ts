import {
  createHostedFlowCanvasAdapter,
  type FlowCanvasAdapter
} from '@clearvision/canonical-flow-canvas';
import { FlowEditorInteraction } from '@clearvision/canonical-flow-interaction';

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
}

export interface CanonicalCanvasRuntimeSnapshot {
  readonly nodeCount: number;
  readonly connectionCount: number;
  readonly flowRevision: number;
  readonly selectionRevision: number;
  readonly selectedNodeId: string | null;
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
  readonly nodes: readonly CanonicalNodeGeometry[];
  readonly resources: CanonicalCanvasResourceDiagnostics;
}

export interface CanonicalFlowCanvasHost {
  serialize(): unknown;
  replaceFlow(flow: unknown): void;
  resize(): void;
  validateConnection(
    sourceId: string,
    sourcePort: number,
    targetId: string,
    targetPort: number
  ): string | null;
  subscribe(listener: () => void): () => void;
  getRuntimeSnapshot(): CanonicalCanvasRuntimeSnapshot;
  disposeInteraction(): void;
  disposeAdapter(): void;
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
  readonly x?: unknown;
  readonly y?: unknown;
  readonly width?: unknown;
  readonly height?: unknown;
  readonly inputs?: readonly CanonicalPort[];
  readonly outputs?: readonly CanonicalPort[];
}

interface CanonicalSelectionState {
  readonly selectedNodeId?: unknown;
  readonly selectedConnectionId?: unknown;
  readonly selectionRevision?: unknown;
  readonly flowRevision?: unknown;
}

interface CanonicalFlowCanvas {
  readonly canvas: HTMLCanvasElement;
  readonly nodes: ReadonlyMap<string, CanonicalNode>;
  readonly connections: readonly unknown[];
  readonly selectedNode?: unknown;
  readonly selectedConnection?: { readonly id?: unknown } | null;
  readonly scale?: unknown;
  readonly offset?: { readonly x?: unknown; readonly y?: unknown };
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
  getFlowRevision?(): unknown;
  getSelectionState?(): CanonicalSelectionState;
  getNodeScreenRect?(nodeId: string): Readonly<Record<string, unknown>> | null;
  getPortPosition?(
    nodeId: string,
    portIndex: number,
    isOutput: boolean
  ): Readonly<Record<string, unknown>> | null;
  getConnectionValidationError?(
    sourceId: string,
    sourcePort: number,
    targetId: string,
    targetPort: number
  ): unknown;
}

function finiteNumber(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function textValue(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback;
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
    dataType: textValue(dataType, String(dataType ?? 'Any')),
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
    type: textValue(node.type, String(node.type ?? 'Unknown')),
    title: textValue(node.title, String(node.type ?? 'Unknown')),
    x: finiteNumber(rect?.x),
    y: finiteNumber(rect?.y),
    width: finiteNumber(rect?.width),
    height: finiteNumber(rect?.height),
    inputs: Object.freeze(inputs.map((port, index) =>
      readPortPoint(canvas, id, port, index, false))),
    outputs: Object.freeze(outputs.map((port, index) =>
      readPortPoint(canvas, id, port, index, true)))
  });
}

export function createCanonicalFlowCanvasHost(
  canvasId: string,
  initialFlow: unknown
): CanonicalFlowCanvasHost {
  let adapter: FlowCanvasAdapter | undefined;
  let interaction: FlowEditorInteraction | undefined;
  let interactionDisposed = false;
  let adapterDisposed = false;

  try {
    adapter = createHostedFlowCanvasAdapter(canvasId);
    adapter.replaceFlow(initialFlow);
    const canvas = adapter.raw as CanonicalFlowCanvas;
    interaction = new FlowEditorInteraction(canvas);
  } catch (error) {
    interaction?.destroy();
    adapter?.dispose();
    throw error;
  }

  const ownedAdapter = adapter;
  const ownedInteraction = interaction;
  const canvas = ownedAdapter.raw as CanonicalFlowCanvas;

  const readResources = (): CanonicalCanvasResourceDiagnostics => Object.freeze({
    adapterDisposed: adapterDisposed || ownedAdapter.disposed === true,
    canvasDestroyed: canvas._isDestroyed === true,
    interactionDisposed: interactionDisposed || ownedInteraction.disposed === true,
    resizeObserverActive: Boolean(canvas._resizeObserver),
    themeObserverActive: Boolean(canvas._themeObserver),
    drawFramePending: canvas._animationFrameId !== null && canvas._animationFrameId !== undefined,
    resizeFramePending: canvas._resizeRafId !== null && canvas._resizeRafId !== undefined,
    interactionFramePending: ownedInteraction.viewStateNotifyRaf !== null &&
      ownedInteraction.viewStateNotifyRaf !== undefined,
    contextMenuTimerActive: canvas._contextMenuOpenTimer !== null &&
      canvas._contextMenuOpenTimer !== undefined,
    structureListenerCount: canvas.structureStateListeners?.size ?? 0,
    viewListenerCount: canvas.viewStateListeners?.size ?? 0,
    selectionListenerCount: canvas.selectionStateListeners?.size ?? 0,
    interactionCleanupCount: ownedInteraction.cleanup?.length ?? 0
  });

  return Object.freeze({
    serialize(): unknown {
      return ownedAdapter.serialize();
    },
    replaceFlow(flow: unknown): void {
      ownedAdapter.replaceFlow(flow);
      ownedInteraction.resetTransientInteractionAfterRestore();
      ownedInteraction.saveState();
    },
    resize(): void {
      ownedAdapter.resize();
    },
    validateConnection(
      sourceId: string,
      sourcePort: number,
      targetId: string,
      targetPort: number
    ): string | null {
      const result = canvas.getConnectionValidationError?.(
        sourceId,
        sourcePort,
        targetId,
        targetPort
      );
      return typeof result === 'string' ? result : null;
    },
    subscribe(listener: () => void): () => void {
      const disposeStructure = ownedAdapter.subscribeStructureState(listener);
      const disposeView = ownedAdapter.subscribeViewState(listener);
      const disposeSelection = ownedAdapter.subscribeSelection(listener);
      let subscribed = true;
      return () => {
        if (!subscribed) {
          return;
        }
        subscribed = false;
        disposeSelection();
        disposeView();
        disposeStructure();
      };
    },
    getRuntimeSnapshot(): CanonicalCanvasRuntimeSnapshot {
      const selection = canvas.getSelectionState?.() ?? {};
      const selectedConnectionId = textValue(
        selection.selectedConnectionId ?? canvas.selectedConnection?.id
      );
      return Object.freeze({
        nodeCount: canvas.nodes.size,
        connectionCount: canvas.connections.length,
        flowRevision: finiteNumber(selection.flowRevision ?? canvas.getFlowRevision?.()),
        selectionRevision: finiteNumber(selection.selectionRevision),
        selectedNodeId: textValue(selection.selectedNodeId ?? canvas.selectedNode) || null,
        selectedConnectionId: selectedConnectionId || null,
        multiSelectionCount: ownedInteraction.multiSelectedNodes?.size ?? 0,
        scale: finiteNumber(canvas.scale, 1),
        offsetX: finiteNumber(canvas.offset?.x),
        offsetY: finiteNumber(canvas.offset?.y),
        logicalWidth: finiteNumber(canvas._logicalWidth),
        logicalHeight: finiteNumber(canvas._logicalHeight),
        backingWidth: finiteNumber(canvas.canvas.width),
        backingHeight: finiteNumber(canvas.canvas.height),
        dpr: finiteNumber(canvas._dpr, 1),
        isConnecting: ownedInteraction.isConnecting === true,
        isDraggingNodes: ownedInteraction.isDraggingNodes === true,
        isPanning: ownedInteraction.isPanning === true,
        isSelecting: ownedInteraction.isSelecting === true,
        nodes: Object.freeze([...canvas.nodes.values()].map(node => readNodeGeometry(canvas, node))),
        resources: readResources()
      });
    },
    disposeInteraction(): void {
      if (interactionDisposed) {
        return;
      }
      interactionDisposed = true;
      ownedInteraction.destroy();
    },
    disposeAdapter(): void {
      if (adapterDisposed) {
        return;
      }
      adapterDisposed = true;
      ownedAdapter.dispose();
    }
  });
}
