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
    title="最近工程"
    :level="2"
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
        <div class="projects-recent__copy">
          <RouterLink
            :to="`/projects/${project.id}`"
            :title="project.name"
          >
            {{ project.name }}
          </RouterLink>
          <span>{{ formatProjectDateTime(project.lastOpenedAt) }}</span>
        </div>
        <CvButton
          v-if="canOpen"
          size="sm"
          variant="quiet"
          :disabled="busy"
          @click="emit('open', project)"
        >
          打开工作区
        </CvButton>
      </li>
    </ol>
  </CvPanel>
</template>

<style scoped>
.projects-recent__list { display: grid; gap: 0; margin: 0; padding: 0; list-style: none; }
.projects-recent__list li { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-2); padding: var(--cv-space-3) var(--cv-density-panel-padding); border-top: 1px solid var(--cv-border-subtle); }
.projects-recent__copy { display: grid; min-width: 0; gap: 2px; }
.projects-recent__copy a { overflow: hidden; color: var(--cv-text-primary); font-weight: var(--cv-font-weight-medium); text-overflow: ellipsis; white-space: nowrap; }
.projects-recent__copy span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.projects-recent :deep(.cv-panel__header) { padding-bottom: var(--cv-space-3); }
.projects-recent :deep(.cv-inline-alert),
.projects-recent :deep(.cv-page-state) { margin: 0 var(--cv-density-panel-padding) var(--cv-density-panel-padding); }
@media (max-width: 1040px) {
  .projects-recent__list { grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); }
  .projects-recent__list li { border-right: 1px solid var(--cv-border-subtle); }
}
</style>
