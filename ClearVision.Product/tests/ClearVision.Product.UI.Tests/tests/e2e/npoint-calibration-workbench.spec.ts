import { expect, Page, test } from '@playwright/test';
import { mkdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { bootAuthenticatedApp } from './authHelper';

const PREVIEW_PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAACNSURBVHhe7dAxAQAwDITAyn73qQEcwHALI2/bmTWAokkDKJo0gKJJAyiaNICiSQMomjSAokkDKJo0gKJJAyiaNICiSQMomjSAokkDKJo0gKJJAyiaNICiSQMomjSAokkDKJo0gKJJAyiaNICiSQMomjSAokkDKJo0gKJJAyiaNICiSQMomjSAool8wO4D9cdyOzoyljkAAAAASUVORK5CYII=';

const screenshotDir = path.resolve(process.cwd(), '../../..', 'test_results', 'g12b-playwright');

async function stubOperatorLibrary(page: Page) {
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

async function installStartupFlag(page: Page, enabled: boolean) {
  await page.addInitScript(flagEnabled => {
    const startup = {
      featureFlags: Object.freeze({
        'Studio:NPointCalibrationWorkbenchEnabled': flagEnabled,
      }),
    };
    Object.freeze(startup);
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: startup,
      writable: false,
      configurable: false,
      enumerable: true,
    });
  }, enabled);
}

async function setCurrentProject(page: Page) {
  await page.evaluate(async () => {
    const projectModule = await import('/src/features/project/projectManager.js');
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    projectModule.setCurrentProject({
      id: 'e2e-npoint-project',
      name: 'E2E NPoint Project',
      description: '',
      flow: null,
    });
    inspectionModule.default.setProject('e2e-npoint-project');
  });
}

async function stubPreview(page: Page) {
  await page.route('**/api/flows/preview-node', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        inputImageBase64: PREVIEW_PNG_BASE64,
        outputImageBase64: PREVIEW_PNG_BASE64,
        outputData: {
          Points: [
            { x: 8, y: 8 },
            { x: 56, y: 8 },
          ],
          Circle: { centerX: 32, centerY: 32 },
        },
        executionTimeMs: 12,
      }),
    });
  });
}

function createNPointParameters() {
  return [
    { name: 'CalibrationMode', displayName: 'Mode', dataType: 'enum', value: 'Affine', defaultValue: 'Affine', options: ['Affine', 'Perspective'] },
    { name: 'CalibrationUnit', displayName: 'Unit', dataType: 'string', value: 'mm', defaultValue: 'mm' },
    { name: 'PointPairs', displayName: 'PointPairs', dataType: 'string', value: '[]', defaultValue: '[]' },
    { name: 'RansacReprojectionThreshold', displayName: 'RANSAC Threshold', dataType: 'double', value: 3, defaultValue: 3 },
    { name: 'RansacMaxIterations', displayName: 'RANSAC Iterations', dataType: 'int', value: 3000, defaultValue: 3000 },
    { name: 'RansacConfidence', displayName: 'RANSAC Confidence', dataType: 'double', value: 0.995, defaultValue: 0.995 },
    { name: 'MaxAcceptedReprojectionError', displayName: 'Max Error', dataType: 'double', value: 3, defaultValue: 3 },
    { name: 'MinInlierCount', displayName: 'Min Inliers', dataType: 'int', value: 0, defaultValue: 0 },
    { name: 'MinInlierRatio', displayName: 'Min Inlier Ratio', dataType: 'double', value: 0.5, defaultValue: 0.5 },
  ];
}

async function addAndSelectNPointNode(page: Page) {
  return page.evaluate(parameters => {
    const flowCanvas = (window as any).flowCanvas;
    const node = flowCanvas.addNode(
      'NPointCalibration',
      220,
      160,
      {
        title: 'N Point Calibration',
        parameters,
        inputs: [{ name: 'Image', type: 'Image' }],
        outputs: [
          { name: 'CalibrationDraft', type: 'CalibrationDraft' },
          { name: 'CalibrationBundle', type: 'CalibrationBundleV2' },
        ],
        color: '#2563eb',
      }
    );

    flowCanvas.selectedNode = node.id;
    flowCanvas.onNodeSelected?.(node);
    (window as any).__e2eNPointNodeId = node.id;
    return node.id;
  }, createNPointParameters());
}

async function waitForWorkbenchReady(page: Page) {
  await expect(page.locator('[data-testid="npoint-calibration-workbench"]')).toBeVisible();
  await page.waitForFunction(() => {
    const workbench = (window as any).propertyPanel?.calibrationDraftWorkbench;
    return Boolean(workbench?.currentImageSource && workbench?.imageCanvas?.image);
  });
}

async function fillWorldCoordinates(page: Page) {
  const worldCoordinates = [
    [0, 0],
    [10, 0],
    [20, 0],
    [0, 10],
    [10, 10],
    [20, 10],
    [0, 20],
    [10, 20],
    [20, 20],
  ];
  const rows = page.locator('.calibration-draft-table tbody tr');
  await expect(rows).toHaveCount(worldCoordinates.length);

  for (let index = 0; index < worldCoordinates.length; index += 1) {
    const inputs = rows.nth(index).locator('input.calibration-draft-cell-input');
    await inputs.nth(2).fill(String(worldCoordinates[index][0]));
    await inputs.nth(2).dispatchEvent('change');
    await inputs.nth(3).fill(String(worldCoordinates[index][1]));
    await inputs.nth(3).dispatchEvent('change');
  }
}

