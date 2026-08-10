<script setup lang="ts">
import { computed } from 'vue';
import { CvIconButton, CvInlineAlert, CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type { ResultAnalysisOwner } from './resultAnalysisOwner';

const props = defineProps<{ owner: ResultAnalysisOwner }>();

const projection = computed(() => props.owner.projection);
const distribution = computed(() => projection.value.distribution.data ?? null);
const trend = computed(() => projection.value.trend.data ?? null);
const report = computed(() => projection.value.report.data ?? null);
const visibleTrendPoints = computed(() => trend.value?.dataPoints.slice(-12) ?? []);
const maxDistributionCount = computed(() => Math.max(
  1,
  ...(distribution.value?.items.map(item => item.count) ?? [1])
));

function formatPercent(value: number): string {
  return new Intl.NumberFormat('zh-CN', {
    style: 'percent',
    minimumFractionDigits: 1,
    maximumFractionDigits: 1
  }).format(value);
}

function formatDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : new Intl.DateTimeFormat('zh-CN', {
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false
    }).format(date);
}

function phaseTone(phase: string): 'idle' | 'info' | 'ok' | 'warning' | 'ng' {
  if (phase === 'ready') return 'ok';
  if (phase === 'partial-failure') return 'warning';
  if (phase === 'error') return 'ng';
  if (phase === 'loading') return 'info';
  return 'idle';
}

function phaseLabel(phase: string): string {
  return ({
    idle: '等待读取',
    loading: '读取中',
    ready: '分析已更新',
    'partial-failure': '部分数据过期',
    error: '读取失败',
    disposed: '已关闭'
  } as Readonly<Record<string, string>>)[phase] ?? '状态未知';
}

function intervalLabel(interval: string): string {
  return ({
    minute: '每分钟',
    hour: '每小时',
    day: '每天'
  } as Readonly<Record<string, string>>)[interval] ?? '自定义间隔';
}

function refresh(): void {
  void props.owner.refresh({ force: true });
}
</script>

