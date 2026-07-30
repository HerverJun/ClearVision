export const aiTimestamp = '2026-07-29T08:00:00.000Z';
export const aiOperationId = '11111111-1111-4111-8111-111111111111';
export const aiPlanOperationId = '33333333-3333-4333-8333-333333333333';
export const aiBuildOperationId = '44444444-4444-4444-8444-444444444444';
export const aiProjectId = '22222222-2222-4222-8222-222222222222';
export const aiPlanId = 'plan_fixture_01';
export const aiPlanHash = 'a'.repeat(64);
export const aiBuildRunId = 'run_build_01';
export const aiBuildId = 'build_fixture_01';
export const aiBuildFingerprint = 'd'.repeat(64);
export const aiCandidateFingerprint = 'e'.repeat(64);

export function answerFixture(field = 'defect_definition', value = '划伤长度超过 2 mm') {
  return { questionId: `q_${field}`, field, value, origin: 'user', confidence: 1, resolved: true };
}

export function readinessFixture(canBuild = false) {
  return {
    canBuild,
    blockers: canBuild ? [] : [{
      id: 'blocker_defect', category: 'clarification', field: 'defect_definition',
      questionId: 'q_defect_definition', blocksBuild: true, resolutionMode: 'answer',
      publicLabel: '缺陷阈值尚未确认', resource: null
    }],
    resolvedFields: canBuild ? ['defect_definition'] : [],
    remainingFields: canBuild ? [] : ['defect_definition'],
    primaryMessage: canBuild ? '方案已具备构建条件。' : '请确认缺陷阈值。',
    contractVersion: 'vision-agent-plan-v2',
    missingResources: []
  };
}

export function readinessPreviewFixture(canBuild = false) {
  return {
    planId: aiPlanId,
    planHash: aiPlanHash,
    requirementMode: 'strict',
    answerRevision: 2,
    resourceRevision: 0,
    acceptedAnswers: canBuild ? [answerFixture()] : [],
    answerSetFingerprint: `sha256:${'b'.repeat(64)}`,
    buildReadiness: readinessFixture(canBuild),
    deferredQuestionIds: [],
    pendingConfirmationCount: canBuild ? 0 : 1,
    resourcePendingCount: 0,
    hardBlockerCount: canBuild ? 0 : 1,
    contractValid: true,
    failureCode: '',
    failureMessage: '',
    metadataOnly: true
  };
}

export function snapshotFixture(overrides: Record<string, unknown> = {}) {
  return {
    schemaVersion: 1,
    revision: 3,
    projectId: null,
    lifecycleState: 'idle',
    planRunId: null,
    planRunStatus: null,
    buildRunId: null,
    buildRunStatus: null,
    buildClientOperationId: null,
    projectBaseline: null,
    requirementMode: 'strict',
    planQuestionSelections: {},
    confirmedPlanAnswers: [],
    optimisticPlanAnswers: [],
    answerRevision: 0,
    buildParameterValues: {},
    readinessPreview: null,
    missingResources: [],
    resourceDecisions: [],
    resourceRevision: 0,
    buildResult: null,
    planAcceptedRecommendedDefaults: false,
    planTerminalSequence: null,
    buildTerminalSequence: null,
    submittedBuildFingerprint: null,
    updatedAtUtc: aiTimestamp,
    ...overrides
  };
}

export function newProjectBaselineFixture() {
  return {
    targetKind: 'new' as const, projectId: null, persistenceRevision: null,
    canonicalFlowHash: ''
  };
}

export function existingProjectBaselineFixture() {
  return {
    targetKind: 'existing' as const, projectId: aiProjectId, persistenceRevision: 12,
    canonicalFlowHash: '9'.repeat(64)
  };
}

