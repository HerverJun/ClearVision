import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { isAbsolute, relative, resolve } from 'node:path';
import type { Page, Route } from '@playwright/test';
import { fulfillF02Json, installF02BrowserStartup } from './f02-browser-fixture';

export const f06ProjectId = '22222222-2222-4222-8222-222222222222';
export const f06SessionId = 'session_f06_01';
export const f06ProjectSessionId = 'session_f06_project_02';
export const f06UnboundHistorySessionId = 'session_f06_03';
export const f06RunId = 'run_plan_f06_01';
export const f06BuildRunId = 'run_build_f06_01';
export const f06PlanId = 'plan_f06_01';
export const f06PlanHash = 'a'.repeat(64);
export const f06BuildId = 'build_f06_01';
export const f06BuildFingerprint = 'd'.repeat(64);
export const f06CandidateFingerprint = 'e'.repeat(64);
export const f06HandoffArtifactId = '0123456789abcdef0123456789abcdef';
export const f06SecondHandoffArtifactId = 'fedcba9876543210fedcba9876543210';
export const f06CreatedProjectId = '77777777-7777-4777-8777-777777777777';
const f06CandidateFlowId = '66666666-6666-4666-8666-666666666666';
const timestamp = '2026-07-29T08:00:00.000Z';

export interface F06BrowserAudit {
  readonly requests: Array<{ method: string; path: string; body: unknown }>;
  readonly consoleErrors: string[];
  readonly pageErrors: string[];
  readonly releaseBuildStream: () => void;
  readonly releaseRevalidation: () => void;
  readonly releaseHistory: () => void;
}

export type F06InitialBuildState = 'ready' | 'building' | 'validating' | 'failed' | 'cancelled' |
  'baseline-conflict' | 'stale';

export interface F06BrowserFixtureOptions {
  readonly role?: 'Admin' | 'Engineer' | 'Operator';
  readonly flag?: boolean;
  readonly projectBound?: boolean;
  readonly failSession?: boolean;
  readonly longContent?: boolean;
  readonly recoveredBuild?: boolean;
  readonly initialBuildState?: F06InitialBuildState;
  readonly buildUnknownOutcome?: boolean;
  readonly holdRevalidation?: boolean;
  readonly enableHandoff?: boolean;
  readonly artifactStatus?: 'available' | 'consuming' | 'consumed' | 'expired' | 'rejected';
  readonly artifactBaselineRevision?: number;
  readonly handoffCreateUnknownOutcome?: boolean;
  readonly saveUnknownOutcome?: boolean;
  readonly historyMode?: 'empty' | 'long';
  readonly historyDelete?: 'success' | 'blocked' | 'unknown-reconcile-deleted';
  readonly historyUnauthorized?: boolean;
  readonly holdHistory?: boolean;
}

function candidateFlow() {
  return {
    id: f06CandidateFlowId,
    name: 'AI 候选流程',
    operators: [],
    connections: [],
    decisionConfiguration: null
  };
}

function workspaceProject(
  id: string,
  revision: number,
  flow: ReturnType<typeof candidateFlow> | null,
  name = '新能源托盘超长中文名称外观检测工程',
  description: string | null = '高反光表面缺陷检测'
) {
  return {
    id, name, description, version: '2.3.0', persistenceRevision: revision,
    createdAt: timestamp, modifiedAt: revision > 0 ? timestamp : null, lastOpenedAt: timestamp,
    flow, globalSettings: {},
    globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: {
      schemaVersion: 1,
      calibrationAssetCount: 0,
      spatialAssetCount: 0,
      calibrationAssets: [],
      spatialAssets: []
    }
  };
}

function projectDetails(project: ReturnType<typeof workspaceProject>) {
  return {
    id: project.id,
    name: project.name,
    description: project.description,
    version: project.version,
    persistenceRevision: project.persistenceRevision,
    createdAt: project.createdAt,
    modifiedAt: project.modifiedAt,
    lastOpenedAt: project.lastOpenedAt,
    flow: project.flow,
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
  };
}

function answer(field = 'defect_definition', value = '2 mm') {
  return { questionId: `q_${field}`, field, value, origin: 'user', confidence: 1, resolved: true };
}

function readiness(canBuild: boolean) {
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
    contractVersion: 'vision-agent-plan-v2', missingResources: []
  };
}

function readinessPreview(canBuild: boolean) {
  return {
    planId: f06PlanId, planHash: f06PlanHash, requirementMode: 'strict', answerRevision: 1,
    resourceRevision: 0, acceptedAnswers: canBuild ? [
      answer('defect_definition', '2 mm'),
      answer('image_source', '顶视面阵相机'),
      answer('output_target', '缺陷类型、位置和尺寸')
    ] : [],
    answerSetFingerprint: `sha256:${'b'.repeat(64)}`, buildReadiness: readiness(canBuild),
    deferredQuestionIds: [], pendingConfirmationCount: canBuild ? 0 : 1, resourcePendingCount: 0,
    hardBlockerCount: canBuild ? 0 : 1, contractValid: true, failureCode: '', failureMessage: '',
    metadataOnly: true
  };
}

function sessionSnapshot(projectBound: boolean, overrides: Record<string, unknown> = {}) {
  return {
    schemaVersion: 1, revision: 1, projectId: projectBound ? f06ProjectId : null, lifecycleState: 'idle',
    planRunId: null, planRunStatus: null, buildRunId: null, buildRunStatus: null,
    buildClientOperationId: null, buildTerminalSequence: null, submittedBuildFingerprint: null,
    projectBaseline: null, requirementMode: 'strict',
    planQuestionSelections: {}, confirmedPlanAnswers: [], optimisticPlanAnswers: [], answerRevision: 0,
    buildParameterValues: {}, readinessPreview: null, missingResources: [], resourceDecisions: [],
    resourceRevision: 0, buildResult: null, planAcceptedRecommendedDefaults: false, planTerminalSequence: null,
    updatedAtUtc: timestamp, ...overrides
  };
}

function session(projectBound: boolean, snapshot: Record<string, unknown>, sessionId = f06SessionId) {
  return { sessionId, snapshot, updatedAtUtc: timestamp };
}

function projectBaseline(projectBound: boolean) {
  return projectBound
    ? { targetKind: 'existing', projectId: f06ProjectId, persistenceRevision: 18, canonicalFlowHash: '9'.repeat(64) }
    : { targetKind: 'new', projectId: null, persistenceRevision: null, canonicalFlowHash: '' };
}

function operation(kind: 'session_create' | 'plan_run' | 'build_run', clientOperationId: string, projectBound = false) {
  return {
    clientOperationId, kind, status: 'created', sessionId: f06SessionId,
    runId: kind === 'plan_run' ? f06RunId : kind === 'build_run' ? f06BuildRunId : null,
    artifactId: null,
    payloadFingerprint: `sha256:${'c'.repeat(64)}`,
    projectBaseline: kind === 'build_run' ? projectBaseline(projectBound) : null,
    errorCode: null, publicMessage: null,
    createdAtUtc: timestamp, updatedAtUtc: timestamp, expiresAtUtc: timestamp
  };
}

