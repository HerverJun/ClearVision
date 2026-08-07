import type { ReadQueryClient, ReadQueryDefinition, ReadQueryOwner } from '@/platform/query';
import {
  decodeDefectDistributionResponse,
  decodeResultsAnalysisReport,
  decodeResultsAnalysisTrend,
  type ResultsAnalysisFilters,
  type DefectDistribution,
  type ResultsAnalysisReport,
  type AnalysisTrend
} from './analysisContracts';
import { isResultsProjectId } from './resultsContracts';

export type AnalysisTrendInterval = 'Hour' | 'Day' | 'Week' | 'Month';

export interface AnalysisTrendWindow {
  readonly start: string;
  readonly end: string;
}

/** Keep server trend bounds ordered when a filter contains only one date. */
export function normalizeAnalysisTrendWindow(
  from: string,
  to: string,
  now = Date.now()
): AnalysisTrendWindow {
  const parsedFrom = from.trim() && !Number.isNaN(Date.parse(from)) ? Date.parse(from) : Number.NaN;
  const parsedTo = to.trim() && !Number.isNaN(Date.parse(to)) ? Date.parse(to) : Number.NaN;
  let start = Number.isNaN(parsedFrom)
    ? (Number.isNaN(parsedTo) ? now - 24 * 60 * 60 * 1000 : parsedTo - 24 * 60 * 60 * 1000)
    : parsedFrom;
  let end = Number.isNaN(parsedTo) ? now : parsedTo;

  if (Number.isNaN(parsedTo) && !Number.isNaN(parsedFrom) && parsedFrom > end) {
    end = parsedFrom;
  }
  if (start > end) {
    [start, end] = [end, start];
  }

  return Object.freeze({
    start: new Date(start).toISOString(),
    end: new Date(end).toISOString()
  });
}

function assertProjectId(value: string): string {
  const projectId = value.trim();
  if (!isResultsProjectId(projectId)) throw new TypeError('Analysis project id must be a non-empty UUID.');
  return projectId;
}

function appendOptionalDate(query: URLSearchParams, key: string, value: string): void {
  const normalized = value.trim();
  if (normalized && !Number.isNaN(Date.parse(normalized))) query.set(key, normalized);
}

function appendFilters(query: URLSearchParams, filters: ResultsAnalysisFilters): void {
  appendOptionalDate(query, 'startTime', filters.from);
  appendOptionalDate(query, 'endTime', filters.to);
  if (filters.outcome.trim()) query.set('status', filters.outcome.trim());
  if (filters.defectType.trim()) query.set('defectType', filters.defectType.trim());
}

function analysisPath(kind: 'defect-distribution' | 'report', projectId: string, filters: ResultsAnalysisFilters): string {
  const query = new URLSearchParams();
  appendFilters(query, filters);
  const suffix = query.toString();
  return `analysis/${kind}/${encodeURIComponent(assertProjectId(projectId))}${suffix ? `?${suffix}` : ''}`;
}

function trendPath(
  projectId: string,
  filters: ResultsAnalysisFilters,
  interval: AnalysisTrendInterval,
  trendStart: string,
  trendEnd: string
): string {
  const startTime = trendStart.trim();
  const endTime = trendEnd.trim();
  if (!startTime || Number.isNaN(Date.parse(startTime)) || !endTime || Number.isNaN(Date.parse(endTime))) {
    throw new TypeError('Trend analysis requires valid start and end times.');
  }
  if (Date.parse(startTime) > Date.parse(endTime)) throw new TypeError('Trend analysis period is reversed.');
  const query = new URLSearchParams({
    interval,
    startTime,
    endTime
  });
  if (filters.outcome.trim()) query.set('status', filters.outcome.trim());
  if (filters.defectType.trim()) query.set('defectType', filters.defectType.trim());
  return `analysis/trend/${encodeURIComponent(assertProjectId(projectId))}?${query.toString()}`;
}

export function createDefectDistributionDefinition(
  projectId: () => string,
  filters: () => ResultsAnalysisFilters
): ReadQueryDefinition<DefectDistribution> {
  return Object.freeze({
    key: () => `results-analysis:distribution:${analysisPath('defect-distribution', projectId(), filters())}`,
    path: () => analysisPath('defect-distribution', projectId(), filters()),
    decode: decodeDefectDistributionResponse,
    isEmpty: (value: DefectDistribution) => value.items.length === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createDefectDistributionQuery(
  client: ReadQueryClient,
  projectId: () => string,
  filters: () => ResultsAnalysisFilters
): ReadQueryOwner<DefectDistribution> {
  return client.createQuery(createDefectDistributionDefinition(projectId, filters));
}

export function createAnalysisTrendDefinition(
  projectId: () => string,
  filters: () => ResultsAnalysisFilters,
  interval: () => AnalysisTrendInterval,
  trendStart: () => string,
  trendEnd: () => string
): ReadQueryDefinition<AnalysisTrend> {
  return Object.freeze({
    key: () => `results-analysis:trend:${trendPath(projectId(), filters(), interval(), trendStart(), trendEnd())}`,
    path: () => trendPath(projectId(), filters(), interval(), trendStart(), trendEnd()),
    decode: decodeResultsAnalysisTrend,
    isEmpty: (value: AnalysisTrend) => value.dataPoints.length === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createAnalysisTrendQuery(
  client: ReadQueryClient,
  projectId: () => string,
  filters: () => ResultsAnalysisFilters,
  interval: () => AnalysisTrendInterval,
  trendStart: () => string,
  trendEnd: () => string
): ReadQueryOwner<AnalysisTrend> {
  return client.createQuery(createAnalysisTrendDefinition(projectId, filters, interval, trendStart, trendEnd));
}

export function createResultsAnalysisReportDefinition(
  projectId: () => string,
  filters: () => ResultsAnalysisFilters
): ReadQueryDefinition<ResultsAnalysisReport> {
  return Object.freeze({
    key: () => `results-analysis:report:${analysisPath('report', projectId(), filters())}`,
    path: () => analysisPath('report', projectId(), filters()),
    decode: decodeResultsAnalysisReport,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createResultsAnalysisReportQuery(
  client: ReadQueryClient,
  projectId: () => string,
  filters: () => ResultsAnalysisFilters
): ReadQueryOwner<ResultsAnalysisReport> {
  return client.createQuery(createResultsAnalysisReportDefinition(projectId, filters));
}
