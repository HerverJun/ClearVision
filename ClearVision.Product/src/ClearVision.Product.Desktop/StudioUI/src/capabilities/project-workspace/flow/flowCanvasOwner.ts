import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import {
  createCanonicalFlowCanvasHost,
  type CanonicalConnectCommand,
  type CanonicalFlowCanvasHost,
  type CanonicalFlowCommandResult,
  type CanonicalFlowDraft,
  type CanonicalFlowFeedback,
  type CanonicalNodeParameterPatch,
  type CanonicalNodeParametersPatch,
  type CanonicalNodePropertiesPatch,
  type CanonicalCaliperSearchRegionPatch,
  type CanonicalOperatorDefinition,
  type CanonicalCanvasRuntimeSnapshot,
  type FlowMutationGate
} from '@/platform/canvas';
import type { ReadQueryClient, ReadQueryState } from '@/platform/query';
import type { ApiTransport } from '@/platform/api';
import {
  createOperatorCatalogQuery
} from '@/capabilities/operators-read/operatorQueries';
import type { OperatorCatalogItem } from '@/capabilities/operators-read/operatorContracts';
import type { OperatorParameter } from '@/capabilities/operators-read/operatorContracts';
import {
  encodeWorkspaceDecisionConfigurationV1,
  type WorkspaceFlowV1,
  type WorkspaceJsonValue,
  type WorkspaceProjectV1
} from '../workspaceContracts';
import type {
  WorkspaceFlowCanvasDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceResourceSnapshot
} from '../workspaceLifecycleDiagnostics';
import {
  createInspectorOwner,
  type InspectorOwner
} from '../inspector';
import {
  createPreviewWorkbenchOwner,
  type PreviewWorkbenchOwner
} from '../preview/previewWorkbenchOwner';

export type FlowCanvasOwnerPhase = 'idle' | 'mounted' | 'error' | 'disposed';

export interface OperatorCatalogProjection {
  readonly phase: ReadQueryState<readonly OperatorCatalogItem[]>['phase'];
  readonly operators: readonly OperatorCatalogItem[];
  readonly isRefreshing: boolean;
  readonly message: string | null;
}

export interface FlowCanvasOwnerProjection {
  readonly phase: FlowCanvasOwnerPhase;
  readonly projectId: string;
  readonly mutationGate: FlowMutationGate;
  readonly draft: CanonicalFlowDraft;
  readonly runtime: CanonicalCanvasRuntimeSnapshot | null;
  readonly feedback: CanonicalFlowFeedback | null;
  readonly catalog: OperatorCatalogProjection;
  readonly error: string | null;
}

type MutableProjection = {
  -readonly [Key in keyof FlowCanvasOwnerProjection]: FlowCanvasOwnerProjection[Key]
};

export interface FlowCanvasMountOptions {
  readonly canvasId: string;
  readonly operatorLibraryElement: HTMLElement;
  readonly shortcutScopeElement: HTMLElement;
}

export interface FlowCanvasCommands {
  addOperator(operator: OperatorCatalogItem, position?: Readonly<{ x: number; y: number }>): CanonicalFlowCommandResult;
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
  patchNodeParameter(command: FlowNodeParameterPatch): CanonicalFlowCommandResult;
  patchNodeParameters(command: FlowNodeParametersPatch): CanonicalFlowCommandResult;
  upsertCaliperSearchRegion(command: FlowCaliperSearchRegionPatch): CanonicalFlowCommandResult;
  patchNodeProperties(command: FlowNodePropertiesPatch): CanonicalFlowCommandResult;
  zoomIn(): CanonicalFlowCommandResult;
  zoomOut(): CanonicalFlowCommandResult;
  resetView(): CanonicalFlowCommandResult;
  focus(): void;
}

export interface FlowNodeParameterPatch {
  readonly nodeId: string;
  readonly parameterName: string;
  readonly value: WorkspaceJsonValue;
  readonly definition?: OperatorParameter;
}

export interface FlowNodePropertiesPatch {
  readonly nodeId: string;
  readonly name?: string;
  readonly isEnabled?: boolean;
}

export interface FlowNodeParametersPatch {
  readonly nodeId: string;
  readonly values: Readonly<Record<string, WorkspaceJsonValue>>;
  readonly definitions?: readonly OperatorParameter[];
}

export interface FlowCaliperSearchRegionPatch {
  readonly caliperNodeId: string;
  readonly values: Readonly<Record<string, WorkspaceJsonValue>>;
}

