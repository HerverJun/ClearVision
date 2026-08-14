import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { isAbsolute, relative, resolve } from 'node:path';
import { expect, test, type Page, type Route } from '@playwright/test';
import {
  createF02RuntimeErrorAudit,
  fulfillF02Json,
  installF02BrowserStartup,
  installF02VisualPreferences,
  type F02RuntimeErrorAudit
} from './f02-browser-fixture';
import {
  captureR2FinalMatrixGroup,
  prepareR2FinalMatrixPage,
  r2Viewport,
  type R2FinalVariant
} from './r2-visual/r2-final-matrix-evidence';

const projectId = '11111111-1111-4111-8111-111111111111';
const snapshotId = '22222222-2222-4222-8222-222222222222';
const sessionId = '33333333-3333-4333-8333-333333333333';
const resultId = '44444444-4444-4444-8444-444444444444';

const project = {
  id: projectId,
  name: '在线瓶盖检测',
  description: '一号线连续检测',
  version: '1.0.0',
  persistenceRevision: 12,
  createdAt: '2026-07-26T00:00:00Z',
  modifiedAt: '2026-07-26T01:00:00Z',
  lastOpenedAt: '2026-07-26T01:00:00Z',
  flow: null,
  assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
};

async function fulfill(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, 'f05-inspection-run.v1');
}

interface InspectionRequestAuditEntry {
  readonly path: string;
  readonly method: string;
  readonly body: unknown;
}

interface InspectionEvidenceAudit {
  readonly requests: InspectionRequestAuditEntry[];
  readonly runtimeErrors: F02RuntimeErrorAudit;
  readonly expectedHttpStatuses: readonly number[];
  readonly stops: () => number;
}

interface InspectionEvidenceOptions {
  readonly theme: 'light' | 'dark';
  readonly density: 'compact' | 'comfortable';
  readonly interactions: readonly string[];
}

const pendingEvidence = new WeakMap<Page, string[]>();

function inspectionEvidenceRoot(): string | null {
  const directory = process.env.CV_F05_SCREENSHOT_DIR?.trim();
  if (!directory) return null;
  const repositoryRoot = resolve(process.cwd(), '..', '..', '..');
  const allowedRoot = resolve(repositoryRoot, '.tmp', 'studio-ui-next', 'f05');
  const outputRoot = isAbsolute(directory) ? resolve(directory) : resolve(repositoryRoot, directory);
  const relativeOutput = relative(allowedRoot, outputRoot);
  if (relativeOutput.startsWith('..') || isAbsolute(relativeOutput)) {
    throw new Error('CV_F05_SCREENSHOT_DIR must remain under .tmp/studio-ui-next/f05.');
  }
  return outputRoot;
}

function requiredEvidenceIdentity(): Readonly<{ sourceSha: string; contentHash: string }> {
  const sourceSha = process.env.CV_F05_SOURCE_SHA?.trim() ?? '';
  const contentHash = process.env.CV_F05_CONTENT_HASH?.trim() ?? '';
  if (!/^[0-9a-f]{40}$/i.test(sourceSha)) {
    throw new Error('CV_F05_SOURCE_SHA must contain the 40-character source Git SHA.');
  }
  if (!/^[0-9a-f]{64}$/i.test(contentHash)) {
    throw new Error('CV_F05_CONTENT_HASH must contain the 64-character dirty candidate content hash.');
  }
  return Object.freeze({ sourceSha: sourceSha.toLowerCase(), contentHash: contentHash.toLowerCase() });
}

