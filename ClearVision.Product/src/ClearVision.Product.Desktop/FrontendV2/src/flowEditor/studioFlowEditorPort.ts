import type {
  LegacyFlowCanvasAdapter,
  LegacyFlowCanvasSnapshot
} from '@/adapters/legacyModules';

export type FlowEditorDisposition =
  | 'accepted'
  | 'project_mismatch'
  | 'stale_request'
  | 'stale_flow_revision'
  | 'stale_selection'
  | 'node_not_found'
  | 'parameter_not_found'
  | 'disposed';

export interface StudioFlowEditorSnapshot {
  readonly projectId: string | null;
  readonly flowRevision: number;
  readonly selectionRevision: number;
  readonly selectedNodeId: string | null;
  readonly flow: unknown;
  readonly selectedNode: StudioFlowEditorNodeSnapshot | null;
}

export interface StudioFlowEditorNodeSnapshot {
  readonly id: string;
  readonly type: string;
  readonly title: string;
  readonly parameters: readonly StudioFlowEditorParameterSnapshot[];
}

export interface StudioFlowEditorParameterSnapshot {
  readonly name: string;
  readonly displayName: string;
  readonly value: unknown;
  readonly dataType: string;
  readonly minValue?: unknown;
  readonly maxValue?: unknown;
  readonly options?: readonly unknown[];
}

export interface StudioFlowEditorCommandBase {
  readonly projectId: string;
  readonly requestSequence: number;
}

export interface ReplaceFlowCommand extends StudioFlowEditorCommandBase {
  readonly flow: unknown;
  readonly expectedFlowRevision?: number;
}

export interface SelectNodeCommand extends StudioFlowEditorCommandBase {
  readonly nodeId: string | null;
}

export interface PatchParametersCommand extends StudioFlowEditorCommandBase {
  readonly expectedFlowRevision: number;
  readonly expectedSelectionRevision?: number;
  readonly nodeId: string;
  readonly parameters: Readonly<Record<string, unknown>>;
}

export interface FlowEditorCommandResult {
  readonly accepted: boolean;
  readonly disposition: FlowEditorDisposition;
  readonly snapshot: StudioFlowEditorSnapshot;
  readonly missingParameters?: readonly string[];
}

export type StudioFlowEditorSnapshotListener = (snapshot: StudioFlowEditorSnapshot) => void;

export interface StudioFlowEditorPort {
  nextRequestSequence(projectId: string): number;
  getSnapshot(): StudioFlowEditorSnapshot;
  replaceFlow(command: ReplaceFlowCommand): FlowEditorCommandResult;
  selectNode(command: SelectNodeCommand): FlowEditorCommandResult;
  patchParameters(command: PatchParametersCommand): FlowEditorCommandResult;
  subscribeStructure(listener: StudioFlowEditorSnapshotListener): () => void;
  subscribeSelection(listener: StudioFlowEditorSnapshotListener): () => void;
  dispose(): void;
}

export function createStudioFlowEditorPort(adapter: LegacyFlowCanvasAdapter): StudioFlowEditorPort {
  return new StudioFlowEditorPortAdapter(adapter);
}

class StudioFlowEditorPortAdapter implements StudioFlowEditorPort {
  private projectId: string | null = null;
  private readonly maxObservedRequestSequenceByProject = new Map<string, number>();
  private lastAllocatedRequestSequence = 0;
  private disposed = false;
  private readonly unsubscribers = new Set<() => void>();

  constructor(private readonly adapter: LegacyFlowCanvasAdapter) {
  }

  getSnapshot(): StudioFlowEditorSnapshot {
    const snapshot = this.adapter.getSnapshot();
    return this.buildSnapshot(snapshot);
  }

  nextRequestSequence(projectId: string): number {
    if (this.disposed) {
      return this.lastAllocatedRequestSequence + 1;
    }

    const projectMaxObserved = this.maxObservedRequestSequenceByProject.get(projectId) ?? 0;
    this.lastAllocatedRequestSequence = Math.max(this.lastAllocatedRequestSequence, projectMaxObserved) + 1;
    return this.lastAllocatedRequestSequence;
  }

