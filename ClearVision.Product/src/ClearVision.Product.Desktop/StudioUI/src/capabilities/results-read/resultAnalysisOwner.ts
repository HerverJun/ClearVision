import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import type { ReadQueryClient, ReadQueryState } from '@/platform/query';
import type {
  DefectDistribution,
  ResultsAnalysisFilters,
  ResultsAnalysisReport,
  AnalysisTrend
} from './analysisContracts';
import {
  createAnalysisTrendQuery,
  createDefectDistributionQuery,
  createResultsAnalysisReportQuery,
  type AnalysisTrendInterval
} from './analysisQueries';

export type ResultAnalysisPhase = 'idle' | 'loading' | 'ready' | 'partial-failure' | 'error' | 'disposed';

export interface ResultAnalysisProjection {
  readonly phase: ResultAnalysisPhase;
  readonly projectId: string;
  readonly distribution: ReadQueryState<DefectDistribution>;
  readonly trend: ReadQueryState<AnalysisTrend>;
  readonly report: ReadQueryState<ResultsAnalysisReport>;
  readonly interval: AnalysisTrendInterval;
  readonly message: string;
}

type MutableProjection = { -readonly [Key in keyof ResultAnalysisProjection]: ResultAnalysisProjection[Key] };

export interface ResultAnalysisOwner {
  readonly projection: DeepReadonly<ResultAnalysisProjection>;
  refresh(options?: Readonly<{ force?: boolean }>): Promise<void>;
  dispose(reason?: string): void;
}

function messageFor(states: readonly ReadQueryState<unknown>[]): string {
  const failure = states.find(state => state.failure)?.failure;
  if (failure) return failure.message;
  if (states.some(state => state.phase === 'loading')) return '正在读取服务端分析投影。';
  return '结果分析将使用当前筛选条件重新读取服务端数据。';
}

function phaseFor(states: readonly ReadQueryState<unknown>[]): ResultAnalysisPhase {
  if (states.every(state => state.phase === 'idle')) return 'idle';
  if (states.some(state => state.phase === 'loading')) return 'loading';
  const hasData = states.some(state => state.data !== undefined);
  const hasFailure = states.some(state => [
    'unauthorized', 'forbidden', 'not-found', 'error', 'stale', 'partial-failure'
  ].includes(state.phase));
  return hasFailure ? (hasData ? 'partial-failure' : 'error') : 'ready';
}

export function createResultAnalysisOwner(options: {
  readonly projectId: string;
  readonly queries: ReadQueryClient;
  readonly filters: () => ResultsAnalysisFilters;
  readonly interval?: AnalysisTrendInterval;
  readonly trendStart: () => string;
  readonly trendEnd: () => string;
}): ResultAnalysisOwner {
  const interval = options.interval ?? 'Hour';
  const distribution = createDefectDistributionQuery(options.queries, () => options.projectId, options.filters);
  const trend = createAnalysisTrendQuery(
    options.queries,
    () => options.projectId,
    options.filters,
    () => interval,
    options.trendStart,
    options.trendEnd
  );
  const report = createResultsAnalysisReportQuery(options.queries, () => options.projectId, options.filters);
  const state = reactive<MutableProjection>({
    phase: 'idle',
    projectId: options.projectId,
    distribution: distribution.state.value,
    trend: trend.state.value,
    report: report.state.value,
    interval,
    message: '结果分析将使用当前筛选条件重新读取服务端数据。'
  });
  let disposed = false;

  function update(): void {
    if (disposed) return;
    state.distribution = distribution.state.value;
    state.trend = trend.state.value;
    state.report = report.state.value;
    const states = [state.distribution, state.trend, state.report];
    state.phase = phaseFor(states);
    state.message = messageFor(states);
  }

  const stop = [
    watch(() => distribution.state.value, update, { immediate: true }),
    watch(() => trend.state.value, update),
    watch(() => report.state.value, update)
  ];

  const owner: ResultAnalysisOwner = Object.freeze({
    projection: readonly(state),
    async refresh(refreshOptions = {}): Promise<void> {
      if (disposed) return;
      await Promise.all([
        distribution.refresh(refreshOptions),
        trend.refresh(refreshOptions),
        report.refresh(refreshOptions)
      ]);
      update();
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      for (const stopWatch of stop) stopWatch();
      distribution.dispose();
      trend.dispose();
      report.dispose();
      state.phase = 'disposed';
      state.message = '结果分析已关闭。';
    }
  });

  return owner;
}
