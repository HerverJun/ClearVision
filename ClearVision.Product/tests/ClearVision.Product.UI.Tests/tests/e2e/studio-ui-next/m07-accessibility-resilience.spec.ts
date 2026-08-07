import { expect, test, type Page, type Route } from '@playwright/test';
import {
  fulfillF02Json,
  installF02BrowserStartup
} from './f02-browser-fixture';

const fixtureSchema = 'm07-accessibility-resilience.v1';

async function fulfill(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, fixtureSchema);
}

async function boot(page: Page): Promise<void> {
  await installF02BrowserStartup(page, {
    'Studio2.AiWorkbench': true,
    'Studio2.InspectionRun': true,
    'Studio2.Settings': true,
    'Studio2.StationsRead': true
  });
  await page.route('**/health', route => fulfill(route, 200, { status: 'Healthy', port: 5177 }));
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/auth/setup-status') {
      await fulfill(route, 200, {
        requiresInitialAdminSetup: false,
        usernameMinLength: 3,
        passwordMinLength: 6,
        requiresUppercase: false,
        requiresLowercase: false,
        requiresDigit: false
      });
      return;
    }
    if (path === '/api/auth/me') {
      await fulfill(route, 200, { userId: 'm07-user', username: 'm07-engineer', role: 'Engineer' });
      return;
    }
    if (path === '/api/projects/recent' || path === '/api/projects' || path === '/api/operators/library') {
      await fulfill(route, 200, []);
      return;
    }
    await fulfill(route, 404, { error: 'NotFound' });
  });
}

async function installEquivalentTextScale(page: Page): Promise<void> {
  await page.evaluate(() => {
    const style = document.createElement('style');
    style.dataset.m07TextScale = '200-percent-equivalent';
    style.textContent = `
      html { font-size: 200% !important; }
      :root {
        --cv-font-size-2xs: 22px !important;
        --cv-font-size-xs: 24px !important;
        --cv-font-size-sm: 26px !important;
        --cv-font-size-md: 28px !important;
        --cv-font-size-lg: 32px !important;
        --cv-font-size-xl: 44px !important;
        --cv-font-size-2xl: 56px !important;
      }
    `;
    document.head.append(style);
  });
}

