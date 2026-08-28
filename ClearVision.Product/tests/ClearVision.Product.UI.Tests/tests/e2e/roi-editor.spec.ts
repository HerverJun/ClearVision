import { test, expect, Page } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const PREVIEW_PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAACNSURBVHhe7dAxAQAwDITAyn73qQEcwHALI2/bmTWAokkDKJo0gKJJAyiaNICiSQMomjSAokkDKJo0gKJJAyiaNICiSQMomjSAokkDKJo0gKJJAyiaNICiSQMomjSAokkDKJo0gKJJAyiaNICiSQMomjSAokkDKJo0gKJJAyiaNICiSQMomjSAool8wO4D9cdyOzoyljkAAAAASUVORK5CYII=';
const SECOND_PREVIEW_PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAYUlEQVR42u3QAQ0AAAwCIPuX1hzfGQ1I+pwAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBBw3wBSmOHSCAaCYwAAAABJRU5ErkJggg==';

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

async function setCurrentProject(page: Page) {
  await page.evaluate(async () => {
    const projectModule = await import('/src/features/project/projectManager.js');
    const inspectionModule = await import('/src/features/inspection/inspectionController.js');
    projectModule.setCurrentProject({
      id: 'e2e-roi-project',
      name: 'E2E ROI Project',
      description: '',
      flow: null,
    });
    inspectionModule.default.setProject('e2e-roi-project');
  });
}

