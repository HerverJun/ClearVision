import test from 'node:test';
import assert from 'node:assert/strict';

function installDom() {
  const escapeHtml = value => String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');

  global.window = {
    chrome: null,
    mockWebViewResponse: null,
    confirm() {
      return true;
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
      let text = '';
      return {
        get textContent() { return text; },
        set textContent(value) { text = String(value ?? ''); },
        get innerHTML() { return escapeHtml(text); },
        set innerHTML(value) { text = String(value ?? ''); },
        addEventListener() {},
        appendChild() {},
        querySelector() { return null; },
        querySelectorAll() { return []; },
        classList: { add() {}, remove() {}, toggle() {} },
        style: {}
      };
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
  return {
    hidden: false,
    disabled: false,
    value: '',
    scrollHeight: 40,
    innerHTML: '',
    textContent: '',
    className: '',
    attributes: new Map(),
    classList: new FakeClassList(),
    style: {},
    focus() {
      this.focused = true;
    },
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

test('AiPanel apply callback receives serialized canvas flow after deserialize', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const applied = [];
  const serializedFlow = { operators: [{ id: 'applied-canvas' }], connections: [] };
  const panel = Object.create(AiPanel.prototype);
  panel.flowCanvas = {
    deserialize(flow) {
      this.lastDeserialized = flow;
    },
    serialize() {
      return serializedFlow;
    },
    getFlowRevision() {
      return 1;
    }
  };
  panel.options = {
    onApplied(flow) {
      applied.push(flow);
    },
    showToast() {}
  };
  panel.currentResult = { flow: { operators: [] } };
  panel.container = { querySelector() { return null; } };
  panel._markCurrentResultAppliedToCanvas = () => {};
  panel._syncPendingParameterDrafts = () => {};
  panel._renderFollowupChecklist = () => {};
  panel._renderParameterDraftEditor = () => {};
  panel._setWorkbenchState = () => {};
  panel._setResultStatusNote = () => {};

  const incomingFlow = { operators: [{ id: 'incoming' }], connections: [] };
  panel._executeApplyFlow(incomingFlow);

  assert.equal(panel.flowCanvas.lastDeserialized, incomingFlow);
  assert.equal(applied.length, 1);
  assert.equal(applied[0], serializedFlow);
});

test('AiPanel undo notifies canvas flow change with restored snapshot', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const canvasChanges = [];
  const restoredFlow = { operators: [{ id: 'restored' }], connections: [] };
  const panel = Object.create(AiPanel.prototype);
  panel.flowCanvas = {
    deserialize(flow) {
      this.lastDeserialized = flow;
    },
    serialize() {
      return restoredFlow;
    },
    getFlowRevision() {
      return 1;
    }
  };
  panel.options = {
    onCanvasChanged(payload) {
      canvasChanges.push(payload);
    }
  };
  panel._preApplySnapshot = restoredFlow;
  panel._preApplySnapshotVersion = 1;
  panel._preApplyCanvasRevision = 0;
  panel._setResultStatusNote = () => {};
  panel._setWorkbenchState = () => {};
  panel._addMessage = () => {};

  panel._undoApply();

  assert.equal(panel.flowCanvas.lastDeserialized, restoredFlow);
  assert.equal(canvasChanges.length, 1);
  assert.equal(canvasChanges[0].action, 'undo-apply');
  assert.equal(canvasChanges[0].flow, restoredFlow);
});

test('AiPanel runtime strip renders clarification state and missing-field counts', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const runtime = createFakeElement();
  const panel = Object.create(AiPanel.prototype);
  panel.container = createContainer({ '#ai-agent-runtime': runtime });
  panel._lastAgentRuntime = null;

  panel._renderAgentRuntime({
    turnIntent: 'new_flow',
    interactionState: 'clarifying',
    routerConfidence: 'high',
    blockingClarificationFields: ['scene', 'object_type'],
    nonBlockingMissingFields: ['model_path', 'roi'],
    requirementBrief: {
      clarificationQuestions: [
        { field: 'scene', question: '请确认场景。', required: true }
      ]
    }
  });

  assert.equal(runtime.hidden, false);
  assert.match(runtime.className, /is-clarifying/);
  assert.match(runtime.innerHTML, /待澄清/);
  assert.match(runtime.innerHTML, /2 项阻断澄清/);
  assert.match(runtime.innerHTML, /意图[\s\S]*新建流程/);
  assert.match(runtime.innerHTML, /置信度[\s\S]*高/);
  assert.match(runtime.innerHTML, /阻断[\s\S]*2/);
  assert.match(runtime.innerHTML, /待补[\s\S]*2/);
  assert.match(runtime.innerHTML, /下一步：先回答 2 个阻断问题/);
});

test('AiPanel runtime next action guides apply, review, and manual retry states', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const panel = Object.create(AiPanel.prototype);

  assert.equal(
    panel._buildAgentNextAction({
      turnIntent: 'review_pending_parameters',
      interactionState: 'reviewing_parameters',
      pendingCount: 2,
      hasFlow: true
    }),
    '下一步：补齐 2 组待确认参数，再执行统一确认。'
  );

  assert.equal(
    panel._buildAgentNextAction({
      turnIntent: 'manual_retry_repair',
      interactionState: 'manual_retry',
      manualRetryRequired: true
    }),
    '下一步：检查已回填的修复草稿并发送，只进入修复链路，不重新澄清需求。'
  );

  assert.equal(
    panel._buildAgentNextAction({
      turnIntent: 'new_flow',
      interactionState: 'completed',
      hasFlow: true
    }),
    '下一步：确认方案后应用到流程草稿，或继续输入微调需求。'
  );
});

