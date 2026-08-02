import type {
  StationAudit,
  StationCommand,
  StationPackage,
  StationResult,
  StationStatus
} from './stationContracts';

export type StationAdminEvidenceState = 'available' | 'loading' | 'restricted' | 'unavailable';
export type StationProductionTracePhase = 'complete' | 'partial' | 'mismatch';

export interface StationProductionTraceProjection {
  readonly phase: StationProductionTracePhase;
  readonly projectId: string | null;
  readonly projectRevision: number | null;
  readonly activePackage: StationPackage | null;
  readonly deploymentCommand: StationCommand | null;
  readonly deploymentAudit: StationAudit | null;
  readonly latestResult: StationResult | null;
  readonly gaps: readonly string[];
  readonly mismatches: readonly string[];
}

function same(left: string | null | undefined, right: string | null | undefined): boolean {
  return Boolean(left && right && left.toLocaleLowerCase() === right.toLocaleLowerCase());
}

function sameHash(left: string | null | undefined, right: string | null | undefined): boolean {
  const normalize = (value: string | null | undefined) => value?.trim().replace(/^sha256:/i, '').toLocaleLowerCase();
  return Boolean(left && right && normalize(left) === normalize(right));
}

function newest<T>(items: readonly T[], date: (item: T) => string): T | null {
  return [...items].sort((left, right) => Date.parse(date(right)) - Date.parse(date(left)))[0] ?? null;
}

export function deploymentPackageId(command: StationCommand): string | null {
  if (command.commandType !== 'DeployPackage') return null;
  try {
    const payload: unknown = JSON.parse(command.payloadJson);
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) return null;
    const record = payload as Readonly<Record<string, unknown>>;
    const value = record.packageId ?? record.PackageId;
    return typeof value === 'string' && value.trim() ? value.trim() : null;
  } catch {
    return null;
  }
}

export function summarizeTraceIdentity(value: string | null | undefined): string {
  const normalized = value?.trim() ?? '';
  if (!normalized) return '身份未上报';
  if (normalized.length <= 24) return normalized;
  return `${normalized.slice(0, 12)}…${normalized.slice(-8)}`;
}

