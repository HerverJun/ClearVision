<script setup lang="ts">
import {
  computed,
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
import type { WorkspaceProjectReadPort } from './workspaceQueries';
import type { WorkspaceRuntime } from './workspaceRuntime';

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
const shellState = ref<WorkspaceShellState>('loading');
const project = shallowRef<WorkspaceProjectV1 | null>(null);
const activeWorkspaceOwner = shallowRef<WorkspaceOwner | null>(null);
const message = ref<string | null>(null);
let lifecycleGeneration = 0;
let readPort: WorkspaceProjectReadPort | undefined;
let workspaceOwner: WorkspaceOwner | undefined;

function handleSaveShortcut(event: KeyboardEvent): void {
  if (!(event.ctrlKey || event.metaKey) || event.altKey || event.key.toLowerCase() !== 's') return;
  event.preventDefault();
  void workspaceOwner?.save();
}

if (typeof window !== 'undefined') {
  window.addEventListener('keydown', handleSaveShortcut);
}

function disposeActive(reason: string): void {
  lifecycleGeneration += 1;
  const previousRead = readPort;
  const previousOwner = workspaceOwner;
  readPort = undefined;
  workspaceOwner = undefined;
  activeWorkspaceOwner.value = null;
  previousRead?.dispose(reason);
  previousOwner?.dispose(reason);
  project.value = null;
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
  if (!isWorkspaceProjectId(projectId)) {
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
  [activeProjectId, () => runtime.session.phase],
  ([projectId, sessionPhase], [previousProjectId, previousSessionPhase] = ['', 'loading']) => {
    if (projectId === previousProjectId && sessionPhase === previousSessionPhase) return;
    void startLifecycle(projectId === previousProjectId ? 'session-changed' : 'project-changed');
  },
  { immediate: true }
);

watch(
  () => activeWorkspaceOwner.value?.projection.run?.result ?? null,
  result => {
    // Only a completed execution has a Results handoff. Cancelled and failed
    // terminal results remain in Workspace so its mutation gate can settle.
    if (!result || result.outcome.execution !== 'Succeeded' || result.projectId !== activeProjectId.value) return;
    const currentOwner = workspaceOwner;
    if (!currentOwner || currentOwner.projectId !== result.projectId ||
      currentOwner.projection.run?.result?.executionSnapshotId !== result.executionSnapshotId) return;
    void router.push({
      path: '/results',
      query: {
        source: 'local',
        projectId: result.projectId,
        resultId: result.id
      }
    });
  }
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
    :message="message"
    :diagnostics="runtime.diagnostics"
    @retry="retry"
    @refresh-session="refreshSession"
  />
</template>
