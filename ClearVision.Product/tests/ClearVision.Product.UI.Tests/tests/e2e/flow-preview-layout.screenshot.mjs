import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import zlib from 'node:zlib';

import { chromium } from '@playwright/test';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const uiRoot = path.resolve(__dirname, '../..');
const webRoot = path.resolve(uiRoot, '../../src/ClearVision.Product.Desktop/wwwroot');
const outputRoot = path.resolve(uiRoot, 'test-results/flow-preview-layout');
const runId = new Date().toISOString().replace(/[:.]/g, '-');
const outputDir = path.join(outputRoot, runId);

const project = {
  id: 'flow-layout-project',
  name: 'Flow Layout QA',
  description: 'Playwright layout screenshot project',
  flow: null,
  globalVariables: {
    variables: [
      {
        id: 'gv-threshold',
        name: 'threshold',
        displayName: 'Threshold',
        valueType: 'Double',
        initialValue: 128,
        manualWriteAllowed: true,
        description: 'Preview threshold'
      },
      {
        id: 'gv-line-count',
        name: 'line.count',
        displayName: 'Line count',
        valueType: 'Int64',
        initialValue: 4,
        manualWriteAllowed: true,
        description: 'Wire count'
      }
    ],
    sourceBindings: [],
    targetBindings: []
  }
};

const user = {
  username: 'admin',
  displayName: 'Layout QA',
  role: 'Admin'
};

const acquisitionImage = createPngBase64(640, 240, 'acquisition');
const preprocessingImage = createPngBase64(640, 240, 'preprocessing');

function crc32(buffer) {
  const table = crc32.table || (crc32.table = Array.from({ length: 256 }, (_, index) => {
    let value = index;
    for (let bit = 0; bit < 8; bit += 1) {
      value = (value & 1) ? (0xedb88320 ^ (value >>> 1)) : (value >>> 1);
    }
    return value >>> 0;
  }));
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc = table[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function pngChunk(type, data) {
  const typeBuffer = Buffer.from(type, 'ascii');
  const lengthBuffer = Buffer.alloc(4);
  lengthBuffer.writeUInt32BE(data.length, 0);
  const crcBuffer = Buffer.alloc(4);
  crcBuffer.writeUInt32BE(crc32(Buffer.concat([typeBuffer, data])), 0);
  return Buffer.concat([lengthBuffer, typeBuffer, data, crcBuffer]);
}

function createPngBase64(width, height, variant) {
  const rows = [];
  for (let y = 0; y < height; y += 1) {
    const row = Buffer.alloc(1 + width * 3);
    row[0] = 0;
    for (let x = 0; x < width; x += 1) {
      const offset = 1 + x * 3;
      if (variant === 'acquisition') {
        const checker = ((Math.floor(x / 32) + Math.floor(y / 32)) % 2) === 0 ? 36 : 0;
        row[offset] = 36 + Math.floor((x / width) * 110) + checker;
        row[offset + 1] = 72 + Math.floor((y / height) * 90) + Math.floor(checker / 2);
        row[offset + 2] = 124;
      } else {
        const centerLine = Math.abs(y - height / 2) < 4 || Math.abs(x - width / 2) < 4;
        row[offset] = centerLine ? 235 : 28 + Math.floor((x / width) * 175);
        row[offset + 1] = centerLine ? 190 : (Math.floor(x / 40) % 2 === 0 ? 160 : 92);
        row[offset + 2] = centerLine ? 70 : 60 + Math.floor((y / height) * 150);
      }
    }
    rows.push(row);
  }

  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8;
  header[9] = 2;
  header[10] = 0;
  header[11] = 0;
  header[12] = 0;

  const png = Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk('IHDR', header),
    pngChunk('IDAT', zlib.deflateSync(Buffer.concat(rows), { level: 9 })),
    pngChunk('IEND', Buffer.alloc(0))
  ]);
  return png.toString('base64');
}

async function findFreePort() {
  return await new Promise((resolve, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      const port = typeof address === 'object' && address ? address.port : 0;
      server.close(() => resolve(port));
    });
  });
}

