import type { ApiTransport } from '@/platform/api';
import { ApiConfigurationError } from '@/platform/api';
import {
  buildGenericSectionWritePayload,
  type GenericSettingsSection,
  type SettingsWriteTaskContext
} from './contracts';
import {
  decodeAiModelsProjectionV1,
  decodeAiModelConnectionTestProjectionV1,
  decodeAiModelMutationResponseV1,
  decodeAiReasoningSupportProjectionV1,
  decodeSettingsAccountOperationResponseV1,
  decodeSettingsDatabaseBackupProjectionV1,
  decodeSettingsDatabaseStatusProjectionV1,
  decodeSettingsDiskUsageProjectionV1,
  decodeSettingsProjectionV1,
  decodeSettingsUserProjectionV1,
  decodeSettingsUsersProjectionV1,
  decodeSettingsWriteResponseV1,
  decodeStationCommunicationProjectionV1,
  decodeStationTokenOperationV1,
  type AiModelConnectionTestProjectionV1,
  type AiModelMutationResponseV1,
  type AiReasoningSupportProjectionV1,
  type AiModelsProjectionV1,
  type SettingsAccountOperationResponseV1,
  type SettingsDatabaseBackupProjectionV1,
  type SettingsDatabaseStatusProjectionV1,
  type SettingsDiskUsageProjectionV1,
  type SettingsProjectionV1,
  type SettingsUserProjectionV1,
  type SettingsUsersProjectionV1,
  type SettingsWriteResponseV1,
  type StationCommunicationProjectionV1,
  type StationTokenOperationV1
} from './decoder';
import {
  createSettingsDeviceApiAdapter,
  type SettingsDeviceApiAdapter
} from './deviceApiAdapter';

export interface SettingsChangePasswordRequest {
  readonly oldPassword: string;
  readonly newPassword: string;
}

export interface SettingsCreateUserRequest {
  readonly username: string;
  readonly password: string;
  readonly displayName: string;
  readonly role: number;
}

export interface SettingsUpdateUserRequest {
  readonly displayName: string;
  readonly role: number;
  readonly isActive: boolean;
}

export type StationTokenOperationNameV1 = 'regenerate';

export interface StationCommunicationSettingsUpdateRequestV1 {
  readonly mode: string;
  readonly port: number;
  readonly lanHost: string;
  readonly localStationSyncEnabled: boolean;
  /** Omit for preserve; provide only for an explicit replacement. */
  readonly sharedToken?: string;
}

export type AiApiKeyOperationV1 = 'keep' | 'replace' | 'clear';
export type AiBaseUrlOperationV1 = 'preserve' | 'replace' | 'clear';

export interface AiModelMutationRequestV1 {
  readonly name?: string | null;
  readonly displayName: string;
  readonly provider: string;
  readonly apiKey?: string;
  readonly apiKeyOperation?: AiApiKeyOperationV1;
  readonly model: string;
  readonly baseUrlOperation?: AiBaseUrlOperationV1;
  readonly baseUrl?: string | null;
  readonly timeoutMs: number;
  readonly protocol?: string | null;
  readonly wireApi?: string | null;
  readonly authMode?: string | null;
  readonly authHeaderName?: string | null;
  readonly extraHeaders?: Readonly<Record<string, string>> | null;
  readonly extraQuery?: Readonly<Record<string, string>> | null;
  readonly extraBody?: Readonly<Record<string, unknown>> | null;
  readonly reasoning?: Readonly<Record<string, string>> | null;
  readonly roleBindings?: readonly string[] | null;
  readonly modelRole?: string | null;
  readonly priority?: number | null;
  readonly isEnabled: boolean;
  readonly remark?: string | null;
  readonly capabilities?: Readonly<Record<string, unknown>> | null;
}

export interface AiReasoningSupportRequestV1 {
  readonly provider: string;
  readonly model: string;
  readonly baseUrl?: string | null;
  readonly protocol?: string | null;
}

