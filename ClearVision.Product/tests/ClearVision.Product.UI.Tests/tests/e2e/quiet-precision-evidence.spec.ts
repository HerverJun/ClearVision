import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { expect, Page, Route, test } from '@playwright/test';

type Theme = 'light' | 'dark';

const phase = process.env.QP_EVIDENCE_PHASE === 'final' ? 'final' : 'initial';
const evidenceRoot = path.resolve(
  process.cwd(),
  '../../../artifacts/quiet-precision',
  phase,
);

const user = {
  userId: 'quiet-precision-admin',
  id: 'quiet-precision-admin',
  username: 'admin',
  displayName: 'Quiet Precision QA',
  role: 'Admin',
};

const project = {
  id: 'quiet-precision-project',
  name: '精密连接器检测',
  description: '用于设计语言收敛取证的正式工程',
  createdAt: '2026-07-01T08:00:00.000Z',
  modifiedAt: '2026-07-12T08:00:00.000Z',
  flowRevision: 3,
  flow: {
    version: '1.0',
    nodes: [],
    connections: [],
  },
  globalVariables: {
    schemaVersion: 1,
    variables: [
      {
        id: 'gv-threshold',
        name: 'threshold',
        displayName: '判定阈值',
        valueType: 'Double',
        initialValue: 128,
        description: '用于最终判定的工程阈值',
        order: 0,
      },
      {
        id: 'gv-station-name',
        name: 'station_name',
        displayName: '工位名称',
        valueType: 'String',
        initialValue: 'Station A',
        description: '当前检测工位',
        order: 1,
      },
    ],
    sourceBindings: [],
    targetBindings: [],
  },
};

const stations = [
  {
    stationId: 'station-a',
    stationName: 'Station A',
    lineName: '连接器一线',
    machineName: 'CV-01',
    state: 'Running',
    onlineState: 'Online',
    isOnline: true,
    lastSeenAtUtc: '2026-07-12T08:00:00Z',
    sessionOkCount: 248,
    sessionNgCount: 7,
    sessionErrorCount: 0,
    averageExecutionTimeMs: 26,
    lastOutcome: 'Ok',
    lastDiagnosticCode: '',
  },
  {
    stationId: 'station-b',
    stationName: 'Station B',
    lineName: '连接器二线',
    machineName: 'CV-02',
    state: 'Warning',
    onlineState: 'Online',
    isOnline: true,
    lastSeenAtUtc: '2026-07-12T07:59:56Z',
    sessionOkCount: 191,
    sessionNgCount: 11,
    sessionErrorCount: 2,
    averageExecutionTimeMs: 31,
    lastOutcome: 'Ng',
    lastDiagnosticCode: 'PIN_OFFSET',
  },
  {
    stationId: 'station-c',
    stationName: 'Station C',
    lineName: '连接器三线',
    machineName: 'CV-03',
    state: 'Offline',
    onlineState: 'Offline',
    isOnline: false,
    lastSeenAtUtc: '2026-07-12T07:54:00Z',
    sessionOkCount: 0,
    sessionNgCount: 0,
    sessionErrorCount: 0,
    averageExecutionTimeMs: 0,
    lastOutcome: 'Unknown',
    lastDiagnosticCode: 'HEARTBEAT_TIMEOUT',
  },
];

