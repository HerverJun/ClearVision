import { resolve } from 'node:path';
import { defineConfig, devices } from '@playwright/test';

const noProxyEntries = new Set(
  [process.env.NO_PROXY, process.env.no_proxy, '127.0.0.1', 'localhost', '::1']
    .flatMap(value => (value ?? '').split(','))
    .map(value => value.trim())
    .filter(Boolean),
);
process.env.NO_PROXY = Array.from(noProxyEntries).join(',');
process.env.no_proxy = process.env.NO_PROXY;

const scenario = process.env.CV_UI_SCENARIO?.trim() || 'legacy';
const isStudioUiNext = scenario === 'studio-ui-next';
const host = '127.0.0.1';
const defaultPort = isStudioUiNext ? 5177 : 5000;
const parsedPort = Number.parseInt(process.env.CV_UI_PORT ?? '', 10);
const port = Number.isInteger(parsedPort) && parsedPort > 0 && parsedPort <= 65535
  ? parsedPort
  : defaultPort;
const configuredBaseUrl = process.env.CV_UI_BASE_URL?.trim();
const origin = configuredBaseUrl
  ? new URL(configuredBaseUrl).origin
  : 'http://' + host + ':' + port;
const readyPath = isStudioUiNext
  ? '/studio/index.html'
  : '/index.html';
const defaultLegacyWebRoot = resolve(
  __dirname,
  '../../src/ClearVision.Product.Desktop/wwwroot'
);
const webRoot = process.env.CV_UI_WEB_ROOT?.trim() ||
  (isStudioUiNext ? '' : defaultLegacyWebRoot);
const serverCommand = isStudioUiNext
  ? 'node ./tests/support/studio-ui-next-server.cjs'
  : 'node ./node_modules/http-server/bin/http-server "' + webRoot +
    '" -p ' + port + ' -a ' + host;
const studioUiNextManagedServer = isStudioUiNext && !configuredBaseUrl;
const htmlReportOutput = resolve(
  __dirname,
  '../../../.tmp/playwright-reports/clearvision-product-ui'
);
process.env.CV_UI_PORT = String(port);
process.env.CV_UI_HOST = host;

export default defineConfig({
  testDir: './tests/e2e',
  testMatch: isStudioUiNext
    ? ['**/studio-ui-next/**/*.spec.ts']
    : ['**/*.spec.ts'],
  testIgnore: isStudioUiNext
    ? []
    : ['**/studio-ui-next/**/*.spec.ts'],
  grepInvert: isStudioUiNext ? /@r2-final/ : undefined,
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: [['html', { outputFolder: htmlReportOutput, open: 'never' }]],
  use: {
    baseURL: origin,
    trace: 'on-first-retry',
  },
  globalSetup: studioUiNextManagedServer
    ? resolve(__dirname, 'tests/support/studio-ui-next-global-setup.cjs')
    : undefined,
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: configuredBaseUrl || isStudioUiNext
    ? undefined
    : {
        command: serverCommand,
        url: origin + readyPath,
        reuseExistingServer: !process.env.CI && !isStudioUiNext,
        timeout: 180 * 1000,
      },
});
