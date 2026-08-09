import { nextTick, reactive, readonly, type DeepReadonly } from 'vue';
import type { ApiTransport } from '@/platform/api';
import type { ReadQueryClient } from '@/platform/query';
import {
  encodeWorkspaceFlowDraftUpdateV1,
  encodeWorkspaceHandoffFlowV1,
  type WorkspaceCanvasProjectV1,
  type WorkspaceJsonObject
} from './workspaceContracts';
import {
  createFlowCanvasOwner,
  type FlowCanvasOwner
} from './flow';
import type {
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceOwnerDiagnosticsLease
} from './workspaceLifecycleDiagnostics';
import type {
  WorkspaceHandoffArtifactV1,
  WorkspaceHandoffBuildSummaryV1,
  WorkspaceHandoffSourceV1
} from './handoff/handoffContracts';
import type { WorkspaceHandoffOwnerProjection } from './workspaceOwner';

export type WorkspaceNewDraftSavePhase =
  | 'workspace-staged-unsaved'
  | 'workspace-project-creating'
  | 'workspace-save-unknown-outcome'
  | 'workspace-save-failed';

export interface WorkspaceNewDraftProjection {
  readonly phase: 'ready' | 'disposed';
  readonly project: WorkspaceCanvasProjectV1;
  readonly handoff: WorkspaceHandoffOwnerProjection | null;
  readonly savePhase: WorkspaceNewDraftSavePhase;
  readonly message: string;
  readonly canSave: boolean;
  readonly metadataLocked: boolean;
}

type MutableProjection = {
  -readonly [Key in keyof WorkspaceNewDraftProjection]: WorkspaceNewDraftProjection[Key]
};

export interface WorkspaceNewDraftSaveIntent {
  readonly name: string;
  readonly description: string | null;
  readonly flow: WorkspaceJsonObject;
  readonly source: WorkspaceHandoffSourceV1;
  readonly build: WorkspaceHandoffBuildSummaryV1;
}

export interface WorkspaceNewDraftOwner {
  readonly projectId: null;
  readonly projection: DeepReadonly<WorkspaceNewDraftProjection>;
  openFlowCanvas(): FlowCanvasOwner;
  getFlowCanvasOwner(): FlowCanvasOwner | null;
  isDirty(): boolean;
  setMetadata(input: Readonly<{ name?: string; description?: string | null }>): void;
  stageHandoffDraft(artifact: WorkspaceHandoffArtifactV1): Promise<void>;
  confirmHandoff(source: WorkspaceHandoffSourceV1): void;
  createSaveIntent(): WorkspaceNewDraftSaveIntent;
  markProjectCreating(): void;
  markSaveUnknown(message: string): void;
  markSaveFailed(message: string, allowEdit?: boolean): void;
  discardHandoffDraft(): Promise<void>;
  setReadonly(reason: string): void;
  clearReadonly(): boolean;
  prepareForLeave(): Promise<boolean>;
  dispose(reason?: string): void;
}

const compatibleFlow = Object.freeze({
  status: 'compatible' as const,
  canEncode: true,
  opaquePassthroughPaths: Object.freeze([]),
  blockedPaths: Object.freeze([]),
  readOnlyUnknownPaths: Object.freeze([])
});