function settingsPayload(theme: Theme) {
  return {
    general: {
      softwareTitle: 'ClearVision',
      language: 'zh-CN',
      theme,
      autoStart: false,
    },
    communication: {
      activeProtocol: 'S7',
      heartbeatIntervalMs: 1000,
      s7: {
        ipAddress: '192.168.0.10',
        port: 102,
        cpuType: 'S7-1200',
        rack: 0,
        slot: 1,
        mappings: [],
      },
      mc: { ipAddress: '192.168.3.10', port: 5002, mappings: [] },
      fins: { ipAddress: '192.168.250.10', port: 9600, mappings: [] },
    },
    tcp: { enabled: false, host: '127.0.0.1', port: 9000 },
    station: { enabled: true, stationId: 'station-a' },
    storage: {
      imageDirectory: 'D:/ClearVision/images',
      resultDirectory: 'D:/ClearVision/results',
      retentionDays: 30,
    },
    database: { provider: 'SQLite', connectionString: 'clearvision.db' },
    runtime: {
      autoRun: false,
      stopOnConsecutiveNg: 3,
      missingMaterialTimeoutSeconds: 15,
    },
    cameras: [],
    ai: { enabled: true, provider: 'OpenAI', model: 'gpt-5' },
  };
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

async function mockApis(page: Page, theme: Theme): Promise<void> {
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const pathname = url.pathname;
    const method = request.method();

    if (pathname === '/api/auth/me') return json(route, user);
    if (pathname === '/api/auth/setup-status') return json(route, { requiresInitialAdminSetup: false });
    if (pathname === '/api/settings') return json(route, settingsPayload(theme));
    if (pathname === '/api/operators/library' || pathname === '/api/operators/types') return json(route, []);
    if (pathname === '/api/projects' || pathname === '/api/projects/recent') return json(route, [project]);
    if (pathname === `/api/projects/${project.id}`) return json(route, project);
    if (pathname === `/api/projects/${project.id}/global-variables`) return json(route, project.globalVariables);
    if (pathname.startsWith('/api/projects/') && method === 'PUT') return json(route, project);
    if (pathname === '/api/inspection/history') {
      return json(route, {
        items: [
          {
            id: 'inspection-001',
            projectId: project.id,
            status: 'OK',
            timestamp: '2026-07-12T07:58:00Z',
            processingTimeMs: 26,
            defectCount: 0,
          },
          {
            id: 'inspection-002',
            projectId: project.id,
            status: 'NG',
            timestamp: '2026-07-12T07:57:30Z',
            processingTimeMs: 29,
            defectCount: 1,
          },
        ],
        totalCount: 2,
        pageIndex: 0,
        pageSize: 12,
      });
    }
    if (pathname.includes('/analytics')) {
      return json(route, {
        totalCount: 255,
        okCount: 248,
        ngCount: 7,
        errorCount: 0,
        yieldRate: 97.25,
        averageProcessingTimeMs: 26,
        trend: [],
        defectDistribution: [{ name: 'PIN_OFFSET', count: 7 }],
      });
    }
    if (pathname === '/api/station-packages') return json(route, []);
    if (pathname === '/api/stations/summary') {
      return json(route, {
        totalStations: 3,
        onlineStations: 2,
        offlineStations: 1,
        runningStations: 2,
        alertCount: 2,
        totalOkCount: 439,
        totalNgCount: 18,
        totalErrorCount: 2,
        averageExecutionTimeMs: 28,
        offlineThresholdSeconds: 15,
        updatedAtUtc: '2026-07-12T08:00:00Z',
      });
    }
    if (pathname === '/api/stations/results') {
      return json(route, { items: [], totalCount: 0, pageIndex: 0, pageSize: 12 });
    }
    if (pathname === '/api/stations/events') {
      const body = [
        'event: initialState',
        `data: ${JSON.stringify({ summary: { offlineThresholdSeconds: 15 }, stations, recentResults: [] })}`,
        '',
        '',
      ].join('\n');
      return route.fulfill({ status: 200, contentType: 'text/event-stream', body });
    }
    if (pathname === '/api/stations') return json(route, stations);
    if (pathname.startsWith('/api/stations/')) {
      const stationId = decodeURIComponent(pathname.split('/').pop() || '');
      const station = stations.find(item => item.stationId === stationId);
      return json(route, station ? { ...station, recentResults: [], recentHealth: [], recentLogs: [], recentCommands: [] } : {}, station ? 200 : 404);
    }
    if (pathname === '/api/users' || pathname === '/api/camera-bindings' || pathname === '/api/ai/models') {
      return json(route, []);
    }
    if (pathname === '/api/cameras/bindings' || pathname === '/api/cameras/serial-photoelectric/ports') {
      return json(route, []);
    }
    if (pathname === '/api/cameras/discover/huaray') {
      return json(route, {
        devices: [{
          cameraId: 'Huaray-MV-001',
          manufacturer: 'Huaray',
          model: 'MV-CV200',
          connectionType: 'GigE',
          ipAddress: '192.168.1.21',
        }],
      });
    }

    if (method === 'GET') return json(route, {});
    return json(route, { ok: true, theme });
  });
}

