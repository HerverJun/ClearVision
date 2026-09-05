const SUPPORTED_WORKSPACE_SNAPSHOT_VERSIONS = new Set([1, 2]);
const SAFE_LIFECYCLE_STATES = new Set([
    'idle',
    'routing',
    'clarifying',
    'plan_running',
    'planning',
    'plan_ready',
    'plan_blocked',
    'plan_failed',
    'plan_cancelled',
    'plan',
    'build',
    'building',
    'build_completed',
    'build_failed',
    'build_cancelled',
    'applied'
]);
const SAFE_RUN_STATUSES = new Set(['idle', 'pending', 'running', 'completed', 'failed', 'cancelled', 'canceled']);

function read(snapshot, camel, pascal = '') {
    return snapshot?.[camel] ?? snapshot?.[pascal || `${camel[0].toUpperCase()}${camel.slice(1)}`];
}

function finiteNonNegative(value, fallback = 0) {
    const number = Number(value);
    return Number.isFinite(number) && number >= 0 ? number : fallback;
}

function safeObject(value) {
    return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
}

function safeRunStatus(value) {
    const normalized = String(value || '').trim().toLowerCase();
    return SAFE_RUN_STATUSES.has(normalized) ? normalized : 'idle';
}

function hasDoubleEncodedDerivedResource(preview) {
    const readiness = safeObject(read(preview, 'buildReadiness'));
    const resources = [
        ...(Array.isArray(read(readiness, 'missingResources')) ? read(readiness, 'missingResources') : []),
        ...(Array.isArray(read(readiness, 'blockers'))
            ? read(readiness, 'blockers').map(blocker => read(blocker, 'resource')).filter(Boolean)
            : [])
    ];
    return resources.some(resource => {
        const canonicalId = String(read(resource, 'canonicalId') || '').trim().toLowerCase();
        const parts = canonicalId.split('|');
        if (parts.length !== 4 || parts[0] !== 'resource:v1' || parts[1] !== 'resource') return false;
        const encodedIdentity = `${parts[2]}${parts[3]}`.replace(/[^a-z0-9]/g, '');
        return encodedIdentity.includes('resourcev1resource');
    });
}

