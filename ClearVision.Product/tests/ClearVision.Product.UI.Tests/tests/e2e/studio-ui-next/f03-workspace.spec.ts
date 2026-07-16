import { expect, Page, Route, test } from '@playwright/test';
import {
  auditF03Request,
  captureF03WorkspaceEvidence,
  createF03RuntimeErrorAudit,
  fulfillF03Json,
  hasF03VisualEvidenceTarget,
  installF03BrowserStartup,
  isF03G2RequestAllowlist,
  type F03RequestAuditEntry
} from './f03-browser-fixture';

const fixtureSchema = 'f03-g3-workspace.v1';
const projectA = '11111111-1111-4111-8111-111111111111';
const projectB = '22222222-2222-4222-8222-222222222222';
const flowId = '33333333-3333-4333-8333-333333333333';

function projectPayload(projectId = projectA, overrides: Record<string, unknown> = {}) {
  return {
    id: projectId,
    name: projectId === projectA ? '瓶盖检测 A' : '瓶盖检测 B',
    description: 'F03 G2 Browser fixture',
    version: '1.0.0',
    persistenceRevision: projectId === projectA ? 7 : 8,
    flow: {
      id: flowId,
      name: '空流程',
      operators: [],
      connections: [],
      decisionConfiguration: null
    },
    globalSettings: {},
    globalVariables: {
      schemaVersion: '1.0',
      variables: [],
      sourceBindings: [],
      targetBindings: []
    },
    assets: {
      schemaVersion: 1,
      calibrationAssets: [],
      spatialAssets: []
    },
    createdAt: '2026-07-15T01:00:00Z',
    modifiedAt: '2026-07-15T02:00:00Z',
    lastOpenedAt: null,
    ...overrides
  };
}

function operatorMetadata(overrides: Record<string, unknown>) {
  return {
    type: 20,
    displayName: '全局阈值处理',
    description: '将灰度图像转换为二值图像。',
    categoryId: 1,
    category: '图像预处理',
    lifecycle: 0,
    lifecycleNote: null,
    defaultHidden: false,
    iconName: 'threshold',
    keywords: ['阈值', '二值化', 'threshold'],
    tags: ['image'],
    version: '1.0.0',
    inputPorts: [{ name: 'Image', displayName: '图像', dataType: 0, isRequired: true, description: null }],
    outputPorts: [{ name: 'Binary', displayName: '二值图', dataType: 0, isRequired: false, description: null }],
    parameters: [],
    ...overrides
  };
}

const operatorCatalog = Object.freeze([
  operatorMetadata({
    type: 0,
    displayName: '图像采集',
    description: '读取图像来源。',
    categoryId: 0,
    category: '采集',
    iconName: 'camera',
    keywords: ['采集', 'camera'],
    inputPorts: [
      { name: 'Trigger', displayName: '触发', dataType: 3, isRequired: false, description: null },
      { name: 'ExternalImage', displayName: '外部图像', dataType: 0, isRequired: false, description: null }
    ],
    outputPorts: [{ name: 'Image', displayName: '图像', dataType: 0, isRequired: false, description: null }]
  }),
  operatorMetadata({
    type: 20,
    parameters: [
      { name: 'Text', displayName: '文本', description: '字符串参数', dataType: 'string', defaultValue: '', minValue: null, maxValue: null, isRequired: false, options: null },
      { name: 'Count', displayName: '数量', description: '0 到 10 的整数', dataType: 'int', defaultValue: 0, minValue: 0, maxValue: 10, isRequired: true, options: null },
      { name: 'Enabled', displayName: '启用输出', description: '布尔参数', dataType: 'bool', defaultValue: false, minValue: null, maxValue: null, isRequired: false, options: null },
      { name: 'Mode', displayName: '模式', description: '枚举参数', dataType: 'enum', defaultValue: 'Auto', minValue: null, maxValue: null, isRequired: false, options: [{ label: '自动', value: 'Auto' }, { label: '手动', value: 'Manual' }] },
      { name: 'Gain', displayName: '增益', description: '显式 slider presentation', dataType: 'double', defaultValue: 0, minValue: 0, maxValue: 5, isRequired: false, options: null },
      { name: 'OptionalCount', displayName: '可空数量', description: '显式 nullable 参数', dataType: 'int', defaultValue: null, minValue: 0, maxValue: 10, isRequired: false, options: null },
      { name: 'FilePath', displayName: '文件路径', description: '延后到 Host file picker', dataType: 'file', defaultValue: '', minValue: null, maxValue: null, isRequired: false, options: null }
    ],
    parameterConstraints: [{
      parameter: 'Count', requiredPolicy: 'required', requiredWhen: null,
      enabledWhen: null, disabledWhen: null, visibleWhen: null, hiddenWhen: null,
      ignoredWhen: null, atLeastOneGroup: null, mutuallyExclusiveGroup: null,
      aliasFor: null, deprecated: false, resourceKind: null,
      reasonCode: 'COUNT_REQUIRED', satisfiedByInputPorts: []
    }],
    outputAvailabilityRules: [{
      output: 'Binary',
      availableWhen: { all: [{ parameter: 'Enabled', comparison: 'equals', value: true }] },
      reasonCode: 'BINARY_DISABLED'
    }]
  }),
  operatorMetadata({
    type: 238,
    displayName: '二值图转区域',
    description: '把二值图像转换为 Region。',
    categoryId: 2,
    category: '分割与区域',
    keywords: ['区域转换', 'BinaryImageToRegion'],
    inputPorts: [{ name: 'Image', displayName: '二值图', dataType: 0, isRequired: true, description: null }],
    outputPorts: [{ name: 'Region', displayName: '区域', dataType: 13, isRequired: false, description: null }]
  }),
  operatorMetadata({
    type: 240,
    displayName: '区域腐蚀',
    description: '腐蚀 Region。',
    categoryId: 2,
    category: '分割与区域',
    keywords: ['区域腐蚀', 'RegionErosion'],
    inputPorts: [{ name: 'Region', displayName: '区域', dataType: 13, isRequired: true, description: null }],
    outputPorts: [{ name: 'Region', displayName: '区域', dataType: 13, isRequired: false, description: null }]
  }),
  operatorMetadata({
    type: 17,
    displayName: '形态学（兼容）',
    lifecycle: 3,
    defaultHidden: true,
    keywords: ['形态学', 'morphology']
  })
]);

