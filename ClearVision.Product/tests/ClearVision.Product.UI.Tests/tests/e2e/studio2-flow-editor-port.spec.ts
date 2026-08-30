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
      requestSequence: port.nextRequestSequence('project-a'),
      flow: createBrowserFlow()
    });
    const select = port.selectNode({
      projectId: 'project-a',
      requestSequence: port.nextRequestSequence('project-a'),
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

  await thresholdInput.fill('33');
  await expect(thresholdInput).toHaveValue('33');
  const beforeDrag = await readPortSnapshot(page);
  const canvas = page.locator('#studio2-flow-canvas');
  await expect(canvas).toBeVisible();
  const canvasBox = await canvas.boundingBox();
  expect(canvasBox).not.toBeNull();
  if (!canvasBox) {
    throw new Error('Studio 2.0 flow canvas did not expose a bounding box.');
  }

  await page.mouse.move(canvasBox.x + 34, canvasBox.y + 38);
  await page.mouse.down();
  await page.mouse.move(canvasBox.x + 112, canvasBox.y + 86, { steps: 4 });
  await page.mouse.up();

  await expect(page.locator('.studio2-flow-port-panel__stale')).toBeVisible();
  await expect(thresholdInput).toHaveValue('33');
  await page.locator('.studio2-flow-port-panel__actions button[type="submit"]').click();
  await expect(page.locator('.studio2-flow-port-panel__disposition')).toContainText('stale_flow_revision');

  const staleSnapshot = await readPortSnapshot(page);
  expect(getParameterValue(staleSnapshot, 'node-a', 'Threshold')).toBe(21);
  expect(staleSnapshot.flowRevision).toBe(beforeDrag.flowRevision + 1);

  await page.locator('.studio2-flow-port-panel__actions button[type="button"]').click();
  await expect(thresholdInput).toHaveValue('21');
});

test('Studio 2.0 Project Persistence Port saves metadata and flow without resubmitting schema', async ({ page }) => {
  const apiCalls: ProjectApiCall[] = [];
  await page.route('**/api/projects/project-a', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(createProjectApiFixture(1))
      });
      return;
    }

    if (request.method() === 'PUT') {
      const body = request.postDataJSON() as ProjectSavePayload;
      apiCalls.push({
        method: 'PUT',
        url: request.url(),
        body
      });
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          ...createProjectApiFixture(2),
          flow: body.flow
        })
      });
      return;
    }

    await route.fulfill({
      status: 405,
      contentType: 'text/plain',
      body: 'Method not allowed'
    });
  });
  await page.route('**/api/projects/project-a/flow', async (route) => {
    apiCalls.push({
      method: route.request().method(),
      url: route.request().url(),
      body: route.request().postData()
    });
    await route.fulfill({
      status: 500,
      contentType: 'text/plain',
      body: 'Unexpected flow endpoint call'
    });
  });
  await page.route('**/api/projects/project-a/global-variables', async (route) => {
    apiCalls.push({
      method: route.request().method(),
      url: route.request().url(),
      body: route.request().postData()
    });
    await route.fulfill({
      status: 500,
      contentType: 'text/plain',
      body: 'Unexpected global variables endpoint call'
    });
  });
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

  const opened = await page.evaluate(async () => {
    const { default: serviceRegistry } = await import('/src/core/app/serviceRegistry.js');
    const projectPort = serviceRegistry.get('studio2.projectPersistencePort');
    const flowPort = serviceRegistry.get('studio2.flowEditorPort');
    if (!projectPort || !flowPort) {
      return { error: 'missing-port' };
    }

    const openResult = await projectPort.openProject('project-a');
    const selectResult = flowPort.selectNode({
      projectId: 'project-a',
      requestSequence: flowPort.nextRequestSequence('project-a'),
      nodeId: 'node-a'
    });
    return {
      openResult,
      selectResult,
      projectSnapshot: projectPort.getSnapshot(),
      flowSnapshot: flowPort.getSnapshot()
    };
  });

  expect(opened).not.toHaveProperty('error');
  expect(opened.openResult.disposition).toBe('accepted');
  expect(opened.selectResult.disposition).toBe('accepted');
  await expect(page.locator('.studio2-project-port-panel__meta')).toContainText('project-a');
  await expect(page.locator('.studio2-project-port-panel__meta')).toContainText('1');

  const thresholdInput = page.locator('.studio2-flow-port-panel__field input[name="Threshold"]');
  await expect(thresholdInput).toHaveValue('10');
  await thresholdInput.fill('21');
  await page.locator('.studio2-flow-port-panel__actions button[type="submit"]').click();
  await expect(thresholdInput).toHaveValue('21');
  await expect(page.locator('.studio2-project-port-panel__meta')).toContainText('true');

  await page.locator('.studio2-project-port-panel__save').click();
  await expect(page.locator('.studio2-project-port-panel__disposition')).toContainText('accepted');
  await expect(page.locator('.studio2-project-port-panel__meta')).toContainText('2');

  const projectPutCalls = apiCalls.filter(isProjectPutCall);
  expect(projectPutCalls).toHaveLength(1);
  expect(apiCalls.some((call) => call.url.includes('/flow'))).toBe(false);
  expect(apiCalls.some((call) => call.url.includes('/global-variables'))).toBe(false);
  const projectPut = projectPutCalls[0];
  if (!projectPut) {
    throw new Error('Expected one project PUT call.');
  }

  expect(projectPut.body.expectedPersistenceRevision).toBe(1);
  expect(projectPut.body).not.toHaveProperty('globalVariables');
  expect(getParameterValueFromFlow(projectPut.body.flow, 'node-a', 'Threshold')).toBe(21);
});

