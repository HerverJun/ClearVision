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
const optionDG1VisualPhase = process.env.CV_OPTION_D_G1_VISUAL_PHASE?.trim();
const optionDG1GateInvocationId = process.env.CV_OPTION_D_G1_GATE_INVOCATION_ID?.trim();
const optionDG1VisualRequested = process.argv.some(argument =>
  argument.replaceAll('\\', '/').includes('option-d-g1-visual.spec.ts'));
if (optionDG1VisualRequested && (!optionDG1VisualPhase || !optionDG1GateInvocationId)) {
  throw new Error(
    'Option D G1 visual evidence requires its phase and gate invocation ID. Use the dedicated reference or candidate gate.'
  );
}
const optionDG1VisualEnabled = optionDG1VisualRequested
  && Boolean(optionDG1VisualPhase)
  && Boolean(optionDG1GateInvocationId);
const optionDG2VisualPhase = process.env.CV_OPTION_D_G2_VISUAL_PHASE?.trim();
const optionDG2GateInvocationId = process.env.CV_OPTION_D_G2_GATE_INVOCATION_ID?.trim();
const optionDG2VisualRequested = process.argv.some(argument =>
  argument.replaceAll('\\', '/').includes('option-d-g2-visual.spec.ts'));
if (optionDG2VisualRequested && (!optionDG2VisualPhase || !optionDG2GateInvocationId)) {
  throw new Error(
    'Option D G2 visual evidence requires its phase and gate invocation ID. Use the dedicated reference or candidate gate.'
  );
}
const optionDG2VisualEnabled = optionDG2VisualRequested
  && Boolean(optionDG2VisualPhase)
  && Boolean(optionDG2GateInvocationId);
const optionDG3VisualPhase = process.env.CV_OPTION_D_G3_VISUAL_PHASE?.trim();
const optionDG3GateInvocationId = process.env.CV_OPTION_D_G3_GATE_INVOCATION_ID?.trim();
const optionDG3VisualRequested = process.argv.some(argument =>
  argument.replaceAll('\\', '/').includes('option-d-g3-visual.spec.ts'));
if (optionDG3VisualRequested && (!optionDG3VisualPhase || !optionDG3GateInvocationId)) {
  throw new Error(
    'Option D G3 visual evidence requires its phase and gate invocation ID. Use the dedicated reference or candidate gate.'
  );
}
const optionDG3VisualEnabled = optionDG3VisualRequested
  && Boolean(optionDG3VisualPhase)
  && Boolean(optionDG3GateInvocationId);
const studioUiNextVisualIgnores = [
  ...(!optionDG1VisualEnabled ? ['**/studio-ui-next/option-d-g1-visual.spec.ts'] : []),
  ...(!optionDG2VisualEnabled ? ['**/studio-ui-next/option-d-g2-visual.spec.ts'] : []),
  ...(!optionDG3VisualEnabled ? ['**/studio-ui-next/option-d-g3-visual.spec.ts'] : [])
];
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
    ? studioUiNextVisualIgnores
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
