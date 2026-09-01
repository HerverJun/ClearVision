import { test, expect, Page } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { bootAuthenticatedApp } from './authHelper';

const applyEvidenceDir = path.resolve(process.cwd(), 'test-results', 'agent-apply-visible');
const workbenchEvidenceDir = path.resolve(process.cwd(), 'test-results', 'agent-workbench-default');

async function expectCtaHitTarget(page: Page, selector: string): Promise<void> {
  const locator = page.locator(selector);
  await expect(locator).toBeVisible();
  await expect(locator).toBeEnabled();
  await locator.scrollIntoViewIfNeeded();
  const state = await locator.evaluate(element => {
    const rect = element.getBoundingClientRect();
    const x = rect.left + rect.width / 2;
    const y = rect.top + rect.height / 2;
    const hit = document.elementFromPoint(x, y);
    const style = window.getComputedStyle(element);
    return {
      disabled: (element as HTMLButtonElement).disabled,
      pointerEvents: style.pointerEvents,
      hitSelf: hit === element || element.contains(hit),
      hitTag: hit?.tagName || '',
      hitText: hit?.textContent || '',
    };
  });
  expect(state.disabled).toBe(false);
  expect(state.pointerEvents).toBe('auto');
  expect(state.hitSelf, `${selector} center hit ${state.hitTag}: ${state.hitText}`).toBe(true);
}

const applyReadyFlow = {
  operators: [
    {
      id: 'op_image',
      name: '图像采集',
      type: 'ImageAcquisition',
      x: 80,
      y: 96,
      inputPorts: [],
      outputPorts: [{ id: 'op_image_out', name: 'Image', dataType: 'Image' }],
      parameters: [{ name: 'Source', displayName: '图像源', value: 'sample-image' }],
      isEnabled: true,
    },
    {
      id: 'op_roi',
      name: 'ROI管理器',
      type: 'RoiManager',
      x: 360,
      y: 96,
      inputPorts: [{ id: 'op_roi_in', name: 'Image', dataType: 'Image', isRequired: true }],
      outputPorts: [{ id: 'op_roi_out', name: 'Image', dataType: 'Image' }],
      parameters: [{ name: 'Region', displayName: 'ROI区域', value: 'full-frame' }],
      isEnabled: true,
    },
    {
      id: 'op_threshold',
      name: '二值化',
      type: 'Threshold',
      x: 640,
      y: 96,
      inputPorts: [{ id: 'op_threshold_in', name: 'Image', dataType: 'Image', isRequired: true }],
      outputPorts: [{ id: 'op_threshold_out', name: 'BinaryImage', dataType: 'Image' }],
      parameters: [{ name: 'Threshold', displayName: '阈值', value: 128 }],
      isEnabled: true,
    },
  ],
  connections: [
    {
      id: 'conn_image_roi',
      sourceOperatorId: 'op_image',
      sourcePortId: 'op_image_out',
      targetOperatorId: 'op_roi',
      targetPortId: 'op_roi_in',
    },
    {
      id: 'conn_roi_threshold',
      sourceOperatorId: 'op_roi',
      sourcePortId: 'op_roi_out',
      targetOperatorId: 'op_threshold',
      targetPortId: 'op_threshold_in',
    },
  ],
};

function createApplyReadyPlanResult() {
  return {
    planId: 'plan_apply_visible',
    planHash: 'sha256:apply-visible',
    originalUserPrompt: '检测产品表面划痕并输出二值化结果',
    goal: '检测产品表面划痕并输出二值化结果',
    intent: 'surface_defect',
    confidence: 'high',
    requirementMode: 'strict',
    planSource: 'model_router',
    requirementUnderstanding: ['目标是表面缺陷检测。', '需要图像采集、ROI管理器和二值化处理。'],
    recommendedRoute: {
      routeId: 'surface_defect_apply_visible',
      title: '表面缺陷检测路线',
      summary: '采集图像后限定 ROI，再进行二值化。',
      operators: ['ImageAcquisition', 'RoiManager', 'Threshold'],
    },
    clarificationQuestions: [],
    recommendedDefaults: [],
    risks: [],
    acceptanceCriteria: ['画布包含图像采集、ROI管理器、二值化节点。'],
    executablePlan: ['按确认计划构建可应用流程草稿。'],
    canPlan: true,
    canBuild: true,
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: '构建条件已满足。',
      contractVersion: 'v2',
    },
    requirementMaturity: {
      maturity: 'actionable',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: true,
      objectSignals: ['产品表面'],
      taskSignals: ['划痕', '二值化'],
      missingFields: [],
      blockingReasons: [],
      publicReason: '需求足够明确，可以进入构建。',
    },
    semanticExtraction: {
      isVisionRequest: true,
      source: 'model_router',
      taskType: 'surface_defect',
      confidence: 0.92,
      taskTypeConfidence: 0.92,
      inspectionObject: '产品表面',
      defectType: '划痕',
      imageSource: 'sample-image',
      okCondition: '无明显划痕',
      ngCondition: '存在划痕',
      outputTarget: '二值化结果',
      missingFields: [],
    },
    publicEvents: [],
    metadataOnly: true,
  };
}

function createApplyReadyBuildPayload() {
  return {
    success: true,
    status: 'completed',
    completionStatus: 'completed',
    interactionState: 'completed',
    planId: 'plan_apply_visible',
    planHash: 'sha256:apply-visible',
    buildFromPlan: {
      planId: 'plan_apply_visible',
      planHash: 'sha256:apply-visible',
      metadataOnly: true,
    },
    requirementMode: 'strict',
    aiExplanation: '构建完成，可应用到画布。',
    flow: applyReadyFlow,
    buildResult: {
      buildId: 'build_apply_visible',
      buildIntent: 'new',
      workflowDraft: {
        operatorCount: applyReadyFlow.operators.length,
        connectionCount: applyReadyFlow.connections.length,
      },
      operatorPipeline: applyReadyFlow.operators.map(operator => ({
        tempId: operator.id,
        operatorType: operator.type,
        displayName: operator.name,
        status: 'completed',
        source: 'plan',
      })),
      parameterMapping: [
        { tempId: 'op_threshold', operatorType: 'Threshold', parameterName: 'Threshold', valueSummary: '128', source: 'default' },
      ],
      applyGate: {
        canvasApplyReady: true,
        runtimeDraftReady: true,
        deploymentReady: true,
        blocked: false,
        status: 'ready',
        metadataOnly: true,
      },
      metadataOnly: true,
    },
    applyGate: {
      canvasApplyReady: true,
      runtimeDraftReady: true,
      deploymentReady: true,
      blocked: false,
      status: 'ready',
      metadataOnly: true,
    },
    pendingParameters: [],
    missingResources: [],
    metadataOnly: true,
  };
}

