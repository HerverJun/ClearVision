import { test, expect, Page } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

type Theme = 'dark' | 'light';
type BuildScenario = 'ready' | 'parameters' | 'resources' | 'mixed' | 'validation-failed' | 'dryrun-failed' | 'applied';

const buildFlow = {
  operators: [
    {
      id: 'op_acq', type: 'ImageAcquisition', name: '图像采集', x: 80, y: 96,
      inputPorts: [], outputPorts: [{ id: 'op_acq_out', name: 'Image', dataType: 'Image' }],
      parameters: [{ name: 'CameraId', value: 'camera-main' }], isEnabled: true,
    },
    {
      id: 'op_roi', type: 'RoiManager', name: 'ROI 管理器', x: 360, y: 96,
      inputPorts: [{ id: 'op_roi_in', name: 'Image', dataType: 'Image', isRequired: true }],
      outputPorts: [{ id: 'op_roi_out', name: 'Image', dataType: 'Image' }],
      parameters: [], isEnabled: true,
    },
    {
      id: 'op_threshold', type: 'Threshold', name: '二值化', x: 640, y: 96,
      inputPorts: [{ id: 'op_threshold_in', name: 'Image', dataType: 'Image', isRequired: true }],
      outputPorts: [{ id: 'op_threshold_out', name: 'BinaryImage', dataType: 'Image' }],
      parameters: [{ name: 'Threshold', value: 128 }], isEnabled: true,
    },
    {
      id: 'op_output', type: 'ResultOutput', name: '结果输出', x: 920, y: 96,
      inputPorts: [{ id: 'op_output_in', name: 'Result', dataType: 'Image', isRequired: true }],
      outputPorts: [], parameters: [], isEnabled: true,
    },
  ],
  connections: [
    { id: 'conn_acq_roi', sourceOperatorId: 'op_acq', sourcePortId: 'op_acq_out', targetOperatorId: 'op_roi', targetPortId: 'op_roi_in' },
    { id: 'conn_roi_threshold', sourceOperatorId: 'op_roi', sourcePortId: 'op_roi_out', targetOperatorId: 'op_threshold', targetPortId: 'op_threshold_in' },
    { id: 'conn_threshold_output', sourceOperatorId: 'op_threshold', sourcePortId: 'op_threshold_out', targetOperatorId: 'op_output', targetPortId: 'op_output_in' },
  ],
};

