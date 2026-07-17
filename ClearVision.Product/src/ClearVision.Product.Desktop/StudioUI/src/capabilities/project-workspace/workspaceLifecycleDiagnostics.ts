import { reactive } from 'vue';

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
  readonly inspectorOwnerCount: number;
  readonly imageCanvasOwnerCount: number;
  readonly roiOwnerCount: number;
  readonly previewOwnerCount: number;
  readonly persistenceOwnerCount: number;
  readonly activeProjectId: string | null;
  readonly activeReadProjectId: string | null;
  readonly totalWorkspaceMounts: number;
  readonly totalWorkspaceDisposals: number;
  readonly totalReadMounts: number;
  readonly totalReadDisposals: number;
  readonly totalInspectorMounts: number;
  readonly totalInspectorDisposals: number;
  readonly totalPersistenceMounts: number;
  readonly totalPersistenceDisposals: number;
  readonly activeInspectorDrafts: number;
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
  readonly ownerKind: 'workspace' | 'read' | 'flow-canvas' | 'inspector' | 'preview' | 'image-canvas' | 'roi' | 'persistence';
  readonly activeProjectId: string;

  constructor(
    ownerKind: 'workspace' | 'read' | 'flow-canvas' | 'inspector' | 'preview' | 'image-canvas' | 'roi' | 'persistence',
    activeProjectId: string
  ) {
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

export interface WorkspaceFlowCanvasDiagnosticsLease {
  readonly projectId: string;
  update(resources: WorkspaceResourceSnapshot): void;
  dispose(reason?: string): void;
}

export interface WorkspaceInspectorDiagnosticsLease {
  readonly projectId: string;
  updateDraftCount(count: number): void;
  dispose(reason?: string): void;
}

export interface WorkspaceCapabilityDiagnosticsLease {
  readonly projectId: string;
  update(resources: WorkspaceResourceSnapshot): void;
  dispose(reason?: string): void;
}

export interface WorkspaceLifecycleDiagnosticsOwner {
  readonly diagnostics: WorkspaceLifecycleDiagnostics;
  reserveRead(projectId: string): WorkspaceReadDiagnosticsLease;
  reserveWorkspaceOwner(projectId: string): WorkspaceOwnerDiagnosticsLease;
  reserveFlowCanvas(projectId: string): WorkspaceFlowCanvasDiagnosticsLease;
  reserveInspector(projectId: string): WorkspaceInspectorDiagnosticsLease;
  reservePreview(projectId: string): WorkspaceCapabilityDiagnosticsLease;
  reserveImageCanvas(projectId: string): WorkspaceCapabilityDiagnosticsLease;
  reserveRoi(projectId: string): WorkspaceCapabilityDiagnosticsLease;
  reservePersistence(projectId: string): WorkspaceCapabilityDiagnosticsLease;
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
  inspectorOwnerCount: number;
  imageCanvasOwnerCount: number;
  roiOwnerCount: number;
  previewOwnerCount: number;
  persistenceOwnerCount: number;
  activeProjectId: string | null;
  activeReadProjectId: string | null;
  totalWorkspaceMounts: number;
  totalWorkspaceDisposals: number;
  totalReadMounts: number;
  totalReadDisposals: number;
  totalInspectorMounts: number;
  totalInspectorDisposals: number;
  totalPersistenceMounts: number;
  totalPersistenceDisposals: number;
  activeInspectorDrafts: number;
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

function assertProjectId(projectId: string): void {
  if (!projectId.trim()) throw new TypeError('Workspace diagnostics require a project id.');
}

export function createWorkspaceLifecycleDiagnosticsOwner(
  options: CreateWorkspaceLifecycleDiagnosticsOptions = {}
): WorkspaceLifecycleDiagnosticsOwner {
  const state = reactive<MutableWorkspaceDiagnosticsState>({
    workspaceOwnerCount: 0,
    flowCanvasOwnerCount: 0,
    inspectorOwnerCount: 0,
    imageCanvasOwnerCount: 0,
    roiOwnerCount: 0,
    previewOwnerCount: 0,
    persistenceOwnerCount: 0,
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
    totalInspectorMounts: 0,
    totalInspectorDisposals: 0,
    totalPersistenceMounts: 0,
    totalPersistenceDisposals: 0,
    activeInspectorDrafts: 0,
    ownerConflictCount: 0,
    lastDisposedProjectId: null,
    lastDisposeReason: null,
    lastDisposedResources: null,
    disposed: false
  });
  let activeReadLease: object | undefined;
  let activeWorkspaceLease: object | undefined;
  let activeFlowCanvasLease: object | undefined;
  let activeInspectorLease: object | undefined;
  let activePreviewLease: object | undefined;
  let activeImageCanvasLease: object | undefined;
  let activeRoiLease: object | undefined;
  let activePersistenceLease: object | undefined;
  let readOwnerSubscriptionActive = false;
  let readRequestActive = false;
  let inspectorSubscriptionActive = false;
  let flowResources: WorkspaceResourceSnapshot = resourceSnapshot(state);
  let previewResources: WorkspaceResourceSnapshot = zeroResources();
  let imageCanvasResources: WorkspaceResourceSnapshot = zeroResources();
  let roiResources: WorkspaceResourceSnapshot = zeroResources();
  let persistenceResources: WorkspaceResourceSnapshot = zeroResources();
  let publishedWindow: WorkspaceDiagnosticsWindow | undefined;

  function recomputeResources(): void {
    const owned = [flowResources, previewResources, imageCanvasResources, roiResources, persistenceResources];
    const sum = (key: keyof WorkspaceResourceSnapshot): number =>
      owned.reduce((total, resources) => total + resources[key], 0);
    state.activeSubscriptions = (readOwnerSubscriptionActive ? 1 : 0) +
      (inspectorSubscriptionActive ? 1 : 0) + sum('activeSubscriptions');
    state.activeTimers = sum('activeTimers');
    state.activeAnimationFrames = sum('activeAnimationFrames');
    state.activeObservers = sum('activeObservers');
    state.activeAbortControllers = (readRequestActive ? 1 : 0) + sum('activeAbortControllers');
    state.activeBlobUrls = sum('activeBlobUrls');
    state.activePreviewArtifactIds = sum('activePreviewArtifactIds');
    state.activeHostSubscriptions = sum('activeHostSubscriptions');
    state.inFlightReads = (readRequestActive ? 1 : 0) + sum('inFlightReads');
    state.inFlightWrites = sum('inFlightWrites');
    state.inFlightPreview = sum('inFlightPreview');
    state.inFlightExecute = sum('inFlightExecute');
  }

  const diagnostics: WorkspaceLifecycleDiagnostics = Object.freeze({
    get workspaceOwnerCount() { return state.workspaceOwnerCount; },
    get flowCanvasOwnerCount() { return state.flowCanvasOwnerCount; },
    get inspectorOwnerCount() { return state.inspectorOwnerCount; },
    get imageCanvasOwnerCount() { return state.imageCanvasOwnerCount; },
    get roiOwnerCount() { return state.roiOwnerCount; },
    get previewOwnerCount() { return state.previewOwnerCount; },
    get persistenceOwnerCount() { return state.persistenceOwnerCount; },
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
    get totalInspectorMounts() { return state.totalInspectorMounts; },
    get totalInspectorDisposals() { return state.totalInspectorDisposals; },
    get totalPersistenceMounts() { return state.totalPersistenceMounts; },
    get totalPersistenceDisposals() { return state.totalPersistenceDisposals; },
    get activeInspectorDrafts() { return state.activeInspectorDrafts; },
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
      readOwnerSubscriptionActive = true;
      recomputeResources();
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
          readRequestActive = true;
          recomputeResources();
          return requestToken;
        },
        settleRequest(requestToken: number): void {
          if (leaseDisposed || activeReadLease !== leaseIdentity || requestToken !== activeRequestToken) {
            return;
          }
          activeRequestToken = 0;
          readRequestActive = false;
          recomputeResources();
        },
        dispose(reason = 'read-disposed'): void {
          if (leaseDisposed) return;
          leaseDisposed = true;
          activeRequestToken = 0;
          if (activeReadLease === leaseIdentity) {
            activeReadLease = undefined;
            state.activeReadProjectId = null;
            readOwnerSubscriptionActive = false;
            readRequestActive = false;
            recomputeResources();
            state.totalReadDisposals += 1;
            state.lastDisposeReason = reason;
          }
        }
      });
    },
    reserveFlowCanvas(projectId: string): WorkspaceFlowCanvasDiagnosticsLease {
      assertActive();
      assertProjectId(projectId);
      if (activeFlowCanvasLease) {
        state.ownerConflictCount += 1;
        throw new WorkspaceOwnerConflictError('flow-canvas', state.activeProjectId ?? projectId);
      }
      const leaseIdentity = {};
      activeFlowCanvasLease = leaseIdentity;
      state.flowCanvasOwnerCount = 1;
      let leaseDisposed = false;

      return Object.freeze({
        projectId,
        update(resources: WorkspaceResourceSnapshot): void {
          if (leaseDisposed || activeFlowCanvasLease !== leaseIdentity || state.disposed) return;
          flowResources = Object.freeze({ ...resources });
          recomputeResources();
        },
        dispose(reason = 'flow-canvas-disposed'): void {
          if (leaseDisposed) return;
          leaseDisposed = true;
          if (activeFlowCanvasLease === leaseIdentity) {
            activeFlowCanvasLease = undefined;
            state.flowCanvasOwnerCount = 0;
            flowResources = zeroResources();
            recomputeResources();
            state.lastDisposeReason = reason;
          }
        }
      });
    },
    reserveInspector(projectId: string): WorkspaceInspectorDiagnosticsLease {
      assertActive();
      assertProjectId(projectId);
      if (activeInspectorLease) {
        state.ownerConflictCount += 1;
        throw new WorkspaceOwnerConflictError('inspector', state.activeProjectId ?? projectId);
      }
      const leaseIdentity = {};
      activeInspectorLease = leaseIdentity;
      inspectorSubscriptionActive = true;
      state.inspectorOwnerCount = 1;
      state.activeInspectorDrafts = 0;
      state.totalInspectorMounts += 1;
      recomputeResources();
      let leaseDisposed = false;

      return Object.freeze({
        projectId,
        updateDraftCount(count: number): void {
          if (leaseDisposed || activeInspectorLease !== leaseIdentity || state.disposed) return;
          state.activeInspectorDrafts = Number.isSafeInteger(count) && count > 0 ? count : 0;
        },
        dispose(reason = 'inspector-disposed'): void {
          if (leaseDisposed) return;
          leaseDisposed = true;
          if (activeInspectorLease === leaseIdentity) {
            activeInspectorLease = undefined;
            inspectorSubscriptionActive = false;
            state.inspectorOwnerCount = 0;
            state.activeInspectorDrafts = 0;
            state.totalInspectorDisposals += 1;
            recomputeResources();
            state.lastDisposeReason = reason;
          }
        }
      });
    },
    reservePreview(projectId: string): WorkspaceCapabilityDiagnosticsLease {
      assertActive();
      assertProjectId(projectId);
      if (activePreviewLease) {
        state.ownerConflictCount += 1;
        throw new WorkspaceOwnerConflictError('preview', state.activeProjectId ?? projectId);
      }
      const leaseIdentity = {};
      activePreviewLease = leaseIdentity;
      state.previewOwnerCount = 1;
      let leaseDisposed = false;
      return Object.freeze({
        projectId,
        update(resources: WorkspaceResourceSnapshot): void {
          if (leaseDisposed || activePreviewLease !== leaseIdentity || state.disposed) return;
          previewResources = Object.freeze({ ...resources });
          recomputeResources();
        },
        dispose(reason = 'preview-disposed'): void {
          if (leaseDisposed) return;
          leaseDisposed = true;
          if (activePreviewLease === leaseIdentity) {
            activePreviewLease = undefined;
            state.previewOwnerCount = 0;
            previewResources = zeroResources();
            recomputeResources();
            state.lastDisposeReason = reason;
          }
        }
      });
    },
    reserveImageCanvas(projectId: string): WorkspaceCapabilityDiagnosticsLease {
      assertActive();
      assertProjectId(projectId);
      if (activeImageCanvasLease) {
        state.ownerConflictCount += 1;
        throw new WorkspaceOwnerConflictError('image-canvas', state.activeProjectId ?? projectId);
      }
      const leaseIdentity = {};
      activeImageCanvasLease = leaseIdentity;
      state.imageCanvasOwnerCount = 1;
      let leaseDisposed = false;
      return Object.freeze({
        projectId,
        update(resources: WorkspaceResourceSnapshot): void {
          if (leaseDisposed || activeImageCanvasLease !== leaseIdentity || state.disposed) return;
          imageCanvasResources = Object.freeze({ ...resources });
          recomputeResources();
        },
        dispose(reason = 'image-canvas-disposed'): void {
          if (leaseDisposed) return;
          leaseDisposed = true;
          if (activeImageCanvasLease === leaseIdentity) {
            activeImageCanvasLease = undefined;
            state.imageCanvasOwnerCount = 0;
            imageCanvasResources = zeroResources();
            recomputeResources();
            state.lastDisposeReason = reason;
          }
        }
      });
    },
    reserveRoi(projectId: string): WorkspaceCapabilityDiagnosticsLease {
      assertActive();
      assertProjectId(projectId);
      if (activeRoiLease) {
        state.ownerConflictCount += 1;
        throw new WorkspaceOwnerConflictError('roi', state.activeProjectId ?? projectId);
      }
      const leaseIdentity = {};
      activeRoiLease = leaseIdentity;
      state.roiOwnerCount = 1;
      let leaseDisposed = false;
      return Object.freeze({
        projectId,
        update(resources: WorkspaceResourceSnapshot): void {
          if (leaseDisposed || activeRoiLease !== leaseIdentity || state.disposed) return;
          roiResources = Object.freeze({ ...resources });
          recomputeResources();
        },
        dispose(reason = 'roi-disposed'): void {
          if (leaseDisposed) return;
          leaseDisposed = true;
          if (activeRoiLease === leaseIdentity) {
            activeRoiLease = undefined;
            state.roiOwnerCount = 0;
            roiResources = zeroResources();
            recomputeResources();
            state.lastDisposeReason = reason;
          }
        }
      });
    },
    reservePersistence(projectId: string): WorkspaceCapabilityDiagnosticsLease {
      assertActive();
      assertProjectId(projectId);
      if (activePersistenceLease) {
        state.ownerConflictCount += 1;
        throw new WorkspaceOwnerConflictError('persistence', state.activeProjectId ?? projectId);
      }
      const leaseIdentity = {};
      activePersistenceLease = leaseIdentity;
      state.persistenceOwnerCount = 1;
      state.totalPersistenceMounts += 1;
      let leaseDisposed = false;
      return Object.freeze({
        projectId,
        update(resources: WorkspaceResourceSnapshot): void {
          if (leaseDisposed || activePersistenceLease !== leaseIdentity || state.disposed) return;
          persistenceResources = Object.freeze({ ...resources });
          recomputeResources();
        },
        dispose(reason = 'persistence-disposed'): void {
          if (leaseDisposed) return;
          leaseDisposed = true;
          if (activePersistenceLease === leaseIdentity) {
            activePersistenceLease = undefined;
            state.persistenceOwnerCount = 0;
            state.totalPersistenceDisposals += 1;
            persistenceResources = zeroResources();
            recomputeResources();
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
      activeFlowCanvasLease = undefined;
      activeInspectorLease = undefined;
      activePreviewLease = undefined;
      activeImageCanvasLease = undefined;
      activeRoiLease = undefined;
      activePersistenceLease = undefined;
      readOwnerSubscriptionActive = false;
      readRequestActive = false;
      inspectorSubscriptionActive = false;
      state.workspaceOwnerCount = 0;
      state.flowCanvasOwnerCount = 0;
      state.inspectorOwnerCount = 0;
      state.imageCanvasOwnerCount = 0;
      state.roiOwnerCount = 0;
      state.previewOwnerCount = 0;
      state.persistenceOwnerCount = 0;
      state.activeInspectorDrafts = 0;
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
