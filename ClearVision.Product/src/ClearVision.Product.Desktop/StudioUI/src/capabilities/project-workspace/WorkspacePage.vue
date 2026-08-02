<script setup lang="ts">
import {
  computed,
  nextTick,
  onBeforeUnmount,
  ref,
  shallowRef,
  watch
} from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useProductRuntime } from '@/app/productRuntime';
import type { ProjectLifecycleCommandOwner } from '@/capabilities/project-lifecycle';
import WorkspaceShell, { type WorkspaceShellState } from './WorkspaceShell.vue';
import {
  isWorkspaceProjectId,
  type WorkspaceProjectV1
} from './workspaceContracts';
import type { WorkspaceOwner } from './workspaceOwner';
import type {
  WorkspaceNewDraftOwner,
  WorkspaceNewDraftSaveIntent
} from './workspaceNewDraftOwner';
import type { WorkspaceProjectReadPort } from './workspaceQueries';
import type { WorkspaceRuntime } from './workspaceRuntime';
import type { WorkspaceHandoffReceivePort } from './handoff/handoffReceivePort';

const props = defineProps<{
  projectId?: string;
  runtime?: WorkspaceRuntime;
  projectLifecycle?: ProjectLifecycleCommandOwner;
}>();

const route = useRoute();
const router = useRouter();
const productRuntime = props.runtime ? null : useProductRuntime();
const runtime = props.runtime ?? productRuntime!.workspace;
const projectLifecycle = props.projectLifecycle ?? productRuntime?.projectLifecycle ?? null;
const activeProjectId = computed(() => props.projectId ?? String(route.params.id ?? ''));
const activeHandoffId = computed(() => typeof route.query.handoff === 'string' ? route.query.handoff : null);
const shellState = ref<WorkspaceShellState>('loading');
const project = shallowRef<WorkspaceProjectV1 | null>(null);
const activeWorkspaceOwner = shallowRef<WorkspaceOwner | null>(null);
const activeNewDraftOwner = shallowRef<WorkspaceNewDraftOwner | null>(null);
const activeHandoffReceiver = shallowRef<WorkspaceHandoffReceivePort | null>(null);
const message = ref<string | null>(null);
let lifecycleGeneration = 0;
let readPort: WorkspaceProjectReadPort | undefined;
let workspaceOwner: WorkspaceOwner | undefined;
let newDraftOwner: WorkspaceNewDraftOwner | undefined;
let handoffReceiver: WorkspaceHandoffReceivePort | undefined;
let pendingNewSaveIntent: WorkspaceNewDraftSaveIntent | null = null;
let pendingCreatedProjectId: string | null = null;
let promotedProjectId: string | null = null;

function handleSaveShortcut(event: KeyboardEvent): void {
  if (!(event.ctrlKey || event.metaKey) || event.altKey || event.key.toLowerCase() !== 's') return;
  event.preventDefault();
  void requestSave();
}

if (typeof window !== 'undefined') {
  window.addEventListener('keydown', handleSaveShortcut);
}

function disposeActive(reason: string): void {
  lifecycleGeneration += 1;
  const previousRead = readPort;
  const previousOwner = workspaceOwner;
  const previousNewDraftOwner = newDraftOwner;
  const previousHandoff = handoffReceiver;
  readPort = undefined;
  workspaceOwner = undefined;
  newDraftOwner = undefined;
  handoffReceiver = undefined;
  activeWorkspaceOwner.value = null;
  activeNewDraftOwner.value = null;
  activeHandoffReceiver.value = null;
  previousHandoff?.dispose(reason);
  previousRead?.dispose(reason);
  previousOwner?.dispose(reason);
  previousNewDraftOwner?.dispose(reason);
  project.value = null;
  pendingNewSaveIntent = null;
  pendingCreatedProjectId = null;
  promotedProjectId = null;
}

