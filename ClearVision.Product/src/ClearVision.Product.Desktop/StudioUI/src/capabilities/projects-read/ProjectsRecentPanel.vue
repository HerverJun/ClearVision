<script setup lang="ts">
import { RouterLink } from 'vue-router';
import { CvButton, CvInlineAlert, CvPageState, CvPanel } from '@/design-system';
import type { ProjectSummary } from './projectContracts';
import { formatProjectDateTime } from './projectViewModel';

defineProps<{
  phase: string;
  projects: readonly ProjectSummary[] | null;
  isRefreshing: boolean;
  canOpen: boolean;
  busy: boolean;
}>();

const emit = defineEmits<{
  open: [project: ProjectSummary];
}>();
</script>

<template>
  <CvPanel
    class="projects-recent"
    title="最近打开"
    description="按最近打开时间排列。"
    :level="2"
    variant="section"
    :padded="false"
  >
    <CvInlineAlert
      v-if="isRefreshing && projects"
      compact
      tone="info"
    >
      正在刷新，暂时显示上次读取的最近工程。
    </CvInlineAlert>
    <CvInlineAlert
      v-if="(phase === 'stale' || phase === 'partial-failure') && projects"
      compact
      tone="warning"
      title="最近工程刷新未完成"
    >
      当前显示上次成功读取的数据。
    </CvInlineAlert>
    <CvPageState
      v-if="phase === 'loading' && !projects"
      compact
      kind="loading"
      title="正在读取最近工程"
    />
    <CvPageState
      v-else-if="phase === 'empty'"
      compact
      kind="empty"
      title="暂无最近工程"
      description="打开工程后会显示在这里。"
    />
    <CvPageState
      v-else-if="phase === 'unauthorized'"
      compact
      kind="unauthorized"
      title="当前会话不可用"
    />
    <CvPageState
      v-else-if="phase === 'forbidden'"
      compact
      kind="forbidden"
      title="无权读取最近工程"
    />
    <CvPageState
      v-else-if="phase === 'error' || phase === 'not-found'"
      compact
      kind="error"
      title="最近工程读取失败"
    />
    <ol
      v-if="projects?.length"
      class="projects-recent__list"
    >
      <li
        v-for="project in projects"
        :key="project.id"
      >
        <RouterLink
          class="projects-recent__copy"
          :to="`/projects/${project.id}`"
          :title="project.name"
        >
          <strong>{{ project.name }}</strong>
          <time :datetime="project.lastOpenedAt ?? undefined">
            {{ formatProjectDateTime(project.lastOpenedAt) }}
          </time>
        </RouterLink>
        <CvButton
          v-if="canOpen"
          size="sm"
          variant="quiet"
          :disabled="busy"
          @click="emit('open', project)"
        >
          打开
        </CvButton>
      </li>
    </ol>
  </CvPanel>
</template>

<style scoped>
.projects-recent {
  display: grid;
  grid-template-columns: minmax(168px, 204px) minmax(0, 1fr);
  align-items: stretch;
  border-block: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
}
.projects-recent :deep(.cv-panel__header) {
  align-items: center;
  padding: var(--cv-space-4);
  border-inline-end: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-page);
}
.projects-recent :deep(.cv-panel__content) { min-width: 0; }
.projects-recent__list {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(176px, 1fr));
  min-height: 72px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.projects-recent__list li {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--cv-space-2);
  min-width: 0;
  padding: var(--cv-space-3);
  border-inline-end: 1px solid var(--cv-border-subtle);
  transition: background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.projects-recent__list li:last-child { border-inline-end: 0; }
.projects-recent__list li:hover,
.projects-recent__list li:focus-within { background: var(--cv-interactive-hover); }
.projects-recent__copy {
  display: grid;
  min-width: 0;
  gap: var(--cv-space-1);
  color: var(--cv-text-primary);
  text-decoration: none;
}
.projects-recent__copy:hover strong { color: var(--cv-color-link); }
.projects-recent__copy:focus-visible { border-radius: var(--cv-radius-xs); outline: none; box-shadow: var(--cv-focus-ring); }
.projects-recent__copy strong {
  overflow: hidden;
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-semibold);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.projects-recent__copy time {
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  font-variant-numeric: tabular-nums lining-nums;
  white-space: nowrap;
}
.projects-recent :deep(.cv-inline-alert),
.projects-recent :deep(.cv-page-state) { margin: 0 var(--cv-density-panel-padding) var(--cv-density-panel-padding); }
@media (max-width: 1180px) {
  .projects-recent { display: block; }
  .projects-recent :deep(.cv-panel__header) {
    padding-inline: 0;
    border-inline-end: 0;
    background: transparent;
  }
  .projects-recent__list { grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); border-block-start: 1px solid var(--cv-border-subtle); }
  .projects-recent__list li { border-block-end: 1px solid var(--cv-border-subtle); }
}
@media (max-width: 640px) {
  .projects-recent__list { grid-template-columns: 1fr; }
  .projects-recent__list li { border-inline-end: 0; }
}
</style>
