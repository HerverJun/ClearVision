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
import { CvIcon } from '@/design-system/icons';
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
const primaryRecentProject = computed(() => recentProjectsState.value.data?.[0] ?? null);
const earlierRecentProjects = computed(() => recentProjectsState.value.data?.slice(1) ?? []);
const session = computed(() => runtime.session.projection);
const systemStatus = computed(() => runtime.systemStatus.projection);
const canViewDiagnostics = computed(() => session.value.user?.role === 'Admin' ||
  session.value.user?.role === 'Engineer');
const canRunInspection = computed(() => canViewDiagnostics.value);
const systemItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'message', label: '状态说明', value: systemStatus.value.message },
  { key: 'updated', label: '最近确认', value: formatUpdatedAt(systemStatus.value.updatedAt) }
]);
const sessionItems = computed<readonly CvDescriptionItem[]>(() => [
  { key: 'role', label: '角色', value: formatRole(session.value.user?.role) },
  { key: 'message', label: '会话状态', value: session.value.message }
]);

const dateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false
});

function formatUpdatedAt(value: number | null | undefined): string {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
    ? dateTimeFormatter.format(new Date(value))
    : '尚未确认';
}

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
      title="工作台"
      description="继续最近工程，查看当前运行环境与可用功能。"
    >
      <template #actions>
        <CvButton
          size="sm"
          @click="refreshSharedProjections"
        >
          <template #leading>
            <CvIcon
              name="refresh"
              size="sm"
            />
          </template>
          刷新概览
        </CvButton>
      </template>
    </CvPageHeader>

    <CvPanel
      class="overview-page__resume-section"
      title="继续工作"
      description="从最近打开的工程接续配置。"
      variant="section"
    >
      <template #actions>
        <RouterLink to="/projects">
          查看全部工程
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
        description="服务端当前没有最近打开记录，可前往工程库选择或创建工程。"
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

      <article
        v-if="primaryRecentProject"
        class="overview-page__resume"
      >
        <div class="overview-page__resume-copy">
          <span class="overview-page__resume-label">
            <CvIcon
              name="clock"
              size="sm"
            />
            最近打开
          </span>
          <h3 class="overview-page__resume-title">
            <RouterLink :to="`/projects/${primaryRecentProject.id}`">
              {{ primaryRecentProject.name }}
            </RouterLink>
          </h3>
          <p>{{ primaryRecentProject.description || '暂无工程描述' }}</p>
        </div>
        <div class="overview-page__resume-time">
          <span>上次打开</span>
          <time :datetime="primaryRecentProject.lastOpenedAt ?? undefined">
            {{ formatProjectDateTime(primaryRecentProject.lastOpenedAt) }}
          </time>
        </div>
        <div class="overview-page__resume-actions">
          <RouterLink :to="`/projects/${primaryRecentProject.id}`">
            查看详情
          </RouterLink>
          <RouterLink
            class="overview-page__continue"
            :to="`/projects/${encodeURIComponent(primaryRecentProject.id)}/workspace`"
          >
            继续配置
            <CvIcon
              name="chevron-right"
              size="sm"
            />
          </RouterLink>
        </div>
      </article>

      <section
        v-if="earlierRecentProjects.length"
        class="overview-page__earlier"
        aria-labelledby="overview-earlier-title"
      >
        <h3 id="overview-earlier-title">
          更早打开
        </h3>
        <ul class="overview-page__project-list">
          <li
            v-for="project in earlierRecentProjects"
            :key="project.id"
          >
            <RouterLink
              class="overview-page__project-copy"
              :to="`/projects/${project.id}`"
            >
              <strong>{{ project.name }}</strong>
              <small>{{ project.description || '暂无工程描述' }}</small>
            </RouterLink>
            <span class="overview-page__project-meta">
              <time :datetime="project.lastOpenedAt ?? undefined">
                {{ formatProjectDateTime(project.lastOpenedAt) }}
              </time>
              <RouterLink :to="`/projects/${encodeURIComponent(project.id)}/workspace`">
                继续配置
              </RouterLink>
            </span>
          </li>
        </ul>
      </section>
    </CvPanel>

    <section class="overview-page__context-grid">
      <CvPanel
        class="overview-page__environment"
        title="运行环境"
        description="本地服务与当前会话的实时投影。"
        :level="2"
        variant="section"
        :padded="false"
      >
        <div class="overview-page__environment-grid">
          <section aria-label="本地服务状态">
            <div class="overview-page__context-heading">
              <span>本地服务</span>
              <CvStatusBadge
                :tone="systemStatus.phase === 'online' ? 'ok' : systemStatus.phase === 'stale' ? 'warning' : systemStatus.phase === 'loading' ? 'info' : 'error'"
                :label="systemStatus.phase === 'online' ? '在线' : systemStatus.phase === 'loading' ? '连接中' : systemStatus.phase === 'stale' ? '状态过期' : '离线'"
              />
            </div>
            <strong class="overview-page__context-value">{{ formatHealthStatus(systemStatus.health?.status) }}</strong>
            <CvDescriptionList
              :items="systemItems"
              :columns="1"
              label="本地服务状态"
            />
          </section>

          <section aria-label="当前会话状态">
            <div class="overview-page__context-heading">
              <span>当前会话</span>
              <CvStatusBadge
                :tone="session.phase === 'authenticated' ? 'ok' : session.phase === 'stale' ? 'warning' : session.phase === 'loading' ? 'info' : 'idle'"
                :label="session.phase === 'authenticated' ? '已认证' : session.phase === 'loading' ? '确认中' : session.phase === 'stale' ? '会话过期' : '未认证'"
              />
            </div>
            <strong class="overview-page__context-value">{{ session.user?.username ?? '未提供会话' }}</strong>
            <CvDescriptionList
              :items="sessionItems"
              :columns="1"
              label="当前会话状态"
            />
          </section>
        </div>
      </CvPanel>

      <CvPanel
        class="overview-page__capabilities"
        title="可用功能"
        description="按当前角色显示可进入的工作区。"
        :level="2"
        variant="section"
        :padded="false"
      >
        <nav
          class="overview-page__quick-links"
          aria-label="产品快速入口"
        >
          <RouterLink to="/projects">
            <CvIcon name="projects" />
            <span><strong>工程</strong><small>配置流程与工程资产</small></span>
            <CvIcon
              name="chevron-right"
              size="sm"
            />
          </RouterLink>
          <RouterLink
            v-if="canRunInspection"
            to="/inspection"
          >
            <CvIcon name="play" />
            <span><strong>连续检测</strong><small>进入正式运行控制</small></span>
            <CvIcon
              name="chevron-right"
              size="sm"
            />
          </RouterLink>
          <RouterLink to="/results">
            <CvIcon name="results" />
            <span><strong>检测结果</strong><small>调查本机与工作站结果</small></span>
            <CvIcon
              name="chevron-right"
              size="sm"
            />
          </RouterLink>
          <RouterLink
            v-if="canViewDiagnostics"
            to="/diagnostics"
          >
            <CvIcon name="diagnostics" />
            <span><strong>诊断</strong><small>核对应用、宿主与服务状态</small></span>
            <CvIcon
              name="chevron-right"
              size="sm"
            />
          </RouterLink>
          <RouterLink to="/about">
            <CvIcon name="about" />
            <span><strong>关于</strong><small>查看版本与支持信息</small></span>
            <CvIcon
              name="chevron-right"
              size="sm"
            />
          </RouterLink>
        </nav>
      </CvPanel>
    </section>
  </section>
