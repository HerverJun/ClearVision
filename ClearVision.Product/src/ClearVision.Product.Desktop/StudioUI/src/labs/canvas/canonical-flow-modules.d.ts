declare module '@clearvision/canonical-flow-canvas' {
  export class FlowCanvasAdapter {
    readonly raw: unknown;
    readonly disposed: boolean;
    serialize(): unknown;
    getSnapshot(): unknown;
    deserialize(flow: unknown): unknown;
    replaceFlow(flow: unknown): unknown;
    resize(): unknown;
    render(): unknown;
    getViewState(): unknown;
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
    readonly multiSelectedNodes: ReadonlySet<string>;
    readonly cleanup?: readonly unknown[];
    readonly viewStateNotifyRaf?: unknown;
    constructor(flowCanvas: unknown, options?: Readonly<Record<string, unknown>>);
    saveState(): void;
    resetTransientInteractionAfterRestore(): void;
    destroy(): void;
  }
}