export function buildParameterFixture(overrides: Record<string, unknown> = {}) {
  return {
    canonicalKey: 'threshold_1.threshold', tempId: 'threshold_1', operatorType: 'Threshold',
    operatorDisplayName: 'Threshold', parameterName: 'threshold', parameterDisplayName: 'Threshold value',
    purpose: 'Controls segmentation sensitivity.', dataType: 'number', isRequired: true,
    value: null, hasExplicitValue: false, valueSummary: 'Not confirmed', source: 'suggested', pending: true,
    impact: 'Changes the defect boundary.', suggestedReason: 'Derived from the acceptance threshold.',
    defaultValue: 128, minValue: 0, maxValue: 255, options: [], requiredPolicy: 'required',
    atLeastOneGroup: '', mutuallyExclusiveGroup: '', requiredWhen: null, enabledWhen: null, disabledWhen: null,
    resourceKind: '', resourceCanonicalId: '', resourceDependent: false,
    ...overrides
  };
}

export function resourceRequirementFixture(
  resourceType = 'camera_binding',
  overrides: Record<string, unknown> = {}
) {
  const parameterName = resourceType === 'camera_binding' ? 'CameraId' : 'ResourceId';
  const normalizedParameter = resourceType === 'camera_binding' ? 'camera_binding_id' : 'resourceid';
  return {
    canonicalId: `resource:v1|${resourceType}|acquireimage#1|${normalizedParameter}`,
    resourceType,
    resourceName: resourceType === 'camera_binding' ? 'Inspection camera' : 'Inspection resource',
    resourceKey: '', operatorKey: 'acquireimage#1', operatorId: 'acquire_1', operatorType: 'AcquireImage',
    operatorIndex: 0, parameterName, status: 'missing', blockingScope: 'apply',
    resolutionTarget: 'canonical_binding', draftPolicy: 'block',
    description: 'A canonical resource binding is required.', source: 'operator_contract', aliases: [],
    ...overrides
  };
}

export function resourceDecisionFixture(overrides: Record<string, unknown> = {}) {
  const resource = resourceRequirementFixture();
  return {
    canonicalId: resource.canonicalId, status: 'bound' as const, resourceKey: 'camera-binding-01',
    resourceType: resource.resourceType, operatorKey: resource.operatorKey, operatorId: resource.operatorId,
    operatorType: resource.operatorType, operatorIndex: resource.operatorIndex,
    parameterName: resource.parameterName, valueSummary: 'Line camera', source: 'camera_binding_authority',
    ...overrides
  };
}

export function resourceDecisionSelectionFixture(overrides: Record<string, unknown> = {}) {
  const resource = resourceRequirementFixture();
  return {
    canonicalId: resource.canonicalId,
    resourceKey: 'camera-binding-01',
    ...overrides
  };
}

export function applyGateFixture(ready = false) {
  return {
    canvasApplyReady: ready, runtimeDraftReady: ready, deploymentReady: false, blocked: !ready,
    status: ready ? 'ready_for_handoff' : 'blocked',
    applyBlockers: ready ? [] : ['Confirm pending parameters.'], deploymentBlockers: ['Workspace review required.'],
    firstFixRecommendation: ready ? '' : 'Confirm the threshold value.', metadataOnly: true
  };
}

export function validationFixture(ready = false) {
  const check = (id: string, label: string) => ({
    id, label, status: ready ? 'passed' : 'pending',
    summary: ready ? 'Passed.' : 'Waiting for confirmed inputs.', blockerCount: ready ? 0 : 1, warningCount: 0
  });
  return {
    structural: check('structural', 'Structure validation'),
    dryRun: check('dry_run', 'Dry run'),
    manifest: check('manifest', 'Manifest validation'),
    applyGate: applyGateFixture(ready), handoffEligible: ready,
    readinessStatus: ready ? 'ready' : 'blocked',
    firstFixRecommendation: ready ? '' : 'Confirm the threshold value.', metadataOnly: true
  };
}

