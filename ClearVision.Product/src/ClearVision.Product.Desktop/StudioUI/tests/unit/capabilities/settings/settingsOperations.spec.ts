import { describe, expect, it, vi } from 'vitest';
import type { ProductRuntime } from '@/app/productRuntime';
import type { ApiTransport } from '@/platform/api';
import {
  createSettingsOwner,
  type SettingsOwner
} from '@/capabilities/settings';

function fullSettings() {
  return {
    revision: 9,
    general: { softwareTitle: 'Studio', theme: 'dark', autoStart: false },
    storage: { imageSavePath: 'D:/VisionData', savePolicy: 'NgOnly', retentionDays: 30, minFreeSpaceGb: 5 },
    runtime: { autoRun: false, stopOnConsecutiveNg: 3, missingMaterialTimeoutSeconds: 120, applyProtectionRules: true },
    security: { passwordMinLength: 8, sessionTimeoutMinutes: 30, loginFailureLockoutCount: 5 },
    communication: {}, tcpCommunication: {}, features: {}, cameras: [], activeCameraId: ''
  };
}

function api(overrides: Partial<ApiTransport> = {}): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(): Promise<T | undefined> {
      return fullSettings() as T;
    },
    ...overrides
  } as ApiTransport;
}

function ownerWithApi(apiTransport: ApiTransport, role: string): SettingsOwner {
  return createSettingsOwner({
    runtime: { api: apiTransport } as Pick<ProductRuntime, 'api'>,
    role
  });
}

