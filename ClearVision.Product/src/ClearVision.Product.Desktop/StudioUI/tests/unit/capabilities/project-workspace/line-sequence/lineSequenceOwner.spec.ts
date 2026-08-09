import { nextTick, reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import type { ApiTransport, ApiWriteOptions } from '@/platform/api';
import type { FlowCanvasOwner } from '@/capabilities/project-workspace/flow';
import {
  createLineSequenceOwner,
  decodeLineSequenceAnalysisV1,
  decodeLineSequenceRecommendationV1,
  LineSequenceContractDecodeError,
  resolveLineSequenceParameterPatch
} from '@/capabilities/project-workspace/line-sequence';

const projectId = '11111111-1111-4111-8111-111111111111';
const judgeNodeId = 'judge-node';
type ApiPost = NonNullable<ApiTransport['post']>;

function analysisPayload() {
  return {
    success: true,
    targetNodeId: judgeNodeId,
    metrics: { overallScore: 0.72 },
    diagnosticCodes: ['sequence_mismatch'],
    suggestions: [],
    missingResources: [],
    errorMessage: null
  };
}

function recommendationPayload(finalParameters: Readonly<Record<string, number>> = {
  'BoxNms.ScoreThreshold': 0.64,
  'BoxNms.IouThreshold': 0.38
}) {
  return {
    success: false,
    scenarioKey: 'wire-sequence-terminal',
    finalParameters,
    totalIterations: 5,
    totalExecutionTimeMs: 31,
    isGoalAchieved: false,
    diagnosticCodes: ['sequence_mismatch'],
    missingResources: [],
    errorMessage: null
  };
}

function createHarness(post: ApiPost, judgeType: string | number = 'DetectionSequenceJudge') {
  const projection = reactive({
    mutationGate: 'editable' as const,
    runtime: {
      selectedNodeId: judgeNodeId as string | null,
      selectedNodeIds: [judgeNodeId],
      selectedConnectionId: null,
      nodeCount: 3,
      connectionCount: 2,
      flowRevision: 7,
      selectionRevision: 3,
      viewRevision: 0,
      zoom: 1,
      panX: 0,
      panY: 0
    },
    draft: {
      id: 'flow-1',
      name: 'Line sequence flow',
      operators: [
        { id: 'deep-node', type: 'DeepLearning', parameters: [] },
        { id: 'box-node', type: 'BoxNms', parameters: [] },
        { id: judgeNodeId, type: judgeType, parameters: [] }
      ],
      connections: [
        { id: 'c1', sourceOperatorId: 'deep-node', targetOperatorId: 'box-node' },
        { id: 'c2', sourceOperatorId: 'box-node', targetOperatorId: judgeNodeId }
      ],
      decisionConfiguration: null,
      opaquePassthrough: {}
    }
  });
  const patchNodeParameters = vi.fn(() => {
    projection.runtime.flowRevision += 1;
    projection.runtime.selectionRevision += 1;
    return {
      ok: true,
      code: 'ok',
      message: 'ok',
      flowRevision: projection.runtime.flowRevision
    };
  });
  const flowOwner = {
    projection,
    commands: { patchNodeParameters }
  } as unknown as FlowCanvasOwner;
  const api: ApiTransport = {
    apiBaseUrl: 'http://localhost:5000/api',
    get: vi.fn(async () => undefined),
    post
  };
  const owner = createLineSequenceOwner({
    projectId,
    flowOwner,
    api,
    getRecentImageBase64: () => 'AQID'
  });
  return { owner, projection, patchNodeParameters };
}

describe('Line sequence contracts', () => {
  it('rejects malformed analysis responses at the boundary', () => {
    expect(() => decodeLineSequenceAnalysisV1({
      ...analysisPayload(),
      diagnosticCodes: 'sequence_mismatch'
    })).toThrow(LineSequenceContractDecodeError);
  });

  it('decodes preview images and final scenario previews without making them authoritative', () => {
    const analysis = decodeLineSequenceAnalysisV1({
      ...analysisPayload(),
      inputImageBase64: 'data:image/png;base64,AQID',
      previewImageBase64: 'BAUG',
      outputs: { sequence: ['red', 'black'] }
    });
    expect(analysis.preview).toEqual({
      inputImageBase64: 'AQID',
      previewImageBase64: 'BAUG',
      outputs: { sequence: ['red', 'black'] }
    });
    expect(decodeLineSequenceRecommendationV1({
      ...recommendationPayload(),
      finalPreview: { inputImageBase64: 'AQID', previewImageBase64: 'BAUG', outputs: {} }
    }).finalPreview).toMatchObject({ inputImageBase64: 'AQID', previewImageBase64: 'BAUG' });
  });

  it('selects the nearest upstream BoxNms node deterministically', () => {
    const flow = {
      id: 'flow-1',
      name: 'Flow',
      operators: [
        { id: 'box-far', type: 'BoxNms' },
        { id: 'bridge', type: 'Transform' },
        { id: 'box-near', type: 'BoxNms' },
        { id: judgeNodeId, type: 'DetectionSequenceJudge' }
      ],
      connections: [
        { sourceOperatorId: 'box-far', targetOperatorId: 'bridge' },
        { sourceOperatorId: 'bridge', targetOperatorId: 'box-near' },
        { sourceOperatorId: 'box-near', targetOperatorId: judgeNodeId }
      ],
      decisionConfiguration: null,
      opaquePassthrough: {}
    };

    expect(resolveLineSequenceParameterPatch(flow, judgeNodeId, {
      'BoxNms.ScoreThreshold': 0.61
    })).toEqual({
      nodeId: 'box-near',
      operatorType: 'BoxNms',
      values: { ScoreThreshold: 0.61 }
    });
  });

  it('resolves persisted numeric BoxNms and DeepLearning identities', () => {
    const flow = {
      id: 'flow-1',
      name: 'Flow',
      operators: [
        { id: 'deep-node', type: 10 },
        { id: 'box-node', type: 140 },
        { id: judgeNodeId, type: 61 }
      ],
      connections: [
        { sourceOperatorId: 'deep-node', targetOperatorId: 'box-node' },
        { sourceOperatorId: 'box-node', targetOperatorId: judgeNodeId }
      ],
      decisionConfiguration: null,
      opaquePassthrough: {}
    };

    expect(resolveLineSequenceParameterPatch(flow, judgeNodeId, {
      'BoxNms.ScoreThreshold': 0.62
    })).toEqual({
      nodeId: 'box-node',
      operatorType: 'BoxNms',
      values: { ScoreThreshold: 0.62 }
    });
    expect(resolveLineSequenceParameterPatch(flow, judgeNodeId, {
      'DeepLearning.Confidence': 0.81
    })).toEqual({
      nodeId: 'deep-node',
      operatorType: 'DeepLearning',
      values: { Confidence: 0.81 }
    });
  });

  it('only accepts allowlisted finite values in the inclusive zero-to-one range', () => {
    const flow = {
      id: 'flow-1',
      name: 'Flow',
      operators: [
        { id: 'deep-node', type: 'DeepLearning' },
        { id: 'box-node', type: 'BoxNms' },
        { id: judgeNodeId, type: 'DetectionSequenceJudge' }
      ],
      connections: [
        { sourceOperatorId: 'deep-node', targetOperatorId: 'box-node' },
        { sourceOperatorId: 'box-node', targetOperatorId: judgeNodeId }
      ],
      decisionConfiguration: null,
      opaquePassthrough: {}
    };

    expect(resolveLineSequenceParameterPatch(flow, judgeNodeId, {
      'BoxNms.ScoreThreshold': 1.1,
      'BoxNms.IouThreshold': 0.4,
      'DeepLearning.Confidence': 0.8,
      'Device.WriteValue': 1
    })).toEqual({
      nodeId: 'box-node',
      operatorType: 'BoxNms',
      values: { IouThreshold: 0.4 }
    });
    expect(resolveLineSequenceParameterPatch(flow, judgeNodeId, {
      'BoxNms.ScoreThreshold': -0.1,
      'DeepLearning.Confidence': 0.8
    })).toEqual({
      nodeId: 'deep-node',
      operatorType: 'DeepLearning',
      values: { Confidence: 0.8 }
    });
    expect(resolveLineSequenceParameterPatch(flow, judgeNodeId, {
      'Device.WriteValue': 1
    })).toBeNull();
  });
});

describe('LineSequenceOwner', () => {
  it('recognizes the persisted numeric DetectionSequenceJudge identity', () => {
    const post = vi.fn(async () => analysisPayload());
    const harness = createHarness(post as unknown as ApiPost, 61);

    expect(harness.owner.projection).toMatchObject({ available: true, selectedNodeId: judgeNodeId });
    expect(harness.owner.projection.canAnalyze).toBe(true);
    harness.owner.dispose();
  });

  it('runs Analyze and Recommend, then applies one canonical draft patch', async () => {
    const post = vi.fn(async (path: string) => {
      if (path === 'autotune/flow-node/preview') return analysisPayload();
      if (path === 'autotune/scenario') return recommendationPayload();
      throw new Error(`Unexpected POST ${path}`);
    });
    const harness = createHarness(post as unknown as ApiPost);

    await harness.owner.analyze();
    expect(harness.owner.projection).toMatchObject({ phase: 'analyzed', canRecommend: true });
    await harness.owner.recommend();
    expect(harness.owner.projection).toMatchObject({ phase: 'recommended', canApply: true });

    harness.owner.applyRecommendation();

    expect(harness.patchNodeParameters).toHaveBeenCalledTimes(1);
    expect(harness.patchNodeParameters).toHaveBeenCalledWith({
      nodeId: 'box-node',
      values: { ScoreThreshold: 0.64, IouThreshold: 0.38 }
    });
    await nextTick();
    expect(harness.owner.projection.phase).toBe('applied');
    harness.owner.dispose();
  });

  it('sends the recent preview image and projects the backend preview image', async () => {
    let requestBody: Readonly<Record<string, unknown>> | null = null;
    const post = vi.fn(async (path: string, body: unknown) => {
      requestBody = body as Readonly<Record<string, unknown>>;
      return path === 'autotune/flow-node/preview'
        ? { ...analysisPayload(), inputImageBase64: 'AQID', previewImageBase64: 'BAUG', outputs: { count: 2 } }
        : recommendationPayload();
    });
    const harness = createHarness(post as unknown as ApiPost);

    await harness.owner.analyze();

    expect(requestBody).toMatchObject({ inputImageBase64: 'AQID', targetNodeId: judgeNodeId });
    expect(harness.owner.projection.preview).toEqual({
      inputImageBase64: 'AQID',
      previewImageBase64: 'BAUG',
      outputs: { count: 2 }
    });
    harness.owner.dispose();
  });

  it('marks a recommendation stale after the flow revision changes and blocks apply', async () => {
    const post = vi.fn(async (path: string) => path === 'autotune/flow-node/preview'
      ? analysisPayload()
      : recommendationPayload());
    const harness = createHarness(post as unknown as ApiPost);
    await harness.owner.analyze();
    await harness.owner.recommend();

    harness.projection.runtime.flowRevision += 1;
    harness.projection.runtime.selectionRevision += 1;
    await nextTick();
    harness.owner.applyRecommendation();

    expect(harness.owner.projection).toMatchObject({ phase: 'stale', canApply: false });
    expect(harness.patchNodeParameters).not.toHaveBeenCalled();
    harness.owner.dispose();
  });

  it('aborts and drops a late analysis after selection changes', async () => {
    let resolvePost: ((value: unknown) => void) | undefined;
    let signal: AbortSignal | undefined;
    const post = vi.fn(async (_path: string, _body: unknown, options: ApiWriteOptions = {}) => {
      signal = options.signal;
      return await new Promise<unknown>(resolve => { resolvePost = resolve; });
    });
    const harness = createHarness(post as unknown as ApiPost);

    const analyzing = harness.owner.analyze();
    harness.projection.runtime.selectedNodeId = 'box-node';
    harness.projection.runtime.selectionRevision += 1;
    await nextTick();

    expect(signal?.aborted).toBe(true);
    resolvePost?.(analysisPayload());
    await analyzing;
    expect(harness.owner.projection).toMatchObject({ phase: 'idle', available: false, analysis: null });
    harness.owner.dispose();
  });

  it('aborts and drops a late recommendation after disposal', async () => {
    let resolveRecommendation: ((value: unknown) => void) | undefined;
    let recommendationSignal: AbortSignal | undefined;
    const post = vi.fn(async (path: string, _body: unknown, options: ApiWriteOptions = {}) => {
      if (path === 'autotune/flow-node/preview') return analysisPayload();
      recommendationSignal = options.signal;
      return await new Promise<unknown>(resolve => { resolveRecommendation = resolve; });
    });
    const harness = createHarness(post as unknown as ApiPost);
    await harness.owner.analyze();

    const recommending = harness.owner.recommend();
    harness.owner.dispose('unit-test');
    expect(recommendationSignal?.aborted).toBe(true);
    resolveRecommendation?.(recommendationPayload());
    await recommending;

    expect(harness.owner.projection.phase).toBe('disposed');
    expect(harness.patchNodeParameters).not.toHaveBeenCalled();
  });
});
