import { expect, type Page, type Route, test } from '@playwright/test';
import {
  auditF02Request,
  captureF02VisualEvidence,
  createF02RuntimeErrorAudit,
  expectGetOnly,
  f02ResultsPerformanceFixtureCount,
  fulfillF02Json,
  hasF02VisualEvidenceTarget,
  installF02BrowserStartup,
  installF02VisualPreferences,
  type F02MethodAuditEntry
} from './f02-browser-fixture';
import {
  captureF04VisualEvidence,
  createF04RuntimeErrorAudit,
  hasF04VisualEvidenceTarget
} from './f04-browser-evidence';

const fixtureSchemaVersion = 'f02-results-read.v1';
const projectId = '11111111-1111-4111-8111-111111111111';
const localResultId = '22222222-2222-4222-8222-222222222222';
const missingResultId = '99999999-9999-4999-8999-999999999999';
const defectId = '33333333-3333-4333-8333-333333333333';
const canonicalCases = Object.freeze([
  ['Ok', 'Succeeded', 'Ok'],
  ['Ng', 'Succeeded', 'Ng'],
  ['Undetermined', 'Succeeded', 'Undetermined'],
  ['NotApplicable', 'Succeeded', 'NotApplicable'],
  ['Invalid', 'Succeeded', 'Invalid'],
  ['Failed', 'Failed', 'Undetermined'],
  ['Cancelled', 'Cancelled', 'NotApplicable'],
  ['TimedOut', 'TimedOut', 'Undetermined'],
  ['Skipped', 'Skipped', 'NotApplicable']
] as const);

function project() {
  return {
    id: projectId,
    name: '瓶盖检测',
    description: 'Results 工程选择摘要',
    version: '1.0.0',
    persistenceRevision: 8,
    createdAt: '2026-07-15T00:00:00Z',
    modifiedAt: '2026-07-15T00:30:00Z',
    lastOpenedAt: null
  };
}

function localSummary(kind = 'NotApplicable') {
  const canonical = canonicalCases.find(item => item[0] === kind) ?? canonicalCases[3];
  return {
    id: localResultId,
    resultId: localResultId,
    projectId,
    status: kind,
    executionOutcome: canonical[1],
    decisionOutcome: canonical[2],
    decisionSource: 'FinalDecision',
    reasonCode: `FIXTURE_${kind.toUpperCase()}`,
    hasJudgmentSignal: kind === 'Ok' || kind === 'Ng',
    defectCount: 1,
    processingTimeMs: 16,
    inspectionTime: '2026-07-15T01:00:02Z',
    startedAt: '2026-07-15T01:00:01Z',
    completedAt: '2026-07-15T01:00:02Z',
    confidenceScore: 0.91,
    flowVersionHash: 'flow-hash',
    calibrationBundleId: null,
    runId: null,
    diagnosticCode: `FIXTURE_${kind.toUpperCase()}`,
    diagnosticMessage: `fixture ${kind}`,
    errorMessage: null
  };
}

function localDetail(kind = 'NotApplicable') {
  return {
    ...localSummary(kind),
    defects: [{
      id: defectId,
      type: 'Scratch',
      x: 1,
      y: 2,
      width: 3,
      height: 4,
      confidenceScore: 0.91,
      description: '轻微划痕',
      annotationData: null
    }],
    traceability: {
      flowVersionHash: 'flow-hash',
      calibrationBundleId: null,
      sessionId: null,
      runId: null,
      packageId: null,
      stationId: null
    }
  };
}

