const CATEGORY_DEFINITIONS = Object.freeze({
    ok: { key: 'ok', label: 'OK', tone: 'ok' },
    ng: { key: 'ng', label: 'NG', tone: 'ng' },
    undetermined: { key: 'undetermined', label: '未判定', tone: 'warning' },
    notApplicable: { key: 'notApplicable', label: '不适用', tone: 'muted' },
    invalid: { key: 'invalid', label: '判定无效', tone: 'invalid' },
    failed: { key: 'failed', label: '执行失败', tone: 'error' },
    cancelled: { key: 'cancelled', label: '已取消', tone: 'muted' },
    timedOut: { key: 'timedOut', label: '执行超时', tone: 'error' },
    skipped: { key: 'skipped', label: '未检测', tone: 'muted' }
});

function readFirst(source, ...keys) {
    for (const key of keys) {
        if (source?.[key] !== undefined && source?.[key] !== null) {
            return source[key];
        }
    }
    return null;
}

function normalizeToken(value) {
    return String(value ?? '').trim().toLowerCase().replace(/[\s_-]/g, '');
}

function projectLegacyStatus(status) {
    switch (normalizeToken(status)) {
        case 'ok':
        case '2':
            return { executionOutcome: 'Succeeded', decisionOutcome: 'Ok', isLegacyProjection: true };
        case 'ng':
        case '3':
            return { executionOutcome: 'Succeeded', decisionOutcome: 'Ng', isLegacyProjection: true };
        case 'error':
        case '4':
            return { executionOutcome: 'Failed', decisionOutcome: 'Undetermined', isLegacyProjection: true };
        case 'failed':
        case 'fail':
            return { executionOutcome: 'Failed', decisionOutcome: 'Undetermined', isLegacyProjection: true };
        case 'cancelled':
        case 'canceled':
            return { executionOutcome: 'Cancelled', decisionOutcome: 'NotApplicable', isLegacyProjection: true };
        case 'timedout':
        case 'timeout':
            return { executionOutcome: 'TimedOut', decisionOutcome: 'Undetermined', isLegacyProjection: true };
        case 'notinspected':
        case '1':
            return { executionOutcome: 'Skipped', decisionOutcome: 'NotApplicable', isLegacyProjection: true };
        case 'inspecting':
        case '0':
            return { executionOutcome: 'Succeeded', decisionOutcome: 'Undetermined', isLegacyProjection: true };
        default:
            return { executionOutcome: 'Succeeded', decisionOutcome: 'Undetermined', isLegacyProjection: true };
    }
}

export function normalizeCanonicalOutcome(source = {}) {
    const executionValue = readFirst(source, 'executionOutcome', 'ExecutionOutcome', 'execution');
    const decisionValue = readFirst(source, 'decisionOutcome', 'DecisionOutcome', 'decision');
    const hasCanonical = executionValue !== null && decisionValue !== null;
    const projected = hasCanonical
        ? {
            executionOutcome: String(executionValue),
            decisionOutcome: String(decisionValue),
            isLegacyProjection: false
        }
        : projectLegacyStatus(readFirst(source, 'status', 'Status', 'inspectionStatus', 'InspectionStatus', 'outcome', 'Outcome'));

    const execution = normalizeToken(projected.executionOutcome);
    const decision = normalizeToken(projected.decisionOutcome);
    let category = 'invalid';

    if (execution === 'failed') category = 'failed';
    else if (execution === 'cancelled' || execution === 'canceled') category = 'cancelled';
    else if (execution === 'timedout' || execution === 'timeout') category = 'timedOut';
    else if (execution === 'skipped') category = 'skipped';
    else if (execution === 'succeeded') {
        if (decision === 'ok') category = 'ok';
        else if (decision === 'ng') category = 'ng';
        else if (decision === 'undetermined') category = 'undetermined';
        else if (decision === 'notapplicable') category = 'notApplicable';
        else category = 'invalid';
    }

    return {
        executionOutcome: projected.executionOutcome,
        decisionOutcome: projected.decisionOutcome,
        isLegacyProjection: projected.isLegacyProjection,
        category,
        ...CATEGORY_DEFINITIONS[category]
    };
}

