<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue';
import { RouterLink } from 'vue-router';
import {
  CvButton,
  CvInlineAlert,
  CvPageHeader,
  CvPageState,
  CvPanel
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
const environmentFacts = computed(() => [
  {
    key: 'service',
    label: '本地服务',
    value: systemStatus.value.phase === 'online'
      ? '在线'
      : systemStatus.value.phase === 'loading'
        ? '连接中'
        : systemStatus.value.phase === 'stale'
          ? '状态过期'
          : '离线',
    tone: systemStatus.value.phase === 'online'
      ? 'ok'
      : systemStatus.value.phase === 'loading'
        ? 'info'
        : systemStatus.value.phase === 'stale'
          ? 'warning'
          : 'error',
    detail: formatHealthStatus(systemStatus.value.health?.status)
  },
  { key: 'message', label: '状态说明', value: systemStatus.value.message, tone: 'default', detail: null },
  { key: 'updated', label: '最近确认', value: formatUpdatedAt(systemStatus.value.updatedAt), tone: 'default', detail: null },
  { key: 'session', label: '当前会话', value: session.value.user?.username ?? '未提供会话', tone: 'default', detail: null },
  { key: 'role', label: '角色', value: formatRole(session.value.user?.role) ?? '无', tone: 'default', detail: null },
  { key: 'session-state', label: '会话状态', value: session.value.message, tone: 'default', detail: null }
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
      description="继续最近工作，查看当前运行环境与可用功能。"
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
          <dl
            role="region"
            aria-label="运行环境与当前会话"
          >
            <div
              v-for="fact in environmentFacts"
              :key="fact.key"
              class="overview-page__environment-fact"
            >
              <dt>{{ fact.label }}</dt>
              <dd
                :data-tone="fact.tone"
                :title="`${fact.label}：${fact.value}${fact.detail ? `；${fact.detail}` : ''}`"
              >
                <span>{{ fact.value }}</span>
                <small v-if="fact.detail">{{ fact.detail }}</small>
              </dd>
            </div>
          </dl>
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
            <strong>工程</strong>
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
            <strong>连续检测</strong>
            <CvIcon
              name="chevron-right"
              size="sm"
            />
          </RouterLink>
          <RouterLink to="/results">
            <CvIcon name="results" />
            <strong>检测结果</strong>
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
            <strong>诊断</strong>
            <CvIcon
              name="chevron-right"
              size="sm"
            />
          </RouterLink>
          <RouterLink to="/about">
            <CvIcon name="about" />
            <strong>关于</strong>
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
  min-height: 96px;
  padding: var(--cv-space-5);
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-lg);
  background: var(--cv-surface-raised);
  box-shadow: inset 4px 0 0 var(--cv-color-action);
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
  grid-template-columns: minmax(0, 1fr);
  align-items: start;
  gap: var(--cv-density-page-gap);
}
.overview-page__environment-grid {
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-raised);
}
.overview-page__environment-grid dl {
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  margin: 0;
}
.overview-page__environment-fact {
  display: flex;
  min-width: 0;
  align-items: center;
  justify-content: center;
  gap: var(--cv-space-2);
  min-height: 56px;
  padding: var(--cv-space-2) var(--cv-space-3);
}
.overview-page__environment-fact + .overview-page__environment-fact {
  border-inline-start: 1px solid var(--cv-border-subtle);
}
.overview-page__environment-fact dt {
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  white-space: nowrap;
}
.overview-page__environment-fact dd {
  display: flex;
  min-width: 0;
  align-items: baseline;
  gap: var(--cv-space-1);
  margin: 0;
  overflow: hidden;
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-medium);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.overview-page__environment-fact dd[data-tone="ok"] { color: var(--cv-color-status-ok-strong); }
.overview-page__environment-fact dd[data-tone="info"] { color: var(--cv-color-status-info-strong); }
.overview-page__environment-fact dd[data-tone="warning"] { color: var(--cv-color-status-warning-strong); }
.overview-page__environment-fact dd[data-tone="error"] { color: var(--cv-color-status-error-strong); }
.overview-page__environment-fact dd small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }

.overview-page__quick-links {
  display: grid;
  grid-template-columns: 1fr;
  border-block-start: 1px solid var(--cv-border-subtle);
}
.overview-page__quick-links a {
  display: grid;
  grid-template-columns: 20px minmax(0, 1fr) 16px;
  align-items: center;
  gap: var(--cv-space-3);
  min-height: 48px;
  padding: var(--cv-space-2) var(--cv-space-3);
  border-bottom: 1px solid var(--cv-border-subtle);
  color: var(--cv-text-primary);
  text-decoration: none;
  transition: background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.overview-page__quick-links a:hover { background: var(--cv-interactive-hover); }
.overview-page__quick-links a:focus-visible { outline: none; box-shadow: inset var(--cv-focus-ring); }
.overview-page__quick-links strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.overview-page__quick-links strong { font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-medium); }
.overview-page__quick-links a > :last-child { color: var(--cv-text-muted); }

@media (min-width: 1920px) {
  .overview-page.overview-page {
    position: relative;
    left: 4.5px;
    width: 1763px;
    max-width: 1763px;
  }
  .overview-page__resume-section {
    min-height: 274px;
    margin-top: 46.1875px;
  }
}

@media (max-width: 1180px) {
  .overview-page__resume { grid-template-columns: minmax(0, 1fr) auto; }
  .overview-page__resume-time { grid-column: 1; grid-row: 2; }
  .overview-page__resume-actions { grid-column: 2; grid-row: 1 / span 2; }
  .overview-page__context-grid { grid-template-columns: 1fr; }
  .overview-page__environment-grid dl { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .overview-page__environment-fact:nth-child(4) { border-inline-start: 0; }
  .overview-page__environment-fact:nth-child(n + 4) { border-block-start: 1px solid var(--cv-border-subtle); }
}

@media (max-width: 760px) {
  .overview-page__resume { grid-template-columns: 1fr; gap: var(--cv-space-3); padding: var(--cv-space-4); }
  .overview-page__resume-time,
  .overview-page__resume-actions { grid-column: 1; grid-row: auto; }
  .overview-page__resume-actions { justify-content: space-between; }
  .overview-page__project-list,
  .overview-page__quick-links { grid-template-columns: 1fr; }
  .overview-page__environment-grid dl { grid-template-columns: 1fr; }
  .overview-page__environment-fact { justify-content: space-between; }
  .overview-page__environment-fact + .overview-page__environment-fact,
  .overview-page__environment-fact:nth-child(4) { border-block-start: 1px solid var(--cv-border-subtle); border-inline-start: 0; }
  .overview-page__project-list li:nth-child(odd),
  .overview-page__quick-links a:nth-child(odd) { border-inline-end: 0; }
  .overview-page__project-list li + li { border-block-start: 1px solid var(--cv-border-subtle); border-inline-start: 0; }
}
</style>