function stationFixture(index: number) {
  const canonical = canonicalCases[index % canonicalCases.length]!;
  const legacy = index === 0;
  return {
    schemaVersion: 2,
    stationId: `station-${String((index % 8) + 1).padStart(2, '0')}`,
    lineName: `line-${(index % 3) + 1}`,
    sequenceId: index + 1,
    messageId: `fixture-result-${String(index + 1).padStart(4, '0')}`,
    runId: `fixture-run-${String(index + 1).padStart(4, '0')}`,
    packageId: 'package-results-fixture',
    packageName: 'Results 500 Fixture',
    packageVersion: '1.0.0',
    projectRevision: 8,
    outcome: legacy ? 2 : canonical[0] === 'Ng' ? 1 : 0,
    inspectionStatus: legacy ? 'Error' : canonical[0],
    ...(legacy ? {} : {
      executionOutcome: canonical[1],
      decisionOutcome: canonical[2],
      hasJudgmentSignal: canonical[0] === 'Ok' || canonical[0] === 'Ng',
      decisionSource: 'FinalDecision',
      reasonCode: `FIXTURE_${canonical[0].toUpperCase()}`
    }),
    executionTimeMs: 10 + index,
    diagnosticCode: legacy ? 'LEGACY_ERROR' : `FIXTURE_${canonical[0].toUpperCase()}`,
    diagnosticMessage: legacy ? 'legacy 文案中即使出现 NG 也不得推断' : null,
    startedAtUtc: new Date(Date.UTC(2026, 6, 15, 0, 0, index)).toISOString(),
    completedAtUtc: new Date(Date.UTC(2026, 6, 15, 0, 0, index + 1)).toISOString()
  };
}

const stationFixtures = Object.freeze(
  Array.from({ length: f02ResultsPerformanceFixtureCount }, (_, index) => stationFixture(index))
);

async function fulfillJson(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, fixtureSchemaVersion);
}

async function bootResults(page: Page, initialHash = '/results'): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  await installF02BrowserStartup(page);
  await page.route('**/health', route => fulfillJson(route, 200, { status: 'Healthy', port: 5177 }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    if (url.pathname === '/api/auth/setup-status') {
      await fulfillJson(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
      return;
    }
    if (url.pathname === '/api/auth/me') {
      await fulfillJson(route, 200, {
        userId: 'fixture-user',
        username: 'fixture-engineer',
        role: 'Engineer'
      });
      return;
    }
    if (url.pathname === '/api/projects') {
      await fulfillJson(route, 200, [project()]);
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectId}/${missingResultId}`) {
      await fulfillJson(route, 404, { error: 'Inspection history result was not found.' });
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectId}/${localResultId}`) {
      await fulfillJson(route, 200, localDetail(url.searchParams.get('status') ?? 'NotApplicable'));
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectId}`) {
      const item = localSummary(url.searchParams.get('status') ?? 'NotApplicable');
      await fulfillJson(route, 200, {
        items: [item],
        totalCount: 1,
        pageIndex: Number(url.searchParams.get('pageIndex') ?? 0),
        pageSize: Number(url.searchParams.get('pageSize') ?? 20)
      });
      return;
    }
    if (url.pathname === '/api/stations/results') {
      const requestedStatus = url.searchParams.get('status');
      const requestedDiagnostic = url.searchParams.get('diagnosticCode');
      const filtered = stationFixtures.filter(item => {
        const kind = item.messageId === 'fixture-result-0001'
          ? 'Failed'
          : canonicalCases[(item.sequenceId - 1) % canonicalCases.length]![0];
        return (!requestedStatus || kind === requestedStatus) &&
          (!requestedDiagnostic || item.diagnosticCode === requestedDiagnostic);
      });
      const pageIndex = Number(url.searchParams.get('pageIndex') ?? 0);
      const pageSize = Number(url.searchParams.get('pageSize') ?? 20);
      await fulfillJson(route, 200, {
        items: filtered.slice(pageIndex * pageSize, (pageIndex + 1) * pageSize),
        totalCount: filtered.length,
        pageIndex,
        pageSize
      });
      return;
    }
    await fulfillJson(route, 404, { error: 'NotFound' });
  });

  await page.goto(`/studio/index.html#${initialHash}`);
  await expect(page.locator('[data-capability="results-read"]')).toBeVisible();
  return audit;
}

test('Results local view keeps query filters, dual axes, detail 404 and GET-only transport', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF04RuntimeErrorAudit(page);
  const audit = await bootResults(page, '/results?source=local');
  await expect(page.getByText('请选择本机工程')).toBeVisible();
  expect(audit.some(entry => entry.path.includes('/inspection/history/'))).toBe(false);

  await page.getByLabel('本机工程').selectOption(projectId);
  await expect(page.getByRole('button', { name: '返回工作区' })).toBeVisible();
  await expect(page.getByRole('cell', { name: '不适用', exact: true }).first()).toBeVisible();
  await expect(page.getByRole('cell', { name: '执行成功', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: '不适用', exact: true }).nth(1)).toBeVisible();
  await page.getByLabel('标准结果').selectOption('Invalid');
  await expect.poll(() => audit.some(entry => entry.path.includes('status=Invalid'))).toBe(true);
  await page.getByLabel('诊断码').fill('FIXTURE_INVALID');
  await expect(page.getByText('本机诊断码为当前页过滤')).toBeVisible();
  await page.getByRole('button', { name: '查看详情' }).first().click();
  await expect(page.getByText('轻微划痕')).toBeVisible();
  await expect(page.getByText('Flow Hash')).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'results', viewport, runtimeErrors, requestAudit: audit
    });
  }

  await page.goto(`/studio/index.html#/results?source=local&projectId=${projectId}&resultId=${missingResultId}`);
  await expect(page.getByText('结果详情不存在')).toBeVisible();
  expect(expectGetOnly(audit)).toBe(true);
});

