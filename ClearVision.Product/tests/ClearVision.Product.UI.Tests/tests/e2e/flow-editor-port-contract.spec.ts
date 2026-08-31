import { test, expect, Page } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

// 端口契约 E2E：算子库 → 画布 → 序列化 / 预览工作台。
//
// 背景：clonePorts 曾在 metadata 明确声明空端口时伪造一个 Any 端口。
// 修复后：metadata 明确给出 inputPorts: [] 必须原样保留空端口，
// 只有 metadata 缺失该侧端口时才允许兼容 fallback。
//
// RectangleRegion 是真正的「无输入」几何源头算子（metadata inputPorts: []），
// 用它验证「画布不得凭空多出输入端口」这一契约。
// ImageAcquisition 的后端契约刻意声明了 2 个可选输入端口（Image/FilePath，运行时供图），
// 用它作为「不得矫枉过正、真实端口必须保留」的护栏。

const RECTANGLE_REGION = {
  type: 'RectangleRegion',
  displayName: '矩形区域',
  category: '几何',
  description: '生成矩形区域',
  parameters: [
    { name: 'Width', displayName: '宽度', dataType: 'int', value: 100, defaultValue: 100 },
    { name: 'Height', displayName: '高度', dataType: 'int', value: 100, defaultValue: 100 },
  ],
  inputPorts: [],
  outputPorts: [{ name: 'Rectangle', displayName: '矩形', dataType: 'Rectangle' }],
};

const IMAGE_ACQUISITION = {
  type: 'ImageAcquisition',
  displayName: '图像采集',
  category: '采集',
  description: '从文件或相机采集图像',
  parameters: [
    { name: 'SourceType', displayName: '采集源', dataType: 'enum', value: 'File', defaultValue: 'File' },
    { name: 'FilePath', displayName: '文件路径', dataType: 'file', value: '', defaultValue: '' },
  ],
  inputPorts: [
    { name: 'Image', displayName: 'Runtime supplied image', dataType: 'Image', isRequired: false },
    { name: 'FilePath', displayName: '文件路径输入', dataType: 'String', isRequired: false },
  ],
  outputPorts: [{ name: 'Image', displayName: '图像', dataType: 'Image' }],
};

async function stubOperatorLibrary(page: Page) {
  // 静态 UI server 不承载 production-only 的最终判定校验端点；预览协调器会在
  // 选中节点时调用它，因此在本 contract fixture 中显式提供其稳定成功响应。
  await page.route('**/api/inspection/decision-configuration/validate', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isValid: true, issues: [], eligibleOutputs: [] }),
    });
  });
  await page.route('**/api/operators/library', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([RECTANGLE_REGION, IMAGE_ACQUISITION]),
    });
  });
  // 兜底：回退接口也一并 stub，避免命中真实后端。
  await page.route('**/api/operators/types', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(['RectangleRegion', 'ImageAcquisition']),
    });
  });
  await page.route('**/api/operators/*/metadata', async route => {
    const url = new URL(route.request().url());
    const parts = url.pathname.split('/');
    const type = decodeURIComponent(parts[parts.length - 2] ?? '');
    const meta = type === 'ImageAcquisition' ? IMAGE_ACQUISITION
      : type === 'RectangleRegion' ? RECTANGLE_REGION
        : null;
    await route.fulfill({
      status: meta ? 200 : 404,
      contentType: 'application/json',
      body: JSON.stringify(meta ?? { message: 'not found' }),
    });
  });
}

// 设置当前项目，令预览协调器 getProjectId() 有值（否则只会提示“缺少项目”）。
async function setCurrentProject(page: Page) {
  await page.evaluate(async () => {
    const projectModule = await import('/src/features/project/projectManager.js');
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    projectModule.setCurrentProject({
      id: 'e2e-project',
      name: 'E2E Project',
      description: '',
      flow: null,
      persistenceRevision: 0,
    });
    inspectionModule.default.setProject('e2e-project');
  });
}

async function openFlyoutGroup(page: Page, categoryLabel: string) {
  const railItem = page.locator('#operator-rail .operator-rail-item', { hasText: categoryLabel });
  await expect(railItem).toBeVisible();
  await railItem.click();
  await expect(page.locator('#operator-group-flyout')).toBeVisible();
}

// 点击添加：算子组 flyout 内点击算子项 → onOperatorAdd → addOperatorFromPalette。
async function clickAddOperator(page: Page, categoryLabel: string, operatorType: string) {
  await openFlyoutGroup(page, categoryLabel);
  const item = page.locator(
    `#operator-group-flyout .operator-flyout-item[data-operator-type="${operatorType}"]`,
  );
  await expect(item).toBeVisible();
  await item.click();
}

// 拖拽添加：走 flyout 的 dragstart（设置 window.__draggingOperatorData）+ 画布 drop 处理器。
// 使用真实 DataTransfer，否则 handleFlyoutDragStart 里的 dataTransfer.setData 会抛错。
async function dragAddOperator(page: Page, categoryLabel: string, operatorType: string) {
  await openFlyoutGroup(page, categoryLabel);
  const item = page.locator(
    `#operator-group-flyout .operator-flyout-item[data-operator-type="${operatorType}"]`,
  );
  await expect(item).toBeVisible();

  const dataTransfer = await page.evaluateHandle(() => new DataTransfer());
  await item.dispatchEvent('dragstart', { dataTransfer });
  const canvas = page.locator('#flow-canvas');
  await canvas.dispatchEvent('dragover', { dataTransfer });
  await canvas.dispatchEvent('drop', { clientX: 400, clientY: 300, dataTransfer });
}

