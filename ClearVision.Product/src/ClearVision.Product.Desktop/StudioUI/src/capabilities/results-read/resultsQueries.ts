import type {
  ReadQueryClient,
  ReadQueryDefinition,
  ReadQueryOwner
} from '@/platform/query';
import type { CanonicalInspectionOutcomeKind } from '@/shared/inspectionOutcome';
import {
  decodeInspectionHistoryComparison,
  decodeInspectionPreviousSuccess,
  decodeLocalInspectionResultDetail,
  decodeLocalInspectionResultPage,
  decodeResultsOutcomeStatistics,
  decodeResultsProjects,
  decodeStationInspectionResultPage,
  type InspectionHistoryComparison,
  type InspectionPreviousSuccessReference,
  isResultsProjectId,
  type LocalInspectionResultDetail,
  type LocalInspectionResultPage,
  type ResultsOutcomeStatistics,
  type ResultsProjectOption,
  type StationInspectionResultPage
} from './resultsContracts';

export type ResultsSource = 'local' | 'station';

export interface ResultsListFilters {
  readonly outcome: CanonicalInspectionOutcomeKind | '';
  readonly diagnosticCode: string;
  readonly from: string;
  readonly to: string;
  readonly page: number;
  readonly pageSize: number;
}

function appendDate(query: URLSearchParams, key: string, value: string): void {
  const normalized = value.trim();
  if (normalized && !Number.isNaN(Date.parse(normalized))) query.set(key, normalized);
}

function normalizePage(value: number): number {
  return Number.isInteger(value) && value > 0 ? value : 1;
}

function normalizePageSize(value: number): number {
  return Number.isInteger(value) && value > 0 ? Math.min(value, 200) : 20;
}

export function createLocalResultsPath(
  projectId: string,
  filters: ResultsListFilters
): string {
  if (!isResultsProjectId(projectId)) {
    throw new TypeError('Local results project id must be a non-empty UUID.');
  }
  const query = new URLSearchParams();
  appendDate(query, 'startTime', filters.from);
  appendDate(query, 'endTime', filters.to);
  if (filters.outcome) query.set('status', filters.outcome);
  query.set('pageIndex', String(normalizePage(filters.page) - 1));
  query.set('pageSize', String(normalizePageSize(filters.pageSize)));
  return `inspection/history/${projectId}?${query.toString()}`;
}

export function createLocalResultDetailPath(projectId: string, resultId: string): string {
  if (!isResultsProjectId(projectId) || !isResultsProjectId(resultId)) {
    throw new TypeError('Local result detail ids must be non-empty UUIDs.');
  }
  return `inspection/history/${projectId}/${resultId}`;
}

export function createLocalStatisticsPath(
  projectId: string,
  filters: ResultsListFilters
): string {
  if (!isResultsProjectId(projectId)) {
    throw new TypeError('Local results statistics project id must be a non-empty UUID.');
  }
  const query = new URLSearchParams();
  appendDate(query, 'startTime', filters.from);
  appendDate(query, 'endTime', filters.to);
  if (filters.outcome) query.set('status', filters.outcome);
  const suffix = query.toString();
  return suffix ? `inspection/statistics/${projectId}?${suffix}` : `inspection/statistics/${projectId}`;
}

export function createPreviousSuccessPath(projectId: string, resultId: string): string {
  if (!isResultsProjectId(projectId) || !isResultsProjectId(resultId)) {
    throw new TypeError('Previous-success ids must be non-empty UUIDs.');
  }
  return `inspection/history/${projectId}/${resultId}/previous-success?limit=50`;
}

export function createComparisonPath(
  projectId: string,
  leftId: string,
  rightId: string
): string {
  if (![projectId, leftId, rightId].every(isResultsProjectId)) {
    throw new TypeError('Comparison ids must be non-empty UUIDs.');
  }
  const query = new URLSearchParams({ leftId, rightId });
  return `inspection/history/${projectId}/compare?${query.toString()}`;
}

export function createStationResultsPath(filters: ResultsListFilters): string {
  const query = new URLSearchParams();
  appendDate(query, 'from', filters.from);
  appendDate(query, 'to', filters.to);
  if (filters.outcome) query.set('status', filters.outcome);
  const diagnosticCode = filters.diagnosticCode.trim();
  if (diagnosticCode) query.set('diagnosticCode', diagnosticCode);
  query.set('pageIndex', String(normalizePage(filters.page) - 1));
  query.set('pageSize', String(normalizePageSize(filters.pageSize)));
  return `stations/results?${query.toString()}`;
}

export function createStationStatisticsPath(filters: ResultsListFilters): string {
  const query = new URLSearchParams();
  appendDate(query, 'from', filters.from);
  appendDate(query, 'to', filters.to);
  if (filters.outcome) query.set('status', filters.outcome);
  const diagnosticCode = filters.diagnosticCode.trim();
  if (diagnosticCode) query.set('diagnosticCode', diagnosticCode);
  const suffix = query.toString();
  return suffix ? `stations/statistics?${suffix}` : 'stations/statistics';
}

export function createResultsProjectsDefinition(): ReadQueryDefinition<readonly ResultsProjectOption[]> {
  return Object.freeze({
    key: 'results:projects',
    path: 'projects',
    decode: decodeResultsProjects,
    isEmpty: (projects: readonly ResultsProjectOption[]) => projects.length === 0,
    protected: true,
    cacheTimeMs: 10_000
  });
}