function createBuildPayload(scenario: BuildScenario) {
  const pendingParameters = scenario === 'parameters' || scenario === 'mixed'
    ? [{ operatorId: 'op_threshold', parameterNames: scenario === 'mixed' ? ['Threshold', 'ModelPath'] : ['Threshold'] }]
    : [];
  const missingResources = scenario === 'resources' || scenario === 'mixed'
    ? [{
        resourceType: 'model_resource',
        resourceKey: 'op_threshold.ModelPath',
        operatorId: 'op_threshold',
        parameterName: 'ModelPath',
        description: '验证前需要绑定缺陷检测模型资源。',
      }]
    : [];
  const validationFailed = scenario === 'validation-failed' || scenario === 'dryrun-failed';
  const gateReady = !pendingParameters.length && !missingResources.length && !validationFailed;
  const validationPreview = scenario === 'validation-failed'
    ? {
        structuralValidation: { passed: false, status: 'failed', summary: '输出端口类型不兼容。' },
        dryRun: { status: 'pending' },
        deploymentPrecheck: { readyForDeployment: false, deploymentBlocked: true },
      }
    : scenario === 'dryrun-failed'
      ? {
          structuralValidation: { passed: true, status: 'passed' },
          dryRun: { succeeded: false, status: 'failed', inputSummary: '离线样本 sample-01', summary: '未产生有效判定结果。' },
          deploymentPrecheck: { readyForDeployment: false, deploymentBlocked: true },
        }
      : {
          structuralValidation: { passed: true, status: 'passed' },
          dryRun: { succeeded: true, status: 'completed', inputSummary: '离线样本 sample-01' },
          deploymentPrecheck: { readyForDeployment: gateReady, deploymentBlocked: !gateReady },
        };
  const applyGate = {
    canvasApplyReady: scenario === 'resources' || scenario === 'mixed' ? true : gateReady,
    runtimeDraftReady: true,
    deploymentReady: gateReady,
    blocked: !gateReady,
    status: gateReady ? 'ready' : 'blocked',
    deploymentBlockers: scenario === 'mixed'
      ? ['pending_parameter:op_threshold.Threshold', 'missing_model_resource:op_threshold.ModelPath']
      : pendingParameters.length
        ? ['pending_parameter:op_threshold.Threshold']
        : missingResources.length
        ? ['missing_model_resource:op_threshold.ModelPath']
        : validationFailed
          ? ['validation_failed']
          : [],
    metadataOnly: true,
  };
  return {
    success: true,
    status: scenario === 'applied' ? 'applied' : validationFailed ? 'failed' : 'completed',
    completionStatus: validationFailed ? 'failed' : 'completed',
    interactionState: validationFailed ? 'failed' : 'completed',
    aiExplanation: validationFailed ? '流程草稿已生成，但验证未通过。' : '构建完成，进入工程补齐与应用工作台。',
    flow: buildFlow,
    pendingParameters,
    missingResources,
    validationPreview,
    lastAttemptDiagnostics: scenario === 'validation-failed'
      ? [{ severity: 'error', stage: 'validate_schema', category: 'connection', message: '输出端口类型不兼容。', repairHint: '检查结果输出算子的输入类型。' }]
      : scenario === 'dryrun-failed'
        ? [{
            stage: 'metadata_dry_run',
            issues: [{ severity: 'error', category: 'validation', message: 'DryRun 未产生有效判定结果。', repairHint: '检查输入样本与阈值。' }],
          }]
        : [],
    buildResult: {
      buildId: `build-${scenario}`,
      workflowDraft: { operatorCount: buildFlow.operators.length, connectionCount: buildFlow.connections.length },
      operatorPipeline: buildFlow.operators.map(operator => ({
        tempId: operator.id,
        operatorType: operator.type,
        displayName: operator.name,
        status: 'completed',
        source: 'plan',
      })),
      parameterMapping: [{
        tempId: 'op_threshold',
        operatorType: 'Threshold',
        parameterName: 'Threshold',
        valueSummary: scenario === 'parameters' || scenario === 'mixed' ? '<pending>' : '128',
        source: scenario === 'parameters' || scenario === 'mixed' ? 'pending' : 'default',
        pending: scenario === 'parameters' || scenario === 'mixed',
      }],
      workflowDiff: {
        addedNodes: buildFlow.operators.map(operator => operator.name),
        modifiedNodes: scenario === 'parameters' || scenario === 'mixed' ? ['二值化'] : [],
        removedNodes: [],
        connectionChanges: buildFlow.connections,
        parameterChanges: scenario === 'parameters' || scenario === 'mixed' ? ['二值化阈值'] : ['采集源', '输出格式'],
        pendingParameters,
        deploymentBlockers: applyGate.deploymentBlockers,
      },
      applyGate,
      firstFixRecommendation: validationFailed ? '先修复最主要的验证问题。' : '',
      metadataOnly: true,
    },
    applyGate,
    metadataOnly: true,
  };
}

