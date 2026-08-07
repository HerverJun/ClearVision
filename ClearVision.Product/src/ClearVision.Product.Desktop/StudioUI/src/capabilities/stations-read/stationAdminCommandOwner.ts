import { reactive, readonly, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiConflictError,
  ApiDecodeError,
  ApiForbiddenError,
  ApiNetworkError,
  ApiNotFoundError,
  ApiServerError,
  ApiUnauthorizedError,
  ApiUnexpectedHttpError,
  type ApiTransport
} from '@/platform/api';
import {
  decodeStationAdminDetails,
  decodeStationCommand,
  StationContractDecodeError,
  type StationAdminDetails,
  type StationCommand,
  type StationCommandType
} from './stationContracts';
import {
  createStationAdminDetailsPath,
  createStationCommandByClientRequestPath
} from './stationQueries';

export type StationAdminOperation = 'command' | 'identity' | 'deploy-package' | null;
export type StationAdminCommandPhase =
  | 'idle'
  | 'pending'
  | 'reconciling'
  | 'command-created'
  | 'succeeded'
  | 'failed'
  | 'conflict'
  | 'unknown-outcome'
  | 'disposed';

export interface StationIdentityUpdate {
  readonly stationName: string;
  readonly lineName: string | null;
  readonly areaName: string | null;
  readonly workcellName: string | null;
  readonly inspectionNodeName: string | null;
  readonly cameraAlias: string | null;
  readonly stationRole: string;
  readonly owner: string | null;
  readonly isEnabled: boolean;
  readonly remark: string | null;
}

export interface StationAdminCommandProjection {
  readonly phase: StationAdminCommandPhase;
  readonly operation: StationAdminOperation;
  readonly message: string;
  readonly errorCode: string | null;
  readonly canRecover: boolean;
  readonly command: StationCommand | null;
  readonly identity: StationAdminDetails | null;
}

type MutableProjection = { -readonly [Key in keyof StationAdminCommandProjection]: StationAdminCommandProjection[Key] };

export interface StationAdminCommandDiagnostics {
  readonly ownerCount: number;
  readonly activeAbortControllerCount: number;
  readonly inFlightCommandCount: number;
  readonly disposed: boolean;
}

export interface StationAdminCommandOwner {
  readonly projection: DeepReadonly<StationAdminCommandProjection>;
  readonly diagnostics: StationAdminCommandDiagnostics;
  issueCommand(commandType: StationCommandType): Promise<StationCommand | null>;
  reviseIdentity(input: StationIdentityUpdate): Promise<StationAdminDetails | null>;
  deployPackage(packageId: string): Promise<StationCommand | null>;
  recover(): Promise<boolean>;
  reset(): void;
  dispose(): void;
}

interface PendingCommand {
  readonly operation: Exclude<StationAdminOperation, null>;
  readonly requestId: string;
  readonly commandType?: StationCommandType;
  readonly packageId?: string;
  readonly identity?: StationIdentityUpdate;
}

let activeStationAdminCommandOwnerCount = 0;

export function getStationAdminCommandOwnerActiveCount(): number {
  return activeStationAdminCommandOwnerCount;
}

function textOrNull(value: string | null): string | null {
  const normalized = value?.trim() ?? '';
  return normalized || null;
}

function normalizeIdentity(input: StationIdentityUpdate): StationIdentityUpdate {
  const stationName = input.stationName.trim();
  const stationRole = input.stationRole.trim();
  if (!stationName) throw new TypeError('工作站名称不能为空。');
  if (!stationRole) throw new TypeError('工作站角色不能为空。');
  return Object.freeze({
    stationName,
    lineName: textOrNull(input.lineName),
    areaName: textOrNull(input.areaName),
    workcellName: textOrNull(input.workcellName),
    inspectionNodeName: textOrNull(input.inspectionNodeName),
    cameraAlias: textOrNull(input.cameraAlias),
    stationRole,
    owner: textOrNull(input.owner),
    isEnabled: input.isEnabled,
    remark: textOrNull(input.remark)
  });
}

function identityMatches(actual: StationAdminDetails, expected: StationIdentityUpdate): boolean {
  return actual.stationName === expected.stationName && actual.lineName === expected.lineName &&
    actual.areaName === expected.areaName && actual.workcellName === expected.workcellName &&
    actual.inspectionNodeName === expected.inspectionNodeName && actual.cameraAlias === expected.cameraAlias &&
    actual.stationRole === expected.stationRole && actual.owner === expected.owner &&
    actual.isEnabled === expected.isEnabled && actual.remark === expected.remark;
}

