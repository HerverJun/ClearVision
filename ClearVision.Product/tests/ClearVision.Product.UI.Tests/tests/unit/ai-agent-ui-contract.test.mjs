import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

function installDom({ search = '', localValues = {} } = {}) {
  const store = new Map(Object.entries(localValues));
  global.window = {
    chrome: null,
    location: { search },
    __CLEARVISION_AGENT_DEV_UI__: false,
    confirm() {
      return true;
    }
  };
  global.localStorage = {
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
  const escapeHtml = value => String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');

  return {
    hidden: false,
    disabled: false,
    checked: false,
    value: '',
    get innerHTML() {
      return html || escapeHtml(text);
    },
    set innerHTML(value) {
      html = String(value ?? '');
      text = '';
    },
    get textContent() {
      return text;
    },
    set textContent(value) {
      text = String(value ?? '');
      html = '';
    },
    className: '',
    dataset: {},
    classList: new FakeClassList(),
    style: {},
    attributes: new Map(),
    setAttribute(name, value) {
      this.attributes.set(name, String(value));
    },
    getAttribute(name) {
      return this.attributes.get(name);
    },
    addEventListener() {},
    querySelector() { return null; },
    querySelectorAll() { return []; }
  };
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

test('default UI payload does not include useVisionAgentGenerateFlow', async () => {
  const { AiPanel } = await loadAiPanel();
  const panel = createPanel(AiPanel, { developer: false, enabled: true, mode: 'planner' });

  assert.deepEqual(panel._buildAgentGenerateFlowRequestPayload(), {});
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
  assert.match(validation.innerHTML, /validate_flow:Simulation:--:ok:12ms/);
  assert.match(validation.innerHTML, /replay_flow_with_frame:RuntimePreview:offline_runtime_preview:ok:7ms/);
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
            { name: 'FilePath', dataType: 'file', displayName: '文件路径' }
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

  assert.match(editor.innerHTML, /图像采集（ImageAcquisition）/);
});

test('PropertyPanel header displays Chinese operator type first', async () => {
  const { PropertyPanel } = await loadPropertyPanel();
  const panel = createPropertyPanel(PropertyPanel, imageAcquisitionOperator('File'));

  panel.render();

  assert.match(panel.container.innerHTML, /图像采集（ImageAcquisition）/);
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
    createInput({ name: 'CameraId', value: 'cam-1', dataType: 'string' })
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
    createInput({ name: 'CameraId', value: '', dataType: 'string' })
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
    '进行中',
    '当前计划',
    'VisionAgent_RuntimePreview_Pilot_Gate.md'
  );
  const doc = fs.readFileSync(gatePath, 'utf8');

  assert.match(doc, /fixed shadow/i);
  assert.match(doc, /holdout shadow/i);
  assert.match(doc, /permission negative/i);
  assert.match(doc, /model config regression/i);
  assert.match(doc, /default closed|默认关闭/);
  assert.match(doc, /resource allowlist/i);
  assert.match(doc, /不得返回图片 bytes\/base64|no image bytes\/base64/i);
  assert.match(doc, /不得写 PLC|no PLC write/i);
  assert.match(doc, /不得打包、下发、热加载|no package, deploy, or hot-load/i);
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
