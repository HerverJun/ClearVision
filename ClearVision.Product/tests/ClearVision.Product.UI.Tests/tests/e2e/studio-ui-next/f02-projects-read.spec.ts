import { expect, Page, Route, test } from '@playwright/test';
import {
  auditF02Request,
  expectGetOnly,
  fulfillF02Json,
  installF02BrowserStartup,
  type F02MethodAuditEntry
} from './f02-browser-fixture';

const fixtureSchemaVersion = 'f02-projects-read.v1';
const projectId = '11111111-1111-4111-8111-111111111111';
const missingProjectId = '99999999-9999-4999-8999-999999999999';
const flowId = '22222222-2222-4222-8222-222222222222';

function summary(name = '瓶盖检测'): Record<string, unknown> {
  return {
    id: projectId,
    name,
    description: '稳定工程摘要',
    version: '1.0.0',
    persistenceRevision: 12,
    createdAt: '2026-07-15T01:00:00Z',
    modifiedAt: '2026-07-15T02:00:00Z',
    lastOpenedAt: '2026-07-15T03:00:00Z'
  };
}

async function fulfillJson(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, fixtureSchemaVersion);
}

async function bootProjects(
  page: Page,
  listResponse: unknown = [summary()]
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  await installF02BrowserStartup(page);
  await page.route('**/health', route => fulfillJson(route, 200, { status: 'Healthy', port: 5177 }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    if (url.pathname === '/api/auth/me') {
      await fulfillJson(route, 200, { userId: 'fixture-user', username: 'fixture-engineer', role: 'Engineer' });
      return;
    }
    if (url.pathname === '/api/projects/recent') {
      await fulfillJson(route, 200, [summary()]);
      return;
    }
    if (url.pathname === '/api/projects/search') {
      await fulfillJson(route, 200, [summary(`搜索：${url.searchParams.get('keyword')}`)]);
      return;
    }
    if (url.pathname === `/api/projects/${missingProjectId}`) {
      await fulfillJson(route, 404, { error: 'NotFound' });
      return;
    }
    if (url.pathname === `/api/projects/${projectId}`) {
      await fulfillJson(route, 200, {
        ...summary(),
        flow: {
          id: flowId,
          name: '主流程',
          operators: [{ id: 'a' }, { id: 'b' }],
          connections: [{ id: 'c' }],
          decisionConfiguration: {
            finalDecisionBinding: { sourceOperatorId: 'b' },
            missingDecisionPolicy: 'Undetermined'
          }
        },
        assets: {
          schemaVersion: 1,
          calibrationAssets: [{ assetId: 'calibration' }],
          spatialAssets: [{ assetId: 'spatial' }]
        }
      });
      return;
    }
    if (url.pathname === '/api/projects') {
      await fulfillJson(route, 200, listResponse);
      return;
    }
    await fulfillJson(route, 404, { error: 'NotFound' });
  });

  await page.goto('/studio/index.html#/projects');
  await expect(page.locator('[data-capability="projects-read"]')).toBeVisible();
  return audit;
}

test('Projects list ignores non-canonical Flow fields, supports search and stays GET-only', async ({ page }) => {
  const listPayload = [{
    ...summary(),
    flow: { operators: new Array(40).fill({}), connections: new Array(50).fill({}) },
    assets: { calibrationAssets: new Array(60).fill({}) }
  }];
  const audit = await bootProjects(page, listPayload);

  await expect(page.getByRole('cell', { name: '瓶盖检测', exact: true })).toBeVisible();
  await expect(page.getByText('算子数量')).toHaveCount(0);
  await expect(page.getByText('连接数量')).toHaveCount(0);
  await page.getByRole('searchbox', { name: '搜索工程' }).fill('相机 A/B');
  await page.getByRole('button', { name: '搜索', exact: true }).click();
  await expect(page.getByRole('cell', { name: '搜索：相机 A/B', exact: true })).toBeVisible();
  await expect.poll(() => audit.some(entry => entry.path.includes('keyword=%E7%9B%B8%E6%9C%BA+A%2FB'))).toBe(true);
  expect(expectGetOnly(audit)).toBe(true);
});

test('Project detail displays counts, decision and assets only from the detail payload', async ({ page }) => {
  const audit = await bootProjects(page, [summary()]);
  await page.getByRole('link', { name: '查看详情' }).first().click();

  await expect(page.locator('[data-capability="projects-read-detail"]')).toBeVisible();
  await expect(page.getByText('算子数量', { exact: true })).toBeVisible();
  await expect(page.getByText('连接数量', { exact: true })).toBeVisible();
  await expect(page.getByText('已配置（缺失策略：Undetermined）')).toBeVisible();
  await expect(page.getByText('标定资源')).toBeVisible();
  expect(expectGetOnly(audit)).toBe(true);
});

test('Projects surfaces malformed list and missing detail as localized product states', async ({ page }) => {
  await bootProjects(page, { items: [] });
  await expect(page.getByText('工程列表读取失败')).toBeVisible();

  await page.goto(`/studio/index.html#/projects/${missingProjectId}`);
  await expect(page.getByText('工程不存在')).toBeVisible();
});
