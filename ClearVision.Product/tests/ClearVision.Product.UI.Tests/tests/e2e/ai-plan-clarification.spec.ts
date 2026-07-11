import { test, expect, Page } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

type Theme = 'dark' | 'light';

const basePlan = {
  planId: 'plan_phase_two',
  planHash: 'sha256:phase-two',
  originalUserPrompt: '检测包装箱表面的破损并输出 OK/NG',
  goal: '包装箱外观缺陷检测',
  intent: 'surface_defect',
  confidence: 'high',
  requirementMode: 'strict',
  planSource: 'model_router',
  operatorCatalogVersion: 'catalog-2026.07',
  templateCatalogVersion: 'templates-2026.07',
  stationBoundarySummary: '工作站只接收已确认的流程草稿。',
  requirementUnderstanding: ['检测对象是包装箱表面', '目标是识别破损并输出 OK/NG'],
  recommendedRoute: {
    routeId: 'packaging_surface_phase_two',
    title: '包装箱外观检测流程',
    summary: '采集图像后限定检测区域，完成表面破损检测并输出判定。',
    operators: ['ImageAcquisition', 'RoiManager', 'SurfaceDefectDetection', 'ResultOutput'],
  },
  recommendedDefaults: [
    { field: 'image_source', label: '图像来源', value: 'industrial_camera', impact: '使用现场工业相机。' },
    { field: 'output_target', label: '输出目标', value: 'ok_ng', impact: '输出结构化 OK/NG。' },
  ],
  risks: ['现场光照变化需要在验证阶段复核'],
  acceptanceCriteria: ['破损样本输出 NG', '完整样本输出 OK'],
  executablePlan: ['采集图像', '限定检测区域', '检测破损', '输出结果'],
  canPlan: true,
  canBuild: false,
  nextAction: '确认关键问题后开始构建。',
  semanticExtraction: {
    isVisionRequest: true,
    source: 'model_router',
    taskType: 'surface_defect',
    confidence: 0.94,
    inspectionObject: '包装箱表面',
    defectType: '破损',
    imageSource: '',
    okCondition: '包装箱表面完整',
    ngCondition: '存在可见破损',
    outputTarget: 'OK/NG',
    missingFields: ['image_source'],
  },
  requirementMaturity: {
    maturity: 'needs_clarification',
    taskType: 'surface_defect',
    canPlan: true,
    canBuild: false,
    objectSignals: ['包装箱表面'],
    taskSignals: ['破损', 'OK/NG'],
    missingFields: ['image_source'],
    blockingReasons: ['image_source'],
    publicReason: '需要确认图像来源。',
  },
  publicEvents: [],
  metadataOnly: true,
};

const imageSourceQuestion = {
  id: 'question-image-source',
  field: 'image_source',
  title: '图像从哪里获取？',
  why: '图像来源会影响采集算子和验证方式。',
  options: [
    {
      value: 'industrial_camera',
      label: '工业相机',
      recommended: true,
      answerEffect: 'resolve_field',
      description: '使用现场相机作为稳定输入。',
      impact: '构建将包含图像采集算子。',
    },
    {
      value: 'image_folder',
      label: '图片目录',
      recommended: false,
      answerEffect: 'resolve_field',
      description: '先使用离线图片验证。',
      impact: '适合离线验收，不代表现场相机已就绪。',
    },
  ],
};

const outputQuestion = {
  id: 'question-output-target',
  field: 'output_target',
  title: '检测结果输出到哪里？',
  why: '输出目标决定结果算子的结构。',
  options: [
    {
      value: 'ok_ng',
      label: 'OK / NG',
      recommended: true,
      answerEffect: 'resolve_field',
      description: '输出稳定的二值判定。',
      impact: '适合工站和 PLC 后续消费。',
    },
    {
      value: 'structured_result',
      label: '结构化结果',
      recommended: false,
      answerEffect: 'resolve_field',
      description: '同时输出缺陷类型和位置。',
      impact: '结果结构更丰富，需要下游适配。',
    },
  ],
};