</template>

<style scoped>
.overview-page {
  display: grid;
  width: 100%;
  max-width: 1720px;
  min-width: 0;
  gap: var(--cv-density-page-gap);
}

.overview-page__resume-section :deep(.cv-panel__header) { padding-bottom: var(--cv-space-3); }
.overview-page__notice { margin-bottom: var(--cv-space-3); }

.overview-page__resume {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(132px, auto) auto;
  align-items: center;
  gap: var(--cv-space-6);
  min-height: 112px;
  padding: var(--cv-space-5) var(--cv-space-6);
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-lg);
  background: var(--cv-surface-raised);
}

.overview-page__resume-copy { display: grid; min-width: 0; gap: var(--cv-space-1); }
.overview-page__resume-label {
  display: inline-flex;
  align-items: center;
  gap: var(--cv-space-2);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-medium);
}
.overview-page__resume-title {
  min-width: 0;
  margin: 0;
  font-size: var(--cv-font-size-xl);
  font-weight: var(--cv-font-weight-semibold);
  line-height: var(--cv-line-height-tight);
}
.overview-page__resume-title a { color: var(--cv-text-primary); text-decoration: none; }
.overview-page__resume-title a:hover { color: var(--cv-color-link); }
.overview-page__resume-copy p {
  max-width: 68ch;
  margin: 0;
  overflow: hidden;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.overview-page__resume-time { display: grid; gap: var(--cv-space-1); color: var(--cv-text-secondary); }
.overview-page__resume-time span { font-size: var(--cv-font-size-xs); }
.overview-page__resume-time time {
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  font-variant-numeric: tabular-nums lining-nums;
  white-space: nowrap;
}
.overview-page__resume-actions { display: flex; align-items: center; gap: var(--cv-space-3); white-space: nowrap; }
.overview-page__resume-actions > a:first-child { color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.overview-page__continue {
  display: inline-flex;
  align-items: center;
  gap: var(--cv-space-2);
  min-height: var(--cv-density-control-height);
  padding: 0 var(--cv-space-4);
  border: 1px solid var(--cv-color-action);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-color-action);
  color: var(--cv-color-on-action);
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-semibold);
  text-decoration: none;
  transition: background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.overview-page__continue:hover { background: var(--cv-color-action-hover); color: var(--cv-color-on-action); }
.overview-page__continue:focus-visible { outline: none; box-shadow: var(--cv-focus-ring); }

.overview-page__earlier { margin-top: var(--cv-space-4); }
.overview-page__earlier h3 {
  margin: 0 0 var(--cv-space-2);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-semibold);
}
.overview-page__project-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin: 0;
  padding: 0;
  border-block: 1px solid var(--cv-border-subtle);
  list-style: none;
}
.overview-page__project-list li:nth-child(odd):not(:last-child) { border-inline-end: 1px solid var(--cv-border-subtle); }
.overview-page__project-list li:nth-child(n + 3) { border-block-start: 1px solid var(--cv-border-subtle); }
.overview-page__project-list li {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--cv-space-3);
  min-height: 56px;
  padding: var(--cv-space-2) var(--cv-space-3);
  transition: background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.overview-page__project-list li:hover,
