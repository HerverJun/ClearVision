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

function createFakeElement(tagName = 'div') {
  let text = '';
  let html = '';
  let className = '';
  let id = '';
  const children = [];
  const listeners = new Map();
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
  const hasId = (element, selector) => String(element.id || '') === String(selector || '').replace(/^#/, '');
  const findById = (element, selector) => {
    for (const child of element.children || []) {
      if (hasId(child, selector)) {
        return child;
      }
      const nested = findById(child, selector);
      if (nested) {
        return nested;
      }
    }
    return null;
  };
  const matchesSelector = (element, selector) => {
    const normalized = String(selector || '').trim();
    if (!normalized) return false;
    if (normalized.startsWith('#')) return hasId(element, normalized);
    if (normalized.startsWith('.')) return hasClass(element, normalized);
    if (/^\[[^\]]+\]$/.test(normalized)) {
      const attr = normalized.slice(1, -1).split('=')[0].trim();
      return element.getAttribute?.(attr) !== undefined;
    }
    const tagClass = normalized.match(/^([a-z][\w-]*)\.([\w-]+)$/i);
    if (tagClass) {
      return String(element.tagName || '').toLowerCase() === tagClass[1].toLowerCase() &&
        hasClass(element, `.${tagClass[2]}`);
    }
    return String(element.tagName || '').toLowerCase() === normalized.toLowerCase();
  };
  const parseAttributes = (element, rawAttributes = '') => {
    for (const attr of String(rawAttributes || '').matchAll(/([:\w-]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+)))?/g)) {
      const name = attr[1];
      const value = attr[2] ?? attr[3] ?? attr[4] ?? '';
      if (!name || name === String(element.tagName || '').toLowerCase()) continue;
      element.setAttribute(name, value);
      if (name === 'id') element.id = value;
      if (name === 'class') element.className = value;
      if (name === 'disabled') element.disabled = true;
      if (name === 'open') element.open = true;
      if (name === 'hidden') element.hidden = true;
    }
  };
  const emitEvent = (element, type, event = {}) => {
    const handlers = listeners.get(type) || [];
    const eventObject = {
      ...event,
      type,
      target: event.target || element,
      currentTarget: element,
      defaultPrevented: false,
      preventDefault() {
        this.defaultPrevented = true;
      }
    };
    return handlers.map(handler => handler.call(element, eventObject));
  };

  const element = {
    tagName: String(tagName || 'div').toUpperCase(),
    hidden: false,
    disabled: false,
    checked: false,
    open: false,
    focused: false,
    scrollIntoViewCalled: false,
    lastScrollIntoViewOptions: null,
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
      for (const match of html.matchAll(/<([a-z][\w:-]*)([^>]*)>/gi)) {
        const rawTag = match[1];
        if (!rawTag || rawTag.startsWith('/')) continue;
        const child = createFakeElement(rawTag);
        parseAttributes(child, match[2]);
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
      const idAttr = id ? ` id="${escapeHtml(id)}"` : '';
      return `<${String(this.tagName || 'div').toLowerCase()}${idAttr}${cls}>${this.innerHTML}</${String(this.tagName || 'div').toLowerCase()}>`;
    },
    get id() {
      return id;
    },
    set id(value) {
      id = String(value ?? '');
      if (id) {
        this.attributes.set('id', id);
      } else {
        this.attributes.delete('id');
      }
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
      const attrName = String(name || '');
      const attrValue = String(value ?? '');
      this.attributes.set(attrName, attrValue);
      if (attrName === 'id') {
        id = attrValue;
      } else if (attrName === 'class') {
        this.className = attrValue;
      } else if (attrName === 'disabled') {
        this.disabled = true;
      } else if (attrName === 'open') {
        this.open = true;
      } else if (attrName === 'hidden') {
        this.hidden = true;
      } else if (attrName.startsWith('data-')) {
        const key = attrName.slice(5).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
        this.dataset[key] = attrValue;
      }
    },
    removeAttribute(name) {
      const attrName = String(name || '');
      this.attributes.delete(attrName);
      if (attrName === 'id') id = '';
      if (attrName === 'disabled') this.disabled = false;
      if (attrName === 'open') this.open = false;
      if (attrName === 'hidden') this.hidden = false;
    },
    getAttribute(name) {
      return this.attributes.get(String(name || ''));
    },
    addEventListener(type, handler) {
      const key = String(type || '');
      if (!key || typeof handler !== 'function') return;
      const handlers = listeners.get(key) || [];
      handlers.push(handler);
      listeners.set(key, handlers);
    },
    removeEventListener(type, handler) {
      const key = String(type || '');
      const handlers = listeners.get(key) || [];
      listeners.set(key, handlers.filter(item => item !== handler));
    },
    dispatchEvent(event) {
      const type = String(event?.type || '');
      if (!type) return true;
      emitEvent(this, type, event);
      return event?.defaultPrevented !== true;
    },
    click() {
      const results = emitEvent(this, 'click');
      const pending = results.filter(result => result && typeof result.then === 'function');
      return pending.length ? Promise.allSettled(pending) : undefined;
    },
    focus() {
      this.focused = true;
    },
    scrollIntoView(options) {
      this.scrollIntoViewCalled = true;
      this.lastScrollIntoViewOptions = options || null;
    },
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
      const selectors = String(selector || '').split(',').map(item => item.trim()).filter(Boolean);
      for (const item of selectors) {
        if (item.startsWith('#')) {
          const found = findById(this, item);
          if (found) return found;
        } else if (item.startsWith('.')) {
          const found = findByClass(this, item);
          if (found) return found;
        } else {
          const found = this.querySelectorAll(item)[0] || null;
          if (found) return found;
        }
      }
      return null;
    },
    querySelectorAll(selector) {
      const results = [];
      const selectors = String(selector || '').split(',').map(item => item.trim()).filter(Boolean);
      const visit = node => {
        for (const child of node.children || []) {
          if (selectors.some(item => matchesSelector(child, item))) {
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

function canonicalConstraintsFor(operatorType) {
  const spec = loadParameterRuleParitySpec();
  return spec.operatorConstraints?.[operatorType] || [];
}

function withCanonicalConstraints(operator) {
  const operatorType = operator?.operatorType || operator?.type || '';
  return {
    ...operator,
    parameterConstraints: canonicalConstraintsFor(operatorType)
  };
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
  panel._ensureAgentWorkspaceState({ sessionId: 'agent-ui-contract' });
  panel.options = overrides.options || {};
  panel.isVisionAgentDeveloperUiEnabled = overrides.developer === true;
  panel.useVisionAgentGenerateFlow = overrides.enabled === true;
  panel.agentGenerateFlowMode = overrides.mode || 'scripted';
  panel.runtimePreviewConsent = overrides.runtimePreviewConsent === true;
  panel.directBuildDebugNextRequest = overrides.directBuildDebugNextRequest === true;
  panel.pendingParameterDrafts = {};
  panel.pendingResourceDrafts = {};
  panel.pendingOperatorBindings = {};
  panel.operatorMetadataCache = new Map(
    Object.entries(loadParameterRuleParitySpec().operatorConstraints || {})
      .map(([operatorType, parameterConstraints]) => [
        operatorType.toLowerCase(),
        { type: operatorType, parameterConstraints }
      ])
  );
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
    let readiness = overrides.previewReadiness ||
      plan?.authoritativeBuildReadiness ||
      plan?.buildReadiness ||
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
      resourceRevision: request.resourceRevision || 0,
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
  panel._requestBackendPlanReadinessPreview = async (...args) => overrides.planReadinessPreview
    ? await overrides.planReadinessPreview(...args)
    : panel._buildTestPlanReadinessPreview(args[0]);
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
    parameterConstraints: canonicalConstraintsFor('ImageAcquisition'),
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
    buildReadiness: overrides.buildReadiness ?? {
      canBuild,
      blockers: canBuild ? [] : [
        {
          id: 'hard_requirement:inspection_object_missing',
          category: 'hard_requirement',
          field: 'inspection_object',
          questionId: '',
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: '请确认检测对象。'
        },
        {
          id: 'hard_requirement:task_type_missing',
          category: 'hard_requirement',
          field: 'task_type',
          questionId: '',
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: '请确认任务类型。'
        }
      ],
      resolvedFields: canBuild
        ? ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'algorithm_strategy']
        : [],
      remainingFields: canBuild ? [] : ['inspection_object', 'task_type'],
      primaryMessage: canBuild ? '规划已完成，可以开始构建。' : '请先完成澄清。',
      contractVersion: 'v2'
    },
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
        { name: 'ModelPath', dataType: 'text', displayName: '模型资源' },
        { name: 'Threshold', dataType: 'number', displayName: '阈值' }
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
    buildReadiness: {
      canBuild: false,
      blockers: [
        {
          id: 'strategy_confirmation:model_or_rule_strategy_missing',
          category: 'strategy_confirmation',
          field: 'algorithm_strategy',
          questionId: 'model_or_rule_strategy',
          blocksBuild: true,
          resolutionMode: 'answer_question',
          publicLabel: '请确认算法策略。'
        }
      ],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: ['algorithm_strategy'],
      primaryMessage: '请确认算法策略。',
      contractVersion: 'v2'
    },
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

function mixedPendingParameterReviewResult() {
  const result = pendingParameterReviewResult();
  result.pendingParameters = [{
    operatorId: 'op_detect',
    actualOperatorId: 'op_detect',
    parameterNames: ['Threshold', 'ModelPath']
  }];
  return result;
}

function assertNoSensitiveLeak(text) {
  assert.doesNotMatch(text, /rawPrompt|systemPrompt|SystemPrompt|chainOfThought|reasoning_content/i);
  assert.doesNotMatch(text, /C:\\|D:\\|\\.onnx|192\\.168\\.|DB1\\.DBX|base64|data:image|sk-secret|token|key/i);
}

test('Assistant failure cards redact unsafe backend diagnostics', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const turn = attachAgentRunTurn(panel);

  panel._renderAssistantFailure(turn, {
    errorMessage: 'Build failed rawPrompt=secret systemPrompt=hidden chainOfThought=private baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token',
    failureSummary: {
      Message: 'Build failed with token=super-secret-value',
      RepairTarget: 'Retry with public metadata only. apiKey=super-secret-value',
      LastOutputSummary: 'Generated D:\\models\\secret.onnx'
    },
    lastAttemptDiagnostics: [
      {
        issues: [
          {
            category: 'planner',
            code: 'rawPrompt=secret',
            message: 'headers={Authorization: Bearer abcdefghijklmnop} plc://line1/DB1.DBX0.0',
            repairHint: 'Remove baseUrl=http://example.invalid and C:\\factory\\secret.onnx'
          }
        ]
      }
    ]
  });

  assert.equal(turn.failureSection.hidden, false);
  assert.match(turn.failureBody.innerHTML, /redacted/);
  assertNoSensitiveLeak(turn.failureBody.innerHTML);
});

test('Firewall blocked alert redacts unsafe backend details', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const chatContainer = createFakeElement();
  panel.container = createContainer({ '#ai-chat-container': chatContainer });
  const turn = attachAgentRunTurn(panel);

  panel._handleFirewallBlocked({
    payload: {
      message: 'Proxy blocked rawPrompt=secret systemPrompt=hidden token=super-secret-value http://192.168.1.8/v1',
      detail: 'Check C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token'
    }
  });

  assert.equal(turn.failureSection.hidden, false);
  assert.match(`${chatContainer.innerHTML} ${turn.failureBody.innerHTML}`, /redacted/);
  assertNoSensitiveLeak(`${chatContainer.innerHTML} ${turn.failureBody.innerHTML}`);
});

test('System chat messages redact unsafe backend text', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const chatContainer = createFakeElement();
  panel.container = createContainer({ '#ai-chat-container': chatContainer });
  panel._addMessage = AiPanel.prototype._addMessage;

  const message = panel._addMessage(
    'system',
    '历史加载失败: rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token'
  );

  assert.match(message.innerHTML, /redacted/);
  assertNoSensitiveLeak(message.innerHTML);
});

test('Cancel and system error handlers redact unsafe boundary text before projection', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const capturedFailures = [];
  panel._renderAssistantFailure = (_turn, failure) => capturedFailures.push(failure);

  panel.isGenerating = true;
  panel._handleCancelResult({ payload: { status: 'cancelled', message: `Cancel requested ${unsafe}` } });
  panel.isGenerating = true;
  panel._handleCancelResult({ payload: { status: 'failed', errorMessage: `Cancel failed ${unsafe}` } });

  attachAgentRunTurn(panel);
  panel._handleError(`Backend error ${unsafe}`);
  panel._handleError(`Detached backend error ${unsafe}`);

  const combined = `${(panel.messages || []).map(item => item.text).join('\n')}\n${JSON.stringify(capturedFailures)}`;
  assert.match(combined, /redacted/);
  assertNoSensitiveLeak(combined);
});

test('Result status notes redact unsafe backend text', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const note = createFakeElement();
  panel.container = createContainer({ '#ai-result-status-note': note });
  panel._setResultStatusNote = AiPanel.prototype._setResultStatusNote;

  panel._setResultStatusNote(
    'Plan 状态已变化 rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token',
    'warning'
  );

  assert.equal(note.hidden, false);
  assert.equal(note.classList.contains('is-warning'), true);
  assert.match(note.textContent, /redacted/);
  assertNoSensitiveLeak(note.textContent);
});

test('Assistant public replies redact unsafe backend text', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const chatContainer = createFakeElement();
  panel.container = createContainer({ '#ai-chat-container': chatContainer });
  panel._addMessage = AiPanel.prototype._addMessage;
  const unsafe = 'AI reply rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token';

  const message = panel._addMessage('ai', unsafe);
  const turn = attachAgentRunTurn(panel);
  panel._setAssistantSectionText(turn, 'reply', unsafe);

  const rendered = `${message.innerHTML} ${turn.replyBody.textContent}`;
  assert.match(rendered, /redacted/);
  assertNoSensitiveLeak(rendered);
});

test('Assistant progress and status helpers redact unsafe public boundary text', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const turn = attachAgentRunTurn(panel);
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';

  const item = panel._updateThinkingStep(`run ${unsafe}`, `step ${unsafe}`, `Running public step ${unsafe}`);
  panel._setAssistantTurnStatus(turn, `Unsafe status ${unsafe}`, `bad tone ${unsafe}`);

  const rendered = [
    item?.textContent,
    item?.dataset?.stepKey,
    turn.statusEl.textContent,
    turn.statusEl.className,
    turn.card.dataset.turnTone
  ].join('\n');

  assert.match(rendered, /redacted/);
  assert.equal(turn.card.dataset.turnTone, 'streaming');
  assertNoSensitiveLeak(rendered);
});

test('Requirement brief and clarification copy redact unsafe backend metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const card = createFakeElement();
  const briefElement = createFakeElement();
  const confidence = createFakeElement();
  panel.container = createContainer({
    '#ai-result-requirement-brief-card': card,
    '#ai-result-requirement-brief': briefElement,
    '#ai-requirement-confidence': confidence
  });
  panel._scrollToBottom = () => {};
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const payload = {
    requirementBrief: {
      scenarioName: `surface check ${unsafe}`,
      intentType: `detect ${unsafe}`,
      draftRiskLevel: `medium ${unsafe}`,
      knownFacts: [`inspect scratches ${unsafe}`],
      missingFacts: [`camera source pending ${unsafe}`],
      attachmentFacts: [`sample image ${unsafe}`],
      objectName: `panel ${unsafe}`,
      imageSource: `camera ${unsafe}`,
      outputTarget: `PLC result ${unsafe}`,
      decisionRule: `OK when no scratch ${unsafe}`,
      nonBlockingMissingFields: [`model_path ${unsafe}`],
      blockingClarificationFields: [`image_source ${unsafe}`],
      clarificationQuestions: [
        {
          field: `image_source ${unsafe}`,
          question: `Which image source should be used? ${unsafe}`,
          reason: `Need deployment-safe source ${unsafe}`,
          priority: `high ${unsafe}`,
          required: true,
          options: [`camera-1 ${unsafe}`]
        }
      ]
    }
  };

  const brief = panel._renderRequirementBrief(payload);
  const followup = panel._buildClarificationFollowupText(brief);
  const turn = attachAgentRunTurn(panel);
  turn.clarificationSection = createFakeElement();
  turn.clarificationBody = createFakeElement();
  panel._renderAssistantClarification(turn, {
    aiExplanation: `Need clarification ${unsafe}`,
    requirementBrief: payload.requirementBrief
  });
  const combined = `${briefElement.innerHTML}\n${followup}\n${turn.clarificationBody.innerHTML}`;

  assert.match(combined, /redacted/);
  assertNoSensitiveLeak(combined);
  assert.doesNotMatch(combined, /super-secret-value|raw-key|rawPrompt=|systemPrompt=|baseUrl=|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i);
});

