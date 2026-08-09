'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const { analyzeSoakMetric } = require('./soak-metric-analysis.cjs');

const mib = 1024 * 1024;
const policy = Object.freeze({
  name: 'jsHeapUsedBytes',
  growthLimit: 8 * mib,
  monotonicGrowthLimit: 2 * mib
});

function analyze(values) {
  return analyzeSoakMetric(values.map(value => ({ value })), sample => sample.value, policy);
}

test('accepts bounded warm-up that converges in the observation tail', () => {
  const result = analyze([
    7_259_284, 8_020_468, 8_477_576, 8_820_576, 9_067_896,
    9_170_016, 9_220_632, 9_408_264, 10_128_996, 10_323_148,
    10_400_420, 10_423_076, 10_493_596, 10_509_928, 10_536_196,
    10_547_872, 10_576_796, 10_594_884, 10_607_808, 10_615_208
  ]);

  assert.equal(result.monotonicIncrease, true);
  assert.ok(result.delta > policy.monotonicGrowthLimit);
  assert.ok(result.tailDelta < result.tailMonotonicGrowthLimit);
  assert.equal(result.unexplainedMonotonicGrowth, false);
  assert.equal(result.passed, true);
});

test('rejects sustained monotonic growth even when total growth stays below the hard limit', () => {
  const result = analyze(Array.from({ length: 20 }, (_, index) => 8 * mib + index * 180_000));

  assert.ok(result.delta < policy.growthLimit);
  assert.ok(result.tailDelta > result.tailMonotonicGrowthLimit);
  assert.equal(result.unexplainedMonotonicGrowth, true);
  assert.equal(result.passed, false);
});

test('rejects a large bounded increase through the independent total-growth gate', () => {
  const result = analyze([
    1 * mib, 2 * mib, 3 * mib, 12 * mib, 12 * mib, 12 * mib,
    12 * mib, 12 * mib, 12 * mib, 12 * mib, 12 * mib, 12 * mib
  ]);

  assert.ok(result.delta > policy.growthLimit);
  assert.equal(result.unexplainedMonotonicGrowth, false);
  assert.equal(result.passed, false);
});

test('rejects invalid input instead of manufacturing a trend', () => {
  assert.throws(() => analyzeSoakMetric([{ value: null }], sample => sample.value, policy),
    /did not provide enough finite samples/);
  assert.throws(() => analyzeSoakMetric([], sample => sample.value, { ...policy, growthLimit: -1 }),
    /growthLimit must be a non-negative finite number/);
});
