import { test, expect, Page } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

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
        'event: run.completed',
        `data: {"runId":"${runId}","sequence":2,"eventType":"run.completed","stage":"run","status":"completed","payload":{"status":"completed","flow":{"operators":[]},"buildResult":{"applyGate":{"canvasApplyReady":false,"blocked":true}},"metadataOnly":true}}`,
        '',
        '',
      ].join('\n'),
    });
  });
}

test('AI agent clarification layout stays usable on narrow viewports', async ({ page }) => {
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
      sessionId: 'responsive-session',
      turnIntent: 'new_flow',
      interactionState: 'clarifying',
      routerConfidence: 'high',
      aiExplanation: '当前需求还需要澄清 2 项关键信息。',
      blockingClarificationFields: ['scene', 'object_type'],
      nonBlockingMissingFields: ['model_path', 'roi', 'plc_address'],
      requirementBrief: {
        requirementMode: 'strict',
        confidence: 0,
        clarificationRequired: true,
        draftRiskLevel: 'high',
        missingFacts: ['需要确认具体场景', '需要确认检测对象'],
        blockingClarificationFields: ['scene', 'object_type'],
        nonBlockingMissingFields: ['model_path', 'roi', 'plc_address'],
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
    });
  });

  const clarificationPlan = page.locator('#ai-clarification-plan-card');
  await expect(clarificationPlan).toBeVisible();
  await expect(clarificationPlan).toContainText('待澄清');
  await expect(clarificationPlan).toContainText('阻断问题');
  await expect(clarificationPlan).toContainText('下一步：先回答 2 个阻断问题');
  await expect(page.locator('#ai-btn-send-clarification-plan')).toBeDisabled();
  const initialLayout = await page.evaluate(() => {
    const rect = (selector: string) => {
      const element = document.querySelector(selector);
      if (!element) return null;
      const box = element.getBoundingClientRect();
      return {
        x: Math.round(box.x),
        y: Math.round(box.y),
        width: Math.round(box.width),
        height: Math.round(box.height),
        right: Math.round(box.right),
        bottom: Math.round(box.bottom),
      };
    };
    const main = document.querySelector('.main-content');
    const header = rect('.ai-pane-header');
    const input = rect('.ai-input-section');
    const leftPane = rect('.ai-pane-left');
    const rightPane = rect('#ai-result-pane');
    return {
      viewportWidth: window.innerWidth,
      viewportHeight: window.innerHeight,
      documentWidth: document.documentElement.scrollWidth,
      bodyWidth: document.body.scrollWidth,
      mainCanScroll: main ? main.scrollHeight > main.clientHeight : false,
      header,
      input,
      leftPane,
      rightPane,
    };
  });
  expect(initialLayout.header?.y).toBeGreaterThanOrEqual(0);
  expect(initialLayout.header?.bottom).toBeLessThanOrEqual(initialLayout.viewportHeight);
  expect(initialLayout.input?.y).toBeGreaterThan(initialLayout.header?.bottom || 0);
  expect(initialLayout.rightPane?.y).toBeGreaterThanOrEqual(initialLayout.leftPane?.bottom || 0);
  expect(initialLayout.mainCanScroll).toBe(true);

  const defectOption = page.locator('#ai-clarification-plan-card .ai-clarification-option[data-clarification-field="scene"][data-clarification-value="外观缺陷"]').first();
  await defectOption.scrollIntoViewIfNeeded();
  await defectOption.click();
  await expect(page.locator('#ai-input')).toHaveValue(/澄清回答：\n场景类型：外观缺陷/);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toBeEnabled();
  await expect(defectOption).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
  await expect(page.locator('#ai-btn-apply')).toContainText('暂无可应用方案');
  await expect(page.locator('#btn-template-create')).toBeHidden();

  const metrics = await page.evaluate(() => ({
    viewportWidth: window.innerWidth,
    documentWidth: document.documentElement.scrollWidth,
    bodyWidth: document.body.scrollWidth,
  }));

  expect(metrics.documentWidth).toBeLessThanOrEqual(metrics.viewportWidth);
  expect(metrics.bodyWidth).toBeLessThanOrEqual(metrics.viewportWidth);
});

test('AI panel renders Intent Router clarification in Plan workspace and enables send after option', async ({ page }) => {
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
        publicReason: '需求信息不足，暂不可构建。',
        assistantReply: '请先确认包装箱检测的具体目标。',
        clarificationQuestions: [
          {
            field: 'scene',
            question: '请确认这是哪一类视觉场景。',
            required: true,
            priority: 'high',
            reason: '场景类型会决定模板和算子链。',
            options: ['外观缺陷', '漏装/有无', '线序判定'],
          },
        ],
        fallbackAllowed: true,
        routerSource: 'model_router',
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
    panel._dispatchGenerateRequest({
      description: '包装箱检测',
      userMessage: '包装箱检测',
    });
  });

  const card = page.locator('#ai-clarification-plan-card');
  await expect(card).toBeVisible();
  await expect(page.locator('#ai-plan-workspace .ai-plan-empty')).toHaveCount(0);
  await expect(page.locator('#ai-build-workspace')).toBeHidden();
  await expect(page.locator('#ai-btn-send-clarification-plan')).toBeDisabled();

  const option = card.locator('.ai-clarification-option[data-clarification-field="scene"][data-clarification-value="外观缺陷"]').first();
  await option.click();
  await expect(page.locator('#ai-input')).toHaveValue(/澄清回答：\n场景类型：外观缺陷/);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toBeEnabled();
  await expect(option).toHaveAttribute('aria-pressed', 'true');

  const state = await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    return {
      mode: panel.agentWorkspaceMode,
      hasPlan: Boolean(panel.pendingVisionPlan),
      status: panel.pendingClarificationPayload?.status,
      interactionState: panel.pendingClarificationPayload?.interactionState,
      question: panel.pendingClarificationPayload?.requirementBrief?.clarificationQuestions?.[0]?.question,
    };
  });
  expect(state).toEqual({
    mode: 'plan',
    hasPlan: false,
    status: 'clarification_required',
    interactionState: 'clarifying',
    question: '请确认这是哪一类视觉场景。',
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

  const requestId = await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.isVisionAgentDeveloperUiEnabled = true;
    panel._dispatchGenerateRequest({
      description: '帮我构建一个流程',
      userMessage: '帮我构建一个流程',
      explicitMode: 'new',
      skipPlan: true,
      skipPlanSource: 'developer_direct_build_debug',
    });
    return panel.activeGenerateRequestId;
  });
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

  await expect(page.locator('#ai-clarification-plan-card')).toBeVisible();
  await expect(page.locator('#ai-clarification-plan-card')).toContainText('待澄清');
  await expect(page.locator('#ai-clarification-plan-card')).toContainText('阻断问题');
  await expect(page.locator('.ai-assistant-clarification-section').last()).toBeVisible();
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