function createSolvedResponse(request: any) {
  const samples = (request.samples || []).map((sample: any, index: number) => {
    const enabled = sample.enabled !== false;
    const outlier = enabled && index === 7;
    return {
      ...sample,
      enabled,
      inlier: enabled ? !outlier : null,
      reprojectionX: enabled ? Number(sample.pixelX) + (outlier ? 5.5 : 0.12) : null,
      reprojectionY: enabled ? Number(sample.pixelY) + (outlier ? -4.25 : -0.08) : null,
      error: enabled ? (outlier ? 6.95 : 0.16) : null,
    };
  });
  const candidateBundle = {
    version: 2,
    mode: request.mode,
    unit: request.unit,
    accepted: true,
    draftOnly: true,
    notSavedToProjectAssets: true,
    transform: {
      matrix: [
        [1, 0, 0],
        [0, 1, 0],
        [0, 0, 1],
      ],
    },
    quality: {
      meanError: 0.16,
      maxError: 6.95,
      inlierCount: 7,
      enabledSampleCount: 8,
    },
  };

  return {
    success: true,
    status: 'Solved',
    samples,
    lastSolveResult: {
      accepted: true,
      matrix: candidateBundle.transform.matrix,
      meanError: 0.16,
      maxError: 6.95,
      inlierCount: 7,
      inlierRatio: 0.875,
    },
    candidateBundle,
    candidateBundleJson: JSON.stringify(candidateBundle),
    artifacts: [
      { role: 'candidate-bundle', schema: 'calibration-candidate-bundle.v1', artifactId: 'artifact-candidate' },
      { role: 'sample-table', schema: 'calibration-sample-table.v1', artifactId: 'artifact-samples' },
    ],
    diagnostics: ['visual-scene-calibration-draft-truncated=false'],
  };
}

function createFailedResponse(request: any) {
  return {
    success: false,
    status: 'Failed',
    samples: request.samples || [],
    lastSolveResult: null,
    candidateBundle: null,
    candidateBundleJson: null,
    artifacts: [],
    diagnostics: ['Degenerate calibration point set'],
  };
}

async function saveScreenshot(page: Page, name: string) {
  await mkdir(screenshotDir, { recursive: true });
  await page.screenshot({ path: path.join(screenshotDir, name), fullPage: true });
}

test.describe('NPoint calibration draft workbench', () => {
  test.beforeEach(async ({ page }) => {
    await stubOperatorLibrary(page);
    await stubPreview(page);
  });

  test('supports template editing solve outlier display export and degenerate failure screenshots', async ({ page }) => {
    await installStartupFlag(page, true);
    await bootAuthenticatedApp(page);
    await setCurrentProject(page);

    let solveCallCount = 0;
    await page.route('**/api/calibration/npoint-draft/solve', async route => {
      solveCallCount += 1;
      const request = route.request().postDataJSON();
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(solveCallCount === 1 ? createSolvedResponse(request) : createFailedResponse(request)),
      });
    });

    await addAndSelectNPointNode(page);
    await waitForWorkbenchReady(page);

    await page.locator('[data-action="template9"]').click();
    await fillWorldCoordinates(page);

    const rows = page.locator('.calibration-draft-table tbody tr');
    await rows.nth(8).locator('input[type="checkbox"]').uncheck();
    await page.locator('[data-action="solve"]').click();

    await expect(page.locator('.calibration-draft-status')).toContainText('Solved');
    await expect(page.locator('.calibration-draft-status')).toContainText('true');
    await expect(rows.nth(7)).toContainText('no');
    await expect(rows.nth(8).locator('input[type="checkbox"]')).not.toBeChecked();
    await saveScreenshot(page, 'g12b-npoint-outlier-disabled.png');

    await expect(page.locator('[data-action="export"]')).toBeEnabled();
    await saveScreenshot(page, 'g12b-npoint-candidate-export-ready.png');
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.locator('[data-action="export"]').click(),
    ]);
    const downloadedPath = await download.path();
    expect(downloadedPath).toBeTruthy();
    const exportPayload = JSON.parse(await readFile(downloadedPath!, 'utf8'));
    expect(exportPayload.draftNotice).toContain('Draft candidate only');
    expect(exportPayload.candidateBundle.accepted).toBe(true);

    await saveScreenshot(page, 'g12b-npoint-happy-path.png');
    for (let index = 0; index < 9; index += 1) {
      const inputs = rows.nth(index).locator('input.calibration-draft-cell-input');
      await inputs.nth(2).fill('0');
      await inputs.nth(2).dispatchEvent('change');
      await inputs.nth(3).fill('0');
      await inputs.nth(3).dispatchEvent('change');
    }

    await page.locator('[data-action="solve"]').click();
    await expect(page.locator('.calibration-draft-status')).toContainText('Failed');
    await saveScreenshot(page, 'g12b-npoint-degenerate-failure.png');
  });

  test('falls back to generic parameters when the startup flag is off', async ({ page }) => {
    await installStartupFlag(page, false);
    await bootAuthenticatedApp(page);
    await setCurrentProject(page);

    await addAndSelectNPointNode(page);

    await expect(page.locator('[data-testid="npoint-calibration-workbench"]')).toHaveCount(0);
    await expect(page.locator('.property-form')).toBeVisible();
    await expect(page.locator('#param-PointPairs')).toBeVisible();
  });
});
