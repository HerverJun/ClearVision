<script setup lang="ts">
import { computed } from 'vue';
import { CvStatusBadge } from '@/design-system/primitives';
import type { AiAgentRunEventV1 } from './contracts';

const props = defineProps<{
  events: readonly AiAgentRunEventV1[];
}>();

const stages = computed(() => {
  const definitions = [
    { id: 'context', label: '收集上下文', matches: ['plan.context.started', 'plan.context.completed'] },
    { id: 'semantic', label: '理解视觉任务', matches: ['semantic.started', 'semantic.completed', 'semantic.fallback.used', 'semantic.failed'] },
    { id: 'planner', label: '生成视觉方案', matches: ['plan.model.started', 'plan.model.completed', 'plan.model.timeout', 'plan.model.failed'] },
    { id: 'contract', label: '校验方案合同', matches: ['plan.contract.started', 'plan.contract.completed'] },
    { id: 'ready', label: '计算就绪条件', matches: ['plan.safety.completed', 'plan.completed'] }
  ];
  return definitions.map(definition => {
    const matched = props.events.filter(event => definition.matches.includes(event.eventType));
    const latest = matched.at(-1);
    const failed = matched.some(event => event.status === 'failed');
    const completed = matched.some(event => event.status === 'completed' || event.eventType.endsWith('.completed'));
    return Object.freeze({
      ...definition,
      status: failed ? 'error' as const : completed ? 'ok' as const : latest ? 'info' as const : 'idle' as const,
      statusLabel: failed ? '失败' : completed ? '完成' : latest ? '进行中' : '等待',
      summary: latest?.summary || '等待前序阶段完成。'
    });
  });
});
</script>

<template>
  <section
    class="ai-plan-progress"
    data-ai-plan-progress
    aria-labelledby="ai-plan-progress-title"
  >
    <header>
      <h2 id="ai-plan-progress-title">
        规划进度
      </h2>
      <span>公开阶段</span>
    </header>
    <ol>
      <li
        v-for="(stage, index) in stages"
        :key="stage.id"
        :class="`is-${stage.status}`"
      >
        <span class="ai-plan-progress__index">{{ index + 1 }}</span>
        <div><strong>{{ stage.label }}</strong><p>{{ stage.summary }}</p></div>
        <CvStatusBadge
          :tone="stage.status"
          :label="stage.statusLabel"
        />
      </li>
    </ol>
  </section>
</template>

<style scoped>
.ai-plan-progress { max-width: 980px; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-lg); background: var(--cv-surface-raised); }
.ai-plan-progress header { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); padding: var(--cv-space-3) var(--cv-density-panel-padding); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-plan-progress h2 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.ai-plan-progress header span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.ai-plan-progress ol { display: grid; margin: 0; padding: 0; list-style: none; }
.ai-plan-progress li { display: grid; grid-template-columns: 26px minmax(0, 1fr) auto; align-items: center; gap: var(--cv-space-3); padding: var(--cv-space-3) var(--cv-density-panel-padding); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-plan-progress li:last-child { border-block-end: 0; }
.ai-plan-progress__index { display: grid; width: 24px; height: 24px; place-items: center; border: 1px solid var(--cv-border-default); border-radius: 50%; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-variant-numeric: tabular-nums; }
.ai-plan-progress li.is-info .ai-plan-progress__index { border-color: var(--cv-color-status-info-border); background: var(--cv-color-status-info-soft); color: var(--cv-color-status-info-strong); }
.ai-plan-progress li.is-ok .ai-plan-progress__index { border-color: var(--cv-color-status-ok-border); background: var(--cv-color-status-ok-soft); color: var(--cv-color-status-ok-strong); }
.ai-plan-progress strong { display: block; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.ai-plan-progress p { margin: 2px 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
</style>