async function mockBaseApis(page: Page, theme: Theme): Promise<void> {
  await page.route('**/api/settings', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ general: { softwareTitle: 'ClearVision', theme, autoStart: false } }),
  }));
  await page.route('**/api/operators/types', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.route('**/api/operators/library', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.route('**/api/projects**', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.route('**/api/health', route => route.fulfill({ status: 200, contentType: 'application/json', body: '{"ok":true}' }));
}

async function openAi(page: Page, options: { width: number; height: number; theme?: Theme }): Promise<void> {
  const theme = options.theme ?? 'dark';
  await page.setViewportSize({ width: options.width, height: options.height });
  await mockBaseApis(page, theme);
  await bootAuthenticatedApp(page);
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

async function seedPlan(page: Page, options: {
  ready?: boolean;
  questions?: 'none' | 'single' | 'multiple';
  confirming?: boolean;
  resourcePending?: boolean;
} = {}): Promise<void> {
  await page.evaluate(({ rawPlan, imageQuestion, outputTargetQuestion, options }) => {
    const panel = (window as any).aiPanel;
    const questions = options.questions === 'none'
      ? []
      : options.questions === 'single'
        ? [imageQuestion]
        : [imageQuestion, outputTargetQuestion];
    const blockers = options.ready ? [] : questions.map((question: any) => ({
      id: `blocker:${question.field}`,
      category: 'hard_requirement',
      field: question.field,
      questionId: question.id,
      blocksBuild: true,
      resolutionMode: 'answer_question',
      publicLabel: `请确认${question.title}`,
    }));
    if (options.resourcePending) {
      blockers.push({
        id: 'resource:model_asset',
        category: 'resource_pending',
        field: 'model_asset',
        blocksBuild: true,
        resolutionMode: 'provide_resource',
        publicLabel: '缺陷检测模型待补齐',
      });
    }
    const plan = panel._normalizeBackendPlanResult({
      ...rawPlan,
      semanticExtraction: {
        ...rawPlan.semanticExtraction,
        imageSource: options.ready ? '工业相机' : rawPlan.semanticExtraction.imageSource,
        missingFields: options.ready ? [] : rawPlan.semanticExtraction.missingFields,
      },
      canBuild: options.ready === true,
      clarificationQuestions: questions,
      buildReadiness: {
        canBuild: options.ready === true,
        blockers,
        resolvedFields: options.ready
          ? ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'output_target']
          : ['inspection_object', 'task_type', 'acceptance_criteria'],
        remainingFields: blockers.map((item: any) => item.field),
        primaryMessage: options.ready ? '方案信息完整，可以开始构建。' : '请先确认关键问题。',
        contractVersion: 'v2',
      },
    }, rawPlan.originalUserPrompt);
    panel.pendingVisionPlan = plan;
    panel.agentWorkspaceMode = 'plan';
    panel._setWorkspaceViewMode('plan', { render: false });
    panel._addMessage('user', rawPlan.originalUserPrompt);
    panel._addMessage('ai', options.ready ? '方案已准备好，可复核后开始构建。' : '已形成方案，请确认关键问题。');

    if (options.confirming && questions.length) {
      const question = questions[0];
      const answer = {
        questionId: question.id,
        field: question.field,
        value: question.options[0].value,
        origin: 'explicit_user_selection',
      };
      panel._dispatchAgentWorkspaceEvent({
        type: 'workspace/selection-set',
        payload: { questionId: question.id, value: answer.value },
      });
      panel._dispatchAgentWorkspaceEvent({
        type: 'workspace/answer-optimistic-set',
        payload: { answer, question },
      });
      panel._dispatchAgentWorkspaceEvent({
        type: 'workspace/readiness-requested',
        payload: {
          planId: plan.planId,
          planHash: plan.planHash,
          answerRevision: panel.planAnswerRevision,
          requirementMode: 'strict',
        },
      });
    }
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(plan);
    panel._renderBuildWorkspaceFromAgentRun();
  }, {
    rawPlan: basePlan,
    imageQuestion: imageSourceQuestion,
    outputTargetQuestion: outputQuestion,
    options: {
      ready: options.ready === true,
      questions: options.questions ?? 'multiple',
      confirming: options.confirming === true,
      resourcePending: options.resourcePending === true,
    },
  });
  await expect(page.locator('[data-ai-hook="shell"]')).toHaveAttribute('data-ai-shell-state', 'active');
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

async function focusClarificationViewport(page: Page): Promise<void> {
  await page.evaluate(() => {
    const pane = document.querySelector('#ai-plan-workspace') as HTMLElement | null;
    const clarification = document.querySelector('[data-ai-hook="clarification-workspace"]') as HTMLElement | null;
    if (!pane || !clarification) return;
    const paneRect = pane.getBoundingClientRect();
    const clarificationRect = clarification.getBoundingClientRect();
    pane.scrollTop += clarificationRect.top - paneRect.top - 18;
  });
}

test('Plan-ready workspace shows understanding and recommendation while preserving the canonical Build gate', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { ready: true, questions: 'none' });

  await expect(page.locator('[data-ai-hook="plan-understanding"]')).toContainText('包装箱表面');
  await expect(page.locator('[data-ai-hook="plan-recommendation"]')).toContainText('包装箱外观检测流程');
  await expect(page.locator('.ai-plan-v2-sequence li')).toHaveCount(4);
  await expect(page.locator('[data-ai-hook="clarification-workspace"]')).toHaveCount(0);
  await expect(page.locator('[data-ai-hook="clarification-ready"]')).toContainText('方案已就绪');
  await expect(page.locator('#ai-btn-start-build')).toHaveCount(1);
  await expect(page.locator('#ai-btn-start-build')).toBeEnabled();
});

test('方案 and 构建与验证 switch bidirectionally without resetting Build evidence', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { ready: true, questions: 'none' });
  await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.activeAgentRunId = 'run-phase-two';
    panel.activeAgentRunEvents = [
      { sequence: 1, stage: 'planning', status: 'completed', eventType: 'stage.completed', summary: '方案快照已接收' },
      { sequence: 2, stage: 'validation', status: 'completed', eventType: 'stage.completed', summary: 'DryRun 与参数校验已完成' },
    ];
    panel.agentWorkspaceMode = 'build';
    panel.currentResult = {
      buildResult: { runId: 'run-phase-two' },
      flow: { operators: [{ id: 'camera', type: 'ImageAcquisition' }], connections: [] },
    };
    panel._setWorkspaceViewMode('build', { render: false });
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(panel.pendingVisionPlan);
    panel._renderBuildWorkspaceFromAgentRun();
  });

  const planTab = page.locator('[data-workspace-view-mode="plan"]');
  const buildTab = page.locator('[data-workspace-view-mode="build"]');
  await expect(planTab).toContainText('方案');
  await expect(buildTab).toContainText('构建与验证');
  await expect(buildTab).toBeEnabled();
  await expect(page.locator('#ai-build-workspace')).toBeVisible();
  await expect(page.locator('#ai-build-event-timeline')).toContainText('DryRun 与参数校验已完成');
  await expect(page.locator('#ai-result-parameter-editor')).toHaveCount(1);
  await expect(page.locator('#ai-result-followups')).toHaveCount(1);
  await expect(page.locator('#ai-result-validation')).toHaveCount(1);
  await expect(page.locator('#ai-btn-apply')).toHaveCount(1);

  await planTab.click();
  await expect(page.locator('#ai-plan-workspace')).toBeVisible();
  await expect(page.locator('#ai-build-workspace')).toBeHidden();
  await expect(page.locator('[data-ai-hook="plan-recommendation"]')).toContainText('包装箱外观检测流程');

  await buildTab.click();
  await expect(page.locator('#ai-build-workspace')).toBeVisible();
  await expect(page.locator('#ai-build-event-timeline')).toContainText('DryRun 与参数校验已完成');
  await expect(page.locator('#ai-btn-apply')).toHaveCount(1);
});

