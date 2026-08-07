import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import { ApiForbiddenError, type ApiTransport, type ApiWriteOptions } from '@/platform/api';
import type { FlowCanvasOwner } from '@/capabilities/project-workspace/flow';
import type { ImageCanvasClick, ImageCanvasOwner } from '@/capabilities/project-workspace/image/imageCanvasOwner';
import { createCalibrationOwner } from '@/capabilities/project-workspace/calibration/calibrationOwner';

const projectId = '11111111-1111-4111-8111-111111111111';
const nodeId = '22222222-2222-4222-8222-222222222222';

function point(sampleId: string, x: number, y: number, worldX: number, worldY: number) {
  return { sampleId, ImageX: x, ImageY: y, WorldX: worldX, WorldY: worldY, Enabled: true };
}

function createHarness(post: ApiTransport['post'] = async () => undefined, calibrationMode = 'Affine') {
  const flowProjection = reactive({
    mutationGate: 'editable' as const,
    runtime: {
      selectedNodeId: nodeId,
      selectionRevision: 0,
      flowRevision: 1
    },
    draft: {
      operators: [{
        id: nodeId,
        type: 'NPointCalibration',
        parameters: [
          { name: 'CalibrationMode', value: calibrationMode },
          { name: 'CalibrationUnit', value: 'mm' },
          { name: 'PointPairs', value: JSON.stringify([
            point('sample-1', 10, 20, 1, 2),
            point('sample-2', 30, 40, 3, 4),
            point('sample-3', 50, 60, 5, 6)
          ]) }
        ]
      }]
    }
  });
  const imageProjection = reactive({
    phase: 'ready' as const,
    imageIdentity: 'preview-1' as string | null,
    imageGeneration: 1
  });
  const listeners = new Set<(click: ImageCanvasClick) => void>();
  const flowOwner = { projection: flowProjection } as unknown as FlowCanvasOwner;
  const imageOwner = {
    projection: imageProjection,
    subscribeImageClick(listener: (click: ImageCanvasClick) => void) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    }
  } as unknown as ImageCanvasOwner;
  const api: ApiTransport = {
    apiBaseUrl: 'http://localhost:5000/api',
    get: vi.fn(async () => undefined),
    post
  };
  const inspectorOwner = {} as never;
  const owner = createCalibrationOwner({
    projectId,
    flowOwner,
    inspectorOwner,
    imageOwner,
    api,
    getPersistenceRevision: () => 10,
    reconcileAfterSave: async () => true
  });
  return { owner, flowProjection, imageProjection, api, listeners };
}

function solveResponse(sessionId = 'calibration-draft-session') {
  return {
    schemaVersion: 'calibration-draft-session.v1',
    sessionId,
    projectId,
    targetNodeId: nodeId,
    imageIdentity: 'preview-1',
    mode: 'Affine',
    unit: 'mm',
    success: true,
    samples: [
      { sampleId: 'sample-1', order: 1, pixelX: 10, pixelY: 20, worldX: 1, worldY: 2, source: 'Imported', enabled: true, valid: true, inlier: true, reprojectionX: 10, reprojectionY: 20, error: 0, note: '', createdAtUtc: '2026-08-07T00:00:00Z' },
      { sampleId: 'sample-2', order: 2, pixelX: 30, pixelY: 40, worldX: 3, worldY: 4, source: 'Imported', enabled: true, valid: true, inlier: true, reprojectionX: 30, reprojectionY: 40, error: 0, note: '', createdAtUtc: '2026-08-07T00:00:00Z' },
      { sampleId: 'sample-3', order: 3, pixelX: 50, pixelY: 60, worldX: 5, worldY: 6, source: 'Imported', enabled: true, valid: true, inlier: true, reprojectionX: 50, reprojectionY: 60, error: 0, note: '', createdAtUtc: '2026-08-07T00:00:00Z' }
    ],
    lastSolveResult: { accepted: true, inlierCount: 3, totalSampleCount: 3, inlierRatio: 1 },
    candidateBundle: { schemaVersion: 'calibration-candidate-bundle.v1' },
    candidateBundleJson: '{"schemaVersion":"calibration-candidate-bundle.v1"}',
    diagnostics: []
  };
}

