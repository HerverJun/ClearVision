const SUPPORTED_WORKSPACE_SNAPSHOT_VERSIONS = new Set([1, 2]);
const SAFE_LIFECYCLE_STATES = new Set([
    'idle',
    'routing',
    'clarifying',
    'plan_running',
    'plan_ready',
    'plan_failed',
    'plan_cancelled',
    'plan',
    'build',
    'building',
    'build_failed',
    'build_cancelled',
    'applied'
]);
const SAFE_RUN_STATUSES = new Set(['idle', 'running', 'completed', 'failed', 'cancelled', 'canceled']);

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

    const snapshot = {
        schemaVersion: versionSupported ? schemaVersion : 0,
        revision: trusted ? finiteNonNegative(read(raw, 'revision')) : 0,
        lifecycleState: safeLifecycle,
        pendingPlanSnapshot: hasPlanIdentity ? pendingPlanSnapshot : null,
        planQuestionSelections: trusted ? safeObject(read(raw, 'planQuestionSelections')) : {},
        confirmedPlanAnswers: trusted && Array.isArray(read(raw, 'confirmedPlanAnswers')) ? read(raw, 'confirmedPlanAnswers') : [],
        optimisticPlanAnswers: trusted && Array.isArray(read(raw, 'optimisticPlanAnswers')) ? read(raw, 'optimisticPlanAnswers') : [],
        answerRevision: trusted ? finiteNonNegative(read(raw, 'answerRevision')) : 0,
        readinessPreview: trusted && !appliedDowngraded ? (read(raw, 'readinessPreview') || null) : null,
        resourceDecisions: trusted ? safeObject(read(raw, 'resourceDecisions')) : {},
        requirementMode: trusted && String(read(raw, 'requirementMode')).trim().toLowerCase() === 'draft' ? 'draft' : 'strict',
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
            : (!lifecycleSupported ? 'invalid_lifecycle' : 'invalid_plan')
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