async function waitForServer(url) {
  for (let attempt = 0; attempt < 80; attempt += 1) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch {
      // Wait until http-server is ready.
    }
    await new Promise(resolve => setTimeout(resolve, 150));
  }
  throw new Error(`Timed out waiting for ${url}`);
}

async function startServer(port) {
  const server = spawn(process.execPath, [
    './node_modules/http-server/bin/http-server',
    webRoot,
    '-p',
    String(port),
    '-a',
    '127.0.0.1',
    '-c-1'
  ], {
    cwd: uiRoot,
    stdio: ['ignore', 'pipe', 'pipe']
  });

  let startupOutput = '';
  server.stdout.on('data', chunk => {
    startupOutput += chunk.toString();
  });
  server.stderr.on('data', chunk => {
    startupOutput += chunk.toString();
  });

  await waitForServer(`http://127.0.0.1:${port}/index.html`).catch(error => {
    throw new Error(`${error.message}\n${startupOutput}`);
  });

  return server;
}

async function stopServer(server) {
  if (!server || server.killed) {
    return;
  }
  await new Promise(resolve => {
    server.once('exit', resolve);
    server.kill();
    setTimeout(resolve, 1500);
  });
}

function buildObservation(request, nodeType) {
  const targetNodeId = request.targetNodeId || request.TargetNodeId || '';
  return {
    schemaVersion: 'execution-observation.v1',
    observedAtUtc: '2026-07-05T00:00:00Z',
    identity: {
      projectId: request.projectId || request.ProjectId || project.id,
      targetNodeId,
      debugSessionId: request.debugSessionId || request.DebugSessionId || 'layout-debug',
      clientRequestSequence: request.clientRequestSequence || request.ClientRequestSequence || 1,
      flowRevision: request.flowRevision || request.FlowRevision || 1
    },
    outcome: {
      success: true,
      executionTimeMs: 18,
      executedOperatorCount: nodeType === 'ImageAcquisition' ? 1 : 2
    },
    summary: [
      {
        key: 'Score',
        displayValue: '0.982',
        originalType: 'System.Double',
        pathHint: '$["Score"]',
        addressable: true
      }
    ],
    detail: {
      kind: 'dictionary',
      name: 'Output',
      displayValue: 'Score 0.982',
      children: []
    },
    diagnostics: [],
    visualScene: {
      coordinateSpace: 'image.pixel',
      imageWidth: 640,
      imageHeight: 240,
      primitives: [],
      diagnostics: []
    }
  };
}

async function installRoutes(page, runtimeStateRef, nodeTypesById) {
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const pathname = url.pathname;
    const method = request.method();

    const fulfillJson = body => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(body)
    });

    if (pathname.endsWith('/auth/me')) {
      return fulfillJson(user);
    }

    if (pathname.endsWith('/operators/types') || pathname.endsWith('/operators/library')) {
      return fulfillJson([]);
    }

    if (pathname.endsWith('/projects') && method === 'GET') {
      return fulfillJson([]);
    }

    if (pathname.endsWith(`/projects/${project.id}/global-variable-values`) && method === 'GET') {
      return fulfillJson([
        { variableId: 'gv-threshold', value: 128, valueType: 'Double', version: 1 },
        { variableId: 'gv-line-count', value: 4, valueType: 'Int64', version: 1 }
      ]);
    }

    if (pathname.endsWith(`/projects/${project.id}/global-variables`) && method === 'PUT') {
      const body = request.postDataJSON();
      return fulfillJson(body);
    }

    if (pathname.endsWith(`/inspection/realtime/${project.id}/state`) && method === 'GET') {
      return fulfillJson(runtimeStateRef.locked
        ? {
            projectId: project.id,
            sessionId: 'layout-running-session',
            status: 'Running',
            isRunning: true,
            isRealtime: true,
            isBusy: true
          }
        : {
            projectId: project.id,
            sessionId: 'layout-stopped-session',
            status: 'Stopped',
            isRunning: false,
            isRealtime: false,
            isBusy: false
          });
    }

    if (pathname.endsWith('/flows/preview-node') && method === 'POST') {
      const body = request.postDataJSON();
      const targetNodeId = body.targetNodeId || body.TargetNodeId || '';
      const nodeType = nodeTypesById.get(targetNodeId) || 'Thresholding';
      const outputImageBase64 = nodeType === 'ImageAcquisition' ? acquisitionImage : preprocessingImage;
      return fulfillJson({
        success: true,
        inputImageBase64: acquisitionImage,
        outputImageBase64,
        outputData: {
          Score: 0.982,
          Width: 640,
          Height: 240,
          Operator: nodeType
        },
        observation: buildObservation(body, nodeType),
        artifacts: [],
        executionTimeMs: 18
      });
    }

    return fulfillJson({});
  });
}

