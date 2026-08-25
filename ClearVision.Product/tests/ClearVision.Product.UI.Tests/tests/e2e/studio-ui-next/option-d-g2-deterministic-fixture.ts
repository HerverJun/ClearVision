import type { Page, Route } from '@playwright/test';

export type OptionDG2FixtureMode = 'login' | 'forbidden';

export interface OptionDG2RequestEvidence {
  readonly method: string;
  readonly pathname: string;
  readonly authorization: string | null;
  readonly handledAs: string;
}

export interface OptionDG2FixtureAudit {
  readonly requests: OptionDG2RequestEvidence[];
}

const engineer = Object.freeze({
  userId: 'option-d-g2-engineer',
  username: 'engineer',
  role: 'Engineer'
});
const operator = Object.freeze({
  userId: 'option-d-g2-operator',
  username: 'operator',
  role: 'Operator'
});

export const optionDG2DeterministicFixture = Object.freeze({
  schemaVersion: 1,
  id: 'option-d-g2-shell-auth.v1',
  dataSource: 'OPTION_D_G2_FIXTURE',
  healthPort: 5177,
  password: 'g2-password',
  loginToken: 'option-d-g2-login-token',
  forbiddenToken: 'option-d-g2-forbidden-token',
  engineer,
  operator,
  setupStatus: Object.freeze({
    requiresInitialAdminSetup: false,
    usernameMinLength: 3,
    passwordMinLength: 6,
    requiresUppercase: false,
    requiresLowercase: false,
    requiresDigit: false
  }),
  startup: Object.freeze({
    schemaVersion: 1,
    uiKind: 'studio-ui',
    hostKind: 'browser-test',
    studioUiBasePath: '/studio/',
    startupProfile: 'NEXT_DEFAULT',
    profileAllowedRoles: Object.freeze(['Admin', 'Engineer', 'Operator']),
    featureFlags: Object.freeze({
      'Studio2.Workspace': true,
      'Studio2.Settings': true,
      'Studio2.StationsRead': true,
      'Studio2.InspectionRun': true,
      'Studio2.AiWorkbench': true
    })
  })
});

function authorizationOf(route: Route): string | null {
  return route.request().headers().authorization ?? null;
}

function fulfillJson(route: Route, status: number, payload: unknown): Promise<void> {
  return route.fulfill({
    status,
    contentType: 'application/json',
    headers: { 'x-clearvision-data-source': optionDG2DeterministicFixture.dataSource },
    body: JSON.stringify(payload)
  });
}

export async function installOptionDG2DeterministicFixture(
  page: Page,
  mode: OptionDG2FixtureMode
): Promise<OptionDG2FixtureAudit> {
  const fixture = optionDG2DeterministicFixture;
  const requests: OptionDG2RequestEvidence[] = [];

  function audit(route: Route, handledAs: string): void {
    const url = new URL(route.request().url());
    requests.push(Object.freeze({
      method: route.request().method(),
      pathname: url.pathname,
      authorization: authorizationOf(route),
      handledAs
    }));
  }

  await page.route('**/api/**', route => {
    audit(route, 'UNHANDLED_FAIL_CLOSED');
    throw new Error(
      `Option D G2 fixture received an unhandled API request: ${route.request().method()} ${route.request().url()}`
    );
  });
  await page.route('**/health', route => {
    audit(route, 'health');
    return fulfillJson(route, 200, { status: 'Healthy', port: fixture.healthPort });
  });
  await page.route('**/api/auth/setup-status', route => {
    audit(route, 'auth/setup-status');
    return fulfillJson(route, 200, fixture.setupStatus);
  });
  await page.route('**/api/auth/login', async route => {
    audit(route, 'auth/login');
    if (route.request().method() !== 'POST') {
      await fulfillJson(route, 405, { message: 'Method not allowed.' });
      return;
    }
    const payload = await route.request().postDataJSON() as { username?: string; password?: string };
    if (payload.username !== fixture.engineer.username || payload.password !== fixture.password) {
      await fulfillJson(route, 401, { message: '用户名或密码错误。', errorCode: 'AUTH_INVALID_CREDENTIALS' });
      return;
    }
    await fulfillJson(route, 200, { token: fixture.loginToken });
  });
  await page.route('**/api/auth/me', route => {
    audit(route, 'auth/me');
    const authorization = authorizationOf(route);
    if (authorization === `Bearer ${fixture.loginToken}`) {
      return fulfillJson(route, 200, fixture.engineer);
    }
    if (authorization === `Bearer ${fixture.forbiddenToken}`) {
      return fulfillJson(route, 200, fixture.operator);
    }
    return fulfillJson(route, 401, { message: '需要登录。', errorCode: 'AUTH_REQUIRED' });
  });
  await page.route('**/api/projects/recent**', route => {
    audit(route, 'projects/recent');
    return fulfillJson(route, 200, []);
  });
  await page.route('**/api/projects', route => {
    audit(route, 'projects');
    return fulfillJson(route, 200, []);
  });

  await page.addInitScript(({ authToken, startup, fixtureId }) => {
    localStorage.setItem('clearvision.studio-ui.preferences.v1', JSON.stringify({
      schemaVersion: 1,
      theme: 'light',
      density: 'compact',
      rememberedUsername: null
    }));
    if (authToken) sessionStorage.setItem('cv_auth_token', authToken);
    else sessionStorage.removeItem('cv_auth_token');
    sessionStorage.setItem('cv_option_d_g2_fixture_id', fixtureId);
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      configurable: false,
      writable: false,
      value: Object.freeze({
        ...startup,
        profileAllowedRoles: Object.freeze([...startup.profileAllowedRoles]),
        featureFlags: Object.freeze({ ...startup.featureFlags }),
        apiBaseUrl: `${window.location.origin}/api`
      })
    });
  }, {
    authToken: mode === 'forbidden' ? fixture.forbiddenToken : null,
    startup: fixture.startup,
    fixtureId: fixture.id
  });

  return Object.freeze({ requests });
}
