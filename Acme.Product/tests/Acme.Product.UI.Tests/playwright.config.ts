import { defineConfig, devices } from '@playwright/test';

const noProxyEntries = new Set(
  [process.env.NO_PROXY, process.env.no_proxy, '127.0.0.1', 'localhost', '::1']
    .flatMap(value => (value ?? '').split(','))
    .map(value => value.trim())
    .filter(Boolean),
);
process.env.NO_PROXY = Array.from(noProxyEntries).join(',');
process.env.no_proxy = process.env.NO_PROXY;

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL: 'http://127.0.0.1:5000',
    trace: 'on-first-retry',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: 'node ./node_modules/http-server/bin/http-server ../../src/Acme.Product.Desktop/wwwroot -p 5000 -a 127.0.0.1',
    url: 'http://127.0.0.1:5000/index.html',
    reuseExistingServer: !process.env.CI,
    timeout: 120 * 1000,
  },
});