async function bootApp(page, baseUrl, theme = 'dark') {
  await page.addInitScript(({ currentUser, selectedTheme }) => {
    sessionStorage.setItem('cv_auth_token', 'layout-token');
    sessionStorage.setItem('cv_current_user', JSON.stringify(currentUser));
    localStorage.setItem('cv_welcome_shown', 'true');
    localStorage.setItem('cv_theme', selectedTheme);
  }, { currentUser: user, selectedTheme: theme });

  await page.goto(`${baseUrl}/index.html`, { waitUntil: 'domcontentloaded' });
  await page.locator('#app').waitFor({ state: 'visible', timeout: 10000 });
  await page.locator('#loading-screen').waitFor({ state: 'detached', timeout: 10000 }).catch(async () => {
    await page.locator('#loading-screen').waitFor({ state: 'hidden', timeout: 10000 });
  });
  await page.locator('#preview-panel [data-role="preview-output-image"]').waitFor({ state: 'attached', timeout: 10000 });
}

async function setProject(page) {
  await page.evaluate(async activeProject => {
    const projectModule = await import('/src/features/project/projectManager.js');
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    projectModule.setCurrentProject(structuredClone(activeProject));
    inspectionModule.default.setProject(activeProject.id);
  }, project);
  await page.waitForTimeout(250);
}

async function clearFlowSelection(page) {
  await page.evaluate(() => {
    const flowCanvas = window.flowCanvas;
    if (!flowCanvas) {
      return;
    }
    flowCanvas.clear?.();
    flowCanvas.selectedNode = null;
    flowCanvas.selectedConnection = null;
    flowCanvas.onNodeSelected?.(null);
    flowCanvas.render?.();
  });
  await page.waitForTimeout(150);
}

async function addAndSelectNode(page, nodeTypesById, config) {
  const nodeId = await page.evaluate(nodeConfig => {
    const flowCanvas = window.flowCanvas;
    const node = flowCanvas.addNode(
      nodeConfig.type,
      nodeConfig.x,
      nodeConfig.y,
      {
        title: nodeConfig.title,
        parameters: nodeConfig.parameters,
        inputs: nodeConfig.inputs || [{ name: 'input', type: 'Image' }],
        outputs: nodeConfig.outputs || [{ name: 'output', type: 'Image' }],
        color: nodeConfig.color || '#1890ff'
      }
    );
    flowCanvas.selectedNode = node.id;
    flowCanvas.selectedConnection = null;
    flowCanvas.onNodeSelected?.(node);
    flowCanvas.render?.();
    return node.id;
  }, config);

  nodeTypesById.set(nodeId, config.type);
  await page.waitForFunction(id => window.flowCanvas?.selectedNode === id, nodeId);
  return nodeId;
}

async function waitForOutputImage(page) {
  await page.waitForFunction(() => {
    const image = document.querySelector('#preview-output-image');
    return image?.getAttribute('src')?.startsWith('data:image/');
  }, null, { timeout: 10000 });
}

async function setTheme(page, theme) {
  await page.evaluate(nextTheme => {
    localStorage.setItem('cv_theme', nextTheme);
    document.documentElement.dataset.theme = nextTheme;
    document.documentElement.style.colorScheme = nextTheme;
  }, theme);
  await page.waitForTimeout(100);
}