async function installCircleSearchV2StartupFlag(page: Page, enabled: boolean) {
  await page.addInitScript((flagEnabled) => {
    const startup = {
      featureFlags: Object.freeze({
        'Studio:CircleSearchV2ToolEnabled': flagEnabled,
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

function createRoiParameters(overrides: Record<string, unknown> = {}) {
  const values = {
    Shape: 'Rectangle',
    Operation: 'Crop',
    X: 8,
    Y: 10,
    Width: 20,
    Height: 18,
    CenterX: 12,
    CenterY: 12,
    Radius: 10,
    PolygonPoints: '[[10,10],[20,10],[20,20],[10,20]]',
    ...overrides,
  };

  return [
    {
      name: 'Shape',
      displayName: '形状',
      dataType: 'enum',
      value: values.Shape,
      defaultValue: 'Rectangle',
      options: ['Rectangle', 'Circle', 'Polygon'],
    },
    {
      name: 'Operation',
      displayName: '操作',
      dataType: 'enum',
      value: values.Operation,
      defaultValue: 'Crop',
      options: ['Crop', 'Mask'],
    },
    { name: 'X', displayName: 'X', dataType: 'int', value: values.X, defaultValue: values.X, min: 0, max: 64 },
    { name: 'Y', displayName: 'Y', dataType: 'int', value: values.Y, defaultValue: values.Y, min: 0, max: 64 },
    { name: 'Width', displayName: '宽度', dataType: 'int', value: values.Width, defaultValue: values.Width, min: 1, max: 64 },
    { name: 'Height', displayName: '高度', dataType: 'int', value: values.Height, defaultValue: values.Height, min: 1, max: 64 },
    { name: 'CenterX', displayName: '圆心X', dataType: 'int', value: values.CenterX, defaultValue: values.CenterX },
    { name: 'CenterY', displayName: '圆心Y', dataType: 'int', value: values.CenterY, defaultValue: values.CenterY },
    { name: 'Radius', displayName: '半径', dataType: 'int', value: values.Radius, defaultValue: values.Radius, min: 1, max: 64 },
    { name: 'PolygonPoints', displayName: '多边形顶点(JSON)', dataType: 'string', value: values.PolygonPoints, defaultValue: values.PolygonPoints },
  ];
}

async function addAndSelectRoiNode(page: Page, overrides: Record<string, unknown> = {}) {
  return page.evaluate((parameters) => {
    const flowCanvas = (window as any).flowCanvas;
    const node = flowCanvas.addNode(
      'RoiManager',
      180,
      140,
      {
        title: '固定ROI',
        parameters,
        inputs: [{ name: 'Image', type: 'Image' }],
        outputs: [{ name: 'Image', type: 'Image' }, { name: 'Mask', type: 'Image' }],
        color: '#1890ff',
      }
    );

    flowCanvas.selectedNode = node.id;
    flowCanvas.onNodeSelected?.(node);
    (window as any).__e2eRoiNodeId = node.id;
    return node.id;
  }, createRoiParameters(overrides));
}

function createCircleMeasurementParameters(overrides: Record<string, unknown> = {}) {
  const values = {
    Method: 'CaliperFitV2',
    SearchCenterMode: 'ImageCenter',
    SearchCenterX: 10,
    SearchCenterY: 12,
    MinRadius: 12,
    NominalRadius: 18,
    MaxRadius: 24,
    Dp: 1,
    MinDist: 50,
    Param1: 100,
    Param2: 30,
    CaliperCount: 96,
    AveragingThickness: 5,
    ProfileSampleCount: 129,
    GaussianSigma: 1.2,
    EdgePolarity: 'Auto',
    EdgeThreshold: 0,
    MinEdgeStrength: 4,
    OutlierMode: 'Mad',
    OutlierThreshold: 3.5,
    MaxOutlierIterations: 3,
    MinValidCalipers: 24,
    MinCoverageRatio: 0.35,
    MinAngularCoverageDegrees: 180,
    MaxResidualRmse: 2,
    ...overrides,
  };

  return [
    { name: 'Method', displayName: 'Method', dataType: 'enum', value: values.Method, defaultValue: 'HoughCircle', options: ['HoughCircle', 'FitEllipse', 'CaliperFitV2'] },
    { name: 'Dp', displayName: 'Dp', dataType: 'double', value: values.Dp, defaultValue: values.Dp },
    { name: 'MinDist', displayName: 'MinDist', dataType: 'double', value: values.MinDist, defaultValue: values.MinDist },
    { name: 'Param1', displayName: 'Param1', dataType: 'double', value: values.Param1, defaultValue: values.Param1 },
    { name: 'Param2', displayName: 'Param2', dataType: 'double', value: values.Param2, defaultValue: values.Param2 },
    { name: 'SearchCenterMode', displayName: 'SearchCenterMode', dataType: 'enum', value: values.SearchCenterMode, defaultValue: 'ImageCenter', options: ['ImageCenter', 'Explicit'] },
    { name: 'SearchCenterX', displayName: 'SearchCenterX', dataType: 'double', value: values.SearchCenterX, defaultValue: values.SearchCenterX, min: 0, max: 64 },
    { name: 'SearchCenterY', displayName: 'SearchCenterY', dataType: 'double', value: values.SearchCenterY, defaultValue: values.SearchCenterY, min: 0, max: 64 },
    { name: 'MinRadius', displayName: 'MinRadius', dataType: 'double', value: values.MinRadius, defaultValue: values.MinRadius, min: 1, max: 64 },
    { name: 'NominalRadius', displayName: 'NominalRadius', dataType: 'double', value: values.NominalRadius, defaultValue: values.NominalRadius, min: 1, max: 64 },
    { name: 'MaxRadius', displayName: 'MaxRadius', dataType: 'double', value: values.MaxRadius, defaultValue: values.MaxRadius, min: 1, max: 64 },
    { name: 'CaliperCount', displayName: 'CaliperCount', dataType: 'int', value: values.CaliperCount, defaultValue: values.CaliperCount, min: 1, max: 720 },
    { name: 'AveragingThickness', displayName: 'AveragingThickness', dataType: 'double', value: values.AveragingThickness, defaultValue: values.AveragingThickness },
    { name: 'ProfileSampleCount', displayName: 'ProfileSampleCount', dataType: 'int', value: values.ProfileSampleCount, defaultValue: values.ProfileSampleCount },
    { name: 'GaussianSigma', displayName: 'GaussianSigma', dataType: 'double', value: values.GaussianSigma, defaultValue: values.GaussianSigma },
    { name: 'EdgePolarity', displayName: 'EdgePolarity', dataType: 'enum', value: values.EdgePolarity, defaultValue: 'Auto', options: ['Auto', 'DarkToLight', 'LightToDark'] },
    { name: 'EdgeThreshold', displayName: 'EdgeThreshold', dataType: 'double', value: values.EdgeThreshold, defaultValue: values.EdgeThreshold },
    { name: 'MinEdgeStrength', displayName: 'MinEdgeStrength', dataType: 'double', value: values.MinEdgeStrength, defaultValue: values.MinEdgeStrength },
    { name: 'OutlierMode', displayName: 'OutlierMode', dataType: 'enum', value: values.OutlierMode, defaultValue: 'Mad', options: ['None', 'Mad', 'Huber'] },
    { name: 'OutlierThreshold', displayName: 'OutlierThreshold', dataType: 'double', value: values.OutlierThreshold, defaultValue: values.OutlierThreshold },
    { name: 'MaxOutlierIterations', displayName: 'MaxOutlierIterations', dataType: 'int', value: values.MaxOutlierIterations, defaultValue: values.MaxOutlierIterations },
    { name: 'MinValidCalipers', displayName: 'MinValidCalipers', dataType: 'int', value: values.MinValidCalipers, defaultValue: values.MinValidCalipers },
    { name: 'MinCoverageRatio', displayName: 'MinCoverageRatio', dataType: 'double', value: values.MinCoverageRatio, defaultValue: values.MinCoverageRatio },
    { name: 'MinAngularCoverageDegrees', displayName: 'MinAngularCoverageDegrees', dataType: 'double', value: values.MinAngularCoverageDegrees, defaultValue: values.MinAngularCoverageDegrees },
    { name: 'MaxResidualRmse', displayName: 'MaxResidualRmse', dataType: 'double', value: values.MaxResidualRmse, defaultValue: values.MaxResidualRmse },
  ];
}

async function addAndSelectCircleMeasurementNode(page: Page, overrides: Record<string, unknown> = {}) {
  return page.evaluate((parameters) => {
    const flowCanvas = (window as any).flowCanvas;
    const node = flowCanvas.addNode(
      'CircleMeasurement',
      180,
      140,
      {
        title: 'Circle Search',
        parameters,
        inputs: [{ name: 'Image', type: 'Image' }],
        outputs: [{ name: 'Circle', type: 'Circle' }, { name: 'EdgePoints', type: 'PointList' }],
        color: '#0ea5e9',
      }
    );

    flowCanvas.selectedNode = node.id;
    flowCanvas.onNodeSelected?.(node);
    (window as any).__e2eRoiNodeId = node.id;
    return node.id;
  }, createCircleMeasurementParameters(overrides));
}

function createPolarParameters(overrides: Record<string, unknown> = {}) {
  const values = {
    CenterX: 32,
    CenterY: 32,
    InnerRadius: 8,
    OuterRadius: 20,
    StartAngle: 0,
    EndAngle: 180,
    OutputWidth: 0,
    UseWarpPolar: true,
    ...overrides,
  };

  return [
    { name: 'CenterX', displayName: 'Center X', dataType: 'int', value: values.CenterX, defaultValue: values.CenterX },
    { name: 'CenterY', displayName: 'Center Y', dataType: 'int', value: values.CenterY, defaultValue: values.CenterY },
    { name: 'InnerRadius', displayName: 'Inner Radius', dataType: 'int', value: values.InnerRadius, defaultValue: values.InnerRadius, min: 0 },
    { name: 'OuterRadius', displayName: 'Outer Radius', dataType: 'int', value: values.OuterRadius, defaultValue: values.OuterRadius, min: 1 },
    { name: 'StartAngle', displayName: 'Start Angle', dataType: 'double', value: values.StartAngle, defaultValue: values.StartAngle },
    { name: 'EndAngle', displayName: 'End Angle', dataType: 'double', value: values.EndAngle, defaultValue: values.EndAngle },
    { name: 'OutputWidth', displayName: 'Output Width', dataType: 'int', value: values.OutputWidth, defaultValue: values.OutputWidth },
    { name: 'UseWarpPolar', displayName: 'Use WarpPolar', dataType: 'bool', value: values.UseWarpPolar, defaultValue: values.UseWarpPolar },
  ];
}

async function addAndSelectPolarNode(page: Page, overrides: Record<string, unknown> = {}) {
  return page.evaluate((parameters) => {
    const flowCanvas = (window as any).flowCanvas;
    const node = flowCanvas.addNode(
      'PolarUnwrap',
      180,
      140,
      {
        title: '极坐标展开',
        parameters,
        inputs: [{ name: 'Image', type: 'Image' }],
        outputs: [{ name: 'Image', type: 'Image' }],
        color: '#1890ff',
      }
    );

    flowCanvas.selectedNode = node.id;
    flowCanvas.onNodeSelected?.(node);
    (window as any).__e2eRoiNodeId = node.id;
    return node.id;
  }, createPolarParameters(overrides));
}

function createNPointParameters(overrides: Record<string, unknown> = {}) {
  const values = {
    CalibrationMode: 'Affine',
    PointPairs: JSON.stringify([
      { ImageX: 10, ImageY: 20, WorldX: 1, WorldY: 2 },
      { ImageX: 30, ImageY: 20, WorldX: 3, WorldY: 2 },
      { ImageX: 10, ImageY: 40, WorldX: 1, WorldY: 4 },
    ]),
    ...overrides,
  };

  return [
    {
      name: 'CalibrationMode',
      displayName: 'Calibration Mode',
      dataType: 'enum',
      value: values.CalibrationMode,
      defaultValue: 'Affine',
      options: ['Affine', 'Perspective'],
    },
    { name: 'PointPairs', displayName: 'Point Pairs', dataType: 'string', value: values.PointPairs, defaultValue: values.PointPairs },
  ];
}

async function addAndSelectNPointNode(page: Page, overrides: Record<string, unknown> = {}) {
  return page.evaluate((parameters) => {
    const flowCanvas = (window as any).flowCanvas;
    const node = flowCanvas.addNode(
      'NPointCalibration',
      180,
      140,
      {
        title: 'NPoint',
        parameters,
        inputs: [{ name: 'Image', type: 'Image' }],
        outputs: [{ name: 'CalibrationData', type: 'String' }],
        color: '#1890ff',
      }
    );

    flowCanvas.selectedNode = node.id;
    flowCanvas.onNodeSelected?.(node);
    (window as any).__e2eRoiNodeId = node.id;
    return node.id;
  }, createNPointParameters(overrides));
}

async function addAndSelectPlainImageNode(page: Page) {
  return page.evaluate(() => {
    const flowCanvas = (window as any).flowCanvas;
    const node = flowCanvas.addNode(
      'Thresholding',
      420,
      180,
      {
        title: 'Threshold',
        parameters: [],
        inputs: [{ name: 'Image', type: 'Image' }],
        outputs: [{ name: 'Image', type: 'Image' }],
        color: '#64748b',
      }
    );

    flowCanvas.selectedNode = node.id;
    flowCanvas.onNodeSelected?.(node);
    (window as any).__e2ePlainNodeId = node.id;
    return node.id;
  });
}

async function waitForRoiEditorReady(page: Page) {
  await expect(page.locator('.roi-editor-panel')).toBeVisible();
  await page.waitForFunction(() => {
    const panel = (window as any).propertyPanel?.roiEditorPanel;
    return Boolean(panel?.currentImageSource && panel?.imageCanvas?.image);
  });
}

async function getRoiState(page: Page) {
  return page.evaluate(() => {
    const panel = (window as any).propertyPanel;
    const overlay = panel?.roiEditorPanel?.imageCanvas?.getPrimaryEditableOverlay?.();
    const readValue = (name: string) => {
      const input = document.querySelector<HTMLInputElement>(`#param-${name}`);
      return input ? Number.parseInt(input.value, 10) : null;
    };

    return {
      params: {
        x: readValue('X'),
        y: readValue('Y'),
        width: readValue('Width'),
        height: readValue('Height'),
      },
      overlay: overlay
        ? {
            x: Math.round(overlay.x),
            y: Math.round(overlay.y),
            width: Math.round(overlay.width),
            height: Math.round(overlay.height),
          }
        : null,
    };
  });
}

async function dispatchRoiDrag(
  page: Page,
  from: { x: number; y: number },
  to: { x: number; y: number },
  button: 'left' | 'right' = 'left',
  options: { altKey?: boolean } = {}
) {
  await page.evaluate(({ startPoint, endPoint, mouseButton, eventOptions }) => {
    const panel = (window as any).propertyPanel?.roiEditorPanel;
    const canvas = document.querySelector<HTMLCanvasElement>('.roi-editor-canvas');
    if (!panel?.imageCanvas || !canvas) {
      throw new Error('ROI editor canvas not ready');
    }

    const toClient = (point: { x: number; y: number }) => ({
      clientX: canvas.getBoundingClientRect().left + panel.imageCanvas.offset.x + point.x * panel.imageCanvas.scale,
      clientY: canvas.getBoundingClientRect().top + panel.imageCanvas.offset.y + point.y * panel.imageCanvas.scale
    });

    const buttonValue = mouseButton === 'right' ? 2 : 0;
    const buttonsValue = mouseButton === 'right' ? 2 : 1;
    const start = toClient(startPoint);
    const end = toClient(endPoint);

    canvas.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true,
      button: buttonValue,
      buttons: buttonsValue,
      altKey: eventOptions.altKey === true,
      ...start
    }));

    for (let step = 1; step <= 6; step += 1) {
      const progress = step / 6;
      const intermediate = {
        clientX: start.clientX + (end.clientX - start.clientX) * progress,
        clientY: start.clientY + (end.clientY - start.clientY) * progress
      };
      canvas.dispatchEvent(new MouseEvent('mousemove', {
        bubbles: true,
        button: buttonValue,
        buttons: buttonsValue,
        altKey: eventOptions.altKey === true,
        ...intermediate
      }));
    }

    canvas.dispatchEvent(new MouseEvent('mouseup', {
      bubbles: true,
      button: buttonValue,
      buttons: 0,
      altKey: eventOptions.altKey === true,
      ...end
    }));
  }, {
    startPoint: from,
    endPoint: to,
    mouseButton: button,
    eventOptions: options
  });
}

async function getCircleState(page: Page) {
  return page.evaluate(() => {
    const panel = (window as any).propertyPanel;
    const overlay = panel?.roiEditorPanel?.imageCanvas?.getPrimaryEditableOverlay?.();
    const readValue = (name: string) => {
      const input = document.querySelector<HTMLInputElement>(`#param-${name}`);
      return input ? Number.parseInt(input.value, 10) : null;
    };

    return {
      params: {
        centerX: readValue('CenterX'),
        centerY: readValue('CenterY'),
        radius: readValue('Radius'),
      },
      overlay: overlay
        ? {
            type: overlay.type,
            centerX: Math.round(overlay.centerX ?? overlay.x),
            centerY: Math.round(overlay.centerY ?? overlay.y),
            radius: Math.round(overlay.radius),
          }
        : null,
    };
  });
}

async function getCircleSearchV2State(page: Page) {
  return page.evaluate(() => {
    const node = (window as any).flowCanvas.nodes.get((window as any).__e2eRoiNodeId);
    const overlay = (window as any).propertyPanel?.roiEditorPanel?.imageCanvas?.getPrimaryEditableOverlay?.();
    const round = (value: unknown) => {
      const numberValue = Number(value);
      return Number.isFinite(numberValue)
        ? Math.round(numberValue * 1000) / 1000
        : null;
    };
    const readInputValue = (name: string) =>
      document.querySelector<HTMLInputElement | HTMLSelectElement>(`#param-${name}`)?.value ?? null;
    const readInputNumber = (name: string) => round(readInputValue(name));
    const readNodeValue = (name: string) =>
      node?.parameters?.find((param: any) => param.name === name)?.value ?? null;
    const readNodeNumber = (name: string) => round(readNodeValue(name));

    return {
      changeCount: (window as any).__roiParameterChangeCount ?? 0,
      params: {
        searchCenterMode: readInputValue('SearchCenterMode'),
        searchCenterX: readInputNumber('SearchCenterX'),
        searchCenterY: readInputNumber('SearchCenterY'),
        minRadius: readInputNumber('MinRadius'),
        nominalRadius: readInputNumber('NominalRadius'),
        maxRadius: readInputNumber('MaxRadius'),
      },
      nodeParams: {
        searchCenterMode: readNodeValue('SearchCenterMode'),
        searchCenterX: readNodeNumber('SearchCenterX'),
        searchCenterY: readNodeNumber('SearchCenterY'),
        minRadius: readNodeNumber('MinRadius'),
        nominalRadius: readNodeNumber('NominalRadius'),
        maxRadius: readNodeNumber('MaxRadius'),
      },
      overlay: overlay
        ? {
            type: overlay.type,
            centerX: round(overlay.centerX ?? overlay.x),
            centerY: round(overlay.centerY ?? overlay.y),
            minRadius: round(overlay.minRadius ?? overlay.innerRadius),
            nominalRadius: round(overlay.nominalRadius),
            maxRadius: round(overlay.maxRadius ?? overlay.outerRadius ?? overlay.radius),
          }
        : null,
      readonly: {
        searchCenterX: document.querySelector<HTMLInputElement>('#param-SearchCenterX')?.readOnly ?? false,
        searchCenterY: document.querySelector<HTMLInputElement>('#param-SearchCenterY')?.readOnly ?? false,
      },
      roiEditorExists: Boolean((window as any).propertyPanel?.roiEditorPanel),
    };
  });
}

async function getPolarState(page: Page) {
  return page.evaluate(() => {
    const panel = (window as any).propertyPanel;
    const overlay = panel?.roiEditorPanel?.imageCanvas?.getPrimaryEditableOverlay?.();
    const readNumber = (name: string) => {
      const input = document.querySelector<HTMLInputElement>(`#param-${name}`);
      return input ? Number(input.value) : null;
    };

    return {
      params: {
        centerX: readNumber('CenterX'),
        centerY: readNumber('CenterY'),
        innerRadius: readNumber('InnerRadius'),
        outerRadius: readNumber('OuterRadius'),
        startAngle: readNumber('StartAngle'),
        endAngle: readNumber('EndAngle'),
      },
      overlay: overlay
        ? {
            type: overlay.type,
            centerX: Math.round(overlay.centerX ?? overlay.x),
            centerY: Math.round(overlay.centerY ?? overlay.y),
            innerRadius: Math.round(overlay.innerRadius),
            outerRadius: Math.round(overlay.outerRadius ?? overlay.radius),
            startAngle: Math.round(overlay.startAngle),
            spanDegrees: Math.round(overlay.spanDegrees),
          }
        : null,
    };
  });
}

async function getPolygonState(page: Page) {
  return page.evaluate(() => {
    const panel = (window as any).propertyPanel;
    const overlay = panel?.roiEditorPanel?.imageCanvas?.getPrimaryEditableOverlay?.();
    const input = document.querySelector<HTMLInputElement>('#param-PolygonPoints');
    const params = input ? JSON.parse(input.value) : null;

    return {
      params,
      overlay: overlay
        ? {
            type: overlay.type,
            points: (overlay.points || []).map((point: any) => ({
              x: Math.round(point.x),
              y: Math.round(point.y),
            })),
            selectedPointIndex: overlay.selectedPointIndex ?? null,
          }
        : null,
    };
  });
}

async function getPointSequenceState(page: Page) {
  return page.evaluate(() => {
    const panel = (window as any).propertyPanel;
    const overlay = panel?.roiEditorPanel?.imageCanvas?.getPrimaryEditableOverlay?.();
    const input = document.querySelector<HTMLInputElement>('#param-PointPairs');
    const params = input ? JSON.parse(input.value) : null;

    return {
      params,
      overlay: overlay
        ? {
            type: overlay.type,
            points: (overlay.points || []).map((point: any) => ({
              x: Math.round(point.x),
              y: Math.round(point.y),
              enabled: point.enabled !== false,
            })),
            selectedPointIndex: overlay.selectedPointIndex ?? null,
          }
        : null,
    };
  });
}

async function waitForCalibrationDraftWorkbenchReady(page: Page) {
  await expect(page.locator('[data-testid="npoint-calibration-workbench"]')).toBeVisible();
  await expect(page.locator('.roi-editor-panel')).toHaveCount(0);
  await page.waitForFunction(() => {
    const workbench = (window as any).propertyPanel?.calibrationDraftWorkbench;
    return Boolean(workbench?.currentImageSource && workbench?.imageCanvas?.image && workbench?.session?.samples?.length);
  });
}

async function getCalibrationDraftState(page: Page) {
  return page.evaluate(() => {
    const workbench = (window as any).propertyPanel?.calibrationDraftWorkbench;
    const overlay = workbench?.imageCanvas?.getPrimaryEditableOverlay?.();

    return {
      roiEditorMounted: Boolean((window as any).propertyPanel?.roiEditorPanel),
      samples: (workbench?.session?.samples || []).map((sample: any) => ({
        pixelX: Math.round(Number(sample.pixelX)),
        pixelY: Math.round(Number(sample.pixelY)),
        worldX: sample.worldX,
        worldY: sample.worldY,
        enabled: sample.enabled !== false,
      })),
      overlay: overlay
        ? {
            type: overlay.type,
            points: (overlay.points || []).map((point: any) => ({
              x: Math.round(point.x),
              y: Math.round(point.y),
              enabled: point.enabled !== false,
            })),
          }
        : null,
    };
  });
}

async function setCalibrationDraftNumberCell(page: Page, rowIndex: number, numberInputIndex: number, value: number) {
  await page.evaluate(({ rowIndex: targetRow, numberInputIndex: targetInput, value: nextValue }) => {
    const rows = Array.from(document.querySelectorAll<HTMLTableRowElement>('.calibration-draft-table tbody tr'));
    const input = rows[targetRow]?.querySelectorAll<HTMLInputElement>('input[type="number"]')?.[targetInput];
    if (!input) {
      throw new Error(`Calibration draft input not found: row=${targetRow}, input=${targetInput}`);
    }

    input.value = String(nextValue);
    input.dispatchEvent(new Event('change', { bubbles: true }));
  }, { rowIndex, numberInputIndex, value });
}

async function dispatchRoiDragWithoutMouseUp(
  page: Page,
  from: { x: number; y: number },
  to: { x: number; y: number }
) {
  await page.evaluate(({ startPoint, endPoint }) => {
    const panel = (window as any).propertyPanel?.roiEditorPanel;
    const canvas = document.querySelector<HTMLCanvasElement>('.roi-editor-canvas');
    if (!panel?.imageCanvas || !canvas) {
      throw new Error('ROI editor canvas not ready');
    }

    const rect = canvas.getBoundingClientRect();
    const toClient = (point: { x: number; y: number }) => ({
      clientX: rect.left + panel.imageCanvas.offset.x + point.x * panel.imageCanvas.scale,
      clientY: rect.top + panel.imageCanvas.offset.y + point.y * panel.imageCanvas.scale
    });
    const start = toClient(startPoint);
    const end = toClient(endPoint);

    canvas.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true,
      button: 0,
      buttons: 1,
      ...start
    }));

    for (let step = 1; step <= 6; step += 1) {
      const progress = step / 6;
      canvas.dispatchEvent(new MouseEvent('mousemove', {
        bubbles: true,
        button: 0,
        buttons: 1,
        clientX: start.clientX + (end.clientX - start.clientX) * progress,
        clientY: start.clientY + (end.clientY - start.clientY) * progress
      }));
    }
  }, {
    startPoint: from,
    endPoint: to
  });
}