async function mockBaseApis(page: Page, theme: Theme): Promise<void> {
  await page.route('**/api/settings', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ general: { softwareTitle: 'ClearVision', theme, autoStart: false } }),
  }));
  await page.route('**/api/operators/types', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.route('**/api/operators/library', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.route('**/api/projects**', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.route('**/api/cameras/bindings', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.route('**/api/health', route => route.fulfill({ status: 200, contentType: 'application/json', body: '{"ok":true}' }));
}

async function installFakeWebView2(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const listeners: Record<string, Array<(event: { data: unknown }) => void>> = {};
    (window as any).__cvWebViewMessages = [];
    (window as any).chrome = {
      webview: {
        addEventListener(type: string, handler: (event: { data: unknown }) => void) {
          listeners[type] = listeners[type] || [];
          listeners[type].push(handler);
        },
        removeEventListener(type: string, handler: (event: { data: unknown }) => void) {
          listeners[type] = (listeners[type] || []).filter(item => item !== handler);
        },
        postMessage(message: unknown) {
          (window as any).__cvWebViewMessages.push(message);
        },
      },
    };
  });
}

async function pickModelResource(page: Page, button: ReturnType<Page['locator']>, filePath: string): Promise<void> {
  await button.click();
  const pickMessage = await page.evaluate(() =>
    [...((window as any).__cvWebViewMessages || [])]
      .reverse()
      .find((message: any) => message.messageType === 'PickFileCommand'));
  expect(pickMessage).toMatchObject({
    messageType: 'PickFileCommand',
    parameterName: 'aiPendingParameterFile',
  });
  expect(pickMessage.filter).toContain('Model Files');
  await page.evaluate(path => {
    (window as any).aiPanel._handleFilePickedEvent({
      payload: { parameterName: 'aiPendingParameterFile', filePath: path },
    });
  }, filePath);
}

async function openAi(page: Page, options: { width: number; height: number; theme?: Theme }): Promise<void> {
  const theme = options.theme ?? 'dark';
  await page.setViewportSize({ width: options.width, height: options.height });
  await installFakeWebView2(page);
  await page.route('**/api/**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: '{}',
  }));
  await mockBaseApis(page, theme);
  const authReady = page.waitForResponse(response => response.url().includes('/api/auth/me') && response.ok());
  await bootAuthenticatedApp(page);
  await authReady;
  await page.locator('.nav-btn[data-view="ai"]').click();
  await page.waitForFunction(() => Boolean((window as any).aiPanel));
  await page.evaluate(selectedTheme => { document.documentElement.dataset.theme = selectedTheme; }, theme);
  await page.addStyleTag({
    content: `
      *, *::before, *::after {
        animation-duration: 0s !important;
        animation-delay: 0s !important;
        transition-duration: 0s !important;
        caret-color: transparent !important;
      }
    `,
  });
}

async function seedBuild(page: Page, scenario: BuildScenario): Promise<void> {
  const payload = createBuildPayload(scenario);
  await page.evaluate(({ payload, scenario }) => {
    const panel = (window as any).aiPanel;
    const plan = panel._normalizeBackendPlanResult({
      planId: 'plan-build-workspace',
      planHash: 'sha256:build-workspace',
      originalUserPrompt: '检测包装箱表面缺陷并输出 OK/NG',
      goal: '包装箱表面缺陷检测',
      requirementMode: 'strict',
      recommendedRoute: {
        routeId: 'route-build-workspace',
        title: '包装箱外观检测流程',
        summary: '采集、定位、二值化并输出结果。',
        operators: ['ImageAcquisition', 'RoiManager', 'Threshold', 'ResultOutput'],
      },
      semanticExtraction: {
        isVisionRequest: true,
        inspectionObject: '包装箱表面',
        taskType: 'surface_defect',
        imageSource: '工业相机',
        outputTarget: 'OK/NG',
        missingFields: [],
      },
      clarificationQuestions: [],
      acceptanceCriteria: ['缺陷样本输出 NG', '完整样本输出 OK'],
      canPlan: true,
      canBuild: true,
      buildReadiness: {
        canBuild: true,
        blockers: [],
        resolvedFields: ['inspection_object', 'task_type', 'image_source', 'output_target'],
        remainingFields: [],
        primaryMessage: '构建条件已满足。',
        contractVersion: 'v2',
      },
      metadataOnly: true,
    }, '检测包装箱表面缺陷并输出 OK/NG');
    panel.pendingVisionPlan = plan;
    panel.currentResultVersion += 1;
    panel.currentResult = payload;
    panel.activeAgentRunId = `run-${scenario}`;
    panel.activeAgentRunEvents = [{
      runId: `run-${scenario}`,
      sequence: 1,
      eventType: scenario === 'validation-failed' || scenario === 'dryrun-failed' ? 'run.failed' : 'run.completed',
      stage: scenario === 'validation-failed' ? 'validator' : scenario === 'dryrun-failed' ? 'dryrun' : 'run',
      status: scenario === 'validation-failed' || scenario === 'dryrun-failed' ? 'failed' : 'completed',
      summary: payload.aiExplanation,
      payload,
    }];
    panel.agentWorkspaceMode = scenario === 'applied' ? 'applied' : 'build';
    panel.workspaceViewMode = 'build';
    panel._displayResult(payload);
    panel._setWorkspaceViewMode('build', { persist: false, render: false });
    panel.workbenchState = scenario === 'applied'
      ? 'applied'
      : scenario === 'parameters' || scenario === 'mixed'
        ? 'reviewing_parameters'
        : scenario === 'validation-failed' || scenario === 'dryrun-failed'
          ? 'failed'
          : 'ready_to_apply';
    if (scenario === 'applied') {
      panel.appliedResultVersion = panel.currentResultVersion;
    }
    panel._renderAgentRuntime(payload);
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(plan);
    panel._renderBuildWorkspaceFromAgentRun();
    panel._renderWorkbenchStateBar();
    panel._updateApplyButtonState();
    panel._syncShellPresentation?.();
  }, { payload, scenario });
  await expect(page.locator('#ai-build-workspace')).toBeVisible();
  await expect(page.locator('[data-ai-hook="build-workspace-v3"]')).toBeVisible();
}

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => ({
    document: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    body: document.body.scrollWidth - document.body.clientWidth,
    ai: document.querySelector('#ai-view')!.scrollWidth - document.querySelector('#ai-view')!.clientWidth,
  }));
  expect(overflow.document).toBeLessThanOrEqual(1);
  expect(overflow.body).toBeLessThanOrEqual(1);
  expect(overflow.ai).toBeLessThanOrEqual(1);
}