async function mockAgentPlanAndApplyReadyBuild(page: Page): Promise<void> {
  const planResult = createApplyReadyPlanResult();
  await page.route('**/api/ai/agent-intent-router-runs', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        intent: 'actionable_vision_plan',
        confidence: 'high',
        shouldOpenPlan: true,
        shouldBuildDirectly: false,
        canBuild: true,
        needsClarification: false,
        publicReason: '需求已明确，先进入 Plan。',
        assistantReply: '已生成工程计划，请确认后开始构建。',
        semanticExtraction: planResult.semanticExtraction,
        metadataOnly: true,
      }),
    });
  });
  await page.route('**/api/ai/agent-plan/readiness-preview', async route => {
    const request = route.request().postDataJSON();
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        planId: request.planId,
        planHash: request.planHash,
        requirementMode: request.requirementMode || 'strict',
        answerRevision: request.answerRevision || 0,
        resourceRevision: request.resourceRevision || 0,
        buildReadiness: {
          canBuild: true,
          blockers: [],
          resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
          remainingFields: [],
          primaryMessage: '构建条件已满足。',
          contractVersion: 'v2',
        },
        pendingConfirmationCount: 0,
        resourcePendingCount: 0,
        hardBlockerCount: 0,
        buildBlockingConfirmationCount: 0,
        buildRequiredResourceCount: 0,
        deferredFieldCount: 0,
        draftAllowedResourceCount: 0,
        mustConfirmBeforeBuildCount: 0,
        fillLaterCount: 0,
        totalIncompleteCount: 0,
        contractValid: true,
        metadataOnly: true,
      }),
    });
  });
  await page.route('**/api/ai/agent-plan-runs', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        runId: 'plan_apply_visible_run',
        events: [
          {
            runId: 'plan_apply_visible_run',
            sequence: 1,
            eventType: 'run.started',
            stage: 'run',
            status: 'running',
            payload: { metadataOnly: true },
          },
        ],
      }),
    });
  });
  await page.route('**/api/ai/agent-runs**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === 'GET' && url.pathname.endsWith('/agent-runs/plan_apply_visible_run/events')) {
      await route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        body: [
          'event: run.completed',
          `data: ${JSON.stringify({
            runId: 'plan_apply_visible_run',
            sequence: 2,
            eventType: 'run.completed',
            stage: 'run',
            status: 'completed',
            payload: { planResult, metadataOnly: true },
          })}`,
          '',
          '',
        ].join('\n'),
      });
      return;
    }

    if (request.method() === 'POST' && url.pathname.endsWith('/api/ai/agent-runs')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          runId: 'build_apply_visible_run',
          brief: 'Build started',
          events: [
            {
              runId: 'build_apply_visible_run',
              sequence: 1,
              eventType: 'run.started',
              stage: 'run',
              status: 'running',
              payload: { metadataOnly: true },
            },
          ],
        }),
      });
      return;
    }

    if (request.method() === 'GET' && url.pathname.endsWith('/agent-runs/build_apply_visible_run/events')) {
      await route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        body: [
          'event: run.completed',
          `data: ${JSON.stringify({
            runId: 'build_apply_visible_run',
            sequence: 2,
            eventType: 'run.completed',
            stage: 'run',
            status: 'completed',
            summary: 'Build completed.',
            payload: createApplyReadyBuildPayload(),
          })}`,
          '',
          '',
        ].join('\n'),
      });
      return;
    }

    await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ message: 'unexpected agent run route' }) });
  });
}

async function collectApplyButtonState(page: Page) {
  return await page.evaluate(() => {
    const rectOf = (element: Element | null) => {
      if (!element) return null;
      const rect = element.getBoundingClientRect();
      return {
        x: Math.round(rect.x),
        y: Math.round(rect.y),
        width: Math.round(rect.width),
        height: Math.round(rect.height),
        top: Math.round(rect.top),
        right: Math.round(rect.right),
        bottom: Math.round(rect.bottom),
        left: Math.round(rect.left),
      };
    };
    const describe = (selector: string) => {
      const element = document.querySelector(selector) as HTMLElement | null;
      const style = element ? window.getComputedStyle(element) : null;
      return {
        selector,
        exists: Boolean(element),
        hidden: element?.hidden ?? null,
        display: style?.display ?? null,
        visibility: style?.visibility ?? null,
        height: style?.height ?? null,
        overflow: style ? `${style.overflow}/${style.overflowY}` : null,
        className: element?.className?.toString?.() ?? '',
        rect: rectOf(element),
      };
    };
    const button = document.querySelector('#ai-btn-apply') as HTMLButtonElement | null;
    const panel = (window as any).aiPanel;
    const gate = panel?._getPayloadApplyGate?.(panel.currentResult) ||
      panel?.currentResult?.applyGate ||
      panel?.currentResult?.ApplyGate ||
      panel?.currentResult?.buildResult?.applyGate ||
      null;
    const parentVisibilityChain = [];
    let current: HTMLElement | null = button;
    while (current) {
      const style = window.getComputedStyle(current);
      parentVisibilityChain.push({
        tag: current.tagName.toLowerCase(),
        id: current.id || '',
        className: current.className?.toString?.() || '',
        hidden: current.hidden,
        display: style.display,
        visibility: style.visibility,
        height: style.height,
        overflow: `${style.overflow}/${style.overflowY}`,
        rect: rectOf(current),
      });
      current = current.parentElement;
    }

    const appliedNodeNames = Array.from((window as any).flowCanvas?.nodes?.values?.() || [])
      .map((node: any) => node.title || node.displayName || node.name || node.type || '')
      .filter(Boolean);
    return {
      workbenchState: panel?.workbenchState || null,
      workspaceViewMode: panel?._getWorkspaceViewMode?.() || panel?.workspaceViewMode || null,
      agentWorkspaceMode: panel?.agentWorkspaceMode || null,
      canvasApplyReady: panel?._isCanvasApplyReadyForResult?.(panel.currentResult) ?? gate?.canvasApplyReady ?? null,
      disabled: button?.disabled ?? null,
      ariaDisabled: button?.getAttribute('aria-disabled') ?? null,
      text: button?.textContent?.replace(/\s+/g, ' ').trim() || '',
      className: button?.className || '',
      rect: rectOf(button),
      tracked: {
        applyContainer: describe('.apply-container'),
        buildWorkspace: describe('#ai-build-workspace'),
        resultPane: describe('#ai-result-pane'),
        overview: describe('#ai-agent-workspace-overview'),
        planWorkspace: describe('#ai-plan-workspace'),
      },
      parentVisibilityChain,
      appliedNodeNames,
    };
  });
}

async function writeApplyEvidence(name: string, payload: unknown): Promise<void> {
  await mkdir(applyEvidenceDir, { recursive: true });
  await writeFile(path.join(applyEvidenceDir, name), JSON.stringify(payload, null, 2), 'utf8');
}

async function mockShellApis(page: Page): Promise<void> {
  await page.route('**/api/settings', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        general: {
          softwareTitle: 'ClearVision',
          theme: 'dark',
          autoStart: false,
        },
        runtime: {
          autoRun: false,
          stopOnConsecutiveNg: 2,
          missingMaterialTimeoutSeconds: 15,
          applyProtectionRules: true,
        },
      }),
    });
  });
  await page.route('**/api/operators/types', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });
  await page.route('**/api/operators/library', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });
  await page.route('**/api/projects**', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
  });
  await page.route('**/api/health', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ok: true }) });
  });
  await page.route('**/api/ai/vision-agent/planning-deadline', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        contractVersion: 'v1',
        totalBudgetMs: 120000,
        clientNetworkMarginMs: 15000,
        minimumRepairBudgetMs: 5000,
        metadataOnly: true,
      }),
    });
  });
  await page.route('**/api/inspection/decision-configuration/validate', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isValid: true, issues: [], eligibleOutputs: [] }),
    });
  });
}

