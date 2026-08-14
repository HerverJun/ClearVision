import { resolve } from 'node:path';
import { defineConfig, devices } from '@playwright/test';

process.env.CV_UI_HOST = '127.0.0.1';
process.env.CV_UI_PORT = process.env.CV_UI_PORT?.trim() || '5318';
process.env.CV_STUDIO_UI_EVIDENCE_PHASE = 'r2';

const uiTestsRoot = resolve(__dirname, '../../../..');
const port = Number(process.env.CV_UI_PORT);
const runId = process.env.CV_R2_MATRIX_RUN_ID?.trim() || 'unbound-run';
const phase = process.env.CV_R2_CAPTURE_PHASE?.trim() || 'unbound-phase';

export default defineConfig({
  testDir: resolve(__dirname, '..'),
  testMatch: [
    'f02-operators.spec.ts',
    'f02-overview.spec.ts',
    'f02-projects-read.spec.ts',
    'f02-results.spec.ts',
    'f02-stations.spec.ts',
    'f02-support-surfaces.spec.ts',
    'f03-workspace.spec.ts',
    'f05-inspection-run.spec.ts',
    'f06-ai-workbench.spec.ts',
    'f07-settings-shell.spec.ts',
    'r2-visual/r2-visual.spec.ts'
  ],
  grep: /@r2-final/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  reporter: [['list']],
  outputDir: resolve(
    uiTestsRoot,
    '../../../.tmp/studio-ui-next/r2/matrix-playwright-results',
    runId,
    phase
  ),
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    trace: 'retain-on-failure',
    screenshot: 'off',
    ...devices['Desktop Chrome']
  },
  globalSetup: resolve(uiTestsRoot, 'tests/support/studio-ui-next-global-setup.cjs'),
  projects: [{ name: 'r2-matrix-chromium', use: { ...devices['Desktop Chrome'] } }]
});