async function captureScenario(
  page: Page,
  name: string,
  audit: InspectionEvidenceAudit,
  options: InspectionEvidenceOptions
): Promise<void> {
  const outputRoot = inspectionEvidenceRoot();
  if (!outputRoot) return;
  const identity = requiredEvidenceIdentity();
  const safeName = name.trim().toLowerCase().replace(/[^a-z0-9_.-]+/g, '-').replace(/^-+|-+$/g, '');
  if (!safeName) throw new Error('Inspection evidence scenario requires a safe name.');
  const screenshotPath = resolve(outputRoot, `${safeName}.png`);
  const metadataPath = resolve(outputRoot, `${safeName}.json`);
  await mkdir(outputRoot, { recursive: true });
  const projection = await page.evaluate(() => {
    const runConsole = document.querySelector<HTMLElement>('[data-testid="run-console"]');
    const visibleCapabilities = Array.from(document.querySelectorAll<HTMLElement>('[data-capability], [data-testid]'))
      .filter(element => {
        const style = getComputedStyle(element);
        const rect = element.getBoundingClientRect();
        return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
      })
      .slice(0, 24)
      .map(element => element.dataset.capability ?? element.dataset.testid ?? element.tagName.toLowerCase());
    return {
      viewport: { width: window.innerWidth, height: window.innerHeight },
      theme: document.documentElement.dataset.theme ?? null,
      density: document.documentElement.dataset.density ?? null,
      devicePixelRatio: window.devicePixelRatio,
      horizontalOverflow: Math.max(
        document.documentElement.scrollWidth - document.documentElement.clientWidth,
        document.body.scrollWidth - document.body.clientWidth
      ),
      activeElement: document.activeElement instanceof HTMLElement
        ? document.activeElement.dataset.testid ?? document.activeElement.tagName.toLowerCase()
        : null,
      visibleCapabilities,
      disabledControlCount: document.querySelectorAll(':disabled').length,
      runConsole: runConsole
        ? {
            clientWidth: runConsole.clientWidth,
            scrollWidth: runConsole.scrollWidth,
            clientHeight: runConsole.clientHeight,
            scrollHeight: runConsole.scrollHeight
          }
        : null
    };
  });
  if (projection.theme !== options.theme || projection.density !== options.density) {
    throw new Error(`Inspection visual preference projection drifted: ${JSON.stringify(projection)}.`);
  }
  if (projection.horizontalOverflow > 1 || (projection.runConsole && projection.runConsole.scrollWidth > projection.runConsole.clientWidth + 1)) {
    throw new Error(`Inspection scenario has horizontal overflow: ${JSON.stringify(projection)}.`);
  }
  if (audit.runtimeErrors.consoleErrors.length || audit.runtimeErrors.pageErrors.length) {
    throw new Error(`Inspection scenario emitted runtime errors: ${JSON.stringify(audit.runtimeErrors)}.`);
  }
  const unexpectedMethods = audit.requests.filter(request => !['GET', 'POST'].includes(request.method));
  if (unexpectedMethods.length) {
    throw new Error(`Inspection scenario emitted unexpected request methods: ${JSON.stringify(unexpectedMethods)}.`);
  }
  const screenshot = await page.screenshot({ animations: 'disabled', fullPage: false, type: 'png' });
  if (screenshot.byteLength < 10_000) throw new Error('Inspection evidence screenshot is unexpectedly small.');
  await writeFile(screenshotPath, screenshot);
  await writeFile(metadataPath, `${JSON.stringify({
    schemaVersion: 'f05-inspection-evidence.v1',
    capturedAtUtc: new Date().toISOString(),
    sourceState: 'DIRTY_CANDIDATE',
    sourceSha: identity.sourceSha,
    candidateContentHash: identity.contentHash,
    fixtureSource: 'f05-inspection-run.v1',
    scenario: name,
    url: page.url(),
    viewport: projection.viewport,
    theme: projection.theme,
    density: projection.density,
    dpr: {
      type: 'BROWSER_EMULATED_DPR',
      value: projection.devicePixelRatio,
      windowsDpi: 'NOT_PERFORMED'
    },
    dom: projection,
    interactions: options.interactions,
    requestMethods: audit.requests.map(request => ({ method: request.method, path: request.path })),
    expectedHttpStatuses: audit.expectedHttpStatuses,
    runtimeErrors: audit.runtimeErrors,
    screenshot: {
      fileName: `${safeName}.png`,
      sha256: createHash('sha256').update(screenshot).digest('hex'),
      bytes: screenshot.byteLength
    },
    cleanup: {
      status: 'PENDING_TEST_TEARDOWN',
      pageClosed: false,
      serverLifecycle: 'PLAYWRIGHT_GLOBAL_SETUP'
    }
  }, null, 2)}\n`, 'utf8');
  const paths = pendingEvidence.get(page) ?? [];
  paths.push(metadataPath);
  pendingEvidence.set(page, paths);
}

