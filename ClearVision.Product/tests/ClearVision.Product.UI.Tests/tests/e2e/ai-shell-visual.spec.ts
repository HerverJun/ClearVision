import { test, expect, Page } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { bootAuthenticatedApp } from './authHelper';

type Theme = 'dark' | 'light';
const planningEvidenceDir = path.resolve(process.cwd(), 'test-results', 'ai-planning-evidence');

async function capturePlanningEvidence(page: Page, filename: string): Promise<void> {
  await mkdir(planningEvidenceDir, { recursive: true });
  await page.screenshot({ path: path.join(planningEvidenceDir, filename), fullPage: false });
}

async function focusPlanningClarification(page: Page): Promise<void> {
  await page.evaluate(() => {
    const pane = document.querySelector('#ai-plan-workspace') as HTMLElement | null;
    const clarification = document.querySelector('[data-ai-hook="clarification-workspace"]') as HTMLElement | null;
    if (!pane || !clarification) return;
    const paneRect = pane.getBoundingClientRect();
    const clarificationRect = clarification.getBoundingClientRect();
    pane.scrollTop += clarificationRect.top - paneRect.top - 18;
  });
}

async function mockShellApis(page: Page, theme: Theme): Promise<void> {
  await page.route('**/api/settings', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        general: { softwareTitle: 'ClearVision', theme, autoStart: false },
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
}

async function openAi(page: Page, options: { theme?: Theme; width: number; height: number }): Promise<void> {
  const theme = options.theme ?? 'dark';
  await page.setViewportSize({ width: options.width, height: options.height });
  await mockShellApis(page, theme);
  await bootAuthenticatedApp(page);
  await page.locator('.nav-btn[data-view="ai"]').click();
  await page.waitForFunction(() => Boolean((window as any).aiPanel && document.querySelector('[data-ai-hook="shell"]')));
  await page.evaluate(selectedTheme => {
    document.documentElement.dataset.theme = selectedTheme;
  }, theme);
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

async function seedActivePlan(page: Page): Promise<void> {
  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    const prompt = '检测包装箱表面的破损，并输出稳定的 OK/NG 结果';
    const plan = panel._normalizeBackendPlanResult({
      planId: 'plan_shell_visual',
      planHash: 'sha256:shell-visual',
      originalUserPrompt: prompt,
      goal: '包装箱外观缺陷检测',
      intent: 'surface_defect',
      confidence: 'high',
      requirementMode: 'strict',
      planSource: 'model_router',
      requirementUnderstanding: [
        '检测对象是包装箱表面',
        '目标是识别破损并输出 OK/NG',
      ],
      recommendedRoute: {
        routeId: 'packaging_surface_shell',
        title: '包装箱外观检测流程',
        summary: '采集图像后限定检测区域，完成缺陷检测与结果输出。',
        operators: ['ImageAcquisition', 'RoiManager', 'SurfaceDefectDetection', 'ResultOutput'],
      },
      clarificationQuestions: [],
      recommendedDefaults: [],
      risks: ['现场光照变化需要在验证阶段复核'],
      acceptanceCriteria: ['破损样本输出 NG，完整样本输出 OK'],
      executablePlan: ['采集图像', '限定检测区域', '检测破损', '输出结果'],
      canPlan: true,
      canBuild: true,
      nextAction: '确认方案后开始构建可编辑流程草稿。',
      buildReadiness: {
        canBuild: true,
        blockers: [],
        resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
        remainingFields: [],
        primaryMessage: '方案信息完整，可以开始构建。',
        contractVersion: 'v2',
      },
      requirementMaturity: {
        maturity: 'actionable',
        taskType: 'surface_defect',
        canPlan: true,
        canBuild: true,
        objectSignals: ['包装箱表面'],
        taskSignals: ['破损', 'OK/NG'],
        missingFields: [],
        blockingReasons: [],
        publicReason: '检测对象、任务目标和验收标准已明确。',
      },
      semanticExtraction: {
        isVisionRequest: true,
        source: 'model_router',
        taskType: 'surface_defect',
        confidence: 0.94,
        inspectionObject: '包装箱表面',
        defectType: '破损',
        imageSource: 'camera',
        okCondition: '包装箱表面完整',
        ngCondition: '存在可见破损',
        outputTarget: 'OK/NG',
        missingFields: [],
      },
      publicEvents: [],
      metadataOnly: true,
    }, prompt);

    panel.pendingVisionPlan = plan;
    panel.agentWorkspaceMode = 'plan';
    panel.workspaceViewMode = 'plan';
    panel.history = [
      {
        sessionId: 'recent-1',
        lastMessage: '端子线序复核',
        updatedAtUtc: '2026-07-10T08:00:00.000Z',
        turnCount: 4,
      },
      {
        sessionId: 'recent-2',
        lastMessage: '金属表面划痕检测',
        updatedAtUtc: '2026-07-09T08:00:00.000Z',
        turnCount: 3,
      },
    ];
    panel._addMessage('user', prompt);
    panel._addMessage('ai', '已形成初步方案。工作台保留现有方案与构建内容，会话区可继续补充条件。');
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(plan);
    panel._renderBuildWorkspaceFromAgentRun();
    panel._renderHistoryList();
  });
  await expect(page.locator('[data-ai-hook="shell"]')).toHaveAttribute('data-ai-shell-state', 'active');
}

