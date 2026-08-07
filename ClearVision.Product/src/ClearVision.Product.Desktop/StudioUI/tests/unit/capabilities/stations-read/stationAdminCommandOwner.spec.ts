import { describe, expect, it, vi } from 'vitest';
import {
  createStationAdminCommandOwner,
  getStationAdminCommandOwnerActiveCount,
  type StationIdentityUpdate
} from '@/capabilities/stations-read';
import {
  ApiConflictError,
  ApiForbiddenError,
  ApiNetworkError,
  ApiNotFoundError,
  type ApiTransport
} from '@/platform/api';
import { stationCommand, stationStatus } from './stationFixtures';

function details(status: number) {
  return { url: 'http://localhost/api/stations/station-a', status, statusText: 'test', payload: {}, responseBody: '{}' };
}

function identity(): StationIdentityUpdate {
  return {
    stationName: '一号检测站（修订）', lineName: '一号线', areaName: 'A 区', workcellName: '单元 1',
    inspectionNodeName: '瓶盖检测', cameraAlias: '顶视相机', stationRole: 'Inspection', owner: '生产一组',
    isEnabled: true, remark: '已修订'
  };
}

describe('stationAdminCommandOwner', () => {
  it('handles command, identity and package deployment success without duplicate submission', async () => {
    const post = vi.fn(async (path: string, body: unknown) => stationCommand({
      commandId: path.endsWith('deploy-package') ? 'deploy-a' : 'command-a',
      commandType: path.endsWith('deploy-package') ? 'DeployPackage' : 'Ping',
      payloadJson: JSON.stringify(body)
    }));
    const patch = vi.fn(async () => stationStatus({ stationName: '一号检测站（修订）', remark: '已修订' }));
    const api = { apiBaseUrl: 'http://localhost/api', get: vi.fn(), post, patch } as ApiTransport;
    const owner = createStationAdminCommandOwner({ api, stationId: () => 'station-a', createRequestId: () => 'request-a' });

    const first = owner.issueCommand('Ping');
    const duplicate = owner.issueCommand('Ping');
    expect(duplicate).toBe(first);
    await expect(first).resolves.toMatchObject({ commandType: 'Ping' });
    expect(post).toHaveBeenCalledTimes(1);

    owner.reset();
    await expect(owner.reviseIdentity(identity())).resolves.toMatchObject({ stationName: '一号检测站（修订）' });
    owner.reset();
    await expect(owner.deployPackage('pkg-a')).resolves.toMatchObject({ commandType: 'DeployPackage' });
    expect(owner.projection.phase).toBe('command-created');
    expect(post).toHaveBeenNthCalledWith(1, 'stations/station-a/commands', {
      commandType: 'Ping', payloadJson: '{}', expiresInSeconds: 300, clientRequestId: 'request-a'
    }, expect.any(Object));
    expect(post).toHaveBeenNthCalledWith(2, 'stations/station-a/deploy-package', {
      packageId: 'pkg-a', clientRequestId: 'request-a'
    }, expect.any(Object));
    owner.dispose();
  });

  it('classifies forbidden and conflict as terminal failures while preserving the backend as final authority', async () => {
    const failures = [new ApiForbiddenError(details(403)), new ApiConflictError(details(409))];
    const post = vi.fn(async () => { throw failures.shift(); });
    const api = { apiBaseUrl: 'http://localhost/api', get: vi.fn(), post, patch: vi.fn() } as ApiTransport;
    const owner = createStationAdminCommandOwner({ api, stationId: () => 'station-a', createRequestId: () => 'request-a' });

    await owner.issueCommand('Ping');
    expect(owner.projection).toMatchObject({ phase: 'failed', errorCode: 'STATION_ADMIN_FORBIDDEN' });
    owner.reset();
    await owner.issueCommand('Ping');
    expect(owner.projection).toMatchObject({ phase: 'conflict', errorCode: 'STATION_ADMIN_CONFLICT' });
    owner.dispose();
  });

  it('recovers command, identity and deployment after unknown network outcomes by rereading authority', async () => {
    let recovery: 'command' | 'identity' | 'deploy' = 'command';
    const post = vi.fn(async () => { throw new ApiNetworkError('http://localhost/api/stations', new Error('lost')); });
    const patch = vi.fn(async () => { throw new ApiNetworkError('http://localhost/api/stations', new Error('lost')); });
    const get = vi.fn(async () => {
      if (recovery === 'identity') return stationStatus({ stationName: '一号检测站（修订）', remark: '已修订' });
      return stationCommand({
        commandType: recovery === 'deploy' ? 'DeployPackage' : 'Ping',
        clientRequestId: 'request-a',
        createdAtUtc: new Date().toISOString(),
        payloadJson: JSON.stringify(recovery === 'deploy' ? { packageId: 'pkg-a' } : {})
      });
    });
    const api = { apiBaseUrl: 'http://localhost/api', get, post, patch } as ApiTransport;
    const owner = createStationAdminCommandOwner({ api, stationId: () => 'station-a', createRequestId: () => 'request-a' });

    await owner.issueCommand('Ping');
    expect(owner.projection.phase).toBe('unknown-outcome');
    await expect(owner.recover()).resolves.toBe(true);
    expect(owner.projection.phase).toBe('command-created');

    owner.reset(); recovery = 'identity';
    await owner.reviseIdentity(identity());
    expect(owner.projection.phase).toBe('unknown-outcome');
    await expect(owner.recover()).resolves.toBe(true);

    owner.reset(); recovery = 'deploy';
    await owner.deployPackage('pkg-a');
    expect(owner.projection.phase).toBe('unknown-outcome');
    await expect(owner.recover()).resolves.toBe(true);
    expect(get).toHaveBeenCalledTimes(3);
    expect(get).toHaveBeenNthCalledWith(
      1,
      'stations/station-a/commands/by-client-request/request-a?commandType=Ping',
      expect.any(Object)
    );
    expect(get).toHaveBeenNthCalledWith(3,
      'stations/station-a/commands/by-client-request/request-a?commandType=DeployPackage',
      expect.any(Object)
    );
    owner.dispose();
  });

  it('exposes reconciliation as a distinct phase while an unknown deployment is being checked', async () => {
    let resolveLookup: ((value: unknown) => void) | undefined;
    const post = vi.fn(async () => {
      throw new ApiNetworkError('http://localhost/api/stations/station-a/deploy-package', new Error('lost'));
    });
    const get = vi.fn(async () => await new Promise<unknown>(resolve => {
      resolveLookup = resolve;
    }));
    const api = { apiBaseUrl: 'http://localhost/api', get, post, patch: vi.fn() } as ApiTransport;
    const owner = createStationAdminCommandOwner({
      api,
      stationId: () => 'station-a',
      createRequestId: () => 'request-a'
    });

    await owner.deployPackage('pkg-a');
    expect(owner.projection.phase).toBe('unknown-outcome');

    const recovery = owner.recover();
    await Promise.resolve();
    expect(owner.projection.phase).toBe('reconciling');
    owner.reset();
    expect(owner.projection.phase).toBe('reconciling');

    resolveLookup?.(stationCommand({ commandType: 'DeployPackage', clientRequestId: 'request-a' }));
    await expect(recovery).resolves.toBe(true);
    expect(owner.projection.phase).toBe('command-created');
    owner.dispose();
  });

  it('treats an undecodable successful response as unknown instead of safe-to-retry failure', async () => {
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(),
      post: vi.fn(async () => ({ accepted: true })),
      patch: vi.fn()
    } as ApiTransport;
    const owner = createStationAdminCommandOwner({ api, stationId: () => 'station-a', createRequestId: () => 'request-a' });

    await owner.issueCommand('Ping');

    expect(owner.projection).toMatchObject({
      phase: 'unknown-outcome',
      errorCode: 'STATION_ADMIN_CONTRACT_UNKNOWN',
      canRecover: true
    });
    owner.dispose();
  });

  it('allows a retry only after the exact request lookup authoritatively returns 404', async () => {
    const requestIds = ['request-a', 'request-b'];
    let submitAttempt = 0;
    const post = vi.fn(async () => {
      submitAttempt += 1;
      if (submitAttempt === 1) {
        throw new ApiNetworkError('http://localhost/api/stations/station-a/deploy-package', new Error('lost'));
      }
      return stationCommand({ commandType: 'DeployPackage', clientRequestId: 'request-b' });
    });
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async () => { throw new ApiNotFoundError(details(404)); }),
      post,
      patch: vi.fn()
    } as ApiTransport;
    const owner = createStationAdminCommandOwner({
      api,
      stationId: () => 'station-a',
      createRequestId: () => requestIds.shift() ?? 'request-c'
    });

    await owner.deployPackage('pkg-a');
    await expect(owner.recover()).resolves.toBe(false);

    expect(owner.projection).toMatchObject({
      phase: 'failed',
      canRecover: false,
      errorCode: 'STATION_COMMAND_NOT_CREATED',
      command: null
    });
    await expect(owner.deployPackage('pkg-a')).resolves.toMatchObject({ clientRequestId: 'request-b' });
    expect(post).toHaveBeenCalledTimes(2);
    expect(owner.projection.phase).toBe('command-created');
    owner.dispose();
  });

  it('releases all command resources across 20 mount/dispose cycles', () => {
    const api = { apiBaseUrl: 'http://localhost/api', get: vi.fn(), post: vi.fn(), patch: vi.fn() } as ApiTransport;
    for (let index = 0; index < 20; index += 1) {
      const owner = createStationAdminCommandOwner({ api, stationId: () => 'station-a', createRequestId: () => `request-${index}` });
      expect(owner.diagnostics).toMatchObject({ ownerCount: 1, activeAbortControllerCount: 0, inFlightCommandCount: 0 });
      owner.dispose();
      expect(owner.diagnostics).toMatchObject({ ownerCount: 0, activeAbortControllerCount: 0, disposed: true });
    }
    expect(getStationAdminCommandOwnerActiveCount()).toBe(0);
  });
});
