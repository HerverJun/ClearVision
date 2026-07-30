<script setup lang="ts">
import { computed } from 'vue';
import { CvStatusBadge, type CvStatusTone } from '@/design-system/primitives';
import type { AiAgentRunEventV1 } from './contracts';

const props = defineProps<{
  events: readonly AiAgentRunEventV1[];
}>();

interface PublicStage {
  readonly id: string;
  readonly label: string;
  readonly status: 'pending' | 'running' | 'completed' | 'failed';
  readonly summary: string;
}

const definitions = Object.freeze([
  { id: 'draft', label: '理解并生成流程', keys: ['plan_generation', 'resolve_build_intent', 'workflow_draft'] },
  { id: 'mapping', label: '映射算子与参数', keys: ['plan_selection', 'operator_pipeline', 'parameter_mapping'] },
  { id: 'validation', label: '结构校验', keys: ['validate_schema', 'canonical_build_contract'] },
  { id: 'dryrun', label: '运行预演', keys: ['metadata_dry_run'] },
  { id: 'readiness', label: '就绪检查', keys: ['package_readiness', 'operator_contract', 'release_review', 'apply_gate'] },
  { id: 'candidate', label: '生成候选', keys: ['artifact', 'run'] }
]);

const stages = computed<readonly PublicStage[]>(() => definitions.map(definition => {
  const matching = props.events.filter(event => definition.keys.some(key =>
    event.stage.toLowerCase().includes(key) || event.eventType === (key === 'run' ? 'run.completed' : '')));
  const latest = matching.at(-1);
  const status = matching.some(event => event.status === 'failed' || event.eventType === 'run.failed')
    ? 'failed'
    : matching.some(event => event.status === 'running')
      ? 'running'
      : matching.some(event => event.status === 'completed' || event.eventType === 'run.completed')
        ? 'completed'
        : 'pending';
  return Object.freeze({
    id: definition.id,
    label: definition.label,
    status,
    summary: latest?.summary || (status === 'pending' ? '等待前序阶段完成' : '')
  });
}));

function badge(stage: PublicStage): Readonly<{ label: string; tone: CvStatusTone }> {
  if (stage.status === 'completed') return Object.freeze({ label: '已完成', tone: 'ok' });
  if (stage.status === 'running') return Object.freeze({ label: '进行中', tone: 'info' });
  if (stage.status === 'failed') return Object.freeze({ label: '未通过', tone: 'error' });
  return Object.freeze({ label: '等待', tone: 'idle' });
}
</script>

<template>
  <section
    class="ai-build-progress"
    aria-labelledby="ai-build-progress-title"
    data-ai-build-progress
  >
    <header class="ai-build-progress__header">
      <h2 id="ai-build-progress-title">
        构建进度
      </h2>
      <span>{{ stages.filter(stage => stage.status === 'completed').length }} / {{ stages.length }}</span>
    </header>
    <ol class="ai-build-progress__list">
      <li
        v-for="(stage, index) in stages"
        :key="stage.id"
        :class="`is-${stage.status}`"
      >
        <span class="ai-build-progress__index">{{ index + 1 }}</span>
        <div>
          <strong>{{ stage.label }}</strong>
          <p>{{ stage.summary }}</p>
        </div>
        <CvStatusBadge v-bind="badge(stage)" />
      </li>
    </ol>
  </section>
</template>

<style scoped>
.ai-build-progress { min-width: 0; border-block: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); }
.ai-build-progress__header { display: flex; align-items: center; justify-content: space-between; padding: var(--cv-space-3) var(--cv-density-panel-padding); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-build-progress__header h2 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.ai-build-progress__header span { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); font-variant-numeric: tabular-nums; }
.ai-build-progress__list { display: grid; margin: 0; padding: 0; list-style: none; }
.ai-build-progress__list li { display: grid; grid-template-columns: 26px minmax(0, 1fr) auto; min-height: 52px; align-items: center; gap: var(--cv-space-3); padding: var(--cv-space-2) var(--cv-density-panel-padding); border-block-end: 1px solid var(--cv-border-subtle); }
.ai-build-progress__list li:last-child { border-block-end: 0; }
.ai-build-progress__index { display: grid; width: 22px; height: 22px; place-items: center; border: 1px solid var(--cv-border-default); border-radius: 50%; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.ai-build-progress__list strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.ai-build-progress__list p { margin: 2px 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
</style>