export interface FlowCanvasOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<FlowCanvasOwnerProjection>;
  readonly commands: FlowCanvasCommands;
  mountCanvas(options: FlowCanvasMountOptions): void;
  replaceFlow(flow: Readonly<Record<string, unknown>> | null, projectName: string): void;
  openInspector(): InspectorOwner;
  openPreviewWorkbench(inspectorOwner: InspectorOwner): PreviewWorkbenchOwner;
  refreshOperators(force?: boolean): Promise<void>;
  setMutationGate(gate: FlowMutationGate): void;
  dispose(reason?: string): void;
}

export class FlowCanvasOwnerConflictError extends Error {
  constructor(projectId: string) {
    super(`FlowCanvas owner already mounted for project ${projectId}.`);
    this.name = 'FlowCanvasOwnerConflictError';
  }
}

function enumPersistenceValue<T extends string>(value: Readonly<{ persistenceValue: T | number }>): T | number {
  return value.persistenceValue;
}

function decisionValue(value: WorkspaceJsonValue | undefined): WorkspaceJsonValue | undefined {
  return value;
}

function toCanvasFlow(flow: WorkspaceFlowV1 | null, projectName: string): Readonly<Record<string, unknown>> {
  if (flow === null) {
    return Object.freeze({
      id: null,
      name: `${projectName} 流程`,
      operators: Object.freeze([]),
      connections: Object.freeze([]),
      decisionConfiguration: null
    });
  }

  return Object.freeze({
    ...flow.opaquePassthrough,
    id: flow.id,
    name: flow.name,
    operators: Object.freeze(flow.operators.map(operator => Object.freeze({
      ...operator.opaquePassthrough,
      id: operator.id,
      name: operator.name,
      type: enumPersistenceValue(operator.type),
      metadata: operator.metadata,
      x: operator.x,
      y: operator.y,
      inputPorts: Object.freeze(operator.inputPorts.map(port => Object.freeze({
        ...port.opaquePassthrough,
        id: port.id,
        name: port.name,
        direction: enumPersistenceValue(port.direction),
        dataType: enumPersistenceValue(port.dataType),
        isRequired: port.isRequired
      }))),
      outputPorts: Object.freeze(operator.outputPorts.map(port => Object.freeze({
        ...port.opaquePassthrough,
        id: port.id,
        name: port.name,
        direction: enumPersistenceValue(port.direction),
        dataType: enumPersistenceValue(port.dataType),
        isRequired: port.isRequired
      }))),
      parameters: Object.freeze(operator.parameters.map(parameter => Object.freeze({
        ...parameter.opaquePassthrough,
        id: parameter.id,
        name: parameter.name,
        displayName: parameter.displayName,
        description: parameter.description,
        dataType: parameter.dataType,
        value: decisionValue(parameter.value),
        defaultValue: decisionValue(parameter.defaultValue),
        minValue: decisionValue(parameter.minValue),
        maxValue: decisionValue(parameter.maxValue),
        isRequired: parameter.isRequired,
        options: parameter.options === null
          ? null
          : Object.freeze(parameter.options.map(option => Object.freeze({
              ...option.opaquePassthrough,
              label: option.label,
              value: option.value
            })))
      }))),
      isEnabled: operator.isEnabled
    }))),
    connections: Object.freeze(flow.connections.map(connection => Object.freeze({
      ...connection.opaquePassthrough,
      id: connection.id,
      sourceOperatorId: connection.sourceOperatorId,
      sourcePortId: connection.sourcePortId,
      targetOperatorId: connection.targetOperatorId,
      targetPortId: connection.targetPortId
    }))),
    decisionConfiguration: encodeWorkspaceDecisionConfigurationV1(flow.decisionConfiguration)
  });
}

function initialDraft(flow: WorkspaceFlowV1 | null, projectName: string): CanonicalFlowDraft {
  const canvasFlow = toCanvasFlow(flow, projectName);
  return Object.freeze({
    id: typeof canvasFlow.id === 'string' ? canvasFlow.id : null,
    name: typeof canvasFlow.name === 'string' ? canvasFlow.name : `${projectName} 流程`,
    operators: Array.isArray(canvasFlow.operators)
      ? canvasFlow.operators as readonly Readonly<Record<string, unknown>>[]
      : Object.freeze([]),
    connections: Array.isArray(canvasFlow.connections)
      ? canvasFlow.connections as readonly Readonly<Record<string, unknown>>[]
      : Object.freeze([]),
    decisionConfiguration: canvasFlow.decisionConfiguration ?? null,
    opaquePassthrough: flow?.opaquePassthrough ?? Object.freeze({})
  });
}

