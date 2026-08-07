import { describe, expect, it } from 'vitest';
import {
  CalibrationContractDecodeError,
  decodeCalibrationAssetSaveResponse,
  decodeNPointCalibrationSolveResponse
} from '@/capabilities/project-workspace/calibration/calibrationContracts';

const projectId = '11111111-1111-4111-8111-111111111111';
const nodeId = '22222222-2222-4222-8222-222222222222';

describe('calibration contracts', () => {
  it('decodes a successful solve with nullable residual fields and a candidate bundle', () => {
    const decoded = decodeNPointCalibrationSolveResponse({
      schemaVersion: 'calibration-draft-session.v1',
      sessionId: 'calibration-draft-1',
      projectId,
      targetNodeId: nodeId,
      imageIdentity: 'preview-1',
      mode: 'Perspective',
      unit: 'mm',
      success: true,
      samples: [{
        sampleId: 'sample-1',
        order: 1,
        pixelX: 10,
        pixelY: 20,
        worldX: 1,
        worldY: 2,
        enabled: true,
        valid: true,
        inlier: true,
        reprojectionX: 10.1,
        reprojectionY: 20.2,
        error: 0.22,
        source: 'ManualClick',
        note: '',
        createdAtUtc: '2026-08-07T00:00:00Z'
      }],
      lastSolveResult: { accepted: true, inlierCount: 1, totalSampleCount: 1 },
      candidateBundle: { schemaVersion: 'calibration-candidate-bundle.v1' },
      candidateBundleJson: '{"schemaVersion":"calibration-candidate-bundle.v1"}',
      diagnostics: []
    });

    expect(decoded.mode).toBe('Perspective');
    expect(decoded.samples[0]?.valid).toBe(true);
    expect(decoded.lastSolveResult?.accepted).toBe(true);
  });

  it('normalizes planar scale-offset mode aliases to the shared UI mode', () => {
    const decoded = decodeNPointCalibrationSolveResponse({
      sessionId: 'calibration-draft-planar',
      projectId,
      targetNodeId: nodeId,
      mode: 'PlanarScaleOffset',
      success: true,
      samples: [],
      candidateBundle: { schemaVersion: 'calibration-candidate-bundle.v1' },
      candidateBundleJson: '{"schemaVersion":"calibration-candidate-bundle.v1"}'
    });

    expect(decoded.mode).toBe('ScaleOffset');
  });

  it('fails closed when a successful solve omits its candidate bundle', () => {
    expect(() => decodeNPointCalibrationSolveResponse({
      sessionId: 'calibration-draft-1',
      projectId,
      targetNodeId: nodeId,
      success: true,
      samples: []
    })).toThrow(CalibrationContractDecodeError);
  });

  it('decodes the formal asset response from the shared project asset contract', () => {
    const decoded = decodeCalibrationAssetSaveResponse({
      projectId,
      persistenceRevision: 12,
      assetsHash: 'sha256:assets',
      asset: {
        assetId: 'calibration-1',
        contentHash: 'sha256:content',
        projectRevision: 12
      }
    });

    expect(decoded.assetId).toBe('calibration-1');
    expect(decoded.persistenceRevision).toBe(12);
    expect(decoded.contentHash).toBe('sha256:content');
  });
});
