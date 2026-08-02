import type {
  ReadQueryClient,
  ReadQueryDefinition,
  ReadQueryOwner
} from '@/platform/query';
import {
  decodeStationAdminDetails,
  decodeStationAudits,
  decodeStationCommands,
  decodeStationHealth,
  decodeStationList,
  decodeStationLogs,
  decodeStationPackages,
  decodeStationResults,
  decodeStationResultsPage,
  decodeStationStatistics,
  decodeStationSummary,
  type StationAdminDetails,
  type StationAudit,
  type StationCommand,
  type StationHealthSnapshot,
  type StationLog,
  type StationPackage,
  type StationResult,
  type StationResultsPage,
  type StationStatistics,
  type StationStatus,
  type StationSummary
} from './stationContracts';

export const stationPollingIntervalMs = 15_000;
export const defaultStationDetailTake = 50;

export interface StationStatisticsFilters {
  readonly range?: string;
  readonly from?: string;
  readonly to?: string;
  readonly stationId?: string;
  readonly status?: string;
  readonly diagnosticCode?: string;
}

export interface StationResultsFilters {
  readonly stationId?: string;
  readonly from?: string;
  readonly to?: string;
  readonly status?: string;
  readonly diagnosticCode?: string;
  readonly pageIndex?: number;
  readonly pageSize?: number;
}

function nonEmpty(value: string | undefined): string | undefined {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}

function appendText(query: URLSearchParams, key: string, value: string | undefined): void {
  const normalized = nonEmpty(value);
  if (normalized) query.set(key, normalized);
}

function requireStationId(stationId: string): string {
  const normalized = stationId.trim();
  if (!normalized) throw new TypeError('Station id must be a non-empty string.');
  return normalized;
}

function requireTake(take: number): number {
  if (!Number.isInteger(take) || take < 1 || take > 500) {
    throw new RangeError('Station detail take must be an integer between 1 and 500.');
  }
  return take;
}

function requirePageValue(value: number, label: string, minimum: number): number {
  if (!Number.isInteger(value) || value < minimum) {
    throw new RangeError(`${label} must be an integer greater than or equal to ${minimum}.`);
  }
  return value;
}

export function createStationsPath(): string {
  return 'stations';
}

export function createStationSummaryPath(): string {
  return 'stations/summary';
}

export function createStationStatisticsPath(filters: StationStatisticsFilters): string {
  const query = new URLSearchParams();
  appendText(query, 'range', filters.range);
  appendText(query, 'from', filters.from);
  appendText(query, 'to', filters.to);
  appendText(query, 'stationId', filters.stationId);
  appendText(query, 'status', filters.status);
  appendText(query, 'diagnosticCode', filters.diagnosticCode);
  const suffix = query.toString();
  return suffix ? `stations/statistics?${suffix}` : 'stations/statistics';
}

export function createStationResultsPagePath(filters: StationResultsFilters): string {
  const query = new URLSearchParams();
  appendText(query, 'stationId', filters.stationId);
  appendText(query, 'from', filters.from);
  appendText(query, 'to', filters.to);
  appendText(query, 'status', filters.status);
  appendText(query, 'diagnosticCode', filters.diagnosticCode);
  if (filters.pageIndex !== undefined) {
    query.set('pageIndex', String(requirePageValue(filters.pageIndex, 'Page index', 0)));
  }
  if (filters.pageSize !== undefined) {
    const pageSize = requirePageValue(filters.pageSize, 'Page size', 1);
    if (pageSize > 500) throw new RangeError('Page size must not exceed 500.');
    query.set('pageSize', String(pageSize));
  }
  const suffix = query.toString();
  return suffix ? `stations/results?${suffix}` : 'stations/results';
}

export function createStationResultsPath(stationId: string, take = defaultStationDetailTake): string {
  return `stations/${encodeURIComponent(requireStationId(stationId))}/results?take=${requireTake(take)}`;
}