function catalogMessage(state: ReadQueryState<readonly OperatorCatalogItem[]>): string | null {
  if (state.phase === 'loading') return '正在读取算子目录。';
  if (state.phase === 'unauthorized') return '当前会话不可用，无法读取算子目录。';
  if (state.phase === 'forbidden') return '当前账号无权读取算子目录。';
  if (state.phase === 'empty') return '算子目录为空。';
  return state.failure?.message ?? null;
}

function operatorDefinition(operator: OperatorCatalogItem): CanonicalOperatorDefinition {
  return Object.freeze({
    operatorType: operator.operatorType,
    displayName: operator.displayName,
    category: operator.category,
    iconName: operator.iconName,
    inputPorts: operator.inputPorts,
    outputPorts: operator.outputPorts,
    parameters: operator.parameters
  });
}

function parameterDefinition(parameter: OperatorParameter): NonNullable<CanonicalNodeParameterPatch['definition']> {
  return Object.freeze({
    name: parameter.name,
    displayName: parameter.displayName,
    description: parameter.description,
    dataType: parameter.dataType,
    defaultValue: parameter.defaultValue,
    minValue: parameter.minValue,
    maxValue: parameter.maxValue,
    isRequired: parameter.isRequired,
    options: parameter.options
  });
}

function zeroResources(): WorkspaceResourceSnapshot {
  return Object.freeze({
    activeSubscriptions: 0,
    activeTimers: 0,
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: 0,
    activeBlobUrls: 0,
    activePreviewArtifactIds: 0,
    activeHostSubscriptions: 0,
    inFlightReads: 0,
    inFlightWrites: 0,
    inFlightPreview: 0,
    inFlightExecute: 0
  });
}

