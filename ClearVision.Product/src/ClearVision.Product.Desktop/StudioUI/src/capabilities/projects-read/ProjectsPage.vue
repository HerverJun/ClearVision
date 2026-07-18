<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink, useRouter } from 'vue-router';
import {
  CvButton,
  CvDataTable,
  CvField,
  CvInlineAlert,
  CvModal,
  CvPageHeader,
  CvPageState,
  CvPagination,
  CvPanel,
  CvSearchField,
  CvSelect,
  CvToolbar,
  type CvDataTableColumn,
  type CvSelectOption
} from '@/design-system';
import {
  createProjectsListQuery,
  createRecentProjectsQuery
} from './projectQueries';
import type { ProjectSummary } from './projectContracts';
import {
  formatProjectDateTime,
  paginateProjects,
  sortProjects,
  type ProjectSort
} from './projectViewModel';
import {
  useProjectsReadRuntime,
  type ProjectsReadRuntime
} from './projectsReadRuntime';

const props = defineProps<{
  runtime?: ProjectsReadRuntime;
}>();

const router = useRouter();
const runtime = useProjectsReadRuntime(props.runtime);
const commands = runtime.projectLifecycle;
const searchDraft = ref('');
const activeSearch = ref('');
const sort = ref<ProjectSort>('modified-desc');
const page = ref(1);
const pageSize = 10;
const listQuery = createProjectsListQuery(runtime.queries, () => activeSearch.value);
const recentQuery = createRecentProjectsQuery(runtime.queries);
const createOpen = ref(false);
const createName = ref('');
const createDescription = ref('');
const createValidationError = ref<string | null>(null);
const deleteTarget = ref<ProjectSummary | null>(null);

const listState = computed(() => listQuery.state.value);
const recentState = computed(() => recentQuery.state.value);
const sortedProjects = computed(() => sortProjects(listState.value.data ?? [], sort.value));
const pageSlice = computed(() => paginateProjects(sortedProjects.value, page.value, pageSize));
const isSearching = computed(() => activeSearch.value.length > 0);
const commandState = computed(() => commands?.projection ?? null);
const commandBusy = computed(() => commandState.value?.phase === 'creating' ||
  commandState.value?.phase === 'updating' || commandState.value?.phase === 'deleting' ||
  commandState.value?.phase === 'reconciling');
const sortModel = computed({
  get: () => sort.value,
  set: value => { sort.value = value as ProjectSort; }
});
const sortOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'modified-desc', label: '最近修改' },
  { value: 'created-desc', label: '最近创建' },
  { value: 'name-asc', label: '名称' }
]);
const columns: readonly CvDataTableColumn<ProjectSummary>[] = Object.freeze([
  { key: 'name', label: '名称', width: '18%' },
  { key: 'description', label: '描述', width: '26%' },
  { key: 'version', label: '版本', width: '10%' },
  { key: 'modifiedAt', label: '修改时间', width: '17%' },
  { key: 'lastOpenedAt', label: '最近打开', width: '17%' },
  { key: 'actions', label: '操作', align: 'end', width: '24%' }
]);

watch([sortedProjects, sort], () => {
  if (page.value > pageSlice.value.pageCount) page.value = pageSlice.value.pageCount;
});

async function submitSearch(): Promise<void> {
  activeSearch.value = searchDraft.value.trim();
  page.value = 1;
  await listQuery.refresh({ force: true });
}

async function clearSearch(): Promise<void> {
  searchDraft.value = '';
  activeSearch.value = '';
  page.value = 1;
  await listQuery.refresh({ force: true });
}

function showCreate(): void {
  if (!commands || commandBusy.value) return;
  commands.setProjectScope(null);
  commands.reset();
  createName.value = '';
  createDescription.value = '';
  createValidationError.value = null;
  createOpen.value = true;
}

function closeCreate(): void {
  if (commandBusy.value) return;
  createOpen.value = false;
}