test('Apply preview redacts unsafe risk and diff metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createFakeElement();
  panel._setWorkbenchState = state => { panel.workbenchState = state; };
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const diff = {
    added: [
      { displayName: `Detect scratches ${unsafe}`, operatorType: `DeepLearning ${unsafe}` }
    ],
    removed: [
      { displayName: `Old detector ${unsafe}`, operatorType: `TemplateMatching ${unsafe}` }
    ],
    modified: [
      {
        op: { displayName: `Classifier ${unsafe}`, operatorType: 'DeepLearning' },
        changes: [
          { name: `ModelPath ${unsafe}`, old: `C:\\factory\\old.onnx ${unsafe}`, new: `/models/new.onnx ${unsafe}` }
        ]
      }
    ],
    addedConnections: [
      { sourceTempId: `op_a ${unsafe}`, sourcePortName: 'Output', targetTempId: 'op_b', targetPortName: `Input ${unsafe}` }
    ],
    removedConnections: []
  };
  const applyRisk = {
    hasWarnings: true,
    totalCount: 3,
    pending: [{ actualOperatorId: `op_detect ${unsafe}`, parameterNames: [`ModelPath ${unsafe}`] }],
    missing: [{ description: `Upload model resource ${unsafe}` }],
    nonBlockingFields: [`model_path ${unsafe}`]
  };

  panel._showApplyPreview(diff, { operators: [] }, { applyRisk });
  const html = panel.container.children[0].innerHTML;

  assert.match(html, /redacted/);
  assertNoSensitiveLeak(html);
  assert.doesNotMatch(html, /super-secret-value|raw-key|rawPrompt=|systemPrompt=|baseUrl=|C:\\factory|secret\.onnx|old\.onnx|new\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i);
});

test('Apply undo failure redacts unsafe canvas diagnostics', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  panel._preApplySnapshot = { operators: [] };
  panel._preApplyCanvasRevision = 0;
  panel.flowCanvas = {
    getFlowRevision: () => 0,
    deserialize: () => {
      throw new Error(`Restore failed ${unsafe}`);
    }
  };
  const originalConsoleError = console.error;
  console.error = () => {};
  try {
    panel._undoApply();
  } finally {
    console.error = originalConsoleError;
  }

  const messages = (panel.messages || []).map(item => item.text).join('\n');
  assert.match(messages, /撤销失败/);
  assert.match(messages, /redacted/);
  assertNoSensitiveLeak(messages);
});

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
    agentGenerateFlowMode: 'scripted'
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

test('AI attachment UI redacts local paths and unsafe report metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const attachments = createFakeElement();
  const attachmentCard = createFakeElement();
  const attachmentPanel = createFakeElement();
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|raw-key|192\.168\.1\.8|C:\\factory|D:\\models|secret\.onnx|other\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token|data-path=/i;
  panel.container = createContainer({
    '#ai-attachments': attachments,
    '#ai-result-attachment-card': attachmentCard,
    '#ai-result-attachments': attachmentPanel
  });
  panel._shouldHandleGenerateRealtimePayload = () => true;
  panel.attachments = [
    { path: 'C:\\factory\\secret.onnx', name: `C:\\factory\\secret.onnx ${unsafe}`, status: 'ready', reason: unsafe },
    { path: 'D:\\models\\other.onnx', name: `other.onnx ${unsafe}`, status: 'ready', reason: '' }
  ];

  panel._handleAttachmentReport({
    sent: [{ path: 'C:\\factory\\secret.onnx', name: `C:\\factory\\secret.onnx ${unsafe}` }],
    skipped: [{ path: 'D:\\models\\other.onnx', name: `D:\\models\\other.onnx ${unsafe}`, reason: `read_failed ${unsafe}` }]
  });
  panel._renderAttachmentPanel();

  const combined = [
    attachments.innerHTML,
    attachmentPanel.innerHTML,
    (panel.messages || []).map(item => item.text).join('\n')
  ].join('\n');

  assert.equal(attachmentCard.hidden, false);
  assert.match(combined, /redacted/);
  assertNoSensitiveLeak(combined);
  assert.doesNotMatch(combined, unsafePattern);
});

test('Generate user message redacts unsafe attachment display names', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const input = createFakeElement();
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|raw-key|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i;
  input.value = '检测瓶盖外观';
  panel.container = createContainer({ '#ai-input': input });
  panel.attachments = [
    { path: 'C:\\factory\\secret.onnx', name: `C:\\factory\\secret.onnx ${unsafe}`, status: 'ready', reason: '' }
  ];
  let captured = null;
  panel._dispatchGenerateRequest = args => {
    captured = args;
    return true;
  };

  await panel._handleGenerate();

  assert.deepEqual(captured.attachmentPaths, ['C:\\factory\\secret.onnx']);
  assert.match(captured.userMessage, /附件/);
  assert.match(captured.userMessage, /redacted/);
  assertNoSensitiveLeak(captured.userMessage);
  assert.doesNotMatch(captured.userMessage, unsafePattern);
});

test('AI capability owner redacts unsafe AgentRun public projection text', async () => {
  const { AiPanelCapabilityOwner } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelCapabilityOwner.mjs');
  const container = createFakeElement();
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|raw-key|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i;
  const owner = new AiPanelCapabilityOwner(container, {
    adapter: {
      loadLatestRun: async () => null,
      loadRun: async () => null,
      cancelRun: async () => null,
      buildEventStreamUrl: () => ''
    }
  });

  owner.errorMessage = `load failed ${unsafe}`;
  owner.activeRunId = `run ${unsafe}`;
  owner.replay = { summary: { status: `failed ${unsafe}` } };
  owner.events = [{
    sequence: 1,
    eventType: `tool.failed ${unsafe}`,
    title: `Validate flow ${unsafe}`,
    summary: `Tool failed ${unsafe}`
  }];
  owner.render();

  assert.match(container.innerHTML, /redacted/);
  assertNoSensitiveLeak(container.innerHTML);
  assert.doesNotMatch(container.innerHTML, unsafePattern);
});

test('developer mode payload includes useVisionAgentGenerateFlow=true', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });

  assert.equal(panel._buildAgentGenerateFlowRequestPayload().useVisionAgentGenerateFlow, true);
});

test('retired GenerateFlow modes are coerced to the official compatibility mode', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true, mode: 'planner' });

  assert.deepEqual(panel._buildAgentGenerateFlowRequestPayload(), {
    useVisionAgentGenerateFlow: true,
    agentGenerateFlowMode: 'scripted'
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

test('developer mode exposes no retired GenerateFlow mode selectors', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });

  assert.doesNotMatch(panel._renderAgentDeveloperControls(), /data-agent-generate-mode/);
});

test('ordinary UI still hides developer controls by default', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });

  assert.equal(panel._renderAgentDeveloperControls(), '');
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

  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  panel._replayLatestAgentRunPublicEvents = async () => {
    throw new Error(`Replay failed ${unsafe}`);
  };
  handlers.get('click')();
  await flushAsync(3);

  assert.match(panel.lastResultStatusNote.text, /回放最近一次 AgentRun 失败/);
  assert.match(panel.lastResultStatusNote.text, /redacted/);
  assertNoSensitiveLeak(panel.lastResultStatusNote.text);
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
    agentGenerateFlowMode: 'scripted',
    runtimePreviewConsent: true
  });
  assert.equal(panel.runtimePreviewConsent, false);
  assert.deepEqual(panel._buildAgentGenerateFlowRequestPayload(), {
    useVisionAgentGenerateFlow: true,
    agentGenerateFlowMode: 'scripted'
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
  assert.equal(payload.agentGenerateFlowMode, 'scripted');
  assert.equal(payload.runtimePreviewConsent, true);
  assert.equal(payload.attachmentCount, 1);
  assert.deepEqual(payload.attachments, []);
  assert.equal(payload.existingFlowJson, '{"operators":[]}');
  assert.deepEqual(payload.templateSelection, { mode: 'template_fill', templateId: 'tmpl-1' });
});

test('Generate request additional context redacts unsafe queued hint metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true, mode: 'planner' });
  panel.container = createContainer({
    '#ai-chat-container': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._shouldRouteIntentBeforeGenerate = () => false;
  panel._shouldOpenPlanModeBeforeBuild = () => false;
  panel._shouldUseAgentRunEventStream = () => true;
  panel._startAssistantTurn = () => attachAgentRunTurn(panel);
  panel._renderManualRetryBanner = () => {};
  panel._renderAgentRuntime = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderPlanWorkspace = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  let capturedPayload = null;
  panel._dispatchAgentRunGenerateRequest = payload => {
    capturedPayload = payload;
    return Promise.resolve();
  };
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';

  const accepted = panel._dispatchGenerateRequest({
    description: '检测工件划痕',
    hint: `queued clarification ${unsafe}`,
    userMessage: '检测工件划痕',
    skipPlan: true,
    skipPlanSource: 'test'
  });

  assert.equal(accepted, true);
  assert.ok(capturedPayload);
  assert.match(capturedPayload.additionalContext, /redacted/);
  assertNoSensitiveLeak(capturedPayload.additionalContext);
  assert.doesNotMatch(capturedPayload.additionalContext, /super-secret-value|raw-key|rawPrompt=|systemPrompt=|baseUrl=|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i);
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
  assert.match(collectProcessText(turn), /理解需求：完成/);
  assert.match(collectProcessText(turn), /整理工程上下文/);
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

test('Plan workspace shows rule fallback as recoverable confirmation state', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-plan-workspace': planWorkspace,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const planResult = backendPlanResult({
    canPlan: true,
    canBuild: false,
    clarificationQuestions: [],
    planSource: 'rule_fallback',
    fallbackReason: 'semantic_model_request_failed',
    buildReadiness: {
      canBuild: false,
      blockers: [
        { field: 'image_source', blocksBuild: true, category: 'hard_requirement' }
      ],
      resolvedFields: ['inspection_object'],
      remainingFields: ['image_source'],
      primaryMessage: '需要确认图像来源。',
      contractVersion: 'v2'
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      objectSignals: ['metal surface'],
      taskSignals: ['scratch'],
      missingFields: ['image_source'],
      blockingReasons: ['image_source_missing'],
      publicReason: '规则兜底可继续，但图像来源还未确认。'
    },
    semanticExtraction: {
      isVisionRequest: true,
      source: 'rule_fallback',
      taskType: 'surface_defect',
      inspectionObject: 'metal surface',
      okCondition: 'no scratch is OK',
      ngCondition: 'scratch is NG',
      imageSource: 'unknown',
      missingFields: ['image_source'],
      failureCode: 'semantic_model_request_failed',
      sanitizedErrorMessage: 'semantic service unavailable',
      metadataOnly: true
    }
  });

  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(planResult, 'detect metal scratches');
  panel._renderPlanWorkspace(panel.pendingVisionPlan);
  const actionState = panel._getPlanBuildActionState(panel.pendingVisionPlan);

  assert.match(planWorkspace.innerHTML, /AI 理解成了什么/);
  assert.match(planWorkspace.innerHTML, /暂无可回答的关键问题/);
  assert.doesNotMatch(planWorkspace.innerHTML, /还需确认 1 项/);
  assert.match(planWorkspace.innerHTML, /规则兜底/);
  assert.match(actionState.statusText, /总计 1 项；构建前必须确认 1 项；可构建后补齐 0 项/);
  assert.equal(actionState.label, '还需补充 1 项信息');
  assert.match(planWorkspace.innerHTML, /id="ai-btn-start-build">开始构建/);
  assert.doesNotMatch(planWorkspace.innerHTML, /Plan 失败|规划失败|语义抽取失败/);

  panel.activePlanRunId = 'semantic_fallback_run';
  const publicEvent = panel._normalizePublicLiveEvent({
    runId: 'semantic_fallback_run',
    sequence: 1,
    eventType: 'semantic.failed',
    stage: 'semantic_extraction',
    status: 'failed',
    payload: {
      metadata: {
        semanticSource: 'rule_fallback',
        failureCode: 'semantic_model_request_failed'
      },
      metadataOnly: true
    },
    metadataOnly: true,
    redactionPass: true
  }, { source: 'plan-run' });

  assert.equal(publicEvent.status, 'warning');
  assert.equal(publicEvent.kind, 'warning');
  assert.match(publicEvent.title, /已启用规则兜底/);
});

test('Plan workspace hides raw semantic diagnostics until diagnostics details', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-plan-workspace': planWorkspace,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const planResult = backendPlanResult({
    canPlan: true,
    canBuild: false,
    clarificationQuestions: [],
    planSource: 'rule_fallback',
    fallbackReason: 'semantic_json_parse_failed',
    publicEvents: [
      {
        stage: 'semantic_fallback_used',
        status: 'warning',
        title: 'semantic fallback',
        summary: 'semantic fallback used',
        metadata: {
          failureCode: 'semantic_json_parse_failed',
          taskType: 'surface_defect',
          metadataOnly: true
        },
        metadataOnly: true
      }
    ],
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      objectSignals: ['raw-object-signal'],
      taskSignals: ['raw-task-signal'],
      missingFields: ['image_source'],
      blockingReasons: [],
      publicReason: 'need image source'
    },
    decisionTrace: {
      taskType: 'surface_defect',
      objectSignalsHit: ['raw-object-signal'],
      fallbackReason: 'semantic_json_parse_failed',
      metadataOnly: true
    },
    semanticExtraction: {
      isVisionRequest: true,
      source: 'rule_fallback',
      taskType: 'surface_defect',
      inspectionObject: 'product surface',
      okCondition: 'clean is OK',
      ngCondition: 'scratch is NG',
      imageSource: 'unknown',
      missingFields: ['image_source'],
      failureCode: 'semantic_json_parse_failed',
      metadataOnly: true
    }
  });

  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(planResult, 'detect scratches');
  panel._renderPlanWorkspace(panel.pendingVisionPlan);

  const visibleHtml = planWorkspace.innerHTML.replace(/<details[\s\S]*<\/details>/g, '');
  assert.doesNotMatch(visibleHtml, /semantic\.taskType|semantic\.failureCode|failureCode|objectSignals|metadataOnly|Agent Trace|Trace/);
  assert.match(visibleHtml, /AI 理解成了什么/);
  assert.match(visibleHtml, /推荐方案/);
  assert.match(visibleHtml, /关键问题/);
  assert.ok(visibleHtml.indexOf('AI 理解成了什么') < visibleHtml.indexOf('推荐方案'));
  assert.ok(visibleHtml.indexOf('推荐方案') < visibleHtml.indexOf('关键问题'));
  assert.match(planWorkspace.innerHTML, /风险与工程详情/);
  assert.match(planWorkspace.innerHTML, /semantic\.taskType/);
  assert.match(planWorkspace.innerHTML, /semantic\.failureCode/);
  assert.match(planWorkspace.innerHTML, /objectSignals/);
  assert.match(planWorkspace.innerHTML, /metadataOnly/);
  assert.match(planWorkspace.innerHTML, /Agent Trace/);
});

test('Plan workspace missing fields render user-facing missing information count and chat guidance', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  const turn = attachAgentRunTurn(panel);
  panel.container = createContainer({
    '#ai-plan-workspace': planWorkspace,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const planResult = backendPlanResult({
    canPlan: true,
    canBuild: false,
    clarificationQuestions: [],
    buildReadiness: {
      canBuild: false,
      blockers: [
        { field: 'image_source', blocksBuild: true, category: 'hard_requirement' },
        { field: 'acceptance_criteria', blocksBuild: true, category: 'hard_requirement' }
      ],
      resolvedFields: ['inspection_object'],
      remainingFields: ['image_source', 'acceptance_criteria'],
      primaryMessage: '需要补充图像来源和判定标准。',
      contractVersion: 'v2'
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      objectSignals: [],
      taskSignals: [],
      missingFields: ['image_source', 'acceptance_criteria'],
      blockingReasons: [],
      publicReason: '缺少图像来源和判定标准。'
    },
    semanticExtraction: {
      isVisionRequest: true,
      source: 'model',
      taskType: 'surface_defect',
      inspectionObject: '产品表面',
      imageSource: 'unknown',
      missingFields: ['image_source', 'acceptance_criteria'],
      metadataOnly: true
    }
  });

  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(planResult, '检测产品表面');
  panel._renderPlanWorkspace(panel.pendingVisionPlan);

  assert.match(planWorkspace.innerHTML, /暂无可回答的关键问题/);
  assert.doesNotMatch(planWorkspace.innerHTML, /还需确认 2 项/);
  assert.match(planWorkspace.innerHTML, /图像来源/);
  assert.match(planWorkspace.innerHTML, /OK \/ NG 判定/);
  assert.doesNotMatch(planWorkspace.innerHTML, /id="ai-plan-focus-confirmation"|id="ai-plan-use-recommended-defaults"/);
  assert.doesNotMatch(turn.card.innerHTML, /还需补充 2 项信息|补充信息/);
});

test('Plan workspace makes the first canonical question focal without a supplement CTA', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const planResult = backendPlanResult({
    canBuild: false,
    clarificationQuestions: [
      {
        id: 'image_source',
        field: 'image_source',
        title: '图像来源是什么？',
        why: '构建前需要确认输入来源。',
        defaultValue: 'camera_pending',
        defaultAssumption: '先保留相机待绑定。',
        impact: '未确认前不能构建。',
        options: [
          {
            value: 'camera_pending',
            label: '稍后绑定相机',
            recommended: true,
            description: '保持待补。',
            impact: '仍需确认。'
          },
          {
            value: 'file_sample',
            label: '使用离线样张',
            recommended: false,
            description: '使用样张验证。',
            impact: '不代表产线相机已绑定。'
          }
        ]
      }
    ],
    buildReadiness: {
      canBuild: false,
      blockers: [
        { field: 'image_source', questionId: 'image_source', blocksBuild: true, category: 'hard_requirement' }
      ],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: ['image_source'],
      primaryMessage: '需要确认图像来源。',
      contractVersion: 'v2'
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      missingFields: ['image_source'],
      blockingReasons: ['image_source_missing'],
      publicReason: '需要确认图像来源。'
    }
  });

  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(planResult, '检测产品表面');
  panel._renderPlanWorkspace(panel.pendingVisionPlan);

  assert.match(planWorkspace.innerHTML, /data-ai-hook="clarification-question"/);
  assert.match(planWorkspace.innerHTML, /图像来源是什么？/);
  assert.doesNotMatch(planWorkspace.innerHTML, /id="ai-plan-focus-confirmation"|更多方案细节/);
});

