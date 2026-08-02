import { expect, test, type Page, type Route } from '@playwright/test';
import {
  auditF02Request,
  captureF02VisualEvidence,
  createF02RuntimeErrorAudit,
  expectGetOnly,
  f02BrowserFixture,
  fulfillF02Json,
  hasF02VisualEvidenceTarget,
  installF02BrowserStartup,
  installF02VisualPreferences,
  type F02MethodAuditEntry
} from './f02-browser-fixture';
import {
  captureF04VisualEvidence,
  hasF04VisualEvidenceTarget
} from './f04-browser-evidence';

const stationFixture = Object.freeze({
  schemaVersion: 'f02-stations-read.v1',
  endpoint: Object.freeze([
    'GET /api/stations',
    'GET /api/stations/events',
    'GET /api/stations/summary',
    'GET /api/stations/statistics',
    'GET /api/stations/{stationId}',
    'GET /api/stations/{stationId}/results',
    'GET /api/stations/{stationId}/health',
    'GET /api/stations/{stationId}/logs',
    'GET /api/stations/{stationId}/commands',
    'GET /api/stations/{stationId}/commands/by-client-request/{clientRequestId}',
    'GET /api/stations/audit',
    'GET /api/station-packages',
    'POST /api/stations/{stationId}/commands',
    'PATCH /api/stations/{stationId}/identity',
    'POST /api/stations/{stationId}/deploy-package'
  ]),
  sourceSha: f02BrowserFixture.sourceSha,
  dataSource: f02BrowserFixture.dataSource
});

function outcomeStatistics(): Record<string, unknown> {
  return {
    totalAttemptCount: 9,
    executionSucceededCount: 5,
    validDecisionCount: 2,
    okCount: 1,
    ngCount: 1,
    undeterminedCount: 1,
    notApplicableCount: 1,
    invalidCount: 1,
    failedCount: 1,
    cancelledCount: 1,
    timedOutCount: 1,
    skippedCount: 1
  };
}

function station(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    stationId: 'station-a',
    stationName: '一号检测站',
    lineName: '一号线',
    machineName: 'CV-STATION-A',
    clientVersion: '2.1.0',
    areaName: 'A 区',
    workcellName: '单元 1',
    inspectionNodeName: '瓶盖检测',
    cameraAlias: '顶视相机',
    stationRole: 'Inspection',
    owner: '生产一组',
    isEnabled: true,
    offlineReason: null,
    remark: '只读 fixture',
    onlineState: 1,
    state: 'Running',
    runtimeState: 2,
    isOnline: true,
    startedAtUtc: '2026-07-15T01:00:00Z',
    lastSeenAtUtc: '2026-07-15T02:00:00Z',
    packageId: 'pkg-a',
    packageName: '瓶盖检测包',
    packageVersion: '1.0.0',
    packageSha256: `sha256:${'a'.repeat(64)}`,
    sourceProjectId: '11111111-2222-3333-4444-555555555555',
    sourceProjectRevision: 12,
    packageFlowHash: 'sha256:package',
    executionFlowHash: 'sha256:execution',
    flowHash: 'sha256:execution',
    executionSnapshotId: '11111111-1111-4111-8111-111111111111',
    projectRevision: 12,
    decisionConfigurationHash: 'sha256:decision',
    executionRunMode: 'Production',
    currentRunId: 'run-a',
    sessionOutcomeStatistics: outcomeStatistics(),
    sessionOutcomeStatisticsIsLegacyProjection: false,
    lastExecutionOutcome: 'Succeeded',
    lastDecisionOutcome: 'Ng',
    lastDiagnosticCode: 'WIRE_SWAP',
    lastDiagnosticMessage: '线序错误',
    lastResultAtUtc: '2026-07-15T01:59:30Z',
    averageExecutionTimeMs: 24.5,
    spoolPendingCount: 2,
    spoolBytes: 4096,
    cpuUsagePercent: 32.5,
    workingSetMb: 256,
    diskFreeMb: 20480,
    diskTotalMb: 51200,
    cameraStatusSummary: 'Ready',
    plcStatusSummary: 'Connected',
    currentPackageHealth: 'Healthy',
    ...overrides
  };
}

function summary(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    totalStations: 1,
    onlineStations: 1,
    offlineStations: 0,
    runningStations: 1,
    faultedStations: 0,
    alertCount: 0,
    warningStations: 0,
    criticalStations: 0,
    outcomeStatistics: outcomeStatistics(),
    averageExecutionTimeMs: 24.5,
    offlineThresholdSeconds: 15,
    updatedAtUtc: '2026-07-15T02:00:00Z',
    ...overrides
  };
}

