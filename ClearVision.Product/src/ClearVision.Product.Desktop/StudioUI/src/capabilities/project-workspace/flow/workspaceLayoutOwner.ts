import { reactive, readonly, type DeepReadonly } from 'vue';

export const workspaceLayoutStorageKey = 'clearvision.studio-ui.workspace-layout.v1';
export const workspaceInspectorMinWidth = 240;
export const workspaceInspectorDefaultWidth = 280;
export const workspaceInspectorMaxWidth = 380;
export const workspacePreviewCollapsedHeight = 38;
export const workspacePreviewMinHeight = 160;
export const workspacePreviewDefaultHeight = 220;
export const workspacePreviewMaxHeight = 420;
export const workspacePreviewCollapsedWidth = 44;
export const workspacePreviewMinWidth = 300;
export const workspacePreviewDefaultWidth = 340;
export const workspacePreviewMaxWidth = 480;

const workspaceSplitterSize = 8;
const workspaceWideOperatorWidth = 56;
const workspaceCompactOperatorWidth = 56;
const workspaceWideCanvasMinWidth = 680;
const workspaceCompactCanvasMinWidth = 560;
const workspaceCanvasSurfaceMinHeight = 352;

export interface WorkspaceLayoutProjection {
  readonly containerWidth: number;
  readonly containerHeight: number;
  readonly inspectorWidth: number;
  readonly inspectorMinWidth: number;
  readonly inspectorMaxWidth: number;
  readonly previewHeight: number;
  readonly previewMinHeight: number;
  readonly previewMaxHeight: number;
  readonly previewCollapsed: boolean;
  readonly previewWidth: number;
  readonly previewMinWidth: number;
  readonly previewMaxWidth: number;
}

type MutableWorkspaceLayoutProjection = {
  -readonly [Key in keyof WorkspaceLayoutProjection]: WorkspaceLayoutProjection[Key]
};

export interface WorkspaceLayoutObserver {
  observe(element: Element): void;
  disconnect(): void;
}

export interface WorkspaceLayoutOwnerOptions {
  readonly storage?: Pick<Storage, 'getItem' | 'setItem'>;
  readonly createObserver?: (
    onSize: (width: number, height: number) => void
  ) => WorkspaceLayoutObserver | null;
}

export interface WorkspaceLayoutOwner {
  readonly projection: DeepReadonly<WorkspaceLayoutProjection>;
  attach(element: HTMLElement): void;
  setInspectorWidth(width: number): void;
  setPreviewHeight(height: number): void;
  setPreviewWidth(width: number): void;
  setPreviewCollapsed(collapsed: boolean): void;
  togglePreviewCollapsed(): void;
  commit(): void;
  dispose(): void;
}

interface StoredWorkspaceLayout {
  readonly inspectorWidth: number;
  readonly previewHeight: number;
  readonly previewWidth: number;
  readonly previewCollapsed: boolean;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, Math.round(value)));
}

function readStoredLayout(storage?: Pick<Storage, 'getItem'>): StoredWorkspaceLayout {
  try {
    const raw = storage?.getItem(workspaceLayoutStorageKey);
    if (!raw) throw new Error('Workspace layout preference is empty.');
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    return {
      inspectorWidth: typeof parsed.inspectorWidth === 'number'
        ? clamp(parsed.inspectorWidth, workspaceInspectorMinWidth, workspaceInspectorMaxWidth)
        : workspaceInspectorDefaultWidth,
      previewHeight: typeof parsed.previewHeight === 'number'
        ? clamp(parsed.previewHeight, workspacePreviewMinHeight, workspacePreviewMaxHeight)
        : workspacePreviewDefaultHeight,
      previewWidth: typeof parsed.previewWidth === 'number'
        ? clamp(parsed.previewWidth, workspacePreviewMinWidth, workspacePreviewMaxWidth)
        : workspacePreviewDefaultWidth,
      previewCollapsed: parsed.previewCollapsed === true
    };
  } catch {
    return {
      inspectorWidth: workspaceInspectorDefaultWidth,
      previewHeight: workspacePreviewDefaultHeight,
      previewWidth: workspacePreviewDefaultWidth,
      previewCollapsed: false
    };
  }
}

function createNativeObserver(
  onSize: (width: number, height: number) => void
): WorkspaceLayoutObserver | null {
  const ResizeObserverConstructor = globalThis.ResizeObserver;
  if (!ResizeObserverConstructor) return null;
  return new ResizeObserverConstructor(entries => {
    const entry = entries[0];
    if (!entry) return;
    onSize(entry.contentRect.width, entry.contentRect.height);
  });
}