test('legacy page remains flag-off and does not mount the Studio 2.0 Vue root', async ({ page }) => {
  await page.addInitScript(() => {
    window.__API_BASE_URL__ = 'http://127.0.0.1:5000/api';
    window.sessionStorage.setItem('cv_auth_token', 'playwright-token');
    window.sessionStorage.setItem('cv_current_user', JSON.stringify({
      userId: 'playwright-user',
      username: 'playwright',
      displayName: 'Playwright',
      role: 'Admin',
      capabilities: ['project.edit']
    }));
  });
  await page.route('**/api/auth/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        userId: 'playwright-user',
        username: 'playwright',
        displayName: 'Playwright',
        role: 'Admin',
        capabilities: ['project.edit'],
        passwordPolicy: { minimumLength: 12 }
      })
    });
  });

  await page.goto('/index.html', { waitUntil: 'domcontentloaded' });

  await expect(page.locator('#flow-canvas')).toBeAttached();
  await expect(page.locator('#studio2-v2-root')).toHaveCount(0);
});

test('legacy flow editor connection toast reads alternate port type fields', async ({ page }) => {
  await page.route('**/flow-editor-interaction-host.html', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'text/html',
      body: '<!doctype html><meta charset="utf-8"><canvas id="flow-canvas" width="800" height="500"></canvas>'
    });
  });

  await page.goto('/flow-editor-interaction-host.html', { waitUntil: 'domcontentloaded' });

  const toastMessage = await page.evaluate(async () => {
    const { FlowEditorInteraction } = await import('/src/features/flow-editor/flowEditorInteraction.js');
    const {
      arePortTypesCompatible,
      getPortTypeMismatchMessage
    } = await import('/src/core/canvas/portTypeCompatibility.mjs');

    const canvasElement = document.getElementById('flow-canvas');
    const canvas = {
      canvas: canvasElement,
      scale: 1,
      offset: { x: 0, y: 0 },
      nodes: new Map(),
      connections: [],
      selectedNode: null,
      mousePosition: { x: 0, y: 0 },
      isConnecting: false,
      connectingFrom: null,
      hoveredPort: null,
      handleMouseDown() {},
      handleMouseMove() {},
      handleMouseUp() {},
      checkTypeCompatibility: arePortTypesCompatible,
      getPortTypeMismatchMessage,
      readPortType(port) {
        return port?.type ?? port?.dataType ?? port?.DataType ?? port?.Type ?? 'Any';
      },
      invalidate() {},
      notifyViewStateChanged() {},
      getNodeAt() {
        return null;
      },
      addConnection() {
        throw new Error('A mismatched Image -> Region connection should not be added.');
      }
    };

    const interaction = new FlowEditorInteraction(canvas);
    interaction.connectionStart = {
      type: 'output',
      nodeId: 'thresholding',
      portIndex: 0,
      port: { name: 'Image', dataType: 'Image' }
    };
    interaction.isConnecting = true;
    interaction.endConnection(null, {
      type: 'input',
      nodeId: 'region-closing',
      portIndex: 0,
      port: { name: 'Region', DataType: 'Region' }
    });

    return document.querySelector('.cv-toast-message')?.textContent ?? '';
  });

  expect(toastMessage).toBe('当前输出是 Image/图像，不是 Region；请插入 BinaryImageToRegion。');
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

function getParameterValueFromFlow(
  flow: ProjectFlowFixture,
  nodeId: string,
  parameterName: string
): unknown {
  return flow.operators
    .find((operator) => operator.id === nodeId)
    ?.parameters
    .find((parameter) => parameter.name === parameterName)
    ?.value;
}

function createProjectApiFixture(persistenceRevision: number): ProjectApiFixture {
  return {
    id: 'project-a',
    name: 'Project A',
    description: 'G04B fixture',
    persistenceRevision,
    flow: createProjectFlowFixture(),
    globalVariables: createGlobalVariablesFixture()
  };
}

function createProjectFlowFixture(): ProjectFlowFixture {
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
      }
    ],
    connections: []
  };
}

function createGlobalVariablesFixture(): ProjectGlobalVariablesFixture {
  return {
    schemaVersion: '1.0',
    variables: [
      {
        id: 'variable-a',
        name: 'stats.count',
        valueType: 'Int64',
        initialValue: 1
      }
    ],
    sourceBindings: [],
    targetBindings: []
  };
}

function isProjectPutCall(call: ProjectApiCall): call is ProjectApiCall & { readonly body: ProjectSavePayload } {
  return call.method === 'PUT' &&
    call.url.endsWith('/api/projects/project-a') &&
    Boolean(call.body && typeof call.body === 'object');
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

interface ProjectApiCall {
  readonly method: string;
  readonly url: string;
  readonly body: unknown;
}

interface ProjectApiFixture {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly persistenceRevision: number;
  readonly flow: ProjectFlowFixture;
  readonly globalVariables: ProjectGlobalVariablesFixture;
}

interface ProjectSavePayload {
  readonly name: string;
  readonly description: string | null;
  readonly expectedPersistenceRevision: number;
  readonly flow: ProjectFlowFixture;
}

interface ProjectFlowFixture {
  readonly operators: Array<{
    readonly id: string;
    readonly type: string;
    readonly title: string;
    readonly x?: number;
    readonly y?: number;
    readonly inputPorts?: readonly unknown[];
    readonly outputPorts?: readonly unknown[];
    readonly parameters: Array<{
      readonly name: string;
      readonly displayName: string;
      readonly value: unknown;
      readonly dataType: string;
    }>;
  }>;
  readonly connections: [];
}

interface ProjectGlobalVariablesFixture {
  readonly schemaVersion: string;
  readonly variables: Array<{
    readonly id: string;
    readonly name: string;
    readonly valueType: string;
    readonly initialValue: number;
  }>;
  readonly sourceBindings: [];
  readonly targetBindings: [];
}
