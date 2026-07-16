<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue';
import { RouterLink } from 'vue-router';
import {
  CvButton,
  CvDescriptionList,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvPanel,
  CvStatusBadge,
  type CvDescriptionItem
} from '@/design-system';
import {
  createRecentProjectsQuery,
  formatProjectDateTime
} from '@/capabilities/projects-read';
import { useOverviewRuntime, type OverviewRuntime } from './overviewRuntime';

const props = defineProps<{
  runtime?: OverviewRuntime;
}>();

const runtime = useOverviewRuntime(props.runtime);
const recentProjectsQuery = createRecentProjectsQuery(runtime.queries);
const recentProjectsState = computed(() => recentProjectsQuery.state.value);
const session = computed(() => runtime.session.projection);
const systemStatus = computed(() => runtime.systemStatus.projection);
const systemItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'port', label: '服务端口', value: systemStatus.value.health?.port ?? null },
  { key: 'message', label: '状态说明', value: systemStatus.value.message, span: 2 }
]);
const sessionItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'role', label: '角色', value: formatRole(session.value.user?.role) },
  { key: 'message', label: '状态说明', value: session.value.message, span: 2 }
]);

function formatHealthStatus(value: string | undefined): string {
  if (!value) return '状态不可用';
  const labels: Readonly<Record<string, string>> = Object.freeze({
    healthy: '健康',
    degraded: '降级',
    unhealthy: '异常'
  });
  return labels[value.toLocaleLowerCase()] ?? value;
}

function formatRole(value: string | undefined): string | null {
  if (!value) return null;
  const labels: Readonly<Record<string, string>> = Object.freeze({
    admin: '管理员',
    engineer: '工程师',
    operator: '操作员',
    viewer: '查看者'
  });
  return labels[value.toLocaleLowerCase()] ?? value;
}

function refreshSharedProjections(): void {
  void runtime.session.refresh();
  void runtime.systemStatus.refresh();
  void recentProjectsQuery.refresh({ force: true });
}

onMounted(() => {
  void recentProjectsQuery.refresh();
});

onBeforeUnmount(() => {
  recentProjectsQuery.dispose();
});
</script>

