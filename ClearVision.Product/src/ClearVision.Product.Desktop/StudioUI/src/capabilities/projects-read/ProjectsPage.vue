<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, shallowRef, watch } from 'vue';
import { RouterLink, useRouter } from 'vue-router';
import {
  CvButton,
  CvDataTable,
  CvField,
  CvIconButton,
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
import { CvIcon } from '@/design-system/icons';
import {
  decodeProjectImportDocument,
  type ProjectImportDocument,
  type ProjectImportMode
} from '@/capabilities/project-lifecycle';
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
import ProjectsRecentPanel from './ProjectsRecentPanel.vue';

const props = defineProps<{
  runtime?: ProjectsReadRuntime;
}>();

const router = useRouter();
const runtime = useProjectsReadRuntime(props.runtime);
const commands = runtime.projectLifecycle;
const searchDraft = shallowRef('');
const activeSearch = shallowRef('');
const sort = shallowRef<ProjectSort>('modified-desc');
const page = shallowRef(1);
const pageSize = 10;
const listQuery = createProjectsListQuery(runtime.queries, () => activeSearch.value);
const recentQuery = createRecentProjectsQuery(runtime.queries);
const createOpen = shallowRef(false);
const createName = shallowRef('');
const createDescription = shallowRef('');
const createValidationError = shallowRef<string | null>(null);
const deleteTarget = shallowRef<ProjectSummary | null>(null);
const importPicker = shallowRef<HTMLInputElement | null>(null);
const importOpen = shallowRef(false);
const importFileName = shallowRef<string | null>(null);
const importDocument = shallowRef<ProjectImportDocument | null>(null);
const importFileError = shallowRef<string | null>(null);
const importMode = shallowRef<ProjectImportMode>('CREATE_NEW');
const importTargetId = shallowRef('');
const importAcknowledged = shallowRef(false);

const listState = computed(() => listQuery.state.value);
const recentState = computed(() => recentQuery.state.value);
const sortedProjects = computed(() => sortProjects(listState.value.data ?? [], sort.value));
const pageSlice = computed(() => paginateProjects(sortedProjects.value, page.value, pageSize));
const isSearching = computed(() => activeSearch.value.length > 0);
const commandState = computed(() => commands?.projection ?? null);
const commandBusy = computed(() => commandState.value?.phase === 'creating' ||
  commandState.value?.phase === 'updating' || commandState.value?.phase === 'deleting' ||
  commandState.value?.phase === 'importing' || commandState.value?.phase === 'exporting' ||
  commandState.value?.phase === 'reconciling');
const projectCountLabel = computed(() => {
  if (!listState.value.data) return '工程库';
  return isSearching.value
    ? `${sortedProjects.value.length} 个匹配工程`
    : `${sortedProjects.value.length} 个工程`;
});
const recentCountLabel = computed(() => recentState.value.data?.length
  ? `${recentState.value.data.length} 个最近工程`
  : '暂无最近记录');
const sortModel = computed({
  get: () => sort.value,
  set: value => { sort.value = value as ProjectSort; }
});
const sortOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'modified-desc', label: '最近修改' },
  { value: 'created-desc', label: '最近创建' },
  { value: 'name-asc', label: '名称' }
]);
const importModeModel = computed({
  get: () => importMode.value,
  set: value => {
    importMode.value = value as ProjectImportMode;
    importAcknowledged.value = false;
    if (importMode.value === 'CREATE_NEW') importTargetId.value = '';
  }
});
const importTargetModel = computed({
  get: () => importTargetId.value,
  set: value => {
    importTargetId.value = value;
    importAcknowledged.value = false;
  }
});
const importTarget = computed(() => listState.value.data?.find(project => project.id === importTargetId.value) ?? null);
const importTargetOptions = computed<readonly CvSelectOption[]>(() => [
  { value: '', label: '选择目标工程', disabled: true },
  ...(listState.value.data ?? []).map(project => ({
    value: project.id,
    label: `${project.name}（保存修订 ${project.persistenceRevision}）`
  }))
]);
const importOperatorCount = computed(() => {
  const operators = importDocument.value?.flow.operators;
  return Array.isArray(operators) ? operators.length : 0;
});
const importConnectionCount = computed(() => {
  const connections = importDocument.value?.flow.connections;
  return Array.isArray(connections) ? connections.length : 0;
});
const canSubmitImport = computed(() => Boolean(importDocument.value) && (
  importMode.value === 'CREATE_NEW' || Boolean(importTarget.value && importAcknowledged.value)
));
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

function showImportPicker(): void {
  if (!commands || commandBusy.value) return;
  importPicker.value?.click();
}

function clearImportDraft(): void {
  importOpen.value = false;
  importFileName.value = null;
  importDocument.value = null;
  importMode.value = 'CREATE_NEW';
  importTargetId.value = '';
  importAcknowledged.value = false;
}

