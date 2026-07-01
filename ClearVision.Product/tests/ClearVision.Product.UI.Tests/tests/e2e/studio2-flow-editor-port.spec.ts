import { existsSync } from 'node:fs';
import {
  extname,
  join,
  resolve
} from 'node:path';
import { expect, test, type Page } from '@playwright/test';

const repositoryRoot = resolve(process.cwd(), '..', '..', '..');
const configuration = process.env.Configuration ?? process.env.CONFIGURATION ?? 'Debug';
const targetFramework = process.env.TargetFramework ?? process.env.TARGET_FRAMEWORK ?? 'net8.0-windows';
const frontendV2Dist = join(
  repositoryRoot,
  'ClearVision.Product',
  'src',
  'ClearVision.Product.Desktop',
  'obj',
  configuration,
  targetFramework,
  'FrontendV2',
  'dist'
);

test('Studio 2.0 Flow Editor Port commits a real node parameter draft and rejects stale commits', async ({ page }) => {
  await installStudio2FrontendV2Routes(page);

  await page.addInitScript(() => {
    window.__CLEARVISION_STARTUP__ = {
      workspaceV2Enabled: true,
      apiBaseUrl: 'http://127.0.0.1:5000/api',
      hostKind: 'playwright-browser',
      frontendV2BasePath: '/v2'
    };
    window.__API_BASE_URL__ = 'http://127.0.0.1:5000/api';
  });

  await page.goto('/v2/index.html');
  await expect(page.locator('.studio2-workspace-shell')).toBeVisible();

  const initial = await page.evaluate(async () => {
    const { default: serviceRegistry } = await import('/src/core/app/serviceRegistry.js');
    const port = serviceRegistry.get('studio2.flowEditorPort');
    if (!port) {
      return { error: 'missing-port' };
    }

    const replace = port.replaceFlow({
      projectId: 'project-a',
      requestSequence: 1,
      flow: createBrowserFlow()
    });
    const select = port.selectNode({
      projectId: 'project-a',
      requestSequence: 2,
      nodeId: 'node-a'
    });

    return {
      replace,
      select,
      snapshot: port.getSnapshot(),
      exposesRaw: Object.prototype.hasOwnProperty.call(port, 'raw'),
      exposesNodes: Object.prototype.hasOwnProperty.call(port, 'nodes')
    };

    function createBrowserFlow() {
      return {
        operators: [
          {
            id: 'node-a',
            type: 'Thresholding',
            title: 'Threshold',
            x: 20,
            y: 24,
            inputPorts: [],
            outputPorts: [],
            parameters: [
              {
                name: 'Threshold',
                displayName: 'Threshold',
                value: 10,
                dataType: 'int'
              }
            ]
          },
          {
            id: 'node-b',
            type: 'Blur',
            title: 'Blur',
            x: 220,
            y: 24,
            inputPorts: [],
            outputPorts: [],
            parameters: [
              {
                name: 'Sigma',
                displayName: 'Sigma',
                value: 1,
                dataType: 'float'
              }
            ]
          }
        ],
        connections: []
      };
    }
  });

  expect(initial).not.toHaveProperty('error');
  expect(initial.replace.disposition).toBe('accepted');
  expect(initial.select.disposition).toBe('accepted');
  expect(initial.exposesRaw).toBe(false);
  expect(initial.exposesNodes).toBe(false);

  const thresholdInput = page.locator('.studio2-flow-port-panel__field input[name="Threshold"]');
  await expect(thresholdInput).toHaveValue('10');
  await thresholdInput.fill('21');
  await page.locator('.studio2-flow-port-panel__actions button[type="submit"]').click();
  await expect(thresholdInput).toHaveValue('21');

  const committed = await readPortSnapshot(page);
  expect(getParameterValue(committed, 'node-a', 'Threshold')).toBe(21);
  expect(committed.flowRevision).toBe(initial.snapshot.flowRevision + 1);

  const staleResult = await page.evaluate(async () => {
    const { default: serviceRegistry } = await import('/src/core/app/serviceRegistry.js');
    const port = serviceRegistry.get('studio2.flowEditorPort');
    const sequenceBase = Date.now() + 1000;

    port.selectNode({
      projectId: 'project-a',
      requestSequence: sequenceBase,
      nodeId: 'node-a'
    });
    const draftSnapshot = port.getSnapshot();
    port.selectNode({
      projectId: 'project-a',
      requestSequence: sequenceBase + 1,
      nodeId: 'node-b'
    });
    const staleCommit = port.patchParameters({
      projectId: 'project-a',
      requestSequence: sequenceBase + 2,
      expectedFlowRevision: draftSnapshot.flowRevision,
      expectedSelectionRevision: draftSnapshot.selectionRevision,
      nodeId: 'node-a',
      parameters: {
        Threshold: 99
      }
    });

    return {
      staleCommit,
      snapshot: port.getSnapshot()
    };
  });

  expect(staleResult.staleCommit.accepted).toBe(false);
  expect(staleResult.staleCommit.disposition).toBe('stale_selection');
  expect(getParameterValue(staleResult.snapshot, 'node-a', 'Threshold')).toBe(21);
});

