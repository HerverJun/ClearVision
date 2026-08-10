<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, shallowRef } from 'vue';
import { RouterLink } from 'vue-router';
import { useProductRuntime } from '@/app/productRuntime';
import {
  CvButton,
  CvIconButton,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvPanel,
  CvSearchField
} from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import { createProjectsListQuery } from '@/capabilities/projects-read/projectQueries';
import { formatProjectDateTime } from '@/capabilities/projects-read/projectViewModel';

const runtime = useProductRuntime();
const searchDraft = shallowRef('');
const activeSearch = shallowRef('');
const query = createProjectsListQuery(runtime.queries, () => activeSearch.value);
const state = computed(() => query.state.value);
const projects = computed(() => query.state.value.data ?? []);
const isSearching = computed(() => activeSearch.value.length > 0);
const projectCountLabel = computed(() => {
  if (state.value.phase === 'loading' && !state.value.data) return '正在读取工程';
  if (isSearching.value) return `${projects.value.length} 个匹配工程`;
  return `${projects.value.length} 个工程可供选择`;
});

async function submitSearch(): Promise<void> {
  activeSearch.value = searchDraft.value.trim();
  await query.refresh({ force: true });
}

async function clearSearch(): Promise<void> {
  searchDraft.value = '';
  activeSearch.value = '';
  await query.refresh({ force: true });
}

onMounted(() => void query.refresh());
onBeforeUnmount(() => query.dispose());
</script>

<template>
  <section
    class="inspection-projects"
    data-testid="inspection-projects-page"
  >
    <CvPageHeader
      title="连续检测"
      description="选择已保存工程，检查设备与运行条件后开始连续检测。"
    >
      <template #actions>
        <CvIconButton
          size="sm"
          label="刷新可检测工程"
          :loading="state.isRefreshing"
          @click="query.refresh({ force: true })"
        >
          <CvIcon
            name="refresh"
            size="sm"
          />
        </CvIconButton>
      </template>
    </CvPageHeader>
    <CvInlineAlert
      tone="info"
      compact
      title="运行依据"
    >
      连续检测使用后端确认的已保存工程；工作区中尚未保存的修改不会参与运行。
    </CvInlineAlert>
    <CvPanel
      title="可检测工程"
      :description="projectCountLabel"
      variant="section"
      :padded="false"
    >
      <div class="inspection-projects__toolbar">
        <CvSearchField
          v-model="searchDraft"
          class="inspection-projects__search"
          label="搜索可检测工程"
          placeholder="按名称或描述搜索…"
          clear-label="清除工程搜索"
          input-test-id="inspection-project-search"
          @search="submitSearch"
          @clear="clearSearch"
        />
        <CvButton
          size="sm"
          variant="secondary"
          :disabled="state.isRefreshing"
          @click="submitSearch"
        >
          搜索
        </CvButton>
      </div>
      <CvInlineAlert
        v-if="state.isRefreshing && state.data"
        class="inspection-projects__notice"
        tone="info"
        compact
      >
        正在刷新，暂时显示上次读取的工程。
      </CvInlineAlert>
      <CvPageState
        v-if="state.phase === 'loading' && !state.data"
        kind="loading"
        title="正在读取工程"
        description="正在同步可用于连续检测的已保存工程。"
      />
      <CvPageState
        v-else-if="state.phase === 'unauthorized'"
        kind="unauthorized"
        title="会话已失效"
        description="请重新登录后再读取可检测工程。"
      />
      <CvPageState
        v-else-if="state.phase === 'forbidden'"
        kind="forbidden"
        title="无权读取检测工程"
        description="当前账号不能使用连续检测，请联系管理员调整权限。"
      />
      <CvPageState
        v-else-if="state.phase === 'aborted' && projects.length === 0"
        kind="unknown"
        title="工程读取已取消"
        description="读取被新的页面操作取消，请重新刷新。"
      >
        <template #actions>
          <CvButton
            size="sm"
            @click="query.refresh({ force: true })"
          >
            重新读取
          </CvButton>
        </template>
      </CvPageState>
      <CvPageState
        v-else-if="state.phase === 'stale' && projects.length === 0"
        kind="stale"
        title="工程列表已过期"
        description="服务暂时不可用，当前没有可核对的工程列表。"
      >
        <template #actions>
          <CvButton
            size="sm"
            @click="query.refresh({ force: true })"
          >
            重试
          </CvButton>
        </template>
      </CvPageState>
      <CvPageState
        v-else-if="state.phase === 'partial-failure' && projects.length === 0"
        kind="partial"
        title="工程列表读取不完整"
        description="部分服务响应失败，当前没有可安全选择的工程。"
      >
        <template #actions>
          <CvButton
            size="sm"
            @click="query.refresh({ force: true })"
          >
            重试
          </CvButton>
        </template>
      </CvPageState>
      <CvPageState
        v-else-if="state.phase === 'error' || state.phase === 'not-found'"
        kind="error"
        title="工程读取失败"
        :description="state.failure?.message ?? '暂时无法读取可检测工程。'"
      >
        <template #actions>
          <CvButton
            size="sm"
            @click="query.refresh({ force: true })"
          >
            重试
          </CvButton>
        </template>
      </CvPageState>
      <CvPageState
        v-else-if="(state.phase === 'empty' || state.phase === 'success') && projects.length === 0"
        kind="empty"
        :title="isSearching ? '没有匹配的工程' : '暂无可检测工程'"
        :description="isSearching ? '调整关键词，或清除搜索查看全部工程。' : '请先在工程页创建并保存工程。'"
      >
        <template #actions>
          <CvButton
            v-if="isSearching"
            size="sm"
            @click="clearSearch"
          >
            清除搜索
          </CvButton>
          <RouterLink
            v-else
            class="inspection-projects__projects-link"
            to="/projects"
          >
            前往工程页
          </RouterLink>
        </template>
      </CvPageState>
      <CvInlineAlert
        v-if="state.phase === 'stale' && projects.length > 0"
        class="inspection-projects__notice"
        tone="warning"
        compact
        title="列表可能已过期"
      >
        当前显示上次成功读取的工程。进入运行页后仍会重新核对权限与运行条件。
      </CvInlineAlert>
      <CvInlineAlert
        v-if="state.phase === 'partial-failure' && projects.length > 0"
        class="inspection-projects__notice"
        tone="warning"
        compact
        title="列表读取不完整"
      >
        已显示成功读取的工程，列表可能不完整。进入运行页后仍会重新核对运行条件。
      </CvInlineAlert>
      <ul
        v-if="projects.length > 0"
        class="inspection-projects__list"
      >
        <li
          v-for="project in projects"
          :key="project.id"
        >
          <div class="inspection-projects__summary">
            <strong>{{ project.name }}</strong>
            <span>{{ project.description || '无工程描述' }}</span>
            <small>保存修订 {{ project.persistenceRevision }} · 修改于 {{ formatProjectDateTime(project.modifiedAt) }}</small>
          </div>
          <RouterLink
            class="inspection-projects__open"
            :to="`/projects/${project.id}/inspection`"
            :aria-label="`进入连续检测：${project.name}`"
            data-testid="inspection-project-open"
          >
            <CvIcon
              name="play"
              size="sm"
            />
            进入检测
          </RouterLink>
        </li>
      </ul>
    </CvPanel>
  </section>
