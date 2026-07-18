import { describe, expect, it } from 'vitest';
import {
  createRuntimeStudioPlatform,
  createStudioPlatform,
  StudioPlatformConfigurationError
} from '@/app/studioPlatform';
import type { ApiTransport } from '@/platform/api';
import { createBrowserHostFake } from '@/platform/host';
import {
  readBrowserTestStudioStartupConfig,
  readDesktopStudioStartupConfig
} from '@/platform/startup';

function createApi(apiBaseUrl: string): ApiTransport {
  return Object.freeze({
    apiBaseUrl,
    async get<T>(): Promise<T | undefined> {
      return undefined;
    }
  });
}

describe('StudioPlatform composition', () => {
  it('selects the explicit browser-test composition without a WebView2 shim', () => {
    const platform = createRuntimeStudioPlatform({
      location: { origin: 'http://127.0.0.1:5177' },
      sessionStorage: {
        getItem(key: string) {
          return key === 'cv_auth_token' ? 'browser-token' : null;
        },
        setItem() {},
        removeItem() {}
      },
      __CLEARVISION_STARTUP__: {
        schemaVersion: 1,
        uiKind: 'studio-ui',
        hostKind: 'browser-test',
        apiBaseUrl: 'http://127.0.0.1:5177/api',
        studioUiBasePath: '/studio/',
        featureFlags: {}
      }
    });

    expect(platform.startup.hostKind).toBe('browser-test');
    expect(platform.host.kind).toBe('browser-fake');
    expect(platform.hasToken()).toBe(true);

    platform.dispose();
    expect(platform.host.getDiagnostics().disposed).toBe(true);
  });

  it('owns the explicit browser-test Host/API pair and disposes it idempotently', () => {
    const startup = readBrowserTestStudioStartupConfig({
      schemaVersion: 1,
      uiKind: 'studio-ui',
      hostKind: 'browser-test',
      apiBaseUrl: 'http://localhost:5000/api',
      studioUiBasePath: '/studio/',
      featureFlags: {}
    }, { pageOrigin: 'http://localhost:5000' });
    const host = createBrowserHostFake();
    const platform = createStudioPlatform({
      startup,
      host,
      api: createApi(startup.apiBaseUrl),
      tokenProvider: () => 'token-1'
    });

    expect(platform.hasToken()).toBe(true);
    expect(platform.host.kind).toBe('browser-fake');

    platform.dispose();
    platform.dispose();
    expect(host.getDiagnostics().disposed).toBe(true);
  });

  it('rejects an adapter that does not match the validated startup host', () => {
    const startup = readDesktopStudioStartupConfig({
      location: { origin: 'http://localhost:5000' },
      __CLEARVISION_STARTUP__: {
        schemaVersion: 1,
        uiKind: 'studio-ui',
        hostKind: 'desktop-webview2',
        apiBaseUrl: 'http://localhost:5000/api',
        studioUiBasePath: '/studio/',
        featureFlags: {}
      }
    });
    const host = createBrowserHostFake();

    expect(() => createStudioPlatform({
      startup,
      host,
      api: createApi(startup.apiBaseUrl)
    })).toThrow(StudioPlatformConfigurationError);
    expect(host.getDiagnostics().disposed).toBe(true);
  });

  it('rejects an API transport that diverges from the startup base URL', () => {
    const startup = readBrowserTestStudioStartupConfig({
      schemaVersion: 1,
      uiKind: 'studio-ui',
      hostKind: 'browser-test',
      apiBaseUrl: 'http://localhost:5000/api',
      studioUiBasePath: '/studio/',
      featureFlags: {}
    }, { pageOrigin: 'http://localhost:5000' });
    const host = createBrowserHostFake();

    expect(() => createStudioPlatform({
      startup,
      host,
      api: createApi('http://localhost:5001/api')
    })).toThrow('validated startup apiBaseUrl');
    expect(host.getDiagnostics().disposed).toBe(true);
  });
});
