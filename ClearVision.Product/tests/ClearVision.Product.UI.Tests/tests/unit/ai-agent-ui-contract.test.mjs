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
  panel.pendingParameterDrafts = {};
  panel.pendingResourceDrafts = {};
  panel.pendingOperatorBindings = {};
  panel.operatorMetadataCache = new Map();
  panel.operatorMetadataLoading = new Map();
  panel.cameraBindingsCache = [];
  panel.cameraBindingsLoadingPromise = null;
  panel.currentResultVersion = 1;
  panel.sessionId = 'agent-ui-contract';
  panel.currentResult = overrides.currentResult || null;
  panel.isGenerating = false;
  panel.flowCanvas = null;
  panel.requirementMode = 'strict';
  panel.nextHintDraft = '';
  panel.nextTemplateSelection = null;
  panel.activeGenerateRequestId = null;
  panel.activePlanRequestId = null;
  panel.activeGenerateSessionId = null;
  panel.activeAgentRunId = null;
  panel.activeAgentRunEventSource = null;
  panel.activeAgentRunTransport = null;
  panel.activeAgentRunEvents = [];
  panel.activeAgentRunEventKeys = new Set();
  panel.agentRunStepMap = new Map();
  panel.agentRunToolMap = new Map();
  panel.agentRunArtifactMap = new Map();
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
  panel._setWorkbenchState = state => {
    panel.lastWorkbenchState = state;
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
  return {
    planId: overrides.planId || 'plan_backend_1',
    planHash: overrides.planHash || 'sha256:backend-plan-hash',
    originalUserPrompt: overrides.originalUserPrompt || overrides.goal || 'detect metal scratches',
    goal: overrides.goal || 'detect metal scratches',
    intent: overrides.intent || 'surface_defect',
    confidence: overrides.confidence || 'high',
    planSource: overrides.planSource || 'rule_fallback',
    fallbackReason: overrides.fallbackReason || 'planner_failed',
    requirementUnderstanding: ['Inspection intent: surface defect inspection.'],
    recommendedRoute: {
      routeId: 'surface_defect_detection',
      title: 'Surface defect inspection route',
      summary: 'Detect visible scratches and blobs.',
      operators: ['ImageAcquisition', 'SurfaceDefectDetection', 'ResultOutput'],
      templateDecision: 'Use selected template first.'
    },
    clarificationQuestions: [
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
    canBuild: true,
    blockingReasons: [],
    nextAction: 'Accept recommended defaults, then start Build.',
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
  const sourceType = overrides.sourceType || 'File';
  const buildResult = {
    buildId: 'build-contract-1',
    planId: 'plan-contract-1',
    planHash: 'sha256:contract',
    buildIntent: overrides.buildIntent || 'modify',
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
    BuildResult: {
      BuildId: buildResult.buildId,
      PlanId: buildResult.planId,
      PlanHash: buildResult.planHash,
      BuildIntent: buildResult.buildIntent,
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
  return {
    deserialize(nextFlow) {
      flow = cloneJson(nextFlow);
      revision += 1;
    },
    serialize() {
      return cloneJson(flow);
    },
    getFlowRevision() {
      return revision;
    }
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
  assert.equal(payload.runtimePreviewConsent, false);
  assert.equal(payload.attachmentCount, 1);
  assert.deepEqual(payload.attachments, []);
  assert.equal(payload.existingFlowJson, '{"operators":[]}');
  assert.deepEqual(payload.templateSelection, { mode: 'template_fill', templateId: 'tmpl-1' });
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
  assert.match(plan.innerHTML, /阈值需要结合样品确认/);
  assert.match(plan.innerHTML, /按推荐方案开始构建/);
  assert.doesNotMatch(plan.innerHTML, /Clarifying Questions|Accept Recommended and Build|Plan Mode/);
  assert.doesNotMatch([
    overview.innerHTML,
    plan.innerHTML
  ].join('\n'), /Accept recommended defaults|rule_fallback|\bplanner_failed\b|collecting_context completed|What should count as a defect|Defect definition controls|Scratch\/blob|Use general surface defect candidates|Good first draft|>Crack<|Dent\/stain|Thresholds need sample confirmation/);
  assert.doesNotMatch(plan.innerHTML, /setTimeout/);
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
    templateSelection: { mode: 'template_adapt', templateId: 'tmpl-plan', scenarioKey: 'scratch' }
  }));
  panel.planQuestionSelections = Object.fromEntries(
    panel.pendingVisionPlan.questions.map(question => [question.id, question.defaultValue])
  );
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  const started = panel._startBuildFromCurrentPlan({ acceptedRecommended: true });

  assert.equal(started, true);
  assert.equal(panel.agentWorkspaceMode, 'build');
  assert.equal(captured.skipPlan, true);
  assert.equal(captured.explicitMode, 'new');
  assert.equal(captured.hint, '');
  assert.match(captured.userMessage, /从计划开始构建/);
  assert.equal(captured.buildFromPlan.planHash, 'sha256:plan-build-1');
  assert.deepEqual(captured.buildFromPlan.templateSelection, {
    mode: 'template_adapt',
    templateId: 'tmpl-plan',
    scenarioKey: 'scratch'
  });
  assert.deepEqual(captured.templateSelection, captured.buildFromPlan.templateSelection);
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

test('BuildFromPlan prefers Plan templateSelection over raw snapshot and queued selection', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.nextTemplateSelection = { mode: 'template_fill', templateId: 'tmpl-next', scenarioKey: 'queued' };
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
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /title="op_detect \/ SurfaceDefectDetection"/);
  assert.doesNotMatch(elements['#ai-build-operator-chain'].innerHTML, />SurfaceDefectDetection</);
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /模板骨架/);
  assert.match(elements['#ai-build-operator-chain'].innerHTML, /非法算子已修复/);
  assert.match(elements['#ai-build-parameters'].innerHTML, /模型资源/);
  assert.match(elements['#ai-build-parameters'].innerHTML, /缺失资源 \/ 待确认/);
  assert.match(elements['#ai-build-checks'].innerHTML, /画布：可应用/);
  assert.match(elements['#ai-build-checks'].innerHTML, /运行草稿：就绪/);
  assert.match(elements['#ai-build-checks'].innerHTML, /部署：阻断/);
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
  assert.match(panel.lastResultStatusNote.text, /部署前待绑定或确认/);
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
    summary: 'I will build a safe workflow draft.',
    status: 'completed',
    metadataOnly: true,
    redactionPass: true
  });

  assert.match(turn.replyBody.textContent, /safe workflow draft/);
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
  assert.match(validation.innerHTML, /运行预演回放工具 已通过 7ms/);
  assert.doesNotMatch(validation.innerHTML, /validate_flow:Simulation/);
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

  assert.match(followups.innerHTML, /缺失资源/);
  for (const label of [
    '绑定模型资源',
    '选择模板文件',
    '填写标定\/像素比例',
    '选择相机绑定',
    '设置输出通道',
    '记录 PLC 元数据',
    '稍后处理'
  ]) {
    assert.match(followups.innerHTML, new RegExp(label));
  }
  assert.match(followups.innerHTML, /仅记录 metadata，不触发真实 PLC 写入/);
  assert.doesNotMatch(followups.innerHTML, /rawPrompt|systemPrompt|chainOfThought|data:image\/png;base64|sk-secret|192\.168\./i);
});