interface BootOptions {
  readonly workspaceEnabled?: boolean;
  readonly authStatus?: number;
  readonly projectStatus?: number | (() => number);
  readonly projectBody?: unknown | ((projectId: string) => unknown);
  readonly projectDelayMs?: number;
  readonly operatorCatalogBody?: unknown;
}

async function bootWorkspace(page: Page, options: BootOptions = {}) {
  const audit: F03RequestAuditEntry[] = [];
  await installF03BrowserStartup(page, options.workspaceEnabled ?? true);
  await page.route('**/health', route => fulfillF03Json(
    route,
    200,
    { status: 'Healthy', port: 5177 },
    fixtureSchema
  ));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF03Request(request));
    if (url.pathname === '/api/auth/me') {
      const status = options.authStatus ?? 200;
      await fulfillF03Json(route, status, status === 200
        ? { userId: 'f03-user', username: 'f03-engineer', role: 'Engineer' }
        : { code: 'AUTH_REQUIRED' }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/operators/library' && url.search === '?includeCompatibility=true') {
      await fulfillF03Json(route, 200, options.operatorCatalogBody ?? operatorCatalog, fixtureSchema);
      return;
    }
    const operatorMatch = url.pathname.match(/^\/api\/operators\/(\d+|[A-Za-z][A-Za-z0-9_]*)\/metadata$/);
    if (operatorMatch) {
      const metadata = operatorCatalog.find(item => String(item.type) === operatorMatch[1]);
      await fulfillF03Json(route, metadata ? 200 : 404, metadata ?? { code: 'OPERATOR_NOT_FOUND' }, fixtureSchema);
      return;
    }
    const projectMatch = url.pathname.match(
      /^\/api\/projects\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i
    );
    if (projectMatch) {
      if (options.projectDelayMs) await new Promise(resolve => setTimeout(resolve, options.projectDelayMs));
      const id = projectMatch[1]!;
      const status = typeof options.projectStatus === 'function'
        ? options.projectStatus()
        : options.projectStatus ?? 200;
      const body = typeof options.projectBody === 'function'
        ? options.projectBody(id)
        : options.projectBody ?? projectPayload(id);
      await fulfillF03Json(route, status, body, fixtureSchema);
      return;
    }
    await fulfillF03Json(route, 404, { code: 'UNEXPECTED_F03_ROUTE' }, fixtureSchema);
  });
  await page.goto(`/studio/index.html#/projects/${projectA}/workspace`);
  await expect(page.locator('[data-evidence-surface="f03-workspace-shell"]')).toBeVisible();
  return audit;
}

