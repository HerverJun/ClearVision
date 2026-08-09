import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { isAbsolute, relative, resolve } from 'node:path';
import type { Page, Request, Route } from '@playwright/test';

export const f02BrowserFixture = Object.freeze({
  schemaVersion: 'f02-browser.v2',
  endpoint: 'response-specific-read-endpoint',
  sourceSha: '235dccfd246d5b204463f103d1652ef90a11745d',
  dataSource: 'BROWSER_FIXTURE',
  authSource: 'HARNESS_SEEDED_SESSION',
  DATA_SOURCE: 'BROWSER_FIXTURE',
  AUTH_SOURCE: 'HARNESS_SEEDED_SESSION'
});

export const f02OperatorPerformanceFixtureCount = 200;
export const f02ResultsPerformanceFixtureCount = 500;

export interface F02G3VisualScenario {
  readonly viewport: Readonly<{ width: number; height: number }>;
  readonly theme: 'light' | 'dark';
  readonly density: 'compact' | 'comfortable';
}

const f02G3Viewports = Object.freeze([
  { width: 1920, height: 1080 },
  { width: 1536, height: 864 },
  { width: 1366, height: 768 }
] as const);

export const f02G3VisualMatrix: readonly F02G3VisualScenario[] = Object.freeze(
  f02G3Viewports.flatMap(viewport =>
    (['light', 'dark'] as const).flatMap(theme =>
      (['compact', 'comfortable'] as const).map(density => Object.freeze({
        viewport,
        theme,
        density
      }))
    )
  )
);

export interface F02MethodAuditEntry {
  readonly method: string;
  readonly path: string;
}

export interface F02RuntimeErrorAudit {
  readonly consoleErrors: string[];
  readonly pageErrors: string[];
}

export interface F02VisualEvidenceOptions {
  readonly scenario: string;
  readonly viewport: Readonly<{ width: number; height: number }>;
  readonly theme: 'light' | 'dark';
  readonly density: 'compact' | 'comfortable';
  readonly requireVisualPreferenceProjection?: boolean;
  readonly requests: readonly F02MethodAuditEntry[];
  readonly runtimeErrors: F02RuntimeErrorAudit;
  readonly expectedHttpStatuses?: readonly number[];
}

const preferencesStorageKey = 'clearvision.studio-ui.preferences.v1';

function visualEvidenceRoot(): string | null {
  const configured = process.env.CV_F02_VISUAL_EVIDENCE_DIR?.trim();
  if (!configured) return null;
  const repositoryRoot = resolve(process.cwd(), '..', '..', '..');
  const allowedRoot = resolve(repositoryRoot, '.tmp', 'studio-ui-next', 'f02-1');
  const outputRoot = isAbsolute(configured) ? resolve(configured) : resolve(repositoryRoot, configured);
  const relativeOutput = relative(allowedRoot, outputRoot);
  if (relativeOutput.startsWith('..') || isAbsolute(relativeOutput)) {
    throw new Error('CV_F02_VISUAL_EVIDENCE_DIR must remain under .tmp/studio-ui-next/f02-1.');
  }
  return outputRoot;
}

function requiredCandidateSha(): string {
  const candidate = process.env.CV_F02_SOURCE_SHA?.trim() ?? '';
  if (!/^[0-9a-f]{40}$/i.test(candidate)) {
    throw new Error('CV_F02_SOURCE_SHA must contain the 40-character Final Candidate SHA.');
  }
  return candidate.toLowerCase();
}

function safeEvidenceName(value: string): string {
  const safe = value.trim().toLowerCase().replace(/[^a-z0-9_.-]+/g, '-').replace(/^-+|-+$/g, '');
  if (!safe) throw new Error('Visual evidence scenario must contain a safe filename character.');
  return safe;
}

export function hasF02VisualEvidenceTarget(): boolean {
  return Boolean(process.env.CV_F02_VISUAL_EVIDENCE_DIR?.trim());
}

