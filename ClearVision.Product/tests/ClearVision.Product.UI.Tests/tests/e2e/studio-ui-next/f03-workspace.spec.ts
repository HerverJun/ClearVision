import { createHash } from 'node:crypto';
import { expect, Page, Route, test } from '@playwright/test';
import {
  auditF03Request,
  captureF03WorkspaceEvidence,
  createF03RuntimeErrorAudit,
  fulfillF03Json,
  hasF03VisualEvidenceTarget,
  installF03BrowserStartup,
  isF03G4RequestAllowlist,
  isF03G5RequestAllowlist,
  isF03G6RequestAllowlist,
  type F03RequestAuditEntry
} from './f03-browser-fixture';
import {
  captureF04VisualEvidence,
  createF04RuntimeErrorAudit,
  hasF04VisualEvidenceTarget
} from './f04-browser-evidence';

const fixtureSchema = 'f03-g5-workspace.v1';
const projectA = '11111111-1111-4111-8111-111111111111';
const projectB = '22222222-2222-4222-8222-222222222222';
const flowId = '33333333-3333-4333-8333-333333333333';
const goldenSourceNodeId = 'aaaaaaaa-aaaa-4aaa-8aaa-00000000c351';
const goldenRoiNodeId = 'aaaaaaaa-aaaa-4aaa-8aaa-00000000c352';
const goldenJudgeNodeId = 'aaaaaaaa-aaaa-4aaa-8aaa-00000000c353';
const goldenJudgeOutputId = 'aaaaaaaa-aaaa-4aaa-8aaa-00000000c3b5';
const goldenResultId = 'aaaaaaaa-aaaa-4aaa-8aaa-000000015f91';
const previewImage = Buffer.from(
  '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100">' +
  '<rect width="100" height="100" fill="#203040"/><circle cx="50" cy="50" r="24" fill="#7dd3fc"/></svg>',
  'utf8'
);
const previewImageSha256 = createHash('sha256').update(previewImage).digest('hex');

function deferred<T = void>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>(next => { resolve = next; });
  return { promise, resolve };
}

async function requestStudioHashNavigation(page: Page, hash: string): Promise<void> {
  await page.evaluate(nextHash => { window.location.hash = nextHash; }, hash);
}

async function resolveLeavePrompt(page: Page, decision: 'stay' | 'discard'): Promise<void> {
  const action = page.locator(`[data-testid="leave-guard-${decision}"]`);
  await expect(action).toBeVisible();
  await action.click();
}

function previewArtifactId(call: number): string {
  return createHash('sha256').update(`f03-preview-artifact-${call}`).digest('base64url');
}

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

function previewArtifactReference(call: number) {
  return {
    artifactId: previewArtifactId(call),
    kind: 'image',
    role: 'outputImage',
    pathHint: '$.output',
    contentType: 'image/svg+xml',
    length: previewImage.length,
    sha256: previewImageSha256,
    createdAtUtc: '2026-07-17T00:00:00Z',
    expiresAtUtc: '2026-07-17T00:10:00Z',
    width: 100,
    height: 100,
    channels: 4
  };
}

function previewPayload(
  request: Readonly<Record<string, unknown>>,
  call: number,
  overrides: Record<string, unknown> = {}
) {
  const success = overrides.success !== false;
  const executionTimeMs = 5 + call;
  const errorMessage = success ? null : String(overrides.errorMessage ?? 'Preview fixture failure');
  return {
    success,
    projectId: String(request.projectId),
    targetNodeId: String(request.targetNodeId),
    debugSessionId: String(request.debugSessionId),
    executionTimeMs,
    inputImageBase64: null,
    outputImageBase64: null,
    outputData: { call, targetNodeId: String(request.targetNodeId) },
    errorMessage,
    failedOperatorId: success ? null : String(request.targetNodeId),
    failedOperatorName: success ? null : 'Preview fixture node',
    failedOperatorType: success ? null : 'FixtureOperator',
    diagnostics: [],
    missingResources: [],
    artifacts: [],
    observation: {
      schemaVersion: 'execution-observation.v1',
      identity: {
        projectId: String(request.projectId),
        targetNodeId: String(request.targetNodeId),
        debugSessionId: String(request.debugSessionId),
        clientRequestSequence: Number(request.clientRequestSequence),
        flowRevision: Number(request.flowRevision)
      },
      outcome: {
        success,
        executionTimeMs,
        errorMessage,
        failedOperatorId: success ? null : String(request.targetNodeId),
        failedOperatorName: success ? null : 'Preview fixture node',
        failedOperatorType: success ? null : 'FixtureOperator',
        executedOperatorCount: 1
      },
      diagnostics: []
    },
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
    type: 8,
    displayName: '宽度测量',
    description: '测量 ROI 的密封宽度并输出浮点结果。',
    categoryId: 3,
    category: '测量',
    keywords: ['宽度', 'measurement'],
    inputPorts: [{ name: 'Region', displayName: '区域', dataType: 13, isRequired: true, description: null }],
    outputPorts: [{ name: 'Width', displayName: '宽度', dataType: 2, isRequired: false, description: null }],
    parameters: [{
      name: 'Tolerance', displayName: '允许偏差', description: '密封宽度允许偏差。', dataType: 'double',
      defaultValue: 12.5, minValue: 0, maxValue: 100, isRequired: true, options: null
    }]
  }),
  operatorMetadata({
    type: 'RoiManager',
    displayName: 'ROI Manager',
    description: 'Editable rectangular ROI fixture.',
    categoryId: 2,
    category: 'SegmentationAndRegion',
    keywords: ['rectangle', 'roi'],
    inputPorts: [],
    outputPorts: [{ name: 'Roi', displayName: 'ROI', dataType: 13, isRequired: false, description: null }],
    parameters: [{
      name: 'Shape',
      displayName: 'Shape',
      description: 'ROI shape',
      dataType: 'enum',
      defaultValue: 'Rectangle',
      minValue: null,
      maxValue: null,
      isRequired: true,
      options: [{ label: 'Rectangle', value: 'Rectangle' }]
    }, ...['X', 'Y', 'Width', 'Height'].map(name => ({
      name,
      displayName: name,
      description: `${name} image coordinate`,
      dataType: 'double',
      defaultValue: name === 'X' || name === 'Y' ? 10 : name === 'Width' ? 30 : 20,
      minValue: 0,
      maxValue: 100,
      isRequired: true,
      options: null
    }))]
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
  readonly authRole?: 'Admin' | 'Engineer';
  readonly expectAuthShell?: boolean;
  readonly projectStatus?: number | (() => number);
  readonly projectBody?: unknown | ((projectId: string) => unknown);
  readonly projectDelayMs?: number;
  readonly projectGetScenario?: (
    projectId: string,
    current: Readonly<Record<string, unknown>>,
    call: number
  ) => Readonly<{ status?: number; body?: unknown; delayMs?: number; abort?: boolean }>;
  readonly projectPutScenario?: (
    request: Readonly<Record<string, unknown>>,
    current: Readonly<Record<string, unknown>>,
    call: number
  ) => Readonly<{
    status?: number;
    body?: unknown;
    delayMs?: number;
    abort?: boolean;
    authoritativeProject?: Readonly<Record<string, unknown>>;
  }>;
  readonly operatorCatalogBody?: unknown;
  readonly previewScenario?: (
    request: Readonly<Record<string, unknown>>,
    call: number
  ) => Readonly<{ status?: number; body?: unknown; delayMs?: number; abort?: boolean }>;
  readonly runScenario?: (
    stage: 'admission' | 'execute' | 'stop' | 'reconcile',
    request: Readonly<Record<string, unknown>>,
    call: number
  ) => Readonly<{ status?: number; body?: unknown; delayMs?: number; abort?: boolean }> |
    Promise<Readonly<{ status?: number; body?: unknown; delayMs?: number; abort?: boolean }>>;
}

async function bootWorkspace(page: Page, options: BootOptions = {}) {
  const audit: F03RequestAuditEntry[] = [];
  let previewCall = 0;
  let projectGetCall = 0;
  let projectPutCall = 0;
  let runCall = 0;
  const projects = new Map<string, Readonly<Record<string, unknown>>>();
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
    if (url.pathname === '/api/auth/setup-status') {
      await fulfillF03Json(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/auth/me') {
      const status = options.authStatus ?? 200;
      await fulfillF03Json(route, status, status === 200
        ? { userId: 'f03-user', username: 'f03-engineer', role: options.authRole ?? 'Engineer' }
        : { code: 'AUTH_REQUIRED' }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/cameras/bindings' && request.method() === 'GET') {
      await fulfillF03Json(route, 200, [{
        id: 'camera-a',
        displayName: '一号工位面阵相机',
        deviceId: 'CAM-G3-001',
        manufacturer: 'ClearVision Fixture',
        modelName: 'CV-FRAME-01',
        triggerMode: 'Software',
        isEnabled: true,
        connectionStatus: 'Connected'
      }], fixtureSchema);
      return;
    }
    if (url.pathname === '/api/cameras/soft-trigger-capture' && request.method() === 'POST') {
      await route.fulfill({
        status: 200,
        contentType: 'image/svg+xml',
        headers: {
          'Content-Length': String(previewImage.length),
          'X-Camera-Id': 'camera-a',
          'X-Trigger-Mode': 'Software',
          'X-Image-Width': '100',
          'X-Image-Height': '100',
          'x-clearvision-fixture-schema': fixtureSchema
        },
        body: previewImage
      });
      return;
    }
    if (url.pathname === '/api/inspection/decision-configuration/validate' && request.method() === 'POST') {
      const flow = request.postDataJSON() as Readonly<Record<string, unknown>>;
      const configured = (flow.decisionConfiguration as Readonly<Record<string, unknown>> | null)
        ?.finalDecisionBinding;
      await fulfillF03Json(route, 200, {
        isValid: Boolean(configured),
        issues: configured ? [] : [{
          code: 'DECISION_BINDING_REQUIRED',
          message: '请选择最终判定输出。',
          field: 'decisionConfiguration.finalDecisionBinding',
          operatorId: null,
          outputName: null
        }],
        eligibleOutputs: [goldenDecisionCandidate()]
      }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/flows/preview-node' && request.method() === 'POST') {
      previewCall += 1;
      const requestBody = request.postDataJSON() as Readonly<Record<string, unknown>>;
      const scenario = options.previewScenario?.(requestBody, previewCall) ?? {
        body: previewPayload(requestBody, previewCall)
      };
      if (scenario.delayMs) await new Promise(resolve => setTimeout(resolve, scenario.delayMs));
      if (scenario.abort) {
        await route.abort('failed');
        return;
      }
      await fulfillF03Json(
        route,
        scenario.status ?? 200,
        scenario.body ?? previewPayload(requestBody, previewCall),
        fixtureSchema
      );
      return;
    }
    if (url.pathname === '/api/inspection/admission' && request.method() === 'POST') {
      runCall += 1;
      const requestBody = request.postDataJSON() as Readonly<Record<string, unknown>>;
      const scenario = await (options.runScenario?.('admission', requestBody, runCall) ?? {});
      if (scenario.delayMs) await new Promise(resolve => setTimeout(resolve, scenario.delayMs));
      if (scenario.abort) {
        await route.abort('failed');
        return;
      }
      await fulfillF03Json(route, scenario.status ?? 200, scenario.body ?? {
        allowed: true,
        code: null,
        message: 'fixture admission allowed',
        projectId: requestBody.projectId,
        clientSnapshotId: requestBody.clientSnapshotId,
        projectPersistenceRevision: requestBody.expectedPersistenceRevision,
        canonicalFlowHash: 'fixture-persisted-flow-hash',
        decisionConfigurationHash: 'fixture-decision-hash',
        violations: []
      }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/inspection/stop' && request.method() === 'POST') {
      runCall += 1;
      const requestBody = request.postDataJSON() as Readonly<Record<string, unknown>>;
      const scenario = await (options.runScenario?.('stop', requestBody, runCall) ?? {});
      if (scenario.delayMs) await new Promise(resolve => setTimeout(resolve, scenario.delayMs));
      if (scenario.abort) {
        await route.abort('failed');
        return;
      }
      await fulfillF03Json(route, scenario.status ?? 200, scenario.body ?? {
        status: 'cancelled',
        code: 'RUN_CANCELLED',
        message: 'fixture authoritative cancellation',
        projectId: requestBody.projectId,
        clientSnapshotId: requestBody.clientSnapshotId,
        projectPersistenceRevision: requestBody.expectedPersistenceRevision,
        canonicalFlowHash: requestBody.expectedCanonicalFlowHash,
        decisionConfigurationHash: requestBody.expectedDecisionConfigurationHash,
        result: {
          id: fixtureUuid(90_002),
          projectId: requestBody.projectId,
          status: 'NotInspected',
          executionOutcome: 'Cancelled',
          decisionOutcome: 'NotApplicable',
          executionSnapshotId: requestBody.clientSnapshotId,
          projectPersistenceRevision: requestBody.expectedPersistenceRevision,
          flowVersionHash: requestBody.expectedCanonicalFlowHash,
          decisionConfigurationHash: requestBody.expectedDecisionConfigurationHash,
          errorMessage: null
        }
      }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/inspection/reconcile' && request.method() === 'POST') {
      runCall += 1;
      const requestBody = request.postDataJSON() as Readonly<Record<string, unknown>>;
      const scenario = await (options.runScenario?.('reconcile', requestBody, runCall) ?? {});
      if (scenario.delayMs) await new Promise(resolve => setTimeout(resolve, scenario.delayMs));
      if (scenario.abort) {
        await route.abort('failed');
        return;
      }
      await fulfillF03Json(route, scenario.status ?? 200, scenario.body ?? {
        status: 'result-not-found',
        code: 'RUN_RESULT_NOT_FOUND',
        message: 'fixture result not found',
        projectId: requestBody.projectId,
        clientSnapshotId: requestBody.clientSnapshotId,
        projectPersistenceRevision: requestBody.expectedPersistenceRevision,
        canonicalFlowHash: requestBody.expectedCanonicalFlowHash,
        decisionConfigurationHash: requestBody.expectedDecisionConfigurationHash,
        result: null
      }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/inspection/execute' && request.method() === 'POST') {
      runCall += 1;
      const requestBody = request.postDataJSON() as Readonly<Record<string, unknown>>;
      const scenario = await (options.runScenario?.('execute', requestBody, runCall) ?? {});
      if (scenario.delayMs) await new Promise(resolve => setTimeout(resolve, scenario.delayMs));
      if (scenario.abort) {
        await route.abort('failed');
        return;
      }
      await fulfillF03Json(route, scenario.status ?? 200, scenario.body ?? {
        id: fixtureUuid(90_001),
        projectId: requestBody.projectId,
        status: 'Completed',
        executionOutcome: 'Succeeded',
        decisionOutcome: 'Ok',
        executionSnapshotId: requestBody.clientSnapshotId,
        projectPersistenceRevision: requestBody.expectedPersistenceRevision,
        flowVersionHash: requestBody.expectedCanonicalFlowHash,
        decisionConfigurationHash: requestBody.expectedDecisionConfigurationHash,
        errorMessage: null
      }, fixtureSchema);
      return;
    }
    const runtimeValuesMatch = url.pathname.match(
      /^\/api\/projects\/([0-9a-f-]{36})\/global-variable-values$/i
    );
    if (runtimeValuesMatch && request.method() === 'GET') {
      await fulfillF03Json(route, 200, [{
        variableId: fixtureUuid(61_001),
        name: 'SealWidth',
        displayName: '密封宽度',
        valueType: 'Double',
        value: 12.8,
        version: 4,
        updatedAtUtc: '2026-07-22T01:02:03Z',
        updatedBy: 'FormalRun',
        runId: goldenResultId,
        operatorId: goldenJudgeNodeId
      }], fixtureSchema);
      return;
    }
    const runtimePackageMatch = url.pathname.match(
      /^\/api\/projects\/([0-9a-f-]{36})\/runtime-package\/export$/i
    );
    if (runtimePackageMatch && request.method() === 'POST') {
      await fulfillF03Json(route, 200, {
        packageRootPath: 'C:\\ClearVision\\Packages\\cvpkg-g3',
        packageId: 'cvpkg-g3-golden',
        packageName: '瓶盖检测 A',
        flowHash: 'fixture-persisted-flow-hash',
        decisionConfigurationHash: 'fixture-decision-hash',
        registeredForStationDeployment: true,
        stationPackageId: 'station-pkg-g3',
        readmePath: 'C:\\ClearVision\\Packages\\cvpkg-g3\\README.txt'
      }, fixtureSchema);
      return;
    }
    if (url.pathname === '/api/projects' && request.method() === 'GET') {
      await fulfillF03Json(route, 200, [projectPayload(projectA)], fixtureSchema);
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectA}/${goldenResultId}/evidence/manifest` &&
        request.method() === 'GET') {
      await fulfillF03Json(route, 200, goldenEvidenceManifest(), fixtureSchema);
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectA}/${goldenResultId}/evidence/export` &&
        request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/zip',
        headers: {
          'Content-Disposition': 'attachment; filename="g3-evidence.zip"',
          'Content-Length': String(previewImage.length),
          'x-clearvision-fixture-schema': fixtureSchema
        },
        body: previewImage
      });
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectA}/${goldenResultId}` && request.method() === 'GET') {
      await fulfillF03Json(route, 200, goldenResultDetail(), fixtureSchema);
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectA}` && request.method() === 'GET') {
      await fulfillF03Json(route, 200, {
        items: [goldenResultSummary()],
        totalCount: 1,
        pageIndex: Number(url.searchParams.get('pageIndex') ?? 0),
        pageSize: Number(url.searchParams.get('pageSize') ?? 20)
      }, fixtureSchema);
      return;
    }
    if (/^\/api\/preview-artifacts\/[A-Za-z0-9_-]{43}$/.test(url.pathname)) {
      if (request.method() === 'DELETE') {
        await route.fulfill({ status: 204, body: '' });
        return;
      }
      if (request.method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'image/svg+xml',
          headers: {
            'Content-Length': String(previewImage.length),
            ETag: `"${previewImageSha256}"`,
            'X-Artifact-Sha256': previewImageSha256,
            'x-clearvision-fixture-schema': fixtureSchema
          },
          body: previewImage
        });
        return;
      }
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
    const projectOpenMatch = url.pathname.match(
      /^\/api\/projects\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/open$/i
    );
    if (projectOpenMatch && request.method() === 'POST') {
      await fulfillF03Json(route, 200, {
        projectId: projectOpenMatch[1],
        lastOpenedAtUtc: '2026-07-19T00:00:00Z'
      }, fixtureSchema);
      return;
    }
    const projectMatch = url.pathname.match(
      /^\/api\/projects\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i
    );
    if (projectMatch) {
      if (options.projectDelayMs) await new Promise(resolve => setTimeout(resolve, options.projectDelayMs));
      const id = projectMatch[1]!;
      const current = projects.get(id) ?? structuredClone(
        (typeof options.projectBody === 'function'
          ? options.projectBody(id)
          : options.projectBody ?? projectPayload(id)) as Readonly<Record<string, unknown>>
      );
      projects.set(id, current);
      if (request.method() === 'PUT') {
        projectPutCall += 1;
        const requestBody = request.postDataJSON() as Readonly<Record<string, unknown>>;
        const scenario = options.projectPutScenario?.(requestBody, current, projectPutCall);
        if (scenario?.delayMs) await new Promise(resolve => setTimeout(resolve, scenario.delayMs));
        if (scenario?.authoritativeProject) {
          projects.set(id, structuredClone(scenario.authoritativeProject));
        }
        if (scenario?.abort) {
          await route.abort('failed');
          return;
        }
        const status = scenario?.status ?? 200;
        const responseBody = scenario?.body ?? {
          ...current,
          name: requestBody.name,
          description: requestBody.description,
          flow: requestBody.flow,
          globalVariables: requestBody.globalVariables,
          persistenceRevision: Number(current.persistenceRevision) + 1,
          modifiedAt: '2026-07-17T03:00:00Z'
        };
        if (status >= 200 && status < 300 && typeof responseBody === 'object' && responseBody !== null) {
          projects.set(id, structuredClone(responseBody as Readonly<Record<string, unknown>>));
        }
        await fulfillF03Json(route, status, responseBody, fixtureSchema);
        return;
      }
      projectGetCall += 1;
      const scenario = options.projectGetScenario?.(id, current, projectGetCall);
      if (scenario?.delayMs) await new Promise(resolve => setTimeout(resolve, scenario.delayMs));
      if (scenario?.abort) {
        await route.abort('failed');
        return;
      }
      const status = scenario?.status ?? (typeof options.projectStatus === 'function'
        ? options.projectStatus()
        : options.projectStatus ?? 200);
      await fulfillF03Json(route, status, scenario?.body ?? current, fixtureSchema);
      return;
    }
    await fulfillF03Json(route, 404, { code: 'UNEXPECTED_F03_ROUTE' }, fixtureSchema);
  });
  await page.goto(`/studio/index.html#/projects/${projectA}/workspace`);
  if (options.expectAuthShell) {
    await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  } else {
    await expect(page.locator('[data-evidence-surface="f03-workspace-shell"]')).toBeVisible();
  }
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
  await ensureOperatorFlyout(page);
  const search = page.locator('[data-testid="operator-search"]');
  await search.fill(value);
  return page.locator('.operator-item');
}