async function handleImportFileChange(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0] ?? null;
  input.value = '';
  if (!file) return;
  importFileError.value = null;
  try {
    const raw = await file.text();
    const parsed: unknown = JSON.parse(raw);
    const document = decodeProjectImportDocument(parsed);
    if (!commands) return;
    commands.setProjectScope(null);
    commands.reset();
    importFileName.value = file.name;
    importDocument.value = document;
    importMode.value = 'CREATE_NEW';
    importTargetId.value = '';
    importAcknowledged.value = false;
    importOpen.value = true;
  } catch {
    importFileError.value = '无法读取工程 JSON：文件格式或 schema 不符合 ClearVision 工程导出合同。';
  }
}

function closeImport(): void {
  if (commandBusy.value) return;
  clearImportDraft();
}

async function submitImport(): Promise<void> {
  const document = importDocument.value;
  if (!commands || !document) return;
  const target = importMode.value === 'OVERWRITE_EXISTING' ? importTarget.value : null;
  if (importMode.value === 'OVERWRITE_EXISTING' && (!target || !importAcknowledged.value)) return;
  const result = await commands.importProject({
    mode: importMode.value,
    document,
    ...(target ? {
      targetProjectId: target.id,
      expectedPersistenceRevision: target.persistenceRevision
    } : {})
  });
  if (!result) return;
  clearImportDraft();
  await Promise.all([
    listQuery.refresh({ force: true }),
    recentQuery.refresh({ force: true })
  ]);
  await router.push(`/projects/${result.projectId}`);
}

