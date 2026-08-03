import { mergeCanonicalResources, normalizeCanonicalResource, serializeCanonicalResource } from './aiResourceIdentity.js';

export const AgentWorkspaceEventTypes = Object.freeze({
    RESET: 'workspace/reset',
    SESSION_ADOPTED: 'workspace/session-adopted',
    SESSION_RESTORED: 'workspace/session-restored',
    INTENT_RESOLVED: 'workspace/intent-resolved',
    PLAN_RECEIVED: 'workspace/plan-received',
    PLAN_CLEARED: 'workspace/plan-cleared',
    RESULT_RECEIVED: 'workspace/result-received',
    REQUIREMENT_MODE_CHANGED: 'workspace/requirement-mode-changed',
    ANSWERS_REPLACED: 'workspace/answers-replaced',
    ANSWER_REVISION_SET: 'workspace/answer-revision-set',
    SELECTION_SET: 'workspace/selection-set',
    ANSWER_OPTIMISTIC_SET: 'workspace/answer-optimistic-set',
    ANSWERS_CONFIRMED: 'workspace/answers-confirmed',
    CLARIFICATION_BATCH_SUBMITTED: 'workspace/clarification-batch-submitted',
    READINESS_REQUESTED: 'workspace/readiness-requested',
    READINESS_RECEIVED: 'workspace/readiness-received',
    READINESS_FAILED: 'workspace/readiness-failed',
    READINESS_CLEARED: 'workspace/readiness-cleared',
    READINESS_STATUS_CHANGED: 'workspace/readiness-status-changed',
    RESOURCE_DECISION_SET: 'workspace/resource-decision-set',
    RUN_STARTED: 'workspace/run-started',
    RUN_EVENT_RECEIVED: 'workspace/run-event-received',
    RUN_RESET: 'workspace/run-reset',
    RUN_PATCHED: 'workspace/run-patched',
    VIEW_CHANGED: 'workspace/view-changed',
    WORKSPACE_MODE_CHANGED: 'workspace/mode-changed',
    BUILD_SUBMITTED: 'workspace/build-submitted',
    APPLY_COMPLETED: 'workspace/apply-completed',
    PERSISTENCE_UPDATED: 'workspace/persistence-updated'
});

const TERMINAL_EVENT_TYPES = new Set(['run.completed', 'run.failed', 'run.cancelled']);
const ANSWER_ORIGIN_PRIORITY = Object.freeze({
    explicit_user_text: 60,
    explicit_user_selection: 50,
    resource_bound: 40,
    model_inferred: 30,
    accepted_recommended_default: 20,
    default_assumption: 10,
    legacy_inferred: 0
});
const PLACEHOLDER_VALUES = new Set([
    'custom',
    'custom_input',
    'other',
    'unknown',
    'unspecified',
    'metadata_only',
    'pending'
]);
const AUTHORITATIVE_ANSWER_ORIGINS = new Set([
    'explicit_user_selection',
    'explicit_user_text',
    'accepted_recommended_default',
    'resource_bound'
]);

const asArray = value => Array.isArray(value) ? value : [];
const asObject = value => value && typeof value === 'object' && !Array.isArray(value) ? value : {};
const read = (value, camel, pascal = '') => asObject(value)[camel] ?? asObject(value)[pascal || `${camel[0].toUpperCase()}${camel.slice(1)}`];
const clean = value => String(value ?? '').trim();
const lower = value => clean(value).toLowerCase();
const unique = values => [...new Set(asArray(values).map(clean).filter(Boolean))];
const cloneMap = value => ({ ...asObject(value) });

export function normalizeCanonicalField(value) {
    const normalized = lower(value).replace(/[\s.-]+/g, '_');
    if (!normalized) return '';
    if (/inspection_object|object_type|product_type|part_type|inspection_target|detection_target/.test(normalized)) return 'inspection_object';
    if (/task_type|task_category|detection_task|inspection_task|visual_task|medical_modality|lesion_type/.test(normalized)) return 'task_type';
    if (/image_source|image_input|input_source|source_image|camera_source|image_source_roi/.test(normalized)) return 'image_source';
    if (/acceptance_criteria|ok_ng|ok_condition|ng_condition|judgment_rule|result_rule|presence_judgment|decode_policy|sequence_rule/.test(normalized)) return 'acceptance_criteria';
    if (/output_target|output_goal|output_destination|result_output|local_result_payload|structured_result|business_system/.test(normalized)) return 'output_target';
    if (/algorithm_strategy|model_or_rule_strategy|classification_strategy/.test(normalized)) return 'algorithm_strategy';
    if (/roi_strategy/.test(normalized)) return 'roi_strategy';
    if (/template_strategy|template_asset/.test(normalized)) return 'template_strategy';
    if (/target_attribute|attribute_target/.test(normalized)) return 'target_attribute';
    if (/defect_type|defect_definition/.test(normalized)) return 'defect_type';
    if (/measurement_target/.test(normalized)) return 'measurement_target';
    return normalized;
}

export function isPlaceholderAnswer(value) {
    const normalized = lower(value);
    return !normalized || PLACEHOLDER_VALUES.has(normalized) || normalized.endsWith('_pending');
}