async function finalizeInspectionEvidence(page: Page, testPassed: boolean): Promise<void> {
  const paths = pendingEvidence.get(page) ?? [];
  if (paths.length === 0) return;
  await page.close();
  if (!page.isClosed()) throw new Error('Inspection evidence page did not close during cleanup.');
  for (const metadataPath of paths) {
    const metadata = JSON.parse(await readFile(metadataPath, 'utf8')) as Record<string, unknown>;
    metadata.cleanup = {
      status: testPassed ? 'PASS' : 'TEST_FAILED',
      pageClosed: true,
      routeHandlersReleasedWithPage: true,
      serverLifecycle: 'PLAYWRIGHT_GLOBAL_SETUP'
    };
    await writeFile(metadataPath, `${JSON.stringify(metadata, null, 2)}\n`, 'utf8');
  }
  pendingEvidence.delete(page);
}

test.afterEach(async ({ page }, testInfo) => {
  await finalizeInspectionEvidence(page, testInfo.status === testInfo.expectedStatus);
});

const inspectionVisualMatrix = Object.freeze(
  ([
    { width: 1920, height: 1080 },
    { width: 1366, height: 768 }
  ] as const).flatMap(viewport =>
    (['light', 'dark'] as const).flatMap(theme =>
      (['compact', 'comfortable'] as const).map(density => Object.freeze({ viewport, theme, density }))
    )
  )
);

interface BootOptions {
  readonly initialSessionType?: 'WorkspaceFormalRun' | 'ContinuousInspection';
  readonly admissionAllowed?: boolean;
  readonly density?: 'compact' | 'comfortable';
  readonly theme?: 'light' | 'dark';
  readonly projectName?: string;
  readonly projectListStatus?: number;
  readonly projectListFailureAfter?: number;
}

