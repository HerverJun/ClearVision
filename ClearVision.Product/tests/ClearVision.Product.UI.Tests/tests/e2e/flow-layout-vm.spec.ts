import { test, expect, Page } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

type PreviewMode = { value: 'success-no-image' | 'success-image' | 'error' };

const PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==';

const operators = [
  {
    type: 'ImageAcquisition',
    displayName: '图像采集',
    category: '输入',
    description: '从相机或文件读取图像',
    parameters: [
      { name: 'SourceType', displayName: '采集源', dataType: 'enum', value: 'File', options: [{ value: 'File', label: '文件' }, { value: 'Camera', label: '相机' }] },
      { name: 'FilePath', displayName: '文件路径', dataType: 'string', value: '' },
      { name: 'CameraId', displayName: '相机', dataType: 'cameraBinding', value: '' },
    ],
    inputPorts: [],
    outputPorts: [{ name: 'Image', dataType: 'Image' }],
  },
  {
    type: 'Thresholding',
    displayName: '阈值分割',
    category: '预处理',
    description: '按阈值生成二值图',
    parameters: [{ name: 'Threshold', displayName: '阈值', dataType: 'int', value: 128, min: 0, max: 255 }],
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Mask', dataType: 'Image' }],
  },
  {
    type: 'GaussianBlur',
    displayName: '高斯滤波',
    category: '预处理',
    description: '平滑图像噪声',
    parameters: [{ name: 'Sigma', displayName: 'Sigma', dataType: 'double', value: 1.2 }],
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Image', dataType: 'Image' }],
  },
];

async function installStudio2Flags(page: Page) {
  await page.addInitScript(() => {
    const webviewListeners = new Map<string, ((event: any) => void)[]>();
    (window as any).__pickFileMessages = [];
    (window as any).__cvDispatchWebViewMessage = (message: any) => {
      for (const listener of webviewListeners.get('message') ?? []) {
        listener({ data: message });
      }
    };
    (window as any).chrome = {
      webview: {
        addEventListener(type: string, listener: (event: any) => void) {
          if (!webviewListeners.has(type)) {
            webviewListeners.set(type, []);
          }
          webviewListeners.get(type)?.push(listener);
        },
        removeEventListener(type: string, listener: (event: any) => void) {
          const listeners = webviewListeners.get(type) ?? [];
          webviewListeners.set(type, listeners.filter(item => item !== listener));
        },
        postMessage(message: any) {
          (window as any).__pickFileMessages.push(message);
        },
      },
    };
    const startup = {
      featureFlags: {
        'Studio2.PropertyPanel': true,
        'Studio2.PreviewPanel': true,
      },
    };
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: startup,
      writable: false,
      configurable: false,
    });
  });
}

function buildObservation(request: any, success: boolean) {
  const targetNodeId = request.targetNodeId || request.TargetNodeId || '';
  return {
    schemaVersion: 'execution-observation.v1',
    observedAtUtc: '2026-07-05T00:00:00Z',
    identity: {
      projectId: request.projectId || request.ProjectId || 'flow-layout-vm',
      targetNodeId,
      debugSessionId: request.debugSessionId || request.DebugSessionId || 'layout-debug',
      clientRequestSequence: request.clientRequestSequence || request.ClientRequestSequence || 1,
      flowRevision: request.flowRevision || request.FlowRevision || 1,
    },
    outcome: {
      success,
      executionTimeMs: success ? 24 : 0,
      errorMessage: success ? null : '模拟预览失败',
      executedOperatorCount: success ? 1 : 0,
    },
    summary: success
      ? [{ key: 'Score', displayValue: '0.982', pathHint: '$["Score"]', addressable: true }]
      : [],
    detail: {
      kind: 'dictionary',
      name: 'Output',
      displayValue: success ? 'Score 0.982' : 'Failed',
      children: [],
    },
    diagnostics: [],
    visualScene: {
      coordinateSpace: 'image.pixel',
      imageWidth: 320,
      imageHeight: 240,
      primitives: [],
      diagnostics: [],
    },
  };
}

async function installRoutes(page: Page, previewMode: PreviewMode) {
  await page.route('**/api/operators/library', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(operators),
    });
  });

  await page.route('**/api/operators/types', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(operators.map(operator => operator.type)),
    });
  });

  await page.route('**/api/operators/*/metadata', async route => {
    const url = new URL(route.request().url());
    const type = decodeURIComponent(url.pathname.split('/').at(-2) ?? '');
    const operator = operators.find(item => item.type === type);
    await route.fulfill({
      status: operator ? 200 : 404,
      contentType: 'application/json',
      body: JSON.stringify(operator ?? { message: 'not found' }),
    });
  });

  await page.route('**/api/cameras/bindings', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]',
    });
  });

  await page.route('**/api/projects', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]',
    });
  });

  await page.route('**/api/flows/preview-node', async route => {
    const request = route.request().postDataJSON();
    const isError = previewMode.value === 'error';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: !isError,
        outputImageBase64: previewMode.value === 'success-image' ? PNG_BASE64 : null,
        outputData: isError ? null : { Score: 0.982, Width: 320 },
        observation: buildObservation(request, !isError),
        artifacts: [],
        executionTimeMs: isError ? 0 : 24,
        errorMessage: isError ? '模拟预览失败' : null,
      }),
    });
  });
}

