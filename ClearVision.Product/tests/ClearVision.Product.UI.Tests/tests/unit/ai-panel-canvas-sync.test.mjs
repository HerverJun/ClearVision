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
    dataset: {},
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
    removeAttribute(name) {
      this.attributes.delete(name);
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
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
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

test('AI generated canvas titles stay friendly through apply, property panel, and preview results', async () => {
  installDom();
  const friendlyName = '图像采集';
  const { AiPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );
  const { PropertyPanelCapabilityOwner } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanelCapabilityOwner.mjs'
  );
  const { buildOperatorResultViewModel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/operatorResultViewModel.mjs'
  );

  const generatedFlow = {
    operators: [
      {
        id: 'node-1',
        type: 'ImageAcquisition',
        name: friendlyName,
        title: friendlyName,
        displayName: friendlyName,
        parameters: []
      }
    ],
    connections: []
  };
  const panel = Object.create(AiPanel.prototype);
  panel.flowCanvas = {
    deserialize(flow) {
      this.lastDeserialized = flow;
    },
    serialize() {
      return this.lastDeserialized;
    },
    getFlowRevision() {
      return 1;
    }
  };
  panel.options = { showToast() {} };
  panel.currentResult = { flow: generatedFlow };
  panel.container = { querySelector() { return null; } };
  panel._markCurrentResultAppliedToCanvas = () => {};
  panel._syncPendingParameterDrafts = () => {};
  panel._renderFollowupChecklist = () => {};
  panel._renderParameterDraftEditor = () => {};
  panel._setWorkbenchState = () => {};
  panel._setResultStatusNote = () => {};

  panel._executeApplyFlow(generatedFlow);
  const appliedOperator = panel.flowCanvas.lastDeserialized.operators[0];

  assert.equal(appliedOperator.title, friendlyName);
  assert.equal(appliedOperator.name, friendlyName);
  assert.doesNotMatch(JSON.stringify(appliedOperator), /\bop_1\b/);

  const propertyContainer = createFakeElement();
  const propertyAdapter = {
    subscribeSelectedNode() { return () => {}; },
    subscribeFlowChanges() { return () => {}; },
    getSelectedConnectionSnapshot() { return null; }
  };
  const owner = new PropertyPanelCapabilityOwner(propertyContainer, {
    propertyAdapter,
    previewResourcesEnabled: false
  });
  owner.handleSelectedNodeChanged(appliedOperator);

  assert.match(propertyContainer.innerHTML, new RegExp(friendlyName));
  assert.doesNotMatch(propertyContainer.innerHTML, /\bop_1\b/);

  const viewModel = buildOperatorResultViewModel(
    appliedOperator,
    { status: 'success', activeNodeId: 'node-1' },
    { nodes: [appliedOperator] }
  );

  assert.equal(viewModel.operatorName, friendlyName);
  assert.equal(viewModel.nodeResults[0].title, friendlyName);
  assert.doesNotMatch(JSON.stringify({
    operatorName: viewModel.operatorName,
    nodeResults: viewModel.nodeResults
  }), /\bop_1\b/);
});

