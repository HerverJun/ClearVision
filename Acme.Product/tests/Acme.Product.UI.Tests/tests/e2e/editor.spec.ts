import { test, expect } from '@playwright/test';

test.describe('Flow Editor', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
  });

  test('should have canvas visible', async ({ page }) => {
    await expect(page.locator('#flow-canvas')).toBeVisible();
  });

  test('should have operator library', async ({ page }) => {
    await expect(page.locator('#operator-library')).toBeVisible();
  });
});