async function focusBuildSection(page: Page, selector: string): Promise<void> {
  await page.evaluate(targetSelector => {
    const pane = document.querySelector('#ai-build-workspace') as HTMLElement | null;
    const target = document.querySelector(targetSelector) as HTMLElement | null;
    if (!pane || !target) return;
    const paneRect = pane.getBoundingClientRect();
    const targetRect = target.getBoundingClientRect();
    pane.scrollTop += targetRect.top - paneRect.top - 52;
  }, selector);
}

test('Build ready uses one primary action and keeps internal diagnostics collapsed', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'ready');

  await expect(page.locator('#ai-build-status-summary')).toContainText('可以应用');
  await expect(page.locator('#ai-build-status-summary')).toContainText('复核流程变更');
  await expect(page.locator('#ai-build-flow-summary')).toContainText('图像采集');
  await expect(page.locator('#ai-build-action-queue')).toContainText('当前没有需要处理的阻断');
  const applyMetric = page.locator('#ai-build-status-summary .ai-build-v2-metric').filter({ hasText: '应用状态' });
  await expect(applyMetric.locator('small')).toHaveText('已就绪');
  await expect(page.locator('#ai-btn-apply')).toBeEnabled();
  await expect(page.locator('#ai-btn-apply:visible')).toHaveCount(1);
  await expect(page.locator('[data-ai-hook="build-engineering-details"]')).not.toHaveAttribute('open', '');
  await expect(page.locator('#ai-agent-runtime')).not.toBeVisible();
});

test('parameter workspace preserves the real fill and confirmation interaction', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'parameters');

  const input = page.locator('[data-draft-input="true"]').first();
  const parameterMetric = page.locator('#ai-build-status-summary .ai-build-v2-metric').filter({ hasText: '待补参数' });
  const nextStep = page.locator('#ai-build-status-summary .ai-build-v2-next strong');
  await expect(parameterMetric.locator('dd')).toHaveText('1');
  await expect(page.locator('#ai-build-action-queue')).toContainText('1 项参数待填写');
  await expect(nextStep).toContainText('先补齐 1 项参数');
  await expect(input).toHaveAttribute('aria-describedby', /-status$/);
  await expect(input).toHaveAttribute('aria-invalid', 'true');
  await input.fill('146');
  await expect(parameterMetric.locator('dd')).toHaveText('0');
  await expect(parameterMetric.locator('small')).toContainText('已填写，等待确认');
  await expect(page.locator('#ai-build-action-queue')).toContainText('参数已填写，等待确认');
  await expect(nextStep).toContainText('请执行人工确认');
  await page.locator('#ai-btn-confirm-parameters').click();
  await expect(page.locator('.ai-parameter-field-status').first()).toContainText('已确认');
  await expect(parameterMetric.locator('dd')).toHaveText('0');
  await expect(parameterMetric.locator('small')).toContainText('已确认');
  await expect(page.locator('#ai-build-action-queue')).not.toContainText('参数已填写，等待确认');
  await expect(page.locator('#ai-build-action-queue')).toContainText('暂不可应用');
  await expect(nextStep).toContainText('查看应用条件');
  await expect(page.locator('#ai-build-apply-summary')).toContainText('暂不可应用');
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
});