async function installFakeWebView2(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const listeners: Record<string, Array<(event: { data: unknown }) => void>> = {};
    (window as any).__cvWebViewMessages = [];
    (window as any).__dispatchCvWebViewMessage = (message: unknown) => {
      for (const handler of listeners.message || []) {
        handler({ data: message });
      }
    };
    (window as any).chrome = {
      webview: {
        addEventListener(type: string, handler: (event: { data: unknown }) => void) {
          listeners[type] = listeners[type] || [];
          listeners[type].push(handler);
        },
        postMessage(message: unknown) {
          (window as any).__cvWebViewMessages.push(message);
        },
      },
    };
  });
}

async function mockAgentRunBuild(page: Page, runId: string, payloads: any[]): Promise<void> {
  await page.route('**/api/ai/agent-runs**', async route => {
    const request = route.request();
    if (request.method() === 'POST' && request.url().endsWith('/api/ai/agent-runs')) {
      payloads.push(request.postDataJSON());
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          runId,
          brief: 'Build started',
          events: [
            {
              runId,
              sequence: 1,
              eventType: 'run.started',
              stage: 'run',
              status: 'running',
              payload: { metadataOnly: true },
            },
          ],
        }),
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'text/event-stream',
      body: [
        'event: run.started',
        `data: {"runId":"${runId}","sequence":2,"eventType":"run.started","stage":"run","status":"running","payload":{"metadataOnly":true}}`,
        '',
        '',
      ].join('\n'),
    });
  });
}

async function seedLegacyWebMessageGenerateRequest(page: Page, userMessage: string): Promise<string> {
  return await page.evaluate(message => {
    const panel = (window as any).aiPanel;
    const requestId = panel._createGenerateRequestId?.() ?? `legacy-${Date.now()}`;
    panel.isVisionAgentDeveloperUiEnabled = true;
    panel.lastUserPrompt = message;
    panel.activeGenerateRequestId = requestId;
    panel.activeGenerateSessionId = panel.sessionId;
    panel.isCancellingGenerate = false;
    panel.pendingManualRetry = null;
    panel.pendingClarificationPayload = null;
    panel.agentWorkspaceMode = 'build';
    panel._setGeneratingState?.(true);
    panel._setWorkbenchState?.('generating');
    panel._renderAgentWorkspaceOverview?.();
    panel._renderPlanWorkspace?.(panel.pendingVisionPlan);
    panel._renderBuildWorkspaceFromAgentRun?.();
    panel._renderAgentRuntime?.({
      turnIntent: 'new_flow',
      interactionState: 'generating',
      routerConfidence: '',
      blockingClarificationFields: [],
      nonBlockingMissingFields: [],
    });
    panel._startAssistantTurn?.();
    return requestId;
  }, userMessage);
}

test('AI agent clarification terminal does not render legacy card on narrow viewports', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('#ai-plan-workspace')));

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.activeGenerateRequestId = 'responsive-clarify';
    panel.isGenerating = true;
    panel._handleResult({
      requestId: 'responsive-clarify',
      success: false,
      status: 'clarification_required',
      failureType: 'clarification_required',
      clarificationRequired: true,
      turnIntent: 'new_flow',
      interactionState: 'clarifying',
      aiExplanation: '需要补充关键信息。',
      requirementBrief: {
        requirementMode: 'strict',
        confidence: 0,
        clarificationRequired: true,
        draftRiskLevel: 'high',
        missingFacts: ['需要确认具体场景'],
        blockingClarificationFields: ['scene'],
        nonBlockingMissingFields: [],
        clarificationQuestions: [
          { field: 'scene', question: '确认视觉场景。', required: true, options: ['外观缺陷'] },
        ],
      },
    });
  });

  await expect(page.locator('#ai-clarification-plan-card')).toHaveCount(0);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toHaveCount(0);
  await expect(page.locator('#ai-plan-workspace')).not.toContainText('ClarificationPlanCard');
  const metrics = await page.evaluate(() => ({
    viewportWidth: window.innerWidth,
    documentWidth: document.documentElement.scrollWidth,
    bodyWidth: document.body.scrollWidth,
    pendingClarificationPayload: (window as any).aiPanel.pendingClarificationPayload,
  }));

  expect(metrics.pendingClarificationPayload).toBeNull();
  expect(metrics.documentWidth).toBeLessThanOrEqual(metrics.viewportWidth);
  expect(metrics.bodyWidth).toBeLessThanOrEqual(metrics.viewportWidth);
});

test('AI panel obeys ambiguous Router shouldOpenPlan=false without legacy card', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await mockShellApis(page);
  await page.route('**/ai/agent-intent-router-runs', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        intent: 'ambiguous_vision_requirement',
        confidence: 'medium',
        shouldOpenPlan: false,
        shouldBuildDirectly: false,
        canBuild: false,
        needsClarification: true,
        publicReason: 'Need more details.',
        assistantReply: '请先确认包装盒检测的具体目标。',
        clarificationQuestions: [{ field: 'scene', question: 'legacy question must be ignored' }],
        fallbackAllowed: true,
        routerSource: 'model_router',
        metadataOnly: true,
      }),
    });
  });
  await page.route('**/ai/vision-agent-plans', async route => {
    throw new Error('ambiguous shouldOpenPlan=false must not request planner');
  });
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('#ai-plan-workspace')));

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel._dispatchGenerateRequest({
      description: '包装盒检测',
      userMessage: '包装盒检测',
    });
  });

  await expect(page.locator('#ai-clarification-plan-card')).toHaveCount(0);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toHaveCount(0);
  await expect(page.locator('#ai-clarification-plan-card')).toHaveCount(0);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toHaveCount(0);
  await expect(page.locator('.ai-assistant-clarification-section').last()).toBeHidden();
  expect(await page.evaluate(() => (window as any).aiPanel.pendingClarificationPayload)).toBeNull();
  await expect(page.locator('#ai-plan-workspace .ai-plan-v2-empty')).toBeVisible();
  const state = await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    return {
      mode: panel.agentWorkspaceMode,
      hasPlan: Boolean(panel.pendingVisionPlan),
      pendingClarificationPayload: panel.pendingClarificationPayload,
    };
  });
  expect(state).toEqual({
    mode: 'plan',
    hasPlan: false,
    pendingClarificationPayload: null,
  });
});

test('AI assistant diagnostic text stays collapsed by default', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel));

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel._dispatchAgentWorkspaceEvent({
      type: 'workspace/intent-resolved',
      payload: { intent: { description: '生成诊断可见性测试' } },
    });
    panel.activeGenerateRequestId = 'diagnostic-default';
    panel.isGenerating = true;
    panel._startAssistantTurn();
    panel._handleResult({
      requestId: 'diagnostic-default',
      success: true,
      status: 'completed',
      turnIntent: 'new_flow',
      interactionState: 'completed',
      routerConfidence: 'high',
      aiExplanation: '已生成可应用流程。',
      publicDiagnostics: ['Raw model diagnostic trace that should not be expanded by default.'],
      flow: {
        operators: [],
        connections: [],
      },
    });
  });

  const diagnostic = page.locator('.ai-assistant-reasoning-section').last();
  await expect(diagnostic).toBeVisible();
  await expect(diagnostic.locator('summary')).toHaveText('生成诊断');
  await expect(diagnostic).toContainText('Raw model diagnostic trace');
  await expect(diagnostic).not.toHaveAttribute('open', '');
});