test('legacy page remains flag-off and does not mount the Studio 2.0 Vue root', async ({ page }) => {
  await page.addInitScript(() => {
    window.__API_BASE_URL__ = 'http://127.0.0.1:5000/api';
    window.sessionStorage.setItem('cv_auth_token', 'playwright-token');
    window.sessionStorage.setItem('cv_current_user', JSON.stringify({
      username: 'playwright',
      displayName: 'Playwright',
      role: 'Admin'
    }));
  });
  await page.route('**/api/auth/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        username: 'playwright',
        displayName: 'Playwright',
        role: 'Admin'
      })
    });
  });

  await page.goto('/index.html', { waitUntil: 'domcontentloaded' });

  await expect(page.locator('#flow-canvas')).toBeAttached();
  await expect(page.locator('#studio2-v2-root')).toHaveCount(0);
});

async function installStudio2FrontendV2Routes(page: Page): Promise<void> {
  test.skip(!existsSync(join(frontendV2Dist, 'index.html')), `FrontendV2 dist not found: ${frontendV2Dist}`);

  await page.route('**/health', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'Healthy', port: 5000 })
    });
  });

  await page.route('**/v2/**', async (route) => {
    const requestUrl = new URL(route.request().url());
    const relativeAssetPath = decodeURIComponent(requestUrl.pathname.replace(/^\/v2\/?/, '')) || 'index.html';
    const assetPath = resolve(frontendV2Dist, relativeAssetPath);

    if (!assetPath.startsWith(frontendV2Dist) || !existsSync(assetPath)) {
      await route.fulfill({
        status: 404,
        contentType: 'text/plain',
        body: `Missing V2 asset: ${relativeAssetPath}`
      });
      return;
    }

    await route.fulfill({
      path: assetPath,
      contentType: resolveContentType(assetPath)
    });
  });
}

async function readPortSnapshot(page: Page): Promise<StudioFlowEditorBrowserSnapshot> {
  return await page.evaluate(async () => {
    const { default: serviceRegistry } = await import('/src/core/app/serviceRegistry.js');
    const port = serviceRegistry.get('studio2.flowEditorPort');
    return port.getSnapshot();
  });
}

function getParameterValue(
  snapshot: StudioFlowEditorBrowserSnapshot,
  nodeId: string,
  parameterName: string
): unknown {
  const flow = snapshot.flow;
  return flow.operators
    .find((operator) => operator.id === nodeId)
    ?.parameters
    .find((parameter) => parameter.name === parameterName)
    ?.value;
}

function resolveContentType(assetPath: string): string {
  const extension = extname(assetPath).toLowerCase();
  if (extension === '.html') {
    return 'text/html';
  }
  if (extension === '.js') {
    return 'text/javascript';
  }
  if (extension === '.css') {
    return 'text/css';
  }
  if (extension === '.json') {
    return 'application/json';
  }
  if (extension === '.svg') {
    return 'image/svg+xml';
  }
  return 'application/octet-stream';
}

interface StudioFlowEditorBrowserSnapshot {
  readonly flowRevision: number;
  readonly selectionRevision: number;
  readonly selectedNodeId: string | null;
  readonly flow: {
    readonly operators: Array<{
      readonly id: string;
      readonly parameters: Array<{
        readonly name: string;
        readonly value: unknown;
      }>;
    }>;
  };
}