test('Plan workspace does not fall back to the Composer when canonical options are unavailable', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  const input = createFakeElement();
  panel.container = createContainer({
    '#ai-input': input,
    '#ai-plan-workspace': planWorkspace,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const planResult = backendPlanResult({
    canBuild: false,
    clarificationQuestions: [],
    buildReadiness: {
      canBuild: false,
      blockers: [
        { field: 'image_source', blocksBuild: true, category: 'hard_requirement' }
      ],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: ['image_source'],
      primaryMessage: '需要确认图像来源。',
      contractVersion: 'v2'
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      missingFields: ['image_source'],
      blockingReasons: ['image_source_missing'],
      publicReason: '需要确认图像来源。'
    }
  });

  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(planResult, '检测产品表面');
  panel._renderPlanWorkspace(panel.pendingVisionPlan);

  assert.equal(input.focused, false);
  assert.match(planWorkspace.innerHTML, /不会用资源项或前端临时问卷代替/);
  assert.doesNotMatch(planWorkspace.innerHTML, /id="ai-plan-focus-confirmation"/);
});

test('Plan recommended-answer handler reports blocked count instead of opening Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-plan-workspace': planWorkspace,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const planResult = backendPlanResult({
    canBuild: false,
    clarificationQuestions: [],
    buildReadiness: {
      canBuild: false,
      blockers: [
        { field: 'image_source', blocksBuild: true, category: 'hard_requirement' },
        { field: 'acceptance_criteria', blocksBuild: true, category: 'hard_requirement' }
      ],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: ['image_source', 'acceptance_criteria'],
      primaryMessage: '需要补充图像来源和判定标准。',
      contractVersion: 'v2'
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      missingFields: ['image_source', 'acceptance_criteria'],
      blockingReasons: ['image_source_missing', 'acceptance_criteria_missing'],
      publicReason: '需要补充图像来源和判定标准。'
    }
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(planResult, '检测产品表面');
  panel._dispatchGenerateRequest = () => {
    throw new Error('Build should not dispatch while hard blockers remain');
  };
  panel._renderPlanWorkspace(panel.pendingVisionPlan);

  await panel._handlePlanUseRecommendedDefaultsClick(panel.pendingVisionPlan);

  assert.match(panel.lastResultStatusNote.text, /仍需先确认 2 项构建前信息/);
  assert.equal(panel.lastResultStatusNote.tone, 'warning');
});

test('Plan recommended-answer handler keeps the existing buildable path', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-plan-workspace': planWorkspace,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: true,
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: '构建条件已满足。',
      contractVersion: 'v2'
    },
    requirementMaturity: {
      maturity: 'actionable',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: true,
      missingFields: [],
      blockingReasons: [],
      publicReason: '需求足够明确，可以进入构建。'
    }
  }), '检测产品表面');
  let started = false;
  panel._startBuildFromCurrentPlan = async () => {
    started = true;
    return true;
  };
  panel._renderPlanWorkspace(panel.pendingVisionPlan);

  await panel._handlePlanUseRecommendedDefaultsClick(panel.pendingVisionPlan);

  assert.equal(started, true);
  assert.match(panel.lastResultStatusNote.text, /正在进入构建/);
  assert.equal(panel.lastResultStatusNote.tone, 'info');
});

test('Plan draft-view handler switches view or explains no draft exists', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-plan-workspace': planWorkspace,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    clarificationQuestions: []
  }), '检测产品表面');
  panel._renderPlanWorkspace(panel.pendingVisionPlan);

  panel._handlePlanViewDraftClick();
  assert.match(panel.lastResultStatusNote.text, /开始构建后会生成可查看的流程草稿/);
  assert.equal(panel.lastResultStatusNote.tone, 'info');

  let switchedTo = '';
  panel.currentResult = { buildResult: { flow: { operators: [] } } };
  panel._setWorkspaceViewMode = mode => {
    switchedTo = mode;
    panel.workspaceViewMode = mode;
  };
  panel._renderPlanWorkspace(panel.pendingVisionPlan);
  panel._handlePlanViewDraftClick();

  assert.equal(switchedTo, 'build');
  assert.match(panel.lastResultStatusNote.text, /已切换到构建草稿视图/);
  assert.equal(panel.lastResultStatusNote.tone, 'info');
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
  assert.match(plan.innerHTML, /ai-plan-v2-empty/);
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

test('Intent Router failure delegates to backend Plan without local business routing', async () => {
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
  panel._requestBackendIntentRouterRun = async () => { throw new Error('router unavailable'); };
  panel._shouldUsePlanRunEventStream = () => false;
  let planCalls = 0;
  panel._requestBackendVisionPlan = async request => {
    planCalls += 1;
    assert.equal(request.description, '检测目标是外星人');
    return backendPlanResult({ goal: 'backend planner decides' });
  };
  panel._dispatchGenerateRequest({ description: '检测目标是外星人', userMessage: '检测目标是外星人' });
  await flushAsync(5);
  assert.equal(planCalls, 1);
  assert.equal(panel.pendingVisionPlan.goal, 'backend planner decides');
  assert.equal(typeof panel._buildLocalIntentRouterFallback, 'undefined');
});
test('Plan Mode renders one canonical clarification workspace without starting Build', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const overview = createFakeElement();
  const plan = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': createFakeElement(),
    '#ai-agent-workspace-overview': overview,
    '#ai-plan-workspace': plan,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = async () => backendPlanResult({ goal: 'metal scratch inspection workflow' });
  panel._dispatchGenerateRequest({ description: '帮我做一个金属表面划痕检测流程', userMessage: '帮我做一个金属表面划痕检测流程' });
  await flushAsync();
  assert.equal(panel.activeAgentRunId, null);
  assert.equal(panel.agentWorkspaceMode, 'plan');
  assert.match(plan.innerHTML, /data-ai-hook="clarification-workspace"/);
  assert.match(plan.innerHTML, /还需确认 \d+ 项/);
  assert.equal((plan.innerHTML.match(/data-ai-hook="clarification-workspace"/g) || []).length, 1);
  assert.doesNotMatch(plan.innerHTML, /资源补齐会在开始构建后出现|ai-clarification-plan-card/);
  assert.match(overview.innerHTML, /暂不能构建/);
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

test('Plan Mode request failure redacts unsafe assistant reply and status note', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const chatContainer = createFakeElement();
  panel.container = createContainer({
    '#ai-input': createFakeElement(),
    '#ai-chat-container': chatContainer,
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const sectionTexts = [];
  const originalSetAssistantSectionText = panel._setAssistantSectionText;
  panel._setAssistantSectionText = (turn, field, text, options) => {
    sectionTexts.push(String(text || ''));
    return originalSetAssistantSectionText.call(panel, turn, field, text, options);
  };
  panel._shouldUsePlanRunEventStream = () => false;
  panel._requestBackendVisionPlan = async () => {
    throw new Error(`Planner request failed ${unsafe}`);
  };

  panel._enterPlanModeFromPrompt({
    description: 'detect board defects',
    userMessage: 'detect board defects',
    input: panel.container.querySelector('#ai-input')
  });
  await flushAsync();

  const rendered = `${sectionTexts.join('\n')}\n${panel.lastResultStatusNote?.text || ''}`;
  assert.match(rendered, /redacted/);
  assert.match(rendered, /规划模式失败/);
  assertNoSensitiveLeak(rendered);
});

test('Plan workspace public fields redact unsafe backend metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.agentWorkspaceMode = 'plan';
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|raw-key|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token|baseUrl=/i;
  const normalized = panel._normalizeBackendPlanResult(backendPlanResult({
    planHash: `sha256:public-fields ${unsafe}`,
    originalUserPrompt: `preserve user build input ${unsafe}`,
    goal: `surface inspection ${unsafe}`,
    nextAction: `accept defaults ${unsafe}`,
    requirementUnderstanding: [`Need defect inspection ${unsafe}`],
    recommendedRoute: {
      routeId: `route_${unsafe}`,
      title: `Unsafe route ${unsafe}`,
      summary: `Route summary ${unsafe}`,
      operators: [`ImageAcquisition ${unsafe}`, `SurfaceDefectDetection ${unsafe}`],
      templateDecision: `Template decision ${unsafe}`
    },
    clarificationQuestions: [
      {
        id: 'unsafe_question',
        title: `Question title ${unsafe}`,
        why: `Question why ${unsafe}`,
        defaultValue: 'unsafe_answer',
        defaultAssumption: `Default assumption ${unsafe}`,
        impact: `Question impact ${unsafe}`,
        options: [
          {
            value: 'unsafe_answer',
            label: `Unsafe label ${unsafe}`,
            recommended: true,
            description: `Unsafe description ${unsafe}`,
            impact: `Unsafe impact ${unsafe}`
          }
        ]
      }
    ],
    recommendedDefaults: [
      { id: 'unsafe_default', label: `Default label ${unsafe}`, value: `Default value ${unsafe}`, impact: `Default impact ${unsafe}` }
    ],
    risks: [`Risk text ${unsafe}`],
    acceptanceCriteria: [`Acceptance text ${unsafe}`],
    executablePlan: [`Step text ${unsafe}`],
    buildReadiness: {
      contractVersion: 'v2',
      canBuild: false,
      primaryMessage: `Readiness message ${unsafe}`,
      blockers: [
        { id: 'unsafe_blocker', category: 'hard_requirement', field: 'inspection_object', publicLabel: `Blocker label ${unsafe}`, blocksBuild: true }
      ]
    },
    requirementMaturity: {
      maturity: 'ambiguous',
      taskType: 'surface_defect',
      canPlan: true,
      canBuild: false,
      objectSignals: [`metal ${unsafe}`],
      taskSignals: [`scratch ${unsafe}`],
      missingFields: ['inspection_object'],
      blockingReasons: [`blocking reason ${unsafe}`],
      publicReason: `Maturity reason ${unsafe}`
    }
  }));

  panel.pendingVisionPlan = normalized;
  panel._renderPlanWorkspace(normalized);
  const displayProjection = JSON.stringify({
    goal: normalized.goal,
    route: normalized.route,
    questions: normalized.questions,
    assumptions: normalized.assumptions,
    steps: normalized.steps,
    risks: normalized.risks,
    acceptanceCriteria: normalized.acceptanceCriteria,
    buildReadiness: normalized.buildReadiness,
    requirementMaturity: normalized.requirementMaturity
  });
  const combined = `${planWorkspace.innerHTML}\n${displayProjection}`;

  assert.match(combined, /redacted/);
  assert.doesNotMatch(combined, unsafePattern);
  assert.match(normalized.originalDescription, /C:\\factory\\secret\.onnx/);
});

test('ordinary build prompt stays Plan-first', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
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
  assert.match(plan.innerHTML, /关键假设/);
  assert.match(plan.innerHTML, /开始构建/);
  assert.doesNotMatch(plan.innerHTML, /按推荐方案开始构建/);
  assert.match(overview.innerHTML, /正式 Plan→Build/);
});

test('quick example selection only fills the shared composer without sending', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const input = createFakeElement();
  panel.attachments = [];
  panel.container = createContainer({
    '#ai-input': input,
    '#ai-chat-container': createFakeElement()
  });
  let sendCount = 0;
  panel._handleGenerate = () => {
    sendCount += 1;
  };

  const selected = await panel._handleQuickExampleSelection('检测金属零件表面的划痕缺陷。');

  assert.equal(selected, true);
  assert.equal(input.value, '检测金属零件表面的划痕缺陷。');
  assert.equal(sendCount, 0);
});

test('unknown skipPlan and build-like explicit modes cannot bypass Plan', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });

  assert.equal(panel._shouldOpenPlanModeBeforeBuild({ explicitMode: 'build' }), true);
  assert.equal(panel._shouldOpenPlanModeBeforeBuild({ explicitMode: 'stable' }), true);
  assert.equal(panel._shouldOpenPlanModeBeforeBuild({ explicitMode: 'deprecated' }), true);
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
  assert.match(turn.replyBody.textContent, /详细阶段和当前工作见左侧工作台/);
  assert.match(plan.innerHTML, /规划进行中工作台/);
  assert.doesNotMatch(plan.innerHTML, /data-planning-phase="context"[^]*已完成/);
  await waitFor(() => panel.pendingVisionPlan?.planId === 'plan_backend_1', 'streamed plan result');

  assert.equal(panel.pendingVisionPlan.goal, 'streamed plan ready');
  assert.equal(panel.isGenerating, false);
  assert.match(turn.replyBody.textContent, /规划已完成，请确认推荐项或手动回答后开始构建/);
  assert.match(collectProcessText(turn), /整理工程上下文：完成/);
  assert.match(collectProcessText(turn), /生成方案：进行中|生成方案：完成/);
  assert.match(collectProcessText(turn), /校验方案：进行中|校验方案：完成/);
  assert.match(overview.innerHTML, /streamed plan ready/);
  assert.match(plan.innerHTML, /风险与工程详情/);
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

test('Workbench runtime and stage timeline redact unsafe backend metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const runtime = createFakeElement();
  const stageCard = createFakeElement();
  const stageTimeline = createFakeElement();
  const stageSummary = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-runtime': runtime,
    '#ai-result-stage-timeline-card': stageCard,
    '#ai-result-stage-timeline': stageTimeline,
    '#ai-stage-timeline-summary': stageSummary
  });
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';

  panel._renderAgentRuntime({
    turnIntent: `custom_intent ${unsafe}`,
    interactionState: `custom_state ${unsafe}`,
    routerConfidence: `custom_confidence ${unsafe}`,
    performanceBudget: {
      totalDurationMs: 1234,
      estimatedInputTokens: 200,
      estimatedOutputTokens: 100,
      budgetStatus: 'warning',
      warnings: [`Slow stage ${unsafe}`]
    }
  });
  panel._renderStageTimeline([
    {
      stage: `custom_stage ${unsafe}`,
      status: 'warning',
      durationMs: `17 ${unsafe}`,
      summary: `Stage emitted unsafe diagnostics ${unsafe}`
    }
  ]);

  const combined = `${runtime.innerHTML}\n${stageTimeline.innerHTML}\n${stageSummary.textContent}`;
  assert.match(combined, /redacted/);
  assert.doesNotMatch(combined, /super-secret-value|raw-key|rawPrompt=|systemPrompt=|baseUrl=|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i);
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
  assert.match(collectProcessText(turn), /生成方案：超时|规则兜底：完成/);
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

