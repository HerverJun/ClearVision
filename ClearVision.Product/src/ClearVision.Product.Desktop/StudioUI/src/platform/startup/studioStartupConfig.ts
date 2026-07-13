export interface StudioStartupConfigV1 {
  readonly schemaVersion: 1;
  readonly uiKind: 'studio-ui';
  readonly hostKind: 'desktop-webview2' | 'browser-test';
  readonly apiBaseUrl: string;
  readonly studioUiBasePath: '/studio/';
  readonly featureFlags: Readonly<Record<string, boolean>>;
}

export interface StudioStartupWindow {
  readonly location: {
    readonly origin: string;
  };
  readonly __CLEARVISION_STARTUP__?: unknown;
}

export interface StudioStartupValidationEnvironment {
  readonly pageOrigin: string;
}

export type StudioStartupConfigErrorCode =
  | 'missing-desktop-startup'
  | 'missing-browser-test-fixture'
  | 'invalid-startup-object'
  | 'missing-startup-field'
  | 'unexpected-startup-field'
  | 'invalid-schema-version'
  | 'invalid-ui-kind'
  | 'invalid-host-kind'
  | 'host-kind-mismatch'
  | 'invalid-api-base-url'
  | 'api-base-url-not-loopback'
  | 'api-base-url-origin-mismatch'
  | 'invalid-page-origin'
  | 'invalid-studio-ui-base-path'
  | 'invalid-feature-flags';

export class StudioStartupConfigError extends Error {
  readonly code: StudioStartupConfigErrorCode;

  constructor(code: StudioStartupConfigErrorCode, message: string) {
    super(message);
    this.name = 'StudioStartupConfigError';
    this.code = code;
  }
}

const startupFieldNames = new Set<string>([
  'schemaVersion',
  'uiKind',
  'hostKind',
  'apiBaseUrl',
  'studioUiBasePath',
  'featureFlags'
]);

type StudioHostKind = StudioStartupConfigV1['hostKind'];
type UnknownRecord = Readonly<Record<string, unknown>>;
interface UntrustedStartupEnvironment {
  readonly pageOrigin: unknown;
}

function fail(code: StudioStartupConfigErrorCode, message: string): never {
  throw new StudioStartupConfigError(code, message);
}

function currentStudioWindow(): StudioStartupWindow {
  if (typeof window === 'undefined') {
    return fail(
      'missing-desktop-startup',
      'StudioUI requires a browser window containing Desktop startup configuration.'
    );
  }

  return window as unknown as StudioStartupWindow;
}

function currentPageEnvironment(): StudioStartupValidationEnvironment {
  const runtimeWindow = currentStudioWindow();
  try {
    return { pageOrigin: runtimeWindow.location.origin };
  } catch {
    return fail('invalid-page-origin', 'StudioUI page origin could not be read safely.');
  }
}

function isPlainRecord(value: unknown): value is UnknownRecord {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false;
  }

  const prototype = Object.getPrototypeOf(value);
  if (prototype === null) {
    return true;
  }

  const constructorDescriptor = Object.getOwnPropertyDescriptor(prototype, 'constructor');
  return Object.getPrototypeOf(prototype) === null
    && typeof constructorDescriptor?.value === 'function'
    && constructorDescriptor.value.name === 'Object';
}

function assertExactStartupFields(startup: UnknownRecord): void {
  for (const key of Reflect.ownKeys(startup)) {
    if (typeof key !== 'string' || !startupFieldNames.has(key)) {
      const fieldName = typeof key === 'string' ? key : key.toString();
      fail(
        'unexpected-startup-field',
        `StudioUI startup contains the unsupported field "${fieldName}".`
      );
    }
  }

  for (const fieldName of startupFieldNames) {
    if (!Object.prototype.hasOwnProperty.call(startup, fieldName)) {
      fail(
        'missing-startup-field',
        `StudioUI startup is missing the required field "${fieldName}".`
      );
    }
  }
}

function parsePageOrigin(pageOrigin: unknown): string {
  if (typeof pageOrigin !== 'string' || pageOrigin.trim() !== pageOrigin || pageOrigin.length === 0) {
    return fail('invalid-page-origin', 'StudioUI page origin must be a non-empty absolute URL.');
  }

  try {
    const parsedOrigin = new URL(pageOrigin);
    if (parsedOrigin.protocol !== 'http:' && parsedOrigin.protocol !== 'https:') {
      return fail('invalid-page-origin', 'StudioUI page origin must use HTTP or HTTPS.');
    }

    return parsedOrigin.origin;
  } catch {
    return fail('invalid-page-origin', 'StudioUI page origin must be a valid absolute URL.');
  }
}

function isLoopbackHostname(hostname: string): boolean {
  const normalizedHostname = hostname.toLowerCase();
  if (normalizedHostname === 'localhost' || normalizedHostname === '[::1]') {
    return true;
  }

  const ipv4Parts = normalizedHostname.split('.');
  return ipv4Parts.length === 4
    && ipv4Parts[0] === '127'
    && ipv4Parts.every(part => /^\d{1,3}$/.test(part) && Number(part) <= 255);
}

