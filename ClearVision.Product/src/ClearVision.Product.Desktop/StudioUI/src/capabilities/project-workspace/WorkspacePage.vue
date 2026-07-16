<script setup lang="ts">
import {
  computed,
  onBeforeUnmount,
  ref,
  shallowRef,
  watch
} from 'vue';
import { useRoute } from 'vue-router';
import { useProductRuntime } from '@/app/productRuntime';
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
}>();

const route = useRoute();
const runtime = props.runtime ?? useProductRuntime().workspace;
const activeProjectId = computed(() => props.projectId ?? String(route.params.id ?? ''));
const shellState = ref<WorkspaceShellState>('loading');
const project = shallowRef<WorkspaceProjectV1 | null>(null);
const message = ref<string | null>(null);
let lifecycleGeneration = 0;
let readPort: WorkspaceProjectReadPort | undefined;
let workspaceOwner: WorkspaceOwner | undefined;

function disposeActive(reason: string): void {
  lifecycleGeneration += 1;
  const previousRead = readPort;
  const previousOwner = workspaceOwner;
  readPort = undefined;
  workspaceOwner = undefined;
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
    message.value = 'Workspace route 中的 project id 不是有效的非空 UUID。';
    return;
  }

  const sessionPhase = runtime.session.phase;
  if (sessionPhase === 'loading') {
    shellState.value = 'loading';
    message.value = '正在等待唯一 session owner 完成 GET /api/auth/me。';
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
  let nextRead: WorkspaceProjectReadPort;
  try {
    nextRead = runtime.openProject(projectId);
    readPort = nextRead;
  } catch (error) {
    shellState.value = 'error';
    message.value = error instanceof Error ? error.message : 'Workspace read owner 冲突。';
    return;
  }

  const pendingRead = nextRead.refresh({ force: true });
  message.value = '正在执行唯一 GET /api/projects/{id} 读取；未创建 Workspace owner。';
  const result = await pendingRead;
  if (generation !== lifecycleGeneration || readPort !== nextRead) return;

  if ((result.phase === 'success' || result.phase === 'empty') && result.data) {
    try {
      workspaceOwner = runtime.mountProject(result.data);
      project.value = result.data;
      shellState.value = workspaceOwner.projection.phase === 'empty' ? 'empty' : 'ready';
      message.value = null;
    } catch (error) {
      nextRead.dispose('workspace-owner-conflict');
      readPort = undefined;
      shellState.value = 'error';
      message.value = error instanceof Error ? error.message : 'Workspace owner 冲突。';
    }
    return;
  }

  if (result.phase === 'partial-failure' && result.data) {
    try {
      workspaceOwner = runtime.mountProject(result.data);
      project.value = result.data;
      workspaceOwner.setReadonly(result.failure?.message ?? '刷新被后端拒绝。');
      shellState.value = 'readonly';
      message.value = workspaceOwner.projection.readonlyReason;
    } catch (error) {
      shellState.value = 'error';
      message.value = error instanceof Error ? error.message : 'Workspace owner 冲突。';
    }
    return;
  }

  shellState.value = projectFailureState();
  message.value = shellState.value === 'decode-error'
    ? `${result.failure?.message ?? '服务响应不符合冻结合同。'} 未生成伪 Flow。`
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

onBeforeUnmount(() => {
  disposeActive('route-leave');
});
</script>

<template>
  <WorkspaceShell
    :state="shellState"
    :project-id="activeProjectId"
    :project="project"
    :message="message"
    :diagnostics="runtime.diagnostics"
    @retry="retry"
    @refresh-session="refreshSession"
  />
</template>
