import { expect, Page, test } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

function createDarkSettingsPayload() {
  return {
    general: {
      softwareTitle: 'ClearVision',
      theme: 'dark',
      autoStart: false,
    },
    runtime: {
      autoRun: false,
      stopOnConsecutiveNg: 2,
      missingMaterialTimeoutSeconds: 15,
      applyProtectionRules: true,
    },
  };
}

async function mockDarkSettings(page: Page) {
  const settingsPayload = createDarkSettingsPayload();

  await page.route('**/api/settings', async route => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(settingsPayload),
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(settingsPayload),
    });
  });
}

async function readEffectiveBackgroundMaxChannel(page: Page, selector: string) {
  return page.locator(selector).evaluate(node => {
    let current: Element | null = node;
    while (current) {
      const color = getComputedStyle(current).backgroundColor;
      const match = color.match(/rgba?\(([^)]+)\)/);
      if (match) {
        const parts = match[1].split(',').map(part => Number.parseFloat(part.trim()));
        const alpha = parts.length >= 4 ? parts[3] : 1;
        if (alpha > 0) {
          return Math.max(parts[0] ?? 255, parts[1] ?? 255, parts[2] ?? 255);
        }
      }

      current = current.parentElement;
    }

    return 255;
  });
}

test('dark theme keeps flow and inspection critical surfaces off white', async ({ page }) => {
  await mockDarkSettings(page);
  await page.addInitScript(() => {
    localStorage.setItem('cv_theme', 'dark');
  });

  await bootAuthenticatedApp(page);

  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect.poll(() => readEffectiveBackgroundMaxChannel(page, '#flow-canvas')).toBeLessThan(90);
  await expect.poll(() => readEffectiveBackgroundMaxChannel(page, '#property-panel')).toBeLessThan(90);

  await page.locator('.nav-btn[data-view="inspection"]').click();
  await expect(page.locator('#inspection-view')).toBeVisible();
  await expect(page.locator('.inspection-protection-notice')).toBeVisible();

  await expect.poll(() => readEffectiveBackgroundMaxChannel(page, '#inspection-image-area')).toBeLessThan(90);
  await expect.poll(() => readEffectiveBackgroundMaxChannel(page, '.inspection-protection-notice')).toBeLessThan(140);
});