export function normalizeWorkspaceAnswer(value, fallback = {}) {
    const item = asObject(value);
    const field = normalizeCanonicalField(read(item, 'field') || read(fallback, 'field'));
    const answerValue = clean(read(item, 'value'));
    if (!field || !answerValue || isPlaceholderAnswer(answerValue)) return null;
    const origin = lower(read(item, 'origin'));
    const normalizedOrigin = ANSWER_ORIGIN_PRIORITY[origin] === undefined ? 'legacy_inferred' : origin;
    return {
        field,
        questionId: clean(read(item, 'questionId') || read(fallback, 'questionId')),
        value: answerValue,
        origin: normalizedOrigin,
        confidence: Number(read(item, 'confidence') ?? 1) || 1,
        resolved: read(item, 'resolved') !== false && AUTHORITATIVE_ANSWER_ORIGINS.has(normalizedOrigin)
    };
}

function normalizeAnswerMap(value) {
    const result = {};
    const candidates = Array.isArray(value) ? value : Object.values(asObject(value));
    candidates.forEach(candidate => {
        const answer = normalizeWorkspaceAnswer(candidate);
        if (!answer) return;
        const existing = result[answer.field];
        const existingPriority = ANSWER_ORIGIN_PRIORITY[existing?.origin] ?? -1;
        const nextPriority = ANSWER_ORIGIN_PRIORITY[answer.origin] ?? -1;
        if (!existing || nextPriority >= existingPriority) result[answer.field] = answer;
    });
    return result;
}

function normalizeQuestion(value) {
    const item = asObject(value);
    const id = clean(read(item, 'id'));
    const field = normalizeCanonicalField(read(item, 'field') || id);
    const options = asArray(read(item, 'options')).map(option => {
        const optionItem = asObject(option);
        return {
            value: clean(read(optionItem, 'value')),
            label: clean(read(optionItem, 'label')),
            recommended: read(optionItem, 'recommended') === true,
            answerEffect: lower(read(optionItem, 'answerEffect')) || 'resolve_field',
            recommendationReason: clean(read(optionItem, 'recommendationReason')),
            description: clean(read(optionItem, 'description')),
            impact: clean(read(optionItem, 'impact'))
        };
    }).filter(option => option.value && option.label);
    if (!id && !field) return null;
    return {
        kind: 'question',
        id: id || `question:${field}`,
        questionId: id,
        field,
        title: clean(read(item, 'title')) || clean(read(item, 'publicLabel')) || field,
        why: clean(read(item, 'why')),
        impact: clean(read(item, 'impact')),
        defaultValue: clean(read(item, 'defaultValue')),
        defaultAssumption: clean(read(item, 'defaultAssumption')),
        options,
        interactive: options.length >= 2,
        blocksBuild: true,
        source: 'clarification_question'
    };
}

function normalizeBlocker(value) {
    const item = asObject(value);
    const id = clean(read(item, 'id'));
    const category = lower(read(item, 'category'));
    if (category === 'resource_pending') {
        return normalizeCanonicalResource(item, { source: 'build_readiness' });
    }
    const resourceKey = category === 'resource_pending'
        ? clean(read(item, 'resourceKey')) || id.replace(/^resource_pending:/i, '').replace(/^resource:/i, '')
        : '';
    const field = normalizeCanonicalField(read(item, 'field') || read(item, 'questionId') || id);
    if (!id && !field) return null;
    return {
        kind: category === 'resource_pending' ? 'resource' : 'blocker',
        id: id || `blocker:${field}`,
        resourceKey,
        questionId: clean(read(item, 'questionId')),
        field,
        title: clean(read(item, 'publicLabel')) || field || id,
        why: '',
        impact: '',
        options: [],
        interactive: false,
        blocksBuild: read(item, 'blocksBuild') === true,
        category,
        resolutionMode: lower(read(item, 'resolutionMode')),
        source: 'build_readiness'
    };
}

function normalizeMissingResource(value, index = 0) {
    return normalizeCanonicalResource(value, { source: value?.source || 'missing_resource', index });
}

function normalizeReadiness(value) {
    const item = asObject(value);
    return {
        canBuild: read(item, 'canBuild') === true,
        blockers: asArray(read(item, 'blockers')).map(normalizeBlocker).filter(Boolean),
        resolvedFields: unique(asArray(read(item, 'resolvedFields')).map(normalizeCanonicalField)),
        remainingFields: unique(asArray(read(item, 'remainingFields')).map(normalizeCanonicalField)),
        primaryMessage: clean(read(item, 'primaryMessage')),
        contractVersion: clean(read(item, 'contractVersion')) || 'v2',
        missingResources: asArray(read(item, 'missingResources'))
            .map((resource, index) => normalizeMissingResource(resource, index))
    };
}

function derivePhase(state, plan, readiness, queue) {
    if (state.apply.status === 'applied') return 'applied';
    if (state.run.build.status === 'running') return 'building';
    if (state.run.build.status === 'failed') return 'build_failed';
    if (!plan) return state.intent ? 'routing' : 'idle';
    if (queue.some(item => item.blocksBuild && !item.answered && !item.deferred)) return 'clarifying';
    return readiness.canBuild ? 'ready_to_build' : 'plan_blocked';
}

