import { mkdir } from 'node:fs/promises';
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
        workspaceV2Enabled: false,
        featureFlags: Object.freeze({
          'Studio2.PropertyPanel': true,
          'Studio2.PreviewPanel': true,
          'Studio2.GlobalVariables': true,
          'Studio2.Settings': false,
          'Studio2.ProjectPage': true,
          'Studio2.Inspection': true,
          'Studio2.ResultsReview': true,
          'Studio2.AiPanel': false,
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
}

test(`Quiet Precision ${phase} login evidence`, async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.goto('/login.html');
  await expect(page.locator('.login-box')).toBeVisible();
  const directory = path.join(evidenceRoot, 'login');
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: path.join(directory, '1366-login.png'), fullPage: true });
});
