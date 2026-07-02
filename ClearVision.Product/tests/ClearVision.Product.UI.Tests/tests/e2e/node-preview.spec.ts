import { test, expect } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==';

async function stubOperatorLibrary(page) {
  await page.route('**/api/operators/types', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]',
    });
  });

  await page.route('**/api/operators/library', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: '[]',
    });
  });
}

async function setCurrentProject(page) {
  await page.evaluate(async () => {
    const projectModule = await import('/src/features/project/projectManager.js');
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    projectModule.setCurrentProject({
      id: 'e2e-project',
      name: 'E2E Project',
      description: '',
      flow: null,
    });
    inspectionModule.default.setProject('e2e-project');
  });
}

async function addAndSelectNode(page, config: {
  type: string;
  title: string;
  x?: number;
  y?: number;
  parameters?: unknown[];
  inputs?: Array<{ name: string; type: string }>;
  outputs?: Array<{ name: string; type: string }>;
}) {
  return page.evaluate(nodeConfig => {
    const flowCanvas = (window as any).flowCanvas;
    const node = flowCanvas.addNode(
      nodeConfig.type,
      nodeConfig.x ?? 120,
      nodeConfig.y ?? 120,
      {
        title: nodeConfig.title,
        parameters: nodeConfig.parameters ?? [],
        inputs: nodeConfig.inputs ?? [{ name: 'input', type: 'Image' }],
        outputs: nodeConfig.outputs ?? [{ name: 'output', type: 'Image' }],
        color: '#1890ff',
      }
    );

    (window as any).__e2ePreviewNodeId = node.id;
    flowCanvas.selectedNode = node.id;
    flowCanvas.onNodeSelected?.(node);
    return node.id;
  }, config);
}

function buildObservationResponse(request: any, detail: any, artifacts: any[] = []) {
  const targetNodeId = request.targetNodeId || request.TargetNodeId;
  return {
    success: true,
    executionTimeMs: 21,
    outputData: { Score: 0.98 },
    artifacts,
    observation: {
      schemaVersion: 'execution-observation.v1',
      observedAtUtc: '2026-07-02T08:00:00Z',
      identity: {
        projectId: request.projectId || request.ProjectId,
        targetNodeId,
        debugSessionId: request.debugSessionId || request.DebugSessionId,
        clientRequestSequence: request.clientRequestSequence || request.ClientRequestSequence,
        flowRevision: request.flowRevision || request.FlowRevision,
      },
      outcome: {
        success: true,
        executionTimeMs: 21,
        errorMessage: null,
        failedOperatorId: null,
        failedOperatorName: null,
        failedOperatorType: null,
        executedOperatorCount: 2,
      },
      summary: [
        {
          key: 'Score',
          displayValue: '0.98',
          originalType: 'System.Double',
          pathHint: '$["Score"]',
          addressable: true,
        },
      ],
      detail,
      diagnostics: [],
      limits: {
        maxDepth: 4,
        maxObjectFields: 64,
        maxCollectionItems: 64,
        maxStringChars: 1024,
        maxNodes: 2048,
        maxDetailBytes: 262144,
      },
      truncated: false,
    },
  };
}

function buildLargeDetail() {
  return {
    kind: 'dictionary',
    displayValue: '180/180 fields',
    originalType: 'System.Collections.Generic.Dictionary',
    pathHint: '$',
    addressable: false,
    truncated: false,
    children: Array.from({ length: 180 }, (_, index) => ({
      kind: 'number',
      displayValue: index === 3 ? '<script>alert(1)</script><img src=x onerror=alert(2)>' : `Value${index}`,
      originalType: 'System.Int32',
      name: `Field${index}`,
      pathHint: `$["Field${index}"]`,
      addressable: index % 2 === 0,
      truncated: false,
      children: [],
    })),
  };
}