export function deriveAgentWorkspaceProjection(state) {
    const plan = state.plan;
    if (!plan) {
        return {
            phase: derivePhase(state, null, state.readiness, []),
            confirmedAnswers: [],
            optimisticAnswers: [],
            answersByField: {},
            clarificationQueue: [],
            clarificationBatch: [],
            missingResources: [],
            readiness: state.readiness,
            buildAction: { canBuild: false, canStart: false, status: 'no_plan' }
        };
    }

    const confirmed = normalizeAnswerMap(
        read(plan, 'confirmedPlanAnswers') || read(read(plan, 'rawPlanSnapshot'), 'confirmedPlanAnswers'));
    Object.assign(confirmed, state.answers.confirmedByField);
    const optimistic = cloneMap(state.answers.optimisticByField);
    const answersByField = { ...confirmed };
    Object.values(optimistic).forEach(answer => {
        const current = answersByField[answer.field];
        const currentPriority = ANSWER_ORIGIN_PRIORITY[current?.origin] ?? -1;
        const optimisticPriority = ANSWER_ORIGIN_PRIORITY[answer.origin] ?? -1;
        if (!current || optimisticPriority >= currentPriority) answersByField[answer.field] = answer;
    });

    const readiness = state.readiness.contractVersion
        ? state.readiness
        : normalizeReadiness(read(plan, 'buildReadiness'));
    const result = asObject(state.result);
    const buildResult = asObject(read(result, 'buildResult'));
    const resultMissingResources = read(result, 'missingResources') || read(buildResult, 'missingResources');
    const resources = [
        ...asArray(read(plan, 'missingResources') || read(read(plan, 'rawPlanSnapshot'), 'missingResources')),
        ...asArray(resultMissingResources),
        ...asArray(readiness.missingResources),
        ...Object.values(state.resources.missingByKey)
    ].map(normalizeMissingResource);
    const questions = asArray(read(plan, 'clarificationQuestions') || read(plan, 'questions')).map(normalizeQuestion).filter(Boolean);
    const blockers = asArray(readiness.blockers).map(item => item?.source ? item : normalizeBlocker(item)).filter(Boolean);
    const blockersByField = new Map();
    blockers.filter(item => item.kind !== 'resource').forEach(item => {
        const field = normalizeCanonicalField(item.field);
        if (field) blockersByField.set(field, item);
    });
    const queueByKey = new Map();
    questions.forEach(item => {
        const field = normalizeCanonicalField(item.field);
        const key = field || item.questionId || item.resourceKey || item.id;
        if (!key) return;
        const answer = field ? answersByField[field] : null;
        const selectedValue = item.questionId ? state.answers.selectionByQuestion[item.questionId] : '';
        const selectedOption = asArray(item.options).find(option => option.value === selectedValue);
        const blocker = field ? blockersByField.get(field) : null;
        const normalized = {
            ...item,
            ...(blocker ? {
                blocksBuild: item.blocksBuild || blocker.blocksBuild,
                category: blocker.category || item.category,
                resolutionMode: blocker.resolutionMode || item.resolutionMode,
                sources: unique([item.source, blocker.source])
            } : {}),
            field,
            answer: answer || null,
            answered: Boolean(answer?.resolved),
            deferred: selectedOption?.answerEffect === 'defer',
            selectedValue,
            resourceDecision: null
        };
        queueByKey.set(key, normalized);
    });

    const queue = [...queueByKey.values()];
    const unresolved = queue.filter(item => !item.answered && !item.deferred);
    const requestedBatchKeys = asArray(state.clarification?.batchKeys);
    const clarificationBatch = requestedBatchKeys.length
        ? requestedBatchKeys.map(key => queue.find(item => (item.field || item.id) === key)).filter(Boolean)
        : unresolved.filter(item => item.interactive && asArray(item.options).length >= 2).slice(0, 3);
    const missingResources = mergeCanonicalResources([
        ...resources,
        ...blockers.filter(item => item.kind === 'resource')
    ]).map(item => {
        const decision = state.resources.decisionsByKey[item.canonicalId] || null;
        return {
            ...item,
            kind: 'resource',
            resourceDecision: decision,
            deferred: decision?.status === 'deferred',
            answered: decision?.status === 'bound' &&
                !asArray(item.sources).some(source => ['build_readiness', 'readiness'].includes(lower(source)))
        };
    });
    const phase = derivePhase(state, plan, readiness, queue);
    return {
        phase,
        confirmedAnswers: Object.values(confirmed),
        optimisticAnswers: Object.values(optimistic),
        answersByField,
        clarificationQueue: queue,
        clarificationBatch,
        missingResources,
        readiness,
        buildAction: {
            canBuild: readiness.canBuild === true,
            canStart: readiness.canBuild === true && state.run.build.status !== 'running',
            status: readiness.canBuild === true ? 'ready' : phase
        }
    };
}

export function createAgentWorkspaceState(seed = {}) {
    const state = {
        schemaVersion: 1,
        revision: Number(seed.revision) || 0,
        identity: {
            sessionId: clean(seed.sessionId),
            planId: '',
            planHash: '',
            planRevision: 0
        },
        intent: null,
        plan: null,
        result: null,
        requirementMode: lower(seed.requirementMode) === 'draft' ? 'draft' : 'strict',
        answers: { confirmedByField: {}, optimisticByField: {}, selectionByQuestion: {}, answerRevision: 0, lastSubmittedBatch: [] },
        readiness: { canBuild: false, blockers: [], resolvedFields: [], remainingFields: [], primaryMessage: '', contractVersion: '', missingResources: [] },
        readinessPreview: null,
        readinessStatus: 'idle',
        readinessError: '',
        readinessRequest: null,
        resources: { missingByKey: {}, decisionsByKey: {}, revision: 0 },
        clarification: { batchKeys: [], batchRevision: 0 },
        run: {
            plan: { runId: '', status: 'idle', events: [], eventKeys: {}, terminalSequence: null },
            build: { runId: '', status: 'idle', events: [], eventKeys: {}, terminalSequence: null }
        },
        apply: { status: 'idle', revision: 0 },
        ui: { workspaceMode: 'plan', viewMode: lower(seed.viewMode) === 'build' ? 'build' : 'plan' },
        persistence: {
            snapshotRevision: 0,
            dirty: false,
            mutationGeneration: 0,
            persistedGeneration: 0,
            pendingMutationCount: 0,
            buildRunId: '',
            submittedBuildFingerprint: ''
        },
        projection: null,
        lastEvent: null
    };
    state.projection = deriveAgentWorkspaceProjection(state);
    return state;
}