async function ensureOperatorFlyout(page: Page): Promise<void> {
  if (await page.locator('[data-capability="operator-flyout"]').count()) return;
  await page.getByRole('button', { name: '搜索与全部算子' }).click();
  await expect(page.locator('[data-capability="operator-flyout"]')).toBeVisible();
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

function goldenDecisionCandidate() {
  return {
    operatorId: goldenJudgeNodeId,
    operatorName: '密封宽度判定',
    outputPortId: goldenJudgeOutputId,
    outputName: 'Width',
    dataType: 'Float',
    rule: 'NumericComparison',
    defaultTrueMeansOk: null,
    defaultOkValue: null,
    defaultNgValue: null,
    requiredOkValue: null,
    requiredNgValue: null
  };
}

function goldenJourneyFlow() {
  const parameter = (seed: number, name: string, dataType: string, value: unknown, overrides: Record<string, unknown> = {}) => ({
    id: fixtureUuid(seed),
    name,
    displayName: name,
    description: `${name} G3 golden journey parameter`,
    dataType,
    value,
    defaultValue: value,
    minValue: null,
    maxValue: null,
    isRequired: false,
    options: null,
    ...overrides
  });
  const sourceOutputId = fixtureUuid(50_101);
  const roiInputId = fixtureUuid(50_102);
  const roiOutputId = fixtureUuid(50_103);
  const judgeInputId = fixtureUuid(50_104);
  return {
    id: flowId,
    name: 'G3 瓶盖密封黄金旅程',
    operators: [{
      id: goldenSourceNodeId,
      name: '一号工位相机采集',
      type: 0,
      metadata: null,
      x: 60,
      y: 100,
      inputPorts: [],
      outputPorts: [{ id: sourceOutputId, name: 'Image', direction: 1, dataType: 0, isRequired: false }],
      parameters: [
        parameter(51_001, 'SourceType', 'string', 'Camera'),
        parameter(51_002, 'CameraBindingId', 'CameraBinding', 'camera-a'),
        parameter(51_003, 'TriggerMode', 'string', 'Software'),
        parameter(51_004, 'ExposureTime', 'double', 1000),
        parameter(51_005, 'Gain', 'double', 2)
      ],
      isEnabled: true,
      executionStatus: 0,
      executionTimeMs: null,
      errorMessage: null
    }, {
      id: goldenRoiNodeId,
      name: '瓶盖密封区域 ROI',
      type: 'RoiManager',
      metadata: null,
      x: 280,
      y: 100,
      inputPorts: [{ id: roiInputId, name: 'Image', direction: 0, dataType: 0, isRequired: true }],
      outputPorts: [{ id: roiOutputId, name: 'Roi', direction: 1, dataType: 13, isRequired: false }],
      parameters: [
        parameter(52_001, 'Shape', 'enum', 'Rectangle', { options: [{ label: '矩形', value: 'Rectangle' }] }),
        parameter(52_002, 'X', 'double', 10, { minValue: 0, maxValue: 100, isRequired: true }),
        parameter(52_003, 'Y', 'double', 10, { minValue: 0, maxValue: 100, isRequired: true }),
        parameter(52_004, 'Width', 'double', 30, { minValue: 0, maxValue: 100, isRequired: true }),
        parameter(52_005, 'Height', 'double', 20, { minValue: 0, maxValue: 100, isRequired: true })
      ],
      isEnabled: true,
      executionStatus: 0,
      executionTimeMs: null,
      errorMessage: null
    }, {
      id: goldenJudgeNodeId,
      name: '密封宽度判定',
      type: 8,
      metadata: null,
      x: 480,
      y: 100,
      inputPorts: [{ id: judgeInputId, name: 'Region', direction: 0, dataType: 13, isRequired: true }],
      outputPorts: [{ id: goldenJudgeOutputId, name: 'Width', direction: 1, dataType: 2, isRequired: false }],
      parameters: [parameter(53_001, 'Tolerance', 'double', 12.5, { minValue: 0, maxValue: 100 })],
      isEnabled: true,
      executionStatus: 0,
      executionTimeMs: null,
      errorMessage: null
    }],
    connections: [{
      id: fixtureUuid(54_001),
      sourceOperatorId: goldenSourceNodeId,
      sourcePortId: sourceOutputId,
      targetOperatorId: goldenRoiNodeId,
      targetPortId: roiInputId
    }, {
      id: fixtureUuid(54_002),
      sourceOperatorId: goldenRoiNodeId,
      sourcePortId: roiOutputId,
      targetOperatorId: goldenJudgeNodeId,
      targetPortId: judgeInputId
    }],
    decisionConfiguration: null
  };
}

function goldenResultSummary() {
  return {
    id: goldenResultId,
    resultId: goldenResultId,
    projectId: projectA,
    status: 'Completed',
    executionOutcome: 'Succeeded',
    decisionOutcome: 'Ok',
    decisionSource: 'FinalDecision',
    reasonCode: 'G3_OK',
    hasJudgmentSignal: true,
    defectCount: 0,
    processingTimeMs: 18,
    inspectionTime: '2026-07-22T01:02:03Z',
    startedAt: '2026-07-22T01:02:02Z',
    completedAt: '2026-07-22T01:02:03Z',
    confidenceScore: 0.98,
    flowVersionHash: 'fixture-persisted-flow-hash',
    calibrationBundleId: null,
    runId: goldenResultId,
    diagnosticCode: 'G3_OK',
    diagnosticMessage: '黄金旅程正式运行完成。',
    errorMessage: null
  };
}

function goldenResultDetail() {
  return {
    ...goldenResultSummary(),
    defects: [],
    traceability: {
      flowVersionHash: 'fixture-persisted-flow-hash',
      calibrationBundleId: null,
      sessionId: null,
      runId: goldenResultId,
      packageId: 'cvpkg-g3-golden',
      stationId: null,
      projectPersistenceRevision: 8,
      decisionConfigurationHash: 'fixture-decision-hash'
    },
    hasEvidenceManifest: true,
    evidenceStatus: 'available',
    evidenceManifestReference: 'manifest-g3',
    evidenceTotalBytes: previewImage.length,
    retentionExpiresAtUtc: null,
    evidenceMessage: '本次结果证据完整，可导出。'
  };
}

function goldenEvidenceManifest() {
  return {
    status: 'available',
    message: '本次结果证据完整，可导出。',
    manifest: {
      schemaVersion: 1,
      manifestId: 'manifest-g3',
      projectId: projectA,
      inspectionResultId: goldenResultId,
      status: 'available',
      outcome: 'OK',
      createdAtUtc: '2026-07-22T01:02:03Z',
      flowVersionHash: 'fixture-persisted-flow-hash',
      calibrationBundleId: null,
      sessionId: null,
      runId: goldenResultId,
      retentionClass: 'standard',
      retentionExpiresAtUtc: null,
      totalBytes: previewImage.length,
      checksum: previewImageSha256,
      redaction: { applied: true },
      items: [{
        id: 'output-image',
        role: 'output-image',
        contentType: 'image/svg+xml',
        relativePath: 'output.svg',
        sizeBytes: previewImage.length,
        sha256: previewImageSha256,
        available: true,
        missingReason: null
      }]
    }
  };
}

function formalRunFixtureResult(
  request: Readonly<Record<string, unknown>>,
  seed: number,
  executionOutcome: string,
  decisionOutcome: string
) {
  return {
    id: fixtureUuid(seed),
    projectId: request.projectId,
    status: executionOutcome === 'Succeeded' ? 'Completed' : 'NotInspected',
    executionOutcome,
    decisionOutcome,
    executionSnapshotId: request.clientSnapshotId,
    projectPersistenceRevision: request.expectedPersistenceRevision,
    flowVersionHash: request.expectedCanonicalFlowHash,
    decisionConfigurationHash: request.expectedDecisionConfigurationHash,
    errorMessage: null
  };
}

function reconciliationFixture(
  request: Readonly<Record<string, unknown>>,
  status: string,
  result: Readonly<Record<string, unknown>> | null = null
) {
  return {
    status,
    code: status === 'cancelled' ? 'RUN_CANCELLED' : status === 'still-running' ? 'RUN_STILL_RUNNING' : null,
    message: `fixture ${status}`,
    projectId: request.projectId,
    clientSnapshotId: request.clientSnapshotId,
    projectPersistenceRevision: request.expectedPersistenceRevision,
    canonicalFlowHash: request.expectedCanonicalFlowHash,
    decisionConfigurationHash: request.expectedDecisionConfigurationHash,
    result
  };
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

const prompt3ParameterPresentation: Readonly<Record<string, Readonly<{
  displayName: string;
  description: string;
}>>> = Object.freeze({
  Text: {
    displayName: '工艺切换备注与异常处置说明',
    description: '记录当前批次的工艺切换原因、现场异常和后续处理要求；内容较长时应保持可读。'
  },
  Count: {
    displayName: '连续检测允许缺陷数量上限',
    description: '超过该数量后停止继续处理，并提示操作员检查上料、光照和瓶盖位置。'
  },
  Enabled: {
    displayName: '输出二值图供后续区域分析',
    description: '关闭后，下游区域分析节点不会收到二值图。'
  },
  Mode: {
    displayName: '阈值计算模式',
    description: '选择自动计算或使用当前工程中的手动阈值。'
  },
  Gain: {
    displayName: '光照补偿增益',
    description: '用于补偿现场光照波动，调整后应重新执行节点预览。'
  },
  OptionalCount: {
    displayName: '相机触发等待帧数（未配置时使用设备默认值）',
    description: '留空时使用相机设备配置中的默认等待帧数。'
  },
  FilePath: {
    displayName: '标定文件路径',
    description: '旧参数，仅用于兼容已有工程；新工程应使用正式标定资产。'
  }
});
const prompt3RoiParameterPresentation: Readonly<Record<string, Readonly<{
  displayName: string;
  description: string;
}>>> = Object.freeze({
  Shape: { displayName: 'ROI 形状', description: '选择要编辑的感兴趣区域形状。' },
  X: { displayName: 'X 坐标', description: '感兴趣区域左上角的图像 X 坐标。' },
  Y: { displayName: 'Y 坐标', description: '感兴趣区域左上角的图像 Y 坐标。' },
  Width: { displayName: '宽度', description: '感兴趣区域宽度。' },
  Height: { displayName: '高度', description: '感兴趣区域高度。' }
});

function prompt3OperatorCatalog() {
  const catalog = structuredClone(operatorCatalog) as Array<Record<string, unknown>>;
  const threshold = catalog.find(item => String(item.type) === '20');
  if (!threshold) throw new Error('Prompt 3 fixture requires operator type 20.');
  const parameterCopy = (threshold.parameters as Array<Record<string, unknown>>).map(parameter => {
    const name = String(parameter.name);
    const presentation = prompt3ParameterPresentation[name];
    return presentation ? { ...parameter, ...presentation } : parameter;
  });
  threshold.displayName = '瓶盖外观与密封完整性综合检测';
  threshold.description = '对瓶盖位置、边缘、密封区域和表面缺陷进行综合检测。';
  threshold.parameters = parameterCopy;
  threshold.parameterConstraints = [
    ...(threshold.parameterConstraints as Array<Record<string, unknown>>),
    {
      parameter: 'FilePath', requiredPolicy: 'optional', requiredWhen: null,
      enabledWhen: null, disabledWhen: null, visibleWhen: null, hiddenWhen: null,
      ignoredWhen: null, atLeastOneGroup: null, mutuallyExclusiveGroup: null,
      aliasFor: null, deprecated: true, resourceKind: null,
      reasonCode: 'LEGACY_CALIBRATION_PATH', satisfiedByInputPorts: []
    }
  ];
  const roiOperator = catalog.find(item => String(item.type) === 'RoiManager');
  if (!roiOperator) throw new Error('Prompt 3 fixture requires RoiManager.');
  roiOperator.displayName = 'ROI 矩形编辑';
  roiOperator.description = '在预览图像上编辑矩形感兴趣区域。';
  roiOperator.parameters = (roiOperator.parameters as Array<Record<string, unknown>>).map(parameter => {
    const presentation = prompt3RoiParameterPresentation[String(parameter.name)];
    return presentation
      ? {
          ...parameter,
          ...presentation,
          ...(String(parameter.name) === 'Shape'
            ? { options: [{ label: '矩形', value: 'Rectangle' }] }
            : {})
        }
      : parameter;
  });
  return catalog;
}

function prompt3PreviewOperatorCatalog() {
  return prompt3OperatorCatalog();
}

function prompt3InspectorFlow() {
  const flow = structuredClone(inspectorFlow()) as Record<string, unknown>;
  flow.name = '瓶盖外观与密封完整性综合检测主流程（华东二号产线夜班工艺）';
  const operators = flow.operators as Array<Record<string, unknown>>;
  operators[0]!.name = '上料工位瓶盖外观与密封完整性综合检测节点（主检测流程）';
  operators[1]!.name = '不合格瓶盖区域提取与下游剔除信号准备节点';
  for (const operator of operators) {
    operator.parameters = (operator.parameters as Array<Record<string, unknown>>).map(parameter => {
      const presentation = prompt3ParameterPresentation[String(parameter.name)];
      return presentation ? { ...parameter, ...presentation } : parameter;
    });
  }
  return flow;
}

function roiPreviewFlow() {
  const nodeId = fixtureUuid(50_001);
  const parameter = (seed: number, name: string, value: number) => ({
    id: fixtureUuid(seed),
    name,
    displayName: name,
    description: `${name} image coordinate`,
    dataType: 'double',
    value,
    defaultValue: value,
    minValue: 0,
    maxValue: 100,
    isRequired: true,
    options: null
  });
  return {
    id: flowId,
    name: 'G4 ROI Preview flow',
    operators: [{
      id: nodeId,
      name: 'ROI Rectangle',
      type: 'RoiManager',
      metadata: null,
      x: 80,
      y: 100,
      inputPorts: [],
      outputPorts: [{
        id: fixtureUuid(50_101),
        name: 'Roi',
        direction: 1,
        dataType: 13,
        isRequired: false
      }],
      parameters: [{
        id: fixtureUuid(50_200),
        name: 'Shape',
        displayName: 'Shape',
        description: 'ROI shape',
        dataType: 'enum',
        value: 'Rectangle',
        defaultValue: 'Rectangle',
        minValue: null,
        maxValue: null,
        isRequired: true,
        options: [{ label: 'Rectangle', value: 'Rectangle' }]
      },
        parameter(50_201, 'X', 10),
        parameter(50_202, 'Y', 10),
        parameter(50_203, 'Width', 30),
        parameter(50_204, 'Height', 20)
      ],
      isEnabled: true,
      executionStatus: 0,
      executionTimeMs: null,
      errorMessage: null
    }],
    connections: [],
    decisionConfiguration: null
  };
}

function prompt3PreviewFlow() {
  const flow = structuredClone(roiPreviewFlow()) as Record<string, unknown>;
  flow.name = '瓶盖密封区域 ROI 与结构化结果联合调试流程';
  const operator = (flow.operators as Array<Record<string, unknown>>)[0]!;
  operator.name = '瓶盖密封区域矩形 ROI 编辑节点';
  operator.parameters = (operator.parameters as Array<Record<string, unknown>>).map(parameter => {
    const presentation = prompt3RoiParameterPresentation[String(parameter.name)];
    return presentation
      ? {
          ...parameter,
          ...presentation,
          ...(String(parameter.name) === 'Shape'
            ? { options: [{ label: '矩形', value: 'Rectangle' }] }
            : {})
        }
      : parameter;
  });
  return flow;
}

async function selectInspectorNode(page: Page, x: number, y: number) {
  const canvas = page.locator('[data-testid="flow-canvas"]');
  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  await page.mouse.click(box!.x + x, box!.y + y);
  return { canvas, box: box! };
}

function withoutExpectedPreviewTransportConsoleErrors(
  audit: Readonly<{ consoleErrors: string[]; pageErrors: string[] }>
) {
  return {
    consoleErrors: audit.consoleErrors.filter(message =>
      !/Failed to load resource: (?:the server responded with a status of 400|net::ERR_FAILED)/.test(message)),
    pageErrors: [...audit.pageErrors]
  };
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
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
});

test('flag on mounts one owner only after full decode and disposes on route leave/project switch', async ({ page }) => {
  const audit = await bootWorkspace(page);
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(shell).toHaveAttribute('data-workspace-state', 'empty');
  await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 1,
    activeProjectId: projectA,
    flowCanvasOwnerCount: 1,
    inFlightReads: 0
  });
  expect(audit.filter(entry => entry.path === `/api/projects/${projectA}`)).toHaveLength(1);

  await page.goto(`/studio/index.html#/projects/${projectB}/workspace`);
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectB);
  await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 1,
    activeProjectId: projectB,
    lastDisposedProjectId: projectA,
    persistenceOwnerCount: 1,
    inFlightReads: 0,
    inFlightWrites: 0,
    ownerConflictCount: 0
  });

  await page.goto('/studio/index.html#/about');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    activeSubscriptions: 0,
    activeAbortControllers: 0,
    inFlightReads: 0
  });
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
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

  await ensureOperatorFlyout(page);
  await page.locator('[data-testid="operator-search"]').fill('');
  await page.locator('[data-testid="operator-category"]').selectOption('SegmentationAndRegion');
  await expect(page.locator('.operator-item')).toHaveCount(3);
  await dragOperator(page, '二值图转区域', 120, 120);
  await expect(canvas).toHaveAttribute('data-node-count', '2');
  await expect(canvas).toHaveAttribute('data-flow-revision', '2');
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
});