async function exportProject(project: ProjectSummary): Promise<void> {
  if (!commands || commandBusy.value) return;
  const result = await commands.exportProject(project.id);
  if (!result) return;
  const url = URL.createObjectURL(result.blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = result.fileName;
    anchor.click();
  } finally {
    URL.revokeObjectURL(url);
  }
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
  } else if (result.operation.kind === 'import') {
    clearImportDraft();
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
      title="工程"
      description="管理工程库与最近工作。"
    >
      <template #meta>
        <span class="projects-page__meta">{{ projectCountLabel }}</span>
        <span class="projects-page__meta">{{ recentCountLabel }}</span>
      </template>
      <template #actions>
        <CvButton
          v-if="commands"
          size="sm"
          variant="secondary"
          :disabled="commandBusy"
          data-testid="project-import-open"
          @click="showImportPicker"
        >
          <template #leading>
            <CvIcon
              name="paste"
              size="sm"
            />
          </template>
          导入
        </CvButton>
        <CvButton
          v-if="commands"
          size="sm"
          variant="primary"
          data-testid="project-create-open"
          @click="showCreate"
        >
          <template #leading>
            <CvIcon
              name="plus"
              size="sm"
            />
          </template>
          新建工程
        </CvButton>
        <CvIconButton
          size="sm"
          label="刷新工程列表"
          :loading="listState.isRefreshing"
          @click="listQuery.refresh({ force: true })"
        >
          <CvIcon
            name="refresh"
            size="sm"
          />
        </CvIconButton>
      </template>
    </CvPageHeader>

    <input
      ref="importPicker"
      class="projects-page__file-input"
      type="file"
      tabindex="-1"
      accept="application/json,.json"
      aria-label="选择工程 JSON 文件"
      @change="handleImportFileChange"
    >

    <CvInlineAlert
      v-if="importFileError"
      tone="error"
      title="工程 JSON 无法读取"
      role="alert"
    >
      {{ importFileError }}
    </CvInlineAlert>

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
          核对服务端结果
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

    <div
      class="projects-page__layout"
      :class="{ 'projects-page__layout--empty': listState.phase === 'empty' && !isSearching }"
    >
      <CvPanel
        class="projects-page__library"
        title="全部工程"
        variant="section"
        :padded="false"
      >
        <CvToolbar
          class="projects-page__library-toolbar"
          interaction="group"
          label="工程列表工具栏"
        >
          <CvSearchField
            v-model="searchDraft"
            class="projects-page__search"
            name="projectSearch"
            label="搜索工程"
            placeholder="按名称或描述搜索…"
            clear-label="清除工程搜索"
            :hide-label="true"
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
              name="projectSort"
              label="排序"
              :options="sortOptions"
            />
          </template>
        </CvToolbar>

        <CvInlineAlert
          v-if="listState.isRefreshing && listState.data"
          class="projects-page__notice projects-page__inset"
          tone="info"
        >
          正在刷新，暂时显示上次读取的数据。
        </CvInlineAlert>
        <CvInlineAlert
          v-if="(listState.phase === 'stale' || listState.phase === 'partial-failure') && listState.data"
          class="projects-page__notice projects-page__inset"
          tone="warning"
          title="刷新未完成"
        >
          当前显示上次成功读取的数据。
        </CvInlineAlert>

        <CvPageState
          v-if="listState.phase === 'loading' && !listState.data"
          class="projects-page__state"
          kind="loading"
          title="正在读取工程列表"
          description="正在同步工程库，请稍候。"
        />
        <CvPageState
          v-else-if="listState.phase === 'unauthorized'"
          class="projects-page__state"
          kind="unauthorized"
          title="当前会话不可用"
          description="会话已失效，请重新登录后再试。"
        />
        <CvPageState
          v-else-if="listState.phase === 'forbidden'"
          class="projects-page__state"
          kind="forbidden"
          title="无权读取工程"
          description="当前账号没有工程读取权限，请联系管理员。"
        />
        <CvPageState
          v-else-if="listState.phase === 'error' || listState.phase === 'not-found'"
          class="projects-page__state"
          kind="error"
          title="工程列表读取失败"
          :description="listState.failure?.message ?? '工程列表暂时不可用。'"
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
          class="projects-page__state"
          kind="empty"
          :title="isSearching ? '没有匹配的工程' : '暂无工程'"
          :description="isSearching ? '调整关键词，或清除搜索返回完整工程库。' : '创建工程后即可进入流程工作区。'"
        >
          <template #actions>
            <CvButton
              v-if="isSearching"
              size="sm"
              @click="clearSearch"
            >
              清除搜索
            </CvButton>
            <CvButton
              v-else-if="commands"
              size="sm"
              variant="primary"
              @click="showCreate"
            >
              创建工程
            </CvButton>
          </template>
        </CvPageState>

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
              <RouterLink
                :to="`/projects/${row.id}`"
                :aria-label="`查看详情：${row.name}`"
              >
                详情
              </RouterLink>
              <CvButton
                v-if="commands"
                size="sm"
                variant="secondary"
                :disabled="commandBusy"
                :data-testid="`project-open-${row.id}`"
                @click="openWorkspace(row)"
              >
                打开
              </CvButton>
              <CvButton
                v-if="commands"
                size="sm"
                variant="quiet"
                :disabled="commandBusy"
                :loading="commandState?.phase === 'exporting' && commandState.projectId === row.id"
                loading-label="正在导出"
                :data-testid="`project-export-${row.id}`"
                @click="exportProject(row)"
              >
                <template #leading>
                  <CvIcon
                    name="save"
                    size="sm"
                  />
                </template>
                导出
              </CvButton>
              <CvButton
                v-if="commands"
                size="sm"
                variant="destructive"
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

      <ProjectsRecentPanel
        class="projects-page__recent"
        :phase="recentState.phase"
        :projects="recentState.data ?? null"
        :is-refreshing="recentState.isRefreshing"
        :can-open="Boolean(commands)"
        :busy="commandBusy"
        @open="openWorkspace"
      />
    </div>

    <CvModal
      :open="createOpen"
      title="新建空白工程"
      description="创建一个不含模板、流程或导入资源的新工程。"
      :close-on-backdrop="!commandBusy"
      @close="closeCreate"
    >
      <form
        class="projects-page__form"
        @submit.prevent="submitCreate"
      >
        <CvField
          v-model="createName"
          name="projectName"
          label="工程名称"
          required
          autocomplete="off"
          :disabled="commandBusy"
          :error="createValidationError ?? undefined"
          data-modal-initial-focus
        />
        <CvField
          v-model="createDescription"
          name="projectDescription"
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
      :open="importOpen"
      title="导入工程 JSON"
      description="选择正式导出的 JSON 文件，系统会先校验内容再导入。"
      size="md"
      :close-on-backdrop="!commandBusy"
      @close="closeImport"
    >
      <div class="projects-page__import-form">
        <div class="projects-page__file-summary">
          <span class="projects-page__file-label">文件</span>
          <strong>{{ importFileName }}</strong>
          <span>流程 {{ importOperatorCount }} 个算子，{{ importConnectionCount }} 条连接</span>
        </div>

        <CvSelect
          v-model="importModeModel"
          name="projectImportMode"
          label="导入方式"
          :options="[
            { value: 'CREATE_NEW', label: '新建工程' },
            { value: 'OVERWRITE_EXISTING', label: '覆盖现有工程' }
          ]"
          :disabled="commandBusy"
        />

        <CvSelect
          v-if="importMode === 'OVERWRITE_EXISTING'"
          v-model="importTargetModel"
          name="projectImportTarget"
          label="目标工程"
          hint="保存版本来自当前工程列表；提交时会再次校验。"
          :options="importTargetOptions"
          :disabled="commandBusy"
          required
        />

        <CvInlineAlert
          v-if="importMode === 'CREATE_NEW'"
          tone="info"
          title="新建工程"
        >
          系统会创建新的工程身份，不会沿用导出文件中的源工程标识。
        </CvInlineAlert>
        <CvInlineAlert
          v-else
          tone="warning"
          title="覆盖会替换目标工程内容"
        >
          <template v-if="importTarget">
            将覆盖“{{ importTarget.name }}”的流程、全局变量和正式资源。当前保存版本为
            <strong>{{ importTarget.persistenceRevision }}</strong>；保存修订变化时请求会被拒绝，不会部分写入。
          </template>
          <template v-else>
            请选择目标工程后查看当前保存修订与覆盖影响。
          </template>
        </CvInlineAlert>

        <label
          v-if="importMode === 'OVERWRITE_EXISTING'"
          class="projects-page__acknowledge"
        >
          <input
            v-model="importAcknowledged"
            type="checkbox"
            :disabled="commandBusy || !importTarget"
          >
          <span>我已确认按当前保存版本覆盖目标工程。</span>
        </label>
      </div>
      <template #footer>
        <CvButton
          size="sm"
          variant="quiet"
          :disabled="commandBusy"
          data-modal-initial-focus
          @click="closeImport"
        >
          取消
        </CvButton>
        <CvButton
          size="sm"
          variant="primary"
          :disabled="!canSubmitImport"
          :loading="commandState?.phase === 'importing' || commandState?.phase === 'reconciling'"
          loading-label="正在导入工程"
          data-testid="project-import-submit"
          @click="submitImport"
        >
          导入工程
        </CvButton>
      </template>
    </CvModal>

    <CvModal
      :open="deleteTarget !== null"
      title="删除工程"
      :description="deleteTarget ? `将删除“${deleteTarget.name}”。操作确认后才会从工程库移除。` : undefined"
      size="sm"
      :close-on-backdrop="!commandBusy"
      @close="closeDelete"
    >
      <CvInlineAlert tone="error">
        将按当前保存版本删除；如果工程已被修改，系统会提示冲突且不会覆盖。
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
.projects-page { display: grid; max-width: 1720px; gap: var(--cv-density-page-gap); min-width: 0; }
.projects-page__meta { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-variant-numeric: tabular-nums lining-nums; }
.projects-page__file-input { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); clip-path: inset(50%); pointer-events: none; white-space: nowrap; }
.projects-page__meta + .projects-page__meta::before { margin-right: var(--cv-space-2); color: var(--cv-border-strong); content: '·'; }
.projects-page__layout { display: grid; grid-template-columns: minmax(0, 1fr) minmax(244px, 288px); gap: var(--cv-space-4); align-items: start; }
.projects-page__layout--empty { grid-template-columns: minmax(0, 1fr); }
.projects-page__layout--empty .projects-page__recent { display: none; }
.projects-page__library { background: var(--cv-surface-raised); }
.projects-page__library-toolbar { padding: var(--cv-space-3) var(--cv-density-panel-padding); border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.projects-page__library-toolbar :deep(.cv-toolbar__primary) { flex: 1 1 620px; }
.projects-page__search { flex: 1 1 360px; max-width: 560px; }
.projects-page__sort { min-width: 150px; }
.projects-page__notice { margin-bottom: var(--cv-space-3); }
.projects-page__inset { margin-inline: var(--cv-density-panel-padding); }
.projects-page__state { margin: 0 var(--cv-density-panel-padding) var(--cv-density-panel-padding); }
.projects-page__name { color: var(--cv-text-primary); font-weight: var(--cv-font-weight-semibold); }
.projects-page__actions { display: inline-flex; align-items: center; justify-content: flex-end; gap: var(--cv-space-1); white-space: nowrap; }
.projects-page__actions > a { flex: 0 0 auto; }
.projects-page__form { display: grid; gap: var(--cv-space-4); }
.projects-page__import-form { display: grid; gap: var(--cv-space-4); }
.projects-page__file-summary { display: grid; gap: var(--cv-space-1); padding: var(--cv-space-3); border: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.projects-page__file-summary strong { overflow-wrap: anywhere; color: var(--cv-text-primary); font-weight: var(--cv-font-weight-semibold); }
.projects-page__file-label { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.projects-page__acknowledge { display: flex; align-items: flex-start; gap: var(--cv-space-2); color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); line-height: var(--cv-line-height-normal); }
.projects-page__acknowledge input { width: 16px; height: 16px; flex: 0 0 auto; margin-top: 2px; accent-color: var(--cv-color-brand-500); }
.projects-page :deep(.projects-page__library > .cv-panel__header),
.projects-page :deep(.projects-page__recent > .cv-panel__header) { padding-bottom: var(--cv-space-3); }
@media (max-width: 1040px) { .projects-page__layout { grid-template-columns: 1fr; } }
@media (max-width: 640px) { .projects-page__actions { flex-wrap: wrap; justify-content: flex-start; } }
</style>
