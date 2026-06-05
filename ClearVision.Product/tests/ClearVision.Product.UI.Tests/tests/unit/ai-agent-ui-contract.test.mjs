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
  panel.cameraBindingsCache = [];
  panel.currentResultVersion = 1;
  panel.sessionId = 'agent-ui-contract';
  panel.currentResult = overrides.currentResult || null;
  panel.isGenerating = false;
  panel.flowCanvas = null;
  return panel;
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

test('source guard: Agent UI has no RuntimePreview hardware network or process tool entry', () => {
  const currentFile = fileURLToPath(import.meta.url);
  const testProjectRoot = path.resolve(path.dirname(currentFile), '..', '..');
  const productRoot = path.resolve(testProjectRoot, '..', '..');
  const sourcePath = path.resolve(productRoot, 'src', 'ClearVision.Product.Desktop', 'wwwroot', 'src', 'features', 'ai', 'aiPanel.js');
  const source = fs.readFileSync(sourcePath, 'utf8');
  const developerControlSection = source.slice(
    source.indexOf('_isAgentDeveloperControlsEnabled'),
    source.indexOf('_normalizeRequirementMode')
  );
  const artifactSection = source.slice(
    source.indexOf('_renderAgentValidationArtifacts'),
    source.indexOf('_renderValidationConsole')
  );
  const guardedSource = `${developerControlSection}\n${artifactSection}`;

  assert.doesNotMatch(guardedSource, /capture_test_frame|replay_flow_with_frame|runtime_package_precheck/i);
  assert.doesNotMatch(guardedSource, /AcquireSingleFrameAsync|EnumerateCamerasAsync|GetOrCreateByBindingAsync|fetch\(|XMLHttpRequest|child_process|process\.|powershell|cmd\.exe|execute_command/i);
});
