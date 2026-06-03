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

test('dark theme keeps template selector surfaces off white', async ({ page }) => {
  await mockDarkSettings(page);
  await page.addInitScript(() => {
    localStorage.setItem('cv_theme', 'dark');
  });

  await bootAuthenticatedApp(page);

  await page.evaluate(() => {
    const overlay = document.createElement('div');
    overlay.className = 'template-selector-overlay';
    overlay.innerHTML = `
      <div class="template-selector-dialog" role="dialog" aria-modal="true">
        <div class="template-selector-header">
          <div>
            <h3>从模板创建流程</h3>
            <p>选择预设模板，快速生成流程骨架。</p>
          </div>
        </div>
        <div class="template-selector-filters">
          <input class="cv-input" placeholder="搜索模板名称或描述..." />
          <select class="cv-input"><option>全部行业</option></select>
        </div>
        <div class="template-tags">
          <button type="button" class="template-tag-btn active">全部标签</button>
        </div>
        <div class="template-card-grid">
          <article class="template-card">
            <div class="template-card-head">
              <h4>检测模板</h4>
              <span class="template-card-industry">通用</span>
            </div>
            <p class="template-card-description">用于暗色模式表面验证。</p>
            <div class="template-card-meta"><span>3 个算子</span><span>2 个标签</span></div>
            <div class="template-card-tags"><span class="template-card-tag">OCR</span></div>
          </article>
        </div>
      </div>
    `;
    document.body.appendChild(overlay);
  });

  await expect(page.locator('.template-selector-dialog')).toBeVisible();
  await expect.poll(() => readEffectiveBackgroundMaxChannel(page, '.template-selector-dialog')).toBeLessThan(90);
  await expect.poll(() => readEffectiveBackgroundMaxChannel(page, '.template-card')).toBeLessThan(100);
});