function result(): Record<string, unknown> {
  return {
    schemaVersion: 2,
    stationId: 'station-a',
    lineName: '一号线',
    sequenceId: 9,
    messageId: 'message-9',
    runId: 'run-9',
    packageId: 'pkg-a',
    packageName: '瓶盖检测包',
    packageVersion: '1.0.0',
    outcome: 1,
    inspectionStatus: 'NG',
    executionOutcome: 'Succeeded',
    decisionOutcome: 'Ng',
    executionTimeMs: 25,
    diagnosticCode: 'WIRE_SWAP',
    diagnosticMessage: '线序错误',
    completedAtUtc: '2026-07-15T02:00:00Z'
  };
}

function health(): Record<string, unknown> {
  return {
    schemaVersion: 2,
    stationId: 'station-a',
    sequenceId: 10,
    messageId: 'health-10',
    runtimeState: 2,
    processUptimeSeconds: 3600,
    cpuUsagePercent: 32.5,
    workingSetMb: 256,
    privateMemoryMb: 220,
    diskFreeMb: 20480,
    diskTotalMb: 51200,
    spoolPendingCount: 2,
    spoolBytes: 4096,
    cameraStatusSummary: 'Ready',
    plcStatusSummary: 'Connected',
    currentPackageId: 'pkg-a',
    currentPackageHealth: 'Healthy',
    lastErrorCode: null,
    lastErrorMessage: null,
    createdAtUtc: '2026-07-15T02:00:00Z'
  };
}

function command(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    schemaVersion: 2, commandId: 'command-a', stationId: 'station-a', commandType: 'Ping', payloadJson: '{}',
    createdAtUtc: new Date().toISOString(), expiresAtUtc: '2026-07-26T03:00:00Z', issuedBy: 'fixture-admin',
    correlationId: 'correlation-a', clientRequestId: 'request-a', status: 'Created', progressPercent: 0, deliveredAtUtc: null,
    acceptedAtUtc: null, startedAtUtc: null, completedAtUtc: null, resultMessage: null, errorCode: null,
    ...overrides
  };
}

const stationLog = {
  schemaVersion: 2, stationId: 'station-a', sequenceId: 11, messageId: 'log-11', timestampUtc: '2026-07-15T02:00:00Z',
  level: 'WARN', source: 'RuntimeHost', eventId: 'runtime-warning', messageTemplate: null,
  renderedMessage: '运行包健康状态降级', exceptionType: null, exceptionMessage: null,
  correlationId: null, runId: 'run-a', packageId: 'pkg-a', createdAtUtc: '2026-07-15T02:00:01Z'
};

const stationPackage = {
  schemaVersion: 2, packageId: 'pkg-a', packageName: '瓶盖检测包', packageVersion: '1.0.0', packageKind: 'Production',
  flowHash: 'sha256:package', sourceProjectId: '11111111-2222-3333-4444-555555555555', sourceProjectRevision: 12,
  decisionConfigurationHash: 'sha256:decision', createdBy: 'fixture-admin', minStationVersion: '2.0.0', requiredOperators: ['Threshold'],
  sizeBytes: 4096, sha256: 'a'.repeat(64), createdAtUtc: '2026-07-15T01:00:00Z'
};

function deploymentPayload(packageRecord = stationPackage): Record<string, unknown> {
  return {
    packageId: packageRecord.packageId,
    packageName: packageRecord.packageName,
    packageVersion: packageRecord.packageVersion,
    packageKind: packageRecord.packageKind,
    sha256: packageRecord.sha256,
    flowHash: packageRecord.flowHash,
    sourceProjectId: packageRecord.sourceProjectId,
    sourceProjectRevision: packageRecord.sourceProjectRevision,
    decisionConfigurationHash: packageRecord.decisionConfigurationHash,
    downloadUrl: `/api/station-packages/${encodeURIComponent(packageRecord.packageId)}/download`
  };
}

async function fulfill(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, stationFixture.schemaVersion);
}

