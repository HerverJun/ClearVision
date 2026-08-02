import type {
  StationAdminDetails,
  StationCommand,
  StationPackage
} from './stationContracts';

export type StationDeploymentPhase =
  | 'none'
  | 'command-created'
  | 'in-progress'
  | 'terminal-failed'
  | 'awaiting-active-identity'
  | 'identity-mismatch'
  | 'succeeded';

export interface StationDeploymentProjection {
  readonly phase: StationDeploymentPhase;
  readonly label: string;
  readonly message: string;
  readonly tone: 'idle' | 'info' | 'warning' | 'error' | 'ok';
  readonly command: StationCommand | null;
  readonly expectedPackage: StationPackage | null;
  readonly expectedIdentity: StationDeploymentIdentity | null;
  readonly mismatches: readonly string[];
}

export interface StationDeploymentIdentity {
  readonly packageId: string;
  readonly packageVersion: string;
  readonly sha256: string;
  readonly sourceProjectId: string;
  readonly sourceProjectRevision: number;
  readonly flowHash: string;
  readonly decisionConfigurationHash: string;
}

export function mergeStationCommandProjection(
  authoritativeCommands: readonly StationCommand[],
  pendingCommand: StationCommand | null
): readonly StationCommand[] {
  if (!pendingCommand || authoritativeCommands.some(item => item.commandId === pendingCommand.commandId)) {
    return authoritativeCommands;
  }
  return Object.freeze([pendingCommand, ...authoritativeCommands]);
}

function normalize(value: string | null | undefined): string | null {
  const result = value?.trim().toLocaleLowerCase() ?? '';
  return result || null;
}

function normalizeSha256(value: string | null | undefined): string | null {
  return normalize(value)?.replace(/^sha256:/, '') ?? null;
}

function payloadDeploymentIdentity(command: StationCommand): StationDeploymentIdentity | null {
  try {
    const payload = JSON.parse(command.payloadJson) as unknown;
    if (typeof payload !== 'object' || payload === null || Array.isArray(payload)) return null;
    const record = payload as Record<string, unknown>;
    const strings = [
      record.packageId,
      record.packageVersion,
      record.sha256,
      record.sourceProjectId,
      record.flowHash,
      record.decisionConfigurationHash
    ];
    if (strings.some(value => typeof value !== 'string' || !value.trim())) return null;
    if (typeof record.sourceProjectRevision !== 'number' ||
        !Number.isSafeInteger(record.sourceProjectRevision) || record.sourceProjectRevision < 0) return null;
    return Object.freeze({
      packageId: (record.packageId as string).trim(),
      packageVersion: (record.packageVersion as string).trim(),
      sha256: (record.sha256 as string).trim(),
      sourceProjectId: (record.sourceProjectId as string).trim(),
      sourceProjectRevision: record.sourceProjectRevision,
      flowHash: (record.flowHash as string).trim(),
      decisionConfigurationHash: (record.decisionConfigurationHash as string).trim()
    });
  } catch {
    return null;
  }
}

function latestDeployment(commands: readonly StationCommand[]): StationCommand | null {
  return commands
    .filter(command => command.commandType === 'DeployPackage')
    .sort((left, right) => Date.parse(right.createdAtUtc) - Date.parse(left.createdAtUtc))[0] ?? null;
}

function mismatch(label: string, expected: unknown, actual: unknown): string | null {
  return expected === actual ? null : label;
}

export function projectStationDeployment(input: {
  readonly commands: readonly StationCommand[];
  readonly packages: readonly StationPackage[];
  readonly station: StationAdminDetails | null;
}): StationDeploymentProjection {
  const command = latestDeployment(input.commands);
  if (!command) {
    return Object.freeze({
      phase: 'none', label: '尚无部署命令', message: '选择正式运行包后创建部署命令。', tone: 'idle',
      command: null, expectedPackage: null, expectedIdentity: null, mismatches: Object.freeze([])
    });
  }

  const expectedIdentity = payloadDeploymentIdentity(command);
  const expectedPackage = input.packages.find(
    item => normalize(item.packageId) === normalize(expectedIdentity?.packageId)
  ) ?? null;
  if (command.status === 'Created') {
    return Object.freeze({
      phase: 'command-created', label: '命令已创建', message: '等待工作站接收命令。', tone: 'info',
      command, expectedPackage, expectedIdentity, mismatches: Object.freeze([])
    });
  }
  if (['Delivered', 'Accepted', 'Running'].includes(command.status)) {
    const label = command.status === 'Delivered'
      ? '命令已送达'
      : command.status === 'Accepted'
        ? '工作站已接受'
        : '正在激活运行包';
    return Object.freeze({
      phase: 'in-progress', label, message: '部署尚未完成，继续等待读取命令终态。', tone: 'info',
      command, expectedPackage, expectedIdentity, mismatches: Object.freeze([])
    });
  }
  if (command.status !== 'Succeeded') {
    return Object.freeze({
      phase: 'terminal-failed', label: `部署命令${command.status === 'TimedOut' ? '已过期' : '未成功'}`,
      message: command.resultMessage || command.errorCode || '工作站返回了非成功终态。',
      tone: command.status === 'Cancelled' ? 'warning' : 'error', command, expectedPackage, expectedIdentity,
      mismatches: Object.freeze([])
    });
  }

  const station = input.station;
  if (!station?.isOnline) {
    return Object.freeze({
      phase: 'awaiting-active-identity', label: '命令成功，工作站离线',
      message: '无法读取在线实际激活身份；不得据此声明部署完成。', tone: 'warning',
      command, expectedPackage, expectedIdentity, mismatches: Object.freeze(['工作站在线状态'])
    });
  }
  if (!expectedIdentity) {
    return Object.freeze({
      phase: 'awaiting-active-identity', label: '命令成功，目标身份缺失',
      message: '部署命令没有完整记录目标运行包身份，无法进行精确核对。', tone: 'warning',
      command, expectedPackage, expectedIdentity, mismatches: Object.freeze(['部署命令身份'])
    });
  }

  const mismatches = [
    mismatch('运行包 ID', normalize(expectedIdentity.packageId), normalize(station.packageId)),
    mismatch('运行包版本', normalize(expectedIdentity.packageVersion), normalize(station.packageVersion)),
    mismatch('SHA-256', normalizeSha256(expectedIdentity.sha256), normalizeSha256(station.packageSha256)),
    mismatch('来源工程', normalize(expectedIdentity.sourceProjectId), normalize(station.sourceProjectId)),
    mismatch('来源修订', expectedIdentity.sourceProjectRevision, station.sourceProjectRevision),
    mismatch('流程哈希', normalize(expectedIdentity.flowHash), normalize(station.packageFlowHash)),
    mismatch(
      '判定配置哈希',
      normalize(expectedIdentity.decisionConfigurationHash),
      normalize(station.decisionConfigurationHash)
    )
  ].filter((item): item is string => item !== null);
  if (mismatches.length > 0) {
    return Object.freeze({
      phase: 'identity-mismatch', label: '命令成功，激活身份不匹配',
      message: `以下身份未匹配：${mismatches.join('、')}。可能仍在上报，或工作站已回滚到上一稳定版本。`,
      tone: 'warning', command, expectedPackage, expectedIdentity, mismatches: Object.freeze(mismatches)
    });
  }

  return Object.freeze({
    phase: 'succeeded', label: '部署完成',
    message: '命令终态为成功，且工作站实际激活身份与目标运行包完全一致。', tone: 'ok',
    command, expectedPackage, expectedIdentity, mismatches: Object.freeze([])
  });
}
