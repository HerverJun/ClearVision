import { describe, expect, it, vi } from 'vitest';
import {
  ApiForbiddenError,
  ApiNotFoundError,
  ApiServerError,
  ApiUnauthorizedError,
  type ApiGetOptions,
  type ApiTransport
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import {
  createStationAdminDetailsPath,
  createStationAdminDetailsQuery,
  createStationAuditPath,
  createStationCommandsPath,
  createStationCommandByClientRequestPath,
  createStationHealthPath,
  createStationLogsPath,
  createStationPackagesPath,
  createStationResultsPagePath,
  createStationResultsPath,
  createStationStatisticsPath,
  createStationsQuery
} from '@/capabilities/stations-read';
import { stationStatus } from './stationFixtures';

type GetImplementation = (path: string, options?: ApiGetOptions) => Promise<unknown>;

function apiWith(implementation: GetImplementation): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await implementation(path, options) as T | undefined;
    }
  };
}

function details(status: number) {
  return {
    url: 'http://localhost:5000/api/stations',
    status,
    statusText: 'test',
    payload: undefined,
    responseBody: ''
  };
}

describe('Station query definitions', () => {
  it('builds only frozen transport-relative GET paths', () => {
    expect(createStationStatisticsPath({
      range: 'week',
      stationId: 'station A/B',
      status: 'Undetermined',
      diagnosticCode: ' WIRE SWAP '
    })).toBe('stations/statistics?range=week&stationId=station+A%2FB&status=Undetermined&diagnosticCode=WIRE+SWAP');
    expect(createStationResultsPagePath({ pageIndex: 2, pageSize: 100 })).toBe(
      'stations/results?pageIndex=2&pageSize=100'
    );
    expect(createStationResultsPath('station A/B', 25)).toBe('stations/station%20A%2FB/results?take=25');
    expect(createStationHealthPath('station-a', 50)).toBe('stations/station-a/health?take=50');
    expect(createStationAdminDetailsPath('station-a')).toBe('stations/station-a');
    expect(createStationLogsPath('station-a', 25)).toBe('stations/station-a/logs?take=25');
    expect(createStationCommandsPath('station-a', 50)).toBe('stations/station-a/commands?take=50');
    expect(createStationCommandByClientRequestPath('station A/B', 'DeployPackage', 'request A/B')).toBe(
      'stations/station%20A%2FB/commands/by-client-request/request%20A%2FB?commandType=DeployPackage'
    );
    expect(createStationAuditPath('station A/B', 100)).toBe('stations/audit?stationId=station%20A%2FB&take=100');
    expect(createStationPackagesPath()).toBe('station-packages');
    expect(() => createStationResultsPath('', 50)).toThrow(TypeError);
    expect(() => createStationHealthPath('station-a', 501)).toThrow(RangeError);
    expect(() => createStationResultsPagePath({ pageIndex: -1 })).toThrow(RangeError);
    expect(() => createStationCommandByClientRequestPath('station-a', 'Ping', ' ')).toThrow(TypeError);
  });

  it.each([
    [new ApiUnauthorizedError(details(401)), 'unauthorized'],
    [new ApiForbiddenError(details(403)), 'forbidden'],
    [new ApiNotFoundError(details(404)), 'not-found'],
    [new ApiServerError(details(503)), 'error']
  ] as const)('maps %s to the shared %s phase', async (failure, phase) => {
    const client = createReadQueryClient(apiWith(async () => { throw failure; }));
    const owner = createStationsQuery(client);

    await expect(owner.refresh({ force: true })).resolves.toMatchObject({ phase });
    owner.dispose();
    client.dispose();
  });

  it('maps an empty Station list to empty and rejects malformed DTOs', async () => {
    const emptyClient = createReadQueryClient(apiWith(async () => []));
    const empty = createStationsQuery(emptyClient);
    await expect(empty.refresh()).resolves.toMatchObject({ phase: 'empty', data: [] });
    empty.dispose();
    emptyClient.dispose();

    const malformedClient = createReadQueryClient(apiWith(async () => ({ items: [] })));
    const malformed = createStationsQuery(malformedClient);
    await expect(malformed.refresh()).resolves.toMatchObject({
      phase: 'error',
      failure: { kind: 'decode' }
    });
    malformed.dispose();
    malformedClient.dispose();
  });

  it('keeps previous Station data stale after a 5xx refresh failure', async () => {
    let attempt = 0;
    const client = createReadQueryClient(apiWith(async () => {
      attempt += 1;
      if (attempt === 1) return [stationStatus()];
      throw new ApiServerError(details(503));
    }));
    const owner = createStationsQuery(client);

    await owner.refresh({ force: true });
    const stale = await owner.refresh({ force: true });

    expect(stale).toMatchObject({
      phase: 'stale',
      data: [{ stationId: 'station-a' }]
    });
    owner.dispose();
    client.dispose();
  });

  it('keeps an Admin 403 isolated from the ordinary Station list', async () => {
    const get = vi.fn(async (path: string) => {
      if (path === 'stations') return [stationStatus()];
      throw new ApiForbiddenError(details(403));
    });
    const client = createReadQueryClient(apiWith(get));
    const list = createStationsQuery(client);
    const admin = createStationAdminDetailsQuery(client, () => 'station-a');

    const [listState, adminState] = await Promise.all([
      list.refresh({ force: true }),
      admin.refresh({ force: true })
    ]);

    expect(listState).toMatchObject({ phase: 'success', data: [{ stationId: 'station-a' }] });
    expect(adminState).toMatchObject({ phase: 'forbidden' });
    expect(get).toHaveBeenCalledWith('stations/station-a', expect.any(Object));
    list.dispose();
    admin.dispose();
    client.dispose();
  });
});