async function submitCreate(): Promise<void> {
  if (!commands) return;
  if (!createName.value.trim()) {
    createValidationError.value = '请输入工程名称。';
    return;
  }
  createValidationError.value = null;
  const result = await commands.createBlank({
    name: createName.value,
    description: createDescription.value
  });
  if (!result) return;
  createOpen.value = false;
  await Promise.all([
    listQuery.refresh({ force: true }),
    recentQuery.refresh({ force: true })
  ]);
  await router.push(`/projects/${result.projectId}`);
}

async function openWorkspace(project: ProjectSummary): Promise<void> {
  if (!commands) return;
  commands.setProjectScope(project.id);
  const opened = await commands.openProject(project.id);
  if (!opened) return;
  await recentQuery.refresh({ force: true });
  await router.push(`/projects/${project.id}/workspace`);
}

function requestDelete(project: ProjectSummary): void {
  if (!commands || commandBusy.value) return;
  commands.setProjectScope(project.id);
  commands.reset();
  deleteTarget.value = project;
}

function closeDelete(): void {
  if (commandBusy.value) return;
  deleteTarget.value = null;
}

async function confirmDelete(): Promise<void> {
  const project = deleteTarget.value;
  if (!commands || !project) return;
  const result = await commands.deleteProject({
    projectId: project.id,
    expectedPersistenceRevision: project.persistenceRevision
  });
  if (!result) return;
  deleteTarget.value = null;
  await Promise.all([
    listQuery.refresh({ force: true }),
    recentQuery.refresh({ force: true })
  ]);
}

async function reconcileUnknownOutcome(): Promise<void> {
  if (!commands) return;
  const result = await commands.reconcile();
  if (!result) return;
  await Promise.all([
    listQuery.refresh({ force: true }),
    recentQuery.refresh({ force: true })
  ]);
  if (result.operation.kind === 'create') {
    createOpen.value = false;
    await router.push(`/projects/${result.projectId}`);
  } else {
    deleteTarget.value = null;
  }
}

onMounted(() => {
  commands?.setProjectScope(null);
  void listQuery.refresh();
  void recentQuery.refresh();
});

onBeforeUnmount(() => {
  listQuery.dispose();
  recentQuery.dispose();
});
</script>