function semantic() {
  return {
    isVisionRequest: true, intent: 'surface_defect_inspection', taskType: 'defect_detection', confidence: 0.92,
    taskTypeConfidence: 0.9, inspectionObject: '新能源电池托盘冲压件', targetAttribute: '高反光金属表面',
    defectType: '划伤、压痕与脏污', measurementTarget: '缺陷长度和面积', imageSource: '顶视面阵相机',
    okCondition: '没有超过阈值的表面缺陷', ngCondition: '任一划伤长度超过 2 mm 或压痕面积超过 1.5 mm²',
    outputTarget: 'OK/NG、缺陷类型、位置和尺寸', suggestedRoute: '定位后进行光照均衡与多尺度缺陷分割',
    canPlanCandidate: true, canBuildCandidate: false, objectSignals: ['冲压件'], taskSignals: ['划伤'],
    missingFields: ['defect_definition'], clarificationQuestions: ['缺陷阈值是多少？'],
    source: 'rule_fallback', failureCode: '', sanitizedErrorMessage: '', metadataOnly: true
  };
}

function intent() {
  return {
    intent: 'new_vision_flow', confidence: 'high', shouldOpenPlan: true, shouldBuildDirectly: false,
    canBuild: false, needsClarification: true, publicReason: '已识别为表面缺陷检测任务。',
    assistantReply: '将先规划检测路线。', fallbackAllowed: true, routerSource: 'rule_fallback',
    fallbackReason: 'MODEL_MODE=RULE_FALLBACK', semanticExtraction: semantic(), requirementMaturity: null,
    decisionTrace: null, shouldMergeIntoPendingPlan: false, shouldResetPendingPlan: false,
    planAnswerUpdates: [], resolvedPlanFields: [], remainingPlanFields: ['defect_definition'], metadataOnly: true
  };
}

function question(index: number) {
  const fields = ['defect_definition', 'image_source', 'output_target'];
  const titles = ['划伤达到什么条件判定 NG？', '规划使用哪种图像来源？', '检测结果需要输出哪些信息？'];
  const values = ['2 mm', '顶视面阵相机', '缺陷类型、位置和尺寸'];
  const field = fields[index]!;
  return {
    id: `q_${field}`, field, title: titles[index]!, why: '该条件会直接影响方案和验收标准。',
    defaultValue: values[index]!, defaultAssumption: values[index]!, impact: '影响判定稳定性与结果可追溯性。',
    options: [{
      value: values[index]!, label: values[index]!, recommended: true, answerEffect: 'resolve',
      recommendationReason: '与当前任务描述和常用现场验收方式一致。', description: '采用推荐条件。', impact: '可在后续阶段编辑。'
    }]
  };
}

function plan(longContent: boolean) {
  const longGoal = '检测新能源电池托盘冲压件高反光金属表面的划伤、压痕与脏污，并在复杂环境光变化、批次纹理差异和边缘反射干扰下稳定输出缺陷类型、像素位置、物理尺寸、置信度与最终 OK/NG 判定。';
  return {
    planContractVersion: 'vision-agent-plan-v2', planId: f06PlanId, planHash: f06PlanHash,
    planSource: 'rule_fallback', currentPhase: 'clarifying', fallbackReason: 'MODEL_MODE=RULE_FALLBACK',
    plannerFailureStage: '', plannerFailureCode: '', sanitizedErrorKind: '', sanitizedErrorMessage: '',
    originalUserPrompt: longContent ? longGoal : '检测冲压件表面划伤与压痕并输出缺陷位置',
    goal: longContent ? `${longGoal}${longGoal}` : '检测冲压件表面划伤与压痕并输出缺陷位置。',
    intent: 'surface_defect_inspection', confidence: 'high',
    requirementUnderstanding: ['检测高反光冲压件表面缺陷', '输出缺陷位置、类型和尺寸'],
    confirmedPlanAnswers: [], resolvedPlanFields: [], remainingPlanFields: ['defect_definition'],
    recommendedRoute: {
      routeId: 'surface_defect_route', title: '工件定位 + 光照均衡 + 多尺度缺陷分割',
      summary: '先稳定定位工件与有效表面区域，再抑制高光并分割划伤、压痕与脏污。',
      operators: ['工件定位', 'ROI 裁剪', '光照均衡', '缺陷分割', '形态测量', '最终判定'],
      templateDecision: '使用通用表面缺陷模板'
    },
    clarificationQuestions: [question(0), question(1), question(2)],
    recommendedDefaults: [{ id: 'threshold', label: '划伤长度阈值', value: '2 mm', impact: '后续可编辑' }],
    risks: ['高光可能降低缺陷对比度'], acceptanceCriteria: ['已知缺陷样本全部判定 NG', '无缺陷样本误判率低于 1%'],
    executablePlan: ['定位工件和有效表面区域', '均衡光照并增强细小缺陷', '分割缺陷并测量长度与面积', '依据确认阈值输出 OK/NG'],
    canBuild: false, blockingReasons: ['关键条件尚未确认'], buildReadiness: readiness(false),
    semanticExtraction: semantic(), requirementMaturity: null, decisionTrace: null, nextAction: '确认关键条件',
    contextSummary: { hasCurrentFlow: false, hasCurrentResult: false, attachmentCount: 0, templateSelectionMode: '', templateId: '', contextKinds: ['user_requirement', 'new_flow'], operatorCatalogTools: ['定位', '图像增强'] },
    operatorCatalogVersion: 'catalog-v1', templateCatalogVersion: 'template-v1', templateSelection: null,
    stationBoundarySummary: '不涉及现场控制', plcOutputPolicy: 'metadata_only', planWarnings: [],
    contractRepairNotes: [], publicEvents: [{ stage: 'planning', status: 'completed', title: '方案合同已校验', summary: '公开字段校验通过。', metadata: {}, metadataOnly: true }],
    metadataOnly: true
  };
}

function runEvent(sequence: number, eventType: string, activePlan: Record<string, unknown>, snapshot: Record<string, unknown>) {
  const isPlanComplete = eventType === 'plan.completed';
  const payload = isPlanComplete ? {
    status: 'plan_completed', generationMode: 'plan', sessionId: f06SessionId, planRunId: f06RunId,
    planSource: 'rule_fallback', fallbackReason: 'MODEL_MODE=RULE_FALLBACK', plannerFailureStage: '',
    plannerFailureCode: '', sanitizedErrorKind: '', sanitizedErrorMessage: '', planResult: activePlan,
    planModeResult: activePlan, planId: f06PlanId, planHash: f06PlanHash, canBuild: false,
    questionCount: 3, publicEventCount: 1, workspaceSnapshot: snapshot, persistenceStatus: {},
    persistenceWarning: null, metadataOnly: true
  } : { sessionId: f06SessionId, mode: 'plan', metadataOnly: true };
  return {
    runId: f06RunId, sequence, timestamp, eventType, stage: 'plan', title: eventType,
    summary: sequence === 1 ? '规划已创建。' : sequence === 2 ? '公开方案已生成。' : '规划已完成。',
    status: eventType === 'run.completed' || isPlanComplete ? 'completed' : 'running', payload,
    metadataOnly: true, redactionPass: true
  };
}

