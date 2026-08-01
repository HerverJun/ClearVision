import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, nextTick, reactive, ref } from 'vue';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AuthLifecycleOwner } from '@/app/auth';
import type {
  SettingsDatabaseBackupProjectionV1,
  SettingsDatabaseStatusProjectionV1,
  SettingsOwner,
  SettingsPanelState,
  SettingsUserProjectionV1,
  SettingsWriteResult
} from '@/capabilities/settings';
import SettingsSecurityPanel from '@/capabilities/settings/SettingsSecurityPanel.vue';
import SettingsUsersPanel from '@/capabilities/settings/SettingsUsersPanel.vue';
import SettingsDatabasePanel from '@/capabilities/settings/SettingsDatabasePanel.vue';

const mountedWrappers: Array<{ unmount: () => void }> = [];

afterEach(() => {
  while (mountedWrappers.length > 0) mountedWrappers.pop()?.unmount();
  vi.restoreAllMocks();
  document.body.querySelectorAll('[data-design-primitive="modal"]').forEach(element => element.parentElement?.remove());
});

function completed<T>(value: T): SettingsWriteResult<T> {
  return { status: 'completed', section: 'security', generation: 0, operationKind: 'write', value };
}

function user(): SettingsUserProjectionV1 {
  return {
    id: 'user-1',
    username: 'operator',
    displayName: 'Operator',
    role: 'Operator',
    isActive: true,
    lastLoginAt: null
  };
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>(resolvePromise => { resolve = resolvePromise; });
  return { promise, resolve };
}

function createOwnerFixture() {
  const readers = new Set<() => SettingsPanelState>();
  const pending = deferred<SettingsWriteResult<unknown>>();
  const currentUser = user();
  const projection = reactive({
    phase: 'ready',
    role: 'Admin',
    settings: null,
    error: null,
    message: '',
    generation: 0,
    started: true,
    dirtySectionCount: 0,
    pendingSectionCount: 0,
    unknownOutcomeKeys: [],
    device: {
      plcSettings: null,
      plcMappings: [],
      tcpProfiles: [],
      tcpStatuses: {},
      tcpFrames: {},
      cameraBindings: [],
      activeCameraId: '',
      cameraDiscovery: null,
      triggerDiagnostics: null,
      serialPorts: [],
      preview: {
        phase: 'idle',
        sessionId: null,
        cameraBindingId: null,
        imageUrl: null,
        width: null,
        height: null,
        frameSequence: null,
        triggerMode: null,
        triggerSource: null,
        contentType: null,
        message: 'idle'
      }
    }
  });
  const readUsers = vi.fn(async () => completed({ items: [currentUser] }));
  const readDatabaseStatus = vi.fn(async () => completed({
    databasePath: 'C:/private/vision.db',
    exists: true,
    state: 'Healthy',
    schemaVersion: 1,
    currentSchemaVersion: 1,
    appliedMigrations: [],
    pendingMigrations: [],
    missingSchemaItems: [],
    integrityCheck: 'ok',
    issues: [],
    checkedAtUtc: '2026-08-02T00:00:00Z',
    databaseSizeBytes: 1,
    walSizeBytes: 0,
    packageFileCount: 0
  }) as SettingsWriteResult<SettingsDatabaseStatusProjectionV1>);
  const createUser = vi.fn(() => pending.promise as unknown as Promise<SettingsWriteResult<SettingsUserProjectionV1>>);
  const updateUser = vi.fn(() => pending.promise as unknown as Promise<SettingsWriteResult<SettingsUserProjectionV1>>);
  const deleteUser = vi.fn(() => pending.promise as unknown as Promise<SettingsWriteResult<void>>);
  const resetUserPassword = vi.fn(() => pending.promise as unknown as Promise<SettingsWriteResult<{ message: string }>>);
  const backupDatabase = vi.fn(() => pending.promise as unknown as Promise<SettingsWriteResult<SettingsDatabaseBackupProjectionV1>>);
  const owner = {
    projection,
    registerPanelState: vi.fn((_section: string, reader: () => SettingsPanelState) => {
      readers.add(reader);
      return () => readers.delete(reader);
    }),
    refreshPanelState: vi.fn(),
    readUsers,
    readDatabaseStatus,
    createUser,
    updateUser,
    deleteUser,
    resetUserPassword,
    backupDatabase,
    saveGenericSection: vi.fn(),
    changePassword: vi.fn(),
    recordChangePasswordSessionResult: vi.fn()
  } as unknown as SettingsOwner;
  const leaveProtection = vi.fn(() => {
    for (const reader of readers) {
      if (reader().pending) return 'settings-pending';
    }
    return null;
  });
  return {
    owner,
    projection,
    pending,
    readUsers,
    createUser,
    updateUser,
    deleteUser,
    resetUserPassword,
    backupDatabase,
    leaveProtection
  };
}

