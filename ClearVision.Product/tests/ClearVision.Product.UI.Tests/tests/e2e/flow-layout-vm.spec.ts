import { readFileSync } from 'node:fs';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { test, expect, Page, TestInfo } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

type PreviewMode = {
  value: 'success-no-image' | 'success-image' | 'success-pixel-image' | 'error' | 'error-diagnostics';
  delayMs?: number;
  requests: any[];
};

const PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==';
const PIXEL_PROBE_PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAEAAAAAwCAIAAAAuKetIAAAGGklEQVR4nO3Yoc6yUACAYS7CZiNhO00So7gRDGwWRsJ2miRHYSMY2CzMhO00SIzwuhEIbBZmwnaaJJr38F+F6f9u4LmAx1hZrC1Mi42FsNhaOBY7i73FwSK0OFpIi5PF2SK1uFhcLW4WdwtlUVk0Fg+L3uJp8bJ4W2iLj8Vi8bX4tW+sBGuBKdgIhGArcAQ7wV5wEISCo0AKToKzIBVcBFfBTXAXKEElaAQPQS94Cl6Ct0ALPoJF8BX82jdWNmsb02ZjI2y2No7NzmZvc7AJbY420uZkc7ZJbS42V5ubzd1G2VQ2jc3Dprd52rxs3jba5mOz2Hxtfu0bK5e1i+mycREuWxfHZeeydzm4hC5HF+lycjm7pC4Xl6vLzeXuolwql8bl4dK7PF1eLm8X7fJxWVy+Lr/2jZXH2sP02HgIj62H47Hz2HscPEKPo4f0OHmcPVKPi8fV4+Zx91AelUfj8fDoPZ4eL4+3h/b4eCweX49f+8bKZ+1j+mx8hM/Wx/HZ+ex9Dj6hz9FH+px8zj6pz8Xn6nPzufson8qn8Xn49D5Pn5fP20f7fHwWn6/Pr31jFbAOMAM2ASJgG+AE7AL2AYeAMOAYIANOAeeANOAScA24BdwDVEAV0AQ8AvqAZ8Ar4B2gAz4BS8A34Ne+sYpYR5gRmwgRsY1wInYR+4hDRBhxjJARp4hzRBpxibhG3CLuESqiimgiHhF9xDPiFfGO0BGfiCXiG/Fr31hJ1hJTspEIyVbiSHaSveQgCSVHiZScJGdJKrlIrpKb5C5RkkrSSB6SXvKUvCRviZZ8JIvkK/m1b6xi1jFmzCZGxGxjnJhdzD7mEBPGHGNkzCnmHJPGXGKuMbeYe4yKqWKamEdMH/OMecW8Y3TMJ2aJ+cb82jdWCesEM2GTIBK2CU7CLmGfcEgIE44JMuGUcE5IEy4J14Rbwj1BJVQJTcIjoU94JrwS3gk64ZOwJHwTfu0bq4x1hpmxyRAZ2wwnY5exzzhkhBnHDJlxyjhnpBmXjGvGLeOeoTKqjCbjkdFnPDNeGe8MnfHJWDK+Gb/2jVXOOsfM2eSInG2Ok7PL2ecccsKcY47MOeWcc9KcS84155Zzz1E5VU6T88jpc545r5x3js755Cw535xf+8aqYF1gFmwKRMG2wCnYFewLDgVhwbFAFpwKzgVpwaXgWnAruBeogqqgKXgU9AXPglfBu0AXfAqWgm/Br31jVbIuMUs2JaJkW+KU7Er2JYeSsORYIktOJeeStORSci25ldxLVElV0pQ8SvqSZ8mr5F2iSz4lS8m35Ne+sVKsFaZioxCKrcJR7BR7xUERKo4KqTgpzopUcVFcFTfFXaEUlaJRPBS94ql4Kd4KrfgoFsVX8WvfWNWsa8yaTY2o2dY4Nbuafc2hJqw51siaU825Jq251FxrbjX3GlVT1TQ1j5q+5lnzqnnX6JpPzVLzrfm1b6xa1i1my6ZFtGxbnJZdy77l0BK2HFtky6nl3JK2XFquLbeWe4tqqVqalkdL3/JsebW8W3TLp2Vp+bb82jdWHesOs2PTITq2HU7HrmPfcegIO44dsuPUce5IOy4d145bx71DdVQdTcejo+94drw63h2649OxdHw7fu0bq4H1gDmwGRAD2wFnYDewHzgMhAPHATlwGjgPpAOXgevAbeA+oAaqgWbgMdAPPAdeA+8BPfAZWAa+A7/2jdXIesQc2YyIke2IM7Ib2Y8cRsKR44gcOY2cR9KRy8h15DZyH1Ej1Ugz8hjpR54jr5H3iB75jCwj35Ff+8ZqYj1hTmwmxMR2wpnYTewnDhPhxHFCTpwmzhPpxGXiOnGbuE+oiWqimXhM9BPPidfEe0JPfCaWie/Er31jpVlrTM1GIzRbjaPZafaagybUHDVSc9KcNanmorlqbpq7RmkqTaN5aHrNU/PSvDVa89Esmq/m176xmlnPmDObGTGznXFmdjP7mcNMOHOckTOnmfNMOnOZuc7cZu4zaqaaaWYeM/3Mc+Y1857RM5+ZZeY782vf+Huhvxf6e6G/F/p7ob8X+nuhvxf6e6G/F/p7ob8X+nuhvxf6e6G/F/p7of/yhf4BfEKnLUtsuicAAAAASUVORK5CYII=';
const STALE_PREVIEW_TEXT = '参数或流程已变更，需重新预览';
const PROPERTY_RESIZER_SELECTOR = '[data-sidebar-resizer="property"]';
const PROPERTY_SIDEBAR_STORAGE_KEY = 'cv_flow_property_sidebar_width';
const OLD_PREVIEW_WORKBENCH_MAX_WIDTH = 560;
const parameterRuleParitySpec = JSON.parse(readFileSync(
  resolve(process.cwd(), '../../../quality/evals/specs/vision_agent_parameter_rule_parity_cases.json'),
  'utf8'
));

const operators = [
  {
    type: 'ImageAcquisition',
    displayName: '图像采集',
    category: '输入',
    description: '从相机或文件读取图像',
    parameterConstraints: parameterRuleParitySpec.operatorConstraints.ImageAcquisition,
    parameters: [
      { name: 'SourceType', displayName: '采集源', dataType: 'enum', value: 'File', options: [{ value: 'File', label: '文件' }, { value: 'Camera', label: '相机' }] },
      { name: 'FilePath', displayName: '文件路径', dataType: 'string', value: '' },
      { name: 'CameraId', displayName: '相机', dataType: 'cameraBinding', value: '' },
      { name: 'CameraBindingId', displayName: '相机绑定', dataType: 'string', value: '' },
    ],
    inputPorts: [],
    outputPorts: [{ name: 'Image', dataType: 'Image' }],
  },
  {
    type: 'Thresholding',
    displayName: '阈值分割',
    category: '预处理',
    description: '按阈值生成二值图',
    parameters: [
      { name: 'Threshold', displayName: '阈值', dataType: 'int', value: 128, min: 0, max: 255 },
      { name: 'OverlayColor', displayName: '叠加颜色', dataType: 'color', value: '#ff0000' },
    ],
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Mask', dataType: 'Image' }],
  },
  {
    type: 'TemplateMatching',
    displayName: '模板匹配',
    category: '预处理',
    description: '定位模板姿态',
    parameters: [
      { name: 'TemplatePath', displayName: '模板路径', dataType: 'string', value: '' },
      { name: 'TemplateId', displayName: '模板ID', dataType: 'string', value: '' },
    ],
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Pose', dataType: 'Object' }],
  },
  {
    type: 'GaussianBlur',
    displayName: '高斯滤波',
    category: '预处理',
    description: '平滑图像噪声',
    parameters: [{ name: 'Sigma', displayName: 'Sigma', dataType: 'double', value: 1.2 }],
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Image', dataType: 'Image' }],
  },
];