test('resource workspace reuses the existing binding action and updates canonical result data', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'resources');

  const resourcePicker = page.locator('#ai-build-resources-section [data-resource-action="pick_model_resource"]');
  const resourceMetric = page.locator('#ai-build-status-summary .ai-build-v2-metric').filter({ hasText: '待补资源' });
  const nextStep = page.locator('#ai-build-status-summary .ai-build-v2-next strong');
  await expect(resourceMetric.locator('dd')).toHaveText('1');
  await expect(page.locator('#ai-build-action-queue')).toContainText('1 项资源待绑定');
  await expect(nextStep).toContainText('先绑定 1 项具体资源');
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
  await expect(resourcePicker).toBeVisible();
  await pickModelResource(page, resourcePicker, 'C:\\Models\\surface-v2.onnx');
  await expect(resourceMetric.locator('dd')).toHaveText('0');
  await expect(page.locator('#ai-build-action-queue')).not.toContainText('资源待绑定');
  await expect(page.locator('#ai-build-action-queue')).toContainText('当前没有需要处理的阻断');
  await expect(nextStep).toContainText('复核流程变更');
  await expect(page.locator('#ai-build-apply-summary')).toContainText('可应用到画布');
  await expect(page.locator('#ai-btn-apply')).toBeEnabled();
  const remaining = await page.evaluate(() => (window as any).aiPanel.currentResult.missingResources.length);
  expect(remaining).toBe(0);
});

test('mixed ordinary and resource parameters stay partitioned through confirmation and binding', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'mixed');

  const parameterSection = page.locator('#ai-build-parameters-section');
  const resourceSection = page.locator('#ai-build-resources-section');
  const thresholdInput = parameterSection.locator('[data-draft-input="true"][data-draft-parameter-name="Threshold"]');
  const modelParameterInput = parameterSection.locator('[data-draft-input="true"][data-draft-parameter-name="ModelPath"]');
  const modelResourcePicker = resourceSection.locator('[data-resource-action="pick_model_resource"]');
  const parameterMetric = page.locator('#ai-build-status-summary .ai-build-v2-metric').filter({ hasText: '待补参数' });
  const resourceMetric = page.locator('#ai-build-status-summary .ai-build-v2-metric').filter({ hasText: '待补资源' });
  const nextStep = page.locator('#ai-build-status-summary .ai-build-v2-next strong');

  await expect(parameterSection).toContainText('Threshold');
  await expect(parameterSection).not.toContainText('ModelPath');
  await expect(resourceSection).toContainText('ModelPath');
  await expect(resourceSection).not.toContainText('Threshold');
  await expect(thresholdInput).toHaveCount(1);
  await expect(modelParameterInput).toHaveCount(0);
  await expect(modelResourcePicker).toHaveCount(1);
  await expect(parameterMetric.locator('dd')).toHaveText('1');
  await expect(resourceMetric.locator('dd')).toHaveText('1');

  await thresholdInput.fill('146');
  await expect(parameterMetric.locator('dd')).toHaveText('0');
  await expect(parameterMetric.locator('small')).toContainText('已填写，等待确认');
  await expect(page.locator('#ai-btn-confirm-parameters')).toBeEnabled();
  await page.locator('#ai-btn-confirm-parameters').click();

  await expect(parameterMetric.locator('small')).toContainText('已确认');
  await expect(page.locator('#ai-build-action-queue')).not.toContainText('参数待填写');
  await expect(page.locator('#ai-build-action-queue')).toContainText('1 项资源待绑定');
  await expect(nextStep).toContainText('先绑定 1 项具体资源');

  await pickModelResource(page, modelResourcePicker, 'C:\\Models\\mixed-v1.onnx');

  await expect(resourceMetric.locator('dd')).toHaveText('0');
  await expect(page.locator('#ai-build-action-queue')).not.toContainText('资源待绑定');
  await expect(page.locator('#ai-build-action-queue')).toContainText('当前没有需要处理的阻断');
  await expect(nextStep).toContainText('复核流程变更');
  await expect(page.locator('#ai-build-apply-summary')).toContainText('可应用到画布');
  await expect(page.locator('#ai-btn-apply')).toBeEnabled();
  await expect(parameterSection.locator('[data-draft-input="true"]')).toHaveCount(1);
  await expect(resourceSection.locator('[data-resource-action="pick_model_resource"]')).toHaveCount(0);
});

