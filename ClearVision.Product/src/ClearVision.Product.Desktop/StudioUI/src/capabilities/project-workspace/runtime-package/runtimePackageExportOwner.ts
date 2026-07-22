import { reactive, readonly, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiForbiddenError,
  ApiNetworkError,
  type ApiTransport
} from '@/platform/api';
import type { WorkspacePersistenceOwner } from '../persistence';

export interface RuntimePackageExportResultV1 {
  readonly packageRootPath: string;
  readonly packageId: string;
  readonly packageName: string;
  readonly flowHash: string;
  readonly decisionConfigurationHash: string;
  readonly registeredForStationDeployment: boolean;
  readonly stationPackageId: string | null;
  readonly readmePath: string | null;
}

export interface RuntimePackageExportProjection {
  readonly phase: 'idle' | 'saving' | 'exporting' | 'success' | 'error' | 'forbidden' | 'unknown-outcome' | 'disposed';
  readonly result: RuntimePackageExportResultV1 | null;
  readonly message: string;
  readonly canExport: boolean;
  readonly requestedRevision: number | null;
  readonly requestedAtUtc: string | null;
}

type MutableProjection = { -readonly [Key in keyof RuntimePackageExportProjection]: RuntimePackageExportProjection[Key] };

export interface RuntimePackageExportOwner {
  readonly projection: DeepReadonly<RuntimePackageExportProjection>;
  exportPackage(): Promise<RuntimePackageExportResultV1 | null>;
  cancel(): void;
  dispose(): void;
}

function record(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function field(source: Readonly<Record<string, unknown>>, camel: string): unknown {
  if (Object.prototype.hasOwnProperty.call(source, camel)) return source[camel];
  return source[`${camel.slice(0, 1).toUpperCase()}${camel.slice(1)}`];
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : value === null || value === undefined ? '' : String(value).trim();
}

function decodeResult(payload: unknown): RuntimePackageExportResultV1 {
  const source = record(payload);
  const result = Object.freeze({
    packageRootPath: text(field(source, 'packageRootPath')),
    packageId: text(field(source, 'packageId')),
    packageName: text(field(source, 'packageName')),
    flowHash: text(field(source, 'flowHash')),
    decisionConfigurationHash: text(field(source, 'decisionConfigurationHash')),
    registeredForStationDeployment: field(source, 'registeredForStationDeployment') === true,
    stationPackageId: text(field(source, 'stationPackageId')) || null,
    readmePath: text(field(source, 'readmePath')) || null
  });
  if (!result.packageId || !result.flowHash || !result.decisionConfigurationHash) {
    throw new TypeError('Runtime Package 响应缺少正式 package/hash identity。');
  }
  return result;
}

export function createRuntimePackageExportOwner(options: {
  readonly projectId: string;
  readonly persistenceOwner: WorkspacePersistenceOwner;
  readonly api: ApiTransport;
}): RuntimePackageExportOwner {
  if (!options.api.post) throw new TypeError('Runtime Package 导出需要 shared ApiTransport POST。');
  let disposed = false;
  let generation = 0;
  let controller: AbortController | null = null;
  const state = reactive<MutableProjection>({
    phase: 'idle',
    result: null,
    message: '仅导出已正式保存、无未应用修改的工程。',
    canExport: true,
    requestedRevision: null,
    requestedAtUtc: null
  });

  function syncAvailable(): void {
    state.canExport = !disposed && !['saving', 'exporting', 'unknown-outcome'].includes(state.phase);
  }

  const owner: RuntimePackageExportOwner = Object.freeze({
    projection: readonly(state),
    async exportPackage(): Promise<RuntimePackageExportResultV1 | null> {
      if (disposed || !state.canExport) return null;
      const operation = ++generation;
      state.result = null;
      if (options.persistenceOwner.projection.dirty) {
        state.phase = 'saving';
        state.message = '工程存在修改，正在通过统一保存链正式保存。';
        syncAvailable();
        const saved = await options.persistenceOwner.save();
        if (disposed || operation !== generation) return null;
        if (saved.status !== 'saved' && saved.status !== 'no-op') {
          state.phase = 'error';
          state.message = '工程未能正式保存，未发起运行包导出。';
          syncAvailable();
          return null;
        }
      }
      if (options.persistenceOwner.projection.dirty || !options.persistenceOwner.projection.canRun) {
        state.phase = 'error';
        state.message = '工程仍有未保存修改或处于运行锁定状态，未发起导出。';
        syncAvailable();
        return null;
      }
      controller = new AbortController();
      state.phase = 'exporting';
      state.requestedRevision = options.persistenceOwner.projection.persistenceRevision;
      state.requestedAtUtc = new Date().toISOString();
      state.message = `正在由服务端导出 revision ${state.requestedRevision} 的运行包。`;
      syncAvailable();
      try {
        // Deliberately no Flow override: the backend exports the persisted Project snapshot.
        const result = decodeResult(await options.api.post!(
          `projects/${encodeURIComponent(options.projectId)}/runtime-package/export`,
          { registerForStationDeployment: true },
          { signal: controller.signal }
        ));
        if (disposed || operation !== generation) return null;
        state.result = result;
        state.phase = 'success';
        state.message = `运行包 ${result.packageId} 已由服务端生成。`;
        return result;
      } catch (error) {
        if (disposed || operation !== generation || error instanceof ApiAbortError || controller.signal.aborted) return null;
        if (error instanceof ApiForbiddenError) {
          state.phase = 'forbidden';
          state.message = '导出运行包需要管理员权限。';
        } else if (error instanceof ApiNetworkError) {
          state.phase = 'unknown-outcome';
          state.message = '导出响应未知，禁止自动重试；请按工程、revision 与请求时间核对已注册运行包。';
        } else {
          state.phase = 'error';
          state.message = `运行包导出失败：${error instanceof Error ? error.message : '服务端校验未通过。'}`;
        }
        return null;
      } finally {
        controller = null;
        syncAvailable();
      }
    },
    cancel(): void {
      if (disposed || !controller) return;
      generation += 1;
      controller.abort('runtime-package-export-cancelled');
      controller = null;
      state.phase = 'idle';
      state.message = '已取消本次运行包导出请求。';
      syncAvailable();
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      controller?.abort('runtime-package-owner-disposed');
      controller = null;
      state.phase = 'disposed';
      state.canExport = false;
      state.result = null;
    }
  });
  return owner;
}