test('node selection, move, copy/paste, undo/redo, delete and focus/IME gates stay scoped', async ({ page }) => {
  await bootWorkspace(page);
  const topStateStack = page.locator('[data-testid="workspace-top-state-stack"]');
  await expect(topStateStack).toHaveCount(1);
  expect(await topStateStack.evaluate((element) => element.getBoundingClientRect().height)).toBe(0);
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
  await page.locator('[data-testid="flow-canvas"]').focus();
  await page.keyboard.press('Control+z');
  await expect(surface).toHaveAttribute('data-node-count', '1');
  await page.keyboard.press('Control+y');
  await expect(surface).toHaveAttribute('data-node-count', '2');

  await ensureOperatorFlyout(page);
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

test('pointer wiring creates and disconnects connections with stable feedback', async ({ page }) => {
  await bootWorkspace(page);
  const surface = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  const canvas = page.locator('[data-testid="flow-canvas"]');
  await dragOperator(page, '图像采集', 80, 100);
  await dragOperator(page, '全局阈值处理', 360, 100);
  await dragOperator(page, '区域腐蚀', 360, 180);
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

  await page.mouse.click(thresholdInput.x, thresholdInput.y);
  await expect(surface).toHaveAttribute('data-connection-count', '0');
});

test('G3 Inspector follows empty, node, multi-node and connection selection from Canvas', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() })
  });
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'empty');

  const { box } = await selectInspectorNode(page, 120, 125);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'node');
  await expect(inspector).toHaveAttribute('data-metadata-phase', 'ready');
  await expect(inspector).toContainText('Inspector Source');
  await expect(inspector.locator('[data-parameter-name]')).toHaveCount(7);
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-node-selected-success', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Selected node with authoritative executionStatus=Success in the Inspector.']
    });
  }

  await page.keyboard.down('Control');
  await page.mouse.click(box.x + 400, box.y + 125);
  await page.keyboard.up('Control');
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'multi-node');
  await expect(inspector.locator('.inspector-panel__summary-node')).toHaveCount(2);
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-multi-node-selected', viewport, runtimeErrors, requestAudit: audit
    });
  }

  await page.mouse.click(box.x + 250, box.y + 250);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'empty');

  await page.mouse.click(box.x + 290, box.y + 142);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'connection');
  await expect(inspector).toContainText('Inspector Source');
  await expect(inspector).toContainText('Inspector Target');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-connection-selected', viewport, runtimeErrors, requestAudit: audit
    });
  }
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
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
  await page.evaluate(projectId => {
    window.location.hash = `#/projects/${projectId}/workspace`;
  }, projectB);
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(shell).toHaveAttribute('data-workspace-state', 'forbidden');
  await expect(shell).toHaveAttribute('data-workspace-inspector-owner-count', '0');
  await expect(inspector).toHaveCount(0);
});

test('G4 Preview and ImageCanvas render artifacts, probe pixels and commit ROI once with undo redo', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: roiPreviewFlow() }),
    previewScenario: (request, call) => ({
      body: previewPayload(request, call, {
        outputData: { score: 0.98, call },
        artifacts: [previewArtifactReference(call)]
      })
    })
  });
  await selectInspectorNode(page, 120, 125);

  const preview = page.locator('[data-capability="preview-workbench"]');
  const image = page.locator('[data-capability="image-canvas"]');
  const canvas = page.locator('[data-testid="image-canvas"]');
  const roi = page.locator('.preview-panel__roi');
  const flow = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  const xInput = page.locator('[data-evidence-surface="f03-g3-inspector"] [data-parameter-name="X"] input');

  await expect(preview).toHaveAttribute('data-preview-phase', 'success');
  await expect(preview).toHaveAttribute('data-preview-stale', 'false');
  await expect(image).toHaveAttribute('data-image-phase', 'ready');
  await expect(image).not.toHaveAttribute('data-image-identity', '');
  await expect(preview.locator('.preview-panel__result pre')).toContainText('0.98');
  await expect.poll(async () => Number(await image.getAttribute('data-image-dpr'))).toBeGreaterThan(0);
  expect(await workspaceDiagnostics(page)).toMatchObject({
    previewOwnerCount: 1,
    imageCanvasOwnerCount: 1,
    roiOwnerCount: 1,
    ownerConflictCount: 0
  });

  await page.locator('[data-testid="image-actual-size"]').click();
  await expect.poll(async () => Number(await image.getAttribute('data-image-scale'))).toBe(1);
  await page.locator('[data-testid="image-zoom-in"]').click();
  await expect.poll(async () => Number(await image.getAttribute('data-image-scale'))).toBeGreaterThan(1);
  await page.locator('[data-testid="image-fit"]').click();

  const imageBox = await canvas.boundingBox();
  expect(imageBox).not.toBeNull();
  await page.mouse.click(imageBox!.x + imageBox!.width / 2, imageBox!.y + imageBox!.height / 2);
  await expect(image.locator('.image-viewport__probe')).toHaveAttribute('data-probe-phase', 'locked');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-image-probe-locked', viewport, runtimeErrors, requestAudit: audit
    });
  }

  await expect(roi).toHaveAttribute('data-roi-phase', 'ready');
  await page.locator('[data-testid="roi-start"]').click();
  await expect(roi).toHaveAttribute('data-roi-phase', 'editing');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-roi-editing', viewport, runtimeErrors, requestAudit: audit
    });
  }
  await expect(flow).toHaveAttribute('data-flow-revision', '0');
  await canvas.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.locator('[data-testid="roi-confirm"]')).toBeEnabled();
  await expect(flow).toHaveAttribute('data-flow-revision', '0');
  await expect(xInput).toHaveValue('10');
  await page.locator('[data-testid="roi-confirm"]').click();
  await expect(flow).toHaveAttribute('data-flow-revision', '1');
  await expect(xInput).toHaveValue('11');

  await page.locator('[data-testid="flow-canvas"]').focus();
  await page.keyboard.press('Control+z');
  await expect(xInput).toHaveValue('10');
  await page.keyboard.press('Control+y');
  await expect(xInput).toHaveValue('11');

  await expect(preview).toHaveAttribute('data-preview-phase', 'success');
  await expect(roi).toHaveAttribute('data-roi-phase', 'ready');
  await page.locator('[data-testid="roi-start"]').click();
  await canvas.focus();
  await page.keyboard.press('ArrowRight');
  await page.locator('[data-testid="roi-cancel"]').click();
  await expect(xInput).toHaveValue('11');

  await requestStudioHashNavigation(page, '#/about');
  await resolveLeavePrompt(page, 'discard');
  await expect(page).toHaveURL(/#\/about$/);
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    previewOwnerCount: 0,
    imageCanvasOwnerCount: 0,
    roiOwnerCount: 0,
    activeSubscriptions: 0,
    activeTimers: 0,
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: 0,
    activeBlobUrls: 0,
    activePreviewArtifactIds: 0,
    inFlightPreview: 0,
    ownerConflictCount: 0
  });
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
  expect(audit.some(entry => entry.method === 'POST' && entry.path === '/api/flows/preview-node')).toBe(true);
  expect(audit.some(entry => entry.method === 'GET' && entry.path.startsWith('/api/preview-artifacts/'))).toBe(true);
  expect(audit.some(entry => entry.method === 'DELETE' && entry.path.startsWith('/api/preview-artifacts/'))).toBe(true);
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
});

