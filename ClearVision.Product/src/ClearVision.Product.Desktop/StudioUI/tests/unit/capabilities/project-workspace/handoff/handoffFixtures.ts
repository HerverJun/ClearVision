import {
  aiBuildId,
  aiBuildOperationId,
  aiBuildRunId,
  aiCandidateFingerprint,
  aiPlanHash,
  aiPlanId,
  aiTimestamp,
  buildResultFixture,
  validationFixture
} from '../../ai-workbench/aiFixtures';

export const artifactId = '0123456789abcdef0123456789abcdef';
export const consumeOperationId = '55555555-5555-4555-8555-555555555555';
export const existingProjectId = '22222222-2222-4222-8222-222222222222';
export const candidateFlowId = '66666666-6666-4666-8666-666666666666';

export function candidateFlowFixture() {
  return {
    id: candidateFlowId,
    name: 'AI 候选流程',
    operators: [],
    connections: [],
    decisionConfiguration: null
  };
}

export function handoffArtifactPayload(overrides: Record<string, unknown> = {}) {
  const targetKind = overrides.targetKind === 'existing' ? 'existing' : 'new';
  const baseline = targetKind === 'existing'
    ? {
        targetKind: 'existing',
        projectId: existingProjectId,
        persistenceRevision: 3,
        canonicalFlowHash: 'a'.repeat(64)
      }
    : {
        targetKind: 'new',
        projectId: null,
        persistenceRevision: null,
        canonicalFlowHash: ''
      };
  const build = buildResultFixture({
    buildId: aiBuildId,
    buildIdentity: `${aiPlanId}:${aiBuildId}`,
    candidateFlowFingerprint: aiCandidateFingerprint,
    operatorCount: 0,
    connectionCount: 0,
    projectBaseline: baseline,
    parameterMapping: [],
    workflowDiff: {
      addedNodes: [], modifiedNodes: [], preservedNodes: [], removedNodes: [],
      addedOrChangedParameters: [], pendingParameters: [], missingResources: [],
      validationFailures: [], autoRepairs: [], deploymentBlockers: [], metadataOnly: true
    },
    validation: validationFixture(true)
  });
  return {
    schemaVersion: 1,
    artifactId,
    clientOperationId: '11111111-1111-4111-8111-111111111111',
    sessionId: 'session_01',
    sessionRevision: 5,
    planRunId: 'run_plan_01',
    planId: aiPlanId,
    planHash: aiPlanHash,
    buildRunId: aiBuildRunId,
    buildClientOperationId: aiBuildOperationId,
    buildIdentity: `${aiPlanId}:${aiBuildId}`,
    targetKind,
    projectBaseline: baseline,
    candidateFlow: candidateFlowFixture(),
    candidateFlowFingerprint: aiCandidateFingerprint,
    build,
    createdAtUtc: aiTimestamp,
    expiresAtUtc: '2026-07-29T08:30:00.000Z',
    status: 'available',
    consumeClientOperationId: null,
    consumeReceipt: null,
    ...overrides
  };
}
