import { describe, expect, it } from 'vitest';
import {
  decodeInspectionHistoryComparison,
  decodeInspectionPreviousSuccess,
  decodeLocalInspectionResultDetail,
  decodeLocalInspectionResultPage,
  decodeResultsOutcomeStatistics,
  decodeStationInspectionResultPage,
  projectLegacyStationOutcome,
  ResultsContractDecodeError
} from '@/capabilities/results-read';
import {
  canonicalInspectionOutcomeKinds,
  classifyInspectionOutcome,
  type InspectionOutcome
} from '@/shared/inspectionOutcome';

const projectId = '11111111-1111-4111-8111-111111111111';
const resultId = '22222222-2222-4222-8222-222222222222';
const defectId = '33333333-3333-4333-8333-333333333333';
const sessionId = '44444444-4444-4444-8444-444444444444';

function localSummary(outcome: InspectionOutcome = { execution: 'Succeeded', decision: 'Ok' }) {
  return {
    id: resultId,
    resultId,
    projectId,
    status: 'OK',
    executionOutcome: outcome.execution,
    decisionOutcome: outcome.decision,
    decisionSource: 'FinalDecision',
    reasonCode: 'FINAL_DECISION_OK',
    hasJudgmentSignal: true,
    defectCount: 0,
    processingTime: 12,
    processingTimeMs: 12,
    timestamp: '2026-07-15T01:00:02Z',
    inspectionTime: '2026-07-15T01:00:02Z',
    startedAt: '2026-07-15T01:00:01Z',
    completedAt: '2026-07-15T01:00:02Z',
    confidenceScore: null,
    flowVersionHash: 'flow-hash',
    calibrationBundleId: null,
    sessionId,
    runId: null,
    diagnosticCode: 'FINAL_DECISION_OK',
    diagnosticMessage: '完成',
    errorMessage: null
  };
}

function stationSummary(overrides: Record<string, unknown> = {}) {
  return {
    schemaVersion: 2,
    stationId: 'station-a',
    lineName: 'line-a',
    sequenceId: 7,
    messageId: 'message-7',
    runId: 'station-run-7',
    packageId: 'package-a',
    packageName: '瓶盖检测',
    packageVersion: '1.2.0',
    packageFlowHash: 'package-flow',
    executionFlowHash: 'execution-flow',
    flowHash: 'execution-flow',
    executionSnapshotId: '55555555-5555-4555-8555-555555555555',
    projectRevision: 12,
    decisionConfigurationHash: 'decision-hash',
    executionRunMode: 'StationRuntime',
    outcome: 'Ok',
    inspectionStatus: 'OK',
    executionOutcome: 'Succeeded',
    decisionOutcome: 'Ok',
    hasJudgmentSignal: true,
    decisionSource: 'FinalDecision',
    reasonCode: 'FINAL_DECISION_OK',
    executionTimeMs: 34,
    diagnosticCode: 'OK',
    diagnosticMessage: null,
    primaryOutputsPreview: { score: '0.91' },
    startedAtUtc: '2026-07-15T01:00:00Z',
    completedAtUtc: '2026-07-15T01:00:01Z',
    createdAtUtc: '2026-07-15T01:00:01Z',
    ...overrides
  };
}

const canonicalCases: readonly InspectionOutcome[] = Object.freeze([
  { execution: 'Succeeded', decision: 'Ok' },
  { execution: 'Succeeded', decision: 'Ng' },
  { execution: 'Succeeded', decision: 'Undetermined' },
  { execution: 'Succeeded', decision: 'NotApplicable' },
  { execution: 'Succeeded', decision: 'Invalid' },
  { execution: 'Failed', decision: 'Undetermined' },
  { execution: 'Cancelled', decision: 'NotApplicable' },
  { execution: 'TimedOut', decision: 'Undetermined' },
  { execution: 'Skipped', decision: 'NotApplicable' }
]);