test('Apply Preview remains the existing modal and can be reached by keyboard', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedBuild(page, 'ready');

  const apply = page.locator('#ai-btn-apply');
  await apply.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('.ai-apply-preview-overlay')).toBeVisible();
  const dialog = page.locator('.ai-apply-preview-dialog');
  await expect(dialog).toContainText('应用预览');
  await expect(dialog).toHaveAttribute('role', 'dialog');
  await expect(dialog).toHaveAttribute('aria-modal', 'true');
  await expect(page.locator('.ai-shell')).toHaveAttribute('inert', '');
  await expect(page.locator('.ai-shell')).toHaveAttribute('aria-hidden', 'true');
  await expect(page.locator('.ai-apply-preview-confirm')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.locator('.ai-apply-preview-close')).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(page.locator('.ai-apply-preview-confirm')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.locator('.ai-apply-preview-overlay')).toHaveCount(0);
  await expect(page.locator('.ai-shell')).not.toHaveAttribute('inert', '');
  await expect(page.locator('.ai-shell')).not.toHaveAttribute('aria-hidden', 'true');
  await expect(apply).toBeFocused();
});

test('keyboard tabs switch conversation and Plan Build workspaces without pointer input', async ({ page }) => {
  await openAi(page, { width: 1024, height: 768, theme: 'dark' });
  await seedBuild(page, 'ready');

  const workbenchTab = page.locator('[data-ai-shell-pane="workbench"]');
  const conversationTab = page.locator('[data-ai-shell-pane="conversation"]');
  await workbenchTab.focus();
  await page.keyboard.press('ArrowRight');
  await expect(conversationTab).toHaveAttribute('aria-selected', 'true');
  await expect(conversationTab).toBeFocused();
  await page.keyboard.press('ArrowLeft');
  await expect(workbenchTab).toHaveAttribute('aria-selected', 'true');

  const planTab = page.locator('[data-workspace-view-mode="plan"]');
  const buildTab = page.locator('[data-workspace-view-mode="build"]');
  await planTab.focus();
  await page.keyboard.press('ArrowRight');
  await expect(buildTab).toHaveAttribute('aria-selected', 'true');
  await expect(buildTab).toBeFocused();
});

test('history session rows expose native keyboard selection and delete actions', async ({ page }) => {
  await openAi(page, { width: 1024, height: 768, theme: 'dark' });
  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.__keyboardSelectedSession = '';
    panel._switchToSession = (sessionId: string) => { panel.__keyboardSelectedSession = sessionId; };
    panel.history = [{
      sessionId: 'history-keyboard',
      lastMessage: '键盘恢复测试',
      updatedAtUtc: '2026-07-12T00:00:00Z',
      turnCount: 2,
      applied: false,
    }];
    panel.filteredHistory = [...panel.history];
    panel.isHistoryPanelOpen = true;
    panel.container.querySelector('#ai-history-panel')?.classList.add('expanded');
    panel._renderHistoryList();
  });

  const select = page.locator('.ai-history-select');
  const remove = page.locator('.ai-history-delete');
  await select.focus();
  await page.keyboard.press('Enter');
  await expect.poll(() => page.evaluate(() => (window as any).aiPanel.__keyboardSelectedSession)).toBe('history-keyboard');
  await select.focus();
  await page.keyboard.press('Tab');
  await expect(remove).toBeFocused();
  await expect(remove).toHaveAttribute('aria-label', /键盘恢复测试/);
});

test('Apply dialog remains usable with reduced motion and 200 percent zoom', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedBuild(page, 'ready');
  await page.evaluate(() => { document.documentElement.style.zoom = '2'; });
  const apply = page.locator('#ai-btn-apply');
  await apply.focus();
  await page.keyboard.press('Enter');
  const dialog = page.locator('.ai-apply-preview-dialog');
  await expect(dialog).toBeVisible();
  await expect(page.locator('.ai-apply-preview-confirm')).toBeFocused();
  await expect(page.locator('.ai-apply-preview-overlay')).toHaveCSS('animation-name', 'none');
  await page.keyboard.press('Escape');
  await expect(apply).toBeFocused();
});

