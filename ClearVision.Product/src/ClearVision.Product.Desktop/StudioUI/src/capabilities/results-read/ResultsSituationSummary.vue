<script setup lang="ts">
import { computed } from 'vue';
import type { ResultsOutcomeStatistics } from './resultsContracts';

const props = defineProps<{ statistics: ResultsOutcomeStatistics }>();

function percent(value: number): string {
  return new Intl.NumberFormat('zh-CN', {
    style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1
  }).format(value);
}

const executionSuccessRate = computed(() => {
  return props.statistics.totalAttemptCount > 0
    ? props.statistics.executionSucceededCount / props.statistics.totalAttemptCount
    : 0;
});
</script>

<template>
  <section
    class="results-situation-summary"
    aria-label="执行与判定双轴统计"
  >
    <section
      class="results-situation-summary__axis results-situation-summary__axis--execution"
      aria-labelledby="results-execution-axis"
    >
      <header>
        <span id="results-execution-axis">执行轴</span>
        <small>任务是否完成</small>
      </header>
      <div><span>检测次数</span><strong>{{ statistics.totalAttemptCount.toLocaleString('zh-CN') }}</strong></div>
      <div><span>执行成功</span><strong>{{ statistics.executionSucceededCount.toLocaleString('zh-CN') }}</strong><small>{{ percent(executionSuccessRate) }}</small></div>
      <div :class="{ 'has-exception': statistics.executionFailureCount > 0 }">
        <span>执行失败</span><strong>{{ statistics.executionFailureCount.toLocaleString('zh-CN') }}</strong><small>含超时 {{ statistics.timedOutCount }}</small>
      </div>
      <div><span>取消 / 跳过</span><strong>{{ statistics.cancelledCount }} / {{ statistics.skippedCount }}</strong></div>
      <div><span>平均耗时</span><strong>{{ Math.round(statistics.averageExecutionTimeMs).toLocaleString('zh-CN') }} ms</strong></div>
    </section>
    <section
      class="results-situation-summary__axis results-situation-summary__axis--decision"
      aria-labelledby="results-decision-axis"
    >
      <header>
        <span id="results-decision-axis">判定轴</span>
        <small>合格与缺陷结论</small>
      </header>
      <div><span>有效判定</span><strong>{{ statistics.validDecisionCount.toLocaleString('zh-CN') }}</strong><small>覆盖 {{ percent(statistics.decisionCoverageRate) }}</small></div>
      <div><span>判定 OK</span><strong>{{ statistics.okCount.toLocaleString('zh-CN') }}</strong></div>
      <div :class="{ 'has-ng': statistics.ngCount > 0 }">
        <span>判定 NG</span><strong>{{ statistics.ngCount.toLocaleString('zh-CN') }}</strong>
      </div>
      <div><span>未判定 / 不适用</span><strong>{{ statistics.undeterminedCount }} / {{ statistics.notApplicableCount }}</strong></div>
      <div><span>有效判定良率</span><strong>{{ percent(statistics.yieldRate) }}</strong></div>
    </section>
  </section>
</template>

<style scoped>
.results-situation-summary {
  min-width: 0;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--cv-space-3);
}

.results-situation-summary__axis {
  min-width: 0;
  overflow: hidden;
  display: grid;
  grid-template-columns: repeat(5, minmax(96px, 1fr));
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-md);
  background: var(--cv-surface-raised);
}

.results-situation-summary__axis header,
.results-situation-summary__axis > div {
  min-width: 0;
  display: grid;
  align-content: center;
}

.results-situation-summary__axis header {
  grid-column: 1 / -1;
  min-height: 46px;
  padding: var(--cv-space-2) var(--cv-space-4);
  align-items: baseline;
  grid-template-columns: auto 1fr;
  gap: var(--cv-space-2);
  border-bottom: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-page);
}

.results-situation-summary__axis > div {
  min-height: 68px;
  padding: var(--cv-space-2) var(--cv-space-3);
  gap: 2px;
}

.results-situation-summary__axis > div + div {
  border-left: 1px solid var(--cv-border-subtle);
}

.results-situation-summary__axis header span {
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-semibold);
}

.results-situation-summary__axis span,
.results-situation-summary__axis small {
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-xs);
  line-height: var(--cv-line-height-normal);
}

.results-situation-summary__axis strong {
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-lg);
  font-weight: var(--cv-font-weight-semibold);
  font-variant-numeric: tabular-nums;
  line-height: var(--cv-line-height-tight);
}

.results-situation-summary__axis .has-exception {
  background: var(--cv-color-status-error-soft);
}

.results-situation-summary__axis .has-exception strong {
  color: var(--cv-color-status-error-strong);
}

.results-situation-summary__axis .has-ng {
  background: var(--cv-color-status-ng-soft);
}

.results-situation-summary__axis .has-ng strong {
  color: var(--cv-color-status-ng-strong);
}

@media (max-width: 1400px) {
  .results-situation-summary { grid-template-columns: 1fr; }
}

@media (max-width: 700px) {
  .results-situation-summary__axis { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .results-situation-summary__axis header { min-height: 44px; }
  .results-situation-summary__axis > div { border-bottom: 1px solid var(--cv-border-subtle); }
  .results-situation-summary__axis > div + div { border-left: 0; }
  .results-situation-summary__axis > div:nth-of-type(odd) { border-right: 1px solid var(--cv-border-subtle); }
  .results-situation-summary__axis > div:last-child { grid-column: 1 / -1; border-bottom: 0; }
}
</style>