<template>
  <section
    class="projects-page"
    data-capability="projects-read"
  >
    <CvPageHeader
      eyebrow="工程管理"
      title="工程"
      description="创建空白工程、查看服务端摘要，并通过权威 open/delete command 进入后续任务。"
    >
      <template #actions>
        <CvButton
          v-if="commands"
          size="sm"
          variant="primary"
          data-testid="project-create-open"
          @click="showCreate"
        >
          新建空白工程
        </CvButton>
        <CvButton
          size="sm"
          :loading="listState.isRefreshing"
          loading-label="正在刷新工程"
          @click="listQuery.refresh({ force: true })"
        >
          刷新
        </CvButton>
      </template>
    </CvPageHeader>

    <CvInlineAlert
      v-if="commandState?.phase === 'unknown-outcome'"
      tone="warning"
      title="工程操作结果未知"
      role="status"
    >
      {{ commandState.message }}
      <template #actions>
        <CvButton
          size="sm"
          data-testid="project-command-reconcile"
          @click="reconcileUnknownOutcome"
        >
          查询权威结果
        </CvButton>
      </template>
    </CvInlineAlert>
    <CvInlineAlert
      v-else-if="commandState?.phase === 'conflict'"
      tone="warning"
      title="工程操作冲突"
      role="alert"
    >
      {{ commandState.message }}
    </CvInlineAlert>
    <CvInlineAlert
      v-else-if="commandState?.phase === 'failed' && commandState.errorCode"
      tone="error"
      title="工程操作未完成"
      role="alert"
    >
      {{ commandState.message }}
    </CvInlineAlert>

    <div class="projects-page__layout">
      <CvPanel
        title="全部工程"
        description="列表仅显示稳定摘要字段，不从列表数据推断流程信息。"
      >
        <CvToolbar
          interaction="group"
          label="工程列表工具栏"
        >
          <CvSearchField
            v-model="searchDraft"
            class="projects-page__search"
            label="搜索工程"
            placeholder="按名称或描述搜索"
            clear-label="清除工程搜索"
            :hide-label="false"
            @search="submitSearch"
            @clear="clearSearch"
          />
          <CvButton
            size="sm"
            variant="secondary"
            @click="submitSearch"
          >
            搜索
          </CvButton>
          <template #secondary>
            <CvSelect
              v-model="sortModel"
              class="projects-page__sort"
              label="排序"
              :options="sortOptions"
            />
          </template>
        </CvToolbar>

        <CvInlineAlert
          v-if="listState.isRefreshing && listState.data"
          class="projects-page__notice"
          tone="info"
        >
          正在刷新，暂时显示上次读取的数据。
        </CvInlineAlert>
        <CvInlineAlert
          v-if="(listState.phase === 'stale' || listState.phase === 'partial-failure') && listState.data"
          class="projects-page__notice"
          tone="warning"
          title="刷新未完成"
        >
          当前显示上次成功读取的数据。
        </CvInlineAlert>

        <CvPageState
          v-if="listState.phase === 'loading' && !listState.data"
          kind="loading"
          title="正在读取工程列表"
          description="请稍候，正在读取后端工程摘要。"
        />
        <CvPageState
          v-else-if="listState.phase === 'unauthorized'"
          kind="unauthorized"
          title="当前会话不可用"
          description="请由宿主或测试环境预置有效会话后重试。"
        />
        <CvPageState
          v-else-if="listState.phase === 'forbidden'"
          kind="forbidden"
          title="无权读取工程"
          description="导航可见性不代表后端授权，请联系管理员核对权限。"
        />
        <CvPageState
          v-else-if="listState.phase === 'error' || listState.phase === 'not-found'"
          kind="error"
          title="工程列表读取失败"
          :description="listState.failure?.message ?? '本地服务未返回可用的工程列表。'"
        >
          <template #actions>
            <CvButton
              size="sm"
              @click="listQuery.refresh({ force: true })"
            >
              重试
            </CvButton>
          </template>
        </CvPageState>
        <CvPageState
          v-else-if="listState.phase === 'empty'"
          kind="empty"
          :title="isSearching ? '没有匹配的工程' : '暂无工程'"
          :description="isSearching ? '请调整关键词后重新搜索。' : '后端当前没有返回可浏览的工程。'"
        />

        <CvDataTable
          v-if="pageSlice.totalCount > 0"
          :rows="pageSlice.items"
          :columns="columns"
          row-key="id"
          caption="工程稳定摘要列表"
          :busy="listState.isRefreshing"
        >
          <template #cell-name="{ row }">
            <span class="projects-page__name">{{ row.name }}</span>
          </template>
          <template #cell-description="{ row }">
            {{ row.description || '—' }}
          </template>
          <template #cell-modifiedAt="{ row }">
            {{ formatProjectDateTime(row.modifiedAt) }}
          </template>
          <template #cell-lastOpenedAt="{ row }">
            {{ formatProjectDateTime(row.lastOpenedAt) }}
          </template>
          <template #cell-actions="{ row }">
            <span class="projects-page__actions">
              <RouterLink :to="`/projects/${row.id}`">
                查看详情
              </RouterLink>
              <CvButton
                v-if="commands"
                size="sm"
                variant="quiet"
                :disabled="commandBusy"
                :data-testid="`project-open-${row.id}`"
                @click="openWorkspace(row)"
              >
                打开
              </CvButton>
              <CvButton
                v-if="commands"
                size="sm"
                variant="danger"
                :disabled="commandBusy"
                :data-testid="`project-delete-${row.id}`"
                @click="requestDelete(row)"
              >
                删除
              </CvButton>
            </span>
          </template>
        </CvDataTable>

        <CvPagination
          v-if="pageSlice.totalCount > pageSize"
          v-model:page="page"
          :page-size="pageSize"
          :total-items="pageSlice.totalCount"
          label="工程列表分页"
          previous-label="上一页"
          next-label="下一页"
        />
      </CvPanel>

      <CvPanel
        title="最近工程"
        description="按服务端记录的最近打开时间排列。"
        :level="2"
      >
        <CvPageState
          v-if="recentState.phase === 'loading' && !recentState.data"
          compact
          kind="loading"
          title="正在读取最近工程"
        />
        <CvPageState
          v-else-if="recentState.phase === 'empty'"
          compact
          kind="empty"
          title="暂无最近工程"
        />
        <CvPageState
          v-else-if="recentState.phase === 'unauthorized' || recentState.phase === 'forbidden' || recentState.phase === 'error'"
          compact
          kind="error"
          title="最近工程不可用"
        />
        <ol
          v-if="recentState.data?.length"
          class="projects-page__recent-list"
        >
          <li
            v-for="project in recentState.data"
            :key="project.id"
          >
            <RouterLink :to="`/projects/${project.id}`">
              {{ project.name }}
            </RouterLink>
            <span>{{ formatProjectDateTime(project.lastOpenedAt) }}</span>
            <CvButton
              v-if="commands"
              size="sm"
              variant="quiet"
              :disabled="commandBusy"
              @click="openWorkspace(project)"
            >
              打开工作区
            </CvButton>
          </li>
        </ol>
      </CvPanel>
    </div>

    <CvModal
      :open="createOpen"
      title="新建空白工程"
      description="创建阶段只保存工程记录；不接受 initial Flow、模板或导入资源。"
      :close-on-backdrop="!commandBusy"
      @close="closeCreate"
    >
      <form
        class="projects-page__form"
        @submit.prevent="submitCreate"
      >
        <CvField
          v-model="createName"
          label="工程名称"
          required
          autocomplete="off"
          :disabled="commandBusy"
          :error="createValidationError ?? undefined"
          data-modal-initial-focus
        />
        <CvField
          v-model="createDescription"
          label="工程描述"
          autocomplete="off"
          :disabled="commandBusy"
        />
      </form>
      <template #footer>
        <CvButton
          size="sm"
          variant="quiet"
          :disabled="commandBusy"
          @click="closeCreate"
        >
          取消
        </CvButton>
        <CvButton
          size="sm"
          variant="primary"
          :loading="commandState?.phase === 'creating' || commandState?.phase === 'reconciling'"
          loading-label="正在创建工程"
          data-testid="project-create-submit"
          @click="submitCreate"
        >
          创建
        </CvButton>
      </template>
    </CvModal>

    <CvModal
      :open="deleteTarget !== null"
      title="删除工程"
      :description="deleteTarget ? `将删除“${deleteTarget.name}”。服务端 tombstone 成功后才会从列表移除。` : undefined"
      size="sm"
      :close-on-backdrop="!commandBusy"
      @close="closeDelete"
    >
      <CvInlineAlert tone="error">
        此操作使用当前服务端 revision；冲突时不会自动覆盖或乐观移除。
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
          variant="danger"
          :loading="commandState?.phase === 'deleting' || commandState?.phase === 'reconciling'"
          loading-label="正在确认删除"
          data-testid="project-delete-confirm"
          @click="confirmDelete"
        >
          确认删除
        </CvButton>
      </template>
    </CvModal>
  </section>
</template>

<style scoped>
.projects-page { display: grid; max-width: 1540px; gap: var(--cv-density-page-gap); min-width: 0; }
.projects-page__layout { display: grid; grid-template-columns: minmax(0, 1fr) minmax(220px, 300px); gap: var(--cv-space-4); align-items: start; }
.projects-page__search { flex: 1 1 320px; }
.projects-page__sort { min-width: 150px; }
.projects-page__notice { margin-bottom: var(--cv-space-3); }
.projects-page__name { color: var(--cv-text-primary); font-weight: var(--cv-font-weight-semibold); }
.projects-page__actions { display: inline-flex; align-items: center; justify-content: flex-end; gap: var(--cv-space-1); }
.projects-page__form { display: grid; gap: var(--cv-space-4); }
.projects-page__recent-list { display: grid; gap: 0; margin: 0; padding: 0; list-style: none; }
.projects-page__recent-list li { display: grid; gap: var(--cv-space-1); padding-block: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); }
.projects-page__recent-list li:last-child { border-bottom: 0; }
.projects-page__recent-list span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
@media (max-width: 900px) { .projects-page__layout { grid-template-columns: 1fr; } }
</style>