async function inspectSurface(page: Page): Promise<{
  horizontalOverflow: number;
  mainCount: number;
  unnamedControls: string[];
  liveRegions: number;
  scrollOwners: string[];
}> {
  return page.evaluate(() => {
    const isVisible = (element: Element): element is HTMLElement => {
      const node = element as HTMLElement;
      const style = getComputedStyle(node);
      const rect = node.getBoundingClientRect();
      return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
    };
    const hasAccessibleName = (element: HTMLElement): boolean => {
      const ariaLabel = element.getAttribute('aria-label')?.trim();
      const labelledBy = element.getAttribute('aria-labelledby')?.trim();
      const title = element.getAttribute('title')?.trim();
      const text = element.textContent?.replace(/\s+/g, ' ').trim();
      if (ariaLabel || labelledBy || title || text) return true;
      if (element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement) {
        return element.labels?.length > 0;
      }
      return false;
    };
    const controls = [...document.querySelectorAll<HTMLElement>('button, a, input, select, textarea, [role="button"]')]
      .filter(isVisible);
    const unnamedControls = controls
      .filter(element => !hasAccessibleName(element))
      .map(element => `${element.tagName.toLowerCase()}${element.id ? `#${element.id}` : ''}`);
    const scrollOwners = [...document.querySelectorAll<HTMLElement>('*')]
      .filter(isVisible)
      .filter(element => {
        const style = getComputedStyle(element);
        return ['auto', 'scroll'].includes(style.overflowY) && element.scrollHeight > element.clientHeight + 1;
      })
      .slice(0, 20)
      .map(element => `${element.tagName.toLowerCase()}${element.className ? `.${String(element.className).trim().replace(/\s+/g, '.')}` : ''}`);
    return {
      horizontalOverflow: Math.max(
        document.documentElement.scrollWidth - document.documentElement.clientWidth,
        document.body.scrollWidth - document.body.clientWidth
      ),
      mainCount: document.querySelectorAll('main').length,
      unnamedControls,
      liveRegions: document.querySelectorAll('[aria-live]').length,
      scrollOwners
    };
  });
}

async function inspectContrast(page: Page): Promise<{ minRatio: number; pairs: Array<{ name: string; ratio: number }> }> {
  return page.evaluate(() => {
    const parseColor = (value: string): [number, number, number] | null => {
      const hex = value.trim().match(/^#([0-9a-f]{6})$/i);
      if (hex) return [0, 2, 4].map(index => Number.parseInt(hex[1]!.slice(index, index + 2), 16)) as [number, number, number];
      const shortHex = value.trim().match(/^#([0-9a-f]{3})$/i);
      if (shortHex) return shortHex[1]!.split('').map(channel => Number.parseInt(channel + channel, 16)) as [number, number, number];
      const rgb = value.trim().match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
      if (rgb) return [Number(rgb[1]), Number(rgb[2]), Number(rgb[3])];
      return null;
    };
    const luminance = (value: [number, number, number]): number => {
      const channels = value.map(channel => channel / 255).map(channel => channel <= 0.03928
        ? channel / 12.92
        : ((channel + 0.055) / 1.055) ** 2.4);
      return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
    };
    const root = getComputedStyle(document.documentElement);
    const token = (name: string, seen = new Set<string>()): [number, number, number] => {
      if (seen.has(name)) throw new Error(`Circular color token ${name}.`);
      seen.add(name);
      const value = root.getPropertyValue(name).trim();
      const reference = value.match(/^var\((--[a-z0-9-]+)(?:,\s*([^)]*))?\)$/i);
      if (reference) return token(reference[1]!, seen);
      const color = parseColor(value);
      if (!color) throw new Error(`Unparseable color token ${name}: ${value}.`);
      return color;
    };
    const pairs = [
      ['primary-page', '--cv-text-primary', '--cv-surface-page'],
      ['secondary-page', '--cv-text-secondary', '--cv-surface-page'],
      ['muted-page', '--cv-text-muted', '--cv-surface-page'],
      ['link-page', '--cv-color-link', '--cv-surface-page'],
      ['focus-page', '--cv-focus-ring-color', '--cv-surface-page'],
      ['ok-raised', '--cv-color-status-ok-strong', '--cv-surface-raised'],
      ['ng-raised', '--cv-color-status-ng-strong', '--cv-surface-raised'],
      ['error-raised', '--cv-color-status-error-strong', '--cv-surface-raised'],
      ['warning-raised', '--cv-color-status-warning-strong', '--cv-surface-raised']
    ].map(([name, foreground, background]) => {
      const foregroundLuminance = luminance(token(foreground));
      const backgroundLuminance = luminance(token(background));
      const lighter = Math.max(foregroundLuminance, backgroundLuminance);
      const darker = Math.min(foregroundLuminance, backgroundLuminance);
      return { name, ratio: (lighter + 0.05) / (darker + 0.05) };
    });
    return { minRatio: Math.min(...pairs.map(pair => pair.ratio)), pairs };
  });
}

async function inspectPointerTargets(page: Page): Promise<{ undersized: string[]; controlCount: number }> {
  return page.evaluate(() => {
    const isVisible = (element: Element): element is HTMLElement => {
      const node = element as HTMLElement;
      const style = getComputedStyle(node);
      const rect = node.getBoundingClientRect();
      return style.display !== 'none' && style.visibility !== 'hidden' && style.pointerEvents !== 'none'
        && rect.width > 0 && rect.height > 0;
    };
    const controls = [...document.querySelectorAll<HTMLElement>(
      'button, a, input:not([type="hidden"]), select, textarea, summary, [role="button"], [role="tab"]'
    )].filter(isVisible);
    const undersized = controls
      .filter(element => {
        const rect = element.getBoundingClientRect();
        return rect.width < 24 || rect.height < 24;
      })
      .map(element => {
        const label = element.getAttribute('aria-label') || element.textContent?.replace(/\s+/g, ' ').trim() || element.tagName;
        const rect = element.getBoundingClientRect();
        return `${element.tagName.toLowerCase()}[${label}] ${Math.round(rect.width)}x${Math.round(rect.height)}`;
      });
    return { undersized, controlCount: controls.length };
  });
}

test('M07 equivalent 200 percent text scale keeps product surfaces named and horizontally stable', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await boot(page);
  await page.goto('/studio/index.html#/overview');
  await expect(page.locator('[data-product-shell]')).toBeVisible();
  await installEquivalentTextScale(page);

  const surfaces = [
    { hash: '#/overview', ready: '[data-capability="overview"]' },
    { hash: '#/projects', ready: '[data-capability="projects-read"]' },
    { hash: '#/results', ready: '[data-capability="results-read"]' },
    { hash: '#/stations', ready: '[data-capability="stations-read"]' },
    { hash: '#/inspection', ready: '[data-testid="inspection-projects-page"]' },
    { hash: '#/settings', ready: '[data-capability="settings"]' },
    { hash: '#/ai', ready: '.ai-workbench-page' }
  ] as const;

  for (const { hash, ready } of surfaces) {
    await page.evaluate(nextHash => { window.location.hash = nextHash; }, hash);
    await expect(page.locator(ready)).toBeVisible();
    await expect(page.getByRole('main')).toHaveCount(1);
    await expect.poll(async () => (await inspectSurface(page)).mainCount).toBe(1);
    const audit = await inspectSurface(page);
    expect(audit.horizontalOverflow).toBeLessThanOrEqual(1);
    expect(audit.unnamedControls).toEqual([]);
    expect(audit.liveRegions).toBeGreaterThan(0);
  }

  const appearance = page.locator('[data-product-appearance]');
  await appearance.locator('summary').click();
  const popover = page.locator('.product-layout__appearance-popover');
  const bounds = await popover.boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(1366);
});

test('M07 keyboard focus and reduced motion remain observable at the product shell boundary', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 1366, height: 600 });
  await boot(page);
  await page.goto('/studio/index.html#/overview');

  const skipLink = page.locator('.product-layout__skip-link');
  await skipLink.focus();
  await expect(skipLink).toBeFocused();
  const focus = await page.evaluate(() => {
    const element = document.activeElement as HTMLElement | null;
    if (!element) return null;
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return { outlineWidth: style.outlineWidth, outlineStyle: style.outlineStyle, rect };
  });
  expect(focus).not.toBeNull();
  expect(focus!.outlineStyle).not.toBe('none');
  expect(focus!.rect.width).toBeGreaterThan(0);
  expect(focus!.rect.height).toBeGreaterThan(0);

  const duration = await page.locator('html').evaluate(element =>
    getComputedStyle(element).getPropertyValue('--cv-motion-duration-normal').trim()
  );
  expect(['0ms', '0s']).toContain(duration);
  const audit = await inspectSurface(page);
  expect(audit.horizontalOverflow).toBeLessThanOrEqual(1);
  expect(audit.mainCount).toBe(1);
});