export function calculateCanonicalStatistics(records = []) {
    const statistics = {
        total: 0,
        executionSucceeded: 0,
        validDecisions: 0,
        ok: 0,
        ng: 0,
        undetermined: 0,
        notApplicable: 0,
        invalid: 0,
        failed: 0,
        cancelled: 0,
        timedOut: 0,
        skipped: 0,
        executionFailures: 0,
        yieldRate: 0,
        decisionCoverageRate: 0
    };

    for (const record of Array.isArray(records) ? records : []) {
        const outcome = normalizeCanonicalOutcome(record);
        statistics.total += 1;
        statistics[outcome.category] += 1;
        if (normalizeToken(outcome.executionOutcome) === 'succeeded') {
            statistics.executionSucceeded += 1;
        }
    }

    statistics.validDecisions = statistics.ok + statistics.ng;
    statistics.executionFailures = statistics.failed + statistics.timedOut;
    statistics.yieldRate = statistics.validDecisions > 0
        ? statistics.ok / statistics.validDecisions
        : 0;
    statistics.decisionCoverageRate = statistics.executionSucceeded > 0
        ? statistics.validDecisions / statistics.executionSucceeded
        : 0;
    return statistics;
}

export function normalizeCanonicalStatistics(source = {}) {
    const readNumber = (...keys) => {
        const value = readFirst(source, ...keys);
        if (value === null) return null;
        const numeric = Number(value);
        return Number.isFinite(numeric) ? numeric : 0;
    };
    const valueOrZero = (value) => value === null ? 0 : value;
    const ok = valueOrZero(readNumber('okCount', 'OKCount', 'ok', 'Ok'));
    const ng = valueOrZero(readNumber('ngCount', 'NGCount', 'ng', 'Ng'));
    const canonicalValidDecisions = readNumber('validDecisionCount', 'ValidDecisionCount');
    const validDecisions = canonicalValidDecisions === null ? ok + ng : canonicalValidDecisions;
    const executionSucceeded = valueOrZero(readNumber('executionSucceededCount', 'ExecutionSucceededCount'));
    const failed = valueOrZero(readNumber('failedCount', 'FailedCount'));
    const timedOut = valueOrZero(readNumber('timedOutCount', 'TimedOutCount'));
    const canonicalExecutionFailures = readNumber('executionFailureCount', 'ExecutionFailureCount');
    const hasCanonicalFailureBreakdown = readNumber('failedCount', 'FailedCount') !== null ||
        readNumber('timedOutCount', 'TimedOutCount') !== null;
    const executionFailures = canonicalExecutionFailures !== null
        ? canonicalExecutionFailures
        : hasCanonicalFailureBreakdown
            ? failed + timedOut
            : valueOrZero(readNumber('errorCount', 'ErrorCount', 'error', 'Error'));
    const yieldRateValue = readFirst(source, 'yieldRate', 'YieldRate');
    const coverageValue = readFirst(source, 'decisionCoverageRate', 'DecisionCoverageRate');

    return {
        total: valueOrZero(readNumber('totalAttemptCount', 'TotalAttemptCount', 'totalCount', 'TotalCount', 'total', 'Total')),
        executionSucceeded,
        validDecisions,
        ok,
        ng,
        undetermined: valueOrZero(readNumber('undeterminedCount', 'UndeterminedCount')),
        notApplicable: valueOrZero(readNumber('notApplicableCount', 'NotApplicableCount')),
        invalid: valueOrZero(readNumber('invalidCount', 'InvalidCount')),
        failed,
        cancelled: valueOrZero(readNumber('cancelledCount', 'CancelledCount', 'canceledCount', 'CanceledCount')),
        timedOut,
        skipped: valueOrZero(readNumber('skippedCount', 'SkippedCount')),
        executionFailures,
        yieldRate: yieldRateValue === null ? (validDecisions > 0 ? ok / validDecisions : 0) : Number(yieldRateValue),
        decisionCoverageRate: coverageValue === null
            ? (executionSucceeded > 0 ? validDecisions / executionSucceeded : 0)
            : Number(coverageValue),
        avgTime: Math.round(valueOrZero(readNumber(
            'averageProcessingTimeMs',
            'AverageProcessingTimeMs',
            'averageExecutionTimeMs',
            'AverageExecutionTimeMs',
            'avgTime')))
    };
}

export function matchesCanonicalOutcomeFilter(record, filter) {
    if (!filter || normalizeToken(filter) === 'all') {
        return true;
    }
    return normalizeToken(normalizeCanonicalOutcome(record).category) === normalizeToken(filter);
}

export function getCanonicalOutcomeDefinition(category) {
    return CATEGORY_DEFINITIONS[category] || CATEGORY_DEFINITIONS.invalid;
}
