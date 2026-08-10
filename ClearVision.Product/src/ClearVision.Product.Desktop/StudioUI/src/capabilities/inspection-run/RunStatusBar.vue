<script setup lang="ts">
import { computed } from 'vue';
import { CvButton, CvStatusBadge, type CvStatusTone } from '@/design-system';
import { CvIcon } from '@/design-system/icons';

const props = withDefaults(defineProps<{
  phaseLabel: string;
  tone: CvStatusTone;
  message?: string | null;
  connected: boolean;
  reconnectAttempt?: number;
  pending: boolean;
  canStart: boolean;
  canStop: boolean;
  canReconcile: boolean;
  admissionLabel: string;
  admissionTone: CvStatusTone;
  blockerCount?: number;
  blockerMessage?: string | null;
  startTestId?: string;
  stopTestId?: string;
  reconcileTestId?: string;
}>(), {
  message: null,
  reconnectAttempt: 0,
  blockerCount: 0,
  blockerMessage: null,
  startTestId: 'run-console-start',
  stopTestId: 'run-console-stop',
  reconcileTestId: 'run-console-reconcile'
});

const emit = defineEmits<{
  checkAdmission: [];
  start: [];
  stop: [];
  reconcile: [];
  details: [];
}>();

const inspectionLabel = computed(() => props.admissionLabel
  .replace('准入阻断', '运行检查阻断')
  .replace('准入通过', '运行检查通过')
  .replace('准入检查中', '正在检查运行条件')
  .replace('待检查准入', '待检查运行条件'));
</script>

<template>
  <section
    class="run-status-bar"
    data-testid="run-status-bar"
    aria-label="正式运行状态"
  >
    <div class="run-status-bar__state">
      <span class="run-status-bar__eyebrow">正式运行</span>
      <CvStatusBadge
        :tone="tone"
        :label="phaseLabel"
      />
      <span
        class="run-status-bar__connection"
        :class="{ 'is-connected': connected }"
      >
        {{ connected ? '实时已连接' : reconnectAttempt ? `恢复中 ${reconnectAttempt}` : '状态已读取' }}
      </span>
    </div>

    <div class="run-status-bar__admission">
      <CvStatusBadge
        :tone="admissionTone"
        :label="inspectionLabel"
      />
      <span
        v-if="blockerCount > 0"
        class="run-status-bar__blocker"
        :title="blockerMessage || undefined"
      >
        阻断 {{ blockerCount }} 项<span v-if="blockerMessage"> · {{ blockerMessage }}</span>
      </span>
      <span
        v-else-if="message"
        class="run-status-bar__message"
        :title="message"
      >{{ message }}</span>
    </div>

    <div class="run-status-bar__actions">
      <CvButton
        size="sm"
        variant="quiet"
        :disabled="pending || canStop"
        data-testid="run-console-admission-refresh"
        aria-label="检查正式运行条件"
        title="重新检查已保存工程与运行条件"
        @click="emit('checkAdmission')"
      >
        <template #leading>
          <CvIcon
            name="refresh"
            size="sm"
          />
        </template>
        检查条件
      </CvButton>
      <CvButton
        size="sm"
        variant="primary"
        :disabled="!canStart || pending"
        :loading="pending && !canStop"
        :data-testid="startTestId"
        aria-label="开始正式运行"
        @click="emit('start')"
      >
        <template #leading>
          <CvIcon
            name="play"
            size="sm"
          />
        </template>
        正式运行
      </CvButton>
      <CvButton
        v-if="canStop"
        size="sm"
        variant="danger"
        :data-testid="stopTestId"
        aria-label="停止正式运行"
        @click="emit('stop')"
      >
        <template #leading>
          <CvIcon
            name="square"
            size="sm"
          />
        </template>
        停止
      </CvButton>
      <CvButton
        v-if="canReconcile"
        size="sm"
        variant="secondary"
        :data-testid="reconcileTestId"
        :aria-label="phaseLabel === '运行结果待确认' ? '核对正式运行结果' : '查询正式运行结果'"
        @click="emit('reconcile')"
      >
        <template #leading>
          <CvIcon
            name="refresh"
            size="sm"
          />
        </template>
        {{ phaseLabel === '运行结果待确认' ? '核对结果' : '查询运行结果' }}
      </CvButton>
      <CvButton
        size="sm"
        variant="quiet"
        data-testid="workspace-run-details"
        aria-label="查看正式运行详情"
        title="查看运行身份、准入详情与近期结果"
        @click="emit('details')"
      >
        <template #leading>
          <CvIcon
            name="info"
            size="sm"
          />
        </template>
        运行详情
      </CvButton>
      <slot name="result-action" />
    </div>
  </section>
</template>

<style scoped>
.run-status-bar {
  min-width: 0;
  min-height: 42px;
  display: grid;
  grid-template-columns: minmax(220px, auto) minmax(220px, 1fr) auto;
  align-items: center;
  gap: var(--cv-space-3);
  padding: 3px var(--cv-space-3);
  border-bottom: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
}
.run-status-bar__state,
.run-status-bar__admission,
.run-status-bar__actions {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: var(--cv-space-2);
}
.run-status-bar__eyebrow {
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
  font-weight: var(--cv-font-weight-semibold);
  white-space: nowrap;
}
.run-status-bar__connection,
.run-status-bar__message,
.run-status-bar__blocker {
  min-width: 0;
  overflow: hidden;
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.run-status-bar__connection.is-connected { color: var(--cv-color-status-ok-strong); }
.run-status-bar__admission { overflow: hidden; }
.run-status-bar__blocker { color: var(--cv-color-status-warning-strong); }
.run-status-bar__actions { justify-content: flex-end; }
.run-status-bar__actions :deep(.cv-button) { flex: 0 0 auto; }
.run-status-bar__actions :deep(a) {
  color: var(--cv-color-link);
  font-size: var(--cv-font-size-2xs);
  font-weight: var(--cv-font-weight-medium);
  white-space: nowrap;
}
.run-status-bar__actions :deep(a:hover) { text-decoration: underline; }
.run-status-bar__actions :deep(a:focus-visible) {
  outline: 2px solid var(--cv-focus-ring-color);
  outline-offset: 2px;
}

@media (max-width: 1180px) {
  .run-status-bar { grid-template-columns: minmax(180px, 1fr) auto; }
  .run-status-bar__admission { grid-column: 1 / -1; grid-row: 2; }
  .run-status-bar__actions { grid-column: 2; grid-row: 1; }
}

@media (max-width: 760px) {
  .run-status-bar { grid-template-columns: 1fr auto; gap: var(--cv-space-1); }
  .run-status-bar__state { overflow: hidden; }
  .run-status-bar__connection { display: none; }
  .run-status-bar__admission { grid-column: 1 / -1; }
  .run-status-bar__actions :deep(.cv-button__visual-label) { display: none; }
  .run-status-bar__actions :deep(.cv-button--sm) { width: 28px; padding-inline: 0; }
}
</style>
