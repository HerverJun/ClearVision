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

const E2E_USER: E2EAuthenticatedUser = {
  userId: 'e2e-admin',
  id: 'e2e-admin',
  username: 'admin',
  displayName: 'E2E Admin',
  role: 'Admin',
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