test('single and multiple canonical questions render one accessible focal question', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { questions: 'multiple' });

  await expect(page.locator('[data-ai-hook="clarification-question"]')).toHaveCount(1);
  await expect(page.locator('[data-ai-hook="clarification-title"]')).toHaveText('图像从哪里获取？');
  await expect(page.locator('.ai-clarification-v2-header')).toContainText('还需确认 2 项 · 当前第 1 项');
  await expect(page.locator('.ai-clarification-v2-recommended')).toHaveText('推荐');
  await expect(page.locator('.ai-clarification-v2-option')).toContainText(['构建将包含图像采集算子。', '适合离线验收，不代表现场相机已就绪。']);
  await expect(page.locator('#ai-btn-start-build')).toBeDisabled();
});

test('selection stays confirming until canonical readiness confirms it, then advances focus', async ({ page }) => {
  let release!: () => void;
  let requested!: () => void;
  const requestSeen = new Promise<void>(resolve => { requested = resolve; });
  const responseGate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/ai/agent-plan/readiness-preview', async route => {
    const request = route.request().postDataJSON();
    requested();
    await responseGate;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        planId: request.planId,
        planHash: request.planHash,
        requirementMode: request.requirementMode,
        answerRevision: request.answerRevision,
        acceptedAnswers: [{
          questionId: imageSourceQuestion.id,
          field: imageSourceQuestion.field,
          value: 'industrial_camera',
          origin: 'explicit_user_selection',
        }],
        buildReadiness: {
          canBuild: false,
          blockers: [{
            id: 'blocker:output_target',
            category: 'hard_requirement',
            field: 'output_target',
            questionId: outputQuestion.id,
            blocksBuild: true,
            resolutionMode: 'answer_question',
            publicLabel: '请确认输出目标',
          }],
          resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
          remainingFields: ['output_target'],
          primaryMessage: '请继续确认输出目标。',
          contractVersion: 'v2',
        },
        contractValid: true,
        metadataOnly: true,
      }),
    });
  });
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { questions: 'multiple' });

  await page.locator('input[value="industrial_camera"]').check();
  await requested;
  await expect(page.locator('[data-ai-hook="clarification-question"]')).toContainText('正在等待权威 Readiness 确认');
  await expect(page.locator('input[value="industrial_camera"]')).toBeDisabled();
  await expect(page.locator('#ai-btn-start-build')).toBeDisabled();

  release();
  await expect(page.locator('[data-ai-hook="clarification-title"]')).toHaveText('检测结果输出到哪里？');
  await expect(page.locator('[data-ai-hook="clarification-confirmed"]')).toContainText('图像从哪里获取？：工业相机');
  await expect(page.locator('[data-ai-hook="clarification-title"]')).toBeFocused();
  await expect(page.locator('#ai-btn-start-build')).toBeDisabled();
});

