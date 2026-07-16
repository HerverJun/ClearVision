import { describe, expect, it } from 'vitest';
import {
  readBrowserTestStudioStartupConfig,
  readDesktopStudioStartupConfig,
  StudioStartupConfigError,
  type StudioStartupConfigErrorCode,
  type StudioStartupWindow
} from '@/platform/startup';

const localhostOrigin = 'http://localhost:5071';

function createStartupFixture(
  overrides: Record<string, unknown> = {}
): Record<string, unknown> {
  return {
    schemaVersion: 1,
    uiKind: 'studio-ui',
    hostKind: 'desktop-webview2',
    apiBaseUrl: `${localhostOrigin}/api`,
    studioUiBasePath: '/studio/',
    featureFlags: {
      'Studio2.Workspace': false,
      'Studio2.PropertyPanel': true,
      'Studio2.PreviewPanel': false
    },
    ...overrides
  };
}

function createDesktopWindow(
  startup: unknown = createStartupFixture(),
  origin = localhostOrigin
): StudioStartupWindow {
  return Object.freeze({
    location: Object.freeze({ origin }),
    __CLEARVISION_STARTUP__: startup
  });
}

function expectStartupError(
  action: () => unknown,
  expectedCode: StudioStartupConfigErrorCode
): void {
  try {
    action();
  } catch (error) {
    expect(error).toBeInstanceOf(StudioStartupConfigError);
    expect((error as StudioStartupConfigError).code).toBe(expectedCode);
    return;
  }

  throw new Error(`Expected StudioStartupConfigError with code ${expectedCode}.`);
}

describe('readDesktopStudioStartupConfig', () => {
  it('reads the exact Desktop V1 contract without writing to the supplied window', () => {
    const sourceFeatureFlags = {
      'Studio2.Workspace': false,
      'Studio2.PropertyPanel': true,
      'Studio2.PreviewPanel': false
    };
    const source = createStartupFixture({ featureFlags: sourceFeatureFlags });
    const runtimeWindow = createDesktopWindow(source);

    const startup = readDesktopStudioStartupConfig(runtimeWindow);

    expect(startup).toEqual(source);
    expect(startup).not.toBe(source);
    expect(startup.featureFlags).not.toBe(sourceFeatureFlags);
    expect(Object.isFrozen(startup)).toBe(true);
    expect(Object.isFrozen(startup.featureFlags)).toBe(true);
    expect(runtimeWindow.__CLEARVISION_STARTUP__).toBe(source);

    source.apiBaseUrl = 'http://localhost:9999/api';
    sourceFeatureFlags['Studio2.PropertyPanel'] = false;
    expect(startup.apiBaseUrl).toBe(`${localhostOrigin}/api`);
    expect(startup.featureFlags['Studio2.PropertyPanel']).toBe(true);
    expect(startup.featureFlags['Studio2.Workspace']).toBe(false);
  });

  it('fails fast when Desktop did not inject startup configuration', () => {
    const runtimeWindow = Object.freeze({
      location: Object.freeze({ origin: localhostOrigin })
    });

    expectStartupError(
      () => readDesktopStudioStartupConfig(runtimeWindow),
      'missing-desktop-startup'
    );
  });

  it.each([
    ['null payload', null, 'invalid-startup-object'],
    ['array payload', [], 'invalid-startup-object'],
    ['schemaVersion', createStartupFixture({ schemaVersion: 2 }), 'invalid-schema-version'],
    ['uiKind', createStartupFixture({ uiKind: 'legacy' }), 'invalid-ui-kind'],
    ['unknown hostKind', createStartupFixture({ hostKind: 'desktop' }), 'invalid-host-kind'],
    ['browser host in Desktop reader', createStartupFixture({ hostKind: 'browser-test' }), 'host-kind-mismatch'],
    ['base path', createStartupFixture({ studioUiBasePath: '/studio' }), 'invalid-studio-ui-base-path'],
    ['null featureFlags', createStartupFixture({ featureFlags: null }), 'invalid-feature-flags'],
    ['array featureFlags', createStartupFixture({ featureFlags: [] }), 'invalid-feature-flags'],
    ['non-boolean feature flag', createStartupFixture({ featureFlags: { enabled: 1 } }), 'invalid-feature-flags']
  ] as const)('rejects invalid %s', (_label, fixture, errorCode) => {
    expectStartupError(
      () => readDesktopStudioStartupConfig(createDesktopWindow(fixture)),
      errorCode
    );
  });

  it('rejects missing fields and unsupported legacy-only fields', () => {
    const missingFeatureFlags = createStartupFixture();
    delete missingFeatureFlags.featureFlags;

    expectStartupError(
      () => readDesktopStudioStartupConfig(createDesktopWindow(missingFeatureFlags)),
      'missing-startup-field'
    );
    expectStartupError(
      () => readDesktopStudioStartupConfig(createDesktopWindow(createStartupFixture({
        nodePreviewInspectorEnabled: true
      }))),
      'unexpected-startup-field'
    );
  });

  it.each([
    ['non-string', 5071, 'invalid-api-base-url'],
    ['relative URL', '/api', 'invalid-api-base-url'],
    ['malformed URL', 'not a URL', 'invalid-api-base-url'],
    ['non-HTTP URL', 'file:///api', 'invalid-api-base-url'],
    ['remote host', 'http://example.com:5071/api', 'api-base-url-not-loopback'],
    ['different port', 'http://localhost:5072/api', 'api-base-url-origin-mismatch'],
    ['different scheme', 'https://localhost:5071/api', 'api-base-url-origin-mismatch'],
    ['different loopback host', 'http://127.0.0.1:5071/api', 'api-base-url-origin-mismatch']
  ] as const)('rejects apiBaseUrl with %s', (_label, apiBaseUrl, errorCode) => {
    expectStartupError(
      () => readDesktopStudioStartupConfig(createDesktopWindow(createStartupFixture({ apiBaseUrl }))),
      errorCode
    );
  });

  it('rejects a non-HTTP page origin before comparing API origins', () => {
    expectStartupError(
      () => readDesktopStudioStartupConfig(createDesktopWindow(
        createStartupFixture(),
        'file:///studio/index.html'
      )),
      'invalid-page-origin'
    );
  });
});