test('AI page suppresses routine toast noise and keeps critical toast compact', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('#ai-agent-runtime')));

  await page.evaluate(() => {
    const container = document.createElement('div');
    container.id = 'cv-toast-container';
    container.className = 'cv-toast-container';
    container.innerHTML = `
      <div class="cv-toast cv-toast-success"><span class="cv-toast-message">ClearVision 已就绪</span></div>
      <div class="cv-toast cv-toast-info"><span class="cv-toast-message">当前状态已同步</span></div>
      <div class="cv-toast cv-toast-warning"><span class="cv-toast-message">AI 配置需要复核</span></div>
    `;
    document.body.appendChild(container);
  });

  await expect(page.locator('.cv-toast-success').last()).toBeHidden();
  await expect(page.locator('.cv-toast-info').last()).toBeHidden();
  await expect(page.locator('.cv-toast-warning').last()).toBeVisible();

  const toastLayout = await page.evaluate(() => {
    const warning = document.querySelector('.cv-toast-warning');
    const box = warning?.getBoundingClientRect();
    return {
      viewportWidth: window.innerWidth,
      documentWidth: document.documentElement.scrollWidth,
      bodyWidth: document.body.scrollWidth,
      warning: box
        ? {
          x: Math.round(box.x),
          y: Math.round(box.y),
          width: Math.round(box.width),
          height: Math.round(box.height),
          right: Math.round(box.right),
          bottom: Math.round(box.bottom),
        }
        : null,
    };
  });

  expect(toastLayout.documentWidth).toBeLessThanOrEqual(toastLayout.viewportWidth);
  expect(toastLayout.bodyWidth).toBeLessThanOrEqual(toastLayout.viewportWidth);
  expect(toastLayout.warning?.width).toBeLessThanOrEqual(220);
  expect(toastLayout.warning?.right).toBeLessThanOrEqual(toastLayout.viewportWidth);
});

test('AI panel routes WebView GenerateFlowResult into agent clarification state', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && (window as any).mockWebViewResponse));

  const requestId = await seedLegacyWebMessageGenerateRequest(page, '帮我构建一个流程');
  await expect(page.locator('#ai-agent-runtime')).toContainText('生成中');
  expect(requestId).toBeTruthy();

  await page.evaluate(id => {
    (window as any).mockWebViewResponse({
      type: 'GenerateFlowResult',
      payload: {
        requestId: id,
        success: false,
        status: 'clarification_required',
        failureType: 'clarification_required',
        clarificationRequired: true,
        turnIntent: 'new_flow',
        interactionState: 'clarifying',
        routerConfidence: 'high',
        aiExplanation: '当前需求还需要澄清 2 项关键信息。',
        blockingClarificationFields: ['scene', 'object_type'],
        nonBlockingMissingFields: ['model_path'],
        requirementBrief: {
          requirementMode: 'strict',
          confidence: 0,
          clarificationRequired: true,
          draftRiskLevel: 'high',
          missingFacts: ['需要确认具体场景', '需要确认检测对象'],
          blockingClarificationFields: ['scene', 'object_type'],
          nonBlockingMissingFields: ['model_path'],
          clarificationQuestions: [
            {
              field: 'scene',
              question: '请确认这是外观缺陷、漏装有无、线序判定还是尺寸测量场景。',
              required: true,
              priority: 'high',
              reason: '场景未明确时无法安全生成流程。',
              options: ['外观缺陷', '漏装有无', '线序判定', '尺寸测量'],
            },
            {
              field: 'object_type',
              question: '请补充检测对象是什么。',
              required: true,
              priority: 'high',
              reason: '需要明确对象才能选择正确模板与算子。',
              options: ['产品', '包装箱/纸箱', '金属件'],
            },
          ],
        },
      },
    });
  }, requestId);

  await expect(page.locator('#ai-clarification-plan-card')).toHaveCount(0);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toHaveCount(0);
  await expect(page.locator('.ai-assistant-clarification-section').last()).toBeHidden();
  expect(await page.evaluate(() => (window as any).aiPanel.pendingClarificationPayload)).toBeNull();
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
  await expect(page.locator('#ai-btn-apply')).toContainText('暂无可应用方案');
});

test('AI agent runtime infers terminal states without interactionState', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('#ai-agent-runtime')));

  const runTerminal = async (payload: Record<string, unknown>) =>
    page.evaluate(data => {
      const panel = (window as any).aiPanel;
      panel.activeGenerateRequestId = data.requestId;
      panel.isGenerating = true;
      panel._handleResult(data);
      const runtime = document.querySelector('#ai-agent-runtime') as HTMLElement | null;
      return {
        text: runtime?.innerText || '',
        className: runtime?.className || '',
      };
    }, payload);

  const cancelled = await runTerminal({
    requestId: 'terminal-cancelled',
    success: false,
    status: 'cancelled',
    failureType: 'user_cancelled',
    errorMessage: '已取消',
  });
  expect(cancelled.text).toContain('已取消');
  expect(cancelled.text).not.toContain('生成中');
  expect(cancelled.className).toContain('is-cancelled');

  const timedOut = await runTerminal({
    requestId: 'terminal-timeout',
    success: false,
    status: 'timed_out',
    failureType: 'timeout',
    errorMessage: '请求超时',
  });
  expect(timedOut.text).toContain('请求超时');
  expect(timedOut.text).not.toContain('生成中');
  expect(timedOut.className).toContain('is-timed_out');

  const failed = await runTerminal({
    requestId: 'terminal-failed',
    success: false,
    status: 'failed',
    failureType: 'system_error',
    errorMessage: '系统错误',
  });
  expect(failed.text).toContain('失败');
  expect(failed.text).not.toContain('生成中');
  expect(failed.className).toContain('is-failed');
});

test('AI panel ignores Vision Agent clarification terminal legacy card path', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && (window as any).mockWebViewResponse));

  const requestId = await seedLegacyWebMessageGenerateRequest(page, '帮我做一个没有模板的检测');
  expect(requestId).toBeTruthy();

  await page.evaluate(id => {
    (window as any).mockWebViewResponse({
      type: 'GenerateFlowResult',
      payload: {
        requestId: id,
        success: false,
        status: 'clarification_required',
        failureType: 'clarification_required',
        clarificationRequired: true,
        turnIntent: 'new_flow',
        interactionState: 'clarifying',
        aiExplanation: '需要补充关键信息。',
        requirementBrief: {
          requirementMode: 'strict',
          confidence: 0,
          clarificationRequired: true,
          draftRiskLevel: 'high',
          knownFacts: [],
          missingFacts: ['需要确认检测目标'],
          blockingClarificationFields: ['inspection_object'],
          nonBlockingMissingFields: [],
          clarificationQuestions: [
            {
              field: 'inspection_object',
              question: '检测目标是什么？',
              required: true,
              options: ['零件', '包装盒'],
            },
          ],
        },
      },
    });
  }, requestId);

  await expect(page.locator('#ai-clarification-plan-card')).toHaveCount(0);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toHaveCount(0);
  await expect(page.locator('#ai-plan-workspace')).not.toContainText('ClarificationPlanCard');
  const state = await page.evaluate(() => ({
    pendingClarificationPayload: (window as any).aiPanel.pendingClarificationPayload,
    hasPlan: Boolean((window as any).aiPanel.pendingVisionPlan),
  }));
  expect(state.pendingClarificationPayload).toBeNull();
  expect(state.hasPlan).toBe(false);
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
});