async function expectNoPageOverflow(page: Page): Promise<void> {
  const metrics = await page.evaluate(() => {
    const root = document.querySelector('#ai-view') as HTMLElement | null;
    const main = document.querySelector('.main-content') as HTMLElement | null;
    return {
      documentHorizontal: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      bodyHorizontal: document.body.scrollWidth - document.body.clientWidth,
      rootHorizontal: root ? root.scrollWidth - root.clientWidth : 1,
      rootVertical: root ? root.scrollHeight - root.clientHeight : 1,
      mainVertical: main ? main.scrollHeight - main.clientHeight : 1,
    };
  });
  expect(metrics.documentHorizontal).toBeLessThanOrEqual(1);
  expect(metrics.bodyHorizontal).toBeLessThanOrEqual(1);
  expect(metrics.rootHorizontal).toBeLessThanOrEqual(1);
  expect(metrics.rootVertical).toBeLessThanOrEqual(1);
  expect(metrics.mainVertical).toBeLessThanOrEqual(1);
}

async function switchCompactPane(page: Page, pane: 'workbench' | 'conversation'): Promise<void> {
  const tab = page.locator(`[data-ai-shell-pane="${pane}"]`);
  await expect(tab).toBeVisible();
  await tab.click();
  await expect(tab).toHaveAttribute('aria-selected', 'true');
}

async function restoreLegacySession(page: Page, options: { messageOnly?: boolean } = {}): Promise<void> {
  await page.evaluate(({ messageOnly }) => {
    const panel = (window as any).aiPanel;
    const sessionId = messageOnly ? 'legacy-message-only' : 'legacy-result-only';
    const requestId = `${sessionId}-request`;
    const navigationEpoch = Number(panel.sessionNavigationEpoch || 0);
    panel.pendingSessionLoad = {
      sessionId,
      source: 'history_switch',
      epoch: navigationEpoch,
      requestId,
    };
    const history = messageOnly
      ? [
          { role: 'user', message: '恢复这条只有历史消息的会话' },
          { role: 'assistant', message: '历史消息已恢复，但没有方案或构建结果。' },
        ]
      : [
          { role: 'user', message: '恢复旧版构建结果' },
          {
            role: 'assistant',
            message: '历史构建结果已恢复。',
            payload: {
              reply: '历史构建结果已恢复。',
              buildResult: {
                status: 'completed',
                applyGate: {
                  canvasApplyReady: false,
                  blocked: true,
                  status: 'blocked',
                },
              },
              applyGate: {
                canvasApplyReady: false,
                blocked: true,
                status: 'blocked',
              },
            },
          },
        ];
    panel._handleGetAiSessionResult({
      payload: {
        success: true,
        sessionId,
        requestId,
        navigationEpoch,
        session: {
          sessionId,
          history,
          workspaceSnapshot: null,
          updatedAtUtc: '2026-07-11T08:00:00.000Z',
        },
      },
    });
  }, options);
}

test('default startup uses legacy AiPanel and idle examples only fill the composer', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });

  const pathEvidence = await page.evaluate(() => ({
    constructorName: (window as any).aiPanel?.constructor?.name,
    capabilityEnabled: (window as any).__CLEARVISION_STARTUP__?.featureFlags?.['Studio2.AiPanel'] === true,
    shellHookCount: document.querySelectorAll('[data-ai-hook="shell"]').length,
    workbenchPaneCount: document.querySelectorAll('[data-ai-hook="workbench-pane"]').length,
    conversationPaneCount: document.querySelectorAll('[data-ai-hook="conversation-pane"]').length,
  }));
  expect(pathEvidence).toEqual({
    constructorName: 'AiPanel',
    capabilityEnabled: false,
    shellHookCount: 1,
    workbenchPaneCount: 1,
    conversationPaneCount: 1,
  });

  await expect(page.locator('[data-ai-hook="shell"]')).toHaveAttribute('data-ai-shell-state', 'idle');
  await expect(page.locator('[data-ai-hook="workbench-pane"]')).toBeHidden();
  await expect(page.locator('[data-ai-hook="idle-intro"]')).toBeVisible();
  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.history = [1, 2, 3, 4].map(index => ({
      sessionId: `idle-recent-${index}`,
      lastMessage: `最近视觉任务 ${index}`,
      updatedAtUtc: `2026-07-0${index}T08:00:00.000Z`,
      turnCount: index,
    }));
    panel._renderHistoryList();
  });
  await expect(page.locator('[data-ai-hook="idle-recent-item"]')).toHaveCount(3);
  const userMessagesBefore = await page.locator('.ai-message.user').count();
  await page.locator('.ai-tag').filter({ hasText: '条码读取' }).click();
  await expect(page.locator('#ai-input')).not.toHaveValue('');
  await expect(page.locator('.ai-message.user')).toHaveCount(userMessagesBefore);
  await expect(page.locator('#ai-btn-start-build')).toHaveCount(0);
  await expect(page.locator('#ai-btn-apply')).toHaveCount(1);
  await expectNoPageOverflow(page);
});