function errorCode(error: unknown): string {
  if (error instanceof ApiForbiddenError) return 'STATION_ADMIN_FORBIDDEN';
  if (error instanceof ApiUnauthorizedError) return 'SESSION_UNAUTHORIZED';
  if (error instanceof ApiConflictError) return 'STATION_ADMIN_CONFLICT';
  if (error instanceof ApiAbortError) return 'STATION_ADMIN_ABORTED_UNKNOWN';
  if (error instanceof ApiNetworkError || error instanceof ApiServerError || error instanceof ApiUnexpectedHttpError) {
    return 'STATION_ADMIN_UNKNOWN_OUTCOME';
  }
  if (error instanceof ApiDecodeError || error instanceof StationContractDecodeError || error instanceof TypeError) {
    return 'STATION_ADMIN_CONTRACT_UNKNOWN';
  }
  return 'STATION_ADMIN_FAILED';
}

function messageFor(code: string): string {
  switch (code) {
    case 'STATION_ADMIN_FORBIDDEN': return '后端拒绝了工作站管理员操作；当前账号没有 StationAdmin 权限。';
    case 'SESSION_UNAUTHORIZED': return '会话已失效，未确认工作站操作结果。';
    case 'STATION_ADMIN_CONFLICT': return '工作站状态已发生冲突；请刷新后重新确认。';
    case 'STATION_ADMIN_ABORTED_UNKNOWN': return '页面已停止等待响应，但后端操作可能已经受理；必须读取命令或工作站状态确认。';
    case 'STATION_ADMIN_UNKNOWN_OUTCOME': return '网络响应结果未知；禁止重复提交，必须先读取后端命令或工作站状态确认。';
    case 'STATION_ADMIN_CONTRACT_UNKNOWN': return '后端响应无法按合同确认；操作可能已受理，必须先读取权威记录。';
    default: return '工作站管理员操作失败。';
  }
}