async function boot(page: Page, theme: Theme): Promise<void> {
  await mockApis(page, theme);
  await page.addInitScript(({ currentUser, currentTheme }) => {
    sessionStorage.setItem('cv_auth_token', 'quiet-precision-token');
    sessionStorage.setItem('cv_current_user', JSON.stringify(currentUser));
    localStorage.setItem('cv_welcome_shown', 'true');
    localStorage.setItem('cv_theme', currentTheme);
    Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
      value: Object.freeze({
        featureFlags: Object.freeze({
          'Studio2.PropertyPanel': true,
          'Studio2.PreviewPanel': true,
          'Studio2.GlobalVariables': true,
          'Studio2.ProjectPage': true,
          'Studio2.ResultsReview': true,
          'Studio:NodePreviewInspectorEnabled': false,
          'Studio:CircleSearchV2ToolEnabled': true,
          'Studio:NPointCalibrationWorkbenchEnabled': true,
        }),
      }),
      writable: false,
      configurable: false,
    });
  }, { currentUser: user, currentTheme: theme });
  await page.goto('/index.html');
  await expect(page.locator('#app')).toBeVisible();
  await expect(page.locator('#loading-screen')).toBeHidden();
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
}

async function selectView(page: Page, view: string): Promise<void> {
  const button = page.locator(`.nav-btn[data-view="${view}"]`);
  await expect(button).toBeVisible();
  await button.click();
  await expect(button).toHaveClass(/active/);
}

async function capture(page: Page, theme: Theme, name: string): Promise<void> {
  await page.evaluate(() => {
    document.querySelectorAll('.cv-toast').forEach(toast => toast.remove());
  });
  const directory = path.join(evidenceRoot, theme);
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: path.join(directory, `${name}.png`), fullPage: true });
}

async function writeEvidenceJson(theme: Theme, name: string, value: unknown): Promise<void> {
  const directory = path.join(evidenceRoot, theme);
  await mkdir(directory, { recursive: true });
  await writeFile(path.join(directory, `${name}.json`), `${JSON.stringify(value, null, 2)}\n`, 'utf8');
}