async function collectLayoutMetrics(page) {
  return await page.evaluate(() => {
    const rect = selector => {
      const element = document.querySelector(selector);
      if (!element) {
        return null;
      }
      const value = element.getBoundingClientRect();
      return {
        x: value.x,
        y: value.y,
        width: value.width,
        height: value.height,
        right: value.right,
        bottom: value.bottom
      };
    };

    return {
      theme: document.documentElement.dataset.theme,
      toolbar: rect('.toolbar'),
      toolbarRight: rect('.toolbar-right'),
      rightSidebar: rect('.sidebar.right'),
      previewPanel: rect('.preview-sidebar-panel'),
      propertyPanel: rect('.property-sidebar-panel'),
      previewMain: rect('[data-role="preview-main-image-container"]'),
      panelTitles: Array.from(document.querySelectorAll('.sidebar.right .panel-title')).map(item => item.textContent.trim()),
      globalInRight: Boolean(document.querySelector('.sidebar.right #global-variable-panel')),
      outputImageCount: document.querySelectorAll('#preview-panel [data-role="preview-output-image"]').length,
      oldPreviewCount: document.querySelectorAll('#preview-panel [id*="preview-before"], #preview-panel [id*="preview-after"]').length,
      horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 2,
      bodyHorizontalOverflow: document.body.scrollWidth > document.body.clientWidth + 2,
      toolbarWrapped: (document.querySelector('.toolbar')?.getBoundingClientRect().height || 0) > 58,
      navCanWrap: Array.from(document.querySelectorAll('.main-nav .nav-btn'))
        .some(button => getComputedStyle(button).whiteSpace !== 'nowrap')
    };
  });
}

function assertLayoutMetrics(metrics, scenarioName) {
  if (metrics.panelTitles.join('|') !== '预览|属性') {
    throw new Error(`${scenarioName}: right sidebar titles are ${metrics.panelTitles.join(', ')}`);
  }
  if (metrics.globalInRight) {
    throw new Error(`${scenarioName}: global-variable-panel is still inside the right sidebar`);
  }
  if (metrics.outputImageCount !== 1) {
    throw new Error(`${scenarioName}: expected one output image surface, found ${metrics.outputImageCount}`);
  }
  if (metrics.oldPreviewCount !== 0) {
    throw new Error(`${scenarioName}: old before/after preview DOM is present`);
  }
  if (!metrics.previewMain || metrics.previewMain.height < 170) {
    throw new Error(`${scenarioName}: preview main image is too short`);
  }
  if (metrics.horizontalOverflow || metrics.bodyHorizontalOverflow) {
    throw new Error(`${scenarioName}: horizontal overflow detected`);
  }
  if (metrics.toolbarWrapped) {
    throw new Error(`${scenarioName}: toolbar height suggests wrapping`);
  }
  if (metrics.navCanWrap) {
    throw new Error(`${scenarioName}: navigation labels can wrap`);
  }
  if (!metrics.propertyPanel || metrics.propertyPanel.height < 120) {
    throw new Error(`${scenarioName}: property panel was squeezed below usable height`);
  }
}

async function capture(page, name, note, screenshots) {
  await page.evaluate(() => {
    document.querySelector('#cv-toast-container')?.replaceChildren();
  });
  await page.waitForTimeout(50);
  const metrics = await collectLayoutMetrics(page);
  assertLayoutMetrics(metrics, name);
  const filePath = path.join(outputDir, `${name}.png`);
  await page.screenshot({ path: filePath, fullPage: false });
  screenshots.push({ name, path: filePath, note, metrics });
}