export function buildResultFixture(overrides: Record<string, unknown> = {}) {
  return {
    schemaVersion: 1, runId: aiBuildRunId, buildId: aiBuildId,
    clientOperationId: aiBuildOperationId, buildIdentity: `${aiPlanId}:${aiBuildId}`,
    submittedBuildFingerprint: aiBuildFingerprint, planId: aiPlanId, planHash: aiPlanHash,
    answerSetFingerprint: `sha256:${'b'.repeat(64)}`, answerRevision: 2, resourceRevision: 0,
    projectBaseline: newProjectBaselineFixture(), candidateFlowFingerprint: aiCandidateFingerprint,
    operatorCount: 2, connectionCount: 1,
    operatorPipeline: [
      { tempId: 'acquire_1', operatorType: 'AcquireImage', source: 'plan', status: 'mapped', repairNote: '' },
      { tempId: 'threshold_1', operatorType: 'Threshold', source: 'plan', status: 'mapped', repairNote: '' }
    ],
    parameterMapping: [buildParameterFixture()], missingResources: [],
    workflowDiff: {
      addedNodes: ['AcquireImage', 'Threshold'], modifiedNodes: [], preservedNodes: [], removedNodes: [],
      addedOrChangedParameters: ['threshold_1.threshold'], pendingParameters: ['threshold_1.threshold'],
      missingResources: [], validationFailures: [], autoRepairs: [],
      deploymentBlockers: ['Workspace review required.'], metadataOnly: true
    },
    validation: validationFixture(false),
    publicTimeline: [{
      stage: 'validation', toolName: 'FlowValidationTool', source: 'existing_tool',
      inputSummary: 'Candidate flow', outputSummary: 'Waiting for confirmed inputs.', status: 'pending',
      durationMs: 12, evidenceId: 'evidence_01', repairAction: '', warningCode: '',
      applyImpact: 'blocked', deploymentImpact: 'blocked', metadataOnly: true, redactionPass: true
    }],
    publicWarnings: [], metadataOnly: true, redactionPass: true,
    ...overrides
  };
}

export function buildOperationFixture(overrides: Record<string, unknown> = {}) {
  return {
    ...operationFixture(), clientOperationId: aiBuildOperationId, kind: 'build_run', runId: aiBuildRunId,
    projectBaseline: newProjectBaselineFixture(),
    ...overrides
  };
}

export function buildTerminalEventFixture(
  sequence = 6,
  build = buildResultFixture(),
  overrides: Record<string, unknown> = {}
) {
  const snapshot = snapshotFixture({
    revision: 5, lifecycleState: 'build_blocked', buildRunId: aiBuildRunId, buildRunStatus: 'completed',
    buildClientOperationId: aiBuildOperationId, buildTerminalSequence: sequence,
    submittedBuildFingerprint: aiBuildFingerprint, projectBaseline: newProjectBaselineFixture(),
    answerRevision: 2, buildResult: build
  });
  return {
    runId: aiBuildRunId, sequence, timestamp: aiTimestamp, eventType: 'run.completed', stage: 'build',
    title: 'Build completed', summary: 'Candidate build completed.', status: 'completed',
    payload: {
      sessionId: 'session_01', planId: aiPlanId, planHash: aiPlanHash,
      publicBuildResult: build, workspaceSnapshot: snapshot, metadataOnly: true
    },
    metadataOnly: true, redactionPass: true,
    ...overrides
  };
}

export function buildReplayFixture(build = buildResultFixture()) {
  const events = [buildTerminalEventFixture(1, build)];
  return {
    summary: {
      runId: aiBuildRunId, createdAt: aiTimestamp, updatedAt: aiTimestamp, status: 'completed',
      title: 'Build completed', summary: 'Candidate build completed.',
      firstFixRecommendation: build.validation.firstFixRecommendation,
      lastSequence: 1, eventCount: 1, duplicateEventCount: 0, droppedEventCount: 0, staleEventCount: 0,
      ownerHash: 'redacted-owner', terminalIntent: null, metadataOnly: true, redactionPass: true, payload: null
    },
    events,
    snapshot: {
      storageVersion: 'agent-run-events.jsonl.v1', runId: aiBuildRunId, generatedAt: aiTimestamp,
      firstSequence: 1, lastSequence: 1, eventCount: 1, metadataOnly: true, redactionPass: true, events
    },
    diagnostics: {
      runId: aiBuildRunId, eventCount: 1, duplicateEventCount: 0, droppedEventCount: 0,
      staleEventCount: 0, metadataOnly: true, redactionPass: true
    }
  };
}