function withProjection(state, event) {
    let next = { ...state, revision: state.revision + 1, lastEvent: event };
    next.projection = deriveAgentWorkspaceProjection(next);
    if (next.plan && !asArray(next.clarification?.batchKeys).length) {
        const batchKeys = next.projection.clarificationBatch
            .map(item => item.field || item.id)
            .filter(Boolean)
            .slice(0, 3);
        if (batchKeys.length) {
            next = { ...next, clarification: { ...next.clarification, batchKeys } };
            next.projection = deriveAgentWorkspaceProjection(next);
        }
    }
    return next;
}

function isEventStale(state, event) {
    if (event.type === AgentWorkspaceEventTypes.SESSION_RESTORED ||
        event.type === AgentWorkspaceEventTypes.SESSION_ADOPTED ||
        event.type === AgentWorkspaceEventTypes.RESET) {
        return false;
    }
    const sessionId = clean(event.sessionId || event.payload?.sessionId);
    if (sessionId && state.identity.sessionId && lower(sessionId) !== lower(state.identity.sessionId)) return true;
    const eventRevision = Number(event.revision ?? event.payload?.revision);
    if (Number.isFinite(eventRevision) && eventRevision > 0 && eventRevision < state.identity.planRevision) return true;
    const planId = clean(event.planId || event.payload?.planId);
    const planHash = clean(event.planHash || event.payload?.planHash);
    if (event.type !== AgentWorkspaceEventTypes.PLAN_RECEIVED && event.type !== AgentWorkspaceEventTypes.SESSION_RESTORED) {
        if (planId && state.identity.planId && planId !== state.identity.planId) return true;
        if (planHash && state.identity.planHash && planHash !== state.identity.planHash) return true;
    }
    return false;
}

function reduceRunEvent(state, event) {
    const payload = asObject(event.payload);
    const normalizedEvent = asObject(payload.event || payload);
    const kind = lower(payload.kind || event.kind) === 'plan' ? 'plan' : 'build';
    const runId = clean(read(normalizedEvent, 'runId') || payload.runId || event.runId);
    const current = state.run[kind];
    if (!runId || (current.runId && current.runId !== runId)) return state;
    const sequence = Number(read(normalizedEvent, 'sequence'));
    const eventType = lower(read(normalizedEvent, 'eventType'));
    const eventId = clean(read(normalizedEvent, 'eventId'));
    const key = eventId || `${runId}:${Number.isFinite(sequence) ? sequence : 'na'}:${eventType}`;
    if (current.eventKeys[key]) return state;
    const isTerminal = TERMINAL_EVENT_TYPES.has(eventType);
    if (current.status !== 'idle' && TERMINAL_EVENT_TYPES.has(`run.${current.status}`) && !isTerminal) return state;
    if (isTerminal && current.terminalSequence !== null && Number.isFinite(sequence) && sequence <= current.terminalSequence) return state;
    const status = eventType === 'run.completed'
        ? 'completed'
        : eventType === 'run.failed'
            ? 'failed'
            : eventType === 'run.cancelled'
                ? 'cancelled'
                : 'running';
    const nextRun = {
        ...current,
        runId,
        status,
        events: [...current.events, normalizedEvent],
        eventKeys: { ...current.eventKeys, [key]: true },
        terminalSequence: isTerminal && Number.isFinite(sequence) ? sequence : current.terminalSequence
    };
    return withProjection({
        ...state,
        run: { ...state.run, [kind]: nextRun },
        ui: { ...state.ui, workspaceMode: kind === 'build' ? 'build' : state.ui.workspaceMode }
    }, event);
}

