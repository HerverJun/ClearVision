<script setup lang="ts">
import { computed } from 'vue';
import { CvInlineAlert } from '@/design-system';
import type { SettingsFeedback } from './settingsViewModel';

const props = defineProps<{
  feedback: SettingsFeedback | null;
}>();

const tone = computed(() => {
  if (!props.feedback) return 'info' as const;
  if (props.feedback.kind === 'saved') return 'success' as const;
  if (props.feedback.kind === 'unknown') return 'warning' as const;
  if (props.feedback.kind === 'forbidden') return 'warning' as const;
  return 'error' as const;
});

const statusLabel = computed(() => {
  if (!props.feedback) return '';
  if (props.feedback.kind === 'saved') return '已完成';
  if (props.feedback.kind === 'unknown') return '结果未知';
  if (props.feedback.kind === 'forbidden') return '无权限';
  if (props.feedback.kind === 'cancelled') return '已取消';
  return '未完成';
});
</script>

<template>
  <div
    v-if="feedback"
    class="settings-write-feedback"
    :data-settings-feedback="feedback.kind"
  >
    <CvInlineAlert
      :tone="tone"
      :title="statusLabel"
    >
      {{ feedback.message }}
    </CvInlineAlert>
    <dl class="settings-write-feedback__semantics">
      <div>
        <dt>保存</dt>
        <dd>{{ feedback.savedLabel }}</dd>
      </div>
      <div>
        <dt>投影</dt>
        <dd>{{ feedback.effectiveLabel }}</dd>
      </div>
      <div>
        <dt>重载</dt>
        <dd>{{ feedback.restartLabel }}</dd>
      </div>
    </dl>
  </div>
</template>

<style scoped>
.settings-write-feedback {
  display: grid;
  gap: var(--cv-space-2);
  margin-top: var(--cv-space-4);
}

.settings-write-feedback__semantics {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--cv-space-2);
  margin: 0;
}

.settings-write-feedback__semantics > div {
  min-width: 0;
  padding: var(--cv-space-2) var(--cv-space-3);
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-page);
}

.settings-write-feedback__semantics dt {
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
}

.settings-write-feedback__semantics dd {
  margin: var(--cv-space-1) 0 0;
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-xs);
  overflow-wrap: anywhere;
}

@media (max-width: 620px) {
  .settings-write-feedback__semantics { grid-template-columns: 1fr; }
}
</style>
