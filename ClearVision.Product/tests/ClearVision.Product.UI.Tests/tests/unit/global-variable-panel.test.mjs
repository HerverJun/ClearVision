import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

function createElementStub() {
  return {
    innerHTML: '',
    dataset: {},
    value: '',
    checked: false,
    disabled: false,
    className: '',
    style: {},
    appendChild() {},
    remove() {},
    focus() {},
    addEventListener() {},
    querySelector() { return null; },
    querySelectorAll() { return []; },
    closest() { return null; },
    setAttribute() {}
  };
}

function installDom() {
  const container = createElementStub();
  const body = createElementStub();
  body.appended = [];
  body.appendChild = (element) => {
    body.appended.push(element);
    return element;
  };

  global.window = {
    location: { protocol: 'http:', hostname: 'localhost', port: '5000' },
    localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
    setTimeout(callback) { callback?.(); return 1; },
    clearTimeout() {},
    prompt() { throw new Error('window.prompt must not be used'); },
    alert() { throw new Error('window.alert must not be used'); },
    confirm() { throw new Error('window.confirm must not be used'); }
  };
  global.localStorage = global.window.localStorage;
  global.document = {
    title: '',
    body,
    head: createElementStub(),
    createElement: createElementStub,
    addEventListener() {},
    getElementById(id) {
      return id === 'global-variables-root' ? container : null;
    },
    querySelector() { return null; },
    querySelectorAll() { return []; }
  };
  Object.defineProperty(global, 'crypto', {
    configurable: true,
    value: {
      randomUUID() {
        global.__uuidCounter = (global.__uuidCounter ?? 0) + 1;
        return `00000000-0000-0000-0000-${String(global.__uuidCounter).padStart(12, '0')}`;
      }
    }
  });
  global.__uuidCounter = 0;
  return { container, body };
}

function jsonResponse(payload, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { 'content-type': 'application/json' }
  });
}

function createProject() {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Project',
    flow: {
      operators: [
        {
          id: 'op-counter',
          name: '计数算子',
          outputPorts: [
            { id: 'out-count', name: '数量', dataType: 'Integer' },
            { id: 'out-image', name: '图像', dataType: 'Image' }
          ],
          parameters: [
            { id: 'param-threshold', name: 'Threshold', displayName: '阈值', dataType: 'double' }
          ]
        }
      ]
    },
    globalVariables: {
      schemaVersion: '1.0',
      variables: [
        {
          id: 'var-count',
          name: 'judge.expected_count',
          displayName: '期望数量',
          description: '用于判定',
          valueType: 'Int64',
          initialValue: 4,
          min: 0,
          max: 10,
          manualWriteAllowed: true,
          includeInResultMetadata: true,
          order: 1
        }
      ],
      sourceBindings: [],
      targetBindings: []
    }
  };
}

test('global variable drafts validate type, range, duplicate names and serialize complete fields', async () => {
  installDom();
  const store = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariableStore.js');
  const schema = createProject().globalVariables;
  const draft = store.createVariableDraft(null, 2);
  Object.assign(draft, {
    name: 'judge.expected_count',
    displayName: '重复名称',
    description: 'desc',
    valueType: 'Int64',
    initialValueText: '11',
    minText: '12',
    maxText: '10',
    manualWriteAllowed: false,
    includeInResultMetadata: true,
    order: 5
  });

  const duplicate = store.validateVariableDraft(draft, schema);
  assert.match(duplicate.name, /已存在/);
  assert.match(duplicate.max, /最大值/);
  assert.match(duplicate.initialValue, /初始值不能/);

  draft.name = 'judge.actual_count';
  draft.initialValueText = '8';
  draft.minText = '0';
  draft.maxText = '10';
  const result = store.serializeVariableDraft(draft, schema);

  assert.equal(result.ok, true);
  assert.equal(result.variable.id, draft.id);
  assert.equal(result.variable.name, 'judge.actual_count');
  assert.equal(result.variable.displayName, '重复名称');
  assert.equal(result.variable.description, 'desc');
  assert.equal(result.variable.valueType, 'Int64');
  assert.equal(result.variable.initialValue, 8);
  assert.equal(result.variable.min, 0);
  assert.equal(result.variable.max, 10);
  assert.equal(result.variable.manualWriteAllowed, false);
  assert.equal(result.variable.includeInResultMetadata, true);
  assert.equal(result.variable.order, 5);
});