async function workspaceDiagnostics(page: Page) {
  return page.evaluate(() => {
    const diagnostics = (window as typeof window & {
      __STUDIO_UI_WORKSPACE_DIAGNOSTICS__?: Record<string, unknown>;
    }).__STUDIO_UI_WORKSPACE_DIAGNOSTICS__;
    return diagnostics ? { ...diagnostics } : null;
  });
}

async function searchOperator(page: Page, value: string) {
  const search = page.locator('[data-testid="operator-search"]');
  await search.fill(value);
  return page.locator('.operator-item');
}

async function dragOperator(page: Page, name: string, x: number, y: number) {
  const item = await searchOperator(page, name);
  await expect(item).toHaveCount(1);
  await item.dragTo(page.locator('[data-testid="flow-canvas"]'), {
    targetPosition: { x, y }
  });
}

function fixtureUuid(seed: number): string {
  return `aaaaaaaa-aaaa-4aaa-8aaa-${seed.toString(16).padStart(12, '0')}`;
}

function performanceFlow(nodeCount: number, connectionCount: number) {
  const operators = Array.from({ length: nodeCount }, (_, index) => {
    const id = fixtureUuid(index + 1);
    return {
      id,
      name: `节点 ${index + 1}`,
      type: 20,
      metadata: null,
      x: 40 + (index % 20) * 180,
      y: 40 + Math.floor(index / 20) * 100,
      inputPorts: Array.from({ length: 5 }, (_unused, port) => ({
        id: fixtureUuid(10_000 + index * 10 + port),
        name: `Input${port}`,
        direction: 0,
        dataType: 0,
        isRequired: false
      })),
      outputPorts: Array.from({ length: 5 }, (_unused, port) => ({
        id: fixtureUuid(20_000 + index * 10 + port),
        name: `Output${port}`,
        direction: 1,
        dataType: 0,
        isRequired: false
      })),
      parameters: [],
      isEnabled: true,
      executionStatus: 0,
      executionTimeMs: null,
      errorMessage: null
    };
  });
  const connections = Array.from({ length: connectionCount }, (_unused, index) => {
    const sourceIndex = index % Math.max(1, nodeCount - 1);
    const targetIndex = (sourceIndex + 1 + Math.floor(index / Math.max(1, nodeCount - 1))) % nodeCount;
    const port = Math.floor(index / Math.max(1, nodeCount - 1)) % 5;
    return {
      id: fixtureUuid(30_000 + index),
      sourceOperatorId: operators[sourceIndex]!.id,
      sourcePortId: operators[sourceIndex]!.outputPorts[port]!.id,
      targetOperatorId: operators[targetIndex]!.id,
      targetPortId: operators[targetIndex]!.inputPorts[port]!.id
    };
  });
  return {
    id: flowId,
    name: `${nodeCount}/${connectionCount} 性能流程`,
    operators,
    connections,
    decisionConfiguration: null
  };
}

function inspectorFlow() {
  const sourceNodeId = fixtureUuid(40_001);
  const targetNodeId = fixtureUuid(40_002);
  const outputPortId = fixtureUuid(40_101);
  const inputPortId = fixtureUuid(40_102);
  const parameter = (
    seed: number,
    name: string,
    dataType: string,
    value: unknown,
    overrides: Record<string, unknown> = {}
  ) => ({
    id: fixtureUuid(seed),
    name,
    displayName: name,
    description: `${name} Browser parameter`,
    dataType,
    value,
    defaultValue: value,
    minValue: null,
    maxValue: null,
    isRequired: false,
    options: null,
    ...overrides
  });
  const parameters = [
    parameter(41_001, 'Text', 'string', ''),
    parameter(41_002, 'Count', 'int', 0, { minValue: 0, maxValue: 10, isRequired: true }),
    parameter(41_003, 'Enabled', 'bool', false),
    parameter(41_004, 'Mode', 'enum', 'Auto', {
      options: [{ label: '自动', value: 'Auto' }, { label: '手动', value: 'Manual' }]
    }),
    parameter(41_005, 'Gain', 'double', 0, { minValue: 0, maxValue: 5, showSlider: true }),
    parameter(41_006, 'OptionalCount', 'int', null, { minValue: 0, maxValue: 10, nullable: true }),
    parameter(41_007, 'FilePath', 'file', '')
  ];
  return {
    id: flowId,
    name: 'G3 Inspector flow',
    futureFlowField: { schema: 3 },
    operators: [{
      id: sourceNodeId,
      name: 'Inspector Source',
      type: 20,
      metadata: null,
      x: 80,
      y: 100,
      inputPorts: [],
      outputPorts: [{
        id: outputPortId,
        name: 'Binary',
        direction: 1,
        dataType: 0,
        isRequired: false,
        futurePortField: 'keep-port'
      }],
      parameters,
      isEnabled: true,
      executionStatus: 2,
      executionTimeMs: 9,
      errorMessage: null,
      futureOperatorField: 'keep-operator'
    }, {
      id: targetNodeId,
      name: 'Inspector Target',
      type: 20,
      metadata: null,
      x: 360,
      y: 100,
      inputPorts: [{ id: inputPortId, name: 'Image', direction: 0, dataType: 0, isRequired: true }],
      outputPorts: [],
      parameters: parameters.map((item, index) => ({ ...item, id: fixtureUuid(42_000 + index) })),
      isEnabled: true,
      executionStatus: 0,
      executionTimeMs: null,
      errorMessage: null
    }],
    connections: [{
      id: fixtureUuid(43_001),
      sourceOperatorId: sourceNodeId,
      sourcePortId: outputPortId,
      targetOperatorId: targetNodeId,
      targetPortId: inputPortId,
      futureConnectionField: 'keep-connection'
    }],
    decisionConfiguration: null
  };
}