test('Router pending activates the shell before the first canonical result without opening business gates', async ({ page }) => {
  let releaseRouter!: () => void;
  let markRouterRequested!: () => void;
  const routerRequested = new Promise<void>(resolve => { markRouterRequested = resolve; });
  const routerReleased = new Promise<void>(resolve => { releaseRouter = resolve; });
  await page.route('**/api/ai/agent-intent-router-runs', async route => {
    markRouterRequested();
    await routerReleased;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        intent: 'ambiguous_vision_requirement',
        confidence: 'medium',
        shouldOpenPlan: false,
        shouldBuildDirectly: false,
        canPlan: true,
        canBuild: false,
        needsClarification: true,
        publicReason: '请补充检测对象。',
        assistantReply: '请补充检测对象。',
        metadataOnly: true,
      }),
    });
  });
  await openAi(page, { width: 1366, height: 768 });

  await page.locator('#ai-input').fill('帮我创建一个检测任务');
  await page.locator('#ai-btn-gen').click();
  await routerRequested;
  await expect(page.locator('[data-ai-hook="shell"]')).toHaveAttribute('data-ai-shell-state', 'active');
  await expect(page.locator('.ai-message.user')).toContainText('帮我创建一个检测任务');
  await expect(page.locator('#ai-chat-container')).toContainText('正在理解需求');
  await expect(page.locator('[data-planning-phase="understand"]')).toContainText('进行中');
  await expect(page.locator('[data-planning-phase="context"]')).toContainText('等待中');
  await expect(page.locator('[data-planning-phase="generate"]')).toContainText('等待中');
  await expect(page.locator('[data-planning-phase="validate"]')).toContainText('等待中');
  await expect(page.locator('[data-ai-action="planning-cancel"]')).toBeVisible();
  await page.waitForTimeout(6200);
  await expect(page.locator('[data-ai-hook="planning-wait"]')).toContainText('响应较慢，但仍在工作');
  await expect(page.locator('#ai-chat-container')).toContainText('仍在工作，可取消');
  await capturePlanningEvidence(page, 'waiting-dark-1366.png');
  await expect(page.locator('#ai-input')).toBeVisible();
  await expect(page.locator('#ai-btn-start-build')).toHaveCount(0);
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
  const pendingGate = await page.evaluate(() => ({
    phase: (window as any).aiPanel.agentWorkspaceState.projection.phase,
    canBuild: (window as any).aiPanel.agentWorkspaceState.projection.buildAction.canBuild,
    applyStatus: (window as any).aiPanel.agentWorkspaceState.apply.status,
  }));
  expect(pendingGate).toEqual({ phase: 'idle', canBuild: false, applyStatus: 'idle' });

  releaseRouter();
  await expect(page.locator('[data-ai-hook="task-phase"]')).toHaveText('正在判断请求类型');
  await expect.poll(() => page.evaluate(() => (window as any).aiPanel.agentWorkspaceState.projection.phase)).toBe('routing');
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
});

