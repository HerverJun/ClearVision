import { test, expect, Page } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const OPERATOR_LIBRARY = [
  {
    type: 'ContourDetection',
    displayName: '轮廓检测',
    category: '特征提取',
    description: '输出 Contour/轮廓。',
    parameters: [],
    inputPorts: [{ name: 'Image', displayName: '图像', dataType: 'Image', isRequired: true }],
    outputPorts: [{ name: 'Contours', displayName: '轮廓', dataType: 'Contour', description: '边界点轮廓。' }],
  },
  {
    type: 'BlobAnalysis',
    displayName: 'Blob分析',
    category: '特征提取',
    description: '输出 Blob 结果列表。',
    parameters: [],
    inputPorts: [{ name: 'Image', displayName: '二值图像', dataType: 'Image', isRequired: true }],
    outputPorts: [
      { name: 'Image', displayName: '标记图像', dataType: 'Image' },
      { name: 'Blobs', displayName: 'Blob结果列表', dataType: 'BlobList', description: '不是 Contour 或 Region。' },
      { name: 'BlobFeatures', displayName: 'Blob详细特征', dataType: 'BlobFeatureList' },
      { name: 'BlobCount', displayName: 'Blob数量', dataType: 'Integer' },
    ],
  },
  {
    type: 'BinaryImageToRegion',
    displayName: '二值图转区域',
    category: '区域处理',
    description: '将二值图转换为 Region。',
    parameters: [],
    inputPorts: [{ name: 'Image', displayName: '二值图/掩膜', dataType: 'Image', isRequired: true }],
    outputPorts: [
      { name: 'Region', displayName: '像素区域', dataType: 'Region', description: '可连接区域形态学算子。' },
      { name: 'Image', displayName: '可视化图像', dataType: 'Image' },
    ],
  },
  {
    type: 'RegionErosion',
    displayName: '区域腐蚀',
    category: '区域处理',
    description: '对 Region 执行区域腐蚀。',
    parameters: [],
    inputPorts: [
      { name: 'Region', displayName: '输入区域', dataType: 'Region', isRequired: true, description: '区域形态学主输入。' },
      { name: 'Image', displayName: '参考图像（可选）', dataType: 'Image', isRequired: false, description: '仅用于参考图和可视化。' },
    ],
    outputPorts: [
      { name: 'Region', displayName: '腐蚀后区域', dataType: 'Region' },
      { name: 'Image', displayName: '可视化图像', dataType: 'Image' },
    ],
  },
];

async function stubOperatorLibrary(page: Page) {
  await page.route('**/api/operators/library', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(OPERATOR_LIBRARY),
  }));
  await page.route('**/api/operators/types', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(OPERATOR_LIBRARY.map(operator => operator.type)),
  }));
}

async function getPortClientPoint(page: Page, nodeId: string, portIndex: number, isOutput: boolean) {
  return page.evaluate(({ nodeId, portIndex, isOutput }) => {
    const canvas = (window as any).flowCanvas;
    const position = canvas.getPortPosition(nodeId, portIndex, isOutput);
    const rect = canvas.canvas.getBoundingClientRect();
    return { x: rect.left + position.x, y: rect.top + position.y };
  }, { nodeId, portIndex, isOutput });
}

async function dragPort(page: Page, source: { nodeId: string; portIndex: number }, target: { nodeId: string; portIndex: number }, hold = false) {
  const start = await getPortClientPoint(page, source.nodeId, source.portIndex, true);
  const end = await getPortClientPoint(page, target.nodeId, target.portIndex, false);
  await page.mouse.move(start.x, start.y);
  await page.mouse.down();
  await page.mouse.move(end.x, end.y, { steps: 10 });
  if (!hold) {
    await page.mouse.up();
  }
}