function parseApiBaseUrl(value: unknown, pageOrigin: string): string {
  if (typeof value !== 'string' || value.trim() !== value || value.length === 0) {
    return fail('invalid-api-base-url', 'StudioUI apiBaseUrl must be a non-empty absolute URL.');
  }

  let apiUrl: URL;
  try {
    apiUrl = new URL(value);
  } catch {
    return fail('invalid-api-base-url', 'StudioUI apiBaseUrl must be a valid absolute URL.');
  }

  if (apiUrl.protocol !== 'http:' && apiUrl.protocol !== 'https:') {
    return fail('invalid-api-base-url', 'StudioUI apiBaseUrl must use HTTP or HTTPS.');
  }

  if (!isLoopbackHostname(apiUrl.hostname)) {
    return fail(
      'api-base-url-not-loopback',
      'StudioUI apiBaseUrl must target localhost or a loopback address.'
    );
  }

  if (apiUrl.origin !== pageOrigin) {
    return fail(
      'api-base-url-origin-mismatch',
      'StudioUI apiBaseUrl must have the same origin as the current page.'
    );
  }

  return value;
}

function parseFeatureFlags(value: unknown): Readonly<Record<string, boolean>> {
  if (!isPlainRecord(value)) {
    return fail('invalid-feature-flags', 'StudioUI featureFlags must be a boolean record.');
  }

  const entries: Array<readonly [string, boolean]> = [];
  for (const key of Reflect.ownKeys(value)) {
    if (typeof key !== 'string') {
      return fail('invalid-feature-flags', 'Every StudioUI feature flag value must be boolean.');
    }

    const flagValue = value[key];
    if (typeof flagValue !== 'boolean') {
      return fail('invalid-feature-flags', 'Every StudioUI feature flag value must be boolean.');
    }

    entries.push([key, flagValue]);
  }

  return Object.freeze(Object.fromEntries(entries));
}

function validateStudioStartupConfig(
  candidate: unknown,
  expectedHostKind: StudioHostKind,
  environment: UntrustedStartupEnvironment
): StudioStartupConfigV1 {
  try {
    if (!isPlainRecord(candidate)) {
      return fail('invalid-startup-object', 'StudioUI startup configuration must be an object.');
    }

    assertExactStartupFields(candidate);

    if (candidate.schemaVersion !== 1) {
      return fail('invalid-schema-version', 'StudioUI startup schemaVersion must be 1.');
    }

    if (candidate.uiKind !== 'studio-ui') {
      return fail('invalid-ui-kind', 'StudioUI startup uiKind must be "studio-ui".');
    }

    if (candidate.hostKind !== 'desktop-webview2' && candidate.hostKind !== 'browser-test') {
      return fail(
        'invalid-host-kind',
        'StudioUI startup hostKind must be "desktop-webview2" or "browser-test".'
      );
    }

    if (candidate.hostKind !== expectedHostKind) {
      return fail(
        'host-kind-mismatch',
        `StudioUI ${expectedHostKind} reader cannot consume a ${candidate.hostKind} startup payload.`
      );
    }

    if (candidate.studioUiBasePath !== '/studio/') {
      return fail(
        'invalid-studio-ui-base-path',
        'StudioUI startup studioUiBasePath must be "/studio/".'
      );
    }

    const pageOrigin = parsePageOrigin(environment.pageOrigin);
    const apiBaseUrl = parseApiBaseUrl(candidate.apiBaseUrl, pageOrigin);
    const featureFlags = parseFeatureFlags(candidate.featureFlags);

    return Object.freeze({
      schemaVersion: 1,
      uiKind: 'studio-ui',
      hostKind: candidate.hostKind,
      apiBaseUrl,
      studioUiBasePath: '/studio/',
      featureFlags
    });
  } catch (error) {
    if (error instanceof StudioStartupConfigError) {
      throw error;
    }

    return fail('invalid-startup-object', 'StudioUI startup configuration could not be read safely.');
  }
}

export function readDesktopStudioStartupConfig(
  runtimeWindow: StudioStartupWindow = currentStudioWindow()
): StudioStartupConfigV1 {
  let candidate: unknown;
  let pageOrigin: unknown;

  try {
    candidate = runtimeWindow.__CLEARVISION_STARTUP__;
    pageOrigin = runtimeWindow.location.origin;
  } catch {
    return fail('invalid-startup-object', 'Desktop startup configuration could not be read safely.');
  }

  if (candidate === undefined) {
    return fail(
      'missing-desktop-startup',
      'Desktop did not inject window.__CLEARVISION_STARTUP__ for StudioUI.'
    );
  }

  return validateStudioStartupConfig(candidate, 'desktop-webview2', { pageOrigin });
}

export function readBrowserTestStudioStartupConfig(
  fixture: unknown,
  environment?: StudioStartupValidationEnvironment
): StudioStartupConfigV1 {
  if (fixture === undefined) {
    return fail(
      'missing-browser-test-fixture',
      'Browser tests must provide a StudioUI startup fixture explicitly.'
    );
  }

  return validateStudioStartupConfig(
    fixture,
    'browser-test',
    environment ?? currentPageEnvironment()
  );
}