async function setCurrentProject(page: Page) {
  await page.evaluate(async () => {
    const projectModule = await import('/src/features/project/projectManager.js');
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    projectModule.setCurrentProject({
      id: 'flow-layout-vm',
      name: '流程布局验证',
      description: '',
      flow: null,
    });
    inspectionModule.default.setProject('flow-layout-vm');
  });
}

async function openPreprocessFlyout(page: Page) {
  await page.locator('#operator-rail .operator-rail-item', { hasText: '预处理' }).click();
  await expect(page.locator('#operator-group-flyout')).toBeVisible();
}

async function openInputFlyout(page: Page) {
  await page.locator('#operator-rail .operator-rail-item', { hasText: '输入' }).click();
  await expect(page.locator('#operator-group-flyout')).toBeVisible();
}

async function addNodeFromFlyout(page: Page, label = '阈值分割') {
  await openPreprocessFlyout(page);
  await page.locator('#operator-group-flyout .operator-flyout-item', { hasText: label }).click();
  await expect(page.locator('#operator-group-flyout')).toBeHidden();
  return page.evaluate(() => {
    const flowCanvas = (window as any).flowCanvas;
    return {
      count: flowCanvas.nodes.size,
      selectedNode: flowCanvas.selectedNode,
    };
  });
}

async function assertNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() => ({
    document: document.documentElement.scrollWidth > document.documentElement.clientWidth + 2,
    body: document.body.scrollWidth > document.body.clientWidth + 2,
    main: (() => {
      const main = document.querySelector('#main-content');
      if (!main) return true;
      return main.scrollWidth > main.clientWidth + 2;
    })(),
  }));
  expect(overflow).toEqual({ document: false, body: false, main: false });
}

