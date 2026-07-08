import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, Page, test } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const screenshotDir = resolve(process.cwd(), 'test-results', 'project-page-capability-owner');

type ProjectRecord = {
  id: string;
  name: string;
  description: string;
  createdAt: string;
  modifiedAt: string;
};

function screenshotPath(name: string) {
  mkdirSync(screenshotDir, { recursive: true });
  return resolve(screenshotDir, name);
}

function createProjectState() {
  return {
    nextId: 2,
    projects: [
      {
        id: 'project-existing-1',
        name: '既有工程',
        description: '用于删除确认前的列表状态',
        createdAt: '2026-07-08T00:00:00.000Z',
        modifiedAt: '2026-07-08T00:00:00.000Z',
      },
    ] as ProjectRecord[],
    createRequests: [] as any[],
    deleteRequests: [] as string[],
  };
}

async function installProjectPageFlag(page: Page) {
  await page.addInitScript(() => {
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: Object.freeze({
        featureFlags: Object.freeze({
          'Studio2.ProjectPage': true,
          'Studio2.PropertyPanel': true,
          'Studio2.PreviewPanel': true,
        }),
      }),
      writable: false,
      configurable: false,
    });
  });
}

async function installApiRoutes(page: Page, state: ReturnType<typeof createProjectState>) {
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const pathname = url.pathname;

    const fulfillJson = (body: unknown, status = 200) => route.fulfill({
      status,
      contentType: 'application/json',
      body: JSON.stringify(body),
    });

    if (pathname === '/api/auth/me') {
      await fulfillJson({ username: 'admin', displayName: 'E2E Admin', role: 'Admin' });
      return;
    }

    if (pathname === '/api/health' || pathname.endsWith('/health')) {
      await fulfillJson({ status: 'ok' });
      return;
    }

    if (pathname === '/api/projects' && request.method() === 'GET') {
      await fulfillJson(state.projects);
      return;
    }

    if (pathname === '/api/projects' && request.method() === 'POST') {
      const payload = JSON.parse(request.postData() || '{}');
      state.createRequests.push(payload);
      const now = new Date('2026-07-08T00:00:00.000Z').toISOString();
      const project = {
        id: `project-created-${state.nextId++}`,
        name: payload.name,
        description: payload.description || '',
        createdAt: now,
        modifiedAt: now,
      };
      state.projects = [project, ...state.projects];
      await fulfillJson(project);
      return;
    }

    if (pathname === '/api/projects/recent' || pathname.endsWith('/projects/recent')) {
      await fulfillJson(state.projects.slice(0, 10));
      return;
    }

    if (pathname === '/api/projects/search') {
      const keyword = url.searchParams.get('keyword')?.toLowerCase() || '';
      await fulfillJson(state.projects.filter(project => project.name.toLowerCase().includes(keyword)));
      return;
    }

    const projectIdMatch = pathname.match(/^\/api\/projects\/([^/]+)$/);
    if (projectIdMatch && request.method() === 'GET') {
      const project = state.projects.find(item => item.id === decodeURIComponent(projectIdMatch[1]));
      await fulfillJson(project || {}, project ? 200 : 404);
      return;
    }

    if (projectIdMatch && request.method() === 'DELETE') {
      const projectId = decodeURIComponent(projectIdMatch[1]);
      state.deleteRequests.push(projectId);
      state.projects = state.projects.filter(project => project.id !== projectId);
      await fulfillJson({ ok: true });
      return;
    }

    if (pathname.endsWith('/operators/library')) {
      await fulfillJson([]);
      return;
    }

    if (pathname.endsWith('/operators/types')) {
      await fulfillJson([]);
      return;
    }

    if (pathname.endsWith('/settings')) {
      await fulfillJson({});
      return;
    }

    if (pathname.endsWith('/settings/disk-usage')) {
      await fulfillJson({ canWrite: true, totalBytes: 1024, availableBytes: 1024 });
      return;
    }

    if (pathname.endsWith('/settings/database/status')) {
      await fulfillJson({ status: 'ok', migrations: [], tables: [] });
      return;
    }

    if (pathname.endsWith('/users')) {
      await fulfillJson([]);
      return;
    }

    if (request.method() !== 'GET') {
      await fulfillJson({ ok: true });
      return;
    }

    await fulfillJson({});
  });
}