test('Results Station view paginates the frozen 500-result fixture and marks legacy projection', async ({ page }) => {
  const audit = await bootResults(page, '/results?source=station&pageSize=200&resultId=fixture-result-0001');
  await expect(page.getByText('旧版工作站结果映射')).toBeVisible();
  const detail = page.getByLabel('工作站结果详情');
  await expect(detail.getByText('执行失败', { exact: true }).first()).toBeVisible();
  await expect(detail.getByText('未判定', { exact: true })).toBeVisible();
  await expect(page.getByText('第 1–200 项，共 500 项')).toBeVisible();
  await page.getByRole('button', { name: '第 3 页' }).click();
  await expect(page.getByText('第 401–500 项，共 500 项')).toBeVisible();
  await expect.poll(() => audit.some(entry => entry.path.includes('pageIndex=2&pageSize=200'))).toBe(true);

  await page.getByLabel('标准结果').selectOption('TimedOut');
  await page.getByLabel('诊断码').fill('FIXTURE_TIMEDOUT');
  await expect.poll(() => audit.some(entry =>
    entry.path.includes('status=TimedOut') && entry.path.includes('diagnosticCode=FIXTURE_TIMEDOUT')
  )).toBe(true);
  expect(stationFixtures).toHaveLength(f02ResultsPerformanceFixtureCount);
  expect(expectGetOnly(audit)).toBe(true);
});

for (const visual of [
  { id: 'results-light-compact', width: 1366, height: 768 },
  { id: 'results-short-light-compact', width: 1366, height: 600 }
] as const) {
  test(`captures ${visual.id} Browser fixture evidence`, async ({ page }) => {
    test.skip(!hasF02VisualEvidenceTarget(), 'F02 visual evidence output was not requested.');
    await page.setViewportSize({ width: visual.width, height: visual.height });
    await installF02VisualPreferences(page, 'light', 'compact');
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const audit = await bootResults(page, '/results?source=station&pageSize=200&resultId=fixture-result-0001');
    await expect(page.getByText('第 1–200 项，共 500 项')).toBeVisible();
    await captureF02VisualEvidence(page, {
      scenario: visual.id,
      viewport: { width: visual.width, height: visual.height },
      theme: 'light',
      density: 'compact',
      requests: audit,
      runtimeErrors
    });
    expect(expectGetOnly(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}
