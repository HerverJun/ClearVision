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
const referenceResultId = '44444444-4444-4444-8444-444444444444';
const executionSnapshotId = '55555555-5555-4555-8555-555555555555';
const imageId = '66666666-6666-4666-8666-666666666666';
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

function localDetail(kind = 'NotApplicable', evidenceStatus = 'available') {
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
      executionSnapshotId,
      projectPersistenceRevision: 8,
      decisionConfigurationHash: 'decision-hash',
      packageId: 'package-f02',
      runtimePackageId: 'package-f02',
      executionSource: 'PersistedProject',
      executionRunMode: 'FormalPrimary',
      shadowRole: 'Primary',
      stationId: null
    },
    hasImage: true,
    imageReference: `/api/images/${imageId}`,
    imageMissing: false,
    imageMissingMessage: null,
    hasOutputData: true,
    hasAnalysisData: true,
    hasEvidenceManifest: evidenceStatus !== 'missing',
    evidenceStatus,
    evidenceManifestReference: `/api/inspection/history/${projectId}/${localResultId}/evidence/manifest`,
    evidenceTotalBytes: 4,
    retentionExpiresAtUtc: evidenceStatus === 'expired' ? '2026-07-16T00:00:00Z' : null,
    evidenceMessage: evidenceStatus === 'expired' ? '证据已过保留期' : '证据可用'
  };
}

function statistics() {
  return {
    totalAttemptCount: 10,
    executionSucceededCount: 8,
    validDecisionCount: 6,
    okCount: 5,
    ngCount: 1,
    undeterminedCount: 1,
    notApplicableCount: 1,
    invalidCount: 0,
    failedCount: 1,
    cancelledCount: 0,
    timedOutCount: 1,
    skippedCount: 0,
    executionFailureCount: 2,
    yieldRate: 5 / 6,
    decisionCoverageRate: 0.75,
    averageExecutionTimeMs: 18
  };
}