export function createWorkspaceLayoutOwner(
  options: WorkspaceLayoutOwnerOptions = {}
): WorkspaceLayoutOwner {
  const storage = options.storage ?? globalThis.localStorage;
  const stored = readStoredLayout(storage);
  let preferredInspectorWidth = stored.inspectorWidth;
  let preferredPreviewHeight = stored.previewHeight;
  let preferredPreviewWidth = stored.previewWidth;
  const state = reactive<MutableWorkspaceLayoutProjection>({
    containerWidth: 0,
    containerHeight: 0,
    inspectorWidth: stored.inspectorWidth,
    inspectorMinWidth: workspaceInspectorMinWidth,
    inspectorMaxWidth: workspaceInspectorMaxWidth,
    previewHeight: stored.previewHeight,
    previewMinHeight: workspacePreviewMinHeight,
    previewMaxHeight: workspacePreviewMaxHeight,
    previewCollapsed: stored.previewCollapsed,
    previewWidth: stored.previewWidth,
    previewMinWidth: workspacePreviewMinWidth,
    previewMaxWidth: workspacePreviewMaxWidth
  });
  let observer: WorkspaceLayoutObserver | null = null;
  let attachedElement: HTMLElement | null = null;
  let disposed = false;

  function updateBounds(width: number, height: number): void {
    if (disposed) return;
    state.containerWidth = Math.max(0, Math.round(width));
    state.containerHeight = Math.max(0, Math.round(height));

    const wideLayout = state.containerWidth > 1180;
    const operatorWidth = wideLayout ? workspaceWideOperatorWidth : workspaceCompactOperatorWidth;
    const canvasMinWidth = wideLayout ? workspaceWideCanvasMinWidth : workspaceCompactCanvasMinWidth;
    const availableInspectorWidth = state.containerWidth > 0
      ? state.containerWidth - operatorWidth - workspaceSplitterSize * 2 - canvasMinWidth - state.previewMinWidth
      : workspaceInspectorMaxWidth;
    state.inspectorMaxWidth = clamp(
      availableInspectorWidth,
      workspaceInspectorMinWidth,
      workspaceInspectorMaxWidth
    );

    const availablePreviewHeight = state.containerHeight > 0
      ? state.containerHeight - workspaceSplitterSize - workspaceCanvasSurfaceMinHeight
      : workspacePreviewMaxHeight;
    state.previewMaxHeight = clamp(
      availablePreviewHeight,
      workspacePreviewMinHeight,
      workspacePreviewMaxHeight
    );
    state.inspectorWidth = clamp(
      preferredInspectorWidth,
      state.inspectorMinWidth,
      state.inspectorMaxWidth
    );
    state.previewHeight = clamp(
      preferredPreviewHeight,
      state.previewMinHeight,
      state.previewMaxHeight
    );
    const availablePreviewWidth = state.containerWidth > 0
      ? state.containerWidth - operatorWidth - workspaceSplitterSize * 2 - canvasMinWidth - state.inspectorWidth
      : workspacePreviewMaxWidth;
    state.previewMaxWidth = clamp(
      availablePreviewWidth,
      workspacePreviewMinWidth,
      workspacePreviewMaxWidth
    );
    state.previewWidth = clamp(
      preferredPreviewWidth,
      state.previewMinWidth,
      state.previewMaxWidth
    );
  }

  function updateFromElement(): void {
    if (!attachedElement || disposed) return;
    const bounds = attachedElement.getBoundingClientRect();
    updateBounds(bounds.width, bounds.height);
  }

  function removeFallbackListener(): void {
    globalThis.window?.removeEventListener('resize', updateFromElement);
  }

  function detach(): void {
    observer?.disconnect();
    observer = null;
    removeFallbackListener();
    attachedElement = null;
  }

  function persist(): void {
    try {
      storage?.setItem(workspaceLayoutStorageKey, JSON.stringify({
        schemaVersion: 2,
        inspectorWidth: preferredInspectorWidth,
        previewHeight: preferredPreviewHeight,
        previewWidth: preferredPreviewWidth,
        previewCollapsed: state.previewCollapsed
      }));
    } catch {
      // Workspace layout is optional UI projection; storage failures are non-fatal.
    }
  }

  return Object.freeze({
    projection: readonly(state),
    attach(element: HTMLElement): void {
      if (disposed) return;
      detach();
      attachedElement = element;
      updateFromElement();
      observer = (options.createObserver ?? createNativeObserver)(updateBounds);
      if (observer) {
        observer.observe(element);
      } else {
        globalThis.window?.addEventListener('resize', updateFromElement, { passive: true });
      }
    },
    setInspectorWidth(width: number): void {
      if (disposed) return;
      preferredInspectorWidth = clamp(width, state.inspectorMinWidth, state.inspectorMaxWidth);
      updateBounds(state.containerWidth, state.containerHeight);
    },
    setPreviewHeight(height: number): void {
      if (disposed) return;
      preferredPreviewHeight = clamp(height, state.previewMinHeight, state.previewMaxHeight);
      state.previewHeight = preferredPreviewHeight;
    },
    setPreviewWidth(width: number): void {
      if (disposed) return;
      preferredPreviewWidth = clamp(width, state.previewMinWidth, state.previewMaxWidth);
      updateBounds(state.containerWidth, state.containerHeight);
    },
    setPreviewCollapsed(collapsed: boolean): void {
      if (disposed || state.previewCollapsed === collapsed) return;
      state.previewCollapsed = collapsed;
      persist();
    },
    togglePreviewCollapsed(): void {
      if (disposed) return;
      state.previewCollapsed = !state.previewCollapsed;
      persist();
    },
    commit(): void {
      if (disposed) return;
      persist();
    },
    dispose(): void {
      if (disposed) return;
      persist();
      disposed = true;
      detach();
    }
  });
}