async function collectLayoutMetrics(page: Page, view: string) {
  return page.evaluate(currentView => {
    const rect = (selector: string) => {
      const element = document.querySelector(selector) as HTMLElement | null;
      if (!element) return null;
      const bounds = element.getBoundingClientRect();
      return {
        x: Math.round(bounds.x),
        y: Math.round(bounds.y),
        width: Math.round(bounds.width),
        height: Math.round(bounds.height),
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth,
        clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight,
      };
    };
    const visible = (selector: string) => {
      const element = document.querySelector(selector) as HTMLElement | null;
      if (!element) return false;
      const bounds = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return style.display !== 'none' && style.visibility !== 'hidden' && bounds.width > 0 && bounds.height > 0;
    };
    const gridColumnCount = (selector: string) => {
      const element = document.querySelector(selector) as HTMLElement | null;
      if (!element) return 0;
      const columns = getComputedStyle(element).gridTemplateColumns.trim();
      if (!columns || columns === 'none') return 0;
      let depth = 0;
      let token = '';
      const tokens: string[] = [];
      for (const character of columns) {
        if (character === '(') depth += 1;
        if (character === ')') depth = Math.max(0, depth - 1);
        if (/\s/.test(character) && depth === 0) {
          if (token) tokens.push(token);
          token = '';
        } else {
          token += character;
        }
      }
      if (token) tokens.push(token);
      return tokens.length;
    };
    const maxColumnsByRow = (selector: string) => {
      const rows = new Map<number, number>();
      document.querySelectorAll(selector).forEach(node => {
        const bounds = (node as HTMLElement).getBoundingClientRect();
        if (bounds.width <= 0 || bounds.height <= 0) return;
        const row = Math.round(bounds.top);
        rows.set(row, (rows.get(row) || 0) + 1);
      });
      return Math.max(0, ...rows.values());
    };
    const hitTarget = (selector: string) => {
      const element = document.querySelector(selector) as HTMLElement | null;
      if (!element) return false;
      const bounds = element.getBoundingClientRect();
      const target = document.elementFromPoint(bounds.left + bounds.width / 2, bounds.top + bounds.height / 2);
      return target === element || Boolean(target && element.contains(target));
    };
    const viewSelectors: Record<string, string> = {
      project: '#project-view',
      flow: '#flow-editor',
      inspection: '#inspection-view',
      results: '#results-view',
      stations: '#stations-view',
      ai: '#ai-view',
      settings: '#settings-view',
    };
    return {
      view: currentView,
      viewport: { width: innerWidth, height: innerHeight, devicePixelRatio },
      document: {
        clientWidth: document.documentElement.clientWidth,
        scrollWidth: document.documentElement.scrollWidth,
        overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      },
      body: {
        clientWidth: document.body.clientWidth,
        scrollWidth: document.body.scrollWidth,
        overflow: document.body.scrollWidth - document.body.clientWidth,
      },
      main: rect('#main-content'),
      toolbar: rect('.toolbar'),
      toolbarLeft: rect('.toolbar-left'),
      toolbarRight: rect('.toolbar-right'),
      mainNavigation: rect('.main-nav'),
      viewRoot: rect(viewSelectors[currentView] || ''),
      settingsLayout: rect('.settings-layout'),
      settingsContent: rect('.settings-content-area'),
      settingsPanel: rect('.settings-panel.active'),
      settingsSidebar: rect('.settings-sidebar'),
      flowOperatorRail: rect('.operator-rail'),
      flowInspector: rect('.sidebar.left'),
      flowPreview: rect('.preview-workbench-pane'),
      inspectionControl: rect('.inspection-control-panel'),
      inspectionSide: rect('.inspection-side-panel'),
      aiWorkbench: rect('.ai-pane-right'),
      aiConversation: rect('.ai-pane-left'),
      finalDecision: rect('.final-decision-dialog'),
      modal: rect('.cv-modal'),
      columns: {
        plcConnection: gridColumnCount('.plc-connection-grid'),
        plcS7: gridColumnCount('.plc-s7-grid'),
        finalDecision: gridColumnCount('.final-decision-grid'),
        settingsFieldRowMax: maxColumnsByRow('.settings-panel.active .settings-fieldset'),
      },
      actions: {
        toolbarSave: visible('#btn-save'),
        toolbarRun: visible('#btn-run'),
        settingsNavigationHitTarget: hitTarget('.nav-btn[data-view="settings"]'),
        finalDecisionHitTarget: hitTarget('#btn-final-decision'),
        toolbarSaveHitTarget: hitTarget('#btn-save'),
        toolbarRunHitTarget: hitTarget('#btn-run'),
        settingsSave: visible('#btn-save-settings'),
        finalDecisionSave: visible('[data-decision-save]'),
        modalClose: visible('.cv-modal-close'),
      },
    };
  }, view);
}

const settingsAuditTabs = [
  'general',
  'communication',
  'tcp',
  'station',
  'storage',
  'database',
  'runtime',
  'cameras',
  'ai',
  'users',
] as const;

const wideSettingsTabs = new Set([
  'communication',
  'tcp',
  'station',
  'database',
  'cameras',
  'ai',
  'users',
]);

