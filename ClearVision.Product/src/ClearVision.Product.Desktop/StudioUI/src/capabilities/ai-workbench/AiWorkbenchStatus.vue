<script setup lang="ts">
import { computed } from 'vue';
import { CvPageState } from '@/design-system/patterns';
import {
  CvButton,
  CvDescriptionList,
  CvPanel,
  type CvDescriptionItem
} from '@/design-system/primitives';
import { aiWorkbenchActions, type AiWorkbenchActionId } from './actionModel';
import type { AiWorkbenchProjection } from './projection';
import type { AiSessionState } from './reducer';

const props = defineProps<{
  state: AiSessionState;
  projection: AiWorkbenchProjection;
}>();

const emit = defineEmits<{
  action: [actionId: AiWorkbenchActionId];
}>();

const actions = computed(() => aiWorkbenchActions(props.state));
const details = computed<readonly CvDescriptionItem[]>(() => {
  const session = props.state.session;
  if (!session) return Object.freeze([]);
  return Object.freeze([
    { key: 'session', label: '会话标识', value: session.sessionId, span: 2 },
    { key: 'revision', label: '状态版本', value: String(session.snapshot.revision) },
    { key: 'lifecycle', label: '会话状态', value: lifecycleLabel(session.snapshot.lifecycleState) },
    { key: 'project', label: '绑定工程', value: session.snapshot.projectId ?? '新工程会话' },
    { key: 'updated', label: '服务端更新时间', value: formatTimestamp(session.updatedAtUtc) }
  ]);
});

function lifecycleLabel(value: string): string {
  if (value === 'idle') return '空闲';
  if (value === 'planning') return '规划中';
  if (value === 'building') return '构建中';
  if (value.endsWith('_failed')) return '失败';
  if (value.endsWith('_cancelled')) return '已取消';
  return value;
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isFinite(date.getTime())
    ? new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'medium', hour12: false }).format(date)
    : '—';
}
</script>

<template>
  <CvPageState
    v-if="projection.pageState"
    :kind="projection.pageState"
    :title="projection.pageStateTitle"
    :description="projection.pageStateDescription"
    data-ai-session-state
  >
    <template
      v-if="actions.length"
      #actions
    >
      <CvButton
        v-for="action in actions"
        :key="action.id"
        size="sm"
        :variant="action.primary ? 'primary' : 'secondary'"
        @click="emit('action', action.id)"
      >
        {{ action.label }}
      </CvButton>
    </template>
  </CvPageState>

  <CvPanel
    v-else
    title="当前会话"
    data-ai-session-state="ready"
  >
    <template #actions>
      <CvButton
        v-for="action in actions"
        :key="action.id"
        size="sm"
        variant="quiet"
        @click="emit('action', action.id)"
      >
        {{ action.label }}
      </CvButton>
    </template>
    <CvDescriptionList
      :items="details"
      :columns="2"
      label="AI 会话状态"
    />
  </CvPanel>
</template>