export function createF02RuntimeErrorAudit(
  page: Page,
  expectedHttpStatuses: readonly number[] = []
): F02RuntimeErrorAudit {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const expectedStatuses = new Set(expectedHttpStatuses);
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const text = message.text();
    const statusMatch = text.match(/status of (\d{3})/i);
    if (statusMatch && expectedStatuses.has(Number(statusMatch[1]))) return;
    consoleErrors.push(text);
  });
  page.on('pageerror', error => pageErrors.push(error.stack ?? error.message));
  return { consoleErrors, pageErrors };
}

export async function installF02VisualPreferences(
  page: Page,
  theme: F02VisualEvidenceOptions['theme'],
  density: F02VisualEvidenceOptions['density']
): Promise<void> {
  await page.addInitScript(({ key, selectedTheme, selectedDensity }) => {
    localStorage.setItem(key, JSON.stringify({
      schemaVersion: 1,
      theme: selectedTheme,
      density: selectedDensity
    }));
  }, {
    key: preferencesStorageKey,
    selectedTheme: theme,
    selectedDensity: density
  });
}

export async function captureF02VisualEvidence(
  page: Page,
  options: F02VisualEvidenceOptions
): Promise<Readonly<{ screenshotPath: string; metadataPath: string }>> {
  const outputRoot = visualEvidenceRoot();
  if (!outputRoot) throw new Error('CV_F02_VISUAL_EVIDENCE_DIR is required for visual capture.');
  const candidateSha = requiredCandidateSha();
  const scenario = safeEvidenceName(options.scenario);
  const stem = [
    scenario,
    `${options.viewport.width}x${options.viewport.height}`,
    options.theme,
    options.density
  ].join('-');
  const screenshotPath = resolve(outputRoot, `${stem}.png`);
  const metadataPath = resolve(outputRoot, `${stem}.json`);
  await mkdir(outputRoot, { recursive: true });
  const projection = await page.evaluate(() => ({
    devicePixelRatio: window.devicePixelRatio,
    theme: document.documentElement.dataset.theme ?? null,
    density: document.documentElement.dataset.density ?? null,
    viewport: { width: window.innerWidth, height: window.innerHeight },
    horizontalOverflow: Math.max(
      document.documentElement.scrollWidth - document.documentElement.clientWidth,
      document.body.scrollWidth - document.body.clientWidth
    )
  }));
  const preferenceProjectionRequired = options.requireVisualPreferenceProjection ?? true;
  if (preferenceProjectionRequired && (projection.theme !== options.theme || projection.density !== options.density)) {
    throw new Error(`Visual preference projection drifted: ${JSON.stringify(projection)}.`);
  }
  if (projection.viewport.width !== options.viewport.width || projection.viewport.height !== options.viewport.height) {
    throw new Error(`Visual viewport drifted: ${JSON.stringify(projection.viewport)}.`);
  }
  if (projection.horizontalOverflow > 1) {
    throw new Error(`Visual scenario has ${projection.horizontalOverflow}px global horizontal overflow.`);
  }
  if (!expectGetOnly(options.requests)) {
    throw new Error(`Visual scenario emitted non-GET requests: ${JSON.stringify(options.requests)}.`);
  }
  if (options.runtimeErrors.consoleErrors.length > 0 || options.runtimeErrors.pageErrors.length > 0) {
    throw new Error(`Visual scenario emitted runtime errors: ${JSON.stringify(options.runtimeErrors)}.`);
  }
  const screenshot = await page.screenshot({ animations: 'disabled', fullPage: false, type: 'png' });
  await writeFile(screenshotPath, screenshot);
  const metadata = {
    schemaVersion: 'f02-1-visual-evidence.v2',
    capturedAtUtc: new Date().toISOString(),
    sourceSha: candidateSha,
    finalCandidateSha: candidateSha,
    fixtureSourceSha: f02BrowserFixture.sourceSha,
    DATA_SOURCE: f02BrowserFixture.DATA_SOURCE,
    AUTH_SOURCE: f02BrowserFixture.AUTH_SOURCE,
    scenario: options.scenario,
    url: page.url(),
    viewport: options.viewport,
    observedViewport: projection.viewport,
    theme: options.theme,
    observedTheme: projection.theme,
    density: options.density,
    observedDensity: projection.density,
    preferenceProjection: {
      required: preferenceProjectionRequired,
      status: preferenceProjectionRequired ? 'APPLICABLE' : 'NOT_APPLICABLE',
      reason: preferenceProjectionRequired
        ? 'Authenticated ProductRuntime owns the UI preference projection.'
        : 'Unauthenticated auth pages do not mount ProductRuntime by design.'
    },
    dpr: {
      type: 'BROWSER_EMULATED_DPR',
      value: projection.devicePixelRatio,
      windowsDpi: 'NOT_PERFORMED'
    },
    horizontalOverflow: projection.horizontalOverflow,
    requestMethods: options.requests,
    getOnly: expectGetOnly(options.requests),
    expectedHttpStatuses: options.expectedHttpStatuses ?? [],
    runtimeErrors: options.runtimeErrors,
    consoleErrorCount: options.runtimeErrors.consoleErrors.length,
    pageErrorCount: options.runtimeErrors.pageErrors.length,
    screenshot: {
      fileName: `${stem}.png`,
      sha256: createHash('sha256').update(screenshot).digest('hex').toUpperCase(),
      bytes: screenshot.byteLength
    }
  };
  await writeFile(metadataPath, `${JSON.stringify(metadata, null, 2)}\n`, 'utf8');
  return Object.freeze({ screenshotPath, metadataPath });
}