export function readyPlanFixture() {
  return planFixture({
    currentPhase: 'ready', clarificationQuestions: [], confirmedPlanAnswers: [answerFixture()],
    resolvedPlanFields: ['defect_definition'], remainingPlanFields: [], canBuild: true,
    blockingReasons: [], buildReadiness: readinessFixture(true), nextAction: 'Start Build.'
  });
}

export function readyPlanReplayFixture() {
  const plan = readyPlanFixture();
  const completed = runEventFixture(2, 'plan.completed');
  const payload = completed.payload as Record<string, unknown>;
  const readyCompleted = {
    ...completed,
    payload: {
      ...payload, planResult: plan, planModeResult: plan, canBuild: true, questionCount: 0,
      workspaceSnapshot: snapshotFixture({
        revision: 4, lifecycleState: 'plan_ready', planRunId: 'run_plan_01', planRunStatus: 'completed',
        confirmedPlanAnswers: [answerFixture()], answerRevision: 2, planTerminalSequence: 2
      })
    }
  } as ReturnType<typeof runEventFixture>;
  return replayFixture([runEventFixture(1), readyCompleted, runEventFixture(3, 'run.completed')]);
}

export function planRunResponseFixture() {
  return {
    runId: 'run_plan_01', sessionId: 'session_01', brief: 'Plan ready.', events: [],
    workspaceSnapshot: snapshotFixture({
      lifecycleState: 'planning', planRunId: 'run_plan_01', planRunStatus: 'running'
    }),
    operation: operationFixture('plan_run'), persistenceStatus: {}, metadataOnly: true
  };
}

export function buildRunResponseFixture(overrides: Record<string, unknown> = {}) {
  return {
    runId: aiBuildRunId, sessionId: 'session_01', brief: 'Build started.', events: [],
    workspaceSnapshot: snapshotFixture({
      lifecycleState: 'building', buildRunId: aiBuildRunId, buildRunStatus: 'running',
      buildClientOperationId: aiBuildOperationId, projectBaseline: newProjectBaselineFixture(),
      confirmedPlanAnswers: [answerFixture()], answerRevision: 2
    }),
    operation: buildOperationFixture(), persistenceStatus: {}, metadataOnly: true,
    ...overrides
  };
}

export function buildSessionFixture(build = buildResultFixture(), snapshotOverrides: Record<string, unknown> = {}) {
  return sessionFixture({
    lifecycleState: build.validation.handoffEligible ? 'build_ready' : 'build_blocked',
    buildRunId: build.runId, buildRunStatus: 'completed', buildClientOperationId: build.clientOperationId,
    projectBaseline: build.projectBaseline, confirmedPlanAnswers: [answerFixture()], answerRevision: build.answerRevision,
    resourceRevision: build.resourceRevision, buildTerminalSequence: 1,
    submittedBuildFingerprint: build.submittedBuildFingerprint, buildResult: build,
    ...snapshotOverrides
  });
}

export function sessionFixture(snapshotOverrides: Record<string, unknown> = {}) {
  return { sessionId: 'session_01', snapshot: snapshotFixture(snapshotOverrides), updatedAtUtc: aiTimestamp };
}

export function operationFixture(kind: 'session_create' | 'plan_run' = 'session_create') {
  return {
    clientOperationId: kind === 'plan_run' ? aiPlanOperationId : aiOperationId,
    kind,
    status: 'created',
    sessionId: 'session_01',
    runId: kind === 'plan_run' ? 'run_plan_01' : null,
    payloadFingerprint: `sha256:${'c'.repeat(64)}`,
    projectBaseline: null,
    errorCode: null,
    publicMessage: null,
    createdAtUtc: aiTimestamp,
    updatedAtUtc: aiTimestamp,
    expiresAtUtc: aiTimestamp
  };
}