test('Start Build uses BuildFromPlan only after authoritative readiness allows it', async () => {
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
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'algorithm_strategy'],
      remainingFields: [],
      primaryMessage: 'Ready.',
      contractVersion: 'v2'
    }
  }));
  let captured = null;
  panel._dispatchGenerateRequest = args => { captured = args; return true; };
  assert.equal(await panel._startBuildFromCurrentPlan(), true);
  assert.equal(captured.skipPlan, true);
  assert.equal(captured.skipPlanSource, 'confirmed_plan');
  assert.equal(captured.buildFromPlan.planId, 'plan_build_1');
  assert.equal(captured.buildFromPlan.planHash, 'sha256:plan-build-1');
});
test('Recommended strategy remains optimistic until one backend readiness preview confirms the batch', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult());
  let previewCalls = 0;
  panel._requestBackendPlanReadinessPreview = async request => {
    previewCalls += 1;
    return {
      planId: request.planId,
      planHash: request.planHash,
      requirementMode: request.requirementMode,
      answerRevision: request.answerRevision,
      resourceRevision: request.resourceRevision,
      acceptedAnswers: request.confirmedAnswers,
      buildReadiness: {
        canBuild: true,
        blockers: [],
        resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'algorithm_strategy'],
        remainingFields: [],
        primaryMessage: 'Ready.',
        contractVersion: 'v2'
      },
      contractValid: true
    };
  };
  panel._selectPlanQuestionOption('model_or_rule_strategy', 'deep_learning');
  assert.equal(panel.agentWorkspaceState.projection.optimisticAnswers.length, 1);
  assert.equal(panel.agentWorkspaceState.projection.buildAction.canStart, false);
  await flushAsync(4);
  assert.equal(previewCalls, 1);
  assert.equal(panel.agentWorkspaceState.projection.optimisticAnswers.length, 0);
  assert.equal(panel.agentWorkspaceState.projection.confirmedAnswers[0].field, 'algorithm_strategy');
  assert.equal(panel.agentWorkspaceState.projection.buildAction.canStart, true);
});
test('Contract-invalid empty-option questions fail closed instead of creating a second free-text path', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const plan = panel._normalizeBackendPlanResult({
    planContractVersion: 'v2',
    planId: 'invalid-options',
    planHash: 'sha256:invalid-options',
    goal: '病灶检测',
    clarificationQuestions: [{ id: 'task_type', field: 'task_type', title: '任务类型', options: [] }],
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'hard_requirement:task_type_missing', category: 'hard_requirement', field: 'task_type', questionId: 'task_type', blocksBuild: true, resolutionMode: 'answer_question', publicLabel: '任务类型待确认' }],
      resolvedFields: [],
      remainingFields: ['task_type'],
      primaryMessage: '任务类型待确认',
      contractVersion: 'v2'
    }
  });
  panel.pendingVisionPlan = plan;
  panel._dispatchAgentWorkspaceEvent({
    type: 'workspace/plan-received',
    payload: { plan }
  });
  panel._renderPlanWorkspace(plan);
  assert.equal(panel.agentWorkspaceState.projection.buildAction.canStart, false);
  assert.doesNotMatch(planWorkspace.innerHTML, /ai-plan-custom-input-field|data-ai-plan-option=/);
  assert.match(planWorkspace.innerHTML, /前端不会创建替代答案入口/);
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
  panel._requestBackendPlanReadinessPreview = async request => ({
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
  await flushAsync(3);
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

test('Readiness timeout exits validating and retry accepts only the matching authoritative preview', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-btn-start-build-inline': createFakeButton()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult());
  panel.planReadinessTimeoutMs = 1000;
  panel._requestBackendPlanReadinessPreview = (_request, options = {}) => new Promise((_, reject) => {
    options.signal?.addEventListener('abort', () => {
      const error = new Error('aborted');
      error.name = 'AbortError';
      reject(error);
    }, { once: true });
  });

  assert.equal(panel._requestPlanReadinessPreview(panel.pendingVisionPlan, { reason: 'timeout_test' }), true);
  assert.equal(panel.agentWorkspaceState.readinessStatus, 'validating');
  await new Promise(resolve => setTimeout(resolve, 1100));
  assert.equal(panel.agentWorkspaceState.readinessStatus, 'timeout');
  assert.equal(panel.activePlanReadinessPreviewRequest, null);
  assert.match(panel._getPlanBuildActionState(panel.pendingVisionPlan).label, /超时/);

  panel._requestBackendPlanReadinessPreview = async request => ({
    ...panel._buildTestPlanReadinessPreview(request),
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'algorithm_strategy'],
      remainingFields: [],
      primaryMessage: 'Ready.',
      contractVersion: 'v2'
    }
  });
  assert.equal(panel._requestPlanReadinessPreview(panel.pendingVisionPlan, { reason: 'retry' }), true);
  await waitFor(() => panel.agentWorkspaceState.readinessStatus === 'ready', 'matching readiness retry');
  assert.equal(panel._getCurrentCanonicalPreview(panel.pendingVisionPlan)?.buildReadiness?.canBuild, true);
  assert.equal(panel._getPlanBuildActionState(panel.pendingVisionPlan).canStart, true);
});

test('Readiness abort returns to idle and missing canonical preview never masquerades as validating', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-btn-start-build-inline': createFakeButton()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult());
  panel._requestBackendPlanReadinessPreview = (_request, options = {}) => new Promise((_, reject) => {
    options.signal?.addEventListener('abort', () => {
      const error = new Error('aborted');
      error.name = 'AbortError';
      reject(error);
    }, { once: true });
  });

  panel._requestPlanReadinessPreview(panel.pendingVisionPlan, { reason: 'abort_test' });
  assert.equal(panel.agentWorkspaceState.readinessStatus, 'validating');
  panel._resetPlanReadinessPreviewState({ abort: true });
  await flushAsync(2);
  assert.equal(panel.agentWorkspaceState.readinessStatus, 'idle');
  assert.equal(panel.activePlanReadinessPreviewRequest, null);
  const action = panel._getPlanBuildActionState(panel.pendingVisionPlan);
  assert.match(action.label, /尚未获得权威校验结果/);
  assert.doesNotMatch(action.label, /正在校验/);
  assert.equal(action.canRetryReadiness, true);
});

test('Canonical field aliases are stored optimistically but cannot override backend readiness', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    clarificationQuestions: [{
      id: 'medical_modality_and_lesion_type',
      field: 'medical_modality_and_lesion_type',
      title: '检查类型',
      options: [
        { value: 'ct_lung_nodule_detection', label: 'CT 肺结节', recommended: true },
        { value: 'mri_lesion_detection', label: 'MRI 病灶', recommended: false }
      ]
    }],
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'hard_requirement:task_type_missing', category: 'hard_requirement', field: 'task_type', questionId: 'medical_modality_and_lesion_type', blocksBuild: true, resolutionMode: 'answer_question', publicLabel: '任务类型待确认' }],
      resolvedFields: [],
      remainingFields: ['task_type'],
      primaryMessage: '任务类型待确认',
      contractVersion: 'v2'
    }
  }));
  panel._requestBackendPlanReadinessPreview = () => new Promise(() => {});
  panel._selectPlanQuestionOption('medical_modality_and_lesion_type', 'ct_lung_nodule_detection');
  assert.equal(panel.planQuestionAnswers.task_type.field, 'task_type');
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, false);
  assert.equal(panel.agentWorkspaceState.projection.buildAction.canStart, false);
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

test('Empty backend readiness fails closed without legacy compatibility inference', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: true,
    buildReadiness: { canBuild: false, blockers: [], resolvedFields: [], remainingFields: [], primaryMessage: '', contractVersion: 'v2' }
  }));
  panel.pendingVisionPlan = plan;
  assert.equal(plan.authoritativeBuildReadiness, null);
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, false);
  assert.equal(panel._getPlanBuildActionState(plan).canStart, false);
  assert.match(panel._getPlanBuildBlockedReason(plan), /后端未返回合法 readiness/);
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

test('Safety blockers remain authoritative after local answers', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'safety_blocker:external_output', category: 'safety_blocker', field: 'output_target', questionId: '', blocksBuild: true, resolutionMode: 'non_blocking', publicLabel: '安全复核未通过' }],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: ['output_target'],
      primaryMessage: '安全复核未通过',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;
  panel.planQuestionAnswers = {
    output_target: { questionId: 'output_target', field: 'output_target', value: 'local_result_payload', origin: 'explicit_user_text' }
  };
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, false);
  assert.equal(panel._getPlanBuildActionState(plan).canStart, false);
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

test('Authoritative readiness ignores stale legacy blocking strings', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    blockingReasons: ['hard_requirement:output_target_missing'],
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria', 'output_target'],
      remainingFields: [],
      primaryMessage: 'Ready.',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, true);
  assert.equal(panel._getPlanBuildActionState(plan).canStart, true);
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
  assert.match(overview.innerHTML, /已形成初步方案/);
  assert.match(overview.innerHTML, /还需补充 \d+ 项信息/);
  assert.match(overview.innerHTML, /暂不能构建/);
  assert.equal(inlineBuildButton.disabled, true);
  assert.equal(inlineBuildButton.getAttribute('aria-disabled'), 'true');
  assert.ok(planActionButtons.every(button => button.disabled));
});

test('Frontend does not second-guess authoritative readiness when maturity details are omitted', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: false,
    requirementMaturity: null,
    buildReadiness: {
      canBuild: true,
      blockers: [],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria'],
      remainingFields: [],
      primaryMessage: 'Ready.',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, true);
  assert.equal(panel._getPlanBuildActionState(plan).canStart, true);
});
test('Backend Plan can be plannable while authoritative readiness keeps Build disabled', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    canPlan: true,
    canBuild: true,
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'hard_requirement:image_source_missing', category: 'hard_requirement', field: 'image_source', questionId: '', blocksBuild: true, resolutionMode: 'answer_question', publicLabel: '图像来源待确认' }],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: ['image_source'],
      primaryMessage: '图像来源待确认',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;
  assert.equal(plan.canPlan, true);
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, false);
  assert.equal(panel._getPlanBuildActionState(plan).canStart, false);
});
function pendingFieldPlan(panel, {
  questionId,
  field,
  pendingValue,
  concreteValue,
  pendingAnswerEffect = 'defer',
  concreteAnswerEffect = 'resolve_field'
}) {
  return panel._normalizeBackendPlanResult(backendPlanResult({
    clarificationQuestions: [{
      id: questionId,
      field,
      title: `${field} confirmation`,
      options: [
        { value: pendingValue, label: `Keep ${field} pending`, recommended: true, answerEffect: pendingAnswerEffect },
        { value: concreteValue, label: `Confirm ${field}`, recommended: false, answerEffect: concreteAnswerEffect }
      ]
    }],
    buildReadiness: {
      canBuild: false,
      blockers: [{
        id: `hard_requirement:${field}_missing`,
        category: 'hard_requirement',
        field,
        questionId,
        blocksBuild: true,
        resolutionMode: 'answer_question',
        publicLabel: `${field} pending`
      }],
      resolvedFields: ['inspection_object', 'task_type', 'image_source', 'acceptance_criteria']
        .filter(item => item !== field),
      remainingFields: [field],
      primaryMessage: `${field} pending`,
      contractVersion: 'v2'
    }
  }));
}
test('Pending recommendation is a defer control and never becomes a business answer', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const plan = pendingFieldPlan(panel, {
    questionId: 'image_source',
    field: 'image_source',
    pendingValue: 'camera_pending',
    concreteValue: 'file_sample',
    pendingAnswerEffect: 'defer',
    concreteAnswerEffect: 'resolve_field'
  });
  panel.pendingVisionPlan = plan;
  panel._dispatchAgentWorkspaceEvent({
    type: 'workspace/plan-received',
    payload: { plan }
  });
  panel._renderPlanWorkspace(plan);
  panel._selectPlanQuestionOption('image_source', 'camera_pending');
  assert.equal(panel.planQuestionSelections.image_source, 'camera_pending');
  assert.equal(panel.planQuestionAnswers.image_source, undefined);
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, false);
  assert.match(planWorkspace.innerHTML, /建议暂缓|稍后/);
});
test('Resource defer stays resource_pending and cannot enable Build in Strict or Draft', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    missingResources: [{ resourceKey: 'model:detector', resourceType: 'model_resource', parameterName: 'ModelPath', description: '模型资源待绑定' }],
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'resource_pending:model:detector', category: 'resource_pending', field: 'model_resource', questionId: '', blocksBuild: true, resolutionMode: 'provide_resource', publicLabel: '模型资源待绑定' }],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: ['model_resource'],
      primaryMessage: '模型资源待绑定',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;
  panel._setPendingResourceDraft({ resourceKey: 'model:detector', resourceType: 'model_resource', parameterName: 'ModelPath' }, {
    status: 'deferred', source: 'user_deferred', value: ''
  });
  assert.equal(panel.agentWorkspaceState.projection.missingResources[0].deferred, true);
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, false);
  panel._setRequirementMode('draft', { silent: true });
  assert.equal(panel._getPlanBuildActionState(panel.pendingVisionPlan).canStart, false);
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

test('Strict to Draft persistence clears stale readiness before saving matching readiness in a second mutation', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult({ requirementMode: 'strict' }));
  panel.pendingVisionPlan = plan;
  panel._dispatchAgentWorkspaceEvent({ type: 'workspace/plan-received', payload: { plan } });
  const strictRequest = panel._buildPlanReadinessPreviewRequest(plan);
  panel._applyPlanReadinessPreviewResult(plan, {
    ...panel._buildTestPlanReadinessPreview(strictRequest),
    requirementMode: 'strict',
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'hard_requirement:image_source_missing', category: 'hard_requirement', field: 'image_source', blocksBuild: true }],
      resolvedFields: [],
      remainingFields: ['image_source'],
      contractVersion: 'v2'
    }
  });

  const saved = [];
  panel._queueWorkspaceSnapshotFlush = async reason => {
    saved.push({ reason, mode: panel.requirementMode, preview: panel.agentWorkspaceState.readinessPreview });
    return { saved: true };
  };
  let resolveDraft;
  panel._requestBackendPlanReadinessPreview = request => new Promise(resolve => {
    resolveDraft = () => resolve({
      ...panel._buildTestPlanReadinessPreview(request),
      requirementMode: 'draft',
      buildReadiness: {
        canBuild: true,
        blockers: [],
        resolvedFields: [],
        remainingFields: ['image_source'],
        contractVersion: 'v2'
      }
    });
  });

  panel._setRequirementMode('draft', { silent: true });
  assert.equal(saved.length, 1);
  assert.equal(saved[0].mode, 'draft');
  assert.equal(saved[0].preview, null);

  resolveDraft();
  await waitFor(() => saved.length === 2, 'matching readiness second persistence');
  assert.equal(saved[1].mode, 'draft');
  assert.equal(saved[1].preview.requirementMode, 'draft');
});

test('planning lifecycle client deadline covers backend public budget plus network margin', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.planningDeadlineContract = { totalBudgetMs: 120000, clientNetworkMarginMs: 15000 };
  const scheduled = [];
  const originalSetTimeout = window.setTimeout;
  const originalClearTimeout = window.clearTimeout;
  try {
    window.setTimeout = (callback, delay) => {
      scheduled.push({ callback, delay });
      return scheduled.length;
    };
    window.clearTimeout = () => {};
    panel._beginPlanningLifecycle({ requestId: 'router-budget', phase: 'understand' });
    const timeout = Math.max(...scheduled.map(item => item.delay));
    assert.equal(timeout, 135000);
    assert.ok(timeout > 45000);
  } finally {
    panel._clearPlanningLifecycleTimers();
    window.setTimeout = originalSetTimeout;
    window.clearTimeout = originalClearTimeout;
  }
});

test('planning lifecycle keeps one absolute budget across Router and Planner phases', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.planningDeadlineContract = { totalBudgetMs: 120000, clientNetworkMarginMs: 15000 };
  const scheduled = [];
  const originalSetTimeout = window.setTimeout;
  const originalClearTimeout = window.clearTimeout;
  const originalNow = Date.now;
  let now = 1000;
  try {
    Date.now = () => now;
    window.setTimeout = (callback, delay) => {
      scheduled.push({ callback, delay });
      return scheduled.length;
    };
    window.clearTimeout = () => {};
    panel._beginPlanningLifecycle({ requestId: 'router-shared-budget', phase: 'understand' });

    now += 30000;
    scheduled.length = 0;
    panel._advancePlanningLifecycle('context', 'Router completed.');

    assert.equal(Math.max(...scheduled.map(item => item.delay)), 105000);
    const planRequest = panel._buildPlanModeRequest({ description: 'detect scratches' });
    assert.equal(planRequest.planningBudgetMs, 90000);

    panel.planningLifecycle = { backendDeadlineAt: now - 1, status: 'completed' };
    panel._beginPlanningLifecycle({ requestId: 'new-router-budget', phase: 'understand' });
    assert.equal(panel._getPlanningBackendRemainingMs(), 120000);
  } finally {
    panel._clearPlanningLifecycleTimers();
    Date.now = originalNow;
    window.setTimeout = originalSetTimeout;
    window.clearTimeout = originalClearTimeout;
  }
});

test('backend planning deadline stops Router fallback from starting a new Planner budget', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel._startAssistantTurn = () => ({});
  const error = new Error('published planning budget exceeded');
  error.status = 504;
  error.payload = {
    errorCode: 'planning_deadline_exceeded',
    timeoutKind: 'total_budget_exceeded',
    stage: 'intent_router',
    publicMessage: 'Planning deadline exceeded.'
  };
  let plannerCalls = 0;
  panel._requestBackendIntentRouterRun = async () => { throw error; };
  panel._enterPlanModeFromPrompt = () => {
    plannerCalls += 1;
    return true;
  };

  panel._enterIntentRouterFromPrompt({
    description: 'detect scratches',
    userMessage: 'detect scratches',
    addUserMessage: false
  });
  await flushAsync(5);

  try {
    assert.equal(plannerCalls, 0);
    assert.equal(panel.planningLifecycle.status, 'timeout');
    assert.equal(panel.planningLifecycle.timeoutKind, 'total_budget_exceeded');
  } finally {
    panel._clearPlanningLifecycleTimers();
  }
});

