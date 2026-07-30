<script setup lang="ts">
import { computed } from 'vue';
import { CvButton, CvStatusBadge, type CvStatusTone } from '@/design-system';
import type { WorkspaceHandoffOwnerProjection } from '../workspaceOwner';

const props = defineProps<{
  handoff: WorkspaceHandoffOwnerProjection;
}>();

const emit = defineEmits<{
  discard: [];
}>();

const presentation = computed<Readonly<{
  label: string;
  tone: CvStatusTone;
  next: string;
}>>(() => {
  switch (props.handoff.phase) {
    case 'workspace-staging':
      return Object.freeze({ label: '正在装载候选', tone: 'info', next: '完成一次性接收后即可检查。' });
    case 'workspace-save-conflict':
      return Object.freeze({ label: '保存冲突', tone: 'warning', next: '使用现有冲突协调操作，候选仍保留在本地。' });
    case 'workspace-save-unknown-outcome':
      return Object.freeze({ label: '保存结果未知', tone: 'warning', next: '先核对保存结果，禁止重复提交。' });
    case 'workspace-saved':
      return Object.freeze({ label: '已正式保存', tone: 'ok', next: '后续编辑已归工作区工程草稿管理。' });
    default:
      return Object.freeze({ label: 'AI 候选，尚未保存', tone: 'warning', next: '检查画布、参数和资源后，使用现有“保存”操作。' });
  }
});

const canDiscard = computed(() => props.handoff.phase === 'workspace-staged-unsaved' ||
  props.handoff.phase === 'workspace-save-conflict');
</script>

<template>
  <section
    class="workspace-handoff-banner"
    data-workspace-handoff-banner
    :data-handoff-phase="handoff.phase"
    role="status"
  >
    <CvStatusBadge
      :tone="presentation.tone"
      :label="presentation.label"
    />
    <div class="workspace-handoff-banner__copy">
      <strong>{{ handoff.message }}</strong>
      <span>{{ presentation.next }} 交接本身不会自动保存或运行。</span>
    </div>
    <dl>
      <div><dt>候选</dt><dd>{{ handoff.build.operatorCount }} 个算子 / {{ handoff.build.connectionCount }} 条连线</dd></div>
      <div><dt>差异</dt><dd>新增 {{ handoff.build.diff.addedNodes.length }} / 修改 {{ handoff.build.diff.modifiedNodes.length }} / 删除 {{ handoff.build.diff.removedNodes.length }}</dd></div>
    </dl>
    <CvButton
      v-if="canDiscard"
      size="sm"
      variant="quiet"
      @click="emit('discard')"
    >
      放弃 AI 候选
    </CvButton>
  </section>
</template>

<style scoped>
.workspace-handoff-banner { display: grid; grid-template-columns: auto minmax(260px, 1fr) auto auto; min-width: 0; align-items: center; gap: var(--cv-space-4); padding: var(--cv-space-3) var(--cv-density-page-padding); border-block-end: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.workspace-handoff-banner__copy { display: grid; min-width: 0; gap: 2px; }
.workspace-handoff-banner__copy strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.workspace-handoff-banner__copy span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.workspace-handoff-banner dl { display: flex; margin: 0; }
.workspace-handoff-banner dl div { min-width: 110px; padding-inline: var(--cv-space-3); border-inline-start: 1px solid var(--cv-border-subtle); }
.workspace-handoff-banner dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.workspace-handoff-banner dd { margin: 2px 0 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); font-variant-numeric: tabular-nums; }
@media (max-width: 1100px) {
  .workspace-handoff-banner { grid-template-columns: auto minmax(0, 1fr) auto; }
  .workspace-handoff-banner dl { grid-column: 2; grid-row: 2; }
}
@media (max-width: 720px) {
  .workspace-handoff-banner { grid-template-columns: 1fr; align-items: start; }
  .workspace-handoff-banner dl { grid-column: auto; grid-row: auto; flex-wrap: wrap; }
  .workspace-handoff-banner dl div:first-child { padding-inline-start: 0; border-inline-start: 0; }
}
</style>