export function agentWorkspaceReducer(state, event) {
    if (!state) state = createAgentWorkspaceState();
    if (!event?.type) return state;
    if (isEventStale(state, event)) return state;
    const payload = asObject(event.payload);

    switch (event.type) {
        case AgentWorkspaceEventTypes.RESET:
            return createAgentWorkspaceState({
                sessionId: payload.preserveSession === false ? '' : state.identity.sessionId,
                requirementMode: payload.requirementMode || 'strict',
                viewMode: 'plan'
            });
        case AgentWorkspaceEventTypes.SESSION_ADOPTED:
            return withProjection({ ...state, identity: { ...state.identity, sessionId: clean(payload.sessionId) } }, event);
        case AgentWorkspaceEventTypes.SESSION_RESTORED: {
            const restored = createAgentWorkspaceState({
                sessionId: clean(payload.sessionId),
                requirementMode: payload.requirementMode,
                viewMode: payload.viewMode
            });
            restored.revision = Math.max(Number(payload.revision) || 0, state.revision + 1);
            restored.identity = {
                sessionId: clean(payload.sessionId),
                planId: clean(payload.planId || read(payload.plan, 'planId')),
                planHash: clean(payload.planHash || read(payload.plan, 'planHash')),
                planRevision: Number(payload.planRevision || payload.revision) || 0
            };
            restored.intent = payload.intent || null;
            restored.plan = payload.plan || null;
            restored.result = payload.result || null;
            restored.answers.confirmedByField = normalizeAnswerMap(payload.confirmedAnswers);
            restored.answers.optimisticByField = normalizeAnswerMap(payload.optimisticAnswers);
            restored.answers.answerRevision = Number(payload.answerRevision) || 0;
            restored.answers.selectionByQuestion = cloneMap(payload.selections);
            restored.readiness = normalizeReadiness(Object.prototype.hasOwnProperty.call(payload, 'readiness')
                ? payload.readiness
                : read(payload.plan, 'buildReadiness'));
            restored.readinessPreview = payload.readinessPreview || null;
            restored.readinessStatus = payload.readinessStatus || (restored.plan
                ? (restored.readiness.canBuild ? 'ready' : 'blocked')
                : 'idle');
            restored.resources.missingByKey = Object.fromEntries(asArray(payload.missingResources).map((item, index) => {
                const resource = normalizeMissingResource(item, index);
                return [resource.canonicalId, serializeCanonicalResource(resource, { source: resource.source || 'workspace_restore' })];
            }));
            restored.resources.decisionsByKey = cloneMap(payload.resourceDecisions);
            restored.resources.revision = Number(payload.resourceRevision) || 0;
            restored.clarification = { ...restored.clarification, ...asObject(payload.clarification) };
            restored.run = payload.run || restored.run;
            restored.apply = payload.apply || restored.apply;
            restored.persistence = { ...restored.persistence, ...asObject(payload.persistence) };
            restored.ui = { ...restored.ui, ...asObject(payload.ui) };
            restored.lastEvent = event;
            restored.projection = deriveAgentWorkspaceProjection(restored);
            return restored;
        }
        case AgentWorkspaceEventTypes.INTENT_RESOLVED:
            return withProjection({ ...state, intent: payload }, event);
        case AgentWorkspaceEventTypes.PLAN_RECEIVED: {
            const plan = payload.plan || payload;
            const planId = clean(read(plan, 'planId'));
            const planHash = clean(read(plan, 'planHash'));
            const samePlan = planId && planHash && planId === state.identity.planId && planHash === state.identity.planHash;
            const incomingAnswerRevision = Number(read(read(plan, 'effectiveReadiness'), 'answerRevision')) || 0;
            const backendAnswers = normalizeAnswerMap(
                read(plan, 'confirmedPlanAnswers') || read(read(plan, 'rawPlanSnapshot'), 'confirmedPlanAnswers'));
            return withProjection({
                ...state,
                plan,
                identity: {
                    ...state.identity,
                    sessionId: clean(event.sessionId || payload.sessionId) || state.identity.sessionId,
                    planId,
                    planHash,
                    planRevision: Math.max(Number(event.revision || payload.revision) || 0, state.identity.planRevision)
                },
                answers: {
                    ...state.answers,
                    confirmedByField: { ...(samePlan ? state.answers.confirmedByField : {}), ...backendAnswers },
                    optimisticByField: samePlan ? state.answers.optimisticByField : {},
                    answerRevision: samePlan ? state.answers.answerRevision : incomingAnswerRevision
                },
                readiness: normalizeReadiness(read(plan, 'buildReadiness')),
                readinessPreview: read(plan, 'effectiveReadiness') || (samePlan ? state.readinessPreview : null),
                readinessStatus: normalizeReadiness(read(plan, 'buildReadiness')).canBuild ? 'ready' : 'blocked',
                readinessError: '',
                readinessRequest: null,
                clarification: samePlan ? state.clarification : { batchKeys: [], batchRevision: 0 },
                ui: { ...state.ui, workspaceMode: 'plan' }
            }, event);
        }
        case AgentWorkspaceEventTypes.PLAN_CLEARED:
            return withProjection({
                ...state,
                plan: null,
                identity: { ...state.identity, planId: '', planHash: '', planRevision: 0 },
                answers: { confirmedByField: {}, optimisticByField: {}, selectionByQuestion: {}, answerRevision: state.answers.answerRevision + 1, lastSubmittedBatch: [] },
                readiness: createAgentWorkspaceState().readiness,
                readinessPreview: null,
                readinessStatus: 'idle',
                resources: { missingByKey: {}, decisionsByKey: {}, revision: state.resources.revision + 1 },
                clarification: { batchKeys: [], batchRevision: state.clarification.batchRevision + 1 },
                ui: { ...state.ui, workspaceMode: 'plan', viewMode: 'plan' }
            }, event);
        case AgentWorkspaceEventTypes.RESULT_RECEIVED:
            return withProjection({
                ...state,
                result: Object.prototype.hasOwnProperty.call(payload, 'result') ? payload.result : payload
            }, event);
        case AgentWorkspaceEventTypes.REQUIREMENT_MODE_CHANGED:
            return withProjection({
                ...state,
                requirementMode: lower(payload.mode) === 'draft' ? 'draft' : 'strict',
                readiness: createAgentWorkspaceState().readiness,
                readinessPreview: null,
                readinessStatus: 'idle',
                readinessError: '',
                readinessRequest: null
            }, event);
        case AgentWorkspaceEventTypes.ANSWERS_REPLACED:
            return withProjection({
                ...state,
                answers: {
                    ...state.answers,
                    optimisticByField: normalizeAnswerMap(payload.answers),
                    selectionByQuestion: cloneMap(payload.selections || state.answers.selectionByQuestion),
                    answerRevision: state.answers.answerRevision + 1
                }
            }, event);
        case AgentWorkspaceEventTypes.ANSWER_REVISION_SET:
            return withProjection({
                ...state,
                answers: {
                    ...state.answers,
                    answerRevision: Math.max(0, Number(payload.revision) || 0)
                }
            }, event);
        case AgentWorkspaceEventTypes.SELECTION_SET:
            return withProjection({
                ...state,
                answers: {
                    ...state.answers,
                    selectionByQuestion: {
                        ...state.answers.selectionByQuestion,
                        [clean(payload.questionId)]: clean(payload.value)
                    },
                    answerRevision: state.answers.answerRevision + 1
                }
            }, event);
        case AgentWorkspaceEventTypes.ANSWER_OPTIMISTIC_SET: {
            const answer = normalizeWorkspaceAnswer(payload.answer || payload, payload.question || {});
            if (!answer) return state;
            return withProjection({
                ...state,
                answers: {
                    ...state.answers,
                    optimisticByField: { ...state.answers.optimisticByField, [answer.field]: answer },
                    selectionByQuestion: answer.questionId
                        ? { ...state.answers.selectionByQuestion, [answer.questionId]: answer.value }
                        : state.answers.selectionByQuestion,
                    answerRevision: state.answers.answerRevision + 1
                }
            }, event);
        }
        case AgentWorkspaceEventTypes.ANSWERS_CONFIRMED: {
            const confirmed = normalizeAnswerMap(payload.answers);
            const optimistic = { ...state.answers.optimisticByField };
            Object.keys(confirmed).forEach(field => delete optimistic[field]);
            const answerRevision = payload.preserveRevision === true
                ? state.answers.answerRevision
                : state.answers.answerRevision + 1;
            return withProjection({
                ...state,
                answers: {
                    ...state.answers,
                    confirmedByField: { ...state.answers.confirmedByField, ...confirmed },
                    optimisticByField: optimistic,
                    answerRevision
                }
            }, event);
        }
        case AgentWorkspaceEventTypes.CLARIFICATION_BATCH_SUBMITTED:
            return withProjection({
                ...state,
                answers: { ...state.answers, lastSubmittedBatch: asArray(payload.answers) },
                clarification: { batchKeys: [], batchRevision: state.clarification.batchRevision + 1 }
            }, event);
        case AgentWorkspaceEventTypes.READINESS_REQUESTED:
            return withProjection({ ...state, readinessStatus: 'validating', readinessError: '', readinessRequest: payload }, event);
        case AgentWorkspaceEventTypes.READINESS_RECEIVED:
            {
            const readiness = normalizeReadiness(payload.buildReadiness || payload.readiness || payload);
            const readinessPreview = payload.buildReadiness ? payload : { ...payload, buildReadiness: payload.readiness || payload };
            const missingByKey = Object.fromEntries(readiness.missingResources.map((item, index) => {
                const resource = normalizeMissingResource(item, index);
                return [resource.canonicalId, serializeCanonicalResource(resource, { source: resource.source || 'readiness' })];
            }));
            return withProjection({
                ...state,
                plan: state.plan ? {
                    ...state.plan,
                    buildReadiness: readiness,
                    effectiveReadiness: readinessPreview,
                    missingResources: readiness.missingResources
                } : state.plan,
                readiness,
                readinessPreview,
                readinessStatus: readiness.canBuild ? 'ready' : 'blocked',
                readinessError: '',
                readinessRequest: null,
                resources: { ...state.resources, missingByKey }
            }, event);
            }
        case AgentWorkspaceEventTypes.READINESS_FAILED:
            return withProjection({ ...state, readinessStatus: clean(payload.status) === 'timeout' ? 'timeout' : 'failed', readinessError: clean(payload.message), readinessRequest: null }, event);
        case AgentWorkspaceEventTypes.READINESS_CLEARED:
            return withProjection({
                ...state,
                readiness: {
                    canBuild: false,
                    blockers: [],
                    resolvedFields: [],
                    remainingFields: [],
                    primaryMessage: '',
                    contractVersion: '',
                    missingResources: []
                },
                readinessPreview: null,
                readinessStatus: 'idle',
                readinessError: '',
                readinessRequest: null
            }, event);
        case AgentWorkspaceEventTypes.READINESS_STATUS_CHANGED:
            return withProjection({
                ...state,
                readinessStatus: clean(payload.status) || 'idle',
                readinessError: clean(payload.message),
                readinessRequest: clean(payload.status) === 'validating' ? state.readinessRequest : null
            }, event);
        case AgentWorkspaceEventTypes.RESOURCE_DECISION_SET: {
            const resource = normalizeMissingResource(payload.resource || {}, 0);
            return withProjection({
                ...state,
                resources: {
                    missingByKey: {
                        ...state.resources.missingByKey,
                        [resource.canonicalId]: serializeCanonicalResource(resource, { source: 'workspace' })
                    },
                    decisionsByKey: { ...state.resources.decisionsByKey, [resource.canonicalId]: asObject(payload.decision) },
                    revision: state.resources.revision + 1
                }
            }, event);
        }
        case AgentWorkspaceEventTypes.RUN_STARTED: {
            const kind = lower(payload.kind) === 'plan' ? 'plan' : 'build';
            const run = { runId: clean(payload.runId), status: 'running', events: [], eventKeys: {}, terminalSequence: null };
            return withProjection({
                ...state,
                run: { ...state.run, [kind]: run },
                ui: { ...state.ui, workspaceMode: kind === 'build' ? 'build' : state.ui.workspaceMode, viewMode: kind === 'build' ? 'build' : state.ui.viewMode }
            }, event);
        }
        case AgentWorkspaceEventTypes.RUN_EVENT_RECEIVED:
            return reduceRunEvent(state, event);
        case AgentWorkspaceEventTypes.RUN_RESET: {
            const kind = lower(payload.kind) === 'plan' ? 'plan' : 'build';
            return withProjection({
                ...state,
                run: { ...state.run, [kind]: { runId: '', status: 'idle', events: [], eventKeys: {}, terminalSequence: null } }
            }, event);
        }
        case AgentWorkspaceEventTypes.RUN_PATCHED: {
            const kind = lower(payload.kind) === 'plan' ? 'plan' : 'build';
            const current = state.run[kind];
            return withProjection({
                ...state,
                run: {
                    ...state.run,
                    [kind]: {
                        ...current,
                        ...asObject(payload.patch),
                        events: payload.patch?.events ? asArray(payload.patch.events) : current.events,
                        eventKeys: payload.patch?.eventKeys ? cloneMap(payload.patch.eventKeys) : current.eventKeys
                    }
                }
            }, event);
        }
        case AgentWorkspaceEventTypes.VIEW_CHANGED:
            return withProjection({ ...state, ui: { ...state.ui, viewMode: lower(payload.mode) === 'build' ? 'build' : 'plan' } }, event);
        case AgentWorkspaceEventTypes.WORKSPACE_MODE_CHANGED:
            return withProjection({
                ...state,
                ui: {
                    ...state.ui,
                    workspaceMode: ['plan', 'build', 'applied'].includes(lower(payload.mode))
                        ? lower(payload.mode)
                        : 'plan'
                }
            }, event);
        case AgentWorkspaceEventTypes.BUILD_SUBMITTED:
            return withProjection({
                ...state,
                ui: { ...state.ui, workspaceMode: 'build', viewMode: 'build' },
                persistence: {
                    ...state.persistence,
                    buildRunId: clean(payload.runId),
                    submittedBuildFingerprint: clean(payload.fingerprint)
                }
            }, event);
        case AgentWorkspaceEventTypes.APPLY_COMPLETED:
            return withProjection({ ...state, apply: { status: 'applied', revision: state.apply.revision + 1 }, ui: { ...state.ui, workspaceMode: 'applied' } }, event);
        case AgentWorkspaceEventTypes.PERSISTENCE_UPDATED:
            return withProjection({ ...state, persistence: { ...state.persistence, ...payload } }, event);
        default:
            return state;
    }
}