function comparisonSummary(id: string, decision: 'Ok' | 'Ng') {
  return {
    resultId: id,
    id,
    projectId,
    status: decision === 'Ok' ? 'OK' : 'NG',
    executionOutcome: 'Succeeded',
    decisionOutcome: decision,
    inspectionTime: '2026-07-15T01:00:02Z',
    defectCount: decision === 'Ok' ? 0 : 1,
    processingTimeMs: 16,
    confidenceScore: 0.91,
    flowVersionHash: 'flow-hash',
    calibrationBundleId: null,
    sessionId: null,
    runId: null,
    imageReference: null,
    hasImage: false,
    hasOutputData: true,
    hasAnalysisData: true
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
    packageFlowHash: 'package-flow-hash',
    executionFlowHash: 'execution-flow-hash',
    flowHash: 'execution-flow-hash',
    executionSnapshotId,
    projectRevision: 8,
    decisionConfigurationHash: 'decision-hash',
    executionRunMode: 'StationRuntime',
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
    primaryOutputsPreview: { score: String(90 - (index % 10)) },
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
  const initialQuery = new URL(initialHash, 'http://clearvision.local').searchParams;
  let activeLocalKind = initialQuery.get('outcome') ?? 'NotApplicable';
  const evidenceMode = initialQuery.get('evidenceMode') ?? 'available';
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
    if (url.pathname === `/api/inspection/statistics/${projectId}` || url.pathname === '/api/stations/statistics') {
      await fulfillJson(route, 200, statistics());
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectId}/${localResultId}/evidence/manifest`) {
      if (evidenceMode === 'expired') {
        await fulfillJson(route, 200, {
          status: 'expired',
          errorCode: 'EvidenceExpired',
          message: 'Evidence manifest retention has expired.',
          manifest: null
        });
        return;
      }
      await fulfillJson(route, 200, {
        status: 'available',
        message: '证据可用',
        manifest: {
          schemaVersion: 1,
          manifestId: 'manifest-f02',
          projectId,
          inspectionResultId: localResultId,
          status: 'available',
          outcome: 'NotApplicable',
          createdAtUtc: '2026-07-15T01:00:02Z',
          flowVersionHash: 'flow-hash',
          calibrationBundleId: null,
          sessionId: null,
          runId: null,
          retentionClass: 'standard',
          retentionExpiresAtUtc: null,
          totalBytes: 4,
          checksum: 'fixture-sha',
          redaction: { applied: true },
          items: []
        }
      });
      return;
    }
    if (url.pathname === `/api/images/${imageId}`) {
      await route.fulfill({
        status: 200,
        contentType: 'image/png',
        body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64')
      });
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectId}/${missingResultId}`) {
      await fulfillJson(route, 404, { error: 'Inspection history result was not found.' });
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectId}/${localResultId}`) {
      await fulfillJson(route, 200, localDetail(activeLocalKind, evidenceMode));
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectId}/${localResultId}/previous-success`) {
      await fulfillJson(route, 200, {
        currentSummary: comparisonSummary(localResultId, 'Ng'),
        referenceSummary: comparisonSummary(referenceResultId, 'Ok'),
        found: true,
        isFlowVersionFallback: false,
        queryLimit: 50,
        warnings: [],
        message: '已找到失败前成功参考'
      });
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectId}/compare`) {
      await fulfillJson(route, 200, {
        leftSummary: comparisonSummary(referenceResultId, 'Ok'),
        rightSummary: comparisonSummary(localResultId, 'Ng'),
        compatibility: {
          flowVersionCompatible: true,
          calibrationBundleCompatible: true,
          onlySafePreviewComparison: true,
          hasUnknownFields: false
        },
        warnings: ['仅比较安全预览字段'],
        fieldDiffs: [{
          path: '$["outcome"]["decision"]', label: 'decisionOutcome',
          leftValuePreview: 'Ok', rightValuePreview: 'Ng', diffType: 'Changed',
          severity: 'info', message: null
        }],
        traceabilityDiff: [],
        sceneReplayAvailability: {
          kind: 'scene', mode: 'summary-only', isAvailable: false,
          leftAvailable: false, rightAvailable: false, leftReference: null,
          rightReference: null, leftSummary: null, rightSummary: null,
          message: '暂无 Scene evidence，已降级为摘要回放'
        },
        imageReplayAvailability: {
          kind: 'image', mode: 'summary-only', isAvailable: false,
          leftAvailable: false, rightAvailable: false, leftReference: null,
          rightReference: null, leftSummary: 'no image', rightSummary: 'no image',
          message: '无图像引用，已降级为摘要回放'
        }
      });
      return;
    }
    if (url.pathname === `/api/inspection/history/${projectId}`) {
      activeLocalKind = url.searchParams.get('status') ?? 'NotApplicable';
      const item = localSummary(activeLocalKind);
      await fulfillJson(route, 200, {
        items: [item],
        totalCount: 1,
        pageIndex: Number(url.searchParams.get('pageIndex') ?? 0),
        pageSize: Number(url.searchParams.get('pageSize') ?? 20)
      });
      return;
    }
    if (url.pathname === '/api/stations/results') {
      const requestedStationId = url.searchParams.get('stationId');
      const requestedStatus = url.searchParams.get('status');
      const requestedDiagnostic = url.searchParams.get('diagnosticCode');
      const filtered = stationFixtures.filter(item => {
        const kind = item.messageId === 'fixture-result-0001'
          ? 'Failed'
          : canonicalCases[(item.sequenceId - 1) % canonicalCases.length]![0];
        return (!requestedStationId || item.stationId === requestedStationId) &&
          (!requestedStatus || kind === requestedStatus) &&
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
  await expect(page.getByRole('link', { name: '返回工作区' })).toBeVisible();
  await expect(page.getByRole('cell', { name: '不适用', exact: true }).first()).toBeVisible();
  await expect(page.getByRole('cell', { name: '执行成功', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: '不适用', exact: true }).nth(1)).toBeVisible();
  await page.getByLabel('标准结果').selectOption('Invalid');
  await expect.poll(() => audit.some(entry => entry.path.includes('status=Invalid'))).toBe(true);
  await page.getByLabel('诊断码').fill('FIXTURE_INVALID');
  await expect(page.getByText('本机诊断码为当前页过滤')).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'results-filter-list', viewport, runtimeErrors, requestAudit: audit
    });
  }
  await page.getByRole('button', { name: '查看详情' }).first().click();
  await expect(page.getByText('轻微划痕')).toBeVisible();
  await expect(page.getByText('判定依据', { exact: true })).toBeVisible();
  await expect(page.getByText('FinalDecision', { exact: true })).toBeVisible();
  await expect(page.getByRole('img', { name: '本机检测结果图像' })).toBeVisible();
  await expect(page.getByText('83.3%', { exact: true })).toBeVisible();
  await page.getByText('技术追溯', { exact: true }).click();
  await expect(page.getByText('流程版本哈希')).toBeVisible();
  await expect(page.getByText(executionSnapshotId, { exact: true })).toBeVisible();
  await expect(page.getByText('decision-hash', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '查找前次成功并对比' }).click();
  await expect(page.getByText('已找到失败前成功参考')).toBeVisible();
  await expect(page.getByText('仅比较安全预览字段')).toBeVisible();
  await expect(page.getByText('decisionOutcome', { exact: true })).toBeVisible();
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'results-detail', viewport, runtimeErrors, requestAudit: audit
    });
  }

  await page.goto(`/studio/index.html#/results?source=local&projectId=${projectId}&resultId=${missingResultId}`);
  await expect(page.getByText('结果详情不存在')).toBeVisible();
  expect(expectGetOnly(audit)).toBe(true);
});

