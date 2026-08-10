import { describe, expect, it } from 'vitest';
import {
  PreviewContractDecodeError,
  buildPreviewIdentityV1,
  decodePreviewNodeResponseV1,
  previewIdentityEquals
} from '@/capabilities/project-workspace/preview/previewContracts';

const projectId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const targetNodeId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const debugSessionId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const failedOperatorId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';

const identity = buildPreviewIdentityV1({
  projectId,
  targetNodeId,
  debugSessionId,
  clientRequestSequence: 7,
  flowRevision: 12
});

function observationFixture(overrides: Record<string, unknown> = {}) {
  return {
    schemaVersion: 'execution-observation.v1',
    observedAtUtc: '2026-07-17T08:00:00Z',
    identity,
    outcome: {
      success: true,
      executionTimeMs: 18,
      errorMessage: null,
      failedOperatorId: null,
      failedOperatorName: null,
      failedOperatorType: null,
      executedOperatorCount: 2
    },
    summary: [],
    detail: { kind: 'object', children: [] },
    diagnostics: [],
    truncated: false,
    ...overrides
  };
}

function responseFixture(overrides: Record<string, unknown> = {}) {
  return {
    success: true,
    projectId,
    targetNodeId,
    debugSessionId,
    executionTimeMs: 18,
    inputImageBase64: 'AQID',
    outputImageBase64: 'BAUG',
    outputData: { Score: 0.98, Tags: ['ok', 'stable'] },
    errorMessage: null,
    failedOperatorId: null,
    failedOperatorName: null,
    failedOperatorType: null,
    diagnostics: [{ code: 'preview-ready', message: 'Preview completed.', pathHint: '$' }],
    missingResources: [],
    artifacts: [{
      artifactId: 'A'.repeat(43),
      kind: 'image',
      role: 'outputImage',
      pathHint: '$.Image',
      contentType: 'image/png',
      length: 128,
      sha256: 'b'.repeat(64),
      createdAtUtc: '2026-07-17T08:00:00Z',
      expiresAtUtc: '2026-07-17T08:10:00Z',
      width: 640,
      height: 480,
      channels: 3
    }],
    observation: observationFixture(),
    ...overrides
  };
}