for (const theme of ['light', 'dark'] as const) {
  test(`Quiet Precision ${phase} formal-page evidence (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1366, height: 768 });
    await boot(page, theme);

    await selectView(page, 'project');
    await expect(page.locator('#project-view')).toHaveAttribute('data-project-page-owner', 'project-page-capability-v2');
    await expect(page.locator('[data-project-list]')).toContainText(project.name);
    await capture(page, theme, '1366-project');

    await page.locator(`[data-project-id="${project.id}"] [data-project-action="open"]`).click();
    await expect(page.locator('#gv-open-manager')).toBeEnabled();

    await selectView(page, 'flow');
    await expect(page.locator('#property-panel')).toHaveAttribute('data-property-panel-owner', 'property-panel-capability-v2');
    await expect(page.locator('#preview-panel')).toHaveAttribute('data-preview-panel-owner', 'preview-panel-capability-v2');
    await capture(page, theme, '1366-flow');

    await page.locator('#gv-open-manager').click();
    await expect(page.locator('.global-variable-capability-manager')).toBeVisible();
    await capture(page, theme, '1366-global-variables');
    await page.locator('[data-gv-action="close-manager"]').click();

    await selectView(page, 'inspection');
    await expect(page.locator('#inspection-control-panel')).toBeVisible();
    await capture(page, theme, '1366-inspection');

    await selectView(page, 'results');
    await expect(page.locator('#results-list-container')).toHaveAttribute('data-results-review-owner', 'results-review-capability-v2');
    await capture(page, theme, '1366-results');

    await selectView(page, 'stations');
    await expect(page.locator('#stations-view')).toContainText('Station A');
    await capture(page, theme, '1366-stations');

    await selectView(page, 'ai');
    await expect(page.locator('#ai-view .ai-shell')).toBeVisible();
    await capture(page, theme, '1366-ai');

    await selectView(page, 'settings');
    await expect(page.locator('#settings-view .settings-layout')).toBeVisible();
    await capture(page, theme, '1366-settings');

    await page.setViewportSize({ width: 1024, height: 768 });
    await capture(page, theme, '1024-settings');
    await selectView(page, 'flow');
    await capture(page, theme, '1024-flow');

    await page.setViewportSize({ width: 1920, height: 1080 });
    await selectView(page, 'stations');
    await capture(page, theme, '1920-stations');
  });

  test(`Quiet Precision ${phase} 1920 desktop layout audit (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await boot(page, theme);

    const metrics: Record<string, unknown> = {};

    await selectView(page, 'project');
    await expect(page.locator('[data-project-list]')).toContainText(project.name);
    metrics.project = await collectLayoutMetrics(page, 'project');
    await capture(page, theme, '1920-project');

    await page.locator(`[data-project-id="${project.id}"] [data-project-action="open"]`).click();
    await expect(page.locator('#gv-open-manager')).toBeEnabled();

    await selectView(page, 'flow');
    metrics.flow = await collectLayoutMetrics(page, 'flow');
    await capture(page, theme, '1920-flow');

    await page.locator('#gv-open-manager').click();
    await expect(page.locator('.global-variable-capability-manager')).toBeVisible();
    metrics.globalVariables = await collectLayoutMetrics(page, 'flow');
    await capture(page, theme, '1920-global-variables');
    await page.locator('[data-gv-action="close-manager"]').click();

    await page.locator('#btn-final-decision').click();
    await expect(page.locator('.final-decision-dialog')).toBeVisible();
    metrics.finalDecision = await collectLayoutMetrics(page, 'flow');
    await capture(page, theme, '1920-final-decision');
    await page.locator('.final-decision-dialog > header [data-decision-close]').click();

    const viewReadySelectors = {
      inspection: '#inspection-control-panel',
      results: '#results-list-container[data-results-review-owner="results-review-capability-v2"]',
      stations: '.station-monitor-view',
      ai: '#ai-view .ai-shell',
    } as const;
    for (const view of ['inspection', 'results', 'stations', 'ai'] as const) {
      await selectView(page, view);
      await expect(page.locator(viewReadySelectors[view])).toBeVisible();
      metrics[view] = await collectLayoutMetrics(page, view);
      await capture(page, theme, `1920-${view}`);
    }

    await selectView(page, 'settings');
    await expect(page.locator('#settings-view .settings-layout')).toBeVisible();
    const settings: Record<string, unknown> = {};
    let cameraDiscoveryModal: Awaited<ReturnType<typeof collectLayoutMetrics>> | null = null;
    for (const tabName of settingsAuditTabs) {
      const menuItem = page.locator(`.settings-menu-item[data-tab="${tabName}"]`);
      await expect(menuItem).toBeVisible();
      await menuItem.click();
      await expect(page.locator(`.settings-panel[data-section="${tabName}"]`)).toHaveClass(/active/);
      settings[tabName] = await collectLayoutMetrics(page, 'settings');
      await capture(page, theme, `1920-settings-${tabName}`);
      if (tabName === 'cameras') {
        await page.locator('#btn-discover-huaray-cameras').click();
        await expect(page.locator('.cv-modal[data-modal-size="large"]')).toBeVisible();
        cameraDiscoveryModal = await collectLayoutMetrics(page, 'settings');
        await capture(page, theme, '1920-settings-cameras-discovery-modal');
        await page.locator('.cv-modal-close').click();
        await expect(page.locator('.cv-modal-overlay')).toBeHidden();
      }
    }
    metrics.settings = settings;
    metrics.cameraDiscoveryModal = cameraDiscoveryModal;
    await writeEvidenceJson(theme, '1920-layout-metrics', metrics);

    const pageMetrics = Object.entries(metrics)
      .filter(([name]) => !['settings', 'globalVariables', 'finalDecision', 'flow', 'cameraDiscoveryModal'].includes(name))
      .map(([, value]) => value as Awaited<ReturnType<typeof collectLayoutMetrics>>);
    for (const item of pageMetrics) {
      expect(item.document.overflow, `${item.view} document overflow`).toBeLessThanOrEqual(1);
      expect(item.body.overflow, `${item.view} body overflow`).toBeLessThanOrEqual(1);
      expect(item.viewRoot?.width || 0, `${item.view} formal owner width`).toBeGreaterThanOrEqual((item.main?.width || 0) - 28);
    }

    const flowMetrics = metrics.flow as Awaited<ReturnType<typeof collectLayoutMetrics>>;
    expect(flowMetrics.document.overflow, 'flow document overflow').toBeLessThanOrEqual(1);
    expect(flowMetrics.body.overflow, 'flow body overflow').toBeLessThanOrEqual(1);
    expect(flowMetrics.viewRoot?.width || 0, 'flow canvas working width').toBeGreaterThanOrEqual(1000);
    expect(flowMetrics.flowPreview?.width || 0, 'flow preview auxiliary width').toBeLessThanOrEqual(440);

    const finalDecisionMetrics = metrics.finalDecision as Awaited<ReturnType<typeof collectLayoutMetrics>>;
    expect(finalDecisionMetrics.finalDecision?.width || 0, 'Final Decision desktop work dialog width').toBeGreaterThanOrEqual(780);
    expect(finalDecisionMetrics.actions.finalDecisionSave, 'Final Decision save action visible').toBe(true);

    expect(cameraDiscoveryModal?.modal?.width || 0, 'camera discovery work modal width').toBeGreaterThanOrEqual(900);
    expect(cameraDiscoveryModal?.modal?.scrollWidth || 0, 'camera discovery modal horizontal clipping')
      .toBeLessThanOrEqual(cameraDiscoveryModal?.modal?.clientWidth || 0);
    expect(cameraDiscoveryModal?.actions.modalClose, 'camera discovery modal close action visible').toBe(true);

    for (const [tabName, value] of Object.entries(settings)) {
      const item = value as Awaited<ReturnType<typeof collectLayoutMetrics>>;
      expect(item.document.overflow, `${tabName} document overflow`).toBeLessThanOrEqual(1);
      expect(item.body.overflow, `${tabName} body overflow`).toBeLessThanOrEqual(1);
      expect(item.actions.settingsSave, `${tabName} save action visible`).toBe(true);
      const targetWidth = wideSettingsTabs.has(tabName)
        ? Math.min((item.settingsContent?.width || 0) - 64, 1360)
        : Math.min((item.settingsContent?.width || 0) - 64, 1160);
      expect(item.settingsPanel?.width || 0, `${tabName} content width`).toBeGreaterThanOrEqual(targetWidth);
    }

    const communicationMetrics = settings.communication as Awaited<ReturnType<typeof collectLayoutMetrics>>;
    const cameraMetrics = settings.cameras as Awaited<ReturnType<typeof collectLayoutMetrics>>;
    const generalMetrics = settings.general as Awaited<ReturnType<typeof collectLayoutMetrics>>;
    expect(communicationMetrics.columns.plcConnection, 'PLC connection fields per row').toBeGreaterThanOrEqual(4);
    expect(communicationMetrics.columns.plcS7, 'PLC S7 fields per row').toBeGreaterThanOrEqual(3);
    expect(cameraMetrics.columns.settingsFieldRowMax, 'camera configuration fields per row').toBeGreaterThanOrEqual(4);
    expect(generalMetrics.columns.settingsFieldRowMax, 'general settings fields per row').toBeGreaterThanOrEqual(2);
  });
}

