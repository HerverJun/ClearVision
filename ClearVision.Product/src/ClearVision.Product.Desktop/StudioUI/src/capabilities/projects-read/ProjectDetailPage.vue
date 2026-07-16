<script setup lang="ts">
import { computed, inject, onBeforeUnmount, onMounted, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { productRuntimeKey } from '@/app/productRuntime';
import {
  CvButton,
  CvDescriptionList,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvPanel,
  type CvDescriptionItem
} from '@/design-system';
import { createProjectDetailsQuery } from './projectQueries';
import { describeProjectDecision, formatProjectDateTime } from './projectViewModel';
import {
  useProjectsReadRuntime,
  type ProjectsReadRuntime
} from './projectsReadRuntime';

const props = defineProps<{
  projectId?: string;
  runtime?: ProjectsReadRuntime;
  workspaceEnabled?: boolean;
}>();

const route = useRoute();
const productRuntime = inject(productRuntimeKey, null);
const runtime = useProjectsReadRuntime(props.runtime);
const workspaceEnabled = computed(() => props.workspaceEnabled ?? productRuntime?.workspace.enabled ?? false);
const activeProjectId = computed(() => props.projectId ?? String(route.params.id ?? route.params.projectId ?? ''));
const detailsQuery = createProjectDetailsQuery(runtime.queries, () => activeProjectId.value);
const state = computed(() => detailsQuery.state.value);
const summaryItems = computed<readonly CvDescriptionItem[]>(() => state.value.data
  ? [
      { key: 'name', label: '名称', value: state.value.data.name, span: 2 },
      { key: 'description', label: '描述', value: state.value.data.description, span: 2 },
      { key: 'version', label: '工程版本', value: state.value.data.version },
      { key: 'revision', label: '持久化修订', value: state.value.data.persistenceRevision },
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

onMounted(() => {
  void detailsQuery.refresh();
});

watch(activeProjectId, (next, previous) => {
  if (next !== previous) void detailsQuery.refresh({ force: true });
});

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
      description="详情统计只来自当前工程的只读详情接口，不复用列表中的流程信息。"
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
        <RouterLink
          v-if="workspaceEnabled && state.data"
          class="project-details__workspace-link"
          :to="`/projects/${state.data.id}/workspace`"
        >
          打开工作区
        </RouterLink>
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
      description="请由宿主或测试环境预置有效会话。"
    />
    <CvPageState
      v-else-if="state.phase === 'forbidden'"
      kind="forbidden"
      title="无权读取此工程"
      description="后端权限是唯一安全边界。"
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
        description="服务端工程数据的只读摘要。"
      >
        <CvDescriptionList
          :items="summaryItems"
          label="工程摘要"
        />
      </CvPanel>

      <CvPanel
        title="流程摘要"
        description="算子与连接数量仅由详情响应计算。"
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
        description="仅统计详情响应中的正式工程资源。"
      >
        <CvDescriptionList
          :items="assetItems"
          label="正式资源摘要"
        />
      </CvPanel>
    </div>
  </section>
</template>

<style scoped>
.project-details { display: grid; gap: var(--cv-space-5); min-width: 0; }
.project-details__back { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); text-decoration: none; }
.project-details__back:hover { color: var(--cv-color-link); text-decoration: underline; }
.project-details__workspace-link { min-height: var(--cv-density-control-height-sm); padding: 0 var(--cv-space-3); display: inline-flex; align-items: center; border: 1px solid var(--cv-color-brand-border); border-radius: var(--cv-radius-sm); background: var(--cv-color-brand-soft); color: var(--cv-color-brand-text); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); text-decoration: none; }
.project-details__workspace-link:hover { border-color: var(--cv-color-brand-500); color: var(--cv-color-brand-text); }
.project-details__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-4); align-items: start; }
.project-details__grid > :first-child { grid-row: span 2; }
@media (max-width: 800px) { .project-details__grid { grid-template-columns: 1fr; } .project-details__grid > :first-child { grid-row: auto; } }
</style>
