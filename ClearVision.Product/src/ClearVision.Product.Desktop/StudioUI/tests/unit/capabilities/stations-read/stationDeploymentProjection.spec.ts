import { describe, expect, it } from 'vitest';
import {
  decodeStationAdminDetails,
  decodeStationCommands,
  decodeStationPackages,
  mergeStationCommandProjection,
  projectStationDeployment,
  type StationCommandStatus,
  type StationDeploymentResolution
} from '@/capabilities/stations-read';
import { stationCommand, stationPackage, stationStatus } from './stationFixtures';

function project(overrides: {
  readonly commandStatus?: StationCommandStatus;
  readonly command?: Record<string, unknown>;
  readonly station?: Record<string, unknown> | null;
  readonly packages?: readonly Record<string, unknown>[];
  readonly resolution?: StationDeploymentResolution;
  readonly pendingPackageId?: string | null;
} = {}) {
  const expectedPackage = stationPackage();
  const commands = decodeStationCommands([stationCommand({
    commandType: 'DeployPackage',
    payloadJson: JSON.stringify({
      packageId: expectedPackage.packageId,
      packageVersion: expectedPackage.packageVersion,
      sha256: expectedPackage.sha256,
      sourceProjectId: expectedPackage.sourceProjectId,
      sourceProjectRevision: expectedPackage.sourceProjectRevision,
      flowHash: expectedPackage.flowHash,
      decisionConfigurationHash: expectedPackage.decisionConfigurationHash
    }),
    status: overrides.commandStatus ?? 'Succeeded',
    ...overrides.command
  })]);
  const packages = decodeStationPackages(overrides.packages ?? [expectedPackage]);
  const station = overrides.station === null
    ? null
    : decodeStationAdminDetails(stationStatus(overrides.station));
  return projectStationDeployment({
    commands,
    packages,
    station,
    resolution: overrides.resolution ?? 'idle',
    pendingPackageId: overrides.pendingPackageId ?? 'pkg-a'
  });
}

describe('station deployment projection', () => {
  it.each([
    ['submitting', 'submitting', 'info'],
    ['unknown', 'unknown', 'warning'],
    ['reconciling', 'reconciling', 'warning']
  ] as const)('keeps a transient %s deployment state separate from historical commands', (resolution, phase, tone) => {
    expect(project({ resolution })).toMatchObject({
      phase,
      tone,
      command: null,
      expectedPackage: { packageId: 'pkg-a' }
    });
  });

  it('keeps the temporary command only until the authoritative query returns the same command id', () => {
    const pending = decodeStationCommands([stationCommand({ commandId: 'deploy-a', status: 'Created' })])[0]!;
    const previous = decodeStationCommands([stationCommand({ commandId: 'deploy-old', status: 'Succeeded' })]);
    const terminal = decodeStationCommands([stationCommand({ commandId: 'deploy-a', status: 'Succeeded' })]);

    expect(mergeStationCommandProjection(previous, pending).map(item => item.commandId)).toEqual([
      'deploy-a', 'deploy-old'
    ]);
    expect(mergeStationCommandProjection(terminal, pending)[0]?.status).toBe('Succeeded');
    expect(mergeStationCommandProjection(previous, null)).toBe(previous);
  });

  it.each([
    ['Created', 'command-created'],
    ['Delivered', 'in-progress'],
    ['Accepted', 'in-progress'],
    ['Running', 'in-progress']
  ] as const)('keeps %s distinct from deployment completion', (status, phase) => {
    expect(project({ commandStatus: status })).toMatchObject({ phase, tone: 'info' });
  });

  it.each([
    ['Rejected', 'error'],
    ['Failed', 'error'],
    ['TimedOut', 'error'],
    ['Cancelled', 'warning']
  ] as const)('projects terminal %s without claiming the package is active', (status, tone) => {
    expect(project({ commandStatus: status })).toMatchObject({ phase: 'terminal-failed', tone });
  });

  it('waits for an online active identity after command success', () => {
    expect(project({ station: { isOnline: false, onlineState: 'Offline' } })).toMatchObject({
      phase: 'awaiting-active-identity', tone: 'warning'
    });
    expect(project({ station: null })).toMatchObject({ phase: 'awaiting-active-identity' });
    expect(project({ command: { payloadJson: JSON.stringify({ packageId: 'pkg-a' }) } })).toMatchObject({
      phase: 'awaiting-active-identity',
      mismatches: ['部署命令身份']
    });
  });

  it('shows exact identity mismatches, including a last-known-good rollback outcome', () => {
    const projection = project({ station: {
      packageId: 'pkg-last-known-good',
      packageVersion: '0.9.0',
      packageSha256: `sha256:${'b'.repeat(64)}`,
      sourceProjectId: 'project-old',
      sourceProjectRevision: 11,
      packageFlowHash: 'sha256:old-flow',
      decisionConfigurationHash: 'sha256:old-decision'
    } });

    expect(projection).toMatchObject({ phase: 'identity-mismatch', tone: 'warning' });
    expect(projection.mismatches).toEqual([
      '运行包 ID', '运行包版本', 'SHA-256', '来源工程', '来源修订', '流程哈希', '判定配置哈希'
    ]);
  });

  it('reports success only for a terminal Succeeded command and the exact active identity', () => {
    expect(project()).toMatchObject({
      phase: 'succeeded',
      tone: 'ok',
      mismatches: []
    });
  });

  it('uses the immutable command identity when the package registry later changes', () => {
    expect(project({
      packages: [stationPackage({
        packageVersion: '9.9.9',
        sha256: `sha256:${'f'.repeat(64)}`,
        sourceProjectRevision: 999
      })]
    })).toMatchObject({
      phase: 'succeeded',
      tone: 'ok',
      expectedPackage: {
        packageVersion: '9.9.9'
      },
      expectedIdentity: {
        packageVersion: '1.0.0',
        sourceProjectRevision: 12
      }
    });
  });
});