async function dispatchRoiMouseUp(page: Page, point: { x: number; y: number }) {
  await page.evaluate((targetPoint) => {
    const panel = (window as any).propertyPanel?.roiEditorPanel;
    const canvas = document.querySelector<HTMLCanvasElement>('.roi-editor-canvas');
    if (!panel?.imageCanvas || !canvas) {
      throw new Error('ROI editor canvas not ready');
    }

    const rect = canvas.getBoundingClientRect();
    canvas.dispatchEvent(new MouseEvent('mouseup', {
      bubbles: true,
      button: 0,
      buttons: 0,
      clientX: rect.left + panel.imageCanvas.offset.x + targetPoint.x * panel.imageCanvas.scale,
      clientY: rect.top + panel.imageCanvas.offset.y + targetPoint.y * panel.imageCanvas.scale
    }));
  }, point);
}

async function getCanvasPointForImagePoint(page: Page, point: { x: number; y: number }) {
  return page.evaluate((targetPoint) => {
    const panel = (window as any).propertyPanel?.roiEditorPanel;
    const canvas = document.querySelector<HTMLCanvasElement>('.roi-editor-canvas');
    if (!panel?.imageCanvas || !canvas) {
      throw new Error('ROI editor canvas not ready');
    }

    const rect = canvas.getBoundingClientRect();
    return {
      x: rect.left + panel.imageCanvas.offset.x + targetPoint.x * panel.imageCanvas.scale,
      y: rect.top + panel.imageCanvas.offset.y + targetPoint.y * panel.imageCanvas.scale,
    };
  }, point);
}

