import { expect, test, type Page, type Route } from '@playwright/test';
import { fulfillF02Json, installF02BrowserStartup } from './f02-browser-fixture';

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

async function boot(page: Page): Promise<{ requests: Array<{ path: string; method: string; body: unknown }>; stops: () => number }> {
  await installF02BrowserStartup(page, { 'Studio2.InspectionRun': true });
  const requests: Array<{ path: string; method: string; body: unknown }> = [];
  let busy = false;
  let stopCount = 0;
  await page.route('**/health', route => fulfill(route, 200, { status: 'Healthy' }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const body = request.postDataJSON?.() ?? null;
    requests.push({ path: url.pathname, method: request.method(), body });
    if (url.pathname === '/api/auth/setup-status') return fulfill(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
    if (url.pathname === '/api/auth/me') return fulfill(route, 200, { userId: 'fixture-user', username: 'fixture-engineer', role: 'Engineer' });
    if (url.pathname === '/api/projects') return fulfill(route, 200, [project]);
    if (url.pathname === `/api/projects/${projectId}`) return fulfill(route, 200, project);
    if (url.pathname === '/api/cameras/bindings') return fulfill(route, 200, [{ id: 'camera-a', displayName: '顶视相机', isEnabled: true, connectionStatus: 'Connected' }]);
    if (url.pathname === '/api/inspection/admission') return fulfill(route, 200, {
      allowed: true, code: null, message: 'admitted', projectId, clientSnapshotId: snapshotId,
      projectPersistenceRevision: 12, canonicalFlowHash: 'sha256:flow', decisionConfigurationHash: 'sha256:decision', violations: []
    });
    if (url.pathname === '/api/inspection/realtime/start') {
      busy = true;
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
      executionSource: busy ? 'PersistedProject' : null, sessionType: busy ? 'ContinuousInspection' : null
    });
    if (url.pathname === `/api/inspection/realtime/${projectId}/events`) {
      return route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        body: [
          `id: 1\nevent: stateChanged\ndata: ${JSON.stringify({ projectId, sessionId, oldState: 'Starting', newState: 'Running', errorMessage: null, timestamp: '2026-07-26T02:00:01Z', isSnapshot: false, startedAt: '2026-07-26T02:00:00Z', stoppedAt: null, sessionType: 'ContinuousInspection' })}\n\n`,
          `id: 2\nevent: resultProduced\ndata: ${JSON.stringify({ projectId, sessionId, resultId, status: 'OK', executionOutcome: 'Succeeded', decisionOutcome: 'OK', defectCount: 0, processingTimeMs: 18, errorMessage: null, timestamp: '2026-07-26T02:00:02Z' })}\n\n`
        ].join('')
      });
    }
    if (url.pathname === '/api/inspection/realtime/stop') {
      stopCount += 1;
      busy = false;
      return fulfill(route, 200, { message: 'stopped', projectId });
    }
    if (url.pathname.startsWith('/api/inspection/history')) return fulfill(route, 200, { items: [], totalCount: 0, pageIndex: 0, pageSize: 20 });
    return fulfill(route, 404, { error: 'NotFound' });
  });
  return { requests, stops: () => stopCount };
}

test('continuous inspection project selection, persisted start, latest result and stop-on-leave journey', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  const audit = await boot(page);
  await page.goto('/studio/index.html#/inspection');

  await expect(page.getByTestId('inspection-projects-page')).toBeVisible();
  await page.getByTestId('inspection-project-open').click();
  await expect(page.getByTestId('inspection-run-page')).toBeVisible();
  await page.getByTestId('inspection-start').click();

  await expect(page.getByTestId('inspection-latest-result')).toContainText('OK');
  if (process.env.CV_F05_SCREENSHOT) {
    await page.screenshot({ path: process.env.CV_F05_SCREENSHOT, animations: 'disabled', fullPage: false });
  }
  const admissionRequest = audit.requests.find(entry => entry.path === '/api/inspection/admission');
  const startRequest = audit.requests.find(entry => entry.path === '/api/inspection/realtime/start');
  expect(admissionRequest?.body).toMatchObject({ projectId, expectedPersistenceRevision: 12 });
  expect(startRequest?.body).toMatchObject({ projectId, runMode: 'canonical-project', cameraId: 'camera-a' });
  expect(JSON.stringify(startRequest?.body)).not.toContain('FlowData');

  await page.getByRole('link', { name: '查看检测结果' }).click();
  await expect(page).toHaveURL(/#\/results$/);
  expect(audit.stops()).toBe(1);
});