test.describe('Node Preview Overlay', () => {
  test.beforeEach(async ({ page }) => {
    await stubOperatorLibrary(page);
    await bootAuthenticatedApp(page);
    await setCurrentProject(page);
  });

  test('shows overlay for image-output nodes', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          outputImageBase64: PNG_BASE64,
          outputData: {
            OriginalImage: 'hidden-clean-image',
            Score: 0.98,
            Label: 'OK',
            Count: 1,
          },
          executionTimeMs: 12,
        }),
      });
    });

    await addAndSelectNode(page, {
      type: 'PreviewImageNode',
      title: '图像预览节点',
      parameters: [
        { name: 'Threshold', displayName: 'Threshold', dataType: 'int', value: 10, defaultValue: 10 },
      ],
      outputs: [{ name: 'Image', type: 'Image' }],
    });

    await expect(page.locator('.node-preview-card')).toBeVisible();
    await expect(page.locator('.node-preview-card img')).toBeVisible();
    await expect(page.locator('#preview-status-text')).toContainText('预览完成');
    await expect(page.locator('#preview-output-list')).toContainText('Score');
    await expect(page.locator('#preview-output-list')).not.toContainText('OriginalImage');
    await expect(page.locator('#preview-output-list')).not.toContainText('hidden-clean-image');
  });

  test('summarizes structured outputs in the property preview and overlay without raw JSON overflow', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          outputImageBase64: PNG_BASE64,
          outputData: {
            Detections: {
              detections: [
                { label: 'Wire_Black', confidence: 0.77 },
                { label: 'Wire_Red', confidence: 0.61 },
              ],
            },
            SuppressedDetections: {
              detections: [
                { label: 'Wire_Blue', confidence: 0.25 },
              ],
            },
            Meta: {
              station: 'S1',
              mode: 'Auto',
            },
            InternalNmsEnabled: false,
            RawCandidateCount: 5,
            VisualizationDetectionCount: 2,
          },
          executionTimeMs: 18,
        }),
      });
    });

    await addAndSelectNode(page, {
      type: 'PreviewImageNode',
      title: 'Structured Summary Node',
      outputs: [{ name: 'Image', type: 'Image' }],
    });

    await expect(page.locator('#preview-output-list')).toContainText('Detections');
    await expect(page.locator('#preview-output-list')).toContainText('2 detections');
    await expect(page.locator('#preview-output-list')).toContainText('1 suppressed');
    await expect(page.locator('#preview-output-list')).toContainText('2 fields');
    await expect(page.locator('#preview-output-list')).not.toContainText('Wire_Black');
    await expect(page.locator('#preview-output-list')).not.toContainText('{"detections"');

    await expect(page.locator('.node-preview-summary')).toContainText('2 detections');
    await expect(page.locator('.node-preview-summary')).toContainText('1 suppressed');
    await expect(page.locator('#preview-diagnostics-panel .ac-card').first()).toBeVisible();

    const outputListFits = await page.locator('#preview-output-list').evaluate(node =>
      node.scrollWidth <= node.clientWidth + 1
    );
    expect(outputListFits).toBe(true);
  });

  test('keeps overlay hidden for non-image nodes while right panel still shows summary', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          outputData: {
            Result: 'OK',
            Count: 3,
          },
          executionTimeMs: 9,
        }),
      });
    });

    await addAndSelectNode(page, {
      type: 'StringSummaryNode',
      title: '摘要节点',
      outputs: [{ name: 'Text', type: 'String' }],
    });

    await page.waitForTimeout(700);
    await expect(page.locator('.node-preview-card')).toHaveCount(0);
    await expect(page.locator('#preview-output-list')).toContainText('Result');
  });

  test('parameter change triggers one debounced preview and panning does not trigger extra preview', async ({ page }) => {
    let previewCallCount = 0;
    await page.route('**/api/flows/preview-node', async route => {
      previewCallCount += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          outputImageBase64: PNG_BASE64,
          outputData: {
            Score: previewCallCount,
          },
          executionTimeMs: 15,
        }),
      });
    });

    await addAndSelectNode(page, {
      type: 'PreviewImageNode',
      title: '图像预览节点',
      parameters: [
        { name: 'Threshold', displayName: 'Threshold', dataType: 'int', value: 10, defaultValue: 10 },
      ],
      outputs: [{ name: 'Image', type: 'Image' }],
    });

    await page.waitForTimeout(700);
    expect(previewCallCount).toBe(1);

    const overlayBefore = await page.locator('.node-preview-card').boundingBox();

    await page.locator('#param-Threshold').fill('25');
    await page.locator('#param-Threshold').blur();

    await page.waitForTimeout(700);
    expect(previewCallCount).toBe(2);

    const canvas = page.locator('#flow-canvas');
    const box = await canvas.boundingBox();
    if (!box) {
      throw new Error('Canvas bounding box not found');
    }

    await page.mouse.move(box.x + box.width - 30, box.y + box.height - 30);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width - 120, box.y + box.height - 80, { steps: 8 });
    await page.mouse.up();

    await page.waitForTimeout(150);
    expect(previewCallCount).toBe(2);

    const overlayAfter = await page.locator('.node-preview-card').boundingBox();
    const moved =
      overlayBefore &&
      overlayAfter &&
      (Math.abs(overlayBefore.x - overlayAfter.x) > 1 || Math.abs(overlayBefore.y - overlayAfter.y) > 1);
    expect(moved).toBeTruthy();
  });

  test('double click on ForEach still enters subgraph', async ({ page }) => {
    const nodeId = await addAndSelectNode(page, {
      type: 'ForEach',
      title: 'ForEach',
      outputs: [{ name: 'Result', type: 'Any' }],
    });

    const coords = await page.evaluate(selectedNodeId => {
      const flowCanvas = (window as any).flowCanvas;
      const rect = flowCanvas.getNodeScreenRect(selectedNodeId);
      const canvasRect = document.getElementById('flow-canvas').getBoundingClientRect();
      return {
        x: canvasRect.left + rect.x + rect.width / 2,
        y: canvasRect.top + rect.y + rect.height / 2,
      };
    }, nodeId);

    await page.mouse.dblclick(coords.x, coords.y);
    await expect(page.locator('#subgraph-breadcrumb')).toBeVisible();
  });
});