export function dispatchAgentWorkspaceEvent(owner, event) {
    if (!owner.agentWorkspaceState) owner.agentWorkspaceState = createAgentWorkspaceState({ sessionId: owner.sessionId });
    const previous = owner.agentWorkspaceState;
    const next = agentWorkspaceReducer(previous, event);
    owner.agentWorkspaceState = next;
    owner._onAgentWorkspaceStateChanged?.(next, previous, event);
    return next;
}

export function createAgentWorkspaceSnapshot(state) {
    return {
        schemaVersion: state.schemaVersion,
        revision: state.revision,
        sessionId: state.identity.sessionId,
        planId: state.identity.planId,
        planHash: state.identity.planHash,
        planRevision: state.identity.planRevision,
        intent: state.intent,
        plan: state.plan,
        result: state.result,
        requirementMode: state.requirementMode,
        confirmedAnswers: state.projection.confirmedAnswers,
        optimisticAnswers: state.projection.optimisticAnswers,
        selections: state.answers.selectionByQuestion,
        answerRevision: state.answers.answerRevision,
        readiness: state.readiness,
        readinessPreview: state.readinessPreview,
        readinessStatus: state.readinessStatus,
        missingResources: Object.values(state.resources.missingByKey),
        resourceDecisions: state.resources.decisionsByKey,
        resourceRevision: state.resources.revision,
        clarification: state.clarification,
        run: state.run,
        apply: state.apply,
        persistence: state.persistence,
        ui: state.ui
    };
}

