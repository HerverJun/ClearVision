declare module '@clearvision/canonical-flow-canvas' {
  export class FlowCanvasAdapter {
    readonly raw: unknown;
    readonly disposed: boolean;
    serialize(): unknown;
    replaceFlow(flow: unknown): unknown;
    patchNodeParameters(
      nodeId: string,
      parameterPatch: Readonly<Record<string, unknown>>,
      options?: Readonly<Record<string, unknown>>
    ): Readonly<{ updated: boolean; reason: string; missingParameters: readonly string[] }>;
    patchNodeProperties(
      nodeId: string,
      propertyPatch: Readonly<{ name?: string; isEnabled?: boolean }>
    ): Readonly<{ updated: boolean; reason: string }>;
    selectNode(nodeId: string | null): boolean;
    resize(): unknown;
    render(): unknown;
    subscribeStructureState(listener: (state: unknown) => void): () => void;
    subscribeViewState(listener: (state: unknown) => void): () => void;
    subscribeSelection(listener: (state: unknown) => void): () => void;
    dispose(): void;
  }

  export function createHostedFlowCanvasAdapter(
    canvasId: string,
    options?: Readonly<Record<string, unknown>>
  ): FlowCanvasAdapter;
}

declare module '@clearvision/canonical-flow-interaction' {
  export class FlowEditorInteraction {
    readonly disposed: boolean;
    readonly isConnecting: boolean;
    readonly isDraggingNodes: boolean;
    readonly isPanning: boolean;
    readonly isSelecting: boolean;
    readonly multiSelectedNodes: Set<string>;
    readonly cleanup?: readonly unknown[];
    readonly viewStateNotifyRaf?: unknown;
    readonly historyIndex: number;
    readonly history: readonly string[];
    constructor(flowCanvas: unknown, options?: Readonly<Record<string, unknown>>);
    addOperatorNode(type: string | number, x: number, y: number, data?: unknown): unknown;
    selectNode(nodeId: string, options?: Readonly<Record<string, unknown>>): void;
    selectAll(): void;
    clearSelection(options?: Readonly<Record<string, unknown>>): void;
    copySelectedNodes(): void;
    pasteNodes(): boolean;
    deleteSelectedItems(): boolean;
    duplicateNodeFromCanvasRequest(nodeId: string): boolean;
    saveState(options?: Readonly<Record<string, unknown>>): void;
    resetHistory(options?: Readonly<Record<string, unknown>>): void;
    getHistoryState(): Readonly<{ canUndo: boolean; canRedo: boolean }>;
    undo(): boolean;
    redo(): boolean;
    resetTransientInteractionAfterRestore(): void;
    destroy(): void;
  }
}
