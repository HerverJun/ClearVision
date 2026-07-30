import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { isAbsolute, relative, resolve } from 'node:path';
import type { Page, Route } from '@playwright/test';
import { fulfillF02Json, installF02BrowserStartup } from './f02-browser-fixture';

export const f06ProjectId = '22222222-2222-4222-8222-222222222222';
export const f06SessionId = 'session_f06_01';
export const f06RunId = 'run_plan_f06_01';
export const f06BuildRunId = 'run_build_f06_01';
export const f06PlanId = 'plan_f06_01';
export const f06PlanHash = 'a'.repeat(64);
export const f06BuildId = 'build_f06_01';
export const f06BuildFingerprint = 'd'.repeat(64);
export const f06CandidateFingerprint = 'e'.repeat(64);
const timestamp = '2026-07-29T08:00:00.000Z';

export interface F06BrowserAudit {
  readonly requests: Array<{ method: string; path: string; body: unknown }>;
  readonly consoleErrors: string[];
  readonly pageErrors: string[];
}

export interface F06BrowserFixtureOptions {
  readonly role?: 'Admin' | 'Engineer' | 'Operator';
  readonly flag?: boolean;
  readonly projectBound?: boolean;
  readonly failSession?: boolean;
  readonly longContent?: boolean;
  readonly recoveredBuild?: boolean;
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

function session(projectBound: boolean, snapshot: Record<string, unknown>) {
  return { sessionId: f06SessionId, snapshot, updatedAtUtc: timestamp };
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
    atLeastOneGroup: '', mutuallyExclusiveGroup: '', requiredWhen: [], enabledWhen: [],
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

function buildReplay(events: readonly Record<string, unknown>[], status: 'running' | 'completed') {
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

export async function installF06Fixture(page: Page, options: F06BrowserFixtureOptions = {}): Promise<F06BrowserAudit> {
  const role = options.role ?? 'Engineer';
  const flag = options.flag ?? true;
  const projectBound = options.projectBound ?? false;
  const activePlan = plan(options.longContent ?? false);
  let snapshot = sessionSnapshot(projectBound);
  let activeBuildOperationId = '44444444-4444-4444-8444-444444444444';
  if (options.recoveredBuild) {
    const recovered = buildResult(projectBound, true, true, activeBuildOperationId);
    snapshot = sessionSnapshot(projectBound, {
      revision: 8, lifecycleState: 'build_ready', buildRunId: f06BuildRunId,
      buildRunStatus: 'completed', buildTerminalSequence: 1,
      buildClientOperationId: activeBuildOperationId, submittedBuildFingerprint: f06BuildFingerprint,
      projectBaseline: projectBaseline(projectBound), answerRevision: 2, resourceRevision: 1,
      buildParameterValues: { 'threshold_1.Threshold': 128 },
      resourceDecisions: [], missingResources: [], buildResult: recovered
    });
  }
  const audit: F06BrowserAudit = { requests: [], consoleErrors: [], pageErrors: [] };
  await installF02BrowserStartup(page, { 'Studio2.AiWorkbench': flag });
  page.on('console', message => { if (message.type() === 'error') audit.consoleErrors.push(message.text()); });
  page.on('pageerror', error => audit.pageErrors.push(error.stack ?? error.message));
  await page.route('**/health', route => fulfillF02Json(route, 200, { status: 'Healthy' }, 'f06-g2-ai.v1'));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const body = request.postDataJSON?.() ?? null;
    audit.requests.push({ method: request.method(), path: `${url.pathname}${url.search}`, body });
    const json = (status: number, value: unknown) => fulfillF02Json(route, status, value, 'f06-g2-ai.v1');
    if (url.pathname === '/api/auth/setup-status') return json(200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
    if (url.pathname === '/api/auth/me') return json(200, { userId: 'f06-user', username: 'f06-engineer', role });
    if (url.pathname === `/api/projects/${f06ProjectId}`) return json(200, {
      id: f06ProjectId, name: '新能源托盘超长中文名称外观检测工程', description: '高反光表面缺陷检测',
      version: '2.3.0', persistenceRevision: 18, createdAt: timestamp, modifiedAt: timestamp,
      lastOpenedAt: timestamp, flow: null, assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
    });
    if (url.pathname === `/api/ai/projects/${f06ProjectId}/baseline`) {
      return json(200, projectBaseline(true));
    }
    if (url.pathname === '/api/ai/sessions' && request.method() === 'POST') {
      if (options.failSession) return json(200, { malformedPublicContract: true });
      return json(201, { operation: operation('session_create', String((body as { clientOperationId: string }).clientOperationId)), session: session(projectBound, snapshot) });
    }
    if (url.pathname === `/api/ai/sessions/${f06SessionId}` && request.method() === 'GET') {
      return json(200, session(projectBound, snapshot));
    }
    if (options.failSession && url.pathname.startsWith('/api/ai/operations/')) {
      const clientOperationId = url.pathname.split('/').at(-1)!;
      return json(200, {
        ...operation('session_create', clientOperationId),
        status: 'pending',
        sessionId: null
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
      return json(200, buildReplay([], 'running'));
    }
    if (url.pathname === `/api/ai/agent-runs/${f06BuildRunId}/events`) {
      const build = buildResult(projectBound, false, false, activeBuildOperationId);
      snapshot = sessionSnapshot(projectBound, {
        ...snapshot, revision: Number(snapshot.revision) + 1, lifecycleState: 'parameters_pending',
        buildRunId: f06BuildRunId, buildRunStatus: 'completed', buildTerminalSequence: 1,
        buildClientOperationId: activeBuildOperationId, submittedBuildFingerprint: f06BuildFingerprint,
        projectBaseline: projectBaseline(projectBound), missingResources: build.missingResources,
        buildResult: build
      });
      const terminal = buildRunEvent(1, build, snapshot);
      return route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        headers: { 'x-clearvision-fixture-schema': 'f06-g3-ai.v1' },
        body: `id: 1\nevent: run.completed\ndata: ${JSON.stringify(terminal)}\n\n`
      });
    }
    if (url.pathname === '/api/cameras/bindings') return json(200, [{
      id: '55555555-5555-4555-8555-555555555555', displayName: '顶视检测相机 A',
      deviceId: 'camera-a', manufacturer: 'ClearVision', modelName: 'CV-Line', triggerMode: 'software',
      isEnabled: true, connectionStatus: 'connected'
    }]);
    if (url.pathname === `/api/ai/agent-runs/${f06BuildRunId}/revalidate`) {
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
      snapshot = { ...snapshot, ...mutation, revision: Number(snapshot.revision) + 1, updatedAtUtc: timestamp };
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
  const allowedRoot = resolve(repositoryRoot, '.tmp', 'studio-ui-next', 'f06-g3');
  const output = isAbsolute(configured) ? resolve(configured) : resolve(repositoryRoot, configured);
  const relativeOutput = relative(allowedRoot, output);
  if (relativeOutput.startsWith('..') || isAbsolute(relativeOutput)) {
    throw new Error('CV_F06_EVIDENCE_DIR must remain under .tmp/studio-ui-next/f06-g3.');
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
  const projection = await page.evaluate(() => ({
    viewport: { width: window.innerWidth, height: window.innerHeight },
    density: document.documentElement.dataset.density ?? null,
    overflow: Math.max(document.documentElement.scrollWidth - document.documentElement.clientWidth, document.body.scrollWidth - document.body.clientWidth),
    dpr: window.devicePixelRatio
  }));
  if (projection.overflow > 1) throw new Error(`F06 visual evidence has ${projection.overflow}px horizontal overflow.`);
  if (projection.density !== density) throw new Error(`F06 density mismatch: ${JSON.stringify(projection)}.`);
  if (audit.consoleErrors.length || audit.pageErrors.length) throw new Error(`F06 runtime errors: ${JSON.stringify(audit)}.`);
  const screenshot = await page.screenshot({ animations: 'disabled', fullPage: false, type: 'png' });
  const stem = `${safeScenario}-${viewport.width}x${viewport.height}-${density}`;
  await writeFile(resolve(root, `${stem}.png`), screenshot);
  await writeFile(resolve(root, `${stem}.json`), `${JSON.stringify({
    schemaVersion: 'f06-g3-browser-evidence.v1', sourceSha: process.env.CV_F06_SOURCE_SHA ?? 'WORKTREE',
    MODEL_MODE: 'RULE_FALLBACK', DATA_SOURCE: 'DETERMINISTIC_BROWSER_FIXTURE', scenario,
    url: page.url(), viewport, observed: projection, density, requestCount: audit.requests.length,
    forbiddenRequests: audit.requests.filter(item => /handoff|apply-to-canvas|workspace\/consume|project\/save/i.test(item.path)),
    consoleErrors: audit.consoleErrors, pageErrors: audit.pageErrors,
    screenshot: { sha256: createHash('sha256').update(screenshot).digest('hex'), bytes: screenshot.byteLength },
    WINDOWS_DPI: 'NOT_PERFORMED', REAL_LLM_PRODUCT_QUALITY: 'NOT_EVALUATED'
  }, null, 2)}\n`, 'utf8');
}
