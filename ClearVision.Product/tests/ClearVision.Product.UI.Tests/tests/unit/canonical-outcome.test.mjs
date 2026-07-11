import test from 'node:test';
import assert from 'node:assert/strict';

import {
  calculateCanonicalStatistics,
  matchesCanonicalOutcomeFilter,
  normalizeCanonicalOutcome,
  normalizeCanonicalStatistics
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/canonicalOutcome.mjs';

test('canonical outcome prioritizes execution failure over decision text', () => {
  const outcome = normalizeCanonicalOutcome({
    status: 'NG',
    executionOutcome: 'TimedOut',
    decisionOutcome: 'Ng'
  });

  assert.equal(outcome.category, 'timedOut');
  assert.equal(outcome.label, '执行超时');
  assert.equal(outcome.isLegacyProjection, false);
});

test('legacy status is projected only when canonical fields are absent', () => {
  assert.deepEqual(
    normalizeCanonicalOutcome({ status: 'Error' }),
    {
      executionOutcome: 'Failed',
      decisionOutcome: 'Undetermined',
      isLegacyProjection: true,
      category: 'failed',
      key: 'failed',
      label: '执行失败',
      tone: 'error'
    }
  );
});

test('canonical statistics use OK plus NG as the only yield denominator', () => {
  const stats = calculateCanonicalStatistics([
    { executionOutcome: 'Succeeded', decisionOutcome: 'Ok' },
    { executionOutcome: 'Succeeded', decisionOutcome: 'Ng' },
    { executionOutcome: 'Succeeded', decisionOutcome: 'Ng' },
    { executionOutcome: 'Succeeded', decisionOutcome: 'Undetermined' },
    { executionOutcome: 'Succeeded', decisionOutcome: 'Invalid' },
    { executionOutcome: 'Failed', decisionOutcome: 'Undetermined' },
    { executionOutcome: 'TimedOut', decisionOutcome: 'Undetermined' },
    { executionOutcome: 'Skipped', decisionOutcome: 'NotApplicable' }
  ]);

  assert.equal(stats.total, 8);
  assert.equal(stats.validDecisions, 3);
  assert.equal(stats.yieldRate, 1 / 3);
  assert.equal(stats.executionFailures, 2);
  assert.equal(stats.undetermined, 1);
  assert.equal(stats.invalid, 1);
  assert.equal(stats.decisionCoverageRate, 3 / 5);
  assert.equal(matchesCanonicalOutcomeFilter({ executionOutcome: 'Succeeded', decisionOutcome: 'Undetermined' }, 'undetermined'), true);
});

test('server statistics normalization preserves canonical rates and compatibility aliases', () => {
  const stats = normalizeCanonicalStatistics({
    totalCount: 7,
    okCount: 2,
    ngCount: 1,
    validDecisionCount: 3,
    executionSucceededCount: 5,
    executionFailureCount: 2,
    undeterminedCount: 1,
    yieldRate: 2 / 3,
    decisionCoverageRate: 0.6,
    averageProcessingTimeMs: 12.6
  });

  assert.equal(stats.total, 7);
  assert.equal(stats.yieldRate, 2 / 3);
  assert.equal(stats.decisionCoverageRate, 0.6);
  assert.equal(stats.executionFailures, 2);
  assert.equal(stats.avgTime, 13);
});

test('explicit canonical zero execution failures override legacy error count for invalid-only data', () => {
  const stats = normalizeCanonicalStatistics({
    totalAttemptCount: 3,
    executionSucceededCount: 3,
    validDecisionCount: 0,
    invalidCount: 3,
    failedCount: 0,
    timedOutCount: 0,
    executionFailureCount: 0,
    errorCount: 3
  });

  assert.equal(stats.invalid, 3);
  assert.equal(stats.executionFailures, 0);
  assert.equal(stats.yieldRate, 0);
});
