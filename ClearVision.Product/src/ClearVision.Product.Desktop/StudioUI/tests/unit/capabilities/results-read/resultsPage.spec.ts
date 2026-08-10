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
  type ApiTransport,
  type ApiWriteOptions
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

const projectId = '11111111-1111-4111-8111-111111111111';
const resultId = '22222222-2222-4222-8222-222222222222';
const defectId = '33333333-3333-4333-8333-333333333333';
const referenceId = '44444444-4444-4444-8444-444444444444';
const snapshotId = '55555555-5555-4555-8555-555555555555';
const sessionId = '66666666-6666-4666-8666-666666666666';

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
      sessionId,
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
type PostImplementation = (path: string, body: unknown, options?: ApiWriteOptions) => Promise<unknown>;

function apiWith(implementation: GetImplementation, postImplementation?: PostImplementation): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await implementation(path, options) as T | undefined;
    },
    async post<T = unknown>(path: string, body: unknown, options?: ApiWriteOptions): Promise<T | undefined> {
      if (!postImplementation) return undefined;
      return await postImplementation(path, body, options) as T | undefined;
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
    const tabs = mounted.wrapper.get('[data-testid="results-view-tabs"]').findAll('[role="tab"]');
    expect(tabs.map(tab => tab.text())).toEqual(['态势总览', '调查详情']);
    expect(tabs[0]!.attributes('aria-selected')).toBe('true');
    expect(tabs[0]!.attributes('aria-controls')).toBe('results-overview-panel');
    expect(mounted.wrapper.get('#results-overview-panel').attributes('aria-labelledby')).toBe('results-overview-tab');

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

    const tabs = mounted.wrapper.get('[data-testid="results-view-tabs"]').findAll('[role="tab"]');
    expect(tabs[1]!.attributes('aria-selected')).toBe('true');
    expect(mounted.wrapper.get('[aria-label="本机结果调查详情"]').isVisible()).toBe(true);
    await tabs[0]!.trigger('click');
    expect(tabs[0]!.attributes('aria-selected')).toBe('true');
    expect(mounted.wrapper.get('[aria-label="本机结果调查详情"]').attributes('style')).toContain('display: none');
    expect(mounted.wrapper.text()).toContain('83.3%');
    await tabs[1]!.trigger('click');

    expect(mounted.wrapper.text()).toContain('不适用');
    expect(mounted.wrapper.text()).toContain('执行成功');
    expect(mounted.wrapper.text()).toContain('判定结果不适用');
    expect(mounted.wrapper.text()).toContain('轻微划痕');
    expect(mounted.wrapper.text()).toContain('流程版本哈希');
    expect(mounted.wrapper.text()).toContain('执行快照');
    expect(mounted.wrapper.text()).toContain('Session ID');
    expect(mounted.wrapper.text()).toContain(sessionId);
    expect(mounted.wrapper.text()).toContain('Run ID 未记录，旧结果身份不完整');
    expect(mounted.wrapper.text()).toContain('判定配置哈希');
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

    expect(mounted.wrapper.text()).toContain('旧版格式');
    expect(mounted.wrapper.text()).toContain('旧版工作站结果映射');
    expect(mounted.wrapper.text()).toContain('执行失败');
    expect(mounted.wrapper.text()).toContain('未判定');
    expect(mounted.wrapper.findAll('[data-status-tone="error"]')).toHaveLength(1);
    expect(mounted.wrapper.findAll('[data-status-tone="ng"]')).toHaveLength(0);
    expect(mounted.wrapper.get('.results-page__detail-panel').text()).not.toContain('判定 NG');
    expect(mounted.wrapper.text()).toContain('远程结果仅保留摘要');
    expect(mounted.wrapper.text()).toContain('run-9');
    expect(mounted.wrapper.find('[data-remote-image-status="not-uploaded"]').exists()).toBe(true);

    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });

  it('disposes local result analysis when switching to Station results', async () => {
    const analysisSignals: AbortSignal[] = [];
    const mounted = await mountResults(
      `/results?source=local&projectId=${projectId}`,
      apiWith(async (path, options) => {
        if (path === 'projects') return [project()];
        if (path.startsWith('analysis/')) {
          const signal = options?.signal;
          if (!signal) throw new Error(`Missing analysis abort signal for ${path}`);
          analysisSignals.push(signal);
          return await new Promise<never>((_resolve, reject) => {
            const abort = () => reject(new ApiAbortError(path, signal.reason));
            if (signal.aborted) abort();
            else signal.addEventListener('abort', abort, { once: true });
          });
        }
        if (path === `inspection/statistics/${projectId}`) return statistics();
        if (path.startsWith(`inspection/history/${projectId}?`)) return localPage();
        throw new Error(`Unexpected request: ${path}`);
      })
    );

    expect(analysisSignals).toHaveLength(3);
    await mounted.router.push('/results?source=station');
    await flushPromises();

    expect(analysisSignals.every(signal => signal.aborted)).toBe(true);
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
    expect(mounted.wrapper.text()).toContain('已限定工作站');
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

    expect(mounted.wrapper.text()).toContain('诊断码只筛选当前页');
    expect(mounted.wrapper.text()).toContain('当前页没有匹配诊断码的结果');
    expect(mounted.wrapper.text()).toContain('不会重新计算完整历史计数');

    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });

  it('opens a scoped local export and disposes the export owner when switching to Station', async () => {
    const posted: Array<{ path: string; body: Record<string, unknown> }> = [];
    const mounted = await mountResults(
      `/results?source=local&projectId=${projectId}&outcome=Ng&diagnosticCode=CAMERA_TIMEOUT&from=2026-07-15T00:00:00Z&to=2026-07-15T23:59:59Z`,
      apiWith(
        async path => {
          if (path === 'projects') return [project()];
          if (path.startsWith('inspection/statistics/')) return statistics();
          if (path.startsWith('inspection/history/')) return localPage();
          return localPage();
        },
        async (path, body) => {
          posted.push({ path, body: body as Record<string, unknown> });
          const request = body as { clientOperationId: string; format: 'csv' | 'json' };
          return {
            job: {
              exportId: '22222222-2222-4222-8222-222222222222',
              projectId,
              source: 'local',
              format: request.format,
              clientOperationId: request.clientOperationId,
              state: 'completed',
              createdAtUtc: '2026-08-02T00:00:00Z',
              updatedAtUtc: '2026-08-02T00:00:01Z',
              snapshotUpperBoundUtc: '2026-08-02T00:00:00Z',
              completedAtUtc: '2026-08-02T00:00:01Z',
              artifactExpiresAtUtc: '2026-08-03T00:00:01Z',
              fileName: request.format === 'csv' ? 'results.csv' : 'results.json',
              errorCode: null,
              errorMessage: null,
              downloadAvailable: true
            }
          };
        }
      )
    );

    const openButton = mounted.wrapper.get('[data-testid="results-open-export"]');
    expect(openButton.attributes('disabled')).toBeUndefined();
    await openButton.trigger('click');
    await flushPromises();

    const modal = document.body.querySelector<HTMLElement>('[data-design-primitive="modal"]');
    expect(modal).not.toBeNull();
    const startButton = modal?.querySelector<HTMLButtonElement>('.cv-button--primary');
    expect(startButton).not.toBeNull();
    startButton?.click();
    await flushPromises();

    expect(posted).toHaveLength(1);
    expect(posted[0]).toMatchObject({ path: 'results/exports' });
    expect(posted[0]?.body).toMatchObject({
      projectId,
      source: 'local',
      format: 'csv',
      startTime: '2026-07-15T00:00:00Z',
      endTime: '2026-07-15T23:59:59Z',
      status: 'Ng',
      defectType: null,
      diagnosticCode: 'CAMERA_TIMEOUT'
    });

    await mounted.router.push('/results?source=station');
    await flushPromises();

    expect(document.body.querySelector('[data-capability="results-export"]')).toBeNull();
    expect(mounted.wrapper.find('.results-page__export-boundary').exists()).toBe(true);
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

  it('keeps the last statistics visible while reporting a failed refresh', async () => {
    let statisticsReads = 0;
    const mounted = await mountResults('/results?source=station', apiWith(async path => {
      if (path.startsWith('stations/results?')) return legacyStationPage();
      if (path.startsWith('stations/statistics')) {
        statisticsReads += 1;
        if (statisticsReads > 1) throw new ApiServerError(httpDetails(503));
        return statistics();
      }
      throw new Error(`Unexpected request: ${path}`);
    }));

    expect(mounted.wrapper.text()).toContain('执行成功');
    await mounted.wrapper.get('[data-testid="results-refresh"]').trigger('click');
    await flushPromises();

    expect(mounted.wrapper.text()).toContain('统计刷新失败');
    expect(mounted.wrapper.text()).toContain('当前显示上次成功读取的执行与判定统计');
    expect(mounted.wrapper.text()).toContain('执行成功');
    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });
});