function replay(events: readonly Record<string, unknown>[], status: 'running' | 'completed') {
  return {
    summary: {
      runId: f06RunId, createdAt: timestamp, updatedAt: timestamp, status, title: 'AI 规划',
      summary: '公开规划状态', firstFixRecommendation: '', lastSequence: events.at(-1)?.sequence ?? 0,
      eventCount: events.length, duplicateEventCount: 0, droppedEventCount: 0, staleEventCount: 0,
      ownerHash: 'redacted-owner', terminalIntent: null, metadataOnly: true, redactionPass: true, payload: null
    },
    events,
    snapshot: {
      storageVersion: 'agent-run-events.jsonl.v1', runId: f06RunId, generatedAt: timestamp,
      firstSequence: events[0]?.sequence ?? 0, lastSequence: events.at(-1)?.sequence ?? 0,
      eventCount: events.length, metadataOnly: true, redactionPass: true, events
    },
    diagnostics: {
      runId: f06RunId, eventCount: events.length, duplicateEventCount: 0, droppedEventCount: 0,
      staleEventCount: 0, metadataOnly: true, redactionPass: true
    }
  };
}

function cameraResource() {
  return {
    canonicalId: 'resource:v1|camera_binding|imageacquisition#1|camera_binding_id',
    resourceType: 'camera_binding', resourceName: '顶视检测相机',
    resourceKey: 'acquire_1.CameraBindingId', operatorKey: 'imageacquisition#1',
    operatorId: 'acquire_1', operatorType: 'ImageAcquisition', operatorIndex: 0,
    parameterName: 'CameraBindingId', status: 'pending', blockingScope: 'deploy_run',
    resolutionTarget: 'camera_settings', draftPolicy: 'draft_allowed',
    description: '请选择已配置且可用的相机绑定。', source: 'operator_contract', aliases: []
  };
}

function buildParameter(confirmed: boolean) {
  return {
    canonicalKey: 'threshold_1.Threshold', tempId: 'threshold_1', operatorType: 'Thresholding',
    operatorDisplayName: '阈值分割', parameterName: 'Threshold', parameterDisplayName: '分割阈值',
    purpose: '控制高反光表面缺陷的分割灵敏度。', dataType: 'number', isRequired: true,
    value: confirmed ? 128 : null, hasExplicitValue: confirmed,
    valueSummary: confirmed ? '128' : '128（建议）', source: confirmed ? 'user_confirmed_parameter' : 'suggested',
    pending: !confirmed, impact: '影响划伤和压痕边界。', suggestedReason: '依据当前样本对比度给出。',
    defaultValue: 128, minValue: 0, maxValue: 255, options: [], requiredPolicy: 'required',
    atLeastOneGroup: '', mutuallyExclusiveGroup: '', requiredWhen: null, enabledWhen: null, disabledWhen: null,
    resourceKind: '', resourceCanonicalId: '', resourceDependent: false
  };
}

function buildResult(
  projectBound: boolean,
  parameterConfirmed = false,
  resourceBound = false,
  clientOperationId = '44444444-4444-4444-8444-444444444444'
) {
  const ready = parameterConfirmed && resourceBound;
  const missingResources = resourceBound ? [] : [cameraResource()];
  const check = (id: string, label: string) => ({
    id, label, status: ready ? 'passed' : 'pending',
    summary: ready ? '已通过。' : '等待参数与资源处理。', blockerCount: ready ? 0 : 1, warningCount: 0
  });
  return {
    schemaVersion: 1, runId: f06BuildRunId, buildId: f06BuildId,
    clientOperationId,
    buildIdentity: `${f06PlanId}:${f06BuildId}`, submittedBuildFingerprint: f06BuildFingerprint,
    planId: f06PlanId, planHash: f06PlanHash, answerSetFingerprint: `sha256:${'b'.repeat(64)}`,
    answerRevision: parameterConfirmed ? 2 : 1, resourceRevision: resourceBound ? 1 : 0,
    projectBaseline: projectBaseline(projectBound), candidateFlowFingerprint: f06CandidateFingerprint,
    operatorCount: 3, connectionCount: 2,
    operatorPipeline: [
      { tempId: 'acquire_1', operatorType: 'ImageAcquisition', source: 'plan', status: 'mapped', repairNote: '' },
      { tempId: 'threshold_1', operatorType: 'Thresholding', source: 'plan', status: 'mapped', repairNote: '' },
      { tempId: 'judge_1', operatorType: 'ResultJudgment', source: 'plan', status: 'mapped', repairNote: '' }
    ],
    parameterMapping: [buildParameter(parameterConfirmed)], missingResources,
    workflowDiff: {
      addedNodes: ['ImageAcquisition', 'Thresholding', 'ResultJudgment'], modifiedNodes: [],
      preservedNodes: [], removedNodes: [], addedOrChangedParameters: ['threshold_1.Threshold'],
      pendingParameters: parameterConfirmed ? [] : ['threshold_1.Threshold'],
      missingResources: missingResources.map(item => item.canonicalId), validationFailures: [], autoRepairs: [],
      deploymentBlockers: ready ? [] : ['AI 候选仍需人工输入。'], metadataOnly: true
    },
    validation: {
      structural: check('structural', '结构校验'), dryRun: check('dry_run', '运行预演'),
      manifest: check('manifest', '清单预检'),
      applyGate: {
        canvasApplyReady: ready, runtimeDraftReady: ready, deploymentReady: false, blocked: !ready,
        status: ready ? 'ready_for_handoff' : 'blocked',
        applyBlockers: ready ? [] : ['参数或资源尚未处理。'], deploymentBlockers: ['工作区审核尚未执行。'],
        firstFixRecommendation: ready ? '' : parameterConfirmed ? '请选择顶视检测相机。' : '请确认分割阈值。',
        metadataOnly: true
      },
      handoffEligible: ready, readinessStatus: ready ? 'ready' : 'blocked',
      firstFixRecommendation: ready ? '' : parameterConfirmed ? '请选择顶视检测相机。' : '请确认分割阈值。',
      metadataOnly: true
    },
    publicTimeline: [{
      stage: 'validation', toolName: 'FlowValidationTool', source: 'existing_tool',
      inputSummary: '候选流程元数据', outputSummary: ready ? '验证通过。' : '等待人工输入。',
      status: ready ? 'completed' : 'pending', durationMs: 12, evidenceId: 'evidence_f06_01',
      repairAction: '', warningCode: '', applyImpact: ready ? 'ready' : 'blocked',
      deploymentImpact: 'workspace_review_required', metadataOnly: true, redactionPass: true
    }],
    publicWarnings: [], metadataOnly: true, redactionPass: true
  };
}