async function installRoiParameterChangeCounter(page: Page) {
  await page.evaluate(() => {
    const canvas = (window as any).flowCanvas;
    const original = canvas.markFlowStructureChanged?.bind(canvas);
    (window as any).__roiParameterChangeCount = 0;
    canvas.markFlowStructureChanged = (reason: string) => {
      if (reason === 'parameter-change') {
        (window as any).__roiParameterChangeCount += 1;
      }
      return original?.(reason);
    };
  });
}

async function installRoiLifecycleProbe(page: Page) {
  await installRoiParameterChangeCounter(page);
  await page.evaluate(() => {
    (window as any).__roiUnhandledRejections = [];
    if (!(window as any).__roiUnhandledRejectionProbeInstalled) {
      window.addEventListener('unhandledrejection', (event) => {
        const reason = (event as PromiseRejectionEvent).reason;
        (window as any).__roiUnhandledRejections.push(String(reason?.message || reason || 'unknown'));
      });
      (window as any).__roiUnhandledRejectionProbeInstalled = true;
    }
  });
}

async function getRoiCommitProbeState(page: Page) {
  return page.evaluate(() => {
    const node = (window as any).flowCanvas.nodes.get((window as any).__e2eRoiNodeId);
    const imageCanvas = (window as any).propertyPanel?.roiEditorPanel?.imageCanvas ?? null;
    const readNodeParam = (name: string) => Number(node.parameters.find((param: any) => param.name === name)?.value);
    const readInput = (name: string) => Number((document.querySelector<HTMLInputElement>(`#param-${name}`))?.value);
    return {
      changeCount: (window as any).__roiParameterChangeCount,
      nodeX: readNodeParam('X'),
      nodeY: readNodeParam('Y'),
      formX: readInput('X'),
      formY: readInput('Y'),
      activePointerId: imageCanvas?.activePointerId ?? null,
    };
  });
}

async function getRoiLifecycleProbeState(page: Page) {
  return page.evaluate(() => {
    const flowCanvas = (window as any).flowCanvas;
    const roiNode = flowCanvas.nodes.get((window as any).__e2eRoiNodeId);
    const panel = (window as any).propertyPanel?.roiEditorPanel ?? null;
    const imageCanvas = panel?.imageCanvas ?? null;
    const readNodeParam = (name: string) => {
      const raw = roiNode?.parameters?.find((param: any) => param.name === name)?.value;
      return raw === undefined ? null : Number(raw);
    };
    const readInput = (name: string) => {
      const input = document.querySelector<HTMLInputElement>(`#param-${name}`);
      return input ? Number(input.value) : null;
    };

    return {
      changeCount: (window as any).__roiParameterChangeCount ?? 0,
      nodeX: readNodeParam('X'),
      nodeY: readNodeParam('Y'),
      nodeWidth: readNodeParam('Width'),
      nodeHeight: readNodeParam('Height'),
      formX: readInput('X'),
      formY: readInput('Y'),
      activePointerId: imageCanvas?.activePointerId ?? null,
      interactionState: imageCanvas?.interactionState ?? null,
      activeHandle: imageCanvas?.activeHandle ?? null,
      capturedPointerCount: (window as any).__roiCapturedPointers?.size ?? 0,
      roiEditorExists: Boolean(panel),
      currentImageSource: panel?.currentImageSource ?? null,
      imageWidth: imageCanvas?.image?.width ?? null,
      imageHeight: imageCanvas?.image?.height ?? null,
      emptyVisible: (document.querySelector<HTMLElement>('#roi-editor-empty')?.style.display ?? '') !== 'none',
      previewCardCount: document.querySelectorAll('.node-preview-card, .node-preview-inspector-card').length,
      unhandledRejections: (window as any).__roiUnhandledRejections ?? [],
    };
  });
}

async function requestImmediateRoiPreview(page: Page) {
  await page.evaluate(() => {
    (window as any).nodePreviewCoordinator?.requestActivePreview?.({
      immediate: true,
      force: true,
      debounceMs: 0,
      trigger: 'manual',
    });
  });
}

async function waitForRoiImageSource(page: Page, expectedBase64: string | null) {
  await page.waitForFunction((expected) => {
    const source = (window as any).propertyPanel?.roiEditorPanel?.currentImageSource ?? null;
    if (expected === null) {
      return source === null;
    }

    return typeof source === 'string' && source.includes(expected as string);
  }, expectedBase64);
}

function createPreviewResponse(inputImageBase64: string | null, count = 1) {
  return {
    success: true,
    inputImageBase64,
    outputImageBase64: inputImageBase64,
    outputData: { Width: 64, Height: 64, Count: count },
    executionTimeMs: 10,
  };
}

