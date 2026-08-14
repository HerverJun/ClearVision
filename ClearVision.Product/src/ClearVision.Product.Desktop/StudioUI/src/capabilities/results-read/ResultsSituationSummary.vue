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
      <header class="results-situation-summary__heading">
        <h2 id="results-execution-axis">
          执行轴
        </h2>
        <p>任务是否完成</p>
      </header>
      <dl class="results-situation-summary__metrics">
        <div class="results-situation-summary__metric results-situation-summary__metric--anchor">
          <dt>检测次数</dt>
          <dd><strong>{{ statistics.totalAttemptCount.toLocaleString('zh-CN') }}</strong></dd>
        </div>
        <div class="results-situation-summary__metric results-situation-summary__metric--positive">
          <dt>执行成功</dt>
          <dd>
            <strong>{{ statistics.executionSucceededCount.toLocaleString('zh-CN') }}</strong>
            <small>{{ percent(executionSuccessRate) }}</small>
          </dd>
        </div>
        <div
          class="results-situation-summary__metric"
          :class="{ 'has-exception': statistics.executionFailureCount > 0 }"
        >
          <dt>执行失败</dt>
          <dd>
            <strong>{{ statistics.executionFailureCount.toLocaleString('zh-CN') }}</strong>
            <small>含超时 {{ statistics.timedOutCount }}</small>
          </dd>
        </div>
        <div class="results-situation-summary__metric results-situation-summary__metric--secondary">
          <dt>取消 / 跳过</dt>
          <dd><strong>{{ statistics.cancelledCount }} / {{ statistics.skippedCount }}</strong></dd>
        </div>
        <div class="results-situation-summary__metric results-situation-summary__metric--secondary">
          <dt>平均耗时</dt>
          <dd><strong>{{ Math.round(statistics.averageExecutionTimeMs).toLocaleString('zh-CN') }} ms</strong></dd>
        </div>
      </dl>
    </section>
    <section
      class="results-situation-summary__axis results-situation-summary__axis--decision"
      aria-labelledby="results-decision-axis"
    >
      <header class="results-situation-summary__heading">
        <h2 id="results-decision-axis">
          判定轴
        </h2>
        <p>合格与缺陷结论</p>
      </header>
      <dl class="results-situation-summary__metrics">
        <div class="results-situation-summary__metric results-situation-summary__metric--anchor">
          <dt>有效判定</dt>
          <dd>
            <strong>{{ statistics.validDecisionCount.toLocaleString('zh-CN') }}</strong>
            <small>覆盖 {{ percent(statistics.decisionCoverageRate) }}</small>
          </dd>
        </div>
        <div class="results-situation-summary__metric results-situation-summary__metric--positive">
          <dt>判定 OK</dt>
          <dd><strong>{{ statistics.okCount.toLocaleString('zh-CN') }}</strong></dd>
        </div>
        <div
          class="results-situation-summary__metric"
          :class="{ 'has-ng': statistics.ngCount > 0 }"
        >
          <dt>判定 NG</dt>
          <dd><strong>{{ statistics.ngCount.toLocaleString('zh-CN') }}</strong></dd>
        </div>
        <div class="results-situation-summary__metric results-situation-summary__metric--secondary">
          <dt>未判定 / 不适用</dt>
          <dd><strong>{{ statistics.undeterminedCount }} / {{ statistics.notApplicableCount }}</strong></dd>
        </div>
        <div class="results-situation-summary__metric results-situation-summary__metric--secondary">
          <dt>有效判定良率</dt>
          <dd><strong>{{ percent(statistics.yieldRate) }}</strong></dd>
        </div>
      </dl>
    </section>
  </section>
</template>

<style scoped>
.results-situation-summary {
  min-width: 0;
  display: grid;
  border-block: 1px solid var(--cv-border-subtle);
}

.results-situation-summary__axis {
  min-width: 0;
  display: grid;
  grid-template-columns: minmax(148px, 0.72fr) minmax(0, 5fr);
}

.results-situation-summary__axis + .results-situation-summary__axis {
  border-top: 1px solid var(--cv-border-subtle);
}

.results-situation-summary__heading,
.results-situation-summary__metric {
  min-width: 0;
  display: grid;
  align-content: center;
}

.results-situation-summary__heading {
  min-height: 58px;
  padding: var(--cv-space-2) var(--cv-space-4) var(--cv-space-2) 0;
  gap: 2px;
  border-left: 3px solid var(--cv-color-industrial-blue);
  padding-left: var(--cv-space-3);
}

.results-situation-summary__axis--decision .results-situation-summary__heading {
  border-left-color: var(--cv-color-status-ok-strong);
}

.results-situation-summary__heading h2,
.results-situation-summary__heading p {
  margin: 0;
}

.results-situation-summary__heading h2 {
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-semibold);
  line-height: var(--cv-line-height-tight);
}

.results-situation-summary__heading p,
.results-situation-summary__metric dt,
.results-situation-summary__metric small {
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-xs);
  line-height: var(--cv-line-height-normal);
}

.results-situation-summary__metrics {
  min-width: 0;
  margin: 0;
  display: grid;
  grid-template-columns: minmax(108px, 1.28fr) minmax(104px, 1.16fr) repeat(3, minmax(92px, 1fr));
}

.results-situation-summary__metric {
  min-height: 58px;
  padding: var(--cv-space-2) var(--cv-space-3);
  gap: 2px;
  border-left: 1px solid var(--cv-border-subtle);
}

.results-situation-summary__metric dt,
.results-situation-summary__metric dd {
  min-width: 0;
}

.results-situation-summary__metric dd {
  margin: 0;
  display: flex;
  align-items: baseline;
  flex-wrap: wrap;
  gap: 2px var(--cv-space-1);
}

.results-situation-summary__metric strong {
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-md);
  font-weight: var(--cv-font-weight-semibold);
  font-variant-numeric: tabular-nums;
  line-height: var(--cv-line-height-tight);
}

.results-situation-summary__metric--anchor strong,
.results-situation-summary__metric.has-exception strong,
.results-situation-summary__metric.has-ng strong {
  font-size: var(--cv-font-size-lg);
}

.results-situation-summary__metric--secondary strong {
  color: var(--cv-text-secondary);
  font-weight: var(--cv-font-weight-medium);
}

.results-situation-summary__metric.has-exception dt,
.results-situation-summary__metric.has-exception strong {
  color: var(--cv-color-status-error-strong);
}

.results-situation-summary__metric.has-ng dt,
.results-situation-summary__metric.has-ng strong {
  color: var(--cv-color-status-ng-strong);
}

@media (max-width: 700px) {
  .results-situation-summary__axis { grid-template-columns: 1fr; }

  .results-situation-summary__heading {
    min-height: 0;
    padding: var(--cv-space-2) 0;
  }

  .results-situation-summary__metrics {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .results-situation-summary__metric {
    border-top: 1px solid var(--cv-border-subtle);
  }

  .results-situation-summary__metric:nth-child(odd) { border-left: 0; }
  .results-situation-summary__metric:last-child { grid-column: 1 / -1; }
}

@media (forced-colors: active) {
  .results-situation-summary__heading { border-left-color: Highlight; }
  .results-situation-summary__metric.has-exception dt,
  .results-situation-summary__metric.has-exception strong,
  .results-situation-summary__metric.has-ng dt,
  .results-situation-summary__metric.has-ng strong { color: CanvasText; }
}
</style>