export function createStationAdminCommandOwner(options: {
  readonly api: ApiTransport;
  readonly stationId: () => string;
  readonly createRequestId?: () => string;
}): StationAdminCommandOwner {
  if (!options.api.post || !options.api.patch) {
    throw new TypeError('Station Admin commands require POST and PATCH on the shared ApiTransport.');
  }
  activeStationAdminCommandOwnerCount += 1;

  const state = reactive<MutableProjection>({
    phase: 'idle', operation: null, message: '工作站控制已就绪。', errorCode: null,
    canRecover: false, command: null, identity: null
  });
  let disposed = false;
  let controller: AbortController | undefined;
  let flight: Promise<unknown> | undefined;
  let pending: PendingCommand | null = null;

  const diagnostics: StationAdminCommandDiagnostics = Object.freeze({
    get ownerCount() { return disposed ? 0 : 1; },
    get activeAbortControllerCount() { return controller ? 1 : 0; },
    get inFlightCommandCount() { return flight ? 1 : 0; },
    get disposed() { return disposed; }
  });

  function stationId(): string {
    const value = options.stationId().trim();
    if (!value) throw new TypeError('Station id is required.');
    return value;
  }

  function requestId(): string {
    const value = (options.createRequestId ?? (() => crypto.randomUUID()))();
    if (!value.trim()) throw new TypeError('Station Admin request id is required.');
    return value;
  }

  function begin(operation: Exclude<StationAdminOperation, null>, details: Omit<PendingCommand, 'operation'>): AbortController {
    if (disposed) throw new Error('stationAdminCommandOwner has been disposed.');
    if (flight || state.phase === 'pending' || state.phase === 'reconciling' || state.phase === 'unknown-outcome') {
      throw new Error('必须先等待或恢复当前工作站操作，禁止重复提交。');
    }
    state.phase = 'pending';
    state.operation = operation;
    state.message = '正在提交工作站管理员操作。';
    state.errorCode = null;
    state.canRecover = false;
    state.command = null;
    state.identity = null;
    pending = Object.freeze({ operation, ...details });
    controller = new AbortController();
    return controller;
  }

  function fail(error: unknown): void {
    const code = errorCode(error);
    state.errorCode = code;
    state.message = messageFor(code);
    if (code === 'STATION_ADMIN_CONFLICT') {
      state.phase = 'conflict';
      state.canRecover = false;
      pending = null;
    } else if (code === 'STATION_ADMIN_UNKNOWN_OUTCOME' || code === 'STATION_ADMIN_ABORTED_UNKNOWN' ||
        code === 'STATION_ADMIN_CONTRACT_UNKNOWN' || code === 'SESSION_UNAUTHORIZED') {
      state.phase = 'unknown-outcome';
      state.canRecover = true;
    } else {
      state.phase = 'failed';
      state.canRecover = false;
      pending = null;
    }
  }

  function track<T>(operation: () => Promise<T>): Promise<T> {
    if (flight) return flight as Promise<T>;
    const current = operation();
    flight = current;
    void current.finally(() => { if (flight === current) flight = undefined; });
    return current;
  }

  async function recoverPending(): Promise<boolean> {
    if (disposed) return false;
    const target = pending;
    if (!target) return false;
    state.phase = 'reconciling';
    state.canRecover = false;
    state.message = '正在读取后端权威状态，确认上一次操作结果。';
    controller = new AbortController();
    try {
      if (target.operation === 'identity' && target.identity) {
        const payload = await options.api.get(createStationAdminDetailsPath(stationId()), { signal: controller.signal });
        const identity = decodeStationAdminDetails(payload);
        if (!identityMatches(identity, target.identity)) {
          state.phase = 'unknown-outcome';
          state.canRecover = true;
          state.message = '后端身份尚未与提交内容一致；不要重复提交，可稍后再次恢复。';
          return false;
        }
        state.identity = identity;
      } else {
        const commandType = target.operation === 'deploy-package' ? 'DeployPackage' : target.commandType;
        if (!commandType) throw new TypeError('待恢复命令缺少命令类型。');
        const payload = await options.api.get(
          createStationCommandByClientRequestPath(stationId(), commandType, target.requestId),
          { signal: controller.signal }
        );
        state.command = decodeStationCommand(payload);
      }
      pending = null;
      state.phase = target.operation === 'identity' ? 'succeeded' : 'command-created';
      state.canRecover = false;
      state.errorCode = null;
      state.message = target.operation === 'identity'
        ? '已从后端权威记录确认操作结果。'
        : '已按请求标识确认命令记录；执行终态仍以后端命令状态为准。';
      return true;
    } catch (error) {
      if (error instanceof ApiNotFoundError && target.operation !== 'identity') {
        pending = null;
        state.phase = 'failed';
        state.canRecover = false;
        state.errorCode = 'STATION_COMMAND_NOT_CREATED';
        state.message = '后端已确认该请求未创建命令；现在可以重新提交。';
        return false;
      }
      fail(error);
      return false;
    } finally {
      controller = undefined;
    }
  }

  return Object.freeze({
    projection: readonly(state),
    diagnostics,
    issueCommand(commandType: StationCommandType): Promise<StationCommand | null> {
      return track(async () => {
        const id = requestId();
        const active = begin('command', { requestId: id, commandType });
        try {
          const payload = await options.api.post?.(`stations/${encodeURIComponent(stationId())}/commands`, {
            commandType,
            payloadJson: '{}',
            expiresInSeconds: 300,
            clientRequestId: id
          }, { signal: active.signal });
          const command = decodeStationCommand(payload);
          state.command = command;
          state.phase = 'command-created';
          state.message = '命令已创建；执行结果尚未确认。';
          pending = null;
          return command;
        } catch (error) { fail(error); return null; }
        finally { controller = undefined; }
      });
    },
    reviseIdentity(input: StationIdentityUpdate): Promise<StationAdminDetails | null> {
      return track(async () => {
        const identity = normalizeIdentity(input);
        const active = begin('identity', { requestId: requestId(), identity });
        try {
          const payload = await options.api.patch?.(`stations/${encodeURIComponent(stationId())}/identity`, identity, { signal: active.signal });
          const details = decodeStationAdminDetails(payload);
          state.identity = details;
          state.phase = 'succeeded';
          state.message = '工作站身份已由后端修订。';
          pending = null;
          return details;
        } catch (error) { fail(error); return null; }
        finally { controller = undefined; }
      });
    },
    deployPackage(packageIdValue: string): Promise<StationCommand | null> {
      return track(async () => {
        const packageId = packageIdValue.trim();
        if (!packageId) throw new TypeError('请选择运行包。');
        const id = requestId();
        const active = begin('deploy-package', { requestId: id, packageId });
        try {
          const payload = await options.api.post?.(
            `stations/${encodeURIComponent(stationId())}/deploy-package`,
            { packageId, clientRequestId: id },
            { signal: active.signal }
          );
          const command = decodeStationCommand(payload);
          state.command = command;
          state.phase = 'command-created';
          state.message = '部署命令已创建；仅在命令成功且工作站激活身份匹配后才算部署完成。';
          pending = null;
          return command;
        } catch (error) { fail(error); return null; }
        finally { controller = undefined; }
      });
    },
    recover(): Promise<boolean> {
      if (flight) return Promise.resolve(false);
      return track(recoverPending);
    },
    reset(): void {
      if (disposed || flight || state.phase === 'unknown-outcome' || state.phase === 'reconciling') return;
      state.phase = 'idle'; state.operation = null; state.message = '工作站控制已就绪。';
      state.errorCode = null; state.canRecover = false; state.command = null; state.identity = null;
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      activeStationAdminCommandOwnerCount -= 1;
      controller?.abort('station-admin-owner-disposed');
      controller = undefined;
      if (state.phase === 'pending' || state.phase === 'reconciling') {
        state.phase = 'unknown-outcome';
        state.errorCode = 'STATION_ADMIN_ABORTED_UNKNOWN';
        state.message = messageFor(state.errorCode);
        state.canRecover = true;
      } else {
        state.phase = 'disposed';
      }
    }
  });
}
