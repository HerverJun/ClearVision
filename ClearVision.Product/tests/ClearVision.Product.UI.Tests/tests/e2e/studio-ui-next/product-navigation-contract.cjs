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
  '/projects',
  '/results'
]);

const f04ForbiddenRoutes = Object.freeze([
  '/overview',
  '/operators',
  '/stations',
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
    requiredRoutes: f04RequiredRoutes,
    forbiddenRoutes: f04ForbiddenRoutes,
    stationsReadEnabled,
    stationsRule: 'pilot-not-exposed'
  });
}

module.exports = {
  resolveProductNavigationContract,
  stationsReadFeatureFlag
};
