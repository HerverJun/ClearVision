import { inject, type InjectionKey } from 'vue';
import {
  createApiTransport,
  type ApiTokenProvider,
  type ApiTransport
} from '@/platform/api';
import {
  createMemoryTokenPort,
  createSessionStorageTokenPort,
  type AuthTokenPort,
  type AuthTokenStorage
} from '@/platform/auth';
import {
  createBrowserHostFake,
  createWebView2HostAdapter,
  type StudioHostAdapter
} from '@/platform/host';
import {
  readBrowserTestStudioStartupConfig,
  readDesktopStudioStartupConfig,
  type StudioStartupConfigV1,
  type StudioStartupWindow
} from '@/platform/startup';

export interface DesktopStudioRuntimeWindow extends StudioStartupWindow {
  readonly sessionStorage?: AuthTokenStorage;
}

export interface StudioPlatform {
  readonly startup: StudioStartupConfigV1;
  readonly host: StudioHostAdapter;
  readonly api: ApiTransport;
  readonly tokenPort: AuthTokenPort;
  hasToken(): boolean;
  dispose(): void;
}

export interface CreateStudioPlatformOptions {
  readonly startup: StudioStartupConfigV1;
  readonly host: StudioHostAdapter;
  readonly api: ApiTransport;
  readonly tokenPort?: AuthTokenPort;
  readonly tokenProvider?: ApiTokenProvider;
}

export class StudioPlatformConfigurationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'StudioPlatformConfigurationError';
  }
}

export const studioPlatformKey: InjectionKey<StudioPlatform> = Symbol('StudioPlatform');

function normalizeApiBaseUrl(value: string): string {
  return new URL(value).toString().replace(/\/$/, '');
}

function assertCompatiblePlatform(options: CreateStudioPlatformOptions): void {
  if (!Object.isFrozen(options.startup) || !Object.isFrozen(options.startup.featureFlags)) {
    throw new StudioPlatformConfigurationError(
      'StudioPlatform requires the frozen output of the validated startup reader.'
    );
  }

  const expectedHostKind = options.startup.hostKind === 'desktop-webview2'
    ? 'desktop-webview2'
    : 'browser-fake';

  if (options.host.kind !== expectedHostKind) {
    throw new StudioPlatformConfigurationError(
      `Startup host ${options.startup.hostKind} requires the ${expectedHostKind} adapter.`
    );
  }

  if (normalizeApiBaseUrl(options.api.apiBaseUrl) !== normalizeApiBaseUrl(options.startup.apiBaseUrl)) {
    throw new StudioPlatformConfigurationError(
      'The API transport must use the validated startup apiBaseUrl.'
    );
  }
}

export function createStudioPlatform(options: CreateStudioPlatformOptions): StudioPlatform {
  try {
    assertCompatiblePlatform(options);
  } catch (error) {
    options.host.dispose();
    throw error;
  }

  const tokenPort = options.tokenPort ?? createMemoryTokenPort(options.tokenProvider?.() ?? undefined);
  let disposed = false;

  return Object.freeze({
    startup: options.startup,
    host: options.host,
    api: options.api,
    tokenPort,
    hasToken(): boolean {
      try {
        return Boolean(tokenPort.readToken());
      } catch {
        return false;
      }
    },
    dispose(): void {
      if (disposed) {
        return;
      }

      disposed = true;
      options.host.dispose();
    }
  });
}

export function createDesktopStudioPlatform(
  runtimeWindow: DesktopStudioRuntimeWindow = window as unknown as DesktopStudioRuntimeWindow
): StudioPlatform {
  const startup = readDesktopStudioStartupConfig(runtimeWindow);
  const tokenPort = createSessionStorageTokenPort(runtimeWindow.sessionStorage);
  const host = createWebView2HostAdapter();

  try {
    const api = createApiTransport({
      apiBaseUrl: startup.apiBaseUrl,
      expectedOrigin: runtimeWindow.location.origin,
      tokenProvider: tokenPort.readToken
    });

    return createStudioPlatform({
      startup,
      host,
      api,
      tokenPort
    });
  } catch (error) {
    host.dispose();
    throw error;
  }
}

export function createBrowserTestStudioPlatform(
  runtimeWindow: DesktopStudioRuntimeWindow
): StudioPlatform {
  const startup = readBrowserTestStudioStartupConfig(
    runtimeWindow.__CLEARVISION_STARTUP__,
    { pageOrigin: runtimeWindow.location.origin }
  );
  const tokenPort = createSessionStorageTokenPort(runtimeWindow.sessionStorage);
  const host = createBrowserHostFake();

  try {
    const api = createApiTransport({
      apiBaseUrl: startup.apiBaseUrl,
      expectedOrigin: runtimeWindow.location.origin,
      tokenProvider: tokenPort.readToken
    });

    return createStudioPlatform({
      startup,
      host,
      api,
      tokenPort
    });
  } catch (error) {
    host.dispose();
    throw error;
  }
}

function isExplicitBrowserTestStartup(candidate: unknown): boolean {
  return typeof candidate === 'object' &&
    candidate !== null &&
    Reflect.get(candidate, 'hostKind') === 'browser-test';
}

export function createRuntimeStudioPlatform(
  runtimeWindow: DesktopStudioRuntimeWindow = window as unknown as DesktopStudioRuntimeWindow
): StudioPlatform {
  return isExplicitBrowserTestStartup(runtimeWindow.__CLEARVISION_STARTUP__)
    ? createBrowserTestStudioPlatform(runtimeWindow)
    : createDesktopStudioPlatform(runtimeWindow);
}

export function useStudioPlatform(): StudioPlatform {
  const platform = inject(studioPlatformKey);
  if (!platform) {
    throw new StudioPlatformConfigurationError(
      'StudioPlatform was not provided by the application composition root.'
    );
  }

  return platform;
}