test('AI draft normalization ignores leaked tempId labels', async () => {
  installDom();
  const friendlyName = '\u56fe\u50cf\u91c7\u96c6';
  const { AiPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const panel = Object.create(AiPanel.prototype);
  const normalized = panel._normalizeWorkflowDraftForCanvas({
    operators: [
      {
        tempId: 'op_1',
        operatorType: 'ImageAcquisition',
        name: 'op_1',
        displayName: 'op_1',
        title: 'op_1',
        parameters: []
      }
    ],
    connections: []
  });

  const operator = normalized.operators[0];
  assert.equal(operator.name, friendlyName);
  assert.equal(operator.title, friendlyName);
  assert.equal(operator.displayName, friendlyName);
  assert.equal(operator.id, 'op_1');
  assert.equal(operator.metadata.agentTempId, 'op_1');
  assert.doesNotMatch(JSON.stringify({
    name: operator.name,
    title: operator.title,
    displayName: operator.displayName
  }), /\bop_1\b/);

  operator.name = '用户自定义名称';
  operator.title = '用户自定义名称';
  operator.displayName = '用户自定义名称';
  assert.equal(operator.metadata.agentTempId, 'op_1');

  const normalizedChain = panel._normalizeWorkflowDraftForCanvas({
    operators: [
      { tempId: 'op_1', operatorType: 'ImageAcquisition', name: 'op_1', parameters: [] },
      { tempId: 'op_2', operatorType: 'ROIManager', name: 'op_2', parameters: [] },
      { tempId: 'op_3', operatorType: 'Threshold', name: 'op_3', parameters: [] },
      { tempId: 'op_4', operatorType: 'BinaryImageToRegion', name: 'op_4', parameters: [] },
      { tempId: 'op_5', operatorType: 'BlobAnalysis', name: 'op_5', parameters: [] },
      { tempId: 'op_6', operatorType: 'ConditionJudge', name: 'op_6', parameters: [] },
      { tempId: 'op_7', operatorType: 'ResultOutput', name: 'op_7', parameters: [] }
    ],
    connections: []
  });

  assert.deepEqual(
    normalizedChain.operators.map(item => item.name),
    ['图像采集', 'ROI裁剪与掩膜', '全局阈值处理', '二值图转区域', 'Blob分析', '条件判断', '结果输出']
  );
  assert.deepEqual(
    normalizedChain.operators.map(item => item.metadata.agentTempId),
    ['op_1', 'op_2', 'op_3', 'op_4', 'op_5', 'op_6', 'op_7']
  );
  assert.doesNotMatch(
    JSON.stringify(normalizedChain.operators.map(item => item.name)),
    /ROIManager|Threshold|BinaryImageToRegion|ConditionJudge|\bop_\d+\b/
  );
});

test('AI draft normalization recovers legacy numeric operator enum values', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const panel = Object.create(AiPanel.prototype);
  const normalized = panel._normalizeWorkflowDraftForCanvas({
    operators: [
      { id: 'node-1', type: 0, name: '\u56fe\u50cf\u91c7\u96c6', parameters: [] },
      {
        id: 'node-2',
        type: 7,
        name: '\u6a21\u677f\u5339\u914d',
        inputPorts: [{ id: 'node-2-in-image', name: 'Image', dataType: 0 }],
        outputPorts: [
          { id: 'node-2-out-position', name: 'Position', dataType: 5 },
          { id: 'node-2-out-score', name: 'Score', dataType: 2 },
          { id: 'node-2-out-match', name: 'IsMatch', dataType: 3 }
        ],
        parameters: []
      },
      { id: 'node-3', type: 60, name: '\u7ed3\u679c\u5224\u5b9a', parameters: [] },
      { id: 'node-4', type: 11, name: '\u7ed3\u679c\u8f93\u51fa', parameters: [] }
    ],
    connections: []
  });

  assert.deepEqual(
    normalized.operators.map(item => item.type),
    ['ImageAcquisition', 'TemplateMatching', 'ResultJudgment', 'ResultOutput']
  );
  assert.deepEqual(
    normalized.operators.map(item => item.name),
    ['\u56fe\u50cf\u91c7\u96c6', '\u6a21\u677f\u5339\u914d', '\u7ed3\u679c\u5224\u5b9a', '\u7ed3\u679c\u8f93\u51fa']
  );
  assert.deepEqual(
    normalized.operators[1].outputPorts.map(port => port.dataType),
    ['Point', 'Float', 'Boolean']
  );
});

test('AiPanel undo notifies canvas flow change with restored snapshot', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
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
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
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
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const panel = Object.create(AiPanel.prototype);

  assert.equal(
    panel._buildAgentNextAction({
      turnIntent: 'review_pending_parameters',
      interactionState: 'reviewing_parameters',
      pendingCount: 2,
      hasFlow: true
    }),
    '下一步：补齐 2 组待确认参数，再确认人工参数。'
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
    '下一步：确认方案后应用到画布，或继续输入微调需求。'
  );
});