  replaceFlow(command: ReplaceFlowCommand): FlowEditorCommandResult {
    const disposed = this.rejectIfDisposed();
    if (disposed) {
      return disposed;
    }

    const sequenceRejection = this.observeCommandSequence(command.projectId, command.requestSequence);
    if (sequenceRejection) {
      return sequenceRejection;
    }

    if (
      this.projectId === command.projectId &&
      command.expectedFlowRevision !== undefined &&
      command.expectedFlowRevision !== this.getSnapshot().flowRevision
    ) {
      return this.reject('stale_flow_revision');
    }

    this.projectId = command.projectId;
    this.adapter.replaceFlow(deepClone(command.flow));
    return this.accept();
  }

  selectNode(command: SelectNodeCommand): FlowEditorCommandResult {
    const baseRejection = this.validateCommandBase(command);
    if (baseRejection) {
      return baseRejection;
    }

    if (command.nodeId && !snapshotContainsNode(this.adapter.getSnapshot(), command.nodeId)) {
      return this.reject('node_not_found');
    }

    this.adapter.selectNode(command.nodeId);
    return this.accept();
  }

  patchParameters(command: PatchParametersCommand): FlowEditorCommandResult {
    const baseRejection = this.validateCommandBase(command);
    if (baseRejection) {
      return baseRejection;
    }

    const snapshot = this.getSnapshot();
    if (command.expectedFlowRevision !== snapshot.flowRevision) {
      return this.reject('stale_flow_revision');
    }

    if (command.expectedSelectionRevision !== undefined && command.expectedSelectionRevision !== snapshot.selectionRevision) {
      return this.reject('stale_selection');
    }

    if (snapshot.selectedNodeId !== command.nodeId) {
      return this.reject('stale_selection');
    }

    if (!snapshot.selectedNode) {
      return this.reject('node_not_found');
    }

    const result = this.adapter.patchNodeParameters(command.nodeId, deepClone(command.parameters));
    if (!result.updated && result.reason === 'node_not_found') {
      return this.reject('node_not_found');
    }
    if (!result.updated && result.reason === 'parameter_not_found') {
      return this.reject('parameter_not_found', result.missingParameters);
    }

    return this.accept();
  }

  subscribeStructure(listener: StudioFlowEditorSnapshotListener): () => void {
    return this.trackSubscription(this.adapter.subscribeStructure(() => {
      listener(this.getSnapshot());
    }));
  }

  subscribeSelection(listener: StudioFlowEditorSnapshotListener): () => void {
    return this.trackSubscription(this.adapter.subscribeSelection(() => {
      listener(this.getSnapshot());
    }));
  }

  dispose(): void {
    if (this.disposed) {
      return;
    }

    this.disposed = true;
    for (const unsubscribe of [...this.unsubscribers]) {
      unsubscribe();
    }
    this.unsubscribers.clear();
  }

  private validateCommandBase(command: StudioFlowEditorCommandBase): FlowEditorCommandResult | null {
    const disposed = this.rejectIfDisposed();
    if (disposed) {
      return disposed;
    }

    if (!this.projectId || this.projectId !== command.projectId) {
      return this.reject('project_mismatch');
    }

    return this.observeCommandSequence(command.projectId, command.requestSequence);
  }

  private observeCommandSequence(projectId: string, requestSequence: number): FlowEditorCommandResult | null {
    if (!isValidRequestSequence(requestSequence)) {
      return this.reject('stale_request');
    }

    const maxObserved = this.maxObservedRequestSequenceByProject.get(projectId) ?? 0;
    if (requestSequence <= maxObserved) {
      return this.reject('stale_request');
    }

    this.maxObservedRequestSequenceByProject.set(projectId, requestSequence);
    this.lastAllocatedRequestSequence = Math.max(this.lastAllocatedRequestSequence, requestSequence);
    return null;
  }

  private rejectIfDisposed(): FlowEditorCommandResult | null {
    return this.disposed ? this.reject('disposed') : null;
  }

  private accept(): FlowEditorCommandResult {
    return {
      accepted: true,
      disposition: 'accepted',
      snapshot: this.getSnapshot()
    };
  }

  private reject(
    disposition: Exclude<FlowEditorDisposition, 'accepted'>,
    missingParameters?: readonly string[]
  ): FlowEditorCommandResult {
    const result: FlowEditorCommandResult = {
      accepted: false,
      disposition,
      snapshot: this.getSnapshot()
    };
    if (missingParameters) {
      return {
        ...result,
        missingParameters
      };
    }

    return result;
  }