test('G5 GET PUT GET saves one canonical payload and preserves null, falsy and opaque values', async ({ page }) => {
  let captured: Readonly<Record<string, unknown>> | null = null;
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    projectPutScenario: request => {
      captured = request;
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await selectInspectorNode(page, 120, 125);
  const textInput = page.locator('[data-evidence-surface="f03-g3-inspector"] [data-parameter-name="Text"] input');
  await textInput.fill('saved-value');
  await textInput.press('Enter');
  await expect(shell).toHaveAttribute('data-workspace-dirty', 'true');
  await expect(page.locator('[data-testid="workspace-save"]')).toBeEnabled();
  await page.locator('[data-testid="workspace-save"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'saved');
  await expect(shell).toHaveAttribute('data-workspace-dirty', 'false');
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '8');

  expect(captured).not.toBeNull();
  expect(captured).toMatchObject({
    expectedPersistenceRevision: 7,
    globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] }
  });
  const capturedFlow = captured!.flow as Readonly<Record<string, unknown>>;
  expect(capturedFlow).toMatchObject({ futureFlowField: { schema: 3 } });
  const capturedOperator = (capturedFlow.operators as Array<Record<string, unknown>>)[0]!;
  expect(capturedOperator).not.toHaveProperty('executionStatus');
  expect(capturedOperator).not.toHaveProperty('executionTimeMs');
  expect(capturedOperator).not.toHaveProperty('errorMessage');
  const values = Object.fromEntries(
    (capturedOperator.parameters as Array<Record<string, unknown>>).map(parameter => [parameter.name, parameter.value])
  );
  expect(values).toMatchObject({
    Text: 'saved-value', Count: 0, Enabled: false, Gain: 0, OptionalCount: null, FilePath: ''
  });

  const putsAfterSave = audit.filter(entry => entry.method === 'PUT').length;
  await page.keyboard.press('Control+s');
  await page.waitForTimeout(50);
  expect(audit.filter(entry => entry.method === 'PUT')).toHaveLength(putsAfterSave);

  await page.reload();
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '8');
  await selectInspectorNode(page, 120, 125);
  await expect(textInput).toHaveValue('saved-value');
  await textInput.fill('edited-again');
  await textInput.press('Enter');
  await expect(shell).toHaveAttribute('data-workspace-dirty', 'true');
  expect(isF03G5RequestAllowlist(audit)).toBe(true);
  expect(audit.some(entry => /\/api\/(?:inspection\/execute|inspection\/admission|runs)/i.test(entry.path))).toBe(false);
});

test('G6 runs only the saved Project identity, stays in Workspace, and hands off the current result explicitly', async ({ page }) => {
  const requests: Array<{ stage: string; request: Readonly<Record<string, unknown>> }> = [];
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: (stage, request) => {
      requests.push({ stage, request });
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await expect(page.locator('[data-testid="workspace-run"]')).toBeEnabled();
  await page.locator('[data-testid="workspace-run"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'succeeded');
  await expect(page.locator('[data-testid="workspace-current-result"]')).toHaveText('查看本次结果');
  expect(requests.map(item => item.stage)).toEqual(['admission', 'execute']);
  expect(requests[0]!.request).toMatchObject({ projectId: projectA, expectedPersistenceRevision: 7 });
  expect(requests[0]!.request).not.toHaveProperty('flowData');
  expect(requests[1]!.request).toMatchObject({
    projectId: projectA,
    expectedPersistenceRevision: 7,
    expectedCanonicalFlowHash: 'fixture-persisted-flow-hash',
    expectedDecisionConfigurationHash: 'fixture-decision-hash'
  });
  expect(requests[1]!.request.clientSnapshotId).toBe(requests[0]!.request.clientSnapshotId);
  expect(audit.filter(item => item.method === 'POST' && item.path.startsWith('/api/inspection/') &&
    item.path !== '/api/inspection/decision-configuration/validate')
    .map(item => item.path)).toEqual([
    '/api/inspection/admission',
    '/api/inspection/execute'
  ]);
  expect(isF03G6RequestAllowlist(audit), JSON.stringify(audit)).toBe(true);
  await page.locator('[data-testid="workspace-current-result"]').click();
  await expect(page.locator('[data-capability="results-read"]')).toBeVisible();
});

for (const viewport of [{ width: 1920, height: 1080 }, { width: 1366, height: 768 }] as const) {
  test(`F04-R G3 golden journey closes Camera, Variables, Decision, Preview, Save, Run, Evidence and Package at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    test.setTimeout(60_000);
    await page.setViewportSize(viewport);
    const runtimeErrors = createF04RuntimeErrorAudit(page);
    let savedPayload: Readonly<Record<string, unknown>> | null = null;
    const audit = await bootWorkspace(page, {
      authRole: 'Admin',
      projectBody: projectId => projectPayload(projectId, { flow: goldenJourneyFlow() }),
      projectPutScenario: (request, current) => {
        savedPayload = request;
        return {
          body: {
            ...current,
            name: request.name,
            description: request.description,
            flow: request.flow,
            globalVariables: request.globalVariables,
            persistenceRevision: Number(current.persistenceRevision) + 1,
            modifiedAt: '2026-07-22T01:02:01Z'
          }
        };
      },
      previewScenario: (request, call) => ({
        body: previewPayload(request, call, {
          outputData: { width: 12.8, tolerance: 12.5, outcome: 'OK' },
          artifacts: [previewArtifactReference(call)]
        })
      })
    });
    const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
    await expect(page.locator('[data-testid="global-variables"]')).toBeVisible();
    await expect(page.locator('[data-testid="runtime-package-export"]')).toBeVisible();
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario: `g4a-workspace-default-${viewport.width}`,
        viewport,
        runtimeErrors,
        requestAudit: audit
      });
    }

    await selectInspectorNode(page, 120, 125);
    const camera = page.locator('[data-capability="camera-binding-editor"]');
    await expect(camera).toBeVisible();
    await expect(camera.getByLabel('相机绑定')).toHaveValue('camera-a');
    await camera.getByRole('button', { name: '捕获单帧' }).click();
    await expect(camera).toHaveAttribute('data-capture-phase', 'captured');
    await expect(camera).not.toHaveAttribute('data-frame-id', '');
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario: `g4a-camera-binding-${viewport.width}`,
        viewport,
        runtimeErrors,
        requestAudit: audit,
        notes: ['BROWSER_FIXTURE camera frame; REAL_CAMERA=NOT_PERFORMED']
      });
    }

    await page.locator('[data-testid="global-variables"]').click();
    const variables = page.locator('[data-capability="global-variables-workbench"]');
    await variables.getByLabel('名称', { exact: true }).fill('SealWidth');
    await variables.getByLabel('显示名称', { exact: true }).fill('密封宽度');
    await variables.locator('.variables-workbench__form select').selectOption('Double');
    await variables.getByLabel('默认 / 手动初始值').fill('12.5');
    await variables.getByRole('button', { name: '添加定义' }).click();
    await variables.getByRole('button', { name: '绑定', exact: true }).click();
    const sourceBinding = variables.locator('.variables-workbench__bindings > div').nth(0);
    await sourceBinding.locator('select').nth(0).selectOption({ label: '密封宽度' });
    await sourceBinding.locator('select').nth(1).selectOption({ label: '密封宽度判定 / Width' });
    await sourceBinding.getByRole('button', { name: '添加来源' }).click();
    const targetBinding = variables.locator('.variables-workbench__bindings > div').nth(1);
    await targetBinding.locator('select').nth(0).selectOption({ label: '密封宽度' });
    await targetBinding.locator('select').nth(1).selectOption({ label: '密封宽度判定 / Tolerance' });
    await targetBinding.getByRole('button', { name: '添加绑定' }).click();
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario: `g4a-global-variables-${viewport.width}`,
        viewport,
        runtimeErrors,
        requestAudit: audit
      });
    }
    await page.getByRole('button', { name: '应用到工程草稿' }).click();

    await page.locator('[data-testid="final-decision"]').click();
    const decision = page.locator('[data-capability="final-decision-workbench"]');
    await decision.locator('select').first().selectOption(`${goldenJudgeNodeId}:${goldenJudgeOutputId}`);
    await expect(decision.locator('input[type="number"]')).toBeVisible();
    await decision.locator('input[type="number"]').fill('12.5');
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario: `g4a-final-decision-${viewport.width}`,
        viewport,
        runtimeErrors,
        requestAudit: audit
      });
    }
    await page.getByRole('button', { name: '校验并应用' }).click();
    await expect(decision).toHaveCount(0);

    await selectInspectorNode(page, 340, 125);
    const preview = page.locator('[data-capability="preview-workbench"]');
    const roi = page.locator('.preview-panel__roi');
    await expect(preview).toHaveAttribute('data-preview-phase', 'success');
    await expect(roi).toHaveAttribute('data-roi-phase', 'ready');
    await page.locator('[data-testid="roi-start"]').click();
    await page.locator('[data-testid="image-canvas"]').focus();
    await page.keyboard.press('ArrowRight');
    await page.locator('[data-testid="roi-confirm"]').click();
    await expect(preview).toHaveAttribute('data-preview-phase', 'success');

    await expect(page.locator('[data-testid="workspace-save"]')).toBeEnabled();
    await page.locator('[data-testid="workspace-save"]').click();
    await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'saved');
    expect(savedPayload).toMatchObject({
      expectedPersistenceRevision: 7,
      globalVariables: {
        schemaVersion: '1.0',
        variables: [{ name: 'SealWidth', displayName: '密封宽度', valueType: 'Double', initialValue: 12.5 }]
      },
      flow: { decisionConfiguration: { finalDecisionBinding: { threshold: 12.5 } } }
    });

    await page.locator('[data-testid="workspace-run"]').click();
    await expect(shell).toHaveAttribute('data-workspace-run-phase', 'succeeded');
    await expect(page.locator('[data-testid="workspace-current-result"]')).toHaveText('查看本次结果');
    await expect(preview).toHaveAttribute('data-preview-phase', 'success');
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario: `g4a-run-completed-${viewport.width}`,
        viewport,
        runtimeErrors,
        requestAudit: audit,
        notes: ['BROWSER_FIXTURE camera frame; REAL_CAMERA=NOT_PERFORMED']
      });
    }

    await page.locator('[data-testid="workspace-current-result"]').click();
    const evidence = page.locator('[data-capability="result-evidence"]');
    await expect(evidence).toHaveAttribute('data-evidence-phase', 'available');
    await expect(evidence).toContainText('manifest-g3');
    const download = page.waitForEvent('download');
    await page.locator('[data-testid="result-evidence-export"]').click();
    await download;
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario: `g4a-result-evidence-${viewport.width}`, viewport, runtimeErrors, requestAudit: audit
      });
    }

    await page.locator('[data-testid="results-return-workspace"]').click();
    await expect(shell).toBeVisible();
    await page.locator('[data-testid="runtime-package-export"]').click();
    const packageDialog = page.locator('[data-capability="runtime-package-export"]');
    await page.getByRole('button', { name: '导出运行包', exact: true }).click();
    await expect(packageDialog).toHaveAttribute('data-phase', 'success');
    await expect(packageDialog).toContainText('cvpkg-g3-golden');
    await expect(packageDialog.getByTestId('runtime-package-open-stations')).toHaveAttribute(
      'href',
      /#\/stations\?packageId=station-pkg-g3&projectId=.*&revision=8/
    );
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario: `g4a-admin-runtime-package-${viewport.width}`, viewport, runtimeErrors, requestAudit: audit
      });
    }

    expect(audit.some(entry => entry.method === 'POST' && entry.path === '/api/cameras/soft-trigger-capture')).toBe(true);
    expect(audit.some(entry => entry.method === 'PUT' && entry.path === `/api/projects/${projectA}`)).toBe(true);
    expect(audit.some(entry => entry.path === '/api/inspection/admission')).toBe(true);
    expect(audit.some(entry => entry.path === '/api/inspection/execute')).toBe(true);
    expect(audit.some(entry => entry.path.endsWith('/evidence/manifest'))).toBe(true);
    expect(audit.some(entry => entry.path.endsWith('/evidence/export'))).toBe(true);
    const packageRequest = audit.find(entry => entry.path.endsWith('/runtime-package/export'));
    expect(packageRequest?.method).toBe('POST');
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}

test('G4 visual evidence holds Formal Run executing until the authoritative result settles', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const execute = deferred<Readonly<Record<string, unknown>>>();
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: stage => stage === 'execute' ? execute.promise : {}
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await page.locator('[data-testid="workspace-run"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'executing');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'formal-run', viewport, runtimeErrors, requestAudit: audit
    });
  }
  execute.resolve({});
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'succeeded');
  await expect(page.locator('[data-testid="workspace-current-result"]')).toBeVisible();
});

test('G6 blocks execute after admission rejection and keeps the saved Workspace editable', async ({ page }) => {
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: (stage, request) => stage === 'admission'
      ? {
          body: {
            allowed: false,
            code: 'ADMISSION_FINAL_DECISION_INVALID',
            message: 'fixture decision invalid',
            projectId: request.projectId,
            clientSnapshotId: request.clientSnapshotId,
            projectPersistenceRevision: null,
            canonicalFlowHash: null,
            decisionConfigurationHash: null,
            violations: [{ code: 'FINAL_DECISION_INVALID' }]
          }
        }
      : {}
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await page.locator('[data-testid="workspace-run"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'blocked');
  await expect(page.locator('[data-testid="workspace-run"]')).toBeEnabled();
  expect(audit.filter(item => item.path === '/api/inspection/execute')).toHaveLength(0);
  expect(isF03G6RequestAllowlist(audit)).toBe(true);
});

test('G6 genuine running Stop cancels before execute completion and unlocks without Results navigation', async ({ page }) => {
  const executeEntered = deferred();
  const cancellationRequested = deferred();
  let executeCompleted = false;
  let stopObservedBeforeCompletion = false;
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: async (stage, request) => {
      if (stage === 'execute') {
        executeEntered.resolve();
        await cancellationRequested.promise;
        executeCompleted = true;
        return { abort: true };
      }
      if (stage === 'stop') {
        stopObservedBeforeCompletion = !executeCompleted;
        cancellationRequested.resolve();
        return {
          body: {
            status: 'cancelled',
            code: 'RUN_CANCELLED',
            message: 'fixture coordinator token cancelled and result persisted',
            projectId: request.projectId,
            clientSnapshotId: request.clientSnapshotId,
            projectPersistenceRevision: request.expectedPersistenceRevision,
            canonicalFlowHash: request.expectedCanonicalFlowHash,
            decisionConfigurationHash: request.expectedDecisionConfigurationHash,
            result: {
              id: fixtureUuid(90_002),
              projectId: request.projectId,
              status: 'NotInspected',
              executionOutcome: 'Cancelled',
              decisionOutcome: 'NotApplicable',
              executionSnapshotId: request.clientSnapshotId,
              projectPersistenceRevision: request.expectedPersistenceRevision,
              flowVersionHash: request.expectedCanonicalFlowHash,
              decisionConfigurationHash: request.expectedDecisionConfigurationHash,
              errorMessage: null
            }
          }
        };
      }
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await page.locator('[data-testid="workspace-run"]').click();
  await executeEntered.promise;
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'executing');
  await expect(page.locator('[data-testid="workspace-save"]')).toBeDisabled();
  await expect(page.locator('[data-testid="workspace-run-stop"]')).toBeVisible();
  await expect(page.locator('[data-capability="results-read"]')).toHaveCount(0);
  await page.locator('[data-testid="workspace-run-stop"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'cancelled');
  await expect(page.locator('[data-testid="workspace-run"]')).toBeEnabled();
  await expect(page.locator('[data-evidence-surface="f03-g2-flow-canvas"]')).toHaveAttribute('data-mutation-gate', 'editable');
  await expect(page.locator('[data-capability="results-read"]')).toHaveCount(0);
  expect(new URL(page.url()).hash).toContain(`/projects/${projectA}/workspace`);
  expect(stopObservedBeforeCompletion).toBe(true);
  expect(executeCompleted).toBe(true);
  expect(audit.filter(entry => entry.method === 'POST' && entry.path.startsWith('/api/inspection/') &&
    entry.path !== '/api/inspection/decision-configuration/validate')
    .map(entry => entry.path)).toEqual([
    '/api/inspection/admission',
    '/api/inspection/execute',
    '/api/inspection/stop'
  ]);
  expect(isF03G6RequestAllowlist(audit)).toBe(true);
});

test('G6 explicit reconcile recovers a successful result after execute response loss', async ({ page }) => {
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: (stage, request) => stage === 'execute'
      ? { abort: true }
      : stage === 'reconcile'
        ? {
            body: {
              status: 'succeeded',
              code: null,
              message: 'fixture recovered result',
              projectId: request.projectId,
              clientSnapshotId: request.clientSnapshotId,
              projectPersistenceRevision: request.expectedPersistenceRevision,
              canonicalFlowHash: request.expectedCanonicalFlowHash,
              decisionConfigurationHash: request.expectedDecisionConfigurationHash,
              result: {
                id: fixtureUuid(90_003),
                projectId: request.projectId,
                status: 'Completed',
                executionOutcome: 'Succeeded',
                decisionOutcome: 'Ok',
                executionSnapshotId: request.clientSnapshotId,
                projectPersistenceRevision: request.expectedPersistenceRevision,
                flowVersionHash: request.expectedCanonicalFlowHash,
                decisionConfigurationHash: request.expectedDecisionConfigurationHash,
                errorMessage: null
              }
            }
          }
        : {}
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await page.locator('[data-testid="workspace-run"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'unknown-outcome');
  await page.locator('[data-testid="workspace-run-reconcile"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'succeeded');
  await expect(page.locator('[data-testid="workspace-current-result"]')).toBeVisible();
  expect(audit.some(entry => entry.path === '/api/inspection/reconcile')).toBe(true);
  expect(isF03G6RequestAllowlist(audit)).toBe(true);
});

test('G6 reconcile still-running and identity mismatch remain fail-closed', async ({ page }) => {
  let mode: 'running' | 'mismatch' = 'running';
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: (stage, request) => stage === 'execute'
      ? { abort: true }
      : stage === 'reconcile'
        ? mode === 'running'
          ? {
              body: {
                status: 'still-running',
                code: 'RUN_STILL_RUNNING',
                message: 'fixture still running',
                projectId: request.projectId,
                clientSnapshotId: request.clientSnapshotId,
                projectPersistenceRevision: request.expectedPersistenceRevision,
                canonicalFlowHash: request.expectedCanonicalFlowHash,
                decisionConfigurationHash: request.expectedDecisionConfigurationHash,
                result: null
              }
            }
          : {
              body: {
                status: 'identity-mismatch',
                code: 'RUN_IDENTITY_MISMATCH',
                message: 'fixture identity mismatch',
                projectId: request.projectId,
                clientSnapshotId: fixtureUuid(90_004),
                projectPersistenceRevision: request.expectedPersistenceRevision,
                canonicalFlowHash: request.expectedCanonicalFlowHash,
                decisionConfigurationHash: request.expectedDecisionConfigurationHash,
                result: null
              }
            }
        : {}
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await page.locator('[data-testid="workspace-run"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'unknown-outcome');
  await page.locator('[data-testid="workspace-run-reconcile"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'executing');
  await expect(page.locator('[data-testid="workspace-save"]')).toBeDisabled();

  mode = 'mismatch';
  await page.locator('[data-testid="workspace-run-reconcile"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'unknown-outcome');
  await expect(page.locator('[data-testid="workspace-run"]')).toBeDisabled();
  expect(isF03G6RequestAllowlist(audit)).toBe(true);
});

test('G6 protects route leave while Formal Run is still executing', async ({ page }) => {
  let reconcileStatus: 'still-running' | 'cancelled' = 'still-running';
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: (stage, request) => {
      if (stage === 'execute') return { delayMs: 1000, abort: true };
      if (stage === 'stop') return { body: reconciliationFixture(request, 'cancel-requested') };
      if (stage === 'reconcile') {
        return { body: reconciliationFixture(request, reconcileStatus === 'cancelled' ? 'cancelled' : 'still-running') };
      }
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await page.locator('[data-testid="workspace-run"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'executing');

  await requestStudioHashNavigation(page, '#/about');
  await expect(page.locator('[data-product-state="leave-blocked"]')).toContainText('Formal Run');
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectA);
  expect(audit.some(entry => entry.path === '/api/inspection/stop')).toBe(true);
  expect(audit.some(entry => entry.path === '/api/inspection/reconcile')).toBe(true);

  reconcileStatus = 'cancelled';
  await page.locator('[data-testid="workspace-run-reconcile"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'cancelled');
  await requestStudioHashNavigation(page, '#/about');
  await expect(page).toHaveURL(/#\/about$/);
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    runOwnerCount: 0,
    activeAbortControllers: 0,
    inFlightExecute: 0
  });
  expect(isF03G6RequestAllowlist(audit)).toBe(true);
});

test('G6 protects project switch while Formal Run is still executing', async ({ page }) => {
  let reconcileStatus: 'still-running' | 'cancelled' = 'still-running';
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: (stage, request) => {
      if (stage === 'execute') return { delayMs: 1000, abort: true };
      if (stage === 'stop') return { body: reconciliationFixture(request, 'cancel-requested') };
      if (stage === 'reconcile') {
        return { body: reconciliationFixture(request, reconcileStatus === 'cancelled' ? 'cancelled' : 'still-running') };
      }
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await page.locator('[data-testid="workspace-run"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'executing');

  await requestStudioHashNavigation(page, `#/projects/${projectB}/workspace`);
  await expect(page.locator('[data-product-state="leave-blocked"]')).toContainText('Formal Run');
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectA);

  reconcileStatus = 'cancelled';
  await page.locator('[data-testid="workspace-run-reconcile"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'cancelled');
  await requestStudioHashNavigation(page, `#/projects/${projectB}/workspace`);
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectB);
  await requestStudioHashNavigation(page, '#/about');
  await expect(page).toHaveURL(/#\/about$/);
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    runOwnerCount: 0,
    activeAbortControllers: 0,
    inFlightExecute: 0
  });
  expect(isF03G6RequestAllowlist(audit)).toBe(true);
});