export function createResultsProjectsQuery(
  client: ReadQueryClient
): ReadQueryOwner<readonly ResultsProjectOption[]> {
  return client.createQuery(createResultsProjectsDefinition());
}

export function createLocalResultsDefinition(
  projectId: () => string,
  filters: () => ResultsListFilters
): ReadQueryDefinition<LocalInspectionResultPage> {
  return Object.freeze({
    key: () => `results:local:${createLocalResultsPath(projectId(), filters())}`,
    path: () => createLocalResultsPath(projectId(), filters()),
    decode: (payload: unknown) => decodeLocalInspectionResultPage(payload, projectId()),
    isEmpty: (page: LocalInspectionResultPage) => page.totalCount === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createLocalResultsQuery(
  client: ReadQueryClient,
  projectId: () => string,
  filters: () => ResultsListFilters
): ReadQueryOwner<LocalInspectionResultPage> {
  return client.createQuery(createLocalResultsDefinition(projectId, filters));
}

export function createLocalResultDetailDefinition(
  projectId: () => string,
  resultId: () => string
): ReadQueryDefinition<LocalInspectionResultDetail> {
  return Object.freeze({
    key: () => `results:local-detail:${projectId()}:${resultId()}`,
    path: () => createLocalResultDetailPath(projectId(), resultId()),
    decode: (payload: unknown) => decodeLocalInspectionResultDetail(payload, {
      projectId: projectId(),
      resultId: resultId()
    }),
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createLocalResultDetailQuery(
  client: ReadQueryClient,
  projectId: () => string,
  resultId: () => string
): ReadQueryOwner<LocalInspectionResultDetail> {
  return client.createQuery(createLocalResultDetailDefinition(projectId, resultId));
}

export function createLocalStatisticsDefinition(
  projectId: () => string,
  filters: () => ResultsListFilters
): ReadQueryDefinition<ResultsOutcomeStatistics> {
  return Object.freeze({
    key: () => `results:local-statistics:${createLocalStatisticsPath(projectId(), filters())}`,
    path: () => createLocalStatisticsPath(projectId(), filters()),
    decode: decodeResultsOutcomeStatistics,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createLocalStatisticsQuery(
  client: ReadQueryClient,
  projectId: () => string,
  filters: () => ResultsListFilters
): ReadQueryOwner<ResultsOutcomeStatistics> {
  return client.createQuery(createLocalStatisticsDefinition(projectId, filters));
}

export function createPreviousSuccessDefinition(
  projectId: () => string,
  resultId: () => string
): ReadQueryDefinition<InspectionPreviousSuccessReference> {
  return Object.freeze({
    key: () => `results:previous-success:${projectId()}:${resultId()}`,
    path: () => createPreviousSuccessPath(projectId(), resultId()),
    decode: (payload: unknown) => decodeInspectionPreviousSuccess(payload, {
      projectId: projectId(),
      resultId: resultId()
    }),
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createPreviousSuccessQuery(
  client: ReadQueryClient,
  projectId: () => string,
  resultId: () => string
): ReadQueryOwner<InspectionPreviousSuccessReference> {
  return client.createQuery(createPreviousSuccessDefinition(projectId, resultId));
}

export function createComparisonDefinition(
  projectId: () => string,
  leftId: () => string,
  rightId: () => string
): ReadQueryDefinition<InspectionHistoryComparison> {
  return Object.freeze({
    key: () => `results:comparison:${projectId()}:${leftId()}:${rightId()}`,
    path: () => createComparisonPath(projectId(), leftId(), rightId()),
    decode: (payload: unknown) => decodeInspectionHistoryComparison(payload, {
      projectId: projectId(),
      leftResultId: leftId(),
      rightResultId: rightId()
    }),
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createComparisonQuery(
  client: ReadQueryClient,
  projectId: () => string,
  leftId: () => string,
  rightId: () => string
): ReadQueryOwner<InspectionHistoryComparison> {
  return client.createQuery(createComparisonDefinition(projectId, leftId, rightId));
}

export function createStationResultsDefinition(
  filters: () => ResultsListFilters
): ReadQueryDefinition<StationInspectionResultPage> {
  return Object.freeze({
    key: () => `results:station:${createStationResultsPath(filters())}`,
    path: () => createStationResultsPath(filters()),
    decode: decodeStationInspectionResultPage,
    isEmpty: (page: StationInspectionResultPage) => page.totalCount === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationResultsQuery(
  client: ReadQueryClient,
  filters: () => ResultsListFilters
): ReadQueryOwner<StationInspectionResultPage> {
  return client.createQuery(createStationResultsDefinition(filters));
}

export function createStationStatisticsDefinition(
  filters: () => ResultsListFilters
): ReadQueryDefinition<ResultsOutcomeStatistics> {
  return Object.freeze({
    key: () => `results:station-statistics:${createStationStatisticsPath(filters())}`,
    path: () => createStationStatisticsPath(filters()),
    decode: decodeResultsOutcomeStatistics,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationStatisticsQuery(
  client: ReadQueryClient,
  filters: () => ResultsListFilters
): ReadQueryOwner<ResultsOutcomeStatistics> {
  return client.createQuery(createStationStatisticsDefinition(filters));
}