async function dispatchPointerDragByClientPoints(
  page: Page,
  from: { x: number; y: number },
  to: { x: number; y: number },
  options: { release?: boolean; pointerId?: number } = {}
) {
  await page.evaluate(({ start, end, release, pointerId }) => {
    const canvas = document.querySelector<HTMLCanvasElement>('.roi-editor-canvas');
    if (!canvas) {
      throw new Error('ROI editor canvas not ready');
    }

    const target = canvas as HTMLCanvasElement & {
      __e2ePointerCapturePatched?: boolean;
      __e2eCapturedPointers?: Set<number>;
    };
    if (!target.__e2ePointerCapturePatched) {
      const captured = new Set<number>();
      target.__e2eCapturedPointers = captured;
      (window as any).__roiCapturedPointers = captured;
      target.setPointerCapture = (id: number) => {
        captured.add(id);
      };
      target.releasePointerCapture = (id: number) => {
        captured.delete(id);
      };
      target.hasPointerCapture = (id: number) => captured.has(id);
      target.__e2ePointerCapturePatched = true;
    } else {
      (window as any).__roiCapturedPointers = target.__e2eCapturedPointers;
    }

    const dispatch = (type: string, point: { x: number; y: number }, buttons: number) => {
      target.dispatchEvent(new PointerEvent(type, {
        bubbles: true,
        cancelable: true,
        pointerId,
        pointerType: 'mouse',
        isPrimary: true,
        button: 0,
        buttons,
        clientX: point.x,
        clientY: point.y,
      }));
    };

    dispatch('pointerdown', start, 1);
    for (let step = 1; step <= 6; step += 1) {
      const progress = step / 6;
      dispatch('pointermove', {
        x: start.x + (end.x - start.x) * progress,
        y: start.y + (end.y - start.y) * progress,
      }, 1);
    }
    if (release) {
      dispatch('pointerup', end, 0);
    }
  }, {
    start: from,
    end: to,
    release: options.release !== false,
    pointerId: options.pointerId ?? 7,
  });
}

async function dispatchPointerDragWithoutRelease(
  page: Page,
  from: { x: number; y: number },
  to: { x: number; y: number }
) {
  const start = await getCanvasPointForImagePoint(page, from);
  const end = await getCanvasPointForImagePoint(page, to);
  await dispatchPointerDragByClientPoints(page, start, end, { release: false });
}

async function dispatchActivePointerCancel(page: Page) {
  await page.evaluate(() => {
    const panel = (window as any).propertyPanel?.roiEditorPanel;
    const canvas = document.querySelector<HTMLCanvasElement>('.roi-editor-canvas');
    const pointerId = panel?.imageCanvas?.activePointerId;
    if (!panel?.imageCanvas || !canvas || pointerId === null || pointerId === undefined) {
      throw new Error('ROI pointer interaction is not active');
    }

    const rect = canvas.getBoundingClientRect();
    canvas.dispatchEvent(new PointerEvent('pointercancel', {
      bubbles: true,
      cancelable: true,
      pointerId,
      pointerType: 'mouse',
      isPrimary: true,
      button: 0,
      buttons: 0,
      clientX: rect.left + rect.width / 2,
      clientY: rect.top + rect.height / 2,
    }));
  });
}

async function getHandlePoint(page: Page, handle: string) {
  return page.evaluate((handleName) => {
    const panel = (window as any).propertyPanel?.roiEditorPanel;
    const canvas = document.querySelector<HTMLCanvasElement>('.roi-editor-canvas');
    const overlay = panel?.imageCanvas?.getPrimaryEditableOverlay?.();
    if (!panel?.imageCanvas || !canvas || !overlay) {
      throw new Error('ROI overlay not ready');
    }

    const handles = {
      nw: { x: overlay.x, y: overlay.y },
      n: { x: overlay.x + overlay.width / 2, y: overlay.y },
      ne: { x: overlay.x + overlay.width, y: overlay.y },
      e: { x: overlay.x + overlay.width, y: overlay.y + overlay.height / 2 },
      se: { x: overlay.x + overlay.width, y: overlay.y + overlay.height },
      s: { x: overlay.x + overlay.width / 2, y: overlay.y + overlay.height },
      sw: { x: overlay.x, y: overlay.y + overlay.height },
      w: { x: overlay.x, y: overlay.y + overlay.height / 2 },
    } as Record<string, { x: number; y: number }>;

    const point = handles[handleName];
    const rect = canvas.getBoundingClientRect();
    return {
      x: rect.left + panel.imageCanvas.offset.x + point.x * panel.imageCanvas.scale,
      y: rect.top + panel.imageCanvas.offset.y + point.y * panel.imageCanvas.scale,
    };
  }, handle);
}

