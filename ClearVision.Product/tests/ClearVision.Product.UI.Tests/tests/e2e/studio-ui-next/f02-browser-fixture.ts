import type { Page, Request, Route } from '@playwright/test';

export const f02BrowserFixture = Object.freeze({
  schemaVersion: 'f02-browser.v1',
  sourceSha: 'f6d4d98a53914bac088cd62cda261b2c08a11670',
  dataSource: 'BROWSER_FIXTURE',
  authSource: 'HARNESS_SEEDED_SESSION'
});

export interface F02MethodAuditEntry {
  readonly method: string;
  readonly path: string;
}

export async function installF02BrowserStartup(page: Page): Promise<void> {
  await page.addInitScript(metadata => {
    sessionStorage.setItem('cv_auth_token', 'f02-browser-fixture-token');
    sessionStorage.setItem('cv_current_user', 'fixture-user');
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: Object.freeze({
        schemaVersion: 1,
        uiKind: 'studio-ui',
        hostKind: 'browser-test',
        apiBaseUrl: `${window.location.origin}/api`,
        studioUiBasePath: '/studio/',
        featureFlags: Object.freeze({})
      }),
      writable: false,
      configurable: false
    });
    Object.defineProperty(window as typeof window & { __F02_BROWSER_FIXTURE__?: unknown }, '__F02_BROWSER_FIXTURE__', {
      value: Object.freeze(metadata),
      writable: false,
      configurable: false
    });
  }, f02BrowserFixture);
}

export async function fulfillF02Json(
  route: Route,
  status: number,
  body: unknown,
  scenarioSchema: string
): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    headers: {
      'x-clearvision-fixture-schema': scenarioSchema,
      'x-clearvision-fixture-source-sha': f02BrowserFixture.sourceSha,
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