test('global variable panel saves edited schema, keeps id and preserves dirty draft after backend failure', async () => {
  installDom();
  const { default: projectManager } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectManager.js');
  const { default: GlobalVariablePanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablePanel.js');
  const project = createProject();
  projectManager.currentProject = project;
  const savedBodies = [];
  global.fetch = async (url, options = {}) => {
    if (String(url).includes('/global-variables') && options.method === 'PUT') {
      const body = JSON.parse(options.body);
      savedBodies.push(body);
      return jsonResponse(body);
    }
    if (String(url).includes('/global-variable-values')) {
      return jsonResponse([]);
    }
    return jsonResponse({});
  };

  const panel = new GlobalVariablePanel('global-variables-root', {
    requestChoice: () => 'continue',
    showToast() {}
  });
  panel.project = project;
  panel.schema = project.globalVariables;
  panel.selectedVariableId = 'var-count';
  panel.draft = {
    ...project.globalVariables.variables[0],
    initialValueText: '6',
    minText: '0',
    maxText: '20',
    displayName: '目标数量'
  };
  panel.renderDialog = () => {};
  panel.render = () => {};

  const saved = await panel.save();
  assert.equal(saved, true);
  assert.equal(savedBodies[0].variables[0].id, 'var-count');
  assert.equal(savedBodies[0].variables[0].displayName, '目标数量');
  assert.equal(savedBodies[0].variables[0].initialValue, 6);
  assert.equal(projectManager.currentProject.globalVariables.variables[0].displayName, '目标数量');

  global.fetch = async (url, options = {}) => {
    if (String(url).includes('/global-variables') && options.method === 'PUT') {
      return jsonResponse({ Error: 'server says no' }, 400);
    }
    return jsonResponse([]);
  };
  panel.draft.displayName = '失败后草稿';
  panel.dirty = true;
  const failed = await panel.save();

  assert.equal(failed, false);
  assert.equal(panel.draft.displayName, '失败后草稿');
  assert.equal(panel.dirty, true);
  assert.match(panel.errorMessage, /server says no/);
});

test('delete is blocked with source or target references and exposes binding details', async () => {
  installDom();
  const { default: GlobalVariablePanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablePanel.js');
  const project = createProject();
  project.globalVariables.targetBindings.push({
    id: 'bind-target',
    variableId: 'var-count',
    operatorId: 'op-counter',
    parameterId: 'param-threshold',
    operatorName: '计数算子',
    parameterName: '阈值'
  });
  const panel = new GlobalVariablePanel('global-variables-root', { showToast() {} });
  panel.project = project;
  panel.schema = project.globalVariables;
  panel.selectedVariableId = 'var-count';
  panel.renderDialog = () => {};

  const deleted = await panel.deleteSelectedVariable();

  assert.equal(deleted, false);
  assert.equal(panel.schema.variables.length, 1);
  assert.match(panel.errorMessage, /仍被引用/);
  assert.deepEqual(panel.getVariableReferences('var-count'), ['目标：计数算子.阈值']);
});

test('source and target bindings use compatible filtering and locate canvas nodes', async () => {
  installDom();
  const { default: GlobalVariablePanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablePanel.js');
  const serviceRegistry = (await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/app/serviceRegistry.js')).default;
  const project = createProject();
  project.globalVariables.sourceBindings.push({
    id: 'bind-source',
    variableId: 'var-count',
    operatorId: 'op-counter',
    outputPortId: 'out-image',
    operatorName: '计数算子',
    outputPortName: '图像'
  });
  const panel = new GlobalVariablePanel('global-variables-root', { showToast() {} });
  panel.project = project;
  panel.schema = project.globalVariables;
  panel.selectedVariableId = 'var-count';

  const compatible = panel.getFlowOutputs().filter(output =>
    output.outputPortId === 'out-count' || output.outputPortId === 'out-image');
  assert.equal(compatible.length, 2);
  assert.deepEqual(panel.getIncompatibleBindings(project.globalVariables.variables[0], 'Boolean'), ['来源 计数算子.图像']);

  let selectedNode = null;
  serviceRegistry.register('flowCanvas', {
    nodes: new Map([['op-counter', { id: 'op-counter' }]]),
    onNodeSelected(node) { selectedNode = node.id; },
    invalidate() {}
  });
  assert.equal(panel.locateOperator('op-counter'), true);
  assert.equal(selectedNode, 'op-counter');
});

test('running states disable schema and value mutations and 409 refreshes values without success', async () => {
  installDom();
  const { default: GlobalVariablePanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablePanel.js');
  const project = createProject();
  let getValuesCalled = 0;
  global.fetch = async (url, options = {}) => {
    if (String(url).includes('/global-variables') && options.method === 'PUT') {
      return jsonResponse({ Error: 'Project is currently running.' }, 409);
    }
    if (String(url).includes('/global-variable-values')) {
      getValuesCalled += 1;
      return jsonResponse([{ variableId: 'var-count', value: 4, version: 3 }]);
    }
    return jsonResponse({});
  };

  const panel = new GlobalVariablePanel('global-variables-root', {
    getRuntimeState: () => ({ status: 'Running' }),
    showToast() {}
  });
  panel.project = project;
  panel.schema = project.globalVariables;
  panel.selectedVariableId = 'var-count';
  panel.draft = { ...project.globalVariables.variables[0], initialValueText: '7', minText: '0', maxText: '10' };
  panel.render = () => {};
  panel.renderDialog = () => {};

  assert.equal(panel.isRuntimeLocked(), true);
  assert.match(panel.renderEditorHtml(), /工程运行中，变量结构和值不可修改/);

  panel.options.getRuntimeState = () => ({ status: 'Idle' });
  const saved = await panel.save();
  assert.equal(saved, false);
  assert.match(panel.errorMessage, /工程正在运行/);
  assert.equal(getValuesCalled, 1);
});

test('stale value refresh from a previous project cannot overwrite the active project', async () => {
  installDom();
  const { default: GlobalVariablePanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablePanel.js');
  let resolveFirst;
  global.fetch = (url) => {
    const urlText = String(url);
    if (urlText.includes('/project-1/')) {
      return new Promise(resolve => {
        resolveFirst = () => resolve(jsonResponse([{ variableId: 'old', value: 1 }]));
      });
    }
    return Promise.resolve(jsonResponse([{ variableId: 'new', value: 2 }]));
  };

  const panel = new GlobalVariablePanel('global-variables-root', { showToast() {} });
  panel.render = () => {};
  panel.project = { id: 'project-1', globalVariables: { variables: [] } };
  panel.requestSerial = 1;
  const first = panel.refreshValues({ requestId: 1, render: false });
  panel.project = { id: 'project-2', globalVariables: { variables: [] } };
  panel.requestSerial = 2;
  await panel.refreshValues({ requestId: 2, render: false });
  resolveFirst();
  await first;

  assert.deepEqual(panel.values, [{ variableId: 'new', value: 2, version: 0, updatedBy: '', updatedAtUtc: '', runId: '', operatorId: '', operatorName: '' }]);
});

test('property panel binding control is Chinese, filters incompatible variables and syncs schema immediately', async () => {
  installDom();
  const { PropertyPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');
  const { default: projectManager } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectManager.js');
  const serviceRegistry = (await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/app/serviceRegistry.js')).default;
  const project = createProject();
  project.globalVariables.variables.push({
    id: 'var-flag',
    name: 'judge.flag',
    displayName: '标志',
    valueType: 'Boolean',
    initialValue: false
  });
  projectManager.currentProject = project;

  const panel = Object.create(PropertyPanel.prototype);
  panel.currentOperator = project.flow.operators[0];
  panel.escapeHtml = PropertyPanel.prototype.escapeHtml;
  panel.escapeAttribute = PropertyPanel.prototype.escapeAttribute;
  const html = panel.renderGlobalVariableBindingControl(project.flow.operators[0].parameters[0]);

  assert.match(html, /参数来源/);
  assert.match(html, /固定值/);
  assert.match(html, /期望数量/);
  assert.doesNotMatch(html, /标志/);

  let externalSchema = null;
  serviceRegistry.register('globalVariablePanel', {
    setSchemaFromExternal(schema) {
      externalSchema = schema;
    }
  });
  global.document.getElementById = (id) => {
    if (id !== 'property-form') return null;
    return {
      querySelectorAll() {
        return [{
          dataset: { parameterId: 'param-threshold', parameterName: 'Threshold' },
          value: 'var-count'
        }];
      }
    };
  };
  panel.syncGlobalVariableTargetBindings();

  assert.equal(projectManager.currentProject.globalVariables.targetBindings.length, 1);
  assert.equal(projectManager.currentProject.globalVariables.targetBindings[0].variableId, 'var-count');
  assert.equal(externalSchema.targetBindings.length, 1);
});

test('global variable UI has no browser prompt, alert or confirm calls and Station text is Chinese', () => {
  const root = path.resolve(process.cwd(), '../../..');
  const panelSource = fs.readFileSync(path.join(root, 'ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablePanel.js'), 'utf8');
  const stationSource = fs.readFileSync(path.join(root, 'ClearVision.Product/src/ClearVision.Product.Station/MainForm.cs'), 'utf8');

  assert.doesNotMatch(panelSource, /window\.(prompt|alert|confirm)|\b(prompt|alert|confirm)\(/);
  assert.match(stationSource, /全局变量/);
  assert.match(stationSource, /Columns\.Add\("名称"/);
  assert.match(stationSource, /ConfigureButton\(_editProjectVariableButton, "编辑"/);
  assert.doesNotMatch(stationSource, /Global Variables|Edit global variable failed|Reset global variable failed/);
});