test('G6 Host close flush keeps the owner alive when Formal Run cannot be settled', async ({ page }) => {
  let reconcileStatus: 'still-running' | 'cancelled' = 'still-running';
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: (stage, request) => {
      if (stage === 'execute') return { delayMs: 1000, abort: true };
      if (stage === 'stop') return { body: reconciliationFixture(request, 'cancel-requested') };
      if (stage === 'reconcile') {
        return { body: reconciliationFixture(request, reconcileStatus === 'cancelled' ? 'cancelled' : 'still-running') };
      }
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  await page.locator('[data-testid="workspace-run"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'executing');

  const flushResult = await page.evaluate(async () => {
    const flush = (window as Window & {
      __clearVisionFlushProjectWorkspace?: (reason?: string) => Promise<boolean>;
    }).__clearVisionFlushProjectWorkspace;
    return await flush?.('host-close');
  });
  expect(flushResult).toBe(false);
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'cancel-requested');
  await expect(page.locator('[data-testid="workspace-save"]')).toBeDisabled();
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 1,
    runOwnerCount: 1,
    activeAbortControllers: 0,
    inFlightExecute: 0
  });

  reconcileStatus = 'cancelled';
  await page.locator('[data-testid="workspace-run-reconcile"]').click();
  await expect(shell).toHaveAttribute('data-workspace-run-phase', 'cancelled');
  await expect(page.evaluate(async () => {
    const flush = (window as Window & {
      __clearVisionFlushProjectWorkspace?: (reason?: string) => Promise<boolean>;
    }).__clearVisionFlushProjectWorkspace;
    return await flush?.('host-close-after-reconcile');
  })).resolves.toBe(true);
  await page.goto('/studio/index.html#/about');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    runOwnerCount: 0,
    activeAbortControllers: 0,
    inFlightExecute: 0
  });
  expect(isF03G6RequestAllowlist(audit)).toBe(true);
});

test('G5 saves and reloads a catalog-added numeric operator type through the formal Project PUT', async ({ page }) => {
  let captured: Readonly<Record<string, unknown>> | null = null;
  const audit = await bootWorkspace(page, {
    projectPutScenario: request => {
      captured = request;
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const canvas = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  const operator = await searchOperator(page, 'threshold');
  await expect(operator).toHaveCount(1);
  await operator.click();
  await expect(canvas).toHaveAttribute('data-node-count', '1');
  await expect(shell).toHaveAttribute('data-workspace-dirty', 'true');
  await page.locator('[data-testid="workspace-save"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'saved');
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '8');
  await expect(shell).toHaveAttribute('data-workspace-dirty', 'false');

  expect(captured).not.toBeNull();
  const savedOperator = ((captured!.flow as Readonly<Record<string, unknown>>)
    .operators as Array<Record<string, unknown>>)[0]!;
  expect(savedOperator.type).toBe(20);
  expect(savedOperator).not.toHaveProperty('executionStatus');
  expect(audit.filter(entry => entry.method === 'PUT')).toHaveLength(1);

  await page.reload();
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '8');
  await expect(canvas).toHaveAttribute('data-node-count', '1');
  expect(audit.some(entry => /\/api\/(?:inspection\/execute|inspection\/admission|runs)/i.test(entry.path))).toBe(false);
  expect(isF03G5RequestAllowlist(audit)).toBe(true);
});

test('G5 save failure retries explicitly, PSV011 reconciles fail closed, and unknown outcome reconciles by GET', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  let mode: 'failure' | 'conflict' | 'unknown' = 'failure';
  let putCall = 0;
  const bodies: Readonly<Record<string, unknown>>[] = [];
  const serverConflictFlow = structuredClone(inspectorFlow());
  ((serverConflictFlow.operators[0]!.parameters as Array<Record<string, unknown>>)
    .find(parameter => parameter.name === 'Text')!).value = 'server-value';
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    projectPutScenario: (request, current) => {
      bodies.push(request);
      putCall += 1;
      if (mode === 'failure' && putCall === 1) {
        return { status: 500, body: { code: 'PSV999', error: 'injected' } };
      }
      if (mode === 'conflict' && putCall === 3) {
        return {
          status: 409,
          body: { code: 'PSV011', error: 'stale' },
          authoritativeProject: projectPayload(projectA, {
            persistenceRevision: 9,
            flow: serverConflictFlow
          })
        };
      }
      if (mode === 'unknown' && putCall === 5) {
        return {
          abort: true,
          authoritativeProject: {
            ...current,
            name: request.name,
            description: request.description,
            flow: request.flow,
            persistenceRevision: Number(current.persistenceRevision) + 1
          }
        };
      }
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const textInput = page.locator('[data-evidence-surface="f03-g3-inspector"] [data-parameter-name="Text"] input');

  await selectInspectorNode(page, 120, 125);
  await textInput.fill('failure-local');
  await textInput.press('Enter');
  await page.locator('[data-testid="workspace-save"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'error');
  await expect(textInput).toHaveValue('failure-local');
  await page.locator('[data-testid="workspace-save-retry"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'saved');

  mode = 'conflict';
  await selectInspectorNode(page, 120, 125);
  await textInput.fill('conflict-local');
  await textInput.press('Enter');
  await page.locator('[data-testid="workspace-save"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'conflict');
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '8');
  await expect(textInput).toHaveValue('conflict-local');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-conflict',
      viewport,
      runtimeErrors: createF04RuntimeErrorAudit(page),
      requestAudit: audit
    });
  }
  await page.locator('[data-testid="workspace-conflict-reapply"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '9');
  await expect(shell).toHaveAttribute('data-workspace-dirty', 'true');
  await page.locator('[data-testid="workspace-save"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '10');
  expect(bodies[3]).toMatchObject({ expectedPersistenceRevision: 9 });

  mode = 'unknown';
  await selectInspectorNode(page, 120, 125);
  await textInput.fill('unknown-local');
  await textInput.press('Enter');
  await page.locator('[data-testid="workspace-save"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'unknown-outcome');
  await expect(page.locator('[data-testid="workspace-save-retry"]')).toHaveCount(0);
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-unknown-outcome',
      viewport,
      runtimeErrors: createF04RuntimeErrorAudit(page),
      requestAudit: audit
    });
  }
  await page.locator('[data-testid="workspace-save-reconcile"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'saved');
  await expect(shell).toHaveAttribute('data-workspace-dirty', 'false');
});

test('G5 settles a delayed reconcile before route leave and cannot overwrite the next Project', async ({ page }) => {
  const runtimeErrors = createF03RuntimeErrorAudit(page);
  let delayedReconcileStarted = false;
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    projectPutScenario: (request, current) => ({
      abort: true,
      authoritativeProject: {
        ...current,
        name: request.name,
        description: request.description,
        flow: request.flow,
        persistenceRevision: Number(current.persistenceRevision) + 1
      }
    }),
    projectGetScenario: (projectId, current, call) => {
      if (projectId === projectA && call === 2) {
        delayedReconcileStarted = true;
        return {
          delayMs: 700,
          body: { ...current, persistenceRevision: 9, flow: inspectorFlow() }
        };
      }
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const textInput = page.locator('[data-evidence-surface="f03-g3-inspector"] [data-parameter-name="Text"] input');
  await selectInspectorNode(page, 120, 125);
  await textInput.fill('route-leave-local');
  await textInput.press('Enter');
  await page.locator('[data-testid="workspace-save"]').click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'unknown-outcome');

  await page.locator('[data-testid="workspace-save-reconcile"]').click();
  await expect.poll(() => delayedReconcileStarted).toBe(true);
  await requestStudioHashNavigation(page, `#/projects/${projectB}/workspace`);
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectB);
  await expect(page.locator('[data-testid="leave-guard-discard"]')).toHaveCount(0);
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '8');
  await page.waitForTimeout(800);
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectB);
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '8');
  expect(runtimeErrors.pageErrors).toEqual([]);
  expect(runtimeErrors.consoleErrors.filter(message => !message.includes('net::ERR_FAILED'))).toEqual([]);

  await requestStudioHashNavigation(page, '#/about');
  await expect(page).toHaveURL(/#\/about$/);
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    persistenceOwnerCount: 0,
    runOwnerCount: 0,
    inFlightReads: 0,
    inFlightWrites: 0,
    activeAbortControllers: 0
  });
  expect(isF03G5RequestAllowlist(audit)).toBe(true);
});