test('M07 desktop pointer targets stay at least 24 CSS pixels across primary shell surfaces', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await boot(page);
  await page.goto('/studio/index.html#/overview');

  const surfaces = [
    { hash: '#/overview', ready: '[data-capability="overview"]' },
    { hash: '#/projects', ready: '[data-capability="projects-read"]' },
    { hash: '#/results', ready: '[data-capability="results-read"]' },
    { hash: '#/stations', ready: '[data-capability="stations-read"]' },
    { hash: '#/inspection', ready: '[data-testid="inspection-projects-page"]' },
    { hash: '#/settings', ready: '[data-capability="settings"]' },
    { hash: '#/ai', ready: '.ai-workbench-page' }
  ] as const;

  for (const { hash, ready } of surfaces) {
    await page.evaluate(nextHash => { window.location.hash = nextHash; }, hash);
    await expect(page.locator(ready)).toBeVisible();
    await expect(page.getByRole('main')).toHaveCount(1);
    const audit = await inspectPointerTargets(page);
    expect(audit.controlCount, `${hash} should expose pointer targets`).toBeGreaterThan(0);
    expect(audit.undersized, `${hash} has undersized pointer targets`).toEqual([]);
  }
});

test('M07 light and dark semantic tokens meet the AA audit baseline', async ({ page }) => {
  await boot(page);
  await page.goto('/studio/index.html#/overview');
  const light = await inspectContrast(page);
  expect(light.minRatio).toBeGreaterThanOrEqual(4.5);

  const appearance = page.locator('[data-product-appearance]');
  await appearance.locator('summary').click();
  await page.locator('[data-product-appearance] button').nth(1).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  const dark = await inspectContrast(page);
  expect(dark.minRatio).toBeGreaterThanOrEqual(4.5);
});