describe('CalibrationOwner', () => {
  it('reads direct legacy PointPairs, edits incomplete World coordinates, and keeps a stable draft session', () => {
    const harness = createHarness();
    expect(harness.owner.projection.samples).toHaveLength(3);
    expect(harness.owner.projection.samples[0]).toMatchObject({ pixelX: 10, pixelY: 20, worldX: 1, worldY: 2 });

    harness.owner.addSample({ pixelX: 70, pixelY: 80 });
    const added = harness.owner.projection.samples[3]!;
    expect(added.valid).toBe(false);
    expect(harness.owner.projection.canSolve).toBe(true);

    harness.owner.updateSample(added.sampleId, { worldX: 7, worldY: 8 });
    expect(harness.owner.projection.samples[3]).toMatchObject({ worldX: 7, worldY: 8, valid: true });

    harness.owner.dispose();
  });

  it('projects ScaleOffset mode and requires three complete samples before solving', () => {
    const harness = createHarness(async () => undefined, 'PlanarScaleOffset');

    expect(harness.owner.projection.mode).toBe('ScaleOffset');
    expect(harness.owner.projection.canSolve).toBe(true);
    harness.owner.removeSample('sample-3');
    expect(harness.owner.projection.canSolve).toBe(false);
    harness.owner.dispose();
  });

  it('marks a dirty draft stale when the image identity changes instead of reloading it', async () => {
    const harness = createHarness();
    harness.owner.addSample({ pixelX: 70, pixelY: 80 });
    const sampleCount = harness.owner.projection.samples.length;

    harness.imageProjection.imageIdentity = 'preview-2';
    harness.imageProjection.imageGeneration = 2;
    await Promise.resolve();

    expect(harness.owner.projection.phase).toBe('stale');
    expect(harness.owner.projection.samples).toHaveLength(sampleCount);
    expect(harness.owner.projection.canCapture).toBe(false);
    harness.owner.reset();
    expect(harness.owner.projection.phase).toBe('ready');
    harness.owner.dispose();
  });

  it('drops a late solve response after dispose and aborts its request', async () => {
    let resolvePost: ((value: unknown) => void) | undefined;
    let signal: AbortSignal | undefined;
    const post = vi.fn(async (_path: string, _body: unknown, options: ApiWriteOptions = {}) => {
      signal = options.signal;
      return await new Promise<unknown>(resolve => { resolvePost = resolve; });
    });
    const harness = createHarness(post as unknown as ApiTransport['post']);
    const solving = harness.owner.solve();
    expect(signal).toBeDefined();

    harness.owner.dispose('unit-dispose');
    expect(signal?.aborted).toBe(true);
    resolvePost?.(solveResponse());
    await solving;
    expect(harness.owner.projection.phase).toBe('disposed');
  });

  it('projects draft solve permission failures separately from formal asset save failures', async () => {
    const post = vi.fn(async () => {
      throw new ApiForbiddenError({
        url: 'http://localhost:5000/api/calibration/npoint-draft/solve',
        status: 403,
        statusText: 'Forbidden',
        payload: { code: 'EngineerOrAdminRequired' },
        responseBody: '{"code":"EngineerOrAdminRequired"}'
      });
    });
    const harness = createHarness(post as unknown as ApiTransport['post']);

    await harness.owner.solve();

    expect(harness.owner.projection.phase).toBe('error');
    expect(harness.owner.projection.message).toBe('当前账户没有执行标定计算的权限。');
    harness.owner.dispose();
  });

  it('saves an accepted candidate through the project asset endpoint and reconciles once', async () => {
    const calls: Array<{ path: string; body: unknown }> = [];
    const post = vi.fn(async (path: string, body: unknown) => {
      calls.push({ path, body });
      if (path === 'calibration/npoint-draft/solve') return solveResponse();
      return {
        projectId,
        persistenceRevision: 11,
        assetsHash: 'sha256:assets',
        asset: { assetId: 'calibration-1', contentHash: 'sha256:content', projectRevision: 11 }
      };
    });
    const harness = createHarness(post as unknown as ApiTransport['post']);
    await harness.owner.solve();
    const sessionId = (calls[0]?.body as { sessionId: string }).sessionId;
    await harness.owner.save();

    expect(calls[1]?.path).toBe(`projects/${projectId}/calibration-assets/from-draft`);
    expect((calls[1]?.body as { sessionId: string }).sessionId).toBe(sessionId);
    expect(harness.owner.projection).toMatchObject({ phase: 'saved', formalAssetId: 'calibration-1' });
    harness.owner.dispose();
  });
});