async function installStudio2Flags(page: Page) {
  await page.addInitScript(() => {
    const webviewListeners = new Map<string, ((event: any) => void)[]>();
    (window as any).__pickFileMessages = [];
    (window as any).__cvDispatchWebViewMessage = (message: any) => {
      for (const listener of webviewListeners.get('message') ?? []) {
        listener({ data: message });
      }
    };
    (window as any).chrome = {
      webview: {
        addEventListener(type: string, listener: (event: any) => void) {
          if (!webviewListeners.has(type)) {
            webviewListeners.set(type, []);
          }
          webviewListeners.get(type)?.push(listener);
        },
        removeEventListener(type: string, listener: (event: any) => void) {
          const listeners = webviewListeners.get(type) ?? [];
          webviewListeners.set(type, listeners.filter(item => item !== listener));
        },
        postMessage(message: any) {
          (window as any).__pickFileMessages.push(message);
        },
      },
    };
    const startup = {
      featureFlags: {
        'Studio2.PropertyPanel': true,
        'Studio2.PreviewPanel': true,
      },
    };
    localStorage.removeItem('cv_flow_property_sidebar_width');
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: startup,
      writable: false,
      configurable: false,
    });
  });
}

function buildObservation(request: any, success: boolean) {
  const targetNodeId = request.targetNodeId || request.TargetNodeId || '';
  return {
    schemaVersion: 'execution-observation.v1',
    observedAtUtc: '2026-07-05T00:00:00Z',
    identity: {
      projectId: request.projectId || request.ProjectId || 'flow-layout-vm',
      targetNodeId,
      debugSessionId: request.debugSessionId || request.DebugSessionId || 'layout-debug',
      clientRequestSequence: request.clientRequestSequence || request.ClientRequestSequence || 1,
      flowRevision: request.flowRevision || request.FlowRevision || 1,
    },
    outcome: {
      success,
      executionTimeMs: success ? 24 : 0,
      errorMessage: success ? null : '模拟预览失败',
      executedOperatorCount: success ? 1 : 0,
    },
    summary: success
      ? [{ key: 'Score', displayValue: '0.982', pathHint: '$["Score"]', addressable: true }]
      : [],
    detail: {
      kind: 'dictionary',
      name: 'Output',
      displayValue: success ? 'Score 0.982' : 'Failed',
      children: [],
    },
    diagnostics: [],
    visualScene: {
      coordinateSpace: 'image.pixel',
      imageWidth: 320,
      imageHeight: 240,
      primitives: [],
      diagnostics: [],
    },
  };
}

async function installRoutes(page: Page, previewMode: PreviewMode) {
  await page.route('**/favicon.ico', async route => {
    await route.fulfill({
      status: 204,
    });
  });

  await page.route('**/api/operators/library', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(operators),
    });
  });

  await page.route('**/api/operators/types', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(operators.map(operator => operator.type)),
    });
  });

  await page.route('**/api/operators/*/metadata', async route => {
    const url = new URL(route.request().url());
    const type = decodeURIComponent(url.pathname.split('/').at(-2) ?? '');
    const operator = operators.find(item => item.type === type);
    await route.fulfill({
      status: operator ? 200 : 404,
      contentType: 'application/json',
      body: JSON.stringify(operator ?? { message: 'not found' }),
    });
  });

  await page.route('**/api/cameras/bindings', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]',
    });
  });

  await page.route('**/api/projects', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]',
    });
  });

  await page.route('**/api/projects/*/global-variable-values', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '{}',
    });
  });

  await page.route('**/api/flows/preview-node', async route => {
    const request = route.request().postDataJSON();
    previewMode.requests.push(request);
    if (previewMode.delayMs) {
      await new Promise(resolve => setTimeout(resolve, previewMode.delayMs));
    }
    const isError = previewMode.value === 'error' || previewMode.value === 'error-diagnostics';
    const diagnosticsError = previewMode.value === 'error-diagnostics';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: !isError,
        outputImageBase64: previewMode.value === 'success-pixel-image'
          ? PIXEL_PROBE_PNG_BASE64
          : (previewMode.value === 'success-image' ? PNG_BASE64 : null),
        outputData: isError ? null : { Score: 0.982, Width: 320 },
        observation: buildObservation(request, !isError),
        artifacts: [],
        executionTimeMs: isError ? 0 : 24,
        errorMessage: diagnosticsError ? 'Parameter validation failed: Threshold invalid' : (isError ? '模拟预览失败' : null),
        diagnostics: diagnosticsError
          ? [{ code: 'VAL001', message: '参数超出范围，请检查阈值' }]
          : [],
        missingResources: diagnosticsError
          ? [{ name: 'Template', pathHint: 'C:\\Users\\A\\templates\\part.ncc' }]
          : [],
        failedOperatorName: diagnosticsError ? '定位算子' : null,
        failedOperatorType: diagnosticsError ? 'BlobAnalysis' : null,
      }),
    });
  });
}

async function setCurrentProject(page: Page) {
  await page.evaluate(async () => {
    const projectModule = await import('/src/features/project/projectManager.js');
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    projectModule.setCurrentProject({
      id: 'flow-layout-vm',
      name: '流程布局验证',
      description: '',
      flow: null,
    });
    inspectionModule.default.setProject('flow-layout-vm');
  });
}

async function openPreprocessFlyout(page: Page) {
  await page.locator('#operator-rail .operator-rail-item', { hasText: '预处理' }).click();
  await expect(page.locator('#operator-group-flyout')).toBeVisible();
}

async function openInputFlyout(page: Page) {
  await page.locator('#operator-rail .operator-rail-item', { hasText: '输入' }).click();
  await expect(page.locator('#operator-group-flyout')).toBeVisible();
}

async function addNodeFromFlyout(page: Page, label = '阈值分割') {
  await openPreprocessFlyout(page);
  await page.locator('#operator-group-flyout .operator-flyout-item', { hasText: label }).click();
  await expect(page.locator('#operator-group-flyout')).toBeHidden();
  return page.evaluate(() => {
    const flowCanvas = (window as any).flowCanvas;
    return {
      count: flowCanvas.nodes.size,
      selectedNode: flowCanvas.selectedNode,
    };
  });
}

async function assertNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() => ({
    document: document.documentElement.scrollWidth > document.documentElement.clientWidth + 2,
    body: document.body.scrollWidth > document.body.clientWidth + 2,
    main: (() => {
      const main = document.querySelector('#main-content');
      if (!main) return true;
      return main.scrollWidth > main.clientWidth + 2;
    })(),
  }));
  expect(overflow).toEqual({ document: false, body: false, main: false });
}

async function assertChineseTextRenderable(page: Page) {
  const text = await page.locator('.inspector-pane, .preview-workbench-pane').evaluateAll(elements =>
    elements.map(element => (element as HTMLElement).innerText).join('\n')
  );
  expect(text).not.toContain('\uFFFD');
  expect(text).toMatch(/预览|算子|图像|属性|文件|相机/);
}

async function assertPreviewButtonDisabledState(page: Page) {
  const mismatches = await page.locator('.preview-workbench-pane button[data-preview-action]').evaluateAll(buttons =>
    buttons
      .map(button => ({
        action: button.getAttribute('data-preview-action'),
        disabled: (button as HTMLButtonElement).disabled,
        ariaDisabled: button.getAttribute('aria-disabled') === 'true',
      }))
      .filter(item => item.disabled !== item.ariaDisabled)
  );
  expect(mismatches).toEqual([]);
}

