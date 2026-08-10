<script setup lang="ts">
import { computed, inject, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import { productRuntimeKey } from '@/app/productRuntime';
import {
  CvButton,
  CvDescriptionList,
  CvField,
  CvInlineAlert,
  CvModal,
  CvPageHeader,
  CvPageState,
  CvPanel,
  type CvDescriptionItem
} from '@/design-system';
import { isProjectId } from './projectContracts';
import { createProjectDetailsQuery } from './projectQueries';
import { describeProjectDecision, formatProjectDateTime } from './projectViewModel';
import {
  useProjectsReadRuntime,
  type ProjectsReadRuntime
} from './projectsReadRuntime';

const props = defineProps<{
  projectId?: string;
  runtime?: ProjectsReadRuntime;
  workspaceEnabled?: unknown;
}>();

const route = useRoute();
const router = useRouter();
const productRuntime = inject(productRuntimeKey, null);
const runtime = useProjectsReadRuntime(props.runtime);
const commands = runtime.projectLifecycle ?? productRuntime?.projectLifecycle;
const workspaceEnabled = computed(() => typeof props.workspaceEnabled === 'boolean'
  ? props.workspaceEnabled
  : productRuntime?.workspace.enabled ?? false);
const activeProjectId = computed(() => props.projectId ?? String(route.params.id ?? route.params.projectId ?? ''));
const detailsQuery = createProjectDetailsQuery(runtime.queries, () => activeProjectId.value);
const state = computed(() => detailsQuery.state.value);
const editName = ref('');
const editDescription = ref('');
const editValidationError = ref<string | null>(null);
const deleteOpen = ref(false);
const commandState = computed(() => commands?.projection ?? null);
const commandBusy = computed(() => commandState.value?.phase === 'creating' ||
  commandState.value?.phase === 'updating' || commandState.value?.phase === 'deleting' ||
  commandState.value?.phase === 'reconciling');
const summaryItems = computed<readonly CvDescriptionItem[]>(() => state.value.data
  ? [
      { key: 'name', label: '名称', value: state.value.data.name, span: 2 },
      { key: 'description', label: '描述', value: state.value.data.description, span: 2 },
      { key: 'version', label: '工程版本', value: state.value.data.version },
      { key: 'revision', label: '保存修订', value: state.value.data.persistenceRevision },
      { key: 'created', label: '创建时间', value: formatProjectDateTime(state.value.data.createdAt) },
      { key: 'modified', label: '修改时间', value: formatProjectDateTime(state.value.data.modifiedAt) },
      { key: 'opened', label: '最近打开', value: formatProjectDateTime(state.value.data.lastOpenedAt), span: 2 }
    ]
  : []);
const flowItems = computed<readonly CvDescriptionItem[]>(() => {
  const project = state.value.data;
  if (!project?.flow) return [];
  return [
    { key: 'flow-name', label: '流程名称', value: project.flow.name, span: 2 },
    { key: 'operator-count', label: '算子数量', value: project.flow.operatorCount },
    { key: 'connection-count', label: '连接数量', value: project.flow.connectionCount },
    { key: 'decision', label: '决策配置', value: describeProjectDecision(project), span: 2 }
  ];
});
const assetItems = computed<readonly CvDescriptionItem[]>(() => state.value.data
  ? [
      { key: 'asset-schema', label: '资源 schema', value: `v${state.value.data.assets.schemaVersion}` },
      { key: 'calibration-count', label: '标定资源', value: state.value.data.assets.calibrationAssetCount },
      { key: 'spatial-count', label: '空间资源', value: state.value.data.assets.spatialAssetCount }
    ]
  : []);

function syncEditForm(): void {
  const project = state.value.data;
  if (!project) return;
  editName.value = project.name;
  editDescription.value = project.description ?? '';
  editValidationError.value = null;
}

async function updateProject(): Promise<void> {
  const project = state.value.data;
  if (!commands || !project) return;
  if (!editName.value.trim()) {
    editValidationError.value = '请输入工程名称。';
    return;
  }
  editValidationError.value = null;
  const updated = await commands.updateProject({
    projectId: project.id,
    name: editName.value,
    description: editDescription.value,
    expectedPersistenceRevision: project.persistenceRevision
  });
  if (!updated) return;
  await detailsQuery.refresh({ force: true });
  syncEditForm();
}

async function reloadAfterConflict(): Promise<void> {
  await detailsQuery.refresh({ force: true });
  syncEditForm();
  commands?.reset();
}

async function openWorkspace(): Promise<void> {
  const project = state.value.data;
  if (!commands || !project || !workspaceEnabled.value) return;
  commands.setProjectScope(project.id);
  const opened = await commands.openProject(project.id);
  if (opened) await router.push(`/projects/${project.id}/workspace`);
}

function requestDelete(): void {
  const project = state.value.data;
  if (!commands || !project || commandBusy.value) return;
  commands.setProjectScope(project.id);
  commands.reset();
  deleteOpen.value = true;
}

function closeDelete(): void {
  if (!commandBusy.value) deleteOpen.value = false;
}

async function confirmDelete(): Promise<void> {
  const project = state.value.data;
  if (!commands || !project) return;
  const deleted = await commands.deleteProject({
    projectId: project.id,
    expectedPersistenceRevision: project.persistenceRevision
  });
  if (!deleted) return;
  deleteOpen.value = false;
  runtime.queries.clearProtectedCache('project-deleted');
  await router.replace('/projects');
}

async function reconcileUnknownOutcome(): Promise<void> {
  if (!commands) return;
  const result = await commands.reconcile();
  if (result?.operation.kind === 'delete') {
    deleteOpen.value = false;
    runtime.queries.clearProtectedCache('project-deleted-reconciled');
    await router.replace('/projects');
  }
}

onMounted(() => {
  if (isProjectId(activeProjectId.value)) commands?.setProjectScope(activeProjectId.value);
  void detailsQuery.refresh();
});

watch(activeProjectId, (next, previous) => {
  if (next !== previous) {
    if (isProjectId(next)) commands?.setProjectScope(next);
    void detailsQuery.refresh({ force: true });
  }
});

watch(() => state.value.data, () => syncEditForm());

onBeforeUnmount(() => {
  detailsQuery.dispose();
});
</script>

<template>
  <section
    class="project-details"
    data-capability="projects-read-detail"
  >
    <CvPageHeader
      :title="state.data?.name ?? '工程详情'"
      description="查看工程状态、流程规模、资源与保存版本。"
    >
      <template #breadcrumbs>
        <RouterLink
          class="project-details__back"
          to="/projects"
        >
          ← 返回工程列表
        </RouterLink>
      </template>
      <template #actions>
        <CvButton
          v-if="workspaceEnabled && state.data && commands"
          size="sm"
          variant="primary"
          :loading="commandState?.command === 'open' && commandBusy"
          data-testid="project-detail-open"
          @click="openWorkspace"
        >
          打开工作区
        </CvButton>
        <CvButton
          v-if="state.data && commands"
          size="sm"
          variant="destructive"
          :disabled="commandBusy"
          data-testid="project-detail-delete"
          @click="requestDelete"
        >
          删除
        </CvButton>
        <CvButton
          size="sm"
          :loading="state.isRefreshing"
          loading-label="正在刷新工程详情"
          @click="detailsQuery.refresh({ force: true })"
        >
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

    <CvInlineAlert
      v-if="commandState?.phase === 'conflict'"
      tone="warning"
      title="工程修订或写入冲突"
    >
      {{ commandState.message }}
      <template #actions>
        <CvButton
          size="sm"
          @click="reloadAfterConflict"
        >
          重新读取服务端版本
        </CvButton>
      </template>
    </CvInlineAlert>
    <CvInlineAlert
      v-else-if="commandState?.phase === 'unknown-outcome'"
      tone="warning"
      title="工程操作结果未知"
    >
      {{ commandState.message }}
      <template #actions>
        <CvButton
          v-if="commandState.canReconcile"
          size="sm"
          @click="reconcileUnknownOutcome"
        >
          核对服务端结果
        </CvButton>
        <CvButton
          v-else
          size="sm"
          @click="reloadAfterConflict"
        >
          重新读取工程
        </CvButton>
      </template>
    </CvInlineAlert>
    <CvInlineAlert
      v-else-if="commandState?.phase === 'failed' && commandState.errorCode"
      tone="error"
      title="工程操作未完成"
    >
      {{ commandState.message }}
    </CvInlineAlert>

    <CvInlineAlert
      v-if="state.isRefreshing && state.data"
      tone="info"
    >
      正在刷新，暂时显示上次读取的数据。
    </CvInlineAlert>
    <CvInlineAlert
      v-if="(state.phase === 'stale' || state.phase === 'partial-failure') && state.data"
      tone="warning"
      title="刷新未完成"
    >
      当前显示上次成功读取的数据。
    </CvInlineAlert>

    <CvPageState
      v-if="state.phase === 'loading' && !state.data"
      kind="loading"
      title="正在读取工程详情"
    />
    <CvPageState
      v-else-if="state.phase === 'unauthorized'"
      kind="unauthorized"
      title="当前会话不可用"
      description="会话已失效，请重新登录后再试。"
    />
    <CvPageState
      v-else-if="state.phase === 'forbidden'"
      kind="forbidden"
      title="无权读取此工程"
      description="当前账号没有此工程的读取权限。"
    />
    <CvPageState
      v-else-if="state.phase === 'not-found'"
      kind="not-found"
      title="工程不存在（404）"
      description="该工程可能已被删除，或当前链接无效。"
    >
      <template #actions>
        <RouterLink to="/projects">
          返回工程列表
        </RouterLink>
      </template>
    </CvPageState>
    <CvPageState
      v-else-if="state.phase === 'error'"
      kind="error"
      title="无法读取工程详情"
      :description="state.failure?.message ?? '本地服务未返回有效详情。'"
    >
      <template #actions>
        <CvButton
          size="sm"
          @click="detailsQuery.refresh({ force: true })"
        >
          重试
        </CvButton>
      </template>
    </CvPageState>

    <div
      v-if="state.data"
      class="project-details__grid"
    >
      <CvPanel
        title="工程摘要"
        description="工程信息与当前保存版本。"
        variant="section"
      >
        <CvDescriptionList
          :items="summaryItems"
          label="工程摘要"
        />
      </CvPanel>

      <CvPanel
        v-if="commands"
        title="编辑工程信息"
        description="更新工程名称和说明。"
        variant="section"
      >
        <form
          class="project-details__form"
          @submit.prevent="updateProject"
        >
          <CvField
            v-model="editName"
            label="工程名称"
            required
            autocomplete="off"
            :disabled="commandBusy"
            :error="editValidationError ?? undefined"
          />
          <CvField
            v-model="editDescription"
            label="工程描述"
            autocomplete="off"
            :disabled="commandBusy"
          />
          <CvButton
            type="submit"
            size="sm"
            variant="primary"
            :loading="commandState?.phase === 'updating'"
            loading-label="正在保存工程信息"
            data-testid="project-detail-update"
          >
            保存工程信息
          </CvButton>
        </form>
      </CvPanel>

      <CvPanel
        title="流程摘要"
        description="当前流程规模。"
        variant="section"
      >
        <CvDescriptionList
          v-if="state.data.flow"
          :items="flowItems"
          label="流程摘要"
        />
        <CvPageState
          v-else
          compact
          kind="empty"
          title="详情响应没有提供流程"
        />
      </CvPanel>

      <CvPanel
        title="正式资源摘要"
        description="当前工程的正式资源。"
        variant="section"
      >
        <CvDescriptionList
          :items="assetItems"
          label="正式资源摘要"
        />
      </CvPanel>
    </div>

    <CvModal
      :open="deleteOpen"
      title="删除工程"
      :description="state.data ? `将删除“${state.data.name}”。操作确认后才会返回工程列表。` : undefined"
      size="sm"
      :close-on-backdrop="!commandBusy"
      @close="closeDelete"
    >
      <CvInlineAlert tone="error">
        删除使用当前保存版本；保存版本或运行状态发生冲突时不会自动覆盖。
      </CvInlineAlert>
      <template #footer>
        <CvButton
          size="sm"
          variant="quiet"
          :disabled="commandBusy"
          data-modal-initial-focus
          @click="closeDelete"
        >
          取消
        </CvButton>
        <CvButton
          size="sm"
          variant="destructive"
          :loading="commandState?.phase === 'deleting' || commandState?.phase === 'reconciling'"
          loading-label="正在确认删除"
          data-testid="project-detail-delete-confirm"
          @click="confirmDelete"
        >
          确认删除
        </CvButton>
      </template>
    </CvModal>
  </section>
</template>

<style scoped>
.project-details { display: grid; gap: var(--cv-space-5); min-width: 0; }
.project-details__back { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); text-decoration: none; }
.project-details__back:hover { color: var(--cv-color-link); text-decoration: underline; }
.project-details__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-4); align-items: start; }
.project-details__grid > :first-child { grid-row: span 2; }
.project-details__form { display: grid; gap: var(--cv-space-3); }
.project-details__form .cv-button { justify-self: start; }
@media (max-width: 800px) { .project-details__grid { grid-template-columns: 1fr; } .project-details__grid > :first-child { grid-row: auto; } }
</style>
