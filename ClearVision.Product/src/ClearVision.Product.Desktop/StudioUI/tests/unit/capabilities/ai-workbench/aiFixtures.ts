export const aiTimestamp = '2026-07-29T08:00:00.000Z';
export const aiOperationId = '11111111-1111-4111-8111-111111111111';
export const aiPlanOperationId = '33333333-3333-4333-8333-333333333333';
export const aiProjectId = '22222222-2222-4222-8222-222222222222';
export const aiPlanId = 'plan_fixture_01';
export const aiPlanHash = 'a'.repeat(64);

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
    readinessPreview: null,
    planAcceptedRecommendedDefaults: false,
    planTerminalSequence: null,
    updatedAtUtc: aiTimestamp,
    ...overrides
  };
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
  return {
    summary: {
      runId: 'run_plan_01', createdAt: aiTimestamp, updatedAt: aiTimestamp, status: 'completed',
      title: '规划完成', summary: '公开规划完成。', firstFixRecommendation: '', lastSequence: events.at(-1)?.sequence ?? 0,
      eventCount: events.length, duplicateEventCount: 0, droppedEventCount: 0, staleEventCount: 0,
      ownerHash: 'redacted-owner', terminalIntent: null, metadataOnly: true, redactionPass: true, payload: null
    },
    events,
    snapshot: {},
    diagnostics: {}
  };
}

export function projectFixture() {
  return {
    id: aiProjectId, name: '冲压件外观检测', description: null, version: '1.4.0', persistenceRevision: 12,
    createdAt: aiTimestamp, modifiedAt: aiTimestamp, lastOpenedAt: aiTimestamp, flow: null,
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
  };
}
