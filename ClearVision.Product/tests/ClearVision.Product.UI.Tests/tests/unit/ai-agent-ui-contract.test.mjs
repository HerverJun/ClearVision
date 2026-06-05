import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

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
  assert.ok(report.summary.caseCount >= 30 && report.summary.caseCount <= 50);
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

test('quality suite tracks raised UI contract minimum', () => {
  const suitePath = path.resolve(getRepoRoot(), 'quality', 'evals', 'suites', 'agent_engineering_harness_suite.json');
  const suite = JSON.parse(fs.readFileSync(suitePath, 'utf8'));
  const uiEntry = suite.stages
    .flatMap(stage => stage.entries)
    .find(entry => entry.id === 'vision_agent_ui_contract_tests');

  assert.ok(uiEntry);
  assert.equal(uiEntry.minimumTests, 34);
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