test('AiPanel runtime infers terminal states when interactionState is missing', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const cases = [
    {
      payload: { success: false, status: 'cancelled', failureType: 'user_cancelled', turnIntent: 'new_flow' },
      className: /is-cancelled/,
      label: /已取消/
    },
    {
      payload: { success: false, status: 'timed_out', failureType: 'timeout', turnIntent: 'new_flow' },
      className: /is-timed_out/,
      label: /请求超时/
    },
    {
      payload: { success: false, status: 'failed', failureType: 'system_error', turnIntent: 'new_flow' },
      className: /is-failed/,
      label: /失败/
    },
    {
      payload: { success: false, status: 'manual_retry_required', turnIntent: 'manual_retry_repair', manualRetry: { required: true } },
      className: /is-manual_retry/,
      label: /修复中/
    }
  ];

  for (const item of cases) {
    const runtime = createFakeElement();
    const panel = Object.create(AiPanel.prototype);
    panel.container = createContainer({ '#ai-agent-runtime': runtime });
    panel._lastAgentRuntime = null;

    panel._renderAgentRuntime(item.payload);

    assert.equal(runtime.hidden, false);
    assert.match(runtime.className, item.className);
    assert.match(runtime.innerHTML, item.label);
    assert.doesNotMatch(runtime.innerHTML, /生成中/);
  }
});

test('AiPanel request mode inference separates explicit new flow from current-flow edits', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
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

test('AiPanel unified clarification selection writes one canonical optimistic answer', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
  );

  const input = createFakeElement();
  const panel = Object.create(AiPanel.prototype);
  panel.container = createContainer({ '#ai-input': input });
  panel.sessionId = 'session-canonical-answer';
  panel.isGenerating = false;
  panel._addMessage = () => {};
  panel._renderPlanWorkspace = () => {};
  panel._renderAgentWorkspaceOverview = () => {};
  panel._requestBackendPlanReadinessPreview = () => new Promise(() => {});
  panel._ensureAgentWorkspaceState();
  panel.pendingVisionPlan = {
    planId: 'plan-canonical-answer',
    planHash: 'sha256:canonical-answer',
    questions: [{
      id: 'object_type',
      field: 'inspection_object',
      title: '检测对象',
      options: [
        { value: 'metal_part', label: '金属件', recommended: true, answerEffect: 'resolve_field' },
        { value: 'plastic_part', label: '塑料件', recommended: false, answerEffect: 'resolve_field' }
      ]
    }],
    buildReadiness: {
      canBuild: false,
      blockers: [{ id: 'hard_requirement:inspection_object_missing', category: 'hard_requirement', field: 'inspection_object', questionId: 'object_type', blocksBuild: true }],
      resolvedFields: [],
      remainingFields: ['inspection_object'],
      primaryMessage: '检测对象待确认',
      contractVersion: 'v2'
    }
  };

  panel._selectPlanQuestionOption('object_type', 'metal_part');

  assert.equal(panel.planQuestionAnswers.inspection_object.value, 'metal_part');
  assert.equal(panel.planQuestionAnswers.inspection_object.origin, 'explicit_user_selection');
  assert.equal(panel.agentWorkspaceState.projection.optimisticAnswers.length, 1);
  assert.equal(panel.agentWorkspaceState.projection.readiness.canBuild, false);
  assert.equal(input.value, '');
});

test('AiPanel apply button is disabled until a generated flow is available', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
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

  assert.equal(button.disabled, true);
  assert.equal(button.getAttribute('aria-disabled'), 'true');
  assert.match(button.innerHTML, /暂无可应用方案/);

  panel.currentResult = { flow: { operators: [{ id: 'op_1', type: 'ImageAcquisition' }], connections: [] } };
  panel.currentResultVersion = 2;
  panel.appliedResultVersion = 0;

  panel._updateApplyButtonState();

  assert.equal(button.disabled, false);
  assert.equal(button.getAttribute('aria-disabled'), 'false');
  assert.match(button.innerHTML, /应用到画布/);

  panel.appliedResultVersion = 2;

  panel._updateApplyButtonState();

  assert.equal(button.disabled, true);
  assert.equal(button.getAttribute('aria-disabled'), 'true');
  assert.match(button.innerHTML, /已应用到画布/);
});

test('AiPanel apply preview risk summary includes unresolved launch items and connection diffs', async () => {
  installDom();
  const { AiPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
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
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanel.js'
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
