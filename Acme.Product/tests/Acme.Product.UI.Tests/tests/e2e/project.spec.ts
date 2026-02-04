import { test, expect } from '@playwright/test';

test.describe('ClearVision E2E', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
  });

  test('should load the home page', async ({ page }) => {
    await expect(page).toHaveTitle(/ClearVision/);
    await expect(page.locator('#app')).toBeVisible();
  });

  test('should verify default status', async ({ page }) => {
    await expect(page.locator('#status-text')).toContainText('就绪');
    await expect(page.locator('#project-name')).toContainText('未命名工程');
  });
});