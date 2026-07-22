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
      projectPersistenceRevision: 17,
      decisionConfigurationHash: 'decision-hash',
      packageId: null,
      stationId: null
    },
    hasEvidenceManifest: false,
    evidenceStatus: 'missing',
    evidenceManifestReference: `/api/inspection/history/${projectId}/${resultId}/evidence/manifest`,
    evidenceTotalBytes: null,
    retentionExpiresAtUtc: null,
    evidenceMessage: '证据清单缺失或已清理'
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
      projectRevision: 9,
      outcome: 2,
      inspectionStatus: 'Error',
      executionTimeMs: 88,
      diagnosticCode: 'TEXT_SAYS_NG',
      diagnosticMessage: '文案写 NG 也不得折叠',
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
    routes: [{ path: '/results', component: { template: '<div />' } }]
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
        throw new Error(`Unexpected request: ${path}`);
      })
    );

    expect(mounted.wrapper.text()).toContain('不适用');
    expect(mounted.wrapper.text()).toContain('执行成功');
    expect(mounted.wrapper.text()).toContain('判定结果不适用');
    expect(mounted.wrapper.text()).toContain('轻微划痕');
    expect(mounted.wrapper.text()).toContain('流程版本哈希');
    expect(mounted.wrapper.text()).toContain('manifest-deep-link');
    expect(requested).toContain(`inspection/history/${projectId}/${resultId}/evidence/manifest`);
    expect(mounted.wrapper.text()).not.toContain('图像预览');

    mounted.wrapper.unmount();
    mounted.queries.dispose();
  });

  it('marks legacy Station mapping and never folds Error into NG', async () => {
    const mounted = await mountResults(
      '/results?source=station&resultId=message-9',
      apiWith(async path => {
        if (path.startsWith('stations/results?')) return legacyStationPage();
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