test('AiPanel request mode inference separates explicit new flow from current-flow edits', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const panel = Object.create(AiPanel.prototype);
  const currentFlow = { operators: [{ id: 'op_1' }], connections: [] };

  assert.equal(
    panel._resolveGenerateRequestMode('', '新增一个缺陷检测流程', currentFlow),
    'new'
  );
  assert.equal(
    panel._resolveGenerateRequestMode('', '新建一个过滤算子', currentFlow),
    'modify'
  );
  assert.equal(
    panel._resolveGenerateRequestMode('', '新增一个过滤算子到当前流程', currentFlow),
    'modify'
  );
  assert.equal(
    panel._resolveGenerateRequestMode('', '重新调整当前流程阈值', currentFlow),
    'modify'
  );
  assert.equal(
    panel._resolveGenerateRequestMode('', '新增一个缺陷检测流程', null),
    'auto'
  );
});

test('AiPanel clarification option selection builds one managed answer draft', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const input = createFakeElement();
  const sendButton = createFakeElement();
  const sceneButton = createFakeElement();
  sceneButton.setAttribute('data-clarification-field', 'scene');
  sceneButton.setAttribute('data-clarification-value', '外观缺陷');
  const objectButton = createFakeElement();
  objectButton.setAttribute('data-clarification-field', 'object_type');
  objectButton.setAttribute('data-clarification-value', '金属件');

  const panel = Object.create(AiPanel.prototype);
  panel.container = createContainer(
    {
      '#ai-input': input,
      '#ai-btn-send-clarification': sendButton
    },
    {
      '[data-clarification-field][data-clarification-value]': [sceneButton, objectButton]
    }
  );
  panel.isGenerating = false;
  panel._clarificationSelectionDraft = {};
  panel._lastClarificationDraftText = '';
  panel._addMessage = () => {};

  panel._handleClarificationOptionSelection(sceneButton);

  assert.match(input.value, /澄清回答：\n场景类型：外观缺陷/);
  assert.equal(sendButton.disabled, false);
  assert.equal(sendButton.getAttribute('aria-disabled'), 'false');
  assert.equal(sceneButton.getAttribute('aria-pressed'), 'true');

  panel._handleClarificationOptionSelection(objectButton);

  assert.match(input.value, /场景类型：外观缺陷/);
  assert.match(input.value, /检测对象：金属件/);
  assert.equal((input.value.match(/澄清回答：/g) || []).length, 1);
});