async function receiveHandoff(generation: number): Promise<void> {
  const artifactId = activeHandoffId.value;
  const owner = workspaceOwner;
  const draftOwner = newDraftOwner;
  handoffReceiver?.dispose('handoff-replaced');
  handoffReceiver = undefined;
  activeHandoffReceiver.value = null;
  if (!artifactId || !owner && !draftOwner) return;
  const receiver = runtime.openHandoffReceiver();
  handoffReceiver = receiver;
  activeHandoffReceiver.value = receiver;
  await nextTick();
  await nextTick();
  if (generation !== lifecycleGeneration || workspaceOwner !== owner || newDraftOwner !== draftOwner ||
      handoffReceiver !== receiver) return;
  const result = await receiver.receive({
    artifactId,
    targetProjectId: owner?.projectId ?? null,
    isDirty: () => owner?.projection.persistence?.dirty === true || draftOwner?.isDirty() === true,
    baselineMatches: artifact => owner
      ? artifact.targetKind === 'existing' &&
        artifact.projectBaseline.projectId === owner.projectId &&
        artifact.projectBaseline.persistenceRevision === owner.projection.project.persistenceRevision
      : artifact.targetKind === 'new' &&
        artifact.projectBaseline.projectId === null &&
        artifact.projectBaseline.persistenceRevision === null,
    stage: artifact => owner
      ? owner.stageHandoffDraft(artifact)
      : draftOwner!.stageHandoffDraft(artifact)
  });
  if (!result || generation !== lifecycleGeneration || workspaceOwner !== owner || newDraftOwner !== draftOwner) return;
  (owner ?? draftOwner)?.confirmHandoff(result.source);
}

async function mountCreatedProjectDraft(
  projectId: string,
  intent: WorkspaceNewDraftSaveIntent
): Promise<void> {
  const draftOwner = newDraftOwner;
  if (!draftOwner) return;
  if (projectLifecycle) {
    projectLifecycle.setProjectScope(projectId);
    const opened = await projectLifecycle.openProject(projectId);
    if (!opened) {
      draftOwner.markSaveFailed(
        projectLifecycle.projection.message || '正式工程已创建，但访问确认失败；禁止重复创建。',
        false
      );
      return;
    }
  }
  let createdRead: WorkspaceProjectReadPort;
  try {
    createdRead = runtime.openProject(projectId);
  } catch (error) {
    draftOwner.markSaveFailed(
      error instanceof Error ? error.message : '无法读取刚创建的工程。',
      false
    );
    return;
  }
  const result = await createdRead.refresh({ force: true });
  createdRead.dispose('new-project-baseline-loaded');
  if ((result.phase !== 'success' && result.phase !== 'empty') || !result.data || newDraftOwner !== draftOwner) {
    draftOwner.markSaveFailed(
      result.failure?.message ?? '正式工程已创建，但无法读取其保存基线；禁止重复创建。',
      false
    );
    return;
  }

  const previousHandoffReceiver = handoffReceiver;
  handoffReceiver = undefined;
  activeHandoffReceiver.value = null;
  previousHandoffReceiver?.dispose('new-project-authority-created');
  draftOwner.dispose('new-project-authority-created');
  newDraftOwner = undefined;
  activeNewDraftOwner.value = null;
  try {
    workspaceOwner = runtime.mountProject(result.data);
    activeWorkspaceOwner.value = workspaceOwner;
    project.value = result.data;
    shellState.value = workspaceOwner.projection.phase === 'empty' ? 'empty' : 'ready';
    await nextTick();
    await nextTick();
    await workspaceOwner.adoptNewHandoffDraft({
      flow: intent.flow,
      source: intent.source,
      build: intent.build
    });
    const saved = await workspaceOwner.save();
    if (saved.status === 'saved' || saved.status === 'no-op') {
      if (typeof workspaceOwner.hydrateFormalRun === 'function') {
        await workspaceOwner.hydrateFormalRun();
      }
      pendingNewSaveIntent = null;
      pendingCreatedProjectId = null;
      promotedProjectId = projectId;
      try {
        await router.replace({
          name: 'project-workspace',
          params: { id: projectId }
        });
      } catch (error) {
        promotedProjectId = null;
        throw error;
      }
    }
  } catch (error) {
    message.value = error instanceof Error ? error.message : '新工程候选接管失败；本地草稿未被重复提交。';
    shellState.value = 'error';
  }
}

