import { describe, expect, it, vi } from 'vitest';
import { createAuthLifecycleOwner, type AuthRuntimeTransitions } from '@/app/auth';
import {
  ApiNetworkError,
  ApiUnauthorizedError,
  type ApiGetOptions,
  type ApiTransport,
  type ApiUnauthorizedContext,
  type ApiUnauthorizedHandler,
  type ApiWriteOptions
} from '@/platform/api';
import { createMemoryTokenPort } from '@/platform/auth';

type Handler = (path: string, body: unknown, options?: ApiGetOptions | ApiWriteOptions) => Promise<unknown>;

function setupStatus(requiresInitialAdminSetup = false): object {
  return {
    requiresInitialAdminSetup,
    usernameMinLength: 3,
    passwordMinLength: 6,
    requiresUppercase: false,
    requiresLowercase: false,
    requiresDigit: false
  };
}

function user(role = 'Engineer'): object {
  return { userId: 'user-1', username: 'engineer', role };
}

function unauthorized(path: string): ApiUnauthorizedError {
  return new ApiUnauthorizedError({
    url: `http://localhost:5000/api/${path}`,
    status: 401,
    statusText: 'Unauthorized',
    payload: { error: 'invalid' },
    responseBody: '{}'
  });
}

function harness(options: Readonly<{
  token?: string;
  get?: Handler;
  post?: Handler;
  prepare?: boolean;
  activate?: boolean;
}> = {}) {
  let unauthorizedHandler: ApiUnauthorizedHandler | null = null;
  let generationProvider = () => 0;
  const get = vi.fn<Handler>(options.get ?? (async path => {
    if (path === 'auth/setup-status') return setupStatus(false);
    if (path === 'auth/me') return user();
    return undefined;
  }));
  const post = vi.fn<Handler>(options.post ?? (async path => {
    if (path === 'auth/login' || path === 'auth/setup-admin') return { token: 'token-new' };
    return { message: 'ok' };
  }));
  const api: ApiTransport = Object.freeze({
    apiBaseUrl: 'http://localhost:5000/api',
    setUnauthorizedHandler(handler: ApiUnauthorizedHandler | null, provider: () => number = () => 0) {
      unauthorizedHandler = handler;
      generationProvider = provider;
    },
    async get<T>(path: string, requestOptions?: ApiGetOptions): Promise<T | undefined> {
      return await get(path, undefined, requestOptions) as T | undefined;
    },
    async post<T>(path: string, body: unknown, requestOptions?: ApiWriteOptions): Promise<T | undefined> {
      return await post(path, body, requestOptions) as T | undefined;
    }
  });
  const tokenPort = createMemoryTokenPort(options.token);
  const runtime: AuthRuntimeTransitions = {
    prepareForProtectedTransition: vi.fn(async () => options.prepare ?? true),
    activateAuthenticatedSession: vi.fn(async () => options.activate ?? true),
    endAuthenticatedSession: vi.fn(),
    expireAuthenticatedSession: vi.fn(async () => undefined),
    navigateToLogin: vi.fn(async () => undefined)
  };
  const owner = createAuthLifecycleOwner({ api, tokenPort, runtime });
  return {
    owner,
    api,
    tokenPort,
    runtime,
    get,
    post,
    unauthorized: (context: Omit<ApiUnauthorizedContext, 'sessionGeneration'>) =>
      unauthorizedHandler?.({ ...context, sessionGeneration: generationProvider() })
  };
}