function buildRunEvent(sequence: number, build: ReturnType<typeof buildResult>, snapshot: Record<string, unknown>) {
  return {
    runId: f06BuildRunId, sequence, timestamp, eventType: 'run.completed', stage: 'build',
    title: '构建完成', summary: '候选流程与公开验证已生成。', status: 'completed',
    payload: {
      sessionId: f06SessionId, planId: f06PlanId, planHash: f06PlanHash,
      publicBuildResult: build, workspaceSnapshot: snapshot, metadataOnly: true
    },
    metadataOnly: true, redactionPass: true
  };
}

function buildProgressEvent(sequence: number, stage: 'build' | 'validation') {
  return {
    runId: f06BuildRunId, sequence, timestamp,
    eventType: stage === 'validation' ? 'build.validation.started' : 'build.started',
    stage, title: stage === 'validation' ? '开始校验' : '开始构建',
    summary: stage === 'validation' ? '正在执行结构校验与运行预演。' : '正在生成候选流程。',
    status: 'running',
    payload: { sessionId: f06SessionId, planId: f06PlanId, planHash: f06PlanHash, metadataOnly: true },
    metadataOnly: true, redactionPass: true
  };
}

function buildOutcomeEvent(eventType: 'run.failed' | 'run.cancelled') {
  return {
    runId: f06BuildRunId, sequence: 1, timestamp, eventType, stage: 'build',
    title: eventType === 'run.failed' ? '构建失败' : '构建已取消',
    summary: eventType === 'run.failed' ? '候选构建未完成。' : '候选构建已安全停止。',
    status: eventType === 'run.failed' ? 'failed' : 'cancelled',
    payload: {
      sessionId: f06SessionId, planId: f06PlanId, planHash: f06PlanHash,
      publicMessage: eventType === 'run.failed' ? '公开校验发现不可恢复错误，请检查后重新构建。' : '',
      metadataOnly: true
    },
    metadataOnly: true, redactionPass: true
  };
}

function buildReplay(
  events: readonly Record<string, unknown>[],
  status: 'running' | 'completed' | 'failed' | 'cancelled'
) {
  return {
    summary: {
      runId: f06BuildRunId, createdAt: timestamp, updatedAt: timestamp, status,
      title: 'AI 构建', summary: '公开构建状态', firstFixRecommendation: '请确认分割阈值。',
      lastSequence: events.at(-1)?.sequence ?? 0, eventCount: events.length,
      duplicateEventCount: 0, droppedEventCount: 0, staleEventCount: 0,
      ownerHash: 'redacted-owner', terminalIntent: null, metadataOnly: true, redactionPass: true, payload: null
    },
    events,
    snapshot: {
      storageVersion: 'agent-run-events.jsonl.v1', runId: f06BuildRunId, generatedAt: timestamp,
      firstSequence: events[0]?.sequence ?? 0, lastSequence: events.at(-1)?.sequence ?? 0,
      eventCount: events.length, metadataOnly: true, redactionPass: true, events
    },
    diagnostics: {
      runId: f06BuildRunId, eventCount: events.length, duplicateEventCount: 0, droppedEventCount: 0,
      staleEventCount: 0, metadataOnly: true, redactionPass: true
    }
  };
}

function historySessionSummary(index: number, projectBound: boolean) {
  const sessionId = index === 0
    ? f06SessionId
    : index === 1
      ? f06ProjectSessionId
      : index === 2
        ? f06UnboundHistorySessionId
        : `session_f06_${String(index + 1).padStart(2, '0')}`;
  const projectId = index === 0
    ? (projectBound ? f06ProjectId : null)
    : index === 1 || index > 2 && index % 3 === 1
      ? f06ProjectId
      : null;
  const lifecycleStates = ['idle', 'plan_ready', 'build_ready', 'build_failed', 'plan_cancelled'] as const;
  return {
    sessionId,
    lifecycleState: lifecycleStates[index % lifecycleStates.length],
    projectId,
    revision: 30 - index,
    updatedAtUtc: new Date(Date.parse(timestamp) - index * 60_000).toISOString()
  };
}

function historyRunSummary(index: number, sessions: readonly ReturnType<typeof historySessionSummary>[]) {
  const runNumber = String(index + 1).padStart(2, '0');
  const status = (['completed', 'running', 'failed', 'cancelled', 'blocked'] as const)[index % 5];
  const recoveryState = status === 'running'
    ? 'active'
    : status === 'blocked'
      ? 'reconciling'
      : 'terminal';
  return {
    runId: `run_history_f06_${runNumber}`,
    sessionId: sessions[index % sessions.length]?.sessionId ?? null,
    kind: index % 2 === 0 ? 'plan' : 'build',
    status,
    title: index % 2 === 0 ? '公开方案规划' : '公开候选构建',
    summary: index === 0
      ? '长历史摘要：复杂环境光变化下的高反光表面缺陷验证；PUBLIC_VALIDATION_RECOMMENDATION_WITHOUT_INTERNAL_IDENTIFIERS 可完整换行。'
      : `公开运行摘要 ${runNumber}`,
    firstFixRecommendation: index % 4 === 0
      ? '先确认公开参数与资源阻断，再从当前服务端会话继续。'
      : '',
    recoveryState,
    createdAtUtc: new Date(Date.parse(timestamp) - (index + 1) * 120_000).toISOString(),
    updatedAtUtc: new Date(Date.parse(timestamp) - index * 120_000).toISOString(),
    lastSequence: index + 3,
    eventCount: index + 3
  };
}