test('Results renders NG and non-NG exception axes without inference and distinguishes expired evidence', async ({ page }) => {
  const audit = await bootResults(
    page,
    `/results?source=local&projectId=${projectId}&resultId=${localResultId}&outcome=Ng&evidenceMode=expired`
  );
  const detail = page.getByLabel('结果详情');
  await expect(detail.getByText('判定 NG', { exact: true })).toBeVisible();
  await expect(detail.getByText('FIXTURE_NG', { exact: true }).first()).toBeVisible();
  await expect(page.locator('[data-evidence-phase="expired"]')).toBeVisible();
  await expect(page.getByText('证据已过保留期，结果摘要仍可调查。')).toBeVisible();
  await expect(page.getByText('加载失败', { exact: true })).toHaveCount(0);

  for (const [kind, executionLabel, decisionLabel] of [
    ['Failed', '执行失败', '未判定'],
    ['Invalid', '执行成功', '判定无效'],
    ['Undetermined', '执行成功', '未判定'],
    ['Cancelled', '已取消', '不适用']
  ] as const) {
    await page.getByLabel('标准结果').selectOption(kind);
    await page.getByRole('button', { name: '查看详情' }).first().click();
    await expect(detail.getByText(executionLabel, { exact: true }).first()).toBeVisible();
    await expect(detail.getByText(decisionLabel, { exact: true }).first()).toBeVisible();
  }
  expect(expectGetOnly(audit)).toBe(true);
});

test('Results Station view paginates the frozen 500-result fixture and marks legacy projection', async ({ page }) => {
  const audit = await bootResults(page, '/results?source=station&pageSize=200&resultId=fixture-result-0001');
  await expect(page.getByText('旧版工作站结果映射')).toBeVisible();
  const detail = page.getByLabel('工作站结果详情');
  await expect(detail.getByText('执行失败', { exact: true }).first()).toBeVisible();
  await expect(detail.getByText('未判定', { exact: true })).toBeVisible();
  await expect(detail.getByText('远程结果仅保留摘要')).toBeVisible();
  await expect(detail.locator('[data-remote-image-status="not-uploaded"]')).toBeVisible();
  expect(audit.some(entry => entry.path.startsWith('/api/images/'))).toBe(false);
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
  { id: 'results-wide-light-compact', width: 1920, height: 1080 },
  { id: 'results-light-compact', width: 1366, height: 768 },
  { id: 'results-short-light-compact', width: 1366, height: 600 }
] as const) {
  test(`captures ${visual.id} Browser fixture evidence`, async ({ page }) => {
    test.skip(
      !hasF02VisualEvidenceTarget() && !hasF04VisualEvidenceTarget(),
      'Visual evidence output was not requested.'
    );
    await page.setViewportSize({ width: visual.width, height: visual.height });
    await installF02VisualPreferences(page, 'light', 'compact');
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const audit = await bootResults(page, '/results?source=station&pageSize=200&resultId=fixture-result-0001');
    await expect(page.getByText('第 1–200 项，共 500 项')).toBeVisible();
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
        notes: ['F04.1 core page short-screen evidence.']
      });
    }
    expect(expectGetOnly(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}