test('BlobList -> Region is rejected while BinaryImageToRegion.Region connects, with visible guidance and colors', async ({ page }) => {
  await page.setViewportSize({ width: 1680, height: 1000 });
  await stubOperatorLibrary(page);
  await bootAuthenticatedApp(page);
  await expect(page.locator('#flow-canvas')).toBeVisible();

  const nodeIds = await page.evaluate(async operators => {
    const canvas = (window as any).flowCanvas;
    const { buildOperatorNodeConfig } = await import('/src/shared/operatorVisuals.js');
    const byType = new Map(operators.map((operator: any) => [operator.type, operator]));
    const add = (type: string, x: number, y: number) =>
      canvas.addNode(type, x, y, buildOperatorNodeConfig(type, byType.get(type)));

    const contour = add('ContourDetection', 40, 40);
    const blob = add('BlobAnalysis', 40, 240);
    const converter = add('BinaryImageToRegion', 330, 40);
    const erosion = add('RegionErosion', 650, 170);
    canvas.selectedNode = erosion.id;
    canvas.onNodeSelected?.(erosion);
    canvas.markSelectionChanged?.('region-port-semantics-e2e');
    canvas.render();
    return { contour: contour.id, blob: blob.id, converter: converter.id, erosion: erosion.id };
  }, OPERATOR_LIBRARY);

  const preview = page.locator('.preview-workbench-pane');
  await expect(preview).toContainText('当前缺少 Region');
  await expect(preview).toContainText('Image/Contour 不能直接替代');
  await expect(preview).toContainText('BinaryImageToRegion');
  await expect(preview).toContainText('可选 Image 输入仅用于参考图和可视化');

  const directValidation = await page.evaluate(ids => {
    const canvas = (window as any).flowCanvas;
    const rejected = canvas.addConnection(ids.blob, 1, ids.erosion, 0);
    return {
      rejected: rejected === null,
      connectionCount: canvas.connections.length,
    };
  }, nodeIds);
  expect(directValidation).toEqual({ rejected: true, connectionCount: 0 });

  await dragPort(page, { nodeId: nodeIds.blob, portIndex: 1 }, { nodeId: nodeIds.erosion, portIndex: 0 }, true);
  const tooltip = page.locator('.flow-port-tooltip');
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toHaveAttribute('data-compatibility', 'incompatible');
  await expect(tooltip).toContainText('BlobList/Blob结果列表');
  await expect(tooltip).toContainText('Region/像素区域');
  await expect(tooltip).toContainText('BinaryImageToRegion');
  await expect(page.locator('#flow-canvas')).toHaveAttribute('title', /数据类型：Region\/像素区域/);
  await expect(page.locator('#flow-canvas')).toHaveAttribute('aria-label', /不兼容：当前输出是 BlobList\/Blob结果列表/);

  const colors = await page.evaluate(async () => {
    const { getPortTypeColor } = await import('/src/core/canvas/portTypeCompatibility.mjs');
    return {
      contour: getPortTypeColor('Contour'),
      region: getPortTypeColor('Region'),
    };
  });
  expect(colors.contour).not.toBe(colors.region);

  await page.screenshot({
    path: 'test-results/region-port-semantics-mismatch.png',
    fullPage: true,
  });

  await page.mouse.up();
  await expect(page.locator('.cv-toast-message').last()).toContainText('BlobList/Blob结果列表');
  expect(await page.evaluate(() => (window as any).flowCanvas.connections.length)).toBe(0);

  await dragPort(page, { nodeId: nodeIds.converter, portIndex: 0 }, { nodeId: nodeIds.erosion, portIndex: 0 });
  await expect.poll(() => page.evaluate(() => (window as any).flowCanvas.connections.length)).toBe(1);
  await expect(preview).not.toContainText('当前缺少 Region');

  const connection = await page.evaluate(ids => {
    const item = (window as any).flowCanvas.connections[0];
    return {
      source: item.source,
      sourcePort: item.sourcePort,
      target: item.target,
      targetPort: item.targetPort,
      expectedSource: ids.converter,
      expectedTarget: ids.erosion,
    };
  }, nodeIds);
  expect(connection).toMatchObject({
    source: connection.expectedSource,
    sourcePort: 0,
    target: connection.expectedTarget,
    targetPort: 0,
  });

  await page.screenshot({
    path: 'test-results/region-port-semantics-valid.png',
    fullPage: true,
  });
});
