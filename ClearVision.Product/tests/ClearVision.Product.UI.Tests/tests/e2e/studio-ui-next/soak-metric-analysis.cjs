'use strict';

function requireNonNegativeFinite(value, name) {
  if (!Number.isFinite(value) || value < 0) {
    throw new TypeError(`${name} must be a non-negative finite number.`);
  }
  return value;
}

function isMonotonicIncrease(values) {
  return values.length > 1 && values.every((value, index) =>
    index === 0 || value >= values[index - 1]);
}

function analyzeSoakMetric(samples, selector, policy) {
  if (!Array.isArray(samples) || typeof selector !== 'function' || !policy?.name) {
    throw new TypeError('Soak metric analysis requires samples, a selector, and a named policy.');
  }
  const growthLimit = requireNonNegativeFinite(policy.growthLimit, 'growthLimit');
  const monotonicGrowthLimit = requireNonNegativeFinite(
    policy.monotonicGrowthLimit,
    'monotonicGrowthLimit'
  );
  const allValues = samples.map(selector).map(Number).filter(Number.isFinite);
  if (allValues.length < 2) {
    throw new Error(`Soak metric ${policy.name} did not provide enough finite samples.`);
  }

  const requestedWarmup = Number.isInteger(policy.warmupSamples) && policy.warmupSamples >= 0
    ? policy.warmupSamples
    : 2;
  const warmupSampleCount = Math.min(requestedWarmup, Math.max(0, allValues.length - 2));
  const values = allValues.slice(warmupSampleCount);
  const requestedTail = Number.isInteger(policy.tailSampleCount) && policy.tailSampleCount >= 2
    ? policy.tailSampleCount
    : Math.ceil(values.length / 2);
  const tailSampleCount = Math.min(values.length, Math.max(2, requestedTail));
  const tailValues = values.slice(-tailSampleCount);

  const first = values[0];
  const last = values.at(-1);
  const delta = last - first;
  const tailFirst = tailValues[0];
  const tailLast = tailValues.at(-1);
  const tailDelta = tailLast - tailFirst;
  const tailMonotonicGrowthLimit = Math.round(
    monotonicGrowthLimit * ((tailValues.length - 1) / (values.length - 1))
  );
  const monotonicIncrease = isMonotonicIncrease(values);
  const tailMonotonicIncrease = isMonotonicIncrease(tailValues);
  const unexplainedMonotonicGrowth = tailMonotonicIncrease &&
    tailDelta > tailMonotonicGrowthLimit;

  return {
    name: policy.name,
    warmupSampleCount,
    sampleCount: values.length,
    first,
    last,
    minimum: Math.min(...values),
    maximum: Math.max(...values),
    delta,
    growthLimit,
    monotonicGrowthLimit,
    monotonicIncrease,
    tailSampleCount,
    tailFirst,
    tailLast,
    tailDelta,
    tailMonotonicGrowthLimit,
    tailMonotonicIncrease,
    unexplainedMonotonicGrowth,
    passed: delta <= growthLimit && !unexplainedMonotonicGrowth
  };
}

module.exports = { analyzeSoakMetric };