async function main() {
  await mkdir(outputDir, { recursive: true });
  const port = await findFreePort();
  const baseUrl = `http://127.0.0.1:${port}`;
  const server = await startServer(port);
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1
  });
  const page = await context.newPage();
  const consoleErrors = [];
  const runtimeStateRef = { locked: false };
  const nodeTypesById = new Map();
  const screenshots = [];

  page.on('console', message => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text());
    }
  });
  page.on('pageerror', error => {
    consoleErrors.push(error.message);
  });

  try {
    await installRoutes(page, runtimeStateRef, nodeTypesById);
    await bootApp(page, baseUrl, 'dark');

    await capture(
      page,
      '01-no-project-dark',
      '未打开工程：顶部变量入口禁用，右侧仅有预览和属性。',
      screenshots);

    await setProject(page);
    await clearFlowSelection(page);
    await capture(
      page,
      '02-project-no-selection-dark',
      '已打开工程但未选中算子：预览为空态，属性显示未选择算子。',
      screenshots);

    await addAndSelectNode(page, nodeTypesById, {
      type: 'ImageAcquisition',
      title: '图像采集',
      x: 120,
      y: 140,
      parameters: [
        { name: 'SourceType', value: 'File', dataType: 'enum' },
        { name: 'FilePath', value: 'C:/ClearVision/samples/part-a.png', dataType: 'string' }
      ],
      inputs: [],
      outputs: [{ name: 'Image', type: 'Image' }],
      color: '#0ea5e9'
    });
    await waitForOutputImage(page);
    await capture(
      page,
      '03-image-acquisition-single-preview-dark',
      '图像采集算子：右侧上方只有一个输出主图区域，没有输入/输出双 PictureBox。',
      screenshots);

    await addAndSelectNode(page, nodeTypesById, {
      type: 'Thresholding',
      title: '预处理阈值',
      x: 340,
      y: 170,
      parameters: [
        { name: 'Threshold', value: 128, dataType: 'int', min: 0, max: 255 },
        { name: 'Mode', value: 'Binary', dataType: 'enum', options: ['Binary', 'Inverse'] }
      ],
      outputs: [{ name: 'Mask', type: 'Image' }],
      color: '#22c55e'
    });
    await waitForOutputImage(page);
    await capture(
      page,
      '04-preprocessing-output-summary-dark',
      '有图像输出的预处理算子：优先展示输出图，摘要区仍可见。',
      screenshots);

    await page.setViewportSize({ width: 980, height: 720 });
    await page.waitForTimeout(150);
    await capture(
      page,
      '05-narrow-window-layout-dark',
      '缩窄窗口：预览和属性区仍保留可用高度，无横向溢出。',
      screenshots);

    await page.setViewportSize({ width: 1440, height: 900 });
    runtimeStateRef.locked = true;
    await page.locator('#gv-open-manager').click();
    await page.locator('.gv-manager-overlay').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('#gv-search').fill('threshold');
    await page.locator('#gv-type-filter').selectOption({ index: 1 });
    await page.locator('.gv-warning').waitFor({ state: 'visible', timeout: 5000 });
    await capture(
      page,
      '06-global-variable-manager-locked-dark',
      '打开全局变量管理弹窗：搜索、筛选、关闭可见，运行中锁定提示保留。',
      screenshots);
    await page.locator('.gv-manager [data-action="close"]').click();
    await page.locator('.gv-manager-overlay').waitFor({ state: 'detached', timeout: 5000 });
    runtimeStateRef.locked = false;

    await setTheme(page, 'dark');
    await capture(
      page,
      '07-theme-dark-flow',
      '暗色主题流程页：顶部变量入口和右侧预览视觉一致。',
      screenshots);

    await setTheme(page, 'light');
    await capture(
      page,
      '08-theme-light-flow',
      '亮色主题流程页：顶部变量入口和右侧预览视觉一致。',
      screenshots);

    if (consoleErrors.length > 0) {
      throw new Error(`Console errors detected:\n${consoleErrors.join('\n')}`);
    }

    const summaryPath = path.join(outputDir, 'summary.json');
    await writeFile(summaryPath, JSON.stringify({
      outputDir,
      screenshots,
      consoleErrors
    }, null, 2), 'utf8');

    console.log(`Flow preview layout screenshots written to ${outputDir}`);
    for (const item of screenshots) {
      console.log(`${path.basename(item.path)} - ${item.note}`);
    }
    console.log(`Summary: ${summaryPath}`);
  } finally {
    await context.close().catch(() => {});
    await browser.close().catch(() => {});
    await stopServer(server);
  }
}

main().catch(error => {
  console.error(error);
  process.exit(1);
});