test('Apply Preview expires immediately when the authoritative gate changes', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedBuild(page, 'ready');
  await page.locator('#ai-btn-apply').click();
  await expect(page.locator('.ai-apply-preview-dialog')).toBeVisible();
  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.currentResult.applyGate.blocked = true;
    panel.currentResult.applyGate.canvasApplyReady = false;
    panel.currentResult.buildResult.applyGate.blocked = true;
    panel.currentResult.buildResult.applyGate.canvasApplyReady = false;
  });
  await page.locator('.ai-apply-preview-confirm').click();
  await expect(page.locator('.ai-apply-preview-overlay')).toHaveCount(0);
  await expect(page.locator('#ai-result-status-note')).toContainText('应用条件已变化');
  const state = await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    return { workbenchState: panel.workbenchState, applied: panel.appliedResultVersion === panel.currentResultVersion };
  });
  expect(state).toEqual({ workbenchState: 'failed', applied: false });
});

test('DryRun failure stays distinct from static validation and keeps Apply blocked', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedBuild(page, 'dryrun-failed');
  await focusBuildSection(page, '#ai-build-validation-section');

  const structuralCheck = page.locator('#ai-build-validation-summary .ai-build-v2-check').filter({ hasText: '静态与拓扑检查' });
  const dryRunCheck = page.locator('#ai-build-validation-summary .ai-build-v2-check').filter({ hasText: '元数据预演' });
  await expect(structuralCheck).toContainText('已通过');
  await expect(dryRunCheck).toContainText('未通过');
  await expect(page.locator('#ai-result-validation')).toContainText('未产生有效判定结果');
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
});

test('Build ready light visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'ready');
  await expect(page.locator('#ai-view')).toHaveScreenshot('build-ready-light-1366.png');
});

test('Build ready dark visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedBuild(page, 'ready');
  await expect(page.locator('#ai-view')).toHaveScreenshot('build-ready-dark-1366.png');
});

test('parameter blocker visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'parameters');
  await focusBuildSection(page, '#ai-build-parameters-section');
  await expect(page.locator('#ai-view')).toHaveScreenshot('build-parameter-blocked-light-1366.png');
});

test('resource blocker visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'resources');
  await focusBuildSection(page, '#ai-build-resources-section');
  await expect(page.locator('#ai-view')).toHaveScreenshot('build-resource-blocked-light-1366.png');
});

test('validation failure visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedBuild(page, 'validation-failed');
  await focusBuildSection(page, '#ai-build-validation-section');
  await expect(page.locator('#ai-view')).toHaveScreenshot('build-validation-failed-dark-1366.png');
});

test('Apply-ready preview visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'ready');
  await focusBuildSection(page, '#ai-build-apply-section');
  await expect(page.locator('#ai-view')).toHaveScreenshot('build-apply-ready-light-1366.png');
});

test('Applied remains inside Build and preserves the engineering history', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'applied');
  await expect(page.locator('#ai-build-status-summary')).toContainText('已应用');
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
  await expect(page.locator('#ai-build-operator-chain')).toContainText('图像采集');
});

test('Applied engineering history visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedBuild(page, 'applied');
  await expect(page.locator('#ai-view')).toHaveScreenshot('build-applied-light-1366.png');
});

for (const viewport of [
  { name: 'compact', width: 1024, height: 768 },
  { name: 'narrow', width: 390, height: 844 },
]) {
  test(`Build remains reachable without horizontal overflow at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await openAi(page, { width: viewport.width, height: viewport.height, theme: 'dark' });
    await seedBuild(page, 'resources');
    await expectNoHorizontalOverflow(page);
    await expect(page.locator('#ai-build-resources-section [data-resource-action="pick_model_resource"]')).toBeVisible();
    await expect(page.locator('#ai-btn-apply')).toBeVisible();
  });

  test(`Build ${viewport.name} visual baseline at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await openAi(page, { width: viewport.width, height: viewport.height, theme: 'dark' });
    await seedBuild(page, 'resources');
    await expect(page.locator('#ai-view')).toHaveScreenshot(`build-${viewport.name}-dark-${viewport.width}.png`);
  });
}