test('backend Plan deadline is presented as timeout rather than generic planning failure', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel._startAssistantTurn = () => ({});
  const error = new Error('published planning budget exceeded');
  error.payload = {
    errorCode: 'planning_deadline_exceeded',
    timeoutKind: 'total_budget_exceeded',
    stage: 'plan_orchestration',
    publicMessage: 'Planning deadline exceeded.'
  };
  panel._requestBackendVisionPlanLive = async () => { throw error; };

  panel._enterPlanModeFromPrompt({
    description: 'detect scratches',
    userMessage: 'detect scratches',
    addUserMessage: false
  });
  await flushAsync(5);

  try {
    assert.equal(panel.planningLifecycle.status, 'timeout');
    assert.equal(panel.planningLifecycle.timeoutKind, 'total_budget_exceeded');
  } finally {
    panel._clearPlanningLifecycleTimers();
  }
});

test('plan missing summary uses authoritative disjoint counts without subtracting resource totals', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const plan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult());
  panel.pendingVisionPlan = plan;
  panel._getCurrentCanonicalPreview = () => ({
    buildReadiness: { canBuild: false, blockers: [], remainingFields: [] },
    pendingConfirmationCount: 99,
    resourcePendingCount: 98,
    hardBlockerCount: 97,
    buildBlockingConfirmationCount: 1,
    buildRequiredResourceCount: 1,
    deferredFieldCount: 1,
    draftAllowedResourceCount: 2,
    mustConfirmBeforeBuildCount: 2,
    fillLaterCount: 3,
    totalIncompleteCount: 5
  });

  const summary = panel._buildPlanMissingSummary(plan, []);

  assert.equal(summary.mustConfirmCount, 2);
  assert.equal(summary.fillLaterCount, 3);
  assert.equal(summary.totalCount, 5);
});

test('Readiness preview failure closes the Build gate and preserves optimistic answers', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(strategyConfirmationPlanResult());
  panel._requestBackendPlanReadinessPreview = async () => { throw new Error('preview unavailable'); };
  panel._selectPlanQuestionOption('model_or_rule_strategy', 'traditional_rule');
  await flushAsync(4);
  assert.equal(panel.previewState, 'failed');
  assert.equal(panel.agentWorkspaceState.projection.optimisticAnswers[0].value, 'traditional_rule');
  assert.equal(panel.agentWorkspaceState.projection.buildAction.canStart, false);
});
test('Plan readiness and cancel failures redact unsafe backend diagnostics', async () => {
  const { AiPanel } = await loadAiPanel();
  const httpClient = (await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js')).default;
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const panel = createPanel(AiPanel, {
    developer: false,
    enabled: true,
    useProductionPreview: true,
    planReadinessPreview: async () => {
      throw new Error(`Preview failed ${unsafe}`);
    }
  });
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
  const plan = pendingFieldPlan(panel, {
    questionId: 'image_source',
    field: 'image_source',
    pendingValue: 'camera_pending',
    concreteValue: 'file_sample'
  });
  panel.pendingVisionPlan = plan;
  panel._selectPlanQuestionOption('image_source', 'file_sample');
  await flushAsync();

  const previewText = `${panel.lastPlanReadinessPreviewError}\n${plan.previewError}\n${planWorkspace.innerHTML}`;
  assert.match(previewText, /redacted/);
  assertNoSensitiveLeak(previewText);

  panel.activePlanRunId = 'plan-run-redacted';
  panel.isGenerating = true;
  const originalPost = httpClient.post;
  httpClient.post = async () => {
    throw new Error(`Cancel plan failed ${unsafe}`);
  };
  try {
    const cancelled = await panel._cancelActivePlanRun();
    assert.equal(cancelled, false);
  } finally {
    httpClient.post = originalPost;
  }

  const messages = (panel.messages || []).map(item => item.text).join('\n');
  assert.match(messages, /取消规划未生效/);
  assert.match(messages, /redacted/);
  assertNoSensitiveLeak(messages);
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

  assert.doesNotMatch(planWorkspace.innerHTML, /Deploy now from model advice/);
  assert.match(planWorkspace.innerHTML, /风险与工程详情/);
  assert.match(panel._getPlanBuildActionState(plan).label, /^还需补充 \d+ 项信息$/);
  assert.notEqual(mainButton.textContent, 'Deploy now from model advice');
});

test('Draft mode cannot locally waive a pending image-source blocker', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    requirementMode: 'draft',
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'resource_pending:image_source', category: 'resource_pending', field: 'image_source', questionId: '', blocksBuild: true, resolutionMode: 'provide_resource', publicLabel: '图像来源待绑定' }],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: ['image_source'],
      primaryMessage: '图像来源待绑定',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;
  panel._setRequirementMode('draft', { silent: true });
  assert.equal(panel._getPlanBuildActionState(panel.pendingVisionPlan).canStart, false);
  assert.equal(await panel._startBuildFromCurrentPlan(), false);
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
  const answerBefore = { ...panel.planQuestionAnswers.algorithm_strategy };

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
  assert.deepEqual(panel.planQuestionAnswers.algorithm_strategy, answerBefore);
  assert.equal(panel.pendingClarificationPayload, null);
});

test('Clarification result summary redacts unsafe assistant explanation', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const summary = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement(),
    '#ai-result-summary': summary
  });
  panel._renderAgentRuntime = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderPlanWorkspace = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._renderManualRetryBanner = () => {};
  panel._setAssistantTurnStatus = () => {};
  panel._setAssistantSectionText = () => {};
  panel._shouldHandleGenerateTerminalPayload = () => true;
  panel.activeAssistantTurn = { clarificationSection: createFakeElement() };
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';

  panel._handleResult({
    payload: {
      success: false,
      status: 'clarification_required',
      failureType: 'clarification_required',
      clarificationRequired: true,
      aiExplanation: `Need clarification ${unsafe}`,
      shouldResetPendingPlan: false
    }
  });

  assert.match(summary.textContent, /redacted/);
  assertNoSensitiveLeak(summary.textContent);
});

test('Intent Router cannot override backend readiness to start Draft Build', async () => {
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
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    requirementMode: 'draft',
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'resource_pending:image_source', category: 'resource_pending', field: 'image_source', questionId: '', blocksBuild: true, resolutionMode: 'provide_resource', publicLabel: '图像来源待绑定' }],
      resolvedFields: ['inspection_object', 'task_type'],
      remainingFields: ['image_source'],
      primaryMessage: '图像来源待绑定',
      contractVersion: 'v2'
    }
  }));
  let started = false;
  panel._startBuildFromCurrentPlan = () => { started = true; return true; };
  panel._handleIntentRouterResult({
    intent: 'build_from_confirmed_plan',
    shouldOpenPlan: false,
    shouldBuildDirectly: true,
    canBuild: true,
    needsClarification: false,
    publicReason: 'Router thinks ready.',
    remainingPlanFields: ['image_source'],
    resolvedPlanFields: ['inspection_object', 'task_type']
  }, {
    routerRequestId: 'router-authority-check',
    turn: {},
    description: '开始 build',
    userMessage: '开始 build',
    planAnswerRevision: panel.planAnswerRevision
  });
  assert.equal(started, false);
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, false);
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
  assert.match(overview.innerHTML, /方案已就绪/);
  assert.match(overview.innerHTML, /可以构建/);
  assert.equal(inlineBuildButton.disabled, false);
  assert.ok(planActionButtons.every(button => !button.disabled));
});

test('Start Build from non-executable Plan is blocked by authoritative readiness', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': createFakeElement(),
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  panel.pendingVisionPlan = panel._normalizeBackendPlanResult(backendPlanResult({
    canBuild: true,
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'hard_requirement:inspection_object_missing', category: 'hard_requirement', field: 'inspection_object', questionId: '', blocksBuild: true, resolutionMode: 'answer_question', publicLabel: '检测对象待确认' }],
      resolvedFields: [],
      remainingFields: ['inspection_object'],
      primaryMessage: '检测对象待确认',
      contractVersion: 'v2'
    }
  }));
  panel._dispatchGenerateRequest = () => { throw new Error('must not dispatch'); };
  assert.equal(await panel._startBuildFromCurrentPlan(), false);
  assert.equal(panel.agentWorkspaceState.projection.buildAction.canStart, false);
  assert.match(panel.lastResultStatusNote.text, /检测对象待确认|暂不能构建/);
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
  assert.equal(panel.isGenerating, false);
  const planActionBeforeBuild = panel._getPlanBuildActionState(panel.pendingVisionPlan);
  assert.equal(planActionBeforeBuild.canStart, true, JSON.stringify(planActionBeforeBuild));
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

test('Build Workspace redacts unsafe operator chain and parameter mapping text', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.agentWorkspaceMode = 'build';
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i;
  const payload = buildResultContractPayload();
  payload.buildResult.selectionSource = `accepted_recommended ${unsafe}`;
  payload.buildResult.effectiveRouteId = `attribute_classification_deep_learning ${unsafe}`;
  payload.buildResult.parameterStrategy = `deep_learning_classification ${unsafe}`;
  payload.buildResult.strategyConfirmationSource = `confirmed_by_user ${unsafe}`;
  payload.buildResult.unresolvedStrategyBlockers = [`missing operator decision ${unsafe}`];
  payload.buildResult.operatorPipeline = [
    {
      tempId: `op_detect ${unsafe}`,
      operatorType: 'SurfaceDefectDetection',
      source: `template_skeleton ${unsafe}`,
      status: `selected ${unsafe}`,
      repairNote: `invalid operator was repaired ${unsafe}`
    }
  ];
  payload.buildResult.parameterMapping = [
    {
      tempId: `op_detect ${unsafe}`,
      operatorType: 'SurfaceDefectDetection',
      parameterName: `ModelPath ${unsafe}`,
      valueSummary: `<pending-model-resource> ${unsafe}`,
      source: `missing_resource ${unsafe}`,
      pending: true
    }
  ];
  panel.activeAgentRunEvents = [{
    runId: 'ar_build_contract_redacted_ops',
    sequence: 20,
    eventType: 'run.completed',
    stage: 'run',
    title: 'Run completed',
    summary: 'Build completed.',
    status: 'completed',
    payload,
    metadataOnly: true,
    redactionPass: false
  }];

  panel._renderBuildWorkspaceFromAgentRun();

  const rendered = `${elements['#ai-build-operator-chain'].innerHTML} ${elements['#ai-build-parameters'].innerHTML}`;
  assert.match(rendered, /redacted/);
  assert.doesNotMatch(rendered, unsafePattern);
});

test('Build Workspace redacts unsafe workflow diff text in final draft', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.agentWorkspaceMode = 'build';
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i;
  const payload = buildResultContractPayload();
  payload.buildResult.workflowDiff = {
    addedNodes: [`op_added ${unsafe}`],
    preservedNodes: [`existing_node ${unsafe}`],
    pendingParameters: [`op_detect.ModelPath ${unsafe}`],
    deploymentBlockers: [`op_output.PlcAddress ${unsafe}`],
    metadataOnly: true
  };
  panel.activeAgentRunEvents = [{
    runId: 'ar_build_diff_redacted',
    sequence: 20,
    eventType: 'run.completed',
    stage: 'run',
    title: 'Run completed',
    summary: 'Build completed.',
    status: 'completed',
    payload,
    metadataOnly: true,
    redactionPass: false
  }];

  panel._renderBuildWorkspaceFromAgentRun();

  const rendered = elements['#ai-build-final-draft'].innerHTML;
  assert.match(rendered, /redacted/);
  assert.doesNotMatch(rendered, unsafePattern);
});

test('Tool Evidence Timeline renders compiler-owned evidence', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.agentWorkspaceMode = 'build';
  const payload = buildResultContractPayload();
  payload.buildResult.toolEvidenceTimeline = [
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
      runId: 'ar_compiler_build',
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
  assert.match(timelineHtml, /固定构建链路/);
  assert.match(timelineHtml, /未找到匹配模板骨架，已改用算子链生成/);
});

test('Build Workspace redacts unsafe tool evidence template and readiness check text', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.agentWorkspaceMode = 'build';
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i;
  const payload = buildResultContractPayload();
  delete payload.buildResult.applyGate;
  payload.buildResult.toolEvidenceTimeline = [
    {
      stage: `template_strategy ${unsafe}`,
      toolName: `get_flow_template_skeleton ${unsafe}`,
      source: `fixed_build_orchestrator ${unsafe}`,
      status: `warning ${unsafe}`,
      warningCode: `template_not_found ${unsafe}`,
      durationMs: 6,
      outputSummary: `Template fallback used. ${unsafe}`,
      metadataOnly: true,
      redactionPass: false
    }
  ];
  panel.activeAgentRunEvents = [
    {
      runId: 'ar_tool_evidence_redacted',
      sequence: 18,
      eventType: 'readiness.checked',
      stage: 'readiness',
      title: `Readiness checked ${unsafe}`,
      summary: `Readiness blocked by missing model resource. ${unsafe}`,
      status: 'warning',
      payload: { firstFixRecommendation: `Bind resource metadata. ${unsafe}` },
      metadataOnly: true,
      redactionPass: false
    },
    {
      runId: 'ar_tool_evidence_redacted',
      sequence: 20,
      eventType: 'run.completed',
      stage: 'run',
      title: 'Run completed',
      summary: `Build completed. ${unsafe}`,
      status: 'completed',
      payload,
      metadataOnly: true,
      redactionPass: false
    }
  ];

  panel._renderBuildWorkspaceFromAgentRun();

  const rendered = [
    elements['#ai-build-event-timeline'].innerHTML,
    elements['#ai-build-template-match'].innerHTML,
    elements['#ai-build-checks'].innerHTML
  ].join('\n');
  assert.match(rendered, /redacted/);
  assert.doesNotMatch(rendered, unsafePattern);
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

test('ready_to_apply state reveals build workspace so Apply entry is visible', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = buildResultContractPayload();
  panel.currentResultVersion = 2;
  panel.appliedResultVersion = 0;
  panel.activeAgentRunId = 'agent-run-visible-apply';
  panel.agentWorkspaceMode = 'build';
  panel.workspaceViewMode = 'plan';
  elements['#ai-build-workspace'].hidden = true;
  elements['#ai-plan-workspace'].hidden = false;

  panel._setWorkbenchState('ready_to_apply');
  panel._updateApplyButtonState();

  assert.equal(panel.workbenchState, 'ready_to_apply');
  assert.equal(panel.workspaceViewMode, 'build');
  assert.equal(elements['#ai-build-workspace'].hidden, false);
  assert.equal(elements['#ai-plan-workspace'].hidden, true);
  assert.equal(elements['#ai-btn-apply'].disabled, false);
  assert.equal(elements['#ai-btn-apply'].getAttribute('aria-disabled'), 'false');
  assert.match(elements['#ai-btn-apply'].innerHTML, /应用到画布/);
});

test('Apply button remains disabled for no flow, blocked gate, generating, and applied states', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;

  panel.currentResult = { flow: { operators: [], connections: [] } };
  panel.currentResultVersion = 1;
  panel.appliedResultVersion = 0;
  panel.isGenerating = false;
  panel._updateApplyButtonState();
  assert.equal(elements['#ai-btn-apply'].disabled, true);
  assert.match(elements['#ai-btn-apply'].innerHTML, /暂无可应用方案/);

  panel.currentResult = {
    flow: { operators: [{ id: 'op_1', type: 'ImageAcquisition' }], connections: [] },
    buildResult: {
      applyGate: {
        canvasApplyReady: false,
        blocked: true,
        status: 'schema_invalid',
        firstFixRecommendation: '修复结构校验错误'
      }
    }
  };
  panel.currentResultVersion = 2;
  panel._updateApplyButtonState();
  assert.equal(elements['#ai-btn-apply'].disabled, true);
  assert.match(elements['#ai-btn-apply'].innerHTML, /当前草稿暂不可应用/);

  panel.currentResult = {
    flow: { operators: [{ id: 'op_1', type: 'ImageAcquisition' }], connections: [] },
    buildResult: { applyGate: { canvasApplyReady: true, blocked: false } }
  };
  panel.currentResultVersion = 3;
  panel.isGenerating = true;
  panel._updateApplyButtonState();
  assert.equal(elements['#ai-btn-apply'].disabled, true);

  panel.isGenerating = false;
  panel.appliedResultVersion = 3;
  panel._updateApplyButtonState();
  assert.equal(elements['#ai-btn-apply'].disabled, true);
  assert.match(elements['#ai-btn-apply'].innerHTML, /已应用到画布/);
});

