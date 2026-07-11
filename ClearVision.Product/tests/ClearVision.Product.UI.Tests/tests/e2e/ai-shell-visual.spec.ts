import { test, expect, Page } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

type Theme = 'dark' | 'light';

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