function setLegacyCoreValue(owner, key, value) {
    const patchRun = (kind, patch) => {
        return dispatchAgentWorkspaceEvent(owner, {
            type: AgentWorkspaceEventTypes.RUN_PATCHED,
            payload: { kind, patch }
        });
    };
    switch (key) {
        case 'pendingVisionPlan':
            return dispatchAgentWorkspaceEvent(owner, value
                ? { type: AgentWorkspaceEventTypes.PLAN_RECEIVED, payload: { plan: value } }
                : { type: AgentWorkspaceEventTypes.PLAN_CLEARED, payload: {} });
        case 'currentResult':
            return dispatchAgentWorkspaceEvent(owner, {
                type: AgentWorkspaceEventTypes.RESULT_RECEIVED,
                payload: { result: value || null },
                planId: owner.agentWorkspaceState.identity.planId,
                planHash: owner.agentWorkspaceState.identity.planHash
            });
        case 'requirementMode':
            return dispatchAgentWorkspaceEvent(owner, { type: AgentWorkspaceEventTypes.REQUIREMENT_MODE_CHANGED, payload: { mode: value }, sessionId: owner.sessionId });
        case 'planQuestionAnswers':
            return dispatchAgentWorkspaceEvent(owner, {
                type: AgentWorkspaceEventTypes.ANSWERS_REPLACED,
                payload: { answers: value, selections: owner.agentWorkspaceState.answers.selectionByQuestion },
                sessionId: owner.sessionId,
                planId: owner.agentWorkspaceState.identity.planId,
                planHash: owner.agentWorkspaceState.identity.planHash
            });
        case 'planQuestionSelections':
            return dispatchAgentWorkspaceEvent(owner, {
                type: AgentWorkspaceEventTypes.ANSWERS_REPLACED,
                payload: { answers: owner.agentWorkspaceState.answers.optimisticByField, selections: value },
                sessionId: owner.sessionId,
                planId: owner.agentWorkspaceState.identity.planId,
                planHash: owner.agentWorkspaceState.identity.planHash
            });
        case 'planAnswerRevision':
            return dispatchAgentWorkspaceEvent(owner, {
                type: AgentWorkspaceEventTypes.ANSWER_REVISION_SET,
                payload: { revision: value }
            });
        case 'effectiveReadiness':
            return dispatchAgentWorkspaceEvent(owner, value
                ? { type: AgentWorkspaceEventTypes.READINESS_RECEIVED, payload: value }
                : { type: AgentWorkspaceEventTypes.READINESS_CLEARED, payload: {} });
        case 'previewState':
            return dispatchAgentWorkspaceEvent(owner, {
                type: AgentWorkspaceEventTypes.READINESS_STATUS_CHANGED,
                payload: { status: value }
            });
        case 'agentWorkspaceMode':
            return dispatchAgentWorkspaceEvent(owner, {
                type: AgentWorkspaceEventTypes.WORKSPACE_MODE_CHANGED,
                payload: { mode: value }
            });
        case 'workspaceViewMode':
            return dispatchAgentWorkspaceEvent(owner, { type: AgentWorkspaceEventTypes.VIEW_CHANGED, payload: { mode: value } });
        case 'workspaceBuildRunId':
            return dispatchAgentWorkspaceEvent(owner, {
                type: AgentWorkspaceEventTypes.PERSISTENCE_UPDATED,
                payload: { buildRunId: clean(value) }
            });
        case 'workspaceSubmittedBuildFingerprint':
            return dispatchAgentWorkspaceEvent(owner, {
                type: AgentWorkspaceEventTypes.PERSISTENCE_UPDATED,
                payload: { submittedBuildFingerprint: clean(value) }
            });
        case 'activePlanRunId':
            return patchRun('plan', { runId: clean(value), status: value ? 'running' : owner.agentWorkspaceState.run.plan.status });
        case 'activePlanRunEvents':
            return patchRun('plan', { events: asArray(value) });
        case 'activePlanRunEventKeys':
            return patchRun('plan', { eventKeys: value instanceof Set ? Object.fromEntries([...value].map(item => [item, true])) : cloneMap(value) });
        case 'activeAgentRunId':
            return patchRun('build', { runId: clean(value), status: value ? 'running' : owner.agentWorkspaceState.run.build.status });
        case 'activeAgentRunEvents':
            return patchRun('build', { events: asArray(value) });
        case 'activeAgentRunEventKeys':
            return patchRun('build', { eventKeys: value instanceof Set ? Object.fromEntries([...value].map(item => [item, true])) : cloneMap(value) });
        default:
            return owner.agentWorkspaceState;
    }
}

