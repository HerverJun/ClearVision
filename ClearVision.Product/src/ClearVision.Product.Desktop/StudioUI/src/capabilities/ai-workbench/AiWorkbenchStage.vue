<script setup lang="ts">
import { computed } from 'vue';
import { CvButton, CvStatusBadge } from '@/design-system/primitives';
import type { AiWorkbenchActionId, AiWorkbenchActionModel } from './actionModel';
import type { AiWorkbenchProjection } from './projection';

const props = defineProps<{
  projection: AiWorkbenchProjection;
  actionModel: AiWorkbenchActionModel;
}>();

const emit = defineEmits<{
  action: [actionId: AiWorkbenchActionId];
}>();

const visibleActions = computed(() => {
  const actions = [props.actionModel.primary, ...props.actionModel.secondary].filter(action => action !== null);
  return actions.filter(action => ![
    'submitTask', 'answerClarification', 'acceptRecommendedAnswers', 'confirmParameters', 'updateResourceDecision'
  ].includes(action.id));
});
</script>

<template>
  <section
    class="ai-workbench-stage"
    data-ai-workbench-stage
    :aria-busy="projection.busy ? 'true' : undefined"
  >
    <div class="ai-workbench-stage__status">
      <CvStatusBadge
        :tone="projection.statusTone"
        :label="projection.statusLabel"
      />
      <div
        class="ai-workbench-stage__copy"
        aria-live="polite"
        aria-atomic="true"
      >
        <h2>{{ projection.currentStage }}</h2>
        <p>{{ projection.stageDescription }}</p>
      </div>
    </div>

    <dl class="ai-workbench-stage__facts">
      <div>
        <dt>待处理</dt>
        <dd>{{ projection.blockerCount }}</dd>
      </div>
      <div>
        <dt>下一步</dt>
        <dd>{{ projection.nextHint }}</dd>
      </div>
    </dl>

    <div
      v-if="visibleActions.length || actionModel.nextStagePlaceholder"
      class="ai-workbench-stage__actions"
    >
      <CvButton
        v-for="action in visibleActions"
        :key="action.id"
        size="sm"
        :variant="action.primary ? 'primary' : 'secondary'"
        :disabled="!action.enabled"
        :title="action.disabledReason || undefined"
        @click="emit('action', action.id)"
      >
        {{ action.label }}
      </CvButton>
      <span
        v-if="actionModel.nextStagePlaceholder"
        class="ai-workbench-stage__next-boundary"
        :title="actionModel.nextStagePlaceholder.disabledReason"
      >
        {{ actionModel.nextStagePlaceholder.label }}
      </span>
    </div>
  </section>
</template>

<style scoped>
.ai-workbench-stage {
  display: grid;
  grid-template-columns: minmax(260px, 0.9fr) minmax(320px, 1.1fr) auto;
  min-width: 0;
  align-items: center;
  gap: var(--cv-space-5);
  padding: var(--cv-space-3) var(--cv-space-4);
  border-block: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-page);
}
.ai-workbench-stage__status { display: flex; min-width: 0; align-items: center; gap: var(--cv-space-3); }
.ai-workbench-stage__copy { min-width: 0; }
.ai-workbench-stage__copy h2 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); line-height: var(--cv-line-height-tight); }
.ai-workbench-stage__copy p { margin: 2px 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-workbench-stage__facts { display: grid; grid-template-columns: 96px minmax(0, 1fr); min-width: 0; margin: 0; }
.ai-workbench-stage__facts div { min-width: 0; padding-inline: var(--cv-space-3); border-inline-start: 1px solid var(--cv-border-subtle); }
.ai-workbench-stage__facts dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.ai-workbench-stage__facts dd { margin: 2px 0 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.ai-workbench-stage__facts div:first-child dd { font-size: var(--cv-font-size-sm); font-variant-numeric: tabular-nums; font-weight: var(--cv-font-weight-semibold); }
.ai-workbench-stage__actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
.ai-workbench-stage__next-boundary { align-self: center; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }

@media (max-width: 1120px) {
  .ai-workbench-stage { grid-template-columns: minmax(0, 1fr) auto; }
  .ai-workbench-stage__facts { grid-column: 1 / -1; grid-row: 2; }
}
@media (max-width: 700px) {
  .ai-workbench-stage { grid-template-columns: 1fr; align-items: start; }
  .ai-workbench-stage__actions { justify-content: flex-start; }
}
</style>