async function boot(
  page: Page,
  options: BootOptions = {}
): Promise<InspectionEvidenceAudit> {
  const expectedHttpStatuses = Object.freeze([
    ...(options.projectListStatus && options.projectListStatus !== 200 ? [options.projectListStatus] : []),
    ...(options.projectListFailureAfter != null ? [503] : [])
  ]);
  const runtimeErrors = createF02RuntimeErrorAudit(page, expectedHttpStatuses);
  await installF02VisualPreferences(page, options.theme ?? 'light', options.density ?? 'compact');
  await installF02BrowserStartup(page, { 'Studio2.InspectionRun': true });
  const requests: Array<{ path: string; method: string; body: unknown }> = [];
  let busy = options.initialSessionType != null;
  let activeSessionType = options.initialSessionType ?? null;
  let stopCount = 0;
  let projectListCount = 0;
  const projectFixture = { ...project, name: options.projectName ?? project.name };
  await page.route('**/health', route => fulfill(route, 200, { status: 'Healthy' }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const body = request.postDataJSON?.() ?? null;
    requests.push({ path: url.pathname, method: request.method(), body });
    if (url.pathname === '/api/auth/setup-status') return fulfill(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
    if (url.pathname === '/api/auth/me') return fulfill(route, 200, { userId: 'fixture-user', username: 'fixture-engineer', role: 'Engineer' });
    if (url.pathname === '/api/projects' || url.pathname === '/api/projects/search') {
      projectListCount += 1;
      const status = options.projectListStatus ?? (
        options.projectListFailureAfter != null && projectListCount > options.projectListFailureAfter ? 503 : 200
      );
      if (status !== 200) return fulfill(route, status, { error: status === 403 ? 'Forbidden' : 'Unavailable' });
      const keyword = url.searchParams.get('keyword')?.trim().toLowerCase() ?? '';
      const matches = !keyword || projectFixture.name.toLowerCase().includes(keyword) ||
        projectFixture.description.toLowerCase().includes(keyword);
      return fulfill(route, 200, matches ? [projectFixture] : []);
    }
    if (url.pathname === `/api/projects/${projectId}`) return fulfill(route, 200, projectFixture);
    if (url.pathname === '/api/cameras/bindings') return fulfill(route, 200, [{ id: 'camera-a', displayName: '顶视相机', isEnabled: true, connectionStatus: 'Connected' }]);
    if (url.pathname === '/api/inspection/admission') {
      const allowed = options.admissionAllowed !== false;
      return fulfill(route, 200, {
        allowed,
        code: allowed ? null : 'RUN_ADMISSION_BLOCKED',
        message: allowed
          ? 'admitted'
          : '工程存在尚未完成的模型路径参数，请返回属性面板完成配置并重新保存工程后再次检查准入。',
        projectId,
        clientSnapshotId: snapshotId,
        projectPersistenceRevision: 12,
        canonicalFlowHash: 'sha256:flow',
        decisionConfigurationHash: 'sha256:decision',
        violations: allowed ? [] : [{
          operatorId: 'operator-long-cn',
          operatorName: '瓶盖表面缺陷深度学习终检算子（高分辨率生产配方）',
          operatorType: 'DeepLearningInspection',
          reason: '模型文件路径尚未配置，正式运行与连续检测均不可绕过该必填参数。',
          parameterName: '模型文件路径与版本标识',
          code: 'PENDING_PARAMETER'
        }]
      });
    }
    if (url.pathname === '/api/inspection/realtime/start') {
      busy = true;
      activeSessionType = 'ContinuousInspection';
      return fulfill(route, 200, {
        projectId, clientSnapshotId: snapshotId, persistenceRevision: 12,
        canonicalFlowHash: 'sha256:flow', decisionConfigurationHash: 'sha256:decision',
        runMode: 'canonical-project', cameraId: 'camera-a', sessionId, sessionType: 'ContinuousInspection'
      });
    }
    if (url.pathname === `/api/inspection/realtime/${projectId}/state`) return fulfill(route, 200, {
      projectId, status: busy ? 'Running' : 'Idle', isBusy: busy, sessionId: busy ? sessionId : null,
      startedAt: busy ? '2026-07-26T02:00:00Z' : null, stoppedAt: null,
      clientSnapshotId: busy ? snapshotId : null, persistenceRevision: busy ? 12 : null,
      canonicalFlowHash: busy ? 'sha256:flow' : null, decisionConfigurationHash: busy ? 'sha256:decision' : null,
      executionSource: busy ? 'PersistedProject' : null, sessionType: busy ? activeSessionType : null
    });
    if (url.pathname === `/api/inspection/realtime/${projectId}/events`) {
      return route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        body: [
          `id: 1\nevent: stateChanged\ndata: ${JSON.stringify({ projectId, sessionId, oldState: 'Starting', newState: 'Running', errorMessage: null, timestamp: '2026-07-26T02:00:01Z', isSnapshot: false, startedAt: '2026-07-26T02:00:00Z', stoppedAt: null, sessionType: 'ContinuousInspection' })}\n\n`,
          `id: 2\nevent: resultProduced\ndata: ${JSON.stringify({ projectId, sessionId, resultId, status: 'OK', executionOutcome: 'Succeeded', decisionOutcome: 'Ok', decisionSource: 'FinalDecision', reasonCode: null, hasJudgmentSignal: true, defectCount: 0, processingTimeMs: 18, errorMessage: null, outputData: { score: 0.98 }, analysisData: { threshold: 0.9 }, timestamp: '2026-07-26T02:00:02Z' })}\n\n`
        ].join('')
      });
    }
    if (url.pathname === '/api/inspection/realtime/stop') {
      stopCount += 1;
      busy = false;
      activeSessionType = null;
      return fulfill(route, 200, { message: 'stopped', projectId });
    }
    if (url.pathname.startsWith('/api/inspection/history')) return fulfill(route, 200, { items: [], totalCount: 0, pageIndex: 0, pageSize: 20 });
    return fulfill(route, 404, { error: 'NotFound' });
  });
  return { requests, runtimeErrors, expectedHttpStatuses, stops: () => stopCount };
}