export function createStationHealthPath(stationId: string, take = defaultStationDetailTake): string {
  return `stations/${encodeURIComponent(requireStationId(stationId))}/health?take=${requireTake(take)}`;
}

export function createStationAdminDetailsPath(stationId: string): string {
  return `stations/${encodeURIComponent(requireStationId(stationId))}`;
}

export function createStationLogsPath(stationId: string, take = defaultStationDetailTake): string {
  return `stations/${encodeURIComponent(requireStationId(stationId))}/logs?take=${requireTake(take)}`;
}

export function createStationCommandsPath(stationId: string, take = defaultStationDetailTake): string {
  return `stations/${encodeURIComponent(requireStationId(stationId))}/commands?take=${requireTake(take)}`;
}

export function createStationCommandByClientRequestPath(
  stationId: string,
  commandType: StationCommand['commandType'],
  clientRequestId: string
): string {
  const requestId = clientRequestId.trim();
  if (!requestId) throw new TypeError('Station command client request id must be a non-empty string.');
  return `stations/${encodeURIComponent(requireStationId(stationId))}/commands/by-client-request/${encodeURIComponent(requestId)}` +
    `?commandType=${encodeURIComponent(commandType)}`;
}

export function createStationAuditPath(stationId: string, take = defaultStationDetailTake): string {
  return `stations/audit?stationId=${encodeURIComponent(requireStationId(stationId))}&take=${requireTake(take)}`;
}

export function createStationPackagesPath(): string {
  return 'station-packages';
}