async function requestSave(): Promise<void> {
  if (workspaceOwner) {
    await workspaceOwner.save();
    return;
  }
  const draftOwner = newDraftOwner;
  if (!draftOwner || !projectLifecycle) return;

  if (pendingCreatedProjectId && pendingNewSaveIntent) {
    await mountCreatedProjectDraft(pendingCreatedProjectId, pendingNewSaveIntent);
    return;
  }

  if (draftOwner.projection.savePhase === 'workspace-save-unknown-outcome') {
    const reconciled = await projectLifecycle.reconcile();
    if (reconciled?.operation.kind === 'create' && pendingNewSaveIntent) {
      pendingCreatedProjectId = reconciled.projectId;
      await mountCreatedProjectDraft(reconciled.projectId, pendingNewSaveIntent);
      return;
    }
    if (projectLifecycle.projection.phase === 'unknown-outcome') {
      draftOwner.markSaveUnknown(projectLifecycle.projection.message);
    } else {
      draftOwner.markSaveFailed(projectLifecycle.projection.message);
    }
    return;
  }

  try {
    pendingNewSaveIntent = draftOwner.createSaveIntent();
  } catch (error) {
    draftOwner.markSaveFailed(error instanceof Error ? error.message : '新工程草稿尚未具备保存条件。');
    return;
  }
  draftOwner.markProjectCreating();
  projectLifecycle.setProjectScope(null);
  const created = await projectLifecycle.createBlank({
    name: pendingNewSaveIntent.name,
    description: pendingNewSaveIntent.description
  });
  if (!created) {
    if (projectLifecycle.projection.phase === 'unknown-outcome') {
      draftOwner.markSaveUnknown(projectLifecycle.projection.message);
    } else {
      draftOwner.markSaveFailed(projectLifecycle.projection.message);
      pendingNewSaveIntent = null;
    }
    return;
  }
  pendingCreatedProjectId = created.projectId;
  await mountCreatedProjectDraft(created.projectId, pendingNewSaveIntent);
}

function projectFailureState(): WorkspaceShellState {
  const state = readPort?.state.value;
  if (state?.phase === 'unauthorized') return 'unauthorized';
  if (state?.phase === 'forbidden') return 'forbidden';
  if (state?.phase === 'not-found') return 'not-found';
  if (state?.failure?.kind === 'decode') return 'decode-error';
  return 'error';
}

function openFailureState(): WorkspaceShellState {
  const code = projectLifecycle?.projection.errorCode;
  if (code === 'SESSION_UNAUTHORIZED') return 'unauthorized';
  if (code === 'PROJECT_FORBIDDEN') return 'forbidden';
  if (code === 'PROJECT_NOT_FOUND') return 'not-found';
  return 'error';
}