export function createFlowCanvasOwner(options: {
  readonly project: WorkspaceProjectV1;
  readonly queries: ReadQueryClient;
  readonly api: ApiTransport;
  readonly featureFlags: Readonly<Record<string, boolean>>;
  readonly diagnostics: WorkspaceLifecycleDiagnosticsOwner;
  readonly initialMutationGate?: FlowMutationGate;
}): FlowCanvasOwner {
  const catalogQuery = createOperatorCatalogQuery(options.queries);
  const diagnosticsLease: WorkspaceFlowCanvasDiagnosticsLease =
    options.diagnostics.reserveFlowCanvas(options.project.id);
  const canvasFlow = toCanvasFlow(options.project.flow, options.project.name);
  const state = reactive<MutableProjection>({
    phase: 'idle',
    projectId: options.project.id,
    mutationGate: options.initialMutationGate ?? 'editable',
    draft: initialDraft(options.project.flow, options.project.name),
    runtime: null,
    feedback: null,
    catalog: Object.freeze({
      phase: 'idle',
      operators: Object.freeze([]),
      isRefreshing: false,
      message: null
    }),
    error: null
  });
  let host: CanonicalFlowCanvasHost | undefined;
  let unsubscribeHost: (() => void) | undefined;
  let disposed = false;
  let commandFeedbackSequence = 0;
  let inspectorOwner: InspectorOwner | undefined;
  let previewWorkbenchOwner: PreviewWorkbenchOwner | undefined;

  function assertActive(): void {
    if (disposed) throw new Error('FlowCanvas owner has been disposed.');
  }

  function syncDiagnostics(): void {
    const runtime = state.runtime;
    const catalogBusy = state.catalog.phase === 'loading' || state.catalog.isRefreshing;
    diagnosticsLease.update(Object.freeze({
      activeSubscriptions: 1 + (runtime?.resources.structureListenerCount ?? 0) +
        (runtime?.resources.viewListenerCount ?? 0) +
        (runtime?.resources.selectionListenerCount ?? 0) +
        (runtime?.resources.facadeListenerCount ?? 0),
      activeTimers: runtime?.resources.contextMenuTimerActive ? 1 : 0,
      activeAnimationFrames: Number(runtime?.resources.drawFramePending === true) +
        Number(runtime?.resources.resizeFramePending === true) +
        Number(runtime?.resources.interactionFramePending === true),
      activeObservers: Number(runtime?.resources.resizeObserverActive === true) +
        Number(runtime?.resources.themeObserverActive === true),
      activeAbortControllers: catalogBusy ? 1 : 0,
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads: catalogBusy ? 1 : 0,
      inFlightWrites: 0,
      inFlightPreview: 0,
      inFlightExecute: 0
    }));
  }

  function applyCommandResult(result: CanonicalFlowCommandResult): CanonicalFlowCommandResult {
    if (!host) return result;
    const next = host.getProjection();
    state.draft = next.draft;
    state.runtime = next.runtime;
    state.feedback = next.feedback ?? Object.freeze({
      sequence: ++commandFeedbackSequence,
      code: result.code,
      message: result.message,
      tone: result.ok ? 'success' : 'warning',
      details: null
    });
    syncDiagnostics();
    return result;
  }

  const stopCatalogWatch = watch(
    () => catalogQuery.state.value,
    value => {
      if (disposed) return;
      state.catalog = Object.freeze({
        phase: value.phase,
        operators: value.data ?? Object.freeze([]),
        isRefreshing: value.isRefreshing,
        message: catalogMessage(value)
      });
      syncDiagnostics();
    },
    { immediate: true }
  );

  const commands: FlowCanvasCommands = Object.freeze({
    addOperator(
      operator: OperatorCatalogItem,
      position?: Readonly<{ x: number; y: number }>
    ) {
      assertActive();
      if (!host) return Object.freeze({ ok: false, code: 'canvas-not-mounted', message: '画布尚未挂载。', flowRevision: 0 });
      return applyCommandResult(host.addOperator(operatorDefinition(operator), position));
    },
    deleteSelection() { assertActive(); return applyCommandResult(host!.deleteSelection()); },
    copySelection() { assertActive(); return applyCommandResult(host!.copySelection()); },
    pasteSelection() { assertActive(); return applyCommandResult(host!.pasteSelection()); },
    duplicateSelection() { assertActive(); return applyCommandResult(host!.duplicateSelection()); },
    toggleSelectedDisabled() { assertActive(); return applyCommandResult(host!.toggleSelectedDisabled()); },
    undo() { assertActive(); return applyCommandResult(host!.undo()); },
    redo() { assertActive(); return applyCommandResult(host!.redo()); },
    selectAll() { assertActive(); return applyCommandResult(host!.selectAll()); },
    clearSelection() { assertActive(); return applyCommandResult(host!.clearSelection()); },
    selectNode(nodeId: string) { assertActive(); return applyCommandResult(host!.selectNode(nodeId)); },
    connect(command: CanonicalConnectCommand) { assertActive(); return applyCommandResult(host!.connect(command)); },
    disconnect(connectionId: string) { assertActive(); return applyCommandResult(host!.disconnect(connectionId)); },
    patchNodeParameter(command: FlowNodeParameterPatch) {
      assertActive();
      const canonical: CanonicalNodeParameterPatch = command.definition
        ? Object.freeze({
            nodeId: command.nodeId,
            parameterName: command.parameterName,
            value: command.value,
            definition: parameterDefinition(command.definition)
          })
        : Object.freeze({
            nodeId: command.nodeId,
            parameterName: command.parameterName,
            value: command.value
          });
      return applyCommandResult(host!.patchNodeParameter(canonical));
    },
    patchNodeParameters(command: FlowNodeParametersPatch) {
      assertActive();
      const base = {
        nodeId: command.nodeId,
        values: Object.freeze({ ...command.values })
      };
      const canonical: CanonicalNodeParametersPatch = command.definitions
        ? Object.freeze({ ...base, definitions: Object.freeze(command.definitions.map(parameterDefinition)) })
        : Object.freeze(base);
      return applyCommandResult(host!.patchNodeParameters(canonical));
    },
    upsertCaliperSearchRegion(command: FlowCaliperSearchRegionPatch) {
      assertActive();
      const rectangleRegion = state.catalog.operators.find(operator =>
        operator.operatorType.trim().toLowerCase() === 'rectangleregion');
      if (!rectangleRegion) {
        return Object.freeze({
          ok: false,
          code: 'rectangle-region-metadata-missing',
          message: '算子目录缺少 RectangleRegion，无法编辑 CaliperTool.SearchRegion。',
          flowRevision: state.runtime?.flowRevision ?? 0
        });
      }
      const canonical: CanonicalCaliperSearchRegionPatch = Object.freeze({
        caliperNodeId: command.caliperNodeId,
        values: Object.freeze({ ...command.values }),
        rectangleRegion: operatorDefinition(rectangleRegion)
      });
      return applyCommandResult(host!.upsertCaliperSearchRegion(canonical));
    },
    patchNodeProperties(command: FlowNodePropertiesPatch) {
      assertActive();
      const canonical: CanonicalNodePropertiesPatch = Object.freeze({ ...command });
      return applyCommandResult(host!.patchNodeProperties(canonical));
    },
    zoomIn() { assertActive(); return applyCommandResult(host!.zoomBy(1.1)); },
    zoomOut() { assertActive(); return applyCommandResult(host!.zoomBy(0.9)); },
    resetView() { assertActive(); return applyCommandResult(host!.resetView()); },
    focus() { assertActive(); host?.focus(); }
  });

  void catalogQuery.refresh({ force: true });

  const owner: FlowCanvasOwner = Object.freeze({
    projectId: options.project.id,
    projection: readonly(state),
    commands,
    openInspector(): InspectorOwner {
      assertActive();
      if (inspectorOwner) throw new Error(`Inspector owner already exists for project ${options.project.id}.`);
      inspectorOwner = createInspectorOwner({
        project: options.project,
        flowOwner: owner,
        diagnostics: options.diagnostics
      });
      return inspectorOwner;
    },
    openPreviewWorkbench(openedInspector: InspectorOwner): PreviewWorkbenchOwner {
      assertActive();
      if (openedInspector !== inspectorOwner) {
        throw new Error('Preview workbench requires the Flow owner\'s active Inspector owner.');
      }
      if (previewWorkbenchOwner) {
        throw new Error(`Preview workbench owner already exists for project ${options.project.id}.`);
      }
      previewWorkbenchOwner = createPreviewWorkbenchOwner({
        projectId: options.project.id,
        flowOwner: owner,
        inspectorOwner: openedInspector,
        api: options.api,
        diagnostics: options.diagnostics,
        featureFlags: options.featureFlags
      });
      return previewWorkbenchOwner;
    },
    mountCanvas(mountOptions: FlowCanvasMountOptions): void {
      assertActive();
      if (host) throw new FlowCanvasOwnerConflictError(options.project.id);
      try {
        host = createCanonicalFlowCanvasHost({
          canvasId: mountOptions.canvasId,
          initialFlow: canvasFlow,
          operatorLibraryElement: mountOptions.operatorLibraryElement,
          shortcutScopeElement: mountOptions.shortcutScopeElement,
          initialMutationGate: state.mutationGate
        });
        unsubscribeHost = host.subscribe(event => {
          if (disposed || !host) return;
          state.draft = event.projection.draft;
          state.runtime = event.projection.runtime;
          state.feedback = event.projection.feedback;
          state.phase = 'mounted';
          state.error = null;
          syncDiagnostics();
        });
        state.phase = 'mounted';
        host.resize();
        syncDiagnostics();
      } catch (error) {
        state.phase = 'error';
        state.error = error instanceof Error ? error.message : 'FlowCanvas 挂载失败。';
        syncDiagnostics();
        throw error;
      }
    },
    replaceFlow(flow: Readonly<Record<string, unknown>> | null, projectName: string): void {
      assertActive();
      if (!host) throw new Error('FlowCanvas must be mounted before its persistence baseline can be replaced.');
      host.replaceFlow(flow ?? toCanvasFlow(null, projectName));
      const next = host.getProjection();
      state.draft = next.draft;
      state.runtime = next.runtime;
      state.feedback = next.feedback;
      state.phase = 'mounted';
      state.error = null;
      syncDiagnostics();
    },
    async refreshOperators(force = true): Promise<void> {
      assertActive();
      await catalogQuery.refresh({ force });
    },
    setMutationGate(gate: FlowMutationGate): void {
      assertActive();
      state.mutationGate = gate;
      host?.setMutationGate(gate);
      if (host) {
        state.runtime = host.getRuntimeSnapshot();
        syncDiagnostics();
      }
    },
    dispose(reason = 'flow-canvas-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      let disposalError: unknown;
      try {
        previewWorkbenchOwner?.dispose(reason);
      } catch (error) {
        disposalError = error;
      }
      previewWorkbenchOwner = undefined;
      try {
        inspectorOwner?.dispose(reason);
      } catch (error) {
        disposalError ??= error;
      }
      inspectorOwner = undefined;
      try {
        unsubscribeHost?.();
      } catch (error) {
        disposalError ??= error;
      }
      unsubscribeHost = undefined;
      try {
        host?.disposeInteraction();
      } catch (error) {
        disposalError ??= error;
      }
      try {
        host?.disposeAdapter();
      } catch (error) {
        disposalError ??= error;
      }
      host = undefined;
      stopCatalogWatch();
      catalogQuery.dispose();
      state.phase = 'disposed';
      state.runtime = null;
      state.feedback = null;
      diagnosticsLease.update(zeroResources());
      diagnosticsLease.dispose(reason);
      if (disposalError !== undefined) throw disposalError;
    }
  });
  return owner;
}