test('parameter review copy makes AI review optional and removes submit audit wording', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = mixedPendingParameterReviewResult();
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

test('Build resource workspace exposes existing binding controls and redacts unsafe metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  const unsafe = 'rawPrompt=secret token=super-secret-value C:\\factory\\secret.onnx';
  const result = pendingParameterReviewResult();
  result.missingResources = [{ resourceType: 'model_resource', resourceKey: 'op_detect.ModelPath', description: unsafe }];
  panel.currentResult = result;
  panel._renderFollowupChecklist(result, result.flow);
  const html = elements['#ai-result-followups'].innerHTML;
  assertNoSensitiveLeak(html);
  assert.doesNotMatch(html, /data-missing-resource-action/);
  assert.match(html, /data-resource-action="pick_model_resource"/);
  assert.match(html, /选择模型文件/);
  assert.match(html, /模型文件选择器|当前 Plan 工作台/);
});
test('apply hint separates canvas apply from DeploymentReady gate', async () => {
  const source = [
    'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelBuildPresentation.js',
    'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  ].map(file => fs.readFileSync(path.resolve(getRepoRoot(), file), 'utf8')).join('\n');

  assert.match(source, /按钮继续服从现有 CanvasApplyReady、ApplyGate 与画布应用语义/);
  assert.match(source, /不在前端建立平行状态/);
});

test('confirmed manual parameters are written when applying to canvas', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = mixedPendingParameterReviewResult();
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
  panel._setPendingDraftConfirmedValue('op_detect', 'Threshold', 0.91, 'number', 'user_input');
  panel._handleConfirmPendingParameters(panel.currentResult, panel.currentResult.flow);

  panel._handleApplyFlow();

  const detect = panel._extractOperators(panel.appliedFlow).find(op => op.id === 'op_detect');
  assert.equal(panel._readOperatorParameterValue(detect, 'Threshold'), 0.91);
  assert.match(panel.lastResultStatusNote.text, /已应用到画布/);
  assert.equal(panel._getPayloadApplyGate(panel.currentResult).deploymentReady, false);
});

test('unconfirmed pending parameters are not silently written during canvas apply', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = mixedPendingParameterReviewResult();
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
  panel.currentResult = mixedPendingParameterReviewResult();
  panel.currentResultVersion = 10;
  panel.flowCanvas = createFakeFlowCanvas();
  panel.options.getOperators = () => resourceBindingOperatorMetadata();
  panel._showApplyPreview = (_diff, flow) => panel._executeApplyFlow(flow);
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._setupCanvasStructureSync();

  panel._handleApplyFlow();
  const editedFlow = panel.flowCanvas.serialize();
  panel._writeOperatorParameterValue(editedFlow.operators[0], 'Threshold', 0.92);
  panel.flowCanvas.replaceFlow(editedFlow, 'parameter-change');

  const entry = panel._getPendingDraftEntry('op_detect', 'Threshold');
  assert.equal(entry.confirmedValue, '0.92');
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
  panel.currentResult = mixedPendingParameterReviewResult();
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

test('canvas manual edit replay records redact unsafe metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.currentResult = mixedPendingParameterReviewResult();
  panel.options.getOperators = () => resourceBindingOperatorMetadata();
  panel.currentResult.CanvasManualEditRecords = [
    {
      ChangeKey: 'op_detect::ModelPath::C:\\factory\\secret.onnx token=abc123',
      OperatorId: 'op_detect 192.168.1.8',
      DisplayName: 'Detector C:\\factory\\secret.onnx token=abc123',
      OperatorType: 'DeepLearning apiKey=raw-key',
      ParameterName: 'ModelPath DB1.DBX0.0',
      OldValueSummary: 'C:\\models\\old.onnx',
      NewValueSummary: 'https://internal.local/model.onnx Bearer secretBearer999 data:image/png;base64,AAAA',
      ChangedAtUtc: '2026-07-07T00:00:00Z',
      Actor: 'operator token=abc123',
      SourceLabel: 'canvas replay C:\\factory\\edit.json secret=raw',
      IsPendingParameter: 'true',
      AffectsApplyGate: 'true',
      AffectsDeploymentReady: 'true',
      MetadataOnly: 'true'
    }
  ];
  panel._rebuildPendingOperatorBindings({
    pending: panel._resolvePendingParametersForDraft(panel.currentResult),
    flow: panel.currentResult.flow,
    preferIndexFallback: true
  });

  panel._renderFollowupChecklist(panel.currentResult, panel.currentResult.flow);
  const review = panel._buildPendingParameterReviewRequest();
  const combined = [
    JSON.stringify(panel._getCanvasManualEditRecords(panel.currentResult)),
    elements['#ai-result-followups'].innerHTML,
    review.hint,
    review.userMessage
  ].join('\n');

  assert.match(combined, /pendingParameter=true/);
  assert.doesNotMatch(combined, /C:\\factory/);
  assert.doesNotMatch(combined, /C:\\models/);
  assert.doesNotMatch(combined, /secret\.onnx/);
  assert.doesNotMatch(combined, /abc123/);
  assert.doesNotMatch(combined, /raw-key/);
  assert.doesNotMatch(combined, /192\.168\.1\.8/);
  assert.doesNotMatch(combined, /DB1\.DBX0\.0/);
  assert.doesNotMatch(combined, /internal\.local/);
  assert.doesNotMatch(combined, /data:image/);
  assert.doesNotMatch(combined, /edit\.json/);
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

test('AgentRun Build workspace redacts unsafe blocker and first fix text', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  panel.activeAgentRunId = 'ar_readiness_redacted';
  panel.agentWorkspaceMode = 'build';
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i;
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
    metadataOnly: true
  };
  payload.buildResult.firstFixRecommendation = `Bind metadata before applying. ${unsafe}`;
  panel.activeAgentRunEvents = [{
    runId: 'ar_readiness_redacted',
    sequence: 31,
    eventType: 'run.failed',
    stage: 'run',
    title: 'Run failed',
    summary: `Readiness blocked by missing model resource. ${unsafe}`,
    status: 'failed',
    payload,
    metadataOnly: true,
    redactionPass: false
  }];

  panel._renderBuildWorkspaceFromAgentRun();

  const rendered = `${elements['#ai-build-checks'].innerHTML} ${elements['#ai-build-final-draft'].innerHTML}`;
  assert.match(rendered, /redacted/);
  assert.doesNotMatch(rendered, unsafePattern);
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

test('Session failure projection redacts unsafe result summary and empty ops text', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { elements, container } = createBuildWorkspaceContainer();
  panel.container = container;
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i;
  const payload = buildResultContractPayload();
  delete payload.flow;
  delete payload.buildResult.flow;
  payload.kind = 'assistant_agent_failure';
  payload.status = 'failed';
  payload.success = false;
  payload.completionStatus = 'failed';
  payload.failureType = 'system_error';
  payload.failureSummary = { message: `Historical session system error. ${unsafe}`, repairTarget: `Retry the build. ${unsafe}` };
  payload.aiExplanation = `Historical session system error. ${unsafe}`;

  panel._displayResult(payload, { appendChatMessage: false });

  const rendered = `${elements['#ai-result-summary'].innerHTML} ${elements['#ai-result-ops'].innerHTML}`;
  assert.match(rendered, /redacted/);
  assert.doesNotMatch(rendered, unsafePattern);
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

test('Apply flow sanitizes draft canvas display labels before canvas handoff', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const { container } = createBuildWorkspaceContainer();
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  const draftFlow = {
    operators: [
      {
        id: 'op_detect',
        type: `SurfaceDefectDetection ${unsafe}`,
        title: `Detector title ${unsafe}`,
        displayName: `Detect scratches ${unsafe}`,
        description: `Operator description ${unsafe}`,
        inputPorts: [{ id: 'op_detect_in_image', name: `Image ${unsafe}`, displayName: `Input image ${unsafe}`, dataType: `Image ${unsafe}`, description: `Input description ${unsafe}`, direction: 0 }],
        outputPorts: [{ id: 'op_detect_out_result', name: `Result ${unsafe}`, displayName: `Output result ${unsafe}`, dataType: `Object ${unsafe}`, description: `Output description ${unsafe}`, direction: 1 }],
        parameters: [
          {
            name: 'ModelId',
            displayName: `Model resource ${unsafe}`,
            dataType: `string ${unsafe}`,
            Type: `string ${unsafe}`,
            description: `Parameter description ${unsafe}`,
            value: 'model-resource-approved',
            defaultValue: 'model-resource-approved',
            isRequired: true
          }
        ]
      }
    ],
    connections: [],
    metadataOnly: true
  };
  const payload = buildResultContractPayload();
  payload.flow = draftFlow;
  payload.Flow = draftFlow;
  payload.pendingParameters = [];
  payload.PendingParameters = [];
  payload.missingResources = [];
  payload.MissingResources = [];
  payload.buildResult.flow = draftFlow;
  payload.buildResult.pendingParameters = [];
  payload.buildResult.missingResources = [];
  payload.buildResult.workflowDiff.pendingParameters = [];
  payload.buildResult.workflowDiff.deploymentBlockers = [];
  let appliedFlow = null;

  panel.container = container;
  panel.flowCanvas = createFakeFlowCanvas({ operators: [], connections: [] });
  panel.currentResult = payload;
  panel.currentResultVersion = 4;
  panel.options.onApplied = flow => {
    appliedFlow = flow;
  };
  panel._showApplyPreview = (diff, flow) => panel._executeApplyFlow(flow);
  panel._renderFollowupChecklist = () => {};
  panel._renderParameterDraftEditor = () => {};
  panel._syncPendingParameterDrafts = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};

  panel._handleApplyFlow();

  const [operator] = panel._extractOperators(appliedFlow);
  const [inputPort] = operator.inputPorts;
  const [outputPort] = operator.outputPorts;
  const [parameter] = operator.parameters;
  const visibleCanvasText = [
    operator.name,
    operator.Name,
    operator.title,
    operator.Title,
    operator.displayName,
    operator.DisplayName,
    operator.description,
    operator.Description,
    operator.type,
    operator.Type,
    operator.operatorType,
    operator.OperatorType,
    inputPort.name,
    inputPort.Name,
    inputPort.portName,
    inputPort.PortName,
    inputPort.displayName,
    inputPort.dataType,
    inputPort.Type,
    inputPort.description,
    inputPort.Description,
    outputPort.name,
    outputPort.Name,
    outputPort.portName,
    outputPort.PortName,
    outputPort.displayName,
    outputPort.dataType,
    outputPort.Type,
    outputPort.description,
    outputPort.Description,
    parameter.displayName,
    parameter.DisplayName,
    parameter.dataType,
    parameter.DataType,
    parameter.type,
    parameter.Type,
    parameter.description,
    parameter.Description
  ].join('\n');

  assert.equal(operator.type, 'SurfaceDefectDetection');
  assert.equal(operator.Type, 'SurfaceDefectDetection');
  assert.equal(parameter.value, 'model-resource-approved');
  assert.equal(parameter.dataType, 'string');
  assert.equal(parameter.Type, 'string');
  assert.equal(inputPort.DataType, 'Image');
  assert.equal(outputPort.dataType, 'Any');
  assert.match(visibleCanvasText, /redacted/);
  assertNoSensitiveLeak(visibleCanvasText);
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

test('AgentRun assistant brief payload fallback is public and redacted', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_brief_payload';
  const unsafe = 'rawPrompt=SYSTEM chainOfThought=hidden C:\\factory\\secret.onnx 192.168.1.8 DB1.DBX0.0 plc://line1 data:image/png;base64,QUJD sk-secret-token';

  panel._handleAgentRunEvent({
    runId: 'ar_brief_payload',
    sequence: 2,
    eventType: 'assistant.brief',
    stage: 'brief',
    summary: '',
    status: 'completed',
    payload: {
      brief: `I will turn this request into a safe Vision Agent workflow draft and stream each public progress step: Detect scratches ${unsafe}`
    },
    metadataOnly: true,
    redactionPass: false
  });

  assert.match(turn.replyBody.textContent, /已创建安全的视觉智能体流程草稿任务/);
  assert.match(turn.replyBody.textContent, /Detect scratches/);
  assert.doesNotMatch(turn.replyBody.textContent, /rawPrompt=|chainOfThought|C:\\factory|secret\.onnx|192\.168\.1\.8|DB1\.DBX0\.0|plc:\/\/line1|data:image|QUJD|sk-secret-token/i);
  assert.match(turn.replyBody.textContent, /redacted|已隐藏/);
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

test('AgentRun transport and cancel failure messages redact unsafe diagnostics', async () => {
  const { AiPanel } = await loadAiPanel();
  const httpClient = (await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js')).default;
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  panel.activeAgentRunId = 'ar_transport_redacted';
  panel.isGenerating = true;

  panel._setAgentRunTransportStatus(`Transport fallback ${unsafe}`, 'warning', `Event stream failed ${unsafe}`);

  const processText = `${collectProcessText(turn)}\n${turn.processBody.innerHTML}\n${turn.statusEl.textContent}`;
  assert.match(processText, /redacted/);
  assertNoSensitiveLeak(processText);

  const originalPost = httpClient.post;
  httpClient.post = async () => {
    throw new Error(`Cancel failed ${unsafe}`);
  };
  try {
    const cancelled = await panel._cancelActiveAgentRun();
    assert.equal(cancelled, false);
  } finally {
    httpClient.post = originalPost;
  }

  const messages = (panel.messages || []).map(item => item.text).join('\n');
  assert.match(messages, /取消生成未生效/);
  assert.match(messages, /redacted/);
  assertNoSensitiveLeak(messages);
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

test('AgentRun public renderers redact unsafe event and payload text', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true, enabled: true });
  const turn = attachAgentRunTurn(panel);
  panel.activeAgentRunId = 'ar_redacted_public';
  const unsafe = 'rawPrompt=SYSTEM systemPrompt=ROOT chainOfThought=hidden C:\\factory\\secret.onnx 192.168.1.8 DB1.DBX0.0 plc://line1 data:image/png;base64,QUJD sk-secret-token Authorization: Bearer super-secret-value https://example.invalid/v1?' +
    'token=secret-token';
  const unsafePattern = /rawPrompt=|systemPrompt=|chainOfThought|C:\\factory|secret\.onnx|192\.168\.1\.8|DB1\.DBX0\.0|plc:\/\/line1|data:image|QUJD|sk-secret-token|super-secret-value|secret-token|example\.invalid/i;

  panel._handleAgentRunEvent({
    runId: 'ar_redacted_public',
    sequence: 7,
    eventType: 'tool.call.failed',
    stage: 'readiness',
    title: `Tool failed: manifest_dryrun ${unsafe}`,
    summary: `Manifest dry-run blocked. ${unsafe}`,
    status: 'failed',
    payload: {
      toolName: `manifest_dryrun ${unsafe}`,
      durationMs: 9,
      reportId: `report-unsafe ${unsafe}`,
      blockedReasons: [`missing operator contract ${unsafe}`],
      firstFixRecommendation: `Remove unsafe config ${unsafe}`
    },
    metadataOnly: true,
    redactionPass: false
  });

  const toolText = `${collectProcessText(turn)} ${turn.toolsBody.innerHTML} ${turn.failureBody.innerHTML}`;
  assert.doesNotMatch(toolText, unsafePattern);
  assert.match(toolText, /redacted|已隐藏/);

  panel._handleAgentRunEvent({
    runId: 'ar_redacted_public',
    sequence: 8,
    eventType: 'run.failed',
    stage: 'run',
    title: `Run failed ${unsafe}`,
    summary: `Build failed before completion. ${unsafe}`,
    status: 'failed',
    payload: {
      firstFixRecommendation: `Fix metadata only. ${unsafe}`,
      diagnostic: {
        firstFixRecommendation: `Nested fix should also be safe. ${unsafe}`
      }
    },
    metadataOnly: true,
    redactionPass: false
  });

  const failureText = `${turn.failureBody.innerHTML} ${panel.lastResultStatusNote?.text || ''}`;
  assert.doesNotMatch(failureText, unsafePattern);
  assert.match(failureText, /redacted|已隐藏/);
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
  const canBuildBefore = panel.agentWorkspaceState.projection.readiness.canBuild;

  assert.equal(panel._applyBuildFromPlanCanonicalState({
    planId: 'plan_stale',
    planHash: 'sha256:current',
    buildReadiness: readiness
  }), false);
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, canBuildBefore);

  assert.equal(panel._applyBuildFromPlanCanonicalState({
    planId: 'plan_current',
    planHash: 'sha256:stale',
    buildReadiness: readiness
  }), false);
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, canBuildBefore);
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

test('Vision Agent workspace source guard enforces one reducer and one answer surface', () => {
  const currentFile = fileURLToPath(import.meta.url);
  const testProjectRoot = path.resolve(path.dirname(currentFile), '..', '..');
  const productRoot = path.resolve(testProjectRoot, '..', '..');
  const aiRoot = path.resolve(productRoot, 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'ai');
  const workspaceSource = fs.readFileSync(path.resolve(aiRoot, 'aiPanelAgentWorkspace.js'), 'utf8');
  const panelSource = fs.readFileSync(path.resolve(aiRoot, 'aiPanel.js'), 'utf8');
  const chatSource = fs.readFileSync(path.resolve(aiRoot, 'aiPanelChat.js'), 'utf8');
  const resourceSource = fs.readFileSync(path.resolve(aiRoot, 'aiPanelResourceBinding.js'), 'utf8');
  const stateSource = fs.readFileSync(path.resolve(aiRoot, 'agentWorkspaceState.js'), 'utf8');

  assert.match(panelSource, /installAgentWorkspaceState/);
  assert.match(workspaceSource, /AgentWorkspaceEventTypes/);
  assert.match(stateSource, /agentWorkspaceReducer/);
  assert.match(stateSource, /SESSION_RESTORED/);
  assert.match(stateSource, /RUN_EVENT_RECEIVED/);
  assert.doesNotMatch(workspaceSource, /_assessLocalRequirementMaturity|_buildLocalIntentRouterFallback|_computeEffectivePlanBuildReadiness|_buildLegacyPlanReadinessSnapshot|_applyAnswersToAuthoritativeReadiness|_buildLegacyPlanReadinessEvidenceOnly|_projectAuthoritativeReadinessEvidenceOnly|_computePlanReadinessEvidenceOnly/);
  assert.doesNotMatch(stateSource, /LEGACY_PATCH|workspace\/legacy-patch/);
  assert.doesNotMatch(panelSource, /_clarificationSelectionDraft|_bindClarificationOptionButtons|_buildClarificationAnswerDraft/);
  assert.doesNotMatch(chatSource, /data-clarification-field|data-clarification-value|send-clarification/);
  assert.doesNotMatch(resourceSource, /开始构建后才补齐|开始构建后补齐/);
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

test('RuntimePreview UI redacts raw prompt PLC and model diagnostics', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: true });
  const { validation } = attachValidationPanel(panel);
  const response = agentResponse();
  const unsafe = 'rawPrompt=secret systemPrompt=hidden chainOfThought=private baseUrl=http://192.168.1.8/v1 plc://line1/DB1.DBX0.0 DB1.DBX0.0 model.onnx sk-secret-token';
  const unsafePattern = /rawPrompt=|systemPrompt=|chainOfThought=|baseUrl=|192\.168\.1\.8|plc:\/\/|DB1\.DBX0\.0|model\.onnx|sk-secret-token/i;
  response.validationPreview.runtimePreview = {
    previewReady: false,
    adapterName: `pilot_runtime_preview ${unsafe}`,
    previewMode: `metadata_only ${unsafe}`,
    permissionDecision: {
      allowed: false,
      reasonCode: `runtime_preview_denied ${unsafe}`,
      reason: `deny because ${unsafe}`,
      effectiveAdapterName: `offline_runtime_preview ${unsafe}`,
      pilotEnabled: true,
      metadataOnly: true
    },
    resourceTrace: {
      allowed: false,
      reasonCode: `resource_denied ${unsafe}`,
      resourceType: `model_resource ${unsafe}`,
      resourceId: `model.onnx ${unsafe}`,
      normalizedKey: `op_detect.ModelPath ${unsafe}`,
      missingResources: [{ resourceKey: `op_detect.ModelPath ${unsafe}`, description: `Missing model ${unsafe}` }]
    },
    readiness: {
      status: `not_ready ${unsafe}`,
      blockingIssues: [{ code: `blocker_${unsafe}`, message: `Blocked ${unsafe}`, operatorId: `op_detect ${unsafe}` }],
      missingResources: [{ resourceKey: `model ${unsafe}`, description: `Missing ${unsafe}` }],
      unsafeFindings: [{ message: `Unsafe ${unsafe}` }],
      pendingActions: [{ actionType: `RuntimePreviewReview ${unsafe}`, summary: `Review ${unsafe}` }],
      resourceTrace: { allowed: false, reasonCode: `resource_trace ${unsafe}`, resourceType: `model ${unsafe}` },
      allowlistCoverage: { detail: unsafe }
    },
    fallback: {
      used: true,
      fallbackAdapterName: `offline_runtime_preview ${unsafe}`,
      reason: `fallback ${unsafe}`,
      errorCode: `runtime_preview_error ${unsafe}`
    },
    warnings: [{ message: `Warning ${unsafe}` }],
    issues: [{ description: `Issue ${unsafe}` }],
    pendingActions: [{ actionType: `RuntimePreviewPilotReadinessReview ${unsafe}`, summary: `Review ${unsafe}` }],
    artifacts: [{ artifactId: `artifact ${unsafe}`, artifactType: `operator_result_metadata ${unsafe}`, metadataOnly: true, binaryIncluded: false, byteLength: 0 }]
  };

  panel._renderValidationConsole(response);

  assert.match(validation.innerHTML, /&lt;redacted&gt;/);
  assert.doesNotMatch(validation.innerHTML, unsafePattern);
});

test('validation preview redacts unsafe operator diagnostics', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false });
  const { validation } = attachValidationPanel(panel);
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=super-secret-value';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i;

  panel._renderValidationConsole({
    validationPreview: {
      structuralValidation: {
        summary: `Structure blocked ${unsafe}`,
        blockingIssues: [{ message: `Model path invalid ${unsafe}`, code: `code_${unsafe}`, operatorId: `op_${unsafe}` }],
        warnings: [{ description: `Warning detail ${unsafe}` }]
      },
      dryRun: {
        dryRunSucceeded: false,
        dryRunSummary: `Dry run failed ${unsafe}`,
        missingResources: [{ resourceKey: `resource_${unsafe}`, description: `Missing resource ${unsafe}` }]
      }
    },
    missingResources: [{ resourceType: `model_${unsafe}`, parameterName: `ModelPath ${unsafe}`, description: `Upload model ${unsafe}` }],
    pendingActions: [{ actionType: 'ProvideModelPath', summary: `Provide resource ${unsafe}` }],
    toolTrace: [{
      toolName: `validate_flow ${unsafe}`,
      permission: `allowed ${unsafe}`,
      adapterName: `offline_adapter ${unsafe}`,
      status: `failed ${unsafe}`,
      errorCode: `ERR_MODEL ${unsafe}`,
      permissionDecision: { reasonCode: `policy_reason ${unsafe}` },
      durationMs: 11
    }],
    manualRetry: { required: true, summary: `Retry summary ${unsafe}`, stage: `stage_${unsafe}` },
    lastAttemptDiagnostics: [{
      issues: [{
        severity: 'error',
        category: `category_${unsafe}`,
        message: `Issue message ${unsafe}`,
        repairHint: `Repair hint ${unsafe}`,
        operatorId: `operator_${unsafe}`
      }]
    }],
    knowledgeDiagnostics: [{
      severity: 'warning',
      code: `knowledge_${unsafe}`,
      message: `Knowledge message ${unsafe}`,
      repairHint: `Knowledge repair ${unsafe}`,
      operatorId: `kg_${unsafe}`
    }]
  });

  assert.match(validation.innerHTML, /redacted/);
  assert.doesNotMatch(validation.innerHTML, unsafePattern);
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

test('missingResources partition generated pending parameters into the resource side only', async () => {
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
  const partition = panel._getPendingParameterPartition(response);

  assert.equal(pending[0].operatorId, 'op_detect');
  assert.equal(pending[0].parameterNames[0], 'ModelPath');
  assert.equal(partition.ordinaryPendingParameters.length, 0);
  assert.equal(partition.resourceBackedPendingParameters[0].parameterNames[0], 'ModelPath');
  assert.equal(partition.resourceBackedFieldCount, 1);
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

test('Build result resource cards reuse the existing binding handler without automatic deployment', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { options: { getOperators: resourceBindingOperatorMetadata } });
  const followups = createFakeElement();
  panel.container = createContainer({ '#ai-result-followups': followups });
  const response = resourceBindingResponse();
  panel._renderFollowupChecklist(response, response.flow);
  assert.doesNotMatch(followups.innerHTML, /data-missing-resource-action/);
  assert.match(followups.innerHTML, /待绑定资源/);
  assert.match(followups.innerHTML, /data-resource-action="pick_model_resource"/);
  assert.match(followups.innerHTML, /data-resource-action="switch_to_draft"/);
  assert.doesNotMatch(followups.innerHTML, /自动绑定|自动选择|自动部署/);
});
test('Unified resource clarification redacts unsafe metadata and exposes the canonical resolution action', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true });
  const planWorkspace = createFakeElement();
  panel.container = createContainer({
    '#ai-agent-workspace-overview': createFakeElement(),
    '#ai-plan-workspace': planWorkspace,
    '#ai-build-workspace': createFakeElement(),
    '#ai-result-status-note': createFakeElement()
  });
  const unsafe = 'rawPrompt=secret token=super-secret-value C:\\factory\\secret.onnx';
  const plan = panel._normalizeBackendPlanResult(backendPlanResult({
    missingResources: [{ resourceKey: 'model:detector', resourceType: 'model_resource', parameterName: 'ModelPath', description: unsafe }],
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'resource_pending:model:detector', category: 'resource_pending', field: 'model_resource', questionId: '', blocksBuild: true, resolutionMode: 'provide_resource', publicLabel: '模型资源待绑定' }],
      resolvedFields: [],
      remainingFields: ['model_resource'],
      primaryMessage: '模型资源待绑定',
      contractVersion: 'v2'
    }
  }));
  panel.pendingVisionPlan = plan;
  panel._renderPlanWorkspace(plan);
  assertNoSensitiveLeak(planWorkspace.innerHTML);
  assert.equal((planWorkspace.innerHTML.match(/data-ai-hook="clarification-resources"/g) || []).length, 1);
  assert.match(planWorkspace.innerHTML, /data-resource-action="pick_model_resource"/);
  assert.match(planWorkspace.innerHTML, /资源|影响算子|影响参数|阻断范围|解决位置/);
  assert.doesNotMatch(planWorkspace.innerHTML, /type="file"/);
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
  const draftKey = panel._getPendingResourceDraftKey(modelResource);
  const updated = panel._handleMissingResourceAction(modelResource, 'pick_model_resource', {
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
  assert.equal(panel.pendingResourceDrafts[draftKey].metadataOnly, true);
  assert.equal(panel.pendingResourceDrafts[draftKey].confirmedBy, 'local-user');
  assert.equal(response.manualResourceConfirmations.length, 1);
  assert.equal(response.manualResourceConfirmations[0].metadataOnly, true);
  assert.match(response.manualResourceConfirmations[0].actionLabel, /选择模型文件/);
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
  const draftKey = panel._getPendingResourceDraftKey(resource);
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
  assert.equal(panel.pendingResourceDrafts[draftKey].value, 'model-resource:pascal-v1');
  assert.equal(panel.pendingResourceDrafts[draftKey].actualOperatorId, 'op_detect');
  assert.equal(response.ManualResourceConfirmations.length, 1);
  assert.equal(response.ManualResourceConfirmations[0].metadataOnly, true);
});