export async function installF02BrowserStartup(
  page: Page,
  featureFlags: Readonly<Record<string, boolean>> = {}
): Promise<void> {
  await page.addInitScript(({ metadata, flags }) => {
    sessionStorage.setItem('cv_auth_token', 'f02-browser-fixture-token');
    sessionStorage.setItem('cv_current_user', 'fixture-user');
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: Object.freeze({
        schemaVersion: 1,
        uiKind: 'studio-ui',
        hostKind: 'browser-test',
        apiBaseUrl: `${window.location.origin}/api`,
        studioUiBasePath: '/studio/',
        startupProfile: 'NEXT_DEFAULT',
        profileAllowedRoles: Object.freeze(['Admin', 'Engineer', 'Operator']),
        featureFlags: Object.freeze(flags)
      }),
      writable: false,
      configurable: false
    });
    Object.defineProperty(window as typeof window & { __F02_BROWSER_FIXTURE__?: unknown }, '__F02_BROWSER_FIXTURE__', {
      value: Object.freeze(metadata),
      writable: false,
      configurable: false
    });
  }, { metadata: f02BrowserFixture, flags: featureFlags });
}

export async function fulfillF02Json(
  route: Route,
  status: number,
  body: unknown,
  scenarioSchema: string,
  sourceSha = f02BrowserFixture.sourceSha
): Promise<void> {
  const requestUrl = new URL(route.request().url());
  const endpoint = `${route.request().method()} ${requestUrl.pathname}`;
  await route.fulfill({
    status,
    contentType: 'application/json',
    headers: {
      'x-clearvision-fixture-schema': scenarioSchema,
      'x-clearvision-fixture-endpoint': endpoint,
      'x-clearvision-fixture-source-sha': sourceSha,
      'x-clearvision-data-source': f02BrowserFixture.dataSource,
      'x-clearvision-auth-source': f02BrowserFixture.authSource
    },
    body: JSON.stringify(body)
  });
}

export function auditF02Request(request: Request): F02MethodAuditEntry {
  const url = new URL(request.url());
  return Object.freeze({
    method: request.method(),
    path: `${url.pathname}${url.search}`
  });
}

export function expectGetOnly(audit: readonly F02MethodAuditEntry[]): boolean {
  return audit.every(entry => entry.method === 'GET');
}
