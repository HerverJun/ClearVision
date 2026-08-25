import type { Page } from '@playwright/test';

export const optionDG1DeterministicFixture = Object.freeze({
  schemaVersion: 1,
  id: 'option-d-g1-design-system.v1',
  dataSource: 'OPTION_D_G1_FIXTURE',
  healthPort: 5177,
  authToken: 'option-d-g1-browser-fixture-token',
  user: Object.freeze({
    userId: 'option-d-g1-user',
    username: 'option-d-g1',
    role: 'Engineer'
  }),
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
    featureFlags: Object.freeze({})
  })
});

export async function installOptionDG1DeterministicFixture(page: Page): Promise<void> {
  const fixture = optionDG1DeterministicFixture;
  const headers = { 'x-clearvision-data-source': fixture.dataSource };
  await page.route('**/api/**', route => {
    throw new Error(
      `Option D G1 fixture received an unhandled API request: ${route.request().method()} ${route.request().url()}`
    );
  });
  await page.route('**/health', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers,
    body: JSON.stringify({ status: 'Healthy', port: fixture.healthPort })
  }));
  await page.route('**/api/auth/me', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers,
    body: JSON.stringify(fixture.user)
  }));
  await page.route('**/api/auth/setup-status', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers,
    body: JSON.stringify(fixture.setupStatus)
  }));

  await page.addInitScript(({ authToken, userId, startup, fixtureId }) => {
    sessionStorage.setItem('cv_auth_token', authToken);
    sessionStorage.setItem('cv_current_user', userId);
    sessionStorage.setItem('cv_option_d_g1_fixture_id', fixtureId);
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
    authToken: fixture.authToken,
    userId: fixture.user.userId,
    startup: fixture.startup,
    fixtureId: fixture.id
  });
}
