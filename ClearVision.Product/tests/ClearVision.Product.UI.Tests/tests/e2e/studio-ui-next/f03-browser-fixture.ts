import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { isAbsolute, relative, resolve } from 'node:path';
import type { Page, Request, Route } from '@playwright/test';

export const f03BrowserFixture = Object.freeze({
  schemaVersion: 'f03-g1-browser.v1',
  phase: 'f03',
  dataSource: 'BROWSER_FIXTURE',
  authSource: 'HARNESS_SEEDED_SESSION',
  DATA_SOURCE: 'BROWSER_FIXTURE',
  AUTH_SOURCE: 'HARNESS_SEEDED_SESSION'
});

export interface F03RequestAuditEntry {
  readonly method: string;
  readonly path: string;
}

export interface F03RuntimeErrorAudit {
  readonly consoleErrors: string[];
  readonly pageErrors: string[];
}

export interface F03WorkspaceEvidenceOptions {
  readonly scenario: string;
  readonly viewport: Readonly<{ width: number; height: number }>;
  readonly requests: readonly F03RequestAuditEntry[];
  readonly runtimeErrors: F03RuntimeErrorAudit;
}

const preferencesStorageKey = 'clearvision.studio-ui.preferences.v1';

function visualEvidenceRoot(): string | null {
  const configured = process.env.CV_F03_VISUAL_EVIDENCE_DIR?.trim();
  if (!configured) return null;
  const repositoryRoot = resolve(process.cwd(), '..', '..', '..');
  const allowedRoot = resolve(repositoryRoot, '.tmp', 'studio-ui-next', 'f03');
  const outputRoot = isAbsolute(configured) ? resolve(configured) : resolve(repositoryRoot, configured);
  const relativeOutput = relative(allowedRoot, outputRoot);
  if (relativeOutput.startsWith('..') || isAbsolute(relativeOutput)) {
    throw new Error('CV_F03_VISUAL_EVIDENCE_DIR must remain under .tmp/studio-ui-next/f03.');
  }
  return outputRoot;
}

function requiredSha(name: 'CV_F03_SOURCE_SHA' | 'CV_F03_STABLE_AUDIT_SHA'): string {
  const value = process.env[name]?.trim() ?? '';
  if (!/^[0-9a-f]{40}$/i.test(value)) {
    throw new Error(`${name} must contain a 40-character SHA.`);
  }
  return value.toLowerCase();
}

function safeEvidenceName(value: string): string {
  const safe = value.trim().toLowerCase().replace(/[^a-z0-9_.-]+/g, '-').replace(/^-+|-+$/g, '');
  if (!safe) throw new Error('F03 evidence scenario requires a safe filename.');
  return safe;
}

export function auditF03Request(request: Request): F03RequestAuditEntry {
  const url = new URL(request.url());
  return Object.freeze({
    method: request.method(),
    path: `${url.pathname}${url.search}`
  });
}

export function isF03G1RequestAllowlist(
  audit: readonly F03RequestAuditEntry[]
): boolean {
  return audit.every(entry => {
    if (entry.method !== 'GET') return false;
    return entry.path === '/api/auth/me' ||
      /^\/api\/projects\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
        .test(entry.path);
  });
}

export function createF03RuntimeErrorAudit(page: Page): F03RuntimeErrorAudit {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => pageErrors.push(error.stack ?? error.message));
  return { consoleErrors, pageErrors };
}

export async function installF03BrowserStartup(
  page: Page,
  workspaceEnabled: boolean
): Promise<void> {
  await page.addInitScript(({ metadata, enabled, preferencesKey }) => {
    sessionStorage.setItem('cv_auth_token', 'f03-browser-fixture-token');
    sessionStorage.setItem('cv_current_user', 'f03-workspace-user');
    localStorage.setItem(preferencesKey, JSON.stringify({
      schemaVersion: 1,
      theme: 'light',
      density: 'compact'
    }));
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: Object.freeze({
        schemaVersion: 1,
        uiKind: 'studio-ui',
        hostKind: 'browser-test',
        apiBaseUrl: `${window.location.origin}/api`,
        studioUiBasePath: '/studio/',
        featureFlags: Object.freeze({ 'Studio2.Workspace': enabled })
      }),
      writable: false,
      configurable: false
    });
    Object.defineProperty(
      window as typeof window & { __F03_BROWSER_FIXTURE__?: unknown },
      '__F03_BROWSER_FIXTURE__',
      { value: Object.freeze(metadata), writable: false, configurable: false }
    );
  }, {
    metadata: f03BrowserFixture,
    enabled: workspaceEnabled,
    preferencesKey: preferencesStorageKey
  });
}