export interface SettingsApiAdapter extends SettingsDeviceApiAdapter {
  readGenericProjection(signal?: AbortSignal): Promise<SettingsProjectionV1>;
  readStationCommunication(signal?: AbortSignal): Promise<StationCommunicationProjectionV1>;
  writeStationCommunication(
    request: StationCommunicationSettingsUpdateRequestV1,
    signal?: AbortSignal
  ): Promise<StationCommunicationProjectionV1>;
  stationToken(
    operation: StationTokenOperationNameV1,
    signal?: AbortSignal
  ): Promise<StationTokenOperationV1>;
  readAiModels(signal?: AbortSignal): Promise<AiModelsProjectionV1>;
  createAiModel(
    request: AiModelMutationRequestV1,
    signal?: AbortSignal
  ): Promise<AiModelMutationResponseV1>;
  updateAiModel(
    id: string,
    request: AiModelMutationRequestV1,
    signal?: AbortSignal
  ): Promise<AiModelMutationResponseV1>;
  deleteAiModel(id: string, signal?: AbortSignal): Promise<AiModelMutationResponseV1>;
  activateAiModel(id: string, signal?: AbortSignal): Promise<AiModelMutationResponseV1>;
  setAiModelDefault(
    id: string,
    role: 'planner' | 'shadow-eval',
    signal?: AbortSignal
  ): Promise<AiModelMutationResponseV1>;
  testAiModel(id: string, signal?: AbortSignal): Promise<AiModelConnectionTestProjectionV1>;
  readAiReasoningSupport(
    request: AiReasoningSupportRequestV1,
    signal?: AbortSignal
  ): Promise<AiReasoningSupportProjectionV1>;
  writeGenericSection(
    section: GenericSettingsSection,
    value: Readonly<Record<string, unknown>>,
    signal?: AbortSignal
  ): Promise<SettingsWriteResponseV1>;
  readDiskUsage(path?: string, signal?: AbortSignal): Promise<SettingsDiskUsageProjectionV1>;
  readDatabaseStatus(signal?: AbortSignal): Promise<SettingsDatabaseStatusProjectionV1>;
  backupDatabase(signal?: AbortSignal): Promise<SettingsDatabaseBackupProjectionV1>;
  changePassword(
    request: SettingsChangePasswordRequest,
    signal?: AbortSignal
  ): Promise<SettingsAccountOperationResponseV1>;
  readUsers(signal?: AbortSignal): Promise<SettingsUsersProjectionV1>;
  createUser(request: SettingsCreateUserRequest, signal?: AbortSignal): Promise<SettingsUserProjectionV1>;
  updateUser(id: string, request: SettingsUpdateUserRequest, signal?: AbortSignal): Promise<SettingsUserProjectionV1>;
  deleteUser(id: string, signal?: AbortSignal): Promise<void>;
  resetUserPassword(id: string, newPassword: string, signal?: AbortSignal): Promise<SettingsAccountOperationResponseV1>;
  prepareGenericSectionWrite(
    section: GenericSettingsSection,
    value: Readonly<Record<string, unknown>>
  ): Readonly<Record<string, unknown>>;
}

function signalOptions(signal?: AbortSignal): Readonly<{ readonly signal?: AbortSignal }> {
  return signal ? { signal } : {};
}

function requiredMethod<T>(method: T | undefined, name: string): T {
  if (method === undefined) throw new ApiConfigurationError(`Settings API transport does not provide ${name}.`);
  return method;
}

function userPath(id: string, suffix = ''): string {
  const normalized = id.trim();
  if (!normalized || normalized.includes('/')) {
    throw new ApiConfigurationError('Settings user id must be a non-empty path-safe value.');
  }
  return `users/${encodeURIComponent(normalized)}${suffix}`;
}

function modelPath(id: string, suffix = ''): string {
  const normalized = id.trim();
  if (!normalized || normalized.includes('/')) {
    throw new ApiConfigurationError('AI model id must be a non-empty path-safe value.');
  }
  return `ai/models/${encodeURIComponent(normalized)}${suffix}`;
}