test.describe('Node Preview Inspector flag', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
        value: Object.freeze({
          workspaceV2Enabled: false,
          nodePreviewInspectorEnabled: true,
          featureFlags: {
            'Studio:NodePreviewInspectorEnabled': true,
          },
        }),
        writable: false,
        configurable: false,
      });
    });
    await stubOperatorLibrary(page);
    await bootAuthenticatedApp(page);
    await setCurrentProject(page);
  });

  test('flag on mounts only inspector and keeps detail/artifact state bounded', async ({ page }) => {
    let releasePreview: (() => void) | null = null;
    const previewBlocked = new Promise<void>(resolve => {
      releasePreview = resolve;
    });
    const artifact = {
      artifactId: 'profile-artifact',
      kind: 'profile',
      role: 'profile',
      pathHint: '$["Profile"]',
      contentType: 'application/json',
      length: 128,
      sha256: 'abc123',
      expiresAtUtc: '2026-07-02T09:00:00Z',
    };

    await page.route('**/api/flows/preview-node', async route => {
      const request = route.request().postDataJSON();
      await previewBlocked;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(buildObservationResponse(request, buildLargeDetail(), [artifact])),
      });
    });

    await page.route('**/api/preview-artifacts/profile-artifact', async route => {
      await route.fulfill({
        status: 404,
        contentType: 'text/plain',
        body: 'expired',
      });
    });

    await addAndSelectNode(page, {
      type: 'PreviewImageNode',
      title: 'Inspector Node',
      outputs: [{ name: 'Image', type: 'Image' }],
    });
    await page.evaluate(() => (window as any).nodePreviewCoordinator.invalidateActivePreview({ immediate: true, force: true }));

    await expect(page.locator('.node-preview-inspector-card')).toBeVisible();
    await expect(page.locator('.node-preview-card')).toHaveCount(0);
    await expect(page.locator('.node-preview-inspector-status')).toContainText('loading');
    releasePreview?.();
    await expect(page.locator('.node-preview-inspector-status')).toContainText('success');
    await expect(page.locator('.node-preview-inspector-card')).toContainText('Score');

    const owners = await page.evaluate(() => ({
      hasOverlay: Boolean((window as any).nodePreviewOverlay),
      hasInspector: Boolean((window as any).nodePreviewInspector),
      hasCoordinator: Boolean((window as any).nodePreviewCoordinator),
    }));
    expect(owners).toEqual({
      hasOverlay: false,
      hasInspector: true,
      hasCoordinator: true,
    });

    await page.locator('.node-preview-inspector-tab', { hasText: 'Detail' }).click();
    await expect(page.locator('.node-preview-inspector-tree')).toBeVisible();
    const initialRows = await page.locator('.node-preview-inspector-tree-row').count();
    expect(initialRows).toBeLessThanOrEqual(80);
    await expect(page.locator('.node-preview-inspector-card script')).toHaveCount(0);
    await expect(page.locator('.node-preview-inspector-card')).toContainText('<script>alert(1)</script>');

    await page.locator('.node-preview-inspector-search').fill('Field120');
    await expect(page.locator('.node-preview-inspector-tree-row', { hasText: 'Field120' })).toBeVisible();
    await page.locator('.node-preview-inspector-tree-row', { hasText: 'Field120' }).first().click();

    const selection = await page.evaluate(() => (window as any).nodePreviewSelectionStore.getSelection());
    expect(selection.pathHint).toBe('$["Field120"]');
    expect(selection.identity.targetNodeId).toBeTruthy();
    expect(selection.addressable).toBe(true);

    await page.locator('.node-preview-inspector-tab', { hasText: 'Artifact' }).click();
    await expect(page.locator('.node-preview-inspector-artifact')).toContainText('profile');
    await page.locator('.node-preview-inspector-action-btn', { hasText: '按需读取' }).click();
    await expect(page.locator('.node-preview-inspector-artifact-read.error')).toContainText('资源已过期或不可用');

    await page.evaluate(async () => {
      const projectModule = await import('/src/features/project/projectManager.js');
      projectModule.setCurrentProject({
        id: 'other-project',
        name: 'Other Project',
        description: '',
        flow: null,
      });
    });
    await expect.poll(async () => page.evaluate(() => (window as any).nodePreviewSelectionStore.getSelection()))
      .toBeNull();
  });
});