export async function installF06Fixture(page: Page, options: F06BrowserFixtureOptions = {}): Promise<F06BrowserAudit> {
  const role = options.role ?? 'Engineer';
  const flag = options.flag ?? true;
  const projectBound = options.projectBound ?? false;
  const activePlan = plan(options.longContent ?? false);
  const projects = new Map<string, ReturnType<typeof workspaceProject>>([
    [f06ProjectId, workspaceProject(f06ProjectId, 18, null)]
  ]);
  const consumeOperations = new Map<string, string>();
  let handoffOperationId = '11111111-1111-4111-8111-111111111111';
  let handoffCreateFailed = false;
  let saveResponseLost = false;
  let snapshot = sessionSnapshot(projectBound);
  let activeBuildOperationId = '44444444-4444-4444-8444-444444444444';
  const initialBuildState = options.initialBuildState ?? (options.recoveredBuild ? 'ready' : null);
  if (initialBuildState) {
    const currentBaseline = projectBaseline(projectBound);
    const recoveredBaseline = initialBuildState === 'baseline-conflict'
      ? { ...currentBaseline, persistenceRevision: 17, canonicalFlowHash: '8'.repeat(64) }
      : currentBaseline;
    const recovered = {
      ...buildResult(projectBound, true, true, activeBuildOperationId),
      projectBaseline: recoveredBaseline
    };
    const hasTerminalBuild = ['ready', 'baseline-conflict', 'stale'].includes(initialBuildState);
    const hasResolvedInputs = hasTerminalBuild;
    snapshot = sessionSnapshot(projectBound, {
      revision: 8,
      lifecycleState: initialBuildState === 'stale' ? 'build_inputs_changed' :
        initialBuildState === 'ready' ? 'build_ready' : initialBuildState,
      buildRunId: f06BuildRunId,
      planRunId: f06RunId,
      planRunStatus: 'completed',
      planTerminalSequence: 3,
      buildRunStatus: ['failed', 'cancelled'].includes(initialBuildState) ? initialBuildState :
        hasTerminalBuild ? 'completed' : 'running',
      buildTerminalSequence: hasTerminalBuild || ['failed', 'cancelled'].includes(initialBuildState) ? 1 : null,
      buildClientOperationId: activeBuildOperationId, submittedBuildFingerprint: f06BuildFingerprint,
      projectBaseline: recoveredBaseline,
      answerRevision: initialBuildState === 'stale' ? 3 : hasResolvedInputs ? 2 : 1,
      resourceRevision: hasResolvedInputs ? 1 : 0,
      buildParameterValues: hasResolvedInputs ? { 'threshold_1.Threshold': 128 } : {},
      resourceDecisions: hasResolvedInputs ? [{
        canonicalId: cameraResource().canonicalId, status: 'bound', resourceKey: '55555555-5555-4555-8555-555555555555',
        resourceType: 'camera_binding', operatorKey: 'imageacquisition#1', operatorId: 'acquire_1',
        operatorType: 'ImageAcquisition', operatorIndex: 0, parameterName: 'CameraBindingId',
        valueSummary: '顶视检测相机 A', source: 'camera_binding_authority'
      }] : [],
      missingResources: [], buildResult: hasTerminalBuild ? recovered : null
    });
  }
  let releaseBuildStream = () => {};
  let releaseRevalidation = () => {};
  let releaseHistory = () => {};
  const buildStreamGate = new Promise<void>(resolve => { releaseBuildStream = resolve; });
  const revalidationGate = new Promise<void>(resolve => { releaseRevalidation = resolve; });
  const historyGate = new Promise<void>(resolve => { releaseHistory = resolve; });
  const audit: F06BrowserAudit = {
    requests: [], consoleErrors: [], pageErrors: [], releaseBuildStream, releaseRevalidation, releaseHistory
  };
  const historySessions = options.historyMode === 'long'
    ? Array.from({ length: 25 }, (_, index) => historySessionSummary(index, projectBound))
    : [];
  const historyRuns = options.historyMode === 'long'
    ? Array.from({ length: 23 }, (_, index) => historyRunSummary(index, historySessions))
    : [];
  let deleteOperationLookups = 0;
  let pendingDeletedSessionId: string | null = null;
  function handoffArtifact(
    id: string,
    status = options.artifactStatus ?? 'available',
    consumeClientOperationId = consumeOperations.get(id) ?? null
  ) {
    const baseline = projectBound
      ? {
          ...projectBaseline(true),
          persistenceRevision: options.artifactBaselineRevision ?? 18
        }
      : projectBaseline(false);
    const build = {
      ...buildResult(projectBound, true, true, activeBuildOperationId),
      projectBaseline: baseline
    };
    return {
      schemaVersion: 1,
      artifactId: id,
      clientOperationId: handoffOperationId,
      sessionId: f06SessionId,
      sessionRevision: Number(snapshot.revision),
      planRunId: f06RunId,
      planId: f06PlanId,
      planHash: f06PlanHash,
      buildRunId: f06BuildRunId,
      buildClientOperationId: activeBuildOperationId,
      buildIdentity: build.buildIdentity,
      targetKind: projectBound ? 'existing' : 'new',
      projectBaseline: baseline,
      candidateFlow: candidateFlow(),
      candidateFlowFingerprint: f06CandidateFingerprint,
      build,
      createdAtUtc: timestamp,
      expiresAtUtc: '2026-07-29T08:30:00.000Z',
      status,
      consumeClientOperationId,
      consumeReceipt: status === 'consumed' ? {
        clientOperationId: consumeClientOperationId ?? '55555555-5555-4555-8555-555555555555',
        targetProjectId: projectBound ? f06ProjectId : null,
        result: 'workspace_staged',
        acknowledgedAtUtc: '2026-07-29T08:05:00.000Z',
        projectSaved: false
      } : null
    };
  }
  await installF02BrowserStartup(page, {
    'Studio2.AiWorkbench': flag,
    'Studio2.Workspace': true,
    'Studio2.ProjectPage': true,
    'Studio2.PropertyPanel': true,
    'Studio2.PreviewPanel': true,
    'Studio2.GlobalVariables': true
  });
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const location = message.location();
    const expectedHistoryFailure = (
      location.url.includes('/api/ai/sessions/') && (
        options.historyDelete === 'blocked' && message.text().includes('409') ||
        options.historyDelete === 'unknown-reconcile-deleted' && message.text().includes('ERR_CONNECTION_FAILED')
      )
    ) || (
      options.historyUnauthorized &&
      (location.url.includes('/api/ai/sessions?') || location.url.includes('/api/ai/agent-runs?')) &&
      message.text().includes('401')
    );
    if (expectedHistoryFailure) return;
    const source = location.url ? ` [${location.url}:${location.lineNumber + 1}]` : '';
    audit.consoleErrors.push(`${message.text()}${source}`);
  });
  page.on('pageerror', error => audit.pageErrors.push(error.stack ?? error.message));
  await page.route('**/health', route => fulfillF02Json(
    route,
    200,
    { status: 'Healthy', port: 50_012 },
    'f06-g2-ai.v1'
  ));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const body = request.postDataJSON?.() ?? null;
    audit.requests.push({ method: request.method(), path: `${url.pathname}${url.search}`, body });
    const json = (status: number, value: unknown) => fulfillF02Json(route, status, value, 'f06-g2-ai.v1');
    if (url.pathname === '/api/auth/setup-status') return json(200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
    if (url.pathname === '/api/auth/me') return json(200, { userId: 'f06-user', username: 'f06-engineer', role });
    if (url.pathname === '/api/auth/logout' && request.method() === 'POST') return json(200, {});
    if (url.pathname === '/api/operators/library') return json(200, []);
    if (url.pathname === '/api/cameras/bindings') {
      return json(200, []);
    }
    if (url.pathname === '/api/inspection/decision-configuration/validate' && request.method() === 'POST') {
      return json(200, {
        isValid: false,
        issues: [{
          code: 'DECISION_BINDING_REQUIRED',
          message: '请选择最终判定输出。',
          field: 'decisionConfiguration.finalDecisionBinding',
          operatorId: null,
          outputName: null
        }],
        eligibleOutputs: []
      });
    }
    const realtimeState = url.pathname.match(/^\/api\/inspection\/realtime\/([0-9a-f-]{36})\/state$/i);
    if (realtimeState && request.method() === 'GET') {
      return json(200, {
        projectId: realtimeState[1],
        status: 'Idle',
        isBusy: false,
        sessionId: null,
        startedAt: null,
        stoppedAt: null,
        clientSnapshotId: null,
        persistenceRevision: null,
        canonicalFlowHash: null,
        decisionConfigurationHash: null,
        executionSource: null,
        sessionType: null
      });
    }
    if (url.pathname === '/api/inspection/admission' && request.method() === 'POST') {
      const requestIdentity = body as {
        projectId: string;
        clientSnapshotId: string;
        expectedPersistenceRevision: number;
      };
      return json(200, {
        allowed: true,
        code: null,
        message: 'fixture admission allowed',
        projectId: requestIdentity.projectId,
        clientSnapshotId: requestIdentity.clientSnapshotId,
        projectPersistenceRevision: requestIdentity.expectedPersistenceRevision,
        canonicalFlowHash: 'fixture-f06-flow-hash',
        decisionConfigurationHash: 'fixture-f06-decision-hash',
        violations: []
      });
    }
    if (url.pathname === '/api/projects' && request.method() === 'POST') {
      const create = body as { clientOperationId: string; name: string; description: string | null };
      const created = workspaceProject(f06CreatedProjectId, 0, null, create.name, create.description);
      projects.set(f06CreatedProjectId, created);
      const details = projectDetails(created);
      return json(201, {
        projectId: f06CreatedProjectId,
        project: details,
        operationReplayed: false,
        operation: {
          clientOperationId: create.clientOperationId,
          kind: 'create',
          status: 'completed',
          projectId: f06CreatedProjectId,
          result: {
            project: details,
            projectDeleted: false,
            deleted: false,
            alreadyDeleted: false,
            cleanupStatus: 'not-required'
          },
          errorCode: null,
          createdAtUtc: timestamp,
          updatedAtUtc: timestamp,
          expiresAtUtc: null
        }
      });
    }
    const projectOpen = url.pathname.match(/^\/api\/projects\/([0-9a-f-]+)\/open$/i);
    if (projectOpen && request.method() === 'POST') {
      return json(200, { projectId: projectOpen[1], lastOpenedAtUtc: timestamp });
    }
    const projectRequest = url.pathname.match(/^\/api\/projects\/([0-9a-f-]+)$/i);
    if (projectRequest && request.method() === 'GET') {
      const current = projects.get(projectRequest[1]);
      return current
        ? json(200, page.url().includes('/workspace') ? current : projectDetails(current))
        : json(404, { errorCode: 'project_not_found', publicMessage: '工程不存在。' });
    }
    if (projectRequest && request.method() === 'PUT') {
      const current = projects.get(projectRequest[1]);
      if (!current) return json(404, { errorCode: 'project_not_found', publicMessage: '工程不存在。' });
      const update = body as {
        name: string;
        description: string | null;
        flow: ReturnType<typeof candidateFlow> | null;
        expectedPersistenceRevision: number;
      };
      if (update.expectedPersistenceRevision !== current.persistenceRevision) {
        return json(409, { errorCode: 'PSV011', publicMessage: '工程保存修订已变化。' });
      }
      const saved = workspaceProject(
        current.id,
        current.persistenceRevision + 1,
        update.flow,
        update.name,
        update.description
      );
      projects.set(current.id, saved);
      if (options.saveUnknownOutcome && !saveResponseLost) {
        saveResponseLost = true;
        return route.abort('connectionfailed');
      }
      return json(200, saved);
    }
    if (options.enableHandoff && url.pathname === '/api/ai/handoffs' && request.method() === 'POST') {
      handoffOperationId = String((body as { clientOperationId: string }).clientOperationId);
      if (options.handoffCreateUnknownOutcome && !handoffCreateFailed) {
        handoffCreateFailed = true;
        return route.abort('connectionfailed');
      }
      return json(201, handoffArtifact(f06HandoffArtifactId, 'available'));
    }
    if (options.enableHandoff && url.pathname === `/api/ai/handoffs/by-build/${f06BuildRunId}`) {
      return json(200, handoffArtifact(f06HandoffArtifactId, 'available'));
    }
    const handoffRequest = options.enableHandoff
      ? url.pathname.match(/^\/api\/ai\/handoffs\/([0-9a-f]{32})(?:\/(consume|acknowledge|reject))?$/i)
      : null;
    if (handoffRequest) {
      const id = handoffRequest[1];
      const action = handoffRequest[2];
      if (!action && request.method() === 'GET') {
        return json(200, handoffArtifact(id, options.artifactStatus ?? 'available'));
      }
      const operationId = String((body as { clientOperationId: string }).clientOperationId);
      if (action === 'consume') {
        consumeOperations.set(id, operationId);
        return json(200, handoffArtifact(id, 'consuming', operationId));
      }
      if (action === 'acknowledge') {
        return json(200, handoffArtifact(id, 'consumed', operationId));
      }
      if (action === 'reject') return json(200, handoffArtifact(id, 'rejected', operationId));
    }
    if (url.pathname === `/api/ai/projects/${f06ProjectId}/baseline`) {
      return json(200, projectBaseline(true));
    }
    if (url.pathname === '/api/ai/sessions' && request.method() === 'GET') {
      if (options.historyUnauthorized) {
        return json(401, { errorCode: 'session_expired', publicMessage: '当前会话已失效。' });
      }
      if (options.holdHistory) await historyGate;
      const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0));
      const limit = Math.min(100, Math.max(1, Number(url.searchParams.get('limit') ?? 10)));
      return json(200, {
        items: historySessions.slice(offset, offset + limit),
        offset,
        limit,
        total: historySessions.length
      });
    }
    if (url.pathname === '/api/ai/sessions' && request.method() === 'POST') {
      if (options.failSession) return json(200, { malformedPublicContract: true });
      return json(201, { operation: operation('session_create', String((body as { clientOperationId: string }).clientOperationId)), session: session(projectBound, snapshot) });
    }
    const sessionRequest = url.pathname.match(/^\/api\/ai\/sessions\/([a-z0-9_.:-]+)$/i);
    if (sessionRequest && request.method() === 'DELETE') {
      const deletedSessionId = sessionRequest[1]!;
      pendingDeletedSessionId = deletedSessionId;
      if (options.historyDelete === 'blocked') {
        return json(409, {
          errorCode: 'session_active_run_conflict',
          publicMessage: '会话仍有关联的活动构建；请等待终态并完成恢复后再删除。'
        });
      }
      if (options.historyDelete === 'unknown-reconcile-deleted') {
        return route.abort('connectionfailed');
      }
      const index = historySessions.findIndex(item => item.sessionId === deletedSessionId);
      if (index >= 0) historySessions.splice(index, 1);
      return json(200, {});
    }
    if (sessionRequest && request.method() === 'GET') {
      const requestedId = sessionRequest[1]!;
      const summary = historySessions.find(item => item.sessionId === requestedId);
      if (requestedId !== f06SessionId && !summary) {
        return json(404, { errorCode: 'ai_session_not_found', publicMessage: '会话不存在或不可访问。' });
      }
      const sessionProjectBound = summary?.projectId !== null && summary?.projectId !== undefined
        ? true
        : requestedId === f06SessionId
          ? projectBound
          : false;
      const canonicalSnapshot = requestedId === f06SessionId
        ? snapshot
        : sessionSnapshot(sessionProjectBound, {
            revision: summary?.revision ?? 1,
            lifecycleState: summary?.lifecycleState ?? 'idle'
          });
      return json(200, session(sessionProjectBound, canonicalSnapshot, requestedId));
    }
    if (url.pathname === '/api/ai/agent-runs' && request.method() === 'GET') {
      if (options.historyUnauthorized) {
        return json(401, { errorCode: 'session_expired', publicMessage: '当前会话已失效。' });
      }
      if (options.holdHistory) await historyGate;
      const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0));
      const limit = Math.min(100, Math.max(1, Number(url.searchParams.get('limit') ?? 10)));
      const requestedSessionId = url.searchParams.get('sessionId');
      const scopedRuns = requestedSessionId
        ? historyRuns.filter(item => item.sessionId === requestedSessionId)
        : historyRuns;
      return json(200, {
        items: scopedRuns.slice(offset, offset + limit),
        offset,
        limit,
        total: scopedRuns.length
      });
    }
    if (url.pathname.startsWith('/api/ai/operations/') &&
        url.searchParams.get('kind') === 'session_delete') {
      deleteOperationLookups += 1;
      const clientOperationId = url.pathname.split('/').at(-1)!;
      const status = options.historyDelete === 'unknown-reconcile-deleted' && deleteOperationLookups === 1
        ? 'pending'
        : 'created';
      if (status === 'created') {
        const index = historySessions.findIndex(item => item.sessionId === pendingDeletedSessionId);
        if (index >= 0) historySessions.splice(index, 1);
      }
      return json(200, {
        ...operation('session_create', clientOperationId),
        kind: 'session_delete',
        status,
        runId: null
      });
    }
    if (options.failSession && url.pathname.startsWith('/api/ai/operations/')) {
      const clientOperationId = url.pathname.split('/').at(-1)!;
      return json(200, {
        ...operation('session_create', clientOperationId),
        status: 'pending',
        sessionId: null
      });
    }
    if (options.buildUnknownOutcome && url.pathname.startsWith('/api/ai/operations/') &&
        url.searchParams.get('kind') === 'build_run') {
      const clientOperationId = url.pathname.split('/').at(-1)!;
      return json(200, {
        ...operation('build_run', clientOperationId, projectBound), status: 'pending', runId: null
      });
    }
    if (url.pathname === '/api/ai/agent-intent-router-runs') return json(200, intent());
    if (url.pathname === '/api/ai/agent-plan-runs') {
      snapshot = sessionSnapshot(projectBound, { revision: 2, lifecycleState: 'planning', planRunId: f06RunId, planRunStatus: 'running' });
      const first = runEvent(1, 'plan.started', activePlan, snapshot);
      return json(200, {
        runId: f06RunId, sessionId: f06SessionId, brief: '正在规划', events: [first],
        workspaceSnapshot: snapshot, operation: operation('plan_run', String((body as { clientOperationId: string }).clientOperationId)),
        persistenceStatus: {}
      });
    }
    if (url.pathname === '/api/ai/agent-runs' && request.method() === 'POST') {
      activeBuildOperationId = String((body as { clientOperationId: string }).clientOperationId);
      if (options.buildUnknownOutcome) {
        return json(200, {
          runId: f06BuildRunId, sessionId: 'other_session', brief: '正在构建', events: [],
          workspaceSnapshot: snapshot,
          operation: operation('build_run', activeBuildOperationId, projectBound),
          persistenceStatus: {}, metadataOnly: true
        });
      }
      snapshot = sessionSnapshot(projectBound, {
        ...snapshot, revision: Number(snapshot.revision) + 1, lifecycleState: 'building',
        buildRunId: f06BuildRunId, buildRunStatus: 'running',
        buildClientOperationId: activeBuildOperationId, projectBaseline: projectBaseline(projectBound),
        submittedBuildFingerprint: f06BuildFingerprint
      });
      return json(200, {
        runId: f06BuildRunId, sessionId: f06SessionId, brief: '正在构建', events: [],
        workspaceSnapshot: snapshot,
        operation: operation('build_run', activeBuildOperationId, projectBound),
        persistenceStatus: {}, metadataOnly: true
      });
    }
    if (url.pathname === `/api/ai/agent-runs/${f06RunId}`) {
      return json(200, replay([runEvent(1, 'plan.started', activePlan, snapshot)], 'running'));
    }
    if (url.pathname === `/api/ai/agent-runs/${f06RunId}/events`) {
      const terminalSnapshot = sessionSnapshot(projectBound, { revision: 3, lifecycleState: 'plan_blocked', planRunId: f06RunId, planRunStatus: 'completed', planTerminalSequence: 2 });
      snapshot = terminalSnapshot;
      const streamEvents = [
        runEvent(2, 'plan.completed', activePlan, terminalSnapshot),
        runEvent(3, 'run.completed', activePlan, terminalSnapshot)
      ];
      return route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        headers: { 'x-clearvision-fixture-schema': 'f06-g2-ai.v1' },
        body: streamEvents.map(event => `id: ${event.sequence}\nevent: ${event.eventType}\ndata: ${JSON.stringify(event)}\n\n`).join('')
      });
    }
    if (url.pathname === `/api/ai/agent-runs/${f06BuildRunId}`) {
      if (initialBuildState === 'building') return json(200, buildReplay([buildProgressEvent(1, 'build')], 'running'));
      if (initialBuildState === 'validating') return json(200, buildReplay([buildProgressEvent(1, 'validation')], 'running'));
      if (initialBuildState === 'failed') return json(200, buildReplay([buildOutcomeEvent('run.failed')], 'failed'));
      if (initialBuildState === 'cancelled') return json(200, buildReplay([buildOutcomeEvent('run.cancelled')], 'cancelled'));
      return json(200, buildReplay([], 'running'));
    }
    if (url.pathname === `/api/ai/agent-runs/${f06BuildRunId}/events`) {
      if (initialBuildState === 'building' || initialBuildState === 'validating') await buildStreamGate;
      const build = buildResult(projectBound, false, false, activeBuildOperationId);
      const terminalSequence = initialBuildState === 'building' || initialBuildState === 'validating' ? 2 : 1;
      snapshot = sessionSnapshot(projectBound, {
        ...snapshot, revision: Number(snapshot.revision) + 1, lifecycleState: 'parameters_pending',
        buildRunId: f06BuildRunId, buildRunStatus: 'completed', buildTerminalSequence: terminalSequence,
        buildClientOperationId: activeBuildOperationId, submittedBuildFingerprint: f06BuildFingerprint,
        projectBaseline: projectBaseline(projectBound), missingResources: build.missingResources,
        buildResult: build
      });
      const terminal = buildRunEvent(terminalSequence, build, snapshot);
      return route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        headers: { 'x-clearvision-fixture-schema': 'f06-g3-ai.v1' },
        body: `id: ${terminalSequence}\nevent: run.completed\ndata: ${JSON.stringify(terminal)}\n\n`
      });
    }
    if (url.pathname === '/api/ai/resource-candidates/camera-bindings') return json(200, [{
      id: '55555555-5555-4555-8555-555555555555', displayName: '顶视检测相机 A',
      isEnabled: true
    }]);
    if (url.pathname === `/api/ai/agent-runs/${f06BuildRunId}/revalidate`) {
      if (options.holdRevalidation) await revalidationGate;
      const parameterConfirmed = Object.prototype.hasOwnProperty.call(
        snapshot.buildParameterValues as Record<string, unknown>, 'threshold_1.Threshold'
      );
      const resourceBound = (snapshot.resourceDecisions as unknown[]).length > 0;
      const build = buildResult(projectBound, parameterConfirmed, resourceBound, activeBuildOperationId);
      snapshot = sessionSnapshot(projectBound, {
        ...snapshot, revision: Number(snapshot.revision) + 1,
        lifecycleState: build.validation.handoffEligible ? 'build_ready' : 'resources_pending',
        answerRevision: build.answerRevision, resourceRevision: build.resourceRevision,
        missingResources: build.missingResources, buildResult: build
      });
      return json(200, { build, snapshot, metadataOnly: true });
    }
    if (url.pathname === `/api/ai/sessions/${f06SessionId}/workspace-snapshot`) {
      const mutation = body as Record<string, unknown>;
      const selections = Array.isArray(mutation.resourceDecisions)
        ? mutation.resourceDecisions as Array<Record<string, unknown>>
        : null;
      const canonicalDecisions = selections?.map(selection => ({
        canonicalId: String(selection.canonicalId), status: 'bound',
        resourceKey: String(selection.resourceKey), resourceType: 'camera_binding',
        operatorKey: 'imageacquisition#1', operatorId: 'acquire_1',
        operatorType: 'ImageAcquisition', operatorIndex: 0, parameterName: 'CameraBindingId',
        valueSummary: '顶视检测相机 A', source: 'camera_binding_authority'
      }));
      snapshot = {
        ...snapshot,
        ...mutation,
        ...(canonicalDecisions ? {
          resourceDecisions: canonicalDecisions,
          resourceRevision: Number(snapshot.resourceRevision) + 1
        } : {}),
        revision: Number(snapshot.revision) + 1,
        updatedAtUtc: timestamp
      };
      delete (snapshot as Record<string, unknown>).expectedRevision;
      delete (snapshot as Record<string, unknown>).clientMutationId;
      return json(200, { snapshot });
    }
    if (url.pathname === '/api/ai/agent-plan/readiness-preview') return json(200, readinessPreview(true));
    return json(404, { errorCode: 'not_found', publicMessage: `Fixture route not found: ${url.pathname}` });
  });
  return audit;
}

