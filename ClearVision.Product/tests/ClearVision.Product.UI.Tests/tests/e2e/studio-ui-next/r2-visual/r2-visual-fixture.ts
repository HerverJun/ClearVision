import { createHash } from 'node:crypto';

export type R2SceneId = `S${string}`;
export type R2FixtureCapability = 'DIRECTLY_REUSABLE' | 'REQUIRES_SHARED_EXTRACTION' | 'PUBLIC_ROUTE_ONLY' | 'UNAVAILABLE';

export interface R2RouteState {
  readonly id: string;
  readonly scene: R2SceneId;
  readonly route: `#/${string}`;
  readonly state: string;
  readonly role: 'Public' | 'Operator' | 'Engineer' | 'Admin';
  readonly fixtureCapability: R2FixtureCapability;
  readonly fixtureOwner: 'F02' | 'F03' | 'F04' | 'F05' | 'F06' | 'F07' | 'R2';
  readonly featureFlags: Readonly<Record<string, boolean>>;
  readonly writable: boolean;
  readonly reason: string;
}

export const r2RouteStates = Object.freeze([
  routeState('s00-login-main', 'S00', '#/login', 'main', 'Public', 'PUBLIC_ROUTE_ONLY', 'F04', {}, false, 'Public auth route; F04 owns the stateful auth fixture.'),
  routeState('s00-login-error', 'S00', '#/login', 'auth-error', 'Public', 'PUBLIC_ROUTE_ONLY', 'F04', {}, false, 'F04 can project validation, busy, 401, and recovery states.'),
  routeState('s01-overview-main', 'S01', '#/overview', 'main', 'Engineer', 'DIRECTLY_REUSABLE', 'F02', {}, false, 'F02 owns the read-only overview and session projections.'),
  routeState('s02-projects-main', 'S02', '#/projects', 'data', 'Engineer', 'DIRECTLY_REUSABLE', 'F02', { 'Studio2.ProjectPage': true }, false, 'F02 provides deterministic project list and detail reads.'),
  routeState('s02-projects-empty', 'S02', '#/projects', 'empty', 'Engineer', 'REQUIRES_SHARED_EXTRACTION', 'R2', { 'Studio2.ProjectPage': true }, false, 'R2 dispatcher must select the empty read projection without adding an API owner.'),
  routeState('s03-workspace-main', 'S03', '#/projects/11111111-1111-1111-1111-111111111111/workspace', 'flow-main', 'Engineer', 'DIRECTLY_REUSABLE', 'F03', { 'Studio2.Workspace': true }, false, 'F03 owns the canonical workspace route fixture.'),
  routeState('s04-inspector-error', 'S04', '#/projects/11111111-1111-1111-1111-111111111111/workspace', 'node-validation-error', 'Engineer', 'REQUIRES_SHARED_EXTRACTION', 'R2', { 'Studio2.Workspace': true }, false, 'R2 selects a deterministic canonical node and parameter error through the F03 dispatcher.'),
  routeState('s05-preview-stale', 'S05', '#/projects/11111111-1111-1111-1111-111111111111/workspace', 'preview-stale', 'Engineer', 'DIRECTLY_REUSABLE', 'F03', { 'Studio2.Workspace': true }, false, 'F03 owns the shared deterministic PNG, Preview and ROI lifecycle.'),
  routeState('s06-run-ng', 'S06', '#/projects/11111111-1111-1111-1111-111111111111/workspace', 'run-succeeded-ng', 'Engineer', 'REQUIRES_SHARED_EXTRACTION', 'R2', { 'Studio2.Workspace': true }, false, 'R2 composes F03 admission with F05 run projection; no real write is issued.'),
  routeState('s07-results-main', 'S07', '#/results', 'data-ng-first', 'Engineer', 'DIRECTLY_REUSABLE', 'F02', {}, false, 'F02 provides deterministic local and Station results reads.'),
  routeState('s08-stations-main', 'S08', '#/stations', 'online-offline-stale', 'Operator', 'DIRECTLY_REUSABLE', 'F02', { 'Studio2.Stations': true }, false, 'F02 provides read-only fleet and detail projections.'),
  routeState('s08-station-admin', 'S08', '#/stations/station-a', 'admin-feedback', 'Admin', 'REQUIRES_SHARED_EXTRACTION', 'R2', { 'Studio2.Stations': true }, false, 'R2 must reuse the Station command fixture without sending a real command.'),
  routeState('s09-inspection-main', 'S09', '#/inspection', 'admission-ready', 'Engineer', 'DIRECTLY_REUSABLE', 'F05', { 'Studio2.Inspection': true }, false, 'F05 owns the inspection selector and run fixture.'),
  routeState('s10-settings-main', 'S10', '#/settings', 'general', 'Admin', 'DIRECTLY_REUSABLE', 'F07', { 'Studio2.Settings': true }, false, 'F07 provides deterministic settings and device projections.'),
  routeState('s10-settings-forbidden', 'S10', '#/settings', 'forbidden', 'Operator', 'DIRECTLY_REUSABLE', 'F07', { 'Studio2.Settings': true }, false, 'F07 projects role-scoped 403 without changing real authority.'),
  routeState('s11-ai-main', 'S11', '#/ai', 'clarification', 'Engineer', 'DIRECTLY_REUSABLE', 'F06', { 'Studio2.AiWorkbench': true }, false, 'F06 owns the AgentRun/AI fixture state machine.'),
  routeState('s11-ai-failure', 'S11', '#/ai', 'failure-recovery', 'Engineer', 'DIRECTLY_REUSABLE', 'F06', { 'Studio2.AiWorkbench': true }, false, 'F06 can emit deterministic failed/recovery events.'),
  routeState('s12-operators-main', 'S12', '#/operators', 'catalog', 'Engineer', 'DIRECTLY_REUSABLE', 'F02', {}, false, 'F02 provides a dense deterministic operator catalog.'),
  routeState('s13-diagnostics-main', 'S13', '#/diagnostics', 'service-warning', 'Engineer', 'DIRECTLY_REUSABLE', 'F02', {}, false, 'F02 provides service and owner projections.'),
  routeState('s13-about-main', 'S13', '#/about', 'identity', 'Engineer', 'DIRECTLY_REUSABLE', 'F02', {}, false, 'F02 provides product, host, backend, and license facts.')
] satisfies readonly R2RouteState[]);

export const r2FixtureContract = Object.freeze({
  schemaVersion: 'r2-route-state.v1',
  states: r2RouteStates,
  hash: createHash('sha256').update(JSON.stringify(r2RouteStates)).digest('hex')
});

function routeState(
  id: string,
  scene: R2SceneId,
  route: `#/${string}`,
  state: string,
  role: R2RouteState['role'],
  fixtureCapability: R2FixtureCapability,
  fixtureOwner: R2RouteState['fixtureOwner'],
  featureFlags: Readonly<Record<string, boolean>>,
  writable: boolean,
  reason: string
): R2RouteState {
  return Object.freeze({ id, scene, route, state, role, fixtureCapability, fixtureOwner, featureFlags: Object.freeze(featureFlags), writable, reason });
}

export function findR2RouteState(id: string): R2RouteState {
  const state = r2RouteStates.find(candidate => candidate.id === id);
  if (!state) throw new Error(`Unknown R2 route state: ${id}.`);
  return state;
}