describe('Results contracts', () => {
  it('preserves all nine local and Station canonical outcomes with parity', () => {
    const localKinds = canonicalCases.map(outcome => {
      const page = decodeLocalInspectionResultPage({
        items: [localSummary(outcome)],
        totalCount: 1,
        pageIndex: 0,
        pageSize: 20
      });
      return classifyInspectionOutcome(page.items[0]!.outcome);
    });
    const stationKinds = canonicalCases.map(outcome => {
      const page = decodeStationInspectionResultPage({
        items: [stationSummary({
          executionOutcome: outcome.execution,
          decisionOutcome: outcome.decision
        })],
        totalCount: 1,
        pageIndex: 0,
        pageSize: 20
      });
      return classifyInspectionOutcome(page.items[0]!.outcome);
    });

    expect(localKinds).toEqual(canonicalInspectionOutcomeKinds);
    expect(stationKinds).toEqual(canonicalInspectionOutcomeKinds);
    expect(localKinds).toEqual(stationKinds);
  });

  it('copies only the backend Station legacy read-time projection and marks it', () => {
    expect(projectLegacyStationOutcome('Ok')).toEqual({ execution: 'Succeeded', decision: 'Ok' });
    expect(projectLegacyStationOutcome('Ng')).toEqual({ execution: 'Succeeded', decision: 'Ng' });
    expect(projectLegacyStationOutcome('Error')).toEqual({ execution: 'Failed', decision: 'Undetermined' });
    expect(projectLegacyStationOutcome('Canceled')).toEqual({ execution: 'Cancelled', decision: 'NotApplicable' });
    expect(projectLegacyStationOutcome('Undetermined')).toEqual({ execution: 'Succeeded', decision: 'Undetermined' });

    const payload = stationSummary({
      executionOutcome: undefined,
      decisionOutcome: undefined,
      outcome: 2,
      diagnosticCode: 'TEXT_SAYS_NG',
      diagnosticMessage: '诊断文案包含 NG，但不得据此推断'
    });
    const result = decodeStationInspectionResultPage({
      items: [payload],
      totalCount: 1,
      pageIndex: 0,
      pageSize: 20
    }).items[0]!;

    expect(result).toMatchObject({
      outcome: { execution: 'Failed', decision: 'Undetermined' },
      legacyProjection: true,
      decisionSource: 'LegacyStationResult',
      reasonCode: 'LegacyStationOutcomeProjection'
    });

    const numericKinds = [0, 1, 2, 3, 4].map(outcome => {
      const decoded = decodeStationInspectionResultPage({
        items: [stationSummary({
          executionOutcome: undefined,
          decisionOutcome: undefined,
          outcome
        })],
        totalCount: 1,
        pageIndex: 0,
        pageSize: 20
      }).items[0]!;
      return classifyInspectionOutcome(decoded.outcome);
    });
    expect(numericKinds).toEqual(['Ok', 'Ng', 'Failed', 'Cancelled', 'Undetermined']);
  });

  it('decodes scalar local detail and the evidence summary contract', () => {
    const detail = decodeLocalInspectionResultDetail({
      ...localSummary(),
      defects: [{
        id: defectId,
        type: 'Scratch',
        x: 1,
        y: 2,
        width: 3,
        height: 4,
        confidenceScore: 0.92,
        description: '轻微划痕',
        annotationData: null
      }],
      traceability: {
        flowVersionHash: 'flow-hash',
        calibrationBundleId: null,
        sessionId,
        runId: null,
        executionSnapshotId: '55555555-5555-4555-8555-555555555555',
        projectPersistenceRevision: 17,
        decisionConfigurationHash: 'decision-hash',
        packageId: 'package-17',
        runtimePackageId: 'package-17',
        executionSource: 'PersistedProject',
        executionRunMode: 'FormalPrimary',
        shadowRole: 'Primary',
        stationId: null
      },
      hasImage: true,
      imageReference: '/api/images/55555555-5555-4555-8555-555555555555',
      imageMissing: false,
      imageMissingMessage: null,
      hasOutputData: true,
      hasAnalysisData: true,
      hasEvidenceManifest: true,
      evidenceStatus: 'available',
      evidenceManifestReference: '/api/inspection/history/project/result/evidence/manifest',
      evidenceTotalBytes: 1024,
      retentionExpiresAtUtc: '2026-08-15T01:00:02Z',
      evidenceMessage: '证据清单可用'
    });

    expect(detail.defects).toEqual([{
      id: defectId,
      type: 'Scratch',
      confidenceScore: 0.92,
      description: '轻微划痕'
    }]);
    expect(detail).toMatchObject({
      hasEvidenceManifest: true,
      evidenceStatus: 'available',
      evidenceTotalBytes: 1024,
      traceability: {
        sessionId,
        runId: null,
        executionSnapshotId: '55555555-5555-4555-8555-555555555555',
        projectPersistenceRevision: 17,
        decisionConfigurationHash: 'decision-hash',
        runtimePackageId: 'package-17',
        executionRunMode: 'FormalPrimary'
      },
      imageReference: '/api/images/55555555-5555-4555-8555-555555555555'
    });

    expect(() => decodeLocalInspectionResultDetail({
      ...localSummary(),
      defects: [],
      traceability: detail.traceability,
      hasImage: false,
      imageReference: null,
      imageMissing: false,
      imageMissingMessage: null,
      hasOutputData: false,
      hasAnalysisData: false,
      hasEvidenceManifest: false,
      evidenceStatus: 'missing',
      evidenceManifestReference: null,
      evidenceTotalBytes: null,
      retentionExpiresAtUtc: null,
      evidenceMessage: null
    }, {
      projectId,
      resultId: crypto.randomUUID()
    })).toThrow(ResultsContractDecodeError);
  });

  it('round-trips full Station identity and marks remote image evidence as not uploaded', () => {
    const result = decodeStationInspectionResultPage({
      items: [stationSummary()], totalCount: 1, pageIndex: 0, pageSize: 20
    }).items[0]!;

    expect(result).toMatchObject({
      packageFlowHash: 'package-flow',
      executionFlowHash: 'execution-flow',
      executionSnapshotId: '55555555-5555-4555-8555-555555555555',
      projectRevision: 12,
      decisionConfigurationHash: 'decision-hash',
      executionRunMode: 'StationRuntime',
      primaryOutputsPreview: { score: '0.91' },
      remoteImageAvailability: 'not-uploaded'
    });
    expect(result).not.toHaveProperty('imageReference');

    const legacyIdentity = decodeStationInspectionResultPage({
      items: [stationSummary({
        executionOutcome: undefined,
        decisionOutcome: undefined,
        packageFlowHash: undefined,
        executionFlowHash: undefined,
        flowHash: undefined,
        executionSnapshotId: undefined,
        projectRevision: undefined,
        decisionConfigurationHash: undefined,
        executionRunMode: undefined
      })],
      totalCount: 1,
      pageIndex: 0,
      pageSize: 20
    }).items[0]!;
    expect(legacyIdentity).toMatchObject({
      legacyProjection: true,
      packageFlowHash: null,
      executionFlowHash: null,
      executionSnapshotId: null,
      projectRevision: null,
      decisionConfigurationHash: null,
      executionRunMode: null
    });
  });

  it('decodes authoritative dual-denominator statistics and same-project investigation responses', () => {
    expect(decodeResultsOutcomeStatistics({
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
      averageProcessingTimeMs: 12.5
    })).toMatchObject({
      totalAttemptCount: 10,
      executionSucceededCount: 8,
      validDecisionCount: 6,
      yieldRate: 5 / 6,
      decisionCoverageRate: 0.75
    });

    const referenceId = '66666666-6666-4666-8666-666666666666';
    const comparisonSummary = (id: string, decision: 'Ok' | 'Ng') => ({
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
      sessionId,
      runId: null,
      imageReference: null,
      hasImage: false,
      hasOutputData: true,
      hasAnalysisData: false
    });
    const previous = decodeInspectionPreviousSuccess({
      currentSummary: comparisonSummary(resultId, 'Ng'),
      referenceSummary: comparisonSummary(referenceId, 'Ok'),
      found: true,
      isFlowVersionFallback: false,
      queryLimit: 50,
      warnings: [],
      message: '已找到失败前成功参考'
    });
    expect(previous.referenceSummary?.resultId).toBe(referenceId);
    expect(previous.currentSummary.sessionId).toBe(sessionId);
    expect(previous.currentSummary.runId).toBeNull();
    expect(previous.referenceSummary?.sessionId).toBe(sessionId);
    expect(previous.referenceSummary?.runId).toBeNull();

    const comparison = decodeInspectionHistoryComparison({
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
    });
    expect(comparison.leftSummary.sessionId).toBe(sessionId);
    expect(comparison.leftSummary.runId).toBeNull();
    expect(comparison.rightSummary.sessionId).toBe(sessionId);
    expect(comparison.rightSummary.runId).toBeNull();
    expect(comparison.fieldDiffs[0]).toMatchObject({ leftValuePreview: 'Ok', rightValuePreview: 'Ng' });
    expect(() => decodeInspectionHistoryComparison({
      leftSummary: comparisonSummary(referenceId, 'Ok'),
      rightSummary: comparisonSummary(resultId, 'Ng'),
      compatibility: {
        flowVersionCompatible: true,
        calibrationBundleCompatible: true,
        onlySafePreviewComparison: true,
        hasUnknownFields: false
      },
      warnings: [],
      fieldDiffs: [],
      traceabilityDiff: [],
      sceneReplayAvailability: {
        kind: 'scene', mode: 'none', isAvailable: false,
        leftAvailable: false, rightAvailable: false, leftReference: null,
        rightReference: null, leftSummary: null, rightSummary: null, message: ''
      },
      imageReplayAvailability: {
        kind: 'image', mode: 'none', isAvailable: false,
        leftAvailable: false, rightAvailable: false, leftReference: null,
        rightReference: null, leftSummary: null, rightSummary: null, message: ''
      }
    }, {
      projectId,
      leftResultId: resultId,
      rightResultId: referenceId
    })).toThrow(ResultsContractDecodeError);
    expect(() => decodeInspectionPreviousSuccess({
      currentSummary: comparisonSummary(resultId, 'Ng'),
      referenceSummary: { ...comparisonSummary(referenceId, 'Ok'), projectId: crypto.randomUUID() },
      found: true,
      isFlowVersionFallback: false,
      queryLimit: 50,
      warnings: [],
      message: 'invalid cross-project reference'
    })).toThrow(ResultsContractDecodeError);
    expect(() => decodeInspectionPreviousSuccess({
      currentSummary: comparisonSummary(resultId, 'Ng'),
      referenceSummary: comparisonSummary(referenceId, 'Ok'),
      found: true,
      isFlowVersionFallback: false,
      queryLimit: 50,
      warnings: [],
      message: 'stale current identity'
    }, {
      projectId,
      resultId: crypto.randomUUID()
    })).toThrow(ResultsContractDecodeError);
  });

  it('rejects malformed, unknown and half-present outcome axes', () => {
    expect(() => decodeLocalInspectionResultPage({
      items: [localSummary({ execution: 'Succeeded', decision: 'Unknown' as never })],
      totalCount: 1,
      pageIndex: 0,
      pageSize: 20
    })).toThrow();
    expect(() => decodeStationInspectionResultPage({
      items: [stationSummary({ decisionOutcome: undefined })],
      totalCount: 1,
      pageIndex: 0,
      pageSize: 20
    })).toThrow(ResultsContractDecodeError);
    expect(() => decodeStationInspectionResultPage({
      items: [stationSummary({ executionOutcome: undefined, decisionOutcome: undefined, outcome: 99 })],
      totalCount: 1,
      pageIndex: 0,
      pageSize: 20
    })).toThrow(ResultsContractDecodeError);
    expect(() => decodeLocalInspectionResultPage({ items: {}, totalCount: 0, pageIndex: 0, pageSize: 20 }))
      .toThrow(ResultsContractDecodeError);
    expect(() => decodeLocalInspectionResultPage({
      items: [{ ...localSummary(), projectId: crypto.randomUUID() }],
      totalCount: 1,
      pageIndex: 0,
      pageSize: 20
    }, projectId)).toThrow(ResultsContractDecodeError);
  });
});
