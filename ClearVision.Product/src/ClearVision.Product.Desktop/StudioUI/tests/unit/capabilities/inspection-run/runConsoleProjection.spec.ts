import { describe, expect, it } from 'vitest';
import {
  calculateRunConsoleStatistics,
  flattenRunDiagnostics,
  type RunConsoleResultItem
} from '@/capabilities/inspection-run';

function result(
  id: string,
  execution: RunConsoleResultItem['outcome']['execution'],
  decision: RunConsoleResultItem['outcome']['decision'],
  processingTimeMs: number
): RunConsoleResultItem {
  return {
    id,
    timestamp: null,
    outcome: { execution, decision },
    defectCount: null,
    processingTimeMs,
    errorMessage: null,
    diagnostics: []
  };
}

describe('run console generic projection', () => {
  it('keeps execution failures separate from the decision-axis yield and coverage', () => {
    const statistics = calculateRunConsoleStatistics([
      result('ok', 'Succeeded', 'Ok', 10),
      result('ng', 'Succeeded', 'Ng', 20),
      result('undetermined', 'Succeeded', 'Undetermined', 30),
      result('failed', 'Failed', 'NotApplicable', 40)
    ]);

    expect(statistics).toEqual({
      total: 4,
      executionSucceeded: 3,
      executionFailed: 1,
      ok: 1,
      ng: 1,
      undetermined: 1,
      invalid: 0,
      yieldRate: 0.5,
      decisionCoverageRate: 2 / 3,
      averageProcessingTimeMs: 25
    });
  });

  it('flattens arbitrary observation keys without an operator or output allowlist', () => {
    const diagnostics = flattenRunDiagnostics({
      FutureOperator: { confidence: 0.97, state: 'accepted' },
      vendorMetric: [1, 2, 3]
    }, 'output');

    expect(diagnostics).toEqual([
      { key: 'output.FutureOperator.confidence', label: 'output.FutureOperator.confidence', value: '0.97' },
      { key: 'output.FutureOperator.state', label: 'output.FutureOperator.state', value: 'accepted' },
      { key: 'output.vendorMetric', label: 'output.vendorMetric', value: '[1,2,3]' }
    ]);
  });
});
