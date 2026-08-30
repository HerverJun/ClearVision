import { expect, Page } from '@playwright/test';

type E2EAuthenticatedUser = {
  userId: string;
  id?: string;
  username: string;
  displayName: string;
  role: string;
  capabilities?: string[];
  passwordPolicy?: { minimumLength: number };
};

const E2E_ADMIN_CAPABILITIES = [
  'project.edit',
  'station.commands.create',
  'station.packages.read',
  'station.packages.deploy',
  'station.test-packages.create',
  'settings.update',
  'settings.reset',
  'plc.settings.update',
  'plc.mappings.update',
  'plc.connection.test',
  'tcp.profiles.update',
  'tcp.connections.operate',
  'station.communication.update',
  'station.communication-token.manage',
  'cameras.bindings.update',
  'cameras.capture',
  'cameras.preview.operate',
  'trigger-input.operate',
  'ai.models.create',
  'ai.models.update',
  'ai.models.delete',
  'ai.models.activate',
  'ai.models.set-default',
  'ai.models.test',
  'database.status.read',
  'database.backup',
  'database.repair',
  'database.restore',
  'database.cleanup',
  'users.read',
  'users.create',
  'users.update',
  'users.delete',
  'users.reset-password',
];

const E2E_USER: E2EAuthenticatedUser = {
  userId: 'e2e-admin',
  id: 'e2e-admin',
  username: 'admin',
  displayName: 'E2E Admin',
  role: 'Admin',
  capabilities: E2E_ADMIN_CAPABILITIES,
};

export async function bootAuthenticatedApp(
  page: Page,
  user: E2EAuthenticatedUser = E2E_USER,
): Promise<void> {
  await page.route('**/api/auth/me', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(user),
    });
  });

  await page.addInitScript(user => {
    sessionStorage.setItem('cv_auth_token', 'e2e-token');
    sessionStorage.setItem('cv_current_user', JSON.stringify(user));
    localStorage.setItem('cv_welcome_shown', 'true');
  }, user);

  await page.goto('/index.html');
  await expect(page.locator('#app')).toBeVisible();
  await expect(page.locator('#loading-screen')).toBeHidden();
}