test.describe('Flow layout VisionMaster-style shell', () => {
  let previewMode: PreviewMode;

  test.beforeEach(async ({ page }) => {
    previewMode = { value: 'success-no-image' };
    await installStudio2Flags(page);
    await installRoutes(page, previewMode);
    await bootAuthenticatedApp(page);
    await setCurrentProject(page);
  });

  test('renders group rail and opens/closes operator flyout with search', async ({ page }) => {
    await expect(page.locator('#operator-rail')).toBeVisible();
    await expect(page.locator('#operator-rail .operator-rail-item')).toContainText(['最近', '收藏', '输入', '预处理']);
    await expect(page.locator('#operator-group-flyout')).toBeHidden();

    await openPreprocessFlyout(page);
    await expect(page.locator('#operator-group-flyout')).toContainText('阈值分割');
    await expect(page.locator('#operator-group-flyout')).toContainText('高斯滤波');

    await page.locator('[data-palette-search="true"]').fill('Sigma');
    await expect(page.locator('#operator-group-flyout .operator-flyout-item')).toHaveCount(1);
    await expect(page.locator('#operator-group-flyout')).toContainText('高斯滤波');

    await page.keyboard.press('Escape');
    await expect(page.locator('#operator-group-flyout')).toBeHidden();
  });

  test('adds an operator from flyout and switches inspector and preview workbench', async ({ page }) => {
    const result = await addNodeFromFlyout(page);
    expect(result.count).toBe(1);
    expect(result.selectedNode).toBeTruthy();

    await expect(page.locator('.inspector-pane')).toContainText('阈值分割');
    await expect(page.locator('.inspector-pane')).toContainText('阈值');
    await expect(page.locator('.preview-workbench-pane')).toContainText('预览工作台');
    await expect(page.locator('.preview-workbench-pane')).toContainText('端口与耗时');
    await expect(page.locator('.preview-workbench-pane')).toContainText('中间结果');
    await expect(page.locator('.preview-workbench-pane')).toContainText('暂无输出图像');
  });

  test('keeps migrated acquisition file picker and writes picked file path back to the node', async ({ page }) => {
    await openInputFlyout(page);
    await page.locator('#operator-group-flyout .operator-flyout-item', { hasText: '图像采集' }).click();
    await expect(page.locator('#operator-group-flyout')).toBeHidden();

    const fileInput = page.locator('.inspector-pane input[name="FilePath"]');
    const pickerButton = page.locator('.inspector-pane .btn-pick-file[data-param="FilePath"]');
    await expect(page.locator('.inspector-pane')).toContainText('图像采集');
    await expect(fileInput).toBeVisible();
    await expect(fileInput).toHaveAttribute('readonly', '');
    await expect(pickerButton).toBeVisible();
    await expect(page.locator('.inspector-pane select[data-camera-binding-select="true"]')).toHaveCount(1);
    await expect(page.locator('.inspector-pane #operator-preview-container')).toHaveCount(0);

    await pickerButton.click();
    const pickMessage = await page.evaluate(() => (window as any).__pickFileMessages.at(-1));
    expect(pickMessage.messageType).toBe('PickFileCommand');
    expect(pickMessage.parameterName).toBe('FilePath');
    expect(pickMessage.filter).toContain('Image Files');

    await page.evaluate(() => {
      (window as any).__cvDispatchWebViewMessage({
        messageType: 'FilePickedEvent',
        payload: {
          parameterName: 'FilePath',
          filePath: 'C:\\Data\\sample.png',
        },
      });
    });

    await expect(fileInput).toHaveValue('C:\\Data\\sample.png');
    await expect.poll(async () => page.evaluate(() => {
      const flowCanvas = (window as any).flowCanvas;
      const node = flowCanvas.nodes.get(flowCanvas.selectedNode);
      const parameter = node.parameters.find((item: any) => item.name === 'FilePath');
      return parameter?.value;
    })).toBe('C:\\Data\\sample.png');
  });

  test('shows blank, no-image and preview-failure states', async ({ page }) => {
    await expect(page.locator('.inspector-pane')).toContainText('未选择算子');
    await expect(page.locator('.preview-workbench-pane')).toContainText('请选择一个算子');

    await addNodeFromFlyout(page);
    await expect(page.locator('.preview-workbench-pane')).toContainText('暂无输出图像');

    previewMode.value = 'error';
    await page.locator('.preview-workbench-pane [data-preview-action="manual-preview"]').click();
    await expect(page.locator('.preview-workbench-pane')).toContainText('预览失败');
    await expect(page.locator('.preview-workbench-pane')).toContainText('模拟预览失败');
  });

  test('keeps 1366 and 1920 layouts within viewport and drag-drop coordinates stable', async ({ page }) => {
    await page.setViewportSize({ width: 1366, height: 768 });
    await openPreprocessFlyout(page);
    await assertNoHorizontalOverflow(page);

    await page.setViewportSize({ width: 1920, height: 1080 });
    await assertNoHorizontalOverflow(page);

    const dropResult = await page.evaluate(() => {
      const flowCanvas = (window as any).flowCanvas;
      const canvas = document.querySelector('#flow-canvas') as HTMLCanvasElement;
      const item = document.querySelector('.operator-flyout-item[data-operator-type="GaussianBlur"]') as HTMLElement;
      if (!flowCanvas || !canvas || !item) {
        throw new Error('缺少拖拽验证元素');
      }

      flowCanvas.scale = 1.25;
      flowCanvas.offset = { x: 80, y: 40 };
      flowCanvas.render?.();

      const rect = canvas.getBoundingClientRect();
      const clientX = rect.left + 300;
      const clientY = rect.top + 220;
      const expected = {
        x: (clientX - rect.left) / flowCanvas.scale + flowCanvas.offset.x,
        y: (clientY - rect.top) / flowCanvas.scale + flowCanvas.offset.y,
      };
      const beforeIds = new Set(Array.from(flowCanvas.nodes.keys()));
      const dataTransfer = new DataTransfer();

      item.dispatchEvent(new DragEvent('dragstart', {
        bubbles: true,
        cancelable: true,
        dataTransfer,
      }));
      canvas.dispatchEvent(new DragEvent('dragover', {
        bubbles: true,
        cancelable: true,
        clientX,
        clientY,
        dataTransfer,
      }));
      canvas.dispatchEvent(new DragEvent('drop', {
        bubbles: true,
        cancelable: true,
        clientX,
        clientY,
        dataTransfer,
      }));

      const added = Array.from(flowCanvas.nodes.values()).find((node: any) => !beforeIds.has(node.id)) as any;
      return {
        expected,
        actual: added ? { x: added.x, y: added.y, type: added.type } : null,
      };
    });

    expect(['GaussianBlur', 'Filtering']).toContain(dropResult.actual?.type);
    expect(dropResult.actual?.x).toBeCloseTo(dropResult.expected.x, 5);
    expect(dropResult.actual?.y).toBeCloseTo(dropResult.expected.y, 5);
  });
});