async function collectFlowLayoutMeasurements(page: Page) {
  return page.evaluate(() => {
    const rect = (selector: string) => {
      const element = document.querySelector(selector) as HTMLElement | null;
      if (!element) {
        return null;
      }
      const value = element.getBoundingClientRect();
      return {
        x: value.x,
        y: value.y,
        width: value.width,
        height: value.height,
        top: value.top,
        bottom: value.bottom,
        left: value.left,
        right: value.right,
      };
    };
    const main = document.querySelector('#main-content') as HTMLElement | null;
    return {
      preview: rect('.preview-workbench-pane'),
      workspace: rect('.workspace'),
      inspector: rect('.inspector-pane'),
      imageStage: rect('.preview-workbench-pane .preview-capability-image-stage'),
      imageSurface: rect('.preview-workbench-pane .preview-capability-main-image'),
      manualButton: rect('.preview-workbench-pane [data-preview-action="manual-preview"]'),
      cancelButton: rect('.preview-workbench-pane [data-preview-action="cancel-preview"]'),
      fitButton: rect('.preview-workbench-pane [data-preview-action="image-fit"]'),
      originalButton: rect('.preview-workbench-pane [data-preview-action="image-original"]'),
      openImageButton: rect('.preview-workbench-pane [data-preview-action="open-image"]'),
      viewport: {
        width: window.innerWidth,
        height: window.innerHeight,
      },
      overflow: {
        document: document.documentElement.scrollWidth > document.documentElement.clientWidth + 2,
        body: document.body.scrollWidth > document.body.clientWidth + 2,
        main: main ? main.scrollWidth > main.clientWidth + 2 : true,
      },
    };
  });
}

type PixelProbeGeometry = {
  image: { left: number; top: number; width: number; height: number };
  content: { left: number; top: number; width: number; height: number };
  naturalWidth: number;
  naturalHeight: number;
};

async function getPixelProbeGeometry(page: Page): Promise<PixelProbeGeometry> {
  return page.locator('#preview-panel .preview-capability-main-image img').first().evaluate(image => {
    const rect = image.getBoundingClientRect();
    const naturalWidth = image.naturalWidth;
    const naturalHeight = image.naturalHeight;
    const naturalAspect = naturalWidth / naturalHeight;
    const elementAspect = rect.width / rect.height;
    let width = rect.width;
    let height = rect.height;
    let left = rect.left;
    let top = rect.top;
    if (elementAspect > naturalAspect) {
      width = height * naturalAspect;
      left += (rect.width - width) / 2;
    } else if (elementAspect < naturalAspect) {
      height = width / naturalAspect;
      top += (rect.height - height) / 2;
    }

    return {
      image: { left: rect.left, top: rect.top, width: rect.width, height: rect.height },
      content: { left, top, width, height },
      naturalWidth,
      naturalHeight,
    };
  });
}

function pointInPixelProbeImage(geometry: PixelProbeGeometry, xRatio: number, yRatio: number) {
  return {
    x: geometry.content.left + (geometry.content.width * xRatio),
    y: geometry.content.top + (geometry.content.height * yRatio),
  };
}

async function findFitLetterboxPoint(page: Page, geometry: PixelProbeGeometry) {
  return page.evaluate(content => {
    const stage = document.querySelector('#preview-panel .preview-capability-image-stage') as HTMLElement | null;
    if (!stage) {
      throw new Error('Pixel probe stage is unavailable while locating a letterbox point.');
    }

    const bounds = stage.getBoundingClientRect();
    for (let y = Math.ceil(bounds.top) + 2; y < Math.floor(bounds.bottom) - 1; y += 4) {
      for (let x = Math.ceil(bounds.left) + 2; x < Math.floor(bounds.right) - 1; x += 4) {
        const isOutsideImage = x < content.left || x > content.left + content.width ||
          y < content.top || y > content.top + content.height;
        const target = document.elementFromPoint(x, y);
        if (isOutsideImage && target && (target === stage || stage.contains(target))) {
          return { x, y };
        }
      }
    }

    throw new Error('Fit-mode stage does not expose a hit-testable letterbox point.');
  }, geometry.content);
}

function parsePixelProbeCoordinates(text: string) {
  const match = text.match(/\bX:\s*(\d+)\s+Y:\s*(\d+)/);
  if (!match) {
    throw new Error(`Pixel probe coordinates are missing from "${text}".`);
  }

  return {
    x: Number.parseInt(match[1], 10),
    y: Number.parseInt(match[2], 10),
  };
}

async function dragPreviewWorkbench(page: Page, deltaX: number) {
  const resizer = page.locator(PROPERTY_RESIZER_SELECTOR);
  await expect(resizer).toBeVisible();

  const box = await resizer.boundingBox();
  if (!box) {
    throw new Error('Preview workbench resizer is not visible.');
  }

  const startX = box.x + box.width / 2;
  const centerY = box.y + box.height / 2;

  await page.mouse.move(startX, centerY);
  await page.mouse.down();
  await page.mouse.move(startX + deltaX, centerY, { steps: 18 });
  await page.mouse.up();
}

async function expandPreviewWorkbench(page: Page, deltaX = -420) {
  const before = await collectFlowLayoutMeasurements(page);
  await dragPreviewWorkbench(page, deltaX);
  await expect.poll(async () => {
    const after = await collectFlowLayoutMeasurements(page);
    return Math.round(after.preview?.width ?? 0);
  }).toBeGreaterThan(Math.round((before.preview?.width ?? 0) + 80));
  const after = await collectFlowLayoutMeasurements(page);
  await page.evaluate(() => window.dispatchEvent(new Event('resize')));
  await expect.poll(async () => {
    const current = await collectFlowLayoutMeasurements(page);
    return Math.round(current.preview?.width ?? 0);
  }).toBe(Math.round(after.preview?.width ?? 0));
  return { before, after };
}

async function assertCorePreviewControlsInViewport(page: Page) {
  await expect(page.locator('.preview-workbench-pane [data-preview-action="manual-preview"]')).toBeVisible();
  await expect(page.locator('.preview-workbench-pane [data-preview-action="cancel-preview"]')).toBeVisible();
  await expect(page.locator('.preview-workbench-pane [data-preview-action="image-fit"]')).toBeVisible();
  await expect(page.locator('.preview-workbench-pane [data-preview-action="image-original"]')).toBeVisible();
  await expect(page.locator('.preview-workbench-pane [data-preview-action="open-image"]')).toBeVisible();

  const measurements = await collectFlowLayoutMeasurements(page);
  const viewportHeight = measurements.viewport.height;
  const mustBeFullyVisible = [
    measurements.manualButton,
    measurements.cancelButton,
    measurements.fitButton,
    measurements.originalButton,
    measurements.openImageButton,
  ];
  for (const item of mustBeFullyVisible) {
    expect(item).toBeTruthy();
    expect(item?.top ?? viewportHeight + 1).toBeGreaterThanOrEqual(0);
    expect(item?.bottom ?? viewportHeight + 1).toBeLessThanOrEqual(viewportHeight + 2);
  }
  expect(measurements.imageStage).toBeTruthy();
  expect(measurements.imageStage?.top ?? viewportHeight + 1).toBeLessThan(viewportHeight);
  expect(measurements.imageStage?.bottom ?? 0).toBeGreaterThan(0);
}

async function captureFlowLayoutState(page: Page, testInfo: TestInfo, name: string) {
  await page.evaluate(() => {
    document.querySelector('#cv-toast-container')?.replaceChildren();
  });
  await page.waitForTimeout(50);
  await expect(page.locator('.preview-workbench-pane')).toBeVisible();
  await assertChineseTextRenderable(page);
  await assertNoHorizontalOverflow(page);
  await assertPreviewButtonDisabledState(page);
  await assertCorePreviewControlsInViewport(page);
  await page.screenshot({
    path: testInfo.outputPath(`flow-layout-vm-${name}.png`),
    fullPage: false,
  });
}