async function startLifecycle(reason: string): Promise<void> {
  disposeActive(reason);
  const generation = lifecycleGeneration;
  const projectId = activeProjectId.value;
  message.value = null;

  if (!runtime.enabled) {
    shellState.value = 'flag-off';
    return;
  }
  const isNewProjectDraft = projectId === 'new';
  if (!isNewProjectDraft && !isWorkspaceProjectId(projectId)) {
    shellState.value = 'decode-error';
    message.value = '当前链接中的工程标识无效。';
    return;
  }

  const sessionPhase = runtime.session.phase;
  if (sessionPhase === 'loading') {
    shellState.value = 'loading';
    message.value = '正在确认当前会话。';
    return;
  }
  if (sessionPhase === 'unauthorized') {
    shellState.value = 'unauthorized';
    message.value = runtime.session.message;
    return;
  }
  if (sessionPhase === 'error') {
    shellState.value = 'error';
    message.value = runtime.session.message;
    return;
  }

  if (isNewProjectDraft) {
    const artifactId = activeHandoffId.value;
    if (!artifactId) {
      shellState.value = 'decode-error';
      message.value = '新工程工作区只能接收经过验证的 AI handoff artifact。';
      return;
    }
    try {
      newDraftOwner = runtime.mountNewHandoffDraft(artifactId);
      activeNewDraftOwner.value = newDraftOwner;
      shellState.value = 'ready';
      message.value = null;
      await nextTick();
      await receiveHandoff(generation);
    } catch (error) {
      shellState.value = 'error';
      message.value = error instanceof Error ? error.message : '无法创建未落库的新工程草稿。';
    }
    return;
  }

  shellState.value = 'loading';
  if (projectLifecycle) {
    projectLifecycle.setProjectScope(projectId);
    message.value = '正在确认工程访问权限。';
    const opened = await projectLifecycle.openProject(projectId);
    if (generation !== lifecycleGeneration) return;
    if (!opened) {
      shellState.value = openFailureState();
      message.value = projectLifecycle.projection.message;
      return;
    }
  }

  let nextRead: WorkspaceProjectReadPort;
  try {
    nextRead = runtime.openProject(projectId);
    readPort = nextRead;
  } catch (error) {
    shellState.value = 'error';
    message.value = error instanceof Error ? error.message : '工程读取状态冲突，请重试。';
    return;
  }

  const pendingRead = nextRead.refresh({ force: true });
  message.value = '正在读取工程、流程与资源信息。';
  const result = await pendingRead;
  if (generation !== lifecycleGeneration || readPort !== nextRead) return;

  if ((result.phase === 'success' || result.phase === 'empty') && result.data) {
    try {
      workspaceOwner = runtime.mountProject(result.data);
      activeWorkspaceOwner.value = workspaceOwner;
      project.value = result.data;
      shellState.value = workspaceOwner.projection.phase === 'empty' ? 'empty' : 'ready';
      message.value = null;
      await nextTick();
      if (typeof workspaceOwner.hydrateFormalRun === 'function') {
        await workspaceOwner.hydrateFormalRun();
      }
      if (generation !== lifecycleGeneration) return;
      await receiveHandoff(generation);
    } catch (error) {
      nextRead.dispose('workspace-owner-conflict');
      readPort = undefined;
      shellState.value = 'error';
      message.value = error instanceof Error ? error.message : '工程工作区状态冲突，请重试。';
    }
    return;
  }

  if (result.phase === 'partial-failure' && result.data) {
    try {
      workspaceOwner = runtime.mountProject(result.data);
      activeWorkspaceOwner.value = workspaceOwner;
      project.value = result.data;
      workspaceOwner.setReadonly(result.failure?.message ?? '刷新被后端拒绝。');
      shellState.value = 'readonly';
      message.value = workspaceOwner.projection.readonlyReason;
    } catch (error) {
      shellState.value = 'error';
      message.value = error instanceof Error ? error.message : '工程工作区状态冲突，请重试。';
    }
    return;
  }

  shellState.value = projectFailureState();
  message.value = shellState.value === 'decode-error'
    ? `${result.failure?.message ?? '工程数据格式不受支持。'} 未创建临时流程。`
    : result.failure?.message ?? null;
}

async function retry(): Promise<void> {
  await startLifecycle('workspace-retry');
}

async function refreshSession(): Promise<void> {
  await runtime.refreshSession();
  await startLifecycle('workspace-session-refresh');
}

watch(
  [activeProjectId, () => runtime.session.phase, activeHandoffId],
  ([projectId, sessionPhase, handoffId], [previousProjectId, previousSessionPhase, previousHandoffId] = ['', 'loading', null]) => {
    if (promotedProjectId === projectId && previousProjectId === 'new' && workspaceOwner?.projectId === projectId) {
      promotedProjectId = null;
      return;
    }
    if (projectId === previousProjectId && sessionPhase === previousSessionPhase) {
      if (handoffId !== previousHandoffId) void receiveHandoff(lifecycleGeneration);
      return;
    }
    void startLifecycle(projectId === previousProjectId ? 'session-changed' : 'project-changed');
  },
  { immediate: true }
);

onBeforeUnmount(() => {
  if (typeof window !== 'undefined') {
    window.removeEventListener('keydown', handleSaveShortcut);
  }
  disposeActive('route-leave');
});
</script>

<template>
  <WorkspaceShell
    :state="shellState"
    :project-id="activeProjectId"
    :project="project"
    :workspace-owner="activeWorkspaceOwner"
    :new-draft-owner="activeNewDraftOwner"
    :handoff-receive="activeHandoffReceiver?.projection ?? null"
    :message="message"
    :diagnostics="runtime.diagnostics"
    :user-role="runtime.session.user?.role"
    @retry="retry"
    @refresh-session="refreshSession"
    @request-save="requestSave"
  />
</template>