function evidenceRoot(): string | null {
  const configured = process.env.CV_F06_EVIDENCE_DIR?.trim();
  if (!configured) return null;
  const repositoryRoot = resolve(process.cwd(), '..', '..', '..');
  const allowedRoot = resolve(repositoryRoot, '.tmp', 'studio-ui-next', 'f06-g5');
  const output = isAbsolute(configured) ? resolve(configured) : resolve(repositoryRoot, configured);
  const relativeOutput = relative(allowedRoot, output);
  if (relativeOutput.startsWith('..') || isAbsolute(relativeOutput)) {
    throw new Error('CV_F06_EVIDENCE_DIR must remain under .tmp/studio-ui-next/f06-g5.');
  }
  return output;
}

export async function captureF06Evidence(
  page: Page,
  audit: F06BrowserAudit,
  scenario: string,
  viewport: Readonly<{ width: number; height: number }>,
  density: 'compact' | 'comfortable'
): Promise<void> {
  const root = evidenceRoot();
  if (!root) return;
  const safeScenario = scenario.replace(/[^a-z0-9_.-]+/gi, '-').toLowerCase();
  await mkdir(root, { recursive: true });
  const projection = await page.evaluate(() => {
    const visibleText = document.body.innerText;
    const sensitiveSentinels = [
      'SYSTEM_PROMPT_SENTINEL', 'RAW_TOOL_PAYLOAD_SENTINEL', 'sk-private-f06',
      'C:\\factory\\secret', '10.23.45.67', '192.168.88.9'
    ];
    return {
      viewport: { width: window.innerWidth, height: window.innerHeight },
      density: document.documentElement.dataset.density ?? null,
      overflow: Math.max(document.documentElement.scrollWidth - document.documentElement.clientWidth, document.body.scrollWidth - document.body.clientWidth),
      dpr: window.devicePixelRatio,
      dialogHorizontalOverflow: [...document.querySelectorAll<HTMLElement>('[role="dialog"], [role="dialog"] *')]
        .filter(element => element.getClientRects().length > 0 && element.scrollWidth - element.clientWidth > 1)
        .map(element => ({
          element: `${element.tagName.toLowerCase()}${element.className ? `.${String(element.className).trim().replace(/\s+/g, '.')}` : ''}`,
          overflow: element.scrollWidth - element.clientWidth
        })),
      sensitiveLeaks: sensitiveSentinels.filter(value => visibleText.includes(value))
    };
  });
  if (projection.overflow > 1) throw new Error(`F06 visual evidence has ${projection.overflow}px horizontal overflow.`);
  if (projection.dialogHorizontalOverflow.length) {
    throw new Error(`F06 drawer evidence has horizontal overflow: ${JSON.stringify(projection.dialogHorizontalOverflow)}.`);
  }
  if (projection.density !== density) throw new Error(`F06 density mismatch: ${JSON.stringify(projection)}.`);
  if (projection.sensitiveLeaks.length) throw new Error(`F06 sensitive field leak: ${JSON.stringify(projection.sensitiveLeaks)}.`);
  if (audit.consoleErrors.length || audit.pageErrors.length) throw new Error(`F06 runtime errors: ${JSON.stringify(audit)}.`);
  const screenshot = await page.screenshot({ animations: 'disabled', fullPage: false, type: 'png' });
  const safeDpr = String(projection.dpr).replace('.', '_');
  const stem = `${safeScenario}-${viewport.width}x${viewport.height}-${density}-dpr-${safeDpr}`;
  await writeFile(resolve(root, `${stem}.png`), screenshot);
  await writeFile(resolve(root, `${stem}.json`), `${JSON.stringify({
    schemaVersion: 'f06-g5-browser-evidence.v1', sourceSha: process.env.CV_F06_SOURCE_SHA ?? 'WORKTREE',
    MODEL_MODE: 'RULE_FALLBACK', DATA_SOURCE: 'DETERMINISTIC_BROWSER_FIXTURE', scenario,
    url: page.url(), viewport, observed: projection, density, requestCount: audit.requests.length,
    forbiddenRequests: audit.requests.filter(item =>
      /handoff|apply-to-canvas|workspace\/consume|project\/save|flow-canvas|image-canvas/i.test(item.path) ||
      item.method === 'PUT' && /^\/api\/projects(?:\/|$)/i.test(item.path)),
    consoleErrors: audit.consoleErrors, pageErrors: audit.pageErrors,
    screenshot: { sha256: createHash('sha256').update(screenshot).digest('hex'), bytes: screenshot.byteLength },
    WINDOWS_DPI: 'NOT_PERFORMED', REAL_LLM_PRODUCT_QUALITY: 'NOT_EVALUATED'
  }, null, 2)}\n`, 'utf8');
}