async function captureOperatorPaletteState(page: Page, testInfo: TestInfo, name: string) {
  await page.evaluate(() => {
    document.querySelector('#cv-toast-container')?.replaceChildren();
  });
  await page.waitForTimeout(50);
  await page.screenshot({
    path: testInfo.outputPath(`operator-palette-${name}.png`),
    fullPage: false,
  });
}

async function assertLowHeightScrollability(page: Page) {
  const scrollState = await page.evaluate(() => {
    const read = (selector: string) => {
      const element = document.querySelector(selector) as HTMLElement | null;
      if (!element) {
        return null;
      }
      const style = getComputedStyle(element);
      return {
        overflowY: style.overflowY,
        scrollHeight: element.scrollHeight,
        clientHeight: element.clientHeight,
      };
    };

    return {
      property: read('.property-capability-scroll'),
      preview: read('.preview-capability-scroll'),
    };
  });

  expect(scrollState.property).toBeTruthy();
  expect(scrollState.preview).toBeTruthy();
  expect(scrollState.property?.overflowY).toMatch(/auto|scroll/);
  expect(scrollState.preview?.overflowY).toMatch(/auto|scroll/);
  expect(scrollState.property?.scrollHeight).toBeGreaterThan(scrollState.property?.clientHeight ?? 0);
  expect(scrollState.preview?.scrollHeight).toBeGreaterThan(scrollState.preview?.clientHeight ?? 0);
}

