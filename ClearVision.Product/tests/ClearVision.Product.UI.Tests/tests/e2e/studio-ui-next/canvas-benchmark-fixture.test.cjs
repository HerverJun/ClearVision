'use strict';

const assert = require('node:assert/strict');
const {
  createCanvasFixtureDescriptor,
  createDeterministicCanvasBenchmarkFixture,
  createFlowIdentityFingerprint,
  fnv1a32,
  stableGuid
} = require('./canvas-benchmark-fixture.cjs');

function verifyFixture(nodeCount, connectionCount) {
  const first = createCanvasFixtureDescriptor(nodeCount, connectionCount);
  const second = createCanvasFixtureDescriptor(nodeCount, connectionCount);

  assert.equal(first.flow.operators.length, nodeCount);
  assert.equal(first.flow.connections.length, connectionCount);
  assert.equal(first.flow.id, stableGuid(85, nodeCount * 1000 + connectionCount));
  assert.equal(first.flow.operators[0].id, stableGuid(80, 1));
  assert.equal(first.flow.operators.at(-1).id, stableGuid(80, nodeCount));
  assert.equal(first.flow.connections[0].id, stableGuid(84, 1));
  assert.equal(first.flow.connections.at(-1).id, stableGuid(84, connectionCount));
  assert.deepEqual(first.flow.operators[0].parameters[0].options[0], {
    label: 'Pixel',
    value: 'Pixel'
  });
  assert.equal(first.fingerprint.length, 8);
  assert.equal(first.fingerprint, createFlowIdentityFingerprint(first.flow));
  assert.deepEqual(first, second);

  const rebuilt = createDeterministicCanvasBenchmarkFixture(nodeCount, connectionCount);
  assert.equal(createFlowIdentityFingerprint(rebuilt), first.fingerprint);
  return first;
}

assert.equal(stableGuid(80, 1), '00000050-0000-4000-8000-000000000001');
assert.equal(fnv1a32(''), '811c9dc5');

const benchmark = verifyFixture(100, 150);
const stress = verifyFixture(300, 450);
assert.notEqual(benchmark.fingerprint, stress.fingerprint);

assert.throws(
  () => createDeterministicCanvasBenchmarkFixture(1, 0),
  /nodeCount must be an integer greater than one/
);
assert.throws(
  () => createDeterministicCanvasBenchmarkFixture(100, 98),
  /connectionCount must be between/
);
assert.throws(
  () => createDeterministicCanvasBenchmarkFixture(100, 198),
  /connectionCount must be between/
);

process.stdout.write(`${JSON.stringify({
  ok: true,
  benchmark100Fingerprint: benchmark.fingerprint,
  stress300Fingerprint: stress.fingerprint
})}\n`);