async function selectInspectorNode(page: Page, x: number, y: number) {
  const canvas = page.locator('[data-testid="flow-canvas"]');
  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  await page.mouse.click(box!.x + x, box!.y + y);
  return { canvas, box: box! };
}

test('flag off keeps Workspace owner/resources at zero and skips the Project GET', async ({ page }) => {
  const audit = await bootWorkspace(page, { workspaceEnabled: false });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(shell).toHaveAttribute('data-workspace-state', 'flag-off');
  await expect(shell).toHaveAttribute('data-workspace-owner-count', '0');
  expect(audit.filter(entry => entry.path.startsWith('/api/projects/'))).toEqual([]);
  expect(await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    activeSubscriptions: 0,
    inFlightReads: 0
  });
  expect(isF03G2RequestAllowlist(audit)).toBe(true);
});

test('flag on mounts one owner only after full decode and disposes on route leave/project switch', async ({ page }) => {
  const audit = await bootWorkspace(page);
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(shell).toHaveAttribute('data-workspace-state', 'empty');
  await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  expect(await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 1,
    activeProjectId: projectA,
    flowCanvasOwnerCount: 1,
    inFlightReads: 0
  });
  expect(audit.filter(entry => entry.path === `/api/projects/${projectA}`)).toHaveLength(1);

  await page.goto(`/studio/index.html#/projects/${projectB}/workspace`);
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectB);
  await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  expect(await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 1,
    activeProjectId: projectB,
    lastDisposedProjectId: projectA,
    lastDisposedResources: {
      activeSubscriptions: 0,
      activeTimers: 0,
      activeAnimationFrames: 0,
      activeObservers: 0,
      activeAbortControllers: 0,
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads: 0,
      inFlightWrites: 0,
      inFlightPreview: 0,
      inFlightExecute: 0
    }
  });

  await page.goto('/studio/index.html#/about');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    activeSubscriptions: 0,
    activeAbortControllers: 0,
    inFlightReads: 0
  });
  expect(isF03G2RequestAllowlist(audit)).toBe(true);
});

test('renders loading before the Project read settles', async ({ page }) => {
  const boot = bootWorkspace(page, { projectDelayMs: 300 });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(shell).toHaveAttribute('data-workspace-state', 'loading');
  await boot;
  await expect(shell).toHaveAttribute('data-workspace-state', 'empty');
});

test('Operator Rail supports search, category, click-add and drag-add', async ({ page }) => {
  const audit = await bootWorkspace(page);
  const rail = page.locator('[data-evidence-surface="f03-g2-operator-rail"]');
  const canvas = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  await expect(rail).toHaveAttribute('data-catalog-phase', 'success');

  const threshold = await searchOperator(page, '二值化');
  await expect(threshold).toHaveCount(1);
  await threshold.click();
  await expect(canvas).toHaveAttribute('data-node-count', '1');
  await expect(canvas).toHaveAttribute('data-flow-revision', '1');

  await page.locator('[data-testid="operator-search"]').fill('');
  await page.locator('[data-category="SegmentationAndRegion"]').click();
  await expect(page.locator('.operator-item')).toHaveCount(2);
  await dragOperator(page, '二值图转区域', 120, 120);
  await expect(canvas).toHaveAttribute('data-node-count', '2');
  await expect(canvas).toHaveAttribute('data-flow-revision', '2');
  expect(isF03G2RequestAllowlist(audit)).toBe(true);
});