test('continuous inspection persists across route leave and restores from authority on return', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  const audit = await boot(page);
  await page.goto('/studio/index.html#/inspection');

  await expect(page.getByTestId('inspection-projects-page')).toBeVisible();
  await captureScenario(page, 'inspection-projects-1920x1080-light-compact', audit, {
    theme: 'light', density: 'compact', interactions: ['loaded project picker']
  });
  await page.getByTestId('inspection-project-search').fill('瓶盖');
  await page.getByRole('button', { name: '搜索', exact: true }).click();
  await expect(page.getByText('1 个匹配工程')).toBeVisible();
  await page.getByRole('button', { name: '清除工程搜索' }).click();
  await expect(page.getByText('1 个工程可供选择')).toBeVisible();
  expect(audit.requests.some(entry => entry.path === '/api/projects/search')).toBe(true);
  await page.getByTestId('inspection-project-open').click();
  await expect(page.getByTestId('inspection-run-page')).toBeVisible();
  await page.getByTestId('inspection-start').click();

  await expect(page.getByTestId('inspection-latest-result')).toContainText('OK');
  await captureScenario(page, 'continuous-running-1920x1080-light-compact', audit, {
    theme: 'light', density: 'compact', interactions: ['searched project', 'cleared search', 'opened project', 'started continuous inspection', 'observed latest result']
  });
  if (process.env.CV_F05_SCREENSHOT) {
    await page.screenshot({ path: process.env.CV_F05_SCREENSHOT, animations: 'disabled', fullPage: false });
  }
  const admissionRequest = audit.requests.find(entry => entry.path === '/api/inspection/admission');
  const startRequest = audit.requests.find(entry => entry.path === '/api/inspection/realtime/start');
  expect(admissionRequest?.body).toMatchObject({ projectId, expectedPersistenceRevision: 12 });
  expect(startRequest?.body).toMatchObject({ projectId, runMode: 'canonical-project', cameraId: 'camera-a' });
  expect(JSON.stringify(startRequest?.body)).not.toContain('FlowData');

  await page.getByTestId('inspection-run-result-link').click();
  await expect(page).toHaveURL(new RegExp(
    `#/results\\?source=local&projectId=${projectId}&resultId=${resultId}&returnTo=`
  ));
  expect(audit.stops()).toBe(0);

  await page.goto(`/studio/index.html#/projects/${projectId}/inspection`);
  await expect(page.getByTestId('inspection-run-page')).toBeVisible();
  await expect(page.getByTestId('run-console')).toContainText(/连续检测中|实时恢复中/);
  expect(audit.requests.filter(entry => entry.path === '/api/inspection/realtime/start')).toHaveLength(1);
});

test('inspection project picker keeps cached rows visible when refresh becomes stale', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  const audit = await boot(page, { projectListFailureAfter: 1 });
  await page.goto('/studio/index.html#/inspection');

  await expect(page.getByTestId('inspection-project-open')).toBeVisible();
  await page.getByRole('button', { name: '刷新可检测工程' }).click();
  await expect(page.getByText('列表可能已过期')).toBeVisible();
  await expect(page.getByTestId('inspection-project-open')).toBeVisible();
  await captureScenario(page, 'inspection-projects-stale-1366x768-light-compact', audit, {
    theme: 'light', density: 'compact', interactions: ['loaded cached projects', 'refreshed project list', 'observed stale rows retained']
  });
});

test('inspection project picker presents forbidden access instead of an empty list', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  const audit = await boot(page, { projectListStatus: 403 });
  await page.goto('/studio/index.html#/inspection');

  await expect(page.getByText('无权读取检测工程')).toBeVisible();
  await expect(page.getByText('暂无可检测工程')).toHaveCount(0);
  await captureScenario(page, 'inspection-projects-forbidden-1366x768-light-compact', audit, {
    theme: 'light', density: 'compact', interactions: ['opened project picker', 'observed forbidden state without empty fallback']
  });
});

test('formal run occupancy is read-only on the continuous route and mounts no continuous stream', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  const audit = await boot(page, { initialSessionType: 'WorkspaceFormalRun', density: 'compact' });
  await page.goto(`/studio/index.html#/projects/${projectId}/inspection`);

  const console = page.getByTestId('run-console');
  await expect(console).toBeVisible();
  await expect(console).toContainText('其他运行占用');
  await expect(console).toContainText(sessionId);
  await expect(page.getByTestId('inspection-start')).toBeDisabled();
  await captureScenario(page, 'formal-occupied-1920x1080-light-compact', audit, {
    theme: 'light', density: 'compact', interactions: ['opened continuous route', 'observed formal occupancy', 'verified start disabled']
  });
  expect(audit.requests.filter(entry => entry.path === '/api/inspection/admission')).toHaveLength(0);
  expect(audit.requests.filter(entry => entry.path.endsWith('/events'))).toHaveLength(0);
  expect(audit.requests.filter(entry => entry.path === '/api/inspection/realtime/start')).toHaveLength(0);
});