test.describe('ROI Editor', () => {
  test.beforeEach(async ({ page }) => {
    await stubOperatorLibrary(page);
    await bootAuthenticatedApp(page);
    await setCurrentProject(page);
  });

  test('shows ROI editor for rectangle circle and valid polygon shapes', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64, Shape: 'Rectangle' },
          executionTimeMs: 8,
        }),
      });
    });

    await addAndSelectRoiNode(page);
    await waitForRoiEditorReady(page);

    await expect(page.locator('.roi-editor-panel')).toBeVisible();
    await expect(page.locator('#roi-editor-readonly')).toHaveClass(/hidden/);

    await page.selectOption('#param-Shape', 'Circle');
    await expect(page.locator('#roi-editor-readonly')).toHaveClass(/hidden/);

    await page.selectOption('#param-Shape', 'Polygon');
    await expect(page.locator('#roi-editor-readonly')).toHaveClass(/hidden/);
  });

  test('image editing headers remain readable in the narrow inspector', async ({ page }) => {
    await page.addStyleTag({
      content: `
        .flow-editor-shell { --inspector-pane-width: 240px !important; }
      `,
    });
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page);
    await waitForRoiEditorReady(page);
    await expect(page.locator('.roi-editor-title')).toHaveText('ROI 编辑器');
    await expect(page.locator('#roi-editor-subtitle')).toContainText('拖拽矩形 ROI');
    await expect(page.locator('#roi-editor-subtitle')).not.toContainText('Drag the rectangle');
    const roiHeaderMetrics = await page.locator('.roi-editor-title').evaluate(element => {
      const style = getComputedStyle(element);
      const fontSize = Number.parseFloat(style.fontSize) || 13;
      return {
        height: element.getBoundingClientRect().height,
        lineHeight: Number.parseFloat(style.lineHeight) || fontSize * 1.2,
      };
    });
    expect(roiHeaderMetrics.height).toBeLessThanOrEqual(roiHeaderMetrics.lineHeight * 1.35);

    await addAndSelectNPointNode(page);
    await waitForCalibrationDraftWorkbenchReady(page);
    const npointHeaderMetrics = await page.locator('.calibration-draft-title').evaluate(element => {
      const style = getComputedStyle(element);
      const fontSize = Number.parseFloat(style.fontSize) || 13;
      return {
        height: element.getBoundingClientRect().height,
        lineHeight: Number.parseFloat(style.lineHeight) || fontSize * 1.2,
      };
    });
    expect(npointHeaderMetrics.height).toBeLessThanOrEqual(npointHeaderMetrics.lineHeight * 1.35);
  });

  test('drawing a new rectangle updates XYWH and triggers one extra preview', async ({ page }) => {
    let previewCallCount = 0;
    await page.route('**/api/flows/preview-node', async route => {
      previewCallCount += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64, Count: previewCallCount },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, { X: 4, Y: 4, Width: 8, Height: 8 });
    await waitForRoiEditorReady(page);
    await page.waitForTimeout(700);
    expect(previewCallCount).toBe(1);

    await dispatchRoiDrag(page, { x: 20, y: 18 }, { x: 42, y: 40 });

    await page.waitForTimeout(500);
    expect(previewCallCount).toBe(2);

    const roiState = await getRoiState(page);
    expect(roiState.params.x).toBe(20);
    expect(roiState.params.y).toBe(18);
    expect(roiState.params.width).toBe(22);
    expect(roiState.params.height).toBe(22);
    expect(roiState.overlay).toEqual({ x: 20, y: 18, width: 22, height: 22 });
  });

  test('dragging and resizing the ROI updates parameters', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);

    await dispatchRoiDrag(page, { x: 16, y: 18 }, { x: 24, y: 26 });

    let roiState = await getRoiState(page);
    expect(roiState.params.x).toBe(18);
    expect(roiState.params.y).toBe(20);

    const resizeFrom = await getHandlePoint(page, 'se');
    await page.evaluate((start) => {
      const panel = (window as any).propertyPanel?.roiEditorPanel;
      const canvas = document.querySelector<HTMLCanvasElement>('.roi-editor-canvas');
      if (!panel?.imageCanvas || !canvas) {
        throw new Error('ROI editor canvas not ready');
      }

      const rect = canvas.getBoundingClientRect();
      const startPoint = {
        x: (start.x - rect.left - panel.imageCanvas.offset.x) / panel.imageCanvas.scale,
        y: (start.y - rect.top - panel.imageCanvas.offset.y) / panel.imageCanvas.scale
      };
      const endPoint = { x: 42, y: 46 };
      const toClient = (point: { x: number; y: number }) => ({
        clientX: rect.left + panel.imageCanvas.offset.x + point.x * panel.imageCanvas.scale,
        clientY: rect.top + panel.imageCanvas.offset.y + point.y * panel.imageCanvas.scale
      });

      const startClient = toClient(startPoint);
      const endClient = toClient(endPoint);
      canvas.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0, buttons: 1, ...startClient }));
      for (let step = 1; step <= 6; step += 1) {
        const progress = step / 6;
        const intermediate = {
          clientX: startClient.clientX + (endClient.clientX - startClient.clientX) * progress,
          clientY: startClient.clientY + (endClient.clientY - startClient.clientY) * progress
        };
        canvas.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, button: 0, buttons: 1, ...intermediate }));
      }
      canvas.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0, buttons: 0, ...endClient }));
    }, resizeFrom);

    roiState = await getRoiState(page);
    expect(roiState.params.width).toBe(24);
    expect(roiState.params.height).toBe(26);
    expect(roiState.overlay).toEqual({ x: 18, y: 20, width: 24, height: 26 });
  });

  test('circle ROI move and radius handle update CenterX CenterY and Radius', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, { Shape: 'Circle', CenterX: 24, CenterY: 24, Radius: 8 });
    await waitForRoiEditorReady(page);

    let circleState = await getCircleState(page);
    expect(circleState.overlay).toEqual({ type: 'circle', centerX: 24, centerY: 24, radius: 8 });

    await dispatchRoiDrag(page, { x: 24, y: 24 }, { x: 30, y: 32 });
    circleState = await getCircleState(page);
    expect(circleState.params).toEqual({ centerX: 30, centerY: 32, radius: 8 });
    expect(circleState.overlay).toEqual({ type: 'circle', centerX: 30, centerY: 32, radius: 8 });

    await dispatchRoiDrag(page, { x: 38, y: 32 }, { x: 50, y: 32 });
    circleState = await getCircleState(page);
    expect(circleState.params).toEqual({ centerX: 30, centerY: 32, radius: 20 });
    expect(circleState.overlay).toEqual({ type: 'circle', centerX: 30, centerY: 32, radius: 20 });
  });

  test('circle ROI blank drag creates a circle from the selection box', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, { Shape: 'Circle', CenterX: 8, CenterY: 8, Radius: 4 });
    await waitForRoiEditorReady(page);

    await dispatchRoiDrag(page, { x: 20, y: 18 }, { x: 42, y: 40 });
    const circleState = await getCircleState(page);

    expect(circleState.params).toEqual({ centerX: 31, centerY: 29, radius: 11 });
    expect(circleState.overlay).toEqual({ type: 'circle', centerX: 31, centerY: 29, radius: 11 });
  });

  test('CircleMeasurement CaliperFitV2 groups parameters and mounts circle search geometry', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64, StatusCode: 'OK' },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectCircleMeasurementNode(page);
    await waitForRoiEditorReady(page);

    const groupTitles = await page.locator('.param-group .group-title').allTextContents();
    expect(groupTitles).toEqual([
      '\u68c0\u6d4b\u65b9\u6cd5',
      '\u641c\u7d22\u51e0\u4f55',
      '\u5361\u5c3a\u91c7\u6837',
      '\u8fb9\u7f18',
      '\u7a33\u5065\u62df\u5408',
      '\u8d28\u91cf\u95e8\u7981',
    ]);
    await expect(page.locator('#param-Dp')).toHaveCount(0);
    await expect(page.locator('#param-SearchCenterMode')).toHaveValue('ImageCenter');
    await expect(page.locator('#param-SearchCenterX')).toHaveValue('31.5');
    await expect(page.locator('#param-SearchCenterY')).toHaveValue('31.5');
    await expect(page.locator('#param-SearchCenterX')).toHaveAttribute('readonly', '');
    await expect(page.locator('#param-SearchCenterY')).toHaveAttribute('readonly', '');
    await expect(page.locator('[data-circle-search-v2-workload="true"]')).toContainText('Sampling work: 61,920');

    const state = await getCircleSearchV2State(page);
    expect(state).toMatchObject({
      params: {
        searchCenterMode: 'ImageCenter',
        searchCenterX: 31.5,
        searchCenterY: 31.5,
        minRadius: 12,
        nominalRadius: 18,
        maxRadius: 24,
      },
      overlay: {
        type: 'circleSearchV2',
        centerX: 31.5,
        centerY: 31.5,
        minRadius: 12,
        nominalRadius: 18,
        maxRadius: 24,
      },
      readonly: {
        searchCenterX: true,
        searchCenterY: true,
      },
      roiEditorExists: true,
    });
  });

  test('CircleMeasurement circle search center drag commits explicit geometry once', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64, StatusCode: 'OK' },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectCircleMeasurementNode(page);
    await waitForRoiEditorReady(page);
    await installRoiParameterChangeCounter(page);

    await dispatchRoiDrag(page, { x: 31.5, y: 31.5 }, { x: 36, y: 38 });
    await page.waitForTimeout(100);

    const state = await getCircleSearchV2State(page);
    expect(state).toMatchObject({
      changeCount: 1,
      params: {
        searchCenterMode: 'Explicit',
        minRadius: 12,
        nominalRadius: 18,
        maxRadius: 24,
      },
      nodeParams: {
        searchCenterMode: 'Explicit',
        minRadius: 12,
        nominalRadius: 18,
        maxRadius: 24,
      },
      overlay: {
        type: 'circleSearchV2',
        minRadius: 12,
        nominalRadius: 18,
        maxRadius: 24,
      },
    });
    expect(state.params.searchCenterX).toBeCloseTo(36, 1);
    expect(state.params.searchCenterY).toBeCloseTo(38, 1);
    expect(state.nodeParams.searchCenterX).toBeCloseTo(36, 1);
    expect(state.nodeParams.searchCenterY).toBeCloseTo(38, 1);
    expect(state.overlay?.centerX).toBeCloseTo(36, 1);
    expect(state.overlay?.centerY).toBeCloseTo(38, 1);
  });

  test('PolarUnwrap arc editing updates annulus radii and angle params', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectPolarNode(page, {
      CenterX: 32,
      CenterY: 32,
      InnerRadius: 8,
      OuterRadius: 20,
      StartAngle: 0,
      EndAngle: 180,
    });
    await waitForRoiEditorReady(page);

    let polarState = await getPolarState(page);
    expect(polarState.overlay).toEqual({
      type: 'arc',
      centerX: 32,
      centerY: 32,
      innerRadius: 8,
      outerRadius: 20,
      startAngle: 0,
      spanDegrees: 180,
    });

    await dispatchRoiDrag(page, { x: 52, y: 32 }, { x: 58, y: 32 });
    polarState = await getPolarState(page);
    expect(polarState.params.outerRadius).toBe(26);

    await dispatchRoiDrag(page, { x: 40, y: 32 }, { x: 44, y: 32 });
    polarState = await getPolarState(page);
    expect(polarState.params.innerRadius).toBe(12);

    await dispatchRoiDrag(page, { x: 6, y: 32 }, { x: 32, y: 58 });
    polarState = await getPolarState(page);
    expect(polarState.params.startAngle).toBe(0);
    expect(polarState.params.endAngle).toBe(90);
    expect(polarState.overlay?.spanDegrees).toBe(90);
  });

  test('polygon ROI vertex drag insert delete undo redo updates PolygonPoints only on commit', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, {
      Shape: 'Polygon',
      PolygonPoints: '[[5,5],[55,5],[55,55],[5,55]]',
    });
    await waitForRoiEditorReady(page);

    let polygonState = await getPolygonState(page);
    expect(polygonState.overlay?.type).toBe('polygon');
    expect(polygonState.params).toEqual([[5, 5], [55, 5], [55, 55], [5, 55]]);

    await dispatchRoiDrag(page, { x: 5, y: 5 }, { x: 8, y: 8 });
    polygonState = await getPolygonState(page);
    expect(polygonState.params[0]).toEqual([8, 8]);

    await dispatchRoiDrag(page, { x: 55, y: 30 }, { x: 58, y: 30 }, 'left', { altKey: true });
    polygonState = await getPolygonState(page);
    expect(polygonState.params.length).toBe(5);
    expect(polygonState.params[2]).toEqual([58, 30]);

    await page.locator('.roi-editor-canvas').focus();
    await page.keyboard.press('Delete');
    polygonState = await getPolygonState(page);
    expect(polygonState.params.length).toBe(4);

    await page.keyboard.press(process.platform === 'darwin' ? 'Meta+Z' : 'Control+Z');
    polygonState = await getPolygonState(page);
    expect(polygonState.params.length).toBe(5);

    await page.keyboard.press(process.platform === 'darwin' ? 'Meta+Y' : 'Control+Y');
    polygonState = await getPolygonState(page);
    expect(polygonState.params.length).toBe(4);
  });

  test('polygon ROI stays editable with empty PolygonPoints and blank drag writes valid points', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, {
      Shape: 'Polygon',
      X: 5,
      Y: 6,
      Width: 12,
      Height: 10,
      PolygonPoints: '[]',
    });
    await waitForRoiEditorReady(page);
    await expect(page.locator('#roi-editor-readonly')).toHaveClass(/hidden/);

    let polygonState = await getPolygonState(page);
    expect(polygonState.overlay?.type).toBe('polygon');
    expect(polygonState.overlay?.points).toEqual([
      { x: 5, y: 6 },
      { x: 17, y: 6 },
      { x: 17, y: 16 },
      { x: 5, y: 16 },
    ]);

    await dispatchRoiDrag(page, { x: 30, y: 30 }, { x: 50, y: 48 });
    polygonState = await getPolygonState(page);
    expect(polygonState.params).toEqual([[30, 30], [50, 30], [50, 48], [30, 48]]);
    expect(polygonState.overlay?.points).toEqual([
      { x: 30, y: 30 },
      { x: 50, y: 30 },
      { x: 50, y: 48 },
      { x: 30, y: 48 },
    ]);
  });

  test('polygon ROI drag clamps at image bounds instead of freezing', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, {
      Shape: 'Polygon',
      PolygonPoints: '[[5,5],[25,5],[25,25],[5,25]]',
    });
    await waitForRoiEditorReady(page);

    await dispatchRoiDrag(page, { x: 15, y: 15 }, { x: -20, y: -20 });
    const polygonState = await getPolygonState(page);

    expect(polygonState.params).toEqual([[0, 0], [20, 0], [20, 20], [0, 20]]);
    expect(polygonState.overlay?.points).toEqual([
      { x: 0, y: 0 },
      { x: 20, y: 0 },
      { x: 20, y: 20 },
      { x: 0, y: 20 },
    ]);
  });

  test('NPoint draft workbench edits samples while ROI editor stays unmounted', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectNPointNode(page);
    await waitForCalibrationDraftWorkbenchReady(page);

    let draftState = await getCalibrationDraftState(page);
    expect(draftState.roiEditorMounted).toBe(false);
    expect(draftState.overlay?.type).toBe('pointSequence');
    expect(draftState.samples.length).toBe(3);

    await setCalibrationDraftNumberCell(page, 0, 0, 12);
    await setCalibrationDraftNumberCell(page, 0, 1, 22);
    await page.locator('.calibration-draft-table tbody tr').nth(0).locator('input[type="checkbox"]').uncheck();

    draftState = await getCalibrationDraftState(page);
    expect(draftState.samples[0]).toMatchObject({
      pixelX: 12,
      pixelY: 22,
      worldX: 1,
      worldY: 2,
      enabled: false,
    });

    await page.locator('.calibration-draft-table tbody tr').nth(0).locator('button', { hasText: '↓' }).click();
    draftState = await getCalibrationDraftState(page);
    expect(draftState.samples[1]).toMatchObject({
      pixelX: 12,
      pixelY: 22,
      worldX: 1,
      worldY: 2,
      enabled: false,
    });

    await page.locator('.calibration-draft-table tbody tr').nth(1).locator('button', { hasText: '×' }).click();
    draftState = await getCalibrationDraftState(page);
    expect(draftState.samples.length).toBe(2);
    expect(draftState.samples.some((item: any) => item.enabled === false)).toBe(false);
  });

  test('pointer movement stays local draft until mouseup commit', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);

    await installRoiParameterChangeCounter(page);

    await dispatchRoiDragWithoutMouseUp(page, { x: 16, y: 18 }, { x: 24, y: 26 });
    await page.waitForTimeout(100);

    let draftState = await getRoiCommitProbeState(page);

    expect(draftState).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 18,
      formY: 20
    });

    await dispatchRoiMouseUp(page, { x: 24, y: 26 });
    await page.waitForTimeout(100);

    draftState = await getRoiCommitProbeState(page);

    expect(draftState).toMatchObject({
      changeCount: 1,
      nodeX: 18,
      nodeY: 20,
      formX: 18,
      formY: 20
    });
  });

  test('pointer capture commits once when released outside the ROI canvas', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);
    await installRoiParameterChangeCounter(page);

    const start = await getCanvasPointForImagePoint(page, { x: 16, y: 18 });
    const canvasBox = await page.locator('.roi-editor-canvas').boundingBox();
    if (!canvasBox) {
      throw new Error('ROI editor canvas bounds not available');
    }
    const outside = { x: canvasBox.x + canvasBox.width + 30, y: start.y };

    await dispatchPointerDragByClientPoints(page, start, outside);
    await page.waitForTimeout(100);

    const state = await getRoiCommitProbeState(page);
    expect(state).toMatchObject({
      changeCount: 1,
      nodeX: 52,
      nodeY: 12,
      formX: 52,
      formY: 12,
      activePointerId: null,
    });
  });

  test('unreleased pointer drag is canceled when the preview input image switches', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    let previewCallCount = 0;
    await page.route('**/api/flows/preview-node', async route => {
      previewCallCount += 1;
      const image = previewCallCount === 1 ? PREVIEW_PNG_BASE64 : SECOND_PREVIEW_PNG_BASE64;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(createPreviewResponse(image, previewCallCount)),
      });
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);
    await waitForRoiImageSource(page, PREVIEW_PNG_BASE64);
    expect(previewCallCount).toBe(1);
    await installRoiLifecycleProbe(page);

    await dispatchPointerDragWithoutRelease(page, { x: 16, y: 18 }, { x: 24, y: 26 });
    expect(await getRoiLifecycleProbeState(page)).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 18,
      formY: 20,
    });

    await requestImmediateRoiPreview(page);
    await waitForRoiImageSource(page, SECOND_PREVIEW_PNG_BASE64);

    const state = await getRoiLifecycleProbeState(page);
    expect(state).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 10,
      formY: 12,
      activePointerId: null,
      interactionState: null,
      activeHandle: null,
      capturedPointerCount: 0,
      imageWidth: 64,
      imageHeight: 64,
    });
    expect(state.currentImageSource).toContain(SECOND_PREVIEW_PNG_BASE64);
    expect(previewCallCount).toBe(2);
    expect(state.previewCardCount).toBeLessThanOrEqual(1);
    expect(state.unhandledRejections).toEqual([]);
    expect(pageErrors).toEqual([]);
  });

  test('unreleased pointer drag is canceled when the preview input image disappears', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    let previewCallCount = 0;
    await page.route('**/api/flows/preview-node', async route => {
      previewCallCount += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(createPreviewResponse(PREVIEW_PNG_BASE64, previewCallCount)),
      });
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);
    await installRoiLifecycleProbe(page);

    await dispatchPointerDragWithoutRelease(page, { x: 16, y: 18 }, { x: 24, y: 26 });
    await page.evaluate(() => {
      const coordinator = (window as any).nodePreviewCoordinator;
      coordinator?.updateState?.({
        status: 'success',
        inputImageBase64: null,
        outputImageBase64: null,
        outputData: { Width: 64, Height: 64 },
      });
    });
    await waitForRoiImageSource(page, null);
    await page.waitForFunction(() => !(window as any).propertyPanel?.roiEditorPanel?.imageCanvas?.image);

    const state = await getRoiLifecycleProbeState(page);
    expect(state).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 10,
      formY: 12,
      activePointerId: null,
      interactionState: null,
      activeHandle: null,
      capturedPointerCount: 0,
      currentImageSource: null,
      imageWidth: null,
      imageHeight: null,
      emptyVisible: true,
    });
    expect(previewCallCount).toBe(1);
    expect(state.previewCardCount).toBeLessThanOrEqual(1);
    expect(state.unhandledRejections).toEqual([]);
    expect(pageErrors).toEqual([]);
  });

  test('unreleased pointer drag is canceled when switching to another node', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    let previewCallCount = 0;
    await page.route('**/api/flows/preview-node', async route => {
      previewCallCount += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(createPreviewResponse(PREVIEW_PNG_BASE64, previewCallCount)),
      });
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);
    await installRoiLifecycleProbe(page);

    await dispatchPointerDragWithoutRelease(page, { x: 16, y: 18 }, { x: 24, y: 26 });
    await addAndSelectPlainImageNode(page);
    await page.waitForFunction(() => !(window as any).propertyPanel?.roiEditorPanel);

    const state = await getRoiLifecycleProbeState(page);
    expect(state).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      nodeWidth: 12,
      nodeHeight: 14,
      activePointerId: null,
      interactionState: null,
      activeHandle: null,
      capturedPointerCount: 0,
      roiEditorExists: false,
    });
    expect(previewCallCount).toBe(1);
    expect(state.previewCardCount).toBeLessThanOrEqual(1);
    expect(state.unhandledRejections).toEqual([]);
    expect(pageErrors).toEqual([]);
  });

  test('unreleased pointer drag is canceled when ROI editor is destroyed', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    let previewCallCount = 0;
    await page.route('**/api/flows/preview-node', async route => {
      previewCallCount += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(createPreviewResponse(PREVIEW_PNG_BASE64, previewCallCount)),
      });
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);
    await installRoiLifecycleProbe(page);

    await dispatchPointerDragWithoutRelease(page, { x: 16, y: 18 }, { x: 24, y: 26 });
    await page.evaluate(() => {
      (window as any).propertyPanel?.roiEditorPanel?.destroy?.();
      if ((window as any).propertyPanel) {
        (window as any).propertyPanel.roiEditorPanel = null;
      }
    });

    const state = await getRoiLifecycleProbeState(page);
    expect(state).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 10,
      formY: 12,
      activePointerId: null,
      interactionState: null,
      activeHandle: null,
      capturedPointerCount: 0,
      roiEditorExists: false,
    });
    expect(previewCallCount).toBe(1);
    expect(state.previewCardCount).toBeLessThanOrEqual(1);
    expect(state.unhandledRejections).toEqual([]);
    expect(pageErrors).toEqual([]);
  });

  test('delayed older preview image cannot replace newer preview during unreleased drag', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    let previewCallCount = 0;
    let releaseDelayedPreview: (() => void) | null = null;
    const delayedPreview = new Promise<void>(resolve => {
      releaseDelayedPreview = resolve;
    });

    await page.route('**/api/flows/preview-node', async route => {
      previewCallCount += 1;
      const call = previewCallCount;
      if (call === 2) {
        await delayedPreview;
      }

      const image = call === 3 ? SECOND_PREVIEW_PNG_BASE64 : PREVIEW_PNG_BASE64;
      try {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(createPreviewResponse(image, call)),
        });
      } catch {
        // The delayed request is expected to be aborted by the newer preview request.
      }
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);
    await waitForRoiImageSource(page, PREVIEW_PNG_BASE64);
    await installRoiLifecycleProbe(page);

    await dispatchPointerDragWithoutRelease(page, { x: 16, y: 18 }, { x: 24, y: 26 });
    await requestImmediateRoiPreview(page);
    await expect.poll(() => previewCallCount).toBe(2);
    await requestImmediateRoiPreview(page);
    await expect.poll(() => previewCallCount).toBe(3);
    await waitForRoiImageSource(page, SECOND_PREVIEW_PNG_BASE64);

    releaseDelayedPreview?.();
    await page.waitForTimeout(250);

    const state = await getRoiLifecycleProbeState(page);
    expect(state).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 10,
      formY: 12,
      activePointerId: null,
      interactionState: null,
      activeHandle: null,
      capturedPointerCount: 0,
      imageWidth: 64,
      imageHeight: 64,
    });
    expect(state.currentImageSource).toContain(SECOND_PREVIEW_PNG_BASE64);
    expect(previewCallCount).toBe(3);
    expect(state.previewCardCount).toBeLessThanOrEqual(1);
    expect(state.unhandledRejections).toEqual([]);
    expect(pageErrors).toEqual([]);
  });

  test('pointer cancel and ROI editor destroy roll back active drafts without commit', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);
    await installRoiParameterChangeCounter(page);

    await dispatchPointerDragWithoutRelease(page, { x: 16, y: 18 }, { x: 24, y: 26 });
    expect(await getRoiCommitProbeState(page)).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 18,
      formY: 20,
    });

    await dispatchActivePointerCancel(page);
    await page.waitForTimeout(100);
    expect(await getRoiCommitProbeState(page)).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 10,
      formY: 12,
      activePointerId: null,
    });

    await dispatchPointerDragWithoutRelease(page, { x: 16, y: 18 }, { x: 24, y: 26 });
    expect(await getRoiCommitProbeState(page)).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 18,
      formY: 20,
    });

    await page.evaluate(() => {
      (window as any).propertyPanel?.roiEditorPanel?.destroy?.();
      if ((window as any).propertyPanel) {
        (window as any).propertyPanel.roiEditorPanel = null;
      }
    });
    await page.waitForTimeout(100);
    expect(await getRoiCommitProbeState(page)).toMatchObject({
      changeCount: 0,
      nodeX: 10,
      nodeY: 12,
      formX: 10,
      formY: 12,
      activePointerId: null,
    });
  });

  test('keyboard nudge undo redo and escape cancel remain local to the ROI draft session', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, { X: 10, Y: 12, Width: 12, Height: 14 });
    await waitForRoiEditorReady(page);

    await page.locator('.roi-editor-canvas').focus();
    await page.keyboard.press('ArrowRight');
    let roiState = await getRoiState(page);
    expect(roiState.params.x).toBe(11);
    expect(roiState.overlay?.x).toBe(11);

    await page.keyboard.press(process.platform === 'darwin' ? 'Meta+Z' : 'Control+Z');
    roiState = await getRoiState(page);
    expect(roiState.params.x).toBe(10);
    expect(roiState.overlay?.x).toBe(10);

    await page.keyboard.press(process.platform === 'darwin' ? 'Meta+Y' : 'Control+Y');
    roiState = await getRoiState(page);
    expect(roiState.params.x).toBe(11);
    expect(roiState.overlay?.x).toBe(11);

    await dispatchRoiDragWithoutMouseUp(page, { x: 17, y: 18 }, { x: 30, y: 32 });
    roiState = await getRoiState(page);
    expect(roiState.params.x).toBe(24);
    expect(roiState.params.y).toBe(26);

    await page.keyboard.press('Escape');
    roiState = await getRoiState(page);
    expect(roiState.params.x).toBe(11);
    expect(roiState.params.y).toBe(12);
    expect(roiState.overlay).toEqual({ x: 11, y: 12, width: 12, height: 14 });
  });

  test('manual parameter edits sync overlay and right-button pan does not mutate ROI', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64 },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectRoiNode(page, { X: 6, Y: 7, Width: 10, Height: 12 });
    await waitForRoiEditorReady(page);

    await page.fill('#param-X', '14');
    await page.fill('#param-Y', '16');
    await page.fill('#param-Width', '18');
    await page.fill('#param-Height', '20');
    await page.locator('#param-Height').blur();
    await page.waitForTimeout(300);

    let roiState = await getRoiState(page);
    expect(roiState.overlay).toEqual({ x: 14, y: 16, width: 18, height: 20 });

    const beforePan = roiState.overlay;
    await dispatchRoiDrag(page, { x: 5, y: 5 }, { x: 12, y: 12 }, 'right');

    roiState = await getRoiState(page);
    expect(roiState.overlay).toEqual(beforePan);
    expect(roiState.params).toEqual({
      x: 14,
      y: 16,
      width: 18,
      height: 20,
    });
  });
});