test('resolving in-workbench resources keeps settings-owned output and PLC blockers authoritative', async () => {
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

  assert.deepEqual(response.missingResources.map(item => item.resourceType), ['output_channel', 'plc_address']);
  assert.deepEqual(response.pendingParameters, []);
  assert.equal(response.applyGate.deploymentReady, false);
  assert.equal(response.applyGate.status, 'canvas_apply_ready');
  assert.equal(response.validationPreview.deploymentPrecheck.readyForDeployment, false);
  assert.equal(response.validationPreview.deploymentPrecheck.deploymentBlocked, true);
  assert.equal(response.manualResourceConfirmations.length, resources.length - 2);
  const output = response.flow.operators.find(op => op.tempId === 'op_output');
  assert.equal(Object.prototype.hasOwnProperty.call(output.parameters, 'PlcAddress'), false);
  assert.equal(Object.prototype.hasOwnProperty.call(output.parameters, 'OutputChannel'), false);
  assert.doesNotMatch(JSON.stringify(response), /C:\\|D:\\|\.onnx|192\.168\.|DB1\.DBX|base64|data:image|sk-secret|super-secret-value/i);
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
  assert.equal(Object.values(panel.pendingResourceDrafts).some(item => item.resourceType === 'output_channel'), false);
  assert.match(panel.lastResultStatusNote.text, /不能在当前模式下暂缓/);
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

test('manual confirmation records redact unsafe replay metadata', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const followups = createFakeElement();
  panel.container = createContainer({
    '#ai-result-followups': followups
  });
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token';
  const unsafePattern = /rawPrompt=|systemPrompt=|super-secret-value|192\.168\.1\.8|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i;
  const response = {
    flow: { operators: [], connections: [], metadataOnly: true },
    pendingParameters: [],
    missingResources: [],
    manualResourceConfirmations: [
      {
        confirmedAtUtc: `2026-06-09T00:00:00Z ${unsafe}`,
        actor: `local-user ${unsafe}`,
        resourceType: `model_resource ${unsafe}`,
        affectedOperator: `op_detect ${unsafe}`,
        affectedParameters: [`ModelPath ${unsafe}`],
        resourceKey: `op_detect.ModelPath ${unsafe}`,
        writebackSummary: `model-resource:scratch-v1 ${unsafe}`,
        metadataOnly: true,
        applyGateChange: {
          from: `canvas_apply_ready ${unsafe}`,
          to: `deployment_ready ${unsafe}`,
          clearedBlockers: [`op_detect.ModelPath ${unsafe}`]
        },
        deploymentBlocked: false
      }
    ]
  };

  panel._renderFollowupChecklist(response, response.flow);

  assert.match(followups.innerHTML, /redacted/);
  assert.doesNotMatch(followups.innerHTML, unsafePattern);
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
  const draftKey = panel._getPendingResourceDraftKey(templateResource);

  panel._handleMissingResourceAction(templateResource, 'pick_template_resource', {
    value: 'template-artifact:fixture-a',
    data: response,
    flow: response.flow
  });

  const templateOperator = response.flow.operators.find(op => op.tempId === 'op_match');
  assert.equal(response.missingResources.some(item => item.resourceKey === 'op_match.Template'), false);
  assert.equal(Object.prototype.hasOwnProperty.call(templateOperator.parameters, 'TemplatePath'), false);
  assert.equal(Object.prototype.hasOwnProperty.call(templateOperator.parameters, 'Template'), false);
  assert.equal(panel.pendingResourceDrafts[draftKey].value, 'template-artifact:fixture-a');
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

  assert.deepEqual(names.sort(), ['CameraId', 'SourceType'].sort());
  assert.equal(names.includes('FilePath'), false);
  assert.equal(names.includes('CameraBindingId'), false);
});

test('parameter rules keep an explicitly present canonical default over a conflicting camera alias', async () => {
  const { getOperatorParameterValue } = await loadParameterRules();
  const operator = {
    parameterConstraints: [
      { parameter: 'CameraId' },
      { parameter: 'CameraBindingId', aliasFor: 'CameraId', deprecated: true }
    ],
    parameters: [
      { name: 'CameraId', value: '', defaultValue: '' },
      { name: 'CameraBindingId', value: 'legacy-camera' }
    ]
  };

  assert.equal(getOperatorParameterValue(operator, 'CameraId'), '');
  assert.equal(getOperatorParameterValue(operator, 'CameraBindingId'), '');
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
  const modelById = withCanonicalConstraints({
    type: 'DeepLearning',
    parameters: {
      ModelId: 'wire-sequence-model',
      UseGpu: false,
      OutputFormat: 'EndToEndNms',
      EnableInternalNms: true
    }
  });
  const missingModel = withCanonicalConstraints({
    type: 'DeepLearning',
    parameters: [
      { name: 'ModelPath', value: '' },
      { name: 'ModelId', value: '' },
      { name: 'ModelCatalogPath', value: '' }
    ]
  });

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

test('parameter rules cover EdgeDetection Canny and OnnxEdge model source dependencies', async () => {
  const {
    collectEffectiveRequiredParameterErrors,
    getParameterEffectiveState,
    shouldIncludePendingParameter
  } = await loadParameterRules();
  const cannyParameters = [
    { name: 'Method', value: 'Canny' },
    { name: 'EdgeModelPath', value: '', isRequired: true },
    { name: 'EdgeModelId', value: '', isRequired: true },
    { name: 'ModelCatalogPath', value: '', isRequired: true },
    { name: 'EdgeBinarizationThreshold', value: 0.5, isRequired: true }
  ];
  const canny = withCanonicalConstraints({
    type: 'EdgeDetection',
    parameters: cannyParameters
  });
  const missingOnnxModel = withCanonicalConstraints({
    type: 'EdgeDetection',
    parameters: [
      { name: 'Method', value: 'OnnxEdge' },
      { name: 'EdgeModelPath', value: '', isRequired: false },
      { name: 'EdgeModelId', value: '', isRequired: false },
      { name: 'ModelCatalogPath', value: '', isRequired: false },
      { name: 'EdgeBinarizationThreshold', value: 0.5, isRequired: false }
    ]
  });
  const onnxModelById = withCanonicalConstraints({
    type: 'EdgeDetection',
    parameters: {
      Method: 'OnnxEdge',
      EdgeModelPath: '',
      EdgeModelId: 'edge-catalog-model',
      ModelCatalogPath: '',
      EdgeBinarizationThreshold: 0.5
    }
  });

  for (const name of ['EdgeModelPath', 'EdgeModelId', 'ModelCatalogPath', 'EdgeBinarizationThreshold']) {
    const parameter = cannyParameters.find(item => item.name === name);
    assert.equal(getParameterEffectiveState(canny, parameter).effectiveRequired, false, `${name} Canny required`);
  }
  assert.equal(getParameterEffectiveState(canny, 'EdgeModelPath').effectiveDisabled, true);
  assert.equal(shouldIncludePendingParameter(canny, 'EdgeModelPath'), false);

  const missingErrors = collectEffectiveRequiredParameterErrors(missingOnnxModel, missingOnnxModel.parameters);
  assert.equal(
    missingErrors.some(error =>
      error.kind === 'atLeastOneOf' &&
      error.parameterNames.includes('EdgeModelPath')),
    true
  );
  assert.deepEqual(
    collectEffectiveRequiredParameterErrors(onnxModelById, missingOnnxModel.parameters),
    []
  );
  assert.equal(getParameterEffectiveState(onnxModelById, 'EdgeModelPath').effectiveDisabled, true);
});

test('parameter rules keep ResultOutput constrained to its real SaveToFile parameter', async () => {
  const {
    collectEffectiveRequiredParameterErrors,
    getParameterEffectiveState,
    getOperatorParameterRule
  } = await loadParameterRules();
  const output = withCanonicalConstraints({
    type: 'ResultOutput',
    parameters: [
      { name: 'SaveToFile', value: false }
    ]
  });

  assert.deepEqual(collectEffectiveRequiredParameterErrors(output, output.parameters), []);
  assert.equal(getParameterEffectiveState(output, 'SaveToFile').effectiveRequired, false);
  assert.equal(getParameterEffectiveState(output, 'SaveToFile').effectiveDisabled, false);
  assert.equal(getOperatorParameterRule(output, 'SaveToFile').resourceKind, 'output_file');
  assert.equal(getOperatorParameterRule(output, 'Channel'), null);
});

test('shared parameter rule parity spec matches frontend effective states', async () => {
  const { getParameterEffectiveState } = await loadParameterRules();
  const spec = loadParameterRuleParitySpec();

  for (const parityCase of spec.cases) {
    const operator = {
      type: parityCase.operatorType,
      operatorType: parityCase.operatorType,
      parameters: parityCase.parameters,
      parameterConstraints: spec.operatorConstraints?.[parityCase.operatorType] || []
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
  const operator = withCanonicalConstraints({
    id: 'dl',
    type: 'DeepLearning',
    parameters: [
      { name: 'ModelPath', value: '' },
      { name: 'ModelId', value: '' },
      { name: 'ModelCatalogPath', value: '' }
    ]
  });

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
  const styleDir = path.resolve(productRoot, 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'shared', 'styles');
  const source = fs.readFileSync(sourcePath, 'utf8');
  const shellCss = fs.readFileSync(path.resolve(styleDir, 'ai-shell.css'), 'utf8');
  const conversationCss = fs.readFileSync(path.resolve(styleDir, 'ai-conversation.css'), 'utf8');
  const responsiveCss = fs.readFileSync(path.resolve(styleDir, 'ai-responsive.css'), 'utf8');

  assert.match(source, /data-ai-workbench-pane="true"/);
  assert.match(source, /data-ai-chat-pane="true"/);
  assert.match(source, /aiPanelWorkbenchMixin/);
  assert.match(source, /aiPanelPendingParametersMixin/);
  assert.match(source, /aiPanelChatMixin/);
  assert.match(source, /aiPanelValidationPreviewMixin/);
  assert.ok(source.split(/\r?\n/).length < 2500);
  assert.match(source, /focus\(\{\s*preventScroll:\s*true\s*\}\)/);
  assert.match(shellCss, /--ai-surface-page:\s*var\(--theme-surface-0\)/);
  assert.match(shellCss, /\.ai-workspace\s*{[^}]*clamp\(360px,\s*21vw,\s*400px\)/s);
  assert.match(shellCss, /\.ai-pane-right\s*{[^}]*grid-column:\s*1;/s);
  assert.match(shellCss, /\.ai-pane-left\s*{[^}]*grid-column:\s*2;/s);
  assert.match(conversationCss, /\.ai-view-container\s+\.ai-chat-container\s*{[^}]*background-image:\s*none/s);
  assert.match(responsiveCss, /@media \(max-width:\s*1179px\)[\s\S]*data-ai-active-pane="workbench"[\s\S]*data-ai-active-pane="conversation"/);
  assert.match(responsiveCss, /@media \(max-width:\s*899px\)/);
  assert.doesNotMatch(`${shellCss}\n${conversationCss}\n${responsiveCss}`, /#[0-9a-f]{3,8}|rgba?\(/i);
  assert.doesNotMatch(`${shellCss}\n${conversationCss}\n${responsiveCss}`, /!important/);
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

  const shellSource = fs.readFileSync(path.resolve(aiSourceDir, 'aiPanelShellPresentation.js'), 'utf8');
  assert.match(shellSource, /export function installAiPanelShellPresentation/);
  assert.match(mainSource, /installAiPanelShellPresentation\(AiPanel\.prototype\)/);

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

test('quality suite tracks raised UI contract minimum', () => {
  const suitePath = path.resolve(getRepoRoot(), 'quality', 'evals', 'suites', 'agent_engineering_harness_suite.json');
  const suite = JSON.parse(fs.readFileSync(suitePath, 'utf8'));
  const uiEntry = suite.stages
    .flatMap(stage => stage.entries)
    .find(entry => entry.id === 'vision_agent_ui_contract_tests');

  assert.ok(uiEntry);
  assert.equal(uiEntry.minimumTests, 190);

  assert.equal(suite.stages.flatMap(stage => stage.entries)
    .some(entry => String(entry.id || '').includes('planner_shadow')), false);
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

test('AI session history list redacts unsafe persisted summaries', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const list = createFakeElement();
  panel.container = createContainer({ '#ai-history-list': list });
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';
  panel.initialAutoRestoreSessionId = '';

  panel._handleListAiSessionsResult({
    success: true,
    sessions: [
      {
        sessionId: 'session-redacted',
        lastMessage: `Build failed ${unsafe}`,
        templateName: `scratch-template ${unsafe}`,
        generationMode: `agent ${unsafe}`,
        updatedAtUtc: '2026-01-01T00:00:00Z',
        turnCount: 2,
        applied: true
      }
    ]
  });

  assert.match(list.innerHTML, /redacted/);
  assert.match(list.innerHTML, /class="ai-history-select"[^>]*type="button"/);
  assert.match(list.innerHTML, /class="ai-history-delete"[^>]*type="button"[^>]*aria-label=/);
  assertNoSensitiveLeak(`${list.innerHTML}\n${panel.history[0].lastMessage}\n${panel.history[0].templateName}\n${panel.history[0].generationMode}`);
  assert.doesNotMatch(list.innerHTML, /super-secret-value|raw-key|rawPrompt=|systemPrompt=|baseUrl=|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i);

  panel.history = [{
    sessionId: 'legacy-session',
    lastMessage: `Legacy summary ${unsafe}`,
    templateName: `Legacy template ${unsafe}`,
    generationMode: `Legacy mode ${unsafe}`,
    updatedAtUtc: '2026-01-02T00:00:00Z',
    turnCount: 1
  }];
  panel._filterHistory('');

  assert.match(list.innerHTML, /redacted/);
  assertNoSensitiveLeak(list.innerHTML);
  assert.doesNotMatch(list.innerHTML, /super-secret-value|raw-key|rawPrompt=|systemPrompt=|baseUrl=|C:\\factory|secret\.onnx|DB1\.DBX0\.0|data:image|QUJD|sk-secret-token/i);

  panel._filterHistory('super-secret-value');
  assert.equal(panel.filteredHistory.length, 0);
});

test('AI session history errors redact unsafe backend diagnostics before chat messages', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const unsafe = 'rawPrompt=secret systemPrompt=hidden token=super-secret-value baseUrl=http://192.168.1.8/v1 C:\\factory\\secret.onnx DB1.DBX0.0 data:image/png;base64,QUJD sk-secret-token apiKey=raw-key';

  panel._handleListAiSessionsResult({ success: false, errorMessage: `List failed ${unsafe}` });
  panel.pendingSessionLoad = { sessionId: 'auto-session', source: 'auto_restore', epoch: 1, requestId: 'req-auto' };
  panel._handleGetAiSessionResult({
    success: false,
    sessionId: 'auto-session',
    requestId: 'req-auto',
    navigationEpoch: 1,
    errorMessage: `Auto restore failed ${unsafe}`
  });
  panel.pendingSessionLoad = { sessionId: 'manual-session', source: 'history_switch', epoch: 2, requestId: 'req-manual' };
  panel._handleGetAiSessionResult({
    success: false,
    sessionId: 'manual-session',
    requestId: 'req-manual',
    navigationEpoch: 2,
    errorMessage: `Manual restore failed ${unsafe}`
  });
  panel._handleDeleteAiSessionResult({ success: false, errorMessage: `Delete failed ${unsafe}` });

  const combined = (panel.messages || []).map(item => item.text).join('\n');
  assert.match(combined, /redacted/);
  assert.match(combined, /历史加载失败|自动恢复上次会话失败|会话恢复失败|删除会话失败/);
  assertNoSensitiveLeak(combined);
});

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
        schemaVersion: 2,
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

test('AI history switch always lets the last user selection win despite reversed flush completion', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel._disposed = false;
  panel.sessionSelectionGeneration = 0;
  const pendingFlushes = [];
  panel._flushWorkspaceSnapshotBeforeBoundary = () => new Promise(resolve => pendingFlushes.push(resolve));
  const requests = [];
  panel._requestSessionLoad = sessionId => requests.push(sessionId);

  const selectA = panel._switchToSession('session-a');
  const selectB = panel._switchToSession('session-b');
  pendingFlushes[1](true);
  await selectB;
  pendingFlushes[0](true);
  await selectA;

  assert.deepEqual(requests, ['session-b']);
  assert.equal(panel.sessionNavigationEpoch, 2);
});

test('AI session mismatch and send exceptions always finish pending load and timeout ownership', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  panel._disposed = false;
  panel.container = createContainer({ '#ai-chat-container': createFakeElement() });
  panel.pendingSessionLoad = { sessionId: 'session-a', requestId: 'req-a', epoch: 3, timeoutId: setTimeout(() => {}, 60_000) };
  panel._handleGetAiSessionResult({
    success: true,
    sessionId: 'session-a',
    requestId: 'req-a',
    navigationEpoch: 3,
    session: { sessionId: 'session-other', history: [] }
  });
  assert.equal(panel.pendingSessionLoad, null);
  assert.match(panel.messages.at(-1).text, /会话 ID 与请求不一致/);

  panel._sendGetAiSession = () => { throw new Error('bridge unavailable'); };
  panel._requestSessionLoad('session-b', 'history_switch');
  assert.equal(panel.pendingSessionLoad, null);
  assert.match(panel.lastResultStatusNote.text, /请求发送失败/);
});

test('restored Applied snapshot strips Ready authority and blocks direct reapply', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const chat = createFakeElement();
  const applyButton = createFakeElement('button');
  panel.container = createContainer({ '#ai-chat-container': chat, '#ai-btn-apply': applyButton });
  panel._displayResult = result => { panel.currentResult = result; };
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderPlanWorkspace = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._updatePlanBuildActionState = () => {};
  panel._restoreWorkspaceRunReplays = () => Promise.resolve(true);
  panel._disposed = false;
  panel._lifecycleEpoch = 1;
  panel.sessionNavigationEpoch = 4;
  panel.pendingSessionLoad = { sessionId: 'session-applied', requestId: 'req-applied', epoch: 4 };
  const readyGate = { canvasApplyReady: true, blocked: false, status: 'ready' };
  const flow = { operators: [{ id: 'op-1', type: 'ImageAcquisition' }], connections: [] };

  panel._handleGetAiSessionResult({
    success: true,
    sessionId: 'session-applied',
    requestId: 'req-applied',
    navigationEpoch: 4,
    session: {
      sessionId: 'session-applied',
      currentCanvasFlowJson: JSON.stringify(flow),
      history: [{
        role: 'assistant',
        message: '已应用',
        payload: { status: 'applied', success: true, flow, applyGate: readyGate, buildResult: { applyGate: readyGate } }
      }],
      workspaceSnapshot: {
        schemaVersion: 2,
        revision: 9,
        lifecycleState: 'applied',
        pendingPlanSnapshot: {
          planId: 'plan-applied',
          planHash: 'sha256:applied',
          canBuild: true,
          buildReadiness: { canBuild: true, contractVersion: 'v2', blockers: [] }
        },
        readinessPreview: { canBuild: true },
        buildRunId: 'build-applied',
        buildRunStatus: 'completed',
        submittedBuildFingerprint: 'sha256:build-applied',
        workspaceViewMode: 'build'
      }
    }
  });

  assert.equal(panel.pendingSessionLoad, null);
  assert.equal(panel.currentResult.applyGate, null);
  assert.equal(panel.currentResult.buildResult.applyGate, null);
  assert.equal(panel.agentWorkspaceState.readiness.canBuild, false);
  assert.equal(panel.agentWorkspaceState.readinessPreview, null);
  assert.equal(panel.agentWorkspaceState.run.build.runId, '');
  assert.equal(panel.workspaceBuildRunId, '');
  assert.equal(panel.workspaceSubmittedBuildFingerprint, '');
  assert.equal(panel._applySafetyBlockReason, 'restored_applied_requires_revalidation');
  assert.equal(panel.workbenchState, 'failed');
  assert.equal(applyButton.disabled, true);
});

test('full session restore preserves rollback safety only for the same session and result', async () => {
  installDom();
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel);
  const applyButton = createFakeElement('button');
  panel.container = createContainer({
    '#ai-chat-container': createFakeElement(),
    '#ai-btn-apply': applyButton,
    '#ai-result-status-note': createFakeElement()
  });
  panel._displayResult = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  panel._renderPlanWorkspace = () => {};
  panel._renderBuildWorkspaceFromAgentRun = () => {};
  panel._updatePlanBuildActionState = () => {};
  panel._restoreWorkspaceRunReplays = () => Promise.resolve(true);
  panel._disposed = false;
  panel._lifecycleEpoch = 1;

  const readyGate = { canvasApplyReady: true, blocked: false, status: 'ready' };
  const flowA = { operators: [{ id: 'op-a', type: 'ImageAcquisition' }], connections: [] };
  const resultA = {
    success: true,
    completionStatus: 'completed',
    interactionState: 'completed',
    flow: flowA,
    applyGate: readyGate,
    buildResult: { buildId: 'build-a', applyGate: readyGate }
  };
  const flowB = { operators: [{ id: 'op-b', type: 'ResultOutput' }], connections: [] };
  const resultB = {
    ...resultA,
    flow: flowB,
    buildResult: { buildId: 'build-b', applyGate: readyGate }
  };
  let epoch = 0;
  const restore = (sessionId, result) => {
    epoch += 1;
    panel.sessionNavigationEpoch = epoch;
    panel.pendingSessionLoad = { sessionId, requestId: `restore-${epoch}`, epoch };
    panel._handleGetAiSessionResult({
      success: true,
      sessionId,
      requestId: `restore-${epoch}`,
      navigationEpoch: epoch,
      session: {
        sessionId,
        currentCanvasFlowJson: JSON.stringify(result.flow),
        history: [{ role: 'assistant', message: 'ready', payload: result }],
        workspaceSnapshot: {
          schemaVersion: 2,
          revision: epoch,
          lifecycleState: 'build',
          pendingPlanSnapshot: { planId: `plan-${sessionId}`, planHash: `sha256:${sessionId}`, canBuild: true },
          workspaceViewMode: 'build'
        }
      }
    });
  };

  panel.sessionId = 'session-a';
  panel._setCurrentResult(resultA);
  assert.equal(panel._persistApplySafetyBlock('apply_rollback_failed', resultA), true);

  restore('session-b', resultB);
  assert.equal(panel._applySafetyBlockReason, '');
  assert.equal(panel._getApplySafetyStorageKey(), 'cv_ai_apply_safety_block_v1:session-b');

  restore('session-a', resultA);
  assert.equal(panel._applySafetyBlockReason, 'apply_rollback_failed');
  assert.equal(panel.workbenchState, 'failed');
  assert.equal(applyButton.disabled, true);
  assert.match(applyButton.innerHTML, /需安全恢复后才能应用/);

  const replacementA = {
    ...resultA,
    flow: { operators: [{ id: 'op-a-new', type: 'ImageAcquisition' }], connections: [] },
    buildResult: { buildId: 'build-a-new', applyGate: readyGate }
  };
  panel._setCurrentResult(replacementA);
  assert.equal(panel._applySafetyBlockReason, '');
  assert.equal(panel._readPersistedApplySafetyBlock(), null);

  panel._setCurrentResult(resultA);
  panel._persistApplySafetyBlock('apply_rollback_failed', resultA);
  restore('session-a', replacementA);
  assert.equal(panel._applySafetyBlockReason, '');
  assert.equal(panel._readPersistedApplySafetyBlock(), null);

  panel.sessionId = 'session-a';
  panel._setCurrentResult(resultA);
  panel._persistApplySafetyBlock('apply_rollback_failed', resultA);
  restore('session-b', resultA);
  assert.equal(panel._applySafetyBlockReason, '');
  panel.sessionId = 'session-a';
  assert.equal(panel._restorePersistedApplySafetyBlock(resultA), 'apply_rollback_failed');
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
    schemaVersion: 2,
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