test('G5 protects route leave and project switch while readonly and running responses disable saving', async ({ page }) => {
  let responseMode: 'success' | 'readonly' | 'running' = 'success';
  await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    projectPutScenario: () => responseMode === 'readonly'
      ? { status: 403, body: { code: 'AUTH403', error: 'forbidden' } }
      : responseMode === 'running'
        ? { status: 409, body: { code: 'GV031', error: 'running' } }
        : {}
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  const flowCanvas = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  const textInput = page.locator('[data-evidence-surface="f03-g3-inspector"] [data-parameter-name="Text"] input');
  const saveButton = page.locator('[data-testid="workspace-save"]');
  const editSelectedNode = async (value: string) => {
    await expect(shell).toHaveAttribute('data-workspace-state', 'ready');
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
    await selectInspectorNode(page, 120, 125);
    await expect(inspector).toHaveAttribute('data-inspector-mode', 'node');
    await expect(inspector).toHaveAttribute('data-metadata-phase', 'ready');
    const revisionBefore = Number(await flowCanvas.getAttribute('data-flow-revision'));
    await textInput.fill(value);
    await expect(textInput).toHaveValue(value);
    await textInput.press('Enter');
    await expect.poll(async () => Number(
      await flowCanvas.getAttribute('data-flow-revision')
    ), { message: `${value} flow revision` }).toBeGreaterThan(revisionBefore);
    await expect(shell).toHaveAttribute('data-workspace-dirty', 'true');
    await expect(saveButton).toBeEnabled();
  };
  await editSelectedNode('protected');

  await requestStudioHashNavigation(page, '#/about');
  await expect(page.locator('[role="dialog"]')).toContainText('未保存修改');
  await resolveLeavePrompt(page, 'stay');
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectA);
  await requestStudioHashNavigation(page, `#/projects/${projectB}/workspace`);
  await resolveLeavePrompt(page, 'discard');
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectB);

  await editSelectedNode('readonly');
  responseMode = 'readonly';
  await saveButton.click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'readonly');
  await expect(saveButton).toBeDisabled();

  await requestStudioHashNavigation(page, `#/projects/${projectA}/workspace`);
  await resolveLeavePrompt(page, 'discard');
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectA);
  await editSelectedNode('running');
  responseMode = 'running';
  await saveButton.click();
  await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'running');
  await expect(saveButton).toBeDisabled();
});

test('G4 unified leave prompt traps keyboard focus, Escape stays, and discard leaves', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() })
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const textInput = page.locator(
    '[data-evidence-surface="f03-g3-inspector"] [data-parameter-name="Text"] input'
  );
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectA);
  await expect(shell).toHaveAttribute('data-workspace-persistence-revision', '7');
  await expect(shell.getByRole('link', { name: '工程列表' })).toBeVisible();
  await expect(shell.getByRole('link', { name: '工程详情' })).toBeVisible();
  await expect(shell.getByRole('link', { name: '本次结果' })).toBeVisible();
  await selectInspectorNode(page, 120, 125);
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'workspace-idle', viewport, runtimeErrors });
  }
  await textInput.fill('keyboard-leave-guard');
  await textInput.press('Enter');
  await expect(shell).toHaveAttribute('data-workspace-dirty', 'true');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'workspace-dirty', viewport, runtimeErrors });
  }
  await textInput.focus();

  await requestStudioHashNavigation(page, '#/about');
  const dialog = page.getByRole('dialog', { name: '放弃本地工作区修改？' });
  const stay = page.locator('[data-testid="leave-guard-stay"]');
  const discard = page.locator('[data-testid="leave-guard-discard"]');
  const close = dialog.getByRole('button', { name: '关闭对话框' });
  await expect(dialog).toBeVisible();
  await expect(stay).toBeFocused();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, { scenario: 'leave-prompt', viewport, runtimeErrors });
  }
  await page.keyboard.press('Tab');
  await expect(discard).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(close).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(discard).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(textInput).toBeFocused();
  await expect(shell).toHaveAttribute('data-workspace-project-id', projectA);

  await requestStudioHashNavigation(page, '#/about');
  await expect(stay).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(discard).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/#\/about$/);
});

test('G4 Preview exposes structured, empty, business failure, safety block, network failure and cancellation states', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    previewScenario: (request, call) => {
      if (call === 2) return { body: previewPayload(request, call, { outputData: null }) };
      if (call === 3) return { body: previewPayload(request, call, { success: false, outputData: null }) };
      if (call === 4) return {
        status: 400,
        body: {
          code: 'SIDE_EFFECT_BLOCKED',
          error: '预览已安全拦截副作用算子：该算子可能访问外部设备或执行持久化写入。'
        }
      };
      if (call === 5) return { abort: true };
      if (call === 6) return { delayMs: 600, body: previewPayload(request, call) };
      return { body: previewPayload(request, call) };
    }
  });
  await selectInspectorNode(page, 120, 125);
  const preview = page.locator('[data-capability="preview-workbench"]');
  const run = page.locator('[data-testid="preview-run"]');

  await expect(preview).toHaveAttribute('data-preview-phase', 'success');
  await expect(page.locator('[data-capability="image-canvas"]')).toHaveAttribute('data-image-phase', 'empty');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-structured-success', viewport, runtimeErrors, requestAudit: audit
    });
  }

  await run.click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'empty');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-no-output', viewport, runtimeErrors, requestAudit: audit
    });
  }
  await run.click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'error');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-business-failure', viewport, runtimeErrors, requestAudit: audit
    });
  }
  await run.click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'blocked');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-safety-blocked', viewport,
      runtimeErrors: withoutExpectedPreviewTransportConsoleErrors(runtimeErrors), requestAudit: audit,
      notes: ['Expected HTTP 400 SIDE_EFFECT_BLOCKED is excluded from the browser runtime-error audit.']
    });
  }
  await run.click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'error');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-network-failure', viewport,
      runtimeErrors: withoutExpectedPreviewTransportConsoleErrors(runtimeErrors), requestAudit: audit,
      notes: ['Expected route abort net::ERR_FAILED is excluded from the browser runtime-error audit.']
    });
  }
  await run.click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'loading');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-loading', viewport,
      runtimeErrors: withoutExpectedPreviewTransportConsoleErrors(runtimeErrors), requestAudit: audit
    });
  }
  await page.locator('[data-testid="preview-cancel"]').click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'cancelled');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-cancelled', viewport,
      runtimeErrors: withoutExpectedPreviewTransportConsoleErrors(runtimeErrors), requestAudit: audit
    });
  }
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
  expect(withoutExpectedPreviewTransportConsoleErrors(runtimeErrors))
    .toEqual({ consoleErrors: [], pageErrors: [] });
});

test('F04 design handoff captures a deterministic complex flow without static showcase data', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: performanceFlow(100, 150) })
  });
  const surface = page.locator('[data-evidence-surface="f03-g2-flow-canvas"]');
  await expect(surface).toHaveAttribute('data-node-count', '100');
  await expect(surface).toHaveAttribute('data-connection-count', '150');
  const canvas = page.locator('[data-testid="flow-canvas"]');
  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  await page.mouse.click(box!.x + 60, box!.y + 60);
  await expect(surface).toHaveAttribute('data-selected-count', '1');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-complex-flow-100-150', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Deterministic 100-node/150-connection canonical FlowCanvas fixture.']
    });
  }
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
});

test('G4 Preview keeps the latest node when an older response arrives late', async ({ page }) => {
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    previewScenario: (request, call) => ({
      delayMs: request.targetNodeId === fixtureUuid(40_001) ? 1500 : 0,
      body: previewPayload(request, call)
    })
  });
  await selectInspectorNode(page, 120, 125);
  const preview = page.locator('[data-capability="preview-workbench"]');
  const run = page.locator('[data-testid="preview-run"]');
  await expect(run).toBeEnabled();
  await run.click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'loading');
  await selectInspectorNode(page, 400, 125);
  await expect(run).toBeEnabled();
  await run.click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'success');
  await expect(preview.locator('.preview-panel__result pre')).toContainText(fixtureUuid(40_002));
  await page.waitForTimeout(1600);
  await expect(preview.locator('.preview-panel__result pre')).toContainText(fixtureUuid(40_002));
  expect(audit.filter(entry => entry.method === 'POST' && entry.path === '/api/flows/preview-node').length)
    .toBeGreaterThanOrEqual(2);
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
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
      previewOwnerCount: 1,
      imageCanvasOwnerCount: 1,
      roiOwnerCount: 1,
      activeProjectId: projectId,
      ownerConflictCount: 0
    });
    await selectInspectorNode(page, 120, 125);
    await expect(shell).toHaveAttribute('data-workspace-inspector-owner-count', '1');
    await expect(page.locator('[data-product-shell]')).toHaveAttribute('data-project-command-phase', 'succeeded');
  }
  await page.goto('/studio/index.html#/about');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    flowCanvasOwnerCount: 0,
    inspectorOwnerCount: 0,
    previewOwnerCount: 0,
    imageCanvasOwnerCount: 0,
    roiOwnerCount: 0,
    activeInspectorDrafts: 0,
    activeSubscriptions: 0,
    activeTimers: 0,
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: 0,
    activeBlobUrls: 0,
    activePreviewArtifactIds: 0,
    inFlightReads: 0,
    inFlightPreview: 0,
    ownerConflictCount: 0
  });
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
});

test('G5 passes 20 save and project-switch cycles with one PUT per save and a zero final ledger', async ({ page }) => {
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() })
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  for (let cycle = 0; cycle < 20; cycle += 1) {
    await expect(shell, `cycle ${cycle} owner ready`).toHaveAttribute('data-workspace-state', 'ready');
    await expect(shell, `cycle ${cycle} owner count`).toHaveAttribute('data-workspace-owner-count', '1');
    await expect(page.locator('[data-product-shell]'), `cycle ${cycle} open authority`)
      .toHaveAttribute('data-project-command-phase', 'succeeded');
    const flowWorkspace = page.locator('[data-capability="flow-workspace"]');
    await expect(flowWorkspace, `cycle ${cycle} flow owner phase`)
      .toHaveAttribute('data-flow-owner-phase', 'mounted');
    await expect(flowWorkspace, `cycle ${cycle} flow owner count`)
      .toHaveAttribute('data-flow-owner-count', '1');
    await selectInspectorNode(page, 120, 125);
    const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
    await expect(inspector).toHaveAttribute('data-inspector-mode', 'node');
    await expect(inspector).toHaveAttribute('data-metadata-phase', 'ready');
    await expect(page.locator('[data-capability="preview-workbench"]'), `cycle ${cycle} preview settled`)
      .toHaveAttribute('data-preview-phase', 'success');
    const textInput = page.locator(
      '[data-evidence-surface="f03-g3-inspector"] [data-parameter-name="Text"] input'
    );
    const revisionBefore = Number(await page.locator('[data-evidence-surface="f03-g2-flow-canvas"]')
      .getAttribute('data-flow-revision'));
    await textInput.fill(`save-cycle-${cycle}`);
    await expect(textInput, `cycle ${cycle} filled editor value`).toHaveValue(`save-cycle-${cycle}`);
    await textInput.press('Enter');
    await expect(textInput, `cycle ${cycle} editor value`).toHaveValue(`save-cycle-${cycle}`);
    await expect.poll(async () => Number(
      await page.locator('[data-evidence-surface="f03-g2-flow-canvas"]').getAttribute('data-flow-revision')
    ), { message: `cycle ${cycle} flow revision` }).toBeGreaterThan(revisionBefore);
    await expect(shell, `cycle ${cycle} dirty`).toHaveAttribute('data-workspace-dirty', 'true');
    await page.locator('[data-testid="workspace-save"]').click();
    await expect(shell).toHaveAttribute('data-workspace-persistence-phase', 'saved');
    await expect(shell).toHaveAttribute('data-workspace-dirty', 'false');
    const nextProject = cycle % 2 === 0 ? projectB : projectA;
    await page.goto(`/studio/index.html#/projects/${nextProject}/workspace`);
    await expect(shell).toHaveAttribute('data-workspace-project-id', nextProject);
    await expect(shell).toHaveAttribute('data-workspace-state', 'ready');
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
    await expect(shell).toHaveAttribute('data-workspace-dirty', 'false');
  }
  await page.goto('/studio/index.html#/about');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    flowCanvasOwnerCount: 0,
    inspectorOwnerCount: 0,
    previewOwnerCount: 0,
    imageCanvasOwnerCount: 0,
    roiOwnerCount: 0,
    persistenceOwnerCount: 0,
    activeSubscriptions: 0,
    activeTimers: 0,
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: 0,
    activeBlobUrls: 0,
    activePreviewArtifactIds: 0,
    inFlightReads: 0,
    inFlightWrites: 0,
    inFlightPreview: 0,
    ownerConflictCount: 0
  });
  expect(audit.filter(entry => entry.method === 'PUT')).toHaveLength(20);
  expect(audit.filter(entry => /\/api\/(?:inspection\/execute|inspection\/admission|runs)/i.test(entry.path)))
    .toEqual([]);
  expect(isF03G5RequestAllowlist(audit)).toBe(true);
});

