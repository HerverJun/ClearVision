'use strict';

const assert = require('node:assert/strict');
const {
  resolveProductNavigationContract,
  stationsReadFeatureFlag
} = require('./product-navigation-contract.cjs');

const historical = resolveProductNavigationContract('f03', {});
assert.deepEqual(historical.requiredRoutes, [
  '/overview',
  '/projects',
  '/operators',
  '/stations',
  '/results'
]);
assert.deepEqual(historical.forbiddenRoutes, []);
assert.equal(historical.stationsRule, 'historical-required');

const f04Default = resolveProductNavigationContract(' F04 ', {});
assert.deepEqual(f04Default.requiredRoutes, [
  '/overview',
  '/projects',
  '/operators',
  '/results',
  '/diagnostics',
  '/about'
]);
assert.deepEqual(f04Default.forbiddenRoutes, ['/stations']);
assert.equal(f04Default.stationsReadEnabled, false);
assert.equal(f04Default.stationsRule, 'feature-forbidden');

const f04Stations = resolveProductNavigationContract('f04', {
  [stationsReadFeatureFlag]: true
});
assert.deepEqual(f04Stations.requiredRoutes, [
  '/overview',
  '/projects',
  '/operators',
  '/stations',
  '/results',
  '/diagnostics',
  '/about'
]);
assert.deepEqual(f04Stations.forbiddenRoutes, []);
assert.equal(f04Stations.stationsReadEnabled, true);
assert.equal(f04Stations.stationsRule, 'feature-required');

process.stdout.write(`${JSON.stringify({
  ok: true,
  historical,
  f04Default,
  f04Stations
})}\n`);
