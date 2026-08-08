<script setup lang="ts">
import { computed } from 'vue';
import {
  CvButton,
  CvInlineAlert,
  CvStatusBadge,
  type CvStatusTone
} from '@/design-system';
import type { LineSequenceOwner } from './lineSequenceOwner';

const props = defineProps<{
  owner: LineSequenceOwner;
}>();

const projection = props.owner.projection;
const busy = computed(() => ['analyzing', 'recommending', 'applying'].includes(projection.phase));
const status = computed<Readonly<{ label: string; tone: CvStatusTone }>>(() => {
  switch (projection.phase) {
    case 'analyzing': return { label: '分析中', tone: 'info' };
    case 'analyzed': return { label: '已分析', tone: 'info' };
    case 'recommending': return { label: '计算中', tone: 'info' };
    case 'recommended': return { label: '待应用', tone: projection.canApply ? 'warning' : 'idle' };
    case 'applying': return { label: '应用中', tone: 'info' };
    case 'applied': return { label: '已应用', tone: 'ok' };
    case 'stale': return { label: '已过期', tone: 'warning' };
    case 'error': return { label: '未完成', tone: 'error' };
    default: return { label: '待分析', tone: 'idle' };
  }
});
const recommendationEntries = computed(() => Object.entries(projection.recommendation?.finalParameters ?? {}));

function diagnosticLabel(code: string): string {
  const labels: Readonly<Record<string, string>> = {
    missing_expected_class: '缺少预期类别',
    duplicate_detected_class: '检测类别重复',
    detection_count_mismatch: '检测数量不一致',
    low_detection_confidence: '检测置信度偏低',
    sequence_mismatch: '线序不一致',
    missing_model: '模型资源缺失',
    missing_labels: '标签资源缺失'
  };
  return labels[code] ?? code;
}

function parameterLabel(name: string): string {
  const labels: Readonly<Record<string, string>> = {
    'BoxNms.ScoreThreshold': '候选框分数阈值',
    'BoxNms.IouThreshold': '候选框重叠阈值',
    'DeepLearning.Confidence': '模型置信度'
  };
  return labels[name] ?? name;
}

function formatValue(value: unknown): string {
  if (typeof value === 'number') return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value);
  if (value === null || value === undefined || value === '') return '未提供';
  return String(value);
}
</script>

<template>
  <section
    class="line-sequence"
    data-line-sequence-workbench
    :data-line-sequence-phase="projection.phase"
    :data-line-sequence-source-revision="projection.sourceFlowRevision ?? ''"
  >
    <header class="line-sequence__header">
      <div>
        <h3>线序辅助</h3>
        <small>分析与建议由后端计算</small>
      </div>
      <CvStatusBadge
        :tone="status.tone"
        :label="status.label"
      />
    </header>

    <p
      class="line-sequence__message"
      role="status"
      aria-live="polite"
    >
      {{ projection.message }}
    </p>

    <dl
      v-if="projection.analysis"
      class="line-sequence__summary"
    >
      <div>
        <dt>综合评分</dt>
        <dd>{{ projection.analysis.overallScore === null ? '未提供' : formatValue(projection.analysis.overallScore) }}</dd>
      </div>
      <div>
        <dt>诊断</dt>
        <dd>{{ projection.analysis.diagnosticCodes.length }} 项</dd>
      </div>
      <div>
        <dt>参数建议</dt>
        <dd>{{ projection.analysis.suggestions.length }} 项</dd>
      </div>
    </dl>

    <CvInlineAlert
      v-if="projection.analysis?.missingResources.length"
      tone="warning"
      title="资源未就绪"
      compact
    >
      <ul class="line-sequence__compact-list">
        <li
          v-for="resource in projection.analysis.missingResources"
          :key="`${resource.resourceType}-${resource.resourceKey}-${resource.diagnosticCode}`"
        >
          {{ resource.description || resource.resourceKey || resource.resourceType }}
        </li>
      </ul>
    </CvInlineAlert>

    <div
      v-if="projection.analysis?.diagnosticCodes.length"
      class="line-sequence__group"
    >
      <strong>诊断</strong>
      <ul class="line-sequence__compact-list">
        <li
          v-for="code in projection.analysis.diagnosticCodes"
          :key="code"
        >
          {{ diagnosticLabel(code) }}
          <code translate="no">{{ code }}</code>
        </li>
      </ul>
    </div>

    <div
      v-if="projection.analysis?.suggestions.length"
      class="line-sequence__group"
    >
      <strong>分析建议</strong>
      <ul class="line-sequence__suggestions">
        <li
          v-for="suggestion in projection.analysis.suggestions"
          :key="`${suggestion.parameterName}-${suggestion.reason}`"
        >
          <span>{{ parameterLabel(suggestion.parameterName) }}</span>
          <small>{{ suggestion.reason || suggestion.expectedImprovement }}</small>
        </li>
      </ul>
    </div>

    <div
      v-if="recommendationEntries.length"
      class="line-sequence__group"
      data-line-sequence-recommendation
    >
      <strong>推荐参数</strong>
      <dl class="line-sequence__parameters">
        <div
          v-for="[name, value] in recommendationEntries"
          :key="name"
        >
          <dt :title="name">
            {{ parameterLabel(name) }}
          </dt>
          <dd>{{ formatValue(value) }}</dd>
        </div>
      </dl>
    </div>

    <footer class="line-sequence__actions">
      <CvButton
        size="sm"
        :disabled="!projection.canAnalyze"
        :loading="projection.phase === 'analyzing'"
        loading-label="正在分析线序"
        @click="owner.analyze"
      >
        分析线序
      </CvButton>
      <CvButton
        size="sm"
        :disabled="!projection.canRecommend"
        :loading="projection.phase === 'recommending'"
        loading-label="正在计算参数建议"
        @click="owner.recommend"
      >
        计算建议
      </CvButton>
      <CvButton
        size="sm"
        variant="primary"
        :disabled="!projection.canApply || busy"
        :loading="projection.phase === 'applying'"
        loading-label="正在应用到草稿"
        @click="owner.applyRecommendation"
      >
        应用到草稿
      </CvButton>
    </footer>
  </section>