test('resource binding action writes metadata and updates pending, missing, and apply gate state', async () => {
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
  assert.equal(response.flow.operators.find(op => op.tempId === 'op_detect').parameters.ModelPath, 'model-resource:scratch-v1');
  assert.equal(panel.pendingResourceDrafts['op_detect.ModelPath'].metadataOnly, true);
  assert.match(panel.lastResultStatusNote.text, /仍有 9 项部署前待补/);
  assertNoSensitiveLeak([
    panel.lastResultStatusNote.text,
    ...(panel.messages || []).map(item => item.text || '')
  ].join('\n'));
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
  assert.equal(response.applyGate.status, 'deployment_metadata_ready');
  assert.equal(response.validationPreview.deploymentPrecheck.readyForDeployment, true);
  assert.equal(response.validationPreview.deploymentPrecheck.deploymentBlocked, false);
  assert.equal(response.firstFixRecommendation, '');
  const output = response.flow.operators.find(op => op.tempId === 'op_output');
  assert.equal(Object.prototype.hasOwnProperty.call(output.parameters, 'PlcAddress'), false);
  assert.equal(Object.prototype.hasOwnProperty.call(output.parameters, 'OutputChannel'), false);
  assertNoSensitiveLeak(JSON.stringify(response));
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

    const text = fs.readFileSync(path.resolve(repoRoot, file), 'utf8');
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
    'aiPanelValidationPreview.js',
    'aiPanelRuntimePreview.js',
    'aiPanelToolTrace.js'
  ].map(file => fs.readFileSync(path.resolve(aiSourceDir, file), 'utf8')).join('\n');

  assert.doesNotMatch(guardedSource, /capture_test_frame|replay_flow_with_frame|runtime_package_precheck/i);
  assert.doesNotMatch(guardedSource, /AcquireSingleFrameAsync|EnumerateCamerasAsync|GetOrCreateByBindingAsync|fetch\(|XMLHttpRequest|child_process|process\.|powershell|cmd\.exe|execute_command/i);
});