test('Readiness failure restores options and leaves Build closed', async ({ page }) => {
  await page.route('**/api/ai/agent-plan/readiness-preview', route => route.fulfill({
    status: 500,
    contentType: 'application/json',
    body: JSON.stringify({ message: 'Readiness 暂时不可用' }),
  }));
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { questions: 'single' });

  await page.locator('input[value="industrial_camera"]').check();
  await expect(page.locator('.ai-clarification-v2-status.is-error')).toBeVisible();
  await expect(page.locator('input[value="industrial_camera"]')).toBeEnabled();
  await expect(page.locator('#ai-btn-start-build')).toBeDisabled();
  const gate = await page.evaluate(() => (window as any).aiPanel.agentWorkspaceState.projection.buildAction.canStart);
  expect(gate).toBe(false);
});

test('manual supplement uses explicit_user_text and does not copy the main Composer', async ({ page }) => {
  let release!: () => void;
  const responseGate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/ai/agent-plan/readiness-preview', async route => {
    await responseGate;
    await route.abort();
  });
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { questions: 'single' });

  await page.locator('[data-ai-action="clarification-other"]').click();
  await page.locator('[data-ai-hook="clarification-manual-input"]').fill('使用 GigE 工业相机');
  await page.locator('[data-ai-action="clarification-manual-submit"]').click();
  const answer = await page.evaluate(() => (window as any).aiPanel.agentWorkspaceState.answers.optimisticByField.image_source);
  expect(answer).toMatchObject({ value: '使用 GigE 工业相机', origin: 'explicit_user_text' });
  await expect(page.locator('#ai-input')).toHaveCount(1);
  await expect(page.locator('[data-ai-hook="clarification-question"]')).toContainText('正在等待权威 Readiness 确认');
  release();
});