test('blocked admission keeps long Chinese actionable without overflow at 125 percent layout pressure', async ({ page }) => {
  await page.setViewportSize({ width: 1536, height: 864 });
  const audit = await boot(page, {
    admissionAllowed: false,
    density: 'comfortable',
    projectName: '一号产线瓶盖外观与密封完整性连续检测工程（高分辨率生产配方）'
  });
  await page.goto(`/studio/index.html#/projects/${projectId}/inspection`);

  const console = page.getByTestId('run-console');
  await expect(console).toBeVisible();
  await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable');
  await expect(console).toContainText('RUN_ADMISSION_BLOCKED');
  await expect(console).toContainText('PENDING_PARAMETER');
  await expect(console).toContainText('模型文件路径与版本标识');
  await expect(page.getByTestId('inspection-start')).toBeDisabled();
  const horizontalOverflow = await page.evaluate(() => Math.max(
    document.documentElement.scrollWidth - document.documentElement.clientWidth,
    document.body.scrollWidth - document.body.clientWidth
  ));
  expect(horizontalOverflow).toBeLessThanOrEqual(1);
  await captureScenario(page, 'admission-blocked-long-zh-1536x864-light-comfortable', audit, {
    theme: 'light', density: 'comfortable', interactions: ['opened continuous route', 'observed blocked admission', 'verified long Chinese diagnostics and disabled start']
  });
});

for (const variant of ['B0', 'B2', 'EXCEPTION'] as const satisfies readonly R2FinalVariant[]) {
  test(`@r2-final S09 ${variant} projects the F05 Inspection authority`, async ({ page }) => {
    await page.setViewportSize(r2Viewport(variant));
    const runtime = await prepareR2FinalMatrixPage(page);
    const exception = variant === 'EXCEPTION';
    const audit = await boot(page, { admissionAllowed: !exception, density: 'compact' });
    await page.goto(`/studio/index.html#/projects/${projectId}/inspection`);

    const console = page.getByTestId('run-console');
    await expect(console).toBeVisible();
    if (exception) {
      await expect(console).toContainText('RUN_ADMISSION_BLOCKED');
      await expect(console).toContainText('PENDING_PARAMETER');
      await expect(page.getByTestId('inspection-start')).toBeDisabled();
      expect(audit.requests.filter(entry =>
        entry.path === '/api/inspection/realtime/start' || entry.path === '/api/inspection/realtime/stop'
      )).toHaveLength(0);
    } else {
      await expect(page.getByTestId('inspection-start')).toBeEnabled();
      await page.getByTestId('inspection-start').click();
      await expect(page.getByTestId('inspection-latest-result')).toContainText('OK');
      expect(audit.requests.filter(entry => entry.method === 'POST' && entry.path === '/api/inspection/realtime/start')).toHaveLength(1);
    }
    expect(audit.runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
    await page.getByTestId('inspection-start').scrollIntoViewIfNeeded();
    await captureR2FinalMatrixGroup(page, {
      scene: 'S09', variant, route: `#/projects/${projectId}/inspection`,
      state: exception ? 'blocked' : 'recent-result',
      role: 'Engineer', flags: { 'Studio2.InspectionRun': true }, owner: 'F05-inspection-run',
      writes: exception ? 0 : 1,
      allowedWrites: exception
        ? ['POST /api/inspection/admission']
        : ['POST /api/inspection/admission', 'POST /api/inspection/realtime/start'],
      runtime,
      requiredCriticalActions: [exception
        ? '[data-testid="run-console-reconcile"]'
        : '[data-testid="inspection-run-result-link"]']
    });
  });
}

for (const scenario of inspectionVisualMatrix) {
  test(`captures inspection B0/B2 ${scenario.viewport.width}x${scenario.viewport.height} ${scenario.theme} ${scenario.density} evidence`, async ({ page }) => {
    test.skip(!inspectionEvidenceRoot(), 'CV_F05_SCREENSHOT_DIR is required for inspection evidence capture.');
    await page.setViewportSize(scenario.viewport);
    const audit = await boot(page, { theme: scenario.theme, density: scenario.density });
    await page.goto('/studio/index.html#/inspection');
    await expect(page.getByTestId('inspection-projects-page')).toBeVisible();
    await captureScenario(
      page,
      `inspection-projects-${scenario.viewport.width}x${scenario.viewport.height}-${scenario.theme}-${scenario.density}`,
      audit,
      { theme: scenario.theme, density: scenario.density, interactions: ['loaded project picker'] }
    );
    await page.getByTestId('inspection-project-open').click();
    await expect(page.getByTestId('inspection-run-page')).toBeVisible();
    await page.getByTestId('inspection-start').click();
    await expect(page.getByTestId('inspection-latest-result')).toContainText('OK');
    await captureScenario(
      page,
      `continuous-running-${scenario.viewport.width}x${scenario.viewport.height}-${scenario.theme}-${scenario.density}`,
      audit,
      {
        theme: scenario.theme,
        density: scenario.density,
        interactions: ['opened project', 'started continuous inspection', 'observed latest result']
      }
    );
  });
}
