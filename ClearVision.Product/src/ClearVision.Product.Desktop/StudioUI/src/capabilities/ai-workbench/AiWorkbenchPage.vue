<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue';
import { useRoute } from 'vue-router';
import { useProductRuntime } from '@/app/productRuntime';
import { CvPageHeader } from '@/design-system/patterns';
import { CvStatusBadge } from '@/design-system/primitives';
import AiWorkbenchStatus from './AiWorkbenchStatus.vue';
import { createAiSessionOwner } from './aiSessionOwner';
import type { AiWorkbenchActionId } from './actionModel';

const route = useRoute();
const runtime = useProductRuntime();
const requestedSessionId = computed(() => typeof route.query.sessionId === 'string'
  ? route.query.sessionId
  : null);
const projectId = computed(() => typeof route.params.id === 'string' ? route.params.id : null);
const owner = createAiSessionOwner({
  api: runtime.api,
  requestedSessionId: requestedSessionId.value,
  projectId: projectId.value
});
const state = computed(() => owner.state.value);
const diagnostics = computed(() => owner.diagnostics());

function handleAction(actionId: AiWorkbenchActionId): void {
  if (actionId === 'retry-session') void owner.retry();
  if (actionId === 'refresh-session') void owner.refresh();
}

onMounted(() => void owner.start());
onUnmounted(() => owner.dispose());
</script>

<template>
  <section
    class="ai-workbench-page"
    :data-ai-owner-phase="state.phase"
    :data-ai-owner-request-count="diagnostics.requestCount"
    :data-ai-owner-stream-count="diagnostics.streamCount"
    :data-ai-owner-timer-count="diagnostics.timerCount"
    :data-ai-owner-subscription-count="diagnostics.subscriptionCount"
  >
    <CvPageHeader title="AI 工程工作台">
      <template #meta>
        <CvStatusBadge
          :tone="owner.projection.value.statusTone"
          :label="owner.projection.value.statusLabel"
        />
        <span class="ai-workbench-page__scope">
          {{ projectId ? '工程会话' : '新工程会话' }}
        </span>
      </template>
    </CvPageHeader>

    <AiWorkbenchStatus
      :state="state"
      :projection="owner.projection.value"
      @action="handleAction"
    />
  </section>
</template>

<style scoped>
.ai-workbench-page {
  display: grid;
  min-width: 0;
  align-content: start;
  gap: var(--cv-density-page-gap);
  padding: var(--cv-density-page-padding);
}

.ai-workbench-page__scope {
  display: inline-flex;
  min-height: 22px;
  align-items: center;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
}

@media (max-width: 640px) {
  .ai-workbench-page { padding: var(--cv-space-4); }
}
</style>
