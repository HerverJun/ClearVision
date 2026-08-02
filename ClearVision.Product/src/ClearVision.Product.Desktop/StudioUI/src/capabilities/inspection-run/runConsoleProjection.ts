import type { InspectionOutcome } from '@/shared/inspectionOutcome';

export type RunConsoleMode = 'formal' | 'continuous';
export type RunConsoleCheckState = 'pass' | 'blocked' | 'pending' | 'unknown' | 'not-applicable';

export interface RunConsoleIdentityItem {
  readonly key: string;
  readonly label: string;
  readonly value: string;
}

export interface RunConsoleAdmissionCheck {
  readonly key: string;
  readonly label: string;
  readonly state: RunConsoleCheckState;
  readonly detail: string;
}

export interface RunConsoleViolation {
  readonly key: string;
  readonly code: string;
  readonly message: string;
  readonly target: string | null;
}

export interface RunConsoleDiagnostic {
  readonly key: string;
  readonly label: string;
  readonly value: string;
}

export interface RunConsoleResultItem {
  readonly id: string;
  readonly timestamp: string | null;
  readonly outcome: InspectionOutcome;
  readonly defectCount: number | null;
  readonly processingTimeMs: number | null;
  readonly errorMessage: string | null;
  readonly diagnostics: readonly RunConsoleDiagnostic[];
}

export interface RunConsoleStatistics {
  readonly total: number;
  readonly executionSucceeded: number;
  readonly executionFailed: number;
  readonly ok: number;
  readonly ng: number;
  readonly undetermined: number;
  readonly invalid: number;
  readonly yieldRate: number | null;
  readonly decisionCoverageRate: number | null;
  readonly averageProcessingTimeMs: number | null;
}

export const emptyRunConsoleStatistics: RunConsoleStatistics = Object.freeze({
  total: 0,
  executionSucceeded: 0,
  executionFailed: 0,
  ok: 0,
  ng: 0,
  undetermined: 0,
  invalid: 0,
  yieldRate: null,
  decisionCoverageRate: null,
  averageProcessingTimeMs: null
});

export function calculateRunConsoleStatistics(
  results: readonly RunConsoleResultItem[]
): RunConsoleStatistics {
  if (results.length === 0) return emptyRunConsoleStatistics;
  let executionSucceeded = 0;
  let executionFailed = 0;
  let ok = 0;
  let ng = 0;
  let undetermined = 0;
  let invalid = 0;
  let measuredCount = 0;
  let measuredTotal = 0;

  for (const result of results) {
    if (result.outcome.execution === 'Succeeded') executionSucceeded += 1;
    else executionFailed += 1;
    switch (result.outcome.decision) {
      case 'Ok': ok += 1; break;
      case 'Ng': ng += 1; break;
      case 'Undetermined': undetermined += 1; break;
      case 'Invalid': invalid += 1; break;
    }
    if (result.processingTimeMs != null) {
      measuredCount += 1;
      measuredTotal += result.processingTimeMs;
    }
  }

  const validDecisions = ok + ng;
  return Object.freeze({
    total: results.length,
    executionSucceeded,
    executionFailed,
    ok,
    ng,
    undetermined,
    invalid,
    yieldRate: validDecisions === 0 ? null : ok / validDecisions,
    decisionCoverageRate: executionSucceeded === 0 ? null : validDecisions / executionSucceeded,
    averageProcessingTimeMs: measuredCount === 0 ? null : measuredTotal / measuredCount
  });
}

export function formatDiagnosticValue(value: unknown): string {
  if (value == null) return '--';
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

export function flattenRunDiagnostics(
  value: unknown,
  prefix: string,
  limit = 24
): readonly RunConsoleDiagnostic[] {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return Object.freeze([]);
  const rows: RunConsoleDiagnostic[] = [];
  const visit = (candidate: unknown, path: string, depth: number): void => {
    if (rows.length >= limit) return;
    if (candidate === null || typeof candidate !== 'object' || depth >= 2) {
      rows.push(Object.freeze({ key: path, label: path, value: formatDiagnosticValue(candidate) }));
      return;
    }
    if (Array.isArray(candidate)) {
      rows.push(Object.freeze({ key: path, label: path, value: formatDiagnosticValue(candidate) }));
      return;
    }
    for (const [key, child] of Object.entries(candidate as Record<string, unknown>)) {
      visit(child, path ? path + '.' + key : key, depth + 1);
      if (rows.length >= limit) return;
    }
  };
  visit(value, prefix, 0);
  return Object.freeze(rows);
}
