<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';
import {
  CvButton,
  CvDataTable,
  CvInlineAlert,
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

const runtime = useProjectsReadRuntime(props.runtime);
const searchDraft = ref('');
const activeSearch = ref('');
const sort = ref<ProjectSort>('modified-desc');
const page = ref(1);
const pageSize = 10;
const listQuery = createProjectsListQuery(runtime.queries, () => activeSearch.value);
const recentQuery = createRecentProjectsQuery(runtime.queries);

const listState = computed(() => listQuery.state.value);
const recentState = computed(() => recentQuery.state.value);
const sortedProjects = computed(() => sortProjects(listState.value.data ?? [], sort.value));
const pageSlice = computed(() => paginateProjects(sortedProjects.value, page.value, pageSize));
const isSearching = computed(() => activeSearch.value.length > 0);
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
  { key: 'actions', label: '操作', align: 'end', width: '12%' }
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

onMounted(() => {
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
      description="浏览服务端工程摘要。此页面不提供创建、保存、删除或编辑入口。"
    >
      <template #actions>
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
            <RouterLink :to="`/projects/${row.id}`">
              查看详情
            </RouterLink>
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
          </li>
        </ol>
      </CvPanel>
    </div>
  </section>
</template>

<style scoped>
.projects-page { display: grid; max-width: 1540px; gap: var(--cv-density-page-gap); min-width: 0; }
.projects-page__layout { display: grid; grid-template-columns: minmax(0, 1fr) minmax(220px, 300px); gap: var(--cv-space-4); align-items: start; }
.projects-page__search { flex: 1 1 320px; }
.projects-page__sort { min-width: 150px; }
.projects-page__notice { margin-bottom: var(--cv-space-3); }
.projects-page__name { color: var(--cv-text-primary); font-weight: var(--cv-font-weight-semibold); }
.projects-page__recent-list { display: grid; gap: 0; margin: 0; padding: 0; list-style: none; }
.projects-page__recent-list li { display: grid; gap: var(--cv-space-1); padding-block: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); }
.projects-page__recent-list li:last-child { border-bottom: 0; }
.projects-page__recent-list span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
@media (max-width: 900px) { .projects-page__layout { grid-template-columns: 1fr; } }
</style>
