import { expect, Page, test, TestInfo } from '@playwright/test';

type Theme = 'light' | 'dark';
type Density = 'compact' | 'comfortable';

async function bootDesignLab(page: Page): Promise<string[]> {
  const runtimeErrors: string[] = [];
  page.on('pageerror', error => runtimeErrors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') runtimeErrors.push(message.text());
  });

  await page.route('**/health', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers: { 'x-clearvision-data-source': 'BROWSER_FIXTURE' },
    body: JSON.stringify({ status: 'Healthy', port: 5177 })
  }));
  await page.route('**/api/auth/me', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers: { 'x-clearvision-data-source': 'BROWSER_FIXTURE' },
    body: JSON.stringify({ userId: 'design-lab-user', username: 'design-lab', role: 'Engineer' })
  }));
  await page.route('**/api/auth/setup-status', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    headers: { 'x-clearvision-data-source': 'BROWSER_FIXTURE' },
    body: JSON.stringify({ requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false })
  }));

  await page.addInitScript(() => {
    sessionStorage.setItem('cv_auth_token', 'design-lab-browser-fixture-token');
    sessionStorage.setItem('cv_current_user', 'design-lab-user');
    const startup = Object.freeze({
      schemaVersion: 1,
      uiKind: 'studio-ui',
      hostKind: 'browser-test',
      apiBaseUrl: `${window.location.origin}/api`,
      studioUiBasePath: '/studio/',
      startupProfile: 'NEXT_DEFAULT',
      profileAllowedRoles: Object.freeze(['Admin', 'Engineer', 'Operator']),
      featureFlags: Object.freeze({})
    });
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: startup,
      writable: false,
      configurable: false
    });
  });

  await page.goto('/studio/index.html#/labs/design');
  await expect(page.locator('[data-design-lab="ready"]')).toBeVisible();
  return runtimeErrors;
}

async function setTheme(page: Page, theme: Theme): Promise<void> {
  await page.locator(`[data-design-theme="${theme}"]`).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
}

async function setDensity(page: Page, density: Density): Promise<void> {
  await page.locator(`[data-design-density="${density}"]`).click();
  await expect(page.locator('html')).toHaveAttribute('data-density', density);
}

async function captureEvidence(
  page: Page,
  testInfo: TestInfo,
  name: string
): Promise<void> {
  await page.screenshot({
    path: testInfo.outputPath(`${name}.png`),
    fullPage: true
  });
}

test('Design Lab exposes themes, density and separated industrial status colors', async ({ page }) => {
  const runtimeErrors = await bootDesignLab(page);
  const root = page.locator('html');
  await expect(root).toHaveAttribute('data-theme', 'light');
  await expect(root).toHaveAttribute('data-density', 'comfortable');
  await expect(root).toHaveAttribute('data-reduced-motion', 'false');

  await setTheme(page, 'dark');
  await setDensity(page, 'compact');

  const palette = await page.locator('[data-color-token]').evaluateAll(cards =>
    cards.map(card => ({
      token: card.getAttribute('data-color-token'),
      color: getComputedStyle(card.querySelector('.design-lab__swatch') as Element).backgroundColor
    }))
  );
  expect(palette.map(item => item.token)).toEqual([
    'brand', 'ok', 'ng', 'error', 'warning', 'info', 'idle', 'offline', 'unknown', 'disabled'
  ]);
  expect(new Set(palette.map(item => item.color)).size).toBe(palette.length);
  expect(runtimeErrors).toEqual([]);
});

test('Modal traps keyboard focus, closes with Escape and restores its trigger', async ({ page }) => {
  const runtimeErrors = await bootDesignLab(page);
  const trigger = page.locator('[data-modal-trigger]');
  await trigger.focus();
  await expect(trigger).toBeFocused();
  await page.keyboard.press('Enter');

  const dialog = page.getByRole('dialog', { name: '确认视觉基础' });
  const initial = page.getByRole('button', { name: '接受此方向' });
  const close = page.getByRole('button', { name: '关闭对话框' });
  await expect(dialog).toBeVisible();
  await expect(initial).toBeFocused();

  await page.keyboard.press('Tab');
  await expect(close).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(initial).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(trigger).toBeFocused();
  expect(runtimeErrors).toEqual([]);
});