test('AI panel keeps old requirement brief as read-only evidence without a second answer button', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('#ai-result-requirement-brief')));

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.activeGenerateRequestId = 'old-flow';
    panel.isGenerating = true;
    panel._handleResult({
      requestId: 'old-flow',
      success: true,
      status: 'completed',
      turnIntent: 'new_flow',
      interactionState: 'completed',
      routerConfidence: 'high',
      aiExplanation: '旧方案已生成。',
      flow: {
        operators: [{ id: 'op-old', type: 'ImageAcquisition', displayName: 'ImageAcquisition', parameters: {} }],
        connections: [],
      },
      requirementBrief: {
        requirementMode: 'strict',
        confidence: 0.85,
        clarificationRequired: false,
        draftRiskLevel: 'low',
        knownFacts: ['旧方案保留'],
        missingFacts: [],
        blockingClarificationFields: [],
        nonBlockingMissingFields: [],
        clarificationQuestions: [
          {
            field: 'scene',
            question: '旧摘要里的可选澄清。',
            required: false,
            options: ['旧选项'],
          },
        ],
      },
    });

    panel.activeGenerateRequestId = 'new-clarification';
    panel.isGenerating = true;
    panel._handleResult({
      requestId: 'new-clarification',
      success: false,
      status: 'clarification_required',
      failureType: 'clarification_required',
      clarificationRequired: true,
      turnIntent: 'new_flow',
      routerConfidence: 'high',
      aiExplanation: '新需求需要先澄清。',
      blockingClarificationFields: ['scene'],
      nonBlockingMissingFields: [],
      requirementBrief: {
        requirementMode: 'strict',
        confidence: 0,
        clarificationRequired: true,
        draftRiskLevel: 'high',
        knownFacts: [],
        missingFacts: ['需要确认场景类型'],
        blockingClarificationFields: ['scene'],
        nonBlockingMissingFields: [],
        clarificationQuestions: [
          {
            field: 'scene',
            question: '请确认新需求的视觉场景。',
            required: true,
            reason: '未确认场景时不能构建。',
            options: ['外观缺陷', '漏装/有无'],
          },
        ],
      },
    });
  });

  await expect(page.locator('#ai-btn-send-clarification-brief')).toHaveCount(0);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toHaveCount(0);
  await expect(page.locator('#ai-clarification-plan-card')).toHaveCount(0);
  expect(await page.locator('#ai-btn-send-clarification').count()).toBe(0);
  await expect(page.locator('#ai-result-requirement-brief')).toContainText('只读');

  const retainedFlowOperatorId = await page.evaluate(() =>
    (window as any).aiPanel.currentResult?.flow?.operators?.[0]?.id);
  expect(retainedFlowOperatorId).toBe('op-old');
  await expect(page.locator('#ai-btn-apply')).not.toContainText('暂无可应用方案');
});

test('AI panel sends current flow context for modification turns', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await installFakeWebView2(page);
  await mockShellApis(page);
  const agentRunPayloads: any[] = [];
  await mockAgentRunBuild(page, 'ar_webview_modify', agentRunPayloads);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && !(window as any).mockWebViewResponse));

  await page.evaluate(() => {
    const flow = {
      operators: [
        {
          id: 'op_1',
          type: 'ImageAcquisition',
          displayName: 'ImageAcquisition',
          parameters: { CameraId: 'cam-1' },
        },
        {
          id: 'op_2',
          type: 'Thresholding',
          displayName: 'Thresholding',
          parameters: { Threshold: 128 },
        },
      ],
      connections: [
        {
          sourceId: 'op_1',
          sourcePort: 'Image',
          targetId: 'op_2',
          targetPort: 'Image',
        },
      ],
    };
    const panel = (window as any).aiPanel;
    panel._setCurrentResult({ success: true, flow });
  });

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel._dispatchGenerateRequest({
      description: '把算子名称改成中文，其他参数和连线保持不变',
      userMessage: '把算子名称改成中文，其他参数和连线保持不变',
      explicitMode: 'modify',
      skipPlan: true,
      skipPlanSource: 'intent_router_build',
    });
  });

  await expect.poll(() => agentRunPayloads.length).toBe(1);
  expect(agentRunPayloads).toHaveLength(1);
  expect(agentRunPayloads[0].mode).toBe('modify');
  expect(agentRunPayloads[0].existingFlowJson).toBeTruthy();
  const existingFlowSnapshot = typeof agentRunPayloads[0].existingFlowJson === 'string'
    ? JSON.parse(agentRunPayloads[0].existingFlowJson)
    : agentRunPayloads[0].existingFlowJson;
  expect(existingFlowSnapshot.operators).toHaveLength(2);
  expect(existingFlowSnapshot.connections).toHaveLength(1);
  const webMessages = await page.evaluate(() => (window as any).__cvWebViewMessages || []);
  expect(webMessages.some((message: any) => message.messageType === 'GenerateFlow')).toBe(false);
  await expect(page.locator('#ai-agent-runtime')).toContainText('微调中');
  await expect(page.locator('#ai-agent-runtime')).toContainText('意图 增量微调');
});

test('AI panel keeps explicit new-flow requests from being coerced into modification', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await installFakeWebView2(page);
  await mockShellApis(page);
  const agentRunPayloads: any[] = [];
  await mockAgentRunBuild(page, 'ar_webview_new', agentRunPayloads);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && !(window as any).mockWebViewResponse));

  await page.evaluate(() => {
    const flow = {
      operators: [
        {
          id: 'op_1',
          type: 'ImageAcquisition',
          displayName: 'ImageAcquisition',
          parameters: { CameraId: 'cam-1' },
        },
      ],
      connections: [],
    };
    const panel = (window as any).aiPanel;
    panel._setCurrentResult({ success: true, flow });
  });

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.isVisionAgentDeveloperUiEnabled = true;
    panel._dispatchGenerateRequest({
      description: '新增一个缺陷检测流程',
      userMessage: '新增一个缺陷检测流程',
      explicitMode: 'new',
      skipPlan: true,
      skipPlanSource: 'developer_direct_build_debug',
    });
  });

  await expect.poll(() => agentRunPayloads.length).toBe(1);
  expect(agentRunPayloads).toHaveLength(1);
  expect(agentRunPayloads[0].mode).toBe('new');
  expect(agentRunPayloads[0].existingFlowJson).toBeNull();
  const webMessages = await page.evaluate(() => (window as any).__cvWebViewMessages || []);
  expect(webMessages.some((message: any) => message.messageType === 'GenerateFlow')).toBe(false);
  await expect(page.locator('#ai-agent-runtime')).toContainText('生成中');
  await expect(page.locator('#ai-agent-runtime')).toContainText('意图 新建流程');
});

