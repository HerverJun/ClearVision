import { expect, test, type Page } from '@playwright/test';
import { installR2BrowserScenario, installR2ReadOnlyDispatcher } from './r2-in-app-browser-fixture';

async function observeLayoutShift(page: Page): Promise<void> {
  await page.evaluate(() => {
    const state = { value: 0 };
    Object.defineProperty(window, '__R2_LAYOUT_SHIFT__', { configurable: true, value: state });
    new PerformanceObserver(list => {
      for (const entry of list.getEntries()) {
        const layoutShift = entry as PerformanceEntry & { value?: number; hadRecentInput?: boolean };
        if (!layoutShift.hadRecentInput) state.value += layoutShift.value ?? 0;
      }
    }).observe({ type: 'layout-shift', buffered: true });
  });
}

async function layoutShift(page: Page): Promise<number> {
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
  await page.waitForTimeout(200);
  return page.evaluate(() => (window as typeof window & { __R2_LAYOUT_SHIFT__?: { value: number } }).__R2_LAYOUT_SHIFT__?.value ?? 0);
}

for (const reducedMotion of [false, true]) {
  test(`R2 primitive motion keeps focus, ARIA, and layout stable (reduced=${reducedMotion})`, async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await installR2BrowserScenario(page, {
      routeStateId: 's01-overview-main',
      theme: 'light',
      density: 'compact',
      reducedMotion
    });
    const audit = await installR2ReadOnlyDispatcher(page);
    await page.goto('/studio/index.html#/labs/design');
    await expect(page.locator('[data-design-lab="ready"]')).toBeVisible();
    if (reducedMotion) {
      await page.getByText('减少动效', { exact: true }).click();
      await expect(page.locator('[data-design-reduced-motion]')).toBeChecked();
      await expect(page.locator('html')).toHaveAttribute('data-reduced-motion', 'true');
    }
    await observeLayoutShift(page);

    const menuTrigger = page.getByRole('button', { name: '打开样本操作菜单' });
    await menuTrigger.click();
    await expect(menuTrigger).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('menuitem', { name: /刷新样本/ })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(menuTrigger).toBeFocused();

    await page.getByRole('button', { name: '打开评审对话框' }).click();
    await expect(page.getByRole('dialog', { name: '确认视觉基础' })).toBeVisible();
    await expect(page.getByRole('button', { name: '接受此方向' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);

    await page.getByRole('button', { name: '显示通知' }).click();
    await expect(page.locator('[data-toast-id="design-toast-1"]')).toContainText('设计令牌已应用');
    expect(await layoutShift(page)).toBe(0);
    expect(audit.consoleErrors).toEqual([]);
    expect(audit.pageErrors).toEqual([]);
    expect(audit.failedRequests).toEqual([]);
    expect(audit.unexpectedWrites).toEqual([]);

    const motionValues = await page.evaluate(() => {
      const root = getComputedStyle(document.documentElement);
      return {
        reduced: document.documentElement.dataset.reducedMotion,
        fast: root.getPropertyValue('--cv-motion-duration-fast').trim(),
        normal: root.getPropertyValue('--cv-motion-duration-normal').trim()
      };
    });
    expect(motionValues.reduced).toBe(String(reducedMotion));
    if (reducedMotion) {
      expect(parseDurationMs(motionValues.fast)).toBeLessThanOrEqual(1);
      expect(parseDurationMs(motionValues.normal)).toBeLessThanOrEqual(1);
    } else {
      expect(parseDurationMs(motionValues.fast)).toBe(140);
      expect(parseDurationMs(motionValues.normal)).toBe(180);
    }
  });
}

function parseDurationMs(value: string): number {
  if (value.endsWith('ms')) return Number.parseFloat(value);
  if (value.endsWith('s')) return Number.parseFloat(value) * 1000;
  throw new Error(`Unsupported CSS duration: ${value}.`);
}