// 读取画布上最后一个节点的端口结构（画布节点视图）。
async function readLastNodePorts(page: Page) {
  return page.evaluate(() => {
    const canvas = (window as any).flowCanvas;
    const nodes = Array.from(canvas.nodes.values()) as any[];
    const node = nodes[nodes.length - 1];
    return {
      type: node.type,
      inputCount: node.inputs.length,
      outputCount: node.outputs.length,
      inputNames: node.inputs.map((p: any) => p.name),
      outputNames: node.outputs.map((p: any) => p.name),
    };
  });
}

// 读取序列化后最后一个算子的端口结构（保存 / 反序列化契约）。
async function readSerializedPorts(page: Page) {
  return page.evaluate(() => {
    const canvas = (window as any).flowCanvas;
    const serialized = canvas.serialize();
    const op = serialized.operators[serialized.operators.length - 1];
    return {
      type: op.type,
      inputCount: op.inputPorts.length,
      outputCount: op.outputPorts.length,
      inputNames: op.inputPorts.map((p: any) => p.name),
    };
  });
}

test.describe('Flow Editor port contract (library → canvas → serialize)', () => {
  let consoleErrors: string[] = [];

  // 裸静态服务器没有 ASP.NET 后端，/api/* 会返回 404，这是环境噪声，
  // 与本次「端口契约」改动无关。仅过滤这类后端资源 404，仍捕获真正的 JS 报错。
  const isBenignBackend404 = (text: string) =>
    /Failed to load resource: the server responded with a status of 404/.test(text);

  test.beforeEach(async ({ page }) => {
    consoleErrors = [];
    page.on('console', message => {
      if (message.type() === 'error' && !isBenignBackend404(message.text())) {
        consoleErrors.push(message.text());
      }
    });
    page.on('pageerror', error => {
      consoleErrors.push(error.message);
    });
    await stubOperatorLibrary(page);
    await bootAuthenticatedApp(page);
    await expect(page.locator('#flow-canvas')).toBeVisible();
  });

  test('click-add RectangleRegion keeps zero input ports through canvas and serialize', async ({ page }) => {
    await clickAddOperator(page, '几何', 'RectangleRegion');

    const canvasPorts = await readLastNodePorts(page);
    expect(canvasPorts.type).toBe('RectangleRegion');
    expect(canvasPorts.inputCount).toBe(0);
    expect(canvasPorts.outputCount).toBe(1);
    expect(canvasPorts.outputNames).toEqual(['Rectangle']);

    const serialized = await readSerializedPorts(page);
    expect(serialized.inputCount).toBe(0);
    expect(serialized.outputCount).toBe(1);

    expect(consoleErrors).toEqual([]);
  });

  test('drag-add RectangleRegion keeps zero input ports through canvas and serialize', async ({ page }) => {
    await dragAddOperator(page, '几何', 'RectangleRegion');

    const canvasPorts = await readLastNodePorts(page);
    expect(canvasPorts.type).toBe('RectangleRegion');
    expect(canvasPorts.inputCount).toBe(0);
    expect(canvasPorts.outputCount).toBe(1);

    const serialized = await readSerializedPorts(page);
    expect(serialized.inputCount).toBe(0);

    expect(consoleErrors).toEqual([]);
  });

  test('ImageAcquisition keeps its two real declared input ports (no over-correction)', async ({ page }) => {
    await clickAddOperator(page, '采集', 'ImageAcquisition');

    const canvasPorts = await readLastNodePorts(page);
    expect(canvasPorts.type).toBe('ImageAcquisition');
    expect(canvasPorts.inputCount).toBe(2);
    expect(canvasPorts.inputNames).toEqual(['Image', 'FilePath']);
    expect(canvasPorts.outputCount).toBe(1);

    const serialized = await readSerializedPorts(page);
    expect(serialized.inputCount).toBe(2);
    expect(serialized.inputNames).toEqual(['Image', 'FilePath']);

    expect(consoleErrors).toEqual([]);
  });

  test('layout stays intact and preview workbench is present after adding a source operator', async ({ page }) => {
    await clickAddOperator(page, '几何', 'RectangleRegion');

    // 关键面板仍在位，无布局回退。
    await expect(page.locator('#operator-rail')).toBeVisible();
    await expect(page.locator('.inspector-pane')).toContainText('属性检查器');
    await expect(page.locator('.preview-workbench-pane')).toContainText('预览工作台');

    await page.screenshot({ path: 'test-results/flow-editor-port-contract.png', fullPage: true });

    expect(consoleErrors).toEqual([]);
  });

  test('preview workbench prompts to configure the acquisition source (no console errors)', async ({ page }) => {
    await setCurrentProject(page);
    await clickAddOperator(page, '采集', 'ImageAcquisition');

    // 确保节点被选中，触发预览协调器（默认走 legacy 预览路径，驱动 #preview-status-text）。
    await page.evaluate(() => {
      const canvas = (window as any).flowCanvas;
      const nodes = Array.from(canvas.nodes.values()) as any[];
      const node = nodes[nodes.length - 1];
      canvas.selectedNode = node.id;
      canvas.onNodeSelected?.(node);
    });

    // SourceType=File 且 FilePath 为空 → 预览工作台提示“请先配置文件路径”，
    // 而不是崩溃或空白，证明源头算子预览链路未回退。
    await expect(page.locator('#preview-status-text')).toContainText('请先配置文件路径');
    await expect(page.locator('.preview-workbench-pane')).toContainText('预览工作台');

    expect(consoleErrors).toEqual([]);
  });
});