test('G6 passes 20 formal Run, Project switch, and route-leave cycles with a zero final ledger', async ({ page }) => {
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() })
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  for (let cycle = 0; cycle < 20; cycle += 1) {
    await expect(shell, `cycle ${cycle} Workspace ready`).toHaveAttribute('data-workspace-state', 'ready');
    await expect(page.locator('[data-testid="workspace-run"]'), `cycle ${cycle} Run enabled`).toBeEnabled();
    await page.locator('[data-testid="workspace-run"]').click();
    await expect(shell, `cycle ${cycle} Run succeeded`).toHaveAttribute('data-workspace-run-phase', 'succeeded');
    await expect(page.locator('[data-testid="workspace-current-result"]'), `cycle ${cycle} result link`).toBeVisible();

    const nextProject = cycle % 2 === 0 ? projectB : projectA;
    await page.goto(`/studio/index.html#/projects/${nextProject}/workspace`);
    await expect(shell, `cycle ${cycle} next Project`).toHaveAttribute('data-workspace-project-id', nextProject);
    await expect.poll(async () => await workspaceDiagnostics(page), { message: `cycle ${cycle} owner ledger` })
      .toMatchObject({
        workspaceOwnerCount: 1,
        persistenceOwnerCount: 1,
        runOwnerCount: 1,
        activeProjectId: nextProject,
        inFlightExecute: 0,
        activeAbortControllers: 0,
        ownerConflictCount: 0
      });
  }
  await page.goto('/studio/index.html#/about');
  await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
    workspaceOwnerCount: 0,
    flowCanvasOwnerCount: 0,
    persistenceOwnerCount: 0,
    runOwnerCount: 0,
    inspectorOwnerCount: 0,
    previewOwnerCount: 0,
    imageCanvasOwnerCount: 0,
    roiOwnerCount: 0,
    activeSubscriptions: 0,
    activeTimers: 0,
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: 0,
    activeBlobUrls: 0,
    activePreviewArtifactIds: 0,
    inFlightReads: 0,
    inFlightWrites: 0,
    inFlightPreview: 0,
    inFlightExecute: 0,
    totalRunMounts: 21,
    totalRunDisposals: 21,
    ownerConflictCount: 0
  });
  expect(audit.filter(entry => entry.method === 'POST' &&
    (entry.path === '/api/inspection/admission' || entry.path === '/api/inspection/execute')))
    .toHaveLength(40);
  expect(isF03G6RequestAllowlist(audit)).toBe(true);
});

test('G6 passes 20 run, stop/reconcile, project, and route lifecycle cycles with zero resources', async ({ page }) => {
  let mode: 'stop' | 'reconcile' = 'stop';
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: inspectorFlow() }),
    runScenario: (stage, request) => {
      if (stage === 'execute') return mode === 'stop' ? { delayMs: 1000, abort: true } : { abort: true };
      if (stage === 'stop') {
        return {
          body: reconciliationFixture(
            request,
            'cancelled',
            formalRunFixtureResult(request, 91_000 + Number(request.expectedPersistenceRevision), 'Cancelled', 'NotApplicable')
          )
        };
      }
      if (stage === 'reconcile') {
        return {
          body: mode === 'reconcile'
            ? reconciliationFixture(
                request,
                'succeeded',
                formalRunFixtureResult(request, 92_000 + Number(request.expectedPersistenceRevision), 'Succeeded', 'Ok')
              )
            : reconciliationFixture(request, 'cancelled')
        };
      }
      return {};
    }
  });
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  let currentProject = projectA;

  for (let cycle = 0; cycle < 20; cycle += 1) {
    await page.goto(`/studio/index.html#/projects/${currentProject}/workspace`);
    await expect(shell, `cycle ${cycle} Workspace ready`).toHaveAttribute('data-workspace-state', 'ready');
    await expect(page.locator('[data-testid="workspace-run"]'), `cycle ${cycle} Run enabled`).toBeEnabled();
    mode = cycle % 2 === 0 ? 'stop' : 'reconcile';
    await page.locator('[data-testid="workspace-run"]').click();

    if (mode === 'stop') {
      await expect(shell, `cycle ${cycle} executing`).toHaveAttribute('data-workspace-run-phase', 'executing');
      await page.locator('[data-testid="workspace-run-stop"]').click();
      await expect(shell, `cycle ${cycle} cancelled`).toHaveAttribute('data-workspace-run-phase', 'cancelled');
    } else {
      await expect(shell, `cycle ${cycle} unknown outcome`).toHaveAttribute('data-workspace-run-phase', 'unknown-outcome');
      await page.locator('[data-testid="workspace-run-reconcile"]').click();
      await expect(shell, `cycle ${cycle} reconciled result`).toHaveAttribute('data-workspace-run-phase', 'succeeded');
      await expect(page.locator('[data-testid="workspace-current-result"]'), `cycle ${cycle} result link`).toBeVisible();
    }

    currentProject = currentProject === projectA ? projectB : projectA;
    await page.goto(`/studio/index.html#/projects/${currentProject}/workspace`);
    await expect(shell, `cycle ${cycle} project switch`).toHaveAttribute('data-workspace-project-id', currentProject);
    await expect.poll(async () => await workspaceDiagnostics(page), { message: `cycle ${cycle} active ledger` })
      .toMatchObject({
        workspaceOwnerCount: 1,
        persistenceOwnerCount: 1,
        runOwnerCount: 1,
        activeProjectId: currentProject,
        activeAbortControllers: 0,
        inFlightReads: 0,
        inFlightWrites: 0,
        inFlightExecute: 0
      });
    await page.goto('/studio/index.html#/about');
    await expect.poll(async () => await workspaceDiagnostics(page), { message: `cycle ${cycle} route leave ledger` })
      .toMatchObject({
        workspaceOwnerCount: 0,
        persistenceOwnerCount: 0,
        runOwnerCount: 0,
        activeSubscriptions: 0,
        activeTimers: 0,
        activeAbortControllers: 0,
        activeBlobUrls: 0,
        activePreviewArtifactIds: 0,
        inFlightReads: 0,
        inFlightWrites: 0,
        inFlightPreview: 0,
        inFlightExecute: 0
      });
  }

  expect(audit.filter(entry => entry.method === 'POST' && [
    '/api/inspection/admission', '/api/inspection/execute', '/api/inspection/stop', '/api/inspection/reconcile'
  ].includes(entry.path)))
    .toHaveLength(60);
  expect(isF03G6RequestAllowlist(audit)).toBe(true);
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
      const workspace = page.locator('[data-capability="flow-workspace"]');
      await expect(workspace).toHaveAttribute('data-flow-owner-phase', 'mounted');
      await expect(workspace).toHaveAttribute('data-flow-owner-count', '1');
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
      await expect(surface).toHaveAttribute('data-selected-count', '1');
      await expect(page.locator('[data-evidence-surface="f03-workspace-shell"]'))
        .toHaveAttribute('data-workspace-dirty', 'true');
      await requestStudioHashNavigation(page, '#/about');
      await resolveLeavePrompt(page, 'discard');
      await expect(page).toHaveURL(/#\/about$/);
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
    expect(isF03G4RequestAllowlist(audit)).toBe(true);
  });
}

test('rejects a 401 before mounting ProductRuntime or Workspace owners', async ({ page }) => {
  const audit = await bootWorkspace(page, { authStatus: 401, expectAuthShell: true });
  await expect(page.locator('[data-auth-page="login"]')).toBeVisible();
  await expect(page.locator('[data-evidence-surface="f03-workspace-shell"]')).toHaveCount(0);
  expect(await workspaceDiagnostics(page)).toBeNull();
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
});

test('passes 20 real Browser route mount/unmount cycles with a zero ledger', async ({ page }) => {
  const audit = await bootWorkspace(page);
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');

  for (let cycle = 0; cycle < 20; cycle += 1) {
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
    await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
      previewOwnerCount: 1,
      imageCanvasOwnerCount: 1,
      roiOwnerCount: 1,
      ownerConflictCount: 0
    });
    await page.goto('/studio/index.html#/about');
    await expect.poll(async () => await workspaceDiagnostics(page)).toMatchObject({
      workspaceOwnerCount: 0,
      flowCanvasOwnerCount: 0,
      previewOwnerCount: 0,
      imageCanvasOwnerCount: 0,
      roiOwnerCount: 0,
      activeSubscriptions: 0,
      activeAbortControllers: 0,
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      inFlightReads: 0
    });
    await page.goto(`/studio/index.html#/projects/${projectA}/workspace`);
    await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  }

  await page.goto('/studio/index.html#/about');
  const final = await workspaceDiagnostics(page);
  expect(final).toMatchObject({
    workspaceOwnerCount: 0,
    previewOwnerCount: 0,
    imageCanvasOwnerCount: 0,
    roiOwnerCount: 0,
    activeSubscriptions: 0,
    activeAbortControllers: 0,
    activeBlobUrls: 0,
    activePreviewArtifactIds: 0,
    inFlightReads: 0,
    totalWorkspaceMounts: 21,
    totalWorkspaceDisposals: 21,
    ownerConflictCount: 0
  });
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
});

test('Workspace splitters preserve bounds, Preview recovery and layout preferences across re-entry', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const audit = await bootWorkspace(page);
  const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
  const workspace = page.locator('.flow-workspace');
  const inspectorSplitter = page.locator('[data-workspace-splitter="inspector"]');
  const previewSplitter = page.locator('[data-workspace-splitter="preview"]');
  const previewToggle = page.locator('[data-testid="preview-collapse-toggle"]');

  await expect(shell).toHaveAttribute('data-workspace-owner-count', '1');
  await expect(workspace).toHaveAttribute('data-inspector-width', '280');
  await expect(workspace).toHaveAttribute('data-preview-height', '220');
  await expect(workspace).toHaveAttribute('data-preview-width', '340');
  await expect(inspectorSplitter).toHaveAttribute('aria-valuetext', '属性检查器宽度 280 像素');
  await expect(previewSplitter).toHaveAttribute('aria-valuetext', '预览工作台宽度 340 像素');

  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-layout-default', viewport, runtimeErrors, requestAudit: audit
    });
  }

  const inspectorSplitterBounds = await inspectorSplitter.boundingBox();
  expect(inspectorSplitterBounds).not.toBeNull();
  const inspectorStartX = (inspectorSplitterBounds?.x ?? 0) + (inspectorSplitterBounds?.width ?? 0) / 2;
  const inspectorStartY = (inspectorSplitterBounds?.y ?? 0) + 120;
  await page.mouse.move(
    inspectorStartX,
    inspectorStartY
  );
  await page.mouse.down();
  await page.mouse.move(inspectorStartX + 40, inspectorStartY);
  await page.mouse.up();
  await expect(workspace).toHaveAttribute('data-inspector-width', '320');
  await inspectorSplitter.dblclick();
  await expect(workspace).toHaveAttribute('data-inspector-width', '280');

  await inspectorSplitter.focus();
  await page.keyboard.press('Home');
  await expect(workspace).toHaveAttribute('data-inspector-width', '240');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-inspector-min', viewport, runtimeErrors, requestAudit: audit
    });
  }
  await page.keyboard.press('End');
  await expect(workspace).toHaveAttribute('data-inspector-width', '380');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-inspector-max', viewport, runtimeErrors, requestAudit: audit
    });
  }
  await inspectorSplitter.dblclick();
  await expect(workspace).toHaveAttribute('data-inspector-width', '280');

  const previewSplitterBounds = await previewSplitter.boundingBox();
  expect(previewSplitterBounds).not.toBeNull();
  const previewStartX = (previewSplitterBounds?.x ?? 0) + (previewSplitterBounds?.width ?? 0) / 2;
  const previewStartY = (previewSplitterBounds?.y ?? 0) + (previewSplitterBounds?.height ?? 0) / 2;
  await page.mouse.move(
    previewStartX,
    previewStartY
  );
  await page.mouse.down();
  await page.mouse.move(previewStartX - 40, previewStartY);
  await page.mouse.up();
  await expect(workspace).toHaveAttribute('data-preview-width', '380');
  await previewSplitter.dblclick();
  await expect(workspace).toHaveAttribute('data-preview-width', '340');

  await previewSplitter.focus();
  await page.keyboard.press('Home');
  await expect(workspace).toHaveAttribute('data-preview-width', '300');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-min', viewport, runtimeErrors, requestAudit: audit
    });
  }
  await page.keyboard.press('End');
  await expect(workspace).toHaveAttribute('data-preview-width', '480');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-max', viewport, runtimeErrors, requestAudit: audit
    });
  }
  await previewSplitter.dblclick();
  await expect(workspace).toHaveAttribute('data-preview-width', '340');

  await inspectorSplitter.focus();
  await page.keyboard.press('Shift+ArrowRight');
  await expect(workspace).toHaveAttribute('data-inspector-width', '312');
  await previewSplitter.focus();
  await page.keyboard.press('Shift+ArrowLeft');
  await expect(workspace).toHaveAttribute('data-preview-width', '372');

  await previewToggle.focus();
  await previewToggle.click();
  await expect(previewToggle).toBeFocused();
  await expect(workspace).toHaveAttribute('data-preview-collapsed', 'true');
  await expect(workspace).toHaveAttribute('data-preview-height', '38');
  await expect(workspace).toHaveAttribute('data-preview-width', '44');
  await expect(previewSplitter).toBeHidden();
  await expect(shell).toHaveAttribute('data-workspace-preview-owner-count', '1');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-collapsed', viewport, runtimeErrors, requestAudit: audit
    });
  }

  await page.goto('/studio/index.html#/about');
  await page.goto(`/studio/index.html#/projects/${projectA}/workspace`);
  await expect(workspace).toHaveAttribute('data-inspector-width', '312');
  await expect(workspace).toHaveAttribute('data-preview-collapsed', 'true');
  await page.locator('[data-testid="preview-collapse-toggle"]').click();
  await expect(workspace).toHaveAttribute('data-preview-collapsed', 'false');
  await expect(workspace).toHaveAttribute('data-preview-height', '220');
  await expect(workspace).toHaveAttribute('data-preview-width', '372');
  await expect(previewSplitter).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-preview-restored', viewport, runtimeErrors, requestAudit: audit
    });
  }

  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
});

