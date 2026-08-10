import { expect, test, type Page, type Route } from '@playwright/test';
import {
  auditF02Request,
  captureF02VisualEvidence,
  createF02RuntimeErrorAudit,
  expectGetOnly,
  f02BrowserFixture,
  f02OperatorPerformanceFixtureCount,
  fulfillF02Json,
  hasF02VisualEvidenceTarget,
  installF02BrowserStartup,
  installF02VisualPreferences,
  type F02MethodAuditEntry
} from './f02-browser-fixture';

const operatorFixture = Object.freeze({
  schemaVersion: 'f02-operators-read.v1',
  endpoint: Object.freeze([
    'GET /api/operators/library?includeCompatibility=true',
    'GET /api/operators/{type}/metadata'
  ]),
  sourceSha: f02BrowserFixture.sourceSha,
  dataSource: f02BrowserFixture.dataSource
});

const categoryLabels = Object.freeze([
  '采集', '图像预处理', '分割与区域', '特征提取', '匹配与定位', '缺陷检测', '测量',
  '标定与坐标', 'AI 推理', '3D 点云', '数据处理', '流程控制', '通信', '输出与辅助'
]);

function operator(index: number): Record<string, unknown> {
  const type = 1000 + index;
  const categoryId = index === 0 ? 8 : index % categoryLabels.length;
  const lifecycle = index === 0 ? 1 : index === 1 ? 3 : index === 2 ? 4 : 0;
  const hidden = lifecycle === 3 || lifecycle === 4;
  return {
    fixtureId: `operator-fixture-${String(index + 1).padStart(3, '0')}`,
    originalOperatorType: index % 158,
    type,
    displayName: index === 0 ? '颜色分析' : `性能算子 ${String(index + 1).padStart(3, '0')}`,
    description: index === 0 ? '分析图像颜色分布并输出检测区域。' : `${categoryLabels[categoryId]}目录项 ${index + 1}`,
    categoryId,
    category: categoryLabels[categoryId],
    lifecycle,
    lifecycleNote: lifecycle === 0 ? null : '该版本仍在现场数据验证中。',
    defaultHidden: hidden,
    iconName: 'operator',
    keywords: index === 0 ? ['颜色', 'Color'] : [`fixture-${index + 1}`],
    tags: ['视觉检测', '目录验证'],
    version: '1.0.0',
    inputPorts: [{
      name: index === 0 ? 'Image' : 'Input',
      displayName: index === 0 ? '图像' : '输入',
      dataType: 0,
      isRequired: true,
      description: null
    }],
    outputPorts: [{
      name: 'Result',
      displayName: '结果',
      dataType: 6,
      isRequired: false,
      description: null
    }],
    parameters: index === 0 ? [{
      name: 'Threshold',
      displayName: '颜色差异阈值',
      description: '差异超过此阈值时标记为候选缺陷区域。',
      dataType: 'double',
      defaultValue: 0.5,
      minValue: 0,
      maxValue: 1,
      isRequired: true,
      options: null
    }, {
      name: 'ColorSpace',
      displayName: '颜色空间',
      description: '选择用于比较颜色差异的计算空间。',
      dataType: 'enum',
      defaultValue: 'Lab',
      minValue: null,
      maxValue: null,
      isRequired: true,
      options: [
        { label: 'Lab 感知颜色', value: 'Lab' },
        { label: 'HSV 色相饱和度', value: 'Hsv' },
        { label: 'RGB 原始通道', value: 'Rgb' }
      ]
    }, {
      name: 'IgnoreLowSaturation',
      displayName: '忽略低饱和度区域',
      description: '启用后跳过接近灰度的像素，减少背景波动。',
      dataType: 'boolean',
      defaultValue: true,
      minValue: null,
      maxValue: null,
      isRequired: false,
      options: null
    }, {
      name: 'MinimumDefectArea',
      displayName: '最小缺陷面积',
      description: '小于该面积的候选区域不会进入输出结果。',
      dataType: 'double',
      defaultValue: 12.5,
      minValue: 0,
      maxValue: 100000,
      isRequired: false,
      options: null
    }] : [{
      name: 'Value',
      displayName: '值',
      description: null,
      dataType: 'double',
      defaultValue: 0.5,
      minValue: 0,
      maxValue: 1,
      isRequired: true,
      options: null
    }]
  };
}