export function createWorkspaceNewDraftOwner(options: Readonly<{
  artifactId: string;
  diagnostics: WorkspaceLifecycleDiagnosticsOwner;
  queries: ReadQueryClient;
  api: ApiTransport;
  featureFlags: Readonly<Record<string, boolean>>;
}>): WorkspaceNewDraftOwner {
  const diagnosticsKey = `new-handoff:${options.artifactId}`;
  const lease: WorkspaceOwnerDiagnosticsLease = options.diagnostics.reserveWorkspaceOwner(diagnosticsKey);
  const state = reactive<MutableProjection>({
    phase: 'ready',
    project: Object.freeze({
      id: null,
      name: 'AI 候选工程',
      description: null,
      flow: null
    }),
    handoff: null,
    savePhase: 'workspace-staged-unsaved',
    message: '正在等待 AI 候选进入本地新工程草稿。',
    canSave: false,
    metadataLocked: false
  });
  let disposed = false;
  let readonlyReason: string | null = null;
  let flowOwner: FlowCanvasOwner | undefined;
  let stagedArtifact: WorkspaceHandoffArtifactV1 | null = null;

  function assertActive(): void {
    if (disposed) throw new Error('New Workspace draft owner has been disposed.');
  }

  function assertWritable(): void {
    assertActive();
    if (readonlyReason) throw new Error(readonlyReason);
  }

  return Object.freeze({
    projectId: null,
    projection: readonly(state),
    openFlowCanvas(): FlowCanvasOwner {
      assertActive();
      if (flowOwner) throw new Error('The new Workspace draft already has a FlowCanvas owner.');
      flowOwner = createFlowCanvasOwner({
        project: state.project,
        diagnosticsKey,
        diagnostics: options.diagnostics,
        queries: options.queries,
        api: options.api,
        featureFlags: options.featureFlags
      });
      return flowOwner;
    },
    getFlowCanvasOwner(): FlowCanvasOwner | null {
      return flowOwner ?? null;
    },
    isDirty(): boolean {
      return state.handoff !== null;
    },
    setMetadata(input: Readonly<{ name?: string; description?: string | null }>): void {
      assertWritable();
      if (state.metadataLocked) return;
      const name = input.name === undefined ? state.project.name : input.name;
      const description = input.description === undefined ? state.project.description : input.description;
      state.project = Object.freeze({
        ...state.project,
        name,
        description
      });
      state.canSave = Boolean(state.handoff && name.trim());
    },
    async stageHandoffDraft(artifact: WorkspaceHandoffArtifactV1): Promise<void> {
      assertWritable();
      if (artifact.targetKind !== 'new' || artifact.projectBaseline.projectId !== null ||
          artifact.projectBaseline.persistenceRevision !== null) {
        throw new Error('Only a baseline-free new-project artifact can enter this Workspace draft.');
      }
      if (state.handoff || stagedArtifact) {
        throw new Error('The new Workspace draft already contains an AI candidate.');
      }
      if (!flowOwner || flowOwner.projection.phase !== 'mounted') {
        throw new Error('Canonical FlowCanvas must be mounted before staging a new-project candidate.');
      }
      stagedArtifact = artifact;
      state.handoff = Object.freeze({
        phase: 'workspace-staging',
        source: null,
        build: artifact.build,
        message: '正在把 AI 候选装载到唯一 Workspace owner。'
      });
      flowOwner.replaceFlow(encodeWorkspaceHandoffFlowV1(artifact.candidateFlow), state.project.name);
      await nextTick();
    },
    confirmHandoff(source: WorkspaceHandoffSourceV1): void {
      assertWritable();
      if (!stagedArtifact || !state.handoff || state.handoff.phase !== 'workspace-staging') {
        throw new Error('The new Workspace draft has no staged handoff awaiting confirmation.');
      }
      state.handoff = Object.freeze({
        ...state.handoff,
        phase: 'workspace-staged-unsaved',
        source,
        message: 'AI 候选已进入未落库的新工程草稿，尚未创建或保存工程。'
      });
      state.savePhase = 'workspace-staged-unsaved';
      state.message = '确认工程名称后显式保存；系统才会创建工程并写入候选流程。';
      state.canSave = state.project.name.trim().length > 0;
    },
    createSaveIntent(): WorkspaceNewDraftSaveIntent {
      assertWritable();
      const source = state.handoff?.source;
      if (!stagedArtifact || !source || !flowOwner || !state.canSave) {
        throw new Error('The new Workspace draft is not ready for an explicit save.');
      }
      const flow = encodeWorkspaceFlowDraftUpdateV1(
        { flow: stagedArtifact.candidateFlow, saveCompatibility: compatibleFlow },
        flowOwner.projection.draft,
        { materializedFlowId: stagedArtifact.candidateFlow.id }
      );
      if (!flow) throw new Error('A handoff candidate must contain a canonical Flow.');
      return Object.freeze({
        name: state.project.name.trim(),
        description: state.project.description?.trim() || null,
        flow,
        source,
        build: stagedArtifact.build
      });
    },
    markProjectCreating(): void {
      assertWritable();
      state.savePhase = 'workspace-project-creating';
      state.message = '正在通过既有工程创建 authority 创建空白工程。';
      state.canSave = false;
      state.metadataLocked = true;
      flowOwner?.setMutationGate('readonly');
    },
    markSaveUnknown(message: string): void {
      assertWritable();
      state.savePhase = 'workspace-save-unknown-outcome';
      state.message = message;
      state.canSave = true;
      state.metadataLocked = true;
      flowOwner?.setMutationGate('readonly');
    },
    markSaveFailed(message: string, allowEdit = true): void {
      assertWritable();
      state.savePhase = 'workspace-save-failed';
      state.message = message;
      state.canSave = true;
      state.metadataLocked = !allowEdit;
      flowOwner?.setMutationGate(allowEdit ? 'editable' : 'readonly');
    },
    async discardHandoffDraft(): Promise<void> {
      if (disposed || readonlyReason || !state.handoff) return;
      if (!flowOwner || flowOwner.projection.phase !== 'mounted') {
        throw new Error('Canonical FlowCanvas is unavailable for discarding the new-project candidate.');
      }
      flowOwner.replaceFlow(null, state.project.name);
      await nextTick();
      stagedArtifact = null;
      state.handoff = null;
      state.savePhase = 'workspace-staged-unsaved';
      state.message = 'AI 候选已从本地草稿放弃；未创建正式工程。';
      state.canSave = false;
      state.metadataLocked = false;
    },
    setReadonly(reason: string): void {
      if (disposed) return;
      readonlyReason = reason.trim() || '会话已失效；新工程草稿保持只读。';
      state.canSave = false;
      state.metadataLocked = true;
      flowOwner?.setMutationGate('readonly');
      state.message = readonlyReason;
    },
    clearReadonly(): boolean {
      if (disposed || state.savePhase === 'workspace-project-creating' ||
          state.savePhase === 'workspace-save-unknown-outcome') return false;
      readonlyReason = null;
      state.metadataLocked = false;
      state.canSave = Boolean(state.handoff && state.project.name.trim());
      flowOwner?.setMutationGate('editable');
      state.message = '会话已恢复；新工程草稿仍未保存。';
      return true;
    },
    async prepareForLeave(): Promise<boolean> {
      if (flowOwner && !(await flowOwner.prepareForLeave())) return false;
      return state.savePhase !== 'workspace-project-creating' &&
        state.savePhase !== 'workspace-save-unknown-outcome' &&
        !state.handoff;
    },
    dispose(reason = 'workspace-new-draft-disposed'): void {
      if (disposed) return;
      disposed = true;
      flowOwner?.dispose(reason);
      flowOwner = undefined;
      stagedArtifact = null;
      state.phase = 'disposed';
      state.canSave = false;
      state.metadataLocked = true;
      lease.dispose(reason);
    }
  });
}