function fakeAuth(
  changePassword: AuthLifecycleOwner['changePassword'] = vi.fn(async () => false)
): AuthLifecycleOwner {
  const projection = reactive({
    phase: 'authenticated',
    user: { userId: 'admin-1', username: 'admin', role: 'Admin' },
    setupPolicy: null,
    sessionGeneration: 1,
    message: 'authenticated',
    errorCode: null,
    updatedAt: Date.now()
  });
  const sessionProjection = reactive({
    phase: 'authenticated',
    user: projection.user,
    sessionGeneration: 1,
    message: 'authenticated',
    updatedAt: Date.now()
  });
  return {
    projection,
    session: { projection: sessionProjection },
    changePassword
  } as unknown as AuthLifecycleOwner;
}

describe('F07 G6 sensitive Settings lifecycle', () => {
  it('starts the auth transition before marking the Settings mutation pending', async () => {
    const fixture = createOwnerFixture();
    const protectionDuringAuthTransition: Array<string | null> = [];
    const auth = fakeAuth(vi.fn(async () => {
      protectionDuringAuthTransition.push(fixture.leaveProtection());
      return false;
    }));
    const wrapper = mount(SettingsSecurityPanel, {
      props: {
        owner: fixture.owner,
        auth,
        role: 'Admin',
        projection: { passwordMinLength: 8, sessionTimeoutMinutes: 30, loginFailureLockoutCount: 5 }
      }
    });
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('input[autocomplete="current-password"]').setValue('old-secret');
    await wrapper.get('input[autocomplete="new-password"]').setValue('new-secret');
    await wrapper.get('form.settings-password-form').trigger('submit');
    await flushPromises();

    expect(protectionDuringAuthTransition).toEqual([null]);
  });

  it('clears all password fields and closes the reset modal when Security is deactivated by KeepAlive', async () => {
    const fixture = createOwnerFixture();
    const auth = fakeAuth();
    const active = ref<'security' | 'other'>('security');
    const host = defineComponent({
      components: { SettingsSecurityPanel },
      setup() {
        return { active, auth, owner: fixture.owner };
      },
      template: `
        <KeepAlive>
          <SettingsSecurityPanel
            v-if="active === 'security'"
            :projection="{ passwordMinLength: 8, sessionTimeoutMinutes: 30, loginFailureLockoutCount: 5 }"
            :owner="owner"
            role="Admin"
            :auth="auth"
          />
          <div v-else data-other-panel />
        </KeepAlive>
      `
    });
    const wrapper = mount(host);
    mountedWrappers.push(wrapper);
    await flushPromises();

    await wrapper.get('input[autocomplete="current-password"]').setValue('old-secret');
    await wrapper.get('input[autocomplete="new-password"]').setValue('new-secret');
    await wrapper.get('form.settings-users__create input[type="password"]').setValue('create-secret');
    await wrapper.findAll('.settings-users__row-actions button')[1]!.trigger('click');
    await nextTick();
    const resetInput = document.body.querySelector<HTMLInputElement>(
      '[data-design-primitive="modal"] input[type="password"]'
    );
    expect(resetInput).not.toBeNull();
    resetInput!.value = 'reset-secret';
    resetInput!.dispatchEvent(new Event('input', { bubbles: true }));
    await nextTick();

    active.value = 'other';
    await nextTick();
    expect(document.body.querySelector('[data-design-primitive="modal"]')).toBeNull();
    active.value = 'security';
    await nextTick();

    expect((wrapper.get('input[autocomplete="current-password"]').element as HTMLInputElement).value).toBe('');
    expect((wrapper.get('input[autocomplete="new-password"]').element as HTMLInputElement).value).toBe('');
    expect((wrapper.get('form.settings-users__create input[type="password"]').element as HTMLInputElement).value)
      .toBe('');
    expect(document.body.querySelector('[data-design-primitive="modal"]')).toBeNull();
  });
});

