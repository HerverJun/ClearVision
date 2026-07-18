'use strict';

const stationsReadFeatureFlag = 'Studio2.StationsRead';

const historicalRequiredRoutes = Object.freeze([
  '/overview',
  '/projects',
  '/operators',
  '/stations',
  '/results'
]);

const f04RequiredRoutes = Object.freeze([
  '/overview',
  '/projects',
  '/operators',
  '/results',
  '/diagnostics',
  '/about'
]);

function resolveProductNavigationContract(phase, featureFlags = {}) {
  const normalizedPhase = String(phase || '').trim().toLowerCase();
  const stationsReadEnabled = featureFlags[stationsReadFeatureFlag] === true;

  if (normalizedPhase !== 'f04') {
    return Object.freeze({
      phase: normalizedPhase || 'unknown',
      requiredRoutes: historicalRequiredRoutes,
      forbiddenRoutes: Object.freeze([]),
      stationsReadEnabled,
      stationsRule: 'historical-required'
    });
  }

  return Object.freeze({
    phase: normalizedPhase,
    requiredRoutes: Object.freeze(stationsReadEnabled
      ? [
          '/overview',
          '/projects',
          '/operators',
          '/stations',
          '/results',
          '/diagnostics',
          '/about'
        ]
      : [...f04RequiredRoutes]),
    forbiddenRoutes: Object.freeze(stationsReadEnabled ? [] : ['/stations']),
    stationsReadEnabled,
    stationsRule: stationsReadEnabled ? 'feature-required' : 'feature-forbidden'
  });
}

module.exports = {
  resolveProductNavigationContract,
  stationsReadFeatureFlag
};