test('accept-all uses the existing recommended-answer path only for eligible questions', async ({ page }) => {
  let release!: () => void;
  const responseGate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/ai/agent-plan/readiness-preview', async route => {
    await responseGate;
    await route.abort();
  });
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { questions: 'multiple' });

  await page.locator('[data-ai-action="clarification-accept-recommended"]').click();
  const answers = await page.evaluate(() => (window as any).aiPanel.agentWorkspaceState.answers.optimisticByField);
  expect(answers.image_source.origin).toBe('accepted_recommended_default');
  expect(answers.output_target.origin).toBe('accepted_recommended_default');
  await expect(page.locator('#ai-btn-start-build')).toBeDisabled();
  release();
});

test('stale readiness response cannot overwrite a newer canonical selection', async ({ page }) => {
  const pendingRoutes: Array<{ request: any; route: any }> = [];
  await page.route('**/api/ai/agent-plan/readiness-preview', async route => {
    pendingRoutes.push({ request: route.request().postDataJSON(), route });
  });
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { questions: 'single' });

  await page.locator('input[value="industrial_camera"]').check();
  await expect.poll(() => pendingRoutes.length).toBe(1);
  await page.evaluate(question => {
    const panel = (window as any).aiPanel;
    panel._selectPlanQuestionOption(question.id, 'image_folder');
    panel._requestPlanReadinessPreview(panel.pendingVisionPlan, { reason: 'newer_selection' });
    panel._renderPlanWorkspace(panel.pendingVisionPlan);
  }, imageSourceQuestion);
  await expect.poll(() => pendingRoutes.length).toBe(2);

  const newer = pendingRoutes[1];
  await newer.route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      planId: newer.request.planId,
      planHash: newer.request.planHash,
      requirementMode: newer.request.requirementMode,
      answerRevision: newer.request.answerRevision,
      acceptedAnswers: [{
        questionId: imageSourceQuestion.id,
        field: imageSourceQuestion.field,
        value: 'image_folder',
        origin: 'explicit_user_selection',
      }],
      buildReadiness: {
        canBuild: true,
        blockers: [],
        resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'output_target'],
        remainingFields: [],
        primaryMessage: '方案可构建。',
        contractVersion: 'v2',
      },
      contractValid: true,
      metadataOnly: true,
    }),
  });
  await expect.poll(() => page.evaluate(() =>
    (window as any).aiPanel.agentWorkspaceState.answers.confirmedByField.image_source?.value)).toBe('image_folder');

  const stale = pendingRoutes[0];
  await stale.route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      planId: stale.request.planId,
      planHash: stale.request.planHash,
      requirementMode: stale.request.requirementMode,
      answerRevision: stale.request.answerRevision,
      acceptedAnswers: [{
        questionId: imageSourceQuestion.id,
        field: imageSourceQuestion.field,
        value: 'industrial_camera',
        origin: 'explicit_user_selection',
      }],
      buildReadiness: { canBuild: false, blockers: [], resolvedFields: [], remainingFields: [], primaryMessage: 'stale', contractVersion: 'v2' },
      contractValid: true,
      metadataOnly: true,
    }),
  }).catch(() => {});
  await page.waitForTimeout(50);
  const finalAnswer = await page.evaluate(() =>
    (window as any).aiPanel.agentWorkspaceState.answers.confirmedByField.image_source?.value);
  expect(finalAnswer).toBe('image_folder');
});

