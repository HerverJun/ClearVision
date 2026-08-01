import type { ApiTransport } from '@/platform/api';
import { ApiConfigurationError } from '@/platform/api';
import {
  buildGenericSectionWritePayload,
  type GenericSettingsSection,
  type SettingsWriteTaskContext
} from './contracts';
import {
  decodeAiModelsProjectionV1,
  decodeSettingsAccountOperationResponseV1,
  decodeSettingsDatabaseBackupProjectionV1,
  decodeSettingsDatabaseStatusProjectionV1,
  decodeSettingsDiskUsageProjectionV1,
  decodeSettingsProjectionV1,
  decodeSettingsUserProjectionV1,
  decodeSettingsUsersProjectionV1,
  decodeSettingsWriteResponseV1,
  decodeStationCommunicationProjectionV1,
  type AiModelsProjectionV1,
  type SettingsAccountOperationResponseV1,
  type SettingsDatabaseBackupProjectionV1,
  type SettingsDatabaseStatusProjectionV1,
  type SettingsDiskUsageProjectionV1,
  type SettingsProjectionV1,
  type SettingsUserProjectionV1,
  type SettingsUsersProjectionV1,
  type SettingsWriteResponseV1,
  type StationCommunicationProjectionV1
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

export interface SettingsApiAdapter extends SettingsDeviceApiAdapter {
  readGenericProjection(signal?: AbortSignal): Promise<SettingsProjectionV1>;
  readStationCommunication(signal?: AbortSignal): Promise<StationCommunicationProjectionV1>;
  readAiModels(signal?: AbortSignal): Promise<AiModelsProjectionV1>;
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
    async readAiModels(signal?: AbortSignal): Promise<AiModelsProjectionV1> {
      return decodeAiModelsProjectionV1(await api.get('ai/models', signalOptions(signal)));
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