test('real Plan Run events take over the same first-round planning lifecycle', async ({ page }) => {
  let releaseRouter!: () => void;
  let releasePlanStream!: () => void;
  let markRouterRequested!: () => void;
  let markPlanRunRequested!: () => void;
  const routerRequested = new Promise<void>(resolve => { markRouterRequested = resolve; });
  const routerReleased = new Promise<void>(resolve => { releaseRouter = resolve; });
  const planRunRequested = new Promise<void>(resolve => { markPlanRunRequested = resolve; });
  const planStreamReleased = new Promise<void>(resolve => { releasePlanStream = resolve; });
  const prompt = '读取产品上的DataMatrix二维码';
  const planResult = {
    planId: 'plan_datamatrix_waiting',
    planHash: 'sha256:datamatrix-waiting',
    originalUserPrompt: prompt,
    goal: '读取产品上的 DataMatrix 二维码',
    intent: 'code_recognition',
    confidence: 'high',
    requirementMode: 'strict',
    planSource: 'model_router',
    requirementUnderstanding: ['检测对象是产品上的 DataMatrix 码', '任务是读取码内容'],
    recommendedRoute: {
      routeId: 'datamatrix_reader',
      title: 'DataMatrix 读取流程',
      summary: '采集产品图像，定位并解码 DataMatrix，再输出结构化结果。',
      operators: ['ImageAcquisition', 'CodeRecognition', 'ResultOutput'],
    },
    clarificationQuestions: [{
      id: 'question-image-source',
      field: 'image_source',
      title: '图像从哪里获取？',
      why: '输入方式会影响采集算子和离线验证方式。',
      options: [
        { value: 'industrial_camera', label: '工业相机', recommended: true, answerEffect: 'resolve_field' },
        { value: 'image_folder', label: '图片目录', recommended: false, answerEffect: 'resolve_field' },
      ],
    }],
    missingResources: [{
      resourceKey: 'camera:primary',
      resourceType: 'camera',
      parameterName: 'CameraId',
      description: '相机资源待绑定',
    }],
    recommendedDefaults: [],
    risks: ['低对比度或污损码需要样本验证。'],
    acceptanceCriteria: ['可读码输出解码内容，不可读码按确认策略处理。'],
    executablePlan: ['采集图像', '定位并解码 DataMatrix', '输出读取结果'],
    canPlan: true,
    canBuild: false,
    nextAction: '确认图像输入方式后继续。',
    confirmedPlanAnswers: [
      { questionId: 'prompt-inspection-object', field: 'inspection_object', value: '产品上的 DataMatrix 码', origin: 'explicit_user_text', resolved: true },
      { questionId: 'prompt-task-type', field: 'task_type', value: 'code_recognition', origin: 'explicit_user_text', resolved: true },
    ],
    resolvedPlanFields: ['inspection_object', 'task_type'],
    remainingPlanFields: ['image_source'],
    buildReadiness: {
      canBuild: false,
      blockers: [
        { id: 'hard_requirement:image_source', category: 'hard_requirement', field: 'image_source', questionId: 'question-image-source', blocksBuild: true, resolutionMode: 'answer_question', publicLabel: '图像输入方式待确认' },
        { id: 'resource_pending:camera:primary', category: 'resource_pending', field: 'camera', questionId: '', blocksBuild: true, resolutionMode: 'provide_resource', publicLabel: '相机资源待绑定' },
      ],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: ['image_source', 'camera'],
      primaryMessage: '请先确认图像输入方式，并在后续补齐相机资源。',
      contractVersion: 'v2',
    },
    requirementMaturity: {
      maturity: 'needs_clarification',
      taskType: 'code_recognition',
      canPlan: true,
      canBuild: false,
      objectSignals: ['产品', 'DataMatrix'],
      taskSignals: ['读取'],
      missingFields: ['image_source'],
      blockingReasons: ['image_source'],
      publicReason: '任务与对象已明确，输入方式仍需确认。',
    },
    semanticExtraction: {
      isVisionRequest: true,
      source: 'model_router',
      taskType: 'code_recognition',
      confidence: 0.95,
      inspectionObject: '产品上的 DataMatrix 码',
      imageSource: 'industrial_camera',
      outputTarget: 'decoded_text',
      missingFields: ['image_source'],
    },
    publicEvents: [],
    metadataOnly: true,
  };

  await page.route('**/api/ai/agent-intent-router-runs', async route => {
    markRouterRequested();
    await routerReleased;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        intent: 'actionable_vision_plan',
        confidence: 'high',
        shouldOpenPlan: true,
        shouldBuildDirectly: false,
        canPlan: true,
        canBuild: false,
        needsClarification: true,
        publicReason: '任务与对象已明确，进入 Plan 核对输入方式。',
        assistantReply: '正在整理工程上下文并生成方案。',
        semanticExtraction: planResult.semanticExtraction,
        metadataOnly: true,
      }),
    });
  });
  await page.route('**/api/ai/agent-plan-runs', async route => {
    markPlanRunRequested();
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        runId: 'plan_datamatrix_waiting_run',
        events: [{
          runId: 'plan_datamatrix_waiting_run',
          sequence: 1,
          eventType: 'plan.context.started',
          stage: 'collecting_context',
          status: 'running',
          summary: '正在整理工程上下文。',
          payload: { metadataOnly: true },
        }],
      }),
    });
  });
  await page.route('**/api/ai/agent-runs/plan_datamatrix_waiting_run/events**', async route => {
    await planStreamReleased;
    const events = [
      { sequence: 2, eventType: 'plan.context.completed', stage: 'collecting_context', status: 'completed', summary: '工程上下文已整理。' },
      { sequence: 3, eventType: 'plan.model.started', stage: 'planning_with_model', status: 'running', summary: '正在生成方案。' },
      { sequence: 4, eventType: 'plan.model.completed', stage: 'planning_with_model', status: 'completed', summary: '方案候选已生成。' },
      { sequence: 5, eventType: 'plan.contract.started', stage: 'validating_plan_contract', status: 'running', summary: '正在校验方案。' },
      { sequence: 6, eventType: 'plan.safety.completed', stage: 'validating_plan_contract', status: 'completed', summary: '方案校验完成。' },
      { sequence: 7, eventType: 'run.completed', stage: 'run', status: 'completed', summary: '规划已完成。', payload: { planResult, metadataOnly: true } },
    ];
    await route.fulfill({
      status: 200,
      contentType: 'text/event-stream',
      body: events.map(event => [
        `event: ${event.eventType}`,
        `data: ${JSON.stringify({ runId: 'plan_datamatrix_waiting_run', ...event, payload: event.payload || { metadataOnly: true } })}`,
        '',
      ].join('\n')).join('\n') + '\n',
    });
  });

  await openAi(page, { width: 1366, height: 768 });
  await page.locator('#ai-input').fill(prompt);
  await page.locator('#ai-btn-gen').click();
  await routerRequested;
  await expect(page.locator('[data-planning-phase="understand"]')).toContainText('进行中');

  releaseRouter();
  await planRunRequested;
  await expect(page.locator('[data-planning-phase="understand"]')).toContainText('已完成');
  await expect(page.locator('[data-planning-phase="context"]')).toContainText('进行中');
  await expect(page.locator('.ai-agent-run-step[data-stage="plan:context"]')).toContainText('整理工程上下文');
  await expect(page.locator('[data-planning-phase="generate"]')).not.toContainText('已完成');
  await expect(page.locator('[data-planning-phase="validate"]')).not.toContainText('已完成');

  releasePlanStream();
  await expect(page.locator('[data-ai-hook="plan-recommendation"]')).toContainText('DataMatrix 读取流程');
  await expect(page.locator('[data-ai-hook="clarification-question"]')).toContainText('图像从哪里获取');
  await expect(page.locator('[data-ai-hook="clarification-resources"]')).toContainText('待补资源');
  await expect(page.locator('[data-ai-hook="clarification-workspace"] .ai-clarification-v2-header')).toContainText('还需确认 1 项');
  const confirmationFields = await page.evaluate(() =>
    (window as any).aiPanel.agentWorkspaceState.projection.confirmedAnswers.map((answer: any) => answer.field));
  expect(confirmationFields).toEqual(expect.arrayContaining(['inspection_object', 'task_type']));
  expect(confirmationFields).not.toContain('image_source');
  await focusPlanningClarification(page);
  await capturePlanningEvidence(page, 'datamatrix-plan-dark-1366.png');
});

