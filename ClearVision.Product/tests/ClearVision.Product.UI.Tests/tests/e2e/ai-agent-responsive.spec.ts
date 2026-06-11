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
  await expect(page.locator('#ai-btn-send-clarification')).toBeDisabled();
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
  await expect(page.locator('#ai-btn-send-clarification')).toBeEnabled();
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
  await expect(page.locator('#ai-btn-send-clarification')).toBeEnabled();
  await expect(sceneOption).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#ai-btn-apply')).toBeDisabled();
  await expect(page.locator('#ai-btn-apply')).toContainText('暂无可应用方案');
});

test('AI panel sends current flow context for modification turns', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await installFakeWebView2(page);
  await mockShellApis(page);
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

  await page.waitForFunction(() =>
    ((window as any).__cvWebViewMessages || []).some((message: any) => message.messageType === 'GenerateFlow'));

  const sent = await page.evaluate(() => {
    const messages = (window as any).__cvWebViewMessages || [];
    return messages.find((message: any) => message.messageType === 'GenerateFlow');
  });

  expect(sent.payload.mode).toBe('modify');
  expect(sent.payload.description).toBe('把算子名称改成中文，其他参数和连线保持不变');
  expect(sent.payload.existingFlowJson).toBeTruthy();
  expect(sent.payload.existingFlowJson.operators).toHaveLength(2);
  expect(sent.payload.existingFlowJson.connections).toHaveLength(1);
  await expect(page.locator('#ai-agent-runtime')).toContainText('微调中');
  await expect(page.locator('#ai-agent-runtime')).toContainText('意图 增量微调');
});

test('AI panel keeps explicit new-flow requests from being coerced into modification', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 820 });
  await installFakeWebView2(page);
  await mockShellApis(page);
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

  await page.waitForFunction(() =>
    ((window as any).__cvWebViewMessages || []).some((message: any) => message.messageType === 'GenerateFlow'));

  const sent = await page.evaluate(() => {
    const messages = (window as any).__cvWebViewMessages || [];
    return messages.find((message: any) => message.messageType === 'GenerateFlow');
  });

  expect(sent.payload.mode).toBe('new');
  expect(sent.payload.description).toBe('新增一个缺陷检测流程');
  expect(sent.payload.existingFlowJson).toBeNull();
  await expect(page.locator('#ai-agent-runtime')).toContainText('生成中');
  await expect(page.locator('#ai-agent-runtime')).toContainText('意图 新建流程');
});

test('AI panel uses WebView2 postMessage contract for generation turns', async ({ page }) => {
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
