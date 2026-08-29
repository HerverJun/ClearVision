import { expect, test } from '@playwright/test';

test('browser local drafts remain isolated across logout and user switch', async ({ page }) => {
  await page.route('**/api/auth/setup-status', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ requiresInitialAdminSetup: false }),
    });
  });
  await page.goto('/login.html');

  const result = await page.evaluate(async () => {
    localStorage.clear();
    const { localDraftStorage } = await import('/src/features/project/localDraftStorage.js');
    const project = { id: 'shared-project', name: 'Shared Project' };
    const flowA = { operators: [{ id: 'private-a' }], connections: [] };

    window.currentUser = { userId: 'user-a' };
    localDraftStorage.write(project, flowA, { source: 'playwright' });

    window.currentUser = null;
    const afterLogout = localDraftStorage.read(project.id);

    window.currentUser = { userId: 'user-b' };
    const visibleToB = localDraftStorage.read(project.id);
    localDraftStorage.clear(project.id);

    window.currentUser = { userId: 'user-a' };
    const restoredForA = localDraftStorage.read(project.id);

    return {
      afterLogout,
      visibleToB,
      restoredNodeId: restoredForA?.flow?.operators?.[0]?.id ?? null,
    };
  });

  expect(result.afterLogout).toBeNull();
  expect(result.visibleToB).toBeNull();
  expect(result.restoredNodeId).toBe('private-a');
});