test(`Quiet Precision ${phase} desktop viewport matrix`, async ({ page }) => {
  const theme: Theme = 'dark';
  await page.setViewportSize({ width: 1600, height: 900 });
  await boot(page, theme);
  await selectView(page, 'project');
  await expect(page.locator('[data-project-list]')).toContainText(project.name);
  await page.locator(`[data-project-id="${project.id}"] [data-project-action="open"]`).click();

  const matrix: Record<string, unknown> = {};
  const viewReadySelectors = {
    project: '#project-view[data-project-page-owner="project-page-capability-v2"]',
    flow: '#flow-editor',
    inspection: '#inspection-control-panel',
    results: '#results-list-container[data-results-review-owner="results-review-capability-v2"]',
    stations: '.station-monitor-view',
    ai: '#ai-view .ai-shell',
    settings: '#settings-view .settings-layout',
  } as const;

  for (const viewport of [
    { width: 1600, height: 900 },
    { width: 1366, height: 768 },
    { width: 1024, height: 768 },
  ]) {
    await page.setViewportSize(viewport);
    await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
    const key = `${viewport.width}x${viewport.height}`;
    const viewMetrics: Record<string, unknown> = {};

    for (const view of ['project', 'flow', 'inspection', 'results', 'stations', 'ai', 'settings'] as const) {
      await selectView(page, view);
      await expect(page.locator(viewReadySelectors[view])).toBeVisible();
      const item = await collectLayoutMetrics(page, view);
      viewMetrics[view] = item;
      expect(item.document.overflow, `${key} ${view} document overflow`).toBeLessThanOrEqual(1);
      expect(item.body.overflow, `${key} ${view} body overflow`).toBeLessThanOrEqual(1);
      expect(item.actions.settingsNavigationHitTarget, `${key} ${view} settings navigation reachable`).toBe(true);
      expect(item.actions.finalDecisionHitTarget, `${key} ${view} final decision reachable`).toBe(true);
      expect(item.actions.toolbarSaveHitTarget, `${key} ${view} save action reachable`).toBe(true);
      expect(item.actions.toolbarRunHitTarget, `${key} ${view} run action reachable`).toBe(true);
      expect((item.toolbarLeft?.x || 0) + (item.toolbarLeft?.width || 0), `${key} ${view} toolbar regions do not overlap`)
        .toBeLessThanOrEqual((item.toolbarRight?.x || 0) + 1);
      if (view !== 'flow') {
        expect(item.viewRoot?.width || 0, `${key} ${view} owner width`).toBeGreaterThanOrEqual((item.main?.width || 0) - 28);
      }
    }

    await selectView(page, 'settings');
    const settingsMetrics: Record<string, unknown> = {};
    for (const tabName of ['communication', 'tcp', 'station', 'cameras', 'ai'] as const) {
      await page.locator(`.settings-menu-item[data-tab="${tabName}"]`).click();
      await expect(page.locator(`.settings-panel[data-section="${tabName}"]`)).toHaveClass(/active/);
      const item = await collectLayoutMetrics(page, 'settings');
      settingsMetrics[tabName] = item;
      expect(item.actions.settingsSave, `${key} ${tabName} save reachable`).toBe(true);
      expect(item.document.overflow, `${key} ${tabName} document overflow`).toBeLessThanOrEqual(1);
      expect(item.settingsPanel?.width || 0, `${key} ${tabName} content uses available width`)
        .toBeGreaterThanOrEqual((item.settingsContent?.width || 0) - 2);
    }
    viewMetrics.settingsHighDensity = settingsMetrics;
    matrix[key] = viewMetrics;
  }

  await writeEvidenceJson(theme, 'desktop-viewport-matrix', matrix);
});

test(`Quiet Precision ${phase} login evidence`, async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.goto('/login.html');
  await expect(page.locator('.login-box')).toBeVisible();
  const directory = path.join(evidenceRoot, 'login');
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: path.join(directory, '1366-login.png'), fullPage: true });
});
