<script setup lang="ts">
import { computed } from 'vue';
import { CvIcon } from '@/design-system';
import { CvStatusBadge } from '@/design-system/primitives';
import type { AiProjectContextV1, AiSessionDetailV1 } from './contracts';

const props = defineProps<{
  project: AiProjectContextV1 | null;
  session: AiSessionDetailV1 | null;
}>();

const revision = computed(() => props.project?.persistenceRevision ?? null);
</script>

<template>
  <div
    class="ai-project-context"
    data-ai-project-context
  >
    <CvIcon
      :name="project ? 'projects' : 'info'"
      size="sm"
    />
    <div class="ai-project-context__identity">
      <strong>{{ project?.name ?? '尚未绑定工程' }}</strong>
      <span v-if="project">版本 {{ project.version }} · 保存 revision {{ revision }}</span>
      <span v-else>可先完成任务理解与方案规划，当前不会创建或写入工程。</span>
    </div>
    <CvStatusBadge
      :tone="project ? 'info' : 'idle'"
      :label="project ? '服务端工程上下文' : '未绑定工程'"
    />
    <span
      v-if="session"
      class="ai-project-context__session"
    >会话 revision {{ session.snapshot.revision }}</span>
  </div>
</template>

<style scoped>
.ai-project-context { display: flex; min-width: 0; align-items: center; gap: var(--cv-space-3); color: var(--cv-text-secondary); }
.ai-project-context__identity { display: grid; min-width: 0; gap: 1px; }
.ai-project-context__identity strong { overflow-wrap: anywhere; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.ai-project-context__identity span, .ai-project-context__session { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.ai-project-context__session { margin-inline-start: auto; white-space: nowrap; font-variant-numeric: tabular-nums; }
@media (max-width: 760px) {
  .ai-project-context { align-items: flex-start; flex-wrap: wrap; }
  .ai-project-context__identity { flex: 1 1 240px; }
  .ai-project-context__session { width: 100%; margin-inline-start: 28px; }
}
</style>
