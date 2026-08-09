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
    <div><span>检测次数</span><strong>{{ statistics.totalAttemptCount.toLocaleString('zh-CN') }}</strong></div>
    <div><span>执行成功率</span><strong>{{ percent(executionSuccessRate()) }}</strong><small>{{ statistics.executionSucceededCount }} / {{ statistics.totalAttemptCount }}</small></div>
    <div><span>判定覆盖率</span><strong>{{ percent(statistics.decisionCoverageRate) }}</strong><small>{{ statistics.validDecisionCount }} / {{ statistics.executionSucceededCount }}</small></div>
    <div><span>有效判定良率</span><strong>{{ percent(statistics.yieldRate) }}</strong><small>{{ statistics.okCount }} OK / {{ statistics.ngCount }} NG</small></div>
    <div><span>执行异常</span><strong>{{ statistics.executionFailureCount.toLocaleString('zh-CN') }}</strong></div>
    <div><span>平均耗时</span><strong>{{ Math.round(statistics.averageExecutionTimeMs).toLocaleString('zh-CN') }} ms</strong></div>
  </section>
</template>

<style scoped>
.results-situation-summary { min-width: 0; display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); border-block: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.results-situation-summary > div { min-width: 0; min-height: 48px; padding: 6px var(--cv-space-3); display: grid; align-content: center; gap: 1px; border-right: 1px solid var(--cv-border-subtle); }
.results-situation-summary > div:last-child { border-right: 0; }
.results-situation-summary span,.results-situation-summary small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.results-situation-summary strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-variant-numeric: tabular-nums; }
@media (max-width: 1240px) {
  .results-situation-summary { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .results-situation-summary > div:nth-child(3) { border-right: 0; }
  .results-situation-summary > div:nth-child(-n+3) { border-bottom: 1px solid var(--cv-border-subtle); }
}
@media (max-width: 640px) {
  .results-situation-summary { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .results-situation-summary > div:nth-child(3) { border-right: 1px solid var(--cv-border-subtle); }
  .results-situation-summary > div:nth-child(2n) { border-right: 0; }
  .results-situation-summary > div:nth-child(-n+4) { border-bottom: 1px solid var(--cv-border-subtle); }
}
</style>