export function createStationsDefinition(): ReadQueryDefinition<readonly StationStatus[]> {
  return Object.freeze({
    key: 'stations:list',
    path: createStationsPath(),
    decode: decodeStationList,
    isEmpty: (stations: readonly StationStatus[]) => stations.length === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationSummaryDefinition(): ReadQueryDefinition<StationSummary> {
  return Object.freeze({
    key: 'stations:summary',
    path: createStationSummaryPath(),
    decode: decodeStationSummary,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationStatisticsDefinition(
  filters: () => StationStatisticsFilters
): ReadQueryDefinition<StationStatistics> {
  return Object.freeze({
    key: () => `stations:statistics:${createStationStatisticsPath(filters())}`,
    path: () => createStationStatisticsPath(filters()),
    decode: decodeStationStatistics,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationResultsPageDefinition(
  filters: () => StationResultsFilters
): ReadQueryDefinition<StationResultsPage> {
  return Object.freeze({
    key: () => `stations:results-page:${createStationResultsPagePath(filters())}`,
    path: () => createStationResultsPagePath(filters()),
    decode: decodeStationResultsPage,
    isEmpty: (page: StationResultsPage) => page.items.length === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationResultsDefinition(
  stationId: () => string,
  take: () => number
): ReadQueryDefinition<readonly StationResult[]> {
  return Object.freeze({
    key: () => `stations:detail-results:${requireStationId(stationId())}:${requireTake(take())}`,
    path: () => createStationResultsPath(stationId(), take()),
    decode: decodeStationResults,
    isEmpty: (results: readonly StationResult[]) => results.length === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationHealthDefinition(
  stationId: () => string,
  take: () => number
): ReadQueryDefinition<readonly StationHealthSnapshot[]> {
  return Object.freeze({
    key: () => `stations:detail-health:${requireStationId(stationId())}:${requireTake(take())}`,
    path: () => createStationHealthPath(stationId(), take()),
    decode: decodeStationHealth,
    isEmpty: (health: readonly StationHealthSnapshot[]) => health.length === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationAdminDetailsDefinition(
  stationId: () => string
): ReadQueryDefinition<StationAdminDetails> {
  return Object.freeze({
    key: () => `stations:admin-detail:${requireStationId(stationId())}`,
    path: () => createStationAdminDetailsPath(stationId()),
    decode: decodeStationAdminDetails,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationLogsDefinition(
  stationId: () => string,
  take: () => number
): ReadQueryDefinition<readonly StationLog[]> {
  return Object.freeze({
    key: () => `stations:admin-logs:${requireStationId(stationId())}:${requireTake(take())}`,
    path: () => createStationLogsPath(stationId(), take()),
    decode: decodeStationLogs,
    isEmpty: (logs: readonly StationLog[]) => logs.length === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationCommandsDefinition(
  stationId: () => string,
  take: () => number
): ReadQueryDefinition<readonly StationCommand[]> {
  return Object.freeze({
    key: () => `stations:admin-commands:${requireStationId(stationId())}:${requireTake(take())}`,
    path: () => createStationCommandsPath(stationId(), take()),
    decode: decodeStationCommands,
    isEmpty: (commands: readonly StationCommand[]) => commands.length === 0,
    protected: true,
    cacheTimeMs: 2_000
  });
}

export function createStationAuditsDefinition(
  stationId: () => string,
  take: () => number
): ReadQueryDefinition<readonly StationAudit[]> {
  return Object.freeze({
    key: () => `stations:admin-audit:${requireStationId(stationId())}:${requireTake(take())}`,
    path: () => createStationAuditPath(stationId(), take()),
    decode: decodeStationAudits,
    isEmpty: (audits: readonly StationAudit[]) => audits.length === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationPackagesDefinition(): ReadQueryDefinition<readonly StationPackage[]> {
  return Object.freeze({
    key: 'stations:admin-packages',
    path: createStationPackagesPath(),
    decode: decodeStationPackages,
    isEmpty: (packages: readonly StationPackage[]) => packages.length === 0,
    protected: true,
    cacheTimeMs: 5_000
  });
}

export function createStationsQuery(client: ReadQueryClient): ReadQueryOwner<readonly StationStatus[]> {
  return client.createQuery(createStationsDefinition());
}

export function createStationSummaryQuery(client: ReadQueryClient): ReadQueryOwner<StationSummary> {
  return client.createQuery(createStationSummaryDefinition());
}

export function createStationStatisticsQuery(
  client: ReadQueryClient,
  filters: () => StationStatisticsFilters
): ReadQueryOwner<StationStatistics> {
  return client.createQuery(createStationStatisticsDefinition(filters));
}

export function createStationResultsPageQuery(
  client: ReadQueryClient,
  filters: () => StationResultsFilters
): ReadQueryOwner<StationResultsPage> {
  return client.createQuery(createStationResultsPageDefinition(filters));
}

export function createStationResultsQuery(
  client: ReadQueryClient,
  stationId: () => string,
  take: () => number
): ReadQueryOwner<readonly StationResult[]> {
  return client.createQuery(createStationResultsDefinition(stationId, take));
}

export function createStationHealthQuery(
  client: ReadQueryClient,
  stationId: () => string,
  take: () => number
): ReadQueryOwner<readonly StationHealthSnapshot[]> {
  return client.createQuery(createStationHealthDefinition(stationId, take));
}

export function createStationAdminDetailsQuery(
  client: ReadQueryClient,
  stationId: () => string
): ReadQueryOwner<StationAdminDetails> {
  return client.createQuery(createStationAdminDetailsDefinition(stationId));
}

export function createStationLogsQuery(
  client: ReadQueryClient,
  stationId: () => string,
  take: () => number
): ReadQueryOwner<readonly StationLog[]> {
  return client.createQuery(createStationLogsDefinition(stationId, take));
}

export function createStationCommandsQuery(
  client: ReadQueryClient,
  stationId: () => string,
  take: () => number
): ReadQueryOwner<readonly StationCommand[]> {
  return client.createQuery(createStationCommandsDefinition(stationId, take));
}

export function createStationAuditsQuery(
  client: ReadQueryClient,
  stationId: () => string,
  take: () => number
): ReadQueryOwner<readonly StationAudit[]> {
  return client.createQuery(createStationAuditsDefinition(stationId, take));
}

export function createStationPackagesQuery(client: ReadQueryClient): ReadQueryOwner<readonly StationPackage[]> {
  return client.createQuery(createStationPackagesDefinition());
}