<template>
  <section
    class="results-analysis"
    data-capability="results-analysis"
    :data-analysis-phase="projection.phase"
  >
    <header class="results-analysis__header">
      <div>
        <h2>结果分析</h2>
        <p>按当前工程与筛选条件汇总趋势、缺陷分布和处理节拍。</p>
      </div>
      <div class="results-analysis__actions">
        <CvStatusBadge
          :tone="phaseTone(projection.phase)"
          :label="phaseLabel(projection.phase)"
        />
        <CvIconButton
          size="sm"
          label="刷新结果分析"
          :loading="projection.phase === 'loading'"
          data-testid="results-analysis-refresh"
          @click="refresh"
        >
          <CvIcon
            name="refresh"
            size="sm"
          />
        </CvIconButton>
      </div>
    </header>

    <CvInlineAlert
      v-if="projection.phase === 'error' || projection.phase === 'partial-failure'"
      :tone="projection.phase === 'error' ? 'error' : 'warning'"
      title="分析数据未完全更新"
    >
      {{ projection.message }}
    </CvInlineAlert>

    <div class="results-analysis__grid">
      <section
        class="results-analysis__section"
        aria-label="缺陷分布"
      >
        <div class="results-analysis__section-heading">
          <h3>缺陷分布</h3>
          <span>{{ distribution?.totalDefects ?? 0 }} 个缺陷</span>
        </div>
        <p
          v-if="!distribution || distribution.items.length === 0"
          class="results-analysis__empty"
        >
          当前筛选条件没有缺陷分布数据。
        </p>
        <ul
          v-else
          class="results-analysis__distribution"
        >
          <li
            v-for="item in distribution.items"
            :key="item.defectType"
          >
            <div class="results-analysis__distribution-label">
              <strong>{{ item.defectType }}</strong>
              <span>{{ item.count }} · {{ item.percentage.toFixed(1) }}%</span>
            </div>
            <div
              class="results-analysis__bar"
              aria-hidden="true"
            >
              <span :style="{ width: `${(item.count / maxDistributionCount) * 100}%` }" />
            </div>
          </li>
        </ul>
      </section>

      <section
        class="results-analysis__section"
        aria-label="趋势分析"
      >
        <div class="results-analysis__section-heading">
          <h3>检测趋势</h3>
          <span>{{ intervalLabel(trend?.interval ?? projection.interval) }} · {{ trend?.dataPoints.length ?? 0 }} 个时间点</span>
        </div>
        <p
          v-if="!trend || trend.dataPoints.length === 0"
          class="results-analysis__empty"
        >
          当前趋势窗口没有服务端数据。
        </p>
        <table
          v-else
          class="results-analysis__trend"
        >
          <thead>
            <tr><th>时间</th><th>检测</th><th>OK</th><th>NG</th><th>缺陷</th><th>良率</th></tr>
          </thead>
          <tbody>
            <tr
              v-for="point in visibleTrendPoints"
              :key="point.timestamp"
            >
              <td>{{ formatDateTime(point.timestamp) }}</td>
              <td>{{ point.totalCount }}</td>
              <td>{{ point.okCount }}</td>
              <td>{{ point.ngCount }}</td>
              <td>{{ point.defectCount }}</td>
              <td>{{ formatPercent(point.yieldRate) }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <section
        class="results-analysis__section results-analysis__section--summary"
        aria-label="分析摘要"
      >
        <div class="results-analysis__section-heading">
          <h3>分析摘要</h3>
          <span v-if="report">生成于 {{ formatDateTime(report.generatedAt) }}</span>
        </div>
        <dl class="results-analysis__summary">
          <div><dt>报告检测数</dt><dd>{{ report?.summary.totalCount ?? '—' }}</dd></div>
          <div><dt>报告 OK 率</dt><dd>{{ report ? formatPercent(report.summary.okRate) : '—' }}</dd></div>
          <div><dt>平均置信度</dt><dd>{{ report ? formatPercent(report.confidenceDistribution.averageConfidence) : '—' }}</dd></div>
          <div><dt>平均处理时间</dt><dd>{{ report ? `${Math.round(report.summary.averageProcessingTimeMs)} ms` : '—' }}</dd></div>
        </dl>
        <ul
          v-if="report?.recommendations.length"
          class="results-analysis__recommendations"
        >
          <li
            v-for="recommendation in report.recommendations"
            :key="recommendation"
          >
            {{ recommendation }}
          </li>
        </ul>
      </section>
    </div>
  </section>
</template>

<style scoped>
.results-analysis { min-width: 0; border-block: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.results-analysis__header { min-width: 0; display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); padding: var(--cv-space-3) var(--cv-space-4); border-bottom: 1px solid var(--cv-border-subtle); }
.results-analysis__header h2 { margin: 0; font-size: var(--cv-font-size-sm); }
.results-analysis__header p { margin: 2px 0 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.results-analysis__actions { display: flex; align-items: center; gap: var(--cv-space-2); }
.results-analysis__grid { min-width: 0; display: grid; grid-template-columns: minmax(220px, .85fr) minmax(360px, 1.35fr) minmax(240px, .8fr); }
.results-analysis__section { min-width: 0; min-height: 176px; padding: var(--cv-space-3) var(--cv-space-4); border-right: 1px solid var(--cv-border-subtle); }
.results-analysis__section:last-child { border-right: 0; }
.results-analysis__section-heading { display: flex; align-items: baseline; justify-content: space-between; gap: var(--cv-space-2); margin-bottom: var(--cv-space-2); }
.results-analysis__section-heading h3 { margin: 0; font-size: var(--cv-font-size-xs); }
.results-analysis__section-heading span { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); white-space: nowrap; }
.results-analysis__empty { margin: var(--cv-space-4) 0 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.results-analysis__distribution { display: grid; gap: var(--cv-space-2); margin: 0; padding: 0; list-style: none; }
.results-analysis__distribution-label { display: flex; justify-content: space-between; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.results-analysis__distribution-label strong { min-width: 0; overflow: hidden; color: var(--cv-text-primary); text-overflow: ellipsis; white-space: nowrap; }
.results-analysis__bar { height: 5px; margin-top: 3px; overflow: hidden; background: var(--cv-surface-sunken); }
.results-analysis__bar span { display: block; height: 100%; background: var(--cv-color-industrial-blue); }
.results-analysis__trend { width: 100%; border-collapse: collapse; font-size: var(--cv-font-size-xs); }
.results-analysis__trend th,.results-analysis__trend td { padding: 4px 5px; text-align: right; border-bottom: 1px solid var(--cv-border-subtle); font-variant-numeric: tabular-nums; }
.results-analysis__trend th:first-child,.results-analysis__trend td:first-child { text-align: left; }
.results-analysis__trend th { color: var(--cv-text-muted); font-weight: var(--cv-font-weight-medium); }
.results-analysis__summary { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); margin: 0; border-block: 1px solid var(--cv-border-subtle); }
.results-analysis__summary div { min-width: 0; padding: var(--cv-space-2); }
.results-analysis__summary dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.results-analysis__summary dd { margin: 2px 0 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); font-variant-numeric: tabular-nums; }
.results-analysis__recommendations { margin: var(--cv-space-2) 0 0; padding-left: var(--cv-space-4); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.results-analysis > :deep(.cv-inline-alert) { margin: var(--cv-space-3) var(--cv-space-4) 0; }
@media (max-width: 1180px) { .results-analysis__grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .results-analysis__section:nth-child(2) { border-right: 0; } .results-analysis__section:last-child { grid-column: 1 / -1; border-top: 1px solid var(--cv-border-subtle); } }
@media (max-width: 700px) { .results-analysis__header { align-items: flex-start; flex-direction: column; } .results-analysis__grid { grid-template-columns: 1fr; } .results-analysis__section { border-right: 0; border-bottom: 1px solid var(--cv-border-subtle); } .results-analysis__section:last-child { grid-column: auto; border-top: 0; border-bottom: 0; } }
</style>
