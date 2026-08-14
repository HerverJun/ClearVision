import { resolve } from 'node:path';
import { defineConfig, devices } from '@playwright/test';

process.env.CV_UI_HOST = '127.0.0.1';
process.env.CV_UI_PORT = process.env.CV_UI_PORT?.trim() || '5317';
process.env.CV_STUDIO_UI_EVIDENCE_PHASE = 'r2';

const uiTestsRoot = resolve(__dirname, '../../../..');
const port = Number(process.env.CV_UI_PORT);

export default defineConfig({
  testDir: '.',
  testMatch: ['r2-visual.spec.ts', 'r2-motion.spec.ts'],
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  reporter: [['list']],
  outputDir: resolve(uiTestsRoot, '../../../.tmp/studio-ui-next/r2/playwright-results'),
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    trace: 'retain-on-failure',
    screenshot: 'off',
    ...devices['Desktop Chrome']
  },
  globalSetup: resolve(uiTestsRoot, 'tests/support/studio-ui-next-global-setup.cjs'),
  projects: [{ name: 'r2-chromium', use: { ...devices['Desktop Chrome'] } }]
});