test.describe('ROI Editor Circle Search V2 startup flag off', () => {
  test.beforeEach(async ({ page }) => {
    await installCircleSearchV2StartupFlag(page, false);
    await stubOperatorLibrary(page);
    await bootAuthenticatedApp(page);
    await setCurrentProject(page);
  });

  test('keeps CaliperFitV2 on the generic parameter panel when the startup flag is off', async ({ page }) => {
    await page.route('**/api/flows/preview-node', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          inputImageBase64: PREVIEW_PNG_BASE64,
          outputImageBase64: PREVIEW_PNG_BASE64,
          outputData: { Width: 64, Height: 64, StatusCode: 'OK' },
          executionTimeMs: 10,
        }),
      });
    });

    await addAndSelectCircleMeasurementNode(page);
    await expect(page.locator('#property-form')).toBeVisible();
    await expect(page.locator('.roi-editor-panel')).toHaveCount(0);
    await expect(page.locator('[data-circle-search-v2-workload="true"]')).toHaveCount(0);
    await expect(page.locator('#param-Dp')).toHaveCount(1);
    await expect(page.locator('#param-SearchCenterMode')).toHaveValue('ImageCenter');
    await expect(page.locator('#param-SearchCenterX')).toHaveValue('10');
    await expect(page.locator('#param-SearchCenterY')).toHaveValue('12');

    const state = await getCircleSearchV2State(page);
    expect(state).toMatchObject({
      readonly: {
        searchCenterX: false,
        searchCenterY: false,
      },
      roiEditorExists: false,
    });
  });
});