test('confirmed summary can reopen the canonical question for modification', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { questions: 'single' });
  await page.evaluate(question => {
    const panel = (window as any).aiPanel;
    panel._dispatchAgentWorkspaceEvent({
      type: 'workspace/answers-confirmed',
      payload: { answers: [{ questionId: question.id, field: question.field, value: 'industrial_camera', origin: 'explicit_user_selection' }] },
    });
    panel._renderPlanWorkspace(panel.pendingVisionPlan);
  }, imageSourceQuestion);

  await expect(page.locator('[data-ai-hook="clarification-confirmed"]')).toContainText('工业相机');
  await page.locator('[data-ai-action="clarification-edit"]').click();
  await expect(page.locator('[data-ai-hook="clarification-title"]')).toHaveText('图像从哪里获取？');
  await expect(page.locator('[data-ai-hook="clarification-title"]')).toBeFocused();
});

test('Router text does not create a second question and resource pending stays lightweight', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { questions: 'single', resourcePending: true });
  await page.evaluate(() => {
    (window as any).aiPanel.pendingClarificationPayload = {
      questions: [{ id: 'legacy-router', title: '旧 Router 澄清问题' }],
    };
    (window as any).aiPanel._renderPlanWorkspace((window as any).aiPanel.pendingVisionPlan);
  });

  await expect(page.locator('[data-ai-hook="clarification-question"]')).toHaveCount(1);
  await expect(page.locator('#ai-plan-workspace')).not.toContainText('旧 Router 澄清问题');
  await expect(page.locator('[data-ai-hook="clarification-resources"]')).toContainText('完整资源补齐将在后续阶段处理');
  await expect(page.locator('#ai-plan-workspace input[type="file"]')).toHaveCount(0);
  await expect(page.locator('#ai-plan-workspace [data-resource-action]')).toHaveCount(0);
});

test('keyboard path reaches options and keeps the current action unique', async ({ page }) => {
  let release!: () => void;
  const responseGate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/ai/agent-plan/readiness-preview', async route => {
    await responseGate;
    await route.abort();
  });
  await openAi(page, { width: 1366, height: 768 });
  await seedPlan(page, { questions: 'single' });

  const firstRadio = page.locator('input[value="industrial_camera"]');
  await firstRadio.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('[data-ai-hook="clarification-question"]')).toContainText('正在等待权威 Readiness 确认');
  await expect(page.locator('#ai-btn-start-build')).toHaveCount(1);
  release();
});