async function bootStations(
  page: Page,
  listPayload: unknown = [station()],
  summaryPayload: unknown = summary(),
  role: 'Admin' | 'Engineer' = 'Engineer',
  initialCommands: Record<string, unknown>[] = [command({
    status: 'Succeeded', progressPercent: 100, completedAtUtc: '2026-07-15T02:01:00Z'
  })]
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  const commands = [...initialCommands];
  let identity = station();
  await installF02BrowserStartup(page, { 'Studio2.StationsRead': true });
  await page.route('**/health', route => fulfill(route, 200, { status: 'Healthy', port: 5177 }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    if (url.pathname === '/api/auth/setup-status') {
      await fulfill(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
      return;
    }
    if (url.pathname === '/api/auth/me') {
      await fulfill(route, 200, { userId: 'fixture-user', username: `fixture-${role.toLowerCase()}`, role });
      return;
    }
    if (url.pathname === '/api/stations') {
      await fulfill(route, 200, listPayload);
      return;
    }
    if (url.pathname === '/api/stations/events') {
      const stations = Array.isArray(listPayload) ? listPayload : [];
      const body = [
        'event: initialState',
        `data: ${JSON.stringify({ eventSequenceId: 12, summary: summaryPayload, stations, recentResults: [] })}`,
        '',
        ':keepalive',
        '',
        ''
      ].join('\n');
      await route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        headers: { 'Cache-Control': 'no-cache', 'X-F02-Fixture': stationFixture.schemaVersion },
        body
      });
      return;
    }
    if (url.pathname === '/api/stations/summary') {
      await fulfill(route, 200, summaryPayload);
      return;
    }
    if (url.pathname === '/api/stations/statistics') {
      await fulfill(route, 200, {
        fromUtc: null,
        toUtc: null,
        outcomeStatistics: outcomeStatistics(),
        averageExecutionTimeMs: 24.5,
        byStation: [],
        byDiagnosticCode: [],
        hourlyTrend: []
      });
      return;
    }
    if (url.pathname === '/api/stations/station-a/results') {
      await fulfill(route, 200, [result()]);
      return;
    }
    if (url.pathname === '/api/stations/station-a/health') {
      await fulfill(route, 200, [health()]);
      return;
    }
    if (url.pathname === '/api/stations/station-a') {
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? identity : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/stations/station-a/logs') {
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? [stationLog] : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/stations/station-a/commands') {
      if (request.method() === 'POST') {
        const body = request.postDataJSON();
        const created = command({
          commandId: `command-${commands.length + 1}`, commandType: body.commandType,
          clientRequestId: body.clientRequestId, payloadJson: body.payloadJson
        });
        commands.unshift(created);
        await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? created : { error: 'StationAdminRequired' });
      } else {
        await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? commands : { error: 'StationAdminRequired' });
      }
      return;
    }
    if (url.pathname.startsWith('/api/stations/station-a/commands/by-client-request/')) {
      const requestId = decodeURIComponent(url.pathname.split('/').at(-1) ?? '');
      const found = commands.find(item =>
        item.clientRequestId === requestId && item.commandType === url.searchParams.get('commandType')
      );
      await fulfill(route, found ? 200 : 404, found ?? { error: 'StationCommandNotFound' });
      return;
    }
    if (url.pathname === '/api/stations/audit') {
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? [{
        auditId: 'audit-a', userName: 'fixture-admin', action: 'StationCommandCreated', targetStationId: 'station-a',
        commandId: 'command-a', payloadSummary: 'Ping', createdAtUtc: '2026-07-15T02:00:00Z', result: 'Created', clientIp: '127.0.0.1'
      }] : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/station-packages') {
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? [stationPackage] : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/stations/station-a/identity') {
      identity = { ...identity, ...request.postDataJSON() };
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? identity : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/stations/station-a/deploy-package') {
      const body = request.postDataJSON();
      const created = command({
        commandId: `deploy-${commands.length + 1}`, commandType: 'DeployPackage', clientRequestId: body.clientRequestId,
        payloadJson: JSON.stringify(deploymentPayload())
      });
      commands.unshift(created);
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? created : { error: 'StationAdminRequired' });
      return;
    }
    await fulfill(route, 404, { error: 'NotFound' });
  });
  return audit;
}

test('Station list uses URL filters, preserves nine outcomes and stays GET-only', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF02RuntimeErrorAudit(page);
  const audit = await bootStations(page);
  await page.goto('/studio/index.html#/stations?q=一号&online=Online&range=week&outcome=Ng&diagnosticCode=WIRE_SWAP');

  await expect(page.locator('[data-capability="stations-read"]')).toBeVisible();
  await expect(page.getByRole('cell', { name: /一号检测站/ })).toBeVisible();
  const outcomeCounters = page.locator('.stations-page__outcomes');
  await expect(outcomeCounters.getByText('未判定', { exact: true })).toBeVisible();
  await expect(outcomeCounters.getByText('不适用', { exact: true })).toBeVisible();
  await expect(outcomeCounters.getByText('判定无效', { exact: true })).toBeVisible();
  await expect(outcomeCounters.getByText('执行失败', { exact: true })).toBeVisible();
  await expect.poll(() => audit.some(entry => entry.path.includes(
    '/api/stations/statistics?range=week&status=Ng&diagnosticCode=WIRE_SWAP'
  ))).toBe(true);
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'stations', viewport, runtimeErrors, requestAudit: audit
    });
  }
  expect(expectGetOnly(audit)).toBe(true);
});

