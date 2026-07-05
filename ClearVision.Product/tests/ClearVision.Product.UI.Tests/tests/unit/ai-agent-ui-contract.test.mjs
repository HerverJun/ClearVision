import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

function installDom({ search = '', localValues = {} } = {}) {
  const store = new Map(Object.entries(localValues));
  const storageApi = {
    getItem(key) {
      return store.has(key) ? store.get(key) : null;
    },
    setItem(key, value) {
      store.set(key, String(value));
    },
    removeItem(key) {
      store.delete(key);
    }
  };
  global.window = {
    chrome: null,
    location: {
      protocol: 'http:',
      hostname: 'localhost',
      port: '5000',
      href: `http://localhost:5000/${search}`,
      search
    },
    __CLEARVISION_AGENT_DEV_UI__: false,
    confirm() {
      return true;
    },
    setTimeout: global.setTimeout.bind(global),
    clearTimeout: global.clearTimeout.bind(global),
    sessionStorage: storageApi,
    localStorage: storageApi,
    requestAnimationFrame(callback) {
      return global.setTimeout(callback, 0);
    },
    cancelAnimationFrame(id) {
      global.clearTimeout(id);
    }
  };
  global.localStorage = storageApi;
  global.requestAnimationFrame = global.window.requestAnimationFrame;
  global.cancelAnimationFrame = global.window.cancelAnimationFrame;
  global.document = {
    querySelector() {
      return null;
    },
    getElementById() {
      return null;
    },
    createElement() {
      return createFakeElement();
    },
    addEventListener() {},
    body: { appendChild() {} }
  };
  global.alert = () => {};
}

class FakeClassList {
  constructor() {
    this.items = new Set();
  }

  add(...tokens) {
    tokens.filter(Boolean).forEach(token => this.items.add(token));
  }

  remove(...tokens) {
    tokens.filter(Boolean).forEach(token => this.items.delete(token));
  }

  toggle(token, force) {
    if (force === true) {
      this.items.add(token);
      return true;
    }
    if (force === false) {
      this.items.delete(token);
      return false;
    }
    if (this.items.has(token)) {
      this.items.delete(token);
      return false;
    }
    this.items.add(token);
    return true;
  }

  contains(token) {
    return this.items.has(token);
  }
}

function createFakeElement() {
  let text = '';
  let html = '';
  let className = '';
  const children = [];
  const escapeHtml = value => String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
  const hasClass = (element, classSelector) => String(element.className || '')
    .split(/\s+/)
    .includes(classSelector.replace(/^\./, ''));
  const findByClass = (element, classSelector) => {
    for (const child of element.children || []) {
      if (hasClass(child, classSelector)) {
        return child;
      }
      const nested = findByClass(child, classSelector);
      if (nested) {
        return nested;
      }
    }
    return null;
  };

  const element = {
    hidden: false,
    disabled: false,
    checked: false,
    value: '',
    get innerHTML() {
      if (!html && !text && children.length > 0) {
        return children.map(child => child.outerHTML || child.innerHTML).join('');
      }
      return html || escapeHtml(text);
    },
    set innerHTML(value) {
      html = String(value ?? '');
      text = '';
      children.splice(0, children.length);
      for (const match of html.matchAll(/class="([^"]+)"/g)) {
        const child = createFakeElement();
        child.className = match[1];
        children.push(child);
      }
    },
    get textContent() {
      return text;
    },
    set textContent(value) {
      text = String(value ?? '');
      html = '';
    },
    get outerHTML() {
      const cls = className ? ` class="${escapeHtml(className)}"` : '';
      return `<div${cls}>${this.innerHTML}</div>`;
    },
    get className() {
      return className;
    },
    set className(value) {
      className = String(value ?? '');
      this.classList = new FakeClassList();
      className.split(/\s+/).filter(Boolean).forEach(token => this.classList.add(token));
    },
    children,
    dataset: {},
    classList: new FakeClassList(),
    style: {},
    attributes: new Map(),
    setAttribute(name, value) {
      this.attributes.set(name, String(value));
    },
    removeAttribute(name) {
      this.attributes.delete(name);
    },
    getAttribute(name) {
      return this.attributes.get(name);
    },
    addEventListener() {},
    appendChild(child) {
      children.push(child);
      child.parentNode = this;
      html = '';
      text = '';
      return child;
    },
    contains(child) {
      return children.includes(child) || children.some(item => item.contains?.(child));
    },
    querySelector(selector) {
      if (String(selector || '').startsWith('.')) {
        return findByClass(this, selector);
      }
      return null;
    },
    querySelectorAll(selector) {
      const results = [];
      const visit = node => {
        for (const child of node.children || []) {
          if (String(selector || '').startsWith('.') && hasClass(child, selector)) {
            results.push(child);
          }
          visit(child);
        }
      };
      visit(this);
      return results;
    }
  };

  return element;
}

function createFakeButton() {
  const button = createFakeElement();
  button.disabled = false;
  button.click = () => {};
  return button;
}

function createContainer(elements, collections = {}) {
  return {
    querySelector(selector) {
      return elements[selector] ?? null;
    },
    querySelectorAll(selector) {
      return collections[selector] ?? [];
    }
  };
}

async function loadAiPanel() {
  installDom();
  return import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js');
}

async function loadPropertyPanel() {
  installDom();
  return import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');
}

async function loadParameterRules() {
  installDom();
  return import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/parameterDependencyRules.js');
}

function getRepoRoot() {
  const currentFile = fileURLToPath(import.meta.url);
  return path.resolve(path.dirname(currentFile), '..', '..', '..', '..', '..');
}

function getTrackedRepoFiles() {
  return execFileSync('git', ['ls-files', '-z'], {
    cwd: getRepoRoot(),
    encoding: 'utf8'
  })
    .split('\0')
    .filter(Boolean)
    .map(file => file.replaceAll('\\', '/'));
}

function loadParameterRuleParitySpec() {
  const specPath = path.resolve(
    getRepoRoot(),
    'quality',
    'evals',
    'specs',
    'vision_agent_parameter_rule_parity_cases.json'
  );
  return JSON.parse(fs.readFileSync(specPath, 'utf8'));
}

function assertWorkflowRunMetadata(workflowRun) {
  assert.ok(workflowRun);
  for (const field of ['commitSha', 'branchName', 'runId', 'runAttempt', 'generatedAtUtc']) {
    assert.equal(typeof workflowRun[field], 'string', field);
    assert.ok(workflowRun[field].length > 0, field);
  }
}

function createPanel(AiPanel, overrides = {}) {
  const panel = Object.create(AiPanel.prototype);
  panel.options = overrides.options || {};
  panel.isVisionAgentDeveloperUiEnabled = overrides.developer === true;
  panel.useVisionAgentGenerateFlow = overrides.enabled === true;
  panel.agentGenerateFlowMode = overrides.mode || 'scripted';
  panel.runtimePreviewConsent = overrides.runtimePreviewConsent === true;
  panel.directBuildDebugNextRequest = overrides.directBuildDebugNextRequest === true;
  panel.pendingParameterDrafts = {};
  panel.pendingResourceDrafts = {};
  panel.pendingOperatorBindings = {};
  panel.operatorMetadataCache = new Map();
  panel.operatorMetadataLoading = new Map();
  panel.cameraBindingsCache = [];
  panel.cameraBindingsLoadingPromise = null;
  panel.currentResultVersion = 1;
  panel.appliedResultVersion = 0;
  panel.currentCanvasRevision = 0;
  panel.appliedCanvasRevision = 0;
  panel.appliedCanvasBaselineFlow = null;
  panel.canvasManualEditRecords = [];
  panel.canvasManualEditSignature = '';
  panel.sessionId = 'agent-ui-contract';
  panel.sessionStorageKey = 'cv_ai_session_id';
  panel.sessionNavigationEpoch = 0;
  panel.pendingSessionLoad = null;
  panel.autoRestoreAttempted = false;
  panel.autoRestoreNoticeShown = false;
  panel.currentResult = overrides.currentResult || null;
  panel.isGenerating = false;
  panel.flowCanvas = null;
  panel.requirementMode = 'strict';
  panel.workbenchState = 'idle';
  panel._lastActiveWorkbenchState = 'idle';
  panel._workbenchStageTimeline = [];
  panel.nextHintDraft = '';
  panel.nextTemplateSelection = null;
  panel.agentWorkspaceMode = 'plan';
  panel.workspaceViewMode = 'plan';
  panel.workspaceSnapshotRevision = 0;
  panel.workspaceSnapshotDirty = false;
  panel.workspaceSnapshotSaveQueue = Promise.resolve();
  panel.workspaceMutationGeneration = 0;
  panel.workspacePersistedGeneration = 0;
  panel.workspacePendingMutationCount = 0;
  panel.workspaceSaveErrorGeneration = 0;
  panel.workspaceBoundaryInProgress = false;
  panel.workspaceBuildRunId = '';
  panel.workspaceSubmittedBuildFingerprint = '';
  panel.workspacePersistenceWarning = null;
  panel._workspacePersistenceStatusNoteActive = false;
  panel._workspacePersistenceStatusNoteText = '';
  panel.pendingVisionPlan = null;
  panel.planQuestionSelections = {};
  panel.planQuestionAnswers = {};
  panel.planAnswerRevision = 0;
  panel.planAcceptedRecommendedDefaults = false;
  panel.planRequirementModes = new Map();
  panel.currentPlanIdentity = '';
  panel.effectiveReadiness = null;
  panel.previewState = 'idle';
  panel.activePlanReadinessPreviewController = null;
  panel.activePlanReadinessPreviewRequest = null;
  panel.lastPlanReadinessPreviewError = '';
  panel.activeGenerateRequestId = null;
  panel.activeIntentRouterRequestId = null;
  panel.activePlanRequestId = null;
  panel.activePlanRunId = null;
  panel.activePlanRunRequestId = null;
  panel.activePlanRunEvents = [];
  panel.activePlanRunEventKeys = new Set();
  panel.activePlanRunCompletion = null;
  panel.activeGenerateSessionId = null;
  panel.activeAgentRunId = null;
  panel.activeAgentRunEventSource = null;
  panel.activeAgentRunTransport = null;
  panel.activeAgentRunEvents = [];
  panel.activeAgentRunEventKeys = new Set();
  panel.agentRunStepMap = new Map();
  panel.agentRunToolMap = new Map();
  panel.agentRunArtifactMap = new Map();
  panel.publicLiveEventKeys = new Set();
  panel.publicLiveEvents = [];
  panel.publicLiveStatusTimer = null;
  panel.activeAssistantTurn = null;
  panel.lastUserPrompt = '';
  panel.isCancellingGenerate = false;
  panel._streamBuffer = { thinking: '', content: '' };
  panel._streamFlushPending = false;
  panel._scrollToBottom = () => {};
  panel._updateScrollBottomBtn = () => {};
  panel._setResultStatusNote = (text = '', tone = '') => {
    panel.lastResultStatusNote = { text, tone };
  };
  panel._setGeneratingState = busy => {
    panel.isGenerating = busy;
  };
  const realSetWorkbenchState = AiPanel.prototype._setWorkbenchState;
  panel._setWorkbenchState = state => {
    panel.lastWorkbenchState = state;
    return realSetWorkbenchState.call(panel, state);
  };
  panel._clearActiveRequestState = () => {
    panel.activeGenerateRequestId = null;
    panel.activeGenerateSessionId = null;
  };
  panel._renderQueuedHintBanner = () => {
    panel.queuedHintRendered = true;
  };
  panel._addMessage = (role, text) => {
    panel.messages = panel.messages || [];
    panel.messages.push({ role, text });
  };
  panel._addToHistory = item => {
    panel.historyItems = panel.historyItems || [];
    panel.historyItems.push(item);
  };
  panel._requestBackendIntentRouterRun = overrides.intentRouterRun || (async () => ({
    intent: 'actionable_vision_plan',
    confidence: 'high',
    shouldOpenPlan: true,
    shouldBuildDirectly: false,
    canBuild: true,
    needsClarification: false,
    publicReason: '已识别为可规划的视觉需求。',
    assistantReply: '我先帮你整理规划方案。',
    clarificationQuestions: [],
    fallbackAllowed: true,
    routerSource: 'test_router',
    metadataOnly: true
  }));
  panel._buildTestPlanReadinessPreview = request => {
    const plan = panel.pendingVisionPlan;
    const acceptedRecommended = request.acceptedRecommendedDefaults === true;
    let readiness = overrides.previewReadiness || null;
    if (!readiness && plan && panel._isUsableAuthoritativeReadiness(plan.authoritativeBuildReadiness)) {
      readiness = panel._applyAnswersToAuthoritativeReadiness(
        plan.authoritativeBuildReadiness,
        plan.questions,
        panel.planQuestionAnswers || {},
        { acceptedRecommended }
      );
    }
    if (!readiness && plan) {
      readiness = panel._buildLegacyPlanReadinessSnapshot({
        plan,
        rawCanBuild: plan.rawPlanSnapshot?.canBuild ?? plan.rawPlanSnapshot?.CanBuild ?? plan.executable,
        requirementMaturity: plan.requirementMaturity,
        semanticExtraction: plan.semanticExtraction,
        route: plan.route || plan.recommendedRoute || plan.RecommendedRoute,
        blockingReasons: plan.blockingReasons,
        questions: plan.questions,
        acceptedRecommended,
        requirementMode: request.requirementMode || panel.requirementMode || 'strict'
      });
    }
    readiness = readiness ||
      request.planSnapshot?.buildReadiness ||
      request.planSnapshot?.BuildReadiness ||
      {
        canBuild: false,
        blockers: [],
        resolvedFields: [],
        remainingFields: ['inspection_object'],
        primaryMessage: '仍需确认基础需求。',
        contractVersion: 'v2'
      };
    const acceptedAnswers = request.confirmedAnswers || [];
    const acceptedKeys = new Set(acceptedAnswers.flatMap(answer => [
      String(answer.questionId || answer.QuestionId || '').trim(),
      String(answer.field || answer.Field || '').trim()
    ]).filter(Boolean));
    if (acceptedKeys.size > 0) {
      const nextBlockers = (readiness.blockers || readiness.Blockers || []).filter(blocker => {
        if (String(blocker.category || blocker.Category || '').toLowerCase() === 'safety_blocker') {
          return true;
        }
        const keys = [
          blocker.questionId || blocker.QuestionId || '',
          blocker.field || blocker.Field || ''
        ].map(value => String(value || '').trim()).filter(Boolean);
        return !keys.some(key => acceptedKeys.has(key));
      });
      const nextRemaining = (readiness.remainingFields || readiness.RemainingFields || [])
        .filter(field => !acceptedKeys.has(String(field || '').trim()));
      const blockingLeft = nextBlockers.some(blocker => (blocker.blocksBuild ?? blocker.BlocksBuild) === true);
      readiness = {
        ...readiness,
        canBuild: !blockingLeft && nextRemaining.length === 0,
        blockers: nextBlockers,
        remainingFields: nextRemaining,
        resolvedFields: [
          ...(readiness.resolvedFields || readiness.ResolvedFields || []),
          ...acceptedAnswers.map(answer => answer.field || answer.Field).filter(Boolean)
        ],
        primaryMessage: !blockingLeft && nextRemaining.length === 0
          ? '规划已完成，可以开始构建。'
          : (readiness.primaryMessage || readiness.PrimaryMessage || '')
      };
    }
    const blockers = readiness.blockers || readiness.Blockers || [];
    const resourcePendingCount = blockers.filter(blocker =>
      String(blocker.category || blocker.Category || '').toLowerCase() === 'resource_pending').length;
    const hardBlockerCount = blockers.filter(blocker =>
      (blocker.blocksBuild ?? blocker.BlocksBuild) === true &&
      String(blocker.category || blocker.Category || '').toLowerCase() !== 'resource_pending').length;
    const remainingFields = readiness.remainingFields || readiness.RemainingFields || [];
    const deferredQuestionIds = panel._toArray(plan?.questions)
      .filter(question => {
        const selected = String((request.userSelections || {})[question.id] || '').trim();
        const option = panel._toArray(question.options)
          .find(item => String(item.value || '').trim() === selected);
        return option && panel._isDeferOption(option);
      })
      .map(question => question.id);
    return {
      planId: request.planId,
      planHash: request.planHash,
      requirementMode: request.requirementMode || 'strict',
      answerRevision: request.answerRevision || 0,
      acceptedAnswers: request.confirmedAnswers || [],
      answerSetFingerprint: `test:${request.answerRevision || 0}:${request.requirementMode || 'strict'}`,
      buildReadiness: readiness,
      deferredQuestionIds,
      pendingConfirmationCount: remainingFields.length,
      resourcePendingCount,
      hardBlockerCount,
      metadataOnly: true
    };
  };
  panel._requestBackendPlanReadinessPreview = overrides.planReadinessPreview || (request =>
    panel._buildTestPlanReadinessPreview(request));
  if (!overrides.useProductionPreview) {
    panel._requestPlanReadinessPreview = (plan = panel.pendingVisionPlan, options = {}) => {
      if (!plan) return false;
      panel._activatePlanIdentity?.(plan);
      const request = panel._buildPlanReadinessPreviewRequest(plan, options);
      panel.previewState = 'validating';
      plan.previewState = 'validating';
      plan.executable = false;
      const result = panel._requestBackendPlanReadinessPreview(request);
      if (result && typeof result.then === 'function') {
        return AiPanel.prototype._requestPlanReadinessPreview.call(panel, plan, options);
      }
      panel._applyPlanReadinessPreviewResult(plan, result);
      return true;
    };
    const realNormalizeBackendPlanResult = AiPanel.prototype._normalizeBackendPlanResult;
    panel._normalizeBackendPlanResult = (...args) => {
      const previousPlan = panel.pendingVisionPlan;
      const plan = realNormalizeBackendPlanResult.apply(panel, args);
      panel.pendingVisionPlan = plan;
      panel._requestPlanReadinessPreview(plan, { reason: 'test_normalize' });
      panel.pendingVisionPlan = previousPlan;
      return plan;
    };
  }
  return panel;
}

function createPropertyPanel(PropertyPanel, operator) {
  const panel = Object.create(PropertyPanel.prototype);
  panel.currentOperator = operator;
  panel.container = createFakeElement();
  panel.cameraBindingsCache = [];
  panel.recommendationSupportedOperators = new Set();
  panel.recommendedFieldNames = new Set();
  panel.pendingRecommendation = null;
  panel.bindEvents = () => {};
  panel.initSliders = () => {};
  panel.initRoiEditorPanel = () => {};
  panel.initPreviewPanel = () => {};
  return panel;
}

function imageAcquisitionOperator(sourceType, overrides = {}) {
  return {
    id: overrides.id || 'op_acq',
    type: 'ImageAcquisition',
    title: overrides.title || '采集',
    displayName: overrides.displayName || '采集',
    parameters: [
      { name: 'SourceType', displayName: '采集源', dataType: 'enum', value: sourceType, defaultValue: 'File' },
      { name: 'FilePath', displayName: '文件路径', dataType: 'file', value: overrides.filePath ?? '', isRequired: true },
      { name: 'CameraId', displayName: '相机绑定', dataType: 'cameraBinding', value: overrides.cameraId ?? '', isRequired: true }
    ]
  };
}

function createInput({ name, value = '', type = 'text', dataType = 'string' }) {
  return {
    name,
    value,
    type,
    checked: Boolean(value),
    dataset: { type: dataType },
    closest() { return null; }
  };
}

function installPropertyForm(inputs) {
  const form = {
    querySelectorAll(selector) {
      return selector.includes('input') || selector.includes('select') ? inputs : [];
    },
    querySelector() {
      return null;
    }
  };
  global.document.getElementById = id => id === 'property-form' ? form : null;
  return form;
}

function attachValidationPanel(panel) {
  const card = createFakeElement();
  const validation = createFakeElement();
  panel.container = createContainer({
    '#ai-result-validation-card': card,
    '#ai-result-validation': validation
  });
  return { card, validation };
}

function attachAgentRunTurn(panel) {
  const turn = {
    card: createFakeElement(),
    statusEl: createFakeElement(),
    liveStatusEl: createFakeElement(),
    reasoningSection: createFakeElement(),
    reasoningBody: createFakeElement(),
    replySection: createFakeElement(),
    replyBody: createFakeElement(),
    processSection: createFakeElement(),
    processBody: createFakeElement(),
    toolsSection: createFakeElement(),
    toolsBody: createFakeElement(),
    artifactsSection: createFakeElement(),
    artifactsBody: createFakeElement(),
    failureSection: createFakeElement(),
    failureBody: createFakeElement(),
    reasoningCursor: createFakeElement(),
    replyCursor: createFakeElement()
  };
  turn.card.dataset = {};
  panel.activeAssistantTurn = turn;
  return turn;
}

function collectProcessText(turn) {
  return (turn?.processBody?.children || [])
    .map(item => item.querySelector?.('.ai-agent-run-step-copy')?.textContent || item.textContent || item.innerHTML || '')
    .join('\n');
}

function encodeSseEvent(event) {
  return `id: ${event.sequence}\nevent: ${event.eventType}\ndata: ${JSON.stringify(event)}\n\n`;
}

function flushAsync(ticks = 2) {
  let chain = Promise.resolve();
  for (let i = 0; i < ticks; i += 1) {
    chain = chain.then(() => new Promise(resolve => setTimeout(resolve, 0)));
  }
  return chain;
}

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((resolveValue, rejectValue) => {
    resolve = resolveValue;
    reject = rejectValue;
  });
  return { promise, resolve, reject };
}

function backendPlanResult(overrides = {}) {
  const templateSelection = overrides.templateSelection ?? {
    mode: 'template_adapt',
    templateId: 'tmpl-plan',
    scenarioKey: 'scratch'
  };
  const canBuild = overrides.canBuild === true;
  const canPlan = overrides.canPlan ?? canBuild;
  return {
    planId: overrides.planId || 'plan_backend_1',
    planHash: overrides.planHash || 'sha256:backend-plan-hash',
    originalUserPrompt: overrides.originalUserPrompt || overrides.goal || 'detect metal scratches',
    goal: overrides.goal || 'detect metal scratches',
    intent: overrides.intent || 'surface_defect',
    confidence: overrides.confidence || 'high',
    requirementMode: overrides.requirementMode || 'strict',
    planSource: overrides.planSource || 'rule_fallback',
    fallbackReason: overrides.fallbackReason || 'planner_failed',
    plannerFailureStage: overrides.plannerFailureStage || '',
    plannerFailureCode: overrides.plannerFailureCode || '',
    sanitizedErrorKind: overrides.sanitizedErrorKind || '',
    sanitizedErrorMessage: overrides.sanitizedErrorMessage || '',
    requirementUnderstanding: ['Inspection intent: surface defect inspection.'],
    recommendedRoute: overrides.recommendedRoute ?? {
      routeId: 'surface_defect_detection',
      title: 'Surface defect inspection route',
      summary: 'Detect visible scratches and blobs.',
      operators: ['ImageAcquisition', 'SurfaceDefectDetection', 'ResultOutput'],
      templateDecision: 'Use selected template first.'
    },
    clarificationQuestions: overrides.clarificationQuestions ?? [
      {
        id: 'defect_definition',
        title: 'What should count as a defect?',
        why: 'Thresholds depend on defect definition.',
        defaultValue: 'scratch_or_blob',
        defaultAssumption: 'Detect visible scratches and blobs.',
        impact: 'Thresholds need sample confirmation.',
        options: [
          {
            value: 'scratch_or_blob',
            label: 'Scratch/blob',
            recommended: true,
            description: 'Use general surface defect candidates.',
            impact: 'Good first draft.'
          },
          {
            value: 'crack',
            label: 'Crack',
            recommended: false,
            description: 'Emphasize thin dark/bright crack-like defects.',
            impact: 'Needs contrast assumptions.'
          },
          {
            value: 'dent_or_stain',
            label: 'Dent/stain',
            recommended: false,
            description: 'Look for dents, stain, or discoloration.',
            impact: 'Needs lighting/sample confirmation.'
          }
        ]
      }
    ],
    recommendedDefaults: [
      {
        id: 'resource_policy',
        label: 'Missing resources stay pending',
        value: 'pending_parameters',
        impact: 'No resource path is guessed.'
      }
    ],
    risks: ['Thresholds need representative images.'],
    acceptanceCriteria: ['Workflow draft contains acquisition, inspection, judgment, and output stages.'],
    executablePlan: ['Map parameters and run readiness checks.'],
    canPlan,
    canBuild,
    buildReadiness: overrides.buildReadiness,
    blockingReasons: overrides.blockingReasons ?? [],
    nextAction: 'Accept recommended defaults, then start Build.',
    requirementMaturity: overrides.requirementMaturity ?? {
      maturity: canBuild ? 'actionable' : 'ambiguous',
      taskType: canBuild ? 'surface_or_pose_defect' : 'unknown',
      canPlan,
      canBuild,
      objectSignals: canBuild ? ['metal', 'surface'] : [],
      taskSignals: canBuild ? ['scratch'] : [],
      missingFields: canBuild ? ['image_source', 'acceptance_criteria'] : ['inspection_object', 'task_type'],
      blockingReasons: canBuild ? [] : ['inspection_object_missing', 'task_type_missing'],
      publicReason: canBuild
        ? '需求已明确到可规划视觉流程。'
        : '需求仍缺少检测对象或任务类型，暂不能构建。'
    },
    decisionTrace: overrides.decisionTrace,
    semanticExtraction: overrides.semanticExtraction,
    publicEvents: overrides.publicEvents ?? [
      {
        stage: 'collecting_context',
        status: 'completed',
        title: 'Context collected',
        summary: 'collecting_context completed',
        metadataOnly: true
      },
      {
        stage: 'planning_with_model',
        status: 'started',
        title: 'Planning with model',
        summary: 'planning_with_model started',
        metadataOnly: true
      },
      {
        stage: 'rule_fallback_used',
        status: 'completed',
        title: 'Rule fallback used',
        summary: 'rule_fallback_used completed',
        metadataOnly: true
      },
      {
        stage: 'plan_ready',
        status: 'completed',
        title: 'Plan ready',
        summary: 'plan_ready completed',
        metadataOnly: true
      }
    ],
    contextSummary: {
      hasCurrentFlow: false,
      hasCurrentResult: false,
      attachmentCount: 0,
      templateSelectionMode: templateSelection?.mode || '',
      templateId: templateSelection?.templateId || '',
      contextKinds: ['user_requirement', 'operator_catalog'],
      operatorCatalogTools: ['validate_flow']
    },
    operatorCatalogVersion: 'catalog.v1',
    templateCatalogVersion: 'template.v1',
    templateSelection,
    stationBoundarySummary: 'metadata-only station boundary',
    plcOutputPolicy: 'local result first',
    metadataOnly: true
  };
}

function planRunCompletedEvent({
  runId,
  sequence,
  planResult,
  eventType = 'run.completed',
  revision = 9,
  persistenceStatus = { primaryStoreSaved: true, recoveryBackupSaved: true },
  persistenceWarning = null,
  publicMessage = '',
  diagnostic = null
} = {}) {
  const status = eventType === 'run.failed'
    ? 'failed'
    : eventType === 'run.cancelled'
      ? 'cancelled'
      : 'completed';
  const defaultSummary = eventType === 'run.failed'
    ? '规划在完成前失败。'
    : eventType === 'run.cancelled'
      ? '规划已取消。'
      : 'Plan run completed.';
  const payload = {
    ...(planResult ? { planResult } : {}),
    workspaceSnapshot: {
      revision,
      lifecycleState: status === 'completed' ? 'plan_ready' : `plan_${status}`,
      planRunId: runId,
      planRunStatus: status
    },
    persistenceStatus,
    persistenceWarning,
    publicMessage: publicMessage || defaultSummary,
    metadataOnly: true
  };
  if (diagnostic && typeof diagnostic === 'object') {
    payload.diagnostic = diagnostic;
  }
  return {
    runId,
    sequence,
    eventType,
    stage: 'run',
    title: eventType === 'run.failed' ? 'Run failed' : eventType === 'run.cancelled' ? 'Run cancelled' : 'Run completed',
    summary: publicMessage || defaultSummary,
    status,
    payload,
    metadataOnly: true,
    redactionPass: true
  };
}

async function waitFor(predicate, message = 'condition', attempts = 20) {
  for (let i = 0; i < attempts; i += 1) {
    if (predicate()) {
      return;
    }
    await flushAsync(1);
  }

  assert.ok(predicate(), message);
}

function installFetchStream(responses, requests = []) {
  global.fetch = async (url, options = {}) => {
    requests.push({ url: String(url), options });
    const next = responses.shift();
    if (!next) {
      throw new Error('Unexpected fetch call');
    }

    if (next.error) {
      throw next.error;
    }

    const status = next.status ?? 200;
    const ok = next.ok ?? (status >= 200 && status < 300);
    const headers = new Map(Object.entries(next.headers || { 'content-type': next.body ? 'text/event-stream' : 'application/json' }));
    return {
      ok,
      status,
      headers: {
        get(name) {
          return headers.get(String(name || '').toLowerCase()) || headers.get(name) || null;
        }
      },
      async text() {
        return typeof next.body === 'string' ? next.body : JSON.stringify(next.json ?? {});
      },
      async json() {
        return next.json ?? JSON.parse(String(next.body || '{}'));
      },
      body: next.body
        ? {
            getReader() {
              let consumed = false;
              return {
                async read() {
                  if (consumed) {
                    return { done: true };
                  }
                  consumed = true;
                  return { done: false, value: new TextEncoder().encode(next.body) };
                },
                async cancel() {
                  consumed = true;
                }
              };
            }
          }
        : null
    };
  };
  return requests;
}

function agentResponse() {
  return {
    flow: {
      operators: [
        {
          tempId: 'op_detect',
          operatorType: 'DeepLearning',
          displayName: 'Detector',
          parameters: { ModelPath: '<pending-model-path>' }
        }
      ],
      connections: []
    },
    pendingParameters: [],
    missingResources: [
      {
        resourceType: 'model_path',
        resourceKey: 'op_detect.ModelPath',
        description: 'DeepLearning model path is pending.'
      }
    ],
    pendingActions: [
      {
        actionType: 'ProvideModelPath',
        resourceKey: 'op_detect.ModelPath',
        summary: 'Provide model path before deployment.'
      }
    ],
    validationPreview: {
      structuralValidation: {
        blockingIssues: [],
        warnings: [{ message: 'ModelPath is pending engineer input.' }],
        missingResources: [{ resourceKey: 'op_detect.ModelPath' }]
      },
      dryRun: {
        executedOperators: ['op_detect'],
        skippedOperators: [],
        warnings: ['DeepLearning uses simulated status.'],
        summary: 'Stub dryrun completed.'
      },
      deploymentPrecheck: {
        readyForDeployment: false,
        workflowDraftAllowed: true,
        deploymentBlocked: true,
        deployed: false,
        packageCreated: false,
        stationTouched: false,
        precheckSummary: 'Deployment blocked by missing resources.'
      },
      runtimePreview: {
        previewReady: true,
        adapterName: 'offline_runtime_preview',
        previewMode: 'offline_fixture',
        warnings: [{ code: 'offline_replay_metadata_only', message: 'Offline metadata only.' }],
        blockingIssues: [],
        artifacts: [
          {
            artifactId: 'operator-result-1',
            artifactType: 'operator_result_metadata',
            metadataOnly: true,
            binaryIncluded: false,
            byteLength: 0
          }
        ]
      }
    },
    toolTrace: [
      {
        toolName: 'validate_flow',
        permission: 'Simulation',
        adapterName: '',
        success: true,
        durationMs: 12,
        errorCode: ''
      },
      {
        toolName: 'replay_flow_with_frame',
        permission: 'RuntimePreview',
        adapterName: 'offline_runtime_preview',
        success: true,
        durationMs: 7,
        errorCode: ''
      }
    ]
  };
}

function resourceBindingResponse() {
  return {
    flow: {
      operators: [
        {
          tempId: 'op_acq',
          operatorType: 'ImageAcquisition',
          displayName: '采集',
          parameters: { SourceType: 'Camera', CameraId: '<pending-camera-binding>' }
        },
        {
          tempId: 'op_detect',
          operatorType: 'DeepLearning',
          displayName: '缺陷检测',
          parameters: { ModelPath: '<pending-model-resource>' }
        },
        {
          tempId: 'op_match',
          operatorType: 'TemplateMatching',
          displayName: '模板定位',
          parameters: { SimilarityThreshold: 0.8 }
        },
        {
          tempId: 'op_calibration',
          operatorType: 'UnitConvert',
          displayName: '标定换算',
          parameters: { Scale: '<pending-pixel-to-world-scale>' }
        },
        {
          tempId: 'op_output',
          operatorType: 'ResultOutput',
          displayName: '输出',
          parameters: {}
        }
      ],
      connections: [],
      metadataOnly: true
    },
    pendingParameters: [
      { operatorId: 'op_detect', actualOperatorId: 'op_detect', parameterNames: ['ModelPath'] },
      { operatorId: 'op_acq', actualOperatorId: 'op_acq', parameterNames: ['CameraId'] },
      { operatorId: 'op_calibration', actualOperatorId: 'op_calibration', parameterNames: ['Scale'] }
    ],
    missingResources: [
      { resourceType: 'model_resource', resourceKey: 'op_detect.ModelPath', operatorId: 'op_detect', parameterName: 'ModelPath', description: '部署前绑定模型资源元数据。' },
      { resourceType: 'template_artifact', resourceKey: 'op_match.Template', operatorId: 'op_match', parameterName: 'Template', description: '部署前选择模板资源。' },
      { resourceType: 'measurement_parameter', resourceKey: 'op_calibration.Scale', operatorId: 'op_calibration', parameterName: 'Scale', description: '部署前填写像素比例。' },
      { resourceType: 'camera_binding', resourceKey: 'op_acq.CameraId', operatorId: 'op_acq', parameterName: 'CameraId', description: '部署前选择相机绑定。' },
      { resourceType: 'output_channel', resourceKey: 'op_output.OutputChannel', operatorId: 'op_output', parameterName: 'OutputChannel', description: '部署前设置输出通道。' },
      { resourceType: 'plc_address', resourceKey: 'op_output.PlcAddress', operatorId: 'op_output', parameterName: 'PlcAddress', description: 'PLC 地址仅记录 metadata，不写入 PLC。' }
    ],
    applyGate: {
      canvasApplyReady: true,
      runtimeDraftReady: true,
      deploymentReady: false,
      blocked: false,
      status: 'canvas_apply_ready',
      deploymentBlockers: [
        'op_detect.ModelPath',
        'op_match.Template',
        'op_calibration.Scale',
        'op_acq.CameraId',
        'op_output.OutputChannel',
        'op_output.PlcAddress'
      ],
      firstFixRecommendation: '先绑定模型资源。',
      metadataOnly: true
    },
    validationPreview: {
      deploymentPrecheck: {
        readyForDeployment: false,
        workflowDraftAllowed: true,
        deploymentBlocked: true,
        stationTouched: false
      }
    },
    metadataOnly: true,
    firstFixRecommendation: '先绑定模型资源。'
  };
}

function resourceBindingOperatorMetadata() {
  return [
    {
      type: 'ImageAcquisition',
      parameters: [
        { name: 'SourceType', dataType: 'enum' },
        { name: 'CameraId', dataType: 'text', displayName: '相机绑定' }
      ]
    },
    {
      type: 'DeepLearning',
      parameters: [
        { name: 'ModelPath', dataType: 'text', displayName: '模型资源' }
      ]
    },
    {
      type: 'TemplateMatching',
      parameters: [
        { name: 'SimilarityThreshold', dataType: 'number', displayName: '相似度阈值' }
      ]
    },
    {
      type: 'UnitConvert',
      parameters: [
        { name: 'Scale', dataType: 'number', displayName: '像素比例' }
      ]
    },
    {
      type: 'ResultOutput',
      parameters: [
        { name: 'OutputChannel', dataType: 'text', displayName: '输出通道' },
        { name: 'PlcAddress', dataType: 'text', displayName: 'PLC 地址' }
      ]
    }
  ];
}

function cloneJson(value) {
  return JSON.parse(JSON.stringify(value));
}

function buildResultContractPayload(overrides = {}) {
  const pascal = overrides.pascal === true;
  const sourceType = overrides.sourceType || 'File';
  const preserveExistingNode = Array.isArray(overrides.preservedNodes) && overrides.preservedNodes.includes('existing_node');
  const workflowDraft = {
    operators: [
      {
        tempId: 'op_acq',
        operatorType: 'ImageAcquisition',
        displayName: 'Acquire image',
        parameters: { SourceType: 'Camera' }
      },
      {
        tempId: 'op_detect',
        operatorType: 'SurfaceDefectDetection',
        displayName: 'Detect scratch',
        parameters: { ModelId: '<pending-model-resource>' }
      },
      {
        tempId: 'op_judge',
        operatorType: 'ResultJudgment',
        displayName: 'Judge result',
        parameters: { Rule: 'NG when scratch candidate exceeds pending threshold.' }
      },
      {
        tempId: 'op_output',
        operatorType: 'ResultOutput',
        displayName: 'Output result',
        parameters: { Channel: '<pending-output-channel>' }
      }
    ],
    connections: [
      { sourceTempId: 'op_acq', sourcePortName: 'Image', targetTempId: 'op_detect', targetPortName: 'Image' },
      { sourceTempId: 'op_detect', sourcePortName: 'Result', targetTempId: 'op_judge', targetPortName: 'Image' },
      { sourceTempId: 'op_judge', sourcePortName: 'Result', targetTempId: 'op_output', targetPortName: 'Input' }
    ],
    metadataOnly: true
  };
  const canonicalFlow = {
    id: 'flow_build_contract',
    name: 'Vision Agent build contract flow',
    operators: [
      ...(preserveExistingNode ? [{
        id: 'existing_node',
        type: 'ImageAcquisition',
        name: 'existing_node',
        inputPorts: [],
        outputPorts: [{ id: 'existing_node_out_0', name: 'Image', dataType: 'Image', direction: 1 }],
        parameters: [{ name: 'SourceType', displayName: '采集源', dataType: 'string', value: 'Camera', defaultValue: 'Camera', isRequired: true }],
        isEnabled: true
      }] : []),
      {
        id: 'op_acq',
        type: 'ImageAcquisition',
        name: 'op_acq',
        inputPorts: [],
        outputPorts: [{ id: 'op_acq_out_image', name: 'Image', dataType: 'Image', direction: 1 }],
        parameters: [{ name: 'SourceType', displayName: '采集源', dataType: 'enum', value: sourceType, defaultValue: 'File', isRequired: true }],
        isEnabled: true
      },
      {
        id: 'op_detect',
        type: 'SurfaceDefectDetection',
        name: 'op_detect',
        inputPorts: [{ id: 'op_detect_in_image', name: 'Image', dataType: 'Image', direction: 0, isRequired: true }],
        outputPorts: [{ id: 'op_detect_out_result', name: 'Result', dataType: 'Object', direction: 1 }],
        parameters: [{ name: 'ModelId', displayName: '模型资源', dataType: 'string', value: '<pending-model-resource>', defaultValue: '', isRequired: true }],
        isEnabled: true
      },
      {
        id: 'op_judge',
        type: 'ResultJudgment',
        name: 'op_judge',
        inputPorts: [{ id: 'op_judge_in_value', name: 'Value', dataType: 'Object', direction: 0, isRequired: true }],
        outputPorts: [{ id: 'op_judge_out_result', name: 'JudgmentResult', dataType: 'Boolean', direction: 1 }],
        parameters: [{ name: 'Rule', displayName: '判定规则', dataType: 'string', value: 'NG when scratch candidate exceeds pending threshold.', defaultValue: '', isRequired: true }],
        isEnabled: true
      },
      {
        id: 'op_output',
        type: 'ResultOutput',
        name: 'op_output',
        inputPorts: [{ id: 'op_output_in_result', name: 'Result', dataType: 'Boolean', direction: 0, isRequired: true }],
        outputPorts: [],
        parameters: [{ name: 'Channel', displayName: '输出通道', dataType: 'string', value: '<pending-output-channel>', defaultValue: '', isRequired: true }],
        isEnabled: true
      }
    ],
    connections: [
      { id: 'conn_acq_detect', sourceOperatorId: 'op_acq', sourcePortId: 'op_acq_out_image', targetOperatorId: 'op_detect', targetPortId: 'op_detect_in_image' },
      { id: 'conn_detect_judge', sourceOperatorId: 'op_detect', sourcePortId: 'op_detect_out_result', targetOperatorId: 'op_judge', targetPortId: 'op_judge_in_value' },
      { id: 'conn_judge_output', sourceOperatorId: 'op_judge', sourcePortId: 'op_judge_out_result', targetOperatorId: 'op_output', targetPortId: 'op_output_in_result' }
    ],
    metadataOnly: true
  };
  const evidence = [
    'plan_generation',
    'template_strategy',
    'operator_pipeline',
    'parameter_mapping',
    'workflow_draft',
    'validate_schema',
    'metadata_dry_run',
    'package_readiness',
    'workflow_diff',
    'apply_gate'
  ].map((stage, index) => ({
    stage,
    toolName: `${stage}_tool`,
    status: 'completed',
    warningCode: index === 5 ? 'deployment_resource_pending' : '',
    durationMs: 10 + index,
    outputSummary: `${stage} completed with metadata-only public evidence.`,
    metadataOnly: true,
    redactionPass: true
  }));
  const buildResult = {
    buildId: 'build-contract-1',
    planId: 'plan-contract-1',
    planHash: 'sha256:contract',
    buildIntent: overrides.buildIntent || 'modify',
    selectionSource: overrides.selectionSource || 'accepted_recommended',
    effectiveRouteId: overrides.effectiveRouteId || 'attribute_classification_deep_learning',
    effectiveOperators: overrides.effectiveOperators || ['ImageAcquisition', 'DeepLearning', 'ResultJudgment', 'ResultOutput'],
    strategyConfirmed: overrides.strategyConfirmed ?? true,
    strategyConfirmationSource: overrides.strategyConfirmationSource || 'accepted_recommended',
    unresolvedStrategyBlockers: overrides.unresolvedStrategyBlockers || [],
    parameterStrategy: overrides.parameterStrategy || 'deep_learning_classification',
    flow: canonicalFlow,
    workflowDraft,
    operatorPipeline: [
      { tempId: 'op_acq', operatorType: 'ImageAcquisition', source: 'template_skeleton', status: 'selected', repairNote: '' },
      { tempId: 'op_detect', operatorType: 'SurfaceDefectDetection', source: 'plan_route', status: 'selected', repairNote: 'invalid operator was repaired' },
      { tempId: 'op_judge', operatorType: 'ResultJudgment', source: 'catalog_required', status: 'selected', repairNote: '' },
      { tempId: 'op_output', operatorType: 'ResultOutput', source: 'catalog_required', status: 'selected', repairNote: '' }
    ],
    parameterMapping: [
      { name: 'SourceType', displayName: '采集源', dataType: 'enum', value: sourceType, defaultValue: 'File' },
      { tempId: 'op_detect', operatorType: 'SurfaceDefectDetection', parameterName: 'ModelId', valueSummary: '<pending-model-resource>', source: 'missing_resource', pending: true },
      { tempId: 'op_judge', operatorType: 'ResultJudgment', parameterName: 'Rule', valueSummary: 'NG when scratch candidate exceeds pending threshold.', source: 'plan_default', pending: true }
    ],
    pendingParameters: [
      { operatorId: 'op_detect', actualOperatorId: 'op_detect', parameterNames: ['ModelId'] }
    ],
    missingResources: [
      {
        resourceType: 'model_resource',
        resourceKey: 'op_detect.ModelId',
        operatorId: 'op_detect',
        parameterName: 'ModelId',
        description: 'Bind model_resource metadata before deployment.'
      }
    ],
    workflowDiff: {
      addedNodes: ['op_acq', 'op_detect', 'op_judge', 'op_output'],
      preservedNodes: overrides.preservedNodes || ['existing_node'],
      pendingParameters: ['op_detect.ModelId'],
      deploymentBlockers: ['op_detect.ModelId'],
      metadataOnly: true
    },
    applyGate: {
      canvasApplyReady: true,
      runtimeDraftReady: true,
      deploymentReady: false,
      blocked: false,
      status: 'canvas_apply_ready',
      deploymentBlockers: ['op_detect.ModelId'],
      firstFixRecommendation: 'Bind missing model_resource metadata for op_detect.ModelId before deployment.',
      metadataOnly: true
    },
    toolEvidenceTimeline: evidence,
    firstFixRecommendation: 'Bind missing model_resource metadata for op_detect.ModelId before deployment.',
    metadataOnly: true,
    rawPrompt: 'SYSTEM PROMPT C:\\factory\\model.onnx 192.168.0.2 DB1.DBX0.0 data:image/png;base64,abcd sk-secret',
    chainOfThought: 'hidden reasoning must never render',
    reasoning_content: 'do not render'
  };
  const payload = {
    runId: 'ar_build_contract',
    sessionId: 'session-build-contract',
    success: true,
    completionStatus: 'completed',
    flow: canonicalFlow,
    buildResult,
    aiExplanation: 'Build completed with public metadata.',
    metadataOnly: true
  };

  if (!pascal) {
    return payload;
  }

  return {
    RunId: payload.runId,
    SessionId: payload.sessionId,
    Success: payload.success,
    CompletionStatus: payload.completionStatus,
    Flow: canonicalFlow,
    BuildResult: {
      BuildId: buildResult.buildId,
      PlanId: buildResult.planId,
      PlanHash: buildResult.planHash,
      BuildIntent: buildResult.buildIntent,
      SelectionSource: buildResult.selectionSource,
      EffectiveRouteId: buildResult.effectiveRouteId,
      EffectiveOperators: buildResult.effectiveOperators,
      StrategyConfirmed: buildResult.strategyConfirmed,
      StrategyConfirmationSource: buildResult.strategyConfirmationSource,
      UnresolvedStrategyBlockers: buildResult.unresolvedStrategyBlockers,
      ParameterStrategy: buildResult.parameterStrategy,
      Flow: canonicalFlow,
      WorkflowDraft: workflowDraft,
      OperatorPipeline: buildResult.operatorPipeline.map(item => ({
        TempId: item.tempId,
        OperatorType: item.operatorType,
        Source: item.source,
        Status: item.status,
        RepairNote: item.repairNote
      })),
      ParameterMapping: buildResult.parameterMapping.map(item => ({
        TempId: item.tempId,
        OperatorType: item.operatorType,
        ParameterName: item.parameterName,
        ValueSummary: item.valueSummary,
        Source: item.source,
        Pending: item.pending
      })),
      PendingParameters: buildResult.pendingParameters,
      MissingResources: buildResult.missingResources,
      WorkflowDiff: {
        AddedNodes: buildResult.workflowDiff.addedNodes,
        PreservedNodes: buildResult.workflowDiff.preservedNodes,
        PendingParameters: buildResult.workflowDiff.pendingParameters,
        DeploymentBlockers: buildResult.workflowDiff.deploymentBlockers,
        MetadataOnly: true
      },
      ApplyGate: {
        CanvasApplyReady: true,
        RuntimeDraftReady: true,
        DeploymentReady: false,
        Blocked: false,
        Status: 'canvas_apply_ready',
        DeploymentBlockers: ['op_detect.ModelId'],
        FirstFixRecommendation: buildResult.firstFixRecommendation,
        MetadataOnly: true
      },
      ToolEvidenceTimeline: evidence.map(item => ({
        Stage: item.stage,
        ToolName: item.toolName,
        Status: item.status,
        WarningCode: item.warningCode,
        DurationMs: item.durationMs,
        OutputSummary: item.outputSummary,
        MetadataOnly: true,
        RedactionPass: true
      })),
      FirstFixRecommendation: buildResult.firstFixRecommendation,
      MetadataOnly: true,
      SystemPrompt: 'SYSTEM PROMPT C:\\factory\\model.onnx 192.168.0.2 DB1.DBX0.0 data:image/png;base64,abcd sk-secret'
    },
    AiExplanation: payload.aiExplanation,
    MetadataOnly: true
  };
}

function createBuildWorkspaceContainer() {
  const elements = {
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-build-event-timeline': createFakeElement(),
    '#ai-build-template-match': createFakeElement(),
    '#ai-build-operator-chain': createFakeElement(),
    '#ai-build-parameters': createFakeElement(),
    '#ai-build-checks': createFakeElement(),
    '#ai-build-final-draft': createFakeElement(),
    '#ai-btn-apply': createFakeButton(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-result-summary': createFakeElement(),
    '#ai-result-ops': createFakeElement(),
    '#ai-result-followups': createFakeElement(),
    '#ai-result-parameter-editor': createFakeElement(),
    '#ai-result-validation-card': createFakeElement(),
    '#ai-result-validation': createFakeElement(),
    '#ai-result-prompt-trace-card': createFakeElement(),
    '#ai-result-prompt-trace': createFakeElement()
  };
  return { elements, container: createContainer(elements) };
}

function createFakeFlowCanvas(initialFlow = { operators: [], connections: [] }) {
  let flow = cloneJson(initialFlow);
  let revision = 0;
  const listeners = new Set();
  const notify = reason => {
    revision += 1;
    listeners.forEach(listener => listener({ flowRevision: revision, reason }));
  };
  return {
    deserialize(nextFlow) {
      flow = cloneJson(nextFlow);
      notify('deserialize');
    },
    serialize() {
      return cloneJson(flow);
    },
    getFlowRevision() {
      return revision;
    },
    subscribeStructureState(listener) {
      listeners.add(listener);
      listener({ flowRevision: revision, reason: 'initial' });
      return () => listeners.delete(listener);
    },
    replaceFlow(nextFlow, reason = 'parameter-change') {
      flow = cloneJson(nextFlow);
      notify(reason);
    }
  };
}

function strategyConfirmationQuestion() {
  return {
    id: 'model_or_rule_strategy',
    field: 'algorithm_strategy',
    title: 'Classification strategy',
    why: 'Changes the implementation route.',
    defaultValue: 'deep_learning',
    defaultAssumption: 'Use the recommended deep learning classification route.',
    impact: 'Draft is editable; deployment waits for resources.',
    options: [
      {
        value: 'deep_learning',
        label: 'Deep learning',
        recommended: true,
        description: 'Use model classification.',
        impact: 'Model resource remains pending.'
      },
      {
        value: 'traditional_rule',
        label: 'Traditional rule',
        recommended: false,
        description: 'Use numeric rule calibration.',
        impact: 'Calibration parameters remain pending.'
      }
    ]
  };
}

function strategyConfirmationPlanResult(overrides = {}) {
  return backendPlanResult({
    canBuild: true,
    canPlan: true,
    planSource: 'model_planner',
    fallbackReason: '',
    intent: 'attribute_classification',
    goal: 'classify fruit maturity',
    originalUserPrompt: 'detect strawberries, ripe is OK, otherwise NG, source is camera',
    recommendedRoute: {
      routeId: 'attribute_classification_planner_route',
      title: 'Attribute classification route',
      summary: 'Use acquisition, classification, judgment, and output.',
      operators: ['ImageAcquisition', 'DeepLearning', 'ResultJudgment', 'ResultOutput'],
      templateDecision: 'planner_route'
    },
    clarificationQuestions: [strategyConfirmationQuestion()],
    blockingReasons: ['strategy_confirmation:model_or_rule_strategy_missing'],
    requirementMaturity: {
      maturity: 'actionable',
      taskType: 'attribute_classification',
      canPlan: true,
      canBuild: true,
      objectSignals: ['strawberry'],
      taskSignals: ['maturity'],
      missingFields: [],
      blockingReasons: [],
      publicReason: 'Requirement hard facts are ready; strategy still needs confirmation.'
    },
    semanticExtraction: {
      isVisionRequest: true,
      source: 'model',
      taskType: 'attribute_classification',
      confidence: 0.91,
      taskTypeConfidence: 0.9,
      inspectionObject: 'strawberry',
      targetAttribute: 'maturity',
      imageSource: 'camera',
      okCondition: 'ripe is OK',
      ngCondition: 'otherwise NG',
      outputTarget: 'OK/NG result',
      missingFields: []
    },
    ...overrides
  });
}

function pendingParameterReviewResult() {
  return {
    flow: {
      operators: [
        {
          id: 'op_detect',
          type: 'DeepLearning',
          displayName: '缺陷检测',
          parameters: {
            ModelPath: '<pending-model-resource>',
            Threshold: 0.8
          }
        }
      ],
      connections: [],
      metadataOnly: true
    },
    pendingParameters: [
      { operatorId: 'op_detect', actualOperatorId: 'op_detect', parameterNames: ['ModelPath'] }
    ],
    missingResources: [
      {
        resourceType: 'model_resource',
        resourceKey: 'op_detect.ModelPath',
        operatorId: 'op_detect',
        parameterName: 'ModelPath',
        description: '部署前绑定模型资源元数据。'
      }
    ],
    applyGate: {
      canvasApplyReady: true,
      runtimeDraftReady: true,
      deploymentReady: false,
      blocked: false,
      status: 'canvas_apply_ready',
      deploymentBlockers: ['op_detect.ModelPath'],
      metadataOnly: true
    },
    validationPreview: {
      deploymentPrecheck: {
        readyForDeployment: false,
        workflowDraftAllowed: true,
        deploymentBlocked: true
      }
    },
    metadataOnly: true
  };
}

function assertNoSensitiveLeak(text) {
  assert.doesNotMatch(text, /rawPrompt|systemPrompt|SystemPrompt|chainOfThought|reasoning_content/i);
  assert.doesNotMatch(text, /C:\\|D:\\|\\.onnx|192\\.168\\.|DB1\\.DBX|base64|data:image|sk-secret|token|key/i);
}

test('default UI does not render Agent GenerateFlow controls', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);

  assert.equal(panel._renderAgentDeveloperControls(), '');
});

test('default UI does not render RuntimePreview consent control', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);

  assert.doesNotMatch(panel._renderAgentDeveloperControls(), /RuntimePreview/);
});

test('default UI payload uses Agent GenerateFlow for Build Mode', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true, mode: 'planner' });

  assert.deepEqual(panel._buildAgentGenerateFlowRequestPayload(), {
    useVisionAgentGenerateFlow: true,
    agentGenerateFlowMode: 'planner'
  });
});

test('Prompt Trace debug view hides raw prompts and sensitive metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const promptTraceCard = createFakeElement();
  const promptTrace = createFakeElement();
  panel.container = createContainer({
    '#ai-result-prompt-trace-card': promptTraceCard,
    '#ai-result-prompt-trace': promptTrace
  });
  panel._promptTraceViewMode = 'debug';

  panel._renderPromptTrace({
    mode: 'agent',
    provider: 'provider-a',
    model: 'planner-model',
    baseUrl: 'https://example.invalid/v1?' + 'token=secret-token',
    capabilities: {
      headers: 'Authorization: Bearer super-secret-value',
      modelPath: 'C:\\factory\\model.onnx'
    },
    attachmentReport: {
      preview: `data:image/png;base64,${'A'.repeat(120)}`,
      station: '192.168.1.10 DB1.DBX0.0'
    },
    usedReferenceFlowSummary: 'from /home/operator/flows/ref.json',
    systemPrompt: 'raw system prompt with sk-secret-token',
    userPrompt: 'raw user prompt'
  });

  assert.equal(promptTraceCard.hidden, false);
  assert.match(promptTrace.innerHTML, /系统提示状态/);
  assert.match(promptTrace.innerHTML, /用户提示状态/);
  assert.doesNotMatch(promptTrace.innerHTML, /raw system prompt|raw user prompt|example\.invalid|super-secret-value/);
  assertNoSensitiveLeak(promptTrace.innerHTML);
});

test('developer mode payload includes useVisionAgentGenerateFlow=true', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });

  assert.equal(panel._buildAgentGenerateFlowRequestPayload().useVisionAgentGenerateFlow, true);
});

test('planner mode payload includes agentGenerateFlowMode=planner', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'planner' });

  assert.deepEqual(panel._buildAgentGenerateFlowRequestPayload(), {
    useVisionAgentGenerateFlow: true,
    agentGenerateFlowMode: 'planner'
  });
});

test('scripted mode payload includes agentGenerateFlowMode=scripted', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'scripted' });

  assert.deepEqual(panel._buildAgentGenerateFlowRequestPayload(), {
    useVisionAgentGenerateFlow: true,
    agentGenerateFlowMode: 'scripted'
  });
});

test('developer mode can select Tool Loop experimental mode', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'tool_loop' });

  const html = panel._renderAgentDeveloperControls();
  assert.match(html, /data-agent-generate-mode="tool_loop"/);
  assert.match(html, /Tool Loop 实验/);
  assert.match(html, /实验模式：LLM 会在权限门禁内自主选择工具；失败会回退稳定构建链路。/);
  assert.deepEqual(panel._buildAgentGenerateFlowRequestPayload(), {
    useVisionAgentGenerateFlow: true,
    agentGenerateFlowMode: 'tool_loop'
  });
});

test('ordinary UI still hides Tool Loop developer controls by default', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true, mode: 'tool_loop' });

  assert.equal(panel._renderAgentDeveloperControls(), '');
  assert.doesNotMatch(panel._renderAgentDeveloperControls(), /tool_loop|Tool Loop 实验/);
});

test('developer direct Build debug control is labeled as Plan skip debug only', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });

  const html = panel._renderAgentDeveloperControls();

  assert.match(html, /ai-agent-direct-build-debug/);
  assert.match(html, /ai-agent-replay-latest/);
  assert.match(html, /AgentRun/);
  assert.match(html, /直接 Build 调试/);
  assert.match(html, /跳过 Plan，仅用于调试/);
});

test('developer latest replay control invokes AgentRun replay entry', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const replayButton = createFakeElement();
  const handlers = new Map();
  replayButton.addEventListener = (type, handler) => {
    handlers.set(type, handler);
  };
  panel.container = createContainer({
    '#ai-agent-replay-latest': replayButton
  }, {
    '[data-agent-generate-mode]': []
  });
  let replayed = false;
  panel._replayLatestAgentRunPublicEvents = async () => {
    replayed = true;
    assert.equal(replayButton.disabled, true);
    return true;
  };

  panel._bindAgentDeveloperControls();
  handlers.get('click')();
  await flushAsync(3);

  assert.equal(replayed, true);
  assert.equal(replayButton.disabled, false);
});

test('developer mode renders RuntimePreview consent switch', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });

  assert.match(panel._renderAgentDeveloperControls(), /ai-agent-runtime-preview-consent/);
  assert.match(panel._renderAgentDeveloperControls(), /允许本轮 RuntimePreview/);
});

test('RuntimePreview consent payload is one request only', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    developer: true,
    enabled: true,
    mode: 'planner',
    runtimePreviewConsent: true
  });

  assert.deepEqual(panel._buildAgentGenerateFlowRequestPayload(), {
    useVisionAgentGenerateFlow: true,
    agentGenerateFlowMode: 'planner',
    runtimePreviewConsent: true
  });
  assert.equal(panel.runtimePreviewConsent, false);
  assert.deepEqual(panel._buildAgentGenerateFlowRequestPayload(), {
    useVisionAgentGenerateFlow: true,
    agentGenerateFlowMode: 'planner'
  });
});

test('AgentRun event stream is used for Agent GenerateFlow mode with fetch support', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const originalFetch = global.fetch;
  global.fetch = async () => ({ ok: true });

  assert.equal(panel._shouldUseAgentRunEventStream(), true);
  window.chrome = { webview: { postMessage: () => {} } };
  assert.equal(panel._shouldUseAgentRunEventStream(), true);
  delete window.chrome;
  delete window.EventSource;
  assert.equal(panel._shouldUseAgentRunEventStream(), true);
  panel.useVisionAgentGenerateFlow = false;
  assert.equal(panel._shouldUseAgentRunEventStream(), false);
  panel.useVisionAgentGenerateFlow = true;
  panel.isVisionAgentDeveloperUiEnabled = false;
  assert.equal(panel._shouldUseAgentRunEventStream(), true);
  panel.isVisionAgentDeveloperUiEnabled = true;
  global.fetch = undefined;
  assert.equal(panel._shouldUseAgentRunEventStream(), false);
  global.fetch = originalFetch;
});

test('AgentRun create payload is metadata-only and does not send attachment paths', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'planner' });
  panel.runtimePreviewConsent = true;

  const payload = panel._buildAgentRunCreatePayload({
    normalizedDescription: 'detect scratch',
    normalizedHint: 'safe hint',
    requestId: 'req-1',
    resolvedMode: 'modify',
    flowPayload: { operators: [] },
    attachmentPaths: ['C:\\factory\\real-image.png'],
    normalizedTemplateSelection: { mode: 'template_fill', templateId: 'tmpl-1' },
    agentGenerateFlowPayload: panel._buildAgentGenerateFlowRequestPayload()
  });

  assert.equal(payload.description, 'detect scratch');
  assert.equal(payload.additionalContext, 'safe hint');
  assert.equal(payload.mode, 'modify');
  assert.equal(payload.useVisionAgentGenerateFlow, true);
  assert.equal(payload.agentGenerateFlowMode, 'planner');
  assert.equal(payload.runtimePreviewConsent, true);
  assert.equal(payload.attachmentCount, 1);
  assert.deepEqual(payload.attachments, []);
  assert.equal(payload.existingFlowJson, '{"operators":[]}');
  assert.deepEqual(payload.templateSelection, { mode: 'template_fill', templateId: 'tmpl-1' });
});

test('ordinary send runs Intent Router before Plan planner', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const chat = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': chat,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const calls = [];
  panel._requestBackendIntentRouterRun = async request => {
    calls.push({ type: 'router', request });
    return {
      intent: 'actionable_vision_plan',
      confidence: 'high',
      shouldOpenPlan: true,
      shouldBuildDirectly: false,
      canBuild: true,
      needsClarification: false,
      publicReason: '已识别为可规划的视觉需求。',
      assistantReply: '我先帮你整理规划方案。',
      clarificationQuestions: [],
      fallbackAllowed: true,
      routerSource: 'model_router',
      metadataOnly: true
    };
  };
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = async request => {
    calls.push({ type: 'plan', request });
    return backendPlanResult({ goal: 'router then plan' });
  };

  const accepted = panel._dispatchGenerateRequest({
    description: '为我构建一个包装箱外观视觉检测流程',
    userMessage: '为我构建一个包装箱外观视觉检测流程'
  });
  const turn = panel.activeAssistantTurn;
  await flushAsync();

  assert.equal(accepted, true);
  assert.deepEqual(calls.map(item => item.type), ['router', 'plan']);
  assert.equal(calls[0].request.description, '为我构建一个包装箱外观视觉检测流程');
  assert.equal(calls[1].request.description, '为我构建一个包装箱外观视觉检测流程');
  assert.equal(panel.pendingVisionPlan.goal, 'router then plan');
  assert.equal(panel.isGenerating, false);
  assert.match(collectProcessText(turn), /正在判断请求类型/);
  assert.match(collectProcessText(turn), /已理解请求/);
  assert.doesNotMatch(collectProcessText(turn), /已识别为/);
});

test('Intent Router semanticExtraction is passed to PlanRun and rendered from Plan result', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const semanticExtraction = {
    isVisionRequest: true,
    intent: 'new_flow',
    taskType: 'attribute_classification',
    confidence: 0.92,
    taskTypeConfidence: 0.9,
    inspectionObject: 'strawberry',
    targetAttribute: 'maturity',
    imageSource: 'camera',
    okCondition: 'ripe is OK',
    ngCondition: 'otherwise NG',
    suggestedRoute: 'attribute classification OK/NG route',
    source: 'model',
    metadataOnly: true
  };
  panel._requestBackendIntentRouterRun = async () => ({
    intent: 'actionable_vision_plan',
    confidence: 'high',
    shouldOpenPlan: true,
    shouldBuildDirectly: false,
    canBuild: false,
    needsClarification: false,
    publicReason: 'semantic route ready',
    assistantReply: 'Plan first.',
    clarificationQuestions: [],
    fallbackAllowed: true,
    routerSource: 'model_router',
    semanticExtraction,
    metadataOnly: true
  });
  const planResult = backendPlanResult({
    goal: 'semantic reused plan',
    intent: 'attribute_classification',
    canPlan: true,
    canBuild: false,
    semanticExtraction,
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'attribute_classification',
      canPlan: true,
      canBuild: false,
      objectSignals: ['strawberry'],
      taskSignals: ['maturity'],
      missingFields: ['model_or_rule_strategy'],
      blockingReasons: ['model_or_rule_strategy_missing'],
      publicReason: 'semantic extraction was reused for planning'
    }
  });
  const requests = installFetchStream([
    { json: { runId: 'ar_router_semantic_reuse', events: [] } },
    {
      body: [
        encodeSseEvent({
          runId: 'ar_router_semantic_reuse',
          sequence: 3,
          eventType: 'plan.completed',
          stage: 'plan_ready',
          title: 'Plan ready',
          summary: 'Plan completed with semantic extraction.',
          status: 'completed',
          payload: { planResult, metadataOnly: true },
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent(planRunCompletedEvent({
          runId: 'ar_router_semantic_reuse',
          sequence: 4,
          planResult,
          revision: 14
        }))
      ].join('')
    }
  ]);

  panel._dispatchGenerateRequest({
    description: 'classify strawberry maturity',
    userMessage: 'classify strawberry maturity'
  });
  await waitFor(() => panel.pendingVisionPlan?.goal === 'semantic reused plan', 'semantic reused plan');

  const createRequestBody = JSON.parse(requests[0].options.body);
  assert.match(requests[0].url, /\/api\/ai\/agent-plan-runs$/);
  assert.equal(createRequestBody.semanticExtraction.source, 'model');
  assert.equal(createRequestBody.semanticExtraction.taskType, 'attribute_classification');
  assert.equal(createRequestBody.semanticExtraction.inspectionObject, 'strawberry');
  assert.equal(createRequestBody.semanticExtraction.targetAttribute, 'maturity');
  assert.equal(createRequestBody.semanticExtraction.okCondition, 'ripe is OK');
  assert.equal(createRequestBody.semanticExtraction.ngCondition, 'otherwise NG');
  assert.equal(createRequestBody.semanticExtraction.imageSource, 'camera');
  assert.equal(panel.pendingVisionPlan.semanticExtraction.taskType, 'attribute_classification');
  assert.match(planWorkspace.innerHTML, /strawberry/);
  assert.match(planWorkspace.innerHTML, /maturity/);
  assert.match(planWorkspace.innerHTML, /ripe is OK/);
  assert.match(planWorkspace.innerHTML, /otherwise NG/);
  assert.match(planWorkspace.innerHTML, /camera/);
});

test('casual Intent Router result replies without opening Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._requestBackendIntentRouterRun = async () => ({
    intent: 'casual_chat',
    confidence: 'high',
    shouldOpenPlan: false,
    shouldBuildDirectly: false,
    canBuild: false,
    needsClarification: false,
    publicReason: '这是普通寒暄，不需要进入规划。',
    assistantReply: '在的。你可以直接描述检测目标、缺陷类型、测量项或流程修改需求，我会先帮你规划方案。',
    clarificationQuestions: [],
    fallbackAllowed: true,
    routerSource: 'model_router',
    metadataOnly: true
  });
  panel._requestBackendVisionPlan = async () => {
    throw new Error('casual chat should not request Plan');
  };

  panel._dispatchGenerateRequest({ description: 'hi', userMessage: 'hi' });
  const turn = panel.activeAssistantTurn;
  await flushAsync();

  assert.equal(panel.pendingVisionPlan, null);
  assert.equal(panel.isGenerating, false);
  assert.match(turn.replyBody.textContent, /在的。你可以直接描述检测目标/);
  assert.doesNotMatch(turn.replyBody.textContent, /普通寒暄/);
  assert.doesNotMatch(turn.replyBody.textContent, /不需要进入规划/);
  assert.doesNotMatch(collectProcessText(turn), /普通寒暄/);
  assert.doesNotMatch(collectProcessText(turn), /已识别为/);
  const diagnosticNode = Array.from(turn.processBody.children)
    .find(item => item.dataset?.eventType === 'intent-router-result');
  assert.equal(diagnosticNode?.dataset?.publicReason, '这是普通寒暄，不需要进入规划。');
  assert.equal(diagnosticNode?.title, '这是普通寒暄，不需要进入规划。');
  assert.doesNotMatch(collectProcessText(turn), /规划中/);
});

test('Intent Router public diagnostics are redacted outside the main reply', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._requestBackendIntentRouterRun = async () => ({
    intent: 'casual_chat',
    confidence: 'high',
    shouldOpenPlan: false,
    shouldBuildDirectly: false,
    canBuild: false,
    needsClarification: false,
    publicReason: '这是普通寒暄，不需要进入规划。 token=abc123 baseUrl=http://10.1.2.3 C:\\factory\\image.png',
    assistantReply: '在的。你可以直接描述检测目标、缺陷类型、测量项或流程修改需求，我会先帮你规划方案。 token=abc123 192.168.1.8',
    clarificationQuestions: [],
    fallbackAllowed: true,
    routerSource: 'model_router',
    metadataOnly: true
  });

  panel._dispatchGenerateRequest({ description: 'hi', userMessage: 'hi' });
  const turn = panel.activeAssistantTurn;
  await flushAsync();

  const combined = [
    turn.replyBody.textContent,
    collectProcessText(turn),
    ...Array.from(turn.processBody.children).map(item => `${item.title || ''} ${item.dataset?.publicReason || ''}`)
  ].join('\n');
  assert.match(turn.replyBody.textContent, /在的。你可以直接描述检测目标/);
  assert.doesNotMatch(combined, /token=abc123/);
  assert.doesNotMatch(combined, /baseUrl=http/);
  assert.doesNotMatch(combined, /10\.1\.2\.3|192\.168\.1\.8/);
  assert.doesNotMatch(combined, /C:\\factory/);
});

test('help Intent Router result replies without opening Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._requestBackendIntentRouterRun = async () => ({
    intent: 'help',
    confidence: 'high',
    shouldOpenPlan: false,
    shouldBuildDirectly: false,
    canBuild: false,
    needsClarification: false,
    publicReason: '这是能力咨询，不需要进入规划。',
    assistantReply: '我可以帮你规划视觉检测流程、选择算子链、整理待确认资源，并在人工确认后生成可应用到画布的草稿。',
    clarificationQuestions: [],
    fallbackAllowed: true,
    routerSource: 'model_router',
    metadataOnly: true
  });
  panel._requestBackendVisionPlan = async () => {
    throw new Error('help should not request Plan');
  };

  panel._dispatchGenerateRequest({ description: '你好，你能做什么', userMessage: '你好，你能做什么' });
  const turn = panel.activeAssistantTurn;
  await flushAsync();

  assert.equal(panel.pendingVisionPlan, null);
  assert.equal(panel.isGenerating, false);
  assert.match(turn.replyBody.textContent, /规划视觉检测流程/);
  assert.doesNotMatch(turn.replyBody.textContent, /能力咨询/);
  assert.doesNotMatch(turn.replyBody.textContent, /不需要进入规划/);
  assert.doesNotMatch(collectProcessText(turn), /能力咨询/);
  assert.doesNotMatch(collectProcessText(turn), /已识别为/);
  assert.doesNotMatch(collectProcessText(turn), /规划中/);
});

test('ambiguous Intent Router result obeys canonical shouldOpenPlan=false without legacy clarification card', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = createFakeElement();
  const build = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': plan,
    '#ai-build-workspace': build,
    '#ai-result-status-note': createFakeElement()
  });
  panel._requestBackendIntentRouterRun = async () => ({
    intent: 'ambiguous_vision_requirement',
    confidence: 'medium',
    shouldOpenPlan: false,
    shouldBuildDirectly: false,
    canBuild: false,
    needsClarification: true,
    publicReason: 'Need more details before planning.',
    assistantReply: '请补充检测目标、输入来源和判定标准。',
    clarificationQuestions: ['legacy question must be ignored'],
    fallbackAllowed: true,
    routerSource: 'model_router',
    metadataOnly: true
  });
  panel._requestBackendVisionPlan = async () => {
    throw new Error('canonical shouldOpenPlan=false must not request Plan');
  };

  panel._dispatchGenerateRequest({ description: '包装盒', userMessage: '包装盒' });
  const turn = panel.activeAssistantTurn;
  await flushAsync();

  assert.equal(panel.pendingVisionPlan, null);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(panel.lastWorkbenchState, 'idle');
  assert.equal(panel.pendingClarificationPayload, null);
  assert.doesNotMatch(plan.innerHTML, /ai-clarification-plan-card|ClarificationPlanCard|clarification_1/);
  assert.match(plan.innerHTML, /ai-plan-empty/);
  assert.equal(build.hidden, true);
  assert.equal(panel.isGenerating, false);
  assert.equal(panel.lastResultStatusNote.text, '');
  assert.match(turn.replyBody.textContent, /请补充检测目标/);
  assert.doesNotMatch(turn.replyBody.textContent, /ClarificationPlanCard|clarification_1/);

});

test('abstract visual goal with canonical shouldOpenPlan enters requirement decomposition Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = createFakeElement();
  const build = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': plan,
    '#ai-build-workspace': build,
    '#ai-result-status-note': createFakeElement()
  });
  panel._requestBackendIntentRouterRun = async () => ({
    intent: 'actionable_vision_plan',
    confidence: 'low',
    shouldOpenPlan: true,
    shouldBuildDirectly: false,
    canBuild: false,
    needsClarification: false,
    publicReason: 'Abstract visual goal needs requirement decomposition.',
    assistantReply: '我先整理视觉工程规划。',
    fallbackAllowed: true,
    routerSource: 'model_router',
    requirementMaturity: {
      maturity: 'abstract_goal',
      taskType: 'abstract_goal',
      canPlan: false,
      canBuild: false,
      missingFields: ['inspection_object', 'task_type'],
      blockingReasons: ['abstract_goal_needs_decomposition']
    },
    metadataOnly: true
  });
  let planRequest = null;
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = async request => {
    planRequest = request;
    return backendPlanResult({
      planId: 'plan_abstract',
      goal: '完整视觉检测方案',
      canBuild: false,
      canPlan: false,
      recommendedRoute: {
        routeId: 'requirement_decomposition',
        title: '需求分解',
        summary: '先澄清视觉目标，不预选算子链。',
        operators: [],
        templateDecision: 'requirement_decomposition'
      },
      requirementMaturity: {
        maturity: 'abstract_goal',
        taskType: 'abstract_goal',
        canPlan: false,
        canBuild: false,
        missingFields: ['inspection_object', 'task_type'],
        blockingReasons: ['abstract_goal_needs_decomposition']
      }
    });
  };

  panel._dispatchGenerateRequest({ description: '做一个完整视觉检测方案', userMessage: '做一个完整视觉检测方案' });
  await flushAsync();

  assert.equal(planRequest.description, '做一个完整视觉检测方案');
  assert.equal(panel.pendingVisionPlan?.planId, 'plan_abstract');
  assert.equal(panel.pendingVisionPlan?.route?.routeId, 'requirement_decomposition');
  assert.deepEqual(panel.pendingVisionPlan?.route?.operators, []);
  assert.equal(panel.pendingVisionPlan?.executable, false);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(panel.pendingClarificationPayload, null);
  assert.doesNotMatch(plan.innerHTML, /ai-clarification-plan-card|ClarificationPlanCard|clarification_1/);
  assert.equal(build.hidden, true);
});

test('local Intent Router fallback opens Plan for explicit unknown slots and reports degraded routing', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });

  const route = panel._buildLocalIntentRouterFallback(
    '检测目标是外星人，识别内容是额头上的第三只竖眼',
    new Error('router unavailable')
  );

  assert.equal(route.intent, 'actionable_vision_plan');
  assert.equal(route.shouldOpenPlan, true);
  assert.equal(route.canPlan, true);
  assert.equal(route.canBuild, false);
  assert.equal(route.needsClarification, false);
  assert.match(route.publicReason, /模型路由不可用，当前为规则降级解析/);
  assert.equal(route.requirementMaturity.canPlan, true);
  assert.equal(route.requirementMaturity.canBuild, false);
  assert.deepEqual(route.requirementMaturity.objectSignals, ['外星人']);
  assert.deepEqual(route.requirementMaturity.taskSignals, ['额头上的第三只竖眼']);
  assert.ok(!route.requirementMaturity.missingFields.includes('inspection_object'));
  assert.ok(!route.requirementMaturity.missingFields.includes('task_type'));
});

test('Plan Mode captures vague inspection request without starting Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const input = createFakeElement();
  const chat = createFakeElement();
  const overview = createFakeElement();
  const plan = createFakeElement();
  const build = createFakeElement();
  panel.container = createContainer({
    '#ai-input': input,
    '#ai-chat-container': chat,
    '#ai-agent-workspace-overview': overview,
    '#ai-plan-workspace': plan,
    '#ai-build-workspace': build,
    '#ai-result-status-note': createFakeElement()
  });
  let capturedPlanRequest = null;
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = async request => {
    capturedPlanRequest = request;
    return backendPlanResult({
      goal: 'metal scratch inspection workflow',
      templateSelection: { mode: 'template_fill', templateId: 'tmpl-scratch', scenarioKey: 'scratch' }
    });
  };

  const accepted = panel._dispatchGenerateRequest({
    description: '帮我做一个金属表面划痕检测流程',
    userMessage: '帮我做一个金属表面划痕检测流程'
  });

  await flushAsync();

  assert.equal(accepted, true);
  assert.equal(panel.isGenerating, false);
  assert.equal(panel.activeAgentRunId, null);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(capturedPlanRequest.templateSelection, null);
  assert.equal(panel.pendingVisionPlan.planHash, 'sha256:backend-plan-hash');
  assert.deepEqual(panel.pendingVisionPlan.templateSelection, {
    mode: 'template_fill',
    templateId: 'tmpl-scratch',
    scenarioKey: 'scratch'
  });
  assert.equal(panel.pendingVisionPlan.goal, 'metal scratch inspection workflow');
  assert.match(overview.innerHTML, /高/);
  assert.match(overview.innerHTML, /规则兜底/);
  assert.match(plan.innerHTML, /关键问题/);
  assert.match(plan.innerHTML, /规则兜底/);
  assert.match(plan.innerHTML, /模型规划失败/);
  assert.match(plan.innerHTML, /上下文收集完成/);
  assert.match(plan.innerHTML, /模型规划已开始/);
  assert.match(plan.innerHTML, /规划已就绪/);
  assert.match(plan.innerHTML, /缺陷判定标准是什么/);
  assert.match(plan.innerHTML, /划痕\/斑点/);
  assert.match(plan.innerHTML, /通用表面缺陷候选区域/);
  assert.match(plan.innerHTML, /裂纹/);
  assert.match(plan.innerHTML, /凹痕\/污渍/);
  assert.match(plan.innerHTML, /开始构建/);
  assert.doesNotMatch(plan.innerHTML, /按推荐方案开始构建/);
  assert.match(plan.innerHTML, /资源补齐会在开始构建后出现/);
  assert.match(overview.innerHTML, /Plan 规划/);
  assert.match(overview.innerHTML, /Build 审计/);
  assert.match(overview.innerHTML, /Applied 复核/);
  assert.doesNotMatch(plan.innerHTML, /资源审计任务|人工确认模型资源|人工选择模板资源|仅记录 PLC 元数据/);
  assert.doesNotMatch(plan.innerHTML, /Clarifying Questions|Accept Recommended and Build|Plan Mode/);
  assert.doesNotMatch([
    overview.innerHTML,
    plan.innerHTML
  ].join('\n'), /Accept recommended defaults|rule_fallback|collecting_context completed|What should count as a defect|Defect definition controls|Scratch\/blob|Use general surface defect candidates|Good first draft|>Crack<|Dent\/stain|Thresholds need sample confirmation/);
  assert.doesNotMatch(plan.innerHTML, /setTimeout/);
});

test('Plan Mode diagnostics show planner failure code and rule fallback caveat', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': plan,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = async () => backendPlanResult({
    goal: 'planner json fallback plan',
    plannerFailureStage: 'json_parse',
    plannerFailureCode: 'planner_json_parse_failed',
    sanitizedErrorKind: 'planner_json_parse_failed',
    sanitizedErrorMessage: 'Planner 返回内容无法解析为 PlanModeResult JSON。'
  });

  panel._dispatchGenerateRequest({
    description: 'detect board defects',
    userMessage: 'detect board defects'
  });
  await flushAsync();

  assert.match(plan.innerHTML, /当前方案为规则兜底草案，不是大模型 Planner 生成结果/);
  assert.match(plan.innerHTML, /模型规划失败阶段：JSON 解析失败/);
  assert.match(plan.innerHTML, /安全错误类型：<span class="ai-plan-tech-code">planner_json_parse_failed<\/span>/);
  assert.match(plan.innerHTML, /fallbackReason：<span class="ai-plan-tech-code">planner_failed<\/span>/);
  assert.match(plan.innerHTML, /请检查 Planner 模型是否按 PlanModeResult JSON 契约输出/);
});

test('Plan Mode diagnostics redact sensitive planner failure details', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': plan,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const unsafe = `token=abc123 api_key=secret baseUrl=https://planner.example.invalid/v1 C:\\factory\\model.onnx 192.168.1.10 DB1.DBX0.0 plc://line1 data:image/png;base64,${'A'.repeat(120)}`;
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = async () => backendPlanResult({
    goal: 'redacted fallback plan',
    plannerFailureStage: 'completion_request',
    plannerFailureCode: 'completion_request_failed',
    sanitizedErrorKind: 'completion_request_failed',
    sanitizedErrorMessage: unsafe,
    publicEvents: [
      {
        stage: 'planning_with_model',
        status: 'failed',
        title: unsafe,
        summary: unsafe,
        metadata: {
          fallbackReason: 'planner_failed',
          plannerFailureStage: 'completion_request',
          plannerFailureCode: 'completion_request_failed',
          sanitizedErrorKind: 'completion_request_failed',
          sanitizedErrorMessage: unsafe
        }
      },
      {
        stage: 'rule_fallback_used',
        status: 'completed',
        title: 'Rule fallback used',
        summary: unsafe,
        metadata: { fallbackReason: 'planner_failed' }
      }
    ]
  });

  panel._dispatchGenerateRequest({
    description: 'detect board defects',
    userMessage: 'detect board defects'
  });
  await flushAsync();

  assert.match(plan.innerHTML, /completion_request_failed/);
  assert.match(plan.innerHTML, /请检查网络、Planner 接口地址配置、模型服务和中转站状态/);
  assert.doesNotMatch(plan.innerHTML, /token=abc123|api_key=secret|baseUrl|https:\/\/planner\.example|C:\\factory|192\.168\.1\.10|DB1\.DBX0\.0|plc:\/\/line1|data:image|base64|AAAA/i);
});

test('ordinary build prompt stays Plan-first even when Tool Loop mode is selected', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'tool_loop' });
  const input = createFakeElement();
  const overview = createFakeElement();
  const plan = createFakeElement();
  const build = createFakeElement();
  panel.attachments = [];
  panel.container = createContainer({
    '#ai-input': input,
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': overview,
    '#ai-plan-workspace': plan,
    '#ai-build-workspace': build,
    '#ai-result-status-note': createFakeElement()
  });
  let capturedPlanRequest = null;
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = async request => {
    capturedPlanRequest = request;
    return backendPlanResult({
      goal: 'packaging box appearance inspection workflow',
      originalUserPrompt: request.description
    });
  };
  panel._dispatchAgentRunGenerateRequest = () => {
    throw new Error('ordinary prompt should not start Build before Plan confirmation');
  };
  input.value = '为我构建一个包装箱外观视觉检测流程';

  await panel._handleGenerate();
  await flushAsync();

  assert.equal(capturedPlanRequest.description, '为我构建一个包装箱外观视觉检测流程');
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(panel.activeAgentRunId, null);
  assert.equal(panel.isGenerating, false);
  assert.equal(panel.pendingVisionPlan.goal, 'packaging box appearance inspection workflow');
  assert.match(plan.innerHTML, /推荐方案/);
  assert.match(plan.innerHTML, /关键问题/);
  assert.match(plan.innerHTML, /推荐默认值/);
  assert.match(plan.innerHTML, /开始构建/);
  assert.doesNotMatch(plan.innerHTML, /按推荐方案开始构建/);
  assert.doesNotMatch(overview.innerHTML, /VisionAgentLoop/);
});

test('quick example selection submits through Plan-first path', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const input = createFakeElement();
  const plan = createFakeElement();
  panel.attachments = [];
  panel.container = createContainer({
    '#ai-input': input,
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': plan,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  let capturedPlanRequest = null;
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = async request => {
    capturedPlanRequest = request;
    return backendPlanResult({ goal: 'quick example plan' });
  };

  await panel._handleQuickExampleSelection('检测金属零件表面的划痕缺陷。');
  await flushAsync();

  assert.equal(capturedPlanRequest.description, '检测金属零件表面的划痕缺陷。');
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(panel.pendingVisionPlan.goal, 'quick example plan');
  assert.match(plan.innerHTML, /开始构建/);
  assert.doesNotMatch(plan.innerHTML, /按推荐方案开始构建/);
});

test('unknown skipPlan and build-like explicit modes cannot bypass Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'tool_loop' });

  assert.equal(panel._shouldOpenPlanModeBeforeBuild({ explicitMode: 'build' }), true);
  assert.equal(panel._shouldOpenPlanModeBeforeBuild({ explicitMode: 'stable' }), true);
  assert.equal(panel._shouldOpenPlanModeBeforeBuild({ explicitMode: 'tool_loop' }), true);
  assert.equal(panel._shouldOpenPlanModeBeforeBuild({ explicitMode: 'auto', skipPlan: true }), true);
  assert.equal(panel._shouldOpenPlanModeBeforeBuild({
    explicitMode: 'new',
    skipPlan: true,
    buildFromPlan: { planHash: 'sha256:from-plan' }
  }), false);
  assert.equal(panel._shouldOpenPlanModeBeforeBuild({
    explicitMode: 'new',
    skipPlan: true,
    skipPlanSource: 'confirmed_plan',
    buildFromPlan: { planHash: 'sha256:confirmed' }
  }), false);
  assert.equal(panel._shouldOpenPlanModeBeforeBuild({
    explicitMode: 'new',
    skipPlan: true,
    skipPlanSource: 'developer_direct_build_debug'
  }), false);
});

test('existing-flow modification turns still bypass Plan and enter Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });

  assert.equal(panel._shouldOpenPlanModeBeforeBuild({
    explicitMode: '',
    description: '把当前流程里的算子名称改成中文，其他参数保持不变',
    hasCurrentFlowContext: true
  }), false);
  assert.equal(panel._shouldOpenPlanModeBeforeBuild({
    explicitMode: '',
    description: '新增一个缺陷检测流程',
    hasCurrentFlowContext: true
  }), true);
});

test('Plan Mode streams public Plan progress into the assistant message', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const chat = createFakeElement();
  const overview = createFakeElement();
  const plan = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': chat,
    '#ai-agent-workspace-overview': overview,
    '#ai-plan-workspace': plan,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.activeAgentRunId = 'ar_previous_build';
  const planResult = backendPlanResult({
    planSource: 'model_planner',
    fallbackReason: '',
    goal: 'streamed plan ready',
    publicEvents: [
      { stage: 'collecting_context', status: 'completed', title: 'Context collected', summary: 'Context collected' },
      { stage: 'planning_with_model', status: 'completed', title: 'Planner candidate returned', summary: 'Planner returned a public structured candidate for validation.' },
      { stage: 'validating_plan_contract', status: 'completed', title: 'Plan contract valid', summary: 'Planner plan was normalized to the public PlanModeResult contract.' },
      { stage: 'applying_safety_constraints', status: 'completed', title: 'Safety constraints applied', summary: 'Redaction, metadata-only boundaries, resource placeholders, and PLC safety policy were applied.' }
    ]
  });
  installFetchStream([
    {
      json: {
        runId: 'ar_plan_stream',
        events: [
          {
            runId: 'ar_plan_stream',
            sequence: 1,
            eventType: 'run.started',
            stage: 'run',
            title: 'Vision Agent run started',
            summary: 'Plan run started.',
            status: 'running',
            payload: { mode: 'plan', metadataOnly: true },
            metadataOnly: true,
            redactionPass: true
          },
          {
            runId: 'ar_plan_stream',
            sequence: 2,
            eventType: 'plan.started',
            stage: 'plan',
            title: '规划已启动',
            summary: '正在进入规划阶段。',
            status: 'running',
            metadataOnly: true,
            redactionPass: true
          }
        ]
      }
    },
    {
      body: [
        encodeSseEvent({
          runId: 'ar_plan_stream',
          sequence: 3,
          eventType: 'plan.context.completed',
          stage: 'collecting_context',
          title: '上下文已收集',
          summary: '已收集公开需求、流程、模板、附件、算子和工站边界。',
          status: 'completed',
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent({
          runId: 'ar_plan_stream',
          sequence: 4,
          eventType: 'plan.model.started',
          stage: 'planning_with_model',
          title: '模型规划中',
          summary: '模型正在生成公开结构化规划候选。',
          status: 'running',
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent({
          runId: 'ar_plan_stream',
          sequence: 5,
          eventType: 'plan.contract.completed',
          stage: 'validating_plan_contract',
          title: '规划契约已校验',
          summary: '规划已归一到公开 PlanModeResult 契约。',
          status: 'completed',
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent({
          runId: 'ar_plan_stream',
          sequence: 6,
          eventType: 'plan.safety.completed',
          stage: 'applying_safety_constraints',
          title: '安全约束已应用',
          summary: '已应用安全约束。',
          status: 'completed',
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent({
          runId: 'ar_plan_stream',
          sequence: 7,
          eventType: 'plan.completed',
          stage: 'plan_ready',
          title: '规划已就绪',
          summary: '规划已完成，可以开始构建。',
          status: 'completed',
          payload: { planResult, metadataOnly: true },
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent(planRunCompletedEvent({
          runId: 'ar_plan_stream',
          sequence: 8,
          planResult,
          revision: 18
        }))
      ].join('')
    }
  ]);

  const accepted = panel._dispatchGenerateRequest({
    description: 'stream plan',
    userMessage: 'stream plan'
  });
  const turn = panel.activeAssistantTurn;

  assert.equal(accepted, true);
  assert.ok(turn);
  assert.match(turn.replyBody.textContent, /正在判断请求类型/);
  await waitFor(() => panel.pendingVisionPlan?.planId === 'plan_backend_1', 'streamed plan result');

  assert.equal(panel.pendingVisionPlan.goal, 'streamed plan ready');
  assert.equal(panel.isGenerating, false);
  assert.match(turn.replyBody.textContent, /规划已完成，请确认推荐项或手动回答后开始构建/);
  assert.match(collectProcessText(turn), /收集上下文：完成/);
  assert.match(collectProcessText(turn), /模型规划：进行中|模型规划：完成/);
  assert.match(collectProcessText(turn), /契约校验：完成/);
  assert.match(collectProcessText(turn), /安全约束：完成/);
  assert.match(overview.innerHTML, /streamed plan ready/);
  assert.match(plan.innerHTML, /规划诊断/);
  assert.doesNotMatch(turn.replyBody.textContent, /collecting_context|planning_with_model|rawPrompt|chain/i);
});

test('PlanRun public live events split ephemeral status persistent fallback and diagnostics', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.activePlanRequestId = 'plan-request-live';
  panel.activePlanRunId = 'ar_plan_live';
  panel.activePlanRunRequestId = 'plan-request-live';

  panel._handlePlanRunEvent({
    runId: 'ar_plan_live',
    sequence: 1,
    eventType: 'plan.context.started',
    stage: 'collecting_context',
    title: '收集上下文',
    summary: '正在收集公开需求、流程、模板、附件、算子和工站边界。',
    status: 'running',
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(turn.liveStatusEl.hidden, false);
  assert.match(turn.liveStatusEl.innerHTML, /正在收集工程上下文/);

  panel._handlePlanRunEvent({
    runId: 'ar_plan_live',
    sequence: 2,
    eventType: 'plan.model.started',
    stage: 'planning_with_model',
    title: '模型规划中',
    summary: '模型正在生成公开结构化规划候选。',
    status: 'running',
    metadataOnly: true,
    redactionPass: true
  });

  assert.match(turn.liveStatusEl.innerHTML, /Planner 模型/);

  panel._handlePlanRunEvent({
    runId: 'ar_plan_live',
    sequence: 3,
    eventType: 'plan.model.failed',
    stage: 'planning_with_model',
    title: '模型规划失败',
    summary: '模型规划未能产出可用规划，已使用规则兜底方案。',
    status: 'failed',
    payload: {
      fallbackReason: 'planner_failed',
      plannerFailureStage: 'json_parse',
      plannerFailureCode: 'planner_json_parse_failed',
      sanitizedErrorKind: 'planner_json_parse_failed',
      sanitizedErrorMessage: 'rawPrompt=secret baseUrl=http://10.1.2.3/v1 C:\\factory\\image.png DB1.DBX0.0 data:image/png;base64,abcd'
    },
    metadataOnly: true,
    redactionPass: true
  });

  let processText = collectProcessText(turn);
  assert.match(processText, /Planner 未能产出可用规划/);
  assert.match(processText, /Planner JSON 解析失败/);
  assert.match(processText, /当前方案为规则兜底草案/);
  assert.equal(turn.failureSection.hidden, false);
  assert.match(turn.failureBody.innerHTML, /诊断详情/);
  assert.match(turn.failureBody.innerHTML, /Planner JSON 解析失败/);
  assert.match(turn.failureBody.innerHTML, /规则兜底草案/);
  assertNoSensitiveLeak(turn.failureBody.innerHTML);

  panel._handlePlanRunEvent({
    runId: 'ar_plan_live',
    sequence: 4,
    eventType: 'plan.fallback.used',
    stage: 'rule_fallback_used',
    title: '已使用规则兜底方案',
    summary: '已启用规则兜底草案。',
    status: 'completed',
    payload: {
      fallbackReason: 'planner_failed',
      plannerFailureCode: 'planner_json_parse_failed'
    },
    metadataOnly: true,
    redactionPass: true
  });

  processText = collectProcessText(turn);
  assert.match(processText, /已启用规则兜底草案/);
  assert.match(processText, /不是大模型 Planner 生成结果/);
  assert.equal(panel.publicLiveEvents.filter(evt => evt.visibility === 'ephemeral').length, 2);
  assert.equal(panel.publicLiveEvents.filter(evt => evt.visibility === 'persistent').length, 2);
});

test('Public live event stats count accepted duplicate stale and dropped events', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  attachAgentRunTurn(panel);
  panel.activePlanRequestId = 'plan-request-stats';
  panel.activePlanRunId = 'ar_plan_stats';
  panel.activePlanRunRequestId = 'plan-request-stats';

  const evt = {
    runId: 'ar_plan_stats',
    sequence: 1,
    eventType: 'plan.context.started',
    stage: 'collecting_context',
    title: 'Context started',
    summary: 'Collecting public context.',
    status: 'running',
    metadataOnly: true,
    redactionPass: true
  };

  panel._handlePlanRunEvent(evt);
  panel._handlePlanRunEvent(evt);
  panel._handlePlanRunEvent({
    ...evt,
    runId: 'ar_plan_old',
    sequence: 2
  });
  panel._handlePlanRunEvent(null);

  const stats = panel._getPublicLiveEventStats();
  assert.equal(stats.accepted, 1);
  assert.equal(stats.duplicate, 1);
  assert.equal(stats.stale, 1);
  assert.equal(stats.dropped, 1);
  assert.equal(stats.ephemeral, 1);
  assert.equal(panel.publicLiveEvents.length, 1);
});

test('Semantic public live events render public slots and redact unsafe diagnostics', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activePlanRequestId = 'plan-request-semantic';
  panel.activePlanRunId = 'ar_plan_semantic';
  panel.activePlanRunRequestId = 'plan-request-semantic';

  panel._handlePlanRunEvent({
    runId: 'ar_plan_semantic',
    sequence: 1,
    eventType: 'semantic.completed',
    stage: 'semantic_extraction',
    title: '语义抽取完成',
    summary: '语义理解来自模型，已生成公开结构化摘要。',
    status: 'completed',
    payload: {
      metadata: {
        semanticSource: 'model',
        taskType: 'attribute_classification',
        inspectionObject: '草莓',
        targetAttribute: '成熟度',
        okCondition: '熟透则 OK',
        ngCondition: '否则 NG',
        imageSource: '相机'
      },
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });
  panel._handlePlanRunEvent({
    runId: 'ar_plan_semantic',
    sequence: 2,
    eventType: 'semantic.fallback.used',
    stage: 'semantic_fallback_used',
    title: '已启用语义规则降级',
    summary: '语义抽取模型不可用，当前为规则降级解析。',
    status: 'warning',
    payload: {
      metadata: {
        semanticSource: 'rule_fallback',
        failureCode: 'semantic_json_parse_failed',
        sanitizedErrorMessage: 'rawPrompt=secret C:\\factory\\hidden.png'
      },
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });

  const processText = collectProcessText(turn);
  assert.match(processText, /语义来源：模型/);
  assert.match(processText, /任务类型：属性分类 \/ OK-NG 判别/);
  assert.match(processText, /对象：草莓/);
  assert.match(processText, /OK：熟透则 OK/);
  assert.match(processText, /语义抽取 JSON 解析失败/);
  assert.doesNotMatch(processText, /rawPrompt|C:\\|hidden\.png/);
  assert.equal(panel._getPublicLiveEventStats().accepted, 2);
});

test('Workbench state changes publish matching right-side public events', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const turn = attachAgentRunTurn(panel);
  const stateBar = createFakeElement();
  panel.container = createContainer({
    '#ai-workbench-state-bar': stateBar
  });
  panel.activeAgentRunId = 'ar_workbench';

  const states = [
    'clarifying',
    'matching_template',
    'generating',
    'parsing',
    'validating',
    'dry_running',
    'reviewing_parameters',
    'ready_to_apply',
    'applying',
    'applied',
    'failed',
    'cancelled',
    'idle'
  ];

  states.forEach(state => panel._setWorkbenchState(state));

  const eventTypes = panel.publicLiveEvents.map(evt => evt.eventType);
  for (const state of states) {
    assert.ok(eventTypes.includes(`workbench.state.${state}`), state);
  }
  assert.match(collectProcessText(turn), /构建已完成，可应用到画布/);
  assert.match(collectProcessText(turn), /流程草稿已应用/);
  assert.match(collectProcessText(turn), /工作台进入失败状态/);
  assert.equal(panel.publicLiveEvents.find(evt => evt.eventType === 'workbench.state.ready_to_apply')?.visibility, 'persistent');
  assert.equal(panel.publicLiveEvents.find(evt => evt.eventType === 'workbench.state.dry_running')?.visibility, 'ephemeral');
});

test('Public live event snapshot can replay and rebuild the assistant public stream', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.activePlanRequestId = 'plan-request-snapshot';
  panel.activePlanRunId = 'ar_plan_snapshot';
  panel.activePlanRunRequestId = 'plan-request-snapshot';

  panel._handlePlanRunEvent({
    runId: 'ar_plan_snapshot',
    sequence: 1,
    eventType: 'plan.model.failed',
    stage: 'planning_with_model',
    title: '模型规划失败',
    summary: '模型规划未能产出可用规划，已使用规则兜底方案。',
    status: 'failed',
    payload: {
      fallbackReason: 'planner_failed',
      plannerFailureCode: 'planner_json_parse_failed',
      sanitizedErrorMessage: 'systemPrompt=secret token=abc123 http://192.168.1.9 C:\\factory\\model.onnx'
    },
    metadataOnly: true,
    redactionPass: true
  });
  panel._handlePlanRunEvent({
    runId: 'ar_plan_snapshot',
    sequence: 2,
    eventType: 'plan.fallback.used',
    stage: 'rule_fallback_used',
    title: '已使用规则兜底方案',
    summary: '当前方案为规则兜底草案，不是大模型 Planner 生成结果。',
    status: 'completed',
    payload: {
      fallbackReason: 'planner_failed',
      plannerFailureCode: 'planner_json_parse_failed'
    },
    metadataOnly: true,
    redactionPass: true
  });

  const snapshot = panel._buildPublicLiveEventSnapshot({ runId: 'ar_plan_snapshot' });
  assert.equal(snapshot.length, 2);
  assert.doesNotMatch(JSON.stringify(snapshot), /systemPrompt|rawPrompt|chainOfThought|C:\\|\.onnx|192\.168\.|abc123|data:image|base64|http:\/\//i);

  const replayPanel = createPanel(AiPanel, { developer: false, enabled: true });
  const replayTurn = attachAgentRunTurn(replayPanel);
  replayPanel.activePlanRunId = 'ar_plan_snapshot';
  const replayed = replayPanel._replayPublicLiveEventSnapshot(snapshot, { runId: 'ar_plan_snapshot' });

  assert.equal(replayed, 2);
  assert.match(collectProcessText(replayTurn), /Planner 未能产出可用规划/);
  assert.match(collectProcessText(replayTurn), /不是大模型 Planner 生成结果/);
  assert.equal(replayTurn.failureSection.hidden, false);
  assert.match(replayTurn.failureBody.innerHTML, /Planner JSON 解析失败/);
});

test('Developer latest AgentRun replay rebuilds public Plan stream from snapshot', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  const requests = installFetchStream([
    {
      json: {
        summary: {
          runId: 'ar_latest_plan',
          status: 'completed'
        },
        snapshot: {
          events: [
            {
              runId: 'ar_latest_plan',
              sequence: 3,
              eventType: 'plan.fallback.used',
              stage: 'rule_fallback_used',
              title: 'Rule fallback used',
              summary: 'Rule fallback is available.',
              status: 'completed',
              payload: {
                fallbackReason: 'planner_failed',
                metadataOnly: true
              },
              metadataOnly: true,
              redactionPass: true
            },
            {
              runId: 'ar_latest_plan',
              sequence: 2,
              eventType: 'plan.model.failed',
              stage: 'planning_with_model',
              title: 'Planner failed',
              summary: 'Planner did not produce a usable plan.',
              status: 'failed',
              payload: {
                plannerFailureCode: 'planner_json_parse_failed',
                sanitizedErrorMessage: 'systemPrompt=secret C:\\factory\\hidden.png'
              },
              metadataOnly: true,
              redactionPass: true
            }
          ]
        },
        diagnostics: {
          eventCount: 2,
          droppedEventCount: 0,
          duplicateEventCount: 0,
          staleEventCount: 0
        }
      }
    }
  ]);
  panel._startAssistantTurn = () => turn;
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });

  const replayed = await panel._replayLatestAgentRunPublicEvents();

  assert.equal(replayed, true);
  assert.equal(requests[0].url, 'http://localhost:5000/api/ai/agent-runs/latest');
  assert.equal(panel.activePlanRunId, 'ar_latest_plan');
  assert.equal(panel.activeAgentRunId, null);
  assert.deepEqual(panel.activePlanRunEvents.map(evt => evt.sequence), [2, 3]);
  assert.equal(panel.publicLiveEvents.length, 2);
  const stats = panel._getPublicLiveEventStats();
  assert.equal(stats.accepted, 2);
  assert.equal(stats.persistent, 2);
  assert.match(panel.lastResultStatusNote.text, /ar_latest_plan/);
  assert.doesNotMatch(JSON.stringify(panel.publicLiveEvents), /systemPrompt|C:\\|hidden\.png/);
});

test('Plan Mode timeout stream shows rule fallback copy', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const planResult = backendPlanResult({
    planSource: 'rule_fallback',
    fallbackReason: 'planner_timeout',
    goal: 'timeout fallback plan'
  });
  installFetchStream([
    { json: { runId: 'ar_plan_timeout', events: [] } },
    {
      body: [
        encodeSseEvent({
          runId: 'ar_plan_timeout',
          sequence: 3,
          eventType: 'plan.model.timeout',
          stage: 'planning_with_model',
          title: '模型规划超时',
          summary: '模型规划超时，已使用规则兜底方案。',
          status: 'failed',
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent({
          runId: 'ar_plan_timeout',
          sequence: 4,
          eventType: 'plan.fallback.used',
          stage: 'rule_fallback_used',
          title: '已使用规则兜底方案',
          summary: '已使用规则兜底方案。',
          status: 'completed',
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent({
          runId: 'ar_plan_timeout',
          sequence: 5,
          eventType: 'plan.completed',
          stage: 'plan_ready',
          title: '规划已就绪',
          summary: '模型规划超时，已使用规则兜底方案。',
          status: 'completed',
          payload: { planResult, metadataOnly: true },
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent(planRunCompletedEvent({
          runId: 'ar_plan_timeout',
          sequence: 6,
          planResult,
          revision: 19
        }))
      ].join('')
    }
  ]);

  panel._dispatchGenerateRequest({ description: 'timeout plan', userMessage: 'timeout plan' });
  const turn = panel.activeAssistantTurn;
  await waitFor(() => panel.pendingVisionPlan?.goal === 'timeout fallback plan', 'timeout fallback plan');

  assert.match(turn.replyBody.textContent, /模型规划超时，已使用规则兜底方案/);
  assert.match(turn.replyBody.textContent, /稍后重试深度规划/);
  assert.match(collectProcessText(turn), /模型规划：超时|规则兜底：完成/);
  assert.equal(panel.pendingVisionPlan.planSource, 'rule_fallback');
  assert.match(panel.pendingVisionPlan.fallbackReason, /模型规划超时/);
});

test('Plan Mode falls back to ordinary POST only when PlanRun event mode is unavailable before request', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._shouldUsePlanRunEventStream = () => false;
  let fallbackCalls = 0;
  panel._requestBackendVisionPlan = async () => {
    fallbackCalls += 1;
    return backendPlanResult({ planId: 'plan_fallback_post', goal: 'ordinary fallback plan' });
  };
  const originalFetch = global.fetch;
  global.fetch = () => {
    throw new Error('PlanRun fetch should not start when event mode is unavailable');
  };

  try {
    panel._dispatchGenerateRequest({ description: 'fallback to post', userMessage: 'fallback to post' });
    const turn = panel.activeAssistantTurn;
    await waitFor(() => panel.pendingVisionPlan?.planId === 'plan_fallback_post', 'ordinary plan fallback');

    assert.equal(fallbackCalls, 1);
    assert.match(turn.replyBody.textContent, /规划已完成|已使用规则兜底方案/);
    assert.match(panel.lastResultStatusNote.text, /规划模式等待确认/);
  } finally {
    global.fetch = originalFetch;
  }
});

test('ordinary Plan terminal persistence warning stays visible after plan confirmation prompt', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestPlanReadinessPreview = () => null;
  const warningMessage = '规划结果已生成，但本次 Plan 工作台状态未能保存。';
  installFetchStream([
    {
      json: {
        sessionId: 'session-ordinary-warning',
        planResult: backendPlanResult({
          planId: 'plan_ordinary_warning',
          goal: 'ordinary warning plan',
          canPlan: true,
          canBuild: false
        }),
        workspaceSnapshot: {
          revision: 44,
          lifecycleState: 'plan_ready',
          planRunStatus: 'completed'
        },
        persistenceStatus: {
          primaryStoreSaved: false,
          recoveryBackupSaved: true,
          errorCode: 'primary_store_save_failed',
          publicMessage: warningMessage
        },
        persistenceWarning: {
          code: 'primary_store_save_failed',
          message: warningMessage
        },
        metadataOnly: true
      }
    }
  ]);

  panel._enterPlanModeFromPrompt({
    description: 'ordinary warning plan',
    userMessage: 'ordinary warning plan',
    clearInput: false
  });
  await waitFor(() => panel.pendingVisionPlan?.planId === 'plan_ordinary_warning', 'ordinary warning plan');

  assert.equal(panel.workspaceSnapshotRevision, 44);
  assert.equal(panel.pendingVisionPlan.rawPlanSnapshot.workspaceSnapshot.revision, 44);
  assert.equal(panel.pendingVisionPlan.rawPlanSnapshot.persistenceStatus.primaryStoreSaved, false);
  assert.equal(panel.pendingVisionPlan.rawPlanSnapshot.persistenceWarning.message, warningMessage);
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
  assert.match(panel.lastResultStatusNote.text, /Plan 工作台状态未能保存/);
  assert.doesNotMatch(panel.lastResultStatusNote.text, /规划模式等待确认/);
  assert.match(panel.workspacePersistenceWarning.message, /Plan 工作台状态未能保存/);
});

test('PlanRun create 503 keeps Plan input and does not fall back to ordinary Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const input = createFakeElement();
  input.value = 'detect scratches';
  panel.container = createContainer({
    '#ai-input': input,
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  let fallbackCalls = 0;
  panel._requestBackendVisionPlan = async () => {
    fallbackCalls += 1;
    throw new Error('ordinary Plan must not be called after PlanRun create failure');
  };
  const requests = installFetchStream([
    {
      status: 503,
      json: {
        errorCode: 'session_persistence_failed',
        publicMessage: 'Plan Run 创建失败：会话状态未能保存，模型规划未启动。'
      }
    }
  ]);

  panel._enterPlanModeFromPrompt({
    description: 'detect scratches',
    userMessage: 'detect scratches',
    input,
    clearInput: true
  });
  const turn = panel.activeAssistantTurn;
  await waitFor(() => panel.isGenerating === false, 'PlanRun create failure handled');

  assert.equal(fallbackCalls, 0);
  assert.equal(requests.length, 1);
  assert.match(requests[0].url, /\/api\/ai\/agent-plan-runs$/);
  assert.doesNotMatch(requests[0].url, /\/api\/ai\/agent-plan$/);
  assert.equal(input.value, 'detect scratches');
  assert.match(turn.replyBody.textContent, /Plan Run 创建失败：会话状态未能保存，模型规划未启动。/);
  assert.match(panel.lastResultStatusNote.text, /Plan Run 创建失败：会话状态未能保存，模型规划未启动。/);
});

test('Start Build from Plan enters Build request with skipPlan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    planId: 'plan_build_1',
    planHash: 'sha256:plan-build-1',
    canBuild: true,
    templateSelection: { mode: 'template_adapt', templateId: 'tmpl-plan', scenarioKey: 'scratch' }
  }));
  panel.planQuestionAnswers = Object.fromEntries(
    panel.pendingVisionPlan.questions.map(question => [question.id, {
      questionId: question.id,
      field: question.field || question.id,
      value: question.defaultValue,
      origin: 'explicit_user_selection'
    }])
  );
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  const started = await panel._startBuildFromCurrentPlan();

  assert.equal(started, true);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(captured.skipPlan, true);
  assert.equal(captured.skipPlanSource, 'confirmed_plan');
  assert.equal(captured.explicitMode, 'new');
  assert.equal(captured.hint, '');
  assert.match(captured.userMessage, /从计划开始构建/);
  assert.equal(captured.buildFromPlan.planHash, 'sha256:plan-build-1');
  assert.equal(captured.buildFromPlan.acceptedRecommendedDefaults, false);
  assert.equal(captured.buildFromPlan.planSnapshot.planHash, 'sha256:plan-build-1');
  assert.equal(captured.buildFromPlan.planSnapshot.canBuild, true);
  assert.equal(captured.buildFromPlan.planSnapshot.buildReadiness, undefined);
  assert.equal(captured.buildFromPlan.requirementMaturity.canBuild, true);
  assert.deepEqual(captured.buildFromPlan.templateSelection, {
    mode: 'template_adapt',
    templateId: 'tmpl-plan',
    scenarioKey: 'scratch'
  });
  assert.deepEqual(captured.templateSelection, captured.buildFromPlan.templateSelection);
});

test('Recommended strategy is not selected until accepted for Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.requirementMode = 'strict';
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult());
  panel.planQuestionSelections = {};
  panel.planQuestionAnswers = {};
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  panel._renderPlanWorkspace(panel.pendingVisionPlan);
  assert.equal(panel.pendingVisionPlan.executable, false);
  assert.deepEqual(panel.planQuestionSelections, {});
  assert.deepEqual(panel.planQuestionAnswers, {});
  assert.deepEqual(panel._buildPlanSelectionMap(panel.pendingVisionPlan), {});
  assert.match(planWorkspace.innerHTML, /is-recommended/);
  assert.match(planWorkspace.innerHTML, /aria-pressed="false"/);

  const started = await panel._startBuildFromCurrentPlan();
  assert.equal(started, false);
  assert.equal(captured, null);

  const acceptedStarted = await panel._startBuildFromCurrentPlan({ acceptedRecommended: true });
  assert.equal(acceptedStarted, false);
  assert.equal(captured, null);
  assert.equal(panel.planAcceptedRecommendedDefaults, true);

  const startedAfterPreview = await panel._startBuildFromCurrentPlan();
  assert.equal(startedAfterPreview, true);
  assert.equal(captured.skipPlan, true);
  assert.equal(captured.buildFromPlan.acceptedRecommendedDefaults, true);
  assert.equal(captured.buildFromPlan.userSelections.model_or_rule_strategy, 'deep_learning');
  assert.deepEqual(captured.buildFromPlan.confirmedAnswers, [
    {
      questionId: 'model_or_rule_strategy',
      field: 'algorithm_strategy',
      value: 'deep_learning',
      origin: 'accepted_recommended_default'
    }
  ]);
});

test('Fallback questions with empty options stay free-text and clean stale selections', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult({
    planContractVersion: 'v2',
    planId: 'fallback-free-text',
    planHash: 'sha256:fallback-free-text',
    goal: '病灶检测',
    canBuild: false,
    clarificationQuestions: [
      {
        id: 'q_old_task',
        field: 'task_type',
        title: '任务类型',
        defaultValue: 'custom_input',
        options: [{ value: 'custom_input', label: '自定义输入', recommended: true }]
      },
      {
        id: 'q_new_task',
        field: 'task_type',
        title: '任务类型补充',
        defaultValue: '',
        options: []
      }
    ],
    buildReadiness: {
      canBuild: false,
      resolvedFields: ['inspection_object'],
      remainingFields: ['task_type'],
      blockers: [{ id: 'hard_requirement:task_type_missing', field: 'task_type', blocksBuild: true }]
    },
    requirementMaturity: {
      canPlan: true,
      canBuild: false,
      missingFields: ['task_type']
    }
  });
  panel.planQuestionSelections = { q_old_task: 'custom_input' };
  panel.planQuestionAnswers = {};

  panel._renderPlanWorkspace(panel.pendingVisionPlan);
  assert.match(planWorkspace.innerHTML, /ai-plan-custom-input-field/);
  assert.match(planWorkspace.innerHTML, /data-plan-question-option="custom_input"/);

  const acceptedAnswers = panel._buildConfirmedPlanAnswers(panel.pendingVisionPlan, { acceptedRecommended: true });
  assert.deepEqual(acceptedAnswers, []);

  panel._customInputPlanQuestion('q_new_task', 'presence_absence');
  assert.deepEqual(panel.planQuestionSelections, { q_new_task: 'presence_absence' });

  const answers = panel._buildConfirmedPlanAnswers(panel.pendingVisionPlan);
  assert.deepEqual(answers, [
    {
      questionId: 'q_new_task',
      field: 'task_type',
      value: 'presence_absence',
      origin: 'explicit_user_text'
    }
  ]);
});

test('Draft Plan can start Build with legal Planner route without accepting strategy', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult({
    requirementMode: 'draft',
    canBuild: false
  }));
  panel.planQuestionSelections = {};
  panel.planQuestionAnswers = {};
  panel._requestBackendPlanReadinessPreview = request => ({
    ...panel._buildTestPlanReadinessPreview(request),
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: [],
      primaryMessage: '可生成可编辑草稿。',
      contractVersion: 'v2'
    },
    pendingConfirmationCount: 0,
    resourcePendingCount: 0,
    hardBlockerCount: 0
  });
  panel._setRequirementMode('draft', { silent: true });
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  panel._renderPlanWorkspace(panel.pendingVisionPlan);
  assert.equal(panel.pendingVisionPlan.executable, true);
  assert.deepEqual(panel.planQuestionSelections, {});
  assert.deepEqual(panel.planQuestionAnswers, {});

  const started = await panel._startBuildFromCurrentPlan();

  assert.equal(started, true);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(captured.skipPlan, true);
  assert.equal(captured.buildFromPlan.acceptedRecommendedDefaults, false);
  assert.deepEqual(captured.buildFromPlan.userSelections, {});
  assert.deepEqual(captured.buildFromPlan.confirmedAnswers, []);
});

test('Explicit strategy switch is submitted through unified Build button', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult());
  panel.planQuestionSelections = {};
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  panel._renderPlanWorkspace(panel.pendingVisionPlan);
  panel._selectPlanQuestionOption('model_or_rule_strategy', 'traditional_rule');
  const started = await panel._startBuildFromCurrentPlan();

  assert.equal(started, true);
  assert.equal(captured.skipPlan, true);
  assert.equal(captured.buildFromPlan.acceptedRecommendedDefaults, false);
  assert.equal(captured.buildFromPlan.userSelections.model_or_rule_strategy, 'traditional_rule');
  assert.deepEqual(captured.buildFromPlan.confirmedAnswers, [
    {
      questionId: 'model_or_rule_strategy',
      field: 'algorithm_strategy',
      value: 'traditional_rule',
      origin: 'explicit_user_selection'
    }
  ]);
  assert.deepEqual(panel.planQuestionSelections, { model_or_rule_strategy: 'traditional_rule' });
  assert.equal(panel.planQuestionAnswers.model_or_rule_strategy.field, 'algorithm_strategy');
  assert.equal(panel.pendingVisionPlan.executable, true);
});

test('Aliased medical requirement answers unblock Plan Build button', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  const planActionButton = createFakeButton();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': createFakeElement(),
      '#ai-plan-workspace': createFakeElement(),
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': inlineBuildButton
    },
    { '.ai-plan-action': [planActionButton] }
  );
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    goal: 'medical lesion detection workflow',
    intent: 'medical_lesion_detection',
    canPlan: true,
    canBuild: false,
    recommendedRoute: {
      routeId: 'medical_lesion_detection_route',
      title: 'Medical lesion detection route',
      summary: 'Segment suspected lesions and output structured findings.',
      operators: ['ImageAcquisition', 'SemanticSegmentation', 'ResultJudgment', 'ResultOutput']
    },
    clarificationQuestions: [
      {
        id: 'medical_modality_and_lesion_type',
        field: 'medical_modality_and_lesion_type',
        title: 'Medical modality and lesion type',
        why: 'Determines the task type and model family.',
        defaultValue: 'ct_lung_nodule_detection',
        defaultAssumption: 'Use CT lung nodule detection as the editable draft task.',
        impact: 'Model resources remain pending.',
        options: [
          {
            value: 'ct_lung_nodule_detection',
            label: 'CT lung nodule',
            recommended: true,
            description: 'Detect suspected lung nodules.',
            impact: 'Draft can continue with pending resources.'
          },
          {
            value: 'mri_brain_lesion_segmentation',
            label: 'MRI brain lesion',
            recommended: false,
            description: 'Segment suspected brain lesions.',
            impact: 'Model resource assumptions change.'
          }
        ]
      },
      {
        id: 'input_source',
        field: 'input_source',
        title: 'Image source',
        why: 'Build needs a source slot.',
        defaultValue: 'offline_image_dataset',
        defaultAssumption: 'Use an offline image dataset placeholder.',
        impact: 'Dataset path remains pending.',
        options: [
          {
            value: 'offline_image_dataset',
            label: 'Offline dataset',
            recommended: true,
            description: 'Use metadata-only dataset input.',
            impact: 'No local path is guessed.'
          },
          {
            value: 'camera_stream',
            label: 'Camera stream',
            recommended: false,
            description: 'Use camera acquisition placeholder.',
            impact: 'Camera binding remains pending.'
          }
        ]
      }
    ],
    blockingReasons: [
      'hard_requirement:task_type_missing',
      'hard_requirement:image_source_missing',
      'strategy_confirmation:medical_modality_and_lesion_type_missing'
    ],
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'unknown',
      canPlan: true,
      canBuild: false,
      objectSignals: ['medical image lesion'],
      taskSignals: [],
      missingFields: ['task_type', 'image_source'],
      blockingReasons: ['task_type_missing', 'image_source_missing'],
      publicReason: 'Task type and image source need confirmation.'
    },
    semanticExtraction: {
      isVisionRequest: true,
      intent: 'new_flow',
      source: 'model',
      taskType: 'unknown',
      confidence: 0.8,
      taskTypeConfidence: 0.2,
      inspectionObject: 'medical image lesion',
      targetAttribute: '',
      defectType: '',
      measurementTarget: '',
      imageSource: '',
      okCondition: 'no suspected lesion',
      ngCondition: 'suspected lesion detected',
      outputTarget: 'structured lesion findings',
      missingFields: ['task_type', 'image_source']
    }
  }));
  panel.pendingVisionPlan = plan;
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  panel._updatePlanBuildActionState();
  assert.equal(plan.executable, false);
  assert.equal(panel._getPlanBuildActionState(plan).canAcceptRecommended, false);

  panel._selectPlanQuestionOption('medical_modality_and_lesion_type', 'ct_lung_nodule_detection');
  assert.equal(panel.planQuestionAnswers.medical_modality_and_lesion_type.field, 'task_type');
  assert.equal(panel.pendingVisionPlan.executable, false);

  panel._selectPlanQuestionOption('input_source', 'offline_image_dataset');
  assert.equal(panel.planQuestionAnswers.input_source.field, 'image_source');
  assert.equal(panel.pendingVisionPlan.executable, true);
  assert.equal(inlineBuildButton.disabled, false);
  assert.equal(planActionButton.disabled, false);

  const started = await panel._startBuildFromCurrentPlan();
  assert.equal(started, true);
  assert.equal(captured.skipPlan, true);
  assert.deepEqual(captured.buildFromPlan.confirmedAnswers, [
    {
      questionId: 'input_source',
      field: 'image_source',
      value: 'offline_image_dataset',
      origin: 'explicit_user_selection'
    },
    {
      questionId: 'medical_modality_and_lesion_type',
      field: 'task_type',
      value: 'ct_lung_nodule_detection',
      origin: 'explicit_user_selection'
    }
  ]);
});

test('Unknown strategy confirmation question id is resolved from matching blocker', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-btn-start-build-inline': inlineBuildButton
  });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    goal: 'custom line guidance inspection workflow',
    canPlan: true,
    canBuild: false,
    blockingReasons: ['strategy_confirmation:line_guidance_profile_missing'],
    clarificationQuestions: [
      {
        id: 'line_guidance_profile',
        field: 'line_guidance_profile',
        title: 'Line guidance profile',
        why: 'A new industry-specific profile gates the planner route.',
        defaultValue: 'profile_a',
        defaultAssumption: 'Use profile A for the first editable draft.',
        impact: 'Parameters remain editable.',
        options: [
          {
            value: 'profile_a',
            label: 'Profile A',
            recommended: true,
            description: 'Use the new profile A.',
            impact: 'Editable draft can continue.'
          },
          {
            value: 'profile_b',
            label: 'Profile B',
            recommended: false,
            description: 'Use the new profile B.',
            impact: 'Different parameters are selected.'
          }
        ]
      }
    ],
    requirementMaturity: {
      maturity: 'actionable',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: true,
      objectSignals: ['custom part'],
      taskSignals: ['line guidance'],
      missingFields: [],
      blockingReasons: [],
      publicReason: 'Hard facts are ready; one planner-specific confirmation remains.'
    },
    semanticExtraction: {
      isVisionRequest: true,
      intent: 'new_flow',
      source: 'model',
      taskType: 'surface_defect',
      confidence: 0.8,
      taskTypeConfidence: 0.8,
      inspectionObject: 'custom part',
      imageSource: 'camera',
      okCondition: 'line guidance is OK',
      ngCondition: 'line guidance is NG',
      outputTarget: 'OK/NG result',
      missingFields: []
    }
  }));
  panel.pendingVisionPlan = plan;
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  panel._updatePlanBuildActionState();
  assert.equal(panel._getPlanBuildActionState(plan).canAcceptRecommended, false);

  panel._selectPlanQuestionOption('line_guidance_profile', 'profile_a');
  assert.equal(panel.planQuestionAnswers.line_guidance_profile.field, 'line_guidance_profile');
  assert.equal(panel.pendingVisionPlan.executable, true);
  assert.equal(inlineBuildButton.disabled, false);

  assert.equal(await panel._startBuildFromCurrentPlan(), true);
  assert.equal(captured.buildFromPlan.confirmedAnswers[0].questionId, 'line_guidance_profile');
  assert.equal(captured.buildFromPlan.confirmedAnswers[0].field, 'line_guidance_profile');
  assert.equal(captured.buildFromPlan.confirmedAnswers[0].value, 'profile_a');
});

test('Authoritative buildReadiness enables Build despite legacy strategy blocker', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-btn-start-build-inline': inlineBuildButton
  });

  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    blockingReasons: ['strategy_confirmation:planner_candidate_not_buildable'],
    requirementMaturity: {
      maturity: 'actionable',
      taskType: 'classification',
      canPlan: true,
      canBuild: true,
      missingFields: [],
      blockingReasons: [],
      publicReason: 'Requirement is actionable.'
    },
    buildReadiness: {
      canBuild: true,
      blockers: [
        {
          id: 'contract_warning:planner_candidate_not_buildable',
          category: 'contract_warning',
          blocksBuild: false,
          resolutionMode: 'non_blocking',
          publicLabel: 'Planner candidate warning retained.'
        }
      ],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: '规划已完成，可以开始构建。',
      contractVersion: 'v2'
    }
  }));

  panel.pendingVisionPlan = plan;
  panel._updatePlanBuildActionState();

  assert.equal(plan.executable, true);
  assert.equal(inlineBuildButton.disabled, false);
});

test('Empty default buildReadiness falls back to legacy compatibility', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    blockingReasons: ['hard_requirement:image_source_missing'],
    buildReadiness: {
      canBuild: false,
      blockers: [],
      resolvedFields: [],
      remainingFields: [],
      primaryMessage: '',
      contractVersion: 'v2'
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      missingFields: ['image_source'],
      blockingReasons: ['image_source_missing'],
      publicReason: 'Image source is required.'
    },
    semanticExtraction: {
      isVisionRequest: true,
      intent: 'new_flow',
      taskType: 'surface_defect',
      inspectionObject: 'metal part',
      imageSource: '',
      okCondition: 'no defect',
      outputTarget: 'local_result_payload',
      missingFields: ['image_source']
    }
  }));

  assert.equal(plan.authoritativeBuildReadiness, null);
  assert.equal(plan.executable, false);
  assert.equal(plan.buildReadiness.blockers.some(blocker =>
    blocker.id === 'hard_requirement:image_source_missing' &&
    blocker.blocksBuild === true), true);
});

test('V2 answer overlay resolves authoritative blocker by exact questionId', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel._renderPlanWorkspace = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    clarificationQuestions: [
      {
        id: 'medical_modality_and_lesion_type',
        field: 'medical_modality_and_lesion_type',
        title: 'Medical modality',
        defaultValue: 'ct_lung_nodule_detection',
        options: [
          { value: 'ct_lung_nodule_detection', label: 'CT lung nodule', recommended: true }
        ]
      }
    ],
    buildReadiness: {
      canBuild: false,
      blockers: [
        {
          id: 'hard_requirement:medical_modality_and_lesion_type_missing',
          category: 'hard_requirement',
          field: 'task_type',
          questionId: 'medical_modality_and_lesion_type',
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: 'Confirm task type.'
        }
      ],
      resolvedFields: ['inspection_object', 'image_source', 'acceptance_criteria'],
      remainingFields: ['task_type'],
      primaryMessage: 'Confirm task type.',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;

  panel._selectPlanQuestionOption('medical_modality_and_lesion_type', 'ct_lung_nodule_detection');

  assert.equal(plan.authoritativeBuildReadiness.canBuild, false);
  assert.equal(plan.executable, true);
  assert.deepEqual(plan.buildReadiness.blockers.filter(blocker => blocker.blocksBuild), []);
  assert.equal(plan.buildReadiness.resolvedFields.includes('task_type'), true);
});

test('V2 answer overlay resolves authoritative blocker by canonical field alias', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel._renderPlanWorkspace = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    clarificationQuestions: [
      {
        id: 'input_source',
        field: 'input_source',
        title: 'Image source',
        defaultValue: 'camera',
        options: [
          { value: 'camera', label: 'Camera', recommended: true }
        ]
      }
    ],
    buildReadiness: {
      canBuild: false,
      blockers: [
        {
          id: 'hard_requirement:image_source_missing',
          category: 'hard_requirement',
          field: 'image_source',
          questionId: '',
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: 'Image source required.'
        }
      ],
      resolvedFields: ['inspection_object', 'task_type', 'acceptance_criteria'],
      remainingFields: ['image_source'],
      primaryMessage: 'Image source required.',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;

  panel._selectPlanQuestionOption('input_source', 'camera');

  assert.equal(panel.planQuestionAnswers.input_source.field, 'image_source');
  assert.equal(plan.executable, true);
  assert.equal(plan.buildReadiness.remainingFields.includes('image_source'), false);
});

test('V2 answer overlay does not invent blockers from stale legacy strings', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    blockingReasons: ['hard_requirement:image_source_missing'],
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: 'Plan ready.',
      contractVersion: 'v2'
    }
  }));

  panel.pendingVisionPlan = plan;
  panel._refreshPlanEffectiveBuildReadiness(plan);

  assert.equal(plan.executable, true);
  assert.deepEqual(plan.buildReadiness.blockers, []);
});

test('V2 answer overlay does not downgrade unmatched blocker', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel._renderPlanWorkspace = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    clarificationQuestions: [
      {
        id: 'input_source',
        field: 'input_source',
        title: 'Image source',
        defaultValue: 'camera',
        options: [{ value: 'camera', label: 'Camera', recommended: true }]
      },
      {
        id: 'output_target',
        field: 'output_target',
        title: 'Output target',
        defaultValue: 'business_system_output',
        options: [{ value: 'business_system_output', label: 'MES', recommended: true }]
      }
    ],
    buildReadiness: {
      canBuild: false,
      blockers: [
        {
          id: 'hard_requirement:image_source_missing',
          category: 'hard_requirement',
          field: 'image_source',
          questionId: 'input_source',
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: 'Image source required.'
        },
        {
          id: 'hard_requirement:output_target_missing',
          category: 'hard_requirement',
          field: 'output_target',
          questionId: 'output_target',
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: 'Output target required.'
        }
      ],
      resolvedFields: ['inspection_object', 'task_type', 'acceptance_criteria'],
      remainingFields: ['image_source', 'output_target'],
      primaryMessage: 'Two blockers remain.',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;

  panel._selectPlanQuestionOption('input_source', 'camera');

  assert.equal(plan.executable, false);
  assert.deepEqual(
    plan.buildReadiness.blockers.filter(blocker => blocker.blocksBuild).map(blocker => blocker.id),
    ['hard_requirement:output_target_missing']
  );
});

test('V2 answer overlay cannot resolve safety blocker', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel._renderPlanWorkspace = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    clarificationQuestions: [
      {
        id: 'safety_ack',
        field: 'safety_ack',
        title: 'Safety acknowledgement',
        defaultValue: 'acknowledged',
        options: [{ value: 'acknowledged', label: 'Acknowledge', recommended: true }]
      }
    ],
    buildReadiness: {
      canBuild: false,
      blockers: [
        {
          id: 'safety_blocker:unsafe_operation',
          category: 'safety_blocker',
          field: '',
          questionId: 'safety_ack',
          blocksBuild: true,
          resolutionMode: 'non_blocking',
          publicLabel: 'Safety review required.'
        }
      ],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: 'Safety review required.',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;

  assert.equal(panel._canBuildPlanWithRecommendedAnswers(plan), false);
  panel._selectPlanQuestionOption('safety_ack', 'acknowledged');

  assert.equal(plan.executable, false);
  assert.equal(plan.buildReadiness.blockers[0].category, 'safety_blocker');
  assert.equal(plan.buildReadiness.blockers[0].blocksBuild, true);
});

test('Plan external output matching treats rapid and capital as local fragments', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });

  assert.equal(panel._planRequestsExternalOutput({ goal: 'rapid inspection of capital letters' }), false);
  assert.equal(panel._planRequestsExternalOutput({ goal: 'capital letter OCR inspection' }), false);
  assert.equal(panel._planRequestsExternalOutput({ goal: 'ApiInspection local OCR workflow' }), false);
  assert.equal(panel._planRequestsExternalOutput({ goal: '检测连接器对接到位' }), false);
  assert.equal(panel._planRequestsExternalOutput({ goal: 'external housing scratch inspection' }), false);
  assert.equal(panel._planRequestsExternalOutput({ goal: '外部标签缺失检测' }), false);
  assert.equal(panel._planRequestsExternalOutput(
    { goal: '外部壳体连接器对接到位检测' },
    '',
    { outputTarget: 'local_result_payload' }
  ), false);
  assert.equal(panel._planRequestsExternalOutput({ goal: 'send result to MES' }), true);
  assert.equal(panel._planRequestsExternalOutput({ goal: 'send result to HTTP API endpoint' }), true);
  assert.equal(panel._planRequestsExternalOutput({ goal: '检测结果输出到 MES' }), true);
  assert.equal(panel._planRequestsExternalOutput({ goal: 'OK NG 结果写入 PLC' }), true);
  assert.equal(panel._planRequestsExternalOutput({ goal: '调用 HTTP API 推送检测结果' }), true);
  assert.equal(panel._planRequestsExternalOutput({ intent: 'classification' }, '', { taskType: 'plc_output' }), true);
  assert.equal(panel._planRequestsExternalOutput({
    goal: 'classify apples',
    plcOutputPolicy: 'PLC disabled; local ResultOutput first; 不写入 PLC; 不对接业务系统'
  }), false);
});

test('Local output target legacy blocker does not disable Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-btn-start-build-inline': inlineBuildButton
  });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    goal: '构建用于超市苹果类别识别的视觉分类流程',
    intent: 'classification',
    canBuild: false,
    blockingReasons: ['strategy_confirmation:output_target_missing'],
    clarificationQuestions: [
      {
        id: 'output_target',
        field: 'output_target',
        title: '输出目标',
        defaultValue: 'local_result_payload',
        options: [
          {
            value: 'local_result_payload',
            label: '本地结构化结果输出',
            recommended: true
          }
        ]
      }
    ],
    requirementMaturity: {
      maturity: 'actionable',
      taskType: 'classification',
      canPlan: true,
      canBuild: true,
      objectSignals: ['apple'],
      taskSignals: ['classification'],
      missingFields: [],
      blockingReasons: [],
      publicReason: 'Hard facts are ready.'
    },
    semanticExtraction: {
      isVisionRequest: true,
      intent: 'new_flow',
      source: 'model',
      taskType: 'classification',
      confidence: 0.8,
      taskTypeConfidence: 0.8,
      inspectionObject: 'apple',
      imageSource: 'camera',
      okCondition: 'apple category identified',
      ngCondition: '',
      outputTarget: '',
      missingFields: []
    }
  }));
  panel.pendingVisionPlan = plan;

  panel._updatePlanBuildActionState();

  assert.equal(plan.executable, true);
  assert.equal(inlineBuildButton.disabled, false);
  assert.equal(plan.buildReadiness.canBuild, true);
  assert.equal(plan.buildReadiness.blockers[0].category, 'contract_warning');
  assert.equal(plan.buildReadiness.blockers[0].blocksBuild, false);
});

test('External output target blocker shows concrete label and recommended output enables Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-btn-start-build-inline': inlineBuildButton
  });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    goal: '构建用于超市苹果类别识别并对接 MES 的视觉分类流程',
    intent: 'classification',
    canBuild: false,
    blockingReasons: ['strategy_confirmation:output_target_missing'],
    clarificationQuestions: [
      {
        id: 'output_target',
        field: 'output_target',
        title: '输出目标',
        why: '选择结果输出目标。',
        defaultValue: 'business_system_output',
        defaultAssumption: '输出到业务系统。',
        impact: '接口信息保持待配置。',
        options: [
          {
            value: 'local_result_payload',
            label: '本地结构化结果输出',
            recommended: false,
            description: '输出类别标签、置信度和判定结果。',
            impact: '适合作为流程草案输出目标。'
          },
          {
            value: 'business_system_output',
            label: '对接业务系统',
            recommended: true,
            description: '后续对接 MES/ERP。',
            impact: '接口信息保持待配置。'
          }
        ]
      }
    ],
    requirementMaturity: {
      maturity: 'actionable',
      taskType: 'classification',
      canPlan: true,
      canBuild: true,
      objectSignals: ['apple'],
      taskSignals: ['classification'],
      missingFields: [],
      blockingReasons: [],
      publicReason: 'Hard facts are ready; output target remains.'
    },
    semanticExtraction: {
      isVisionRequest: true,
      intent: 'new_flow',
      source: 'model',
      taskType: 'classification',
      confidence: 0.8,
      taskTypeConfidence: 0.8,
      inspectionObject: 'apple',
      imageSource: 'camera',
      okCondition: 'apple category identified',
      ngCondition: '',
      outputTarget: '',
      missingFields: []
    },
    buildReadiness: {
      canBuild: false,
      blockers: [
        {
          id: 'hard_requirement:output_target_missing',
          category: 'hard_requirement',
          field: 'output_target',
          questionId: 'output_target',
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: '请选择输出目标。'
        }
      ],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: ['output_target'],
      primaryMessage: '请选择输出目标。',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  panel._updatePlanBuildActionState();
  assert.equal(plan.executable, false);
  assert.match(panel._getPlanBuildBlockedReason(plan), /请选择输出目标/);
  assert.equal(panel._getPlanBuildActionState(plan).canAcceptRecommended, false);

  assert.equal(await panel._startBuildFromCurrentPlan({ acceptedRecommended: true }), false);
  assert.equal(captured, null);
  panel._selectPlanQuestionOption('output_target', 'local_result_payload');
  assert.equal(await panel._startBuildFromCurrentPlan(), true);
  assert.equal(captured.buildFromPlan.confirmedAnswers[0].field, 'output_target');
  assert.equal(captured.buildFromPlan.confirmedAnswers[0].value, 'local_result_payload');
});

test('Backend Plan without explicit canBuild is not executable by default', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  const planActionButtons = [createFakeButton()];
  const overview = createFakeElement();
  const planWorkspace = createFakeElement();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': overview,
      '#ai-plan-workspace': planWorkspace,
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': inlineBuildButton
    },
    { '.ai-plan-action': planActionButtons }
  );
  const rawPlan = backendPlanResult({
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'unknown',
      canBuild: false,
      missingFields: ['inspection_object', 'task_type'],
      blockingReasons: ['inspection_object_missing'],
      publicReason: '需求仍缺少检测对象或任务类型，暂不能构建。'
    }
  });
  delete rawPlan.canBuild;

  const plan = panel._normalizeBackendPlanResult(rawPlan);
  panel.pendingVisionPlan = plan;
  panel._renderAgentWorkspaceOverview();
  panel._renderPlanWorkspace(plan);

  assert.equal(plan.executable, false);
  assert.equal(plan.requirementMaturity.maturity, 'ambiguous');
  assert.deepEqual(plan.requirementMaturity.missingFields, ['inspection_object', 'task_type']);
  assert.match(overview.innerHTML, /可构建：否/);
  assert.equal(inlineBuildButton.disabled, true);
  assert.equal(inlineBuildButton.getAttribute('aria-disabled'), 'true');
  assert.ok(planActionButtons.every(button => button.disabled));
});

test('Backend Plan with canBuild true but missing RequirementMaturity remains non-executable', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  const planActionButtons = [createFakeButton()];
  const overview = createFakeElement();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': overview,
      '#ai-plan-workspace': createFakeElement(),
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': inlineBuildButton
    },
    { '.ai-plan-action': planActionButtons }
  );
  const rawPlan = backendPlanResult({ canBuild: true });
  delete rawPlan.requirementMaturity;
  delete rawPlan.RequirementMaturity;

  const plan = panel._normalizeBackendPlanResult(rawPlan);
  panel.pendingVisionPlan = plan;
  panel._renderAgentWorkspaceOverview();
  panel._updatePlanBuildActionState();

  assert.equal(plan.executable, false);
  assert.equal(plan.requirementMaturity.canBuild, false);
  assert.match(overview.innerHTML, /可构建：否/);
  assert.equal(inlineBuildButton.disabled, true);
  assert.ok(planActionButtons.every(button => button.disabled));
});

test('Backend Plan can be plannable while Build remains disabled', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  const overview = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': overview,
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-btn-start-build-inline': inlineBuildButton
  });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    goal: '检测目标是外星人，识别内容是额头上的第三只竖眼',
    canPlan: true,
    canBuild: false,
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'classification',
      canPlan: true,
      canBuild: false,
      objectSignals: ['外星人'],
      taskSignals: ['额头上的第三只竖眼'],
      missingFields: ['image_source', 'acceptance_criteria', 'model_or_rule_strategy'],
      blockingReasons: ['model_or_rule_strategy_missing'],
      publicReason: '需求已足够进入规划，但构建前仍需补充图像来源、判定标准或实现策略。'
    }
  }));

  panel.pendingVisionPlan = plan;
  panel._renderAgentWorkspaceOverview();
  panel._updatePlanBuildActionState();

  assert.equal(plan.canPlan, true);
  assert.equal(plan.executable, false);
  assert.match(overview.innerHTML, /可规划：是/);
  assert.match(overview.innerHTML, /可构建：否/);
  assert.equal(inlineBuildButton.disabled, true);
});

test('pending recommended Plan option stays visible and cannot unblock Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  const planActionButton = createFakeButton();
  const buildStatus = createFakeElement();
  const planWorkspace = createFakeElement();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': createFakeElement(),
      '#ai-plan-workspace': planWorkspace,
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': inlineBuildButton,
      '#ai-plan-build-status': buildStatus
    },
    { '.ai-plan-action': [planActionButton] }
  );
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    clarificationQuestions: [
      {
        id: 'image_source',
        field: 'image_source',
        title: '图像来源如何确认？',
        defaultValue: 'camera_pending',
        defaultAssumption: '暂无安全默认值，需确认；推荐保持待确认。',
        options: [
          {
            value: 'camera_pending',
            label: '保持图像来源待确认',
            recommended: true,
            description: '不猜测相机或文件路径。',
            impact: '不会解除构建阻断。'
          },
          {
            value: 'file_sample',
            label: '本地图像样本',
            recommended: false,
            description: '使用文件样本继续。',
            impact: '确认后可解除该字段阻断。'
          }
        ]
      }
    ],
    buildReadiness: {
      canBuild: false,
      blockers: [
        {
          id: 'hard_requirement:image_source',
          category: 'hard_requirement',
          field: 'image_source',
          questionId: 'image_source',
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: '图像来源待确认'
        }
      ],
      resolvedFields: ['inspection_object', 'task_type', 'acceptance_criteria'],
      remainingFields: ['image_source'],
      primaryMessage: '图像来源待确认',
      contractVersion: 'v2'
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      missingFields: ['image_source'],
      blockingReasons: ['image_source_missing']
    }
  }));

  panel.pendingVisionPlan = plan;
  panel._renderPlanWorkspace(plan);

  const question = plan.questions[0];
  assert.equal(question.options[0].value, 'camera_pending');
  assert.equal(question.options[0].recommended, true);
  assert.equal(question.options[1].value, 'file_sample');
  assert.equal(question.options[1].recommended, false);
  assert.equal(question.defaultValue, 'camera_pending');
  assert.match(planWorkspace.innerHTML, /保持图像来源待确认/);
  assert.match(planWorkspace.innerHTML, /本地图像样本/);
  assert.equal(panel._acceptRecommendedPlanAnswers(plan), false);
  assert.equal(panel._refreshPlanEffectiveBuildReadiness(plan, { acceptedRecommended: true }), false);
  assert.equal(panel._getPlanBuildActionState(plan).canStart, false);
  assert.equal(inlineBuildButton.disabled, true);

  panel._selectPlanQuestionOption('image_source', 'file_sample');
  assert.equal(panel._refreshPlanEffectiveBuildReadiness(plan), true);
  assert.equal(panel._getPlanBuildActionState(plan).canStart, true);
});

function pendingFieldPlan(panel, {
  questionId,
  field,
  pendingValue,
  concreteValue,
  pendingAnswerEffect,
  concreteAnswerEffect,
  blockerCategory = 'hard_requirement',
  blockingReason = `${field}_missing`
}) {
  const labels = {
    image_source: 'Image source',
    acceptance_criteria: 'Acceptance criteria',
    algorithm_strategy: 'Algorithm strategy'
  };
  const resolved = ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria']
    .filter(item => item !== field);
  return panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    clarificationQuestions: [
      {
        id: questionId,
        field,
        title: `${labels[field] || field} confirmation`,
        why: `${labels[field] || field} must be confirmed before Build.`,
        defaultValue: pendingValue,
        defaultAssumption: 'Keep this field pending until the user confirms it.',
        impact: 'Pending selections do not unblock Build.',
        options: [
          {
            value: pendingValue,
            label: `Keep ${field} pending`,
            recommended: true,
            answerEffect: pendingAnswerEffect,
            description: 'Do not guess this requirement.',
            impact: 'This selection keeps Build blocked.'
          },
          {
            value: concreteValue,
            label: `Confirm ${field}`,
            recommended: false,
            answerEffect: concreteAnswerEffect,
            description: 'Use this confirmed answer.',
            impact: 'This can resolve the field.'
          }
        ]
      }
    ],
    buildReadiness: {
      canBuild: false,
      blockers: [
        {
          id: `${blockerCategory}:${blockingReason}`,
          category: blockerCategory,
          field,
          questionId,
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: `${labels[field] || field} pending`
        }
      ],
      resolvedFields: resolved,
      remainingFields: [field],
      primaryMessage: `${labels[field] || field} pending`,
      contractVersion: 'v2'
    },
    blockingReasons: [`${blockerCategory}:${blockingReason}`],
    semanticExtraction: {
      isVisionRequest: true,
      source: 'rule_fallback',
      taskType: 'surface_defect',
      confidence: 0.8,
      taskTypeConfidence: 0.8,
      inspectionObject: 'logo area',
      defectType: 'appearance defect',
      imageSource: field === 'image_source' ? '' : 'camera',
      okCondition: field === 'acceptance_criteria' ? '' : 'no visible defect',
      ngCondition: '',
      outputTarget: 'OK/NG result',
      missingFields: [field]
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      objectSignals: ['logo area'],
      taskSignals: ['appearance defect'],
      missingFields: [field],
      blockingReasons: [blockingReason],
      publicReason: `${labels[field] || field} pending`
    }
  }));
}

function optionMarkup(html, value) {
  const escaped = value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = String(html || '').match(new RegExp(`<button(?:(?!</button>)[\\s\\S])*?data-plan-question-option="${escaped}"(?:(?!</button>)[\\s\\S])*?</button>`));
  assert.ok(match, `missing option ${value}`);
  return match[0];
}

test('pending recommended Plan option is a UI selection without becoming an effective answer', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  const planActionButton = createFakeButton();
  const planWorkspace = createFakeElement();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': createFakeElement(),
      '#ai-plan-workspace': planWorkspace,
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': inlineBuildButton,
      '#ai-plan-build-status': createFakeElement()
    },
    { '.ai-plan-action': [planActionButton] }
  );
  const plan = pendingFieldPlan(panel, {
    questionId: 'image_source',
    field: 'image_source',
    pendingValue: 'camera_pending',
    concreteValue: 'file_sample'
  });
  panel.pendingVisionPlan = plan;
  panel._renderPlanWorkspace(plan);

  panel._selectPlanQuestionOption('image_source', 'camera_pending');

  assert.equal(panel.planQuestionSelections.image_source, 'camera_pending');
  assert.equal(Object.prototype.hasOwnProperty.call(panel.planQuestionAnswers, 'image_source'), false);
  assert.equal(plan.buildReadiness.canBuild, false);
  assert.equal(plan.executable, false);
  assert.equal(plan.buildReadiness.resolvedFields.includes('image_source'), false);
  assert.equal(panel._getPlanBuildActionState(plan).canStart, false);
  assert.equal(inlineBuildButton.disabled, true);
  let pending = optionMarkup(planWorkspace.innerHTML, 'camera_pending');
  let concrete = optionMarkup(planWorkspace.innerHTML, 'file_sample');
  assert.match(pending, /is-selected/);
  assert.match(pending, /is-recommended/);
  assert.match(pending, /aria-pressed="true"/);
  assert.match(pending, /建议暂缓/);
  assert.doesNotMatch(concrete, /is-selected/);
  assert.match(concrete, /可选方案/);
  assert.match(planWorkspace.innerHTML, /已选择暂缓确认，该字段仍会阻断构建。/);

  panel._selectPlanQuestionOption('image_source', 'file_sample');

  assert.equal(panel.planQuestionSelections.image_source, 'file_sample');
  assert.equal(panel.planQuestionAnswers.image_source.field, 'image_source');
  assert.equal(panel.planQuestionAnswers.image_source.value, 'file_sample');
  assert.equal(panel.planQuestionAnswers.image_source.origin, 'explicit_user_selection');
  assert.equal(plan.buildReadiness.canBuild, true);
  pending = optionMarkup(planWorkspace.innerHTML, 'camera_pending');
  concrete = optionMarkup(planWorkspace.innerHTML, 'file_sample');
  assert.doesNotMatch(pending, /is-selected/);
  assert.match(pending, /aria-pressed="false"/);
  assert.match(concrete, /is-selected/);
  assert.match(concrete, /aria-pressed="true"/);
  assert.match(planWorkspace.innerHTML, /已确认，该选择可用于构建判断。/);

  panel._selectPlanQuestionOption('image_source', 'camera_pending');

  assert.equal(panel.planQuestionSelections.image_source, 'camera_pending');
  assert.equal(Object.prototype.hasOwnProperty.call(panel.planQuestionAnswers, 'image_source'), false);
  assert.equal(plan.buildReadiness.canBuild, false);
  assert.equal(plan.executable, false);
  pending = optionMarkup(planWorkspace.innerHTML, 'camera_pending');
  assert.match(pending, /is-selected/);
  assert.match(pending, /aria-pressed="true"/);
  panel._renderPlanWorkspace(plan);
  pending = optionMarkup(planWorkspace.innerHTML, 'camera_pending');
  assert.match(pending, /is-selected/);
  assert.match(pending, /aria-pressed="true"/);
  panel._customInputPlanQuestion('image_source', 'pending');
  assert.equal(panel.planQuestionSelections.image_source, 'camera_pending');
  assert.equal(Object.prototype.hasOwnProperty.call(panel.planQuestionAnswers, 'image_source'), false);

  panel._customInputPlanQuestion('image_source', 'line camera 1');
  assert.equal(panel.planQuestionSelections.image_source, 'line camera 1');
  assert.equal(panel.planQuestionAnswers.image_source.value, 'line camera 1');
  panel._selectPlanQuestionOption('image_source', 'camera_pending');
  assert.equal(panel.planQuestionSelections.image_source, 'camera_pending');
  assert.equal(Object.prototype.hasOwnProperty.call(panel.planQuestionAnswers, 'image_source'), false);
});

test('Plan option AnswerEffect controls labels feedback and informational no-op', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': createFakeElement(),
      '#ai-plan-workspace': planWorkspace,
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': createFakeButton(),
      '#ai-plan-build-status': createFakeElement()
    },
    { '.ai-plan-action': [createFakeButton()] }
  );
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    clarificationQuestions: [
      {
        id: 'image_source',
        field: 'image_source',
        title: 'Image source',
        why: 'Confirm image source.',
        defaultValue: 'camera_pending',
        defaultAssumption: 'Keep pending until confirmed.',
        impact: 'Controls Build readiness.',
        options: [
          { value: 'file_sample', label: 'File sample', recommended: true, answerEffect: 'resolve_field', recommendationReason: 'Safe public sample route.', description: 'Use sample.', impact: 'Resolves input.' },
          { value: 'camera_pending', label: 'Keep pending', recommended: true, answerEffect: 'defer', description: 'Do not decide yet.', impact: 'Still blocks Build.' },
          { value: 'camera_note', label: 'Camera note', recommended: false, answerEffect: 'informational', description: 'Read-only detail.', impact: 'No answer.' }
        ]
      }
    ],
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'hard_requirement:image_source_missing', category: 'hard_requirement', field: 'image_source', questionId: 'image_source', blocksBuild: true, resolutionMode: 'answer_question', publicLabel: 'Image source pending' }],
      resolvedFields: ['inspection_object', 'task_type', 'acceptance_criteria'],
      remainingFields: ['image_source'],
      primaryMessage: 'Image source pending',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;
  panel._renderPlanWorkspace(plan);

  assert.match(optionMarkup(planWorkspace.innerHTML, 'file_sample'), /推荐方案/);
  assert.match(optionMarkup(planWorkspace.innerHTML, 'camera_pending'), /建议暂缓/);
  assert.match(optionMarkup(planWorkspace.innerHTML, 'camera_note'), /仅供阅读/);

  panel._selectPlanQuestionOption('image_source', 'file_sample');
  assert.equal(panel.planQuestionAnswers.image_source.value, 'file_sample');
  assert.match(planWorkspace.innerHTML, /已确认，该选择可用于构建判断。/);

  panel._selectPlanQuestionOption('image_source', 'camera_pending');
  assert.equal(panel.planQuestionSelections.image_source, 'camera_pending');
  assert.equal(Object.prototype.hasOwnProperty.call(panel.planQuestionAnswers, 'image_source'), false);
  assert.match(planWorkspace.innerHTML, /已选择暂缓确认，该字段仍会阻断构建。/);

  panel._selectPlanQuestionOption('image_source', 'camera_note');
  assert.equal(panel.planQuestionSelections.image_source, 'camera_pending');
  assert.equal(Object.prototype.hasOwnProperty.call(panel.planQuestionAnswers, 'image_source'), false);
});

test('Plan resource pending drives strict and draft CTA without deployment-ready copy', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  const mainButton = createFakeButton();
  const buildStatus = createFakeElement();
  const planWorkspace = createFakeElement();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': createFakeElement(),
      '#ai-plan-workspace': planWorkspace,
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': inlineBuildButton,
      '#ai-plan-build-status': buildStatus,
      '#ai-btn-start-build': mainButton
    },
    { '.ai-plan-action': [mainButton] }
  );
  const draftPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    requirementMode: 'draft',
    canBuild: true,
    clarificationQuestions: [
      {
        id: 'image_source',
        field: 'image_source',
        title: 'Image source',
        why: 'Confirm input route.',
        defaultValue: 'station_camera',
        options: [{ value: 'station_camera', label: 'Station camera', recommended: true, answerEffect: 'resolve_field' }]
      }
    ],
    buildReadiness: {
      canBuild: true,
      blockers: [{ id: 'resource_pending:camera_binding', category: 'resource_pending', field: 'image_source', questionId: 'image_source', blocksBuild: false, resolutionMode: 'provide_resource', publicLabel: '可以生成可编辑草稿，部署前仍需绑定资源。' }],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: '可以生成可编辑草稿，部署前仍需绑定资源。',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = draftPlan;
  panel._setRequirementMode('draft', { silent: true });
  panel._renderPlanWorkspace(draftPlan);

  const action = panel._getPlanBuildActionState(draftPlan);
  assert.equal(action.canStart, true);
  assert.equal(action.label, '按当前方案生成可编辑草稿');
  assert.match(planWorkspace.innerHTML, /可编辑草稿模式/);
  assert.match(planWorkspace.innerHTML, /后补资源/);
  assert.match(optionMarkup(planWorkspace.innerHTML, 'station_camera'), /资源待后补/);
  assert.doesNotMatch(planWorkspace.innerHTML, /部署就绪/);

  const strictPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    requirementMode: 'strict',
    canBuild: false,
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'resource_pending:camera_binding', category: 'resource_pending', field: 'image_source', questionId: 'image_source', blocksBuild: true, resolutionMode: 'provide_resource', publicLabel: '资源仍待绑定，当前模式下不能开始构建。' }],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: '资源仍待绑定，当前模式下不能开始构建。',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = strictPlan;
  panel._setRequirementMode('strict', { silent: true });
  panel._renderPlanWorkspace(strictPlan);

  const blocked = panel._getPlanBuildActionState(strictPlan);
  assert.equal(blocked.canStart, false);
  assert.equal(blocked.label, '仍需补齐资源 1 项');
  assert.match(planWorkspace.innerHTML, /严格确认模式/);
  assert.match(buildStatus.textContent, /资源仍待绑定/);
});

test('Plan requirement mode is scoped to plan identity and never restored from localStorage', async () => {
  const { AiPanel } = await loadAiPanel();
  localStorage.setItem('cv_ai_requirement_mode', 'draft');
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });

  assert.equal(panel._loadRequirementMode(), 'strict');
  const planA = panel._normalizeBackendPlanResult(backendPlanResult({
    planId: 'plan_mode_a',
    planHash: 'sha256:mode-a',
    canBuild: true
  }));
  panel.pendingVisionPlan = planA;
  panel._setRequirementMode('draft', { silent: true });
  assert.equal(panel.requirementMode, 'draft');
  assert.equal(planA.planId, 'plan_mode_a');

  const planB = panel._normalizeBackendPlanResult(backendPlanResult({
    planId: 'plan_mode_b',
    planHash: 'sha256:mode-b',
    canBuild: true
  }));
  panel.pendingVisionPlan = planB;
  panel._activatePlanIdentity(planB);

  assert.equal(panel.requirementMode, 'strict');
  assert.equal(planB.requirementMode, 'strict');
  assert.equal(planA.planHash, 'sha256:mode-a');
  assert.equal(planB.planHash, 'sha256:mode-b');
});

test('Plan readiness preview accepts only latest revision and fails closed on preview error', async () => {
  const { AiPanel } = await loadAiPanel();
  const first = deferred();
  const second = deferred();
  const calls = [];
  const panel = createPanel(AiPanel, {
    developer: false,
    enabled: true,
    useProductionPreview: true,
    planReadinessPreview: request => {
      calls.push(request);
      return calls.length === 1 ? first.promise : second.promise;
    }
  });
  const inlineBuildButton = createFakeButton();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': createFakeElement(),
      '#ai-plan-workspace': createFakeElement(),
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': inlineBuildButton,
      '#ai-plan-build-status': createFakeElement()
    },
    { '.ai-plan-action': [createFakeButton()] }
  );
  const plan = pendingFieldPlan(panel, {
    questionId: 'image_source',
    field: 'image_source',
    pendingValue: 'camera_pending',
    concreteValue: 'file_sample'
  });
  panel.pendingVisionPlan = plan;

  panel._selectPlanQuestionOption('image_source', 'camera_pending');
  assert.equal(panel.previewState, 'validating');
  assert.equal(panel._getPlanBuildActionState(plan).canStart, false);
  assert.equal(inlineBuildButton.disabled, true);
  panel._selectPlanQuestionOption('image_source', 'file_sample');
  assert.equal(calls.length, 2);

  first.resolve({
    planId: plan.planId,
    planHash: plan.planHash,
    requirementMode: 'strict',
    answerRevision: calls[0].answerRevision,
    acceptedAnswers: [],
    answerSetFingerprint: 'stale',
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'hard_requirement:image_source_missing', category: 'hard_requirement', field: 'image_source', questionId: 'image_source', blocksBuild: true, resolutionMode: 'answer_question' }],
      resolvedFields: [],
      remainingFields: ['image_source'],
      primaryMessage: 'stale',
      contractVersion: 'v2'
    },
    pendingConfirmationCount: 1,
    resourcePendingCount: 0,
    hardBlockerCount: 1,
    metadataOnly: true
  });
  await flushAsync();
  assert.equal(panel.previewState, 'validating');
  assert.equal(panel.planQuestionSelections.image_source, 'file_sample');

  second.reject(new Error('network down'));
  await flushAsync();
  assert.equal(panel.previewState, 'failed');
  assert.equal(panel._getPlanBuildActionState(plan).canStart, false);
  assert.equal(panel.planQuestionAnswers.image_source.value, 'file_sample');
});

test('Plan main CTA ignores model NextAction and uses canonical readiness', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const mainButton = createFakeButton();
  const planWorkspace = createFakeElement();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': createFakeElement(),
      '#ai-plan-workspace': planWorkspace,
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': createFakeButton(),
      '#ai-plan-build-status': createFakeElement(),
      '#ai-btn-start-build': mainButton
    },
    { '.ai-plan-action': [mainButton] }
  );
  const plan = pendingFieldPlan(panel, {
    questionId: 'image_source',
    field: 'image_source',
    pendingValue: 'camera_pending',
    concreteValue: 'file_sample'
  });
  plan.nextAction = 'Deploy now from model advice';
  panel.pendingVisionPlan = plan;
  panel._renderPlanWorkspace(plan);

  assert.match(planWorkspace.innerHTML, /模型 NextAction/);
  assert.match(planWorkspace.innerHTML, /Deploy now from model advice/);
  assert.equal(panel._getPlanBuildActionState(plan).label, '仍需人工确认 1 项');
  assert.notEqual(mainButton.textContent, 'Deploy now from model advice');
});

test('fallback pending recommendations stay UI-only for all canonical pending question types', async () => {
  const { AiPanel } = await loadAiPanel();
  const cases = [
    {
      questionId: 'image_source',
      field: 'image_source',
      pendingValue: 'camera_pending',
      concreteValue: 'file_sample',
      blockerCategory: 'hard_requirement',
      blockingReason: 'image_source_missing'
    },
    {
      questionId: 'acceptance_criteria',
      field: 'acceptance_criteria',
      pendingValue: 'ok_ng_pending',
      concreteValue: 'defect_is_ng',
      blockerCategory: 'hard_requirement',
      blockingReason: 'acceptance_criteria_missing'
    },
    {
      questionId: 'model_or_rule_strategy',
      field: 'algorithm_strategy',
      pendingValue: 'strategy_pending',
      concreteValue: 'traditional_rule',
      blockerCategory: 'strategy_confirmation',
      blockingReason: 'model_or_rule_strategy_missing'
    }
  ];

  for (const item of cases) {
    const panel = createPanel(AiPanel, { developer: false, enabled: true });
    const planWorkspace = createFakeElement();
    panel.container = createContainer(
      {
        '#ai-agent-workspace-overview': createFakeElement(),
        '#ai-plan-workspace': planWorkspace,
        '#ai-build-workspace': createFakeElement(),
        '#ai-result-status-note': createFakeElement(),
        '#ai-btn-start-build-inline': createFakeButton(),
        '#ai-plan-build-status': createFakeElement()
      },
      { '.ai-plan-action': [createFakeButton()] }
    );
    const plan = pendingFieldPlan(panel, item);
    panel.pendingVisionPlan = plan;
    panel._renderPlanWorkspace(plan);

    assert.equal(panel._acceptRecommendedPlanAnswers(plan), false);
    assert.deepEqual(panel.planQuestionSelections, {});
    assert.deepEqual(panel.planQuestionAnswers, {});
    assert.equal(panel._refreshPlanEffectiveBuildReadiness(plan, { acceptedRecommended: true }), false);
    assert.equal(plan.buildReadiness.resolvedFields.includes(item.field), false);

    panel._selectPlanQuestionOption(item.questionId, item.pendingValue);
    assert.equal(panel.planQuestionSelections[item.questionId], item.pendingValue);
    assert.equal(Object.prototype.hasOwnProperty.call(panel.planQuestionAnswers, item.field), false);
    assert.equal(plan.buildReadiness.canBuild, false);
    assert.equal(plan.buildReadiness.resolvedFields.includes(item.field), false);
    assert.match(optionMarkup(planWorkspace.innerHTML, item.pendingValue), /is-selected/);
    assert.match(optionMarkup(planWorkspace.innerHTML, item.pendingValue), /is-recommended/);
    assert.match(optionMarkup(planWorkspace.innerHTML, item.pendingValue), /aria-pressed="true"/);

    panel._selectPlanQuestionOption(item.questionId, item.concreteValue);
    assert.equal(panel.planQuestionSelections[item.questionId], item.concreteValue);
    assert.equal(panel.planQuestionAnswers[item.field].value, item.concreteValue);
    assert.equal(panel.planQuestionAnswers[item.field].origin, 'explicit_user_selection');
    assert.match(optionMarkup(planWorkspace.innerHTML, item.concreteValue), /is-selected/);

    panel._selectPlanQuestionOption(item.questionId, item.pendingValue);
    assert.equal(panel.planQuestionSelections[item.questionId], item.pendingValue);
    assert.equal(Object.prototype.hasOwnProperty.call(panel.planQuestionAnswers, item.field), false);
    assert.equal(plan.buildReadiness.canBuild, false);
  }
});

test('Plan with pending image source and acquisition route can start editable draft', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  const planActionButton = createFakeButton();
  const buildStatus = createFakeElement();
  const planWorkspace = createFakeElement();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': createFakeElement(),
      '#ai-plan-workspace': planWorkspace,
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': inlineBuildButton,
      '#ai-plan-build-status': buildStatus
    },
    { '.ai-plan-action': [planActionButton] }
  );
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    requirementMode: 'draft',
    goal: 'logo appearance defect workflow',
    canBuild: false,
    recommendedRoute: {
      routeId: 'surface_defect_with_pending_camera',
      title: 'Surface defect route',
      summary: 'Acquisition placeholder plus defect judgment.',
      operators: ['ImageAcquisition', 'SurfaceDefectDetection', 'ResultJudgment', 'ResultOutput']
    },
    blockingReasons: ['hard_requirement:image_source_missing'],
    semanticExtraction: {
      isVisionRequest: true,
      source: 'model',
      taskType: 'surface_defect',
      confidence: 0.9,
      taskTypeConfidence: 0.9,
      inspectionObject: 'steering wheel logo area',
      defectType: 'appearance defect',
      imageSource: '',
      okCondition: 'logo area has no visible defect',
      ngCondition: 'scratch, dirt, deformation, or missing print',
      outputTarget: 'OK/NG result',
      missingFields: ['image_source']
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      objectSignals: ['logo area'],
      taskSignals: ['appearance defect'],
      missingFields: ['image_source'],
      blockingReasons: ['image_source_missing'],
      publicReason: '图像来源需要在部署前绑定。'
    }
  }));

  panel.pendingVisionPlan = plan;
  panel._setRequirementMode('draft', { silent: true });
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };
  panel._renderPlanWorkspace(plan);

  assert.equal(plan.executable, true);
  assert.equal(inlineBuildButton.disabled, false);
  assert.equal(planActionButton.disabled, false);
  assert.equal(await panel._startBuildFromCurrentPlan(), true);
  assert.equal(captured.skipPlan, true);
  assert.equal(captured.buildFromPlan.planId, plan.planId);
  assert.equal(captured.buildFromPlan.planHash, plan.planHash);
  assert.equal(buildStatus.textContent, '可编辑草稿可先生成；当前不代表可部署。');
  assert.match(planWorkspace.innerHTML, /按当前方案生成可编辑草稿|可编辑草稿模式/);
  assert.doesNotMatch(planWorkspace.innerHTML, /按推荐方案开始构建/);
});

test('Build clarification preserves pending Plan and confirmed answers', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-result-summary': createFakeElement()
  });
  panel._renderAgentRuntime = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderPlanWorkspace = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._renderManualRetryBanner = () => {};
  panel._renderAssistantClarification = () => {};
  panel._setAssistantTurnStatus = (turn, status, tone) => {
    turn.status = status;
    turn.tone = tone;
  };
  panel._setAssistantSectionText = (turn, section, text) => {
    turn[section] = text;
  };
  panel._saveSessionId = () => {};
  panel._shouldHandleGenerateTerminalPayload = () => true;
  panel.activeAssistantTurn = { clarificationSection: createFakeElement(), replyBody: createFakeElement() };
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult({
    planId: 'plan_keep',
    planHash: 'sha256:keep-plan'
  }));
  panel._selectPlanQuestionOption('model_or_rule_strategy', 'traditional_rule');
  const planRef = panel.pendingVisionPlan;
  const answerBefore = { ...panel.planQuestionAnswers.model_or_rule_strategy };

  panel._handleResult({
    payload: {
      success: false,
      status: 'clarification_required',
      failureType: 'clarification_required',
      clarificationRequired: true,
      aiExplanation: 'Need one more field.',
      shouldResetPendingPlan: false
    }
  });

  assert.equal(panel.pendingVisionPlan, planRef);
  assert.equal(panel.pendingVisionPlan.planId, 'plan_keep');
  assert.equal(panel.pendingVisionPlan.planHash, 'sha256:keep-plan');
  assert.deepEqual(panel.planQuestionAnswers.model_or_rule_strategy, answerBefore);
  assert.equal(panel.pendingClarificationPayload, null);
});

test('Router local HTTP fallback preserves Pending Plan by default', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._setAssistantTurnStatus = () => {};
  panel._setAssistantSectionText = () => {};
  panel._updateIntentRouterTimeline = () => {};
  panel._renderAgentRuntime = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderPlanWorkspace = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult({
    planId: 'plan_fallback_keep',
    planHash: 'sha256:fallback-keep'
  }));
  panel._selectPlanQuestionOption('model_or_rule_strategy', 'traditional_rule');
  const planRef = panel.pendingVisionPlan;
  const fallback = panel._buildLocalIntentRouterFallback('开始 build', new Error('router offline'));

  panel._handleIntentRouterResult(fallback, {
    routerRequestId: 'ir_fallback',
    turn: {},
    description: '开始 build',
    hint: '',
    userMessage: '开始 build',
    attachmentPaths: [],
    templateSelection: null
  });

  assert.equal(fallback.shouldResetPendingPlan, false);
  assert.equal(panel.pendingVisionPlan, planRef);
  assert.equal(panel.pendingVisionPlan.planHash, 'sha256:fallback-keep');
  assert.equal(panel.planQuestionAnswers.model_or_rule_strategy.value, 'traditional_rule');
});

test('Draft chat start build can enter Build with legal pending fields', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._setAssistantTurnStatus = () => {};
  panel._setAssistantSectionText = () => {};
  panel._updateIntentRouterTimeline = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderPlanWorkspace = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  let started = false;
  panel._startBuildFromCurrentPlan = () => {
    started = true;
    return true;
  };
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    requirementMode: 'draft',
    goal: 'logo appearance defect workflow',
    canBuild: false,
    recommendedRoute: {
      routeId: 'surface_defect_with_pending_camera',
      title: 'Surface defect route',
      summary: 'Acquisition placeholder plus defect judgment.',
      operators: ['ImageAcquisition', 'SurfaceDefectDetection', 'ResultJudgment', 'ResultOutput']
    },
    blockingReasons: ['hard_requirement:image_source_missing'],
    semanticExtraction: {
      isVisionRequest: true,
      source: 'model',
      taskType: 'surface_defect',
      confidence: 0.9,
      taskTypeConfidence: 0.9,
      inspectionObject: 'steering wheel logo area',
      defectType: 'appearance defect',
      imageSource: '',
      okCondition: 'logo area has no visible defect',
      outputTarget: '',
      missingFields: ['image_source', 'acceptance_criteria']
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      objectSignals: ['logo area'],
      taskSignals: ['appearance defect'],
      missingFields: ['image_source', 'acceptance_criteria'],
      blockingReasons: ['image_source_missing'],
      publicReason: 'Image source and acceptance criteria can remain pending in draft.'
    }
  }));
  panel._setRequirementMode('draft', { silent: true });

  panel._handleIntentRouterResult({
    intent: 'build_from_confirmed_plan',
    confidence: 'high',
    shouldOpenPlan: false,
    shouldBuildDirectly: true,
    shouldResetPendingPlan: false,
    canBuild: true,
    needsClarification: false,
    publicReason: 'Draft is ready.',
    remainingPlanFields: ['image_source', 'acceptance_criteria'],
    resolvedPlanFields: ['inspection_object', 'task_type']
  }, {
    routerRequestId: 'ir_build',
    turn: {},
    description: '开始 build',
    hint: '',
    userMessage: '开始 build',
    attachmentPaths: [],
    templateSelection: null
  });

  assert.equal(panel.pendingVisionPlan.executable, true);
  assert.equal(started, true);
});

test('Backend Plan renders semantic extraction slots and keeps them in Build snapshot', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    goal: '检测成熟草莓',
    intent: 'attribute_classification',
    canPlan: true,
    canBuild: false,
    semanticExtraction: {
      isVisionRequest: true,
      intent: 'new_flow',
      taskType: 'attribute_classification',
      confidence: 0.91,
      taskTypeConfidence: 0.88,
      inspectionObject: '草莓',
      targetAttribute: '成熟度/熟透',
      imageSource: '相机',
      okCondition: '草莓熟透则 OK',
      ngCondition: '否则 NG',
      suggestedRoute: '属性分类 / OK-NG 判别路线',
      source: 'model',
      missingFields: [],
      metadataOnly: true
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'attribute_classification',
      canPlan: true,
      canBuild: false,
      objectSignals: ['草莓'],
      taskSignals: ['成熟度'],
      missingFields: ['model_or_rule_strategy'],
      blockingReasons: ['model_or_rule_strategy_missing'],
      publicReason: '语义抽取结果已足够进入规划。'
    }
  }));

  panel.pendingVisionPlan = plan;
  panel._renderPlanWorkspace(plan);
  const snapshot = panel._buildPlanSnapshotForBuild(plan);

  assert.equal(plan.semanticExtraction.taskType, 'attribute_classification');
  assert.match(planWorkspace.innerHTML, /语义抽取/);
  assert.match(planWorkspace.innerHTML, /模型/);
  assert.match(planWorkspace.innerHTML, /属性分类 \/ OK-NG 判别/);
  assert.match(planWorkspace.innerHTML, /草莓/);
  assert.match(planWorkspace.innerHTML, /熟透/);
  assert.match(planWorkspace.innerHTML, /相机/);
  assert.equal(snapshot.semanticExtraction.taskType, 'attribute_classification');
});

test('Backend Plan with explicit canBuild true and actionable maturity enables Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  const planActionButtons = [createFakeButton()];
  const planWorkspace = createFakeElement();
  const overview = createFakeElement();
  panel.container = createContainer(
    {
      '#ai-agent-workspace-overview': overview,
      '#ai-plan-workspace': planWorkspace,
      '#ai-build-workspace': createFakeElement(),
      '#ai-result-status-note': createFakeElement(),
      '#ai-btn-start-build-inline': inlineBuildButton
    },
    { '.ai-plan-action': planActionButtons }
  );
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: true,
    requirementMaturity: {
      maturity: 'actionable',
      taskType: 'surface_or_pose_defect',
      canBuild: true,
      objectSignals: ['metal', 'surface'],
      taskSignals: ['scratch'],
      missingFields: ['image_source', 'acceptance_criteria'],
      blockingReasons: [],
      publicReason: '需求已明确到可规划视觉流程。'
    }
  }));

  panel.pendingVisionPlan = plan;
  panel._renderAgentWorkspaceOverview();
  panel._renderPlanWorkspace(plan);

  assert.equal(plan.executable, true);
  assert.match(overview.innerHTML, /可构建：是/);
  assert.equal(inlineBuildButton.disabled, false);
  assert.ok(planActionButtons.every(button => !button.disabled));
});

test('Start Build from non-executable Plan is blocked before dispatch', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    requirementMaturity: {
      maturity: 'abstract_goal',
      taskType: 'abstract_goal',
      canBuild: false,
      missingFields: ['inspection_object', 'task_type'],
      blockingReasons: ['abstract_goal_needs_decomposition'],
      publicReason: '这是方案愿景，不是可直接构建的检测流程。'
    },
    decisionTrace: {
      maturityLevel: 'abstract_goal',
      taskType: 'abstract_goal',
      canBuild: false,
      fallbackReason: 'maturity_gate_blocked',
      blockingReasons: ['abstract_goal_needs_decomposition']
    }
  }));
  panel._dispatchGenerateRequest = () => {
    throw new Error('Build should not dispatch for a non-executable Plan');
  };

  const started = await panel._startBuildFromCurrentPlan();

  assert.equal(started, false);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
  assert.match(panel.lastResultStatusNote.text, /还缺：检测对象|方案愿景|暂不可构建/);
  assert.match(panel.container.querySelector('#ai-plan-workspace').innerHTML, /需求成熟度/);
});

test('Start Build without pending Plan is blocked with Plan-first prompt', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const inlineBuildButton = createFakeButton();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-btn-start-build-inline': inlineBuildButton
  });
  panel._dispatchGenerateRequest = () => {
    throw new Error('Build should not dispatch without a pending Plan');
  };

  const started = await panel._startBuildFromCurrentPlan();

  assert.equal(started, false);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(panel.lastResultStatusNote.text, '请先完成规划，再开始构建。');
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
  assert.equal(panel.messages.at(-1).text, '请先完成规划，再开始构建。');
  assert.equal(inlineBuildButton.disabled, true);
  assert.equal(inlineBuildButton.getAttribute('aria-disabled'), 'true');
});

test('developer direct Build debug is one-shot and explicitly skips Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    developer: true,
    enabled: true,
    directBuildDebugNextRequest: true
  });
  const input = createFakeElement();
  input.value = '直接调试构建路径';
  panel.attachments = [];
  panel.container = createContainer({
    '#ai-input': input
  });
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  await panel._handleGenerate();

  assert.equal(captured.skipPlan, true);
  assert.equal(captured.skipPlanSource, 'developer_direct_build_debug');
  assert.equal(captured.explicitMode, 'new');
  assert.equal(panel.directBuildDebugNextRequest, false);
});

test('Plan Mode ignores stale backend response when a newer Plan request wins', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });

  const first = deferred();
  const second = deferred();
  let calls = 0;
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = () => {
    calls += 1;
    return calls === 1 ? first.promise : second.promise;
  };

  panel._enterPlanModeFromPrompt({ description: 'old plan', userMessage: 'old plan' });
  panel._enterPlanModeFromPrompt({ description: 'new plan', userMessage: 'new plan' });

  second.resolve(backendPlanResult({ planId: 'plan_new', goal: 'new plan ready' }));
  await flushAsync();
  assert.equal(panel.pendingVisionPlan.planId, 'plan_new');
  assert.equal(panel.pendingVisionPlan.goal, 'new plan ready');

  first.resolve(backendPlanResult({ planId: 'plan_old', goal: 'old plan should not win' }));
  await flushAsync();
  assert.equal(panel.pendingVisionPlan.planId, 'plan_new');
  assert.equal(panel.pendingVisionPlan.goal, 'new plan ready');
});

test('Plan Mode ignores stale Plan run events when a newer run is active', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.activePlanRequestId = 'plan-request-new';
  panel.activePlanRunId = 'ar_plan_new';
  panel.activePlanRunRequestId = 'plan-request-new';
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    planId: 'plan_new_active',
    goal: 'new active plan'
  }));

  panel._handlePlanRunEvent({
    runId: 'ar_plan_old',
    sequence: 99,
    eventType: 'plan.completed',
    stage: 'plan_ready',
    title: '规划已就绪',
    summary: 'old completion',
    status: 'completed',
    payload: {
      planResult: backendPlanResult({ planId: 'plan_old_late', goal: 'old late plan' })
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(panel.pendingVisionPlan.planId, 'plan_new_active');
  assert.equal(panel.pendingVisionPlan.goal, 'new active plan');
  assert.equal(panel.activePlanRunEvents.length, 0);
});

test('PlanRun plan.completed/cancelled/failed record timeline but do not close or settle stream', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  attachAgentRunTurn(panel);
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.activePlanRequestId = 'plan-request-open';
  panel.activePlanRunId = 'ar_plan_open';
  panel.activePlanRunRequestId = 'plan-request-open';
  const completion = deferred();
  let resolved = false;
  let rejected = false;
  completion.promise.then(
    () => { resolved = true; },
    () => { rejected = true; }
  );
  panel.activePlanRunCompletion = {
    runId: 'ar_plan_open',
    resolve: completion.resolve,
    reject: completion.reject
  };
  let closed = false;
  panel._closeAgentRunEventSource = () => {
    closed = true;
  };
  const planResult = backendPlanResult({ planId: 'plan_intermediate_only', goal: 'intermediate plan' });

  panel._handlePlanRunEvent({
    runId: 'ar_plan_open',
    sequence: 7,
    eventType: 'plan.completed',
    stage: 'plan_ready',
    title: '规划已就绪',
    summary: '规划已完成，可以开始构建。',
    status: 'completed',
    payload: { planResult, metadataOnly: true },
    metadataOnly: true,
    redactionPass: true
  });
  ['plan.cancelled', 'plan.failed'].forEach((eventType, index) => {
    panel._handlePlanRunEvent({
      runId: 'ar_plan_open',
      sequence: 8 + index,
      eventType,
      stage: 'plan_ready',
      title: eventType,
      summary: `${eventType} only updates the timeline`,
      status: eventType === 'plan.cancelled' ? 'cancelled' : 'failed',
      payload: { planResult, metadataOnly: true },
      metadataOnly: true,
      redactionPass: true
    });
  });
  await flushAsync();

  assert.equal(closed, false);
  assert.equal(resolved, false);
  assert.equal(rejected, false);
  assert.equal(panel.activePlanRunCompletion?.runId, 'ar_plan_open');
  assert.equal(panel.activePlanRunEvents.length, 3);
});

test('PlanRun run.completed applies final revision before resolving Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  attachAgentRunTurn(panel);
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.workspaceSnapshotRevision = 3;
  panel.activePlanRequestId = 'plan-request-final';
  panel.activePlanRunId = 'ar_plan_final';
  panel.activePlanRunRequestId = 'plan-request-final';
  let revisionAtResolve = 0;
  let resolvedPlan = null;
  let rejectedError = null;
  panel.activePlanRunCompletion = {
    runId: 'ar_plan_final',
    resolve(result) {
      revisionAtResolve = panel.workspaceSnapshotRevision;
      resolvedPlan = result;
    },
    reject(error) {
      rejectedError = error;
    }
  };
  let closed = false;
  panel._closeAgentRunEventSource = () => {
    closed = true;
  };
  const planResult = backendPlanResult({ planId: 'plan_final_revision', goal: 'final revision plan' });

  panel._handlePlanRunEvent(planRunCompletedEvent({
    runId: 'ar_plan_final',
    sequence: 9,
    planResult,
    revision: 42
  }));

  assert.equal(rejectedError, null);
  assert.equal(closed, true);
  assert.equal(panel.workspaceSnapshotRevision, 42);
  assert.equal(revisionAtResolve, 42);
  assert.equal(resolvedPlan.planId, 'plan_final_revision');
  assert.equal(resolvedPlan.workspaceSnapshot.revision, 42);
  assert.equal(resolvedPlan.persistenceStatus.primaryStoreSaved, true);
  assert.equal(panel.activePlanRunCompletion, null);
});

test('PlanRun run.cancelled applies final persistence before rejecting Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  attachAgentRunTurn(panel);
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.workspaceSnapshotRevision = 3;
  panel.activePlanRequestId = 'plan-request-cancel-final';
  panel.activePlanRunId = 'ar_plan_cancel_final';
  panel.activePlanRunRequestId = 'plan-request-cancel-final';
  let rejectedError = null;
  panel.activePlanRunCompletion = {
    runId: 'ar_plan_cancel_final',
    resolve() {},
    reject(error) {
      rejectedError = error;
    }
  };
  let closed = false;
  panel._closeAgentRunEventSource = () => {
    closed = true;
  };
  const warningMessage = '规划已取消，但本次 Plan 工作台状态未能保存。';

  panel._handlePlanRunEvent(planRunCompletedEvent({
    eventType: 'run.cancelled',
    runId: 'ar_plan_cancel_final',
    sequence: 9,
    revision: 51,
    publicMessage: '规划已取消。',
    persistenceStatus: {
      primaryStoreSaved: false,
      recoveryBackupSaved: true,
      errorCode: 'primary_store_save_failed',
      publicMessage: warningMessage
    },
    persistenceWarning: {
      code: 'primary_store_save_failed',
      message: warningMessage
    }
  }));

  assert.equal(closed, true);
  assert.equal(panel.workspaceSnapshotRevision, 51);
  assert.equal(panel.activePlanRunCompletion, null);
  assert.match(rejectedError?.message || '', /规划已取消/);
  assert.match(rejectedError?.message || '', /Plan 工作台状态未能保存/);
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
  assert.match(panel.lastResultStatusNote.text, /Plan 工作台状态未能保存/);
  assert.match(panel.workspacePersistenceWarning.message, /Plan 工作台状态未能保存/);
});

test('PlanRun run.failed applies final persistence before rejecting Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  attachAgentRunTurn(panel);
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.workspaceSnapshotRevision = 3;
  panel.activePlanRequestId = 'plan-request-fail-final';
  panel.activePlanRunId = 'ar_plan_fail_final';
  panel.activePlanRunRequestId = 'plan-request-fail-final';
  let rejectedError = null;
  panel.activePlanRunCompletion = {
    runId: 'ar_plan_fail_final',
    resolve() {},
    reject(error) {
      rejectedError = error;
    }
  };
  let closed = false;
  panel._closeAgentRunEventSource = () => {
    closed = true;
  };
  const warningMessage = '规划失败，但本次 Plan 工作台状态未能保存。';

  panel._handlePlanRunEvent(planRunCompletedEvent({
    eventType: 'run.failed',
    runId: 'ar_plan_fail_final',
    sequence: 10,
    revision: 52,
    publicMessage: '规划在完成前失败。',
    persistenceStatus: {
      primaryStoreSaved: false,
      recoveryBackupSaved: true,
      errorCode: 'primary_store_save_failed',
      publicMessage: warningMessage
    },
    persistenceWarning: {
      code: 'primary_store_save_failed',
      message: warningMessage
    },
    diagnostic: {
      publicMessage: '规划在完成前失败。',
      workspaceSnapshot: {
        revision: 52,
        lifecycleState: 'plan_failed',
        planRunId: 'ar_plan_fail_final',
        planRunStatus: 'failed'
      },
      persistenceStatus: {
        primaryStoreSaved: false,
        recoveryBackupSaved: true,
        errorCode: 'primary_store_save_failed',
        publicMessage: warningMessage
      },
      persistenceWarning: {
        code: 'primary_store_save_failed',
        message: warningMessage
      }
    }
  }));

  assert.equal(closed, true);
  assert.equal(panel.workspaceSnapshotRevision, 52);
  assert.equal(panel.activePlanRunCompletion, null);
  assert.match(rejectedError?.message || '', /规划在完成前失败/);
  assert.match(rejectedError?.message || '', /Plan 工作台状态未能保存/);
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
  assert.match(panel.lastResultStatusNote.text, /Plan 工作台状态未能保存/);
  assert.match(panel.workspacePersistenceWarning.message, /Plan 工作台状态未能保存/);
});

test('PlanRun first Build after completion uses final workspace revision and starts Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.useVisionAgentGenerateFlow = true;
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._requestPlanReadinessPreview = () => null;
  const startedBuildStreams = [];
  const planResult = backendPlanResult({
    planId: 'plan_revision_gate',
    planHash: 'sha256:plan-revision-gate',
    goal: 'revision gate plan',
    canPlan: true,
    canBuild: true,
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: [],
      primaryMessage: '当前确认项已满足构建条件。',
      contractVersion: 'v2'
    }
  });
  const requests = installFetchStream([
    { json: { runId: 'ar_plan_revision_gate', sessionId: 'session-plan-revision', events: [] } },
    {
      body: [
        encodeSseEvent({
          runId: 'ar_plan_revision_gate',
          sequence: 4,
          eventType: 'plan.completed',
          stage: 'plan_ready',
          title: '规划已就绪',
          summary: '规划已完成，可以开始构建。',
          status: 'completed',
          payload: { planResult, metadataOnly: true },
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent(planRunCompletedEvent({
          runId: 'ar_plan_revision_gate',
          sequence: 5,
          planResult,
          revision: 31
        }))
      ].join('')
    },
    {
      json: {
        runId: 'ar_build_revision_gate',
        sessionId: 'session-plan-revision',
        events: [],
        workspaceSnapshot: {
          revision: 32,
          lifecycleState: 'building',
          buildRunId: 'ar_build_revision_gate',
          submittedBuildFingerprint: 'sha256:submitted'
        },
        persistenceStatus: { primaryStoreSaved: true, recoveryBackupSaved: true }
      }
    }
  ]);

  panel._enterPlanModeFromPrompt({
    description: 'revision gate plan',
    userMessage: 'revision gate plan',
    clearInput: false
  });
  await waitFor(() => panel.pendingVisionPlan?.planId === 'plan_revision_gate', 'PlanRun final plan');
  assert.equal(panel.workspaceSnapshotRevision, 31);
  assert.equal(panel.activePlanRunId, null);
  assert.equal(panel.activePlanRunRequestId, null);
  panel._startAgentRunEventSource = (runId, options) => {
    startedBuildStreams.push({ runId, options });
  };

  const started = await panel._startBuildFromCurrentPlan();
  await waitFor(() => panel.activeAgentRunId === 'ar_build_revision_gate', 'Build run created');

  assert.equal(started, true);
  const buildRequest = requests.find(item => /\/api\/ai\/agent-runs$/.test(item.url));
  assert.ok(buildRequest, 'Build create request was sent');
  const buildBody = JSON.parse(buildRequest.options.body);
  assert.equal(buildBody.buildFromPlan.workspaceExpectedRevision, 31);
  assert.equal(panel.activeAgentRunId, 'ar_build_revision_gate');
  assert.equal(panel.agentWorkspaceMode, 'build');
  assert.deepEqual(startedBuildStreams.map(item => item.runId), ['ar_build_revision_gate']);

  const cancelTargets = [];
  panel._updateProgress = payload => {
    panel.lastProgress = payload;
  };
  panel._cancelActivePlanRun = () => {
    cancelTargets.push('plan');
  };
  panel._cancelActiveAgentRun = () => {
    cancelTargets.push('agent');
  };

  panel._handleCancelGenerate();

  assert.deepEqual(cancelTargets, ['agent']);
  assert.equal(panel.lastProgress?.phase, 'cancelling');
});

test('PlanRun terminal persistence warning is visible and plan result remains available', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._requestPlanReadinessPreview = () => null;
  const planResult = backendPlanResult({
    planId: 'plan_terminal_warning',
    goal: 'terminal warning plan',
    canPlan: true,
    canBuild: false
  });
  installFetchStream([
    { json: { runId: 'ar_plan_terminal_warning', sessionId: 'session-terminal-warning', events: [] } },
    {
      body: [
        encodeSseEvent({
          runId: 'ar_plan_terminal_warning',
          sequence: 4,
          eventType: 'plan.completed',
          stage: 'plan_ready',
          title: '规划已就绪',
          summary: '规划已完成，可以开始构建。',
          status: 'completed',
          payload: { planResult, metadataOnly: true },
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent(planRunCompletedEvent({
          runId: 'ar_plan_terminal_warning',
          sequence: 5,
          planResult,
          revision: 22,
          persistenceStatus: {
            primaryStoreSaved: false,
            recoveryBackupSaved: true,
            errorCode: 'primary_store_save_failed',
            publicMessage: '规划结果已生成，但本次 Plan 工作台状态未能保存。'
          },
          persistenceWarning: {
            code: 'primary_store_save_failed',
            message: '规划结果已生成，但本次 Plan 工作台状态未能保存。'
          }
        }))
      ].join('')
    }
  ]);

  panel._enterPlanModeFromPrompt({
    description: 'terminal warning plan',
    userMessage: 'terminal warning plan',
    clearInput: false
  });
  await waitFor(() => panel.pendingVisionPlan?.planId === 'plan_terminal_warning', 'warning plan result');

  assert.equal(panel.pendingVisionPlan.goal, 'terminal warning plan');
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
  assert.match(panel.lastResultStatusNote.text, /Plan 工作台状态未能保存/);
  assert.equal(panel._workspacePersistenceStatusNoteActive, true);
  assert.match(panel.workspacePersistenceWarning.message, /Plan 工作台状态未能保存/);
});

test('BuildFromPlan prefers Plan templateSelection over raw snapshot and queued selection', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.nextTemplateSelection = { mode: 'template_fill', templateId: 'tmpl-next', scenarioKey: 'queued' };
  panel.workspaceSnapshotRevision = 17;
  panel.planQuestionSelections = {};
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    planId: 'plan_template_priority',
    planHash: 'sha256:priority',
    templateSelection: { mode: 'template_adapt', templateId: 'tmpl-plan', scenarioKey: 'plan' }
  }));
  plan.rawPlanSnapshot = {
    ...plan.rawPlanSnapshot,
    templateSelection: { mode: 'template_fill', templateId: 'tmpl-raw', scenarioKey: 'raw' }
  };
  plan.templateSelection = { mode: 'template_adapt', templateId: 'tmpl-plan', scenarioKey: 'plan' };

  const buildFromPlan = panel._buildStructuredBuildFromPlanRequest(plan);

  assert.equal(buildFromPlan.planHash, 'sha256:priority');
  assert.equal(buildFromPlan.workspaceExpectedRevision, 17);
  assert.deepEqual(buildFromPlan.templateSelection, {
    mode: 'template_adapt',
    templateId: 'tmpl-plan',
    scenarioKey: 'plan'
  });
  assert.equal(buildFromPlan.planSnapshot.templateSelection.templateId, 'tmpl-raw');
});

test('BuildResult replay payload renders Build Workspace and apply gate without leaking private fields', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.agentWorkspaceMode = 'build';
  panel.activeAgentRunEvents = [
    {
      runId: 'ar_build_contract',
      sequence: 20,
      eventType: 'run.completed',
      stage: 'run',
      title: 'Run completed',
      summary: 'Build completed.',
      status: 'completed',
      payload: buildResultContractPayload({ pascal: true }),
      metadataOnly: true,
      redactionPass: true
    }
  ];

  panel._renderBuildWorkspaceFromAgentRun();

  const timelineHtml = elements['#ai-build-event-timeline'].innerHTML;
  for (const label of [
    '生成计划',
    '模板策略',
    '算子链',
    '参数映射',
    '流程草稿',
    '结构校验',
    '元数据预演',
    '运行包就绪',
    '流程差异',
    '应用门禁'
  ]) {
    assert.match(timelineHtml, new RegExp(label));
  }
  assert.match(timelineHtml, /结构校验工具/);
  assert.match(timelineHtml, /部署资源待绑定/);
  assert.match(timelineHtml, /15 ms/);
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /表面缺陷检测/);
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /accepted_recommended/);
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /attribute_classification_deep_learning/);
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /deep_learning_classification/);
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /title="op_detect \/ SurfaceDefectDetection"/);
  assert.doesNotMatch(elements['#ai-build-operator-chain'].innerHTML, />SurfaceDefectDetection</);
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /模板骨架/);
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /非法算子已修复/);
  assert.match(elements['#ai-build-parameters'].innerHTML, /模型资源/);
  assert.match(elements['#ai-build-parameters'].innerHTML, /缺失资源 \/ 待确认/);
  assert.match(elements['#ai-build-checks'].innerHTML, /画布可应用：是/);
  assert.match(elements['#ai-build-checks'].innerHTML, /运行草稿：就绪/);
  assert.match(elements['#ai-build-checks'].innerHTML, /部署：阻断/);
  assert.match(elements['#ai-build-checks'].innerHTML, /First Fix/);
  assert.match(elements['#ai-build-checks'].innerHTML, /模型资源/);
  assert.match(elements['#ai-build-final-draft'].innerHTML, /可编辑草稿已就绪/);
  assert.match(elements['#ai-build-final-draft'].innerHTML, /流程差异/);
  assert.match(elements['#ai-build-final-draft'].innerHTML, /保留节点/);
  assert.match(elements['#ai-build-final-draft'].innerHTML, /模型资源/);
  assert.doesNotMatch([
    timelineHtml,
    elements['#ai-build-operator-chain'].innerHTML,
    elements['#ai-build-parameters'].innerHTML,
    elements['#ai-build-checks'].innerHTML,
    elements['#ai-build-final-draft'].innerHTML
  ].join('\n'), /Tool Evidence|Workflow Diff|Apply Gate|Editable draft ready|Canvas: ready|Runtime draft|Deployment: blocked|validate_schema_tool|deployment_resource_pending|template_skeleton|invalid operator was repaired|missing_resource \/ pending|model_resource metadata|>SurfaceDefectDetection</);
  assertNoSensitiveLeak([
    timelineHtml,
    elements['#ai-build-operator-chain'].innerHTML,
    elements['#ai-build-parameters'].innerHTML,
    elements['#ai-build-checks'].innerHTML,
    elements['#ai-build-final-draft'].innerHTML
  ].join('\n'));
});

test('Tool Evidence Timeline distinguishes LLM tool loop and fallback fixed chain sources', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.agentWorkspaceMode = 'build';
  const payload = buildResultContractPayload();
  payload.buildResult.toolEvidenceTimeline = [
    {
      stage: 'tool_loop',
      toolName: 'inspect_current_flow',
      source: 'llm_tool_loop',
      status: 'completed',
      durationMs: 8,
      outputSummary: 'LLM-requested tool completed with public metadata.',
      metadataOnly: true,
      redactionPass: true
    },
    {
      stage: 'workflow_draft',
      toolName: 'stable_build_tool',
      source: 'fixed_build_orchestrator',
      status: 'completed',
      durationMs: 10,
      outputSummary: 'Stable BuildOrchestrator completed.',
      metadataOnly: true,
      redactionPass: true
    },
    {
      stage: 'workflow_draft',
      toolName: 'stable_build_tool',
      source: 'fallback_build_orchestrator',
      status: 'completed',
      warningCode: 'partial_final_requires_stable_completion',
      durationMs: 12,
      outputSummary: 'Stable BuildOrchestrator completed.',
      metadataOnly: true,
      redactionPass: true
    },
    {
      stage: 'template_strategy',
      toolName: 'get_flow_template_skeleton',
      source: 'fixed_build_orchestrator',
      status: 'warning',
      warningCode: 'template_not_found',
      durationMs: 6,
      outputSummary: '未找到匹配模板骨架，已改用算子链生成。',
      metadataOnly: true,
      redactionPass: true
    }
  ];
  panel.activeAgentRunEvents = [
    {
      runId: 'ar_tool_loop_build',
      sequence: 20,
      eventType: 'run.completed',
      stage: 'run',
      title: 'Run completed',
      summary: 'Build completed.',
      status: 'completed',
      payload,
      metadataOnly: true,
      redactionPass: true
    }
  ];

  panel._renderAgentWorkspaceOverview();
  panel._renderBuildWorkspaceFromAgentRun();

  const timelineHtml = elements['#ai-build-event-timeline'].innerHTML;
  assert.match(timelineHtml, /Build 工具证据/);
  assert.match(timelineHtml, /LLM 工具循环/);
  assert.match(timelineHtml, /固定构建链路/);
  assert.match(timelineHtml, /回退构建链路/);
  assert.match(timelineHtml, /Tool Loop 草稿不完整，已回退稳定构建链路/);
  assert.match(timelineHtml, /未找到匹配模板骨架，已改用算子链生成/);
});

test('Build Workspace displays Tool Loop execution path and fallback reason', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'tool_loop' });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.agentWorkspaceMode = 'build';
  const payload = buildResultContractPayload();
  panel.activeAgentRunEvents = [
    {
      runId: 'ar_tool_loop_path',
      sequence: 1,
      eventType: 'run.started',
      stage: 'run',
      title: 'Run started',
      status: 'running',
      payload: {
        useVisionAgentGenerateFlow: true,
        agentGenerateFlowMode: 'tool_loop',
        metadataOnly: true
      },
      metadataOnly: true,
      redactionPass: true
    },
    {
      runId: 'ar_tool_loop_path',
      sequence: 3,
      eventType: 'tool_loop.started',
      stage: 'tool_loop',
      title: 'Tool Loop started',
      status: 'running',
      metadataOnly: true,
      redactionPass: true
    },
    {
      runId: 'ar_tool_loop_path',
      sequence: 12,
      eventType: 'tool_loop.fallback',
      stage: 'tool_loop',
      title: 'Tool Loop fallback',
      status: 'blocked',
      payload: {
        fallbackReason: 'duplicate_tool_call',
        metadataOnly: true
      },
      metadataOnly: true,
      redactionPass: true
    },
    {
      runId: 'ar_tool_loop_path',
      sequence: 20,
      eventType: 'run.completed',
      stage: 'run',
      title: 'Run completed',
      summary: 'Build completed.',
      status: 'completed',
      payload,
      metadataOnly: true,
      redactionPass: true
    }
  ];

  panel._renderAgentWorkspaceOverview();
  panel._renderBuildWorkspaceFromAgentRun();

  const overviewHtml = elements['#ai-agent-workspace-overview'].innerHTML;
  assert.match(overviewHtml, /当前模式/);
  assert.match(overviewHtml, /Tool Loop 实验/);
  assert.match(overviewHtml, /VisionAgentLoop/);
  assert.match(overviewHtml, /已进入/);
  assert.match(overviewHtml, /路径原因/);
  assert.match(overviewHtml, /重复工具调用超限，已回退稳定构建链路/);
});

test('Canvas apply remains enabled when deployment is blocked by missing resources', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = buildResultContractPayload();
  panel.currentResultVersion = 2;
  panel.appliedResultVersion = 0;

  panel._updateApplyButtonState();
  assert.equal(elements['#ai-btn-apply'].disabled, false);
  assert.equal(elements['#ai-btn-apply'].getAttribute('aria-disabled'), 'false');
  assert.match(elements['#ai-btn-apply'].innerHTML, /\u5e94\u7528\u5230\u753b\u5e03/);
  assert.equal(panel._isCanvasApplyReadyForResult(panel.currentResult), true);
  const gate = panel._getPayloadApplyGate(panel.currentResult);
  assert.equal(gate.deploymentReady, false);
  assert.equal(gate.blocked, false);
});

test('parameter review copy makes AI review optional and removes submit audit wording', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = pendingParameterReviewResult();
  panel.options.getOperators = () => resourceBindingOperatorMetadata();
  panel._rebuildPendingOperatorBindings({
    pending: panel._resolvePendingParametersForDraft(panel.currentResult),
    flow: panel.currentResult.flow,
    preferIndexFallback: true
  });

  panel._renderFollowupChecklist(panel.currentResult, panel.currentResult.flow);
  panel._renderParameterDraftEditor(panel.currentResult, panel.currentResult.flow);
  const combined = [
    elements['#ai-result-followups'].innerHTML,
    elements['#ai-result-parameter-editor'].innerHTML
  ].join('\n');

  assert.match(combined, /确认人工参数/);
  assert.match(combined, /提交 AI 复核（可选）/);
  assert.match(combined, /AI 复核仅用于二次检查或继续优化，不是应用到画布的必要步骤/);
  assert.match(combined, /人工确认后的参数可直接应用到画布/);
  assert.doesNotMatch(combined, /提交审核/);
  assert.doesNotMatch(combined, /确认全部参数/);
});

test('apply hint separates canvas apply from DeploymentReady gate', async () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'),
    'utf8'
  );

  assert.match(source, /确认人工参数后，可应用到画布继续编辑/);
  assert.match(source, /部署仍受资源确认和 DeploymentReady 门禁约束/);
});

test('confirmed manual parameters are written when applying to canvas', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = pendingParameterReviewResult();
  panel.currentResultVersion = 7;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.options.getOperators = () => resourceBindingOperatorMetadata();
  panel.options.onApplied = flow => {
    panel.appliedFlow = flow;
  };
  panel._showApplyPreview = (_diff, flow) => panel._executeApplyFlow(flow);
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._rebuildPendingOperatorBindings({
    pending: panel._resolvePendingParametersForDraft(panel.currentResult),
    flow: panel.currentResult.flow,
    preferIndexFallback: true
  });
  panel._syncPendingParameterDrafts(panel.currentResult, panel.currentResult.flow, { force: true });
  panel._setPendingDraftConfirmedValue('op_detect', 'ModelPath', 'model-resource-approved', 'text', 'user_input');
  panel._handleConfirmPendingParameters(panel.currentResult, panel.currentResult.flow);

  panel._handleApplyFlow();

  const detect = panel._extractOperators(panel.appliedFlow).find(op => op.id === 'op_detect');
  assert.equal(panel._readOperatorParameterValue(detect, 'ModelPath'), 'model-resource-approved');
  assert.match(panel.lastResultStatusNote.text, /已应用到画布/);
  assert.equal(panel._getPayloadApplyGate(panel.currentResult).deploymentReady, false);
});

test('unconfirmed pending parameters are not silently written during canvas apply', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = pendingParameterReviewResult();
  panel.currentResultVersion = 8;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.options.getOperators = () => resourceBindingOperatorMetadata();
  panel.options.onApplied = flow => {
    panel.appliedFlow = flow;
  };
  panel._showApplyPreview = (_diff, flow) => panel._executeApplyFlow(flow);
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._rebuildPendingOperatorBindings({
    pending: panel._resolvePendingParametersForDraft(panel.currentResult),
    flow: panel.currentResult.flow,
    preferIndexFallback: true
  });
  panel._syncPendingParameterDrafts(panel.currentResult, panel.currentResult.flow, { force: true });
  panel._setPendingDraftConfirmedValue('op_detect', 'ModelPath', 'model-resource-not-confirmed', 'text', 'user_input');

  panel._handleApplyFlow();

  const detect = panel._extractOperators(panel.appliedFlow).find(op => op.id === 'op_detect');
  assert.equal(panel._readOperatorParameterValue(detect, 'ModelPath'), '<pending-model-resource>');
  assert.equal(panel._getPayloadApplyGate(panel.currentResult).deploymentReady, false);
});

test('canvas manual edits are audited after applying AI result', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = pendingParameterReviewResult();
  panel.currentResultVersion = 9;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.options.getOperators = () => resourceBindingOperatorMetadata();
  panel._showApplyPreview = (_diff, flow) => panel._executeApplyFlow(flow);
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._setupCanvasStructureSync();

  panel._handleApplyFlow();
  const editedFlow = panel.flowCanvas.serialize();
  panel._writeOperatorParameterValue(editedFlow.operators[0], 'Threshold', 0.91);
  panel.flowCanvas.replaceFlow(editedFlow, 'parameter-change');

  assert.equal(panel.canvasManualEditRecords.length, 1);
  const record = panel.canvasManualEditRecords[0];
  assert.equal(record.source, 'canvas_manual_edit');
  assert.equal(record.sourceLabel, '流程页算子属性面板');
  assert.equal(record.actor, 'local-user');
  assert.equal(record.parameterName, 'Threshold');
  assert.equal(record.oldValueSummary, '0.8');
  assert.equal(record.newValueSummary, '0.91');
  assert.equal(record.isPendingParameter, false);
  assert.equal(record.metadataOnly, true);
  assert.equal(record.affectsDeploymentReady, false);
  assert.equal(panel._getPayloadApplyGate(panel.currentResult).deploymentReady, false);
  assert.match(elements['#ai-result-followups'].innerHTML, /画布人工修改记录/);
  assert.match(elements['#ai-result-followups'].innerHTML, /Threshold/);
});

test('canvas edits to pending parameters sync back to review fields', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = pendingParameterReviewResult();
  panel.currentResultVersion = 10;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.options.getOperators = () => resourceBindingOperatorMetadata();
  panel._showApplyPreview = (_diff, flow) => panel._executeApplyFlow(flow);
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._setupCanvasStructureSync();

  panel._handleApplyFlow();
  const editedFlow = panel.flowCanvas.serialize();
  panel._writeOperatorParameterValue(editedFlow.operators[0], 'ModelPath', 'model-resource-from-canvas');
  panel.flowCanvas.replaceFlow(editedFlow, 'parameter-change');

  const entry = panel._getPendingDraftEntry('op_detect', 'ModelPath');
  assert.equal(entry.confirmedValue, 'model-resource-from-canvas');
  assert.equal(entry.source, 'canvas_override');
  assert.match(elements['#ai-result-parameter-editor'].innerHTML, /当前值已从画布同步/);
  assert.match(elements['#ai-result-followups'].innerHTML, /pendingParameter=true/);
  assert.equal(panel._getPayloadApplyGate(panel.currentResult).deploymentReady, false);
});

test('canvas manual edit audit redacts sensitive values', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = pendingParameterReviewResult();
  panel.currentResultVersion = 11;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.options.getOperators = () => resourceBindingOperatorMetadata();
  panel._showApplyPreview = (_diff, flow) => panel._executeApplyFlow(flow);
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._setupCanvasStructureSync();

  panel._handleApplyFlow();
  const editedFlow = panel.flowCanvas.serialize();
  panel._writeOperatorParameterValue(
    editedFlow.operators[0],
    'Threshold',
    'C:\\factory\\secret.onnx token=abc123 192.168.1.8 DB1.DBX0.0 data:image/png;base64,AAAA'
  );
  panel.flowCanvas.replaceFlow(editedFlow, 'parameter-change');

  const combined = [
    JSON.stringify(panel.canvasManualEditRecords),
    elements['#ai-result-followups'].innerHTML
  ].join('\n');
  assert.doesNotMatch(combined, /C:\\factory/);
  assert.doesNotMatch(combined, /secret\.onnx/);
  assert.doesNotMatch(combined, /abc123/);
  assert.doesNotMatch(combined, /192\.168\.1\.8/);
  assert.doesNotMatch(combined, /DB1\.DBX0\.0/);
  assert.doesNotMatch(combined, /data:image/);
});

test('AgentRun completed payload restores BuildResult fallback flow and keeps Apply state after replay', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.activeAgentRunId = 'ar_build_contract';
  panel.agentWorkspaceMode = 'build';
  panel._displayResult = result => {
    panel.displayedResult = result;
  };
  const completedPayload = buildResultContractPayload({ pascal: true });
  const completedEvent = {
    runId: 'ar_build_contract',
    sequence: 30,
    eventType: 'run.completed',
    stage: 'run',
    title: 'Run completed',
    summary: 'Replay completed.',
    status: 'completed',
    payload: completedPayload,
    metadataOnly: true,
    redactionPass: true
  };
  panel.activeAgentRunEvents = [completedEvent];

  const applied = panel._applyAgentRunResultPayload({
    eventType: 'run.completed',
    payload: completedPayload
  });

  assert.equal(applied, true);
  assert.ok(panel.currentResult?.flow);
  assert.equal(panel._extractOperators(panel.currentResult.flow).length, 4);
  assert.equal(panel._extractOperators(panel.currentResult.flow)[1].type, 'SurfaceDefectDetection');
  assert.equal(panel.currentResult.missingResources.length, 1);
  assert.match(elements['#ai-btn-apply'].innerHTML, /\u5e94\u7528\u5230\u753b\u5e03/);
  panel._renderBuildWorkspaceFromAgentRun();
  assert.match(elements['#ai-build-final-draft'].innerHTML, /可编辑草稿已就绪/);
  assert.match(elements['#ai-build-checks'].innerHTML, /部署：阻断/);
});

test('AgentRun completed payload without canonical flow fails closed despite WorkflowDraft and OperatorPipeline', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.flowCanvas = createFakeFlowCanvas({
    operators: [{ id: 'existing_canvas', type: 'ImageAcquisition', name: 'Existing canvas', parameters: [] }],
    connections: []
  });
  const initialCanvasRevision = panel.flowCanvas.getFlowRevision();
  panel.activeAgentRunId = 'ar_missing_canonical_flow';
  panel.agentWorkspaceMode = 'build';
  panel._setCurrentResult(buildResultContractPayload());
  assert.equal(elements['#ai-btn-apply'].disabled, false);
  const payload = buildResultContractPayload();
  delete payload.flow;
  delete payload.buildResult.flow;
  assert.ok(payload.buildResult.workflowDraft);
  assert.ok(payload.buildResult.operatorPipeline.length > 0);

  const completedEvent = {
    runId: 'ar_missing_canonical_flow',
    sequence: 31,
    eventType: 'run.completed',
    stage: 'run',
    title: 'Run completed',
    summary: 'Build completed.',
    status: 'completed',
    payload,
    metadataOnly: true,
    redactionPass: true
  };
  panel.activeAgentRunEvents = [completedEvent];

  assert.equal(panel._getResultFlowForCanvas(payload), null);
  assert.equal(panel._applyAgentRunResultPayload(completedEvent), false);

  assert.equal(panel.currentResult?.compatibilityDiagnosticCode, 'legacy_build_artifact_missing_canonical_flow');
  assert.equal(panel.currentResult?.status, 'failed');
  assert.equal(panel.currentResult?.Status, 'failed');
  assert.equal(panel.currentResult?.completionStatus, 'failed');
  assert.equal(panel.currentResult?.CompletionStatus, 'failed');
  assert.equal(panel.currentResult?.interactionState, 'failed');
  assert.equal(panel.currentResult?.InteractionState, 'failed');
  assert.equal(panel.currentResult?.success, false);
  assert.equal(panel.currentResult?.Success, false);
  assert.equal(panel.currentResult?.failureType, 'legacy_build_artifact_missing_canonical_flow');
  assert.equal(panel.currentResult?.FailureType, 'legacy_build_artifact_missing_canonical_flow');
  assert.notEqual(panel.currentResult?.status, 'completed');
  assert.equal(panel.currentResult?.flow, null);
  assert.equal(panel.currentResult?.applyGate?.canvasApplyReady, false);
  assert.equal(panel._isCanvasApplyReadyForResult(panel.currentResult), false);
  assert.equal(panel.flowCanvas.getFlowRevision(), initialCanvasRevision);
  assert.equal(elements['#ai-btn-apply'].disabled, true);
  assert.equal(elements['#ai-btn-apply'].getAttribute('aria-disabled'), 'true');
  assert.match(panel.lastResultStatusNote?.text || '', /该构建结果不包含可验证的画布流程产物/);
  assert.match(elements['#ai-result-summary'].innerHTML, /该构建结果不包含可验证的画布流程产物/);
  assert.doesNotMatch(elements['#ai-result-summary'].innerHTML, /该方案包含 <span class="result-count">0<\/span> 个算子/);
  assert.match(elements['#ai-build-event-timeline'].innerHTML, /Build 工具证据/);
  assert.match(elements['#ai-build-parameters'].innerHTML, /模型资源/);
  assert.match(elements['#ai-build-checks'].innerHTML, /应用门禁：已阻断/);
  assert.match(elements['#ai-build-checks'].innerHTML, /请基于原计划重新构建/);
  assert.match(elements['#ai-build-final-draft'].innerHTML, /无法应用构建结果/);
  assert.match(elements['#ai-build-final-draft'].innerHTML, /legacy_build_artifact_missing_canonical_flow/);
  assert.doesNotMatch(elements['#ai-build-final-draft'].innerHTML, /可编辑草稿已就绪/);
  assert.doesNotMatch(elements['#ai-btn-apply'].innerHTML, /应用到画布/);
});

test('AgentRun failed payload with BuildResult and no Flow preserves original failure terminal', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.agentWorkspaceMode = 'build';
  const payload = buildResultContractPayload();
  delete payload.flow;
  delete payload.buildResult.flow;
  delete payload.buildResult.applyGate;
  payload.success = false;
  payload.status = 'failed';
  payload.completionStatus = 'failed';
  payload.failureType = 'system_error';
  payload.aiExplanation = 'Model provider returned a system error.';
  payload.failureSummary = { message: 'Model provider returned a system error.', repairTarget: 'Retry after checking model service.' };
  const failedEvent = {
    runId: 'ar_failed_no_flow',
    sequence: 31,
    eventType: 'run.failed',
    stage: 'run',
    title: 'Run failed',
    summary: 'Model provider returned a system error.',
    status: 'failed',
    payload,
    metadataOnly: true,
    redactionPass: true
  };
  panel.activeAgentRunEvents = [failedEvent];

  const state = panel._getBuildArtifactFlowCompatibilityState(payload, [failedEvent]);
  assert.equal(state.status, 'terminal_failed_without_flow');
  assert.equal(state.terminal.failureType, 'system_error');

  panel._renderBuildWorkspaceFromAgentRun();
  panel._displayResult(payload, { appendChatMessage: false });

  assert.match(elements['#ai-build-final-draft'].innerHTML, /Model provider returned a system error/);
  assert.match(elements['#ai-result-summary'].innerHTML, /Model provider returned a system error/);
  assert.doesNotMatch(elements['#ai-build-final-draft'].innerHTML, /legacy_build_artifact_missing_canonical_flow/);
  assert.doesNotMatch(elements['#ai-build-final-draft'].innerHTML, /该构建结果不包含可验证的画布流程产物/);
});

test('AgentRun failed readiness blocker with BuildResult keeps real blocker and first fix', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.agentWorkspaceMode = 'build';
  const payload = buildResultContractPayload();
  delete payload.flow;
  delete payload.buildResult.flow;
  payload.success = false;
  payload.status = 'failed';
  payload.completionStatus = 'failed';
  payload.failureType = 'readiness_blocked';
  payload.buildResult.applyGate = {
    canvasApplyReady: false,
    runtimeDraftReady: false,
    deploymentReady: false,
    blocked: true,
    status: 'readiness_blocked',
    firstFixRecommendation: 'Bind model_resource metadata before applying.',
    metadataOnly: true
  };
  payload.buildResult.firstFixRecommendation = 'Bind model_resource metadata before applying.';
  const failedEvent = {
    runId: 'ar_readiness_blocked',
    sequence: 31,
    eventType: 'run.failed',
    stage: 'run',
    title: 'Run failed',
    summary: 'Readiness blocked by missing model resource.',
    status: 'failed',
    payload,
    metadataOnly: true,
    redactionPass: true
  };
  panel.activeAgentRunEvents = [failedEvent];

  assert.equal(panel._getBuildArtifactFlowCompatibilityState(payload, [failedEvent]).status, 'terminal_failed_without_flow');
  panel._renderBuildWorkspaceFromAgentRun();

  assert.match(elements['#ai-build-checks'].innerHTML, /应用门禁/);
  assert.match(elements['#ai-build-checks'].innerHTML, /画布可应用：否/);
  assert.match(elements['#ai-build-checks'].innerHTML, /First Fix/);
  assert.match(elements['#ai-build-checks'].innerHTML, /模型资源/);
  assert.match(elements['#ai-build-final-draft'].innerHTML, /Readiness blocked by missing model resource/);
  assert.doesNotMatch(elements['#ai-build-checks'].innerHTML, /该构建结果不包含可验证的画布流程产物/);
});

test('AgentRun cancelled payload with BuildResult and no Flow stays cancelled', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const payload = buildResultContractPayload();
  delete payload.flow;
  delete payload.buildResult.flow;
  payload.status = 'cancelled';
  payload.completionStatus = 'cancelled';

  const cancelledEvent = {
    runId: 'ar_cancelled_no_flow',
    sequence: 31,
    eventType: 'run.cancelled',
    stage: 'run',
    title: 'Run cancelled',
    summary: 'User cancelled build.',
    status: 'cancelled',
    payload,
    metadataOnly: true,
    redactionPass: true
  };

  const state = panel._getBuildArtifactFlowCompatibilityState(payload, [cancelledEvent]);
  assert.equal(state.status, 'terminal_cancelled_without_flow');
  assert.doesNotMatch(JSON.stringify(state), /legacy_build_artifact_missing_canonical_flow/);
});

test('AgentRun completed payload with BuildResult.Flow remains apply-ready', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.activeAgentRunId = 'ar_build_result_flow';
  panel.agentWorkspaceMode = 'build';
  panel._displayResult = result => {
    panel.displayedResult = result;
  };
  const payload = buildResultContractPayload();
  delete payload.flow;
  assert.ok(payload.buildResult.flow);
  const completedEvent = {
    runId: 'ar_build_result_flow',
    sequence: 32,
    eventType: 'run.completed',
    stage: 'run',
    title: 'Run completed',
    summary: 'Build completed.',
    status: 'completed',
    payload,
    metadataOnly: true,
    redactionPass: true
  };
  panel.activeAgentRunEvents = [completedEvent];

  assert.equal(panel._getBuildArtifactFlowCompatibilityState(payload).status, 'canonical_flow_available');
  assert.equal(panel._applyAgentRunResultPayload(completedEvent), true);

  assert.equal(panel._extractOperators(panel.currentResult.flow).length, 4);
  assert.equal(panel._isCanvasApplyReadyForResult(panel.currentResult), true);
  assert.equal(elements['#ai-btn-apply'].disabled, false);
  assert.match(elements['#ai-btn-apply'].innerHTML, /应用到画布/);
  assert.match(elements['#ai-build-final-draft'].innerHTML, /可编辑草稿已就绪/);
  assert.doesNotMatch(elements['#ai-build-final-draft'].innerHTML, /legacy_build_artifact_missing_canonical_flow/);
});

test('Session failure projection with BuildResult and no Flow keeps original failure without active events', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  const payload = buildResultContractPayload();
  delete payload.flow;
  delete payload.buildResult.flow;
  payload.kind = 'assistant_agent_failure';
  payload.status = 'failed';
  payload.success = false;
  payload.completionStatus = 'failed';
  payload.failureType = 'system_error';
  payload.failureSummary = { message: 'Historical session system error.', repairTarget: 'Retry the build.' };
  payload.aiExplanation = 'Historical session system error.';

  assert.equal(panel._getBuildArtifactFlowCompatibilityState(payload, []).status, 'terminal_failed_without_flow');

  panel._displayResult(payload, { appendChatMessage: false });

  assert.match(elements['#ai-result-summary'].innerHTML, /Historical session system error/);
  assert.match(elements['#ai-result-ops'].innerHTML, /Historical session system error/);
  assert.doesNotMatch(elements['#ai-result-summary'].innerHTML, /该构建结果不包含可验证的画布流程产物/);
  assert.doesNotMatch(elements['#ai-result-summary'].innerHTML, /legacy_build_artifact_missing_canonical_flow/);
});

test('Session completed projection without Flow restores compatibility without active events', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  const payload = buildResultContractPayload();
  delete payload.flow;
  delete payload.buildResult.flow;
  payload.kind = 'assistant_agent_result';
  payload.status = 'completed';
  payload.success = true;
  payload.completionStatus = 'completed';

  const state = panel._getBuildArtifactFlowCompatibilityState(payload, []);
  assert.equal(state.status, 'legacy_build_artifact_missing_canonical_flow');

  panel._displayResult(payload, { appendChatMessage: false });

  assert.equal(panel._isCanvasApplyReadyForResult(payload), false);
  assert.match(elements['#ai-result-summary'].innerHTML, /该构建结果不包含可验证的画布流程产物/);
  assert.doesNotMatch(elements['#ai-result-summary'].innerHTML, /该方案包含 <span class="result-count">0<\/span> 个算子/);
});

test('Existing compatibility marker restores legacy missing Flow status without active events', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const payload = buildResultContractPayload();
  delete payload.flow;
  delete payload.buildResult.flow;
  payload.status = 'failed';
  payload.completionStatus = 'failed';
  payload.success = false;
  payload.failureType = 'legacy_build_artifact_missing_canonical_flow';
  payload.buildCompatibilityStatus = 'legacy_build_artifact_missing_canonical_flow';
  payload.compatibilityDiagnosticCode = 'legacy_build_artifact_missing_canonical_flow';

  const state = panel._getBuildArtifactFlowCompatibilityState(payload, []);

  assert.equal(state.status, 'legacy_build_artifact_missing_canonical_flow');
  assert.equal(state.code, 'legacy_build_artifact_missing_canonical_flow');
});

test('Legacy WebMessage fallback does not overwrite AgentRun completed BuildResult draft', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.activeGenerateRequestId = 'req-legacy';
  panel.activeAgentRunId = 'ar_build_contract';
  panel.isGenerating = true;
  panel.agentWorkspaceMode = 'build';
  panel._displayResult = result => {
    panel.displayedResult = result;
  };

  panel._handleAgentRunTerminalEvent({
    runId: 'ar_build_contract',
    sequence: 40,
    eventType: 'run.completed',
    stage: 'run',
    title: 'Run completed',
    summary: 'AgentRun completed.',
    status: 'completed',
    payload: buildResultContractPayload(),
    metadataOnly: true,
    redactionPass: true
  });
  const afterAgentRunTypes = panel._extractOperators(panel.currentResult.flow).map(op => op.type);

  panel._handleResult({
    payload: {
      requestId: 'req-legacy',
      success: true,
      flow: {
        operators: [
          { id: 'legacy_op', type: 'DeepLearning', name: 'Legacy fallback', parameters: [] }
        ],
        connections: []
      },
      aiExplanation: 'Legacy WebMessage result should be ignored.'
    }
  });

  assert.deepEqual(panel._extractOperators(panel.currentResult.flow).map(op => op.type), afterAgentRunTypes);
  assert.ok(afterAgentRunTypes.includes('SurfaceDefectDetection'));
  assert.equal(panel._extractOperators(panel.currentResult.flow).some(op => op.id === 'legacy_op'), false);
});

test('Apply fallback sends editable draft to canvas without dropping node types parameters or modify context', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  const existingFlow = {
    operators: [
      {
        id: 'existing_node',
        type: 'ImageAcquisition',
        name: 'Existing acquisition',
        inputPorts: [],
        outputPorts: [{ id: 'existing_node_out_0', name: 'Image', dataType: 'Image', direction: 1 }],
        parameters: [{ name: 'SourceType', value: 'Camera', dataType: 'string' }]
      }
    ],
    connections: []
  };
  let appliedFlow = null;
  panel.container = container;
  panel.flowCanvas = createFakeFlowCanvas(existingFlow);
  panel.currentResult = buildResultContractPayload({ buildIntent: 'modify', preservedNodes: ['existing_node'] });
  panel.currentResultVersion = 3;
  panel.options.onApplied = flow => {
    appliedFlow = flow;
  };
  panel._showApplyPreview = (diff, flow) => panel._executeApplyFlow(flow);
  panel._renderFollowupChecklist = () => {};
  panel._renderParameterDraftEditor = () => {};
  panel._syncPendingParameterDrafts = () => {};

  panel._handleApplyFlow();

  assert.ok(appliedFlow);
  const operators = panel._extractOperators(appliedFlow);
  assert.ok(operators.length > 1);
  assert.ok(operators.some(op => op.id === 'existing_node'));
  assert.ok(operators.some(op => op.type === 'SurfaceDefectDetection'));
  const detect = operators.find(op => op.type === 'SurfaceDefectDetection');
  assert.ok((detect.parameters || []).some(param => param.name === 'ModelId' && param.value === '<pending-model-resource>'));
  assert.ok(panel._extractConnections(appliedFlow).length > 0);
  assert.equal(panel.currentResult.missingResources.length, 1);
  assert.match(panel.lastResultStatusNote.text, /已应用到画布/);
  assert.match(panel.lastResultStatusNote.text, /部署前待补齐或确认/);
  assert.equal(panel.lastWorkbenchState, 'applied');
});

test('AgentRun fetch stream is preferred and sends Authorization header', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_fetch';
  panel.isGenerating = true;
  window.sessionStorage.setItem('cv_auth_token', 'owner-token');
  const requests = installFetchStream([
    {
      body: [
        encodeSseEvent({
          runId: 'ar_fetch',
          sequence: 3,
          eventType: 'stage.started',
          stage: 'planner',
          title: 'Planner started',
          summary: 'Fetch streamed before terminal.',
          status: 'running',
          metadataOnly: true,
          redactionPass: true
        }),
        encodeSseEvent({
          runId: 'ar_fetch',
          sequence: 4,
          eventType: 'run.completed',
          stage: 'run',
          title: 'Run completed',
          summary: 'Done.',
          status: 'completed',
          payload: buildResultContractPayload(),
          metadataOnly: true,
          redactionPass: true
        })
      ].join('')
    }
  ]);

  const transport = panel._startAgentRunEventSource('ar_fetch', { lastSequence: 2 });
  assert.ok(transport);
  await waitFor(() => panel.activeAgentRunEvents.some(evt => evt.eventType === 'run.completed'), 'terminal event from fetch stream');

  assert.match(requests[0].url, /\/api\/ai\/agent-runs\/ar_fetch\/events\?lastEventId=2$/);
  assert.equal(requests[0].options.headers.Authorization, 'Bearer owner-token');
  assert.equal(requests.length, 1);
  assert.ok(requests.every(request => !request.url.includes('/stream-token')));
  assert.match(collectProcessText(turn), /事件流已在终态前返回/);
  assert.equal(panel.activeAgentRunTransport, null);
  assert.equal(panel.isGenerating, false);
});

test('AgentRun fetch stream auth failure replays without duplicate history', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_replay';
  panel.isGenerating = true;
  panel._handleAgentRunEvent({
    runId: 'ar_replay',
    sequence: 2,
    eventType: 'stage.started',
    stage: 'planner',
    title: 'Planner started',
    summary: 'Already rendered.',
    status: 'running',
    metadataOnly: true,
    redactionPass: true
  });
  installFetchStream([
    { status: 401, body: '' },
    {
      json: {
        runId: 'ar_replay',
        events: [
          {
            runId: 'ar_replay',
            sequence: 2,
            eventType: 'stage.started',
            stage: 'planner',
            title: 'Planner started',
            summary: 'Already rendered.',
            status: 'running',
            metadataOnly: true,
            redactionPass: true
          },
          {
            runId: 'ar_replay',
            sequence: 3,
            eventType: 'run.completed',
            stage: 'run',
            title: 'Run completed',
            summary: 'Replay completed.',
            status: 'completed',
            metadataOnly: true,
            redactionPass: true
          }
        ]
      }
    }
  ]);

  panel._startAgentRunEventSource('ar_replay', { lastSequence: 2 });
  await waitFor(() => panel.activeAgentRunEvents.some(evt => evt.eventType === 'run.completed'), 'terminal event from replay');

  assert.equal(panel.activeAgentRunEvents.filter(evt => evt.sequence === 2).length, 1);
  assert.match(collectProcessText(turn), /已切换备用事件流|已进入回放模式/);
  assert.doesNotMatch(collectProcessText(turn), /事件流重连中|Event stream reconnecting/);
  assert.equal(panel.isGenerating, false);
});

test('AgentRun EventSource exists but failing connection falls back to replay mode', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_eventsource_fail';
  panel.isGenerating = true;
  const originalReadableStream = global.ReadableStream;
  global.ReadableStream = undefined;
  let eventSourceClosed = false;
  let eventSourceUrl = '';
  window.EventSource = class MockEventSource {
    constructor(url) {
      eventSourceUrl = url;
      setTimeout(() => this.onerror?.(new Error('401')), 0);
    }
    addEventListener() {}
    close() {
      eventSourceClosed = true;
    }
  };
  installFetchStream([
    { status: 503, body: 'Replay unavailable' },
    { json: { streamToken: 'single-use-stream-ticket' } },
    {
      json: {
        runId: 'ar_eventsource_fail',
        events: [
          {
            runId: 'ar_eventsource_fail',
            sequence: 5,
            eventType: 'run.completed',
            stage: 'run',
            title: 'Run completed',
            summary: 'Replay mode completed.',
            status: 'completed',
            payload: buildResultContractPayload(),
            metadataOnly: true,
            redactionPass: true
          }
        ]
      }
    }
  ]);

  try {
    panel._startAgentRunEventSource('ar_eventsource_fail', { lastSequence: 4 });
    await waitFor(() => panel.activeAgentRunEvents.some(evt => evt.eventType === 'run.completed'), 'terminal event after EventSource failure');

    assert.match(eventSourceUrl, /\/api\/ai\/agent-runs\/ar_eventsource_fail\/events\?/);
    assert.match(eventSourceUrl, /streamToken=single-use-stream-ticket/);
    assert.equal(eventSourceClosed, true);
    assert.match(collectProcessText(turn), /\u5df2\u8fdb\u5165\u56de\u653e\u6a21\u5f0f/);
    assert.match(collectProcessText(turn), /回放模式已完成/);
  } finally {
    global.ReadableStream = originalReadableStream;
  }
});

test('AgentRun assistant brief appends immediate public summary', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_brief';

  panel._handleAgentRunEvent({
    runId: 'ar_brief',
    sequence: 2,
    eventType: 'assistant.brief',
    stage: 'brief',
    summary: 'I will turn this request into a safe Vision Agent workflow draft and stream each public progress step: Detect scratches',
    status: 'completed',
    metadataOnly: true,
    redactionPass: true
  });

  assert.match(turn.replyBody.textContent, /已创建安全的视觉智能体流程草稿任务/);
  assert.match(turn.replyBody.textContent, /Detect scratches/);
  assert.doesNotMatch(turn.replyBody.textContent, /I will turn this request|safe Vision Agent workflow draft/);
  assert.equal(turn.replySection.hidden, false);
  assert.equal(turn.statusEl.textContent, '执行中');
  assert.doesNotMatch(turn.replyBody.textContent, /chain.?of.?thought|hidden reasoning/i);
});

test('AgentRun stage events render public execution process steps', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_stage';

  panel._handleAgentRunEvent({
    runId: 'ar_stage',
    sequence: 3,
    eventType: 'stage.started',
    stage: 'planner',
    title: 'Planner started',
    summary: 'Building a public execution plan.',
    status: 'running',
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(turn.processSection.hidden, false);
  assert.equal(turn.processBody.children.length, 1);
  const copy = turn.processBody.children[0].querySelector('.ai-agent-run-step-copy');
  assert.match(copy.textContent, /规划器已开始/);
  assert.match(copy.textContent, /正在生成公开执行计划/);
});

test('AgentRun tool_loop events render realtime public progress with fallback copy', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'tool_loop' });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_tool_loop';
  panel.isGenerating = true;
  panel.isCancellingGenerate = true;
  let transportClosed = false;
  panel.activeAgentRunTransport = {
    close() {
      transportClosed = true;
    }
  };

  panel._handleAgentRunEvent({
    runId: 'ar_tool_loop',
    sequence: 3,
    eventType: 'tool_loop.started',
    stage: 'tool_loop',
    title: 'Tool Loop started',
    summary: 'Tool Loop started.',
    status: 'running',
    metadataOnly: true,
    redactionPass: true
  });
  panel._handleAgentRunEvent({
    runId: 'ar_tool_loop',
    sequence: 4,
    eventType: 'tool_loop.round.started',
    stage: 'tool_loop',
    title: 'Tool Loop 第 1 轮',
    summary: 'request next tool',
    status: 'running',
    payload: { round: 1, metadataOnly: true },
    metadataOnly: true,
    redactionPass: true
  });
  panel._handleAgentRunEvent({
    runId: 'ar_tool_loop',
    sequence: 5,
    eventType: 'tool_call.requested',
    stage: 'tool_loop',
    title: 'Tool call requested: inspect_current_flow',
    summary: 'metadata-only tool',
    status: 'running',
    payload: { round: 1, toolName: 'inspect_current_flow', metadataOnly: true },
    metadataOnly: true,
    redactionPass: true
  });
  panel._handleAgentRunEvent({
    runId: 'ar_tool_loop',
    sequence: 6,
    eventType: 'tool_call.completed',
    stage: 'tool_loop',
    title: 'Tool call completed: inspect_current_flow',
    summary: 'metadata returned',
    status: 'completed',
    payload: { round: 1, toolName: 'inspect_current_flow', metadataOnly: true },
    metadataOnly: true,
    redactionPass: true
  });
  panel._handleAgentRunEvent({
    runId: 'ar_tool_loop',
    sequence: 7,
    eventType: 'tool_result.appended',
    stage: 'tool_loop',
    title: 'Tool result appended',
    summary: 'tool result appended',
    status: 'completed',
    payload: { round: 1, metadataOnly: true },
    metadataOnly: true,
    redactionPass: true
  });
  panel._handleAgentRunEvent({
    runId: 'ar_tool_loop',
    sequence: 8,
    eventType: 'tool_loop.finalized',
    stage: 'tool_loop',
    title: 'Tool Loop finalized',
    summary: 'final returned',
    status: 'completed',
    metadataOnly: true,
    redactionPass: true
  });
  panel._handleAgentRunEvent({
    runId: 'ar_tool_loop',
    sequence: 9,
    eventType: 'tool_loop.draft.accepted',
    stage: 'tool_loop',
    title: 'Tool Loop draft accepted',
    summary: 'accepted',
    status: 'completed',
    payload: { metadataOnly: true },
    metadataOnly: true,
    redactionPass: true
  });
  panel._handleAgentRunEvent({
    runId: 'ar_tool_loop',
    sequence: 10,
    eventType: 'tool_loop.fallback',
    stage: 'tool_loop',
    title: 'Tool Loop fallback',
    summary: 'Experimental Tool Loop could not safely produce a complete Build payload; using stable BuildOrchestrator.',
    status: 'blocked',
    payload: {
      fallbackReason: 'tool_permission_denied',
      userMessage: '实验 Tool Loop 已回退到稳定构建链路。',
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });

  const text = collectProcessText(turn);
  assert.match(text, /Tool Loop 实验已启动/);
  assert.match(text, /第 1 轮工具决策/);
  assert.match(text, /请求工具：inspect_current_flow/);
  assert.match(text, /工具完成：inspect_current_flow/);
  assert.match(text, /工具结果已回填/);
  assert.match(text, /LLM 已给出 final/);
  assert.match(text, /实验草稿已通过校验/);
  assert.match(text, /已回退稳定构建链路：工具权限被拒绝，已回退稳定构建链路/);
  assert.equal(panel.isGenerating, false);
  assert.equal(panel.isCancellingGenerate, false);
  assert.equal(transportClosed, false);
  assert.equal(panel.activeAgentRunTransport !== null, true);
});

test('AgentRun tool_call events render LLM requested tool cards', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'tool_loop' });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_tool_call';

  panel._handleAgentRunEvent({
    runId: 'ar_tool_call',
    sequence: 5,
    eventType: 'tool_call.requested',
    stage: 'tool_loop',
    title: 'Tool call requested: inspect_current_flow',
    summary: 'LLM requested a metadata-only Vision Agent tool.',
    status: 'running',
    payload: {
      toolName: 'inspect_current_flow',
      permission: 'ReadOnly',
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });
  panel._handleAgentRunEvent({
    runId: 'ar_tool_call',
    sequence: 6,
    eventType: 'tool_call.denied',
    stage: 'tool_loop',
    title: 'Tool call denied: runtime_package_precheck',
    summary: 'LLM requested tool was denied by the experimental permission gate.',
    status: 'blocked',
    payload: {
      toolName: 'runtime_package_precheck',
      errorCode: 'tool_permission_denied',
      firstFixRecommendation: 'Remove the blocked tool intent or retry in a mode that only uses metadata-only review tools.',
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(turn.toolsSection.hidden, false);
  assert.match(turn.toolsBody.innerHTML, /inspect_current_flow/);
  assert.match(turn.toolsBody.innerHTML, /runtime_package_precheck/);
  assert.match(turn.toolsBody.innerHTML, /已阻断|blocked|warning/);
});

test('AgentRun tool_loop draft rejected releases generating state', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'tool_loop' });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_tool_loop_rejected';
  panel.isGenerating = true;
  panel.isCancellingGenerate = true;

  panel._handleAgentRunEvent({
    runId: 'ar_tool_loop_rejected',
    sequence: 11,
    eventType: 'tool_loop.draft.rejected',
    stage: 'tool_loop',
    title: 'Tool Loop draft rejected',
    summary: 'draft rejected',
    status: 'warning',
    payload: {
      rejectionReason: 'validate_flow_failed',
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.match(collectProcessText(turn), /实验草稿未通过校验：流程校验未通过，已回退稳定构建链路/);
  assert.equal(panel.isGenerating, false);
  assert.equal(panel.isCancellingGenerate, false);
  assert.equal(turn.statusEl.textContent, '草稿验收未通过');
});

test('AgentRun duplicate replay events are ignored by run sequence and type', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_dupe';
  const evt = {
    runId: 'ar_dupe',
    sequence: 4,
    eventType: 'stage.completed',
    stage: 'readiness',
    title: 'Readiness checked',
    summary: 'Ready.',
    status: 'completed',
    metadataOnly: true,
    redactionPass: true
  };

  panel._handleAgentRunEvent(evt);
  panel._handleAgentRunEvent(evt);

  assert.equal(panel.activeAgentRunEvents.length, 1);
  assert.equal(turn.processBody.children.length, 1);
  assert.equal(panel._getPublicLiveEventStats().duplicate, 1);
});

test('AgentRun tool events render tool name duration report and blocked reasons', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_tool';

  panel._handleAgentRunEvent({
    runId: 'ar_tool',
    sequence: 5,
    eventType: 'tool.call.completed',
    stage: 'readiness',
    title: 'Tool completed: validate_flow',
    summary: 'Tool completed.',
    status: 'completed',
    payload: {
      toolName: 'validate_flow',
      durationMs: 42,
      summary: 'Validated draft metadata.',
      reportId: 'agent-report-42',
      blockedReasons: ['missing threshold']
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(turn.toolsSection.hidden, false);
  assert.equal(turn.toolsBody.children.length, 1);
  assert.match(turn.toolsBody.children[0].innerHTML, /validate_flow/);
  assert.match(turn.toolsBody.children[0].innerHTML, /42 ms/);
  assert.match(turn.toolsBody.children[0].innerHTML, /agent-report-42/);
  assert.match(turn.toolsBody.children[0].innerHTML, /缺少阈值/);
});

test('AgentRun artifact events render report result cards', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_artifact';

  panel._handleAgentRunEvent({
    runId: 'ar_artifact',
    sequence: 6,
    eventType: 'release.review.completed',
    stage: 'release_review',
    title: 'Release review completed',
    summary: 'Metadata-only release review passed.',
    status: 'completed',
    payload: {
      reportId: 'release-review-1',
      blockedReasons: [],
      firstFixRecommendation: 'No fix required.'
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(turn.artifactsSection.hidden, false);
  assert.equal(turn.artifactsBody.children.length, 1);
  assert.match(turn.artifactsBody.children[0].innerHTML, /发布复核已完成/);
  assert.match(turn.artifactsBody.children[0].innerHTML, /release-review-1/);
  assert.match(turn.artifactsBody.children[0].innerHTML, /当前无需修复/);
});

test('AgentRun failed tool event displays first fix recommendation', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_tool_fail';

  panel._handleAgentRunEvent({
    runId: 'ar_tool_fail',
    sequence: 7,
    eventType: 'tool.call.failed',
    stage: 'readiness',
    title: 'Tool failed: manifest_dryrun',
    summary: 'Manifest dry-run was blocked.',
    status: 'failed',
    payload: {
      toolName: 'manifest_dryrun',
      durationMs: 9,
      blockedReasons: ['missing operator contract'],
      firstFixRecommendation: 'Add the missing operator contract metadata.'
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.match(turn.toolsBody.children[0].innerHTML, /manifest_dryrun/);
  assert.match(turn.toolsBody.children[0].innerHTML, /missing operator contract/);
  assert.match(turn.toolsBody.children[0].innerHTML, /Add the missing operator contract metadata/);
});

test('AgentRun run.failed renders failure diagnosis and first fix only', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_failed';

  panel._handleAgentRunEvent({
    runId: 'ar_failed',
    sequence: 8,
    eventType: 'run.failed',
    stage: 'run',
    title: 'Run failed',
    summary: 'Workflow metadata was incomplete.',
    status: 'failed',
    payload: {
      firstFixRecommendation: 'Provide missing model metadata.'
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(turn.failureSection.hidden, false);
  assert.match(turn.failureBody.innerHTML, /Workflow metadata was incomplete/);
  assert.match(turn.failureBody.innerHTML, /Provide missing model metadata/);
  assert.doesNotMatch(turn.failureBody.innerHTML, /chain.?of.?thought|raw prompt|system prompt/i);
  assert.equal(panel.lastWorkbenchState, 'failed');
  assert.equal(turn.statusEl.textContent, '构建失败');
});

test('AgentRun BuildFromPlan run.failed applies canonical readiness without legacy scene clarification', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.activeAgentRunId = 'ar_build_from_plan_blocked';
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    planId: 'plan_build_from_plan_blocked',
    planHash: 'sha256:block-me',
    canBuild: true,
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: 'Ready',
      contractVersion: 'v2'
    }
  }));

  panel._handleAgentRunEvent({
    runId: 'ar_build_from_plan_blocked',
    sequence: 8,
    eventType: 'run.failed',
    stage: 'run',
    title: 'Run failed',
    summary: 'Canonical readiness blocked Build.',
    status: 'failed',
    payload: {
      status: 'clarification_required',
      failureType: 'clarification_required',
      planId: 'plan_build_from_plan_blocked',
      planHash: 'sha256:block-me',
      buildFromPlan: { planId: 'plan_build_from_plan_blocked', planHash: 'sha256:block-me' },
      planSnapshot: {
        planId: 'plan_build_from_plan_blocked',
        planHash: 'sha256:block-me'
      },
      buildReadiness: {
        canBuild: false,
        blockers: [
          {
            id: 'hard_requirement:image_source',
            category: 'hard_requirement',
            field: 'image_source',
            blocksBuild: true,
            resolutionMode: 'answer_question'
          }
        ],
        resolvedFields: ['inspection_object', 'task_type'],
        remainingFields: ['image_source', 'acceptance_criteria'],
        primaryMessage: 'Canonical readiness blocked Build.',
        contractVersion: 'v2'
      },
      blockingClarificationFields: ['image_source', 'acceptance_criteria'],
      requirementMaturity: {
        canPlan: true,
        canBuild: false,
        missingFields: ['image_source', 'acceptance_criteria'],
        publicReason: 'Canonical readiness blocked Build.'
      },
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(panel.pendingVisionPlan.executable, false);
  assert.equal(panel.pendingVisionPlan.buildReadiness.canBuild, false);
  assert.deepEqual(panel.pendingVisionPlan.remainingPlanFields, ['image_source', 'acceptance_criteria']);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(panel.lastWorkbenchState, 'clarifying');
  assert.equal(turn.statusEl.textContent, '待澄清');
  const renderedText = `${turn.failureBody.innerHTML} ${panel.container.querySelector('#ai-plan-workspace').innerHTML}`;
  assert.doesNotMatch(renderedText, /请确认这是外观缺陷、漏装有无、线序判定还是尺寸测量场景/);
});

test('AgentRun BuildFromPlan system_error without authoritative readiness stays failed', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_build_system_error';
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    planId: 'plan_system_error',
    planHash: 'sha256:system-error',
    canBuild: true,
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: 'Ready',
      contractVersion: 'v2'
    }
  }));

  panel._handleAgentRunEvent({
    runId: 'ar_build_system_error',
    sequence: 8,
    eventType: 'run.failed',
    stage: 'run',
    title: 'Run failed',
    summary: 'Build failed before completion.',
    status: 'failed',
    payload: {
      status: 'failed',
      failureType: 'system_error',
      planId: 'plan_system_error',
      planHash: 'sha256:system-error',
      planSnapshot: {
        planId: 'plan_system_error',
        planHash: 'sha256:system-error',
        buildReadiness: {
          canBuild: true,
          blockers: [],
          resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
          remainingFields: [],
          primaryMessage: 'Snapshot should not drive failure state',
          contractVersion: 'v2'
        }
      },
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(panel.lastWorkbenchState, 'failed');
  assert.notEqual(panel.agentWorkspaceMode, 'plan');
  assert.equal(turn.statusEl.textContent, '构建失败');
});

test('BuildFromPlan canonical state rejects stale PlanId and PlanHash responses', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    planId: 'plan_current',
    planHash: 'sha256:current',
    canBuild: true
  }));

  const readiness = {
    canBuild: false,
    blockers: [
      {
        id: 'hard_requirement:image_source',
        category: 'hard_requirement',
        field: 'image_source',
        blocksBuild: true,
        resolutionMode: 'answer_question'
      }
    ],
    resolvedFields: ['inspection_object'],
    remainingFields: ['image_source'],
    primaryMessage: 'Blocked',
    contractVersion: 'v2'
  };

  assert.equal(panel._applyBuildFromPlanCanonicalState({
    planId: 'plan_stale',
    planHash: 'sha256:current',
    buildReadiness: readiness
  }), false);
  assert.equal(panel.pendingVisionPlan.executable, true);

  assert.equal(panel._applyBuildFromPlanCanonicalState({
    planId: 'plan_current',
    planHash: 'sha256:stale',
    buildReadiness: readiness
  }), false);
  assert.equal(panel.pendingVisionPlan.executable, true);
});

test('AgentRun non-Planner blockers render unified failure reason and next action', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_blockers';

  panel._handleAgentRunEvent({
    runId: 'ar_blockers',
    sequence: 1,
    eventType: 'readiness.checked',
    stage: 'readiness',
    title: 'Readiness checked',
    summary: 'Deployment blocked by missing resources.',
    status: 'blocked',
    payload: {
      warningCode: 'readiness_blocked',
      blockedReasons: ['missing model_resource', 'missing output_channel'],
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.match(collectProcessText(turn), /就绪检查阻断/);
  assert.match(collectProcessText(turn), /缺失资源|人工参数|运行包元数据/);
  assert.equal(turn.failureSection.hidden, false);
  assert.match(turn.failureBody.innerHTML, /失败原因/);
  assert.match(turn.failureBody.innerHTML, /就绪检查阻断/);
  assert.match(turn.failureBody.innerHTML, /下一步/);

  panel._handleAgentRunEvent({
    runId: 'ar_blockers',
    sequence: 2,
    eventType: 'station.compatibility.completed',
    stage: 'station_compatibility',
    title: 'Station compatibility completed',
    summary: 'Station compatibility blocked.',
    status: 'failed',
    payload: {
      warningCode: 'station_incompatible',
      blockedReasons: ['camera allowlist missing'],
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  });

  assert.match(collectProcessText(turn), /工站兼容性阻断/);
  assert.match(turn.failureBody.innerHTML, /工站兼容性阻断/);
  assert.match(turn.failureBody.innerHTML, /工站能力|allowlist|运行配置/);
});

test('Stale AgentRun events cannot pollute current public live turn', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_current';

  panel._handleAgentRunEvent({
    runId: 'ar_old',
    sequence: 99,
    eventType: 'run.failed',
    stage: 'run',
    title: 'Old run failed',
    summary: 'old failure should not render',
    status: 'failed',
    payload: { firstFixRecommendation: 'old fix should not render' },
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(panel.publicLiveEvents.length, 0);
  assert.equal(turn.processBody.children.length, 0);
  assert.equal(turn.failureSection.hidden, false);
  assert.equal(turn.failureBody.innerHTML, '');
  assert.equal(panel._getPublicLiveEventStats().stale, 1);
});

test('AgentRun terminal completed event closes source and releases generating state', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  let closed = false;
  panel.activeAgentRunId = 'ar_done';
  panel.activeAgentRunTransport = { close: () => { closed = true; } };
  panel.isGenerating = true;
  panel._displayResult = () => {};

  panel._handleAgentRunEvent({
    runId: 'ar_done',
    sequence: 9,
    eventType: 'run.completed',
    stage: 'run',
    title: 'Run completed',
    summary: 'Release review completed.',
    status: 'completed',
    payload: buildResultContractPayload(),
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(closed, true);
  assert.equal(panel.activeAgentRunTransport, null);
  assert.equal(panel.isGenerating, false);
  assert.equal(turn.statusEl.textContent, '构建完成');
  assert.equal(panel.lastResultStatusNote.text, '发布复核已完成');
});

test('AgentRun completed without replayable draft does not mark workbench apply-ready', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  let closed = false;
  panel.activeAgentRunId = 'ar_done_missing_payload';
  panel.activeAgentRunTransport = { close: () => { closed = true; } };
  panel.isGenerating = true;

  panel._handleAgentRunEvent({
    runId: 'ar_done_missing_payload',
    sequence: 9,
    eventType: 'run.completed',
    stage: 'run',
    title: 'Run completed',
    summary: 'Done.',
    status: 'completed',
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(closed, true);
  assert.equal(panel.isGenerating, false);
  assert.equal(turn.statusEl.textContent, '构建完成但草稿缺失');
  assert.equal(panel.lastWorkbenchState, 'failed');
  assert.match(panel.lastResultStatusNote.text, /没有收到可回放流程草稿/);
  assert.notEqual(panel.currentResult?.compatibilityDiagnosticCode, 'legacy_build_artifact_missing_canonical_flow');
});

test('AgentRun cancelled event sets cancelled UI state', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  let closed = false;
  panel.activeAgentRunId = 'ar_cancel';
  panel.activeAgentRunTransport = { close: () => { closed = true; } };
  panel.isGenerating = true;

  panel._handleAgentRunEvent({
    runId: 'ar_cancel',
    sequence: 10,
    eventType: 'run.cancelled',
    stage: 'run',
    title: 'Run cancelled',
    summary: 'Cancelled by user.',
    status: 'cancelled',
    metadataOnly: true,
    redactionPass: true
  });

  assert.equal(closed, true);
  assert.equal(panel.activeAgentRunTransport, null);
  assert.equal(panel.isGenerating, false);
  assert.equal(turn.statusEl.textContent, '已取消');
  assert.equal(panel.lastWorkbenchState, 'cancelled');
});

test('legacy WebMessage fallback drops hidden thinking and renders only public diagnostics', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.isGenerating = true;
  panel.activeGenerateRequestId = 'req-legacy';

  panel._handleStreamChunk({
    payload: {
      requestId: 'req-legacy',
      chunkType: 'thinking',
      content: 'SECRET_THINKING_TRACE'
    }
  });
  await flushAsync();

  assert.doesNotMatch(`${turn.reasoningBody.textContent}\n${turn.replyBody.textContent}`, /SECRET_THINKING_TRACE/);

  panel._handleStreamChunk({
    payload: {
      requestId: 'req-legacy',
      chunkType: 'content',
      content: 'Public reply.'
    }
  });
  await flushAsync();

  assert.match(turn.replyBody.textContent, /Public reply/);
  assert.doesNotMatch(turn.reasoningBody.textContent, /SECRET_THINKING_TRACE/);

  const chatContainer = createFakeElement();
  panel.container = createContainer({ '#ai-chat-container': chatContainer });
  const rendered = panel._renderAssistantTurnFromPayload({
    payload: {
      reply: 'Done.',
      reasoning: 'SECRET_REASONING_TRACE',
      thinking: 'SECRET_THOUGHT_FIELD',
      publicDiagnostics: ['Public diagnostic.'],
      executionTrace: [
        {
          stage: 'planner',
          status: 'completed',
          summary: 'Public trace.',
          reasoning: 'SECRET_NESTED_REASONING'
        }
      ]
    }
  });

  assert.ok(rendered);
  assert.match(rendered.reasoningBody.textContent, /Public diagnostic/);
  assert.match(rendered.reasoningBody.textContent, /Public trace/);
  assert.doesNotMatch(`${rendered.reasoningBody.textContent}\n${rendered.replyBody.textContent}`, /SECRET_/);
});

test('AgentRun source guard registers frontend stream transports and cancel endpoint without shell tools', () => {
  const currentFile = fileURLToPath(import.meta.url);
  const testProjectRoot = path.resolve(path.dirname(currentFile), '..', '..');
  const productRoot = path.resolve(testProjectRoot, '..', '..');
  const agentRunSource = fs.readFileSync(
    path.resolve(productRoot, 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'ai', 'aiPanelAgentRun.js'),
    'utf8'
  );

  assert.match(agentRunSource, /fetch\(/);
  assert.match(agentRunSource, /httpClient\.defaultHeaders/);
  assert.match(agentRunSource, /\/stream-token/);
  assert.match(agentRunSource, /new window\.EventSource/);
  assert.match(agentRunSource, /\/ai\/agent-runs\/\$\{encodeURIComponent\(this\.runId\)\}\/events/);
  assert.match(agentRunSource, /\/ai\/agent-runs\/\$\{encodeURIComponent\(runId\)\}\/cancel/);
  assert.doesNotMatch(agentRunSource, /chain.?of.?thought|reasoning_content|\bsystemPrompt\b|\buserPrompt\b|powershell|cmd\.exe|child_process|process\./i);
});

test('Vision Agent source guard has no legacy ClarificationPlanCard production path', () => {
  const currentFile = fileURLToPath(import.meta.url);
  const testProjectRoot = path.resolve(path.dirname(currentFile), '..', '..');
  const productRoot = path.resolve(testProjectRoot, '..', '..');
  const workspaceSource = fs.readFileSync(
    path.resolve(productRoot, 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'ai', 'aiPanelAgentWorkspace.js'),
    'utf8'
  );

  assert.doesNotMatch(workspaceSource, /_buildIntentRouterClarificationPayload/);
  assert.doesNotMatch(workspaceSource, /_normalizeClarificationPlanBrief/);
  assert.doesNotMatch(workspaceSource, /_renderClarificationPlanWorkspace/);
  assert.doesNotMatch(workspaceSource, /ClarificationPlanCard/);
  assert.doesNotMatch(workspaceSource, /clarification_\$\{index \+ 1\}|clarification_1/);
});

test('response mapping displays missingResources', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole(agentResponse());

  assert.match(validation.innerHTML, /missingResources/);
  assert.match(validation.innerHTML, /op_detect\.ModelPath/);
});

test('response mapping displays pendingActions', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole(agentResponse());

  assert.match(validation.innerHTML, /pendingActions/);
  assert.match(validation.innerHTML, /ProvideModelPath/);
});

test('response mapping displays validationPreview sections', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole(agentResponse());

  assert.match(validation.innerHTML, /structuralValidation/);
  assert.match(validation.innerHTML, /dryRun/);
  assert.match(validation.innerHTML, /deploymentPrecheck/);
});

test('DryRun structure simulation uses dryRunSucceeded contract without legacy coverage failure', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole({
    dryRunResult: {
      dryRunSucceeded: true,
      executedOperators: ['op_acq', 'op_detect'],
      skippedOperators: [],
      warnings: [],
      blockingIssues: [],
      missingResources: [{ resourceKey: 'op_detect.ModelId', description: 'model metadata pending' }],
      dryRunSummary: 'Structure simulation completed.'
    }
  });

  assert.match(validation.innerHTML, /data-dryrun-contract="structure-simulation"/);
  assert.match(validation.innerHTML, /结构预演：通过/);
  assert.match(validation.innerHTML, /Structure simulation completed/);
  assert.doesNotMatch(validation.innerHTML, /DryRun 失败/);
  assert.doesNotMatch(validation.innerHTML, /分支覆盖 0\/0/);
});

test('validationPreview dryRun hides duplicate top-level DryRunResult card', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);
  const response = agentResponse();
  response.dryRunResult = {
    dryRunSucceeded: true,
    executedOperators: ['op_detect'],
    dryRunSummary: 'Duplicate top-level structure simulation.'
  };

  panel._renderValidationConsole(response);

  assert.match(validation.innerHTML, /data-validation-preview-section="dryRun"/);
  assert.doesNotMatch(validation.innerHTML, /data-dryrun-contract=/);
  assert.doesNotMatch(validation.innerHTML, /Duplicate top-level structure simulation/);
});

test('unknown DryRun contract displays unavailable instead of default failure', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole({ dryRunResult: { metadataOnly: true } });

  assert.match(validation.innerHTML, /data-dryrun-contract="unknown"/);
  assert.match(validation.innerHTML, /DryRun 状态不可用/);
  assert.doesNotMatch(validation.innerHTML, /is-failed/);
  assert.doesNotMatch(validation.innerHTML, /分支覆盖 0\/0/);
});

test('legacy execution DryRun with complete coverage renders branch statistics', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole({
    dryRunResult: {
      IsSuccess: true,
      CoveredBranches: 3,
      TotalBranches: 4,
      CoveragePercentage: 75,
      DurationMs: 4
    }
  });

  assert.match(validation.innerHTML, /data-dryrun-contract="execution-stub"/);
  assert.match(validation.innerHTML, /DryRun 通过/);
  assert.match(validation.innerHTML, /分支覆盖 3\/4 \(75\.0%\)/);
  assert.doesNotMatch(validation.innerHTML, /覆盖率数据未提供/);
});

test('legacy execution DryRun without coverage does not invent zero statistics', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole({
    dryRunResult: {
      IsSuccess: true,
      DurationMs: 5
    }
  });

  assert.match(validation.innerHTML, /data-dryrun-contract="execution-stub"/);
  assert.match(validation.innerHTML, /DryRun 通过/);
  assert.match(validation.innerHTML, /覆盖率数据未提供/);
  assert.doesNotMatch(validation.innerHTML, /分支覆盖 0\/0/);
  assert.doesNotMatch(validation.innerHTML, /ai-coverage-bar-fill/);
});

test('legacy execution DryRun preserves valid zero branch totals when coverage is explicit', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole({
    dryRunResult: {
      IsSuccess: true,
      CoveredBranches: 0,
      TotalBranches: 0,
      CoveragePercentage: 100,
      DurationMs: 4
    }
  });

  assert.match(validation.innerHTML, /data-dryrun-contract="execution-stub"/);
  assert.match(validation.innerHTML, /分支覆盖 0\/0 \(100\.0%\)/);
  assert.doesNotMatch(validation.innerHTML, /覆盖率数据未提供/);
});

test('legacy execution DryRun without isSuccess remains unavailable', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole({
    dryRunResult: {
      CoveredBranches: 3,
      TotalBranches: 4,
      CoveragePercentage: 75,
      DurationMs: 4
    }
  });

  assert.match(validation.innerHTML, /data-dryrun-contract="unknown"/);
  assert.match(validation.innerHTML, /DryRun 状态不可用/);
  assert.doesNotMatch(validation.innerHTML, /分支覆盖 3\/4/);
});

test('validationPreview structure dryRun renders explicit passed failed and unavailable states', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);
  const response = agentResponse();

  response.validationPreview.dryRun = {
    dryRunSucceeded: true,
    blockingIssues: [],
    warnings: [],
    executedOperators: ['op_detect'],
    skippedOperators: [],
    dryRunSummary: 'Metadata structure simulation passed.'
  };
  panel._renderValidationConsole(response);
  assert.match(validation.innerHTML, /data-structure-dryrun-status="passed"/);
  assert.match(validation.innerHTML, /结构预演：通过/);

  response.validationPreview.dryRun = {
    dryRunSucceeded: false,
    blockingIssues: [],
    warnings: [],
    executedOperators: ['op_detect'],
    skippedOperators: [],
    dryRunSummary: 'Backend marked structure simulation failed.'
  };
  panel._renderValidationConsole(response);
  assert.match(validation.innerHTML, /data-structure-dryrun-status="failed"/);
  assert.match(validation.innerHTML, /结构预演：未通过/);
  assert.match(validation.innerHTML, /Backend marked structure simulation failed/);

  response.validationPreview.dryRun = {
    blockingIssues: [],
    warnings: [],
    executedOperators: ['op_detect'],
    skippedOperators: [],
    dryRunSummary: 'Missing status field.'
  };
  panel._renderValidationConsole(response);
  assert.match(validation.innerHTML, /data-structure-dryrun-status="unavailable"/);
  assert.match(validation.innerHTML, /结构预演：状态不可用/);
  assert.doesNotMatch(validation.innerHTML, /data-dryrun-contract=/);
});

test('response mapping displays RuntimePreview adapter summary', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole(agentResponse());

  assert.match(validation.innerHTML, /runtimePreview/);
  assert.match(validation.innerHTML, /previewReady=true/);
  assert.match(validation.innerHTML, /adapterName=offline_runtime_preview/);
  assert.match(validation.innerHTML, /operator_result_metadata/);
  assert.match(validation.innerHTML, /binaryIncluded=false/);
});

test('response mapping displays RuntimePreview pilot permission fallback trace and pending actions', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);
  const response = agentResponse();
  response.validationPreview.runtimePreview = {
    previewReady: false,
    adapterName: 'pilot_runtime_preview',
    previewMode: 'metadata_only',
    permissionDecision: {
      allowed: false,
      reasonCode: 'runtime_preview_camera_not_allowlisted',
      pilotEnabled: true,
      metadataOnly: true,
      effectiveAdapterName: 'offline_runtime_preview',
      allowlistCounts: { camera: 1, model: 1, template: 1, flow: 1, resourceRoot: 1 }
    },
    resourceTrace: {
      allowed: false,
      reasonCode: 'runtime_preview_camera_not_allowlisted',
      resourceType: 'camera',
      normalizedKey: 'cam-missing',
      missingResources: [{ resourceType: 'camera', resourceKey: 'cam-missing', description: 'not allowlisted' }],
      trace: [{ resourceType: 'camera', reasonCode: 'allowlist_miss', allowed: false }]
    },
    readiness: {
      status: 'not_ready',
      canRunMetadataPilot: false,
      workflowDraftAllowed: true,
      blockingIssues: [],
      missingResources: [{ resourceType: 'camera', resourceKey: 'cam-missing', description: 'not allowlisted' }],
      pendingActions: [{ actionType: 'RuntimePreviewPilotReadinessReview', summary: 'Review readiness' }],
      resourceTrace: { allowed: false, reasonCode: 'runtime_preview_camera_not_allowlisted', resourceType: 'camera' },
      fallback: { used: true, fallbackAdapterName: 'offline_runtime_preview' },
      allowlistCoverage: { counts: { camera: 1 }, safeCatalogItems: 1 }
    },
    fallback: {
      used: true,
      fallbackAdapterName: 'offline_runtime_preview',
      reasonCode: 'runtime_preview_camera_not_allowlisted',
      reason: 'offline metadata fallback retained'
    },
    pendingActions: [{ actionType: 'RuntimePreviewPilotReadinessReview', summary: 'Review readiness' }],
    artifacts: [{ artifactId: 'operator-result-1', artifactType: 'operator_result_metadata', metadataOnly: true, binaryIncluded: false, byteLength: 0 }]
  };

  panel._renderValidationConsole(response);

  assert.match(validation.innerHTML, /adapterName=pilot_runtime_preview/);
  assert.match(validation.innerHTML, /previewMode=metadata_only/);
  assert.match(validation.innerHTML, /permission=denied/);
  assert.match(validation.innerHTML, /permissionReason=runtime_preview_camera_not_allowlisted/);
  assert.match(validation.innerHTML, /readiness=not_ready/);
  assert.match(validation.innerHTML, /canRunMetadataPilot=false/);
  assert.match(validation.innerHTML, /workflowDraftAllowed=true/);
  assert.match(validation.innerHTML, /fallbackAdapterName=offline_runtime_preview/);
  assert.match(validation.innerHTML, /resourceTrace=camera \/ runtime_preview_camera_not_allowlisted \/ cam-missing/);
  assert.match(validation.innerHTML, /RuntimePreviewPilotReadinessReview/);
});

test('RuntimePreview pilot developer status is hidden by default and visible in developer mode', async () => {
  const { AiPanel } = await loadAiPanel();
  const defaultPanel = createPanel(AiPanel);
  const developerPanel = createPanel(AiPanel, { developer: true });
  const runtimePreview = {
    previewReady: true,
    adapterName: 'pilot_runtime_preview',
    previewMode: 'metadata_only',
    permissionDecision: {
      allowed: true,
      pilotEnabled: true,
      metadataOnly: true,
      allowlistCounts: { camera: 2, model: 1, template: 1, flow: 0, resourceRoot: 0 }
    },
    readiness: {
      status: 'ready',
      canRunMetadataPilot: true,
      workflowDraftAllowed: true,
      allowlistCoverage: { safeCatalogItems: 4, allowlistedCatalogItems: 3 }
    }
  };

  const defaultHtml = defaultPanel._renderAgentValidationArtifacts({ validationPreview: { runtimePreview } });
  const developerHtml = developerPanel._renderAgentValidationArtifacts({ validationPreview: { runtimePreview } });

  assert.match(defaultHtml, /data-runtime-preview-pilot-status="true" hidden/);
  assert.match(developerHtml, /data-runtime-preview-pilot-status="true"/);
  assert.doesNotMatch(developerHtml, /data-runtime-preview-pilot-status="true" hidden/);
  assert.match(developerHtml, /pilotEnabled=true/);
  assert.match(developerHtml, /metadataOnly=true/);
  assert.match(developerHtml, /allowlistCounts=camera=2 \/ model=1 \/ template=1 \/ flow=0 \/ root=0/);
  assert.match(developerHtml, /readinessCoverage=/);
  assert.match(developerHtml, /realResourcesTouched=false/);
});

test('RuntimePreview UI redacts bytes paths IP BaseUrl and key fragments', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true });
  const { validation } = attachValidationPanel(panel);
  const response = agentResponse();
  response.validationPreview.runtimePreview = {
    previewReady: false,
    adapterName: 'pilot_runtime_preview',
    previewMode: 'metadata_only',
    permissionDecision: {
      allowed: false,
      reason: 'http://192.0.2.10:8317/v1 should not show',
      pilotEnabled: true,
      metadataOnly: true
    },
    resourceTrace: {
      allowed: false,
      reasonCode: 'runtime_preview_external_path_denied',
      resourceType: 'external_path',
      resourceId: 'C:\\secret\\live.png',
      normalizedKey: 'C:\\secret\\live.png',
      missingResources: [{ resourceKey: 'C:\\secret\\live.png', description: 'apiKey=VALUE_SHOULD_HIDE encoded image' }]
    },
    fallback: { used: true, fallbackAdapterName: 'offline_runtime_preview', reason: 'Bearer test token and BaseUrl http://192.0.2.10:8317/v1' },
    warnings: [{ message: 'C:\\secret\\template.png' }],
    issues: [{ description: 'apiKey=VALUE_SHOULD_HIDE' }],
    pendingActions: [{ actionType: 'RuntimePreviewPilotReadinessReview', summary: 'Authorization Bearer secret' }],
    artifacts: [{ artifactId: 'C:\\secret\\frame.png', artifactType: 'frame_metadata', metadataOnly: true, binaryIncluded: false, byteLength: 0 }]
  };

  panel._renderValidationConsole(response);

  assert.doesNotMatch(validation.innerHTML, /192\.0\.2\.10/);
  assert.doesNotMatch(validation.innerHTML, /VALUE_SHOULD_HIDE/);
  assert.doesNotMatch(validation.innerHTML, /secret/);
  assert.doesNotMatch(validation.innerHTML, /\.png/);
  assert.doesNotMatch(validation.innerHTML, /base64/i);
  assert.match(validation.innerHTML, /&lt;redacted&gt;/);
});

test('response mapping displays folded toolTrace summary only', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole(agentResponse());

  assert.match(validation.innerHTML, /<details class="ai-agent-tool-trace"/);
  assert.doesNotMatch(validation.innerHTML, /<details class="ai-agent-tool-trace"[^>]*open/);
  assert.match(validation.innerHTML, /工具轨迹/);
  assert.match(validation.innerHTML, /流程校验工具 已通过 12ms/);
  assert.match(validation.innerHTML, /工具 已通过 7ms/);
  assert.doesNotMatch(validation.innerHTML, /运行预演回放工具/);
  assert.doesNotMatch(validation.innerHTML, /validate_flow:Simulation/);
});

test('tool evidence status contract maps completed warning skipped denied and unknown without fake failures', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole({
    toolTrace: [
      { ToolName: 'validate_flow', Status: 'completed', DurationMs: 3 },
      { toolName: 'dryrun_flow', status: 'warning', durationMs: 5 },
      { toolName: 'runtime_package_precheck', status: 'skipped', durationMs: 0 },
      { toolName: 'write_project_file', status: 'denied', durationMs: 1 },
      { toolName: 'station_compatibility_checker', durationMs: 2 }
    ]
  });

  assert.match(validation.innerHTML, /流程校验工具 已完成 3ms/);
  assert.match(validation.innerHTML, /title="dryrun_flow">dryrun_flow/);
  assert.match(validation.innerHTML, /title="warning">警告/);
  assert.match(validation.innerHTML, /已跳过 0ms/);
  assert.match(validation.innerHTML, /拒绝 1ms/);
  assert.match(validation.innerHTML, /已记录\/状态不可用 2ms/);
  assert.doesNotMatch(validation.innerHTML, /流程校验工具 失败 3ms/);
});

test('pendingParameters can locate operator parameter from missingResources', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    options: {
      getOperators: () => [
        {
          type: 'DeepLearning',
          parameters: [
            { name: 'ModelPath', dataType: 'file', displayName: 'Model path' }
          ]
        }
      ]
    }
  });
  const response = agentResponse();
  const pending = panel._resolvePendingParametersForDraft(response);

  panel._rebuildPendingOperatorBindings({ pending, flow: response.flow, preferIndexFallback: true });
  panel._syncPendingParameterDrafts(response, response.flow, { force: true });
  const groups = panel._collectPendingDraftGroups(pending, response.flow.operators);

  assert.equal(groups[0].operatorId, 'op_detect');
  assert.equal(groups[0].fields[0].parameterName, 'ModelPath');
  assert.equal(groups[0].fields[0].isPendingPlaceholder, true);
});

test('missing resource workflow remains editable as draft', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);

  const state = panel._deriveAgentDeploymentUiState(agentResponse());

  assert.equal(state.workflowDraftAllowed, true);
  assert.equal(state.workflowEditingAllowed, true);
});

test('readyForDeployment=false disables deployment actions but not workflow editing', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const { validation } = attachValidationPanel(panel);

  panel._renderValidationConsole(agentResponse());

  assert.match(validation.innerHTML, /data-agent-deployment-disabled="true"/);
  assert.match(validation.innerHTML, /data-agent-workflow-edit-enabled="true"/);
});

test('AI followup renders actionable resource binding entries in Chinese', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    options: { getOperators: resourceBindingOperatorMetadata }
  });
  const followups = createFakeElement();
  panel.container = createContainer({
    '#ai-result-followups': followups
  });
  const response = resourceBindingResponse();

  panel._renderFollowupChecklist(response, response.flow);

  assert.match(followups.innerHTML, /资源审计任务/);
  for (const label of [
    '人工确认模型资源',
    '人工选择模板资源',
    '人工填写标定参数',
    '人工选择相机绑定',
    '人工确认输出通道',
    '仅记录 PLC 元数据',
    '稍后处理'
  ]) {
    assert.match(followups.innerHTML, new RegExp(label));
  }
  assert.match(followups.innerHTML, /资源审计任务/);
  assert.match(followups.innerHTML, /影响算子/);
  assert.match(followups.innerHTML, /影响参数/);
  assert.match(followups.innerHTML, /阻断原因/);
  assert.match(followups.innerHTML, /AI 建议/);
  assert.match(followups.innerHTML, /人工确认输入区/);
  assert.match(followups.innerHTML, /查看技术详情/);
  assert.match(followups.innerHTML, /仅记录 metadata，不触发真实 PLC 写入/);
  assert.doesNotMatch(followups.innerHTML, /自动绑定|自动选择|自动部署|智能自动补齐/);
  assert.doesNotMatch(followups.innerHTML, /rawPrompt|systemPrompt|chainOfThought|data:image\/png;base64|sk-secret|192\.168\./i);
});

test('resource binding action writes metadata and updates pending, missing, and apply gate state', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    options: { getOperators: resourceBindingOperatorMetadata }
  });
  const response = resourceBindingResponse();
  response.workflowDiff = {
    deploymentBlockers: [
      'missing_model_resource:op_detect.ModelPath',
      'missing_camera_binding:op_acq.CameraId'
    ],
    pendingParameters: [
      'provide op_detect.ModelPath before deployment',
      'provide op_acq.CameraId before deployment'
    ]
  };
  response.applyGate.deploymentBlockers.push('missing_model_resource:op_detect.ModelPath');
  panel.currentResult = response;
  panel.currentResultVersion = 1;
  panel.container = createContainer({
    '#ai-result-followups': createFakeElement(),
    '#ai-result-parameter-editor': createFakeElement(),
    '#ai-result-validation-card': createFakeElement(),
    '#ai-result-validation': createFakeElement(),
    '#ai-btn-apply': createFakeButton()
  });

  const modelResource = response.missingResources.find(item => item.resourceType === 'model_resource');
  const updated = panel._handleMissingResourceAction(modelResource, 'bind_model_resource', {
    value: 'model-resource:scratch-v1',
    data: response,
    flow: response.flow
  });

  assert.equal(updated, true);
  assert.equal(response.missingResources.some(item => item.resourceKey === 'op_detect.ModelPath'), false);
  assert.equal(response.pendingParameters.some(item => item.operatorId === 'op_detect'), false);
  assert.equal(response.applyGate.deploymentReady, false);
  assert.equal(response.applyGate.deploymentBlockers.includes('op_detect.ModelPath'), false);
  assert.equal(response.applyGate.deploymentBlockers.includes('missing_model_resource:op_detect.ModelPath'), false);
  assert.equal(response.workflowDiff.deploymentBlockers.includes('missing_model_resource:op_detect.ModelPath'), false);
  assert.equal(response.workflowDiff.deploymentBlockers.includes('missing_camera_binding:op_acq.CameraId'), true);
  assert.equal(response.workflowDiff.pendingParameters.includes('provide op_detect.ModelPath before deployment'), false);
  assert.equal(response.flow.operators.find(op => op.tempId === 'op_detect').parameters.ModelPath, 'model-resource:scratch-v1');
  assert.equal(panel.pendingResourceDrafts['op_detect.ModelPath'].metadataOnly, true);
  assert.equal(panel.pendingResourceDrafts['op_detect.ModelPath'].confirmedBy, 'local-user');
  assert.equal(response.manualResourceConfirmations.length, 1);
  assert.equal(response.manualResourceConfirmations[0].metadataOnly, true);
  assert.match(response.manualResourceConfirmations[0].actionLabel, /人工确认模型资源/);
  assert.match(response.manualResourceConfirmations[0].writebackSummary, /model-resource:scratch-v1/);
  assert.match(panel.lastResultStatusNote.text, /仍有 9 项部署前待补/);
  assertNoSensitiveLeak([
    panel.lastResultStatusNote.text,
    ...(panel.messages || []).map(item => item.text || '')
  ].join('\n'));
});

test('resource binding action updates PascalCase replay payloads by ActualOperatorId', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    options: { getOperators: resourceBindingOperatorMetadata }
  });
  const response = {
    Flow: {
      Operators: [
        {
          TempId: 'op_detect',
          OperatorType: 'DeepLearning',
          DisplayName: '缺陷检测',
          Parameters: { ModelPath: '<pending-model-resource>' }
        }
      ],
      Connections: [],
      MetadataOnly: true
    },
    PendingParameters: [
      { ActualOperatorId: 'op_detect', ParameterNames: ['ModelPath'] }
    ],
    MissingResources: [
      {
        ResourceType: 'model_resource',
        ActualOperatorId: 'op_detect',
        ParameterName: 'ModelPath',
        Description: '部署前绑定模型资源元数据。'
      }
    ],
    ApplyGate: {
      CanvasApplyReady: true,
      RuntimeDraftReady: true,
      DeploymentReady: false,
      Blocked: false,
      Status: 'canvas_apply_ready',
      DeploymentBlockers: ['op_detect.ModelPath'],
      MetadataOnly: true
    },
    ValidationPreview: {
      DeploymentPrecheck: {
        ReadyForDeployment: false,
        WorkflowDraftAllowed: true,
        DeploymentBlocked: true,
        StationTouched: false
      }
    }
  };
  panel.currentResult = response;
  panel.currentResultVersion = 1;
  panel.container = createContainer({
    '#ai-result-followups': createFakeElement(),
    '#ai-result-parameter-editor': createFakeElement(),
    '#ai-result-validation-card': createFakeElement(),
    '#ai-result-validation': createFakeElement(),
    '#ai-btn-apply': createFakeButton()
  });

  const resource = response.MissingResources[0];
  const model = panel._getMissingResourceActionModel(resource);
  const updated = panel._handleMissingResourceAction(resource, model.action, {
    value: 'model-resource:pascal-v1',
    data: response,
    flow: response.Flow
  });

  assert.equal(updated, true);
  assert.deepEqual(response.MissingResources, []);
  assert.deepEqual(response.PendingParameters, []);
  assert.equal(response.ApplyGate.DeploymentReady, true);
  assert.equal(response.ApplyGate.Status, 'deployment_ready');
  assert.equal(response.ValidationPreview.DeploymentPrecheck.ReadyForDeployment, true);
  const operator = response.Flow.Operators[0];
  assert.equal(operator.Parameters.ModelPath, 'model-resource:pascal-v1');
  assert.equal(Object.prototype.hasOwnProperty.call(operator, 'parameters'), false);
  assert.equal(panel.pendingResourceDrafts['model_resource:op_detect:ModelPath'].value, 'model-resource:pascal-v1');
  assert.equal(panel.pendingResourceDrafts['model_resource:op_detect:ModelPath'].actualOperatorId, 'op_detect');
  assert.equal(response.ManualResourceConfirmations.length, 1);
  assert.equal(response.ManualResourceConfirmations[0].metadataOnly, true);
});

test('resolving all resource tasks updates apply gate without guessing file paths or PLC writes', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    options: { getOperators: resourceBindingOperatorMetadata }
  });
  const response = resourceBindingResponse();
  panel.currentResult = response;
  panel.currentResultVersion = 1;
  panel.container = createContainer({
    '#ai-result-followups': createFakeElement(),
    '#ai-result-parameter-editor': createFakeElement(),
    '#ai-result-validation-card': createFakeElement(),
    '#ai-result-validation': createFakeElement(),
    '#ai-btn-apply': createFakeButton()
  });
  const resources = [...response.missingResources];
  const valuesByType = new Map([
    ['model_resource', 'model-resource:scratch-v1'],
    ['template_artifact', 'template-artifact:fixture-a'],
    ['measurement_parameter', '0.024'],
    ['camera_binding', 'camera-binding:top-01'],
    ['output_channel', 'output-channel:qa-board'],
    ['plc_address', 'plc-metadata:db1-dbx0-0']
  ]);

  for (const resource of resources) {
    const model = panel._getMissingResourceActionModel(resource);
    panel._handleMissingResourceAction(resource, model.action, {
      value: valuesByType.get(resource.resourceType),
      data: response,
      flow: response.flow
    });
  }

  assert.deepEqual(response.missingResources, []);
  assert.deepEqual(response.pendingParameters, []);
  assert.equal(response.applyGate.deploymentReady, true);
  assert.equal(response.applyGate.status, 'deployment_ready');
  assert.equal(response.validationPreview.deploymentPrecheck.readyForDeployment, true);
  assert.equal(response.validationPreview.deploymentPrecheck.deploymentBlocked, false);
  assert.equal(response.firstFixRecommendation, '');
  assert.equal(response.manualResourceConfirmations.length, resources.length);
  const output = response.flow.operators.find(op => op.tempId === 'op_output');
  assert.equal(Object.prototype.hasOwnProperty.call(output.parameters, 'PlcAddress'), false);
  assert.equal(Object.prototype.hasOwnProperty.call(output.parameters, 'OutputChannel'), false);
  assertNoSensitiveLeak(JSON.stringify(response));
});

test('defer resource keeps DeploymentReady false and leaves the blocker visible', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    options: { getOperators: resourceBindingOperatorMetadata }
  });
  const response = resourceBindingResponse();
  panel.currentResult = response;
  panel.currentResultVersion = 1;
  panel.container = createContainer({
    '#ai-result-followups': createFakeElement(),
    '#ai-result-parameter-editor': createFakeElement(),
    '#ai-result-validation-card': createFakeElement(),
    '#ai-result-validation': createFakeElement(),
    '#ai-btn-apply': createFakeButton()
  });
  const outputResource = response.missingResources.find(item => item.resourceType === 'output_channel');

  const updated = panel._handleMissingResourceAction(outputResource, 'defer_resource', {
    data: response,
    flow: response.flow
  });

  assert.equal(updated, false);
  assert.equal(response.missingResources.some(item => item.resourceKey === 'op_output.OutputChannel'), true);
  assert.equal(response.applyGate.deploymentReady, false);
  assert.equal(panel.pendingResourceDrafts['op_output.OutputChannel'].status, 'deferred');
  assert.match(panel.lastResultStatusNote.text, /稍后处理/);
});

test('manual confirmation records render and survive replay payload rendering', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const followups = createFakeElement();
  panel.container = createContainer({
    '#ai-result-followups': followups
  });
  const response = {
    flow: { operators: [], connections: [], metadataOnly: true },
    pendingParameters: [],
    missingResources: [],
    manualResourceConfirmations: [
      {
        confirmedAtUtc: '2026-06-09T00:00:00Z',
        actor: 'local-user',
        resourceType: 'model_resource',
        affectedOperator: 'op_detect',
        affectedParameters: ['ModelPath'],
        resourceKey: 'op_detect.ModelPath',
        writebackSummary: 'model-resource:scratch-v1',
        metadataOnly: true,
        applyGateChange: {
          from: 'canvas_apply_ready',
          to: 'deployment_ready',
          clearedBlockers: ['op_detect.ModelPath']
        },
        deploymentBlocked: false
      }
    ]
  };

  panel._renderFollowupChecklist(response, response.flow);

  assert.match(followups.innerHTML, /人工确认记录/);
  assert.match(followups.innerHTML, /local-user/);
  assert.match(followups.innerHTML, /metadataOnly=true/);
  assert.match(followups.innerHTML, /model-resource:scratch-v1/);
  assert.doesNotMatch(followups.innerHTML, /rawPrompt|systemPrompt|chainOfThought|C:\\|192\.168\.|base64|token|key/i);
});

test('template artifact binding stays metadata-only and does not create fake TemplatePath parameter', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    options: { getOperators: resourceBindingOperatorMetadata }
  });
  const response = resourceBindingResponse();
  panel.currentResult = response;
  panel.currentResultVersion = 1;
  panel.container = createContainer({
    '#ai-result-followups': createFakeElement(),
    '#ai-result-parameter-editor': createFakeElement(),
    '#ai-result-validation-card': createFakeElement(),
    '#ai-result-validation': createFakeElement(),
    '#ai-btn-apply': createFakeButton()
  });
  const templateResource = response.missingResources.find(item => item.resourceType === 'template_artifact');

  panel._handleMissingResourceAction(templateResource, 'select_template_artifact', {
    value: 'template-artifact:fixture-a',
    data: response,
    flow: response.flow
  });

  const templateOperator = response.flow.operators.find(op => op.tempId === 'op_match');
  assert.equal(response.missingResources.some(item => item.resourceKey === 'op_match.Template'), false);
  assert.equal(Object.prototype.hasOwnProperty.call(templateOperator.parameters, 'TemplatePath'), false);
  assert.equal(Object.prototype.hasOwnProperty.call(templateOperator.parameters, 'Template'), false);
  assert.equal(panel.pendingResourceDrafts['op_match.Template'].value, 'template-artifact:fixture-a');
});

test('applied workspace tells users to review details on the flow page without bypassing deployment gate', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  const payload = buildResultContractPayload();
  panel.container = container;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.currentResult = {
    ...payload,
    flow: panel._getResultFlowForCanvas(payload)
  };
  panel.currentResultVersion = 1;
  panel.agentWorkspaceMode = 'build';
  panel.activeAgentRunEvents = [
    {
      runId: 'ar_build_contract',
      sequence: 20,
      eventType: 'run.completed',
      stage: 'run',
      title: 'Run completed',
      summary: 'Build completed.',
      status: 'completed',
      payload,
      metadataOnly: true,
      redactionPass: true
    }
  ];

  panel._executeApplyFlow(panel.currentResult.flow);
  panel.workbenchState = 'applied';
  panel._renderAgentWorkspaceOverview();
  panel._renderBuildWorkspaceFromAgentRun();

  assert.match(panel.lastResultStatusNote.text, /流程页点击算子进行细节复核与微调/);
  assert.match(panel.lastResultStatusNote.text, /流程页修改不会绕过部署门禁/);
  assert.match(elements['#ai-agent-workspace-overview'].innerHTML, /Applied 复核/);
  assert.match(elements['#ai-build-final-draft'].innerHTML, /已应用到画布/);
  assert.equal(panel._getPayloadApplyGate(panel.currentResult).deploymentReady, false);
});

test('AI pending draft excludes FilePath when ImageAcquisition SourceType is Camera', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const response = {
    flow: {
      operators: [
        {
          tempId: 'op_acq',
          operatorType: 'ImageAcquisition',
          displayName: '采集',
          parameters: { SourceType: 'Camera' }
        }
      ]
    },
    pendingParameters: [
      { operatorId: 'op_acq', parameterNames: ['SourceType', 'FilePath', 'CameraId', 'CameraBindingId'] }
    ]
  };

  const pending = panel._resolvePendingParametersForDraft(response);
  const groups = panel._collectPendingDraftGroups(pending, response.flow.operators);
  const names = groups.flatMap(group => group.fields.map(field => field.parameterName));

  assert.deepEqual(names.sort(), ['CameraBindingId', 'CameraId', 'SourceType'].sort());
  assert.equal(names.includes('FilePath'), false);
});

test('AI pending draft excludes camera binding fields when ImageAcquisition SourceType is File', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const response = {
    flow: {
      operators: [
        {
          tempId: 'op_acq',
          operatorType: 'ImageAcquisition',
          displayName: '采集',
          parameters: { SourceType: 'File' }
        }
      ]
    },
    pendingParameters: [
      { operatorId: 'op_acq', parameterNames: ['FilePath', 'CameraId', 'CameraBindingId'] }
    ]
  };

  const pending = panel._resolvePendingParametersForDraft(response);
  const groups = panel._collectPendingDraftGroups(pending, response.flow.operators);
  const names = groups.flatMap(group => group.fields.map(field => field.parameterName));

  assert.deepEqual(names, ['FilePath']);
});

test('parameter rules cover TemplateMatching template and ROI dependencies', async () => {
  const {
    collectEffectiveRequiredParameterErrors,
    getParameterEffectiveState,
    shouldIncludePendingParameter
  } = await loadParameterRules();
  const missingTemplate = {
    type: 'TemplateMatching',
    parameters: [
      { name: 'TemplatePath', value: '' },
      { name: 'TemplateId', value: '' },
      { name: 'UseRoi', value: true },
      { name: 'RoiWidth', value: '' },
      { name: 'RoiHeight', value: '' }
    ]
  };
  const configuredTemplate = {
    type: 'TemplateMatching',
    parameters: {
      TemplateId: 'tmpl-fixture',
      UseRoi: false,
      EnablePoseSearch: false
    }
  };

  assert.equal(getParameterEffectiveState(configuredTemplate, 'TemplatePath').effectiveDisabled, true);
  assert.equal(getParameterEffectiveState(configuredTemplate, 'RoiWidth').effectiveDisabled, true);
  assert.equal(shouldIncludePendingParameter(configuredTemplate, 'TemplatePath'), false);
  assert.equal(
    collectEffectiveRequiredParameterErrors(missingTemplate, missingTemplate.parameters)
      .some(error => error.kind === 'atLeastOneOf' && error.parameterNames.includes('TemplatePath')),
    true
  );
});

test('parameter rules cover DeepLearning model source and NMS dependencies', async () => {
  const {
    collectEffectiveRequiredParameterErrors,
    getParameterEffectiveState,
    shouldIncludePendingParameter
  } = await loadParameterRules();
  const modelById = {
    type: 'DeepLearning',
    parameters: {
      ModelId: 'wire-sequence-model',
      UseGpu: false,
      OutputFormat: 'EndToEndNms',
      EnableInternalNms: true
    }
  };
  const missingModel = {
    type: 'DeepLearning',
    parameters: [
      { name: 'ModelPath', value: '' },
      { name: 'ModelId', value: '' },
      { name: 'ModelCatalogPath', value: '' }
    ]
  };

  assert.equal(getParameterEffectiveState(modelById, 'ModelPath').effectiveDisabled, true);
  assert.equal(getParameterEffectiveState(modelById, 'GpuDeviceId').effectiveDisabled, true);
  assert.equal(getParameterEffectiveState(modelById, 'NmsIouThreshold').effectiveDisabled, true);
  assert.equal(shouldIncludePendingParameter(modelById, 'ModelPath'), false);
  assert.equal(
    collectEffectiveRequiredParameterErrors(missingModel, missingModel.parameters)
      .some(error => error.kind === 'atLeastOneOf' && error.parameterNames.includes('ModelPath')),
    true
  );
});

test('parameter rules cover ResultOutput channel file and PLC safety dependencies', async () => {
  const {
    collectEffectiveRequiredParameterErrors,
    getParameterEffectiveState,
    shouldIncludePendingParameter
  } = await loadParameterRules();
  const outputById = {
    type: 'ResultOutput',
    parameters: {
      OutputChannelId: 'qa-board',
      SaveToFile: false,
      Channel: 'plc'
    }
  };
  const missingChannel = {
    type: 'ResultOutput',
    parameters: [
      { name: 'Channel', value: '' },
      { name: 'OutputChannel', value: '' },
      { name: 'OutputChannelId', value: '' }
    ]
  };

  assert.equal(getParameterEffectiveState(outputById, 'Channel').effectiveDisabled, true);
  assert.equal(getParameterEffectiveState(outputById, 'FilePath').effectiveDisabled, true);
  assert.equal(getParameterEffectiveState(outputById, 'PlcAddress').effectiveDisabled, false);
  assert.equal(shouldIncludePendingParameter(outputById, 'Channel'), false);
  assert.equal(
    collectEffectiveRequiredParameterErrors(missingChannel, missingChannel.parameters)
      .some(error => error.kind === 'atLeastOneOf' && error.parameterNames.includes('OutputChannelId')),
    true
  );
});

test('shared parameter rule parity spec matches frontend effective states', async () => {
  const { getParameterEffectiveState } = await loadParameterRules();
  const spec = loadParameterRuleParitySpec();

  for (const parityCase of spec.cases) {
    const operator = {
      type: parityCase.operatorType,
      operatorType: parityCase.operatorType,
      parameters: parityCase.parameters
    };

    for (const [parameterName, expected] of Object.entries(parityCase.uiStates)) {
      const actual = getParameterEffectiveState(operator, parameterName);
      assert.equal(
        actual.effectiveRequired,
        expected.effectiveRequired,
        `${parityCase.caseId}.${parameterName}.effectiveRequired`
      );
      assert.equal(
        actual.effectiveDisabled,
        expected.effectiveDisabled,
        `${parityCase.caseId}.${parameterName}.effectiveDisabled`
      );
    }
  }
});

test('AI pending draft uses effectiveDisabled rules for precheck missing resources', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const response = {
    flow: {
      operators: [
        {
          tempId: 'op_detect',
          operatorType: 'DeepLearning',
          displayName: 'Detector',
          parameters: { ModelId: 'catalog-model' }
        }
      ]
    },
    missingResources: [
      { resourceType: 'model_path', resourceKey: 'op_detect.ModelPath' }
    ]
  };

  const pending = panel._resolvePendingParametersForDraft(response);
  const groups = panel._collectPendingDraftGroups(pending, response.flow.operators);

  assert.deepEqual(groups, []);
});

test('PropertyPanel model validation uses shared effectiveRequired rules beyond ImageAcquisition', async () => {
  const { PropertyPanel } = await loadPropertyPanel();
  const panel = createPropertyPanel(PropertyPanel, null);
  const operator = {
    id: 'dl',
    type: 'DeepLearning',
    parameters: [
      { name: 'ModelPath', value: '' },
      { name: 'ModelId', value: '' },
      { name: 'ModelCatalogPath', value: '' }
    ]
  };

  const errors = panel.validateOperatorModel(operator);

  assert.equal(errors.some(error => String(error.message).includes('ModelPath')), true);
});

test('AI pending parameter editor displays Chinese operator type metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, {
    options: {
      getOperators: () => [
        {
          type: 'ImageAcquisition',
          parameters: [
            { name: 'FilePath', displayName: '文件路径', dataType: 'file', value: '', isRequired: true },
          ]
        }
      ]
    }
  });
  const editor = createFakeElement();
  panel.container = createContainer({
    '#ai-result-parameter-editor': editor
  });
  const response = {
    flow: {
      operators: [
        {
          tempId: 'op_acq',
          operatorType: 'ImageAcquisition',
          displayName: '采集',
          parameters: { SourceType: 'File' }
        }
      ]
    },
    pendingParameters: [
      { operatorId: 'op_acq', parameterNames: ['FilePath'] }
    ]
  };

  panel._renderParameterDraftEditor(response, response.flow);

  assert.match(editor.innerHTML, /title="ImageAcquisition">\u56fe\u50cf\u91c7\u96c6/);
  assert.match(editor.innerHTML, /title="FilePath">\u6280\u672f\u952e/);
  assert.doesNotMatch(editor.innerHTML, /\u56fe\u50cf\u91c7\u96c6\uff08ImageAcquisition\uff09/);
});

test('PropertyPanel header displays Chinese operator type first', async () => {
  const { PropertyPanel } = await loadPropertyPanel();
  const panel = createPropertyPanel(PropertyPanel, imageAcquisitionOperator('File'));

  panel.render();

  assert.match(panel.container.innerHTML, /\u56fe\u50cf\u91c7\u96c6\uff08ImageAcquisition\uff09/);
});

test('PropertyPanel Camera mode does not mark FilePath required and does not require it', async () => {
  const { PropertyPanel } = await loadPropertyPanel();
  const operator = imageAcquisitionOperator('Camera', { filePath: '', cameraId: 'cam-1' });
  const panel = createPropertyPanel(PropertyPanel, operator);
  const filePathParam = operator.parameters.find(param => param.name === 'FilePath');
  const html = panel.renderParameter(filePathParam);

  installPropertyForm([
    createInput({ name: 'SourceType', value: 'Camera', dataType: 'enum' }),
    createInput({ name: 'FilePath', value: '', dataType: 'file' }),
    createInput({ name: 'CameraId', value: 'cam-1', dataType: 'cameraBinding' })
  ]);

  assert.doesNotMatch(html, /<span class="required">\*<\/span>/);
  assert.deepEqual(panel.collectCurrentOperatorValidationErrors(), []);
});

test('PropertyPanel File mode marks FilePath required and reports empty value', async () => {
  const { PropertyPanel } = await loadPropertyPanel();
  const operator = imageAcquisitionOperator('File', { filePath: '', cameraId: '' });
  const panel = createPropertyPanel(PropertyPanel, operator);
  const filePathParam = operator.parameters.find(param => param.name === 'FilePath');
  const html = panel.renderParameter(filePathParam);

  installPropertyForm([
    createInput({ name: 'SourceType', value: 'File', dataType: 'enum' }),
    createInput({ name: 'FilePath', value: '', dataType: 'file' }),
    createInput({ name: 'CameraId', value: '', dataType: 'cameraBinding' })
  ]);

  assert.match(html, /<span class="required">\*<\/span>/);
  const errors = panel.collectCurrentOperatorValidationErrors();
  assert.equal(errors.some(error => error.name === 'FilePath'), true);
  assert.equal(errors.some(error => error.name === 'CameraId'), false);
});

test('validateOperatorModel applies ImageAcquisition mutual exclusion rules off current node', async () => {
  const { PropertyPanel } = await loadPropertyPanel();
  const panel = createPropertyPanel(PropertyPanel, null);
  const cameraOperator = imageAcquisitionOperator('Camera', { filePath: '', cameraId: 'cam-1' });
  const fileOperator = imageAcquisitionOperator('File', { filePath: '', cameraId: '' });

  assert.deepEqual(panel.validateOperatorModel(cameraOperator), []);
  const fileErrors = panel.validateOperatorModel(fileOperator);
  assert.equal(fileErrors.some(error => error.name === 'FilePath'), true);
  assert.equal(fileErrors.some(error => error.name === 'CameraId'), false);
});

test('AI layout CSS places Agent workbench left and chat right with mobile fallback', () => {
  const currentFile = fileURLToPath(import.meta.url);
  const testProjectRoot = path.resolve(path.dirname(currentFile), '..', '..');
  const productRoot = path.resolve(testProjectRoot, '..', '..');
  const sourcePath = path.resolve(productRoot, 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'ai', 'aiPanel.js');
  const cssPath = path.resolve(productRoot, 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'shared', 'styles', 'ai-panel.css');
  const source = fs.readFileSync(sourcePath, 'utf8');
  const css = fs.readFileSync(cssPath, 'utf8');

  assert.match(source, /data-ai-workbench-pane="true"/);
  assert.match(source, /data-ai-chat-pane="true"/);
  assert.match(source, /aiPanelWorkbenchMixin/);
  assert.match(source, /aiPanelPendingParametersMixin/);
  assert.match(source, /aiPanelChatMixin/);
  assert.match(source, /aiPanelValidationPreviewMixin/);
  assert.ok(source.split(/\r?\n/).length < 2500);
  assert.match(source, /focus\(\{\s*preventScroll:\s*true\s*\}\)/);
  assert.match(css, /\.ai-view-container\s*{[^}]*--ai-surface-page:\s*#f3f6fa[^}]*background:\s*var\(--ai-surface-page\)/s);
  assert.match(css, /\[data-theme="dark"\]\s+\.ai-view-container\s*{[^}]*--ai-surface-page:\s*#14181d/s);
  assert.match(css, /\.ai-workspace\s*{[^}]*2\.05fr[^}]*clamp\(22rem,\s*26vw,\s*31rem\)/s);
  assert.match(css, /\.ai-pane-right\s*{[^}]*grid-column:\s*1;/s);
  assert.match(css, /\.ai-pane-left\s*{[^}]*grid-column:\s*2;/s);
  assert.match(css, /\.ai-results-scroll\s*{[^}]*grid-template-columns:\s*repeat\(12,\s*minmax\(0,\s*1fr\)\)/s);
  assert.match(css, /@media \(max-width:\s*1180px\)[\s\S]*\.ai-pane-right\s*{[\s\S]*grid-row:\s*1;[\s\S]*\.ai-pane-left\s*{[\s\S]*grid-row:\s*2;/);
});

test('AI panel productization modules carry remaining workbench responsibilities', () => {
  const productRoot = path.resolve(getRepoRoot(), 'ClearVision.Product');
  const aiSourceDir = path.resolve(productRoot, 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'ai');
  const mainSource = fs.readFileSync(path.resolve(aiSourceDir, 'aiPanel.js'), 'utf8');
  const modules = [
    ['aiPanelGenerateRequest.js', 'aiPanelGenerateRequestMixin'],
    ['aiPanelRequirementBrief.js', 'aiPanelRequirementBriefMixin'],
    ['aiPanelAttachments.js', 'aiPanelAttachmentsMixin'],
    ['aiPanelSessionHistory.js', 'aiPanelSessionHistoryMixin'],
    ['aiPanelApplyPreview.js', 'aiPanelApplyPreviewMixin'],
    ['aiPanelTopologySummary.js', 'aiPanelTopologySummaryMixin']
  ];

  for (const [fileName, exportName] of modules) {
    const source = fs.readFileSync(path.resolve(aiSourceDir, fileName), 'utf8');
    assert.match(source, new RegExp(`export const ${exportName}`));
    assert.match(mainSource, new RegExp(exportName));
  }

  const runtimePreviewSource = fs.readFileSync(path.resolve(aiSourceDir, 'aiPanelRuntimePreview.js'), 'utf8');
  assert.match(runtimePreviewSource, /normalizeRuntimePreviewSummary/);
  assert.match(runtimePreviewSource, /adapterName/);
  assert.match(runtimePreviewSource, /previewMode/);
  assert.match(runtimePreviewSource, /previewReady/);
  assert.match(runtimePreviewSource, /fallback/);
});

test('executable business benchmark report exposes actual toolchain fields', () => {
  const reportPath = path.resolve(
    getRepoRoot(),
    'quality',
    'evals',
    'reports',
    'VisionAgent_business_benchmark_baseline.json'
  );
  const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));

  assert.equal(report.benchmarkId, 'vision_agent_executable_business_benchmark');
  assert.equal(report.mode, 'offline_metadata_only');
  assertWorkflowRunMetadata(report.workflowRun);
  assert.ok(report.summary.caseCount >= 55);
  assert.equal(report.summary.accepted, true);
  for (const item of report.cases) {
    assert.ok(Array.isArray(item.expectedBusinessActions), item.caseId);
    assert.ok(Array.isArray(item.expectedToolCalls), item.caseId);
    assert.ok(Array.isArray(item.actualToolCalls), item.caseId);
    assert.ok(Object.prototype.hasOwnProperty.call(item, 'actualValidationResult'), item.caseId);
    assert.ok(Object.prototype.hasOwnProperty.call(item, 'actualDryRunResult'), item.caseId);
    assert.ok(Object.prototype.hasOwnProperty.call(item, 'actualPrecheckResult'), item.caseId);
    assert.ok(Object.prototype.hasOwnProperty.call(item, 'actualRuntimePreviewResult'), item.caseId);
  }
  const expectedTools = report.cases.flatMap(item => item.expectedToolCalls);
  assert.equal(expectedTools.includes('list_camera_bindings'), false);
  assert.equal(expectedTools.includes('propose_flow_patch'), false);
  assert.equal(expectedTools.includes('propose_parameter_patch'), false);
  assert.equal(expectedTools.includes('runtime_preview_metadata'), false);
});

test('planner autonomy benchmark report exposes planner and permission negative fields', () => {
  const reportPath = path.resolve(
    getRepoRoot(),
    'quality',
    'evals',
    'reports',
    'planner_autonomy_benchmark.json'
  );
  const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));

  assert.equal(report.benchmarkId, 'vision_agent_planner_autonomy_benchmark');
  assert.equal(report.mode, 'offline_metadata_only');
  assertWorkflowRunMetadata(report.workflowRun);
  assert.equal(report.summary.plannerCaseCount, 15);
  assert.equal(report.summary.permissionNegativeCaseCount, 6);
  assert.equal(report.summary.accepted, true);

  for (const item of [...report.cases, ...report.permissionNegativeCases]) {
    assert.ok(Array.isArray(item.expectedBusinessActions), item.caseId);
    assert.ok(Array.isArray(item.allowedTools), item.caseId);
    assert.ok(Array.isArray(item.plannerMessages), item.caseId);
    assert.ok(Array.isArray(item.plannedToolCalls), item.caseId);
    assert.ok(Array.isArray(item.policyDecisions), item.caseId);
    assert.ok(Array.isArray(item.actualToolCalls), item.caseId);
    assert.ok(Object.prototype.hasOwnProperty.call(item, 'actualValidationResult'), item.caseId);
    assert.ok(Object.prototype.hasOwnProperty.call(item, 'actualDryRunResult'), item.caseId);
    assert.ok(Object.prototype.hasOwnProperty.call(item, 'actualPrecheckResult'), item.caseId);
    assert.ok(Object.prototype.hasOwnProperty.call(item, 'actualRuntimePreviewResult'), item.caseId);
    assert.ok(Object.prototype.hasOwnProperty.call(item, 'finalWorkflowDraftAllowed'), item.caseId);
    assert.equal(item.passed, true, item.caseId);
  }

  const permissionErrors = report.permissionNegativeCases
    .flatMap(item => item.policyDecisions)
    .filter(item => item.errorCode)
    .map(item => item.errorCode);
  assert.ok(permissionErrors.includes('runtime_preview_consent_required'));
  assert.ok(permissionErrors.includes('runtime_preview_permission_denied'));
  assert.ok(permissionErrors.includes('tool_permission_denied'));
  assert.ok(permissionErrors.includes('config_write_denied'));
  assert.ok(permissionErrors.includes('tool_not_whitelisted'));
  assert.ok(permissionErrors.includes('deployment_prepare_tool_denied'));
});

test('real LLM planner shadow eval report remains default-off and policy-only', () => {
  const reportPath = path.resolve(
    getRepoRoot(),
    'quality',
    'evals',
    'reports',
    'real_llm_planner_shadow_eval.json'
  );
  const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));

  assert.equal(report.evalId, 'vision_agent_real_llm_planner_shadow_eval');
  assert.equal(report.mode, 'offline_metadata_only');
  assert.equal(report.enabled, false);
  assert.equal(report.summary.runnerStatus, 'skipped');
  assert.equal(report.summary.caseCount, 12);
  assert.equal(report.summary.reportGenerated, true);
  assert.equal(report.summary.enabledReason, '');
  assert.match(report.summary.skippedReason, /CV_AGENT_REAL_LLM_SHADOW_EVAL/);
  assert.equal(report.summary.configurationMissingReason, '');
  assert.equal(report.summary.requestCount, 0);
  assert.equal(report.summary.parseSuccessRate, 0);
  assert.equal(report.summary.unsafeAttemptRate, 0);
  assert.equal(report.summary.averageToolPlanMatchScore, 0);
  assert.equal(report.summary.averageNextActionMatchScore, 0);
  assert.equal(report.summary.averageFullPlanMatchScore, 0);
  assert.equal(report.summary.averageOrderedPrefixScore, 0);
  assert.equal(report.summary.averagePolicySafetyScore, 1);
  assert.ok(Array.isArray(report.summary.badToolNames));
  assert.ok(Array.isArray(report.summary.missingRequiredLaterTools));
  assert.ok(Array.isArray(report.summary.overPlanningTools));
  assert.ok(Array.isArray(report.summary.underPlanningCases));
  assert.equal(report.llmConfiguration.provider, 'not_read_when_disabled');
  assert.equal(report.llmConfiguration.protocol, 'not_read_when_disabled');
  assert.equal(report.llmConfiguration.wireApi, 'not_read_when_disabled');
  assert.equal(report.llmConfiguration.authMode, 'not_read_when_disabled');
  assert.equal(report.llmConfiguration.modelRole, 'not_read_when_disabled');
  assertWorkflowRunMetadata(report.workflowRun);
  assert.equal(report.safety.workflowExecutionAttempted, false);
  assert.equal(report.safety.deploymentPrepareExecuted, false);
  assert.equal(report.safety.realCameraSdkTouched, false);
  assert.equal(report.safety.realStationTouched, false);
  assert.equal(report.safety.realImageFilesRead, false);
  assert.equal(report.safety.realModelFilesLoaded, false);
  assert.equal(report.safety.plcWriteAttempted, false);

  for (const item of report.cases) {
    assert.ok(Array.isArray(item.expectedToolCalls), item.caseId);
    assert.ok(Array.isArray(item.mockPlannerToolCalls), item.caseId);
    assert.ok(Array.isArray(item.plannedToolCalls), item.caseId);
    assert.ok(Array.isArray(item.policyDecision), item.caseId);
    assert.equal(typeof item.nextActionMatchScore, 'number', item.caseId);
    assert.equal(typeof item.fullPlanMatchScore, 'number', item.caseId);
    assert.equal(typeof item.orderedPrefixScore, 'number', item.caseId);
    assert.equal(typeof item.policySafetyScore, 'number', item.caseId);
    assert.ok(['next_action', 'full_plan', 'final', 'invalid'].includes(item.completionIntent), item.caseId);
    assert.ok(Array.isArray(item.missingRequiredLaterTools), item.caseId);
    assert.ok(Array.isArray(item.overPlanningTools), item.caseId);
    assert.equal(item.requestCount, 0, item.caseId);
    assert.equal(item.fallbackToMockSuggested, true, item.caseId);
  }
});

test('real LLM shadow eval report exposes split planner scoring fields', () => {
  const reportPath = path.resolve(
    getRepoRoot(),
    'quality',
    'evals',
    'reports',
    'real_llm_planner_shadow_eval.manual.json'
  );
  const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));

  assert.equal(typeof report.summary.averageNextActionMatchScore, 'number');
  assert.equal(typeof report.summary.averageFullPlanMatchScore, 'number');
  assert.equal(typeof report.summary.averageOrderedPrefixScore, 'number');
  assert.equal(typeof report.summary.averagePolicySafetyScore, 'number');
  assert.ok(Array.isArray(report.summary.badToolNames));
  assert.ok(Array.isArray(report.summary.missingRequiredLaterTools));
  assert.ok(Array.isArray(report.summary.overPlanningTools));
  assert.ok(Array.isArray(report.summary.underPlanningCases));

  for (const item of report.cases) {
    assert.equal(typeof item.nextActionMatchScore, 'number', item.caseId);
    assert.equal(typeof item.fullPlanMatchScore, 'number', item.caseId);
    assert.equal(typeof item.orderedPrefixScore, 'number', item.caseId);
    assert.equal(typeof item.policySafetyScore, 'number', item.caseId);
    assert.ok(['next_action', 'full_plan', 'final', 'invalid'].includes(item.completionIntent), item.caseId);
    assert.ok(Array.isArray(item.missingRequiredLaterTools), item.caseId);
    assert.ok(Array.isArray(item.overPlanningTools), item.caseId);
  }
});

test('holdout shadow eval report exposes robustness metrics and case set', () => {
  const reportPath = path.resolve(
    getRepoRoot(),
    'quality',
    'evals',
    'reports',
    'real_llm_planner_shadow_eval.holdout.json'
  );
  const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));

  assert.equal(report.caseSet, 'holdout');
  assert.equal(report.evalId, 'vision_agent_real_llm_planner_shadow_eval_holdout');
  assert.ok(report.summary.caseCount >= 20 && report.summary.caseCount <= 30);
  assert.equal(typeof report.summary.averageNextActionMatchScore, 'number');
  assert.equal(typeof report.summary.averageFullPlanMatchScore, 'number');
  assert.equal(typeof report.summary.averageOrderedPrefixScore, 'number');
  assert.equal(typeof report.summary.averagePolicySafetyScore, 'number');
  assert.equal(typeof report.summary.completionIntentDistribution, 'object');
  assert.ok(Array.isArray(report.summary.badToolNames));
  assert.ok(Array.isArray(report.summary.missingRequiredLaterTools));
  assert.ok(Array.isArray(report.summary.overPlanningTools));
  assert.ok(Array.isArray(report.summary.underPlanningCases));

  const categories = new Set(report.cases.map(item => item.category));
  assert.ok([...categories].some(item => item.includes('chinese')));
  assert.ok([...categories].some(item => item.includes('mixed')));
  assert.ok([...categories].some(item => item.includes('overreach') || item.includes('denied')));
  assert.ok(report.cases.some(item => item.runtimePreviewConsent === true || item.allowedTools.includes('capture_test_frame')));
});

test('shadow eval prompt defaults to complete ordered plan wording', () => {
  const repoRoot = getRepoRoot();
  const runner = fs.readFileSync(path.resolve(repoRoot, 'quality', 'tools', 'VisionAgentPlannerShadowEvalRunner', 'Program.cs'), 'utf8');
  const composer = fs.readFileSync(path.resolve(repoRoot, 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Agent', 'AgentPlannerPromptComposer.cs'), 'utf8');
  const builder = fs.readFileSync(path.resolve(repoRoot, 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Agent', 'AgentPlannerPromptBuilder.cs'), 'utf8');

  for (const source of [runner, composer, builder]) {
    assert.doesNotMatch(source, /Plan the next tool call/);
    assert.match(source, /Plan the complete ordered tool sequence or return final draft/);
  }
});

test('real LLM shadow eval report does not expose API keys and keeps BaseUrl redacted', () => {
  const reportPath = path.resolve(
    getRepoRoot(),
    'quality',
    'evals',
    'reports',
    'real_llm_planner_shadow_eval.json'
  );
  const reportText = fs.readFileSync(reportPath, 'utf8');
  const report = JSON.parse(reportText);

  assert.doesNotMatch(reportText, /apiKey|ApiKey|CV_AGENT_REAL_LLM_API_KEY|Bearer\s+|x-api-key|Authorization/);
  assert.ok(report.llmConfiguration);
  assert.equal(Object.prototype.hasOwnProperty.call(report.llmConfiguration, 'apiKey'), false);
  if (typeof report.llmConfiguration.baseUrl === 'string') {
    assert.doesNotMatch(report.llmConfiguration.baseUrl, /\?|@/);
  }
});

test('CI artifact assertion enforces non-local workflow metadata before upload', () => {
  const repoRoot = getRepoRoot();
  const scriptPath = path.resolve(repoRoot, 'quality', 'tools', 'assert_vision_agent_report_artifacts.py');
  const dedicatedWorkflow = fs.readFileSync(path.resolve(repoRoot, '.github', 'workflows', 'vision-agent-quality.yml'), 'utf8');
  const ciWorkflow = fs.readFileSync(path.resolve(repoRoot, '.github', 'workflows', 'ci.yml'), 'utf8');
  const script = fs.readFileSync(scriptPath, 'utf8');

  assert.match(script, /--require-non-local-workflow-run/);
  assert.match(script, /--scan-source-files/);
  assert.match(script, /CV_AGENT_FORBIDDEN_SECRET_FRAGMENTS/);
  assert.match(script, /unredacted CGNAT CPA base URL/);
  assert.match(script, /workflowRun\.commitSha.*must not be local|workflowRun\.\{field\} must not be local/s);
  for (const workflow of [dedicatedWorkflow, ciWorkflow]) {
    assert.match(workflow, /Assert Vision Agent Artifact Reports/);
    assert.match(workflow, /assert_vision_agent_report_artifacts\.py --require-non-local-workflow-run/);
    assert.match(workflow, /--scan-source-files/);
    assert.match(workflow, /vision_agent_quality_artifact_manifest\.json/);
    assert.match(workflow, /VisionAgent_business_benchmark_baseline\.json/);
    assert.match(workflow, /planner_autonomy_benchmark\.json/);
    assert.match(workflow, /real_llm_planner_shadow_eval\.json/);
    assert.match(workflow, /real_llm_planner_shadow_eval\.holdout\.json/);
    assert.match(workflow, /agent_ui_contract_output\.txt/);
    assert.match(workflow, /\*\*\/\*\.trx/);
  }
});

test('repository naming guard blocks legacy package names and NuGet package artifacts', () => {
  const repoRoot = getRepoRoot();
  const trackedFiles = getTrackedRepoFiles();
  const packageArtifacts = trackedFiles.filter(file => /\.(?:nupkg|snupkg)$/i.test(file));
  const packageOutputFiles = trackedFiles.filter(file => /(^|\/)nupkg\//i.test(file));
  const packagesDirectoryFiles = trackedFiles.filter(file => /(^|\/)packages\//i.test(file));
  const allowlistPath = path.resolve(repoRoot, 'quality', 'evals', 'allowlists', 'acme_naming_allowlist.json');
  const allowlist = JSON.parse(fs.readFileSync(allowlistPath, 'utf8'));
  const allowedFiles = new Set(allowlist.allowedFiles.map(entry => entry.path));
  const forbiddenFragments = [
    ['Ac', 'me.Product'].join(''),
    ['Ac', 'me.OperatorLibrary'].join(''),
    ['Ac', 'me.'].join('')
  ];
  const textExtensions = new Set([
    '.cs',
    '.csproj',
    '.sln',
    '.props',
    '.targets',
    '.ps1',
    '.cmd',
    '.bat',
    '.sh',
    '.py',
    '.js',
    '.mjs',
    '.json',
    '.md',
    '.txt',
    '.yml',
    '.yaml',
    '.xml',
    '.config'
  ]);
  const legacyHits = [];

  assert.deepEqual(packageArtifacts, []);
  assert.deepEqual(packageOutputFiles, []);
  assert.deepEqual(packagesDirectoryFiles, []);

  for (const file of trackedFiles) {
    if (allowedFiles.has(file)) {
      continue;
    }

    const extension = path.extname(file).toLowerCase();
    if (!textExtensions.has(extension)) {
      continue;
    }

    const absolutePath = path.resolve(repoRoot, file);
    if (!fs.existsSync(absolutePath)) {
      continue;
    }

    const text = fs.readFileSync(absolutePath, 'utf8');
    for (const fragment of forbiddenFragments) {
      if (text.includes(fragment)) {
        legacyHits.push(`${file}: ${fragment}`);
      }
    }
  }

  assert.deepEqual(legacyHits, []);
});

test('shadow eval source guard confines HttpClient to shadow runner only', () => {
  const repoRoot = getRepoRoot();
  const shadowRunner = fs.readFileSync(
    path.resolve(repoRoot, 'quality', 'tools', 'VisionAgentPlannerShadowEvalRunner', 'Program.cs'),
    'utf8'
  );
  assert.match(shadowRunner, /new HttpClient/);

  const guardedRoots = [
    path.resolve(repoRoot, 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Agent'),
    path.resolve(repoRoot, 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools'),
    path.resolve(repoRoot, 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'ai')
  ];
  for (const root of guardedRoots) {
    for (const sourcePath of fs.readdirSync(root, { recursive: true })
      .filter(fileName => String(fileName).endsWith('.cs') || String(fileName).endsWith('.js'))
      .map(fileName => path.resolve(root, fileName))) {
      const source = fs.readFileSync(sourcePath, 'utf8');
      assert.doesNotMatch(source, /new\s+HttpClient|HttpClient\s*\(/, sourcePath);
    }
  }
});

test('CPA shadow bridge reads only explicit CPA or Codex CPA provider config', () => {
  const repoRoot = getRepoRoot();
  const bridgePath = path.resolve(repoRoot, 'quality', 'tools', 'run_real_llm_shadow_eval_from_codex_config.ps1');
  const bridge = fs.readFileSync(bridgePath, 'utf8');

  assert.match(bridge, /CODEX_CONFIG_PATH/);
  assert.match(bridge, /CODEX_HOME/);
  assert.match(bridge, /\$HOME.*\.codex\/config\.toml/s);
  assert.match(bridge, /\[model_providers\\\./);
  assert.match(bridge, /function Test-IsCpaProvider/);
  assert.match(bridge, /Read-CpaProviderAliases/);
  assert.match(bridge, /CV_AGENT_CPA_PROVIDER_ALIASES/);
  assert.match(bridge, /cpa,ccswitch/);
  assert.match(bridge, /ProviderKey -match 'cpa'/);
  assert.match(bridge, /ProviderAliases -contains/);
  assert.match(bridge, /env_key/);
  assert.match(bridge, /CV_AGENT_CPA_MODEL/);
  assert.match(bridge, /CV_AGENT_CPA_BASE_URL/);
  assert.match(bridge, /CV_AGENT_CPA_API_KEY/);
  assert.match(bridge, /InspectConfigOnly/);
  assert.match(bridge, /shadowEvalWouldRun/);
  assert.doesNotMatch(bridge, /model_provider.*ccswitch/i);
});

test('CPA shadow bridge reports missing config without printing secrets', () => {
  const repoRoot = getRepoRoot();
  const bridge = fs.readFileSync(
    path.resolve(repoRoot, 'quality', 'tools', 'run_real_llm_shadow_eval_from_codex_config.ps1'),
    'utf8'
  );
  const manualReport = JSON.parse(fs.readFileSync(
    path.resolve(repoRoot, 'quality', 'evals', 'reports', 'real_llm_planner_shadow_eval.manual.json'),
    'utf8'
  ));
  const manualText = JSON.stringify(manualReport);

  assert.match(bridge, /CV_AGENT_REAL_LLM_CONFIGURATION_MISSING_REASON/);
  assert.match(bridge, /No CPA provider was found/);
  assert.match(bridge, /Secrets and full BaseUrl are not printed/);
  assert.match(bridge, /mode = "inspect_config_only"/);
  assert.match(bridge, /apiKeyConfigured/);
  assert.match(bridge, /Redact-BaseUrlForReport/);
  assert.ok(['configuration_missing', 'completed'].includes(manualReport.summary.runnerStatus));
  if (manualReport.summary.runnerStatus === 'configuration_missing') {
    assert.match(manualReport.summary.configurationMissingReason, /CPA model is missing|CPA API key is missing/);
    assert.equal(manualReport.summary.requestCount, 0);
  } else {
    assert.equal(manualReport.summary.configurationMissingReason, '');
    assert.ok(manualReport.summary.requestCount > 0);
  }
  assert.equal(manualReport.safety.workflowExecutionAttempted, false);
  assert.equal(manualReport.safety.deploymentPrepareExecuted, false);
  assert.doesNotMatch(manualText, /Bearer\s+|Authorization|x-api-key|sk-[A-Za-z0-9_-]{8,}/);
});

test('quality suite tracks raised UI contract minimum', () => {
  const suitePath = path.resolve(getRepoRoot(), 'quality', 'evals', 'suites', 'agent_engineering_harness_suite.json');
  const suite = JSON.parse(fs.readFileSync(suitePath, 'utf8'));
  const uiEntry = suite.stages
    .flatMap(stage => stage.entries)
    .find(entry => entry.id === 'vision_agent_ui_contract_tests');

  assert.ok(uiEntry);
  assert.equal(uiEntry.minimumTests, 190);

  const shadowEntry = suite.stages
    .flatMap(stage => stage.entries)
    .find(entry => entry.id === 'vision_agent_real_llm_planner_shadow_eval');
  assert.ok(shadowEntry);
  assert.equal(shadowEntry.status, 'manual');
});

test('RuntimePreview pilot gate document keeps real adapter gated and offline-fallback safe', () => {
  const gatePath = path.resolve(
    getRepoRoot(),
    'docs',
    '\u8fdb\u884c\u4e2d',
    '\u5f53\u524d\u8ba1\u5212',
    'VisionAgent_RuntimePreview_Pilot_Gate.md'
  );
  const doc = fs.readFileSync(gatePath, 'utf8');

  assert.match(doc, /fixed shadow/i);
  assert.match(doc, /holdout shadow/i);
  assert.match(doc, /permission negative/i);
  assert.match(doc, /model config regression/i);
  assert.match(doc, /default closed|\u9ed8\u8ba4\u5173\u95ed/);
  assert.match(doc, /resource allowlist/i);
  assert.match(doc, /\u4e0d\u5f97\u8fd4\u56de\u56fe\u7247 bytes\/base64|no image bytes\/base64/i);
  assert.match(doc, /\u4e0d\u5f97\u5199 PLC|no PLC write/i);
  assert.match(doc, /\u4e0d\u5f97\u6253\u5305\u3001\u4e0b\u53d1\u3001\u70ed\u52a0\u8f7d|no package, deploy, or hot-load/i);
  assert.match(doc, /fallback offline/i);
  assert.match(doc, /workflowDraftAllowed/i);
});

test('AI settings model config UI exposes productized model fields', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  for (const token of [
    'cfg-ai-display-name',
    'cfg-ai-protocol',
    'cfg-ai-authmode',
    'cfg-ai-priority',
    'cfg-ai-enabled',
    'cfg-ai-remark'
  ]) {
    assert.match(source, new RegExp(token));
  }
});

test('AI settings key input uses explicit keep replace clear semantics', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /cfg-ai-apikey-clear/);
  assert.match(source, /apiKeyOperation/);
  assert.match(source, /"clear"/);
  assert.match(source, /"replace"/);
  assert.match(source, /"keep"/);
  assert.match(source, /apiKeyOperation === "replace" \|\| apiKeyOperation === "new"/);
});

test('AI settings model roles include planner and shadow eval bindings', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /data-ai-role="generation"/);
  assert.match(source, /data-ai-role="planner"/);
  assert.match(source, /data-ai-role="vision-agent-shadow-eval"/);
  assert.match(source, /setDefaultPlannerAiModel/);
  assert.match(source, /setDefaultShadowEvalAiModel/);
});

test('AI settings Test Connection consumes structured sanitized result fields', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /connectionOk/);
  assert.match(source, /sanitizedMessage/);
  assert.match(source, /errorCode/);
  assert.match(source, /latencyMs/);
});

test('AI settings keeps shadow eval execution entry developer-hidden by default', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /hidden data-ai-shadow-eval-entry="hidden"/);
});

test('AI settings API wrapper exposes planner and shadow default endpoints', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /default-planner/);
  assert.match(source, /default-shadow-eval/);
});

test('AI settings API wrapper exposes RuntimePreview Pilot management endpoints', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /loadRuntimePreviewPilotConfig/);
  assert.match(source, /saveRuntimePreviewPilotConfig/);
  assert.match(source, /loadRuntimePreviewPilotCatalog/);
  assert.match(source, /checkRuntimePreviewPilotReadiness/);
  assert.match(source, /\/settings\/runtime-preview-pilot\/config/);
  assert.match(source, /\/settings\/runtime-preview-pilot\/catalog/);
  assert.match(source, /\/settings\/runtime-preview-pilot\/readiness/);
});

test('AI settings API wrapper exposes RuntimePreview Pilot session endpoints', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /listRuntimePreviewPilotSessions/);
  assert.match(source, /createRuntimePreviewPilotSession/);
  assert.match(source, /simulateRuntimePreviewPilotSession/);
  assert.match(source, /loadRuntimePreviewPilotSessionReport/);
  assert.match(source, /cancelRuntimePreviewPilotSession/);
  assert.match(source, /\/settings\/runtime-preview-pilot\/sessions\/simulate/);
});

test('AI settings API wrapper exposes RuntimePreview v1.1 governance endpoints', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /replayRuntimePreviewPilotSession/);
  assert.match(source, /exportRuntimePreviewPilotSessionReport/);
  assert.match(source, /generateRuntimePreviewDeployReadiness/);
  assert.match(source, /cleanupRuntimePreviewPilotRetention/);
  assert.match(source, /loadRuntimePreviewScenarioEvidence/);
  assert.match(source, /runtime-preview-pilot\/sessions\/deploy-readiness/);
  assert.match(source, /runtime-preview-pilot\/scenario-evidence/);
});

test('settings API wrapper exposes RuntimePreview v1.2 corpus package readiness and governance endpoints', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /generateRuntimePreviewPackageReadiness/);
  assert.match(source, /loadRuntimePreviewScenarioCorpus/);
  assert.match(source, /loadRuntimePreviewAgentExplanationBenchmark/);
  assert.match(source, /loadRuntimePreviewGovernanceIndex/);
  assert.match(source, /exportRuntimePreviewGovernance/);
  assert.match(source, /lookupRuntimePreviewGovernance/);
  assert.match(source, /runtime-preview-pilot\/sessions\/package-readiness/);
  assert.match(source, /runtime-preview-pilot\/scenario-corpus/);
  assert.match(source, /runtime-preview-pilot\/governance\/lookup/);
});

test('settings view exposes independent developer-only RuntimePreview Pilot Console tab', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsView.js'),
    'utf8'
  );

  assert.match(source, /installRuntimePreviewPilotConsole/);
  assert.match(source, /data-tab="runtime-preview-pilot"/);
  assert.match(source, /data-section="runtime-preview-pilot"/);
  assert.match(source, /isRuntimePreviewPilotDeveloperUiEnabled/);
  assert.match(source, /this\.isAdmin && this\.isRuntimePreviewPilotDeveloperUiEnabled/);
  assert.match(source, /renderRuntimePreviewPilotConsoleTab/);
  assert.match(source, /bindRuntimePreviewPilotConsoleEvents/);
});

test('AI settings tab no longer embeds RuntimePreview Pilot Console as model settings content', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );
  const renderAiTab = source.slice(source.indexOf('renderAiTab()'), source.indexOf('refreshAiTableAndForm'));

  assert.doesNotMatch(renderAiTab, /renderRuntimePreviewPilotPanel\(\)/);
  assert.match(source, /readRuntimePreviewPilotConfigDraft/);
});

test('independent RuntimePreview Pilot Console module renders v1.4 title and page marker', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /RuntimePreview Pilot Console v1\.4/);
  assert.match(source, /RuntimePreview Pre-release Review Desk/);
  assert.match(source, /data-runtime-preview-pilot-console-page="true"/);
  assert.match(source, /renderRuntimePreviewPilotConsoleV12Panels/);
  assert.match(source, /metadata-only scenario, manifest dry-run, package readiness/i);
});

test('independent RuntimePreview Pilot Console renders scenario corpus selector and run action', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /data-rp-scenario-corpus-panel="true"/);
  assert.match(source, /data-rp-scenario-corpus="true"/);
  assert.match(source, /cfg-rp-scenario-case-id/);
  assert.match(source, /btn-runtime-preview-pilot-load-corpus/);
  assert.match(source, /btn-runtime-preview-pilot-run-selected-scenario/);
  assert.match(source, /loadRuntimePreviewScenarioCorpus/);
});

test('independent RuntimePreview Pilot Console renders package readiness bridge output', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /data-rp-package-readiness-panel="true"/);
  assert.match(source, /data-rp-package-readiness-report="true"/);
  assert.match(source, /btn-runtime-preview-pilot-package-readiness/);
  assert.match(source, /generateRuntimePreviewPackageReadiness/);
  assert.match(source, /readyForPackage/);
  assert.match(source, /packageBlocked/);
  assert.match(source, /packageCreated/);
  assert.match(source, /deploymentExecuted/);
});

test('independent RuntimePreview Pilot Console renders governance index lookup export controls', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /data-rp-governance-panel="true"/);
  assert.match(source, /cfg-rp-lookup-session-id/);
  assert.match(source, /cfg-rp-lookup-report-id/);
  assert.match(source, /cfg-rp-lookup-case-id/);
  assert.match(source, /btn-runtime-preview-pilot-governance-index/);
  assert.match(source, /btn-runtime-preview-pilot-governance-lookup/);
  assert.match(source, /btn-runtime-preview-pilot-governance-export/);
  assert.match(source, /lookupRuntimePreviewGovernance/);
});

test('independent RuntimePreview Pilot Console renders Agent explanation benchmark', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /btn-runtime-preview-pilot-agent-explanation/);
  assert.match(source, /data-rp-agent-explanation="true"/);
  assert.match(source, /loadRuntimePreviewAgentExplanationBenchmark/);
  assert.match(source, /nextEngineerAction/);
});

test('AI settings RuntimePreview Pilot UI is developer-hidden and exposes config catalog readiness controls', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /data-runtime-preview-pilot-admin="hidden"/);
  assert.match(source, /isRuntimePreviewPilotDeveloperUiEnabled/);
  assert.match(source, /cv_ai_agent_dev_ui/);
  assert.match(source, /cfg-rp-enabled/);
  assert.match(source, /cfg-rp-allow-cameras/);
  assert.match(source, /cfg-rp-allow-models/);
  assert.match(source, /cfg-rp-allow-templates/);
  assert.match(source, /cfg-rp-allow-flows/);
  assert.match(source, /cfg-rp-allow-roots/);
  assert.match(source, /btn-runtime-preview-pilot-readiness/);
  assert.match(source, /data-rp-readiness-status/);
  assert.match(source, /blockingIssues/);
  assert.match(source, /missingResources/);
  assert.match(source, /pendingActions/);
  assert.match(source, /resourceTrace/);
  assert.match(source, /fallback/);
  assert.match(source, /allowlistCoverage/);
});

test('AI settings RuntimePreview Pilot Console exposes session controls', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /RuntimePreview Pilot Console v1\.1/);
  assert.match(source, /btn-runtime-preview-pilot-create-session/);
  assert.match(source, /btn-runtime-preview-pilot-simulate/);
  assert.match(source, /btn-runtime-preview-pilot-load-report/);
  assert.match(source, /btn-runtime-preview-pilot-cancel-session/);
  assert.match(source, /data-rp-session-console/);
});

test('AI settings RuntimePreview Pilot Console exposes v1.1 product console controls', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /btn-runtime-preview-pilot-apply-catalog-allowlist/);
  assert.match(source, /btn-runtime-preview-pilot-replay-session/);
  assert.match(source, /btn-runtime-preview-pilot-export-report/);
  assert.match(source, /btn-runtime-preview-pilot-deploy-readiness/);
  assert.match(source, /btn-runtime-preview-pilot-scenario-evidence/);
  assert.match(source, /btn-runtime-preview-pilot-cleanup/);
});

test('AI settings RuntimePreview Pilot Console supports catalog-driven allowlist diff and save confirmation', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /data-rp-catalog-allowlist="true"/);
  assert.match(source, /getRuntimePreviewPilotCatalogSelections/);
  assert.match(source, /applyRuntimePreviewPilotCatalogAllowlist/);
  assert.match(source, /buildRuntimePreviewPilotConfigDiff/);
  assert.match(source, /data-rp-allowlist-diff="true"/);
  assert.match(source, /window\.confirm/);
});

test('AI settings RuntimePreview Pilot Console renders session list fields', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /runtimePreviewPilotSessions/);
  assert.match(source, /data-rp-session-list/);
  assert.match(source, /session\.sessionId/);
  assert.match(source, /session\.readinessStatus/);
  assert.match(source, /session\.permissionStatus/);
  assert.match(source, /session\.reportId/);
});

test('AI settings RuntimePreview Pilot Console renders audit timeline and report preview', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /runtimePreviewPilotSessionReport/);
  assert.match(source, /data-rp-report-preview/);
  assert.match(source, /data-rp-audit-timeline/);
  assert.match(source, /simulatedTimeline/);
  assert.match(source, /auditTimeline/);
  assert.match(source, /resourceHandles/);
});

test('AI settings RuntimePreview Pilot Console renders replay and report export surfaces', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /runtimePreviewPilotReplay/);
  assert.match(source, /runtimePreviewPilotExport/);
  assert.match(source, /data-rp-session-replay/);
  assert.match(source, /data-rp-report-export-payload/);
  assert.match(source, /replayRuntimePreviewPilotSession/);
  assert.match(source, /exportRuntimePreviewPilotSessionReport/);
});

test('AI settings RuntimePreview Pilot Console renders deploy readiness report without deployment actions', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /runtimePreviewPilotDeployReadinessReport/);
  assert.match(source, /data-rp-deploy-readiness-report/);
  assert.match(source, /packageCreated/);
  assert.match(source, /deploymentExecuted/);
  assert.match(source, /realResourcesTouched/);
  assert.match(source, /generateRuntimePreviewDeployReadiness/);
  assert.doesNotMatch(source, /package_flow|deploy_flow|hot_load|write_plc/i);
});

test('AI settings RuntimePreview Pilot Console renders scenario evidence and retention cleanup surfaces', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /runtimePreviewPilotScenarioEvidence/);
  assert.match(source, /data-rp-scenario-evidence/);
  assert.match(source, /runtimePreviewPilotRetentionCleanup/);
  assert.match(source, /data-rp-retention-cleanup/);
  assert.match(source, /cleanupRuntimePreviewPilotRetention/);
  assert.match(source, /loadRuntimePreviewScenarioEvidence/);
});

test('AI settings RuntimePreview Pilot session payload remains metadata-only', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /buildRuntimePreviewPilotSessionPayload/);
  assert.match(source, /toolName:\s*'runtime_preview_metadata'/);
  assert.match(source, /runtimePreviewConsent:\s*true/);
  assert.doesNotMatch(source, /capture_test_frame|replay_flow_with_frame/);
});

test('RuntimePreview governance source defines session lifecycle and audit events', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );

  for (const token of [
    'RuntimePreviewSessionStatuses',
    'Created',
    'Configured',
    'ReadinessChecked',
    'Authorized',
    'Simulated',
    'Completed',
    'Denied',
    'Failed',
    'Cancelled',
    'RuntimePreviewAuditEventTypes',
    'ReportGenerated'
  ]) {
    assert.match(source, new RegExp(token));
  }
});

test('RuntimePreview governance source exposes brokers and metadata resource handles', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  assert.match(source, /RuntimePreviewPermissionBroker/);
  assert.match(source, /RuntimePreviewResourceBroker/);
  assert.match(source, /RuntimePreviewSimulatedExecutionHarness/);
  assert.match(source, /RuntimePreviewResourceHandle/);
  assert.match(source, /RealResourcesTouched = false/);
});

test('RuntimePreview governance source defines persistent store replay cleanup deploy readiness and scenario evidence', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  for (const token of [
    'RuntimePreviewGovernanceStore',
    'StorageMode => "jsonl"',
    'runtime_preview_sessions.jsonl',
    'RuntimePreviewGovernanceMaintenanceService',
    'RuntimePreviewDeployReadinessService',
    'RuntimePreviewScenarioEvidenceService',
    'SaveDeployReadinessReport',
    'Replay(string sessionId)',
    'ThrowIfUnsafeStorageText'
  ]) {
    assert.match(source, new RegExp(token.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }
});

test('RuntimePreview readiness endpoint uses PermissionBroker admin gate', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'Endpoints', 'SettingsEndpoints.cs'),
    'utf8'
  );

  assert.match(source, /runtime-preview-pilot\/readiness/);
  assert.match(source, /RuntimePreviewPermissionBroker/);
  assert.match(source, /EvaluateEndpointAccess/);
  assert.match(source, /runtime-preview-pilot-readiness/);
  assert.match(source, /Status403Forbidden/);
});

test('RuntimePreview v1.1 endpoints expose admin gated replay export deploy readiness scenario and cleanup paths', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'Endpoints', 'SettingsEndpoints.cs'),
    'utf8'
  );

  assert.match(source, /runtime-preview-pilot\/sessions\/\{sessionId\}\/replay/);
  assert.match(source, /runtime-preview-pilot\/sessions\/\{sessionId\}\/report\/export/);
  assert.match(source, /runtime-preview-pilot\/sessions\/deploy-readiness/);
  assert.match(source, /runtime-preview-pilot\/retention\/cleanup/);
  assert.match(source, /runtime-preview-pilot\/scenario-evidence/);
  assert.match(source, /RuntimePreviewDeployReadinessService/);
  assert.match(source, /RuntimePreviewScenarioEvidenceService/);
  assert.match(source, /EvaluateEndpointAccess/);
});

test('RuntimePreview v1.2 endpoints expose admin gated package readiness corpus governance and explanation paths', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'Endpoints', 'SettingsEndpoints.cs'),
    'utf8'
  );

  assert.match(source, /runtime-preview-pilot\/sessions\/package-readiness/);
  assert.match(source, /runtime-preview-pilot\/scenario-corpus/);
  assert.match(source, /runtime-preview-pilot\/agent-explanation-benchmark/);
  assert.match(source, /runtime-preview-pilot\/governance\/index/);
  assert.match(source, /runtime-preview-pilot\/governance\/export/);
  assert.match(source, /runtime-preview-pilot\/governance\/lookup/);
  assert.match(source, /RuntimePreviewPackageReadinessBridge/);
  assert.match(source, /RuntimePreviewScenarioCorpusService/);
  assert.match(source, /RuntimePreviewAgentExplanationService/);
});

test('settings API wrapper exposes RuntimePreview v1.3 redacted corpus manifest dry-run endpoints', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /generateRuntimePackageManifestDryRun/);
  assert.match(source, /loadRuntimePreviewRedactedFlowCorpus/);
  assert.match(source, /runtime-preview-pilot\/sessions\/manifest-dry-run/);
  assert.match(source, /runtime-preview-pilot\/redacted-flow-corpus/);
  assert.match(source, /manifestId = ''/);
  assert.match(source, /encodeURIComponent\(manifestId\)/);
});

test('independent RuntimePreview Pilot Console renders redacted flow corpus controls', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /data-rp-redacted-flow-corpus-panel="true"/);
  assert.match(source, /data-rp-redacted-flow-corpus="true"/);
  assert.match(source, /cfg-rp-redacted-flow-case-id/);
  assert.match(source, /btn-runtime-preview-pilot-load-redacted-flow-corpus/);
  assert.match(source, /btn-runtime-preview-pilot-run-redacted-flow-chain/);
  assert.match(source, /loadRuntimePreviewRedactedFlowCorpus/);
});

test('independent RuntimePreview Pilot Console renders manifest dry-run report surface', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /data-rp-manifest-dry-run-panel="true"/);
  assert.match(source, /data-rp-manifest-dry-run-report="true"/);
  assert.match(source, /btn-runtime-preview-pilot-manifest-dry-run/);
  assert.match(source, /generateRuntimePackageManifestDryRun/);
  assert.match(source, /manifestId/);
  assert.match(source, /manifestHash/);
  assert.match(source, /manifestArtifactGenerated/);
});

test('independent RuntimePreview Pilot Console surfaces Package Readiness Bridge v2 fields', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /packageReviewAllowed/);
  assert.match(source, /manifestDryRunReportId/);
  assert.match(source, /packageRiskLevel/);
  assert.match(source, /packageReviewExplanation/);
  assert.match(source, /dependencyTrace/);
});

test('independent RuntimePreview Pilot Console lookup supports manifestId', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /cfg-rp-lookup-manifest-id/);
  assert.match(source, /placeholder="manifestId"/);
  assert.match(source, /manifestId:\s*root\.querySelector\('#cfg-rp-lookup-manifest-id'\)\?\.value/);
});

test('independent RuntimePreview Pilot Console redacted flow chain builds selected case payload', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /ensureRuntimePreviewRedactedFlowCorpusLoaded/);
  assert.match(source, /findRuntimePreviewRedactedFlowCase/);
  assert.match(source, /flowCase\.workflowDraft/);
  assert.match(source, /const caseId = root\.querySelector\('#cfg-rp-redacted-flow-case-id'\)\?\.value/);
  assert.match(source, /runtimePreviewGovernanceLookup = \{ redactedFlowCase: flowCase, preReleaseReviewReport: this\.runtimePreviewPreReleaseReviewReport \}/);
});

test('RuntimePreview v1.3 endpoints expose admin gated redacted corpus manifest dry-run and manifest lookup', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'Endpoints', 'SettingsEndpoints.cs'),
    'utf8'
  );

  assert.match(source, /runtime-preview-pilot\/sessions\/manifest-dry-run/);
  assert.match(source, /runtime-preview-pilot\/redacted-flow-corpus/);
  assert.match(source, /manifestId/);
  assert.match(source, /RuntimePreviewPackageReadinessBridge/);
  assert.match(source, /RuntimePreviewRedactedFlowCorpusService/);
  assert.match(source, /EvaluateEndpointAccess/);
  assert.match(source, /manifestDryRunReport/);
});

test('settings API wrapper exposes RuntimePreview v1.4 pre-release review endpoint', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /generateRuntimePreviewPreReleaseReview/);
  assert.match(source, /runtime-preview-pilot\/sessions\/pre-release-review/);
});

test('settings API wrapper exposes RuntimePreview v1.4 station profiles endpoint', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /loadRuntimePreviewStationProfiles/);
  assert.match(source, /runtime-preview-pilot\/station-profiles/);
});

test('settings API wrapper exposes RuntimePreview v1.4 operator contract registry endpoint', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /loadRuntimePreviewOperatorContractRegistry/);
  assert.match(source, /runtime-preview-pilot\/operator-contract-registry/);
});

test('settings API wrapper sends RuntimePreview v1.4 review and station lookup keys', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'settingsApi.js'),
    'utf8'
  );

  assert.match(source, /reviewId = ''/);
  assert.match(source, /stationProfileId = ''/);
  assert.match(source, /encodeURIComponent\(reviewId\)/);
  assert.match(source, /encodeURIComponent\(stationProfileId\)/);
});

test('RuntimePreview Review Desk v2 renders station profile selector', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /cfg-rp-station-profile-id/);
  assert.match(source, /stationProfileOptions/);
});

test('RuntimePreview Review Desk v2 renders station profile load action', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /btn-runtime-preview-pilot-load-station-profiles/);
  assert.match(source, /loadRuntimePreviewStationProfiles/);
});

test('RuntimePreview Review Desk v2 renders operator contract load action', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /btn-runtime-preview-pilot-load-operator-contract-registry/);
  assert.match(source, /loadRuntimePreviewOperatorContractRegistry/);
});

test('RuntimePreview Review Desk v2 runs full pre-release review chain', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /Run full review chain/);
  assert.match(source, /generateRuntimePreviewPreReleaseReview/);
});

test('RuntimePreview Review Desk v2 renders pre-release report panel', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /data-rp-pre-release-review-panel="true"/);
  assert.match(source, /data-rp-pre-release-review-report="true"/);
});

test('RuntimePreview Review Desk v2 renders station compatibility panel', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /data-rp-station-compatibility-panel="true"/);
  assert.match(source, /data-rp-station-compatibility-report="true"/);
});

test('RuntimePreview Review Desk v2 renders operator contract validation panel', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /data-rp-operator-contract-validation-panel="true"/);
  assert.match(source, /data-rp-operator-contract-validation-report="true"/);
});

test('RuntimePreview Review Desk v2 displays release decision fields', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  for (const token of ['reviewId', 'releaseReviewAllowed', 'requiresEngineerApproval', 'riskLevel', 'engineerActions']) {
    assert.match(source, new RegExp(token));
  }
});

test('RuntimePreview Review Desk v2 displays station compatibility fields', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  for (const token of ['stationCompatible', 'runtimeVersionCompatible', 'operatorSupportCompatible', 'cameraSlotsCompatible']) {
    assert.match(source, new RegExp(token));
  }
});

test('RuntimePreview Review Desk v2 displays operator validation fields', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  for (const token of ['operatorContractsSatisfied', 'contractResults', 'blockedReasons', 'requiredEngineerApprovals']) {
    assert.match(source, new RegExp(token));
  }
});

test('RuntimePreview Review Desk v2 lookup includes reviewId input', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /cfg-rp-lookup-review-id/);
  assert.match(source, /placeholder="reviewId"/);
});

test('RuntimePreview Review Desk v2 lookup includes stationProfileId input', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.match(source, /cfg-rp-lookup-station-profile-id/);
  assert.match(source, /placeholder="stationProfileId"/);
});

test('RuntimePreview v1.4 endpoints expose admin gated station contract and review paths', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'Endpoints', 'SettingsEndpoints.cs'),
    'utf8'
  );

  assert.match(source, /runtime-preview-pilot\/sessions\/pre-release-review/);
  assert.match(source, /runtime-preview-pilot\/station-profiles/);
  assert.match(source, /runtime-preview-pilot\/operator-contract-registry/);
  assert.match(source, /RuntimePreviewPreReleaseReviewService/);
});

test('RuntimePreview v1.4 governance lookup accepts review and station keys', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'Endpoints', 'SettingsEndpoints.cs'),
    'utf8'
  );

  assert.match(source, /string\? reviewId/);
  assert.match(source, /string\? stationProfileId/);
  assert.match(source, /GetPreReleaseReviewReport/);
  assert.match(source, /GetStationCompatibilityReportsByStationProfileId/);
});

test('RuntimePreview v1.4 contracts expose station profile records', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );

  assert.match(source, /RuntimePreviewStationProfile/);
  assert.match(source, /supportedOperatorTypes/);
  assert.match(source, /plcWriteAllowed/);
  assert.match(source, /networkPolicy/);
});

test('RuntimePreview v1.4 contracts expose operator contract registry records', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );

  assert.match(source, /RuntimePreviewOperatorContractDefinition/);
  assert.match(source, /requiredInputs/);
  assert.match(source, /forbiddenParameters/);
  assert.match(source, /stationCompatibilityRequirements/);
});

test('RuntimePreview v1.4 contracts expose pre-release review report fields', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );

  for (const token of ['RuntimePreviewPreReleaseReviewReport', 'reviewId', 'stationProfileId', 'operatorContractVersion', 'releaseReviewAllowed']) {
    assert.match(source, new RegExp(token));
  }
});

test('RuntimePreview governance store v4 persists v1.4 report streams', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  assert.match(source, /jsonl\.v4/);
  assert.match(source, /runtime_preview_station_compatibility_reports\.jsonl/);
  assert.match(source, /runtime_preview_operator_contract_validation_reports\.jsonl/);
  assert.match(source, /runtime_preview_pre_release_review_reports\.jsonl/);
});

test('RuntimePreview v1.4 services register full review simulator dependencies', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'AiGenerationServiceExtensions.cs'),
    'utf8'
  );

  assert.match(source, /AddScoped<RuntimePreviewStationProfileCatalog>/);
  assert.match(source, /AddScoped<RuntimePreviewOperatorContractRegistry>/);
  assert.match(source, /AddScoped<RuntimePreviewStationCompatibilityDryRunService>/);
  assert.match(source, /AddScoped<RuntimePreviewPreReleaseReviewService>/);
});

test('RuntimePreview v1.4 explanation fields cover release station and contract reasoning', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );

  assert.match(source, /releaseDecisionExplanation/);
  assert.match(source, /stationCompatibilityExplanation/);
  assert.match(source, /operatorContractExplanation/);
  assert.match(source, /workflowDraftVsReleaseExplanation/);
});

test('RuntimePreview v1.4 redacted corpus exposes station and release expectations', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );

  assert.match(source, /expectedStationCompatibility/);
  assert.match(source, /expectedReleaseReviewDecision/);
  assert.match(source, /requiredEngineerApprovals/);
  assert.match(source, /operatorContractExpectations/);
});

test('RuntimePreview governance contracts expose v1.3 manifest dry-run and redacted corpus records', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );

  for (const token of [
    'RuntimePackageManifestDryRunReport',
    'RuntimePackageManifestDryRunRequest',
    'RuntimePreviewRedactedFlowCorpusCase',
    'RuntimePreviewRedactedFlowCorpusDocument',
    'manifestDryRunReports',
    'manifestDryRunReportCount'
  ]) {
    assert.match(source, new RegExp(token));
  }
});

test('RuntimePreview governance contracts expose v1.3 manifest safety flags', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );

  assert.match(source, /manifestArtifactGenerated/);
  assert.match(source, /PackageCreated/);
  assert.match(source, /DeploymentExecuted/);
  assert.match(source, /MetadataOnly/);
  assert.match(source, /RealResourcesTouched/);
  assert.match(source, /packageReviewAllowed/);
});

test('RuntimePreview governance store v4 persists manifest dry-run stream and export counts', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  assert.match(source, /jsonl\.v4/);
  assert.match(source, /runtime_package_manifest_dry_run_reports\.jsonl/);
  assert.match(source, /SaveManifestDryRunReport/);
  assert.match(source, /LoadManifestDryRunReports/);
  assert.match(source, /ManifestDryRunReportCount/);
  assert.match(source, /manifest_dry_run_report/);
});

test('RuntimePreview report archive can lookup manifest dry-run by manifest report and session ids', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  assert.match(source, /GetManifestDryRunReport\(string manifestId\)/);
  assert.match(source, /GetManifestDryRunReportByReportId/);
  assert.match(source, /GetManifestDryRunReportBySessionId/);
  assert.match(source, /ListManifestDryRunReports/);
});

test('RuntimePackage manifest dry-run service builds dependency operator and resource traces only from metadata', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  assert.match(source, /RuntimePackageManifestDryRunService/);
  assert.match(source, /GenerateFromPackageReadiness/);
  assert.match(source, /BuildMissingDependencies/);
  assert.match(source, /BuildManifestDependencyTrace/);
  assert.match(source, /OperatorTrace/);
  assert.match(source, /ResourceTrace/);
  assert.match(source, /ManifestDryRunGenerated/);
});

test('RuntimePreview redacted flow corpus service exposes at least thirty metadata-only production-like cases', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  assert.match(source, /RuntimePreviewRedactedFlowCorpusService/);
  assert.match(source, /RP-RF-001/);
  assert.match(source, /RP-RF-032/);
  assert.match(source, /redacted_metadata_only/);
  assert.match(source, /package_manifest_blocked/);
});

test('RuntimePreview Package Readiness Bridge v2 links manifest dry-run report id and review decision', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  assert.match(source, /ManifestDryRunReportId = manifestReport\.ManifestId/);
  assert.match(source, /PackageReviewAllowed = manifestReport\.PackageReviewAllowed/);
  assert.match(source, /PackageBlocked = !manifestReport\.PackageReviewAllowed/);
  assert.match(source, /PackageReviewExplanation/);
  assert.match(source, /ResourceContract/);
});

test('RuntimePreview v1.3 DI registers manifest dry-run and redacted corpus services', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'AiGenerationServiceExtensions.cs'),
    'utf8'
  );

  assert.match(source, /AddScoped<RuntimePackageManifestDryRunService>/);
  assert.match(source, /AddScoped<RuntimePreviewRedactedFlowCorpusService>/);
});

test('RuntimePreview Final quality runner writes release review final reports', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'quality', 'tools', 'run_runtime_preview_scenario_evidence.py'),
    'utf8'
  );

  assert.match(source, /RuntimePreview Release Review Final/);
  assert.match(source, /MIN_FINAL_CASES = 60/);
  assert.match(source, /runtime_preview_redacted_flow_corpus/);
  assert.match(source, /runtime_preview_redacted_flow_corpus_v2/);
  assert.match(source, /runtime_preview_redacted_flow_corpus_final/);
  assert.match(source, /runtime_package_manifest_dry_run\.sample/);
  assert.match(source, /runtime_package_manifest_dry_run_final/);
  assert.match(source, /runtime_preview_station_compatibility_dry_run\.sample/);
  assert.match(source, /runtime_preview_station_compatibility_final/);
  assert.match(source, /runtime_preview_operator_contract_validation_sample/);
  assert.match(source, /runtime_preview_operator_contract_validation_final/);
  assert.match(source, /runtime_preview_pre_release_review_report\.sample/);
  assert.match(source, /runtime_preview_pre_release_review_final/);
  assert.match(source, /runtime_preview_release_decision_matrix/);
  assert.match(source, /runtime_preview_agent_explanation_v3/);
  assert.match(source, /runtime_preview_agent_explanation_final/);
});

test('RuntimePreview Final artifact assertion scans release review reports and package fragments', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'quality', 'tools', 'assert_vision_agent_report_artifacts.py'),
    'utf8'
  );

  assert.match(source, /runtime-preview-final-pre-pilot-hardening-scan/);
  assert.match(source, /runtime_preview_redacted_flow_corpus\.json/);
  assert.match(source, /runtime_preview_redacted_flow_corpus_v2\.json/);
  assert.match(source, /runtime_preview_redacted_flow_corpus_final\.json/);
  assert.match(source, /runtime_package_manifest_dry_run\.sample\.json/);
  assert.match(source, /runtime_package_manifest_dry_run_final\.json/);
  assert.match(source, /runtime_preview_station_compatibility_dry_run\.sample\.json/);
  assert.match(source, /runtime_preview_station_compatibility_final\.json/);
  assert.match(source, /runtime_preview_operator_contract_validation_sample\.json/);
  assert.match(source, /runtime_preview_operator_contract_validation_final\.json/);
  assert.match(source, /runtime_preview_pre_release_review_report\.sample\.json/);
  assert.match(source, /runtime_preview_pre_release_review_final\.json/);
  assert.match(source, /runtime_preview_release_decision_matrix\.json/);
  assert.match(source, /\\.cvpkg\\b/);
});

const runtimePreviewFinalArtifacts = [
  'quality/evals/reports/runtime_preview_redacted_flow_corpus_final.json',
  'quality/evals/reports/runtime_preview_redacted_flow_corpus_final.md',
  'quality/evals/reports/runtime_preview_station_profiles_final.json',
  'quality/evals/reports/runtime_preview_station_profiles_final.md',
  'quality/evals/reports/runtime_preview_operator_contract_registry_final.json',
  'quality/evals/reports/runtime_preview_operator_contract_registry_final.md',
  'quality/evals/reports/runtime_preview_operator_contract_coverage.json',
  'quality/evals/reports/runtime_preview_operator_contract_coverage.md',
  'quality/evals/reports/runtime_preview_operator_contract_validation_final.json',
  'quality/evals/reports/runtime_preview_operator_contract_validation_final.md',
  'quality/evals/reports/runtime_preview_station_compatibility_final.json',
  'quality/evals/reports/runtime_preview_station_compatibility_final.md',
  'quality/evals/reports/runtime_package_manifest_dry_run_final.json',
  'quality/evals/reports/runtime_package_manifest_dry_run_final.md',
  'quality/evals/reports/runtime_preview_package_readiness_final.json',
  'quality/evals/reports/runtime_preview_package_readiness_final.md',
  'quality/evals/reports/runtime_preview_pre_release_review_final.json',
  'quality/evals/reports/runtime_preview_pre_release_review_final.md',
  'quality/evals/reports/runtime_preview_release_decision_matrix.json',
  'quality/evals/reports/runtime_preview_release_decision_matrix.md',
  'quality/evals/reports/runtime_preview_agent_explanation_final.json',
  'quality/evals/reports/runtime_preview_agent_explanation_final.md',
  'quality/evals/reports/runtime_preview_governance_export_final.json',
  'quality/evals/reports/runtime_preview_governance_export_final.md',
  'quality/evals/reports/runtime_preview_report_readability_gate.json',
  'quality/evals/reports/runtime_preview_report_readability_gate.md'
];

for (const artifactPath of runtimePreviewFinalArtifacts) {
  test(`RuntimePreview Final artifact manifest includes ${path.basename(artifactPath)}`, () => {
    const manifest = JSON.parse(fs.readFileSync(
      path.resolve(getRepoRoot(), 'quality', 'evals', 'reports', 'vision_agent_quality_artifact_manifest.json'),
      'utf8'
    ));
    const entry = manifest.files.find(file => file.path === artifactPath);

    assert.ok(entry, artifactPath);
    assert.ok(entry.sizeBytes > 0, artifactPath);
  });
}

const preReleaseFinalFields = [
  'reviewId',
  'caseId',
  'sessionId',
  'workflowDraftHash',
  'manifestId',
  'stationProfileId',
  'operatorContractVersion',
  'readinessStatus',
  'packageReviewAllowed',
  'stationCompatible',
  'operatorContractsSatisfied',
  'releaseReviewAllowed',
  'requiresEngineerApproval',
  'goNoGoDecision',
  'blockedReasons',
  'riskLevel',
  'engineerActions',
  'firstFixRecommendation',
  'workflowDraftAllowed',
  'decisionMatrix',
  'packageCreated',
  'deploymentExecuted',
  'realResourcesTouched'
];

for (const field of preReleaseFinalFields) {
  test(`PreRelease Review Final report carries ${field}`, () => {
    const report = JSON.parse(fs.readFileSync(
      path.resolve(getRepoRoot(), 'quality', 'evals', 'reports', 'runtime_preview_pre_release_review_final.json'),
      'utf8'
    ));
    const first = report.reports[0];

    assert.ok(Object.hasOwn(first, field), field);
    if (['packageCreated', 'deploymentExecuted', 'realResourcesTouched'].includes(field)) {
      assert.equal(first[field], false, field);
    } else if (Array.isArray(first[field])) {
      assert.ok(first[field].length >= 0, field);
    } else {
      assert.notEqual(first[field], '', field);
      assert.notEqual(first[field], null, field);
      assert.notEqual(first[field], undefined, field);
    }
  });
}

const releaseDecisionTypes = [
  'releaseAllowed',
  'requiresEngineerApproval',
  'blocked',
  'forbiddenIntentDenied',
  'metadataIncomplete',
  'stationIncompatible',
  'operatorContractFailed',
  'manifestRiskBlocked',
  'packageReviewBlocked'
];

for (const decisionType of releaseDecisionTypes) {
  test(`Release decision matrix carries ${decisionType}`, () => {
    const report = JSON.parse(fs.readFileSync(
      path.resolve(getRepoRoot(), 'quality', 'evals', 'reports', 'runtime_preview_release_decision_matrix.json'),
      'utf8'
    ));
    const first = report.reports[0];
    const decision = first[decisionType];

    assert.ok(report.summary.decisionTypes.includes(decisionType), decisionType);
    assert.ok(decision.reason, decisionType);
    assert.ok(decision.nextAction, decisionType);
    assert.equal(typeof decision.engineerApprovalRequired, 'boolean', decisionType);
    assert.equal(typeof decision.workflowDraftAllowed, 'boolean', decisionType);
    assert.equal(typeof decision.packageReviewAllowed, 'boolean', decisionType);
    assert.equal(typeof decision.releaseReviewAllowed, 'boolean', decisionType);
  });
}

test('Vision Agent quality workflow uploads v1.4 release review artifacts', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), '.github', 'workflows', 'vision-agent-quality.yml'),
    'utf8'
  );

  assert.match(source, /runtime_preview_redacted_flow_corpus\.json/);
  assert.match(source, /runtime_preview_redacted_flow_corpus\.md/);
  assert.match(source, /runtime_preview_redacted_flow_corpus_v2\.json/);
  assert.match(source, /runtime_package_manifest_dry_run\.sample\.json/);
  assert.match(source, /runtime_package_manifest_dry_run\.sample\.md/);
  assert.match(source, /runtime_preview_station_profiles_sample\.json/);
  assert.match(source, /runtime_preview_operator_contract_registry\.json/);
  assert.match(source, /runtime_preview_pre_release_review_report\.sample\.json/);
});

test('business benchmark includes v1.4 release review cases through registered tools', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'quality', 'tools', 'VisionAgentBusinessBenchmarkRunner', 'Program.cs'),
    'utf8'
  );

  assert.match(source, /VA-BM-046/);
  assert.match(source, /VA-BM-070/);
  assert.match(source, /redacted_flow_corpus/);
  assert.match(source, /pre_release_review/);
  assert.match(source, /operator_contract_validation/);
  assert.match(source, /manifest_dry_run/);
  assert.match(source, /packageReviewAllowed\.false/);
  assert.match(source, /RuntimePreviewSimulateMetadataSessionTool\.ToolName/);
});

test('Agent explanation v3 output includes status release station contract and manifest risk', () => {
  const contractSource = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );
  const serviceSource = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  assert.match(contractSource, /AffectedOperators/);
  assert.match(contractSource, /BlockedReasons/);
  assert.match(contractSource, /ManifestRisk/);
  assert.match(contractSource, /PackageReviewAllowed/);
  assert.match(contractSource, /ReleaseDecisionExplanation/);
  assert.match(contractSource, /StationCompatibilityExplanation/);
  assert.match(contractSource, /OperatorContractExplanation/);
  assert.match(serviceSource, /ExplainRedacted/);
  assert.match(serviceSource, /WorkflowDraftAllowed = true/);
});

test('RuntimePreview v1.3 source guard keeps manifest dry-run from creating package or deployment artifacts', () => {
  const sources = [
    fs.readFileSync(path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'), 'utf8'),
    fs.readFileSync(path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'), 'utf8')
  ].join('\n');

  assert.match(sources, /PackageCreated = false|packageCreated: manifestDryRun\.packageCreated/);
  assert.match(sources, /DeploymentExecuted = false|deploymentExecuted: manifestDryRun\.deploymentExecuted/);
  assert.match(sources, /RealResourcesTouched = false|realResourcesTouched/);
  assert.doesNotMatch(sources, /ZipArchive|CreatePackage|deployPackage|HotLoad|write_plc|StationPackage/i);
});

test('Vision Agent build canvas source guard only applies canonical Flow artifacts', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'ai', 'aiPanelAgentWorkspace.js'),
    'utf8'
  );

  assert.match(source, /buildResult\.flow \|\| buildResult\.Flow/);
  const getFlowStart = source.indexOf('\n    _getResultFlowForCanvas');
  const getFlowEnd = source.indexOf('\n    _normalizeWorkflowDraftForCanvas', getFlowStart);
  const getFlowSource = source.slice(getFlowStart, getFlowEnd);
  const compatibilityStart = source.indexOf('\n    _getBuildArtifactFlowCompatibilityState');
  const compatibilityEnd = source.indexOf('\n    _buildLegacyMissingCanonicalFlowResult', compatibilityStart);
  const compatibilitySource = source.slice(compatibilityStart, compatibilityEnd);
  assert.doesNotMatch(source, /_buildCanvasFlowFromOperatorPipeline/);
  assert.doesNotMatch(source, /_buildLinearCanvasConnections/);
  assert.doesNotMatch(source, /_mergeBuildFallbackWithCurrentCanvas/);
  assert.doesNotMatch(getFlowSource, /operatorPipeline|OperatorPipeline/);
  assert.doesNotMatch(getFlowSource, /buildResult\.(workflowDraft|WorkflowDraft)|obj\.(workflowDraft|WorkflowDraft)/);
  assert.doesNotMatch(compatibilitySource, /_normalizeWorkflowDraftForCanvas/);
  assert.doesNotMatch(source, /connections:\s*connections\.length\s*\?\s*connections\s*:/);
});

test('RuntimePreview governance contracts expose v1.2 corpus package readiness export and explanation records', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Core', 'AI', 'Tools', 'RuntimePreviewGovernanceContracts.cs'),
    'utf8'
  );

  for (const token of [
    'RuntimePreviewScenarioCorpusCase',
    'RuntimePreviewScenarioCorpusDocument',
    'RuntimePreviewPackageReadinessReport',
    'RuntimePreviewPackageReadinessRequest',
    'RuntimePreviewGovernanceStorageIndexSummary',
    'RuntimePreviewGovernanceExportManifest',
    'RuntimePreviewAgentExplanationBenchmarkDocument'
  ]) {
    assert.match(source, new RegExp(token));
  }
});

test('RuntimePreview simulated harness records audit and report archive events', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  for (const token of [
    'SessionCreated',
    'CatalogLoaded',
    'ReadinessChecked',
    'PermissionDenied',
    'SimulationStarted',
    'SimulationCompleted',
    'ReportGenerated',
    'RuntimePreviewReportArchive'
  ]) {
    assert.match(source, new RegExp(token));
  }
});

test('business benchmark includes RuntimePreview session simulation case', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'quality', 'tools', 'VisionAgentBusinessBenchmarkRunner', 'Program.cs'),
    'utf8'
  );

  assert.match(source, /VA-BM-037/);
  assert.match(source, /runtime_preview_session/);
  assert.match(source, /RuntimePreviewSimulateMetadataSessionTool\.ToolName/);
  assert.match(source, /create_runtime_preview_session/);
});

test('RuntimePreview scenario evidence source covers required metadata business scenarios', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'Tools', 'RuntimePreviewGovernanceServices.cs'),
    'utf8'
  );

  for (const scenario of [
    'wire_sequence',
    'template_matching',
    'hole_distance',
    'remote_control_detection',
    'missing_camera',
    'dangerous_path',
    'plc_station_deny',
    'precheck_blocked'
  ]) {
    assert.match(source, new RegExp(scenario));
  }
  assert.match(source, /RealResourcesTouched = false/);
  assert.match(source, /PackageCreated = false/);
  assert.match(source, /DeploymentExecuted = false/);
});

test('RuntimePreview v1.1 DI registers governance store and metadata-only readiness services', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Infrastructure', 'AI', 'AiGenerationServiceExtensions.cs'),
    'utf8'
  );

  assert.match(source, /AddSingleton<RuntimePreviewGovernanceStore>/);
  assert.match(source, /AddScoped<RuntimePreviewGovernanceMaintenanceService>/);
  assert.match(source, /AddScoped<RuntimePreviewDeployReadinessService>/);
  assert.match(source, /AddScoped<RuntimePreviewPackageReadinessBridge>/);
  assert.match(source, /AddScoped<RuntimePreviewScenarioCorpusService>/);
  assert.match(source, /AddScoped<RuntimePreviewScenarioEvidenceService>/);
  assert.match(source, /AddScoped<RuntimePreviewAgentExplanationService>/);
  assert.match(source, /AddScoped<RuntimePackagePrecheckTool>/);
  assert.doesNotMatch(source, /RealRuntimePreview|CameraSdk|StationPackage|HotLoad/i);
});

test('RuntimePreview v1.2 quality runner writes corpus package readiness governance export and explanation reports', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'quality', 'tools', 'run_runtime_preview_scenario_evidence.py'),
    'utf8'
  );

  assert.match(source, /runtime_preview_scenario_corpus/);
  assert.match(source, /runtime_preview_package_readiness_report\.sample/);
  assert.match(source, /runtime_preview_governance_export_sample/);
  assert.match(source, /runtime_preview_agent_explanation_benchmark/);
  assert.match(source, /minimum-cases/);
});

test('RuntimePreview v1.2 console source guard keeps real resources and shell tools out of developer UI', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'runtimePreviewPilotConsole.js'),
    'utf8'
  );

  assert.doesNotMatch(source, /capture_test_frame|replay_flow_with_frame|realRuntimePreview|CameraSdk|StationPackage|write_plc|hot_load/i);
  assert.doesNotMatch(source, /child_process|powershell|cmd\.exe|process\./i);
  assert.match(source, /sanitizeRuntimePreviewPilotValue/);
});

test('AI settings RuntimePreview Pilot UI redacts sensitive display values and keeps metadata-only safety flags', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.match(source, /sanitizeRuntimePreviewPilotValue/);
  assert.match(source, /<redacted>/);
  assert.match(source, /denyExternalPath:\s*true/);
  assert.match(source, /denyImageBytes:\s*true/);
  assert.match(source, /mode:\s*'metadata_only'/);
  assert.doesNotMatch(source, /console\.log/);
});

test('AI settings source does not log or render full API key values', () => {
  const source = fs.readFileSync(
    path.resolve(getRepoRoot(), 'ClearVision.Product', 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'settings', 'tabs', 'aiTab.js'),
    'utf8'
  );

  assert.doesNotMatch(source, /console\.(log|debug|info)\([^)]*apiKey/i);
  assert.match(source, /placeholder="\$\{apiKeyPlaceholderValue\}"/);
  assert.match(source, /value=""/);
});

test('source guard: Agent UI has no RuntimePreview hardware network or process tool entry', () => {
  const currentFile = fileURLToPath(import.meta.url);
  const testProjectRoot = path.resolve(path.dirname(currentFile), '..', '..');
  const productRoot = path.resolve(testProjectRoot, '..', '..');
  const aiSourceDir = path.resolve(productRoot, 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'ai');
  const guardedSource = [
    'aiPanel.js',
    'aiPanelGenerateRequest.js',
    'aiPanelLiveEvents.js',
    'aiPanelValidationPreview.js',
    'aiPanelRuntimePreview.js',
    'aiPanelToolTrace.js'
  ].map(file => fs.readFileSync(path.resolve(aiSourceDir, file), 'utf8')).join('\n');

  assert.doesNotMatch(guardedSource, /capture_test_frame|replay_flow_with_frame|runtime_package_precheck/i);
  assert.doesNotMatch(guardedSource, /AcquireSingleFrameAsync|EnumerateCamerasAsync|GetOrCreateByBindingAsync|fetch\(|XMLHttpRequest|child_process|process\.|powershell|cmd\.exe|execute_command/i);
});

function jsonResponse(payload, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 409 ? 'Conflict' : (status === 503 ? 'Service Unavailable' : 'OK'),
    headers: {
      get(name) {
        return String(name || '').toLowerCase() === 'content-type'
          ? 'application/json'
          : '';
      }
    },
    async json() {
      return payload;
    },
    async text() {
      return JSON.stringify(payload);
    }
  };
}

function testPlan({ planId = 'plan-a', planHash = 'sha256:plan-a' } = {}) {
  return {
    planId,
    id: planId,
    planHash,
    goal: '检测表面缺陷',
    buildPrompt: '从计划构建检测流程',
    originalDescription: '检测表面缺陷',
    rawPlanSnapshot: { planId, planHash, canBuild: true },
    questions: []
  };
}

test('AI session history auto restores stored active session exactly once', async () => {
  const { AiPanel } = await loadAiPanel();
  global.localStorage.setItem('cv_ai_session_id', 'session-active');
  const panel = createPanel(AiPanel);
  panel.container = createContainer({ '#ai-history-list': createFakeElement() });
  panel.sessionId = 'session-active';
  panel.initialAutoRestoreSessionId = 'session-active';
  const requested = [];
  panel._sendGetAiSession = sessionId => requested.push(sessionId);

  panel._handleListAiSessionsResult({
    success: true,
    sessions: [
      { sessionId: 'session-active', lastMessage: '上次会话', updatedAtUtc: '2026-01-01T00:00:00Z', turnCount: 1 }
    ]
  });
  panel._handleListAiSessionsResult({
    success: true,
    sessions: [
      { sessionId: 'session-active', lastMessage: '上次会话', updatedAtUtc: '2026-01-01T00:00:00Z', turnCount: 1 }
    ]
  });

  assert.deepEqual(requested, ['session-active']);
  assert.equal(panel.pendingSessionLoad.sessionId, 'session-active');
  assert.equal(panel.pendingSessionLoad.source, 'auto_restore');
});

test('AI session history clears invalid active session pointer', async () => {
  const { AiPanel } = await loadAiPanel();
  global.localStorage.setItem('cv_ai_session_id', 'missing-session');
  const panel = createPanel(AiPanel);
  panel.container = createContainer({ '#ai-history-list': createFakeElement() });
  panel.sessionId = 'missing-session';
  panel.initialAutoRestoreSessionId = 'missing-session';

  panel._handleListAiSessionsResult({
    success: true,
    sessions: [{ sessionId: 'other-session', lastMessage: '其他', updatedAtUtc: '2026-01-01T00:00:00Z', turnCount: 1 }]
  });

  assert.equal(global.localStorage.getItem('cv_ai_session_id'), null);
  assert.equal(panel.sessionId, null);
});

test('AI session restore ignores late auto restore after user switches session', async () => {
  const { AiPanel } = await loadAiPanel();
  global.localStorage.setItem('cv_ai_session_id', 'old-session');
  const panel = createPanel(AiPanel);
  panel.container = createContainer({ '#ai-history-list': createFakeElement() });
  panel.sessionId = 'current-session';
  panel.initialAutoRestoreSessionId = 'old-session';
  panel.currentResult = { aiExplanation: 'current' };
  panel._sendGetAiSession = () => {};
  panel._handleListAiSessionsResult({
    success: true,
    sessions: [{ sessionId: 'old-session', lastMessage: '旧', updatedAtUtc: '2026-01-01T00:00:00Z', turnCount: 1 }]
  });

  panel.sessionNavigationEpoch += 1;
  panel.pendingSessionLoad = { sessionId: 'new-session', source: 'history_switch', epoch: panel.sessionNavigationEpoch };
  panel._handleGetAiSessionResult({
    success: true,
    session: { sessionId: 'old-session', history: [], workspaceSnapshot: { revision: 3, lifecycleState: 'plan_ready' } }
  });

  assert.equal(panel.sessionId, 'current-session');
  assert.deepEqual(panel.currentResult, { aiExplanation: 'current' });
});

test('AI session restore keeps manual B pending when auto A failure arrives late', async () => {
  const { AiPanel } = await loadAiPanel();
  global.localStorage.setItem('cv_ai_session_id', 'session-a');
  const panel = createPanel(AiPanel);
  panel.container = createContainer({
    '#ai-history-list': createFakeElement(),
    '#ai-chat-container': createFakeElement()
  });
  panel._displayResult = result => {
    panel.restoredResult = result;
  };
  panel.sessionId = 'session-a';
  panel.initialAutoRestoreSessionId = 'session-a';
  panel._createSessionLoadRequestId = () => `req-${(panel._requestCounter = (panel._requestCounter || 0) + 1)}`;
  const requests = [];
  panel._sendGetAiSession = (sessionId, request) => requests.push({ sessionId, ...request });

  panel._handleListAiSessionsResult({
    success: true,
    sessions: [
      { sessionId: 'session-a', lastMessage: 'A', updatedAtUtc: '2026-01-01T00:00:00Z', turnCount: 1 },
      { sessionId: 'session-b', lastMessage: 'B', updatedAtUtc: '2026-01-02T00:00:00Z', turnCount: 1 }
    ]
  });
  const autoA = { ...panel.pendingSessionLoad };
  panel.sessionNavigationEpoch += 1;
  panel._requestSessionLoad('session-b', 'history_switch');
  const manualB = { ...panel.pendingSessionLoad };

  panel._handleGetAiSessionResult({
    success: false,
    sessionId: 'session-a',
    requestId: autoA.requestId,
    navigationEpoch: autoA.epoch,
    errorMessage: 'not found'
  });

  assert.equal(panel.pendingSessionLoad.sessionId, 'session-b');
  assert.equal(panel.pendingSessionLoad.requestId, manualB.requestId);
  assert.equal(global.localStorage.getItem('cv_ai_session_id'), 'session-a');

  panel._handleGetAiSessionResult({
    success: true,
    sessionId: 'session-b',
    requestId: manualB.requestId,
    navigationEpoch: manualB.epoch,
    session: {
      sessionId: 'session-b',
      history: [],
      workspaceSnapshot: {
        revision: 7,
        lifecycleState: 'plan_ready',
        buildRunId: '',
        submittedBuildFingerprint: ''
      }
    }
  });

  assert.deepEqual(requests.map(item => item.sessionId), ['session-a', 'session-b']);
  assert.equal(panel.pendingSessionLoad, null);
  assert.equal(panel.sessionId, 'session-b');
  assert.equal(global.localStorage.getItem('cv_ai_session_id'), 'session-b');
  assert.equal(panel.workspaceSnapshotRevision, 7);
});

test('AI session restore resets workspace fields and clears stale build readonly state', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel.workspaceSnapshotRevision = 12;
  panel.workspaceBuildRunId = 'ar_old';
  panel.workspaceSubmittedBuildFingerprint = 'sha256:old';
  panel.activeAgentRunId = 'ar_old';
  panel.pendingVisionPlan = testPlan({ planId: 'old-plan', planHash: 'sha256:old' });

  panel._restoreWorkspaceSnapshotFromSession({
    revision: 5,
    lifecycleState: 'plan_ready',
    pendingPlanSnapshot: { planId: 'plan-a', planHash: 'sha256:plan-a', goal: '检测表面缺陷', canBuild: true },
    planQuestionSelections: {},
    confirmedPlanAnswers: [],
    requirementMode: 'strict',
    planAcceptedRecommendedDefaults: false,
    planRunId: '',
    buildRunId: '',
    submittedBuildFingerprint: ''
  }, 'session-a');

  assert.equal(panel.workspaceSnapshotRevision, 5);
  assert.equal(panel.workspaceBuildRunId, '');
  assert.equal(panel.workspaceSubmittedBuildFingerprint, '');
  assert.equal(panel.activeAgentRunId, null);
  assert.equal(panel.agentWorkspaceMode, 'plan');
});

test('AI workspace conflict rebases same plan once without fetching full session', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel.pendingVisionPlan = testPlan();
  panel.sessionId = 'session-conflict';
  panel.workspaceSnapshotRevision = 4;
  panel.workspaceSnapshotDirty = true;
  panel.planQuestionSelections = { image_source: 'camera' };
  panel._sendGetAiSession = () => {
    throw new Error('should not fetch full session');
  };

  const calls = [];
  const originalFetch = global.fetch;
  global.fetch = async (url, options = {}) => {
    const body = JSON.parse(options.body);
    calls.push(body);
    if (calls.length === 1) {
      return jsonResponse({
        errorCode: 'workspace_revision_conflict',
        snapshot: {
          revision: 8,
          lifecycleState: 'plan_ready',
          buildRunId: '',
          submittedBuildFingerprint: ''
        },
        persistenceStatus: { primaryStoreSaved: true, recoveryBackupSaved: true }
      }, 409);
    }
    return jsonResponse({
      success: true,
      snapshot: {
        revision: 9,
        lifecycleState: 'plan_ready',
        buildRunId: '',
        submittedBuildFingerprint: ''
      },
      persistenceStatus: { primaryStoreSaved: true, recoveryBackupSaved: true }
    });
  };

  try {
    await panel._queueWorkspaceSnapshotFlush('test-conflict');
  } finally {
    global.fetch = originalFetch;
  }

  assert.equal(calls.length, 2);
  assert.equal(calls[0].expectedRevision, 4);
  assert.equal(calls[1].expectedRevision, 8);
  assert.equal(calls[0].clientMutationId, calls[1].clientMutationId);
  assert.equal(panel.workspaceSnapshotRevision, 9);
  assert.equal(panel.workspaceSnapshotDirty, false);
});

test('AI workspace consecutive mutations keep dirty until latest generation persists', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel.pendingVisionPlan = testPlan();
  panel.sessionId = 'session-generations';
  panel.workspaceSnapshotRevision = 1;
  panel.planQuestionSelections = { image_source: 'camera' };

  const pending = [];
  const originalFetch = global.fetch;
  global.fetch = async (url, options = {}) => {
    const body = JSON.parse(options.body);
    return new Promise(resolve => {
      pending.push({
        body,
        resolve(snapshot) {
          resolve(jsonResponse({
            success: true,
            snapshot,
            persistenceStatus: { primaryStoreSaved: true, recoveryBackupSaved: true }
          }));
        }
      });
    });
  };

  try {
    const firstFlush = panel._queueWorkspaceSnapshotFlush('first');
    panel.planQuestionSelections = { image_source: 'folder' };
    const secondFlush = panel._queueWorkspaceSnapshotFlush('second');
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(panel.workspaceMutationGeneration, 2);
    assert.equal(panel.workspaceSnapshotDirty, true);
    assert.equal(pending.length, 1);

    pending[0].resolve({ revision: 2, lifecycleState: 'plan_ready' });
    await firstFlush;
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(panel.workspacePersistedGeneration, 1);
    assert.equal(panel.workspaceSnapshotDirty, true);
    assert.equal(pending.length, 2);

    pending[1].resolve({ revision: 3, lifecycleState: 'plan_ready' });
    await secondFlush;
  } finally {
    global.fetch = originalFetch;
  }

  assert.equal(panel.workspacePersistedGeneration, 2);
  assert.equal(panel.workspaceSnapshotRevision, 3);
  assert.equal(panel.workspaceSnapshotDirty, false);
});

test('AI workspace stale save response cannot update a new session', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel.pendingVisionPlan = testPlan();
  panel.sessionId = 'session-old';
  panel.workspaceSnapshotRevision = 1;
  panel.planQuestionSelections = { image_source: 'camera' };
  let releaseFetch;
  const originalFetch = global.fetch;
  global.fetch = async () => new Promise(resolve => {
    releaseFetch = () => resolve(jsonResponse({
      success: true,
      snapshot: { revision: 9, lifecycleState: 'building', buildRunId: 'ar_old' },
      persistenceStatus: { primaryStoreSaved: true, recoveryBackupSaved: true }
    }));
  });

  try {
    const flush = panel._queueWorkspaceSnapshotFlush('old-session');
    await new Promise(resolve => setTimeout(resolve, 0));
    panel.sessionId = 'session-new';
    panel.sessionNavigationEpoch += 1;
    panel.workspaceSnapshotRevision = 0;
    panel.workspaceBuildRunId = '';
    releaseFetch();
    await flush;
  } finally {
    global.fetch = originalFetch;
  }

  assert.equal(panel.sessionId, 'session-new');
  assert.equal(panel.workspaceSnapshotRevision, 0);
  assert.equal(panel.workspaceBuildRunId, '');
});

test('AI workspace boundary waits for target generation before continuing', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel.pendingVisionPlan = testPlan();
  panel.sessionId = 'session-boundary';
  panel.workspaceSnapshotRevision = 1;
  panel.planQuestionSelections = { image_source: 'camera' };
  let releaseFetch;
  const originalFetch = global.fetch;
  global.fetch = async () => new Promise(resolve => {
    releaseFetch = () => resolve(jsonResponse({
      success: true,
      snapshot: { revision: 2, lifecycleState: 'plan_ready' },
      persistenceStatus: { primaryStoreSaved: true, recoveryBackupSaved: true }
    }));
  });

  try {
    panel._queueWorkspaceSnapshotFlush('edit');
    await new Promise(resolve => setTimeout(resolve, 0));
    const boundary = panel._flushWorkspaceSnapshotBeforeBoundary('build');
    assert.equal(panel.workspaceBoundaryInProgress, true);
    let completed = false;
    boundary.then(() => { completed = true; });
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(completed, false);
    releaseFetch();
    assert.equal(await boundary, true);
  } finally {
    global.fetch = originalFetch;
  }

  assert.equal(panel.workspaceBoundaryInProgress, false);
  assert.equal(panel.workspacePersistedGeneration, panel.workspaceMutationGeneration);
  assert.equal(panel.workspaceSnapshotDirty, false);
});

test('AI AgentRun Build success applies canonical session and snapshot before SSE', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel.pendingVisionPlan = testPlan();
  const started = [];
  panel._startAgentRunEventSource = (runId, options) => started.push({ runId, options });
  panel._handleAgentRunEvent = evt => {
    panel.activeAgentRunEvents.push(evt);
  };
  const originalFetch = global.fetch;
  global.fetch = async () => jsonResponse({
    runId: 'ar_build',
    sessionId: 'session-canonical',
    brief: 'brief',
    events: [{ runId: 'ar_build', sequence: 1, eventType: 'run.started', stage: 'run', status: 'running' }],
    workspaceSnapshot: {
      revision: 6,
      lifecycleState: 'building',
      buildRunId: 'ar_build',
      submittedBuildFingerprint: 'sha256:submitted'
    },
    persistenceStatus: { primaryStoreSaved: true, recoveryBackupSaved: true }
  });

  try {
    await panel._dispatchAgentRunGenerateRequest({
      description: 'build',
      sessionId: 'session-old',
      buildFromPlan: { planId: 'plan-a' }
    }, { clearInput: false });
  } finally {
    global.fetch = originalFetch;
  }

  assert.equal(panel.sessionId, 'session-canonical');
  assert.equal(global.localStorage.getItem('cv_ai_session_id'), 'session-canonical');
  assert.equal(panel.workspaceSnapshotRevision, 6);
  assert.equal(panel.workspaceBuildRunId, 'ar_build');
  assert.equal(panel.workspaceSubmittedBuildFingerprint, 'sha256:submitted');
  assert.equal(panel.agentWorkspaceMode, 'build');
  assert.deepEqual(started.map(item => item.runId), ['ar_build']);
});

test('AI Build 503 keeps Plan mode and does not start SSE', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel.useVisionAgentGenerateFlow = true;
  panel.pendingVisionPlan = testPlan();
  panel.agentWorkspaceMode = 'plan';
  panel._startAgentRunEventSource = () => {
    throw new Error('SSE should not start');
  };
  panel._startAssistantTurn = () => {};
  panel._createGenerateRequestId = () => 'request-build-503';
  panel.container = createContainer({ '#ai-input': { value: '', style: {} } });
  const originalFetch = global.fetch;
  global.fetch = async () => jsonResponse({
    errorCode: 'session_persistence_failed',
    publicMessage: 'Build 创建失败：会话状态未能保存，后台构建未启动。',
    runId: 'ar_failed',
    events: [{ runId: 'ar_failed', sequence: 3, eventType: 'run.failed' }],
    persistenceStatus: { primaryStoreSaved: false, recoveryBackupSaved: true }
  }, 503);

  try {
    const dispatched = panel._dispatchGenerateRequest({
      description: 'build',
      buildFromPlan: { planId: 'plan-a' },
      skipPlan: true,
      clearInput: false
    });
    assert.equal(dispatched, true);
    await new Promise(resolve => setTimeout(resolve, 0));
  } finally {
    global.fetch = originalFetch;
  }

  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(panel.activeAgentRunId, null);
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
});

test('AI result persistence warning exposes Chinese status note', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const warning = panel._getPersistenceWarning({
    persistenceWarning: {
      code: 'primary_store_save_failed',
      message: '结果已生成，但本次会话尚未成功保存。'
    }
  });

  assert.equal(warning.code, 'primary_store_save_failed');
  panel._setResultStatusNote(warning.message, 'warning');
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
  assert.match(panel.lastResultStatusNote.text, /尚未成功保存/);
});

test('AI Build 409 applies latest workspace revision and stays in Plan without SSE', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel.useVisionAgentGenerateFlow = true;
  panel.pendingVisionPlan = testPlan();
  panel.agentWorkspaceMode = 'plan';
  panel.workspaceSnapshotRevision = 4;
  panel.container = createContainer({ '#ai-input': { value: 'keep this prompt', style: {} } });
  panel._startAssistantTurn = () => {};
  panel._createGenerateRequestId = () => 'request-build-409';
  panel._startAgentRunEventSource = () => {
    throw new Error('SSE should not start');
  };
  const originalFetch = global.fetch;
  global.fetch = async () => jsonResponse({
    errorCode: 'workspace_revision_conflict',
    publicMessage: 'Plan 状态已变化，请确认后重新构建',
    runId: 'ar_conflict',
    workspaceSnapshot: {
      revision: 11,
      lifecycleState: 'plan_ready',
      buildRunId: '',
      submittedBuildFingerprint: ''
    },
    persistenceStatus: { primaryStoreSaved: true, recoveryBackupSaved: true }
  }, 409);

  try {
    const dispatched = panel._dispatchGenerateRequest({
      description: 'build',
      buildFromPlan: { planId: 'plan-a' },
      skipPlan: true,
      clearInput: false
    });
    assert.equal(dispatched, true);
    await new Promise(resolve => setTimeout(resolve, 0));
  } finally {
    global.fetch = originalFetch;
  }

  assert.equal(panel.workspaceSnapshotRevision, 11);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.equal(panel.activeAgentRunId, null);
  assert.equal(panel.container.querySelector('#ai-input').value, 'keep this prompt');
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
  assert.equal(panel.lastResultStatusNote.text, 'Plan 状态已变化，请确认后重新构建');
});