<template>
  <section
    class="overview-page"
    data-capability="overview"
  >
    <CvPageHeader
      eyebrow="工作台"
      title="概览"
      description="查看本地服务、当前会话和最近工程的只读状态。"
    >
      <template #actions>
        <CvButton
          size="sm"
          @click="refreshSharedProjections"
        >
          刷新概览
        </CvButton>
      </template>
    </CvPageHeader>

    <section
      class="overview-page__status-grid"
      aria-label="系统与会话状态"
    >
      <CvPanel
        title="本地服务"
        description="由全应用统一的系统状态服务提供。"
        :level="2"
      >
        <div class="overview-page__status-row">
          <CvStatusBadge
            :tone="systemStatus.phase === 'online' ? 'ok' : systemStatus.phase === 'stale' ? 'warning' : systemStatus.phase === 'loading' ? 'info' : 'ng'"
            :label="systemStatus.phase === 'online' ? '在线' : systemStatus.phase === 'loading' ? '连接中' : systemStatus.phase === 'stale' ? '状态过期' : '离线'"
          />
          <strong>{{ formatHealthStatus(systemStatus.health?.status) }}</strong>
        </div>
        <CvDescriptionList
          :items="systemItems"
          :columns="1"
          label="本地服务状态"
        />
      </CvPanel>

      <CvPanel
        title="当前会话"
        description="由全应用统一的会话状态服务提供。"
        :level="2"
      >
        <div class="overview-page__status-row">
          <CvStatusBadge
            :tone="session.phase === 'authenticated' ? 'ok' : session.phase === 'stale' ? 'warning' : session.phase === 'loading' ? 'info' : 'idle'"
            :label="session.phase === 'authenticated' ? '已认证' : session.phase === 'loading' ? '确认中' : session.phase === 'stale' ? '会话过期' : '未认证'"
          />
          <strong>{{ session.user?.username ?? '未提供会话' }}</strong>
        </div>
        <CvDescriptionList
          :items="sessionItems"
          :columns="1"
          label="当前会话状态"
        />
      </CvPanel>
    </section>

    <section class="overview-page__content-grid">
      <CvPanel
        title="最近工程"
        description="显示服务端记录的最近打开工程。"
      >
        <template #actions>
          <RouterLink to="/projects">
            查看全部
          </RouterLink>
        </template>

        <CvInlineAlert
          v-if="recentProjectsState.isRefreshing && recentProjectsState.data"
          class="overview-page__notice"
          tone="info"
        >
          正在刷新，暂时显示上次读取的数据。
        </CvInlineAlert>
        <CvInlineAlert
          v-if="(recentProjectsState.phase === 'stale' || recentProjectsState.phase === 'partial-failure') && recentProjectsState.data"
          class="overview-page__notice"
          tone="warning"
          title="刷新未完成"
        >
          当前显示上次成功读取的数据。
        </CvInlineAlert>

        <CvPageState
          v-if="recentProjectsState.phase === 'loading' && !recentProjectsState.data"
          compact
          kind="loading"
          title="正在读取最近工程"
        />
        <CvPageState
          v-else-if="recentProjectsState.phase === 'empty'"
          compact
          kind="empty"
          title="暂无最近工程"
          description="服务端当前没有最近打开记录。"
        />
        <CvPageState
          v-else-if="recentProjectsState.phase === 'unauthorized'"
          compact
          kind="unauthorized"
          title="会话不可用"
          description="系统状态仍可查看，受保护的最近工程暂不可用。"
        />
        <CvPageState
          v-else-if="recentProjectsState.phase === 'forbidden'"
          compact
          kind="forbidden"
          title="无权读取最近工程"
        />
        <CvPageState
          v-else-if="recentProjectsState.phase === 'error' || recentProjectsState.phase === 'not-found'"
          compact
          kind="error"
          title="最近工程不可用"
          :description="recentProjectsState.failure?.message ?? '本地服务没有返回有效数据。'"
        >
          <template #actions>
            <CvButton
              size="sm"
              @click="recentProjectsQuery.refresh({ force: true })"
            >
              重试
            </CvButton>
          </template>
        </CvPageState>

        <ul
          v-if="recentProjectsState.data?.length"
          class="overview-page__project-list"
        >
          <li
            v-for="project in recentProjectsState.data"
            :key="project.id"
          >
            <div>
              <RouterLink :to="`/projects/${project.id}`">
                {{ project.name }}
              </RouterLink>
              <p>{{ project.description || '无描述' }}</p>
            </div>
            <time :datetime="project.lastOpenedAt ?? undefined">{{ formatProjectDateTime(project.lastOpenedAt) }}</time>
          </li>
        </ul>
      </CvPanel>

      <CvPanel
        title="快速入口"
        description="前往当前可用的只读产品页面。"
        :level="2"
      >
        <nav
          class="overview-page__quick-links"
          aria-label="产品快速入口"
        >
          <RouterLink to="/projects">
            <strong>工程</strong><span>查看工程列表和正式详情摘要</span>
          </RouterLink>
          <RouterLink to="/diagnostics">
            <strong>诊断</strong><span>查看 StudioUI 与宿主诊断投影</span>
          </RouterLink>
          <RouterLink to="/about">
            <strong>关于</strong><span>查看产品版本和环境信息</span>
          </RouterLink>
        </nav>
      </CvPanel>
    </section>
  </section>
</template>

<style scoped>
.overview-page { display: grid; max-width: 1440px; gap: var(--cv-density-page-gap); min-width: 0; }
.overview-page__status-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-3); }
.overview-page__content-grid { display: grid; grid-template-columns: minmax(0, 1.55fr) minmax(240px, .65fr); gap: var(--cv-space-3); align-items: start; }
.overview-page__status-row { display: flex; align-items: center; gap: var(--cv-space-3); margin-bottom: var(--cv-space-3); }
.overview-page__notice { margin-bottom: var(--cv-space-3); }
.overview-page__project-list { display: grid; gap: 0; margin: 0; padding: 0; list-style: none; }
.overview-page__project-list li { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-4); padding: var(--cv-space-3) 0; border-bottom: 1px solid var(--cv-border-subtle); }
.overview-page__project-list li:first-child { padding-top: 0; }
.overview-page__project-list p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.overview-page__project-list time { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); white-space: nowrap; }
.overview-page__quick-links { display: grid; gap: 0; }
.overview-page__quick-links a { display: grid; gap: var(--cv-space-1); padding: var(--cv-space-3) var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); color: var(--cv-text-primary); text-decoration: none; }
.overview-page__quick-links a:last-child { border-bottom: 0; }
.overview-page__quick-links a:hover { background: var(--cv-interactive-hover); }
.overview-page__quick-links span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
@media (max-width: 900px) { .overview-page__content-grid { grid-template-columns: 1fr; } }
@media (max-width: 680px) { .overview-page__status-grid { grid-template-columns: 1fr; } }
</style>