export function semanticFixture() {
  return {
    isVisionRequest: true,
    intent: 'surface_defect_inspection',
    taskType: 'defect_detection',
    confidence: 0.92,
    taskTypeConfidence: 0.9,
    inspectionObject: '冲压件',
    targetAttribute: '表面',
    defectType: '划伤与压痕',
    measurementTarget: '缺陷长度',
    imageSource: '顶视相机',
    okCondition: '无超阈值缺陷',
    ngCondition: '任一缺陷长度超过 2 mm',
    outputTarget: '缺陷位置与类型',
    suggestedRoute: '定位后进行表面缺陷检测',
    canPlanCandidate: true,
    canBuildCandidate: false,
    objectSignals: ['冲压件'],
    taskSignals: ['划伤'],
    missingFields: ['defect_definition'],
    clarificationQuestions: ['缺陷阈值是多少？'],
    source: 'rule_fallback',
    failureCode: '',
    sanitizedErrorMessage: '',
    metadataOnly: true
  };
}

export function intentFixture() {
  return {
    intent: 'new_vision_flow', confidence: 'high', shouldOpenPlan: true, shouldBuildDirectly: false,
    canBuild: false, needsClarification: true, publicReason: '已识别为表面缺陷检测任务。',
    assistantReply: '将先规划检测路线。', fallbackAllowed: true, routerSource: 'rule_fallback',
    fallbackReason: 'MODEL_MODE=RULE_FALLBACK', semanticExtraction: semanticFixture(),
    requirementMaturity: null, decisionTrace: null, shouldMergeIntoPendingPlan: false,
    shouldResetPendingPlan: false, planAnswerUpdates: [], resolvedPlanFields: [],
    remainingPlanFields: ['defect_definition'], metadataOnly: true
  };
}

export function questionFixture(field = 'defect_definition', index = 1) {
  return {
    id: `q_${field}`, field, title: index === 1 ? '划伤达到什么条件判定 NG？' : `请确认条件 ${index}`,
    why: '阈值直接影响 OK/NG 判定。', defaultValue: '2 mm', defaultAssumption: '划伤长度超过 2 mm',
    impact: '影响缺陷判定灵敏度。', options: [{
      value: '2 mm', label: '超过 2 mm 判定 NG', recommended: true, answerEffect: 'resolve',
      recommendationReason: '与现有验收要求一致。', description: '采用长度阈值。', impact: '中等灵敏度。'
    }]
  };
}

export function planFixture(overrides: Record<string, unknown> = {}) {
  return {
    planContractVersion: 'vision-agent-plan-v2', planId: aiPlanId, planHash: aiPlanHash,
    planSource: 'rule_fallback', currentPhase: 'clarifying', fallbackReason: 'MODEL_MODE=RULE_FALLBACK',
    plannerFailureStage: '', plannerFailureCode: '', sanitizedErrorKind: '', sanitizedErrorMessage: '',
    originalUserPrompt: '检测冲压件表面划伤与压痕', goal: '检测冲压件表面划伤与压痕并输出位置',
    intent: 'surface_defect_inspection', confidence: 'high',
    requirementUnderstanding: ['检测冲压件表面划伤与压痕', '输出缺陷位置与类型'],
    confirmedPlanAnswers: [], resolvedPlanFields: [], remainingPlanFields: ['defect_definition'],
    recommendedRoute: {
      routeId: 'surface_defect_route', title: '定位 + 表面缺陷检测',
      summary: '先定位工件，再增强并分割划伤与压痕。', operators: ['定位', '图像增强', '缺陷分割'],
      templateDecision: '使用通用表面缺陷模板'
    },
    clarificationQuestions: [questionFixture()],
    recommendedDefaults: [{ id: 'threshold', label: '缺陷长度阈值', value: '2 mm', impact: '可后续编辑' }],
    risks: ['反光可能影响缺陷对比度'], acceptanceCriteria: ['已知缺陷样本全部判定 NG'],
    executablePlan: ['定位工件区域', '增强表面缺陷', '分割缺陷并测量长度', '按阈值输出 OK/NG'],
    canBuild: false, blockingReasons: ['缺陷阈值尚未确认'], buildReadiness: readinessFixture(false),
    semanticExtraction: semanticFixture(), requirementMaturity: null, decisionTrace: null,
    nextAction: '确认缺陷阈值',
    contextSummary: {
      hasCurrentFlow: false, hasCurrentResult: false, attachmentCount: 0, templateSelectionMode: '',
      templateId: '', contextKinds: ['user_requirement', 'new_flow'], operatorCatalogTools: ['定位', '图像增强']
    },
    operatorCatalogVersion: 'catalog-v1', templateCatalogVersion: 'template-v1', templateSelection: null,
    stationBoundarySummary: '不涉及现场控制', plcOutputPolicy: 'metadata_only', planWarnings: [],
    contractRepairNotes: [], publicEvents: [{
      stage: 'planning', status: 'completed', title: '方案合同已校验', summary: '公开字段校验通过。',
      metadata: {}, metadataOnly: true
    }], metadataOnly: true,
    ...overrides
  };
}