test('fruit classification scene shows only route and judgment questions while model resource stays separate', async ({ page }) => {
  const prompt = '帮我构建一个超市水果标签识别的视觉流程，实现相机输入水果并输出水果类型';
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await page.evaluate(({ prompt }) => {
    const panel = (window as any).aiPanel;
    const plan = panel._normalizeBackendPlanResult({
      planId: 'plan_fruit_label_evidence',
      planHash: 'sha256:fruit-label-evidence',
      originalUserPrompt: prompt,
      goal: '识别超市水果标签并输出水果类型',
      intent: 'classification',
      confidence: 'high',
      requirementMode: 'strict',
      planSource: 'model_router',
      requirementUnderstanding: ['检测对象是水果', '相机输入', '输出水果类型'],
      recommendedRoute: {
        routeId: 'fruit_label_classification',
        title: '水果标签分类流程',
        summary: '采集水果图像，按确认的分类路线输出水果类型与置信度。',
        operators: ['ImageAcquisition', 'Classification', 'ResultOutput'],
      },
      clarificationQuestions: [
        {
          id: 'classification_strategy',
          field: 'algorithm_strategy',
          title: '类型识别采用哪条实现路线？',
          why: '分类模型与传统特征规则会改变算子链、样本要求和资源契约。',
          options: [
            { value: 'model_strategy', label: '分类模型', recommended: true, answerEffect: 'resolve_field' },
            { value: 'traditional_rule', label: '颜色 / 纹理规则', recommended: false, answerEffect: 'resolve_field' },
          ],
        },
        {
          id: 'ok_ng_rule',
          field: 'acceptance_criteria',
          title: '低置信度或无法分类时如何判定？',
          why: '判定口径会影响有效输出与异常分支。',
          options: [
            { value: 'reject_low_confidence', label: '低置信度标记未知', recommended: true, answerEffect: 'resolve_field' },
            { value: 'always_top1', label: '始终输出 Top-1', recommended: false, answerEffect: 'resolve_field' },
          ],
        },
      ],
      missingResources: [{
        resourceKey: 'model:fruit-classifier',
        resourceType: 'model_resource',
        parameterName: 'ModelPath',
        description: '水果分类模型待绑定',
      }],
      confirmedPlanAnswers: [
        { questionId: 'prompt-object', field: 'inspection_object', value: '水果', origin: 'explicit_user_text', resolved: true },
        { questionId: 'prompt-task', field: 'task_type', value: 'classification', origin: 'explicit_user_text', resolved: true },
        { questionId: 'prompt-source', field: 'image_source', value: 'station_camera', origin: 'explicit_user_text', resolved: true },
        { questionId: 'prompt-output', field: 'output_target', value: '水果类型', origin: 'explicit_user_text', resolved: true },
      ],
      resolvedPlanFields: ['inspection_object', 'task_type', 'image_source', 'output_target'],
      remainingPlanFields: ['algorithm_strategy', 'acceptance_criteria'],
      recommendedDefaults: [],
      risks: ['类别样本覆盖与光照变化需要在验证阶段复核。'],
      acceptanceCriteria: ['输出水果类型与置信度。'],
      executablePlan: ['采集图像', '执行分类', '输出水果类型'],
      canPlan: true,
      canBuild: false,
      nextAction: '确认分类路线和低置信度判定口径。',
      buildReadiness: {
        canBuild: false,
        blockers: [
          { id: 'hard_requirement:algorithm_strategy', category: 'hard_requirement', field: 'algorithm_strategy', questionId: 'classification_strategy', blocksBuild: true, resolutionMode: 'answer_question', publicLabel: '分类实现路线待确认' },
          { id: 'hard_requirement:acceptance_criteria', category: 'hard_requirement', field: 'acceptance_criteria', questionId: 'ok_ng_rule', blocksBuild: true, resolutionMode: 'answer_question', publicLabel: '判定口径待确认' },
          { id: 'resource_pending:model:fruit-classifier', category: 'resource_pending', field: 'model_resource', questionId: '', blocksBuild: true, resolutionMode: 'provide_resource', publicLabel: '水果分类模型待绑定' },
        ],
        resolvedFields: ['inspection_object', 'task_type', 'image_source', 'output_target'],
        remainingFields: ['algorithm_strategy', 'acceptance_criteria', 'model_resource'],
        primaryMessage: '请确认两项关键决策，模型资源在后续单独绑定。',
        contractVersion: 'v2',
      },
      requirementMaturity: {
        maturity: 'needs_clarification',
        taskType: 'classification',
        canPlan: true,
        canBuild: false,
        objectSignals: ['水果'],
        taskSignals: ['识别', '水果类型'],
        missingFields: ['algorithm_strategy', 'acceptance_criteria'],
        blockingReasons: ['algorithm_strategy', 'acceptance_criteria'],
        publicReason: '对象、输入和输出已明确，只需确认路线与判定口径。',
      },
      semanticExtraction: {
        isVisionRequest: true,
        source: 'model_router',
        taskType: 'classification',
        confidence: 0.96,
        inspectionObject: '水果',
        imageSource: '相机',
        outputTarget: '水果类型',
        okCondition: '最高置信类别有效',
        ngCondition: '低置信度无效',
        missingFields: ['algorithm_strategy', 'acceptance_criteria'],
      },
      publicEvents: [],
      metadataOnly: true,
    }, prompt);
    panel.pendingVisionPlan = plan;
    panel.agentWorkspaceMode = 'plan';
    panel._setWorkspaceViewMode('plan', { render: false });
    panel._addMessage('user', prompt);
    panel._addMessage('ai', '对象、相机输入和水果类型输出已明确，只核对会改变方案路线的关键决策。');
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(plan);
  }, { prompt });

  await expect(page.locator('.ai-clarification-v2-header')).toContainText('还需确认 2 项');
  await expect(page.locator('[data-ai-hook="clarification-question"]')).toContainText('类型识别采用哪条实现路线');
  await expect(page.locator('[data-ai-hook="clarification-resources"]')).toContainText('待补资源 · 1 项');
  await expect(page.locator('[data-ai-hook="clarification-resources"]')).toContainText('水果分类模型待绑定');
  await focusPlanningClarification(page);
  await capturePlanningEvidence(page, 'fruit-plan-light-1366.png');
});

