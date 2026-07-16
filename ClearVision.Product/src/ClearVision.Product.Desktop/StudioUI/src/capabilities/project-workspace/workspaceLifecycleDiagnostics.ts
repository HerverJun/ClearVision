export interface WorkspaceResourceSnapshot {
  readonly activeSubscriptions: number;
  readonly activeTimers: number;
  readonly activeAnimationFrames: number;
  readonly activeObservers: number;
  readonly activeAbortControllers: number;
  readonly activeBlobUrls: number;
  readonly activePreviewArtifactIds: number;
  readonly activeHostSubscriptions: number;
  readonly inFlightReads: number;
  readonly inFlightWrites: number;
  readonly inFlightPreview: number;
  readonly inFlightExecute: number;
}

export interface WorkspaceLifecycleDiagnostics extends WorkspaceResourceSnapshot {
  readonly workspaceOwnerCount: number;
  readonly flowCanvasOwnerCount: number;
  readonly imageCanvasOwnerCount: number;
  readonly roiOwnerCount: number;
  readonly previewOwnerCount: number;
  readonly activeProjectId: string | null;
  readonly activeReadProjectId: string | null;
  readonly totalWorkspaceMounts: number;
  readonly totalWorkspaceDisposals: number;
  readonly totalReadMounts: number;
  readonly totalReadDisposals: number;
  readonly ownerConflictCount: number;
  readonly lastDisposedProjectId: string | null;
  readonly lastDisposeReason: string | null;
  readonly lastDisposedResources: WorkspaceResourceSnapshot | null;
  readonly disposed: boolean;
}

export interface WorkspaceDiagnosticsWindow {
  readonly __STUDIO_UI_WORKSPACE_DIAGNOSTICS__?: WorkspaceLifecycleDiagnostics;
}

export class WorkspaceOwnerConflictError extends Error {
  readonly ownerKind: 'workspace' | 'read';
  readonly activeProjectId: string;

  constructor(ownerKind: 'workspace' | 'read', activeProjectId: string) {
    super(`A ${ownerKind} owner is already active for project ${activeProjectId}.`);
    this.name = 'WorkspaceOwnerConflictError';
    this.ownerKind = ownerKind;
    this.activeProjectId = activeProjectId;
  }
}

export interface WorkspaceReadDiagnosticsLease {
  readonly projectId: string;
  startRequest(): number;
  settleRequest(requestToken: number): void;
  dispose(reason?: string): void;
}

export interface WorkspaceOwnerDiagnosticsLease {
  readonly projectId: string;
  dispose(reason?: string): void;
}

export interface WorkspaceLifecycleDiagnosticsOwner {
  readonly diagnostics: WorkspaceLifecycleDiagnostics;
  reserveRead(projectId: string): WorkspaceReadDiagnosticsLease;
  reserveWorkspaceOwner(projectId: string): WorkspaceOwnerDiagnosticsLease;
  dispose(): void;
}

export interface CreateWorkspaceLifecycleDiagnosticsOptions {
  readonly runtimeWindow?: WorkspaceDiagnosticsWindow;
  readonly publishToWindow?: boolean;
}

type MutableWorkspaceResourceSnapshot = {
  -readonly [Key in keyof WorkspaceResourceSnapshot]: WorkspaceResourceSnapshot[Key]
};

interface MutableWorkspaceDiagnosticsState extends MutableWorkspaceResourceSnapshot {
  workspaceOwnerCount: number;
  flowCanvasOwnerCount: number;
  imageCanvasOwnerCount: number;
  roiOwnerCount: number;
  previewOwnerCount: number;
  activeProjectId: string | null;
  activeReadProjectId: string | null;
  totalWorkspaceMounts: number;
  totalWorkspaceDisposals: number;
  totalReadMounts: number;
  totalReadDisposals: number;
  ownerConflictCount: number;
  lastDisposedProjectId: string | null;
  lastDisposeReason: string | null;
  lastDisposedResources: WorkspaceResourceSnapshot | null;
  disposed: boolean;
}