describe('F07 G3/G4 Settings owner endpoint operations', () => {
  it('writes only the selected generic section and replaces the authoritative projection', async () => {
    const put = vi.fn(async <T = unknown>(
      _path: string,
      body: unknown
    ): Promise<T | undefined> => {
      expect(body).toEqual({ saveScope: 'general', general: { softwareTitle: 'Updated' } });
      return {
        message: 'saved',
        config: { ...fullSettings(), general: { softwareTitle: 'Updated', theme: 'dark', autoStart: false } }
      } as T;
    }) as NonNullable<ApiTransport['put']>;
    const owner = ownerWithApi(api({ put }), 'Admin');

    const result = await owner.saveGenericSection('general', { softwareTitle: 'Updated' });

    expect(result).toMatchObject({ status: 'completed', value: { config: { sections: { general: { softwareTitle: 'Updated' } } } } });
    expect(put).toHaveBeenCalledTimes(1);
    expect(owner.projection.settings?.sections.general.softwareTitle).toBe('Updated');
    owner.dispose();
  });

  it('fails closed for Engineer generic mutation and Operator account operations', async () => {
    const put = vi.fn();
    const post = vi.fn();
    const engineer = ownerWithApi(api({ put, post }), 'Engineer');
    const engineerSave = await engineer.saveGenericSection('runtime', { autoRun: true });
    expect(engineerSave.status).toBe('forbidden');
    expect(put).not.toHaveBeenCalled();
    engineer.dispose();

    const operator = ownerWithApi(api({ post }), 'Operator');
    const operatorPassword = await operator.changePassword({ oldPassword: 'old', newPassword: 'new' });
    expect(operatorPassword.status).toBe('forbidden');
    expect(post).not.toHaveBeenCalled();
    operator.dispose();
  });

  it('binds password and database operations to their concrete endpoint contracts', async () => {
    const post = vi.fn(async <T = unknown>(path: string): Promise<T | undefined> => {
      if (path === 'auth/change-password') return { message: 'changed' } as T;
      if (path === 'settings/database/backup') {
        return {
          backupPath: 'C:/private/manual.cvdbbak',
          createdAtUtc: '2026-08-01T00:00:00Z',
          sizeBytes: 10,
          databaseSizeBytes: 8,
          packageFileCount: 1,
          packageBytes: 2
        } as T;
      }
      throw new Error(`unexpected path: ${path}`);
    }) as NonNullable<ApiTransport['post']>;
    const get = vi.fn(async <T = unknown>(
      path: string
    ): Promise<T | undefined> => {
      if (path === 'settings/database/status') {
        return {
          databasePath: 'C:/private/vision.db', exists: true, state: 'Healthy', schemaVersion: 6,
          currentSchemaVersion: 6, appliedMigrations: [], pendingMigrations: [], missingSchemaItems: [],
          integrityCheck: 'ok', foreignKeyViolationCount: 0, rowCounts: {}, issues: [], databaseSizeBytes: 8,
          walSizeBytes: 0, backupRootDirectory: 'C:/private', packageRootDirectory: 'C:/private', packageFileCount: 1
        } as T;
      }
      return fullSettings() as T;
    }) as NonNullable<ApiTransport['get']>;
    const owner = ownerWithApi(api({ get, post }), 'Admin');

    const password = await owner.changePassword({ oldPassword: 'old', newPassword: 'new' });
    const status = await owner.readDatabaseStatus();
    const backup = await owner.backupDatabase();

    expect(password.status).toBe('completed');
    expect(status.status).toBe('completed');
    expect(backup.status).toBe('completed');
    expect(post).toHaveBeenNthCalledWith(1, 'auth/change-password', { oldPassword: 'old', newPassword: 'new' }, expect.anything());
    expect(post).toHaveBeenNthCalledWith(2, 'settings/database/backup', {}, expect.anything());
    if (backup.status === 'completed') expect(backup.value).not.toHaveProperty('backupPath');
    owner.dispose();
  });

  it('keeps Admin user management on dedicated endpoints and out of projections', async () => {
    const user = {
      id: 'user-1', username: 'operator', displayName: 'Operator', role: 2,
      isActive: true, lastLoginAt: null
    };
    const get = vi.fn(async <T = unknown>(path: string): Promise<T | undefined> => {
      if (path === 'users') return [user] as T;
      return fullSettings() as T;
    }) as NonNullable<ApiTransport['get']>;
    const post = vi.fn(async <T = unknown>(path: string): Promise<T | undefined> => {
      if (path === 'users') return user as T;
      return { message: '密码重置成功' } as T;
    }) as NonNullable<ApiTransport['post']>;
    const put = vi.fn(async <T = unknown>(path: string): Promise<T | undefined> => {
      if (path !== 'users/user-1') throw new Error(`unexpected path: ${path}`);
      return user as T;
    }) as NonNullable<ApiTransport['put']>;
    const remove = vi.fn(async (path: string): Promise<void> => {
      expect(path).toBe('users/user-1');
    }) as NonNullable<ApiTransport['delete']>;
    const owner = ownerWithApi(api({ get, post, put, delete: remove }), 'Admin');

    const listed = await owner.readUsers();
    const created = await owner.createUser({
      username: 'operator', displayName: 'Operator', role: 2, password: 'fixture-password'
    });
    const updated = await owner.updateUser('user-1', { displayName: 'Updated', role: 2, isActive: true });
    const deleted = await owner.deleteUser('user-1');
    const reset = await owner.resetUserPassword('user-1', 'fixture-reset-password');

    expect(listed.status).toBe('completed');
    expect(created.status).toBe('completed');
    expect(updated.status).toBe('completed');
    expect(deleted.status).toBe('completed');
    expect(reset.status).toBe('completed');
    expect(get).toHaveBeenCalledWith('users', expect.anything());
    expect(post).toHaveBeenNthCalledWith(1, 'users', expect.objectContaining({ username: 'operator' }), expect.anything());
    expect(post).toHaveBeenNthCalledWith(2, 'users/user-1/reset-password', expect.objectContaining({ newPassword: 'fixture-reset-password' }), expect.anything());
    expect(put).toHaveBeenCalledWith('users/user-1', expect.objectContaining({ displayName: 'Updated' }), expect.anything());
    expect(remove).toHaveBeenCalledWith('users/user-1', expect.anything());
    if (listed.status === 'completed') expect(listed.value.items[0]).not.toHaveProperty('password');
    owner.dispose();
  });
});