test('node selection, move, copy/paste, undo/redo, delete and focus/IME gates stay scoped', async ({ page }) => {
  await bootWorkspace(page);
  const surface = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  const canvas = page.locator('[data-testid="flow-canvas"]');
  const threshold = await searchOperator(page, '二值化');
  await threshold.click();
  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  const nodeX = box!.x + box!.width / 2 + 24;
  const nodeY = box!.y + box!.height / 2 + 24;

  await page.mouse.click(nodeX, nodeY);
  await expect(surface).toHaveAttribute('data-selected-count', '1');
  await page.mouse.move(nodeX, nodeY);
  await page.mouse.down();
  await page.mouse.move(nodeX + 60, nodeY + 30, { steps: 5 });
  await page.mouse.up();
  await expect(surface).toHaveAttribute('data-flow-revision', '2');

  await page.keyboard.press('Control+c');
  await page.keyboard.press('Control+v');
  await expect(surface).toHaveAttribute('data-node-count', '2');
  await page.keyboard.press('Control+z');
  await expect(surface).toHaveAttribute('data-node-count', '1');
  await page.keyboard.press('Control+y');
  await expect(surface).toHaveAttribute('data-node-count', '2');

  const search = page.locator('[data-testid="operator-search"]');
  await search.focus();
  await page.keyboard.press('Control+a');
  await page.keyboard.press('Backspace');
  await expect(surface).toHaveAttribute('data-node-count', '2');

  await page.mouse.click(nodeX + 65, nodeY + 35);
  await expect(surface).toHaveAttribute('data-selected-count', '1');
  await canvas.dispatchEvent('keydown', {
    key: 'Delete',
    code: 'Delete',
    isComposing: true,
    bubbles: true,
    cancelable: true
  });
  await expect(surface).toHaveAttribute('data-node-count', '2');
  await page.keyboard.press('Delete');
  await expect(surface).toHaveAttribute('data-node-count', '1');
});

test('pointer wiring creates, rejects and disconnects connections with stable feedback', async ({ page }) => {
  await bootWorkspace(page);
  const surface = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  const canvas = page.locator('[data-testid="flow-canvas"]');
  await dragOperator(page, '图像采集', 80, 100);
  await dragOperator(page, '全局阈值处理', 360, 100);
  await dragOperator(page, '区域腐蚀', 360, 260);
  await expect(surface).toHaveAttribute('data-node-count', '3');

  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  const point = (x: number, y: number) => ({ x: box!.x + x, y: box!.y + y });
  const sourceOutput = point(220, 152);
  const thresholdInput = point(360, 142);
  await page.mouse.move(sourceOutput.x, sourceOutput.y);
  await page.mouse.down();
  await page.mouse.move(thresholdInput.x, thresholdInput.y, { steps: 8 });
  await page.mouse.up();
  await expect(surface).toHaveAttribute('data-connection-count', '1');

  const thresholdOutput = point(500, 142);
  const regionInput = point(360, 302);
  await page.mouse.move(thresholdOutput.x, thresholdOutput.y);
  await page.mouse.down();
  await page.mouse.move(regionInput.x, regionInput.y, { steps: 8 });
  await page.mouse.up();
  await expect(surface).toHaveAttribute('data-connection-count', '1');
  await expect(page.locator('.flow-canvas-surface__status')).toContainText(/不是 Region|不匹配|不兼容/);

  await page.mouse.click(thresholdInput.x, thresholdInput.y);
  await expect(surface).toHaveAttribute('data-connection-count', '0');
});

test('G3 Inspector follows empty, node, multi-node and connection selection from Canvas', async ({ page }) => {
  await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() })
  });
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'empty');

  const { box } = await selectInspectorNode(page, 120, 125);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'node');
  await expect(inspector).toHaveAttribute('data-metadata-phase', 'ready');
  await expect(inspector).toContainText('Inspector Source');
  await expect(inspector.locator('[data-parameter-name]')).toHaveCount(7);

  await page.keyboard.down('Control');
  await page.mouse.click(box.x + 400, box.y + 125);
  await page.keyboard.up('Control');
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'multi-node');
  await expect(inspector.locator('.inspector-panel__summary-node')).toHaveCount(2);

  await page.mouse.click(box.x + 540, box.y + 300);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'empty');

  await page.mouse.click(box.x + 290, box.y + 142);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'connection');
  await expect(inspector).toContainText('Inspector Source');
  await expect(inspector).toContainText('Inspector Target');
});