describe('readBrowserTestStudioStartupConfig', () => {
  it.each([
    ['localhost', 'http://localhost:6100'],
    ['IPv4 loopback', 'https://127.23.45.67:7443'],
    ['IPv6 loopback', 'http://[::1]:6200']
  ] as const)('accepts an explicit browser fixture on %s', (_label, pageOrigin) => {
    const fixture = createStartupFixture({
      hostKind: 'browser-test',
      apiBaseUrl: `${pageOrigin}/api`
    });

    const startup = readBrowserTestStudioStartupConfig(fixture, { pageOrigin });

    expect(startup.hostKind).toBe('browser-test');
    expect(startup.apiBaseUrl).toBe(`${pageOrigin}/api`);
    expect(Object.isFrozen(startup)).toBe(true);
    expect(Object.isFrozen(startup.featureFlags)).toBe(true);
  });

  it('requires the fixture even when the browser window has injected startup data', () => {
    const originalDescriptor = Object.getOwnPropertyDescriptor(window, '__CLEARVISION_STARTUP__');
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      configurable: true,
      value: createStartupFixture({ hostKind: 'browser-test' })
    });

    try {
      const callWithoutFixture = readBrowserTestStudioStartupConfig as (
        fixture?: unknown
      ) => unknown;
      expectStartupError(callWithoutFixture, 'missing-browser-test-fixture');
    } finally {
      if (originalDescriptor) {
        Object.defineProperty(window, '__CLEARVISION_STARTUP__', originalDescriptor);
      } else {
        Reflect.deleteProperty(window, '__CLEARVISION_STARTUP__');
      }
    }
  });

  it('rejects a Desktop payload and a fixture whose explicit environment is not same-origin', () => {
    expectStartupError(
      () => readBrowserTestStudioStartupConfig(createStartupFixture(), {
        pageOrigin: localhostOrigin
      }),
      'host-kind-mismatch'
    );

    expectStartupError(
      () => readBrowserTestStudioStartupConfig(createStartupFixture({
        hostKind: 'browser-test'
      }), {
        pageOrigin: 'http://localhost:5072'
      }),
      'api-base-url-origin-mismatch'
    );
  });
});