export function projectStationProductionTrace(input: {
  readonly station: StationStatus;
  readonly results: readonly StationResult[];
  readonly commands: readonly StationCommand[];
  readonly audits: readonly StationAudit[];
  readonly packages: readonly StationPackage[];
  readonly adminEvidence: StationAdminEvidenceState;
}): StationProductionTraceProjection {
  const stationId = input.station.stationId;
  const activePackage = input.station.packageId
    ? input.packages.find(item => same(item.packageId, input.station.packageId)) ?? null
    : null;
  const stationCommands = input.commands.filter(command => same(command.stationId, stationId));
  const deployCommands = stationCommands.filter(command => command.commandType === 'DeployPackage');
  const matchingDeployment = input.station.packageId
    ? newest(
      deployCommands.filter(command => same(deploymentPackageId(command), input.station.packageId)),
      command => command.createdAtUtc
    )
    : null;
  const deploymentCommand = matchingDeployment;
  const deploymentAudit = deploymentCommand
    ? newest(
      input.audits.filter(audit => same(audit.commandId, deploymentCommand.commandId)),
      audit => audit.createdAtUtc
    )
    : null;
  const stationResults = input.results.filter(result => same(result.stationId, stationId));
  const matchingResults = input.station.packageId
    ? stationResults.filter(result => same(result.packageId, input.station.packageId))
    : stationResults;
  const latestResult = newest(matchingResults.length ? matchingResults : stationResults, result => result.completedAtUtc);
  const latestResultExecutionFlowHash = latestResult?.executionFlowHash || latestResult?.flowHash || null;
  const projectId = activePackage?.sourceProjectId ?? input.station.sourceProjectId;
  const projectRevision = activePackage?.sourceProjectRevision ?? input.station.sourceProjectRevision;
  const gaps: string[] = [];
  const mismatches: string[] = [];

  if (!input.station.packageId) gaps.push('工作站未上报当前激活运行包身份。');
  if (!projectId || projectRevision === null) gaps.push('来源工程或正式保存修订不完整。');
  if (!input.station.packageSha256 || !input.station.packageFlowHash || !input.station.decisionConfigurationHash) {
    gaps.push('当前激活运行包的 SHA、流程或判定配置身份不完整。');
  }
  if (!input.station.executionSnapshotId || !input.station.currentRunId) {
    gaps.push('当前执行快照或运行身份不完整。');
  }
  if (input.adminEvidence === 'restricted') {
    gaps.push('当前角色仅能查看监控摘要，命令与审计详情未扩权。');
  } else if (input.adminEvidence === 'loading') {
    gaps.push('命令、审计与运行包权威仍在读取。');
  } else if (input.adminEvidence === 'unavailable') {
    gaps.push('命令、审计或运行包权威读取失败。');
  } else {
    if (input.station.packageId && !activePackage) gaps.push('当前激活运行包不在可用包窗口中，可能已归档。');
    if (!deploymentCommand) gaps.push('当前读取窗口没有可关联的部署命令。');
    if (deploymentCommand && !['Succeeded', 'Failed', 'TimedOut', 'Cancelled', 'Rejected'].includes(deploymentCommand.status)) {
      gaps.push('可关联的部署命令尚未进入终态。');
    } else if (deploymentCommand && deploymentCommand.status !== 'Succeeded') {
      gaps.push(`可关联的部署命令终态为 ${deploymentCommand.status}，不能确认当前激活运行包来源。`);
    }
    if (deploymentCommand && !deploymentAudit) gaps.push('当前读取窗口没有可关联的命令审计记录。');
  }
  if (!latestResult) {
    gaps.push('当前读取窗口没有可关联的工作站结果。');
  } else if (!latestResult.packageFlowHash || !latestResultExecutionFlowHash ||
      latestResult.projectRevision === null || !latestResult.decisionConfigurationHash ||
      !latestResult.executionSnapshotId) {
    gaps.push('最近结果的运行包流程、执行流程、工程修订、判定配置或执行快照身份不完整。');
  }

  if (activePackage?.sourceProjectId && input.station.sourceProjectId &&
      !same(activePackage.sourceProjectId, input.station.sourceProjectId)) {
    mismatches.push('运行包目录与当前激活身份的来源工程不一致。');
  }
  if (activePackage?.sourceProjectRevision !== null && activePackage?.sourceProjectRevision !== undefined &&
      input.station.sourceProjectRevision !== null &&
      activePackage.sourceProjectRevision !== input.station.sourceProjectRevision) {
    mismatches.push('运行包目录与当前激活身份的工程修订不一致。');
  }
  if (activePackage?.flowHash && input.station.packageFlowHash &&
      !sameHash(activePackage.flowHash, input.station.packageFlowHash)) {
    mismatches.push('运行包目录与当前激活身份的流程哈希不一致。');
  }
  if (activePackage?.decisionConfigurationHash && input.station.decisionConfigurationHash &&
      !sameHash(activePackage.decisionConfigurationHash, input.station.decisionConfigurationHash)) {
    mismatches.push('运行包目录与当前激活身份的判定配置哈希不一致。');
  }
  if (activePackage?.sha256 && input.station.packageSha256 &&
      !sameHash(activePackage.sha256, input.station.packageSha256)) {
    mismatches.push('运行包目录与当前激活身份的 SHA-256 不一致。');
  }
  if (deploymentCommand?.status === 'Succeeded' && input.station.packageId &&
      !same(deploymentPackageId(deploymentCommand), input.station.packageId)) {
    mismatches.push('最近成功部署命令与当前激活运行包不一致。');
  }
  if (latestResult && input.station.packageId && !same(latestResult.packageId, input.station.packageId)) {
    mismatches.push('最近结果与当前激活运行包不一致。');
  }
  if (latestResult?.packageFlowHash && input.station.packageFlowHash &&
      !sameHash(latestResult.packageFlowHash, input.station.packageFlowHash)) {
    mismatches.push('最近结果与当前激活运行包的流程哈希不一致。');
  }
  if (latestResultExecutionFlowHash && input.station.executionFlowHash &&
      !sameHash(latestResultExecutionFlowHash, input.station.executionFlowHash)) {
    mismatches.push('最近结果与当前执行流程哈希不一致。');
  }
  if (latestResult?.projectRevision !== null && latestResult?.projectRevision !== undefined &&
      input.station.projectRevision !== null && latestResult.projectRevision !== input.station.projectRevision) {
    mismatches.push('最近结果与当前执行工程修订不一致。');
  }
  if (latestResult?.decisionConfigurationHash && input.station.decisionConfigurationHash &&
      !sameHash(latestResult.decisionConfigurationHash, input.station.decisionConfigurationHash)) {
    mismatches.push('最近结果与当前判定配置哈希不一致。');
  }
  if (latestResult?.executionSnapshotId && input.station.executionSnapshotId &&
      !same(latestResult.executionSnapshotId, input.station.executionSnapshotId)) {
    mismatches.push('最近结果与当前执行快照身份不一致。');
  }

  return Object.freeze({
    phase: mismatches.length ? 'mismatch' : gaps.length ? 'partial' : 'complete',
    projectId: projectId ?? null,
    projectRevision: projectRevision ?? null,
    activePackage,
    deploymentCommand,
    deploymentAudit,
    latestResult,
    gaps: Object.freeze(gaps),
    mismatches: Object.freeze(mismatches)
  });
}