test('narrow Workspace overlays restore focus and remain inside the viewport', async ({ page }) => {
  await page.setViewportSize({ width: 900, height: 704 });
  const runtimeErrors = createF03RuntimeErrorAudit(page);
  await bootWorkspace(page);

  const inspectorToggle = page.getByRole('button', { name: '打开属性检查器' });
  await expect(inspectorToggle).toBeVisible();
  await inspectorToggle.click();
  await expect(page.locator('#workspace-inspector-pane')).toBeVisible();
  expect(await page.locator('#workspace-inspector-pane').evaluate(element =>
    element.contains(document.activeElement))).toBe(true);
  const inspectorBounds = await page.locator('#workspace-inspector-pane').boundingBox();
  expect((inspectorBounds?.x ?? 0) + (inspectorBounds?.width ?? 0)).toBeLessThanOrEqual(901);
  await page.keyboard.press('Escape');
  await expect(inspectorToggle).toBeFocused();
  await expect(page.locator('#workspace-inspector-pane')).toBeHidden();

  await page.setViewportSize({ width: 740, height: 704 });
  const railToggle = page.getByRole('button', { name: '打开算子区' });
  await expect(railToggle).toBeVisible();
  await railToggle.click();
  await expect(page.locator('#workspace-operator-pane')).toBeVisible();
  expect(await page.locator('#workspace-operator-pane').evaluate(element =>
    element.contains(document.activeElement))).toBe(true);
  const railBounds = await page.locator('#workspace-operator-pane').boundingBox();
  expect((railBounds?.x ?? 0) + (railBounds?.width ?? 0)).toBeLessThanOrEqual(741);
  await page.keyboard.press('Escape');
  await expect(railToggle).toBeFocused();
  await expect(page.locator('#workspace-operator-pane')).toBeHidden();

  const overflow = await page.evaluate(() => ({
    horizontal: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    vertical: document.documentElement.scrollHeight - document.documentElement.clientHeight
  }));
  expect(overflow).toEqual({ horizontal: 0, vertical: 0 });
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
});

test('Prompt 3 refines Operator Rail and populated Inspector across width and long-Chinese states', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, {
      name: '华东二号产线瓶盖外观与密封完整性综合检测工程（夜班工艺验证）',
      flow: prompt3InspectorFlow()
    }),
    operatorCatalogBody: prompt3OperatorCatalog()
  });
  const workspace = page.locator('.flow-workspace');
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  const inspectorSplitter = page.locator('[data-workspace-splitter="inspector"]');
  await ensureOperatorFlyout(page);
  const search = page.locator('[data-testid="operator-search"]');
  const category = page.locator('[data-testid="operator-category"]');

  await category.selectOption('ImagePreprocessing');
  await search.fill('缺陷数量');
  const operator = page.locator('.operator-item');
  await expect(operator).toHaveCount(1);
  await expect(operator).toContainText('瓶盖外观与密封完整性综合检测');
  await operator.focus();
  const dataTransfer = await page.evaluateHandle(() => new DataTransfer());
  await operator.dispatchEvent('dragstart', { dataTransfer });
  await expect(operator).toHaveAttribute('data-dragging', 'true');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-prompt3-operator-search-drag', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Prompt 3 Operator Rail search, category, focus and drag state.']
    });
  }
  await operator.dispatchEvent('dragend');
  await search.fill('');
  await category.selectOption('');
  await page.getByRole('button', { name: '关闭算子面板' }).click();
  await expect(page.locator('[data-capability="operator-flyout"]')).toHaveCount(0);

  await selectInspectorNode(page, 120, 125);
  await expect(inspector).toHaveAttribute('data-inspector-mode', 'node');
  await expect(inspector).toContainText('上料工位瓶盖外观与密封完整性综合检测节点');
  await expect(inspector).toContainText('工艺切换备注与异常处置说明');
  await expect(workspace).toHaveAttribute('data-inspector-width', '280');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-prompt3-inspector-default-long-zh', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Prompt 3 populated Inspector at the 280px default width.']
    });
  }

  await inspectorSplitter.focus();
  await page.keyboard.press('Home');
  await expect(workspace).toHaveAttribute('data-inspector-width', '240');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-prompt3-inspector-min-long-zh', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Prompt 3 populated Inspector at the 240px minimum width.']
    });
  }

  await page.keyboard.press('End');
  await expect(workspace).toHaveAttribute('data-inspector-width', '380');
  const countInput = inspector.locator('[data-parameter-name="Count"] input[type="number"]');
  await countInput.fill('11');
  await countInput.blur();
  await expect(inspector.locator('.inspector-panel__validation')).toContainText('不能大于 10');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-prompt3-inspector-max-validation', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Prompt 3 populated Inspector at 380px with an inline validation error.']
    });
  }

  const internalOverflow = await page.evaluate(() => Object.fromEntries([
    ['operator', document.querySelector('.operator-rail__categories')],
    ['flowToolbar', document.querySelector('.flow-canvas-surface__toolbar')],
    ['inspector', document.querySelector('.inspector-panel__body')]
  ].map(([key, element]) => [key, element ? element.scrollWidth - element.clientWidth : null])));
  expect(internalOverflow).toEqual({ operator: 0, flowToolbar: 0, inspector: 0 });
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
});

test('Prompt 3 Inspector explains disabled parameters without exposing internal metadata terms', async ({ page }) => {
  const viewport = { width: 1920, height: 1080 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, { flow: prompt3InspectorFlow() }),
    operatorCatalogBody: []
  });
  const inspector = page.locator('[data-evidence-surface="f03-g3-inspector"]');
  await selectInspectorNode(page, 120, 125);
  await expect(inspector).toHaveAttribute('data-metadata-phase', 'missing');
  await expect(inspector).toContainText('参数定义');
  await expect(inspector).not.toContainText('metadata');
  await expect(inspector.locator('[data-parameter-name="Text"] input')).toBeDisabled();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-prompt3-inspector-disabled', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Prompt 3 disabled Inspector state with Chinese recovery guidance.']
    });
  }
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
});

test('Prompt 3 Preview preserves image, result, ROI, empty and error hierarchy on a short comfortable viewport', async ({ page }) => {
  const viewport = { width: 1350, height: 704 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const audit = await bootWorkspace(page, {
    projectBody: projectId => projectPayload(projectId, {
      name: '瓶盖 ROI 与结构化结果联合调试工程（短屏验证）',
      flow: prompt3PreviewFlow()
    }),
    operatorCatalogBody: prompt3PreviewOperatorCatalog(),
    previewScenario: (request, call) => {
      if (call === 1) return {
        body: previewPayload(request, call, {
          outputData: {
            判定: '检测通过',
            置信度: 0.9826,
            缺陷区域数量: 0,
            说明: '瓶盖边缘、密封区域与表面纹理均满足当前工程阈值。'
          },
          artifacts: [previewArtifactReference(call)]
        })
      };
      if (call === 2) return {
        body: previewPayload(request, call, { outputData: null, artifacts: [] })
      };
      return {
        body: previewPayload(request, call, {
          success: false,
          outputData: null,
          artifacts: [],
          errorMessage: '无法读取瓶盖定位所需的标定资产；当前预览已停止。请检查工程资产是否完整，然后重新预览。',
          failedOperatorName: 'ROI 矩形编辑节点',
          failedOperatorType: 'RoiManager',
          diagnostics: [{ code: 'CALIBRATION_ASSET_MISSING', message: '未找到相机一对应的平面标定资产。', pathHint: '$.assets' }],
          missingResources: [{
            resourceType: 'CalibrationAsset', resourceKey: 'camera-1-plane',
            description: '相机一平面标定资产未配置。', diagnosticCode: 'CVP401'
          }]
        })
      };
    }
  });
  await page.locator('[data-product-appearance] summary').click();
  await page.getByRole('button', { name: '舒适' }).click();
  await page.locator('[data-product-appearance] summary').click();
  await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable');
  await selectInspectorNode(page, 120, 125);

  const preview = page.locator('[data-capability="preview-workbench"]');
  const image = page.locator('[data-capability="image-canvas"]');
  const roi = page.locator('.preview-panel__roi');
  const run = page.locator('[data-testid="preview-run"]');
  await expect(preview).toHaveAttribute('data-preview-phase', 'success');
  await expect(image).toHaveAttribute('data-image-phase', 'ready');
  await expect(preview.locator('.preview-panel__result pre')).toContainText('检测通过');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-prompt3-preview-success-1350-comfortable', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Prompt 3 Preview with image, structured result and ROI at 1350x704 comfortable.']
    });
  }

  await page.locator('[data-testid="roi-start"]').click();
  await expect(roi).toHaveAttribute('data-roi-phase', 'editing');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-prompt3-preview-roi-editing-1350-comfortable', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Prompt 3 ROI editing actions remain visible on the short viewport.']
    });
  }
  await page.locator('[data-testid="roi-cancel"]').click();

  await run.click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'empty');
  await expect(image).toHaveAttribute('data-image-phase', 'empty');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-prompt3-preview-empty-1350-comfortable', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Prompt 3 Preview empty result state.']
    });
  }

  await run.click();
  await expect(preview).toHaveAttribute('data-preview-phase', 'error');
  await expect(preview).toContainText('无法读取瓶盖定位所需的标定资产');
  await expect(preview).toContainText('CALIBRATION_ASSET_MISSING');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'workspace-prompt3-preview-error-1350-comfortable', viewport, runtimeErrors, requestAudit: audit,
      notes: ['Prompt 3 Preview error and diagnostic hierarchy with recovery guidance.']
    });
  }

  const overflow = await page.evaluate(() => ({
    pageHorizontal: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    pageVertical: document.documentElement.scrollHeight - document.documentElement.clientHeight,
    previewHorizontal: (() => {
      const element = document.querySelector('.preview-panel__body');
      return element ? element.scrollWidth - element.clientWidth : null;
    })(),
    imageToolbarHorizontal: (() => {
      const element = document.querySelector('.image-viewport__toolbar');
      return element ? element.scrollWidth - element.clientWidth : null;
    })(),
    detailsHorizontal: (() => {
      const element = document.querySelector('.preview-panel__details');
      return element ? element.scrollWidth - element.clientWidth : null;
    })()
  }));
  expect(overflow).toEqual({
    pageHorizontal: 0,
    pageVertical: 0,
    previewHorizontal: 0,
    imageToolbarHorizontal: 0,
    detailsHorizontal: 0
  });
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  expect(isF03G4RequestAllowlist(audit)).toBe(true);
});

for (const scenario of [
  { viewport: { width: 1920, height: 1080 }, density: 'compact' },
  { viewport: { width: 1366, height: 768 }, density: 'compact' },
  { viewport: { width: 1350, height: 704 }, density: 'compact' },
  { viewport: { width: 1920, height: 1080 }, density: 'comfortable' },
  { viewport: { width: 1350, height: 704 }, density: 'comfortable' }
] as const) {
  test(`Workspace Shell fits ${scenario.viewport.width}x${scenario.viewport.height} at ${scenario.density} density`, async ({ page }) => {
    const { viewport, density } = scenario;
    await page.setViewportSize(viewport);
    const runtimeErrors = createF03RuntimeErrorAudit(page);
    const audit = await bootWorkspace(page);
    const shell = page.locator('[data-evidence-surface="f03-workspace-shell"]');
    await expect(shell).toHaveAttribute('data-workspace-state', 'empty');
    if (density === 'comfortable') {
      await page.locator('[data-product-appearance] summary').click();
      await page.getByRole('button', { name: '舒适' }).click();
      await page.locator('[data-product-appearance] summary').click();
    }
    await expect(page.locator('html')).toHaveAttribute('data-density', density);

    const layout = await page.evaluate(() => {
      const topbar = document.querySelector('.product-layout__topbar')?.getBoundingClientRect();
      const toolbar = document.querySelector('.workspace-shell__toolbar')?.getBoundingClientRect();
      const status = document.querySelector('.workspace-shell__statusbar')?.getBoundingClientRect();
      const canvas = document.querySelector('.flow-canvas-surface__stage')?.getBoundingClientRect();
      const inspector = document.querySelector('.inspector-panel')?.getBoundingClientRect();
      const operatorRail = document.querySelector('.operator-rail')?.getBoundingClientRect();
      const workspace = document.querySelector('.flow-workspace')?.getBoundingClientRect();
      const save = document.querySelector('[data-testid="workspace-save"]')?.getBoundingClientRect();
      const run = document.querySelector('[data-testid="workspace-run"]')?.getBoundingClientRect();
      return {
        horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        verticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight,
        topbar: topbar ? { height: topbar.height } : null,
        toolbar: toolbar ? { top: toolbar.top, bottom: toolbar.bottom } : null,
        status: status ? { top: status.top, bottom: status.bottom } : null,
        canvas: canvas ? { width: canvas.width, height: canvas.height } : null,
        inspector: inspector ? { width: inspector.width, height: inspector.height } : null,
        operatorRail: operatorRail ? { top: operatorRail.top, bottom: operatorRail.bottom, height: operatorRail.height } : null,
        workspace: workspace ? { top: workspace.top, bottom: workspace.bottom, height: workspace.height } : null,
        saveVisible: Boolean(save && save.width > 0 && save.height > 0),
        runVisible: Boolean(run && run.width > 0 && run.height > 0),
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
    expect(layout.topbar?.height).toBeLessThanOrEqual(density === 'comfortable' ? 56 : 52);
    expect(layout.saveVisible).toBe(true);
    expect(layout.runVisible).toBe(true);
    expect(layout.inspector?.width).toBeGreaterThanOrEqual(248);
    expect(layout.operatorRail?.top).toBeCloseTo(layout.workspace?.top ?? -1, 0);
    expect(layout.operatorRail?.bottom).toBeCloseTo(layout.workspace?.bottom ?? -1, 0);
    expect(layout.operatorRail?.height).toBeCloseTo(layout.workspace?.height ?? -1, 0);
    expect(layout.canvas?.width).toBeGreaterThanOrEqual(viewport.width >= 1900 ? 900 : 600);
    expect(layout.canvas?.height).toBeGreaterThanOrEqual(300);
    expect(isF03G4RequestAllowlist(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });

    if (hasF03VisualEvidenceTarget()) {
      await captureF03WorkspaceEvidence(page, {
        scenario: `workspace-shell-${viewport.width}x${viewport.height}-${density}`,
        viewport,
        requests: audit,
        runtimeErrors
      });
    }
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario: `workspace-prompt3-layout-default-${density}`,
        viewport,
        runtimeErrors,
        requestAudit: audit,
        notes: ['Prompt 3/5 Workspace core tool and Inspector visual refinement evidence.']
      });
    }
  });
}
