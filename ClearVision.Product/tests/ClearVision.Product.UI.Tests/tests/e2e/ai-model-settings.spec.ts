import { test, expect } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const configurations = [
  { id: 'local-compatible', name: '本地兼容服务', provider: 'OpenAI Compatible', protocol: 'openai_compatible', baseUrl: 'http://127.0.0.1:11434/v1', authMode: 'none' },
  { id: 'ollama', name: 'Ollama 服务', provider: 'OpenAI Compatible', protocol: 'ollama_native', baseUrl: 'http://127.0.0.1:11434', authMode: 'none' },
  { id: 'cloud', name: '云端服务', provider: 'OpenAI API', protocol: 'openai_compatible', baseUrl: 'https://example.invalid/v1', authMode: 'bearer' }
];

test('one LLM list preserves local, Ollama, and cloud configuration operations', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  const models: any[] = configurations.map((config, index) => ({ ...config, model: 'test-model',
    hasApiKey: config.authMode !== 'none', isActive: index === 0, isEnabled: true,
    wireApi: 'chat_completions', roleBindings: ['generation'], priority: 100, timeoutMs: 120000 }));
  const saves: any[] = [];
  const tested: string[] = [];
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    const modelMatch = url.pathname.match(/^\/api\/ai\/models\/([^/]+)(?:\/(test|activate))?$/);
    let json: any = [];
    if (url.pathname === '/api/settings') json = { general: { theme: 'light', softwareTitle: 'ClearVision' } };
    else if (url.pathname === '/api/ai/reasoning-support') json = { familyName: 'Compatible', familyId: 'compatible', allowedModes: ['auto'], allowedEfforts: ['medium'] };
    else if (url.pathname === '/api/ai/models') {
      if (method === 'POST') { const model = { ...route.request().postDataJSON(), id: 'created-model' }; models.push(model); json = model; }
      else json = models;
    } else if (modelMatch) {
      const model = models.find(item => item.id === modelMatch[1]);
      if (method === 'PUT') { const payload = route.request().postDataJSON(); saves.push({ id: model.id, ...payload }); Object.assign(model, payload); json = model; }
      else if (modelMatch[2] === 'test') { tested.push(model.id); json = { connectionOk: true, sanitizedMessage: '测试通过', latencyMs: 25 }; }
      else if (modelMatch[2] === 'activate') { models.forEach(item => { item.isActive = item.id === model.id; }); json = model; }
    }
    await route.fulfill({ json });
  });
  await bootAuthenticatedApp(page);
  await page.locator('.nav-btn[data-view="settings"]').click();
  await page.locator('.settings-menu-item[data-tab="ai"]').click();
  await expect(page.getByRole('heading', { name: '大语言模型', exact: true })).toHaveCount(1);
  await expect(page.locator('[data-section="ai"]')).not.toContainText('本地模型');
  await expect(page.locator('#ai-models-table tbody tr')).toHaveCount(3);
  await expect(page.getByRole('button', { name: '添加模型', exact: true })).toBeEnabled();
  for (const config of configurations) {
    await page.getByRole('button', { name: `编辑模型 ${config.name}`, exact: true }).click();
    await expect(page.locator('#cfg-ai-protocol')).toHaveValue(config.protocol);
    await expect(page.locator('#cfg-ai-baseurl')).toHaveValue(config.baseUrl);
    await page.locator('#cfg-ai-display-name').fill(`${config.name}已编辑`);
    await page.locator('[data-ai-role="planner"]').check();
    await page.locator('#btn-ai-test').click();
    await expect.poll(() => tested.includes(config.id)).toBe(true);
    expect(saves.findLast(item => item.id === config.id)).toMatchObject({ protocol: config.protocol, baseUrl: config.baseUrl,
      authMode: config.authMode, roleBindings: ['generation', 'planner'], isEnabled: true, apiKeyOperation: 'keep' });
    await page.locator('#btn-ai-save').click();
    await expect(page.locator('#ai-models-table tbody tr').filter({ hasText: config.name })).toContainText('已启用');
    await expect(page.locator('#cfg-ai-display-name')).toHaveValue(`${config.name}已编辑`);
  }
  await page.getByRole('button', { name: '添加模型', exact: true }).press('Enter');
  await expect(page.locator('#ai-models-table tbody tr')).toHaveCount(4);
  await expect(page.locator('#cfg-ai-name')).toHaveValue('新建模型');
  expect(models.find(item => item.id === 'created-model').protocol).toBe('openai_compatible');
});