</template>

<style scoped>
.inspection-projects { display: grid; width: 100%; max-width: 1320px; min-width: 0; gap: var(--cv-density-page-gap); }
.inspection-projects__toolbar { display: flex; align-items: end; gap: var(--cv-space-2); padding: var(--cv-space-3) 0; border-top: 1px solid var(--cv-border-subtle); }
.inspection-projects__search { width: min(100%, 520px); }
.inspection-projects__notice { margin-bottom: var(--cv-space-3); }
.inspection-projects__list { margin: 0; padding: 0; border-top: 1px solid var(--cv-border-subtle); list-style: none; }
.inspection-projects__list li { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: var(--cv-space-4); min-height: 72px; padding: var(--cv-space-3) 0; border-bottom: 1px solid var(--cv-border-subtle); }
.inspection-projects__summary { display: grid; min-width: 0; gap: 2px; }
.inspection-projects__summary strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.inspection-projects__summary strong,
.inspection-projects__summary span,
.inspection-projects__summary small { overflow-wrap: anywhere; }
.inspection-projects__summary span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.inspection-projects__summary small { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.inspection-projects__open,
.inspection-projects__projects-link { display: inline-flex; min-height: var(--cv-density-control-height-sm); align-items: center; gap: var(--cv-space-1); color: var(--cv-color-link); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); text-decoration: none; white-space: nowrap; }
.inspection-projects__open:hover,
.inspection-projects__projects-link:hover { text-decoration: underline; }
.inspection-projects__open:focus-visible,
.inspection-projects__projects-link:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 2px; }
@media (max-width: 640px) {
  .inspection-projects__toolbar { align-items: stretch; flex-direction: column; }
  .inspection-projects__list li { grid-template-columns: 1fr; }
  .inspection-projects__open { justify-self: start; }
}
</style>