test('Menu and Tooltip keep keyboard focus and floating layers inside the viewport', async ({ page }) => {
  const runtimeErrors = await bootDesignLab(page);
  const guidance = page.getByRole('button', { name: '切换状态说明' });
  await guidance.focus();
  await expect(page.getByRole('tooltip')).toHaveText('显示或隐藏键盘状态说明');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('tooltip')).toHaveCount(0);

  const trigger = page.getByRole('button', { name: '打开样本操作菜单' });
  await trigger.scrollIntoViewIfNeeded();
  await trigger.click();
  const menu = page.getByRole('menu', { name: 'Design Lab 操作' });
  const menuItems = menu.getByRole('menuitem');
  await expect(menu).toBeVisible();
  await expect(menuItems.first()).toBeFocused();
  await page.keyboard.press('End');
  await expect(menuItems.last()).toBeFocused();

  const box = await menu.boundingBox();
  const viewport = page.viewportSize();
  expect(box).not.toBeNull();
  expect(viewport).not.toBeNull();
  if (box && viewport) {
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.y).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);
    expect(box.y + box.height).toBeLessThanOrEqual(viewport.height);
  }

  await page.keyboard.press('Escape');
  await expect(menu).toHaveCount(0);
  await expect(trigger).toBeFocused();
  expect(runtimeErrors).toEqual([]);
});

test('Toast and Splitter release transient lifecycle work', async ({ page }) => {
  const runtimeErrors = await bootDesignLab(page);
  await page.getByRole('button', { name: '显示通知' }).click();
  const toast = page.locator('[data-toast-id]');
  await expect(toast).toBeVisible();
  await toast.getByRole('button').click();
  await expect(toast).toHaveCount(0);

  const separator = page.getByRole('separator', { name: '调整属性检查器预览宽度' });
  await separator.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.locator('.design-lab__inspector-sample')).toContainText('300 px');

  const box = await separator.boundingBox();
  expect(box).not.toBeNull();
  if (box) {
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 + 32, box.y + box.height / 2);
    await page.mouse.up();
    await expect(page.locator('.design-lab__inspector-sample')).toContainText('332 px');
  }
  expect(runtimeErrors).toEqual([]);
});

test('Reduced-motion preference removes design motion durations', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const runtimeErrors = await bootDesignLab(page);

  const mediaDuration = await page.locator('html').evaluate(element =>
    getComputedStyle(element).getPropertyValue('--cv-motion-duration-normal').trim()
  );
  expect(['0ms', '0s']).toContain(mediaDuration);

  const reducedMotionToggle = page.locator('[data-design-reduced-motion]');
  await page.locator('label:has([data-design-reduced-motion])').click();
  await expect(reducedMotionToggle).toBeChecked();
  await expect(page.locator('html')).toHaveAttribute('data-reduced-motion', 'true');
  const explicitDuration = await page.locator('html').evaluate(element =>
    getComputedStyle(element).getPropertyValue('--cv-motion-duration-slow').trim()
  );
  expect(['0ms', '0s']).toContain(explicitDuration);
  expect(runtimeErrors).toEqual([]);
});

test('Design evidence matrix covers light/dark, density and desktop/short viewports', async ({ page }, testInfo) => {
  const runtimeErrors = await bootDesignLab(page);
  const viewports = [
    { name: '1366x768', width: 1366, height: 768 },
    { name: '1920x1080', width: 1920, height: 1080 },
    { name: '1366x600-short', width: 1366, height: 600 }
  ] as const;

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    for (const theme of ['light', 'dark'] as const) {
      await setTheme(page, theme);
      for (const density of ['comfortable', 'compact'] as const) {
        await setDensity(page, density);
        const overflow = await page.locator('html').evaluate(element =>
          element.scrollWidth - element.clientWidth
        );
        expect(overflow, `${viewport.name} ${theme} ${density} horizontal overflow`).toBeLessThanOrEqual(1);
        await page.locator('[data-modal-trigger]').scrollIntoViewIfNeeded();
        await expect(page.locator('[data-modal-trigger]')).toBeVisible();
        await captureEvidence(page, testInfo, `${viewport.name}-${theme}-${density}`);
      }
    }
  }

  expect(runtimeErrors).toEqual([]);
});