test('planning cancellation, request failure, timeout and retry stay in one recoverable lifecycle', async ({ page }) => {
  let requestCount = 0;
  let releaseFirstRouter!: () => void;
  let markFirstRouterRequested!: () => void;
  let markSecondRouterRequested!: () => void;
  const firstRouterRequested = new Promise<void>(resolve => { markFirstRouterRequested = resolve; });
  const secondRouterRequested = new Promise<void>(resolve => { markSecondRouterRequested = resolve; });
  const firstRouterReleased = new Promise<void>(resolve => { releaseFirstRouter = resolve; });

  await page.route('**/api/ai/agent-intent-router-runs', async route => {
    requestCount += 1;
    if (requestCount === 1) {
      markFirstRouterRequested();
      await firstRouterReleased;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          intent: 'ambiguous_vision_requirement',
          confidence: 'medium',
          shouldOpenPlan: false,
          shouldBuildDirectly: false,
          canPlan: true,
          canBuild: false,
          needsClarification: true,
          publicReason: '请求已由用户取消。',
          assistantReply: '请求已由用户取消。',
          metadataOnly: true,
        }),
      });
      return;
    }

    markSecondRouterRequested();
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ message: 'router temporarily unavailable' }),
    });
  });

  await openAi(page, { width: 1366, height: 768 });
  await page.locator('#ai-input').fill('帮我创建一个水果识别流程');
  await page.locator('#ai-btn-gen').click();
  await firstRouterRequested;
  await page.locator('[data-ai-action="planning-cancel"]').click();
  await expect(page.locator('[data-planning-phase="understand"]')).toContainText('已取消');
  await expect(page.locator('[data-ai-action="planning-retry"]')).toBeVisible();
  expect(await page.evaluate(() => (window as any).aiPanel.isCancellingGenerate)).toBe(false);

  releaseFirstRouter();
  await page.locator('[data-ai-action="planning-retry"]').click();
  await secondRouterRequested;
  await expect(page.locator('[data-planning-phase="understand"]')).toContainText('失败');
  await expect(page.locator('[data-ai-action="planning-retry"]')).toBeVisible();

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel._beginPlanningLifecycle({
      requestId: 'timeout-visual-check',
      requestContext: panel.lastPlanningRequestContext,
      turn: panel.activeAssistantTurn,
      phase: 'understand',
    });
    panel._markPlanningLifecycleTerminal('timeout', '规划等待超时，可重试本次需求。');
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(null);
  });
  await expect(page.locator('[data-planning-phase="understand"]')).toContainText('超时');
  await expect(page.locator('[data-ai-action="planning-retry"]')).toBeVisible();
});

test('first-round planning stays readable at 390px with reduced motion and both themes', async ({ page }) => {
  let releaseRouter!: () => void;
  let markRouterRequested!: () => void;
  const routerRequested = new Promise<void>(resolve => { markRouterRequested = resolve; });
  const routerReleased = new Promise<void>(resolve => { releaseRouter = resolve; });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.route('**/api/ai/agent-intent-router-runs', async route => {
    markRouterRequested();
    await routerReleased;
    await route.fulfill({ status: 499, contentType: 'application/json', body: '{}' });
  });

  await openAi(page, { width: 390, height: 844, theme: 'dark' });
  await page.locator('#ai-input').fill('读取产品上的DataMatrix二维码');
  await page.locator('#ai-btn-gen').click();
  await routerRequested;
  await expect(page.locator('.ai-planning-stages li')).toHaveCount(4);
  await expect(page.locator('[data-ai-action="planning-cancel"]')).toBeVisible();
  const reducedMotionState = await page.locator('[data-ai-hook="planning-wait"]').evaluate(element => {
    const style = getComputedStyle(element.querySelector('.ai-planning-stages li')!);
    return {
      transitionDuration: style.transitionDuration,
      documentOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      bodyOverflow: document.body.scrollWidth - document.body.clientWidth,
    };
  });
  expect(reducedMotionState.transitionDuration).toBe('0s');
  expect(reducedMotionState.documentOverflow).toBeLessThanOrEqual(1);
  expect(reducedMotionState.bodyOverflow).toBeLessThanOrEqual(1);

  await page.evaluate(() => { document.documentElement.dataset.theme = 'light'; });
  await expect(page.locator('[data-ai-hook="planning-wait"]')).toBeVisible();
  await expect(page.locator('[data-planning-phase="understand"]')).toContainText('进行中');
  await capturePlanningEvidence(page, 'waiting-light-reduced-390.png');
  releaseRouter();
});

