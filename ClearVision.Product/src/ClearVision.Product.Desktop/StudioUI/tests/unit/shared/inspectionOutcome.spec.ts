import { describe, expect, it } from 'vitest';
import {
  canonicalInspectionOutcomeKinds,
  classifyInspectionOutcome,
  decodeInspectionOutcome,
  formatInspectionOutcome,
  InspectionOutcomeDecodeError,
  type InspectionOutcome
} from '@/shared/inspectionOutcome';

const canonicalCases: readonly [InspectionOutcome, string][] = [
  [{ execution: 'Succeeded', decision: 'Ok' }, 'Ok'],
  [{ execution: 'Succeeded', decision: 'Ng' }, 'Ng'],
  [{ execution: 'Succeeded', decision: 'Undetermined' }, 'Undetermined'],
  [{ execution: 'Succeeded', decision: 'NotApplicable' }, 'NotApplicable'],
  [{ execution: 'Succeeded', decision: 'Invalid' }, 'Invalid'],
  [{ execution: 'Failed', decision: 'Undetermined' }, 'Failed'],
  [{ execution: 'Cancelled', decision: 'NotApplicable' }, 'Cancelled'],
  [{ execution: 'TimedOut', decision: 'Undetermined' }, 'TimedOut'],
  [{ execution: 'Skipped', decision: 'NotApplicable' }, 'Skipped']
];

describe('canonical inspection outcome formatter', () => {
  it('preserves all nine canonical outcomes without folding them into NG', () => {
    expect(canonicalCases.map(([outcome]) => classifyInspectionOutcome(outcome)))
      .toEqual(canonicalInspectionOutcomeKinds);
    expect(formatInspectionOutcome(canonicalCases[2]![0]).label).toBe('未判定');
    expect(formatInspectionOutcome(canonicalCases[4]![0]).label).toBe('判定无效');
    expect(formatInspectionOutcome(canonicalCases[5]![0]).label).toBe('执行失败');
    expect(formatInspectionOutcome(canonicalCases[5]![0]).tone).toBe('error');
    expect(formatInspectionOutcome(canonicalCases[7]![0]).tone).toBe('error');
  });

  it('keeps the execution and decision axes visible in the presentation', () => {
    const presentation = formatInspectionOutcome({
      execution: 'Succeeded',
      decision: 'NotApplicable'
    });
    expect(presentation).toMatchObject({
      kind: 'NotApplicable',
      executionLabel: '执行成功',
      decisionLabel: '不适用'
    });
  });

  it('rejects malformed and unknown outcome values', () => {
    expect(() => decodeInspectionOutcome('Unknown', 'Ok')).toThrow(InspectionOutcomeDecodeError);
    expect(() => decodeInspectionOutcome('Succeeded', null)).toThrow(InspectionOutcomeDecodeError);
  });
});