export function installAgentWorkspaceState(owner, seed = {}) {
    if (owner.agentWorkspaceState) return owner.agentWorkspaceState;
    owner.agentWorkspaceState = createAgentWorkspaceState(seed);
    const descriptors = {
        pendingVisionPlan: { get: () => owner.agentWorkspaceState.plan },
        currentResult: { get: () => owner.agentWorkspaceState.result },
        requirementMode: { get: () => owner.agentWorkspaceState.requirementMode },
        planQuestionAnswers: { get: () => owner.agentWorkspaceState.projection.answersByField },
        planQuestionSelections: { get: () => owner.agentWorkspaceState.answers.selectionByQuestion },
        planAnswerRevision: { get: () => owner.agentWorkspaceState.answers.answerRevision },
        effectiveReadiness: { get: () => owner.agentWorkspaceState.readinessPreview || null },
        previewState: { get: () => owner.agentWorkspaceState.readinessStatus },
        agentWorkspaceMode: { get: () => owner.agentWorkspaceState.ui.workspaceMode },
        workspaceViewMode: { get: () => owner.agentWorkspaceState.ui.viewMode },
        workspaceBuildRunId: { get: () => owner.agentWorkspaceState.persistence.buildRunId },
        workspaceSubmittedBuildFingerprint: { get: () => owner.agentWorkspaceState.persistence.submittedBuildFingerprint },
        activePlanRunId: { get: () => owner.agentWorkspaceState.run.plan.runId || null },
        activePlanRunEvents: { get: () => owner.agentWorkspaceState.run.plan.events },
        activePlanRunEventKeys: { get: () => new Set(Object.keys(owner.agentWorkspaceState.run.plan.eventKeys)) },
        activeAgentRunId: { get: () => owner.agentWorkspaceState.run.build.runId || null },
        activeAgentRunEvents: { get: () => owner.agentWorkspaceState.run.build.events },
        activeAgentRunEventKeys: { get: () => new Set(Object.keys(owner.agentWorkspaceState.run.build.eventKeys)) }
    };
    Object.entries(descriptors).forEach(([key, descriptor]) => {
        Object.defineProperty(owner, key, {
            configurable: true,
            enumerable: false,
            get: descriptor.get,
            set: value => setLegacyCoreValue(owner, key, value)
        });
    });
    return owner.agentWorkspaceState;
}
