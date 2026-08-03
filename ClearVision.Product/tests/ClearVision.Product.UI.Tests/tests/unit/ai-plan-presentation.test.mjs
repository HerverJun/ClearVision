import test from 'node:test';
import assert from 'node:assert/strict';
import {
  aiPanelPlanPresentationTestApi,
  deriveAiPlanPresentation
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelPlanPresentation.js';
import {
  deriveAiClarificationPresentation,
  renderAiClarification,
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelClarificationPresentation.js';

function createQuestion(overrides = {}) {
  return {
    id: 'question-image-source',
    questionId: 'question-image-source',
    field: 'image_source',
    title: '图像从哪里获取？',
    why: '图像来源会影响采集算子和验证方式。',
    blocksBuild: true,
    interactive: true,
    kind: 'question',
    options: [
      {
        value: 'industrial_camera',
        label: '工业相机',
        recommended: true,
        answerEffect: 'resolve_field',
        description: '使用现场相机作为稳定输入。',
      },
      {
        value: 'image_folder',
        label: '图片目录',
        recommended: false,
        answerEffect: 'resolve_field',
        description: '先使用离线图片完成验证。',
      },
    ],
    ...overrides,
  };
}

function createPlan(overrides = {}) {
  const question = createQuestion();
  return {
    planId: 'plan-v2',
    planHash: 'sha256:plan-v2',
    confidence: 'high',
    planSource: 'model_router',
    operatorCatalogVersion: 'catalog-1',
    templateCatalogVersion: 'template-1',
    route: {
      title: '包装箱外观检测流程',
      summary: '采集图像后限定检测区域并判断破损。',
      operators: ['ImageAcquisition', 'RoiManager', 'SurfaceDefectDetection', 'ResultOutput'],
    },
    semanticExtraction: {
      inspectionObject: '包装箱表面',
      taskType: 'surface_defect',
      imageSource: '',
      defectType: '破损',
      okCondition: '表面完整',
      ngCondition: '存在可见破损',
      outputTarget: 'OK/NG',
    },
    questions: [question],
    assumptions: ['光照保持稳定'],
    acceptanceCriteria: ['破损样本输出 NG'],
    risks: ['现场光照需要复核'],
    contractRepairNotes: [],
    ...overrides,
  };
}

function createPanel({ queue = [], batch = queue, missingResources = [], confirmed = {}, optimistic = {}, readinessStatus = 'ready', readinessError = '' } = {}) {
  return {
    requirementMode: 'strict',
    agentWorkspaceState: {
      answers: {
        confirmedByField: confirmed,
        optimisticByField: optimistic,
        selectionByQuestion: {},
      },
      readinessStatus,
      readinessError,
      projection: {
        clarificationQueue: queue,
        clarificationBatch: batch,
        missingResources,
        readiness: { canBuild: false, blockers: [] },
      },
    },
    _escapeHtml: value => String(value ?? ''),
    _localizeDisplayText: value => String(value ?? ''),
    _formatRequirementTaskTypeLabel: value => value === 'surface_defect' ? '表面缺陷检测' : value,
    _formatOperatorType: value => ({
      ImageAcquisition: '图像采集',
      RoiManager: '检测区域',
      SurfaceDefectDetection: '表面缺陷检测',
      ResultOutput: '结果输出',
    }[value] || value),
    _inferPlanQuestionFieldForQuestion: question => question.field,
    _getMissingResourceActionModel: resource => ({
      action: resource.resourceType === 'model_resource' ? 'pick_model_resource' : 'open_resource_location',
      primaryLabel: resource.resourceType === 'model_resource' ? '选择模型文件' : '前往解决位置',
      parameterName: resource.parameterName || '',
    }),
    _renderResourceAuditTaskCard: (resource, actionModel) => `
      <article class="ai-resource-audit-card">
        <span>资源 ${resource.resourceName}</span>
        <span>影响算子 ${resource.operatorKey}</span>
        <span>影响参数 ${resource.parameterName}</span>
        <span>阻断范围 ${resource.blockingScope}</span>
        <span>解决位置 ${resource.resolutionTarget}</span>
        <button data-resource-action="${actionModel.action}">${actionModel.primaryLabel}</button>
      </article>`,
    _canViewBuildWorkspace: () => false,
    _isPlanSnapshotReadOnly: () => false,
  };
}

test('Plan-ready presentation shows understanding and recommendation without changing the gate', () => {
  const panel = createPanel({ queue: [], batch: [] });
  panel.agentWorkspaceState.projection.readiness = { canBuild: true, blockers: [] };
  const beforeGate = structuredClone(panel.agentWorkspaceState.projection.readiness);
  const presentation = deriveAiPlanPresentation(panel, createPlan({ questions: [] }));

  assert.equal(presentation.understanding.find(item => item.field === 'inspection_object').value, '包装箱表面');
  assert.equal(presentation.understanding.find(item => item.field === 'task_type').value, '表面缺陷检测');
  assert.equal(presentation.route.title, '包装箱外观检测流程');
  assert.equal(presentation.route.operators.length, 4);
  assert.deepEqual(panel.agentWorkspaceState.projection.readiness, beforeGate);
});

test('task understanding presents canonical answer labels instead of internal enum values', () => {
  const question = createQuestion();
  const panel = createPanel({
    queue: [],
    batch: [],
    confirmed: {
      image_source: { field: 'image_source', questionId: question.id, value: 'industrial_camera', resolved: true },
    },
  });
  const presentation = deriveAiPlanPresentation(panel, createPlan({ questions: [question] }));
  const imageSource = presentation.understanding.find(item => item.field === 'image_source');

  assert.equal(imageSource.value, '工业相机');
  assert.notEqual(imageSource.value, 'industrial_camera');
});

test('single canonical question exposes recommended and ordinary choices with impact copy', () => {
  const question = createQuestion();
  const panel = createPanel({ queue: [question], batch: [question] });
  const presentation = deriveAiClarificationPresentation(panel, createPlan());
  const html = renderAiClarification(panel, createPlan());

  assert.equal(presentation.activeQuestion.field, 'image_source');
  assert.equal(presentation.activeQuestion.options.length, 2);
  assert.match(html, /推荐/);
  assert.match(html, /使用现场相机作为稳定输入/);
  assert.match(html, /先使用离线图片完成验证/);
});

test('multiple questions highlight only the first and report remaining progress', () => {
  const first = createQuestion();
  const second = createQuestion({
    id: 'question-output',
    questionId: 'question-output',
    field: 'output_target',
    title: '结果输出到哪里？',
  });
  const panel = createPanel({ queue: [first, second], batch: [first, second] });
  const presentation = deriveAiClarificationPresentation(panel, createPlan({ questions: [first, second] }));
  const html = renderAiClarification(panel, createPlan({ questions: [first, second] }));

  assert.equal(presentation.activeQuestion.field, 'image_source');
  assert.equal(presentation.unresolvedCount, 2);
  assert.equal((html.match(/data-ai-hook="clarification-question"/g) || []).length, 1);
  assert.match(html, /还需确认 2 项 · 当前第 1 项/);
});

test('optimistic answer remains visible as confirming until canonical confirmation arrives', () => {
  const question = createQuestion();
  const optimistic = {
    image_source: { field: 'image_source', questionId: question.id, value: 'industrial_camera', origin: 'explicit_user_selection', resolved: true },
  };
  const panel = createPanel({ queue: [question], batch: [question], optimistic, readinessStatus: 'validating' });
  let presentation = deriveAiClarificationPresentation(panel, createPlan());
  assert.equal(presentation.activeQuestion.confirming, true);
  assert.match(renderAiClarification(panel, createPlan()), /正在等待权威 Readiness 确认/);

  panel.agentWorkspaceState.answers.optimisticByField = {};
  panel.agentWorkspaceState.answers.confirmedByField = {
    image_source: { field: 'image_source', questionId: question.id, value: 'industrial_camera', origin: 'explicit_user_selection', resolved: true },
  };
  panel.agentWorkspaceState.readinessStatus = 'ready';
  presentation = deriveAiClarificationPresentation(panel, createPlan());
  assert.equal(presentation.activeQuestion, null);
  assert.equal(presentation.confirmedItems[0].confirmedAnswer.value, 'industrial_camera');
  assert.equal(presentation.confirmedItems[0].confirmedDisplayValue, '工业相机');
  assert.doesNotMatch(renderAiClarification(panel, createPlan()), /industrial_camera/);
});

test('Readiness failure restores question operability and never opens Build locally', () => {
  const question = createQuestion();
  const optimistic = {
    image_source: { field: 'image_source', questionId: question.id, value: 'image_folder', origin: 'explicit_user_selection', resolved: true },
  };
  const panel = createPanel({
    queue: [question],
    batch: [question],
    optimistic,
    readinessStatus: 'failed',
    readinessError: '权威校验失败',
  });
  const html = renderAiClarification(panel, createPlan());
  assert.match(html, /权威校验失败/);
  assert.doesNotMatch(html, /data-ai-plan-option="true"[^>]*disabled/);
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, false);
});

test('newer optimistic choice wins over stale confirmed choice in presentation', () => {
  const question = createQuestion();
  const panel = createPanel({
    queue: [question],
    batch: [question],
    confirmed: {
      image_source: { field: 'image_source', questionId: question.id, value: 'industrial_camera', resolved: true },
    },
    optimistic: {
      image_source: { field: 'image_source', questionId: question.id, value: 'image_folder', resolved: true },
    },
    readinessStatus: 'validating',
  });
  const presentation = deriveAiClarificationPresentation(panel, createPlan());
  assert.equal(presentation.activeQuestion.selectedValue, 'image_folder');
  assert.equal(presentation.activeQuestion.confirming, true);
});

test('deferred clarification never appears in the confirmed summary', () => {
  const question = createQuestion({
    options: [
      { value: 'industrial_camera', label: '工业相机', answerEffect: 'resolve_field' },
      { value: 'camera_pending', label: '稍后补充', answerEffect: 'defer' },
    ],
  });
  const deferred = { ...question, deferred: true, selectedValue: 'camera_pending' };
  const panel = createPanel({
    queue: [deferred],
    batch: [deferred],
    confirmed: {
      image_source: {
        field: 'image_source',
        questionId: question.id,
        value: 'industrial_camera',
        resolved: true,
      },
    },
  });

  const presentation = deriveAiClarificationPresentation(panel, createPlan({ questions: [question] }));

  assert.equal(presentation.confirmedItems.length, 0);
  assert.equal(presentation.deferredItems.length, 1);
  assert.equal(presentation.deferredItems[0].confirmed, false);
});

test('accept-all recommendation is hidden for safety, resource, or non-resolving recommendations', () => {
  const safe = createQuestion();
  const safePanel = createPanel({ queue: [safe], batch: [safe] });
  assert.equal(deriveAiClarificationPresentation(safePanel, createPlan()).canAcceptAllRecommended, true);

  const unsafe = createQuestion({ category: 'safety_blocker' });
  const unsafePanel = createPanel({ queue: [unsafe], batch: [unsafe] });
  assert.equal(deriveAiClarificationPresentation(unsafePanel, createPlan({ questions: [unsafe] })).canAcceptAllRecommended, false);
});

test('manual supplement is scoped to the current canonical question and does not duplicate the composer', () => {
  const question = createQuestion();
  const panel = createPanel({ queue: [question], batch: [question] });
  const html = renderAiClarification(panel, createPlan());
  assert.match(html, /补充内容将用于「图像从哪里获取？」/);
  assert.equal((html.match(/<textarea/g) || []).length, 1);
  assert.doesNotMatch(html, /id="ai-input"/);
});

test('canonical restored confirmed and validating answers rebuild summary and confirming state', () => {
  const first = createQuestion();
  const second = createQuestion({ id: 'question-output', questionId: 'question-output', field: 'output_target', title: '输出目标' });
  const panel = createPanel({
    queue: [first, second],
    batch: [first, second],
    confirmed: {
      image_source: { field: 'image_source', questionId: first.id, value: 'industrial_camera', resolved: true },
    },
    optimistic: {
      output_target: { field: 'output_target', questionId: second.id, value: 'image_folder', resolved: true },
    },
    readinessStatus: 'validating',
  });
  const presentation = deriveAiClarificationPresentation(panel, createPlan({ questions: [first, second] }));
  assert.equal(presentation.confirmedItems[0].field, 'image_source');
  assert.equal(presentation.activeQuestion.field, 'output_target');
  assert.equal(presentation.activeQuestion.confirming, true);
});

test('Router text and local clarification payload cannot create a second question card', () => {
  const panel = createPanel({ queue: [], batch: [] });
  panel.pendingClarificationPayload = {
    questions: [{ id: 'legacy-router-question', title: '旧 Router 问题' }],
  };
  const presentation = deriveAiClarificationPresentation(panel, createPlan({ questions: [] }));
  const html = renderAiClarification(panel, createPlan({ questions: [] }));
  assert.equal(presentation.activeQuestion, null);
  assert.doesNotMatch(html, /旧 Router 问题/);
});

test('resource pending renders one identifiable task with the existing resolution action', () => {
  const resource = {
    id: 'resource:v1|model_resource|surfacedefectdetection#1|modelpath',
    canonicalId: 'resource:v1|model_resource|surfacedefectdetection#1|modelpath',
    resourceType: 'model_resource',
    resourceName: '缺陷检测模型',
    operatorKey: 'SurfaceDefectDetection#1',
    parameterName: 'ModelPath',
    blockingScope: 'build',
    resolutionTarget: 'picker:model',
    kind: 'resource',
    category: 'resource_pending',
    blocksBuild: true,
    interactive: true,
  };
  const panel = createPanel({ queue: [], batch: [], missingResources: [resource] });
  const html = renderAiClarification(panel, createPlan({ questions: [] }));
  assert.match(html, /当前没有待确认问题/);
  assert.match(html, /待补资源 · 1 项/);
  assert.doesNotMatch(html, /还需确认 1 项/);
  assert.match(html, /缺陷检测模型/);
  assert.match(html, /SurfaceDefectDetection#1/);
  assert.match(html, /ModelPath/);
  assert.match(html, /picker:model/);
  assert.match(html, /data-resource-action="pick_model_resource"/);
  assert.doesNotMatch(html, /type="file"/);
});

test('first planning wait renders four honest phases with cancel feedback', () => {
  const panel = createPanel();
  panel.lastPlanningRequestContext = { description: '检测连接器端子是否缺针' };
  panel._getPlanRunProgressState = () => ({
    status: 'running',
    slow: true,
    canCancel: true,
    canRetry: false,
    currentLabel: '响应较慢，系统仍在理解需求；当前阶段尚未标记完成。',
    phases: {
      understand: { status: 'running', summary: '等待 Intent Router 返回。' },
      context: { status: 'waiting', summary: '' },
      generate: { status: 'waiting', summary: '' },
      validate: { status: 'waiting', summary: '' },
    },
  });

  const html = aiPanelPlanPresentationTestApi.renderEmptyPlan(panel);
  assert.match(html, /规划进行中工作台/);
  assert.match(html, /检测连接器端子是否缺针/);
  assert.match(html, /当前工作/);
  assert.match(html, /进度依据/);
  assert.match(html, /尚未收到 Plan Run 流式事件/);
  assert.match(html, /data-planning-status="running"/);
  assert.match(html, /理解需求/);
  assert.match(html, /整理工程上下文/);
  assert.match(html, /生成方案/);
  assert.match(html, /校验方案/);
  assert.match(html, /尚未标记完成/);
  assert.match(html, /取消规划/);
  assert.doesNotMatch(html, /data-planning-phase="context"[^]*已完成/);
});

test('planning wait exposes terminal states without inventing completed phases', () => {
  for (const [status, label] of [
    ['failed', '规划失败'],
    ['timeout', '等待超时'],
    ['cancelled', '已取消'],
  ]) {
    const panel = createPanel();
    panel.lastPlanningRequestContext = { description: '检测玻璃划痕' };
    panel._getPlanRunProgressState = () => ({
      status,
      canCancel: false,
      canRetry: true,
      currentLabel: `${label}，可重试。`,
      eventCount: 0,
      phases: {
        understand: { status: 'completed', summary: '需求理解已返回。' },
        context: { status, summary: `${label}。` },
        generate: { status: 'waiting', summary: '' },
        validate: { status: 'waiting', summary: '' },
      },
    });

    const html = aiPanelPlanPresentationTestApi.renderEmptyPlan(panel);
    assert.match(html, new RegExp(`data-planning-status="${status}"`));
    assert.match(html, new RegExp(label));
    assert.match(html, /data-ai-action="planning-retry"/);
    const generatePhase = html.match(/<li[^>]*data-planning-phase="generate"[^>]*>[\s\S]*?<\/li>/)?.[0] || '';
    const validatePhase = html.match(/<li[^>]*data-planning-phase="validate"[^>]*>[\s\S]*?<\/li>/)?.[0] || '';
    assert.match(generatePhase, /等待中/);
    assert.match(validatePhase, /等待中/);
    assert.doesNotMatch(generatePhase, /已完成/);
    assert.doesNotMatch(validatePhase, /已完成/);
  }
});

test('failed or cancelled planning wait exposes retry from the same lifecycle', () => {
  const panel = createPanel();
  panel._getPlanRunProgressState = () => ({
    status: 'failed',
    canCancel: false,
    canRetry: true,
    currentLabel: '规划失败，可重试本次需求。',
    phases: {
      understand: { status: 'completed', summary: '需求理解已返回。' },
      context: { status: 'failed', summary: '上下文服务失败。' },
      generate: { status: 'waiting', summary: '' },
      validate: { status: 'waiting', summary: '' },
    },
  });

  const html = aiPanelPlanPresentationTestApi.renderEmptyPlan(panel);
  assert.match(html, /规划失败，可重试本次需求/);
  assert.match(html, /data-ai-action="planning-retry"/);
  assert.doesNotMatch(html, /data-ai-action="planning-cancel"/);
});