test('AiPanel apply button is disabled until a generated flow is available', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const button = createFakeElement();
  const panel = Object.create(AiPanel.prototype);
  panel.container = createContainer({ '#ai-btn-apply': button });
  panel.isGenerating = false;
  panel.currentResult = null;
  panel.currentResultVersion = 0;
  panel.appliedResultVersion = 0;

  panel._updateApplyButtonState();

  assert.equal(button.disabled, true);
  assert.equal(button.getAttribute('aria-disabled'), 'true');
  assert.match(button.innerHTML, /暂无可应用方案/);

  panel.currentResult = { flow: { operators: [], connections: [] } };
  panel.currentResultVersion = 1;
  panel.appliedResultVersion = 0;

  panel._updateApplyButtonState();

  assert.equal(button.disabled, false);
  assert.equal(button.getAttribute('aria-disabled'), 'false');
  assert.match(button.innerHTML, /应用到当前流程草稿/);

  panel.appliedResultVersion = 1;

  panel._updateApplyButtonState();

  assert.equal(button.disabled, true);
  assert.equal(button.getAttribute('aria-disabled'), 'true');
  assert.match(button.innerHTML, /已应用到流程草稿/);
});

test('AiPanel apply preview risk summary includes unresolved launch items and connection diffs', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const panel = Object.create(AiPanel.prototype);
  const result = {
    pendingParameters: [
      { operatorId: 'op_1', parameterNames: ['ModelPath', 'Threshold'] }
    ],
    missingResources: [
      { resourceType: 'model', resourceKey: 'detector.onnx', description: '缺少检测模型文件' }
    ],
    nonBlockingMissingFields: ['roi', 'plc_address']
  };

  const risk = panel._buildApplyRiskSummary(result);
  const changes = panel._getApplyPreviewChangeCount({
    added: [],
    removed: [],
    modified: [],
    addedConnections: [{ sourceId: 'a', targetId: 'b' }],
    removedConnections: []
  });

  assert.equal(risk.hasWarnings, true);
  assert.equal(risk.totalCount, 4);
  assert.equal(risk.pending.length, 1);
  assert.equal(risk.missing.length, 1);
  assert.deepEqual(risk.nonBlockingFields, ['roi', 'plc_address']);
  assert.equal(changes, 1);

  const html = panel._renderApplyRiskSummary(risk);
  assert.match(html, /应用前检查/);
  assert.match(html, /缺少检测模型文件/);
  assert.match(html, /ROI范围/);
  assert.match(html, /PLC地址/);
});

test('AiPanel apply diff reports display name, removed parameters, and concrete connection changes', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const panel = Object.create(AiPanel.prototype);
  const diff = panel._computeFlowDiff(
    {
      operators: [
        {
          id: 'op_1',
          type: 'Filtering',
          name: 'Filtering',
          parameters: { KernelSize: '5', Mode: 'Fast' }
        }
      ],
      connections: [
        {
          sourceOperatorId: 'op_1',
          sourcePortId: 'Image',
          targetOperatorId: 'op_2',
          targetPortId: 'Image'
        }
      ]
    },
    {
      operators: [
        {
          id: 'op_1',
          operatorType: 'Filtering',
          displayName: '滤波',
          parameters: { KernelSize: '7' }
        }
      ],
      connections: [
        {
          sourceOperatorId: 'op_1',
          sourcePortId: 'Image',
          targetOperatorId: 'op_3',
          targetPortId: 'Image'
        }
      ]
    }
  );

  assert.equal(diff.added.length, 0);
  assert.equal(diff.removed.length, 0);
  assert.equal(diff.modified.length, 1);
  assert.deepEqual(
    diff.modified[0].changes.map(change => change.name),
    ['displayName', 'KernelSize', 'Mode']
  );
  assert.equal(diff.addedConnections.length, 1);
  assert.equal(diff.removedConnections.length, 1);
  assert.equal(
    panel._formatConnectionPreview(diff.addedConnections[0]),
    'op_1.Image -> op_3.Image'
  );
});