test('G3 Inspector edits primitive, slider and nullable parameters with validation/history/focus isolation', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 600 });
  await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() })
  });
  const surface = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  await selectInspectorNode(page, 120, 125);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'node');
  await expect(surface).toHaveAttribute('data-flow-revision', '0');

  const textInput = inspector.locator('[data-parameter-name="Text"] input[type="text"]');
  await textInput.fill('0');
  await textInput.press('Enter');
  await expect(surface).toHaveAttribute('data-flow-revision', '1');

  const countInput = inspector.locator('[data-parameter-name="Count"] input[type="number"]');
  await countInput.fill('11');
  await countInput.press('Enter');
  await expect(surface).toHaveAttribute('data-flow-revision', '1');
  await expect(inspector.locator('.inspector-panel__validation')).toContainText('不能大于 10');
  await countInput.fill('10');
  await countInput.press('Enter');
  await expect(surface).toHaveAttribute('data-flow-revision', '2');

  const booleanInput = inspector.locator('[data-parameter-name="Enabled"] input[type="checkbox"]');
  await booleanInput.check();
  await expect(surface).toHaveAttribute('data-flow-revision', '3');
  await booleanInput.uncheck();
  await expect(surface).toHaveAttribute('data-flow-revision', '4');

  await inspector.locator('[data-parameter-name="Mode"] select').selectOption('Manual');
  await expect(surface).toHaveAttribute('data-flow-revision', '5');

  await inspector.locator('[data-parameter-name="Gain"] input[type="range"]').evaluate(element => {
    const input = element as HTMLInputElement;
    input.value = '4';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await expect(surface).toHaveAttribute('data-flow-revision', '6');

  const nullable = inspector.locator('[data-parameter-name="OptionalCount"]');
  await nullable.locator('.parameter-editor__nullable input').uncheck();
  await nullable.locator('input[type="number"]').fill('0');
  await nullable.locator('input[type="number"]').press('Enter');
  await expect(surface).toHaveAttribute('data-flow-revision', '7');
  await nullable.locator('.parameter-editor__nullable input').check();
  await expect(surface).toHaveAttribute('data-flow-revision', '8');

  const name = inspector.locator('.inspector-panel__field input');
  await name.fill('Renamed Source');
  await name.press('Enter');
  await expect(surface).toHaveAttribute('data-flow-revision', '9');
  const enabled = inspector.locator('.inspector-panel__check input');
  await enabled.uncheck();
  await expect(surface).toHaveAttribute('data-flow-revision', '10');

  await page.locator('[data-flow-command="undo"]').click();
  await expect(surface).toHaveAttribute('data-flow-revision', '11');
  await expect(enabled).toBeChecked();
  await page.locator('[data-flow-command="redo"]').click();
  await expect(surface).toHaveAttribute('data-flow-revision', '12');
  await expect(enabled).not.toBeChecked();

  await textInput.focus();
  await textInput.fill('draft-only');
  await page.keyboard.press('Control+z');
  await expect(surface).toHaveAttribute('data-flow-revision', '12');
  await expect(surface).toHaveAttribute('data-selected-count', '1');

  const body = inspector.locator('.inspector-panel__body');
  const scale = await surface.getAttribute('data-scale');
  await body.hover();
  await page.mouse.wheel(0, 420);
  await expect.poll(() => body.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  await expect(surface).toHaveAttribute('data-scale', scale!);

  const canvas = page.locator('[data-testid="flow-canvas"]');
  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  await countInput.fill('11');
  await page.mouse.click(box!.x + 400, box!.y + 125);
  await expect(inspector).toContainText('Inspector Target');
  await expect(inspector).toHaveAttribute('data-active-drafts', '0');
  await expect(inspector.locator('.inspector-panel__validation')).toHaveCount(0);
});

test('G3 connection Inspector selects endpoints and disconnects through the typed command', async ({ page }) => {
  await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() })
  });
  const surface = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  const canvas = page.locator('[data-testid="flow-canvas"]');
  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  await page.mouse.click(box!.x + 290, box!.y + 142);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'connection');
  await inspector.locator('.inspector-panel__connection button').first().click();
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'node');
  await expect(inspector).toContainText('Inspector Source');

  await page.mouse.click(box!.x + 290, box!.y + 142);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'connection');
  await inspector.locator('.inspector-panel__danger').click();
  await expect(surface).toHaveAttribute('data-connection-count', '0');
  await expect(surface).toHaveAttribute('data-flow-revision', '1');
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'empty');
});