describe('authLifecycleOwner', () => {
  it('checks setup first and keeps ProductRuntime absent without a token', async () => {
    const h = harness();
    await h.owner.start();

    expect(h.get.mock.calls.map(call => call[0])).toEqual(['auth/setup-status']);
    expect(h.owner.projection.phase).toBe('unauthenticated');
    expect(h.runtime.activateAuthenticatedSession).not.toHaveBeenCalled();
  });

  it('performs setup-admin auto-login through auth/me and deduplicates submit', async () => {
    let resolveSetup!: (value: unknown) => void;
    const setupFlight = new Promise<unknown>(resolve => { resolveSetup = resolve; });
    const h = harness({
      get: async path => path === 'auth/setup-status' ? setupStatus(true) : user('Admin'),
      post: async path => path === 'auth/setup-admin' ? setupFlight : { message: 'ok' }
    });
    await h.owner.start();
    const first = h.owner.setupAdmin({ username: 'admin', password: 'secret1', confirmPassword: 'secret1' });
    const second = h.owner.setupAdmin({ username: 'admin', password: 'secret1', confirmPassword: 'secret1' });
    resolveSetup({ token: 'setup-token' });

    await expect(first).resolves.toBe(true);
    await expect(second).resolves.toBe(true);
    expect(h.post).toHaveBeenCalledTimes(1);
    expect(h.tokenPort.readToken()).toBe('setup-token');
    expect(h.owner.projection).toMatchObject({ phase: 'authenticated', user: { role: 'Admin' } });
    expect(h.runtime.activateAuthenticatedSession).toHaveBeenCalledTimes(1);
  });

  it('re-reads setup authority after a lost setup-admin response instead of guessing success', async () => {
    let setupChecks = 0;
    const h = harness({
      get: async path => {
        if (path === 'auth/setup-status') {
          setupChecks += 1;
          return setupStatus(setupChecks === 1);
        }
        return user('Admin');
      },
      post: async path => {
        if (path === 'auth/setup-admin') {
          throw new ApiNetworkError('http://localhost:5000/api/auth/setup-admin', new Error('lost'));
        }
        return { message: 'ok' };
      }
    });
    await h.owner.start();
    await expect(h.owner.setupAdmin({ username: 'admin', password: 'secret1', confirmPassword: 'secret1' }))
      .resolves.toBe(false);

    expect(setupChecks).toBe(2);
    expect(h.owner.projection.phase).toBe('unauthenticated');
    expect(h.owner.projection.message).toContain('初始化已完成');
    expect(h.runtime.activateAuthenticatedSession).not.toHaveBeenCalled();
  });

  it('distinguishes invalid, stale and expired sessions', async () => {
    const invalid = harness({ token: 'bad', get: async path => {
      if (path === 'auth/setup-status') return setupStatus(false);
      throw unauthorized(path);
    } });
    await invalid.owner.start();
    expect(invalid.owner.projection.phase).toBe('unauthenticated');
    expect(invalid.tokenPort.readToken()).toBeUndefined();

    const stale = harness({ token: 'maybe', get: async path => {
      if (path === 'auth/setup-status') return setupStatus(false);
      throw new ApiNetworkError(`http://localhost:5000/api/${path}`, new Error('offline'));
    } });
    await stale.owner.start();
    expect(stale.owner.projection.phase).toBe('stale');
    expect(stale.tokenPort.readToken()).toBe('maybe');

    const active = harness({ token: 'valid' });
    await active.owner.start();
    await active.unauthorized({ method: 'GET', path: 'projects', url: 'http://localhost:5000/api/projects' });
    expect(active.owner.projection.phase).toBe('expired');
    expect(active.tokenPort.readToken()).toBeUndefined();
    expect(active.runtime.expireAuthenticatedSession).toHaveBeenCalledTimes(1);
  });

  it('keeps the confirmed user on a transient refresh failure and expires on authoritative auth/me 401', async () => {
    let refreshMode: 'ok' | 'network' | 'unauthorized' = 'ok';
    const h = harness({
      token: 'valid',
      get: async path => {
        if (path === 'auth/setup-status') return setupStatus(false);
        if (refreshMode === 'network') {
          throw new ApiNetworkError('http://localhost:5000/api/auth/me', new Error('offline'));
        }
        if (refreshMode === 'unauthorized') throw unauthorized(path);
        return user();
      }
    });
    await h.owner.start();
    const generation = h.owner.projection.sessionGeneration;
    refreshMode = 'network';
    await h.owner.refreshSession();
    expect(h.owner.projection).toMatchObject({ phase: 'stale', user: { username: 'engineer' } });
    expect(h.owner.projection.sessionGeneration).toBe(generation);
    expect(h.runtime.expireAuthenticatedSession).not.toHaveBeenCalled();

    refreshMode = 'unauthorized';
    await h.owner.refreshSession();
    expect(h.owner.projection.phase).toBe('expired');
    expect(h.owner.projection.user).toBeNull();
    expect(h.runtime.expireAuthenticatedSession).toHaveBeenCalledTimes(1);
  });

  it('deduplicates a 401 burst and ignores an old generation after reauthentication', async () => {
    const h = harness({ token: 'valid' });
    await h.owner.start();
    const generation = h.owner.projection.sessionGeneration;
    const context = {
      method: 'GET' as const,
      path: 'projects',
      url: 'http://localhost:5000/api/projects',
      sessionGeneration: generation
    };

    await Promise.all([h.owner.handleUnauthorized(context), h.owner.handleUnauthorized(context)]);
    expect(h.runtime.expireAuthenticatedSession).toHaveBeenCalledTimes(1);

    await h.owner.login({ username: 'engineer', password: 'new-session' });
    await h.owner.handleUnauthorized(context);
    expect(h.runtime.expireAuthenticatedSession).toHaveBeenCalledTimes(1);
    expect(h.owner.projection.phase).toBe('authenticated');
  });

  it('does not let dispose or a late login response write token or mount runtime', async () => {
    let resolveLogin!: (value: unknown) => void;
    const flight = new Promise<unknown>(resolve => { resolveLogin = resolve; });
    const h = harness({ post: async path => path === 'auth/login' ? flight : { message: 'ok' } });
    await h.owner.start();
    const pending = h.owner.login({ username: 'engineer', password: 'secret' });
    h.owner.dispose();
    resolveLogin({ token: 'late-token' });

    await expect(pending).resolves.toBe(false);
    expect(h.tokenPort.readToken()).toBeUndefined();
    expect(h.runtime.activateAuthenticatedSession).not.toHaveBeenCalled();
    expect(h.owner.projection.phase).toBe('disposed');
  });

  it('blocks logout and password change when leave protection cannot settle', async () => {
    const h = harness({ token: 'valid', prepare: false });
    await h.owner.start();

    await expect(h.owner.logout()).resolves.toBe(false);
    expect(h.owner.projection.errorCode).toBe('LOGOUT_BLOCKED');
    await expect(h.owner.changePassword({ oldPassword: 'old', newPassword: 'new-password' })).resolves.toBe(false);
    expect(h.owner.projection.errorCode).toBe('CHANGE_PASSWORD_BLOCKED');
    expect(h.post).not.toHaveBeenCalled();
    expect(h.tokenPort.readToken()).toBe('valid');
  });

  it('invalidates local runtime only after successful change-password or logout response', async () => {
    const changed = harness({ token: 'valid' });
    await changed.owner.start();
    await expect(changed.owner.changePassword({ oldPassword: 'old', newPassword: 'new-password' })).resolves.toBe(true);
    expect(changed.runtime.endAuthenticatedSession).toHaveBeenCalledWith('change-password');
    expect(changed.tokenPort.readToken()).toBeUndefined();

    const unknown = harness({
      token: 'valid',
      post: async path => {
        if (path === 'auth/logout') throw new ApiNetworkError('http://localhost:5000/api/auth/logout', new Error('lost'));
        return { token: 'token-new' };
      }
    });
    await unknown.owner.start();
    await expect(unknown.owner.logout()).resolves.toBe(false);
    expect(unknown.runtime.endAuthenticatedSession).not.toHaveBeenCalled();
    expect(unknown.tokenPort.readToken()).toBe('valid');
    expect(unknown.owner.projection.phase).toBe('authenticated');
  });
});