  private buildSnapshot(snapshot: LegacyFlowCanvasSnapshot): StudioFlowEditorSnapshot {
    return {
      projectId: this.projectId,
      flowRevision: snapshot.flowRevision,
      selectionRevision: snapshot.selectionRevision,
      selectedNodeId: snapshot.selectedNodeId,
      flow: deepClone(snapshot.flow),
      selectedNode: normalizeNodeSnapshot(snapshot.selectedNode)
    };
  }

  private trackSubscription(unsubscribe: () => void): () => void {
    if (this.disposed) {
      unsubscribe();
      return () => {};
    }

    this.unsubscribers.add(unsubscribe);
    return () => {
      if (this.unsubscribers.delete(unsubscribe)) {
        unsubscribe();
      }
    };
  }
}

function normalizeNodeSnapshot(node: unknown): StudioFlowEditorNodeSnapshot | null {
  if (!isRecord(node)) {
    return null;
  }

  const id = toSafeString(node.id ?? node.Id);
  if (!id) {
    return null;
  }

  const type = toSafeString(node.type ?? node.Type);
  const title = toSafeString(node.title ?? node.name ?? node.Name, type);
  const rawParameters = Array.isArray(node.parameters)
    ? node.parameters
    : (Array.isArray(node.Parameters) ? node.Parameters : []);

  return {
    id,
    type,
    title,
    parameters: rawParameters
      .filter(isRecord)
      .map(normalizeParameterSnapshot)
      .filter((parameter): parameter is StudioFlowEditorParameterSnapshot => Boolean(parameter?.name))
  };
}

function normalizeParameterSnapshot(parameter: Record<string, unknown>): StudioFlowEditorParameterSnapshot | null {
  const normalized: StudioFlowEditorParameterSnapshot = {
    name: toSafeString(parameter.name ?? parameter.Name),
    displayName: toSafeString(parameter.displayName ?? parameter.DisplayName ?? parameter.name ?? parameter.Name),
    value: deepClone(parameter.value ?? parameter.Value ?? parameter.defaultValue ?? parameter.DefaultValue ?? null),
    dataType: toSafeString(parameter.dataType ?? parameter.DataType ?? parameter.type ?? parameter.Type)
  };
  const minValue = parameter.minValue ?? parameter.MinValue ?? parameter.min ?? parameter.Min;
  const maxValue = parameter.maxValue ?? parameter.MaxValue ?? parameter.max ?? parameter.Max;
  const options = Array.isArray(parameter.options)
    ? deepClone(parameter.options)
    : (Array.isArray(parameter.Options) ? deepClone(parameter.Options) : undefined);

  return {
    ...normalized,
    ...(minValue !== undefined ? { minValue: deepClone(minValue) } : {}),
    ...(maxValue !== undefined ? { maxValue: deepClone(maxValue) } : {}),
    ...(options !== undefined ? { options } : {})
  };
}

function snapshotContainsNode(snapshot: LegacyFlowCanvasSnapshot, nodeId: string): boolean {
  const flow = snapshot.flow;
  if (!isRecord(flow)) {
    return false;
  }

  const operators = Array.isArray(flow.operators)
    ? flow.operators
    : (Array.isArray(flow.Operators) ? flow.Operators : []);

  return operators.some((operator) => isRecord(operator) && toSafeString(operator.id ?? operator.Id) === nodeId);
}

function isValidRequestSequence(requestSequence: number): boolean {
  return Number.isSafeInteger(requestSequence) && requestSequence > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === 'object' && !Array.isArray(value));
}

function toSafeString(value: unknown, fallback = ''): string {
  if (value === null || value === undefined) {
    return fallback;
  }

  if (
    typeof value === 'string' ||
    typeof value === 'number' ||
    typeof value === 'boolean' ||
    typeof value === 'bigint'
  ) {
    return String(value);
  }

  return fallback;
}

function deepClone<T>(value: T): T {
  if (value === null || value === undefined) {
    return value;
  }

  if (typeof structuredClone === 'function') {
    return structuredClone(value);
  }

  return JSON.parse(JSON.stringify(value)) as T;
}
