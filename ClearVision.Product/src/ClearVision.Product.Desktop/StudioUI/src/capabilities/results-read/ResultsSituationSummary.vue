<script setup lang="ts">
import type { ResultsOutcomeStatistics } from './resultsContracts';

const props = defineProps<{ statistics: ResultsOutcomeStatistics }>();

function percent(value: number): string {
  return new Intl.NumberFormat('zh-CN', {
    style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1
  }).format(value);
}

function executionSuccessRate(): number {
  return props.statistics.totalAttemptCount > 0
    ? props.statistics.executionSucceededCount / props.statistics.totalAttemptCount
    : 0;
}
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
      <div><span>执行成功</span><strong>{{ statistics.executionSucceededCount.toLocaleString('zh-CN') }}</strong><small>{{ percent(executionSuccessRate()) }}</small></div>
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
.results-situation-summary { min-width: 0; border-block: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.results-situation-summary__axis { min-width: 0; display: grid; grid-template-columns: minmax(112px, .62fr) repeat(5, minmax(104px, 1fr)); }
.results-situation-summary__axis + .results-situation-summary__axis { border-top: 1px solid var(--cv-border-subtle); }
.results-situation-summary__axis header,
.results-situation-summary__axis > div { min-width: 0; min-height: 58px; padding: var(--cv-space-2) var(--cv-space-3); display: grid; align-content: center; gap: 2px; border-right: 1px solid var(--cv-border-subtle); }
.results-situation-summary__axis > div:last-child { border-right: 0; }
.results-situation-summary__axis header { border-top: 2px solid var(--cv-color-industrial-blue); background: var(--cv-surface-raised); }
.results-situation-summary__axis--decision header { border-top-color: var(--cv-color-brand-500); }
.results-situation-summary__axis header span { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); }
.results-situation-summary__axis span,
.results-situation-summary__axis small { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.results-situation-summary__axis strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-variant-numeric: tabular-nums; }
.results-situation-summary__axis .has-exception strong { color: var(--cv-color-status-error-strong); }
.results-situation-summary__axis .has-ng strong { color: var(--cv-color-status-ng-strong); }
@media (max-width: 1240px) {
  .results-situation-summary__axis { grid-template-columns: minmax(112px, .62fr) repeat(3, minmax(104px, 1fr)); }
  .results-situation-summary__axis header { grid-row: span 2; }
  .results-situation-summary__axis > div:nth-of-type(3) { border-right: 0; }
  .results-situation-summary__axis > div:nth-of-type(-n+3) { border-bottom: 1px solid var(--cv-border-subtle); }
}
@media (max-width: 700px) {
  .results-situation-summary__axis { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .results-situation-summary__axis header { grid-column: 1 / -1; grid-row: auto; min-height: 48px; border-right: 0; }
  .results-situation-summary__axis > div { border-bottom: 1px solid var(--cv-border-subtle); }
  .results-situation-summary__axis > div:nth-of-type(odd) { border-right: 1px solid var(--cv-border-subtle); }
  .results-situation-summary__axis > div:nth-of-type(even) { border-right: 0; }
  .results-situation-summary__axis > div:last-child { grid-column: 1 / -1; border-bottom: 0; }
}
</style>