test.skip('AI panel uses WebView2 postMessage contract for generation turns', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await installFakeWebView2(page);
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && !(window as any).mockWebViewResponse));

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.isVisionAgentDeveloperUiEnabled = true;
    panel._dispatchGenerateRequest({
      description: '你好，帮我做缺陷检测',
      userMessage: '你好，帮我做缺陷检测',
      explicitMode: 'auto',
      skipPlan: true,
      skipPlanSource: 'developer_direct_build_debug',
    });
  });

  await page.waitForFunction(() =>
    ((window as any).__cvWebViewMessages || []).some((message: any) => message.messageType === 'GenerateFlow'));

  const sent = await page.evaluate(() => {
    const messages = (window as any).__cvWebViewMessages || [];
    return messages.find((message: any) => message.messageType === 'GenerateFlow');
  });

  expect(sent.payload.description).toBe('你好，帮我做缺陷检测');
  expect(sent.payload.mode).toBe('auto');
  expect(sent.payload.requirementMode).toBe('strict');
  expect(sent.payload.requestId).toBeTruthy();
  expect(Object.prototype.hasOwnProperty.call(sent.payload, 'sessionId')).toBe(true);

  await page.evaluate(id => {
    (window as any).__dispatchCvWebViewMessage({
      type: 'GenerateFlowResult',
      payload: {
        requestId: id,
        success: false,
        status: 'clarification_required',
        failureType: 'clarification_required',
        clarificationRequired: true,
        turnIntent: 'new_flow',
        interactionState: 'clarifying',
        routerConfidence: 'high',
        aiExplanation: '当前需求还需要澄清检测对象。',
        blockingClarificationFields: ['object_type'],
        nonBlockingMissingFields: ['model_path'],
        requirementBrief: {
          requirementMode: 'strict',
          confidence: 0.42,
          clarificationRequired: true,
          draftRiskLevel: 'high',
          missingFacts: ['需要确认检测对象'],
          blockingClarificationFields: ['object_type'],
          nonBlockingMissingFields: ['model_path'],
          clarificationQuestions: [
            {
              field: 'object_type',
              question: '请补充检测对象是什么。',
              required: true,
              priority: 'high',
              reason: '需要明确对象才能选择正确模板与算子。',
              options: ['产品', '包装箱/纸箱', '金属件'],
            },
          ],
        },
      },
    });
  }, sent.payload.requestId);

  await expect(page.locator('#ai-clarification-plan-card')).toHaveCount(0);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toHaveCount(0);
  await expect(page.locator('.ai-assistant-clarification-section').last()).toBeHidden();
  expect(await page.evaluate(() => (window as any).aiPanel.pendingClarificationPayload)).toBeNull();
});

test('Plan pending recommendation records defer without becoming an effective answer', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await installFakeWebView2(page);
  await mockShellApis(page);
  await page.route('**/api/ai/agent-plan/readiness-preview', async route => {
    const request = route.request().postDataJSON();
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        planId: request.planId,
        planHash: request.planHash,
        requirementMode: request.requirementMode || 'strict',
        answerRevision: request.answerRevision || 0,
        resourceRevision: request.resourceRevision || 0,
        acceptedAnswers: [],
        deferredQuestionIds: ['image_source'],
        buildReadiness: {
          canBuild: false,
          blockers: [{
            id: 'hard_requirement:image_source',
            category: 'hard_requirement',
            field: 'image_source',
            questionId: 'image_source',
            blocksBuild: true,
            resolutionMode: 'answer_question',
            publicLabel: 'Image source pending',
          }],
          resolvedFields: ['inspection_object', 'task_type', 'acceptance_criteria'],
          remainingFields: ['image_source'],
          primaryMessage: 'Image source pending',
          contractVersion: 'v2',
        },
        pendingConfirmationCount: 1,
        resourcePendingCount: 0,
        hardBlockerCount: 1,
        buildBlockingConfirmationCount: 1,
        buildRequiredResourceCount: 0,
        deferredFieldCount: 1,
        draftAllowedResourceCount: 0,
        mustConfirmBeforeBuildCount: 1,
        fillLaterCount: 1,
        totalIncompleteCount: 1,
        contractValid: true,
        metadataOnly: true,
      }),
    });
  });
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('#ai-plan-workspace')));

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    const plan = panel._normalizeBackendPlanResult({
      planId: 'plan_playwright_pending',
      planHash: 'sha256:playwright-pending',
      originalUserPrompt: 'detect logo scratches',
      goal: 'detect logo scratches',
      intent: 'surface_defect',
      confidence: 'high',
      requirementMode: 'strict',
      planSource: 'rule_fallback',
      fallbackReason: 'planner_failed',
      requirementUnderstanding: ['Inspection intent: surface defect inspection.'],
      recommendedRoute: {
        routeId: 'surface_defect_detection',
        title: 'Surface defect route',
        summary: 'Detect visible defects.',
        operators: ['ImageAcquisition', 'SurfaceDefectDetection', 'ResultOutput'],
      },
      clarificationQuestions: [
        {
          id: 'image_source',
          field: 'image_source',
          title: 'Image source',
          why: 'Build needs a confirmed input source.',
          defaultValue: 'camera_pending',
          defaultAssumption: 'Keep image source pending until the user confirms it.',
          impact: 'Pending selections do not unblock Build.',
          options: [
            {
              value: 'camera_pending',
              label: 'Keep image source pending',
              recommended: true,
              answerEffect: 'defer',
              description: 'Do not guess a camera or file path.',
              impact: 'This keeps Build blocked.',
            },
            {
              value: 'file_sample',
              label: 'Offline sample',
              recommended: false,
              answerEffect: 'resolve_field',
              description: 'Use an offline image sample.',
              impact: 'This resolves the image source.',
            },
          ],
        },
      ],
      recommendedDefaults: [],
      risks: ['Representative images are still needed.'],
      acceptanceCriteria: ['Workflow contains acquisition, detection, and output.'],
      executablePlan: ['Map parameters and run readiness checks.'],
      canPlan: true,
      canBuild: false,
      buildReadiness: {
        canBuild: false,
        blockers: [
          {
            id: 'hard_requirement:image_source',
            category: 'hard_requirement',
            field: 'image_source',
            questionId: 'image_source',
            blocksBuild: true,
            resolutionMode: 'answer_question',
            publicLabel: 'Image source pending',
          },
        ],
        resolvedFields: ['inspection_object', 'task_type', 'acceptance_criteria'],
        remainingFields: ['image_source'],
        primaryMessage: 'Image source pending',
        contractVersion: 'v2',
      },
      blockingReasons: ['hard_requirement:image_source_missing'],
      requirementMaturity: {
        maturity: 'ambiguous',
        taskType: 'surface_defect',
        canPlan: true,
        canBuild: false,
        objectSignals: ['logo area'],
        taskSignals: ['scratch'],
        missingFields: ['image_source'],
        blockingReasons: ['image_source_missing'],
        publicReason: 'Image source pending',
      },
      semanticExtraction: {
        isVisionRequest: true,
        source: 'rule_fallback',
        taskType: 'surface_defect',
        confidence: 0.8,
        taskTypeConfidence: 0.8,
        inspectionObject: 'logo area',
        defectType: 'scratch',
        imageSource: '',
        okCondition: 'no visible scratch',
        ngCondition: '',
        outputTarget: 'OK/NG result',
        missingFields: ['image_source'],
      },
    });
    panel.pendingVisionPlan = plan;
    panel.planQuestionSelections = {};
    panel.planQuestionAnswers = {};
    panel.agentWorkspaceMode = 'plan';
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(plan);
  });

  const pending = page.locator('input[data-ai-plan-option="true"][value="camera_pending"]');
  await pending.click();
  await expect(page.locator('[data-ai-hook="clarification-deferred"]')).toContainText('稍后确认，当前不会作为业务答案');
  await expect(page.locator('#ai-btn-start-build')).toBeDisabled();
  expect(await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    return {
      selected: panel.planQuestionSelections.image_source,
      hasAnswer: Object.prototype.hasOwnProperty.call(panel.planQuestionAnswers, 'image_source'),
      canBuild: panel.pendingVisionPlan.buildReadiness.canBuild,
      resolved: [...panel.pendingVisionPlan.buildReadiness.resolvedFields].sort(),
    };
  })).toEqual({
    selected: 'camera_pending',
    hasAnswer: false,
    canBuild: false,
    resolved: ['acceptance_criteria', 'inspection_object', 'task_type'],
  });
});