test('Engineer Station journey remains read-only and never mounts the Admin control domain', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF02RuntimeErrorAudit(page);
  const audit = await bootStations(page);
  await page.goto('/studio/index.html#/stations/station-a');

  await expect(page.locator('[data-capability="stations-read-detail"]')).toBeVisible();
  await expect(page.locator('[data-capability="station-admin-control"]')).toHaveCount(0);
  await expect(page.getByText('判定 NG')).toBeVisible();
  await expect(page.getByText('进程运行时长')).toBeVisible();
  await expect(page.getByText('工作站详情读取失败')).toHaveCount(0);
  await expect(page.getByText('TCP', { exact: true })).toBeVisible();
  await expect(page.getByText('未上报/不可确认', { exact: true })).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'station-detail-abnormal', viewport, runtimeErrors, requestAudit: audit
    });
  }
  expect(expectGetOnly(audit)).toBe(true);
  expect(audit.some(entry => entry.path === '/api/stations/events')).toBe(true);
  expect(audit.some(entry => /commands|logs|audit|packages|download/.test(entry.path))).toBe(false);
});

test('Admin Station journey mounts controls and creates command, identity and package operations', async ({ page, context }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  const runtimeErrors = createF02RuntimeErrorAudit(page);
  const commandAudit = await bootStations(page, [station()], summary(), 'Admin');
  await page.goto('/studio/index.html#/stations/station-a');

  const admin = page.locator('[data-capability="station-admin-control"]');
  await expect(admin).toBeVisible();
  await expect(admin.getByText('运行包健康状态降级')).toBeVisible();
  await admin.getByTestId('station-issue-command').click();
  await expect(admin.getByText('命令已创建；执行结果尚未确认。')).toBeVisible();

  const identityPage = await context.newPage();
  const identityErrors = createF02RuntimeErrorAudit(identityPage);
  const identityAudit = await bootStations(identityPage, [station()], summary(), 'Admin');
  await identityPage.goto('/studio/index.html#/stations/station-a');
  const identityAdmin = identityPage.locator('[data-capability="station-admin-control"]');
  await identityAdmin.getByLabel('工作站名称').fill('一号检测站（修订）');
  await identityAdmin.getByTestId('station-save-identity').click();
  await expect(identityAdmin.getByText('工作站身份已由后端修订。')).toBeVisible();

  await identityPage.close();

  const deployPage = await context.newPage();
  const deployErrors = createF02RuntimeErrorAudit(deployPage);
  const deployAudit = await bootStations(deployPage, [station()], summary(), 'Admin');
  await deployPage.goto('/studio/index.html#/stations/station-a');
  const deployAdmin = deployPage.locator('[data-capability="station-admin-control"]');
  await deployAdmin.getByLabel('生产运行包').selectOption('pkg-a');
  await deployAdmin.getByTestId('station-deploy-package').click();
  await expect(deployAdmin.getByText('部署命令已创建；仅在命令成功且工作站激活身份匹配后才算部署完成。')).toBeVisible();
  await expect(deployAdmin.getByTestId('station-deployment-status').getByText('命令已创建')).toBeVisible();

  const audits = [...commandAudit, ...identityAudit, ...deployAudit];
  expect(audits.some(entry => entry.method === 'POST' && entry.path === '/api/stations/station-a/commands')).toBe(true);
  expect(audits.some(entry => entry.method === 'PATCH' && entry.path === '/api/stations/station-a/identity')).toBe(true);
  expect(audits.some(entry => entry.method === 'POST' && entry.path === '/api/stations/station-a/deploy-package')).toBe(true);
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  expect(identityErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  expect(deployErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  await deployPage.close();
});

test('Station list surfaces frozen-contract malformed and empty responses', async ({ page }) => {
  await bootStations(page, { items: [] });
  await page.goto('/studio/index.html#/stations');
  await expect(page.getByText('工作站列表读取失败')).toBeVisible();

  await page.unrouteAll({ behavior: 'wait' });
  await bootStations(page, []);
  await page.reload();
  await expect(page.getByText('暂无工作站')).toBeVisible();
});

for (const visual of [
  { id: 'stations-wide-light-compact', width: 1920, height: 1080 },
  { id: 'stations-mixed-light-compact', width: 1600, height: 1000 },
  { id: 'stations-light-compact', width: 1366, height: 768 },
  { id: 'stations-short-light-compact', width: 1366, height: 600 }
] as const) {
  test(`captures ${visual.id} Browser fixture evidence`, async ({ page }) => {
    test.skip(
      !hasF02VisualEvidenceTarget() && !hasF04VisualEvidenceTarget(),
      'Visual evidence output was not requested.'
    );
    await page.setViewportSize({ width: visual.width, height: visual.height });
    await installF02VisualPreferences(page, 'light', 'compact');
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const mixedStations = [
      station({
        lastExecutionOutcome: 'Succeeded',
        lastDecisionOutcome: 'Ok',
        lastDiagnosticCode: null,
        lastDiagnosticMessage: null
      }),
      station({
        stationId: 'station-b',
        stationName: '二号检测站',
        lineName: '二号线',
        machineName: 'CV-STATION-B',
        onlineState: 'Offline',
        runtimeState: 'Faulted',
        isOnline: false,
        offlineReason: 'HeartbeatExpired',
        packageName: '端子检测包',
        lastExecutionOutcome: 'Failed',
        lastDecisionOutcome: 'Undetermined',
        lastDiagnosticCode: 'CAMERA_DISCONNECTED',
        lastDiagnosticMessage: '相机连接中断'
      }),
      station({
        stationId: 'station-c',
        stationName: '三号检测站',
        lineName: '三号线',
        machineName: 'CV-STATION-C',
        onlineState: 'Degraded',
        runtimeState: 'Paused',
        packageName: '外观复检包',
        lastExecutionOutcome: 'Succeeded',
        lastDecisionOutcome: 'Ng',
        lastDiagnosticCode: 'QUALITY_GATE',
        lastDiagnosticMessage: '连续 NG，等待复核'
      })
    ];
    const audit = await bootStations(page, mixedStations, summary({
      totalStations: 3,
      onlineStations: 1,
      offlineStations: 1,
      runningStations: 1,
      faultedStations: 1,
      alertCount: 2,
      warningStations: 1,
      criticalStations: 1
    }));
    await page.goto('/studio/index.html#/stations');
    await expect(page.locator('[data-capability="stations-read"]')).toBeVisible();
    if (hasF02VisualEvidenceTarget()) {
      await captureF02VisualEvidence(page, {
        scenario: visual.id,
        viewport: { width: visual.width, height: visual.height },
        theme: 'light',
        density: 'compact',
        requests: audit,
        runtimeErrors
      });
    }
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario: visual.id,
        viewport: { width: visual.width, height: visual.height },
        runtimeErrors,
        requestAudit: audit,
        notes: ['F04.1 product baseline short-screen evidence.']
      });
    }
    expect(expectGetOnly(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}

for (const visual of [
  { id: 'station-admin-wide-light-compact', width: 1920, height: 1080, density: 'compact' },
  { id: 'station-admin-pressure-light-comfortable', width: 1536, height: 864, density: 'comfortable' }
] as const) {
  test(`captures ${visual.id} deployment identity evidence`, async ({ page }) => {
    test.skip(
      !hasF02VisualEvidenceTarget(),
      'Visual evidence output was not requested.'
    );
    await page.setViewportSize({ width: visual.width, height: visual.height });
    await installF02VisualPreferences(page, 'light', visual.density);
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const deploymentCommand = command({
      commandId: 'deploy-succeeded', commandType: 'DeployPackage', clientRequestId: 'deploy-request-1',
      payloadJson: JSON.stringify(deploymentPayload()), status: 'Succeeded', progressPercent: 100,
      completedAtUtc: '2026-07-15T02:02:00Z', resultMessage: 'Package pkg-a deployed.'
    });
    const audit = await bootStations(page, [station()], summary(), 'Admin', [deploymentCommand]);
    await page.goto('/studio/index.html#/stations/station-a');
    const admin = page.locator('[data-capability="station-admin-control"]');
    await expect(admin).toBeVisible();
    const deploymentStatus = admin.getByTestId('station-deployment-status');
    await expect(deploymentStatus.getByText('部署完成')).toBeVisible();
    await deploymentStatus.evaluate(element => element.scrollIntoView({ block: 'center' }));
    await captureF02VisualEvidence(page, {
      scenario: visual.id,
      viewport: { width: visual.width, height: visual.height },
      theme: 'light',
      density: visual.density,
      requests: audit,
      runtimeErrors
    });
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}