test.describe('150% DPI planning evidence', () => {
  test.use({ deviceScaleFactor: 1.5 });

  test('first-round planning has no clipping at 150% device scale', async ({ page }) => {
    let releaseRouter!: () => void;
    let markRouterRequested!: () => void;
    const routerRequested = new Promise<void>(resolve => { markRouterRequested = resolve; });
    const routerReleased = new Promise<void>(resolve => { releaseRouter = resolve; });
    await page.route('**/api/ai/agent-intent-router-runs', async route => {
      markRouterRequested();
      await routerReleased;
      await route.fulfill({ status: 499, contentType: 'application/json', body: '{}' });
    });

    await openAi(page, { width: 1024, height: 768, theme: 'dark' });
    await page.locator('#ai-input').fill('读取产品上的DataMatrix二维码');
    await page.locator('#ai-btn-gen').click();
    await routerRequested;
    const dpiState = await page.evaluate(() => ({
      devicePixelRatio: window.devicePixelRatio,
      documentOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      bodyOverflow: document.body.scrollWidth - document.body.clientWidth,
    }));
    expect(dpiState.devicePixelRatio).toBe(1.5);
    expect(dpiState.documentOverflow).toBeLessThanOrEqual(1);
    expect(dpiState.bodyOverflow).toBeLessThanOrEqual(1);
    await expect(page.locator('.ai-planning-stages li')).toHaveCount(4);
    await capturePlanningEvidence(page, 'waiting-dark-1024-dpi150.png');
    releaseRouter();
  });
});

test('legacy Result-only restore stays active while Apply remains governed by the existing gate', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  await restoreLegacySession(page);

  await expect(page.locator('[data-ai-hook="shell"]')).toHaveAttribute('data-ai-shell-state', 'active');
  await expect(page.locator('#ai-chat-container')).toContainText('历史构建结果已恢复');
  await expect(page.locator('[data-ai-hook="workbench-pane"]')).toBeVisible();
  await expect(page.locator('#ai-result-summary')).not.toHaveText('--');
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
  const restored = await page.evaluate(() => ({
    plan: (window as any).aiPanel.agentWorkspaceState.plan,
    projectionPhase: (window as any).aiPanel.agentWorkspaceState.projection.phase,
    applyStatus: (window as any).aiPanel.agentWorkspaceState.apply.status,
  }));
  expect(restored).toEqual({ plan: null, projectionPhase: 'idle', applyStatus: 'idle' });
  await expect(page.locator('[data-ai-hook="idle-intro"]')).toBeHidden();
});

test('message-only restore activates conversation without inventing a result or primary action', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  await restoreLegacySession(page, { messageOnly: true });

  await expect(page.locator('[data-ai-hook="shell"]')).toHaveAttribute('data-ai-shell-state', 'active');
  await expect(page.locator('#ai-chat-container')).toContainText('恢复这条只有历史消息的会话');
  await expect(page.locator('[data-ai-hook="task-primary-action"] button')).toHaveCount(0);
  await expect(page.locator('#ai-result-summary')).toHaveText('--');
  await expect(page.locator('[data-ai-hook="idle-intro"]')).toBeHidden();
});

test('event-only Result and RESET synchronize the shell without changing canonical gates', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  const before = await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    const gate = structuredClone(panel.agentWorkspaceState.projection.buildAction);
    const apply = structuredClone(panel.agentWorkspaceState.apply);
    panel._dispatchAgentWorkspaceEvent({
      type: 'workspace/result-received',
      payload: { result: { aiExplanation: 'Result-only canonical payload' } },
    });
    return { gate, apply };
  });
  await expect(page.locator('[data-ai-hook="shell"]')).toHaveAttribute('data-ai-shell-state', 'active');
  const afterResult = await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    return {
      phase: panel.agentWorkspaceState.projection.phase,
      gate: panel.agentWorkspaceState.projection.buildAction,
      apply: panel.agentWorkspaceState.apply,
    };
  });
  expect(afterResult.phase).toBe('idle');
  expect(afterResult.gate).toEqual(before.gate);
  expect(afterResult.apply).toEqual(before.apply);

  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.lastUserPrompt = '';
    panel.activeGenerateRequestId = null;
    panel.activeIntentRouterRequestId = null;
    panel.isGenerating = false;
    panel._dispatchAgentWorkspaceEvent({ type: 'workspace/reset', payload: { preserveSession: true } });
  });
  await expect(page.locator('[data-ai-hook="shell"]')).toHaveAttribute('data-ai-shell-state', 'idle');
  await expect(page.locator('[data-ai-hook="workbench-pane"]')).toBeHidden();
  await expect(page.locator('[data-ai-hook="idle-intro"]')).toBeVisible();
});

