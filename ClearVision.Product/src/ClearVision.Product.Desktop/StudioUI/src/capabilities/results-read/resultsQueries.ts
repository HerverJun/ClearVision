import type {
  ReadQueryClient,
  ReadQueryDefinition,
  ReadQueryOwner
} from '@/platform/query';
import type { CanonicalInspectionOutcomeKind } from '@/shared/inspectionOutcome';
import {
  decodeLocalInspectionResultDetail,
  decodeLocalInspectionResultPage,
  decodeResultsProjects,
  decodeStationInspectionResultPage,
  isResultsProjectId,
  type LocalInspectionResultDetail,
  type LocalInspectionResultPage,
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
    decode: decodeLocalInspectionResultPage,
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
    decode: decodeLocalInspectionResultDetail,
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