test('G3 Inspector shows metadata missing without enabling parameter writes', async ({ page }) => {
  await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    operatorCatalogBody: []
  });
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  await selectInspectorNode(page, 120, 125);
  await expect(inspector).toHaveAttribute('data-metadata-phase', 'missing');
  await expect(inspector.locator('[data-parameter-name="Text"] input')).toBeDisabled();
});

test('G3 Inspector shows metadata decode failure without enabling parameter writes', async ({ page }) => {
  await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    operatorCatalogBody: [{ ...operatorCatalog[1], parameters: 'invalid' }]
  });
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  await selectInspectorNode(page, 120, 125);
  await expect(inspector).toHaveAttribute('data-metadata-phase', 'error');
  await expect(inspector.locator('[data-parameter-name="Text"] input')).toBeDisabled();
});

test('G3 Inspector is fully unmounted when a later Project read is forbidden', async ({ page }) => {
  let projectStatus = 200;
  await bootWorkspace(page, {
    projectStatus: () => projectStatus,
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() })
  });
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  await selectInspectorNode(page, 120, 125);
  projectStatus = 403;
  await page.goto('/studio/index.html#/about');
  await page.goto(`/studio/index.html#/projects/${projectA}/workspace`);
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(shell).toHaveAttribute('data-workspace-state', 'forbidden');
  await expect(shell).toHaveAttribute('data-workspace-inspector-owner-count', '0');
  await expect(inspector).toHaveCount(0);
});

test('passes 20 project switches with one owner and a zero final resource ledger', async ({ page }) => {
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() })
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  for (let cycle = 0; cycle < 20; cycle += 1) {
    const projectId = cycle % 2 === 0 ? projectB : projectA;
    await page.goto(`/studio/index.html#/projects/${projectId}/workspace`);
    await expect(shell).toHaveAttribute('data-workspace-project-id', projectId);
    await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
      workspaceOwnerCount: 1,
      flowCanvasOwnerCount: 1,
      inspectorOwnerCount: 1,
      activeProjectId: projectId,
      ownerConflictCount: 0
    });
    await selectInspectorNode(page, 120, 125);
    await expect(shell).toHaveAttribute('data-workspace-inspector-owner-count', '1');
  }
  await page.goto('/studio/index.html#/about');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    flowCanvasOwnerCount: 0,
    inspectorOwnerCount: 0,
    activeInspectorDrafts: 0,
    activeSubscriptions: 0,
    activeTimers: 0,
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: 0,
    inFlightReads: 0,
    ownerConflictCount: 0
  });
  expect(isF03G2RequestAllowlist(audit)).toBe(true);
});

for (const fixture of [
  { nodes: 100, connections: 150 },
  { nodes: 300, connections: 450 }
] as const) {
  test(`formal Workspace records ${fixture.nodes}/${fixture.connections} route-ready and interaction samples`, async ({ page }) => {
    const samples: number[] = [];
    const flow = performanceFlow(fixture.nodes, fixture.connections);
    await bootWorkspace(page, {
      projectBody: projectId => projectPayload(projectId, { flow })
    });

    for (let sample = 0; sample < 7; sample += 1) {
      const projectId = `99999999-9999-4999-8999-${(fixture.nodes * 100 + sample).toString().padStart(12, '0')}`;
      const started = Date.now();
      await page.goto(`/studio/index.html#/projects/${projectId}/workspace`);
      const surface = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
      await expect(surface).toHaveAttribute('data-node-count', String(fixture.nodes));
      await expect(surface).toHaveAttribute('data-connection-count', String(fixture.connections));
      if (sample >= 2) samples.push(Date.now() - started);
      const canvas = page.locator('[data-testid="flow-canvas"]');
      const box = await canvas.boundingBox();
      if (box) {
        await page.mouse.move(box.x + 60, box.y + 60);
        await page.mouse.down();
        await page.mouse.move(box.x + 90, box.y + 80, { steps: 3 });
        await page.mouse.up();
        await canvas.hover();
        await page.mouse.wheel(0, -120);
      }
      await page.goto('/studio/index.html#/about');
      await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
        workspaceOwnerCount: 0,
        flowCanvasOwnerCount: 0,
        activeSubscriptions: 0,
        activeAnimationFrames: 0,
        activeObservers: 0
      });
    }

    const sorted = [...samples].sort((left, right) => left - right);
    console.log(`[F03_G2_PERF] ${JSON.stringify({
      fixture: `${fixture.nodes}/${fixture.connections}`,
      warmups: 2,
      samples,
      medianMs: sorted[Math.floor(sorted.length / 2)],
      maxMs: sorted.at(-1)
    })}`);
    expect(samples).toHaveLength(5);
  });
}

