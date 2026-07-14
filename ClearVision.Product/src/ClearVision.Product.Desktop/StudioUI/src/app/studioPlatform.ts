import { inject, type InjectionKey } from 'vue';
import {
  createApiTransport,
  type ApiTokenProvider,
  type ApiTransport
} from '@/platform/api';
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

const authTokenStorageKey = 'cv_auth_token';

export interface DesktopStudioRuntimeWindow extends StudioStartupWindow {
  readonly sessionStorage?: Pick<Storage, 'getItem'>;
}

export interface StudioPlatform {
  readonly startup: StudioStartupConfigV1;
  readonly host: StudioHostAdapter;
  readonly api: ApiTransport;
  hasToken(): boolean;
  dispose(): void;
}

export interface CreateStudioPlatformOptions {
  readonly startup: StudioStartupConfigV1;
  readonly host: StudioHostAdapter;
  readonly api: ApiTransport;
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

function createSessionTokenProvider(runtimeWindow: DesktopStudioRuntimeWindow): ApiTokenProvider {
  return () => {
    try {
      return runtimeWindow.sessionStorage?.getItem(authTokenStorageKey) ?? undefined;
    } catch {
      return undefined;
    }
  };
}

export function createStudioPlatform(options: CreateStudioPlatformOptions): StudioPlatform {
  try {
    assertCompatiblePlatform(options);
  } catch (error) {
    options.host.dispose();
    throw error;
  }

  const tokenProvider = options.tokenProvider ?? (() => undefined);
  let disposed = false;

  return Object.freeze({
    startup: options.startup,
    host: options.host,
    api: options.api,
    hasToken(): boolean {
      try {
        return Boolean(tokenProvider()?.trim());
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
  const tokenProvider = createSessionTokenProvider(runtimeWindow);
  const host = createWebView2HostAdapter();

  try {
    const api = createApiTransport({
      apiBaseUrl: startup.apiBaseUrl,
      expectedOrigin: runtimeWindow.location.origin,
      tokenProvider
    });

    return createStudioPlatform({
      startup,
      host,
      api,
      tokenProvider
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
  const tokenProvider = createSessionTokenProvider(runtimeWindow);
  const host = createBrowserHostFake();

  try {
    const api = createApiTransport({
      apiBaseUrl: startup.apiBaseUrl,
      expectedOrigin: runtimeWindow.location.origin,
      tokenProvider
    });

    return createStudioPlatform({
      startup,
      host,
      api,
      tokenProvider
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