describe('G4 Preview response contracts', () => {
  it('decodes a fully identified response with bounded artifact references', () => {
    const decoded = decodePreviewNodeResponseV1(responseFixture(), identity);

    expect(decoded.success).toBe(true);
    expect(decoded.observation.identity).toEqual(identity);
    expect(decoded.outputData).toEqual({ Score: 0.98, Tags: ['ok', 'stable'] });
    expect(decoded.artifacts[0]).toMatchObject({
      artifactId: 'A'.repeat(43),
      contentType: 'image/png',
      length: 128,
      sha256: 'b'.repeat(64),
      role: 'outputImage',
      kind: 'image'
    });
  });

  it('requires the observation envelope and all five identity fields', () => {
    expect(() => decodePreviewNodeResponseV1(responseFixture({ observation: null })))
      .toThrow(PreviewContractDecodeError);

    const missingRevision = observationFixture({
      identity: { ...identity, flowRevision: undefined }
    });
    expect(() => decodePreviewNodeResponseV1(responseFixture({ observation: missingRevision })))
      .toThrow(PreviewContractDecodeError);
  });

  it('rejects root, observation, or active-request identity mismatches', () => {
    const wrongNodeObservation = observationFixture({
      identity: { ...identity, targetNodeId: failedOperatorId }
    });
    let nodeFailure: unknown;
    try {
      decodePreviewNodeResponseV1(responseFixture({ observation: wrongNodeObservation }));
    } catch (error) {
      nodeFailure = error;
    }
    expect(nodeFailure).toMatchObject({ path: '$.observation.identity.targetNodeId' });

    const newerIdentity = buildPreviewIdentityV1({ ...identity, clientRequestSequence: 8 });
    let sequenceFailure: unknown;
    try {
      decodePreviewNodeResponseV1(responseFixture(), newerIdentity);
    } catch (error) {
      sequenceFailure = error;
    }
    expect(sequenceFailure).toMatchObject({ path: '$.observation.identity.clientRequestSequence' });
  });

  it.each([
    ['artifact id', { artifactId: 'short' }],
    ['content type', { contentType: 'not-a-mime' }],
    ['length', { length: -1 }],
    ['sha256', { sha256: 'abcd' }],
    ['role', { role: '' }],
    ['kind', { kind: '' }]
  ])('rejects an artifact with invalid %s', (_label, artifactPatch) => {
    const artifact = {
      ...(responseFixture().artifacts as Record<string, unknown>[])[0],
      ...artifactPatch
    };
    expect(() => decodePreviewNodeResponseV1(responseFixture({ artifacts: [artifact] })))
      .toThrow(PreviewContractDecodeError);
  });

  it('accepts a schema-valid business failure with diagnostics, missing resources, and failed operator data', () => {
    const observation = observationFixture({
      outcome: {
        success: false,
        executionTimeMs: 9,
        errorMessage: 'Model resource is missing.',
        failedOperatorId,
        failedOperatorName: 'Inference',
        failedOperatorType: 'OnnxInference',
        executedOperatorCount: 1
      },
      diagnostics: [{ code: 'missing_model', message: 'Model resource is missing.', pathHint: '$.ModelPath' }]
    });
    const decoded = decodePreviewNodeResponseV1(responseFixture({
      success: false,
      executionTimeMs: 9,
      inputImageBase64: null,
      outputImageBase64: null,
      outputData: { PreviewBlocked: false },
      errorMessage: 'Model resource is missing.',
      failedOperatorId,
      failedOperatorName: 'Inference',
      failedOperatorType: 'OnnxInference',
      diagnostics: ['missing_model'],
      missingResources: [{
        resourceType: 'Model',
        resourceKey: 'OnnxInference.ModelPath',
        description: 'ONNX model',
        diagnosticCode: 'missing_model'
      }],
      artifacts: [],
      observation
    }), identity);

    expect(decoded.success).toBe(false);
    expect(decoded.failedOperatorId).toBe(failedOperatorId);
    expect(decoded.diagnostics[0]).toEqual({
      code: 'preview', message: 'missing_model', pathHint: null
    });
    expect(decoded.missingResources[0]?.resourceKey).toBe('OnnxInference.ModelPath');
  });

  it('accepts structured-only output without an image', () => {
    const decoded = decodePreviewNodeResponseV1(responseFixture({
      inputImageBase64: null,
      outputImageBase64: null,
      outputData: { Count: 4, Passed: true },
      artifacts: []
    }));

    expect(decoded.outputImageBase64).toBeNull();
    expect(decoded.outputData).toEqual({ Count: 4, Passed: true });
  });

  it('accepts a successful response with no previewable output', () => {
    const decoded = decodePreviewNodeResponseV1(responseFixture({
      inputImageBase64: null,
      outputImageBase64: null,
      outputData: null,
      diagnostics: [],
      artifacts: []
    }));

    expect(decoded.success).toBe(true);
    expect(decoded.outputData).toBeNull();
    expect(decoded.artifacts).toEqual([]);
  });

  it('builds canonical identities and compares UUIDs case-insensitively', () => {
    const uppercase = buildPreviewIdentityV1({
      ...identity,
      projectId: projectId.toUpperCase(),
      targetNodeId: targetNodeId.toUpperCase(),
      debugSessionId: debugSessionId.toUpperCase()
    });

    expect(uppercase).toEqual(identity);
    expect(previewIdentityEquals(uppercase, identity)).toBe(true);
    expect(previewIdentityEquals(uppercase, { ...identity, flowRevision: 13 })).toBe(false);
  });
});