test('AI panel posts Build through AgentRun even when WebView2 is available', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await installFakeWebView2(page);
  await mockShellApis(page);
  const agentRunPayloads: any[] = [];
  await page.route('**/api/ai/agent-runs**', async route => {
    const request = route.request();
    if (request.method() === 'POST' && request.url().endsWith('/api/ai/agent-runs')) {
      agentRunPayloads.push(request.postDataJSON());
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          runId: 'ar_webview_build',
          brief: 'Build started',
          events: [
            {
              runId: 'ar_webview_build',
              sequence: 1,
              eventType: 'run.started',
              stage: 'run',
              status: 'running',
              payload: { metadataOnly: true },
            },
          ],
        }),
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'text/event-stream',
      body: [
        'event: run.completed',
        'data: {"runId":"ar_webview_build","sequence":2,"eventType":"run.completed","stage":"run","status":"completed","payload":{"status":"completed","flow":{"operators":[]},"buildResult":{"applyGate":{"canvasApplyReady":false,"blocked":true}},"metadataOnly":true}}',
        '',
        '',
      ].join('\n'),
    });
  });
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && !(window as any).mockWebViewResponse));

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.isVisionAgentDeveloperUiEnabled = true;
    panel._dispatchGenerateRequest({
      description: 'webview build request',
      userMessage: 'webview build request',
      explicitMode: 'auto',
      skipPlan: true,
      skipPlanSource: 'developer_direct_build_debug',
    });
  });

  await expect.poll(() => agentRunPayloads.length).toBe(1);
  expect(agentRunPayloads).toHaveLength(1);
  expect(agentRunPayloads[0].description).toBe('webview build request');
  expect(agentRunPayloads[0].mode).toBe('auto');
  expect(agentRunPayloads[0].requirementMode).toBe('strict');
  expect(agentRunPayloads[0].requestId).toBeTruthy();
  expect(Object.prototype.hasOwnProperty.call(agentRunPayloads[0], 'sessionId')).toBe(true);
  const webMessages = await page.evaluate(() => (window as any).__cvWebViewMessages || []);
  expect(webMessages.some((message: any) => message.messageType === 'GenerateFlow')).toBe(false);
});

test('AI agent workbench default Plan view hides raw semantic trace until diagnostics expand', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('#ai-plan-workspace')));

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    const plan = panel._normalizeBackendPlanResult({
      planId: 'plan_default_semantic_hidden',
      planHash: 'sha256:default-semantic-hidden',
      originalUserPrompt: '检测产品表面划痕',
      goal: '检测产品表面划痕',
      intent: 'surface_defect',
      confidence: 'high',
      requirementMode: 'strict',
      planSource: 'rule_fallback',
      fallbackReason: 'semantic_model_request_failed',
      requirementUnderstanding: ['目标是表面划痕检测。'],
      recommendedRoute: {
        routeId: 'surface_defect_default',
        title: '表面缺陷检测流程',
        summary: '采集图像后检测划痕并输出 OK/NG。',
        operators: ['ImageAcquisition', 'SurfaceDefectDetection', 'ResultOutput'],
      },
      clarificationQuestions: [],
      recommendedDefaults: [],
      risks: [],
      acceptanceCriteria: ['确认图像来源和 OK/NG 判定。'],
      executablePlan: ['补齐图像来源后构建流程草稿。'],
      canPlan: true,
      canBuild: false,
      buildReadiness: {
        canBuild: false,
        blockers: [
          { field: 'image_source', blocksBuild: true, category: 'hard_requirement' },
          { field: 'acceptance_criteria', blocksBuild: true, category: 'hard_requirement' },
        ],
        resolvedFields: ['inspection_object', 'task_type'],
        remainingFields: ['image_source', 'acceptance_criteria'],
        primaryMessage: '需要补充图像来源和判定标准。',
        contractVersion: 'v2',
      },
      requirementMaturity: {
        maturity: 'ambiguous',
        taskType: 'surface_defect',
        canPlan: true,
        canBuild: false,
        objectSignals: ['product surface'],
        taskSignals: ['scratch'],
        missingFields: ['image_source', 'acceptance_criteria'],
        blockingReasons: ['image_source_missing', 'acceptance_criteria_missing'],
        publicReason: '规则兜底可继续，但还需要确认输入和判定标准。',
        metadataOnly: true,
      },
      decisionTrace: {
        taskType: 'surface_defect',
        objectSignalsHit: ['product surface'],
        fallbackReason: 'semantic_model_request_failed',
        metadataOnly: true,
      },
      semanticExtraction: {
        isVisionRequest: true,
        source: 'rule_fallback',
        taskType: 'surface_defect',
        inspectionObject: '产品表面',
        okCondition: '',
        ngCondition: '',
        imageSource: 'unknown',
        missingFields: ['image_source', 'acceptance_criteria'],
        failureCode: 'semantic_model_request_failed',
        sanitizedErrorMessage: 'semantic service unavailable',
        metadataOnly: true,
      },
      publicEvents: [
        {
          stage: 'semantic_fallback_used',
          status: 'warning',
          title: 'semantic fallback',
          summary: 'semantic fallback used',
          metadata: {
            failureCode: 'semantic_model_request_failed',
            taskType: 'surface_defect',
            metadataOnly: true,
          },
          metadataOnly: true,
        },
      ],
      metadataOnly: true,
    }, '检测产品表面划痕');
    panel.pendingVisionPlan = plan;
    panel.agentWorkspaceMode = 'plan';
    panel.workspaceViewMode = 'plan';
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(plan);
  });

  await expect(page.locator('#ai-plan-workspace')).toContainText('AI 理解成了什么');
  await expect(page.locator('#ai-plan-workspace')).toContainText('推荐方案');
  await expect(page.locator('#ai-plan-workspace')).toContainText('关键问题');
  await expect(page.locator('#ai-plan-workspace')).toContainText('风险与工程详情');
  await expect(page.locator('[data-ai-hook="clarification-question"]')).toHaveCount(0);
  await expect(page.locator('[data-ai-hook="clarification-contract-gap"]')).toContainText('暂无可回答的关键问题');
  await expect(page.locator('[data-workspace-view-mode="plan"]')).toContainText('方案');
  await expect(page.locator('[data-workspace-view-mode="build"]')).toContainText('构建与验证');
  await expect(page.locator('[data-workspace-view-mode="build"]')).toBeDisabled();
  await expect(page.locator('#ai-agent-workspace-overview')).toContainText('已形成初步方案');
  await expect(page.locator('#ai-agent-workspace-overview')).toContainText('还需补充 2 项信息');
  await expect(page.locator('#ai-agent-workspace-overview')).toContainText('暂不能构建');
  await expect(page.locator('#ai-agent-workspace-overview')).not.toContainText('可构建：否');
  await expect(page.locator('.ai-agent-overview-card')).toHaveClass(/is-warning/);
  await expect(page.locator('.ai-agent-overview-card')).not.toHaveClass(/is-danger/);
  await expect(page.locator('[data-ai-hook="task-blockers"]')).toBeHidden();
  const visibleRawSnippets = await page.evaluate(() => {
    const root = document.querySelector('#ai-plan-workspace');
    const snippets = ['semantic.taskType', 'semantic.failureCode', 'objectSignals', 'metadataOnly'];
    if (!root) return snippets;
    const isTextVisible = (node: Text) => {
      const parent = node.parentElement;
      const closedDetails = parent?.closest('details:not([open])');
      if (closedDetails && !parent?.closest('summary')) {
        return false;
      }
      const range = document.createRange();
      range.selectNodeContents(node);
      const visible = Array.from(range.getClientRects()).some(rect => rect.width > 0 && rect.height > 0);
      range.detach();
      return visible;
    };
    return snippets.filter(snippet => {
      const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
      let current = walker.nextNode() as Text | null;
      while (current) {
        if ((current.nodeValue || '').includes(snippet) && isTextVisible(current)) {
          return true;
        }
        current = walker.nextNode() as Text | null;
      }
      return false;
    });
  });
  expect(visibleRawSnippets).toEqual([]);
  await mkdir(workbenchEvidenceDir, { recursive: true });
  await page.screenshot({
    path: path.join(workbenchEvidenceDir, 'default-plan-1280x820-no-raw-semantic.png'),
    fullPage: true,
  });
  await page.setViewportSize({ width: 1920, height: 1080 });
  await expect(page.locator('[data-workspace-view-mode="build"]')).toContainText('构建与验证');
  await page.screenshot({
    path: path.join(workbenchEvidenceDir, 'default-plan-1920x1080-no-raw-semantic.png'),
    fullPage: true,
  });

  await page.setViewportSize({ width: 1280, height: 820 });
  await page.screenshot({
    path: path.join(workbenchEvidenceDir, 'default-plan-1280x820-before-cta-clicks.png'),
    fullPage: true,
  });

  await expect(page.locator('#ai-plan-focus-confirmation')).toHaveCount(0);
  await expect(page.locator('#ai-plan-use-recommended-defaults')).toHaveCount(0);
  await expect(page.locator('#ai-plan-view-draft')).toHaveCount(0);

  await page.locator('summary', { hasText: '风险与工程详情' }).click();
  await expect(page.locator('.ai-plan-raw-diagnostic-rows span', { hasText: 'semantic.taskType' })).toBeVisible();
  await expect(page.locator('.ai-plan-raw-diagnostic-rows span', { hasText: 'semantic.failureCode' })).toBeVisible();
  await expect(page.locator('.ai-plan-raw-diagnostic-block > span', { hasText: 'Agent Trace' })).toBeVisible();
});