test('repeated shell synchronization does not duplicate recent-task navigation', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.history = [{
      sessionId: 'recent-once',
      lastMessage: '只切换一次的历史任务',
      updatedAtUtc: '2026-07-11T08:00:00.000Z',
      turnCount: 2,
    }];
    panel.__shellSwitchCount = 0;
    panel._switchToSession = () => { panel.__shellSwitchCount += 1; };
    panel._renderHistoryList();
    panel._renderHistoryList();
    panel._renderAgentWorkspaceOverview();
  });
  await page.locator('[data-ai-hook="idle-recent-item"]').click();
  await expect.poll(() => page.evaluate(() => (window as any).aiPanel.__shellSwitchCount)).toBe(1);
});

test('active desktop shell keeps workbench left, conversation right, and one primary action', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  await seedActivePlan(page);

  const geometry = await page.evaluate(() => {
    const workbench = document.querySelector('[data-ai-hook="workbench-pane"]')!.getBoundingClientRect();
    const conversation = document.querySelector('[data-ai-hook="conversation-pane"]')!.getBoundingClientRect();
    const backgroundImages = [
      '#ai-view',
      '[data-ai-hook="workspace"]',
      '[data-ai-hook="workbench-pane"]',
      '[data-ai-hook="conversation-pane"]',
      '#ai-chat-container',
      '#ai-plan-workspace',
    ].map(selector => ({
      selector,
      value: getComputedStyle(document.querySelector(selector)!).backgroundImage,
    }));
    const backgroundColors = [
      '#ai-view',
      '[data-ai-hook="workspace"]',
      '[data-ai-hook="workbench-pane"]',
      '[data-ai-hook="conversation-pane"]',
      '#ai-chat-container',
      '#ai-plan-workspace',
    ].map(selector => ({
      selector,
      value: getComputedStyle(document.querySelector(selector)!).backgroundColor,
    }));
    return {
      workbenchLeft: workbench.left,
      workbenchRight: workbench.right,
      conversationLeft: conversation.left,
      inputVisible: Boolean(document.querySelector('#ai-input')?.getBoundingClientRect().height),
      backgroundImages,
      backgroundColors,
    };
  });
  expect(geometry.workbenchLeft).toBeLessThan(geometry.conversationLeft);
  expect(geometry.workbenchRight).toBeLessThanOrEqual(geometry.conversationLeft + 1);
  expect(geometry.inputVisible).toBe(true);
  expect(geometry.backgroundImages).toEqual([
    { selector: '#ai-view', value: 'none' },
    { selector: '[data-ai-hook="workspace"]', value: 'none' },
    { selector: '[data-ai-hook="workbench-pane"]', value: 'none' },
    { selector: '[data-ai-hook="conversation-pane"]', value: 'none' },
    { selector: '#ai-chat-container', value: 'none' },
    { selector: '#ai-plan-workspace', value: 'none' },
  ]);
  expect(geometry.backgroundColors.every(item => item.value !== 'rgba(0, 0, 0, 0)')).toBe(true);
  await expect(page.locator('#ai-btn-start-build')).toHaveCount(1);
  await expect(page.locator('#ai-btn-start-build')).toBeVisible();
  await expect(page.locator('[data-ai-hook="task-context"]')).toBeVisible();
  await expectNoPageOverflow(page);
});

for (const viewport of [
  { width: 1920, height: 1080 },
  { width: 1366, height: 768 },
  { width: 1280, height: 720 },
  { width: 1024, height: 768 },
  { width: 390, height: 844 },
]) {
  test(`AI shell keeps actions reachable without page overflow at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await openAi(page, viewport);
    await seedActivePlan(page);
    await expect(page.locator('#ai-btn-start-build')).toBeVisible();

    if (viewport.width < 1180) {
      await expect(page.locator('[data-ai-hook="compact-tabs"]')).toBeVisible();
      await switchCompactPane(page, 'conversation');
    }
    await expect(page.locator('#ai-input')).toBeVisible();
    await expectNoPageOverflow(page);
  });
}

test('idle dark visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await expect(page.locator('#ai-view')).toHaveScreenshot('idle-dark-1366.png');
});

test('active dark visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedActivePlan(page);
  await expect(page.locator('#ai-view')).toHaveScreenshot('active-dark-1366.png');
});

test('active light visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedActivePlan(page);
  const lightSurfaces = await page.evaluate(() => [
    '#ai-view',
    '#ai-chat-container',
    '#ai-plan-workspace',
  ].map(selector => {
    const style = getComputedStyle(document.querySelector(selector)!);
    return { selector, backgroundImage: style.backgroundImage, backgroundColor: style.backgroundColor };
  }));
  expect(lightSurfaces.every(item => item.backgroundImage === 'none')).toBe(true);
  expect(lightSurfaces.every(item => item.backgroundColor !== 'rgba(0, 0, 0, 0)')).toBe(true);
  await expect(page.locator('#ai-view')).toHaveScreenshot('active-light-1366.png');
});

test('compact dark visual baseline at 1024', async ({ page }) => {
  await openAi(page, { width: 1024, height: 768, theme: 'dark' });
  await seedActivePlan(page);
  await expect(page.locator('[data-ai-hook="compact-tabs"]')).toBeVisible();
  await expect(page.locator('#ai-view')).toHaveScreenshot('compact-dark-1024.png');
});

test('narrow dark visual baseline at 390', async ({ page }) => {
  await openAi(page, { width: 390, height: 844, theme: 'dark' });
  await seedActivePlan(page);
  await switchCompactPane(page, 'conversation');
  await expect(page.locator('#ai-input')).toBeVisible();
  await expect(page.locator('#ai-view')).toHaveScreenshot('narrow-dark-390.png');
});