for (const scenario of [
  { label: '401', options: { authStatus: 401 }, state: 'unauthorized', readonly: 'false' },
  { label: '403/readonly', options: { projectStatus: 403 }, state: 'forbidden', readonly: 'true' },
  { label: '404', options: { projectStatus: 404 }, state: 'not-found', readonly: 'false' },
  {
    label: 'decode-error',
    options: { projectBody: { id: projectA, operatorCount: 40, connectionCount: 50 } },
    state: 'decode-error',
    readonly: 'false'
  }
] as const) {
  test(`renders ${scenario.label} with owner=0`, async ({ page }) => {
    const audit = await bootWorkspace(page, scenario.options);
    const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
    await expect(shell).toHaveAttribute('data-workspace-state', scenario.state);
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '0');
    await expect(shell).toHaveAttribute('data-workspace-readonly', scenario.readonly);
    expect(await workspaceDiagnostics(page)).toMatchObject({ workspaceOwnerCount: 0 });
    expect(isF03G2RequestAllowlist(audit)).toBe(true);
  });
}

test('passes 20 real Browser route mount/unmount cycles with a zero ledger', async ({ page }) => {
  const audit = await bootWorkspace(page);
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');

  for (let cycle = 0; cycle < 20; cycle += 1) {
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
    await page.goto('/studio/index.html#/about');
    await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
      workspaceOwnerCount: 0,
      flowCanvasOwnerCount: 0,
      activeSubscriptions: 0,
      activeAbortControllers: 0,
      inFlightReads: 0
    });
    await page.goto(`/studio/index.html#/projects/${projectA}/workspace`);
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  }

  await page.goto('/studio/index.html#/about');
  const final = await workspaceDiagnostics(page);
  expect(final).toMatchObject({
    workspaceOwnerCount: 0,
    activeSubscriptions: 0,
    activeAbortControllers: 0,
    inFlightReads: 0,
    totalWorkspaceMounts: 21,
    totalWorkspaceDisposals: 21,
    ownerConflictCount: 0
  });
  expect(isF03G2RequestAllowlist(audit)).toBe(true);
});

for (const viewport of [
  { width: 1366, height: 768 },
  { width: 1366, height: 600 }
] as const) {
  test(`Workspace Shell fits ${viewport.width}x${viewport.height} without global overflow`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const runtimeErrors = createF03RuntimeErrorAudit(page);
    const audit = await bootWorkspace(page);
    const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
    await expect(shell).toHaveAttribute('data-workspace-state', 'empty');

    const layout = await page.evaluate(() => {
      const toolbar = document.querySelector('.workspace-shell__toolbar')?.getBoundingClientRect();
      const status = document.querySelector('.workspace-shell__statusbar')?.getBoundingClientRect();
      const canvas = document.querySelector('.flow-canvas-surface__stage')?.getBoundingClientRect();
      return {
        horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        verticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight,
        toolbar: toolbar ? { top: toolbar.top, bottom: toolbar.bottom } : null,
        status: status ? { top: status.top, bottom: status.bottom } : null,
        canvas: canvas ? { width: canvas.width, height: canvas.height } : null,
        viewport: { width: window.innerWidth, height: window.innerHeight }
      };
    });

    expect(layout).toMatchObject({
      horizontalOverflow: 0,
      verticalOverflow: 0,
      viewport
    });
    expect(layout.toolbar?.top).toBeGreaterThanOrEqual(0);
    expect(layout.status?.bottom).toBeLessThanOrEqual(viewport.height + 1);
    expect(layout.canvas?.height).toBeGreaterThanOrEqual(300);
    expect(isF03G2RequestAllowlist(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });

    if (hasF03VisualEvidenceTarget()) {
      await captureF03WorkspaceEvidence(page, {
        scenario: `workspace-shell-${viewport.height}`,
        viewport,
        requests: audit,
        runtimeErrors
      });
    }
  });
}