.overview-page__project-list li:focus-within { background: var(--cv-interactive-hover); }
.overview-page__project-copy {
  display: grid;
  min-width: 0;
  gap: 2px;
  color: var(--cv-text-primary);
  text-decoration: none;
}
.overview-page__project-copy:hover strong { color: var(--cv-color-link); }
.overview-page__project-copy:focus-visible { border-radius: var(--cv-radius-xs); outline: none; box-shadow: var(--cv-focus-ring); }
.overview-page__project-copy strong,
.overview-page__project-copy small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.overview-page__project-copy strong { font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-medium); }
.overview-page__project-copy small,
.overview-page__project-list time { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.overview-page__project-list time { font-variant-numeric: tabular-nums lining-nums; white-space: nowrap; }
.overview-page__project-meta { display: flex; align-items: center; gap: var(--cv-space-3); white-space: nowrap; }
.overview-page__project-meta a { font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-medium); }

.overview-page__context-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.3fr) minmax(360px, .9fr);
  align-items: start;
  gap: var(--cv-space-6);
}
.overview-page__environment-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  border-block: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
}
.overview-page__environment-grid > section { min-width: 0; padding: var(--cv-space-4); }
.overview-page__environment-grid > section + section { border-inline-start: 1px solid var(--cv-border-subtle); }
.overview-page__context-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-3);
  margin-bottom: var(--cv-space-2);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-sm);
}
.overview-page__context-value {
  display: block;
  margin-bottom: var(--cv-space-3);
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-lg);
  font-weight: var(--cv-font-weight-semibold);
}

.overview-page__quick-links {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  border-block-start: 1px solid var(--cv-border-subtle);
}
.overview-page__quick-links a {
  display: grid;
  grid-template-columns: 20px minmax(0, 1fr) 16px;
  align-items: center;
  gap: var(--cv-space-3);
  min-height: 58px;
  padding: var(--cv-space-2) var(--cv-space-3);
  border-bottom: 1px solid var(--cv-border-subtle);
  color: var(--cv-text-primary);
  text-decoration: none;
  transition: background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.overview-page__quick-links a:nth-child(odd):not(:last-child) { border-inline-end: 1px solid var(--cv-border-subtle); }
.overview-page__quick-links a:hover { background: var(--cv-interactive-hover); }
.overview-page__quick-links a:focus-visible { outline: none; box-shadow: inset var(--cv-focus-ring); }
.overview-page__quick-links a > span { display: grid; min-width: 0; gap: 2px; }
.overview-page__quick-links strong,
.overview-page__quick-links small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.overview-page__quick-links strong { font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-medium); }
.overview-page__quick-links small { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.overview-page__quick-links a > :last-child { color: var(--cv-text-muted); }

@media (max-width: 1180px) {
  .overview-page__resume { grid-template-columns: minmax(0, 1fr) auto; }
  .overview-page__resume-time { grid-column: 1; grid-row: 2; }
  .overview-page__resume-actions { grid-column: 2; grid-row: 1 / span 2; }
  .overview-page__context-grid { grid-template-columns: 1fr; }
}

@media (max-width: 760px) {
  .overview-page__resume { grid-template-columns: 1fr; gap: var(--cv-space-3); padding: var(--cv-space-4); }
  .overview-page__resume-time,
  .overview-page__resume-actions { grid-column: 1; grid-row: auto; }
  .overview-page__resume-actions { justify-content: space-between; }
  .overview-page__project-list,
  .overview-page__environment-grid,
  .overview-page__quick-links { grid-template-columns: 1fr; }
  .overview-page__project-list li:nth-child(odd),
  .overview-page__quick-links a:nth-child(odd) { border-inline-end: 0; }
  .overview-page__project-list li + li,
  .overview-page__environment-grid > section + section { border-block-start: 1px solid var(--cv-border-subtle); border-inline-start: 0; }
}
</style>