export function runEventFixture(sequence: number, eventType = 'plan.started', overrides: Record<string, unknown> = {}) {
  const plan = eventType === 'plan.completed' ? planFixture() : null;
  const payload = eventType === 'plan.completed' ? {
    status: 'plan_completed', generationMode: 'plan', sessionId: 'session_01', planRunId: 'run_plan_01',
    planSource: 'rule_fallback', fallbackReason: 'MODEL_MODE=RULE_FALLBACK', plannerFailureStage: '',
    plannerFailureCode: '', sanitizedErrorKind: '', sanitizedErrorMessage: '', planResult: plan,
    planModeResult: plan, planId: aiPlanId, planHash: aiPlanHash, canBuild: false, questionCount: 1,
    publicEventCount: 1, workspaceSnapshot: snapshotFixture({ lifecycleState: 'plan_blocked', planRunId: 'run_plan_01', planRunStatus: 'completed', planTerminalSequence: sequence }),
    persistenceStatus: {}, persistenceWarning: null, metadataOnly: true
  } : { sessionId: 'session_01', mode: 'plan', metadataOnly: true };
  return {
    runId: 'run_plan_01', sequence, timestamp: aiTimestamp, eventType, stage: 'plan',
    title: eventType, summary: `公开事件 ${sequence}`, status: eventType === 'run.failed' ? 'failed' : eventType === 'run.cancelled' ? 'cancelled' : eventType === 'run.completed' || eventType === 'plan.completed' ? 'completed' : 'running',
    payload, metadataOnly: true, redactionPass: true, ...overrides
  };
}

export function replayFixture(events = [runEventFixture(1), runEventFixture(2, 'plan.completed'), runEventFixture(3, 'run.completed')]) {
  const firstSequence = events.length ? Math.min(...events.map(event => event.sequence)) : 0;
  const lastSequence = events.length ? Math.max(...events.map(event => event.sequence)) : 0;
  return {
    summary: {
      runId: 'run_plan_01', createdAt: aiTimestamp, updatedAt: aiTimestamp, status: 'completed',
      title: '规划完成', summary: '公开规划完成。', firstFixRecommendation: '', lastSequence: events.at(-1)?.sequence ?? 0,
      eventCount: events.length, duplicateEventCount: 0, droppedEventCount: 0, staleEventCount: 0,
      ownerHash: 'redacted-owner', terminalIntent: null, metadataOnly: true, redactionPass: true, payload: null
    },
    events,
    snapshot: {
      storageVersion: 'agent-run-events.jsonl.v1', runId: 'run_plan_01', generatedAt: aiTimestamp,
      firstSequence, lastSequence, eventCount: events.length, metadataOnly: true, redactionPass: true, events
    },
    diagnostics: {
      runId: 'run_plan_01', eventCount: events.length, duplicateEventCount: 0, droppedEventCount: 0,
      staleEventCount: 0, metadataOnly: true, redactionPass: true
    }
  };
}

export function projectFixture() {
  return {
    id: aiProjectId, name: '冲压件外观检测', description: null, version: '1.4.0', persistenceRevision: 12,
    createdAt: aiTimestamp, modifiedAt: aiTimestamp, lastOpenedAt: aiTimestamp, flow: null,
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
  };
}
