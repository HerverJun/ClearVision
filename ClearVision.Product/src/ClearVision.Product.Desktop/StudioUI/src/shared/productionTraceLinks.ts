const resultsQueryKeys = new Set([
  'source',
  'projectId',
  'stationId',
  'resultId',
  'outcome',
  'diagnosticCode',
  'from',
  'to',
  'page',
  'pageSize',
  'returnTo'
]);

const stationListQueryKeys = new Set([
  'q',
  'online',
  'runtime',
  'range',
  'outcome',
  'diagnosticCode',
  'packageId',
  'projectId',
  'revision'
]);

function hasControlCharacter(value: string): boolean {
  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index);
    if (code <= 0x1f || code === 0x7f) return true;
  }
  return false;
}

function normalizedText(value: string | null | undefined): string {
  const normalized = value?.trim() ?? '';
  return hasControlCharacter(normalized) ? '' : normalized;
}

function append(query: URLSearchParams, key: string, value: string | number | null | undefined): void {
  const normalized = normalizedText(value == null ? '' : String(value));
  if (normalized) query.set(key, normalized);
}

function route(path: string, query: URLSearchParams): string {
  const suffix = query.toString();
  return suffix ? `${path}?${suffix}` : path;
}

function safeReturnTo(value: string | null | undefined): string {
  return resolveProductionReturnTo(value) ?? '';
}

export interface LocalResultsDeepLinkInput {
  readonly projectId: string;
  readonly resultId?: string | null;
  readonly returnTo?: string | null;
}

export function createLocalResultsDeepLink(input: LocalResultsDeepLinkInput): string {
  const query = new URLSearchParams({ source: 'local' });
  append(query, 'projectId', input.projectId);
  append(query, 'resultId', input.resultId);
  append(query, 'returnTo', safeReturnTo(input.returnTo));
  return route('/results', query);
}

export interface StationResultsDeepLinkInput {
  readonly stationId?: string | null;
  readonly resultId?: string | null;
  readonly outcome?: string | null;
  readonly diagnosticCode?: string | null;
  readonly from?: string | null;
  readonly to?: string | null;
  readonly page?: number | null;
  readonly pageSize?: number | null;
  readonly returnTo?: string | null;
}

export function createStationResultsDeepLink(input: StationResultsDeepLinkInput): string {
  const query = new URLSearchParams({ source: 'station' });
  append(query, 'stationId', input.stationId);
  append(query, 'resultId', input.resultId);
  append(query, 'outcome', input.outcome);
  append(query, 'diagnosticCode', input.diagnosticCode);
  append(query, 'from', input.from);
  append(query, 'to', input.to);
  if (input.page != null && input.page > 1) append(query, 'page', input.page);
  if (input.pageSize != null && input.pageSize !== 20) append(query, 'pageSize', input.pageSize);
  append(query, 'returnTo', safeReturnTo(input.returnTo));
  return route('/results', query);
}

export interface StationFleetDeepLinkInput {
  readonly packageId?: string | null;
  readonly projectId?: string | null;
  readonly revision?: number | null;
  readonly q?: string | null;
  readonly online?: string | null;
  readonly runtime?: string | null;
  readonly range?: string | null;
  readonly outcome?: string | null;
  readonly diagnosticCode?: string | null;
}

export function createStationFleetDeepLink(input: StationFleetDeepLinkInput = {}): string {
  const query = new URLSearchParams();
  append(query, 'packageId', input.packageId);
  append(query, 'projectId', input.projectId);
  if (input.revision != null && input.revision >= 0) append(query, 'revision', input.revision);
  append(query, 'q', input.q);
  append(query, 'online', input.online);
  append(query, 'runtime', input.runtime);
  append(query, 'range', input.range);
  append(query, 'outcome', input.outcome);
  append(query, 'diagnosticCode', input.diagnosticCode);
  return route('/stations', query);
}

export function createStationDetailDeepLink(
  stationId: string,
  returnTo?: string | null
): string {
  const normalizedStationId = normalizedText(stationId);
  if (!normalizedStationId) throw new TypeError('Station detail link requires a stable station id.');
  const query = new URLSearchParams();
  append(query, 'returnTo', safeReturnTo(returnTo));
  return route(`/stations/${encodeURIComponent(normalizedStationId)}`, query);
}

function hasOnlyKeys(query: URLSearchParams, allowed: ReadonlySet<string>): boolean {
  return [...query.keys()].every(key => allowed.has(key));
}

function parseInternalRoute(value: string | null | undefined): URL | null {
  const normalized = normalizedText(value);
  if (!normalized || !normalized.startsWith('/') || normalized.startsWith('//') || normalized.includes('\\')) {
    return null;
  }
  try {
    const parsed = new URL(normalized, 'https://clearvision.invalid');
    return parsed.origin === 'https://clearvision.invalid' && !parsed.hash ? parsed : null;
  } catch {
    return null;
  }
}

export function resolveProductionReturnTo(value: string | null | undefined): string | null {
  const parsed = parseInternalRoute(value);
  if (!parsed) return null;
  if (parsed.pathname === '/results') {
    if (!hasOnlyKeys(parsed.searchParams, resultsQueryKeys) || parsed.searchParams.has('returnTo')) return null;
    const source = parsed.searchParams.get('source');
    if (source !== 'local' && source !== 'station') return null;
    return route(parsed.pathname, parsed.searchParams);
  }
  if (parsed.pathname === '/stations') {
    return hasOnlyKeys(parsed.searchParams, stationListQueryKeys)
      ? route(parsed.pathname, parsed.searchParams)
      : null;
  }
  if (/^\/stations\/[^/]+$/.test(parsed.pathname)) {
    if (!parsed.search) return parsed.pathname;
    if ([...parsed.searchParams.keys()].some(key => key !== 'returnTo')) return null;
    const fleet = parseInternalRoute(parsed.searchParams.get('returnTo'));
    if (!fleet || fleet.pathname !== '/stations' || !hasOnlyKeys(fleet.searchParams, stationListQueryKeys)) {
      return null;
    }
    const query = new URLSearchParams({ returnTo: route(fleet.pathname, fleet.searchParams) });
    return route(parsed.pathname, query);
  }
  if (/^\/projects\/[^/]+\/(?:workspace|inspection)$/.test(parsed.pathname) && !parsed.search) {
    return parsed.pathname;
  }
  return null;
}
