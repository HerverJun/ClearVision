import { describe, expect, it } from 'vitest';
import {
  decodeLocalInspectionResultDetail,
  decodeLocalInspectionResultPage,
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
const runId = '44444444-4444-4444-8444-444444444444';

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
    sessionId: runId,
    runId,
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
    projectRevision: 12,
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
    primaryOutputsPreview: {},
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

  it('decodes scalar local detail and excludes image, ROI and evidence contracts', () => {
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
        sessionId: runId,
        runId,
        packageId: null,
        stationId: null
      },
      imageReference: '/api/images/ignored',
      evidenceManifestReference: '/api/evidence/ignored'
    });

    expect(detail.defects).toEqual([{
      id: defectId,
      type: 'Scratch',
      confidenceScore: 0.92,
      description: '轻微划痕'
    }]);
    expect(detail).not.toHaveProperty('imageReference');
    expect(detail).not.toHaveProperty('evidenceManifestReference');
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
  });
});
