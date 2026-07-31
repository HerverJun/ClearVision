import { flushPromises, mount } from '@vue/test-utils';
import { nextTick, reactive } from 'vue';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiUnauthorizedError, type ApiGetOptions, type ApiTransport } from '@/platform/api';
import {
  getSettingsOwnerActiveCount,
  SettingsPage,
  type SettingsPageRuntime
} from '@/capabilities/settings';

const mountedWrappers: Array<{ unmount: () => void }> = [];

afterEach(() => {
  while (mountedWrappers.length > 0) mountedWrappers.pop()?.unmount();
});

function mountSettingsPage(runtime: SettingsPageRuntime) {
  const wrapper = mount(SettingsPage, { props: { runtime } });
  mountedWrappers.push(wrapper);
  return wrapper;
}

function settingsPayload(safeSubset = false): Record<string, unknown> {
  return safeSubset
    ? {
        safeSubset: true,
        revision: 4,
        general: { softwareTitle: 'ClearVision', theme: 'dark' }
      }
    : {
        revision: 4,
        general: { softwareTitle: 'ClearVision', theme: 'dark', autoStart: false },
        storage: { imageSavePath: 'D:/VisionData', savePolicy: 'NgOnly', retentionDays: 30, minFreeSpaceGb: 5 },
        runtime: {
          autoRun: false,
          stopOnConsecutiveNg: 3,
          missingMaterialTimeoutSeconds: 120,
          applyProtectionRules: true,
          runtimePreviewPilot: { mode: 'metadata_only' }
        },
        security: { passwordMinLength: 8, sessionTimeoutMinutes: 30, loginFailureLockoutCount: 5 },
        communication: {},
        tcpCommunication: {},
        features: {},
        cameras: [],
        activeCameraId: ''
      };
}

function apiWith(
  implementation: (path: string, options?: ApiGetOptions) => Promise<unknown>
): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await implementation(path, options) as T | undefined;
    }
  };
}

function createRuntime(
  implementation: (path: string, options?: ApiGetOptions) => Promise<unknown>,
  role: string
): { readonly runtime: SettingsPageRuntime; readonly session: { phase: string; user: { role: string } } } {
  const session = reactive({
    phase: 'authenticated',
    user: { userId: 'user-1', username: 'settings-user', role },
    sessionGeneration: 1,
    message: '会话有效',
    updatedAt: Date.now()
  });
  const runtime = {
    api: apiWith(implementation),
    session: { projection: session }
  } as unknown as SettingsPageRuntime;
  return { runtime, session };
}

function unauthorizedError(): ApiUnauthorizedError {
  return new ApiUnauthorizedError({
    url: 'http://localhost:5000/api/settings',
    status: 401,
    statusText: 'Unauthorized',
    payload: { code: 'AuthenticatedSessionRequired', token: 'raw-token' },
    responseBody: '{"code":"AuthenticatedSessionRequired"}'
  });
}

describe('F07 G2 Settings shell', () => {
  it('renders an Admin full projection through ProductRuntime.api and exposes group navigation', async () => {
    const requestedPaths: string[] = [];
    const { runtime } = createRuntime(async path => {
      requestedPaths.push(path);
      return settingsPayload();
    }, 'Admin');
    const wrapper = mountSettingsPage(runtime);
    await flushPromises();

    expect(requestedPaths).toEqual(['settings']);
    expect(wrapper.attributes('data-settings-phase')).toBe('ready');
    expect(wrapper.text()).toContain('完整投影');
    expect(wrapper.text()).toContain('ClearVision');
    expect(wrapper.find('[data-settings-navigation]').exists()).toBe(true);
    await wrapper.get('[data-settings-group="storage"]').trigger('click');
    expect(wrapper.text()).toContain('D:/VisionData');
    expect(wrapper.text()).not.toContain('保存设置');

    wrapper.unmount();
    expect(getSettingsOwnerActiveCount()).toBe(0);
  });

  it('renders Engineer safe subset without inventing restricted section data', async () => {
    const { runtime } = createRuntime(async () => settingsPayload(true), 'Engineer');
    const wrapper = mountSettingsPage(runtime);
    await flushPromises();

    expect(wrapper.attributes('data-settings-safe-subset')).toBe('true');
    expect(wrapper.text()).toContain('safe subset');
    expect(wrapper.text()).toContain('安全子集未返回');
    expect(wrapper.text()).not.toContain('D:/VisionData');

    wrapper.unmount();
  });

  it('fails closed for Operator without issuing a Settings request', async () => {
    const get = vi.fn(async () => settingsPayload());
    const { runtime } = createRuntime(get, 'Operator');
    const wrapper = mountSettingsPage(runtime);
    await flushPromises();

    expect(get).not.toHaveBeenCalled();
    expect(wrapper.find('[data-page-state="forbidden"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('无权访问设置');

    wrapper.unmount();
  });

  it('projects 401 as an unauthenticated state without exposing raw payload data', async () => {
    const { runtime } = createRuntime(async () => { throw unauthorizedError(); }, 'Engineer');
    const wrapper = mountSettingsPage(runtime);
    await flushPromises();

    expect(wrapper.find('[data-page-state="unauthorized"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('会话不可用');
    expect(wrapper.text()).not.toContain('raw-token');

    wrapper.unmount();
  });

  it('renders decoder failure as a safe error state', async () => {
    const { runtime } = createRuntime(async () => ({
      revision: 4,
      general: { softwareTitle: 'ClearVision', theme: 'dark', autoStart: false },
      unknownField: 'must-not-render'
    }), 'Admin');
    const wrapper = mountSettingsPage(runtime);
    await flushPromises();

    expect(wrapper.find('[data-page-state="error"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('Settings 投影无法解析');
    expect(wrapper.text()).not.toContain('must-not-render');

    wrapper.unmount();
  });

  it('aborts the old owner on permission change and disposes the active owner on leave', async () => {
    let callCount = 0;
    let firstSignal: AbortSignal | undefined;
    let resolveFirst!: (value: unknown) => void;
    const firstResponse = new Promise<unknown>(resolve => { resolveFirst = resolve; });
    const { runtime, session } = createRuntime(async (_path, options) => {
      callCount += 1;
      if (callCount === 1) {
        firstSignal = options?.signal;
        return firstResponse;
      }
      return settingsPayload(true);
    }, 'Engineer');
    const wrapper = mountSettingsPage(runtime);
    await Promise.resolve();
    expect(getSettingsOwnerActiveCount()).toBe(1);

    session.user.role = 'Operator';
    await nextTick();
    await flushPromises();
    expect(firstSignal?.aborted).toBe(true);
    expect(wrapper.find('[data-page-state="forbidden"]').exists()).toBe(true);
    expect(callCount).toBe(1);

    wrapper.unmount();
    expect(getSettingsOwnerActiveCount()).toBe(0);
    resolveFirst(settingsPayload());
    await flushPromises();
  });

  it('disposes the owner immediately when the shared session leaves authenticated state', async () => {
    let requestSignal: AbortSignal | undefined;
    let resolveRequest!: (value: unknown) => void;
    const response = new Promise<unknown>(resolve => { resolveRequest = resolve; });
    const { runtime, session } = createRuntime(async (_path, options) => {
      requestSignal = options?.signal;
      return response;
    }, 'Engineer');
    const wrapper = mountSettingsPage(runtime);
    await Promise.resolve();

    session.phase = 'unauthenticated';
    await nextTick();
    expect(requestSignal?.aborted).toBe(true);
    expect(getSettingsOwnerActiveCount()).toBe(0);

    wrapper.unmount();
    resolveRequest(settingsPayload(true));
    await flushPromises();
  });
});