export async function fulfillF03Json(
  route: Route,
  status: number,
  body: unknown,
  scenarioSchema: string
): Promise<void> {
  const request = route.request();
  const url = new URL(request.url());
  await route.fulfill({
    status,
    contentType: 'application/json',
    headers: {
      'x-clearvision-fixture-schema': scenarioSchema,
      'x-clearvision-fixture-endpoint': `${request.method()} ${url.pathname}`,
      'x-clearvision-data-source': f03BrowserFixture.dataSource,
      'x-clearvision-auth-source': f03BrowserFixture.authSource
    },
    body: JSON.stringify(body)
  });
}

export function hasF03VisualEvidenceTarget(): boolean {
  return visualEvidenceRoot() !== null;
}

export async function captureF03WorkspaceEvidence(
  page: Page,
  options: F03WorkspaceEvidenceOptions
): Promise<Readonly<{ screenshotPath: string; metadataPath: string }>> {
  const outputRoot = visualEvidenceRoot();
  if (!outputRoot) throw new Error('CV_F03_VISUAL_EVIDENCE_DIR is required for visual capture.');
  const sourceSha = requiredSha('CV_F03_SOURCE_SHA');
  const stableAuditSha = requiredSha('CV_F03_STABLE_AUDIT_SHA');
  const stem = `${safeEvidenceName(options.scenario)}-${options.viewport.width}x${options.viewport.height}`;
  const screenshotPath = resolve(outputRoot, `${stem}.png`);
  const metadataPath = resolve(outputRoot, `${stem}.json`);
  await mkdir(outputRoot, { recursive: true });
  const projection = await page.evaluate(() => ({
    viewport: { width: window.innerWidth, height: window.innerHeight },
    dpr: window.devicePixelRatio,
    theme: document.documentElement.dataset.theme ?? null,
    density: document.documentElement.dataset.density ?? null,
    horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    verticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight,
    state: document.querySelector('[data-evidence-surface="f03-workspace-shell"]')
      ?.getAttribute('data-workspace-state') ?? null,
    diagnostics: (window as typeof window & {
      __STUDIO_UI_WORKSPACE_DIAGNOSTICS__?: Record<string, unknown>;
    }).__STUDIO_UI_WORKSPACE_DIAGNOSTICS__
      ? { ...(window as typeof window & {
          __STUDIO_UI_WORKSPACE_DIAGNOSTICS__: Record<string, unknown>;
        }).__STUDIO_UI_WORKSPACE_DIAGNOSTICS__ }
      : null
  }));
  if (projection.viewport.width !== options.viewport.width || projection.viewport.height !== options.viewport.height) {
    throw new Error(`F03 evidence viewport drifted: ${JSON.stringify(projection.viewport)}.`);
  }
  if (projection.horizontalOverflow > 1 || projection.verticalOverflow > 1) {
    throw new Error(`F03 Workspace overflowed globally: ${JSON.stringify(projection)}.`);
  }
  if (!isF03G1RequestAllowlist(options.requests)) {
    throw new Error(`F03 G1 request allowlist failed: ${JSON.stringify(options.requests)}.`);
  }
  if (options.runtimeErrors.consoleErrors.length || options.runtimeErrors.pageErrors.length) {
    throw new Error(`F03 Workspace emitted runtime errors: ${JSON.stringify(options.runtimeErrors)}.`);
  }
  const screenshot = await page.screenshot({ animations: 'disabled', fullPage: false, type: 'png' });
  await writeFile(screenshotPath, screenshot);
  await writeFile(metadataPath, `${JSON.stringify({
    schemaVersion: 'f03-g1-browser-evidence.v1',
    capturedAtUtc: new Date().toISOString(),
    sourceSha,
    stableAuditSha,
    EvidencePhase: 'f03',
    DATA_SOURCE: f03BrowserFixture.DATA_SOURCE,
    AUTH_SOURCE: f03BrowserFixture.AUTH_SOURCE,
    route: page.url(),
    scenario: options.scenario,
    viewport: options.viewport,
    projection,
    expectedMethods: ['GET'],
    observedRequests: options.requests,
    requestAllowlistPassed: true,
    runtimeErrors: options.runtimeErrors,
    dpr: { type: 'BROWSER_EMULATED_DPR', value: projection.dpr },
    nativeDpi: 'NOT_PERFORMED',
    screenshot: {
      fileName: `${stem}.png`,
      bytes: screenshot.byteLength,
      sha256: createHash('sha256').update(screenshot).digest('hex').toUpperCase()
    }
  }, null, 2)}\n`, 'utf8');
  return Object.freeze({ screenshotPath, metadataPath });
}