function resourceSnapshot(state: MutableWorkspaceDiagnosticsState): WorkspaceResourceSnapshot {
  return Object.freeze({
    activeSubscriptions: state.activeSubscriptions,
    activeTimers: state.activeTimers,
    activeAnimationFrames: state.activeAnimationFrames,
    activeObservers: state.activeObservers,
    activeAbortControllers: state.activeAbortControllers,
    activeBlobUrls: state.activeBlobUrls,
    activePreviewArtifactIds: state.activePreviewArtifactIds,
    activeHostSubscriptions: state.activeHostSubscriptions,
    inFlightReads: state.inFlightReads,
    inFlightWrites: state.inFlightWrites,
    inFlightPreview: state.inFlightPreview,
    inFlightExecute: state.inFlightExecute
  });
}

function assertProjectId(projectId: string): void {
  if (!projectId.trim()) throw new TypeError('Workspace diagnostics require a project id.');
}

export function createWorkspaceLifecycleDiagnosticsOwner(
  options: CreateWorkspaceLifecycleDiagnosticsOptions = {}
): WorkspaceLifecycleDiagnosticsOwner {
  const state: MutableWorkspaceDiagnosticsState = {
    workspaceOwnerCount: 0,
    flowCanvasOwnerCount: 0,
    imageCanvasOwnerCount: 0,
    roiOwnerCount: 0,
    previewOwnerCount: 0,
    activeProjectId: null,
    activeReadProjectId: null,
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
    inFlightExecute: 0,
    totalWorkspaceMounts: 0,
    totalWorkspaceDisposals: 0,
    totalReadMounts: 0,
    totalReadDisposals: 0,
    ownerConflictCount: 0,
    lastDisposedProjectId: null,
    lastDisposeReason: null,
    lastDisposedResources: null,
    disposed: false
  };
  let activeReadLease: object | undefined;
  let activeWorkspaceLease: object | undefined;
  let publishedWindow: WorkspaceDiagnosticsWindow | undefined;

  const diagnostics: WorkspaceLifecycleDiagnostics = Object.freeze({
    get workspaceOwnerCount() { return state.workspaceOwnerCount; },
    get flowCanvasOwnerCount() { return state.flowCanvasOwnerCount; },
    get imageCanvasOwnerCount() { return state.imageCanvasOwnerCount; },
    get roiOwnerCount() { return state.roiOwnerCount; },
    get previewOwnerCount() { return state.previewOwnerCount; },
    get activeProjectId() { return state.activeProjectId; },
    get activeReadProjectId() { return state.activeReadProjectId; },
    get activeSubscriptions() { return state.activeSubscriptions; },
    get activeTimers() { return state.activeTimers; },
    get activeAnimationFrames() { return state.activeAnimationFrames; },
    get activeObservers() { return state.activeObservers; },
    get activeAbortControllers() { return state.activeAbortControllers; },
    get activeBlobUrls() { return state.activeBlobUrls; },
    get activePreviewArtifactIds() { return state.activePreviewArtifactIds; },
    get activeHostSubscriptions() { return state.activeHostSubscriptions; },
    get inFlightReads() { return state.inFlightReads; },
    get inFlightWrites() { return state.inFlightWrites; },
    get inFlightPreview() { return state.inFlightPreview; },
    get inFlightExecute() { return state.inFlightExecute; },
    get totalWorkspaceMounts() { return state.totalWorkspaceMounts; },
    get totalWorkspaceDisposals() { return state.totalWorkspaceDisposals; },
    get totalReadMounts() { return state.totalReadMounts; },
    get totalReadDisposals() { return state.totalReadDisposals; },
    get ownerConflictCount() { return state.ownerConflictCount; },
    get lastDisposedProjectId() { return state.lastDisposedProjectId; },
    get lastDisposeReason() { return state.lastDisposeReason; },
    get lastDisposedResources() { return state.lastDisposedResources; },
    get disposed() { return state.disposed; }
  });

  const shouldPublish = options.publishToWindow ?? typeof window !== 'undefined';
  const runtimeWindow = options.runtimeWindow ?? (
    typeof window === 'undefined' ? undefined : window as unknown as WorkspaceDiagnosticsWindow
  );
  if (shouldPublish && runtimeWindow) {
    const existing = runtimeWindow.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__;
    if (existing && existing !== diagnostics) {
      throw new Error('Workspace lifecycle diagnostics already has a published owner.');
    }
    if (!existing) {
      Object.defineProperty(runtimeWindow, '__STUDIO_UI_WORKSPACE_DIAGNOSTICS__', {
        value: diagnostics,
        writable: false,
        configurable: true,
        enumerable: true
      });
      publishedWindow = runtimeWindow;
    }
  }

  function assertActive(): void {
    if (state.disposed) throw new Error('Workspace lifecycle diagnostics has been disposed.');
  }

  return Object.freeze({
    diagnostics,
    reserveRead(projectId: string): WorkspaceReadDiagnosticsLease {
      assertActive();
      assertProjectId(projectId);
      if (activeReadLease) {
        state.ownerConflictCount += 1;
        throw new WorkspaceOwnerConflictError('read', state.activeReadProjectId ?? projectId);
      }
      const leaseIdentity = {};
      activeReadLease = leaseIdentity;
      state.activeReadProjectId = projectId;
      state.activeSubscriptions = 1;
      state.totalReadMounts += 1;
      let requestSequence = 0;
      let activeRequestToken = 0;
      let leaseDisposed = false;

      return Object.freeze({
        projectId,
        startRequest(): number {
          if (leaseDisposed || state.disposed) {
            throw new Error('The Workspace read diagnostics lease has been disposed.');
          }
          const requestToken = ++requestSequence;
          activeRequestToken = requestToken;
          state.activeAbortControllers = 1;
          state.inFlightReads = 1;
          return requestToken;
        },
        settleRequest(requestToken: number): void {
          if (leaseDisposed || activeReadLease !== leaseIdentity || requestToken !== activeRequestToken) {
            return;
          }
          activeRequestToken = 0;
          state.activeAbortControllers = 0;
          state.inFlightReads = 0;
        },
        dispose(reason = 'read-disposed'): void {
          if (leaseDisposed) return;
          leaseDisposed = true;
          activeRequestToken = 0;
          if (activeReadLease === leaseIdentity) {
            activeReadLease = undefined;
            state.activeReadProjectId = null;
            state.activeSubscriptions = 0;
            state.activeAbortControllers = 0;
            state.inFlightReads = 0;
            state.totalReadDisposals += 1;
            state.lastDisposeReason = reason;
          }
        }
      });
    },
    reserveWorkspaceOwner(projectId: string): WorkspaceOwnerDiagnosticsLease {
      assertActive();
      assertProjectId(projectId);
      if (activeWorkspaceLease) {
        state.ownerConflictCount += 1;
        throw new WorkspaceOwnerConflictError('workspace', state.activeProjectId ?? projectId);
      }
      const leaseIdentity = {};
      activeWorkspaceLease = leaseIdentity;
      state.workspaceOwnerCount = 1;
      state.activeProjectId = projectId;
      state.totalWorkspaceMounts += 1;
      let leaseDisposed = false;

      return Object.freeze({
        projectId,
        dispose(reason = 'workspace-disposed'): void {
          if (leaseDisposed) return;
          leaseDisposed = true;
          if (activeWorkspaceLease === leaseIdentity) {
            activeWorkspaceLease = undefined;
            state.workspaceOwnerCount = 0;
            state.activeProjectId = null;
            state.totalWorkspaceDisposals += 1;
            state.lastDisposedProjectId = projectId;
            state.lastDisposeReason = reason;
            state.lastDisposedResources = resourceSnapshot(state);
          }
        }
      });
    },
    dispose(): void {
      if (state.disposed) return;
      state.disposed = true;
      activeReadLease = undefined;
      activeWorkspaceLease = undefined;
      state.workspaceOwnerCount = 0;
      state.flowCanvasOwnerCount = 0;
      state.imageCanvasOwnerCount = 0;
      state.roiOwnerCount = 0;
      state.previewOwnerCount = 0;
      state.activeProjectId = null;
      state.activeReadProjectId = null;
      state.activeSubscriptions = 0;
      state.activeTimers = 0;
      state.activeAnimationFrames = 0;
      state.activeObservers = 0;
      state.activeAbortControllers = 0;
      state.activeBlobUrls = 0;
      state.activePreviewArtifactIds = 0;
      state.activeHostSubscriptions = 0;
      state.inFlightReads = 0;
      state.inFlightWrites = 0;
      state.inFlightPreview = 0;
      state.inFlightExecute = 0;
      if (publishedWindow?.__STUDIO_UI_WORKSPACE_DIAGNOSTICS__ === diagnostics) {
        Reflect.deleteProperty(publishedWindow, '__STUDIO_UI_WORKSPACE_DIAGNOSTICS__');
      }
      publishedWindow = undefined;
    }
  });
}