for (const viewport of [
  { width: 1024, height: 768 },
  { width: 390, height: 844 },
]) {
  test(`clarification remains reachable without horizontal overflow at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await openAi(page, viewport);
    await seedPlan(page, { questions: 'multiple' });
    await expect(page.locator('[data-ai-hook="clarification-question"]')).toBeVisible();
    await expect(page.locator('#ai-btn-start-build')).toBeVisible();
    await focusClarificationViewport(page);
    const scrollState = await page.evaluate(() => {
      const pane = document.querySelector('#ai-plan-workspace') as HTMLElement;
      const clarification = document.querySelector('[data-ai-hook="clarification-workspace"]') as HTMLElement;
      const workspace = document.querySelector('[data-ai-hook="workspace"]') as HTMLElement;
      const resultPane = document.querySelector('[data-ai-hook="workbench-pane"]') as HTMLElement;
      const aiView = document.querySelector('#ai-view') as HTMLElement;
      const main = document.querySelector('.main-content') as HTMLElement;
      return {
        scrollTop: pane.scrollTop,
        scrollHeight: pane.scrollHeight,
        clientHeight: pane.clientHeight,
        overflowY: getComputedStyle(pane).overflowY,
        workspaceHeight: workspace.clientHeight,
        workspaceAlign: getComputedStyle(workspace).alignItems,
        resultPaneHeight: resultPane.clientHeight,
        resultPaneMinHeight: getComputedStyle(resultPane).minHeight,
        aiViewHeight: aiView.clientHeight,
        aiViewScrollHeight: aiView.scrollHeight,
        mainHeight: main.clientHeight,
        mainScrollHeight: main.scrollHeight,
        clarificationTop: clarification.getBoundingClientRect().top,
        paneTop: pane.getBoundingClientRect().top,
        paneBottom: pane.getBoundingClientRect().bottom,
      };
    });
    expect(scrollState.scrollTop, JSON.stringify(scrollState)).toBeGreaterThan(0);
    expect(scrollState.clarificationTop).toBeGreaterThanOrEqual(scrollState.paneTop - 1);
    expect(scrollState.clarificationTop).toBeLessThan(scrollState.paneBottom);
    await expectNoHorizontalOverflow(page);
  });
}

test('plan-ready dark visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedPlan(page, { ready: true, questions: 'none' });
  await expect(page.locator('#ai-view')).toHaveScreenshot('plan-ready-dark-1366.png');
});

test('plan-ready light visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedPlan(page, { ready: true, questions: 'none' });
  await expect(page.locator('#ai-view')).toHaveScreenshot('plan-ready-light-1366.png');
});

test('clarification dark visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedPlan(page, { questions: 'multiple' });
  await focusClarificationViewport(page);
  await expect(page.locator('#ai-view')).toHaveScreenshot('clarification-dark-1366.png');
});

test('clarification confirming dark visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'dark' });
  await seedPlan(page, { questions: 'multiple', confirming: true });
  await focusClarificationViewport(page);
  await expect(page.locator('#ai-view')).toHaveScreenshot('clarification-confirming-dark-1366.png');
});

test('confirmed clarification collapses into a low-emphasis light summary', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedPlan(page, { questions: 'single' });
  await page.evaluate(question => {
    const panel = (window as any).aiPanel;
    panel._dispatchAgentWorkspaceEvent({
      type: 'workspace/answers-confirmed',
      payload: {
        answers: [{
          questionId: question.id,
          field: question.field,
          value: 'industrial_camera',
          origin: 'explicit_user_selection',
        }],
      },
    });
    panel._dispatchAgentWorkspaceEvent({
      type: 'workspace/readiness-received',
      payload: {
        buildReadiness: {
          canBuild: true,
          blockers: [],
          resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'output_target'],
          remainingFields: [],
          primaryMessage: '方案信息完整，可以开始构建。',
          contractVersion: 'v2',
        },
      },
    });
    panel.pendingVisionPlan.buildReadiness = {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'output_target'],
      remainingFields: [],
      primaryMessage: '方案信息完整，可以开始构建。',
      contractVersion: 'v2',
    };
    panel.pendingVisionPlan.canBuild = true;
    panel._renderAgentWorkspaceOverview();
    panel._renderPlanWorkspace(panel.pendingVisionPlan);
  }, imageSourceQuestion);
  await focusClarificationViewport(page);
  await expect(page.locator('[data-ai-hook="clarification-confirmed"]')).toContainText('工业相机');
  await expect(page.locator('#ai-view')).toHaveScreenshot('clarification-confirmed-light-1366.png');
});

test('clarification light visual baseline at 1366', async ({ page }) => {
  await openAi(page, { width: 1366, height: 768, theme: 'light' });
  await seedPlan(page, { questions: 'multiple' });
  await focusClarificationViewport(page);
  await expect(page.locator('#ai-view')).toHaveScreenshot('clarification-light-1366.png');
});

test('clarification compact visual baseline at 1024', async ({ page }) => {
  await openAi(page, { width: 1024, height: 768, theme: 'dark' });
  await seedPlan(page, { questions: 'multiple' });
  await focusClarificationViewport(page);
  await expect(page.locator('#ai-view')).toHaveScreenshot('clarification-compact-1024.png');
});

test('clarification narrow visual baseline at 390', async ({ page }) => {
  await openAi(page, { width: 390, height: 844, theme: 'dark' });
  await seedPlan(page, { questions: 'multiple' });
  await focusClarificationViewport(page);
  await expect(page.locator('#ai-view')).toHaveScreenshot('clarification-narrow-390.png');
});