</template>

<style scoped>
.line-sequence { display: grid; gap: var(--cv-space-3); padding: 12px 14px; border-block-start: 1px solid var(--cv-border-subtle); background: color-mix(in srgb, var(--cv-color-status-info-soft) 28%, var(--cv-surface-raised)); }
.line-sequence__header { min-width: 0; display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-2); }
.line-sequence__header h3 { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.line-sequence__header small { display: block; margin-block-start: 2px; color: var(--cv-text-muted); font-size: 9px; }
.line-sequence__message { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); overflow-wrap: anywhere; }
.line-sequence__summary { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); margin: 0; }
.line-sequence__summary div { min-width: 0; padding-inline: var(--cv-space-2); border-inline-start: 1px solid var(--cv-border-subtle); }
.line-sequence__summary dt { color: var(--cv-text-muted); font-size: 9px; }
.line-sequence__summary dd { margin: 2px 0 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); font-variant-numeric: tabular-nums; }
.line-sequence__group { min-width: 0; display: grid; gap: var(--cv-space-2); }
.line-sequence__group > strong { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.line-sequence__compact-list,
.line-sequence__suggestions { display: grid; gap: var(--cv-space-1); margin: 0; padding-inline-start: 16px; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.line-sequence__compact-list li { overflow-wrap: anywhere; }
.line-sequence__compact-list code { display: block; color: var(--cv-text-muted); font-family: var(--cv-font-mono); font-size: 9px; }
.line-sequence__suggestions { padding: 0; list-style: none; }
.line-sequence__suggestions li { min-width: 0; display: grid; gap: 2px; padding-block-end: var(--cv-space-1); border-block-end: 1px solid var(--cv-border-subtle); }
.line-sequence__suggestions span { color: var(--cv-text-primary); }
.line-sequence__suggestions small { color: var(--cv-text-muted); overflow-wrap: anywhere; }
.line-sequence__parameters { display: grid; margin: 0; }
.line-sequence__parameters div { min-width: 0; display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: var(--cv-space-2); padding: 5px 0; border-block-end: 1px solid var(--cv-border-subtle); }
.line-sequence__parameters dt { overflow: hidden; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); text-overflow: ellipsis; white-space: nowrap; }
.line-sequence__parameters dd { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); font-variant-numeric: tabular-nums; }
.line-sequence__actions { display: flex; flex-wrap: wrap; gap: var(--cv-space-2); }
.line-sequence__actions :deep(.cv-button) { flex: 1 1 92px; }
</style>