test('AI agent Plan to Build exposes visible Apply button and applies through real click path', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await mkdir(applyEvidenceDir, { recursive: true });

  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const networkErrors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text());
    }
  });
  page.on('pageerror', error => {
    pageErrors.push(error.message);
  });
  page.on('requestfailed', request => {
    networkErrors.push(`${request.method()} ${request.url()} ${request.failure()?.errorText || ''}`.trim());
  });
  page.on('response', response => {
    if (response.status() >= 400) {
      networkErrors.push(`${response.status()} ${response.url()}`);
    }
  });

  await installFakeWebView2(page);
  await mockShellApis(page);
  await mockAgentPlanAndApplyReadyBuild(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').click();
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('#ai-input')));

  await page.locator('#ai-input').fill('检测产品表面划痕并输出二值化结果');
  await page.locator('#ai-btn-gen').click();

  const buildButton = page.locator('#ai-btn-start-build');
  await expect(buildButton).toBeVisible({ timeout: 15_000 });
  await expect(buildButton).toBeEnabled({ timeout: 15_000 });
  await buildButton.click();

  await page.waitForFunction(() => (window as any).aiPanel?.workbenchState === 'ready_to_apply', null, { timeout: 15_000 });
  const applyButton = page.locator('#ai-btn-apply');
  await expect(applyButton).toBeEnabled();
  await expect(applyButton).toHaveAttribute('aria-disabled', 'false');

  const beforeClickState = await collectApplyButtonState(page);
  await writeApplyEvidence('apply-button-state.before-click.json', beforeClickState);
  await page.screenshot({ path: path.join(applyEvidenceDir, 'before-apply-button-visible.png'), fullPage: true });
  if (!beforeClickState.rect || beforeClickState.rect.width === 0 || beforeClickState.rect.height === 0) {
    await writeApplyEvidence('apply-button-state.before-fix.json', beforeClickState);
  }

  const applyBox = await applyButton.boundingBox();
  expect(applyBox).not.toBeNull();
  expect(applyBox?.width || 0).toBeGreaterThan(0);
  expect(applyBox?.height || 0).toBeGreaterThan(0);

  await applyButton.click();
  const confirmPreview = page.locator('.ai-apply-preview-confirm');
  if (await confirmPreview.isVisible({ timeout: 1000 }).catch(() => false)) {
    await confirmPreview.click();
  }

  await page.waitForFunction(() => ((window as any).flowCanvas?.nodes?.size || 0) >= 3, null, { timeout: 10_000 });
  await page.screenshot({ path: path.join(applyEvidenceDir, 'after-apply-canvas-nodes.png'), fullPage: true });
  const appliedNodeTypes = await page.evaluate(() =>
    Array.from((window as any).flowCanvas?.nodes?.values?.() || [])
      .map((node: any) => node.type || '')
      .filter(Boolean));
  expect(appliedNodeTypes).toEqual(expect.arrayContaining(['ImageAcquisition', 'ROIManager', 'Threshold']));
  expect(appliedNodeTypes).toHaveLength(3);

  await page.locator('.nav-btn[data-view="ai"]').click();
  await page.waitForFunction(() => (window as any).aiPanel?.workbenchState === 'applied', null, { timeout: 10_000 });
  await expect(applyButton).toBeDisabled();
  await expect(applyButton).toContainText('已应用到画布');

  const finalState = await collectApplyButtonState(page);
  await writeApplyEvidence('apply-button-state.json', {
    ...finalState,
    beforeClick: beforeClickState,
  });
  await writeApplyEvidence('console-errors.json', {
    consoleErrors,
    pageErrors,
    networkErrors,
  });

  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(networkErrors).toEqual([]);
});

test('AI agent Apply button keeps dimensions on narrow viewport after Build ready', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await installFakeWebView2(page);
  await mockShellApis(page);
  await mockAgentPlanAndApplyReadyBuild(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').click();
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('#ai-input')));

  await page.locator('#ai-input').fill('检测产品表面划痕并输出二值化结果');
  await page.locator('#ai-btn-gen').click();
  await expect(page.locator('#ai-btn-start-build')).toBeEnabled({ timeout: 15_000 });
  await page.locator('#ai-btn-start-build').click();
  await page.waitForFunction(() => (window as any).aiPanel?.workbenchState === 'ready_to_apply', null, { timeout: 15_000 });

  const state = await collectApplyButtonState(page);
  expect(state.workspaceViewMode).toBe('build');
  expect(state.tracked.buildWorkspace.hidden).toBe(false);
  expect(state.rect?.width || 0).toBeGreaterThan(0);
  expect(state.rect?.height || 0).toBeGreaterThan(0);
  await expect(page.locator('#ai-btn-apply')).toBeEnabled();
});