export function normalizeWorkspaceSnapshotForRestore(raw) {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
        return { snapshot: null, trusted: false, degraded: true, reason: 'invalid_snapshot' };
    }

    const schemaVersion = Number(read(raw, 'schemaVersion'));
    const versionSupported = Number.isInteger(schemaVersion) && SUPPORTED_WORKSPACE_SNAPSHOT_VERSIONS.has(schemaVersion);
    const lifecycleState = String(read(raw, 'lifecycleState') || 'idle').trim().toLowerCase();
    const lifecycleSupported = SAFE_LIFECYCLE_STATES.has(lifecycleState);
    const pendingPlanSnapshot = safeObject(read(raw, 'pendingPlanSnapshot'));
    const hasPlanIdentity = Boolean(
        String(pendingPlanSnapshot.planId || pendingPlanSnapshot.PlanId || '').trim() &&
        String(pendingPlanSnapshot.planHash || pendingPlanSnapshot.PlanHash || '').trim()
    );
    const trusted = versionSupported && lifecycleSupported && (Object.keys(pendingPlanSnapshot).length === 0 || hasPlanIdentity);
    const appliedDowngraded = trusted && lifecycleState === 'applied';
    const safeLifecycle = trusted ? (appliedDowngraded ? 'build' : lifecycleState) : 'idle';
    const requirementMode = trusted && String(read(raw, 'requirementMode')).trim().toLowerCase() === 'draft' ? 'draft' : 'strict';
    const answerRevision = trusted ? finiteNonNegative(read(raw, 'answerRevision')) : 0;
    const resourceRevision = trusted ? finiteNonNegative(read(raw, 'resourceRevision')) : 0;
    const candidateReadiness = trusted && !appliedDowngraded ? (read(raw, 'readinessPreview') || null) : null;
    const readinessMatches = Boolean(candidateReadiness && hasPlanIdentity &&
        String(read(candidateReadiness, 'planId') || '').trim() === String(pendingPlanSnapshot.planId || pendingPlanSnapshot.PlanId || '').trim() &&
        String(read(candidateReadiness, 'planHash') || '').trim() === String(pendingPlanSnapshot.planHash || pendingPlanSnapshot.PlanHash || '').trim() &&
        (String(read(candidateReadiness, 'requirementMode') || '').trim().toLowerCase() === 'draft' ? 'draft' : 'strict') === requirementMode &&
        finiteNonNegative(read(candidateReadiness, 'answerRevision')) === answerRevision &&
        finiteNonNegative(read(candidateReadiness, 'resourceRevision')) === resourceRevision &&
        !hasDoubleEncodedDerivedResource(candidateReadiness));
    const readinessStale = Boolean(trusted && !appliedDowngraded && hasPlanIdentity && !readinessMatches);
    const persistedMissingResources = trusted && Array.isArray(read(raw, 'missingResources')) ? read(raw, 'missingResources') : [];
    const previewMissingResources = readinessMatches && Array.isArray(read(read(candidateReadiness, 'buildReadiness'), 'missingResources'))
        ? read(read(candidateReadiness, 'buildReadiness'), 'missingResources')
        : [];

    const snapshot = {
        schemaVersion: versionSupported ? schemaVersion : 0,
        revision: finiteNonNegative(read(raw, 'revision')),
        recoveryRunIds: {
            plan: String(read(raw, 'planRunId') || '').trim(),
            build: String(read(raw, 'buildRunId') || '').trim()
        },
        lifecycleState: safeLifecycle,
        pendingPlanSnapshot: hasPlanIdentity ? pendingPlanSnapshot : null,
        planQuestionSelections: trusted ? safeObject(read(raw, 'planQuestionSelections')) : {},
        confirmedPlanAnswers: trusted && Array.isArray(read(raw, 'confirmedPlanAnswers')) ? read(raw, 'confirmedPlanAnswers') : [],
        optimisticPlanAnswers: trusted && Array.isArray(read(raw, 'optimisticPlanAnswers')) ? read(raw, 'optimisticPlanAnswers') : [],
        answerRevision,
        readinessPreview: readinessMatches ? candidateReadiness : null,
        readinessStale,
        missingResources: readinessMatches
            ? previewMissingResources
            : (readinessStale ? [] : persistedMissingResources),
        resourceDecisions: trusted ? safeObject(read(raw, 'resourceDecisions')) : {},
        resourceRevision,
        requirementMode,
        workspaceViewMode: trusted && String(read(raw, 'workspaceViewMode')).trim().toLowerCase() === 'build' ? 'build' : 'plan',
        planAcceptedRecommendedDefaults: trusted && read(raw, 'planAcceptedRecommendedDefaults') === true,
        planRunId: trusted ? String(read(raw, 'planRunId') || '').trim() : '',
        planRunStatus: trusted ? safeRunStatus(read(raw, 'planRunStatus')) : 'idle',
        planTerminalSequence: trusted ? finiteNonNegative(read(raw, 'planTerminalSequence'), null) : null,
        buildRunId: trusted && !appliedDowngraded ? String(read(raw, 'buildRunId') || '').trim() : '',
        buildRunStatus: trusted && !appliedDowngraded ? safeRunStatus(read(raw, 'buildRunStatus')) : 'idle',
        buildTerminalSequence: trusted && !appliedDowngraded ? finiteNonNegative(read(raw, 'buildTerminalSequence'), null) : null,
        submittedBuildFingerprint: trusted && !appliedDowngraded ? String(read(raw, 'submittedBuildFingerprint') || '').trim() : '',
        trusted,
        degraded: !trusted,
        appliedDowngraded,
        degradationReason: !versionSupported
            ? (Number.isFinite(schemaVersion) && schemaVersion > 0 ? 'unsupported_version' : 'missing_version')
            : (lifecycleState === 'recovery_conflict' ? 'recovery_conflict'
                : (!lifecycleSupported ? 'invalid_lifecycle' : 'invalid_plan'))
    };

    return {
        snapshot,
        trusted,
        degraded: !trusted,
        reason: snapshot.degradationReason || ''
    };
}

export const aiPanelSnapshotRecoveryTestApi = {
    SAFE_LIFECYCLE_STATES,
    SAFE_RUN_STATUSES,
    SUPPORTED_WORKSPACE_SNAPSHOT_VERSIONS
};