describe('F07 G3 user mutation pending leave protection', () => {
  it.each(['create', 'update', 'delete', 'reset'] as const)(
    'reports %s as pending to the shared route leave guard',
    async operation => {
      const fixture = createOwnerFixture();
      const wrapper = mount(SettingsUsersPanel, { props: { owner: fixture.owner, canManage: true } });
      mountedWrappers.push(wrapper);
      await flushPromises();

      if (operation === 'create') {
        await wrapper.get('form.settings-users__create input[type="text"]').setValue('new-user');
        await wrapper.get('form.settings-users__create input[type="password"]').setValue('create-secret');
        await wrapper.get('form.settings-users__create').trigger('submit');
      } else if (operation === 'update') {
        await wrapper.find('.settings-users__row-actions button').trigger('click');
        await wrapper.get('form.settings-users__edit').trigger('submit');
      } else if (operation === 'delete') {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        await wrapper.find('.settings-users__row-actions .cv-button--danger').trigger('click');
      } else {
        await wrapper.findAll('.settings-users__row-actions button')[1]!.trigger('click');
        await nextTick();
        const resetInput = document.body.querySelector<HTMLInputElement>(
          '[data-design-primitive="modal"] input[type="password"]'
        );
        expect(resetInput).not.toBeNull();
        resetInput!.value = 'reset-secret';
        resetInput!.dispatchEvent(new Event('input', { bubbles: true }));
        await nextTick();
        document.body.querySelector<HTMLButtonElement>('[data-design-primitive="modal"] .cv-button--danger')?.click();
      }

      await nextTick();
      expect(fixture.leaveProtection()).toBe('settings-pending');
      fixture.pending.resolve(completed(operation === 'delete' ? undefined : operation === 'reset'
        ? { message: 'completed' }
        : user()) as SettingsWriteResult<unknown>);
      await flushPromises();
      expect(fixture.leaveProtection()).toBeNull();
    }
  );
});

describe('F07 G4 database backup pending leave protection', () => {
  it('reports database backup as pending to the shared route leave guard', async () => {
    const fixture = createOwnerFixture();
    const wrapper = mount(SettingsDatabasePanel, { props: { owner: fixture.owner, role: 'Admin' } });
    mountedWrappers.push(wrapper);
    await flushPromises();

    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await wrapper.get('button').trigger('click');
    await nextTick();

    expect(fixture.backupDatabase).toHaveBeenCalledTimes(1);
    expect(fixture.leaveProtection()).toBe('settings-pending');
    fixture.pending.resolve(completed({
      backupPath: 'C:/private/backup.zip',
      createdAtUtc: '2026-08-02T00:00:00Z',
      sizeBytes: 1,
      databaseSizeBytes: 1,
      packageFileCount: 0,
      packageBytes: 0
    }) as SettingsWriteResult<unknown>);
    await flushPromises();
    expect(fixture.leaveProtection()).toBeNull();
  });
});