const performanceOperators = Object.freeze(
  Array.from({ length: f02OperatorPerformanceFixtureCount }, (_, index) => operator(index))
);

async function fulfill(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, operatorFixture.schemaVersion);
}

async function bootOperators(
  page: Page,
  catalogPayload: unknown = performanceOperators
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  await installF02BrowserStartup(page);
  await page.route('**/health', route => fulfill(route, 200, { status: 'Healthy', port: 5177 }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    if (url.pathname === '/api/auth/setup-status') {
      await fulfill(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
      return;
    }
    if (url.pathname === '/api/auth/me') {
      await fulfill(route, 200, { userId: 'fixture-user', username: 'fixture-engineer', role: 'Engineer' });
      return;
    }
    if (url.pathname === '/api/operators/library') {
      await fulfill(route, 200, catalogPayload);
      return;
    }
    const detailMatch = /^\/api\/operators\/(\d+)\/metadata$/.exec(url.pathname);
    if (detailMatch) {
      const match = performanceOperators.find(item => item.type === Number(detailMatch[1]));
      await fulfill(route, match ? 200 : 404, match ?? { error: 'NotFound' });
      return;
    }
    await fulfill(route, 404, { error: 'NotFound' });
  });
  return audit;
}

test('Operator catalog paginates 200 numeric-enum fixtures and persists every filter in URL query', async ({ page }) => {
  const audit = await bootOperators(page);
  const startedAt = Date.now();
  await page.goto('/studio/index.html#/operators');

  await expect(page.locator('[data-capability="operators-read"]')).toBeVisible();
  await expect(page.locator('tbody tr')).toHaveCount(25);
  expect(Date.now() - startedAt).toBeLessThan(10_000);

  await page.getByRole('button', { name: '下一页' }).click();
  await expect(page).toHaveURL(/page=2/);
  await expect(page.getByText('性能算子 028', { exact: true })).toBeVisible();

  await page.getByRole('searchbox', { name: '搜索算子' }).fill('颜色分析');
  await page.getByRole('searchbox', { name: '端口' }).fill('Image');
  await page.getByRole('searchbox', { name: '参数' }).fill('Threshold');
  await page.getByLabel('分类').selectOption('AiInference');
  await page.getByLabel('生命周期').selectOption('Experimental');
  await page.getByLabel('可见范围').selectOption('all');

  await expect(page.getByText('颜色分析', { exact: true })).toBeVisible();
  await expect(page).toHaveURL(/q=%E9%A2%9C%E8%89%B2%E5%88%86%E6%9E%90/);
  await expect(page).toHaveURL(/category=AiInference/);
  await expect(page).toHaveURL(/port=Image/);
  await expect(page).toHaveURL(/parameter=Threshold/);
  await expect(page).toHaveURL(/lifecycle=Experimental/);
  await expect(page).toHaveURL(/visibility=all/);
  await expect(page.getByRole('button', { name: '清除 6 项筛选' })).toBeVisible();
  expect(expectGetOnly(audit)).toBe(true);
});

test('Operator detail shows current ports and parameters while remaining GET-only', async ({ page }) => {
  const audit = await bootOperators(page);
  await page.goto('/studio/index.html#/operators?q=颜色分析&visibility=all');
  await page.getByRole('link', { name: '查看颜色分析详情' }).click();

  await expect(page.locator('[data-capability="operators-read-detail"]')).toBeVisible();
  await expect(page.getByText('颜色分析', { exact: true })).toBeVisible();
  await expect(page.getByText('输入端口', { exact: true })).toBeVisible();
  await expect(page.getByText('输出端口', { exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: /Threshold/ })).toBeVisible();
  await expect(page.getByRole('cell', { name: /颜色空间/ })).toBeVisible();
  await expect(page.getByText('是 / 否', { exact: true })).toBeVisible();
  await expect(page.getByText('只读', { exact: true })).toBeVisible();
  expect(expectGetOnly(audit)).toBe(true);
  expect(audit.some(entry => /preview|recommend-parameters/.test(entry.path))).toBe(false);
});

test('Operator surfaces malformed catalog and missing detail as frozen product states', async ({ page }) => {
  await bootOperators(page, { items: [] });
  await page.goto('/studio/index.html#/operators');
  await expect(page.getByText('算子目录读取失败')).toBeVisible();

  await page.unrouteAll({ behavior: 'wait' });
  await bootOperators(page);
  await page.goto('/studio/index.html#/operators/999999');
  await expect(page.getByText('未找到算子')).toBeVisible();
});

for (const theme of ['light', 'dark'] as const) {
  test(`Operator filters expose a visible ${theme} focus boundary`, async ({ page }) => {
    await installF02VisualPreferences(page, theme, 'compact');
    await bootOperators(page);
    await page.goto('/studio/index.html#/operators');
    const search = page.getByRole('searchbox', { name: '搜索算子' });
    await search.focus();
    await expect(search).toBeFocused();
    const focusStyle = await search.evaluate(element => {
      const style = getComputedStyle(element);
      return {
        outlineStyle: style.outlineStyle,
        outlineWidth: Number.parseFloat(style.outlineWidth),
        outlineColor: style.outlineColor
      };
    });
    expect(focusStyle.outlineStyle).toBe('solid');
    expect(focusStyle.outlineWidth).toBeGreaterThanOrEqual(2);
    expect(focusStyle.outlineColor).not.toBe('rgba(0, 0, 0, 0)');
  });
}

for (const visual of [
  { id: 'operators-catalog-b0', route: '/studio/index.html#/operators', surface: 'operators-read', width: 1920, height: 1080, theme: 'light', density: 'compact' },
  { id: 'operators-catalog-b1', route: '/studio/index.html#/operators', surface: 'operators-read', width: 1536, height: 864, theme: 'light', density: 'compact' },
  { id: 'operators-catalog-b2', route: '/studio/index.html#/operators', surface: 'operators-read', width: 1366, height: 768, theme: 'light', density: 'compact' },
  { id: 'operators-catalog-b3', route: '/studio/index.html#/operators', surface: 'operators-read', width: 1920, height: 1080, theme: 'dark', density: 'compact' },
  { id: 'operators-catalog-b4-light', route: '/studio/index.html#/operators', surface: 'operators-read', width: 1920, height: 1080, theme: 'light', density: 'comfortable' },
  { id: 'operators-catalog-b4-dark', route: '/studio/index.html#/operators', surface: 'operators-read', width: 1920, height: 1080, theme: 'dark', density: 'comfortable' },
  { id: 'operator-detail-b0', route: '/studio/index.html#/operators/1000', surface: 'operators-read-detail', width: 1920, height: 1080, theme: 'light', density: 'compact' },
  { id: 'operator-detail-b1', route: '/studio/index.html#/operators/1000', surface: 'operators-read-detail', width: 1536, height: 864, theme: 'light', density: 'compact' },
  { id: 'operator-detail-b2', route: '/studio/index.html#/operators/1000', surface: 'operators-read-detail', width: 1366, height: 768, theme: 'light', density: 'compact' },
  { id: 'operator-detail-b3', route: '/studio/index.html#/operators/1000', surface: 'operators-read-detail', width: 1920, height: 1080, theme: 'dark', density: 'compact' },
  { id: 'operator-detail-b4-light', route: '/studio/index.html#/operators/1000', surface: 'operators-read-detail', width: 1920, height: 1080, theme: 'light', density: 'comfortable' },
  { id: 'operator-detail-b4-dark', route: '/studio/index.html#/operators/1000', surface: 'operators-read-detail', width: 1920, height: 1080, theme: 'dark', density: 'comfortable' }
] as const) {
  test(`captures ${visual.id} Browser fixture evidence`, async ({ page }) => {
    test.skip(!hasF02VisualEvidenceTarget(), 'F02 visual evidence output was not requested.');
    await page.setViewportSize({ width: visual.width, height: visual.height });
    await installF02VisualPreferences(page, visual.theme, visual.density);
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const audit = await bootOperators(page);
    await page.goto(visual.route);
    await expect(page.locator(`[data-capability="${visual.surface}"]`)).toBeVisible();
    if (visual.surface === 'operators-read') await expect(page.locator('tbody tr')).toHaveCount(25);
    else await expect(page.getByRole('cell', { name: /颜色空间/ })).toBeVisible();
    await captureF02VisualEvidence(page, {
      scenario: visual.id,
      viewport: { width: visual.width, height: visual.height },
      theme: visual.theme,
      density: visual.density,
      requests: audit,
      runtimeErrors
    });
    expect(expectGetOnly(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}