export function createSettingsApiAdapter(api: ApiTransport): SettingsApiAdapter {
  const deviceAdapter = createSettingsDeviceApiAdapter(api);
  return Object.freeze({
    ...deviceAdapter,
    async readGenericProjection(signal?: AbortSignal): Promise<SettingsProjectionV1> {
      return decodeSettingsProjectionV1(await api.get('settings', signalOptions(signal)));
    },
    async readStationCommunication(signal?: AbortSignal): Promise<StationCommunicationProjectionV1> {
      return decodeStationCommunicationProjectionV1(
        await api.get('station-communication/settings', signalOptions(signal))
      );
    },
    async writeStationCommunication(
      request: StationCommunicationSettingsUpdateRequestV1,
      signal?: AbortSignal
    ): Promise<StationCommunicationProjectionV1> {
      const put = requiredMethod(api.put, 'PUT');
      return decodeStationCommunicationProjectionV1(
        await put('station-communication/settings', request, signalOptions(signal))
      );
    },
    async stationToken(
      operation: StationTokenOperationNameV1,
      signal?: AbortSignal
    ): Promise<StationTokenOperationV1> {
      const post = requiredMethod(api.post, 'POST');
      return decodeStationTokenOperationV1(
        await post('station-communication/token', { operation }, signalOptions(signal))
      );
    },
    async readAiModels(signal?: AbortSignal): Promise<AiModelsProjectionV1> {
      return decodeAiModelsProjectionV1(await api.get('ai/models', signalOptions(signal)));
    },
    async createAiModel(
      request: AiModelMutationRequestV1,
      signal?: AbortSignal
    ): Promise<AiModelMutationResponseV1> {
      const post = requiredMethod(api.post, 'POST');
      return decodeAiModelMutationResponseV1(await post('ai/models', request, signalOptions(signal)));
    },
    async updateAiModel(
      id: string,
      request: AiModelMutationRequestV1,
      signal?: AbortSignal
    ): Promise<AiModelMutationResponseV1> {
      const put = requiredMethod(api.put, 'PUT');
      return decodeAiModelMutationResponseV1(await put(modelPath(id), request, signalOptions(signal)));
    },
    async deleteAiModel(id: string, signal?: AbortSignal): Promise<AiModelMutationResponseV1> {
      const remove = requiredMethod(api.delete, 'DELETE');
      await remove(modelPath(id), signalOptions(signal));
      return Object.freeze({ message: 'AI model deleted.', id, role: null });
    },
    async activateAiModel(id: string, signal?: AbortSignal): Promise<AiModelMutationResponseV1> {
      const post = requiredMethod(api.post, 'POST');
      return decodeAiModelMutationResponseV1(
        await post(modelPath(id, '/activate'), {}, signalOptions(signal))
      );
    },
    async setAiModelDefault(
      id: string,
      role: 'planner' | 'shadow-eval',
      signal?: AbortSignal
    ): Promise<AiModelMutationResponseV1> {
      const post = requiredMethod(api.post, 'POST');
      const suffix = role === 'planner' ? '/default-planner' : '/default-shadow-eval';
      return decodeAiModelMutationResponseV1(
        await post(modelPath(id, suffix), {}, signalOptions(signal))
      );
    },
    async testAiModel(id: string, signal?: AbortSignal): Promise<AiModelConnectionTestProjectionV1> {
      const post = requiredMethod(api.post, 'POST');
      return decodeAiModelConnectionTestProjectionV1(
        await post(modelPath(id, '/test'), {}, signalOptions(signal))
      );
    },
    async readAiReasoningSupport(
      request: AiReasoningSupportRequestV1,
      signal?: AbortSignal
    ): Promise<AiReasoningSupportProjectionV1> {
      const post = requiredMethod(api.post, 'POST');
      return decodeAiReasoningSupportProjectionV1(
        await post('ai/reasoning-support', request, signalOptions(signal))
      );
    },
    async writeGenericSection(
      section: GenericSettingsSection,
      value: Readonly<Record<string, unknown>>,
      signal?: AbortSignal
    ): Promise<SettingsWriteResponseV1> {
      const put = requiredMethod(api.put, 'PUT');
      return decodeSettingsWriteResponseV1(
        await put('settings', buildGenericSectionWritePayload(section, value), signalOptions(signal))
      );
    },
    async readDiskUsage(path?: string, signal?: AbortSignal): Promise<SettingsDiskUsageProjectionV1> {
      const query = path === undefined ? '' : `?path=${encodeURIComponent(path)}`;
      return decodeSettingsDiskUsageProjectionV1(await api.get(`settings/disk-usage${query}`, signalOptions(signal)));
    },
    async readDatabaseStatus(signal?: AbortSignal): Promise<SettingsDatabaseStatusProjectionV1> {
      return decodeSettingsDatabaseStatusProjectionV1(
        await api.get('settings/database/status', signalOptions(signal))
      );
    },
    async backupDatabase(signal?: AbortSignal): Promise<SettingsDatabaseBackupProjectionV1> {
      const post = requiredMethod(api.post, 'POST');
      return decodeSettingsDatabaseBackupProjectionV1(
        await post('settings/database/backup', {}, signalOptions(signal))
      );
    },
    async changePassword(
      request: SettingsChangePasswordRequest,
      signal?: AbortSignal
    ): Promise<SettingsAccountOperationResponseV1> {
      const post = requiredMethod(api.post, 'POST');
      return decodeSettingsAccountOperationResponseV1(
        await post('auth/change-password', request, signalOptions(signal))
      );
    },
    async readUsers(signal?: AbortSignal): Promise<SettingsUsersProjectionV1> {
      return decodeSettingsUsersProjectionV1(await api.get('users', signalOptions(signal)));
    },
    async createUser(
      request: SettingsCreateUserRequest,
      signal?: AbortSignal
    ): Promise<SettingsUserProjectionV1> {
      const post = requiredMethod(api.post, 'POST');
      return decodeSettingsUserProjectionV1(await post('users', request, signalOptions(signal)));
    },
    async updateUser(
      id: string,
      request: SettingsUpdateUserRequest,
      signal?: AbortSignal
    ): Promise<SettingsUserProjectionV1> {
      const put = requiredMethod(api.put, 'PUT');
      return decodeSettingsUserProjectionV1(await put(userPath(id), request, signalOptions(signal)));
    },
    async deleteUser(id: string, signal?: AbortSignal): Promise<void> {
      const remove = requiredMethod(api.delete, 'DELETE');
      await remove(userPath(id), signalOptions(signal));
    },
    async resetUserPassword(
      id: string,
      newPassword: string,
      signal?: AbortSignal
    ): Promise<SettingsAccountOperationResponseV1> {
      const post = requiredMethod(api.post, 'POST');
      return decodeSettingsAccountOperationResponseV1(
        await post(`${userPath(id)}/reset-password`, { newPassword }, signalOptions(signal))
      );
    },
    prepareGenericSectionWrite(
      section: GenericSettingsSection,
      value: Readonly<Record<string, unknown>>
    ): Readonly<Record<string, unknown>> {
      return buildGenericSectionWritePayload(section, value);
    }
  });
}

/**
 * Keeps future writer signatures tied to the shared transport context without
 * providing a concrete section save method in G1.
 */
export type SettingsSectionWriterContext = SettingsWriteTaskContext;
