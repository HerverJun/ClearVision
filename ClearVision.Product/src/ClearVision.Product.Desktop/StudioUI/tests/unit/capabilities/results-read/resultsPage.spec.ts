import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it } from 'vitest';
import { ResultsPage } from '@/capabilities/results-read';
import {
  ApiAbortError,
  ApiForbiddenError,
  ApiNotFoundError,
  ApiServerError,
  type ApiGetOptions,
  type ApiTransport
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

const projectId = '11111111-1111-4111-8111-111111111111';
const resultId = '22222222-2222-4222-8222-222222222222';
const defectId = '33333333-3333-4333-8333-333333333333';
const referenceId = '44444444-4444-4444-8444-444444444444';
const snapshotId = '55555555-5555-4555-8555-555555555555';

function project() {
  return {
    id: projectId,
    name: '瓶盖检测',
    description: '稳定摘要',
    version: '1.0.0',
    persistenceRevision: 2,
    createdAt: '2026-07-15T00:00:00Z',
    modifiedAt: null,
    lastOpenedAt: null
  };
}

function localSummary(overrides: Record<string, unknown> = {}) {
  return {
    id: resultId,
    resultId,
    projectId,
    status: 'NotInspected',
    executionOutcome: 'Succeeded',
    decisionOutcome: 'NotApplicable',
    decisionSource: 'FinalDecision',
    reasonCode: 'NOT_APPLICABLE',
    hasJudgmentSignal: false,
    defectCount: 1,
    processingTimeMs: 12,
    inspectionTime: '2026-07-15T01:00:02Z',
    startedAt: '2026-07-15T01:00:01Z',
    completedAt: '2026-07-15T01:00:02Z',
    confidenceScore: null,
    flowVersionHash: 'flow-hash',
    calibrationBundleId: null,
    runId: null,
    diagnosticCode: 'NOT_APPLICABLE',
    diagnosticMessage: '该工件不适用',
    errorMessage: null,
    ...overrides
  };
}

function localPage() {
  return {
    items: [localSummary()],
    totalCount: 1,
    pageIndex: 0,
    pageSize: 20
  };
}

function localDetail() {
  return {
    ...localSummary(),
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
      executionSnapshotId: snapshotId,
      projectPersistenceRevision: 17,
      decisionConfigurationHash: 'decision-hash',
      packageId: 'package-17',
      runtimePackageId: 'package-17',
      executionSource: 'PersistedProject',
      executionRunMode: 'FormalPrimary',
      shadowRole: 'Primary',
      stationId: null
    },
    hasImage: false,
    imageReference: null,
    imageMissing: false,
    imageMissingMessage: null,
    hasOutputData: true,
    hasAnalysisData: true,
    hasEvidenceManifest: false,
    evidenceStatus: 'missing',
    evidenceManifestReference: `/api/inspection/history/${projectId}/${resultId}/evidence/manifest`,
    evidenceTotalBytes: null,
    retentionExpiresAtUtc: null,
    evidenceMessage: '证据清单缺失或已清理'
  };
}

function statistics() {
  return {
    totalCount: 10,
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
    averageProcessingTimeMs: 18
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
    processingTimeMs: 12,
    confidenceScore: null,
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

function legacyStationPage() {
  return {
    items: [{
      schemaVersion: 1,
      stationId: 'station-a',
      lineName: 'line-a',
      sequenceId: 9,
      messageId: 'message-9',
      runId: 'run-9',
      packageId: 'package-a',
      packageName: '瓶盖检测',
      packageVersion: '1.0.0',
      packageFlowHash: null,
      executionFlowHash: null,
      flowHash: null,
      executionSnapshotId: null,
      projectRevision: null,
      decisionConfigurationHash: null,
      executionRunMode: null,
      outcome: 2,
      inspectionStatus: 'Error',
      executionTimeMs: 88,
      diagnosticCode: 'TEXT_SAYS_NG',
      diagnosticMessage: '文案写 NG 也不得折叠',
      primaryOutputsPreview: {},
      startedAtUtc: '2026-07-15T01:00:00Z',
      completedAtUtc: '2026-07-15T01:00:01Z'
    }],
    totalCount: 1,
    pageIndex: 0,
    pageSize: 20
  };
}

function httpDetails(status: number) {
  return {
    url: 'http://localhost:5000/api/results',
    status,
    statusText: 'test',
    payload: undefined,
    responseBody: ''
  };
}

type GetImplementation = (path: string, options?: ApiGetOptions) => Promise<unknown>;

function apiWith(implementation: GetImplementation): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await implementation(path, options) as T | undefined;
    },
    async getBlob() {
      return {
        blob: new Blob([new Uint8Array([1])]), contentType: 'application/zip', contentLength: 1,
        etag: null, sha256: null, headers: new Headers()
      };
    }
  };
}

