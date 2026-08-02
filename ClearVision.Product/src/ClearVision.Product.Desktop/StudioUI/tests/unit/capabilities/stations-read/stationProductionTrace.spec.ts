import { describe, expect, it } from 'vitest';
import {
  deploymentPackageId,
  projectStationProductionTrace,
  summarizeTraceIdentity
} from '@/capabilities/stations-read/stationProductionTrace';
import {
  stationAudit,
  stationCommand,
  stationPackage,
  stationResult,
  stationStatus
} from './stationFixtures';
import {
  decodeStationAudits,
  decodeStationCommands,
  decodeStationList,
  decodeStationPackages,
  decodeStationResults
} from '@/capabilities/stations-read/stationContracts';

describe('Station production trace projection', () => {
  it('closes project, package, command, audit, active Run and result identity without copying payloads', () => {
    const [station] = decodeStationList([stationStatus()]);
    const [command] = decodeStationCommands([stationCommand({
      commandType: 'DeployPackage',
      payloadJson: JSON.stringify({ packageId: 'pkg-a', downloadUrl: '/api/station-packages/pkg-a/download' }),
      status: 'Succeeded',
      completedAtUtc: '2026-07-15T02:00:10Z'
    })]);
    expect(station).toBeDefined();
    expect(command).toBeDefined();
    const projection = projectStationProductionTrace({
      station: station!,
      results: decodeStationResults([stationResult()]),
      commands: command ? [command] : [],
      audits: decodeStationAudits([stationAudit()]),
      packages: decodeStationPackages([stationPackage()]),
      adminEvidence: 'available'
    });

    expect(projection).toMatchObject({
      phase: 'complete',
      projectId: 'project-a',
      projectRevision: 12,
      deploymentCommand: { commandId: 'command-a', issuedBy: 'admin', status: 'Succeeded' },
      deploymentAudit: { auditId: 'audit-a' },
      latestResult: { messageId: 'message-9', runId: 'run-9' }
    });
    expect(deploymentPackageId(command!)).toBe('pkg-a');
  });

  it('marks restricted and legacy gaps explicitly instead of guessing associations', () => {
    const [station] = decodeStationList([stationStatus({
      packageId: null,
      sourceProjectId: null,
      sourceProjectRevision: null,
      executionSnapshotId: null,
      currentRunId: null
    })]);
    const projection = projectStationProductionTrace({
      station: station!, results: [], commands: [], audits: [], packages: [], adminEvidence: 'restricted'
    });

    expect(projection.phase).toBe('partial');
    expect(projection.gaps.join(' ')).toContain('未上报');
    expect(projection.gaps.join(' ')).toContain('当前角色');
    expect(projection.projectId).toBeNull();
  });

  it('never associates an unrelated or unsuccessful deployment command with the active package', () => {
    const [station] = decodeStationList([stationStatus()]);
    const results = decodeStationResults([stationResult()]);
    const packages = decodeStationPackages([stationPackage()]);
    const [unrelated] = decodeStationCommands([stationCommand({
      commandType: 'DeployPackage',
      payloadJson: JSON.stringify({ packageId: 'pkg-b' }),
      status: 'Failed'
    })]);
    const unrelatedProjection = projectStationProductionTrace({
      station: station!,
      results,
      commands: unrelated ? [unrelated] : [],
      audits: decodeStationAudits([stationAudit()]),
      packages,
      adminEvidence: 'available'
    });

    expect(unrelatedProjection.deploymentCommand).toBeNull();
    expect(unrelatedProjection.phase).toBe('partial');
    expect(unrelatedProjection.gaps).toContain('当前读取窗口没有可关联的部署命令。');

    const [failed] = decodeStationCommands([stationCommand({
      commandType: 'DeployPackage',
      payloadJson: JSON.stringify({ packageId: 'pkg-a' }),
      status: 'Failed'
    })]);
    const failedProjection = projectStationProductionTrace({
      station: station!,
      results,
      commands: failed ? [failed] : [],
      audits: decodeStationAudits([stationAudit()]),
      packages,
      adminEvidence: 'available'
    });

    expect(failedProjection.deploymentCommand?.status).toBe('Failed');
    expect(failedProjection.phase).toBe('partial');
    expect(failedProjection.gaps.join(' ')).toContain('不能确认当前激活运行包来源');
  });

  it('detects active package identity mismatches and presents comparable hash summaries', () => {
    const [station] = decodeStationList([stationStatus({ packageFlowHash: 'sha256:different' })]);
    const projection = projectStationProductionTrace({
      station: station!,
      results: decodeStationResults([stationResult({ executionFlowHash: 'sha256:other-execution' })]),
      commands: [],
      audits: [],
      packages: decodeStationPackages([stationPackage()]),
      adminEvidence: 'available'
    });

    expect(projection.phase).toBe('mismatch');
    expect(projection.mismatches).toContain('运行包目录与当前激活身份的流程哈希不一致。');
    expect(projection.mismatches).toContain('最近结果与当前执行流程哈希不一致。');
    expect(summarizeTraceIdentity('a'.repeat(64))).toBe(`${'a'.repeat(12)}…${'a'.repeat(8)}`);
    expect(summarizeTraceIdentity(null)).toBe('身份未上报');
  });
});