test.describe('Flow layout VisionMaster-style shell', () => {
  let previewMode: PreviewMode;

  test.beforeEach(async ({ page }) => {
    previewMode = { value: 'success-no-image', requests: [] };
    await installStudio2Flags(page);
    await installRoutes(page, previewMode);
    await bootAuthenticatedApp(page);
    await setCurrentProject(page);
  });

  test('renders group rail and opens/closes operator flyout with search', async ({ page }) => {
    await expect(page.locator('#operator-rail')).toBeVisible();
    await expect(page.locator('#operator-rail .operator-rail-item')).toContainText(['搜索', '最近', '收藏', '输入', '预处理']);
    await expect(page.locator('#operator-group-flyout')).toBeHidden();

    await openPreprocessFlyout(page);
    await expect(page.locator('#operator-group-flyout')).toContainText('阈值分割');
    await expect(page.locator('#operator-group-flyout')).toContainText('高斯滤波');

    await page.locator('[data-palette-search="true"]').fill('Sigma');
    await expect(page.locator('#operator-group-flyout .operator-flyout-item')).toHaveCount(1);
    await expect(page.locator('#operator-group-flyout')).toContainText('高斯滤波');

    await page.keyboard.press('Escape');
    await expect(page.locator('#operator-group-flyout')).toBeHidden();
  });

  test('supports global operator search, scoped category search, drag-add, and rail scroll retention', async ({ page }, testInfo) => {
    const consoleErrors: string[] = [];
    page.on('console', message => {
      if (message.type() === 'error') {
        const location = message.location();
        consoleErrors.push(`${message.text()} ${location.url}:${location.lineNumber}:${location.columnNumber}`);
      }
    });

    await expect(page.locator('#operator-rail .operator-rail-item', { hasText: '搜索' })).toBeVisible();
    await page.locator('#operator-rail .operator-rail-item', { hasText: '搜索' }).click();
    await expect(page.locator('#operator-group-flyout')).toBeVisible();
    await expect(page.locator('#operator-group-flyout')).toContainText('全部算子');
    await expect(page.locator('#operator-group-flyout')).toContainText('搜索范围：全部算子');
    await expect(page.locator('[data-palette-search="true"]')).toHaveAttribute('placeholder', '搜索全部算子：名称、类型、端口、参数');

    await page.locator('[data-palette-search="true"]').fill('Image');
    await expect(page.locator('#operator-group-flyout')).toContainText('图像采集');
    await expect(page.locator('#operator-group-flyout')).toContainText('阈值分割');
    await expect(page.locator('#operator-group-flyout')).toContainText('预处理');
    await captureOperatorPaletteState(page, testInfo, 'global-search-results');

    await page.locator('#operator-rail .operator-rail-item', { hasText: '预处理' }).click();
    await page.locator('[data-palette-search="true"]').fill('采集源');
    await expect(page.locator('#operator-group-flyout')).toContainText('搜索范围：预处理');
    await expect(page.locator('#operator-group-flyout')).toContainText('未找到匹配算子，可尝试输入算子名称、类型、端口或参数。');

    await page.locator('#operator-rail .operator-rail-item', { hasText: '搜索' }).click();
    await page.locator('[data-palette-search="true"]').fill('采集源');
    await expect(page.locator('#operator-group-flyout .operator-flyout-item')).toHaveCount(1);
    await expect(page.locator('#operator-group-flyout')).toContainText('图像采集');

    const dropResult = await page.evaluate(() => {
      const flowCanvas = (window as any).flowCanvas;
      const canvas = document.querySelector('#flow-canvas') as HTMLCanvasElement;
      const item = document.querySelector('.operator-flyout-item[data-operator-type="ImageAcquisition"]') as HTMLElement;
      if (!flowCanvas || !canvas || !item) {
        throw new Error('缺少全局搜索拖拽验证元素');
      }

      const rect = canvas.getBoundingClientRect();
      const clientX = rect.left + 220;
      const clientY = rect.top + 180;
      const beforeIds = new Set(Array.from(flowCanvas.nodes.keys()));
      const dataTransfer = new DataTransfer();

      item.dispatchEvent(new DragEvent('dragstart', {
        bubbles: true,
        cancelable: true,
        dataTransfer,
      }));
      canvas.dispatchEvent(new DragEvent('dragover', {
        bubbles: true,
        cancelable: true,
        clientX,
        clientY,
        dataTransfer,
      }));
      canvas.dispatchEvent(new DragEvent('drop', {
        bubbles: true,
        cancelable: true,
        clientX,
        clientY,
        dataTransfer,
      }));

      const added = Array.from(flowCanvas.nodes.values()).find((node: any) => !beforeIds.has(node.id)) as any;
      return {
        count: flowCanvas.nodes.size,
        addedType: added?.type ?? null,
      };
    });
    expect(dropResult.count).toBeGreaterThan(0);
    expect(dropResult.addedType).toBe('ImageAcquisition');

    await page.evaluate(async () => {
      const module = await import('/src/core/app/serviceRegistry.js');
      const shell = module.default.get('operatorPaletteShell');
      if (!shell) {
        throw new Error('operatorPaletteShell 未注册');
      }
      const extras = Array.from({ length: 18 }, (_, index) => ({
        key: `category:scroll-test-${index}`,
        label: `测试${String(index).padStart(2, '0')}`,
        kind: 'category',
        operators: []
      }));
      shell.groups = [...shell.groups.filter((group: any) => !String(group.key).includes('scroll-test')), ...extras];
      shell.renderRail();
    });

    const railScroller = page.locator('#operator-rail .operator-rail-inner');
    const scrollBeforeClick = await railScroller.evaluate(element => {
      element.scrollTop = element.scrollHeight;
      return element.scrollTop;
    });
    expect(scrollBeforeClick).toBeGreaterThan(0);

    await page.locator('#operator-rail .operator-rail-item', { hasText: '测试17' }).click();
    await page.waitForTimeout(100);
    const scrollAfterClick = await railScroller.evaluate(element => element.scrollTop);
    expect(scrollAfterClick).toBeGreaterThan(0);
    expect(Math.abs(scrollAfterClick - scrollBeforeClick)).toBeLessThanOrEqual(2);
    await captureOperatorPaletteState(page, testInfo, 'rail-scroll-preserved');

    expect(consoleErrors).toEqual([]);
  });

  test('adds an operator from flyout and switches inspector and preview workbench', async ({ page }) => {
    const result = await addNodeFromFlyout(page);
    expect(result.count).toBe(1);
    expect(result.selectedNode).toBeTruthy();

    await expect(page.locator('.inspector-pane')).toContainText('阈值分割');
    await expect(page.locator('.inspector-pane')).toContainText('阈值');
    await expect(page.locator('.inspector-pane #param-Threshold')).toBeVisible();
    await expect(page.locator('.inspector-pane label[for="param-Threshold"]')).toContainText('阈值');
    await expect(page.locator('.inspector-pane #param-OverlayColor')).toHaveAttribute('type', 'color');
    await expect(page.locator('.inspector-pane .color-preview-box[role="button"]')).toHaveAttribute('tabindex', '0');
    await expect(page.locator('.inspector-pane .color-preview-box[role="button"]')).toHaveAttribute('aria-label', '选择叠加颜色');
    await expect(page.locator('.preview-workbench-pane')).toContainText('预览工作台');
    await expect(page.locator('.preview-workbench-pane')).toContainText('端口与耗时');
    await expect(page.locator('.preview-workbench-pane')).toContainText('模块结果');
    await expect(page.locator('.preview-workbench-pane')).toContainText('没有返回图像输出');
  });

  test('resizes the preview workbench wider with a real splitter drag and keeps core controls first-screen', async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1366, height: 768 });
    previewMode.value = 'success-image';
    await addNodeFromFlyout(page);

    await expect(page.locator('.preview-workbench-pane')).toContainText('预览完成');
    await expect(page.locator('.preview-workbench-pane .preview-capability-main-image img')).toBeVisible();
    await captureFlowLayoutState(page, testInfo, 'default-layout');

    const { before, after } = await expandPreviewWorkbench(page);
    expect(after.preview?.width ?? 0).toBeGreaterThan((before.preview?.width ?? 0) + 80);
    expect(after.preview?.width ?? 0).toBeGreaterThan(OLD_PREVIEW_WORKBENCH_MAX_WIDTH + 30);
    expect(after.workspace?.width ?? 0).toBeLessThan((before.workspace?.width ?? 0) - 60);
    expect(after.workspace?.width ?? 0).toBeGreaterThanOrEqual(300);
    expect(after.inspector?.width ?? 0).toBeGreaterThanOrEqual(250);
    expect(after.imageStage?.width ?? 0).toBeGreaterThan((before.imageStage?.width ?? 0) + 80);
    expect(after.imageStage?.height ?? 0).toBeGreaterThan((before.imageStage?.height ?? 0) + 40);
    expect(after.overflow).toEqual({ document: false, body: false, main: false });

    const storedWidth = await page.evaluate(storageKey => localStorage.getItem(storageKey), PROPERTY_SIDEBAR_STORAGE_KEY);
    expect(storedWidth).toBe(String(Math.round(after.preview?.width ?? 0)));
    await writeFile(testInfo.outputPath('flow-layout-vm-resize-metrics.json'), JSON.stringify({
      oldPreviewWorkbenchMaxWidth: OLD_PREVIEW_WORKBENCH_MAX_WIDTH,
      storedWidth,
      before,
      after,
    }, null, 2), 'utf8');
    await captureFlowLayoutState(page, testInfo, 'preview-workbench-resized-wider');
    await captureFlowLayoutState(page, testInfo, 'success-output-image-expanded');
  });

  test('shows output image summary and debug image operations in the right workbench', async ({ page }, testInfo) => {
    previewMode.value = 'success-image';
    await addNodeFromFlyout(page);

    const workbench = page.locator('.preview-workbench-pane');
    await expect(workbench).toContainText('预览完成');
    await expect(workbench.locator('.preview-capability-main-image img')).toBeVisible();
    await expect(workbench).toContainText('端口与耗时');
    await expect(workbench).toContainText('运行耗时');
    await expect(workbench).toContainText('Score');
    await expect(workbench).toContainText('Width');
    await expect(workbench.locator('[data-preview-action="image-fit"]')).toContainText('适应窗口');
    await expect(workbench.locator('[data-preview-action="image-original"]')).toContainText('原始大小');
    await expect(workbench.locator('[data-preview-action="open-image"]')).toContainText('打开大图');
    await expect(workbench.locator('[data-preview-action="image-fit"]')).toHaveAttribute('aria-pressed', 'true');
    await expect(workbench.locator('[data-preview-action="image-original"]')).toHaveAttribute('aria-pressed', 'false');

    await workbench.locator('[data-preview-action="image-original"]').click();
    await expect(workbench.locator('.preview-capability-main-image')).toHaveAttribute('data-image-mode', 'original');
    await expect(workbench.locator('[data-preview-action="image-fit"]')).toHaveAttribute('aria-pressed', 'false');
    await expect(workbench.locator('[data-preview-action="image-original"]')).toHaveAttribute('aria-pressed', 'true');
    await workbench.locator('[data-preview-action="image-fit"]').click();
    await expect(workbench.locator('.preview-capability-main-image')).toHaveAttribute('data-image-mode', 'fit');
    await expect(workbench.locator('[data-preview-action="image-fit"]')).toHaveAttribute('aria-pressed', 'true');
    await expect(workbench.locator('[data-preview-action="image-original"]')).toHaveAttribute('aria-pressed', 'false');
    await assertNoHorizontalOverflow(page);
    await captureFlowLayoutState(page, testInfo, 'success-output-image');
  });

  test('routes a real retargeted pointermove through the image stage', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', message => {
      if (message.type() === 'error') {
        consoleErrors.push(message.text());
      }
    });
    page.on('pageerror', error => consoleErrors.push(error.message));

    previewMode.value = 'success-pixel-image';
    await addNodeFromFlyout(page);
    await expect(page.locator('#preview-panel .preview-capability-main-image img')).toBeVisible();

    const target = await page.evaluate(() => {
      const container = document.querySelector('#preview-panel');
      const stage = container?.querySelector('.preview-capability-image-stage') as HTMLElement | null;
      const image = stage?.querySelector('img') as HTMLImageElement | null;
      if (!container || !stage || !image || !stage.parentElement) {
        throw new Error('Pixel probe stage is unavailable for retargeted pointer testing.');
      }

      const host = document.createElement('div');
      host.dataset.role = 'pixel-probe-retarget-host';
      host.style.cssText = 'display:block;width:100%;height:260px;position:relative;';
      const shadow = host.attachShadow({ mode: 'open' });
      stage.parentElement.replaceChild(host, stage);
      stage.style.cssText = 'display:block;position:relative;width:100%;height:100%;overflow:hidden;background:#08080a;';
      image.style.cssText = 'display:block;width:100%;height:100%;object-fit:contain;';
      shadow.appendChild(stage);
      container.addEventListener('pointermove', event => {
        const path = event.composedPath();
        (window as any).__pixelProbeRetargetPath = {
          targetIsHost: event.target === host,
          stageIsInComposedPath: path.includes(stage),
        };
      }, { capture: true });

      const rect = image.getBoundingClientRect();
      return {
        x: rect.left + (rect.width / 2),
        y: rect.top + (rect.height / 2),
      };
    });

    await page.mouse.move(target.x, target.y);
    const status = page.locator('#preview-panel [data-role="pixel-probe-status"]');
    await expect(status).toHaveAttribute('data-probe-state', 'pixel');
    expect(await page.evaluate(() => (window as any).__pixelProbeRetargetPath)).toEqual({
      targetIsHost: true,
      stageIsInComposedPath: true,
    });
    expect(consoleErrors).toEqual([]);
  });

  test('keeps the pixel probe live across hover, lock, ROI, and preview changes', async ({ page }, testInfo) => {
    const consoleErrors: string[] = [];
    page.on('console', message => {
      if (message.type() === 'error') {
        consoleErrors.push(message.text());
      }
    });
    page.on('pageerror', error => consoleErrors.push(error.message));

    previewMode.value = 'success-pixel-image';
    await addNodeFromFlyout(page);
    const workbench = page.locator('.preview-workbench-pane');
    const image = workbench.locator('.preview-capability-main-image img');
    const status = workbench.locator('[data-role="pixel-probe-status"]');
    await expect(image).toBeVisible();
    await expect(status).toHaveAttribute('data-probe-state', 'default');

    await page.evaluate(async () => {
      const registry = (await import('/src/core/app/serviceRegistry.js')).default;
      const owner = registry.get('previewPanelCapabilityOwner');
      if (!owner?.pixelProbe) {
        throw new Error('PreviewPanelCapabilityOwner is unavailable.');
      }

      (window as any).__pixelProbeOwner = owner;
      (window as any).__pixelProbeCalls = 0;
      const probePoint = owner.pixelProbe.probePoint.bind(owner.pixelProbe);
      owner.pixelProbe.probePoint = (...args: any[]) => {
        (window as any).__pixelProbeCalls += 1;
        return probePoint(...args);
      };
    });

    await page.evaluate(() => {
      const stage = document.querySelector('#preview-panel .preview-capability-image-stage') as HTMLElement | null;
      if (!stage) {
        throw new Error('Pixel probe stage is unavailable for fit-mode testing.');
      }

      stage.style.width = '300px';
      stage.style.height = '300px';
      stage.style.minHeight = '0';
      stage.style.maxHeight = 'none';
    });

    await image.scrollIntoViewIfNeeded();
    const fit = await getPixelProbeGeometry(page);
    expect(fit.naturalWidth).toBe(64);
    expect(fit.naturalHeight).toBe(48);
    expect(fit.content.top - fit.image.top).toBeGreaterThan(1);

    const firstPoint = pointInPixelProbeImage(fit, 0.2, 0.25);
    await page.mouse.move(firstPoint.x, firstPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'pixel');
    const firstText = (await status.textContent()) || '';
    expect(firstText).toMatch(/X:\s*\d+\s+Y:\s*\d+/);
    expect(firstText).toMatch(/RGB:\s*\d+,\d+,\d+|灰度:\s*\d+/);
    expect(firstText).toContain('图像: 64x48');
    expect(firstText).toMatch(/缩放:\s*\d+%/);
    expect(await status.evaluate(element => getComputedStyle(element).color)).toBe('rgb(248, 250, 252)');

    await page.evaluate(() => {
      (window as any).__pixelProbeStage = document.querySelector('#preview-panel .preview-capability-image-stage');
    });
    const secondPoint = pointInPixelProbeImage(fit, 0.75, 0.75);
    await page.mouse.move(secondPoint.x, secondPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'pixel');
    const secondText = (await status.textContent()) || '';
    expect(parsePixelProbeCoordinates(secondText)).not.toEqual(parsePixelProbeCoordinates(firstText));
    expect(await page.evaluate(() =>
      (window as any).__pixelProbeStage === document.querySelector('#preview-panel .preview-capability-image-stage'))
    ).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('pixel-probe-hover.png'), fullPage: false });

    const letterboxPoint = await findFitLetterboxPoint(page, fit);
    await page.mouse.move(letterboxPoint.x, letterboxPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'outside');
    await page.mouse.move(5, 5);
    await expect(status).toHaveAttribute('data-probe-state', 'default');

    await workbench.locator('[data-preview-action="image-original"]').click();
    await expect(workbench.locator('.preview-capability-main-image')).toHaveAttribute('data-image-mode', 'original');
    const original = await getPixelProbeGeometry(page);
    const originalPoint = pointInPixelProbeImage(original, 0.25, 0.25);
    await page.mouse.move(originalPoint.x, originalPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'pixel');
    expect((await status.textContent()) || '').toMatch(/缩放:\s*100%/);

    await workbench.locator('[data-preview-action="image-fit"]').click();
    await expect(workbench.locator('.preview-capability-main-image')).toHaveAttribute('data-image-mode', 'fit');
    const lockGeometry = await getPixelProbeGeometry(page);
    const lockPoint = pointInPixelProbeImage(lockGeometry, 0.35, 0.35);
    const movedPoint = pointInPixelProbeImage(lockGeometry, 0.7, 0.7);
    await page.mouse.move(lockPoint.x, lockPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'pixel');
    await page.mouse.click(lockPoint.x, lockPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'locked');
    const lockedText = (await status.textContent()) || '';
    expect(lockedText).toContain('已锁定');
    await expect(workbench.locator('[data-role="pixel-probe-crosshair"]')).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath('pixel-probe-locked.png'), fullPage: false });

    await page.mouse.move(movedPoint.x, movedPoint.y);
    expect((await status.textContent()) || '').toBe(lockedText);

    await workbench.locator('[data-preview-action="clear-pixel-lock"]').click();
    await expect(status).toHaveAttribute('data-probe-state', 'default');
    await page.mouse.move(movedPoint.x, movedPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'pixel');

    await page.mouse.click(lockPoint.x, lockPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'locked');
    await page.keyboard.press('Escape');
    await expect(status).toHaveAttribute('data-probe-state', 'default');
    await page.mouse.move(movedPoint.x, movedPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'pixel');

    const roiStart = pointInPixelProbeImage(lockGeometry, 0.15, 0.2);
    const roiEnd = pointInPixelProbeImage(lockGeometry, 0.8, 0.8);
    await page.mouse.move(roiStart.x, roiStart.y);
    await page.mouse.down();
    await page.mouse.move(roiEnd.x, roiEnd.y, { steps: 8 });
    await page.mouse.up();
    await expect(status).toHaveAttribute('data-probe-state', 'roi');
    expect((await status.textContent()) || '').toContain('ROI');
    await workbench.locator('[data-preview-action="clear-pixel-roi"]').click();
    await expect(status).toHaveAttribute('data-probe-state', 'default');
    await page.mouse.move(movedPoint.x, movedPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'pixel');

    const requestsBeforeSwitch = previewMode.requests.length;
    await openPreprocessFlyout(page);
    await page.locator('#operator-group-flyout .operator-flyout-item[data-operator-type="GaussianBlur"]').click();
    await expect.poll(() => previewMode.requests.length).toBeGreaterThan(requestsBeforeSwitch);
    await expect(status).toHaveAttribute('data-probe-state', 'default');
    expect((await status.textContent()) || '').not.toMatch(/已锁定|ROI/);

    await page.evaluate(() => {
      (window as any).__pixelProbeCalls = 0;
    });
    const switchedGeometry = await getPixelProbeGeometry(page);
    const switchedPoint = pointInPixelProbeImage(switchedGeometry, 0.5, 0.5);
    await page.mouse.move(switchedPoint.x, switchedPoint.y);
    await expect(status).toHaveAttribute('data-probe-state', 'pixel');
    await expect.poll(() => page.evaluate(() => (window as any).__pixelProbeCalls)).toBe(1);
    expect(await page.evaluate(async () => {
      const registry = (await import('/src/core/app/serviceRegistry.js')).default;
      return registry.get('previewPanelCapabilityOwner') === (window as any).__pixelProbeOwner;
    })).toBe(true);
    expect(consoleErrors).toEqual([]);
  });

  test('marks old preview stale after parameter edit and clears stale after manual preview', async ({ page }, testInfo) => {
    previewMode.value = 'success-image';
    await addNodeFromFlyout(page);
    const workbench = page.locator('.preview-workbench-pane');
    await expect(workbench).toContainText('预览完成');
    await expandPreviewWorkbench(page);

    await page.evaluate(() => {
      (window as any).nodePreviewCoordinator.debounceMs = 5000;
    });
    previewMode.requests.length = 0;

    await page.locator('.inspector-pane #param-Threshold').fill('180');
    await page.locator('.inspector-pane #param-Threshold').blur();

    await expect(workbench).toContainText(STALE_PREVIEW_TEXT, { timeout: 1000 });
    await expect(workbench.locator('.preview-capability-main-image')).toHaveAttribute('data-stale', 'true');
    expect(previewMode.requests).toHaveLength(0);
    await captureFlowLayoutState(page, testInfo, 'stale-old-preview-expanded');

    previewMode.delayMs = 0;
    await workbench.locator('[data-preview-action="manual-preview"]').click();
    await expect(workbench).not.toContainText(STALE_PREVIEW_TEXT);
    await expect(workbench).toContainText('预览完成');
    await expect(workbench.locator('.preview-capability-main-image')).toHaveAttribute('data-stale', 'false');
  });

  test('prevents duplicate manual preview while loading and exposes cancel state', async ({ page }, testInfo) => {
    previewMode.value = 'success-image';
    await addNodeFromFlyout(page);
    const workbench = page.locator('.preview-workbench-pane');
    await expect(workbench).toContainText('预览完成');
    await expandPreviewWorkbench(page);

    previewMode.requests.length = 0;
    previewMode.delayMs = 600;
    const manualButton = workbench.locator('[data-preview-action="manual-preview"]');
    const cancelButton = workbench.locator('[data-preview-action="cancel-preview"]');

    await manualButton.click();
    await expect(manualButton).toBeDisabled();
    await expect(manualButton).toContainText('预览中...');
    await expect(cancelButton).toBeEnabled();
    await expect.poll(() => previewMode.requests.length).toBe(1);
    await captureFlowLayoutState(page, testInfo, 'manual-preview-loading-expanded');

    await cancelButton.click();
    await expect(cancelButton).toBeDisabled();
    await captureFlowLayoutState(page, testInfo, 'manual-preview-canceled');
    await expect(workbench).toContainText('预览已取消');
    expect(previewMode.requests).toHaveLength(1);
    previewMode.delayMs = 0;
  });

  test('keeps migrated acquisition file picker and writes picked file path back to the node', async ({ page }, testInfo) => {
    await openInputFlyout(page);
    await page.locator('#operator-group-flyout .operator-flyout-item', { hasText: '图像采集' }).click();
    await expect(page.locator('#operator-group-flyout')).toBeHidden();

    const fileInput = page.locator('.inspector-pane input[name="FilePath"]');
    const pickerButton = page.locator('.inspector-pane .btn-pick-file[data-param="FilePath"]');
    const workbench = page.locator('.preview-workbench-pane');
    await expect(page.locator('.inspector-pane')).toContainText('图像采集');
    await expect(page.locator('.inspector-pane .property-form')).toBeVisible();
    await expect(fileInput).toBeVisible();
    await expect(fileInput).toHaveAttribute('readonly', '');
    await expect(pickerButton).toBeVisible();
    await expect(pickerButton).toHaveAttribute('aria-label', '选择文件路径');
    await expect(page.locator('.inspector-pane select[data-camera-binding-select="true"]')).toHaveCount(1);
    await expect(page.locator('.inspector-pane #operator-preview-container')).toHaveCount(0);
    await expect(workbench).toContainText('缺输入图或采集源');
    await expect(workbench).toContainText('请先配置文件路径');
    await expect(workbench).not.toContainText('预览完成，但没有返回图像输出');
    await expandPreviewWorkbench(page);

    await captureFlowLayoutState(page, testInfo, 'image-acquisition-file-missing-path');

    await pickerButton.click();
    const pickMessage = await page.evaluate(() => (window as any).__pickFileMessages.at(-1));
    expect(pickMessage.messageType).toBe('PickFileCommand');
    expect(pickMessage.parameterName).toBe('FilePath');
    expect(pickMessage.filter).toContain('Image Files');

    await page.evaluate(() => {
      (window as any).__cvDispatchWebViewMessage({
        messageType: 'FilePickedEvent',
        payload: {
          parameterName: 'FilePath',
          filePath: 'C:\\Data\\sample.png',
        },
      });
    });

    await expect(fileInput).toHaveValue('C:\\Data\\sample.png');
    await expect.poll(async () => page.evaluate(() => {
      const flowCanvas = (window as any).flowCanvas;
      const node = flowCanvas.nodes.get(flowCanvas.selectedNode);
      const parameter = node.parameters.find((item: any) => item.name === 'FilePath');
      return parameter?.value;
    })).toBe('C:\\Data\\sample.png');
    await captureFlowLayoutState(page, testInfo, 'image-acquisition-file-picked');
  });

  test('syncs dependency-controlled fields for template matching', async ({ page }) => {
    await addNodeFromFlyout(page, '模板匹配');

    const status = page.locator('.inspector-pane [data-property-capability-status]');
    const templatePathGroup = page.locator('.inspector-pane .form-group[data-parameter-name="TemplatePath"]');
    const templatePathInput = page.locator('.inspector-pane #param-TemplatePath');
    const templatePathPicker = page.locator('.inspector-pane .btn-pick-file[data-param="TemplatePath"]');
    const templateIdInput = page.locator('.inspector-pane #param-TemplateId');

    await expect(templatePathInput).toBeEnabled();
    await expect(templatePathPicker).toBeEnabled();
    await expect(templateIdInput).toBeEnabled();

    await templateIdInput.fill('tpl-01');
    await templateIdInput.blur();

    await expect(templatePathInput).toBeDisabled();
    await expect(templatePathPicker).toBeDisabled();
    await expect(templatePathGroup).toHaveClass(/is-rule-disabled/);
    await expect(templatePathGroup).toHaveAttribute('data-effective-disabled', 'true');
    await expect(templatePathGroup.locator('.required')).toHaveCount(0);
    await expect(templatePathGroup.locator('[data-parameter-rule-hint="true"]')).toHaveCount(0);
    await expect(templatePathGroup).not.toContainText('Template path is disabled');
    await expect(templateIdInput).toBeEnabled();
    await expect(page.locator('.inspector-pane .validation-error')).toHaveCount(0);
    await expect(status).toContainText('参数已更新');
  });

  test('shows missing camera prerequisite for camera acquisition without CameraId', async ({ page }, testInfo) => {
    await openInputFlyout(page);
    await page.locator('#operator-group-flyout .operator-flyout-item', { hasText: '图像采集' }).click();
    await expect(page.locator('#operator-group-flyout')).toBeHidden();

    await expect(page.locator('.inspector-pane .property-form')).toBeVisible();
    await page.locator('.inspector-pane #param-SourceType').selectOption('Camera');

    const workbench = page.locator('.preview-workbench-pane');
    const cameraGroup = page.locator('.inspector-pane .form-group[data-parameter-name="CameraId"]');
    const cameraBindingGroup = page.locator('.inspector-pane .form-group[data-parameter-name="CameraBindingId"]');
    const cameraErrors = page.locator('.inspector-pane .validation-error', { hasText: '请先选择相机或相机绑定' });
    await expect(cameraErrors).toHaveCount(2);
    await expect(cameraGroup).toHaveClass(/invalid/);
    await expect(cameraBindingGroup).toHaveClass(/invalid/);
    await expect(page.locator('.inspector-pane #param-CameraId')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('.inspector-pane #param-CameraBindingId')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('.inspector-pane [data-property-capability-status]')).toContainText('参数校验失败');
    await expect(workbench).toContainText('请先选择相机');
    await expect(workbench).toContainText('缺输入图或采集源');
    await expect(workbench).not.toContainText('预览完成，但没有返回图像输出');
    await expect(page.locator('.inspector-pane #operator-preview-container')).toHaveCount(0);
    await expandPreviewWorkbench(page);
    await captureFlowLayoutState(page, testInfo, 'camera-missing-camera');

    previewMode.requests.length = 0;
    await page.locator('.inspector-pane #param-CameraBindingId').fill('line-camera-01');
    await page.locator('.inspector-pane #param-CameraBindingId').blur();

    await expect(page.locator('.inspector-pane .validation-error')).toHaveCount(0);
    await expect(page.locator('.inspector-pane [data-property-capability-status]')).toContainText('参数已更新');
    await expect(workbench).toContainText('需手动预览');
    await expect(workbench).not.toContainText('请先选择相机');
    await expect(workbench).not.toContainText('刷新预览');

    await captureFlowLayoutState(page, testInfo, 'camera-binding-manual-required-expanded');

    await workbench.locator('[data-preview-action="manual-preview"]').click();
    await expect.poll(() => previewMode.requests.length).toBe(1);
    await expect(workbench.locator('.preview-capability-owner')).toHaveAttribute('data-status', 'success');
    await page.setViewportSize({ width: 1366, height: 420 });
    await assertLowHeightScrollability(page);
    await captureFlowLayoutState(page, testInfo, '1366x420-low-height-compact');
    await expect(workbench).toContainText('预览完成');
  });

  test('shows blank, no-image and preview-failure states', async ({ page }, testInfo) => {
    await captureFlowLayoutState(page, testInfo, 'no-operator-selected');

    await expect(page.locator('.inspector-pane')).toContainText('未选择算子');
    await expect(page.locator('.preview-workbench-pane')).toContainText('请选择一个算子');

    await addNodeFromFlyout(page);
    await expect(page.locator('.preview-workbench-pane')).toContainText('没有返回图像输出');
    await expandPreviewWorkbench(page);

    previewMode.value = 'error';
    await page.locator('.preview-workbench-pane [data-preview-action="manual-preview"]').click();
    await expect(page.locator('.preview-workbench-pane')).toContainText('预览失败');
    await expect(page.locator('.preview-workbench-pane')).toContainText('模拟预览失败');
    await captureFlowLayoutState(page, testInfo, 'preview-failure-error-expanded');
  });

  test('clears current preview state for connection, blank selection and deleted selected node', async ({ page }) => {
    previewMode.value = 'success-image';
    await addNodeFromFlyout(page);
    const workbench = page.locator('.preview-workbench-pane');
    await expect(workbench).toContainText('预览完成');
    await expect(workbench).toContainText('Score');

    await page.evaluate(() => {
      const flowCanvas = (window as any).flowCanvas;
      const selected = flowCanvas.nodes.get(flowCanvas.selectedNode);
      const next = flowCanvas.addNode('GaussianBlur', selected.x + 260, selected.y, {
        title: '高斯滤波',
        parameters: [{ name: 'Sigma', value: 1.2 }],
        inputs: [{ name: 'Image', type: 'Image' }],
        outputs: [{ name: 'Image', type: 'Image' }],
      });
      const connection = flowCanvas.addConnection(selected.id, 0, next.id, 0);
      flowCanvas.selectedNode = null;
      flowCanvas.selectedConnection = connection;
      flowCanvas.markSelectionChanged('test-select-connection');
      flowCanvas.render();
    });

    await expect(workbench).toContainText('当前连线');
    await expect(workbench).toContainText('连线用于传递端口数据');
    await expect(workbench).toContainText('模块结果');
    await expect(workbench).not.toContainText('中间结果');
    await expect(workbench).not.toContainText('Score');

    await page.evaluate(() => {
      const flowCanvas = (window as any).flowCanvas;
      flowCanvas.selectedNode = null;
      flowCanvas.selectedConnection = null;
      flowCanvas.markSelectionChanged('test-clear-selection');
      flowCanvas.render();
    });

    await expect(workbench).toContainText('请选择一个算子');
    await expect(workbench).not.toContainText('Score');

    await addNodeFromFlyout(page, '阈值分割');
    await expect(workbench).toContainText('预览完成');
    await page.evaluate(() => {
      const flowCanvas = (window as any).flowCanvas;
      flowCanvas.removeNode(flowCanvas.selectedNode);
    });

    await expect(workbench).toContainText(/节点已删除|请选择一个算子/);
    await expect(workbench).not.toContainText('Score');
  });

  test('shows layered Chinese diagnostics for missing resources and failed operator metadata', async ({ page }) => {
    previewMode.value = 'error-diagnostics';
    await addNodeFromFlyout(page);
    const workbench = page.locator('.preview-workbench-pane');

    await expect(workbench).toContainText('预览失败');
    await expect(workbench).toContainText('参数校验失败');
    await expect(workbench).toContainText('缺少资源');
    await expect(workbench).toContainText('失败算子');
    await expect(workbench).toContainText('定位算子');
    await expect(workbench).toContainText('VAL001');

    const text = await workbench.textContent();
    expect(text ?? '').not.toContain('C:\\Users\\A');
    expect(text ?? '').toContain('[redacted-path]');
  });

  test('keeps the 1024x768 narrow layout usable without horizontal overflow', async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1024, height: 768 });
    previewMode.value = 'success-image';
    await addNodeFromFlyout(page);

    const measurements = await collectFlowLayoutMeasurements(page);
    expect(measurements.preview?.width ?? 0).toBeGreaterThanOrEqual(300);
    expect(measurements.workspace?.width ?? 0).toBeGreaterThanOrEqual(280);
    expect(measurements.overflow).toEqual({ document: false, body: false, main: false });
    await captureFlowLayoutState(page, testInfo, '1024x768-narrow-layout');
  });

  test('keeps 1366 and 1920 layouts within viewport and drag-drop coordinates stable', async ({ page }) => {
    await page.setViewportSize({ width: 1366, height: 768 });
    await openPreprocessFlyout(page);
    await assertNoHorizontalOverflow(page);

    await page.setViewportSize({ width: 1920, height: 1080 });
    await assertNoHorizontalOverflow(page);

    const dropResult = await page.evaluate(() => {
      const flowCanvas = (window as any).flowCanvas;
      const canvas = document.querySelector('#flow-canvas') as HTMLCanvasElement;
      const item = document.querySelector('.operator-flyout-item[data-operator-type="GaussianBlur"]') as HTMLElement;
      if (!flowCanvas || !canvas || !item) {
        throw new Error('缺少拖拽验证元素');
      }

      flowCanvas.scale = 1.25;
      flowCanvas.offset = { x: 80, y: 40 };
      flowCanvas.render?.();

      const rect = canvas.getBoundingClientRect();
      const clientX = rect.left + 300;
      const clientY = rect.top + 220;
      const expected = {
        x: (clientX - rect.left) / flowCanvas.scale + flowCanvas.offset.x,
        y: (clientY - rect.top) / flowCanvas.scale + flowCanvas.offset.y,
      };
      const beforeIds = new Set(Array.from(flowCanvas.nodes.keys()));
      const dataTransfer = new DataTransfer();

      item.dispatchEvent(new DragEvent('dragstart', {
        bubbles: true,
        cancelable: true,
        dataTransfer,
      }));
      canvas.dispatchEvent(new DragEvent('dragover', {
        bubbles: true,
        cancelable: true,
        clientX,
        clientY,
        dataTransfer,
      }));
      canvas.dispatchEvent(new DragEvent('drop', {
        bubbles: true,
        cancelable: true,
        clientX,
        clientY,
        dataTransfer,
      }));

      const added = Array.from(flowCanvas.nodes.values()).find((node: any) => !beforeIds.has(node.id)) as any;
      return {
        expected,
        actual: added ? { x: added.x, y: added.y, type: added.type } : null,
      };
    });

    expect(['GaussianBlur', 'Filtering']).toContain(dropResult.actual?.type);
    expect(dropResult.actual?.x).toBeCloseTo(dropResult.expected.x, 5);
    expect(dropResult.actual?.y).toBeCloseTo(dropResult.expected.y, 5);
  });
});