async function mountResults(path: string, api: ApiTransport) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/results', component: { template: '<div />' } },
      { path: '/stations/:stationId', component: { template: '<div />' } },
      { path: '/projects/:id/workspace', component: { template: '<div />' } },
      { path: '/projects/:id/inspection', component: { template: '<div />' } }
    ]
  });
  await router.push(path);
  await router.isReady();
  const queries = createReadQueryClient(api);
  const wrapper = mount(ResultsPage, {
    props: { runtime: { queries, api } },
    global: { plugins: [router] }
  });
  await flushPromises();
  return { wrapper, queries, router };
}

describe('Results page', () => {
  it('does not request local history without a projectId', async () => {
    const requested: string[] = [];
    const mounted = await mountResults('/results?source=local', apiWith(async path => {
      requested.push(path);
      if (path === 'projects') return [project()];
      throw new Error(`Unexpected request: ${path}`);
    }));

    expect(requested).toEqual(['projects']);
    expect(mounted.wrapper.text()).toContain('请选择本机工程');

    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });

  it('renders local Execution and Decision axes, scalar detail and defect summary', async () => {
    const requested: string[] = [];
    const mounted = await mountResults(
      `/results?source=local&projectId=${projectId}&resultId=${resultId}`,
      apiWith(async path => {
        requested.push(path);
        if (path === 'projects') return [project()];
        if (path === `inspection/statistics/${projectId}`) return statistics();
        if (path === `inspection/history/${projectId}/${resultId}`) return localDetail();
        if (path === `inspection/history/${projectId}/${resultId}/evidence/manifest`) return {
          status: 'available',
          message: '证据可用',
          manifest: {
            schemaVersion: 1,
            manifestId: 'manifest-deep-link',
            projectId,
            inspectionResultId: resultId,
            status: 'available',
            outcome: 'NotApplicable',
            createdAtUtc: '2026-07-15T01:00:02Z',
            flowVersionHash: 'flow-hash',
            calibrationBundleId: null,
            sessionId: null,
            runId: null,
            retentionClass: 'standard',
            retentionExpiresAtUtc: null,
            totalBytes: 1,
            checksum: 'sha',
            redaction: { applied: true },
            items: []
          }
        };
        if (path.startsWith(`inspection/history/${projectId}?`)) return localPage();
        if (path === `inspection/history/${projectId}/${resultId}/previous-success?limit=50`) return {
          currentSummary: comparisonSummary(resultId, 'Ng'),
          referenceSummary: comparisonSummary(referenceId, 'Ok'),
          found: true,
          isFlowVersionFallback: false,
          queryLimit: 50,
          warnings: [],
          message: '已找到失败前成功参考'
        };
        if (path.startsWith(`inspection/history/${projectId}/compare?`)) return {
          leftSummary: comparisonSummary(referenceId, 'Ok'),
          rightSummary: comparisonSummary(resultId, 'Ng'),
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
        };
        throw new Error(`Unexpected request: ${path}`);
      })
    );

    expect(mounted.wrapper.text()).toContain('不适用');
    expect(mounted.wrapper.text()).toContain('执行成功');
    expect(mounted.wrapper.text()).toContain('判定结果不适用');
    expect(mounted.wrapper.text()).toContain('轻微划痕');
    expect(mounted.wrapper.text()).toContain('流程版本哈希');
    expect(mounted.wrapper.text()).toContain('执行快照');
    expect(mounted.wrapper.text()).toContain('判定配置哈希');
    expect(mounted.wrapper.text()).toContain('83.3%');
    expect(mounted.wrapper.text()).toContain('manifest-deep-link');
    expect(requested).toContain(`inspection/history/${projectId}/${resultId}/evidence/manifest`);
    expect(mounted.wrapper.text()).toContain('本次检测未产生可访问图像');

    await mounted.wrapper.get('[data-testid="results-previous-success"]').trigger('click');
    await flushPromises();
    expect(mounted.wrapper.text()).toContain('已找到失败前成功参考');
    expect(mounted.wrapper.text()).toContain('仅比较安全预览字段');
    expect(mounted.wrapper.text()).toContain('decisionOutcome');

    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });

  it('marks legacy Station mapping and never folds Error into NG', async () => {
    const mounted = await mountResults(
      '/results?source=station&resultId=message-9',
      apiWith(async path => {
        if (path.startsWith('stations/results?')) return legacyStationPage();
        if (path === 'stations/statistics') return statistics();
        throw new Error(`Unexpected request: ${path}`);
      })
    );

    expect(mounted.wrapper.text()).toContain('旧版结果映射');
    expect(mounted.wrapper.text()).toContain('旧版工作站结果映射');
    expect(mounted.wrapper.text()).toContain('执行失败');
    expect(mounted.wrapper.text()).toContain('未判定');
    expect(mounted.wrapper.findAll('[data-status-tone="error"]')).toHaveLength(1);
    expect(mounted.wrapper.findAll('[data-status-tone="ng"]')).toHaveLength(0);
    expect(mounted.wrapper.text()).not.toContain('判定 NG');
    expect(mounted.wrapper.text()).toContain('远程结果仅保留摘要');
    expect(mounted.wrapper.find('[data-remote-image-status="not-uploaded"]').exists()).toBe(true);

    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });

  it('re-reads a Station deep link by canonical station id and preserves a safe return route', async () => {
    const requested: string[] = [];
    const mounted = await mountResults(
      '/results?source=station&stationId=station-a&resultId=message-9&returnTo=/stations/station-a',
      apiWith(async path => {
        requested.push(path);
        if (path.startsWith('stations/results?')) return legacyStationPage();
        if (path.startsWith('stations/statistics?')) return statistics();
        throw new Error(`Unexpected request: ${path}`);
      })
    );

    expect(requested).toContain('stations/results?stationId=station-a&pageIndex=0&pageSize=20');
    expect(requested).toContain('stations/statistics?stationId=station-a');
    expect(mounted.wrapper.text()).toContain('已按工作站身份读取');
    expect(mounted.wrapper.get('[data-testid="results-return-workspace"]').text()).toBe('返回工作站');

    await mounted.wrapper.get('[data-testid="results-detail-open-station"]').trigger('click');
    await flushPromises();
    expect(mounted.router.currentRoute.value.path).toBe('/stations/station-a');
    expect(mounted.router.currentRoute.value.query.returnTo).toBe(
      '/results?source=station&stationId=station-a&resultId=message-9'
    );

    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });

  it('rejects Station rows whose identity differs from the deep-link filter', async () => {
    const mounted = await mountResults(
      '/results?source=station&stationId=station-a',
      apiWith(async path => path.startsWith('stations/results?')
        ? { ...legacyStationPage(), items: [{ ...legacyStationPage().items[0], stationId: 'station-b' }] }
        : statistics())
    );

    expect(mounted.wrapper.text()).toContain('工作站结果读取失败');
    expect(mounted.wrapper.text()).not.toContain('station-b');
    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });

  it('labels local diagnostic filtering as current-page only', async () => {
    const mounted = await mountResults(
      `/results?source=local&projectId=${projectId}&diagnosticCode=OTHER_CODE`,
      apiWith(async path => path === 'projects' ? [project()] : localPage())
    );

    expect(mounted.wrapper.text()).toContain('本机诊断码为当前页过滤');
    expect(mounted.wrapper.text()).toContain('当前页没有匹配诊断码的结果');
    expect(mounted.wrapper.text()).toContain('不代表后端全量结果计数');

    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });

  it('surfaces forbidden list and 404 local detail as localized regions', async () => {
    const forbidden = await mountResults(
      `/results?source=local&projectId=${projectId}`,
      apiWith(async path => {
        if (path === 'projects') return [project()];
        throw new ApiForbiddenError(httpDetails(403));
      })
    );
    expect(forbidden.wrapper.text()).toContain('无权读取本机结果');
    forbidden.wrapper.unmount();
    forbidden.queries.dispose();

    const missing = await mountResults(
      `/results?source=local&projectId=${projectId}&resultId=${resultId}`,
      apiWith(async path => {
        if (path === 'projects') return [project()];
        if (path.endsWith(`/${resultId}`)) throw new ApiNotFoundError(httpDetails(404));
        return localPage();
      })
    );
    expect(missing.wrapper.text()).toContain('结果详情不存在');
    expect(missing.wrapper.text()).toContain('404');
    missing.wrapper.unmount();
    missing.queries.dispose();
  });

  it('presents stale and aborted query phases at capability level', async () => {
    let attempt = 0;
    const stale = await mountResults('/results?source=station', apiWith(async () => {
      attempt += 1;
      if (attempt === 1) return legacyStationPage();
      throw new ApiServerError(httpDetails(503));
    }));
    await stale.wrapper.get('button').trigger('click');
    await flushPromises();
    expect(stale.wrapper.text()).toContain('工作站结果刷新失败');
    expect(stale.wrapper.text()).toContain('旧数据');
    stale.wrapper.unmount();
    stale.queries.dispose();

    const aborted = await mountResults('/results?source=station', apiWith(async path => {
      throw new ApiAbortError(path);
    }));
    expect(aborted.wrapper.text()).toContain('工作站结果请求已取消');
    aborted.wrapper.unmount();
    aborted.queries.dispose();
  });
});