test('AI panel renders fallback ClarificationPlanCard for no-template no-rule clarification_required response', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await mockShellApis(page);
  await bootAuthenticatedApp(page);

  await page.locator('.nav-btn[data-view="ai"]').evaluate(element => {
    (element as HTMLElement).click();
  });
  await page.waitForFunction(() => Boolean((window as any).aiPanel && (window as any).mockWebViewResponse));

  const requestId = await page.evaluate(() => {
    const panel = (window as any).aiPanel;
    panel.isVisionAgentDeveloperUiEnabled = true;
    panel._dispatchGenerateRequest({
      description: '帮我做一个没有模板的检测',
      userMessage: '帮我做一个没有模板的检测',
      explicitMode: 'new',
      skipPlan: true,
      skipPlanSource: 'developer_direct_build_debug',
    });
    return panel.activeGenerateRequestId;
  });
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
        routerConfidence: 'medium',
        aiExplanation: '未命中预置模板和规则兜底，需要先补充关键信息。',
        generationMode: 'free_generate',
        templateLockLevel: 'none',
        templateCandidates: [],
        blockingClarificationFields: [],
        nonBlockingMissingFields: ['model_path'],
        requirementBrief: {
          requirementMode: 'strict',
          confidence: 0,
          clarificationRequired: true,
          draftRiskLevel: 'high',
          knownFacts: ['未命中预置模板', '未命中规则兜底'],
          missingFacts: [],
          blockingClarificationFields: [],
          nonBlockingMissingFields: ['model_path'],
          clarificationQuestions: [],
        },
      },
    });
  }, requestId);

  const card = page.locator('#ai-clarification-plan-card');
  await expect(card).toBeVisible();
  await expect(card).toContainText('待澄清');
  await expect(card).toContainText('阻断问题');
  await expect(card).toContainText('场景类型');
  await expect(card).toContainText('检测对象');
  await expect(card).toContainText('图像来源/ROI');
  await expect(card.locator('.ai-clarification-option')).toHaveCount(22);

  const sceneOption = card.locator('.ai-clarification-option[data-clarification-field="scene"][data-clarification-value="外观缺陷"]').first();
  await sceneOption.click();
  await expect(page.locator('#ai-input')).toHaveValue(/澄清回答：\n场景类型：外观缺陷/);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toBeEnabled();
  await expect(sceneOption).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
  await expect(page.locator('#ai-btn-apply')).toContainText('暂无可应用方案');
});

test('AI panel keeps old requirement brief send button independent from new clarification Plan card', async ({ page }) => {
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

  await expect(page.locator('#ai-btn-send-clarification-brief')).toHaveCount(1);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toBeVisible();
  await expect(page.locator('#ai-btn-send-clarification-plan')).toBeDisabled();
  expect(await page.locator('#ai-btn-send-clarification').count()).toBe(0);

  const option = page.locator('#ai-clarification-plan-card .ai-clarification-option[data-clarification-field="scene"][data-clarification-value="外观缺陷"]').first();
  await option.click();
  await expect(page.locator('#ai-input')).toHaveValue(/澄清回答：\n场景类型：外观缺陷/);
  await expect(page.locator('#ai-btn-send-clarification-plan')).toBeEnabled();
  await expect(page.locator('#ai-btn-send-clarification-brief')).toBeEnabled();

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

  await page.waitForFunction(() => (window as any).aiPanel?.activeAgentRunId === 'ar_webview_modify');
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

  await page.waitForFunction(() => (window as any).aiPanel?.activeAgentRunId === 'ar_webview_new');
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

  await expect(page.locator('#ai-clarification-plan-card')).toContainText('待澄清');
  await expect(page.locator('#ai-clarification-plan-card')).toContainText('1 个阻断问题');
  await expect(page.locator('.ai-assistant-clarification-section').last()).toContainText('请补充检测对象是什么。');
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

  await page.waitForFunction(() => (window as any).aiPanel?.activeAgentRunId === 'ar_webview_build');
  expect(agentRunPayloads).toHaveLength(1);
  expect(agentRunPayloads[0].description).toBe('webview build request');
  expect(agentRunPayloads[0].mode).toBe('auto');
  expect(agentRunPayloads[0].requirementMode).toBe('strict');
  expect(agentRunPayloads[0].requestId).toBeTruthy();
  expect(Object.prototype.hasOwnProperty.call(agentRunPayloads[0], 'sessionId')).toBe(true);
  const webMessages = await page.evaluate(() => (window as any).__cvWebViewMessages || []);
  expect(webMessages.some((message: any) => message.messageType === 'GenerateFlow')).toBe(false);
});