async function bootProjectPage(page: Page, state: ReturnType<typeof createProjectState>) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installProjectPageFlag(page);
  await installApiRoutes(page, state);
  await bootAuthenticatedApp(page);
  await page.locator('.nav-btn[data-view="project"]').click();
  await expect(page.locator('.nav-btn[data-view="project"]')).toHaveClass(/active/);
  await expect(page.locator('#project-view[data-project-page-owner="project-page-capability-v2"]')).toBeVisible();
  await expect(page.locator('[data-project-action="new"]')).toBeVisible();
}

test.describe('ProjectPageCapabilityOwner modal interactions', () => {
  test('new and delete project use ClearVision modals without browser dialogs', async ({ page }) => {
    const state = createProjectState();
    const dialogs: string[] = [];
    const consoleErrors: string[] = [];

    page.on('dialog', async dialog => {
      dialogs.push(`${dialog.type()}: ${dialog.message()}`);
      await dialog.dismiss().catch(() => {});
    });
    page.on('console', message => {
      if (message.type() === 'error') {
        consoleErrors.push(message.text());
      }
    });
    page.on('pageerror', error => {
      consoleErrors.push(error.message);
    });

    await bootProjectPage(page, state);

    await page.locator('[data-project-action="new"]').click();
    const createModal = page.locator('[data-project-create-modal]');
    await expect(createModal).toBeVisible();
    await expect(createModal.locator('.cv-modal-title')).toHaveText('新建工程');
    await expect(createModal.locator('[data-project-name-input]')).toBeVisible();
    await expect(createModal.locator('[data-project-desc-input]')).toBeVisible();
    await page.screenshot({ path: screenshotPath('01-new-project-modal.png'), fullPage: true });

    await createModal.locator('[data-project-modal-action="create"]').click();
    await expect(createModal.locator('[data-project-name-error]')).toHaveText('请输入工程名称');
    await expect(createModal).toBeVisible();
    await page.screenshot({ path: screenshotPath('02-empty-name-validation.png'), fullPage: true });
    expect(state.createRequests).toEqual([]);

    await createModal.locator('[data-project-name-input]').fill('E2E 工程页新建');
    await createModal.locator('[data-project-desc-input]').fill('由 ProjectPageCapabilityOwner modal 创建');
    await createModal.locator('[data-project-modal-action="create"]').click();
    await expect(createModal).toHaveCount(0);
    await expect(page.locator('.nav-btn[data-view="flow"]')).toHaveClass(/active/);
    await page.locator('.nav-btn[data-view="project"]').click();
    await expect(page.locator('.nav-btn[data-view="project"]')).toHaveClass(/active/);
    await expect(page.locator('#project-view[data-project-page-owner="project-page-capability-v2"]')).toBeVisible();

    const createdRow = page.locator('.project-list-item').filter({ hasText: 'E2E 工程页新建' });
    await expect(createdRow).toBeVisible();
    await expect(createdRow).toContainText('由 ProjectPageCapabilityOwner modal 创建');
    await page.screenshot({ path: screenshotPath('03-created-project-list-refresh.png'), fullPage: true });

    await createdRow.locator('[data-project-action="delete"]').click();
    const deleteModal = page.locator('[data-project-delete-modal]');
    await expect(deleteModal).toBeVisible();
    await expect(deleteModal.locator('.cv-modal-title')).toHaveText('确认删除');
    await expect(deleteModal.locator('[data-project-delete-name]')).toHaveText('E2E 工程页新建');
    await expect(deleteModal.locator('[data-project-delete-id]')).toHaveText(state.projects[0].id);
    await page.screenshot({ path: screenshotPath('04-delete-confirm-modal.png'), fullPage: true });

    await deleteModal.locator('[data-project-modal-action="confirm-delete"]').click();
    await expect(deleteModal).toHaveCount(0);
    await expect(createdRow).toHaveCount(0);

    expect(state.createRequests).toEqual([
      {
        name: 'E2E 工程页新建',
        description: '由 ProjectPageCapabilityOwner modal 创建',
      },
    ]);
    expect(state.deleteRequests).toEqual(['project-created-2']);
    expect(dialogs).toEqual([]);
    expect(consoleErrors).toEqual([]);
  });
});
